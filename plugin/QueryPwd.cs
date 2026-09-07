using Rests;
using System;
using System.Data;
using TShockAPI;
using TShockAPI.DB;

namespace TShockData
{
    public class QueryPwd
    {
        public static object GetUserPassword(RestRequestArgs args)
        {
            string username = null;
            try
            {
                username = args.Parameters["username"];
            }
            catch { }

            if (string.IsNullOrEmpty(username))
            {
                return new RestObject("400")
                {
                    { "error", "username parameter is required" }
                };
            }

            try
            {
                // 统一大小写匹配规则：先精确后大小写不敏感兜底，并用数据库真实账号名回显
                var account = UserAccountHelper.FindUserAccountByName(username);
                if (account == null)
                {
                    return new RestObject("404")
                    {
                        { "error", "User not found" }
                    };
                }

                IDbConnection db = TShock.DB;
                string query = "SELECT Password, Usergroup FROM Users WHERE ID = @0";

                using (QueryResult res = db.QueryReader(query, account.ID))
                {
                    if (res.Read())
                    {
                        return new RestObject()
                        {
                            { "username", account.Name },
                            { "password", res.Get<string>("Password") },
                            { "usergroup", res.Get<string>("Usergroup") }
                        };
                    }
                    else
                    {
                        return new RestObject("404")
                        {
                            { "error", "User not found" }
                        };
                    }
                }
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