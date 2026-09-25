using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;
using Rests;

namespace TShockData;

/// <summary>
/// 进度锁 · 按时间锁（BossTimeLock，叠加开关）
///
/// 功能：
/// 1. 无论开关，每次启动（GamePostInitialize / 热重载）都记录地图 ID（Main.worldID）；
///    时间锁开启后对比地图 ID，若地图已更换则视为重新开服，开服时间重置为当前时间。
/// 2. 解锁计划（TimeSchedule）：档名 → 开服后第 N 天 HH:mm 纯按时间解锁（不看击杀）。
/// 3. 按时间锁开启（TimeLockEnabled）时，配置了时间的档由 <see cref="BossProgress.GetWorldStatus"/> 按时间判定；
///    未配置时间的档保持原击杀判定（叠加，不互斥）。
/// 4. BlockLockedBossSpawn=true 时，未解锁档的 BOSS 召唤（BossLimitSummon）与自然生成（NpcSpawn）均被拦截。
/// </summary>
public static class BossTimeLock
{
    /// <summary>进度档名 → NPCID（用于召唤/自然生成拦截时反查档名）</summary>
    private static readonly Dictionary<string, int> BossNameToNpcId = new(StringComparer.OrdinalIgnoreCase)
    {
        { "史莱姆王", NPCID.KingSlime },
        { "克苏鲁之眼", NPCID.EyeofCthulhu },
        { "世界吞噬者", NPCID.EaterofWorldsHead },
        { "克苏鲁之脑", NPCID.BrainofCthulhu },
        { "蜂后", NPCID.QueenBee },
        { "巨鹿", NPCID.Deerclops },
        { "骷髅王", NPCID.SkeletronHead },
        { "血肉墙", NPCID.WallofFlesh },
        { "史莱姆皇后", NPCID.QueenSlimeBoss },
        { "毁灭者", NPCID.TheDestroyer },
        { "机械骷髅王", NPCID.SkeletronPrime },
        { "双子魔眼", NPCID.Retinazer },
        { "世纪之花", NPCID.Plantera },
        { "石巨人", NPCID.Golem },
        { "猪龙鱼公爵", NPCID.DukeFishron },
        { "光之女皇", NPCID.HallowBoss },
        { "拜月教教徒", NPCID.CultistBoss },
        { "月亮领主", NPCID.MoonLordCore }
    };

    /// <summary>缓存：档名 → 解锁时刻（null = 未配置 / 非时间锁）</summary>
    private static readonly Dictionary<string, DateTime?> UnlockCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheLock = new();

    private static bool _initialized;
    private static TerrariaPlugin? _plugin;

    public static void Initialize(TerrariaPlugin plugin)
    {
        if (_initialized) return;
        _plugin = plugin;

        ServerApi.Hooks.GamePostInitialize.Register(plugin, OnGamePostInitialize);
        ServerApi.Hooks.NpcSpawn.Register(plugin, OnNpcSpawn);

        // 热重载（HotReload /hr 或 TsWebHost 下发）场景：世界已加载，立即记录地图 ID；
        // 冷启动仍由 GamePostInitialize（世界加载完成后）触发
        if (!Main.gameMenu) SyncWorldId();

        _initialized = true;
        TShock.Log.ConsoleInfo("[BossTimeLock] 进度锁·按时间锁已初始化（地图ID记录 + 解锁计划 + BOSS生成拦截）");
    }

    public static void Dispose()
    {
        if (!_initialized) return;

        if (_plugin != null)
        {
            ServerApi.Hooks.GamePostInitialize.Deregister(_plugin, OnGamePostInitialize);
            ServerApi.Hooks.NpcSpawn.Deregister(_plugin, OnNpcSpawn);
        }

        lock (CacheLock) UnlockCache.Clear();
        _plugin = null;
        _initialized = false;
        TShock.Log.ConsoleInfo("[BossTimeLock] 进度锁·按时间锁模式已卸载");
    }

    // ═══════════════════════════════════════════
    // 世界 ID 记录与开服时间
    // ═══════════════════════════════════════════

    private static void OnGamePostInitialize(EventArgs args)
    {
        SyncWorldId();
    }

