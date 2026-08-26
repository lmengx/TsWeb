# ConnectionGuard —— 回归 1.4.5.6 监听健壮性（修复 tcping 高并发假死）

> 适用：Terraria **1.4.5.7** + TShock 6.x 服务器（TShockAPI ≥6.1 / OTAPI3）
> 类型：独立 TShock 插件（可独立加载，不依赖 TSWeb）
> 编译目标：net9.0

---

## 一、问题现象

服务器被 **tcping / 连接风暴** 攻击时假死：

- 控制台**不再显示任何连接信息**（`Client connecting` 消失）
- 游戏客户端**只显示"连接到 ip:port"**，卡住进不来
- 高并发持续打时**永久假死**（不重启不恢复），已在线玩家不受影响

关键：**哪怕攻击只是 tcping（connect 后不发任何数据）也会触发**，无需进入游戏、无需任何协议包。

---

## 二、根因分析（本地四份反编译实证）

> 证据文件：
> - 1.4.5.6 原版服务器：`C:\Games\TerraAngelv1.4.5.6\steam\Terraria\TerrariaServer.exe`
> - 1.4.5.6 TShock 时代：`参考源码/api文件/1456/OTAPI.dll` + `TShockAPI.dll`
> - 1.4.5.7 原版服务器：`参考源码/api文件/1457/TerrariaServer.exe`
> - 1.4.5.7 TShock 时代：`参考源码/api文件/1457/OTAPI.dll` + `TShockAPI.dll`
> - TShock 网络层：`参考源码/TShock-general-devel/TShockAPI/Sockets/LinuxTcpSocket.cs`

### 2.1 缺陷①：挂起连接永久占槽（两个版本都有）

`Terraria.RemoteClient`（服务端槽位对象）对**已连接但永不发数据**的连接没有超时踢出：

```csharp
public void Update()
{
    if (!IsActive) { State = 0; IsActive = true; }
    TryRead();                      // ← 无任何超时检查
}
private void TryRead()
{
    if (_isReading) return;
    if (Socket.IsDataAvailable() && !ReadBufferFull)   // ← 只在"有数据"时才读
        Socket.AsyncReceive(...);
}
```

- `TimeOutTimer` 字段存在，但**全代码库从未检查**（原版遗留的未实现超时）
- 攻击者连上不发数据 → `IsDataAvailable()` 恒 false → 永不读取、永不踢出 → **永久占槽**

### 2.2 缺陷②：1.4.5.6 vs 1.4.5.7 服务器架构差异（关键！）

| 项 | **1.4.5.6 服务器**（原版反编译） | **1.4.5.7 服务器**（原版反编译） |
|---|---|---|
| `OnConnectionAccepted` 槽位满时 | `StopListening()` + `IsListening=false`（**停监听**） | 仅 `KickClient`（发"服务器已满"）→ **监听常驻** |
| `ServerLoop` | 每帧 `StartListeningIfNeeded()`（**自动恢复监听**） | **无此方法** |
| 挂起连接超时 | 无 | 无 |
| 结果 | 瞬时洪水"停监听→连接断开→槽位释放→自动恢复"**自愈闭环** | 洪水**硬扛**：backlog 堆积 + 挂起占槽 → **假死无自愈** |

> ⚠️ 结论：**1.4.5.7 原版就存在此问题**（不是 TShock 引入的），1.4.5.7 把 1.4.5.6 的"停监听+自动恢复"自愈机制删除了。用户实测确认：1.4.5.6 无缺陷、1.4.5.7 有、原版服务端也有。

### 2.3 缺陷③：TShock `LinuxTcpSocket` 雪上加霜

TShock 6.x 通过 `OTAPI.Hooks.Netplay.CreateTcpListener` 把原版监听器替换为 `LinuxTcpSocket`（1.4.5.6 与 1.4.5.7 的 TShock 该实现**反编译完全一致**），存在二次缺陷：

