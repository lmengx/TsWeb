﻿﻿﻿﻿﻿﻿﻿﻿using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using MonoMod.RuntimeDetour;
using Rests;
using TShockAPI;
using TShockAPI.DB;
using Terraria;
using TerrariaApi.Server;

namespace Silent
{
    [ApiVersion(2, 1)]
    public class SilentPlugin : TerrariaPlugin
    {
        public override string Author => "lmx12330";
        public override string Description => "管理员无痕进服插件 - 隐身、无敌、隐藏进服消息";
        public override string Name => "Silent";
        public override Version Version => new Version(1, 0, 0, 0);

        private static readonly HashSet<int> SilentPlayers = new HashSet<int>();
        private static readonly List<Hook> _hooks = new List<Hook>();

        public SilentPlugin(Main game) : base(game) { }

        public override void Initialize()
        {
            RegisterHooks();
            RegisterCommands();

            ServerApi.Hooks.GameUpdate.Register(this, OnUpdate);
            ServerApi.Hooks.ServerJoin.Register(this, OnJoin);
            ServerApi.Hooks.ServerChat.Register(this, OnChat);
        }

        private void RegisterHooks()
        {
            try
            {
                var netMessageType = typeof(NetMessage);
                var sendDataMethods = new List<MethodInfo>();

                foreach (var method in netMessageType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (method.Name == "SendData" && method.ReturnType == typeof(void))
                    {
                        sendDataMethods.Add(method);
                    }
                }

                foreach (var sendDataMethod in sendDataMethods)
                {
                    try
                    {
                        var hook = new Hook(sendDataMethod, typeof(SilentPlugin).GetMethod(nameof(HookedSendData), BindingFlags.NonPublic | BindingFlags.Static)!);
                        _hooks.Add(hook);
                    }
                    catch { }
                }

                TShock.Log.ConsoleInfo($"[Silent] Hooked {sendDataMethods.Count} SendData methods");
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"Silent: Failed to initialize hook: {ex.Message}");
            }
        }

        private void RegisterCommands()
        {
            Commands.ChatCommands.Add(new Command("silent.use", SilentCommand, "silent", "st"));
        }

