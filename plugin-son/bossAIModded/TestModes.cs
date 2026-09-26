using Terraria;
using TerrariaApi.Server;

namespace bossAIModded;

/// <summary>
/// 无敌方案对比测试（仅测试用，勿在生产开启）。
///
/// 背景：出场免伤若用 npc.defense=99999（或 npc.immortal=true）实现，只在服务器端生效——
/// NPCUpdate(23) 包不携带 defense/immortal/dontTakeDamage，客户端本地伤害计算（飘字+本地扣血）
/// 永远用类型静态防御，因此攻击者客户端"看不到"免伤。本类提供两个可开关的测试方案，
/// 开启后对服务器中所有敌怪生效，用于实机对比两种机制的客户端表现差异：
///
///   A. ClampDamage（服务端钳制）：NpcStrike 钩子 args.Handled=true 吞掉所有伤害。
///      预期：服务器血条 0 扣血（所有客户端血条正确），但攻击者客户端本地飘字照旧（"不生效"）。
///   B. AiRewrite（改写 ai）：改写 npc.ai[] 让客户端跑原版 AI 时自己推导出"不可受伤"。
///      预期：客户端本地也推导出无敌 → 飘字消失（客户端观感一致）。注意：原版只有极少数
///      AI 有 ai 驱动的无敌窗口（1.4.5.8 反编译实证仅 AI_112_FairyCritter 的 ai[2]&gt;1f 等），
///      不存在"所有敌怪通用"的 ai 无敌值——本测试按 aiStyle 分派已知窗口 + 哨兵值观察同步。
/// </summary>
public static class TestModes
{
    /// <summary>方案 A：服务端钳制伤害（NpcStrike 中 args.Handled = true 吞掉所有伤害）。</summary>
    public static bool ClampDamage;

    /// <summary>方案 B：改写 ai[] 让客户端跑原版 AI 时自己推导无敌。</summary>
    public static bool AiRewrite;

    /// <summary>是否任一测试模式开启。</summary>
    public static bool AnyActive => ClampDamage || AiRewrite;

    /// <summary>是否为可测试的敌怪（active + 非友好 + 有效 netID；测试面向"所有敌怪"）。</summary>
    private static bool IsTestableHostile(NPC npc)
        => npc != null && npc.active && !npc.friendly && npc.netID != 0;

    /// <summary>
    /// 方案 A：NpcStrike 服务端钳制。返回 true 表示已吞掉这次 strike（调用方应跳过后续处理）。
    /// 仅测试模式开启且目标是敌怪时钳制；其余情况返回 false 走原逻辑。
    /// </summary>
    public static bool TryClampStrike(NPC npc, NpcStrikeEventArgs args)
    {
        if (!ClampDamage || !IsTestableHostile(npc) || args == null)
        {
            return false;
        }
        // Handled=true → OTAPI 不执行原 StrikeNPC → 服务器端不扣血、不广播生命变化。
        // 攻击者客户端本地已先扣血+飘字（28 包已发），下次 23 包同步时血条跳回——即"服务端钳制不生效于客户端观感"。
        args.Handled = true;
        return true;
    }

    /// <summary>
    /// 方案 B：改写 ai[]。返回 true 表示已改写（调用方应跳过后续处理）。
    /// 改写后 netUpdate=true 强制广播 23 包（ai 是 23 包同步字段），客户端据此重跑原版 AI。
    /// </summary>
    public static bool TryRewriteAi(NPC npc)
    {
        if (!AiRewrite || !IsTestableHostile(npc))
        {
            return false;
        }
        RewriteAi(npc);
        npc.netUpdate = true;
        return true;
    }

    /// <summary>
    /// 按 aiStyle 写入"原版 AI 恰好识别的无敌窗口"值（1.4.5.8 反编译实证，见注释）：
    ///   - aiStyle 112（仙灵 AI_112_FairyCritter）：ai[2]&gt;1f → 客户端本地 dontTakeDamage=true（NPC.cs:53349）
    ///   - 其余：ai[0]=-999f 哨兵（对齐史莱姆王 AI_015 冻结值；原版 AI 对非法值多空转，
    ///     行为异常属预期——用于验证 ai 改写确实同步到客户端并改变其本地 AI 状态）
    /// </summary>
    private static void RewriteAi(NPC npc)
    {
        if (npc.aiStyle == 112)
        {
            npc.ai[0] = 0f;
            npc.ai[1] = 0f;
            npc.ai[2] = 2f; // >1f → dontTakeDamage（客户端本地推导无敌）
            npc.ai[3] = 0f;
            return;
        }
        npc.ai[0] = -999f;
        npc.ai[1] = 0f;
        npc.ai[2] = 0f;
        npc.ai[3] = 0f;
    }
}
