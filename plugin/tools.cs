﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
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
                args.Player.SendErrorMessage("语法错误。正确格式: /remove <玩家名|*> <物品ID> 或 /remove <物品ID>(清除所有玩家)");
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

            if (!int.TryParse(itemIdStr, out int netID))
            {
                args.Player.SendErrorMessage($"物品ID必须是数字: {itemIdStr}");
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
                    bool cleared = false;

                    for (int i = 0; i < arrinventory.Length; i++)
                    {
                        string[] item = arrinventory[i].Split(",");
                        if (item.Length >= 1 && int.TryParse(item[0], out int slotItemId) && slotItemId == netID)
                        {
                            item[0] = "0";
                            item[1] = "0";
                            item[2] = "0";
                            arrinventory[i] = string.Join(",", item);
                            cleared = true;
                        }
                    }

                    if (cleared)
                    {
                        string finalinv = string.Join("~", arrinventory);
                        db.Query("UPDATE tsCharacter SET Inventory = @0 WHERE Account = @1", finalinv, accountId);

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
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[remove] 清除玩家 {playerName} 的物品失败: {ex.Message}");
            }

            return false;
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
}

    