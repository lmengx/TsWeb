using System.Reflection;
using MonoMod.RuntimeDetour;
using Terraria;
using TShockAPI;

namespace TShockData;

public static class BossLimit
{
    private static Hook? _hook;
    private static bool _initialized;

    private delegate bool OrigHandleSpawnBoss(GetDataHandlerArgs args);

    public static void Initialize()
    {
        if (_initialized) return;

        var method = typeof(GetDataHandlers).GetMethod("HandleSpawnBoss",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (method == null)
        {
            TShock.Log.ConsoleError("[BossLimit] 未找到 HandleSpawnBoss 方法，Hook 失败");
            return;
        }

        _hook = new Hook(method, OnHandleSpawnBoss);
        TShockAPI.Commands.ChatCommands.Add(
            new Command("tshock.admin", BossLimitCommand, "bosslimit", "进度锁"));

        _initialized = true;
        TShock.Log.ConsoleInfo("[BossLimit] Boss 召唤限制已初始化");
    }

    public static void Dispose()
    {
        if (!_initialized) return;
        _hook?.Dispose();
        _hook = null;
        _initialized = false;
        TShock.Log.ConsoleInfo("[BossLimit] Boss 召唤限制已卸载");
    }

    private static bool OnHandleSpawnBoss(OrigHandleSpawnBoss orig, GetDataHandlerArgs args)
    {
        var pos = args.Data.Position;
        short thingType;
        try
        {
            args.Data.Seek(2, System.IO.SeekOrigin.Current);
            var buf = new byte[2];
            args.Data.Read(buf, 0, 2);
            thingType = BitConverter.ToInt16(buf, 0);
        }
        finally { args.Data.Position = pos; }

        string playerName = args.Player?.Name ?? "???";

        if (thingType < 0)
            return orig(args);

        string bossName = Lang.GetNPCNameValue(thingType);
        var config = AutoRegister.Config;

        // ===== 根据模式判断 =====

        switch (config.BossLimitMode)
        {
            case "disabled":
                // 模式1：不做任何限制，直接放行
                return orig(args);

            case "playerlimit":
                // 模式2：按最低人数限制（原有逻辑）
                return HandlePlayerLimitMode(orig, args, thingType, bossName, playerName, config);

            case "killrequired":
                // 模式3：不允许召唤没有打过的boss
                return HandleKillRequiredMode(orig, args, thingType, bossName, playerName, config);

            default:
                return orig(args);
        }
    }

    /// <summary>
    /// 模式2：按最低人数限制 — 未击败的boss需要最低在线人数才能召唤
    /// </summary>
    private static bool HandlePlayerLimitMode(
        OrigHandleSpawnBoss orig, GetDataHandlerArgs args,
        short thingType, string bossName, string playerName, RegisterConfig config)
    {
        int killCount;
        try { killCount = BossProgress.GetKillCount(thingType); }
        catch { return orig(args); }

        // 已击败过的boss允许召唤
        if (killCount > 0)
        {
            TShock.Log.ConsoleInfo($"[BossLimit] {playerName} 召唤了 {bossName}");
            return orig(args);
        }

        // 有 spawnboss 权限的玩家不受限制
        if (args.Player.HasPermission(Permissions.spawnboss))
        {
            TShock.Log.ConsoleInfo($"[BossLimit] {playerName} 召唤了 {bossName}");
            return orig(args);
        }

        int online = GetActivePlayerCount();
        if (online >= config.BossLimitMinPlayers)
        {
            TShock.Log.ConsoleInfo($"[BossLimit] {playerName} 召唤了 {bossName}");
            return orig(args);
        }

        TShock.Log.ConsoleInfo($"[BossLimit] {playerName} 试图召唤 {bossName}，因人数不足({online}/{config.BossLimitMinPlayers})被阻止");
        args.Player.SendErrorMessage(
            $"当前在线人数不足 {config.BossLimitMinPlayers} 人，无法召唤未击败的 Boss");
        return true;
    }

    /// <summary>
    /// 模式3：不允许召唤没有打过的boss — 必须至少击败过一次
    /// </summary>
    private static bool HandleKillRequiredMode(
        OrigHandleSpawnBoss orig, GetDataHandlerArgs args,
        short thingType, string bossName, string playerName, RegisterConfig config)
    {
        int killCount;
        try { killCount = BossProgress.GetKillCount(thingType); }
        catch { return orig(args); }

        // 已击败过的boss允许召唤
        if (killCount > 0)
        {
            TShock.Log.ConsoleInfo($"[BossLimit] {playerName} 召唤了 {bossName}");
            return orig(args);
        }

        // 有 spawnboss 权限的玩家不受限制
        if (args.Player.HasPermission(Permissions.spawnboss))
        {
            TShock.Log.ConsoleInfo($"[BossLimit] {playerName} 召唤了 {bossName}");
            return orig(args);
        }

        TShock.Log.ConsoleInfo($"[BossLimit] {playerName} 试图召唤未击败的 {bossName}，被阻止");
        args.Player.SendErrorMessage("此 Boss 尚未被击败过，无法召唤");
        return true;
    }

    private static void BossLimitCommand(CommandArgs args)
    {
        var config = AutoRegister.Config;

        if (args.Parameters.Count == 0)
        {
            ShowStatus(args, config);
            return;
        }

        switch (args.Parameters[0].ToLower())
        {
            case "mode":
            case "模式":
                HandleModeCommand(args, config);
                break;

            case "playernum":
            case "人数":
                HandlePlayerNumCommand(args, config);
                break;

            case "on":
                // 快速开启：切换到 playerlimit 模式
                config.BossLimitMode = "playerlimit";
                config.BossLimitEnabled = true;
                AutoRegister.SaveConfig();
                args.Player.SendSuccessMessage($"Boss 限制已开启（模式: playerlimit，最低人数: {config.BossLimitMinPlayers}）");
                TShock.Log.ConsoleInfo($"[BossLimit] {args.Player.Name} 开启了 Boss 限制（playerlimit）");
                break;

            case "off":
                config.BossLimitMode = "disabled";
                config.BossLimitEnabled = false;
                AutoRegister.SaveConfig();
                args.Player.SendSuccessMessage("Boss 限制已关闭");
                TShock.Log.ConsoleInfo($"[BossLimit] {args.Player.Name} 关闭了 Boss 限制");
                break;

            default:
                args.Player.SendErrorMessage("无效参数");
                ShowHelp(args);
                break;
        }
    }

    private static void ShowStatus(CommandArgs args, RegisterConfig config)
    {
        string modeDesc = config.BossLimitMode switch
        {
            "disabled" => "[c/ff0000:关闭]",
            "playerlimit" => $"[c/00ff00:人数限制]（最低 {config.BossLimitMinPlayers} 人）",
            "killrequired" => "[c/00ff00:需击败过]",
            _ => "[c/ffff00:未知]"
        };
        args.Player.SendInfoMessage($"Boss 限制模式: {modeDesc}");
        args.Player.SendInfoMessage("─ 命令 ─────────────────────");
        ShowHelp(args);
    }

    private static void ShowHelp(CommandArgs args)
    {
        args.Player.SendInfoMessage("/bosslimit mode disabled       — 不做任何限制");
        args.Player.SendInfoMessage("/bosslimit mode playerlimit    — 按最低人数限制");
        args.Player.SendInfoMessage("/bosslimit mode killrequired   — 不允许召唤未击败的 Boss");
        args.Player.SendInfoMessage("/bosslimit playernum <人数>    — 设置最低在线人数（仅 playerlimit 模式）");
        args.Player.SendInfoMessage("/bosslimit on                  — 快速开启（切换到 playerlimit 模式）");
        args.Player.SendInfoMessage("/bosslimit off                 — 关闭限制");
    }

    private static void HandleModeCommand(CommandArgs args, RegisterConfig config)
    {
        if (args.Parameters.Count < 2)
        {
            args.Player.SendInfoMessage($"当前模式: {config.BossLimitMode}");
            args.Player.SendInfoMessage("可用模式: disabled / playerlimit / killrequired");
            return;
        }

        var newMode = args.Parameters[1].ToLower();
        if (newMode != "disabled" && newMode != "playerlimit" && newMode != "killrequired")
        {
            args.Player.SendErrorMessage("无效模式！可用模式: disabled / playerlimit / killrequired");
            return;
        }

        config.BossLimitMode = newMode;
        config.BossLimitEnabled = newMode != "disabled";
        AutoRegister.SaveConfig();

        string modeName = newMode switch
        {
            "disabled" => "不做任何限制",
            "playerlimit" => "按最低人数限制",
            "killrequired" => "不允许召唤未击败的 Boss",
            _ => newMode
        };
        args.Player.SendSuccessMessage($"Boss 限制模式已设置为: {modeName}");
        TShock.Log.ConsoleInfo($"[BossLimit] {args.Player.Name} 设置了模式为 {newMode}");
    }

    private static void HandlePlayerNumCommand(CommandArgs args, RegisterConfig config)
    {
        if (args.Parameters.Count < 2 || !int.TryParse(args.Parameters[1], out var num) || num <= 0)
        {
            args.Player.SendErrorMessage("用法: /bosslimit playernum <人数>，人数必须为正整数");
            return;
        }
        config.BossLimitMinPlayers = num;
        AutoRegister.SaveConfig();
        args.Player.SendSuccessMessage($"Boss 限制最低人数已更新为: {num} 人");
        TShock.Log.ConsoleInfo($"[BossLimit] {args.Player.Name} 设置最低人数为 {num}");
    }

    private static int GetActivePlayerCount()
    {
        int count = 0;
        foreach (var player in TShock.Players)
            if (player != null && player.Active) count++;
        return count;
    }
}
