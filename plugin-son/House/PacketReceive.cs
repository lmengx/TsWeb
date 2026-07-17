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
    public GetDataHandlerArgs(TSPlayer player, MemoryStream data) { this.Player = player; this.Data = data; }
}
public static class GetDataHandlers
{
    internal static readonly string EditHouse = "house.edit";
    private static Dictionary<PacketTypes, GetDataHandlerDelegate> GetDataHandlerDelegates = null!;
    private static readonly Dictionary<int, List<Rectangle>> PlayerActiveHouses = new();
    private static readonly Dictionary<int, bool> PlayerRefreshFlags = new();
    private const int RefreshIntervalSeconds = 20;
    public static void InitGetDataHandler()
    {
        GetDataHandlerDelegates = new Dictionary<PacketTypes, GetDataHandlerDelegate>
        {   {PacketTypes.Tile, HandleTile},
		    {PacketTypes.DoorUse,HandleDoorUse},
            { PacketTypes.PlayerSlot, HandlePlayerSlot },
			{PacketTypes.ChestGetContents, HandleChestOpen },
			{PacketTypes.ChestItem, HandleChestItem },
			{PacketTypes.ChestOpen, HandleChestActive },
			{PacketTypes.PlaceChest, HandlePlaceChest },
			{PacketTypes.SignNew, HandleSign },
			{PacketTypes.LiquidSet, HandleLiquidSet},
			{PacketTypes.PaintTile, HandlePaintTile},
			{PacketTypes.PaintWall, HandlePaintWall},
			{PacketTypes.PlaceObject, HandlePlaceObject },
			{PacketTypes.PlaceTileEntity, HandlePlaceTileEntity },
			{PacketTypes.PlaceItemFrame, HandlePlaceItemFrame },
            {PacketTypes.WeaponsRackTryPlacing, HandleWeaponsRackTryPlacing },
            {PacketTypes.FoodPlatterTryPlacing, HandleFoodPlatterTryPlacing },
            {PacketTypes.RequestTileEntityInteraction, HandleRequestTileEntityInteraction },
            {PacketTypes.TileEntityHatRackItemSync, HandleTileEntityHatRackItemSync },
			{PacketTypes.GemLockToggle, HandleGemLockToggle },
			{PacketTypes.MassWireOperation, HandleMassWireOperation },
		};
    }

    private static bool HandleTileEntityHatRackItemSync(GetDataHandlerArgs args)
    {
        var id = args.Data.ReadInt32();
        var ply = args.Data.ReadByte();
        if (TileEntity.ByID.TryGetValue(id, out var tileEntity) && tileEntity is TEHatRack)
        {
            var house = Utils.InAreaHouse(tileEntity.Position.X, tileEntity.Position.Y);
            if (house == null) return false;
            if (args.Player.Group.HasPermission(EditHouse) || args.Player.Account.ID.ToString() == house.Author || Utils.OwnsHouse(args.Player.Account.ID.ToString(), house) || Utils.CanUseHouse(args.Player.Account.ID.ToString(), house))
                return false;
            if (Config.Instance.WarningSpoiler) args.Player.Disable("无权修改房子保护的物品!");
            args.Player.SendErrorMessage("你没有权力修改被房子保护的物品。");
            return true;
        }
        return false;
    }

    private static bool HandleFoodPlatterTryPlacing(GetDataHandlerArgs args)
    {
        var x = args.Data.ReadInt16();
        var y = args.Data.ReadInt16();
        var house = Utils.InAreaHouse(x, y);
        var te = (TEFoodPlatter)TileEntity.ByID[TEFoodPlatter.Find(x, y)];
        if (house == null) return false;
        if (args.Player.Group.HasPermission(EditHouse) || args.Player.Account.ID.ToString() == house.Author || Utils.OwnsHouse(args.Player.Account.ID.ToString(), house) || Utils.CanUseHouse(args.Player.Account.ID.ToString(), house))
            return false;
        if (Config.Instance.WarningSpoiler) args.Player.Disable("无权修改房子保护的物品!");
        args.Player.SendErrorMessage("你没有权力修改被房子保护的物品。");
        if (args.Player.SelectedItem.type > 0)
        {
            args.Player.SetData("PlaceSlot", (true, args.Player.TPlayer.selectedItem));
            NetMessage.SendData(86, -1, -1, NetworkText.Empty, te.ID);
        }
        return true;
    }

