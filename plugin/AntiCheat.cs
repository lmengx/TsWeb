﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Microsoft.Xna.Framework;
using Terraria;
using TShockAPI;
using TShockAPI.DB;
using Rests;

namespace TShockData
{
    public class AntiCheatConfig
    {
        [JsonProperty("启用")]
        public bool Enabled { get; set; } = false;

        [JsonProperty("自动扫描")]
        public bool AutoScan { get; set; } = false;

        [JsonProperty("扫描间隔")]
        public int AutoScanInterval { get; set; } = 600;

        [JsonProperty("没收违禁物品")]
        public bool ConfiscateItems { get; set; } = false;

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

            // 统计当前清单条数（物品 / 弹幕），用于确认是否已收到后端下发的默认配置
            int totalItems = _config.Restrictions?.Where(r => r?.Items != null).Sum(r => r.Items.Count) ?? 0;
            int totalProjs = _projConfig.Restrictions?.Where(r => r?.Projectiles != null).Sum(r => r.Projectiles.Count) ?? 0;
            TShock.Log.ConsoleInfo($"[TSWeb] 反作弊模块已加载 - 启用: {_config.Enabled}, 自动扫描: {_config.AutoScan}, 违禁物: {totalItems}, 违禁弹幕: {totalProjs}");

            // 空清单是首次运行的正常状态：清单以后端下发为唯一来源，插件不再内置任何规则
            if (totalItems == 0 && totalProjs == 0)
            {
                TShock.Log.ConsoleInfo("[TSWeb] 反作弊清单为空，等待后端下发默认配置（网页端打开反作弊开关即自动下发）");
            }
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
                    // 首次运行只落盘「空配置」占位，绝不写入内置违禁规则：
                    // 违禁清单以后端下发为唯一来源（backend/resources/默认配置/弹幕违禁.json，
                    // 由后端 anticheatDefaults.pushDefaultToPlugin 推送，入口为前端「启用检测」开关）。
                    // 若此处写入非空规则，后端 hasEffectiveConfig 会把插件端判定为「已有配置」而跳过下发，
                    // 服务器将长期运行在插件内置的旧清单上（即本次修复的问题）。
                    var emptyConfig = new ProjRestrictionConfig
                    {
                        Enabled = false,
                        DamageLimit = 20000,
                        Restrictions = new List<ProjRestriction>()
                    };

