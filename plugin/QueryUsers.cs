﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using Rests;
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
        /// <summary>每页条数上限（玩家列表页固定 100 条/页）。</summary>
        private const int MaxPageSize = 100;

        /// <summary>
        /// 解析布尔参数：支持 1/true/yes（大小写不敏感），空/其他为 false。
        /// </summary>
        private static bool ParseBool(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            return value.Equals("1") || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        public static object QueryUsersList(RestRequestArgs args)
        {
            // ═══ 参数解析（全部可选；不带分页参数时保持原有"全量返回"行为，向后兼容）═══
            // username      单查（精确+大小写兜底）
            // onlineOnly    只看在线玩家（内存过滤，供统一列表的"仅在线"筛选）
            // keyword       用户名模糊搜索（服务端 LIKE 语义：子串匹配，大小写不敏感）
            // hasCharacter  只看有 SSC 角色数据的玩家
            // page/pageSize 分页（内存切片，pageSize 上限 100）；两者任一提供即启用分页
            //
            // 排序策略（2026-09 优化，替代旧版"在线/全部"两个 Tab）：在线玩家永远置顶，
            // 同一在线状态下按 ID（注册顺序）升序；排序在分页切片之前完成，故在线玩家必落在第 1 页。
            // 返回体额外含 onlineCount（筛选结果中当前在线的账号数，与 total 同口径）
            // 与 onlineFirst=true（排序语义标记）。
            string username = args.Parameters["username"];
            string keyword = args.Parameters["keyword"];
            bool onlineOnly = ParseBool(args.Parameters["onlineOnly"]);
            bool hasCharacterOnly = ParseBool(args.Parameters["hasCharacter"]);

            int page = 1;
            int pageSize = MaxPageSize;
            bool usePaging = false;
            if (int.TryParse(args.Parameters["page"], out int p) && p >= 1)
            {
                page = p;
                usePaging = true;
            }
            if (int.TryParse(args.Parameters["pageSize"], out int ps) && ps >= 1)
            {
                pageSize = Math.Min(ps, MaxPageSize);
                usePaging = true;
            }

            try
            {
                IDbConnection db = TShock.DB;
                List<Dictionary<string, object>> users = new List<Dictionary<string, object>>();
                // 匹配结果中的在线账号数：与 total 同口径（都只统计通过筛选的账号），
                // 供前端徽标「在线 N / 共 M」使用，避免分子分母口径不一致。
                int onlineMatchedCount = 0;

                // 在线账号集合（TShock.Players 内存，量小）：判定口径与旧实现保持一致——
                // 账号名大小写不敏感匹配且玩家 Active。预先建集合，避免逐用户扫描玩家列表。
                HashSet<string> onlineNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var plr in TShock.Players)
                {
                    if (plr == null || plr.Account == null || !plr.Active) continue;
                    if (!string.IsNullOrEmpty(plr.Account.Name)) onlineNames.Add(plr.Account.Name);
                }

                string query;
                object[] parameters;

                if (!string.IsNullOrEmpty(username))
                {
                    // 统一大小写匹配规则：先精确后大小写不敏感兜底（UserAccountHelper），
                    // 再用解析出的真实账号名精确查询，避免"仅大小写不同"的账号被漏查/错查
                    var resolved = UserAccountHelper.FindUserAccountByName(username);
                    if (resolved == null)
                    {
                        return new RestObject()
                        {
                            { "users", users },
                            { "total", 0 },
                            { "page", page },
                            { "pageSize", usePaging ? pageSize : 0 },
                            { "onlineCount", 0 },
                            { "onlineFirst", true }
                        };
                    }
                    query = "SELECT u.* FROM Users u WHERE u.Username = @0";
                    parameters = new object[] { resolved.Name };
                }
                else
                {
                    query = "SELECT u.* FROM Users u";
                    parameters = new object[] { };
                }

                // 有 SSC 角色数据的账号集合（tsCharacter 表，Account = Users.ID，一个账号可多行）
                // 用于 HasCharacter 标记：玩家管理页可筛选"仅有角色数据的玩家"
                HashSet<int> characterAccounts = new HashSet<int>();
                try
                {
                    using (QueryResult cr = db.QueryReader("SELECT DISTINCT Account FROM tsCharacter"))
                    {
                        while (cr.Read())
                        {
                            characterAccounts.Add(cr.Get<int>("Account"));
                        }
                    }
                }
                catch
                {
                    // tsCharacter 表不存在/查询失败时降级为全部无角色数据，不影响用户列表主流程
                }

                // 过滤（keyword / hasCharacter / onlineOnly）+ 收集
                using (QueryResult res = db.QueryReader(query, parameters))
                {
                    while (res.Read())
                    {
                        string resUsername = res.Get<string>("Username");
                        int resId = res.Get<int>("ID");

                        // 服务端搜索：用户名子串匹配（大小写不敏感）
                        if (!string.IsNullOrEmpty(keyword) &&
                            resUsername.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }

                        // 服务端筛选：仅显示有角色数据的玩家
                        if (hasCharacterOnly && !characterAccounts.Contains(resId))
                        {
                            continue;
                        }

                        // 检查玩家是否在线（预建集合 O(1) 命中，大小写不敏感）
                        bool isOnline = onlineNames.Contains(resUsername);

                        // 关键词/角色筛选已通过，此时计入在线数（与 total 同口径）
                        if (isOnline) onlineMatchedCount++;

                        // 服务端筛选：仅在线玩家
                        if (onlineOnly && !isOnline)
                        {
                            continue;
                        }

                        Dictionary<string, object> user = new Dictionary<string, object>();
                        user.Add("ID", resId);
                        user.Add("Username", resUsername);
                        user.Add("Usergroup", res.Get<string>("Usergroup"));
                        user.Add("Registered", res.Get<string>("Registered"));
                        user.Add("LastAccessed", res.Get<string>("LastAccessed"));
                        user.Add("UUID", res.Get<string>("UUID") ?? "");
                        user.Add("KnownIPs", res.Get<string>("KnownIPs") ?? "");
                        user.Add("IsOnline", isOnline);
                        user.Add("HasCharacter", characterAccounts.Contains(resId));

                        users.Add(user);
                    }
                }

                // 排序：在线玩家置顶，其次按 ID（注册顺序）。
                // 必须在分页切片之前排序，否则在线玩家会散落在各页而不是置顶；
                // 在线状态相同时以唯一键 ID 兜底，避免 List.Sort 不稳定导致跨页重复/漏项。
                users.Sort((a, b) =>
                {
                    int byOnline = ((bool)b["IsOnline"]).CompareTo((bool)a["IsOnline"]);
                    if (byOnline != 0) return byOnline;
                    return ((int)a["ID"]).CompareTo((int)b["ID"]);
                });

                int total = users.Count;
                List<Dictionary<string, object>> pageUsers;
                if (usePaging)
                {
                    int start = (page - 1) * pageSize;
                    if (start >= total)
                    {
                        pageUsers = new List<Dictionary<string, object>>();
                    }
                    else
                    {
                        pageUsers = users.GetRange(start, Math.Min(pageSize, total - start));
                    }
                }
                else
                {
                    pageUsers = users;
                }

                return new RestObject()
                {
                    { "users", pageUsers },
                    { "total", total },
                    { "page", page },
                    { "pageSize", usePaging ? pageSize : total },
                    { "onlineCount", onlineMatchedCount },
                    { "onlineFirst", true }
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
                string matchedUsername = null; // 实际命中的账号名（数据库中的真实大小写）
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

                        // 匹配规则与 UserAccountHelper 一致：
                        // 精确大小写优先；仅大小写不敏感命中时取第一个（先注册者，确定性）
                        if (userUsername.Equals(username, StringComparison.Ordinal))
                        {
                            targetIndex = index;
                            matchedUsername = userUsername;
                        }
                        else if (targetIndex == -1 &&
                                 userUsername.Equals(username, StringComparison.OrdinalIgnoreCase))
                        {
                            targetIndex = index;
                            matchedUsername = userUsername;
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
                    { "targetUser", matchedUsername },
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
            string character = "后台操作";

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

            try
            {
                if (!string.IsNullOrEmpty(args.Parameters["character"]))
                    character = args.Parameters["character"];
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
                    // 统一大小写匹配规则：先精确后大小写不敏感兜底，再用真实账号名精确查询
                    var resolved = UserAccountHelper.FindUserAccountByName(name);
                    if (resolved == null)
                    {
                        return new RestObject("404")
                        {
                            { "error", "用户不存在" }
                        };
                    }
                    query = "SELECT ID, Username, UUID, KnownIPs FROM Users WHERE Username = @0";
                    parameters = new object[] { resolved.Name };
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

                DateTime now = DateTime.UtcNow;
                DateTime never = DateTime.MaxValue;

                // 使用 InsertBan 直接写入数据库，带操作人信息
                TShock.Bans.InsertBan($"acc:{username}", reason, character, now, never);

                if (!string.IsNullOrEmpty(uuid))
                {
                    TShock.Bans.InsertBan($"uuid:{uuid}", reason, character, now, never);
                }

                foreach (string ip in ipList)
                {
                    if (!string.IsNullOrEmpty(ip))
                    {
                        TShock.Bans.InsertBan($"ip:{ip}", reason, character, now, never);
                    }
                }

                return new RestObject()
                {
                    { "response", "封禁成功" },
                    { "username", username },
                    { "uuid", uuid ?? "无" },
                    { "bannedIPs", ipList.Count },
                    { "reason", reason },
                    { "character", character }
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