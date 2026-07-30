using Microsoft.Xna.Framework;
using On.Terraria.GameContent;
using OTAPI;
using System.Timers;
using Terraria;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;
using Hooks = On.OTAPI.Hooks;

namespace HouseRegion;

[ApiVersion(2, 1)]
public class HousingPlugin : TerrariaPlugin
{
    public override string Author => "lmx12330";
    public override string Description => "保护房屋的插件";
    public override string Name => System.Reflection.Assembly.GetExecutingAssembly().GetName().Name!;
    public override Version Version => new Version(2, 0, 0);

    public HousingPlugin(Main game) : base(game) { }

    public static LPlayer?[] LPlayers { get; set; } = new LPlayer[256];
    public static List<House> Houses = new();
    static readonly System.Timers.Timer Update = new(1100);
    public static bool ULock = false;
    private static TSPlayer? _explosionOwner = null;

    private static readonly HashSet<int> ExplosiveTypes = new()
    {
        ProjectileID.Bomb, ProjectileID.StickyBomb, ProjectileID.BouncyBomb,
        ProjectileID.Dynamite, ProjectileID.BouncyDynamite, ProjectileID.StickyDynamite,
        ProjectileID.Grenade, ProjectileID.StickyGrenade, ProjectileID.BouncyGrenade,
        ProjectileID.Explosives,
        ProjectileID.RocketI, ProjectileID.RocketII, ProjectileID.RocketIII, ProjectileID.RocketIV,
        ProjectileID.WetBomb, ProjectileID.LavaBomb, ProjectileID.HoneyBomb, ProjectileID.DryBomb,
        ProjectileID.WetGrenade, ProjectileID.LavaGrenade, ProjectileID.HoneyGrenade, ProjectileID.DryGrenade,
        ProjectileID.WetRocket, ProjectileID.LavaRocket, ProjectileID.HoneyRocket, ProjectileID.DryRocket,
        ProjectileID.DrySnowmanRocket, ProjectileID.WetSnowmanRocket,
        ProjectileID.LavaSnowmanRocket, ProjectileID.HoneySnowmanRocket,
        ProjectileID.HappyBomb,
    };

    private static readonly HashSet<int> LiquidBombTypes = new()
    {
        ProjectileID.DirtBomb,
        ProjectileID.DirtStickyBomb,
        ProjectileID.WetBomb, ProjectileID.DryBomb,
        ProjectileID.LavaBomb, ProjectileID.HoneyBomb,
        ProjectileID.WetGrenade, ProjectileID.DryGrenade,
        ProjectileID.LavaGrenade, ProjectileID.HoneyGrenade,
        ProjectileID.WetRocket, ProjectileID.DryRocket,
        ProjectileID.LavaRocket, ProjectileID.HoneyRocket,
    };

    // ══════════════════════════════════════════════════════════
    //  初始化
    // ══════════════════════════════════════════════════════════

