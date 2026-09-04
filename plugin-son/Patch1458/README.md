# 1458临时补丁（Patch1458）

TShock Bouncer NPC buff 白名单的 **1.4.5.8 内容兼容补丁**。修复使用 1.4.5.8 新内容（典型：新饰品**催化手环**）的合法玩家被误踢（`Added buff to ... NPC abnormally.` / 前端"给 NPC：敌怪 异常添加了buff"）。

## 背景

- TShock `Bouncer.OnNPCAddBuff`（Bouncer.cs:2167）对客户端 53 号包（AddNPCBuff，仅含 npcId/buffType/time）做**静态白名单**校验：buffType 不在 `NPCAddBuffTimeMax`（Bouncer.cs:3228，private static，33 项）即踢。
- 该白名单按 1.4.4 / 早期 1.4.5 原版数值抄死，**1.4.5.8 新增的 5 个 NPC debuff 全部缺席** → 合法玩家被误踢。
- TShock 全链**不存在**"按手持物品核对 debuff 合法性"的机制（53 包根本没有物品字段），此前疑似"外挂"的踢人实为白名单过期。

## 补丁内容（注入条目 = 1.4.5.8 原版全部调用点的最大时长）

| buff | 名称 | 上限 | 1.4.5.8 来源（t8_Projectile.cs 实证） |
|---|---|---|---|
| 395 | PotentAcids 强酸 | 120 | 诅咒涂层 60（:11203）；弹幕 282/283 → 120（:11684） |
| 397 | ChlorophyteSpore 叶绿孢子 | 300 | 弹幕 1127（:11729） |
| 398 | AcceleratePoisons 催化毒液 | 300 | **催化手环** catalystBand：任意玩家弹幕命中敌怪即 AddBuff(398,300)（:11191，唯一调用点） |
| 399 | BlueLightning 蓝闪电 | 420 | 弹幕 1117 LightningStrikeShot，60*rand(4,8)（:11230） |
| 400 | RedLightning 红闪电 | 420 | 弹幕 1122 ArcSurge，60*rand(4,8)（:11235） |

实现：启动阶段反射取出 `Bouncer.NPCAddBuffTimeMax` 并注入上表；不改任何包处理流程，Bouncer 其余防护（超时踢 / townNPC / 禁用检查）原样保留。官方未来收录某条目后本插件自动跳过、不覆盖；Dispose 仅回滚自己注入的条目，可安全热重载。

## 使用

- 把 `bin/Release/net9.0/Patch1458.dll` 放入服务器 `ServerPlugins/`。
- 启动日志确认：`[1458临时补丁] 已向 Bouncer.NPCAddBuffTimeMax 注入 5 项：...`。
- 游戏内 `/patch1458`（权限 `patch1458.use`，自动授予 admin 组）查看注入状态。

## 已知局限

- **城镇 NPC（townNPC）debuff 白名单是 Bouncer 内硬编码的 16 个 BuffID 常量 if 链**（非集合），无法以字典注入扩展——催化手环等对**城镇 NPC** 施加新 debuff 仍会被踢。玩家主动攻击城镇 NPC 属边缘场景；如确需放行须改 TShock 源码或 Harmony patch，本插件刻意不做。
- 本插件**不是**免踢通行证：白名单外的其它 buff 伪造（外挂）仍照常被踢；注入的上限值超过原版最大值同样照踢。

## 构建

```
dotnet build plugin-son/Patch1458/Patch1458.csproj -c Release
```

依赖：NuGet TShock 6.1.0（与 AutoSee/Compat1456 等子插件同模式，运行时兼容 1458 服务器在用 TShock）。
