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

                bool success = SaveItemConfig(incoming);
                if (success)
                {
                    // 重启自动扫描计时器，使新间隔立即生效
                    ItemDetection.StopAutoScan();
                    ItemDetection.StartAutoScan();

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
                // 只扫描在线玩家，命中违禁规则自动执行违规处理
                var report = ItemDetection.ScanAllPlayers();

                var playerGroups = report.Results.GroupBy(r => r.PlayerName);

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

                return new
                {
                    status = 200,
                    players = players,
                    count = players.Count,
                    scannedPlayers = report.ScannedPlayers,
                    violationCount = report.ViolationCount,
                    durationMs = report.DurationMs
                };
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

                // 只查询在线玩家中持有该物品的玩家（纯查询，不判定违禁、不执行违规处理）
                var report = ItemDetection.ScanOnlinePlayersByItem(itemId);

                var players = new System.Collections.Generic.List<object>();
                foreach (var result in report.Results)
                {
                    players.Add(new
                    {
                        name = result.PlayerName,
                        itemId = result.ItemID,
                        itemName = result.ItemName,
                        stack = result.FoundStack,
                        allowedStack = result.AllowedStack,
                        method = result.Method
                    });
                }

                return new
                {
                    status = 200,
                    players = players,
                    count = players.Count,
                    scannedPlayers = report.ScannedPlayers,
                    durationMs = report.DurationMs
                };
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[ItemConfig] ScanItemByIdApi error: {ex.Message}");
                return new { status = 500, error = ex.Message };
            }
        }
    }
}