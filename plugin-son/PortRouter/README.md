# PortRouter —— 端口协议路由（游戏端口同时承载 HTTP(REST) 与游戏流量）

> 适用：Terraria **1.4.5.7 / 1.4.5.8** + TShock 6.x 服务器（TShockAPI ≥6.1 / OTAPI3）
> 类型：独立 TShock 插件（可独立加载，不依赖 TSWeb 主插件）
> 编译目标：net9.0

---

## 一、做什么

默认架构下，Terraria 服务器有两个监听端口：

| 端口 | 用途 |
|------|------|
| 游戏端口（默认 7777） | 游戏协议（Terraria 客户端） |
| REST 端口（默认 7878） | HTTP(REST API，TShock / TSWeb WebRestServer） |

本插件让**游戏端口一个端口同时承载两种流量**，且判定发生在 accept 循环内、任何日志/进槽之前：

```
客户端 ──TCP 直连──▶ 游戏端口(7777) 协议感知监听器（HttpAwareSocket）
                        │  accept 后 peek 首字节（SocketFlags.Peek，不消费数据）
                        ├─ HTTP（前2字节为 ASCII 字母：GET/POST/HEAD/...）
                        │     └─▶ 静默字节泵转发到 REST 端口（127.0.0.1:7878）
                        │          不打印「xxx正在连接」、不占游戏槽位
                        └─ 游戏协议（[2字节长度][MessageID 0x01]）
                              └─▶ 正常打印「正在连接」+ 原版进槽流程（无转发）
```

## 二、实现机制（非传统转发）

**不挂钩任何方法、不做任何 detour** —— 直接通过 OTAPI 官方扩展点
`OTAPI.Hooks.Netplay.CreateTcpListener` 把游戏监听器替换为协议感知监听器
`HttpAwareSocket`（与 TShock 安装 LinuxTcpSocket、开源插件 ProxyProtocolSocket /
yaaiomni 完全同款机制）。

关键点：
- **`Order = 1000`**（Main.cs 构造函数）：TShock 默认 Order=1，本插件 Order=1000
  保证在 TShock **之后**订阅 CreateTcpListener → `args.Result` 最后写入 → 监听器替换**必然生效**
  （ProxyProtocolSocket 的硬性约束，注释原文 "Must be the last to handle CreateTcpListener"）。
- **「正在连接」日志**：由监听器自己打印（`Console.WriteLine`），HTTP 连接在判定后直接静默转发、
  **根本不走到打印那一步** → 后台不再刷屏；游戏连接照常打印，行为与原来完全一致。
- **原 IP 保留**：客户端直连游戏端口，游戏流量用同一 TCP 连接进槽，
  `TcpAddress` 从 `RemoteEndPoint` 读取 → 真实来源 IP，无任何本地中转。

## 三、判定可靠性

| 流量 | 首包内容 | 判定 |
|------|----------|------|
| HTTP | `GET /...` / `POST /...` / `HTTP/1.1 ...`（前 2 字节为 ASCII 字母） | HTTP ✓ |
| 游戏 | `[2 字节长度][MessageID 0x01]`，长度高字节恒为 0x00（非字母） | 游戏 ✓ |

只有游戏首包长度 ≥ 0x4141（16705 字节）且两个长度字节恰好都是字母才会误判 ——
对 ConnectRequest 握手包（数十字节）不现实，实际零误判。

## 四、安全性

- **零 detour**：不挂钩监听生命周期方法（ConnectionGuard v4 实测 detour
  StartListening/StopListening/ListenLoop 会导致 accept 线程泄漏 + 槽位竞争 → 内核崩溃），
  本插件是完整的 ISocket 实现，无此风险。
- **fail-open**：嗅探/转发任何异常一律放行进游戏流程，路由逻辑自身故障绝不误伤正常游戏连接。
- **兼容性**：`Netplay.Disconnect` 反射安全读取（字段缺失时保守放行，ConnectionGuard 同款模式）；
  I/O 方法与 LinuxTcpSocket 一致（LegacyNetBufferPool + BeginRead/Write，生产实证）。

## 五、部署与配置

### 部署

1. 编译：`dotnet build plugin-son/PortRouter/PortRouter.csproj -c Release`
2. 将 `bin/Release/net9.0/PortRouter.dll` 放入服务器 `ServerPlugins/` 目录
3. **重启服务器进程**
4. 首次启动自动生成配置 `tshock/PortRouter/portrouter.json`

### 配置项

| 键 | 默认 | 说明 |
|---|---|---|
| `enabled` | `true` | 总开关。关闭后所有连接原样进游戏流程 |
| `httpDetectTimeoutMs` | `1500` | 等待首字节的超时（毫秒）。游戏/HTTP 客户端连上即发包，正常毫秒级返回 |
| `restForwardPort` | `0` | HTTP 转发目标端口。`0` = 自动取 TShock 配置的 `RestApiPort`（默认 7878） |
| `logHttpDetections` | `false` | 记录每次 HTTP 转发日志（默认关，避免被轮询/健康检查刷屏） |

### 启动日志验证

```
[PortRouter] 已注册协议感知监听器（Order=1000，替换 TShock 默认监听器；HTTP 不再打印「正在连接」）
[PortRouter] 端口协议路由已启用：游戏端口同时接受 HTTP(→REST 127.0.0.1:7878) 与游戏流量（游戏流量原 IP 直连保留，嗅探超时 1500ms）
[PortRouter] 协议感知监听器已生效（HTTP 静默转发 REST / 游戏流量原 IP 进槽）
[PortRouter] 首次捕获 HTTP 连接（来源 1.2.3.4:54321），已静默转发到 REST；游戏端口协议路由生效
```

**验证「不再刷屏」**：HTTP 请求打游戏端口时，后台不再出现「xxx正在连接」；
游戏玩家连接时该日志照常显示。

### 使用方式

- 游戏客户端照常连接游戏端口（7777），无感知
- 后端/浏览器把 REST 地址改为游戏端口（7777），HTTP 请求自动被路由到 REST
- REST 端口（7878）可继续对外开放，也可在安全组/防火墙中关闭仅留游戏端口

## 六、注意事项

1. **仅支持明文 HTTP**：HTTPS（TLS ClientHello 首字节 0x16）会被判为游戏流量 → 游戏服超时踢出。
   后端到服务器走明文 HTTP（TSWeb WebRestServer 即明文），不受影响。
2. **REST 端口必须在本机监听**：HTTP 转发目标是 `127.0.0.1:REST端口`，
   若 REST 完全关闭（`RestApiEnabled=false` 且无 WebRestServer），HTTP 转发会失败并记日志。
3. **转发后 REST 侧来源 IP 为 127.0.0.1**：这是「http 转发到 rest」的固有代价；
   游戏流量不受影响（原 IP 保留）。若需 REST 也见原始 IP，需在插件内自实现 HTTP 处理（不在本插件范围）。
4. 监听器替换依赖插件 Order：本插件已设 `Order=1000`（最后处理 CreateTcpListener）。
   若再安装其他同样替换监听器且 Order 更高的插件，可能互相覆盖，注意排查。
