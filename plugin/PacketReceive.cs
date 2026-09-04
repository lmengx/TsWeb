using Microsoft.Xna.Framework;
using System.Collections;
using System.IO.Streams;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Tile_Entities;
using Terraria.ID;
using Terraria.Localization;
using TShockAPI;

namespace HouseRegion;

public delegate bool GetDataHandlerDelegate(GetDataHandlerArgs args);

public class GetDataHandlerArgs : EventArgs
{
    public TSPlayer Player { get; private set; }
    public MemoryStream Data { get; private set; }
    public Player TPlayer => this.Player.TPlayer;
    public GetDataHandlerArgs(TSPlayer player, MemoryStream data)
    {
        this.Player = player;
        this.Data = data;
    }
}

public static class GetDataHandlers
{
    internal static readonly string EditHouse = "house.edit";
    internal static readonly string AdminHouse = "house.admin";
    private static Dictionary<PacketTypes, GetDataHandlerDelegate> GetDataHandlerDelegates = null!;
    internal static readonly Dictionary<int, List<Rectangle>> PlayerActiveHouses = new();
    /// <summary>玩家 → 显示代次：每次 Show 递增，作废旧的一次性计时协程（瞬态提示）</summary>
    private static readonly Dictionary<int, int> PlayerDisplayEpoch = new();
    /// <summary>边框显示时长（秒）：显示一次后到时自动消失，不再循环刷新</summary>
    private const int DisplayDurationSeconds = 2;

    /// <summary>热重载时重置静态状态</summary>
    internal static void ResetState()
    {
        PlayerActiveHouses.Clear();
        PlayerDisplayEpoch.Clear();
    }
    private static readonly HashSet<int> PlantTiles = new()
    {
        TileID.Plants, TileID.Plants2,
        TileID.DyePlants,
        TileID.HallowedPlants, TileID.HallowedPlants2,
        TileID.JunglePlants, TileID.JunglePlants2,
        TileID.MushroomPlants,
        TileID.CorruptPlants,
        TileID.CrimsonPlants,
        TileID.ImmatureHerbs, TileID.MatureHerbs, TileID.BloomingHerbs,
    };
    private static readonly HashSet<int> FragileTiles = new()
    {
        TileID.Cobweb,
        TileID.Grass,
        TileID.HallowedGrass,
        TileID.JungleGrass,
        TileID.MushroomGrass,
        TileID.CorruptGrass,
        TileID.CrimsonGrass,
    };

    // 徒手可破坏的方块（无需工具，参考 TShock GetDataHandlers.breakableTiles）
    private static readonly HashSet<int> BreakableTiles = new()
    {
        TileID.Books,
        TileID.Bottles,
        TileID.BreakableIce,
        TileID.Candles,
        TileID.CorruptGrass,
        TileID.Dirt,
        TileID.CrimsonGrass,
        TileID.Grass,
        TileID.HallowedGrass,
        TileID.MagicalIceBlock,
        TileID.Mannequin,
        TileID.Torches,
        TileID.WaterCandle,
        TileID.Womannequin,
    };

    /// <summary>
    /// Bouncer 同款工具校验：判断玩家手持物品是否足以破坏该方块。
    /// 斧头类方块需斧头；锤子类方块需锤子；其他实心方块需镐（或掘墓铲/钻头坐骑/挖掘鼹鼠矿车）。
    /// </summary>
    private static bool HasProperTool(TSPlayer player, int tileType, Item selectedItem)
    {
        if (selectedItem == null) return false;
        bool isDrill = player.TPlayer.mount.Type == MountID.Drill;
        bool isMole = player.TPlayer.mount.Type == MountID.DiggingMoleMinecart;

        // 斧头类方块 → 需要斧头（或钻头坐骑）
        if (Main.tileAxe[tileType])
            return selectedItem.axe > 0 || isDrill;

        // 锤子类方块 → 需要锤子（或钻头坐骑）
        if (Main.tileHammer[tileType])
            return selectedItem.hammer > 0 || isDrill;

        // 豁免：物品展示框 / 骷髅罐 / 蛇绳 / 放置时可打破的方块
        if (tileType == TileID.ItemFrame ||
            tileType == TileID.DeadCellsDisplayJar ||
            tileType == TileID.MysticSnakeRope ||
            TileID.Sets.BreakableWhenPlacing[tileType])
            return true;

        // 普通实心方块 → 需要镐（或掘墓铲/钻头坐骑/挖掘鼹鼠矿车）
        return selectedItem.pick > 0 ||
               selectedItem.type == ItemID.GravediggerShovel ||
               isDrill || isMole;
    }

