using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Rests;
using TShockAPI;
using TShockAPI.DB;
using Terraria;
using Terraria.ID;

namespace TShockData
{
    // ═══════════════════════════════════════════════════
    // 模型定义
    // ═══════════════════════════════════════════════════

    /// <summary>任务配置文件根</summary>
    public class TaskConfig
    {
        [JsonProperty("tasks")]
        public List<AutoTask> Tasks { get; set; } = new();
    }

    /// <summary>单个自动任务</summary>
    public class AutoTask
    {
        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>manual / interval / daily</summary>
        [JsonProperty("triggerMode")]
        public string TriggerMode { get; set; } = "manual";

        [JsonProperty("intervalSeconds")]
        public int IntervalSeconds { get; set; } = 0;

        /// <summary>格式 "HH:mm"</summary>
        [JsonProperty("dailyTime")]
        public string DailyTime { get; set; } = "00:00";

        [JsonProperty("condition")]
        public TaskCondition Condition { get; set; } = new();

        /// <summary>sequential / concurrent</summary>
        [JsonProperty("execMode")]
        public string ExecMode { get; set; } = "sequential";

        [JsonProperty("commands")]
        public List<string> Commands { get; set; } = new();

        // ===== 运行期状态 =====
        // 上次任意触发（手动/间隔/每日均更新），用于 interval 判断与前端显示。
        //
        // 本字段虽然看着像"显示用"，实际是**调度依据**：interval 的到期判定是
        // (Now - LastRunAt) >= IntervalSeconds。它原先挂 [JsonIgnore] 不落盘，每次加载
        // （冷启动 / 热重载 / reload）后回到 DateTime.MinValue，于是加载后的第一次 tick
        // 就把所有 interval 任务判成"已到期"、立刻执行一次——这就是"每次热重载都会自动
        // 清理一次地面"的根因。故必须持久化，且必须经下面的 LastRunAtJson 代理落盘。
        [JsonIgnore]
        public DateTime LastRunAt { get; set; } = DateTime.MinValue;

        /// <summary>
        /// LastRunAt 的落盘代理：JSON 中的 null 表示"从未执行过"（对应 DateTime.MinValue）。
        ///
        /// 为什么不直接给 LastRunAt 挂 [JsonProperty]，而要这个可空代理：
        /// Newtonsoft 把 JSON null 绑到**非可空** DateTime 属性时会直接抛
        /// "Error converting value {null} to type 'System.DateTime'"，而列表接口对从未执行的
        /// 任务正是返回 null；前端 getTask 拿到后编辑保存会原样回传，那样保存会以 500 失败。
        /// 可空代理让 null 与"字段缺失"（旧配置升级）都能安全落到 MinValue，
        /// 同时让 get 与 list 两个接口对"从未执行"的表示统一为 null。
        /// </summary>
        [JsonProperty("lastRunAt")]
        public DateTime? LastRunAtJson
        {
            get => LastRunAt == DateTime.MinValue ? null : LastRunAt;
            set => LastRunAt = value ?? DateTime.MinValue;
        }
        // 上次每日定时触发的日期（落盘：重启后仍能记住当天已执行），仅 daily 模式使用
        [JsonProperty("lastDailyRunAt")]
        public DateTime LastDailyRunAt { get; set; } = DateTime.MinValue;
        [JsonIgnore]
        public string LastRunStatus { get; set; } = "";
        [JsonIgnore]
        public int RunCount { get; set; } = 0;
        [JsonIgnore]
        public bool Running { get; set; } = false;
    }

    /// <summary>执行前提（单一条件 + 取否）</summary>
    public class TaskCondition
    {
        /// <summary>always / online_count / boss_defeated / player_online</summary>
        [JsonProperty("type")]
        public string Type { get; set; } = "always";

        [JsonProperty("not")]
        public bool Not { get; set; } = false;

        [JsonProperty("params")]
        public ConditionParams Params { get; set; } = new();
    }

    public class ConditionParams
    {
        [JsonProperty("min")] public int Min { get; set; } = 0;
        [JsonProperty("max")] public int Max { get; set; } = 9999;
        [JsonProperty("bossNames")] public List<string> BossNames { get; set; } = new();
        [JsonProperty("playerNames")] public List<string> PlayerNames { get; set; } = new();
    }

