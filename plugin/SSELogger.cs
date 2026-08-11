using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Rests;
using TerrariaApi.Server;
using TShockAPI;

namespace TShockData
{
    /// <summary>
    /// 一个带颜色的文本片段
    /// </summary>
    public class LogSegment
    {
        [JsonProperty("t")]
        public string Text { get; set; } = "";

        /// <summary>ConsoleColor 名称，如 "Red"、"Green"、"Gray"，null 表示默认色</summary>
        [JsonProperty("c")]
        public string? Color { get; set; }
    }

    /// <summary>
    /// 拦截 Console 输出，同时写入原始输出流和内存缓冲区
    /// 捕获 Console.ForegroundColor 信息，以结构化的 LogSegment 形式存储
    /// </summary>
    public class LogInterceptor : TextWriter
    {
        private readonly TextWriter _original;
        private readonly List<LogSegment> _segments = new();
        private StringBuilder _currentText = new();
        private ConsoleColor? _currentColor = null;

        public LogInterceptor(TextWriter original)
        {
            _original = original;
        }

        public override Encoding Encoding => _original.Encoding;
        public override IFormatProvider FormatProvider => _original.FormatProvider;
        public override string NewLine => _original.NewLine;

        private void CheckColor()
        {
            var cc = Console.ForegroundColor;
            if (_currentColor.HasValue && _currentColor.Value == cc)
                return;

            FlushCurrentSegment();
            _currentColor = cc;
        }

        private void FlushCurrentSegment()
        {
            if (_currentText.Length == 0) return;
            _segments.Add(new LogSegment
            {
                Text = _currentText.ToString(),
                Color = _currentColor?.ToString()
            });
            _currentText.Clear();
        }

        private void FlushLine()
        {
            FlushCurrentSegment();
            _currentColor = null;

            if (_segments.Count == 0) return;

            var segments = new List<LogSegment>(_segments);
            _segments.Clear();

            SSELogger.AddLogLine(segments);
        }

        public override void Write(char value)
        {
            _original.Write(value);
            if (value == '\n')
                FlushLine();
            else if (value != '\r')
            {
                CheckColor();
                _currentText.Append(value);
            }
        }

        public override void Write(char[]? buffer)
        {
            if (buffer == null) return;
            _original.Write(buffer);
            CheckColor();
            foreach (var c in buffer)
            {
                if (c == '\n') FlushLine();
                else if (c != '\r') _currentText.Append(c);
            }
        }

        public override void Write(char[]? buffer, int index, int count)
        {
            if (buffer == null) return;
            _original.Write(buffer, index, count);
            CheckColor();
            for (int i = index; i < index + count; i++)
            {
                var c = buffer[i];
                if (c == '\n') FlushLine();
                else if (c != '\r') _currentText.Append(c);
            }
        }

        public override void Write(string? value)
        {
            if (value == null) return;
            _original.Write(value);
            CheckColor();
            foreach (var c in value)
            {
                if (c == '\n') FlushLine();
                else if (c != '\r') _currentText.Append(c);
            }
        }

        public override void Write(StringBuilder? value)
        {
            if (value == null) return;
            _original.Write(value);
            CheckColor();
            for (int i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c == '\n') FlushLine();
                else if (c != '\r') _currentText.Append(c);
            }
        }

        public override void WriteLine()
        {
            _original.WriteLine();
            FlushLine();
        }

        public override void WriteLine(char[]? buffer)
        {
            if (buffer == null) { WriteLine(); return; }
            _original.WriteLine(buffer);
            CheckColor();
            foreach (var c in buffer)
                if (c != '\r') _currentText.Append(c);
            FlushLine();
        }

        public override void WriteLine(char[]? buffer, int index, int count)
        {
            if (buffer == null) { WriteLine(); return; }
            _original.WriteLine(buffer, index, count);
            CheckColor();
            for (int i = index; i < index + count; i++)
                if (buffer[i] != '\r') _currentText.Append(buffer[i]);
            FlushLine();
        }

        public override void WriteLine(string? value)
        {
            _original.WriteLine(value);
            CheckColor();
            if (value != null)
                foreach (var c in value)
                    if (c != '\r') _currentText.Append(c);
            FlushLine();
        }

