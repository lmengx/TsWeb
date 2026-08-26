using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using MonoMod.RuntimeDetour;
using OTAPI;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using TerrariaApi.Server;
using TShockAPI;

namespace Possess
{
	/// <summary>
	/// Possess —— 寄生 / 观战 / 直播 插件（管理员专用）。
	///
	/// ════════════════════════════════════════════════════════════════
	/// 核心机制：客户端角色伪装（下行 index 改写 + 上行 index 映射）
	/// ════════════════════════════════════════════════════════════════
	/// 灵感来源：多服同步槽位错乱 —— 玩家 A 的包被错发给玩家 B，B 客户端
	/// 认为"自己"是 A 的角色（外观/背包/位置都是 A 的），但操作全被服务端拒绝。
	/// 本插件主动复刻该机制：
	///
	/// 1) 下行伪装（给管理员的出站包）：
	///    服务端广播目标玩家的角色状态包（SyncPlayer/PlayerUpdate/PlayerHp/
	///    PlayerAnimation/PlayerMana/PlayerSlot/PlayerTeam/PlayerBuff，payload[0]
	///    均为 player index）时，发给管理员的那一份把 index 改成管理员自己
	///    → 管理员客户端认为"自己"就是目标（外观/背包/选中物品格/位置全同）。
	///    同时丢弃发给管理员的"管理员自己 index"的 PlayerUpdate（防旧位置覆盖伪装）。
	///
	/// 2) 上行映射（管理员 → 目标）：
	///    管理员的 PlayerUpdate(13)/PlayerSlot(42)（payload[0] 有 index）把 index
	///    改成目标 → 服务端把管理员的移动/物品操作应用到目标玩家并广播。
	///    无 index 的操作包（Tile 放块等）以管理员身份自然生效（管理员客户端
	///    渲染"自己"在目标位置 → 操作位置即目标位置）。
	///
	/// 3) 目标冻结（寄生模式）：
	///    目标发来的全部"操作类包"（移动/放块/开箱/攻击/物品栏/队伍/传送等）
	///    直接丢弃（聊天放行）→ 目标完全只读；服务端用权威状态持续覆盖其客户端。
	///
	/// 4) 目标看到自己被驱动（寄生模式）：
	///    原生广播 exclude 发送者（改包后=目标）→ GameUpdate 延迟 1 帧补发
	///    PlayerUpdate 给目标，目标看到自己的角色被操作者驱动却无法操作。
	///
	/// ════════════════════════════════════════════════════════════════
	/// 通道（1.4.5.7 update otapi / OTAPI3 重打包实证）：
	///   • 上行：OTAPI.Hooks.MessageBuffer.GetData（CrossTransfer 同款可靠）
	///           + MonoMod detour MessageBuffer.GetData（兜底）
	///   • 下行：OTAPI.Hooks.NetMessage.SendPacket（mfwh_SendPacket 逐客户端发送，
	///            remoteClient 必为具体索引；丢弃 = RemoteClient 置 -1 吞越界异常）
	/// </summary>
	[ApiVersion(2, 1)]
	public class PossessPlugin : TerrariaPlugin
	{
		public override string Author => "lmx12330";
		public override string Description => "寄生/观战/直播：客户端角色伪装（管理员客户端=目标数据），管理员操作映射到目标，目标操作被服务端拒绝";
		public override string Name => "Possess";
		public override Version Version => new Version(1, 0, 0, 0);

		/// <summary>权限节点（Initialize 时自动授予 admin 组）</summary>
		public const string Permission = "possess.use";

		/// <summary>默认挂机切换阈值（秒）</summary>
		private const double DefaultIdleSeconds = 10.0;

		// ═══ 观看状态（全局单实例：寄生/观战/直播互斥共用一个"导演"）═══
		private static int _viewer = -1;      // 被伪装的管理员 whoAmI
		private static int _viewTarget = -1;  // 当前伪装目标 whoAmI
		private static bool _possessMode;     // true=寄生（管理员操作映射+目标冻结）；false=观战/直播（管理员操作丢弃、目标正常）
		private static bool _liveMode;        // 直播（自动切换目标）
		private static double _liveIdleSeconds = DefaultIdleSeconds;

