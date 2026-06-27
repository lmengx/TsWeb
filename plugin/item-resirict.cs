using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Newtonsoft.Json;
using Terraria;
using TShockAPI;
using TShockAPI.DB;

namespace TShockData
{
    /// <summary>
    /// 物品限制数据类
    /// </summary>
    public class RestrictedItem
    {
        [JsonProperty("id")]
        public int ID { get; set; }

        [JsonProperty("stack")]
        public int Stack { get; set; } = 1;

        [JsonProperty("method")]
        public string Method { get; set; } = "log";
    }

    /// <summary>
    /// 扫描结果数据类
    /// </summary>
    public class CheatResult
    {
        public string PlayerName { get; set; }
        public int PlayerID { get; set; }
        public int ItemID { get; set; }
        public string ItemName { get; set; }
        public int FoundStack { get; set; }
        public int AllowedStack { get; set; }
        public string Progress { get; set; }
        public string Method { get; set; }
        public int Slot { get; set; }
    }

    /// <summary>
    /// 物品检测核心：背包扫描 + 丢出检测
    /// </summary>
    public static class ItemDetection
    {
        private static List<RestrictedItem> _currentRestrictedItems = new List<RestrictedItem>();
        private static System.Timers.Timer? _autoScanTimer;

        public static void Initialize()
        {
            RefreshRestrictedItems();
            TShock.Log.ConsoleInfo($"[ItemDetection] 物品检测已初始化，违禁物品数: {_currentRestrictedItems.Count}");
            StartAutoScan();
        }

        public static void StartAutoScan()
        {
            if (_autoScanTimer != null)
            {
                _autoScanTimer.Stop();
                _autoScanTimer.Dispose();
            }

            var config = AntiCheat.GetConfig();
            if (config == null || !config.AutoScan)
            {
                TShock.Log.ConsoleInfo("[ItemDetection] 自动扫描已禁用");
                return;
            }

            int interval = config.AutoScanInterval * 1000;
            _autoScanTimer = new System.Timers.Timer(interval);
            _autoScanTimer.Elapsed += AutoScanCallback;
            _autoScanTimer.AutoReset = true;
            _autoScanTimer.Start();

            TShock.Log.ConsoleInfo($"[ItemDetection] 自动扫描已启动，间隔: {config.AutoScanInterval}秒");
        }

        private static void AutoScanCallback(object? sender, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                int totalViolations = 0;
                foreach (var player in TShock.Players)
                {
                    if (player == null || !player.Active || !player.IsLoggedIn)
                        continue;

                    var results = ScanOnlinePlayer(player);
                    totalViolations += results.Count;
                }

                if (totalViolations > 0)
                {
                    TShock.Log.ConsoleInfo($"[ItemDetection] 自动扫描完成，共检测到 {totalViolations} 条违规");
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[ItemDetection] 自动扫描出错: {ex.Message}");
            }
        }

        public static void StopAutoScan()
        {
            if (_autoScanTimer != null)
            {
                _autoScanTimer.Stop();
                _autoScanTimer.Dispose();
                _autoScanTimer = null;
                TShock.Log.ConsoleInfo("[ItemDetection] 自动扫描已停止");
            }
        }

        public static void RefreshRestrictedItems()
        {
            _currentRestrictedItems.Clear();

            var config = ItemConfigHandler.GetItemConfig();
            if (config?.Restrictions == null)
                return;

            foreach (var restriction in config.Restrictions)
            {
                bool progressMet = BossProgress.GetWorldStatus(restriction.Progress);

                if (progressMet)
                {
                    foreach (var item in restriction.Items)
                    {
                        item.Method = item.Method?.ToLower() ?? "log";
                        _currentRestrictedItems.Add(item);
                    }
                }
            }
        }

        public static void ShowRestrictedList(CommandArgs args)
        {
            args.Player.SendInfoMessage("=== 当前生效的违禁物品列表 ===");
            args.Player.SendInfoMessage($"总数: {_currentRestrictedItems.Count}");

            foreach (var item in _currentRestrictedItems)
            {
                string methodDesc = item.Method switch
                {
                    "ban" => "封禁",
                    "kick" => "踢出",
                    "log" => "记录",
                    _ => $"命令: {item.Method}"
                };

                args.Player.SendInfoMessage($"物品ID: {item.ID}({AntiCheat.GetItemName(item.ID)}), 限制数量: {item.Stack}, 处理方式: {methodDesc}");
            }

            if (_currentRestrictedItems.Count == 0)
            {
                args.Player.SendInfoMessage("当前没有生效的违禁物品");
            }
        }

