using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Net.Sockets;
using TerrariaApi.Server;
using TShockAPI;

namespace TShockData
{
	/// <summary>
	/// ShopUI — 虚拟旅商商店
	///
	/// 交互（全部基于 1.4.5 协议源码实证）：
	///   1. 手持锡斧（ItemID.TinAxe=3500）按下使用键（挥动）→ 在脚底召唤虚拟旅商（NPC 368）
	///   2. 仅自己可见：OTAPI.Hooks.NetMessage.SendBytes 出站钩子（Omni Ghost 同款），
	///      把虚拟旅商的 23 号包（SyncNPC）/ 40 号包（SyncTalkNPC）对非所属玩家
	///      args.Result = Cancel 取消发送 → 其他玩家客户端无实体 → 天然不可见
	///   3. 旅商存在时再次挥动锡斧 → 拉回身边（改 position + 定向广播 23）
	///   4. 关闭对话（客户端上报 talkNPC=-1）或下线 → 自动移除（life=0 + active=false + 定向 23）
	///   5. 商店系统（40 槽 10×4）：
	///      - 槽 0-36：当前商店商品（宝藏袋 / 方块 / 药水，价格随机）
	///      - 槽 37/38/39：雕像控件（箱子=宝藏袋、镐子=方块、药水=药水，价格 0）
	///      - 点击雕像（购买触发）→ PlayerSlot 钩子拦截 → 回滚清空手持/背包雕像
	///        → 切换对应商店 → 立即 104 刷新
	///      - 每 5 秒 GameUpdate 按当前商店重新应用（104 号包 ShopOverride，socket 直发）
	///
	/// 已确认的限制：40 号包只同步 talkNPC 状态，不填充客户端对话文本（npcChatText 由
	/// 客户端点击 NPC 时 GetChat() 生成）→ 服务端无法强制弹出对话面板，玩家需点击一次旅商。
	/// ⚠️ 因此绝不主动发 40 号包/SetTalkNPC（会把 talkNPC 卡住产生虚假对话窗口）。
	/// </summary>
	[ApiVersion(2, 1)]
	public class ShopUIPlugin : TerrariaPlugin
	{
		public override string Author => "lmx12330";
		public override string Description => "虚拟旅商商店（挥动锡斧召唤，仅自己可见）";
		public override string Name => "ShopUI";
		public override Version Version => new System.Version(1, 3, 0, 0);

		public ShopUIPlugin(Main game) : base(game) { }

		public override void Initialize()
		{
			ShopUICore.Initialize(this);
			TShockAPI.Commands.ChatCommands.Add(new Command("", ShopUICore.HandleCommand, "shopui", "旅商"));
		}

		protected override void Dispose(bool Disposing)
		{
			if (Disposing)
			{
				ShopUICore.Dispose();
				TShockAPI.Commands.ChatCommands.RemoveAll(c =>
					c.Names.Any(n => n.Equals("shopui", StringComparison.OrdinalIgnoreCase) || n == "旅商"));
			}
			base.Dispose(Disposing);
		}
	}

	public static class ShopUICore
	{
		private const int TravelingMerchantType = NPCID.TravellingMerchant; // 368
		private const int TinAxeItem = ItemID.TinAxe;                       // 3500

		// ═══════════ 商店配置 ═══════════

		private const int GoodsSlots = 37;   // 槽 0-36：商品区
		private const int StatueSlotBase = 37; // 槽 37/38/39：雕像控件

		/// <summary>雕像控件 → 商店索引</summary>
		private static readonly (int itemId, int shopIndex, string name)[] StatueControls =
		{
			(ItemID.ChestStatue,  0, "宝藏袋商店"),
			(ItemID.PickaxeStatue, 1, "方块商店"),
			(ItemID.PotionStatue, 2, "药水商店"),
		};

