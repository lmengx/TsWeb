# HouseRegion 圈地保护插件（房屋系统）

> 泰拉瑞亚 TShock 服务器的房屋圈地保护插件，支持房屋创建/权限管理/违规拦截/边框显示，并内置**建筑导出导入**（`.tsb` 格式）。

- **作者**：lmx12330
- **版本**：v2.0.0
- **框架**：TShock 6.1.0（net9.0）、Terraria 1.4.4.x、TShock API 2.1
- **入口**：`Plugin.cs`（HousingPlugin）

---

## 一、功能特性

| 类别 | 说明 |
|------|------|
| 🏠 圈地建房 | 敲击选点创建房屋，继承所有house原有功能 |
| 🛡️ 全面保护 | 拦截破坏、放置、门、箱子、标牌、液体、油漆、家具、机关、布线等 20+ 类操作 |
| 💥 爆炸防护 | 炸弹/火箭/液体炸弹，全部无法绕过 |
| ⚙️ 每个房主自定义房屋 | 12 项开关：进入/传送/放置/破坏/液体/箱子/植物/复活点/挖坟/开关/门/易碎品 |
| 🔔 房主自定义通知 | 进入通知，破坏通知，可以单独设置 |
| 📦 建筑导出/导入 | 管理员可将房屋区域建筑导出为 `.tsb` 文件 / 从 `.tsb` 导入还原（跨服友好），json+base64结构任意平台可解析 |
| 🔥 热重载 | 支持  `Dispose()`触发完整卸载，命令/钩子准确注册与卸载 |

---

## 二、指令

### 主命令 `/house`（别名 `/h`，权限 `house.use`）

| 指令 | 说明 |
|------|------|
| `/h help` | 显示帮助 |
| `/h c 1` / `/h c 2` | 设置敲击点（左上角 / 右下角） |
| `/h c 屋名` | 用已选敲击点直接创建房屋 |
| `/h c clear` / `/h clear` | 清除已选敲击点 |
| `/h add 屋名` | 用已选敲击点创建房屋 |
| `/h delete [屋名]` | 删除房屋（缺省当前所在房屋） |
| `/h redefine [屋名]` | 用新敲击点重定义房屋范围 |
| `/h list [页码]` | 房屋列表（每页 15 个） |
| `/h info [屋名]` | 查看房屋完整信息（区域/传送点/权限/通知） |
| `/h name` | 敲击方块查询其所属房屋 |
| `/h tp [屋名]` | 传送到房屋（受 `AllowTP` 权限控制） |
| `/h 传送点 [屋名]` | 以当前位置设置传送点（缺省当前所在房屋，必须在房屋内） |
| `/h 驱离点 [屋名]` | 以当前位置设置驱离点（缺省选择最近房屋，必须在房屋外且距边界 ≤100 格） |
| `/h settings` / `/h set` | 查看当前房屋设置面板 |
| `/h showme` | 切换「自己房屋」进入自动显示边框 |
| `/h showothers` | 切换「他人房屋」进入自动显示边框 |
| `/h export [屋名]` | **管理员**：导出房屋区域建筑为 `.tsb` 文件 |
| `/h import [文件名]` | **管理员**：导入 `.tsb` 建筑（以玩家所在位置为中心粘贴）；无参数列出可用文件 |

### 归属管理

| 指令 | 说明 |
|------|------|
| `/h addowner 玩家 [屋名]` | 添加共有者 |
| `/h delowner 玩家 [屋名]` | 移除共有者 |
| `/h adduser 玩家 [屋名]` | 添加使用者 |
| `/h deluser 玩家 [屋名]` | 移除使用者 |

### 通知设置

| 指令 | 说明 |
|------|------|
| `/h editmsg [屋名] 0/1 [0/1]` | 0=进入通知，1=破坏通知；第三个参数为开(1)/关(0) |

### 权限快捷设置（15 项）

```
/h [屋名] 项目名 0/1      # 指定房屋
/h 项目名 0/1             # 当前所在房屋
```

项目名：`进入` `传送` `放置` `破坏` `液体` `箱子` `植物` `复活点` `挖坟` `开关` `门` `易碎品` `违规驱离` `破坏通知` `进入通知`

### 其他

| 指令 | 权限 | 说明 |
|------|------|------|
| `/htp 屋名` | 无 | 快速传送到房屋 |

---

## 三、权限节点

| 权限 | 说明 |
|------|------|
| `house.use` | 使用 `/house` `/h` 命令 |
| `house.edit` | 编辑房屋（拥有者级别通行） |
| `house.admin` | 管理员：删除/修改他人房屋、导入导出、分享管理 |
| `house.count.N` | 房屋数量上限（N 为数字，默认 2） |
| `house.size.N` | 房屋最大面积（N 为数字，默认 1000） |
| `house.bypasscount` | 绕过房屋数量限制 |
| `house.bypasssize` | 绕过房屋面积限制 |

---

## 四、配置

### `tshock/HouseRegion.json`（房屋限制）
| 字段 | 默认 | 说明 |
|------|------|------|
| `房屋最小宽度` | 15 | 房屋最小宽度（格） |
| `房屋最小高度` | 10 | 房屋最小高度（格） |

