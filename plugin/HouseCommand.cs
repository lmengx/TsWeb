using Newtonsoft.Json;
using System.Text;
using Terraria;
using TShockAPI;
using TShockData;

namespace HouseRegion;

/// <summary>
/// 领地附加指令（玩家进入领地时触发）。
/// 每条指令包含四个属性：转义、以玩家自身执行、是否越过权限。
/// </summary>
public class HouseCommandConfig
{
    /// <summary>是否启用该指令</summary>
    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 指令文本。支持占位符：
    ///   {player} 进入玩家名、{x}/{y} 进入玩家格坐标、{time} 游戏内时间(HH:mm)、{house} 领地名称。
    /// </summary>
    [JsonProperty("command")]
    public string Command { get; set; } = "";

    /// <summary>
    /// 转义字段：开启时对占位符替换进来的文本值做转义（空白折叠为下划线、剔除引号），
    /// 防止玩家名/领地名中的特殊字符破坏指令结构；关闭时原样替换。
    /// 数字占位符（{x}/{y}/{time}）不受影响。
    /// </summary>
    [JsonProperty("escape")]
    public bool Escape { get; set; } = true;

    /// <summary>以玩家自身执行：通过 Commands.HandleCommand 以进入玩家身份执行</summary>
    [JsonProperty("asSelf")]
    public bool AsSelf { get; set; } = true;

    /// <summary>是否可越过权限：开启时通过 BypassHelper 跳过权限检查（玩家身份不变）</summary>
    [JsonProperty("bypass")]
    public bool BypassPermission { get; set; } = false;
}

/// <summary>
/// 领地进入指令执行器：玩家进入领地时按顺序执行该领地配置的指令。
/// 由 HouseCore.OnUpdate 的进入事件调用（定时器线程），指令实际执行切到游戏主线程。
/// </summary>
public static class HouseCommandRunner
{
    /// <summary>玩家进入领地触发：快照指令列表后切主线程逐条执行（被驱离玩家不会走到这里）。</summary>
    public static void ExecuteOnEnter(TSPlayer player, House house)
    {
        if (player == null || house == null)
            return;
        var commands = house.Commands;
        if (commands == null || commands.Count == 0)
            return;

        var snapshot = commands
            .Where(c => c != null && c.Enabled && !string.IsNullOrWhiteSpace(c.Command))
            .ToList();
        if (snapshot.Count == 0)
            return;

        // OnUpdate 运行在 System.Timers.Timer 线程，指令（Commands.HandleCommand）会改游戏状态，
        // 必须切到游戏主线程执行，避免与主循环竞争。
        Main.QueueMainThreadAction(() =>
        {
            foreach (var c in snapshot)
            {
                try
                {
                    ExecuteOne(player, house, c);
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleError($"[房屋] 领地进入指令执行失败: {ex.Message}");
                }
            }
        });
    }

    private static void ExecuteOne(TSPlayer player, House house, HouseCommandConfig c)
    {
        // 排队期间玩家可能已离开/掉线
        if (player == null || !player.ConnectionAlive)
            return;

        var cmd = BuildCommand(player, house, c);
        if (string.IsNullOrEmpty(cmd))
            return;

        // Commands.HandleCommand 要求首字符为命令前缀（/ 或 .），未带则自动补 /
        if (cmd[0] != '/' && cmd[0] != '.')
            cmd = "/" + cmd;

        if (c.BypassPermission)
            BypassHelper.RunWithoutPermissionChecks(() => Commands.HandleCommand(player, cmd), player);
        else
            Commands.HandleCommand(player, cmd);
    }

    /// <summary>替换占位符并应用转义，生成最终指令文本。</summary>
    public static string BuildCommand(TSPlayer player, House house, HouseCommandConfig c)
    {
        var text = c.Command;
        if (string.IsNullOrEmpty(text))
            return "";

        var playerName = player.Name ?? "";
        var houseName = house.Name ?? "";
        if (c.Escape)
        {
            playerName = EscapeValue(playerName);
            houseName = EscapeValue(houseName);
        }

        text = text
            .Replace("{player}", playerName)
            .Replace("{x}", player.TileX.ToString())
            .Replace("{y}", player.TileY.ToString())
            .Replace("{time}", FormatGameTime())
            .Replace("{house}", houseName);
        return text.Trim();
    }

    /// <summary>转义文本值：空白折叠为下划线，剔除引号，避免破坏指令参数结构。</summary>
    private static string EscapeValue(string s)
    {
        if (string.IsNullOrEmpty(s))
            return s;
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch == '"' || ch == '\'')
                continue;
            sb.Append(char.IsWhiteSpace(ch) ? '_' : ch);
        }
        return sb.ToString();
    }

    /// <summary>Terraria 游戏内时间（HH:mm）：白天自 4:30 起，夜晚 +12 小时。</summary>
    private static string FormatGameTime()
    {
        double t = Main.time;
        int h = 4 + (int)(t / 3600.0);
        int m = (int)((t % 3600.0) / 60.0);
        if (!Main.dayTime)
            h = (h + 12) % 24;
        return $"{h:D2}:{m:D2}";
    }
}
