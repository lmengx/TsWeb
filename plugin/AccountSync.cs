using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MonoMod.RuntimeDetour;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Terraria;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;
using TShockAPI.Hooks;

namespace TShockData
{
    /// <summary>
    /// QQ 账号台账同步（AccountSync）：
    ///
    /// 架构（后端为中心台账，本插件为消费端）：
    ///   - 后端 /tsweb/qqsync POST 推送（HMAC-SHA256 签名，与 /hook 协议一致）：
    ///       type=full → 完整台账 { records: { 用户名: { qq, passwordHash, uuidList } } }
    ///                     对比本地 Users：缺失 → 注册（直插密码哈希，不重新哈希）；
    ///                     密码不同 → 覆盖（台账权威）；uuidList → 更新内存缓存
    ///       type=uuid → 单条 { username, uuid }（登录新设备）→ 追加内存缓存
    ///   - 多设备免密登录（syncUUID 开关，SSE 握手时由后端下发）：
    ///       拦截 MessageBuffer.GetData 的 ClientUUID 包拿 (name, uuid) →
    ///       hook GetUserAccountByName：该 uuid 命中账号的已授权设备集合 →
    ///       返回 UUID 字段改为该设备 uuid 的账号副本 → TShock 原生免密判断自然命中
    ///   - 登录成功（PlayerPostLogin）→ 新设备 UUID 上报后端 /hook/qq-uuid（HMAC）
    ///   - 免密判定本地缓存 miss → 查后端 /hook/uuid-check
    ///   - 绑定资格（canBind）：auto 自动注册=0（防抢绑盗号），/pwd 改密成功=1；
    ///       绑定/注册 REST 查询用；find-account 返回
    /// </summary>
    public static class AccountSync
    {
        // ════════════════════════════════════════════
        //  开关（SSE 握手 query 由后端下发）
        // ════════════════════════════════════════════
        private static bool _syncAccounts; // 接收 QQ 台账并创建/覆盖本地账号
        private static bool _syncUuid;     // 多设备 UUID 免密
        private static bool _initialized;

        // ════════════════════════════════════════════
        //  UUID 内存缓存：username → 已授权设备 UUID 集合
        // ════════════════════════════════════════════
        private static readonly Dictionary<string, HashSet<string>> _uuidCache = new();
        private static readonly object _cacheLock = new();

        // 连接期 name → 客户端UUID（ClientUUID 包拦截写入，免密判定用）
        private static readonly Dictionary<string, string> _connectingUuid = new();
        private static readonly object _connectingLock = new();

        // MonoMod hooks
        private static Hook? _hookGetData;
        private static Hook? _hookGetUserByName;

        private static readonly HttpClient _http = new();
        private static readonly TimeSpan _httpTimeout = TimeSpan.FromSeconds(8);

        // ════════════════════════════════════════════
        //  canBind 标记表（本地 SQLite，TShock.DB）
        // ════════════════════════════════════════════
        private static bool _canBindTableChecked;
        private static readonly object _canBindLock = new();
        private static readonly HashSet<string> _nonceCache = new();
        private static readonly object _nonceLock = new();

        // ════════════════════════════════════════════
        //  生命周期
        // ════════════════════════════════════════════

