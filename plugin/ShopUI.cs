using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using MonoMod.RuntimeDetour;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Net.Sockets;
using TerrariaApi.Server;
using TShockAPI;

namespace TShockData
{
	/// <summary>
	/// ShopUI — 虚拟旅商商店（已并入 TSWeb 主插件，内容由 ShopUIConfigManager 配置驱动，前端可编辑）
	///
	/// 交互（全部基于 1.4.5 协议源码实证）：
	///   1. 手持召唤物（默认锡斧 3500，可配置）按下使用键（挥动）→ 在脚底召唤虚拟旅商（NPC 368）
	///   2. 仅自己可见：OTAPI.Hooks.NetMessage.SendBytes 出站钩子（Omni Ghost 同款），
	///      把虚拟旅商的 23 号包（SyncNPC）/ 40 号包（SyncTalkNPC）对非所属玩家
	///      args.Result = Cancel 取消发送 → 其他玩家客户端无实体 → 天然不可见
	///   3. 旅商存在时再次挥动召唤物 → 拉回身边（改 position + 定向广播 23）
	///   4. 关闭对话（客户端上报 talkNPC=-1）或下线 → 自动移除（life=0 + active=false + 定向 23）
	///   5. 商店系统（40 槽 10×4）：
	///      - 槽 0-35：当前商店商品（配置 shops[]，价格/数量/解锁条件均可配置）
	///      - 槽 36-39：雕像控件（配置 statueControls[]，雕像 ID/跳转目标/数量均可配置，价格 0）
	///      - 点击雕像（购买触发）→ PlayerSlot 钩子拦截 → 回滚清空手持/背包雕像
	///        → 跳到 statueControls[i].targetShopIndex 对应商店 → 立即 104 刷新
	///      - 商店刷新机制：72 号包只能写物品 ID（无法定价/置空），104 号包能定价/置空
	///        但只在客户端 Main.npcShop>0（商店已打开）时应用 → 72 全空快照（打开即空白页），
	///        点开旅商对话后持续高频刷新（每 0.2s 重发 104）填充商品+雕像，退出旅商/购买雕像才停。
	///
	/// 已确认的限制：40 号包只同步 talkNPC 状态，不填充客户端对话文本（npcChatText 由
	/// 客户端点击 NPC 时 GetChat() 生成）→ 服务端无法强制弹出对话面板，玩家需点击一次旅商。
	/// ⚠️ 因此绝不主动发 40 号包/SetTalkNPC（会把 talkNPC 卡住产生虚假对话窗口）。
	/// </summary>
	public static class ShopUICore
	{
		private const int TravelingMerchantType = NPCID.TravellingMerchant; // 368

		// ═══════════ 配置（由 ShopUIConfigManager 驱动，ReloadConfig 热重载） ═══════════

		// 40 格布局（10×4）：控件（目录）优先级高于商品——先占尾部格子，剩余格才是商品区；商品/控件数量均不限，超出截断

		private static bool _enabled = true;
		private static int _summonItemId = ItemID.TinAxe; // 3500

		// ═══════════ 运行时状态 ═══════════

		/// <summary>whoAmI → 虚拟旅商 NPC 索引（每玩家同时最多一个）</summary>
		private static readonly Dictionary<int, int> _active = new Dictionary<int, int>();
		/// <summary>whoAmI → 当前商店索引（shops[] 下标）</summary>
		private static readonly Dictionary<int, int> _currentShop = new Dictionary<int, int>();
		/// <summary>whoAmI → 上一帧是否按着使用键（挥动上升沿检测）</summary>
		private static readonly Dictionary<int, bool> _prevUsing = new Dictionary<int, bool>();
		/// <summary>whoAmI → 是否正在与虚拟旅商对话（精确区分：关闭的是旅商对话还是其他 NPC 对话）</summary>
		private static readonly Dictionary<int, bool> _talkingWithMerchant = new Dictionary<int, bool>();
		/// <summary>whoAmI → 是否在持续高频刷新初始页（打开商店后每 0.2s 重发 104，直到退出旅商/购买雕像）</summary>
		private static readonly Dictionary<int, bool> _refreshing = new Dictionary<int, bool>();
		/// <summary>whoAmI → 距上次 104 发送的累计毫秒（每 0.2s 一次）</summary>
		private static readonly Dictionary<int, double> _refreshAccum = new Dictionary<int, double>();

		private static readonly SocketSendCallback _emptySendCallback = _ => { };

		/// <summary>MonoMod detour：WorldGen.UnspawnTravelNPC。
		/// 天黑/黄昏时原版会移除第一个 type==368 旅商并全服播报"旅商已离开"（NPC.cs UpdateNPC: type==368→travelNPC=true；
		/// Main.cs: !dayTime||time>48600→UnspawnTravelNPC；WorldGen.cs: 广播 Lang.misc[35] + active=false，不走 checkDead）。
		/// 虚拟旅商也是 368 → 被无差别移除+播报。detour 临时隐藏虚拟旅商，让原逻辑只处理真旅商。</summary>
		private static Hook? _unspawnTravelHook;