        public override void WriteLine(StringBuilder? value)
        {
            if (value == null) { WriteLine(); return; }
            _original.WriteLine(value);
            CheckColor();
            for (int i = 0; i < value.Length; i++)
                if (value[i] != '\r') _currentText.Append(value[i]);
            FlushLine();
        }
    }

    /// <summary>
    /// 日志管理 — 通过 Webhook HTTP POST 推送给后端，不再占用插件端口
    /// </summary>
    public static class SSELogger
    {
        // 日志环形缓冲区
        private static readonly List<string> _logHistory = new();
        private static readonly object _logLock = new();
        private const int MaxLogLines = 1000;
        private static long _nextId = 1; // 日志全局递增序号（供断线补拉/去重）

        private static LogInterceptor? _interceptor;
        private static TextWriter? _originalOut;
        private static bool _initialized = false;

        // ═══ 推送凭据（SSE 常连握手时由后端下发，自动备份推送 /hook/backup 用）═══
        private static string? _webhookSecret; // pushSecret（HMAC 签名用）
        private static string? _serverId;       // 后端注册表中的服务器 id（X-Server-Id 头）
        private static string? _hookBase;       // 后端 /hook 基础地址（自动备份推送用）
        private static readonly object _webhookLock = new();

        /// <summary>
        /// 初始化：重定向 Console 输出
        /// </summary>
        public static void Initialize(TerrariaPlugin plugin)
        {
            if (_initialized) return;
            _initialized = true;

            _originalOut = Console.Out;
            _interceptor = new LogInterceptor(_originalOut);
            Console.SetOut(_interceptor);
        }

        /// <summary>
        /// SSE 常连握手时下发的 pushSecret（用于 /hook/* 推送的 HMAC 签名）
        /// </summary>
        public static void RegisterWebhookSecret(string secret)
        {
            lock (_webhookLock)
            {
                _webhookSecret = secret;
            }
        }

        /// <summary>
        /// 后端注册时下发的服务器 id（用于 /hook/* 推送的 X-Server-Id 头）
        /// </summary>
        public static void RegisterServerId(string serverId)
        {
            lock (_webhookLock)
            {
                _serverId = serverId;
            }
        }

        /// <summary>SSE 常连下发后端 /hook 基础地址（如 http://127.0.0.1:3000），自动备份推送用</summary>
        public static void RegisterHookBase(string hookBase)
        {
            lock (_webhookLock)
            {
                _hookBase = hookBase;
            }
        }

        /// <summary>获取后端 /hook 基础地址（SSE 常连下发，非空即表示后端已连接）</summary>
        public static string? GetHookBase()
        {
            lock (_webhookLock)
            {
                return _hookBase;
            }
        }

        /// <summary>获取当前 webhook 签名密钥（供自动备份等模块推送 /hook/* 使用）</summary>
        public static string? GetWebhookSecret()
        {
            lock (_webhookLock)
            {
                return _webhookSecret;
            }
        }

        /// <summary>获取当前注册的服务器 id（供自动备份等模块推送 /hook/* 使用）</summary>
        public static string? GetServerId()
        {
            lock (_webhookLock)
            {
                return _serverId;
            }
        }

        /// <summary>
        /// 添加一行日志到环形缓冲区并推送
        /// 包装为 { id, time, segments }，segments 为带原始颜色的片段（不做级别推断）
        /// </summary>
        public static void AddLogLine(List<LogSegment> segments)
        {
            // 多服：日志带来源服务器 id（后端按 serverId 分组广播，前端按服务器订阅）
            string serverId;
            lock (_webhookLock)
            {
                serverId = _serverId ?? "";
            }

            string json;
            lock (_logLock)
            {
                var wrapped = new
                {
                    id = _nextId++,
                    time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    segments,
                    serverId
                };
                json = JsonConvert.SerializeObject(wrapped);
                _logHistory.Add(json);
                if (_logHistory.Count > MaxLogLines)
                    _logHistory.RemoveRange(0, _logHistory.Count - MaxLogLines);
            }

            // SSE 实时推送（唯一日志回传通道）
            try { WebRestServer.Broadcast("log", json); }
            catch { }
        }

        /// <summary>
        /// REST API: 获取最近日志
        /// GET /data/online/log/poll?since=索引
        /// </summary>
        public static object PollLogs(RestRequestArgs args)
        {
            var sinceStr = args.Parameters["since"];
            int since = 0;
            if (!string.IsNullOrEmpty(sinceStr))
                int.TryParse(sinceStr, out since);

            lock (_logLock)
            {
                if (since < 0) since = 0;
                if (since >= _logHistory.Count)
                    return new { status = "200", lines = new List<string>(), next = _logHistory.Count };

                var lines = new List<string>();
                for (int i = since; i < _logHistory.Count; i++)
                    lines.Add(_logHistory[i]);

                return new
                {
                    status = "200",
                    lines,
                    next = _logHistory.Count
                };
            }
        }

        /// <summary>
        /// REST API: 以 superadmin 身份执行服务器命令
        /// GET /data/online/log/command?cmd=say hello&executor=xxx
        /// 命令执行信息及输出通过 Console 写入（LogInterceptor 自动捕获）
        /// </summary>
        public static object ExecuteCommandApi(RestRequestArgs args)
        {
            try
            {
                var cmd = args.Parameters["cmd"];
                if (string.IsNullOrWhiteSpace(cmd))
                {
                    return new RestObject("400") { { "error", "Missing cmd parameter" } };
                }

                var executor = args.Parameters["executor"];
                if (string.IsNullOrWhiteSpace(executor))
                    executor = "SSE-Console";

                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.Write("[" + DateTime.Now.ToString("HH:mm:ss") + "] ");
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(executor);
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.Write(" 执行了 ");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(cmd);
                Console.ResetColor();

                var group = TShock.Groups.GetGroupByName("superadmin");
                var tr = new TSRestPlayer(executor, group);
                Commands.HandleCommand(tr, cmd);

                var outputList = tr.GetCommandOutput();

                if (outputList != null && outputList.Count > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Gray;
                    foreach (var line in outputList)
                    {
                        if (!string.IsNullOrEmpty(line))
                            Console.WriteLine(line);
                    }
                    Console.ResetColor();
                }

                var output = string.Join("\n", outputList ?? new List<string>());

                return new RestObject
                {
                    { "response", output }
                };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }

        /// <summary>
        /// 清理
        /// </summary>
        public static void Dispose()
        {
            if (!_initialized) return;
            _initialized = false;

            if (_originalOut != null)
                Console.SetOut(_originalOut);
        }
    }
}