        public static void Initialize(TerrariaPlugin plugin)
        {
            if (_initialized) return;
            _initialized = true;

            EnsureCanBindTable();

            // 拦截 ClientUUID 包（CaiBot 同款：底层网络层，TShock 任何处理之前）
            try
            {
                var getData = typeof(MessageBuffer).GetMethod("GetData",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                    null, new[] { typeof(int), typeof(int), typeof(int).MakeByRefType() }, null);
                if (getData != null)
                    _hookGetData = new Hook(getData, OnMessageBufferGetData);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[AccountSync] MessageBuffer.GetData hook 失败: {ex.Message}");
            }

            // 免密判定：GetUserAccountByName 拦截（连接时 TShock 用它取账号）
            try
            {
                var m = typeof(UserAccountManager).GetMethod("GetUserAccountByName",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (m != null)
                    _hookGetUserByName = new Hook(m, OnGetUserAccountByName);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[AccountSync] GetUserAccountByName hook 失败: {ex.Message}");
            }

            PlayerHooks.PlayerPostLogin += OnPlayerPostLogin;
            ServerApi.Hooks.ServerLeave.Register(plugin, OnServerLeave);

            TShock.Log.ConsoleInfo($"[AccountSync] QQ 台账同步已初始化 (syncAccounts={_syncAccounts}, syncUUID={_syncUuid})");
        }

        public static void Dispose(TerrariaPlugin plugin)
        {
            if (!_initialized) return;
            _initialized = false;

            try { _hookGetData?.Dispose(); } catch { }
            try { _hookGetUserByName?.Dispose(); } catch { }
            _hookGetData = null;
            _hookGetUserByName = null;

            PlayerHooks.PlayerPostLogin -= OnPlayerPostLogin;
            ServerApi.Hooks.ServerLeave.Deregister(plugin, OnServerLeave);

            lock (_cacheLock) _uuidCache.Clear();
            lock (_connectingLock) _connectingUuid.Clear();
            lock (_nonceLock) _nonceCache.Clear();
        }

        /// <summary>SSE 握手时由后端下发开关（WebRestServer.HandleSseAsync 调用）</summary>
        public static void SetFlags(bool syncAccounts, bool syncUuid)
        {
            _syncAccounts = syncAccounts;
            _syncUuid = syncUuid;
        }

        public static bool IsSyncAccounts => _syncAccounts;
        public static bool IsSyncUuid => _syncUuid;

        /// <summary>获取账号已授权设备 UUID 列表（绑定 find-account 用，来源为台账同步缓存）</summary>
        public static List<string> GetUuidList(string username)
        {
            lock (_cacheLock)
            {
                if (_uuidCache.TryGetValue(username, out var set))
                    return set.ToList();
            }
            return new List<string>();
        }

        // ════════════════════════════════════════════
        //  /tsweb/qqsync 入口（WebRestServer 调用）
        // ════════════════════════════════════════════

        /// <summary>处理后端同步请求，返回 JSON 响应体。headers 为大小写不敏感的请求头字典。</summary>
        public static string HandleQqSync(string body, Dictionary<string, string> headers)
        {
            if (!VerifySignature(headers, body))
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

            foreach (var prop in records.Properties())
            {
                var username = prop.Name;
                if (string.IsNullOrEmpty(username)) continue;
                var rec = prop.Value as JObject;
                if (rec == null) continue;

                var hash = rec["passwordHash"]?.ToString() ?? "";
                var uuids = (rec["uuidList"] as JArray)?
                    .Select(x => x.ToString())
                    .Where(IsValidUuid)
                    .ToList() ?? new List<string>();

                // UUID 缓存无条件更新（full 推给 syncAccounts 或 syncUUID 的服）
                lock (_cacheLock)
                {
                    _uuidCache[username] = new HashSet<string>(uuids);
                }

                // 账号创建/覆盖仅在启用账号同步时执行
                if (!_syncAccounts) continue;
                ApplyAccount(username, hash);
            }
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
                    SetCanBind(username, true);
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
                    SetCanBind(username, true);
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[AccountSync] 应用账号失败 {username}: {ex.Message}");
            }
        }

        private static void ApplyUuid(JObject payload)
        {
            var username = payload["username"]?.ToString();
            var uuid = payload["uuid"]?.ToString();
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(uuid)) return;
            if (!IsValidUuid(uuid))
            {
                TShock.Log.ConsoleWarn($"[AccountSync] 忽略非法 UUID: {uuid}");
                return;
            }
            lock (_cacheLock)
            {
                if (!_uuidCache.TryGetValue(username, out var set))
                    _uuidCache[username] = set = new HashSet<string>();
                set.Add(uuid);
            }
            TShock.Log.ConsoleInfo($"[AccountSync] 已更新设备: {username} +{uuid}");
        }

        // ════════════════════════════════════════════
        //  多设备免密登录
        // ════════════════════════════════════════════

        private delegate void OrigMessageBufferGetData(MessageBuffer self, int start, int length, out int messageType);

        private static void OnMessageBufferGetData(OrigMessageBufferGetData orig, MessageBuffer self, int start, int length, out int messageType)
        {
            try
            {
                if (_syncUuid && self != null && self.readerStream != null &&
                    start >= 0 && start + 1 <= self.readerStream.Length)
                {
                    long pos = self.readerStream.Position;
                    self.readerStream.Position = start;
                    int type = self.readerStream.ReadByte();
                    self.readerStream.Position = pos;

                    if (type == (int)PacketTypes.ClientUUID)
                    {
                        long p2 = self.readerStream.Position;
                        self.readerStream.Position = start + 1; // 跳过包类型字节
                        var br = new BinaryReader(self.readerStream, Encoding.UTF8, true);
                        string uuid = br.ReadString();
                        self.readerStream.Position = p2;

                        if (self.whoAmI >= 0 && self.whoAmI < TShock.Players.Length)
                        {
                            var player = TShock.Players[self.whoAmI];
                            if (player != null && !string.IsNullOrEmpty(player.Name) && IsValidUuid(uuid))
                            {
                                lock (_connectingLock)
                                {
                                    _connectingUuid[player.Name] = uuid;
                                }
                            }
                        }
                    }
                }
            }
            catch { /* 解析失败不阻断原始流程 */ }

            orig(self, start, length, out messageType);
        }

