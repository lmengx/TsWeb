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
        /// 临时封禁时长上限：100 年（36500 天）。
        /// 存在的意义是拦住两类输入——手滑多敲几个 0 的（如 999999999d），
        /// 以及超出 DateTime 表示范围会导致 AddSeconds 抛异常的极端值。
        /// 真要"永久"就用不带 durationSeconds 的永久封禁，不必靠一个大数字凑。
        /// </summary>
        private const long MaxBanDurationSeconds = 100L * 365 * 24 * 60 * 60;

        /// <summary>
        /// 把秒数说成人话，用于踢下线提示。
        /// 这里刻意**不写"封到几点几分"**：那个时刻要么按服务器时区渲染、要么按管理员浏览器时区渲染，
        /// 两者不一致时（服务器跑 UTC、管理员在 UTC+8 很常见）玩家被踢时看到的到期时间
        /// 与管理员在面板上看到的对不上。只说时长就没有这个问题，玩家自己知道现在几点。
        /// </summary>
        private static string FormatBanDuration(long seconds)
        {
            if (seconds >= 86400 && seconds % 86400 == 0) return $"{seconds / 86400} 天";
            if (seconds >= 3600 && seconds % 3600 == 0) return $"{seconds / 3600} 小时";
            if (seconds >= 60 && seconds % 60 == 0) return $"{seconds / 60} 分钟";
            return $"{seconds} 秒";
        }

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

            // 封禁时长（秒）：**不传 = 永久封禁**，保持本接口原有语义，其他调用方不受影响。
            // 传了但不合法则直接 400 拒绝，而不是"兜底成永久"：想封 1 小时却悄悄变成永久，
            // 是使用者事后才会发现的静默加重，宁可当场报错让人重填。
            string durationRaw = null;

            try
            {
                durationRaw = args.Parameters["durationSeconds"];
            }
            catch { }

            // durationRaw 可能为 null（参数缺失），先归一成已 trim 的文本再判断，
            // 这样下面的解析不必再靠可空流分析去推"这里一定不是 null"
            string durationText = (durationRaw ?? string.Empty).Trim();
            bool permanentBan = durationText.Length == 0;
            long durationSeconds = 0;

            if (!permanentBan)
            {
                if (!long.TryParse(durationText, out durationSeconds) || durationSeconds <= 0)
                {
                    return new RestObject("400")
                    {
                        { "error", "durationSeconds 必须是大于 0 的整数秒（不传该参数表示永久封禁）" }
                    };
                }

                if (durationSeconds > MaxBanDurationSeconds)
                {
                    return new RestObject("400")
                    {
                        { "error", $"临时封禁最长 {MaxBanDurationSeconds / 86400} 天，需要更久请直接用永久封禁" }
                    };
                }
            }

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
                DateTime end = permanentBan ? DateTime.MaxValue : now.AddSeconds(durationSeconds);

                // 给这个标识套上本次封禁（时长 + 理由）。
                //
                // 每个标识在 TShock 里只允许存在一条生效中的封禁：BanManager.BanAddedCheck 会拒绝
                // 给"已经有生效封禁"的标识插入新记录，InsertBan 于是返回 Ban = null。
                // 旧代码不看返回值，"一条都没写进去"也回报成功；就算看了，结果也只是把
                // "把永久改成 1 小时"这种事永远堵死——而这恰恰是临时封禁最该能用的场景。
                //
                // 所以分两种走法：
                //   已有生效封禁 → 改写那条的时长与理由（不删记录、不留"未封禁"的空窗，
                //                  玩家全程处于封禁状态，封禁票据号保持不变）；
                //   没有         → 照常插入新记录。
                int insertedCount = 0;
                int replacedCount = 0;
                List<int> replacedTickets = new List<int>();
                string rejectMessage = null;

                bool ApplyBan(string identifier)
                {
                    if (string.IsNullOrEmpty(identifier)) return false;

                    // 判定口径与 BanManager.BanAddedCheck 逐字一致（同一标识 + 未过期），
                    // 否则会出现"我判定没冲突、TShock 判定冲突"的死角。
                    // 必须读 TShock.Bans.Bans 这份内存字典——TShock 判封禁用的就是它，
                    // 而 RetrieveBansByIdentifier 是现查数据库并返回新对象，改它不生效。
                    List<Ban> actives = TShock.Bans.Bans.Values
                        .Where(b => b.Identifier == identifier && b.ExpirationDateTime > now)
                        .ToList();

                    if (actives.Count > 0)
                    {
                        bool rewritten = false;

                        foreach (Ban old in actives)
                        {
                            // Date 也一起改成现在：这条记录的理由与操作人都换成了本次的，
                            // 若还把起始时间留在几个月前，封禁列表会读成"几个月前由当前操作人封禁"。
                            // now 在本方法开头就已取好，所以 Date 落到过去，封禁立即生效，
                            // 不会出现"起始时间在未来所以暂时不算封禁"的空档。
                            int affected = TShock.DB.Query(
                                "UPDATE PlayerBans SET Reason=@0, BanningUser=@1, Date=@2, Expiration=@3 WHERE TicketNumber=@4",
                                reason, character, now.Ticks, end.Ticks, old.TicketNumber);

                            if (affected <= 0)
                            {
                                if (rejectMessage == null)
                                {
                                    rejectMessage = $"改写封禁记录 #{old.TicketNumber} 失败（数据库未受影响）";
                                }
                                continue;
                            }

                            // 内存里那份要跟着改：TShock 判封禁读的是 Bans 字典而不是数据库，
                            // 只改库会出现"库里已放行、服务器仍按旧封禁拦人"直到下次重载。
                            old.Reason = reason;
                            old.BanningUser = character;
                            old.BanDateTime = now;
                            old.ExpirationDateTime = end;

                            replacedTickets.Add(old.TicketNumber);
                            rewritten = true;
                        }

                        if (rewritten)
                        {
                            replacedCount++;
                            return true;
                        }

                        return false;
                    }

                    AddBanResult banResult = TShock.Bans.InsertBan(identifier, reason, character, now, end);

                    if (banResult != null && banResult.Ban != null)
                    {
                        insertedCount++;
                        return true;
                    }

                    // 记下第一条拒绝原因：可能是别的插件在 BanPreAdd 里否决，也可能是写库失败。
                    // 不吞掉它，否则管理员只看到"失败"却不知道为什么。
                    if (rejectMessage == null && banResult != null && !string.IsNullOrEmpty(banResult.Message))
                    {
                        rejectMessage = banResult.Message;
                    }

                    return false;
                }

                // 到期时间用的是 TShock 原生能力：BanManager 的判定为 UtcNow < ExpirationDateTime，
                // 过期即自动失效，不需要另做"定时解封"的任务。
                ApplyBan($"acc:{username}");

                if (!string.IsNullOrEmpty(uuid))
                {
                    ApplyBan($"uuid:{uuid}");
                }

                foreach (string ip in ipList.Distinct())
                {
                    // 空 IP 必须先挡掉：拼出来的是 "ip:" 这个非空字符串，
                    // 会绕过 ApplyBan 的空判断，往封禁表写进一条谁也匹配不上的垃圾记录
                    if (string.IsNullOrEmpty(ip)) continue;

                    // Distinct：KnownIPs 不保证去重，同一个 IP 出现两次时，
                    // 第二次会命中"刚才那条"并走改写分支，让回报凭空多出一条"已覆盖原有封禁"
                    ApplyBan($"ip:{ip}");
                }

                // 把在线的同一玩家踢下线：只写封禁记录不会让人立刻掉线，他会一直玩到主动退出为止。
                // 只按账号名 / UUID 匹配，**不按 IP 匹配**——同一个 IP 后面可能是同住或同网吧的其他人，
                // 按 IP 踢会把无辜的人一起踢掉。
                List<string> kicked = new List<string>();

                foreach (var plr in TShock.Players)
                {
                    if (plr == null || !plr.Active) continue;

                    bool isTarget =
                        (!string.IsNullOrEmpty(username) && plr.Account != null
                            && plr.Account.Name.Equals(username, StringComparison.OrdinalIgnoreCase))
                        || (!string.IsNullOrEmpty(uuid)
                            && uuid.Equals(plr.UUID, StringComparison.OrdinalIgnoreCase));

                    if (!isTarget) continue;

                    try
                    {
                        string kickMessage = permanentBan
                            ? $"你已被封禁（永久）。原因：{reason}"
                            : $"你已被封禁 {FormatBanDuration(durationSeconds)}，到期自动解封。原因：{reason}";

                        if (plr.Kick(kickMessage, force: true, silent: false, adminUserName: character))
                        {
                            kicked.Add(plr.Name);
                        }
                    }
                    catch (Exception kickEx)
                    {
                        // 踢人失败不能让整个封禁报失败：封禁记录已经写进去了，如实记下来，
                        // 由管理员决定是否手工处理这个还挂在线上的人。
                        TShock.Log.ConsoleError($"[TSWeb] 封禁后踢出 {plr.Name} 失败: {kickEx.Message}");
                    }
                }

                // 一条都没落地 = 这次封禁（含时长）根本没有生效，必须报错而不是报成功
                if (insertedCount + replacedCount == 0)
                {
                    string reasonNote = string.IsNullOrEmpty(rejectMessage) ? "" : $"（{rejectMessage}）";
                    string kickedNote = kicked.Count > 0 ? "已将其在线角色踢下线。" : "";

                    return new RestObject("409")
                    {
                        { "error", $"本次封禁未能写入或改写任何记录{reasonNote}。{kickedNote}" }
                    };
                }

                return new RestObject()
                {
                    { "response", permanentBan ? "封禁成功（永久）" : "封禁成功（临时）" },
                    { "username", username },
                    { "uuid", uuid ?? "无" },
                    // bannedIPs 保持本接口原有含义（该账号已知 IP 的条数），不悄悄改老字段的语义；
                    // 这次实际生效了几条由 bansInserted / bansReplaced 表达
                    { "bannedIPs", ipList.Count },
                    { "bansInserted", insertedCount },
                    { "bansReplaced", replacedTickets },
                    { "reason", reason },
                    { "character", character },
                    { "permanent", permanentBan },
                    { "durationSeconds", permanentBan ? 0 : durationSeconds },
                    { "endDateTicks", end.Ticks },
                    { "kicked", kicked }
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