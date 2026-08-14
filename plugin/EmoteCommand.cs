using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Rests;
using TShockAPI;

namespace TShockData
{
    /// <summary>
    /// 单个表情触发规则：一个表情 ID 对应一组按顺序执行的指令。
    /// </summary>
    public class EmoteRuleConfig
    {
        /// <summary>表情 ID（EmoteID，如 0=爱心）</summary>
        [JsonProperty("emojiId")]
        public int EmojiId { get; set; } = 0;

        /// <summary>是否启用该规则</summary>
        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 指令列表，一条记录一个指令串；触发时按顺序逐条执行（执行完一条再执行下一条）。
        /// 每条支持 {player} 占位符（替换为触发者名字）。
        /// </summary>
        [JsonProperty("commands")]
        public List<string> Commands { get; set; } = new();

        /// <summary>备注（可选，仅前端展示）</summary>
        [JsonProperty("remark")]
        public string Remark { get; set; } = "";

        /// <summary>忽略权限执行：勾选后以玩家身份执行，但不做权限检查</summary>
        [JsonProperty("ignorePermission")]
        public bool IgnorePermission { get; set; } = false;
    }

    /// <summary>
    /// 表情指令根配置
    /// </summary>
    public class EmoteConfig
    {
        /// <summary>同一玩家两次表情触发之间的最小间隔（毫秒），防连点刷指令；0 = 不限制</summary>
        [JsonProperty("cooldownMs")]
        public long CooldownMs { get; set; } = 2000;

        [JsonProperty("emotes")]
        public List<EmoteRuleConfig> Emotes { get; set; } = new();
    }

    /// <summary>
    /// 表情指令管理器：监听玩家表情包（120 号 Emoji → GetDataHandlers.Emoji），
    /// 按 EmojiID 匹配规则，顺序执行配置的指令。
    /// </summary>
    public static class EmoteCommandManager
    {
        private static EmoteConfig _config = new();
        private static string ConfigPath => Path.Combine(TShock.SavePath, "TSWeb", "emote_command_config.json");
        private static bool _loaded = false;

        // 每玩家上次触发时间（whoAmI → 毫秒时间戳）。槽位数量有限（≤255），不会泄漏；
        // slot 复用后残留时间戳最多让新玩家多等一个冷却周期，无实际影响。
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, long> _lastTrigger = new();

        // ═══════════════════════════════════════════════
        // 生命周期
        // ═══════════════════════════════════════════════

        public static void Initialize()
        {
            EnsureLoaded();
            GetDataHandlers.Emoji.Register(OnGetEmoji);
            TShock.Log.ConsoleInfo("[TSWeb] 表情指令模块已初始化");
        }

        public static void Dispose()
        {
            GetDataHandlers.Emoji.UnRegister(OnGetEmoji);
        }

        private static void EnsureLoaded()
        {
            if (!_loaded)
            {
                LoadConfig();
                _loaded = true;
            }
        }

        // ═══════════════════════════════════════════════
        // 配置读写
        // ═══════════════════════════════════════════════

        public static void LoadConfig()
        {
            try
            {
                var directory = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    _config = JsonConvert.DeserializeObject<EmoteConfig>(json) ?? new EmoteConfig();
                    TShock.Log.ConsoleInfo("[TSWeb] 表情指令配置已加载");
                }
                else
                {
                    _config = new EmoteConfig();
                    SaveConfig();
                    TShock.Log.ConsoleInfo("[TSWeb] 已创建默认表情指令配置");
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 加载表情指令配置失败: {ex.Message}");
                _config = new EmoteConfig();
            }
        }