        private delegate UserAccount OrigGetUserAccountByName(UserAccountManager self, string name);

        private static UserAccount OnGetUserAccountByName(OrigGetUserAccountByName orig, UserAccountManager self, string name)
        {
            var account = orig(self, name);
            if (account == null || !_syncUuid) return account;

            string clientUuid;
            lock (_connectingLock)
            {
                _connectingUuid.TryGetValue(name ?? "", out clientUuid);
            }
            if (string.IsNullOrEmpty(clientUuid) || !IsValidUuid(clientUuid)) return account;

            if (IsAuthorizedDevice(name, clientUuid))
            {
                // 命中：返回 UUID 改为当前设备 uuid 的副本 → TShock 免密判断 account.UUID == player.UUID 命中
                return new UserAccount(name, account.Password, clientUuid, account.Group,
                    account.Registered, account.LastAccessed, account.KnownIps) { ID = account.ID };
            }
            return account;
        }

        private static bool IsAuthorizedDevice(string name, string uuid)
        {
            lock (_cacheLock)
            {
                if (_uuidCache.TryGetValue(name, out var set) && set.Contains(uuid))
                    return true;
            }
            // miss → 查后端（不可达/不在台账 → 走密码，不阻塞登录）
            var res = QueryBackendUuid(name, uuid);
            return res == true;
        }

        /// <summary>免密判定 miss 时向后端确认设备授权；返回 null 表示无法判定</summary>
        private static bool? QueryBackendUuid(string username, string uuid)
        {
            var hookBase = SSELogger.GetHookBase();
            var secret = SSELogger.GetWebhookSecret();
            var serverId = SSELogger.GetServerId();
            if (string.IsNullOrEmpty(hookBase) || string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(serverId))
                return null;

            var body = JsonConvert.SerializeObject(new { username, uuid });
            var respText = PostSignedAsync(hookBase, "/hook/uuid-check", body, secret, serverId)
                .GetAwaiter().GetResult();
            if (respText == null) return null;

            try
            {
                var res = JsonConvert.DeserializeObject<JObject>(respText);
                if (res?["inList"] != null)
                {
                    // 顺带把后端集合写回本地缓存
                    if (res["uuidList"] is JArray arr)
                    {
                        var list = arr.Select(x => x.ToString()).Where(IsValidUuid).ToList();
                        lock (_cacheLock)
                        {
                            _uuidCache[username] = new HashSet<string>(list);
                        }
                    }
                    return (bool)res["inList"];
                }
            }
            catch { }
            return null;
        }

        /// <summary>登录成功：新设备 UUID 追加本地缓存并上报后端</summary>
        private static void OnPlayerPostLogin(PlayerPostLoginEventArgs e)
        {
            var p = e.Player;
            if (p == null || string.IsNullOrEmpty(p.Name) || string.IsNullOrEmpty(p.UUID)) return;
            if (!IsValidUuid(p.UUID)) return;

            bool isNew;
            lock (_cacheLock)
            {
                if (!_uuidCache.TryGetValue(p.Name, out var set))
                    _uuidCache[p.Name] = set = new HashSet<string>();
                isNew = set.Add(p.UUID);
            }
            lock (_connectingLock)
            {
                _connectingUuid.Remove(p.Name);
            }

            // 无论本服 syncUUID 开关如何都上报：本服是采集点，其他启用服消费
            if (isNew)
            {
                var name = p.Name;
                var uuid = p.UUID;
                _ = Task.Run(() =>
                {
                    try
                    {
                        var hookBase = SSELogger.GetHookBase();
                        var secret = SSELogger.GetWebhookSecret();
                        var serverId = SSELogger.GetServerId();
                        if (string.IsNullOrEmpty(hookBase) || string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(serverId))
                            return;
                        var body = JsonConvert.SerializeObject(new { username = name, uuid });
                        PostSignedAsync(hookBase, "/hook/qq-uuid", body, secret, serverId).GetAwaiter().GetResult();
                        TShock.Log.ConsoleInfo($"[AccountSync] 已上报新设备: {name} +{uuid}");
                    }
                    catch (Exception ex)
                    {
                        TShock.Log.ConsoleWarn($"[AccountSync] 上报新设备失败 {name}: {ex.Message}");
                    }
                });
            }
        }

