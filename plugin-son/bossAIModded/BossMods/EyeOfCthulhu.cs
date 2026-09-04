using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using TShockAPI;

namespace bossAIModded.BossMods;

/// <summary>
/// 克眼（NPCID.EyeofCthulhu = 4）Eternity-lite 强化（第一版：后插桩安全子集）。
/// 移植自 FargowiltasSouls v1.7.3.9 VanillaEternity.EyeofCthulhu（本地反编译），保留"判定与数值层"。
///
/// 原版克眼 AI（Terraria aiStyle 4，1.4.5.8 源码实证）状态备忘：
///   ai[0]==0 一阶段：ai[1] 0=盘旋(期间按距离召仆从 5) 1=起冲 2=dash 滑行(ai[2]计时/ai[3]轮次, 3 连)
///   ai[0]==1/2 半血变身自旋(ai[1]++ 至 100) → ai[0]==3 二阶段连续 dash 循环：
///       ai[1] 0=贴位 1=起冲 2=dash 滑行 3=预瞄(单帧算弹道) 4=冲刺 5=低空迂回(ai[2] 70 → 回 3)
///
/// 已迁移（Fargo 数值原样；全部为"后插桩追加"，不干预原版状态机流转）：
///   1) dash 滑行漂移：position += velocity × k（距离越远越飘，二阶段翻倍）
///   2) dash 撒镰：滑行期每 N tick 沿速度方向掷一枚镰刀弹
///      换壳 44 DemonSickle（hostile 敌弹；⚠45 DemonScythe 是 friendly 玩家弹会反打克眼）；
///      撞墙即消失（44 原生 tileCollide=true，且 27 网络包不同步 tileCollide → 服务端改无效，定案接受）；
///      伤害 = defDamage×factor 但 clamp ≤ ScytheDamageMax
///      （Fargo 用 defDamage，但高难/增强服的 defDamage 会被放大到 300+，须封顶）；
///      44 原生 AI 带 ai[0]∈[30,100) 每 tick×1.06 自加速，钉 ai[0]=200 段 → 恒定可控弹速
///   3) 二阶段预瞄(ai[1]==3) 8 向镰刀环（每轮冲刺组开火一次）；≤10% 血升级 12 向 + 撒镰 2 tick/发
///   4) 命中 debuff：克眼本体接触 或 己方镰刀弹命中玩家 →
///      中毒(Poisoned 20)+着火(OnFire 24)+破损盔甲(BrokenArmor 36)，各 5 秒（300 tick）
///      （Fargo 原接触 debuff 是自定义 CurseOfTheMoon/Berserked，无原版等价 → 换壳原版三 debuff）
///
/// 未迁移（原版客户端无消费能力 / 需冻结接管原版 AI，均不入首版，见 README）：
///   - 换色 shader / 换色 Boss 头 / Dust 229/266 / AddLight / ForceRoar 音效（视觉层）
///   - SpectralEoC 幽灵复制体、GlowRing 光环（自定义实体/全屏 shader）
///   - 二阶段"消失→瞬移→显形强冲"重写与 ≤10% 终局完整三段循环：Fargo 靠 SafePreAI return false
///     冻结原版 AI 完全接管；本插件是 NpcAIUpdate 后插桩无法冻结 → 留待"接管引擎"再迁
///   - Masochist 难度分支（额外仆从补召、Shadowflame 等）
/// </summary>
public sealed class EyeOfCthulhu : BossAIModBase
{
    // ---------- 可调参数（依实机手感微调） ----------
    private const float DriftKPhase1 = 0.25f;   // 一阶段 dash 漂移系数（Fargo: ai[0]==0 → 0.25）
    private const float DriftKPhase2 = 0.50f;   // 二阶段 dash 漂移系数（Fargo: ai[0]==3 → 0.5）
    private const float DriftKMin = 0.15f;      // 近距离（<~700px）漂移下限（Fargo: 0.15）
    private const float DriftKMax = 0.50f;      // 远距离漂移上限（Fargo: 0.5）
    private const float DriftDistRange = 1000f; // 距离→漂移 lerp 参考距（Fargo: /1000）
    private const int DriftSyncEvery = 2;       // 漂移时每 N tick 主动推一次 23（客户端本地预测无此位移）

