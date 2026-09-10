using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using MonoMod.RuntimeDetour;
using Terraria;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;

namespace Spectate
{
	/// <summary>
	/// Spectate —— 直播插件：/slive 进入直播模式，发放占卜球(5644)，
	/// 直播者点击后客户端原生进入观战（相机每帧跟随目标实体，平滑不卡顿）；
	/// 服务端接管后自动切换活跃玩家（排除挂机）+ 无痕隐身 + 无敌。
	///
	/// 技术要点（已用 1.4.5.7 客户端反编译源码核实）：
	///   1) 客户端每帧 ResetControls() 无条件清空 controlUseItem（Terraria_Player.cs:29289），
	///      服务端注入 13 包 useItem 永远无法触发 ItemCheck → 必须发真实占卜球，
	///      由真实鼠标输入触发占卜球使用（ScryingOrb 5644 → SpectateNextPlayer）；
	///   2) 客户端 case 150 要求 player6.spectating >= 0 才接受（Terraria_MessageBuffer.cs:4406）
	///      → 进入观战后服务端 150 包才可切换目标；
	///   3) 隐身用 Silent 同款（active=false + PlayerActive 0 广播 + 每帧强制 +
	///      SendData hook 防恢复；ignoreClient 跳过直播者自己，不影响其视角）。
	/// </summary>
	[ApiVersion(2, 1)]
	public class SpectatePlugin : TerrariaPlugin
	{
		public override string Author => "lmx12330";
		public override string Description => "直播：占卜球原生观战 + 自动切换活跃玩家";
		public override string Name => "Spectate";
		public override Version Version => new(1, 1, 0, 0);

		/// <summary>权限节点（Initialize 时自动授予 admin 组）</summary>
		public const string Permission = "spectate.use";

		/// <summary>默认挂机切换阈值（秒）</summary>
		private const double DefaultIdleSeconds = 10.0;

		/// <summary>占卜球物品 ID（客户端点击后进入观战）</summary>
		private const short ScryingOrbItem = 5644;

		// ═══ 直播状态（全局单实例）═══
		private static int _viewer = -1;          // 直播者 whoAmI
		private static int _viewTarget = -1;      // 当前直播目标 whoAmI
		private static bool _spectateActivated;   // 客户端是否已真正进入观战（spectating>=0）
		private static bool _awaitingEntry;       // 已发放占卜球，等待直播者点击进入
		private static double _liveIdleSeconds = DefaultIdleSeconds;
		private static int _targetLockUntil;      // 目标锁定窗口（接管/切换后 1 秒内不覆盖 _viewTarget）
		private static int _replacedHotbarSlot = -1;   // 快捷栏被顶替的槽位（占卜球临时占位）
		private static Item? _replacedHotbarItem;      // 被顶替的原物品

		// ═══ 活跃度统计（直播"挂机"判定）═══
		private static readonly Dictionary<int, DateTime> _lastActive = new();
		private static readonly Dictionary<int, Vector2> _lastPos = new();
		private static readonly object _sync = new();

		// ═══ 钩子 / 生命周期 ═══
		private static Hook? _getDataHook;
		private static bool _otapiGetDataHooked;
		private static readonly List<Hook> _sendDataHooks = new();
		private static SpectatePlugin? _instance;
		private static int _tick;