    private static bool HandleRequestTileEntityInteraction(GetDataHandlerArgs args)
    {
        var id = args.Data.ReadInt32();
        var ply = args.Data.ReadByte();
        if (!TileEntity.IsOccupied(id, out var _) && TileEntity.ByID.TryGetValue(id, out var tileEntity))
        {
            var house = Utils.InAreaHouse(tileEntity.Position.X, tileEntity.Position.Y);
            if (house == null) return false;
            if (args.Player.Group.HasPermission(EditHouse) || args.Player.Account.ID.ToString() == house.Author || Utils.OwnsHouse(args.Player.Account.ID.ToString(), house) || Utils.CanUseHouse(args.Player.Account.ID.ToString(), house))
                return false;
            if (Config.Instance.WarningSpoiler) args.Player.Disable("无权修改房子保护的物品!");
            args.Player.SendErrorMessage("你没有权力修改被房子保护的物品。");
            return true;
        }
        return false;
    }

    private static bool HandlePlayerSlot(GetDataHandlerArgs args)
    {
        var plr = args.Data.ReadInt8();
        var slot = args.Data.ReadInt16();
        var plyData = args.Player.GetData<(bool, int)>("PlaceSlot");
        if (plyData.Item1 && plyData.Item2 == slot)
        {
            NetMessage.SendData(5, -1, -1, null, plr, slot);
            args.Player.RemoveData("PlaceSlot");
            return true;
        }
        return false;
    }

    private static bool HandleWeaponsRackTryPlacing(GetDataHandlerArgs args)
    {
        var x = args.Data.ReadInt16();
        var y = args.Data.ReadInt16();
        var house = Utils.InAreaHouse(x, y);
        var te = (TEWeaponsRack)TileEntity.ByID[TEWeaponsRack.Find(x, y)];
        if (house == null) return false;
        if (args.Player.Group.HasPermission(EditHouse) || args.Player.Account.ID.ToString() == house.Author || Utils.OwnsHouse(args.Player.Account.ID.ToString(), house) || Utils.CanUseHouse(args.Player.Account.ID.ToString(), house))
            return false;
        if (Config.Instance.WarningSpoiler) args.Player.Disable("无权修改房子保护的物品!");
        args.Player.SendErrorMessage("你没有权力修改被房子保护的物品。");
        if (args.Player.SelectedItem.type > 0)
        {
            args.Player.SetData("PlaceSlot", (true, args.Player.TPlayer.selectedItem));
            NetMessage.SendData(86, -1, -1, NetworkText.Empty, te.ID);
        }
        return true;
    }

