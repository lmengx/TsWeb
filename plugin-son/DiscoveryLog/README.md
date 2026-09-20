# DiscoveryLog —— 探索物品发现日志

> 适用：Terraria **1.4.5.7** + TShock 6.x 服务器（TShockAPI 6.1.0 / OTAPI3）
> 类型：独立 TShock 插件（可独立加载，不依赖 TSWeb 主插件）
> 编译目标：net9.0

---

## 一、功能

对重要探索物品的**自然获取事件**挂钩并公屏记日志，形成合规获取的证据链
（供后续判定玩家背包中对应物品是否来源合理）。

**只做事件日志，不做合理性判定、不做背包扫描。**

### 监控物品（写死）

| 物品 | 物品 ID | 世界形态 | 合规获取方式 | 事件挂钩 |
|------|---------|---------|-------------|---------|
| 生命水晶 | 29 | 图格 12 (Heart) | 挖掘 | `GetDataHandlers.TileEdit` |
| 龙蛋 | 6142 | 图格 752 (PalworldChilletEgg) | 挖掘 | `GetDataHandlers.TileEdit` |
| 捣蛋猫 | 5663 | 受困 NPC 695 | 对话救助 | `GetDataHandlers.NpcTalk` |
| 火绒狐 | 5664 | 受困 NPC 696 | 对话救助 | `GetDataHandlers.NpcTalk` |

> 注：龙蛋为 Palworld 联动的 Chillet 蛋（物品 6142 / 图格 752），世界中自然生成，
> 与生命水晶同类（挖掘图格获得）。不是旧神军团 Betsy 的宠物蛋（3857）。

---

## 二、实现原理（本地源码实证）

### 2.1 挖掘事件（生命水晶 / 龙蛋）

- 世界中生命水晶以 **TileID 12 (Heart)** 方块形式放置（`WorldGen.AddLifeCrystal`），
  龙蛋以 **TileID 752 (PalworldChilletEgg)** 图格放置
- 玩家挖矿 → 客户端发 `PacketTypes.Tile` 包（`EditAction.KillTile`）
- TShock `HandleTile` 解析后触发 **`GetDataHandlers.TileEdit`** 事件
- 判定：`Action ∈ {KillTile, KillTileNoItem, TryKillTile}` 且
  `Main.tile[X,Y].type ∈ {12, 752}`
- 关键：事件触发于方块**移除前**，`Main.tile[X,Y].type` 仍是原类型，判定可靠；
  不用包内 `EditData` 字段（挖掘时不可靠）

### 2.2 救助事件（捣蛋猫 / 火绒狐）

- 1.4.5 Palworld 联动：受困 Pal NPC 在野外生成
  （**NPCID 695** PalworldCattivaDistressed / **NPCID 696** PalworldFoxsparksDistressed）
- 玩家对话 → 客户端发 `PacketTypes.NpcTalk` 包 → TShock 触发 **`GetDataHandlers.NpcTalk`** 事件
- 判定：`Main.npc[NPCTalkTarget].type ∈ {695, 696}`
- 关键：NpcTalk 触发时 NPC 仍是受困态（之后游戏侧 `AI_000_TransformBoundNPC`
  才转为城镇形态并给物品），type 判定时机正确

### 2.3 与房屋系统的兼容

TSWeb 房屋系统以 `ServerApi.Hooks.NetGetData`（int.MaxValue，先于 TShock.OnGetData）
拦截 Tile 包，拦截（Handled=true）后 TShock 的 `HandleTile` 不执行 →
TileEdit 事件不触发 → **房屋内无权破坏不产生日志**（符合「合规事件」语义）。

### 2.4 去重

同玩家 + 同物品 + 同坐标在 **3 秒窗口**内只记一次（多格图格 / 连挖触发多个包）。

---

## 三、日志输出

公屏广播（LimeGreen）+ 控制台日志：

```
[发现] PlayerA 挖掘了 生命水晶 (ID:29) @ (1234, 567)
[发现] PlayerB 救助了 捣蛋猫 (ID:5663) @ (800, 300)
```

---

## 四、部署

1. 编译：`dotnet build plugin-son/DiscoveryLog/DiscoveryLog.csproj -c Release`
2. 将 `bin/Release/net9.0/DiscoveryLog.dll` 放入服务器 `ServerPlugins/` 目录
3. 重启服务器进程

启动日志：`[DiscoveryLog] 探索物品发现日志已启用 (生命水晶/龙蛋挖掘 + 捣蛋猫/火绒狐救助)`

---

## 五、测试指令（权限 tshock.admin）

### `/dlog spawn <cattiva|foxparks>`

在指令者附近生成受困捣蛋猫(695) / 受困火绒狐(696)，用于实测救助日志：
玩家走过去对话 → 触发 NpcTalk 事件 → 公屏输出救助日志。

### `/dlog findegg`

全图扫描 `Main.tile[x,y].type == 752` 的龙蛋图格，输出总数与前 20 个坐标，
用于实测挖掘日志。

---

## 六、边界情况

| # | 情况 | 处理 |
|---|------|------|
| 1 | 房屋区域内挖掘被房屋系统拦截 | TileEdit 事件不触发 → 不记录（合规语义） |
| 2 | 服务器端破坏（爆炸、命令）不经过 Tile 包 | 不记录（只追踪玩家主动挖掘/救助） |
| 3 | 多格图格 / 连续挖掘触发多个包 | 3 秒窗口去重 |
| 4 | 玩家未登录（游客） | 仍记录玩家名（事件证据不依赖账号） |
| 5 | 救助判定时机 | NpcTalk 触发时 NPC 仍是受困态，type 判定可靠 |
| 6 | 龙蛋图格具体掉落是否总是 6142 | 挖掘判定只认图格类型 752，不依赖掉落物 |

---

## 七、兼容性说明

插件按 TShock 6.1.0 NuGet（Terraria **1.4.4.x** API）编译，运行时为 **1.4.5.7** 服务端。
Palworld 联动常量（图格 752、NPC 695/696、物品 6142/5663/5664）在 1.4.4.x 编译期
API 中不存在，一律使用**裸数字常量**（运行时数值有效）。
