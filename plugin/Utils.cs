using Microsoft.Data.Sqlite;
using Microsoft.Xna.Framework;
using System.Text;
using System.Text.RegularExpressions;
using Terraria;
using TShockAPI;

namespace HouseRegion;

static class Utils
{
    public static int MaxCount(TSPlayer ply)
    {
        for (var i = 0; i < ply.Group.permissions.Count; i++)
        {
            var perm = ply.Group.permissions[i];
            var Match = Regex.Match(perm, @"^house\.count\.(\d{1,9})$");
            if (Match.Success)
                return Convert.ToInt32(Match.Groups[1].Value);
        }
        return 2; // 硬编码默认值
    }

    public static int MaxSize(TSPlayer ply)
    {
        for (var i = 0; i < ply.Group.permissions.Count; i++)
        {
            var perm = ply.Group.permissions[i];
            var Match = Regex.Match(perm, @"^house\.size\.(\d{1,9})$");
            if (Match.Success)
                return Convert.ToInt32(Match.Groups[1].Value);
        }
        return 1000; // 硬编码默认值
    }

    public static House? GetHouseByName(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        for (var i = 0; i < HouseCore.Houses.Count; i++)
        {
            var house = HouseCore.Houses[i];
            if (house != null && house.Name == name) return house;
        }
        return null;
    }

    public static House? CurrentHouse(TSPlayer ply)
    {
        return InAreaHouse(ply.TileX, ply.TileY);
    }

    public static bool IsAuthorized(TSPlayer ply, House house)
    {
        if (ply == null || !ply.IsLoggedIn || ply.Account == null) return false;
        var id = ply.Account.ID.ToString();
        return ply.Group.HasPermission(GetDataHandlers.EditHouse) ||
               id == house.Author ||
               OwnsHouse(id, house) ||
               CanUseHouse(id, house);
    }

    public static bool OwnsHouse(string UserID, House house)
    {
        if (!string.IsNullOrEmpty(UserID) && UserID != "0" && house != null)
        {
            try { return house.Owners.Contains(UserID); }
            catch { return false; }
        }
        return false;
    }

    public static bool CanUseHouse(string UserID, House house)
    {
        return !string.IsNullOrEmpty(UserID) && UserID != "0" && house.Users.Contains(UserID);
    }

    public static House? InAreaHouse(int x, int y)
    {
        for (var i = 0; i < HouseCore.Houses.Count; i++)
        {
            var house = HouseCore.Houses[i];
            if (house != null &&
                x >= house.HouseArea.Left && x < house.HouseArea.Right &&
                y >= house.HouseArea.Top && y < house.HouseArea.Bottom)
                return house;
        }
        return null;
    }

    public static string? InAreaHouseName(int x, int y)
    {
        var h = InAreaHouse(x, y);
        return h?.Name;
    }

    /// <summary>
    /// 距离矩形边界的最近距离（四个方向取最小）
    /// </summary>
    public static int DistanceToRect(Point p, Rectangle rect)
    {
        int dx = 0, dy = 0;
        if (p.X < rect.Left) dx = rect.Left - p.X;
        else if (p.X > rect.Right) dx = p.X - rect.Right;
        if (p.Y < rect.Top) dy = rect.Top - p.Y;
        else if (p.Y > rect.Bottom) dy = p.Y - rect.Bottom;
        return Math.Max(dx, dy);
    }
}

public static class HouseManager
{
    private static string ConnStr => Database.GetConnection().ConnectionString;

    public static List<House> LoadAllHouses(string worldId)
    {
        var list = new List<House>();
        using var conn = Database.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM HousingDistrict WHERE WorldID = @world";
        cmd.Parameters.AddWithValue("@world", worldId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(ReadHouse(reader));
        }
        return list;
    }

