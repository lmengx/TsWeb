using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Rests;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;
using TShockAPI.Hooks;

namespace TShockData
{
	/// <summary>
	/// 宵禁（禁止进服）模块。
	/// 交互模型：
	///  - 条目两种时间模型：每天循环（repeatDaily=true，按 HH:mm 每天自动生效/结束）、一次性（按日期+时间，到期自动关闭）。
	///  - 多条目任一激活即生效（并集）；踢出消息取"最早结束"的激活条目，可进服组取所有激活条目的豁免组并集。
	///  - ServerJoin（int.MaxValue，先于 TShock.OnJoin）：按账号组判定（未注册玩家视为 guest 组），
	///    非豁免组立刻断连；此钩子触发时玩家名已就绪（PlayerInfo 包已处理，见 greetPlayer 时机）。
	///  - PlayerPostLogin 兜底复查：只处理"宵禁生效期间进服且被 ServerJoin 放行"的玩家
	///    （账号名查找漏判等边缘情况），已在线玩家不受影响。
	///  - 一次性条目到期自动置 enabled=false 并持久化；每日循环条目到点自动开关。
	///  - 踢出消息占位符：{now} {date} {weekday} {startTime} {endTime} {timeLeft} {allowedGroups} {curfewName} {serverName}
	/// </summary>
	public static class Curfew
	{
		// ── 常量 ──
		private const string DefaultMessageText =
			"当前为宵禁时段，服务器暂时禁止进服！\n" +
			"[当前时间] {now}\n" +
			"[预计恢复] {endTime}（还剩 {timeLeft}）\n" +
			"[可进服组] {allowedGroups}";
		private const int TickerIntervalSeconds = 30;

		private static TerrariaPlugin? _plugin;
		private static bool _initialized;

		/// <summary>配置（路径: {TShock.SavePath}/TSWeb/curfew.json）</summary>
		public static CurfewConfig Config { get; private set; } = new CurfewConfig();

		private static string ConfigPath => Path.Combine(TShock.SavePath, "TSWeb", "curfew.json");

		/// <summary>宵禁生效期间被 ServerJoin 放行的玩家（进服时间），登录时兜底复查</summary>
		private static readonly ConcurrentDictionary<int, DateTime> _joinTimes = new();

		private static Timer? _ticker;
		private static bool _lastActive;

		public static void Initialize(TerrariaPlugin plugin)
		{
			if (_initialized) return;
			_plugin = plugin;
			LoadConfig();

			// int.MaxValue：在所有 TShock 自身钩子（优先级 0）之前执行
			ServerApi.Hooks.ServerJoin.Register(plugin, OnServerJoin, int.MaxValue);
			PlayerHooks.PlayerPostLogin += OnPlayerPostLogin;
			ServerApi.Hooks.ServerLeave.Register(plugin, OnServerLeave);

			_ticker = new Timer(OnTick, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(TickerIntervalSeconds));

			_initialized = true;
			TShock.Log.ConsoleInfo($"[TSWeb] 宵禁已初始化（{Config.Entries.Count} 个条目，ServerJoin 优先级: int.MaxValue）");
		}

		public static void Dispose()
		{
			if (!_initialized) return;
			ServerApi.Hooks.ServerJoin.Deregister(_plugin, OnServerJoin);
			PlayerHooks.PlayerPostLogin -= OnPlayerPostLogin;
			ServerApi.Hooks.ServerLeave.Deregister(_plugin, OnServerLeave);
			_ticker?.Dispose();
			_ticker = null;
			_joinTimes.Clear();
			_initialized = false;
			TShock.Log.ConsoleInfo("[TSWeb] 宵禁已释放");
		}

		// ═══════════════════════════════════════════
		// 配置读写
		// ═══════════════════════════════════════════

