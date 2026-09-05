using Rests;
using System;
using System.Globalization;
using TShockAPI;
using TShockAPI.DB;

namespace TShockData
{
    /// <summary>
    /// QQ 绑定辅助（后端台账为权威，本插件不再持有 qq_bind 表）。
    /// 保留的后端调用方接口：
    ///   - /data/qq/find-account  （后端 /api/bot/bind 绑定流程：查角色是否存在 + 密码哈希 + UUID 真值）
    ///   - /data/qq/player-data   （后端「我的信息」主服游戏数据）
    /// 登录晋升识别统一走 AccountSync 台账快照（IsQqBound），与风控豁免同源。
    /// </summary>
    public static class QQBind
    {
        /// <summary>
        /// REST API: 绑定流程查询账号（后端 /api/bot/bind 广播调用）
        /// 返回本地是否存在该角色名、密码哈希与本地数据库 UUID 真值
        /// 入参: name (角色名)
        /// </summary>
        public static object FindAccount(RestRequestArgs args)
        {
            string name = null;
            try { name = args.Parameters["name"]; } catch { }

            if (string.IsNullOrEmpty(name))
            {
                return new RestObject("400") { { "error", "缺少参数: name" } };
            }

            try
            {
                var account = TShock.UserAccounts.GetUserAccountByName(name);
                if (account == null)
                {
                    return new RestObject() { { "found", false } };
                }

                return new RestObject()
                {
                    { "found", true },
                    { "passwordHash", account.Password },
                    { "uuid", AccountSync.GetUuid(name) },
                    { "group", account.Group }
                };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }

        /// <summary>
        /// REST API: 按用户名查询玩家游戏数据（后端「我的信息」主服数据来源）
        /// 返回: found, player, group, registered, deaths, fishing_quests
        /// 入参: name (用户名)
        /// </summary>
        public static object PlayerData(RestRequestArgs args)
        {
            string name = null;
            try { name = args.Parameters["name"]; } catch { }

            if (string.IsNullOrEmpty(name))
            {
                return new RestObject("400") { { "error", "缺少参数: name" } };
            }

            try
            {
                var account = TShock.UserAccounts.GetUserAccountByName(name);
                if (account == null)
                {
                    return new RestObject() { { "found", false } };
                }

                // 用户组 / 注册时间
                string userGroup = "";
                string registeredRaw = "";
                using (var res = TShock.DB.QueryReader(
                    "SELECT Username, Usergroup, Registered FROM Users WHERE ID = @0", account.ID))
                {
                    if (res.Read())
                    {
                        userGroup = res.Get<string>("Usergroup") ?? "";
                        registeredRaw = res.Get<string>("Registered") ?? "";
                    }
                }

                // 死亡次数 / 钓鱼任务
                int deathsPVE = 0;
                int questsCompleted = 0;
                using (var res = TShock.DB.QueryReader(
                    "SELECT deathsPVE, questsCompleted FROM tsCharacter WHERE Account = @0", account.ID))
                {
                    if (res.Read())
                    {
                        deathsPVE = res.Get<int>("deathsPVE");
                        questsCompleted = res.Get<int>("questsCompleted");
                    }
                }

                return new RestObject()
                {
                    { "found", true },
                    { "player", account.Name },
                    { "group", userGroup },
                    { "registered", FormatLocalTime(registeredRaw) },
                    { "deaths", deathsPVE },
                    { "fishing_quests", questsCompleted }
                };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }

        /// <summary>
        /// 将数据库中的注册时间字符串转为服务器本地时间
        /// 兼容格式: ISO8601 (2026-06-23T11:49:11) 和 TShock 默认格式 (2026-06-23 11:49:11)
        /// </summary>
        private static string FormatLocalTime(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;

            DateTime dt;
            // 尝试 ISO 格式
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out dt))
            {
                return dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            }
            // 尝试 TShock 默认格式
            if (DateTime.TryParseExact(raw, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            {
                return dt.ToString("yyyy-MM-dd HH:mm:ss");
            }
            return raw;
        }

        /// <summary>
        /// 玩家登录时识别一次：若其账号在后端 QQ 台账中已有绑定数据，且 qqBind 晋升启用，则按配置晋升。
        /// （QQ 机器人绑定已迁移到后端 /api/bot/*，插件经 /tsweb/qqsync 台账快照识别绑定关系）
        /// </summary>
        public static void TryPromoteOnLogin(TSPlayer player)
        {
            if (player == null || !player.IsLoggedIn || player.Account == null) return;

            var account = player.Account;

            // 后端台账快照中无该账号的 QQ 绑定数据 → 跳过
            if (!AccountSync.IsQqBound(account.Name)) return;

            var config = PromotionManager.GetConfig();
            if (!config.QqBind.Enabled) return;   // 未启用不打扰

            // TryPromote 幂等：auto 模式沿父组链检查，已达目标组 / 命中忽略组会自动跳过
            PromotionManager.TryPromote(
                account,
                config.QqBind.TargetGroup,
                config.QqBind.Mode,
                player,
                "QQ绑定登录识别");
        }

        /// <summary>
        /// 根据权限提升配置执行晋升
        /// </summary>
        private static void TryPromoteByConfig(UserAccount account, string playerName, string source)
        {
            var config = PromotionManager.GetConfig();

            if (!config.QqBind.Enabled)
            {
                TShock.Log.ConsoleInfo($"[TSWeb] {source}: 权限提升已禁用，跳过");
                return;
            }

            PromotionManager.TryPromote(
                account,
                config.QqBind.TargetGroup,
                config.QqBind.Mode,
                reason: $"{source}自动晋升");
        }
    }
}
