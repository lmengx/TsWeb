using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Rests;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;

namespace TShockData
{
	/// <summary>
	/// 命令别名（CommandAlias）模块。
	/// 功能对齐插件库 ShortCommand（简短指令）并整合进 TSWeb：
	///  - 配置驱动：把任意原始命令（含 TShock 原生/其他插件命令）映射成一个自定义命令名（别名）
	///  - 参数占位符：{0} {1} ... 按位置重排，{player} 替换为玩家名；"余段补充"自动把多余参数拼到末尾
	///  - 阻止原始：配置后原始命令被禁（拥有免检权限的玩家不受影响），可彻底隐藏不想暴露的命令
	///  - 限制条件：None / Death（死亡才能用）/ Alive（活着才能用）
	///  - 冷却：按玩家或全服共享的秒数冷却
	///  - REST 管理：GET /data/aliases/config、POST /data/aliases/config/set
	///  - 游戏内命令 /alias （tshock.admin）：status / list / add / del / reload
	/// </summary>
	public static class CommandAlias
	{
		// ── 常量 ──
		/// <summary>免检权限：拥有该权限的玩家不受"阻止原始"影响，可继续使用原始命令</summary>
		public const string BypassPermission = "tsweb.alias.bypass";

		private static TerrariaPlugin? _plugin;
		private static bool _initialized;

		/// <summary>配置（路径: {TShock.SavePath}/TSWeb/aliases.json）</summary>
		public static CommandAliasConfig Config { get; private set; } = new CommandAliasConfig();

		private static string ConfigPath => Path.Combine(TShock.SavePath, "TSWeb", "aliases.json");

		/// <summary>新命令名 → 条目（键不区分大小写，读取时构建）</summary>
		private static readonly Dictionary<string, AliasEntry> _aliasMap = new(StringComparer.OrdinalIgnoreCase);

		/// <summary>被阻止的原始命令首词集合（"阻止原始"=true 的条目）</summary>
		private static readonly HashSet<string> _blockedSource = new(StringComparer.OrdinalIgnoreCase);

		/// <summary>冷却记录：玩家名+命令 → 上次使用时间（共享冷却按命令记）</summary>
		private static readonly Dictionary<string, DateTime> _cooldowns = new();

		public static void Initialize(TerrariaPlugin plugin)
		{
			if (_initialized) return;
			_plugin = plugin;
			LoadConfig();

			PlayerHooks.PlayerCommand += OnPlayerCommand;

			_initialized = true;
			TShock.Log.ConsoleInfo($"[TSWeb] 命令别名已初始化（{Config.Entries.Count} 条映射）");
		}

		public static void Dispose()
		{
			if (!_initialized) return;
			PlayerHooks.PlayerCommand -= OnPlayerCommand;
			_aliasMap.Clear();
			_blockedSource.Clear();
			_cooldowns.Clear();
			_initialized = false;
			TShock.Log.ConsoleInfo("[TSWeb] 命令别名已释放");
		}

