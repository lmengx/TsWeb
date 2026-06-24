﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using System.Collections.Concurrent;
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

        private static void ExecuteBan(string username, int accountId, string reason, CommandArgs args)
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

                ExecuteBanCommand($"ban add \"acc:{username}\" \"{reason}\" -e", "账户");

                if (!string.IsNullOrEmpty(uuid))
                {
                    Thread.Sleep(_random.Next(100, 300));
                    ExecuteBanCommand($"ban add \"uuid:{uuid}\" \"{reason}\" -e", "UUID");
                }

                foreach (string ip in ipList)
                {
                    if (!string.IsNullOrEmpty(ip) && ip != "127.0.0.1")
                    {
                        Thread.Sleep(_random.Next(100, 300));
                        ExecuteBanCommand($"ban add \"ip:{ip}\" \"{reason}\" -e", $"IP({ip})");
                    }
                }

                args.Player.SendSuccessMessage($"已封禁账户 {username}，封禁原因: {reason}");
            }
            catch (Exception ex)
            {
                args.Player.SendErrorMessage($"封禁失败: {ex.Message}");
            }
        }

        private static void ExecuteBanCommand(string command, string type)
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

    