		/// <summary>宝藏袋商店：按 Boss 击败进度解锁，固定价格（铜币）。肉山标志 = Main.hardMode</summary>
		private static readonly (int itemId, Func<bool> unlocked, string name, long price)[] TreasureBags =
		{
			(3318, () => NPC.downedSlimeKing,      "史莱姆王",   500000L),   // 50金
			(3319, () => NPC.downedBoss1,          "克眼",       550000L),   // 55金
			(3320, () => NPC.downedBoss2,          "世界吞噬者", 600000L),   // 60金
			(3321, () => NPC.downedBoss3,          "克脑",       600000L),   // 60金
			(3322, () => NPC.downedQueenBee,       "蜂后",       700000L),   // 70金
			(3323, () => NPC.downedBoss3,          "骷髅王",     750000L),   // 75金
			(3324, () => Main.hardMode,            "肉山",       1000000L),  // 1铂金
			(3325, () => NPC.downedMechBoss1,      "毁灭者",     1500000L),  // 1.5铂金
			(3326, () => NPC.downedMechBoss2,      "双子魔眼",   1500000L),
			(3327, () => NPC.downedMechBoss3,      "机械骷髅王", 1500000L),
			(3328, () => NPC.downedPlantBoss,      "世纪之花",   2000000L),  // 2铂金
			(3329, () => NPC.downedGolemBoss,      "石巨人",     2500000L),  // 2.5铂金
			(3330, () => NPC.downedFishron,        "猪鲨",       3000000L),  // 3铂金
			(3332, () => NPC.downedMoonlord,       "月总",       10000000L), // 10铂金
			(4957, () => NPC.downedQueenSlime,     "史莱姆皇后", 1200000L),
			(4782, () => NPC.downedEmpressOfLight, "光之女皇",   4500000L),
			(5111, () => NPC.downedDeerclops,      "鹿角怪",     800000L),
		};

		/// <summary>方块商店：固定列表与价格（铜币）</summary>
		private static readonly (int itemId, long price)[] BlockItems =
		{
			// 1 银组
			(ItemID.Wood, 10000L), (ItemID.BambooBlock, 10000L), (ItemID.DynastyWood, 10000L),
			(ItemID.DirtBlock, 10000L), (ItemID.ClayBlock, 10000L), (ItemID.StoneBlock, 10000L),
			(ItemID.SandBlock, 10000L), (ItemID.MudBlock, 10000L), (ItemID.SnowBlock, 10000L),
			(ItemID.IceBlock, 10000L), (ItemID.MarbleBlock, 10000L), (ItemID.GraniteBlock, 10000L),
			(ItemID.Cloud, 10000L), (ItemID.RainCloud, 10000L),
			// 3 银组
			(ItemID.RedBrick, 30000L), (ItemID.GrayBrick, 30000L), (ItemID.Glass, 30000L),
			(ItemID.SnowBrick, 30000L), (ItemID.IceBrick, 30000L), (ItemID.SandstoneBrick, 30000L),
		};

		/// <summary>药水商店：增益药水，每个 50 银（50000 铜币）</summary>
		private static readonly int[] PotionItems =
		{
			ItemID.IronskinPotion, ItemID.SwiftnessPotion, ItemID.RegenerationPotion, ItemID.ShinePotion,
			ItemID.NightOwlPotion, ItemID.GillsPotion, ItemID.WaterWalkingPotion, ItemID.HunterPotion,
			ItemID.ObsidianSkinPotion, ItemID.GravitationPotion, ItemID.ThornsPotion, ItemID.BattlePotion,
			ItemID.ArcheryPotion, ItemID.AmmoReservationPotion, ItemID.EndurancePotion, ItemID.LifeforcePotion,
			ItemID.RagePotion, ItemID.WrathPotion, ItemID.FishingPotion, ItemID.SonarPotion,
			ItemID.CratePotion, ItemID.WarmthPotion, ItemID.CalmingPotion, ItemID.TitanPotion,
			ItemID.BuilderPotion, ItemID.InfernoPotion, ItemID.SpelunkerPotion, ItemID.SummoningPotion,
			ItemID.LuckPotion,
		};

		/// <summary>按商店索引构建商品列表（顺序铺满，固定价格）</summary>
		private static List<(int itemId, int stack, int price)> BuildGoods(int shopIndex)
		{
			var list = new List<(int, int, int)>();
			switch (shopIndex)
			{
				case 0: // 宝藏袋：按 Boss 击败解锁
					foreach (var t in TreasureBags)
					{
						if (t.unlocked())
						{
							list.Add((t.itemId, 1, (int)t.price));
						}
					}
					break;
				case 1: // 方块：固定 50 个一组
					foreach (var b in BlockItems)
					{
						list.Add((b.itemId, 50, (int)b.price));
					}
					break;
				case 2: // 药水：每个 50 银
					foreach (var p in PotionItems)
					{
						list.Add((p, 1, 50000));
					}
					break;
			}
			return list;
		}

		// ═══════════ 运行时状态 ═══════════