    public static bool HandlerGetData(PacketTypes type, TSPlayer player, MemoryStream data)
    {
        if (GetDataHandlerDelegates.TryGetValue(type, out var handler))
        {
            try { return handler(new GetDataHandlerArgs(player, data)); }
            catch (Exception ex) { TShock.Log.Error("房屋插件错误调用事件时出错:" + ex.ToString()); }
        }
        return false;
    }
    private static bool HandleTile(GetDataHandlerArgs args)
    {
        int action = args.Data.ReadInt8();
        int x = args.Data.ReadInt16();
        int y = args.Data.ReadInt16();
        var house = Utils.InAreaHouse(x, y);
        if (HousingPlugin.LPlayers[args.Player.Index]!.Look)
        {
            if (house == null)
                args.Player.SendMessage("敲击处不属于任何房子。", Color.Yellow);
            else
            {
                var AuthorNames = "";
                try { AuthorNames = TShock.UserAccounts.GetUserAccountByID(Convert.ToInt32(house.Author)).Name; }
                catch (Exception ex) { TShock.Log.Error("房屋插件错误超标错误:" + ex.ToString()); }
                args.Player.SendMessage("敲击处为 " + AuthorNames + " 的房子: " + house.Name + " 状态: " + (!house.Locked || Config.Instance.LimitLockHouse ? "未上锁" : "已上锁"), Color.Yellow);
            }
            args.Player.SendTileSquareCentered(x, y);
            HousingPlugin.LPlayers[args.Player.Index]!.Look = false;
            return true;
        }
        if (args.Player.AwaitingTempPoint > 0)
        {
            args.Player.TempPoints[args.Player.AwaitingTempPoint - 1].X = x;
            args.Player.TempPoints[args.Player.AwaitingTempPoint - 1].Y = y;
            if (args.Player.AwaitingTempPoint == 1) args.Player.SendMessage("保护区左上角已设置!", Color.Yellow);
            if (args.Player.AwaitingTempPoint == 2) args.Player.SendMessage("保护区右下角已设置!", Color.Yellow);
            args.Player.SendTileSquareCentered(x, y);
            args.Player.AwaitingTempPoint = 0;
            return true;
        }
        if (house == null) return false;
        if (args.Player.Group.HasPermission(EditHouse) || args.Player.Account.ID.ToString() == house.Author || Utils.OwnsHouse(args.Player.Account.ID.ToString(), house))
            return false;
        if (Config.Instance.WarningSpoiler) args.Player.Disable("无权修改房子保护!");
        args.Player.SendErrorMessage("你没有权力损坏被房子保护的地区。");
        args.Player.SendTileSquareCentered(x, y);
        return true;
    }
    private static bool HandleDoorUse(GetDataHandlerArgs args)
    {
        args.Data.ReadInt8();
        int x = args.Data.ReadInt16();
        int y = args.Data.ReadInt16();
        var house = Utils.InAreaHouse(x, y);
        if (house == null) return false;

        // 允许门自由通行 → 任何人都可以开关门
        if (Config.Instance.AllowDoorPassage) return false;

        if (!house.Locked || Config.Instance.LimitLockHouse) return false;
        if (args.Player.Group.HasPermission(EditHouse) || args.Player.Account.ID.ToString() == house.Author || Utils.OwnsHouse(args.Player.Account.ID.ToString(), house) || Utils.CanUseHouse(args.Player.Account.ID.ToString(), house))
            return false;
        if (Config.Instance.WarningSpoiler) args.Player.Disable("无权修改门!");
        args.Player.SendErrorMessage("你没有权力修改被房子保护的地区的门。");
        args.Player.SendTileSquareCentered(x, y);
        return true;
    }
    private static bool HandleChestOpen(GetDataHandlerArgs args)
    {
        int x = args.Data.ReadInt16();
        int y = args.Data.ReadInt16();
        var house = Utils.InAreaHouse(x, y);
        if (house == null) return false;
        if ((!house.Locked || Config.Instance.LimitLockHouse) && !Config.Instance.ProtectiveChest) return false;
        if (args.Player.Group.HasPermission(EditHouse) || args.Player.Account.ID.ToString() == house.Author || Utils.OwnsHouse(args.Player.Account.ID.ToString(), house) || Utils.CanUseHouse(args.Player.Account.ID.ToString(), house))
            return false;
        if (Config.Instance.WarningSpoiler) args.Player.Disable("无权打开箱子!");
        args.Player.SendErrorMessage("你没有权力打开被房子保护的地区的箱子。");
        return true;
    }
    private static bool HandleChestItem(GetDataHandlerArgs args)
    {
        var id = args.Data.ReadInt16();
        var x = Main.chest[id].x;
        var y = Main.chest[id].y;
        var house = Utils.InAreaHouse(x, y);
        if (house == null) return false;
        if ((!house.Locked || Config.Instance.LimitLockHouse) && !Config.Instance.ProtectiveChest) return false;
        if (args.Player.Group.HasPermission(EditHouse) || args.Player.Account.ID.ToString() == house.Author || Utils.OwnsHouse(args.Player.Account.ID.ToString(), house) || Utils.CanUseHouse(args.Player.Account.ID.ToString(), house))
            return false;
        if (Config.Instance.WarningSpoiler) args.Player.Disable("无权更新箱子!");
        args.Player.SendErrorMessage("你没有权力更新被房子保护的地区的箱子。");
        return true;
    }
    private static bool HandleChestActive(GetDataHandlerArgs args)
    {
        args.Data.ReadInt16();
        int x = args.Data.ReadInt16();
        int y = args.Data.ReadInt16();
        var house = Utils.InAreaHouse(x, y);
        if (house == null) return false;
        if ((!house.Locked || Config.Instance.LimitLockHouse) && !Config.Instance.ProtectiveChest) return false;
        if (args.Player.Group.HasPermission(EditHouse) || args.Player.Account.ID.ToString() == house.Author || Utils.OwnsHouse(args.Player.Account.ID.ToString(), house) || Utils.CanUseHouse(args.Player.Account.ID.ToString(), house))
            return false;
        if (Config.Instance.WarningSpoiler) args.Player.Disable("无权修改箱子!");
        args.Player.SendErrorMessage("你没有权力修改被房子保护的地区的箱子。");
        args.Player.SendData(PacketTypes.ChestOpen, "", -1);
        return true;
    }
    private static bool HandlePlaceChest(GetDataHandlerArgs args)
    {
        args.Data.ReadByte();
        int tileX = args.Data.ReadInt16();
        int tileY = args.Data.ReadInt16();
        var rect = new Rectangle(tileX, tileY, 3, 3);
        for (var i = 0; i < HousingPlugin.Houses.Count; i++)
        {
            var house = HousingPlugin.Houses[i];
            if (house == null) continue;
            if (house.HouseArea.Intersects(rect) && !(args.Player.Group.HasPermission(EditHouse) || args.Player.Account.ID.ToString() == house.Author || Utils.OwnsHouse(args.Player.Account.ID.ToString(), house)))
            {
                if (Config.Instance.WarningSpoiler) args.Player.Disable("无权放置家具!");
                args.Player.SendErrorMessage("你没有权力放置被房子保护的地区的家具。");
                args.Player.SendTileSquareCentered(tileX, tileY, 3);
                return true;
            }
        }
        return false;
    }
    private static bool HandleSign(GetDataHandlerArgs args)
    {
        var id = args.Data.ReadInt16();
        var x = args.Data.ReadInt16();
        var y = args.Data.ReadInt16();
        var house = Utils.InAreaHouse(x, y);
        if (house == null) return false;
        if (args.Player.Group.HasPermission(EditHouse) || args.Player.Account.ID.ToString() == house.Author || Utils.OwnsHouse(args.Player.Account.ID.ToString(), house) || Utils.CanUseHouse(args.Player.Account.ID.ToString(), house))
            return false;
        if (Config.Instance.WarningSpoiler) args.Player.Disable("无权修改标牌!");
        args.Player.SendErrorMessage("你没有权力修改被房子保护的地区的标牌。");
        args.Player.SendData(PacketTypes.SignNew, "", id);
        return true;
    }
    private static bool HandleLiquidSet(GetDataHandlerArgs args)
    {
        int tileX = args.Data.ReadInt16();
        int tileY = args.Data.ReadInt16();
        var house = Utils.InAreaHouse(tileX, tileY);
        if (house == null) return false;
        if (args.Player.Group.HasPermission(EditHouse) || args.Player.Account.ID.ToString() == house.Author || Utils.OwnsHouse(args.Player.Account.ID.ToString(), house))
            return false;
        if (Config.Instance.WarningSpoiler) args.Player.Disable("无权放水!");
        args.Player.SendErrorMessage("你没有权力在被房子保护的地区放水。");
        args.Player.SendTileSquareCentered(tileX, tileY);
        return true;
    }
    private static bool HandlePaintTile(GetDataHandlerArgs args)
    {
        var X = args.Data.ReadInt16();
        var Y = args.Data.ReadInt16();
        var house = Utils.InAreaHouse(X, Y);
        if (house == null) return false;
        if (args.Player.Group.HasPermission(EditHouse) || args.Player.Account.ID.ToString() == house.Author || Utils.OwnsHouse(args.Player.Account.ID.ToString(), house))
            return false;
        if (Config.Instance.WarningSpoiler) args.Player.Disable("无权油漆砖!");
        args.Player.SendErrorMessage("你没有权力在被房子保护的地区油漆砖。");
        args.Player.SendData(PacketTypes.PaintTile, "", X, Y, Main.tile[X, Y].color());
        return true;
    }
    private static bool HandlePaintWall(GetDataHandlerArgs args)
    {
        var X = args.Data.ReadInt16();
        var Y = args.Data.ReadInt16();
        var house = Utils.InAreaHouse(X, Y);
        if (house == null) return false;
        if (args.Player.Group.HasPermission(EditHouse) || args.Player.Account.ID.ToString() == house.Author || Utils.OwnsHouse(args.Player.Account.ID.ToString(), house))
            return false;
        if (Config.Instance.WarningSpoiler) args.Player.Disable("无权油漆墙!");
        args.Player.SendErrorMessage("你没有权力在被房子保护的地区油漆墙。");
        args.Player.SendData(PacketTypes.PaintWall, "", X, Y, Main.tile[X, Y].wallColor());
        return true;
    }
    private static bool HandlePlaceObject(GetDataHandlerArgs args)
    {
        int x = args.Data.ReadInt16();
        int y = args.Data.ReadInt16();
        var house = Utils.InAreaHouse(x, y);
        if (house == null) return false;
        if (args.Player.Group.HasPermission(EditHouse) || args.Player.Account.ID.ToString() == house.Author || Utils.OwnsHouse(args.Player.Account.ID.ToString(), house))
            return false;
        if (Config.Instance.WarningSpoiler) args.Player.Disable("无权修改房子保护!");
        args.Player.SendErrorMessage("你没有权力修改被房子保护的地区。");
        args.Player.SendTileSquareCentered(x, y);
        return true;
    }
    private static bool HandlePlaceTileEntity(GetDataHandlerArgs args)
    {
        var x = args.Data.ReadInt16();
        var y = args.Data.ReadInt16();
        var house = Utils.InAreaHouse(x, y);
        if (house == null) return false;
        if (args.Player.Group.HasPermission(EditHouse) || args.Player.Account.ID.ToString() == house.Author || Utils.OwnsHouse(args.Player.Account.ID.ToString(), house))
            return false;
        if (Config.Instance.WarningSpoiler) args.Player.Disable("无权修改房子保护!");
        args.Player.SendErrorMessage("你没有权力修改被房子保护的地区。");
        args.Player.SendTileSquareCentered(x, y);
        return true;
    }
    private static bool HandlePlaceItemFrame(GetDataHandlerArgs args)
    {
        var x = args.Data.ReadInt16();
        var y = args.Data.ReadInt16();
        var house = Utils.InAreaHouse(x, y);
        var te = (TEItemFrame)TileEntity.ByID[TEItemFrame.Find(x, y)];
        if (house == null) return false;
        if (args.Player.Group.HasPermission(EditHouse) || args.Player.Account.ID.ToString() == house.Author || Utils.OwnsHouse(args.Player.Account.ID.ToString(), house) || Utils.CanUseHouse(args.Player.Account.ID.ToString(), house))
            return false;
        if (Config.Instance.WarningSpoiler) args.Player.Disable("无权修改房子保护的物品!");
        args.Player.SendErrorMessage("你没有权力修改被房子保护的物品。");
        if (args.Player.SelectedItem.type > 0)
        {
            args.Player.SetData("PlaceSlot", (true, args.Player.TPlayer.selectedItem));
            NetMessage.SendData(86, -1, -1, NetworkText.Empty, te.ID);
        }
        return true;
    }
    private static bool HandleGemLockToggle(GetDataHandlerArgs args)
    {
        var x = (int)args.Data.ReadInt16();
        var y = (int)args.Data.ReadInt16();
        if (!Config.Instance.ProtectiveGemstoneLock) return false;
        var house = Utils.InAreaHouse(x, y);
        if (house == null) return false;
        if (args.Player.Group.HasPermission(EditHouse) || args.Player.Account.ID.ToString() == house.Author || Utils.OwnsHouse(args.Player.Account.ID.ToString(), house) || Utils.CanUseHouse(args.Player.Account.ID.ToString(), house))
            return false;
        if (Config.Instance.WarningSpoiler) args.Player.Disable("无权触发房子保护的宝石锁!");
        args.Player.SendErrorMessage("你没有权力触发被房子保护的宝石锁。");
        return true;
    }
    private static bool HandleMassWireOperation(GetDataHandlerArgs args)
    {
        int x1 = args.Data.ReadInt16();
        int y1 = args.Data.ReadInt16();
        int x2 = args.Data.ReadInt16();
        int y2 = args.Data.ReadInt16();
        var A = new Rectangle(Math.Min(x1, x2), args.TPlayer.direction != 1 ? y1 : y2, Math.Abs(x2 - x1) + 1, 1);
        var B = new Rectangle(args.TPlayer.direction != 1 ? x2 : x1, Math.Min(y1, y2), 1, Math.Abs(y2 - y1) + 1);
        for (var i = 0; i < HousingPlugin.Houses.Count; i++)
        {
            var house = HousingPlugin.Houses[i];
            if (house == null) continue;
            if (house.HouseArea.Intersects(A) || house.HouseArea.Intersects(B))
            {
                if (!(args.Player.Group.HasPermission(EditHouse) || args.Player.Account.ID.ToString() == house.Author || Utils.OwnsHouse(args.Player.Account.ID.ToString(), house) || Utils.CanUseHouse(args.Player.Account.ID.ToString(), house)))
                    return true;
            }
        }
        return false;
    }
    public static void ToggleHouseDisplay(TSPlayer player, House house)
    {
        if (!PlayerActiveHouses.TryGetValue(player.Index, out var list))
        {
            list = new List<Rectangle>();
            PlayerActiveHouses[player.Index] = list;
            StartRefreshCycle(player.Index);
        }
        if (list.Contains(house.HouseArea))
        {
            list.Remove(house.HouseArea);
            ClearRegionProjectiles(player, house.HouseArea);
            player.SendSuccessMessage("已隐藏房屋 " + house.Name + " 的边界。");
            if (list.Count == 0) PlayerRefreshFlags.Remove(player.Index);
        }
        else
        {
            list.Add(house.HouseArea);
            ShowRegion(player, house.HouseArea);
            player.SendSuccessMessage("已显示房屋 " + house.Name + " 的边界。");
        }
    }