    public override void Initialize()
    {
        Config.Load();
        Database.EnsureTable();
        GetDataHandlers.InitGetDataHandler();
        Commands.ChatCommands.Add(new Command("house.use", HCommands, "house")
        {
            HelpText = "输入/h help 可以显示与房子相关的操作提示。"
        });
        Commands.ChatCommands.Add(new Command("house.use", HCommands, "h")
        {
            HelpText = "输入/h 可以显示与房子相关的操作提示。"
        });
        ServerApi.Hooks.NetGreetPlayer.Register(this, OnGreetPlayer);
        ServerApi.Hooks.ServerLeave.Register(this, OnLeave);
        ServerApi.Hooks.GamePostInitialize.Register(this, PostInitialize);
        if (!Main.gameMenu)
            PostInitialize(EventArgs.Empty);
        OTAPI.Hooks.Chest.QuickStack += ChestOnQuickStack;
        CraftingRequests.CanCraftFromChest += CraftingRequestsOnCanCraftFromChest;
        Hooks.MessageBuffer.InvokeGetData += MessageBufferOnInvokeGetData;
        On.Terraria.Projectile.Kill += OnProjectileKill;
        On.Terraria.WorldGen.KillTile += OnWorldGenKillTile;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Commands.ChatCommands.RemoveAll(c => c.CommandDelegate == HCommands);
            ServerApi.Hooks.NetGreetPlayer.Deregister(this, OnGreetPlayer);
            ServerApi.Hooks.ServerLeave.Deregister(this, OnLeave);
            ServerApi.Hooks.GamePostInitialize.Deregister(this, PostInitialize);
            Update.Elapsed -= OnUpdate;
            Update.Stop();
            OTAPI.Hooks.Chest.QuickStack -= ChestOnQuickStack;
            CraftingRequests.CanCraftFromChest -= CraftingRequestsOnCanCraftFromChest;
            Hooks.MessageBuffer.InvokeGetData -= MessageBufferOnInvokeGetData;
            On.Terraria.Projectile.Kill -= OnProjectileKill;
            On.Terraria.WorldGen.KillTile -= OnWorldGenKillTile;
        }
        base.Dispose(disposing);
    }

    public void PostInitialize(EventArgs e)
    {
        Houses = HouseManager.LoadAllHouses(Main.worldID.ToString());
        ShowPrefManager.Load();
        Update.Elapsed += OnUpdate;
        Update.Start();
    }

    // ══════════════════════════════════════════════════════════
    //  玩家进出事件
    // ══════════════════════════════════════════════════════════

    private void OnGreetPlayer(GreetPlayerEventArgs e)
    {
        lock (LPlayers)
        {
            LPlayers[e.Who] = new LPlayer(e.Who, TShock.Players[e.Who].TileX, TShock.Players[e.Who].TileY);
        }
    }

    private void OnLeave(LeaveEventArgs e)
    {
        lock (LPlayers)
        {
            if (LPlayers[e.Who] != null)
                LPlayers[e.Who] = null;
        }
        GetDataHandlers.ClearPlayerDisplays(e.Who);
    }

    // ══════════════════════════════════════════════════════════
    //  数据包拦截入口
    // ══════════════════════════════════════════════════════════

    private static bool MessageBufferOnInvokeGetData(
        Hooks.MessageBuffer.orig_InvokeGetData orig, MessageBuffer instance,
        ref byte packetId, ref int readOffset, ref int start, ref int length,
        ref int messageType, int maxPackets)
    {
        var user = TShock.Players[instance.whoAmI];
        using (var data = new MemoryStream(instance.readBuffer, readOffset, length))
        {
            try
            {
                if (GetDataHandlers.HandlerGetData((PacketTypes)packetId, user, data))
                    return false;
            }
            catch (Exception ex)
            {
                TShock.Log.Error("房屋插件错误传递时出错:" + ex);
            }
        }
        return orig.Invoke(instance, ref packetId, ref readOffset, ref start, ref length,
            ref messageType, maxPackets);
    }

    // ══════════════════════════════════════════════════════════
    //  OTAPI 钩子：箱子合成、快速堆叠
    // ══════════════════════════════════════════════════════════

    private static bool CraftingRequestsOnCanCraftFromChest(
        CraftingRequests.orig_CanCraftFromChest orig, Chest chest, int whoAmI)
    {
        var plr = TShock.Players[whoAmI];
        var house = Utils.InAreaHouse(chest.x, chest.y);
        if (house == null) return orig(chest, whoAmI);
        if (Utils.IsAuthorized(plr, house)) return orig(chest, whoAmI);
        if (house.AllowChest == 1) return orig(chest, whoAmI);

        plr.SendErrorMessage("你没有权力使用被房子保护的地区的箱子合成物品。");
        plr.Disable("无权使用被房子保护的地区箱子合成物品!");
        return false;
    }

    private static void ChestOnQuickStack(object? sender, OTAPI.Hooks.Chest.QuickStackEventArgs e)
    {
        var plr = TShock.Players[e.PlayerId];
        var chest = Main.chest[e.ChestIndex];
        var house = Utils.InAreaHouse(chest.x, chest.y);
        if (house == null) return;
        if (Utils.IsAuthorized(plr, house)) return;
        if (house.AllowChest == 1) return;

        plr.SendErrorMessage("你没有权力快速堆叠被房子保护的地区的箱子。");
        plr.SetBuff(BuffID.Webbed, 200, true);
        e.Result = HookResult.Cancel;
    }

    // ══════════════════════════════════════════════════════════
    //  On.Terraria 钩子：弹幕、方块破坏
    // ══════════════════════════════════════════════════════════

    private void OnProjectileKill(On.Terraria.Projectile.orig_Kill orig, Projectile self)
    {
        // 土炸弹/液体炸弹
        if (LiquidBombTypes.Contains(self.type))
        {
            var ctr = self.Center.ToTileCoordinates();
            var radius = 5;
            var blastRect = new Rectangle(ctr.X - radius, ctr.Y - radius, radius * 2, radius * 2);
            for (var i = 0; i < Houses.Count; i++)
            {
                var h = Houses[i];
                if (h != null && h.HouseArea.Intersects(blastRect))
                {
                    var player = self.owner >= 0 && self.owner < 255 ? TShock.Players[self.owner] : null;
                    if (player == null || !player.IsLoggedIn || player.Account == null) return;
                    if (!Utils.IsAuthorized(player, h))
                    {
                        player.SendErrorMessage("你没有权利修改被房子保护的地区。");
                        player.SetBuff(BuffID.Webbed, 200, true);
                        return;
                    }
                    break;
                }
            }
            _explosionOwner = self.owner >= 0 && self.owner < 255 ? TShock.Players[self.owner] : null;
            try { orig(self); } finally { _explosionOwner = null; }
            return;
        }

        // 普通爆炸物
        if (ExplosiveTypes.Contains(self.type))
        {
            _explosionOwner = self.owner >= 0 && self.owner < 255 ? TShock.Players[self.owner] : null;
            try { orig(self); } finally { _explosionOwner = null; }
            return;
        }
        orig(self);
    }

    private void OnWorldGenKillTile(On.Terraria.WorldGen.orig_KillTile orig,
        int i, int j, bool fail, bool effectOnly, bool noItem)
    {
        if (!fail && !effectOnly)
        {
            var house = Utils.InAreaHouse(i, j);
            if (house != null)
            {
                // 只有爆炸来源才检查权限
                if (_explosionOwner != null && _explosionOwner.IsLoggedIn && _explosionOwner.Account != null)
                {
                    if (!Utils.IsAuthorized(_explosionOwner, house))
                    {
                        _explosionOwner.SendErrorMessage("无权破坏房子保护的方块!");
                        _explosionOwner.SetBuff(BuffID.Webbed, 200, true);
                        return;
                    }
                }
                // _explosionOwner == null → 正常挖矿，放行
            }
        }
        orig(i, j, fail, effectOnly, noItem);
    }

    // ══════════════════════════════════════════════════════════
    //  定时器：进入/离开检测 + 边框自动显示
    // ══════════════════════════════════════════════════════════

    private void OnUpdate(object? sender, ElapsedEventArgs e)
    {
        lock (LPlayers)
        {
            for (var i = 0; i < LPlayers.Length; i++)
            {
                if (LPlayers[i] == null) continue;
                var ts = TShock.Players[i];
                if (ts == null || !ts.ConnectionAlive) continue;

                int tx = ts.TileX, ty = ts.TileY;
                var currentHouse = Utils.InAreaHouse(tx, ty);
                var lastHouse = Utils.InAreaHouse(LPlayers[i]!.TileX, LPlayers[i]!.TileY);

                // 进入事件
                if (currentHouse != null && lastHouse == null)
                {
                    if (Utils.IsAuthorized(ts, currentHouse))
                    {
                        ts.SendMessage($"你进入了你的房子: {currentHouse.Name}", Color.LightSeaGreen);
                        if (currentHouse.NotifyEnter == 1)
                            NotifyOwnerStatic(currentHouse, $"{ts.Name} 进入了房屋");
                    }
                    else if (currentHouse.AllowEntry == 0)
                    {
                        ts.SendErrorMessage("你没有权利进入此房屋。");
                        ts.SetBuff(BuffID.Webbed, 200, true);
                        GetDataHandlers.ExpelPlayer(ts, currentHouse);
                        if (currentHouse.NotifyEnter == 1)
                            NotifyOwnerStatic(currentHouse, $"{ts.Name} 试图进入房屋");
                        LPlayers[i]!.TileX = tx;
                        LPlayers[i]!.TileY = ty;
                        continue;
                    }
                    else
                    {
                        ts.SendMessage($"你进入了房子: {currentHouse.Name}", Color.LightSeaGreen);
                        if (currentHouse.NotifyEnter == 1)
                            NotifyOwnerStatic(currentHouse, $"{ts.Name} 进入了房屋");
                    }

                    // 自动显示边框
                    bool isMine = Utils.IsAuthorized(ts, currentHouse);
                    string myId = ts.Account.ID.ToString();
                    if ((isMine && ShowPrefManager.GetShowMe(myId)) ||
                        (!isMine && ShowPrefManager.GetShowOthers(myId)))
                    {
                        GetDataHandlers.ShowHouseDisplay(ts, currentHouse);
                    }
                }

                // 离开事件
                if (currentHouse == null && lastHouse != null)
                {
                    ts.SendMessage($"你离开了房子: {lastHouse.Name}", Color.LightSeaGreen);
                    GetDataHandlers.HideHouseDisplay(ts, lastHouse);
                }

                LPlayers[i]!.TileX = tx;
                LPlayers[i]!.TileY = ty;
            }
        }
    }

    private static void NotifyOwnerStatic(House house, string msg)
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
        catch { }
    }

    // ══════════════════════════════════════════════════════════
    //  指令处理
    // ══════════════════════════════════════════════════════════

    private void HCommands(CommandArgs args)
    {
        if (!args.Player.IsLoggedIn || args.Player.Account == null || args.Player.Account.ID == 0)
        {
            args.Player.SendErrorMessage("你必须登录才能使用房子插件。");
            return;
        }

        var cmd = "help";
        if (args.Parameters.Count > 0)
            cmd = args.Parameters[0].ToLower();

        switch (cmd)
        {
            case "help":
                HandleHelp(args);
                break;

            case "c":
                HandleC(args);
                break;

            case "name":
                args.Player.SendMessage("请敲击一个块查看它属于哪个房子。", Color.Yellow);
                var lp = LPlayers[args.Player.Index];
                if (lp != null) lp.Look = true;
                break;

            case "set":
                HandleSet(args);
                break;

            case "add":
                HandleAdd(args);
                break;

            case "delete":
                HandleDelete(args);
                break;

            case "redefine":
                HandleRedefine(args);
                break;

            case "clear":
                args.Player.TempPoints[0] = Point.Zero;
                args.Player.TempPoints[1] = Point.Zero;
                args.Player.AwaitingTempPoint = 0;
                args.Player.SendMessage("临时敲击点清除完毕!", Color.Yellow);
                break;

            case "list":
                HandleList(args);
                break;

            case "info":
                HandleInfo(args);
                break;

            case "allow":
                HandleAllow(args);
                break;

            case "disallow":
                HandleDisallow(args);
                break;

            case "adduser":
                HandleAddUser(args);
                break;

            case "deluser":
                HandleDelUser(args);
                break;

            case "tp":
                HandleTP(args);
                break;

            case "settp":
                HandleSetTP(args);
                break;

            case "setexpel":
                HandleSetExpel(args);
                break;

            case "editmsg":
                HandleEditMsg(args);
                break;

            case "settings":
                HandleSettings(args);
                break;

            case "show":
                HandleShowStatus(args);
                break;

            case "showme":
                HandleShowMe(args);
                break;

            case "showothers":
                HandleShowOthers(args);
                break;

            default:
                // 尝试匹配房屋权限设置: /house [屋名] 项目名 0/1
                TryHandlePermission(args, cmd);
                break;
        }
    }

    private void HandleHelp(CommandArgs args)
    {
        var myHouses = Houses.Where(h =>
            args.Player.Account.ID.ToString() == h.Author ||
            Utils.OwnsHouse(args.Player.Account.ID.ToString(), h) ||
            Utils.CanUseHouse(args.Player.Account.ID.ToString(), h)).ToList();

        if (myHouses.Count > 0)
        {
            var colors = new Color[] { Color.LightYellow, Color.Cyan, Color.MediumPurple,
                Color.Lime, Color.Orange, Color.Pink, Color.SkyBlue, Color.LightGreen };
            var msg = "你的房屋:";
            for (int i = 0; i < myHouses.Count && i < colors.Length; i++)
                msg += $" [{myHouses[i].Name}]";
            args.Player.SendMessage(msg, Color.White);
        }
        else
        {
            args.Player.SendMessage("你还没有房屋，使用 /h c 创建一个吧", Color.Gray);
        }

        args.Player.SendMessage("/h c 圈地  |  /h settings 查看设置  |  /h tp <屋名> 传送", Color.Lime);
    }

    // ── 新命令 ──

    private void HandleC(CommandArgs args)
    {
        if (args.Parameters.Count <= 1)
        {
            args.Player.SendMessage("/h c 1      — 设置左上角点（敲击方块）", Color.White);
            args.Player.SendMessage("/h c 2      — 设置右下角点（敲击方块）", Color.White);
            args.Player.SendMessage("/h c 屋名   — 完成圈地，创建房屋", Color.White);
            args.Player.SendMessage("/h c clear  — 清除已选的点", Color.White);
            return;
        }
        var sub = args.Parameters[1];
        if (sub == "1" || sub == "2")
        {
            int choice = int.Parse(sub);
            args.Player.SendMessage(choice == 1 ? "现在请敲击要保护的区域的左上角。" : "现在请敲击要保护的区域的右下角。", Color.Yellow);
            args.Player.AwaitingTempPoint = choice;
        }
        else if (sub.ToLower() == "clear")
        {
            args.Player.TempPoints[0] = Point.Zero;
            args.Player.TempPoints[1] = Point.Zero;
            args.Player.AwaitingTempPoint = 0;
            args.Player.SendMessage("临时敲击点清除完毕!", Color.Yellow);
        }
        else
        {
            // 当作屋名创建：构造参数让 HandleAdd 处理
            var saved = new List<string>(args.Parameters);
            args.Parameters.Clear();
            args.Parameters.Add("add");
            args.Parameters.Add(sub);
            HandleAdd(args);
            args.Parameters.Clear();
            args.Parameters.AddRange(saved);
        }
    }

    private void HandleSettings(CommandArgs args)
    {
        House? house;
        if (args.Parameters.Count > 1)
            house = Utils.GetHouseByName(string.Join(" ", args.Parameters.GetRange(1, args.Parameters.Count - 1)));
        else
            house = Utils.CurrentHouse(args.Player);

        if (house == null)
        {
            args.Player.SendMessage("请站在房屋内或指定屋名: /h settings 屋名", Color.Yellow);
            return;
        }

        bool canEdit = house.Author == args.Player.Account.ID.ToString() ||
                       Utils.OwnsHouse(args.Player.Account.ID.ToString(), house) ||
                       args.Player.Group.HasPermission(GetDataHandlers.AdminHouse);

        ShowSettingsPanel(args.Player, house, canEdit);
    }

    private static void ShowSettingsPanel(TSPlayer plr, House house, bool canEdit)
    {
        var on = Color.Lime; var off = Color.Red;
        var gray = Color.Gray;

        plr.SendMessage($"━━━ {house.Name} ━━━", Color.Gold);
        var authorName = "?";
        try { authorName = TShock.UserAccounts.GetUserAccountByID(Convert.ToInt32(house.Author)).Name; } catch { }
        plr.SendMessage($"房主: {authorName}", Color.White);
        plr.SendMessage($"区域: {house.HouseArea.Width}×{house.HouseArea.Height}  传送点: ({house.TpX},{house.TpY})", Color.White);

        plr.SendMessage("通知设置", gray);
        plr.SendMessage(
            $"  破坏通知: {(house.NotifyBreakPlace == 1 ? "●开" : "○开")}  {(house.NotifyBreakPlace == 1 ? "○关" : "●关")}",
            house.NotifyBreakPlace == 1 ? on : off);
        plr.SendMessage(
            $"  进入通知: {(house.NotifyEnter == 1 ? "●开" : "○开")}  {(house.NotifyEnter == 1 ? "○关" : "●关")}",
            house.NotifyEnter == 1 ? on : off);

        plr.SendMessage(canEdit ? "权限设置  (/h 参数 0/1 修改)" : "权限设置", gray);
        plr.SendMessage(
            $"  允许进入 {(house.AllowEntry == 1 ? '●' : '○')}     放置 {(house.AllowPlace == 1 ? '●' : '○')}     破坏 {(house.AllowBreak == 1 ? '●' : '○')}",
            house.AllowEntry == 1 ? on : off);
        plr.SendMessage(
            $"  传送     {(house.AllowTP == 1 ? '●' : '○')}     液体 {(house.AllowLiquid == 1 ? '●' : '○')}     箱子 {(house.AllowChest == 1 ? '●' : '○')}",
            house.AllowTP == 1 ? on : off);
        plr.SendMessage(
            $"  植物     {(house.AllowPlant == 1 ? '●' : '○')}     复活 {(house.AllowSpawn == 1 ? '●' : '○')}     挖坟 {(house.AllowGrave == 1 ? '●' : '○')}",
            house.AllowPlant == 1 ? on : off);
        plr.SendMessage(
            $"  开关     {(house.AllowSwitch == 1 ? '●' : '○')}     门   {(house.AllowDoor == 1 ? '●' : '○')}     易碎 {(house.AllowFragile == 1 ? '●' : '○')}",
            house.AllowSwitch == 1 ? on : off);
        plr.SendMessage(
            $"  违规驱离 {(house.ExpelOnViolate == 1 ? '●' : '○')}",
            house.ExpelOnViolate == 1 ? on : off);

        if (!canEdit)
            plr.SendMessage("你无权修改此房屋设置", Color.Red);
    }

    private void HandleShowStatus(CommandArgs args)
    {
        var id = args.Player.Account.ID.ToString();
        var showMe = ShowPrefManager.GetShowMe(id);
        var showOthers = ShowPrefManager.GetShowOthers(id);
        var on = Color.Lime; var off = Color.Red;

        args.Player.SendMessage("━━━ 边框显示 ━━━", Color.Gold);
        args.Player.SendMessage(
            $"自己房屋: {(showMe ? "●开  ○关" : "○开  ●关")}",
            showMe ? on : off);
        args.Player.SendMessage(
            $"他人房屋: {(showOthers ? "●开  ○关" : "○开  ●关")}",
            showOthers ? on : off);
        args.Player.SendMessage("/h showme     — 切换自己房屋自动边框", Color.White);
        args.Player.SendMessage("/h showothers — 切换他人房屋自动边框", Color.White);
    }

    private void HandleShowMe(CommandArgs args)
    {
        var id = args.Player.Account.ID.ToString();
        bool now = ShowPrefManager.ToggleShowMe(id);
        args.Player.SendSuccessMessage($"自己房屋自动边框: {(now ? "开" : "关")}");
    }

    private void HandleShowOthers(CommandArgs args)
    {
        var id = args.Player.Account.ID.ToString();
        bool now = ShowPrefManager.ToggleShowOthers(id);
        args.Player.SendSuccessMessage($"他人房屋自动边框: {(now ? "开" : "关")}");
    }

    // ── 圈地相关 ──

    private void HandleSet(CommandArgs args)
    {
        if (args.Parameters.Count == 2 && int.TryParse(args.Parameters[1], out var choice) && choice >= 1 && choice <= 2)
        {
            args.Player.SendMessage(choice == 1 ? "现在请敲击要保护的区域的左上角。" : "现在请敲击要保护的区域的右下角。", Color.Yellow);
            args.Player.AwaitingTempPoint = choice;
        }
        else
        {
            args.Player.SendErrorMessage("指令错误! 正确指令: /house set [1/2]");
        }
    }

    private void HandleAdd(CommandArgs args)
    {
        if (args.Parameters.Count <= 1)
        {
            args.Player.SendErrorMessage("语法错误! 正确语法: /house add [屋名]");
            return;
        }

        var maxHouses = Utils.MaxCount(args.Player);
        var authorHouses = Houses.Count(h => h.Author == args.Player.Account.ID.ToString());
        if (authorHouses >= maxHouses && !args.Player.Group.HasPermission("house.bypasscount"))
        {
            args.Player.SendErrorMessage($"房屋添加失败:您只能添加{maxHouses}个房屋!");
            return;
        }

        if (args.Player.TempPoints.Any(p => p == Point.Zero))
        {
            args.Player.SendErrorMessage("未设置完整的房屋点,建议先使用指令: /house help");
            return;
        }

        var houseName = string.Join(" ", args.Parameters.GetRange(1, args.Parameters.Count - 1));
        if (string.IsNullOrEmpty(houseName))
        {
            args.Player.SendErrorMessage("房屋名称不能为空。");
            return;
        }

        var x = Math.Min(args.Player.TempPoints[0].X, args.Player.TempPoints[1].X);
        var y = Math.Min(args.Player.TempPoints[0].Y, args.Player.TempPoints[1].Y);
        var width = Math.Abs(args.Player.TempPoints[0].X - args.Player.TempPoints[1].X) + 1;
        var height = Math.Abs(args.Player.TempPoints[0].Y - args.Player.TempPoints[1].Y) + 1;
        var maxSize = Utils.MaxSize(args.Player);

        if ((width * height > maxSize || width < Config.Instance.MinWidth || height < Config.Instance.MinHeight) &&
            !args.Player.Group.HasPermission("house.bypasssize"))
        {
            args.Player.SendErrorMessage($"您设置的房屋宽:{width} 高:{height} 面积:{width * height} 需重新设置。");
            if (width * height > maxSize) args.Player.SendErrorMessage($"因为您的房子总面积超过了最大限制 {maxSize} 格块。");
            if (width < Config.Instance.MinWidth) args.Player.SendErrorMessage($"因为您的房屋宽度小于最小限制 {Config.Instance.MinWidth} 格块。");
            if (height < Config.Instance.MinHeight) args.Player.SendErrorMessage($"因为您的房屋高度小于最小限制 {Config.Instance.MinHeight} 格块。");
            args.Player.TempPoints[0] = Point.Zero;
            args.Player.TempPoints[1] = Point.Zero;
            return;
        }

        var newHouseR = new Rectangle(x, y, width, height);

        // 出生点保护
        if (newHouseR.Intersects(new Rectangle(
            Main.spawnTileX, Main.spawnTileY,
            TShock.Config.Settings.SpawnProtectionRadius,
            TShock.Config.Settings.SpawnProtectionRadius)))
        {
            args.Player.SendErrorMessage("你选择的区域与出生保护范围重叠，这是不允许的。");
            args.Player.TempPoints[0] = Point.Zero;
            args.Player.TempPoints[1] = Point.Zero;
            return;
        }

        // 重叠检查
        for (var i = 0; i < Houses.Count; i++)
        {
            if (newHouseR.Intersects(Houses[i].HouseArea))
            {
                args.Player.SendErrorMessage("你选择的区域与其他房子存在重叠，这是不允许的。");
                args.Player.TempPoints[0] = Point.Zero;
                args.Player.TempPoints[1] = Point.Zero;
                return;
            }
        }

        for (var i = 0; i < TShock.Regions.Regions.Count; i++)
        {
            if (newHouseR.Intersects(TShock.Regions.Regions[i].Area))
            {
                args.Player.SendErrorMessage($"你选择的区域与Tshock区域 {TShock.Regions.Regions[i].Name} 重叠，这是不允许的。");
                args.Player.TempPoints[0] = Point.Zero;
                args.Player.TempPoints[1] = Point.Zero;
                return;
            }
        }

        if (HouseManager.AddHouse(x, y, width, height, houseName,
            args.Player.Account.ID.ToString(),
            args.Player.TileX, args.Player.TileY))
        {
            args.Player.SendMessage("你建造了新房子 " + houseName, Color.Yellow);
            TShock.Log.ConsoleInfo("{0} 建了新房子: {1}", args.Player.Account.Name, houseName);
        }
        else
        {
            args.Player.SendErrorMessage("房子 " + houseName + " 已存在!");
        }

        args.Player.TempPoints[0] = Point.Zero;
        args.Player.TempPoints[1] = Point.Zero;
    }

    private void HandleDelete(CommandArgs args)
    {
        if (args.Parameters.Count <= 1)
        {
            args.Player.SendErrorMessage("语法错误! 正确语法: /house delete [屋名]");
            return;
        }
        var houseName = string.Join(" ", args.Parameters.GetRange(1, args.Parameters.Count - 1));
        var house = Utils.GetHouseByName(houseName);
        if (house == null) { args.Player.SendErrorMessage("没有找到这个房子!"); return; }
        if (house.Author != args.Player.Account.ID.ToString() && !args.Player.Group.HasPermission(GetDataHandlers.AdminHouse))
        { args.Player.SendErrorMessage("你没有权力删除这个房子!"); return; }

        if (HouseManager.DeleteHouse(house.Name))
        {
            GetDataHandlers.OnHouseDeleted(house.HouseArea);
            Houses.Remove(house);
            args.Player.SendMessage("房屋:" + house.Name + " 删除成功!", Color.Yellow);
            TShock.Log.ConsoleInfo("{0} 删除房屋: {1}", args.Player.Account.Name, house.Name);
        }
        else
        {
            args.Player.SendErrorMessage("房屋删除失败!");
        }
    }

    private void HandleRedefine(CommandArgs args)
    {
        if (args.Parameters.Count <= 1)
        {
            args.Player.SendErrorMessage("语法错误! 正确语法: /house redefine [屋名]");
            return;
        }
        if (args.Player.TempPoints.Any(p => p == Point.Zero))
        {
            args.Player.SendErrorMessage("未设置完整的房屋点,建议先使用指令: /house help");
            return;
        }

        var houseName = string.Join(" ", args.Parameters.GetRange(1, args.Parameters.Count - 1));
        var house = Utils.GetHouseByName(houseName);
        if (house == null) { args.Player.SendErrorMessage("没有找到这个房子!"); return; }
        if (house.Author != args.Player.Account.ID.ToString() && !args.Player.Group.HasPermission(GetDataHandlers.AdminHouse))
        { args.Player.SendErrorMessage("你没有权力修改这个房子!"); return; }

        var x = Math.Min(args.Player.TempPoints[0].X, args.Player.TempPoints[1].X);
        var y = Math.Min(args.Player.TempPoints[0].Y, args.Player.TempPoints[1].Y);
        var width = Math.Abs(args.Player.TempPoints[0].X - args.Player.TempPoints[1].X) + 1;
        var height = Math.Abs(args.Player.TempPoints[0].Y - args.Player.TempPoints[1].Y) + 1;
        var maxSize = Utils.MaxSize(args.Player);

        if ((width * height > maxSize || width < Config.Instance.MinWidth || height < Config.Instance.MinHeight) &&
            !args.Player.Group.HasPermission("house.bypasssize"))
        {
            args.Player.SendErrorMessage("设置的尺寸不符合要求。");
            args.Player.TempPoints[0] = Point.Zero;
            args.Player.TempPoints[1] = Point.Zero;
            return;
        }

        var newHouseR = new Rectangle(x, y, width, height);
        for (var i = 0; i < Houses.Count; i++)
        {
            if (newHouseR.Intersects(Houses[i].HouseArea) && Houses[i].Name != house.Name)
            {
                args.Player.SendErrorMessage("你选择的区域与其他房子存在重叠，这是不允许的。");
                args.Player.TempPoints[0] = Point.Zero;
                args.Player.TempPoints[1] = Point.Zero;
                return;
            }
        }

        if (HouseManager.RedefineHouse(x, y, width, height, houseName))
        {
            args.Player.SendMessage("重新定义了房子 " + houseName, Color.Yellow);
            TShock.Log.ConsoleInfo("{0} 重新定义的房子: {1}", args.Player.Account.Name, houseName);
        }
        else
        {
            args.Player.SendErrorMessage("重新定义房屋时出错!");
        }

        args.Player.TempPoints[0] = Point.Zero;
        args.Player.TempPoints[1] = Point.Zero;
    }

    // ── 列表 / 信息 ──

    private void HandleList(CommandArgs args)
    {
        const int pagelimit = 15;
        const int perline = 5;
        var page = 0;
        if (args.Parameters.Count > 1)
        {
            if (!int.TryParse(args.Parameters[1], out page) || page < 1)
            { args.Player.SendErrorMessage($"无效页码 ({args.Parameters[1]})"); return; }
            page--;
        }

        var myHouses = Houses.Where(h =>
            args.Player.Group.HasPermission(GetDataHandlers.AdminHouse) ||
            args.Player.Account.ID.ToString() == h.Author ||
            Utils.OwnsHouse(args.Player.Account.ID.ToString(), h) ||
            Utils.CanUseHouse(args.Player.Account.ID.ToString(), h)).ToList();

        if (myHouses.Count == 0)
        {
            args.Player.SendMessage("您目前还没有已定义房屋。", Color.Yellow);
            return;
        }

        var pagecount = myHouses.Count / pagelimit;
        if (page > pagecount)
        {
            args.Player.SendErrorMessage($"页码超过最大页数 ({page + 1}/{pagecount + 1})");
            return;
        }

        args.Player.SendMessage($"目前的房屋 ({page + 1}/{pagecount + 1}) 页:", Color.Green);
        var names = myHouses.Skip(page * pagelimit).Take(pagelimit).Select(h => h.Name).ToArray();
        for (var i = 0; i < names.Length; i += perline)
            args.Player.SendMessage(string.Join(", ", names, i, Math.Min(names.Length - i, perline)), Color.Yellow);

        if (page < pagecount)
            args.Player.SendMessage($"输入 /house list {page + 2} 查看更多房屋。", Color.Yellow);
    }

    private void HandleInfo(CommandArgs args)
    {
        House? house;
        if (args.Parameters.Count > 1)
        {
            var name = string.Join(" ", args.Parameters.GetRange(1, args.Parameters.Count - 1));
            house = Utils.GetHouseByName(name);
        }
        else
        {
            house = Utils.CurrentHouse(args.Player);
        }
        if (house == null) { args.Player.SendErrorMessage("未找到房屋。"); return; }

        var authorName = "未知";
        try { authorName = TShock.UserAccounts.GetUserAccountByID(Convert.ToInt32(house.Author)).Name; } catch { }

        args.Player.SendMessage($"房屋: {house.Name}", Color.Green);
        args.Player.SendMessage($"房主: {authorName}", Color.Yellow);
        var ownerNames = string.Join(", ", house.Owners.Select(id =>
        {
            try { return TShock.UserAccounts.GetUserAccountByID(Convert.ToInt32(id))?.Name ?? id; } catch { return id; }
        }));
        var userNames = string.Join(", ", house.Users.Select(id =>
        {
            try { return TShock.UserAccounts.GetUserAccountByID(Convert.ToInt32(id))?.Name ?? id; } catch { return id; }
        }));
        if (!string.IsNullOrEmpty(ownerNames)) args.Player.SendMessage($"共有者: {ownerNames}", Color.Yellow);
        if (!string.IsNullOrEmpty(userNames)) args.Player.SendMessage($"使用者: {userNames}", Color.Yellow);
        args.Player.SendMessage($"区域: ({house.HouseArea.X}, {house.HouseArea.Y}) → ({house.HouseArea.Right}, {house.HouseArea.Bottom})  {house.HouseArea.Width}×{house.HouseArea.Height}", Color.Yellow);
        args.Player.SendMessage($"传送点: ({house.TpX}, {house.TpY})", Color.Yellow);
        args.Player.SendMessage($"驱离点: {(house.ExpelX.HasValue ? $"({house.ExpelX}, {house.ExpelY})" : "未设置")}", Color.Yellow);
        args.Player.SendMessage($"违规驱离: {(house.ExpelOnViolate == 1 ? "开" : "关")}", Color.Yellow);
        args.Player.SendMessage("", Color.Yellow);
        args.Player.SendMessage("━━━ 通知 ━━━", Color.Green);
        args.Player.SendMessage($"进入通知: {(house.NotifyEnter == 1 ? "开" : "关")}", Color.Yellow);
        args.Player.SendMessage($"破坏通知: {(house.NotifyBreakPlace == 1 ? "开" : "关")}", Color.Yellow);
        args.Player.SendMessage("", Color.Yellow);
        args.Player.SendMessage("━━━ 权限 ━━━", Color.Green);
        args.Player.SendMessage(
            $"进入: {(house.AllowEntry == 1 ? "✓" : "✗")}  " +
            $"传送: {(house.AllowTP == 1 ? "✓" : "✗")}  " +
            $"放置: {(house.AllowPlace == 1 ? "✓" : "✗")}  " +
            $"破坏: {(house.AllowBreak == 1 ? "✓" : "✗")}", Color.Yellow);
        args.Player.SendMessage(
            $"液体: {(house.AllowLiquid == 1 ? "✓" : "✗")}  " +
            $"箱子: {(house.AllowChest == 1 ? "✓" : "✗")}  " +
            $"植物: {(house.AllowPlant == 1 ? "✓" : "✗")}  " +
            $"复活点: {(house.AllowSpawn == 1 ? "✓" : "✗")}", Color.Yellow);
        args.Player.SendMessage(
            $"挖坟: {(house.AllowGrave == 1 ? "✓" : "✗")}  " +
            $"开关: {(house.AllowSwitch == 1 ? "✓" : "✗")}  " +
            $"门: {(house.AllowDoor == 1 ? "✓" : "✗")}  " +
            $"易碎品: {(house.AllowFragile == 1 ? "✓" : "✗")}", Color.Yellow);
    }

    // ── 共有者/使用者管理 ──

    private void HandleAllow(CommandArgs args)
    {
        if (args.Parameters.Count <= 2)
        { args.Player.SendErrorMessage("语法错误! 正确语法: /house allow [名字] [屋名]"); return; }
        var playerName = args.Parameters[1];
        var housename = string.Join(" ", args.Parameters.GetRange(2, args.Parameters.Count - 2));
        var house = Utils.GetHouseByName(housename);
        if (house == null) { args.Player.SendErrorMessage("没有找到这个房子!"); return; }
        if (house.Author != args.Player.Account.ID.ToString() && !args.Player.Group.HasPermission(GetDataHandlers.AdminHouse))
        { args.Player.SendErrorMessage("你没有权力分享这个房子!"); return; }

        var target = TShock.UserAccounts.GetUserAccountByName(playerName);
        if (target == null) { args.Player.SendErrorMessage($"用户 {playerName} 不存在。"); return; }
        if (target.ID.ToString() == house.Author || Utils.OwnsHouse(target.ID.ToString(), house))
        { args.Player.SendErrorMessage($"用户 {playerName} 已拥有此房屋权限。"); return; }

        if (HouseManager.AddNewOwner(house.Name, target.ID.ToString()))
        {
            args.Player.SendMessage($"成功为 {playerName} 添加房屋 {house.Name} 的拥有权!", Color.Yellow);
            TShock.Log.ConsoleInfo("{0} 添加 {1} 为房屋 {2} 的拥有者。", args.Player.Account.Name, target.Name, house.Name);
        }
        else { args.Player.SendErrorMessage("添加用户权力失败。"); }
    }

    private void HandleDisallow(CommandArgs args)
    {
        if (args.Parameters.Count <= 2)
        { args.Player.SendErrorMessage("语法错误! 正确语法: /house disallow [名字] [屋名]"); return; }
        var playerName = args.Parameters[1];
        var house = Utils.GetHouseByName(string.Join(" ", args.Parameters.GetRange(2, args.Parameters.Count - 2)));
        if (house == null) { args.Player.SendErrorMessage("没有找到这个房子!"); return; }
        if (house.Author != args.Player.Account.ID.ToString() && !args.Player.Group.HasPermission(GetDataHandlers.AdminHouse))
        { args.Player.SendErrorMessage("你没有权力管理这个房子!"); return; }

        var target = TShock.UserAccounts.GetUserAccountByName(playerName);
        if (target == null) { args.Player.SendErrorMessage($"用户 {playerName} 不存在。"); return; }
        if (!Utils.OwnsHouse(target.ID.ToString(), house))
        { args.Player.SendErrorMessage("目标非此房屋拥有者。"); return; }

        if (HouseManager.DeleteOwner(house.Name, target.ID.ToString()))
        {
            args.Player.SendMessage($"成功移除 {playerName} 的房屋 {house.Name} 的拥有权!", Color.Yellow);
            TShock.Log.ConsoleInfo("{0} 移除 {1} 的房屋 {2} 的拥有者。", args.Player.Account.Name, target.Name, house.Name);
        }
        else { args.Player.SendErrorMessage("移除用户权力失败。"); }
    }

    private void HandleAddUser(CommandArgs args)
    {
        if (args.Parameters.Count <= 2)
        { args.Player.SendErrorMessage("语法错误! 正确语法: /house adduser [名字] [屋名]"); return; }
        var playerName = args.Parameters[1];
        var housename = string.Join(" ", args.Parameters.GetRange(2, args.Parameters.Count - 2));
        var house = Utils.GetHouseByName(housename);
        if (house == null) { args.Player.SendErrorMessage("没有找到这个房子!"); return; }
        if (house.Author != args.Player.Account.ID.ToString() && !Utils.OwnsHouse(args.Player.Account.ID.ToString(), house) && !args.Player.Group.HasPermission(GetDataHandlers.AdminHouse))
        { args.Player.SendErrorMessage("你没有权力分享这个房子!"); return; }

        var target = TShock.UserAccounts.GetUserAccountByName(playerName);
        if (target == null) { args.Player.SendErrorMessage($"用户 {playerName} 不存在。"); return; }
        if (target.ID.ToString() == house.Author || Utils.OwnsHouse(target.ID.ToString(), house) || Utils.CanUseHouse(target.ID.ToString(), house))
        { args.Player.SendErrorMessage($"用户 {playerName} 已拥有此房屋权限。"); return; }

        if (HouseManager.AddNewUser(house.Name, target.ID.ToString()))
        {
            args.Player.SendMessage($"成功为 {playerName} 添加房屋 {house.Name} 的使用权!", Color.Yellow);
            TShock.Log.ConsoleInfo("{0} 添加 {1} 为房屋 {2} 的使用者。", args.Player.Account.Name, target.Name, house.Name);
        }
        else { args.Player.SendErrorMessage("添加用户权力失败。"); }
    }

    private void HandleDelUser(CommandArgs args)
    {
        if (args.Parameters.Count <= 2)
        { args.Player.SendErrorMessage("语法错误! 正确语法: /house deluser [名字] [屋名]"); return; }
        var playerName = args.Parameters[1];
        var house = Utils.GetHouseByName(string.Join(" ", args.Parameters.GetRange(2, args.Parameters.Count - 2)));
        if (house == null) { args.Player.SendErrorMessage("没有找到这个房子!"); return; }
        if (house.Author != args.Player.Account.ID.ToString() && !Utils.OwnsHouse(args.Player.Account.ID.ToString(), house) && !args.Player.Group.HasPermission(GetDataHandlers.AdminHouse))
        { args.Player.SendErrorMessage("你没有权力管理这个房子!"); return; }

        var target = TShock.UserAccounts.GetUserAccountByName(playerName);
        if (target == null) { args.Player.SendErrorMessage($"用户 {playerName} 不存在。"); return; }
        if (!Utils.CanUseHouse(target.ID.ToString(), house))
        { args.Player.SendErrorMessage("目标非此房屋使用者。"); return; }

        if (HouseManager.DeleteUser(house.Name, target.ID.ToString()))
        {
            args.Player.SendMessage($"成功移除 {playerName} 的房屋 {house.Name} 的使用权!", Color.Yellow);
            TShock.Log.ConsoleInfo("{0} 移除 {1} 的房屋 {2} 的使用者。", args.Player.Account.Name, target.Name, house.Name);
        }
        else { args.Player.SendErrorMessage("移除用户权力失败。"); }
    }

    // ── 新增指令：tp / settp / setexpel / editmsg ──

    private void HandleTP(CommandArgs args)
    {
        House? house;
        if (args.Parameters.Count > 1)
            house = Utils.GetHouseByName(string.Join(" ", args.Parameters.GetRange(1, args.Parameters.Count - 1)));
        else
            house = Utils.CurrentHouse(args.Player);

        if (house == null) { args.Player.SendErrorMessage("没有找到这个房子!"); return; }

        if (!Utils.IsAuthorized(args.Player, house) && house.AllowTP == 0)
        { args.Player.SendErrorMessage("你没有权力传送这个房子!"); return; }

        args.Player.Teleport(house.TpX * 16, house.TpY * 16);
        args.Player.SendSuccessMessage("已将你传送到房屋: " + house.Name);
    }

    private void HandleSetTP(CommandArgs args)
    {
        House? house;
        int px, py;
        ParseHouseWithCoords(args, out house, out px, out py);
        if (house == null) { args.Player.SendErrorMessage("未找到房屋。"); return; }

        if (house.Author != args.Player.Account.ID.ToString() &&
            !Utils.OwnsHouse(args.Player.Account.ID.ToString(), house) &&
            !args.Player.Group.HasPermission(GetDataHandlers.AdminHouse))
        { args.Player.SendErrorMessage("你没有权力修改这个房子!"); return; }

        // 校验：传送点必须在房屋内
        if (!house.HouseArea.Contains(px, py))
        { args.Player.SendErrorMessage("传送点必须在房屋矩形范围内!"); return; }

        if (HouseManager.UpdateTP(house.Name, px, py))
        {
            house.TpX = px; house.TpY = py;
            args.Player.SendSuccessMessage($"房屋 {house.Name} 的传送点已设置为 ({px}, {py})。");
        }
        else { args.Player.SendErrorMessage("设置传送点失败。"); }
    }

    private void HandleSetExpel(CommandArgs args)
    {
        House? house;
        int px, py;
        ParseHouseWithCoords(args, out house, out px, out py);
        if (house == null) { args.Player.SendErrorMessage("未找到房屋。"); return; }

        if (house.Author != args.Player.Account.ID.ToString() &&
            !Utils.OwnsHouse(args.Player.Account.ID.ToString(), house) &&
            !args.Player.Group.HasPermission(GetDataHandlers.AdminHouse))
        { args.Player.SendErrorMessage("你没有权力修改这个房子!"); return; }

        // 校验：驱离点必须在房屋外
        if (house.HouseArea.Contains(px, py))
        { args.Player.SendErrorMessage("驱离点必须在房屋矩形范围外!"); return; }

        // 校验：距离不超过 100 格
        var dist = Utils.DistanceToRect(new Point(px, py), house.HouseArea);
        if (dist > 100)
        { args.Player.SendErrorMessage($"驱离点距离房屋边界 {dist} 格，不能超过 100 格!"); return; }

        if (HouseManager.UpdateExpel(house.Name, px, py))
        {
            house.ExpelX = px; house.ExpelY = py;
            args.Player.SendSuccessMessage($"房屋 {house.Name} 的驱离点已设置为 ({px}, {py})。");
        }
        else { args.Player.SendErrorMessage("设置驱离点失败。"); }
    }

    private void HandleEditMsg(CommandArgs args)
    {
        // /house editmsg [屋名] [类型] [0/1]
        if (args.Parameters.Count < 2)
        { args.Player.SendErrorMessage("语法错误! 正确语法: /house editmsg [屋名] [0/1] [0/1]"); return; }

        // 尝试解析参数
        int typeIndex = 1;
        House? house = null;

        // 判断第一个参数是否为屋名
        var maybeHouse = Utils.GetHouseByName(args.Parameters[1]);
        if (maybeHouse != null)
        {
            house = maybeHouse;
            typeIndex = 2;
        }
        else
        {
            house = Utils.CurrentHouse(args.Player);
            typeIndex = 1;
        }

        if (house == null) { args.Player.SendErrorMessage("未找到房屋。"); return; }

        if (house.Author != args.Player.Account.ID.ToString() &&
            !Utils.OwnsHouse(args.Player.Account.ID.ToString(), house) &&
            !args.Player.Group.HasPermission(GetDataHandlers.AdminHouse))
        { args.Player.SendErrorMessage("你没有权力修改这个房子!"); return; }

        if (args.Parameters.Count <= typeIndex)
        { args.Player.SendErrorMessage("请指定通知类型 (0=进入通知, 1=破坏通知) 和值 (0/1)。"); return; }

        if (!int.TryParse(args.Parameters[typeIndex], out var notifyType) || notifyType < 0 || notifyType > 1)
        { args.Player.SendErrorMessage("通知类型必须是 0（进入通知）或 1（破坏通知）。"); return; }

        var valIdx = typeIndex + 1;
        var val = 1; // 默认开
        if (args.Parameters.Count > valIdx)
        {
            if (!int.TryParse(args.Parameters[valIdx], out val) || val < 0 || val > 1)
            { args.Player.SendErrorMessage("值必须是 0 或 1。"); return; }
        }

        string field = notifyType == 0 ? "NotifyEnter" : "NotifyBreakPlace";
        string label = notifyType == 0 ? "进入通知" : "破坏通知";

        if (HouseManager.UpdateNotify(house.Name, field, val))
        {
            if (notifyType == 0) house.NotifyEnter = val;
            else house.NotifyBreakPlace = val;
            args.Player.SendSuccessMessage($"房屋 {house.Name} 的{label}已设置为 {(val == 1 ? "开" : "关")}。");
        }
        else { args.Player.SendErrorMessage("设置失败。"); }
    }

    // ── 边框显示 ──
    // ── 房屋权限设置：/h [屋名] [项目名] [0/1] ──

    private static readonly Dictionary<string, string> PermissionFieldMap = new()
    {
        {"允许进入", "AllowEntry"},
        {"允许传送", "AllowTP"},
        {"放置", "AllowPlace"},
        {"破坏", "AllowBreak"},
        {"液体", "AllowLiquid"},
        {"箱子", "AllowChest"},
        {"植物", "AllowPlant"},
        {"设置复活点", "AllowSpawn"},
        {"挖坟", "AllowGrave"},
        {"开关", "AllowSwitch"},
        {"门", "AllowDoor"},
        {"易碎品", "AllowFragile"},
        {"违规驱离", "ExpelOnViolate"},
    };

    private void TryHandlePermission(CommandArgs args, string firstParam)
    {
        // /house [屋名] 项目名 0/1  或  /house 项目名 0/1
        House? house;
        string permName;
        int valIndex;

        var maybeHouse = Utils.GetHouseByName(firstParam);
        if (maybeHouse != null && args.Parameters.Count >= 3)
        {
            // 第一参数是屋名
            house = maybeHouse;
            permName = args.Parameters[1];
            valIndex = 2;
        }
        else if (PermissionFieldMap.ContainsKey(firstParam) && args.Parameters.Count >= 2)
        {
            // 第一参数是项目名
            house = Utils.CurrentHouse(args.Player);
            permName = firstParam;
            valIndex = 1;
        }
        else
        {
            // 可能的第一参数被当作屋名但找不到，回退为当前房屋
            house = Utils.CurrentHouse(args.Player);
            permName = firstParam;
            valIndex = 1;
        }

        if (house == null)
        {
            args.Player.SendErrorMessage("未找到房屋。输入 /house help 查看帮助。");
            return;
        }

        if (!PermissionFieldMap.TryGetValue(permName, out var field))
        {
            args.Player.SendErrorMessage($"未知的项目名: {permName}");
            return;
        }

        if (house.Author != args.Player.Account.ID.ToString() &&
            !Utils.OwnsHouse(args.Player.Account.ID.ToString(), house) &&
            !args.Player.Group.HasPermission(GetDataHandlers.AdminHouse))
        {
            args.Player.SendErrorMessage("你没有权力修改这个房子的设置!");
            return;
        }

        if (args.Parameters.Count <= valIndex || !int.TryParse(args.Parameters[valIndex], out var val) || val < 0 || val > 1)
        {
            args.Player.SendErrorMessage("请指定值为 0 或 1。");
            return;
        }

        if (HouseManager.UpdatePermission(house.Name, field, val))
        {
            // 更新内存中的值
            var prop = typeof(House).GetProperty(field);
            if (prop != null) prop.SetValue(house, val);
            args.Player.SendSuccessMessage($"房屋 {house.Name} 的「{permName}」已设置为 {(val == 1 ? "开" : "关")}。");
        }
        else
        {
            args.Player.SendErrorMessage("设置失败。");
        }
    }

    // ── 辅助方法 ──

    /// <summary>
    /// 解析可变参数：屋名可选，坐标可选。缺省屋名用当前房屋，缺省坐标用玩家位置。
    /// </summary>
    private void ParseHouseWithCoords(CommandArgs args, out House? house, out int px, out int py)
    {
        house = null;
        px = args.Player.TileX;
        py = args.Player.TileY;

        if (args.Parameters.Count <= 1)
        {
            house = Utils.CurrentHouse(args.Player);
            return;
        }

        // 参数顺序: /house settp [屋名] [x] [y]
        // 尝试将 args.Parameters[1] 解析为屋名
        var maybeHouse = Utils.GetHouseByName(args.Parameters[1]);
        int coordStart;
        if (maybeHouse != null)
        {
            house = maybeHouse;
            coordStart = 2;
        }
        else
        {
            house = Utils.CurrentHouse(args.Player);
            coordStart = 1;
        }

        if (args.Parameters.Count > coordStart && int.TryParse(args.Parameters[coordStart], out var x))
            px = x;
        if (args.Parameters.Count > coordStart + 1 && int.TryParse(args.Parameters[coordStart + 1], out var y))
            py = y;
    }
}
