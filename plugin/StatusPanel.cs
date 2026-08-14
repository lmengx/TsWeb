using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rests;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using TerrariaApi.Server;
using TShockAPI;

namespace TShockData
{
    /// <summary>
    /// 服务器信息面板（整合自 plugin-son/statuspanel + 参考插件 StatusTextManager）：
    ///   - 配置模型兼容参考插件 StatusTextManager.json（LogLevel + StatusTextSettings 三类设置）
    ///   - 保留当前 statuspanel 的排版技巧：每行行尾补 SpacerWidth 空格撑宽 → 客户端固定锚点居中
    ///   - 渲染循环按 UpdateInterval 节流（(TickCount + 玩家Index) % 间隔），DynamicText 行独立节流
    ///   - 全服在线：后端启动 / 服务器配置变更时拉取下发（POST /tsweb/statuspanel，HMAC 签名）；
    ///     玩家上下线经 SSE 事件 "online" 上报本服在线数，后端聚合后实时下发
    ///   - 系统时间：{SystemTime} 由服务器自身获取（DateTime.Now，精确到分钟）
    ///   - /st /statustext 命令：玩家自行开关面板（默认开启）
    /// </summary>
    public class StatusPanelConfig
    {
        /// <summary>总开关（默认开启）</summary>
        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>行尾补空格数：文本块撑宽 → 客户端固定锚点居中（沿用 statuspanel 抓包实证）</summary>
        [JsonProperty("spacerWidth")]
        public int SpacerWidth { get; set; } = 60;

        /// <summary>日志等级（兼容参考插件 LogLevel 枚举，仅用于过滤日志输出）</summary>
        [JsonProperty("logLevel")]
        public string LogLevel { get; set; } = "INFO";

        /// <summary>旧版单面板字段（v1：statusTextSettings），仅用于迁移到 panels.default，迁移后置 null</summary>
        [JsonProperty("statusTextSettings")]
        public List<JObject>? StatusTextSettings { get; set; }

        /// <summary>
        /// 多面板：面板名 → 行列表。default 面板强制存在。
        /// 行元素（兼容参考插件 StatusTextSettings 三类）：
        ///   { typeName: "StaticText", text }                                     —— 静态文本
        ///   { typeName: "DynamicText", text, updateInterval }                     —— 动态文本（插值，帧数节流）
        ///   { typeName: "HandlerInfoOverride", pluginName, enabled, ... }         —— 外部插件 handler 覆盖（主插件无外部 handler，解析兼容）
        /// </summary>
        [JsonProperty("panels")]
        public Dictionary<string, List<JObject>> Panels { get; set; } = new();

        /// <summary>面板级行尾空格覆盖（面板名 → 空格数）；未配置的面板回退用全局 SpacerWidth</summary>
        [JsonProperty("panelSpacers")]
        public Dictionary<string, int> PanelSpacers { get; set; } = new();
    }

    /// <summary>默认面板模板（保留当前 statuspanel 观感 + 新增全服在线/系统时间）</summary>
    public static class StatusPanelDefaults
    {
        public static List<JObject> Template()
        {
            return new List<JObject>
            {
                JObject.Parse("{\"typeName\":\"DynamicText\",\"text\":\"[i:757][c/f15642:开荒服]\",\"updateInterval\":600}"),
                JObject.Parse("{\"typeName\":\"DynamicText\",\"text\":\"在线人数：{OnlinePlayersCount}人\",\"updateInterval\":60}"),
                JObject.Parse("{\"typeName\":\"DynamicText\",\"text\":\"全服在线：{AllOnlineCount}人\",\"updateInterval\":60}"),
                JObject.Parse("{\"typeName\":\"DynamicText\",\"text\":\"系统时间：{SystemTime}\",\"updateInterval\":60}")
            };
        }
    }

    public static class StatusPanel
    {
        // ═══════════════ 配置 ═══════════════
        private static readonly string ConfigPath = Path.Combine(TShock.SavePath, "TSWeb", "statuspanel.json");
        private static StatusPanelConfig _config = new();
        private static bool _loaded;

        // ═══════════════ 状态 ═══════════════
        private static bool _initialized;
        private static TerrariaPlugin? _plugin;

