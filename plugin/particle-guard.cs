using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using MonoMod.RuntimeDetour;
using Terraria;
using Terraria.GameContent.Drawing;
using Terraria.GameContent.NetModules;
using Terraria.Net;
using TerrariaApi.Server;
using TShockAPI;

namespace TShockData
{
	/// <summary>
	/// 粒子防线（ParticleGuard）：
	///
	/// 背景（基于 Terraria 1.4.4.x 原版核心源码实证）：
	///   粒子网络通道只有一条 —— 82 号包（LoadNetModule）+ NetParticlesModule（模块 ID=8）。
	///   服务端收到客户端粒子请求后，在 NetParticlesModule.Deserialize 中无条件
	///   NetManager.Broadcast 广播给所有其他客户端（广播放大）。
	///   TerraAngel 的 LightningTool 正是伪造 StormLightning(61) 粒子洪泛，
	///   1 个客户端 → 服务端广播 → N 个客户端渲染海量闪电粒子 → 客户端崩溃。
	///
	/// 拦截策略（零误伤）：
	///   1. StormLightning / StormlightningWindup：原版客户端从不主动向服务端请求
	///      （仅服务端天气生成后广播；客户端内部调用均为 clientOnly:true）→ 必为恶意 → 直接丢弃并踢出
	///   2. 其他粒子类型：正常客户端武器特效等请求频率极低 → 按频率限流（默认 20 个/秒/玩家）
	///   3. 累计违规达到阈值 → 踢出
	///
	/// 实现：MonoMod RuntimeDetour 挂钩 NetManager.Read（82 号包服务端分发总入口）。
	///   - 只捕获客户端 → 服务端的 82 号包（服务端 Broadcast 直接写 socket，不经 Read）
	///   - moduleId != Particles(8) 的模块（Text/Ping/Liquid 等）原样放行
	///
	/// /lightning 指令：持续劈闪模式 —— 用户指定总数，每帧发 5 道（≈300 道/秒），
	///   自动持续劈直到劈完。服务端广播走 NetManager.Broadcast / SendToClient 不经
	///   NetManager.Read，不受防线拦截影响，同时作为反制是否误伤广播通道的验证工具。
	/// </summary>
	public static class ParticleGuard
	{
		// ════════════════════════════════════════════
		//  拦截配置
		// ════════════════════════════════════════════

		/// <summary>总开关</summary>
		public static bool Enabled = true;

		/// <summary>每秒允许的合法粒子请求上限（正常客户端武器特效等远低于此）</summary>
		public const int MaxParticlesPerSecond = 20;

		/// <summary>非恶意类型因超频被拦截的累计次数达到此值 → 踢出</summary>
		public const int KickAfterViolations = 5;

		/// <summary>
		/// 恶意粒子类型：原版客户端从不主动向服务端请求的类型。
		/// 客户端发来此类请求 = 100% 恶意 → 直接拦截并踢出（不做累计降级）。
		/// </summary>
		private static readonly HashSet<byte> MaliciousTypes = new HashSet<byte>
		{
			(byte)ParticleOrchestraType.StormLightning,       // 61
			(byte)ParticleOrchestraType.StormlightningWindup  // 62
		};

		private static Hook? _hook;
		private static bool _initialized;
		private static ushort _particlesModuleId = ushort.MaxValue;
		private static TerrariaPlugin? _plugin;

		// 玩家索引 → 最近 1 秒内的粒子请求时间戳
		private static readonly Dictionary<int, List<DateTime>> _timestamps = new Dictionary<int, List<DateTime>>();
		// 玩家索引 → 累计违规次数
		private static readonly Dictionary<int, int> _violations = new Dictionary<int, int>();
		private static readonly object SyncLock = new object();

		/// <summary>NetManager.Read(BinaryReader, int, int) 原始委托签名（实例方法，首个参数为 this）</summary>
		private delegate void OrigNetManagerRead(NetManager self, BinaryReader reader, int userId, int readLength);

		// ════════════════════════════════════════════
		//  持续劈闪配置
		// ════════════════════════════════════════════

		/// <summary>每帧发送的闪电数量（GameUpdate 约每秒 60 帧 → 每秒约 300 道）</summary>
		private const int PerFrameCount = 5;

		/// <summary>总数上限（防误触，300 道/秒下 100000 ≈ 5.5 分钟）</summary>
		private const int MaxTotal = 100000;

		/// <summary>当前进行中的劈闪会话（服务器主线程访问，无需锁）</summary>
		private static LightningSession? _session;

		private class LightningSession
		{
			public int TargetIndex;     // 目标玩家索引
			public bool TargetOnly;     // true=仅目标玩家可见, false=全服可见
			public int Remaining;       // 剩余待劈数量
		}

		// ════════════════════════════════════════════
		//  生命周期
		// ════════════════════════════════════════════

