using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using MonoMod.RuntimeDetour;
using Newtonsoft.Json;
using Terraria;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;

namespace AntiAngel
{
    /// <summary>antiangel 配置</summary>
    public class AntiAngelConfig
    {
        /// <summary>总开关</summary>
        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>命中后踢出玩家（false = 仅警告/广播）</summary>
        [JsonProperty("kick")]
        public bool Kick { get; set; } = true;

        /// <summary>踢出原因</summary>
        [JsonProperty("kickText")]
        public string KickText { get; set; } = "检测到使用 TerraAngel 修改客户端，已被踢出";

        /// <summary>命中时全服红字广播警告</summary>
        [JsonProperty("broadcast")]
        public bool Broadcast { get; set; } = true;
    }

    /// <summary>
    /// Detector —— TerraAngel 客户端检测核心。
    ///
    /// 检测原理（移植自 TShockPlugin-master/src/ServerTools/ModifyClientDetect.cs，
    /// 指纹已用本地 TerraAngel 1.4.5.6 客户端源码逐一实证）：
    ///
    /// TerraAngel 开启「隐藏存在感广播」（ClientConfig.BroadcastPresence）后，
    /// 会周期性发送 PlayerControls(13) 包，并在 netCameraTarget 字段写入魔数：
    ///     new Vector2(-114514, -1919810)   // 0xFFFE40AE / 0xFFE2B4BE
    /// 正常客户端永远不会发送该值 → 命中即铁证。
    ///
    /// TA 发送布局（PacketBuilderExtensions.WritePlayerControlsPacket）：
    ///   [start+1] playerIndex    [start+2] controlFlags  [start+3] movementFlags
    ///   [start+4] miscFlags      [start+5] extraFlags    [start+6] selectedItem
    ///   [start+7..14] position(XY)  然后按标志位追加可选字段：
    ///     movementFlags[2] → velocity(8B)   movementFlags[7] → mount.Type(2B)
    ///     miscFlags[6]     → 原位置+家位置(16B)  extraFlags[5] → netCameraTarget(8B)
    ///
    /// 检测偏移与可选字段字节数均与 TA 布局逐字段吻合（9 = selectedItem+position 8B；
    /// optional = 8/2/16 分别对应 velocity/mount/归返药水两位置）。
    ///
    /// 指纹隐藏：目标值不以明文存在，经 88 字节数组 XOR 派生 Salt 后以 HMAC-SHA256
    /// 哈希形式存放，静态扫描 dll 无法直接提取特征值（防特征被扫后一键绕过）。
    /// </summary>
    public static class Detector
    {
        public static AntiAngelConfig Config { get; private set; } = new();

        private static string ConfigPath => Path.Combine(TShock.SavePath, "antiangel", "config.json");

        private static TerrariaPlugin _plugin;
        private static bool _initialized;
        private static Hook _hook2;
        private static Hook _hook3;

        // ═══════════════════════════════════════════════════════════
        // 隐藏指纹（移植原实现，目标值 XOR 派生 + HMAC 存放）
        // ═══════════════════════════════════════════════════════════
        private static readonly byte[] _data =
        [
            0xA3, 0x5C, 0x2E, 0x9F, 0x1A, 0x7B, 0xD4, 0xE6,
            0x38, 0xC9, 0xF2, 0x4D, 0x6B, 0x8A, 0x1C, 0x5F,
            0x9E, 0x27, 0xB0, 0x43, 0x7C, 0xFD, 0x8D, 0x52,
            0xE4, 0x3A, 0x6C, 0x18, 0x5B, 0x8F, 0xCA, 0x2D,
            0xCC, 0xD0, 0x34, 0xA4, 0xF8, 0x36, 0x4B, 0x9A,
            0x1F, 0x95, 0xC8, 0x73, 0x2C, 0xE7, 0x46, 0x5D,
            0xAA, 0x73, 0xCE, 0xD3, 0x5F, 0x26, 0x9B, 0x41,
            0x87, 0x7F, 0x05, 0x61, 0x30, 0x8C, 0xA5, 0xDB,
            0x00, 0x00, 0x00, 0x00, 0xC9, 0x00, 0x00, 0x00,
            0xAE, 0x40, 0xFE, 0xFF, 0x00, 0x00, 0x00, 0x00,
            0xBE, 0xB4, 0xE2, 0xFF, 0x00, 0x00, 0x00, 0x00
        ];