    private static void StartRefreshCycle(int playerIndex)
    {
        if (PlayerRefreshFlags.ContainsKey(playerIndex) && PlayerRefreshFlags[playerIndex]) return;
        PlayerRefreshFlags[playerIndex] = true;
        Main.DelayedProcesses.Add(GetRefreshEnumerator(playerIndex));
    }

    private static IEnumerator GetRefreshEnumerator(int playerIndex)
    {
        try
        {
            while (PlayerActiveHouses.ContainsKey(playerIndex) && PlayerRefreshFlags.ContainsKey(playerIndex) && PlayerRefreshFlags[playerIndex])
            {
                var player = TShock.Players[playerIndex];
                if (player is not { ConnectionAlive: true }) yield break;
                for (var i = 0; i < 60 * RefreshIntervalSeconds; i++)
                {
                    yield return null;
                    player = TShock.Players[playerIndex];
                    if (player == null || !player.ConnectionAlive) yield break;
                }
                if (PlayerActiveHouses.TryGetValue(playerIndex, out var list))
                {
                    foreach (var rect in list) ShowRegion(player, rect);
                }
            }
        }
        finally { PlayerRefreshFlags.Remove(playerIndex); }
    }
    public static void ToggleAllDisplays(TSPlayer player, List<House> houses)
    {
        if (!PlayerActiveHouses.TryGetValue(player.Index, out var list))
        {
            list = new List<Rectangle>();
            PlayerActiveHouses[player.Index] = list;
            StartRefreshCycle(player.Index);
        }
        var anyHidden = false;
        foreach (var house in houses)
        {
            if (!list.Contains(house.HouseArea)) { anyHidden = true; break; }
        }
        if (anyHidden)
        {
            list.Clear();
            foreach (var house in houses) { list.Add(house.HouseArea); ShowRegion(player, house.HouseArea); }
            player.SendSuccessMessage("已显示所有房屋的边界。");
        }
        else
        {
            foreach (var rect in list) ClearRegionProjectiles(player, rect);
            list.Clear();
            player.SendSuccessMessage("已隐藏所有房屋的边界。");
            PlayerRefreshFlags.Remove(player.Index);
        }
    }