    public static void InitGetDataHandler()
    {
        GetDataHandlerDelegates = new Dictionary<PacketTypes, GetDataHandlerDelegate>
        {
            {PacketTypes.Tile, HandleTile},
            {PacketTypes.DoorUse, HandleDoorUse},
            {PacketTypes.PlayerSlot, HandlePlayerSlot},
            {PacketTypes.ChestGetContents, HandleChestOpen},
            {PacketTypes.ChestItem, HandleChestItem},
            {PacketTypes.ChestOpen, HandleChestActive},
            {PacketTypes.PlaceChest, HandlePlaceChest},
            {PacketTypes.SignNew, HandleSign},
            {PacketTypes.LiquidSet, HandleLiquidSet},
            {PacketTypes.PaintTile, HandlePaintTile},
            {PacketTypes.PaintWall, HandlePaintWall},
            {PacketTypes.PlaceObject, HandlePlaceObject},
            {PacketTypes.PlaceTileEntity, HandlePlaceTileEntity},
            {PacketTypes.PlaceItemFrame, HandlePlaceItemFrame},
            {PacketTypes.WeaponsRackTryPlacing, HandleWeaponsRackTryPlacing},
            {PacketTypes.FoodPlatterTryPlacing, HandleFoodPlatterTryPlacing},
            {PacketTypes.RequestTileEntityInteraction, HandleRequestTileEntityInteraction},
            {PacketTypes.TileEntityHatRackItemSync, HandleTileEntityHatRackItemSync},
            {PacketTypes.GemLockToggle, HandleGemLockToggle},
            {PacketTypes.MassWireOperation, HandleMassWireOperation},
            {PacketTypes.PlayerSpawn, HandlePlayerSpawn},
        };
    }

    public static bool HandlerGetData(PacketTypes type, TSPlayer player, MemoryStream data)
    {
        if (GetDataHandlerDelegates.TryGetValue(type, out var handler))
            return handler(new GetDataHandlerArgs(player, data));
        return false;
    }

    // ══════════════════════════════════════════════════════════
    //  违规统一处理入口
    // ══════════════════════════════════════════════════════════

    private static bool Deny(GetDataHandlerArgs args, House house, string msg)
    {
        args.Player.SendErrorMessage(msg);

        if (house.NotifyBreakPlace == 1)
            NotifyOwner(house, args.Player.Name + " " + msg);

        if (house.ExpelOnViolate == 1)
            ExpelPlayer(args.Player, house);

        return true; // 拦截数据包
    }

    private static void NotifyOwner(House house, string msg)
    {
        try
        {
            var ownerId = Convert.ToInt32(house.Author);
            var owner = TShock.UserAccounts.GetUserAccountByID(ownerId);
            if (owner == null) return;
            for (int i = 0; i < TShock.Players.Length; i++)
            {
                var p = TShock.Players[i];
                if (p != null && p.Account != null && p.Account.ID == owner.ID)
                {
                    p.SendMessage($"[{house.Name}] {msg}", Color.Orange);
                    return;
                }
            }
        }
        catch { /* 屋主离线/无效则忽略 */ }
    }

    internal static void ExpelPlayer(TSPlayer player, House house)
    {
        int tx, ty;
        if (house.ExpelX.HasValue && house.ExpelY.HasValue)
        {
            tx = house.ExpelX.Value;
            ty = house.ExpelY.Value;
        }
        else
        {
            // 后备：房屋水平中心 ±100 格
            tx = house.HouseArea.X + house.HouseArea.Width / 2 + 100;
            if (house.HouseArea.Contains(tx, house.HouseArea.Y))
                tx = house.HouseArea.X + house.HouseArea.Width / 2 - 100;
            ty = house.HouseArea.Y;
        }
        player.Teleport(tx * 16, ty * 16);
    }

    /// <summary>
    /// 判断玩家是否对房屋有全权限
    /// </summary>
    private static bool IsHouseAuthorized(TSPlayer player, House house)
    {
        if (player == null || !player.IsLoggedIn || player.Account == null) return false;
        var id = player.Account.ID.ToString();
        return player.Group.HasPermission(EditHouse) ||
               id == house.Author ||
               Utils.OwnsHouse(id, house) ||
               Utils.CanUseHouse(id, house);
    }

    // ══════════════════════════════════════════════════════════
    //  数据包处理器
    // ══════════════════════════════════════════════════════════