        private static readonly byte[] Salt;
        private static readonly byte[] TargetIntHash;    // 目标包 ID = 0xC9 (201)
        private static readonly byte[] TargetCoordHash;  // 目标坐标 = (-114514, -1919810)

        static Detector()
        {
            Salt = new byte[32];
            for (var i = 0; i < 32; i++)
                Salt[i] = (byte)(_data[i] ^ _data[i + 32]);

            var a = BitConverter.ToInt32(_data, 64);
            var b = BitConverter.ToInt32(_data, 68);
            var c = (a ^ b) & 0xFF;

            var xRaw = BitConverter.ToInt32(_data, 72);
            var xMask = BitConverter.ToInt32(_data, 76);
            var d = xRaw ^ xMask;
            var yRaw = BitConverter.ToInt32(_data, 80);
            var yMask = BitConverter.ToInt32(_data, 84);
            var e = yRaw ^ yMask;

            TargetIntHash = ComputeHash(BitConverter.GetBytes(c));
            var coordData = new byte[8];
            Buffer.BlockCopy(BitConverter.GetBytes(d), 0, coordData, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(e), 0, coordData, 4, 4);
            TargetCoordHash = ComputeHash(coordData);
        }

        private static byte[] ComputeHash(byte[] data)
        {
            using var hmac = new HMACSHA256(Salt);
            return hmac.ComputeHash(data);
        }

        private static bool IsIntMatch(int value, byte[] targetHash)
            => ComputeHash(BitConverter.GetBytes(value)).SequenceEqual(targetHash);

        private static bool IsCoordMatch(int x, int y, byte[] targetHash)
        {
            var data = new byte[8];
            Buffer.BlockCopy(BitConverter.GetBytes(x), 0, data, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(y), 0, data, 4, 4);
            return ComputeHash(data).SequenceEqual(targetHash);
        }

        // ═══════════════════════════════════════════════════════════
        // 初始化 / 释放
        // ═══════════════════════════════════════════════════════════

