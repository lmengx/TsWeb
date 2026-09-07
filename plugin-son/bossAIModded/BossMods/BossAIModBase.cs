using System.Reflection;
using Terraria;
using Terraria.DataStructures;
using TerrariaApi.Server;

namespace bossAIModded.BossMods;

/// <summary>
/// 单个 Boss 的 AI 魔改实现基类。
/// 实例由 BossAIModded 按 npc.whoAmI 路由池持有，生命周期绑定对应 NPC 槽。
/// </summary>
public abstract class BossAIModBase
{
    /// <summary>
    /// 客户端 hostile 弹对玩家判伤链路（1.4.5.8 源码实证，见 scripts/_tml_full/Terraria/Projectile.cs:13805）：
    ///   int num49 = Main.DamageVar(damage, -owner.luck) * 2;  →  Player.Hurt(...)  // 再进减防
    /// 即服务器填的 damage 字段 = "最终结算的一半"（原版敌弹均按此"半伤"语义查表填值，与 defDamage 无关）。
    /// 实机标定：本服(大师+属性强化)观察 克眼填15→单发结算≈70、尖刺填150→≈700，链路总系数≈14/3(≈4.667)。
    /// 如需对齐纯 vanilla 源码链路(×2±15%浮动)，把 ResultBias 改为 2f 即可；本服实测值即为 14f/3f。
    /// </summary>
    public const float ResultBias = 14f / 3f;

    /// <summary>以「期望实际扣血」为单位反算网络 damage 字段（结果=字段×ResultBias）。</summary>
    protected static int FieldForResult(int wantResult)
        => Math.Max(1, (int)Math.Round(wantResult / ResultBias));

    /// <summary>NpcAIUpdate 之后每 tick 调用（原版 AI 已执行完，属于"后插桩"语义）。</summary>
    public virtual void Tick(NPC npc) { }

    /// <summary>
    /// 服务器权威伤害结算点（ServerApi.Hooks.NpcStrike）。
    /// 返回 true 表示已吞掉这次 strike（args.Handled 已置位）。
    /// </summary>
    public virtual bool OnStrike(NPC npc, NpcStrikeEventArgs args) => false;

    /// <summary>Boss 死亡（NpcKilled）时调用，用于清理。</summary>
    public virtual void OnKilled(NPC npc) { }

    /// <summary>跨版本安全读取 PlayerDeathReason 的来源弹幕类型（公开属性优先，私有字段兜底；读不到返回 0）。</summary>
    public static int GetSourceProjectileType(PlayerDeathReason reason)
    {
        if (reason == null)
        {
            return 0;
        }
        try
        {
            var prop = reason.GetType().GetProperty("SourceProjectileType");
            if (prop != null)
            {
                return Convert.ToInt32(prop.GetValue(reason));
            }
            var field = reason.GetType().GetField("_sourceProjectileType", BindingFlags.Instance | BindingFlags.NonPublic);
            return field != null ? Convert.ToInt32(field.GetValue(reason)) : 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// 链级生成时刻（出场免伤计时）。取路由池 SpawnTimes 登记值：世界吞噬者段变头 whoAmI 不变，
    /// 沿用首次出现时间 → 整条链统一"刚出现前 10 秒"；无登记时回退当前时刻（不触发免伤）。
    /// </summary>
    protected static DateTime GetChainSpawnTime(int whoAmI)
        => BossAIModded.SpawnTimes.TryGetValue(whoAmI, out var t) ? t : DateTime.UtcNow;

    /// <summary>是否处于出场免伤期（链生成后 SpawnDamageCapDuration 内）。</summary>
    public static bool IsInSpawnGrace(int whoAmI)
        => DateTime.UtcNow - GetChainSpawnTime(whoAmI) < SpawnDamageCapDuration;

    /// <summary>出场免伤时长（与 Eater 类共享；段/头各自设置 defense=99999 时统一口径）。</summary>
    protected static readonly TimeSpan SpawnDamageCapDuration = TimeSpan.FromSeconds(10);
}