		/// <summary>WorldGen.UnspawnTravelNPC 原始委托（public static，无参）</summary>
		private delegate void OrigUnspawnTravelNPC();

		private static bool _initialized;
		private static TerrariaPlugin? _plugin;

		public static void Initialize(TerrariaPlugin plugin)
		{
			if (_initialized) return;
			_initialized = true;
			_plugin = plugin;

			// 加载配置（首次自动生成默认配置文件）
			ShopUIConfigManager.EnsureLoaded();
			SyncConfigState();

			// 1. 挥动检测（PlayerControls 包 13 号，TShock 已解析）
			GetDataHandlers.PlayerUpdate += OnPlayerUpdate;
			// 2. 定向过滤（仅自己可见）：出站包钩子（Omni Ghost 同款）
			OTAPI.Hooks.NetMessage.SendBytes += OnSendBytes;
			// 3. 关闭对话检测（客户端→服务端 40 号包，最底层钩子）
			OTAPI.Hooks.MessageBuffer.GetData += OnGetData;
			// 4. 雕像购买检测（背包槽更新包）
			GetDataHandlers.PlayerSlot += OnPlayerSlot;
			// 4.5 丢弃拦截（防手机端购买雕像瞬间丢出刷雕像：对话期间禁丢雕像）
			GetDataHandlers.ItemDrop += OnItemDrop;
			// 5. 玩家下线兜底清理
			ServerApi.Hooks.ServerLeave.Register(plugin, OnServerLeave);
			// 6. 打开商店后的持续高频刷新（每 0.2s 重发 104，直到退出旅商/购买雕像）
			ServerApi.Hooks.GameUpdate.Register(plugin, OnGameUpdate);
			// 7. 天黑自动移除防护：MonoMod detour WorldGen.UnspawnTravelNPC（跳过虚拟旅商）
			try
			{
				var method = typeof(WorldGen).GetMethod("UnspawnTravelNPC", BindingFlags.Public | BindingFlags.Static);
				if (method == null)
				{
					TShock.Log.ConsoleError("[ShopUI] 未找到 WorldGen.UnspawnTravelNPC，天黑自动移除防护未启用");
				}
				else
				{
					_unspawnTravelHook = new Hook(method, OnUnspawnTravelNPC);
				}
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[ShopUI] UnspawnTravelNPC detour 注册失败: {ex.Message}");
			}
		}

		public static void Dispose()
		{
			if (!_initialized) return;
			_initialized = false;

			GetDataHandlers.PlayerUpdate -= OnPlayerUpdate;
			OTAPI.Hooks.NetMessage.SendBytes -= OnSendBytes;
			OTAPI.Hooks.MessageBuffer.GetData -= OnGetData;
			GetDataHandlers.PlayerSlot -= OnPlayerSlot;
			GetDataHandlers.ItemDrop -= OnItemDrop;
			ServerApi.Hooks.ServerLeave.Deregister(_plugin, OnServerLeave);
			ServerApi.Hooks.GameUpdate.Deregister(_plugin, OnGameUpdate);
			try { _unspawnTravelHook?.Dispose(); } catch { }
			_unspawnTravelHook = null;

			foreach (int who in _active.Keys.ToList())
			{
				RemoveMerchant(who, closeChat: false);
			}
			_active.Clear();
			_currentShop.Clear();
			_prevUsing.Clear();
			_talkingWithMerchant.Clear();
			_refreshing.Clear();
			_refreshAccum.Clear();
			_plugin = null;
		}

		/// <summary>REST 保存配置后热重载：同步 enabled/summonItemId；禁用时移除全部虚拟旅商；启用时在线玩家按新配置即时刷新</summary>
		public static void ReloadConfig()
		{
			if (!_initialized) return;
			SyncConfigState();

			if (!_enabled)
			{
				foreach (int who in _active.Keys.ToList())
				{
					RemoveMerchant(who, closeChat: false);
				}
				return;
			}

			// 启用：当前商店越界则回退 0，并按新配置刷新已打开的商店
			var config = ShopUIConfigManager.GetConfig();
			foreach (int who in _active.Keys.ToList())
			{
				if (_currentShop.TryGetValue(who, out int si) && si >= config.shops.Count)
				{
					_currentShop[who] = 0;
				}
				RefreshShop(who);
			}
		}

		private static void SyncConfigState()
		{
			var config = ShopUIConfigManager.GetConfig();
			_enabled = config.enabled;
			_summonItemId = config.summonItemId > 0 ? config.summonItemId : ItemID.TinAxe;
		}

