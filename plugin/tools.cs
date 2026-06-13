using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using TShockAPI;
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





    }
}

    