    /// <summary>
    /// 记录当前地图 ID；若记录中已有不同 ID（地图更换）→ 视为重新开服，开服时间重置为当前。
    /// 无论进度锁开关与否都执行记录；开服时间为空（首次）时初始化为当前时间。
    /// </summary>
    public static void SyncWorldId()
    {
        var cfg = BossConfigManager.Config;
        var wid = Main.worldID.ToString();

        bool worldChanged = !string.IsNullOrEmpty(cfg.WorldId) && cfg.WorldId != wid;
        bool firstStart = string.IsNullOrEmpty(cfg.ServerStartTime);

        if (worldChanged || firstStart)
        {
            cfg.ServerStartTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            TShock.Log.ConsoleInfo(
                worldChanged
                    ? $"[BossTimeLock] 检测到地图更换（{cfg.WorldId} → {wid}），视为重新开服，开服时间更新为 {cfg.ServerStartTime}"
                    : $"[BossTimeLock] 首次记录开服时间: {cfg.ServerStartTime}");
        }
        else if (cfg.WorldId != wid)
        {
            // 首次记录地图 ID（ServerStartTime 已存在但 WorldId 为空）
            TShock.Log.ConsoleInfo($"[BossTimeLock] 记录地图 ID: {wid}");
        }

        cfg.WorldId = wid;
        BossConfigManager.SaveConfig();
        RebuildCache();
    }

    /// <summary>解析配置中的开服时间；未设置返回 null</summary>
    public static DateTime? GetServerStartTime()
    {
        var s = BossConfigManager.Config.ServerStartTime;
        if (string.IsNullOrEmpty(s)) return null;
        return DateTime.TryParse(s, out var t) ? t : null;
    }

    // ═══════════════════════════════════════════
    // 解锁时间计算与缓存
    // ═══════════════════════════════════════════

    /// <summary>
    /// 计算某个解锁计划项的解锁时刻（日历日语义）：
    /// 开服当天 = 第 1 天，第 N 天 = ServerStartTime.Date.AddDays(N-1) + HH:mm。
    /// 若计算出的时刻不晚于开服时刻（如开服当天指定时刻已过），顺延到下一个相同时刻。
    /// </summary>
    public static DateTime? ComputeUnlockTime(TimeScheduleItem item)
    {
        var start = GetServerStartTime();
        if (start == null || item == null || item.Day < 1) return null;
        if (!TimeSpan.TryParse(item.Time, out var span)) return null;

        var candidate = start.Value.Date.AddDays(item.Day - 1).Add(span);
        if (candidate <= start.Value) candidate = candidate.AddDays(1);
        return candidate;
    }

    /// <summary>配置/开服时间变化后重算缓存</summary>
    public static void RebuildCache()
    {
        lock (CacheLock)
        {
            UnlockCache.Clear();
            if (!BossConfigManager.Config.TimeLockEnabled)
                return;

            foreach (var item in BossConfigManager.Config.TimeSchedule)
            {
                if (string.IsNullOrWhiteSpace(item.Name)) continue;
                UnlockCache[item.Name] = ComputeUnlockTime(item);
            }
        }
    }

    /// <summary>某档的解锁时刻；未配置 / 按时间锁未开启返回 null</summary>
    public static DateTime? GetUnlockTime(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (!BossConfigManager.Config.TimeLockEnabled)
            return null;
        lock (CacheLock)
            return UnlockCache.TryGetValue(name, out var t) ? t : null;
    }

    /// <summary>按时间锁开启 + 该档配置了时间 + 当前未到解锁时刻 → 该档处于锁定状态</summary>
    public static bool IsTimelocked(string name)
    {
        var unlock = GetUnlockTime(name);
        return unlock.HasValue && DateTime.Now < unlock.Value;
    }

    // ═══════════════════════════════════════════
    // BOSS 生成/召唤拦截
    // ═══════════════════════════════════════════

    /// <summary>NPCID → 档名；未在进度档内的 BOSS 返回 null（不拦截）</summary>
    public static string? GetBossNameByNpcId(int npcNetId)
    {
        foreach (var kv in BossNameToNpcId)
            if (kv.Value == npcNetId) return kv.Key;
        return null;
    }

    /// <summary>按 NPCID 判定该 BOSS 是否处于时间锁锁定状态（召唤/生成拦截用）</summary>
    public static bool IsNpcTimelocked(int npcNetId)
    {
        if (!BossConfigManager.Config.TimeLockEnabled)
            return false;
        if (!BossConfigManager.Config.BlockLockedBossSpawn)
            return false;
        var name = GetBossNameByNpcId(npcNetId);
        return name != null && IsTimelocked(name);
    }

