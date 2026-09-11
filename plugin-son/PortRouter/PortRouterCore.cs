using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MonoMod.RuntimeDetour;
using Newtonsoft.Json;
using Terraria;
using Terraria.Net.Sockets;
using TShockAPI;

namespace PortRouter
{
    /// <summary>
    /// 端口协议路由核心（PortRouterCore）。
    ///
    /// 原理：
    ///   客户端直连游戏端口（TCP 连接终止在游戏服监听器上），本模块在
    ///   Netplay.OnConnectionAccepted 进槽前用 SocketFlags.Peek 嗅探首字节（不消费数据）：
    ///     - HTTP（GET/POST/HEAD/... 前 2 字节为 ASCII 字母）→ 字节泵转发到 REST 端口
    ///       （TShock REST / TSWeb WebRestServer，默认 127.0.0.1:7878）—— 用户要求的「http 转发到 rest」
    ///     - 游戏协议（首包 = [2 字节长度][MessageID 0x01]，长度高字节恒为 0x00）→ 原位走原版进槽流程
    ///       —— 无任何转发，RemoteEndPoint 即真实来源 IP
    ///
    /// 实现：MonoMod RuntimeDetour 挂钩 Netplay.OnConnectionAccepted
    ///   （与 plugin-son/ConnectionGuard 已验证的限流钩子同款模式）。
    ///   ⚠ 严禁对监听生命周期方法（StartListening/StopListening/ListenLoop）做 detour ——
    ///   ConnectionGuard v4 实测会导致 accept 线程泄漏 + 槽位分配数据竞争 → 内核崩溃。
    ///
    /// 安全边界（fail-open）：嗅探/反射/转发任何异常一律放行进游戏流程（orig），
    /// 绝不因路由逻辑自身故障误伤正常游戏连接。
    /// </summary>
    public class PortRouterConfig
    {
        /// <summary>总开关（默认开）。关闭后所有连接原样进游戏流程。</summary>
        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>等待首字节的超时（毫秒），默认 1500。游戏/HTTP 客户端都是连上即发包，正常在毫秒级返回</summary>
        [JsonProperty("httpDetectTimeoutMs")]
        public int HttpDetectTimeoutMs { get; set; } = 1500;

        /// <summary>HTTP 转发目标 REST 端口。0 = 自动取 TShock 配置的 RestApiPort（默认 7878）</summary>
        [JsonProperty("restForwardPort")]
        public int RestForwardPort { get; set; } = 0;

        /// <summary>记录每次 HTTP 转发日志（默认关，避免被轮询/健康检查刷屏）</summary>
        [JsonProperty("logHttpDetections")]
        public bool LogHttpDetections { get; set; } = false;
    }

    public static class PortRouterCore
    {
        /// <summary>配置路径：TShock.SavePath/PortRouter/portrouter.json</summary>
        private static readonly string ConfigPath = Path.Combine(TShock.SavePath, "PortRouter", "portrouter.json");
        private static PortRouterConfig _config = new();
        private static bool _initialized;

        // ═══ MonoMod Hook（与 ConnectionGuard 限流钩子同款模式）═══
        private static Hook? _hook;
        private delegate void OrigOnConnectionAccepted(ISocket client);

        // ═══ HTTP 判定 ═══
        // 嗅探缓冲（只需前 2 字节即可判定）
        private static readonly byte[] _peekBuf = new byte[4];

        // ═══ 统计 ═══
        private static int _httpHandled;
        private static int _firstHttpLogged;

        // ═══════════════════════════════════════════
        // 生命周期
        // ═══════════════════════════════════════════

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            LoadConfig();

            if (!_config.Enabled)
            {
                TShock.Log.ConsoleInfo("[PortRouter] 端口协议路由未启用（enabled=false），游戏端口仅接受游戏流量");
                return;
            }

