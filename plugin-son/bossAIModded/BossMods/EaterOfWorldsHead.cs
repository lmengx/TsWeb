using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;

namespace bossAIModded.BossMods;

/// <summary>
/// 世界吞噬者头（NPCID.EaterofWorldsHead = 13）Eternity-lite 强化（可直接移植子集）。
/// 移植自 FargowiltasSouls v1.7.3.9 VanillaEternity.EaterofWorldsHead/EaterofWorlds（本地反编译），
/// 保留"判定与数值层"，新内容全部换壳为原版 ID：
///   - 命中 debuff = 灵液(69)+咒火(24)+眩晕(160)（Fargo 原 ShadowFlame39+RottingBuff 换壳）
///   - 火球齐射 = 96 CursedFlame，每个活跃体节从自身中心向玩家单独发射一发
///     （Fargo 自定义 CursedFireballHoming 全段齐射追踪弹 → 换壳 96 直线定向弹）
/// 后插桩架构限制：UTurn / Coil（Fargo SafePreAI return false 完全接管状态机）留待"接管引擎"。
///
/// ⚠ 原版 AI_006_Worms（1.4.5.x）头 13 状态备忘：
///   ai[0] = 下一段 whoAmI（链向前）；ai[1] = 0（头不吸附，走追逐分支）；
///   专家模式血越低吐 666 越频繁；段 14 断链自动变 13/15（原版分裂，需路由池按 type 重建实例）。
///
/// ⚠ 咒火节奏按【整条 Boss 总血量】分档（用户需求）：
///   ≥60%：齐射（全段同 tick 各 1 发），间隔 300 tick（±随机抖动）；
///   <60%：双倍齐射（全段同 tick 各 2 发：直射 + 随机侧偏 10°），间隔 200 tick；
///   <30%：轮流持续射（每段独立倒计时，各段轮流吐），间隔 90 tick。
///   总血量 = 同"生成批次"（SpawnTimes 时间差 ≤ 免伤时长）的所有活跃 EOW 段加总，
///   既排除其它 Boss 污染（防"满血新链被残血旧链拉低"），断链分截后仍按整条 Boss 判定。
/// ⚠ 出场免伤：链生成后 10 秒内 npc.defense=99999（原版 SuperArmor：一切伤害钳到 1）。
/// </summary>
public sealed class EaterOfWorldsHead : BossAIModBase
{
    // ---------- 可调参数（依实机手感微调） ----------
    private const int HeadDamageMultNum = 4;   // 头伤害 ×4/3（Fargo SetDefaults）
    private const int HeadDamageMultDen = 3;
    private const float DespawnRange = 6000f; // 无目标/超 6000px → 加速消失（Fargo: 6000）
    private const float ChaseRange = 2500f;   // 超 2500px → 转向追击（Fargo: 2500）
    private const float ChaseMaxSpeed = 25f;  // 追击限速（Fargo: 25）
    private const float ChaseTurnRate = 0.1f; // 追击转向速率（Fargo: RotateTowards 0.1）

    // ═══ 命中 debuff（Fargo 39+Rotting → 换壳 灵液/咒火/眩晕）═══
    private const int IchorDuration = 300;        // 灵液(Ichor 69) 5s
    private const int CursedDuration = 300;       // 咒火(CursedInferno 24) 5s
    private const int DazedDuration = 180;        // 眩晕(Dazed 160) 3s
    private const int DebuffApplyInterval = 30;   // 同一玩家两次施加的最小间隔 tick（防逐 tick 重复）

