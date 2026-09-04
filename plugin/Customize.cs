using Microsoft.Xna.Framework;

namespace HouseRegion;

public class LPlayer
{
    public int Who { get; set; }
    public int TileX { get; set; }
    public int TileY { get; set; }
    public bool Look { get; set; }

    /// <summary>
    /// 当前所在房屋名（null = 不在任何房屋内）。
    /// 作为「进入/离开」状态机依据而非用坐标反查，
    /// 保证玩家出生在房内 / 被传送进房 / 跨屋传送时也能触发进入判定。
    /// 用名字而非 House 引用：热重载会重建 Houses 列表，引用会失效。
    /// </summary>
    public string? CurrentHouseName { get; set; }

    /// <summary>
    /// 爆炸弹幕 fuse 标记：玩家创建爆炸弹幕时记为 Main.GameUpdateCount + 10（10 tick 窗口，
    /// 参考 TShock Bouncer 的 RecentFuse 机制）。窗口内到达的 TileEdit/LiquidSet 包视为
    /// 「爆炸引起」（客户端本地模拟爆炸后补发包），用于区分手动挖 vs 爆炸破坏。
    /// </summary>
    public long ExplosionFuseTick { get; set; }

    public LPlayer(int who, int lasttileX, int lasttileY)
    {
        Who = who;
        TileX = lasttileX;
        TileY = lasttileY;
        Look = false;
        CurrentHouseName = null; // 初始未知 → 首帧若在房内即判定进入
        ExplosionFuseTick = 0;
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

    /// <summary>
    /// 爆炸物破坏（1=允许，0=禁止）。
    /// 叠加在基本操作之上：爆炸破坏方块放行 = AllowBreak==1 && AllowExplosion==1；
    /// 爆炸产生/移除液体放行 = AllowLiquid==1 && AllowExplosion==1。
    /// 基本操作不放行时，即使本项为 1 也不允许爆炸破坏。
    /// </summary>
    public int AllowExplosion { get; set; }
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
                 int allowPlace, int allowBreak, int allowExplosion, int allowLiquid, int allowChest,
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
        AllowExplosion = allowExplosion;
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
