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
	///   2. 合法高频类型（ChlorophyteLeafCrystalShot / TrueExcalibur 等）：原版客户端在弹幕存续期间
	///      每帧请求的持续特效 → 走独立高阈值限流（默认 500 个/秒/玩家）
	///   3. 其他粒子类型：正常客户端武器特效等请求频率低 → 按频率限流（默认 20 个/秒/玩家）
	///   4. 除恶意类型外，所有超限请求一律「仅丢弃 + 记日志」，绝不踢出 ——
	///      合法特效（弹幕持续粒子、换装、武器剑气）被插件批量弹幕放大频率是正常现象，踢出会造成大规模误伤
	///
	/// 实现：MonoMod RuntimeDetour 挂钩 NetManager.Read（82 号包服务端分发总入口）。
	///   - 只捕获客户端 → 服务端的 82 号包（服务端 Broadcast 直接写 socket，不经 Read）
	///   - moduleId != Particles(8) 的模块（Text/Ping/Liquid 等）原样放行
	///
	/// /lightning 指令：持续劈闪模式 —— 用户自定义时长（秒 s / 帧 f）与每帧数量，
	///   自动每帧发送指定数量，直到时长结束。默认仅目标玩家可见（self）。
	///   服务端广播走 NetManager.Broadcast / SendToClient 不经 NetManager.Read，
	///   不受防线拦截影响，同时作为反制是否误伤广播通道的验证工具。
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

		/// <summary>合法高频类型（如叶绿水晶矢）的每秒上限：原版客户端每帧请求持续特效，
		/// 多弹幕同时存在时轻松超过普通限流；超限仅丢弃，不累计踢出</summary>
		public const int HighFrequencyLimitPerSecond = 500;

		/// <summary>
		/// 恶意粒子类型：原版客户端从不主动向服务端请求的类型。
		/// 客户端发来此类请求 = 100% 恶意 → 直接拦截并踢出（不做累计降级）。
		/// </summary>
		private static readonly HashSet<byte> MaliciousTypes = new HashSet<byte>
		{
			(byte)ParticleOrchestraType.StormLightning,       // 61
			(byte)ParticleOrchestraType.StormlightningWindup  // 62
		};

		/// <summary>
		/// 合法高频粒子类型：原版客户端在弹幕存续期间每帧请求的持续特效（clientOnly:false），
		/// 如叶绿水晶矢(227) 的 ChlorophyteLeafCrystalShot、海龟套弹幕触发的 TrueExcalibur 剑气 ——
		/// 60fps 下单弹幕即约 60/s，插件批量弹幕（海龟套每 3 秒 15~25 个）频率更高，远超普通限流 20/s。
		/// 此类请求是正常游戏行为 → 走独立高阈值限流，超限仅丢弃，绝不踢出。
		/// </summary>
		private static readonly HashSet<byte> HighFrequencyTypes = new HashSet<byte>
		{
			(byte)ParticleOrchestraType.ChlorophyteLeafCrystalShot,  // 17：叶绿水晶矢粒子（抓包日志实证）
			(byte)ParticleOrchestraType.TrueExcalibur                // 14：真断钢剑/真永夜刃剑气（海龟套弹幕触发，抓包日志实证）
		};

		private static Hook? _hook;
		private static bool _initialized;
		private static ushort _particlesModuleId = ushort.MaxValue;
		private static TerrariaPlugin? _plugin;

		// 玩家索引 → 最近 1 秒内的粒子请求时间戳（普通类型）
		private static readonly Dictionary<int, List<DateTime>> _timestamps = new Dictionary<int, List<DateTime>>();
		// 玩家索引 → 最近 1 秒内的高频类型请求时间戳（独立统计，避免高频类型污染普通类型限流）
		private static readonly Dictionary<int, List<DateTime>> _highFreqTimestamps = new Dictionary<int, List<DateTime>>();
		private static readonly object SyncLock = new object();

		/// <summary>NetManager.Read(BinaryReader, int, int) 原始委托签名（实例方法，首个参数为 this）</summary>
		private delegate void OrigNetManagerRead(NetManager self, BinaryReader reader, int userId, int readLength);

		// ════════════════════════════════════════════
		//  持续劈闪配置
		// ════════════════════════════════════════════

		/// <summary>帧率（GameUpdate 约每秒触发次数）</summary>
		private const int FrameRate = 60;

		/// <summary>默认时长：120 帧 = 2 秒</summary>
		private const int DefaultDurationFrames = 120;

		/// <summary>每帧默认数量</summary>
		private const int DefaultPerFrame = 5;

		/// <summary>每帧数量上限（100）</summary>
		private const int MaxPerFrame = 100;

		/// <summary>时长上限：360000 帧 = 6000 秒 = 100 分钟</summary>
		private const int MaxFrames = 360000;

		/// <summary>总道数兜底上限（帧 × 每帧），防误触导致全服广播洪泛</summary>
		private const int MaxTotalBolts = 100000;

		/// <summary>当前进行中的劈闪会话（服务器主线程访问，无需锁）</summary>
		private static LightningSession? _session;

		private class LightningSession
		{
			public int TargetIndex;     // 目标玩家索引
			public bool TargetOnly;     // true=仅目标玩家可见, false=全服可见
			public int FramesLeft;      // 剩余帧数
			public int PerFrame;        // 每帧数量
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
				_highFreqTimestamps.Clear();
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
			bool isHighFrequency = !isMalicious && HighFrequencyTypes.Contains(particleType);

			// ═══ 恶意类型：100% 恶意 → 丢弃并立即踢出 ═══
			if (isMalicious)
			{
				HandleViolation(userId, particleType);
				return;
			}

			// ═══ 合法高频类型（如叶绿水晶矢）：独立高阈值限流，超限仅丢弃不踢出 ═══
			if (isHighFrequency)
			{
				bool overLimit;
				lock (SyncLock)
				{
					if (!_highFreqTimestamps.TryGetValue(userId, out var list))
					{
						list = new List<DateTime>();
						_highFreqTimestamps[userId] = list;
					}

					var now = DateTime.Now;
					list.Add(now);
					list.RemoveAll(t => (now - t).TotalSeconds > 1.0);
					overLimit = list.Count > HighFrequencyLimitPerSecond;
				}

				if (overLimit)
				{
					// 超限：丢弃该请求（不调 orig → 不广播），仅记日志，绝不踢出
					var name = userId >= 0 && userId < TShock.Players.Length ? TShock.Players[userId]?.Name : null;
					TShock.Log.ConsoleInfo($"[ParticleGuard] 丢弃 {name ?? "#" + userId} 的超高频粒子请求 Type={particleType}（> {HighFrequencyLimitPerSecond}/s）");
					return;
				}

				reader.BaseStream.Position = start;
				orig(self, reader, userId, readLength);
				return;
			}

			// ═══ 普通类型：按频率限流（20/s），超限仅丢弃不踢出 ═══
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

				shouldBlock = list.Count > MaxParticlesPerSecond;
			}

			if (!shouldBlock)
			{
				reader.BaseStream.Position = start;
				orig(self, reader, userId, readLength);
				return;
			}

			// ═══ 普通类型超频：丢弃该请求（不调 orig → 不广播），仅记日志，绝不踢出 ═══
			// 合法特效（武器剑气、换装、弹幕粒子等）被插件批量弹幕放大频率是正常现象，不视为作弊
			var p = userId >= 0 && userId < TShock.Players.Length ? TShock.Players[userId] : null;
			TShock.Log.ConsoleInfo($"[ParticleGuard] 丢弃 {p?.Name ?? "#" + userId} 的超频粒子请求 Type={particleType}（> {MaxParticlesPerSecond}/s）");
		}

		private static void HandleViolation(int userId, byte particleType)
		{
			var player = userId >= 0 && userId < TShock.Players.Length ? TShock.Players[userId] : null;
			string name = player?.Name ?? $"#{userId}";

			string typeName = Enum.IsDefined(typeof(ParticleOrchestraType), particleType)
				? $"{Enum.GetName(typeof(ParticleOrchestraType), particleType)}({particleType})"
				: $"未知类型({particleType})";

			TShock.Log.ConsoleInfo($"[ParticleGuard] 玩家 {name} 发送恶意粒子数据包 Type={typeName}，踢出服务器");
			try
			{
				player?.Kick($"发送恶意粒子数据包（{typeName}）", true);
			}
			catch { }
		}

		// ════════════════════════════════════════════
		//  /lightning 指令：持续劈闪
		// ════════════════════════════════════════════

		/// <summary>
		/// /lightning <玩家> [时长=120f] [每帧=5] [all|self]
		/// /lightning stop
		///
		/// 持续劈闪模式：
		///   时长    —— 帧数后缀 f（如 120f = 120 帧 = 2 秒），秒数后缀 s 或无后缀（如 10s / 10 = 10 秒）
		///   每帧    —— 每帧发送的闪电数量，上限 100
		///   可见性  —— self（默认，仅目标玩家可见）/ all（全服可见）
		/// 例：/lightning 玩家 120f 5 self = 劈 2 秒，每帧 5 道，仅该玩家可见
		/// 服务端广播走 NetManager.Broadcast / SendToClient，不经过 NetManager.Read，
		/// 不受防线拦截影响 —— 同时作为反制是否误伤广播通道的验证工具。
		/// </summary>
		public static void LightningCommand(CommandArgs args)
		{
			if (args.Parameters.Count < 1)
			{
				args.Player.SendInfoMessage(
					"用法: /lightning <玩家> [时长=120f] [每帧=5] [all|self]  —— 持续劈闪；/lightning stop 停止");
				args.Player.SendInfoMessage(
					"  时长: 帧数加 f（120f=2秒）或秒数（10s/10=10秒）；每帧上限 100；默认仅目标玩家可见（self）");
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
				args.Player.SendSuccessMessage($"已停止劈闪（剩余 {old.FramesLeft * old.PerFrame} 道未劈）。");
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

			// 时长：默认 120f（2 秒），支持 f（帧）/ s 或无后缀（秒）
			int frames = DefaultDurationFrames;
			if (args.Parameters.Count >= 2)
			{
				if (!TryParseDuration(args.Parameters[1], out frames))
				{
					args.Player.SendErrorMessage("时长格式无效。示例: 120f（帧）/ 10s 或 10（秒）。");
					return;
				}
			}
			frames = Math.Clamp(frames, 1, MaxFrames);

			// 每帧数量：默认 5，钳制 1~MaxPerFrame
			int perFrame = DefaultPerFrame;
			if (args.Parameters.Count >= 3 && !int.TryParse(args.Parameters[2], out perFrame))
			{
				args.Player.SendErrorMessage("每帧数量必须是整数。");
				return;
			}
			perFrame = Math.Clamp(perFrame, 1, MaxPerFrame);

			// 可见性：默认 self（仅目标玩家可见），显式 all 才全服可见
			bool targetOnly = true;
			if (args.Parameters.Count >= 4)
			{
				var vis = args.Parameters[3].ToLowerInvariant();
				if (vis == "all")
					targetOnly = false;
				else if (vis != "self")
				{
					args.Player.SendErrorMessage("可见性参数无效，仅支持 self / all。");
					return;
				}
			}

			// 总道数兜底：帧 × 每帧超过上限时压缩帧数
			long totalBolts = (long)frames * perFrame;
			if (totalBolts > MaxTotalBolts)
			{
				frames = (int)Math.Max(1, MaxTotalBolts / perFrame);
				totalBolts = (long)frames * perFrame;
				args.Player.SendInfoMessage($"总道数超过上限 {MaxTotalBolts}，时长已压缩为 {frames} 帧。");
			}

			// 替换旧会话，启动新劈闪
			_session = new LightningSession
			{
				TargetIndex = target.Index,
				TargetOnly = targetOnly,
				FramesLeft = frames,
				PerFrame = perFrame
			};

			double seconds = frames / (double)FrameRate;
			args.Player.SendSuccessMessage(
				$"开始劈闪玩家 {target.Name}：时长 {frames} 帧（{seconds:F1} 秒），每帧 {perFrame} 道（共 {totalBolts} 道，≈{perFrame * FrameRate} 道/秒）。" +
				(targetOnly ? "（仅该玩家可见）" : "（全服可见）"));
			if (perFrame > 20)
				args.Player.SendInfoMessage("提示: 每帧 >20 道时客户端渲染负担较重，建议降低或使用 self。");
			TShock.Log.ConsoleInfo($"[ParticleGuard] {args.Player.Name} 启动劈闪: 目标={target.Name}, 帧={frames}, 每帧={perFrame}, 可见={(!targetOnly ? "all" : "self")}");
		}

		/// <summary>
		/// 解析时长字符串 → 帧数。
		///   120f → 120 帧；10s / 10 → 10 秒 = 600 帧。
		/// </summary>
		private static bool TryParseDuration(string raw, out int frames)
		{
			frames = 0;
			if (string.IsNullOrWhiteSpace(raw))
				return false;

			var t = raw.Trim();
			if (t.EndsWith("f", StringComparison.OrdinalIgnoreCase))
			{
				if (!int.TryParse(t[..^1], out frames) || frames <= 0)
					return false;
				return true;
			}

			string numPart = t.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? t[..^1] : t;
			if (!int.TryParse(numPart, out int sec) || sec <= 0)
				return false;

			frames = sec * FrameRate;
			return true;
		}

		/// <summary>
		/// 游戏主循环驱动：每帧发送指定数量的闪电，直到时长（帧数）耗尽。
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

			for (int i = 0; i < session.PerFrame; i++)
				SendLightningOnce(target, session.TargetOnly);
			session.FramesLeft--;

			if (session.FramesLeft <= 0)
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
