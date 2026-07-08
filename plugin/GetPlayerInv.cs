using Rests;
using System;
using System.Collections.Generic;
using System.Data;
using Newtonsoft.Json.Linq;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;

namespace TShockData
{
    public class InventoryData
    {
        public int netID { get; set; }
        public int prefix { get; set; }
        public int stack { get; set; }
        public int slot { get; set; }
        public bool favorited { get; set; }
        public InventoryData(int netID, int prefix, int stack, int slot, bool favorited = false)
        {
            this.netID = netID;
            this.prefix = prefix;
            this.stack = stack;
            this.slot = slot;
            this.favorited = favorited;
        }
    }

    public class GetPlayerInv
    {
        public static object EditInv(RestRequestArgs args)
        {
            int netID = int.Parse(args.Parameters["netID"]);
            int prefix = int.Parse(args.Parameters["prefix"]);
            int stack = int.Parse(args.Parameters["stack"]);
            int index = int.Parse(args.Parameters["index"]);
            string player = args.Parameters["player"];
            var account = TShock.UserAccounts.GetUserAccountByName(player);
            if (account == null)
            {
                return new RestObject("404")
                {
                    { "error", "找不到玩家" }
                };
            }

            try
            {
                IDbConnection db = TShock.DB;

                // 如果玩家在线，先同步当前内存状态到 DB，再修改 DB + 同步到客户端
                var onlinePlayers = TShockAPI.TSPlayer.FindByNameOrID(account.Name);
                TSPlayer? onlinePlayer = null;
                if (onlinePlayers.Count > 0 && onlinePlayers[0].Active)
                {
                    onlinePlayer = onlinePlayers[0];
                    // 将玩家当前内存中的背包/装备/属性保存到 DB
                    onlinePlayer.PlayerData.CopyCharacter(onlinePlayer);
                    TShock.CharacterDB.InsertPlayerData(onlinePlayer);
                }

                string strinventory = "";
                using (QueryResult res = db.QueryReader("SELECT Inventory FROM tsCharacter WHERE Account = @0", account.ID))
                {
                    if (res.Read())
                    {
                        strinventory = res.Get<string>("Inventory");
                    }
                }

                if (string.IsNullOrEmpty(strinventory))
                {
                    return new RestObject("400")
                    {
                        { "error", "数据库错误" }
                    };
                }

                string[] arrinventory = strinventory.Split("~");
                if (index < 0 || index >= arrinventory.Length)
                {
                    return new RestObject("400")
                    {
                        { "error", $"索引 {index} 超出范围 (0-{arrinventory.Length - 1})" }
                    };
                }

                string[] item = arrinventory[index].Split(",");
                item[0] = netID.ToString();
                item[1] = stack.ToString();
                item[2] = prefix.ToString();
                arrinventory[index] = string.Join(",", item);
                string finalinv = string.Join("~", arrinventory);
                db.Query("UPDATE tsCharacter SET Inventory = @0 WHERE Account = @1", finalinv, account.ID);

                // 玩家在线 → 用 RestoreCharacter 将修改后的数据应用到内存并同步客户端
                if (onlinePlayer != null)
                {
                    onlinePlayer.PlayerData = TShock.CharacterDB.GetPlayerData(onlinePlayer, account.ID);
                    onlinePlayer.PlayerData.RestoreCharacter(onlinePlayer);
                }

                return new RestObject()
                {
                    { "response", "修改成功" }
                };
            }
            catch (Exception ex)
            {
                return new RestObject("500")
                {
                    { "error", ex.Message }
                };
            }
        }

        public static object GetInv(RestRequestArgs args)
        {
            string player = args.Parameters["player"];
            
            var onlinePlayers = TShockAPI.TSPlayer.FindByNameOrID(player);
            if (onlinePlayers.Count > 0)
            {
                var tsPlayer = onlinePlayers[0];
                List<InventoryData> invList = GetOnlinePlayerInventory(tsPlayer);
                
                return new RestObject()
                {
                    { "inventory", invList },
                    { "source", "memory" },
                    { "online", true }
                };
            }
            
