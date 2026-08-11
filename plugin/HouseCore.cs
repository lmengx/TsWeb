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

public class HouseCore
{
    public static HouseCore Instance { get; } = new();

    public static LPlayer?[] LPlayers { get; set; } = new LPlayer[256];
    public static List<House> Houses = new();
    static readonly System.Timers.Timer Update = new(1100);
    public static bool ULock = false;
    private static TSPlayer? _explosionOwner = null;
    private static bool _hooksRegistered;
    private TerrariaPlugin? _plugin;

    // 命令缓存（用于热重载时准确移除）
    private Command? _houseCmd, _hCmd, _htpCmd;

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

    public void Initialize(TerrariaPlugin plugin)
    {
        _plugin = plugin;
        // 热重载：重置静态状态
        GetDataHandlers.ResetState();
        GetDataHandlers.InitGetDataHandler();
        _explosionOwner = null;

        Config.Load();
        Database.EnsureTable();

        // 命令（缓存引用以便热重载时准确移除）
        _houseCmd = new Command("house.use", HCommands, "house")
        {
            HelpText = "输入/h help 可以显示与房子相关的操作提示。"
        };
        _hCmd = new Command("house.use", HCommands, "h")
        {
            HelpText = "输入/h 可以显示与房子相关的操作提示。"
        };
        _htpCmd = new Command("", HandleHtp, "htp")
        {
            HelpText = "传送到指定房屋: /htp <屋名>"
        };
        Commands.ChatCommands.Add(_houseCmd);
        Commands.ChatCommands.Add(_hCmd);
        Commands.ChatCommands.Add(_htpCmd);

        ServerApi.Hooks.NetGreetPlayer.Register(plugin, OnGreetPlayer);
        ServerApi.Hooks.ServerLeave.Register(plugin, OnLeave);
        ServerApi.Hooks.GamePostInitialize.Register(plugin, PostInitialize);
        if (!Main.gameMenu)
            PostInitialize(EventArgs.Empty);
        OTAPI.Hooks.Chest.QuickStack += ChestOnQuickStack;
        CraftingRequests.CanCraftFromChest += CraftingRequestsOnCanCraftFromChest;
        Hooks.MessageBuffer.InvokeGetData -= MessageBufferOnInvokeGetData;
        Hooks.MessageBuffer.InvokeGetData += MessageBufferOnInvokeGetData;
        On.Terraria.Projectile.Kill -= OnProjectileKill;
        On.Terraria.Projectile.Kill += OnProjectileKill;
        On.Terraria.WorldGen.KillTile -= OnWorldGenKillTile;
        On.Terraria.WorldGen.KillTile += OnWorldGenKillTile;
        _hooksRegistered = true;
    }

    public void Dispose()
    {
        // 命令（用缓存引用准确移除）
        if (_houseCmd != null) Commands.ChatCommands.Remove(_houseCmd);
        if (_hCmd != null) Commands.ChatCommands.Remove(_hCmd);
        if (_htpCmd != null) Commands.ChatCommands.Remove(_htpCmd);

        ServerApi.Hooks.NetGreetPlayer.Deregister(_plugin!, OnGreetPlayer);
        ServerApi.Hooks.ServerLeave.Deregister(_plugin!, OnLeave);
        ServerApi.Hooks.GamePostInitialize.Deregister(_plugin!, PostInitialize);
        Update.Elapsed -= OnUpdate;
        Update.Stop();

        // OTAPI/MonoMod 钩子（先减后加由 Initialize 保证，Dispose 中无需重复 -= ）
        // 但主动减一次也无害，保留以兼容非热重载的正常卸载
        OTAPI.Hooks.Chest.QuickStack -= ChestOnQuickStack;
        CraftingRequests.CanCraftFromChest -= CraftingRequestsOnCanCraftFromChest;
        Hooks.MessageBuffer.InvokeGetData -= MessageBufferOnInvokeGetData;
        On.Terraria.Projectile.Kill -= OnProjectileKill;
        On.Terraria.WorldGen.KillTile -= OnWorldGenKillTile;
        _hooksRegistered = false;
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

        var cmd = args.Parameters.Count > 0 ? args.Parameters[0].ToLower() : "";

        // 无参数：引导行，完整帮助见 /h help
        if (cmd.Length == 0)
        {
            args.Player.SendMessage("输入 /h help 查看完整帮助提示", Color.Lime);
            args.Player.SendMessage("/h c 圈地  |  /h set 查看设置  |  /htp 屋名 传送", Color.Lime);
            return;
        }

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

            case "addowner":
                HandleAddOwner(args);
                break;

            case "delowner":
                HandleDelOwner(args);
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

            case "传送点":
                HandleSetTP(args);
                break;

            case "驱离点":
                HandleSetExpel(args);
                break;

            case "editmsg":
                HandleEditMsg(args);
                break;

            case "settings":
            case "set":
                HandleSettings(args);
                break;

            case "showme":
                HandleShowMe(args);
                break;

            case "showothers":
                HandleShowOthers(args);
                break;

            case "export":
                HandleExport(args);
                break;

            case "import":
                HandleImport(args);
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
            var hexColors = new[] { "FFD700", "00FFFF", "AA66FF", "7CFC00", "FFA500", "FF69B4", "87CEEB", "90EE90" };
            var msg = "你的房屋:";
            for (int i = 0; i < myHouses.Count && i < hexColors.Length; i++)
                msg += $" [c/{hexColors[i]}:{myHouses[i].Name}]";
            args.Player.SendMessage(msg, Color.White);
        }
        else
        {
            args.Player.SendMessage("你还没有房屋，使用 /h c 创建一个吧", Color.Gray);
        }

