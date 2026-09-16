using Newtonsoft.Json;
using System.Text;
using TShockAPI;

namespace PeaceMode;

/// <summary>和平模式配置文件（tshock/PeaceMode.json）</summary>
public class Config
{
    /// <summary>配置文件路径</summary>
    public static readonly string FilePath = Path.Combine(TShock.SavePath, "PeaceMode.json");

    /// <summary>插件加载时是否自动开启和平模式（默认 false）</summary>
    [JsonProperty("Enabled", Order = 1)]
    public bool Enabled { get; set; } = false;

    /// <summary>开启和平模式时是否立即清空场上所有敌对 NPC（默认 true）</summary>
    [JsonProperty("ClearExistingNpcsOnEnable", Order = 2)]
    public bool ClearExistingNpcsOnEnable { get; set; } = true;

    /// <summary>
    /// 是否连城镇 NPC（商人、护士等友方 NPC）一起禁止生成。
    /// 默认 false 保留城镇 NPC；设为 true 时连城镇 NPC 也会被禁止生成并清除。
    /// </summary>
    [JsonProperty("IncludeTownNpcs", Order = 3)]
    public bool IncludeTownNpcs { get; set; } = false;

    /// <summary>是否禁止全部事件：入侵/血月/日食/南瓜月/霜月/史莱姆雨/旧日军团/沙尘暴（默认 true）</summary>
    [JsonProperty("BanEvents", Order = 4)]
    public bool BanEvents { get; set; } = true;

    /// <summary>是否禁止下雨（默认 true）</summary>
    [JsonProperty("BanRain", Order = 5)]
    public bool BanRain { get; set; } = true;

    /// <summary>是否禁止陨石坠落（默认 true）</summary>
    [JsonProperty("BanMeteor", Order = 6)]
    public bool BanMeteor { get; set; } = true;

    public void Write()
    {
        using var fs = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.Write);
        using var sw = new StreamWriter(fs, new UTF8Encoding(false));
        sw.Write(JsonConvert.SerializeObject(this, Formatting.Indented));
    }

    public static Config Read()
    {
        if (!File.Exists(FilePath))
        {
            var config = new Config();
            config.Write();
            return config;
        }

        try
        {
            return JsonConvert.DeserializeObject<Config>(File.ReadAllText(FilePath)) ?? new Config();
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[PeaceMode] 配置文件解析失败，使用默认配置: {ex.Message}");
            return new Config();
        }
    }
}
