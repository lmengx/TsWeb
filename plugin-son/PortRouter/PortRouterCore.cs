using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TShockAPI;

namespace PortRouter
{
    /// <summary>
    /// 端口协议路由核心（PortRouterCore）。
    ///
    /// 机制：通过 OTAPI 官方扩展点 OTAPI.Hooks.Netplay.CreateTcpListener 安装协议感知监听器
    /// HttpAwareSocket（与 TShock 安装 LinuxTcpSocket、开源插件 ProxyProtocolSocket / yaaiomni
    /// 同款；插件 Order=1000 保证在 TShock 之后订阅 → 替换必然生效）。
    ///
    /// 协议判定发生在监听器 accept 循环内、任何日志/进槽之前（见 HttpAwareSocket.ListenLoop）：
    ///   - HTTP（GET/POST/HEAD/... 前 2 字节为 ASCII 字母）→ 静默字节泵转发到 REST 端口，
    ///     不打印「xxx正在连接」、不占游戏槽位；
    ///   - 游戏协议（首包 = [2 字节长度][MessageID 0x01]，长度高字节恒为 0x00）→ 正常打印
    ///     「正在连接」并交给原版进槽流程 —— 客户端直连、无转发，RemoteEndPoint 即真实来源 IP。
    ///
    /// 安全边界（fail-open）：嗅探/转发任何异常一律放行进游戏流程，绝不误伤正常游戏连接。
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
                // 主机制：安装协议感知监听器（OTAPI 官方扩展点，TShock 装 LinuxTcpSocket 同款）
                OTAPI.Hooks.Netplay.CreateTcpListener += OnCreateTcpListener;
                TShock.Log.ConsoleInfo("[PortRouter] 已注册协议感知监听器（Order=1000，替换 TShock 默认监听器；HTTP 不再打印「正在连接」）");
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[PortRouter] 注册 CreateTcpListener 失败: {ex.Message}（端口协议路由未启用）");
                return;
            }

            TShock.Log.ConsoleInfo($"[PortRouter] 端口协议路由已启用：游戏端口同时接受 HTTP(→REST 127.0.0.1:{GetRestPort()}) 与游戏流量（游戏流量原 IP 直连保留，嗅探超时 {_config.HttpDetectTimeoutMs}ms）");
        }

        /// <summary>
        /// OTAPI CreateTcpListener 回调：把游戏监听器替换为协议感知监听器。
        /// 插件 Order=1000 保证本回调在 TShock 之后订阅 → args.Result 最后写入 → 生效。
        /// </summary>
        private static void OnCreateTcpListener(object? sender, OTAPI.Hooks.Netplay.CreateTcpListenerEventArgs args)
        {
            args.Result = new HttpAwareSocket();
            TShock.Log.ConsoleInfo("[PortRouter] 协议感知监听器已生效（HTTP 静默转发 REST / 游戏流量原 IP 进槽）");
        }

        public static void Dispose()
        {
            if (!_initialized) return;
            _initialized = false;

            try { OTAPI.Hooks.Netplay.CreateTcpListener -= OnCreateTcpListener; } catch { }

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
        // 协议判定与转发（由 HttpAwareSocket.ListenLoop 调用）
        // ═══════════════════════════════════════════

        /// <summary>
        /// 嗅探并处理一条新连接（accept 后、任何日志/进槽之前）。
        /// 返回 true = 判定为 HTTP 并已转交 REST（连接所有权转移）；false = 游戏流量，调用方继续原流程。
        /// fail-open：任何异常返回 false，绝不误伤游戏连接。
        /// </summary>
        internal static bool TryHandleHttp(TcpClient tcp)
        {
            try
            {
                if (_config.Enabled && PeekHttp(tcp))
                {
                    Interlocked.Increment(ref _httpHandled);
                    LogHttpOnce(tcp);
                    _ = RelayHttpAsync(tcp, GetRestPort());
                    return true;
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[PortRouter] 协议判定异常（放行进游戏流程）: {ex.Message}");
            }
            return false;
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
                TShock.Log.ConsoleInfo($"[PortRouter] 首次捕获 HTTP 连接（来源 {ip}），已静默转发到 REST；游戏端口协议路由生效");
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
