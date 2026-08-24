using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Terraria;

namespace CompatCore1457;

/// <summary>
/// CompatCore —— 协议翻译核心（默认 ALC 常驻）。
///
/// 让 1.4.5.6（Terraria319）客户端直连纯净 1.4.5.7（Terraria325）服务器。
/// Harmony prefix 的委托与全部翻译逻辑都在本程序集（默认 ALC，永不卸载），
/// patch 一次安装、永不卸载，规避 TerraAngel #reload 卸载 native detour 的进程级卡死。
///
/// 翻译表（与服务器端 Compat1456 方向相反，且无其 readBuffer 无法增长的硬伤）：
///   出站（SendPacketToServer，ref 替换数组可增长）：
///     1 Hello       版本串 319→325（等长；TerraAngel CustomClientHello 已配 325 时幂等）
///     27 SyncProjectile identity+owner → ProjectileKey(owner,ident,0)（+1B）
///     29 KillProjectile → key+deathPos(NaN,NaN)（+9B，NaN 触发服务器静默销毁）
///     28 DamageNPC    short npc → byte npc + byte gen（gen 来自入站 23 收集的映射）
///     82 NetModule    moduleId 旧→新（ID≥5→+1）
///   入站（MessageBuffer.GetData，只缩短/等长改写 body 前缀 + 缩 length）：
///     17 Tile body 9B→8B；21 SyncItem 截断 24B+flags 清0；22 ItemOwner 头3+尾8；
///     23 SyncNPC gen 清零+存映射；27 key→ident+owner（前移1B）；28 gen 清零；
///     29 key+deathPos→ident+owner（缩9B clamp）；82 位移/过滤（ID==5→类型改93跳过）
///     162 自动忽略（1.4.5.6 MessageID.Count==162）
///
/// ProjectileKey 位布局（反编译实证）：Spawner=bits&0xFF | Index=(bits>>8)&0x3FF | Gen=(bits>>18)&0x3FFF
/// </summary>
public static class CompatCore
{
    /// <summary>当前生效的翻译器（默认 ALC 静态，跨插件重载存活；prefix 均读此实例）</summary>
    private static Translator? _active;

    /// <summary>Harmony 实例（只创建一次，patch 永不卸载）</summary>
    private static Harmony? _harmony;

    /// <summary>是否已安装过补丁（#reload 后不重复 Patch）</summary>
    private static bool _patched;

    /// <summary>激活新翻译器（新会话）。旧翻译器被替换，旧 prefix 自动放行，无双重翻译。</summary>
    public static void Activate(Translator translator)
    {
        Volatile.Write(ref _active, translator);
    }

    /// <summary>停止翻译（后续包原样放行）。不 Unpatch，native 跳板保持不动。</summary>
    public static void Deactivate()
    {
        Volatile.Write(ref _active, null);
    }

    /// <summary>当前翻译器（供插件主线程读取错误计数）</summary>
    public static Translator? Current => Volatile.Read(ref _active);

    /// <summary>
    /// 安装 Harmony 补丁（首次调用）。线程安全；重复调用只换翻译器不重挂补丁。
    /// </summary>
    public static void InstallPatches()
    {
        // 每次安装都激活新翻译器（幂等：#reload 后重复调用也安全）
        Activate(new Translator());

        if (_patched)
            return;
        _patched = true;

        var harmony = new Harmony("com.tsweb.compatclient1457." + Guid.NewGuid().ToString("N"));

        var sendMethod = typeof(NetMessage).GetMethod("SendPacketToServer",
            BindingFlags.NonPublic | BindingFlags.Static,
            null, new[] { typeof(byte[]) }, null);
        if (sendMethod == null)
        {
            throw new InvalidOperationException("未找到 NetMessage.SendPacketToServer，出站翻译不可用");
        }
        harmony.Patch(sendMethod, prefix: new HarmonyMethod(typeof(CompatCore), nameof(SendPrefix)));

        var recvMethod = typeof(MessageBuffer).GetMethod("GetData",
            BindingFlags.Public | BindingFlags.Instance,
            null, new[] { typeof(int), typeof(int), typeof(int).MakeByRefType() }, null);
        if (recvMethod == null)
        {
            throw new InvalidOperationException("未找到 MessageBuffer.GetData，入站翻译不可用");
        }
        harmony.Patch(recvMethod, prefix: new HarmonyMethod(typeof(CompatCore), nameof(GetDataPrefix)));

        _harmony = harmony; // 防 GC
    }