		/// <summary>直播者丢弃的上行包（防客户端反向上传污染 + 无法操控）。
		/// PlayerControls(13)/SpectatePlayer(150) 放行：13 观战时输入清零（空输入无操作），
		/// 150 放行让服务端感知观战状态（进入/切换/退出）。聊天走 82 NetModule，不在清单中。</summary>
		private static readonly HashSet<byte> BlockedOpPackets = new()
		{
			(byte)PacketTypes.PlayerInfo,        // 4 外观上传/请求
			(byte)PacketTypes.PlayerSlot,        // 5 物品栏上传/操作
			(byte)6,                             // 6 RequestWorldData（TShock 枚举无此名）
			(byte)PacketTypes.PlayerSpawn,       // 12 防复活请求
			(byte)PacketTypes.PlayerHp,          // 16 血量上报/请求
			(byte)PacketTypes.Tile,              // 17 放块/挖块
			(byte)PacketTypes.DoorUse,           // 19 开门
			(byte)PacketTypes.ItemDrop,          // 21 捡物品
			(byte)PacketTypes.ItemOwner,         // 22 认领物品
			(byte)PacketTypes.NpcItemStrike,     // 23 物品击怪
			(byte)PacketTypes.ProjectileNew,     // 27 攻击弹幕
			(byte)PacketTypes.NpcStrike,         // 28 打怪
			(byte)PacketTypes.ProjectileDestroy, // 29 灭弹
			(byte)PacketTypes.TogglePvp,         // 30 PVP
			(byte)PacketTypes.ChestGetContents,  // 31 开箱请求
			(byte)PacketTypes.ChestItem,         // 32 箱子物品
			(byte)PacketTypes.ChestOpen,         // 33 箱子开关
			(byte)PacketTypes.PlaceChest,        // 34 放置箱子
			(byte)PacketTypes.NpcTalk,           // 39 NPC 对话
			(byte)PacketTypes.PlayerAnimation,   // 41 使用物品动画（上行）
			(byte)PacketTypes.PlayerMana,        // 42 蓝量上报/请求
			(byte)PacketTypes.PlayerTeam,        // 45 队伍（上行）
			(byte)PacketTypes.SignRead,          // 46 读牌子
			(byte)PacketTypes.SignNew,           // 47 写牌子
			(byte)PacketTypes.PlayerBuff,        // 50 buff 上报/请求
			(byte)PacketTypes.NpcAddBuff,        // 53 给怪上buff
			(byte)PacketTypes.PlayerAddBuff,     // 55 自己上buff
			(byte)PacketTypes.PaintTile,         // 63 涂色块
			(byte)PacketTypes.PaintWall,         // 64 涂色墙
			(byte)PacketTypes.Teleport,          // 65 传送
			(byte)PacketTypes.TeleportationPotion, // 73 传送药水
			(byte)PacketTypes.CompleteAnglerQuest, // 75 渔夫任务
			(byte)PacketTypes.PlaceTileEntity,   // 86 放置实体
			(byte)PacketTypes.PlaceItemFrame,    // 88 放置物品框
			(byte)PacketTypes.PlaceObject,       // 90 放置物体
			(byte)PacketTypes.SyncLoadout,       // 147 配装同步请求
		};

		public SpectatePlugin(Main game) : base(game) { }

		public override void Initialize()
		{
			if (_instance != null)
				return;
			_instance = this;

			// 1) 上行拦截：OTAPI GetData 事件（主）+ MonoMod detour（兜底）
			RegisterOtapiGetData();
			RegisterGetDataHook();

			// 2) 广播拦截（Silent 同款：直播者 active=false 后防其它代码恢复广播）
			RegisterSendDataHooks();

			// 3) 帧驱动：观战激活接管 / 直播自动切换 / 隐身强制
			ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);
			ServerApi.Hooks.ServerLeave.Register(this, OnServerLeave);

			// 4) TShock PlayerUpdate 事件：活跃度统计（直播"挂机"判定）
			GetDataHandlers.PlayerUpdate += OnPlayerUpdateEvent;

			// 5) 权限 + 命令
			try { TShock.Groups.GetGroupByName("admin")?.AddPermission(Permission); }
			catch { }

