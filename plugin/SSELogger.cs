using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HttpServer;
using Newtonsoft.Json;
using Rests;
using TerrariaApi.Server;
using TShockAPI;

namespace TShockData
{
    /// <summary>
    /// 一个带颜色的文本片段
    /// </summary>
    public class LogSegment
    {
        [JsonProperty("t")]
        public string Text { get; set; } = "";

        /// <summary>ConsoleColor 名称，如 "Red"、"Green"、"Gray"，null 表示默认色</summary>
        [JsonProperty("c")]
        public string? Color { get; set; }
    }

    /// <summary>
    /// 拦截 Console 输出，同时写入原始输出流和内存缓冲区
    /// 捕获 Console.ForegroundColor 信息，以结构化的 LogSegment 形式存储
    /// </summary>
    public class LogInterceptor : TextWriter
    {
        private readonly TextWriter _original;
        private readonly List<LogSegment> _segments = new();
        private StringBuilder _currentText = new();
        private ConsoleColor? _currentColor = null;

        public LogInterceptor(TextWriter original)
        {
            _original = original;
        }

        public override Encoding Encoding => _original.Encoding;
        public override IFormatProvider FormatProvider => _original.FormatProvider;
        public override string NewLine => _original.NewLine;

        /// <summary>
        /// 检查 ForegroundColor 是否变化，若变化则关闭当前段、开启新段
        /// </summary>
        private void CheckColor()
        {
            var cc = Console.ForegroundColor;
            if (_currentColor.HasValue && _currentColor.Value == cc)
                return; // 颜色未变

            // 颜色变了 → 保存当前段
            FlushCurrentSegment();
            _currentColor = cc;
        }

        /// <summary>
        /// 将 _currentText 中的内容作为一个 LogSegment 提交
        /// </summary>
        private void FlushCurrentSegment()
        {
            if (_currentText.Length == 0) return;
            _segments.Add(new LogSegment
            {
                Text = _currentText.ToString(),
                Color = _currentColor?.ToString()
            });
            _currentText.Clear();
        }

        /// <summary>
        /// 提交当前段 + 刷新整行为 JSON 字符串，推送到 SSE 环形缓冲区
        /// </summary>
        private void FlushLine()
        {
            FlushCurrentSegment();
            _currentColor = null;

            if (_segments.Count == 0) return;

            // 序列化为 JSON 数组
            var json = JsonConvert.SerializeObject(_segments);
            _segments.Clear();

            SSELogger.AddLogLine(json);
        }

        // ──────────── Write(char) ────────────
        public override void Write(char value)
        {
            _original.Write(value);
            if (value == '\n')
                FlushLine();
            else if (value != '\r')
            {
                CheckColor();
                _currentText.Append(value);
            }
        }

        // ──────────── Write(char[]) ────────────
        public override void Write(char[]? buffer)
        {
            if (buffer == null) return;
            _original.Write(buffer);
            CheckColor();
            foreach (var c in buffer)
            {
                if (c == '\n') FlushLine();
                else if (c != '\r') _currentText.Append(c);
            }
        }

        // ──────────── Write(char[], int, int) ────────────
        public override void Write(char[]? buffer, int index, int count)
        {
            if (buffer == null) return;
            _original.Write(buffer, index, count);
            CheckColor();
            for (int i = index; i < index + count; i++)
            {
                var c = buffer[i];
                if (c == '\n') FlushLine();
                else if (c != '\r') _currentText.Append(c);
            }
        }

        // ──────────── Write(string) ────────────
        public override void Write(string? value)
        {
            if (value == null) return;
            _original.Write(value);
            CheckColor();
            foreach (var c in value)
            {
                if (c == '\n') FlushLine();
                else if (c != '\r') _currentText.Append(c);
            }
        }

        // ──────────── Write(StringBuilder) ────────────
        public override void Write(StringBuilder? value)
        {
            if (value == null) return;
            _original.Write(value);
            CheckColor();
            for (int i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c == '\n') FlushLine();
                else if (c != '\r') _currentText.Append(c);
            }
        }

        // ──────────── WriteLine() ────────────
        public override void WriteLine()
        {
            _original.WriteLine();
            FlushLine();
        }

        // ──────────── WriteLine(char[]) ────────────
        public override void WriteLine(char[]? buffer)
        {
            if (buffer == null) { WriteLine(); return; }
            _original.WriteLine(buffer);
            CheckColor();
            foreach (var c in buffer)
                if (c != '\r') _currentText.Append(c);
            FlushLine();
        }