    // ════════════════════════════════════════════════
    //  Harmony prefix 入口（网络线程回调：绝不访问 UI/Console，只计数）
    // ════════════════════════════════════════════════

    private static bool SendPrefix(ref byte[] data)
    {
        Translator? t = Volatile.Read(ref _active);
        if (t == null || data == null || data.Length < 3)
            return true;

        try
        {
            return t.Send(ref data);
        }
        catch (Exception)
        {
            Interlocked.Increment(ref t.Errors);
            return true;
        }
    }

    private static bool GetDataPrefix(MessageBuffer __instance, int start, ref int length)
    {
        Translator? t = Volatile.Read(ref _active);
        if (t == null || __instance == null)
            return true;

        byte[] buf = __instance.readBuffer;
        if (buf == null || start < 0 || start >= buf.Length)
            return true;

        try
        {
            return t.Receive(buf, start, ref length);
        }
        catch (Exception)
        {
            Interlocked.Increment(ref t.Errors);
            return true;
        }
    }
}

/// <summary>
/// 翻译器：一个会话的翻译状态。新会话（插件重载）创建新实例并替换 CompatCore._active，
/// 旧实例无引用后自然回收。所有状态（npc gen 映射、错误计数）都在实例内，天然隔离。
/// </summary>
public sealed class Translator
{
    /// <summary>网络线程翻译错误计数（Interlocked，主线程读取后清零）</summary>
    public int Errors;

    /// <summary>旧客户端（1.4.5.6）版本串</summary>
    public const string ClientVersion = "Terraria319";

    /// <summary>目标服务器（1.4.5.7）版本串</summary>
    public const string ServerVersion = "Terraria325";

    /// <summary>旧客户端认识的 NetModule ID 上界（Liquid0..Bestiary4；1.4.5.7 插入 CreativeUnlocks=5 → 其后 +1）</summary>
    private const int MaxCompatNetModuleId = 4;

    /// <summary>NPC 槽 → generation 映射（入站 23 收集，出站 28 回填；网络线程访问，lock 保护）</summary>
    private readonly Dictionary<int, byte> _npcGen = new();
    private readonly object _lock = new();

    // ════════════════════════════════════════════════
    //  出站（客户端 → 服务器）：旧格式 → 新格式
    // ════════════════════════════════════════════════

    /// <summary>返回 true 继续原方法；通过 ref 替换 data 可自由增长。</summary>
    public bool Send(ref byte[] data)
    {
        byte type = data[2];
        byte[]? nd = null;

        switch (type)
        {
            case 1: TranslateHelloOut(data); break;                 // 等长改写，原数组
            case 27: nd = TranslateProjectileNewOut(data); break;   // +1B
            case 29: nd = TranslateProjectileKillOut(data); break;  // +9B
            case 28: TranslateDamageNpcOut(data); break;            // 等长改写，原数组
            case 82: TranslateNetModuleOut(data); break;            // 等长改写，原数组
        }

        if (nd != null)
            data = nd;
        return true;
    }

    /// <summary>出站 1 Hello：版本串 "Terraria319" → "Terraria325"（等长 11 字符，7bit 长度前缀不动）。</summary>
    private void TranslateHelloOut(byte[] data)
    {
        int content = 4;
        if (data.Length >= content + ClientVersion.Length && data[3] == ClientVersion.Length)
        {
            bool match = true;
            for (int i = 0; i < ClientVersion.Length; i++)
            {
                if (data[content + i] != (byte)ClientVersion[i])
                {
                    match = false;
                    break;
                }
            }
            if (match)
            {
                for (int i = 0; i < ServerVersion.Length; i++)
                    data[content + i] = (byte)ServerVersion[i];
            }
        }
    }

