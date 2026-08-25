using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Microsoft.Xna.Framework;
using MonoMod.RuntimeDetour;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.Net;
using TerrariaApi.Server;
using TShockAPI;

namespace Compat1456
{
    /// <summary>
    /// Compat1456 —— 反向跨版本兼容插件（分批交付 v1）。
    ///
    /// 场景：1.4.5.6 客户端（协议 Terraria319）连 1.4.5.7 服务器（协议 Terraria325）。
    /// 与 plugin-son/ForceVersion（新客户端→旧服务器）方向相反：本插件跑在【新(1.4.5.7)服务器】上，
    /// 把服务器的【新格式包】翻译成旧格式发给旧客户端，并把旧客户端上行按服务器可接受的方式处理。
    ///
    /// ════════════════════════════════════════════════════════════════
    /// 一、握手层（同 ForceVersion 反向）
    /// ════════════════════════════════════════════════════════════════
    ///   客户端 ConnectRequest(1) 包体版本串 "Terraria319" →
    ///   改写为服务器期望的 "Terraria325"（等长 11 字符，长度前缀不动）。
    ///   （1.4.5.7 版本检查是硬拒绝，Terraria318→325 不匹配直接 Boot）
    ///
    /// ════════════════════════════════════════════════════════════════
    /// 二、协议翻译层（v1：出站字节级翻译 + 入站等长翻译/丢弃）
    /// ════════════════════════════════════════════════════════════════
    /// 依据：《Terraria-1457-源码解读记录》（本地 scripts/，含全部 1.4.5.7 包格式）
    ///   + 新版 TShock(update otapi) GetDataHandlers.cs（1.4.5.7 服务器实现）
    ///
    /// 出站（新服务器 → 旧客户端，在 NetMessage.SendPacket 最终字节层统一翻译，可自由重建数组）：
    ///   • Tile(17)      —— 1.4.5.7 出站 body=9B(action,x,y,type,style,+第9字节)；
    ///                       旧客户端按 8B 解析，多 1 字节残留会破坏流对齐 → 裁掉第 9 字节
    ///   • SyncProjectile(27) —— 新 ProjectileKey(4B) → 旧 identity(2B)+owner(1B)（重排）
    ///   • KillProjectile(29) —— 新 key(4B)+deathPos(8B) → 旧 identity(2B)+owner(1B)
    ///   • SyncNPC(23)   —— 新 byte npc+byte gen 与 旧 short npc 同宽；把 gen 字节清零
    ///                       （npc<256 时旧客户端低字节=npc 正确）；SyncAnchor 位置换算留 v2
    ///   • ItemOwner(22) —— 新 body 中间 3 字段(timeToKeep/grabDelayPlayer/grabDelayTime)
    ///                       确切字节宽度未实证 → v1 保守【跳过】（同 ForceVersion 正向）
    ///   • NetModule(82) —— 模块 ID 位移：新 ID≥6 → -1（CreativePowers 起整体 +1）；新 ID==5
    ///                       (CreativeUnlocks) 旧客户端不存在 → 过滤
    ///   • DamageNPCAck(162) —— 旧客户端 MessageID 只到 161，不认识 → 过滤
    ///
    /// 入站（旧客户端 → 新服务器，在 MessageBuffer.GetData 层；readBuffer 共享缓冲，
    ///   只能等长改写，不能安全增长/缩短包体）：
    ///   • Hello(1)      —— 版本串 319 → 325 改写 + 登记兼容客户端
    ///   • NetModule(82) —— 模块 ID 旧→新：旧 ID≥5 → +1（等长，可直接改 2 字节）
    ///   • SyncProjectile(27)/KillProjectile(29) —— 旧格式→新格式需要包体增长（27 增 1B、29 增 9B）
    ///       readBuffer 内无法安全插入 → v1 【丢弃】（旧客户端弹幕类攻击/同步失效，同 ForceVersion 边界）
    ///   • ItemOwner(22) —— 新版服务器(TShock)只读前 3B(id+owner)，旧包足够 → 透传
    ///   • DamageNPC(28) —— 旧 short npc(2B) 与 新 byte npc+byte gen(2B) 同宽且低字节=npc，
    ///                       npc<256 时字节兼容 → 透传
    ///   • Tile(17)/ItemDrop(21) —— 入站均为 8B / 旧 flags(无 shimmer/enemyDelay 尾)，
    ///                       新版服务器按 8B / flags[2][3]==0 不读可选尾 → 透传
    ///
    /// ════════════════════════════════════════════════════════════════
    /// 三、ProjectileKey 位布局（1457 文档 §5.2）
    /// ════════════════════════════════════════════════════════════════
    ///   Spawner(8bit) | Index(10bit) | Generation(14bit) 打包成一个 int32。
    ///   本文按 key = (Spawner<<24) | (Index<<14) | Generation 解包。
    ///   ⚠️ 位偏移为推断，若实测与 Terraria.DataStructures.ProjectileKey 不符，仅需改
    ///      UnpackKey 一个方法。
    ///
    /// ════════════════════════════════════════════════════════════════
    /// 四、已知边界（v1，待 v2 迭代）
    /// ════════════════════════════════════════════════════════════════
    ///   • 旧客户端弹幕攻击/同步失效（入站 27/29 丢弃）
    ///   • 服务器对全体广播(remoteClient=-1)的新格式包无法按客户端翻译（旧客户端可能偶发错位）
    ///   • ItemOwner(22) 出站跳过（物品拾取归属同步弱化）
    ///   • SyncNPC(23) 未做 SyncAnchor 位置换算（大怪可能位置偏移，多数怪 Anchor=0 等价）
    ///   • 聊天(25) 在 1.4.5.6/1.4.5.7 间格式差异尚未实证（本插件未处理）
    /// </summary>
    [ApiVersion(2, 1)]
    public class Compat1456Plugin : TerrariaPlugin
    {
        public override string Author => "lmx12330";
        public override string Description => "跨版本兼容：325/326 原生放行，319(Terraria319) 翻译后进 1.4.5.7(Terraria325) 服务器";
        public override string Name => "Compat1456";
        public override Version Version => new Version(1, 0, 0, 0);

