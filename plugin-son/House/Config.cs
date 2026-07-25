using Newtonsoft.Json;
using TShockAPI;
using TShockAPI.Hooks;

namespace HouseRegion;

public class Config
{
    private static Config? _instance;
    private static bool _reloadRegistered;
    private static readonly string FilePath = Path.Combine(TShock.SavePath, "HouseRegion.json");

    [JsonProperty("房屋最小宽度")]
    public int MinWidth { get; set; } = 15;

    [JsonProperty("房屋最小高度")]
    public int MinHeight { get; set; } = 10;

    public static Config Instance
    {
        get
        {
            if (_instance == null)
                Load();
            return _instance!;
        }
    }

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                _instance = JsonConvert.DeserializeObject<Config>(json) ?? new Config();
            }
            else
            {
                _instance = new Config();
            }
            // 保存回文件：自动补齐新字段
            Save();
        }
        catch
        {
            _instance = new Config();
        }

        // 仅首次 Load 时注册 ReloadEvent，防止热重载后重复注册
        if (!_reloadRegistered)
        {
            GeneralHooks.ReloadEvent += OnReload;
            _reloadRegistered = true;
        }
    }

    private static void OnReload(ReloadEventArgs args)
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                _instance = JsonConvert.DeserializeObject<Config>(json) ?? new Config();
            }
            args.Player.SendSuccessMessage("[HouseRegion] 配置已重新加载。");
        }
        catch (Exception ex)
        {
            TShock.Log.Error("[HouseRegion] 配置重载失败: " + ex);
        }
    }

    public static void Save()
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        var json = JsonConvert.SerializeObject(_instance ?? new Config(), Formatting.Indented);
        File.WriteAllText(FilePath, json);
    }
}