    private static bool HandleTile(GetDataHandlerArgs args)
    {
        int action = args.Data.ReadInt8();
        int x = args.Data.ReadInt16();
        int y = args.Data.ReadInt16();

        // 安全检查：LPlayers 可能为 null
        var lplayer = HouseCore.LPlayers[args.Player.Index];
        if (lplayer != null && lplayer.Look)
        {
            var h = Utils.InAreaHouse(x, y);
            if (h == null)
                args.Player.SendMessage("敲击处不属于任何房子。", Color.Yellow);
            else
            {
                var AuthorNames = "";
                try { AuthorNames = TShock.UserAccounts.GetUserAccountByID(Convert.ToInt32(h.Author)).Name; }
                catch (Exception ex) { TShock.Log.Error("房屋插件错误:" + ex); }
                args.Player.SendMessage($"敲击处为 {AuthorNames} 的房子: {h.Name}", Color.Yellow);
            }
            args.Player.SendTileSquareCentered(x, y);
            lplayer.Look = false;
            return true;
        }

        if (args.Player.AwaitingTempPoint > 0)
        {
            args.Player.TempPoints[args.Player.AwaitingTempPoint - 1].X = x;
            args.Player.TempPoints[args.Player.AwaitingTempPoint - 1].Y = y;
            args.Player.SendMessage($"点{args.Player.AwaitingTempPoint} 已设置 ({x}, {y})", Color.Yellow);

            // 两个点都设了 → 显示边框预览
            if (args.Player.TempPoints[0] != Point.Zero && args.Player.TempPoints[1] != Point.Zero)
            {
                var p1 = args.Player.TempPoints[0];
                var p2 = args.Player.TempPoints[1];
                var previewRect = new Rectangle(
                    Math.Min(p1.X, p2.X), Math.Min(p1.Y, p2.Y),
                    Math.Abs(p2.X - p1.X), Math.Abs(p2.Y - p1.Y));
                ShowRegion(args.Player, previewRect);
                // 圈地预览：2 秒后自动消失
                Main.DelayedProcesses.Add(ClearPreviewEnumerator(args.Player.Index, previewRect));
            }

            args.Player.SendTileSquareCentered(x, y);
            args.Player.AwaitingTempPoint = 0;
            return true;
        }

        var house = Utils.InAreaHouse(x, y);
        if (house == null) return false;

        // 授权玩家放行
        if (IsHouseAuthorized(args.Player, house)) return false;

        // 读取目标方块类型以分流权限
        var tile = Main.tile[x, y];
        var tileType = tile != null ? tile.type : 0;

        // action: 0=破坏, 1-4=放置
        if (action == 0)
        {
            var selectedItem = args.Player.SelectedItem;

            // ═══ 爆炸破坏判定（可靠：手持爆炸物 → 爆炸破坏）═══
            // 玩家手持炸弹/雷管/火箭筒等爆炸物（物品 ID 在爆炸物集合，或物品 shoot 是爆炸弹幕类型）
            // → 该破坏是爆炸引起，放行需 AllowBreak==1 && AllowExplosion==1。
            // 这是比弹幕位置更可靠的判定：不依赖 fuse 时间窗/弹幕存活，直接看玩家手持物品。
            // 补充：投掷爆炸弹幕后极短窗口（ExplosionFuseTick）与目标附近弹幕记录，兜底持物已切换的场景。
            bool isExplosion = HouseCore.IsExplosiveItem(selectedItem) ||
                               (lplayer != null && (Main.GameUpdateCount < lplayer.ExplosionFuseTick ||
                                                     HouseCore.IsRecentExplosionNear(args.Player, x, y)));
            if (isExplosion)
            {
                if (house.AllowBreak == 1 && house.AllowExplosion == 1)
                    return false;
                args.Player.SendTileSquareCentered(x, y);
                return Deny(args, house, "无权用爆炸物破坏被房子保护的地区。");
            }

            // ═══ 工具校验（Bouncer 同款：不挥动正确工具就无法破坏）═══
            // 植物/墓碑/易碎品是徒手可采集的，不在此校验范围内（各自有 AllowPlant/AllowGrave/AllowFragile）。
            if (!PlantTiles.Contains(tileType) && tileType != TileID.Tombstones && !FragileTiles.Contains(tileType))
            {
				bool hasProperTool = HasProperTool(args.Player, tileType, selectedItem);
                if (!hasProperTool)
                {
                    args.Player.SendTileSquareCentered(x, y);
                    return Deny(args, house, "没有正确的工具无法破坏被房子保护的地区。");
                }
            }

            // 植物
            if (PlantTiles.Contains(tileType))
            {
                if (house.AllowPlant == 1)
                    return false;
                args.Player.SendTileSquareCentered(x, y);
                return Deny(args, house, "无权采集被房子保护的植物。");
            }

            // 墓碑
            if (tileType == TileID.Tombstones)
            {
                if (house.AllowGrave == 1)
                    return false;
                args.Player.SendTileSquareCentered(x, y);
                return Deny(args, house, "无权挖掘被房子保护的墓碑。");
            }

            // 易碎品（蜘蛛网、草类）
            if (FragileTiles.Contains(tileType))
            {
                if (house.AllowFragile == 1)
                    return false;
                args.Player.SendTileSquareCentered(x, y);
                return Deny(args, house, "无权破坏被房子保护的物品。");
            }

            // 普通破坏
            if (house.AllowBreak == 1)
                return false;
            args.Player.SendTileSquareCentered(x, y);
            return Deny(args, house, "你没有权力损坏被房子保护的地区。");
        }

        // 放置
        if (house.AllowPlace == 1)
            return false;
        args.Player.SendTileSquareCentered(x, y);
        return Deny(args, house, "你没有权力修改被房子保护的地区。");
    }

