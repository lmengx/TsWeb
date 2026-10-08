using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;
using Rests;

namespace TShockData;

public static class BossLimitQuit
{
    private class BossDamageRecord
    {
        public int NPCIndex;
        public int NPCType;
        public string BossName;
        public HashSet<string> Damagers;
        public HashSet<string> SummonOnlinePlayers;
        public int SpawnPlayerCount;
    }

    private static bool _initialized;
    private static TerrariaPlugin? _plugin;
    private static readonly List<BossDamageRecord> ActiveBosses = new();
    private static int _cleanupCounter;

    public static void Initialize(TerrariaPlugin plugin)
    {
        if (_initialized) return;
        _plugin = plugin;

        GetDataHandlers.NPCStrike.Register(OnNpcStrike);
        ServerApi.Hooks.NpcSpawn.Register(plugin, OnNpcSpawn);
        ServerApi.Hooks.ServerJoin.Register(plugin, OnServerJoin);
        ServerApi.Hooks.GameUpdate.Register(plugin, OnGameUpdate);

        _initialized = true;
        TShock.Log.ConsoleInfo("[BossLimitQuit] BOSS 退出惩罚 + 晚入补偿已初始化");
    }

    public static void Dispose()
    {
        if (!_initialized) return;

        GetDataHandlers.NPCStrike.UnRegister(OnNpcStrike);
        if (_plugin != null)
        {
            ServerApi.Hooks.NpcSpawn.Deregister(_plugin, OnNpcSpawn);
            ServerApi.Hooks.ServerJoin.Deregister(_plugin, OnServerJoin);
            ServerApi.Hooks.GameUpdate.Deregister(_plugin, OnGameUpdate);
        }

        lock (ActiveBosses) ActiveBosses.Clear();
        _plugin = null;
        _initialized = false;
        TShock.Log.ConsoleInfo("[BossLimitQuit] BOSS 退出惩罚 + 晚入补偿已卸载");
    }

    private static void OnServerJoin(JoinEventArgs args)
    {
        // 「重进击杀」已由 BossGhostMode（BOSS 死亡幽灵模式）取代：
        // 旧机制惩罚"BOSS 存活时退出"，但玩家死亡后退出重进即可刷新复活时间，逃避效率更高；
        // 新机制把约束前移到"死亡"这一刻，并以绝对到期时刻计时，退出重进不重置。
        // 此处不再施加织网+石化惩罚。
    }

    private static void OnGameUpdate(EventArgs args)
    {
        _cleanupCounter++;
        if (_cleanupCounter < 60) return;
        _cleanupCounter = 0;

        lock (ActiveBosses)
            ActiveBosses.RemoveAll(r =>
            {
                if (r.NPCIndex < 0 || r.NPCIndex >= Main.maxNPCs) return true;
                var npc = Main.npc[r.NPCIndex];
                return npc == null || !npc.active || npc.netID != r.NPCType;
            });
    }

