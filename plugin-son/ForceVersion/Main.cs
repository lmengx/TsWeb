using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using MonoMod.RuntimeDetour;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.Net;
using TerrariaApi.Server;
using TShockAPI;

namespace ForceVersion
{
    /// <summary>
    /// ForceVersion —— 强行跨版本兼容插件（临时方案）。
    ///
    /// 场景：1.4.5.7 客户端（协议 Terraria325）连 1.4.5.6 服务器（协议 Terraria319）。
    ///
    /// ════════════════════════════════════════════════════════════════
    /// 一、握手层（已实现 v1，反编译源码实证）
    /// ════════════════════════════════════════════════════════════════
    ///   客户端 ConnectRequest(1) 包体版本串 "Terraria325" →
    ///   改写为服务器期望的 "Terraria319"（等长 11 字符，长度前缀不动）。
    ///   （服务器 MessageBuffer case 1: reader.ReadString() == "Terraria" + 319）
    ///
    /// ════════════════════════════════════════════════════════════════
    /// 二、进服阶段兼容层（v2 临时方案：先能进服）
    /// ════════════════════════════════════════════════════════════════
    /// 根因：服务器应答 TileGetSection(8)（case 8）时，除区块(10号压缩流)外，
    ///   还会推送一批 1.4.5.7 已改版的包，1.4.5.7 客户端按新格式解析旧格式包全部错位：
    ///
    ///   • SyncItem(21)     —— 1.4.5.6 发送时 flags 恒为 0（writer.Write(number2)，case 8 传 0），
    ///                          1.4.5.7 不读 shimmer/enemyDelay 尾 → 字节兼容 → 保留
    ///   • ItemOwner(22)    —— 旧 10B（index|owner|pos）vs 新 18B+（多 4 字段）→ 错位 → 跳过
    ///   • SyncNPC(23)      —— 旧 short npc + pos + vel... 与 新 byte npc + byte gen + pos... 恰好同宽
    ///                          （npcIndex<256 时 gen=0 对齐）→ 碰巧兼容 → 保留
    ///   • SyncProjectile(27)—— 旧 identity(2B)+owner(1B) vs 新 ProjectileKey(4B) → 全错位 → 跳过
    ///   • NetModule(82)    —— 1.4.5.7 在 Bestiary 后插入 CreativeUnlocks → CreativePowers 起模块 ID 全部 +1
    ///                          （1.4.5.6: CreativePowers=5 Pylon=7 Banners=10；
    ///                            1.4.5.7: CreativePowers=6 Pylon=8 Banners=11）
    ///                          服务器图格阶段发 BannerSystem/CreativePowers/Pylon 82 包 →
    ///                          客户端按新表解析成错误模块 → 跳过（保留 ID ≤ 4 的 Liquid/Text/Ping/Ambience/Bestiary）
    ///
    /// 入站（客户端→服务器）低风险防护：
    ///   • NetModules(82)   —— 1.4.5.7 客户端主动发的模块 ID 位移 → 服务器旧表错位解析 → 丢弃
    ///   • SyncProjectile(27)/KillProjectile(29) —— 新格式（ProjectileKey/死亡坐标）→ 丢弃（弹幕类攻击暂失效）
    ///   • DamageNPC(28)    —— 新 byte npc+byte gen 与旧 short npc 字节数相同，把 gen 字节清零后即与旧格式完全一致
    ///                          → 保留近战/常规伤害能力
    ///
    /// 已知边界（临时方案不做，后续补协议翻译层）：
    ///   • 进服后服务器对全体广播的 21/22/23/27（remoteClient=-1）不在此过滤范围
    ///   • 1.4.5.7 客户端发起的弹幕类攻击（入站 27）被丢弃 → 远程武器/坐骑/宠物弹幕无效
    ///   • 跨版本客户端收不到位移 NetModule（粒子特效/传送门/旗帜/创造模式）
    /// </summary>
    [ApiVersion(2, 1)]
    public class ForceVersionPlugin : TerrariaPlugin
    {
        public override string Author => "lmx12330";
        public override string Description => "强行跨版本兼容：1.4.5.7(325) 客户端进 1.4.5.6(319) 服务器（临时方案）";
        public override string Name => "ForceVersion";
        public override Version Version => new Version(2, 0, 0, 0);

        /// <summary>服务器期望的版本字符串（ConnectRequest 版本检查字段）</summary>
        private const string ServerVersion = "Terraria319";

        /// <summary>需要强行认同的客户端版本字符串</summary>
        private const string ClientVersion = "Terraria325";

        /// <summary>1.4.5.6 中与 1.4.5.7 一致的 NetModule ID 上界（Liquid0/Text1/Ping2/Ambience3/Bestiary4；位移从 CreativePowers=5 起）</summary>
        private const int MaxCompatNetModuleId = 4;