    private static bool HandleDoorUse(GetDataHandlerArgs args)
    {
        args.Data.ReadInt8();
        int x = args.Data.ReadInt16();
        int y = args.Data.ReadInt16();
        var house = Utils.InAreaHouse(x, y);
        if (house == null) return false;
        if (IsHouseAuthorized(args.Player, house)) return false;
        if (house.AllowDoor == 1) return false;
        return Deny(args, house, "无权修改被房子保护的地区的门。");
    }

    private static bool HandleChestOpen(GetDataHandlerArgs args)
    {
        int x = args.Data.ReadInt16();
        int y = args.Data.ReadInt16();
        var house = Utils.InAreaHouse(x, y);
        if (house == null) return false;
        if (IsHouseAuthorized(args.Player, house)) return false;
        if (house.AllowChest == 1) return false;
        return Deny(args, house, "无权打开被房子保护的地区的箱子。");
    }

    private static bool HandleChestItem(GetDataHandlerArgs args)
    {
        var id = args.Data.ReadInt16();
        var x = Main.chest[id].x;
        var y = Main.chest[id].y;
        var house = Utils.InAreaHouse(x, y);
        if (house == null) return false;
        if (IsHouseAuthorized(args.Player, house)) return false;
        if (house.AllowChest == 1) return false;
        return Deny(args, house, "无权修改被房子保护的地区的箱子物品。");
    }

    private static bool HandleChestActive(GetDataHandlerArgs args)
    {
        int x = args.Data.ReadInt16();
        int y = args.Data.ReadInt16();
        var house = Utils.InAreaHouse(x, y);
        if (house == null) return false;
        if (IsHouseAuthorized(args.Player, house)) return false;
        if (house.AllowChest == 1) return false;
        return Deny(args, house, "无权修改被房子保护的地区的箱子。");
    }

    private static bool HandlePlaceChest(GetDataHandlerArgs args)
    {
        args.Data.ReadByte();
        args.Data.ReadInt16();
        int x = args.Data.ReadInt16();
        int y = args.Data.ReadInt16();
        var house = Utils.InAreaHouse(x, y);
        if (house == null) return false;
        if (IsHouseAuthorized(args.Player, house)) return false;
        if (house.AllowChest == 1) return false;
        return Deny(args, house, "无权在被房子保护的地区放置箱子。");
    }

    private static bool HandleSign(GetDataHandlerArgs args)
    {
        var id = args.Data.ReadInt16();
        var x = args.Data.ReadInt16();
        var y = args.Data.ReadInt16();
        var house = Utils.InAreaHouse(x, y);
        if (house == null) return false;
        if (IsHouseAuthorized(args.Player, house)) return false;
        if (house.AllowPlace == 1) return false;
        return Deny(args, house, "无权修改被房子保护的地区的标牌。");
    }

    private static bool HandleLiquidSet(GetDataHandlerArgs args)
    {
        var x = args.Data.ReadInt16();
        var y = args.Data.ReadInt16();
        var house = Utils.InAreaHouse(x, y);
        if (house == null) return false;
        if (IsHouseAuthorized(args.Player, house)) return false;

        // 液体炸弹/液体火箭爆炸产生的液体（客户端本地模拟后补发 LiquidSet 包）：
        // 判定 = 目标坐标附近存在产生/移除液体的爆炸弹幕，参考 TShock Bouncer OnLiquidSet 的
        // wasThereABombNearby 机制（RecentlyCreatedProjectiles × projectileCreatesLiquid × 距离<5）。
        // 补充：统一爆炸位置记录（HouseCore.IsRecentExplosionNear）同样覆盖液体爆炸弹幕。
        // 放行 = AllowLiquid==1 && AllowExplosion==1（AllowExplosion 叠加在基本液体操作之上）。
        if (IsExplosionLiquidNearby(args.Player, x, y) || HouseCore.IsRecentExplosionNear(args.Player, x, y))
        {
            if (house.AllowLiquid == 1 && house.AllowExplosion == 1)
                return false;
            args.Player.SendTileSquareCentered(x, y);
            return Deny(args, house, "无权用爆炸物修改被房子保护的地区的液体。");
        }

        if (house.AllowLiquid == 1) return false;
        args.Player.SendTileSquareCentered(x, y);
        return Deny(args, house, "无权修改被房子保护的地区的液体。");
    }