		/// <summary>whoAmI → 虚拟旅商 NPC 索引（每玩家同时最多一个）</summary>
		private static readonly Dictionary<int, int> _active = new Dictionary<int, int>();
		/// <summary>whoAmI → 当前商店索引</summary>
		private static readonly Dictionary<int, int> _currentShop = new Dictionary<int, int>();
		/// <summary>whoAmI → 上一帧是否按着使用键（挥动上升沿检测）</summary>
		private static readonly Dictionary<int, bool> _prevUsing = new Dictionary<int, bool>();
		/// <summary>whoAmI → 是否正在与虚拟旅商对话（精确区分：关闭的是旅商对话还是其他 NPC 对话）</summary>
		private static readonly Dictionary<int, bool> _talkingWithMerchant = new Dictionary<int, bool>();

		private static int _tickCounter; // GameUpdate 计数，300 tick = 5 秒
		private static readonly SocketSendCallback _emptySendCallback = _ => { };

		private static bool _initialized;
		private static TerrariaPlugin? _plugin;

		public static void Initialize(TerrariaPlugin plugin)
		{
			if (_initialized) return;
			_initialized = true;
			_plugin = plugin;

			// 1. 挥动检测（PlayerControls 包 13 号，TShock 已解析）
			GetDataHandlers.PlayerUpdate += OnPlayerUpdate;
			// 2. 定向过滤（仅自己可见）：出站包钩子（Omni Ghost 同款）
			OTAPI.Hooks.NetMessage.SendBytes += OnSendBytes;
			// 3. 关闭对话检测（客户端→服务端 40 号包，最底层钩子）
			OTAPI.Hooks.MessageBuffer.GetData += OnGetData;
			// 4. 雕像购买检测（背包槽更新包）
			GetDataHandlers.PlayerSlot += OnPlayerSlot;
			// 5. 玩家下线兜底清理
			ServerApi.Hooks.ServerLeave.Register(plugin, OnServerLeave);
			// 6. 商店定时刷新（每 5 秒按当前商店重新应用）
			ServerApi.Hooks.GameUpdate.Register(plugin, OnGameUpdate);

			TShock.Log.ConsoleInfo("[ShopUI] 已初始化（挥动锡斧召唤仅自己可见的虚拟旅商，雕像切换商店）");
		}