            var account = TShock.UserAccounts.GetUserAccountByName(player);
            if (account == null)
            {
                return new RestObject("404")
                {
                    { "error", "找不到玩家" }
                };
            }
            
            List<InventoryData> offlineInvList = GetOfflinePlayerInventory(account.ID);
            if (offlineInvList == null)
            {
                return new RestObject("401")
                {
                    { "error", "获取背包数据失败" }
                };
            }
            
            return new RestObject()
            {
                { "inventory", offlineInvList },
                { "source", "database" },
                { "online", false }
            };
        }

        public static List<InventoryData> GetOnlinePlayerInventory(TSPlayer player)
        {
            List<InventoryData> invList = new List<InventoryData>();
            
            if (player?.TPlayer == null)
            {
                return invList;
            }
            
            var result = new Dictionary<int, InventoryData>();
            
            for (int i = 0; i < 50; i++)
            {
                var item = player.TPlayer.inventory[i];
                result[i] = new InventoryData(item.type, item.prefix, item.stack, i, item.favorited);
            }
            
            for (int i = 0; i < 4; i++)
            {
                var item = player.TPlayer.inventory[50 + i];
                result[50 + i] = new InventoryData(item.type, item.prefix, item.stack, 50 + i, item.favorited);
            }
            
            for (int i = 0; i < 4; i++)
            {
                var item = player.TPlayer.inventory[54 + i];
                result[54 + i] = new InventoryData(item.type, item.prefix, item.stack, 54 + i, item.favorited);
            }
            
            for (int i = 0; i < 10; i++)
            {
                var item = player.TPlayer.armor[i];
                result[59 + i] = new InventoryData(item.type, item.prefix, item.stack, 59 + i, item.favorited);
            }
            
            for (int i = 0; i < 10; i++)
            {
                var item = player.TPlayer.armor[10 + i];
                result[69 + i] = new InventoryData(item.type, item.prefix, item.stack, 69 + i, item.favorited);
            }
            
            for (int i = 0; i < 10; i++)
            {
                var item = player.TPlayer.dye[i];
                result[79 + i] = new InventoryData(item.type, item.prefix, item.stack, 79 + i, item.favorited);
            }
            
            for (int i = 0; i < NetItem.MiscEquipSlots; i++)
            {
                var item = player.TPlayer.miscEquips[i];
                result[89 + i] = new InventoryData(item.type, item.prefix, item.stack, 89 + i, item.favorited);
            }
            
            for (int i = 0; i < NetItem.MiscDyeSlots; i++)
            {
                var item = player.TPlayer.miscDyes[i];
                result[94 + i] = new InventoryData(item.type, item.prefix, item.stack, 94 + i, item.favorited);
            }
            
            for (int i = 0; i < NetItem.PiggySlots; i++)
            {
                var item = player.TPlayer.bank.item[i];
                result[99 + i] = new InventoryData(item.type, item.prefix, item.stack, 99 + i, item.favorited);
            }
            
            for (int i = 0; i < NetItem.SafeSlots; i++)
            {
                var item = player.TPlayer.bank2.item[i];
                result[139 + i] = new InventoryData(item.type, item.prefix, item.stack, 139 + i, item.favorited);
            }
            
            var trash = player.TPlayer.trashItem;
            result[179] = new InventoryData(trash.type, trash.prefix, trash.stack, 179, trash.favorited);
            
            for (int i = 0; i < NetItem.ForgeSlots; i++)
            {
                var item = player.TPlayer.bank3.item[i];
                result[180 + i] = new InventoryData(item.type, item.prefix, item.stack, 180 + i, item.favorited);
            }
            
            for (int i = 0; i < NetItem.VoidSlots; i++)
            {
                var item = player.TPlayer.bank4.item[i];
                result[220 + i] = new InventoryData(item.type, item.prefix, item.stack, 220 + i, item.favorited);
            }
            
            for (int i = 0; i < 10; i++)
            {
                var item = player.TPlayer.Loadouts[0].Armor[i];
                result[289 + i] = new InventoryData(item.type, item.prefix, item.stack, 289 + i, item.favorited);
            }
            
            for (int i = 0; i < 10; i++)
            {
                var item = player.TPlayer.Loadouts[0].Armor[10 + i];
                result[299 + i] = new InventoryData(item.type, item.prefix, item.stack, 299 + i, item.favorited);
            }
            
            for (int i = 0; i < NetItem.LoadoutDyeSlots; i++)
            {
                var item = player.TPlayer.Loadouts[0].Dye[i];
                result[309 + i] = new InventoryData(item.type, item.prefix, item.stack, 309 + i, item.favorited);
            }
            
            for (int i = 0; i < 10; i++)
            {
                var item = player.TPlayer.Loadouts[1].Armor[i];
                result[319 + i] = new InventoryData(item.type, item.prefix, item.stack, 319 + i, item.favorited);
            }
            
            for (int i = 0; i < 10; i++)
            {
                var item = player.TPlayer.Loadouts[1].Armor[10 + i];
                result[329 + i] = new InventoryData(item.type, item.prefix, item.stack, 329 + i, item.favorited);
            }
            
            for (int i = 0; i < NetItem.LoadoutDyeSlots; i++)
            {
                var item = player.TPlayer.Loadouts[1].Dye[i];
                result[339 + i] = new InventoryData(item.type, item.prefix, item.stack, 339 + i, item.favorited);
            }
            
            for (int i = 0; i < 10; i++)
            {
                var item = player.TPlayer.Loadouts[2].Armor[i];
                result[349 + i] = new InventoryData(item.type, item.prefix, item.stack, 349 + i, item.favorited);
            }
            
            for (int i = 0; i < 10; i++)
            {
                var item = player.TPlayer.Loadouts[2].Armor[10 + i];
                result[359 + i] = new InventoryData(item.type, item.prefix, item.stack, 359 + i, item.favorited);
            }
            
            for (int i = 0; i < NetItem.LoadoutDyeSlots; i++)
            {
                var item = player.TPlayer.Loadouts[2].Dye[i];
                result[369 + i] = new InventoryData(item.type, item.prefix, item.stack, 369 + i, item.favorited);
            }
            
            for (int i = 0; i <= 378; i++)
            {
                if (result.TryGetValue(i, out var data))
                {
                    invList.Add(data);
                }
                else
                {
                    invList.Add(new InventoryData(0, 0, 0, i, false));
                }
            }
            
            return invList;
        }