    private static bool HandlePaintTile(GetDataHandlerArgs args)
    {
        var x = args.Data.ReadInt16();
        var y = args.Data.ReadInt16();
        var house = Utils.InAreaHouse(x, y);
        if (house == null) return false;
        if (IsHouseAuthorized(args.Player, house)) return false;
        if (house.AllowPlace == 1) return false;
        args.Player.SendTileSquareCentered(x, y);
        return Deny(args, house, "无权油漆被房子保护的地区的瓷砖。");
    }

    private static bool HandlePaintWall(GetDataHandlerArgs args)
    {
        var x = args.Data.ReadInt16();
        var y = args.Data.ReadInt16();
        var house = Utils.InAreaHouse(x, y);
        if (house == null) return false;
        if (IsHouseAuthorized(args.Player, house)) return false;
        if (house.AllowPlace == 1) return false;
        args.Player.SendTileSquareCentered(x, y);
        return Deny(args, house, "无权油漆被房子保护的地区的墙。");
    }

    private static bool HandlePlaceObject(GetDataHandlerArgs args)
    {
        int x = args.Data.ReadInt16();
        int y = args.Data.ReadInt16();
        var house = Utils.InAreaHouse(x, y);
        if (house == null) return false;
        if (IsHouseAuthorized(args.Player, house)) return false;
        if (house.AllowPlace == 1) return false;
        args.Player.SendTileSquareCentered(x, y);
        return Deny(args, house, "无权修改被房子保护的地区。");
    }

    private static bool HandlePlaceTileEntity(GetDataHandlerArgs args)
    {
        var x = args.Data.ReadInt16();
        var y = args.Data.ReadInt16();
        var house = Utils.InAreaHouse(x, y);
        if (house == null) return false;
        if (IsHouseAuthorized(args.Player, house)) return false;
        if (house.AllowPlace == 1) return false;
        args.Player.SendTileSquareCentered(x, y);
        return Deny(args, house, "无权修改被房子保护的地区。");
    }

    private static bool HandlePlaceItemFrame(GetDataHandlerArgs args)
    {
        var x = args.Data.ReadInt16();
        var y = args.Data.ReadInt16();
        var house = Utils.InAreaHouse(x, y);
        if (house == null) return false;
        if (IsHouseAuthorized(args.Player, house)) return false;
        if (house.AllowPlace == 1) return false;
        return Deny(args, house, "无权修改被房子保护的地区的物品框。");
    }

    private static bool HandleWeaponsRackTryPlacing(GetDataHandlerArgs args)
    {
        var x = args.Data.ReadInt16();
        var y = args.Data.ReadInt16();
        var house = Utils.InAreaHouse(x, y);
        if (house == null) return false;
        if (IsHouseAuthorized(args.Player, house)) return false;
        if (house.AllowPlace == 1) return false;
        return Deny(args, house, "无权修改被房子保护的地区的武器架。");
    }

    private static bool HandleFoodPlatterTryPlacing(GetDataHandlerArgs args)
    {
        var x = args.Data.ReadInt16();
        var y = args.Data.ReadInt16();
        var house = Utils.InAreaHouse(x, y);
        if (house == null) return false;
        if (IsHouseAuthorized(args.Player, house)) return false;
        if (house.AllowPlace == 1) return false;
        return Deny(args, house, "无权修改被房子保护的地区的盘子。");
    }

    private static bool HandleRequestTileEntityInteraction(GetDataHandlerArgs args)
    {
        var id = args.Data.ReadInt32();
        if (!TileEntity.ByID.TryGetValue(id, out var te) || te == null)
            return false;
        int x = te.Position.X, y = te.Position.Y;
        var house = Utils.InAreaHouse(x, y);
        if (house == null) return false;
        if (IsHouseAuthorized(args.Player, house)) return false;

        // TEBed.type == 0 → 床（设置复活点走客户端本地，但拦截交互可阻断成功感）
        if (te.type == 0)
        {
            if (house.AllowSpawn == 1) return false;
            args.Player.SendErrorMessage("无权在被房子保护的地区设置复活点。");
            // 不 web，因为床交互是客户端本地行为，web 也没用
            return true;
        }

        if (house.AllowSwitch == 1) return false;
        return Deny(args, house, "无权触发被房子保护的地区的物品。");
    }

    private static bool HandleTileEntityHatRackItemSync(GetDataHandlerArgs args)
    {
        var id = args.Data.ReadInt32();
        if (!TileEntity.ByID.TryGetValue(id, out var te) || te == null)
            return false;
        var house = Utils.InAreaHouse(te.Position.X, te.Position.Y);
        if (house == null) return false;
        if (IsHouseAuthorized(args.Player, house)) return false;
        if (house.AllowPlace == 1) return false;
        if (args.Player.SelectedItem.type > 0)
        {
            args.Player.SetData("PlaceSlot", (true, args.Player.TPlayer.selectedItem));
            NetMessage.SendData(86, -1, -1, NetworkText.Empty, te.ID);
        }
        return Deny(args, house, "无权修改被房子保护的地区的帽架。");
    }

