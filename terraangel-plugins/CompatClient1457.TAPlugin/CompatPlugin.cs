using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using CompatCore1457;
using TerraAngel;
using TerraAngel.Plugin;

namespace CompatClient1457;

/// <summary>
/// CompatClient1457 —— TerraAngel 客户端侧跨版本翻译插件（装配壳）。
///
/// v2.0 稳定架构（解决 v1/v1.1/v1.2 三连"#reload 卡死"）：
///   根因：Harmony/MonoMod 在 .NET 5+ 的 patch 均为 native detour（HarmonyLib.Memory.
///         WriteJump → DetourHelper.Native 反编译实证）。TerraAngel #reload =
///         UnloadPlugins()（同步 Unload + 插件 ALC 卸载 + 重建重载），卸载 native detour
///         （写回原机器码）时若网络线程正在执行 GetData/SendPacketToServer（高频热路径）
///         → 进程级卡死。
///   方案：
///     1. 全部翻译逻辑 + Harmony prefix 委托放 CompatCore.dll，由本插件
///        Assembly.Load(bytes) 加载到【默认 ALC】——不随插件 ALC（isCollectible）卸载；
///     2. 补丁【一次安装、永不卸载】：native 跳板从不写回，网络线程执行永不冲突；
///     3. #reload 时本插件 Unload 只调用 CompatCore.Deactivate()（停止翻译、包放行），
///        新实例重新 Load 后 InstallPatches() 只切换 Translator 实例（纯托管）——彻底安全；
///     4. 网络线程回调（prefix）不访问任何 UI，异常只计数，主线程 Update 汇总。
///
/// 部署（Plugins/ 目录 3 个文件）：
///   CompatClient1457.TAPlugin.dll / CompatCore.dll / 0Harmony.dll
/// </summary>
public sealed class CompatPlugin : Plugin
{
    public override string Name => "CompatClient1457";

    private const string LogTag = "CompatClient1457";

    private static bool _loaded;
    private static bool _hookPending;
    private static bool _assembliesReady;

    public CompatPlugin(string path) : base(path) { }

    // ════════════════════════════════════════════════
    //  生命周期
    // ════════════════════════════════════════════════

    public override void Load()
    {
        if (_loaded)
            return;

        // 先确保 0Harmony / CompatCore 加载到默认 ALC（本方法体不引用 CompatCore 类型，
        // 避免在装配完成前触发其类型初始化 / 0Harmony 解析）。
        EnsureDefaultAssemblies();

        // 补丁延迟到首帧 Update 安装（避开 TerraAngel 启动早期 JIT 不稳定窗口）
        _hookPending = true;
        _loaded = true;
        ClientLoader.Console.WriteLine($"[{LogTag}] 已加载：协议翻译核心已装配到默认 ALC，首帧安装补丁");
    }

    public override void Update()
    {
        if (_hookPending)
        {
            _hookPending = false;
            try
            {
                CompatCore.InstallPatches();
                ClientLoader.Console.WriteLine($"[{LogTag}] 协议翻译补丁已安装（#reload 安全）");
            }
            catch (Exception ex)
            {
                ClientLoader.Console.WriteError($"[{LogTag}] 补丁安装失败: {ex.Message}");
            }
        }

        Translator? t = CompatCore.Current;
        if (t != null)
        {
            int e = Interlocked.Exchange(ref t.Errors, 0);
            if (e > 0)
                ClientLoader.Console.WriteLine($"[{LogTag}] 翻译异常计数（已原样放行）：{e}");
        }
    }

    public override void Unload()
    {
        // ⚠️ 绝不 UnpatchAll：卸载 native detour 与网络线程执行并发 = 进程级卡死。
        //    补丁与 prefix 全在默认 ALC（CompatCore），永不卸载；这里只停止翻译。
        try { CompatCore.Deactivate(); }
        catch { }
        _loaded = false;
        _hookPending = false;
        ClientLoader.Console.WriteLine($"[{LogTag}] 已卸载（翻译已停，补丁保留于默认 ALC）");
    }

    // ════════════════════════════════════════════════
    //  装配：0Harmony / CompatCore 加载到默认 ALC
    // ════════════════════════════════════════════════

    private void EnsureDefaultAssemblies()
    {
        if (_assembliesReady)
            return;

        string dir = Path.GetDirectoryName(PluginPath) ?? ClientLoader.PluginsPath;
        if (dir == null)
        {
            ClientLoader.Console.WriteError($"[{LogTag}] 无法定位插件目录（PluginPath={PluginPath}），翻译不可用");
            return;
        }

        // 顺序必须：0Harmony 先于 CompatCore（CompatCore 静态初始化解析 HarmonyLib）
        LoadToDefaultIfMissing("0Harmony", Path.Combine(dir, "0Harmony.dll"));
        LoadToDefaultIfMissing("CompatCore", Path.Combine(dir, "CompatCore.dll"));
        _assembliesReady = true;
    }

    private static void LoadToDefaultIfMissing(string name, string path)
    {
        if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == name))
            return;
        if (File.Exists(path))
            Assembly.Load(File.ReadAllBytes(path));
    }
}
