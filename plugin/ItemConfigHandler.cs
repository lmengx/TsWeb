using Rests;
using System;
using System.IO;
using Newtonsoft.Json;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;

namespace TShockData
{
    public class ItemRestrictionConfig
    {
        [JsonProperty("启用")]
        public bool Enabled { get; set; } = true;

        [JsonProperty("限制列表")]
        public System.Collections.Generic.List<ItemRestriction> Restrictions { get; set; } = new System.Collections.Generic.List<ItemRestriction>();
    }

    public class ItemRestriction
    {
        [JsonProperty("进度")]
        public string Progress { get; set; } = "始终生效";

        [JsonProperty("限制物品")]
        public System.Collections.Generic.List<RestrictedItem> Items { get; set; } = new System.Collections.Generic.List<RestrictedItem>();
    }

    public class ItemConfigHandler
    {
        private static ItemRestrictionConfig _itemConfig;
        private static string ItemConfigPath => Path.Combine(TShock.SavePath, "TSWeb", "AntiCheat", "物品违禁.json");

        public static void LoadItemConfig()
        {
            try
            {
                var directory = Path.GetDirectoryName(ItemConfigPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (!File.Exists(ItemConfigPath))
                {
                    var defaultConfig = new ItemRestrictionConfig
                    {
                        Enabled = true,
                        Restrictions = new System.Collections.Generic.List<ItemRestriction>
                        {
                            new ItemRestriction
                            {
                                Progress = "始终生效",
                                Items = new System.Collections.Generic.List<RestrictedItem>()
                            },
                            new ItemRestriction
                            {
                                Progress = "月亮领主",
                                Items = new System.Collections.Generic.List<RestrictedItem>
                                {
                                    new RestrictedItem { ID = 4956, Stack = 1, Method = "ban" }
                                }
                            }
                        }
                    };
                    string json = JsonConvert.SerializeObject(defaultConfig, Formatting.Indented);
                    File.WriteAllText(ItemConfigPath, json);
                    _itemConfig = defaultConfig;
                }
                else
                {
                    string json = File.ReadAllText(ItemConfigPath);
                    _itemConfig = JsonConvert.DeserializeObject<ItemRestrictionConfig>(json) ?? new ItemRestrictionConfig();
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 加载物品违禁配置失败: {ex.Message}");
                _itemConfig = new ItemRestrictionConfig();
            }
        }

        public static ItemRestrictionConfig GetItemConfig()
        {
            return _itemConfig;
        }

        public static bool SaveItemConfig(ItemRestrictionConfig config)
        {
            try
            {
                var directory = Path.GetDirectoryName(ItemConfigPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(ItemConfigPath, json);
                _itemConfig = config;
                ItemDetection.RefreshRestrictedItems();
                return true;
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 保存物品违禁配置失败: {ex.Message}");
                return false;
            }
        }

        public static object GetItemConfigApi(RestRequestArgs args)
        {
            try
            {
                var config = GetItemConfig();
                if (config != null)
                {
                    return new { status = 200, config = config };
                }
                return new { status = 500, error = "Failed to load config" };
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[ItemConfig] GetItemConfigApi error: {ex.Message}");
                return new { status = 500, error = ex.Message };
            }
        }

        public static object SaveItemConfigApi(RestRequestArgs args)
        {
            try
            {
                string json = args.Parameters["config"];
                if (string.IsNullOrEmpty(json))
                {
                    return new { status = 400, error = "Missing config parameter" };
                }

                var config = JsonConvert.DeserializeObject<ItemRestrictionConfig>(json);
                if (config == null)
                {
                    return new { status = 400, error = "Invalid config format" };
                }

                bool success = SaveItemConfig(config);
                if (success)
                {
                    return new { status = 200, message = "Config saved successfully" };
                }
                return new { status = 500, error = "Failed to save config" };
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[ItemConfig] SaveItemConfigApi error: {ex.Message}");
                return new { status = 500, error = ex.Message };
            }
        }

        public static object ScanAllItemsApi(RestRequestArgs args)
        {
            try
            {
                var results = ItemDetection.ScanAllPlayers();
                
                var playerGroups = results.GroupBy(r => r.PlayerName);
                
                var players = new System.Collections.Generic.List<object>();
                foreach (var group in playerGroups)
                {
                    var items = new System.Collections.Generic.List<object>();
                    foreach (var result in group)
                    {
                        items.Add(new
                        {
                            id = result.ItemID,
                            stack = result.FoundStack,
                            itemName = result.ItemName,
                            allowedStack = result.AllowedStack,
                            method = result.Method,
                            slot = result.Slot
                        });
                    }
                    
                    players.Add(new
                    {
                        name = group.Key,
                        items = items
                    });
                }

                return new { status = 200, players = players, count = players.Count };
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[ItemConfig] ScanAllItemsApi error: {ex.Message}");
                return new { status = 500, error = ex.Message };
            }
        }

        public static object ScanItemByIdApi(RestRequestArgs args)
        {
            try
            {
                int itemId = 0;
                var rawId = args.Parameters["itemId"];
                if (!string.IsNullOrEmpty(rawId))
                {
                    int.TryParse(rawId, out itemId);
                }

                if (itemId <= 0)
                {
                    return new { status = 400, error = "缺少有效的 itemId 参数" };
                }

                // 复用 tools.FindPlayersWithItem 的数据库扫描逻辑
                var playerNames = tools.FindPlayersWithItem(itemId);

                var players = new System.Collections.Generic.List<object>();
                foreach (var name in playerNames)
                {
                    players.Add(new
                    {
                        name = name,
                        itemId = itemId,
                        itemName = AntiCheat.GetItemName(itemId)
                    });
                }

                return new { status = 200, players = players, count = players.Count };
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[ItemConfig] ScanItemByIdApi error: {ex.Message}");
                return new { status = 500, error = ex.Message };
            }
        }
    }
}