                    var json = JsonConvert.SerializeObject(emptyConfig, Formatting.Indented);
                    File.WriteAllText(ProjConfigPath, json);
                    _projConfig = emptyConfig;
                    TShock.Log.ConsoleInfo("[TSWeb] 弹幕违禁配置不存在，已生成空配置，等待后端下发默认配置");
                }
                else
                {
                    var json = File.ReadAllText(ProjConfigPath);
                    _projConfig = JsonConvert.DeserializeObject<ProjRestrictionConfig>(json) ?? new ProjRestrictionConfig();
                    _projConfig.Restrictions ??= new List<ProjRestriction>();
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

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var results = ItemDetection.ScanOnlinePlayer(player);
                sw.Stop();

                if (results.Count == 0)
                {
                    args.Player.SendSuccessMessage($"玩家 {playerName} 未检测到违规物品（耗时 {sw.ElapsedMilliseconds}ms）");
                    return;
                }

                args.Player.SendInfoMessage($"=== 扫描结果: {playerName} (在线，耗时 {sw.ElapsedMilliseconds}ms) ===");
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

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var results = ItemDetection.ScanOfflinePlayer(account.ID, account.Name);
                sw.Stop();

                if (results.Count == 0)
                {
                    args.Player.SendSuccessMessage($"玩家 {playerName} 未检测到违规物品（耗时 {sw.ElapsedMilliseconds}ms）");
                    return;
                }

                args.Player.SendInfoMessage($"=== 扫描结果: {playerName} (离线，耗时 {sw.ElapsedMilliseconds}ms) ===");
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
            // 只扫描在线玩家，命中违禁规则自动执行违规处理
            var report = ItemDetection.ScanAllPlayers();

            if (report.ViolationCount == 0)
            {
                args.Player.SendSuccessMessage($"已扫描 {report.ScannedPlayers} 名在线玩家，未检测到违规物品（耗时 {report.DurationMs}ms）");
                return;
            }

            args.Player.SendInfoMessage($"=== 批量扫描结果 (共 {report.ViolationCount} 条违规，扫描 {report.ScannedPlayers} 名在线玩家，耗时 {report.DurationMs}ms) ===");

            foreach (var result in report.Results)
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
                    // 首次运行只落盘「空配置」占位，绝不写入内置违禁规则：
                    // 违禁清单以后端下发为唯一来源（backend/resources/默认配置/物品违禁.json，
                    // 由后端 anticheatDefaults.pushDefaultToPlugin 推送，入口为前端「启用检测」开关）。
                    // 若此处写入非空规则，后端 hasEffectiveConfig 会把插件端判定为「已有配置」而跳过下发，
                    // 服务器将长期运行在插件内置的旧清单上（即本次修复的问题）。
                    // 扫描间隔取 10 秒，与后端默认配置资源保持一致（后端下发时会整体覆盖）。
                    var emptyConfig = new AntiCheatConfig
                    {
                        Enabled = false,
                        AutoScan = true,
                        AutoScanInterval = 10,
                        ConfiscateItems = false,
                        Restrictions = new List<ProgressRestriction>()
                    };

                    var json = JsonConvert.SerializeObject(emptyConfig, Formatting.Indented);
                    File.WriteAllText(ConfigPath, json);
                    _config = emptyConfig;
                    TShock.Log.ConsoleInfo("[TSWeb] 物品违禁配置不存在，已生成空配置，等待后端下发默认配置");
                }
                else
                {
                    var json = File.ReadAllText(ConfigPath);
                    _config = JsonConvert.DeserializeObject<AntiCheatConfig>(json) ?? new AntiCheatConfig();
                    _config.Restrictions ??= new List<ProgressRestriction>();

                    // 兼容旧配置字段名：旧版为 "自动扫描间隔-秒"，现统一为 "扫描间隔"
                    if (_config.AutoScanInterval <= 0)
                    {
                        var obj = Newtonsoft.Json.Linq.JObject.Parse(json);
                        var oldInterval = obj["自动扫描间隔-秒"];
                        if (oldInterval != null && oldInterval.Type == Newtonsoft.Json.Linq.JTokenType.Integer)
                        {
                            _config.AutoScanInterval = oldInterval.ToObject<int>();
                        }
                    }
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
            SaveConfig(_config);
        }

