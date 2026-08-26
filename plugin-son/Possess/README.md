# Possess —— 寄生 / 观战 / 直播插件（管理员专用）

TShock 子插件（`plugin-son/Possess`），通过**客户端角色伪装**（复刻多服同步槽位错乱的机制）实现：

1. **寄生**：管理员客户端"变身为"目标玩家（外观/背包/选中物品格/位置全是目标的），管理员操作直接作用于目标；目标自身完全冻结（移动/放块/开箱/攻击/物品栏等操作全被服务端拒绝，只能聊天），且能看到自己被驱动却无法操作
2. **观战**：管理员客户端"变身为"目标的第一人称视角，目标正常玩，管理员操作被丢弃
3. **直播**：自动切换"变身"目标 —— 挂机若干秒自动切换，目标死亡立即换人，跳过挂机/离线玩家

## 命令

| 命令 | 说明 | 权限 |
|------|------|------|
| `/possess <玩家名>` | 寄生：管理员变身为目标并接管其操作，目标冻结 | `possess.use` |
| `/possess stop` | 退出寄生，目标恢复 | `possess.use` |
| `/watch <玩家名>` | 第一人称观战（管理员变身为目标，目标正常玩） | `possess.use` |
| `/watch next` / `stop` | 切换下一位存活玩家 / 退出观战 | `possess.use` |
| `/live` | 开启直播（自动切换变身目标） | `possess.use` |
| `/live 15` | 设置挂机切换阈值为 15 秒（默认 10 秒，上限 300）并开启 | `possess.use` |
| `/live off` | 退出直播 | `possess.use` |

权限 `possess.use` 在插件加载时自动授予 `admin` 组。

## 实现原理（客户端角色伪装）

**灵感**：多服同步时玩家槽位错乱，玩家 B 收到玩家 A 的数据包 → B 客户端认为"自己"是 A
（外观/背包都是 A 的），但 B 的操作因鉴权失败全被拒。本插件主动复刻该机制：

### 1. 下行伪装（给管理员的出站包）

MonoMod detour `NetMessage.SendPacket`（1.4.5.7 为 public static，Compat1456 同款实证触发；
广播逐客户端调用，`remoteClient` 必为具体索引）：

- 服务端广播目标的**角色状态包**（`SyncPlayer(4)/PlayerUpdate(13)/PlayerHp(16)/PlayerAnimation(40)/PlayerMana/PlayerSlot/PlayerTeam(45)/PlayerBuff(50)`，payload[0] 均为 player index）时，发给管理员的那一份**复制数组并把 index 改成管理员自己** → 管理员客户端认为"自己"就是目标
- 丢弃发给管理员的"自己 index" 的 `PlayerUpdate`（防旧位置覆盖伪装）
- ⚠️ `SendData` 广播循环复用同一 writeBuffer → 伪装必须复制数组再改（Compat1456 实证的坑）

### 2. 上行映射（管理员 → 目标）

OTAPI.Hooks.MessageBuffer.GetData（主通道）+ MonoMod detour（兜底）：

- 管理员的 `PlayerUpdate(13)/PlayerSlot`（payload[0] 有 index）→ 改 index 为目标 → 服务端把移动/物品操作应用到目标并广播
- 无 index 的操作包（`Tile` 放块等）以管理员身份自然生效（管理员客户端渲染"自己"在目标位置 → 操作位置即目标位置）
- 原生广播 exclude 发送者（=目标）→ GameUpdate 延迟 1 帧补发 `PlayerUpdate` 给目标 → 目标看到自己被驱动

### 3. 目标冻结（寄生模式）

目标发来的**全部操作类包**（移动/放块/开门/攻击弹幕/开箱/物品栏/队伍/牌子/涂色/传送/放置/捡物品/打怪等约 28 种）直接丢弃（`Result=Cancel` + `PacketId=255`），聊天（走 82 NetModule，不在清单中）自然放行；`TimeOutTimer` 手动归零防超时踢出。

### 4. 观战/直播

- 观战/直播 = 同一套下行伪装（管理员变身为目标），但管理员操作类包被丢弃（纯观看，管理员角色冻结）、目标正常玩
- 直播 = 观战 + 活跃度统计（TShock PlayerUpdate 事件：位置变化或 control 位非零）+ 每秒检查自动切换（跳过死亡/离线/挂机）

## 通道与版本

| 通道 | 方法 | 说明 |
|------|------|------|
| 上行主通道 | `OTAPI.Hooks.MessageBuffer.GetData` | CrossTransfer 同款，1.4.5.7 update otapi 实证可靠 |
| 上行兜底 | MonoMod detour `MessageBuffer.GetData` | 3 参签名优先 / 2 参兜底 |
| 下行伪装 | MonoMod detour `NetMessage.SendPacket` | public static（1.4.5.7），Compat1456 同款 |

依赖 `plugin/api/OTAPI.dll`（与本机服务器同版，1.4.5.7 update otapi）。

## 边界

- 寄生/观战/直播为全局单实例（一个"导演"）；互斥切换
- 被寄生目标死亡/下线 → 自动退出；管理员下线 → 自动清理并恢复目标
- 弹幕（ProjectileNew 27）在寄生时由目标发出被冻结；管理员攻击的弹幕以管理员名义生成于目标位置（未改写 1.4.5.7 的 ProjectileKey 归属）
- 退出时：恢复管理员自己的 SyncPlayer + 广播目标当前状态（所有客户端位置一致）