        args.Player.SendMessage("━━━ 房屋操作 ━━━", Color.Gold);
        args.Player.SendMessage("/h c 圈地  |  /h set 查看设置  |  /htp 屋名 传送", Color.Lime);
        args.Player.SendMessage("/h delete [屋名] 删除房屋    /h redefine [屋名] 重新定义范围", Color.Lime);
        args.Player.SendMessage("/h list [页码] 查看房屋列表    /h info [屋名] 查看房屋信息    /h name 敲击查询归属", Color.Lime);

        args.Player.SendMessage("━━━ 边框显示 ━━━", Color.Gold);
        var pid = args.Player.Account.ID.ToString();
        var showMe = ShowPrefManager.GetShowMe(pid);
        var showOthers = ShowPrefManager.GetShowOthers(pid);
        var cMe = showMe ? "7CFC00" : "FFA500";
        var cOthers = showOthers ? "7CFC00" : "FFA500";
        args.Player.SendMessage(
            $"[c/{cMe}:自己房屋边框 {(showMe ? "开" : "关")}] /h showme 切换    " +
            $"[c/{cOthers}:他人房屋边框 {(showOthers ? "开" : "关")}] /h showothers 切换",
            Color.Lime);

        if (args.Player.Group.HasPermission(GetDataHandlers.AdminHouse))
        {
            args.Player.SendMessage("━━━ 管理员 ━━━", Color.Gold);
            args.Player.SendMessage("/h export [屋名] ——导出房屋区域建筑为 .tsb（管理员）", Color.Aqua);
            args.Player.SendMessage("/h import <文件名> ——导入 .tsb 建筑（以你为中心粘贴，管理员）", Color.Aqua);
        }
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
            args.Player.SendMessage("请站在房屋内查看当前房屋设置", Color.Yellow);
            return;
        }

        bool canEdit = house.Author == args.Player.Account.ID.ToString() ||
                       Utils.OwnsHouse(args.Player.Account.ID.ToString(), house) ||
                       args.Player.Group.HasPermission(GetDataHandlers.AdminHouse);

        ShowSettingsPanel(args.Player, house, canEdit);
    }

    private static void ShowSettingsPanel(TSPlayer plr, House house, bool canEdit)
    {
        var green = Color.Lime;

        plr.SendMessage($"━━━ {house.Name} ━━━", green);
        var authorName = "?";
        try { authorName = TShock.UserAccounts.GetUserAccountByID(Convert.ToInt32(house.Author)).Name; } catch { }
        plr.SendMessage($"房主: {authorName}    区域: {house.HouseArea.Width}×{house.HouseArea.Height}    传送点: ({house.TpX},{house.TpY})", green);

        // 通知
        string NotifyItem(string label, int val)
        {
            var c = val == 1 ? "7CFC00" : "FFA500";
            return $"[c/{c}:{label} {(val == 1 ? "开" : "关")}]";
        }
        plr.SendMessage(
            "◇ 通知设置  " + NotifyItem("破坏通知", house.NotifyBreakPlace) + "    " + NotifyItem("进入通知", house.NotifyEnter),
            green);

        // 权限
        string PermItem(string label, int val)
        {
            var c = val == 1 ? "7CFC00" : "FFA500";
            return $"[c/{c}:{label} {(val == 1 ? "✓" : "✗")}]";
        }
        plr.SendMessage("◇ 权限设置", green);
        plr.SendMessage(
            PermItem("进入", house.AllowEntry) + "    " + PermItem("传送", house.AllowTP), green);
        plr.SendMessage(
            PermItem("放置", house.AllowPlace) + "    " + PermItem("破坏", house.AllowBreak) + "    " + PermItem("液体", house.AllowLiquid) +
            "    " + PermItem("箱子", house.AllowChest) + "    " + PermItem("开关", house.AllowSwitch) + "    " + PermItem("门", house.AllowDoor), green);
        plr.SendMessage(
            PermItem("植物", house.AllowPlant) + "    " + PermItem("易碎品", house.AllowFragile) + "    " + PermItem("挖坟", house.AllowGrave) + "    " + PermItem("复活点", house.AllowSpawn), green);
        plr.SendMessage(
            PermItem("违规驱离", house.ExpelOnViolate), green);

        // 传送点 / 驱离点
        string LocItem(string label, string value, bool hasValue)
        {
            var c = hasValue ? "7CFC00" : "FFA500";
            return $"[c/{c}:{label} {value}]";
        }
        var hasExpel = house.ExpelX.HasValue && house.ExpelY.HasValue;
        plr.SendMessage(
            LocItem("传送点", $"({house.TpX},{house.TpY})（使用 /h 传送点 设置当前位置为房屋传送点）", true) + "    " +
            LocItem("驱离点", hasExpel
                ? $"({house.ExpelX},{house.ExpelY})（在房屋外且附近100格设置为驱离点）"
                : "未设置（在房屋外且附近100格设置为驱离点）", hasExpel),
            green);

        // 使用说明（授权提示上方）
        plr.SendMessage("[c/7CFC00:使用：/h 配置名 0/1] [c/FFA500:修改] [c/7CFC00:例如 /h 箱子 0（站在房屋内操作）]", green);

        // 授权信息
        string NamesFromIds(IEnumerable<string> ids)
        {
            var names = new List<string>();
            foreach (var id in ids)
            {
                try { var u = TShock.UserAccounts.GetUserAccountByID(Convert.ToInt32(id)); if (u != null) names.Add(u.Name); } catch { }
            }
            return names.Count > 0 ? string.Join("、", names) : "无";
        }
        plr.SendMessage(
            "当前共有者：" + NamesFromIds(house.Owners) + "    当前使用者：" + NamesFromIds(house.Users) +
            "    授权他人使用：/h addowner <名字> 或 /h adduser <名字>",
            Color.Gold);

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

    // ── 导出（管理员）──

    private void HandleExport(CommandArgs args)
    {
        if (!args.Player.Group.HasPermission(GetDataHandlers.AdminHouse))
        {
            args.Player.SendErrorMessage("你没有权限使用房屋导出功能。");
            return;
        }

        House? house;
        if (args.Parameters.Count > 1)
            house = Utils.GetHouseByName(string.Join(" ", args.Parameters.GetRange(1, args.Parameters.Count - 1)));
        else
            house = Utils.CurrentHouse(args.Player);

        if (house == null)
        {
            args.Player.SendErrorMessage("未找到要导出的房屋。用法: /h export [屋名]（不写屋名则导出当前所在房屋）");
            return;
        }

        HouseExporter.Export(args.Player, house);
    }

    // ── 导入（管理员）──

    private void HandleImport(CommandArgs args)
    {
        if (!args.Player.Group.HasPermission(GetDataHandlers.AdminHouse))
        {
            args.Player.SendErrorMessage("你没有权限使用房屋导入功能。");
            return;
        }

        // 无参数：列出可用文件
        if (args.Parameters.Count <= 1)
        {
            var files = HouseImporter.ListFiles();
            if (files.Count == 0)
            {
                args.Player.SendErrorMessage($"建筑目录中没有 .tsb 文件（目录: {Path.Combine(TShock.SavePath, "TSWeb", "Buildings")}）。");
                return;
            }
            args.Player.SendMessage($"可用建筑文件 ({files.Count}):", Color.Gold);
            foreach (var f in files)
                args.Player.SendMessage(f, Color.Yellow);
            args.Player.SendMessage("/h import <文件名> ——导入建筑（以你所在位置为中心粘贴）", Color.Lime);
            return;
        }

        var fileName = string.Join(" ", args.Parameters.GetRange(1, args.Parameters.Count - 1));
        HouseImporter.Import(args.Player, fileName);
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
            args.Player.SendMessage("站进房屋用 /h set 查看/修改设置，/h addowner <名字> 可邀请共有者", Color.Lime);
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
        House? house;
        if (args.Parameters.Count > 1)
            house = Utils.GetHouseByName(string.Join(" ", args.Parameters.GetRange(1, args.Parameters.Count - 1)));
        else
            house = Utils.CurrentHouse(args.Player);
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
        if (args.Player.TempPoints.Any(p => p == Point.Zero))
        {
            args.Player.SendErrorMessage("未设置完整的房屋点,建议先使用指令: /house help");
            return;
        }

        House? house;
        if (args.Parameters.Count > 1)
            house = Utils.GetHouseByName(string.Join(" ", args.Parameters.GetRange(1, args.Parameters.Count - 1)));
        else
            house = Utils.CurrentHouse(args.Player);
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

        if (HouseManager.RedefineHouse(x, y, width, height, house.Name))
        {
            args.Player.SendMessage("重新定义了房子 " + house.Name, Color.Yellow);
            TShock.Log.ConsoleInfo("{0} 重新定义的房子: {1}", args.Player.Account.Name, house.Name);
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

    private void HandleAddOwner(CommandArgs args)
    {
        if (args.Parameters.Count <= 1)
        { args.Player.SendErrorMessage("语法错误! 正确语法: /house addowner [名字] [屋名]"); return; }
        var playerName = args.Parameters[1];
        House? house;
        if (args.Parameters.Count > 2)
            house = Utils.GetHouseByName(string.Join(" ", args.Parameters.GetRange(2, args.Parameters.Count - 2)));
        else
            house = Utils.CurrentHouse(args.Player);
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

    private void HandleDelOwner(CommandArgs args)
    {
        if (args.Parameters.Count <= 1)
        { args.Player.SendErrorMessage("语法错误! 正确语法: /house delowner [名字] [屋名]"); return; }
        var playerName = args.Parameters[1];
        House? house;
        if (args.Parameters.Count > 2)
            house = Utils.GetHouseByName(string.Join(" ", args.Parameters.GetRange(2, args.Parameters.Count - 2)));
        else
            house = Utils.CurrentHouse(args.Player);
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
        if (args.Parameters.Count <= 1)
        { args.Player.SendErrorMessage("语法错误! 正确语法: /house adduser [名字] [屋名]"); return; }
        var playerName = args.Parameters[1];
        House? house;
        if (args.Parameters.Count > 2)
            house = Utils.GetHouseByName(string.Join(" ", args.Parameters.GetRange(2, args.Parameters.Count - 2)));
        else
            house = Utils.CurrentHouse(args.Player);
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
        if (args.Parameters.Count <= 1)
        { args.Player.SendErrorMessage("语法错误! 正确语法: /house deluser [名字] [屋名]"); return; }
        var playerName = args.Parameters[1];
        House? house;
        if (args.Parameters.Count > 2)
            house = Utils.GetHouseByName(string.Join(" ", args.Parameters.GetRange(2, args.Parameters.Count - 2)));
        else
            house = Utils.CurrentHouse(args.Player);
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

    // ── 新增指令：tp / 传送点 / 驱离点 / editmsg ──

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

    private void HandleHtp(CommandArgs args)
    {
        if (args.Parameters.Count < 1)
        { args.Player.SendErrorMessage("用法: /htp <屋名>"); return; }
        var name = string.Join(" ", args.Parameters);
        var house = Utils.GetHouseByName(name);
        if (house == null) { args.Player.SendErrorMessage("没有找到这个房子!"); return; }
        if (!Utils.IsAuthorized(args.Player, house) && house.AllowTP == 0)
        { args.Player.SendErrorMessage("你没有权力传送这个房子!"); return; }
        args.Player.Teleport(house.TpX * 16, house.TpY * 16);
        args.Player.SendSuccessMessage("已将你传送到房屋: " + house.Name);
    }

    private void HandleSetTP(CommandArgs args)
    {
        House? house;
        if (args.Parameters.Count > 1)
            house = Utils.GetHouseByName(string.Join(" ", args.Parameters.GetRange(1, args.Parameters.Count - 1)));
        else
            house = Utils.CurrentHouse(args.Player);
        if (house == null) { args.Player.SendErrorMessage("未找到房屋。用法: /h 传送点 [屋名]（缺省当前所在房屋）"); return; }

        if (house.Author != args.Player.Account.ID.ToString() &&
            !Utils.OwnsHouse(args.Player.Account.ID.ToString(), house) &&
            !args.Player.Group.HasPermission(GetDataHandlers.AdminHouse))
        { args.Player.SendErrorMessage("你没有权力修改这个房子!"); return; }

        // 以玩家当前位置作为传送点，必须在房屋内
        var px = args.Player.TileX;
        var py = args.Player.TileY;
        if (!house.HouseArea.Contains(px, py))
        { args.Player.SendErrorMessage($"传送点必须在房屋 {house.Name} 的矩形范围内! 当前位置 ({px},{py}) 不在其中。"); return; }

        if (HouseManager.UpdateTP(house.Name, px, py))
        {
            house.TpX = px; house.TpY = py;
            args.Player.SendSuccessMessage($"房屋 {house.Name} 的传送点已设置为当前位置 ({px}, {py})。");
        }
        else { args.Player.SendErrorMessage("设置传送点失败。"); }
    }

    private void HandleSetExpel(CommandArgs args)
    {
        House? house;
        if (args.Parameters.Count > 1)
            house = Utils.GetHouseByName(string.Join(" ", args.Parameters.GetRange(1, args.Parameters.Count - 1)));
        else
            house = FindNearestHouse(args.Player);
        if (house == null) { args.Player.SendErrorMessage("未找到房屋。用法: /h 驱离点 [屋名]（缺省选择离你最近的房屋）"); return; }

        if (house.Author != args.Player.Account.ID.ToString() &&
            !Utils.OwnsHouse(args.Player.Account.ID.ToString(), house) &&
            !args.Player.Group.HasPermission(GetDataHandlers.AdminHouse))
        { args.Player.SendErrorMessage("你没有权力修改这个房子!"); return; }

        // 以玩家当前位置作为驱离点，必须在房屋外且距边界 ≤100 格
        var px = args.Player.TileX;
        var py = args.Player.TileY;
        if (house.HouseArea.Contains(px, py))
        { args.Player.SendErrorMessage($"驱离点必须在房屋 {house.Name} 的矩形范围外!"); return; }

        var dist = Utils.DistanceToRect(new Point(px, py), house.HouseArea);
        if (dist > 100)
        { args.Player.SendErrorMessage($"驱离点距离房屋边界 {dist} 格，不能超过 100 格!"); return; }

        if (HouseManager.UpdateExpel(house.Name, px, py))
        {
            house.ExpelX = px; house.ExpelY = py;
            args.Player.SendSuccessMessage($"房屋 {house.Name} 的驱离点已设置为当前位置 ({px}, {py})。");
        }
        else { args.Player.SendErrorMessage("设置驱离点失败。"); }
    }

    /// <summary>选择离玩家最近的房屋（驱离点缺省屋名时使用）</summary>
    private static House? FindNearestHouse(TSPlayer ply)
    {
        House? best = null;
        var bestDist = int.MaxValue;
        var pos = new Point(ply.TileX, ply.TileY);
        for (var i = 0; i < HouseCore.Houses.Count; i++)
        {
            var h = HouseCore.Houses[i];
            if (h == null) continue;
            var d = Utils.DistanceToRect(pos, h.HouseArea);
            if (d < bestDist)
            {
                bestDist = d;
                best = h;
            }
        }
        return best;
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
        {"进入", "AllowEntry"},
        {"传送", "AllowTP"},
        {"放置", "AllowPlace"},
        {"破坏", "AllowBreak"},
        {"液体", "AllowLiquid"},
        {"箱子", "AllowChest"},
        {"植物", "AllowPlant"},
        {"复活点", "AllowSpawn"},
        {"挖坟", "AllowGrave"},
        {"开关", "AllowSwitch"},
        {"门", "AllowDoor"},
        {"易碎品", "AllowFragile"},
        {"违规驱离", "ExpelOnViolate"},
        {"破坏通知", "NotifyBreakPlace"},
        {"进入通知", "NotifyEnter"},
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
    // （原 ParseHouseWithCoords 已移除：传送点/驱离点改为以玩家当前位置设置）
}