    /// <summary>单条命令执行结果</summary>
    public class TaskCommandLog
    {
        [JsonProperty("index")] public int Index { get; set; }
        [JsonProperty("command")] public string Command { get; set; } = "";
        [JsonProperty("success")] public bool Success { get; set; }
        [JsonProperty("output")] public string Output { get; set; } = "";
        [JsonProperty("error")] public string Error { get; set; } = "";
    }

    /// <summary>一次任务执行记录</summary>
    public class TaskExecutionLog
    {
        public long Id { get; set; }
        public string TaskId { get; set; } = "";
        public string TaskName { get; set; } = "";
        public DateTime TriggeredAt { get; set; }
        public string TriggerMode { get; set; } = "";
        public bool ConditionResult { get; set; }
        public bool Skipped { get; set; }
        public string Status { get; set; } = "";
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public long DurationMs { get; set; }
        public List<TaskCommandLog> Commands { get; set; } = new();
        public string ErrorSummary { get; set; } = "";
    }

    // ═══════════════════════════════════════════════════
    // 任务调度器
    // ═══════════════════════════════════════════════════

    public static class TaskScheduler
    {
        private static readonly string ConfigPath = Path.Combine(TShock.SavePath, "TSWeb", "Tasks", "tasks.json");
        private static System.Timers.Timer? _timer;
        private static bool _initialized;
        /// <summary>串行化配置写盘（定时器线程与 REST 线程都会写，见 SaveConfig）。</summary>
        private static readonly object _saveLock = new object();

        public static List<AutoTask> Tasks { get; private set; } = new();

        // BOSS 名称 → NPCID（与 BossProgress.BossNames 保持一致）
        private static readonly Dictionary<string, int> BossNpcIds = new Dictionary<string, int>
        {
            { "史莱姆王", NPCID.KingSlime },
            { "克苏鲁之眼", NPCID.EyeofCthulhu },
            { "世界吞噬者", NPCID.EaterofWorldsHead },
            { "克苏鲁之脑", NPCID.BrainofCthulhu },
            { "蜂后", NPCID.QueenBee },
            { "巨鹿", NPCID.Deerclops },
            { "骷髅王", NPCID.SkeletronHead },
            { "血肉墙", NPCID.WallofFlesh },
            { "史莱姆皇后", NPCID.QueenSlimeBoss },
            { "毁灭者", NPCID.TheDestroyer },
            { "机械骷髅王", NPCID.SkeletronPrime },
            { "双子魔眼", NPCID.Retinazer },
            { "世纪之花", NPCID.Plantera },
            { "石巨人", NPCID.Golem },
            { "猪龙鱼公爵", NPCID.DukeFishron },
            { "光之女皇", NPCID.HallowBoss },
            { "拜月教教徒", NPCID.CultistBoss },
            { "月亮领主", NPCID.MoonLordCore }
        };

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            LoadConfig();
            CreateLogTable();

            _timer = new System.Timers.Timer(1000);
            _timer.Elapsed += OnSecondTick;
            _timer.AutoReset = true;
            _timer.Start();

            TShock.Log.ConsoleInfo($"[TSWeb] 自动任务系统已启动，共 {Tasks.Count} 个任务");
        }

        public static void Dispose()
        {
            if (!_initialized) return;
            _initialized = false;

            if (_timer != null)
            {
                _timer.Stop();
                _timer.Dispose();
                _timer = null;
            }

            TShock.Log.ConsoleInfo("[TSWeb] 自动任务系统已停止");
        }

        public static void Reload()
        {
            LoadConfig();
            TShock.Log.ConsoleInfo($"[TSWeb] 自动任务配置已重新加载，共 {Tasks.Count} 个任务");
        }

        // ═══════════════════════════════════════════════════
        // 配置读写
        // ═══════════════════════════════════════════════════