		public static void LoadConfig()
		{
			try
			{
				var dir = Path.GetDirectoryName(ConfigPath);
				if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
					Directory.CreateDirectory(dir);

				if (File.Exists(ConfigPath))
				{
					var json = File.ReadAllText(ConfigPath);
					Config = JsonConvert.DeserializeObject<CurfewConfig>(json) ?? new CurfewConfig();
					TShock.Log.ConsoleInfo($"[TSWeb] 宵禁配置已加载: {Config.Entries.Count} 个条目");
				}
				else
				{
					Config = new CurfewConfig();
					SaveConfig();
					TShock.Log.ConsoleInfo("[TSWeb] 已创建默认宵禁配置");
				}
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[TSWeb] 加载宵禁配置失败: {ex.Message}");
				Config = new CurfewConfig();
			}
		}

		public static void SaveConfig()
		{
			try
			{
				var dir = Path.GetDirectoryName(ConfigPath);
				if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
					Directory.CreateDirectory(dir);
				File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(Config, Formatting.Indented));
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[TSWeb] 保存宵禁配置失败: {ex.Message}");
			}
		}

		// ═══════════════════════════════════════════
		// 调度判定
		// ═══════════════════════════════════════════

		/// <summary>解析 HH:mm（允许省略前导零）</summary>
		private static bool TryParseTime(string s, out TimeSpan t)
		{
			t = default;
			if (string.IsNullOrWhiteSpace(s)) return false;
			var parts = s.Split(':');
			if (parts.Length != 2) return false;
			if (!int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m)) return false;
			if (h < 0 || h > 23 || m < 0 || m > 59) return false;
			t = new TimeSpan(h, m, 0);
			return true;
		}

		/// <summary>解析 yyyy-MM-dd</summary>
		private static bool TryParseDate(string s, out DateTime d)
		{
			return DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
				DateTimeStyles.None, out d);
		}

		/// <summary>
		/// 判定条目当前是否生效，并给出本次生效的结束时刻。
		/// 每日循环：endTime&gt;startTime 为当天窗口；endTime&lt;startTime 为跨天窗口（如 22:00~06:00）。
		/// 一次性：开始/结束为日期+时间，跨天由日期表达。
		/// </summary>
		private static bool Evaluate(CurfewEntry e, DateTime now, out DateTime end)
		{
			end = now;
			if (e == null || !e.Enabled) return false;

			if (e.RepeatDaily)
			{
				if (!TryParseTime(e.StartTime, out var s) || !TryParseTime(e.EndTime, out var t)) return false;
				var tod = now.TimeOfDay;
				if (t > s)
				{
					if (tod >= s && tod < t) { end = now.Date + t; return true; }
				}
				else
				{
					// 跨天：晚上（≥start）生效到今天午夜后；凌晨（&lt;end）生效到今天 end
					if (tod >= s) { end = now.Date + t + TimeSpan.FromDays(1); return true; }
					if (tod < t) { end = now.Date + t; return true; }
				}
				return false;
			}
			else
			{
				if (!TryParseDate(e.StartDate, out var sd) || !TryParseDate(e.EndDate, out var ed)) return false;
				if (!TryParseTime(e.StartTime, out var s) || !TryParseTime(e.EndTime, out var t)) return false;
				var start = sd + s;
				var endDt = ed + t;
				if (now >= start && now < endDt) { end = endDt; return true; }
				return false;
			}
		}

		/// <summary>一次性条目是否已到期（过期后由定时器自动关闭）</summary>
		private static bool IsExpiredOnce(CurfewEntry e, DateTime now)
		{
			if (e == null || !e.Enabled || e.RepeatDaily) return false;
			if (!TryParseDate(e.StartDate, out var sd) || !TryParseDate(e.EndDate, out var ed)) return false;
			if (!TryParseTime(e.StartTime, out var s) || !TryParseTime(e.EndTime, out var t)) return false;
			return now >= ed + t;
		}

		/// <summary>当前激活的条目（含各自结束时刻），按结束时刻升序</summary>
		private static List<(CurfewEntry Entry, DateTime End)> GetActiveEntries(DateTime now)
		{
			var list = new List<(CurfewEntry Entry, DateTime End)>();
			foreach (var e in Config.Entries)
			{
				if (Evaluate(e, now, out var end))
					list.Add((e, end));
			}
			return list.OrderBy(x => x.End).ToList();
		}

		/// <summary>下一个最近的生效开始时刻（取所有启用条目的最早未来开始），无则 null</summary>
		private static DateTime? GetNextOpen(DateTime now)
		{
			DateTime? next = null;
			foreach (var e in Config.Entries)
			{
				if (e == null || !e.Enabled) continue;
				DateTime candidate;
				if (e.RepeatDaily)
				{
					if (!TryParseTime(e.StartTime, out var s)) continue;
					var today = now.Date + s;
					candidate = today > now ? today : today.AddDays(1);
				}
				else
				{
					if (!TryParseDate(e.StartDate, out var sd) || !TryParseTime(e.StartTime, out var s)) continue;
					candidate = sd + s;
					if (candidate <= now) continue;
				}
				if (next == null || candidate < next.Value) next = candidate;
			}
			return next;
		}

		/// <summary>条目的豁免组：条目级未配置时回退全局</summary>
		private static List<string> EntryGroups(CurfewEntry e)
		{
			return e.ExemptGroups != null && e.ExemptGroups.Count > 0
				? e.ExemptGroups
				: Config.ExemptGroups;
		}

		/// <summary>组是否在豁免列表（不区分大小写）</summary>
		private static bool IsGroupExempt(IEnumerable<CurfewEntry> activeEntries, string groupName)
		{
			if (string.IsNullOrEmpty(groupName)) return false;
			foreach (var e in activeEntries)
			{
				foreach (var g in EntryGroups(e))
				{
					if (!string.IsNullOrEmpty(g) && string.Equals(g, groupName, StringComparison.OrdinalIgnoreCase))
						return true;
				}
			}
			return false;
		}

		// ═══════════════════════════════════════════
		// 消息模板
		// ═══════════════════════════════════════════

		private static string WeekdayName(DateTime d)
		{
			return new[] { "周日", "周一", "周二", "周三", "周四", "周五", "周六" }[(int)d.DayOfWeek];
		}

		private static string FormatTime(DateTime d) => d.ToString("HH:mm");

		private static string FormatDateTime(DateTime d) => d.ToString("yyyy-MM-dd HH:mm");

		/// <summary>一次性条目的开始时刻（yyyy-MM-dd HH:mm）；解析失败回退为开始时间文本</summary>
		private static string FormatEntryStart(CurfewEntry e)
		{
			if (TryParseDate(e.StartDate, out var sd) && TryParseTime(e.StartTime, out var s))
				return FormatDateTime(sd + s);
			return e.StartTime ?? "";
		}

		private static string FormatDuration(TimeSpan d)
		{
			if (d < TimeSpan.Zero) d = TimeSpan.Zero;
			if (d.TotalDays >= 1) return $"{(int)d.TotalDays}天{d.Hours}小时{d.Minutes}分";
			if (d.TotalHours >= 1) return $"{d.Hours}小时{d.Minutes}分";
			if (d.TotalMinutes >= 1) return $"{d.Minutes}分钟";
			return "不到1分钟";
		}

		/// <summary>按最早结束的激活条目组装踢出消息，可进服组取并集</summary>
		private static string BuildMessage(List<(CurfewEntry Entry, DateTime End)> active, DateTime now)
		{
			var primary = active[0];
			var e = primary.Entry;
			var groups = active.SelectMany(x => EntryGroups(x.Entry))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

			var template = string.IsNullOrWhiteSpace(e.Message) ? Config.DefaultMessage : e.Message;
			var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				["now"] = FormatTime(now),
				["date"] = now.ToString("yyyy-MM-dd"),
				["weekday"] = WeekdayName(now),
				["startTime"] = e.RepeatDaily ? e.StartTime : FormatEntryStart(e),
				["endTime"] = e.RepeatDaily ? FormatTime(primary.End) : FormatDateTime(primary.End),
				["timeLeft"] = FormatDuration(primary.End - now),
				["allowedGroups"] = groups.Count > 0 ? string.Join("、", groups) : "（无）",
				["curfewName"] = string.IsNullOrWhiteSpace(e.Name) ? "宵禁" : e.Name,
				["serverName"] = TShock.Config.Settings.ServerName ?? "",
			};

			var sb = new StringBuilder(template);
			foreach (var kv in map) sb.Replace("{" + kv.Key + "}", kv.Value);
			return sb.ToString();
		}

		// ═══════════════════════════════════════════
		// 进服拦截
		// ═══════════════════════════════════════════

		/// <summary>ServerJoin（int.MaxValue）：宵禁生效且玩家非豁免组 → 立即断连</summary>
		private static void OnServerJoin(JoinEventArgs args)
		{
			if (args.Handled) return;

			var now = DateTime.Now;
			var active = GetActiveEntries(now);
			if (active.Count == 0) return;

			var player = GetActivePlayer(args.Who);
			if (player == null) return;

			var group = ResolveJoinGroup(player);
			if (group == null) return;

			if (IsGroupExempt(active.Select(x => x.Entry), group))
			{
				// 宵禁生效期间放行 → 记录进服时间，登录时兜底复查
				_joinTimes[args.Who] = now;
				return;
			}

			args.Handled = true;
			SafeKick(player, BuildMessage(active, now));
			TShock.Log.ConsoleInfo($"[TSWeb][Curfew] 进服拦截（宵禁）: {player.Name} 组={group}");
		}

		/// <summary>进服时判定玩家所属组：已登录直接用玩家组；否则按角色名查账号组；无账号视为 guest 组</summary>
		private static string ResolveJoinGroup(TSPlayer player)
		{
			if (player.IsLoggedIn && player.Account != null && player.Group != null)
				return player.Group.Name;

			if (!string.IsNullOrEmpty(player.Name))
			{
				try
				{
					var acc = TShock.UserAccounts.GetUserAccountByName(player.Name);
					if (acc != null && !string.IsNullOrEmpty(acc.Group))
						return acc.Group;
				}
				catch (Exception ex)
				{
					TShock.Log.ConsoleWarn($"[TSWeb][Curfew] 查询账号 {player.Name} 组失败: {ex.Message}");
				}
			}

			return TShock.Config.Settings.DefaultGuestGroupName;
		}

		/// <summary>
		/// 登录兜底复查：仅处理"宵禁生效期间进服且被 ServerJoin 放行"的玩家。
		/// 已在线（宵禁开始前进服）的玩家不受影响。
		/// </summary>
		private static void OnPlayerPostLogin(PlayerPostLoginEventArgs e)
		{
			var player = e.Player;
			if (player == null || !player.Active) return;
			if (!_joinTimes.TryRemove(player.Index, out _)) return;

			var now = DateTime.Now;
			var active = GetActiveEntries(now);
			if (active.Count == 0) return;

			var group = player.Group?.Name;
			if (group == null || IsGroupExempt(active.Select(x => x.Entry), group)) return;

			SafeKick(player, BuildMessage(active, now));
			TShock.Log.ConsoleInfo($"[TSWeb][Curfew] 登录兜底拦截（宵禁）: {player.Name} 组={group}");
		}

		private static void OnServerLeave(LeaveEventArgs args)
		{
			_joinTimes.TryRemove(args.Who, out _);
		}

		// ═══════════════════════════════════════════
		// 定时器：一次性条目到期自动关闭 + 状态变化日志
		// ═══════════════════════════════════════════

		private static void OnTick(object _)
		{
			try
			{
				var now = DateTime.Now;

				var activeNow = GetActiveEntries(now).Count > 0;
				if (activeNow != _lastActive)
				{
					_lastActive = activeNow;
					TShock.Log.ConsoleInfo($"[TSWeb][Curfew] 宵禁状态变化: {(activeNow ? "已生效" : "已结束")}");
				}

				var changed = false;
				foreach (var e in Config.Entries)
				{
					if (IsExpiredOnce(e, now))
					{
						e.Enabled = false;
						changed = true;
					}
				}
				if (changed)
				{
					SaveConfig();
					TShock.Log.ConsoleInfo("[TSWeb][Curfew] 有一次性宵禁条目已到期，自动关闭");
				}
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleWarn($"[TSWeb][Curfew] 定时检查异常: {ex.Message}");
			}
		}

		// ═══════════════════════════════════════════
		// REST API
		// ═══════════════════════════════════════════

		public static object GetConfigJson(RestRequestArgs args)
		{
			var now = DateTime.Now;
			var active = GetActiveEntries(now);
			var entries = new List<object>();
			foreach (var e in Config.Entries)
			{
				var act = Evaluate(e, now, out var end);
				entries.Add(new Dictionary<string, object>
				{
					{ "id", e.Id },
					{ "name", e.Name ?? "" },
					{ "enabled", e.Enabled },
					{ "repeatDaily", e.RepeatDaily },
					{ "startTime", e.StartTime ?? "" },
					{ "endTime", e.EndTime ?? "" },
					{ "startDate", e.StartDate ?? "" },
					{ "endDate", e.EndDate ?? "" },
					{ "message", e.Message ?? "" },
					{ "exemptGroups", e.ExemptGroups ?? new List<string>() },
					{ "resolvedGroups", EntryGroups(e) },
					{ "active", act },
					{ "activeEnd", act ? (e.RepeatDaily ? FormatTime(end) : FormatDateTime(end)) : "" },
					{ "expired", !e.Enabled && !e.RepeatDaily && IsExpiredOnce(e, now) },
				});
			}

			var groups = active.SelectMany(x => EntryGroups(x.Entry))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

			return new RestObject("200")
			{
				{ "version", Config.Version },
				{ "defaultMessage", Config.DefaultMessage },
				{ "exemptGroups", Config.ExemptGroups },
				{ "entries", entries },
				{ "now", now.ToString("yyyy-MM-dd HH:mm:ss") },
				{ "active", active.Count > 0 },
				{ "activeCount", active.Count },
				{ "allowedGroups", groups },
				{ "nextOpen", GetNextOpen(now)?.ToString("yyyy-MM-dd HH:mm") ?? "" },
			};
		}

		/// <summary>
		/// 保存配置：query 参数 config = 完整 JSON（entries / defaultMessage / exemptGroups）。
		/// 会做条目校验；一次性且已到期的条目自动置 disabled。
		/// </summary>
		public static object SetConfigJson(RestRequestArgs args)
		{
			try
			{
				string? json = null;
				try { json = args.Parameters["config"]; } catch { }

				if (string.IsNullOrWhiteSpace(json))
					return new RestObject("400") { { "error", "缺少 config 参数" } };

				var incoming = JsonConvert.DeserializeObject<CurfewConfig>(json);
				if (incoming == null)
					return new RestObject("400") { { "error", "config 无法解析" } };

				var errors = new List<string>();
				var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (var e in incoming.Entries)
				{
					if (string.IsNullOrWhiteSpace(e.Id)) e.Id = Guid.NewGuid().ToString("N").Substring(0, 8);
					if (!seen.Add(e.Id))
					{
						errors.Add($"条目 ID {e.Id} 重复");
						continue;
					}
					var err = ValidateEntry(e, DateTime.Now);
					if (err != null) errors.Add($"[{e.Name ?? e.Id}] {err}");
				}
				if (errors.Count > 0)
					return new RestObject("400") { { "error", string.Join("；", errors) } };

				Config = incoming;
				SaveConfig();
				TShock.Log.ConsoleInfo($"[TSWeb] REST 更新宵禁配置: {Config.Entries.Count} 个条目");

				var now = DateTime.Now;
				var active = GetActiveEntries(now);
				return new RestObject("200")
				{
					{ "message", $"配置已保存，当前宵禁{(active.Count > 0 ? $"已生效（{active.Count} 个条目）" : "未生效")}" },
					{ "active", active.Count > 0 },
					{ "activeCount", active.Count },
				};
			}
			catch (Exception ex)
			{
				return new RestObject("500") { { "error", ex.Message } };
			}
		}

		/// <summary>条目字段校验，非法返回错误描述，合法返回 null</summary>
		private static string ValidateEntry(CurfewEntry e, DateTime now)
		{
			if (string.IsNullOrWhiteSpace(e.Name))
				return "名称不能为空";
			if (!TryParseTime(e.StartTime, out var s) || !TryParseTime(e.EndTime, out var t))
				return "开始/结束时间格式错误（应为 HH:mm）";

			if (e.RepeatDaily)
			{
				if (s == t)
					return "每天循环条目的开始与结束时间不能相同";
			}
			else
			{
				if (!TryParseDate(e.StartDate, out var sd) || !TryParseDate(e.EndDate, out var ed))
					return "一次性条目缺少有效的开始/结束日期（应为 yyyy-MM-dd）";
				var start = sd + s;
				var endDt = ed + t;
				if (endDt <= start)
					return "一次性条目的结束时刻必须晚于开始时刻";
				if (now >= endDt)
					e.Enabled = false; // 已到期 → 自动关闭
			}

			if (e.ExemptGroups != null)
				e.ExemptGroups = e.ExemptGroups.Where(g => !string.IsNullOrWhiteSpace(g)).ToList();
			if (e.ExemptGroups != null && e.ExemptGroups.Count == 0)
				e.ExemptGroups = null;

			return null;
		}

		// ═══════════════════════════════════════════
		// 游戏内命令 /curfew（宵禁）
		// ═══════════════════════════════════════════

		public static void CurfewCommand(CommandArgs args)
		{
			var p = args.Player;
			if (p == null) return;

			var tokens = args.Parameters;
			var sub = tokens.Count > 0 ? tokens[0].ToLowerInvariant() : "";

			switch (sub)
			{
				case "":
				case "status":
					ShowStatus(p);
					break;
				case "list":
					ShowList(p);
					break;
				case "on":
					ToggleEntry(p, tokens, true);
					break;
				case "off":
					ToggleEntry(p, tokens, false);
					break;
				case "add":
				case "create":
					AddEntry(p, tokens);
					break;
				case "del":
				case "delete":
				case "remove":
					DeleteEntry(p, tokens);
					break;
				default:
					p.SendInfoMessage("[宵禁] 用法: /curfew [status|list|on 名称|off 名称|add 名称 开始 结束 [daily|once]|del 名称]");
					break;
			}
		}

		private static void ShowStatus(TSPlayer p)
		{
			var now = DateTime.Now;
			var active = GetActiveEntries(now);
			if (active.Count == 0)
			{
				var next = GetNextOpen(now);
				p.SendInfoMessage($"[宵禁] 当前未生效。{(next.HasValue ? $"下一次生效: {next.Value:yyyy-MM-dd HH:mm}" : "暂无排期的宵禁条目")}");
				return;
			}

			p.SendInfoMessage($"[宵禁] 🔴 当前已生效（{active.Count} 个条目）");
			foreach (var (e, end) in active)
			{
				var mode = e.RepeatDaily ? "每天循环" : "一次性";
				var groups = string.Join("、", EntryGroups(e));
				p.SendInfoMessage($"  - {e.Name}（{mode}）结束: {(e.RepeatDaily ? FormatTime(end) : FormatDateTime(end))}，还剩 {FormatDuration(end - now)}，豁免组: {groups}");
			}
		}

		private static void ShowList(TSPlayer p)
		{
			if (Config.Entries.Count == 0)
			{
				p.SendInfoMessage("[宵禁] 暂无条目，可用 /curfew add 创建，或用管理页面管理");
				return;
			}

			var now = DateTime.Now;
			p.SendInfoMessage($"[宵禁] 共 {Config.Entries.Count} 个条目：");
			for (var i = 0; i < Config.Entries.Count; i++)
			{
				var e = Config.Entries[i];
				var act = Evaluate(e, now, out _);
				var schedule = e.RepeatDaily
					? $"每天 {e.StartTime}~{e.EndTime}"
					: $"{e.StartDate} {e.StartTime} ~ {e.EndDate} {e.EndTime}";
				p.SendInfoMessage($"  {i + 1}. {e.Name} [{e.Id}] {(e.Enabled ? "✔启用" : "✘停用")} {(act ? "🔴生效中" : "")} | {schedule}");
			}
		}

		private static void ToggleEntry(TSPlayer p, List<string> tokens, bool enable)
		{
			if (tokens.Count < 2)
			{
				p.SendErrorMessage($"[宵禁] 用法: /curfew {(enable ? "on" : "off")} <名称或ID>");
				return;
			}
			var target = string.Join(" ", tokens.Skip(1)).Trim();
			var e = FindEntry(target);
			if (e == null)
			{
				p.SendErrorMessage($"[宵禁] 未找到条目: {target}");
				return;
			}
			e.Enabled = enable;
			SaveConfig();
			p.SendSuccessMessage($"[宵禁] 已{(enable ? "启用" : "停用")}条目: {e.Name}");
		}

		private static void AddEntry(TSPlayer p, List<string> tokens)
		{
			// /curfew add <名称> <开始HH:mm> <结束HH:mm> [daily|once]
			// 名称可含空格：扫描第一个 HH:mm 作为开始时间，其后为结束时间，再后为模式
			int startIdx = -1;
			for (var i = 1; i < tokens.Count; i++)
			{
				if (TryParseTime(tokens[i], out _)) { startIdx = i; break; }
			}
			if (startIdx < 1 || startIdx + 1 >= tokens.Count)
			{
				p.SendErrorMessage("[宵禁] 用法: /curfew add <名称> <开始HH:mm> <结束HH:mm> [daily|once]");
				return;
			}

			var name = string.Join(" ", tokens.Skip(1).Take(startIdx - 1)).Trim();
			var start = tokens[startIdx];
			var end = tokens[startIdx + 1];
			var mode = tokens.Count > startIdx + 2 ? tokens[startIdx + 2].ToLowerInvariant() : "daily";

			var e = new CurfewEntry
			{
				Name = string.IsNullOrWhiteSpace(name) ? "宵禁" : name,
				StartTime = start,
				EndTime = end,
				RepeatDaily = mode != "once",
				StartDate = "",
				EndDate = "",
			};

			if (!e.RepeatDaily)
			{
				// 一次性：开始日期今天；end &lt;= start 时结束日期顺延一天
				// 时间非法时留给 ValidateEntry 报错，这里不抛异常
				var now = DateTime.Now;
				if (TryParseTime(e.StartTime, out var s) && TryParseTime(e.EndTime, out var t))
				{
					e.StartDate = now.ToString("yyyy-MM-dd");
					e.EndDate = now.AddDays(t > s ? 0 : 1).ToString("yyyy-MM-dd");
				}
			}

			var err = ValidateEntry(e, DateTime.Now);
			if (err != null)
			{
				p.SendErrorMessage($"[宵禁] 创建失败: {err}");
				return;
			}

			Config.Entries.Add(e);
			SaveConfig();
			p.SendSuccessMessage($"[宵禁] 已创建条目: {e.Name}（{(e.RepeatDaily ? $"每天 {start}~{end}" : $"{e.StartDate} {start} ~ {e.EndDate} {end}")}）");
		}

		private static void DeleteEntry(TSPlayer p, List<string> tokens)
		{
			if (tokens.Count < 2)
			{
				p.SendErrorMessage("[宵禁] 用法: /curfew del <名称或ID>");
				return;
			}
			var target = string.Join(" ", tokens.Skip(1)).Trim();
			var e = FindEntry(target);
			if (e == null)
			{
				p.SendErrorMessage($"[宵禁] 未找到条目: {target}");
				return;
			}
			Config.Entries.Remove(e);
			SaveConfig();
			p.SendSuccessMessage($"[宵禁] 已删除条目: {e.Name}");
		}

		private static CurfewEntry? FindEntry(string target)
		{
			foreach (var e in Config.Entries)
			{
				if (string.Equals(e.Id, target, StringComparison.OrdinalIgnoreCase)
					|| string.Equals(e.Name, target, StringComparison.OrdinalIgnoreCase))
					return e;
			}
			return null;
		}

		// ═══════════════════════════════════════════
		// 辅助
		// ═══════════════════════════════════════════

		private static TSPlayer? GetActivePlayer(int who)
		{
			if (who < 0 || who >= TShock.Players.Length) return null;
			var p = TShock.Players[who];
			return p != null && p.Active ? p : null;
		}

		private static void SafeKick(TSPlayer player, string reason)
		{
			try
			{
				if (player != null && player.Active && player.ConnectionAlive)
					player.Kick(reason, true);
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[TSWeb][Curfew] 踢出玩家异常: {ex.Message}");
			}
		}
	}

	// ═══════════════════════════════════════════════
	// 数据模型
	// ═══════════════════════════════════════════════

	/// <summary>宵禁配置（JSON 持久化路径: {TShock.SavePath}/TSWeb/curfew.json）</summary>
	public class CurfewConfig
	{
		[JsonProperty("version")]
		public int Version { get; set; } = 1;

		/// <summary>全局默认踢出消息模板（条目未配置 message 时使用）</summary>
		[JsonProperty("defaultMessage")]
		public string DefaultMessage { get; set; } =
			"当前为宵禁时段，服务器暂时禁止进服！\n[当前时间] {now}\n[预计恢复] {endTime}（还剩 {timeLeft}）\n[可进服组] {allowedGroups}";

		/// <summary>全局豁免组（条目未单独配置豁免组时生效）</summary>
		[JsonProperty("exemptGroups")]
		public List<string> ExemptGroups { get; set; } = new List<string> { "owner", "superadmin" };

		[JsonProperty("entries")]
		public List<CurfewEntry> Entries { get; set; } = new List<CurfewEntry>();
	}

	/// <summary>单条宵禁条目</summary>
	public class CurfewEntry
	{
		[JsonProperty("id")]
		public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);

		[JsonProperty("name")]
		public string Name { get; set; } = "";

		[JsonProperty("enabled")]
		public bool Enabled { get; set; } = true;

		/// <summary>true=每天循环（按 HH:mm 每日自动生效/结束）；false=一次性（按日期+时间，到期自动关闭）</summary>
		[JsonProperty("repeatDaily")]
		public bool RepeatDaily { get; set; } = true;

		[JsonProperty("startTime")]
		public string StartTime { get; set; } = "22:00";

		[JsonProperty("endTime")]
		public string EndTime { get; set; } = "06:00";

		/// <summary>一次性条目的开始日期（yyyy-MM-dd）；每日循环忽略</summary>
		[JsonProperty("startDate")]
		public string StartDate { get; set; } = "";

		/// <summary>一次性条目的结束日期（yyyy-MM-dd）；每日循环忽略</summary>
		[JsonProperty("endDate")]
		public string EndDate { get; set; } = "";

		/// <summary>条目级踢出消息模板；空则用全局默认</summary>
		[JsonProperty("message")]
		public string Message { get; set; } = "";

		/// <summary>条目级豁免组；null/空则用全局豁免组</summary>
		[JsonProperty("exemptGroups")]
		public List<string> ExemptGroups { get; set; }
	}
}
