using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MonoMod.RuntimeDetour;
using Newtonsoft.Json;
using Terraria;
using Terraria.Net;
using Terraria.Net.Sockets;
using TShockAPI;

namespace ConnectionGuard
{
    /// <summary>
    /// GuardCore —— 连接看门狗（修复 tcping 高并发假死）。
    ///
    /// ⚠ 重要架构说明（v5，2026-08）：此前 v4 用 MonoMod 改写 TShock LinuxTcpSocket 的
    /// StartListening / StopListening / ListenLoop 三个方法做"源头修复"，实测【服务器内核在
    /// 玩家连接时直接崩溃】。根因：
    ///   - OnStartListening 每次重建 self._listener，但旧 ListenLoop 线程仍阻塞在旧监听器的
    ///     AcceptTcpClient() 上且永不退出（旧监听器从未被真停）→ 每次 StartListening 泄漏一个
    ///     accept 线程；
    ///   - 配合 ListenLoop 改写 → 多个 accept 线程并发调用 Netplay.OnConnectionAccepted →
    ///     对 Netplay.Clients 槽位分配产生数据竞争 → 内存破坏 → 内核崩溃（"一有人进来就崩"）。
    /// 结论：不再对连接生命周期方法做 detour。本版改为【纯看门狗（托管代码，零 detour，不可能
    /// 崩内核）】+ 可选 OnConnectionAccepted 限流钩子（默认关，见 rateLimitEnabled 说明）。
    ///
    /// 根因回顾（四份反编译实证，详见 README）：
    ///   1. RemoteClient 对「已连接但永不发数据」的连接无超时（TimeOutTimer 从未被检查）
    ///      → 挂起连接永久占槽 → 新玩家永久被拒 → 假死。
    ///   2. 1.4.5.6 槽位满 → StopListening + ServerLoop 的 StartListeningIfNeeded 自动恢复（自愈）；
    ///      1.4.5.7 删掉了 StartListeningIfNeeded 与挂起超时 → 无自愈 → 永久假死。
    ///      （TShock 1456/1457 的 LinuxTcpSocket 反编译完全一致，差异全在服务器原版 Netplay）
    ///
    /// 本版防御（全部为托管代码，逐层 try/catch，异常最多被记录、绝不崩进程）：
    ///   【1 · 挂起清理（核心，回归 1.4.5.6 自愈能力）】定时扫描 Netplay.Clients[]，对
    ///     State==0（已连接未握手）且超时 / 同 IP 超限 / 全局超限的连接调用 Socket.Close()
    ///     （与成熟开源插件 yaaiomni/Chireiden 同款方式）→ 底层 socket 关闭 → 原版 ServerLoop
    ///     下一轮检测到 IsConnected()==false → Reset 释放槽位。
    ///   【2 · 恢复监听（回归 1.4.5.6 StartListeningIfNeeded）】若 Netplay.IsListening==false
    ///     且存在空槽 → 反射取 Netplay.OnConnectionAccepted 委托 → 调用 TcpListener.StartListening()
    ///     → 重启监听。全部托管 + try/catch，失败仅告警下轮重试。
    ///   【3 · 可选限流钩子（rateLimitEnabled，默认关）】MonoMod detour Netplay.OnConnectionAccepted
    ///     在分配槽位前按每 IP 速率限流。此方法与开源插件 yaaiomni 一致、在标准 TShock/OTAPI 上
    ///     验证过；但本服运行的是定制 1.4.5.7（Netplay 缺少 Disconnect 字段），detour 行为无法
    ///     100% 预知 → 默认关闭。开启后若仍异常，请立即在配置里置回 false。
    ///
    /// 安全边界：跳过 127.0.0.1；服务器未就绪（Main.netMode!=2）时跳过全部操作。
    /// </summary>
    public class GuardConfig
    {
        /// <summary>看门狗总开关（默认开启）</summary>
        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>扫描间隔（秒），默认 1</summary>
        [JsonProperty("scanIntervalSeconds")]
        public int ScanIntervalSeconds { get; set; } = 1;

