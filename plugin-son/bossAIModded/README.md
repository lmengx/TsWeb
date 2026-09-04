# bossAIModded

将 **Fargo Souls（Eternity Mode）** 的 Boss 魔改按"原版客户端可达"原则迁移到 **TShock + Terraria 1.4.5.8** 的独立子插件。

首个实现：**史莱姆王（KingSlime）Eternity-lite**。

## 设计原则

迁移判定三层模型（详见仓库 Fargo 移植研究报告）：

| 层 | 能否搬 | 处理 |
|---|---|---|
| 判定/数值层（何时做什么、改 ai[]/velocity/life） | ✅ | 照搬，客户端跑原版 AI 看到的就是"它本来就会这样" |
| 内容层（自定义 NPC/弹幕/Buff） | ⚠️ | 换壳为原版 ID |
| 视觉层（shader/Dust/粒子/自定义音效/震屏） | ❌ | 不迁移（原版客户端无消费能力） |

## 当前内容：史莱姆王 Eternity-lite（对照 FargoSouls v1.7.3.9 KingSlime 类）

### 已实现
- **掉血召唤波**：血量每跌破 1/6（共 5 波，冷却 3 秒）体内爆出 6 只尖刺史莱姆（换壳：原版 `SlimeSpiked`=535，原版 KS 战原生小怪）
- **普通跳跃弹道修正**：目标在头顶 240px 以上跳得更高；水平距离越远横向速度乘区 1~3 倍 + 侧向推力（Fargo 数值原样）
- **落地蓄力大跳**：特定状态（ai[2] 钳制 + 跳计时满 900）落地后 60 tick 前摇（写 `ai[0]=-999` 冻结原版 AI）→ 超远猛跳（-18 垂直初速 + 1000px 水平预判玩家位置）；飞行中每 5 tick 脚下落刺、过头自动取消归还原版状态机
- **狂暴尖刺雨**：血量 <66% 且非大跳时，每 240 tick 以玩家为中心上方 500px 铺 25 列尖刺下落（换壳：原版 `SpikedSlimeSpike`=605）
- **接触黏液**：碰撞期间持续上 `Slimed`(137) 减速（原版 Buff，客户端完整显示）

### 已知降级（原版客户端边界，README 明示）
- 换色 shader / 忍者头饰 / Boss 头图 / 蓝色 Dust / 粒子演出 / 屏幕震动 / 自定义音效：不迁移
- Fargo 的 Masochist 难度分支与 Mutant Boss 联动：不迁移
- 召唤小怪/弹幕的伤害 = `npc.defDamage × 2/3`（Fargo 语义），未做 Fargo 全局 Eternity 数值缩放

### 实机待调参点（KingSlimeEternity.cs 顶部常量）
`SummonWaveCount / SummonCooldown / SpecialJumpWindup / SpecialJumpVY / SpecialJumpPredictRange / SpikeRainInterval / BerserkLifeRatio` 等。
**依赖 1.4.5.8 原版 KS AI 数值的点**（如 `ai[2]∈[145,150)` 窗口、`ai[0]=-999` 冻结、落地判定 `velocity.Y==0`）已按 Fargo 源码原样移植，若实机与预期不符优先微调这些常量与 `ai[2]` 判断。

## 命令与权限

| 命令 | 权限 | 说明 |
|---|---|---|
| `/bossai` | `bossaimod.admin` | 切换全局开关（默认开启；切换仅影响之后生成的史莱姆王） |

## 构建

本地 1458 API（与 `plugin/api` 同源实机验证），无需 NuGet：

```
dotnet build plugin-son/bossAIModded/bossAIModded.csproj -c Release
```

产物：`bin/Release/net9.0/bossAIModded.dll` → 放入服务器 `ServerPlugins/`。

## 后续 Boss 扩展方式

新增 Boss = 在 `BossMods/` 加一个继承 `BossAIModBase` 的类 + 在 `Main.GetOrCreate` 的 switch 里登记 NPCID。路由池/钩子/异常护栏已就绪。