    /// <summary>自然生成拦截：未解锁档的 BOSS 生成时移除并提示</summary>
    private static void OnNpcSpawn(NpcSpawnEventArgs args)
    {
        try
        {
            var npc = Main.npc[args.NpcId];
            if (npc == null || !npc.active || !npc.boss) return;
            if (!IsNpcTimelocked(npc.netID)) return;

            var name = GetBossNameByNpcId(npc.netID);
            var unlock = GetUnlockTime(name!);

            npc.active = false;
            npc.life = 0;
            TSPlayer.All.SendData(PacketTypes.NpcUpdate, "", args.NpcId);
            TShock.Utils.Broadcast(
                $"[时间锁] {npc.FullName} 尚未到解锁时间" +
                (unlock.HasValue ? $"（开服后 {unlock:MM-dd HH:mm} 解锁），已阻止生成" : "，已阻止生成"),
                Microsoft.Xna.Framework.Color.OrangeRed);
            TShock.Log.ConsoleInfo($"[BossTimeLock] 拦截自然生成未解锁 BOSS: {npc.FullName} (netID={npc.netID})");
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[BossTimeLock] NpcSpawn 拦截异常: {ex.Message}");
        }
    }

    /// <summary>召唤拦截判定（由 BossLimitSummon 调用）：返回 true = 阻止该召唤</summary>
    public static bool TryBlockSpawn(TSPlayer player, int npcNetId)
    {
        if (!IsNpcTimelocked(npcNetId)) return false;

        var name = GetBossNameByNpcId(npcNetId);
        var unlock = GetUnlockTime(name!);
        player.SendErrorMessage(
            $"该 BOSS 尚未到解锁时间" +
            (unlock.HasValue ? $"（开服后 {unlock:MM-dd HH:mm} 解锁）" : ""));
        TShock.Log.ConsoleInfo($"[BossTimeLock] 拦截召唤未解锁 BOSS: {name} (netID={npcNetId}) by {player.Name}");
        return true;
    }

    // ═══════════════════════════════════════════
    // 状态查询（REST / 命令共用）
    // ═══════════════════════════════════════════

    /// <summary>解锁计划项 + 计算出的解锁状态（REST / 命令共用）</summary>
    public class ScheduleStatus
    {
        [Newtonsoft.Json.JsonProperty("name")]
        public string Name { get; set; } = "";

        [Newtonsoft.Json.JsonProperty("day")]
        public int Day { get; set; } = 1;

        [Newtonsoft.Json.JsonProperty("time")]
        public string Time { get; set; } = "12:00";

        [Newtonsoft.Json.JsonProperty("unlockAt")]
        public string? UnlockAt { get; set; }

        [Newtonsoft.Json.JsonProperty("unlocked")]
        public bool Unlocked { get; set; }

        [Newtonsoft.Json.JsonProperty("timeManaged")]
        public bool TimeManaged { get; set; }
    }

    /// <summary>返回解锁计划 + 每个档的解锁时刻/是否已解锁（供 GET /data/config/boss 与 /bosslimit time）</summary>
    public static List<ScheduleStatus> GetScheduleStatus()
    {
        var list = new List<ScheduleStatus>();
        foreach (var item in BossConfigManager.Config.TimeSchedule)
        {
            var unlock = ComputeUnlockTime(item);
            list.Add(new ScheduleStatus
            {
                Name = item.Name,
                Day = item.Day,
                Time = item.Time,
                UnlockAt = unlock?.ToString("yyyy-MM-dd HH:mm:ss"),
                Unlocked = unlock.HasValue && DateTime.Now >= unlock.Value,
                TimeManaged = BossConfigManager.Config.TimeLockEnabled
            });
        }
        return list;
    }

    /// <summary>时间锁总体状态（命令用）</summary>
    public static (bool Enabled, string ServerStart, string WorldId, bool BlockSpawn, int ScheduleCount) GetStatus()
    {
        var cfg = BossConfigManager.Config;
        return (cfg.TimeLockEnabled, cfg.ServerStartTime, cfg.WorldId, cfg.BlockLockedBossSpawn, cfg.TimeSchedule.Count);
    }

