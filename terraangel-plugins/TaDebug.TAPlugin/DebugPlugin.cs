using TerraAngel;
using TerraAngel.Plugin;

namespace TaDebug.TAPlugin;

/// <summary>
/// TaDebug — TerraAngel 验证插件骨架。
/// 用途：验证 TerraAngel 插件加载链路是否正常，并作为后续
/// Bug 验证 / 数据包测试插件的开发模板。
/// </summary>
/// <remarks>
/// 契约要点（源码实证自 Plugin.cs / PluginLoader.cs）：
/// 1. 必须继承 TerraAngel.Plugin.Plugin
/// 2. 构造函数必须接收 string path 并传给 base（Activator.CreateInstance(type, path)）
/// 3. 必须实现 abstract string Name
/// 4. 插件默认禁用，需在客户端插件 UI 中勾选启用（或配置 pluginsToEnable）
/// 5. 控制台命令以 # 前缀触发（如 #hi world）
/// </remarks>
public sealed class DebugPlugin : Plugin
{
    public override string Name => "TaDebug";

    public DebugPlugin(string path) : base(path)
    {
    }

    /// <summary>加载时调用一次：注册控制台命令。</summary>
    public override void Load()
    {
        ClientLoader.Console.WriteLine($"[{Name}] 已加载，dll 路径: {PluginPath}");
        ClientLoader.Console.WriteLine($"[{Name}] 程序集: {PluginAssembly.FullName}");

        ClientLoader.Console.AddCommand("hi",
            x =>
            {
                ClientLoader.Console.WriteLine($"[{Name}] 收到命令，参数: [{string.Join(", ", x.Args)}]");
                if (x.Args.Count > 0)
                {
                    ClientLoader.Console.WriteLine($"[{Name}] 完整参数串: {x.FullArgs}");
                    ClientLoader.Console.WriteLine($"[{Name}] 第2个及之后: {x.FullArgsFrom(1)}");
                }
            },
            "TaDebug 测试命令：输入 #hi 任意参数");
    }

    /// <summary>每帧调用：在此写轮询/检测逻辑。</summary>
    public override void Update()
    {
        // TODO: Bug 验证逻辑（如定时检查、数据包观测）
    }

    /// <summary>卸载时调用一次：反注册资源。</summary>
    public override void Unload()
    {
        ClientLoader.Console.WriteLine($"[{Name}] 已卸载");
    }
}
