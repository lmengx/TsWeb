using Rests;
using System;
using System.Collections.Generic;
using System.Linq;
using TShockAPI;
using TShockAPI.DB;

namespace TShockData
{
    /// <summary>
    /// 玩家账号管理（UserAdmin）：
    ///   - 账号改名（rename）：按 ID 更新 Users.Username（避开大小写变体歧义），可选同步 acc: 封禁标识
    ///   - 删除账号（delete）：踢出在线 + 可选删 tsCharacter + RemoveUserAccount + 可选删 acc: 封禁
    ///   - UUID 管理（uuid）：清除（空串）/ 替换（合法 hex），强制密码登录或换设备绑定
    /// 后端为联动权威（台账/时长/后端账号由后端 service 处理），本插件只负责本地数据库写操作。
    /// 权限：tsweb.useradmin（REST token 用户需授予）。
    /// </summary>
    public static class UserAdmin
    {
        /// <summary>用户名最大长度（TShock Users.Username 列 VarChar(32)）</summary>
        private const int MaxUsernameLength = 32;

        /// <summary>
        /// 校验新用户名合法性：
        ///  - 非空、去首尾空白后长度 1..32
        ///  - 不含控制字符 / 引号 / 逗号 / 冒号 / 波浪号（这些字符在 TShock 标识符、KnownIPs 等场景有语义冲突）
        ///  - 不与库中其它账号冲突（精确或仅大小写不同均拒绝，避免制造歧义账号）
        /// </summary>
        private static string ValidateNewUsername(string newName)
        {
            if (string.IsNullOrWhiteSpace(newName))
                return "新用户名不能为空";

            var trimmed = newName.Trim();
            if (trimmed.Length > MaxUsernameLength)
                return $"用户名长度不能超过 {MaxUsernameLength} 字符";

            if (trimmed.IndexOfAny(new[] { '~', ':', ',', '"', '\'', '`', '\\', '\r', '\n', '\t' }) >= 0)
                return "用户名包含非法字符（禁止 ~ : , 引号 反斜杠 及控制字符）";

            // 与现库其它账号冲突检测：精确优先，其次大小写不敏感（UserAccountHelper 规则）
            try
            {
                var exact = TShock.UserAccounts.GetUserAccountByName(trimmed);
                if (exact != null)
                    return $"用户名 {trimmed} 已被占用";
            }
            catch { /* 走兜底 */ }

            try
            {
                using var res = TShock.DB.QueryReader(
                    "SELECT ID FROM Users WHERE LOWER(Username) = LOWER(@0) ORDER BY ID LIMIT 1", trimmed);
                if (res.Read())
                    return $"用户名 {trimmed} 已被占用（含仅大小写不同的账号）";
            }
            catch { /* 表结构异常，忽略 */ }

            return null;
        }

        /// <summary>按 id 或 username 解析账号（先精确后大小写兜底；id 优先）</summary>
        private static UserAccount ResolveAccount(RestRequestArgs args, out string error)
        {
            error = null;
            string idStr = null, username = null;
            try { idStr = args.Parameters["id"]; } catch { }
            try { username = args.Parameters["username"]; } catch { }

            if (!string.IsNullOrEmpty(idStr))
            {
                if (int.TryParse(idStr, out int id))
                {
                    var acc = TShock.UserAccounts.GetUserAccountByID(id);
                    if (acc == null) { error = "用户不存在"; return null; }
                    return acc;
                }
                error = "id 参数必须是有效的数字";
                return null;
            }

            if (string.IsNullOrEmpty(username))
            {
                error = "缺少参数: 必须提供 id 或 username";
                return null;
            }

            var resolved = UserAccountHelper.FindUserAccountByName(username);
            if (resolved == null) { error = "用户不存在"; return null; }
            return resolved;
        }

        /// <summary>踢出指定账号名（大小写不敏感）的在线已登录角色</summary>
        private static int KickOnlineByAccount(string accountName, string reason)
        {
            int kicked = 0;
            foreach (var p in TShock.Players)
            {
                if (p == null || !p.Active || p.Account == null) continue;
                if (!p.Account.Name.Equals(accountName, StringComparison.OrdinalIgnoreCase)) continue;
                if (!p.IsLoggedIn) continue;
                try
                {
                    p.Kick(reason, true);
                    kicked++;
                }
                catch { /* 单个踢出失败不阻断整体 */ }
            }
            return kicked;
        }