    /// <summary>出站 27：identity(2)+pos+vel+owner(1)+type+flags+rest → key(4)+pos+vel+type+flags+rest（+1B）。</summary>
    private byte[]? TranslateProjectileNewOut(byte[] data)
    {
        if (data.Length < 3 + 2 + 8 + 8 + 1 + 2 + 1)
            return null;

        int ident = data[3] | (data[4] << 8);
        byte owner = data[21];
        int key = PackKey(owner, ident, 0);

        int newLen = data.Length + 1;
        if (newLen > ushort.MaxValue)
            return null;

        byte[] nd = new byte[newLen];
        nd[0] = (byte)newLen;
        nd[1] = (byte)(newLen >> 8);
        nd[2] = 27;
        nd[3] = (byte)key;
        nd[4] = (byte)(key >> 8);
        nd[5] = (byte)(key >> 16);
        nd[6] = (byte)(key >> 24);
        Array.Copy(data, 5, nd, 7, 8);        // pos
        Array.Copy(data, 13, nd, 15, 8);      // vel
        Array.Copy(data, 22, nd, 23, data.Length - 22); // type+flags+rest
        return nd;
    }

    /// <summary>出站 29：identity(2)+owner(1) → key(4)+deathPos(8=NaN,NaN)（+9B；NaN 触发服务器静默销毁）。</summary>
    private byte[]? TranslateProjectileKillOut(byte[] data)
    {
        if (data.Length < 3 + 3)
            return null;

        int ident = data[3] | (data[4] << 8);
        byte owner = data[5];
        int key = PackKey(owner, ident, 0);

        byte[] nd = new byte[15];
        nd[0] = 15;
        nd[1] = 0;
        nd[2] = 29;
        nd[3] = (byte)key;
        nd[4] = (byte)(key >> 8);
        nd[5] = (byte)(key >> 16);
        nd[6] = (byte)(key >> 24);
        nd[7] = 0x00; nd[8] = 0x00; nd[9] = 0xC0; nd[10] = 0x7F;  // NaN
        nd[11] = 0x00; nd[12] = 0x00; nd[13] = 0xC0; nd[14] = 0x7F; // NaN
        return nd;
    }

    /// <summary>出站 28：short npc(2) → byte npc(1)+byte gen(1)（等长 10B）。gen 查映射，服务器硬校验。</summary>
    private void TranslateDamageNpcOut(byte[] data)
    {
        if (data.Length < 3 + 10)
            return;

        int npc = data[3] | (data[4] << 8);
        if (npc > 255)
            return;

        data[3] = (byte)npc;
        byte gen;
        lock (_lock)
        {
            _npcGen.TryGetValue(npc, out gen);
        }
        data[4] = gen;
    }

    /// <summary>出站 82：moduleId 旧→新（ID≥5 → +1，等长 2B）。</summary>
    private void TranslateNetModuleOut(byte[] data)
    {
        if (data.Length < 3 + 2)
            return;

        ushort mid = (ushort)(data[3] | (data[4] << 8));
        if (mid > MaxCompatNetModuleId)
        {
            ushort newMid = (ushort)(mid + 1);
            data[3] = (byte)newMid;
            data[4] = (byte)(newMid >> 8);
        }
    }

    // ════════════════════════════════════════════════
    //  入站（服务器 → 客户端）：新格式 → 旧格式
    //  只缩短/等长改写 body 前缀 + 缩 length（网络循环按包头推进，不动长度头）
    // ════════════════════════════════════════════════

    /// <summary>返回 true 继续原方法；ref length 已调整为翻译后长度。</summary>
    public bool Receive(byte[] buf, int start, ref int length)
    {
        byte type = buf[start];
        switch (type)
        {
            case 17: length = TranslateTileIn(buf, start, length); break;
            case 21: length = TranslateItemDropIn(buf, start, length); break;
            case 22: length = TranslateItemOwnerIn(buf, start, length); break;
            case 23: TranslateSyncNpcIn(buf, start, length); break;
            case 27: length = TranslateProjectileNewIn(buf, start, length); break;
            case 28: TranslateDamageNpcIn(buf, start, length); break;
            case 29: length = TranslateProjectileKillIn(buf, start, length); break;
            case 82: TranslateNetModuleIn(buf, start, length); break;
        }
        return true;
    }

    /// <summary>入站 17：新 body 9B → 旧 8B（缩 length）。</summary>
    private int TranslateTileIn(byte[] buf, int start, int length)
    {
        if (length >= 10)
            return length - 1;
        return length;
    }

    /// <summary>入站 21：截断 24B body + flags(body[21]) 置 0。</summary>
    private int TranslateItemDropIn(byte[] buf, int start, int length)
    {
        if (length >= 1 + 21 + 1)
            buf[start + 22] = 0;
        if (length > 1 + 24)
            return 1 + 24;
        return length;
    }

