using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using OTAPI;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;

namespace TShockData
{
    /// <summary>
    /// PacketCatch 配置
    /// </summary>
    public class PacketCatchConfig
    {
        /// <summary>启动时自动开始记录</summary>
        [JsonProperty("启用")]
        public bool Enabled { get; set; } = true;

        /// <summary>输出目录（相对 TShock.SavePath，支持绝对路径）</summary>
        [JsonProperty("输出目录")]
        public string OutputDir { get; set; } = "PacketCatch";

        /// <summary>后台刷盘间隔（秒）</summary>
        [JsonProperty("刷新间隔秒")]
        public int FlushSeconds { get; set; } = 5;

        /// <summary>单文件超过该大小(MB)后滚动新文件</summary>
        [JsonProperty("单文件大小MB")]
        public int RotateMB { get; set; } = 100;

        /// <summary>是否记录最高频的 PlayerUpdate 包（每玩家每秒约60个）</summary>
        [JsonProperty("记录PlayerUpdate")]
        public bool RecordPlayerUpdate { get; set; } = true;

        /// <summary>是否脱敏密码包（PasswordSend 仅记元数据，不记明文密码）</summary>
        [JsonProperty("脱敏密码包")]
        public bool MaskPasswordPackets { get; set; } = true;

        /// <summary>仅记录指定包 ID（空 = 记录全部）。调试用。</summary>
        [JsonProperty("仅记录这些包ID")]
        public List<int> FilterOnlyIds { get; set; } = new List<int>();
    }

    [ApiVersion(2, 1)]
    public class PacketCatchPlugin : TerrariaPlugin
    {
        public override string Author => "lmx12330";
        public override string Description => "全量入站数据包记录器(用于外挂原理分析取证)";
        public override string Name => "PacketCatch";
        public override Version Version => new Version(1, 0, 0, 0);

        public PacketCatchPlugin(Main game) : base(game) { }

        public override void Initialize()
        {
            PacketCatchCore.Initialize(this);
            TShockAPI.Commands.ChatCommands.Add(new Command("tshock.admin", PacketCatchCore.HandleCommand, "pcatch", "抓包"));
        }

        protected override void Dispose(bool Disposing)
        {
            if (Disposing)
            {
                PacketCatchCore.Dispose();
            }
            base.Dispose(Disposing);
        }
    }

    /// <summary>
    /// 底层包捕获核心。
    /// 使用 OTAPI.Hooks.MessageBuffer.GetData —— 与 Omni 同款的最底层钩子，
    /// 每个客户端→服务器的数据包（含握手期包、TShock 不处理的包如 Dust 66）都会经过这里。
    /// </summary>
    public static class PacketCatchCore
    {
        // ticks(8) + who(1) + pid(1) + dir(1) + nameLen(1) + ipLen(1) + payloadLen(2)
        private const int HEADER_SIZE = 15;

        private static readonly object _writeLock = new object();
        private static PacketCatchConfig _config = new PacketCatchConfig();
        private static FileStream? _stream;
        private static string _outputDir = "";
        private static string _currentFile = "";
        private static long _bytesWritten;
        private static long _totalPackets;
        private static bool _running;
        private static bool _fatalError;
        private static Timer? _flushTimer;
        private static HashSet<int> _filter = new HashSet<int>();
        private static bool _initialized;

        private static string ConfigPath => Path.Combine(TShock.SavePath, "PacketCatch", "config.json");
        private static string DefaultOutputDir => Path.Combine(TShock.SavePath, "PacketCatch");

        public static void Initialize(TerrariaPlugin plugin)
        {
            if (_initialized) return;
            _initialized = true;

            LoadConfig();
            EnsureOutputDir();
            DumpPacketTypeNames();

            // 底层钩子：入站 + 出站（不拦截，仅记录）
            OTAPI.Hooks.MessageBuffer.GetData += OnGetData;
            OTAPI.Hooks.NetMessage.SendBytes += OnSendBytes;

            _flushTimer = new Timer(_ => Flush(), null,
                TimeSpan.FromSeconds(_config.FlushSeconds), TimeSpan.FromSeconds(_config.FlushSeconds));

            if (_config.Enabled)
            {
                Start();
            }
            else
            {
                TShock.Log.ConsoleInfo("[PacketCatch] 已加载(配置为未启用)，使用 /pcatch start 开始记录");
            }
        }