    private static bool HandleGemLockToggle(GetDataHandlerArgs args)
    {
        var x = (int)args.Data.ReadInt16();
        var y = (int)args.Data.ReadInt16();
        var house = Utils.InAreaHouse(x, y);
        if (house == null) return false;
        if (IsHouseAuthorized(args.Player, house)) return false;
        if (house.AllowSwitch == 1) return false;
        return Deny(args, house, "无权触发被房子保护的宝石锁。");
    }

    private static bool HandleMassWireOperation(GetDataHandlerArgs args)
    {
        int x1 = args.Data.ReadInt16();
        int y1 = args.Data.ReadInt16();
        int x2 = args.Data.ReadInt16();
        int y2 = args.Data.ReadInt16();
        var A = new Rectangle(Math.Min(x1, x2), args.TPlayer.direction != 1 ? y1 : y2, Math.Abs(x2 - x1) + 1, 1);
        var B = new Rectangle(args.TPlayer.direction != 1 ? x2 : x1, Math.Min(y1, y2), 1, Math.Abs(y2 - y1) + 1);
        for (var i = 0; i < HouseCore.Houses.Count; i++)
        {
            var house = HouseCore.Houses[i];
            if (house == null) continue;
            if (house.HouseArea.Intersects(A) || house.HouseArea.Intersects(B))
            {
                if (!IsHouseAuthorized(args.Player, house))
                    return Deny(args, house, "无权在房子保护地区进行大规模布线。");
            }
        }
        return false;
    }

    private static bool HandlePlayerSlot(GetDataHandlerArgs args)
    {
        var slot = (int)args.Data.ReadByte();
        var x = (int)args.Data.ReadInt16();
        var y = (int)args.Data.ReadInt16();
        var house = Utils.InAreaHouse(x, y);
        if (house == null) return false;
        if (IsHouseAuthorized(args.Player, house)) return false;
        if (house.AllowPlace == 1) return false;
        return Deny(args, house, "无权修改被房子保护的地区的物品。");
    }

    /// <summary>会产生/移除液体的爆炸弹幕（与 TShock GetDataHandlers.projectileCreatesLiquid 一致）</summary>
    private static readonly HashSet<int> ExplosionLiquidProjectiles = new()
    {
        ProjectileID.LavaBomb, ProjectileID.LavaRocket, ProjectileID.LavaGrenade, ProjectileID.LavaMine,
        ProjectileID.WetBomb, ProjectileID.WetRocket, ProjectileID.WetGrenade, ProjectileID.WetMine,
        ProjectileID.HoneyBomb, ProjectileID.HoneyRocket, ProjectileID.HoneyGrenade, ProjectileID.HoneyMine,
        ProjectileID.DryBomb, ProjectileID.DryRocket, ProjectileID.DryGrenade, ProjectileID.DryMine,
    };

    /// <summary>
    /// 判定目标坐标附近是否存在「产生/移除液体」的爆炸弹幕。
    /// 参考 TShock Bouncer OnLiquidSet 的 wasThereABombNearby：
    /// 遍历 TSPlayer.RecentlyCreatedProjectiles，类型 ∈ projectileCreatesLiquid 且距离 < BombExplosionRadius(=5)。
    /// </summary>
    private static bool IsExplosionLiquidNearby(TSPlayer player, int tileX, int tileY)
    {
        const int radius = 5; // TShock.Config.Settings.BombExplosionRadius 默认值
        lock (player.RecentlyCreatedProjectiles)
        {
            foreach (var p in player.RecentlyCreatedProjectiles)
            {
                if (!ExplosionLiquidProjectiles.Contains(p.Type)) continue;
                if (p.Index < 0 || p.Index >= Main.projectile.Length) continue;
                var proj = Main.projectile[p.Index];
                if (proj == null || !proj.active) continue;
                var px = (int)(proj.position.X / 16f);
                var py = (int)(proj.position.Y / 16f);
                if (Math.Abs(tileX - px) < radius && Math.Abs(tileY - py) < radius)
                    return true;
            }
        }
        return false;
    }

    // ══════════════════════════════════════════════════════════
    //  边框显示
    // ══════════════════════════════════════════════════════════

    public static void ShowHouseDisplay(TSPlayer player, House house)
    {
        if (!PlayerActiveHouses.TryGetValue(player.Index, out var list))
        {
            list = new List<Rectangle>();
            PlayerActiveHouses[player.Index] = list;
        }
        if (!list.Contains(house.HouseArea))
        {
            list.Add(house.HouseArea);
            ShowRegion(player, house.HouseArea);
            // 瞬态提示：显示 2 秒后自动消失。代次 +1 作废旧计时器，新计时从此刻起算
            var epoch = PlayerDisplayEpoch.TryGetValue(player.Index, out var e) ? e + 1 : 1;
            PlayerDisplayEpoch[player.Index] = epoch;
            Main.DelayedProcesses.Add(GetDisplayEnumerator(player.Index, epoch));
        }
    }