    // ═══════════════════════════════════════════
    // 聊天命令 /bosslimit time ...
    // ═══════════════════════════════════════════

    /// <summary>处理 /bosslimit time 子命令（args.Parameters = ["time", ...]）</summary>
    public static void HandleCommand(CommandArgs args)
    {
        if (args.Parameters.Count < 2)
        {
            ShowStatus(args);
            ShowHelp(args);
            return;
        }

        switch (args.Parameters[1].ToLower())
        {
            case "on":
            case "off":
            case "开关":
                HandleSwitchCommand(args);
                break;

            case "start":
            case "开服时间":
                HandleStartCommand(args);
                break;

            case "add":
            case "添加":
                HandleAddCommand(args);
                break;

            case "del":
            case "删除":
                HandleDelCommand(args);
                break;

            case "spawnblock":
            case "生成拦截":
                HandleSpawnBlockCommand(args);
                break;

            case "reload":
            case "刷新":
                BossConfigManager.LoadConfig();
                RebuildCache();
                args.Player.SendSuccessMessage("时间锁配置已重新加载");
                break;

            default:
                args.Player.SendErrorMessage("无效参数！可用: on / off / start / add / del / spawnblock / reload");
                ShowHelp(args);
                break;
        }
    }

    public static void ShowStatus(CommandArgs args)
    {
        var (enabled, start, worldId, blockSpawn, count) = GetStatus();

        args.Player.SendInfoMessage($"[进度锁·按时间] 开关: {(enabled ? "[c/00ff00:开启]" : "[c/ff0000:关闭]")}（开启后叠加时间解锁，不看击杀）");
        args.Player.SendInfoMessage($"  开服时间: {(string.IsNullOrEmpty(start) ? "[c/ff0000:未设置]" : start)}");
        args.Player.SendInfoMessage($"  地图 ID: {(string.IsNullOrEmpty(worldId) ? "未记录" : worldId)}");
        args.Player.SendInfoMessage($"  BOSS 生成拦截: {(blockSpawn ? "[c/00ff00:开启]" : "[c/ff0000:关闭]")}");

        args.Player.SendInfoMessage($"  解锁计划（{count} 项）:");
        foreach (var s in GetScheduleStatus())
        {
            string unlocked = s.Unlocked ? "[c/00ff00:已解锁]" : "[c/ff0000:锁定]";
            string managed = s.TimeManaged ? "" : "（未开启不生效）";
            args.Player.SendInfoMessage($"    {s.Name} — 开服后第 {s.Day} 天 {s.Time}（{s.UnlockAt}）{unlocked}{managed}");
        }
    }

    public static void ShowHelp(CommandArgs args)
    {
        args.Player.SendInfoMessage("  time on|off                — 按时间锁开关（开启=叠加时间解锁，不看击杀）");
        args.Player.SendInfoMessage("  time start <yyyy-MM-dd HH:mm>  — 手动指定开服时间");
        args.Player.SendInfoMessage("  time add <档名> <第N天> <HH:mm> — 添加解锁计划项");
        args.Player.SendInfoMessage("  time del <档名>               — 删除解锁计划项");
        args.Player.SendInfoMessage("  time spawnblock on|off         — BOSS 生成/召唤拦截开关");
        args.Player.SendInfoMessage("  time reload                   — 重新加载时间锁配置");
    }

    private static void HandleSwitchCommand(CommandArgs args)
    {
        if (args.Parameters.Count < 2)
        {
            args.Player.SendInfoMessage($"按时间锁: {(BossConfigManager.Config.TimeLockEnabled ? "[c/00ff00:开启]" : "[c/ff0000:关闭]")}");
            args.Player.SendInfoMessage("用法: time on|off");
            return;
        }

        var flag = args.Parameters[1].ToLower();
        if (flag != "on" && flag != "off" && flag != "开关")
        {
            args.Player.SendErrorMessage("用法: time on|off");
            return;
        }

        // on / 开关 → 开启；off → 关闭
        var turnOn = flag != "off";
        var cfg = BossConfigManager.Config;
        if (turnOn && !cfg.TimeLockEnabled)
            SyncWorldId(); // 开启时同步一次地图 ID（若已换图则重置开服时间）
        cfg.TimeLockEnabled = turnOn;
        BossConfigManager.SaveConfig();
        RebuildCache();

        args.Player.SendSuccessMessage(cfg.TimeLockEnabled
            ? "按时间锁已开启（配置了时间的档将按时间解锁，不看击杀）"
            : "按时间锁已关闭（恢复纯击杀进度判定）");
        TShock.Log.ConsoleInfo($"[BossTimeLock] {args.Player.Name} 设置按时间锁: {cfg.TimeLockEnabled}");
    }