        public static void Dispose()
        {
            _running = false;
            _flushTimer?.Dispose();
            _flushTimer = null;
            OTAPI.Hooks.MessageBuffer.GetData -= OnGetData;
            OTAPI.Hooks.NetMessage.SendBytes -= OnSendBytes;
            lock (_writeLock)
            {
                _stream?.Flush();
                _stream?.Dispose();
                _stream = null;
            }
            _initialized = false;
            TShock.Log.ConsoleInfo($"[PacketCatch] 已停止，本次共记录 {_totalPackets} 个包");
        }

        // ═══════════════════ 核心：包捕获 ═══════════════════

        /// <summary>入站：客户端 → 服务器（MessageBuffer 层，含握手期/未注册包）</summary>
        private static void OnGetData(object? sender, OTAPI.Hooks.MessageBuffer.GetDataEventArgs args)
        {
            if (!_running || _fatalError) return;
            try
            {
                var buf = args.Instance?.readBuffer;
                if (buf == null) return;

                int off = args.ReadOffset;
                int len = args.Length;
                if (off < 0 || len < 0 || off > buf.Length || len > buf.Length - Math.Max(off, 0)) return;

                // 真实包 ID：从原始缓冲区读取（args.PacketId 可能被其他插件改写为 255 以取消包）
                byte id = (off > 0) ? buf[off - 1] : args.PacketId;

                // 包过滤
                if (_filter.Count > 0 && !_filter.Contains(id)) return;
                if (id == (byte)PacketTypes.PlayerUpdate && !_config.RecordPlayerUpdate) return;

                int who = args.Instance?.whoAmI ?? 255;
                var (pname, pip) = ResolveNameIp(who);

                // 密码包脱敏：仅记元数据
                if (id == (byte)PacketTypes.PasswordSend && _config.MaskPasswordPackets) len = 0;

                WriteRecord(who, id, 0, pname, pip, buf, off, len);
            }
            catch (Exception ex)
            {
                _fatalError = true;
                TShock.Log.ConsoleError($"[PacketCatch] 记录异常，已自动停用: {ex}");
            }
        }

        /// <summary>出站：服务器 → 客户端（socket 发送层）。缓冲格式: [2字节长度][msgType][data...]</summary>
        private static void OnSendBytes(object? sender, OTAPI.Hooks.NetMessage.SendBytesEventArgs args)
        {
            if (!_running || _fatalError) return;
            try
            {
                var buf = args.Data;
                if (buf == null) return;

                int off = args.Offset;
                int size = args.Size;
                if (off < 0 || size < 0 || off + 2 > buf.Length || off + size > buf.Length) return;

                byte id = buf[off + 2]; // 跳过 2 字节长度前缀

                if (_filter.Count > 0 && !_filter.Contains(id)) return;
                if (id == (byte)PacketTypes.PlayerUpdate && !_config.RecordPlayerUpdate) return;

                int who = args.RemoteClient; // 目标客户端索引
                var (pname, pip) = ResolveNameIp(who);

                int bodyOff = off + 3;      // 跳过长度前缀 + msgType
                int bodyLen = size - 3;
                if (bodyLen < 0) return;

                WriteRecord(who, id, 1, pname, pip, buf, bodyOff, bodyLen);
            }
            catch (Exception ex)
            {
                _fatalError = true;
                TShock.Log.ConsoleError($"[PacketCatch] 出站记录异常，已自动停用: {ex}");
            }
        }

        /// <summary>解析玩家名+IP（O(1) 数组索引，开销可忽略）；握手早期退回 socket 远程地址</summary>
        private static (string, string) ResolveNameIp(int who)
        {
            if (who >= 0 && who < TShock.Players.Length && TShock.Players[who] != null)
            {
                return (TShock.Players[who].Name ?? "", TShock.Players[who].IP ?? "");
            }
            if (who >= 0 && who < Netplay.Clients.Length && Netplay.Clients[who]?.Socket != null)
            {
                try { return ("", Netplay.Clients[who].Socket.GetRemoteAddress()?.ToString() ?? ""); } catch { }
            }
            return ("", "");
        }