        // 玩家面板开关（/st 命令，默认开启）
        private static bool[] _isVisible = new bool[Main.maxPlayers];
        private static bool[] _needInit = new bool[Main.maxPlayers];

        // 玩家当前选中的面板名（仅内存态，不持久化；默认 default，/st 面板名 切换）
        private static string[] _playerPanel = new string[Main.maxPlayers];

        // 渲染节流与缓存
        private static ulong _tickCount;
        private static string?[][] _lineCache = Array.Empty<string?[]>(); // [playerIndex][settingIdx]
        private static Dictionary<string, string> _panelSpacerCache = new(); // 面板名 → 行尾空格串（含面板级覆盖）

        // 全服在线缓存（后端推送）
        private static int _allOnlineTotal = -1; // -1 = 未知（后端未下发）
        private static readonly object _onlineLock = new();

        // 插值正则（照搬参考插件：匹配 {abc} 与 {{abc}}）
        private static readonly Regex _interpolationRegex = new(@"(?:{{[^{\s]*?}}|{[^{\s]*?})");

        public static StatusPanelConfig Config => _config;

        // ═══════════════ 初始化 / 释放 ═══════════════

        public static void Initialize(TerrariaPlugin plugin)
        {
            if (_initialized) return;
            _initialized = true;
            _plugin = plugin;

            LoadConfig();

            ServerApi.Hooks.GamePostUpdate.Register(plugin, OnGamePostUpdate);
            ServerApi.Hooks.ServerJoin.Register(plugin, OnServerJoin);
            ServerApi.Hooks.ServerLeave.Register(plugin, OnServerLeave);

            TShock.Log.ConsoleInfo($"[TSWeb] 状态面板已初始化 (启用:{(Config.Enabled ? "是" : "否")}, 面板数:{Config.Panels.Count})");
        }

        public static void Dispose()
        {
            if (!_initialized) return;
            _initialized = false;

            if (_plugin != null)
            {
                ServerApi.Hooks.GamePostUpdate.Deregister(_plugin, OnGamePostUpdate);
                ServerApi.Hooks.ServerJoin.Deregister(_plugin, OnServerJoin);
                ServerApi.Hooks.ServerLeave.Deregister(_plugin, OnServerLeave);
            }

            // 卸载时清空所有玩家屏幕文本
            foreach (var p in TShock.Players)
            {
                if (p != null && p.Active)
                    p.SendData(PacketTypes.Status, "", 0, 0x1f);
            }

            _config = new StatusPanelConfig();
            _allOnlineTotal = -1;
            _isVisible = new bool[Main.maxPlayers];
            _needInit = new bool[Main.maxPlayers];
            _playerPanel = new string[Main.maxPlayers];

            TShock.Log.ConsoleInfo("[TSWeb] 状态面板已停止");
        }

        // ═══════════════ 配置读写 ═══════════════

        public static void LoadConfig()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir!);

                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    _config = JsonConvert.DeserializeObject<StatusPanelConfig>(json) ?? new StatusPanelConfig();
                    _config.Panels ??= new Dictionary<string, List<JObject>>();

                    // 旧版单面板迁移：statusTextSettings → panels.default
                    if (_config.Panels.Count == 0 && _config.StatusTextSettings != null && _config.StatusTextSettings.Count > 0)
                    {
                        _config.Panels["default"] = _config.StatusTextSettings;
                        _config.StatusTextSettings = null;
                    }

