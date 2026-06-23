﻿﻿﻿﻿﻿﻿using System.Collections.Concurrent;
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

    public static class BypassHelper
    {
        private static readonly ConcurrentDictionary<TSPlayer, int> _bypassCount = new();

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static void RunWithoutPermissionChecks(Action action, TSPlayer? player = null)
        {



            TSPlayer target = (player != null && player.RealPlayer) ? player : TSPlayer.Server;



            // 先取出/初始化计数变量
            int count = _bypassCount.GetOrAdd(target, 0);
            Interlocked.Increment(ref count);
            // 写回字典
            _bypassCount[target] = count;

            try
            {
                TShock.Log.Info($"[权限绕过计数] 玩家: {target.Name} | 当前层数: {count}");
                action();
            }
            finally
            {
                if (_bypassCount.TryGetValue(target, out int current))
                {
                    Interlocked.Decrement(ref current);
                    if (current <= 0)
                    {
                        _bypassCount.TryRemove(target, out _);
                    }
                    else
                    {
                        _bypassCount[target] = current;
                    }
                }
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

                TShockAPI.Commands.HandleCommand(TShockAPI.TSPlayer.Server, $"/ban add \"acc:{username}\" \"{reason}\" -e");

                if (!string.IsNullOrEmpty(uuid))
                {
                    TShockAPI.Commands.HandleCommand(TShockAPI.TSPlayer.Server, $"/ban add \"uuid:{uuid}\" \"{reason}\" -e");
                }

                foreach (string ip in ipList)
                {
                    if (!string.IsNullOrEmpty(ip) && ip != "127.0.0.1")
                    {
                        TShockAPI.Commands.HandleCommand(TShockAPI.TSPlayer.Server, $"/ban add \"ip:{ip}\" \"{reason}\" -e");
                    }
                }

                args.Player.SendSuccessMessage($"已封禁账户 {username}，封禁原因: {reason}");
            }
            catch (Exception ex)
            {
                args.Player.SendErrorMessage($"封禁失败: {ex.Message}");
            }
        }

    }
}

    