        // 被 1.4.5.7 重写、进服阶段需对跨版本客户端跳过的出站包
        private static readonly HashSet<byte> SkippedOutboundTypes = new HashSet<byte>
        {
            MessageID.ItemOwner,        // 22：变长错位
            MessageID.SyncProjectile    // 27：ProjectileKey 错位
        };

        // 1.4.5.7 客户端发来、服务器旧表无法解析的入站包（直接丢弃）
        private static readonly HashSet<byte> DroppedInboundTypes = new HashSet<byte>
        {
            MessageID.SyncProjectile,   // 27：新格式 → 旧表错位
            MessageID.KillProjectile,   // 29：新格式 → 旧表错位
            MessageID.NetModules        // 82：模块 ID 位移
        };

        /// <summary>已识别为 1.4.5.7 跨版本客户端的连接索引（网络线程访问，lock 保护）</summary>
        private static readonly HashSet<int> _crossVersionClients = new HashSet<int>();
        private static readonly object SyncLock = new object();

        private static Hook? _getDataHook;
        private static Hook? _sendDataHook;
        private static Hook? _sendToClientHook;
        private static Hook? _sendPacketHook;
        private static bool _initialized;

        public ForceVersionPlugin(Main game) : base(game) { }

        public override void Initialize()
        {
            if (_initialized)
                return;

            RegisterGetDataHook();
            RegisterSendDataHook();
            RegisterSendToClientHook();
            RegisterSendPacketHook();
            ServerApi.Hooks.ServerLeave.Register(this, OnServerLeave);

            _initialized = true;
            TShock.Log.ConsoleInfo($"[ForceVersion] 已启用（v2 临时方案）：客户端 {ClientVersion} → 认同 {ServerVersion}，已过滤 22/27/82 错位包并补齐出站 Tile(17) 第 9 字节");
        }

        // ════════════════════════════════════════════════
        //  detour 1：MessageBuffer.GetData —— 入站包总入口
        // ════════════════════════════════════════════════

        private void RegisterGetDataHook()
        {
            try
            {
                var method = typeof(MessageBuffer).GetMethod("GetData",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(int), typeof(int), typeof(int).MakeByRefType() },
                    null);

                if (method == null)
                {
                    TShock.Log.ConsoleError("[ForceVersion] 未找到 MessageBuffer.GetData 方法");
                    return;
                }

                _getDataHook = new Hook(method, OnGetData);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[ForceVersion] GetData Hook 注册失败: {ex.Message}");
            }
        }

        /// <summary>MessageBuffer.GetData 原始委托（实例方法，首个参数为 this）</summary>
        private delegate void OrigGetData(MessageBuffer self, int start, int length, out int messageType);

        private static void OnGetData(OrigGetData orig, MessageBuffer self, int start, int length, out int messageType)
        {
            bool canRead = self.readBuffer != null && start >= 0 && start < self.readBuffer.Length;

            // ConnectRequest(Hello=1)：改写版本串并登记跨版本客户端
            if (canRead && self.readBuffer[start] == MessageID.Hello)
            {
                try
                {
                    var version = TryReadVersion(self.readBuffer, start);
                    if (version == ClientVersion)
                    {
                        RewriteVersion(self.readBuffer, start);
                        lock (SyncLock)
                        {
                            _crossVersionClients.Add(self.whoAmI);
                        }
                        TShock.Log.ConsoleInfo($"[ForceVersion] 客户端 #{self.whoAmI} 版本 {ClientVersion} → 认同 {ServerVersion} 并登记跨版本兼容");
                    }
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleError($"[ForceVersion] ConnectRequest 处理异常: {ex.Message}");
                }
            }

            // 跨版本客户端的入站防护
            bool isCross = false;
            lock (SyncLock)
            {
                isCross = _crossVersionClients.Contains(self.whoAmI);
            }

            if (isCross && canRead)
            {
                byte type = self.readBuffer[start];

                // DamageNPC(28)：新格式 byte npc + byte gen 与旧格式 short npc 字节数相同，
                // gen 字节清零后即与 1.4.5.6 旧格式完全一致 → 保留伤害能力
                if (type == MessageID.DamageNPC && start + 2 < self.readBuffer.Length)
                {
                    self.readBuffer[start + 2] = 0;
                }

                // 无法兼容的新格式包 → 丢弃（不调 orig，TShock 也收不到）
                if (DroppedInboundTypes.Contains(type))
                {
                    messageType = type;
                    return;
                }
            }

            orig(self, start, length, out messageType);
        }

        // ════════════════════════════════════════════════
        //  detour 2：NetMessage.SendData —— 出站定向包过滤（22/27）
        // ════════════════════════════════════════════════

