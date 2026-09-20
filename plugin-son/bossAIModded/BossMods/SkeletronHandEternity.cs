using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using TShockAPI;

namespace bossAIModded.BossMods;

/// <summary>
/// 骷髅王手（NPCID.SkeletronHand = 36）Eternity-lite 强化。
/// 移植自 FargowiltasSouls v1.7.3.9 VanillaEternity.SkeletronHand（本地反编译），保留"判定与数值层"。
///
/// 原版手 AI（aiStyle 12，1.4.5.8 源码实证）：
///   ai[0]=±1 左右手标记；ai[1]=头 whoAmI；ai[2] 摆动相位；头 ai[1]==0 时 ai[3] 计数摆动。
///   头消失/脱战（ai[1]==3）→ 手 EncouragDespawn 消失。
///
/// 已迁移（Fargo 数值原样；后插桩追加，不干预原版状态机流转）：
///   1) 抛骷髅小怪：头旋转模式结束后（AttackTimer 从递减复位到 215 的瞬间），若头血 ≥50% 且有玩家目标，
///      向玩家方向抛掷随机投骨骷髅小怪（NPCID.BoneThrowingSkeleton 449/450/451/452），
///      抛物线初速：dx/60, dy/60 - 0.5×0.4×60（Fargo 原样；生成后手动设 velocity）
///      ⚠ 骷髅小怪是原版敌怪，落点附近会正常攻击玩家（"骷髅雨"语义）
///   2) 命中 debuff：手本体接触 → 眩晕 Dazed(160)，3 秒（180 tick），30 tick 节流
///      （Fargo 原接触 debuff 是 Lethargic+眩晕 160（60tick），用户要求统一换壳眩晕 3 秒）
///
/// 未迁移（本期不迁，见 README）：
///   - 旋转扫击（SpinAttack：围绕头旋转 + 双手相撞弹幕）——需 SafePreAI 接管手行为，后插桩只追加
///   - 突进（PrepareLunge 蓄力冲刺）、红色火花粒子、僵尸吼声（视觉层）
///   - 旋转模式下的 SkeletronGuardian2 直线鬼魂弹（Fargo 自定义弹幕）
///   - Masochist 难度分支（第二对手 secondSet、额外行为）
/// </summary>
public sealed class SkeletronHandEternity : BossAIModBase
{
    // ---------- 可调参数（依实机手感微调） ----------
    private const int AttackTimerReset = 215;        // 非旋转模式 AttackTimer 复位值（Fargo: 215）
    private const float ThrowArcGravity = 0.4f;      // 抛掷抛物线重力系数（Fargo: num = 0.4f）
    private const float ThrowJitterRadius = 80f;     // 抛掷落点随机圆半径 px（Fargo: NextVector2Circular 80）
    private const float ThrowVelocityDiv = 60f;      // 初速除数（Fargo: /60）
    private const float ThrowMinHeadLifeRatio = 0.5f; // 头血 ≥50% 才抛骷髅（Fargo: val.life >= val.lifeMax/2）

    private const int DazedDuration = 180;           // 眩晕时长 tick（3 秒）
    private const int DebuffApplyInterval = 30;      // 同一玩家两次施加的最小间隔 tick（防持续接触逐 tick 重复）

    // ---------- 实例状态（服务器内存即可） ----------
    private int _attackTimer = AttackTimerReset;     // Fargo AttackTimer（旋转模式递减，复位时抛骷髅）
    private readonly int[] _debuffCd = new int[255]; // 每玩家命中 debuff 冷却（按 whoAmI 索引）

    public override void Tick(NPC npc)
    {
        // 命中 debuff 冷却节拍
        for (var i = 0; i < Main.maxPlayers; i++)
        {
            if (_debuffCd[i] > 0) _debuffCd[i]--;
        }

        // ★ 头状态驱动抛骷髅（Fargo SafePreAI 126-158 判定骨架）
        var head = GetHead(npc);
        bool headSpinning = head != null && (head.ai[1] == 1f || head.ai[1] == 2f);
        if (headSpinning)
        {
            _attackTimer--;
        }
        else if (_attackTimer != AttackTimerReset)
        {
            _attackTimer = AttackTimerReset;
            // 退出旋转模式的瞬间抛一次骷髅雨（Fargo：有玩家目标 且 头血 ≥50%）
            if (head != null && head.life >= head.lifeMax * ThrowMinHeadLifeRatio && TryGetTarget(npc, out var p))
            {
                ThrowSkeleton(npc, p);
            }
        }

        // ★ 本体接触玩家（精确碰撞箱相交；_debuffCd 节流防逐 tick 重复）
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

    /// <summary>头（NPCID.SkeletronHead=35，ai[1] 指向的母体）。</summary>
    private static NPC? GetHead(NPC npc)
    {
        int idx = (int)npc.ai[1];
        if (idx < 0 || idx >= Main.maxNPCs) return null;
        var head = Main.npc[idx];
        return head != null && head.active && head.type == NPCID.SkeletronHead ? head : null;
    }

    /// <summary>向玩家方向抛掷一颗随机投骨骷髅小怪（449/450/451/452，Fargo 抛物线初速原样）。</summary>
    private void ThrowSkeleton(NPC npc, Player p)
    {
        // Fargo：val3 = 玩家Top - 手Center + 随机圆(80)；val3.X/=60；val3.Y = val3.Y/60 - 0.5×0.4×60
        Vector2 vel = p.Top - npc.Center + UtilsRandomCircular(Main.rand, ThrowJitterRadius);
        vel.X /= ThrowVelocityDiv;
        vel.Y = vel.Y / ThrowVelocityDiv - 0.5f * ThrowArcGravity * ThrowVelocityDiv;
        int type = Main.rand.Next(4) switch
        {
            0 => NPCID.BoneThrowingSkeleton,     // 449
            1 => NPCID.BoneThrowingSkeleton2,    // 450
            2 => NPCID.BoneThrowingSkeleton3,    // 451
            _ => NPCID.BoneThrowingSkeleton4,    // 452
        };
        int who = NPC.NewNPC(new EntitySource_Parent(npc), (int)npc.Center.X, (int)npc.Center.Y, type, 0, 0f, 0f, 0f, 0f, 255);
        if (who < 0 || who >= Main.maxNPCs) return;
        var skel = Main.npc[who];
        skel.velocity = vel;
        skel.netUpdate = true;
        NetMessage.SendData(23, -1, -1, null, who, 0f, 0f, 0f, 0, 0, 0);
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

    /// <summary>随机圆向量（Fargo Utils.NextVector2Circular 等价：半径内均匀随机点）。</summary>
    private static Vector2 UtilsRandomCircular(Terraria.Utilities.UnifiedRandom rand, float radius)
    {
        double angle = rand.NextDouble() * Math.PI * 2.0;
        double r = Math.Sqrt(rand.NextDouble()) * radius;
        return new Vector2((float)(Math.Cos(angle) * r), (float)(Math.Sin(angle) * r));
    }
}
