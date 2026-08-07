using Microsoft.Xna.Framework;

namespace HouseRegion;

public class LPlayer
{
    public int Who { get; set; }
    public int TileX { get; set; }
    public int TileY { get; set; }
    public bool Look { get; set; }

    public LPlayer(int who, int lasttileX, int lasttileY)
    {
        Who = who;
        TileX = lasttileX;
        TileY = lasttileY;
        Look = false;
    }
}

public class House
{
    public Rectangle HouseArea { get; set; }
    public string Author { get; set; }
    public List<string> Owners { get; set; }
    public string Name { get; set; }
    public List<string> Users { get; set; }

    // 传送与驱离
    public int TpX { get; set; }
    public int TpY { get; set; }
    public int? ExpelX { get; set; }
    public int? ExpelY { get; set; }
    public int ExpelOnViolate { get; set; }

    // 通知开关（面向屋主）
    public int NotifyBreakPlace { get; set; }
    public int NotifyEnter { get; set; }

    // 权限开关（1=允许，0=禁止）
    public int AllowEntry { get; set; }
    public int AllowTP { get; set; }
    public int AllowPlace { get; set; }
    public int AllowBreak { get; set; }
    public int AllowLiquid { get; set; }
    public int AllowChest { get; set; }
    public int AllowPlant { get; set; }
    public int AllowSpawn { get; set; }
    public int AllowGrave { get; set; }
    public int AllowSwitch { get; set; }
    public int AllowDoor { get; set; }
    public int AllowFragile { get; set; }

    public House(Rectangle housearea, string author, List<string> owners, string name,
                 List<string> users,
                 int tpX, int tpY,
                 int? expelX, int? expelY, int expelOnViolate,
                 int notifyBreakPlace, int notifyEnter,
                 int allowEntry, int allowTP,
                 int allowPlace, int allowBreak, int allowLiquid, int allowChest,
                 int allowPlant, int allowSpawn, int allowGrave,
                 int allowSwitch, int allowDoor, int allowFragile)
    {
        HouseArea = housearea;
        Author = author;
        Owners = owners;
        Name = name;
        Users = users;
        TpX = tpX;
        TpY = tpY;
        ExpelX = expelX;
        ExpelY = expelY;
        ExpelOnViolate = expelOnViolate;
        NotifyBreakPlace = notifyBreakPlace;
        NotifyEnter = notifyEnter;
        AllowEntry = allowEntry;
        AllowTP = allowTP;
        AllowPlace = allowPlace;
        AllowBreak = allowBreak;
        AllowLiquid = allowLiquid;
        AllowChest = allowChest;
        AllowPlant = allowPlant;
        AllowSpawn = allowSpawn;
        AllowGrave = allowGrave;
        AllowSwitch = allowSwitch;
        AllowDoor = allowDoor;
        AllowFragile = allowFragile;
    }
}
