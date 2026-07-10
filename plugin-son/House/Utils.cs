using Microsoft.Xna.Framework;
using System.Text;
using System.Text.RegularExpressions;
using Terraria;
using TShockAPI;
using TShockAPI.DB;

namespace HouseRegion;

class Utils
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
        return Config.Instance.HouseMaxNumber;
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
        return Config.Instance.HouseMaxSize;
    }
    public static House? GetHouseByName(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        for (var i = 0; i < HousingPlugin.Houses.Count; i++)
        {
            var house = HousingPlugin.Houses[i];
            if (house != null && house.Name == name) return house;
        }
        return null;
    }
    public static bool OwnsHouse(UserAccount U, string housename) => U != null && OwnsHouse(U.ID.ToString(), housename);
    public static bool OwnsHouse(UserAccount U, House house) => U != null && OwnsHouse(U.ID.ToString(), house);
    public static bool OwnsHouse(string UserID, string housename)
    {
        if (string.IsNullOrWhiteSpace(UserID) || UserID == "0" || string.IsNullOrEmpty(housename)) return false;
        var H = GetHouseByName(housename);
        return H != null && OwnsHouse(UserID, H);
    }
    public static bool OwnsHouse(string UserID, House house)
    {
        if (!string.IsNullOrEmpty(UserID) && UserID != "0" && house != null)
        {
            try { return house.Owners.Contains(UserID); }
            catch (Exception ex) { TShock.Log.Error("房屋插件错误超标错误:" + ex.ToString()); return false; }
        }
        return false;
    }
    public static bool CanUseHouse(string UserID, House house) => !string.IsNullOrEmpty(UserID) && UserID != "0" && house.Users.Contains(UserID);
    public static bool CanUseHouse(UserAccount U, House house) => U != null && U.ID != 0 && house.Users.Contains(U.ID.ToString());
    public static string? InAreaHouseName(int x, int y)
    {
        for (var i = 0; i < HousingPlugin.Houses.Count; i++)
        {
            var house = HousingPlugin.Houses[i];
            if (house != null && x >= house.HouseArea.Left && x < house.HouseArea.Right && y >= house.HouseArea.Top && y < house.HouseArea.Bottom)
                return house.Name;
        }
        return null;
    }
    public static House? InAreaHouse(int x, int y)
    {
        for (var i = 0; i < HousingPlugin.Houses.Count; i++)
        {
            var house = HousingPlugin.Houses[i];
            if (house != null && x >= house.HouseArea.Left && x < house.HouseArea.Right && y >= house.HouseArea.Top && y < house.HouseArea.Bottom)
                return house;
        }
        return null;
    }
}
public class HouseManager
{
    const string cols = "Name, TopX, TopY, BottomX, BottomY, Author, Owners, WorldID, Locked, Users";
    public static bool AddHouse(int tx, int ty, int width, int height, string housename, string author)
    {
        if (Utils.GetHouseByName(housename) != null) return false;
        try
        {
            TShock.DB.Query("INSERT INTO HousingDistrict (" + cols + ") VALUES (@0, @1, @2, @3, @4, @5, @6, @7, @8, @9);",
                housename, tx, ty, width, height, author, "", Main.worldID.ToString(), 1, "");
        }
        catch (Exception ex) { TShock.Log.Error("房屋插件错误数据库写入错误:" + ex.ToString()); return false; }
        HousingPlugin.Houses.Add(new House(new Rectangle(tx, ty, width, height), author, new List<string>(), housename, true, new List<string>()));
        return true;
    }
    public static bool AddNewOwner(string houseName, string id)
    {
        var house = Utils.GetHouseByName(houseName);
        if (house == null) return false;
        house.Owners.Add(id);
        var sb = new StringBuilder(string.Join(",", house.Owners));
        try { TShock.DB.Query("UPDATE HousingDistrict SET Owners=@0 WHERE Name=@1", sb.ToString(), houseName); }
        catch (Exception ex) { TShock.Log.Error("房屋插件错误数据库修改错误:" + ex.ToString()); return false; }
        return true;
    }
    public static bool AddNewUser(string houseName, string id)
    {
        var house = Utils.GetHouseByName(houseName);
        if (house == null) return false;
        house.Users.Add(id);
        var sb = new StringBuilder(string.Join(",", house.Users));
        try { TShock.DB.Query("UPDATE HousingDistrict SET Users=@0 WHERE Name=@1", sb.ToString(), houseName); }
        catch (Exception ex) { TShock.Log.Error("房屋插件错误数据库修改错误:" + ex.ToString()); return false; }
        return true;
    }
    public static bool DeleteOwner(string houseName, string id)
    {
        var house = Utils.GetHouseByName(houseName);
        if (house == null) return false;
        house.Owners.Remove(id);
        var sb = new StringBuilder(string.Join(",", house.Owners));
        try { TShock.DB.Query("UPDATE HousingDistrict SET Owners=@0 WHERE Name=@1", sb.ToString(), houseName); }
        catch (Exception ex) { TShock.Log.Error("房屋插件错误数据库修改错误:" + ex.ToString()); return false; }
        return true;
    }
    public static bool DeleteUser(string houseName, string id)
    {
        var house = Utils.GetHouseByName(houseName);
        if (house == null) return false;
        house.Users.Remove(id);
        var sb = new StringBuilder(string.Join(",", house.Users));
        try { TShock.DB.Query("UPDATE HousingDistrict SET Users=@0 WHERE Name=@1", sb.ToString(), houseName); }
        catch (Exception ex) { TShock.Log.Error("房屋插件错误数据库修改错误:" + ex.ToString()); return false; }
        return true;
    }
    public static bool RedefineHouse(int tx, int ty, int width, int height, string housename)
    {
        try
        {
            var house = Utils.GetHouseByName(housename);
            if (house == null) return false;
            try { TShock.DB.Query("UPDATE HousingDistrict SET TopX=@0, TopY=@1, BottomX=@2, BottomY=@3, WorldID=@4 WHERE Name=@5",
                    tx, ty, width, height, Main.worldID.ToString(), house.Name); }
            catch (Exception ex) { TShock.Log.Error("房屋插件错误数据库修改错误:" + ex.ToString()); return false; }
            house.HouseArea = new Rectangle(tx, ty, width, height);
        }
        catch (Exception ex) { TShock.Log.Error("房屋插件错误重新定义房屋时出错:" + ex.ToString()); return false; }
        return true;
    }
    public static bool ChangeLock(House house)
    {
        house.Locked = !house.Locked;
        try { TShock.DB.Query("UPDATE HousingDistrict SET Locked=@0 WHERE Name=@1", house.Locked ? 1 : 0, house.Name); }
        catch (Exception ex) { TShock.Log.Error("房屋插件错误修改锁房屋时出错:" + ex.ToString()); return false; }
        return true;
    }
}