        public static void SaveConfig()
        {
            try
            {
                var directory = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var json = JsonConvert.SerializeObject(_config, Formatting.Indented);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 保存表情指令配置失败: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════
        // 核心执行逻辑
        // ═══════════════════════════════════════════════

        /// <summary>
        /// 表情包事件：匹配该 EmojiID 的所有启用规则，按顺序逐条执行指令。
        /// 一个表情可配置多组指令（多条规则 / 一条规则内多条指令）。
        /// </summary>
        private static void OnGetEmoji(object sender, GetDataHandlers.EmojiEventArgs e)
        {
            if (e.Player == null)
                return;

            // 冷却：同一玩家两次表情触发至少间隔 cooldownMs（防连点刷指令，如刷发物/刷屏）
            var cooldown = _config.CooldownMs;
            if (cooldown > 0)
            {
                var now = Environment.TickCount64;
                if (_lastTrigger.TryGetValue(e.Player.Index, out var last) && now - last < cooldown)
                    return;
                _lastTrigger[e.Player.Index] = now;
            }

            var rules = _config.Emotes.Where(r => r.Enabled && r.EmojiId == e.EmojiID);
            foreach (var rule in rules)
            {
                foreach (var cmd in rule.Commands)
                {
                    if (string.IsNullOrWhiteSpace(cmd))
                        continue;

                    ExecuteCommand(e.Player, cmd, rule.IgnorePermission);
                }
            }
        }

        /// <summary>
        /// 以玩家身份执行一条指令：替换 {player} 占位符，自动补命令前缀。
        /// ignorePermission=true 时通过 BypassHelper 跳过权限检查（玩家身份不变）。
        /// </summary>
        private static void ExecuteCommand(TSPlayer player, string command, bool ignorePermission)
        {
            var cmd = command.Replace("{player}", player.Name).Trim();
            if (cmd.Length == 0)
                return;

            // Commands.HandleCommand 要求首字符为命令前缀（/ 或 .），未带则自动补 /
            if (cmd[0] != '/' && cmd[0] != '.')
                cmd = "/" + cmd;

            try
            {
                if (ignorePermission)
                {
                    // 玩家身份执行 + 跳过权限检查（BypassHelper 权限钩子，与 runas -f 同机制）
                    BypassHelper.RunWithoutPermissionChecks(
                        () => Commands.HandleCommand(player, cmd), player);
                }
                else
                {
                    Commands.HandleCommand(player, cmd);
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 表情指令执行失败: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════
        // REST API
        // ═══════════════════════════════════════════════

        public static object GetConfigJson(RestRequestArgs args)
        {
            EnsureLoaded();

            // 手工构建小写字段名，JavaScriptSerializer 不认 [JsonProperty]
            var emotes = new List<object>();
            foreach (var r in _config.Emotes)
            {
                emotes.Add(new Dictionary<string, object>
                {
                    { "emojiId", r.EmojiId },
                    { "enabled", r.Enabled },
                    { "commands", new List<string>(r.Commands ?? new List<string>()) },
                    { "remark", r.Remark ?? "" },
                    { "ignorePermission", r.IgnorePermission }
                });
            }

            return new RestObject
            {
                { "cooldownMs", _config.CooldownMs },
                { "emotes", emotes }
            };
        }

        public static object SetConfigJson(RestRequestArgs args)
        {
            try
            {
                string configJson = null;
                try { configJson = args.Parameters["config"]; } catch { }

                if (string.IsNullOrEmpty(configJson))
                    return new RestObject("400") { { "error", "缺少 config 参数" } };

                var parsed = JsonConvert.DeserializeObject<EmoteConfig>(configJson);
                if (parsed == null)
                    return new RestObject("400") { { "error", "配置解析失败" } };

                // 防御性清洗
                if (parsed.CooldownMs < 0) parsed.CooldownMs = 0; // 冷却最小 0（不限制）
                if (parsed.Emotes == null)
                    parsed.Emotes = new List<EmoteRuleConfig>();
                foreach (var rule in parsed.Emotes)
                {
                    if (rule.Commands == null)
                        rule.Commands = new List<string>();
                    rule.Remark = rule.Remark ?? "";
                    // 过滤空指令串
                    rule.Commands = rule.Commands
                        .Where(c => !string.IsNullOrWhiteSpace(c))
                        .ToList();
                }

                _config = parsed;
                SaveConfig();

                TShock.Log.ConsoleInfo("[TSWeb] 表情指令配置已通过 REST API 更新");
                return new RestObject { { "response", "配置已保存" } };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }
    }
}
