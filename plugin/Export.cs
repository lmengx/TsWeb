using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Tile_Entities;
using Terraria.ID;
using TShockAPI;

namespace HouseRegion;

/// <summary>
/// 房屋建筑导出（管理员）：将房屋领地范围内的方块与实体导出为 .tsb 文件。
/// 格式遵循《建筑文件格式规范 tsweb-building》（scripts/建筑文件格式规范_tsweb-building.md）。
/// 输出目录: {TShock.SavePath}/TSWeb/Buildings/{屋名}_{时间戳}.tsb
/// </summary>
public static class HouseExporter
{
    private static readonly string ExportDir = Path.Combine(TShock.SavePath, "TSWeb", "Buildings");

    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        StringEscapeHandling = StringEscapeHandling.Default
    };

    public static bool Export(TSPlayer op, House house)
    {
        try
        {
            Directory.CreateDirectory(ExportDir);

            var doc = TsbBuilder.Build(house.HouseArea, op, house.Name);
            var fileName = $"{SanitizeFileName(house.Name)}_{DateTime.Now:yyyyMMdd_HHmmss}.tsb";
            var filePath = Path.Combine(ExportDir, fileName);

            File.WriteAllText(filePath, JsonConvert.SerializeObject(doc, JsonSettings), Encoding.UTF8);
            TShock.Log.ConsoleInfo($"[HouseRegion] {op.Name} 导出了房屋 {house.Name} 的建筑 -> {filePath}");
            op.SendSuccessMessage($"房屋 [{house.Name}] 建筑已导出 ({doc.Size.Width}x{doc.Size.Height}): {filePath}");
            return true;
        }
        catch (Exception ex)
        {
            TShock.Log.Error("[HouseRegion] 导出房屋建筑失败: " + ex);
            op.SendErrorMessage($"导出房屋建筑失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>清理屋名中的非法文件名字符</summary>
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(invalid.Contains(c) ? '_' : c);
        var s = sb.ToString().Trim();
        return string.IsNullOrEmpty(s) ? "unnamed" : s;
    }
}

/// <summary>.tsb 构建器：区域采集 -> raw14 编码 -> gzip -> 实体清单</summary>
public static class TsbBuilder
{
    // raw14 位掩码
    private const ushort S_ACTUATOR = 0x0800;          // sTileHeader bit11
    private const ushort S_WIRE1 = 0x0080, S_WIRE2 = 0x0100, S_WIRE3 = 0x0200;
    private const byte B_WIRE4 = 0x80;                 // bTileHeader bit7

    public static TsbDocument Build(Rectangle area, TSPlayer op, string name)
    {
        var width = area.Width;
        var height = area.Height;
        ushort maxTile = 0, maxWall = 0, maxItem = 0;
        var requiresActuator = false;
        var requiresWire = false;

        // ── 1. 采集 tile -> raw14 字节流 ──
        byte[] raw;
        using (var ms = new MemoryStream())
        {
            using (var bw = new BinaryWriter(ms))
            {
                for (var x = 0; x < width; x++)
                {
                    for (var y = 0; y < height; y++)
                    {
                        var t = Main.tile[area.X + x, area.Y + y];
                        if (t == null) continue;

                        var s = t.sTileHeader;
                        var b = t.bTileHeader;

                        if (t.type > maxTile) maxTile = t.type;
                        if (t.wall > maxWall) maxWall = t.wall;
                        if ((s & S_ACTUATOR) != 0) requiresActuator = true;
                        if ((s & (S_WIRE1 | S_WIRE2 | S_WIRE3)) != 0 || (b & B_WIRE4) != 0) requiresWire = true;

                        // raw14: type(2) wall(2) liquid(1) sTileHeader(2) bTileHeader(1) bTileHeader2(1) bTileHeader3(1) frameX(2) frameY(2)
                        bw.Write(t.type);
                        bw.Write(t.wall);
                        bw.Write(t.liquid);
                        bw.Write(s);
                        bw.Write(t.bTileHeader);
                        bw.Write(t.bTileHeader2);
                        bw.Write(t.bTileHeader3);
                        bw.Write(t.frameX);
                        bw.Write(t.frameY);
                    }
                }
            }
            raw = ms.ToArray();
        }

        // ── 2. gzip 压缩 ──
        byte[] gz;
        using (var gzs = new MemoryStream())
        {
            using (var gzStream = new GZipStream(gzs, CompressionLevel.Optimal, true))
                gzStream.Write(raw, 0, raw.Length);
            gz = gzs.ToArray();
        }

        // ── 3. 实体采集 ──
        var itemIds = new List<int>();
        var entities = CollectEntities(area, itemIds);
        if (itemIds.Count > 0)
            maxItem = (ushort)Math.Min(itemIds.Max(), ushort.MaxValue);

        // ── 4. 组装文档 ──
        return new TsbDocument
        {
            Format = "tsweb-building",
            FormatVersion = 1,
            Meta = new TsbMeta
            {
                Name = name,
                Author = op.Name,
                CreatedAt = DateTime.Now.ToString("O"),
                Source = new TsbSource
                {
                    World = Main.worldName,
                    WorldSeed = Main.ActiveWorldFileData.Seed,
                    GameVersion = Main.versionNumber
                }
            },
            Compat = new TsbCompat
            {
                MaxTileId = maxTile,
                MaxWallId = maxWall,
                MaxItemId = maxItem,
                RequiresActuator = requiresActuator,
                RequiresWire = requiresWire
            },
            Size = new TsbSize { Width = width, Height = height },
            Tile = new TsbTile
            {
                Encoding = "raw14",
                Compression = "gzip",
                ExpectedCount = width * height,
                Checksum = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant(),
                Data = Convert.ToBase64String(gz)
            },
            Entities = entities
        };
    }

    // ══════════════════════════════════════════════════════════
    //  实体采集（13 种：chest / sign / 11 种 tileEntity）
    //  统一通过 TileEntity.ByPosition 查找（不依赖各类的 Find 方法）
    // ══════════════════════════════════════════════════════════

    private static List<TsbEntity> CollectEntities(Rectangle area, List<int> itemIds)
    {
        var entities = new List<TsbEntity>();
        var minX = area.X;
        var minY = area.Y;
        var maxX = area.Right;
        var maxY = area.Bottom;

        void TrackItem(int id)
        {
            if (id > 0) itemIds.Add(id);
        }

        for (var x = minX; x < maxX; x++)
        {
            for (var y = minY; y < maxY; y++)
            {
                var tile = Main.tile[x, y];
                if (tile == null || !tile.active()) continue;

                var relX = x - minX;
                var relY = y - minY;
                var type = tile.type;
                TileEntity? te;

                // ── 箱子：40 槽稀疏导出，含 slot ──
                if (TileID.Sets.BasicChest[type])
                {
                    var idx = Chest.FindChest(x, y);
                    if (idx >= 0)
                    {
                        var chest = Main.chest[idx];
                        if (chest != null && chest.x == x && chest.y == y)
                        {
                            var items = new List<TsbItem>();
                            for (var slot = 0; slot < 40; slot++)
                            {
                                var it = chest.item[slot];
                                if (it != null && it.active && it.type > 0)
                                {
                                    TrackItem(it.type);
                                    items.Add(new TsbItem { Slot = slot, Id = it.type, Stack = it.stack, Prefix = it.prefix });
                                }
                            }
                            entities.Add(new TsbEntity { Type = "chest", X = relX, Y = relY, Name = chest.name ?? "", Items = items });
                        }
                    }
                }
                // ── 标牌/墓碑/广播盒：文本 ──
                else if (Main.tileSign[type])
                {
                    var sid = Sign.ReadSign(x, y);
                    if (sid >= 0)
                    {
                        var sign = Main.sign[sid];
                        if (sign != null)
                            entities.Add(new TsbEntity { Type = "sign", X = relX, Y = relY, Text = NormalizeText(sign.text ?? "") });
                    }
                }
                // ── 物品框 ──
                else if (type == TileID.ItemFrame && tile.frameX % 36 == 0 && tile.frameY == 0)
                {
                    if (TileEntity.ByPosition.TryGetValue(new Point16(x, y), out te) && te is TEItemFrame iframe && HasItem(iframe.item))
                    {
                        TrackItem(iframe.item.type);
                        entities.Add(new TsbEntity { Type = "tileEntity", Kind = "itemFrame", X = relX, Y = relY, Item = ToTsbItem(iframe.item) });
                    }
                }
                // ── 武器架 ──
                else if ((type == TileID.WeaponsRack || type == TileID.WeaponsRack2) && tile.frameX % 54 == 0 && tile.frameY == 0)
                {
                    if (TileEntity.ByPosition.TryGetValue(new Point16(x, y), out te) && te is TEWeaponsRack wrack && HasItem(wrack.item))
                    {
                        TrackItem(wrack.item.type);
                        entities.Add(new TsbEntity { Type = "tileEntity", Kind = "weaponsRack", X = relX, Y = relY, Item = ToTsbItem(wrack.item) });
                    }
                }
                // ── 食物托盘 ──
                else if (type == TileID.FoodPlatter)
                {
                    if (TileEntity.ByPosition.TryGetValue(new Point16(x, y), out te) && te is TEFoodPlatter fplate && HasItem(fplate.item))
                    {
                        TrackItem(fplate.item.type);
                        entities.Add(new TsbEntity { Type = "tileEntity", Kind = "foodPlatter", X = relX, Y = relY, Item = ToTsbItem(fplate.item) });
                    }
                }
                // ── 展示假人（8 装备 + 8 染料）──
                else if (type == TileID.DisplayDoll && tile.frameX % 36 == 0 && tile.frameY == 0)
                {
                    if (TileEntity.ByPosition.TryGetValue(new Point16(x, y), out te) && te is TEDisplayDoll ddoll)
                    {
                        var items = SlotItems(ddoll._equip, 8, TrackItem);
                        var dyes = SlotItems(ddoll._dyes, 8, TrackItem);
                        if (items.Count > 0 || dyes.Count > 0)
                            entities.Add(new TsbEntity { Type = "tileEntity", Kind = "displayDoll", X = relX, Y = relY, Items = items, Dyes = dyes });
                    }
                }
                // ── 帽子架（2 装备 + 2 染料）──
                else if (type == TileID.HatRack && tile.frameX == 0 && tile.frameY == 0)
                {
                    if (TileEntity.ByPosition.TryGetValue(new Point16(x, y), out te) && te is TEHatRack hrack)
                    {
                        var items = SlotItems(hrack._items, 2, TrackItem);
                        var dyes = SlotItems(hrack._dyes, 2, TrackItem);
                        if (items.Count > 0 || dyes.Count > 0)
                            entities.Add(new TsbEntity { Type = "tileEntity", Kind = "hatRack", X = relX, Y = relY, Items = items, Dyes = dyes });
                    }
                }
                // ── 逻辑感应器 ──
                else if (type == TileID.LogicSensor)
                {
                    if (TileEntity.ByPosition.TryGetValue(new Point16(x, y), out te) && te is TELogicSensor lsensor)
                        entities.Add(new TsbEntity { Type = "tileEntity", Kind = "logicSensor", X = relX, Y = relY, LogicCheck = LogicCheckName(lsensor.logicCheck), On = lsensor.On });
                }
                // ── 传送晶塔（无载荷）──
                else if (type == TileID.TeleportationPylon && tile.frameX % 54 == 0 && tile.frameY == 0)
                {
                    if (TileEntity.ByPosition.TryGetValue(new Point16(x, y), out te))
                        entities.Add(new TsbEntity { Type = "tileEntity", Kind = "pylon", X = relX, Y = relY });
                }
                // ── 训练假人（无载荷，还原时 npc=0）──
                else if (type == TileID.TargetDummy && tile.frameX % 36 == 0 && tile.frameY == 0)
                {
                    if (TileEntity.ByPosition.TryGetValue(new Point16(x, y), out te))
                        entities.Add(new TsbEntity { Type = "tileEntity", Kind = "trainingDummy", X = relX, Y = relY });
                }
                // ── 死细胞罐 ──
                else if (type == TileID.DeadCellsDisplayJar)
                {
                    if (TileEntity.ByPosition.TryGetValue(new Point16(x, y), out te) && te is TEDeadCellsDisplayJar djar && HasItem(djar.item))
                    {
                        TrackItem(djar.item.type);
                        entities.Add(new TsbEntity { Type = "tileEntity", Kind = "deadCellsJar", X = relX, Y = relY, Item = ToTsbItem(djar.item) });
                    }
                }
                // ── 风筝锚（仅物品 ID）──
                else if (type == TileID.KiteAnchor)
                {
                    if (TileEntity.ByPosition.TryGetValue(new Point16(x, y), out te) && te is TEKiteAnchor kite)
                    {
                        var itemType = ReadShortField(kite, "itemType", "ItemType");
                        if (itemType > 0)
                        {
                            TrackItem(itemType);
                            entities.Add(new TsbEntity { Type = "tileEntity", Kind = "kiteAnchor", X = relX, Y = relY, ItemType = itemType });
                        }
                    }
                }
                // ── 生物锚（仅物品 ID）──
                else if (type == TileID.CritterAnchor)
                {
                    if (TileEntity.ByPosition.TryGetValue(new Point16(x, y), out te) && te is TECritterAnchor critter)
                    {
                        var itemType = ReadShortField(critter, "itemType", "ItemType");
                        if (itemType > 0)
                        {
                            TrackItem(itemType);
                            entities.Add(new TsbEntity { Type = "tileEntity", Kind = "critterAnchor", X = relX, Y = relY, ItemType = itemType });
                        }
                    }
                }
            }
        }

        return entities;
    }

    /// <summary>文本归一化：\r\n / 单独 \r 统一为 \n（标牌多行），避免导入校验被 \r 误判为控制字符</summary>
    private static string NormalizeText(string s) => s.Replace("\r\n", "\n").Replace('\r', '\n');

    private static bool HasItem(Item item) => item != null && item.active && item.type > 0;
    private static TsbItem? ToTsbItem(Item item) =>
        HasItem(item) ? new TsbItem { Id = item.type, Stack = item.stack, Prefix = item.prefix } : null;

    private static List<TsbItem> SlotItems(Item[] arr, int capacity, Action<int> track)
    {
        var list = new List<TsbItem>();
        if (arr == null) return list;
        for (var slot = 0; slot < arr.Length && slot < capacity; slot++)
        {
            var it = arr[slot];
            if (it == null || !it.active || it.type <= 0) continue;
            track(it.type);
            list.Add(new TsbItem { Slot = slot, Id = it.type, Stack = it.stack, Prefix = it.prefix });
        }
        return list;
    }

    /// <summary>枚举名转小驼峰（契约命名）：PlayerAbove -> playerAbove</summary>
    private static string LogicCheckName(TELogicSensor.LogicCheckType t)
    {
        var s = t.ToString();
        return char.ToLowerInvariant(s[0]) + s[1..];
    }

    /// <summary>反射读取 short 字段（兼容 itemType / ItemType 两种命名）</summary>
    private static short ReadShortField(TileEntity te, params string[] fieldNames)
    {
        foreach (var fn in fieldNames)
        {
            var f = te.GetType().GetField(fn);
            if (f != null && f.FieldType == typeof(short))
                return (short)(f.GetValue(te) ?? (short)0);
        }
        return 0;
    }
}

// ══════════════════════════════════════════════════════════
//  .tsb 数据结构（契约：scripts/建筑文件格式规范_tsweb-building.md）
// ══════════════════════════════════════════════════════════

public class TsbDocument
{
    public string Format { get; set; } = "tsweb-building";
    public int FormatVersion { get; set; } = 1;
    public TsbMeta Meta { get; set; } = new();
    public TsbCompat Compat { get; set; } = new();
    public TsbSize Size { get; set; } = new();
    public TsbTile Tile { get; set; } = new();
    public List<TsbEntity>? Entities { get; set; }
}

public class TsbMeta
{
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string? Description { get; set; }
    public List<string>? Tags { get; set; }
    public string CreatedAt { get; set; } = "";
    public TsbSource? Source { get; set; }
}

public class TsbSource
{
    public string? Server { get; set; }
    public string? World { get; set; }
    public long? WorldSeed { get; set; }
    public string? GameVersion { get; set; }
}

public class TsbCompat
{
    public int MaxTileId { get; set; }
    public int MaxWallId { get; set; }
    public int MaxItemId { get; set; }
    public bool RequiresActuator { get; set; }
    public bool RequiresWire { get; set; }
}

public class TsbSize
{
    public int Width { get; set; }
    public int Height { get; set; }
}

public class TsbTile
{
    public string Encoding { get; set; } = "raw14";
    public string Compression { get; set; } = "gzip";
    public int ExpectedCount { get; set; }
    public string Checksum { get; set; } = "";
    public string Data { get; set; } = "";
}

public class TsbItem
{
    public int? Slot { get; set; }
    public int Id { get; set; }
    public int Stack { get; set; }
    public int Prefix { get; set; }
}

public class TsbEntity
{
    public string Type { get; set; } = "";         // chest / sign / tileEntity
    public string? Kind { get; set; }              // tileEntity 时必填
    public int X { get; set; }
    public int Y { get; set; }
    public string? Name { get; set; }              // chest
    public string? Text { get; set; }              // sign
    public List<TsbItem>? Items { get; set; }      // chest / displayDoll / hatRack
    public List<TsbItem>? Dyes { get; set; }       // displayDoll / hatRack
    public TsbItem? Item { get; set; }             // itemFrame / weaponsRack / foodPlatter / deadCellsJar
    public string? LogicCheck { get; set; }        // logicSensor
    public bool? On { get; set; }                  // logicSensor
    public int? ItemType { get; set; }             // kiteAnchor / critterAnchor
}