    /// <summary>入站 22：头 3 字节(index+owner) + 尾 8 字节(position) → 11B body。</summary>
    private int TranslateItemOwnerIn(byte[] buf, int start, int length)
    {
        if (length < 1 + 11)
            return length;

        int posStart = start + 1 + (length - 1 - 8);
        if (posStart < start + 1 + 3)
            return length;

        Array.Copy(buf, posStart, buf, start + 1 + 3, 8);
        return 1 + 11;
    }

    /// <summary>入站 23：gen(body[1]) 清零（旧 short npc 高位）+ 保存 gen→映射。
    /// ⚠️ position 还原（SyncAnchor）v1 跳过：1.4.5.6 客户端无 SyncAnchor 表。</summary>
    private void TranslateSyncNpcIn(byte[] buf, int start, int length)
    {
        if (length < 1 + 2)
            return;

        int npc = buf[start + 1];
        byte gen = buf[start + 2];
        lock (_lock)
        {
            _npcGen[npc] = gen;
        }
        buf[start + 2] = 0;
    }

    /// <summary>入站 27：key(4)→identity(2)+owner(1)，body 前移 1B（缩短）。</summary>
    private int TranslateProjectileNewIn(byte[] buf, int start, int length)
    {
        int bodyLen = length - 1;
        if (bodyLen < 4 + 8 + 8 + 2 + 1)
            return length;

        int key = buf[start + 1] | (buf[start + 2] << 8) | (buf[start + 3] << 16) | (buf[start + 4] << 24);
        byte spawner = (byte)(key & 0xFF);
        int index = (key >> 8) & 0x3FF;

        int moveLen = bodyLen - 4;
        byte[] tmp = new byte[moveLen];
        Array.Copy(buf, start + 1 + 4, tmp, 0, moveLen);
        Array.Copy(tmp, 0, buf, start + 1 + 2, moveLen);

        buf[start + 1] = (byte)index;
        buf[start + 2] = (byte)(index >> 8);
        if (1 + 18 < bodyLen)
            buf[start + 1 + 18] = spawner;

        return length - 1;
    }

    /// <summary>入站 28：gen(body[1]) 清零（等长）。</summary>
    private void TranslateDamageNpcIn(byte[] buf, int start, int length)
    {
        if (length >= 1 + 2)
            buf[start + 2] = 0;
    }

    /// <summary>入站 29：key(4)+deathPos(8) → identity(2)+owner(1)（缩 9B，clamp）。</summary>
    private int TranslateProjectileKillIn(byte[] buf, int start, int length)
    {
        int bodyLen = length - 1;
        if (bodyLen < 4)
            return length;

        int key = buf[start + 1] | (buf[start + 2] << 8) | (buf[start + 3] << 16) | (buf[start + 4] << 24);
        byte spawner = (byte)(key & 0xFF);
        int index = (key >> 8) & 0x3FF;

        buf[start + 1] = (byte)index;
        buf[start + 2] = (byte)(index >> 8);
        buf[start + 3] = spawner;

        return Math.Max(1 + 3, length - 9);
    }

    /// <summary>入站 82：moduleId 新→旧（ID≥6 → -1）；ID==5 → 类型改 93（旧客户端空 case 整体跳过）。</summary>
    private void TranslateNetModuleIn(byte[] buf, int start, int length)
    {
        if (length < 1 + 2)
            return;

        ushort mid = (ushort)(buf[start + 1] | (buf[start + 2] << 8));
        if (mid == 5)
        {
            buf[start] = 93;
        }
        else if (mid > MaxCompatNetModuleId)
        {
            ushort oldMid = (ushort)(mid - 1);
            buf[start + 1] = (byte)oldMid;
            buf[start + 2] = (byte)(oldMid >> 8);
        }
    }

    // ════════════════════════════════════════════════
    //  工具
    // ════════════════════════════════════════════════

    /// <summary>ProjectileKey 打包（1.4.5.7 位布局反编译实证）。</summary>
    private static int PackKey(int spawner, int index, int generation)
    {
        return (spawner & 0xFF) | ((index & 0x3FF) << 8) | ((generation & 0x3FFF) << 18);
    }
}
