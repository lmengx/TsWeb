﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using Terraria;
using Terraria.IO;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;
using Microsoft.Xna.Framework;

namespace TShockData
{
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

    public class PasswordManager
    {
        public static void ChangePassword(CommandArgs args)
        {
            if (args.Parameters.Count == 0)
            {
                args.Player.SendInfoMessage("用法:");
                args.Player.SendInfoMessage("/pwd <新密码> - 修改自己的密码");
                args.Player.SendInfoMessage("/pwd <玩家名> <新密码> - 修改指定玩家的密码(需要权限)");
                return;
            }

            if (args.Parameters.Count == 1)
            {
                ChangeOwnPassword(args);
            }
            else if (args.Parameters.Count >= 2)
            {
                ChangeOtherPassword(args);
            }
        }

        private static void ChangeOwnPassword(CommandArgs args)
        {
            if (!args.Player.IsLoggedIn)
            {
                args.Player.SendErrorMessage("请先登录");
                return;
            }

            string newPassword = args.Parameters[0];

            if (string.IsNullOrWhiteSpace(newPassword))
            {
                args.Player.SendErrorMessage("密码不能为空");
                return;
            }

            if (newPassword.Length < 4)
            {
                args.Player.SendErrorMessage("密码长度至少需要4个字符");
                return;
            }

            try
            {
                args.Player.Account.CreateBCryptHash(newPassword);
                TShock.DB.Query("UPDATE Users SET Password=@0 WHERE ID=@1", args.Player.Account.Password, args.Player.Account.ID);
                args.Player.SendSuccessMessage("密码修改成功");
                TShock.Log.ConsoleInfo($"玩家 {args.Player.Name} 修改了自己的密码");
            }
            catch (Exception ex)
            {
                args.Player.SendErrorMessage($"密码修改失败: {ex.Message}");
                TShock.Log.ConsoleError($"密码修改失败: {ex}");
            }
        }

        private static void ChangeOtherPassword(CommandArgs args)
        {
            if (!args.Player.HasPermission("tshock.admin.changepassword"))
            {
                args.Player.SendErrorMessage("你没有权限修改其他玩家的密码");
                return;
            }

            string targetName = args.Parameters[0];
            string newPassword = args.Parameters[1];

            if (string.IsNullOrWhiteSpace(newPassword))
            {
                args.Player.SendErrorMessage("密码不能为空");
                return;
            }

            if (newPassword.Length < 4)
            {
                args.Player.SendErrorMessage("密码长度至少需要4个字符");
                return;
            }

            var targetAccount = TShock.UserAccounts.GetUserAccountByName(targetName);
            if (targetAccount == null)
            {
                var accounts = TShock.UserAccounts.GetUserAccountsByName(targetName, true);
                if (accounts.Count == 1)
                {
                    targetAccount = accounts[0];
                }
                else if (accounts.Count > 1)
                {
                    args.Player.SendErrorMessage("找到多个同名玩家，请输入更精确的名字");
                    return;
                }
                else
                {
                    args.Player.SendErrorMessage($"找不到玩家: {targetName}");
                    return;
                }
            }

            try
            {
                targetAccount.CreateBCryptHash(newPassword);
                TShock.DB.Query("UPDATE Users SET Password=@0 WHERE ID=@1", targetAccount.Password, targetAccount.ID);
                args.Player.SendSuccessMessage($"已成功修改玩家 {targetAccount.Name} 的密码");
                TShock.Log.ConsoleInfo($"管理员 {args.Player.Name} 修改了玩家 {targetAccount.Name} 的密码");

                var onlinePlayer = TShock.Players.FirstOrDefault(p => p?.Account?.ID == targetAccount.ID);
                if (onlinePlayer != null)
                {
                    onlinePlayer.SendInfoMessage($"管理员已修改你的密码，请重新登录");
                }
            }
            catch (Exception ex)
            {
                args.Player.SendErrorMessage($"密码修改失败: {ex.Message}");
                TShock.Log.ConsoleError($"密码修改失败: {ex}");
            }
        }
    }

    public class ExportPlayer
        {
            private static readonly string ExportPath = Path.Combine(TShock.SavePath, "PlayerExports");

