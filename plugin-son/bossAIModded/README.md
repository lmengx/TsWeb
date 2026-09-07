# bossAIModded

将 **Fargo Souls（Eternity Mode）** 的 Boss 魔改按"原版客户端可达"原则迁移到 **TShock + Terraria 1.4.5.8** 的独立子插件。

当前 Boss：**史莱姆王（KingSlime）**、**克眼（Eye of Cthulhu）**、**世界吞噬者（Eater of Worlds）** 均实现 Eternity-lite。

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

## Boss 3：世界吞噬者 Eternity-lite（对照 FargoSouls v1.7.3.9 EaterofWorldsHead/EaterofWorlds/EaterofWorldsSegment）

链式多 NPC Boss（头 13 + 段 14×N + 尾 15，`ai[0]/ai[1]` 双向链）。已实现为「头实例主控 + 段实例从属」，路由池按 type 分支。

### 已实现（后插桩安全子集）
- **出场免伤**：整条链出现后**前 10 秒所有伤害只能造成 1 点**——免伤期 `npc.defense=99999`（原版 SuperArmor：`CalculateDamageNPCsTake` 把一切伤害钳到 1），免伤结束恢复 `defDefense`，变化时 netUpdate 广播；链级生成时间按 whoAmI 登记，段变头沿用，NPC 死亡/失效清理）
- **命中 debuff**：本体接触 / 我方 96 火球命中 → 灵液(69)+咒火(24) 各 5s + 眩晕(160) 3s（Fargo 原 ShadowFlame39+RottingBuff 换壳为原版三 debuff；`_debuffCd` 30 tick 节流）
- **火球全段齐射（按整条链总血量分档）**：头实例每轮调度，**每个活跃体节（头/段/尾）从自身中心向玩家发射 96 CursedFlame**（Fargo 自定义 CursedFireballHoming 全段齐射追踪弹 → 换壳 96 直线定向弹；火力与体节数正比，`SegmentEvery` 可调发射密度）：
  - 总血量 ≥60%：**齐射**（全段共享倒计时，到点所有段同 tick 各 1 发），间隔 300 tick（±随机抖动）
  - 总血量 <60%：**双倍齐射**（全段同 tick 各 2 发：直射 + 随机侧偏 10°），间隔 200 tick
  - 总血量 <30%：**轮流持续射**（每段独立倒计时 + 随机相位起步，各段轮流吐，连发 3 发），间隔 90 tick
  - 总血量 = **同一"生成批次"的所有活跃 EOW 段**（头 13/段 14/尾 15，按 `SpawnTimes` 生成时间差 ≤ 免伤时长分组）life 之和 ÷ lifeMax 之和——既排除地图上其它 Boss（残血旧链/他人正打的链）拉低满血新链，断链分截后前后截仍合并按整条 Boss 判定；各段独立血量，避免"头残血但链还有很多血"误入高阶段）
- **数值层**：头伤害 ×4/3、段伤害 ×2（⚠ 仅服务端生效，NPC 撞击伤害客户端本地判定）、免疫暗影焰
- **脱战消失 / 远距追击**：>6000px 下坠 + timeLeft=120；>2500px 转向加速限速 25
- **链完整性自检**：生成 15 tick 保护期后每 6 tick 检查 `ai[0]` 下一段断链 → 自杀（Fargo NoSelfDestruct 语义）

### Bug 修复
- **"只放一轮咒火"**：原版段(14)断链自动变身 头(13)/尾(15)（NPC.cs 54793-54809），whoAmI 不变但 type 变了；路由池此前直接返回缓存实例（旧 `EaterOfWorldsSegment` 无齐射逻辑）→ 新头不再放咒火。现 `GetOrCreate` 按 `npc.type` 期望类型校验缓存，不匹配则重建实例（`TypeFor`/`CreateFor` 分离）。

### 未迁移（详见类头注释）
- **UTurn（Attack==2）** / **Coil 盘圈（Attack==3）**：Fargo SafePreAI return false 完全接管状态机，后插桩无法复刻 → 留待"接管引擎"（Coil 的段拉圈依赖冻结整条链跟随）
- **CursedFireballHoming 追踪弹**：无原版追踪 hostile 弹等价 → 已换壳直线环形（判定/数值层保留）
- **MassDefense 群体防御** / **CheckDead 段多不死** / **弹幕磨损** / **666 唾沫强化**：后续迭代
- **WormyFood 召唤物掉落**：Fargo ModItem，后续换壳原版蠕虫诱饵

### 实机待调参点（EaterOfWorldsHead.cs 顶部常量）
`FireballInterval / FireballIntervalEnraged / FireballIntervalBerserk / EnragedLifeRatio(=0.6) / BerserkLifeRatio(=0.3) / ShotsPerRoundNormal/Enraged/Berserk / SpreadAngleDeg / IntervalJitterMax / ShotGapTicks / SegmentEvery / RingSpeed / FireballHitDamage / IchorDuration / CursedDuration / DazedDuration / SpawnDamageCapDuration`。96 弹为原版直线弹（撞墙消失），弹速 = `RingSpeed` 恒定；期望结算写 `FireballHitDamage`（默认 110），字段 = 期望 ÷ `BossAIModBase.ResultBias`；出场免伤时长 `SpawnDamageCapDuration`（默认 10s）。

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
