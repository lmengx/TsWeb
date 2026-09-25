using System.Data;
using TShockAPI;
using TShockAPI.DB;

namespace TShockData
{
    /// <summary>
    /// 跨数据库方言兼容助手（SQLite / MySQL / Postgres）。
    ///
    /// TShock 的 TShock.DB 只负责参数绑定（@0/@1 → AddParameter），
    /// SQL 方言差异需要插件自行处理（参考 TShock 源码 GroupManager.cs:349-353
    /// 的 GetSqlType() switch 模式）。
    ///
    /// 本助手集中封装 TSWeb 用到的三种方言差异：
    ///   - 自增主键列定义（SQLite: INTEGER PRIMARY KEY AUTOINCREMENT / MySQL: INT AUTO_INCREMENT PRIMARY KEY）
    ///   - 建索引的 IF NOT EXISTS 语义（SQLite/Postgres 原生支持；MySQL 需先查 information_schema.statistics）
    ///   - upsert 冲突子句（SQLite: ON CONFLICT(...) DO UPDATE SET / MySQL: ON DUPLICATE KEY UPDATE）
    /// </summary>
    public static class SqlCompat
    {
        /// <summary>当前 TShock 数据库是否为 MySQL。</summary>
        public static bool IsMysql => TShock.DB.GetSqlType() == SqlType.Mysql;

        /// <summary>自增主键列定义（表内唯一 Id 列）：SQLite 用 AUTOINCREMENT，MySQL 用 AUTO_INCREMENT。</summary>
        public static string AutoIncrementIdColumn =>
            IsMysql ? "INT AUTO_INCREMENT PRIMARY KEY" : "INTEGER PRIMARY KEY AUTOINCREMENT";

        /// <summary>
        /// 创建索引（带 IF NOT EXISTS 语义）。
        /// SQLite/Postgres 原生支持 CREATE INDEX IF NOT EXISTS；MySQL 不支持该语法，
        /// 改为先查 information_schema.statistics 判断索引是否已存在，再决定是否创建。
        /// </summary>
        /// <param name="indexName">索引名</param>
        /// <param name="table">表名</param>
        /// <param name="columns">索引列（逗号分隔）</param>
        public static void CreateIndexIfNotExists(string indexName, string table, string columns)
        {
            try
            {
                if (IsMysql)
                {
                    using var reader = TShock.DB.QueryReader(
                        "SELECT COUNT(*) AS cnt FROM information_schema.statistics " +
                        "WHERE table_schema = DATABASE() AND table_name = @0 AND index_name = @1",
                        table, indexName);
                    if (reader.Read() && reader.Get<int>("cnt") > 0)
                        return;
                    TShock.DB.Query($"CREATE INDEX {indexName} ON {table} ({columns})");
                }
                else
                {
                    // SQLite / Postgres：原生支持 IF NOT EXISTS
                    TShock.DB.Query($"CREATE INDEX IF NOT EXISTS {indexName} ON {table} ({columns})");
                }
            }
            catch
            {
                // 并发创建/索引已存在等竞态场景：忽略，不阻断主流程
            }
        }

        /// <summary>
        /// 生成 upsert 的冲突更新子句（不含分号），拼在 INSERT ... VALUES 之后。
        /// </summary>
        /// <param name="conflictTarget">SQLite 的 ON CONFLICT 目标列（如 "uid, date"）；MySQL 忽略</param>
        /// <param name="sqliteAssignments">SQLite 的 DO UPDATE SET 赋值段（可用 excluded.列 引用新值）</param>
        /// <param name="mysqlAssignments">MySQL 的 ON DUPLICATE KEY UPDATE 赋值段（可用 VALUES(列) 引用新值）</param>
        public static string UpsertClause(string conflictTarget, string sqliteAssignments, string mysqlAssignments)
        {
            return IsMysql
                ? $"ON DUPLICATE KEY UPDATE {mysqlAssignments}"
                : $"ON CONFLICT({conflictTarget}) DO UPDATE SET {sqliteAssignments}";
        }
    }
}
