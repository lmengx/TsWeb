using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using TerrariaApi.Server;
using TShockAPI;

namespace DiscoveryLog
{
    /// <summary>
    /// DiscoveryLogCore —— 探索物品发现日志核心。
    ///
    /// 对「自然获取事件」挂钩并公屏记日志：
    ///   1. 挖掘：TShock GetDataHandlers.TileEdit 事件，检测 KillTile 动作且目标方块为
    ///      生命水晶(图格 12) / 龙蛋(图格 752)
    ///   2. 救助：TShock GetDataHandlers.NpcTalk 事件，检测对话对象为受困 NPC
    ///      捣蛋猫(NPC 695) / 火绒狐(NPC 696)
    ///
    /// 判定要点（实证）：
    ///   - TileEdit 事件触发于方块被移除前，Main.tile[X,Y].type 仍是原类型，可直接判定
    ///   - NpcTalk 事件触发时 NPC 仍是受困态（之后才 TransformBoundNPC），type 判定可靠
    ///   - 房屋系统以 NetGetData(int.MaxValue) 先拦截；被拦截（Handled=true）的挖掘不会
    ///     触发 TShock TileEdit 事件 → 房屋内无权破坏不产生日志（符合「合规事件」语义）
    ///
    /// 编译目标：TShock 6.1.0 NuGet（Terraria 1.4.4.x API）。Palworld 联动常量（图格 752、
    /// 受困 NPC 695/696、物品 6142/5663/5664）在 1.4.4.x 编译期 API 中不存在，
    /// 一律使用裸数字常量（运行时为 1.4.5.7 服务端，数值有效）。
    /// </summary>
    public static class DiscoveryLogCore
    {
        // ══════════════════════════════════════════════════════════
        //  监控物品定义（写死）
        // ══════════════════════════════════════════════════════════

        /// <summary>图格挖掘类：图格类型 → (物品ID, 显示名)</summary>
        private static readonly Dictionary<ushort, (int ItemId, string Name)> MinedTiles = new()
        {
            { 12,  (29,  "生命水晶") },   // TileID.Heart
            { 752, (6142, "龙蛋")   },   // TileID.PalworldChilletEgg
        };

        /// <summary>救助类：受困 NPC 类型 → (物品ID, 显示名)</summary>
        private static readonly Dictionary<int, (int ItemId, string Name)> RescueNpcs = new()
        {
            { 695, (5663, "捣蛋猫") },   // NPCID.PalworldCattivaDistressed
            { 696, (5664, "火绒狐") },   // NPCID.PalworldFoxsparksDistressed
        };

        // ══════════════════════════════════════════════════════════
        //  状态
        // ══════════════════════════════════════════════════════════

        private static bool _initialized;

        /// <summary>去重字典：玩家|物品ID|x|y → 最近一次记录时间</summary>
        private static readonly Dictionary<string, DateTime> _lastLog = new();

        /// <summary>去重窗口（秒）</summary>
        private const double DedupSeconds = 3.0;

        // ══════════════════════════════════════════════════════════
        //  生命周期
        // ══════════════════════════════════════════════════════════

        public static void Initialize()
        {
            if (_initialized) return;

            GetDataHandlers.TileEdit.Register(OnTileEdit);
            GetDataHandlers.NpcTalk.Register(OnNpcTalk);

            Commands.ChatCommands.Add(new Command("tshock.admin", HandleCommand, "dlog", "discoverylog")
            {
                HelpText = "探索物品发现日志测试指令：spawn <cattiva|foxparks> 生成受困NPC；findegg 定位世界中的龙蛋图格"
            });

            _initialized = true;
            TShock.Log.ConsoleInfo("[DiscoveryLog] 探索物品发现日志已启用 (生命水晶/龙蛋挖掘 + 捣蛋猫/火绒狐救助)");
        }

        public static void Dispose()
        {
            if (!_initialized) return;

            GetDataHandlers.TileEdit.UnRegister(OnTileEdit);
            GetDataHandlers.NpcTalk.UnRegister(OnNpcTalk);
            Commands.ChatCommands.RemoveAll(cmd => cmd.Names.Contains("dlog") || cmd.Names.Contains("discoverylog"));

            lock (_lastLog)
            {
                _lastLog.Clear();
            }
            _initialized = false;
            TShock.Log.ConsoleInfo("[DiscoveryLog] 探索物品发现日志已卸载");
        }

        // ══════════════════════════════════════════════════════════
        //  事件挂钩
        // ══════════════════════════════════════════════════════════

        /// <summary>挖掘检测：生命水晶(图格12) / 龙蛋(图格752)</summary>
        private static void OnTileEdit(object? sender, GetDataHandlers.TileEditEventArgs e)
        {
            if (e.Player == null) return;

            // 挖掘动作：KillTile(0) / KillTileNoItem(4) / TryKillTile(20)
            // 注：EditAction 为 GetDataHandlers 的嵌套枚举（TShock 6.1.0）
            if (e.Action != GetDataHandlers.EditAction.KillTile &&
                e.Action != GetDataHandlers.EditAction.KillTileNoItem &&
                e.Action != GetDataHandlers.EditAction.TryKillTile)
                return;

            // 边界检查
            if (e.X < 0 || e.Y < 0 || e.X >= Main.maxTilesX || e.Y >= Main.maxTilesY) return;

            // 事件早于方块移除，Main.tile[X,Y] 仍是原方块
            var tile = Main.tile[e.X, e.Y];
            if (tile == null || !tile.active()) return;

            if (!MinedTiles.TryGetValue(tile.type, out var info)) return;

            // 防重复（多格图格 / 连挖触发多个包）
            if (!TryDedup(e.Player, info.ItemId, e.X, e.Y)) return;

            LogDiscovery(e.Player, info.Name, info.ItemId, e.X, e.Y, "挖掘");
        }