        private static void LoadConfig()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var config = JsonConvert.DeserializeObject<TaskConfig>(json);
                    Tasks = config?.Tasks ?? new List<AutoTask>();
                }
                else
                {
                    Tasks = new List<AutoTask>();
                    SaveConfig();
                }

                // 补种缺失的计时起点（见 SeedMissingLastRunAt 的说明）
                SeedMissingLastRunAt();
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 加载自动任务配置失败: {ex.Message}");
                Tasks = new List<AutoTask>();
            }
        }

        /// <summary>
        /// 给没有历史计时起点的任务补上"当前时间"作为起点，并在确实补种时落盘。
        ///
        /// 这是 D1 修法的第二步（第一步是把 LastRunAt 落盘，只做第一步并不够）：
        /// 没有起点指 LastRunAt == DateTime.MinValue，即旧配置升级后首次加载（文件里还没有
        /// 这个字段）、或任务从未执行过。此时**不能**按"已到期"处理——MinValue 距今约 6.4e10 秒，
        /// 恒大于任何 IntervalSeconds，于是加载后的第一次 tick 会立刻执行一次：
        /// 清理地面这类任务是真的清一次地面，`/off` 这类任务则是"开服即关服"。
        /// 新版 cordis（tsweb-cordis/plugins/task-scheduler）对同一缺陷的修法是
        /// TaskSchedulerModel.SeedMissingLastRunAt，此处与之对齐。
        ///
        /// 补种必须落盘：否则每次加载都要重新补种，间隔大于两次加载间隔的任务将永远不执行
        /// （这正是"加载后一律重新计时"方案的固有代价，补种的持久化恰好避开了它）。
        /// </summary>
        private static void SeedMissingLastRunAt()
        {
            var now = DateTime.Now;
            int seeded = 0;
            foreach (var t in Tasks)
            {
                if (t.LastRunAt != DateTime.MinValue) continue;
                t.LastRunAt = now;
                seeded++;
            }

            if (seeded > 0)
            {
                SaveConfig();
                TShock.Log.ConsoleInfo($"[TSWeb] {seeded} 个自动任务缺少计时起点，已以当前时间补种（本次加载不会立刻触发）");
            }
        }

        /// <summary>
        /// 记录任务的本次触发时刻并**立即落盘**。
        ///
        /// 为什么必须落盘：interval 的到期判定是 (Now - LastRunAt) >= IntervalSeconds。
        /// LastRunAt 以前是 [JsonIgnore] 的纯内存字段，每次加载后恒为 DateTime.MinValue，
        /// 使得插件每次加载（冷启动、热重载）后的第一次 tick 就把所有 interval 任务判为到期、
        /// 立刻执行一次——「自动清理地面物品」每次热重载都清一次地面就是这么来的。
        /// 记完不落盘同样不行：内存里更新了、磁盘上还是旧值，下一次加载照样从头算。
        /// </summary>
        /// <param name="markDaily">是否同时记"今天已执行"（仅触发时刻需要，见下方说明）</param>
        private static void PersistRunTime(AutoTask task, bool markDaily)
        {
            task.LastRunAt = DateTime.Now;

            // 每日任务的"今天已跑过"只在**触发时刻**记，不在执行结束时记：
            // 跨零点执行完的任务若在结束时记，日期会落到第二天，把第二天本该跑的那次挡掉。
            if (markDaily && task.TriggerMode == "daily")
                task.LastDailyRunAt = task.LastRunAt;

            SaveConfig();
        }

        private static void SaveConfig()
        {
            // 现在有两个线程会写这份配置：定时器线程（任务触发/结束时记时刻）与 REST 线程
            // （面板保存/删除任务）。File.WriteAllText 不是原子操作，并发写会互相覆盖或抛
            // IOException，所以把写文件串行化。
            lock (_saveLock)
            {
                try
                {
                    var dir = Path.GetDirectoryName(ConfigPath);
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    var json = JsonConvert.SerializeObject(new TaskConfig { Tasks = Tasks }, Formatting.Indented);
                    File.WriteAllText(ConfigPath, json);
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleError($"[TSWeb] 保存自动任务配置失败: {ex.Message}");
                }
            }
        }

        // ═══════════════════════════════════════════════════
        // 调度逻辑
        // ═══════════════════════════════════════════════════

        private static void OnSecondTick(object sender, System.Timers.ElapsedEventArgs e)
        {
            foreach (var task in Tasks.ToList())
            {
                if (!task.Enabled || task.Running)
                    continue;

                bool due = task.TriggerMode switch
                {
                    "interval" => task.IntervalSeconds > 0 &&
                                  (DateTime.Now - task.LastRunAt).TotalSeconds >= task.IntervalSeconds,
                    "daily" => IsDailyTimeDue(task),
                    _ => false
                };

                if (due)
                {
                    // 触发时刻先记一次并落盘（见 PersistRunTime 的说明）。
                    // 任务本身可能跑很久（模板里的清理任务是 /wait 60000 之后才真正清），
                    // 这段时间里若发生热重载/重启，落盘的时刻能保证它不会被立刻再触发一遍。
                    PersistRunTime(task, markDaily: true);
                    Task.Run(() => ExecuteTask(task, force: false));
                }
            }
        }

        /// <summary>每天 HH:mm 触发，且当天只触发一次（基于 LastDailyRunAt，与手动/间隔执行解耦）</summary>
        private static bool IsDailyTimeDue(AutoTask task)
        {
            if (string.IsNullOrEmpty(task.DailyTime))
                return false;

            if (DateTime.Now.ToString("HH:mm") != task.DailyTime)
                return false;

            if (task.LastDailyRunAt.Date == DateTime.Today)
                return false;

            return true;
        }

        // ═══════════════════════════════════════════════════
        // 条件求值
        // ═══════════════════════════════════════════════════

        private static bool EvaluateCondition(TaskCondition condition)
        {
            bool result = condition.Type switch
            {
                "always" => true,
                "online_count" => IsOnlineCountInRange(condition.Params),
                "boss_defeated" => AreBossesDefeated(condition.Params),
                "player_online" => ArePlayersOnline(condition.Params),
                _ => true
            };

            return condition.Not ? !result : result;
        }

        private static int OnlinePlayerCount =>
            TShock.Players.Count(p => p != null && p.Active);

        private static bool IsOnlineCountInRange(ConditionParams p)
        {
            int count = OnlinePlayerCount;
            return count >= p.Min && count <= p.Max;
        }

        private static bool AreBossesDefeated(ConditionParams p)
        {
            if (p.BossNames == null || p.BossNames.Count == 0)
                return false;

            foreach (var name in p.BossNames)
            {
                if (!BossNpcIds.TryGetValue(name, out var npcId))
                    return false;
                if (BossProgress.GetKillCount(npcId) <= 0)
                    return false;
            }
            return true;
        }

        private static bool ArePlayersOnline(ConditionParams p)
        {
            if (p.PlayerNames == null || p.PlayerNames.Count == 0)
                return false;

            foreach (var name in p.PlayerNames)
            {
                if (!TShock.Players.Any(pl => pl != null && pl.Active && pl.Name == name))
                    return false;
            }
            return true;
        }

        // ═══════════════════════════════════════════════════
        // 执行
        // ═══════════════════════════════════════════════════

        /// <summary>
        /// 执行任务。
        /// force=false（定时触发）：判断条件，不满足则记录 skipped。
        /// force=true（手动强制）：跳过条件判断直接执行。
        /// </summary>
        public static void ExecuteTask(AutoTask task, bool force)
        {
            if (task.Running) return;
            task.Running = true;
            task.RunCount++;

            var log = new TaskExecutionLog
            {
                TaskId = task.Id,
                TaskName = task.Name,
                TriggeredAt = DateTime.Now,
                TriggerMode = force ? "manual" : task.TriggerMode,
                StartedAt = DateTime.Now
            };

            bool logSaved = false;
            try
            {
                // 1. 条件判断
                bool pass = true;
                if (!force)
                {
                    pass = EvaluateCondition(task.Condition);
                    log.ConditionResult = pass;
                    if (!pass)
                    {
                        log.Status = "skipped";
                        log.Skipped = true;
                        log.CompletedAt = DateTime.Now;
                        log.DurationMs = 0;
                        InsertLog(log);
                        logSaved = true;
                        task.LastRunStatus = "skipped";
                        return;
                    }
                }
                else
                {
                    log.ConditionResult = true;
                }

                // 2. 执行命令
                log.Status = "running";

                if (task.ExecMode == "concurrent")
                {
                    var results = task.Commands
                        .Select((cmd, i) => Task.Run(() => ExecuteSingleCommand(cmd, i)))
                        .ToArray();
                    Task.WaitAll(results);
                    log.Commands.AddRange(results.Select(r => r.Result));
                }
                else
                {
                    for (int i = 0; i < task.Commands.Count; i++)
                        log.Commands.Add(ExecuteSingleCommand(task.Commands[i], i));
                }

                // 3. 完成
                log.Status = log.Commands.Any(c => !c.Success) ? "failed" : "success";
                if (log.Status == "failed")
                {
                    log.ErrorSummary = string.Join("; ", log.Commands
                        .Where(c => !c.Success && !string.IsNullOrEmpty(c.Error))
                        .Select(c => $"[{c.Command}] {c.Error}"));
                }

                task.LastRunStatus = log.Status;
            }
            catch (Exception ex)
            {
                log.Status = "failed";
                log.ErrorSummary = ex.Message;
                task.LastRunStatus = "failed";
                TShock.Log.ConsoleError($"[TSWeb] 任务 {task.Name} 执行异常: {ex.Message}");
            }
            finally
            {
                log.CompletedAt = DateTime.Now;
                log.DurationMs = (long)(log.CompletedAt.Value - log.StartedAt.Value).TotalMilliseconds;
                if (!logSaved)
                    InsertLog(log);
                // 执行结束再记一次并落盘。这条路径同时覆盖定时触发与面板手动触发，
                // 所以在这里落盘也保证了"手动跑过一次"会把 interval 的计时起点一并推后
                PersistRunTime(task, markDaily: false);
                task.Running = false;
            }
        }

        private static TaskCommandLog ExecuteSingleCommand(string cmd, int index)
        {
            var result = new TaskCommandLog { Index = index, Command = cmd };

            // 命令规范化：不以执行符号或静默符号开头时，自动追加执行符号前缀
            var normalized = NormalizeCommand(cmd);
            result.Command = normalized;

            // 拦截虚拟命令 wait <毫秒>（可带任意前缀符号）
            var match = MatchWaitCommand(normalized);
            if (match.Success)
            {
                try
                {
                    int ms = int.Parse(match.Groups[1].Value);
                    if (ms > 0) Thread.Sleep(ms);
                    result.Success = true;
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Error = ex.Message;
                }
                return result;
            }

            try
            {
                var group = TShock.Groups.GetGroupByName("superadmin");
                var tr = new TSRestPlayer("TaskExecutor", group);
                Commands.HandleCommand(tr, normalized);

                var output = tr.GetCommandOutput();
                if (output != null && output.Count > 0)
                    result.Output = string.Join("\n", output.Where(l => !string.IsNullOrEmpty(l)));

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// 命令规范化：如果命令不以执行符号或静默执行符号开头，则追加执行符号作为前缀。
        /// 这样任务中可以直接写 "broadcast hello" 而无需手动加 "/"。
        /// </summary>
        private static string NormalizeCommand(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd))
                return cmd;

            var trimmed = cmd.Trim();
            var specifier = string.IsNullOrWhiteSpace(Commands.Specifier) ? "/" : Commands.Specifier;
            var silentSpecifier = string.IsNullOrWhiteSpace(Commands.SilentSpecifier) ? "." : Commands.SilentSpecifier;

            if (trimmed.StartsWith(specifier, StringComparison.Ordinal) ||
                trimmed.StartsWith(silentSpecifier, StringComparison.Ordinal))
                return trimmed;

            return specifier + trimmed;
        }

        /// <summary>
        /// 匹配 wait 虚拟命令（不受执行符号配置影响）：wait N / wait N 秒
        /// </summary>
        private static Match MatchWaitCommand(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd))
                return Match.Empty;

            // 去掉前缀符号（执行符号或静默符号）后匹配命令体
            var body = cmd.Trim();
            foreach (var prefix in new[] { Commands.Specifier, Commands.SilentSpecifier })
            {
                if (!string.IsNullOrEmpty(prefix) && body.StartsWith(prefix, StringComparison.Ordinal))
                {
                    body = body.Substring(prefix.Length);
                    break;
                }
            }

            return Regex.Match(body, @"^wait\s+(\d+)$", RegexOptions.IgnoreCase);
        }

        // ═══════════════════════════════════════════════════
        // 执行记录（SQLite）
        // ═══════════════════════════════════════════════════

        private static void CreateLogTable()
        {
            // Id 用 SqlCompat.AutoIncrementIdColumn：SQLite AUTOINCREMENT / MySQL AUTO_INCREMENT。
            // CreatedAt 不再用 SQLite 专属的 datetime('now','localtime') 默认值，
            // 改由 InsertLog 从 C# 传入（两库通用）。
            // 其余列用 VARCHAR：MySQL 的 TEXT 列不能直接作索引，VARCHAR 两库通用；
            // 存量 SQLite 库已存在表时本语句为 no-op，不破坏旧表。
            TShock.DB.Query($@"
                CREATE TABLE IF NOT EXISTS task_execution_logs (
                    Id {SqlCompat.AutoIncrementIdColumn},
                    TaskId VARCHAR(64) NOT NULL,
                    TaskName VARCHAR(255) NOT NULL,
                    TriggeredAt VARCHAR(19) NOT NULL,
                    TriggerMode VARCHAR(16) NOT NULL,
                    ConditionResult INTEGER NOT NULL,
                    Skipped INTEGER NOT NULL DEFAULT 0,
                    Status VARCHAR(16) NOT NULL,
                    StartedAt VARCHAR(19),
                    CompletedAt VARCHAR(19),
                    DurationMs INTEGER DEFAULT 0,
                    CommandResults TEXT,
                    ErrorSummary TEXT,
                    CreatedAt TEXT
                )");
        }

        private static void InsertLog(TaskExecutionLog log)
        {
            try
            {
                TShock.DB.Query(
                    @"INSERT INTO task_execution_logs
                      (TaskId, TaskName, TriggeredAt, TriggerMode, ConditionResult, Skipped, Status, StartedAt, CompletedAt, DurationMs, CommandResults, ErrorSummary, CreatedAt)
                      VALUES (@0, @1, @2, @3, @4, @5, @6, @7, @8, @9, @10, @11, @12)",
                    log.TaskId, log.TaskName,
                    log.TriggeredAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    log.TriggerMode,
                    log.ConditionResult ? 1 : 0,
                    log.Skipped ? 1 : 0,
                    log.Status,
                    log.StartedAt?.ToString("yyyy-MM-dd HH:mm:ss"),
                    log.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss"),
                    log.DurationMs,
                    JsonConvert.SerializeObject(log.Commands),
                    log.ErrorSummary ?? "",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 写入任务执行记录失败: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════
        // REST API
        // ═══════════════════════════════════════════════════

        /// <summary>GET /data/tasks/list — 获取所有任务</summary>
        public static object ListTasksApi(RestRequestArgs args)
        {
            try
            {
                // 从执行记录表统计每个任务的真实执行次数（重启后依然准确）
                var countMap = new Dictionary<string, int>();
                try
                {
                    using (var res = TShock.DB.QueryReader("SELECT TaskId, COUNT(*) AS cnt FROM task_execution_logs GROUP BY TaskId"))
                    {
                        while (res.Read())
                            countMap[res.Get<string>("TaskId")] = res.Get<int>("cnt");
                    }
                }
                catch { /* 表不存在等场景降级为内存计数 */ }

                var list = Tasks.Select(t =>
                {
                    // 实际执行次数取自执行记录表（整表统计，重启后依然准确）
                    var runCount = countMap.TryGetValue(t.Id, out var cnt) ? cnt : 0;
                    return new
                    {
                        id = t.Id,
                        name = t.Name,
                        enabled = t.Enabled,
                        triggerMode = t.TriggerMode,
                        intervalSeconds = t.IntervalSeconds,
                        dailyTime = t.DailyTime,
                        condition = t.Condition,
                        execMode = t.ExecMode,
                        commandCount = t.Commands.Count,
                        // 从未真正执行过的任务不显示时间：LastRunAt 现在既可能是真实执行时刻，
                        // 也可能是补种的计时起点（见 SeedMissingLastRunAt），后者不是"上次执行"，
                        // 显示出来会让面板把补种时间谎报成执行时刻
                        lastRunAt = (t.LastRunAt == DateTime.MinValue || runCount == 0)
                            ? null
                            : t.LastRunAt.ToString("yyyy-MM-dd HH:mm:ss"),
                        lastRunStatus = t.LastRunStatus,
                        runCount,
                        running = t.Running
                    };
                });

                return new { status = 200, tasks = list, count = Tasks.Count };
            }
            catch (Exception ex)
            {
                return new { status = 500, error = ex.Message };
            }
        }

        /// <summary>GET /data/tasks/get?id= — 获取单个任务详情</summary>
        public static object GetTaskApi(RestRequestArgs args)
        {
            try
            {
                var id = args.Parameters["id"];
                var task = Tasks.FirstOrDefault(t => t.Id == id);
                if (task == null)
                    return new { status = 404, error = "任务不存在" };

                return new { status = 200, task };
            }
            catch (Exception ex)
            {
                return new { status = 500, error = ex.Message };
            }
        }

        /// <summary>POST /data/tasks/save — 创建/更新任务（body 参数 task=JSON）</summary>
        public static object SaveTaskApi(RestRequestArgs args)
        {
            try
            {
                var json = args.Parameters["task"];
                if (string.IsNullOrEmpty(json))
                    return new { status = 400, error = "Missing task parameter" };

                var task = JsonConvert.DeserializeObject<AutoTask>(json);
                if (task == null)
                    return new { status = 400, error = "Invalid task format" };

                if (string.IsNullOrEmpty(task.Id))
                    task.Id = Guid.NewGuid().ToString("N");

                // 校验执行模式
                if (task.TriggerMode != "manual" && task.TriggerMode != "interval" && task.TriggerMode != "daily")
                    return new { status = 400, error = "Invalid triggerMode" };

                if (task.ExecMode != "sequential" && task.ExecMode != "concurrent")
                    return new { status = 400, error = "Invalid execMode" };

                // 校验条件类型
                if (task.Condition == null)
                    task.Condition = new TaskCondition();
                if (task.Condition.Type != "always" && task.Condition.Type != "online_count" &&
                    task.Condition.Type != "boss_defeated" && task.Condition.Type != "player_online")
                    return new { status = 400, error = "Invalid condition type" };

                // 更新或新增
                var existing = Tasks.FirstOrDefault(t => t.Id == task.Id);
                if (existing != null)
                {
                    // 保留运行期状态（前端保存不会携带这些字段）
                    task.LastRunAt = existing.LastRunAt;
                    task.LastDailyRunAt = existing.LastDailyRunAt;
                    task.LastRunStatus = existing.LastRunStatus;
                    task.RunCount = existing.RunCount;
                    Tasks[Tasks.IndexOf(existing)] = task;
                }
                else
                {
                    // 新建任务：运行期字段一律以服务端为准，不采纳客户端回传值。
                    // 两个原因：
                    //   1. 前端"编辑过某任务后再点新建"时，emptyTask() 走 Object.assign 填充、
                    //      不会删掉旧键，于是上一个任务的 lastRunAt 会被原样带过来；若采纳，
                    //      新任务会继承旧任务的计时起点，可能刚建好就立刻跑一次（清理地面即清一次）。
                    //   2. 无历史的任务以"当前时间"为起点（D1 修法，见 SeedMissingLastRunAt），
                    //      所以新建的 interval 任务从此刻开始计满一个完整间隔才首次执行，
                    //      而不是因为 MinValue 被当成已到期而在下一个 tick 立刻执行。
                    // 需要"马上跑一次"时面板另有「立即执行」按钮（RunTaskApi）。
                    task.LastRunAt = DateTime.Now;
                    task.LastDailyRunAt = DateTime.MinValue;
                    task.LastRunStatus = "";
                    task.RunCount = 0;
                    task.Running = false;

                    Tasks.Add(task);
                }

                SaveConfig();
                TShock.Log.ConsoleInfo($"[TSWeb] 任务已保存: {task.Name} ({task.TriggerMode})");
                return new { status = 200, message = "任务已保存", id = task.Id };
            }
            catch (Exception ex)
            {
                return new { status = 500, error = ex.Message };
            }
        }

        /// <summary>POST /data/tasks/delete?id= — 删除任务</summary>
        public static object DeleteTaskApi(RestRequestArgs args)
        {
            try
            {
                var id = args.Parameters["id"];
                var task = Tasks.FirstOrDefault(t => t.Id == id);
                if (task == null)
                    return new { status = 404, error = "任务不存在" };

                if (task.Running)
                    return new { status = 400, error = "任务正在执行中，无法删除" };

                Tasks.Remove(task);
                SaveConfig();
                TShock.Log.ConsoleInfo($"[TSWeb] 任务已删除: {task.Name}");
                return new { status = 200, message = "任务已删除" };
            }
            catch (Exception ex)
            {
                return new { status = 500, error = ex.Message };
            }
        }

        /// <summary>
        /// POST /data/tasks/run?id=&force=0/1 — 手动执行
        /// force=1 跳过条件判断强制执行
        /// </summary>
        public static object RunTaskApi(RestRequestArgs args)
        {
            try
            {
                var id = args.Parameters["id"];
                var task = Tasks.FirstOrDefault(t => t.Id == id);
                if (task == null)
                    return new { status = 404, error = "任务不存在" };

                if (task.Running)
                    return new { status = 400, error = "任务正在执行中" };

                bool force = false;
                var forceRaw = args.Parameters["force"];
                if (!string.IsNullOrEmpty(forceRaw))
                {
                    if (!bool.TryParse(forceRaw, out force))
                        force = forceRaw == "1";
                }

                Task.Run(() => ExecuteTask(task, force));
                TShock.Log.ConsoleInfo($"[TSWeb] 任务手动执行: {task.Name} (force={force})");
                return new { status = 200, message = "任务已开始执行" };
            }
            catch (Exception ex)
            {
                return new { status = 500, error = ex.Message };
            }
        }

        /// <summary>GET /data/tasks/log?taskId=&page=&pageSize= — 执行记录列表</summary>
        public static object ListLogsApi(RestRequestArgs args)
        {
            try
            {
                var taskId = args.Parameters["taskId"] ?? "";
                int page = 1, pageSize = 20;
                int.TryParse(args.Parameters["page"], out page);
                int.TryParse(args.Parameters["pageSize"], out pageSize);
                if (page < 1) page = 1;
                if (pageSize < 1 || pageSize > 100) pageSize = 20;

                long total = 0;
                using (var res = string.IsNullOrEmpty(taskId)
                    ? TShock.DB.QueryReader("SELECT COUNT(*) AS cnt FROM task_execution_logs")
                    : TShock.DB.QueryReader("SELECT COUNT(*) AS cnt FROM task_execution_logs WHERE TaskId = @0", taskId))
                {
                    if (res.Read())
                        total = res.Get<long>("cnt");
                }

                var list = new List<object>();
                using (var res = string.IsNullOrEmpty(taskId)
                    ? TShock.DB.QueryReader("SELECT * FROM task_execution_logs ORDER BY Id DESC LIMIT @0 OFFSET @1", pageSize, (page - 1) * pageSize)
                    : TShock.DB.QueryReader("SELECT * FROM task_execution_logs WHERE TaskId = @0 ORDER BY Id DESC LIMIT @1 OFFSET @2", taskId, pageSize, (page - 1) * pageSize))
                {
                    while (res.Read())
                    {
                        list.Add(new
                        {
                            id = res.Get<long>("Id"),
                            taskId = res.Get<string>("TaskId"),
                            taskName = res.Get<string>("TaskName"),
                            triggeredAt = res.Get<string>("TriggeredAt"),
                            triggerMode = res.Get<string>("TriggerMode"),
                            conditionResult = res.Get<int>("ConditionResult") == 1,
                            skipped = res.Get<int>("Skipped") == 1,
                            status = res.Get<string>("Status"),
                            durationMs = res.Get<long>("DurationMs"),
                            errorSummary = res.Get<string>("ErrorSummary")
                        });
                    }
                }

                return new { status = 200, logs = list, total = total, page, pageSize };
            }
            catch (Exception ex)
            {
                return new { status = 500, error = ex.Message };
            }
        }

        /// <summary>GET /data/tasks/log/detail?id= — 单条执行记录详情（含完整命令输出）</summary>
        public static object LogDetailApi(RestRequestArgs args)
        {
            try
            {
                long id = 0;
                long.TryParse(args.Parameters["id"], out id);
                if (id <= 0)
                    return new { status = 400, error = "Missing id" };

                using (var res = TShock.DB.QueryReader("SELECT * FROM task_execution_logs WHERE Id = @0", id))
                {
                    if (!res.Read())
                        return new { status = 404, error = "记录不存在" };

                    var commandResults = new List<TaskCommandLog>();
                    var raw = res.Get<string>("CommandResults");
                    if (!string.IsNullOrEmpty(raw))
                    {
                        try { commandResults = JsonConvert.DeserializeObject<List<TaskCommandLog>>(raw) ?? new(); }
                        catch { commandResults = new(); }
                    }

                    return new
                    {
                        status = 200,
                        log = new
                        {
                            id = res.Get<long>("Id"),
                            taskId = res.Get<string>("TaskId"),
                            taskName = res.Get<string>("TaskName"),
                            triggeredAt = res.Get<string>("TriggeredAt"),
                            triggerMode = res.Get<string>("TriggerMode"),
                            conditionResult = res.Get<int>("ConditionResult") == 1,
                            skipped = res.Get<int>("Skipped") == 1,
                            status = res.Get<string>("Status"),
                            startedAt = res.Get<string>("StartedAt"),
                            completedAt = res.Get<string>("CompletedAt"),
                            durationMs = res.Get<long>("DurationMs"),
                            commands = commandResults,
                            errorSummary = res.Get<string>("ErrorSummary")
                        }
                    };
                }
            }
            catch (Exception ex)
            {
                return new { status = 500, error = ex.Message };
            }
        }
    }
}
