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

    /// <summary>
    /// 旧库补列清单（只列「可空或带 DEFAULT」的列，NOT NULL 列不会缺）。
    /// 必须覆盖建表语句里所有可缺的列：CREATE TABLE IF NOT EXISTS 对已存在的老表不做任何补列，
    /// 缺列会导致两处故障——UPDATE 抛 no such column（该权限永远改不动、界面报「设置失败」），
    /// 读取侧 SafeGetInt 又把缺列一律当 0（恒定显示为禁止）。
    /// </summary>
    private static readonly string[] ColumnMigrations =
    {
        "Owners TEXT DEFAULT ''",
        "Users TEXT DEFAULT ''",
        "TpX INTEGER",
        "TpY INTEGER",
        "ExpelX INTEGER",
        "ExpelY INTEGER",
        "ExpelOnViolate INTEGER DEFAULT 0",
        "NotifyBreakPlace INTEGER DEFAULT 1",
        "NotifyEnter INTEGER DEFAULT 0",
        "AllowEntry INTEGER DEFAULT 1",
        "AllowTP INTEGER DEFAULT 0",
        "AllowPlace INTEGER DEFAULT 0",
        "AllowBreak INTEGER DEFAULT 0",
        "AllowExplosion INTEGER DEFAULT 0",
        "AllowLiquid INTEGER DEFAULT 0",
        "AllowChest INTEGER DEFAULT 0",
        "AllowPlant INTEGER DEFAULT 0",
        "AllowSpawn INTEGER DEFAULT 1",
        "AllowGrave INTEGER DEFAULT 1",
        "AllowSwitch INTEGER DEFAULT 1",
        "AllowDoor INTEGER DEFAULT 1",
        "AllowFragile INTEGER DEFAULT 1",
        "Commands TEXT DEFAULT '[]'",
    };

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
                AllowFragile INTEGER DEFAULT 1,

                Commands     TEXT    DEFAULT '[]'
            );
        ";
        cmd.ExecuteNonQuery();

        // 旧库补列：SQLite 不支持 ADD COLUMN IF NOT EXISTS，逐个尝试、列已存在时报错忽略。
        // 列定义来自本文件内的常量数组，不含任何外部输入。
        int added = 0;
        foreach (var column in ColumnMigrations)
        {
            try
            {
                using var alter = conn.CreateCommand();
                alter.CommandText = $"ALTER TABLE HousingDistrict ADD COLUMN {column}";
                alter.ExecuteNonQuery();
                added++;
            }
            catch
            {
                // 列已存在，忽略
            }
        }

        if (added > 0)
            TShockAPI.TShock.Log.ConsoleInfo($"[房屋] 数据库结构升级完成，补齐 {added} 个缺失列");
    }
}