        /// <summary>公共写入：v3 记录头 + name + ip + payload（锁内调用）</summary>
        private static void WriteRecord(int who, byte id, byte dir, string pname, string pip,
            byte[] srcBuf, int srcOff, int srcLen)
        {
            var nameBytes = Encoding.UTF8.GetBytes(pname.Length > 60 ? pname.Substring(0, 60) : pname);
            if (nameBytes.Length > 255) Array.Resize(ref nameBytes, 255);
            var ipBytes = Encoding.UTF8.GetBytes(pip.Length > 45 ? pip.Substring(0, 45) : pip);
            if (ipBytes.Length > 255) Array.Resize(ref ipBytes, 255);

            lock (_writeLock)
            {
                if (_stream == null) return;

                if (_bytesWritten >= (long)_config.RotateMB * 1024 * 1024)
                {
                    RotateLocked();
                    if (_stream == null) return;
                }

                // 记录头: ticks(8) who(1) pid(1) dir(1) nameLen(1) ipLen(1) payloadLen(2) [name][ip][payload]
                Span<byte> header = stackalloc byte[HEADER_SIZE];
                BinaryPrimitives.WriteInt64LittleEndian(header, DateTime.UtcNow.Ticks);
                header[8] = (byte)who;
                header[9] = id;
                header[10] = dir;
                header[11] = (byte)nameBytes.Length;
                header[12] = (byte)ipBytes.Length;
                BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(13), (ushort)srcLen);

                _stream.Write(header);
                if (nameBytes.Length > 0) _stream.Write(nameBytes);
                if (ipBytes.Length > 0) _stream.Write(ipBytes);
                if (srcLen > 0) _stream.Write(srcBuf, srcOff, srcLen);
                _bytesWritten += HEADER_SIZE + nameBytes.Length + ipBytes.Length + srcLen;
                _totalPackets++;
            }
        }

        // ═══════════════════ 生命周期 ═══════════════════

        public static void Start()
        {
            lock (_writeLock)
            {
                _fatalError = false;
                EnsureOutputDir();
                if (_stream == null)
                {
                    OpenNewFileLocked();
                }
                _running = true;
            }
            TShock.Log.ConsoleInfo($"[PacketCatch] 开始全量记录 → {_currentFile}");
        }

        public static void Stop()
        {
            lock (_writeLock)
            {
                _running = false;
                _stream?.Flush();
            }
            TShock.Log.ConsoleInfo($"[PacketCatch] 已暂停记录（数据已刷盘）。当前累计 {_totalPackets} 个包");
        }

        public static void Flush()
        {
            lock (_writeLock)
            {
                try
                {
                    _stream?.Flush();
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleError($"[PacketCatch] 刷盘失败: {ex.Message}");
                }
            }
        }

        private static void RotateLocked()
        {
            _stream?.Flush();
            _stream?.Dispose();
            _stream = null;
            _bytesWritten = 0;
            OpenNewFileLocked();
        }

        private static void OpenNewFileLocked()
        {
            var name = $"PacketCatch_{DateTime.Now:yyyyMMdd_HHmmss}.pcapd";
            _currentFile = Path.Combine(_outputDir, name);
            _stream = new FileStream(_currentFile, FileMode.Create, FileAccess.Write, FileShare.Read, 64 * 1024, useAsync: false);

            // 文件头：magic + version + 创建时间
            Span<byte> header = stackalloc byte[13];
            Encoding.ASCII.GetBytes("PCAT").CopyTo(header);
            header[4] = 3; // version 3: 入站+出站双向，dir 字段
            BinaryPrimitives.WriteInt64LittleEndian(header.Slice(5), DateTime.UtcNow.Ticks);
            _stream.Write(header);
            _bytesWritten += 13;
        }

        private static void EnsureOutputDir()
        {
            var dir = _config.OutputDir;
            if (!Path.IsPathRooted(dir))
            {
                dir = Path.Combine(TShock.SavePath, dir);
            }
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            _outputDir = dir;
        }

        // ═══════════════════ 配置 ═══════════════════

