using Rests;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using TShockAPI;
using TShockAPI.DB;

namespace TShockData
{
    /// <summary>
    /// 账号属性判定机制（Account Attributes）
    ///
    /// 数据来源（全部为 TShock 既有数据，不新增采集）：
    ///   - Users 表：Username / UUID / KnownIPs / Registered（注册时间）/ LastAccessed（最后访问）/ Usergroup
    ///   - player_daily_stat 表：uid=用户名, date=yyyy-MM-dd, daily_min=当日在线分钟
    ///   - 关联账号：IP（KnownIPs 集合）+ UUID 并查集聚类（与 QueryUsers.QueryAllDuplicateIPs 同构）
    ///
    /// 属性定义（阈值全部集中于此，判定规则固定可解释、可重复）：
    ///   - alt            小号：在关联组内 且 累计时长 &lt;= AltMaxMinutes
    ///   - guest          游客账号：不在任何关联组 且 累计时长 &lt;= AltMaxMinutes
    ///   - churn          流失玩家：累计时长 &gt; ChurnMinMinutes 且 最后访问距今 &gt;= ChurnGapDays
    ///   - new_active     近期新增活跃：注册 &lt;= NewActiveRegDays 且 最后访问距今 &lt;= NewActiveLoginDays 且 累计时长 &gt; AltMaxMinutes
    ///   - returning      回流玩家：曾活跃（累计&gt;10h 或曾连续活跃&gt;=7天）且 最近活跃段前存在 &gt;= ReturningSilentDays 空档 且 最后访问距今 &lt;= ReturningRecentDays
    ///   - sustained      持续活跃：近 SustainedWindowDays 天活跃 &gt;= SustainedActiveDays 天 且 最后访问距今 &lt;= SustainedRecentDays
    ///   - dormant        长期沉睡：累计时长 &gt; ChurnMinMinutes 且 最后访问距今 &gt;= DormantGapDays
    ///   - high_risk_group 高风险关联组：所在关联组账号数 &gt;= HighRiskGroupSize（疑似批量小号/工作室）
    ///
    /// 每个账号可命中多个属性（attributes 数组）；主属性 primaryAttribute 用于概览互斥分类。
    /// </summary>
    public static class AccountAttributes
    {
        // ── 属性键 ──
        public const string AttrAlt = "alt";
        public const string AttrGuest = "guest";
        public const string AttrChurn = "churn";
        public const string AttrNewActive = "new_active";
        public const string AttrReturning = "returning";
        public const string AttrSustained = "sustained";
        public const string AttrDormant = "dormant";
        public const string AttrHighRiskGroup = "high_risk_group";
        public const string AttrNormal = "normal";

        // ── 判定阈值（分钟 / 天）──
        public const int AltMaxMinutes = 30;            // 小号/游客时长上限
        public const int ChurnMinMinutes = 600;         // 流失/沉睡最低累计时长 10h
        public const int ChurnGapDays = 10;             // 流失沉默天数
        public const int NewActiveRegDays = 14;         // 新增活跃注册窗口
        public const int NewActiveLoginDays = 7;        // 新增活跃登录窗口
        public const int ReturningSilentDays = 14;      // 回流沉默空档
        public const int ReturningRecentDays = 3;       // 回流回归窗口
        public const int ReturningHistoryMinDays = 7;   // 曾连续活跃天数
        public const int SustainedWindowDays = 14;      // 持续活跃统计窗口
        public const int SustainedActiveDays = 7;       // 持续活跃最低活跃天数
        public const int SustainedRecentDays = 3;       // 持续活跃最近登录窗口
        public const int DormantGapDays = 30;           // 长期沉睡沉默天数
        public const int HighRiskGroupSize = 3;         // 高风险关联组最小账号数

        // ── 主属性优先级（高→低；用于概览互斥分类）──
        private static readonly string[] PrimaryPriority = new[]
        {
            AttrAlt, AttrGuest, AttrChurn, AttrDormant, AttrReturning, AttrNewActive, AttrSustained
        };

