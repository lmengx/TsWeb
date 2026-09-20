using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using TShockAPI;

namespace bossAIModded.BossMods;

/// <summary>
/// 骷髅王头（NPCID.SkeletronHead = 35）Eternity-lite 强化。
/// 移植自 FargowiltasSouls v1.7.3.9 VanillaEternity.SkeletronHead（本地反编译），保留"判定与数值层"。
///
/// 原版骷髅王头 AI（aiStyle 11，1.4.5.8 源码实证）状态备忘：
///   ai[1]==0 盘旋模式：追玩家保持 250px 上方，ai[2] 计数到 800 → ai[1]=1
///   ai[1]==1 旋转攻击模式：低头俯冲旋转，ai[2] 计数 400 → 回 ai[1]=0
///   ai[1]==2 地牢守卫形态（白天/脱战触发，原版 damage×15 / defense 9999）
///   ai[1]==3 脱战消失
///
/// 已迁移（Fargo 数值原样；全部为"后插桩追加"，不干预原版状态机流转）：
///   1) 8 向骨弹环：旋转模式（ai[1]==1/2）下每 20+100×生命比例 tick 以玩家为中心 8 向发射骨头
///      换壳 471 SkeletonBone（hostile 敌弹，aiStyle 2 重力抛物线）；弹道叠加头速度×(1-生命比例)
///      + Y 修正 -|X|×0.2（Fargo 数值原样）；伤害期望结算查表（FieldForResult）
///      ⚠ 471 原生 tileCollide=true 且 27 网络包不同步该字段 → 无法服务端改穿墙，
///      朝下方/贴墙方向的骨弹会撞墙消失（与克眼 44 同理，定案接受）
///   2) 手重生（GrowHands，仅一次）：头血首次跌破 50% 且无手在场 → 生成一对新手（左右手 ai[0]=±1）
///      （Fargo：无手重生；有手则把头血锁到 50%+10 防掉太快）
///   3) 命中 debuff：骨头(471)命中 / 头本体接触 → 眩晕 Dazed(160)，3 秒（180 tick），30 tick 节流
///      （Fargo 原接触 debuff 是 Defenseless+Lethargic 自定义 buff，无原版等价 → 用户要求换壳眩晕）
///
/// 未迁移（原版客户端无消费能力 / 需冻结接管原版 AI，均不入首版，见 README）：
///   - 减伤护甲（ArmDR：手活着时头按生命比例减伤）——需 ModifyIncomingHit 钩子，后插桩暂不做
///   - 换色 shader / Boss 头贴图 / 骨骼手臂素材（视觉层）
///   - 瞄准框(TargetingReticle)/十字守护者墙/追踪小鬼魂/扇形鬼魂弹（Fargo 自定义实体与追加弹幕，本期不迁）
///   - 濒死锁血变地牢守卫（ai[1]=2 三段演出，需 CheckDead 拦截 + AI 冻结，后插桩无法复刻）
///   - Masochist 难度分支（双倍重生手、DungeonGuardianAttack 大招池）
/// </summary>
public sealed class SkeletronHeadEternity : BossAIModBase
{
    // ---------- 可调参数（依实机手感微调） ----------
    private const int BoneRingWays = 8;              // 骨弹环数量（Fargo: 8）
    private const float BoneSpeed = 6f;              // 骨弹基础速率（Fargo: 6）
    private const float BoneIntervalBase = 20f;      // 间隔基础 tick（Fargo: 20）
    private const float BoneIntervalPerLife = 100f;  // 间隔随生命比例增量（Fargo: +100×life%）
    private const int BoneHitDamage = 60;            // 期望单发结算（实际扣血）。字段由 FieldForResult 反算

    private const float RegrowLifeRatio = 0.5f;      // 手重生血线（Fargo: life < 50%）
    private const int RegrowLockExtra = 10;          // 有手在场时头血锁到 50%+10（Fargo 原样）

    private const int DazedDuration = 180;           // 眩晕时长 tick（3 秒）
    private const int DebuffApplyInterval = 30;      // 同一玩家两次施加的最小间隔 tick（防持续接触逐 tick 重复）
    private const float BoneActiveWindowSeconds = 3f; // 我方骨弹"活跃时间窗"：来源 471 的受伤仅在此窗内算我方骨弹
                                                      //   （排除原版骷髅王/其它来源的 471）

    private const int BoneProjectile = ProjectileID.SkeletonBone; // 换壳：原版骷髅骨弹（471），hostile 敌弹

    // ---------- 实例状态（服务器内存即可） ----------
    private int _boneTimer;              // 骨弹环节拍（Fargo npc.localAI[2]）
    private bool _spawnedArms;           // 是否已重生过手（Fargo SpawnedArms，仅一次）
    private DateTime _lastBoneTime = DateTime.MinValue; // 最近一次骨弹生成时间（134 归因用）
    private readonly int[] _debuffCd = new int[255];   // 每玩家命中 debuff 冷却（按 whoAmI 索引）

