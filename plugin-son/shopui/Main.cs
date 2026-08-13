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
///      - 槽 0-35：当前商店商品（宝藏袋 / 天然方块 / 建筑方块 / 药水，固定价格）
///      - 槽 36-39：雕像控件（箱子=宝藏袋、镐子=天然方块、锤子=建筑方块、药水=药水，价格 0）
	///      - 点击雕像（购买触发）→ PlayerSlot 钩子拦截 → 回滚清空手持/背包雕像
	///        → 切换对应商店 → 立即 104 刷新
	///      - 商店刷新机制：72 号包只能写物品 ID（无法定价/置空），104 号包能定价/置空
	///        但只在客户端 Main.npcShop>0（商店已打开）时应用 → 72 全空快照（打开即空白页），
	///        点开旅商对话后持续高频刷新（每 0.2s 重发 104）填充商品+雕像，退出旅商/购买雕像才停。
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
		public override Version Version => new System.Version(1, 4, 0, 0);

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

		private const int GoodsSlots = 36;   // 槽 0-35：商品区
		private const int StatueSlotBase = 36; // 槽 36/37/38/39：雕像控件

		/// <summary>雕像控件 → 商店索引</summary>
		private static readonly (int itemId, int shopIndex, string name)[] StatueControls =
		{
			(ItemID.ChestStatue,   0, "宝藏袋商店"),
			(ItemID.PickaxeStatue, 1, "天然方块商店"),
			(ItemID.HammerStatue,  2, "建筑方块商店"),
			(ItemID.PotionStatue,  3, "药水商店"),
		};

		/// <summary>宝藏袋商店：按 Boss 击败进度解锁，固定价格（铜币）。肉山标志 = Main.hardMode</summary>
		/// ⚠️ 克脑/世吞不用 NPC.downedBoss2（原版里世吞/克脑共享同一 flag，打一个卖两个）；
		///    改用 BestiaryTracker 图鉴击杀计数（同主插件 BossProgress.GetKillCount，随世界存档持久化）
		///    按 NPC 击杀数各自独立判定（腐化世界打世吞只卖世吞袋，猩红世界打克脑只卖克脑袋）
		private static readonly (int itemId, Func<bool> unlocked, string name, long price)[] TreasureBags =
		{
			(3318, () => NPC.downedSlimeKing,      "史莱姆王",   500000L),   // 50金
			(3319, () => NPC.downedBoss1,          "克眼",       550000L),   // 55金
			(3320, () => Killed(NPCID.EaterofWorldsHead) || Killed(NPCID.EaterofWorldsBody) || Killed(NPCID.EaterofWorldsTail), "世界吞噬者", 600000L),   // 60金
			(3321, () => Killed(NPCID.BrainofCthulhu), "克脑", 600000L),   // 60金
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

		/// <summary>指定 NPC type 是否被击杀过（图鉴 BestiaryTracker 击杀计数，随世界存档持久化，重启不清零）。
		/// 用于区分克脑/世吞——原版 downedBoss2 两 Boss 共享同一 flag，无法各自判定</summary>
		private static bool Killed(int npcType)
		{
			try
			{
				return Main.BestiaryTracker.Kills.GetKillCount(ContentSamples.NpcBestiaryCreditIdsByNpcNetIds[npcType]) > 0;
			}
			catch
			{
				return false;
			}
		}

		/// <summary>方块商店：固定列表与价格（铜币）。1 银 = 100 铜。珍珠木仅困难模式（肉山前隐藏）</summary>
		private static readonly (int itemId, long price)[] BlockItems =
		{
			// 木材（珍珠木=肉山后）
			(ItemID.Wood, 100L), (ItemID.RichMahogany, 100L), (ItemID.PalmWood, 100L), (ItemID.BorealWood, 100L),
			(ItemID.Ebonwood, 100L), (ItemID.Shadewood, 100L), (ItemID.AshWood, 100L), (ItemID.BambooBlock, 100L),
			(ItemID.DynastyWood, 100L), (ItemID.Pearlwood, 100L),
			// 土石沙
			(ItemID.DirtBlock, 100L), (ItemID.ClayBlock, 100L), (ItemID.StoneBlock, 100L), (ItemID.SandBlock, 100L),
			(ItemID.EbonstoneBlock, 100L), (ItemID.CrimstoneBlock, 100L), (ItemID.EbonsandBlock, 100L),
			(ItemID.CrimsandBlock, 100L), (ItemID.Sandstone, 100L), (ItemID.HardenedSand, 100L),
			// 泥雪等
			(ItemID.MudBlock, 100L), (ItemID.AshBlock, 100L), (ItemID.SiltBlock, 100L), (ItemID.SlushBlock, 100L),
			(ItemID.SnowBlock, 100L), (ItemID.IceBlock, 100L), (ItemID.MarbleBlock, 100L), (ItemID.GraniteBlock, 100L),
			(ItemID.Cloud, 100L), (ItemID.RainCloud, 100L),
		};

		/// <summary>建筑方块商店：固定列表与价格（铜币）。每个 3 银 = 300 铜</summary>
		private static readonly (int itemId, long price)[] BuildingItems =
		{
			(ItemID.GrayBrick, 300L), (ItemID.RedBrick, 300L), (ItemID.SnowBrick, 300L), (ItemID.IceBrick, 300L),
			(ItemID.SandstoneBrick, 300L), (ItemID.EbonstoneBrick, 300L), (ItemID.CrimstoneBrick, 300L), (ItemID.Glass, 300L),
			(ItemID.RedDynastyShingles, 300L), (ItemID.BlueDynastyShingles, 300L), (ItemID.Pumpkin, 300L), (ItemID.Cactus, 300L),
			(ItemID.ObsidianBrick, 300L), (ItemID.IridescentBrick, 300L), (ItemID.StoneSlab, 300L), (ItemID.AccentSlab, 300L),
			(ItemID.SandstoneSlab, 300L), (ItemID.MarbleBlock, 300L), (ItemID.GraniteBlock, 300L), (ItemID.SunplateBlock, 300L),
		};

		/// <summary>药水商店：增益药水，便宜在前。铁皮/敏捷/再生 30银(3000)；基础 1金(10000)；洞穴探险/狩猎/危险感知/耐力/隐身 3金(30000)；生命力 5金(50000，仅肉山后)；战斗/镇静/暴怒/怒气/狱火 10金(100000，后三者仅肉山后)；生物群落观测/黑曜石皮 20金(200000)。钓鱼/宝匣/声呐/幸运药水暂不卖</summary>
		private static readonly (int itemId, long price)[] PotionItems =
		{
			// ── 30 银：铁皮/敏捷/再生 ──
			(ItemID.IronskinPotion, 3000L), (ItemID.SwiftnessPotion, 3000L), (ItemID.RegenerationPotion, 3000L),
			// ── 基础 1 金 ──
			(ItemID.ShinePotion, 10000L), (ItemID.NightOwlPotion, 10000L), (ItemID.GillsPotion, 10000L),
			(ItemID.WaterWalkingPotion, 10000L), (ItemID.GravitationPotion, 10000L), (ItemID.ThornsPotion, 10000L),
			(ItemID.ArcheryPotion, 10000L), (ItemID.AmmoReservationPotion, 10000L), (ItemID.WarmthPotion, 10000L),
			(ItemID.TitanPotion, 10000L), (ItemID.BuilderPotion, 10000L), (ItemID.SummoningPotion, 10000L),
			(ItemID.ManaRegenerationPotion, 10000L), (ItemID.MagicPowerPotion, 10000L), (ItemID.FeatherfallPotion, 10000L),
			(ItemID.MiningPotion, 10000L), (ItemID.HeartreachPotion, 10000L), (ItemID.FlipperPotion, 10000L),
			// ── 3 金：洞穴探险/狩猎/危险感知/耐力/隐身 ──
			(ItemID.SpelunkerPotion, 30000L), (ItemID.HunterPotion, 30000L), (ItemID.TrapsightPotion, 30000L),
			(ItemID.EndurancePotion, 30000L), (ItemID.InvisibilityPotion, 30000L),
			// ── 5 金：生命力（仅肉山后售卖）──
			(ItemID.LifeforcePotion, 50000L),
			// ── 10 金：战斗/镇静 + 暴怒/怒气/狱火（后三者仅肉山后）──
			(ItemID.BattlePotion, 100000L), (ItemID.CalmingPotion, 100000L),
			(ItemID.RagePotion, 100000L), (ItemID.WrathPotion, 100000L), (ItemID.InfernoPotion, 100000L),
			// ── 20 金：生物群落观测/黑曜石皮 ──
			(ItemID.BiomeSightPotion, 200000L), (ItemID.ObsidianSkinPotion, 200000L),
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
				case 1: // 天然方块：每个 1 银（100 铜），珍珠木仅困难模式
					foreach (var b in BlockItems)
					{
						if (b.itemId == ItemID.Pearlwood && !Main.hardMode) continue; // 肉山前移除
						list.Add((b.itemId, 1, (int)b.price));
					}
					break;
				case 2: // 建筑方块：每个 3 银（300 铜）
					foreach (var b in BuildingItems)
					{
						list.Add((b.itemId, 1, (int)b.price));
					}
					break;
				case 3: // 药水：便宜在前；生命力/暴怒/怒气/狱火 仅肉山后售卖
					foreach (var p in PotionItems)
					{
						if (p.itemId == ItemID.LifeforcePotion && !Main.hardMode) continue; // 肉山前不卖生命力
						if ((p.itemId == ItemID.RagePotion || p.itemId == ItemID.WrathPotion || p.itemId == ItemID.InfernoPotion) && !Main.hardMode) continue; // 肉山前不卖暴怒/怒气/狱火
						list.Add((p.itemId, 1, (int)p.price));
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
		/// <summary>whoAmI → 是否在持续高频刷新初始页（打开商店后每 0.2s 重发 104，直到退出旅商/购买雕像）</summary>
		private static readonly Dictionary<int, bool> _refreshing = new Dictionary<int, bool>();
		/// <summary>whoAmI → 距上次 104 发送的累计毫秒（每 0.2s 一次）</summary>
		private static readonly Dictionary<int, double> _refreshAccum = new Dictionary<int, double>();

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
			// 4.5 丢弃拦截（防手机端购买雕像瞬间丢出刷雕像：对话期间禁丢雕像）
			GetDataHandlers.ItemDrop += OnItemDrop;
			// 5. 玩家下线兜底清理
			ServerApi.Hooks.ServerLeave.Register(plugin, OnServerLeave);
			// 6. 打开商店后的持续高频刷新（每 0.2s 重发 104，直到退出旅商/购买雕像）
			ServerApi.Hooks.GameUpdate.Register(plugin, OnGameUpdate);
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
		/// 调用时机：召唤旅商、玩家点开旅商对话（打开商店前）、雕像切商店。
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
		/// 否则再次挥动锡斧会走 PullBack 把错误实体拉回身边，且 SendBytes 过滤会把它对其他玩家隐藏。
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

		/// <summary>按玩家当前商店索引应用商店：商品区 0-36 + 雕像控件 37-39（固定内容与价格）</summary>
		private static void ApplyShop(int who)
		{
			int shopIndex = _currentShop.TryGetValue(who, out var s) ? s : 0;
			var goods = BuildGoods(shopIndex);

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
			}

			// 雕像控件 37-39（价格 0，点击即切商店）
			for (int i = 0; i < StatueControls.Length; i++)
			{
				// buyOnce=true：雕像只能买一个，买后该槽被客户端清空，靠持续 104 刷新（0.2s）重新出现 → 延缓刷钱速度
				SendShopOverride(who, (byte)(StatueSlotBase + i), (short)StatueControls[i].itemId, 1, 0, 0, true);
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

		/// <summary>是否雕像控件类型</summary>
		private static bool IsStatueItem(int type)
		{
			for (int i = 0; i < StatueControls.Length; i++)
			{
				if (StatueControls[i].itemId == type) return true;
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
			_currentShop[who] = 0;      // 默认宝藏袋商店（打开商店默认页）

			// 广播 23 号包 → SendBytes 钩子过滤 → 仅目标玩家可见
			// ⚠️ 不再主动发 40 号包（会把 talkNPC 卡住产生虚假对话窗口）
			NetMessage.SendData(23, -1, -1, null, npcIndex);

			// 初始化商店：72 号包同步 travelShop（全空快照），104 号包立即应用
			RefreshShop(who);

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

			// 无参数 = 模拟一次挥动（调试方便）
			Trigger(args.Player.Index);
		}
	}
}
