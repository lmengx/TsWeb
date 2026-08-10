using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rests;
using TShockAPI;

namespace TShockData
{
    /// <summary>
    /// 现代 REST 监听服务（接管 TShock REST 端口）：
    ///  - 普通请求 → 透传 TShockRestBridge 到原 REST 处理
    ///  - /tsweb/stream → SSE 长连接（日志 / ping / 定向文件推送）
    ///  - /tsweb/file    → 请求定向文件推送（通过 SSE 长连接下发 file.* 事件）
    /// 使用 TcpListener + 手写 HTTP/1.1，避免 HttpListener 的 urlacl 权限依赖，
    /// 并完全掌控 SSE 长连接的保活与断连检测。
    /// </summary>
    public static class WebRestServer
    {
        public const string SsePath = "/tsweb/stream";
        public const string FilePushPath = "/tsweb/file";
        private const int FileChunkSize = 32768; // 每段字节数（base64 后 ~44KB）
        private const long MaxFileBytes = 200L * 1024 * 1024; // 200MB 上限

        private static TcpListener? _listener;
        private static CancellationTokenSource? _cts;
        private static Timer? _heartbeatTimer;

        private static readonly object _clientsLock = new();
        private static readonly List<SseClient> _clients = new();

        public static bool Running { get; private set; }

