# Spectate —— 直播观战插件（占卜球原生观战 + 自动切换活跃玩家）

管理员专用直播插件。**Terraria 原生观战系统（ScryingOrb 占卜球 + SpectatePlayer 150 包）+ 挂机自动切换（排除挂机）**。

## 功能

| 命令 | 说明 |
|------|------|
| `/slive [秒数]` | 开启直播（自动切换活跃玩家，排除挂机，默认 10 秒挂机切换，可调 1~300 秒） |
| `/slive off` | 退出直播 |

权限节点：`spectate.use`（Initialize 时自动授予 admin 组）。

提示文本：**只有开关提示**（`[slive]启动` / `[slive]结束`），切换过程无任何提示。

## 核心机制（v1.2 精简版）

### 1) 进入直播（`/slive` → `PrepareViewing`）
给直播者发一个**真实占卜球 ScryingOrb(5644)** 到快捷栏（空槽优先，满了顶替第 9 格，退出时恢复原物品）。必须放快捷栏，因为客户端 `ItemCheck` 只处理选中格。直播者点击占卜球 → 客户端原生触发 `SpectateNextPlayer` → 客户端主动进入原生观战（spectating ≥ 0），相机每帧跟随目标实体，零发包平滑跟随不卡顿。

### 2) 服务端接管（`OnGameUpdate` 帧驱动）
检测到 `spectating >= 0` 后接管：开 **GodMode**（防直播者角色站原地被打死）+ 发 `SpectatePlayer(150)` 包把观战目标切到指定玩家。直播者按 Esc/任意键退出观战视角（客户端原版行为）→ 服务端检测到后**保持会话**回到待进入，可再点占卜球恢复。

### 3) 自动切换（核心）
- **活跃度统计**：监听 TShock `PlayerUpdate` 事件，玩家"移动了"或"按了移动/跳跃/使用物品键"算活跃，记录最近活跃时间（直播者自己不计）。
- **目标死亡/下线 → 立即自动切换**：找下一位存活玩家（whoAmI 递增循环，跳过自己）。无存活玩家时保持状态等待，玩家复活/上线后自动切过去。
- **挂机超阈值（默认 10 秒）→ 每秒检查自动切换**：只挑"存活且未挂机"的玩家，排除挂机。
- 直播是**持续状态**：只有 `/slive off` 或直播者本人下线才结束。
- **手动切换同步**：直播者按左右键手动切目标后，服务端在 1 秒锁定期外同步目标记录，避免把服务端刚切的目标误判成手动切换。
- **KeepAlive**：spectating 时客户端停止上报 → 每 tick 归零超时计时器防被踢出。

### 4) 退出（`StopViewing`）
发 150(-1) 退出原生观战 → 关 GodMode → 恢复被占卜球顶替的快捷栏物品。

## v1.2 精简说明（删掉了什么、为什么）

| 删除项 | 原作用 | 删除原因 |
|--------|--------|----------|
| 上行拦截（OTAPI GetData 事件 + MonoMod GetData detour 双通道） | 丢弃直播者上行的角色数据包/操作包 | 全局每个上行包都被 detour 包装，满员服务器累积开销 → **卡顿** |
| `BlockedOpPackets`（含 ProjectileNew 27 / ProjectileDestroy 29 等） | 防客户端反向上传污染 | 无条件丢弃直播者上行的**射弹创建/销毁包** → **射弹丢失** |
| 无痕隐身（`ApplyStealth`/`ForceStealth` + SendData detour 防恢复） | 直播者角色不可见 | SendData detour 委托签名（`object text`）与真实签名（`NetworkText text`）不匹配，**从未生效**；隐身本身复杂化 |
| 伪装数据推送（虚拟登录） | 观战者看到目标背包/装备 | 原生观战本身跟随目标实体并显示目标状态，无需伪装；且伪装是上行拦截存在的根因 |

精简后直播者观战期间角色**可见**、留在原地（不再隐身），退出直播恢复。代码从 766 行减至约 460 行，无任何 detour/反射，纯事件驱动。

## 依赖与限制

- **必须开启 SSC**：非 SSC 时客户端忽略"自己 index"的 PlayerControls/SyncEquipment 包，占卜球进入不生效。启动日志会警告。
- 直播者角色以正常状态留在服务器上（不再幽灵化/隐身），退出直播时恢复实体 + 关 GodMode + 恢复快捷栏。
- 目标死亡 → 自动切换下一位存活玩家；无存活玩家 → 保持直播状态等待；只有 `/slive off` 或直播者本人下线才结束直播。
- 直播者按 Esc/任意键会退出原生观战视角（客户端原版行为），服务端检测到后保持会话，可再点占卜球恢复。

## 构建与部署

```
dotnet build plugin-son/Spectate/Spectate.csproj -c Release
产物：plugin-son/Spectate/bin/Release/net9.0/Spectate.dll
```

**部署**：把 `Spectate.dll` 拷贝到服务器 `ServerPlugins/` 目录，**重启服务器**生效。

引用：TShock NuGet 6.1.0 + `plugin/api/OTAPI.dll`（1.4.5.7 update otapi，与本机主 plugin 同版）。

## 关键源码依据

- ScryingOrb(5644) 使用触发观战：`_spectate_ref/Terraria_Player.cs:44031-44051`（`sItem.type == 5644 → SpectateNextPlayer(1, false)`）
- 客户端 SpectatePlayer(150) 处理（首观限制）：`_spectate_ref/Terraria_MessageBuffer.cs:4387-4411`
- 客户端 `SetOrRequestSpectating`（占位+上行）：`_spectate_ref/Terraria_Player.cs:17344-17367`
- spectating 时输入清零：`_spectate_ref/Terraria_Player.cs:25001-25009`
