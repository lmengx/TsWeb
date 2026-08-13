using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;
using TShockAPI.Hooks;

namespace TShockData
{
    /// <summary>
    /// QQ 账号台账同步（AccountSync）：
    ///
    /// 架构（后端为中心台账，本插件为消费端；UUID 只转发不落后端）：
    ///   - 后端 /tsweb/qqsync POST 推送（HMAC-SHA256 签名，与 /hook 协议一致）：
    ///       type=full → 完整台账 { records: { 用户名: { qq, passwordHash } } }
    ///                     对比本地 Users：缺失 → 注册（直插密码哈希，不重新哈希）；密码不同 → 覆盖
    ///       type=uuid → 单条 { username, uuid }（登录设备同步）→ 直接 UPDATE Users.SET UUID 落盘
    ///   - 登录上报（PlayerPostLogin）：本服玩家每次登录成功 → 经 SSE 推送 {username, uuid} 给后端，
    ///       后端只转发给所有启用 syncUUID 的服务器 → 各服落盘覆盖该账号 UUID 字段。
    ///   - 免密逻辑完全交给 TShock 原生：各服数据库该账号 UUID = 最新登录设备，
    ///       TShock 免密判断 account.UUID == player.UUID 自然命中。无多设备集合/内存缓存/hook 副本。
    /// </summary>
    public static class AccountSync
    {
        // ════════════════════════════════════════════
        //  开关（SSE 握手 query 由后端下发）
        // ════════════════════════════════════════════
        private static bool _syncAccounts; // 接收 QQ 台账并创建/覆盖本地账号
        private static bool _syncUuid;     // 接收 UUID 转发并落盘覆盖
        private static bool _initialized;

        /// <summary>
        /// 后端 QQ 台账绑定快照（用户名 → QQ，大小写不敏感）。
        /// 由 /tsweb/qqsync full 推送整体重建；用于玩家登录时识别后端绑定数据（QQ 绑定晋升）。
        /// </summary>
        private static volatile Dictionary<string, string> _qqBindings = new(StringComparer.OrdinalIgnoreCase);

        // ════════════════════════════════════════════
        //  生命周期
        // ════════════════════════════════════════════

        public static void Initialize(TerrariaPlugin plugin)
        {
            if (_initialized) return;
            _initialized = true;

            PlayerHooks.PlayerPostLogin += OnPlayerPostLogin;

            TShock.Log.ConsoleInfo($"[AccountSync] QQ 台账同步已初始化 (syncAccounts={_syncAccounts}, syncUUID={_syncUuid})");
        }

        public static void Dispose(TerrariaPlugin plugin)
        {
            if (!_initialized) return;
            _initialized = false;

            PlayerHooks.PlayerPostLogin -= OnPlayerPostLogin;
            WebhookAuth.ClearNonceCache();
        }

        /// <summary>SSE 握手时由后端下发开关（WebRestServer.HandleSseAsync 调用）</summary>
        public static void SetFlags(bool syncAccounts, bool syncUuid)
        {
            _syncAccounts = syncAccounts;
            _syncUuid = syncUuid;
        }

        public static bool IsSyncAccounts => _syncAccounts;
        public static bool IsSyncUuid => _syncUuid;

        /// <summary>获取账号当前数据库 UUID 字段（绑定 find-account 用，来源为本地 Users 真值）</summary>
        public static string GetUuid(string username)
        {
            try
            {
                var account = TShock.UserAccounts.GetUserAccountByName(username);
                return account?.UUID ?? "";
            }
            catch { return ""; }
        }

        // ════════════════════════════════════════════
        //  /tsweb/qqsync 入口（WebRestServer 调用）
        // ════════════════════════════════════════════

        /// <summary>处理后端同步请求，返回 JSON 响应体。headers 为大小写不敏感的请求头字典。</summary>
        public static string HandleQqSync(string body, Dictionary<string, string> headers)
        {
            if (!WebhookAuth.VerifySignature(headers, body))
                return "{\"status\":\"401\",\"error\":\"Invalid signature\"}";

            try
            {
                var payload = JsonConvert.DeserializeObject<JObject>(body);
                var type = payload?["type"]?.ToString();
                switch (type)
                {
                    case "full":
                        ApplyFull(payload);
                        break;
                    case "uuid":
                        ApplyUuid(payload);
                        break;
                    default:
                        return "{\"status\":\"400\",\"error\":\"Unknown type\"}";
                }
                return "{\"status\":\"200\",\"ok\":true}";
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[AccountSync] 同步处理失败: {ex}");
                return "{\"status\":\"500\",\"error\":\"" + JsonConvert.ToString(ex.Message) + "\"}";
            }
        }

        private static void ApplyFull(JObject payload)
        {
            var records = payload["records"] as JObject;
            if (records == null) return;

            // 先构建新绑定快照，处理完再整体替换：full 是完整台账，
            // 台账中消失的用户 = 已解绑（快照移除，登录不再识别为已绑定）
            var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var prop in records.Properties())
            {
                var username = prop.Name;
                if (string.IsNullOrEmpty(username)) continue;
                var rec = prop.Value as JObject;
                if (rec == null) continue;

                // 绑定快照：record 中 qq 非空即视为已绑定（后端台账权威）
                var qq = rec["qq"]?.ToString() ?? "";
                if (!string.IsNullOrEmpty(qq))
                    bindings[username] = qq;

                var hash = rec["passwordHash"]?.ToString() ?? "";

                // 账号创建/覆盖仅在启用账号同步时执行
                if (!_syncAccounts) continue;
                ApplyAccount(username, hash);
            }

            _qqBindings = bindings;
        }