        public static List<RestrictedItem> CheckItem(TSPlayer player, int itemId, int stack)
        {
            List<RestrictedItem> matchedItems = new List<RestrictedItem>();

            var config = AntiCheat.GetConfig();
            if (config == null || !config.Enabled)
                return matchedItems;

            if (player != null && player.Active)
            {
                if (player.HasPermission("tshock.admin") || player.HasPermission("tshock.item.usebanned"))
                    return matchedItems;
            }

            RefreshRestrictedItems();

            var candidates = _currentRestrictedItems.Where(r => r.ID == itemId).ToList();
            foreach (var candidate in candidates)
            {
                if (stack >= candidate.Stack)
                {
                    matchedItems.Add(candidate);
                }
            }

            if (matchedItems.Count > 0)
            {
                RefreshRestrictedItems();

                List<RestrictedItem> recheckedItems = new List<RestrictedItem>();
                foreach (var originalItem in matchedItems)
                {
                    var rechecked = _currentRestrictedItems.FirstOrDefault(r => r.ID == originalItem.ID);
                    if (rechecked != null && stack >= rechecked.Stack)
                    {
                        recheckedItems.Add(rechecked);
                    }
                }
                matchedItems = recheckedItems;
            }

            return matchedItems;
        }

        public static List<CheatResult> ScanOnlinePlayer(TSPlayer player)
        {
            List<CheatResult> results = new List<CheatResult>();

            if (player == null || !player.Active)
                return results;

            var config = AntiCheat.GetConfig();
            if (config == null || !config.Enabled)
                return results;

            bool hasPermission = player.HasPermission("tshock.admin") || player.HasPermission("tshock.item.usebanned");

            List<InventoryData> inventory = GetPlayerInv.GetOnlinePlayerInventory(player);
            if (inventory == null || inventory.Count == 0)
                return results;

            foreach (var item in inventory)
            {
                if (item.netID <= 0) continue;

                var matchedItems = CheckItem(player, item.netID, item.stack);
                if (matchedItems.Count > 0)
                {
                    foreach (var matchedItem in matchedItems)
                    {
                        if (hasPermission)
                        {
                            TShock.Log.ConsoleError($"[ItemDetection] [豁免] 玩家: {player.Name} 持有违禁物品但有权限豁免 - 物品ID: {item.netID}, 数量: {item.stack}, 限制: {matchedItem.Stack}");
                        }
                        else
                        {
                            TShock.Log.ConsoleError($"[ItemDetection] 检测到违禁物品! 玩家: {player.Name}, 物品ID: {item.netID}, 数量: {item.stack}, 限制: {matchedItem.Stack}, 处理方式: {matchedItem.Method}");
                        }

                        results.Add(new CheatResult
                        {
                            PlayerName = player.Name,
                            PlayerID = player.Account?.ID ?? 0,
                            ItemID = item.netID,
                            ItemName = AntiCheat.GetItemName(item.netID),
                            FoundStack = item.stack,
                            AllowedStack = matchedItem.Stack,
                            Progress = "当前进度",
                            Method = matchedItem.Method,
                            Slot = item.slot
                        });

                        if (!hasPermission)
                        {
                            ViolationExecutor.ExecuteViolation(player, matchedItem.Method, itemId: item.netID, itemName: AntiCheat.GetItemName(item.netID));
                        }
                    }
                }
            }

            return results;
        }

        public static List<CheatResult> ScanOfflinePlayer(int accountId, string playerName, bool executeViolations = true)
        {
            List<CheatResult> results = new List<CheatResult>();

            var config = AntiCheat.GetConfig();
            if (config == null || !config.Enabled)
                return results;

            List<InventoryData> inventory = GetPlayerInv.GetOfflinePlayerInventory(accountId);
            if (inventory == null)
                return results;

            foreach (var item in inventory)
            {
                if (item.netID <= 0) continue;

                var matchedItems = CheckItem(null, item.netID, item.stack);
                if (matchedItems.Count > 0)
                {
                    foreach (var matchedItem in matchedItems)
                    {
                        TShock.Log.ConsoleError($"[ItemDetection] 检测到违禁物品! 玩家: {playerName}, 物品ID: {item.netID}, 数量: {item.stack}, 限制: {matchedItem.Stack}, 处理方式: {matchedItem.Method}");

                        if (executeViolations)
                        {
                            System.Threading.Tasks.Task.Run(() =>
                            {
                                System.Threading.Thread.Sleep(new Random().Next(200, 500));
                                ViolationExecutor.ExecuteViolation(playerName, matchedItem.Method, itemId: item.netID, itemName: AntiCheat.GetItemName(item.netID));
                            });
                        }

                        results.Add(new CheatResult
                        {
                            PlayerName = playerName,
                            PlayerID = accountId,
                            ItemID = item.netID,
                            ItemName = AntiCheat.GetItemName(item.netID),
                            FoundStack = item.stack,
                            AllowedStack = matchedItem.Stack,
                            Progress = "当前进度",
                            Method = matchedItem.Method,
                            Slot = item.slot
                        });
                    }
                }
            }

            return results;
        }

