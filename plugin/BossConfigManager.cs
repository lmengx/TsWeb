using System;
using System.IO;
using Newtonsoft.Json;
using Rests;
using TShockAPI;

namespace TShockData
{
    public class BossConfig
    {
        [JsonProperty("Boss限制模式")]
        public string BossLimitMode { get; set; } = "disabled";

        [JsonProperty("BOSS限制")]
        public bool BossLimitEnabled { get; set; } = false;

        [JsonProperty("新BOSS召唤最低人数")]
        public int BossLimitMinPlayers { get; set; } = 7;

        [JsonProperty("QuitLimitEnabled")]
        public bool QuitLimitEnabled { get; set; } = false;

        [JsonProperty("LateCompEnabled")]
        public bool LateCompEnabled { get; set; } = false;
    }

    public static class BossConfigManager
    {
        public static BossConfig Config { get; private set; } = new BossConfig();
        private static string ConfigPath => Path.Combine(TShock.SavePath, "TSWeb", "boss_config.json");
        private static bool _loaded;

        public static void LoadConfig()
        {
            if (_loaded) return;
            _loaded = true;

            try
            {
                var directory = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    Config = JsonConvert.DeserializeObject<BossConfig>(json) ?? new BossConfig();
                }
                else
                {
                    Config = new BossConfig();
                    SaveConfig();
                }

                TShock.Log.ConsoleInfo($"[TSWeb] Boss配置已加载 - 召唤限制:{(Config.BossLimitEnabled ? Config.BossLimitMode : "关闭")}, 退出惩罚:{(Config.QuitLimitEnabled ? "开启" : "关闭")}, 晚入补偿:{(Config.LateCompEnabled ? "开启" : "关闭")}");
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 加载Boss配置失败: {ex.Message}");
                Config = new BossConfig();
            }
        }

        public static void SaveConfig()
        {
            try
            {
                var directory = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var json = JsonConvert.SerializeObject(Config, Formatting.Indented);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 保存Boss配置失败: {ex.Message}");
            }
        }

        public static BossConfig GetConfig()
        {
            if (!_loaded) LoadConfig();
            return Config;
        }

        // ═══════════════════════════════════════════
        // REST API
        // ═══════════════════════════════════════════

        public static object GetConfigJson(RestRequestArgs args)
        {
            return new
            {
                status = "200",
                bossLimitMode = Config.BossLimitMode,
                bossLimitEnabled = Config.BossLimitEnabled,
                bossLimitMinPlayers = Config.BossLimitMinPlayers,
                quitLimitEnabled = Config.QuitLimitEnabled,
                lateCompEnabled = Config.LateCompEnabled,
            };
        }

        public static object SetConfigJson(RestRequestArgs args)
        {
            try
            {
                var blm = args.Parameters["bossLimitMode"];
                if (!string.IsNullOrEmpty(blm))
                {
                    var m = blm.ToLower();
                    if (m == "disabled" || m == "playerlimit" || m == "killrequired")
                    {
                        Config.BossLimitMode = m;
                        Config.BossLimitEnabled = m != "disabled";
                    }
                }
                var ble = args.Parameters["bossLimitEnabled"];
                if (!string.IsNullOrEmpty(ble))
                    Config.BossLimitEnabled = ble.ToLower() == "true";
                var blmp = args.Parameters["bossLimitMinPlayers"];
                if (!string.IsNullOrEmpty(blmp) && int.TryParse(blmp, out var num) && num > 0)
                    Config.BossLimitMinPlayers = num;
                var qle = args.Parameters["quitLimitEnabled"];
                if (!string.IsNullOrEmpty(qle))
                    Config.QuitLimitEnabled = qle.ToLower() == "true";
                var lce = args.Parameters["lateCompEnabled"];
                if (!string.IsNullOrEmpty(lce))
                    Config.LateCompEnabled = lce.ToLower() == "true";

                SaveConfig();
                TShock.Log.ConsoleInfo($"[TSWeb] REST 更新Boss配置: mode={Config.BossLimitMode}, minPlayers={Config.BossLimitMinPlayers}, quitLimit={Config.QuitLimitEnabled}, lateComp={Config.LateCompEnabled}");
                return new { status = "200", message = "配置已保存" };
            }
            catch (Exception ex)
            {
                return new { status = "500", error = ex.Message };
            }
        }
    }
}