		// ═══════════════════ 出站包定向过滤（仅自己可见） ═══════════════════

		/// <summary>
		/// 出站包格式：[2字节长度][包类型][body...]，msgType 在 Data[Offset+2]
		/// 23 号包（SyncNPC）body 前 2 字节 = NPC 索引；40 号包（SyncTalkNPC）body = [玩家ID][talkNPC]
		/// 匹配虚拟旅商且接收者≠所属玩家 → Cancel 取消发送
		/// </summary>
		private static void OnSendBytes(object? sender, OTAPI.Hooks.NetMessage.SendBytesEventArgs args)
		{
			if (_active.Count == 0) return;
			try
			{
				var buf = args.Data;
				if (buf == null) return;
				int off = args.Offset;
				if (off < 0 || off + 6 > buf.Length) return; // 40 包总长恰好 6 字节，用 > 而非 >=
				byte msgType = buf[off + 2];

				if (msgType == (byte)MessageID.SyncNPC) // 23
				{
					if (off + 5 > buf.Length) return;
					int npcIdx = buf[off + 3] | (buf[off + 4] << 8);
					foreach (var kvp in _active)
					{
						if (kvp.Value == npcIdx && kvp.Key != args.RemoteClient)
						{
							args.Result = OTAPI.HookResult.Cancel;
							return;
						}
					}
				}
				else if (msgType == (byte)MessageID.SyncTalkNPC) // 40
				{
					if (off + 6 > buf.Length) return;
					short talkNPC = (short)(buf[off + 4] | (buf[off + 5] << 8));
					foreach (var kvp in _active)
					{
						if (kvp.Value == talkNPC && kvp.Key != args.RemoteClient)
						{
							args.Result = OTAPI.HookResult.Cancel;
							return;
						}
					}
				}
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[ShopUI] SendBytes 过滤异常: {ex.Message}");
			}
		}

		// ═══════════════════ 商店应用（104 号包逐槽刷新） ═══════════════════

		/// <summary>
		/// 刷新某玩家商店（事件驱动，非轮询）：
		/// 72 号包更新 travelShop 快照（玩家打开商店时的初始布局），
		/// 104 号包覆盖已打开的商店（价格），客户端 Main.npcShop>0 时才应用。
		/// 调用时机：召唤旅商、玩家点开旅商对话（打开商店前）、雕像切商店、配置热重载。
		/// </summary>
		private static void RefreshShop(int who)
		{
			if (who < 0 || who >= Main.maxPlayers) return;
			SyncTravelShop(who);
			ApplyShop(who);
		}

		/// <summary>
		/// 打开旅商商店：立即应用一次（72 快照 + 104），并开启持续高频刷新（每 0.2s 重发 104）。
		/// 原因（源码实证）：72 号包只能写物品 ID，无法定价/置空栏位；104 号包能定价/置空，
		/// 但客户端只在 Main.npcShop > 0（OpenShop 已执行、商店面板已打开）时才应用。
		/// 因此必须打开商店后持续发 104，才能把价格/空槽刷新正确。
		/// 停止时机：退出旅商（关闭对话/移除/下线）或点击雕像切商店。
		/// </summary>
		private static void StartRefresh(int who)
		{
			RefreshShop(who); // 立即先应用一次
			_refreshing[who] = true;
			_refreshAccum[who] = 0;
		}

		/// <summary>每帧检查：持续刷新中的玩家每 0.2s 重发一次 104；旅商已移除或玩家已离线则停</summary>
		private static void OnGameUpdate(EventArgs e)
		{
			ValidateMerchants(); // 旅商死亡/槽被复用 → 清理状态（每帧，不依赖其他事件）
			if (_refreshing.Count == 0) return;
			var stop = new List<int>();            // 仅移除刷新状态（被标记停止）
			var stopAndCleanup = new List<int>();  // 移除刷新状态 + RemoveMerchant 兜底（旅商移除/离线）
			foreach (var who in _refreshing.Keys)
			{
				// 防御：键存在但值为 false（历史残留的置位停止）→ 只移除刷新状态，不触碰旅商
				if (!_refreshing.TryGetValue(who, out var r) || !r)
				{
					stop.Add(who);
					continue;
				}
				// 玩家已离线/断开（ServerLeave 可能未及时触发）→ 停止并兜底移除，避免持续向断开 socket 发包报错
				bool offline = who < 0 || who >= Netplay.Clients.Length
					|| Netplay.Clients[who]?.Socket == null
					|| !Netplay.Clients[who]!.Socket.IsConnected();
				if (!_active.ContainsKey(who) || offline)
				{
					stopAndCleanup.Add(who); // 旅商已移除/玩家离线 → 停止并兜底
					continue;
				}
				double acc = _refreshAccum.TryGetValue(who, out var a) ? a : 0;
				acc += 1000.0 / 60.0; // GameUpdate 约 60fps
				_refreshAccum[who] = acc;
				if (acc >= 200) // 每 0.2s
				{
					_refreshAccum[who] = 0;
					ApplyShop(who);
				}
			}
			foreach (var who in stop)
			{
				_refreshing.Remove(who);
				_refreshAccum.Remove(who);
			}
			foreach (var who in stopAndCleanup)
			{
				_refreshing.Remove(who);
				_refreshAccum.Remove(who);
				if (_active.ContainsKey(who))
				{
					RemoveMerchant(who, closeChat: false); // 兜底：清 NPC + 状态
				}
			}
		}