        /// <summary>救助检测：捣蛋猫(NPC695) / 火绒狐(NPC696)</summary>
        private static void OnNpcTalk(object? sender, GetDataHandlers.NpcTalkEventArgs e)
        {
            if (e.Player == null) return;

            // NPC 数组下标越界检查（-1 表示未对话 NPC）
            if (e.NPCTalkTarget < 0 || e.NPCTalkTarget >= Main.maxNPCs) return;

            var npc = Main.npc[e.NPCTalkTarget];
            if (npc == null || !npc.active) return;

            if (!RescueNpcs.TryGetValue(npc.type, out var info)) return;

            int tileX = (int)(npc.position.X / 16f);
            int tileY = (int)(npc.position.Y / 16f);

            if (!TryDedup(e.Player, info.ItemId, tileX, tileY)) return;

            LogDiscovery(e.Player, info.Name, info.ItemId, tileX, tileY, "救助");
        }

        // ══════════════════════════════════════════════════════════
        //  日志输出
        // ══════════════════════════════════════════════════════════

        private static void LogDiscovery(TSPlayer player, string itemName, int itemId, int tileX, int tileY, string action)
        {
            var msg = $"[发现] {player.Name} {action}了 {itemName} (ID:{itemId}) @ ({tileX}, {tileY})";
            TShock.Utils.Broadcast(msg, Color.LimeGreen);
            TShock.Log.ConsoleInfo($"[DiscoveryLog] {msg}");
        }

        // ══════════════════════════════════════════════════════════
        //  去重
        // ══════════════════════════════════════════════════════════

        /// <summary>同玩家同物品同坐标在去重窗口内只记一次；首次返回 true</summary>
        private static bool TryDedup(TSPlayer player, int itemId, int x, int y)
        {
            var key = $"{player.Name}|{itemId}|{x}|{y}";
            lock (_lastLog)
            {
                if (_lastLog.TryGetValue(key, out var t) && (DateTime.Now - t).TotalSeconds < DedupSeconds)
                    return false;

                _lastLog[key] = DateTime.Now;

                // 简单上限，防无限增长
                if (_lastLog.Count > 500)
                    _lastLog.Clear();

                return true;
            }
        }

        // ══════════════════════════════════════════════════════════
        //  测试指令
        // ══════════════════════════════════════════════════════════

        private static void HandleCommand(CommandArgs args)
        {
            if (args.Parameters.Count == 0)
            {
                args.Player.SendInfoMessage("用法: /dlog spawn <cattiva|foxparks> | /dlog findegg");
                return;
            }

            switch (args.Parameters[0].ToLowerInvariant())
            {
                case "spawn":
                    SpawnDistressed(args);
                    break;
                case "findegg":
                    FindEgg(args);
                    break;
                default:
                    args.Player.SendInfoMessage("用法: /dlog spawn <cattiva|foxparks> | /dlog findegg");
                    break;
            }
        }

        /// <summary>生成受困捣蛋猫(695) / 受困火绒狐(696)，用于实测救助日志</summary>
        private static void SpawnDistressed(CommandArgs args)
        {
            if (args.Parameters.Count < 2)
            {
                args.Player.SendInfoMessage("用法: /dlog spawn <cattiva|foxparks>");
                return;
            }

            int npcType = args.Parameters[1].ToLowerInvariant() switch
            {
                "cattiva" => 695,   // PalworldCattivaDistressed
                "foxparks" => 696,  // PalworldFoxsparksDistressed
                _ => 0,
            };

            if (npcType == 0)
            {
                args.Player.SendInfoMessage("未知类型，仅支持 cattiva / foxparks");
                return;
            }

            var p = args.Player;
            // 玩家坐标（像素）附近生成，避免重叠
            int x = (int)p.X + 48;
            int y = (int)p.Y - 16;
            int whoAmI = NPC.NewNPC(new EntitySource_DebugCommand(), x, y, npcType);

            if (whoAmI >= 0)
                args.Player.SendSuccessMessage($"已生成受困NPC: {RescueNpcs[npcType].Name} (NPC #{whoAmI})，对话即可触发救助日志");
            else
                args.Player.SendErrorMessage("生成受困NPC失败（NPC 槽位已满？）");
        }

        /// <summary>全图扫描定位龙蛋图格(752)：一次扫描同时统计总数并收集前 N 个坐标</summary>
        private static void FindEgg(CommandArgs args)
        {
            const int MaxList = 20;
            var found = new List<(int X, int Y)>();
            int total = 0;

            for (int x = 0; x < Main.maxTilesX; x++)
            {
                for (int y = 0; y < Main.maxTilesY; y++)
                {
                    var tile = Main.tile[x, y];
                    if (tile != null && tile.active() && tile.type == 752)
                    {
                        total++;
                        if (found.Count < MaxList)
                            found.Add((x, y));
                    }
                }
            }

            if (total == 0)
            {
                args.Player.SendInfoMessage("世界中未发现龙蛋图格(752)");
                return;
            }

            args.Player.SendInfoMessage($"世界中龙蛋图格共 {total} 个，以下为前 {found.Count} 个:");

            for (int i = 0; i < found.Count; i++)
            {
                var (fx, fy) = found[i];
                args.Player.SendInfoMessage($"  [{i + 1}] ({fx}, {fy})");
            }
        }
    }
}