    private static void HandleStartCommand(CommandArgs args)
    {
        if (args.Parameters.Count < 3 || !DateTime.TryParse(args.Parameters[2], out var start))
        {
            args.Player.SendErrorMessage("用法: time start <yyyy-MM-dd HH:mm>，例如 time start 2026-09-01 12:00");
            return;
        }

        var cfg = BossConfigManager.Config;
        cfg.ServerStartTime = start.ToString("yyyy-MM-dd HH:mm:ss");
        BossConfigManager.SaveConfig();
        RebuildCache();
        args.Player.SendSuccessMessage($"开服时间已手动指定为: {cfg.ServerStartTime}");
        TShock.Log.ConsoleInfo($"[BossTimeLock] {args.Player.Name} 手动指定开服时间: {cfg.ServerStartTime}");
    }

    private static void HandleAddCommand(CommandArgs args)
    {
        // time add <档名> <第N天> <HH:mm>
        if (args.Parameters.Count < 5)
        {
            args.Player.SendErrorMessage("用法: time add <档名> <第N天> <HH:mm>，例如 time add 世纪之花 4 18:00");
            return;
        }

        var name = args.Parameters[2];
        if (!int.TryParse(args.Parameters[3], out var day) || day < 1)
        {
            args.Player.SendErrorMessage("第N天必须为正整数");
            return;
        }
        var timeStr = args.Parameters[4];
        if (!TimeSpan.TryParse(timeStr, out _))
        {
            args.Player.SendErrorMessage("时间格式应为 HH:mm");
            return;
        }

        var cfg = BossConfigManager.Config;
        // 重名覆盖
        cfg.TimeSchedule.RemoveAll(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        cfg.TimeSchedule.Add(new TimeScheduleItem { Name = name, Day = day, Time = timeStr });
        BossConfigManager.SaveConfig();
        RebuildCache();
        args.Player.SendSuccessMessage($"已添加解锁计划: {name} 开服后第 {day} 天 {timeStr} 解锁");
        TShock.Log.ConsoleInfo($"[BossTimeLock] {args.Player.Name} 添加解锁计划: {name} D{day} {timeStr}");
    }

    private static void HandleDelCommand(CommandArgs args)
    {
        if (args.Parameters.Count < 3)
        {
            args.Player.SendErrorMessage("用法: time del <档名>");
            return;
        }

        var name = args.Parameters[2];
        var cfg = BossConfigManager.Config;
        int removed = cfg.TimeSchedule.RemoveAll(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            args.Player.SendErrorMessage($"未找到解锁计划项: {name}");
            return;
        }

        BossConfigManager.SaveConfig();
        RebuildCache();
        args.Player.SendSuccessMessage($"已删除解锁计划: {name}");
        TShock.Log.ConsoleInfo($"[BossTimeLock] {args.Player.Name} 删除解锁计划: {name}");
    }

    private static void HandleSpawnBlockCommand(CommandArgs args)
    {
        if (args.Parameters.Count < 3)
        {
            args.Player.SendInfoMessage($"BOSS 生成拦截: {(BossConfigManager.Config.BlockLockedBossSpawn ? "[c/00ff00:开启]" : "[c/ff0000:关闭]")}");
            args.Player.SendInfoMessage("用法: time spawnblock on|off");
            return;
        }

        var on = args.Parameters[2].ToLower();
        if (on != "on" && on != "off")
        {
            args.Player.SendErrorMessage("用法: time spawnblock on|off");
            return;
        }

        var cfg = BossConfigManager.Config;
        cfg.BlockLockedBossSpawn = on == "on";
        BossConfigManager.SaveConfig();
        args.Player.SendSuccessMessage($"BOSS 生成/召唤拦截已{(cfg.BlockLockedBossSpawn ? "开启" : "关闭")}");
        TShock.Log.ConsoleInfo($"[BossTimeLock] {args.Player.Name} 设置 BOSS 生成拦截: {cfg.BlockLockedBossSpawn}");
    }
}
