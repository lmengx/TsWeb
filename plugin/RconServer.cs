using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TShockAPI;

namespace TShockData
{
    /// <summary>
    /// Source RCON 协议服务器
    /// 在独立端口运行，不依赖 TShock REST API HttpServer
    /// </summary>
    public static class RconServer
    {
        private static TcpListener? _listener;
        private static CancellationTokenSource? _cts;
        private static bool _running = false;

        // 已认证的连接
        private static readonly List<RconClient> _clients = new();
        private static readonly object _clientLock = new();

        /// <summary>最近的日志行（LogInterceptor 写入）</summary>
        private static readonly List<string> _logHistory = new();
        private static readonly object _logLock = new();
        private const int MaxLogLines = 1000;

        /// <summary>
        /// RCON 客户端连接
        /// </summary>
        private class RconClient : IDisposable
        {
            public TcpClient Tcp { get; }
            public NetworkStream Stream { get; }
            public string Address { get; }
            public bool Authenticated { get; set; }
            public int LogIndex { get; set; }
            public CancellationTokenSource CTS { get; }

            public RconClient(TcpClient tcp, string addr)
            {
                Tcp = tcp;
                Stream = tcp.GetStream();
                Address = addr;
                CTS = new CancellationTokenSource();
                LogIndex = 0;
            }

            public void Dispose()
            {
                CTS.Cancel();
                CTS.Dispose();
                try { Stream.Dispose(); } catch { }
                try { Tcp.Close(); } catch { }
            }
        }

        /// <summary>启动 RCON 服务器</summary>
        public static void Start(int port)
        {
            if (_running) return;

            try
            {
                _cts = new CancellationTokenSource();
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Start();
                _running = true;

                TShock.Log.ConsoleInfo($"[TSWeb] RCON 服务器已启动，监听端口: {port}");

                _ = Task.Run(() => AcceptLoop(_cts.Token));
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] RCON 服务器启动失败: {ex.Message}");
                _running = false;
            }
        }

        /// <summary>停止 RCON 服务器</summary>
        public static void Stop()
        {
            if (!_running) return;
            _running = false;

            _cts?.Cancel();

            lock (_clientLock)
            {
                foreach (var c in _clients)
                    try { c.Dispose(); } catch { }
                _clients.Clear();
            }

            try { _listener?.Stop(); } catch { }

            TShock.Log.ConsoleInfo("[TSWeb] RCON 服务器已停止");
        }

        /// <summary>添加一行日志（由 LogInterceptor 调用）</summary>
        public static void AddLogLine(string line)
        {
            lock (_logLock)
            {
                _logHistory.Add(line);
                if (_logHistory.Count > MaxLogLines)
                    _logHistory.RemoveRange(0, _logHistory.Count - MaxLogLines);
            }
        }

        /// <summary>获取是否正在运行</summary>
        public static bool IsRunning => _running;

