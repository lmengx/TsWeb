using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Timers;
using Timer = System.Timers.Timer;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;

namespace TeleportRequest
{
	[ApiVersion(2, 1)]
	public class TeleportRequest : TerrariaPlugin
	{
		public override string Author => "lmx12330";
		public override string Description => "tp重写：允许/需同意/拒绝";
		public override string Name => "Teleport";
		public override Version Version => Assembly.GetExecutingAssembly().GetName().Version;

		private Config _config = new Config();
		private Timer _timer;
		private readonly TPRequest[] _requests = new TPRequest[256];
		private bool _disposed;

		// 本插件注册的所有命令，用于卸载时清理
		private static readonly HashSet<string> OwnedCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"tp", "tpa", "tpd", "tpallow", "tpmode"
		};

		public TeleportRequest(Main game) : base(game)
		{
			for (int i = 0; i < _requests.Length; i++)
				_requests[i] = new TPRequest();
		}

		protected override void Dispose(bool disposing)
		{
			if (_disposed)
				return;

			if (disposing)
			{
				// 停止定时器
				_timer?.Stop();
				_timer?.Dispose();

				// 注销 hooks
				ServerApi.Hooks.ServerLeave.Deregister(this, OnLeave);
				TShockAPI.Hooks.GeneralHooks.ReloadEvent -= OnReload;

				// 清理所有注册的命令
				Commands.ChatCommands.RemoveAll(cmd =>
					cmd.Names.Any(n => OwnedCommands.Contains(n)));

				_disposed = true;
			}
		}

		public override void Initialize()
		{
			// ========== 移除 TShock 自带的 /tp 和 /tpallow ==========
			Commands.ChatCommands.RemoveAll(cmd =>
				cmd.Names.Any(n => n.Equals("tp", StringComparison.OrdinalIgnoreCase) ||
				                  n.Equals("tpallow", StringComparison.OrdinalIgnoreCase)));

			// ========== 注册命令 ==========

			Commands.ChatCommands.Add(new Command("", TP, "tp")
			{
				AllowServer = false,
				HelpText = "传送到目标玩家。有 tshock.tp.override 则直接传送，否则根据对方模式决定。"
			});

			Commands.ChatCommands.Add(new Command("", TPAllow, "tpallow")
			{
				AllowServer = false,
				HelpText = "切换传送模式：block ↔ agree。需要 tshock.tp.block 权限。"
			});

			Commands.ChatCommands.Add(new Command("", TPAccept, "tpa")
			{
				AllowServer = false,
				HelpText = "同意传送请求。"
			});

			Commands.ChatCommands.Add(new Command("", TPDeny, "tpd")
			{
				AllowServer = false,
				HelpText = "拒绝传送请求。"
			});

			Commands.ChatCommands.Add(new Command("", TPModeCmd, "tpmode")
			{
				AllowServer = false,
				HelpText = "设置传送模式：agree(允许)/request(需同意)/block(拒绝)。block 需要 tshock.tp.block 权限"
			});

			// ========== 加载持久数据 ==========

			LoadConfig();
			TPModeStore.Initialize(TShock.SavePath);

			// ========== 启动定时器 ==========

			StartTimer();

			// ========== 注册事件 ==========

			ServerApi.Hooks.ServerLeave.Register(this, OnLeave);
			TShockAPI.Hooks.GeneralHooks.ReloadEvent += OnReload;
		}

		// ================================================================
		//  生命周期辅助方法
		// ================================================================

		private void LoadConfig()
		{
			var configPath = Path.Combine(TShock.SavePath, "tpconfig.json");
			if (File.Exists(configPath))
				_config = Config.Read(configPath);
			_config.Write(configPath);
		}

		private void StartTimer()
		{
			_timer?.Stop();
			_timer?.Dispose();

			_timer = new Timer(_config.Interval * 1000);
			_timer.Elapsed += OnTimerElapsed;
			_timer.Start();
		}

		// ================================================================
		//  热重载事件
		// ================================================================

		private void OnReload(TShockAPI.Hooks.ReloadEventArgs e)
		{
			LoadConfig();
			TPModeStore.Initialize(TShock.SavePath);
			StartTimer();

			// 清理过期请求
			for (int i = 0; i < _requests.Length; i++)
				_requests[i].timeout = 0;

			TShock.Log.ConsoleInfo("[Teleport] 配置已重新加载");
		}