    public static void HideHouseDisplay(TSPlayer player, House house)
    {
        if (PlayerActiveHouses.TryGetValue(player.Index, out var list))
        {
            if (list.Remove(house.HouseArea))
            {
                // 立即精确清除该玩家该区域的边框弹幕（多房屋/离开重进均不残留）
                ClearBorderProjectiles(player, house.HouseArea);
                // 作废显示计时器；若玩家随后重进，将以新代次重新计时
                PlayerDisplayEpoch.Remove(player.Index);
            }
        }
    }

    public static void ToggleHouseDisplay(TSPlayer player, House house)
    {
        if (PlayerActiveHouses.TryGetValue(player.Index, out var list) && list.Contains(house.HouseArea))
        {
            HideHouseDisplay(player, house);
            player.SendSuccessMessage("已隐藏房屋 " + house.Name + " 的边界。");
        }
        else
        {
            ShowHouseDisplay(player, house);
            player.SendSuccessMessage("已显示房屋 " + house.Name + " 的边界。");
        }
    }

    /// <summary>一次性显示计时：等待 2 秒后清除该玩家全部边框弹幕（瞬态提示，不再循环重画）</summary>
    private static IEnumerator GetDisplayEnumerator(int playerIndex, int epoch)
    {
        try
        {
            var player = TShock.Players[playerIndex];
            if (player is not { ConnectionAlive: true }) yield break;
            for (var i = 0; i < 60 * DisplayDurationSeconds; i++)
            {
                yield return null;
                player = TShock.Players[playerIndex];
                if (player == null || !player.ConnectionAlive) yield break;
                // 玩家已离开/重进（代次变更）→ 本计时作废，由新代次接管
                if (!PlayerDisplayEpoch.TryGetValue(playerIndex, out var cur) || cur != epoch) yield break;
            }
            // 2 秒到：清除该玩家全部边框弹幕（同一玩家同一时刻最多一个活动区域）
            ClearPlayerBorderProjectiles(player);
            PlayerActiveHouses.Remove(playerIndex);
        }
        finally
        {
            // 仅当仍是最新代次才清理，避免误删新代次的状态
            if (PlayerDisplayEpoch.TryGetValue(playerIndex, out var cur) && cur == epoch)
                PlayerDisplayEpoch.Remove(playerIndex);
        }
    }

    /// <summary>圈地预览：2 秒后自动清除指定区域的边框弹幕</summary>
    private static IEnumerator ClearPreviewEnumerator(int playerIndex, Rectangle area)
    {
        var player = TShock.Players[playerIndex];
        if (player is not { ConnectionAlive: true }) yield break;
        for (var i = 0; i < 60 * DisplayDurationSeconds; i++)
        {
            yield return null;
            player = TShock.Players[playerIndex];
            if (player == null || !player.ConnectionAlive) yield break;
        }
        ClearBorderProjectiles(player, area);
    }

    // ── 边框显示方法 ──

    private static void ShowRegion(TSPlayer ts, Rectangle rect)
    {
        var maxSide = Math.Max(rect.Width, rect.Height);
        var step = maxSide <= 30 ? 1 : Math.Clamp(maxSide / 30, 1, 10);
        int projType = ProjectileID.TopazBolt;
        for (var x = rect.Left; x <= rect.Right; x += step)
        {
            CreateProjectile(ts, x, rect.Top, projType, rect);
            CreateProjectile(ts, x, rect.Bottom, projType, rect);
        }
        for (var y = rect.Top + step; y <= rect.Bottom - step; y += step)
        {
            CreateProjectile(ts, rect.Left, y, projType, rect);
            CreateProjectile(ts, rect.Right, y, projType, rect);
        }
    }