            private class MyPlayer : TSPlayer
            {
                public MyPlayer() : base(string.Empty)
                {
                    this.Account = new UserAccount();
                }

                public Player Player
                {
                    get => this.TPlayer;
                    set => typeof(TSPlayer).GetField("FakePlayer",
                                    System.Reflection.BindingFlags.NonPublic |
                                    System.Reflection.BindingFlags.Instance)!.
                                    SetValue(this, value);
                }
            }

            public static void Export(CommandArgs args)
            {
                if (args.Parameters.Count < 1)
                {
                    args.Player.SendInfoMessage("用法: /export <玩家名> 或 /export *");
                    return;
                }

                string target = args.Parameters[0];

                try
                {
                    if (!Directory.Exists(ExportPath))
                        Directory.CreateDirectory(ExportPath);

                    if (target.Equals("*", StringComparison.OrdinalIgnoreCase))
                    {
                        ExportAll(args.Player);
                    }
                    else
                    {
                        ExportByName(target, args.Player);
                    }
                }
                catch (Exception ex)
                {
                    args.Player.SendErrorMessage($"导出失败: {ex.Message}");
                    TShock.Log.ConsoleError($"[ExportPlayer] 导出失败: {ex}");
                }
            }

            private static void ExportByName(string playerName, TSPlayer executor)
            {
                var online = TShock.Players.FirstOrDefault(p => p?.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase) ?? false);
                
                if (online != null)
                {
                    string safeName = FormatFileName(playerName);
                    string exportDir = Path.Combine(ExportPath, FormatFileName(Main.worldName));
                    if (!Directory.Exists(exportDir))
                        Directory.CreateDirectory(exportDir);
                    
                    string filePath = Path.Combine(exportDir, $"{safeName}.plr");
                    
                    if (Export(online.TPlayer, filePath))
                    {
                        executor.SendSuccessMessage($"导出成功！目录: {filePath}");
                    }
                    else
                    {
                        executor.SendErrorMessage("导出失败");
                    }
                    return;
                }

                var users = TShock.UserAccounts.GetUserAccountsByName(playerName, true);
                if (users.Count == 1 || users.Count > 1 && users.Exists(x => x.Name == playerName))
                {
                    if (users.Count > 1)
                    {
                        users[0] = users.Find(x => x.Name == playerName)!;
                    }

                    Player player = NewPlayer(users[0]);
                    string safeName = FormatFileName(playerName);
                    string exportDir = Path.Combine(ExportPath, FormatFileName(Main.worldName));
                    if (!Directory.Exists(exportDir))
                        Directory.CreateDirectory(exportDir);
                    
                    string filePath = Path.Combine(exportDir, $"{safeName}.plr");

                    if (Export(player, filePath))
                    {
                        executor.SendSuccessMessage($"导出成功！目录: {filePath}");
                    }
                    else
                    {
                        executor.SendErrorMessage("导出失败，因数据残缺");
                    }
                }
                else if (users.Count == 0)
                {
                    executor.SendErrorMessage("未找到该玩家的备份数据");
                }
                else if (users.Count > 1)
                {
                    executor.SendErrorMessage("找到多个同名玩家，请输入更精确的名字");
                }
            }

            private static void ExportAll(TSPlayer executor)
            {
                List<string> yes = new();
                List<string> no = new();

                try
                {
                    var all = new List<UserAccount>();
                    using (var queryResult = TShock.DB.QueryReader("SELECT * FROM tsCharacter"))
                    {
                        while (queryResult.Read())
                        {
                            int num = queryResult.Get<int>("Account");
                            UserAccount user = TShock.UserAccounts.GetUserAccountByID(num);
                            all.Add(user);
                        }
                    }

                    executor.SendInfoMessage($"预计导出数量: {all.Count}");

                    if (all.Count < 1) return;

                    string worldName = FormatFileName(Main.worldName);
                    string exportDir = Path.Combine(ExportPath, worldName);

                    if (!Directory.Exists(exportDir))
                        Directory.CreateDirectory(exportDir);

                    foreach (var one in all)
                    {
                        var online = TShock.Players.FirstOrDefault(p => p?.Account?.ID == one.ID);
                        Player player;

                        if (online != null)
                            player = online.TPlayer;
                        else
                            player = NewPlayer(one);

                        string safeName = FormatFileName(one.Name);
                        string filePath = Path.Combine(exportDir, $"{safeName}.plr");

                        if (Export(player, filePath))
                            yes.Add(one.Name);
                        else
                            no.Add(one.Name);
                    }

                    if (no.Count > 0)
                    {
                        executor.SendErrorMessage($"导出失败: {string.Join(", ", no)}");
                    }

                    if (yes.Count > 0)
                    {
                        executor.SendSuccessMessage($"导出成功: {string.Join(", ", yes)}");
                    }

                    executor.SendInfoMessage($"导出目录: {exportDir}");
                }
                catch (Exception ex)
                {
                    executor.SendErrorMessage("导出存档错误");
                    TShock.Log.ConsoleError(ex.ToString());
                }
            }