		// ================================================================
		//  玩家离开事件
		// ================================================================

		void OnLeave(LeaveEventArgs e)
		{
			_requests[e.Who].timeout = 0;
		}

		// ================================================================
		//  定时器：请求提醒 & 超时
		// ================================================================

		void OnTimerElapsed(object sender, ElapsedEventArgs e)
		{
			for (int i = 0; i < _requests.Length; i++)
			{
				var req = _requests[i];
				if (req.timeout <= 0)
					continue;

				var src = TShock.Players[i];
				var dst = TShock.Players[req.dst];

				if (src == null || dst == null)
				{
					req.timeout = 0;
					continue;
				}

				req.timeout--;
				if (req.timeout == 0)
				{
					src.SendErrorMessage("你的传送请求已超时。");
					dst.SendInfoMessage("{0} 的传送请求已超时。", src.Name);
				}
				else
				{
					dst.SendInfoMessage("{0} 请求传送到你所在位置。(/tpa 或 /tpd)", src.Name);
				}
			}
		}

		// ================================================================
		//  /tp <玩家名> — 主传送命令，含 ac/aclist/acdel 子命令
		// ================================================================
		void TP(CommandArgs e)
		{
			if (e.Parameters.Count == 0)
			{
				e.Player.SendErrorMessage("语法错误：/tp <玩家名> 或 /tp ac/aclist/acdel");
				return;
			}

			// ---- 子命令：/tp aclist — 查看白名单 ----
			if (e.Parameters[0].Equals("aclist", StringComparison.OrdinalIgnoreCase))
			{
				var list = TPModeStore.GetAllowedList(e.Player.Index);
				if (list.Count == 0)
				{
					e.Player.SendInfoMessage("你的白名单为空。");
					return;
				}
				var names = new List<string>();
				foreach (var id in list)
				{
					var p = TShock.Players[id];
					names.Add(p != null ? p.Name : $"ID:{id}");
				}
				e.Player.SendInfoMessage("白名单 ({0}): {1}", list.Count, string.Join(", ", names));
				return;
			}

			// ---- 子命令：/tp acdel <玩家名> — 移出白名单 ----
			if (e.Parameters[0].Equals("acdel", StringComparison.OrdinalIgnoreCase))
			{
				if (e.Parameters.Count < 2)
				{
					e.Player.SendErrorMessage("语法错误：/tp acdel <玩家名>");
					return;
				}
				string delName = string.Join(" ", e.Parameters.Skip(1));
				var delPlayers = TSPlayer.FindByNameOrID(delName);
				if (delPlayers.Count == 0)
				{
					e.Player.SendErrorMessage("未找到该玩家。");
					return;
				}
				if (delPlayers.Count > 1)
				{
					e.Player.SendErrorMessage("匹配到多个玩家，请指定更准确的名称。");
					return;
				}
				TPModeStore.RemoveAllowed(e.Player.Index, delPlayers[0].Index);
				e.Player.SendSuccessMessage("已将 {0} 移出白名单。", delPlayers[0].Name);
				return;
			}

			// ---- 子命令：/tp ac <玩家名> — 加入白名单 ----
			if (e.Parameters[0].Equals("ac", StringComparison.OrdinalIgnoreCase))
			{
				if (e.Parameters.Count < 2)
				{
					e.Player.SendErrorMessage("语法错误：/tp ac <玩家名>");
					return;
				}
				string acName = string.Join(" ", e.Parameters.Skip(1));
				var acPlayers = TSPlayer.FindByNameOrID(acName);
				if (acPlayers.Count == 0)
				{
					e.Player.SendErrorMessage("未找到该玩家。");
					return;
				}
				if (acPlayers.Count > 1)
				{
					e.Player.SendErrorMessage("匹配到多个玩家，请指定更准确的名称。");
					return;
				}
				TPModeStore.AddAllowed(e.Player.Index, acPlayers[0].Index);
				e.Player.SendSuccessMessage("已将 {0} 加入白名单，可无视模式传送。", acPlayers[0].Name);
				return;
			}

			// ---- 以下为正常传送逻辑 ----

			string plrName = string.Join(" ", e.Parameters);
			var players = TSPlayer.FindByNameOrID(plrName);
			if (players.Count == 0)
			{
				e.Player.SendErrorMessage("未找到该玩家。");
				return;
			}
			if (players.Count > 1)
			{
				e.Player.SendErrorMessage("匹配到多个玩家，请指定更准确的名称。");
				return;
			}

			var target = players[0];
			if (target == e.Player)
			{
				e.Player.SendErrorMessage("不能传送到自己。");
				return;
			}

			// ---- 白名单优先于一切（可绕过 block） ----
			if (TPModeStore.IsAllowed(target.Index, e.Player.Index))
			{
				if (e.Player.Teleport(target.X, target.Y))
				{
					e.Player.SendSuccessMessage("已传送到 {0}（白名单）。", target.Name);
					target.SendSuccessMessage("{0} 通过白名单传送到了你所在位置。", e.Player.Name);
				}
				return;
			}

			// ---- 有 tshock.tp.override 权限 → 直接传送 ----
			if (e.Player.HasPermission("tshock.tp.override"))
			{
				if (e.Player.Teleport(target.X, target.Y))
				{
					e.Player.SendSuccessMessage("已传送到 {0}。", target.Name);
					target.SendSuccessMessage("{0} 传送到了你所在位置。", e.Player.Name);
				}
				return;
			}

			// ---- 根据目标玩家的传送模式判定 ----
			var mode = TPModeStore.GetMode(target.Index);
			switch (mode)
			{
				case TPMode.Agree:
					if (e.Player.Teleport(target.X, target.Y))
					{
						e.Player.SendSuccessMessage("已传送到 {0}。", target.Name);
						target.SendSuccessMessage("{0} 传送到了你所在位置。", e.Player.Name);
					}
					break;

				case TPMode.Request:
					for (int i = 0; i < _requests.Length; i++)
					{
						if (_requests[i].timeout > 0 && _requests[i].dst == target.Index)
						{
							e.Player.SendErrorMessage("{0} 已有待处理的传送请求。", target.Name);
							return;
						}
					}

					_requests[e.Player.Index].dir = false;
					_requests[e.Player.Index].dst = (byte)target.Index;
					_requests[e.Player.Index].timeout = _config.Timeout + 1;
					e.Player.SendSuccessMessage("已向 {0} 发送传送请求。", target.Name);
					break;

				case TPMode.Block:
					e.Player.SendErrorMessage("目标玩家拒绝了传送请求。");
					break;
			}
		}