    private static void CreateProjectile(TSPlayer ts, int tileX, int tileY, int projType, Rectangle area)
    {
        var pos = new Vector2((tileX * 16) + 8, (tileY * 16) + 8);
        // 服务器端创建无主边框弹幕：owner 必须 == Main.myPlayer(255)（专用服务器无本地玩家），
        // 否则 1.4.5.7 Projectile.NewProjectile 触发 Invariant（"owner (x) != myPlayer (255)"）返回哨兵 1000，
        // 1000 又经 SendData(27) 序列化未初始化槽 1000（owner=255/type=0/key=default）触发第二条
        // "SyncProjectile owner (255) must match spawner (0)" —— 后台成对刷屏的根因。
        // owner=255 时 NewProjectileSetup 默认 key.Spawner=myPlayer=255，发送断言 owner==Spawner 通过。
        int identity = Projectile.NewProjectile(null, pos.X, pos.Y, 0f, 0f, projType, 0, 0f, 255);
        if (identity > -1 && identity < Main.projectile.Length)
        {
            var proj = Main.projectile[identity];
            if (proj != null)
            {
                // 归属/区域标记：owner 已是 255（无主），ai[0]=查看玩家索引、ai[1]=区域 Left、ai[2]=区域 Top
                // （TopazBolt 静止弹幕 AI 不使用 ai 字段；世界坐标 < 2^24，float 精确表示，供清理精确匹配）
                proj.ai[0] = ts.Index;
                proj.ai[1] = area.Left;
                proj.ai[2] = area.Top;
                proj.netUpdate = true;
            }
            NetMessage.SendData((int)PacketTypes.ProjectileNew, ts.Index, -1, null, identity);
        }
    }

    /// <summary>清除某玩家所有 TopazBolt 边框弹幕（owner=255 无主 + ai[0] 归属标记匹配）</summary>
    internal static void ClearPlayerBorderProjectiles(TSPlayer player)
    {
        if (player == null || player.Index < 0) return;
        for (var i = 0; i < Main.projectile.Length; i++)
        {
            var proj = Main.projectile[i];
            if (proj is { active: true, type: ProjectileID.TopazBolt, owner: 255 } && proj.ai[0] == player.Index)
            {
                proj.Kill();
                NetMessage.SendData((int)PacketTypes.ProjectileDestroy, player.Index, -1, null, i);
            }
        }
    }

    /// <summary>精确清除某玩家指定区域的 TopazBolt 边框弹幕（ai[0]=玩家 + ai[1]/ai[2]=区域左上角 匹配）</summary>
    internal static void ClearBorderProjectiles(TSPlayer player, Rectangle area)
    {
        if (player == null || player.Index < 0) return;
        for (var i = 0; i < Main.projectile.Length; i++)
        {
            var proj = Main.projectile[i];
            if (proj is { active: true, type: ProjectileID.TopazBolt, owner: 255 }
                && proj.ai[0] == player.Index
                && proj.ai[1] == area.Left
                && proj.ai[2] == area.Top)
            {
                proj.Kill();
                NetMessage.SendData((int)PacketTypes.ProjectileDestroy, player.Index, -1, null, i);
            }
        }
    }

    internal static void ClearPlayerDisplays(int playerIndex)
    {
        // 掉线：服务器端 Kill 该玩家的边框弹幕（客户端已断开无需发包），并清理状态
        for (var i = 0; i < Main.projectile.Length; i++)
        {
            var proj = Main.projectile[i];
            if (proj is { active: true, type: ProjectileID.TopazBolt, owner: 255 } && proj.ai[0] == playerIndex)
                proj.Kill();
        }
        PlayerActiveHouses.Remove(playerIndex);
        PlayerDisplayEpoch.Remove(playerIndex);
    }

    // ══════════════════════════════════════════════════════════
    //  PlayerSpawn：拦截复活点（床设置客户端不通知服务器，只在复活包中暴露坐标）
    // ══════════════════════════════════════════════════════════

    private static bool HandlePlayerSpawn(GetDataHandlerArgs args)
    {
        // 读取客户端提供的复活坐标
        var reader = new BinaryReader(args.Data);
        byte _playerIdx = reader.ReadByte();
        short spawnX = reader.ReadInt16();
        short spawnY = reader.ReadInt16();

        var house = Utils.InAreaHouse(spawnX, spawnY);
        if (house == null) return false;
        if (IsHouseAuthorized(args.Player, house)) return false;
        if (house.AllowSpawn == 1) return false;

        // 不允许在此复活 → 延迟一帧后传送至世界出生点
        var playerIdx = args.Player.Index;
        Main.DelayedProcesses.Add(TeleportToSpawn(playerIdx));
        args.Player.SendErrorMessage("不允许在被房子保护的地区设置复活点。");
        return false; // 放行包，由延迟传送修正位置
    }

    private static IEnumerator TeleportToSpawn(int playerIdx)
    {
        yield return null; // 等一帧让 spawn 完成
        var player = TShock.Players[playerIdx];
        if (player != null && player.ConnectionAlive)
        {
            player.Teleport(Main.spawnTileX * 16 + 8, (Main.spawnTileY - 3) * 16 + 8);
        }
    
    }

    internal static void OnHouseDeleted(Rectangle area)
    {
        foreach (var kv in PlayerActiveHouses)
        {
            kv.Value.RemoveAll(r => r == area);
        }
    }

    internal static bool IsPlayerShowingHouse(int playerIndex)
    {
        return PlayerActiveHouses.TryGetValue(playerIndex, out var list) && list.Count > 0;
    }
}
