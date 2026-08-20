using System;
using System.Collections.Concurrent;
using System.IO;
using Newtonsoft.Json;
using Rests;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;
using TShockAPI.Hooks;

namespace TShockData
{
    /// <summary>
    /// 实时风控：进服拦截 + 发言/命令拦截 + 一键踢出。
    /// ServerJoin / ServerChat 钩子优先级 int.MaxValue，先于 TShock 框架处理。
    /// 注意：BlockUnder1hChat 场景下命令（以 CommandSpecifier 开头）一律放行，
    /// 否则 TShock.OnChat 因 args.Handled 直接 return，未登录玩家的 /login /register 将永远无法执行。
    /// </summary>
    public static class RiskControl
    {
        // ── 常量 ──
        private const string MsgBlockAllEnter = "服务器当前禁止所有玩家进入。";
        private const string MsgUnder1h = "您的游玩时间不足1小时，暂不允许进入服务器。";
        private const string MsgAdminKickAll = "管理员执行：全局踢出。";
        private const string SqlTotalPlayMinutes =
            "SELECT COALESCE(SUM(daily_min), 0) AS total FROM player_daily_stat WHERE uid = @0";

        private static TerrariaPlugin _plugin;
        private static bool _initialized;

        /// <summary>
        /// 游玩时长 TTL 缓存（键=玩家索引）。30 秒内复用，防刷屏玩家每次发言都触发 SQL；
        /// 超过 1 小时的玩家 30 秒内自动解除禁言，不会因陈旧值被永久误伤。
        /// </summary>
        private static readonly ConcurrentDictionary<int, (int Minutes, DateTime At)> _playtimeCache = new();
        private static readonly TimeSpan _playtimeCacheTtl = TimeSpan.FromSeconds(30);

        public static RiskConfig Config { get; private set; } = new RiskConfig();

        private static string ConfigPath => Path.Combine(TShock.SavePath, "TSWeb", "risk_control.json");

        public static void Initialize(TerrariaPlugin plugin)
        {
            if (_initialized) return;
            _plugin = plugin;
            LoadConfig();

            // int.MaxValue：在所有 TShock 自身钩子（优先级 0）之前执行
            ServerApi.Hooks.ServerJoin.Register(plugin, OnServerJoin, int.MaxValue);
            ServerApi.Hooks.ServerChat.Register(plugin, OnServerChat, int.MaxValue);
            // 登录完成再检查游玩时长（替代 Task.Run 轮询等待握手，无 3 秒窗口漏洞）
            PlayerHooks.PlayerPostLogin += OnPlayerPostLogin;
            // 断线清理缓存，避免长期运行残留
            ServerApi.Hooks.ServerLeave.Register(plugin, OnServerLeave);

            _initialized = true;
            TShock.Log.ConsoleInfo("[TSWeb] 实时风控已初始化（ServerJoin/ServerChat 优先级: int.MaxValue）");
        }

