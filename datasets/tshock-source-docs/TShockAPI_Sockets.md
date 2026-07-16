# 模块: Sockets

---

# Sockets/LinuxTcpSocket.cs

**命名空间**: `TShockAPI.Sockets`

## 类型定义
- **LinuxTcpSocket** : `I`

## 方法
### `public LinuxTcpSocket()`

### `public LinuxTcpSocket(TcpClient tcpClient)`

### `void ReadCallback(IAsyncResult result)`

### `void SendCallback(IAsyncResult result)`

### `void ListenLoop(object unused)`

### `void ISocket.Close()`

### `bool ISocket.IsConnected()`

### `void ISocket.Connect(RemoteAddress address)`

### `void ISocket.AsyncSend(byte[] data, int offset, int size, SocketSendCallback callback, object state)`

### `void ISocket.AsyncReceive(byte[] data, int offset, int size, SocketReceiveCallback callback, object state)`

### `bool ISocket.IsDataAvailable()`

### `RemoteAddress ISocket.GetRemoteAddress()`

### `bool ISocket.StartListening(SocketConnectionAccepted callback)`

### `void ISocket.StopListening()`
