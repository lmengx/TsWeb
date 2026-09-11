using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using Terraria;
using Terraria.Localization;
using Terraria.Net;
using Terraria.Net.Sockets;

namespace PortRouter
{
    /// <summary>
    /// 协议感知监听器（替代 TShock LinuxTcpSocket / 原版 TcpSocket）。
    ///
    /// 通过 OTAPI 官方扩展点 OTAPI.Hooks.Netplay.CreateTcpListener 安装
    /// （插件 Order=1000 保证在 TShock 之后订阅 → 替换生效，与开源插件
    /// ProxyProtocolSocket / yaaiomni 同款机制）。
    ///
    /// ListenLoop（accept 循环内判定，早于任何日志/进槽）：
    ///   - HTTP（前 2 字节为 ASCII 字母）→ 静默字节泵转发到 REST 端口，
    ///     不打印「xxx正在连接」、不占游戏槽位；
    ///   - 游戏协议（[2字节长度][MessageID 0x01]）→ 正常打印「正在连接」
    ///     并交给原版 Netplay.OnConnectionAccepted 进槽 —— 客户端直连同一 TCP 连接、
    ///     无转发，RemoteEndPoint 即真实来源 IP。
    ///
    /// 模型：yaaiomni SelfSocket / TShock LinuxTcpSocket（生产实证），仅 ListenLoop
    /// 增加协议嗅探。I/O 方法与 LinuxTcpSocket 一致（LegacyNetBufferPool + BeginRead/Write）。
    /// </summary>
    public class HttpAwareSocket : ISocket
    {
        public byte[] _packetBuffer = new byte[1024];
        public List<object> _callbackBuffer = new();
        public int _messagesInQueue;
        public TcpClient _connection = null!;
        public TcpListener? _listener;
        public SocketConnectionAccepted? _listenerCallback;
        public RemoteAddress? _remoteAddress;
        public volatile bool _isListening;

        public int MessagesInQueue => _messagesInQueue;

        public HttpAwareSocket()
        {
            _connection = new TcpClient { NoDelay = true };
        }

        public HttpAwareSocket(TcpClient tcpClient)
        {
            _connection = tcpClient;
            _connection.NoDelay = true;
            var ep = (IPEndPoint)tcpClient.Client.RemoteEndPoint!;
            _remoteAddress = new TcpAddress(ep.Address, ep.Port);
        }

        // ═══════════════════════════════════════════
        // 监听
        // ═══════════════════════════════════════════

        bool ISocket.StartListening(SocketConnectionAccepted callback)
        {
            var any = IPAddress.Any;
            try
            {
                if (Program.LaunchParameters.TryGetValue("-ip", out var ipString)
                    && !IPAddress.TryParse(ipString, out any))
                    any = IPAddress.Any;
            }
            catch
            {
                any = IPAddress.Any;
            }

            _isListening = true;
            _listenerCallback = callback;
            if (_listener == null)
                _listener = new TcpListener(any, Netplay.ListenPort);
            try
            {
                _listener.Start();
            }
            catch
            {
                return false;
            }
            ThreadPool.QueueUserWorkItem(ListenLoop);
            return true;
        }

        void ISocket.StopListening()
        {
            _isListening = false;
        }

        private void ListenLoop(object? unused)
        {
            while (_isListening && !IsNetplayDisconnect())
            {
                try
                {
                    var tcp = _listener!.AcceptTcpClient();

                    // HTTP → 静默转发 REST（不打印「正在连接」、不占游戏槽位）
                    if (PortRouterCore.TryHandleHttp(tcp))
                        continue;

                    // 游戏流量 → 打印「正在连接」+ 原版进槽（客户端直连，原 IP 保留）
                    var sock = new HttpAwareSocket(tcp);
                    Console.WriteLine(Language.GetTextValue("Net.ClientConnecting", ((ISocket)sock).GetRemoteAddress()));
                    _listenerCallback!(sock);
                }
                catch
                {
                    // 与原版一致：单条连接异常不中断 accept 循环
                }
            }
            _listener?.Stop();
            Netplay.IsListening = false;
        }

        // ═══════════════════════════════════════════
        // 客户端侧 I/O（accepted 包装用，与 LinuxTcpSocket 一致）
        // ═══════════════════════════════════════════

        void ISocket.Close()
        {
            _remoteAddress = null;
            _connection.Close();
        }

        bool ISocket.IsConnected()
        {
            return _connection != null && _connection.Client != null && _connection.Connected;
        }

        void ISocket.Connect(RemoteAddress address)
        {
            var tcpAddress = (TcpAddress)address;
            _connection.Connect(tcpAddress.Address, tcpAddress.Port);
            _remoteAddress = address;
        }

        bool ISocket.IsDataAvailable()
        {
            return _connection.GetStream().DataAvailable;
        }

        RemoteAddress ISocket.GetRemoteAddress()
        {
            return _remoteAddress!;
        }

        private void ReadCallback(IAsyncResult result)
        {
            var tuple = (Tuple<SocketReceiveCallback, object>)result.AsyncState;
            try
            {
                tuple.Item1(tuple.Item2, _connection.GetStream().EndRead(result));
            }
            catch (InvalidOperationException)
            {
                // 客户端断开时的常见行为
                ((ISocket)this).Close();
            }
            catch (Exception ex)
            {
                TShockAPI.TShock.Log.Error(ex.ToString());
            }
        }

        private void SendCallback(IAsyncResult result)
        {
            var expr = (object[])result.AsyncState;
            LegacyNetBufferPool.ReturnBuffer((byte[])expr[1]);
            var tuple = (Tuple<SocketSendCallback, object>)expr[0];
            try
            {
                _connection.GetStream().EndWrite(result);
                tuple.Item1(tuple.Item2);
            }
            catch (Exception)
            {
                ((ISocket)this).Close();
            }
        }

        void ISocket.AsyncSend(byte[] data, int offset, int size, SocketSendCallback callback, object? state)
        {
            var buffer = LegacyNetBufferPool.RequestBuffer(data, offset, size);
            _connection.GetStream().BeginWrite(buffer, 0, size, SendCallback,
                new object[] { new Tuple<SocketSendCallback, object>(callback, state), buffer });
        }

        void ISocket.AsyncReceive(byte[] data, int offset, int size, SocketReceiveCallback callback, object? state)
        {
            _connection.GetStream().BeginRead(data, offset, size, ReadCallback,
                new Tuple<SocketReceiveCallback, object>(callback, state));
        }

        // ═══════════════════════════════════════════
        // 工具
        // ═══════════════════════════════════════════

        /// <summary>
        /// 反射安全读取 Netplay.Disconnect（不同版本字段集合有差异，ConnectionGuard 同款模式）。
        /// 字段不存在/读取失败 → 返回 false（视为未断开，保守不误伤）。
        /// </summary>
        private static FieldInfo? _disconnectField;
        private static bool IsNetplayDisconnect()
        {
            try
            {
                if (_disconnectField == null)
                    _disconnectField = typeof(Netplay).GetField("Disconnect",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                return _disconnectField?.GetValue(null) is bool b && b;
            }
            catch
            {
                return false;
            }
        }
    }
}
