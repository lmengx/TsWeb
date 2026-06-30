# ChestFix

TShock 宝箱安全修复插件。修复原生 TShock 对宝箱数据包缺少校验的问题，防止恶意客户端通过网络包写入越界/非法数据到世界宝箱。

## 修复的漏洞

TShock 的 `HandleChestItem` (ChestItem 包处理) 仅校验了堆叠数量上限，**未校验以下字段**：

| 缺失校验 | 攻击效果 |
|---|---|
| slot 边界 (± 40) | 写入 slot=255 可触发数组越界，严重时崩服 |
| chest ID 存在性 | 空引用导致 NPE |
| item type 有效范围 | 写入非法物品 ID 到宝箱 |
| prefix 非负 | 写入非法词缀 |
| stacks 非负 | 写入负堆叠数 |

同时 `HandleChestActive` (ChestOpen 包) **没有任何事件暴露给插件**，恶意客户端可发送任意宝箱 ID。

## 防御机制

### 运行时拦截

- **ChestItem** 包：通过 `GetDataHandlers.ChestItemChange` 事件，6 项校验全部通过才放行
- **ChestOpen** 包：通过 `ServerApi.Hooks.NetGetData` 最高优先级拦截，校验宝箱 ID 合法性
- **ChestGetContents** 包：校验坐标是否对应有效宝箱

触发拦截的玩家将被**自动永久封禁**并踢出。

### 世界扫描修复

已受污染的世界存档可通过指令扫描并修复：

- 数组长度异常（>40）→ 截断为 40 并重置 `maxItems`
- 孤悬宝箱（方块已拆但数据残留）→ 置空
- 同坐标重复宝箱 → 保留第一个，其余置空
- 脏数据槽位（非法 type/stack/prefix）→ 清空为空气

## 指令

| 指令 | 别名 | 功能 |
|---|---|---|
| `/chestfix` | `/cstf` | 显示状态 |
| `/chestfix reset` | `/cstf reset` | 重置拦截计数 |
| `/chestfix scan` | `/cstf scan` | 扫描世界宝箱异常数据（预览） |
| `/chestfix scan fix` | `/cstf scan fix` | 扫描并自动修复 |

权限：`chestfix.admin`

依赖：TShock 6.1.0
