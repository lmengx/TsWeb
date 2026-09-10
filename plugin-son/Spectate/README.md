# Spectate —— 虚拟登录观战 / 直播插件

管理员专用观战插件。**Terraria 原生观战系统（ScryingOrb 占卜球 + SpectatePlayer 150 包）+ 服务端伪装数据推送 + 无敌 + 直播自动切换（排除挂机）**。

## 版本历史与方案取舍

| 版本 | 方案 | 结果 |
|------|------|------|
| v1 | case 3 身份切换（`Main.myPlayer = 目标`） | 已废弃：本地存档污染（背包被保存）、目标受伤误判观战者死亡、卡顿 |
| v2 | 自研伪装 PlayerControls 间隔发包同步位置 | 已废弃：相机位置离散更新 → 观战视角非常卡顿（用户实测） |
| v3（当前） | **占卜球原生观战 + 伪装数据推送** | 原生 spectating 相机每帧跟随目标实体（零发包、平滑）；伪装数据让观战者看到目标背包/装备/HUD |

## 核心机制

### 1) 真正自动进入观战（ScryingOrb hack）
客户端"自己"（Main.myPlayer）收到服务端伪装 SyncEquipment 后背包槽 0 是 **ScryingOrb(5644)**；服务端再发 PlayerUpdate(13, controlUseItem=true) 驱动客户端使用占卜球 → 客户端本地 ItemCheck 触发 `SpectateNextPlayer` → **主动进入观战**（spectating >= 0）→ 客户端发 150 上行 → 服务端广播 → 观战生效。随后服务端发 SpectatePlayer(150, 观战者, 目标) 把观战目标切到指定玩家。**未生效时每 30 tick 重试**（`Main.player[观战者].spectating` 服务端字段可检测）。

### 2) 为什么不再卡顿
原生 spectating 时客户端**相机每帧直接读取目标实体位置渲染**（零发包、平滑跟随），同时**跳过"自己"的物理模拟与输入**（controlUp/Left/Down/Right/Jump 清零）。v2 的"发包同步位置"（相机离散更新）是卡顿根源，v3 完全消除。

### 3) 虚拟登录（看到目标全部状态）
服务端低频推送伪装数据包（payload[0]=观战者自己 index，内容=目标数据）：
- SyncPlayer(4) 外观 / SyncEquipment(5) 背包 59 格 + 装备 + 染料 + 饰品 + 时装 + 3 套配装
- PlayerHp(16) 血量（钳制最小 1 防本地死亡误判）/ PlayerMana(42) 蓝量 / PlayerBuff(50) buff / PlayerTeam(45) 队伍 / PlayerAnimation(41) 动作
- **不推 PlayerControls(13) 位置**（相机由原生观战跟随目标实体）

### 4) 无法操控 + 防污染
上行拦截（OTAPI GetData 事件 + MonoMod detour 双通道）：
- **角色数据包** PlayerInfo(4)/PlayerSlot(5)/PlayerHp(16)/PlayerMana(42)/PlayerBuff(50)/SyncLoadout(147)/RequestWorldData(6) **丢弃**：观战者本地"自己"背包被伪装成目标后，客户端反向上传会被丢弃 → 服务器端观战者账号零污染（SSC 下 TShock 还忽略上传，双保险）
- **操作类包** Tile/Projectile/ItemDrop/PlayerSpawn/Teleport 等 **丢弃**：无法操控
- **PlayerControls(13)/SpectatePlayer(150) 放行**：spectating 时客户端输入清零（上行空输入无操作）；150 上行放行让服务端同步观战状态（观战者按 Esc 退出观战时服务端感知并自动恢复）
- **KeepAlive**：spectating 时客户端停止上报 → 每 tick 保活防超时踢出

### 5) 无敌
进入观战：观战者服务器角色 **ghost 化**（NPC 不攻击、穿墙）+ **TShock GodMode**（免疫伤害，防环境伤害/意外）；退出观战恢复。

### 6) 直播（自动切换，持续状态，排除挂机）
活跃度统计（TShock PlayerUpdate 事件：移动/按键）+ 每秒检查：目标死亡/下线/挂机超阈值（默认 10s）→ **自动切换下一位存活且未挂机的玩家（排除挂机）**；全员死亡/离线时保持直播状态等待（**提示带 30 秒冷却，防左下角刷屏**），有玩家复活/上线自动切过去；只有 `/slive off` 或观战者本人下线才结束直播。

## 命令与权限

| 命令 | 说明 |
|------|------|
| `/spectate <玩家名>` | 虚拟登录观战指定在线玩家（原生观战视角） |
| `/spectate next` | 切换下一位存活玩家 |
| `/spectate stop` | 退出观战 |
| `/slive [秒数]` | 开启直播（自动切换活跃玩家，排除挂机，默认 10s 挂机切换） |
| `/slive off` | 退出直播 |

权限节点：`spectate.use`（Initialize 时自动授予 admin 组）。

## 依赖与限制

- **必须开启 SSC**：非 SSC 时客户端忽略"自己 index"的 PlayerControls/SyncEquipment 包（`num121 == Main.myPlayer && !Main.ServerSideCharacter`），占卜球进入与伪装数据都不会生效。启动日志会警告。
- 观战者角色以幽灵形式留在服务器上（其他玩家可见半透明幽灵），退出时恢复实体 + GodMode 关闭 + 恢复自己数据。
- 目标死亡 → 直播/观战自动切换下一位存活玩家（直播排除挂机）；观战模式无存活玩家 → 退出；直播模式保持状态等待。
- 观战者按 Esc/任意键会退出原生观战视角（客户端原版行为），服务端检测到后自动恢复观战者角色（观战结束）。

## 构建与部署

```
dotnet build plugin-son/Spectate/Spectate.csproj -c Release
产物：plugin-son/Spectate/bin/Release/net9.0/Spectate.dll
```

**部署**：把 `Spectate.dll` 拷贝到服务器 `ServerPlugins/` 目录，**重启服务器**生效（热重载对带 OTAPI 钩子的插件不可靠）。

引用：TShock NuGet 6.1.0 + `plugin/api/OTAPI.dll`（1.4.5.7 update otapi，与本机主 plugin 同版）。

## 关键源码依据

- ScryingOrb(5644) 使用触发观战：`_spectate_ref/Terraria_Player.cs:44031-44051`（`sItem.type == 5644 → SpectateNextPlayer(1, false)`）
- 客户端 SpectatePlayer(150) 处理（首观限制）：`_spectate_ref/Terraria_MessageBuffer.cs:4387-4411`（`player6 != Main.LocalPlayer || player6.spectating >= 0`，两个独立反编译一致）
- 客户端 `SetOrRequestSpectating`（占位+上行）：`_spectate_ref/Terraria_Player.cs:17344-17367`
- spectating 时输入清零：`_spectate_ref/Terraria_Player.cs:25001-25009`
- ghost 跳过物理模拟：`_spectate_ref/Terraria_Player.cs:24936-24940`
- SSC 忽略客户端上传：TShock `GetDataHandlers.HandlePlayerSlot`（`参考源码/TShock-general-devel/TShockAPI/GetDataHandlers.cs:2741`）
- 玩家退出保存（CopyCharacter）：TShock `TShock.cs:1452-1456`、`PlayerData.cs:128`
