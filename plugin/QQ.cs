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

                // 5. 根据配置提升权限组
                TryPromoteByConfig(account, playerName, "QQ绑定");

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

        /// <summary>
        /// REST API: 注册并绑定QQ
        /// 自动创建新角色并绑定QQ，若QQ已绑定、角色已被注册或角色已被绑定则报错
        /// 入参: player (角色名), qq (QQ号)
        /// </summary>
        public static object RegisterAndBind(RestRequestArgs args)
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
                IDbConnection db = TShock.DB;

                // 1. 检查QQ是否已被其他角色绑定
                using (var res = db.QueryReader("SELECT UserId FROM qq_bind WHERE QQ = @0", qq))
                {
                    if (res.Read())
                    {
                        int boundUserId = res.Get<int>("UserId");
                        var boundAccount = TShock.UserAccounts.GetUserAccountByID(boundUserId);
                        string boundName = boundAccount?.Name ?? "未知";
                        return new RestObject("409")
                        {
                            { "error", $"该QQ已绑定其他角色：{boundName}" }
                        };
                    }
                }

                // 2. 检查角色名是否已被注册
                var existingAccount = TShock.UserAccounts.GetUserAccountByName(playerName);
                if (existingAccount != null)
                {
                    // 2a. 检查该角色是否已被绑定QQ
                    using (var res = db.QueryReader("SELECT QQ FROM qq_bind WHERE UserId = @0", existingAccount.ID))
                    {
                        if (res.Read())
                        {
                            string existingQQ = res.Get<string>("QQ");
                            return new RestObject("409")
                            {
                                { "error", $"角色已被绑定，当前QQ：{existingQQ}" }
                            };
                        }
                    }
                    return new RestObject("409")
                    {
                        { "error", "角色名已被注册" }
                    };
                }

                // 3. 创建新角色
                var newAccount = new UserAccount
                {
                    Name = playerName,
                    Group = TShock.Config.Settings.DefaultRegistrationGroupName,
                    UUID = ""
                };
                newAccount.CreateBCryptHash(Guid.NewGuid().ToString());
                TShock.UserAccounts.AddUserAccount(newAccount);

                // 重新获取以获取正确 ID
                newAccount = TShock.UserAccounts.GetUserAccountByName(playerName);
                if (newAccount == null)
                {
                    return new RestObject("500")
                    {
                        { "error", "创建角色失败，未知错误" }
                    };
                }

                // 4. 绑定QQ
                db.Query("INSERT INTO qq_bind (UserId, QQ) VALUES (@0, @1)", newAccount.ID, qq);

                // 5. 根据配置设置权限组
                TryPromoteByConfig(newAccount, playerName, "QQ注册绑定");

                TShock.Log.ConsoleInfo($"[TSWeb] QQ注册绑定成功 - 新角色:{playerName}(ID:{newAccount.ID}), QQ:{qq}");

                return new RestObject()
                {
                    { "response", "注册并绑定成功" },
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

        /// <summary>
        /// REST API: 根据QQ号修改密码
        /// 入参: qq (QQ号), password (新密码)
        /// </summary>
        public static object ResetPasswordByQQ(RestRequestArgs args)
        {
            string qq = null;
            string newPassword = null;

            try { qq = args.Parameters["qq"]; } catch { }
            try { newPassword = args.Parameters["password"]; } catch { }

            if (string.IsNullOrEmpty(qq))
            {
                return new RestObject("400")
                {
                    { "error", "缺少参数: qq" }
                };
            }

            if (string.IsNullOrEmpty(newPassword))
            {
                return new RestObject("400")
                {
                    { "error", "缺少参数: password" }
                };
            }

            try
            {
                EnsureTable();
                IDbConnection db = TShock.DB;

                // 1. 根据QQ查找绑定的用户
                int userId = -1;
                using (var res = db.QueryReader("SELECT UserId FROM qq_bind WHERE QQ = @0", qq))
                {
                    if (res.Read())
                    {
                        userId = res.Get<int>("UserId");
                    }
                }

                if (userId == -1)
                {
                    return new RestObject("404")
                    {
                        { "error", "你还没有绑定角色" }
                    };
                }

                // 2. 获取用户账号
                var account = TShock.UserAccounts.GetUserAccountByID(userId);
                if (account == null)
                {
                    return new RestObject("404")
                    {
                        { "error", "绑定的角色不存在" }
                    };
                }

                // 3. 修改密码 — 使用 TShock 的 SetUserAccountPassword
                account.CreateBCryptHash(newPassword);
                TShock.UserAccounts.SetUserAccountPassword(account, newPassword);

                TShock.Log.ConsoleInfo($"[TSWeb] QQ密码重置成功 - 角色:{account.Name}(ID:{account.ID}), QQ:{qq}");

                return new RestObject()
                {
                    { "response", "密码修改成功" },
                    { "player", account.Name },
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

        /// <summary>
        /// REST API: 根据QQ号查询玩家信息
        /// 返回: 玩家名, 在线时长(小时), 死亡次数, 钓鱼任务次数, 注册时间, 用户组
        /// 入参: qq (QQ号)
        /// </summary>
        public static object QueryPlayerByQQ(RestRequestArgs args)
        {
            string qq = null;
            try { qq = args.Parameters["qq"]; } catch { }

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
                IDbConnection db = TShock.DB;

                // 1. 根据QQ查找绑定的用户
                int userId = -1;
                using (var res = db.QueryReader("SELECT UserId FROM qq_bind WHERE QQ = @0", qq))
                {
                    if (res.Read())
                    {
                        userId = res.Get<int>("UserId");
                    }
                }

                if (userId == -1)
                {
                    return new RestObject("404")
                    {
                        { "error", "该QQ未绑定任何角色" }
                    };
                }

                // 2. 查询用户信息 (Users表)
                string playerName = "";
                string userGroup = "";
                string registered = "";
                using (var res = db.QueryReader(
                    "SELECT Username, Usergroup, Registered FROM Users WHERE ID = @0", userId))
                {
                    if (res.Read())
                    {
                        playerName = res.Get<string>("Username") ?? "";
                        userGroup = res.Get<string>("Usergroup") ?? "";
                        registered = res.Get<string>("Registered") ?? "";
                    }
                }

                if (string.IsNullOrEmpty(playerName))
                {
                    return new RestObject("404")
                    {
                        { "error", "绑定的角色不存在" }
                    };
                }

                // 3. 查询角色数据 (tsCharacter表)
                int deathsPVE = 0;
                int questsCompleted = 0;
                using (var res = db.QueryReader(
                    "SELECT deathsPVE, questsCompleted FROM tsCharacter WHERE Account = @0", userId))
                {
                    if (res.Read())
                    {
                        deathsPVE = res.Get<int>("deathsPVE");
                        questsCompleted = res.Get<int>("questsCompleted");
                    }
                }

                // 4. 查询总在线时长 (player_daily_stat表)
                int totalMinutes = 0;
                using (var res = db.QueryReader(
                    "SELECT COALESCE(SUM(daily_min), 0) AS total_min FROM player_daily_stat WHERE uid = @0",
                    playerName))
                {
                    if (res.Read())
                    {
                        totalMinutes = Convert.ToInt32(res.Get<long>("total_min"));
                    }
                }

                return new RestObject()
                {
                    { "player", playerName },
                    { "qq", qq },
                    { "group", userGroup },
                    { "registered", registered },
                    { "online_minutes", totalMinutes },
                    { "online_hours", Math.Round(totalMinutes / 60.0, 1) },
                    { "deaths", deathsPVE },
                    { "fishing_quests", questsCompleted }
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