        private void RegisterSendDataHook()
        {
            try
            {
                var method = typeof(NetMessage).GetMethod("SendData",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[]
                    {
                        typeof(int), typeof(int), typeof(int), typeof(NetworkText),
                        typeof(int), typeof(float), typeof(float), typeof(float),
                        typeof(int), typeof(int), typeof(int)
                    },
                    null);

                if (method == null)
                {
                    TShock.Log.ConsoleError("[ForceVersion] 未找到 NetMessage.SendData 方法");
                    return;
                }

                _sendDataHook = new Hook(method, OnSendData);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[ForceVersion] SendData Hook 注册失败: {ex.Message}");
            }
        }

        /// <summary>NetMessage.SendData 原始委托（核心重载，TrySendData 最终调用它）</summary>
        private delegate void OrigSendData(int msgType, int remoteClient, int ignoreClient, NetworkText text,
            int number, float number2, float number3, float number4, int number5, int number6, int number7);

        private static void OnSendData(OrigSendData orig, int msgType, int remoteClient, int ignoreClient, NetworkText text,
            int number, float number2, float number3, float number4, int number5, int number6, int number7)
        {
            // 定向发给跨版本客户端的 22/27 错位包 → 跳过（case 8 进服同步即定向发送）
            if (remoteClient >= 0 && SkippedOutboundTypes.Contains((byte)msgType))
            {
                bool cross;
                lock (SyncLock)
                {
                    cross = _crossVersionClients.Contains(remoteClient);
                }

                if (cross)
                {
                    TShock.Log.ConsoleDebug($"[ForceVersion] 跳过发给 #{remoteClient} 的包 {msgType}（1.4.5.7 格式不兼容）");
                    return;
                }
            }

            orig(msgType, remoteClient, ignoreClient, text, number, number2, number3, number4, number5, number6, number7);
        }

