using Newtonsoft.Json;

namespace InvJudge;

/// <summary>
/// InvJudge 配置。
/// 配置文件路径：TShock.SavePath/InvJudge/config.json
/// 第一期行为：仅记录日志，不拦截、不封禁。
/// </summary>
public sealed class InvJudgeConfig
{
    /// <summary>总开关</summary>
    [JsonProperty("启用")]
    public bool Enabled { get; set; } = true;

    /// <summary>合成检测时间窗口（毫秒）：把该窗口内的背包/箱子槽位变化聚合为一次操作再校验</summary>
    [JsonProperty("检测窗口毫秒")]
    public int DetectWindowMs { get; set; } = 1500;

    /// <summary>微光可分解物品严格模式：产物为可微光分解物品时，产量必须严格等于扣材可支撑的合成次数，任何超额即记录</summary>
    [JsonProperty("微光可分解严格模式")]
    public bool ShimmerStrictMode { get; set; } = true;

    /// <summary>普通合成允许的产量倍率容差（>=1）。例：1.0 表示产量必须精确等于扣材支撑量；1.1 允许 10% 溢出（防误报）</summary>
    [JsonProperty("普通合成产量容差")]
    public double NormalRatioTolerance { get; set; } = 1.05;

    /// <summary>日志输出目录（相对 TShock.SavePath）</summary>
    [JsonProperty("日志目录")]
    public string LogDir { get; set; } = "InvJudge";

    /// <summary>是否输出每次合成通过的详细日志（调试用，默认关）</summary>
    [JsonProperty("记录通过日志")]
    public bool LogPasses { get; set; } = false;

    /// <summary>
    /// 是否记录「仅产出无消耗」提示（默认关）。
    /// 该分支对拾取地面物/BOSS掉落/任务奖励等合法「只增不减」行为存在误报可能，
    /// 核心的手机端 1 材料做 2 物品漏洞已由合成比例校验覆盖，此开关仅作辅助观察。
    /// </summary>
    [JsonProperty("记录仅产出提示")]
    public bool LogProduceOnly { get; set; } = false;

    public static InvJudgeConfig Load()
    {
        var path = Path.Combine(TShockAPI.TShock.SavePath, "InvJudge", "config.json");
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (File.Exists(path))
            {
                var cfg = JsonConvert.DeserializeObject<InvJudgeConfig>(File.ReadAllText(path));
                if (cfg != null)
                    return cfg;
            }
        }
        catch (Exception ex)
        {
            TShockAPI.TShock.Log.ConsoleError($"[InvJudge] 读取配置失败，使用默认配置: {ex.Message}");
        }

        var fresh = new InvJudgeConfig();
        try
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(fresh, Formatting.Indented));
        }
        catch (Exception ex)
        {
            TShockAPI.TShock.Log.ConsoleError($"[InvJudge] 写入默认配置失败: {ex.Message}");
        }
        return fresh;
    }
}
