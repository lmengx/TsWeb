using Rests;
using System;
using Newtonsoft.Json;
using TShockAPI;

namespace TShockData
{
    public class ItemConfigHandler
    {
        public static void LoadItemConfig()
        {
            AntiCheat.LoadConfig();
        }

        public static AntiCheatConfig GetItemConfig()
        {
            return AntiCheat.GetConfig();
        }

        public static bool SaveItemConfig(AntiCheatConfig config)
        {
            try
            {
                AntiCheat.SaveConfig(config);
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

                var incoming = JsonConvert.DeserializeObject<AntiCheatConfig>(json);
                if (incoming == null)
                {
                    return new { status = 400, error = "Invalid config format" };
                }

                // 合并前端未传递的字段，防止自动扫描等配置丢失
                var existing = AntiCheat.GetConfig();
                if (existing != null)
                {
                    incoming.AutoScan = existing.AutoScan;
                    incoming.AutoScanInterval = existing.AutoScanInterval;
                }

                bool success = SaveItemConfig(incoming);
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