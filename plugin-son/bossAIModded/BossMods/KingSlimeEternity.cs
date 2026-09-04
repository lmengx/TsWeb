using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;

namespace bossAIModded.BossMods;

/// <summary>
/// 史莱姆王（NPCID.KingSlime=50）Eternity-lite 强化。
/// 移植自 FargowiltasSouls v1.7.3.9 VanillaEternity.KingSlime（本地反编译），保留"判定与数值层"，
/// 新内容全部换壳为原版 ID（召唤=SlimeSpiked 535；弹幕=SpikedSlimeSpike 605；Buff=Slimed 137），
/// 视觉层（换色 shader/Dust/粒子演出）按不可达处理，不作迁移。
///
/// ⚠ 依赖原版 1.4.5.8 KS AI 的数值点已集中到文件顶部常量，实机手感可据此直接调。
/// </summary>
public sealed class KingSlimeEternity : BossAIModBase
{
    // ---------- 可调参数（依实机手感微调） ----------
    private const int SummonWaveCount = 6;            // 每波召唤数量（Fargo: 6）
    private const int SummonCooldown = 180;           // 召唤波冷却 tick（Fargo: 180）
    private const float SummonStart = 5f;             // 血量阈值除数起点（Fargo: SummonCounter=5 → 5/6..1/6 共 5 波）
    private const int SpecialJumpCooldown = 240;      // 大跳后冷却 tick（Fargo: 240）
    private const int SpecialJumpWindup = 60;         // 大跳蓄力前摇 tick（Fargo: 60）
    private const float SpecialJumpVY = -18f;         // 大跳垂直初速（Fargo: -18）
    private const float SpecialJumpPredictRange = 1000f; // 大跳水平预判距离（Fargo: 1000px）
    private const int SpikeRainInterval = 240;        // 狂暴尖刺雨间隔 tick（Fargo: 240）
    private const float BerserkLifeRatio = 0.66f;     // 狂暴血线（Fargo: life < 66%）
    private const int BuffTick = 90;                  // 接触黏液刷新时长 tick
    private const int SpikeHitDamage = 80;            // 期望单发结算（实际扣血）。字段由 FieldForResult 反算，
                                                      //   不再依赖 defDamage（该服 KS defDamage≈225，原 ×2/3 会填出 150→结算≈700）

    /// <summary>"当前在场的史莱姆王槽位"全局标记（语义同 Fargo EModeGlobalNPC.slimeBoss）。</summary>
    public static int SlimeBossWhoAmI = -1;

    // ---------- 实例状态（Fargo 同名实例字段，服务器内存即可） ----------
    private int _spikeRainCounter;
    private bool _landingAttackReady;
    private bool _currentlyJumping;
    private bool _specialJumping;
    private int _certainAttackCooldown;
    private float _jumpTimer;
    private int _specialJumpWindupTimer;
    private float _summonCounter = SummonStart;