            try
            {
                // 1.4.5.x 服务端（OTAPI 注入版）OnConnectionAccepted 为 public/private static 不定
                // → Public|NonPublic 双查（与 ConnectionGuard 同款）
                var method = typeof(Netplay).GetMethod("OnConnectionAccepted",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (method == null)
                {
                    TShock.Log.ConsoleWarn("[PortRouter] 未找到 Netplay.OnConnectionAccepted，端口协议路由未启用");
                    return;
                }
                _hook = new Hook(method, OnConnectionAcceptedHook);
                TShock.Log.ConsoleInfo($"[PortRouter] 端口协议路由已启用：游戏端口同时接受 HTTP(→REST 127.0.0.1:{GetRestPort()}) 与游戏流量（游戏流量原 IP 直连保留，嗅探超时 {_config.HttpDetectTimeoutMs}ms）");
            }
            catch (Exception ex)
            {
                _hook = null;
                TShock.Log.ConsoleError($"[PortRouter] 挂钩 Netplay.OnConnectionAccepted 失败: {ex.Message}（端口协议路由未启用）");
            }
        }

        public static void Dispose()
        {
            if (!_initialized) return;
            _initialized = false;

            try { _hook?.Dispose(); } catch { }
            _hook = null;

            TShock.Log.ConsoleInfo("[PortRouter] 端口协议路由已卸载");
        }

        /// <summary>加载配置（仅启动时；修改配置需重启或重载插件）</summary>
        public static void LoadConfig()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (File.Exists(ConfigPath))
                {
                    _config = JsonConvert.DeserializeObject<PortRouterConfig>(File.ReadAllText(ConfigPath)) ?? new PortRouterConfig();
                }
                else
                {
                    _config = new PortRouterConfig();
                    SaveConfig();
                }

