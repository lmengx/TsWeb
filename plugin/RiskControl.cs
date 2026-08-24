using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
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
    /// 实时风控：进服拦截 + 发言/命令拦截 + 一键踢出 + 代理/海外检测（ip.sb）。
    /// 交互模型：操作（禁止进入/禁言/踢出）× 目标群体（所有/海外代理/注册不足1h/游玩不足1h/未登录）。
    /// 目标间为 AND 放行语义：任一命中即拦截，全部通过才放行。
    /// ServerJoin / ServerChat 钩子优先级 int.MaxValue，先于 TShock 框架处理。
    /// 注意：命令（以 CommandSpecifier 开头）一律放行，否则 TShock.OnChat 因 args.Handled
    /// 直接 return，未登录玩家的 /login /register 将永远无法执行。
    /// 代理检测（紧急模式）：海外/非四大运营商直接拦截；ip.sb 正常响应但无法判定（unknown）也拦截；
    /// ip.sb 失联/报错（unavailable）则整个 proxy 目标放行，不影响其他目标。
    /// </summary>
    public static class RiskControl
    {
        // ── 常量 ──
        private const string MsgBlockAllEnter = "服务器当前禁止所有玩家进入。";
        private const string MsgUnder1h = "您的游玩时间不足1小时，暂不允许进入服务器。";
        private const string MsgRegisterUnder1h = "您的账号注册时间不足1小时，暂不允许进入服务器。";
        private const string MsgProxyBlocked = "您当前使用的网络属于海外代理/非中国大陆四大运营商，暂不允许进入服务器。";
        private const string MsgAdminKick = "管理员执行：风控踢出。";
        private const string SqlTotalPlayMinutes =
            "SELECT COALESCE(SUM(daily_min), 0) AS total FROM player_daily_stat WHERE uid = @0";
        private const string SqlQqBound =
            "SELECT COUNT(1) AS C FROM qq_bind WHERE UserId = @0";

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
            // 登录完成再检查游玩时长/注册时间/代理（替代 Task.Run 轮询等待握手，无 3 秒窗口漏洞）
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
            ProxyDetector.Shutdown();
            _initialized = false;
        }

        // ═══════════════════════════════════════════
        // 配置读写（v2，自动迁移旧版）
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
                    using (var doc = JsonDocument.Parse(json))
                    {
                        if (doc.RootElement.TryGetProperty("version", out var v)
                            && v.ValueKind == JsonValueKind.Number && v.GetInt32() >= 2)
                        {
                            Config = JsonConvert.DeserializeObject<RiskConfig>(json) ?? new RiskConfig();
                        }
                        else
                        {
                            Config = MigrateLegacy(json);
                            SaveConfig();
                            TShock.Log.ConsoleInfo("[TSWeb] 风控配置已从旧版迁移至 v2");
                        }
                    }
                }
                else
                {
                    Config = new RiskConfig();
                    SaveConfig();
                }
                TShock.Log.ConsoleInfo(
                    $"[TSWeb] 风控配置已加载: 禁入={Config.BlockEnter.Enabled}[{string.Join(",", Config.BlockEnter.Targets)}], " +
                    $"禁言={Config.BlockChat.Enabled}[{string.Join(",", Config.BlockChat.Targets)}], " +
                    $"QQ豁免={Config.QqBindExempt}, 豁免组={string.Join(",", Config.ExemptGroups)}");
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

        /// <summary>旧版（v1 布尔开关）配置迁移</summary>
        private static RiskConfig MigrateLegacy(string json)
        {
            var cfg = new RiskConfig();
            try
            {
                var legacy = JsonConvert.DeserializeObject<LegacyRiskConfig>(json);
                if (legacy == null) return cfg;

                var enter = new List<string>();
                if (legacy.BlockAllEnter) enter.Add(RiskTarget.All);
                if (legacy.BlockUnder1hEnter) enter.Add(RiskTarget.PlaytimeUnder1h);
                cfg.BlockEnter = new BlockRule { Enabled = enter.Count > 0, Targets = enter };

                var chat = new List<string>();
                if (legacy.BlockAllChat) chat.Add(RiskTarget.All);
                if (legacy.BlockUnder1hChat) chat.Add(RiskTarget.PlaytimeUnder1h);
                cfg.BlockChat = new BlockRule { Enabled = chat.Count > 0, Targets = chat };
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 风控配置迁移失败，使用默认配置: {ex.Message}");
            }
            return cfg;
        }

        private class LegacyRiskConfig
        {
            [JsonProperty("blockAllEnter")] public bool BlockAllEnter { get; set; }
            [JsonProperty("blockUnder1hEnter")] public bool BlockUnder1hEnter { get; set; }
            [JsonProperty("blockAllChat")] public bool BlockAllChat { get; set; }
            [JsonProperty("blockUnder1hChat")] public bool BlockUnder1hChat { get; set; }
        }

        // ═══════════════════════════════════════════
        // ServerJoin 钩子（全服禁入，立即拦截，不豁免）
        // ═══════════════════════════════════════════

        private static void OnServerJoin(JoinEventArgs args)
        {
            if (args.Handled) return;
            if (!Config.BlockEnter.Enabled) return;
            if (!Config.BlockEnter.Targets.Contains(RiskTarget.All)) return;

            var player = GetActivePlayer(args.Who);
            if (player == null) return;

            args.Handled = true;
            SafeKick(player, MsgBlockAllEnter);
        }

        // ═══════════════════════════════════════════
        // PlayerPostLogin 钩子（按目标拦截，豁免组/QQ豁免生效）
        // ═══════════════════════════════════════════

        private static void OnPlayerPostLogin(PlayerPostLoginEventArgs e)
        {
            if (!Config.BlockEnter.Enabled) return;

            var player = e.Player;
            if (player == null || !player.Active) return;

            // 前置豁免：豁免组 / 绑定 QQ 玩家直接放行
            if (IsExempt(player)) return;
            if (Config.QqBindExempt && player.Account != null && IsQqBound(player)) return;

            var targets = Config.BlockEnter.Targets;
            if (targets.Count == 0) return;

            // 1. 注册不足 1 小时
            if (targets.Contains(RiskTarget.RegisterUnder1h)
                && player.Account != null && IsRegisterUnder1h(player))
            {
                SafeKick(player, MsgRegisterUnder1h);
                TShock.Log.ConsoleInfo($"[TSWeb][RiskControl] 进服拦截（注册不足1h）: {player.Name}");
                return;
            }

            // 2. 游玩时间不足 1 小时
            if (targets.Contains(RiskTarget.PlaytimeUnder1h))
            {
                int minutes = GetPlaytimeMinutes(player.Name, player.Index);
                if (minutes < 60)
                {
                    SafeKick(player, MsgUnder1h);
                    TShock.Log.ConsoleInfo($"[TSWeb][RiskControl] 进服拦截（游玩不足1h）: {player.Name} ({minutes}分钟)");
                    return;
                }
            }

            // 3. 海外/代理（紧急模式，异步检测不阻塞登录线程）
            if (targets.Contains(RiskTarget.Proxy))
            {
                CheckProxyOnEnter(player);
            }
        }

        /// <summary>进服代理判定：缓存命中同步踢；未命中异步检测后回调踢</summary>
        private static void CheckProxyOnEnter(TSPlayer player)
        {
            if (!Config.Proxy.Enabled) return;

            var ip = player.IP;
            if (string.IsNullOrEmpty(ip)) return;

            var cached = ProxyDetector.LookupCached(ip);
            if (cached != null)
            {
                if (cached.Status == ProxyStatus.Proxy || cached.Status == ProxyStatus.Unknown)
                {
                    SafeKick(player, MsgProxyBlocked);
                    TShock.Log.ConsoleInfo($"[TSWeb][RiskControl] 进服拦截（代理/海外）: {player.Name} [{ip}] {cached.Isp}");
                }
                return;
            }

            var who = player.Index;
            ProxyDetector.GetOrStartLookupAsync(ip).ContinueWith(t =>
            {
                try
                {
                    var info = t.Result;
                    if (info.Status != ProxyStatus.Proxy && info.Status != ProxyStatus.Unknown) return;

                    var p = GetActivePlayer(who);
                    if (p == null) return;
                    // 复查：规则仍启用 + 仍选中该目标 + 仍非豁免
                    if (!Config.BlockEnter.Enabled || !Config.BlockEnter.Targets.Contains(RiskTarget.Proxy)) return;
                    if (IsExempt(p)) return;
                    if (Config.QqBindExempt && p.Account != null && IsQqBound(p)) return;

                    SafeKick(p, MsgProxyBlocked);
                    TShock.Log.ConsoleInfo($"[TSWeb][RiskControl] 进服拦截（代理/海外,异步）: {p.Name} [{ip}] {info.Isp}");
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleWarn($"[TSWeb][RiskControl] 代理进服拦截异常: {ex.Message}");
                }
            }, System.Threading.Tasks.TaskScheduler.Default);
        }

        // ═══════════════════════════════════════════
        // ServerChat 钩子（发言/命令拦截）
        // ═══════════════════════════════════════════

        private static void OnServerChat(ServerChatEventArgs args)
        {
            if (args.Handled) return;

            var player = GetActivePlayer(args.Who);
            if (player == null) return;
            if (!Config.BlockChat.Enabled) return;

            // 命令放行：/login /register 等必须可达，否则未登录玩家永远无法登录
            if (IsCommand(args.Text)) return;

            // 豁免组放行
            if (IsExempt(player)) return;

            // QQ 绑定豁免（需已登录有账号）
            if (Config.QqBindExempt && player.IsLoggedIn && player.Account != null && IsQqBound(player)) return;

            var targets = Config.BlockChat.Targets;
            if (targets.Count == 0) return;

            if (targets.Contains(RiskTarget.All))
            {
                args.Handled = true;
                return;
            }

            if (targets.Contains(RiskTarget.NotLoggedIn) && !player.IsLoggedIn)
            {
                args.Handled = true; // 静默丢弃，不发送提示
                return;
            }

            if (targets.Contains(RiskTarget.RegisterUnder1h)
                && player.IsLoggedIn && player.Account != null && IsRegisterUnder1h(player))
            {
                args.Handled = true;
                return;
            }

            if (targets.Contains(RiskTarget.PlaytimeUnder1h) && player.IsLoggedIn)
            {
                int minutes = GetPlaytimeMinutes(player.Name, player.Index);
                if (minutes < 60)
                {
                    args.Handled = true;
                    return;
                }
            }

            if (targets.Contains(RiskTarget.Proxy) && Config.Proxy.Enabled)
            {
                var ip = player.IP;
                if (string.IsNullOrEmpty(ip)) return;

                var cached = ProxyDetector.LookupCached(ip);
                if (cached != null)
                {
                    if (cached.Status == ProxyStatus.Proxy || cached.Status == ProxyStatus.Unknown)
                    {
                        args.Handled = true;
                    }
                }
                else
                {
                    // 未缓存：不拦截（避免卡服），异步预热供后续判定
                    ProxyDetector.GetOrStartLookupAsync(ip);
                }
            }
        }

        // ═══════════════════════════════════════════
        // ServerLeave 钩子（清理游玩时长缓存）
        // ═══════════════════════════════════════════

        private static void OnServerLeave(LeaveEventArgs args)
        {
            _playtimeCache.TryRemove(args.Who, out _);
        }

        // ═══════════════════════════════════════════
        // REST API：配置读写（v2）
        // ═══════════════════════════════════════════

        public static object GetConfigJson(RestRequestArgs args)
        {
            return new RestObject("200")
            {
                { "version", Config.Version },
                { "blockEnter", new Dictionary<string, object> {
                    { "enabled", Config.BlockEnter.Enabled },
                    { "targets", Config.BlockEnter.Targets } } },
                { "blockChat", new Dictionary<string, object> {
                    { "enabled", Config.BlockChat.Enabled },
                    { "targets", Config.BlockChat.Targets } } },
                { "qqBindExempt", Config.QqBindExempt },
                { "exemptGroups", Config.ExemptGroups },
                { "proxy", new Dictionary<string, object> {
                    { "enabled", Config.Proxy.Enabled },
                    { "cacheTtlHours", Config.Proxy.CacheTtlHours },
                    { "allowIsps", Config.Proxy.AllowIsps },
                    { "proxyKeywords", Config.Proxy.ProxyKeywords } } },
            };
        }

        /// <summary>
        /// 保存配置（扁平 query 参数，数组用逗号分隔）：
        /// blockEnterEnabled / blockEnterTargets / blockChatEnabled / blockChatTargets /
        /// qqBindExempt / exemptGroups / proxyEnabled / proxyCacheTtlHours / proxyAllowIsps / proxyProxyKeywords
        /// 未传的参数保持原值。
        /// </summary>
        public static object SetConfigJson(RestRequestArgs args)
        {
            try
            {
                Config.BlockEnter.Enabled = GetBool(args, "blockEnterEnabled", Config.BlockEnter.Enabled);
                var enterTargets = GetCsv(args, "blockEnterTargets");
                if (enterTargets != null) Config.BlockEnter.Targets = enterTargets;

                Config.BlockChat.Enabled = GetBool(args, "blockChatEnabled", Config.BlockChat.Enabled);
                var chatTargets = GetCsv(args, "blockChatTargets");
                if (chatTargets != null) Config.BlockChat.Targets = chatTargets;

                Config.QqBindExempt = GetBool(args, "qqBindExempt", Config.QqBindExempt);

                var groups = GetCsv(args, "exemptGroups");
                if (groups != null) Config.ExemptGroups = groups;

                Config.Proxy.Enabled = GetBool(args, "proxyEnabled", Config.Proxy.Enabled);
                var ttl = GetInt(args, "proxyCacheTtlHours", Config.Proxy.CacheTtlHours);
                if (ttl > 0) Config.Proxy.CacheTtlHours = ttl;
                var isps = GetCsv(args, "proxyAllowIsps");
                if (isps != null) Config.Proxy.AllowIsps = isps;
                var keywords = GetCsv(args, "proxyProxyKeywords");
                if (keywords != null) Config.Proxy.ProxyKeywords = keywords;

                SaveConfig();
                TShock.Log.ConsoleInfo(
                    $"[TSWeb] REST 更新风控配置: 禁入={Config.BlockEnter.Enabled}[{string.Join(",", Config.BlockEnter.Targets)}], " +
                    $"禁言={Config.BlockChat.Enabled}[{string.Join(",", Config.BlockChat.Targets)}], " +
                    $"QQ豁免={Config.QqBindExempt}, 豁免组={string.Join(",", Config.ExemptGroups)}");
                return new RestObject("200") { { "message", "配置已保存" } };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }

        // ═══════════════════════════════════════════
        // REST API：在线玩家特征（群体命中计算数据源）
        // ═══════════════════════════════════════════

        /// <summary>
        /// GET /data/riskcontrol/players
        /// 返回在线玩家的完整特征（登录态/QQ绑定/注册时间/游玩时长/代理判定），
        /// 前端本地按目标群体规则计算命中名单。未缓存的 IP 触发后台异步检测并最多等待 3 秒。
        /// </summary>
        public static object GetPlayersRest(RestRequestArgs args)
        {
            var players = TShock.Players.Where(p => p != null && p.Active).ToList();
            var proxyEnabled = Config.Proxy.Enabled;

            // 触发所有代理检测（缓存命中立即返回，未命中共享同一任务）
            var tasks = new List<Task<ProxyInfo>>();
            if (proxyEnabled)
            {
                foreach (var p in players)
                {
                    if (!string.IsNullOrEmpty(p.IP))
                        tasks.Add(ProxyDetector.GetOrStartLookupAsync(p.IP));
                }
                if (tasks.Count > 0)
                {
                    try { Task.WaitAll(tasks.Cast<Task>().ToArray(), TimeSpan.FromSeconds(3)); }
                    catch { /* 超时部分保持 pending */ }
                }
            }

            var list = new List<object>();
            int proxyCount = 0, unknownCount = 0, pendingCount = 0;
            foreach (var p in players)
            {
                var entry = new Dictionary<string, object>
                {
                    ["name"] = p.Name ?? "",
                    ["ip"] = p.IP ?? "",
                    ["group"] = p.Group?.Name ?? "",
                    ["loggedIn"] = p.IsLoggedIn,
                };

                if (p.IsLoggedIn && p.Account != null)
                {
                    entry["qqBound"] = IsQqBound(p);
                    entry["registerMinutesAgo"] = GetRegisterMinutesAgo(p);
                }
                if (p.IsLoggedIn)
                    entry["playtimeMinutes"] = GetPlaytimeMinutes(p.Name, p.Index);

                var ip = p.IP;
                if (!proxyEnabled)
                {
                    entry["proxyStatus"] = "disabled";
                }
                else
                {
                    var cached = string.IsNullOrEmpty(ip) ? null : ProxyDetector.LookupCached(ip);
                    if (cached != null)
                    {
                        entry["proxyStatus"] = cached.Status.ToString().ToLowerInvariant();
                        entry["geo"] = new Dictionary<string, object>
                        {
                            ["country"] = cached.Country ?? "",
                            ["region"] = cached.Region ?? "",
                            ["city"] = cached.City ?? "",
                            ["isp"] = cached.Isp ?? "",
                            ["organization"] = cached.Organization ?? "",
                            ["asn"] = cached.Asn,
                        };
                        if (cached.Status == ProxyStatus.Proxy) proxyCount++;
                        else if (cached.Status == ProxyStatus.Unknown) unknownCount++;
                    }
                    else
                    {
                        entry["proxyStatus"] = "pending";
                        pendingCount++;
                    }
                }
                list.Add(entry);
            }

            return new RestObject("200")
            {
                { "apiHealth", ProxyDetector.ApiHealth ? "ok" : "degraded" },
                { "proxyEnabled", Config.Proxy.Enabled },
                { "proxyCount", proxyCount },
                { "unknownCount", unknownCount },
                { "pending", pendingCount },
                { "players", list },
            };
        }

        /// <summary>
        /// POST /data/riskcontrol/proxy/refresh
        /// 强制刷新代理检测缓存（无 ip 参数=清空全部，有 ip=清单个）。
        /// </summary>
        public static object RefreshProxyRest(RestRequestArgs args)
        {
            var ip = args.Parameters["ip"];
            if (string.IsNullOrEmpty(ip))
            {
                ProxyDetector.RefreshAll();
                TShock.Log.ConsoleInfo("[TSWeb][RiskControl] 代理检测缓存已全部刷新");
                return new RestObject("200") { { "message", "代理检测缓存已全部刷新" } };
            }
            ProxyDetector.Refresh(ip);
            return new RestObject("200") { { "message", $"代理检测缓存已刷新: {ip}" } };
        }

        // ═══════════════════════════════════════════
        // REST API：一次性动作（参数化踢出，只扫在线）
        // ═══════════════════════════════════════════

        /// <summary>
        /// POST /data/riskcontrol/action?action=kick&targets=proxy,register-under-1h
        /// 兼容旧动作：kick-all（=targets=all）、kick-under-1h（=targets=playtime-under-1h）。
        /// </summary>
        public static object ExecuteAction(RestRequestArgs args)
        {
            var action = args.Parameters["action"] ?? "";
            List<string> targets;

            switch (action)
            {
                case "kick-all":
                    targets = new List<string> { RiskTarget.All };
                    break;
                case "kick-under-1h":
                    targets = new List<string> { RiskTarget.PlaytimeUnder1h };
                    break;
                case "kick":
                    targets = GetCsv(args, "targets") ?? new List<string>();
                    if (targets.Count == 0)
                        return new RestObject("400") { { "error", "缺少 targets 参数（逗号分隔）" } };
                    break;
                default:
                    return new RestObject("400") { { "error", $"未知动作: {action}" } };
            }

            var players = TShock.Players.Where(p => p != null && p.Active).ToList();

            // 代理目标：预触发所有未缓存 IP 的检测并等待，保证"执行即可"的即时性
            if (targets.Contains(RiskTarget.Proxy) && Config.Proxy.Enabled)
            {
                var tasks = new List<Task<ProxyInfo>>();
                foreach (var p in players)
                {
                    if (!string.IsNullOrEmpty(p.IP))
                        tasks.Add(ProxyDetector.GetOrStartLookupAsync(p.IP));
                }
                if (tasks.Count > 0)
                {
                    try { Task.WaitAll(tasks.Cast<Task>().ToArray(), TimeSpan.FromSeconds(8)); }
                    catch { /* 超时部分按未命中处理（fail-open） */ }
                }
            }

            int kicked = 0;
            foreach (var p in players)
            {
                if (ShouldKick(p, targets))
                {
                    SafeKick(p, MsgAdminKick);
                    kicked++;
                }
            }
            TShock.Log.ConsoleInfo($"[TSWeb][RiskControl] 执行踢出 [targets={string.Join(",", targets)}]: {kicked}人");
            return new RestObject("200") { { "kicked", kicked } };
        }

        private static bool ShouldKick(TSPlayer p, List<string> targets)
        {
            if (targets.Contains(RiskTarget.All)) return true;
            if (targets.Contains(RiskTarget.NotLoggedIn) && !p.IsLoggedIn) return true;
            if (targets.Contains(RiskTarget.RegisterUnder1h)
                && p.IsLoggedIn && p.Account != null && IsRegisterUnder1h(p)) return true;
            if (targets.Contains(RiskTarget.PlaytimeUnder1h)
                && p.IsLoggedIn && GetPlaytimeMinutes(p.Name, p.Index) < 60) return true;
            if (targets.Contains(RiskTarget.Proxy) && Config.Proxy.Enabled)
            {
                var cached = ProxyDetector.LookupCached(p.IP);
                if (cached != null && (cached.Status == ProxyStatus.Proxy || cached.Status == ProxyStatus.Unknown))
                    return true;
            }
            return false;
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

        /// <summary>豁免组判定（不区分大小写）</summary>
        private static bool IsExempt(TSPlayer player)
        {
            var group = player.Group?.Name;
            if (string.IsNullOrEmpty(group)) return false;
            foreach (var g in Config.ExemptGroups)
            {
                if (!string.IsNullOrEmpty(g) && string.Equals(g, group, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>QQ 绑定判定（qq_bind 表；表缺失/查询异常视为未绑定，不豁免）</summary>
        private static bool IsQqBound(TSPlayer player)
        {
            if (player.Account == null) return false;
            try
            {
                using (var reader = TShock.DB.QueryReader(SqlQqBound, player.Account.ID))
                {
                    if (reader.Read())
                        return reader.Get<long>("C") > 0;
                }
            }
            catch { }
            return false;
        }

        /// <summary>注册不足 1 小时判定（UserAccount.Registered 为 UTC ISO8601）</summary>
        private static bool IsRegisterUnder1h(TSPlayer player)
        {
            var reg = player.Account?.Registered;
            if (string.IsNullOrEmpty(reg)) return false;
            if (DateTime.TryParse(reg, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var regTime))
                return DateTime.UtcNow - regTime < TimeSpan.FromHours(1);
            return false;
        }

        /// <summary>注册距今分钟数（-1=无法解析）</summary>
        private static int GetRegisterMinutesAgo(TSPlayer player)
        {
            var reg = player.Account?.Registered;
            if (string.IsNullOrEmpty(reg)) return -1;
            if (DateTime.TryParse(reg, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var regTime))
                return (int)Math.Max(0, (DateTime.UtcNow - regTime).TotalMinutes);
            return -1;
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

        /// <summary>解析整数参数；缺失或非法时保持原值</summary>
        private static int GetInt(RestRequestArgs args, string key, int current)
        {
            var val = args.Parameters[key];
            return val != null && int.TryParse(val, out var result) ? result : current;
        }

        /// <summary>解析逗号分隔数组参数；未传返回 null（保持原值），空串返回空列表</summary>
        private static List<string> GetCsv(RestRequestArgs args, string key)
        {
            var val = args.Parameters[key];
            if (val == null) return null;
            return val.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
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

    /// <summary>风控目标群体常量</summary>
    public static class RiskTarget
    {
        public const string All = "all";
        public const string Proxy = "proxy";
        public const string RegisterUnder1h = "register-under-1h";
        public const string PlaytimeUnder1h = "playtime-under-1h";
        public const string NotLoggedIn = "not-logged-in";
    }

    /// <summary>实时风控配置 v2（JSON 持久化路径: {TShock.SavePath}/TSWeb/risk_control.json）</summary>
    public class RiskConfig
    {
        [JsonProperty("version")]
        public int Version { get; set; } = 2;

        [JsonProperty("blockEnter")]
        public BlockRule BlockEnter { get; set; } = new BlockRule();

        [JsonProperty("blockChat")]
        public BlockRule BlockChat { get; set; } = new BlockRule();

        /// <summary>绑定 QQ 的玩家直接放行（保险项，默认勾选）</summary>
        [JsonProperty("qqBindExempt")]
        public bool QqBindExempt { get; set; } = true;

        [JsonProperty("exemptGroups")]
        public List<string> ExemptGroups { get; set; } = new List<string> { "owner", "superadmin" };

        [JsonProperty("proxy")]
        public ProxyConfig Proxy { get; set; } = new ProxyConfig();
    }

    /// <summary>动作规则：enabled + 目标群体列表（任一命中即拦截，全部通过才放行）</summary>
    public class BlockRule
    {
        [JsonProperty("enabled")]
        public bool Enabled { get; set; }

        [JsonProperty("targets")]
        public List<string> Targets { get; set; } = new List<string>();
    }

    /// <summary>代理检测配置（ip.sb）</summary>
    public class ProxyConfig
    {
        /// <summary>检测能力总开关（关闭后不发起任何 ip.sb 请求）</summary>
        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonProperty("cacheTtlHours")]
        public int CacheTtlHours { get; set; } = 24;

        /// <summary>允许的 ISP/组织关键字（四大运营商，命中即判正常）</summary>
        [JsonProperty("allowIsps")]
        public List<string> AllowIsps { get; set; } = new List<string>
        {
            "中国电信", "中国联通", "中国移动", "中国广电",
            "China Telecom", "China Unicom", "China Mobile",
            "CBN", "Chinanet", "CHINANET", "CMNET"
        };

        /// <summary>明确代理特征关键字（命中直接判恶意；当前紧急语义下未命中 allowIsps 也判恶意，此列表用于增强标签）</summary>
        [JsonProperty("proxyKeywords")]
        public List<string> ProxyKeywords { get; set; } = new List<string>
        {
            "relay", "vpn", "proxy", "hosting", "datacenter", "cloud",
            "akamai", "cloudflare", "ovh", "hetzner"
        };
    }

    /// <summary>代理判定状态</summary>
    public enum ProxyStatus
    {
        /// <summary>四大运营商，正常</summary>
        Normal,
        /// <summary>海外/非四大运营商，直接算恶意</summary>
        Proxy,
        /// <summary>ip.sb 正常响应但信息不足，紧急模式下也拦截</summary>
        Unknown,
        /// <summary>ip.sb 失联/报错，整个 proxy 目标放行</summary>
        Unavailable,
    }

    /// <summary>单 IP 代理判定结果</summary>
    public class ProxyInfo
    {
        public string Ip { get; set; }
        public ProxyStatus Status { get; set; }
        public string Country { get; set; }
        public string Region { get; set; }
        public string City { get; set; }
        public string Isp { get; set; }
        public string Organization { get; set; }
        public long Asn { get; set; }
        public DateTime CheckedAt { get; set; }
    }

    /// <summary>
    /// 代理检测器（ip.sb）：缓存 + 限流(5并发) + 共享任务去重 + fail-open 降级。
    /// 状态机：命中 allowIsps → Normal；信息不足 → Unknown；请求失败/超时/4xx/5xx → Unavailable。
    /// </summary>
    public static class ProxyDetector
    {
        private const string GeoApiBase = "https://api.ip.sb/geoip/";
        private static readonly HttpClient _http = CreateClient();
        private static readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
        private static readonly ConcurrentDictionary<string, Lazy<Task<ProxyInfo>>> _inflight = new();
        private static readonly SemaphoreSlim _gate = new(5, 5);
        private static volatile bool _lastApiOk = true;

        private sealed class CacheEntry
        {
            public ProxyInfo Info;
            public DateTime ExpiresAt;
            public CacheEntry(ProxyInfo info, DateTime expiresAt) { Info = info; ExpiresAt = expiresAt; }
            public bool Expired => DateTime.UtcNow > ExpiresAt;
        }

        /// <summary>ip.sb 最近一次请求是否成功（前端 apiHealth 提示）</summary>
        public static bool ApiHealth => _lastApiOk;

        /// <summary>仅查缓存；未命中或过期返回 null</summary>
        public static ProxyInfo LookupCached(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return null;
            if (_cache.TryGetValue(ip, out var entry) && !entry.Expired)
                return entry.Info;
            return null;
        }

        /// <summary>取检测任务：缓存命中立即返回；未命中共享同一任务（Lazy 保证同 IP 只请求一次）</summary>
        public static Task<ProxyInfo> GetOrStartLookupAsync(string ip)
        {
            if (string.IsNullOrEmpty(ip))
                return Task.FromResult(new ProxyInfo { Ip = ip, Status = ProxyStatus.Normal });

            var cached = LookupCached(ip);
            if (cached != null)
                return Task.FromResult(cached);

            return _inflight.GetOrAdd(ip, k => new Lazy<Task<ProxyInfo>>(
                () => DoLookupAsync(k), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        }

        /// <summary>同步阻塞等待检测结果（REST 线程用，不阻塞游戏主线程）</summary>
        public static ProxyInfo LookupBlocking(string ip, TimeSpan timeout)
        {
            try
            {
                return GetOrStartLookupAsync(ip).Wait(timeout) ? GetOrStartLookupAsync(ip).Result : null;
            }
            catch
            {
                return null;
            }
        }

        public static void RefreshAll()
        {
            _cache.Clear();
            _inflight.Clear();
        }

        public static void Refresh(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return;
            _cache.TryRemove(ip, out _);
            _inflight.TryRemove(ip, out _);
        }

        public static void Shutdown()
        {
            _cache.Clear();
            _inflight.Clear();
        }

        private static async Task<ProxyInfo> DoLookupAsync(string ip)
        {
            try
            {
                // 本地回环/保留段直接判正常，不发请求
                if (IsLocalIp(ip))
                {
                    var local = new ProxyInfo { Ip = ip, Status = ProxyStatus.Normal, CheckedAt = DateTime.UtcNow };
                    CachePut(ip, local);
                    return local;
                }

                await _gate.WaitAsync();
                try
                {
                    // 双重检查：等待限流期间可能已被其他任务填充
                    var recheck = LookupCached(ip);
                    if (recheck != null) return recheck;

                    string json;
                    try
                    {
                        json = await _http.GetStringAsync(GeoApiBase + Uri.EscapeDataString(ip));
                        _lastApiOk = true;
                    }
                    catch (Exception)
                    {
                        _lastApiOk = false;
                        var fail = new ProxyInfo { Ip = ip, Status = ProxyStatus.Unavailable, CheckedAt = DateTime.UtcNow };
                        _cache[ip] = new CacheEntry(fail, DateTime.UtcNow.AddMinutes(1)); // 失败短缓存，防风暴
                        return fail;
                    }

                    var info = ParseGeo(json, ip);
                    CachePut(ip, info);
                    return info;
                }
                finally
                {
                    _gate.Release();
                }
            }
            finally
            {
                _inflight.TryRemove(ip, out _);
            }
        }

        private static void CachePut(string ip, ProxyInfo info)
        {
            var ttl = RiskControl.Config?.Proxy?.CacheTtlHours > 0
                ? RiskControl.Config.Proxy.CacheTtlHours
                : 24;
            _cache[ip] = new CacheEntry(info, DateTime.UtcNow.AddHours(ttl));
        }

        private static ProxyInfo ParseGeo(string json, string ip)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string GetStr(string name) =>
                root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

            var isp = GetStr("isp") ?? "";
            var org = GetStr("organization") ?? "";
            var asnOrg = GetStr("asn_organization") ?? "";
            long asn = root.TryGetProperty("asn", out var a) && a.ValueKind == JsonValueKind.Number
                ? a.GetInt64() : 0;

            var info = new ProxyInfo
            {
                Ip = ip,
                Country = GetStr("country") ?? "",
                Region = GetStr("region") ?? "",
                City = GetStr("city") ?? "",
                Isp = isp,
                Organization = org,
                Asn = asn,
                CheckedAt = DateTime.UtcNow,
            };

            // ip.sb 正常响应但无任何归属信息 → 无法判定 → 紧急模式下拦截
            if (string.IsNullOrWhiteSpace(isp) && string.IsNullOrWhiteSpace(org) && string.IsNullOrWhiteSpace(asnOrg))
            {
                info.Status = ProxyStatus.Unknown;
                return info;
            }

            var text = (isp + " " + org + " " + asnOrg).ToLowerInvariant();

            // 四大运营商（允许列表）→ 正常
            foreach (var kw in RiskControl.Config.Proxy.AllowIsps)
            {
                if (!string.IsNullOrEmpty(kw) && text.Contains(kw.ToLowerInvariant(), StringComparison.Ordinal))
                {
                    info.Status = ProxyStatus.Normal;
                    return info;
                }
            }

            // 明确代理特征 → 恶意
            foreach (var kw in RiskControl.Config.Proxy.ProxyKeywords)
            {
                if (!string.IsNullOrEmpty(kw) && text.Contains(kw.ToLowerInvariant(), StringComparison.Ordinal))
                {
                    info.Status = ProxyStatus.Proxy;
                    return info;
                }
            }

            // 非四大运营商 → 海外/恶意（紧急模式：直接算恶意）
            info.Status = ProxyStatus.Proxy;
            return info;
        }

        private static bool IsLocalIp(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return true;
            ip = ip.Trim();
            if (ip == "127.0.0.1" || ip == "::1" || ip == "localhost") return true;
            if (ip.StartsWith("10.", StringComparison.Ordinal)
                || ip.StartsWith("192.168.", StringComparison.Ordinal)) return true;
            if (ip.StartsWith("172.", StringComparison.Ordinal))
            {
                var parts = ip.Split('.');
                if (parts.Length >= 2 && int.TryParse(parts[1], out var b))
                    return b >= 16 && b <= 31;
            }
            return false;
        }

        private static HttpClient CreateClient()
        {
            var h = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("TSWeb/1.0");
            h.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            return h;
        }
    }
}