    public override void Tick(NPC npc)
    {
        // 命中 debuff 冷却节拍
        for (var i = 0; i < Main.maxPlayers; i++)
        {
            if (_debuffCd[i] > 0) _debuffCd[i]--;
        }

        // ★ 手重生（Fargo SafePreAI 186-201，仅一次）
        if (!_spawnedArms && npc.life < npc.lifeMax * RegrowLifeRatio)
        {
            if (AnyArmAlive(npc))
            {
                // 手还在场：锁血 50%+10，防止重生判定前头血掉太快（Fargo 原样）
                npc.life = (int)Math.Round(npc.lifeMax * RegrowLifeRatio) + RegrowLockExtra;
                npc.netUpdate = true;
            }
            else
            {
                _spawnedArms = true;
                GrowHands(npc);
            }
        }

        // ★ 8 向骨弹环（Fargo SafePreAI 236-256，旋转/守卫模式 ai[1]==1/2）
        if (npc.ai[1] == 1f || npc.ai[1] == 2f)
        {
            float lifePercent = (float)npc.life / (float)npc.lifeMax;
            float interval = BoneIntervalBase + BoneIntervalPerLife * lifePercent;
            if (++_boneTimer >= interval)
            {
                _boneTimer = 0;
                // Fargo 条件：有玩家目标 且（无手在场 或 守卫模式）
                if (interval > 0f && TryGetTarget(npc, out var p) && (!AnyArmAlive(npc) || npc.ai[1] == 2f))
                {
                    Vector2 baseDir = Terraria.Utils.SafeNormalize(p.Center - npc.Center, Vector2.Zero) * BoneSpeed;
                    for (var i = 0; i < BoneRingWays; i++)
                    {
                        Vector2 dir = Rotate(baseDir, (float)(Math.PI / 4.0 * i));
                        dir += npc.velocity * (1f - lifePercent); // 叠加头速度（Fargo: ×(1-life%)）
                        dir.Y -= Math.Abs(dir.X) * 0.2f;          // Y 修正（Fargo: 抛物线上抛）
                        SpawnBone(npc, dir);
                    }
                }
            }
        }

        // ★ 本体接触玩家（精确碰撞箱相交，仅真碰到才触发；_debuffCd 节流防逐 tick 重复）
        for (var i = 0; i < Main.maxPlayers; i++)
        {
            var pl = Main.player[i];
            if (pl == null || !pl.active || pl.dead || _debuffCd[i] > 0) continue;
            if (npc.Hitbox.Intersects(pl.Hitbox))
            {
                ApplyDazed(i);
            }
        }
    }

    // ---------- 私有实现 ----------

    /// <summary>是否有存活的手（NPCID.SkeletronHand=36，ai[1] 指向本头）。</summary>
    private static bool AnyArmAlive(NPC npc)
    {
        foreach (var n in Main.npc)
        {
            if (n != null && n.active && n.type == NPCID.SkeletronHand && n.ai[1] == npc.whoAmI)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>重生一对新手（Fargo GrowHands：左右手 ai[0]=±1，ai[1]=头 whoAmI；对齐原版 22113-22123 生成参数）。</summary>
    private void GrowHands(NPC npc)
    {
        int cx = (int)npc.Center.X;
        int cy = (int)npc.Center.Y;
        for (var i = -1; i <= 1; i += 2)
        {
            int who = NPC.NewNPC(new EntitySource_Parent(npc), cx, cy, NPCID.SkeletronHand, npc.whoAmI, i, npc.whoAmI, 0f, 0f, npc.target);
            if (who < 0 || who >= Main.maxNPCs) continue;
            var hand = Main.npc[who];
            hand.ai[0] = i;                 // 左右手标记
            hand.ai[1] = npc.whoAmI;        // 母体头 whoAmI
            hand.ai[3] = (i == 1) ? 150f : 0f; // 右手对齐原版 ai[3]=150（第二只手摆动相位）
            hand.target = npc.target;
            hand.netUpdate = true;
            NetMessage.SendData(23, -1, -1, null, who, 0f, 0f, 0f, 0, 0, 0);
        }
    }

    /// <summary>服务器造一颗原版骨头弹（owner=255；1458 原版 NewProjectile 会自动广播 27）。
    /// 期望结算查表：网络字段 = 期望 ÷ ResultBias（不再依赖 defDamage）。</summary>
    private void SpawnBone(NPC npc, Vector2 vel)
    {
        Projectile.NewProjectile(new EntitySource_Parent(npc), npc.Center.X, npc.Center.Y, vel.X, vel.Y,
            BoneProjectile, FieldForResult(BoneHitDamage), 0f, 255, 0f, 0f, 0f);
        // 记录生成时间：把 134 PlayerHurtV2 上报的来源 471 归因到本 Boss 的骨弹（活跃窗内才算）
        _lastBoneTime = DateTime.UtcNow;
    }

    /// <summary>玩家受伤(134 PlayerHurtV2 上报)时判定：来源弹幕=471 且 在我方骨弹活跃窗内 → 眩晕。</summary>
    public void OnPlayerDamage(int who, PlayerDeathReason reason)
    {
        if (DateTime.UtcNow - _lastBoneTime > TimeSpan.FromSeconds(BoneActiveWindowSeconds))
        {
            return;
        }
        if (GetSourceProjectileType(reason) != BoneProjectile)
        {
            return;
        }
        ApplyDazed(who);
    }

    /// <summary>给玩家上眩晕（Dazed 160，TShock SetBuff 服务端广播；受 _debuffCd 节流）。</summary>
    private void ApplyDazed(int who)
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
        tp.SetBuff(BuffID.Dazed, DazedDuration, false);
    }

    private static bool TryGetTarget(NPC npc, out Player p)
    {
        p = null!;
        if (!npc.HasValidTarget || npc.target < 0 || npc.target >= Main.maxPlayers)
        {
            return false;
        }
        p = Main.player[npc.target];
        return p != null && p.active && !p.dead;
    }

    /// <summary>绕原点旋转向量（Fargo 用 Utils.RotatedBy，此处手写避免 API 版本差异）。</summary>
    private static Vector2 Rotate(Vector2 v, float radians)
    {
        float cos = (float)Math.Cos(radians);
        float sin = (float)Math.Sin(radians);
        return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }

    public override void OnKilled(NPC npc)
    {
        _lastBoneTime = DateTime.MinValue;
    }
}
