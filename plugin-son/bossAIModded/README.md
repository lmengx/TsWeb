# bossAIModded

将 **Fargo Souls（Eternity Mode）** 的 Boss 魔改按"原版客户端可达"原则迁移到 **TShock + Terraria 1.4.5.8** 的独立子插件。

当前 Boss：**史莱姆王（KingSlime）**、**克眼（Eye of Cthulhu）** 均实现 Eternity-lite。

## 设计原则

迁移判定三层模型（详见仓库 Fargo 移植研究报告）：

| 层 | 能否搬 | 处理 |
|---|---|---|
| 判定/数值层（何时做什么、改 ai[]/velocity/life） | ✅ | 照搬，客户端跑原版 AI 看到的就是"它本来就会这样" |
| 内容层（自定义 NPC/弹幕/Buff） | ⚠️ | 换壳为原版 ID |
| 视觉层（shader/Dust/粒子/自定义音效/震屏） | ❌ | 不迁移（原版客户端无消费能力） |

架构限制：插件挂在 `NpcAIUpdate` **后插桩**（原版 AI 已执行完），**无法像 Fargo `SafePreAI return false` 一样冻结原版 AI**。凡是 Fargo 需要"完全接管状态机"的机制（瞬移重写/锁血死亡演出/终局三段循环）都只能降级或留待"接管引擎"。史莱姆王的 `ai[0]=-999` 冻结属于原版 KS AI 恰好对非法值空转的巧合，不通用。

## Boss 1：史莱姆王 Eternity-lite（对照 FargoSouls v1.7.3.9 KingSlime 类）

### 已实现
- **掉血召唤波**：血量每跌破 1/6（共 5 波，冷却 3 秒）体内爆出 6 只尖刺史莱姆（换壳：原版 `SlimeSpiked`=535，原版 KS 战原生小怪）
- **普通跳跃弹道修正**：目标在头顶 240px 以上跳得更高；水平距离越远横向速度乘区 1~3 倍 + 侧向推力（Fargo 数值原样）
- **落地蓄力大跳**：特定状态（ai[2] 钳制 + 跳计时满 900）落地后 60 tick 前摇（写 `ai[0]=-999` 冻结原版 AI）→ 超远猛跳（-18 垂直初速 + 1000px 水平预判玩家位置）；飞行中每 5 tick 脚下落刺、过头自动取消归还原版状态机
- **狂暴尖刺雨**：血量 <66% 且非大跳时，每 240 tick 以玩家为中心上方 500px 铺 25 列尖刺下落（换壳：原版 `SpikedSlimeSpike`=605）
- **接触黏液**：碰撞期间持续上 `Slimed`(137) 减速（原版 Buff，客户端完整显示）
- **防卡墙瞬移抑制**：`ai[2]≥145` 一律钉 145 + 跳计时顶满（防原版 300 触发"隐身+瞬移"，卡墙局面改由自家大跳追人）——原版 KS 只有 [145,150) 窄窗会脱锚，这里全盖

### 已知降级
- 换色 shader / 忍者头饰 / Boss 头图 / 蓝色 Dust / 粒子演出 / 屏幕震动 / 自定义音效：不迁移
- Fargo 的 Masochist 难度分支与 Mutant Boss 联动：不迁移
- 召唤小怪/弹幕伤害已改为“期望结算”查表填写（见 BossAIModBase.FieldForResult），不再依赖 `npc.defDamage`（大师+属性强化下 defDamage 被放大是历史失控根源）
- **无死亡延迟演出**（Fargo 的 300tick 濒死锁血"聚能爆炸秀"依赖 CheckDead 拦截 + AI 冻结，后插桩无法复刻，已按原版正常死亡）

### 实机待调参点（KingSlimeEternity.cs 顶部常量）
`SummonWaveCount / SummonCooldown / SpecialJumpWindup / SpecialJumpVY / SpecialJumpPredictRange / SpikeRainInterval / BerserkLifeRatio / SpikeHitDamage` 等。
**伤害校准**：尖刺期望单发结算写 `SpikeHitDamage`（默认 80）；网络字段 = 期望 ÷ `BossAIModBase.ResultBias`（纯 vanilla 源码链路为 2f，本服实测 14/3≈4.667），换环境只需调 `ResultBias`。
**依赖 1.4.5.8 原版 KS AI 数值的点**（如 `ai[2]∈[145,150)` 窗口、`ai[0]=-999` 冻结、落地判定 `velocity.Y==0`）已按 Fargo 源码原样移植，若实机与预期不符优先微调这些常量与 `ai[2]` 判断。

