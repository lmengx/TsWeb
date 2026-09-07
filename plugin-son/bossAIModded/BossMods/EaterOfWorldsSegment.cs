using Terraria;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;

namespace bossAIModded.BossMods;

/// <summary>
/// 世界吞噬者段/尾（NPCID.EaterofWorldsBody=14 / EaterofWorldsTail=15）Eternity-lite 强化。
/// 移植自 FargowiltasSouls v1.7.3.9 VanillaEternity.EaterofWorldsSegment/EaterofWorlds（本地反编译），
/// 保留"判定与数值层"，换壳原版 ID。
///
/// 后插桩架构说明：
///   - 段跟随/吸附由原版 AI_006 处理（ai[1] 指向前一段，位置硬拉），本类不做干预；
///   - 断链变头/变尾的分裂是原版行为，无需处理；
///   - Fargo 的"头 Coiling 时段拉回圆周"依赖 Coil 状态机（SafePreAI 完全接管），留待"接管引擎"。
/// </summary>
public sealed class EaterOfWorldsSegment : BossAIModBase
{
    // ---------- 可调参数 ----------
    private const int SegmentDamageMult = 2; // 段/尾 伤害 ×2（Fargo SetDefaults）
                                             // ⚠ 仅服务端生效：NPC 撞击伤害由客户端本地判定，
                                             //   服务器改 npc.damage 客户端不可见（忠实移植保留）

    // ═══ 命中 debuff（与头部一致：灵液/咒火/眩晕）═══
    private const int IchorDuration = 300;      // 灵液(Ichor 69) 5s
    private const int CursedDuration = 300;     // 咒火(CursedInferno 24) 5s
    private const int DazedDuration = 180;      // 眩晕(Dazed 160) 3s
    private const int DebuffApplyInterval = 30; // 同一玩家两次施加的最小间隔 tick

    // ---------- 实例状态 ----------
    private readonly int[] _debuffCd = new int[255];
    private bool _statsApplied;

    public override void Tick(NPC npc)
    {
        // 出场免伤：免伤期 npc.defense=99999（原版 SuperArmor：伤害钳到 1），
        // 结束恢复原防御（defDefense），仅在变化时 netUpdate 广播。
        bool inGrace = IsInSpawnGrace(npc.whoAmI);
        int graceDefense = inGrace ? 99999 : npc.defDefense;
        if (npc.defense != graceDefense)
        {
            npc.defense = graceDefense;
            npc.netUpdate = true;
        }

        // 冷却节拍
        for (var i = 0; i < Main.maxPlayers; i++)
        {
            if (_debuffCd[i] > 0) _debuffCd[i]--;
        }

        // 生成首 tick：伤害 ×2（Fargo SetDefaults）
        if (!_statsApplied)
        {
            _statsApplied = true;
            npc.damage *= SegmentDamageMult;
            npc.netUpdate = true;
        }

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