        public static void Initialize(TerrariaPlugin plugin)
        {
            if (_initialized) return;
            _plugin = plugin;
            LoadConfig();

            try
            {
                // 1.4.4.9 为 2 参签名 GetData(int,int)；1.4.5.x 为 3 参带 out → 运行时自适应
                var mi2 = typeof(MessageBuffer).GetMethod("GetData",
                    BindingFlags.Public | BindingFlags.Instance, null,
                    new[] { typeof(int), typeof(int) }, null);
                if (mi2 != null)
                {
                    _hook2 = new Hook(mi2, new DetourGetData2(OnGetData2));
                    TShock.Log.ConsoleInfo("[antiangel] GetData detour 已挂载（2 参签名）");
                }
                else
                {
                    var mi3 = typeof(MessageBuffer).GetMethod("GetData",
                        BindingFlags.Public | BindingFlags.Instance, null,
                        new[] { typeof(int), typeof(int), typeof(int).MakeByRefType() }, null);
                    if (mi3 != null)
                    {
                        _hook3 = new Hook(mi3, new DetourGetData3(OnGetData3));
                        TShock.Log.ConsoleInfo("[antiangel] GetData detour 已挂载（3 参签名）");
                    }
                    else
                        TShock.Log.ConsoleError("[antiangel] 未找到 MessageBuffer.GetData，检测不可用");
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[antiangel] GetData detour 注册失败: {ex}");
            }

            _initialized = true;
        }

        public static void Dispose()
        {
            if (!_initialized) return;
            try { _hook2?.Dispose(); } catch { }
            try { _hook3?.Dispose(); } catch { }
            _hook2 = null;
            _hook3 = null;
            _initialized = false;
            TShock.Log.ConsoleInfo("[antiangel] 已卸载");
        }

        // ═══════════════════════════════════════════════════════════
        // GetData detour
        // ═══════════════════════════════════════════════════════════

        private delegate void OrigGetData2(MessageBuffer self, int start, int length);
        private delegate void DetourGetData2(OrigGetData2 orig, MessageBuffer self, int start, int length);
        private delegate void OrigGetData3(MessageBuffer self, int start, int length, out int messageType);
        private delegate void DetourGetData3(OrigGetData3 orig, MessageBuffer self, int start, int length, out int messageType);

        private static void OnGetData2(OrigGetData2 orig, MessageBuffer self, int start, int length)
        {
            try { Check(self, start, length); } catch { /* 检测异常绝不干扰网络处理 */ }
            orig(self, start, length);
        }

        private static void OnGetData3(OrigGetData3 orig, MessageBuffer self, int start, int length, out int messageType)
        {
            try { Check(self, start, length); } catch { /* 检测异常绝不干扰网络处理 */ }
            orig(self, start, length, out messageType);
        }

        // ═══════════════════════════════════════════════════════════
        // 核心检测
        // ═══════════════════════════════════════════════════════════

        private static void Check(MessageBuffer instance, int start, int length)
        {
            if (!Config.Enabled) return;
            if (instance == null || instance.readBuffer == null) return;
            if (start < 0 || start >= instance.readBuffer.Length) return;
            if (length < 2) return; // 至少 type(1) + body(1)

            int value = instance.readBuffer[start]; // 数据包 ID
            bool isCheater = false;

            // ── 指纹①：PlayerControls(13) 的 TA 隐藏存在感 netCameraTarget 魔数 ──
            if (value == MessageID.PlayerControls)
            {
                // 包体须足够容纳：idx(1)+3flags(3)+selectedItem(1)+pos(8)=13B（TA 布局）
                if (length >= 14)
                {
                    var reader = instance.reader;
                    if (reader == null) return;
                    long oldPos = reader.BaseStream.Position;
                    try
                    {
                        // TA 布局 [start+1]=playerIndex [start+2]=controlFlags [start+3..5]=mov/misc/extra
                        reader.BaseStream.Position = start + 3;
                        BitsByte movementFlags = reader.ReadByte();
                        BitsByte miscFlags = reader.ReadByte();
                        BitsByte extraFlags = reader.ReadByte();

                        // extraFlags[5] = netCameraTarget 有值（TA hidden presence 置 true）
                        if (extraFlags[5])
                        {
                            // 可选字段字节数：velocity(8) / mount.Type(2) / 归返药水两位置(16)
                            int optional = (movementFlags[2] ? 8 : 0)
                                         + (movementFlags[7] ? 2 : 0)
                                         + (miscFlags[6] ? 16 : 0);

                            // 读完 3 flags 在 start+6；+9 = selectedItem(1) + position(8) → netCameraTarget 起点
                            long target = start + 6 + 9 + optional;
                            if (target + 8 <= start + length) // 越界防护
                            {
                                reader.BaseStream.Position = target;
                                var nct = reader.ReadVector2();
                                if (IsCoordMatch((int)nct.X, (int)nct.Y, TargetCoordHash))
                                    isCheater = true;
                            }
                        }
                    }
                    catch { /* 解析失败不误伤 */ }
                    finally { reader.BaseStream.Position = oldPos; }
                }
            }

            // ── 指纹②：私有包号 201（原版 MessageID.Count=162，正常客户端永不发送）──
            if (!isCheater && value != MessageID.PlayerControls && IsIntMatch(value, TargetIntHash))
                isCheater = true;

            if (isCheater)
                HandleCheater(instance);
        }

        private static void HandleCheater(MessageBuffer instance)
        {
            var player = TShock.Players[instance.whoAmI];
            if (player == null) return;

            var text = $"[antiangel] 玩家 {player.Name} 使用 TerraAngel 修改客户端进入服务器！";
            TShock.Log.ConsoleWarn(text);
            if (Config.Broadcast)
                TShock.Utils.Broadcast(text, Microsoft.Xna.Framework.Color.Red);
            if (Config.Kick)
            {
                try { player.Kick(Config.KickText, true); }
                catch (Exception ex) { TShock.Log.ConsoleError($"[antiangel] 踢出失败: {ex.Message}"); }
            }
        }

        // ═══════════════════════════════════════════════════════════
        // 配置读写
        // ═══════════════════════════════════════════════════════════

        public static void LoadConfig()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (File.Exists(ConfigPath))
                    Config = JsonConvert.DeserializeObject<AntiAngelConfig>(File.ReadAllText(ConfigPath)) ?? new AntiAngelConfig();
                else
                    SaveConfig();
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[antiangel] 配置读取失败，使用默认: {ex.Message}");
                Config = new AntiAngelConfig();
            }
        }

        public static void SaveConfig()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(Config, Formatting.Indented));
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[antiangel] 配置保存失败: {ex.Message}");
            }
        }
    }
}
