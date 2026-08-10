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
	///      （仅服务端天气生成后广播；客户端内部调用均为 clientOnly:true）→ 必为恶意 → 直接丢弃
	///   2. 其他粒子类型：正常客户端武器特效等请求频率极低 → 按频率限流（默认 20 个/秒/玩家）
	///   3. 累计违规达到阈值 → 踢出
	///
	/// 实现：MonoMod RuntimeDetour 挂钩 NetManager.Read（82 号包服务端分发总入口）。
	///   - 只捕获客户端 → 服务端的 82 号包（服务端 Broadcast 直接写 socket，不经 Read）
	///   - moduleId != Particles(8) 的模块（Text/Ping/Liquid 等）原样放行
	///
	/// /lightning 指令：服务端广播 StormLightning（全服可见或仅目标玩家可见），
	///   作为管理员受控入口，同时验证反制未误伤服务端广播通道。
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

		// 玩家索引 → 最近 1 秒内的粒子请求时间戳
		private static readonly Dictionary<int, List<DateTime>> _timestamps = new Dictionary<int, List<DateTime>>();
		// 玩家索引 → 累计违规次数
		private static readonly Dictionary<int, int> _violations = new Dictionary<int, int>();
		private static readonly object SyncLock = new object();

		/// <summary>NetManager.Read(BinaryReader, int, int) 原始委托签名（实例方法，首个参数为 this）</summary>
		private delegate void OrigNetManagerRead(NetManager self, BinaryReader reader, int userId, int readLength);

		// ════════════════════════════════════════════
		//  生命周期
		// ════════════════════════════════════════════

		public static void Initialize()
		{
			if (_initialized)
				return;

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
				_initialized = true;
				TShock.Log.ConsoleInfo($"[ParticleGuard] 粒子防线已启用（NetParticlesModule ID={_particlesModuleId}）");
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[ParticleGuard] Hook 注册失败: {ex.Message}");
			}
		}

		public static void Dispose()
		{
			if (!_initialized)
				return;

			try { _hook?.Dispose(); }
			catch { }
			_hook = null;
			_initialized = false;

			lock (SyncLock)
			{
				_timestamps.Clear();
				_violations.Clear();
			}
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
		//  /lightning 指令：服务端广播闪电
		// ════════════════════════════════════════════

		/// <summary>
		/// /lightning <玩家> [数量=1] [all|self]
		///   在目标玩家附近召唤闪电。all（默认）= 全服可见；self = 仅目标玩家可见。
		/// 服务端广播走 NetManager.Broadcast / SendToClient，不经过 NetManager.Read，
		/// 因此不受防线拦截影响 —— 同时作为反制是否误伤广播通道的验证工具。
		/// </summary>
		public static void LightningCommand(CommandArgs args)
		{
			if (args.Parameters.Count < 1)
			{
				args.Player.SendInfoMessage("用法: /lightning <玩家> [数量=1] [all|self]  —— 在目标玩家处召唤闪电");
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

			// 数量：默认 1，钳制 1~50（防止管理员指令自身变成 DDoS 工具）
			int count = 1;
			if (args.Parameters.Count >= 2 && !int.TryParse(args.Parameters[1], out count))
			{
				args.Player.SendErrorMessage("数量必须是整数。");
				return;
			}
			count = Math.Clamp(count, 1, 50);

			// 可见性：all（默认）/ self
			bool targetOnly = args.Parameters.Count >= 3
				&& args.Parameters[2].Equals("self", StringComparison.OrdinalIgnoreCase);

			// 复刻 TerraAngel LightningTool 的随机参数生成逻辑
			var origin = target.TPlayer.position;
			int sent = 0;
			for (int i = 0; i < count; i++)
			{
				int direction = Main.rand.Next(0, 2) == 0 ? -1 : 1;
				var settings = new ParticleOrchestraSettings
				{
					PositionInWorld = origin + new Vector2(Main.rand.Next(60 * 16) * direction, 0),
					UniqueInfoPiece = (int)new Color(Main.rand.Next(0, 255), Main.rand.Next(0, 255), Main.rand.Next(0, 255)).PackedValue,
					MovementVector = new Vector2(Main.rand.Next(0, 1145), 0f),
					IndexOfPlayerWhoInvokedThis = (byte)target.Index
				};

				var packet = NetParticlesModule.Serialize(ParticleOrchestraType.StormLightning, settings);
				if (targetOnly)
					NetManager.Instance.SendToClient(packet, target.Index);
				else
					NetManager.Instance.Broadcast(packet);
				sent++;
			}

			args.Player.SendSuccessMessage(
				$"已在玩家 {target.Name} 处召唤 {sent} 道闪电（{(targetOnly ? "仅该玩家可见" : "全服可见")}）。");
			TShock.Log.ConsoleInfo($"[ParticleGuard] {args.Player.Name} 在 {target.Name} 处召唤 {sent} 道闪电");
		}
	}
}
