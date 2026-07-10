using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
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
    /// 拦截 Console 输出，同时写入原始输出流和内存缓冲区
    /// 重写所有 Write/WriteLine 重载以确保不遗漏任何输出
    /// </summary>
    public class LogInterceptor : TextWriter
    {
        private readonly TextWriter _original;
        private readonly StringBuilder _lineBuffer = new();

        public LogInterceptor(TextWriter original)
        {
            _original = original;
        }

        public override Encoding Encoding => _original.Encoding;
        public override IFormatProvider FormatProvider => _original.FormatProvider;
        public override string NewLine => _original.NewLine;

        // ──────────── Write(char) ────────────
        public override void Write(char value)
        {
            _original.Write(value);
            if (value == '\n')
                FlushLineBuffer();
            else if (value != '\r')
                _lineBuffer.Append(value);
        }

        // ──────────── Write(char[]) ────────────
        public override void Write(char[]? buffer)
        {
            if (buffer == null) return;
            _original.Write(buffer);
            AppendChars(buffer, 0, buffer.Length);
        }

        // ──────────── Write(char[], int, int) ────────────
        public override void Write(char[]? buffer, int index, int count)
        {
            if (buffer == null) return;
            _original.Write(buffer, index, count);
            AppendChars(buffer, index, count);
        }

        // ──────────── Write(string) ────────────
        public override void Write(string? value)
        {
            if (value == null) return;
            _original.Write(value);
            AppendString(value);
        }

        // ──────────── Write(StringBuilder) ────────────
        public override void Write(StringBuilder? value)
        {
            if (value == null) return;
            _original.Write(value);
            for (int i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c == '\n')
                    FlushLineBuffer();
                else if (c != '\r')
                    _lineBuffer.Append(c);
            }
        }

        // ──────────── WriteLine() ────────────
        public override void WriteLine()
        {
            _original.WriteLine();
            FlushLineBuffer();
        }

        // ──────────── WriteLine(char[]) ────────────
        public override void WriteLine(char[]? buffer)
        {
            if (buffer == null) { WriteLine(); return; }
            _original.WriteLine(buffer);
            AppendChars(buffer, 0, buffer.Length);
            FlushLineBuffer();
        }

        // ──────────── WriteLine(char[], int, int) ────────────
        public override void WriteLine(char[]? buffer, int index, int count)
        {
            if (buffer == null) { WriteLine(); return; }
            _original.WriteLine(buffer, index, count);
            AppendChars(buffer, index, count);
            FlushLineBuffer();
        }

        // ──────────── WriteLine(string) ────────────
        public override void WriteLine(string? value)
        {
            _original.WriteLine(value);
            if (value != null)
                AppendString(value);
            FlushLineBuffer();
        }

        // ──────────── WriteLine(StringBuilder) ────────────
        public override void WriteLine(StringBuilder? value)
        {
            if (value == null) { WriteLine(); return; }
            _original.WriteLine(value);
            for (int i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c != '\r')
                    _lineBuffer.Append(c);
            }
            FlushLineBuffer();
        }

        // ──────────── 辅助方法 ────────────

        private void AppendChars(char[] buffer, int index, int count)
        {
            for (int i = index; i < index + count; i++)
            {
                var c = buffer[i];
                if (c == '\n')
                    FlushLineBuffer();
                else if (c != '\r')
                    _lineBuffer.Append(c);
            }
        }

        private void AppendString(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c == '\n')
                    FlushLineBuffer();
                else if (c != '\r')
                    _lineBuffer.Append(c);
            }
        }

        private void FlushLineBuffer()
        {
            if (_lineBuffer.Length > 0)
            {
                var line = _lineBuffer.ToString();
                _lineBuffer.Clear();

                // 写入共享环形缓冲区
                SSELogger.AddLogLine(line);
            }
        }
    }

    /// <summary>
    /// SSE 日志推送管理
    /// </summary>
    public static class SSELogger
    {
        // 日志环形缓冲区（所有客户端共享读取，各自跟踪位置）
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
        /// 这样即使旧程序集的拦截器残留（HotReload），也能精确清除
        /// </summary>
        private static void RemoveAllSSELoggerHandlers()
        {
            if (_listener == null || _requestEvent == null) return;

            try
            {
                // 尝试多种可能的编译器生成的后台字段名
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
                    // 检查 handler 是否属于 SSELogger（任何程序集）
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

            // 其他路由 → 原 OnRequest
            _originalOnRequest?.DynamicInvoke(sender, e);
        }

        /// <summary>
        /// 处理 SSE 连接
        /// </summary>
        private static void HandleSSEStream(RequestEventArgs e)
        {
            try
            {
                // 鉴权 — 从 URL 参数取 token
                var token = e.Request.Parameters["token"];
                if (string.IsNullOrEmpty(token) || !ValidateToken(token))
                {
                    e.Response.Status = HttpStatusCode.Unauthorized;
                    return;
                }

                var stream = e.Context.Stream;

                // ═══ 手动发送 HTTP 响应头 ═══
                var headerBuilder = new StringBuilder();
                headerBuilder.Append("HTTP/1.1 200 OK\r\n");
                headerBuilder.Append("Content-Type: text/event-stream; charset=utf-8\r\n");
                headerBuilder.Append("Cache-Control: no-cache\r\n");
                headerBuilder.Append("Connection: keep-alive\r\n");
                headerBuilder.Append("Access-Control-Allow-Origin: *\r\n");
                headerBuilder.Append("\r\n");

                var headerBytes = Encoding.UTF8.GetBytes(headerBuilder.ToString());
                stream.Write(headerBytes, 0, headerBytes.Length);
                stream.Flush();

                // 标记已处理，阻止 HttpServer 发送任何响应
                e.IsHandled = true;

                // 发送初始 SSE 数据
                var init = Encoding.UTF8.GetBytes("data: {\"connected\":true}\n\n");
                stream.Write(init, 0, init.Length);
                stream.Flush();

                // 创建客户端对象，记录当前日志位置
                var addr = e.Context.RemoteEndPoint?.ToString() ?? "unknown";
                var client = new SSEClient(stream, addr);

                lock (_clientLock)
                {
                    client.ReadIndex = _logHistory.Count; // 从当前位置开始
                    _sseClients.Add(client);
                }

                _ = Task.Run(() => PushLoop(client));
            }
            catch
            {
                // 静默失败
            }
        }

        private static async Task PushLoop(SSEClient client)
        {
            try
            {
                while (!client.CTS.IsCancellationRequested)
                {
                    List<string>? lines = null;

                    // 从环形缓冲区读取新行
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
                        var json = Newtonsoft.Json.JsonConvert.SerializeObject(lines);
                        var data = Encoding.UTF8.GetBytes($"data: {json}\n\n");
                        await client.Stream.WriteAsync(data, 0, data.Length, client.CTS.Token);
                        await client.Stream.FlushAsync(client.CTS.Token);
                    }
                    else
                    {
                        // 无新日志时延长休眠，减少 CPU 空转
                        await Task.Delay(1000, client.CTS.Token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 主动取消，正常
            }
            catch
            {
                // 客户端断开
            }
            finally
            {
                lock (_clientLock) _sseClients.Remove(client);
                client.Dispose();
            }
        }

        /// <summary>
        /// 添加一行日志到环形缓冲区，所有连接的客户端都能读取到
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
        /// 验证 REST token — 要求 superadmin 或拥有 data.rest.invsee 权限
        /// </summary>
        private static bool ValidateToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return false;
            if (TShock.RestApi is not SecureRest secure) return false;

            if (secure.Tokens.TryGetValue(token, out var td) ||
                secure.AppTokens.TryGetValue(token, out td))
            {
                var group = TShock.Groups.GetGroupByName(td.UserGroupName);
                return group != null && (group.Name == "superadmin" || group.HasPermission("data.rest.invsee"));
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

            // 停止定时器
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

            // 移除所有 SSELogger 残留的拦截器
            RemoveAllSSELoggerHandlers();
            _ourDelegate = null;
            _originalOnRequest = null;
            _requestEvent = null;
            _listener = null;
        }
    }
}