		/// <summary>
		/// 校验虚拟旅商是否仍存活（每帧）。
		/// 关键：_active 记录的是 NPC 槽索引（Main.npc[] 下标），而 NPC 槽是可复用的——
		/// 旅商死亡后槽被服务器自动复用生成其他 NPC/怪物时，Main.npc[槽] 会变成别的实体。
		/// 若槽内不是活跃的 368 旅商，则判定旅商已死/丢失，仅清理本插件状态，
		/// 绝不触碰 Main.npc[槽]（可能是其他玩家的实体，误删会杀错 NPC/怪物）。
		/// </summary>
		private static void ValidateMerchants()
		{
			if (_active.Count == 0) return;
			var lost = new List<int>();
			foreach (var kvp in _active)
			{
				int idx = kvp.Value;
				bool alive = idx >= 0 && idx < Main.maxNPCs
					&& Main.npc[idx].active
					&& Main.npc[idx].type == TravelingMerchantType;
				if (!alive) lost.Add(kvp.Key);
			}
			foreach (var who in lost)
			{
				CleanupState(who); // 旅商已死/丢失：只清状态，不删 Main.npc[槽]
			}
		}

		/// <summary>仅清理某玩家的虚拟旅商状态（不触碰 NPC 实体，避免误删被复用的其他实体）</summary>
		private static void CleanupState(int who)
		{
			_active.Remove(who);
			_currentShop.Remove(who);
			_talkingWithMerchant.Remove(who);
			_refreshing.Remove(who);
			_refreshAccum.Remove(who);
		}

		// ═══════════════════ 天黑自动移除防护（WorldGen.UnspawnTravelNPC detour） ═══════════════════

		/// <summary>
		/// detour：临时把虚拟旅商 active 置 false → 原版 UnspawnTravelNPC 遍历时跳过它们（只处理真旅商）→ 恢复。
		/// 效果：天黑/黄昏时虚拟旅商不被移除，且不产生全服"旅商已离开"播报；真旅商不受影响照常离开。
		/// </summary>
		private static void OnUnspawnTravelNPC(OrigUnspawnTravelNPC orig)
		{
			var hidden = new List<int>();
			foreach (var kvp in _active)
			{
				int idx = kvp.Value;
				if (idx >= 0 && idx < Main.maxNPCs && Main.npc[idx].active && Main.npc[idx].type == TravelingMerchantType)
				{
					hidden.Add(idx);
					Main.npc[idx].active = false; // 临时隐藏：让原逻辑遍历时跳过虚拟旅商
				}
			}
			try
			{
				orig();
			}
			finally
			{
				foreach (int idx in hidden)
				{
					// 恢复 active；若槽内已不是旅商（极端复用）则不动
					if (idx >= 0 && idx < Main.maxNPCs && Main.npc[idx].type == TravelingMerchantType)
					{
						Main.npc[idx].active = true;
					}
				}
			}
		}

		/// <summary>
		/// 发送 72 号包（TravelMerchantItems）全空快照：40 槽全 0。
		/// 玩家打开商店瞬间 SetupShop(19) 顺序填充全为 0 → 初始商店完全空白；
		/// 内容（商品+雕像）由打开后的持续高频 104 逐槽填充（104 按槽位精确覆盖，不依赖 SetupShop）。
		/// </summary>
		private static void SyncTravelShop(int who)
		{
			var old = (int[])Main.travelShop.Clone();
			try
			{
				for (int i = 0; i < Main.travelShop.Length; i++)
				{
					Main.travelShop[i] = 0; // 全空
				}
				SendTravelShopPacket(who);
			}
			finally
			{
				Array.Copy(old, Main.travelShop, Main.travelShop.Length);
			}
		}

