using Rests;
using System;
using System.Collections.Generic;
using System.Data;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;

namespace TShockData
{
    public static class OnlineData
    {
        private static long _tickCounter = 0;
        private static bool _initialized = false;
        private static TerrariaPlugin _plugin = null;

        public static void Initialize(TerrariaPlugin plugin)
        {
            if (_initialized) return;
            _initialized = true;
            _plugin = plugin;

            CreateTables();
            ServerApi.Hooks.GameUpdate.Register(_plugin, OnGameUpdate);
        }

        public static void Dispose()
        {
            if (!_initialized) return;
            ServerApi.Hooks.GameUpdate.Deregister(_plugin, OnGameUpdate);
            _initialized = false;
            _plugin = null;
        }

        private static void CreateTables()
        {
            TShock.DB.Query(@"
                CREATE TABLE IF NOT EXISTS player_daily_stat (
                    uid TEXT NOT NULL,
                    date TEXT NOT NULL,
                    daily_min INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY (uid, date)
                );
            ");
            TShock.DB.Query(@"
                CREATE INDEX IF NOT EXISTS idx_pds_uid ON player_daily_stat(uid);
            ");
            TShock.DB.Query(@"
                CREATE INDEX IF NOT EXISTS idx_pds_date ON player_daily_stat(date);
            ");

            TShock.DB.Query(@"
                CREATE TABLE IF NOT EXISTS hourly_online_snapshot (
                    hour_ts INTEGER PRIMARY KEY,
                    online_names TEXT NOT NULL DEFAULT ''
                );
            ");
            TShock.DB.Query(@"
                CREATE INDEX IF NOT EXISTS idx_hour_ts ON hourly_online_snapshot(hour_ts);
            ");

            TShock.Log.ConsoleInfo("[TSWeb] 在线统计表已创建/确认");
        }

        public static int CurrentHourTs()
        {
            var now = DateTime.Now;
            return now.Year * 1000000 + now.Month * 10000 + now.Day * 100 + now.Hour;
        }

        private static void OnGameUpdate(EventArgs args)
        {
            _tickCounter++;
            if (_tickCounter % 3600 == 0)
            {
                UpdatePlayerDailyMin();
                EnsureHourlyRecord();
            }
            if (_tickCounter % 216000 == 0)
            {
                SnapshotHourlyOnline();
            }
        }

        /// <summary>
        /// 确保当前小时在 hourly_online_snapshot 中有记录。
        /// 每分钟调用，收集所有已登录在线玩家名写入当前小时记录。
        /// 
        /// 不用 ServerJoin 的原因：ServerJoin 在玩家建立 TCP 连接时触发，
        /// 此时 player.IsLoggedIn 为 false，无法获取已登录玩家列表。
        /// </summary>
        private static void EnsureHourlyRecord()
        {
            try
            {
                var nameList = new List<string>();

                foreach (var player in TShock.Players)
                {
                    if (player != null && player.Active && player.IsLoggedIn)
                    {
                        nameList.Add(player.Name);
                    }
                }

                int hourTs = CurrentHourTs();
                string onlineNames = string.Join(" ", nameList);
                TShock.DB.Query(
                    "INSERT INTO hourly_online_snapshot (hour_ts, online_names) VALUES (@0, @1) " +
                    "ON CONFLICT(hour_ts) DO UPDATE SET online_names = @1",
                    hourTs, onlineNames);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] EnsureHourlyRecord error: {ex.Message}");
            }
        }

        private static void UpdatePlayerDailyMin()
        {
            try
            {
                string today = DateTime.Now.ToString("yyyy-MM-dd");
                var activePlayers = new List<string>();

                foreach (var player in TShock.Players)
                {
                    if (player != null && player.Active && player.IsLoggedIn)
                    {
                        activePlayers.Add(player.Name);
                    }
                }

                if (activePlayers.Count == 0) return;

                foreach (var name in activePlayers)
                {
                    TShock.DB.Query(
                        "INSERT INTO player_daily_stat (uid, date, daily_min) VALUES (@0, @1, 1) " +
                        "ON CONFLICT(uid, date) DO UPDATE SET daily_min = daily_min + 1",
                        name, today);
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] UpdatePlayerDailyMin error: {ex.Message}");
            }
        }

        private static void SnapshotHourlyOnline()
        {
            try
            {
                int hourTs = CurrentHourTs();
                var nameList = new List<string>();

                foreach (var player in TShock.Players)
                {
                    if (player != null && player.Active && player.IsLoggedIn)
                    {
                        nameList.Add(player.Name);
                    }
                }

                string onlineNames = string.Join(" ", nameList);
                TShock.DB.Query(
                    "INSERT INTO hourly_online_snapshot (hour_ts, online_names) VALUES (@0, @1) " +
                    "ON CONFLICT(hour_ts) DO UPDATE SET online_names = @1",
                    hourTs, onlineNames);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] SnapshotHourlyOnline error: {ex.Message}");
            }
        }

        /// <summary>
        /// 安全获取可选参数值，若不存在或解析失败返回默认值
        /// </summary>
        private static int GetOptionalIntParam(RestRequestArgs args, string key, int defaultValue)
        {
            try
            {
                string val = args.Parameters[key];
                if (int.TryParse(val, out int result))
                    return result;
            }
            catch { }
            return defaultValue;
        }

        public static object GetHourlyOnline(RestRequestArgs args)
        {
            try
            {
                string date;
                try
                {
                    date = args.Parameters["date"];
                }
                catch
                {
                    return new RestObject("400")
                    {
                        { "error", "date parameter is required (yyyy-MM-dd)" }
                    };
                }

                DateTime parsedDate;
                if (!DateTime.TryParseExact(date, "yyyy-MM-dd", null,
                    System.Globalization.DateTimeStyles.None, out parsedDate))
                {
                    return new RestObject("400")
                    {
                        { "error", "Invalid date format, use yyyy-MM-dd" }
                    };
                }

                int startTs = parsedDate.Year * 1000000 + parsedDate.Month * 10000 + parsedDate.Day * 100;
                int endTs = startTs + 23;

                var hours = new List<Dictionary<string, object>>();

                using (var reader = TShock.DB.QueryReader(
                    "SELECT hour_ts, online_names FROM hourly_online_snapshot " +
                    "WHERE hour_ts >= @0 AND hour_ts <= @1 ORDER BY hour_ts",
                    startTs, endTs))
                {
                    while (reader.Read())
                    {
                        int hourTs = reader.Get<int>("hour_ts");
                        string onlineNames = reader.Get<string>("online_names") ?? "";

                        var nameList = new List<string>(
                            onlineNames.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

                        hours.Add(new Dictionary<string, object>
                        {
                            { "hour_ts", hourTs },
                            { "online_players", nameList }
                        });
                    }
                }

                return new RestObject
                {
                    { "date", date },
                    { "hours", hours }
                };
            }
            catch (Exception ex)
            {
                return new RestObject("500")
                {
                    { "error", ex.Message }
                };
            }
        }

        public static object GetRanking(RestRequestArgs args)
        {
            try
            {
                int days = GetOptionalIntParam(args, "days", 30);
                if (days <= 0) days = 30;

                string since = DateTime.Now.AddDays(-days).ToString("yyyy-MM-dd");

                var ranking = new List<Dictionary<string, object>>();

                using (var reader = TShock.DB.QueryReader(
                    "SELECT uid, SUM(daily_min) AS total_min FROM player_daily_stat " +
                    "WHERE date >= @0 GROUP BY uid ORDER BY total_min DESC LIMIT 100",
                    since))
                {
                    while (reader.Read())
                    {
                        string uid = reader.Get<string>("uid");
                        int totalMin = 0;
                        try { totalMin = reader.Get<int>("total_min"); } catch { }

                        ranking.Add(new Dictionary<string, object>
                        {
                            { "uid", uid },
                            { "total_min", totalMin }
                        });
                    }
                }

                return new RestObject
                {
                    { "days", days },
                    { "since", since },
                    { "ranking", ranking }
                };
            }
            catch (Exception ex)
            {
                return new RestObject("500")
                {
                    { "error", ex.Message }
                };
            }
        }

        public static object GetPlayerCalendar(RestRequestArgs args)
        {
            try
            {
                string name;
                try
                {
                    name = args.Parameters["name"];
                }
                catch
                {
                    return new RestObject("400") { { "error", "name parameter is required" } };
                }

                if (string.IsNullOrEmpty(name))
                {
                    return new RestObject("400") { { "error", "name parameter is required" } };
                }

                int year = GetOptionalIntParam(args, "year", DateTime.Now.Year);

                string since = $"{year}-01-01";
                string until = $"{year}-12-31";

                var days = new List<Dictionary<string, object>>();
                int totalMin = 0;

                using (var reader = TShock.DB.QueryReader(
                    "SELECT date, daily_min FROM player_daily_stat " +
                    "WHERE uid = @0 AND date >= @1 AND date <= @2 ORDER BY date",
                    name, since, until))
                {
                    while (reader.Read())
                    {
                        string date = reader.Get<string>("date");
                        int dailyMin = 0;
                        try { dailyMin = reader.Get<int>("daily_min"); } catch { }

                        totalMin += dailyMin;
                        days.Add(new Dictionary<string, object>
                        {
                            { "date", date },
                            { "daily_min", dailyMin }
                        });
                    }
                }

                return new RestObject
                {
                    { "player", name },
                    { "year", year },
                    { "days", days },
                    { "total_min", totalMin }
                };
            }
            catch (Exception ex)
            {
                return new RestObject("500")
                {
                    { "error", ex.Message }
                };
            }
        }
    }
}