		// ═══ 活跃度统计（直播"挂机"判定）═══
		private static readonly Dictionary<int, DateTime> _lastActive = new();
		private static readonly Dictionary<int, Vector2> _lastPos = new();
		private static readonly object _sync = new();

		// ═══ 钩子 / 事件 / 生命周期 ═══
		private static Hook? _getDataHook;
		private static Hook? _sendPacketHook;
		private static bool _otapiGetDataHooked;
		private static PossessPlugin? _instance;
		private static int _tick;
		private static int _pendingRelayTick = -1; // 寄生映射后需补发给目标的标记

		/// <summary>目标冻结：被寄生时丢弃的"主动操作类"上行包（聊天/被动状态放行）</summary>
		private static readonly HashSet<byte> FrozenOpPackets = new()
		{
			(byte)PacketTypes.PlayerUpdate,        // 13 移动/使用
			(byte)PacketTypes.Tile,                // 17 放块/挖块
			(byte)PacketTypes.DoorUse,             // 19 开门
			(byte)PacketTypes.ProjectileNew,       // 27 攻击弹幕
			(byte)PacketTypes.ProjectileDestroy,   // 29 灭弹
			(byte)PacketTypes.TogglePvp,           // 30 PVP 开关
			(byte)PacketTypes.ChestGetContents,    // 31 开箱请求
			(byte)PacketTypes.ChestItem,           // 32 箱子物品操作
			(byte)PacketTypes.ChestOpen,           // 33 箱子开关
			(byte)PacketTypes.PlaceChest,          // 34 放置箱子
			(byte)PacketTypes.NpcTalk,             // 39 NPC 对话
			(byte)PacketTypes.PlayerAnimation,     // 40 攻击动画
			(byte)PacketTypes.PlayerSlot,          // 物品栏操作
			(byte)PacketTypes.PlayerTeam,          // 45 队伍
			(byte)PacketTypes.SignRead,            // 46 读牌子
			(byte)PacketTypes.SignNew,             // 47 写牌子
			(byte)PacketTypes.PaintTile,           // 63 涂色块
			(byte)PacketTypes.PaintWall,           // 64 涂色墙
			(byte)PacketTypes.Teleport,            // 65 传送
			(byte)PacketTypes.PlaceTileEntity,     // 86 放置实体
			(byte)PacketTypes.PlaceItemFrame,      // 88 放置物品框
			(byte)PacketTypes.PlaceObject,         // 90 放置物体
			(byte)PacketTypes.ItemDrop,            // 21 捡物品
			(byte)PacketTypes.ItemOwner,           // 22 认领物品
			(byte)PacketTypes.NpcStrike,           // 28 打怪
			(byte)PacketTypes.NpcItemStrike,       // 23 物品击怪
			(byte)PacketTypes.NpcAddBuff,          // 给怪上 buff
			(byte)PacketTypes.PlayerAddBuff,       // 给自己上 buff
			(byte)PacketTypes.TeleportationPotion, // 传送药水
			(byte)PacketTypes.CompleteAnglerQuest, // 渔夫任务
		};

		/// <summary>下行伪装：发给管理员的"玩家角色状态包"（payload[0] 均为 player index）</summary>
		private static readonly HashSet<byte> RoleStatePackets = new()
		{
			(byte)PacketTypes.PlayerInfo,      // 4 SyncPlayer：外观/属性/背包
			(byte)PacketTypes.PlayerUpdate,    // 13 位置/控制/选中物品格
			(byte)PacketTypes.PlayerHp,        // 16 血量
			(byte)PacketTypes.PlayerAnimation, // 40 攻击动画
			(byte)PacketTypes.PlayerMana,      // 蓝量
			(byte)PacketTypes.PlayerSlot,      // 物品栏同步
			(byte)PacketTypes.PlayerTeam,      // 45 队伍
			(byte)PacketTypes.PlayerBuff,      // 50 buff
		};

		public PossessPlugin(Main game) : base(game) { }

