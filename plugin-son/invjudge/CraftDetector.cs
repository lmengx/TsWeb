using System.Text;
using Terraria;
using TShockAPI;

namespace InvJudge;

/// <summary>
/// 合成检测器：把每个玩家的背包/箱子槽位变化流聚合为「操作窗口」，
/// 在窗口空闲时反推可能的合成操作并校验扣材/产量比例合法性。
/// 第一期：仅记录日志（violations.log），不拦截不处罚。
/// </summary>
public static class CraftDetector
{
    private sealed class PlayerWindow
    {
        public int PlayerIndex;
        public long FirstChangeTick;          // 首个变化的环境时钟（Environment.TickCount64）
        public long LastChangeTick;
        public Dictionary<int, int> Delta = new(); // 物品类型 → 净变化（负=消耗，正=产出）
        public int ChangeCount;
    }

    private static readonly Dictionary<int, PlayerWindow> _windows = new();

    /// <summary>已完成进服背包同步的玩家（避免把登录同步风暴误判为操作）</summary>
    private static readonly HashSet<int> _synced = new();

    private static InvJudgeConfig _config = new();
    private static string _logDir = "";
    private static readonly object _logLock = new();
    private static int _tickCounter;

    private static string ViolationsPath => Path.Combine(_logDir, "violations.log");
    private static string PassesPath => Path.Combine(_logDir, "passes.log");
    private static string AuditPath => Path.Combine(_logDir, "audit.log");

    public static void Initialize(InvJudgeConfig config)
    {
        _config = config;
        _logDir = Path.Combine(TShock.SavePath, string.IsNullOrWhiteSpace(config.LogDir) ? "InvJudge" : config.LogDir);
        try
        {
            if (!Directory.Exists(_logDir))
                Directory.CreateDirectory(_logDir);
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[InvJudge] 创建日志目录失败: {ex.Message}");
        }

        InvLedger.ResetAll();
        _windows.Clear();
        _synced.Clear();
        _tickCounter = 0;
    }

    /// <summary>
    /// 收到一次玩家槽位变化（packet 5 解析后）。
    /// </summary>
    public static void OnPlayerSlot(TSPlayer player, int slot, int type, int stack)
    {
        if (!_config.Enabled || player == null || !player.Active || player.Index < 0)
            return;

        // 进服同步风暴保护：TShock 处理完最后一批槽位包后 HasSentInventory 才为 true，
        // 在此之前只建立账本基线，不聚合 delta。
        if (player.HasSentInventory)
            _synced.Add(player.Index);
        if (!_synced.Contains(player.Index))
        {
            var baselineWindow = new PlayerWindow { PlayerIndex = player.Index };
            InvLedger.ApplyPlayerSlot(player.Index, slot, type, stack, baselineWindow.Delta);
            return;
        }

        var window = GetWindow(player.Index);
        InvLedger.ApplyPlayerSlot(player.Index, slot, type, stack, window.Delta);
        Touch(window);
    }

    /// <summary>
    /// 收到一次箱子槽位变化（packet 32 解析后）。
    /// </summary>
    public static void OnChestItem(TSPlayer player, int chestId, int slot, int type, int stack)
    {
        if (!_config.Enabled || player == null || !player.Active || player.Index < 0)
            return;

        // 箱子包也受进服同步保护（登录后才会操作箱子）
        if (!_synced.Contains(player.Index))
            return;

        var window = GetWindow(player.Index);
        InvLedger.ApplyChestSlot(chestId, slot, type, stack, window.Delta);
        Touch(window);
    }

    public static void OnPlayerLeave(int playerIndex)
    {
        _windows.Remove(playerIndex);
        _synced.Remove(playerIndex);
        InvLedger.RemovePlayer(playerIndex);
    }

    /// <summary>
    /// 服务端 tick 钩子：周期性检查空闲窗口并触发检测（主线程，安全）。
    /// </summary>
    public static void OnServerTick()
    {
        if (!_config.Enabled)
            return;

        // 每 30 tick（0.5 秒）检查一次
        if (++_tickCounter % 30 != 0)
            return;

        long now = Environment.TickCount64;
        var idle = new List<int>();
        foreach (var (idx, w) in _windows)
        {
            if (now - w.LastChangeTick >= _config.DetectWindowMs)
                idle.Add(idx);
        }

        foreach (int idx in idle)
        {
            if (_windows.TryGetValue(idx, out var w))
            {
                _windows.Remove(idx);
                try
                {
                    FlushWindow(w);
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleError($"[InvJudge] 检测窗口处理异常: {ex.Message}");
                }
            }
        }
    }

    private static PlayerWindow GetWindow(int playerIndex)
    {
        if (!_windows.TryGetValue(playerIndex, out var w))
        {
            w = new PlayerWindow { PlayerIndex = playerIndex };
            _windows[playerIndex] = w;
        }
        return w;
    }

