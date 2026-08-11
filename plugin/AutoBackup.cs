using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Rests;
using TShockAPI;
using Terraria;

namespace TShockData
{
    /// <summary>
    /// 自动备份模块：按配置间隔将「世界 .wld + tshock.sqlite + HouseRegion.sqlite」打包为 zip。
    /// 本地留存（不自动清理）；若已注册后端 webhook，则主动通知后端经 SSE 拉取到后端
    /// data/backup/{serverId}/ 专门目录。
    ///  - sqlite 用 SqliteConnection.BackupDatabase 在线备份（运行中安全、一致）
    ///  - 备份前先 SaveManager.Instance.SaveWorld() 确保 .wld 为最新
    /// </summary>
    public class AutoBackupConfig
    {
        /// <summary>总开关（默认关闭）</summary>
        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = false;

        /// <summary>备份间隔（秒），默认 3600</summary>
        [JsonProperty("intervalSeconds")]
        public int IntervalSeconds { get; set; } = 3600;

        /// <summary>是否推送后端专门目录（默认开启；未注册 webhook 时自动跳过）</summary>
        [JsonProperty("pushToBackend")]
        public bool PushToBackend { get; set; } = true;
    }

    public static class AutoBackup
    {
        private static readonly string ConfigPath = Path.Combine(TShock.SavePath, "TSWeb", "Backup", "backup.json");
        private static readonly string BackupRoot = Path.Combine(TShock.SavePath, "TSWeb", "Backup");
        private static readonly string TempDir = Path.Combine(TShock.SavePath, "TSWeb", "Backup", ".tmp");

        private static AutoBackupConfig _config = new();
        private static System.Timers.Timer? _timer;
        private static bool _initialized;
        private static bool _running;
        private static DateTime _lastRun = DateTime.MinValue;

        private static readonly HttpClient _http = new();
        private static readonly object _lock = new();

        public static AutoBackupConfig Config => _config;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            LoadConfig();

            _timer = new System.Timers.Timer(1000);
            _timer.Elapsed += OnTick;
            _timer.AutoReset = true;
            _timer.Start();

            TShock.Log.ConsoleInfo($"[TSWeb] 自动备份已初始化 (启用:{(Config.Enabled ? "是" : "否")}, 间隔:{Config.IntervalSeconds}s, 推送后端:{(Config.PushToBackend ? "是" : "否")})");
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

            TShock.Log.ConsoleInfo("[TSWeb] 自动备份已停止");
        }

        // ═══════════════════════════════════════════
        // 配置读写（仿 BossConfigManager）
        // ═══════════════════════════════════════════

