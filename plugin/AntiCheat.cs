﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Terraria;
using TShockAPI;
using TShockAPI.DB;

namespace TShockData
{
    public class AntiCheatConfig
    {
        [JsonProperty("启用")]
        public bool Enabled { get; set; } = true;

        [JsonProperty("自动扫描")]
        public bool AutoScan { get; set; } = false;

        [JsonProperty("自动扫描间隔-秒")]
        public int AutoScanInterval { get; set; } = 600;

        [JsonProperty("限制列表")]
        public List<ProgressRestriction> Restrictions { get; set; } = new List<ProgressRestriction>();
    }

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
            {
                return results;
            }

            var config = AntiCheat.GetConfig();
            if (config == null || !config.Enabled)
            {
                return results;
            }

            bool hasPermission = player.HasPermission("tshock.admin") || player.HasPermission("tshock.item.usebanned");

            List<InventoryData> inventory = GetPlayerInv.GetOnlinePlayerInventory(player);
            if (inventory == null || inventory.Count == 0)
            {
                return results;
            }

            foreach (var item in inventory)
            {
                if (item.netID <= 0) continue;

                var matchedItems = CheckItem(player, item.netID, item.stack);
                if (matchedItems.Count > 0)
                {
                    foreach (var matchedItem in matchedItems)
                    {
                        string reason = $"持有违禁物品(ID:{item.netID},数量:{item.stack},限制:{matchedItem.Stack})";
                        
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
            {
                return results;
            }

            List<InventoryData> inventory = GetPlayerInv.GetOfflinePlayerInventory(accountId);
            if (inventory == null)
            {
                return results;
            }

            foreach (var item in inventory)
            {
                if (item.netID <= 0) continue;

                var matchedItems = CheckItem(null, item.netID, item.stack);
                if (matchedItems.Count > 0)
                {
                    foreach (var matchedItem in matchedItems)
                    {
                        string reason = $"持有违禁物品(ID:{item.netID},数量:{item.stack},限制:{matchedItem.Stack})";
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

    public class ProgressRestriction
    {
        [JsonProperty("进度")]
        public string Progress { get; set; } = "始终生效";

        [JsonProperty("限制物品")]
        public List<RestrictedItem> Items { get; set; } = new List<RestrictedItem>();
    }

    public class RestrictedItem
    {
        [JsonProperty("id")]
        public int ID { get; set; }

        [JsonProperty("stack")]
        public int Stack { get; set; } = 1;

        [JsonProperty("method")]
        public string Method { get; set; } = "log";
    }

    public class ProjRestrictionConfig
    {
        [JsonProperty("启用")]
        public bool Enabled { get; set; } = true;

        [JsonProperty("伤害上限")]
        public int DamageLimit { get; set; } = 20000;

        [JsonProperty("限制列表")]
        public List<ProjRestriction> Restrictions { get; set; } = new List<ProjRestriction>();
    }

    public class ProjRestriction
    {
        [JsonProperty("进度")]
        public string Progress { get; set; } = "始终生效";

        [JsonProperty("限制弹幕")]
        public List<RestrictedProj> Projectiles { get; set; } = new List<RestrictedProj>();
    }

    public class RestrictedProj
    {
        [JsonProperty("id")]
        public int ID { get; set; }

        [JsonProperty("method")]
        public string Method { get; set; } = "log";
    }

    public static class AntiCheat
    {
        private static AntiCheatConfig _config;
        private static ProjRestrictionConfig _projConfig;
        private static string ConfigPath => Path.Combine(TShock.SavePath, "TSWeb", "AntiCheat", "物品违禁.json");
        private static string ProjConfigPath => Path.Combine(TShock.SavePath, "TSWeb", "AntiCheat", "弹幕违禁.json");

        public static void Initialize()
        {
            LoadConfig();
            LoadProjConfig();
            int totalItems = _config.Restrictions.Sum(r => r.Items.Count);
            TShock.Log.ConsoleInfo($"[TSWeb] 反作弊模块已加载 - 启用: {_config.Enabled}, 自动扫描: {_config.AutoScan}, 违禁物: {totalItems}");
        }

        public static void LoadProjConfig()
        {
            try
            {
                var directory = Path.GetDirectoryName(ProjConfigPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (!File.Exists(ProjConfigPath))
                {
                    var defaultConfig = new ProjRestrictionConfig
                    {
                        Enabled = true,
                        DamageLimit = 20000,
                        Restrictions = new List<ProjRestriction>
                        {
                            new ProjRestriction
                            {
                                Progress = "始终生效",
                                Projectiles = new List<RestrictedProj>
                                {
                                    new RestrictedProj { ID = 108, Method = "ban" },
                                    new RestrictedProj { ID = 136, Method = "kick" },
                                    new RestrictedProj { ID = 137, Method = "kick" },
                                    new RestrictedProj { ID = 138, Method = "kick" },
                                    new RestrictedProj { ID = 142, Method = "kick" },
                                    new RestrictedProj { ID = 143, Method = "kick" },
                                    new RestrictedProj { ID = 144, Method = "kick" },
                                    new RestrictedProj { ID = 164, Method = "ban" }
                                }
                            },
                            new ProjRestriction
                            {
                                Progress = "蜂后",
                                Projectiles = new List<RestrictedProj>
                                {
                                    new RestrictedProj { ID = 183, Method = "ban" },
                                    new RestrictedProj { ID = 469, Method = "ban" }
                                }
                            },
                            new ProjRestriction
                            {
                                Progress = "血肉墙",
                                Projectiles = new List<RestrictedProj>
                                {
                                    new RestrictedProj { ID = 79, Method = "ban" },
                                    new RestrictedProj { ID = 91, Method = "ban" },
                                    new RestrictedProj { ID = 161, Method = "ban" }
                                }
                            },
                            new ProjRestriction
                            {
                                Progress = "世纪之花",
                                Projectiles = new List<RestrictedProj>
                                {
                                    new RestrictedProj { ID = 302, Method = "ban" },
                                    new RestrictedProj { ID = 356, Method = "ban" }
                                }
                            },
                            new ProjRestriction
                            {
                                Progress = "石巨人",
                                Projectiles = new List<RestrictedProj>
                                {
                                    new RestrictedProj { ID = 182, Method = "ban" }
                                }
                            },
                            new ProjRestriction
                            {
                                Progress = "光之女皇",
                                Projectiles = new List<RestrictedProj>
                                {
                                    new RestrictedProj { ID = 915, Method = "ban" }
                                }
                            },
                            new ProjRestriction
                            {
                                Progress = "拜月教教徒",
                                Projectiles = new List<RestrictedProj>
                                {
                                    new RestrictedProj { ID = 625, Method = "ban" },
                                    new RestrictedProj { ID = 632, Method = "ban" },
                                    new RestrictedProj { ID = 634, Method = "ban" },
                                    new RestrictedProj { ID = 636, Method = "ban" },
                                    new RestrictedProj { ID = 611, Method = "ban" },
                                    new RestrictedProj { ID = 613, Method = "ban" },
                                    new RestrictedProj { ID = 615, Method = "ban" }
                                }
                            },
                            new ProjRestriction
                            {
                                Progress = "月亮领主",
                                Projectiles = new List<RestrictedProj>
                                {
                                    new RestrictedProj { ID = 933, Method = "ban" },
                                    new RestrictedProj { ID = 1100, Method = "ban" },
                                    new RestrictedProj { ID = 502, Method = "ban" },
                                    new RestrictedProj { ID = 503, Method = "ban" },
                                    new RestrictedProj { ID = 1035, Method = "ban" },
                                    new RestrictedProj { ID = 603, Method = "ban" },
                                    new RestrictedProj { ID = 645, Method = "ban" },
                                    new RestrictedProj { ID = 643, Method = "ban" },
                                    new RestrictedProj { ID = 650, Method = "ban" },
                                    new RestrictedProj { ID = 715, Method = "ban" },
                                    new RestrictedProj { ID = 716, Method = "ban" },
                                    new RestrictedProj { ID = 717, Method = "ban" },
                                    new RestrictedProj { ID = 718, Method = "ban" },
                                    new RestrictedProj { ID = 609, Method = "ban" },
                                    new RestrictedProj { ID = 610, Method = "ban" }
                                }
                            }
                        }
                    };

                    var json = JsonConvert.SerializeObject(defaultConfig, Formatting.Indented);
                    File.WriteAllText(ProjConfigPath, json);
                    _projConfig = defaultConfig;
                }
                else
                {
                    var json = File.ReadAllText(ProjConfigPath);
                    _projConfig = JsonConvert.DeserializeObject<ProjRestrictionConfig>(json) ?? new ProjRestrictionConfig();
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 加载弹幕违禁配置失败: {ex.Message}");
                _projConfig = new ProjRestrictionConfig();
            }
        }

        public static ProjRestrictionConfig GetProjConfig()
        {
            return _projConfig;
        }

        public static bool SaveProjConfig(ProjRestrictionConfig config)
        {
            try
            {
                var directory = Path.GetDirectoryName(ProjConfigPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(ProjConfigPath, json);
                _projConfig = config;

                ProjDetection.RefreshRestrictedProjectiles();

                TShock.Log.ConsoleInfo($"[TSWeb] 弹幕违禁配置已保存");
                return true;
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 保存弹幕违禁配置失败: {ex.Message}");
                return false;
            }
        }

        public static void HandleScanCommand(CommandArgs args)
        {
            if (args.Parameters.Count == 0)
            {
                args.Player.SendInfoMessage("用法:");
                args.Player.SendInfoMessage("/scan <玩家名> - 扫描指定玩家(支持离线)");
                args.Player.SendInfoMessage("/scan * - 扫描所有玩家(包括离线)");
                return;
            }

            string target = args.Parameters[0];

            if (target == "*")
            {
                ScanAllCommand(args);
            }
            else
            {
                ScanSingleCommand(args, target);
            }
        }

        private static void ScanSingleCommand(CommandArgs args, string playerName)
        {
            var onlinePlayers = TShockAPI.TSPlayer.FindByNameOrID(playerName);
            if (onlinePlayers.Count > 0)
            {
                var player = onlinePlayers[0];
                if (!player.Active)
                {
                    args.Player.SendErrorMessage($"玩家 {playerName} 不在游戏中");
                    return;
                }

                var results = ItemDetection.ScanOnlinePlayer(player);

                if (results.Count == 0)
                {
                    args.Player.SendSuccessMessage($"玩家 {playerName} 未检测到违规物品");
                    return;
                }

                args.Player.SendInfoMessage($"=== 扫描结果: {playerName} (在线) ===");
                foreach (var result in results)
                {
                    string msg = $"[警告] 物品ID:{result.ItemID}({result.ItemName}) 数量:{result.FoundStack} 限制:{result.AllowedStack}";
                    args.Player.SendInfoMessage(msg);
                    TShock.Log.ConsoleInfo($"[ItemDetection] {msg} - 玩家: {result.PlayerName}");
                }
            }
            else
            {
                var account = TShock.UserAccounts.GetUserAccountByName(playerName);
                if (account == null)
                {
                    args.Player.SendErrorMessage($"找不到玩家: {playerName}");
                    return;
                }

                var results = ItemDetection.ScanOfflinePlayer(account.ID, account.Name);

                if (results.Count == 0)
                {
                    args.Player.SendSuccessMessage($"玩家 {playerName} 未检测到违规物品");
                    return;
                }

                args.Player.SendInfoMessage($"=== 扫描结果: {playerName} (离线) ===");
                foreach (var result in results)
                {
                    string msg = $"[警告] 物品ID:{result.ItemID}({result.ItemName}) 数量:{result.FoundStack} 限制:{result.AllowedStack}";
                    args.Player.SendInfoMessage(msg);
                    TShock.Log.ConsoleInfo($"[ItemDetection] {msg} - 玩家: {result.PlayerName}");
                }
            }
        }

        private static void ScanAllCommand(CommandArgs args)
        {
            var results = ItemDetection.ScanAllPlayers();

            if (results.Count == 0)
            {
                args.Player.SendSuccessMessage("所有在线玩家未检测到违规物品");
                return;
            }

            args.Player.SendInfoMessage($"=== 批量扫描结果 (共 {results.Count} 条违规) ===");

            foreach (var result in results)
            {
                string msg = $"玩家:{result.PlayerName} 物品ID:{result.ItemID}({result.ItemName}) 数量:{result.FoundStack} 限制:{result.AllowedStack}";
                args.Player.SendInfoMessage(msg);
                TShock.Log.ConsoleInfo($"[ItemDetection] {msg}");
            }
        }

        public static void LoadConfig()
        {
            try
            {
                var directory = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (!File.Exists(ConfigPath))
                {
                    var defaultConfig = new AntiCheatConfig
                    {
                        Enabled = true,
                        AutoScan = true,
                        AutoScanInterval = 600,
                        Restrictions = new List<ProgressRestriction>
                        {
                            new ProgressRestriction
                            {
                                Progress = "始终生效",
                                Items = new List<RestrictedItem>()
                            },
                            new ProgressRestriction
                            {
                                Progress = "月亮领主",
                                Items = new List<RestrictedItem>
                                {
                                    new RestrictedItem { ID = 4956, Stack = 1, Method = "ban" }
                                }
                            }
                        }
                    };

                    var json = JsonConvert.SerializeObject(defaultConfig, Formatting.Indented);
                    File.WriteAllText(ConfigPath, json);
                    _config = defaultConfig;
                }
                else
                {
                    var json = File.ReadAllText(ConfigPath);
                    _config = JsonConvert.DeserializeObject<AntiCheatConfig>(json) ?? new AntiCheatConfig();
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 加载物品违禁配置失败: {ex.Message}");
                _config = new AntiCheatConfig();
            }
        }

        public static void SaveConfig()
        {
            try
            {
                var directory = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonConvert.SerializeObject(_config, Formatting.Indented);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 保存物品违禁配置失败: {ex.Message}");
            }
        }

        public static AntiCheatConfig GetConfig()
        {
            return _config;
        }

        public static string GetItemName(int itemId)
        {
            try
            {
                var item = TShock.Utils.GetItemById(itemId);
                return item != null && item.type > 0 ? item.Name : $"Item_{itemId}";
            }
            catch
            {
                return $"Item_{itemId}";
            }
        }
    }

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

    public static class ViolationExecutor
    {
        public static void ExecuteViolation(TSPlayer player, string method, string playerName = null, int itemId = 0, string itemName = null, int projId = 0)
        {
            string name = playerName ?? player?.Name ?? "未知";

            try
            {
                string reason = BuildReason(name, itemId, itemName, projId);

                switch (method?.ToLower())
                {
                    case "ban":
                        ExecuteBan(name, reason);
                        if (player != null)
                        {
                            player.Kick($"检测到作弊行为: {reason}", true);
                        }
                        TShock.Log.ConsoleError($"[反作弊] 已封禁玩家: {name}, 原因: {reason}");
                        break;
                    case "kick":
                        if (player != null)
                        {
                            ExecuteKick(player, name, reason);
                            TShock.Log.ConsoleError($"[反作弊] 已踢出玩家: {name}, 原因: {reason}");
                        }
                        else
                        {
                            TShock.Log.ConsoleError($"[反作弊] 违规记录 - 离线玩家: {name}, 原因: {reason}");
                        }
                        break;
                    case "log":
                        TShock.Log.ConsoleError($"[反作弊] 违规记录 - 玩家: {name}, 原因: {reason}");
                        break;
                    default:
                        string command = ReplacePlaceholders(method, name, itemId, itemName, projId);
                        ExecuteCommand(command);
                        TShock.Log.ConsoleError($"[反作弊] 违规记录 - 玩家: {name}, 原因: {reason}");
                        break;
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[反作弊] 执行违规处理失败: {ex.Message}");
            }
        }

        public static void ExecuteViolation(string playerName, string method, int itemId = 0, string itemName = null, int projId = 0)
        {
            try
            {
                string reason = BuildReason(playerName, itemId, itemName, projId);

                switch (method?.ToLower())
                {
                    case "ban":
                        ExecuteBan(playerName, reason);
                        TShock.Log.ConsoleError($"[反作弊] 已封禁玩家: {playerName}, 原因: {reason}");
                        break;
                    case "kick":
                        TShock.Log.ConsoleError($"[反作弊] 违规记录 - 离线玩家: {playerName}, 原因: {reason}");
                        break;
                    case "log":
                        TShock.Log.ConsoleError($"[反作弊] 违规记录 - 玩家: {playerName}, 原因: {reason}");
                        break;
                    default:
                        string command = ReplacePlaceholders(method, playerName, itemId, itemName, projId);
                        ExecuteCommand(command);
                        TShock.Log.ConsoleError($"[反作弊] 违规记录 - 玩家: {playerName}, 原因: {reason}");
                        break;
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[反作弊] 执行违规处理失败: {ex.Message}");
            }
        }

        private static string BuildReason(string playerName, int itemId, string itemName, int projId)
        {
            if (projId > 0)
            {
                return $"使用违禁弹幕(ID:{projId})";
            }
            if (itemId > 0)
            {
                string name = !string.IsNullOrEmpty(itemName) ? $"({itemName})" : "";
                return $"持有违禁物品(ID:{itemId}{name})";
            }
            return "检测到作弊行为";
        }

        private static string ReplacePlaceholders(string command, string playerName, int itemId, string itemName, int projId)
        {
            if (string.IsNullOrEmpty(command))
                return string.Empty;

            return command
                .Replace("{playername}", playerName)
                .Replace("{itemid}", itemId.ToString())
                .Replace("{itemname}", itemName ?? "")
                .Replace("{projid}", projId.ToString());
        }

        private static void ExecuteBan(string username, string reason)
        {
            string command = $"banp {username} {reason}";
            TShock.Log.ConsoleInfo($"[反作弊] 执行命令: /{command}");
            TShockAPI.Commands.HandleCommand(TShockAPI.TSPlayer.Server, "/" + command);
        }

        private static void ExecuteKick(TSPlayer player, string username, string reason)
        {
            string command = $"kick {username} {reason}";
            TShock.Log.ConsoleInfo($"[反作弊] 执行命令: /{command}");
            TShockAPI.Commands.HandleCommand(TShockAPI.TSPlayer.Server, "/" + command);
        }

        private static void ExecuteCommand(string command)
        {
            TShock.Log.ConsoleInfo($"[反作弊] 执行命令: /{command}");
            TShockAPI.Commands.HandleCommand(TShockAPI.TSPlayer.Server, "/" + command);
        }
    }

    public static class ProjDetection
    {
        private static List<RestrictedProj> _currentRestrictedProjectiles = new List<RestrictedProj>();

        public static void Initialize()
        {
            RefreshRestrictedProjectiles();
            TShock.Log.ConsoleInfo($"[ProjDetection] 弹幕检测已初始化，违禁弹幕数: {_currentRestrictedProjectiles.Count}");
        }

        public static void ShowRestrictedList(CommandArgs args)
        {
            args.Player.SendInfoMessage("=== 当前生效的违禁弹幕列表 ===");
            args.Player.SendInfoMessage($"总数: {_currentRestrictedProjectiles.Count}");
            
            foreach (var proj in _currentRestrictedProjectiles)
            {
                string methodDesc = proj.Method switch
                {
                    "ban" => "封禁",
                    "kick" => "踢出",
                    "log" => "记录",
                    _ => $"命令: {proj.Method}"
                };
                
                args.Player.SendInfoMessage($"弹幕ID: {proj.ID}, 处理方式: {methodDesc}");
            }
            
            if (_currentRestrictedProjectiles.Count == 0)
            {
                args.Player.SendInfoMessage("当前没有生效的违禁弹幕");
            }
        }

        public static void RefreshRestrictedProjectiles()
        {
            _currentRestrictedProjectiles.Clear();

            var config = AntiCheat.GetProjConfig();
            if (config?.Restrictions == null)
                return;

            foreach (var restriction in config.Restrictions)
            {
                bool progressMet = BossProgress.GetWorldStatus(restriction.Progress);

                if (progressMet)
                {
                    foreach (var proj in restriction.Projectiles)
                    {
                        proj.Method = proj.Method?.ToLower() ?? "log";
                        _currentRestrictedProjectiles.Add(proj);
                    }
                }
            }
        }

        public static bool CheckProjectile(TSPlayer player, short type, short damage)
        {
            if (player == null || !player.Active || !player.IsLoggedIn)
                return false;

            if (player.HasPermission("tshock.admin.projectileban"))
            {
                return false;
            }

            var config = AntiCheat.GetProjConfig();
            if (config == null || !config.Enabled)
                return false;

            if (damage > config.DamageLimit || damage < -10)
            {
                TShock.Log.ConsoleError($"[ProjDetection] 检测到异常伤害弹幕! 玩家: {player.Name}, 弹幕ID: {type}, 伤害: {damage}");
                string reason = damage < -10
                    ? $"弹幕伤害异常(负数:{damage})"
                    : $"弹幕伤害异常({damage} > {config.DamageLimit})";


                if (player.HasPermission("tshock.projectiles.usebanned"))
                {
                    return true;
                }
                
                ViolationExecutor.ExecuteViolation(player, "ban");
                return true;
            }

            var matchedProjs = _currentRestrictedProjectiles.Where(p => p.ID == type).ToList();
            if (matchedProjs.Count > 0)
            {
                RefreshRestrictedProjectiles();
                var confirmedProjs = _currentRestrictedProjectiles.Where(p => p.ID == type).ToList();
                if (confirmedProjs.Count > 0)
                {
                    if (player.HasPermission("tshock.projectiles.usebanned"))
                    {
                        return true;
                    }

                    foreach (var confirmedProj in confirmedProjs)
                        {
                            TShock.Log.ConsoleError($"[ProjDetection] 检测到违禁弹幕! 玩家: {player.Name}, 弹幕ID: {type}, 伤害: {damage}, 处理方式: {confirmedProj.Method}");

                            ViolationExecutor.ExecuteViolation(player, confirmedProj.Method, projId: type);
                        }
                    return true;
                }
            }
            return false;
        }
    }
}