        /// <summary>挂起连接（已连接但未握手/无数据）超时（秒），默认 10。正常客户端握手在毫秒级完成</summary>
        [JsonProperty("handshakeTimeoutSeconds")]
        public int HandshakeTimeoutSeconds { get; set; } = 10;

        /// <summary>同 IP 允许的最大挂起连接数，超出即清理，默认 3</summary>
        [JsonProperty("maxHangingPerIp")]
        public int MaxHangingPerIp { get; set; } = 3;

        /// <summary>全局挂起连接数上限（默认 20，必须小于槽位数 = MaxSlots+ReservedSlots）</summary>
        [JsonProperty("maxHangingGlobal")]
        public int MaxHangingGlobal { get; set; } = 20;

        /// <summary>监听停止（槽位满）后自动恢复监听（回归 1.4.5.6 StartListeningIfNeeded），默认开启</summary>
        [JsonProperty("restoreListening")]
        public bool RestoreListening { get; set; } = true;

        /// <summary>监听停止（槽位满）时输出告警日志，默认开启</summary>
        [JsonProperty("warnOnListenStop")]
        public bool WarnOnListenStop { get; set; } = true;

        /// <summary>清理挂起连接时写日志，默认开启</summary>
        [JsonProperty("logKicks")]
        public bool LogKicks { get; set; } = true;

        // ── 可选限流钩子（默认关，见类注释）──
        /// <summary>
        /// 是否用 MonoMod detour 挂钩 Netplay.OnConnectionAccepted 做进槽前限流。
        /// 默认 false：本服是定制 1.4.5.7，detour 连接路径有内核崩溃风险（v4 实测）。
        /// 纯看门狗模式（默认）已能通过 1s 级清理 + 恢复监听解决假死。
        /// 如需更强的前置拦截再置 true，并在低峰期实测。
        /// </summary>
        [JsonProperty("rateLimitEnabled")]
        public bool RateLimitEnabled { get; set; } = false;

        /// <summary>每 IP 在限流窗口内的最大连接次数，超出直接拒绝，默认 10</summary>
        [JsonProperty("maxConnectionsPerWindow")]
        public int MaxConnectionsPerWindow { get; set; } = 10;

        /// <summary>限流窗口长度（秒），默认 5</summary>
        [JsonProperty("rateWindowSeconds")]
        public int RateWindowSeconds { get; set; } = 5;

        /// <summary>限流白名单 IP（如局域网/后端服务器），默认空</summary>
        [JsonProperty("ipWhiteList")]
        public List<string> IpWhiteList { get; set; } = new();

        /// <summary>拒绝连接时记录汇总日志，默认开启</summary>
        [JsonProperty("logRejections")]
        public bool LogRejections { get; set; } = true;
    }

    public static class GuardCore
    {
        private static readonly string ConfigPath = Path.Combine(TShock.SavePath, "ConnectionGuard", "connection-guard.json");
        private static GuardConfig _config = new();
        private static System.Timers.Timer? _timer;
        private static bool _initialized;

        // ═══ 可选限流钩子（rateLimitEnabled）═══
        private static Hook? _rateHook;
        private static bool _hookFiredLogged;

        private delegate void OrigOnConnectionAccepted(ISocket client);

        private static readonly Dictionary<string, Queue<DateTime>> _connectTimes = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> _rejectCounts = new(StringComparer.OrdinalIgnoreCase);
        private static int _rejectTotal;

        // ═══ 挂起清理看门狗 ═══
        private static readonly Dictionary<int, DateTime> _suspectSince = new();
        private static bool _lastListening = true;

        private static readonly object _sync = new();

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            LoadConfig();

            // ── 可选：OnConnectionAccepted 进槽前限流钩子（默认关，见类注释）──
            if (_config.RateLimitEnabled)
                HookRateLimit();

            // ── 启动挂起清理定时扫描 ──
            _timer = new System.Timers.Timer(Math.Max(1, _config.ScanIntervalSeconds) * 1000);
            _timer.Elapsed += (_, _) => Scan();
            _timer.AutoReset = true;
            _timer.Start();