		/// <summary>
		/// 构造 72 号包（TravelMerchantItems）socket 直发：body = 40 个 short 物品 ID（80 字节）。
		/// 包格式与 case 72 写 buffer 一致，长度字段 = 完整包长（含 2 字节长度头）。
		/// </summary>
		private static void SendTravelShopPacket(int who)
		{
			if (who < 0 || who >= Main.maxPlayers) return;
			if (who >= Netplay.Clients.Length) return;
			var sock = Netplay.Clients[who]?.Socket;
			if (sock == null || !sock.IsConnected()) return; // 玩家已断开 → 不再发包

			var body = new byte[80];
			for (int i = 0; i < 40; i++)
			{
				int id = i < Main.travelShop.Length ? Main.travelShop[i] : 0;
				body[i * 2] = (byte)id;
				body[i * 2 + 1] = (byte)(id >> 8);
			}

			var packet = new byte[83]; // 2 长度 + 1 类型 + 80 body
			packet[0] = 83;
			packet[1] = 0;
			packet[2] = (byte)MessageID.TravelMerchantItems; // 72
			Array.Copy(body, 0, packet, 3, 80);

			try
			{
				Netplay.Clients[who].Socket.AsyncSend(packet, 0, packet.Length, _emptySendCallback);
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[ShopUI] 72号包发送失败: {ex.Message}");
			}
		}

		/// <summary>按配置构建商品列表：过滤未解锁（条件求值）与无效格（slot 越界/控件格），价格/数量钳制合法范围</summary>
		private static List<(int slot, int itemId, int stack, int price)> BuildGoods(int shopIndex)
		{
			var list = new List<(int, int, int, int)>();
			var config = ShopUIConfigManager.GetConfig();
			if (shopIndex < 0 || shopIndex >= config.shops.Count) return list;
			var controlSlots = new HashSet<int>();
			foreach (var sc in config.statueControls)
				if (sc.slot >= 0 && sc.slot < 40) controlSlots.Add(sc.slot);

			foreach (var it in config.shops[shopIndex].items)
			{
				if (!ShopUIConfigManager.EvalCondition(it.condition)) continue; // 未解锁 → 不上架
				if (it.slot < 0 || it.slot >= 40 || controlSlots.Contains(it.slot)) continue; // 无效格 → 不上架
				long price = Math.Max(0, Math.Min(int.MaxValue, it.price));
				int stack = Math.Max(1, it.stack);
				list.Add((it.slot, it.itemId, stack, (int)price));
			}
			return list;
		}

		/// <summary>按玩家当前商店索引应用商店：40 格布局（10×4）。控件（目录）优先级高于商品——
		/// 控件占据 statueControls[].slot 指定格子（各商店一致），商品按各自 slot 放置（支持中间留空格、不强制紧凑排序）；
		/// 非法/重复 slot 已被 BuildGoods 过滤，其余格子置空。</summary>
		private static void ApplyShop(int who)
		{
			int shopIndex = _currentShop.TryGetValue(who, out var s) ? s : 0;
			var goods = BuildGoods(shopIndex);
			var config = ShopUIConfigManager.GetConfig();

			// 控件占格集合（slot 0-39）
			var controlSlots = new HashSet<int>();
			foreach (var sc in config.statueControls)
			{
				if (sc.slot >= 0 && sc.slot < 40 && controlSlots.Add(sc.slot))
				{
					// 控件（价格 0，点击跳转 statueControls[i].targetShopIndex；buyOnce=true 延缓刷钱速度）
					SendShopOverride(who, (byte)sc.slot, (short)sc.statueItemId, 1, 0, 0, true);
				}
			}

			// 商品按各自 slot 放置（支持中间留空格，不强制紧凑排序）；非法/重复 slot 已在 BuildGoods 过滤
			var placed = new HashSet<int>();
			foreach (var (slot, itemId, stack, price) in goods)
			{
				if (controlSlots.Contains(slot) || !placed.Add(slot)) continue;
				SendShopOverride(who, (byte)slot, (short)itemId, (short)stack, 0, price, false);
			}

			// 其余格子置空
			for (int slot = 0; slot < 40; slot++)
			{
				if (controlSlots.Contains(slot) || placed.Contains(slot)) continue;
				SendShopOverride(who, (byte)slot, 0, 0, 0, 0, false);
			}
		}

		/// <summary>
		/// 构造 104 号包（ShopOverride）并 socket 直发给目标玩家。
		/// 包格式（TrProtocol 实证）：[2长度][104][byte 槽位][short 物品][short 数量][byte 前缀][int 价格][byte buyOnce]
		/// 长度字段 = 完整包长（含 2 字节长度头）。
		/// </summary>
		private static void SendShopOverride(int who, byte slot, short itemType, short stack, byte prefix, int value, bool buyOnce)
		{
			if (who < 0 || who >= Main.maxPlayers) return;
			if (who >= Netplay.Clients.Length) return;
			var sock = Netplay.Clients[who]?.Socket;
			if (sock == null || !sock.IsConnected()) return; // 玩家已断开 → 不再发包

			var body = new byte[11];
			body[0] = slot;
			body[1] = (byte)itemType;
			body[2] = (byte)(itemType >> 8);
			body[3] = (byte)stack;
			body[4] = (byte)(stack >> 8);
			body[5] = prefix;
			body[6] = (byte)value;
			body[7] = (byte)(value >> 8);
			body[8] = (byte)(value >> 16);
			body[9] = (byte)(value >> 24);
			body[10] = (byte)(buyOnce ? 1 : 0);

			var packet = new byte[14];
			packet[0] = 14;
			packet[1] = 0;
			packet[2] = (byte)MessageID.ShopOverride; // 104
			Array.Copy(body, 0, packet, 3, 11);

			try
			{
				Netplay.Clients[who].Socket.AsyncSend(packet, 0, packet.Length, _emptySendCallback);
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[ShopUI] 104号包发送失败: {ex.Message}");
			}
		}

