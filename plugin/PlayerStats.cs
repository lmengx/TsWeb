using Rests;
using System;
using System.Collections.Generic;
using Terraria;
using TShockAPI;
using TShockAPI.DB;

namespace TShockData
{
    public class PlayerStatData
    {
        public int health { get; set; }
        public int maxHealth { get; set; }
        public int mana { get; set; }
        public int maxMana { get; set; }
        public int questsCompleted { get; set; }

        public bool extraSlot { get; set; }
        public bool unlockedBiomeTorches { get; set; }
        public bool ateArtisanBread { get; set; }
        public bool usedAegisCrystal { get; set; }
        public bool usedAegisFruit { get; set; }
        public bool usedArcaneCrystal { get; set; }
        public bool usedGalaxyPearl { get; set; }
        public bool usedGummyWorm { get; set; }
        public bool usedAmbrosia { get; set; }
        public bool unlockedSuperCart { get; set; }
        public bool enabledSuperCart { get; set; }
    }

    public class PlayerStats
    {
        public static object GetPlayerStats(RestRequestArgs args)
        {
            string playerName = args.Parameters["player"];

            var onlinePlayers = TSPlayer.FindByNameOrID(playerName);
            if (onlinePlayers.Count > 0)
            {
                var tsPlayer = onlinePlayers[0];
                var stats = GetOnlinePlayerStats(tsPlayer);
                return new RestObject()
                {
                    { "data", stats },
                    { "source", "memory" },
                    { "online", true }
                };
            }

            var account = TShock.UserAccounts.GetUserAccountByName(playerName);
            if (account == null)
            {
                return new RestObject("404")
                {
                    { "error", "找不到玩家" }
                };
            }

            var data = TShock.CharacterDB.GetPlayerData(null, account.ID);
            if (data == null)
            {
                return new RestObject("404")
                {
                    { "error", "找不到角色数据" }
                };
            }

            var offlineStats = GetOfflinePlayerStats(data);
            return new RestObject()
            {
                { "data", offlineStats },
                { "source", "database" },
                { "online", false }
            };
        }

        public static object SetPlayerStats(RestRequestArgs args)
        {
            string playerName = args.Parameters["player"];

            var account = TShock.UserAccounts.GetUserAccountByName(playerName);
            if (account == null)
            {
                return new RestObject("404")
                {
                    { "error", "找不到玩家" }
                };
            }

            var onlinePlayers = TSPlayer.FindByNameOrID(playerName);
            if (onlinePlayers.Count > 0)
            {
                foreach (var tsPlayer in onlinePlayers)
                {
                    tsPlayer.Kick("管理员修改了您的角色属性", true);
                }
                System.Threading.Thread.Sleep(1000);
            }