    public static bool AddHouse(int tx, int ty, int width, int height,
        string housename, string author, int tpX, int tpY)
    {
        if (Utils.GetHouseByName(housename) != null) return false;
        try
        {
            using var conn = Database.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO HousingDistrict
                (Name, TopX, TopY, Width, Height, Author, Owners, Users, WorldID,
                 TpX, TpY)
                VALUES
                (@name, @topx, @topy, @width, @height, @author, '', '', @world,
                 @tpx, @tpy)";
            cmd.Parameters.AddWithValue("@name", housename);
            cmd.Parameters.AddWithValue("@topx", tx);
            cmd.Parameters.AddWithValue("@topy", ty);
            cmd.Parameters.AddWithValue("@width", width);
            cmd.Parameters.AddWithValue("@height", height);
            cmd.Parameters.AddWithValue("@author", author);
            cmd.Parameters.AddWithValue("@world", Terraria.Main.worldID.ToString());
            cmd.Parameters.AddWithValue("@tpx", tpX);
            cmd.Parameters.AddWithValue("@tpy", tpY);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            TShock.Log.Error("房屋插件数据库写入错误:" + ex);
            return false;
        }
        // 从数据库重读以获取完整默认值
        var house = LoadSingle(housename);
        if (house != null) HouseCore.Houses.Add(house);
        return true;
    }

    public static bool DeleteHouse(string name)
    {
        try
        {
            using var conn = Database.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM HousingDistrict WHERE Name = @name";
            cmd.Parameters.AddWithValue("@name", name);
            cmd.ExecuteNonQuery();
            return true;
        }
        catch (Exception ex)
        {
            TShock.Log.Error("房屋插件删除错误:" + ex);
            return false;
        }
    }

    public static bool RedefineHouse(int tx, int ty, int width, int height, string housename)
    {
        try
        {
            using var conn = Database.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE HousingDistrict
                SET TopX=@topx, TopY=@topy, Width=@w, Height=@h, WorldID=@world
                WHERE Name=@name";
            cmd.Parameters.AddWithValue("@topx", tx);
            cmd.Parameters.AddWithValue("@topy", ty);
            cmd.Parameters.AddWithValue("@w", width);
            cmd.Parameters.AddWithValue("@h", height);
            cmd.Parameters.AddWithValue("@world", Terraria.Main.worldID.ToString());
            cmd.Parameters.AddWithValue("@name", housename);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            TShock.Log.Error("房屋插件重新定义错误:" + ex);
            return false;
        }
        var house = Utils.GetHouseByName(housename);
        if (house != null) house.HouseArea = new Rectangle(tx, ty, width, height);
        return true;
    }

    public static bool AddNewOwner(string houseName, string id)
    {
        var house = Utils.GetHouseByName(houseName);
        if (house == null) return false;
        house.Owners.Add(id);
        return UpdateListField(houseName, "Owners", house.Owners);
    }

    public static bool DeleteOwner(string houseName, string id)
    {
        var house = Utils.GetHouseByName(houseName);
        if (house == null) return false;
        house.Owners.Remove(id);
        return UpdateListField(houseName, "Owners", house.Owners);
    }

    public static bool AddNewUser(string houseName, string id)
    {
        var house = Utils.GetHouseByName(houseName);
        if (house == null) return false;
        house.Users.Add(id);
        return UpdateListField(houseName, "Users", house.Users);
    }

    public static bool DeleteUser(string houseName, string id)
    {
        var house = Utils.GetHouseByName(houseName);
        if (house == null) return false;
        house.Users.Remove(id);
        return UpdateListField(houseName, "Users", house.Users);
    }

    public static bool UpdatePermission(string houseName, string field, int value)
    {
        try
        {
            using var conn = Database.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"UPDATE HousingDistrict SET {field}=@val WHERE Name=@name";
            cmd.Parameters.AddWithValue("@val", value);
            cmd.Parameters.AddWithValue("@name", houseName);
            cmd.ExecuteNonQuery();
            return true;
        }
        catch (Exception ex)
        {
            TShock.Log.Error("房屋插件更新权限错误:" + ex);
            return false;
        }
    }

    public static bool UpdateNotify(string houseName, string field, int value)
    {
        return UpdatePermission(houseName, field, value); // 同样逻辑
    }

    public static bool UpdateTP(string houseName, int x, int y)
    {
        try
        {
            using var conn = Database.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE HousingDistrict SET TpX=@x, TpY=@y WHERE Name=@name";
            cmd.Parameters.AddWithValue("@x", x);
            cmd.Parameters.AddWithValue("@y", y);
            cmd.Parameters.AddWithValue("@name", houseName);
            cmd.ExecuteNonQuery();
            return true;
        }
        catch (Exception ex)
        {
            TShock.Log.Error("房屋插件更新传送点错误:" + ex);
            return false;
        }
    }