    // ═══ 火球齐射（Fargo CursedFireballHoming 全段齐射 → 换壳 96 CursedFlame 定向弹）═══
    // ≥60%：齐射（全段共享倒计时，同 tick 各 1 发）；<60%：双倍齐射（同 tick 各 2 发）；
    // <30%：轮流持续射（每段独立倒计时，各段轮流吐、间隔最短）。
    private const int FireballInterval = 300;         // ≥60% 血：齐射间隔 tick
    private const int FireballIntervalEnraged = 200;  // <60% 血：双倍齐射间隔 tick
    private const int FireballIntervalBerserk = 90;   // <30% 血：轮流持续射，每段间隔 tick
    private const float EnragedLifeRatio = 0.6f;      // 总血量 <60% → 双倍齐射（每段 2 发）
    private const float BerserkLifeRatio = 0.3f;      // 总血量 <30% → 轮流持续射
    private const int ShotsPerRoundNormal = 1;        // ≥60% 每次发射数
    private const int ShotsPerRoundEnraged = 2;       // <60% 每次发射数（双倍）
    private const int ShotsPerRoundBerserk = 3;       // <30% 每次发射数（连发，持续火力）
    private const float SpreadAngleDeg = 10f;         // 双倍齐射第二发随机侧偏角（°）
    private const int IntervalJitterMax = 40;         // 齐射/段间隔随机抖动上限 tick
    private const int ShotGapTicks = 8;               // 同一段多发射击之间的间隔 tick（连发节奏）
    private const int SegmentEvery = 1;               // 每隔 N 个体节发射一个（1=每个体节都发）
    private const float RingSpeed = 6f;               // 弹速（96 原版直线弹，恒定）
    private const int FireballHitDamage = 110;        // 期望单发结算（实际扣血）。字段由 FieldForResult 反算
    private const float FireballActiveWindowSeconds = 3f; // 我方火球"活跃时间窗"：来源 96 的受伤仅在此窗内算我方
                                                      //   （地图上腐化吞噬者也会吐 96，窗内归因 + 冷却节流兜底）

    // ═══ 出场免伤（用户需求：刚出现前 10 秒所有伤害只能造成 1 点）═══
    // 实现：免伤期 npc.defense=99999（原版 SuperArmor，防御极高 → 任何伤害钳到 1）。

    // ---------- 实例状态 ----------
    private readonly int[] _debuffCd = new int[255]; // 每玩家命中冷却（按 whoAmI 索引）
    private readonly Dictionary<int, int> _segFireCd = new(); // 段 whoAmI → 距下次发射剩余 tick（轮流档每段独立）
    private readonly Dictionary<int, int> _segBurstLeft = new(); // 段 whoAmI → 本次连发剩余发数（>0 处于连发中）
    private int _salvoCd;                            // 齐射档共享倒计时（全段同 tick 齐射）
    private bool _statsApplied;                      // 伤害倍率/免疫已应用
    private int _noSelfDestructTimer = 15;           // 生成链保护期（Fargo: NoSelfDestructTimer=15）
    private int _chainCheckCounter;                  // 链自检节拍（每 6 tick 一次）
    private DateTime _lastFireballTime = DateTime.MinValue; // 134 上报来源 96 归因时间窗

    public override void Tick(NPC npc)
    {
        // 出场免伤：免伤期 npc.defense=99999（原版 SuperArmor：CalculateDamageNPCsTake 把
        // 一切伤害钳到 1），结束恢复原防御（defDefense），仅在变化时 netUpdate 广播。
        bool inGrace = IsInSpawnGrace(npc.whoAmI);
        int graceDefense = inGrace ? 99999 : npc.defDefense;
        if (npc.defense != graceDefense)
        {
            npc.defense = graceDefense;
            npc.netUpdate = true;
        }

        // 冷却节拍（无接触时也逐 tick 递减，玩家才能被再次命中）
        for (var i = 0; i < Main.maxPlayers; i++)
        {
            if (_debuffCd[i] > 0) _debuffCd[i]--;
        }

        // 生成首 tick：伤害 ×4/3 + 免疫暗影焰（Fargo OnFirstTick/SetDefaults 语义）
        if (!_statsApplied)
        {
            _statsApplied = true;
            npc.damage = npc.damage * HeadDamageMultNum / HeadDamageMultDen;
            npc.buffImmune[BuffID.ShadowFlame] = true;
            npc.netUpdate = true;
        }

        // 目标校验 + 脱战消失 / 远距追击（Fargo SafePreAI 前段；后插桩逐 tick 覆盖）
        if (!npc.HasValidTarget)
        {
            npc.TargetClosest(false);
        }
        if (!npc.HasValidTarget || npc.Distance(Main.player[npc.target].Center) > DespawnRange)
        {
            npc.velocity.Y += 0.25f;
            if (npc.timeLeft > 120) npc.timeLeft = 120;
        }
        else if (npc.Distance(Main.player[npc.target].Center) > ChaseRange)
        {
            var toTarget = npc.DirectionTo(Main.player[npc.target].Center);
            if (npc.velocity.Length() < ChaseMaxSpeed)
            {
                npc.velocity += toTarget * 1f;
            }
            npc.velocity = RotateTowards(npc.velocity, toTarget.ToRotation(), ChaseTurnRate);
        }

        // 链完整性自检（Fargo NoSelfDestruct：ai[0] 指向的下一段断链/失效 → 自杀）
        // 15 tick 保护期跳过（生成瞬间原版链尚未建好）；之后每 6 tick 检查一次
        if (_noSelfDestructTimer > 0)
        {
            _noSelfDestructTimer--;
        }
        else if (++_chainCheckCounter % 6 == 3)
        {
            if (IsChainBroken(npc))
            {
                SelfDestruct(npc);
                return;
            }
        }

        // 咒火：每段独立倒计时（随机相位起步 + 间隔抖动），任何血线下体节间都不同步
        UpdateSegmentFire(npc);

        // 本体接触玩家（精确碰撞箱相交，仅真碰到才触发；_debuffCd 节流）
        for (var i = 0; i < Main.maxPlayers; i++)
        {
            var pl = Main.player[i];
            if (pl == null || !pl.active || pl.dead || _debuffCd[i] > 0) continue;
            if (npc.Hitbox.Intersects(pl.Hitbox))
            {
                ApplyDebuffs(i);
            }
        }
    }

