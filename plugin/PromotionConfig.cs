using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TShockAPI;
using TShockAPI.DB;
using Newtonsoft.Json;
using Rests;

namespace TShockData
{
    /// <summary>
    /// 权限提升模式
    /// </summary>
    public enum PromotionMode
    {
        /// <summary>沿父组链检查：若当前组 ≥ 目标组则跳过，否则晋升</summary>
        Auto,
        /// <summary>忽略父组链，直接强制设为目标组</summary>
        Force
    }

    /// <summary>
    /// QQ 绑定后自动提升配置
    /// </summary>
    public class QqBindPromotionConfig
    {
        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = false;

        [JsonProperty("targetGroup")]
        public string TargetGroup { get; set; } = "vip";

        [JsonProperty("mode")]
        public string Mode { get; set; } = "auto";
    }

    /// <summary>
    /// 单个游玩时长阈值配置
    /// </summary>
    public class PlaytimeThresholdConfig
    {
        [JsonProperty("minutes")]
        public int Minutes { get; set; } = 3000;

        [JsonProperty("targetGroup")]
        public string TargetGroup { get; set; } = "trustedplayer";

        [JsonProperty("mode")]
        public string Mode { get; set; } = "auto";
    }

    /// <summary>
    /// 权限提升统一配置
    /// </summary>
    public class PromotionConfig
    {
        [JsonProperty("qqBind")]
        public QqBindPromotionConfig QqBind { get; set; } = new();

        [JsonProperty("playtimeThresholds")]
        public List<PlaytimeThresholdConfig> PlaytimeThresholds { get; set; } = new();

        [JsonProperty("ignoreGroups")]
        public List<string> IgnoreGroups { get; set; } = new()
        {
            "owner",
            "superadmin"
        };
    }

    /// <summary>
    /// 权限提升配置管理器
    /// </summary>
    public static class PromotionManager
    {
        private static PromotionConfig _config = new();
        private static string ConfigPath => Path.Combine(TShock.SavePath, "TSWeb", "promotion_config.json");
        private static bool _loaded = false;

        private static void EnsureLoaded()
        {
            if (!_loaded)
            {
                LoadConfig();
                _loaded = true;
            }
        }

        public static PromotionConfig GetConfig()
        {
            EnsureLoaded();
            return JsonConvert.DeserializeObject<PromotionConfig>(
                JsonConvert.SerializeObject(_config));
        }

        public static void LoadConfig()
        {
            try
            {
                var directory = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    _config = JsonConvert.DeserializeObject<PromotionConfig>(json) ?? new PromotionConfig();
                    TShock.Log.ConsoleInfo("[TSWeb] 权限提升配置已加载");
                }
                else
                {
                    _config = new PromotionConfig();
                    SaveConfig();
                    TShock.Log.ConsoleInfo("[TSWeb] 已创建默认权限提升配置");
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 加载权限提升配置失败: {ex.Message}");
                _config = new PromotionConfig();
            }
        }