        /// <summary>
        /// REST: 账号改名
        /// 入参: id 或 username（目标）；newName（新用户名）；updateBans（可选，默认 true，同步 acc: 封禁标识）
        /// 说明: 按 ID 更新 Username，tsCharacter/地区按 ID 关联自然跟随；在线玩家踢出重登。
        /// </summary>
        public static object Rename(RestRequestArgs args)
        {
            var account = ResolveAccount(args, out string error);
            if (account == null)
                return new RestObject("404") { { "error", error ?? "用户不存在" } };

            string newName = null;
            try { newName = args.Parameters["newName"]; } catch { }
            if (string.IsNullOrWhiteSpace(newName))
                return new RestObject("400") { { "error", "缺少参数: newName" } };

            var validateErr = ValidateNewUsername(newName);
            if (validateErr != null)
                return new RestObject("400") { { "error", validateErr } };

            var trimmedNew = newName.Trim();
            if (account.Name.Equals(trimmedNew, StringComparison.Ordinal))
                return new RestObject("400") { { "error", "新用户名与原用户名相同" } };

            bool updateBans = true;
            try
            {
                string ub = args.Parameters["updateBans"];
                if (!string.IsNullOrEmpty(ub))
                    updateBans = ub.Equals("1") || ub.Equals("true", StringComparison.OrdinalIgnoreCase) || ub.Equals("yes", StringComparison.OrdinalIgnoreCase);
            }
            catch { }

