using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using System.Collections;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Tile_Entities;
using Terraria.ID;
using TShockAPI;

namespace HouseRegion;

/// <summary>
/// 房屋建筑导入（管理员）：将 .tsb 文件校验后还原到世界。
/// 读取目录: {TShock.SavePath}/TSWeb/Buildings/
/// 粘贴锚点：以执行命令的玩家为中心（建筑底部对齐玩家脚部）。
/// 还原流程：KillAll(旧实体) -> 写 tile -> FixAll(重建实体) -> Restore(实体数据)
/// </summary>
public static class HouseImporter
{
    private const int TileSize = 14;                    // raw14 每格字节数
    private const int MaxWidth = 4096, MaxHeight = 4096;
    private const long MaxArea = 16_777_216;
    private const int MaxEntities = 2048;

    private static readonly string ImportDir = Path.Combine(TShock.SavePath, "TSWeb", "Buildings");

    private static readonly string[] ChatMarkers = { "[c/", "[i:", "[n:", "[a:" };

    public static bool Import(TSPlayer op, string fileName)
    {
        try
        {
            // ── 1. 读文件（防路径穿越：仅取文件名 + 强制 .tsb 扩展名）──
            var safeName = Path.GetFileName(fileName);
            if (!safeName.EndsWith(".tsb", StringComparison.OrdinalIgnoreCase))
            {
                op.SendErrorMessage("仅支持 .tsb 文件。用法: /h import <文件名>");
                return false;
            }

            var filePath = Path.Combine(ImportDir, safeName);
            if (!File.Exists(filePath))
            {
                op.SendErrorMessage($"未找到建筑文件: {safeName}（目录: {ImportDir}）");
                return false;
            }

            var json = File.ReadAllText(filePath, Encoding.UTF8);
            TsbDocument? doc;
            try
            {
                doc = JsonConvert.DeserializeObject<TsbDocument>(json);
            }
            catch (Exception ex)
            {
                op.SendErrorMessage($"建筑文件解析失败: {ex.Message}");
                return false;
            }
            if (doc == null)
            {
                op.SendErrorMessage("建筑文件内容为空。");
                return false;
            }

            // ── 2. 校验（L0-L3，硬性拒绝）──
            var raw = Validate(op, doc);
            if (raw == null) return false;

            // ── 3. 粘贴位置：玩家为中心 ──
            var startX = op.TileX - doc.Size.Width / 2;
            var startY = op.TileY - doc.Size.Height;

            // ── 4. 写入世界 ──
            Paste(op, doc, raw, startX, startY);
            return true;
        }
        catch (Exception ex)
        {
            TShock.Log.Error("[HouseRegion] 导入房屋建筑失败: " + ex);
            op.SendErrorMessage($"导入房屋建筑失败: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }

    /// <summary>列出可用 .tsb 文件</summary>
    public static List<string> ListFiles()
    {
        if (!Directory.Exists(ImportDir))
            return new List<string>();
        return Directory.GetFiles(ImportDir, "*.tsb").Select(Path.GetFileName).ToList()!;
    }

    // ══════════════════════════════════════════════════════════
    //  校验（L0 外壳 / L1 尺寸 / L2 tile 段 / L3 实体）
    //  通过则返回解压后的 raw14 字节流，否则返回 null
    // ══════════════════════════════════════════════════════════

    private static byte[]? Validate(TSPlayer op, TsbDocument doc)
    {
        var errors = new List<string>();
        var w = doc.Size.Width;
        var h = doc.Size.Height;

        // L0
        if (doc.Format != "tsweb-building")
            errors.Add($"格式标识错误: {doc.Format}");
        if (doc.FormatVersion != 1)
            errors.Add($"不支持的格式版本: {doc.FormatVersion}");

        // L1 尺寸
        if (w < 1 || w > MaxWidth)
            errors.Add($"宽度越界: {w}");
        if (h < 1 || h > MaxHeight)
            errors.Add($"高度越界: {h}");
        if ((long)w * h > MaxArea)
            errors.Add($"面积超限: {w}x{h}");

        // L2 tile 段
        if (doc.Tile.Encoding != "raw14")
            errors.Add($"不支持的 tile 编码: {doc.Tile.Encoding}");
        if (doc.Tile.Compression is not ("none" or "gzip"))
            errors.Add($"不支持的压缩方式: {doc.Tile.Compression}");
        if (doc.Tile.ExpectedCount != (long)w * h)
            errors.Add($"expectedCount 与尺寸不符: {doc.Tile.ExpectedCount} != {w * h}");

        byte[]? raw = null;
        if (errors.Count == 0)
        {
            try
            {
                var data = Convert.FromBase64String(doc.Tile.Data);
                raw = doc.Tile.Compression == "gzip" ? GZipDecompress(data) : data;
            }
            catch (Exception ex)
            {
                errors.Add($"tile 数据解码失败: {ex.Message}");
            }
        }

        if (raw != null)
        {
            if (raw.Length != doc.Tile.ExpectedCount * TileSize)
                errors.Add($"tile 数据长度错误: {raw.Length} != {doc.Tile.ExpectedCount * TileSize}");
            else
            {
                var hash = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant();
                if (!hash.Equals(doc.Tile.Checksum ?? "", StringComparison.OrdinalIgnoreCase))
                    errors.Add("SHA-256 校验和不匹配，文件可能损坏或被篡改");

                // 逐格：保留位必须为 0
                for (var i = 0; i < raw.Length && errors.Count == 0; i += TileSize)
                {
                    var sHeader = BitConverter.ToUInt16(raw, i + 5);
                    var bHeader3 = raw[i + 9];
                    if ((sHeader & 0x8000) != 0)
                    {
                        errors.Add($"第 {i / TileSize} 格 sTileHeader 保留位非法");
                        break;
                    }
                    if ((bHeader3 & 0xE0) != 0)
                    {
                        errors.Add($"第 {i / TileSize} 格 bTileHeader3 保留位非法");
                        break;
                    }
                }
            }
        }

        // L3 实体
        var entities = doc.Entities;
        if (entities != null)
        {
            if (entities.Count > MaxEntities)
                errors.Add($"实体数量超限: {entities.Count} > {MaxEntities}");
            var chestSlots = new HashSet<int>();
            foreach (var e in entities)
            {
                if (e.Type is not ("chest" or "sign" or "tileEntity"))
                {
                    errors.Add($"未知实体类型: {e.Type}");
                    continue;
                }
                if (e.X < 0 || e.X >= w || e.Y < 0 || e.Y >= h)
                {
                    errors.Add($"实体坐标越界: {e.Type}({e.X},{e.Y})");
                    continue;
                }
                if (e.Type == "tileEntity" && !IsKnownKind(e.Kind))
                {
                    errors.Add($"未知 tileEntity 类型: {e.Kind}");
                    continue;
                }

                switch (e.Type)
                {
                    case "chest":
                        if ((e.Name ?? "").Length > 20) errors.Add("箱子名超长");
                        if (e.Items != null)
                        {
                            if (e.Items.Count > 40) errors.Add("箱子物品数超限");
                            chestSlots.Clear();
                            foreach (var it in e.Items)
                            {
                                if (it.Slot is not int slot || slot < 0 || slot >= 40)
                                {
                                    errors.Add($"箱子槽位非法: {it.Slot}");
                                    continue;
                                }
                                if (!chestSlots.Add(slot)) errors.Add($"箱子槽位重复: {slot}");
                                ValidateItem(it, errors);
                            }
                        }
                        if (HasChatMarkup(e.Name)) errors.Add("箱子名含聊天标记");
                        break;

                    case "sign":
                        var text = e.Text ?? "";
                        if (text.Length > 500) errors.Add("标牌文字超长");
                        if (text.Any(IsControlChar)) errors.Add("标牌文字含控制字符");
                        if (HasChatMarkup(text)) errors.Add("标牌文字含聊天标记");
                        break;

                    case "tileEntity":
                        ValidateEntityPayload(e, errors);
                        break;
                }
            }
        }

        if (errors.Count > 0)
        {
            var msg = string.Join("\n", errors.Take(10));
            op.SendErrorMessage($"建筑校验失败:\n{msg}" + (errors.Count > 10 ? $"\n...共 {errors.Count} 条" : ""));
            return null;
        }

        return raw;
    }

    private static void ValidateEntityPayload(TsbEntity e, List<string> errors)
    {
        switch (e.Kind)
        {
            case "itemFrame":
            case "weaponsRack":
            case "foodPlatter":
            case "deadCellsJar":
                if (e.Item == null) errors.Add($"{e.Kind} 缺少物品");
                else ValidateItem(e.Item, errors);
                break;

            case "displayDoll":
                ValidateSlotItems(e.Items, 8, errors);
                ValidateSlotItems(e.Dyes, 8, errors);
                break;

            case "hatRack":
                ValidateSlotItems(e.Items, 2, errors);
                ValidateSlotItems(e.Dyes, 2, errors);
                break;

            case "logicSensor":
                if (e.LogicCheck is not ("none" or "day" or "night" or "playerAbove" or "water" or "lava" or "honey" or "liquid"))
                    errors.Add($"logicCheck 枚举非法: {e.LogicCheck}");
                if (e.On != null && e.On is not bool)
                    errors.Add("logicSensor on 字段类型错误");
                break;

            case "kiteAnchor":
            case "critterAnchor":
                if (e.ItemType is not int it || it < 0 || it > ushort.MaxValue)
                    errors.Add($"{e.Kind} itemType 非法: {e.ItemType}");
                break;

            case "pylon":
            case "trainingDummy":
                break; // 无载荷

            default:
                errors.Add($"未知 tileEntity 类型: {e.Kind}");
                break;
        }
    }

    private static void ValidateSlotItems(List<TsbItem>? items, int capacity, List<string> errors)
    {
        if (items == null) return;
        var slots = new HashSet<int>();
        foreach (var it in items)
        {
            if (it.Slot is not int slot || slot < 0 || slot >= capacity)
            {
                errors.Add($"槽位非法: {it.Slot}");
                continue;
            }
            if (!slots.Add(slot)) errors.Add($"槽位重复: {slot}");
            ValidateItem(it, errors);
        }
    }

    private static void ValidateItem(TsbItem it, List<string> errors)
    {
        if (it.Id < 0 || it.Id > ushort.MaxValue) errors.Add($"物品 ID 非法: {it.Id}");
        if (it.Stack < 1 || it.Stack > 9999) errors.Add($"物品数量非法: {it.Stack}");
        if (it.Prefix < 0 || it.Prefix > 84) errors.Add($"前缀非法: {it.Prefix}");
    }

    private static bool IsKnownKind(string? kind) => kind is
        "trainingDummy" or "itemFrame" or "logicSensor" or "displayDoll" or "weaponsRack"
        or "hatRack" or "foodPlatter" or "pylon" or "deadCellsJar" or "kiteAnchor" or "critterAnchor";

    private static bool HasChatMarkup(string? s) =>
        s != null && ChatMarkers.Any(m => s.Contains(m, StringComparison.OrdinalIgnoreCase));

    private static bool IsControlChar(char c) => c < 0x20 && c != '\n';

    // ══════════════════════════════════════════════════════════
    //  写入世界（KillAll -> tile -> FixAll -> Restore）
    // ══════════════════════════════════════════════════════════

    private static void Paste(TSPlayer op, TsbDocument doc, byte[] raw, int startX, int startY)
    {
        var w = doc.Size.Width;
        var h = doc.Size.Height;
        var endX = startX + w;
        var endY = startY + h;

        // 1) 清除目标区域旧实体（箱子/标牌/各类 TileEntity）
        KillAll(startX, startY, w, h);
        TShock.Log.ConsoleInfo($"[HouseRegion] 导入 1/5 清除完成 ({w}x{h})");

        // 2) 写入 tile（越界格也读取消耗流，保证后续字节对齐）
        using var ms = new MemoryStream(raw);
        using var br = new BinaryReader(ms);
        for (var x = 0; x < w; x++)
        {
            for (var y = 0; y < h; y++)
            {
                var tile = new Tile
                {
                    type = br.ReadUInt16(),
                    wall = br.ReadUInt16(),
                    liquid = br.ReadByte(),
                    sTileHeader = br.ReadUInt16(),
                    bTileHeader = br.ReadByte(),
                    bTileHeader2 = br.ReadByte(),
                    bTileHeader3 = br.ReadByte(),
                    frameX = br.ReadInt16(),
                    frameY = br.ReadInt16()
                };

                var worldX = startX + x;
                var worldY = startY + y;
                if (worldX < 0 || worldX >= Main.maxTilesX || worldY < 0 || worldY >= Main.maxTilesY)
                    continue;
                Main.tile[worldX, worldY] = tile;
            }
        }
        TShock.Log.ConsoleInfo("[HouseRegion] 导入 2/5 写 tile 完成");

        // 3) 重建实体骨架（chest/sign/TileEntity）
        FixAll(startX, startY, w, h);
        TShock.Log.ConsoleInfo("[HouseRegion] 导入 3/5 实体骨架完成");

        // 4) 还原实体数据
        RestoreEntities(doc.Entities, startX, startY);
        TShock.Log.ConsoleInfo("[HouseRegion] 导入 4/5 实体数据完成");

        // 5) 刷新帧图 + 强制全量重发 tile 数据
        for (var x = startX; x < endX; x++)
        {
            for (var y = startY; y < endY; y++)
            {
                if (x < 0 || x >= Main.maxTilesX || y < 0 || y >= Main.maxTilesY) continue;
                WorldGen.SquareTileFrame(x, y, true);
            }
        }
        InformPlayers();
        TShock.Log.ConsoleInfo($"[HouseRegion] {op.Name} 导入建筑 {doc.Meta?.Name ?? doc.Tile.ExpectedCount.ToString()} ({w}x{h}) -> ({startX},{startY})");
        op.SendSuccessMessage($"建筑已导入 ({w}x{h})，起始位置 ({startX}, {startY})");
    }

    private static void KillAll(int startX, int startY, int w, int h)
    {
        for (var x = startX; x < startX + w; x++)
        {
            for (var y = startY; y < startY + h; y++)
            {
                if (x < 0 || x >= Main.maxTilesX || y < 0 || y >= Main.maxTilesY) continue;
                var tile = Main.tile[x, y];
                if (tile == null || !tile.active()) continue;

                var type = tile.type;
                if (TileID.Sets.BasicChest[type] && Chest.FindChest(x, y) != -1)
                    Chest.DestroyChest(x, y);
                if (Main.tileSign[type])
                    Sign.KillSign(x, y);

                // TileEntity 统一反射 Kill
                if (TileEntity.ByPosition.TryGetValue(new Point16(x, y), out var te))
                {
                    var kill = te.GetType().GetMethod("Kill", new[] { typeof(int), typeof(int) });
                    kill?.Invoke(null, new object[] { x, y });
                }
            }
        }
    }

    private static void FixAll(int startX, int startY, int w, int h)
    {
        for (var x = startX; x < startX + w; x++)
        {
            for (var y = startY; y < startY + h; y++)
            {
                if (x < 0 || x >= Main.maxTilesX || y < 0 || y >= Main.maxTilesY) continue;
                var tile = Main.tile[x, y];
                if (tile == null || !tile.active()) continue;
                var type = tile.type;

                // 箱子（仅左上角格）
                if (TileID.Sets.BasicChest[type])
                {
                    if ((tile.frameX / 18 % 2) == 0 && (tile.frameY / 18) == 0 && Chest.FindChest(x, y) == -1)
                        Chest.CreateChest(x, y);
                }
                // 标牌/墓碑/广播盒
                else if (Main.tileSign[type] && tile.frameX % 36 == 0 && tile.frameY == 0 && Sign.ReadSign(x, y, false) == -1)
                {
                    Sign.ReadSign(x, y, true);
                }
                else if (type == TileID.ItemFrame && tile.frameX % 36 == 0 && tile.frameY == 0)
                    PlaceEntity(x, y, typeof(TEItemFrame));
                else if ((type == TileID.WeaponsRack || type == TileID.WeaponsRack2) && tile.frameX % 54 == 0 && tile.frameY == 0)
                    PlaceEntity(x, y, typeof(TEWeaponsRack));
                else if (type == TileID.FoodPlatter)
                    PlaceEntity(x, y, typeof(TEFoodPlatter));
                else if (type == TileID.DisplayDoll && tile.frameX % 36 == 0 && tile.frameY == 0)
                    PlaceEntity(x, y, typeof(TEDisplayDoll));
                else if (type == TileID.HatRack && tile.frameX == 0 && tile.frameY == 0)
                    PlaceEntity(x, y, typeof(TEHatRack));
                else if (type == TileID.LogicSensor)
                    PlaceEntity(x, y, typeof(TELogicSensor));
                else if (type == TileID.TeleportationPylon && tile.frameX % 54 == 0 && tile.frameY == 0)
                    PlaceEntity(x, y, typeof(TETeleportationPylon));
                else if (type == TileID.TargetDummy && tile.frameX % 36 == 0 && tile.frameY == 0)
                    PlaceEntity(x, y, typeof(TETrainingDummy));
                else if (type == TileID.DeadCellsDisplayJar)
                    PlaceEntity(x, y, typeof(TEDeadCellsDisplayJar));
                else if (type == TileID.KiteAnchor)
                    PlaceEntity(x, y, typeof(TEKiteAnchor));
                else if (type == TileID.CritterAnchor)
                    PlaceEntity(x, y, typeof(TECritterAnchor));
            }
        }
    }

    private static void PlaceEntity(int x, int y, Type type)
    {
        if (TileEntity.ByPosition.ContainsKey(new Point16(x, y))) return;

        // 强类型调用各子类的 Place(int,int)（基类泛型 Place<T> 经子类类型推断，返回实体 ID）。
        // 不用反射：GetMethod(name, types) 匹配不到泛型方法定义（会返回 null）。
        int id;
        if (type == typeof(TEItemFrame)) id = TEItemFrame.Place(x, y);
        else if (type == typeof(TEWeaponsRack)) id = TEWeaponsRack.Place(x, y);
        else if (type == typeof(TEFoodPlatter)) id = TEFoodPlatter.Place(x, y);
        else if (type == typeof(TEDisplayDoll)) id = TEDisplayDoll.Place(x, y);
        else if (type == typeof(TEHatRack)) id = TEHatRack.Place(x, y);
        else if (type == typeof(TELogicSensor)) id = TELogicSensor.Place(x, y);
        else if (type == typeof(TETeleportationPylon)) id = TETeleportationPylon.Place(x, y);
        else if (type == typeof(TETrainingDummy) || type == typeof(TEDeadCellsDisplayJar)
              || type == typeof(TEKiteAnchor) || type == typeof(TECritterAnchor))
        {
            // 这几种无 public 无参构造，基类泛型 Place<T> 的 new() 约束不满足，强类型调用会被解析到
            // 非泛型 Place(int,int,int) 而缺 type 参数；改用反射构造 + 注册（绕开 new() 约束与类型码不确定性）
            CreateTeReflect(x, y, type);
            return;
        }
        else
        {
            TShock.Log.ConsoleError($"[HouseRegion] 未知实体类型 {type.Name} @ ({x},{y})");
            return;
        }

        if (id != -1 && TileEntity.ByID.TryGetValue(id, out var te))
        {
            PrepareTeArrays(te);  // 确保 Item[] 字段空槽为非 null Item（TEDisplayDoll/TEHatRack 序列化无 null 检查）
            BroadcastTe(te);      // 创建后必须广播，否则在线客户端本地 ByPosition 无此实体，右键无法交互
        }
        else
            TShock.Log.ConsoleError($"[HouseRegion] 创建实体失败: {type.Name} @ ({x},{y})");
    }

    /// <summary>反射：把实体所有 Item[] 字段的空槽填为非 null Item。</summary>
    /// <remarks>Terraria 约定空槽是 netID=0 的 Item 实例，WriteExtraData 序列化无 null 检查，null 元素会 NRE。</remarks>
    private static void PrepareTeArrays(TileEntity te)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        foreach (var f in te.GetType().GetFields(all))
        {
            if (f.FieldType != typeof(Item[])) continue;
            var arr = f.GetValue(te) as Item[];
            if (arr == null)
            {
                f.SetValue(te, new Item[0]);
                continue;
            }
            for (var i = 0; i < arr.Length; i++)
                if (arr[i] == null) arr[i] = new Item();
        }
    }

    /// <summary>反射创建 TileEntity：构造实例 + 分配 ID + 注册到 ByID/ByPosition/_TileEntities + 广播。</summary>
    /// <remarks>用于无 public 无参构造（泛型 Place 的 new() 约束不满足）的实体，如训练假人/死细胞罐/风筝锚/生物锚。</remarks>
    private static void CreateTeReflect(int x, int y, Type type)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        const BindingFlags allS = all | BindingFlags.Static;

        // 允许私有/内部构造创建实例
        var instance = (TileEntity)Activator.CreateInstance(type, nonPublic: true)!;

        var id = 0;
        var assignNewId = typeof(TileEntity).GetMethod("AssignNewID", allS);
        if (assignNewId != null) id = (int)assignNewId.Invoke(null, null)!;

        typeof(TileEntity).GetField("ID", all)?.SetValue(instance, id);
        typeof(TileEntity).GetField("Position", all)?.SetValue(instance, new Point16(x, y));

        (typeof(TileEntity).GetField("ByID", allS)?.GetValue(null) as IDictionary)?.Add(id, instance);
        (typeof(TileEntity).GetField("ByPosition", allS)?.GetValue(null) as IDictionary)?.Add(new Point16(x, y), instance);
        (typeof(TileEntity).GetField("_TileEntities", allS)?.GetValue(null) as IList)?.Add(instance);

        PrepareTeArrays(instance);  // 空槽填非 null Item，避免序列化 NRE
        // 创建后必须广播，否则在线客户端本地 ByPosition 无此实体，右键无法交互
        BroadcastTe(instance);
    }

    /// <summary>广播单个 TileEntity 给所有在线客户端（服务器创建/修改实体后必须发）</summary>
    private static void BroadcastTe(TileEntity te)
        => NetMessage.SendData((int)MessageID.TileEntitySharing, -1, -1, null, te.ID, te.Position.X, te.Position.Y);

    // ══════════════════════════════════════════════════════════
    //  实体数据还原
    // ══════════════════════════════════════════════════════════

    private static void RestoreEntities(List<TsbEntity>? entities, int startX, int startY)
    {
        if (entities == null) return;
        foreach (var e in entities)
        {
            var wx = startX + e.X;
            var wy = startY + e.Y;
            if (wx < 0 || wx >= Main.maxTilesX || wy < 0 || wy >= Main.maxTilesY) continue;

            switch (e.Type)
            {
                case "chest":
                    RestoreChest(wx, wy, e);
                    break;
                case "sign":
                    RestoreSign(wx, wy, e);
                    break;
                case "tileEntity":
                    RestoreTileEntity(wx, wy, e);
                    break;
            }
        }
    }

    private static void RestoreChest(int wx, int wy, TsbEntity e)
    {
        var idx = Chest.FindChest(wx, wy);
        if (idx < 0 || e.Items == null) return;
        var chest = Main.chest[idx];
        foreach (var it in e.Items)
        {
            if (it.Slot is not int slot || slot < 0 || slot >= 40) continue;
            chest.item[slot] = MakeItem(it);
        }
    }

    private static void RestoreSign(int wx, int wy, TsbEntity e)
    {
        var sid = Sign.ReadSign(wx, wy, false);
        if (sid == -1)
            sid = Sign.ReadSign(wx, wy, true);
        if (sid >= 0 && Main.sign[sid] != null)
            Main.sign[sid].text = e.Text ?? "";
    }

    private static void RestoreTileEntity(int wx, int wy, TsbEntity e)
    {
        if (!TileEntity.ByPosition.TryGetValue(new Point16(wx, wy), out var te)) return;

        switch (e.Kind)
        {
            case "itemFrame" when te is TEItemFrame f && e.Item != null:
                f.item = MakeItem(e.Item);
                break;

            case "weaponsRack" when te is TEWeaponsRack wr && e.Item != null:
                wr.item = MakeItem(e.Item);
                break;

            case "foodPlatter" when te is TEFoodPlatter fp && e.Item != null:
                fp.item = MakeItem(e.Item);
                break;

            case "deadCellsJar" when te is TEDeadCellsDisplayJar dj && e.Item != null:
                dj.item = MakeItem(e.Item);
                break;

            case "displayDoll" when te is TEDisplayDoll dd:
                // 不要重建 _equip/_dyes：vanilla 创建时数组元素已是非 null Item（空槽 netID=0），
                // WriteExtraData 序列化无 null 检查，重建会把空槽变 null 导致 NRE
                RestoreSlotItems(dd._equip, e.Items, 8);
                RestoreSlotItems(dd._dyes, e.Dyes, 8);
                break;

            case "hatRack" when te is TEHatRack hr:
                RestoreSlotItems(hr._items, e.Items, 2);
                RestoreSlotItems(hr._dyes, e.Dyes, 2);
                break;

            case "logicSensor" when te is TELogicSensor ls:
                if (e.LogicCheck != null &&
                    Enum.TryParse<TELogicSensor.LogicCheckType>(e.LogicCheck, true, out var lc))
                    ls.logicCheck = lc;
                ls.On = e.On ?? false;
                break;

            case "kiteAnchor":
                if (e.ItemType is int kt)
                    WriteShortField(te, kt, "itemType", "ItemType");
                break;

            case "critterAnchor":
                if (e.ItemType is int ct)
                    WriteShortField(te, ct, "itemType", "ItemType");
                break;

            // pylon / trainingDummy：无载荷，跳过
        }

        // 数据已还原，广播最新实体给所有在线客户端
        BroadcastTe(te);
    }

    private static void RestoreSlotItems(Item[]? target, List<TsbItem>? items, int capacity)
    {
        if (target == null || items == null) return;
        foreach (var it in items)
        {
            if (it.Slot is not int slot || slot < 0 || slot >= capacity) continue;
            target[slot] = MakeItem(it);
        }
    }

    private static Item MakeItem(TsbItem it)
    {
        var item = new Item();
        item.netDefaults(Math.Clamp(it.Id, 0, short.MaxValue));
        item.stack = it.Stack;
        item.prefix = (byte)Math.Clamp(it.Prefix, 0, 84);
        return item;
    }

    private static void WriteShortField(TileEntity te, int value, params string[] fieldNames)
    {
        foreach (var fn in fieldNames)
        {
            var f = te.GetType().GetField(fn);
            if (f != null && f.FieldType == typeof(short))
            {
                f.SetValue(te, (short)Math.Clamp(value, 0, short.MaxValue));
                return;
            }
        }
    }

    // ══════════════════════════════════════════════════════════
    //  工具
    // ══════════════════════════════════════════════════════════

    private static byte[] GZipDecompress(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var gz = new GZipStream(ms, CompressionMode.Decompress);
        using var outMs = new MemoryStream();
        gz.CopyTo(outMs);
        return outMs.ToArray();
    }

    /// <summary>强制所有在线玩家重发 tile 数据（清空已接收区块标记）</summary>
    private static void InformPlayers()
    {
        for (var j = 0; j < 255; j++)
        {
            if (!Netplay.Clients[j].IsActive) continue;
            for (var k = 0; k < Main.maxSectionsX; k++)
            {
                for (var l = 0; l < Main.maxSectionsY; l++)
                {
                    Netplay.Clients[j].TileSections[k, l] = false;
                }
            }
        }
    }
}