    private const int ScytheEveryPhase1 = 6;    // 一阶段撒镰间隔 tick（Fargo: 6）
    private const int ScytheEveryPhase2 = 4;    // 二阶段撒镰间隔 tick（Fargo final 段用 2，非 final 用 6，中间档 4）
    private const int ScytheEveryBerserk = 2;   // ≤10% 血撒镰间隔 tick（Fargo final: 2）
    private const float ScytheSpeed = 8f;       // 弹速（44 AI 自加速已被钉死，此为恒定终速）
    private const float ScytheDamageFactor = 1f;// 镰刀伤害 = defDamage × factor（Fargo: ScaledProjectileDamage(defDamage)）
    private const int ScytheDamageMax = 15;      // ★damage 字段上限：客户端判伤恒 ×2（Projectile.cs 判伤段 DamageVar(damage)×2），
                                                 //   故实际扣血≈字段×2：30→≈60 封顶；普通服 defDamage≈20 → ≈40
                                                 //   ⚠高难/增强服 defDamage 会被放大到 150~250+，不封顶会打出 300~500（×2）

    private const int RingWays = 8;             // 预瞄期镰刀环数量（Fargo: 8）
    private const int RingWaysBerserk = 12;     // ≤10% 血镰刀环数量
    private const float RingSpeed = 6f;         // 环弹弹速
    private const float BerserkLifeRatio = 0.10f; // 狂化血线（Fargo: life <= lifeMax * 0.1）

    private const int ScytheProjectile = ProjectileID.DemonSickle; // 换壳：原版恶魔镰（44），hostile 敌弹

    private const int DebuffDuration = 300;          // 中毒/着火/破损盔甲时长 tick（5 秒）
    private const int DebuffApplyInterval = 30;      // 同一玩家两次施加的最小间隔 tick（防弹穿身逐 tick 重复叠）

    // ---------- 实例状态（服务器内存即可） ----------
    private int _scytheTimer;        // 撒镰节拍
    private int _driftSyncCounter;   // 漂移同步节拍
    private bool _wasDashing;        // 上一 tick 是否处于滑行
    private bool _ringFiredThisAim;  // 本轮预瞄(ai[1]==3)是否已放环

    private readonly HashSet<int> _scythes = new();  // 己方存活镰刀弹（用于命中叠 debuff）
    private readonly int[] _debuffCd = new int[255]; // 每玩家命中冷却（按 whoAmI 索引）

    public override void Tick(NPC npc)
    {
        // 冷却节拍（无弹无接触时也要走完，玩家才能重新被命中）
        for (var i = 0; i < Main.maxPlayers; i++)
        {
            if (_debuffCd[i] > 0) _debuffCd[i]--;
        }

        // Fargo 后置逻辑只作用于生命≥0 且在一/二形态主体（ai[0]==0 或 3）期间的"追加攻击"。
        var ai0 = npc.ai[0];
        var ai1 = npc.ai[1];
        bool berserk = npc.life <= npc.lifeMax * BerserkLifeRatio;

        // ---------- dash 滑行期（ai[1]==2 在 ai[0]==0 与 ai[0]==3 都代表"滑行"） ----------
        bool dashing = (ai0 == 0f || ai0 == 3f) && ai1 == 2f && npc.velocity.LengthSquared() > 9f;
        if (dashing && TryGetTarget(npc, out var p))
        {
            // 1) 漂移：本 tick 位置额外顺速度方向滑一段（Fargo 语义"冲刺更飘、更难瞄准"）
            float dx = Math.Abs(p.Center.X - npc.Center.X);
            float k = ai0 == 0f
                ? MathHelper.Lerp(DriftKMin, DriftKMax, Math.Clamp(dx / DriftDistRange, 0f, 1f))
                : DriftKPhase2;
            npc.position += npc.velocity * k;

            // 2) 撒镰：滑行期节拍掷弹（沿当前速度方向）
            if (++_scytheTimer >= ScytheInterval(berserk, ai0))
            {
                _scytheTimer = 0;
                SpawnScythe(npc, npc.Center, Vector2.Normalize(npc.velocity) * ScytheSpeed);
            }

            // 漂移是"服务器权威追加位移"，客户端本地原版预测追不上 → 周期性主动推 23 防顿挫
            if (++_driftSyncCounter >= DriftSyncEvery)
            {
                _driftSyncCounter = 0;
                PushNpcSync(npc);
            }
            _wasDashing = true;
        }
        else
        {
            if (_wasDashing)
            {
                // 离开滑行：清节拍，撒镰只在滑行期进行
                _scytheTimer = 0;
                _driftSyncCounter = 0;
                _wasDashing = false;
            }
        }

        // ---------- 二阶段预瞄(ai[1]==3)镰刀环：每轮冲刺组开火一次（Fargo ScytheRingIsOnCD 语义） ----------
        if (ai0 == 3f && ai1 == 3f)
        {
            if (!_ringFiredThisAim && TryGetTarget(npc, out _))
            {
                _ringFiredThisAim = true;
                SpawnScytheRing(npc, berserk ? RingWaysBerserk : RingWays, RingSpeed);
            }
        }
        else if (_ringFiredThisAim)
        {
            _ringFiredThisAim = false; // 离开预瞄状态 → 允许下一轮再放
        }

        // ---------- 4) 命中 debuff：克眼本体接触 / 己方镰刀弹命中玩家 ----------
        if (TryGetTarget(npc, out var contact) && npc.Hitbox.Intersects(contact.Hitbox))
        {
            ApplyDebuffs(contact.whoAmI);
        }
        TickScytheHits();
    }