    /// <summary>玩家受伤(134 PlayerHurtV2 上报)时判定：来源弹幕=96 且 在我方火球活跃窗内 → 施加三 debuff。</summary>
    public void OnPlayerDamage(int who, PlayerDeathReason reason)
    {
        if (DateTime.UtcNow - _lastFireballTime > TimeSpan.FromSeconds(FireballActiveWindowSeconds))
        {
            return;
        }
        if (GetSourceProjectileType(reason) != 96)
        {
            return;
        }
        ApplyDebuffs(who);
    }

    public override void OnKilled(NPC npc)
    {
        _lastFireballTime = DateTime.MinValue;
    }

    // ---------- 私有实现 ----------

    /// <summary>
    /// 咒火发射（按整条链总血量分档，用户需求）：
    ///  - ≥60%：齐射——全段共享倒计时 _salvoCd，到点所有段同 tick 各发 1 发；
    ///  - <60%：双倍齐射——同上，每段同 tick 各发 2 发（直射 + 随机侧偏 10°）；
    ///  - <30%：轮流持续射——每段独立倒计时 _segFireCd（天然轮流）+ 随机相位起步 + 间隔抖动，
    ///    到点连发 3 发（ShotGapTicks），间隔 90 tick 持续吐；
    ///  - 段失效/断链 → 移除该段倒计时。
    /// </summary>
    private void UpdateSegmentFire(NPC head)
    {
        if (!head.HasValidTarget)
        {
            head.TargetClosest(false);
        }
        if (!head.HasValidTarget)
        {
            return;
        }
        var target = Main.player[head.target];
        if (target == null || !target.active)
        {
            return;
        }

        float ratio = GetTotalLifeRatio(head);
        int interval = ratio < BerserkLifeRatio ? FireballIntervalBerserk
            : ratio < EnragedLifeRatio ? FireballIntervalEnraged
            : FireballInterval;
        int shots = ratio < BerserkLifeRatio ? ShotsPerRoundBerserk
            : ratio < EnragedLifeRatio ? ShotsPerRoundEnraged
            : ShotsPerRoundNormal;

        // ═══ 齐射/双倍齐射档（≥30% 血）：全段共享倒计时，到点所有段同 tick 发射 ═══
        if (ratio >= BerserkLifeRatio)
        {
            _salvoCd--;
            if (_salvoCd > 0) return;
            _salvoCd = interval + Main.rand.Next(IntervalJitterMax + 1);

            int segCount = 0;
            for (var i = 0; i < Main.maxNPCs; i++)
            {
                var seg = Main.npc[i];
                if (seg == null || !seg.active) continue;
                if (seg.type != NPCID.EaterofWorldsHead
                    && seg.type != NPCID.EaterofWorldsBody
                    && seg.type != NPCID.EaterofWorldsTail) continue;
                if (segCount++ % SegmentEvery != 0) continue;

                FireShot(seg, target.Center); // 直射 1 发
                if (shots >= 2)
                {
                    // 双倍齐射：同 tick 再补 1 发，随机侧偏 10°（直线弹不重叠）
                    var vel = seg.DirectionTo(target.Center) * RingSpeed;
                    float spread = (Main.rand.Next(2) == 0 ? 1f : -1f) * (SpreadAngleDeg * MathF.PI / 180f);
                    SpawnFireball(seg, seg.Center, vel.RotatedBy(spread));
                }
            }
            return;
        }

        // ═══ 轮流持续射档（<30% 血）：每段独立倒计时，各段轮流持续吐 ═══
        // 清理已失效段（死亡/断链/非 EOW 段的残留倒计时）
        foreach (var key in _segFireCd.Keys.ToList())
        {
            if (key < 0 || key >= Main.maxNPCs)
            {
                _segFireCd.Remove(key);
                _segBurstLeft.Remove(key);
                continue;
            }
            var seg = Main.npc[key];
            if (seg == null || !seg.active
                || (seg.type != NPCID.EaterofWorldsHead
                    && seg.type != NPCID.EaterofWorldsBody
                    && seg.type != NPCID.EaterofWorldsTail))
            {
                _segFireCd.Remove(key);
                _segBurstLeft.Remove(key);
            }
        }

        int segCount2 = 0;
        for (var i = 0; i < Main.maxNPCs; i++)
        {
            var seg = Main.npc[i];
            if (seg == null || !seg.active) continue;
            if (seg.type != NPCID.EaterofWorldsHead
                && seg.type != NPCID.EaterofWorldsBody
                && seg.type != NPCID.EaterofWorldsTail) continue;
            if (segCount2++ % SegmentEvery != 0) continue;

            // 新段：随机相位起步（0~interval），体节间从第一轮就错开（轮流）
            if (!_segFireCd.TryGetValue(seg.whoAmI, out var cd))
            {
                _segFireCd[seg.whoAmI] = Main.rand.Next(interval + 1);
                _segBurstLeft[seg.whoAmI] = 0;
                continue; // 起步 tick 不发射（相位延迟已随机）
            }

            if (--cd > 0)
            {
                _segFireCd[seg.whoAmI] = cd;
                continue;
            }

            // 到点发射
            FireShot(seg, target.Center);

            int burstLeft = _segBurstLeft.TryGetValue(seg.whoAmI, out var b) ? b : 0;
            if (burstLeft > 0)
            {
                // 连发中：ShotGapTicks 后发下一发
                _segBurstLeft[seg.whoAmI] = burstLeft - 1;
                _segFireCd[seg.whoAmI] = ShotGapTicks;
            }
            else
            {
                // 本次突发结束：进入下一轮（档位间隔 + 随机抖动），并登记下次连发剩余发数
                _segFireCd[seg.whoAmI] = interval + Main.rand.Next(IntervalJitterMax + 1);
                _segBurstLeft[seg.whoAmI] = shots > 1 ? shots - 1 : 0;
            }
        }
    }