            private static Player NewPlayer(UserAccount acc)
            {
                var p = new MyPlayer();
                p.Account.ID = acc.ID;
                p.Player = new Player { name = acc.Name };

                var data = TShock.CharacterDB.GetPlayerData(p, acc.ID);
                
                return CreatePlayerFromData(acc.Name, data);
            }

            private static Player CreatePlayerFromData(string name, PlayerData data)
            {
                if (data == null)
                {
                    TShock.Log.ConsoleError($"[ExportPlayer] CreatePlayerFromData: data 为 null");
                    return new Player { name = name };
                }

                var player = new Player
                {
                    name = name,
                    statLife = data.health,
                    statLifeMax = data.maxHealth,
                    statMana = data.mana,
                    statManaMax = data.maxMana,
                    extraAccessory = data.extraSlot == 1,
                    skinVariant = data.skinVariant ?? 0,
                    hair = data.hair ?? 0,
                    hairDye = data.hairDye,
                    hairColor = data.hairColor ?? Microsoft.Xna.Framework.Color.White,
                    pantsColor = data.pantsColor ?? Microsoft.Xna.Framework.Color.White,
                    shirtColor = data.shirtColor ?? Microsoft.Xna.Framework.Color.White,
                    underShirtColor = data.underShirtColor ?? Microsoft.Xna.Framework.Color.White,
                    shoeColor = data.shoeColor ?? Microsoft.Xna.Framework.Color.White,
                    hideVisibleAccessory = data.hideVisuals,
                    skinColor = data.skinColor ?? Microsoft.Xna.Framework.Color.White,
                    eyeColor = data.eyeColor ?? Microsoft.Xna.Framework.Color.White,
                    anglerQuestsFinished = data.questsCompleted,
                    UsingBiomeTorches = data.usingBiomeTorches == 1,
                    happyFunTorchTime = data.happyFunTorchTime == 1,
                    unlockedBiomeTorches = data.unlockedBiomeTorches == 1,
                    ateArtisanBread = data.ateArtisanBread == 1,
                    usedAegisCrystal = data.usedAegisCrystal == 1,
                    usedAegisFruit = data.usedAegisFruit == 1,
                    usedArcaneCrystal = data.usedArcaneCrystal == 1,
                    usedGalaxyPearl = data.usedGalaxyPearl == 1,
                    usedGummyWorm = data.usedGummyWorm == 1,
                    usedAmbrosia = data.usedAmbrosia == 1,
                    unlockedSuperCart = data.unlockedSuperCart == 1,
                    enabledSuperCart = data.enabledSuperCart == 1,
                    CurrentLoadoutIndex = data.currentLoadoutIndex
                };

                if (data.inventory != null)
                {
                    for (int i = 0; i < Math.Min(data.inventory.Length, NetItem.MaxInventory); i++)
                    {
                        var invItem = data.inventory[i];
                        if (i < NetItem.InventoryIndex.Item2)
                        {
                            player.inventory[i] = TShock.Utils.GetItemById(invItem.NetId);
                            player.inventory[i].stack = invItem.Stack;
                            player.inventory[i].prefix = invItem.PrefixId;
                        }
                        else if (i < NetItem.ArmorIndex.Item2)
                        {
                            int num = i - NetItem.ArmorIndex.Item1;
                            if (num >= 0 && num < player.armor.Length)
                            {
                                player.armor[num] = TShock.Utils.GetItemById(invItem.NetId);
                                player.armor[num].stack = invItem.Stack;
                                player.armor[num].prefix = invItem.PrefixId;
                            }
                        }
                        else if (i < NetItem.DyeIndex.Item2)
                        {
                            int num = i - NetItem.DyeIndex.Item1;
                            if (num >= 0 && num < player.dye.Length)
                            {
                                player.dye[num] = TShock.Utils.GetItemById(invItem.NetId);
                                player.dye[num].stack = invItem.Stack;
                                player.dye[num].prefix = invItem.PrefixId;
                            }
                        }
                        else if (i < NetItem.MiscEquipIndex.Item2)
                        {
                            int num = i - NetItem.MiscEquipIndex.Item1;
                            if (num >= 0 && num < player.miscEquips.Length)
                            {
                                player.miscEquips[num] = TShock.Utils.GetItemById(invItem.NetId);
                                player.miscEquips[num].stack = invItem.Stack;
                                player.miscEquips[num].prefix = invItem.PrefixId;
                            }
                        }
                        else if (i < NetItem.MiscDyeIndex.Item2)
                        {
                            int num = i - NetItem.MiscDyeIndex.Item1;
                            if (num >= 0 && num < player.miscDyes.Length)
                            {
                                player.miscDyes[num] = TShock.Utils.GetItemById(invItem.NetId);
                                player.miscDyes[num].stack = invItem.Stack;
                                player.miscDyes[num].prefix = invItem.PrefixId;
                            }
                        }
                        else if (i < NetItem.PiggyIndex.Item2)
                        {
                            int num = i - NetItem.PiggyIndex.Item1;
                            if (num >= 0 && num < player.bank.item.Length)
                            {
                                player.bank.item[num] = TShock.Utils.GetItemById(invItem.NetId);
                                player.bank.item[num].stack = invItem.Stack;
                                player.bank.item[num].prefix = invItem.PrefixId;
                            }
                        }
                        else if (i < NetItem.SafeIndex.Item2)
                        {
                            int num = i - NetItem.SafeIndex.Item1;
                            if (num >= 0 && num < player.bank2.item.Length)
                            {
                                player.bank2.item[num] = TShock.Utils.GetItemById(invItem.NetId);
                                player.bank2.item[num].stack = invItem.Stack;
                                player.bank2.item[num].prefix = invItem.PrefixId;
                            }
                        }
                        else if (i < NetItem.TrashIndex.Item2)
                        {
                            player.trashItem = TShock.Utils.GetItemById(invItem.NetId);
                            player.trashItem.stack = invItem.Stack;
                            player.trashItem.prefix = invItem.PrefixId;
                        }
                        else if (i < NetItem.ForgeIndex.Item2)
                        {
                            int num = i - NetItem.ForgeIndex.Item1;
                            if (num >= 0 && num < player.bank3.item.Length)
                            {
                                player.bank3.item[num] = TShock.Utils.GetItemById(invItem.NetId);
                                player.bank3.item[num].stack = invItem.Stack;
                                player.bank3.item[num].prefix = invItem.PrefixId;
                            }
                        }
                        else if (i < NetItem.VoidIndex.Item2)
                        {
                            int num = i - NetItem.VoidIndex.Item1;
                            if (num >= 0 && num < player.bank4.item.Length)
                            {
                                player.bank4.item[num] = TShock.Utils.GetItemById(invItem.NetId);
                                player.bank4.item[num].stack = invItem.Stack;
                                player.bank4.item[num].prefix = invItem.PrefixId;
                            }
                        }
                        else if (i < NetItem.Loadout1Armor.Item2)
                        {
                            int num = i - NetItem.Loadout1Armor.Item1;
                            if (num >= 0 && num < player.Loadouts[0].Armor.Length)
                            {
                                player.Loadouts[0].Armor[num] = TShock.Utils.GetItemById(invItem.NetId);
                                player.Loadouts[0].Armor[num].stack = invItem.Stack;
                                player.Loadouts[0].Armor[num].prefix = invItem.PrefixId;
                            }
                        }
                        else if (i < NetItem.Loadout1Dye.Item2)
                        {
                            int num = i - NetItem.Loadout1Dye.Item1;
                            if (num >= 0 && num < player.Loadouts[0].Dye.Length)
                            {
                                player.Loadouts[0].Dye[num] = TShock.Utils.GetItemById(invItem.NetId);
                                player.Loadouts[0].Dye[num].stack = invItem.Stack;
                                player.Loadouts[0].Dye[num].prefix = invItem.PrefixId;
                            }
                        }
                        else if (i < NetItem.Loadout2Armor.Item2)
                        {
                            int num = i - NetItem.Loadout2Armor.Item1;
                            if (num >= 0 && num < player.Loadouts[1].Armor.Length)
                            {
                                player.Loadouts[1].Armor[num] = TShock.Utils.GetItemById(invItem.NetId);
                                player.Loadouts[1].Armor[num].stack = invItem.Stack;
                                player.Loadouts[1].Armor[num].prefix = invItem.PrefixId;
                            }
                        }
                        else if (i < NetItem.Loadout2Dye.Item2)
                        {
                            int num = i - NetItem.Loadout2Dye.Item1;
                            if (num >= 0 && num < player.Loadouts[1].Dye.Length)
                            {
                                player.Loadouts[1].Dye[num] = TShock.Utils.GetItemById(invItem.NetId);
                                player.Loadouts[1].Dye[num].stack = invItem.Stack;
                                player.Loadouts[1].Dye[num].prefix = invItem.PrefixId;
                            }
                        }
                        else if (i < NetItem.Loadout3Armor.Item2)
                        {
                            int num = i - NetItem.Loadout3Armor.Item1;
                            if (num >= 0 && num < player.Loadouts[2].Armor.Length)
                            {
                                player.Loadouts[2].Armor[num] = TShock.Utils.GetItemById(invItem.NetId);
                                player.Loadouts[2].Armor[num].stack = invItem.Stack;
                                player.Loadouts[2].Armor[num].prefix = invItem.PrefixId;
                            }
                        }
                        else if (i < NetItem.Loadout3Dye.Item2)
                        {
                            int num = i - NetItem.Loadout3Dye.Item1;
                            if (num >= 0 && num < player.Loadouts[2].Dye.Length)
                            {
                                player.Loadouts[2].Dye[num] = TShock.Utils.GetItemById(invItem.NetId);
                                player.Loadouts[2].Dye[num].stack = invItem.Stack;
                                player.Loadouts[2].Dye[num].prefix = invItem.PrefixId;
                            }
                        }
                    }
                }

                return player;
            }