            TShock.Log.ConsoleInfo($"[ConnectionGuard] 连接看门狗已启动 (扫描:{_config.ScanIntervalSeconds}s, 握手超时:{_config.HandshakeTimeoutSeconds}s, 恢复监听:{(_config.RestoreListening ? "开" : "关")}, 限流钩子:{(_config.RateLimitEnabled && _rateHook != null ? "已挂载" : (_config.RateLimitEnabled ? "挂载失败" : "关闭"))})");
        }

        public static void Dispose()
        {
            if (!_initialized) return;
            _initialized = false;

            try { _rateHook?.Dispose(); } catch { }
            _rateHook = null;

            if (_timer != null)
            {
                _timer.Stop();
                _timer.Dispose();
                _timer = null;
            }
            lock (_sync)
            {
                _connectTimes.Clear();
                _rejectCounts.Clear();
                _rejectTotal = 0;
            }
            lock (_suspectSince) _suspectSince.Clear();

            TShock.Log.ConsoleInfo("[ConnectionGuard] 连接看门狗已停止");
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
                    _config = JsonConvert.DeserializeObject<GuardConfig>(json) ?? new GuardConfig();

                    // ⚠ v4→v5 迁移保护：旧配置（无 restoreListening 键）是 v4 时代生成的，
                    // 其 rateLimitEnabled 可能为 true（当时的默认），但 v5 已证明该钩子在
                    // 本定制 1.4.5.7 上有内核崩溃风险 → 强制回落到安全默认（关闭钩子）。
                    if (!json.Contains("\"restoreListening\"", StringComparison.Ordinal))
                        _config.RateLimitEnabled = false;
                }
                else
                {
                    _config = new GuardConfig();
                    SaveConfig();
                }

