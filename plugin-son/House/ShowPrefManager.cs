using Newtonsoft.Json;
using TShockAPI;

namespace HouseRegion;

public class PlayerShowPref
{
    [JsonProperty("sm")]
    public bool ShowMe { get; set; } = false;

    [JsonProperty("so")]
    public bool ShowOthers { get; set; } = true;
}

public static class ShowPrefManager
{
    private static Dictionary<string, PlayerShowPref> _data = new();
    private static readonly string DirPath = Path.Combine(TShock.SavePath, "HouseRegion");
    private static readonly string FilePath = Path.Combine(DirPath, "houseshow.json");

    public static bool GetShowMe(string id)
        => _data.TryGetValue(id, out var p) ? p.ShowMe : false;

    public static bool GetShowOthers(string id)
        => _data.TryGetValue(id, out var p) ? p.ShowOthers : true;

    public static bool ToggleShowMe(string id)
    {
        var cur = GetShowMe(id);
        Set(id, showMe: !cur);
        return !cur;
    }

    public static bool ToggleShowOthers(string id)
    {
        var cur = GetShowOthers(id);
        Set(id, showOthers: !cur);
        return !cur;
    }

    private static void Set(string id, bool? showMe = null, bool? showOthers = null)
    {
        if (!_data.ContainsKey(id)) _data[id] = new PlayerShowPref();
        if (showMe.HasValue) _data[id].ShowMe = showMe.Value;
        if (showOthers.HasValue) _data[id].ShowOthers = showOthers.Value;
        // 回到默认值 → 删除键
        if (!_data[id].ShowMe && _data[id].ShowOthers)
            _data.Remove(id);
        Save();
    }

    public static void Load()
    {
        try
        {
            if (!Directory.Exists(DirPath))
                Directory.CreateDirectory(DirPath);
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                _data = JsonConvert.DeserializeObject<Dictionary<string, PlayerShowPref>>(json)
                        ?? new Dictionary<string, PlayerShowPref>();
            }
            else
            {
                _data = new Dictionary<string, PlayerShowPref>();
                Save();
            }
        }
        catch
        {
            _data = new Dictionary<string, PlayerShowPref>();
        }
    }

    public static void Save()
    {
        try
        {
            if (!Directory.Exists(DirPath))
                Directory.CreateDirectory(DirPath);
            // 只存非默认值
            var toSave = new Dictionary<string, PlayerShowPref>();
            foreach (var kv in _data)
            {
                if (kv.Value.ShowMe != false || kv.Value.ShowOthers != true)
                    toSave[kv.Key] = kv.Value;
            }
            var json = JsonConvert.SerializeObject(toSave, Formatting.Indented);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            TShock.Log.Error("[HouseRegion] 保存 houseshow.json 失败: " + ex);
        }
    }
}
