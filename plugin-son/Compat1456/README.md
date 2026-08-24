# Compat1456 插件

反向跨版本兼容插件（**v1.2**）：让 **1.4.5.6 客户端（协议 Terraria319）** 进入 **1.4.5.7 服务器（协议 Terraria325）**。

与 `plugin-son/ForceVersion`（新客户端→旧服务器）方向相反：本插件跑在 **1.4.5.7（新）服务器** 上，把服务器发出的**新格式包翻译成旧格式**发给旧客户端，并把旧客户端上行按服务器可接受的方式处理。

> 协议依据：**反编译实证**（ilspycmd 反编译 1.4.5.7 `lib/OTAPI.dll` 与 1.4.5.6 `Downloads/utsl-win-x64-v0.3.1-alpha.1/lib/OTAPI.dll` 的 MessageBuffer/NetMessage/ChatMessage 等，逐包对比新旧线格式）＋《Terraria-1457-源码解读记录》。

## 实现机制

MonoMod RuntimeDetour.Hook 三个钩子：

1. `MessageBuffer.GetData(int,int,out int)` —— 入站总入口（版本改写＋登记＋等长翻译/丢弃）
2. `NetManager.SendToClient(NetPacket,int)` —— 出站 NetModule 位移翻译
3. `NetMessage.SendPacket(byte[],int)` —— 出站最终字节层翻译

⚠️ **v1.2 关键修复（跨版本坑）**：`NetMessage.SendPacket` 在 1.4.5.6 是 `private`，在 1.4.5.7（OTAPI3 打包）是 `public static`（hook 包装）。用 `NonPublic` 找 1.4.5.7 会失败（日志“未找到 NetMessage.SendPacket 方法”）→ **出站翻译全部失效** → 旧客户端收未翻译新格式包 → 卡图格/闪退。必须 `Public | NonPublic` 都找。

兼容客户端识别：ConnectRequest 版本串为 `Terraria319` 时自动登记（翻译），断开（ServerLeave）自动清理。

**放行策略（服务器协议 325）**：

| 客户端协议 | 处理 |
|-----------|------|
| `Terraria325` | 原生直进，不干预 |
| `Terraria326` | 仅改写版本串→`Terraria325` 通过校验，**不翻译**（包格式与 325 完全一致） |
| `Terraria319` | 改写版本串 + 登记翻译（17/23/27/29/82/162 等） |

**动态协议号**：服务器期望版本串不硬编码，反射读取 `Main.curRelease`（`"Terraria" + curRelease`；编译期 const 会内联 325 所以必须反射）。插件启动时打印服务器协议号，每次握手打印客户端上报协议号 vs 服务器期望协议号。

## 翻译表（v1.1，反编译实证）

### 出站（新服务器 → 旧客户端）

| 包 | 处理 | 依据 |
|----|------|------|
| 17 Tile | **裁掉第 9 字节**（9B→8B） | 1.4.5.7 出站 body=9B，旧客户端按 8B 读 |
| 21 SyncItem | **截断到 24B 主体 + flags 置 0**（去 ownership/shimmer/enemyDelay 尾） | case 21 实证：固定体 index+pos+vel+stack+prefix+flags+type=24B，尾可选 |
| 22 ItemOwner | **截断：头 3 字节(index+owner) + 尾 8 字节(position)** | case 22 实证：position 是最后字段，中间 timeToKeep/grabDelay 为 7bit 变长，直接跳过 |
| 23 SyncNPC | **gen 字节清零 + position 还原**（同步点−Size×SyncAnchor[type]） | case 23 + NetMessage.SendData case 23 实证：槽位同宽、position 为同步点 |
| 27 SyncProjectile | 完整翻译：ProjectileKey(4B)→identity(2B)+owner(1B) 重排 | case 27 实证 |
| 29 KillProjectile | 完整翻译：key+deathPos(12B)→identity+owner(3B) | case 29 实证 |
| 82 NetModule | 模块 ID 位移：新 ID≥6→-1；新 ID==5(CreativeUnlocks)→过滤 | 模块表位移（CreativePowers 起 +1） |
| 162 DamageNPCAck | 过滤（旧客户端 MessageID 只到 161） | 1.4.5.7 新增 |