                    // 强制 default 面板存在
                    if (!_config.Panels.ContainsKey("default"))
                        _config.Panels["default"] = StatusPanelDefaults.Template();
                }
                else
                {
                    _config = new StatusPanelConfig();
                    _config.Panels["default"] = StatusPanelDefaults.Template();
                    SaveConfig();
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 加载状态面板配置失败: {ex.Message}");
                _config = new StatusPanelConfig();
                _config.Panels["default"] = StatusPanelDefaults.Template();
            }

            RebuildCaches();
            _loaded = true;
        }

        public static void SaveConfig()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir!);

                var json = JsonConvert.SerializeObject(_config, Formatting.Indented);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 保存状态面板配置失败: {ex.Message}");
            }
        }

        private static void EnsureLoaded()
        {
            if (!_loaded)
                LoadConfig();
        }

        /// <summary>配置变更后重建渲染缓存（按各玩家当前面板行数）并强制刷新</summary>
        private static void RebuildCaches()
        {
            _lineCache = new string?[Main.maxPlayers][];
            for (var i = 0; i < Main.maxPlayers; i++)
            {
                var panelName = GetPlayerPanel(i);
                _lineCache[i] = new string?[_config.Panels[panelName].Count];
            }

            // 重建面板级行尾空格缓存（面板覆盖 ?? 全局）
            _panelSpacerCache = new Dictionary<string, string>();
            foreach (var name in _config.Panels.Keys)
                _panelSpacerCache[name] = new string(' ', GetPanelSpacerValue(name));

            ForceRefreshAll();
        }

        /// <summary>面板行尾空格数：面板级 PanelSpacers 覆盖 ?? 全局 SpacerWidth</summary>
        private static int GetPanelSpacerValue(string panelName)
        {
            if (_config.PanelSpacers != null && _config.PanelSpacers.TryGetValue(panelName, out var s) && s >= 0)
                return s;
            return Math.Max(0, _config.SpacerWidth);
        }

        /// <summary>面板行尾空格串（缓存，配置变更时由 RebuildCaches 重建）</summary>
        private static string GetPanelSpacer(string panelName)
        {
            if (_panelSpacerCache.TryGetValue(panelName, out var s))
                return s;
            s = new string(' ', GetPanelSpacerValue(panelName));
            _panelSpacerCache[panelName] = s;
            return s;
        }

        /// <summary>玩家当前面板名（未设置/失效回退 default，并同步内存态）</summary>
        private static string GetPlayerPanel(int playerIdx)
        {
            var name = _playerPanel[playerIdx];
            if (string.IsNullOrEmpty(name) || !_config.Panels.ContainsKey(name))
            {
                name = "default";
                _playerPanel[playerIdx] = name;
            }
            return name;
        }

        /// <summary>切换玩家面板（忽略大小写匹配），成功返回 true</summary>
        private static bool TrySwitchPanel(int playerIdx, string panelName)
        {
            var actual = _config.Panels.Keys.FirstOrDefault(k => k.Equals(panelName, StringComparison.OrdinalIgnoreCase));
            if (actual == null)
                return false;

            _playerPanel[playerIdx] = actual;
            _lineCache[playerIdx] = new string?[_config.Panels[actual].Count];
            _needInit[playerIdx] = true;
            return true;
        }

        /// <summary>强制所有在线玩家下次渲染立即刷新（配置变更后调用）</summary>
        private static void ForceRefreshAll()
        {
            for (var i = 0; i < _needInit.Length; i++)
                _needInit[i] = true;
        }

        // ═══════════════ REST API（前端配置） ═══════════════

        public static object GetConfigJson(RestRequestArgs args)
        {
            EnsureLoaded();

            // 手工构建小写字段（JavaScriptSerializer 不认 [JsonProperty]）
            // 注意：RestObject 无参构造已内置 status=200，不可再手动添加 "status" 键（字典 Add 不查重会抛重复键异常）
            var panels = new Dictionary<string, List<object>>();
            foreach (var kv in _config.Panels)
            {
                var settings = new List<object>();
                foreach (var s in kv.Value)
                {
                    var d = new Dictionary<string, object>();
                    if (s["typeName"] != null) d["typeName"] = s["typeName"]!.ToString()!;
                    if (s["text"] != null) d["text"] = s["text"]!.ToString()!;
                    if (s["updateInterval"] != null) d["updateInterval"] = s["updateInterval"]!.ToObject<int>();
                    if (s["pluginName"] != null) d["pluginName"] = s["pluginName"]!.ToString()!;
                    if (s["enabled"] != null) d["enabled"] = s["enabled"]!.ToObject<bool>();
                    if (s["overrideInterval"] != null) d["overrideInterval"] = s["overrideInterval"]!.ToObject<bool>();
                    settings.Add(d);
                }
                panels[kv.Key] = settings;
            }

            return new RestObject
            {
                { "enabled", _config.Enabled },
                { "spacerWidth", _config.SpacerWidth },
                { "logLevel", _config.LogLevel },
                { "panels", panels },
                { "panelSpacers", new Dictionary<string, int>(_config.PanelSpacers) }
            };
        }

        public static object SetConfigJson(RestRequestArgs args)
        {
            try
            {
                string enabled = null, spacer = null, logLevel = null, panelsJson = null, panelSpacersJson = null;
                try { enabled = args.Parameters["enabled"]; } catch { }
                try { spacer = args.Parameters["spacerWidth"]; } catch { }
                try { logLevel = args.Parameters["logLevel"]; } catch { }
                try { panelsJson = args.Parameters["panels"]; } catch { }
                try { panelSpacersJson = args.Parameters["panelSpacers"]; } catch { }

                if (!string.IsNullOrEmpty(enabled))
                    _config.Enabled = enabled.Equals("true", StringComparison.OrdinalIgnoreCase);
                if (!string.IsNullOrEmpty(spacer) && int.TryParse(spacer, out var sw))
                    _config.SpacerWidth = Math.Max(0, Math.Min(500, sw));
                if (!string.IsNullOrEmpty(logLevel))
                    _config.LogLevel = logLevel;
                if (!string.IsNullOrEmpty(panelsJson))
                {
                    var parsed = JsonConvert.DeserializeObject<Dictionary<string, List<JObject>>>(panelsJson);
                    if (parsed != null)
                    {
                        foreach (var kv in parsed)
                            kv.Value?.RemoveAll(s => s == null);
                        _config.Panels = parsed;
                    }
                }
                if (!string.IsNullOrEmpty(panelSpacersJson))
                {
                    var parsedSpacers = JsonConvert.DeserializeObject<Dictionary<string, int>>(panelSpacersJson);
                    if (parsedSpacers != null)
                    {
                        // 清理无效值（<0 视为未配置，渲染回退全局）
                        foreach (var k in parsedSpacers.Where(kv => kv.Value < 0).Select(kv => kv.Key).ToList())
                            parsedSpacers.Remove(k);
                        _config.PanelSpacers = parsedSpacers;
                    }
                }

                // 强制 default 面板存在
                _config.Panels ??= new Dictionary<string, List<JObject>>();
                if (!_config.Panels.ContainsKey("default"))
                    _config.Panels["default"] = StatusPanelDefaults.Template();

                SaveConfig();
                RebuildCaches();

                TShock.Log.ConsoleInfo($"[TSWeb] 状态面板配置已通过 REST API 更新 (面板数:{_config.Panels.Count})");
                return new RestObject { { "response", "配置已保存" } };
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 更新状态面板配置失败: {ex.Message}");
                return new RestObject("500") { { "error", ex.Message } };
            }
        }

        // ═══════════════ /st /statustext 命令 ═══════════════

        public static void ToggleCommand(CommandArgs args)
        {
            var idx = args.Player.Index;

            // /st：查看所有面板 + 当前选中
            if (args.Parameters.Count == 0)
            {
                var current = GetPlayerPanel(idx);
                var names = string.Join("、", _config.Panels.Keys);
                args.Player.SendInfoMessage($"可用面板：{names}。当前：{current}。用法：/st on|off|<面板名>");
                return;
            }

            var arg = args.Parameters[0];

            // /st on|show：开启面板
            if (arg.Equals("on", StringComparison.OrdinalIgnoreCase) || arg.Equals("show", StringComparison.OrdinalIgnoreCase))
            {
                if (!_isVisible[idx])
                {
                    _isVisible[idx] = true;
                    _needInit[idx] = true;
                }
                args.Player.SendSuccessMessage("已开启状态面板显示");
                return;
            }

            // /st off|hide：关闭面板
            if (arg.Equals("off", StringComparison.OrdinalIgnoreCase) || arg.Equals("hide", StringComparison.OrdinalIgnoreCase))
            {
                if (_isVisible[idx])
                {
                    _isVisible[idx] = false;
                    _needInit[idx] = false;
                    args.Player.SendData(PacketTypes.Status, "", 0, 0x1f);
                }
                args.Player.SendSuccessMessage("已关闭状态面板显示");
                return;
            }

            // /st <面板名>：切换面板（忽略大小写匹配，仅内存态）
            if (TrySwitchPanel(idx, arg))
            {
                args.Player.SendSuccessMessage($"已切换到面板：{_playerPanel[idx]}");
            }
            else
            {
                args.Player.SendInfoMessage($"面板「{arg}」不存在。可用面板：{string.Join("、", _config.Panels.Keys)}。用法：/st on|off|<面板名>");
            }
        }

        // ═══════════════ 渲染循环 ═══════════════

        private static void OnGamePostUpdate(EventArgs args)
        {
            _tickCount++;
            if (!Config.Enabled) return;

            foreach (var p in TShock.Players)
            {
                if (p == null || !p.Active) continue;
                if (!_isVisible[p.Index]) continue;

                try
                {
                    if (RenderPlayer(p, _needInit[p.Index], out var text))
                        p.SendData(PacketTypes.Status, text, 0, 0x1f);
                    // 0x1f -> HideStatusTextPercent + 阴影
                }
                catch { }

                _needInit[p.Index] = false;
            }
        }

        /// <summary>
        /// 拼接某玩家**当前选中面板**的文本（每行尾部自动补 spacer 空格）。
        /// 返回是否需要发送（任一 DynamicText 行到达节流周期或 force）。
        /// </summary>
        private static bool RenderPlayer(TSPlayer p, bool force, out string text)
        {
            var panelName = GetPlayerPanel(p.Index);
            var lines = _config.Panels[panelName];
            var cache = _lineCache[p.Index];
            if (cache.Length != lines.Count) // 防御：面板行数变化时重建
                cache = _lineCache[p.Index] = new string?[lines.Count];
            var spacer = GetPanelSpacer(panelName);
            var needUpdate = false;
            var sb = new StringBuilder();

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var typeName = line["typeName"]?.ToString();

                switch (typeName)
                {
                    case "StaticText":
                        AppendLine(sb, line["text"]?.ToString() ?? "", spacer);
                        break;

                    case "DynamicText":
                        var interval = line["updateInterval"]?.ToObject<ulong>() ?? 60;
                        if (interval < 1) interval = 1;
                        if (force || (_tickCount + (ulong)p.Index) % interval == 0)
                        {
                            cache[i] = Interpolate(line["text"]?.ToString() ?? "", p);
                            needUpdate = true;
                        }
                        AppendLine(sb, cache[i] ?? "", spacer);
                        break;

                    default:
                        // HandlerInfoOverride / 未知类型：主插件无外部 handler，忽略
                        break;
                }
            }

            text = sb.ToString();
            return needUpdate;
        }

        /// <summary>
        /// 追加一行：内容 + 行尾空格（撑宽）+ 换行符。
        /// 若内容已以 \n 结尾（参考插件配置自带换行），原样追加，不重复加换行。
        /// </summary>
        private static void AppendLine(StringBuilder sb, string content, string spacer)
        {
            if (content.EndsWith("\n", StringComparison.Ordinal))
            {
                sb.Append(content);
            }
            else
            {
                sb.Append(content);
                sb.Append(spacer);
                sb.Append('\n');
            }
        }

        // ═══════════════ 在线上报（SSE 上行 → 后端聚合全服在线） ═══════════════

        private static void OnServerJoin(JoinEventArgs args)
        {
            _isVisible[args.Who] = true;         // 默认开
            _playerPanel[args.Who] = "default"; // 进服用默认面板（内存态，不持久化）
            _needInit[args.Who] = true;
            ScheduleReport();
        }

        private static void OnServerLeave(LeaveEventArgs args)
        {
            ScheduleReport();
        }

        /// <summary>延迟 200ms（等 Active 状态落定）后统计并上报本服在线数</summary>
        private static void ScheduleReport()
        {
            Task.Delay(200).ContinueWith(_ => ReportOnline());
        }

        /// <summary>
        /// 统计本服在线数并经 SSE 广播事件 "online" 上报后端。
        /// SSE 握手成功后由 WebRestServer 调用一次（覆盖插件重启/后端重连场景）。
        /// </summary>
        public static void ReportOnline()
        {
            if (!_initialized) return;

            var online = 0;
            foreach (var p in TShock.Players)
                if (p != null && p.Active) online++;

            var json = JsonConvert.SerializeObject(new { online });
            WebRestServer.Broadcast("online", json);
        }

        // ═══════════════ 全服在线接收（后端 POST /tsweb/statuspanel） ═══════════════

        /// <summary>处理后端全服在线推送，返回 JSON 响应体。headers 为大小写不敏感的请求头字典。</summary>
        public static string HandleOnlinePush(string body, Dictionary<string, string> headers)
        {
            if (!WebhookAuth.VerifySignature(headers, body))
                return "{\"status\":\"401\",\"error\":\"Invalid signature\"}";

            try
            {
                var payload = JsonConvert.DeserializeObject<JObject>(body);
                var type = payload?["type"]?.ToString();
                if (type != "online")
                    return "{\"status\":\"400\",\"error\":\"Unknown type\"}";

                var total = payload["total"]?.ToObject<int>() ?? -1;
                lock (_onlineLock)
                {
                    _allOnlineTotal = total;
                }

                return "{\"status\":\"200\",\"ok\":true}";
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 状态面板全服在线推送处理失败: {ex.Message}");
                return "{\"status\":\"500\",\"error\":\"" + JsonConvert.ToString(ex.Message) + "\"}";
            }
        }

        private static string AllOnlineDisplay()
        {
            lock (_onlineLock)
                return _allOnlineTotal >= 0 ? _allOnlineTotal.ToString() : "—";
        }

        // ═══════════════ 插值 ═══════════════

        /// <summary>对模板做插值替换（照搬参考插件 DynamicText 逻辑，含 {{xxx}} 转义）</summary>
        private static string Interpolate(string template, TSPlayer player)
        {
            string MatchEvaluator(Match m)
            {
                if (m.Value[1] == '{') // {{abc}} → {abc}
                    return m.Value.Substring(1, m.Value.Length - 2);

                return m.Value switch
                {
                    "{PlayerName}" => player.Name,
                    "{PlayerGroupName}" => player.Group.Name,
                    "{PlayerLife}" => player.TPlayer.statLife.ToString(),
                    "{PlayerMana}" => player.TPlayer.statMana.ToString(),
                    "{PlayerLifeMax}" => player.TPlayer.statLifeMax2.ToString(),
                    "{PlayerManaMax}" => player.TPlayer.statManaMax2.ToString(),
                    "{PlayerLuck}" => player.TPlayer.luck.ToString(),
                    "{PlayerCoordinateX}" => player.TileX.ToString(),
                    "{PlayerCoordinateY}" => player.TileY.ToString(),
                    "{PlayerCurrentRegion}" => player.CurrentRegion == null ? "空区域" : player.CurrentRegion.Name,
                    "{IsPlayerAlive}" => player.Dead ? "已死亡" : "存活",
                    "{RespawnTimer}" => player.RespawnTimer == 0 ? "未死亡" : player.RespawnTimer.ToString(),
                    "{OnlinePlayersCount}" => TShock.Utils.GetActivePlayerCount().ToString(),
                    "{OnlinePlayersList}" => string.Join(',', TShock.Players.Where(x => x is { Active: true }).Select(x => x.Name)),
                    "{AnglerQuestFishName}" => GetAnglerQuestFishName(),
                    "{AnglerQuestFishID}" => GetAnglerQuestFishId().ToString(),
                    "{AnglerQuestFishingBiome}" => GetAnglerQuestFishingBiome(),
                    "{AnglerQuestCompleted}" => Main.anglerWhoFinishedToday.Exists(x => x == player.Name) ? "已完成" : "未完成",
                    "{CurrentTime}" => GetCurrentTime(),
                    "{RealWorldTime}" => DateTime.Now.ToString("HH:mm"),
                    "{WorldName}" => Main.worldName,
                    "{CurrentBiomes}" => GetFormattedBiomesList(player),
                    // ── 主插件新增 ──
                    "{AllOnlineCount}" => AllOnlineDisplay(),
                    "{SystemTime}" => DateTime.Now.ToString("HH:mm"), // 服务器自身时间，精确到分钟
                    _ => m.Value,
                };
            }

            return _interpolationRegex.Replace(template, MatchEvaluator);
        }

        // ═══════════════ 工具函数（照搬参考插件 Utils/Common.cs） ═══════════════

        /// <summary>游戏内时间（HH:mm）</summary>
        private static string GetCurrentTime()
        {
            var num = Main.time / 3600.0;
            num += 4.5;
            if (!Main.dayTime)
                num += 15.0;
            num %= 24.0;
            return string.Format("{0}:{1:D2}", (int)Math.Floor(num), (int)Math.Floor(num % 1.0 * 60.0));
        }

        /// <summary>群系列表（按地形色着色）</summary>
        private static string GetFormattedBiomesList(TSPlayer plr)
        {
            var sb = new StringBuilder();
            var envInfo = GetBiomesInfo(plr);
            var colorHexCode = envInfo.Contains("空岛") ? "00BFFF"
                : envInfo.Contains("地下") ? "FF8C00"
                : envInfo.Contains("洞穴") ? "A0522D"
                : envInfo.Contains("地狱") ? "FF0000"
                : "008000";
            sb.Append($"[c/{colorHexCode}:{string.Join(',', envInfo)}]");
            return sb.ToString();
        }

        private static List<string> GetBiomesInfo(TSPlayer plr)
        {
            var index = plr.Index;
            var list = new List<string>();
            if (Main.player[index].ZoneDungeon) list.Add("地牢");
            if (Main.player[index].ZoneCorrupt) list.Add("腐化");
            if (Main.player[index].ZoneHallow) list.Add("神圣");
            if (Main.player[index].ZoneMeteor) list.Add("陨石");
            if (Main.player[index].ZoneJungle) list.Add("丛林");
            if (Main.player[index].ZoneSnow) list.Add("雪原");
            if (Main.player[index].ZoneCrimson) list.Add("猩红");
            if (Main.player[index].ZoneWaterCandle) list.Add("水蜡烛");
            if (Main.player[index].ZonePeaceCandle) list.Add("和平蜡烛");
            if (Main.player[index].ZoneDesert) list.Add("沙漠");
            if (Main.player[index].ZoneGlowshroom) list.Add("发光蘑菇");
            if (Main.player[index].ZoneUndergroundDesert) list.Add("地下沙漠");
            if (Main.player[index].ZoneSkyHeight) list.Add("空岛");
            if (Main.player[index].ZoneDirtLayerHeight) list.Add("地下");
            if (Main.player[index].ZoneRockLayerHeight) list.Add("洞穴");
            if (Main.player[index].ZoneUnderworldHeight) list.Add("地狱");
            if (Main.player[index].ZoneBeach) list.Add("海滩");
            if (Main.player[index].ZoneRain) list.Add("雨天");
            if (Main.player[index].ZoneSandstorm) list.Add("沙尘暴");
            if (Main.player[index].ZoneGranite) list.Add("花岗岩");
            if (Main.player[index].ZoneMarble) list.Add("大理石");
            if (Main.player[index].ZoneHive) list.Add("蜂巢");
            if (Main.player[index].ZoneGemCave) list.Add("宝石洞窟");
            if (Main.player[index].ZoneLihzhardTemple) list.Add("神庙");
            if (Main.player[index].ZoneGraveyard) list.Add("墓地");
            if (Main.player[index].ZoneShadowCandle) list.Add("阴影蜡烛");
            if (Main.player[index].ZoneShimmer) list.Add("微光");
            if (Main.player[index].ShoppingZone_Forest) list.Add("森林");
            return list;
        }

        private static string GetAnglerQuestFishName()
        {
            var itemID = Main.anglerQuestItemNetIDs[Main.anglerQuest];
            return (string)Lang.GetItemName(itemID);
        }

        private static int GetAnglerQuestFishId()
        {
            return Main.anglerQuestItemNetIDs[Main.anglerQuest];
        }

        private static readonly Regex FishMissionPlaceRegex = new(@"(?<=（抓捕位置：|\(Capturado no |\(Поймано в |\(można złapać w |\(Se trouve |\(Se encuentra en |\(Caught ).*?(?=）|\))");
        private static readonly Regex FishMissionPlaceExceptionalCasesRegex = new(@"(?<=（|\().*?(?=）|\))");

        private static string GetAnglerQuestFishingBiome()
        {
            var itemId = Main.anglerQuestItemNetIDs[Main.anglerQuest];
            var questText = Language.GetTextValue($"AnglerQuestText.Quest_{ItemID.Search.GetName(itemId)}");
            return Language.ActiveCulture.Name switch
            {
                "en-US" or "fr-FR" or "es-ES" or "ru-RU" or "zh-Hans" or "pt-BR" or "pl-PL" =>
                    FishMissionPlaceRegex.Match(questText).ToString(),
                _ =>
                    FishMissionPlaceExceptionalCasesRegex.Match(questText).ToString()
            };
        }
    }
}
