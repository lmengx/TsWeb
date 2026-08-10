using HouseRegion;
using Newtonsoft.Json.Linq;
using Rests;
using System.Text;
using TShockAPI;

namespace TShockData;

/// <summary>
/// House 房屋系统 Web API（由主插件提供，数据源为主插件内存中的 HouseCore.Houses）。
/// 路由经 TSWeb backend 的 /api/tshock/data/* 通用代理转发给前端。
/// </summary>
public static class HouseApi
{
    private static readonly string BuildingDir = Path.Combine(TShock.SavePath, "TSWeb", "Buildings");

    public static void Register()
    {
        TShock.RestApi.Register(new SecureRestCommand("/data/house/list", HandleHouseList, "data.rest.invsee"));
        TShock.RestApi.Register(new SecureRestCommand("/data/buildings/list", HandleBuildingsList, "data.rest.invsee"));
        TShock.RestApi.Register(new SecureRestCommand("/data/buildings/info", HandleBuildingInfo, "data.rest.invsee"));
        TShock.RestApi.Register(new SecureRestCommand("/data/buildings/export", HandleBuildingsExport, "data.rest.invsee"));
        TShock.RestApi.Register(new SecureRestCommand("/data/buildings/import", HandleBuildingsImport, "data.rest.invsee"));
        TShock.RestApi.Register(new SecureRestCommand("/data/buildings/upload", HandleBuildingsUpload, "data.rest.invsee"));
        TShock.RestApi.Register(new SecureRestCommand("/data/buildings/delete-local", HandleBuildingsDeleteLocal, "data.rest.invsee"));
        TShock.RestApi.Register(new SecureRestCommand("/data/buildings/online-players", HandleBuildingsOnlinePlayers, "data.rest.invsee"));
    }

    // ══════════════════════════════════════════════════════════
    //  /data/house/list — 房屋分页列表（含全部附属信息）
    // ══════════════════════════════════════════════════════════

    private static object HandleHouseList(RestRequestArgs args)
    {
        try
        {
            var page = Math.Max(GetInt(args, "page", 1), 1);
            var pageSize = Math.Clamp(GetInt(args, "pageSize", 20), 1, 100);

            var houses = HouseCore.Houses.Where(h => h != null).ToList();
            var total = houses.Count;
            var items = houses.Skip((page - 1) * pageSize).Take(pageSize)
                .Select(ToHouseDto).ToList();

            return new RestObject()
            {
                { "total", total },
                { "page", page },
                { "pageSize", pageSize },
                { "items", items }
            };
        }
        catch (Exception ex)
        {
            return new RestObject("500") { { "error", ex.Message } };
        }
    }

    private static Dictionary<string, object?> ToHouseDto(House h)
    {
        var area = h.HouseArea;
        var hasExpel = h.ExpelX.HasValue && h.ExpelY.HasValue;
        return new Dictionary<string, object?>
        {
            ["name"] = h.Name,
            ["author"] = h.Author,
            ["authorName"] = UserNameById(h.Author),
            ["owners"] = h.Owners,
            ["ownerNames"] = h.Owners.Select(UserNameById).ToList(),
            ["users"] = h.Users,
            ["userNames"] = h.Users.Select(UserNameById).ToList(),
            ["area"] = new Dictionary<string, object?>
            {
                ["x"] = area.X, ["y"] = area.Y, ["width"] = area.Width, ["height"] = area.Height
            },
            ["tp"] = new Dictionary<string, object?> { ["x"] = h.TpX, ["y"] = h.TpY },
            ["expel"] = hasExpel
                ? new Dictionary<string, object?> { ["x"] = h.ExpelX!.Value, ["y"] = h.ExpelY!.Value }
                : null,
            ["expelOnViolate"] = h.ExpelOnViolate,
            ["notify"] = new Dictionary<string, object?>
            {
                ["breakPlace"] = h.NotifyBreakPlace, ["enter"] = h.NotifyEnter
            },
            ["permissions"] = new Dictionary<string, object?>
            {
                ["entry"] = h.AllowEntry, ["tp"] = h.AllowTP, ["place"] = h.AllowPlace, ["break"] = h.AllowBreak,
                ["liquid"] = h.AllowLiquid, ["chest"] = h.AllowChest, ["plant"] = h.AllowPlant, ["spawn"] = h.AllowSpawn,
                ["grave"] = h.AllowGrave, ["switch"] = h.AllowSwitch, ["door"] = h.AllowDoor, ["fragile"] = h.AllowFragile
            }
        };
    }

    // ══════════════════════════════════════════════════════════
    //  /data/buildings/list — .tsb 导出文件分页列表
    // ══════════════════════════════════════════════════════════