		// ═══════════════════ 丢弃拦截（防刷雕像） ═══════════════════

		/// <summary>是否雕像控件类型（读配置 statueControls）</summary>
		private static bool IsStatueItem(int type)
		{
			var config = ShopUIConfigManager.GetConfig();
			foreach (var sc in config.statueControls)
			{
				if (sc.statueItemId == type) return true;
			}
			return false;
		}

		/// <summary>
		/// 丢弃拦截（21 号包 ItemDrop / 125 号包 UpdateItemDrop）：
		/// 与虚拟旅商对话（商店打开）期间，禁止玩家丢弃雕像类物品。
		/// 防手机端：购买雕像瞬间把雕像丢出 → 绕过 PlayerSlot 回滚 → 刷出雕像。
		/// Handled=true 阻止服务器生成地上雕像掉落，并回滚背包残留槽位。
		/// </summary>
		private static void OnItemDrop(object sender, GetDataHandlers.ItemDropEventArgs args)
		{
			try
			{
				if (args.Type < 0) return;
				if (!IsStatueItem(args.Type)) return;
				int who = args.Player.Index;
				if (who < 0 || who >= Main.maxPlayers) return;
				if (!_active.ContainsKey(who)) return;
				if (!_talkingWithMerchant.TryGetValue(who, out var t) || !t) return; // 仅对话（商店打开）时禁丢

				args.Handled = true; // 阻止服务器生成雕像掉落

				// 兜底：把背包里残留的雕像槽位回滚（服务器端无雕像 → 客户端恢复原状）
				var inv = args.Player.TPlayer.inventory;
				for (int s = 0; s < inv.Length; s++)
				{
					if (inv[s] != null && inv[s].type == args.Type)
					{
						args.Player.SendData(PacketTypes.PlayerSlot, "", args.Player.Index, s, inv[s].prefix);
					}
				}
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[ShopUI] ItemDrop 拦截异常: {ex.Message}");
			}
		}

		// ═══════════════════ 雕像购买检测（切商店） ═══════════════════

		private static void OnPlayerSlot(object sender, GetDataHandlers.PlayerSlotEventArgs args)
		{
			int who = args.Player.Index;
			if (!_active.ContainsKey(who)) return;

			var config = ShopUIConfigManager.GetConfig();
			int targetShop = -1;
			for (int i = 0; i < config.statueControls.Count; i++)
			{
				if (args.Type == config.statueControls[i].statueItemId)
				{
					targetShop = config.statueControls[i].targetShopIndex; // 跳转逻辑可配置
					break;
				}
			}
			if (targetShop < 0 || targetShop >= config.shops.Count) return;

			// 拦截：雕像不进入服务器背包（购买/卖出/移动一律拒绝）
			args.Handled = true;
			// 回滚客户端该槽（服务器端槽无雕像 → 客户端恢复原状，雕像被"清空"）
			args.Player.SendData(PacketTypes.PlayerSlot, "", args.Player.Index, args.Slot, args.Prefix);
			// 回滚货币槽：卖出雕像会在本地加钱，服务器从未接受该雕像 → 把客户端货币恢复为服务器权威值（防刷钱）
			RollbackCoins(args.Player);

			// 仅"雕像进入背包"（购买，stack>0）才视为点击雕像切商店；卖出/移除（stack<=0）只被禁止，不切商店
			if (args.Stack <= 0) return;

			// 切换商店并刷新（72 快照 + 104 覆盖已打开的商店）；购买雕像 = 停止初始页持续高频刷新
			_currentShop[who] = targetShop;
			_refreshing.Remove(who); // 停止持续刷新：必须 Remove 键（若仅置 false，OnGameUpdate 遍历 Keys 只认键存在，会继续 0.2s 发包）
			_refreshAccum.Remove(who);
			RefreshShop(who);
		}

		/// <summary>把客户端货币槽（铜/银/金/铂金币物品）回滚为服务器权威值，防止卖出雕像本地加钱被同步刷钱</summary>
		private static void RollbackCoins(TSPlayer plr)
		{
			var inv = plr.TPlayer.inventory;
			for (int s = 0; s < inv.Length; s++)
			{
				var it = inv[s];
				if (it == null) continue;
				int t = it.type;
				if (t == ItemID.CopperCoin || t == ItemID.SilverCoin || t == ItemID.GoldCoin || t == ItemID.PlatinumCoin)
				{
					plr.SendData(PacketTypes.PlayerSlot, "", plr.Index, s, it.prefix);
				}
			}
		}