        // ──────────── WriteLine(char[], int, int) ────────────
        public override void WriteLine(char[]? buffer, int index, int count)
        {
            if (buffer == null) { WriteLine(); return; }
            _original.WriteLine(buffer, index, count);
            CheckColor();
            for (int i = index; i < index + count; i++)
                if (buffer[i] != '\r') _currentText.Append(buffer[i]);
            FlushLine();
        }

        // ──────────── WriteLine(string) ────────────
        public override void WriteLine(string? value)
        {
            _original.WriteLine(value);
            CheckColor();
            if (value != null)
                foreach (var c in value)
                    if (c != '\r') _currentText.Append(c);
            FlushLine();
        }

        // ──────────── WriteLine(StringBuilder) ────────────
        public override void WriteLine(StringBuilder? value)
        {
            if (value == null) { WriteLine(); return; }
            _original.WriteLine(value);
            CheckColor();
            for (int i = 0; i < value.Length; i++)
                if (value[i] != '\r') _currentText.Append(value[i]);
            FlushLine();
        }
    }

    /// <summary>
    /// SSE 日志推送管理
    /// </summary>
    public static class SSELogger
    {
        // 日志环形缓冲区 — 每项是 LogSegment 序列化的 JSON 数组字符串
        private static readonly List<string> _logHistory = new();
        private static readonly object _logLock = new();
        private const int MaxLogLines = 1000;

        // SSE 客户端跟踪
        private static readonly List<SSEClient> _sseClients = new();
        private static readonly object _clientLock = new();
        private static LogInterceptor? _interceptor;
        private static TextWriter? _originalOut;
        private static bool _initialized = false;
        private static bool _hooksInstalled = false;
        private static TerrariaPlugin? _plugin;

        // 保存原始事件处理器引用
        private static Delegate? _originalOnRequest;
        private static Delegate? _ourDelegate;
        private static object? _listener;
        private static EventInfo? _requestEvent;

        /// <summary>
        /// SSE 客户端信息
        /// </summary>
        private class SSEClient : IDisposable
        {
            public Stream Stream { get; }
            public string Address { get; }
            public int ReadIndex { get; set; }  // 已读取到的日志位置
            public CancellationTokenSource CTS { get; }

            public SSEClient(Stream stream, string addr)
            {
                Stream = stream;
                Address = addr;
                CTS = new CancellationTokenSource();
                ReadIndex = 0;
            }

            public void Dispose()
            {
                CTS.Cancel();
                CTS.Dispose();
                try { Stream.Dispose(); } catch { }
            }
        }

        /// <summary>
        /// 初始化：重定向 Console + 延迟安装 REST Hook
        /// </summary>
        public static void Initialize(TerrariaPlugin plugin)
        {
            if (_initialized) return;
            _initialized = true;
            _plugin = plugin;

            _originalOut = Console.Out;
            _interceptor = new LogInterceptor(_originalOut);
            Console.SetOut(_interceptor);

            // 立即尝试安装 Hook
            if (TryInstallRestHook())
                return;

            // 如果 listener 未就绪，启动定时器重试（每秒一次，最多 30 秒）
            _retryTimer = new Timer(_ =>
            {
                if (_hooksInstalled || _retryCount >= 30)
                {
                    _retryTimer?.Dispose();
                    _retryTimer = null;
                    return;
                }
                _retryCount++;
                if (TryInstallRestHook())
                {
                    _retryTimer?.Dispose();
                    _retryTimer = null;
                }
            }, null, 1000, 1000);
        }

        private static Timer? _retryTimer;
        private static int _retryCount = 0;

        /// <summary>
        /// 尝试安装 REST Hook，成功返回 true
        /// </summary>
        private static bool TryInstallRestHook()
        {
            try
            {
                var listenerField = typeof(Rest).GetField("listener",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (listenerField == null) return false;

                _listener = listenerField.GetValue(TShock.RestApi);
                if (_listener == null) return false;

                InstallRestHook();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 安装 REST Hook — 必须在 RestApi.Start() 之后调用
        /// </summary>
        public static void InstallRestHook()
        {
            if (_hooksInstalled) return;
            _hooksInstalled = true;

            try
            {
                var restType = typeof(Rest);
                var listenerType = _listener!.GetType();

                _requestEvent = listenerType.GetEvent("RequestReceived");
                if (_requestEvent == null) return;

                var eventHandlerType = _requestEvent.EventHandlerType;
                var onRequestMethod = restType.GetMethod("OnRequest",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (onRequestMethod == null) return;

                // ═══ 清理所有 SSELogger 残留（跨程序集热重载） ═══
                RemoveAllSSELoggerHandlers();

                // 移除原来的 Rest.OnRequest handler（首次安装时有效）
                _originalOnRequest = Delegate.CreateDelegate(eventHandlerType, TShock.RestApi, onRequestMethod);
                _requestEvent.RemoveEventHandler(_listener, _originalOnRequest);

                // 注册我们的 handler
                var ourMethod = typeof(SSELogger).GetMethod("OnRequestInterceptor",
                    BindingFlags.Static | BindingFlags.NonPublic)!;
                _ourDelegate = Delegate.CreateDelegate(eventHandlerType, ourMethod);
                _requestEvent.AddEventHandler(_listener, _ourDelegate);
            }
            catch
            {
                // 静默失败
            }
        }

        /// <summary>
        /// 枚举 RequestReceived 事件的所有 handler，移除属于 SSELogger 的
        /// </summary>
        private static void RemoveAllSSELoggerHandlers()
        {
            if (_listener == null || _requestEvent == null) return;

            try
            {
                var field = _listener.GetType().GetField("RequestReceived",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    ?? _listener.GetType().GetField("_requestReceived",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    ?? _listener.GetType().GetField("RequestReceivedEvent",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                if (field == null) return;

                var delegateInstance = field.GetValue(_listener) as Delegate;
                if (delegateInstance == null) return;

                foreach (var handler in delegateInstance.GetInvocationList())
                {
                    if (handler.Method.DeclaringType?.Name == "SSELogger")
                    {
                        _requestEvent.RemoveEventHandler(_listener, handler);
                    }
                }
            }
            catch
            {
                // 反射失败不影响主流程
            }
        }

        /// <summary>
        /// 请求拦截 — 由 HttpListener 回调
        /// </summary>
        private static void OnRequestInterceptor(object sender, RequestEventArgs e)
        {
            var path = e.Request.Uri.AbsolutePath.TrimEnd('/');

            if (path == "/data/online/log/stream")
            {
                HandleSSEStream(e);
                return;
            }

            // 其他路由 → 原 OnRequest（含异常保护，防止异常穿透到 HttpServer）
            try
            {
                _originalOnRequest?.DynamicInvoke(sender, e);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[SSELogger] REST 请求处理异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理 SSE 连接
        /// 修复：剥离底层 Socket 脱离 HttpServer 管理，防止 HttpServer 连接循环误读 SSE 数据
        /// </summary>
        private static void HandleSSEStream(RequestEventArgs e)
        {
            try
            {
                var token = e.Request.Parameters["token"];
                if (string.IsNullOrEmpty(token) || !ValidateToken(token))
                {
                    e.Response.Status = HttpStatusCode.Unauthorized;
                    return;
                }

                var originalStream = e.Context.Stream;

                // 剥离 Socket：让 HttpServer 不再管理此连接
                Stream sseStream = TryDetachSocket(originalStream);

                // ═══ 手动发送 HTTP 响应头 ═══
                var headerBuilder = new StringBuilder();
                headerBuilder.Append("HTTP/1.1 200 OK\r\n");
                headerBuilder.Append("Content-Type: text/event-stream; charset=utf-8\r\n");
                headerBuilder.Append("Cache-Control: no-cache\r\n");
                headerBuilder.Append("Connection: keep-alive\r\n");
                headerBuilder.Append("Access-Control-Allow-Origin: *\r\n");
                headerBuilder.Append("\r\n");

                var headerBytes = Encoding.UTF8.GetBytes(headerBuilder.ToString());
                sseStream.Write(headerBytes, 0, headerBytes.Length);
                sseStream.Flush();

                e.IsHandled = true;

                var init = Encoding.UTF8.GetBytes("data: {\"connected\":true}\n\n");
                sseStream.Write(init, 0, init.Length);
                sseStream.Flush();

                var addr = e.Context.RemoteEndPoint?.ToString() ?? "unknown";
                var client = new SSEClient(sseStream, addr);

                lock (_clientLock)
                {
                    client.ReadIndex = _logHistory.Count;
                    _sseClients.Add(client);
                }

                _ = Task.Run(() => PushLoop(client));
            }
            catch
            {
                // 静默失败
            }
        }

        /// <summary>
        /// 从 HttpServer 的 IHttpContext.Stream 中剥离底层 Socket，
        /// 使 HttpServer 不再管理此 TCP 连接，避免其连接循环误读 SSE 推流数据。
        /// 
        /// 方法原理：
        /// 1. 获取 NetworkStream 内部的 Socket
        /// 2. 通过反射将原 NetworkStream 的内部 Socket 引用置 null
        /// 3. 关闭原 NetworkStream（此时不会关闭真正 Socket）
        /// 4. 用同一个 Socket 创建新的 NetworkStream 供 SSE 使用
        /// 5. 原流关闭后 HttpServer 的连接循环读 0 字节 → 结束循环 → 连接从 HttpServer 中注销
        /// 
        /// 剥离失败时降级使用原流（旧行为，可能有冲突风险）
        /// </summary>
        private static Stream TryDetachSocket(Stream originalStream)
        {
            if (originalStream is NetworkStream ns)
            {
                try
                {
                    // .NET 5+ 中 NetworkStream.Socket 是公开属性
                    var socket = ns.Socket;
                    if (socket == null)
                        return originalStream;

                    // 查找内部 Socket 字段名（不同 .NET 版本名称不同）
                    var socketField = typeof(NetworkStream).GetField("_streamSocket",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    socketField ??= typeof(NetworkStream).GetField("_socket",
                        BindingFlags.Instance | BindingFlags.NonPublic);

                    if (socketField == null || socketField.FieldType != typeof(Socket))
                        return originalStream;

                    // 将原 NetworkStream 的内部 Socket 引用置 null
                    // 这样 Close() 时不会关闭真正的 Socket
                    socketField.SetValue(originalStream, null);
                    originalStream.Close();

                    // 配置 TCP KeepAlive 以加速断连检测和僵尸连接回收
                    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                    socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 60);
                    socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 10);

                    // 使用同一个 Socket 创建新的独立 NetworkStream（拥有 Socket 所有权）
                    return new NetworkStream(socket, FileAccess.ReadWrite, ownsSocket: true);
                }
                catch
                {
                    // 反射失败，降级处理
                }
            }

            return originalStream;
        }

        private static async Task PushLoop(SSEClient client)
        {
            try
            {
                while (!client.CTS.IsCancellationRequested)
                {
                    List<string>? lines = null;

                    lock (_logLock)
                    {
                        int count = _logHistory.Count - client.ReadIndex;
                        if (count > 0)
                        {
                            lines = new List<string>(count);
                            for (int i = client.ReadIndex; i < _logHistory.Count; i++)
                                lines.Add(_logHistory[i]);
                            client.ReadIndex = _logHistory.Count;
                        }
                    }

                    if (lines != null && lines.Count > 0)
                    {
                        var json = JsonConvert.SerializeObject(lines);
                        var data = Encoding.UTF8.GetBytes($"data: {json}\n\n");
                        await client.Stream.WriteAsync(data, 0, data.Length, client.CTS.Token);
                        await client.Stream.FlushAsync(client.CTS.Token);
                    }
                    else
                    {
                        await Task.Delay(1000, client.CTS.Token);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch { }
            finally
            {
                lock (_clientLock) _sseClients.Remove(client);
                client.Dispose();
            }
        }

        /// <summary>
        /// 添加一行日志到环形缓冲区
        /// line 已经是 LogSegment[] 序列化的 JSON 字符串
        /// </summary>
        public static void AddLogLine(string line)
        {
            lock (_logLock)
            {
                _logHistory.Add(line);
                if (_logHistory.Count > MaxLogLines)
                    _logHistory.RemoveRange(0, _logHistory.Count - MaxLogLines);
            }
        }

        /// <summary>
        /// 验证 REST token
        /// </summary>
        private static bool ValidateToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return false;
            if (TShock.RestApi is not SecureRest secure) return false;

            if (secure.Tokens.TryGetValue(token, out var td) ||
                secure.AppTokens.TryGetValue(token, out td))
            {
                if (td.UserGroupName == "superadmin" || td.UserGroupName == "owner")
                    return true;

                var group = TShock.Groups.GetGroupByName(td.UserGroupName);
                return group != null && group.HasPermission("data.rest.invsee");
            }
            return false;
        }

        /// <summary>
        /// 清理
        /// </summary>
        public static void Dispose()
        {
            if (!_initialized) return;
            _initialized = false;
            _hooksInstalled = false;

            _retryTimer?.Dispose();
            _retryTimer = null;

            if (_originalOut != null)
                Console.SetOut(_originalOut);

            lock (_clientLock)
            {
                foreach (var s in _sseClients)
                    try { s.Dispose(); } catch { }
                _sseClients.Clear();
            }

            RemoveAllSSELoggerHandlers();
            _ourDelegate = null;
            _originalOnRequest = null;
            _requestEvent = null;
            _listener = null;
        }
    }
}