		public static void Initialize(TerrariaPlugin plugin)
		{
			if (_initialized)
				return;

			_plugin = plugin;

			// 模块 ID 在 NetworkInitializer 中按注册顺序分配（Particles = 8），运行时取最稳
			try
			{
				_particlesModuleId = NetManager.Instance.GetId<NetParticlesModule>();
			}
			catch
			{
				_particlesModuleId = 8;
			}

			var method = typeof(NetManager).GetMethod("Read",
				BindingFlags.Public | BindingFlags.Instance,
				null,
				new[] { typeof(BinaryReader), typeof(int), typeof(int) },
				null);

			if (method == null)
			{
				TShock.Log.ConsoleError("[ParticleGuard] 未找到 NetManager.Read 方法，粒子防线未启用");
				return;
			}

			try
			{
				_hook = new Hook(method, OnNetManagerRead);
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[ParticleGuard] Hook 注册失败: {ex.Message}");
				return;
			}

			// 持续劈闪的帧驱动
			ServerApi.Hooks.GameUpdate.Register(plugin, OnGameUpdate);

			_initialized = true;
			TShock.Log.ConsoleInfo($"[ParticleGuard] 粒子防线已启用（NetParticlesModule ID={_particlesModuleId}）");
		}

		public static void Dispose()
		{
			if (!_initialized)
				return;

			try { _hook?.Dispose(); }
			catch { }
			_hook = null;

			if (_session != null)
			{
				_session = null;
				TShock.Log.ConsoleInfo("[ParticleGuard] 劈闪会话已终止");
			}
			if (_plugin != null)
				ServerApi.Hooks.GameUpdate.Deregister(_plugin, OnGameUpdate);
			_plugin = null;

			lock (SyncLock)
			{
				_timestamps.Clear();
				_violations.Clear();
			}
			_initialized = false;
			TShock.Log.ConsoleInfo("[ParticleGuard] 粒子防线已卸载");
		}

		// ════════════════════════════════════════════
		//  拦截逻辑
		// ════════════════════════════════════════════

		private static void OnNetManagerRead(OrigNetManagerRead orig, NetManager self, BinaryReader reader, int userId, int readLength)
		{
			long start = reader.BaseStream.Position;
			ushort moduleId = reader.ReadUInt16();

			// 非粒子模块 / 防线关闭 → 原样放行
			if (!Enabled || moduleId != _particlesModuleId)
			{
				reader.BaseStream.Position = start;
				orig(self, reader, userId, readLength);
				return;
			}

			// 粒子模块：读取粒子类型（moduleId 之后 1 字节）
			byte particleType = reader.ReadByte();
			bool isMalicious = MaliciousTypes.Contains(particleType);

			// 频率限流：恶意类型必然拦截；合法类型按每秒数量
			bool shouldBlock;
			lock (SyncLock)
			{
				if (!_timestamps.TryGetValue(userId, out var list))
				{
					list = new List<DateTime>();
					_timestamps[userId] = list;
				}

				var now = DateTime.Now;
				list.Add(now);
				list.RemoveAll(t => (now - t).TotalSeconds > 1.0);

				shouldBlock = isMalicious || list.Count > MaxParticlesPerSecond;
			}

			if (!shouldBlock)
			{
				reader.BaseStream.Position = start;
				orig(self, reader, userId, readLength);
				return;
			}

			// ═══ 违规：丢弃整个粒子模块（不调 orig → 不广播）═══
			// 恶意闪电类型 → 立即踢出；其他类型超频 → 记违规，累计达阈值才踢
			HandleViolation(userId, particleType, kickImmediately: isMalicious);
		}

		private static void HandleViolation(int userId, byte particleType, bool kickImmediately)
		{
			int violations;
			lock (SyncLock)
			{
				violations = _violations.TryGetValue(userId, out var v) ? v : 0;
				violations++;
				_violations[userId] = violations;
			}

			var player = userId >= 0 && userId < TShock.Players.Length ? TShock.Players[userId] : null;
			string name = player?.Name ?? $"#{userId}";

			string typeName = Enum.IsDefined(typeof(ParticleOrchestraType), particleType)
				? $"{Enum.GetName(typeof(ParticleOrchestraType), particleType)}({particleType})"
				: $"未知类型({particleType})";

			TShock.Log.ConsoleInfo($"[ParticleGuard] 拦截 {name} 的异常粒子请求 Type={typeName}" +
				(kickImmediately ? $"，恶意类型，立即踢出" : $"（超频违规 {violations}/{KickAfterViolations}）"));

			// 恶意闪电类型：直接踢出（无需累计）；其他类型：累计达阈值踢出
			if (kickImmediately || violations >= KickAfterViolations)
			{
				TShock.Log.ConsoleInfo($"[ParticleGuard] 玩家 {name} 发送恶意粒子数据包，踢出服务器");
				try
				{
					player?.Kick($"发送恶意粒子数据包（{typeName}）", true);
				}
				catch { }

				lock (SyncLock)
				{
					_violations.Remove(userId);
					_timestamps.Remove(userId);
				}
			}
		}

		// ════════════════════════════════════════════
		//  /lightning 指令：持续劈闪
		// ════════════════════════════════════════════

