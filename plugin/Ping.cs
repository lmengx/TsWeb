using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;

namespace TShockData
{
	/// <summary>
	/// ping 命令：测量玩家到服务器的网络延迟。
	/// 原理：向客户端发送 RemoveItemOwner(39) 包，客户端收到后会回发相同内容的包，
	/// 服务端通过发送与接收之间的时间差计算往返延迟（RTT）。
	/// 参考实现：TShockPlugin-master/Economics.Core 的 Ping 机制。
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

		private static readonly object SyncLock = new object();
		private static readonly Dictionary<TSPlayer, PingSession> Sessions = new Dictionary<TSPlayer, PingSession>();
		private static long _tick;

		/// <summary>一次完整的延迟测量会话</summary>
		private class PingSession
		{
			public TSPlayer Target;                     // 被测量的玩家
			public TSPlayer Requester;                  // 发起命令的玩家（可能等于 Target）
			public int Total;                           // 计划测量总次数
			public int Remaining;                       // 剩余测量次数
			public List<double> Results = new List<double>(); // 每次测量的延迟结果(ms)
			public int LastSlot;                        // 最近一次使用的物品槽位
			public DateTime SendTime;                   // 最近一次发包时间
			public long NextSendTick;                   // 计划发送下一包的时间点(tick)，0 表示正在等待回复
		}

		public static void Initialize(TerrariaPlugin plugin)
		{
			ServerApi.Hooks.NetGetData.Register(plugin, OnGetData, int.MaxValue);
			ServerApi.Hooks.GameUpdate.Register(plugin, OnGameUpdate);
			TShock.Log.ConsoleInfo("[TSWeb] Ping 模块已初始化");
		}

		public static void Dispose(TerrariaPlugin plugin)
		{
			lock (SyncLock)
			{
				Sessions.Clear();
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
		/// 向目标玩家发送一个 RemoveItemOwner 包用于测量延迟。
		/// </summary>
		private static void SendNext(TSPlayer target)
		{
			PingSession s;
			lock (SyncLock)
			{
				if (!Sessions.TryGetValue(target, out s))
					return;
			}

			int slot = FindFreeSlot();
			if (slot < 0)
			{
				lock (SyncLock) Sessions.Remove(target);
				s.Requester.SendErrorMessage("服务器当前没有空闲物品槽，无法测量延迟。");
				return;
			}

			s.LastSlot = slot;
			s.SendTime = DateTime.Now;
			// number2=byte.MaxValue(255) 是关键：客户端仅在收到 owner==255 时才回发 ItemOwner 包
			bool sent = NetMessage.TrySendData(39, target.Index, -1, null, slot, byte.MaxValue);
			TShock.Log.ConsoleInfo($"[Ping] 发送测量包 → {target.Name}(idx:{target.Index}) slot:{slot} 成功:{sent} 剩余:{s.Remaining}");
		}

		/// <summary>
		/// 接收客户端回发的 RemoveItemOwner 包，完成一次延迟测量。
		/// </summary>
		private static void OnGetData(GetDataEventArgs args)
		{
			if (args.Handled)
				return;
			// 发送与回发均为 RemoveItemOwner(39)/ItemOwner；兼容不同版本枚举映射，同时按原始包号判断
			if (args.MsgID != PacketTypes.ItemOwner && (byte)args.MsgID != 39)
				return;

			var player = TShock.Players[args.Msg.whoAmI];
			if (player == null || !player.Active)
				return;

			TShock.Log.ConsoleInfo($"[Ping] 收到回包 玩家:{player.Name} MsgID:{(int)args.MsgID} Index:{args.Index} Length:{args.Length}");

			PingSession s;
			lock (SyncLock)
			{
				if (!Sessions.TryGetValue(player, out s))
				{
					TShock.Log.ConsoleInfo($"[Ping] 玩家 {player.Name} 无进行中的测量会话，忽略");
					return;
				}
				if (s.NextSendTick != 0)   // 不处于"等待回复"状态（重复/多余回包）
				{
					TShock.Log.ConsoleInfo($"[Ping] 重复回包，忽略");
					return;
				}
			}

			// 校验回包内容：short 槽位 + byte 255（无主）
			try
			{
				using (var reader = new BinaryReader(new MemoryStream(args.Msg.readBuffer, args.Index, Math.Max(args.Length - 1, 0))))
				{
					short itemIndex = reader.ReadInt16();
					byte owner = reader.ReadByte();
					TShock.Log.ConsoleInfo($"[Ping] 解析回包 slot:{itemIndex} owner:{owner} 期望slot:{s.LastSlot}");
					if (owner != byte.MaxValue || itemIndex != s.LastSlot)
						return;
				}
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleInfo($"[Ping] 回包解析异常: {ex.Message}");
				return;
			}

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
				req.SendErrorMessage($"测量玩家 {s.Target.Name} 的延迟失败：未收到任何响应。");
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
		/// 查找一个空闲的世界物品槽位（inactive）作为测量用槽位。
		/// 排除 400：TShock 使用该槽 + 所有者 255 作为 SSC 哨兵包，避免误判。
		/// </summary>
		private static int FindFreeSlot()
		{
			for (int i = 0; i < Main.item.Length; i++)
			{
				if (i == 400)
					continue;
				var item = Main.item[i];
				if (item == null || !item.active)
					return i;
			}
			return -1;
		}
	}
}