        public static void SaveConfig(AntiCheatConfig config)
        {
            try
            {
                _config = config;
                var directory = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonConvert.SerializeObject(config, Formatting.Indented);
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

        /// <summary>
        /// 获取弹幕名称（TShock 引用的原版 Terraria API：Lang.GetProjectileName）。
        /// 越界 / 无名称 / 异常时回退为 "弹幕ID:{projId}"。
        /// 用途：公屏播报等需要人类可读名称的场合；日志仍只记录 ID，前端自建表转名称。
        /// </summary>
        public static string GetProjectileName(int projId)
        {
            try
            {
                if (projId > 0)
                {
                    var name = Lang.GetProjectileName(projId).Value;
                    if (!string.IsNullOrEmpty(name))
                        return name;
                }
                return $"弹幕ID:{projId}";
            }
            catch
            {
                return $"弹幕ID:{projId}";
            }
        }

        /// <summary>
        /// POST /data/anticheat/enable — 反作弊轻量开关（仅开关，不动配置列表）
        /// 参数：itemEnabled（物品违禁总开关）/ projEnabled（弹幕违禁总开关），缺省保持原值
        /// </summary>
        public static object SetEnableApi(RestRequestArgs args)
        {
            try
            {
                LoadConfig();
                LoadProjConfig();

                var itemEnabled = args.Parameters["itemEnabled"];
                if (!string.IsNullOrEmpty(itemEnabled))
                {
                    _config.Enabled = itemEnabled.ToLower() == "true";
                    SaveConfig(_config);
                    TShock.Log.ConsoleInfo($"[TSWeb] REST 更新物品违禁开关: {_config.Enabled}");
                }

                var projEnabled = args.Parameters["projEnabled"];
                if (!string.IsNullOrEmpty(projEnabled))
                {
                    _projConfig.Enabled = projEnabled.ToLower() == "true";
                    SaveProjConfig(_projConfig);
                    TShock.Log.ConsoleInfo($"[TSWeb] REST 更新弹幕违禁开关: {_projConfig.Enabled}");
                }

                // 注意：不能写 { "status", "200" } —— RestObject 构造函数已预置 status 键，
                // 集合初始化器再 Add 同键会抛 ArgumentException（被 TShock 兜成 500 Internal server error）
                return new RestObject
                {
                    { "itemEnabled", _config.Enabled },
                    { "projEnabled", _projConfig.Enabled }
                };
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 反作弊开关更新失败: {ex.Message}");
                return new RestObject("500") { { "error", ex.Message } };
            }
        }
    }

    /// <summary>
    /// 单条违规条目（聚合执行时使用）：一次扫描中同一玩家命中的一条违禁物品记录
    /// </summary>
    public class ViolationEntry
    {
        public int ItemId { get; set; }
        public string ItemName { get; set; } = "";
        public int FoundStack { get; set; }
        public int AllowedStack { get; set; }
    }

    public static class ViolationExecutor
    {
        /// <summary>
        /// 聚合执行违规处理：同一次扫描中同一玩家的所有违规条目聚合成一次处理（不再逐条踢出，
        /// 避免客户端连续收到多个断开包）。method 为本次扫描的最高处理结果（ban > kick > log），
        /// 公屏播报统一格式：玩家"xxx"持有[i/s标签列表]，超过当前进度合法值，已记录/已踢出/已封禁。
        /// </summary>
        /// <param name="player">违规玩家（在线）</param>
        /// <param name="method">本次扫描最高处理方式：ban / kick / log</param>
        /// <param name="entries">该玩家的全部违规条目（本次扫描所有非法物品）</param>
        /// <param name="playerName">玩家名（缺省取 player.Name）</param>
        public static void ExecuteViolations(TSPlayer player, string method, List<ViolationEntry> entries, string playerName = null)
        {
            string name = playerName ?? player?.Name ?? "未知";
            string captureMethod = method;

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    string reason = BuildReason(entries);
                    string itemTags = BuildViolationTagText(entries);

                    // 统一公屏播报：玩家名 + 物品图标标签列表（本次扫描所有非法物品）+ 超过当前进度合法值 + 本次最高处理结果
                    string actionWord = captureMethod?.ToLower() switch
                    {
                        "ban" => "已封禁",
                        "kick" => "已踢出",
                        _ => "已记录"
                    };
                    string held = itemTags.Length > 0 ? $"持有{itemTags}" : "持有违禁品";
                    string report = $"玩家\"{name}\"{held}，超过当前进度合法值，{actionWord}";
                    TShock.Utils.Broadcast($"[反作弊] {report}", Color.Red);

                    switch (captureMethod?.ToLower())
                    {
                        case "ban":
                            ExecuteBan(name, reason);
                            if (player != null)
                            {
                                // silent: true → 不再触发 TShock 自带踢出广播，公屏播报统一由上方一条完成
                                player.Kick($"检测到作弊行为: {reason}", true, true);
                            }
                            TShock.Log.ConsoleError($"[反作弊] 已封禁玩家: {name}, 原因: {reason}");
                            break;
                        case "kick":
                            if (player != null)
                            {
                                player.Kick(reason, true, true);
                                TShock.Log.ConsoleError($"[反作弊] 已踢出玩家: {name}, 原因: {reason}");
                            }
                            else
                            {
                                TShock.Log.ConsoleError($"[反作弊] 违规记录 - 离线玩家: {name}, 原因: {reason}");
                            }
                            break;
                        default:
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

        /// <summary>
        /// 生成公屏播报用的物品展示文本：使用 [i/s{stack}:{itemId}] 聊天标签，客户端渲染为物品图标，
        /// 堆叠数量 > 1 时图标文本附带数量（如 [i/s24:356] = 木剑(ID 356) 24 个）。
        /// 同物品分布在多个格子时先按物品 ID 合并数量。无可展示物品时返回空串。
        /// </summary>
        private static string BuildViolationTagText(List<ViolationEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return "";

            var merged = MergeEntries(entries);
            return string.Join(" ", merged.Select(e => $"[i/s{e.FoundStack}:{e.ItemId}]"));
        }

        /// <summary>
        /// 单个物品的聊天标签，客户端 ItemTagHandler 会渲染成物品图标。
        /// 数量 > 1 时带 /s（与 TShock Utils.ItemTag 的生成规则一致），数量为 1 时省略，
        /// 两种写法客户端都会按数量 1 渲染。
        /// </summary>
        private static string FormatItemTag(int itemId, int stack)
        {
            return stack > 1 ? $"[i/s{stack}:{itemId}]" : $"[i:{itemId}]";
        }

        /// <summary>
        /// 单条违规的公屏播报文本（物品图标版）：物品类统一用图标标签，格式与聚合播报
        /// ExecuteViolations 保持一致（玩家名 + 持有的物品图标 + 处理结果），使单数路径
        /// （丢出/存箱拦截、自定义命令）与背包扫描路径的公屏表现一致。
        /// 弹幕类没有对应物品图标，返回 null，由调用方回退到纯文字播报。
        /// </summary>
        private static string? BuildItemBroadcastReport(string name, int itemId, int stack, int allowedStack, string actionWord)
        {
            if (itemId <= 0)
                return null;

            string tag = FormatItemTag(itemId, stack);
            // 合法值为 1 时属于"持有即违规"，与 BuildReason 的同名判断保持一致
            string held = allowedStack <= 1
                ? $"持有违禁品{tag}"
                : $"持有的{tag}共{stack}个，超过了当前阶段合法值";
            return $"玩家\"{name}\"{held}，{actionWord}";
        }

        /// <summary>
        /// 公屏播报的动作词。标准档直接映射；处理方式为自定义命令时按命令首词判断，
        /// 使面板的三个预设都有一条中文图标播报：
        /// 广播公告 /bc、/broadcast、/say → 已公告；踢出玩家 /kick → 已踢出；
        /// 封禁玩家 /banp、TShock /ban → 已封禁。
        /// 其余自定义命令语义未知（插件不代为公告），log 档维持不公告，均返回 null。
        /// 注意：处理方式在 RefreshRestrictedItems 载入时已被统一转为小写。
        /// </summary>
        private static string? ResolveActionWord(string? method)
        {
            if (string.IsNullOrWhiteSpace(method))
                return null;

            string m = method.Trim().ToLowerInvariant();
            if (m == "ban")
                return "已封禁";
            if (m == "kick")
                return "已踢出";
            if (m == "log")
                return null;

            string token = FirstCommandToken(m);
            switch (token)
            {
                // 面板「封禁玩家」预设 /banp，以及 TShock 的 /ban
                case "ban":
                case "banp":
                    return "已封禁";
                // 面板「踢出玩家」预设 /kick
                case "kick":
                    return "已踢出";
                // 面板「广播公告」预设 /bc，以及 TShock broadcast 的两个别名
                // （Commands.cs 中 Broadcast 注册为 "broadcast", "bc", "say"）
                case "bc":
                case "broadcast":
                case "say":
                    return "已公告";
                // 其余自定义命令语义未知，插件不代为公告
                default:
                    return null;
            }
        }

        /// <summary>
        /// 取命令首词：跳过前导的命令前缀符与空白，截到第一个空白为止
        /// （如 "/bc \"{playername}违规使用{itemname}\"" → bc）
        /// 前缀符不写死：TShock 的命令前缀符可配置（Commands.Specifier，默认 "/"，
        /// 另有静默前缀符），因此统一跳过开头的非字母数字字符。
        /// </summary>
        private static string FirstCommandToken(string command)
        {
            string s = command.TrimStart();
            int start = 0;
            while (start < s.Length && !char.IsLetterOrDigit(s[start]))
                start++;
            s = s.Substring(start);

            int end = 0;
            while (end < s.Length && !char.IsWhiteSpace(s[end]))
                end++;
            return s.Substring(0, end);
        }

        /// <summary>
        /// 聚合原因（纯文本，用于踢出/封禁界面，不依赖聊天标签渲染），统一格式：
        /// 持有物品名x数量、物品名x数量，超过当前进度合法值（不论违规物品是一个还是多个）
        /// </summary>
        private static string BuildReason(List<ViolationEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return "检测到作弊行为";

            var merged = MergeEntries(entries);

            var parts = merged.Select(e =>
            {
                string itemName = !string.IsNullOrEmpty(e.ItemName) ? e.ItemName : $"Item_{e.ItemId}";
                return $"{itemName}x{e.FoundStack}";
            });

            return $"持有{string.Join("、", parts)}，超过当前进度合法值";
        }

        /// <summary>
        /// 按物品 ID 合并违规条目：同物品分布在多个格子时数量求和（名称与限制取第一条）
        /// </summary>
        private static List<ViolationEntry> MergeEntries(List<ViolationEntry> entries)
        {
            return entries
                .GroupBy(e => e.ItemId)
                .Select(g => new ViolationEntry
                {
                    ItemId = g.Key,
                    ItemName = g.First().ItemName,
                    FoundStack = g.Sum(e => e.FoundStack),
                    AllowedStack = g.First().AllowedStack
                })
                .ToList();
        }

        public static void ExecuteViolation(TSPlayer player, string method, string playerName = null, int itemId = 0, string itemName = null, int projId = 0, int stack = 0, int allowedStack = 0)
        {
            string name = playerName ?? player?.Name ?? "未知";
            string captureMethod = method;

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    string reason = BuildReason(name, itemId, itemName, projId, stack, allowedStack);

                    // 公屏播报：与聚合播报 ExecuteViolations 同款顺序（先播报，再执行处理）。
                    // 物品类统一用物品图标标签（客户端渲染为图标），弹幕类没有对应物品图标，退回文字。
                    // 处理方式为自定义命令时同样由插件公告（动作词按命令首词判断，见 ResolveActionWord），
                    // 于是「广播公告 /bc」「踢出玩家 /kick」「封禁玩家 /banp」三个预设都有中文图标播报，
                    // 不再依赖管理员模板里的 {itemname} 文字；命令本身照旧执行。
                    // log 档与语义未知的自定义命令不公告（actionWord 为 null）。
                    string? actionWord = ResolveActionWord(captureMethod);
                    // 公告条件：player 非空且连接仍在。
                    // player 为空（离线/已断开）时不公告，与本次改动前一致；
                    // ConnectionAlive 用于吃掉同一批违规的后续条目：首次处理时 Kick 已让连接进入待终止
                    // 状态（原版发送断开包 msgType==2 即置 PendingTermination，见
                    // 反编译参考源码 Terraria/NetMessage.cs SendData 末尾），而存箱/丢出拦截是
                    // 逐条命中调用的（同一物品可命中多个进度条目），不加这个判断会重复刷屏。
                    // 例外：/bc 这类不含断开动作的自定义命令吃不掉后续条目，同一物品命中多条
                    // 进度条目时仍会各发一条（与命令本身逐条执行、逐条广播的既有行为一致）。
                    if (actionWord != null && player != null && player.ConnectionAlive)
                    {
                        string pubReport = BuildItemBroadcastReport(name, itemId, stack, allowedStack, actionWord)
                                           ?? $"{name}{reason}，{actionWord}";
                        TShock.Utils.Broadcast($"[反作弊] {pubReport}", Color.Red);
                    }

                    switch (captureMethod?.ToLower())
                    {
                        case "ban":
                            ExecuteBan(name, reason);
                            if (player != null)
                            {
                                // silent: true → 不再触发 TShock 自带英文全服播报
                                // （TSPlayer.Kick 在 silent=false 时播 "{0} was kicked for '{1}'"）。
                                // 公屏公告已由上方按物品图标统一发出一条，与聚合路径一致。
                                player.Kick($"检测到作弊行为: {reason}", true, true);
                            }
                            TShock.Log.ConsoleError($"[反作弊] 已封禁玩家: {name}, 原因: {reason}");
                            break;
                        case "kick":
                            if (player != null)
                            {
                                // 文字版仅用于踢出界面与日志（那里渲染不了聊天标签）；公屏公告已由上方统一发出
                                string kickReport = $"{name}{reason}，已踢出";
                                // 直接调 Kick，不走 /kick 命令：TShock 的 kick 命令内部固定传 silent=false
                                // （Commands.cs:{0}kick → players[0].Kick(reason, ..., false, ...)），
                                // 会额外产生一条英文全服播报。此处 silent=true，与聚合路径保持一致。
                                player.Kick(kickReport, true, true);
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
                            string command = ReplacePlaceholders(captureMethod, name, itemId, itemName, projId, stack);
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

        public static void ExecuteViolation(string playerName, string method, int itemId = 0, string itemName = null, int projId = 0, int stack = 0, int allowedStack = 0)
        {
            string captureMethod = method;

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    string reason = BuildReason(playerName, itemId, itemName, projId, stack, allowedStack);

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
                            string command = ReplacePlaceholders(captureMethod, playerName, itemId, itemName, projId, stack);
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

        private static string BuildReason(string playerName, int itemId, string itemName, int projId, int stack = 0, int allowedStack = 0)
        {
            if (projId > 0)
            {
                // 公屏/踢出界面消息用 TShock 侧弹幕名称（日志仍只记 ID，前端自建表转名称）
                return $"使用违禁弹幕{AntiCheat.GetProjectileName(projId)}";
            }
            if (itemId > 0)
            {
                string name = !string.IsNullOrEmpty(itemName) ? itemName : $"Item_{itemId}";
                // 限制 1 个 = 持有即违规（持有就踢）
                if (allowedStack <= 1)
                {
                    return $"持有违禁品{name}";
                }
                // 超过当前阶段合法值（不显示阈值具体数量）
                return $"持有的{name}共{stack}个，超过了当前阶段合法值";
            }
            return "检测到作弊行为";
        }

        /// <summary>
        /// 把值包装成 TShock 命令可安全解析的单个参数：加双引号，并转义内部的 \ 与 "。
        ///
        /// 依据（本地源码 TShockAPI/Commands.cs）：
        ///   - HandleCommand：命令名取到第一个空白为止，其余交给 ParseParameters；
        ///   - ParseParameters：\ 是转义符（\" → "、\ 空格 → 空格、\\ → \），
        ///     " 切换「引号内」状态，引号外的空白切分出新的参数。
        /// 因此未加引号地把玩家名拼进命令，名字里的空格会让后面所有参数整体后移：
        /// 作弊者只要把角色名取成「无辜玩家 x」，反作弊执行的 /banp {playername}
        /// 就会把封禁打到「无辜玩家」这个别人身上（定向误封）。
        ///
        /// 加引号后，无论名字含空格、引号还是反斜杠，都只占一个参数位置。
        /// </summary>
        private static string QuoteCommandArg(string value)
        {
            if (value == null)
                return "\"\"";
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        /// <summary>
        /// 清洗将要插入「自定义命令模板」的占位符值（{playername} / {itemname}）。
        ///
        /// 处理方式：只把 \ 与 " 转义，其余字符（含空格）原样保留。
        ///
        /// 为什么不能加引号：模板由管理员在反作弊配置里自定义，而项目内置的预设
        /// 已经自带引号（如 `/banp "{playername}" "违规使用{itemname}"`，
        /// 见 ItemSearchDialog.vue / ItemRestrictView.vue）。若再补一对引号会变成
        /// `""名字""`，反而解析错乱。
        ///
        /// 为什么不能把空白换成下划线：占位符已处于管理员写的引号内，
        /// 引号内的空白本就不切分参数；替换掉空白会把「张 三」这类真名改坏，
        /// 导致封禁查不到账户而失效。保留空白即可，安全性由「引号内」保证。
        ///
        /// 安全性依据：转义后该值既无法闭合管理员写的引号，也无法吞掉后一字符，
        /// 因此无法跳出参数位置去改变命令结构。
        /// </summary>
        private static string SanitizeCommandArg(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string ReplacePlaceholders(string command, string playerName, int itemId, string itemName, int projId, int stack = 0)
        {
            if (string.IsNullOrEmpty(command))
                return string.Empty;

            // 玩家名与物品名属外部数据（角色名可被玩家任意设置），插入模板前必须清洗；
            // itemid / projid 为 int，天然安全，保持原样以免改变既有模板行为。
            // {itemtag} 由本插件生成（形如 [i/s24:356]），只含数字与固定字符，同样无需清洗。
            //
            // 必须「单遍」替换：写成一串 string.Replace 会把已经替换进去的值再扫一遍，
            // 玩家只要把角色名取成 {itemtag}（或 {itemname}），名字里那串字符就会被再次展开，
            // 凭空多出一个物品图标。这里只扫描模板原文，替换结果不再参与匹配；
            // 不认识的 {xxx} 原样保留（与逐次替换的行为一致）。
            var result = new System.Text.StringBuilder(command.Length + 32);
            int pos = 0;
            while (true)
            {
                int open = command.IndexOf('{', pos);
                int close = open < 0 ? -1 : command.IndexOf('}', open + 1);
                if (close < 0)
                {
                    result.Append(command, pos, command.Length - pos);
                    return result.ToString();
                }

                result.Append(command, pos, open - pos);
                switch (command.Substring(open, close - open + 1))
                {
                    case "{playername}":
                        result.Append(SanitizeCommandArg(playerName));
                        break;
                    case "{itemid}":
                        result.Append(itemId.ToString());
                        break;
                    case "{itemname}":
                        result.Append(SanitizeCommandArg(itemName ?? ""));
                        break;
                    case "{projid}":
                        result.Append(projId.ToString());
                        break;
                    case "{itemtag}":
                        // 公屏播报类自定义命令（如 /bc）用它输出物品图标，客户端渲染为图标；
                        // 物品 ID 缺失（弹幕类）时退回物品名文本，避免输出空串。
                        result.Append(itemId > 0 ? FormatItemTag(itemId, stack) : SanitizeCommandArg(itemName ?? ""));
                        break;
                    default:
                        result.Append(command, open, close - open + 1);
                        break;
                }

                pos = close + 1;
            }
        }

        private static void ExecuteBan(string username, string reason)
        {
            // 参数加引号：名字含空格/引号时仍只占一个参数，避免解析错位误封他人
            string command = $"banp {QuoteCommandArg(username)} {QuoteCommandArg(reason)}";
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