            try
            {
                // 1) 踢出在线同名已登录角色（改名后旧名登录态失效）
                int kicked = KickOnlineByAccount(account.Name, "账号已改名，请使用新用户名登录");

                // 2) 按 ID 更新 Username（精确、避开大小写歧义）
                int affected = TShock.DB.Query("UPDATE Users SET Username = @0 WHERE ID = @1", trimmedNew, account.ID);
                if (affected < 1)
                    return new RestObject("500") { { "error", "数据库更新失败" } };

                // 3) 可选：同步 acc: 封禁标识（acc:旧名 → acc:新名）
                int banUpdated = 0;
                if (updateBans)
                {
                    var oldIdent = $"acc:{account.Name}";
                    try
                    {
                        var bans = TShock.Bans.GetBansByIdentifiers(false, oldIdent).ToList();
                        foreach (var b in bans)
                        {
                            if (TShock.Bans.RemoveBan(b.TicketNumber, true))
                            {
                                TShock.Bans.InsertBan(
                                    $"acc:{trimmedNew}", b.Reason, b.BanningUser, b.BanDateTime, b.ExpirationDateTime);
                                banUpdated++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        TShock.Log.ConsoleError($"[UserAdmin] 改名同步封禁标识失败: {ex.Message}");
                    }
                }

                TShock.Log.ConsoleInfo($"[UserAdmin] 账号改名: {account.Name} → {trimmedNew} (ID={account.ID}, 踢出 {kicked}, 封禁同步 {banUpdated})");
                return new RestObject()
                {
                    { "ok", true },
                    { "from", account.Name },
                    { "to", trimmedNew },
                    { "id", account.ID },
                    { "kicked", kicked },
                    { "bansUpdated", banUpdated }
                };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }

        /// <summary>
        /// REST: 删除账号
        /// 入参: id 或 username；deleteCharacter（可选，默认 true，删 tsCharacter）；deleteBans（可选，默认 true，删 acc: 封禁）
        /// 说明: 踢出在线 → 可选删角色数据 → RemoveUserAccount（TShock 原生）→ 可选删 acc: 封禁。
        /// </summary>
        public static object Delete(RestRequestArgs args)
        {
            var account = ResolveAccount(args, out string error);
            if (account == null)
                return new RestObject("404") { { "error", error ?? "用户不存在" } };

            bool deleteCharacter = true;
            bool deleteBans = true;
            try
            {
                string dc = args.Parameters["deleteCharacter"];
                if (!string.IsNullOrEmpty(dc))
                    deleteCharacter = dc.Equals("1") || dc.Equals("true", StringComparison.OrdinalIgnoreCase) || dc.Equals("yes", StringComparison.OrdinalIgnoreCase);
            }
            catch { }
            try
            {
                string db = args.Parameters["deleteBans"];
                if (!string.IsNullOrEmpty(db))
                    deleteBans = db.Equals("1") || db.Equals("true", StringComparison.OrdinalIgnoreCase) || db.Equals("yes", StringComparison.OrdinalIgnoreCase);
            }
            catch { }

            try
            {
                // 1) 踢出在线
                int kicked = KickOnlineByAccount(account.Name, "账号已被管理员删除");

                // 2) 可选：删角色数据
                int characterRows = 0;
                if (deleteCharacter)
                {
                    try
                    {
                        characterRows = TShock.DB.Query("DELETE FROM tsCharacter WHERE Account = @0", account.ID);
                    }
                    catch (Exception ex)
                    {
                        TShock.Log.ConsoleError($"[UserAdmin] 删除角色数据失败: {ex.Message}");
                    }
                }

                // 3) 可选：删 acc: 封禁
                int bansRemoved = 0;
                if (deleteBans)
                {
                    try
                    {
                        var bans = TShock.Bans.GetBansByIdentifiers(false, $"acc:{account.Name}").ToList();
                        foreach (var b in bans)
                        {
                            if (TShock.Bans.RemoveBan(b.TicketNumber, true)) bansRemoved++;
                        }
                    }
                    catch (Exception ex)
                    {
                        TShock.Log.ConsoleError($"[UserAdmin] 删除封禁失败: {ex.Message}");
                    }
                }

                // 4) TShock 原生删除（含在线玩家 Logout + AccountHooks）
                TShock.UserAccounts.RemoveUserAccount(account);

                TShock.Log.ConsoleInfo($"[UserAdmin] 账号删除: {account.Name} (ID={account.ID}, 角色 {characterRows} 行, 封禁 {bansRemoved}, 踢出 {kicked})");
                return new RestObject()
                {
                    { "ok", true },
                    { "deleted", account.Name },
                    { "id", account.ID },
                    { "characterRows", characterRows },
                    { "bansRemoved", bansRemoved },
                    { "kicked", kicked }
                };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }

        /// <summary>
        /// REST: UUID 管理
        /// 入参: id 或 username；uuid（空串 = 清除；非空 = 替换，校验 hex+连字符 ≤128）
        /// 说明: 清除后该账号失去免密，必须密码登录；替换用于换设备手动绑定。
        /// </summary>
        public static object UuidSet(RestRequestArgs args)
        {
            var account = ResolveAccount(args, out string error);
            if (account == null)
                return new RestObject("404") { { "error", error ?? "用户不存在" } };

            string uuid = null;
            try { uuid = args.Parameters["uuid"]; } catch { }
            if (uuid == null)
                return new RestObject("400") { { "error", "缺少参数: uuid（空串清除，非空替换）" } };

            uuid = uuid.Trim();
            if (!string.IsNullOrEmpty(uuid) && !AccountSync.IsValidUuid(uuid))
                return new RestObject("400") { { "error", "UUID 格式非法（仅允许 hex + 连字符，长度不超过 128）" } };

            try
            {
                int affected = TShock.DB.Query("UPDATE Users SET UUID = @0 WHERE ID = @1", uuid, account.ID);
                if (affected < 1)
                    return new RestObject("500") { { "error", "数据库更新失败" } };

                TShock.Log.ConsoleInfo($"[UserAdmin] UUID 更新: {account.Name} (ID={account.ID}, {(string.IsNullOrEmpty(uuid) ? "已清除" : "已替换")})");
                return new RestObject()
                {
                    { "ok", true },
                    { "username", account.Name },
                    { "cleared", string.IsNullOrEmpty(uuid) },
                    { "uuid", uuid }
                };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }
    }
}