		/// <summary>
		/// /lightning <玩家> [总数=100] [all|self]
		/// /lightning stop
		///   持续劈闪模式：指定总数后自动每帧发 5 道（≈300 道/秒）劈向目标玩家，
		///   直到劈完指定总数。all（默认）= 全服可见；self = 仅目标玩家可见。
		/// 服务端广播走 NetManager.Broadcast / SendToClient，不经过 NetManager.Read，
		/// 不受防线拦截影响 —— 同时作为反制是否误伤广播通道的验证工具。
		/// </summary>
		public static void LightningCommand(CommandArgs args)
		{
			if (args.Parameters.Count < 1)
			{
				args.Player.SendInfoMessage("用法: /lightning <玩家> [总数=100] [all|self]  —— 持续劈闪；/lightning stop 停止");
				return;
			}

			// stop：停止当前劈闪
			if (args.Parameters[0].Equals("stop", StringComparison.OrdinalIgnoreCase))
			{
				if (_session == null)
				{
					args.Player.SendInfoMessage("当前没有进行中的劈闪。");
					return;
				}
				var old = _session;
				_session = null;
				args.Player.SendSuccessMessage($"已停止劈闪（剩余 {old.Remaining} 道未劈）。");
				TShock.Log.ConsoleInfo($"[ParticleGuard] {args.Player.Name} 停止了劈闪");
				return;
			}

			var players = TSPlayer.FindByNameOrID(args.Parameters[0]);
			if (players.Count == 0)
			{
				args.Player.SendErrorMessage("玩家不存在。");
				return;
			}
			if (players.Count > 1)
			{
				args.Player.SendMultipleMatchError(players.Select(p => p.Name));
				return;
			}

			var target = players[0];
			if (!target.Active)
			{
				args.Player.SendErrorMessage($"玩家 {target.Name} 不在线，无法在其位置召唤闪电。");
				return;
			}

			// 总数：默认 100，钳制 1~MaxTotal
			int total = 100;
			if (args.Parameters.Count >= 2 && !int.TryParse(args.Parameters[1], out total))
			{
				args.Player.SendErrorMessage("总数必须是整数。");
				return;
			}
			total = Math.Clamp(total, 1, MaxTotal);

			// 可见性：all（默认）/ self
			bool targetOnly = args.Parameters.Count >= 3
				&& args.Parameters[2].Equals("self", StringComparison.OrdinalIgnoreCase);

			// 替换旧会话，启动新劈闪
			_session = new LightningSession
			{
				TargetIndex = target.Index,
				TargetOnly = targetOnly,
				Remaining = total
			};

			double seconds = total / (double)(PerFrameCount * 60);
			args.Player.SendSuccessMessage(
				$"开始劈闪玩家 {target.Name}：共 {total} 道，每帧 {PerFrameCount} 道（≈{PerFrameCount * 60} 道/秒），预计 {seconds:F1} 秒劈完。" +
				(targetOnly ? "（仅该玩家可见）" : "（全服可见）"));
			TShock.Log.ConsoleInfo($"[ParticleGuard] {args.Player.Name} 启动劈闪: 目标={target.Name}, 总数={total}, 可见={(!targetOnly ? "all" : "self")}");
		}

		/// <summary>
		/// 游戏主循环驱动：每帧发送 PerFrameCount 道闪电，直到劈完总数。
		/// GameUpdate 约每秒触发 60 次（每 tick 一次）。
		/// </summary>
		private static void OnGameUpdate(EventArgs args)
		{
			var session = _session;
			if (session == null)
				return;

			// 目标下线 → 自动停止
			var target = session.TargetIndex >= 0 && session.TargetIndex < TShock.Players.Length
				? TShock.Players[session.TargetIndex]
				: null;
			if (target == null || !target.Active)
			{
				_session = null;
				TShock.Log.ConsoleInfo($"[ParticleGuard] 劈闪目标 #{session.TargetIndex} 已下线，自动停止");
				return;
			}

			int toSend = Math.Min(PerFrameCount, session.Remaining);
			for (int i = 0; i < toSend; i++)
				SendLightningOnce(target, session.TargetOnly);
			session.Remaining -= toSend;

			if (session.Remaining <= 0)
			{
				_session = null;
				TShock.Log.ConsoleInfo("[ParticleGuard] 劈闪完成");
			}
		}

		/// <summary>发送单道闪电（复刻 TerraAngel LightningTool 的随机参数生成逻辑）</summary>
		private static void SendLightningOnce(TSPlayer target, bool targetOnly)
		{
			int direction = Main.rand.Next(0, 2) == 0 ? -1 : 1;
			var settings = new ParticleOrchestraSettings
			{
				PositionInWorld = target.TPlayer.position + new Vector2(Main.rand.Next(60 * 16) * direction, 0),
				UniqueInfoPiece = (int)new Color(Main.rand.Next(0, 255), Main.rand.Next(0, 255), Main.rand.Next(0, 255)).PackedValue,
				MovementVector = new Vector2(Main.rand.Next(0, 1145), 0f),
				IndexOfPlayerWhoInvokedThis = (byte)target.Index
			};

			var packet = NetParticlesModule.Serialize(ParticleOrchestraType.StormLightning, settings);
			if (targetOnly)
				NetManager.Instance.SendToClient(packet, target.Index);
			else
				NetManager.Instance.Broadcast(packet);
		}
	}
}