		public static void Dispose()
		{
			if (!_initialized) return;
			_initialized = false;

			GetDataHandlers.PlayerUpdate -= OnPlayerUpdate;
			OTAPI.Hooks.NetMessage.SendBytes -= OnSendBytes;
			OTAPI.Hooks.MessageBuffer.GetData -= OnGetData;
			GetDataHandlers.PlayerSlot -= OnPlayerSlot;
			ServerApi.Hooks.ServerLeave.Deregister(_plugin, OnServerLeave);
			ServerApi.Hooks.GameUpdate.Deregister(_plugin, OnGameUpdate);

			foreach (int who in _active.Keys.ToList())
			{
				RemoveMerchant(who, closeChat: false, silent: true);
			}
			_active.Clear();
			_currentShop.Clear();
			_prevUsing.Clear();
			_talkingWithMerchant.Clear();
			_plugin = null;
			TShock.Log.ConsoleInfo("[ShopUI] 已释放");
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

		/// <summary>GameUpdate 计数驱动：每 300 tick（约 5 秒）给所有持有旅商的玩家重新应用商店</summary>
		private static void OnGameUpdate(EventArgs args)
		{
			if (_active.Count == 0) return;
			if (++_tickCounter < 300) return;
			_tickCounter = 0;
			try
			{
				RefreshAllShops();
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[ShopUI] 商店刷新异常: {ex.Message}");
			}
		}

		private static void RefreshAllShops()
		{
			foreach (var kvp in _active)
			{
				int who = kvp.Key;
				if (who < 0 || who >= Main.maxPlayers) continue;
				// 72 号包更新客户端 travelShop：玩家下次打开商店立即是完整布局（含底部雕像，可跳转）
				SyncTravelShop(who);
				// 104 号包实时刷新已打开的商店（价格/内容），客户端 Main.npcShop>0 时才应用
				ApplyShop(who);
			}
		}

		/// <summary>
		/// 临时填充全局 Main.travelShop 并 socket 直发 72 号包（TravelMerchantItems）给目标玩家，
		/// 然后恢复。填满 40 槽（37 商品 + 3 雕像）保证 SetupShop(19) 顺序填充后槽位对齐、
		/// 雕像固定底部（否则雕像会被推到商品后面乱序）。
		/// </summary>
		private static void SyncTravelShop(int who)
		{
			int shopIndex = _currentShop.TryGetValue(who, out var s) ? s : 0;
			var goods = BuildGoods(shopIndex);
			var old = (int[])Main.travelShop.Clone();
			try
			{
				// 商品铺到 0-36（不足留 0），雕像 37-39。SetupShop 顺序填充：
				// 商品不满 37 时雕像会被推到商品后（打开瞬间位置偏差），104 刷新后按槽位修正回底部
				for (int i = 0; i < GoodsSlots; i++)
				{
					Main.travelShop[i] = i < goods.Count ? goods[i].itemId : 0;
				}
				for (int i = 0; i < StatueControls.Length; i++)
				{
					Main.travelShop[StatueSlotBase + i] = StatueControls[i].itemId;
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
			if (who >= Netplay.Clients.Length || Netplay.Clients[who]?.Socket == null) return;

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

		/// <summary>按玩家当前商店索引应用商店：商品区 0-36 + 雕像控件 37-39（固定内容与价格）</summary>
		private static void ApplyShop(int who)
		{
			int shopIndex = _currentShop.TryGetValue(who, out var s) ? s : 0;
			var goods = BuildGoods(shopIndex);
			int sent = 0;

			// 商品区 0-36：顺序铺满（固定价格），不足补空槽
			for (int slot = 0; slot < GoodsSlots; slot++)
			{
				if (slot < goods.Count)
				{
					SendShopOverride(who, (byte)slot, (short)goods[slot].itemId, (short)goods[slot].stack, 0, goods[slot].price, false);
				}
				else
				{
					SendShopOverride(who, (byte)slot, 0, 0, 0, 0, false); // 空槽
				}
				sent++;
			}

			// 雕像控件 37-39（价格 0，点击即切商店）
			for (int i = 0; i < StatueControls.Length; i++)
			{
				SendShopOverride(who, (byte)(StatueSlotBase + i), (short)StatueControls[i].itemId, 1, 0, 0, false);
				sent++;
			}

			TShock.Log.ConsoleInfo($"[ShopUI][调试] 玩家#{who} 商店[{shopIndex}] 已应用：{goods.Count} 商品 + {StatueControls.Length} 雕像，共 {sent} 个 104 包");
		}

		/// <summary>
		/// 构造 104 号包（ShopOverride）并 socket 直发给目标玩家。
		/// 包格式（TrProtocol 实证）：[2长度][104][byte 槽位][short 物品][short 数量][byte 前缀][int 价格][byte buyOnce]
		/// 长度字段 = 完整包长（含 2 字节长度头）。
		/// </summary>
		private static void SendShopOverride(int who, byte slot, short itemType, short stack, byte prefix, int value, bool buyOnce)
		{
			if (who < 0 || who >= Main.maxPlayers) return;
			if (who >= Netplay.Clients.Length || Netplay.Clients[who]?.Socket == null) return;

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

		// ═══════════════════ 雕像购买检测（切商店） ═══════════════════

		private static void OnPlayerSlot(object sender, GetDataHandlers.PlayerSlotEventArgs args)
		{
			int who = args.Player.Index;
			if (!_active.ContainsKey(who)) return;

			int targetShop = -1;
			for (int i = 0; i < StatueControls.Length; i++)
			{
				if (args.Type == StatueControls[i].itemId)
				{
					targetShop = StatueControls[i].shopIndex;
					break;
				}
			}
			if (targetShop < 0) return;

			// 拦截：雕像不进入服务器背包
			args.Handled = true;
			// 回滚客户端该槽（服务器端槽无雕像 → 客户端恢复原状，雕像被"清空"）
			args.Player.SendData(PacketTypes.PlayerSlot, "", args.Player.Index, args.Slot, args.Prefix);
			// 切换商店并立即应用
			_currentShop[who] = targetShop;
			ApplyShop(who);

			TShock.Players[who]?.SendSuccessMessage($"[ShopUI] 已切换到{StatueControls[targetShop].name}！");
			TShock.Log.ConsoleInfo($"[ShopUI] 玩家 {args.Player.Name} 点击雕像切换到{StatueControls[targetShop].name}");
		}

		// ═══════════════════ 挥动检测 ═══════════════════

		private static void OnPlayerUpdate(object sender, GetDataHandlers.PlayerUpdateEventArgs args)
		{
			var plr = args.Player;
			if (plr == null || !plr.RealPlayer) return;
			int who = plr.Index;
			if (who < 0 || who >= Main.maxPlayers) return;

			// 只在"按下使用键"瞬间响应（false→true 上升沿），按住不重复触发
			bool usingNow = args.Control.IsUsingItem;
			bool wasUsing = _prevUsing.TryGetValue(who, out var w) && w;
			_prevUsing[who] = usingNow;
			if (!usingNow || wasUsing) return;

			// 手持物品必须是锡斧
			int slot = args.SelectedItem;
			if (slot < 0 || slot >= plr.TPlayer.inventory.Length) return;
			var held = plr.TPlayer.inventory[slot];
			if (held == null || held.type != TinAxeItem) return;

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
			_active[who] = npcIndex;
			_currentShop[who] = 0;      // 默认宝藏袋商店

			// 广播 23 号包 → SendBytes 钩子过滤 → 仅目标玩家可见
			// ⚠️ 不再主动发 40 号包（会把 talkNPC 卡住产生虚假对话窗口）
			NetMessage.SendData(23, -1, -1, null, npcIndex);

			// 初始化商店：72 号包同步 travelShop（打开即见完整布局+底部雕像），104 号包立即应用
			SyncTravelShop(who);
			ApplyShop(who);

			TShock.Log.ConsoleInfo($"[ShopUI] 玩家 {TShock.Players[who]?.Name} 召唤了虚拟旅商（NPC #{npcIndex}）");
			TShock.Players[who]?.SendSuccessMessage("[ShopUI] 虚拟旅商已出现！点击他对话开商店；点击底部雕像可切换商店；关闭对话后自动消失；再挥动锡斧可拉回身边");
		}

		/// <summary>把虚拟旅商拉回玩家脚底</summary>
		private static void PullBack(int who)
		{
			if (!_active.TryGetValue(who, out int npcIndex)) return;
			if (npcIndex < 0 || npcIndex >= Main.maxNPCs) return;
			var tp = Main.player[who];
			if (tp == null || !tp.active) return;

			var npc = Main.npc[npcIndex];
			npc.Bottom = tp.Bottom;
			npc.velocity = Vector2.Zero;
			npc.netUpdate = true;
			// 定向广播（SendBytes 钩子过滤 → 仅目标玩家可见拉回）
			NetMessage.SendData(23, -1, -1, null, npcIndex);

			TShock.Players[who]?.SendSuccessMessage("[ShopUI] 虚拟旅商已拉回身边");
		}

		/// <summary>移除：实体置 inactive + 定向广播 23 + 可选关闭对话</summary>
		private static void RemoveMerchant(int who, bool closeChat, bool silent)
		{
			if (!_active.TryGetValue(who, out int npcIndex)) return;
			_active.Remove(who);
			_currentShop.Remove(who);
			_talkingWithMerchant[who] = false;

			if (npcIndex >= 0 && npcIndex < Main.maxNPCs && Main.npc[npcIndex].active)
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

			if (!silent)
			{
				TShock.Log.ConsoleInfo($"[ShopUI] 虚拟旅商（NPC #{npcIndex}）已移除");
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
					// 玩家正在与虚拟旅商对话
					_talkingWithMerchant[who] = true;
				}
				else if (talkNPC == -1)
				{
					// 关闭对话：仅当关闭的是旅商对话才移除（避免误移除：玩家先点了向导再关闭）
					bool wasTalking = _talkingWithMerchant.TryGetValue(who, out var t) && t;
					_talkingWithMerchant[who] = false;
					if (wasTalking)
					{
						TShock.Log.ConsoleInfo($"[ShopUI] 玩家 {TShock.Players[who]?.Name} 已断开旅商对话，移除虚拟旅商");
						RemoveMerchant(who, closeChat: false, silent: false);
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
			if (_active.ContainsKey(e.Who))
			{
				RemoveMerchant(e.Who, closeChat: false, silent: true);
			}
		}

		// ═══════════════════ 命令（调试辅助） ═══════════════════

		public static void HandleCommand(CommandArgs args)
		{
			if (args.Parameters.Count > 0 && args.Parameters[0].Equals("kill", StringComparison.OrdinalIgnoreCase))
			{
				if (_active.ContainsKey(args.Player.Index))
				{
					RemoveMerchant(args.Player.Index, closeChat: true, silent: false);
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

			// 无参数 = 模拟一次挥动（调试方便）
			Trigger(args.Player.Index);
		}
	}
}