    /// <summary>从指定体节中心向目标发射一发 96 咒火（直线弹，方向按发射时最新目标）。</summary>
    private void FireShot(NPC seg, Vector2 targetCenter)
    {
        SpawnFireball(seg, seg.Center, seg.DirectionTo(targetCenter) * RingSpeed);
    }

    /// <summary>
    /// Boss 总血量比例 = 所有活跃 EOW 段（头 13/段 14/尾 15）life 之和 ÷ lifeMax 之和，
    /// 但只统计与 head 同一"生成批次"（SpawnTimes 时间差 ≤ SpawnDamageCapDuration）的段：
    ///  - 排除地图上其它 Boss（残血旧链/他人正打的链）→ 满血新链不会被拉低误入高档；
    ///  - 断链分截后（中间段死，前后截各成独立链）同批仍合并 → 按"整条 Boss"总血量判定，
    ///    不会出现"整条 <30% 但某截 >30%"而不触发轮流持续射。
    /// </summary>
    private static float GetTotalLifeRatio(NPC head)
    {
        if (!BossAIModded.SpawnTimes.TryGetValue(head.whoAmI, out var spawnTime))
        {
            spawnTime = DateTime.UtcNow;
        }
        long sumLife = 0;
        long sumMax = 0;
        for (var i = 0; i < Main.maxNPCs; i++)
        {
            var seg = Main.npc[i];
            if (seg == null || !seg.active) continue;
            if (seg.type != NPCID.EaterofWorldsHead
                && seg.type != NPCID.EaterofWorldsBody
                && seg.type != NPCID.EaterofWorldsTail) continue;
            if (seg.lifeMax <= 0) continue;
            if (!BossAIModded.SpawnTimes.TryGetValue(seg.whoAmI, out var t)) continue; // 未登记（非本插件链）不算
            if ((t - spawnTime).Duration() > SpawnDamageCapDuration) continue; // 不同批次（其它 Boss）不算
            sumLife += seg.life;
            sumMax += seg.lifeMax;
        }
        return sumMax > 0 ? (float)sumLife / sumMax : 1f;
    }