        public static List<CheatResult> ScanAllPlayers()
        {
            List<CheatResult> allResults = new List<CheatResult>();

            foreach (var player in TShock.Players)
            {
                if (player == null || !player.Active || !player.IsLoggedIn)
                    continue;

                var results = ScanOnlinePlayer(player);
                allResults.AddRange(results);
            }

            IDbConnection db = TShock.DB;
            string query = "SELECT ID, Username FROM Users";
            using (QueryResult res = db.QueryReader(query))
            {
                while (res.Read())
                {
                    int userId = res.Get<int>("ID");
                    string username = res.Get<string>("Username");

                    bool isOnline = TShock.Players.Any(p => p != null && p.Active && p.Account != null && p.Account.ID == userId);
                    if (isOnline) continue;

                    var results = ScanOfflinePlayer(userId, username);
                    allResults.AddRange(results);
                }
            }

            return allResults;
        }
    }

    /// <summary>
    /// 物品限制功能：丢出物品拦截
    /// </summary>
    public static class ItemRestrict
    {
        private static bool _initialized = false;

        public static void Initialize()
        {
            if (_initialized) return;
            GetDataHandlers.ItemDrop.Register(OnItemDrop);
            GetDataHandlers.ChestItemChange.Register(OnChestItemChange);
            _initialized = true;
            TShock.Log.ConsoleInfo("[TSWeb] 物品限制功能已启用 (丢出+存箱检测)");
        }

        public static void Dispose()
        {
            if (!_initialized) return;
            GetDataHandlers.ItemDrop.UnRegister(OnItemDrop);
            GetDataHandlers.ChestItemChange.UnRegister(OnChestItemChange);
            _initialized = false;
        }

        private static void OnItemDrop(object? sender, GetDataHandlers.ItemDropEventArgs e)
        {
            if (e.Player == null)
                return;

            // 日志输出
            TShock.Log.ConsoleInfo($"[TSWeb] 玩家丢出物品: {e.Player.Name}, ID={e.ID}, Type={e.Type}, Stacks={e.Stacks}, Pos=({e.Position.X:F1},{e.Position.Y:F1})");

            // 对接物品限制审查器
            var matchedItems = ItemDetection.CheckItem(e.Player, e.Type, e.Stacks);
            if (matchedItems.Count > 0)
            {
                foreach (var matchedItem in matchedItems)
                {
                    // 阻止物品掉落
                    e.Handled = true;
                    Main.item[e.ID].TurnToAir();
                    NetMessage.SendData(21, -1, -1, null, e.ID, 0, 0);

                    TShock.Log.ConsoleError($"[TSWeb] 阻止丢出违禁物品! 玩家: {e.Player.Name}, 物品ID: {e.Type}, 数量: {e.Stacks}, 限制: {matchedItem.Stack}, 处理: {matchedItem.Method}");

                    ViolationExecutor.ExecuteViolation(e.Player, matchedItem.Method,
                        playerName: e.Player.Name,
                        itemId: e.Type,
                        itemName: AntiCheat.GetItemName(e.Type));
                }
            }
        }

        /// <summary>
        /// 箱子物品变更检测：拦截存入箱子的违禁物品
        /// </summary>
        private static void OnChestItemChange(object? sender, GetDataHandlers.ChestItemEventArgs e)
        {
            if (e.Player == null || e.Type <= 0)
                return;

            TShock.Log.ConsoleInfo($"[TSWeb] 玩家存取箱子: {e.Player.Name}, 箱子ID={e.ID}, Slot={e.Slot}, Type={e.Type}, Stacks={e.Stacks}");

            // 对接物品限制审查器
            var matchedItems = ItemDetection.CheckItem(e.Player, e.Type, e.Stacks);
            if (matchedItems.Count > 0)
            {
                foreach (var matchedItem in matchedItems)
                {
                    // 阻止物品存入箱子
                    e.Handled = true;

                    // 刷新箱子该槽位，让客户端恢复原状
                    if (e.ID >= 0 && e.ID < Main.chest.Length && Main.chest[e.ID] != null)
                    {
                        var chest = Main.chest[e.ID];
                        if (e.Slot < chest.item.Length)
                        {
                            var originalItem = chest.item[e.Slot];
                            e.Player.SendData(PacketTypes.ChestItem, "", e.ID, e.Slot, originalItem.stack, originalItem.prefix, originalItem.type);
                        }
                    }

                    e.Player.SendErrorMessage($"违禁物品({AntiCheat.GetItemName(e.Type)})无法存入箱子!");

                    TShock.Log.ConsoleError($"[TSWeb] 阻止存入箱子违禁物品! 玩家: {e.Player.Name}, 物品ID: {e.Type}, 数量: {e.Stacks}, 限制: {matchedItem.Stack}, 处理: {matchedItem.Method}");

                    ViolationExecutor.ExecuteViolation(e.Player, matchedItem.Method,
                        playerName: e.Player.Name,
                        itemId: e.Type,
                        itemName: AntiCheat.GetItemName(e.Type));
                }
            }
        }
    }
}