		// ═══════════════════ 挥动检测 ═══════════════════

		private static void OnPlayerUpdate(object sender, GetDataHandlers.PlayerUpdateEventArgs args)
		{
			if (!_enabled) return;
			var plr = args.Player;
			if (plr == null || !plr.RealPlayer) return;
			int who = plr.Index;
			if (who < 0 || who >= Main.maxPlayers) return;

			// 只在"按下使用键"瞬间响应（false→true 上升沿），按住不重复触发
			bool usingNow = args.Control.IsUsingItem;
			bool wasUsing = _prevUsing.TryGetValue(who, out var w) && w;
			_prevUsing[who] = usingNow;
			if (!usingNow || wasUsing) return;

			// 手持物品必须是配置的召唤物
			int slot = args.SelectedItem;
			if (slot < 0 || slot >= plr.TPlayer.inventory.Length) return;
			var held = plr.TPlayer.inventory[slot];
			if (held == null || held.type != _summonItemId) return;

			Trigger(who);
		}

		// ═══════════════════ 核心逻辑 ═══════════════════

		/// <summary>存在 → 拉回；不存在 → 召唤</summary>
		private static void Trigger(int who)
		{
			if (_active.ContainsKey(who))
			{
				PullBack(who);
			}
			else
			{
				Spawn(who);
			}
		}

		private static void Spawn(int who)
		{
			var config = ShopUIConfigManager.GetConfig();
			if (config.shops.Count == 0) return; // 没有商店配置 → 不召唤
			var tp = Main.player[who];
			if (tp == null || !tp.active) return;

			// 创建真实旅商 NPC（仅自己可见：23 号包被 SendBytes 钩子定向取消）
			int npcIndex = NPC.NewNPC(new EntitySource_WorldGen(),
				(int)tp.Bottom.X, (int)tp.Bottom.Y, TravelingMerchantType);
			if (npcIndex < 0 || npcIndex >= Main.maxNPCs)
			{
				TShock.Players[who]?.SendErrorMessage("[ShopUI] 旅商生成失败（没有可用 NPC 槽位）");
				return;
			}

			var npc = Main.npc[npcIndex];
			npc.aiStyle = -1;           // 冻结移动（站定展示）
			npc.velocity = Vector2.Zero;
			npc.immortal = true;        // 不死：StrikeNPC 中 `if(!immortal)` 跳过扣血 → life 恒>0 → checkDead 开头 `life>0` 直接 return →
			                        // 绝不进入城镇 NPC 死亡流程（NPC.cs checkDead: ChatHelper.BroadcastChatMessage(ChatColors.Death) 全服播报"旅商xxx已死去"）
			                        // 同时根除旅商死亡后 NPC 槽被复用的隐患（v1.4.5）；对 friendly 城镇 NPC 无副作用（CanBeChasedBy 需 !friendly 才检查 immortal）
			_active[who] = npcIndex;
			_currentShop[who] = 0;      // 默认第一个商店（打开商店默认页）

			// 广播 23 号包 → SendBytes 钩子过滤 → 仅目标玩家可见
			// ⚠️ 不再主动发 40 号包（会把 talkNPC 卡住产生虚假对话窗口）
			NetMessage.SendData(23, -1, -1, null, npcIndex);

			// 初始化商店：72 号包同步 travelShop（全空快照），104 号包立即应用
			RefreshShop(who);

			TShock.Players[who]?.SendSuccessMessage("[ShopUI] 虚拟旅商已出现！点击他对话开商店；点击底部雕像可切换商店；关闭对话后自动消失；再挥动召唤物可拉回身边");
		}

		/// <summary>把虚拟旅商拉回玩家脚底</summary>
		private static void PullBack(int who)
		{
			if (!_active.TryGetValue(who, out int npcIndex)) return;
			if (npcIndex < 0 || npcIndex >= Main.maxNPCs) return;
			var tp = Main.player[who];
			if (tp == null || !tp.active) return;

			var npc = Main.npc[npcIndex];
			// 旅商已死/槽被其他 NPC 占用 → 清状态并重新召唤真正的旅商，绝不拉错实体
			if (!npc.active || npc.type != TravelingMerchantType)
			{
				CleanupState(who);
				Spawn(who);
				return;
			}
			npc.Bottom = tp.Bottom;
			npc.velocity = Vector2.Zero;
			npc.netUpdate = true;
			// 定向广播（SendBytes 钩子过滤 → 仅目标玩家可见拉回）
			NetMessage.SendData(23, -1, -1, null, npcIndex);
		}