            private static bool Export(Player? player, string filePath)
            {
                if (player is null) return false;

                var playerName = FormatFileName(player.name);

                PlayerFileData data = new PlayerFileData();
                data.Metadata = FileMetadata.FromCurrentSettings(FileType.Player);
                data.Player = player;
                data._isCloudSave = false;
                FileData fileData = data;
                fileData._path = filePath;
                data.SetPlayTime(new TimeSpan(0));
                Main.LocalFavoriteData.ClearEntry(data);

                try
                {
                    string path = data.Path;

                    if (string.IsNullOrEmpty(path))
                    {
                        return false;
                    }

                    string exportDir = Path.GetDirectoryName(path)!;
                    if (!Directory.Exists(exportDir))
                        Directory.CreateDirectory(exportDir);

                    Player.InternalSavePlayerFile(data);
                    return true;
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleError("导出plr文件错误:\n" + ex.ToString());
                    TShock.Log.ConsoleError($"名字: {playerName}, 路径: {data.Path}");
                    return false;
                }
            }

            private static string FormatFileName(string text)
            {
                for (int i = 0; i < text.Length; ++i)
                {
                    bool flag = text[i] == '\\' || text[i] == '/' || text[i] == ':' || text[i] == '*' || text[i] == '?' || text[i] == '"' || text[i] == '<' || text[i] == '>' || text[i] == '|';
                    if (flag)
                    {
                        text = text.Replace(text[i], '-');
                    }
                }
                return text;
            }
        }




}