        public static void Dispose()
        {
            if (!_initialized) return;
            ServerApi.Hooks.ServerJoin.Deregister(_plugin, OnServerJoin);
            ServerApi.Hooks.ServerChat.Deregister(_plugin, OnServerChat);
            PlayerHooks.PlayerPostLogin -= OnPlayerPostLogin;
            ServerApi.Hooks.ServerLeave.Deregister(_plugin, OnServerLeave);
            _playtimeCache.Clear();
            _initialized = false;
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
                    Config = JsonConvert.DeserializeObject<RiskConfig>(json) ?? new RiskConfig();
                }
                else
                {
                    Config = new RiskConfig();
                    SaveConfig();
                }
                TShock.Log.ConsoleInfo(
                    $"[TSWeb] 风控配置已加载: 禁入={Config.BlockAllEnter}/{Config.BlockUnder1hEnter}, 禁言={Config.BlockAllChat}/{Config.BlockUnder1hChat}");
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 加载风控配置失败: {ex.Message}");
                Config = new RiskConfig();
            }
        }

        public static void SaveConfig()
        {
            try
            {
                var json = JsonConvert.SerializeObject(Config, Formatting.Indented);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 保存风控配置失败: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════
        // ServerJoin 钩子（全服禁入，立即拦截）
        // ═══════════════════════════════════════════

        private static void OnServerJoin(JoinEventArgs args)
        {
            if (args.Handled) return;

            if (!Config.BlockAllEnter) return;

            var player = GetActivePlayer(args.Who);
            if (player == null) return;

            args.Handled = true;
            SafeKick(player, MsgBlockAllEnter);
        }

        // ═══════════════════════════════════════════
        // PlayerPostLogin 钩子（不足1h 进服拦截）
        // ═══════════════════════════════════════════

        private static void OnPlayerPostLogin(PlayerPostLoginEventArgs e)
        {
            if (!Config.BlockUnder1hEnter) return;

            var player = e.Player;
            if (player == null) return;

            int minutes = GetPlaytimeMinutes(player.Name, player.Index);
            if (minutes >= 60) return;

            SafeKick(player, MsgUnder1h);
            TShock.Log.ConsoleInfo($"[TSWeb][RiskControl] 进服拦截（不足1h）: {player.Name} ({minutes}分钟)");
        }

        // ═══════════════════════════════════════════
        // ServerChat 钩子（发言/命令拦截）
        // ═══════════════════════════════════════════

        private static void OnServerChat(ServerChatEventArgs args)
        {
            if (args.Handled) return;

            var player = GetActivePlayer(args.Who);
            if (player == null) return;

            // 紧急开关：禁止所有玩家发言/命令（连命令一起拦，符合"含管理员"语义）
            if (Config.BlockAllChat)
            {
                args.Handled = true;
                return;
            }

            if (!Config.BlockUnder1hChat) return;

            // 命令放行：/login /register 等必须可达，否则未登录玩家永远无法登录
            if (IsCommand(args.Text)) return;

            // 未登录玩家：游玩时长为 0，一律禁言（普通消息）
            if (!player.IsLoggedIn)
            {
                args.Handled = true;
                return;
            }

            int minutes = GetPlaytimeMinutes(player.Name, player.Index);
            if (minutes < 60)
            {
                args.Handled = true; // 静默丢弃，不发送提示
            }
        }

        // ═══════════════════════════════════════════
        // ServerLeave 钩子（清理缓存）
        // ═══════════════════════════════════════════

        private static void OnServerLeave(LeaveEventArgs args)
        {
            _playtimeCache.TryRemove(args.Who, out _);
        }

        // ═══════════════════════════════════════════
        // REST API
        // ═══════════════════════════════════════════

        public static object GetConfigJson(RestRequestArgs args)
        {
            return new RestObject("200")
            {
                { "blockAllEnter", Config.BlockAllEnter },
                { "blockUnder1hEnter", Config.BlockUnder1hEnter },
                { "blockAllChat", Config.BlockAllChat },
                { "blockUnder1hChat", Config.BlockUnder1hChat },
            };
        }

        public static object SetConfigJson(RestRequestArgs args)
        {
            try
            {
                Config.BlockAllEnter = GetBool(args, "blockAllEnter", Config.BlockAllEnter);
                Config.BlockUnder1hEnter = GetBool(args, "blockUnder1hEnter", Config.BlockUnder1hEnter);
                Config.BlockAllChat = GetBool(args, "blockAllChat", Config.BlockAllChat);
                Config.BlockUnder1hChat = GetBool(args, "blockUnder1hChat", Config.BlockUnder1hChat);
                SaveConfig();
                TShock.Log.ConsoleInfo(
                    $"[TSWeb] REST 更新风控配置: 禁入={Config.BlockAllEnter}/{Config.BlockUnder1hEnter}, 禁言={Config.BlockAllChat}/{Config.BlockUnder1hChat}");
                return new RestObject("200") { { "message", "配置已保存" } };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }

        /// <summary>
        /// 执行一次性风控动作
        /// GET /data/riskcontrol/action?action=kick-all|kick-under-1h&token=xxx
        /// </summary>
        public static object ExecuteAction(RestRequestArgs args)
        {
            var action = args.Parameters["action"];
            int kicked = 0;

            switch (action)
            {
                case "kick-all":
                    foreach (var p in TShock.Players)
                    {
                        if (p != null && p.Active)
                        {
                            SafeKick(p, MsgAdminKickAll);
                            kicked++;
                        }
                    }
                    TShock.Log.ConsoleInfo($"[TSWeb][RiskControl] 已踢出所有玩家 ({kicked}人)");
                    break;

                case "kick-under-1h":
                    foreach (var p in TShock.Players)
                    {
                        if (p == null || !p.Active || !p.IsLoggedIn || p.Account == null) continue;

                        int minutes = GetPlaytimeMinutes(p.Name, p.Index);
                        if (minutes < 60)
                        {
                            SafeKick(p, MsgUnder1h);
                            kicked++;
                        }
                    }
                    TShock.Log.ConsoleInfo($"[TSWeb][RiskControl] 已踢出游玩时间不足1小时的玩家 ({kicked}人)");
                    break;

                default:
                    return new RestObject("400") { { "error", $"未知动作: {action}" } };
            }

            return new RestObject("200") { { "kicked", kicked } };
        }

        // ═══════════════════════════════════════════
        // 辅助方法
        // ═══════════════════════════════════════════

        private static TSPlayer GetActivePlayer(int who)
        {
            if (who < 0 || who >= TShock.Players.Length) return null;
            var p = TShock.Players[who];
            return p != null && p.Active ? p : null;
        }

        /// <summary>是否命令消息（按 TShock 配置的指令前缀识别，命令一律放行）</summary>
        private static bool IsCommand(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            var spec = TShock.Config.Settings.CommandSpecifier;
            var silentSpec = TShock.Config.Settings.CommandSilentSpecifier;
            return (!string.IsNullOrEmpty(spec) && text.StartsWith(spec, StringComparison.Ordinal))
                || (!string.IsNullOrEmpty(silentSpec) && text.StartsWith(silentSpec, StringComparison.Ordinal));
        }

        /// <summary>获取玩家累计游玩时长（分钟），30 秒 TTL 缓存复用</summary>
        private static int GetPlaytimeMinutes(string playerName, int playerIndex)
        {
            if (_playtimeCache.TryGetValue(playerIndex, out var hit)
                && DateTime.UtcNow - hit.At < _playtimeCacheTtl)
                return hit.Minutes;

            int minutes = QueryTotalPlayMinutes(playerName);
            _playtimeCache[playerIndex] = (minutes, DateTime.UtcNow);
            return minutes;
        }

        /// <summary>查询累计游玩时长（player_daily_stat 表）</summary>
        private static int QueryTotalPlayMinutes(string playerName)
        {
            try
            {
                using (var reader = TShock.DB.QueryReader(SqlTotalPlayMinutes, playerName))
                {
                    if (reader.Read())
                        return Convert.ToInt32(reader.Get<long>("total"));
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleWarn($"[TSWeb][RiskControl] 查询玩家 {playerName} 游玩时长失败: {ex.Message}");
            }
            return 0;
        }

        /// <summary>解析布尔参数；缺失或非法时保持原值</summary>
        private static bool GetBool(RestRequestArgs args, string key, bool current)
        {
            var val = args.Parameters[key];
            return val != null && bool.TryParse(val, out var result) ? result : current;
        }

        /// <summary>安全踢出（处理 player 可能已断开连接的情况）</summary>
        private static void SafeKick(TSPlayer player, string reason)
        {
            try
            {
                if (player != null && player.Active && player.ConnectionAlive)
                    player.Kick(reason, true);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb][RiskControl] 踢出玩家异常: {ex.Message}");
            }
        }
    }

    /// <summary>实时风控配置（JSON 持久化路径: {TShock.SavePath}/TSWeb/risk_control.json）</summary>
    public class RiskConfig
    {
        // ── 进服限制（持久开关）──
        [JsonProperty("blockAllEnter")]
        public bool BlockAllEnter { get; set; }

        [JsonProperty("blockUnder1hEnter")]
        public bool BlockUnder1hEnter { get; set; }

        // ── 发言/命令限制（持久开关）──
        [JsonProperty("blockAllChat")]
        public bool BlockAllChat { get; set; }

        [JsonProperty("blockUnder1hChat")]
        public bool BlockUnder1hChat { get; set; }
    }
}
