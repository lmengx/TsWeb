using Microsoft.Data.Sqlite;

namespace HouseRegion;

public static class Database
{
    private static readonly string DbPath = Path.Combine(TShockAPI.TShock.SavePath, "HouseRegion.sqlite");

    private static string ConnectionString => $"Data Source={DbPath}";

    public static SqliteConnection GetConnection()
    {
        var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        return conn;
    }

    public static void EnsureTable()
    {
        using var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS HousingDistrict (
                ID        INTEGER PRIMARY KEY AUTOINCREMENT,
                Name      TEXT    UNIQUE NOT NULL,
                TopX      INTEGER NOT NULL,
                TopY      INTEGER NOT NULL,
                Width     INTEGER NOT NULL,
                Height    INTEGER NOT NULL,
                Author    TEXT    NOT NULL,
                Owners    TEXT    DEFAULT '',
                Users     TEXT    DEFAULT '',
                WorldID   TEXT    NOT NULL,

                TpX       INTEGER,
                TpY       INTEGER,
                ExpelX    INTEGER,
                ExpelY    INTEGER,
                ExpelOnViolate INTEGER DEFAULT 0,

                NotifyBreakPlace INTEGER DEFAULT 1,
                NotifyEnter       INTEGER DEFAULT 0,

                AllowEntry   INTEGER DEFAULT 1,
                AllowTP      INTEGER DEFAULT 0,
                AllowPlace   INTEGER DEFAULT 0,
                AllowBreak   INTEGER DEFAULT 0,
                AllowExplosion INTEGER DEFAULT 0,
                AllowLiquid  INTEGER DEFAULT 0,
                AllowChest   INTEGER DEFAULT 0,
                AllowPlant   INTEGER DEFAULT 0,
                AllowSpawn   INTEGER DEFAULT 1,
                AllowGrave   INTEGER DEFAULT 1,
                AllowSwitch  INTEGER DEFAULT 1,
                AllowDoor    INTEGER DEFAULT 1,
                AllowFragile INTEGER DEFAULT 1
            );
        ";
        cmd.ExecuteNonQuery();
        // 不做 ALTER TABLE 迁移：旧表结构缺列时，读取路径（Utils.ReadHouse 的 SafeGetInt）
        // 自动兜底为默认值 0，保证任何时刻、任何历史表结构下插件都能正常加载。
    }
}
