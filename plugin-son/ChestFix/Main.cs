using System.IO.Streams;
using System.Text;
using Terraria;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;

namespace ChestFix;

[ApiVersion(2, 1)]
public class ChestFix : TerrariaPlugin
{
    public override string Name => "ChestFix";
    public override Version Version => new Version(1, 0, 0);
    public override string Author => "TsWeb";
    public override string Description => "修复TShock宝箱数据包校验缺失漏洞";

    private const string Ver = "1.0.0";
    private static int _blockedCount;

    public ChestFix(Main game) : base(game) { }

    public override void Initialize()
    {
        // ========== 修复1: ChestItem 包校验 (通过公开事件) ==========
        GetDataHandlers.ChestItemChange += OnChestItemChange;

        // ========== 修复2: ChestOpen/ChestGetContents 包拦截 (NetGetData 最高优先级) ==========
        ServerApi.Hooks.NetGetData.Register(this, OnNetGetData, -1000);

        // ========== 注册指令 ==========
        Commands.ChatCommands.Add(new Command("chestfix.admin", ChestFixCommand, "chestfix", "cstf"));

        TShock.Log.ConsoleInfo("[ChestFix] 宝箱安全修复已加载");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            GetDataHandlers.ChestItemChange -= OnChestItemChange;
            ServerApi.Hooks.NetGetData.Deregister(this, OnNetGetData);
            Commands.ChatCommands.RemoveAll(c => c.CommandDelegate == ChestFixCommand);
        }
        base.Dispose(disposing);
    }

    // ===================================================================
    // 修复1: ChestItem 包校验（通过公开事件）
    // ===================================================================
    private static void OnChestItemChange(object? sender, GetDataHandlers.ChestItemEventArgs e)
    {
        // --- 1.1 Chest ID 范围 ---
        if (e.ID < 0 || e.ID >= Main.chest.Length)
        {
            Block($"越界宝箱ID {e.ID}", e.Player);
            e.Handled = true;
            return;
        }

        // --- 1.2 Chest 存在性 ---
        if (Main.chest[e.ID] == null)
        {
            Block($"空宝箱引用 ID={e.ID}", e.Player);
            e.Handled = true;
            return;
        }

        // --- 1.3 Slot 边界 (高危!) ---
        if (e.Slot < 0 || e.Slot >= Main.chest[e.ID].maxItems)
        {
            Block($"越界槽位 slot={e.Slot}/max={Main.chest[e.ID].maxItems}", e.Player);
            e.Handled = true;
            return;
        }

        // --- 1.4 Item Type 有效范围 ---
        if (e.Type < 0 || e.Type >= ItemID.Count)
        {
            Block($"无效物品ID type={e.Type}", e.Player);
            e.Handled = true;
            return;
        }

        // --- 1.5 Prefix 非负 ---
        if (e.Prefix < 0)
        {
            Block($"无效词缀 prefix={e.Prefix}", e.Player);
            e.Handled = true;
            return;
        }

        // --- 1.6 Stacks 非负（0=清空该槽位，合法）---
        if (e.Stacks < 0)
        {
            Block($"负堆叠 stacks={e.Stacks}", e.Player);
            e.Handled = true;
            return;
        }

        // 全部校验通过 → 正常操作
        Pass($"ChestItem: id={e.ID} slot={e.Slot} type={e.Type} stack={e.Stacks} prefix={e.Prefix}", e.Player);
    }

    // ===================================================================
    // 修复2: 在 TShock 处理之前拦截 ChestOpen/ChestGetContents 包
    // ===================================================================
    private static void OnNetGetData(GetDataEventArgs e)
    {
        if (e.MsgID == PacketTypes.ChestOpen)
            HandleChestOpenPacket(e);
        else if (e.MsgID == PacketTypes.ChestGetContents)
            HandleChestGetContentsPacket(e);
    }

    private static void HandleChestOpenPacket(GetDataEventArgs e)
    {
        var plr = TShock.Players[e.Msg.whoAmI];
        if (plr == null || !plr.Active)
            return;

        using var ms = new MemoryStream(e.Msg.readBuffer, e.Index, Math.Max(e.Length - 1, 0));
        try
        {
            var id = ms.ReadInt16();

            // id=-1 关闭宝箱，合法放行
            if (id == -1)
            {
                Pass("ChestOpen: 关闭宝箱 id=-1", plr);
                return;
            }

            // 其它容器
            if (id<0 && id>=- 5)
            {
                Pass($"ChestOpen: id={id}", plr);
                return;
            }

            ms.ReadInt16(); // x
            ms.ReadInt16(); // y

            if (id < 0 || id >= Main.chest.Length || Main.chest[id] == null)
            {
                Block($"ChestOpen 无效宝箱ID={id}", plr);
                e.Handled = true;
                plr.SendData(PacketTypes.ChestOpen, "", -1);
                return;
            }

            Pass($"ChestOpen: id={id}", plr);
        }
        catch { }
    }

    private static void HandleChestGetContentsPacket(GetDataEventArgs e)
    {
        var plr = TShock.Players[e.Msg.whoAmI];
        if (plr == null || !plr.Active)
            return;

        using var ms = new MemoryStream(e.Msg.readBuffer, e.Index, Math.Max(e.Length - 1, 0));
        try
        {
            var x = ms.ReadInt16();
            var y = ms.ReadInt16();

            var chestId = Chest.FindChest(x, y);
            if (chestId < 0 || chestId >= Main.chest.Length)
            {
                Block($"ChestGetContents 无效坐标 ({x},{y})", plr);
                e.Handled = true;
                return;
            }

            if (Main.chest[chestId] == null)
            {
                Block($"ChestGetContents 空宝箱 ({x},{y})", plr);
                e.Handled = true;
                return;
            }

                Pass($"", plr);
        }
        catch { }
    }

    // ===================================================================
    // 指令: /chestfix  /cstf
    // ===================================================================
    private static void ChestFixCommand(CommandArgs args)
    {
        var cmd = args.Parameters.Count > 0 ? args.Parameters[0].ToLower() : "";

        switch (cmd)
        {
            case "reset":
                _blockedCount = 0;
                args.Player.SendInfoMessage("[ChestFix] 计数器已重置");
                break;

            case "scan":
                ScanChests(args);
                break;

            default:
                // 默认: 显示状态
                var sb = new StringBuilder();
                sb.AppendLine($"[ChestFix] 宝箱安全修复 v{Ver}");
                sb.AppendLine($"  已拦截恶意包: {_blockedCount}");
                sb.AppendLine($"  用法:");
                sb.AppendLine($"    /chestfix           - 显示状态");
                sb.AppendLine($"    /chestfix reset     - 重置计数器");
                sb.AppendLine($"    /chestfix scan      - 扫描世界宝箱脏数据");
                sb.AppendLine($"    /chestfix scan fix  - 扫描并自动修复");
                args.Player.SendInfoMessage(sb.ToString().TrimEnd());
                break;
        }
    }

    // ===================================================================
    // 扫描与修复
    // ===================================================================
    private static void ScanChests(CommandArgs args)
    {
        var fix = args.Parameters.Count > 1 && args.Parameters[1].ToLower() == "fix";

        args.Player.SendInfoMessage($"[ChestFix] 正在扫描...{(fix ? "" : "(预览模式，加 fix 修复)")}");

        int totalChests = 0, orphanedChests = 0, dirtySlots = 0, fixedSlots = 0, fixedOrphans = 0;
        int abnormalLen = 0, fixedLen = 0;
        int duplicatePos = 0, fixedDup = 0;
        var seenCoords = new HashSet<(int, int)>();
        var report = new StringBuilder();

        for (int i = 0; i < Main.chest.Length; i++)
        {
            var chest = Main.chest[i];
            if (chest == null) continue;
            totalChests++;

            // --- 检测一: 孤悬宝箱（方块已被拆, 但 Main.chest[] 残留数据）---
            bool chestBlockExists = ChestBlockExists(chest.x, chest.y);
            if (!chestBlockExists)
            {
                report.AppendLine($"  [孤悬] 宝箱[{i}] 坐标({chest.x},{chest.y}) 方块已不存在");
                orphanedChests++;
                if (fix)
                {
                    Main.chest[i] = null;
                    fixedOrphans++;
                }
                continue; // 宝箱都没了, 不扫 item
            }

            // --- 检测一.5: 同坐标重复宝箱 ---
            var coord = (chest.x, chest.y);
            if (!seenCoords.Add(coord))
            {
                report.AppendLine($"  [重复] 宝箱[{i}] 坐标({chest.x},{chest.y}) 与前面的宝箱位置相同");
                duplicatePos++;
                if (fix)
                {
                    Main.chest[i] = null;
                    fixedDup++;
                }
                continue;
            }

            // --- 检测二: 数组长度异常（>40 说明被恶意扩展过）---
            if (chest.item.Length > 40)
            {
                report.AppendLine($"  [异常] 宝箱[{i}] 坐标({chest.x},{chest.y}) item数组长度={chest.item.Length}, 标准最大=40");
                abnormalLen++;
                if (fix)
                {
                    var oldItems = chest.item;
                    chest.maxItems = 40;
                    chest.item = new Item[40];
                    for (int s = 0; s < 40 && s < oldItems.Length; s++)
                        chest.item[s] = oldItems[s] ?? new Item();
                    fixedLen++;
                }
            }

            // --- 检测三: 每个物品槽 ---
            for (int s = 0; s < chest.item.Length; s++)
            {
                var item = chest.item[s];
                if (item == null || item.type == 0) continue; // 空气, 跳过

                var issues = new List<string>();

                // 2a. 物品类型
                if (item.type < 0 || item.type >= ItemID.Count)
                    issues.Add($"type={item.type} 超范围[0,{ItemID.Count})");

                // 2b. 堆叠数量
                var def = new Item();
                def.netDefaults(item.type);
                if (item.stack < 0 || item.stack > def.maxStack)
                    issues.Add($"stack={item.stack}/{def.maxStack} 超限");

                // 2c. 词缀
                if (item.prefix < 0)
                    issues.Add($"prefix={item.prefix} 为负数");

                if (issues.Count == 0) continue;

                dirtySlots++;
                report.AppendLine($"  [脏数据] 宝箱[{i}] slot={s} {string.Join(", ", issues)}");

                if (fix)
                {
                    chest.item[s] = new Item();
                    fixedSlots++;
                }
            }
        }

        // --- 输出结果 ---
        var result = new StringBuilder();
        result.AppendLine($"[ChestFix] 扫描完成: 共扫描 {totalChests} 个宝箱");

        if (totalChests == 0)
        {
            result.Append("  世界中没有任何宝箱");
        }
        else if (orphanedChests == 0 && dirtySlots == 0 && abnormalLen == 0 && duplicatePos == 0)
        {
            result.Append("  未发现异常数据 ✓");
        }
        else
        {
            if (orphanedChests > 0)
                result.AppendLine($"  发现 {orphanedChests} 个孤悬宝箱" +
                    (fix ? $" (已清理 {fixedOrphans} 个)" : ""));
            if (duplicatePos > 0)
                result.AppendLine($"  发现 {duplicatePos} 个重复坐标宝箱" +
                    (fix ? $" (已移除 {fixedDup} 个)" : ""));
            if (abnormalLen > 0)
                result.AppendLine($"  发现 {abnormalLen} 个数组长度异常宝箱" +
                    (fix ? $" (已截断 {fixedLen} 个)" : ""));
            if (dirtySlots > 0)
                result.AppendLine($"  发现 {dirtySlots} 个脏数据槽位" +
                    (fix ? $" (已修复 {fixedSlots} 个)" : ""));
        }

        var msg = result.ToString().TrimEnd();
        args.Player.SendInfoMessage(msg);
        TShock.Log.ConsoleInfo(msg);

        if (fix && (fixedOrphans > 0 || fixedLen > 0 || fixedSlots > 0 || fixedDup > 0))
        {
            args.Player.SendInfoMessage("[ChestFix] 修复已写入内存, 重新打开宝箱即可看到效果");
            TShock.Log.ConsoleInfo($"[ChestFix] 内存修复: 清理 {fixedOrphans} 孤悬, 移除 {fixedDup} 重复, 截断 {fixedLen} 长度异常, 修复 {fixedSlots} 脏槽位");
        }
    }

    /// <summary>检测坐标上是否存在宝箱/梳妆台方块</summary>
    private static bool ChestBlockExists(int x, int y)
    {
        if (x < 0 || x >= Main.maxTilesX || y < 0 || y >= Main.maxTilesY)
            return false;

        var tile = Main.tile[x, y];
        if (tile == null || !tile.active())
            return false;

        int type = tile.type;
        return type == TileID.Containers   // 普通宝箱 (原版+模组)
            || type == TileID.Containers2  // 宝箱2 (黄金/冰/etc)
            || type == TileID.Dressers;    // 梳妆台
    }

    /// <summary>强制同步宝箱全部40个槽位到所有在线玩家</summary>
    private static void ForceSyncChest(int chestId)
    {
        var chest = Main.chest[chestId];
        if (chest == null || chest.item == null) return;

        // 遍历所有在线玩家, 对其发送 ChestItem + ChestOpen
        foreach (var plr in TShock.Players)
        {
            if (plr == null || !plr.Active) continue;

            for (int s = 0; s < chest.item.Length; s++)
            {
                var it = chest.item[s];
                plr.SendRawData(ConstructChestItemPacket(chestId, s, it));
            }
            plr.SendData(PacketTypes.ChestOpen, "", chestId);
        }
        TShock.Log.ConsoleDebug($"[ChestFix] 强制同步宝箱[{chestId}] 完成");
    }

    /// <summary>构造 ChestItem 网络包</summary>
    private static byte[] ConstructChestItemPacket(int chestId, int slot, Item item)
    {
        using var ms = new MemoryStream();
        var pw = new OTAPI.PacketWriter(ms);
        pw.BaseStream.Position = 0L;
        var pos = pw.BaseStream.Position;
        pw.BaseStream.Position += 2L;                        // 跳过长度头
        pw.Write((byte)PacketTypes.ChestItem);               // 包类型
        pw.Write((short)chestId);                            // 宝箱ID
        pw.Write((byte)slot);                                // 槽位
        pw.Write((short)item.stack);                         // 堆叠数
        pw.Write(item.prefix);                               // 词缀
        pw.Write((short)(item.Name == null ? 0 : item.type)); // 物品ID
        var endPos = (int)pw.BaseStream.Position;
        pw.BaseStream.Position = pos;
        pw.Write((ushort)endPos);                            // 写长度头
        pw.BaseStream.Position = endPos;
        return ms.ToArray();
    }

    private static void Pass(string reason, TSPlayer? player) { }

    private static void Block(string reason, TSPlayer? player)
    {
        Interlocked.Increment(ref _blockedCount);
        if (player != null && player.Active && player.IsLoggedIn)
        {
            BanPlayer(player, reason);
        }
        TShock.Log.ConsoleInfo($"[ChestFix] 拦截: {reason} | 玩家={player?.Name ?? "?"} [已封禁]");
    }

    private static void BanPlayer(TSPlayer player, string reason)
    {
        try
        {
            var name = player.Name;
            Commands.HandleCommand(TSPlayer.Server, $"/ban add \"acc:{name}\" \"发送恶意宝箱数据: {reason}\" -e");
            Commands.HandleCommand(TSPlayer.Server, $"/kick \"{name}\" 发送恶意宝箱数据");
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[ChestFix] 封禁玩家失败: {ex.Message}");
        }
    }
}