        /// <summary>服务器期望的版本字符串（ConnectRequest 版本检查字段）——动态获取，见 GetServerVersion()。
        /// 1.4.5.7 = "Terraria325"；1.4.5.8 若只改双端逻辑不改网络，期望值大概率仍为 Terraria325。
        /// ⚠️ 编译期直接引用 Main.curRelease 会因 const 内联成 325，故必须反射运行时读取，
        ///    这样 1.4.5.8 即便改了协议号也能自动跟随（仅需刷新日志核对）。</summary>
        private const string FallbackServerVersion = "Terraria325";

        /// <summary>需要强行认同的旧客户端版本字符串（1.4.5.6）</summary>
        private const string ClientVersion = "Terraria319";

        /// <summary>协议与服务器完全一致的透传客户端版本字符串（1.4.5.7 变体构建）：
        /// 包格式与 325 完全相同，仅改写版本串通过校验即可，无需任何翻译。</summary>
        private const string PassthroughVersion = "Terraria326";

        /// <summary>
        /// 旧(1.4.5.6)表中与新版一致的 NetModule ID 上界（Liquid0/Text1/Ping2/Ambience3/Bestiary4；
        /// 1.4.5.7 恢复 NetCreativeUnlocksModule 插在 Bestiary 与 CreativePowers 之间 → CreativePowers 起整体 +1）。
        /// 新→旧：ID≥6 → -1；新 ID==5(CreativeUnlocks) 旧客户端不存在 → 过滤。
        /// 旧→新：ID≥5 → +1。
        /// </summary>
        private const int MaxCompatNetModuleId = 4;

        /// <summary>已识别为 1.4.5.6 兼容客户端的连接索引（网络线程访问，lock 保护）</summary>
        private static readonly HashSet<int> _compatClients = new HashSet<int>();

        /// <summary>运行时读取的 Main.curRelease（服务器当前协议号；读取失败为 null）</summary>
        private static int? _serverCurRelease;

        /// <summary>缓存：服务器期望协议版本串（"Terraria" + curRelease）</summary>
        private static string? _serverVersion;
        private static readonly object SyncLock = new object();

        private static Hook? _getDataHook;
        private static Hook? _sendToClientHook;
        private static Hook? _sendPacketHook;
        private static bool _initialized;

        public Compat1456Plugin(Main game) : base(game) { }

        public override void Initialize()
        {
            if (_initialized)
                return;

            RegisterGetDataHook();
            RegisterSendToClientHook();
            RegisterSendPacketHook();
            ServerApi.Hooks.ServerLeave.Register(this, OnServerLeave);

            _initialized = true;
            TShock.Log.ConsoleInfo($"[Compat1456] 已启用：服务器协议 {GetServerVersion()}（Main.curRelease={_serverCurRelease?.ToString() ?? "<反射失败>"}，游戏 {Main.versionNumber}）");
            TShock.Log.ConsoleInfo($"[Compat1456] 放行策略：325 原生直进｜326({PassthroughVersion}) 仅改版本串放行、不翻译｜319({ClientVersion}) 改版本串+登记翻译（17/23/27/29/82/162）");
        }

        // ════════════════════════════════════════════════
        //  detour 1：MessageBuffer.GetData —— 入站包总入口
        //  （版本改写 + 登记 + 等长翻译/丢弃）
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
                    TShock.Log.ConsoleError("[Compat1456] 未找到 MessageBuffer.GetData 方法");
                    return;
                }

