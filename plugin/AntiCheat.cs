﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using System;
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

    public class ProgressRestriction
    {
        [JsonProperty("进度")]
        public string Progress { get; set; } = "始终生效";

        [JsonProperty("限制物品")]
        public List<RestrictedItem> Items { get; set; } = new List<RestrictedItem>();
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

    public static class ViolationExecutor
    {
        public static void ExecuteViolation(TSPlayer player, string method, string playerName = null, int itemId = 0, string itemName = null, int projId = 0)
        {
            string name = playerName ?? player?.Name ?? "未知";
            string captureMethod = method;

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    string reason = BuildReason(name, itemId, itemName, projId);

                    switch (captureMethod?.ToLower())
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
                            string command = ReplacePlaceholders(captureMethod, name, itemId, itemName, projId);
                            ExecuteCommand(command);
                            TShock.Log.ConsoleError($"[反作弊] 违规记录 - 玩家: {name}, 原因: {reason}");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleError($"[反作弊] 执行违规处理失败: {ex.Message}");
                }
            });
        }

        public static void ExecuteViolation(string playerName, string method, int itemId = 0, string itemName = null, int projId = 0)
        {
            string captureMethod = method;

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    string reason = BuildReason(playerName, itemId, itemName, projId);

                    switch (captureMethod?.ToLower())
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
                            string command = ReplacePlaceholders(captureMethod, playerName, itemId, itemName, projId);
                            ExecuteCommand(command);
                            TShock.Log.ConsoleError($"[反作弊] 违规记录 - 玩家: {playerName}, 原因: {reason}");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleError($"[反作弊] 执行违规处理失败: {ex.Message}");
                }
            });
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
            if (string.IsNullOrEmpty(command))
                return;

            string finalCommand = command.Trim();
            if (!finalCommand.StartsWith("/"))
            {
                finalCommand = "/" + finalCommand;
            }

            TShock.Log.ConsoleInfo($"[反作弊] 执行命令: {finalCommand}");
            TShockAPI.Commands.HandleCommand(TShockAPI.TSPlayer.Server, finalCommand);
        }
    }

}