    /// <summary>狂化（≤10% 血）时 Fargo 把撒镰压缩到 2 tick/发，其余按阶段。</summary>
    private static int ScytheInterval(bool berserk, float ai0)
    {
        if (berserk) return ScytheEveryBerserk;
        return ai0 == 0f ? ScytheEveryPhase1 : ScytheEveryPhase2;
    }

    public override void OnKilled(NPC npc)
    {
        _scythes.Clear();
    }

    // ---------- 私有实现 ----------

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

    /// <summary>强制立即向全体客户端推送该 NPC 的 23 号包（位置/速度/ai 对齐）。</summary>
    private static void PushNpcSync(NPC npc)
    {
        npc.netUpdate = true;
        NetMessage.SendData(23, -1, -1, null, npc.whoAmI, 0f, 0f, 0f, 0, 0, 0);
    }

    /// <summary>
    /// 服务器造一颗己方镰刀弹（44 hostile；owner=255；1458 NewProjectile 自动广播 27）。
    /// 恒定弹速：44 AI 在 ai[0]∈[30,100) 每 tick ×1.06 自加速 → 钉 ai[0]=200 段取消加速。
    /// （不做穿墙：44 原生 tileCollide=true 且 27 包不同步 tileCollide，服务端改客户端无效。）
    /// </summary>
    private int SpawnScythe(NPC npc, Vector2 pos, Vector2 vel)
    {
        // 判伤机制备忘：hostile 弹对玩家的伤害在客户端判定，公式 = Main.DamageVar(damage)×2（Projectile.cs），
        // 因此这里传给 NewProjectile 的 damage 是“目标扣血的一半”，并 clamp 防高难服 defDamage 巨大。
        int dmg = Math.Min((int)(npc.defDamage * ScytheDamageFactor), ScytheDamageMax);
        int who = Projectile.NewProjectile(new EntitySource_Parent(npc), pos.X, pos.Y, vel.X, vel.Y,
            ScytheProjectile, dmg, 0f, 255);
        if (who >= 0 && who < Main.maxProjectiles)
        {
            var pr = Main.projectile[who];
            pr.ai[0] = 200f;
            _scythes.Add(who);
        }
        return who;
    }

    /// <summary>以 NPC 中心铺一圈同速镰刀弹。</summary>
    private void SpawnScytheRing(NPC npc, int ways, float speed)
    {
        for (var i = 0; i < ways; i++)
        {
            var ang = MathHelper.TwoPi * i / ways;
            SpawnScythe(npc, npc.Center, new Vector2((float)Math.Cos(ang), (float)Math.Sin(ang)) * speed);
        }
    }

    /// <summary>扫己方镰刀弹：清理失效 + 与玩家相交则叠 debuff（每玩家有施加冷却）。</summary>
    private void TickScytheHits()
    {
        if (_scythes.Count == 0) return;
        _scythes.RemoveWhere(id => id < 0 || id >= Main.maxProjectiles ||
                                    !Main.projectile[id].active || Main.projectile[id].type != ScytheProjectile);
        if (_scythes.Count == 0) return;

        for (var i = 0; i < Main.maxPlayers; i++)
        {
            var pl = Main.player[i];
            if (pl == null || !pl.active || pl.dead || _debuffCd[i] > 0) continue;
            var rect = pl.Hitbox;
            foreach (var id in _scythes)
            {
                if (Main.projectile[id].Hitbox.Intersects(rect))
                {
                    ApplyDebuffs(i);
                    break;
                }
            }
        }
    }

    /// <summary>给玩家上三连 debuff（受 _debuffCd 节流；走 TShock TSPlayer.SetBuff 保证服务端同步）。</summary>
    private void ApplyDebuffs(int who)
    {
        if (who < 0 || who >= Main.maxPlayers || _debuffCd[who] > 0) return;
        _debuffCd[who] = DebuffApplyInterval;
        var tp = TShock.Players[who];
        if (tp == null) return;
        tp.SetBuff(BuffID.Poisoned, DebuffDuration, false);
        tp.SetBuff(BuffID.OnFire, DebuffDuration, false);
        tp.SetBuff(BuffID.BrokenArmor, DebuffDuration, false);
    }
}