			Commands.ChatCommands.Add(new Command(Permission, LiveCommand, "slive", "直播"));
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing && _instance != null)
			{
				_instance = null;
				StopViewing();

				try { _getDataHook?.Dispose(); }
				catch { }
				_getDataHook = null;

				if (_otapiGetDataHooked)
				{
					try { OTAPI.Hooks.MessageBuffer.GetData -= OnMessageBufferGetData; }
					catch { }
					_otapiGetDataHooked = false;
				}

				foreach (var hook in _sendDataHooks)
				{
					try { hook.Dispose(); }
					catch { }
				}
				_sendDataHooks.Clear();

				GetDataHandlers.PlayerUpdate -= OnPlayerUpdateEvent;
				ServerApi.Hooks.GameUpdate.Deregister(this, OnGameUpdate);
				ServerApi.Hooks.ServerLeave.Deregister(this, OnServerLeave);

				Commands.ChatCommands.RemoveAll(c => c.Names.Any(n =>
					n.Equals("slive", StringComparison.OrdinalIgnoreCase) ||
					n.Equals("直播", StringComparison.OrdinalIgnoreCase)));

				lock (_sync)
				{
					_lastActive.Clear();
					_lastPos.Clear();
				}
			}
			base.Dispose(disposing);
		}

		// ════════════════════════════════════════════════
		//  上行通道 1：OTAPI.Hooks.MessageBuffer.GetData（主通道）
		// ════════════════════════════════════════════════

		private static void RegisterOtapiGetData()
		{
			try
			{
				OTAPI.Hooks.MessageBuffer.GetData += OnMessageBufferGetData;
				_otapiGetDataHooked = true;
			}
			catch { }
		}

		private static void OnMessageBufferGetData(object? sender, OTAPI.Hooks.MessageBuffer.GetDataEventArgs args)
		{
			try
			{
				var instance = args.Instance;
				var buf = instance?.readBuffer;
				if (buf == null || instance == null)
					return;
				int off = args.ReadOffset;   // payload 起始（start+1）
				int len = args.Length;
				if (off <= 0 || len <= 0 || off > buf.Length || len > buf.Length - off)
					return;

				int who = instance.whoAmI;
				if (_viewer < 0 || who != _viewer)
					return;

				byte type = buf[off - 1];   // start 位置 = 包类型

				if (_spectateActivated && BlockedOpPackets.Contains(type))
				{
					KeepAlive(who);
					args.Result = OTAPI.HookResult.Cancel;
					args.PacketId = byte.MaxValue;
				}
			}
			catch { }
		}

		// ════════════════════════════════════════════════
		//  上行通道 2：MonoMod detour MessageBuffer.GetData（兜底）
		// ════════════════════════════════════════════════

		private static void RegisterGetDataHook()
		{
			try
			{
				var mi3 = typeof(MessageBuffer).GetMethod("GetData",
					BindingFlags.Public | BindingFlags.Instance, null,
					new[] { typeof(int), typeof(int), typeof(int).MakeByRefType() }, null);
				if (mi3 != null)
				{
					_getDataHook = new Hook(mi3, OnGetData3);
					return;
				}
				var mi2 = typeof(MessageBuffer).GetMethod("GetData",
					BindingFlags.Public | BindingFlags.Instance, null,
					new[] { typeof(int), typeof(int) }, null);
				if (mi2 != null)
				{
					_getDataHook = new Hook(mi2, OnGetData2);
					return;
				}
			}
			catch { }
		}

		private delegate void OrigGetData3(MessageBuffer self, int start, int length, out int messageType);

		private static void OnGetData3(OrigGetData3 orig, MessageBuffer self, int start, int length, out int messageType)
		{
			try
			{
				if (TryProcessUpstream(self, start))
				{
					messageType = (self.readBuffer != null && start >= 0 && start < self.readBuffer.Length) ? self.readBuffer[start] : (byte)0;
					return;
				}
			}
			catch { }
			orig(self, start, length, out messageType);
		}

		private delegate void OrigGetData2(MessageBuffer self, int start, int length);

		private static void OnGetData2(OrigGetData2 orig, MessageBuffer self, int start, int length)
		{
			try
			{
				if (TryProcessUpstream(self, start))
					return;
			}
			catch { }
			orig(self, start, length);
		}

		/// <summary>上行包处理（detour：start=包类型位置，payload 在 start+1）。返回 true=已消费（丢弃）。</summary>
		private static bool TryProcessUpstream(MessageBuffer self, int start)
		{
			var buf = self.readBuffer;
			if (buf == null || start < 0 || start >= buf.Length || start + 1 >= buf.Length)
				return false;
			byte type = buf[start];
			int sender = self.whoAmI;

			if (_viewer < 0 || sender != _viewer)
				return false;

			if (_spectateActivated && BlockedOpPackets.Contains(type))
			{
				KeepAlive(sender);
				return true;
			}
			return false;
		}

		private static void KeepAlive(int who)
		{
			if (who >= 0 && who < Netplay.Clients.Length)
				Netplay.Clients[who].TimeOutTimer = 0;
		}

		// ════════════════════════════════════════════════
		//  广播拦截（Silent 同款）：直播者 active=false 隐身防恢复
		// ════════════════════════════════════════════════

		private static void RegisterSendDataHooks()
		{
			try
			{
				var sendDataMethods = typeof(NetMessage).GetMethods(BindingFlags.Public | BindingFlags.Static)
					.Where(m => m.Name == "SendData" && m.ReturnType == typeof(void))
					.ToList();
				foreach (var m in sendDataMethods)
				{
					try
					{
						var hook = new Hook(m, typeof(SpectatePlugin).GetMethod(nameof(HookedSendData),
							BindingFlags.NonPublic | BindingFlags.Static)!);
						_sendDataHooks.Add(hook);
					}
					catch { }
				}
			}
			catch { }
		}

		private delegate void OrigSendData(int msgType, int remoteClient, int ignoreClient, object? text, int number, float number2, float number3, float number4, int number5, int number6, int number7);

		private static void HookedSendData(OrigSendData orig, int msgType, int remoteClient, int ignoreClient, object? text, int number, float number2, float number3, float number4, int number5, int number6, int number7)
		{
			try
			{
				if (_viewer >= 0 && (PacketTypes)msgType == PacketTypes.PlayerActive && remoteClient == -1
					&& number == _viewer && number5 == 1)
				{
					// 直播隐身期间：任何"该玩家恢复 active"的广播降级为 active=0
					orig(msgType, remoteClient, ignoreClient, text, number, 0f, number3, number4, number5, number6, number7);
					return;
				}
			}
			catch { }
			orig(msgType, remoteClient, ignoreClient, text, number, number2, number3, number4, number5, number6, number7);
		}

		// ════════════════════════════════════════════════
		//  原生观战通道：SpectatePlayer(150) 包
		// ════════════════════════════════════════════════

		/// <summary>发 SpectatePlayer(150)：直播者 index + 目标 index（short）。
		/// 客户端仅在直播者自己 spectating>=0 时接受（已进入观战），目标=-1 退出观战。</summary>
		private static void SendSpectate(int viewer, int target)
		{
			try
			{
				NetMessage.SendData(150, viewer, -1, null, viewer, target);
			}
			catch { }
		}

		// ════════════════════════════════════════════════
		//  无痕隐身（Silent 同款）
		// ════════════════════════════════════════════════

		/// <summary>开启/关闭无痕隐身：active=false + PlayerActive 0 广播（ignoreClient 跳过直播者自己，不影响其视角）。
		/// 注意：退出路径上 _viewer 已被清空，必须显式传 index。</summary>
		private static void ApplyStealth(int who, bool on)
		{
			if (who < 0 || who >= Main.player.Length)
				return;
			try
			{
				Main.player[who].active = on;
				NetMessage.SendData((int)PacketTypes.PlayerActive, -1, who, null, who, on ? 1f : 0f);
			}
			catch { }
		}

		/// <summary>每帧/周期强制隐身（防 TShock 或其它模块恢复 active 广播）</summary>
		private static void ForceStealth()
		{
			if (_viewer < 0 || _viewer >= Main.player.Length)
				return;
			try
			{
				if (Main.player[_viewer].active)
				{
					Main.player[_viewer].active = false;
					NetMessage.SendData((int)PacketTypes.PlayerActive, -1, _viewer, null, _viewer, 0f);
				}
			}
			catch { }
		}

		// ════════════════════════════════════════════════
		//  状态切换
		// ════════════════════════════════════════════════

		/// <summary>
		/// 准备直播：发放真实占卜球，等待直播者点击进入观战（客户端原生进入，可靠）。
		/// 进入后 OnGameUpdate 检测 spectating 激活 → 接管（隐身/无敌/150 切目标）。
		/// </summary>
		private static void PrepareViewing(int adminWho, int targetWho)
		{
			StopViewing();

			var adminTs = GetPlayer(adminWho);
			if (adminTs == null)
				return;

			_viewer = adminWho;
			_viewTarget = targetWho;
			_spectateActivated = false;
			_awaitingEntry = true;

			// 会话级隐身：从准备开始即无痕（active=false + 广播，每帧强制），直到结束才恢复
			ApplyStealth(adminWho, true);

			GiveScryingOrb(adminTs);
		}

		/// <summary>给直播者发放真实占卜球。
		/// 注意：必须放**快捷栏**（槽 50-58）——客户端 ItemCheck 只处理 selectedItem（快捷栏），
		/// 背包里的物品无法被"使用"触发观战。若快捷栏无空位则顶替第 9 格（58），退出时恢复。</summary>
		private static void GiveScryingOrb(TSPlayer player)
		{
			var tplr = player.TPlayer;
			if (tplr == null)
				return;

			// 优先快捷栏空槽（50-58）
			for (int i = 50; i < 59; i++)
			{
				var slot = tplr.inventory[i];
				if (slot == null || slot.type == 0 || slot.stack <= 0)
				{
					var orb = new Item();
					orb.SetDefaults(ScryingOrbItem);
					orb.stack = 1;
					tplr.inventory[i] = orb;
					player.SendData(PacketTypes.PlayerSlot, "", i);
					return;
				}
			}

			// 快捷栏满：顶替第 9 格（58），记录原物品退出时恢复
			var replaced = tplr.inventory[58];
			_replacedHotbarSlot = 58;
			_replacedHotbarItem = replaced != null && replaced.type > 0 ? replaced.Clone() : null;
			var orb2 = new Item();
			orb2.SetDefaults(ScryingOrbItem);
			orb2.stack = 1;
			tplr.inventory[58] = orb2;
			player.SendData(PacketTypes.PlayerSlot, "", 58);
		}

		/// <summary>切换直播目标（150 切换）</summary>
		private static void SwitchTarget(int newTarget)
		{
			if (_viewer < 0)
				return;
			_viewTarget = newTarget;
			_targetLockUntil = _tick + 60;   // 1 秒锁定期：等客户端稳定到新目标
			SendSpectate(_viewer, newTarget);
		}

		/// <summary>退出直播：150(-1) 退出原生观战 + 恢复隐身/无敌</summary>
		private static void StopViewing()
		{
			if (_viewer < 0)
				return;
			int admin = _viewer;
			_viewer = -1;
			_viewTarget = -1;
			_spectateActivated = false;
			_awaitingEntry = false;

			var adminTs = GetPlayer(admin);

			// 1) 退出原生观战（客户端已进入观战时接受 150(-1)）
			if (adminTs != null && adminTs.ConnectionAlive
				&& Main.player[admin] != null && Main.player[admin].spectating >= 0)
			{
				try { SendSpectate(admin, -1); }
				catch { }
			}

			// 2) 恢复隐身 + 无敌
			ApplyStealth(admin, false);
			if (adminTs != null)
				adminTs.GodMode = false;

			// 3) 恢复被占卜球顶替的快捷栏物品
			if (adminTs != null && adminTs.TPlayer != null && _replacedHotbarSlot >= 0)
			{
				try
				{
					adminTs.TPlayer.inventory[_replacedHotbarSlot] = _replacedHotbarItem ?? new Item();
					adminTs.SendData(PacketTypes.PlayerSlot, "", _replacedHotbarSlot);
				}
				catch { }
			}
			_replacedHotbarSlot = -1;
			_replacedHotbarItem = null;
		}

		// ════════════════════════════════════════════════
		//  帧驱动：观战激活接管 / 隐身强制 / 150 保持 / 直播自动切换（排除挂机）
		// ════════════════════════════════════════════════

		private static void OnGameUpdate(EventArgs args)
		{
			_tick++;
			if (_viewer < 0)
				return;

			var viewer = GetPlayer(_viewer);

			// 1) 直播者真正断开连接 → 结束。
			//    不能用 viewer.Active / viewer.TPlayer.active 判断在线：隐身会把
			//    Main.player[].active 置 false（TSPlayer.Active 内部就是 TPlayer.active，
			//    会误判下线导致会话被错误结束——曾导致"点占卜球立刻结束"）。
			//    必须用 ConnectionAlive（RealPlayer && Client.IsActive && !PendingTermination）。
			if (viewer == null || !viewer.ConnectionAlive)
			{
				StopViewing();
				return;
			}

			// 2) 客户端观战状态检测
			bool spectatingActive = false;
			try
			{
				spectatingActive = Main.player[_viewer] != null && Main.player[_viewer].spectating >= 0;
			}
			catch { }

			if (spectatingActive && !_spectateActivated)
			{
				// 刚进入观战：接管（GodMode + 150 切目标；隐身已在 PrepareViewing 开启）
				_spectateActivated = true;
				_awaitingEntry = false;
				viewer.GodMode = true;
				// 切到指定目标
				int initTarget = ResolveTarget(_viewTarget);
				if (initTarget >= 0)
				{
					_viewTarget = initTarget;
					_targetLockUntil = _tick + 60;   // 1 秒锁定期：等客户端稳定到服务端目标
					SendSpectate(_viewer, initTarget);
				}
				return;
			}

			if (!spectatingActive && _spectateActivated)
			{
				// 客户端退出了观战视角（按 Esc/任意键/滚轮等）。
				// 绝对禁止自动结束：保持会话（隐身不恢复），回到待进入，可再点占卜球恢复。
				_spectateActivated = false;
				viewer.GodMode = false;
				_awaitingEntry = true;
				return;
			}

			// 3) 等待进入：保持会话（等直播者点击占卜球）
			if (!spectatingActive && _awaitingEntry)
				return;

			if (!spectatingActive)
				return;

			// 已激活：KeepAlive + 隐身强制
			if (viewer.ConnectionAlive)
				KeepAlive(_viewer);
			ForceStealth();

			// 直播者左右键手动切换目标 → 同步服务端记录（切换/接管后 1 秒锁定期内不覆盖，
			// 避免把服务端刚切的目标误判成"手动切换"）
			if (_tick > _targetLockUntil
				&& Main.player[_viewer] != null && Main.player[_viewer].spectating >= 0
				&& Main.player[_viewer].spectating != _viewTarget)
			{
				_viewTarget = Main.player[_viewer].spectating;
			}

			// 4) 目标死亡/下线 → 自动切换下一位。绝不自动结束。
			var target = GetPlayer(_viewTarget);
			bool targetGone = target == null || !target.Active || target.TPlayer == null
				|| !target.TPlayer.active || target.TPlayer.dead;
			if (targetGone)
			{
				int next = FindNextLiveTarget(_viewTarget);
				if (next < 0)
				{
					// 无存活玩家：保持当前状态等待（玩家复活/上线后自动切换）
					return;
				}
				SwitchTarget(next);
			}

			// 5) 直播：每秒检查挂机 → 自动切换（排除挂机）
			if (_tick % 60 != 0)
				return;

			bool targetIdle = false;
			if (_viewTarget >= 0)
			{
				lock (_sync)
				{
					targetIdle = !_lastActive.TryGetValue(_viewTarget, out var t)
						|| (DateTime.UtcNow - t).TotalSeconds >= _liveIdleSeconds;
				}
			}
			if (targetIdle)
			{
				int next = FindNextLiveTarget(_viewTarget);
				if (next < 0)
					return;
				SwitchTarget(next);
			}
		}

		/// <summary>解析实际直播目标：指定目标无效时退回第一个存活/活跃玩家（直播优先活跃）</summary>
		private static int ResolveTarget(int specified)
		{
			if (IsAliveTarget(specified) && specified != _viewer)
				return specified;
			return FindNextLiveTarget(-1);
		}

		private static bool IsAliveTarget(int who)
		{
			var ts = GetPlayer(who);
			if (ts == null || !ts.Active || ts.TPlayer == null)
				return false;
			var p = Main.player[who];
			return p != null && p.active && !p.dead;
		}

		/// <summary>找下一位存活玩家（whoAmI 递增循环，跳过自己）</summary>
		private static int FindNextTarget(int current)
		{
			int start = current >= 0 ? current : 0;
			for (int i = 1; i < Main.player.Length; i++)
			{
				int idx = (start + i) % Main.player.Length;
				if (IsAliveTarget(idx) && idx != _viewer)
					return idx;
			}
			return -1;
		}

		/// <summary>直播找下一位活跃玩家（存活 + 未挂机）</summary>
		private static int FindNextLiveTarget(int current)
		{
			int start = current >= 0 ? current : 0;
			for (int i = 1; i < Main.player.Length; i++)
			{
				int idx = (start + i) % Main.player.Length;
				if (!IsAliveTarget(idx) || idx == _viewer)
					continue;
				lock (_sync)
				{
					if (_lastActive.TryGetValue(idx, out var t)
						&& (DateTime.UtcNow - t).TotalSeconds < _liveIdleSeconds)
						return idx;
				}
			}
			return -1;
		}

		private static void OnServerLeave(LeaveEventArgs args)
		{
			int who = args.Who;
			if (who == _viewer)
			{
				// 直播者本人下线：直接退出
				StopViewing();
			}
			// 目标下线：不立即退出（直播是持续状态），交给 OnGameUpdate 自动切换下一位存活玩家
			lock (_sync)
			{
				_lastActive.Remove(who);
				_lastPos.Remove(who);
			}
		}

		// ════════════════════════════════════════════════
		//  活跃度统计（直播"挂机"判定）
		// ════════════════════════════════════════════════

		private static void OnPlayerUpdateEvent(object? sender, GetDataHandlers.PlayerUpdateEventArgs e)
		{
			if (e.Player == null)
				return;
			int who = e.Player.Index;
			if (who == _viewer)
				return; // 直播者自己不计活跃

			bool operating = e.Control.MoveUp || e.Control.MoveDown || e.Control.MoveLeft || e.Control.MoveRight
				|| e.Control.Jump || e.Control.IsUsingItem;

			lock (_sync)
			{
				bool moved = _lastPos.TryGetValue(who, out var lp)
					&& Vector2.DistanceSquared(lp, e.Position) > 1f;
				_lastPos[who] = e.Position;

				if (moved || operating)
					_lastActive[who] = DateTime.UtcNow;
				else if (!_lastActive.ContainsKey(who))
					_lastActive[who] = DateTime.UtcNow;
			}
		}

		// ════════════════════════════════════════════════
		//  命令
		// ════════════════════════════════════════════════

		private static void LiveCommand(CommandArgs args)
		{
			TSPlayer admin = args.Player;
			if (admin == null) return;

			var paras = args.Parameters;
			if (paras.Count >= 1 && (paras[0].Equals("off", StringComparison.OrdinalIgnoreCase) || paras[0].Equals("stop", StringComparison.OrdinalIgnoreCase)))
			{
				if (_viewer == admin.Index && (_spectateActivated || _awaitingEntry))
				{
					StopViewing();
					admin.SendInfoMessage("[slive]结束");
				}
				return;
			}

			if (paras.Count >= 1 && double.TryParse(paras[0], out double sec))
			{
				_liveIdleSeconds = Math.Clamp(sec, 1.0, 300.0);
			}

			if (_viewer == admin.Index && (_spectateActivated || _awaitingEntry))
				return;

			// 优先切第一个活跃玩家；全员挂机时降级切第一个存活玩家（直播是持续状态）
			int first = FindNextLiveTarget(-1);
			if (first < 0)
				first = FindNextTarget(-1);
			if (first < 0)
				return;

			PrepareViewing(admin.Index, first);
			admin.SendInfoMessage("[slive]启动");
		}

		// ════════════════════════════════════════════════
		//  辅助
		// ════════════════════════════════════════════════

		private static TSPlayer? GetPlayer(int who)
			=> who >= 0 && who < TShock.Players.Length ? TShock.Players[who] : null;
	}
}