### 入站（旧客户端 → 新服务器）

| 包 | 处理 | 依据 |
|----|------|------|
| 1 Hello | 版本串 319→325 改写＋登记 | 1.4.5.7 硬拒 |
| 82 NetModule | 模块 ID 旧→新：**moduleId 读 start+1**（type 后第一字节），旧 ID≥5→+1 | ⚠️ 修正：原读 start+2 多偏 1 字节，会把聊天/粒子包 payload 写坏 → "发消息无效" 根因 |
| 27/29 弹幕 | 丢弃（readBuffer 内无法增长包体） | 旧→新需增 1B/9B |
| 22 ItemOwner | 透传 | 新版 TShock 只读前 3B |
| 28 DamageNPC | 透传 | short npc 与 byte npc+byte gen 同宽，npc<256 兼容 |
| 17/21/23/25 | 透传 | 入站 Tile 8B、旧 21 flags 无尾、23 同宽、25 case 空（聊天走 82/Text） |

## 实测修复进度（用户反馈对照）

| 现象 | 状态 | 修复 |
|------|------|------|
| 看不到掉落物 | ✅ 已修 | 出站 21 截断+flags 清 0 |
| 无法捡起掉落物 | ✅ 已修 | 出站 22 截断（头3+尾8） |
| 发消息和指令无效 | ✅ 已修 | 入站 82 moduleId 偏移 start+2→start+1（原 bug 写坏聊天包） |
| 不能看到 NPC | ✅ 已修（待实测） | 出站 23 gen 清零 + position 还原（同步点−Size×SyncAnchor） |
| PC 卡接收图格 / PE 闪退 | ✅ 已修（待实测） | **v1.2：SendPacket 钩子 NonPublic→Public\|NonPublic**（原出站翻译全失效→旧客户端收新格式包错位→卡/崩） |
| 打到怪没反应 | ✅ 已修（v1.3） | 入站 28：gen 字节 = Main.npc[npc].generation（新服务器硬校验 generation，旧客户端 gen=0 被拒） |
| PE 打到怪闪退 | ✅ 已修（v1.3） | 出站 28：gen 字节清零（旧客户端读 short npc，gen≠0 污染高位→越界） |
| 旧版创建的弹幕其他人看不见 | ✅ 已修（v1.4 待实测） | 入站 27/29 语义级重放：解析旧格式→反射 `ProjectileKey.NewProjectileSetup`+`FinalizeProjectile`+`TrySendData(27)` 广播；`key.Index=identity` 使旧客户端匹配本地弹幕 |
| 看别人/服务器的弹幕错乱 | ✅ 已修（v1.4） | **ProjectileKey 位布局反编译实证修正**：`bits&0xFF=Spawner`、`(bits>>8)&0x3FF=Index`、`(bits>>18)&0x3FFF=Gen`（原推断 Spawner 高位全错） |

## 已知边界

- 旧客户端弹幕攻击/同步失效（入站 27/29 丢弃；出站显示翻译已做）
- 服务器对全体广播(remoteClient=-1)的新格式包无法按客户端翻译（跨版本固有硬伤）
- SyncNPC(23) position 还原依赖反射 `NPCID.Sets.SyncAnchor` 与 `ContentSamples.NpcsByNetId`（1.4.5.7 运行时存在；失败则跳过还原，保持 gen 清零）

## 后续待办

- [ ] PE 图格闪退：深度对比 1.4.5.6/1.4.5.7 的 10 号图格区块 Tile 编码（DecompressTileBlock/位标志/RLE），确认是否需翻译
- [ ] ProjectileKey 位布局实测校准（若与 `Terraria.DataStructures.ProjectileKey` 不符，改 `UnpackKey`）
- [ ] 入站 27/29 语义级重放（绕 readBuffer 增长限制，恢复旧客户端弹幕攻击）