        /// <summary>玩家登录识别：该用户名在后端 QQ 台账中是否有绑定数据</summary>
        public static bool IsQqBound(string username)
        {
            if (string.IsNullOrEmpty(username)) return false;
            return _qqBindings.ContainsKey(username);
        }

        private static void ApplyAccount(string username, string hash)
        {
            try
            {
                var account = TShock.UserAccounts.GetUserAccountByName(username);
                if (account == null)
                {
                    // 缺失 → 注册（直插密码哈希，不重新哈希，保证与后端一致）
                    if (string.IsNullOrEmpty(hash)) return;
                    // Password 为 internal set，须经构造函数直插哈希（不会重新哈希）
                    var newAcc = new UserAccount(
                        username, hash, "",
                        TShock.Config.Settings.DefaultRegistrationGroupName, "", "", "");
                    try
                    {
                        TShock.UserAccounts.AddUserAccount(newAcc);
                    }
                    catch (UserAccountExistsException) { /* 并发已存在 */ }
                    TShock.Log.ConsoleInfo($"[AccountSync] 已同步创建账号: {username}");
                }
                else
                {
                    // 台账权威：密码不同 → 覆盖（绑定/改密语义）
                    if (!string.IsNullOrEmpty(hash) && !string.Equals(account.Password, hash, StringComparison.Ordinal))
                    {
                        TShock.DB.Query("UPDATE Users SET Password=@0 WHERE Username=@1", hash, username);
                        TShock.Log.ConsoleInfo($"[AccountSync] 已同步覆盖密码: {username}");
                    }
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[AccountSync] 应用账号失败 {username}: {ex.Message}");
            }
        }

        /// <summary>
        /// 收到后端转发的登录设备 UUID → 直接覆盖本地 Users.UUID 落盘。
        /// TShock 原生免密判断 account.UUID == player.UUID 由此命中。
        /// 若后端带 kick 标志（禁止多服登录全局开关），先踢掉本服同名在线角色。
        /// 注意：后端只转发给启用 syncUUID 的服务器，故此处天然满足"不开 uuid 同步就不踢"。
        /// </summary>
        private static void ApplyUuid(JObject payload)
        {
            var username = payload["username"]?.ToString();
            var uuid = payload["uuid"]?.ToString();
            var kick = payload["kick"]?.Value<bool>() ?? false;

            // 禁止多服登录：本服存在同名在线且已登录角色 → 踢掉（独立于 UUID 落盘开关）
            if (kick)
                TryKickDuplicate(username);

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(uuid)) return;
            if (!IsValidUuid(uuid))
            {
                TShock.Log.ConsoleWarn($"[AccountSync] 忽略非法 UUID: {uuid}");
                return;
            }
            if (!_syncUuid) return;

            try
            {
                // 接收端静默落盘（无需刷屏）；仅异常时输出错误
                TShock.DB.Query("UPDATE Users SET UUID=@0 WHERE Username=@1", uuid, username);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[AccountSync] UUID 落盘失败 {username}: {ex.Message}");
            }
        }

        /// <summary>禁止多服登录：踢掉本服同名的在线已登录角色</summary>
        private static void TryKickDuplicate(string? username)
        {
            if (string.IsNullOrEmpty(username)) return;
            foreach (var p in TShock.Players)
            {
                if (p == null || !p.Active) continue;
                if (!p.Name.Equals(username, StringComparison.OrdinalIgnoreCase)) continue;
                if (!p.IsLoggedIn) continue;   // 只踢已登录的同名角色（未登录的不构成重复）
                try
                {
                    TShock.Log.ConsoleInfo($"[AccountSync] 禁止多服登录：踢出 {username}（已在其他服务器登录）");
                    p.Kick("该账号已在其他服务器登录，已自动退出", true);
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleError($"[AccountSync] 踢出重复登录失败 {username}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 登录成功：本服玩家每次登录 → 经 SSE 推送 {username, uuid} 给后端。
        /// 后端只转发给其他启用 syncUUID 的服务器落盘覆盖。本服不依赖上报。
        /// </summary>
        private static void OnPlayerPostLogin(PlayerPostLoginEventArgs e)
        {
            var p = e.Player;
            if (p == null) return;

            // QQ 绑定晋升：登录时识别一次后端台账绑定数据（QQ 机器人已迁移到后端 /api/bot/*），
            // 与下方 UUID 上报解耦：无论设备 UUID 是否可上报，已绑定账号登录都参与晋升识别
            try
            {
                QQBind.TryPromoteOnLogin(p);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleWarn($"[AccountSync] QQ 登录晋升失败 {p.Name}: {ex.Message}");
            }

            if (string.IsNullOrEmpty(p.Name) || string.IsNullOrEmpty(p.UUID)) return;
            if (!IsValidUuid(p.UUID)) return;

            var name = p.Name;
            var uuid = p.UUID;
            try
            {
                // 通过已建立的 SSE 连接推送给后端（复用日志通道，插件无需知道后端地址）
                var body = JsonConvert.SerializeObject(new { username = name, uuid });
                WebRestServer.Broadcast("qq-uuid", body);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleWarn($"[AccountSync] 上报登录设备失败 {name}: {ex.Message}");
            }
        }

        /// <summary>客户端 UUID 清洗：仅允许 hex+连字符（不含分隔符/不可见字符），
        /// 上限 128 与 TShock Users.UUID 列宽 VarChar(128) 一致（实测客户端发 128 位 hex）</summary>
        public static bool IsValidUuid(string uuid)
        {
            if (string.IsNullOrEmpty(uuid) || uuid.Length > 128) return false;
            return Regex.IsMatch(uuid, "^[0-9a-fA-F-]+$");
        }
    }
}