        /// <summary>
        /// GET /data/account/attributes
        /// 全量账号属性明细 + 判定规则元信息。
        /// 参数（全部可选）：
        ///   attr        按单一属性过滤（如 attr=alt），可逗号分隔多选（attr=alt,guest）
        ///   keyword     用户名模糊搜索（子串，大小写不敏感）
        ///   minMinutes  累计时长下限（分钟）
        ///   maxMinutes  累计时长上限（分钟）
        ///   sortBy      totalMinutes | lastAccess | registered | activeDays14（默认 totalMinutes）
        ///   sortDir     asc | desc（默认 desc）
        ///   page/pageSize 分页（pageSize 上限 1000）
        /// </summary>
        public static object GetAttributes(RestRequestArgs args)
        {
            try
            {
                string attrFilter = "";
                try { attrFilter = args.Parameters["attr"] ?? ""; } catch { }
                string keyword = "";
                try { keyword = args.Parameters["keyword"] ?? ""; } catch { }

                int minMinutes = GetOptInt(args, "minMinutes", -1);
                int maxMinutes = GetOptInt(args, "maxMinutes", -1);
                string sortBy = "totalMinutes";
                try { sortBy = args.Parameters["sortBy"] ?? "totalMinutes"; } catch { }
                string sortDir = "desc";
                try { sortDir = args.Parameters["sortDir"] ?? "desc"; } catch { }

                int page = GetOptInt(args, "page", 1);
                int pageSize = GetOptInt(args, "pageSize", 1000);
                if (page < 1) page = 1;
                if (pageSize < 1) pageSize = 1;
                if (pageSize > 1000) pageSize = 1000;

                HashSet<string> attrSet = null;
                if (!string.IsNullOrEmpty(attrFilter))
                {
                    attrSet = new HashSet<string>(attrFilter.Split(',')
                        .Select(a => a.Trim().ToLowerInvariant())
                        .Where(a => a.Length > 0), StringComparer.Ordinal);
                }

                var rows = ComputeAllAccounts();

                // 过滤
                var filtered = rows.AsEnumerable();
                if (attrSet != null)
                    filtered = filtered.Where(r => r.Attributes.Any(a => attrSet.Contains(a)));
                if (!string.IsNullOrEmpty(keyword))
                    filtered = filtered.Where(r => r.Username.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
                if (minMinutes >= 0)
                    filtered = filtered.Where(r => r.TotalMinutes >= minMinutes);
                if (maxMinutes >= 0)
                    filtered = filtered.Where(r => r.TotalMinutes <= maxMinutes);

                var list = filtered.ToList();

                // 排序
                Comparison<AccountRow> cmp;
                switch (sortBy)
                {
                    case "lastAccess": cmp = (a, b) => a.LastAccess.CompareTo(b.LastAccess); break;
                    case "registered": cmp = (a, b) => a.Registered.CompareTo(b.Registered); break;
                    case "activeDays14": cmp = (a, b) => a.ActiveDays14.CompareTo(b.ActiveDays14); break;
                    default: cmp = (a, b) => a.TotalMinutes.CompareTo(b.TotalMinutes); break;
                }
                if (sortDir.Equals("asc", StringComparison.OrdinalIgnoreCase))
                    list.Sort(cmp);
                else
                    list.Sort((a, b) => cmp(b, a));

                int total = list.Count;
                var pageRows = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();

                var accounts = new List<Dictionary<string, object>>();
                foreach (var r in pageRows)
                {
                    accounts.Add(new Dictionary<string, object>
                    {
                        { "id", r.Id },
                        { "username", r.Username },
                        { "group", r.Group },
                        { "registered", r.Registered == DateTime.MinValue ? "" : r.Registered.ToString("yyyy-MM-dd") },
                        { "lastAccess", r.LastAccess == DateTime.MinValue ? "" : r.LastAccess.ToString("yyyy-MM-dd") },
                        { "totalMinutes", r.TotalMinutes },
                        { "recent7dMinutes", r.Recent7dMinutes },
                        { "recent14dMinutes", r.Recent14dMinutes },
                        { "recent30dMinutes", r.Recent30dMinutes },
                        { "activeDays7", r.ActiveDays7 },
                        { "activeDays14", r.ActiveDays14 },
                        { "activeDays30", r.ActiveDays30 },
                        { "relGroupIndex", r.RelGroupIndex },
                        { "relGroupSize", r.RelGroupSize },
                        { "attributes", r.Attributes },
                        { "primaryAttribute", r.PrimaryAttribute }
                    });
                }

                return new RestObject
                {
                    { "meta", BuildMeta() },
                    { "total", total },
                    { "page", page },
                    { "pageSize", pageSize },
                    { "accounts", accounts }
                };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }

        /// <summary>判定规则元信息（语义明确：前端展示规则说明用）</summary>
        private static Dictionary<string, object> BuildMeta()
        {
            return new Dictionary<string, object>
            {
                { "generatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
                { "version", "1" },
                { "rules", new Dictionary<string, object>
                    {
                        { "alt", $"关联账号 且 累计时长 <= {AltMaxMinutes} 分钟" },
                        { "guest", $"非关联账号 且 累计时长 <= {AltMaxMinutes} 分钟" },
                        { "churn", $"累计时长 > {ChurnMinMinutes / 60} 小时 且 最后访问距今 >= {ChurnGapDays} 天" },
                        { "new_active", $"注册 <= {NewActiveRegDays} 天 且 最后访问距今 <= {NewActiveLoginDays} 天 且 累计时长 > {AltMaxMinutes} 分钟" },
                        { "returning", $"曾活跃(累计>{ChurnMinMinutes / 60}h 或曾连续活跃>={ReturningHistoryMinDays}天) 且 活跃段间空档 >= {ReturningSilentDays} 天 且 最后访问距今 <= {ReturningRecentDays} 天" },
                        { "sustained", $"近 {SustainedWindowDays} 天活跃 >= {SustainedActiveDays} 天 且 最后访问距今 <= {SustainedRecentDays} 天" },
                        { "dormant", $"累计时长 > {ChurnMinMinutes / 60} 小时 且 最后访问距今 >= {DormantGapDays} 天" },
                        { "high_risk_group", $"所在关联组账号数 >= {HighRiskGroupSize}" }
                    }
                }
            };
        }

        private static int GetOptInt(RestRequestArgs args, string key, int def)
        {
            try
            {
                string v = args.Parameters[key];
                if (int.TryParse(v, out int r)) return r;
            }
            catch { }
            return def;
        }

        // ── 计算模型 ──

        private class AccountRow
        {
            public int Id;
            public string Username;
            public string Group;
            public DateTime Registered;
            public DateTime LastAccess;
            public int TotalMinutes;
            public int Recent7dMinutes;
            public int Recent14dMinutes;
            public int Recent30dMinutes;
            public int ActiveDays7;
            public int ActiveDays14;
            public int ActiveDays30;
            public int RelGroupIndex;   // 0 = 无关联组
            public int RelGroupSize;    // 1 = 无关联组
            public List<string> Attributes = new List<string>();
            public string PrimaryAttribute = AttrNormal;
        }

        /// <summary>
        /// 全量计算所有账号的属性。
        /// 一次性读取 Users + player_daily_stat 全表在内存计算（数据量级：账号数千 × 每日记录，可控）。
        /// </summary>
        private static List<AccountRow> ComputeAllAccounts()
        {
            var rows = new List<AccountRow>();
            IDbConnection db = TShock.DB;
            DateTime now = DateTime.Now;

            // 1) 账号基础信息（同时收集 UUID/IP 用于关联账号并查集，统一用用户名索引 idx）
            var userList = new List<AccountRow>();
            // TShock 账号名大小写敏感（与 QueryUsers 一致）；player_daily_stat.uid 即精确用户名
            var indexByName = new Dictionary<string, int>(StringComparer.Ordinal);
            var uuidToIdx = new Dictionary<string, List<int>>();
            var ipToIdx = new Dictionary<string, List<int>>();
            using (var res = db.QueryReader(
                "SELECT ID, Username, Usergroup, Registered, LastAccessed, UUID, KnownIPs FROM Users"))
            {
                while (res.Read())
                {
                    string name = res.Get<string>("Username");
                    if (string.IsNullOrEmpty(name)) continue;
                    var row = new AccountRow
                    {
                        Id = res.Get<int>("ID"),
                        Username = name,
                        Group = res.Get<string>("Usergroup") ?? "",
                        Registered = ParseTime(res.Get<string>("Registered")),
                        LastAccess = ParseTime(res.Get<string>("LastAccessed"))
                    };
                    indexByName[name] = userList.Count;
                    userList.Add(row);
                    int rowIndex = userList.Count - 1;

                    // 关联元数据：UUID / KnownIPs（键为用户名索引，与 userList 严格对齐）
                    string uuid = res.Get<string>("UUID") ?? "";
                    if (!string.IsNullOrEmpty(uuid))
                    {
                        if (!uuidToIdx.TryGetValue(uuid, out var ul)) { ul = new List<int>(); uuidToIdx[uuid] = ul; }
                        ul.Add(rowIndex);
                    }
                    string known = res.Get<string>("KnownIPs") ?? "";
                    if (!string.IsNullOrEmpty(known))
                    {
                        List<string> ips = new List<string>();
                        try { ips = JsonConvert.DeserializeObject<List<string>>(known) ?? new List<string>(); }
                        catch { }
                        foreach (var ip in ips)
                        {
                            if (string.IsNullOrEmpty(ip)) continue;
                            if (!ipToIdx.TryGetValue(ip, out var il)) { il = new List<int>(); ipToIdx[ip] = il; }
                            il.Add(rowIndex);
                        }
                    }
                }
            }

            // 2) 游玩时长聚合（player_daily_stat 全表一次读，内存分组）
            string d7 = now.AddDays(-7).ToString("yyyy-MM-dd");
            string d14 = now.AddDays(-14).ToString("yyyy-MM-dd");
            string d30 = now.AddDays(-30).ToString("yyyy-MM-dd");

            var datesByUser = new Dictionary<int, List<string>>(); // userIndex -> 活跃日期（升序）
            var minByUser = new Dictionary<int, int>();            // userIndex -> 累计分钟
            var recent7 = new Dictionary<int, int>();
            var recent14 = new Dictionary<int, int>();
            var recent30 = new Dictionary<int, int>();

            using (var res = db.QueryReader(
                "SELECT uid, date, daily_min FROM player_daily_stat"))
            {
                while (res.Read())
                {
                    string uid = res.Get<string>("uid");
                    if (string.IsNullOrEmpty(uid)) continue;
                    if (!indexByName.TryGetValue(uid, out int idx)) continue;
                    string date = res.Get<string>("date") ?? "";
                    int daily = 0;
                    try { daily = res.Get<int>("daily_min"); } catch { }

                    if (string.CompareOrdinal(date, d7) >= 0) { recent7[idx] = recent7.TryGetValue(idx, out var v7) ? v7 + daily : daily; }
                    if (string.CompareOrdinal(date, d14) >= 0) { recent14[idx] = recent14.TryGetValue(idx, out var v14) ? v14 + daily : daily; }
                    if (string.CompareOrdinal(date, d30) >= 0) { recent30[idx] = recent30.TryGetValue(idx, out var v30) ? v30 + daily : daily; }
                    minByUser[idx] = minByUser.TryGetValue(idx, out var vm) ? vm + daily : daily;

                    if (!datesByUser.TryGetValue(idx, out var dates))
                    {
                        dates = new List<string>();
                        datesByUser[idx] = dates;
                    }
                    dates.Add(date);
                }
            }

            // 3) 关联账号并查集（IP + UUID，与 QueryUsers.QueryAllDuplicateIPs 同构；
            //    ipToIdx/uuidToIdx 已在步骤 1 统一按用户名索引收集，行序严格对齐 userList）
            int n = userList.Count;
            int[] parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            Func<int, int> find = null;
            find = x => { if (parent[x] != x) parent[x] = find(parent[x]); return parent[x]; };
            void Union(int x, int y)
            {
                int px = find(x), py = find(y);
                if (px != py) parent[px] = py;
            }

            foreach (var kv in uuidToIdx)
                for (int i = 1; i < kv.Value.Count; i++) Union(kv.Value[0], kv.Value[i]);
            foreach (var kv in ipToIdx)
                for (int i = 1; i < kv.Value.Count; i++) Union(kv.Value[0], kv.Value[i]);

            // 组编号 + 组大小（组内>1 才算关联组）
            var rootToGroup = new Dictionary<int, int>();
            var groupSize = new Dictionary<int, int>();
            int groupCounter = 0;
            for (int i = 0; i < n; i++)
            {
                int root = find(i);
                groupSize[root] = groupSize.TryGetValue(root, out var gs) ? gs + 1 : 1;
            }
            for (int i = 0; i < n; i++)
            {
                int root = find(i);
                if (groupSize[root] > 1 && !rootToGroup.ContainsKey(root))
                {
                    rootToGroup[root] = ++groupCounter;
                }
            }

            // 4) 逐账号判定
            foreach (var user in userList)
            {
                if (!indexByName.TryGetValue(user.Username, out int idx)) continue;
                user.TotalMinutes = minByUser.TryGetValue(idx, out var tm) ? tm : 0;
                user.Recent7dMinutes = recent7.TryGetValue(idx, out var r7) ? r7 : 0;
                user.Recent14dMinutes = recent14.TryGetValue(idx, out var r14) ? r14 : 0;
                user.Recent30dMinutes = recent30.TryGetValue(idx, out var r30) ? r30 : 0;

                if (datesByUser.TryGetValue(idx, out var dates))
                {
                    dates.Sort(StringComparer.Ordinal);
                    user.ActiveDays7 = CountDatesInWindow(dates, d7);
                    user.ActiveDays14 = CountDatesInWindow(dates, d14);
                    user.ActiveDays30 = CountDatesInWindow(dates, d30);
                }

                // 关联组
                if (idx < n)
                {
                    int root = find(idx);
                    if (groupSize.TryGetValue(root, out var gsz) && gsz > 1)
                    {
                        user.RelGroupIndex = rootToGroup.TryGetValue(root, out var gi) ? gi : 0;
                        user.RelGroupSize = gsz;
                    }
                    else
                    {
                        user.RelGroupSize = 1;
                    }
                }

                // ── 属性判定 ──
                bool inRelGroup = user.RelGroupSize > 1;
                int lastGapDays = user.LastAccess == DateTime.MinValue
                    ? int.MaxValue
                    : (int)(now.Date - user.LastAccess.Date).TotalDays;

                if (inRelGroup && user.TotalMinutes <= AltMaxMinutes)
                    user.Attributes.Add(AttrAlt);
                if (!inRelGroup && user.TotalMinutes <= AltMaxMinutes)
                    user.Attributes.Add(AttrGuest);

                if (user.TotalMinutes > ChurnMinMinutes && lastGapDays >= ChurnGapDays)
                    user.Attributes.Add(AttrChurn);

                int regDays = user.Registered == DateTime.MinValue
                    ? int.MaxValue
                    : (int)(now.Date - user.Registered.Date).TotalDays;
                if (regDays <= NewActiveRegDays && lastGapDays <= NewActiveLoginDays && user.TotalMinutes > AltMaxMinutes)
                    user.Attributes.Add(AttrNewActive);

                if (IsReturning(user, dates, now))
                    user.Attributes.Add(AttrReturning);

                if (user.ActiveDays14 >= SustainedActiveDays && lastGapDays <= SustainedRecentDays)
                    user.Attributes.Add(AttrSustained);

                if (user.TotalMinutes > ChurnMinMinutes && lastGapDays >= DormantGapDays)
                    user.Attributes.Add(AttrDormant);

                if (user.RelGroupSize >= HighRiskGroupSize)
                    user.Attributes.Add(AttrHighRiskGroup);

                // 主属性（优先级最高者；无命中 = normal）
                foreach (var p in PrimaryPriority)
                {
                    if (user.Attributes.Contains(p))
                    {
                        user.PrimaryAttribute = p;
                        break;
                    }
                }

                rows.Add(user);
            }

            return rows;
        }

        /// <summary>统计升序日期列表中 >= since 的日期个数</summary>
        private static int CountDatesInWindow(List<string> dates, string since)
        {
            int cnt = 0;
            foreach (var d in dates)
            {
                if (string.CompareOrdinal(d, since) >= 0) cnt++;
            }
            return cnt;
        }

        /// <summary>
        /// 回流判定：
        ///   1) 曾活跃：累计时长 &gt; ChurnMinMinutes 或 曾连续活跃 &gt;= ReturningHistoryMinDays 天；
        ///   2) 最近活跃段之前存在 &gt;= ReturningSilentDays 天的空档；
        ///   3) 最后访问距今 &lt;= ReturningRecentDays 天。
        /// </summary>
        private static bool IsReturning(AccountRow user, List<string> dates, DateTime now)
        {
            if (user.LastAccess == DateTime.MinValue) return false;
            int lastGapDays = (int)(now.Date - user.LastAccess.Date).TotalDays;
            if (lastGapDays > ReturningRecentDays) return false;

            // 曾活跃判定
            bool wasActive = user.TotalMinutes > ChurnMinMinutes;
            if (!wasActive && dates != null && dates.Count > 0)
            {
                // 最长连续活跃段
                int maxRun = 1, run = 1;
                for (int i = 1; i < dates.Count; i++)
                {
                    DateTime cur = DateTime.ParseExact(dates[i], "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    DateTime prev = DateTime.ParseExact(dates[i - 1], "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    if ((cur - prev).TotalDays == 1)
                    {
                        run++;
                        if (run > maxRun) maxRun = run;
                    }
                    else
                    {
                        run = 1;
                    }
                }
                wasActive = maxRun >= ReturningHistoryMinDays;
            }
            if (!wasActive) return false;

            // 活跃段间空档：找最后一段连续活跃的开始，其前一日到最近登录前是否存在 >= ReturningSilentDays 的空档
            // 简化实现：从最近登录日往前找，若存在连续活跃段的起点，起点前一日与更早活跃日之间 gap >= ReturningSilentDays
            if (dates == null || dates.Count == 0) return false;

            var dateSet = new HashSet<string>(dates, StringComparer.Ordinal);
            string lastDate = dates[dates.Count - 1];
            // 最后一段连续活跃的起点
            DateTime lastRunStart = DateTime.ParseExact(lastDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            while (dateSet.Contains(lastRunStart.AddDays(-1).ToString("yyyy-MM-dd")))
            {
                lastRunStart = lastRunStart.AddDays(-1);
            }
            // 起点之前最近的一个活跃日
            string before = lastRunStart.AddDays(-1).ToString("yyyy-MM-dd");
            string lastBefore = null;
            foreach (var d in dates)
            {
                if (string.CompareOrdinal(d, before) < 0)
                    lastBefore = d;
                else
                    break;
            }
            if (lastBefore == null) return false;

            DateTime beforeDt = DateTime.ParseExact(lastBefore, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            return (lastRunStart - beforeDt).TotalDays >= ReturningSilentDays;
        }

        /// <summary>
        /// 解析时间字符串为服务器本地时间。
        /// 兼容格式：ISO8601（2026-06-23T11:49:11）、TShock 空格格式（2026-06-23 11:49:11）、unix epoch。
        /// 语义与 QQ.cs FormatLocalTime 一致：无时区后缀视为服务器本地时间。
        /// </summary>
        private static DateTime ParseTime(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return DateTime.MinValue;
            if (DateTime.TryParse(raw, out var dt)) return dt;
            if (long.TryParse(raw, out var epoch))
            {
                try { return DateTimeOffset.FromUnixTimeSeconds(epoch).LocalDateTime; } catch { }
                try { return DateTimeOffset.FromUnixTimeMilliseconds(epoch).LocalDateTime; } catch { }
            }
            return DateTime.MinValue;
        }
    }
}