    private static void Touch(PlayerWindow w)
    {
        long now = Environment.TickCount64;
        if (w.ChangeCount == 0)
            w.FirstChangeTick = now;
        w.LastChangeTick = now;
        w.ChangeCount++;
    }

    /// <summary>
    /// 窗口空闲后执行合成校验。
    /// </summary>
    private static void FlushWindow(PlayerWindow w)
    {
        var consumed = new Dictionary<int, int>();
        var produced = new Dictionary<int, int>();

        foreach (var (type, delta) in w.Delta)
        {
            if (type <= 0) continue;
            if (delta < 0) consumed[type] = -delta;
            else if (delta > 0) produced[type] = delta;
        }

        // 纯移动/存取（类型净变化为 0）或没有任何操作 → 直接结束
        if (consumed.Count == 0 && produced.Count == 0)
            return;

        var player = TShock.Players[w.PlayerIndex];
        string playerName = player?.Name ?? $"Player{w.PlayerIndex}";
        int accountId = player?.Account?.ID ?? 0;

        // 尝试匹配配方
        var matches = RecipeIndex.MatchCraft(consumed, produced);

        if (matches.Count == 0)
        {
            // 无匹配配方：可能是拾取/奖励/微光产物/存取箱等合法行为，也可能是不明物品生成。
            // 仅在开启「记录仅产出提示」且产物是微光可分解物品时记录（刷微光分解链的早期信号）。
            if (_config.LogProduceOnly && produced.Count > 0 && consumed.Count == 0)
            {
                var shimmerProduced = produced.Where(kv => RecipeIndex.IsShimmerDecraftableItem(kv.Key)).ToList();
                foreach (var (type, count) in shimmerProduced)
                {
                    WriteViolation(
                        playerName, accountId, w,
                        $"仅产出无消耗: {count}x {RecipeIndex.ItemName(type)} [微光可分解物品]",
                        $"无法匹配任何配方; 产物 {count}x {RecipeIndex.ItemName(type)} (可微光分解) 无对应扣材");
                }
            }
            return;
        }

        // 有配方匹配：校验比例
        bool anyViolation = false;
        foreach (var m in matches)
        {
            bool strict = _config.ShimmerStrictMode && m.Info.ShimmerDecraftable;
            double tolerance = strict ? 1.0 : Math.Max(1.0, _config.NormalRatioTolerance);

            bool violated = m.Ratio > tolerance + 1e-9;

            if (violated)
            {
                anyViolation = true;
                WriteViolation(
                    playerName, accountId, w,
                    $"合成比例异常: 扣材{ConcatMap(consumed)} / 产量{ConcatMap(produced)}",
                    $"配方[{m.Info.RecipeIndex}] {m.Info}: 扣材可支撑 {m.CraftByConsumed} 次, 实际产出 {m.CraftByProduced} 次 (倍率 {m.Ratio:F2}{(strict ? ", 微光可分解严格模式" : "")})");
            }
            else if (_config.LogPasses)
            {
                WritePass(
                    playerName, accountId, w,
                    $"合法合成: {m.Info.ResultName} x{m.ProducedCount} (扣材{ConcatMap(consumed)}, 配方[{m.Info.RecipeIndex}])");
            }
        }

        // 有消耗无匹配产物 → 扣材了但没有对应的配方产物（异常消耗，可能是 bug 利用后清场）
        if (!anyViolation && produced.Count == 0 && consumed.Count > 0)
        {
            WriteViolation(
                playerName, accountId, w,
                $"扣材无产物: {ConcatMap(consumed)}",
                $"消耗了材料但窗口内没有任何配方产物产出");
        }
    }

    private static string ConcatMap(Dictionary<int, int> map)
    {
        if (map.Count == 0) return "无";
        var sb = new StringBuilder();
        foreach (var (type, count) in map.OrderBy(kv => kv.Key))
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append($"{count}x {RecipeIndex.ItemName(type)}");
        }
        return sb.ToString();
    }

    private static void WriteViolation(string playerName, int accountId, PlayerWindow w, string summary, string detail)
    {
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 玩家={playerName}(acc:{accountId}) 窗口={w.ChangeCount}次变化 {summary} | {detail}";
        WriteLog(ViolationsPath, line);
        TShock.Log.ConsoleError($"[InvJudge][违规] {line}");
    }

    private static void WritePass(string playerName, int accountId, PlayerWindow w, string summary)
    {
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 玩家={playerName}(acc:{accountId}) {summary}";
        WriteLog(PassesPath, line);
    }

    private static void WriteLog(string path, string line)
    {
        try
        {
            lock (_logLock)
            {
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[InvJudge] 写日志失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 审计入口（命令/加载等事件）
    /// </summary>
    public static void Audit(string message)
    {
        WriteLog(AuditPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
    }
}
