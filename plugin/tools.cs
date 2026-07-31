﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;
using TShockAPI.Hooks;

namespace TShockData
{

    internal class BypassCounter
    {
        public int PermissionBypass;
    }

    public static class BypassHelper
    {
        private static readonly ConcurrentDictionary<TSPlayer, BypassCounter> _bypassCounters = new();

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static void RunWithoutPermissionChecks(Action action, TSPlayer? player = null)
        {
            TSPlayer target = (player != null && player.RealPlayer) ? player : TSPlayer.Server;
            
            var counter = _bypassCounters.GetOrAdd(target, _ => new BypassCounter());
            Interlocked.Increment(ref counter.PermissionBypass);

            try
            {
                action();
            }
            finally
            {
                Interlocked.Decrement(ref counter.PermissionBypass);
            }
        }

        public static void RegisterPermissionHook()
        {
            PlayerHooks.PlayerPermission += OnPlayerPermission;
            //TShock.Log.ConsoleInfo("[BypassHelper] 权限绕过钩子已注册");
        }

        public static void UnregisterPermissionHook()
        {
            PlayerHooks.PlayerPermission -= OnPlayerPermission;
            //TShock.Log.ConsoleInfo("[BypassHelper] 权限绕过钩子已注销");
        }

        private static void OnPlayerPermission(PlayerPermissionEventArgs args)
        {
            if (args.Player == null)
            {
                return;
            }
            
            if (_bypassCounters.TryGetValue(args.Player, out var counter) && counter.PermissionBypass > 0)
            {
                args.Result = PermissionHookResult.Granted;
                return;
            }
            
            if (_bypassCounters.TryGetValue(TSPlayer.Server, out var serverCounter) && serverCounter.PermissionBypass > 0)
            {
                args.Result = PermissionHookResult.Granted;
            }
        }
    }

    public class tools
    {
        public static void runas(CommandArgs args)
        {
            if (args.Parameters.Count <= 1)
            {
                args.Player.SendErrorMessage("Invalid syntax. Who and what to run?");
                return;
            }



            bool withoutcheck = false;
            List<string> parm = new List<string>(args.Parameters);

            // 只检查第一个参数是不是 -f
            if (parm.Count > 0 && parm[0] == "-f")
            {
                withoutcheck = true;
                parm.RemoveAt(0);

            }

            if (parm.Count != 2)
            {
                args.Player.SendErrorMessage("语法错误,请这样使用 runas 玩家名 \"命令内容\" ");
                return;
            }

            var player = TSPlayer.FindByNameOrID(parm[0]);
            if (player.Count == 1)
            {
                // Right one
            }
            else if (parm[0] == "*")
            {
                player = TShock.Players
               .Where(p => p != null && p.IsLoggedIn)
               .ToList();
            }
            else
            {
                if (player.Count == 0)
                {
                    args.Player.SendErrorMessage("玩家不存在.");
                    return;
                }
                if (player.Count > 1)
                {
                    args.Player.SendMultipleMatchError(player.Select(p => p.Name));
                    return;
                }
            }

            if (withoutcheck)
            {
                foreach (var p in player)
                {
                    BypassHelper.RunWithoutPermissionChecks(() => TShockAPI.Commands.HandleCommand(p, parm[1]), p);
                }
            }
            else
            {
                foreach (var p in player)
                {
                    TShockAPI.Commands.HandleCommand(p, parm[1]);
                }
            }
        }

