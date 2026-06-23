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
                IDbConnection db = TShock.DB;
                string query = "SELECT Password, Usergroup FROM Users WHERE Username = @0";

                using (QueryResult res = db.QueryReader(query, username))
                {
                    if (res.Read())
                    {
                        return new RestObject()
                        {
                            { "username", username },
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