        private static void OnServerLeave(LeaveEventArgs e)
        {
            if (e.Who >= 0 && e.Who < TShock.Players.Length)
            {
                var p = TShock.Players[e.Who];
                if (p != null)
                {
                    lock (_connectingLock)
                    {
                        _connectingUuid.Remove(p.Name ?? "");
                    }
                }
            }
        }

        // ════════════════════════════════════════════
        //  canBind 绑定资格
        // ════════════════════════════════════════════

        private static void EnsureCanBindTable()
        {
            if (_canBindTableChecked) return;
            try
            {
                TShock.DB.Query(
                    "CREATE TABLE IF NOT EXISTS tsweb_can_bind (" +
                    "Username TEXT PRIMARY KEY, " +
                    "CanBind INTEGER NOT NULL DEFAULT 1" +
                    ")");
                _canBindTableChecked = true;
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[AccountSync] 创建 can_bind 表失败: {ex.Message}");
            }
        }

        public static void SetCanBind(string username, bool canBind)
        {
            if (string.IsNullOrEmpty(username)) return;
            try
            {
                if (!_canBindTableChecked) EnsureCanBindTable();
                TShock.DB.Query(
                    "INSERT INTO tsweb_can_bind (Username, CanBind) VALUES (@0, @1) " +
                    "ON CONFLICT(Username) DO UPDATE SET CanBind=@1",
                    username, canBind ? 1 : 0);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[AccountSync] canBind 写入失败: {ex.Message}");
            }
        }

        /// <summary>默认 true（老账号/手动注册可绑）；auto 自动注册显式置 0</summary>
        public static bool GetCanBind(string username)
        {
            if (string.IsNullOrEmpty(username)) return false;
            try
            {
                if (!_canBindTableChecked) EnsureCanBindTable();
                using var res = TShock.DB.QueryReader("SELECT CanBind FROM tsweb_can_bind WHERE Username=@0", username);
                if (res.Read())
                    return res.Get<int>("CanBind") != 0;
            }
            catch { }
            return true;
        }

        // ════════════════════════════════════════════
        //  HMAC 签名校验 / 签名请求
        // ════════════════════════════════════════════

        private static bool VerifySignature(Dictionary<string, string> headers, string body)
        {
            var secret = SSELogger.GetWebhookSecret();
            if (string.IsNullOrEmpty(secret)) return false;

            if (!headers.TryGetValue("X-Server-Id", out var sid) ||
                !headers.TryGetValue("X-Timestamp", out var tsRaw) ||
                !headers.TryGetValue("X-Nonce", out var nonce) ||
                !headers.TryGetValue("X-Signature", out var sig))
                return false;

            if (!long.TryParse(tsRaw, out var ts)) return false;
            if (Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - ts) > 300_000) return false;

            var key = $"{sid}:{nonce}";
            lock (_nonceLock)
            {
                if (!_nonceCache.Add(key)) return false;
                if (_nonceCache.Count > 10000) _nonceCache.Clear();
            }

            var bodyHash = Sha256Hex(body);
            var expected = HmacSha256Hex(secret, $"{tsRaw}.{nonce}.{bodyHash}");
            return string.Equals(sig, expected, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>POST {hookBase}{path}（HMAC 签名），返回响应体；失败返回 null</summary>
        private static async Task<string?> PostSignedAsync(string hookBase, string path, string body,
            string secret, string serverId)
        {
            var baseUrl = hookBase.TrimEnd('/');
            const string suffix = "/hook/log";
            if (baseUrl.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                baseUrl = baseUrl.Substring(0, baseUrl.Length - suffix.Length);

            var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var nonce = Guid.NewGuid().ToString("N");
            var bodyHash = Sha256Hex(body);
            var signature = HmacSha256Hex(secret, $"{ts}.{nonce}.{bodyHash}");

            using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + path)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            req.Headers.Add("X-Server-Id", serverId);
            req.Headers.Add("X-Timestamp", ts);
            req.Headers.Add("X-Nonce", nonce);
            req.Headers.Add("X-Signature", signature);

            try
            {
                using var cts = new CancellationTokenSource(_httpTimeout);
                using var resp = await _http.SendAsync(req, cts.Token);
                return await resp.Content.ReadAsStringAsync(cts.Token);
            }
            catch
            {
                return null;
            }
        }

        private static string Sha256Hex(string input)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexStringLower(bytes);
        }

        private static string HmacSha256Hex(string key, string input)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
            var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexStringLower(bytes);
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