    private static object HandleBuildingsList(RestRequestArgs args)
    {
        try
        {
            var page = Math.Max(GetInt(args, "page", 1), 1);
            var pageSize = Math.Clamp(GetInt(args, "pageSize", 20), 1, 100);

            if (!Directory.Exists(BuildingDir))
                return new RestObject()
                {
                    { "total", 0 }, { "page", page }, { "pageSize", pageSize }, { "items", new List<object>() }
                };

            var files = Directory.GetFiles(BuildingDir, "*.tsb")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTime)
                .ToList();
            var total = files.Count;
            var items = files.Skip((page - 1) * pageSize).Take(pageSize)
                .Select(ToBuildingDto).ToList();

            return new RestObject()
            {
                { "total", total },
                { "page", page },
                { "pageSize", pageSize },
                { "items", items }
            };
        }
        catch (Exception ex)
        {
            return new RestObject("500") { { "error", ex.Message } };
        }
    }

    private static Dictionary<string, object?> ToBuildingDto(FileInfo f)
    {
        // 只解析 JSON 外壳（不读 tile 二进制），损坏文件保留基础信息
        string name = "", author = "", createdAt = "";
        int width = 0, height = 0, entities = 0;
        try
        {
            var j = JObject.Parse(File.ReadAllText(f.FullName, Encoding.UTF8));
            name = j["meta"]?["name"]?.ToString() ?? "";
            author = j["meta"]?["author"]?.ToString() ?? "";
            createdAt = j["meta"]?["createdAt"]?.ToString() ?? "";
            width = j["size"]?["width"]?.Value<int>() ?? 0;
            height = j["size"]?["height"]?.Value<int>() ?? 0;
            entities = (j["entities"] as JArray)?.Count ?? 0;
        }
        catch { /* 忽略解析失败 */ }

        return new Dictionary<string, object?>
        {
            ["file"] = f.Name,
            ["name"] = name,
            ["author"] = author,
            ["createdAt"] = createdAt,
            ["width"] = width,
            ["height"] = height,
            ["entities"] = entities,
            ["sizeBytes"] = f.Length,
            ["modifiedAt"] = f.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")
        };
    }

    // ══════════════════════════════════════════════════════════
    //  /data/buildings/info — 单个 .tsb 文件完整外壳信息（camelCase 原样返回）
    // ══════════════════════════════════════════════════════════

    private static object HandleBuildingInfo(RestRequestArgs args)
    {
        try
        {
            var file = args.Parameters["file"] ?? "";
            var safeName = Path.GetFileName(file);
            if (!safeName.EndsWith(".tsb", StringComparison.OrdinalIgnoreCase))
                return new RestObject("400") { { "error", "仅支持 .tsb 文件" } };

            var path = Path.Combine(BuildingDir, safeName);
            if (!File.Exists(path))
                return new RestObject("404") { { "error", "文件不存在: " + safeName } };

            var j = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));

            // 追加文件信息 + 实体统计（按 type/kind 计数）
            j["file"] = safeName;
            j["sizeBytes"] = new FileInfo(path).Length;

            var summary = new Dictionary<string, int>();
            if (j["entities"] is JArray arr)
            {
                foreach (var e in arr.OfType<JObject>())
                {
                    var type = e["type"]?.ToString() ?? "";
                    var key = type == "tileEntity" ? e["kind"]?.ToString() ?? "tileEntity" : type;
                    summary[key] = summary.TryGetValue(key, out var c) ? c + 1 : 1;
                }
            }
            j["entitiesSummary"] = JObject.FromObject(summary);
            return j;
        }
        catch (Exception ex)
        {
            return new RestObject("500") { { "error", ex.Message } };
        }
    }

    // ══════════════════════════════════════════════════════════
    //  /data/buildings/export — 房屋 → 本地 .tsb（Web API）
    // ══════════════════════════════════════════════════════════

    private static object HandleBuildingsExport(RestRequestArgs args)
    {
        var houseName = args.Parameters["house"] ?? "";
        if (string.IsNullOrEmpty(houseName))
            return new RestObject("400") { { "error", "house is required" } };

        var house = HouseCore.Houses.FirstOrDefault(x => x != null
            && x.Name.Equals(houseName, StringComparison.OrdinalIgnoreCase));
        if (house == null)
            return new RestObject("404") { { "error", "房屋不存在: " + houseName } };

        var filePath = HouseExporter.ExportToFile(house, "web", out var error);
        if (filePath == null)
            return new RestObject("500") { { "error", error } };

        var fi = new FileInfo(filePath);
        return new RestObject()
        {
            { "success", true },
            { "file", fi.Name },
            { "width", house.HouseArea.Width },
            { "height", house.HouseArea.Height },
            { "path", filePath }
        };
    }

    // ══════════════════════════════════════════════════════════
    //  /data/buildings/import — .tsb → 世界（锚点 + 对齐 + 领地范围校验）
    // ══════════════════════════════════════════════════════════

    private static object HandleBuildingsImport(RestRequestArgs args)
    {
        var file = args.Parameters["file"] ?? "";
        if (string.IsNullOrEmpty(file))
            return new RestObject("400") { { "error", "file is required" } };

        var anchor = args.Parameters["anchor"] ?? "player";
        var anchorPlayer = args.Parameters["anchorPlayer"] ?? "";
        var anchorHouse = args.Parameters["anchorHouse"] ?? "";
        var coords = args.Parameters["coords"] ?? "";
        var align = args.Parameters["align"] ?? "center";

        var outcome = HouseImporter.ImportAt(file, anchor, anchorPlayer, anchorHouse, coords, align);
        if (!outcome.Success)
        {
            return new RestObject("400")
            {
                { "error", outcome.Error },
                { "startX", outcome.StartX }, { "startY", outcome.StartY },
                { "width", outcome.Width }, { "height", outcome.Height }
            };
        }
        return new RestObject()
        {
            { "success", true },
            { "startX", outcome.StartX }, { "startY", outcome.StartY },
            { "width", outcome.Width }, { "height", outcome.Height }
        };
    }

    // ══════════════════════════════════════════════════════════
    //  /data/buildings/upload — 后端 → 插件 TSWeb/Buildings/（分片）
    // ══════════════════════════════════════════════════════════

    private static object HandleBuildingsUpload(RestRequestArgs args)
    {
        var file = args.Parameters["file"] ?? "";
        var data = args.Parameters["data"] ?? "";
        if (string.IsNullOrEmpty(file) || string.IsNullOrEmpty(data))
            return new RestObject("400") { { "error", "file and data are required" } };

        var safeName = Path.GetFileName(file);
        if (!safeName.EndsWith(".tsb", StringComparison.OrdinalIgnoreCase))
            return new RestObject("400") { { "error", "仅支持 .tsb 文件" } };

        var append = args.Parameters["append"] == "1";
        try
        {
            Directory.CreateDirectory(BuildingDir);
            var full = Path.Combine(BuildingDir, safeName);
            var bytes = Convert.FromBase64String(data);
            using (var fs = new FileStream(full, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read))
                fs.Write(bytes, 0, bytes.Length);
            return new RestObject("200") { { "message", "写入成功" }, { "received", bytes.Length } };
        }
        catch (Exception ex)
        {
            return new RestObject("500") { { "error", ex.Message } };
        }
    }

    // ══════════════════════════════════════════════════════════
    //  /data/buildings/delete-local — 删除插件本地 .tsb
    // ══════════════════════════════════════════════════════════

    private static object HandleBuildingsDeleteLocal(RestRequestArgs args)
    {
        var file = args.Parameters["file"] ?? "";
        if (string.IsNullOrEmpty(file))
            return new RestObject("400") { { "error", "file is required" } };

        var safeName = Path.GetFileName(file);
        if (!safeName.EndsWith(".tsb", StringComparison.OrdinalIgnoreCase))
            return new RestObject("400") { { "error", "仅支持 .tsb 文件" } };

        var full = Path.Combine(BuildingDir, safeName);
        if (!File.Exists(full))
            return new RestObject("404") { { "error", "文件不存在" } };
        try
        {
            File.Delete(full);
            return new RestObject("200") { { "message", "删除成功" } };
        }
        catch (Exception ex)
        {
            return new RestObject("500") { { "error", ex.Message } };
        }
    }

    // ══════════════════════════════════════════════════════════
    //  /data/buildings/online-players — 在线玩家坐标列表
    // ══════════════════════════════════════════════════════════

    private static object HandleBuildingsOnlinePlayers(RestRequestArgs args)
    {
        var list = new List<object>();
        foreach (var p in TShock.Players)
        {
            if (p == null || !p.Active) continue;
            list.Add(new Dictionary<string, object>
            {
                { "name", p.Name },
                { "tileX", p.TileX },
                { "tileY", p.TileY }
            });
        }
        return new RestObject() { { "players", list } };
    }

    // ══════════════════════════════════════════════════════════
    //  工具
    // ══════════════════════════════════════════════════════════

    private static int GetInt(RestRequestArgs args, string key, int def)
    {
        var v = args.Parameters[key];
        return v != null && int.TryParse(v, out var n) ? n : def;
    }

    private static string UserNameById(string id)
    {
        try
        {
            if (int.TryParse(id, out var uid))
            {
                var acc = TShock.UserAccounts.GetUserAccountByID(uid);
                if (acc != null)
                    return acc.Name;
            }
        }
        catch { }
        return id;
    }
}