        public static void remove(CommandArgs args)
        {
            if (args.Parameters.Count < 1)
            {
                args.Player.SendErrorMessage("语法错误。正确格式: /remove <玩家名|*> <物品ID|all> 或 /remove <物品ID|all>(清除所有玩家)");
                return;
            }

            string target;
            string itemIdStr;

            if (args.Parameters.Count == 1)
            {
                target = "*";
                itemIdStr = args.Parameters[0];
            }
            else
            {
                target = args.Parameters[0];
                itemIdStr = args.Parameters[1];
            }

            // all 模式：清空整个背包（所有角色、所有栏位：主背包/护甲/染料/时装/存钱罐/保险箱/垃圾桶/熔炉/虚空袋/三套配装）
            bool clearAll = itemIdStr.Equals("all", StringComparison.OrdinalIgnoreCase) || itemIdStr == "*";

            if (clearAll)
            {
                if (target == "*")
                {
                    args.Player.SendInfoMessage("正在后台批量清空所有玩家的整个背包，请稍后查看控制台日志...");
                    System.Threading.Tasks.Task.Run(BatchClearInventory);
                }
                else
                {
                    var account = TShock.UserAccounts.GetUserAccountByName(target);
                    if (account == null)
                    {
                        args.Player.SendErrorMessage($"找不到玩家: {target}");
                        return;
                    }

                    if (ClearInventoryFromPlayer(account.ID, account.Name))
                    {
                        args.Player.SendSuccessMessage($"已清空玩家 {target} 的整个背包(含存钱罐/保险箱/熔炉/虚空袋/三套配装)");
                    }
                    else
                    {
                        args.Player.SendErrorMessage($"玩家 {target} 没有可清空的角色数据");
                    }
                }
                return;
            }

            if (!int.TryParse(itemIdStr, out int netID))
            {
                args.Player.SendErrorMessage($"物品ID必须是数字或 all: {itemIdStr}");
                return;
            }

            string itemName = TShock.Utils.GetItemById(netID)?.Name ?? $"物品ID:{netID}";

            if (target == "*")
            {
                args.Player.SendInfoMessage("正在后台批量清除物品，请稍后查看控制台日志...");
                System.Threading.Tasks.Task.Run(() =>
                {
                    BatchRemoveItem(netID, itemName);
                });
            }
            else
            {
                var account = TShock.UserAccounts.GetUserAccountByName(target);
                if (account == null)
                {
                    args.Player.SendErrorMessage($"找不到玩家: {target}");
                    return;
                }

                if (RemoveItemFromPlayer(account.ID, account.Name, netID, itemName))
                {
                    args.Player.SendSuccessMessage($"已清除玩家 {target} 的物品: {itemName}");
                }
                else
                {
                    args.Player.SendErrorMessage($"玩家 {target} 的库存中没有物品ID: {netID}");
                }
            }
        }

        private static void BatchRemoveItem(int netID, string itemName)
        {
            int clearedCount = 0;
            IDbConnection db = TShock.DB;
            
            List<Tuple<int, string>> users = new List<Tuple<int, string>>();
            using (QueryResult res = db.QueryReader("SELECT ID, Username FROM Users"))
            {
                while (res.Read())
                {
                    users.Add(Tuple.Create(res.Get<int>("ID"), res.Get<string>("Username")));
                }
            }

            foreach (var user in users)
            {
                if (RemoveItemFromPlayer(user.Item1, user.Item2, netID, itemName))
                {
                    clearedCount++;
                }
                System.Threading.Thread.Sleep(_random.Next(50, 150));
            }

            TShock.Log.ConsoleInfo($"[remove] 批量清除完成，共清除 {clearedCount} 个玩家的物品: {itemName}");
        }