		// ================================================================
		//  /tpa — 同意传送请求
		// ================================================================
		void TPAccept(CommandArgs e)
		{
			for (int i = 0; i < _requests.Length; i++)
			{
				var req = _requests[i];
				if (req.timeout <= 0 || req.dst != e.Player.Index)
					continue;

				var src = TShock.Players[i];
				if (src == null)
				{
					req.timeout = 0;
					continue;
				}

				if (src.Teleport(e.Player.X, e.Player.Y))
				{
					src.SendSuccessMessage("已传送到 {0}。", e.Player.Name);
					e.Player.SendSuccessMessage("{0} 已传送到你所在位置。", src.Name);
				}
				req.timeout = 0;
				return;
			}

			e.Player.SendErrorMessage("没有待处理的传送请求。");
		}

		// ================================================================
		//  /tpd — 拒绝传送请求
		// ================================================================
		void TPDeny(CommandArgs e)
		{
			for (int i = 0; i < _requests.Length; i++)
			{
				var req = _requests[i];
				if (req.timeout <= 0 || req.dst != e.Player.Index)
					continue;

				var src = TShock.Players[i];
				e.Player.SendSuccessMessage("已拒绝 {0} 的传送请求。", src?.Name ?? "未知");
				src?.SendErrorMessage("{0} 拒绝了你的传送请求。", e.Player.Name);
				req.timeout = 0;
				return;
			}

			e.Player.SendErrorMessage("没有待处理的传送请求。");
		}