        /// <summary>启动监听（需先 TShock.RestApi.Stop() 释放旧端口）</summary>
        public static void Start(int port)
        {
            Stop();

            _listener = new TcpListener(IPAddress.Any, port)
            {
                ExclusiveAddressUse = true
            };
            _listener.Start(512);
            _cts = new CancellationTokenSource();
            Running = true;

            _heartbeatTimer = new Timer(_ => KeepAliveAll(), null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
            _ = AcceptLoopAsync(_cts.Token);

            TShock.Log.ConsoleInfo($"[TSWeb] 现代 REST 监听已接管端口 {port}");
        }

        public static void Stop()
        {
            Running = false;
            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            lock (_clientsLock)
            {
                foreach (var c in _clients) c.Close();
                _clients.Clear();
            }
            _listener = null;
        }

        // ═══════════════ 连接循环 ═══════════════

        private static async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener!.AcceptTcpClientAsync(ct);
                }
                catch (OperationCanceledException) { break; }
                catch { continue; }
                _ = HandleClientAsync(client, ct);
            }
        }

        private static async Task HandleClientAsync(TcpClient tcp, CancellationToken ct)
        {
            using var _ = tcp;
            tcp.NoDelay = true;
            using var stream = tcp.GetStream();
            try
            {
                // ── 请求行 + 请求头 ──
                var requestLine = await ReadLineAsync(stream, ct);
                if (string.IsNullOrWhiteSpace(requestLine)) return;
                var parts = requestLine.Split(' ');
                if (parts.Length < 3) return;
                var method = parts[0].ToUpperInvariant();
                var target = parts[1]; // /path?query

                var qIdx = target.IndexOf('?');
                var path = qIdx >= 0 ? target.Substring(0, qIdx) : target;
                var rawQuery = qIdx >= 0 ? target.Substring(qIdx + 1) : "";

                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                while (true)
                {
                    var line = await ReadLineAsync(stream, ct);
                    if (line.Length == 0) break; // 空行 = 头结束
                    var cIdx = line.IndexOf(':');
                    if (cIdx > 0)
                        headers[line.Substring(0, cIdx).Trim()] = line.Substring(cIdx + 1).Trim();
                }

                // ── 请求体（Content-Length） ──
                var body = "";
                if (headers.TryGetValue("Content-Length", out var lenStr)
                    && int.TryParse(lenStr, out var len) && len > 0 && len <= 10 * 1024 * 1024)
                {
                    body = await ReadBodyAsync(stream, len, ct);
                }

                // ── 路由：SSE / 文件推送 / 透传 ──
                var route = path.TrimEnd('/');
                if (route.Equals(SsePath, StringComparison.OrdinalIgnoreCase))
                {
                    await HandleSseAsync(tcp, stream, rawQuery, ct);
                }
                else if (route.Equals(FilePushPath, StringComparison.OrdinalIgnoreCase))
                {
                    await HandleFilePushAsync(stream, rawQuery, ct);
                }
                else
                {
                    var form = ParseForm(body);
                    await HandleRestAsync(tcp, stream, method, path, rawQuery, form, ct);
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 请求处理异常: {ex.Message}");
            }
        }

        // ═══════════════ 普通请求透传 ═══════════════

        private static async Task HandleRestAsync(TcpClient tcp, NetworkStream stream, string method, string path,
            string rawQuery, Dictionary<string, string>? form, CancellationToken ct)
        {
            var remote = tcp.Client.RemoteEndPoint as IPEndPoint;
            var result = TShockRestBridge.Process(path, rawQuery, form, remote, method);
            await WriteResponseAsync(stream, (int)result.Status, StatusText(result.Status),
                result.ContentType, Encoding.UTF8.GetBytes(result.Body), ct);
        }

        // ═══════════════ SSE ═══════════════

        private static async Task HandleSseAsync(TcpClient tcp, NetworkStream stream, string rawQuery, CancellationToken ct)
        {
            var token = ParseQueryToken(rawQuery);
            if (!IsValidToken(token))
            {
                await WriteResponseAsync(stream, 401, "Unauthorized", "application/json; charset=utf-8",
                    Encoding.UTF8.GetBytes("{\"status\":\"401\",\"error\":\"Not authorized. The specified API endpoint requires a token.\"}"), ct);
                return;
            }

            var handshake = "HTTP/1.1 200 OK\r\n"
                          + "Content-Type: text/event-stream\r\n"
                          + "Cache-Control: no-cache\r\n"
                          + "Connection: keep-alive\r\n"
                          + "X-Accel-Buffering: no\r\n"
                          + "Server: TSWeb\r\n\r\n";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(handshake), ct);
            await stream.FlushAsync(ct);

            var client = new SseClient(stream);
            lock (_clientsLock) _clients.Add(client);
            try
            {
                var connected = JsonConvert.SerializeObject(new
                {
                    connected = true,
                    clientId = client.ClientId,
                    time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
                await client.SendAsync($"event: connected\ndata: {connected}\n\n", ct);
                // 读循环：客户端断开时 Read 返回 0 或抛异常
                var buf = new byte[1];
                while (!ct.IsCancellationRequested)
                {
                    int n;
                    try { n = await stream.ReadAsync(buf, 0, 1, ct); }
                    catch (OperationCanceledException) { break; }
                    catch (IOException) { break; }
                    if (n == 0) break;
                }
            }
            finally
            {
                lock (_clientsLock) _clients.Remove(client);
                client.Close();
            }
        }

        // ═══════════════ 文件定向推送 ═══════════════

        /// <summary>
        /// GET /tsweb/file?token=&clientId=&path=
        /// 鉴权后把 TShock.SavePath 相对路径的文件通过目标 SSE 连接定向推送 file.* 事件。
        /// </summary>
        private static async Task HandleFilePushAsync(NetworkStream stream, string rawQuery, CancellationToken ct)
        {
            var q = ParseQuery(rawQuery);
            var token = q.TryGetValue("token", out var t) ? t : null;
            if (!IsValidToken(token))
            {
                await WriteErrorJson(stream, 401, "Not authorized.", ct);
                return;
            }

            var clientId = q.TryGetValue("clientId", out var cid) ? cid : "";
            var pathRaw = q.TryGetValue("path", out var p) ? p : "";
            // root=app → TShock 程序目录（文件管理页）；root=tshock/缺省 → TShock.SavePath（兼容原资源拉取）
            var rootMode = q.TryGetValue("root", out var r) ? r : "tshock";
            // tag：调用方自定义标识，随 file.* 事件原样回传，用于后端关联下载会话（默认空）
            var tag = q.TryGetValue("tag", out var tg) ? tg : "";
            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(pathRaw))
            {
                await WriteErrorJson(stream, 400, "Missing clientId or path", ct);
                return;
            }

            var path = Uri.UnescapeDataString(pathRaw);

            // ── 防目录穿越：只允许根目录内 ──
            string root;
            string full;
            try
            {
                root = rootMode.Equals("app", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory)
                    : Path.GetFullPath(TShock.SavePath);
                full = Path.GetFullPath(Path.Combine(root, path.Replace('\\', '/')));
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    await WriteErrorJson(stream, 403, "Path is outside allowed root", ct);
                    return;
                }
            }
            catch
            {
                await WriteErrorJson(stream, 400, "Invalid path", ct);
                return;
            }

            if (!File.Exists(full))
            {
                await WriteErrorJson(stream, 404, "File not found", ct);
                return;
            }

            var info = new FileInfo(full);
            if (info.Length > MaxFileBytes)
            {
                await WriteErrorJson(stream, 413, "File too large", ct);
                return;
            }

            // ── 读取 + 分块 + 定向推送 ──
            var bytes = await File.ReadAllBytesAsync(full, ct);
            var tid = "f-" + Guid.NewGuid().ToString("N").Substring(0, 10);
            var chunks = (int)((bytes.Length + FileChunkSize - 1) / FileChunkSize);
            var shaHex = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var rel = path.Replace('\\', '/').TrimStart('/');

            var begin = new JObject
            {
                ["id"] = tid,
                ["name"] = rel,
                ["size"] = bytes.Length,
                ["chunks"] = chunks,
                ["chunkSize"] = FileChunkSize,
                ["mime"] = "application/octet-stream"
            };
            if (tag.Length > 0) begin["tag"] = tag;
            if (!SendToClient(clientId, "file.begin", begin.ToString(Formatting.None)))
            {
                await WriteErrorJson(stream, 404, "SSE client not found", ct);
                return;
            }

            for (var i = 0; i < chunks; i++)
            {
                var off = i * FileChunkSize;
                var len = Math.Min(FileChunkSize, bytes.Length - off);
                var chunk = new JObject
                {
                    ["id"] = tid,
                    ["n"] = i,
                    ["d"] = Convert.ToBase64String(bytes, off, len)
                };
                if (tag.Length > 0) chunk["tag"] = tag;
                if (!SendToClient(clientId, "file.chunk", chunk.ToString(Formatting.None)))
                {
                    await WriteErrorJson(stream, 404, "SSE client disconnected", ct);
                    return;
                }
            }

            var end = new JObject
            {
                ["id"] = tid,
                ["size"] = bytes.Length,
                ["sha256"] = shaHex
            };
            if (tag.Length > 0) end["tag"] = tag;
            SendToClient(clientId, "file.end", end.ToString(Formatting.None));

            var ok = JsonConvert.SerializeObject(new { status = "200", id = tid, chunks, size = bytes.Length });
            await WriteResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(ok), ct);
        }

        private static async Task WriteErrorJson(NetworkStream stream, int status, string text, CancellationToken ct)
        {
            var body = JsonConvert.SerializeObject(new { status = status.ToString(), error = text });
            await WriteResponseAsync(stream, status, StatusText((HttpStatusCode)status), "application/json; charset=utf-8",
                Encoding.UTF8.GetBytes(body), ct);
        }

        /// <summary>向所有 SSE 连接广播一条事件</summary>
        public static void Broadcast(string eventName, string jsonData)
        {
            lock (_clientsLock)
            {
                foreach (var c in _clients)
                {
                    _ = SendSafeAsync(c, eventName, jsonData);
                }
            }
        }

        /// <summary>
        /// 向指定 clientId 的 SSE 连接定向推送一条事件
        /// </summary>
        /// <returns>true=已投递到目标连接（不保证发送成功）；false=目标连接不存在</returns>
        public static bool SendToClient(string clientId, string eventName, string jsonData)
        {
            if (string.IsNullOrEmpty(clientId)) return false;
            SseClient? target = null;
            lock (_clientsLock)
            {
                foreach (var c in _clients)
                {
                    if (c.ClientId.Equals(clientId, StringComparison.Ordinal))
                    {
                        target = c;
                        break;
                    }
                }
            }
            if (target == null) return false;
            _ = SendSafeAsync(target, eventName, jsonData);
            return true;
        }

        private static async Task SendSafeAsync(SseClient c, string eventName, string jsonData)
        {
            try
            {
                var payload = $"event: {eventName}\ndata: {jsonData}\n\n";
                await c.SendAsync(payload, CancellationToken.None);
            }
            catch
            {
                // 断连：尝试移除
                lock (_clientsLock) { _clients.Remove(c); }
                c.Close();
            }
        }

        private static void KeepAliveAll()
        {
            lock (_clientsLock)
            {
                foreach (var c in _clients)
                    _ = SendSafeAsync(c, "ping", "{}");
            }
        }

        // ═══════════════ token 鉴权（复用 TShock SecureRest 字典） ═══════════════

        private static Dictionary<string, string> ParseQuery(string rawQuery)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(rawQuery)) return dict;
            foreach (var pair in rawQuery.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var idx = pair.IndexOf('=');
                if (idx < 0) dict[Uri.UnescapeDataString(pair)] = "";
                else dict[Uri.UnescapeDataString(pair.Substring(0, idx))] = pair.Substring(idx + 1);
            }
            return dict;
        }

        private static string? ParseQueryToken(string rawQuery)
        {
            if (string.IsNullOrEmpty(rawQuery)) return null;
            foreach (var pair in rawQuery.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var idx = pair.IndexOf('=');
                var key = idx >= 0 ? Uri.UnescapeDataString(pair.Substring(0, idx)) : Uri.UnescapeDataString(pair);
                if (key.Equals("token", StringComparison.OrdinalIgnoreCase))
                    return idx >= 0 ? pair.Substring(idx + 1) : "";
            }
            return null;
        }

        private static bool IsValidToken(string? token)
        {
            if (string.IsNullOrEmpty(token)) return false;
            foreach (var dict in GetTokenDicts())
                if (dict.Contains(token)) return true;
            return false;
        }

        private static IEnumerable<IDictionary> GetTokenDicts()
        {
            var type = typeof(SecureRest);
            foreach (var propName in new[] { "Tokens", "AppTokens" })
            {
                // SecureRest.Tokens/AppTokens 是属性（get; protected set;），需用 GetProperty
                var p = type.GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p?.GetValue(TShock.RestApi) is IDictionary d)
                    yield return d;
            }
        }

        // ═══════════════ HTTP 解析工具 ═══════════════

        private static Dictionary<string, string>? ParseForm(string body)
        {
            if (string.IsNullOrEmpty(body)) return null;
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var idx = pair.IndexOf('=');
                if (idx < 0) dict[Uri.UnescapeDataString(pair)] = "";
                else dict[Uri.UnescapeDataString(pair.Substring(0, idx))] = pair.Substring(idx + 1);
            }
            return dict;
        }

        private static async Task<string> ReadLineAsync(Stream s, CancellationToken ct)
        {
            var sb = new StringBuilder(64);
            var buf = new byte[1];
            while (true)
            {
                int n = await s.ReadAsync(buf, 0, 1, ct);
                if (n == 0) throw new IOException("连接已关闭");
                if (buf[0] == (byte)'\n') break;
                if (buf[0] != (byte)'\r') sb.Append((char)buf[0]);
                if (sb.Length > 65536) throw new IOException("请求行过长");
            }
            return sb.ToString();
        }

        private static async Task<string> ReadBodyAsync(Stream s, int length, CancellationToken ct)
        {
            var bytes = new byte[length];
            int read = 0;
            while (read < length)
            {
                int n = await s.ReadAsync(bytes, read, length - read, ct);
                if (n == 0) throw new IOException("连接已关闭");
                read += n;
            }
            return Encoding.UTF8.GetString(bytes);
        }

        private static async Task WriteResponseAsync(NetworkStream s, int status, string statusText,
            string contentType, byte[] body, CancellationToken ct)
        {
            var head = $"HTTP/1.1 {status} {statusText}\r\n"
                     + $"Content-Type: {contentType}\r\n"
                     + $"Content-Length: {body.Length}\r\n"
                     + "Server: TSWeb\r\n"
                     + "Connection: close\r\n\r\n";
            var headBytes = Encoding.UTF8.GetBytes(head);
            await s.WriteAsync(headBytes, ct);
            if (body.Length > 0) await s.WriteAsync(body, ct);
            await s.FlushAsync(ct);
        }

        private static string StatusText(HttpStatusCode code) => code switch
        {
            HttpStatusCode.OK => "OK",
            HttpStatusCode.BadRequest => "Bad Request",
            HttpStatusCode.Unauthorized => "Unauthorized",
            HttpStatusCode.Forbidden => "Forbidden",
            HttpStatusCode.NotFound => "Not Found",
            HttpStatusCode.InternalServerError => "Internal Server Error",
            _ => code.ToString()
        };
    }

    /// <summary>一个 SSE 长连接客户端</summary>
    internal sealed class SseClient
    {
        /// <summary>连接唯一标识（connected 事件下发给客户端，用于定向文件推送）</summary>
        public string ClientId { get; }

        private readonly NetworkStream _stream;
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        public SseClient(NetworkStream stream)
        {
            _stream = stream;
            ClientId = Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        public async Task SendAsync(string payload, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(payload);
            await _writeLock.WaitAsync(ct);
            try
            {
                await _stream.WriteAsync(bytes, ct);
                await _stream.FlushAsync(ct);
            }
            finally { _writeLock.Release(); }
        }

        public void Close()
        {
            try { _stream.Close(); } catch { }
        }
    }
}
