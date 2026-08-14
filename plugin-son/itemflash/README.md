# ItemFlash 掉落物献祭插件

> TShock 子插件（TShock 6.1.0 / Terraria 1.4.5.6 / net9.0）
> 玩家把指定物品丢在一起 → 判定成功 → 主角物品做动画后消失。

## 功能

- **组合判定**：检测服务端掉落物，玩家把配方所需物品丢在同一区域（默认 5 格内）即判定成功
- **来源过滤**：只认玩家丢出的物品（通过 `GetDataHandlers.ItemDrop` 登记），NPC 掉落物不参与，避免误触发
- **献祭动画**：主角物品（如土块）锁定防捡 → 升空 1 秒（200px）→ 伴随金色粒子 → 消失；其他材料（如金币）静默消失
- **可配置配方**：JSON 配置文件，支持多配方，`/reload` 热重载

## 默认配方

把 **1 个土块 + 2 个金币** 丢在一起（相距 5 格内），土块升天动画后消失，金币一并消耗。

## 安装

1. `dotnet build` 编译（TShock 6.1.0 官方 NuGet 包）
2. 将 `ItemFlash.dll` 放入服务器的 `ServerPlugins/`
3. 重启服务器（或热重载）
4. 首次运行自动生成配置 `{TShock.SavePath}/ItemFlash/config.json`

## 配置（config.json）

```json
{
  "enabled": true,
  "clusterRange": 80,
  "recordWindowSeconds": 60,
  "recipes": [
    {
      "name": "土块献祭",
      "items": [
        { "itemId": 2, "count": 1 },
        { "itemId": 73, "count": 2 }
      ],
      "animateItemId": 2,
      "message": "献祭成功！土块带着金币升天啦"
    }
  ]
}
```

| 字段 | 说明 |
|------|------|
| `enabled` | 总开关 |
| `clusterRange` | 聚类判定距离（像素，1 格 = 16px；默认 80 ≈ 5 格） |
| `recordWindowSeconds` | 玩家丢物记录有效期（秒）。超过此时长的丢物记录不再参与判定 |
| `recipes[].name` | 配方名（日志用） |
| `recipes[].items` | 所需材料组合，`itemId` 物品 ID（2=土块，73=金币），`count` 所需数量（按掉落物 stack 累计） |
| `recipes[].animateItemId` | 做动画的主角物品 ID（必须出现在 items 中，不填则取 items[0]） |
| `recipes[].message` | 触发成功的玩家提示，空字符串则不提示 |

**匹配规则**：聚类区域内只能出现配方涉及的类型（出现其他类型物品则不匹配），且每种材料数量 ≥ 所需。例如默认配方下"土块 + 3 金币"成功（消耗全部 3 金币），"土块 + 金币 + 火把"失败。

## 判定与动画原理

### 检测
- 玩家丢物品 → 客户端发 **21 号包（ItemDrop）** → TShock `GetDataHandlers.ItemDrop` 事件 → 登记（玩家/类型/位置/时间），不拦截
- 每 10 tick（约 0.17 秒）扫描 `Main.item[]`（服务端权威掉落物表）：
  1. 过滤"落地静止"的掉落物（velocity < 1）
  2. 匹配丢物登记（同类型、时间窗内、位置 6 格内）→ 关联到丢它的玩家
  3. 按玩家分组 → 空间贪心聚类（距离 ≤ clusterRange）→ 配方比对

### 动画
- **防捡**：`keepTime = 600`（掉落物 `FindOwner` 在 keepTime>0 时直接返回，玩家无法拾取）
- **升空**：每帧由服务端直接控制 `position` 线性抬升（不依赖物理），每 3 帧广播一次 21 号包同步位置
- **粒子**：每 10 帧广播一次 **82 号 NetModule 粒子包**（`ItemTransfer` 金色特效）。服务端广播直接写 socket、不经 `NetManager.Read`，不受主插件 ParticleGuard 防线拦截
- **消失**：`TurnToAir()`（type=0）+ 广播 21 号包 → 客户端 `SetDefaults(0)` → `active=false` → 掉落物消失（协议实证：`Item.active => type != 0`）

## 版本历史

- **v1.0.0**：首个版本。玩家丢物登记 + 轮询聚类匹配 + 主角升空动画 + ItemTransfer 粒子 + keepTime 防捡 + 21 号包消失同步；JSON 多配方配置，`/reload` 热重载