        public static List<InventoryData> GetOfflinePlayerInventory(int accountId)
        {
            var data = TShock.CharacterDB.GetPlayerData(null, accountId);
            if (data == null || data.inventory == null)
            {
                return null;
            }
            
            var result = new Dictionary<int, InventoryData>();
            
            for (int i = 0; i < 50; i++)
            {
                var item = data.inventory[i];
                result[i] = new InventoryData(item.NetId, item.PrefixId, item.Stack, i, item.Favorited);
            }
            
            for (int i = 0; i < 4; i++)
            {
                var item = data.inventory[50 + i];
                result[50 + i] = new InventoryData(item.NetId, item.PrefixId, item.Stack, 50 + i, item.Favorited);
            }
            
            for (int i = 0; i < 4; i++)
            {
                var item = data.inventory[54 + i];
                result[54 + i] = new InventoryData(item.NetId, item.PrefixId, item.Stack, 54 + i, item.Favorited);
            }
            
            for (int i = 0; i < 10; i++)
            {
                var item = data.inventory[59 + i];
                result[59 + i] = new InventoryData(item.NetId, item.PrefixId, item.Stack, 59 + i, item.Favorited);
            }
            
            for (int i = 0; i < 10; i++)
            {
                var item = data.inventory[69 + i];
                result[69 + i] = new InventoryData(item.NetId, item.PrefixId, item.Stack, 69 + i, item.Favorited);
            }
            
            for (int i = 0; i < 10; i++)
            {
                var item = data.inventory[79 + i];
                result[79 + i] = new InventoryData(item.NetId, item.PrefixId, item.Stack, 79 + i, item.Favorited);
            }
            
            for (int i = 0; i < NetItem.MiscEquipSlots; i++)
            {
                var item = data.inventory[89 + i];
                result[89 + i] = new InventoryData(item.NetId, item.PrefixId, item.Stack, 89 + i, item.Favorited);
            }
            
            for (int i = 0; i < NetItem.MiscDyeSlots; i++)
            {
                var item = data.inventory[94 + i];
                result[94 + i] = new InventoryData(item.NetId, item.PrefixId, item.Stack, 94 + i, item.Favorited);
            }
            
            for (int i = 0; i < NetItem.PiggySlots; i++)
            {
                var item = data.inventory[99 + i];
                result[99 + i] = new InventoryData(item.NetId, item.PrefixId, item.Stack, 99 + i, item.Favorited);
            }
            
            for (int i = 0; i < NetItem.SafeSlots; i++)
            {
                var item = data.inventory[139 + i];
                result[139 + i] = new InventoryData(item.NetId, item.PrefixId, item.Stack, 139 + i, item.Favorited);
            }
            
            var trashItem = data.inventory[179];
            result[179] = new InventoryData(trashItem.NetId, trashItem.PrefixId, trashItem.Stack, 179, trashItem.Favorited);
            
            for (int i = 0; i < NetItem.ForgeSlots; i++)
            {
                var item = data.inventory[180 + i];
                result[180 + i] = new InventoryData(item.NetId, item.PrefixId, item.Stack, 180 + i, item.Favorited);
            }
            
            for (int i = 0; i < NetItem.VoidSlots; i++)
            {
                var item = data.inventory[220 + i];
                result[220 + i] = new InventoryData(item.NetId, item.PrefixId, item.Stack, 220 + i, item.Favorited);
            }
            
            for (int i = 0; i < 10; i++)
            {
                var item = data.inventory[260 + i];
                result[289 + i] = new InventoryData(item.NetId, item.PrefixId, item.Stack, 289 + i, item.Favorited);
            }
            
            for (int i = 0; i < 10; i++)
            {
                var item = data.inventory[270 + i];
                result[299 + i] = new InventoryData(item.NetId, item.PrefixId, item.Stack, 299 + i, item.Favorited);
            }
            
            for (int i = 0; i < NetItem.LoadoutDyeSlots; i++)
            {
                var item = data.inventory[280 + i];
                result[309 + i] = new InventoryData(item.NetId, item.PrefixId, item.Stack, 309 + i, item.Favorited);
            }
            
            for (int i = 0; i < 10; i++)
            {
                var item = data.inventory[290 + i];
                result[319 + i] = new InventoryData(item.NetId, item.PrefixId, item.Stack, 319 + i, item.Favorited);
            }
            
            for (int i = 0; i < 10; i++)
            {
                var item = data.inventory[300 + i];
                result[329 + i] = new InventoryData(item.NetId, item.PrefixId, item.Stack, 329 + i, item.Favorited);
            }
            
            for (int i = 0; i < NetItem.LoadoutDyeSlots; i++)
            {
                var item = data.inventory[310 + i];
                result[339 + i] = new InventoryData(item.NetId, item.PrefixId, item.Stack, 339 + i, item.Favorited);
            }
            
            for (int i = 0; i < 10; i++)
            {
                var item = data.inventory[320 + i];
                result[349 + i] = new InventoryData(item.NetId, item.PrefixId, item.Stack, 349 + i, item.Favorited);
            }
            
            for (int i = 0; i < 10; i++)
            {
                var item = data.inventory[330 + i];
                result[359 + i] = new InventoryData(item.NetId, item.PrefixId, item.Stack, 359 + i, item.Favorited);
            }
            
            for (int i = 0; i < NetItem.LoadoutDyeSlots; i++)
            {
                var item = data.inventory[340 + i];
                result[369 + i] = new InventoryData(item.NetId, item.PrefixId, item.Stack, 369 + i, item.Favorited);
            }
            
            List<InventoryData> offlineInvList = new List<InventoryData>();
            for (int i = 0; i <= 378; i++)
            {
                if (result.TryGetValue(i, out var invData))
                {
                    offlineInvList.Add(invData);
                }
                else
                {
                    offlineInvList.Add(new InventoryData(0, 0, 0, i, false));
                }
            }
            
            return offlineInvList;
        }

