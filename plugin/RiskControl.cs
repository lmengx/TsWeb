using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
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
    /// ServerJoin / ServerChat 钩子优先级 int.MaxValue，在 TShock 框架处理之前拦截。
    /// ChatMessage 包被 ServerChat 拦截后，TShock 的命令解析器永远不会收到它。
    /// </summary>
    public static class RiskControl
    {
        private static TerrariaPlugin _plugin;
        private static bool _initialized;
        private static string ConfigPath => Path.Combine(TShock.SavePath, "TSWeb", "risk_control.json");
        private static bool _loaded;

        public static RiskConfig Config { get; private set; } = new RiskConfig();

        // 缓存玩家累计游玩时长（分钟），玩家进服时异步查询并缓存
        private static readonly ConcurrentDictionary<int, int> _playtimeCache = new();

        public static void Initialize(TerrariaPlugin plugin)
        {
            if (_initialized) return;
            _plugin = plugin;
            LoadConfig();

            // int.MaxValue：在所有 TShock 自身钩子（优先级 0）之前执行
            ServerApi.Hooks.ServerJoin.Register(plugin, OnServerJoin, int.MaxValue);
            ServerApi.Hooks.ServerChat.Register(plugin, OnServerChat, int.MaxValue);

            _initialized = true;
            TShock.Log.ConsoleInfo("[TSWeb] 实时风控已初始化（ServerJoin/ServerChat 优先级: int.MaxValue）");
        }

        public static void Dispose()
        {
            if (!_initialized) return;
            ServerApi.Hooks.ServerJoin.Deregister(_plugin, OnServerJoin);
            ServerApi.Hooks.ServerChat.Deregister(_plugin, OnServerChat);
            _playtimeCache.Clear();
            _initialized = false;
        }

        // ═══════════════════════════════════════════
        // 配置读写
        // ═══════════════════════════════════════════

        public static void LoadConfig()
        {
            if (_loaded) return;
            _loaded = true;
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
        // ServerJoin 钩子（进服拦截）
        // ═══════════════════════════════════════════

        private static void OnServerJoin(JoinEventArgs args)
        {
            if (args.Handled) return;

            var who = args.Who;
            if (who < 0 || who >= TShock.Players.Length) return;

            var player = TShock.Players[who];
            if (player == null) return;

            // 立即拦截：禁止所有玩家
            if (Config.BlockAllEnter)
            {
                args.Handled = true;
                SafeKick(player, "服务器当前禁止所有玩家进入。");
                return;
            }

            // 异步检查游玩时间（玩家此时尚未加载角色数据，需等待 FinishedHandshake）
            if (Config.BlockUnder1hEnter)
            {
                Task.Run(async () =>
                {
                    // 等待角色数据加载（最多 3 秒）
                    for (int i = 0; i < 300; i++)
                    {
                        await Task.Delay(10);
                        var target = TShock.Players[who];
                        if (target == null || !target.ConnectionAlive) return;
                        if (!target.FinishedHandshake || !target.Active) continue;

                        if (!target.IsLoggedIn || target.Account == null)
                            return; // 未登录玩家由注册逻辑处理

                        int minutes = await GetTotalPlayTimeMinutesAsync(target.Name);
                        // 写入缓存，供后续聊天检查使用
                        _playtimeCache[who] = minutes;

                        if (minutes < 60)
                        {
                            SafeKick(target, "您的游玩时间不足1小时，暂不允许进入服务器。");
                            TShock.Log.ConsoleInfo(
                                $"[TSWeb][RiskControl] 进服拦截（不足1h）: {target.Name} ({minutes}分钟)");
                        }
                        return;
                    }
                });
            }
        }

        // ═══════════════════════════════════════════
        // ServerChat 钩子（发言/命令拦截）
        // ═══════════════════════════════════════════

        private static void OnServerChat(ServerChatEventArgs args)
        {
            if (args.Handled) return;

            var who = args.Who;
            if (who < 0 || who >= TShock.Players.Length) return;

            var player = TShock.Players[who];
            if (player == null || !player.Active) return;

            // 禁止所有玩家发言/命令
            if (Config.BlockAllChat)
            {
                args.Handled = true;
                return;
            }

            // 禁止不足1小时玩家发言/命令（查缓存，无缓存则静默放行避免阻断正常玩家）
            if (Config.BlockUnder1hChat && _playtimeCache.TryGetValue(who, out var cachedMinutes))
            {
                if (cachedMinutes < 60)
                {
                    args.Handled = true;
                    // 静默丢弃，不发送提示
                }
            }
        }

        // ═══════════════════════════════════════════
        // REST API
        // ═══════════════════════════════════════════

        public static object GetConfigJson(RestRequestArgs args)
        {
            return new
            {
                status = "200",
                blockAllEnter = Config.BlockAllEnter,
                blockUnder1hEnter = Config.BlockUnder1hEnter,
                blockAllChat = Config.BlockAllChat,
                blockUnder1hChat = Config.BlockUnder1hChat,
            };
        }

        public static object SetConfigJson(RestRequestArgs args)
        {
            try
            {
                ParseBool(args, "blockAllEnter", v => Config.BlockAllEnter = v);
                ParseBool(args, "blockUnder1hEnter", v => Config.BlockUnder1hEnter = v);
                ParseBool(args, "blockAllChat", v => Config.BlockAllChat = v);
                ParseBool(args, "blockUnder1hChat", v => Config.BlockUnder1hChat = v);
                SaveConfig();
                TShock.Log.ConsoleInfo(
                    $"[TSWeb] REST 更新风控配置: 禁入={Config.BlockAllEnter}/{Config.BlockUnder1hEnter}, 禁言={Config.BlockAllChat}/{Config.BlockUnder1hChat}");
                return new { status = "200", message = "配置已保存" };
            }
            catch (Exception ex)
            {
                return new { status = "500", error = ex.Message };
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
                            SafeKick(p, "管理员执行：全局踢出。");
                            kicked++;
                        }
                    }
                    TShock.Log.ConsoleInfo($"[TSWeb][RiskControl] 已踢出所有玩家 ({kicked}人)");
                    break;

                case "kick-under-1h":
                    foreach (var p in TShock.Players)
                    {
                        if (p != null && p.Active && p.IsLoggedIn && p.Account != null)
                        {
                            // 优先用缓存，无缓存则实时查库
                            int minutes;
                            if (!_playtimeCache.TryGetValue(p.Index, out minutes))
                                minutes = GetTotalPlayTimeMinutesSync(p.Name);
                            if (minutes < 60)
                            {
                                SafeKick(p, "您的游玩时间不足1小时，暂不允许进入服务器。");
                                kicked++;
                            }
                        }
                    }
                    TShock.Log.ConsoleInfo($"[TSWeb][RiskControl] 已踢出游玩时间不足1小时的玩家 ({kicked}人)");
                    break;

                default:
                    return new { status = "400", error = $"未知动作: {action}" };
            }

            return new { status = "200", kicked };
        }

        // ═══════════════════════════════════════════
        // 辅助方法
        // ═══════════════════════════════════════════

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

        /// <summary>异步查询玩家累计游玩时长（分钟），来自 player_daily_stat 表</summary>
        private static async Task<int> GetTotalPlayTimeMinutesAsync(string playerName)
        {
            try
            {
                using (var reader = TShock.DB.QueryReader(
                    "SELECT COALESCE(SUM(daily_min), 0) AS total FROM player_daily_stat WHERE uid = @0",
                    playerName))
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

        /// <summary>同步查询（仅用于 kick-under-1h 一次性动作）</summary>
        private static int GetTotalPlayTimeMinutesSync(string playerName)
        {
            try
            {
                using (var reader = TShock.DB.QueryReader(
                    "SELECT COALESCE(SUM(daily_min), 0) AS total FROM player_daily_stat WHERE uid = @0",
                    playerName))
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

        private static void ParseBool(RestRequestArgs args, string key, Action<bool> setter)
        {
            var val = args.Parameters[key];
            if (string.IsNullOrEmpty(val)) return;
            if (bool.TryParse(val, out var result))
                setter(result);
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