        // ════════════════════════════════════════════════
        //  detour 3：NetManager.SendToClient —— 出站 NetModule 位移过滤
        // ════════════════════════════════════════════════
        private void RegisterSendToClientHook()
        {
            try
            {
                var method = typeof(NetManager).GetMethod("SendToClient",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(NetPacket), typeof(int) },
                    null);

                if (method == null)
                {
                    TShock.Log.ConsoleError("[ForceVersion] 未找到 NetManager.SendToClient 方法");
                    return;
                }

                _sendToClientHook = new Hook(method, OnSendToClient);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[ForceVersion] SendToClient Hook 注册失败: {ex.Message}");
            }
        }

        /// <summary>NetManager.SendToClient 原始委托（图格阶段 BannerSystem/CreativePowers/Pylon 均走此定向发送）</summary>
        private delegate void OrigSendToClient(NetManager self, NetPacket packet, int playerId);

        private static void OnSendToClient(OrigSendToClient orig, NetManager self, NetPacket packet, int playerId)
        {
            bool cross;
            lock (SyncLock)
            {
                cross = _crossVersionClients.Contains(playerId);
            }

            // NetPacket 为值类型，无需 null 检查；Length >= 5 可排除 default(NetPacket)
            if (cross && packet.Length >= 5)
            {
                try
                {
                    // NetPacket 布局: [0..1]=长度(ushort) [2]=0x82 [3..4]=moduleId(ushort LE)
                    ushort moduleId = BitConverter.ToUInt16(packet.Buffer.Data, 3);
                    if (moduleId > MaxCompatNetModuleId)
                    {
                        // 1.4.5.7 模块 ID 位移（CreativePowers 起 +1）→ 客户端按新表错解 → 跳过并回收
                        TShock.Log.ConsoleDebug($"[ForceVersion] 跳过发给 #{playerId} 的 NetModule 模块 {moduleId}（1.4.5.7 已位移）");
                        try { packet.Recycle(); }
                        catch { }
                        return;
                    }
                }
                catch (Exception ex)
                {
                    // 解析异常绝不影响 orig 正常发包
                    TShock.Log.ConsoleDebug($"[ForceVersion] NetModule 检测异常（放行）: {ex.Message}");
                }
            }

            orig(self, packet, playerId);
        }

        // ════════════════════════════════════════════════
        //  detour 4：NetMessage.SendPacket —— 出站 Tile(17) 补第 9 字节
        // ════════════════════════════════════════════════

        private void RegisterSendPacketHook()
        {
            try
            {
                var method = typeof(NetMessage).GetMethod("SendPacket",
                    BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    new[] { typeof(byte[]), typeof(int) },
                    null);

                if (method == null)
                {
                    TShock.Log.ConsoleError("[ForceVersion] 未找到 NetMessage.SendPacket 方法");
                    return;
                }

                _sendPacketHook = new Hook(method, OnSendPacket);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[ForceVersion] SendPacket Hook 注册失败: {ex.Message}");
            }
        }

        /// <summary>NetMessage.SendPacket 原始委托（所有出站包最终都会走这里）</summary>
        private delegate void OrigSendPacket(byte[] data, int remoteClient);

        /// <summary>
        /// 出站包最终发送点。data 布局：
        ///   [0..1] = 包总长(ushort LE)  [2] = 类型字节  [3..] = payload
        /// 对 TileManipulation(17)：payload = action(1) x(2) y(2) type(2) style(1) = 8 字节。
        /// 1.4.5.7 客户端按 9 字节解析（新增第 9 字节），服务器发 8 字节会导致其解析错位/越界
        /// → 本地 Tile 状态不更新 → 破坏方块看起来无效。
        /// 修复：对跨版本客户端补第 9 字节（值填 0，语义待 1.4.5.7 反编译确认）。
        /// </summary>
        private static void OnSendPacket(OrigSendPacket orig, byte[] data, int remoteClient)
        {
            // 定向发给跨版本客户端的 Tile(17) → 8 字节补成 9 字节
            if (remoteClient >= 0 && data != null && data.Length > 3 && data[2] == MessageID.TileManipulation)
            {
                bool cross;
                lock (SyncLock)
                {
                    cross = _crossVersionClients.Contains(remoteClient);
                }

                if (cross)
                {
                    try
                    {
                        // 当前 payload 长度 = data[0..1] - 3（2 字节长度前缀 + 1 字节类型）
                        int len = BitConverter.ToUInt16(data, 0);
                        if (len == 11) // 2 + 1 + 8 payload = 11
                        {
                            byte[] newData = new byte[12];
                            // 更新长度前缀 11 → 12
                            newData[0] = 12;
                            newData[1] = 0;
                            // 复制类型字节 + 原 8 字节 payload
                            Array.Copy(data, 2, newData, 2, 9);
                            // 补第 9 字节（默认 0）
                            newData[11] = 0;

                            TShock.Log.ConsoleDebug($"[ForceVersion] 发给 #{remoteClient} 的 Tile(17) 已补第 9 字节（8→9）");
                            orig(newData, remoteClient);
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        TShock.Log.ConsoleDebug($"[ForceVersion] Tile(17) 补字节异常（原样发送）: {ex.Message}");
                    }
                }
            }

            orig(data, remoteClient);
        }

        // ════════════════════════════════════════════════
        //  生命周期
        // ════════════════════════════════════════════════

        private void OnServerLeave(LeaveEventArgs args)
        {
            lock (SyncLock)
            {
                if (_crossVersionClients.Remove(args.Who))
                {
                    TShock.Log.ConsoleInfo($"[ForceVersion] 跨版本客户端 #{args.Who} 已离开，清理登记");
                }
            }
        }

        protected override void Dispose(bool Disposing)
        {
            if (Disposing)
            {
                try { _getDataHook?.Dispose(); }
                catch { }
                try { _sendDataHook?.Dispose(); }
                catch { }
                try { _sendToClientHook?.Dispose(); }
                catch { }
                try { _sendPacketHook?.Dispose(); }
                catch { }
                _getDataHook = null;
                _sendDataHook = null;
                _sendToClientHook = null;
                _sendPacketHook = null;

                try { ServerApi.Hooks.ServerLeave.Deregister(this, OnServerLeave); }
                catch { }

                lock (SyncLock)
                {
                    _crossVersionClients.Clear();
                }
                _initialized = false;
                TShock.Log.ConsoleInfo("[ForceVersion] 已卸载");
            }
            base.Dispose(Disposing);
        }

        // ════════════════════════════════════════════════
        //  工具方法
        // ════════════════════════════════════════════════

        /// <summary>
        /// 包布局（start 指向包类型字节）：
        ///   readBuffer[start]     = 包类型
        ///   readBuffer[start + 1] = 版本串长度（7bit 编码，版本串 < 128 时单字节）
        ///   readBuffer[start + 2] = 版本串内容（ASCII）
        /// </summary>
        private static string? TryReadVersion(byte[] buf, int start)
        {
            if (start + 1 >= buf.Length)
                return null;

            int len = buf[start + 1];
            if (len < 8 || len > 16)
                return null;

            int content = start + 2;
            if (content + len > buf.Length)
                return null;

            try
            {
                return Encoding.ASCII.GetString(buf, content, len);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>等长改写版本串（Terraria325 → Terraria319，长度前缀无需变动）</summary>
        private static void RewriteVersion(byte[] buf, int start)
        {
            int content = start + 2;
            for (int i = 0; i < ServerVersion.Length; i++)
                buf[content + i] = (byte)ServerVersion[i];
        }
    }
}