		/// <summary>重新从磁盘加载配置（/reload 或 REST set 后调用）</summary>
		public static void Reload()
		{
			LoadConfig();
			TShock.Log.ConsoleInfo($"[TSWeb] 命令别名配置已重载（{Config.Entries.Count} 条映射）");
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
					Config = JsonConvert.DeserializeObject<CommandAliasConfig>(json) ?? new CommandAliasConfig();
				}
				else
				{
					Config = new CommandAliasConfig();
					SaveConfig();
					TShock.Log.ConsoleInfo("[TSWeb] 已创建默认命令别名配置");
				}
				RebuildIndex();
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[TSWeb] 加载命令别名配置失败: {ex.Message}");
				Config = new CommandAliasConfig();
				RebuildIndex();
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
				TShock.Log.ConsoleError($"[TSWeb] 保存命令别名配置失败: {ex.Message}");
			}
		}

		/// <summary>根据配置重建内存索引（新命令名→条目、被阻止原始命令集合）</summary>
		private static void RebuildIndex()
		{
			_aliasMap.Clear();
			_blockedSource.Clear();
			foreach (var e in Config.Entries)
			{
				if (e == null || !e.Enabled || string.IsNullOrWhiteSpace(e.NewCommand)) continue;
				_aliasMap[e.NewCommand.Trim()] = e;
				if (e.NotSource && !string.IsNullOrWhiteSpace(e.SourceCommand))
				{
					var first = e.SourceCommand.Trim().Split(' ')[0];
					if (!string.IsNullOrEmpty(first))
						_blockedSource.Add(first);
				}
			}
		}

		// ═══════════════════════════════════════════
		// 命令拦截核心
		// ═══════════════════════════════════════════

		private static void OnPlayerCommand(PlayerCommandEventArgs args)
		{
			if (args.Handled) return;
			if (args.Player == null) return;

			// 免检权限：可继续使用被阻止的原始命令
			if (args.Player.HasPermission(BypassPermission)) return;

			// 1) 阻止原始：原始命令首词在阻止集合 → 拒绝
			if (_blockedSource.Contains(args.CommandName))
			{
				args.Player.SendErrorMessage("该指令已被禁止使用！");
				args.Handled = true;
				return;
			}

			// 2) 别名映射：新命令名命中 → 转调原始命令
			if (_aliasMap.TryGetValue(args.CommandName, out var entry) && entry != null)
			{
				// 限制条件检查
				if (args.Player.Index >= 0)
				{
					if (entry.Condition == AliasCondition.Alive && (args.Player.Dead || args.Player.TPlayer.statLife < 1))
					{
						args.Player.SendErrorMessage("此指令要求你必须活着才能使用！");
						args.Handled = true;
						return;
					}
					if (entry.Condition == AliasCondition.Death && (!args.Player.Dead || args.Player.TPlayer.statLife > 0))
					{
						args.Player.SendErrorMessage("此指令要求你必须死亡才能使用！");
						args.Handled = true;
						return;
					}

					// 冷却检查
					var cd = GetRemainingCooldown(args.Player.Name, entry);
					if (cd > 0)
					{
						args.Player.SendErrorMessage($"此指令正在冷却，还有 {cd} 秒才能使用！");
						args.Handled = true;
						return;
					}
				}

				// 组转调命令文本
				var source = entry.SourceCommand ?? "";
				if (!BuildSourceCommand(ref source, args.Player.Name, args.Parameters, entry.Supplement))
				{
					args.Handled = true;
					return;
				}

				try
				{
					Commands.HandleCommand(args.Player, args.CommandPrefix + source);
				}
				catch (Exception ex)
				{
					TShock.Log.ConsoleError($"[TSWeb][Alias] 转调命令失败: {ex.Message}");
					args.Player.SendErrorMessage("命令执行失败，请检查控制台日志。");
				}

				// 记录冷却（无论转调是否成功都记录，防止刷屏；共享冷却按原始命令记）
				RecordCooldown(args.Player.Name, entry);

				args.Handled = true;
			}
		}

		/// <summary>
		/// 将原始命令模板中的占位符替换为实际参数。
		///  {0} {1} ... 按位置替换；{player} 替换为玩家名。
		///  余段补充（Supplement）：未命中占位符的多余参数按顺序拼到命令末尾。
		///  模板中残留未替换的 {} 视为非法 → 返回 false（不执行）。
		/// </summary>
		private static bool BuildSourceCommand(ref string cmd, string playerName, List<string> cmdArgs, bool supplement)
		{
			var tail = "";
			for (var i = 0; i < cmdArgs.Count; i++)
			{
				var token = "{" + i + "}";
				if (cmd.Contains(token))
				{
					cmd = cmd.Replace(token, cmdArgs[i]);
					continue;
				}
				if (supplement)
				{
					tail = $"{tail} {cmdArgs[i]}";
					continue;
				}
				return false; // 有多余参数且不允许补充 → 拒绝
			}
			if (cmd.Contains("{player}"))
				cmd = cmd.Replace("{player}", playerName);
			if (cmd.Contains("{") && cmd.Contains("}"))
				return false; // 仍有未替换占位符（参数不足）
			if (supplement)
				cmd += tail;
			return true;
		}

		/// <summary>剩余冷却秒数；0 表示无冷却/冷却已过</summary>
		private static int GetRemainingCooldown(string playerName, AliasEntry entry)
		{
			if (entry == null || entry.CooldownSeconds <= 0) return 0;
			var key = entry.ShareCooldown ? "$$share:" + (entry.SourceCommand ?? "") : playerName + "|" + entry.NewCommand;
			if (!_cooldowns.TryGetValue(key, out var last)) return 0;
			var elapsed = (int)(DateTime.UtcNow - last).TotalSeconds;
			var remain = entry.CooldownSeconds - elapsed;
			if (remain > 0) return remain;
			_cooldowns.Remove(key);
			return 0;
		}

		private static void RecordCooldown(string playerName, AliasEntry entry)
		{
			if (entry == null || entry.CooldownSeconds <= 0) return;
			var key = entry.ShareCooldown ? "$$share:" + (entry.SourceCommand ?? "") : playerName + "|" + entry.NewCommand;
			_cooldowns[key] = DateTime.UtcNow;
		}

		// ═══════════════════════════════════════════
		// REST API
		// ═══════════════════════════════════════════

		public static object GetConfigJson(RestRequestArgs args)
		{
			var entries = new List<object>();
			foreach (var e in Config.Entries)
			{
				entries.Add(new Dictionary<string, object>
				{
					{ "newCommand", e.NewCommand ?? "" },
					{ "sourceCommand", e.SourceCommand ?? "" },
					{ "enabled", e.Enabled },
					{ "supplement", e.Supplement },
					{ "notSource", e.NotSource },
					{ "condition", (int)e.Condition },
					{ "conditionText", e.Condition.ToString() },
					{ "cooldownSeconds", e.CooldownSeconds },
					{ "shareCooldown", e.ShareCooldown },
				});
			}

			return new RestObject("200")
			{
				{ "version", Config.Version },
				{ "bypassPermission", Config.BypassPermission },
				{ "entries", entries },
			};
		}

		/// <summary>
		/// 保存配置：query 参数 config = 完整 JSON。
		/// 校验：新命令名必填、不能以 / 开头、不能与现有 TShock 命令主名冲突（可选警告）；
		/// 原始命令必填。保存后重建内存索引立即生效。
		/// </summary>
		public static object SetConfigJson(RestRequestArgs args)
		{
			try
			{
				string? json = null;
				try { json = args.Parameters["config"]; } catch { }

				if (string.IsNullOrWhiteSpace(json))
					return new RestObject("400") { { "error", "缺少 config 参数" } };

				var incoming = JsonConvert.DeserializeObject<CommandAliasConfig>(json);
				if (incoming == null)
					return new RestObject("400") { { "error", "config 无法解析" } };

				var errors = new List<string>();
				var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (var e in incoming.Entries)
				{
					if (e == null) continue;
					if (string.IsNullOrWhiteSpace(e.NewCommand))
					{
						errors.Add("存在「新命令名」为空的条目");
						continue;
					}
					e.NewCommand = e.NewCommand.Trim();
					if (e.NewCommand.StartsWith("/"))
						e.NewCommand = e.NewCommand.TrimStart('/');
					if (!seen.Add(e.NewCommand))
					{
						errors.Add($"新命令名 {e.NewCommand} 重复");
						continue;
					}
					if (string.IsNullOrWhiteSpace(e.SourceCommand))
					{
						errors.Add($"[{e.NewCommand}] 原始命令不能为空");
						continue;
					}
					if (e.CooldownSeconds < 0) e.CooldownSeconds = 0;
					if (!string.IsNullOrWhiteSpace(incoming.BypassPermission))
						incoming.BypassPermission = incoming.BypassPermission.Trim();
				}
				if (errors.Count > 0)
					return new RestObject("400") { { "error", string.Join("；", errors) } };

				Config = incoming;
				SaveConfig();
				RebuildIndex();
				TShock.Log.ConsoleInfo($"[TSWeb] REST 更新命令别名配置: {Config.Entries.Count} 条映射");
				return new RestObject("200") { { "message", $"配置已保存，共 {Config.Entries.Count} 条映射，已即时生效" } };
			}
			catch (Exception ex)
			{
				return new RestObject("500") { { "error", ex.Message } };
			}
		}

		// ═══════════════════════════════════════════
		// 游戏内命令 /alias（tshock.admin）
		// ═══════════════════════════════════════════

		public static void AliasCommand(CommandArgs args)
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
				case "add":
					AddEntry(p, tokens);
					break;
				case "del":
				case "delete":
				case "remove":
					DeleteEntry(p, tokens);
					break;
				case "reload":
					Reload();
					p.SendSuccessMessage($"[命令别名] 配置已重载（{Config.Entries.Count} 条映射）");
					break;
				default:
					p.SendInfoMessage("[命令别名] 用法: /alias [status|list|add|del|reload]");
					break;
			}
		}

		private static void ShowStatus(TSPlayer p)
		{
			var blocked = _blockedSource.Count;
			p.SendInfoMessage($"[命令别名] 共 {Config.Entries.Count} 条映射，其中 {blocked} 个原始命令被阻止。");
			p.SendInfoMessage($"[命令别名] 免检权限: {Config.BypassPermission}（拥有者可继续使用被阻止的原始命令）");
		}

		private static void ShowList(TSPlayer p)
		{
			if (Config.Entries.Count == 0)
			{
				p.SendInfoMessage("[命令别名] 暂无映射，可用 /alias add 或管理页面配置");
				return;
			}
			p.SendInfoMessage($"[命令别名] 共 {Config.Entries.Count} 条映射：");
			for (var i = 0; i < Config.Entries.Count; i++)
			{
				var e = Config.Entries[i];
				var flags = new List<string>();
				if (e.NotSource) flags.Add("阻止原始");
				if (e.Supplement) flags.Add("余段补充");
				if (e.Condition != AliasCondition.None) flags.Add(e.Condition.ToString());
				if (e.CooldownSeconds > 0) flags.Add(e.ShareCooldown ? $"冷却{e.CooldownSeconds}s(共享)" : $"冷却{e.CooldownSeconds}s");
				var flagText = flags.Count > 0 ? " | " + string.Join(" ", flags) : "";
				p.SendInfoMessage($"  {i + 1}. /{e.NewCommand} → {e.SourceCommand}{flagText}");
			}
		}

		private static void AddEntry(TSPlayer p, List<string> tokens)
		{
			// /alias add <新命令名> <原始命令...>
			if (tokens.Count < 3)
			{
				p.SendErrorMessage("[命令别名] 用法: /alias add <新命令名> <原始命令...> （如: /alias add 传送 warp {0}）");
				return;
			}
			var newCmd = tokens[1].TrimStart('/');
			var source = string.Join(" ", tokens.Skip(2)).Trim();
			if (string.IsNullOrWhiteSpace(newCmd) || string.IsNullOrWhiteSpace(source))
			{
				p.SendErrorMessage("[命令别名] 新命令名与原始命令不能为空");
				return;
			}

			var e = new AliasEntry
			{
				NewCommand = newCmd,
				SourceCommand = source,
				Enabled = true,
				Supplement = true,
				NotSource = false,
				Condition = AliasCondition.None,
				CooldownSeconds = 0,
				ShareCooldown = false,
			};

			var err = ValidateEntry(e);
			if (err != null)
			{
				p.SendErrorMessage($"[命令别名] 创建失败: {err}");
				return;
			}

			Config.Entries.Add(e);
			SaveConfig();
			RebuildIndex();
			p.SendSuccessMessage($"[命令别名] 已添加: /{e.NewCommand} → {e.SourceCommand}");
		}

		private static void DeleteEntry(TSPlayer p, List<string> tokens)
		{
			if (tokens.Count < 2)
			{
				p.SendErrorMessage("[命令别名] 用法: /alias del <新命令名>");
				return;
			}
			var target = tokens[1].TrimStart('/');
			var e = Config.Entries.FirstOrDefault(x =>
				string.Equals(x.NewCommand, target, StringComparison.OrdinalIgnoreCase));
			if (e == null)
			{
				p.SendErrorMessage($"[命令别名] 未找到映射: {target}");
				return;
			}
			Config.Entries.Remove(e);
			SaveConfig();
			RebuildIndex();
			p.SendSuccessMessage($"[命令别名] 已删除: /{e.NewCommand}");
		}

		private static string? ValidateEntry(AliasEntry e)
		{
			if (string.IsNullOrWhiteSpace(e.NewCommand)) return "新命令名不能为空";
			if (string.IsNullOrWhiteSpace(e.SourceCommand)) return "原始命令不能为空";
			if (e.CooldownSeconds < 0) e.CooldownSeconds = 0;
			return null;
		}
	}

	// ═══════════════════════════════════════════════
	// 数据模型
	// ═══════════════════════════════════════════════

	/// <summary>命令别名配置（JSON 持久化路径: {TShock.SavePath}/TSWeb/aliases.json）</summary>
	public class CommandAliasConfig
	{
		[JsonProperty("version")]
		public int Version { get; set; } = 1;

		/// <summary>免检权限：拥有此权限的玩家不受"阻止原始"影响</summary>
		[JsonProperty("bypassPermission")]
		public string BypassPermission { get; set; } = CommandAlias.BypassPermission;

		[JsonProperty("entries")]
		public List<AliasEntry> Entries { get; set; } = new List<AliasEntry>();
	}

	/// <summary>单条命令别名映射</summary>
	public class AliasEntry
	{
		/// <summary>新命令名（别名，不含 /）</summary>
		[JsonProperty("newCommand")]
		public string NewCommand { get; set; } = "";

		/// <summary>原始命令（可含占位符 {0} {1} {player}）</summary>
		[JsonProperty("sourceCommand")]
		public string SourceCommand { get; set; } = "";

		[JsonProperty("enabled")]
		public bool Enabled { get; set; } = true;

		/// <summary>余段补充：多余参数自动拼到命令末尾</summary>
		[JsonProperty("supplement")]
		public bool Supplement { get; set; } = true;

		/// <summary>阻止原始：true 时原始命令首词被禁，只能使用新命令名</summary>
		[JsonProperty("notSource")]
		public bool NotSource { get; set; } = false;

		[JsonProperty("condition")]
		public AliasCondition Condition { get; set; } = AliasCondition.None;

		/// <summary>冷却秒数（0=无冷却）</summary>
		[JsonProperty("cooldownSeconds")]
		public int CooldownSeconds { get; set; } = 0;

		/// <summary>冷却是否全服共享（false=按玩家单独冷却）</summary>
		[JsonProperty("shareCooldown")]
		public bool ShareCooldown { get; set; } = false;
	}

	/// <summary>别名使用限制条件</summary>
	public enum AliasCondition
	{
		/// <summary>无限制</summary>
		None = 0,

		/// <summary>死亡时才能使用</summary>
		Death = 1,

		/// <summary>活着才能使用</summary>
		Alive = 2
	}
}
