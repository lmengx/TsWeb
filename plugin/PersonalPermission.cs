using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using Rests;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;
using TShockAPI.Hooks;

namespace TShockData
{
    /// <summary>
    /// 个人独立权限模块：
    ///   - 追加语义：个人权限未命中返回 Unhandled（继续走组权限检查），不覆盖组权限、零副作用
    ///   - 每条记录独立字段：签发人 / 备注 / 签发时间 / 到期时间（NULL=永久）
    ///   - 到期自动失效（钩子惰性过滤）+ 每分钟定时清理过期记录
    ///   - 支持快速签发、批量签发（多玩家 × 多权限全组合）、回收、聚合统计
    /// </summary>
    public static class PersonalPermissionManager
    {
        private const string Table = "PersonalPermissions";
        private const string DateFormat = "yyyy-MM-dd HH:mm:ss";

        // 禁止签发的敏感权限（防越权：这些只能通过组管理/superadmin 途径授予）
        private static readonly HashSet<string> BannedPermissions = new(StringComparer.OrdinalIgnoreCase)
        {
            "tshock.su",
            "tshock.superadmin.user",
            "tshock.admin.group",
            "tshock.admin.tempgroup"
        };

        // 缓存：账号ID → (权限 → 到期时间, null=永久)；惰性加载，权限检查 O(1)
        private static readonly ConcurrentDictionary<int, Dictionary<string, DateTime?>> Cache = new();
        private static readonly object CacheLock = new();

        private static Timer? _cleanupTimer;
        private static bool _initialized;
        private static TerrariaPlugin? _plugin;

        // ═══════════════ 生命周期 ═══════════════

