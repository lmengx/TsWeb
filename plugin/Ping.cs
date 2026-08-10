using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;

namespace TShockData
{
	/// <summary>
	/// ping 命令：测量玩家到服务器的网络延迟。
	///
	/// 原理（Terraria 1.4.5 官方 Ping 协议）：
	///   - 包 154（PacketTypes.Ping）是空包（0 字节负载），用于延迟探测与心跳。
	///   - 客户端会周期性（约每 10 秒）主动向服务器发送 Ping(154)，
	///     服务器收到后回 Ping(154)，客户端据此计算并显示自己的延迟。
	///   - 本模块向目标客户端发送 Ping(154) 探测包：若客户端回显 Ping(154)，
	///     则按"发送-接收"时间差计算往返延迟（RTT）；若客户端不回显，
	///     则退化为被动心跳监测，报告客户端最近心跳间隔作为连接参考。
	///
	/// 注意：旧的"发 RemoveItemOwner(39) 等 ItemOwner(22) 回包"机制
	///       在 Terraria 1.4.5 已失效（1.4.5 客户端不再对 39 号包回发 22 号包），
	///       抓包实测客户端仅周期性发送 Ping(154)，故旧机制整体移除。
	///
	/// 用法：
	///   /ping          —— 测量自己到服务器的延迟（默认 5 次）
	///   /ping 玩家名    —— 管理员（tshock.admin）测量指定玩家的延迟
	/// </summary>
	public static class Ping
	{
		/// <summary>默认测量次数</summary>
		private const int DefaultCount = 5;

		/// <summary>两次测量之间的间隔（tick，60 tick/秒 ≈ 200ms）</summary>
		private const int IntervalTicks = 12;

		/// <summary>单次测量超时（毫秒），超过则认为该次无响应</summary>
		private const double TimeoutMs = 3000;

		/// <summary>Terraria 1.4.5 Ping 包号（PacketTypes.Ping，空包）</summary>
		private const int PingPacketId = 154;

		private static readonly object SyncLock = new object();
		private static readonly Dictionary<TSPlayer, PingSession> Sessions = new Dictionary<TSPlayer, PingSession>();
		private static readonly Dictionary<TSPlayer, List<DateTime>> Heartbeats = new Dictionary<TSPlayer, List<DateTime>>();
		private static long _tick;

		/// <summary>一次完整的延迟测量会话</summary>
		private class PingSession
		{
			public TSPlayer Target;                     // 被测量的玩家
			public TSPlayer Requester;                  // 发起命令的玩家（可能等于 Target）
			public int Total;                           // 计划测量总次数
			public int Remaining;                       // 剩余测量次数
			public List<double> Results = new List<double>(); // 每次测量的延迟结果(ms)
			public DateTime SendTime;                   // 最近一次发包时间
			public long NextSendTick;                   // 计划发送下一包的时间点(tick)，0 表示正在等待回复
		}

		public static void Initialize(TerrariaPlugin plugin)
		{
			ServerApi.Hooks.NetGetData.Register(plugin, OnGetData, int.MaxValue);
			ServerApi.Hooks.GameUpdate.Register(plugin, OnGameUpdate);
			TShock.Log.ConsoleInfo("[TSWeb] Ping 模块已初始化（Ping 协议 154）");
		}

		public static void Dispose(TerrariaPlugin plugin)
		{
			lock (SyncLock)
			{
				Sessions.Clear();
				Heartbeats.Clear();
			}
			ServerApi.Hooks.NetGetData.Deregister(plugin, OnGetData);
			ServerApi.Hooks.GameUpdate.Deregister(plugin, OnGameUpdate);
			TShock.Log.ConsoleInfo("[TSWeb] Ping 模块已释放");
		}