                _config.HttpDetectTimeoutMs = Math.Max(100, Math.Min(10000, _config.HttpDetectTimeoutMs));
                _config.RestForwardPort = Math.Max(0, Math.Min(65535, _config.RestForwardPort));
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[PortRouter] 加载配置失败: {ex.Message}");
                _config = new PortRouterConfig();
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
                TShock.Log.ConsoleError($"[PortRouter] 保存配置失败: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════
        // 进槽前协议判定
        // ═══════════════════════════════════════════

        private static void OnConnectionAcceptedHook(OrigOnConnectionAccepted orig, ISocket client)
        {
            // ═══ fail-open：任何异常都放行进游戏流程 ═══
            try
            {
                if (_config.Enabled)
                {
                    var tcp = GetTcpClient(client);
                    if (tcp != null && PeekHttp(tcp))
                    {
                        // 判定为 HTTP：字节泵转发到 REST（不占游戏槽位，不调 orig）
                        Interlocked.Increment(ref _httpHandled);
                        LogHttpOnce(tcp);
                        _ = RelayHttpAsync(tcp, GetRestPort());
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[PortRouter] 协议判定异常（放行进游戏流程）: {ex.Message}");
            }

            // 游戏流量（或嗅探超时/失败）：原版进槽流程，原 IP 保留
            orig(client);
        }

        /// <summary>
        /// 从 ISocket 提取 TcpClient。兼容 TShock 的 LinuxTcpSocket（_connection 为 public）
        /// 与原版 TcpSocket（_connection 为 private），统一反射读取，规避程序集身份差异。
        /// </summary>
        private static TcpClient? GetTcpClient(ISocket client)
        {
            if (client == null) return null;
            var type = client.GetType();
            var f = type.GetField("_connection", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return f?.GetValue(client) as TcpClient;
        }

        /// <summary>
        /// 非阻塞式嗅探（SocketFlags.Peek 不消费数据）：
        /// 设置短暂 ReceiveTimeout 等待首字节，读取后立即恢复原超时。
        /// 返回 true 表示首 2 字节符合 HTTP 特征。
        /// </summary>
        private static bool PeekHttp(TcpClient tcp)
        {
            try
            {
                var sock = tcp.Client;
                if (sock == null) return false;

                int oldTimeout = sock.ReceiveTimeout;
                sock.ReceiveTimeout = _config.HttpDetectTimeoutMs;
                try
                {
                    int n = sock.Receive(_peekBuf, 0, _peekBuf.Length, SocketFlags.Peek);
                    return n >= 2 && IsHttpStart(_peekBuf, n);
                }
                catch (SocketException)
                {
                    return false;   // 超时/无数据 → 视为游戏流量
                }
                finally
                {
                    sock.ReceiveTimeout = oldTimeout;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// HTTP 判定：前两字节均为 ASCII 字母（A-Z/a-z）。
        ///   - 所有 HTTP 方法（GET/POST/PUT/DELETE/HEAD/OPTIONS/PATCH/TRACE/CONNECT，含小写变体）
        ///     与代理式请求行（HTTP/...）都满足；
        ///   - 游戏协议首包 = [2 字节长度][MessageID 0x01]，长度高字节恒为 0x00（非字母）→ 必判为游戏。
        ///     只有首包长度 ≥ 0x4141（16705 字节）且两个长度字节恰好都是字母才会误判 —— 对
        ///     ConnectRequest 握手包（数十字节）不现实。
        /// </summary>
        private static bool IsHttpStart(byte[] b, int n)
        {
            if (n < 2) return false;
            return IsAsciiLetter(b[0]) && IsAsciiLetter(b[1]);
        }

        private static bool IsAsciiLetter(byte v)
            => (v >= (byte)'A' && v <= (byte)'Z') || (v >= (byte)'a' && v <= (byte)'z');

        // ═══════════════════════════════════════════
        // HTTP 转发到 REST（字节泵）
        // ═══════════════════════════════════════════

        /// <summary>
        /// 把一条已判定为 HTTP 的游戏端口连接，原样字节泵转发到本地 REST 端口。
        /// 纯透传：不解析 HTTP、不感知 SSE/长连接，双向往返直到任一侧关闭。
        /// （用户要求「http 转发到 rest」；游戏流量不走此路径，故游戏服原始 IP 不受影响）
        /// </summary>
        private static async Task RelayHttpAsync(TcpClient client, int port)
        {
            TcpClient? upstream = null;
            try
            {
                using var _ = client;
                upstream = new TcpClient { NoDelay = true };
                await upstream.ConnectAsync(IPAddress.Loopback, port);

                using var down = client.GetStream();
                using var up = upstream.GetStream();
                var c2s = PumpAsync(down, up);
                var s2c = PumpAsync(up, down);
                await Task.WhenAny(c2s, s2c);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[PortRouter] HTTP 转发到 REST(:{port}) 失败: {ex.Message}");
            }
            finally
            {
                try { upstream?.Close(); } catch { }
            }
        }

        /// <summary>单向字节泵：from → to，任一侧 EOF/异常即结束</summary>
        private static async Task PumpAsync(Stream from, Stream to)
        {
            var buf = new byte[8192];
            try
            {
                int n;
                while ((n = await from.ReadAsync(buf, 0, buf.Length)) > 0)
                {
                    await to.WriteAsync(buf, 0, n);
                    await to.FlushAsync();
                }
            }
            catch
            {
                // 对端关闭/异常：结束本方向泵送，WhenAny 会关闭另一侧
            }
        }

        // ═══════════════════════════════════════════
        // 工具
        // ═══════════════════════════════════════════

        private static int GetRestPort()
        {
            if (_config.RestForwardPort > 0) return _config.RestForwardPort;
            try { return TShock.Config.Settings.RestApiPort; }
            catch { return 7878; }
        }

        private static void LogHttpOnce(TcpClient tcp)
        {
            // 首次捕获必记，之后按配置
            if (Interlocked.Exchange(ref _firstHttpLogged, 1) == 0)
            {
                var ip = GetRemoteIp(tcp);
                TShock.Log.ConsoleInfo($"[PortRouter] 首次捕获 HTTP 连接（来源 {ip}），已转发到 REST；游戏端口协议路由生效");
            }
            else if (_config.LogHttpDetections)
            {
                TShock.Log.ConsoleInfo($"[PortRouter] HTTP 连接已转发到 REST（来源 {GetRemoteIp(tcp)}，累计 {Volatile.Read(ref _httpHandled)}）");
            }
        }

        private static string GetRemoteIp(TcpClient tcp)
        {
            try
            {
                return tcp.Client?.RemoteEndPoint?.ToString() ?? "?";
            }
            catch
            {
                return "?";
            }
        }

        /// <summary>当前已转发到 REST 的 HTTP 连接总数（供调试）</summary>
        public static int HttpHandledCount => Volatile.Read(ref _httpHandled);
    }
}