    private static void ShowRegion(TSPlayer ts, Rectangle rect)
    {
        var maxSide = Math.Max(rect.Width, rect.Height);
        var step = maxSide <= 30 ? 1 : Math.Clamp(maxSide / 30, 1, 10);
        int projType = ProjectileID.TopazBolt;
        for (var x = rect.Left; x <= rect.Right; x += step)
        {
            CreateProjectile(ts, x, rect.Top, projType);
            CreateProjectile(ts, x, rect.Bottom, projType);
        }
        for (var y = rect.Top; y <= rect.Bottom; y += step)
        {
            CreateProjectile(ts, rect.Left, y, projType);
            CreateProjectile(ts, rect.Right, y, projType);
        }
    }

    private static void CreateProjectile(TSPlayer ts, int tileX, int tileY, int projType)
    {
        var pos = new Vector2((tileX * 16) + 8, (tileY * 16) + 8);
        var identity = Projectile.NewProjectile(null, pos.X, pos.Y, 0f, 0f, projType, 0, 0f, ts.Index);
        NetMessage.SendData((int)PacketTypes.ProjectileNew, ts.Index, -1, null, identity);
    }

    private static void ClearRegionProjectiles(TSPlayer ts, Rectangle rect)
    {
        for (var i = 0; i < Main.projectile.Length; i++)
        {
            var proj = Main.projectile[i];
            if (proj is not { active: true } || proj.type != ProjectileID.TopazBolt) continue;
            var projTileX = (int)(proj.position.X + (proj.width / 2f)) / 16;
            var projTileY = (int)(proj.position.Y + (proj.height / 2f)) / 16;
            if (projTileX < rect.Left || projTileX > rect.Right || projTileY < rect.Top || projTileY > rect.Bottom) continue;
            proj.Kill();
            NetMessage.SendData((int)PacketTypes.ProjectileDestroy, ts.Index, -1, null, i);
        }
    }
    public static void ClearPlayerDisplays(int playerIndex)
    {
        if (!PlayerActiveHouses.TryGetValue(playerIndex, out var list)) return;
        var player = TShock.Players[playerIndex];
        if (player != null)
        {
            foreach (var rect in list) ClearRegionProjectiles(player, rect);
            PlayerRefreshFlags.Remove(player.Index);
        }
        PlayerActiveHouses.Remove(playerIndex);
    }
    public static void OnHouseDeleted(Rectangle houseArea)
    {
        foreach (var player in TShock.Players)
        {
            if (player is not { ConnectionAlive: true }) continue;
            if (PlayerActiveHouses.TryGetValue(player.Index, out var list) && list.Contains(houseArea))
            {
                list.Remove(houseArea);
                ClearRegionProjectiles(player, houseArea);
                if (list.Count == 0) PlayerActiveHouses.Remove(player.Index);
            }
        }
    }
    public static bool IsPlayerShowingHouse(int playerIndex, Rectangle houseArea)
    {
        return PlayerActiveHouses.TryGetValue(playerIndex, out var list) && list.Contains(houseArea);
    }
}