        private void SilentCommand(CommandArgs args)
        {
            if (args.Parameters.Count == 0)
            {
                args.Player.SendInfoMessage("用法: /silent add <玩家名> | /silent del <玩家名> | /silent list");
                args.Player.SendInfoMessage("别名: /st add <玩家名> | /st del <玩家名> | /st list");
                return;
            }

            var subCmd = args.Parameters[0].ToLower();

            switch (subCmd)
            {
                case "add":
                    if (args.Parameters.Count < 2)
                    {
                        args.Player.SendErrorMessage("用法: /silent add <玩家名>");
                        return;
                    }
                    var playerName = string.Join(" ", args.Parameters.Skip(1));
                    var players = TShock.Players.Where(p => p != null && p.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (players.Count == 0)
                    {
                        var account = TShock.UserAccounts.GetUserAccountByName(playerName);
                        if (account == null)
                        {
                            args.Player.SendErrorMessage($"找不到玩家: {playerName}");
                            return;
                        }
                        args.Player.SendSuccessMessage($"已将离线玩家 {account.Name} 添加到无痕列表");
                        SilentPlayers.Add(-account.ID);
                    }
                    else
                    {
                        foreach (var p in players)
                        {
                            EnableSilentMode(p);
                        }
                        args.Player.SendSuccessMessage($"已将玩家 {players[0].Name} 添加到无痕列表");
                    }
                    break;

                case "del":
                    if (args.Parameters.Count < 2)
                    {
                        args.Player.SendErrorMessage("用法: /silent del <玩家名>");
                        return;
                    }
                    var delName = string.Join(" ", args.Parameters.Skip(1));
                    var delPlayers = TShock.Players.Where(p => p != null && p.Name.Equals(delName, StringComparison.OrdinalIgnoreCase)).ToList();
                    bool removed = false;
                    if (delPlayers.Count > 0)
                    {
                        foreach (var p in delPlayers)
                        {
                            DisableSilentMode(p);
                        }
                        args.Player.SendSuccessMessage($"已将玩家 {delPlayers[0].Name} 从无痕列表移除");
                        removed = true;
                    }
                    var delAccount = TShock.UserAccounts.GetUserAccountByName(delName);
                    if (delAccount != null && SilentPlayers.Remove(-delAccount.ID))
                    {
                        args.Player.SendSuccessMessage($"已将离线玩家 {delAccount.Name} 从无痕列表移除");
                        removed = true;
                    }
                    if (!removed)
                    {
                        args.Player.SendErrorMessage($"找不到玩家: {delName}");
                    }
                    break;

                case "list":
                    var onlineList = TShock.Players.Where(p => p != null && SilentPlayers.Contains(p.Index)).Select(p => p.Name).ToList();
                    var offlineList = SilentPlayers.Where(i => i < 0).Select(i =>
                    {
                        var acc = TShock.UserAccounts.GetUserAccountByID(-i);
                        return acc?.Name ?? "Unknown";
                    }).ToList();

                    args.Player.SendInfoMessage($"在线无痕玩家 ({onlineList.Count}): {string.Join(", ", onlineList)}");
                    args.Player.SendInfoMessage($"离线无痕玩家 ({offlineList.Count}): {string.Join(", ", offlineList)}");
                    break;

                default:
                    args.Player.SendErrorMessage("未知子命令。可用: add, del, list");
                    break;
            }
        }

        private void EnableSilentMode(TSPlayer player)
        {
            if (player == null) return;

            SilentPlayers.Add(player.Index);
            player.SilentJoinInProgress = true;
            player.SilentKickInProgress = true;
            player.GodMode = true;

            Main.player[player.Index].active = false;
            player.SendInfoMessage("你已进入无痕模式");

            TShock.Log.ConsoleInfo($"[Silent] EnableSilent: Main.player[{player.Index}].active={Main.player[player.Index].active}");
            NetMessage.SendData((int)PacketTypes.PlayerActive, -1, player.Index, null, player.Index, 0f);
        }

        private void DisableSilentMode(TSPlayer player)
        {
            if (player == null) return;

            SilentPlayers.Remove(player.Index);
            player.SilentJoinInProgress = false;
            player.SilentKickInProgress = false;
            player.GodMode = false;

            Main.player[player.Index].active = true;
            player.SendInfoMessage("你已退出无痕模式");

            TShock.Log.ConsoleInfo($"[Silent] DisableSilent: Main.player[{player.Index}].active={Main.player[player.Index].active}");
            NetMessage.SendData((int)PacketTypes.PlayerActive, -1, player.Index, null, player.Index, 1f);
            NetMessage.SendData((int)PacketTypes.PlayerInfo, -1, player.Index, null, player.Index, 1f);
        }

        private void OnUpdate(EventArgs args)
        {
            foreach (var index in SilentPlayers.ToList())
            {
                if (index >= 0 && index < TShock.Players.Length)
                {
                    var player = TShock.Players[index];
                    if (player != null && player.Active)
                    {
                        if (Main.player[player.Index].active)
                        {
                            Main.player[player.Index].active = false;
                            NetMessage.SendData((int)PacketTypes.PlayerActive, -1, player.Index, null, player.Index, 0f);
                            TShock.Log.ConsoleInfo($"[Silent] OnUpdate: Force set active=false for {player.Name}");
                        }
                        player.GodMode = true;
                    }
                }
            }
        }

        private void OnJoin(JoinEventArgs args)
        {
            var player = TShock.Players[args.Who];
            if (player == null) return;

            bool isSilent = false;

            if (SilentPlayers.Contains(args.Who))
            {
                isSilent = true;
            }
            else if (player.Account != null && SilentPlayers.Contains(-player.Account.ID))
            {
                SilentPlayers.Remove(-player.Account.ID);
                SilentPlayers.Add(args.Who);
                isSilent = true;
            }
            else
            {
                foreach (var silentId in SilentPlayers.Where(i => i < 0).ToList())
                {
                    var acc = TShock.UserAccounts.GetUserAccountByID(-silentId);
                    if (acc != null && acc.Name.Equals(player.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        SilentPlayers.Remove(silentId);
                        SilentPlayers.Add(args.Who);
                        isSilent = true;
                        break;
                    }
                }
            }

            if (isSilent)
            {
                TShock.Log.ConsoleInfo($"[Silent] OnJoin: Enabling silent mode for {player.Name}");
                EnableSilentMode(player);
            }
        }

        private void OnChat(ServerChatEventArgs args)
        {
            var player = TShock.Players[args.Who];
            if (player != null && SilentPlayers.Contains(args.Who))
            {
                string text = args.Text;
                if (text != null && text.Length > 0)
                {
                    char prefix = text[0];
                    string specifier = TShock.Config.Settings.CommandSpecifier ?? "/";
                    string silentSpecifier = TShock.Config.Settings.CommandSilentSpecifier ?? ".";
                    if (prefix == specifier[0] || prefix == silentSpecifier[0])
                    {
                        return;
                    }
                }
                args.Handled = true;
                player.SendMessage("无痕模式下无法聊天", 255, 100, 100);
            }
        }

        private delegate void OrigSendData(int msgType, int remoteClient, int ignoreClient, object? text, int number, float number2, float number3, float number4, int number5, int number6, int number7);

        private static void HookedSendData(OrigSendData orig, int msgType, int remoteClient, int ignoreClient, object? text, int number, float number2, float number3, float number4, int number5, int number6, int number7)
        {
            var packetType = (PacketTypes)msgType;

            if (packetType == PacketTypes.PlayerActive && remoteClient == -1)
            {
                if (SilentPlayers.Contains(number) && number5 == 1)
                {
                    orig(msgType, remoteClient, ignoreClient, text, number, 0f, number3, number4, number5, number6, number7);
                    return;
                }
            }

            orig(msgType, remoteClient, ignoreClient, text, number, number2, number3, number4, number5, number6, number7);
        }

        protected override void Dispose(bool Disposing)
        {
            if (Disposing)
            {
                foreach (var hook in _hooks)
                {
                    hook.Dispose();
                }
                _hooks.Clear();

                ServerApi.Hooks.GameUpdate.Deregister(this, OnUpdate);
                ServerApi.Hooks.ServerJoin.Deregister(this, OnJoin);
                ServerApi.Hooks.ServerChat.Deregister(this, OnChat);
            }
            base.Dispose(Disposing);
        }
    }
}
