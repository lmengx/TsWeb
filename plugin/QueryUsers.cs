﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using Rests;
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
                List<Dictionary<string, object>> allUsers = new List<Dictionary<string, object>>();
                int targetIndex = -1;

                string query = "SELECT ID, Username, UUID, KnownIPs FROM Users";
                using (QueryResult res = db.QueryReader(query))
                {
                    int index = 0;
                    while (res.Read())
                    {
                        string userUsername = res.Get<string>("Username");
                        Dictionary<string, object> user = new Dictionary<string, object>
                        {
                            { "id", res.Get<int>("ID") },
                            { "username", userUsername },
                            { "uuid", res.Get<string>("UUID") ?? "" },
                            { "knownIPs", res.Get<string>("KnownIPs") ?? "" }
                        };
                        allUsers.Add(user);
                        
                        if (userUsername.Equals(username, StringComparison.OrdinalIgnoreCase))
                        {
                            targetIndex = index;
                        }
                        index++;
                    }
                }

                if (targetIndex == -1)
                {
                    return new RestObject("404")
                    {
                        { "error", "User not found" }
                    };
                }

                Dictionary<string, List<int>> ipToUsers = new Dictionary<string, List<int>>();
                Dictionary<string, List<int>> uuidToUsers = new Dictionary<string, List<int>>();

                for (int i = 0; i < allUsers.Count; i++)
                {
                    var user = allUsers[i];
                    string uuid = user["uuid"].ToString();
                    string knownIPsJson = user["knownIPs"].ToString();

                    if (!string.IsNullOrEmpty(uuid))
                    {
                        if (!uuidToUsers.ContainsKey(uuid))
                            uuidToUsers[uuid] = new List<int>();
                        uuidToUsers[uuid].Add(i);
                    }

                    List<string> ips = new List<string>();
                    if (!string.IsNullOrEmpty(knownIPsJson))
                    {
                        try
                        {
                            ips = JsonConvert.DeserializeObject<List<string>>(knownIPsJson) ?? new List<string>();
                        }
                        catch { }
                    }

                    foreach (string ip in ips)
                    {
                        if (!string.IsNullOrEmpty(ip))
                        {
                            if (!ipToUsers.ContainsKey(ip))
                                ipToUsers[ip] = new List<int>();
                            ipToUsers[ip].Add(i);
                        }
                    }
                }

                int[] parent = new int[allUsers.Count];
                for (int i = 0; i < parent.Length; i++) parent[i] = i;

                Func<int, int> find = null;
                find = (int x) => {
                    if (parent[x] != x) parent[x] = find(parent[x]);
                    return parent[x];
                };

                Action<int, int> union = (int x, int y) => {
                    int px = find(x);
                    int py = find(y);
                    if (px != py) parent[px] = py;
                };

                foreach (var kvp in ipToUsers)
                {
                    List<int> users = kvp.Value;
                    for (int i = 1; i < users.Count; i++)
                    {
                        union(users[0], users[i]);
                    }
                }

                foreach (var kvp in uuidToUsers)
                {
                    List<int> users = kvp.Value;
                    for (int i = 1; i < users.Count; i++)
                    {
                        union(users[0], users[i]);
                    }
                }

                int targetRoot = find(targetIndex);
                List<Dictionary<string, object>> duplicates = new List<Dictionary<string, object>>();
                HashSet<string> sharedIPs = new HashSet<string>();

                for (int i = 0; i < allUsers.Count; i++)
                {
                    if (find(i) == targetRoot && i != targetIndex)
                    {
                        var user = allUsers[i];
                        duplicates.Add(new Dictionary<string, object>
                        {
                            { "ID", user["id"] },
                            { "Username", user["username"] }
                        });
                        
                        string knownIPsJson = user["knownIPs"].ToString();
                        List<string> ips = new List<string>();
                        if (!string.IsNullOrEmpty(knownIPsJson))
                        {
                            try
                            {
                                ips = JsonConvert.DeserializeObject<List<string>>(knownIPsJson) ?? new List<string>();
                            }
                            catch { }
                        }
                        foreach (string ip in ips)
                        {
                            if (!string.IsNullOrEmpty(ip))
                                sharedIPs.Add(ip);
                        }
                    }
                }

                var targetUser = allUsers[targetIndex];
                string targetKnownIPsJson = targetUser["knownIPs"].ToString();
                List<string> targetIPs = new List<string>();
                if (!string.IsNullOrEmpty(targetKnownIPsJson))
                {
                    try
                    {
                        targetIPs = JsonConvert.DeserializeObject<List<string>>(targetKnownIPsJson) ?? new List<string>();
                    }
                    catch { }
                }
                foreach (string ip in targetIPs)
                {
                    if (!string.IsNullOrEmpty(ip))
                        sharedIPs.Add(ip);
                }

                return new RestObject()
                {
                    { "targetUser", username },
                    { "targetIPs", targetIPs },
                    { "duplicates", duplicates },
                    { "count", duplicates.Count },
                    { "sharedIPs", sharedIPs.ToList() },
                    { "totalAccounts", duplicates.Count + 1 }
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

        public static object UnbanPlayer(RestRequestArgs args)
        {
            string ticketStr = null;
            bool fullDelete = true;

            try
            {
                ticketStr = args.Parameters["ticket"];
            }
            catch { }

            try
            {
                string fd = args.Parameters["fullDelete"];
                if (!string.IsNullOrEmpty(fd))
                    fullDelete = bool.Parse(fd);
            }
            catch { }

            if (string.IsNullOrEmpty(ticketStr))
            {
                return new RestObject("400")
                {
                    { "error", "ticket 参数是必填的（封票据编号）" }
                };
            }

            if (!int.TryParse(ticketStr, out int ticketNumber))
            {
                return new RestObject("400")
                {
                    { "error", "ticket 必须是有效的数字" }
                };
            }

            try
            {
                // 直接调用 TShock.Bans.RemoveBan，绕过损坏的 /ban del 命令
                bool success = TShock.Bans.RemoveBan(ticketNumber, fullDelete);

                if (success)
                {
                    return new RestObject()
                    {
                        { "response", $"封禁令 #{ticketNumber} 已{(fullDelete ? "彻底删除" : "标记过期")}" },
                        { "ticket", ticketNumber },
                        { "fullDelete", fullDelete }
                    };
                }
                else
                {
                    return new RestObject("404")
                    {
                        { "error", $"未找到票号为 #{ticketNumber} 的封禁记录" }
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

        public static object QueryAllDuplicateIPs(RestRequestArgs args)
        {
            try
            {
                IDbConnection db = TShock.DB;
                List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();

                string query = "SELECT ID, Username, UUID, KnownIPs FROM Users";
                List<Dictionary<string, object>> allUsers = new List<Dictionary<string, object>>();

                using (QueryResult res = db.QueryReader(query))
                {
                    while (res.Read())
                    {
                        Dictionary<string, object> user = new Dictionary<string, object>
                        {
                            { "id", res.Get<int>("ID") },
                            { "username", res.Get<string>("Username") },
                            { "uuid", res.Get<string>("UUID") ?? "" },
                            { "knownIPs", res.Get<string>("KnownIPs") ?? "" }
                        };
                        allUsers.Add(user);
                    }
                }

                Dictionary<string, List<int>> ipToUsers = new Dictionary<string, List<int>>();
                Dictionary<string, List<int>> uuidToUsers = new Dictionary<string, List<int>>();

                for (int i = 0; i < allUsers.Count; i++)
                {
                    var user = allUsers[i];
                    int userId = (int)user["id"];
                    string uuid = user["uuid"].ToString();
                    string knownIPsJson = user["knownIPs"].ToString();

                    if (!string.IsNullOrEmpty(uuid))
                    {
                        if (!uuidToUsers.ContainsKey(uuid))
                            uuidToUsers[uuid] = new List<int>();
                        uuidToUsers[uuid].Add(i);
                    }

                    List<string> ips = new List<string>();
                    if (!string.IsNullOrEmpty(knownIPsJson))
                    {
                        try
                        {
                            ips = JsonConvert.DeserializeObject<List<string>>(knownIPsJson) ?? new List<string>();
                        }
                        catch { }
                    }

                    foreach (string ip in ips)
                    {
                        if (!string.IsNullOrEmpty(ip))
                        {
                            if (!ipToUsers.ContainsKey(ip))
                                ipToUsers[ip] = new List<int>();
                            ipToUsers[ip].Add(i);
                        }
                    }
                }

                int[] parent = new int[allUsers.Count];
                for (int i = 0; i < parent.Length; i++) parent[i] = i;

                Func<int, int> find = null;
                find = (int x) => {
                    if (parent[x] != x) parent[x] = find(parent[x]);
                    return parent[x];
                };

                Action<int, int> union = (int x, int y) => {
                    int px = find(x);
                    int py = find(y);
                    if (px != py) parent[px] = py;
                };

                foreach (var kvp in ipToUsers)
                {
                    List<int> users = kvp.Value;
                    for (int i = 1; i < users.Count; i++)
                    {
                        union(users[0], users[i]);
                    }
                }

                foreach (var kvp in uuidToUsers)
                {
                    List<int> users = kvp.Value;
                    for (int i = 1; i < users.Count; i++)
                    {
                        union(users[0], users[i]);
                    }
                }

                Dictionary<int, HashSet<int>> groups = new Dictionary<int, HashSet<int>>();
                for (int i = 0; i < allUsers.Count; i++)
                {
                    int root = find(i);
                    if (!groups.ContainsKey(root))
                        groups[root] = new HashSet<int>();
                    groups[root].Add(i);
                }

                int index = 1;
                foreach (var group in groups)
                {
                    if (group.Value.Count > 1)
                    {
                        Dictionary<string, object> item = new Dictionary<string, object>
                        {
                            { "index", index }
                        };

                        List<Dictionary<string, object>> accounts = new List<Dictionary<string, object>>();
                        HashSet<string> allIPs = new HashSet<string>();

                        foreach (int userIdx in group.Value)
                        {
                            var user = allUsers[userIdx];
                            string username = user["username"].ToString();
                            string knownIPsJson = user["knownIPs"].ToString();

                            accounts.Add(new Dictionary<string, object>
                            {
                                { "id", user["id"] },
                                { "username", username }
                            });

                            List<string> ips = new List<string>();
                            if (!string.IsNullOrEmpty(knownIPsJson))
                            {
                                try
                                {
                                    ips = JsonConvert.DeserializeObject<List<string>>(knownIPsJson) ?? new List<string>();
                                }
                                catch { }
                            }

                            foreach (string ip in ips)
                            {
                                if (!string.IsNullOrEmpty(ip))
                                {
                                    allIPs.Add(ip);
                                }
                            }
                        }

                        item["accounts"] = accounts;
                        item["ips"] = allIPs.ToList();

                        result.Add(item);
                        index++;
                    }
                }

                return new RestObject()
                {
                    { "duplicateips", result }
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