		public override void Initialize()
		{
			if (_instance != null)
				return;
			_instance = this;

			// 1) 上行：MonoMod detour（兜底）+ OTAPI GetData 事件（主通道）
			RegisterGetDataHook();
			RegisterOtapiGetData();

			// 2) 下行：MonoMod detour NetMessage.SendPacket（1.4.5.7 为 public static，Compat1456 同款实证触发）
			RegisterSendPacketHook();

			// 3) TShock PlayerUpdate 事件：活跃度统计（直播"挂机"判定）
			GetDataHandlers.PlayerUpdate += OnPlayerUpdateEvent;

			// 4) 帧驱动：直播自动切换 / 寄生保护 / 补发
			ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);
			ServerApi.Hooks.ServerLeave.Register(this, OnServerLeave);

			// 5) 权限 + 命令
			try
			{
				TShock.Groups.GetGroupByName("admin")?.AddPermission(Permission);
			}
			catch { }

			Commands.ChatCommands.Add(new Command(Permission, PossessCommand, "possess", "寄生")
			{ HelpText = "接管指定玩家全部操作（目标自身冻结）：/possess <玩家名> | /possess stop" });
			Commands.ChatCommands.Add(new Command(Permission, WatchCommand, "watch", "观战")
			{ HelpText = "第一人称观战目标：/watch <玩家名> | /watch next | /watch stop" });
			Commands.ChatCommands.Add(new Command(Permission, LiveCommand, "live", "直播")
			{ HelpText = "自动切换观战活跃玩家：/live [on|off|秒数]" });

