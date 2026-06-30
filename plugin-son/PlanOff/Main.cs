using Microsoft.Xna.Framework;


using Terraria;
using TerrariaApi.Server;
using TShockAPI;

namespace TShockData
{
    [ApiVersion(2, 1)]
    public class TShockData : TerrariaPlugin
    {
        public override string Author => "lmx12330";
        public override string Description => "计划关服重启";
        public override string Name => "PlanOff";
        public override Version Version => new Version(1, 0, 0, 0);
        public TShockData(Main game) : base(game) { }
        public override void Initialize()
        {

            TShockAPI.Commands.ChatCommands.Add(new Command("tools.planoff", PlannedOff.PlanOff, "planoff"));
            PlannedOff.Initialize(this);

        }
        protected override void Dispose(bool Disposing)
        {
            if (Disposing)
            {
                PlannedOff.Dispose();
            }
            base.Dispose(Disposing);
        }


    }

    public class PlannedOff
    {
        private static Timer? offTimer;
        private static int offDelay = 0;
        private static bool isOffScheduled = false;
        private static DateTime? scheduledOffTime;
        private static TerrariaPlugin? _plugin;

        public static void Initialize(TerrariaPlugin plugin)
        {
            _plugin = plugin;
            ServerApi.Hooks.NetGreetPlayer.Register(plugin, OnPlayerJoin);
            ServerApi.Hooks.ServerLeave.Register(plugin, OnPlayerLeave);
        }

        public static void Dispose()
        {
            if (_plugin != null)
            {
                ServerApi.Hooks.NetGreetPlayer.Deregister(_plugin, OnPlayerJoin);
                ServerApi.Hooks.ServerLeave.Deregister(_plugin, OnPlayerLeave);
            }
            CancelOff();
        }

        public static void PlanOff(CommandArgs args)
        {
            if (args.Parameters.Count == 0)
            {
                args.Player.SendInfoMessage("用法: /planrestart <时间(秒)> 或 /planrestart cancel");
                if (isOffScheduled && scheduledOffTime != null)
                {
                    var remaining = scheduledOffTime.Value - DateTime.Now;
                    args.Player.SendInfoMessage($"当前计划关闭剩余时间: {(int)remaining.TotalSeconds} 秒");
                }
                return;
            }

            if (args.Parameters[0].ToLower() == "cancel")
            {
                if (!isOffScheduled)
                {
                    args.Player.SendErrorMessage("当前没有计划关闭任务");
                    return;
                }
                CancelOff();
                args.Player.SendSuccessMessage("已取消计划关闭");
                TShock.Utils.Broadcast("[c/00ff00:计划关闭已取消]", Color.White);
                return;
            }

            if (!int.TryParse(args.Parameters[0], out int time) || time <= 0)
            {
                args.Player.SendErrorMessage("时间必须是正整数(秒)");
                return;
            }

            offDelay = time;

            args.Player.SendSuccessMessage($"已设置计划关闭时间: {time} 秒");

            if (GetActivePlayerCount() == 0)
            {
                TShock.Utils.Broadcast($"[c/00ff00:服务器无玩家，将在 {time} 秒后关闭]", Color.White);
                StartOffTimer();
            }
        }

        private static void OnPlayerJoin(GreetPlayerEventArgs args)
        {
            if (isOffScheduled)
            {
                CancelOff();
                TShock.Utils.Broadcast("[c/00ff00:有玩家连接，关闭计划已暂停]", Color.White);
            }
        }

        private static void OnPlayerLeave(LeaveEventArgs args)
        {
            int playerCount = GetActivePlayerCount();

            if (offDelay > 0 && playerCount <= 1)
            {
                TShock.Utils.Broadcast($"[c/00ff00:服务器无玩家，将在 {offDelay} 秒后关闭]", Color.White);
                StartOffTimer();
            }
        }

        private static void StartOffTimer()
        {
            if (offTimer != null)
            {
                offTimer.Dispose();
            }

            isOffScheduled = true;
            scheduledOffTime = DateTime.Now.AddSeconds(offDelay);

            offTimer = new Timer(OffServer, null, offDelay * 1000, Timeout.Infinite);
        }

        private static void CancelOff()
        {
            if (offTimer != null)
            {
                offTimer.Dispose();
                offTimer = null;
            }
            isOffScheduled = false;
            scheduledOffTime = null;
        }

        private static void OffServer(object? state)
        {
            int playerCount = GetActivePlayerCount();

            if (playerCount > 0)
            {
                CancelOff();
                return;
            }

            CancelOff();

            TShock.Utils.Broadcast("[c/00ff00:服务器正在关闭...]", Color.White);

            Thread.Sleep(2000);

            Commands.HandleCommand(TShock.Players[0], "/off");
        }

        private static int GetActivePlayerCount()
        {
            int count = 0;
            foreach (var player in TShock.Players)
            {
                if (player != null && player.Active)
                {
                    count++;
                }
            }
            return count;
        }
    }

}