    private static bool IsChainBroken(NPC npc)
    {
        int nextIdx = (int)npc.ai[0];
        if (nextIdx <= -1 || nextIdx >= Main.maxNPCs) return true;
        var next = Main.npc[nextIdx];
        return next == null || !next.active
            || (next.type != NPCID.EaterofWorldsBody && next.type != NPCID.EaterofWorldsTail)
            || next.ai[1] != npc.whoAmI;
    }

    private static void SelfDestruct(NPC npc)
    {
        npc.life = 0;
        npc.HitEffect(0, 10.0);
        npc.checkDead();
        npc.active = false;
        npc.netUpdate = false;
        NetMessage.SendData(23, -1, -1, null, npc.whoAmI, 0f, 0f, 0f, 0, 0, 0);
    }

    /// <summary>向玩家方向保持长度、把速度方向转向目标角（Fargo Utilities.RotateTowards 语义）。</summary>
    private static Vector2 RotateTowards(Vector2 current, float targetAngle, float maxRadians)
    {
        float diff = MathHelper.WrapAngle(targetAngle - current.ToRotation());
        float step = MathHelper.Clamp(diff, -maxRadians, maxRadians);
        float newAngle = current.ToRotation() + step;
        float len = current.Length();
        return new Vector2((float)Math.Cos(newAngle), (float)Math.Sin(newAngle)) * len;
    }

    /// <summary>服务器造一颗原版 96 诅咒火球（owner=255；1458 原版 NewProjectile 自动广播 27）。
    /// 96 直线飞行、撞墙消失（tileCollide=true 原生行为），伤害按期望结算查表反算。</summary>
    private int SpawnFireball(NPC from, Vector2 pos, Vector2 vel)
    {
        int dmg = FieldForResult(FireballHitDamage);
        int who = Projectile.NewProjectile(new EntitySource_Parent(from), pos.X, pos.Y, vel.X, vel.Y,
            96, dmg, 0f, 255, 0f, 0f, 0f);
        // 记录生成时间：把 134 PlayerHurtV2 上报的来源 96 归因到本 Boss 的火球（活跃窗内才算）
        _lastFireballTime = DateTime.UtcNow;
        return who;
    }

    /// <summary>给玩家上 灵液(69)+咒火(24)+眩晕(160) 三 debuff（TShock SetBuff 服务端广播；受 _debuffCd 节流）。</summary>
    private void ApplyDebuffs(int who)
    {
        if (who < 0 || who >= Main.maxPlayers || _debuffCd[who] > 0)
        {
            return;
        }
        _debuffCd[who] = DebuffApplyInterval;
        var tp = TShock.Players[who];
        if (tp == null)
        {
            return;
        }
        tp.SetBuff(BuffID.Ichor, IchorDuration, false);
        tp.SetBuff(BuffID.CursedInferno, CursedDuration, false);
        tp.SetBuff(BuffID.Dazed, DazedDuration, false);
    }
}