                _config.ScanIntervalSeconds = Math.Max(1, _config.ScanIntervalSeconds);
                _config.HandshakeTimeoutSeconds = Math.Max(1, _config.HandshakeTimeoutSeconds);
                _config.MaxHangingPerIp = Math.Max(1, _config.MaxHangingPerIp);
                _config.RateWindowSeconds = Math.Max(1, _config.RateWindowSeconds);
                _config.MaxConnectionsPerWindow = Math.Max(1, _config.MaxConnectionsPerWindow);
                _config.MaxHangingGlobal = Math.Max(1, _config.MaxHangingGlobal);
                _config.IpWhiteList ??= new List<string>();
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[ConnectionGuard] 加载配置失败: {ex.Message}");
                _config = new GuardConfig();
            }
        }

        public static void SaveConfig()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(_config, Formatting.Indented));
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[ConnectionGuard] 保存配置失败: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════
        // 可选：OnConnectionAccepted 进槽前限流钩子
        // ═══════════════════════════════════════════

        private static void HookRateLimit()
        {
            try
            {
                // 1.4.5.7 服务端（OTAPI 注入版）为 public static；1.4.5.6 为 private static
                // → 必须 Public|NonPublic 双查
                var method = typeof(Netplay).GetMethod("OnConnectionAccepted", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (method == null)
                {
                    TShock.Log.ConsoleWarn("[ConnectionGuard] 未找到 Netplay.OnConnectionAccepted，限流钩子未启用（看门狗兜底仍工作）");
                    return;
                }
                _rateHook = new Hook(method, OnConnectionAcceptedHook);
                TShock.Log.ConsoleInfo($"[ConnectionGuard] 限流钩子已挂载 (每IP {_config.MaxConnectionsPerWindow}次/{_config.RateWindowSeconds}s)");
            }
            catch (Exception ex)
            {
                _rateHook = null;
                TShock.Log.ConsoleWarn($"[ConnectionGuard] 限流钩子注册失败: {ex.Message}（看门狗兜底仍工作）");
            }
        }

        private static void OnConnectionAcceptedHook(OrigOnConnectionAccepted orig, ISocket client)
        {
            if (!_hookFiredLogged)
            {
                _hookFiredLogged = true;
                TShock.Log.ConsoleInfo("[ConnectionGuard] 限流钩子首次触发（Netplay.OnConnectionAccepted detour 生效）");
            }

            bool allow = true;
            string rejectReason = "";
            try
            {
                if (_config.RateLimitEnabled && !IsWhiteListed(client))
                {
                    allow = CheckLimit(client, out rejectReason);
                }
            }
            catch (Exception ex)
            {
                // 判定异常 → 放行，绝不因限流逻辑自身故障误伤正常连接
                TShock.Log.ConsoleError($"[ConnectionGuard] 限流判定异常: {ex.Message}");
                allow = true;
            }

            if (!allow)
            {
                Reject(client, rejectReason);
                return;
            }

            orig(client);
        }

        private static bool CheckLimit(ISocket client, out string reason)
        {
            var ip = GetIpFromAddress(client?.GetRemoteAddress());
            var now = DateTime.UtcNow;

            // ① 每 IP 连接速率（滑动窗口）
            lock (_sync)
            {
                if (_connectTimes.TryGetValue(ip, out var q))
                {
                    while (q.Count > 0 && (now - q.Peek()).TotalSeconds > _config.RateWindowSeconds)
                        q.Dequeue();
                    if (q.Count >= _config.MaxConnectionsPerWindow)
                    {
                        reason = $"连接速率超限({q.Count}次/{_config.RateWindowSeconds}s)";
                        return false;
                    }
                }
                else
                {
                    q = new Queue<DateTime>();
                    _connectTimes[ip] = q;
                }
                q.Enqueue(now);
            }

            // ② 每 IP 挂起连接数 / 全局挂起连接数（实时统计已入槽的 State==0 连接）
            int hangingGlobal = 0;
            int hangingIp = 0;
            int maxSlots = Math.Min(Main.maxNetPlayers, Netplay.Clients.Length);
            for (int i = 0; i < maxSlots; i++)
            {
                var c = Netplay.Clients[i];
                if (c == null) continue;
                bool connected;
                try { connected = c.IsConnected(); }
                catch { connected = false; }
                if (!connected || c.State != 0) continue;
                hangingGlobal++;
                if (string.Equals(GetIp(c), ip, StringComparison.OrdinalIgnoreCase))
                    hangingIp++;
            }

            if (hangingGlobal >= _config.MaxHangingGlobal)
            {
                reason = $"全局挂起连接超限({hangingGlobal}>={_config.MaxHangingGlobal})";
                return false;
            }
            if (hangingIp >= _config.MaxHangingPerIp)
            {
                reason = $"同IP挂起连接超限({hangingIp}>={_config.MaxHangingPerIp})";
                return false;
            }

            reason = "";
            return true;
        }

        private static void Reject(ISocket client, string reason)
        {
            try
            {
                var ip = GetIpFromAddress(client?.GetRemoteAddress());
                lock (_sync)
                {
                    _rejectCounts[ip] = _rejectCounts.TryGetValue(ip, out var n) ? n + 1 : 1;
                    _rejectTotal++;
                }
                client?.Close();
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[ConnectionGuard] 拒绝连接失败: {ex.Message}");
            }
        }

        private static bool IsWhiteListed(ISocket client)
        {
            var ip = GetIpFromAddress(client?.GetRemoteAddress());
            if (IsLoopbackIp(ip)) return true;

            var list = _config.IpWhiteList;
            if (list == null || list.Count == 0) return false;
            foreach (var w in list)
            {
                if (!string.IsNullOrWhiteSpace(w) && string.Equals(w.Trim(), ip, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        // ═══════════════════════════════════════════
        // 挂起清理看门狗
        // ═══════════════════════════════════════════

        private static void Scan()
        {
            try
            {
                DoScan();
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[ConnectionGuard] 扫描异常: {ex.Message}");
            }
        }

        private static void DoScan()
        {
            if (!_config.Enabled) return;
            if (!IsServerReady()) return;   // 服务器未开监听（插件加载早于开服）：跳过一切

            var now = DateTime.UtcNow;
            int maxSlots = Math.Min(Main.maxNetPlayers, Netplay.Clients.Length);

            // ── 第一遍：统计每 IP 的挂起连接数（State==0 = 已连接但未完成任何握手）──
            var hangingByIp = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var ipOfSlot = new Dictionary<int, string>();
            int hangingGlobal = 0;
            for (int i = 0; i < maxSlots; i++)
            {
                var c = Netplay.Clients[i];
                if (c == null) continue;
                bool connected;
                try { connected = c.IsConnected(); } catch { connected = false; }
                if (connected && c.State == 0)
                {
                    var ip = GetIp(c);
                    if (IsLoopbackIp(ip)) continue;   // 本机连接：不统计不清理
                    ipOfSlot[i] = ip;
                    hangingByIp[ip] = hangingByIp.TryGetValue(ip, out var n) ? n + 1 : 1;
                    hangingGlobal++;
                }
            }

            // ── 第二遍：清理超时/超限的挂起连接（Socket.Close → ServerLoop 下一轮 Reset 释放槽位）──
            int kicked = 0;
            for (int i = 0; i < maxSlots; i++)
            {
                var c = Netplay.Clients[i];
                if (c == null) continue;
                bool connected;
                try { connected = c.IsConnected(); } catch { connected = false; }

                if (!connected || c.State != 0)
                {
                    lock (_suspectSince) _suspectSince.Remove(i);
                    continue;
                }
                if (IsLoopbackIp(GetIp(c)))
                {
                    lock (_suspectSince) _suspectSince.Remove(i);
                    continue;
                }

                string reason = "";
                lock (_suspectSince)
                {
                    if (!_suspectSince.TryGetValue(i, out var since))
                    {
                        _suspectSince[i] = now;
                    }
                    else if ((now - since).TotalSeconds >= _config.HandshakeTimeoutSeconds)
                    {
                        reason = $"握手超时({(int)(now - since).TotalSeconds}s)";
                    }
                }

                if (reason.Length == 0 &&
                    ipOfSlot.TryGetValue(i, out var ip) &&
                    hangingByIp.TryGetValue(ip, out var cnt) &&
                    cnt > _config.MaxHangingPerIp)
                {
                    reason = $"同IP挂起连接超限({cnt}>{_config.MaxHangingPerIp})";
                }

                if (reason.Length == 0 && hangingGlobal > _config.MaxHangingGlobal)
                {
                    reason = $"全局挂起连接超限({hangingGlobal}>{_config.MaxHangingGlobal})";
                }

                if (reason.Length > 0)
                {
                    lock (_suspectSince) _suspectSince.Remove(i);
                    Kick(c, i, reason);
                    kicked++;
                }
            }

            if (kicked > 0)
                TShock.Log.ConsoleInfo($"[ConnectionGuard] 本次清理挂起连接 {kicked} 个");

            if (_config.LogRejections)
            {
                lock (_sync)
                {
                    if (_rejectTotal > 0)
                    {
                        var sb = new System.Text.StringBuilder($"[ConnectionGuard] 限流拒绝汇总（最近 {_config.ScanIntervalSeconds}s 共 {_rejectTotal} 次）: ");
                        foreach (var kv in _rejectCounts)
                            sb.Append($"{kv.Key}×{kv.Value} ");
                        TShock.Log.ConsoleInfo(sb.ToString());
                        _rejectCounts.Clear();
                        _rejectTotal = 0;
                    }
                }
            }

            bool listening = Netplay.IsListening;
            if (_config.WarnOnListenStop && !listening && _lastListening)
                TShock.Log.ConsoleWarn("[ConnectionGuard] ⚠ 服务器已停止接受新连接（槽位被占满）");

            // ── 恢复监听（回归 1.4.5.6 StartListeningIfNeeded）：有空槽才恢复 ──
            if (!listening && _config.RestoreListening && FreeSlotCount(maxSlots) > 0)
                TryRestoreListening();

            _lastListening = listening;
        }

        private static int FreeSlotCount(int maxSlots)
        {
            int free = 0;
            for (int i = 0; i < maxSlots; i++)
            {
                var c = Netplay.Clients[i];
                if (c == null) { free++; continue; }
                bool connected;
                try { connected = c.IsConnected(); } catch { connected = false; }
                if (!connected) free++;
            }
            return free;
        }

        /// <summary>
        /// 安全恢复监听（托管代码 + 反射，无 detour，不可能崩内核）。
        /// 等价于 1.4.5.6 的 StartListeningIfNeeded：重建/重启监听接受新连接。
        /// 失败仅告警，下轮扫描自动重试。
        /// </summary>
        private static void TryRestoreListening()
        {
            try
            {
                var method = typeof(Netplay).GetMethod("OnConnectionAccepted", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (method == null)
                {
                    TShock.Log.ConsoleWarn("[ConnectionGuard] 恢复监听失败：未找到 Netplay.OnConnectionAccepted（下轮重试）");
                    return;
                }
                var d = method.CreateDelegate(typeof(SocketConnectionAccepted)) as SocketConnectionAccepted;
                if (d == null)
                {
                    TShock.Log.ConsoleWarn("[ConnectionGuard] 恢复监听失败：OnConnectionAccepted 签名与 SocketConnectionAccepted 不匹配（下轮重试）");
                    return;
                }
                var listener = Netplay.TcpListener;
                if (listener == null)
                {
                    TShock.Log.ConsoleWarn("[ConnectionGuard] 恢复监听失败：Netplay.TcpListener 为空（下轮重试）");
                    return;
                }

                bool ok = listener.StartListening(d);
                if (ok)
                {
                    Netplay.IsListening = true;
                    TShock.Log.ConsoleInfo("[ConnectionGuard] ✓ 已恢复监听（回归 1.4.5.6 StartListeningIfNeeded）");
                }
                else
                {
                    TShock.Log.ConsoleWarn("[ConnectionGuard] 恢复监听失败：StartListening 返回 false（端口占用/TIME_WAIT，下轮重试）");
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleWarn($"[ConnectionGuard] 恢复监听异常: {ex.Message}");
            }
        }

        private static void Kick(RemoteClient client, int slot, string reason)
        {
            try
            {
                if (_config.LogKicks)
                    TShock.Log.ConsoleInfo($"[ConnectionGuard] 清理挂起连接 槽位:{slot} IP:{GetIp(client)} 原因:{reason}");

                // 关闭底层 socket → IsConnected()==false → 原版 ServerLoop 下一轮 Reset 释放槽位
                //（与成熟开源插件 yaaiomni/Chireiden 同款方式，比 PendingTermination 更可靠）
                client.Socket?.Close();
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[ConnectionGuard] 清理挂起连接失败 槽位:{slot}: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════
        // 工具
        // ═══════════════════════════════════════════

        /// <summary>服务器是否已开监听（插件加载可能早于开服，避免启动期误判/误操作）</summary>
        private static bool IsServerReady()
        {
            try
            {
                return Main.netMode == 2 && Netplay.TcpListener != null && !IsNetplayDisconnect();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 反射安全读取 Netplay.Disconnect。
        /// 不同 TShock/Terraria 版本的 Terraria.Netplay 类型身份/字段版本可能不一致，
        /// 直接字段访问会在运行时抛 MissingFieldException。改用反射 + 缓存，
        /// 字段存在→正常读取；不存在/读取失败→返回 false（视为未断开，保守不误伤）。
        /// </summary>
        private static FieldInfo? _netplayDisconnectField;
        private static bool IsNetplayDisconnect()
        {
            try
            {
                if (_netplayDisconnectField == null)
                    _netplayDisconnectField = typeof(Netplay).GetField("Disconnect", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                return _netplayDisconnectField?.GetValue(null) is bool b && b;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsLoopbackIp(string ip) => ip == "127.0.0.1" || ip == "::1" || ip == "localhost";

        private static string GetIp(RemoteClient client)
        {
            try
            {
                return GetIpFromAddress(client?.Socket?.GetRemoteAddress());
            }
            catch
            {
                return "?";
            }
        }

        /// <summary>从 RemoteAddress 提取 IP 字符串（Terraria 仅接受 IPv4，格式 IP:Port，取最后一个冒号前）</summary>
        private static string GetIpFromAddress(RemoteAddress? addr)
        {
            try
            {
                var s = addr?.ToString();
                if (string.IsNullOrEmpty(s)) return "?";
                var idx = s.LastIndexOf(':');
                return idx > 0 ? s.Substring(0, idx) : s;
            }
            catch
            {
                return "?";
            }
        }
    }
}
