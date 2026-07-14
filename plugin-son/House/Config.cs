using Newtonsoft.Json;
using TShockAPI;
using TShockAPI.Hooks;

namespace HouseRegion;

public class Config
{
    private static Config? _instance;
    private static readonly string FilePath = Path.Combine(TShock.SavePath, "HouseRegion.json");

    [JsonProperty("进出房屋提示")]
    public bool JoinRegionText { get; set; } = true;

    [JsonProperty("房屋最大面积")]
    public int HouseMaxSize { get; set; } = 1000;

    [JsonProperty("房屋最小宽度")]
    public int MinWidth { get; set; } = 15;

    [JsonProperty("房屋最小高度")]
    public int MinHeight { get; set; } = 10;

    [JsonProperty("房屋最大数量")]
    public int HouseMaxNumber { get; set; } = 2;

    [JsonProperty("禁止锁房屋")]
    public bool LimitLockHouse { get; set; } = false;

    [JsonProperty("保护宝石锁")]
    public bool ProtectiveGemstoneLock { get; set; } = false;

    [JsonProperty("始终保护箱子")]
    public bool ProtectiveChest { get; set; } = true;

    [JsonProperty("冻结警告破坏者")]
    public bool WarningSpoiler { get; set; } = true;

    [JsonProperty("禁止分享所有者")]
    public bool ProhibitSharingOwner { get; set; } = false;

    [JsonProperty("禁止分享使用者")]
    public bool ProhibitSharingUser { get; set; } = false;

    [JsonProperty("禁止所有者修改使用者")]
    public bool ProhibitOwnerModifyingUser { get; set; } = true;

    [JsonProperty("禁止TP房屋")]
    public bool ProhibitTPHouse { get; set; } = false;

    [JsonProperty("禁止出生点圈地")]
    public bool ProhibitSpawnClaim { get; set; } = false;

    public static Config Instance
    {
        get
        {
            if (_instance == null)
            {
                Load();
            }
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
                Save();
            }
        }
        catch
        {
            _instance = new Config();
        }

        GeneralHooks.ReloadEvent += OnReload;
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
        {
            Directory.CreateDirectory(dir);
        }
        var json = JsonConvert.SerializeObject(_instance ?? new Config(), Formatting.Indented);
        File.WriteAllText(FilePath, json);
    }
}