                _getDataHook = new Hook(method, OnGetData);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[Compat1456] GetData Hook 注册失败: {ex.Message}");
            }
        }

        /// <summary>MessageBuffer.GetData 原始委托（实例方法，首个参数为 this）</summary>
        private delegate void OrigGetData(MessageBuffer self, int start, int length, out int messageType);

        private static void OnGetData(OrigGetData orig, MessageBuffer self, int start, int length, out int messageType)
        {
            bool canRead = self.readBuffer != null && start >= 0 && start < self.readBuffer.Length;

            // ConnectRequest(Hello=1)：改写版本串并登记兼容客户端
            if (canRead && self.readBuffer[start] == MessageID.Hello)
            {
                try
                {
                    var version = TryReadVersion(self.readBuffer, start);
                    if (version != null)
                    {
                        // 进服前（握手阶段）打印协议号：客户端上报的版本串 + 服务器当前期望的版本串
                        TShock.Log.ConsoleInfo($"[Compat1456] 进服前握手：客户端 #{self.whoAmI} 上报协议 {version}，服务器期望协议 {GetServerVersion()}（Main.curRelease={_serverCurRelease?.ToString() ?? "<反射失败>"}）");
                    }
                    if (version == ClientVersion)
                    {
                        RewriteVersion(self.readBuffer, start);
                        lock (SyncLock)
                        {
                            _compatClients.Add(self.whoAmI);
                        }
                        TShock.Log.ConsoleInfo($"[Compat1456] 客户端 #{self.whoAmI} 协议 {version} → 已改写为服务器期望 {GetServerVersion()} 并登记反向兼容（翻译）");
                    }
                    else if (version == PassthroughVersion)
                    {
                        // 326 与服务器协议(325)完全一致：仅改写版本串通过校验，不登记翻译（不做任何包翻译）
                        RewriteVersion(self.readBuffer, start);
                        TShock.Log.ConsoleInfo($"[Compat1456] 客户端 #{self.whoAmI} 协议 {version} → 已改写为服务器期望 {GetServerVersion()} 放行（协议一致，无需翻译）");
                    }
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleError($"[Compat1456] ConnectRequest 处理异常: {ex.Message}");
                }
            }

            bool isCompat = false;
            lock (SyncLock)
            {
                isCompat = _compatClients.Contains(self.whoAmI);
            }

            if (isCompat && canRead)
            {
                byte type = self.readBuffer[start];

                // NetModule(82) 入站：旧客户端按旧表发模块 ID → 服务器按新表读 → 旧 ID≥5 时 +1（等长 2B）
                // ⚠️ 82 包布局 = [type][moduleId(2B ushort LE)][payload] → moduleId 在 start+1（type 后第一字节），不是 start+2！
                if (type == MessageID.NetModules && start + 3 < self.readBuffer.Length)
                {
                    ushort moduleId = BitConverter.ToUInt16(self.readBuffer, start + 1);
                    if (moduleId > MaxCompatNetModuleId)
                    {
                        ushort newModuleId = (ushort)(moduleId + 1);
                        self.readBuffer[start + 1] = (byte)newModuleId;
                        self.readBuffer[start + 2] = (byte)(newModuleId >> 8);
                        TShock.Log.ConsoleDebug($"[Compat1456] 入站 NetModule 模块 {moduleId} → {newModuleId}（旧→新位移）");
                    }
                }

                // DamageNPC(28) 入站：旧 short npc(2B) 与 新 byte npc+byte gen(2B) 同宽（低字节=npc）。
                // 新服务器校验 gen==Main.npc[npc].generation（1.4.5.7 case 28 实证），旧客户端发的 gen 字节
                // （旧 npc 高位，通常 0）不匹配 → 伤害被拒 → “打到怪没反应”。修复：把 gen 字节填成当前槽位 generation
                if (type == MessageID.DamageNPC && start + 3 < self.readBuffer.Length)
                {
                    byte npc = self.readBuffer[start + 1];
                    if (npc < Main.npc.Length && Main.npc[npc] != null)
                    {
                        byte gen = GetNpcGeneration(Main.npc[npc]);
                        self.readBuffer[start + 2] = gen;
                        TShock.Log.ConsoleDebug($"[Compat1456] 入站 DamageNPC(28) gen 补全 → {gen}");
                    }
                }

                // 弹幕 27/29：旧格式→新格式需增长包体，readBuffer 内无法安全插入
                // → 语义级重放：解析旧格式，调用服务器弹幕 API（Projectile.NewProjectileSetup 等）创建/灭除并广播
                if (type == MessageID.SyncProjectile) // 27 创建弹幕
                {
                    HandleInboundProjectileNew(self, start);
                    messageType = type;
                    return;
                }
                if (type == MessageID.KillProjectile) // 29 灭弹
                {
                    HandleInboundProjectileKill(self, start);
                    messageType = type;
                    return;
                }
            }

            orig(self, start, length, out messageType);
        }

        // ════════════════════════════════════════════════
        //  detour 2：NetManager.SendToClient —— 出站 NetModule 位移翻译
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
                    TShock.Log.ConsoleError("[Compat1456] 未找到 NetManager.SendToClient 方法");
                    return;
                }

                _sendToClientHook = new Hook(method, OnSendToClient);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[Compat1456] SendToClient Hook 注册失败: {ex.Message}");
            }
        }

        /// <summary>NetManager.SendToClient 原始委托（定向 NetModule 走此通道）</summary>
        private delegate void OrigSendToClient(NetManager self, NetPacket packet, int playerId);

        private static void OnSendToClient(OrigSendToClient orig, NetManager self, NetPacket packet, int playerId)
        {
            bool isCompat;
            lock (SyncLock)
            {
                isCompat = _compatClients.Contains(playerId);
            }

            if (isCompat && packet.Length >= 5)
            {
                try
                {
                    // NetPacket 布局: [0..1]=长度(ushort) [2]=0x82 [3..4]=moduleId(ushort LE)
                    if (packet.Buffer.Data[2] == MessageID.NetModules)
                    {
                        ushort moduleId = BitConverter.ToUInt16(packet.Buffer.Data, 3);
                        if (moduleId == 5)
                        {
                            // CreativeUnlocks 是 1.4.5.7 新恢复的模块，旧客户端不存在 → 过滤
                            TShock.Log.ConsoleDebug($"[Compat1456] 跳过发给 #{playerId} 的 NetModule 模块 5(CreativeUnlocks)（旧客户端不存在）");
                            try { packet.Recycle(); }
                            catch { }
                            return;
                        }
                        if (moduleId >= 6)
                        {
                            ushort oldModuleId = (ushort)(moduleId - 1);
                            packet.Buffer.Data[3] = (byte)oldModuleId;
                            packet.Buffer.Data[4] = (byte)(oldModuleId >> 8);
                            TShock.Log.ConsoleDebug($"[Compat1456] 发给 #{playerId} 的 NetModule 模块 {moduleId} → {oldModuleId}（新→旧位移）");
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 解析异常绝不影响 orig 正常发包
                    TShock.Log.ConsoleDebug($"[Compat1456] NetModule 检测异常（放行）: {ex.Message}");
                }
            }

            orig(self, packet, playerId);
        }

        // ════════════════════════════════════════════════
        //  detour 3：NetMessage.SendPacket —— 出站包最终发送点（字节级翻译）
        // ════════════════════════════════════════════════
        private void RegisterSendPacketHook()
        {
            try
            {
                // ⚠️ 跨版本坑：1.4.5.6 的 NetMessage.SendPacket 是 private（ForceVersion 用 NonPublic 能找）；
                //    1.4.5.7（update otapi / OTAPI3 打包）把它改成了 public static（OTAPI hook 包装，内部
                //    调 mfwh_SendPacket → InvokeSendBytes → socket）。用 NonPublic 找 1.4.5.7 会失败
                //    （实测日志“未找到 NetMessage.SendPacket 方法”→ 出站翻译全部失效 → 旧客户端收未翻译新格式包
                //    → 卡图格/闪退）。必须 Public|NonPublic 都找。
                var method = typeof(NetMessage).GetMethod("SendPacket",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    new[] { typeof(byte[]), typeof(int) },
                    null);

                if (method == null)
                {
                    TShock.Log.ConsoleError("[Compat1456] 未找到 NetMessage.SendPacket 方法");
                    return;
                }

                _sendPacketHook = new Hook(method, OnSendPacket);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[Compat1456] SendPacket Hook 注册失败: {ex.Message}");
            }
        }

        /// <summary>NetMessage.SendPacket 原始委托（所有出站包最终都会走这里）</summary>
        private delegate void OrigSendPacket(byte[] data, int remoteClient);

        /// <summary>
        /// 出站包最终发送点。data 布局：[0..1]=包总长(ushort LE) [2]=类型字节 [3..]=payload。
        /// 仅对【定向】发给兼容客户端的包做翻译；广播(remoteClient=-1)无法区分接收者，不做处理。
        /// </summary>
        private static void OnSendPacket(OrigSendPacket orig, byte[] data, int remoteClient)
        {
            // data.Length >= 3 即含类型字节；22/162 等 body 很小或为空的包也必须能进入翻译
            if (remoteClient >= 0 && data != null && data.Length >= 3)
            {
                bool isCompat;
                lock (SyncLock)
                {
                    isCompat = _compatClients.Contains(remoteClient);
                }

                if (isCompat)
                {
                    byte type = data[2];
                    try
                    {
                        switch (type)
                        {
                            case (byte)MessageID.TileManipulation: // 17
                                // 新出站 body=9B(action,x,y,type,style,+第9字节)；旧客户端按 8B 解析。
                                // 完整包长 12（2 长度 + 1 类型 + 9 body）→ 裁第 9 字节 → 11。
                                if (data.Length == 12 && BitConverter.ToUInt16(data, 0) == 12)
                                {
                                    byte[] nd = new byte[11];
                                    nd[0] = 11; nd[1] = 0;
                                    Array.Copy(data, 2, nd, 2, 9); // 类型 + 前 8B body
                                    TShock.Log.ConsoleDebug($"[Compat1456] 发给 #{remoteClient} 的 Tile(17) 已裁第 9 字节（9→8）");
                                    orig(nd, remoteClient);
                                    return;
                                }
                                break;

                            case (byte)PacketTypes.ItemDrop: // 21
                                // 新 body: index(2) pos(8) vel(8) stack(2) prefix(1) flags(1) type(2) [可选尾]
                                // 旧 body: index(2) pos(8) vel(8) stack(2) prefix(1) flags(1) type(2)（无尾, flags=0）
                                // 1.4.5.7 flags 低2位=NewItemOwnership、bit2=shimmer、bit3=enemyGrabDelay → 旧客户端误读错位
                                // 翻译: 截断到 24B body（去可选尾）+ flags 置 0（回到 ForceVersion 验证过的 flags=0 兼容态）
                                if (data.Length >= 3 + 24)
                                {
                                    byte[] nd = new byte[3 + 24];
                                    int oldLen = 3 + 24;
                                    nd[0] = (byte)oldLen; nd[1] = 0; nd[2] = 21;
                                    Array.Copy(data, 3, nd, 3, 24);   // 完整 24B body
                                    nd[3 + 21] = 0;                   // flags(body offset 21) 置 0
                                    TShock.Log.ConsoleDebug($"[Compat1456] 发给 #{remoteClient} 的 SyncItem(21) 已翻译（去尾+flags清0）");
                                    orig(nd, remoteClient);
                                    return;
                                }
                                break;

                            case (byte)MessageID.ItemOwner: // 22
                                // 新 body: index(2) owner(1) [timeToKeep/grabDelayPlayer/grabDelayTime 任意宽度] position(8)
                                // 旧 body: index(2) owner(1) position(8) = 11B
                                // 截断不依赖中间字段宽度：取头 3 字节 + 尾 8 字节（1457 文档 §4.4：position 是最后字段）
                                if (data.Length >= 3 + 11)
                                {
                                    byte[] nd = new byte[3 + 11];
                                    int oldLen = 3 + 11;
                                    nd[0] = (byte)oldLen; nd[1] = 0; nd[2] = 22;
                                    Array.Copy(data, 3, nd, 3, 3);                 // index(2)+owner(1)
                                    Array.Copy(data, data.Length - 8, nd, 3 + 3, 8); // position(8)
                                    TShock.Log.ConsoleDebug($"[Compat1456] 发给 #{remoteClient} 的 ItemOwner(22) 已截断（新→旧 头3+尾8）");
                                    orig(nd, remoteClient);
                                    return;
                                }
                                break;

                            case (byte)MessageID.SyncProjectile: // 27
                                TranslateProjectileNew(data, remoteClient, orig);
                                return;

                            case (byte)MessageID.KillProjectile: // 29
                                TranslateProjectileKill(data, remoteClient, orig);
                                return;

                            case (byte)MessageID.DamageNPC: // 28
                                // 新 byte npc + byte gen → 旧 short npc（低字节=npc）。gen 字节清零，
                                // 否则旧客户端读 short npc = npc + gen*256 > 255 → 越界崩溃（PE 打到怪闪退）
                                // ⚠️ 必须复制数组再改：广播时 SendData 循环复用同一 writeBuffer 发给所有客户端
                                //   （1.4.5.7 反编译实证），原地改会污染排在后面的原生 325 客户端
                                //   → 部分玩家看怪状态不一致。gen==0 时无需翻译，直接放行不复制。
                                if (data.Length > 4 && data[4] != 0)
                                {
                                    byte[] nd = new byte[data.Length];
                                    Array.Copy(data, nd, data.Length);
                                    nd[4] = 0; // nd[3]=npc, nd[4]=gen
                                    TShock.Log.ConsoleDebug($"[Compat1456] 发给 #{remoteClient} 的 DamageNPC(28) 已翻译（复制+gen清零）");
                                    orig(nd, remoteClient);
                                    return;
                                }
                                break;

                            case (byte)PacketTypes.NpcUpdate: // 23
                                TranslateSyncNPC(data, remoteClient, orig);
                                return;

                            case 162: // DamageNPCAck —— 1.4.5.7 新增，旧客户端 MessageID 只到 161
                                TShock.Log.ConsoleDebug($"[Compat1456] 跳过发给 #{remoteClient} 的 DamageNPCAck(162)（旧客户端不识别）");
                                return;
                        }
                    }
                    catch (Exception ex)
                    {
                        TShock.Log.ConsoleDebug($"[Compat1456] SendPacket 翻译异常（原样发送）: {ex.Message}");
                    }
                }
            }

            orig(data, remoteClient);
        }

        // ════════════════════════════════════════════════
        //  弹幕翻译：ProjectileKey(新) ↔ identity+owner(旧)
        // ════════════════════════════════════════════════

        /// <summary>
        /// 从 ProjectileKey(int32) 解包。
        /// ⚠️ 1.4.5.7 ProjectileKey 真实位布局（反编译 Terraria.DataStructures.ProjectileKey 实证）：
        ///    bits(32bit): Spawner = bits & 0xFF (位0-7) | Index = (bits>>8) & 0x3FF (位8-17) | Generation = (bits>>18) & 0x3FFF (位18-31)
        ///    旧版(1.4.5.6 我此前推断 Spawner 高位)是错的，已按真实布局修正。
        /// </summary>
        private static void UnpackKey(int key, out byte spawner, out int index)
        {
            spawner = (byte)(key & 0xFF);
            index = (key >> 8) & 0x3FF;
        }

        /// <summary>
        /// 出站 SyncProjectile(27)：新 body = key(4) pos(8) vel(8) type(2) flags(1) rest
        ///                      → 旧 body = ident(2) pos(8) vel(8) owner(1) type(2) flags(1) rest
        /// ident = key.Index、owner = key.Spawner。完整包短 1B（4→3）。
        /// </summary>
        private static void TranslateProjectileNew(byte[] data, int remoteClient, OrigSendPacket orig)
        {
            if (data.Length < 3 + 4 + 8 + 8 + 2 + 1)
            {
                orig(data, remoteClient);
                return;
            }

            int key = BitConverter.ToInt32(data, 3);
            UnpackKey(key, out byte spawner, out int index);

            int bodyLen = data.Length - 3;
            int restStart = 4 + 8 + 8 + 2 + 1;          // 新 body 前部（key+pos+vel+type+flags）
            int restLen = bodyLen - restStart;

            int oldBodyLen = 2 + 8 + 8 + 1 + 2 + 1 + restLen; // ident+pos+vel+owner+type+flags+rest
            int oldLen = 3 + oldBodyLen;
            if (oldLen > byte.MaxValue)
            {
                // 超长保护（理论上不会发生，弹幕包 body 有限）
                orig(data, remoteClient);
                return;
            }

            byte[] nd = new byte[oldLen];
            nd[0] = (byte)oldLen;
            nd[1] = 0;
            nd[2] = 27;

            // ident(2, LE)
            nd[3] = (byte)(index & 0xFF);
            nd[4] = (byte)((index >> 8) & 0xFF);
            // pos
            Array.Copy(data, 3 + 4, nd, 5, 8);
            // vel
            Array.Copy(data, 3 + 4 + 8, nd, 13, 8);
            // owner
            nd[21] = spawner;
            // type(2)
            Array.Copy(data, 3 + 4 + 8 + 8, nd, 22, 2);
            // flags(1)
            nd[24] = data[3 + 4 + 8 + 8 + 2];
            // rest
            if (restLen > 0)
                Array.Copy(data, 3 + restStart, nd, 25, restLen);

            TShock.Log.ConsoleDebug($"[Compat1456] 发给 #{remoteClient} 的 SyncProjectile(27) 已翻译 key→identity/owner");
            orig(nd, remoteClient);
        }

        /// <summary>
        /// 出站 KillProjectile(29)：新 body = key(4) deathPos(8) → 旧 body = ident(2) owner(1)。
        /// 死亡坐标旧协议没有，忽略。
        /// </summary>
        private static void TranslateProjectileKill(byte[] data, int remoteClient, OrigSendPacket orig)
        {
            if (data.Length < 3 + 12)
            {
                orig(data, remoteClient);
                return;
            }

            int key = BitConverter.ToInt32(data, 3);
            UnpackKey(key, out byte spawner, out int index);

            byte[] nd = new byte[3 + 3];
            nd[0] = 6; // 完整包长 6（2 长度 + 1 类型 + 3 body）
            nd[1] = 0;
            nd[2] = 29;
            nd[3] = (byte)(index & 0xFF);
            nd[4] = (byte)((index >> 8) & 0xFF);
            nd[5] = spawner;

            TShock.Log.ConsoleDebug($"[Compat1456] 发给 #{remoteClient} 的 KillProjectile(29) 已翻译 key→identity/owner");
            orig(nd, remoteClient);
        }

        // ════════════════════════════════════════════════
        //  弹幕语义级重放（入站 27/29）—— 旧客户端创建/灭除弹幕
        // ════════════════════════════════════════════════

        // 反射缓存（1.4.5.7 专用 API：ProjectileKey 等，编译期 TShock 6.1.0 不存在）
        private static Type? _projKeyType;
        private static ConstructorInfo? _projKeyCtor;   // ProjectileKey(int,int,int)
        private static MethodInfo? _projKeyTryGet;      // bool TryGet(out Projectile)
        private static MethodInfo? _projNewSetup;       // Projectile NewProjectileSetup(ProjectileKey)
        private static MethodInfo? _projFinalize;       // void FinalizeProjectile()
        private static bool _projReflectInit;

        /// <summary>懒加载弹幕反射（仅在收到旧客户端 27/29 时执行一次）</summary>
        private static void InitProjectileReflection()
        {
            if (_projReflectInit)
                return;
            _projReflectInit = true;
            try
            {
                _projKeyType = FindType("Terraria.DataStructures.ProjectileKey");
                if (_projKeyType == null)
                {
                    TShock.Log.ConsoleError("[Compat1456] 未找到 ProjectileKey 类型，弹幕语义级重放不可用");
                    return;
                }
                _projKeyCtor = _projKeyType.GetConstructor(new[] { typeof(int), typeof(int), typeof(int) });
                _projKeyTryGet = _projKeyType.GetMethod("TryGet", new[] { typeof(Projectile).MakeByRefType() });
                _projNewSetup = typeof(Projectile).GetMethod("NewProjectileSetup",
                    BindingFlags.Public | BindingFlags.Static, null, new[] { _projKeyType }, null);
                _projFinalize = typeof(Projectile).GetMethod("FinalizeProjectile",
                    BindingFlags.Public | BindingFlags.Instance);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[Compat1456] 弹幕反射初始化异常: {ex.Message}");
            }
        }

        private static object CreateProjectileKey(int spawner, int index, int generation)
        {
            return _projKeyCtor!.Invoke(new object[] { spawner, index, generation });
        }

        private static bool ProjectileTryGet(object key, out Projectile? proj)
        {
            proj = null;
            try
            {
                var args = new object[] { null! };
                bool found = (bool)_projKeyTryGet!.Invoke(key, args);
                proj = (Projectile?)args[0];
                return found && proj != null;
            }
            catch { return false; }
        }

        private static Projectile? ProjectileNewSetup(object key)
        {
            try { return (Projectile?)_projNewSetup!.Invoke(null, new object[] { key }); }
            catch { return null; }
        }

        private static void ProjectileFinalize(Projectile proj)
        {
            try { _projFinalize!.Invoke(proj, null); }
            catch { }
        }

        /// <summary>
        /// 入站 27（旧客户端创建弹幕）语义级重放。
        /// 旧 27 = identity(2) pos(8) vel(8) owner(1) type(2) flags(1) [flags2 if flags[2]] ai...
        /// 复刻 1.4.5.7 服务器 case 27：ProjectileKey.TryGet / NewProjectileSetup + 设字段 + FinalizeProjectile + TrySendData(27) 广播。
        /// key.Index = 旧客户端 identity → 广播的新格式 key 经出站翻译回 identity → 旧客户端能匹配本地弹幕。
        /// </summary>
        private static void HandleInboundProjectileNew(MessageBuffer self, int start)
        {
            try
            {
                InitProjectileReflection();
                if (_projKeyCtor == null || _projNewSetup == null)
                    return;

                byte[] buf = self.readBuffer;
                if (buf == null) return;
                int o = start + 1;
                if (o + 1 >= buf.Length) return;
                short identity = (short)(buf[o] | (buf[o + 1] << 8)); o += 2;
                if (o + 16 > buf.Length) return;
                float posX = BitConverter.ToSingle(buf, o); float posY = BitConverter.ToSingle(buf, o + 4); o += 8;
                float velX = BitConverter.ToSingle(buf, o); float velY = BitConverter.ToSingle(buf, o + 4); o += 8;
                if (o + 3 > buf.Length) return;
                byte owner = buf[o]; o += 1;
                short type = (short)(buf[o] | (buf[o + 1] << 8)); o += 2;
                if (o >= buf.Length) return;
                byte flags = buf[o]; o += 1;
                byte flags2 = ((flags & 4) != 0) ? buf[o++] : (byte)0;

                float ai0 = 0f, ai1 = 0f, ai2 = 0f;
                ushort banner = 0;
                short dmg = 0;
                float kb = 0f;
                short origDmg = 0;
                if ((flags & 1) != 0 && o + 4 <= buf.Length) { ai0 = BitConverter.ToSingle(buf, o); o += 4; }
                if ((flags & 2) != 0 && o + 4 <= buf.Length) { ai1 = BitConverter.ToSingle(buf, o); o += 4; }
                if ((flags & 8) != 0 && o + 2 <= buf.Length) { banner = (ushort)(buf[o] | (buf[o + 1] << 8)); o += 2; }
                if ((flags & 16) != 0 && o + 2 <= buf.Length) { dmg = (short)(buf[o] | (buf[o + 1] << 8)); o += 2; }
                if ((flags & 32) != 0 && o + 4 <= buf.Length) { kb = BitConverter.ToSingle(buf, o); o += 4; }
                if ((flags & 64) != 0 && o + 2 <= buf.Length) { origDmg = (short)(buf[o] | (buf[o + 1] << 8)); o += 2; }
                if ((flags2 & 1) != 0 && o + 4 <= buf.Length) { ai2 = BitConverter.ToSingle(buf, o); }

                // 安全校验（复刻服务器 case 27 拒绝逻辑）：敌对弹拒绝 / 只能创建自己的弹幕
                if (type < 0 || type >= Main.projHostile.Length) return;
                if (Main.projHostile[type]) return;
                if (owner != self.whoAmI) return;

                // 构造 key：Spawner=owner, Index=identity（旧客户端 identity 匹配）, Generation=0
                object key = CreateProjectileKey(owner, identity, 0);

                bool isNew = false;
                Projectile? proj;
                if (!ProjectileTryGet(key, out proj) || proj == null)
                {
                    isNew = true;
                    proj = ProjectileNewSetup(key);
                    if (proj == null) return;
                    proj.SetDefaults(type);
                }
                else if (proj.type != type)
                {
                    proj.SetDefaults(type);
                }

                proj.owner = owner;
                proj.position = new Vector2(posX, posY);
                proj.velocity = new Vector2(velX, velY);
                proj.type = type;
                proj.damage = dmg;
                proj.bannerIdToRespondTo = banner;
                proj.originalDamage = origDmg;
                proj.knockBack = kb;
                proj.ai[0] = ai0;
                proj.ai[1] = ai1;
                proj.ai[2] = ai2;

                if (isNew)
                    ProjectileFinalize(proj);

                // 广播给其他客户端（服务器发新格式 27，出站翻译回旧格式给旧客户端）
                NetMessage.TrySendData(27, -1, self.whoAmI, null, proj.whoAmI);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleDebug($"[Compat1456] 入站弹幕创建异常: {ex.Message}");
            }
        }

        /// <summary>入站 29（旧客户端灭弹）语义级重放：旧 identity+owner → key → TryGet → active=false</summary>
        private static void HandleInboundProjectileKill(MessageBuffer self, int start)
        {
            try
            {
                InitProjectileReflection();
                if (_projKeyCtor == null || _projKeyTryGet == null)
                    return;

                byte[] buf = self.readBuffer;
                if (buf == null) return;
                int o = start + 1;
                if (o + 3 > buf.Length) return;
                short identity = (short)(buf[o] | (buf[o + 1] << 8)); o += 2;
                byte owner = buf[o];

                object key = CreateProjectileKey(owner, identity, 0);
                if (ProjectileTryGet(key, out var proj) && proj != null)
                {
                    proj.active = false;
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleDebug($"[Compat1456] 入站弹幕灭除异常: {ex.Message}");
            }
        }

        // ════════════════════════════════════════════════
        //  NPC 翻译：SyncNPC(23) —— gen 清零 + position 还原
        // ════════════════════════════════════════════════

        /// <summary>
        /// 出站 SyncNPC(23) 翻译（1.4.5.7 服务器 → 1.4.5.6 客户端）。
        /// 反编译实证（OTAPI.dll 1.4.5.6 vs 1.4.5.7 的 MessageBuffer case 23 完全对比）：
        ///   • 槽位字段：新 byte npc + byte gen（2B）与 旧 short npc（2B）同宽
        ///     → gen 字节清零后旧客户端读 short 低字节=npc（npc<256 时）
        ///   • position：1.4.5.7 服务器发的是【同步点】= npc.position + npc.Size * SyncAnchor[type]
        ///     （NetMessage.SendData case 23 实证；1.4.5.7 客户端解析时减回；旧客户端直接当左上角）
        ///     → 需减回 Size*SyncAnchor，否则 NPC 整体偏移/画到屏幕外 → "看不到 NPC"
        ///   • 其余字段（target/flags1/flags2/ai/type/变长 life/releaseOwner）新旧完全一致，无需处理
        /// </summary>
        private static void TranslateSyncNPC(byte[] data, int remoteClient, OrigSendPacket orig)
        {
            // 最小长度：type(1)+npc(1)+gen(1)+pos(8)+vel(8)+target(2)+flags1(1)+flags2(1)+type(2)
            if (data.Length < 3 + 1 + 1 + 8 + 8 + 2 + 1 + 1 + 2)
            {
                orig(data, remoteClient);
                return;
            }

            // ⚠️ 必须复制数组再翻译！广播时 SendData 循环复用同一 buffer[num].writeBuffer 发给所有客户端
            //   （1.4.5.7 反编译实证：SendData default 分支 for num22 → SendPacket(writeBuffer, num22)）。
            //   原地改写会把污染传给排在兼容客户端后面的原生 1.4.5.7 客户端：
            //     - gen 被清 0 → 原生客户端槽位/校验状态异常
            //     - position 被减一次 Size*SyncAnchor → 原生客户端解析时再减一次 → 双重偏移
            //   → 同一只怪不同玩家看到不同位置（"只有部分玩家能看得见的怪物"根因）。
            byte[] nd = new byte[data.Length];
            Array.Copy(data, nd, data.Length);

            // 1) gen 清零：nd[3]=npc(低), nd[4]=gen(高) → 旧 short npc = npc
            nd[4] = 0;

            // 2) position 还原（同步点 → 左上角）
            try
            {
                // 布局：nd[3]=npc nd[4]=gen nd[5..12]=pos nd[13..20]=vel
                //       nd[21..22]=target nd[23]=flags1 nd[24]=flags2 nd[25..]=ai
                byte flags1 = nd[23];
                int aiOffset = 25;
                for (int i = 0; i < NPC.maxAI; i++)
                {
                    if ((flags1 & (1 << (i + 2))) != 0)
                        aiOffset += 4;
                }

                if (aiOffset + 2 <= nd.Length)
                {
                    int netType = (short)(nd[aiOffset] | (nd[aiOffset + 1] << 8));
                    Vector2 anchor = GetSyncAnchor(netType);
                    if (anchor != Vector2.Zero)
                    {
                        Vector2 size = GetNpcSize(netType);
                        Vector2 syncPos = new Vector2(
                            BitConverter.ToSingle(nd, 5),
                            BitConverter.ToSingle(nd, 9));
                        Vector2 oldPos = syncPos - size * anchor;
                        BitConverter.GetBytes(oldPos.X).CopyTo(nd, 5);
                        BitConverter.GetBytes(oldPos.Y).CopyTo(nd, 9);
                        TShock.Log.ConsoleDebug($"[Compat1456] 发给 #{remoteClient} 的 SyncNPC(23) 已还原 position（type={netType}, anchor={anchor}）");
                    }
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleDebug($"[Compat1456] SyncNPC(23) position 还原异常（gen 已清零，原样发送）: {ex.Message}");
            }

            orig(nd, remoteClient);
        }

        private static Vector2[] _syncAnchor;
        private static bool _syncAnchorLoaded;
        private static NPC[] _npcByNetId;
        private static bool _npcByNetIdLoaded;

        /// <summary>反射读取 NPCID.Sets.SyncAnchor（1.4.5.7 新增，编译期引用可能没有；运行时存在）</summary>
        private static Vector2 GetSyncAnchor(int type)
        {
            if (!_syncAnchorLoaded)
            {
                _syncAnchorLoaded = true;
                try
                {
                    var f = typeof(NPCID.Sets).GetField("SyncAnchor", BindingFlags.Public | BindingFlags.Static);
                    _syncAnchor = f?.GetValue(null) as Vector2[];
                }
                catch { }
            }
            if (_syncAnchor != null && type >= 0 && type < _syncAnchor.Length)
                return _syncAnchor[type];
            return Vector2.Zero;
        }

        /// <summary>反射读取 ContentSamples.NpcsByNetId 获取该 netID 的默认 NPC 尺寸（用于 Size*SyncAnchor）。
        /// ⚠️ ContentSamples 命名空间跨版本变化：1.4.5.6=Terraria.ContentSamples，1.4.5.7=Terraria.ID.ContentSamples，
        ///    必须用字符串反射+程序集扫描，避免编译期 typeof 绑定到错误命名空间导致运行时 TypeLoadException。</summary>
        private static Vector2 GetNpcSize(int netType)
        {
            if (!_npcByNetIdLoaded)
            {
                _npcByNetIdLoaded = true;
                try
                {
                    var t = FindType("Terraria.ID.ContentSamples") ?? FindType("Terraria.ContentSamples");
                    var f = t?.GetField("NpcsByNetId", BindingFlags.Public | BindingFlags.Static);
                    _npcByNetId = f?.GetValue(null) as NPC[];
                }
                catch { }
            }
            if (_npcByNetId != null && netType >= 0 && netType < _npcByNetId.Length && _npcByNetId[netType] != null)
                return _npcByNetId[netType].Size;
            return Vector2.Zero;
        }

        /// <summary>跨程序集按全名找类型（避免 ContentSamples 等命名空间跨版本变化的编译期绑定问题）</summary>
        private static Type? FindType(string fullName)
        {
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var t = asm.GetType(fullName, throwOnError: false);
                    if (t != null)
                        return t;
                }
            }
            catch { }
            return null;
        }

        private static PropertyInfo? _npcGenerationProp;
        private static FieldInfo? _npcGenerationField;

        /// <summary>反射读取 NPC.generation（1.4.5.7 新增；编译期 TShock 6.1.0 的 NPC 没有该成员，必须反射）</summary>
        private static byte GetNpcGeneration(NPC npc)
        {
            if (_npcGenerationProp == null && _npcGenerationField == null)
            {
                try { _npcGenerationProp = typeof(NPC).GetProperty("generation", BindingFlags.Public | BindingFlags.Instance); }
                catch { }
                try { _npcGenerationField = typeof(NPC).GetField("generation", BindingFlags.Public | BindingFlags.Instance); }
                catch { }
            }
            try
            {
                if (_npcGenerationProp != null)
                    return (byte)_npcGenerationProp.GetValue(npc);
                if (_npcGenerationField != null)
                    return (byte)_npcGenerationField.GetValue(npc);
            }
            catch { }
            return 0;
        }

        // ════════════════════════════════════════════════
        //  生命周期
        // ════════════════════════════════════════════════

        private void OnServerLeave(LeaveEventArgs args)
        {
            lock (SyncLock)
            {
                if (_compatClients.Remove(args.Who))
                {
                    TShock.Log.ConsoleInfo($"[Compat1456] 兼容客户端 #{args.Who} 已离开，清理登记");
                }
            }
        }

        protected override void Dispose(bool Disposing)
        {
            if (Disposing)
            {
                try { _getDataHook?.Dispose(); }
                catch { }
                try { _sendToClientHook?.Dispose(); }
                catch { }
                try { _sendPacketHook?.Dispose(); }
                catch { }
                _getDataHook = null;
                _sendToClientHook = null;
                _sendPacketHook = null;

                try { ServerApi.Hooks.ServerLeave.Deregister(this, OnServerLeave); }
                catch { }

                lock (SyncLock)
                {
                    _compatClients.Clear();
                }
                _initialized = false;
                TShock.Log.ConsoleInfo("[Compat1456] 已卸载");
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

        /// <summary>
        /// 动态获取服务器期望协议版本串（"Terraria" + Main.curRelease）。
        /// Main.curRelease 是 public const int，编译期引用会被内联成 325；改用反射运行时读取，
        /// 使 1.4.5.8（乃至后续版本）修改协议号时本插件自动跟随，无需改代码重编译。
        /// </summary>
        private static string GetServerVersion()
        {
            if (_serverVersion != null)
                return _serverVersion;

            try
            {
                var f = typeof(Main).GetField("curRelease", BindingFlags.Public | BindingFlags.Static);
                if (f != null && f.GetValue(null) is int cur)
                {
                    _serverCurRelease = cur;
                    _serverVersion = "Terraria" + cur;
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[Compat1456] 反射读取 Main.curRelease 失败: {ex.Message}");
            }

            _serverVersion ??= FallbackServerVersion;
            return _serverVersion;
        }

        /// <summary>改写版本串为服务器期望值（1.4.5.x 全系协议号 3 位、版本串等长 11 字符，长度前缀不动）</summary>
        private static void RewriteVersion(byte[] buf, int start)
        {
            string serverVersion = GetServerVersion();

            // 防御：若未来协议号位数变化导致版本串长度不一致，同步调整长度前缀并告警（Hello 通常为缓冲首包）
            if (start + 1 < buf.Length && serverVersion.Length != buf[start + 1])
            {
                TShock.Log.ConsoleWarn($"[Compat1456] 版本串长度 {buf[start + 1]} → {serverVersion.Length}（非等长改写，需人工核对流对齐）");
                buf[start + 1] = (byte)serverVersion.Length;
            }

            int content = start + 2;
            for (int i = 0; i < serverVersion.Length && content + i < buf.Length; i++)
                buf[content + i] = (byte)serverVersion[i];
        }
    }
}
