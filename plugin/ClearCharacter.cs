using Rests;
using System;
using System.Data;
using TShockAPI;
using TShockAPI.DB;

namespace TShockData
{
    public class ClearCharacter
    {
        public static object ClearCharacterData(RestRequestArgs args)
        {
            string accountStr = null;
            try
            {
                accountStr = args.Parameters["account"];
            }
            catch
            {
                accountStr = null;
            }

            if (string.IsNullOrEmpty(accountStr) || !int.TryParse(accountStr, out int accountId))
            {
                return new RestObject("400")
                {
                    { "error", "参数 account 必须为有效的数字（用户ID）" }
                };
            }

            try
            {
                IDbConnection db = TShock.DB;

                // 先检查是否存在该角色的数据
                string checkQuery = "SELECT Account FROM tsCharacter WHERE Account = @0";
                bool exists = false;

                using (QueryResult res = db.QueryReader(checkQuery, accountId))
                {
                    exists = res.Read();
                }

                if (!exists)
                {
                    return new RestObject("404")
                    {
                        { "error", "该用户没有角色数据" },
                        { "account", accountId }
                    };
                }

                // 删除角色数据
                string deleteQuery = "DELETE FROM tsCharacter WHERE Account = @0";
                int rowsAffected = db.Query(deleteQuery, accountId);

                TShock.Log.ConsoleInfo($"[TSWeb] 已清空用户 ID={accountId} 的角色数据");

                return new RestObject()
                {
                    { "response", "角色数据已清空" },
                    { "account", accountId },
                    { "rowsAffected", rowsAffected }
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

        public static object ClearAllCharacterData(RestRequestArgs args)
        {
            string username = null;
            string password = null;

            try { username = args.Parameters["username"]; } catch { }
            try { password = args.Parameters["password"]; } catch { }

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                return new RestObject("400")
                {
                    { "error", "参数 username 和 password 为必填" }
                };
            }

            try
            {
                // 验证当前管理员密码
                var account = TShock.UserAccounts.GetUserAccountByName(username);
                if (account == null)
                {
                    return new RestObject("401")
                    {
                        { "error", "用户不存在" }
                    };
                }

                if (!account.VerifyPassword(password))
                {
                    return new RestObject("401")
                    {
                        { "error", "密码验证失败，操作已拒绝" }
                    };
                }

                // 密码验证通过，清空所有角色数据
                IDbConnection db = TShock.DB;

                // 先统计数量
                string countQuery = "SELECT COUNT(*) AS cnt FROM tsCharacter";
                int totalCount = 0;
                using (QueryResult res = db.QueryReader(countQuery))
                {
                    if (res.Read())
                    {
                        totalCount = res.Get<int>("cnt");
                    }
                }

                // 执行清空
                string deleteQuery = "DELETE FROM tsCharacter";
                int rowsAffected = db.Query(deleteQuery);

                TShock.Log.ConsoleInfo($"[TSWeb] 管理员 {username} 已清空全部角色数据，共 {rowsAffected} 条记录");

                return new RestObject()
                {
                    { "response", $"已清空全部角色数据" },
                    { "totalCount", totalCount },
                    { "rowsAffected", rowsAffected }
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