        public static void LoadConfig()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    _config = JsonConvert.DeserializeObject<AutoBackupConfig>(json) ?? new AutoBackupConfig();
                }
                else
                {
                    _config = new AutoBackupConfig();
                    SaveConfig();
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 加载自动备份配置失败: {ex.Message}");
                _config = new AutoBackupConfig();
            }
        }

        public static void SaveConfig()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonConvert.SerializeObject(_config, Formatting.Indented);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 保存自动备份配置失败: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════
        // REST API
        // ═══════════════════════════════════════════

        public static object GetConfigJson(RestRequestArgs args)
        {
            return new
            {
                status = "200",
                enabled = Config.Enabled,
                intervalSeconds = Config.IntervalSeconds,
                pushToBackend = Config.PushToBackend
            };
        }

        public static object SetConfigJson(RestRequestArgs args)
        {
            try
            {
                var enabled = args.Parameters["enabled"];
                if (!string.IsNullOrEmpty(enabled))
                    Config.Enabled = enabled.ToLower() == "true";

                var interval = args.Parameters["intervalSeconds"];
                if (!string.IsNullOrEmpty(interval) && int.TryParse(interval, out var secs) && secs > 0)
                    Config.IntervalSeconds = secs;

                var push = args.Parameters["pushToBackend"];
                if (!string.IsNullOrEmpty(push))
                    Config.PushToBackend = push.ToLower() == "true";

                SaveConfig();
                TShock.Log.ConsoleInfo($"[TSWeb] 自动备份配置已更新 (启用:{(Config.Enabled ? "是" : "否")}, 间隔:{Config.IntervalSeconds}s, 推送后端:{(Config.PushToBackend ? "是" : "否")})");
                return new { status = "200", message = "自动备份配置已保存" };
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 更新自动备份配置失败: {ex.Message}");
                return new { status = "500", error = ex.Message };
            }
        }

        // ═══════════════════════════════════════════
        // 调度
        // ═══════════════════════════════════════════

        private static void OnTick(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (!Config.Enabled) return;
            if (_running) return;

            bool due;
            lock (_lock)
            {
                due = Config.IntervalSeconds > 0 &&
                      (DateTime.Now - _lastRun).TotalSeconds >= Config.IntervalSeconds;
                if (due) _lastRun = DateTime.Now;
            }

            if (due)
            {
                Task.Run(() => ExecuteBackup());
            }
        }

        // ═══════════════════════════════════════════
        // 备份执行
        // ═══════════════════════════════════════════

        private static void ExecuteBackup()
        {
            if (_running) return;
            _running = true;
            try
            {
                TShock.Log.ConsoleInfo("[TSWeb] 自动备份开始...");

                // 1. 先保存世界，确保 .wld 为最新状态
                // 注：SaveManager 在 TShock 6.1.0 中为 internal，不可外部访问；
                // TShock.Utils.SaveWorld() 为公开 API，内部同样走 SaveManager（等待全部保存完成）
                try
                {
                    TShock.Utils.SaveWorld();
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleError($"[TSWeb] 自动备份: 保存世界失败，继续使用磁盘上的 .wld: {ex.Message}");
                }

                // 2. 组装临时目录（世界 + 两个 sqlite）
                Directory.CreateDirectory(TempDir);
                ClearDir(TempDir);

                var worldPath = Main.worldPathName;
                var worldName = Path.GetFileNameWithoutExtension(worldPath);
                var worldCopy = Path.Combine(TempDir, Path.GetFileName(worldPath));
                if (File.Exists(worldPath))
                    File.Copy(worldPath, worldCopy, true);
                else
                    TShock.Log.ConsoleWarn($"[TSWeb] 自动备份: 世界文件不存在: {worldPath}");

                var tshockSqlite = ResolveTShockSqlitePath();
                if (File.Exists(tshockSqlite))
                    BackupSqliteOnline(tshockSqlite, Path.Combine(TempDir, Path.GetFileName(tshockSqlite)));
                else
                    TShock.Log.ConsoleWarn($"[TSWeb] 自动备份: tshock.sqlite 不存在: {tshockSqlite}");

                var houseSqlite = Path.Combine(TShock.SavePath, "HouseRegion.sqlite");
                if (File.Exists(houseSqlite))
                    BackupSqliteOnline(houseSqlite, Path.Combine(TempDir, "HouseRegion.sqlite"));
                // HouseRegion.sqlite 不存在（未启用房屋系统）时静默跳过

                // 3. 打包 zip（先确保临时目录文件可读，偶发句柄占用时短暂重试）
                EnsureFilesReadable(TempDir);
                var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var safeWorld = string.IsNullOrEmpty(worldName) ? "world" : SanitizeFileName(worldName);
                var zipPath = Path.Combine(BackupRoot, $"backup-{safeWorld}-{ts}.zip");
                Directory.CreateDirectory(BackupRoot);
                ZipFile.CreateFromDirectory(TempDir, zipPath, CompressionLevel.Optimal, false);

                // 4. 清理临时目录
                ClearDir(TempDir);
                try { Directory.Delete(TempDir, true); } catch { }

                var info = new FileInfo(zipPath);
                TShock.Log.ConsoleInfo($"[TSWeb] 自动备份完成: {Path.GetFileName(zipPath)} ({info.Length} bytes)");

                // 5. 推送后端（失败仅记日志，不重试；本地 zip 保留）
                if (Config.PushToBackend)
                {
                    try
                    {
                        PushToBackendAsync(zipPath).GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        TShock.Log.ConsoleError($"[TSWeb] 自动备份推送后端失败: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 自动备份执行失败: {ex}");
            }
            finally
            {
                _running = false;
            }
        }

        /// <summary>解析 TShock sqlite 数据库路径（默认 tshock.sqlite，位于 SavePath 下）</summary>
        private static string ResolveTShockSqlitePath()
        {
            var cfgPath = TShock.Config.Settings.SqliteDBPath;
            if (string.IsNullOrEmpty(cfgPath))
                cfgPath = "tshock.sqlite";
            return Path.IsPathRooted(cfgPath)
                ? cfgPath
                : Path.Combine(TShock.SavePath, cfgPath);
        }

        /// <summary>SQLite 在线备份：源库运行中安全复制，结果一致</summary>
        /// <remarks>
        /// 目标连接必须禁用连接池并显式释放：Microsoft.Data.Sqlite 默认连接池会让 Dispose
        /// 后的连接句柄仍被进程持有（文件独占），导致随后 ZipFile 读取临时副本时 IOException。
        /// 这里用 finally + ClearPool + Dispose 三重确保文件句柄立即关闭。
        /// </remarks>
        private static void BackupSqliteOnline(string srcPath, string destPath)
        {
            var source = new SqliteConnection($"Data Source={srcPath};Pooling=false");
            var dest = new SqliteConnection($"Data Source={destPath};Pooling=false");
            try
            {
                source.Open();
                dest.Open();
                source.BackupDatabase(dest);
            }
            finally
            {
                try { SqliteConnection.ClearPool(dest); } catch { }
                dest.Dispose();
                try { SqliteConnection.ClearPool(source); } catch { }
                source.Dispose();
            }
        }

        /// <summary>确保目录下所有文件可被读取（文件句柄占用时短暂重试，最多 10 次 × 100ms）</summary>
        private static void EnsureFilesReadable(string dir)
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var allOk = true;
                foreach (var f in Directory.GetFiles(dir))
                {
                    try
                    {
                        using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    }
                    catch (IOException)
                    {
                        allOk = false;
                        break;
                    }
                }
                if (allOk) return;
                Thread.Sleep(100);
            }
        }

        private static void ClearDir(string dir)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.GetFiles(dir))
            {
                try { File.Delete(f); } catch { }
            }
            foreach (var d in Directory.GetDirectories(dir))
            {
                try { Directory.Delete(d, true); } catch { }
            }
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }

        // ═══════════════════════════════════════════
        // 推送后端（HMAC 签名，与后端 hookAuth.js 协议一致）
        // ═══════════════════════════════════════════

        private static async Task PushToBackendAsync(string zipPath)
        {
            // 后端已连接信号：取 SSE 常连下发的 hookBase（后端无条件建立常连）
            const string suffix = "/hook/log";
            string backupUrl;
            var hookBase = SSELogger.GetHookBase();
            if (!string.IsNullOrEmpty(hookBase))
            {
                // 防御：hookBase 可能携带 /hook/log 后缀，剥离后拼接
                hookBase = hookBase.TrimEnd('/');
                if (hookBase.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    hookBase = hookBase.Substring(0, hookBase.Length - suffix.Length);
                backupUrl = hookBase + "/hook/backup";
            }
            else
            {
                TShock.Log.ConsoleWarn("[TSWeb] 自动备份: 后端未连接（无 SSE hookBase），跳过推送（本地备份已保留）");
                return;
            }

            var secret = SSELogger.GetWebhookSecret();
            var serverId = SSELogger.GetServerId();
            if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(serverId))
            {
                TShock.Log.ConsoleWarn("[TSWeb] 自动备份: 缺少 pushSecret/serverId，跳过推送（本地备份已保留）");
                return;
            }

            var rel = Path.GetRelativePath(TShock.SavePath, zipPath).Replace('\\', '/');
            var body = JsonConvert.SerializeObject(new { path = rel });

            var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var nonce = Guid.NewGuid().ToString("N"); // 32 hex
            var bodyHash = Sha256Hex(body);
            var signature = HmacSha256Hex(secret, $"{ts}.{nonce}.{bodyHash}");

            using var req = new HttpRequestMessage(HttpMethod.Post, backupUrl)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            req.Headers.Add("X-Server-Id", serverId);
            req.Headers.Add("X-Timestamp", ts);
            req.Headers.Add("X-Nonce", nonce);
            req.Headers.Add("X-Signature", signature);

            // 通知会等待后端完成 SSE 拉取（大文件可能较久），5 分钟兜底
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var resp = await _http.SendAsync(req, cts.Token);
            var text = await resp.Content.ReadAsStringAsync(cts.Token);
            TShock.Log.ConsoleInfo($"[TSWeb] 自动备份推送: HTTP {(int)resp.StatusCode} {text}");
        }

        private static string Sha256Hex(string input)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexStringLower(bytes);
        }

        private static string HmacSha256Hex(string key, string input)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
            var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexStringLower(bytes);
        }
    }
}