            try
            {
                var data = TShock.CharacterDB.GetPlayerData(null, account.ID);
                if (data == null)
                {
                    return new RestObject("404")
                    {
                        { "error", "找不到角色数据" }
                    };
                }

                List<string> updateFields = new List<string>();
                List<object> updateValues = new List<object>();
                int paramIndex = 0;

                TryUpdateInt(args, "maxHealth", ref data.maxHealth, updateFields, updateValues, "MaxHealth", ref paramIndex);
                TryUpdateInt(args, "maxMana", ref data.maxMana, updateFields, updateValues, "MaxMana", ref paramIndex);
                TryUpdateInt(args, "health", ref data.health, updateFields, updateValues, "Health", ref paramIndex);
                TryUpdateInt(args, "mana", ref data.mana, updateFields, updateValues, "Mana", ref paramIndex);
                TryUpdateInt(args, "questsCompleted", ref data.questsCompleted, updateFields, updateValues, "QuestsCompleted", ref paramIndex);

                TryUpdateBoolNullable(args, "extraSlot", ref data.extraSlot, updateFields, updateValues, "ExtraSlot", ref paramIndex);
                TryUpdateBool(args, "unlockedBiomeTorches", ref data.unlockedBiomeTorches, updateFields, updateValues, "UnlockedBiomeTorches", ref paramIndex);
                TryUpdateBool(args, "ateArtisanBread", ref data.ateArtisanBread, updateFields, updateValues, "AteArtisanBread", ref paramIndex);
                TryUpdateBool(args, "usedAegisCrystal", ref data.usedAegisCrystal, updateFields, updateValues, "UsedAegisCrystal", ref paramIndex);
                TryUpdateBool(args, "usedAegisFruit", ref data.usedAegisFruit, updateFields, updateValues, "UsedAegisFruit", ref paramIndex);
                TryUpdateBool(args, "usedArcaneCrystal", ref data.usedArcaneCrystal, updateFields, updateValues, "UsedArcaneCrystal", ref paramIndex);
                TryUpdateBool(args, "usedGalaxyPearl", ref data.usedGalaxyPearl, updateFields, updateValues, "UsedGalaxyPearl", ref paramIndex);
                TryUpdateBool(args, "usedGummyWorm", ref data.usedGummyWorm, updateFields, updateValues, "UsedGummyWorm", ref paramIndex);
                TryUpdateBool(args, "usedAmbrosia", ref data.usedAmbrosia, updateFields, updateValues, "UsedAmbrosia", ref paramIndex);
                TryUpdateBool(args, "unlockedSuperCart", ref data.unlockedSuperCart, updateFields, updateValues, "UnlockedSuperCart", ref paramIndex);
                TryUpdateBool(args, "enabledSuperCart", ref data.enabledSuperCart, updateFields, updateValues, "EnabledSuperCart", ref paramIndex);

                if (updateFields.Count > 0)
                {
                    string updateSql = "UPDATE tsCharacter SET " + string.Join(", ", updateFields) + " WHERE Account = @" + paramIndex;
                    updateValues.Add(account.ID);
                    TShock.DB.Query(updateSql, updateValues.ToArray());
                }

                return new RestObject()
                {
                    { "response", "修改成功" },
                    { "data", GetOfflinePlayerStats(data) }
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

        private static void TryUpdateInt(RestRequestArgs args, string paramName, ref int field, List<string> updateFields, List<object> updateValues, string dbFieldName, ref int paramIndex)
        {
            try
            {
                string value = args.Parameters[paramName];
                field = int.Parse(value);
                updateFields.Add(dbFieldName + " = @" + paramIndex);
                updateValues.Add(field);
                paramIndex++;
            }
            catch { }
        }

        private static void TryUpdateBool(RestRequestArgs args, string paramName, ref int field, List<string> updateFields, List<object> updateValues, string dbFieldName, ref int paramIndex)
        {
            try
            {
                string value = args.Parameters[paramName];
                field = bool.Parse(value) ? 1 : 0;
                updateFields.Add(dbFieldName + " = @" + paramIndex);
                updateValues.Add(field);
                paramIndex++;
            }
            catch { }
        }

        private static void TryUpdateBoolNullable(RestRequestArgs args, string paramName, ref int? field, List<string> updateFields, List<object> updateValues, string dbFieldName, ref int paramIndex)
        {
            try
            {
                string value = args.Parameters[paramName];
                field = bool.Parse(value) ? 1 : 0;
                updateFields.Add(dbFieldName + " = @" + paramIndex);
                updateValues.Add(field);
                paramIndex++;
            }
            catch { }
        }

        private static PlayerStatData GetOnlinePlayerStats(TSPlayer tsPlayer)
        {
            var player = tsPlayer.TPlayer;
            return new PlayerStatData
            {
                health = player.statLife,
                maxHealth = player.statLifeMax,
                mana = player.statMana,
                maxMana = player.statManaMax,
                questsCompleted = player.anglerQuestsFinished,
                extraSlot = player.extraAccessory,
                unlockedBiomeTorches = player.unlockedBiomeTorches,
                ateArtisanBread = player.ateArtisanBread,
                usedAegisCrystal = player.usedAegisCrystal,
                usedAegisFruit = player.usedAegisFruit,
                usedArcaneCrystal = player.usedArcaneCrystal,
                usedGalaxyPearl = player.usedGalaxyPearl,
                usedGummyWorm = player.usedGummyWorm,
                usedAmbrosia = player.usedAmbrosia,
                unlockedSuperCart = player.unlockedSuperCart,
                enabledSuperCart = player.enabledSuperCart
            };
        }

        private static PlayerStatData GetOfflinePlayerStats(PlayerData data)
        {
            return new PlayerStatData
            {
                health = data.health,
                maxHealth = data.maxHealth,
                mana = data.mana,
                maxMana = data.maxMana,
                questsCompleted = data.questsCompleted,
                extraSlot = data.extraSlot == 1,
                unlockedBiomeTorches = data.unlockedBiomeTorches == 1,
                ateArtisanBread = data.ateArtisanBread == 1,
                usedAegisCrystal = data.usedAegisCrystal == 1,
                usedAegisFruit = data.usedAegisFruit == 1,
                usedArcaneCrystal = data.usedArcaneCrystal == 1,
                usedGalaxyPearl = data.usedGalaxyPearl == 1,
                usedGummyWorm = data.usedGummyWorm == 1,
                usedAmbrosia = data.usedAmbrosia == 1,
                unlockedSuperCart = data.unlockedSuperCart == 1,
                enabledSuperCart = data.enabledSuperCart == 1
            };
        }
    }
}