		public static void PingCommand(CommandArgs args)
		{
			TSPlayer target;
			bool queryOther = args.Parameters.Count > 0;

			if (queryOther)
			{
				// 查询指定玩家 → 需要 tshock.admin 权限
				if (!args.Player.HasPermission("tshock.admin"))
				{
					args.Player.SendErrorMessage("你没有权限查询其他玩家的延迟。");
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

				target = players[0];
				if (!target.Active)
				{
					args.Player.SendErrorMessage($"玩家 {target.Name} 不在线，无法测量延迟。");
					return;
				}
			}
			else
			{
				if (!args.Player.RealPlayer)
				{
					args.Player.SendErrorMessage("该命令只能由游戏内玩家使用。");
					return;
				}
				target = args.Player;
			}

			lock (SyncLock)
			{
				if (Sessions.ContainsKey(target))
				{
					args.Player.SendInfoMessage($"玩家 {target.Name} 的延迟正在测量中，请稍候...");
					return;
				}

				Sessions[target] = new PingSession
				{
					Target = target,
					Requester = args.Player,
					Total = DefaultCount,
					Remaining = DefaultCount,
				};
			}

			if (queryOther)
				args.Player.SendInfoMessage($"正在测量玩家 {target.Name} 到服务器的延迟（共 {DefaultCount} 次）...");
			else
				args.Player.SendInfoMessage($"正在测量你到服务器的延迟（共 {DefaultCount} 次）...");

			SendNext(target);
		}

		/// <summary>
		/// 向目标玩家发送一个 Ping(154) 空包用于测量延迟。
		/// </summary>
		private static void SendNext(TSPlayer target)
		{
			PingSession s;
			lock (SyncLock)
			{
				if (!Sessions.TryGetValue(target, out s))
					return;
			}

			s.SendTime = DateTime.Now;
			NetMessage.TrySendData(PingPacketId, target.Index, -1, null, 0);
		}

		/// <summary>
		/// 接收客户端发来的 Ping(154) 包：
		/// 1) 记录心跳（被动连接监测，用于兜底参考）
		/// 2) 若存在等待回包的探测会话，则完成一次 RTT 测量
		/// 注意：不放行 Handled，游戏引擎仍会正常响应客户端心跳。
		/// </summary>
		private static void OnGetData(GetDataEventArgs args)
		{
			if (args.Handled)
				return;
			// 1.4.5 Ping 包：PacketTypes.Ping；按原始包号判断以兼容不同版本枚举映射
			if ((byte)args.MsgID != PingPacketId)
				return;

			var player = TShock.Players[args.Msg.whoAmI];
			if (player == null || !player.Active)
				return;

			// 记录心跳（仅保留最近 3 次用于计算间隔）
			lock (SyncLock)
			{
				if (!Heartbeats.TryGetValue(player, out var list))
				{
					list = new List<DateTime>();
					Heartbeats[player] = list;
				}
				list.Add(DateTime.Now);
				if (list.Count > 3)
					list.RemoveAt(0);
			}

			PingSession s;
			lock (SyncLock)
			{
				if (!Sessions.TryGetValue(player, out s))
					return;
				if (s.NextSendTick != 0)   // 不处于"等待回复"状态（重复/多余回包）
					return;
			}

			// 收到客户端回显 → 完成一次测量
			double ms = (DateTime.Now - s.SendTime).TotalMilliseconds;
			s.Results.Add(ms);
			s.Remaining--;

			if (s.Remaining <= 0)
			{
				Finish(s);
			}
			else
			{
				lock (SyncLock)
				{
					s.NextSendTick = _tick + IntervalTicks;
				}
			}
		}

		/// <summary>
		/// 游戏主循环：按计划发送后续测量包，并处理超时/掉线。
		/// </summary>
		private static void OnGameUpdate(EventArgs args)
		{
			_tick++;

			List<PingSession> toSend = null;
			List<PingSession> toAbort = null;

			lock (SyncLock)
			{
				foreach (var pair in Sessions)
				{
					var s = pair.Value;

					// 目标掉线或超时未响应 → 终止本次测量
					if (!s.Target.Active || (DateTime.Now - s.SendTime).TotalMilliseconds > TimeoutMs)
					{
						(toAbort ?? (toAbort = new List<PingSession>())).Add(s);
						continue;
					}

					// 已收到上一包回复，到点发送下一包
					if (s.NextSendTick > 0 && _tick >= s.NextSendTick)
					{
						s.NextSendTick = 0;
						(toSend ?? (toSend = new List<PingSession>())).Add(s);
					}
				}

				if (toAbort != null)
				{
					foreach (var s in toAbort)
						Sessions.Remove(s.Target);
				}
			}

			if (toSend != null)
			{
				foreach (var s in toSend)
					SendNext(s.Target);
			}
			if (toAbort != null)
			{
				foreach (var s in toAbort)
					Abort(s);
			}
		}

		/// <summary>正常完成全部测量并输出结果。</summary>
		private static void Finish(PingSession s)
		{
			lock (SyncLock)
			{
				Sessions.Remove(s.Target);
			}
			Report(s, false);
		}

		/// <summary>因超时/掉线终止测量，输出已获得的部分结果。</summary>
		private static void Abort(PingSession s)
		{
			Report(s, true);
		}

		private static void Report(PingSession s, bool partial)
		{
			var req = s.Requester;
			if (req == null)
				return;

			if (s.Results.Count == 0)
			{
				// 主动探测无回包：1.4.5 vanilla 客户端可能不回显 Ping(154)，
				// 改用被动心跳间隔作为连接参考
				var hb = GetHeartbeatInfo(s.Target);
				if (hb.HasValue)
				{
					req.SendErrorMessage($"测量玩家 {s.Target.Name} 的延迟失败：客户端未回显 Ping 包。");
					req.SendInfoMessage($"  参考（心跳监测）：最近一次 Ping 心跳 {hb.Value.LastAgo:F0}s 前，平均间隔约 {hb.Value.AvgInterval:F0}s");
				}
				else
				{
					req.SendErrorMessage($"测量玩家 {s.Target.Name} 的延迟失败：未收到任何 Ping 包响应，且无历史心跳记录。");
				}
				return;
			}

			double avg = s.Results.Average();
			double min = s.Results.Min();
			double max = s.Results.Max();
			string detail = string.Join(" / ", s.Results.Select((r, i) => $"{i + 1}# {r:F0}ms"));
			string suffix = partial ? $"（仅完成 {s.Results.Count}/{s.Total} 次）" : "";
			req.SendSuccessMessage($"[Ping] {s.Target.Name} 延迟: 平均 {avg:F0}ms, 最小 {min:F0}ms, 最大 {max:F0}ms{suffix}");
			req.SendInfoMessage($"  明细: {detail}");
		}

		/// <summary>
		/// 基于被动心跳计算参考连接信息。
		/// 返回 null 表示该玩家没有足够的心跳记录。
		/// </summary>
		private static (double LastAgo, double AvgInterval)? GetHeartbeatInfo(TSPlayer player)
		{
			lock (SyncLock)
			{
				if (!Heartbeats.TryGetValue(player, out var list) || list.Count < 2)
					return null;

				var now = DateTime.Now;
				var lastAgo = (now - list[list.Count - 1]).TotalSeconds;
				double total = 0;
				for (int i = 1; i < list.Count; i++)
					total += (list[i] - list[i - 1]).TotalSeconds;
				return (lastAgo, total / (list.Count - 1));
			}
		}
	}
}