### `tshock/HouseRegion/houseshow.json`（边框显示偏好）
| 字段 | 默认 | 说明 |
|------|------|------|
| `sm` | false | 自己房屋自动显示边框 |
| `so` | true | 他人房屋自动显示边框 |

---

## 五、数据存储

- **数据库**：`tshock/HouseRegion.sqlite`，表 `HousingDistrict`
  - 区域：`TopX/TopY/Width/Height`
  - 归属：`Author`（房主 ID）、`Owners`（共有者 ID 逗号分隔）、`Users`（使用者 ID 逗号分隔）
  - 坐标：`TpX/TpY`（传送点）、`ExpelX/ExpelY`（驱离点，可空）
  - 开关：`ExpelOnViolate`、`NotifyBreakPlace`、`NotifyEnter`、`AllowEntry/AllowTP/AllowPlace/AllowBreak/AllowLiquid/AllowChest/AllowPlant/AllowSpawn/AllowGrave/AllowSwitch/AllowDoor/AllowFragile`
  - 按 `WorldID` 区分世界
- **建筑文件**：`tshock/TSWeb/Buildings/{屋名}_{时间戳}.tsb`

---

## 六、建筑导出 / 导入（`.tsb`）

管理员（`house.admin`）可用，实现房屋建筑（方块 + 实体）的跨服传递与归档。

### 指令
```text
/h export [屋名]        # 导出当前/指定房屋区域建筑 → .tsb 文件
/h import               # 列出 Buildings 目录下所有 .tsb 文件
/h import <文件名>      # 导入 .tsb 建筑（以玩家为中心粘贴，覆盖目标区域）
```

### 导出内容
- **方块数据**：raw14 定长编码（14 字节/格）+ gzip + base64 + SHA-256 校验
- **实体数据**：13 种（箱子含物品槽位、标牌文字、物品框、武器架、食物托盘、展示假人、帽子架、逻辑感应器、传送晶塔、训练假人、死细胞罐、风筝锚、生物锚）
- **元数据**：建筑名、作者、导出时间、来源世界/种子/版本
- **兼容信息**：maxTileId/maxWallId/maxItemId、是否需要执行器/电线

### 格式规范
遵循项目内《建筑文件格式规范 tsweb-building》（`scripts/建筑文件格式规范_tsweb-building.md`），Web 友好（JSON 外壳），导入前执行 L0~L3 恶意数据校验（格式/尺寸/校验和/保留位/实体合法性/文本注入）。

### 导入还原流程
`清除目标区域旧实体 → 写入方块 → 重建实体骨架 → 还原实体数据 → 刷新帧图 + 全量网络同步`

---

## 七、保护机制（技术实现）

- **数据包拦截**：`Hooks.MessageBuffer.InvokeGetData` 拦截 20+ 种数据包（Tile/DoorUse/Chest*/Sign/Liquid/Paint/PlaceObject/PlaceTileEntity/ItemFrame/WeaponsRack/FoodPlatter/TileEntity 交互/帽架/宝石锁/MassWire/PlayerSlot/PlayerSpawn）
- **爆炸防护**：`On.Terraria.Projectile.Kill` 追踪爆炸来源 + `On.Terraria.WorldGen.KillTile` 校验爆炸破坏权限
- **箱子相关**：OTAPI `Chest.QuickStack`、`CraftingRequests.CanCraftFromChest` 防护快速堆叠/合成
- **违规处理**：拦截 + 蛛网冻结（200 tick）+ 可选通知房主 + 可选驱离（违规驱离开启时）
- **进入/离开检测**：1100ms 定时器轮询玩家位置，触发进入/离开提示、通知、边框显示
- **边框显示**：黄玉法弹（TopazBolt）绘制，进入房屋/圈地预览时显示**一次**，**2 秒后自动消失**（一次性计时协程 + 代次作废，`Main.DelayedProcesses`），离开房屋立即精确清除，掉线自动清理

---

## 八、热重载支持

- 注册的所有命令持有缓存引用（`_houseCmd`/`_hCmd`/`_htpCmd`），`Dispose` 时精确移除
- `Initialize` 重置静态状态（`GetDataHandlers.ResetState`），钩子先减后加避免重复注册
- `/reload` 时 `Config.Load` 重新读取配置

---

## 九、更新日志

### v2.0.0
- 重构为独立仓库版（net9.0 / TShock 6.1.0）
- 新增：建筑导出/导入（`.tsb`，管理员）
- 新增：`/h export` `/h import` 命令
- 新增：`/h 传送点`、`/h 驱离点`、`/h editmsg`、`/h showme`、`/h showothers`
- 新增：边框自动显示（黄玉法弹 + 2 秒自动消失）
- 新增：爆炸/液体炸弹防护、箱子快速堆叠与合成防护
- 新增：15 项权限快捷设置（`/h 项目名 0/1`）

### v1.x（社区版）
- 基础圈地、权限分享、箱子/家具保护、传送房屋、显示区域
