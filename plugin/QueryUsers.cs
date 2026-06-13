﻿﻿﻿﻿﻿﻿﻿using Rests;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;
using Newtonsoft.Json;

namespace TShockData
{
    public class QueryUsers
    {
        public static object QueryUsersList(RestRequestArgs args)
        {
            string username = null;
            try
            {
                username = args.Parameters["username"];
            }
            catch
            {
                username = null;
            }
            
            try
            {
                IDbConnection db = TShock.DB;
                List<Dictionary<string, object>> users = new List<Dictionary<string, object>>();
                
                string query;
                object[] parameters;
                
                if (!string.IsNullOrEmpty(username))
                {
                    query = "SELECT * FROM Users WHERE Username LIKE @0";
                    parameters = new object[] { "%" + username + "%" };
                }
                else
                {
                    query = "SELECT * FROM Users";
                    parameters = new object[] { };
                }
                
                using (QueryResult res = db.QueryReader(query, parameters))
                {
                    while (res.Read())
                    {
                        Dictionary<string, object> user = new Dictionary<string, object>();
                        user.Add("ID", res.Get<int>("ID"));
                        user.Add("Username", res.Get<string>("Username"));
                        user.Add("Password", res.Get<string>("Password"));
                        user.Add("UUID", res.Get<string>("UUID"));
                        user.Add("Usergroup", res.Get<string>("Usergroup"));
                        user.Add("Registered", res.Get<string>("Registered"));
                        user.Add("LastAccessed", res.Get<string>("LastAccessed"));
                        user.Add("KnownIPs", res.Get<string>("KnownIPs"));
                        users.Add(user);
                    }
                }
                
                return new RestObject()
                {
                    { "users", users }
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

        public static object QueryDuplicateIPs(RestRequestArgs args)
        {
            string username = null;
            try
            {
                username = args.Parameters["username"];
            }
            catch
            {
                username = null;
            }
            
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
                
                string targetUserQuery = "SELECT ID, KnownIPs FROM Users WHERE Username = @0";
                using (QueryResult targetRes = db.QueryReader(targetUserQuery, username))
                {
                    if (!targetRes.Read())
                    {
                        return new RestObject("404")
                        {
                            { "error", "User not found" }
                        };
                    }
                    
                    int targetUserId = targetRes.Get<int>("ID");
                    string targetIPsJson = targetRes.Get<string>("KnownIPs");
                    
                    List<string> targetIPs = new List<string>();
                    if (!string.IsNullOrEmpty(targetIPsJson))
                    {
                        try
                        {
                            targetIPs = JsonConvert.DeserializeObject<List<string>>(targetIPsJson) ?? new List<string>();
                        }
                        catch
                        {
                            return new RestObject("500")
                            {
                                { "error", "Failed to parse target user IPs" }
                            };
                        }
                    }
                    
                    if (targetIPs.Count == 0)
                    {
                        return new RestObject()
                        {
                            { "message", "No IPs found for target user" },
                            { "duplicates", new List<object>() }
                        };
                    }
                    
                    string allUsersQuery = "SELECT ID, Username, KnownIPs FROM Users WHERE ID != @0";
                    List<Dictionary<string, object>> duplicates = new List<Dictionary<string, object>>();
                    
                    using (QueryResult res = db.QueryReader(allUsersQuery, targetUserId))
                    {
                        while (res.Read())
                        {
                            int userId = res.Get<int>("ID");
                            string userUsername = res.Get<string>("Username");
                            string userIPsJson = res.Get<string>("KnownIPs");
                            
                            List<string> userIPs = new List<string>();
                            if (!string.IsNullOrEmpty(userIPsJson))
                            {
                                try
                                {
                                    userIPs = JsonConvert.DeserializeObject<List<string>>(userIPsJson) ?? new List<string>();
                                }
                                catch
                                {
                                    continue;
                                }
                            }
                            
                            bool hasCommonIP = targetIPs.Any(ip => userIPs.Contains(ip));
                            if (hasCommonIP)
                            {
                                Dictionary<string, object> duplicate = new Dictionary<string, object>();
                                duplicate.Add("ID", userId);
                                duplicate.Add("Username", userUsername);
                                duplicates.Add(duplicate);
                            }
                        }
                    }
                    
                    return new RestObject()
                    {
                        { "targetUser", username },
                        { "targetIPs", targetIPs },
                        { "duplicates", duplicates },
                        { "count", duplicates.Count }
                    };
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

        public static object BanPlayerByNameorID(RestRequestArgs args)
        {
            string name = null;
            string id = null;
            string reason = "不当行为";

            try
            {
                name = args.Parameters["name"];
            }
            catch { }

            try
            {
                id = args.Parameters["id"];
            }
            catch { }

            try
            {
                reason = args.Parameters["reason"];
            }
            catch { }

            bool hasName = !string.IsNullOrEmpty(name);
            bool hasId = !string.IsNullOrEmpty(id);

            if ((hasName && hasId) || (!hasName && !hasId))
            {
                return new RestObject("400")
                {
                    { "error", "必须且只能指定 name 或 id 参数" }
                };
            }

            try
            {
                IDbConnection db = TShock.DB;
                string query;
                object[] parameters;

                if (hasName)
                {
                    query = "SELECT ID, Username, UUID, KnownIPs FROM Users WHERE Username = @0";
                    parameters = new object[] { name };
                }
                else
                {
                    query = "SELECT ID, Username, UUID, KnownIPs FROM Users WHERE ID = @0";
                    parameters = new object[] { int.Parse(id) };
                }

                string username = null;
                string uuid = null;
                List<string> ipList = new List<string>();

                using (QueryResult res = db.QueryReader(query, parameters))
                {
                    if (!res.Read())
                    {
                        return new RestObject("404")
                        {
                            { "error", "用户不存在" }
                        };
                    }

                    username = res.Get<string>("Username");
                    uuid = res.Get<string>("UUID");
                    string knownIPsJson = res.Get<string>("KnownIPs");

                    if (!string.IsNullOrEmpty(knownIPsJson))
                    {
                        try
                        {
                            ipList = JsonConvert.DeserializeObject<List<string>>(knownIPsJson) ?? new List<string>();
                        }
                        catch
                        {
                            return new RestObject("500")
                            {
                                { "error", "解析用户IP列表失败" }
                            };
                        }
                    }
                }

                TShockAPI.Commands.HandleCommand(TShockAPI.TSPlayer.Server, $"/ban add \"acc:{username}\" \"{reason}\" -e");

                if (!string.IsNullOrEmpty(uuid))
                {
                    TShockAPI.Commands.HandleCommand(TShockAPI.TSPlayer.Server, $"/ban add \"uuid:{uuid}\" \"{reason}\" -e");
                }

                foreach (string ip in ipList)
                {
                    if (!string.IsNullOrEmpty(ip))
                    {
                        TShockAPI.Commands.HandleCommand(TShockAPI.TSPlayer.Server, $"/ban add \"ip:{ip}\" \"{reason}\" -e");
                    }
                }

                return new RestObject()
                {
                    { "response", "封禁成功" },
                    { "username", username },
                    { "uuid", uuid ?? "无" },
                    { "bannedIPs", ipList.Count },
                    { "reason", reason }
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