        public static void LoadConfig()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                if (File.Exists(ConfigPath))
                {
                    _config = JsonConvert.DeserializeObject<PacketCatchConfig>(File.ReadAllText(ConfigPath)) ?? new PacketCatchConfig();
                }
                else
                {
                    _config = new PacketCatchConfig();
                    SaveConfig();
                }
                ApplyFilter();
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[PacketCatch] 加载配置失败: {ex.Message}");
                _config = new PacketCatchConfig();
            }
        }

        public static void SaveConfig()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(_config, Formatting.Indented));
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[PacketCatch] 保存配置失败: {ex.Message}");
            }
        }

        private static void ApplyFilter()
        {
            _filter = new HashSet<int>(_config.FilterOnlyIds);
        }

        /// <summary>导出 PacketTypes 枚举名映射，便于后续分析。</summary>
        private static void DumpPacketTypeNames()
        {
            try
            {
                var sb = new StringBuilder();
                var values = Enum.GetValues(typeof(PacketTypes));
                var names = Enum.GetNames(typeof(PacketTypes));
                for (var i = 0; i < values.Length; i++)
                {
                    // PacketTypes 底层类型不一定是 byte，用 Convert 兼容
                    var v = Convert.ToByte(values.GetValue(i));
                    sb.AppendLine($"{v} = {names[i]}");
                }
                File.WriteAllText(Path.Combine(_outputDir, "PacketTypes.txt"), sb.ToString());
                TShock.Log.ConsoleInfo($"[PacketCatch] 已导出包名映射: {Path.Combine(_outputDir, "PacketTypes.txt")}");
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[PacketCatch] 导出包名映射失败: {ex.Message}");
            }
        }

        // ═══════════════════ 命令 ═══════════════════

        public static void HandleCommand(CommandArgs args)
        {
            if (args.Parameters.Count == 0)
            {
                ShowStatus(args);
                return;
            }

            switch (args.Parameters[0].ToLower())
            {
                case "start":
                case "开":
                    Start();
                    args.Player.SendSuccessMessage($"[PacketCatch] 开始记录 → {_currentFile}");
                    break;

                case "stop":
                case "关":
                    Stop();
                    args.Player.SendSuccessMessage("[PacketCatch] 已停止记录，文件已刷盘");
                    break;

                case "flush":
                    Flush();
                    args.Player.SendSuccessMessage("[PacketCatch] 已强制刷盘");
                    break;

                case "reload":
                    LoadConfig();
                    args.Player.SendSuccessMessage("[PacketCatch] 配置已重载");
                    break;

                case "filter":
                    if (args.Parameters.Count >= 2)
                    {
                        _config.FilterOnlyIds = new List<int>();
                        foreach (var part in args.Parameters[1].Split(','))
                        {
                            if (int.TryParse(part.Trim(), out var id)) _config.FilterOnlyIds.Add(id);
                        }
                        ApplyFilter();
                        SaveConfig();
                        args.Player.SendSuccessMessage($"[PacketCatch] 过滤包ID: {string.Join(",", _config.FilterOnlyIds)} (空=全部)");
                    }
                    else
                    {
                        _config.FilterOnlyIds = new List<int>();
                        ApplyFilter();
                        SaveConfig();
                        args.Player.SendSuccessMessage("[PacketCatch] 已清除过滤，记录全部包");
                    }
                    break;

                default:
                    ShowStatus(args);
                    ShowHelp(args);
                    break;
            }
        }

        private static void ShowStatus(CommandArgs args)
        {
            args.Player.SendInfoMessage($"=== PacketCatch 状态 ===");
            args.Player.SendInfoMessage($"记录中: {(_running ? "是" : "否")}");
            args.Player.SendInfoMessage($"累计包数: {_totalPackets}");
            args.Player.SendInfoMessage($"当前文件: {_currentFile}");
            args.Player.SendInfoMessage($"输出目录: {_outputDir}");
            args.Player.SendInfoMessage($"过滤包ID: {(_filter.Count > 0 ? string.Join(",", _filter) : "全部")}");
            args.Player.SendInfoMessage($"PlayerUpdate: {(_config.RecordPlayerUpdate ? "记录" : "跳过")}");
            args.Player.SendInfoMessage($"密码脱敏: {(_config.MaskPasswordPackets ? "是" : "否")}");
        }

        private static void ShowHelp(CommandArgs args)
        {
            args.Player.SendInfoMessage("用法: /pcatch [start|stop|flush|reload|filter <id,id>]");
            args.Player.SendInfoMessage("  start   - 开始记录");
            args.Player.SendInfoMessage("  stop    - 停止记录并刷盘");
            args.Player.SendInfoMessage("  flush   - 强制刷盘");
            args.Player.SendInfoMessage("  reload  - 重载配置");
            args.Player.SendInfoMessage("  filter  - 仅记录指定包ID(逗号分隔)，不带参数=全部");
        }
    }
}