    public override void Tick(NPC npc)
    {
        // ★ 原版 KS(AI_015) 的防卡墙传送抑制（根因修复，必须位于所有 return 分支之前）：
        // 原版在 CanHitLine 失败/高差>160px 时 ai[2]++，累积到 300 且落地 → ai[1]=5（60tick 无敌+隐藏
        // 前摇）→ base.Bottom = 传送点（瞬移）→ ai[1]=6。该瞬移即玩家看到的“中途异常突变位移一次”。
        // Fargo 以 [145,150) 钳制阻止其到 300，但窗口过窄——若在蓄力前摇/死亡演出（其 return 跳过本段）
        // 期间 ai[2] 从 <145 越过 150 就会永久脱锚。这里改为 >=145 一律钉 145（全覆盖），
        // 并同时把跳计时顶满，让“打不过的卡墙局面”改由我们自己的蓄力大跳（1000px 预判）追人。
        if (npc.ai[2] >= 145f)
        {
            if (_jumpTimer < 900f) _jumpTimer = 900f;
            npc.ai[2] = 145f;
        }

        SlimeBossWhoAmI = npc.whoAmI;

        if (_certainAttackCooldown > 0)
        {
            _certainAttackCooldown--;
        }

        // 蓄力前摇期间完全接管：冻结（写 ai[0]=-999 令原版 KS AI 空转）+ 站桩
        if (_specialJumpWindupTimer > 0)
        {
            npc.velocity = Vector2.Zero;
            npc.ai[0] = -999f;
            npc.netUpdate = true;
            if (--_specialJumpWindupTimer <= 0)
            {
                npc.ai[0] = -1f; // 恢复原版 AI，等待其自然起跳 → 下一 tick 由下方飞行分支注入大跳
                PushNpcSync(npc);
            }
            return;
        }

        // 掉血召唤波：血量每跌破 1/6 档（5/6→1/6）爆出一波尖刺史莱姆
        if (npc.GetLifePercent() < _summonCounter / 6f && _certainAttackCooldown <= 0)
        {
            _certainAttackCooldown = SummonCooldown;
            SpawnSlimeWave(npc);
            _summonCounter--;
        }

        // ---------- 落地攻击 / 蓄力大跳状态机（Fargo SafePreAI 移植） ----------
        if (_landingAttackReady)
        {
            if (npc.velocity.Y == 0f)
            {
                _landingAttackReady = false;
                if (_jumpTimer >= 900f && !_specialJumping && _certainAttackCooldown <= 0)
                {
                    _specialJumping = true;
                    _certainAttackCooldown = SpecialJumpCooldown;
                    _specialJumpWindupTimer = SpecialJumpWindup;
                }
                else if (_specialJumping)
                {
                    // 大跳落地 → 归还原版 ai[2]=150 状态机继续
                    _jumpTimer = 0f;
                    _specialJumping = false;
                    npc.ai[2] = 150f;
                    PushNpcSync(npc);
                }
            }
        }
        else if (npc.velocity.Y > 0f)
        {
            _landingAttackReady = true;
        }

        if (npc.velocity.Y < 0f)
        {
            if (!_currentlyJumping)
            {
                _currentlyJumping = true;
                if (_specialJumping)
                {
                    if (TryGetTarget(npc, out var p))
                    {
                        // 大跳起跳：垂直猛跳 + 水平预判玩家位置（1000px）
                        npc.velocity.Y = SpecialJumpVY;
                        int dir = Math.Sign(p.Center.X - npc.Center.X);
                        var predict = p.Center + Vector2.UnitX * SpecialJumpPredictRange * dir;
                        float airTime = Math.Abs(2f * npc.velocity.Y / NPC.gravity);
                        if (airTime > 0f)
                        {
                            npc.velocity.X = (predict.X - npc.Center.X) / airTime;
                        }
                        // 起跳帧主动推送：让客户端尽快对齐大跳轨道，减少飞行中/落地时的 snap
                        PushNpcSync(npc);
                    }
                }
                else
                {
                    // 普通跳跃弹道修正（Fargo 数值原样）
                    if (npc.HasValidTarget && TryGetTarget(npc, out var p2))
                    {
                        if (p2.Center.Y < npc.position.Y + npc.height - 240f)
                        {
                            npc.velocity.Y *= 1.5f; // 目标在头顶 240px 以上 → 跳得更高
                        }
                        float dx = Math.Abs(p2.Center.X - npc.Center.X);
                        if (dx > 0f)
                        {
                            float mult = dx / 700f;
                            mult *= mult;
                            mult += 1f;
                            mult = MathHelper.Clamp(mult, 1f, 3f);
                            npc.velocity.X *= mult;
                            npc.velocity.Y *= Math.Min((float)Math.Cbrt(mult), 1.5f);
                            npc.velocity.X += Math.Sign(npc.velocity.X) * 2.25f;
                            PushNpcSync(npc); // 修正帧主动推送（客户端本地跑原版 AI 预测不到该弹道）
                        }
                    }
                }
            }
        }
        else
        {
            _currentlyJumping = false;
        }

        // 大跳飞行中：计时 + 过冲修正 + 脚下撒刺
        if (_specialJumping && npc.velocity.Y != 0f)
        {
            _jumpTimer++;
            // 飞行中每 10 tick 主动推一次（客户端本地原版预测会逐渐跑偏，需持续纠偏）
            if (_jumpTimer % 10f == 0f)
            {
                npc.netUpdate = true;
            }
            if (TryGetTarget(npc, out var target))
            {
                int want = Math.Sign(target.Center.X - npc.Center.X);
                if (Math.Sign(npc.velocity.X) != want && Math.Abs(npc.Center.X - target.Center.X) > 250f && npc.velocity.Y > 0f)
                {
                    // 跳过头了：空中减速取消，归还状态机
                    npc.velocity.X /= 5f;
                    _specialJumping = false;
                    _jumpTimer = 0f;
                    npc.ai[2] = 150f;
                    PushNpcSync(npc);
                }
                else if (_jumpTimer % 5f < 1f && _jumpTimer % 15f > 1f)
                {
                    // 飞行中脚下撒一根刺（原版映射）
                    SpawnSpike(npc, npc.Bottom, Vector2.UnitY * 8f);
                }
            }
        }

        // 狂暴尖刺雨：血量 <66% 且落地状态，每 240 tick 以玩家为中心铺 25 列高空尖刺
        if (npc.HasValidTarget && npc.life < npc.lifeMax * BerserkLifeRatio && !_specialJumping && --_spikeRainCounter < 0)
        {
            _spikeRainCounter = SpikeRainInterval;
            SpawnSpikeRain(npc);
        }

        // 接触上黏液（近似原版 OnHitPlayer；接触期间持续刷新）
        if (TryGetTarget(npc, out var contact) && npc.Hitbox.Intersects(contact.Hitbox) && contact.active && !contact.dead)
        {
            TShock.Players[contact.whoAmI]?.SetBuff(BuffID.Slimed, BuffTick, false);
        }
    }

