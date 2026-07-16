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

            var json = JsonConvert.SerializeObject(_segments);
            _segments.Clear();

            SSELogger.AddLogLine(json);
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
    /// 日志管理 — 通过 RCON 推送，不再占用 REST API 端口
    /// </summary>
    public static class SSELogger
    {
        // 日志环形缓冲区
        private static readonly List<string> _logHistory = new();
        private static readonly object _logLock = new();
        private const int MaxLogLines = 1000;

        private static LogInterceptor? _interceptor;
        private static TextWriter? _originalOut;
        private static bool _initialized = false;

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
        /// 添加一行日志到环形缓冲区
        /// line 是 LogSegment[] 序列化的 JSON 字符串
        /// </summary>
        public static void AddLogLine(string line)
        {
            lock (_logLock)
            {
                _logHistory.Add(line);
                if (_logHistory.Count > MaxLogLines)
                    _logHistory.RemoveRange(0, _logHistory.Count - MaxLogLines);
            }

            // 推送给 RCON 客户端（如果 RCON 已启用）
            if (RconServer.IsRunning)
                RconServer.AddLogLine(line);
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