        public static void Initialize(TerrariaPlugin plugin)
        {
            if (_initialized) return;
            _initialized = true;
            _plugin = plugin;

            EnsureTable();

            PlayerHooks.PlayerPermission += OnPermissionCheck;
            PlayerHooks.PlayerPostLogin += OnPlayerPostLogin;

            // 每分钟清理一次过期权限
            _cleanupTimer = new Timer(_ => CleanupExpiredSafe(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

            TShock.Log.ConsoleInfo("[TSWeb] 个人权限模块已初始化");
        }

        public static void Dispose()
        {
            if (!_initialized) return;
            _initialized = false;

            PlayerHooks.PlayerPermission -= OnPermissionCheck;
            PlayerHooks.PlayerPostLogin -= OnPlayerPostLogin;

            _cleanupTimer?.Dispose();
            _cleanupTimer = null;
            Cache.Clear();
        }

        /// <summary>重载事件调用：强制全量重建缓存（与 DB 对齐）</summary>
        public static void Reload()
        {
            Cache.Clear();
            TShock.Log.ConsoleInfo("[TSWeb] 个人权限缓存已刷新");
        }

        private static void EnsureTable()
        {
            try
            {
                TShock.DB.Query($@"
                    CREATE TABLE IF NOT EXISTS {Table} (
                        Id         INTEGER PRIMARY KEY AUTOINCREMENT,
                        UserId     INTEGER NOT NULL,
                        PlayerName TEXT    NOT NULL,
                        Permission TEXT    NOT NULL,
                        GrantedBy  TEXT    NOT NULL DEFAULT '',
                        Note       TEXT    DEFAULT '',
                        CreatedAt  TEXT    NOT NULL,
                        ExpireAt   TEXT    DEFAULT NULL,
                        UNIQUE(UserId, Permission)
                    );");
                TShock.DB.Query($"CREATE INDEX IF NOT EXISTS idx_pp_player ON {Table}(PlayerName);");
                TShock.DB.Query($"CREATE INDEX IF NOT EXISTS idx_pp_perm ON {Table}(Permission);");
                TShock.DB.Query($"CREATE INDEX IF NOT EXISTS idx_pp_expire ON {Table}(ExpireAt);");
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 个人权限表创建失败: {ex.Message}");
            }
        }

        // ═══════════════ 权限生效钩子 ═══════════════

        private static void OnPermissionCheck(PlayerPermissionEventArgs e)
        {
            try
            {
                var plr = e.Player;
                if (!plr.RealPlayer || plr.Account == null) return;
                // superadmin / 通配 *：不干预（天然全权限）
                if (plr.Group.Name == "superadmin" || plr.Group.TotalPermissions.Contains("*")) return;

                if (HasPersonalPermission(plr.Account.ID, e.Permission))
                    e.Result = PermissionHookResult.Granted;
                // 未命中 → 保持 Unhandled，走 TShock 正常组检查（追加语义）
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 个人权限检查异常: {ex.Message}");
            }
        }

        private static void OnPlayerPostLogin(PlayerPostLoginEventArgs e)
        {
            // 登录后强制重载缓存（防止运行期边界不一致）
            if (e.Player?.Account != null)
                Cache.TryRemove(e.Player.Account.ID, out _);
        }

        private static bool HasPersonalPermission(int userId, string permission)
        {
            var perms = GetOrLoad(userId);
            if (perms.Count == 0) return false;
            if (!perms.TryGetValue(permission, out var expireAt)) return false;
            if (expireAt.HasValue && expireAt.Value <= DateTime.Now) return false;
            return true;
        }

        private static Dictionary<string, DateTime?> GetOrLoad(int userId)
        {
            if (Cache.TryGetValue(userId, out var perms)) return perms;
            lock (CacheLock)
            {
                if (Cache.TryGetValue(userId, out perms)) return perms;
                perms = LoadFromDb(userId);
                Cache[userId] = perms;
                return perms;
            }
        }

        private static Dictionary<string, DateTime?> LoadFromDb(int userId)
        {
            var result = new Dictionary<string, DateTime?>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var reader = TShock.DB.QueryReader(
                    $"SELECT Permission, ExpireAt FROM {Table} WHERE UserId = @0", userId);
                while (reader.Read())
                {
                    var perm = reader.Get<string>("Permission");
                    if (string.IsNullOrEmpty(perm)) continue;
                    var expire = reader.Get<string>("ExpireAt");
                    if (string.IsNullOrEmpty(expire))
                        result[perm] = null;
                    else if (DateTime.TryParse(expire, out var dt))
                        result[perm] = dt;
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 加载个人权限失败 (uid={userId}): {ex.Message}");
            }
            return result;
        }

        // ═══════════════ 签发 / 回收核心 ═══════════════

        private static string? ValidatePermission(string permission)
        {
            if (string.IsNullOrWhiteSpace(permission)) return "权限名不能为空";
            if (permission.Contains(',')) return "权限名不能包含逗号";
            if (permission.Trim().EndsWith("*", StringComparison.Ordinal)) return "不允许签发通配权限";
            if (BannedPermissions.Contains(permission)) return $"权限 {permission} 属于敏感权限，不允许通过个人权限签发";
            return null;
        }

        private static DateTime? ParseExpireAt(string expireAtStr, string expiresInStr)
        {
            // 有效时长（秒）优先
            if (long.TryParse(expiresInStr, out var seconds) && seconds > 0)
                return DateTime.Now.AddSeconds(seconds);

            if (string.IsNullOrEmpty(expireAtStr)) return null;

            // 支持 ISO 格式 "yyyy-MM-dd HH:mm:ss" / "yyyy-MM-ddTHH:mm:ss"
            if (DateTime.TryParse(expireAtStr, out var dt)) return dt;

            // 支持 Unix 秒 / 毫秒时间戳
            if (long.TryParse(expireAtStr, out var ts))
            {
                return ts > 100000000000L
                    ? DateTimeOffset.FromUnixTimeMilliseconds(ts).LocalDateTime
                    : DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime;
            }

            return null;
        }

        private static void Upsert(int userId, string playerName, string permission,
            string grantedBy, string note, DateTime? expireAt)
        {
            var now = DateTime.Now.ToString(DateFormat);
            var expire = expireAt?.ToString(DateFormat);

            TShock.DB.Query($@"
                INSERT INTO {Table} (UserId, PlayerName, Permission, GrantedBy, Note, CreatedAt, ExpireAt)
                VALUES (@0, @1, @2, @3, @4, @5, @6)
                ON CONFLICT(UserId, Permission) DO UPDATE SET
                    PlayerName = excluded.PlayerName,
                    GrantedBy  = excluded.GrantedBy,
                    Note       = excluded.Note,
                    CreatedAt  = excluded.CreatedAt,
                    ExpireAt   = excluded.ExpireAt",
                userId, playerName, permission, grantedBy, note, now, expire);

            lock (CacheLock)
            {
                var perms = GetOrLoad(userId);
                perms[permission] = expireAt;
            }
        }

        private static bool Revoke(int userId, string permission)
        {
            var affected = TShock.DB.Query($"DELETE FROM {Table} WHERE UserId = @0 AND Permission = @1", userId, permission);
            if (affected > 0)
            {
                lock (CacheLock)
                {
                    if (Cache.TryGetValue(userId, out var perms))
                        perms.Remove(permission);
                }
            }
            return affected > 0;
        }

        // ═══════════════ 过期清理 ═══════════════

        private static void CleanupExpiredSafe()
        {
            try { CleanupExpired(); }
            catch (Exception ex) { TShock.Log.ConsoleError($"[TSWeb] 过期权限清理失败: {ex.Message}"); }
        }

        private static void CleanupExpired()
        {
            var now = DateTime.Now.ToString(DateFormat);
            var expired = new List<(int userId, string permission)>();
            using (var reader = TShock.DB.QueryReader(
                $"SELECT UserId, Permission FROM {Table} WHERE ExpireAt IS NOT NULL AND ExpireAt <> '' AND ExpireAt <= @0", now))
            {
                while (reader.Read())
                {
                    expired.Add((reader.Get<int>("UserId"), reader.Get<string>("Permission") ?? ""));
                }
            }

            if (expired.Count == 0) return;

            TShock.DB.Query($"DELETE FROM {Table} WHERE ExpireAt IS NOT NULL AND ExpireAt <> '' AND ExpireAt <= @0", now);

            // 同步清理缓存中的过期条目
            foreach (var (uid, perm) in expired)
            {
                lock (CacheLock)
                {
                    if (Cache.TryGetValue(uid, out var perms))
                        perms.Remove(perm);
                }
            }

            TShock.Log.ConsoleInfo($"[TSWeb] 个人权限过期清理: {expired.Count} 条");
        }

        // ═══════════════ REST API ═══════════════

        private static string GetParam(RestRequestArgs args, string key)
        {
            try
            {
                var v = args.Parameters[key];
                return v?.ToString() ?? "";
            }
            catch { return ""; }
        }

        private static List<string> GetJsonArray(RestRequestArgs args, string key)
        {
            var raw = GetParam(args, key);
            if (string.IsNullOrWhiteSpace(raw)) return new List<string>();
            try
            {
                return JsonConvert.DeserializeObject<List<string>>(raw) ?? new List<string>();
            }
            catch
            {
                // 兼容逗号分隔格式
                return raw.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            }
        }

        private static RestObject Err(string message) => new("400") { { "error", message } };

        /// <summary>GET /data/permissions/summary — 聚合统计（玩家/权限两个维度 + 数量 + 最近签发时间）</summary>
        public static object SummaryApi(RestRequestArgs args)
        {
            try
            {
                var players = new List<Dictionary<string, object>>();
                using (var reader = TShock.DB.QueryReader(
                    $"SELECT PlayerName, COUNT(*) AS Cnt, MAX(CreatedAt) AS LastAt FROM {Table} GROUP BY PlayerName"))
                {
                    while (reader.Read())
                    {
                        players.Add(new Dictionary<string, object>
                        {
                            { "player", reader.Get<string>("PlayerName") ?? "" },
                            { "count", (int)reader.Get<long>("Cnt") },
                            { "lastGrantedAt", reader.Get<string>("LastAt") ?? "" }
                        });
                    }
                }

                var perms = new List<Dictionary<string, object>>();
                using (var reader = TShock.DB.QueryReader(
                    $"SELECT Permission, COUNT(*) AS Cnt, MAX(CreatedAt) AS LastAt FROM {Table} GROUP BY Permission"))
                {
                    while (reader.Read())
                    {
                        perms.Add(new Dictionary<string, object>
                        {
                            { "permission", reader.Get<string>("Permission") ?? "" },
                            { "count", (int)reader.Get<long>("Cnt") },
                            { "lastGrantedAt", reader.Get<string>("LastAt") ?? "" }
                        });
                    }
                }

                return new RestObject
                {
                    // 注意：RestObject 无参构造已内置 status="200"，勿再 Add "status" 键（会抛重复键异常）
                    { "players", players },
                    { "permissions", perms }
                };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }

        /// <summary>GET /data/permissions/list — 明细（全量返回，排序/筛选由前端完成）</summary>
        public static object ListApi(RestRequestArgs args)
        {
            try
            {
                var player = GetParam(args, "player").Trim();
                var permission = GetParam(args, "permission").Trim();
                var grantedBy = GetParam(args, "grantedBy").Trim();
                var status = GetParam(args, "status").Trim().ToLowerInvariant(); // active / expired / all

                var where = new List<string>();
                var ps = new List<object>();
                var idx = 0;
                if (player.Length > 0) { where.Add($"PlayerName LIKE @{idx++}"); ps.Add($"%{player}%"); }
                if (permission.Length > 0) { where.Add($"Permission LIKE @{idx++}"); ps.Add($"%{permission}%"); }
                if (grantedBy.Length > 0) { where.Add($"GrantedBy LIKE @{idx++}"); ps.Add($"%{grantedBy}%"); }

                var now = DateTime.Now.ToString(DateFormat);
                if (status == "active")
                {
                    where.Add($"(ExpireAt IS NULL OR ExpireAt = '' OR ExpireAt > @{idx++})");
                    ps.Add(now);
                }
                else if (status == "expired")
                {
                    where.Add($"(ExpireAt IS NOT NULL AND ExpireAt <> '' AND ExpireAt <= @{idx++})");
                    ps.Add(now);
                }

                var sql = $"SELECT * FROM {Table}";
                if (where.Count > 0) sql += " WHERE " + string.Join(" AND ", where);

                var items = new List<Dictionary<string, object>>();
                using (var reader = TShock.DB.QueryReader(sql, ps.ToArray()))
                {
                    while (reader.Read())
                    {
                        items.Add(new Dictionary<string, object>
                        {
                            { "id", reader.Get<int>("Id") },
                            { "userId", reader.Get<int>("UserId") },
                            { "player", reader.Get<string>("PlayerName") ?? "" },
                            { "permission", reader.Get<string>("Permission") ?? "" },
                            { "grantedBy", reader.Get<string>("GrantedBy") ?? "" },
                            { "note", reader.Get<string>("Note") ?? "" },
                            { "createdAt", reader.Get<string>("CreatedAt") ?? "" },
                            { "expireAt", reader.Get<string>("ExpireAt") ?? "" }
                        });
                    }
                }

                return new RestObject
                {
                    // 注意：RestObject 无参构造已内置 status="200"
                    { "items", items },
                    { "total", items.Count }
                };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }

        /// <summary>POST /data/permissions/grant — 单条快速签发</summary>
        public static object GrantApi(RestRequestArgs args)
        {
            try
            {
                var player = GetParam(args, "player").Trim();
                var permission = GetParam(args, "permission").Trim();
                var grantedBy = GetParam(args, "grantedBy").Trim();
                var note = GetParam(args, "note").Trim();
                var expireAtStr = GetParam(args, "expireAt").Trim();
                var expiresInStr = GetParam(args, "expiresIn").Trim();

                if (player.Length == 0) return Err("player 参数必填");
                if (permission.Length == 0) return Err("permission 参数必填");

                var permError = ValidatePermission(permission);
                if (permError != null) return Err(permError);

                var account = TShock.UserAccounts.GetUserAccountByName(player);
                if (account == null) return Err($"玩家 {player} 不存在");

                var expireAt = ParseExpireAt(expireAtStr, expiresInStr);

                Upsert(account.ID, account.Name, permission,
                    string.IsNullOrEmpty(grantedBy) ? "unknown" : grantedBy, note, expireAt);

                TShock.Log.ConsoleInfo($"[TSWeb] 个人权限签发: {account.Name} + {permission}" +
                    (expireAt.HasValue ? $" (至 {expireAt.Value:yyyy-MM-dd HH:mm:ss})" : " (永久)") +
                    $" 签发人:{grantedBy}");

                return new RestObject
                {
                    { "response", "签发成功" },
                    { "player", account.Name },
                    { "permission", permission },
                    { "expireAt", expireAt?.ToString(DateFormat) ?? "" },
                    { "note", note }
                };
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 个人权限签发失败: {ex.Message}");
                return new RestObject("500") { { "error", ex.Message } };
            }
        }

        /// <summary>POST /data/permissions/grant-batch — 批量签发（players × permissions 全组合）</summary>
        public static object GrantBatchApi(RestRequestArgs args)
        {
            try
            {
                var playersRaw = GetJsonArray(args, "players");
                var permissionsRaw = GetJsonArray(args, "permissions");
                var grantedBy = GetParam(args, "grantedBy").Trim();
                var note = GetParam(args, "note").Trim();
                var expireAtStr = GetParam(args, "expireAt").Trim();
                var expiresInStr = GetParam(args, "expiresIn").Trim();

                playersRaw = playersRaw.Where(p => p.Length > 0).Distinct().ToList();
                permissionsRaw = permissionsRaw.Where(p => p.Length > 0).Distinct().ToList();

                if (playersRaw.Count == 0) return Err("players 至少需要一个玩家");
                if (permissionsRaw.Count == 0) return Err("permissions 至少需要一个权限");

                var expireAt = ParseExpireAt(expireAtStr, expiresInStr);
                var actor = string.IsNullOrEmpty(grantedBy) ? "unknown" : grantedBy;
                var total = playersRaw.Count * permissionsRaw.Count;

                // 预解析账号，无效玩家一次性提示。
                // 注意：用默认（大小写敏感）键——"仅大小写不同"的账号是两个独立账号，
                // 若用 OrdinalIgnoreCase 键会互相覆盖导致权限签发到错误的账号
                var accounts = new Dictionary<string, UserAccount>();
                foreach (var p in playersRaw)
                {
                    var acc = TShock.UserAccounts.GetUserAccountByName(p);
                    if (acc != null) accounts[p] = acc;
                }

                var success = 0;
                var failures = new List<Dictionary<string, object>>();
                foreach (var p in playersRaw)
                {
                    if (!accounts.TryGetValue(p, out var acc))
                    {
                        failures.Add(new Dictionary<string, object> { { "player", p }, { "reason", "玩家不存在" } });
                        continue;
                    }
                    foreach (var perm in permissionsRaw)
                    {
                        var permError = ValidatePermission(perm);
                        if (permError != null)
                        {
                            failures.Add(new Dictionary<string, object> { { "player", p }, { "permission", perm }, { "reason", permError } });
                            continue;
                        }
                        try
                        {
                            Upsert(acc.ID, acc.Name, perm, actor, note, expireAt);
                            success++;
                        }
                        catch (Exception ex)
                        {
                            failures.Add(new Dictionary<string, object> { { "player", p }, { "permission", perm }, { "reason", ex.Message } });
                        }
                    }
                }

                TShock.Log.ConsoleInfo($"[TSWeb] 个人权限批量签发: 成功 {success}/{total} 条 (签发人:{actor})");

                return new RestObject
                {
                    { "response", $"批量签发完成: 成功 {success} / 失败 {failures.Count}" },
                    { "success", success },
                    { "failed", failures.Count },
                    { "total", total },
                    { "failures", failures },
                    { "expireAt", expireAt?.ToString(DateFormat) ?? "" }
                };
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 个人权限批量签发失败: {ex.Message}");
                return new RestObject("500") { { "error", ex.Message } };
            }
        }

        /// <summary>POST /data/permissions/revoke — 回收单条</summary>
        public static object RevokeApi(RestRequestArgs args)
        {
            try
            {
                var player = GetParam(args, "player").Trim();
                var permission = GetParam(args, "permission").Trim();
                if (player.Length == 0) return Err("player 参数必填");
                if (permission.Length == 0) return Err("permission 参数必填");

                var account = TShock.UserAccounts.GetUserAccountByName(player);
                if (account == null) return Err($"玩家 {player} 不存在");

                if (!Revoke(account.ID, permission))
                    return new RestObject("404") { { "error", $"玩家 {player} 没有权限 {permission}" } };

                TShock.Log.ConsoleInfo($"[TSWeb] 个人权限回收: {account.Name} - {permission}");
                return new RestObject { { "response", "回收成功" }, { "player", account.Name }, { "permission", permission } };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }

        /// <summary>POST /data/permissions/revoke-batch — 批量回收（players × permissions 全组合）</summary>
        public static object RevokeBatchApi(RestRequestArgs args)
        {
            try
            {
                var playersRaw = GetJsonArray(args, "players");
                var permissionsRaw = GetJsonArray(args, "permissions");
                playersRaw = playersRaw.Where(p => p.Length > 0).Distinct().ToList();
                permissionsRaw = permissionsRaw.Where(p => p.Length > 0).Distinct().ToList();

                if (playersRaw.Count == 0) return Err("players 至少需要一个玩家");
                if (permissionsRaw.Count == 0) return Err("permissions 至少需要一个权限");

                var success = 0;
                var failed = 0;
                foreach (var p in playersRaw)
                {
                    var acc = TShock.UserAccounts.GetUserAccountByName(p);
                    if (acc == null) { failed++; continue; }
                    foreach (var perm in permissionsRaw)
                    {
                        if (Revoke(acc.ID, perm)) success++;
                        else failed++;
                    }
                }

                TShock.Log.ConsoleInfo($"[TSWeb] 个人权限批量回收: 成功 {success} / 失败 {failed} 条");
                return new RestObject
                {
                    { "response", $"批量回收完成: 成功 {success} / 失败 {failed}" },
                    { "success", success },
                    { "failed", failed }
                };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }

        /// <summary>POST /data/permissions/cleanup — 手动清理过期权限</summary>
        public static object CleanupApi(RestRequestArgs args)
        {
            try
            {
                var before = 0L;
                using (var r = TShock.DB.QueryReader(
                    $"SELECT COUNT(*) AS Cnt FROM {Table} WHERE ExpireAt IS NOT NULL AND ExpireAt <> '' AND ExpireAt <= @0",
                    DateTime.Now.ToString(DateFormat)))
                {
                    if (r.Read()) before = r.Get<long>("Cnt");
                }
                CleanupExpired();
                return new RestObject { { "response", $"已清理 {before} 条过期权限" }, { "cleaned", before } };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }
    }
}
