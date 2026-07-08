using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;

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
        if (args.Handled) return;
        if (!AutoRegister.Config.QuitLimitEnabled) return;

        var player = TShock.Players[args.Who];
        if (player == null || string.IsNullOrEmpty(player.Name)) return;

        int idx = player.Index;
        string name = player.Name;

        // 分线程轮询等待玩家加载完成（每 10ms 检测一次 FinishedHandshake）
        Task.Run(async () =>
        {
            for (int i = 0; i < 300; i++)
            {
                await Task.Delay(10);

                var target = TShock.Players[idx];
                if (target == null || !target.ConnectionAlive) return;
                if (!target.FinishedHandshake || !target.Active) continue;

                bool shouldPunish = false;
                string? bossName = null;

                lock (ActiveBosses)
                {
                    var hitBoss = ActiveBosses.Find(r => r.Damagers.Contains(name));
                    if (hitBoss != null)
                    {
                        shouldPunish = true;
                        bossName = hitBoss.BossName;
                    }
                }

                if (!shouldPunish) return;

                TShock.Log.ConsoleInfo($"[BossLimitQuit] {name} 曾在 BOSS {bossName} 存活时退出，已执行死亡惩罚");
                target.KillPlayer();
                target.SendWarningMessage($"检测到你曾在 BOSS [{bossName}] 战斗中退出，已对你执行死亡惩罚！");
                return;
            }
        });
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
        if (!AutoRegister.Config.QuitLimitEnabled && !AutoRegister.Config.LateCompEnabled) return;

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

        short npcIndex = args.ID;
        if (npcIndex < 0 || npcIndex >= Main.maxNPCs) return;

        var npc = Main.npc[npcIndex];
        if (npc == null || !npc.active || !npc.boss) return;

        bool quitEnabled = AutoRegister.Config.QuitLimitEnabled;
        bool lateCompEnabled = AutoRegister.Config.LateCompEnabled;
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
    // 命令处理
    // ═══════════════════════════════════════════════

    public static void HandleCommand(CommandArgs args)
    {
        var config = AutoRegister.Config;

        if (args.Parameters.Count < 2)
        {
            ShowStatus(args);
            ShowHelp(args);
            return;
        }

        switch (args.Parameters[1].ToLower())
        {
            case "on":
                config.QuitLimitEnabled = true;
                AutoRegister.SaveConfig();
                args.Player.SendSuccessMessage("BOSS 退出惩罚已开启");
                TShock.Log.ConsoleInfo($"[BossLimitQuit] {args.Player.Name} 开启了 BOSS 退出惩罚");
                break;

            case "off":
                config.QuitLimitEnabled = false;
                AutoRegister.SaveConfig();
                args.Player.SendSuccessMessage("BOSS 退出惩罚已关闭");
                lock (ActiveBosses) ActiveBosses.Clear();
                TShock.Log.ConsoleInfo($"[BossLimitQuit] {args.Player.Name} 关闭了 BOSS 退出惩罚");
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
                args.Player.SendErrorMessage("无效参数！可用: on / off / clear / latecomp");
                ShowHelp(args);
                break;
        }
    }

    private static void HandleLateCompCommand(CommandArgs args, RegisterConfig config)
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
                config.LateCompEnabled = true;
                AutoRegister.SaveConfig();
                args.Player.SendSuccessMessage("晚入 BOSS 血量补偿已开启");
                TShock.Log.ConsoleInfo($"[BossLimitQuit] {args.Player.Name} 开启了晚入补偿");
                break;

            case "off":
                config.LateCompEnabled = false;
                AutoRegister.SaveConfig();
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
        var config = AutoRegister.Config;

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

        args.Player.SendInfoMessage($"[BOSS 退出惩罚] {quitStatus}");
        args.Player.SendInfoMessage($"[晚入血量补偿] {lateStatus}");
        if (config.QuitLimitEnabled || config.LateCompEnabled)
            args.Player.SendInfoMessage($"  当前追踪: {trackedBosses} 个 BOSS, {trackedPlayers} 名伤害者");
    }

    public static void ShowHelp(CommandArgs args)
    {
        args.Player.SendInfoMessage("  quit on                    — 开启退出惩罚（击杀）");
        args.Player.SendInfoMessage("  quit off                   — 关闭退出惩罚");
        args.Player.SendInfoMessage("  quit clear                 — 清空追踪记录");
        args.Player.SendInfoMessage("  quit latecomp on           — 开启晚入血量补偿");
        args.Player.SendInfoMessage("  quit latecomp off          — 关闭晚入血量补偿");
    }
}