```csharp
bool ISocket.StartListening(SocketConnectionAccepted callback)
{
    this._isListening = true;
    ...
    if (this._listener == null) this._listener = new TcpListener(...);   // 复用已 Stop 的实例
    try { this._listener.Start(); }                                      // 重新 bind
    catch (Exception) { return false; }                                  // ← bind 失败（TIME_WAIT）
    ThreadPool.QueueUserWorkItem(new WaitCallback(this.ListenLoop));
    return true;
}
void ISocket.StopListening() { this._isListening = false; }              // ← 只置标志，不真停！
```

- 连接风暴后大量 **TIME_WAIT** 占用端口，`TcpListener` 默认无 `SO_REUSEADDR` → **bind 失败**
- 原版 `StartListeningIfNeeded` **忽略 `StartListening()` 返回值**，无条件 `IsListening = true` → **假监听**（标志在、socket 无）→ 永不重试 → 永久拒绝新连接
- `StopListening` 不真正关闭监听 socket → `AcceptTcpClient` 阻塞不退出、端口不释放

---

## 三、修复方案（v5：纯看门狗 + 可选限流钩子）

> ⚠️ **v4 崩溃事故（2026-08 实测）**：上一版曾用 MonoMod 改写 `LinuxTcpSocket` 的
> `StartListening` / `StopListening` / `ListenLoop` 三个方法做"源头修复"，结果**服务器内核在
> 玩家连接时直接崩溃**。根因：
> - `OnStartListening` 每次重建 `self._listener`，但旧 `ListenLoop` 线程仍阻塞在旧监听器的
>   `AcceptTcpClient()` 上**永不退出**（旧监听器从未被真停）→ 每次 `StartListening` 泄漏一个
>   accept 线程；
> - 配合 `ListenLoop` 改写 → 多个 accept 线程**并发调用 `Netplay.OnConnectionAccepted`** →
>   对 `Netplay.Clients` 槽位分配产生数据竞争 → 内存破坏 → 内核崩溃（"一有人进来就崩"）。
>
> 结论：**不再对连接生命周期方法做 detour**。且本服是定制 1.4.5.7（`Netplay` 缺少 `Disconnect`
> 字段），对 vanilla/TShock 连接路径做 detour 风险不可控。本版改为**纯托管代码**（零 detour，
> 每层 try/catch，异常最多被记录、绝不崩进程）。

### 第 1 层 · 挂起清理看门狗（核心，补回 1.4.5.6 自愈能力）

定时（默认 1s）扫描 `Netplay.Clients[]`，清理「`State==0`（已连接但未握手）且满足以下任一条件」的连接：

- **握手超时**：挂起超过 `handshakeTimeoutSeconds`（默认 10s）
- **同 IP 超限**：同 IP 挂起连接数 > `maxHangingPerIp`（默认 3）
- **全局超限**：全局挂起连接数 > `maxHangingGlobal`（默认 20，必须 < 槽位数）

清理方式：**`client.Socket.Close()`**（与成熟开源插件 **yaaiomni / Chireiden.TShock.Omni** 同款）
→ 关闭底层 socket → `IsConnected()==false` → 原版 `ServerLoop` 下一轮 `Reset()` 释放槽位。

### 第 2 层 · 恢复监听（回归 1.4.5.6 `StartListeningIfNeeded`）

若 `Netplay.IsListening==false`（槽位被占满导致监听停止）且存在空槽：

- 反射取 `Netplay.OnConnectionAccepted` 委托 → 调用 `TcpListener.StartListening(callback)` 重启监听
- 全程托管 + 反射 + try/catch，失败仅告警、下轮自动重试

### 第 3 层 · 可选限流钩子（默认关闭）

`rateLimitEnabled: true` 时，用 MonoMod detour `Netplay.OnConnectionAccepted`（分配槽位前的唯一入口）做进槽前限流：

- 每 IP 连接速率：滑动窗口，默认 ≤10 次 / 5 秒
- 超限 → 直接 `client.Close()`（不占槽位）

> **为什么默认关闭**：此方法与开源插件 yaaiomni 一致、在标准 TShock/OTAPI 上验证过；但本服是
> 定制 1.4.5.7，detour 连接路径行为无法 100% 预知（v4 已有崩溃教训）→ 默认走零风险的纯看门狗。
> 如需更强前置拦截再开启，并在低峰期实测。**开启后若仍有异常，立即把配置置回 false。**