        private static bool RemoveItemFromPlayer(int accountId, string playerName, int netID, string itemName)
        {
            // 如果玩家在线，先同步当前状态到 DB，再修改 DB + 同步到客户端
            var onlinePlayers = TShockAPI.TSPlayer.FindByNameOrID(playerName);
            TSPlayer? onlinePlayer = null;
            if (onlinePlayers.Count > 0 && onlinePlayers[0].Active)
            {
                onlinePlayer = onlinePlayers[0];
                onlinePlayer.PlayerData.CopyCharacter(onlinePlayer);
                TShock.CharacterDB.InsertPlayerData(onlinePlayer);
            }

            try
            {
                IDbConnection db = TShock.DB;
                bool anyCleared = false;

                // 一个账号可能拥有多个角色(tsCharacter 每行一个角色)，必须遍历所有角色行
                using (QueryResult res = db.QueryReader("SELECT ID, Inventory FROM tsCharacter WHERE Account = @0", accountId))
                {
                    while (res.Read())
                    {
                        int characterId = res.Get<int>("ID");
                        string updated = RemoveItemFromInventoryString(res.Get<string>("Inventory"), netID, out bool cleared);
                        if (cleared)
                        {
                            db.Query("UPDATE tsCharacter SET Inventory = @0 WHERE ID = @1", updated, characterId);
                            anyCleared = true;
                        }
                    }
                }

                if (anyCleared)
                {
                    // 玩家在线 → 从 DB 重新加载并同步到客户端
                    if (onlinePlayer != null)
                    {
                        onlinePlayer.PlayerData = TShock.CharacterDB.GetPlayerData(onlinePlayer, accountId);
                        onlinePlayer.PlayerData.RestoreCharacter(onlinePlayer);
                    }

                    TShock.Log.ConsoleInfo($"[remove] 已清除玩家 {playerName} 的物品: {itemName}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[remove] 清除玩家 {playerName} 的物品失败: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// 在序列化的背包字符串中清除指定物品。
        /// 遍历全部槽位(NetItem.MaxInventory，含主背包/护甲/染料/时装/存钱罐/保险箱/垃圾桶/熔炉/虚空袋/三套配装)，
        /// 只要槽位 netID 匹配即整槽置空，确保"整个背包"都被覆盖。
        /// </summary>
        private static string RemoveItemFromInventoryString(string inventory, int netID, out bool cleared)
        {
            cleared = false;
            if (string.IsNullOrEmpty(inventory))
                return inventory;

            var slots = inventory.Split('~');
            for (int i = 0; i < slots.Length; i++)
            {
                var fields = slots[i].Split(',');
                // 空槽位或不完整字段直接跳过，避免解析异常导致整次清除失败
                if (fields.Length == 0 || !int.TryParse(fields[0], out int slotItemId) || slotItemId <= 0)
                    continue;

                if (slotItemId == netID)
                {
                    slots[i] = "0,0,0,0"; // 整槽置空(含 favorited 标志)，不留脏数据
                    cleared = true;
                }
            }

            return string.Join("~", slots);
        }

        /// <summary>
        /// 清空玩家整个背包(所有角色、所有栏位)。
        /// </summary>
        private static bool ClearInventoryFromPlayer(int accountId, string playerName)
        {
            // 在线玩家先同步当前状态到 DB
            var onlinePlayers = TShockAPI.TSPlayer.FindByNameOrID(playerName);
            TSPlayer? onlinePlayer = null;
            if (onlinePlayers.Count > 0 && onlinePlayers[0].Active)
            {
                onlinePlayer = onlinePlayers[0];
                onlinePlayer.PlayerData.CopyCharacter(onlinePlayer);
                TShock.CharacterDB.InsertPlayerData(onlinePlayer);
            }

            try
            {
                IDbConnection db = TShock.DB;
                bool anyCleared = false;

                using (QueryResult res = db.QueryReader("SELECT ID FROM tsCharacter WHERE Account = @0", accountId))
                {
                    while (res.Read())
                    {
                        int characterId = res.Get<int>("ID");
                        // 全空背包：NetItem.MaxInventory 个空槽位
                        string emptyInventory = string.Join("~", Enumerable.Repeat("0,0,0,0", NetItem.MaxInventory));
                        db.Query("UPDATE tsCharacter SET Inventory = @0 WHERE ID = @1", emptyInventory, characterId);
                        anyCleared = true;
                    }
                }

                if (anyCleared)
                {
                    // 玩家在线 → 从 DB 重新加载并同步到客户端
                    if (onlinePlayer != null)
                    {
                        onlinePlayer.PlayerData = TShock.CharacterDB.GetPlayerData(onlinePlayer, accountId);
                        onlinePlayer.PlayerData.RestoreCharacter(onlinePlayer);
                    }

                    TShock.Log.ConsoleInfo($"[remove] 已清空玩家 {playerName} 的整个背包");
                    return true;
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[remove] 清空玩家 {playerName} 的背包失败: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// 批量清空所有玩家的整个背包。
        /// </summary>
        private static void BatchClearInventory()
        {
            int clearedCount = 0;
            IDbConnection db = TShock.DB;

            List<Tuple<int, string>> users = new List<Tuple<int, string>>();
            using (QueryResult res = db.QueryReader("SELECT ID, Username FROM Users"))
            {
                while (res.Read())
                {
                    users.Add(Tuple.Create(res.Get<int>("ID"), res.Get<string>("Username")));
                }
            }

            foreach (var user in users)
            {
                if (ClearInventoryFromPlayer(user.Item1, user.Item2))
                {
                    clearedCount++;
                }
                System.Threading.Thread.Sleep(_random.Next(50, 150));
            }

            TShock.Log.ConsoleInfo($"[remove] 批量清空完成，共清空 {clearedCount} 个玩家的背包");
        }

        public static void find(CommandArgs args)
        {
            if (args.Parameters.Count < 1)
            {
                args.Player.SendErrorMessage("语法错误。正确格式: /find <物品ID>");
                return;
            }

            string itemIdStr = args.Parameters[0];

            if (!int.TryParse(itemIdStr, out int netID))
            {
                args.Player.SendErrorMessage($"物品ID必须是数字: {itemIdStr}");
                return;
            }

            string itemName = TShock.Utils.GetItemById(netID)?.Name ?? $"物品ID:{netID}";
            args.Player.SendInfoMessage($"正在查找拥有物品 {itemName} 的玩家...");

            System.Threading.Tasks.Task.Run(() =>
            {
                var players = FindPlayersWithItem(netID);

                if (players.Count > 0)
                {
                    TShock.Log.ConsoleInfo($"[find] 找到 {players.Count} 个玩家拥有物品 {itemName}:");
                    foreach (var player in players)
                    {
                        TShock.Log.ConsoleInfo($"  - {player}");
                    }

                    if (args.Player != null && args.Player.IsLoggedIn)
                    {
                        args.Player.SendSuccessMessage($"找到 {players.Count} 个玩家拥有物品 {itemName}:");
                        int displayCount = Math.Min(players.Count, 10);
                        for (int i = 0; i < displayCount; i++)
                        {
                            args.Player.SendInfoMessage($"  - {players[i]}");
                        }
                        if (players.Count > 10)
                        {
                            args.Player.SendInfoMessage($"  ... 还有 {players.Count - 10} 个玩家，请查看控制台日志");
                        }
                    }
                }
                else
                {
                    TShock.Log.ConsoleInfo($"[find] 未找到拥有物品 {itemName} 的玩家");
                    if (args.Player != null && args.Player.IsLoggedIn)
                    {
                        args.Player.SendErrorMessage($"未找到拥有物品 {itemName} 的玩家");
                    }
                }
            });
        }

        public static void pvp(CommandArgs args)
        {
            if (args.Parameters.Count == 0 || args.Parameters.Count > 2)
            {
                args.Player.SendInfoMessage("用法: /pvp <玩家名|*> on|off");
                args.Player.SendInfoMessage("      /pvp <玩家名>  - 查看当前 PVP 状态");
                return;
            }

            bool isWildcard = args.Parameters[0] == "*";

            // 仅一个参数：查看状态
            if (args.Parameters.Count == 1)
            {
                if (isWildcard)
                {
                    args.Player.SendInfoMessage("用法: /pvp * on|off");
                    return;
                }
                var queryPlayers = TShockAPI.TSPlayer.FindByNameOrID(args.Parameters[0]);
                if (queryPlayers.Count == 0)
                {
                    args.Player.SendErrorMessage("玩家不存在.");
                    return;
                }
                if (queryPlayers.Count > 1)
                {
                    args.Player.SendMultipleMatchError(queryPlayers.Select(p => p.Name));
                    return;
                }
                args.Player.SendInfoMessage($"{queryPlayers[0].Name} 当前 PVP: {(queryPlayers[0].Hostile ? "开启" : "关闭")}");
                return;
            }

            string mode = args.Parameters[1].ToLower();
            bool enable;
            if (mode == "on" || mode == "true" || mode == "1")
            {
                enable = true;
            }
            else if (mode == "off" || mode == "false" || mode == "0")
            {
                enable = false;
            }
            else
            {
                args.Player.SendErrorMessage("PVP 状态参数必须是 on 或 off.");
                return;
            }

            // * 通配符：批量设置所有在线玩家
            if (isWildcard)
            {
                int count = 0;
                foreach (var p in TShock.Players)
                {
                    if (p != null && p.Active && p.RealPlayer)
                    {
                        p.SetPvP(enable, false);
                        count++;
                    }
                }
                args.Player.SendSuccessMessage($"已将 {count} 个在线玩家的 PVP 设置为 {(enable ? "开启" : "关闭")}");
                return;
            }

            var players = TShockAPI.TSPlayer.FindByNameOrID(args.Parameters[0]);
            if (players.Count == 0)
            {
                args.Player.SendErrorMessage("玩家不存在.");
                return;
            }
            if (players.Count > 1)
            {
                args.Player.SendMultipleMatchError(players.Select(p => p.Name));
                return;
            }

            players[0].SetPvP(enable, true);
            args.Player.SendSuccessMessage($"已将 {players[0].Name} 的 PVP 设置为 {(enable ? "开启" : "关闭")}");
        }

        public static void pvplock(CommandArgs args)
        {
            if (args.Parameters.Count == 0 || args.Parameters.Count > 2)
            {
                ShowPvPLockHelp(args.Player);
                return;
            }

            bool isWildcard = args.Parameters[0] == "*";
            string target = args.Parameters[0];

            // 仅一个参数：查看锁定状态
            if (args.Parameters.Count == 1)
            {
                if (isWildcard)
                {
                    args.Player.SendInfoMessage($"全局 PVP 锁定: {FormatPvPLock(PvPLockManager.GlobalLock)}");
                    return;
                }
                args.Player.SendInfoMessage($"{target} 的 PVP 锁定: {FormatPvPLock(PvPLockManager.GetEffectiveLock(target))}");
                return;
            }

            string mode = args.Parameters[1].ToLower();
            bool? lockState;
            if (mode == "on" || mode == "true" || mode == "1")
            {
                lockState = true;
            }
            else if (mode == "off" || mode == "false" || mode == "0")
            {
                lockState = false;
            }
            else if (mode == "unlock" || mode == "none")
            {
                lockState = null;
            }
            else
            {
                args.Player.SendErrorMessage("锁定参数必须是 on/off/unlock.");
                return;
            }

            // * 通配符：全局锁定状态（新进入的玩家也会被强制应用）
            if (isWildcard)
            {
                PvPLockManager.SetGlobal(lockState);
                args.Player.SendSuccessMessage($"全局 PVP 锁定已设置为 {FormatPvPLock(lockState)}" +
                    (lockState != null ? "，对所有在线及新进玩家生效" : ""));
                return;
            }

            var players = TShockAPI.TSPlayer.FindByNameOrID(target);
            if (players.Count == 0)
            {
                // 离线玩家也允许按名字设置锁定（重新上线后生效）
                PvPLockManager.SetPlayer(target, lockState);
                args.Player.SendSuccessMessage($"已{(lockState == null ? "解除" : "锁定")}玩家 {target} 的 PVP: {FormatPvPLock(lockState)}");
                return;
            }
            if (players.Count > 1)
            {
                args.Player.SendMultipleMatchError(players.Select(p => p.Name));
                return;
            }

            var tp = players[0];
            PvPLockManager.SetPlayer(tp.Name, lockState);
            // 同步一次状态：锁定 → 强制为锁定值；解锁 → 将当前状态同步到客户端
            tp.SetPvP(lockState ?? tp.Hostile, false);
            args.Player.SendSuccessMessage($"已{(lockState == null ? "解除" : "锁定")}玩家 {tp.Name} 的 PVP: {FormatPvPLock(lockState)}");
        }

        private static void ShowPvPLockHelp(TSPlayer player)
        {
            player.SendInfoMessage("用法: /pvplock <玩家名|*> on|off|unlock");
            player.SendInfoMessage("      /pvplock <玩家名>  - 查看该玩家锁定状态");
            player.SendInfoMessage("      /pvplock *  - 查看全局锁定状态");
        }

        private static string FormatPvPLock(bool? state)
        {
            return state == null ? "未锁定" : (state.Value ? "开启(锁定)" : "关闭(锁定)");
        }

        public static List<string> FindPlayersWithItem(int netID)
        {
            List<string> playersWithItem = new List<string>();
            IDbConnection db = TShock.DB;

            List<Tuple<int, string>> users = new List<Tuple<int, string>>();
            using (QueryResult res = db.QueryReader("SELECT ID, Username FROM Users"))
            {
                while (res.Read())
                {
                    users.Add(Tuple.Create(res.Get<int>("ID"), res.Get<string>("Username")));
                }
            }

            foreach (var user in users)
            {
                if (PlayerHasItem(user.Item1, netID))
                {
                    playersWithItem.Add(user.Item2);
                }
                System.Threading.Thread.Sleep(_random.Next(10, 30));
            }

            return playersWithItem;
        }

        private static bool PlayerHasItem(int accountId, int netID)
        {
            try
            {
                IDbConnection db = TShock.DB;
                string strinventory = "";
                using (QueryResult res = db.QueryReader("SELECT Inventory FROM tsCharacter WHERE Account = @0", accountId))
                {
                    if (res.Read())
                    {
                        strinventory = res.Get<string>("Inventory");
                    }
                    else
                    {
                        return false;
                    }
                }

                if (strinventory != "")
                {
                    string[] arrinventory = strinventory.Split("~");
                    for (int i = 0; i < arrinventory.Length; i++)
                    {
                        string[] item = arrinventory[i].Split(",");
                        if (item.Length >= 1 && int.TryParse(item[0], out int slotItemId) && slotItemId == netID)
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[find] 查询玩家库存失败: {ex.Message}");
            }

            return false;
        }

        public static void banp(CommandArgs args)
        {
            if (args.Parameters.Count == 0)
            {
                args.Player.SendErrorMessage("语法错误。正确格式: /banp <对象> [-id|-name] [原因]");
                return;
            }

            List<string> parm = new List<string>(args.Parameters);
            string reason = "不当行为";
            bool forceById = false;
            bool forceByName = false;

            for (int i = parm.Count - 1; i >= 0; i--)
            {
                if (parm[i] == "-id")
                {
                    forceById = true;
                    parm.RemoveAt(i);
                }
                else if (parm[i] == "-name")
                {
                    forceByName = true;
                    parm.RemoveAt(i);
                }
            }

            if (parm.Count > 2)
            {
                reason = string.Join(" ", parm.GetRange(1, parm.Count - 1));
            }
            else if (parm.Count == 2)
            {
                reason = parm[1];
            }

            string target = parm[0];
            bool isNumeric = int.TryParse(target, out int targetId);

            if (forceById)
            {
                var account = TShock.UserAccounts.GetUserAccountByID(targetId);
                if (account == null)
                {
                    args.Player.SendErrorMessage($"找不到ID为 {targetId} 的账户");
                    return;
                }
                ExecuteBan(account.Name, targetId, reason, args);
                return;
            }

            if (forceByName)
            {
                var account = TShock.UserAccounts.GetUserAccountByName(target);
                if (account == null)
                {
                    args.Player.SendErrorMessage($"找不到名为 {target} 的账户");
                    return;
                }
                ExecuteBan(target, account.ID, reason, args);
                return;
            }

            if (isNumeric)
            {
                var accountById = TShock.UserAccounts.GetUserAccountByID(targetId);
                var accountByName = TShock.UserAccounts.GetUserAccountByName(target);

                bool idExists = accountById != null;
                bool nameExists = accountByName != null && accountByName.ID != targetId;

                if (idExists && nameExists)
                {
                    args.Player.SendErrorMessage($"ID {targetId} 和名称 {target} 同时匹配到账户，请使用 -id 或 -name 指定类型");
                    return;
                }
                else if (idExists)
                {
                    ExecuteBan(accountById.Name, targetId, reason, args);
                    return;
                }
                else if (nameExists)
                {
                    ExecuteBan(target, accountByName.ID, reason, args);
                    return;
                }
                else
                {
                    args.Player.SendErrorMessage($"找不到ID为 {targetId} 或名为 {target} 的账户");
                    return;
                }
            }
            else
            {
                var account = TShock.UserAccounts.GetUserAccountByName(target);
                if (account == null)
                {
                    args.Player.SendErrorMessage($"找不到名为 {target} 的账户");
                    return;
                }
                ExecuteBan(target, account.ID, reason, args);
            }
        }

        private static readonly Random _random = new Random();

        public static void ExecuteBan(string username, int accountId, string reason, CommandArgs args)
        {
            try
            {
                IDbConnection db = TShock.DB;
                string query = "SELECT ID, Username, UUID, KnownIPs FROM Users WHERE Username = @0";
                string uuid = null;
                List<string> ipList = new List<string>();

                using (QueryResult res = db.QueryReader(query, username))
                {
                    if (res.Read())
                    {
                        uuid = res.Get<string>("UUID");
                        string knownIPsJson = res.Get<string>("KnownIPs");

                        if (!string.IsNullOrEmpty(knownIPsJson))
                        {
                            try
                            {
                                ipList = JsonConvert.DeserializeObject<List<string>>(knownIPsJson) ?? new List<string>();
                            }
                            catch { }
                        }
                    }
                }

                string character = args.Player?.Account?.Name;
                if (string.IsNullOrEmpty(character))
                    character = "后台操作";

                DateTime now = DateTime.UtcNow;
                DateTime never = DateTime.MaxValue;

                TShock.Bans.InsertBan($"acc:{username}", reason, character, now, never);
                Thread.Sleep(_random.Next(100, 300));

                if (!string.IsNullOrEmpty(uuid))
                {
                    Thread.Sleep(_random.Next(100, 300));
                    TShock.Bans.InsertBan($"uuid:{uuid}", reason, character, now, never);
                }

                foreach (string ip in ipList)
                {
                    if (!string.IsNullOrEmpty(ip) && ip != "127.0.0.1")
                    {
                        Thread.Sleep(_random.Next(100, 300));
                        TShock.Bans.InsertBan($"ip:{ip}", reason, character, now, never);
                    }
                }

                args.Player.SendSuccessMessage($"已封禁账户 {username}，封禁原因: {reason}");
            }
            catch (Exception ex)
            {
                args.Player.SendErrorMessage($"封禁失败: {ex.Message}");
            }
        }

        public static void ExecuteBanCommand(string command, string type)
        {
            const int maxRetries = 5;
            const int baseDelayMs = 200;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    TShockAPI.Commands.HandleCommand(TShockAPI.TSPlayer.Server, "/" + command);
                    return;
                }
                catch (Exception ex)
                {
                    if (ex.Message.Contains("database is locked") && attempt < maxRetries)
                    {
                        int delay = baseDelayMs * (int)Math.Pow(2, attempt - 1);
                        TShock.Log.ConsoleError($"[banp] 数据库锁定，{type}封禁重试第 {attempt} 次，延迟 {delay}ms...");
                        System.Threading.Thread.Sleep(delay);
                    }
                    else
                    {
                        TShock.Log.ConsoleError($"[banp] {type}封禁失败: {ex.Message}");
                        throw;
                    }
                }
            }

            throw new Exception($"{type}封禁重试 {maxRetries} 次后仍失败");
        }

    }

    /// <summary>
    /// PVP 锁定管理器：支持按玩家锁定与全局锁定，状态持久化到 TSWeb/PvPLock.json（兼容热重载）。
    /// 全局锁定时新进入的玩家也会被强制应用。
    /// </summary>
    public static class PvPLockManager
    {
        private static readonly object _syncLock = new object();
        private static bool? _globalLock;                                     // null=未锁定, true=全开锁定, false=全关锁定
        private static readonly Dictionary<string, bool> _playerLocks = new(StringComparer.OrdinalIgnoreCase); // 玩家名→锁定状态
        private static string SavePath => Path.Combine(TShock.SavePath, "TSWeb", "PvPLock.json");
        private static TerrariaPlugin? _plugin;
        private static bool _initialized;

        public static bool? GlobalLock
        {
            get { lock (_syncLock) return _globalLock; }
        }

        public static void Initialize(TerrariaPlugin plugin)
        {
            if (_initialized)
                return;

            _plugin = plugin;
            Load();

            GetDataHandlers.TogglePvp.Register(OnTogglePvp);
            ServerApi.Hooks.NetGreetPlayer.Register(plugin, OnPlayerJoin);

            _initialized = true;
            TShock.Log.ConsoleInfo("[PvPLock] PVP 锁定模块已启用");
        }

        public static void Dispose()
        {
            if (!_initialized)
                return;

            GetDataHandlers.TogglePvp.UnRegister(OnTogglePvp);
            if (_plugin != null)
                ServerApi.Hooks.NetGreetPlayer.Deregister(_plugin, OnPlayerJoin);

            Save();
            _initialized = false;
            TShock.Log.ConsoleInfo("[PvPLock] PVP 锁定模块已停用");
        }

        /// <summary>
        /// 获取玩家生效的锁定状态（单玩家锁定优先于全局锁定）。
        /// </summary>
        public static bool? GetEffectiveLock(string playerName)
        {
            lock (_syncLock)
            {
                if (_playerLocks.TryGetValue(playerName, out bool v))
                    return v;
                return _globalLock;
            }
        }

        public static void SetGlobal(bool? state)
        {
            lock (_syncLock)
            {
                _globalLock = state;
            }
            // 对当前所有在线玩家立即强制生效
            ApplyLockToOnline();
            Save();
        }

        public static void SetPlayer(string playerName, bool? state)
        {
            lock (_syncLock)
            {
                if (state == null)
                    _playerLocks.Remove(playerName);
                else
                    _playerLocks[playerName] = state.Value;
            }
            Save();
        }

        private static void ApplyLockToOnline()
        {
            foreach (var p in TShock.Players)
            {
                if (p != null && p.Active && p.RealPlayer)
                {
                    var locked = GetEffectiveLock(p.Name);
                    // 锁定 → 强制为锁定值；未锁定 → 将当前状态同步到客户端
                    p.SetPvP(locked ?? p.Hostile, false);
                }
            }
        }

        private static void OnPlayerJoin(GreetPlayerEventArgs args)
        {
            try
            {
                if (args.Who < 0 || args.Who >= TShock.Players.Length)
                    return;
                var player = TShock.Players[args.Who];
                if (player == null || !player.Active)
                    return;

                var locked = GetEffectiveLock(player.Name);
                if (locked != null)
                    player.SetPvP(locked.Value, false);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[PvPLock] 玩家加入处理失败: {ex.Message}");
            }
        }

        private static void OnTogglePvp(object sender, GetDataHandlers.TogglePvpEventArgs args)
        {
            try
            {
                var player = args.Player;
                if (player == null)
                    return;

                var locked = GetEffectiveLock(player.Name);
                if (locked == null)
                    return;

                // 玩家请求的状态与锁定状态不一致 → 拒绝并强制恢复
                if (args.Pvp != locked.Value)
                {
                    args.Handled = true;
                    player.SetPvP(locked.Value, false);
                    player.SendInfoMessage($"你的 PVP 已被管理员锁定为 {(locked.Value ? "开启" : "关闭")}");
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[PvPLock] 拦截 PVP 切换失败: {ex.Message}");
            }
        }

        private class PvPLockSaveData
        {
            public bool? Global { get; set; }
            public Dictionary<string, bool> Players { get; set; } = new();
        }

        private static void Load()
        {
            try
            {
                if (!File.Exists(SavePath))
                    return;

                var data = JsonConvert.DeserializeObject<PvPLockSaveData>(File.ReadAllText(SavePath));
                if (data == null)
                    return;

                lock (_syncLock)
                {
                    _globalLock = data.Global;
                    _playerLocks.Clear();
                    if (data.Players != null)
                    {
                        foreach (var kv in data.Players)
                            _playerLocks[kv.Key] = kv.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[PvPLock] 加载锁定配置失败: {ex.Message}");
            }
        }

        private static void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(SavePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                lock (_syncLock)
                {
                    var data = new PvPLockSaveData
                    {
                        Global = _globalLock,
                        Players = new Dictionary<string, bool>(_playerLocks)
                    };
                    File.WriteAllText(SavePath, JsonConvert.SerializeObject(data, Formatting.Indented));
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[PvPLock] 保存锁定配置失败: {ex.Message}");
            }
        }
    }
}

    