        public static void SaveConfig()
        {
            try
            {
                var directory = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var json = JsonConvert.SerializeObject(_config, Formatting.Indented);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 保存权限提升配置失败: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════
        // REST API
        // ═══════════════════════════════════════════════

        public static object GetConfigJson(RestRequestArgs args)
        {
            EnsureLoaded();

            // 手工构建小写字段名，JavaScriptSerializer 不认 [JsonProperty]
            var qqBind = new Dictionary<string, object>
            {
                { "enabled", _config.QqBind.Enabled },
                { "targetGroup", _config.QqBind.TargetGroup },
                { "mode", _config.QqBind.Mode }
            };

            var thresholds = new List<object>();
            foreach (var t in _config.PlaytimeThresholds)
            {
                thresholds.Add(new Dictionary<string, object>
                {
                    { "minutes", t.Minutes },
                    { "targetGroup", t.TargetGroup },
                    { "mode", t.Mode }
                });
            }

            var result = new RestObject
            {
                { "qqBind", qqBind },
                { "playtimeThresholds", thresholds },
                { "ignoreGroups", new List<string>(_config.IgnoreGroups) }
            };

            return result;
        }

        public static object SetConfigJson(RestRequestArgs args)
        {
            try
            {
                // 参数名与前端发送字段一致
                string qqBindJson = null;
                string playtimeThresholdsJson = null;
                string ignoreGroupsJson = null;

                try { qqBindJson = args.Parameters["qqBind"]; } catch { }
                try { playtimeThresholdsJson = args.Parameters["playtimeThresholds"]; } catch { }
                try { ignoreGroupsJson = args.Parameters["ignoreGroups"]; } catch { }

                if (!string.IsNullOrEmpty(qqBindJson))
                {
                    var parsed = Newtonsoft.Json.JsonConvert.DeserializeObject<QqBindPromotionConfig>(qqBindJson);
                    if (parsed != null)
                        _config.QqBind = parsed;
                }

                if (!string.IsNullOrEmpty(playtimeThresholdsJson))
                {
                    var parsed = Newtonsoft.Json.JsonConvert.DeserializeObject<List<PlaytimeThresholdConfig>>(playtimeThresholdsJson);
                    if (parsed != null)
                        _config.PlaytimeThresholds = parsed;
                }

                if (ignoreGroupsJson != null)
                {
                    var parsed = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(ignoreGroupsJson);
                    if (parsed != null)
                        _config.IgnoreGroups = parsed;
                }

                SaveConfig();

                TShock.Log.ConsoleInfo("[TSWeb] 权限提升配置已通过 REST API 更新");
                return new RestObject { { "response", "配置已保存" } };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }

        // ═══════════════════════════════════════════════
        // 核心晋升逻辑
        // ═══════════════════════════════════════════════

        public static bool TryPromote(
            UserAccount account,
            string targetGroupName,
            string mode,
            TSPlayer player = null,
            string reason = "")
        {
            if (account == null || string.IsNullOrEmpty(targetGroupName))
                return false;

            EnsureLoaded();

            if (!TShock.Groups.GroupExists(targetGroupName))
            {
                //TShock.Log.ConsoleWarn($"[TSWeb] 晋升失败: 目标组 {targetGroupName} 不存在");
                return false;
            }

            var currentGroup = TShock.Groups.GetGroupByName(account.Group);
            if (currentGroup != null)
            {
                foreach (var ignore in _config.IgnoreGroups)
                {
                    if (string.Equals(currentGroup.Name, ignore, StringComparison.OrdinalIgnoreCase))
                    {
                        //TShock.Log.ConsoleInfo($"[TSWeb] 跳过晋升: 玩家 {account.Name} 属于忽略组 {ignore}");
                        return false;
                    }
                }
            }

            if (string.Equals(mode, "auto", StringComparison.OrdinalIgnoreCase))
            {
                if (currentGroup != null)
                {
                    if (IsGroupAtOrAbove(currentGroup, targetGroupName))
                    {
                        //TShock.Log.ConsoleInfo(
                            //$"[TSWeb] 跳过晋升: 玩家 {account.Name} 的组 {currentGroup.Name} 已达到 {targetGroupName}");
                        return false;
                    }
                }
            }

            try
            {
                TShock.UserAccounts.SetUserGroup(account, targetGroupName);
                //TShock.Log.ConsoleInfo(
                    //$"[TSWeb] 晋升成功 - 玩家:{account.Name} → {targetGroupName} ({reason})");

                if (player != null && player.IsLoggedIn && player.Account?.ID == account.ID)
                {
                    player.SendSuccessMessage($"恭喜！{reason}，已自动晋升为 {targetGroupName}！");
                }

                return true;
            }
            catch (Exception ex)
            {
                //TShock.Log.ConsoleError(
                    //$"[TSWeb] 晋升失败 - 玩家:{account.Name} → {targetGroupName}: {ex.Message}");
                return false;
            }
        }

        private static bool IsGroupAtOrAbove(Group group, string targetGroupName)
        {
            var visited = new HashSet<string>();
            var current = group;

            while (current != null)
            {
                if (!visited.Add(current.Name))
                    break;

                if (string.Equals(current.Name, targetGroupName, StringComparison.OrdinalIgnoreCase))
                    return true;

                current = current.Parent;
            }

            return false;
        }

        public static PromotionMode ParseMode(string mode)
        {
            return string.Equals(mode, "auto", StringComparison.OrdinalIgnoreCase)
                ? PromotionMode.Auto
                : PromotionMode.Force;
        }
    }
}