		// ================================================================
		//  /tpmode <agree|request|block> — 设置传送模式
		// ================================================================
		void TPModeCmd(CommandArgs e)
		{
			if (e.Parameters.Count == 0)
			{
				var current = TPModeStore.GetMode(e.Player.Index);
				var def = TPModeStore.DefaultMode;
				var modeName = current switch
				{
					TPMode.Agree   => "允许 (agree)",
					TPMode.Request => "需同意 (request)",
					TPMode.Block   => "拒绝 (block)",
					_              => "允许 (agree)"
				};
				var defName = def switch
				{
					TPMode.Agree   => "允许 (agree)",
					TPMode.Request => "需同意 (request)",
					TPMode.Block   => "拒绝 (block)",
					_              => "允许 (agree)"
				};
				e.Player.SendInfoMessage("当前传送模式：{0}", modeName);
				e.Player.SendInfoMessage("未设置玩家的默认模式：{0}", defName);
				e.Player.SendInfoMessage("用法：/tpmode <agree|request|block> (首字母简写)");
				e.Player.SendInfoMessage("管理员：/tpmode setdef <a|r|b> — 设置未设置玩家的默认值");
				return;
			}

			// ---- /tpmode setdef <a|r|b> ：管理员设置全局默认值 ----
			if (e.Parameters[0].Equals("setdef", StringComparison.OrdinalIgnoreCase))
			{
				if (!e.Player.HasPermission("tshock.admin"))
				{
					e.Player.SendErrorMessage("你没有设置全局默认模式权限 (需要 tshock.admin)。");
					return;
				}

				if (e.Parameters.Count < 2)
				{
					e.Player.SendErrorMessage("语法错误：/tpmode setdef <a|r|b>");
					return;
				}

				var defInput = e.Parameters[1].ToLowerInvariant();
				TPMode? defMode = defInput switch
				{
					"agree" or "a" or "ag" or "agr" or "agre" => TPMode.Agree,
					"request" or "r" or "re" or "req" or "requ" or "reque" or "reques" => TPMode.Request,
					"block" or "b" or "bl" or "blo" or "bloc" => TPMode.Block,
					_ => null
				};

				if (defMode == null)
				{
					e.Player.SendErrorMessage("无效模式。可用：a / r / b");
					return;
				}

				TPModeStore.DefaultMode = defMode.Value;

				var defDisplay = defMode.Value switch
				{
					TPMode.Agree   => "允许 (agree)",
					TPMode.Request => "需同意 (request)",
					TPMode.Block   => "拒绝 (block)",
					_              => ""
				};
				e.Player.SendSuccessMessage("已设置未设置玩家的默认传送模式为：{0}", defDisplay);
				return;
			}

			var input = e.Parameters[0].ToLowerInvariant();
			TPMode? parsed = input switch
			{
				"agree" or "a" or "ag" or "agr" or "agre" => TPMode.Agree,
				"request" or "r" or "re" or "req" or "requ" or "reque" or "reques" => TPMode.Request,
				"block" or "b" or "bl" or "blo" or "bloc" => TPMode.Block,
				_ => null
			};

			if (parsed == null)
			{
				e.Player.SendErrorMessage("无效模式。可用：agree / request / block");
				return;
			}

			if (parsed == TPMode.Block && !e.Player.HasPermission("tshock.tp.block"))
			{
				e.Player.SendErrorMessage("你没有设置拒绝模式的权限 (需要 tshock.tp.block)。");
				return;
			}

			TPModeStore.SetMode(e.Player.Index, parsed.Value);

			var display = parsed.Value switch
			{
				TPMode.Agree   => "允许 (agree)",
				TPMode.Request => "需同意 (request)",
				TPMode.Block   => "拒绝 (block)",
				_              => ""
			};
			e.Player.SendSuccessMessage("已设置传送模式为：{0}", display);
		}

		// ================================================================
		//  /tpallow — 切换传送模式 block ↔ agree
		// ================================================================
		void TPAllow(CommandArgs e)
		{
			if (!e.Player.HasPermission("tshock.tp.block"))
			{
				e.Player.SendErrorMessage("你没有使用此命令的权限 (需要 tshock.tp.block)。");
				return;
			}

			var current = TPModeStore.GetMode(e.Player.Index);
			if (current == TPMode.Block)
			{
				TPModeStore.SetMode(e.Player.Index, TPMode.Agree);
				e.Player.SendSuccessMessage("传送模式已切换为：允许 (agree)");
			}
			else
			{
				TPModeStore.SetMode(e.Player.Index, TPMode.Block);
				e.Player.SendSuccessMessage("传送模式已切换为：拒绝 (block)");
			}
		}
	}
}