		/// <summary>移除：实体置 inactive + 定向广播 23 + 可选关闭对话</summary>
		private static void RemoveMerchant(int who, bool closeChat)
		{
			if (!_active.TryGetValue(who, out int npcIndex)) return;
			_active.Remove(who);
			_currentShop.Remove(who);
			_talkingWithMerchant[who] = false;
			_refreshing.Remove(who);
			_refreshAccum.Remove(who);

			if (npcIndex >= 0 && npcIndex < Main.maxNPCs && Main.npc[npcIndex].active
				&& Main.npc[npcIndex].type == TravelingMerchantType) // 双保险：只删除真正的旅商，槽被其他实体占用时不动它
			{
				Main.npc[npcIndex].life = 0;
				Main.npc[npcIndex].active = false;
				NetMessage.SendData(23, -1, -1, null, npcIndex);
			}

			if (closeChat && who >= 0 && who < Main.maxPlayers)
			{
				Main.player[who].SetTalkNPC(-1);
				NetMessage.SendData(40, -1, -1, null, who);
			}
		}

		// ═══════════════════ 关闭对话检测（入站 40 号包） ═══════════════════

		private static void OnGetData(object sender, OTAPI.Hooks.MessageBuffer.GetDataEventArgs args)
		{
			if (_active.Count == 0) return;
			try
			{
				var buf = args.Instance?.readBuffer;
				if (buf == null) return;
				int off = args.ReadOffset;
				if (off < 1 || args.Length < 3) return;

				byte id = buf[off - 1]; // 包类型（off-1，PacketCatch 同款取法）
				if (id != (byte)MessageID.SyncTalkNPC) return; // 只关心 40

				int who = args.Instance.whoAmI;
				if (who < 0 || who >= Main.maxPlayers) return;
				if (!_active.ContainsKey(who)) return;

				// 入站 40 包 body：[0]=玩家ID [1..3]=talkNPC(LE)
				short talkNPC = (short)(buf[off + 1] | (buf[off + 2] << 8));
				int expected = _active[who];

				if (talkNPC == expected)
				{
					// 玩家点开旅商对话：打开商店前刷新一次（72 快照按当前 Boss 状态 + 104 价格）
					bool wasTalking = _talkingWithMerchant.TryGetValue(who, out var t) && t;
					_talkingWithMerchant[who] = true;
					if (!wasTalking)
					{
						// 打开商店：开启持续高频刷新（每 0.2s 重发 104），填充 72 全空快照的商品+雕像
						// （104 只在商店已打开时应用，72 无法定价/置空 → 必须打开后持续刷新才能正确显示价格）
						StartRefresh(who);
					}
				}
				else if (talkNPC == -1)
				{
					// 关闭对话：仅当关闭的是旅商对话才移除（避免误移除：玩家先点了向导再关闭）
					bool wasTalking = _talkingWithMerchant.TryGetValue(who, out var t) && t;
					_talkingWithMerchant[who] = false;
					if (wasTalking)
					{
						RemoveMerchant(who, closeChat: false);
					}
				}
				else
				{
					// 切换到其他 NPC 对话
					_talkingWithMerchant[who] = false;
				}
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[ShopUI] 40号包监听异常: {ex.Message}");
			}
		}

		private static void OnServerLeave(LeaveEventArgs e)
		{
			_prevUsing.Remove(e.Who);
			_talkingWithMerchant.Remove(e.Who);
			_currentShop.Remove(e.Who);
			_refreshing.Remove(e.Who);
			_refreshAccum.Remove(e.Who);
			if (_active.ContainsKey(e.Who))
			{
				RemoveMerchant(e.Who, closeChat: false);
			}
		}

		// ═══════════════════ 命令（调试辅助） ═══════════════════

		public static void HandleCommand(CommandArgs args)
		{
			if (args.Parameters.Count > 0 && args.Parameters[0].Equals("kill", StringComparison.OrdinalIgnoreCase))
			{
				if (_active.ContainsKey(args.Player.Index))
				{
					RemoveMerchant(args.Player.Index, closeChat: true);
					args.Player.SendSuccessMessage("[ShopUI] 已移除你的虚拟旅商");
				}
				else
				{
					args.Player.SendInfoMessage("[ShopUI] 你当前没有虚拟旅商");
				}
				return;
			}

			if (args.Parameters.Count > 0 && args.Parameters[0].Equals("status", StringComparison.OrdinalIgnoreCase))
			{
				args.Player.SendInfoMessage($"[ShopUI] 当前激活的虚拟旅商: {_active.Count} 个");
				return;
			}

			if (!_enabled)
			{
				args.Player.SendErrorMessage("[ShopUI] 虚拟商店已停用");
				return;
			}

			// 无参数 = 模拟一次挥动（调试方便）
			Trigger(args.Player.Index);
		}
	}
}