    public static bool UpdateExpel(string houseName, int x, int y)
    {
        try
        {
            using var conn = Database.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE HousingDistrict SET ExpelX=@x, ExpelY=@y WHERE Name=@name";
            cmd.Parameters.AddWithValue("@x", x);
            cmd.Parameters.AddWithValue("@y", y);
            cmd.Parameters.AddWithValue("@name", houseName);
            cmd.ExecuteNonQuery();
            return true;
        }
        catch (Exception ex)
        {
            TShock.Log.Error("房屋插件更新驱离点错误:" + ex);
            return false;
        }
    }

    // ── 内部方法 ──

    private static House? LoadSingle(string name)
    {
        using var conn = Database.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM HousingDistrict WHERE Name = @name";
        cmd.Parameters.AddWithValue("@name", name);
        using var reader = cmd.ExecuteReader();
        if (reader.Read()) return ReadHouse(reader);
        return null;
    }

    private static House ReadHouse(SqliteDataReader reader)
    {
        var tx = reader.GetInt32(reader.GetOrdinal("TopX"));
        var ty = reader.GetInt32(reader.GetOrdinal("TopY"));
        var w  = reader.GetInt32(reader.GetOrdinal("Width"));
        var h  = reader.GetInt32(reader.GetOrdinal("Height"));
        var owners = SafeSplit(reader, "Owners");
        var users   = SafeSplit(reader, "Users");
        var tpX = SafeGetInt(reader, "TpX");
        var tpY = SafeGetInt(reader, "TpY");
        int? expelX = SafeGetNullableInt(reader, "ExpelX");
        int? expelY = SafeGetNullableInt(reader, "ExpelY");

        return new House(
            new Rectangle(tx, ty, w, h),
            reader.GetString(reader.GetOrdinal("Author")),
            owners,
            reader.GetString(reader.GetOrdinal("Name")),
            users,
            tpX, tpY,
            expelX, expelY,
            SafeGetInt(reader, "ExpelOnViolate"),
            SafeGetInt(reader, "NotifyBreakPlace"),
            SafeGetInt(reader, "NotifyEnter"),
            SafeGetInt(reader, "AllowEntry"),
            SafeGetInt(reader, "AllowTP"),
            SafeGetInt(reader, "AllowPlace"),
            SafeGetInt(reader, "AllowBreak"),
            SafeGetInt(reader, "AllowExplosion"),
            SafeGetInt(reader, "AllowLiquid"),
            SafeGetInt(reader, "AllowChest"),
            SafeGetInt(reader, "AllowPlant"),
            SafeGetInt(reader, "AllowSpawn"),
            SafeGetInt(reader, "AllowGrave"),
            SafeGetInt(reader, "AllowSwitch"),
            SafeGetInt(reader, "AllowDoor"),
            SafeGetInt(reader, "AllowFragile")
        );
    }

    private static bool UpdateListField(string houseName, string field, List<string> list)
    {
        try
        {
            using var conn = Database.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"UPDATE HousingDistrict SET {field}=@val WHERE Name=@name";
            cmd.Parameters.AddWithValue("@val", string.Join(",", list));
            cmd.Parameters.AddWithValue("@name", houseName);
            cmd.ExecuteNonQuery();
            return true;
        }
        catch (Exception ex)
        {
            TShock.Log.Error("房屋插件更新列表字段错误:" + ex);
            return false;
        }
    }

    private static List<string> SafeSplit(SqliteDataReader reader, string col)
    {
        var val = reader.IsDBNull(reader.GetOrdinal(col)) ? "" : reader.GetString(reader.GetOrdinal(col));
        return string.IsNullOrEmpty(val) ? new List<string>() : val.Split(',').ToList();
    }

    private static int SafeGetInt(SqliteDataReader reader, string col)
    {
        try
        {
            var ord = reader.GetOrdinal(col);
            return reader.IsDBNull(ord) ? 0 : reader.GetInt32(ord);
        }
        catch
        {
            // 列不存在（旧表结构未迁移）→ 视为默认值 0，保证旧库/新库任何时刻都能正常加载
            return 0;
        }
    }

    private static int? SafeGetNullableInt(SqliteDataReader reader, string col)
    {
        try
        {
            var ord = reader.GetOrdinal(col);
            return reader.IsDBNull(ord) ? null : reader.GetInt32(ord);
        }
        catch
        {
            // 列不存在（旧表结构）→ null
            return null;
        }
    }
}
