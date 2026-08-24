# ForceVersion 插件

强行跨版本兼容插件（**临时方案 v2**）：让 **1.4.5.7 客户端（协议 Terraria325）** 进入 **1.4.5.6 服务器（协议 Terraria319）**。

## 功能特性

### 一、握手层（v1）

- 客户端 ConnectRequest 包版本串 `Terraria325` → 改写为服务器期望的 `Terraria319`，通过握手版本检查
- 只改版本字段，服务器封禁/密码/连接状态机全部原样执行

### 二、进服阶段兼容层（v2 临时方案）

服务器应答图格请求（case 8）时除了发区块，还会推送一批 1.4.5.7 已改版的包。v2 对**已识别的 1.4.5.7 跨版本客户端**做如下处理：

| 方向 | 包 | 处理 | 原因（协议实证） |
|------|----|------|------------------|
| 出站 | 22 ItemOwner | **跳过** | 旧 10B（index\|owner\|pos）vs 新 18B+（多 4 字段）→ 错位 |
| 出站 | 27 SyncProjectile | **跳过** | 旧 identity(2B)+owner(1B) vs 新 ProjectileKey(4B) → 全错位 |
| 出站 | 82 NetModule（模块 ID>4） | **跳过** | 1.4.5.7 插入 CreativeUnlocks → CreativePowers 起模块 ID 全部 +1 → 客户端错解 |
| 出站 | 21 SyncItem | 保留 | 1.4.5.6 发送 flags 恒为 0 → 1.4.5.7 不读额外尾 → 字节兼容 |
| 出站 | 23 SyncNPC | 保留 | 旧 short npc 与 新 byte npc+byte gen 恰好同宽（npc<256 时对齐） |
| 入站 | 82 NetModule | **丢弃** | 1.4.5.7 客户端模块 ID 位移 → 服务器旧表错位解析 |
| 入站 | 27/29 SyncProjectile/KillProjectile | **丢弃** | 新格式 → 服务器旧表错位（弹幕类攻击暂失效） |
| 入站 | 28 DamageNPC | **gen 字节清零** | 新 byte npc+byte gen 与旧 short npc 同宽，清零后完全兼容 → 保留伤害 |

跨版本客户端识别：ConnectRequest 版本串为 `Terraria325` 时自动登记，断开时（ServerLeave）自动清理。

## 技术实现

- **MonoMod RuntimeDetour.Hook** 挂钩三个方法：
  1. `MessageBuffer.GetData(int, int, out int)` —— 入站总入口（版本改写 + 入站过滤）
  2. `NetMessage.SendData(int msgType, int remoteClient, ...)` —— 出站定向包过滤
  3. `NetManager.SendToClient(NetPacket, int playerId)` —— 出站 NetModule 位移过滤
- 包布局：`readBuffer[start]`=包类型、`readBuffer[start+1]`=版本串长度(7bit)、`readBuffer[start+2..]`=版本串内容
- `Terraria325` 与 `Terraria319` 等长（11 字符），按字节改写长度前缀不动

## 已知边界（临时方案，后续需补协议翻译层）

- **进服后**服务器对全体广播（remoteClient=-1）的 21/22/23/27 不在过滤范围，跨版本客户端可能因错位包偶发断线
- 1.4.5.7 客户端**弹幕类攻击无效**（入站 27 被丢弃）；近战/常规伤害保留（28 gen 清零）
- 跨版本客户端收不到位移 NetModule（粒子特效/传送门/旗帜/创造模式）
- 其他版本串（如 `Terraria326` 等）不会放行

## 安装

将编译好的 `ForceVersion.dll` 放入 `ServerPlugins` 目录。