## Boss 2：克眼 Eternity-lite（对照 FargoSouls v1.7.3.9 EyeofCthulhu 类）

原版克眼 AI（aiStyle 4）状态备忘：`ai[0]==0` 一阶段（盘旋/召仆从/3 连 dash）；半血变身 → `ai[0]==3` 二阶段连续 dash（ai[1] 0 贴位/1 起冲/2 滑行/3 预瞄/4 冲刺/5 低空迂回）。

### 已实现（后插桩安全子集）
- **dash 滑行漂移**：`position += velocity × k`（距离越远越飘；一阶段 0.15~0.5 距离 lerp，二阶段 0.5；Fargo 数值）→ 冲刺更难瞄准；漂移期间每 2 tick 主动推 23 防顿挫
- **dash 撒镰**：滑行期每 N tick 沿速度方向掷一枚镰刀弹（换壳：原版 `DemonSickle`=44，hostile 敌弹；⚠45 DemonScythe 是 friendly 玩家弹会反打克眼）；一阶段 6 / 二阶段 4 / ≤10% 血 2 tick/发（Fargo: 6/6→2 final）；撞墙即消失（44 原生 tileCollide=true 且 27 网络包不同步该字段，无法服务端改穿墙——定案接受）；**伤害查表化**：期望单发结算 `ScytheHitDamage=70`，由 `FieldForResult(期望)` 按客户端判伤链路反算网络字段，不再依赖 defDamage（原 damage 字段上限 ≤30→≈60 的旧设计已废）
- **预瞄镰刀环**：二阶段每次进入预瞄(`ai[1]==3`)以中心放一圈镰刀弹（8 向；≤10% 血升 12 向；Fargo: XWay 8 一次/CD），同上伤害查表
- **低血狂化**：≤10% 血时 Fargo 的终局撒镰加密（2 tick/发）+ 环加密，不冻结原版 AI
- **命中 debuff**：克眼本体接触 或 己方镰刀弹命中玩家 → 中毒+着火+破损盔甲（原版 `Poisoned`20/`OnFire`24/`BrokenArmor`36）各 5 秒（Fargo 接触 debuff 为自定义 CurseOfTheMoon/Berserked，换壳为原版三连）

### 未迁移（详见类头注释）
- 换色 shader / 换色 Boss 头 / Dust 229/266 / AddLight / ForceRoar 音效：视觉层，不迁移
- **SpectralEoC 幽灵复制体**（半透明假克眼误导）：原版客户端无此"无伤害贴图实体"组合，删除
- **GlowRing 光环**（全屏 shader）：删除
- **二阶段"消失→瞬移→显形强冲"重写** 与 **≤10% 终局完整三段循环**（消失→瞬移四角→狂化冲刺×N 轮）：Fargo 用 SafePreAI return false 冻结原版 AI 完全接管，后插桩无法复刻 → 留待"接管引擎"再迁
- 接触附加 debuff：Fargo 自定义（CurseOfTheMoon / Berserked）无原版等价 → 已换壳原版 中毒/着火/破损盔甲 5 秒（见上）
- Masochist 分支（补召仆从/Shadowflame）：不迁

### 实机待调参点（EyeOfCthulhu.cs 顶部常量）
`DriftKPhase1/2 / ScytheEveryPhase1/2 / ScytheEveryBerserk / ScytheSpeed / ScytheHitDamage / RingWays / RingWaysBerserk / BerserkLifeRatio / DebuffDuration / DebuffApplyInterval`。换壳弹 44 原生 AI 自带 ai[0]∈[30,100) 每 tick ×1.06 自加速，已在 SpawnScythe 钉 `ai[0]=200` 段取消 → 弹速 = `ScytheSpeed` 恒定；期望结算写 `ScytheHitDamage`（默认 70），字段 = 期望 ÷ `BossAIModBase.ResultBias`。

## 命令与权限

| 命令 | 权限 | 说明 |
|---|---|---|
| `/bossai` | `bossaimod.admin` | 切换全局开关（默认开启；切换仅影响之后生成的魔改 Boss：史莱姆王/克眼） |

## 构建

本地 1458 API（与 `plugin/api` 同源实机验证），无需 NuGet：

```
dotnet build plugin-son/bossAIModded/bossAIModded.csproj -c Release
```

产物：`bin/Release/net9.0/bossAIModded.dll` → 放入服务器 `ServerPlugins/`。

## 后续 Boss 扩展方式

新增 Boss = 在 `BossMods/` 加一个继承 `BossAIModBase` 的类 + 在 `Main.GetOrCreate` 的 switch 里登记 NPCID。路由池/钩子/异常护栏已就绪。
