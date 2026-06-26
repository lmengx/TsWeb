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

        int killCount;
        try { killCount = BossProgress.GetKillCount(thingType); }
        catch { return orig(args); }

        if (killCount > 0)
        {
            TShock.Log.ConsoleInfo($"[BossLimit] {playerName} 召唤了 {bossName}");
            return orig(args);
        }

        var config = AutoRegister.Config;

        if (!config.BossLimitEnabled)
        {
            TShock.Log.ConsoleInfo($"[BossLimit] {playerName} 召唤了 {bossName}");
            return orig(args);
        }

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

    private static void BossLimitCommand(CommandArgs args)
    {
        var config = AutoRegister.Config;

        if (args.Parameters.Count == 0)
        {
            string status = config.BossLimitEnabled ? "[c/00ff00:开启]" : "[c/ff0000:关闭]";
            args.Player.SendInfoMessage($"Boss 限制: {status}，最低人数: {config.BossLimitMinPlayers}");
            args.Player.SendInfoMessage("用法: /bosslimit on|off  或  /bosslimit playernum <人数>");
            return;
        }

        switch (args.Parameters[0].ToLower())
        {
            case "on":
                config.BossLimitEnabled = true;
                AutoRegister.SaveConfig();
                args.Player.SendSuccessMessage($"Boss 限制已开启（未击败 Boss 至少需要 {config.BossLimitMinPlayers} 人在线才能召唤）");
                TShock.Log.ConsoleInfo($"[BossLimit] {args.Player.Name} 开启了 Boss 限制");
                break;

            case "off":
                config.BossLimitEnabled = false;
                AutoRegister.SaveConfig();
                args.Player.SendSuccessMessage("Boss 限制已关闭");
                TShock.Log.ConsoleInfo($"[BossLimit] {args.Player.Name} 关闭了 Boss 限制");
                break;

            case "playernum":
            case "人数":
                if (args.Parameters.Count < 2 || !int.TryParse(args.Parameters[1], out var num) || num <= 0)
                {
                    args.Player.SendErrorMessage("用法: /bosslimit playernum <人数>，人数必须为正整数");
                    return;
                }
                config.BossLimitMinPlayers = num;
                AutoRegister.SaveConfig();
                args.Player.SendSuccessMessage($"Boss 限制最低人数已更新为: {num} 人");
                TShock.Log.ConsoleInfo($"[BossLimit] {args.Player.Name} 设置最低人数为 {num}");
                break;

            default:
                args.Player.SendErrorMessage("无效参数，用法: /bosslimit on|off  或  /bosslimit playernum <人数>");
                break;
        }
    }

    private static int GetActivePlayerCount()
    {
        int count = 0;
        foreach (var player in TShock.Players)
            if (player != null && player.Active) count++;
        return count;
    }
}
