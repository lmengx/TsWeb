# PacketCatch — 全量入站数据包记录器

用于**外挂原理分析/取证**的底层发包记录插件。基于与 Omni (Chireiden.TShock.Omni) 同款的
`OTAPI.Hooks.MessageBuffer.GetData` 钩子 —— 这是 Terraria 服务器解析客户端包的**最底层入口**，
**每个**客户端→服务器的数据包（包括握手期包、TShock 未注册处理的包如 Dust 66）都会经过这里并被记录。

> ⚠️ 本插件只做**记录**，不做任何拦截/审查，对游戏逻辑零干扰。

## 构建

```bash
cd plugin-son/PacketCatch
dotnet build -c Release
```

产物：`bin/Release/net9.0/PacketCatch.dll`，放入 TShock 的 `ServerPlugins/` 目录。

## 命令

| 命令 | 说明 |
|------|------|
| `/pcatch` | 查看状态 |
| `/pcatch start` | 开始记录 |
| `/pcatch stop` | 停止记录并刷盘 |
| `/pcatch flush` | 强制刷盘 |
| `/pcatch reload` | 重载配置 |
| `/pcatch filter 27,66` | 仅记录指定包 ID（逗号分隔）；不带参数=全部 |

权限：`tshock.admin`

## 配置

首次启动生成 `{TShock.SavePath}/PacketCatch/config.json`：

```json
{
  "启用": true,                 // 启动即自动开始记录
  "输出目录": "PacketCatch",    // 相对 TShock.SavePath，也支持绝对路径
  "刷新间隔秒": 5,              // 后台刷盘间隔
  "单文件大小MB": 100,          // 超过后滚动新文件
  "记录PlayerUpdate": true,     // PlayerUpdate 是最高频包(约60/秒/人)，关闭可大幅减容
  "脱敏密码包": true,           // PasswordSend 仅记元数据，不记明文密码
  "仅记录这些包ID": []          // 空 = 记录全部
}
```

启动时自动在输出目录生成 `PacketTypes.txt`（包 ID → 名称映射，供分析使用）。

## 输出文件格式（.pcapd v2）

二进制，每条记录定长 14 字节头部 + 变长数据，**每条记录内嵌玩家名与 IP**：

```
文件头 (13 字节):  "PCAT" + version=2(1) + 创建时间 UTC ticks(8, LE)

每条记录:
  [0..7]   DateTime.UtcNow.Ticks  (Int64, 小端)
  [8]      whoAmI                 客户端槽位索引 (byte, 255=未知)
  [9]      packetId               真实包 ID (byte)  ← 从原始缓冲区读取
  [10]     nameLen                玩家名长度 (byte)
  [11..]   玩家名 (UTF-8, nameLen 字节)
  [..]     ipLen                  IP 长度 (byte)
  [..]     IP 字符串 (UTF-8, ipLen 字节)
  [..]     payloadLen             payload 长度 (UInt16, 小端)
  [..]     payload                payloadLen 字节的原始包数据
```

> 说明：`payload` 为包体数据（不含包 ID 字节，包 ID 已在 header 单独记录）。
> 包 ID 从原始缓冲区读取而非 `args.PacketId`——因为其他插件取消包时会把 PacketId
> 改写为 255（Omni 的 CancelPacket 机制），原始缓冲区的值才是真实包类型。
> 玩家名/IP 取自 `TShock.Players[whoAmI]`（握手早期退回 socket 远程地址），每条记录独立存储。

> ⚠️ v2 格式不兼容 v1（旧文件无玩家名/IP），重新抓包后使用新 DLL 生成 v2 文件。
> 历史 v1 文件将被解析脚本跳过。

### 性能参考（20-30 人服）

| 场景 | 包量 | 磁盘 |
|------|------|------|
| 挂机（含 PlayerUpdate） | ~2,000 包/秒 | ~200-400 KB/s ≈ 0.7-1.4 GB/小时 |
| 战斗峰值 | ~5,000-6,000 包/秒 | ~0.5-1.2 MB/s ≈ 2-4 GB/小时 |

CPU 增量 < 1% 单核（每包仅一次内存拷贝 + 一次带缓冲的 FileStream 写，无逐条磁盘 IO）。
如需长时间记录建议：`记录PlayerUpdate: false` + SSD + 关注磁盘剩余空间。