			TShock.Log.ConsoleInfo($"[Possess] 寄生/观战/直播插件已启用（权限 {Permission}；上行 GetData={_otapiGetDataHooked}，下行 SendPacket={_sendPacketHook != null}）");
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing && _instance != null)
			{
				_instance = null;

				// 清理运行态（热卸载安全）
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

				try { _sendPacketHook?.Dispose(); }
				catch { }
				_sendPacketHook = null;

				GetDataHandlers.PlayerUpdate -= OnPlayerUpdateEvent;
				ServerApi.Hooks.GameUpdate.Deregister(this, OnGameUpdate);
				ServerApi.Hooks.ServerLeave.Deregister(this, OnServerLeave);

				Commands.ChatCommands.RemoveAll(c => c.Names.Any(n =>
					n.Equals("possess", StringComparison.OrdinalIgnoreCase) ||
					n.Equals("寄生", StringComparison.OrdinalIgnoreCase) ||
					n.Equals("watch", StringComparison.OrdinalIgnoreCase) ||
					n.Equals("观战", StringComparison.OrdinalIgnoreCase) ||
					n.Equals("live", StringComparison.OrdinalIgnoreCase) ||
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
		//  上行通道 1：MonoMod detour MessageBuffer.GetData（兜底）
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
					TShock.Log.ConsoleInfo("[Possess] GetData detour 已挂载（3 参，兜底）");
					return;
				}
				var mi2 = typeof(MessageBuffer).GetMethod("GetData",
					BindingFlags.Public | BindingFlags.Instance, null,
					new[] { typeof(int), typeof(int) }, null);
				if (mi2 != null)
				{
					_getDataHook = new Hook(mi2, OnGetData2);
					TShock.Log.ConsoleInfo("[Possess] GetData detour 已挂载（2 参，兜底）");
					return;
				}
				TShock.Log.ConsoleError("[Possess] 未找到 MessageBuffer.GetData，兜底通道不可用");
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[Possess] GetData detour 注册失败: {ex.Message}");
			}
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

		// ════════════════════════════════════════════════
		//  上行通道 2：OTAPI.Hooks.MessageBuffer.GetData（主通道，实证可靠）
		// ════════════════════════════════════════════════

		private static void RegisterOtapiGetData()
		{
			try
			{
				OTAPI.Hooks.MessageBuffer.GetData += OnMessageBufferGetData;
				_otapiGetDataHooked = true;
				TShock.Log.ConsoleInfo("[Possess] OTAPI GetData 事件已挂载（上行主通道）");
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[Possess] OTAPI GetData 事件注册失败: {ex.Message}");
			}
		}

		/// <summary>
		/// 上行包处理（OTAPI 事件：off=payload 开始，type 在 buf[off-1]，payload[0] 在 buf[off]）。
		/// 返回 true = 包已消费（丢弃），false = 放行。
		/// </summary>
		private static bool TryProcessUpstream(MessageBuffer self, int start)
		{
			var buf = self.readBuffer;
			if (buf == null || start < 0 || start >= buf.Length || start + 1 >= buf.Length)
				return false;
			byte type = buf[start];
			int sender = self.whoAmI;

			// 无观看状态 → 放行
			if (_viewer < 0 || _viewTarget < 0)
				return false;

			// ═══ 1) 目标（寄生中冻结）：丢弃操作类包，聊天放行 ═══
				if (sender == _viewTarget && sender != _viewer)
				{
					if (!_possessMode)
						return false; // 观战/直播：目标正常玩
					// 聊天走 82 NetModule（不在 FrozenOpPackets 中）→ 自然放行
					if (FrozenOpPackets.Contains(type))
					{
						KeepAlive(sender);
						return true; // 丢弃
					}
					return false;
				}

			// ═══ 2) 管理员（寄生中映射 / 观战直播中丢弃操作）═══
			if (sender == _viewer && sender != _viewTarget)
			{
				if (_possessMode)
				{
					// 寄生：有 index 的角色操作包 → 改 index 为目标
					if (type == (byte)PacketTypes.PlayerUpdate)
					{
						if (start + 1 < buf.Length)
						{
							buf[start + 1] = (byte)_viewTarget;
							_pendingRelayTick = _tick; // 补发给目标（原生广播 exclude 发送者）
						}
					}
					else if (type == (byte)PacketTypes.PlayerSlot)
					{
						if (start + 1 < buf.Length)
							buf[start + 1] = (byte)_viewTarget;
					}
					return false; // 放行（改包后由原生应用）
				}
				else
				{
					// 观战/直播：管理员操作类包丢弃（纯观看，管理员角色冻结）
					if (FrozenOpPackets.Contains(type) || type == (byte)PacketTypes.PlayerSlot || type == (byte)PacketTypes.PlayerAnimation)
					{
						KeepAlive(sender);
						return true; // 丢弃
					}
				}
			}

			return false;
		}

		private static void OnMessageBufferGetData(object? sender, OTAPI.Hooks.MessageBuffer.GetDataEventArgs args)
		{
			try
			{
				var buf = args.Instance?.readBuffer;
				if (buf == null)
					return;
				int off = args.ReadOffset;
				int len = args.Length;
				if (off <= 0 || len <= 0 || off > buf.Length || len > buf.Length - off)
					return;

				// ═══ 1) 目标（寄生中冻结）：丢弃操作类包（聊天走 82 NetModule，不在清单中 → 放行）═══
				int who = args.Instance.whoAmI;
				if (_viewer >= 0 && _viewTarget >= 0 && _possessMode && who == _viewTarget && who != _viewer)
				{
					byte type = buf[off - 1];
					if (FrozenOpPackets.Contains(type))
					{
						KeepAlive(who);
						args.Result = OTAPI.HookResult.Cancel;
						args.PacketId = byte.MaxValue;
					}
					return;
				}

				// ═══ 2) 管理员 ═══
				if (_viewer >= 0 && _viewTarget >= 0 && who == _viewer && who != _viewTarget)
				{
					byte type = buf[off - 1];
					if (_possessMode)
					{
						// 寄生：PlayerUpdate/PlayerSlot 的 index → 目标
						if (type == (byte)PacketTypes.PlayerUpdate && off < buf.Length)
						{
							buf[off] = (byte)_viewTarget;
							_pendingRelayTick = _tick;
						}
						else if (type == (byte)PacketTypes.PlayerSlot && off < buf.Length)
						{
							buf[off] = (byte)_viewTarget;
						}
					}
					else
					{
						// 观战/直播：丢弃管理员操作类包
						if (type == (byte)PacketTypes.PlayerSlot || type == (byte)PacketTypes.PlayerAnimation
							|| FrozenOpPackets.Contains(type))
						{
							KeepAlive(who);
							args.Result = OTAPI.HookResult.Cancel;
							args.PacketId = byte.MaxValue;
						}
					}
				}
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleDebug($"[Possess] OTAPI GetData 处理异常: {ex.Message}");
			}
		}

		private static void KeepAlive(int who)
		{
			if (who >= 0 && who < Netplay.Clients.Length)
				Netplay.Clients[who].TimeOutTimer = 0;
		}

		// ════════════════════════════════════════════════
		//  下行通道：MonoMod detour NetMessage.SendPacket（角色伪装）
		//  1.4.5.7（OTAPI3）SendPacket 为 public static，Compat1456 同款实证触发；
		//  广播逐客户端调用（remoteClient=具体索引，-1 会在 buffer[-1] 越界被吞）。
		//  ⚠️ SendData 广播循环复用同一 writeBuffer → 伪装必须复制数组再改，
		//     否则污染排在后面的其它客户端（Compat1456 实证的坑）。
		// ════════════════════════════════════════════════

		private static void RegisterSendPacketHook()
		{
			try
			{
				var method = typeof(NetMessage).GetMethod("SendPacket",
					BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null,
					new[] { typeof(byte[]), typeof(int) }, null);
				if (method == null)
				{
					TShock.Log.ConsoleError("[Possess] 未找到 NetMessage.SendPacket，下行伪装不可用");
					return;
				}
				_sendPacketHook = new Hook(method, OnSendPacket);
				TShock.Log.ConsoleInfo("[Possess] SendPacket detour 已挂载（下行伪装通道）");
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[Possess] SendPacket Hook 注册失败: {ex.Message}");
			}
		}

		private delegate void OrigSendPacket(byte[] data, int remoteClient);

		private static void OnSendPacket(OrigSendPacket orig, byte[] data, int remoteClient)
		{
			// 返回值：true=已处理（丢弃或伪装版已发），false=原样放行
			bool handled = TryDisguise(data, remoteClient, out byte[]? disguised);
			if (handled)
			{
				if (disguised != null)
					orig(disguised, remoteClient); // 发伪装版（独立数组，不污染共享缓冲）
				return;
			}
			orig(data, remoteClient);
		}

		/// <summary>
		/// 下行伪装/丢弃。
		/// 返回 true=已消费；disguised != null 时调用方须发送该伪装版（代替原 data）。
		/// </summary>
		private static bool TryDisguise(byte[] data, int remoteClient, out byte[]? disguised)
		{
			disguised = null;
			if (_viewer < 0 || _viewTarget < 0)
				return false;
			if (remoteClient != _viewer)
				return false;
			if (data == null || data.Length < 4)
				return false;

			byte type = data[2];
			byte payload0 = data[3]; // payload[0]（角色状态包均为 player index）

			// 伪装：目标的状态包 → 管理员自己（复制数组，不改共享 writeBuffer）
			if (payload0 == (byte)_viewTarget && RoleStatePackets.Contains(type))
			{
				byte[] nd = new byte[data.Length];
				Array.Copy(data, nd, data.Length);
				nd[3] = (byte)_viewer;
				disguised = nd;
				return true;
			}

			// 防覆盖：丢弃发给管理员的"自己 index" PlayerUpdate（旧位置/状态会覆盖伪装）
			if (payload0 == (byte)_viewer && type == (byte)PacketTypes.PlayerUpdate)
				return true;

			return false;
		}

		// ════════════════════════════════════════════════
		//  TShock PlayerUpdate 事件：活跃度统计
		// ════════════════════════════════════════════════

		private static void OnPlayerUpdateEvent(object? sender, GetDataHandlers.PlayerUpdateEventArgs e)
		{
			if (e.Player == null)
				return;
			int who = e.Player.Index;
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
		//  帧驱动：直播自动切换 / 寄生保护 / 补发给目标
		// ════════════════════════════════════════════════

		private static void OnGameUpdate(EventArgs args)
		{
			_tick++;

			// 寄生保护：管理员或目标死亡/下线 → 自动退出
			if (_viewer >= 0)
			{
				var viewer = GetPlayer(_viewer);
				var target = GetPlayer(_viewTarget);
				if (viewer == null || !viewer.Active || viewer.TPlayer == null || !viewer.TPlayer.active
					|| target == null || !target.Active || target.TPlayer == null
					|| !target.TPlayer.active || target.TPlayer.dead)
				{
					StopViewing();
					return;
				}
			}

			// 补发：寄生映射后延迟 1 帧给目标回发 PlayerUpdate（原生广播 exclude 目标）
			if (_pendingRelayTick >= 0 && _tick - _pendingRelayTick >= 1)
			{
				_pendingRelayTick = -1;
				if (_viewer >= 0 && _viewTarget >= 0 && _possessMode)
				{
					try
					{
						NetMessage.SendData((int)PacketTypes.PlayerUpdate, _viewTarget, -1, null, _viewTarget);
					}
					catch { }
				}
			}

			if (_tick % 60 != 0)
				return; // 每秒检查一次
			if (!_liveMode || _viewer < 0)
				return;

			// 当前目标：死亡/离线 → 立即切换；挂机超阈值 → 切换
			bool targetDead = _viewTarget < 0 || !IsAliveTarget(_viewTarget);
			bool targetIdle = false;
			if (!targetDead)
			{
				lock (_sync)
				{
					targetIdle = !_lastActive.TryGetValue(_viewTarget, out var t)
						|| (DateTime.UtcNow - t).TotalSeconds >= _liveIdleSeconds;
				}
			}

			if (targetDead || targetIdle)
				SwitchToNextLiveTarget(targetDead);
		}

		/// <summary>直播切换下一位活跃玩家（whoAmI 递增循环，跳过死亡/离线/挂机）</summary>
		private static void SwitchToNextLiveTarget(bool currentInvalid)
		{
			int next = -1;
			int start = _viewTarget >= 0 ? _viewTarget : 0;
			for (int i = 1; i < Main.player.Length; i++)
			{
				int idx = (start + i) % Main.player.Length;
				if (!IsAliveTarget(idx))
					continue;
				lock (_sync)
				{
					if (_lastActive.TryGetValue(idx, out var t)
						&& (DateTime.UtcNow - t).TotalSeconds < _liveIdleSeconds)
					{
						next = idx;
						break;
					}
				}
			}

			var admin = GetPlayer(_viewer);
			if (next < 0)
			{
				if (currentInvalid)
				{
					admin?.SendInfoMessage("[直播] 已无在线活跃玩家，直播结束");
					StopViewing();
				}
				else
				{
					admin?.SendInfoMessage($"[直播] 暂无其它活跃玩家，保持当前观战目标（{GetPlayer(_viewTarget)?.Name}）");
				}
				return;
			}

			_viewTarget = next;
			// 广播新目标的 SyncPlayer → 下行伪装自动让管理员"变成"新目标
			try
			{
				NetMessage.SendData((int)PacketTypes.PlayerInfo, -1, -1, null, next);
			}
			catch { }
			admin?.SendInfoMessage($"[直播] 已切换到活跃玩家：{GetPlayer(next)?.Name}");
		}

		private static bool IsAliveTarget(int who)
		{
			var ts = GetPlayer(who);
			if (ts == null || !ts.Active || ts.TPlayer == null)
				return false;
			var p = Main.player[who];
			return p != null && p.active && !p.dead;
		}

		private static int FindFirstActiveTarget()
		{
			for (int i = 0; i < Main.player.Length; i++)
			{
				if (!IsAliveTarget(i))
					continue;
				lock (_sync)
				{
					if (_lastActive.TryGetValue(i, out var t)
						&& (DateTime.UtcNow - t).TotalSeconds < _liveIdleSeconds)
						return i;
				}
			}
			return -1;
		}

		// ════════════════════════════════════════════════
		//  状态切换
		// ════════════════════════════════════════════════

		/// <summary>进入观看状态（寄生/观战/直播共用）：广播目标 SyncPlayer 触发下行伪装</summary>
		private static void EnterViewing(int adminWho, int targetWho, bool possessMode, bool liveMode)
		{
			StopViewing(quiet: true);

			_viewer = adminWho;
			_viewTarget = targetWho;
			_possessMode = possessMode;
			_liveMode = liveMode;

			// 广播目标 SyncPlayer → 下行事件把发给管理员的那份伪装成"管理员自己"
			try
			{
				NetMessage.SendData((int)PacketTypes.PlayerInfo, -1, -1, null, targetWho);
			}
			catch { }

			TShock.Log.ConsoleInfo($"[Possess] 进入{(possessMode ? "寄生" : liveMode ? "直播" : "观战")}：管理员 #{adminWho} → 目标 #{targetWho}");
		}

		/// <summary>退出观看状态：恢复管理员角色显示 + 广播目标状态 + 解冻</summary>
		private static void StopViewing(bool quiet = false)
		{
			if (_viewer < 0)
				return;
			int admin = _viewer, target = _viewTarget;
			bool wasPossess = _possessMode;
			_viewer = -1;
			_viewTarget = -1;
			_possessMode = false;
			_liveMode = false;
			_pendingRelayTick = -1;

			var adminTs = GetPlayer(admin);

			// 恢复管理员自己的角色显示（发给自己的 SyncPlayer 不再伪装）
			if (adminTs != null && adminTs.TPlayer != null && adminTs.TPlayer.active)
			{
				try
				{
					NetMessage.SendData((int)PacketTypes.PlayerInfo, -1, -1, null, admin);
				}
				catch { }
			}

			// 广播目标当前状态（解冻后所有客户端——含目标自己——位置一致）
			if (target >= 0 && target < Main.player.Length)
			{
				try
				{
					NetMessage.SendData((int)PacketTypes.PlayerUpdate, -1, -1, null, target);
				}
				catch { }
			}

			if (!quiet)
				adminTs?.SendInfoMessage(wasPossess ? "[寄生] 已退出寄生模式，目标玩家恢复操作" : "[观看] 已退出观战/直播");
			TShock.Log.ConsoleInfo($"[Possess] 退出观看：管理员 #{admin}，目标 #{target}");
		}

		private static void OnServerLeave(LeaveEventArgs args)
		{
			int who = args.Who;
			if (who == _viewer || who == _viewTarget)
				StopViewing();
			lock (_sync)
			{
				_lastActive.Remove(who);
				_lastPos.Remove(who);
			}
		}

		private static TSPlayer? GetPlayer(int who)
			=> who >= 0 && who < TShock.Players.Length ? TShock.Players[who] : null;

		/// <summary>查找在线存活目标玩家</summary>
		private static TSPlayer? FindTarget(string name, TSPlayer self)
		{
			var players = TSPlayer.FindByNameOrID(name);
			if (players.Count == 0)
			{
				self.SendErrorMessage($"找不到玩家：{name}");
				return null;
			}
			if (players.Count > 1)
			{
				self.SendMultipleMatchError(players.Select(p => p.Name));
				return null;
			}
			var target = players[0];
			if (!target.Active || target.TPlayer == null || !target.TPlayer.active || target.TPlayer.dead)
			{
				self.SendErrorMessage($"玩家 {target.Name} 不在线或已死亡。");
				return null;
			}
			if (target.Index == self.Index)
			{
				self.SendErrorMessage("不能寄生/观战自己。");
				return null;
			}
			return target;
		}

		// ════════════════════════════════════════════════
		//  命令
		// ════════════════════════════════════════════════

		private void PossessCommand(CommandArgs args)
		{
			var admin = args.Player;
			if (admin == null || admin.TPlayer == null)
				return;

			if (args.Parameters.Count == 0)
			{
				admin.SendInfoMessage("用法：/possess <玩家名>  接管该玩家全部操作（目标自身冻结，只能看）");
				admin.SendInfoMessage("      /possess stop     退出寄生");
				return;
			}

			if (args.Parameters[0].Equals("stop", StringComparison.OrdinalIgnoreCase))
			{
				if (_viewer != admin.Index || !_possessMode)
				{
					admin.SendInfoMessage("你当前不在寄生模式。");
					return;
				}
				StopViewing();
				return;
			}

			if (_viewer >= 0 && _viewer != admin.Index)
			{
				var other = GetPlayer(_viewer);
				admin.SendErrorMessage($"已有管理员（{other?.Name ?? "?"}）正在寄生/观战/直播，请先让其退出。");
				return;
			}

			string name = string.Join(" ", args.Parameters);
			var target = FindTarget(name, admin);
			if (target == null)
				return;

			EnterViewing(admin.Index, target.Index, possessMode: true, liveMode: false);
			admin.SendSuccessMessage($"[寄生] 已接管玩家 {target.Name}：目标自身冻结只能观看；你的客户端已变身为目标。使用 /possess stop 退出。");
			TShock.Log.ConsoleInfo($"[Possess] {admin.Name} 开始寄生玩家 {target.Name} (#{target.Index})");
		}

		private void WatchCommand(CommandArgs args)
		{
			var admin = args.Player;
			if (admin == null || admin.TPlayer == null)
				return;

			if (args.Parameters.Count == 0)
			{
				admin.SendInfoMessage("用法：/watch <玩家名>  第一人称观战目标（客户端变身为目标，目标正常玩）");
				admin.SendInfoMessage("      /watch next     切换下一位存活玩家");
				admin.SendInfoMessage("      /watch stop     退出观战");
				return;
			}

			switch (args.Parameters[0].ToLowerInvariant())
			{
				case "stop":
					if (_viewer == admin.Index && !_possessMode)
						StopViewing();
					else
						admin.SendInfoMessage("你当前不在观战/直播模式。");
					return;
				case "next":
					if (_viewer != admin.Index || _possessMode)
					{
						admin.SendInfoMessage("请先进入观战模式（/watch 玩家名）。");
						return;
					}
					{
						int next = FindNextTarget(_viewTarget);
						if (next < 0)
						{
							admin.SendInfoMessage("[观战] 没有其它存活玩家。");
							return;
						}
						_viewTarget = next;
						try { NetMessage.SendData((int)PacketTypes.PlayerInfo, -1, -1, null, next); }
						catch { }
						admin.SendSuccessMessage($"[观战] 已切换观战目标：{GetPlayer(next)?.Name}");
					}
					return;
			}

			if (_viewer >= 0 && _viewer != admin.Index)
			{
				var other = GetPlayer(_viewer);
				admin.SendErrorMessage($"已有管理员（{other?.Name ?? "?"}）正在寄生/观战/直播，请先让其退出。");
				return;
			}

			string name = string.Join(" ", args.Parameters);
			var target = FindTarget(name, admin);
			if (target == null)
				return;

			EnterViewing(admin.Index, target.Index, possessMode: false, liveMode: false);
			admin.SendSuccessMessage($"[观战] 已变身为玩家 {target.Name} 的第一人称视角（目标正常玩）。使用 /watch stop 退出。");
		}

		private void LiveCommand(CommandArgs args)
		{
			var admin = args.Player;
			if (admin == null || admin.TPlayer == null)
				return;

			string sub = args.Parameters.Count > 0 ? args.Parameters[0].ToLowerInvariant() : "";

			// 关闭
			if (sub == "off" || sub == "stop" || sub == "0")
			{
				if (_viewer == admin.Index && _liveMode)
					StopViewing();
				else
					admin.SendInfoMessage("当前没有进行中的直播。");
				return;
			}

			// 秒数：调整挂机切换阈值
			if (sub.Length > 0 && sub != "on")
			{
				if (double.TryParse(sub, out double sec) && sec > 0)
				{
					_liveIdleSeconds = Math.Min(sec, 300);
					admin.SendSuccessMessage($"[直播] 挂机切换阈值已设为 {_liveIdleSeconds:0.#} 秒");
				}
				else
				{
					admin.SendErrorMessage("参数无效。用法：/live [on|off|秒数]");
					return;
				}
				if (_viewer == admin.Index && _liveMode)
					return;
			}

			if (_viewer >= 0 && _viewer != admin.Index)
			{
				var other = GetPlayer(_viewer);
				admin.SendErrorMessage($"已有管理员（{other?.Name ?? "?"}）正在寄生/观战/直播，请先让其退出。");
				return;
			}

			if (_viewer == admin.Index && _liveMode)
			{
				admin.SendInfoMessage($"[直播] 直播已开启（挂机 {_liveIdleSeconds:0.#} 秒自动切换，死亡立即换人）。/live off 退出");
				return;
			}

			int first = FindFirstActiveTarget();
			if (first < 0)
			{
				admin.SendInfoMessage("[直播] 当前没有在线活跃玩家，无法开始直播。");
				return;
			}

			EnterViewing(admin.Index, first, possessMode: false, liveMode: true);
			admin.SendSuccessMessage($"[直播] 开始直播：自动观战活跃玩家（挂机 {_liveIdleSeconds:0.#} 秒切换，死亡立即换人）。当前：{GetPlayer(first)?.Name}");
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
	}
}