    // ---------- 私有实现 ----------

    /// <summary>强制立即向全体客户端推送该 NPC 的 23 号包（位置/速度/ai 对齐，缓解本地预测偏移导致的顿挫）。</summary>
    private static void PushNpcSync(NPC npc)
    {
        npc.netUpdate = true;
        NetMessage.SendData(23, -1, -1, null, npc.whoAmI, 0f, 0f, 0f, 0, 0, 0);
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

    /// <summary>掉血召唤波：体内爆出 6 只尖刺史莱姆（原版 NPCID.SlimeSpiked=535）。</summary>
    private void SpawnSlimeWave(NPC npc)
    {
        for (var i = 0; i < SummonWaveCount; i++)
        {
            int x = (int)(npc.position.X + Main.rand.Next(npc.width - 32));
            int y = (int)(npc.position.Y + Main.rand.Next(npc.height - 32));
            int who = NPC.NewNPC(new EntitySource_Parent(npc), x, y, NPCID.SlimeSpiked, 0, 0f, 0f, 0f, 0f, 255);
            if (who < 0 || who >= Main.maxNPCs)
            {
                continue;
            }
            var minion = Main.npc[who];
            // 对齐原版 AI_015 的召唤参数（上抛速度、ai[0]=-1000*rand(3) 落点延迟、ai[1]=-1），
            // 不要用 ±1 覆盖 ai[0]——535 原生 AI 需要负 ai[0] 做延迟初始化，写错会使其行为异常。
            minion.velocity = new Vector2(Main.rand.Next(-15, 16) * 0.1f, Main.rand.Next(-30, 1) * 0.1f);
            minion.ai[0] = -1000f * Main.rand.Next(3);
            minion.ai[1] = -1f;
            minion.netUpdate = true;
            NetMessage.SendData(23, -1, -1, null, who, 0f, 0f, 0f, 0, 0, 0);
        }
    }

    /// <summary>狂暴尖刺雨：玩家中心上方 500px、左右各 12 列（列距 110px）落下 605 尖刺。</summary>
    private void SpawnSpikeRain(NPC npc)
    {
        if (!TryGetTarget(npc, out var p))
        {
            return;
        }
        var anchor = p.Center + Vector2.UnitX * Main.rand.Next(-55, 56);
        for (var l = -12; l <= 12; l++)
        {
            var pos = anchor;
            pos.X += 110 * l;
            pos.Y -= 500f;
            SpawnSpike(npc, pos, Vector2.UnitY * 6f);
        }
    }

    /// <summary>服务器造一颗原版尖刺弹（owner=255；1458 原版 NewProjectile 会自动广播 27）。</summary>
    private void SpawnSpike(NPC npc, Vector2 pos, Vector2 vel)
    {
        Projectile.NewProjectile(new EntitySource_Parent(npc), pos.X, pos.Y, vel.X, vel.Y,
            ProjectileID.SpikedSlimeSpike, FieldForResult(SpikeHitDamage), 0f, 255, 0f, 0f, 0f);
    }

    public override void OnKilled(NPC npc)
    {
        if (SlimeBossWhoAmI == npc.whoAmI)
        {
            SlimeBossWhoAmI = -1;
        }
    }
}