    private static void OnNpcSpawn(NpcSpawnEventArgs args)
    {
        var npc = Main.npc[args.NpcId];
        if (npc == null || !npc.active || !npc.boss) return;
        if (!BossConfigManager.Config.QuitLimitEnabled && !BossConfigManager.Config.LateCompEnabled) return;

        lock (ActiveBosses)
        {
            if (ActiveBosses.Any(r => r.NPCIndex == args.NpcId && r.NPCType == npc.netID))
                return;

            var onlinePlayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in TShock.Players)
                if (p != null && p.Active && p.RealPlayer)
                    onlinePlayers.Add(p.Name);

            ActiveBosses.Add(new BossDamageRecord
            {
                NPCIndex = args.NpcId,
                NPCType = npc.netID,
                BossName = npc.FullName,
                Damagers = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                SummonOnlinePlayers = onlinePlayers,
                SpawnPlayerCount = onlinePlayers.Count
            });
        }
    }

    private static void OnNpcStrike(object? sender, GetDataHandlers.NPCStrikeEventArgs args)
    {
        if (args.Handled) return;

        var player = args.Player;
        if (player == null || !player.RealPlayer) return;

        // 1.4.5.8 API：NPCStrikeEventArgs.Index 已改名为 ID（short）
        int npcIndex = args.ID;
        if (npcIndex < 0 || npcIndex >= Main.maxNPCs) return;

        var npc = Main.npc[npcIndex];
        if (npc == null || !npc.active || !npc.boss) return;

        bool quitEnabled = BossConfigManager.Config.QuitLimitEnabled;
        bool lateCompEnabled = BossConfigManager.Config.LateCompEnabled;
        if (!quitEnabled && !lateCompEnabled) return;

        lock (ActiveBosses)
        {
            var record = ActiveBosses.Find(r => r.NPCIndex == npcIndex && r.NPCType == npc.netID);
            if (record == null)
            {
                var onlinePlayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in TShock.Players)
                    if (p != null && p.Active && p.RealPlayer)
                        onlinePlayers.Add(p.Name);

                record = new BossDamageRecord
                {
                    NPCIndex = npcIndex,
                    NPCType = npc.netID,
                    BossName = npc.FullName,
                    Damagers = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    SummonOnlinePlayers = onlinePlayers,
                    SpawnPlayerCount = onlinePlayers.Count
                };
                ActiveBosses.Add(record);
            }

            record.Damagers.Add(player.Name);

            if (lateCompEnabled && record.SummonOnlinePlayers.Count > 0 &&
                !record.SummonOnlinePlayers.Contains(player.Name))
            {
                record.SummonOnlinePlayers.Add(player.Name);

                int extraHP = CalculateExtraHP(npc.lifeMax, record.SpawnPlayerCount);
                if (extraHP > 0)
                {
                    record.SpawnPlayerCount++;
                    npc.lifeMax += extraHP;
                    npc.life = Math.Min(npc.life + extraHP, npc.lifeMax);
                    TSPlayer.All.SendData(PacketTypes.NpcUpdate, "", npcIndex);

                    TSPlayer.All.SendMessage(
                        $"[c/FFA500:新玩家 {player.Name} 加入战斗，{npc.FullName} 血量增加 {extraHP}]",
                        Microsoft.Xna.Framework.Color.Yellow);
                    TShock.Log.ConsoleInfo($"[BossLimitQuit] 晚入补偿: {player.Name} 攻击 {npc.FullName}，血量 +{extraHP}");
                }
            }
        }
    }

    private static int CalculateExtraHP(int currentLifeMax, int originalPlayerCount)
    {
        if (originalPlayerCount <= 1)
            return (int)(currentLifeMax * 0.35);

        double baseLife = currentLifeMax / (1.0 + 0.35 * (originalPlayerCount - 1));
        int extraHP = (int)(baseLife * 0.35);
        return Math.Max(extraHP, 1);
    }

    // ═══════════════════════════════════════════════
    // 供 BossGhostMode（BOSS 死亡幽灵模式）查询的判定接口
    // ═══════════════════════════════════════════════

    /// <summary>
    /// 玩家附近是否有存活的 BOSS；有则返回该 BOSS 名，否则返回 null。
    /// 判定范围沿用 TShock 自己在 HandlePlayerKillMeV2 里的口径（曼哈顿距离 4000），
    /// 保持"什么算 BOSS 战"与服务器原版一致。
    /// </summary>
    public static string? GetLiveBossNameNear(TSPlayer player)
    {
        if (player?.TPlayer == null) return null;

        foreach (var npc in Main.npc)
        {
            if (npc == null || !npc.active) continue;
            if (!npc.boss) continue;

            float distance = Math.Abs(player.TPlayer.Center.X - npc.Center.X)
                           + Math.Abs(player.TPlayer.Center.Y - npc.Center.Y);
            if (distance < 4000f)
            {
                return npc.FullName;
            }
        }

        return null;
    }

    /// <summary>该玩家是否打过当前追踪中的任意 BOSS（即是否在 Damagers 名单里）</summary>
    public static bool HasDamagedAnyBoss(string playerName)
    {
        if (string.IsNullOrEmpty(playerName)) return false;

        lock (ActiveBosses)
        {
            return ActiveBosses.Any(r => r.Damagers.Contains(playerName));
        }
    }

    // ═══════════════════════════════════════════════
    // REST API
    // ═══════════════════════════════════════════════

    public static object GetStatusJson(RestRequestArgs args)
    {
        var config = BossConfigManager.Config;
        int trackedBosses, trackedPlayers;
        var bossList = new List<object>();

        lock (ActiveBosses)
        {
            trackedBosses = ActiveBosses.Count;
            trackedPlayers = ActiveBosses.Sum(r => r.Damagers.Count);
            foreach (var r in ActiveBosses)
            {
                bossList.Add(new
                {
                    bossName = r.BossName,
                    npcType = r.NPCType,
                    damagerCount = r.Damagers.Count,
                    damagers = r.Damagers.ToList(),
                    onlineOnSpawn = r.SpawnPlayerCount
                });
            }
        }

        return new
        {
            status = "200",
            quitLimitEnabled = config.QuitLimitEnabled,
            lateCompEnabled = config.LateCompEnabled,
            ghostRespawnSeconds = config.GhostRespawnSeconds,
            trackedBosses,
            trackedPlayers,
            activeBosses = bossList,
            ghosts = BossGhostMode.GetGhostList()
        };
    }

    // ═══════════════════════════════════════════════
    // 命令处理
    // ═══════════════════════════════════════════════

    public static void HandleCommand(CommandArgs args)
    {
        var config = BossConfigManager.Config;

        if (args.Parameters.Count < 2)
        {
            ShowStatus(args);
            ShowHelp(args);
            return;
        }

        switch (args.Parameters[1].ToLower())
        {
            case "on":
                BossConfigManager.Config.QuitLimitEnabled = true;
                BossConfigManager.SaveConfig();
                args.Player.SendSuccessMessage($"BOSS 死亡幽灵模式已开启（复活倒计时 {BossConfigManager.Config.GhostRespawnSeconds} 秒）");
                TShock.Log.ConsoleInfo($"[BossLimitQuit] {args.Player.Name} 开启了 BOSS 死亡幽灵模式");
                break;

            case "off":
                BossConfigManager.Config.QuitLimitEnabled = false;
                BossGhostMode.ClearAll();
                BossConfigManager.SaveConfig();
                args.Player.SendSuccessMessage("BOSS 死亡幽灵模式已关闭（所有幽灵态玩家已释放）");
                lock (ActiveBosses) ActiveBosses.Clear();
                TShock.Log.ConsoleInfo($"[BossLimitQuit] {args.Player.Name} 关闭了 BOSS 死亡幽灵模式");
                break;

            case "seconds":
                HandleSecondsCommand(args, config);
                break;

            case "release":
                HandleReleaseCommand(args);
                break;

            case "clear":
                lock (ActiveBosses) ActiveBosses.Clear();
                args.Player.SendSuccessMessage("BOSS 追踪记录已清空");
                TShock.Log.ConsoleInfo($"[BossLimitQuit] {args.Player.Name} 清空了 BOSS 追踪记录");
                break;

            case "latecomp":
                HandleLateCompCommand(args, config);
                break;

            default:
                args.Player.SendErrorMessage("无效参数！可用: on / off / seconds / release / clear / latecomp");
                ShowHelp(args);
                break;
        }
    }

    private static void HandleSecondsCommand(CommandArgs args, BossConfig config)
    {
        if (args.Parameters.Count < 3)
        {
            args.Player.SendInfoMessage($"幽灵复活倒计时: [c/00ff00:{config.GhostRespawnSeconds} 秒]");
            args.Player.SendInfoMessage("用法: quit seconds <秒数>（1~3600）");
            return;
        }

        if (!int.TryParse(args.Parameters[2], out var seconds))
        {
            args.Player.SendErrorMessage("用法: quit seconds <秒数>（1~3600）");
            return;
        }

        int clamped = Math.Clamp(seconds, 1, 3600);
        BossConfigManager.Config.GhostRespawnSeconds = clamped;
        BossConfigManager.SaveConfig();
        args.Player.SendSuccessMessage($"幽灵复活倒计时已设为 {clamped} 秒");
        TShock.Log.ConsoleInfo($"[BossLimitQuit] {args.Player.Name} 设置幽灵复活倒计时: {clamped}s");
    }

    private static void HandleReleaseCommand(CommandArgs args)
    {
        if (args.Parameters.Count < 3)
        {
            args.Player.SendErrorMessage("用法: quit release <玩家名>");
            return;
        }

        string name = args.Parameters[2];
        if (BossGhostMode.ReleasePlayer(name))
        {
            args.Player.SendSuccessMessage($"已解除 {name} 的幽灵状态");
            TShock.Log.ConsoleInfo($"[BossLimitQuit] {args.Player.Name} 手动解除了 {name} 的幽灵状态");
        }
        else
        {
            args.Player.SendErrorMessage($"{name} 当前不在幽灵状态");
        }
    }

    private static void HandleLateCompCommand(CommandArgs args, BossConfig config)
    {
        if (args.Parameters.Count < 3)
        {
            args.Player.SendInfoMessage($"晚入补偿: {(config.LateCompEnabled ? "[c/00ff00:开启]" : "[c/ff0000:关闭]")}");
            args.Player.SendInfoMessage("用法: quit latecomp on|off");
            return;
        }

        switch (args.Parameters[2].ToLower())
        {
            case "on":
                BossConfigManager.Config.LateCompEnabled = true;
                BossConfigManager.SaveConfig();
                args.Player.SendSuccessMessage("晚入 BOSS 血量补偿已开启");
                TShock.Log.ConsoleInfo($"[BossLimitQuit] {args.Player.Name} 开启了晚入补偿");
                break;

            case "off":
                BossConfigManager.Config.LateCompEnabled = false;
                BossConfigManager.SaveConfig();
                args.Player.SendSuccessMessage("晚入 BOSS 血量补偿已关闭");
                TShock.Log.ConsoleInfo($"[BossLimitQuit] {args.Player.Name} 关闭了晚入补偿");
                break;

            default:
                args.Player.SendErrorMessage("用法: quit latecomp on|off");
                break;
        }
    }

    public static void ShowStatus(CommandArgs args)
    {
        var config = BossConfigManager.Config;

        string quitStatus = config.QuitLimitEnabled
            ? "[c/00ff00:开启]" : "[c/ff0000:关闭]";
        string lateStatus = config.LateCompEnabled
            ? "[c/00ff00:开启]" : "[c/ff0000:关闭]";

        int trackedBosses, trackedPlayers;
        lock (ActiveBosses)
        {
            trackedBosses = ActiveBosses.Count;
            trackedPlayers = ActiveBosses.Sum(r => r.Damagers.Count);
        }

        args.Player.SendInfoMessage($"[BOSS 死亡幽灵模式] {quitStatus}");
        args.Player.SendInfoMessage($"[幽灵复活倒计时] {config.GhostRespawnSeconds} 秒");
        args.Player.SendInfoMessage($"[晚入血量补偿] {lateStatus}");
        if (config.QuitLimitEnabled || config.LateCompEnabled)
            args.Player.SendInfoMessage($"  当前追踪: {trackedBosses} 个 BOSS, {trackedPlayers} 名伤害者");

        var ghosts = BossGhostMode.GetGhostList();
        if (ghosts.Count > 0)
            args.Player.SendInfoMessage($"  幽灵态玩家: {ghosts.Count} 名（/bosslimit quit release <玩家名> 可解除）");
    }

    public static void ShowHelp(CommandArgs args)
    {
        args.Player.SendInfoMessage("  quit on                    — 开启死亡幽灵模式");
        args.Player.SendInfoMessage("  quit off                   — 关闭死亡幽灵模式（释放所有幽灵态玩家）");
        args.Player.SendInfoMessage("  quit seconds <秒>          — 设置幽灵复活倒计时（1~3600）");
        args.Player.SendInfoMessage("  quit release <玩家名>      — 手动解除某玩家的幽灵状态");
        args.Player.SendInfoMessage("  quit clear                 — 清空 BOSS 追踪记录");
        args.Player.SendInfoMessage("  quit latecomp on           — 开启晚入血量补偿");
        args.Player.SendInfoMessage("  quit latecomp off          — 关闭晚入血量补偿");
    }
}