---

## 四、部署与配置

### 部署

1. 编译：`dotnet build plugin-son/ConnectionGuard/ConnectionGuard.csproj -c Release`
2. 将 `bin/Release/net9.0/ConnectionGuard.dll` 放入服务器 `ServerPlugins/` 目录
3. **重启服务器进程**（插件加载需重启，且能重置当前假死状态）
4. 首次启动自动生成配置 `tshock/ConnectionGuard/connection-guard.json`

### 配置项

| 键 | 默认 | 说明 |
|---|---|---|
| `enabled` | `true` | 看门狗总开关 |
| `scanIntervalSeconds` | `1` | 扫描间隔 |
| `handshakeTimeoutSeconds` | `10` | 挂起连接超时（正常握手毫秒级，10s 极宽松） |
| `maxHangingPerIp` | `3` | 同 IP 挂起上限 |
| `maxHangingGlobal` | `20` | 全局挂起上限（**必须 < 槽位数** = MaxSlots+ReservedSlots） |
| `restoreListening` | `true` | 监听停止后自动恢复（回归 1.4.5.6 StartListeningIfNeeded） |
| `warnOnListenStop` | `true` | 槽位满告警 |
| `logKicks` | `true` | 清理日志 |
| `rateLimitEnabled` | `false` | **可选**限流钩子（默认关，见三章说明） |
| `maxConnectionsPerWindow` | `10` | 每 IP 窗口内连接上限（仅钩子模式） |
| `rateWindowSeconds` | `5` | 限流窗口（仅钩子模式） |
| `ipWhiteList` | `[]` | 限流白名单 IP |
| `logRejections` | `true` | 拒绝汇总日志 |

### 兼容性说明（重要）

插件按 TShock 6.1.0 NuGet（Terraria **1.4.4.x** API）编译，但运行时是 **1.4.5.7** 服务端。
不同版本的 `Terraria.Netplay` 类型字段集合有差异，直接字段访问可能抛 `MissingFieldException`：

```
GuardCore: ERROR: [ConnectionGuard] 扫描异常: Field not found: 'Terraria.Netplay.Disconnect'.
```

**修复**：对 `Netplay.Disconnect` 的读取改为**反射安全**（字段存在→正常读；
不存在/读取失败→返回 `false`，视为"未断开"，保守不误伤）：

- `IsServerReady()` 中 `!IsNetplayDisconnect()`，字段读不到时等价于"未断开"，不影响看门狗

> 若日志再次出现其他 `Netplay.*` 的 `MissingFieldException`，同样可按此反射模式处理。

### 启动日志验证

```
[ConnectionGuard] 连接看门狗已启动 (扫描:1s, 握手超时:10s, 恢复监听:开, 限流钩子:关闭)
```

tcping 高并发压测时（纯看门狗模式）：

- 攻击连接入槽 → 看门狗 1s 内按「同 IP 超限」或「握手超时」清理 → `清理挂起连接 槽位:x IP:y 原因:...`
- 若槽位曾被打满导致监听停止 → 恢复监听日志：`✓ 已恢复监听（回归 1.4.5.6 StartListeningIfNeeded）`
- **玩家始终可进，不再假死**

---

## 五、TSWeb 内置模块（已删除）

TSWeb 主插件 `plugin/` 内曾内置同逻辑的 `ConnectionGuard.cs` 模块（`Main.cs` 中 `ConnectionGuard.Initialize()` 等三处集成）。

**已于 2026-08 从 TSWeb 中移除**，统一使用本独立插件，避免重复操作。

---

## 六、补充建议（运维层根治）

插件是**应用层防护**。配合以下手段更稳妥：

1. **云安全组**：仅放行可信来源 IP 访问游戏端口
2. **Windows 防火墙**：攻击 IP 已知时 `netsh advfirewall firewall add rule ...` 直接封禁
3. 长期暴露公网且频繁被打 → 前置 TCP 代理（如 HAProxy）做连接速率限制