        public static object BatchEdit(RestRequestArgs args)
        {
            string playerName = args.Parameters["player"];
            string statsJson = args.Parameters["stats"];
            string invCompact = args.Parameters["inv"] ?? "";

            TShock.Log.ConsoleInfo($"[BatchEdit] player={playerName}, stats长度={statsJson?.Length ?? 0}, inv长度={invCompact.Length}");

            var account = TShock.UserAccounts.GetUserAccountByName(playerName);
            if (account == null)
            {
                // 尝试用 ID 查找
                if (int.TryParse(playerName, out int accountId))
                {
                    account = TShock.UserAccounts.GetUserAccountByID(accountId);
                }
                if (account == null)
                {
                    return new RestObject("404")
                    {
                        { "error", $"找不到玩家: {playerName}" },
                        { "debug_player", playerName }
                    };
                }
            }

            // 在线则先同步内存→DB
            var onlinePlayers = TSPlayer.FindByNameOrID(playerName);
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

                // === 导入属性 ===
                if (!string.IsNullOrEmpty(statsJson))
                {
                    JObject stats;
                    try
                    {
                        stats = JObject.Parse(statsJson);
                    }
                    catch (Exception ex)
                    {
                        TShock.Log.ConsoleError($"[BatchEdit] stats JSON 解析失败: {ex.Message}");
                        return new RestObject("400")
                        {
                            { "error", $"stats JSON 解析失败: {ex.Message}" }
                        };
                    }

                    if (stats.HasValues)
                    {
                        var charData = TShock.CharacterDB.GetPlayerData(null, account.ID);
                        if (charData != null && charData.exists)
                        {
                            List<string> updateFields = new List<string>();
                            List<object> updateValues = new List<object>();
                            int paramIndex = 0;

                            void tryInt(string key, ref int field, string dbCol)
                            {
                                JToken? t = stats[key];
                                if (t != null && t.Type == JTokenType.Integer)
                                {
                                    field = t.Value<int>();
                                    updateFields.Add(dbCol + " = @" + paramIndex);
                                    updateValues.Add(field);
                                    paramIndex++;
                                }
                            }
                            void tryBool(string key, ref int? field, string dbCol)
                            {
                                JToken? t = stats[key];
                                if (t != null && t.Type == JTokenType.Boolean)
                                {
                                    field = t.Value<bool>() ? 1 : 0;
                                    updateFields.Add(dbCol + " = @" + paramIndex);
                                    updateValues.Add(field);
                                    paramIndex++;
                                }
                            }
                            void tryBoolInt(string key, ref int field, string dbCol)
                            {
                                JToken? t = stats[key];
                                if (t != null && t.Type == JTokenType.Boolean)
                                {
                                    field = t.Value<bool>() ? 1 : 0;
                                    updateFields.Add(dbCol + " = @" + paramIndex);
                                    updateValues.Add(field);
                                    paramIndex++;
                                }
                            }

                            tryInt("maxHealth", ref charData.maxHealth, "MaxHealth");
                            tryInt("maxMana", ref charData.maxMana, "MaxMana");
                            tryInt("health", ref charData.health, "Health");
                            tryInt("mana", ref charData.mana, "Mana");
                            tryInt("questsCompleted", ref charData.questsCompleted, "QuestsCompleted");

                            tryBool("extraSlot", ref charData.extraSlot, "ExtraSlot");
                            tryBoolInt("unlockedBiomeTorches", ref charData.unlockedBiomeTorches, "UnlockedBiomeTorches");
                            tryBoolInt("ateArtisanBread", ref charData.ateArtisanBread, "AteArtisanBread");
                            tryBoolInt("usedAegisCrystal", ref charData.usedAegisCrystal, "UsedAegisCrystal");
                            tryBoolInt("usedAegisFruit", ref charData.usedAegisFruit, "UsedAegisFruit");
                            tryBoolInt("usedArcaneCrystal", ref charData.usedArcaneCrystal, "UsedArcaneCrystal");
                            tryBoolInt("usedGalaxyPearl", ref charData.usedGalaxyPearl, "UsedGalaxyPearl");
                            tryBoolInt("usedGummyWorm", ref charData.usedGummyWorm, "UsedGummyWorm");
                            tryBoolInt("usedAmbrosia", ref charData.usedAmbrosia, "UsedAmbrosia");
                            tryBoolInt("unlockedSuperCart", ref charData.unlockedSuperCart, "UnlockedSuperCart");
                            tryBoolInt("enabledSuperCart", ref charData.enabledSuperCart, "EnabledSuperCart");

                            if (updateFields.Count > 0)
                            {
                                string sql = "UPDATE tsCharacter SET " + string.Join(", ", updateFields) + " WHERE Account = @" + paramIndex;
                                updateValues.Add(account.ID);
                                db.Query(sql, updateValues.ToArray());
                                TShock.Log.ConsoleInfo($"[BatchEdit] 属性更新 {updateFields.Count} 个字段");
                            }
                        }
                    }
                }

