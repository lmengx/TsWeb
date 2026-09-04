using Terraria;
using TerrariaApi.Server;

namespace bossAIModded.BossMods;

/// <summary>
/// 单个 Boss 的 AI 魔改实现基类。
/// 实例由 BossAIModded 按 npc.whoAmI 路由池持有，生命周期绑定对应 NPC 槽。
/// </summary>
public abstract class BossAIModBase
{
    /// <summary>NpcAIUpdate 之后每 tick 调用（原版 AI 已执行完，属于"后插桩"语义）。</summary>
    public virtual void Tick(NPC npc) { }

    /// <summary>
    /// 服务器权威伤害结算点（ServerApi.Hooks.NpcStrike）。
    /// 返回 true 表示已吞掉这次 strike（args.Handled 已置位）。
    /// </summary>
    public virtual bool OnStrike(NPC npc, NpcStrikeEventArgs args) => false;

    /// <summary>Boss 死亡（NpcKilled）时调用，用于清理。</summary>
    public virtual void OnKilled(NPC npc) { }
}
