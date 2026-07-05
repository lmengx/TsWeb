using Rests;
using System;
using System.Data;
using TShockAPI;
using TShockAPI.DB;

namespace TShockData
{
    public static class QQBind
    {
        private static bool _tableChecked = false;

        public static void Initialize()
        {
            EnsureTable();
        }

        private static void EnsureTable()
        {
            if (_tableChecked)
                return;

            try
            {
                IDbConnection db = TShock.DB;
                db.Query(
                    "CREATE TABLE IF NOT EXISTS qq_bind (" +
                    "Id INTEGER PRIMARY KEY AUTOINCREMENT, " +
                    "UserId INTEGER NOT NULL UNIQUE, " +
                    "QQ TEXT NOT NULL, " +
                    "BindTime TEXT NOT NULL DEFAULT (datetime('now', 'localtime')), " +
                    "UNIQUE(QQ)" +
                    ")"
                );
                _tableChecked = true;
                TShock.Log.ConsoleInfo("[TSWeb] qq_bind 表已就绪");
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 创建 qq_bind 表失败: {ex.Message}");
            }
        }

        /// <summary>
        /// REST API: 绑定QQ到玩家
        /// 入参: player (玩家名), qq (QQ号)
        /// </summary>
        public static object BindQQ(RestRequestArgs args)
        {
            string playerName = null;
            string qq = null;

            try { playerName = args.Parameters["player"]; } catch { }
            try { qq = args.Parameters["qq"]; } catch { }

            if (string.IsNullOrEmpty(playerName))
            {
                return new RestObject("400")
                {
                    { "error", "缺少参数: player" }
                };
            }

            if (string.IsNullOrEmpty(qq))
            {
                return new RestObject("400")
                {
                    { "error", "缺少参数: qq" }
                };
            }

            try
            {
                EnsureTable();

                // 1. 检查玩家是否存在
                var account = TShock.UserAccounts.GetUserAccountByName(playerName);
                if (account == null)
                {
                    return new RestObject("404")
                    {
                        { "error", "玩家不存在" }
                    };
                }

                IDbConnection db = TShock.DB;

                // 2. 检查该玩家是否已被绑定
                using (var res = db.QueryReader("SELECT QQ FROM qq_bind WHERE UserId = @0", account.ID))
                {
                    if (res.Read())
                    {
                        string existingQQ = res.Get<string>("QQ");
                        return new RestObject("409")
                        {
                            { "error", $"该玩家已绑定QQ：{existingQQ}" }
                        };
                    }
                }

                // 3. 检查该QQ是否已被其他玩家绑定
                using (var res = db.QueryReader("SELECT UserId FROM qq_bind WHERE QQ = @0", qq))
                {
                    if (res.Read())
                    {
                        int boundUserId = res.Get<int>("UserId");
                        var boundAccount = TShock.UserAccounts.GetUserAccountByID(boundUserId);
                        string boundName = boundAccount?.Name ?? "未知";
                        return new RestObject("409")
                        {
                            { "error", $"你已经绑定了玩家：{boundName}" }
                        };
                    }
                }

                // 4. 所有检查通过，执行绑定
                db.Query("INSERT INTO qq_bind (UserId, QQ) VALUES (@0, @1)", account.ID, qq);

                TShock.Log.ConsoleInfo($"[TSWeb] QQ绑定成功 - 玩家:{playerName}(ID:{account.ID}), QQ:{qq}");

                return new RestObject()
                {
                    { "response", "绑定成功" },
                    { "player", playerName },
                    { "qq", qq }
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
