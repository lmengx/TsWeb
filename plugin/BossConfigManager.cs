using System;
using System.IO;
using Newtonsoft.Json;
using Rests;
using TShockAPI;

namespace TShockData
{
    /// <summary>时间锁解锁计划项：某进度档在开服后第 N 天的 HH:mm 解锁（纯按时间，不看击杀）</summary>
    public class TimeScheduleItem
    {
        [JsonProperty("Name")]
        public string Name { get; set; } = "";

        [JsonProperty("Day")]
        public int Day { get; set; } = 1;

        [JsonProperty("Time")]
        public string Time { get; set; } = "12:00";
    }

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

        // ═══════════════════════════════════════════
        // 进度锁 · 按时间锁模式
        // ═══════════════════════════════════════════

        /// <summary>进度锁模式：killbased（按击杀进度，默认）/ timelock（按时间解锁）</summary>
        [JsonProperty("ProgressLockMode")]
        public string ProgressLockMode { get; set; } = "killbased";

        /// <summary>最近一次记录的地图 ID（无论开关，每次启动都记录；变化视为重新开服）</summary>
        [JsonProperty("WorldId")]
        public string WorldId { get; set; } = "";

        /// <summary>开服时间（ISO yyyy-MM-dd HH:mm:ss）；地图 ID 变化时重置为当前时间，管理员也可手动指定</summary>
        [JsonProperty("ServerStartTime")]
        public string ServerStartTime { get; set; } = "";

        /// <summary>解锁计划（通用表）：档名 → 开服后第 N 天 HH:mm 解锁</summary>
        [JsonProperty("TimeSchedule")]
        public List<TimeScheduleItem> TimeSchedule { get; set; } = new()
        {
            // 默认配置：开服后第二天中午 12:00 开肉山，第三天早上 06:00 开教徒
            new TimeScheduleItem { Name = "血肉墙", Day = 2, Time = "12:00" },
            new TimeScheduleItem { Name = "拜月教教徒", Day = 3, Time = "06:00" }
        };

        /// <summary>时间锁模式下，是否拦截未解锁档 BOSS 的生成/召唤（含自然生成），默认开启</summary>
        [JsonProperty("BlockLockedBossSpawn")]
        public bool BlockLockedBossSpawn { get; set; } = true;
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

                // 进度锁 · 按时间锁
                progressLockMode = Config.ProgressLockMode,
                serverStartTime = Config.ServerStartTime,
                worldId = Config.WorldId,
                blockLockedBossSpawn = Config.BlockLockedBossSpawn,
                timeSchedule = BossTimeLock.GetScheduleStatus(),
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

                // ═══ 进度锁 · 按时间锁 ═══
                var plm = args.Parameters["progressLockMode"];
                if (!string.IsNullOrEmpty(plm))
                {
                    var m = plm.ToLower();
                    if (m == "killbased" || m == "timelock")
                    {
                        Config.ProgressLockMode = m;
                        // 首次切换到时间锁：同步一次世界 ID（若地图已变则重置开服时间）
                        if (m == "timelock")
                            BossTimeLock.SyncWorldId();
                    }
                }
                var sst = args.Parameters["serverStartTime"];
                if (!string.IsNullOrEmpty(sst) && DateTime.TryParse(sst, out var startTime))
                {
                    Config.ServerStartTime = startTime.ToString("yyyy-MM-dd HH:mm:ss");
                    TShock.Log.ConsoleInfo($"[TSWeb] REST 手动指定开服时间: {Config.ServerStartTime}");
                }
                var bls = args.Parameters["blockLockedBossSpawn"];
                if (!string.IsNullOrEmpty(bls))
                    Config.BlockLockedBossSpawn = bls.ToLower() == "true";
                var scheduleJson = args.Parameters["schedule"];
                if (!string.IsNullOrEmpty(scheduleJson))
                {
                    try
                    {
                        var parsed = JsonConvert.DeserializeObject<List<TimeScheduleItem>>(scheduleJson);
                        if (parsed != null)
                        {
                            // 校验：档名非空、Day ≥ 1、Time 为 HH:mm
                            parsed.RemoveAll(s =>
                                string.IsNullOrWhiteSpace(s.Name) ||
                                s.Day < 1 ||
                                !TimeSpan.TryParse(s.Time, out _));
                            Config.TimeSchedule = parsed;
                            TShock.Log.ConsoleInfo($"[TSWeb] REST 更新时间锁解锁计划: {Config.TimeSchedule.Count} 项");
                        }
                    }
                    catch (Exception ex)
                    {
                        TShock.Log.ConsoleError($"[TSWeb] REST 解析 schedule 失败: {ex.Message}");
                    }
                }

                SaveConfig();
                // 解锁时间缓存重算（计划/开服时间变化后立即生效）
                BossTimeLock.RebuildCache();
                TShock.Log.ConsoleInfo($"[TSWeb] REST 更新Boss配置: mode={Config.BossLimitMode}, minPlayers={Config.BossLimitMinPlayers}, quitLimit={Config.QuitLimitEnabled}, lateComp={Config.LateCompEnabled}, progressLock={Config.ProgressLockMode}");
                return new { status = "200", message = "配置已保存" };
            }
            catch (Exception ex)
            {
                return new { status = "500", error = ex.Message };
            }
        }
    }
}
