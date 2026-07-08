using TShockAPI;
using TerrariaApi.Server;

namespace TShockData;

/// <summary>
/// BOSS 限制根命令路由
/// /bosslimit summon ...  → BossLimitSummon
/// /bosslimit quit   ...  → BossLimitQuit
/// </summary>
public static class BossLimit
{
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;

        TShockAPI.Commands.ChatCommands.Add(
            new Command("tshock.admin", BossLimitCommand, "bosslimit", "进度锁"));

        BossLimitSummon.Initialize();
        // BossLimitQuit 需要 TerrariaPlugin 实例，由 Main.cs 额外调用 InitQuit

        _initialized = true;
        TShock.Log.ConsoleInfo("[BossLimit] BOSS 限制模块已初始化");
    }

    /// <summary>
    /// 单独初始化退出惩罚（需要 plugin 实例）
    /// </summary>
    public static void InitQuit(TerrariaPlugin plugin)
    {
        BossLimitQuit.Initialize(plugin);
    }

    public static void Dispose()
    {
        if (!_initialized) return;

        BossLimitSummon.Dispose();
        BossLimitQuit.Dispose();
        _initialized = false;
        TShock.Log.ConsoleInfo("[BossLimit] BOSS 限制模块已卸载");
    }

    private static void BossLimitCommand(CommandArgs args)
    {
        // 无参数 → 显示所有子模块状态
        if (args.Parameters.Count == 0)
        {
            ShowAllStatus(args);
            return;
        }

        switch (args.Parameters[0].ToLower())
        {
            case "summon":
            case "召唤":
                BossLimitSummon.HandleCommand(args);
                return;

            case "quit":
            case "退出":
                BossLimitQuit.HandleCommand(args);
                return;
        }

        // 兼容旧版：直接 mode / playernum / on / off → 派发给 summon
        switch (args.Parameters[0].ToLower())
        {
            case "mode":
            case "模式":
            case "playernum":
            case "人数":
            case "on":
            case "off":
                // 插入 "summon" 前缀后派发
                args.Parameters.Insert(0, "summon");
                BossLimitSummon.HandleCommand(args);
                return;
        }

        args.Player.SendErrorMessage("未知子命令！可用: summon, quit");
        ShowHelp(args);
    }

    private static void ShowAllStatus(CommandArgs args)
    {
        args.Player.SendInfoMessage("══════════ BOSS 限制 ══════════");
        BossLimitSummon.ShowStatus(args, AutoRegister.Config);
        BossLimitQuit.ShowStatus(args);
        args.Player.SendInfoMessage("─ 子命令 ─────────────────────");
        ShowHelp(args);
    }

    private static void ShowHelp(CommandArgs args)
    {
        args.Player.SendInfoMessage("/bosslimit summon ...   — 召唤限制相关");
        args.Player.SendInfoMessage("/bosslimit quit ...     — 退出惩罚相关");
        args.Player.SendInfoMessage("/bosslimit              — 显示总状态");
    }
}