        private static async Task AcceptLoop(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var tcp = await _listener!.AcceptTcpClientAsync();
                    var addr = tcp.Client.RemoteEndPoint?.ToString() ?? "unknown";
                    TShock.Log.ConsoleInfo($"[TSWeb] RCON 客户端连接: {addr}");
                    _ = Task.Run(() => HandleClientAsync(tcp, addr, ct), ct);
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] RCON AcceptLoop 异常: {ex.Message}");
            }
        }

        private static async Task HandleClientAsync(TcpClient tcp, string addr, CancellationToken ct)
        {
            var client = new RconClient(tcp, addr);

            try
            {
                using var ms = new MemoryStream();
                var buffer = new byte[4096];

                while (!ct.IsCancellationRequested)
                {
                    // 读取 4 字节长度头
                    int read = await client.Stream.ReadAsync(buffer, 0, 4, ct);
                    if (read < 4) break;

                    int packetLen = BitConverter.ToInt32(buffer, 0);
                    if (packetLen < 8 || packetLen > 4096) break;   // 最小 RequestID(4)+Type(4)

                    // 读取剩余数据包
                    int totalRead = 0;
                    while (totalRead < packetLen)
                    {
                        read = await client.Stream.ReadAsync(buffer, totalRead, Math.Min(packetLen - totalRead, buffer.Length - totalRead), ct);
                        if (read == 0) break;
                        totalRead += read;
                    }
                    if (totalRead < packetLen) break;

                    int requestId = BitConverter.ToInt32(buffer, 0);
                    int type = BitConverter.ToInt32(buffer, 4);
                    int payloadLen = packetLen - 8 - 2;  // 去掉 RequestID+Type+Padding(2)
                    if (payloadLen < 0) break;

                    string payload = Encoding.UTF8.GetString(buffer, 8, payloadLen).TrimEnd('\0');

                    if (type == 3)  // SERVERDATA_AUTH
                    {
                        HandleAuth(client, requestId, payload);
                        // 认证成功后才开始推送日志
                        if (client.Authenticated)
                        {
                            lock (_clientLock) _clients.Add(client);
                            _ = Task.Run(() => PushLogLoop(client, ct), ct);
                        }
                    }
                    else if (type == 2 && client.Authenticated)  // SERVERDATA_EXECCOMMAND
                    {
                        HandleCommand(client, requestId, payload);
                    }
                    else if (!client.Authenticated)
                    {
                        // 未认证，返回拒绝
                        SendPacket(client.Stream, requestId, 2, "");
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch { }
            finally
            {
                lock (_clientLock) _clients.Remove(client);
                client.Dispose();
                TShock.Log.ConsoleInfo($"[TSWeb] RCON 客户端断开: {addr}");
            }
        }

        private static void HandleAuth(RconClient client, int requestId, string password)
        {
            // 直接用 TShock REST API Key 验证
            if (ValidateRestToken(password))
            {
                client.Authenticated = true;
                SendPacket(client.Stream, requestId, 2, "");
                TShock.Log.ConsoleInfo($"[TSWeb] RCON 客户端认证成功: {client.Address}");
            }
            else
            {
                SendPacket(client.Stream, -1, 2, "");
                TShock.Log.ConsoleWarn($"[TSWeb] RCON 客户端认证失败: {client.Address}");
            }
        }

        /// <summary>验证 REST API Key（复用 TShock 的 ApplicationRestTokens）</summary>
        private static bool ValidateRestToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return false;
            if (TShock.RestApi is not Rests.SecureRest secure) return false;

            if (secure.Tokens.TryGetValue(token, out var td) ||
                secure.AppTokens.TryGetValue(token, out td))
            {
                return td.UserGroupName == "superadmin" || td.UserGroupName == "owner";
            }
            return false;
        }

        private static void HandleCommand(RconClient client, int requestId, string cmd)
        {
            try
            {
                // 将命令写入 Console（LogInterceptor 捕获）
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.Write("[" + DateTime.Now.ToString("HH:mm:ss") + "] ");
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(client.Address);
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.Write(" RCON > ");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(cmd);
                Console.ResetColor();

                var group = TShock.Groups.GetGroupByName("superadmin");
                if (group == null)
                {
                    SendPacket(client.Stream, requestId, 0, "错误: 未找到 superadmin 组");
                    return;
                }

                var tr = new TSRestPlayer("RCON-" + client.Address, group);
                Commands.HandleCommand(tr, cmd);
                var output = string.Join("\n", tr.GetCommandOutput() ?? new List<string>());

                // 命令输出写入 Console
                if (!string.IsNullOrEmpty(output))
                {
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine(output);
                    Console.ResetColor();
                }

                SendPacket(client.Stream, requestId, 0, output);
            }
            catch (Exception ex)
            {
                SendPacket(client.Stream, requestId, 0, $"错误: {ex.Message}");
            }
        }

        /// <summary>向已认证客户端推送新日志</summary>
        private static async Task PushLogLoop(RconClient client, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && client.Authenticated)
                {
                    List<string>? lines = null;

                    lock (_logLock)
                    {
                        int count = _logHistory.Count - client.LogIndex;
                        if (count > 0)
                        {
                            lines = new List<string>(count);
                            for (int i = client.LogIndex; i < _logHistory.Count; i++)
                                lines.Add(_logHistory[i]);
                            client.LogIndex = _logHistory.Count;
                        }
                    }

                    if (lines != null && lines.Count > 0)
                    {
                        // 用 Type=0 (SERVERDATA_RESPONSE_VALUE) 推送日志行
                        // 每个日志行单独发送，RequestID=0 表示服务端主动推送
                        foreach (var line in lines)
                        {
                            SendPacket(client.Stream, 0, 0, line);
                        }
                    }

                    await Task.Delay(500, ct);
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        }

        /// <summary>发送 RCON 数据包</summary>
        private static void SendPacket(NetworkStream stream, int requestId, int type, string payload)
        {
            try
            {
                var payloadBytes = Encoding.UTF8.GetBytes(payload);
                // Length = RequestID(4) + Type(4) + payload + padding(2)
                int length = 8 + payloadBytes.Length + 2;
                var packet = new byte[4 + length];  // Length 本身不算在 length 内

                BitConverter.GetBytes(length).CopyTo(packet, 0);
                BitConverter.GetBytes(requestId).CopyTo(packet, 4);
                BitConverter.GetBytes(type).CopyTo(packet, 8);
                payloadBytes.CopyTo(packet, 12);
                // 末尾 2 字节 padding 默认是 0x00

                stream.Write(packet, 0, packet.Length);
                stream.Flush();
            }
            catch { }
        }
    }
}