                // === 导入背包 ===
                bool hasInventoryWork = false;
                string strinventory = "";
                using (QueryResult res = db.QueryReader("SELECT Inventory FROM tsCharacter WHERE Account = @0", account.ID))
                {
                    if (res.Read())
                        strinventory = res.Get<string>("Inventory");
                }

                if (!string.IsNullOrEmpty(strinventory))
                {
                    string[] arr = strinventory.Split('~');
                    int changedCount = 0;

                    // 如果指定了 clear，先清空所有格子（无论是否有导入物品）
                    string? clearParam = args.Parameters["clear"];
                    if (clearParam == "1")
                    {
                        for (int i = 0; i < arr.Length; i++)
                        {
                            string[] slotParts = arr[i].Split(',');
                            slotParts[0] = "0";
                            slotParts[1] = "0";
                            slotParts[2] = "0";
                            arr[i] = string.Join(",", slotParts);
                        }
                        changedCount++;
                        hasInventoryWork = true;
                    }

                    // 写入导入的物品
                    if (!string.IsNullOrEmpty(invCompact))
                    {
                        string[] items = invCompact.Split('|', StringSplitOptions.RemoveEmptyEntries);
                        foreach (string itemStr in items)
                        {
                            string[] parts = itemStr.Split(',');
                            if (parts.Length < 4) continue;

                            if (!int.TryParse(parts[0], out int slot)) continue;
                            if (!int.TryParse(parts[1], out int netId)) continue;
                            if (!int.TryParse(parts[2], out int stack)) continue;
                            if (!int.TryParse(parts[3], out int prefix)) continue;

                            if (slot < 0 || slot >= arr.Length || netId <= 0)
                                continue;

                            string[] slotParts = arr[slot].Split(',');
                            slotParts[0] = netId.ToString();
                            slotParts[1] = stack.ToString();
                            slotParts[2] = prefix.ToString();
                            arr[slot] = string.Join(",", slotParts);
                            changedCount++;
                        }
                        hasInventoryWork = true;
                    }

                    if (changedCount > 0)
                    {
                        db.Query("UPDATE tsCharacter SET Inventory = @0 WHERE Account = @1", string.Join("~", arr), account.ID);
                        TShock.Log.ConsoleInfo($"[BatchEdit] 背包更新 {changedCount} 个格子");
                    }
                }

                // 在线 → 从 DB 重新加载并同步客户端
                if (onlinePlayer != null)
                {
                    onlinePlayer.PlayerData = TShock.CharacterDB.GetPlayerData(onlinePlayer, account.ID);
                    onlinePlayer.PlayerData.RestoreCharacter(onlinePlayer);
                }

                return new RestObject()
                {
                    { "response", "批量导入成功" }
                };
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[BatchEdit] 异常: {ex}");
                return new RestObject("500")
                {
                    { "error", ex.Message }
                };
            }
        }
    }
}