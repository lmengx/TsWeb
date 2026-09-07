using System;
using TShockAPI;
using TShockAPI.DB;

namespace TShockData
{
    /// <summary>
    /// 账号名解析辅助：统一"仅大小写不同"账号的匹配规则。
    ///
    /// 背景：TShock Users 表的 Username 列无 COLLATE NOCASE，SQLite 默认大小写敏感，
    /// 因此 "Alice" 与 "alice" 可以是两个独立账号。项目此前 SQL（大小写敏感）与
    /// C# 内存比较（OrdinalIgnoreCase / ToLower）规则互相矛盾，导致：
    ///   - 同一名字在不同接口定位到不同账号（前端信息错位）
    ///   - 关联玩家检测（duplicateips）last-match-wins 命中错误目标
    ///
    /// 统一规则：
    ///   1. 精确大小写优先（命中唯一约束索引，行为与 TShock 原生一致）；
    ///   2. 无精确匹配时按大小写不敏感兜底，取 ID 最小的（先注册的）账号，
    ///      保证多个大小写变体共存时结果是确定性的。
    /// </summary>
    public static class UserAccountHelper
    {
        /// <summary>
        /// 按名字解析账号：先精确匹配，再大小写不敏感兜底（取 ID 最小的）。
        /// 找不到返回 null。
        /// </summary>
        public static UserAccount? FindUserAccountByName(string? name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            // 1) 精确大小写（TShock 原生索引查询）
            try
            {
                var exact = TShock.UserAccounts.GetUserAccountByName(name);
                if (exact != null) return exact;
            }
            catch
            {
                // 降级到兜底查询
            }

            // 2) 大小写不敏感兜底：LOWER 比较，按 ID 升序取第一个（先注册者优先，确定性）
            try
            {
                using var res = TShock.DB.QueryReader(
                    "SELECT ID FROM Users WHERE LOWER(Username) = LOWER(@0) ORDER BY ID LIMIT 1", name);
                if (res.Read())
                {
                    return TShock.UserAccounts.GetUserAccountByID(res.Get<int>("ID"));
                }
            }
            catch
            {
                // 表结构异常等情况：返回 null，由调用方按"找不到玩家"处理
            }

            return null;
        }

        /// <summary>
        /// 返回当前在线且已登录到指定账号（大小写不敏感）的玩家。
        /// 用账号名而不是角色名匹配，避免 FindByNameOrID 的前缀/大小写不敏感歧义。
        /// 多个角色登录同一账号时返回第一个。
        /// </summary>
        public static TSPlayer? FindOnlinePlayerByAccount(string? accountName)
        {
            if (string.IsNullOrEmpty(accountName)) return null;
            foreach (var p in TShock.Players)
            {
                if (p == null || !p.Active) continue;
                if (p.Account != null &&
                    p.Account.Name.Equals(accountName, StringComparison.OrdinalIgnoreCase))
                    return p;
            }
            return null;
        }
    }
}
