using Terraria;
using TerrariaApi.Server;
using TShockAPI;

namespace HotReload;

[ApiVersion(2, 1)]
public class HotReload : TerrariaPlugin
{
    public override string Name => "HotReload";
    public override string Description => "运行时热重载插件管理";
    public override string Author => "lmx12330";
    public override Version Version => new(1, 0, 0, 0);

    public HotReload(Main game) : base(game) { }

    public override void Initialize()
    {
        Commands.ChatCommands.Add(new Command("hotreload.admin", HandleCommand, "hr"));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Commands.ChatCommands.RemoveAll(cmd => cmd.CommandDelegate == HandleCommand);
        }
        base.Dispose(disposing);
    }

    // ══════════════════════════════════════════════════════════
    //  命令入口
    // ══════════════════════════════════════════════════════════

    private static void HandleCommand(CommandArgs args)
    {
        // 延迟初始化
        Core.EnsureInitialized();

        if (args.Parameters.Count == 0)
        {
            ShowHelp(args.Player);
            return;
        }

        var sub = args.Parameters[0].ToLowerInvariant();

        switch (sub)
        {
            case "l":
            case "list":
            case "-l":
            case "--list":
                HandleList(args);
                break;

            case "r":
            case "remove":
            case "-r":
            case "--remove":
                HandleRemove(args);
                break;

            case "s":
            case "scan":
            case "-s":
            case "--scan":
                HandleScan(args);
                break;

            case "u":
            case "update":
            case "-u":
            case "--update":
                HandleUpdate(args);
                break;

            case "h":
            case "help":
            case "-h":
            case "--help":
                ShowHelp(args.Player);
                break;

            default:
                args.Player.SendErrorMessage($"未知子命令: {args.Parameters[0]}");
                ShowHelp(args.Player);
                break;
        }
    }

    // ══════════════════════════════════════════════════════════
    //  list
    // ══════════════════════════════════════════════════════════

    private static void HandleList(CommandArgs args)
    {
        var plugins = Core.ListPlugins();

        if (plugins.Count == 0)
        {
            args.Player.SendInfoMessage("当前没有跟踪任何插件");
            return;
        }

        args.Player.SendInfoMessage(
            $"已加载 {plugins.Count(p => p.IsLoaded)} / 共 {plugins.Count} 个插件  |  累计热重载 {Core.GetHotReloadCount()} 次");

        foreach (var p in plugins)
        {
            var protect = p.IsProtected ? " [受保护]" : "";
            var status = p.IsLoaded ? "已加载" : "未加载";
            var hashShort = !string.IsNullOrEmpty(p.FileHash) ? p.FileHash[..12] + "..." : "无文件";
            args.Player.SendInfoMessage(
                $"  [{p.Id,2}] {status}  {p.DisplayName,-20}  v{p.Version}  by {p.Author}{protect}");
            args.Player.SendInfoMessage(
                $"        文件: {p.DllFileName,-20}  哈希: {hashShort}");
        }
    }

    // ══════════════════════════════════════════════════════════
    //  remove
    // ══════════════════════════════════════════════════════════

    private static void HandleRemove(CommandArgs args)
    {
        if (args.Parameters.Count < 2)
        {
            args.Player.SendErrorMessage("用法: /hr remove <序号|插件名>");
            return;
        }

        var target = string.Join(" ", args.Parameters.Skip(1));
        var (ok, msg) = Core.RemovePlugin(target);

        if (ok)
            args.Player.SendSuccessMessage(msg);
        else
            args.Player.SendErrorMessage(msg);
    }

    // ══════════════════════════════════════════════════════════
    //  scan
    // ══════════════════════════════════════════════════════════

    private static void HandleScan(CommandArgs args)
    {
        args.Player.SendInfoMessage("正在扫描插件目录...");

        var changes = Core.ScanDirectory();

        if (changes.Count == 0)
        {
            args.Player.SendSuccessMessage("未发现新插件或文件变更");
            return;
        }

        var newPlugins = changes.Where(c => !c.IsLoaded).ToList();
        var modified = changes.Where(c => c.IsLoaded).ToList();

        args.Player.SendInfoMessage($"发现 {changes.Count} 项变更：");

        if (newPlugins.Count > 0)
        {
            args.Player.SendSuccessMessage("  【新插件】");
            for (int i = 0; i < newPlugins.Count; i++)
            {
                var p = newPlugins[i];
                var hashShort = !string.IsNullOrEmpty(p.FileHash) ? p.FileHash[..12] + "..." : "";
                args.Player.SendInfoMessage(
                    $"    [{i + 1}] {p.AssemblyName} -> {p.DllFileName}  哈希: {hashShort}");
            }
        }

        if (modified.Count > 0)
        {
            args.Player.SendInfoMessage("  【已变更】");
            for (int i = 0; i < modified.Count; i++)
            {
                var p = modified[i];
                var hashShort = !string.IsNullOrEmpty(p.FileHash) ? p.FileHash[..12] + "..." : "";
                args.Player.SendInfoMessage(
                    $"    [{newPlugins.Count + i + 1}] {p.DisplayName,-20} 文件哈希已变  新哈希: {hashShort}");
            }
        }

        args.Player.SendInfoMessage("  使用 /hr update <序号> 加载或更新上述插件");
    }

    // ══════════════════════════════════════════════════════════
    //  update
    // ══════════════════════════════════════════════════════════

    private static void HandleUpdate(CommandArgs args)
    {
        if (args.Parameters.Count < 2)
        {
            args.Player.SendErrorMessage("用法: /hr update <序号|插件名>");
            return;
        }

        var target = string.Join(" ", args.Parameters.Skip(1));
        var (ok, msg) = Core.UpdatePlugin(target);

        if (ok)
            args.Player.SendSuccessMessage(msg);
        else
            args.Player.SendErrorMessage(msg);
    }

    // ══════════════════════════════════════════════════════════
    //  help
    // ══════════════════════════════════════════════════════════

    private static void ShowHelp(TSPlayer player)
    {
        player.SendSuccessMessage($"=== HotReload — 运行时插件热重载 (累计 {Core.GetHotReloadCount()} 次) ===");
        player.SendInfoMessage("  /hr l(ist)        列出所有插件的状态和文件哈希");
        player.SendInfoMessage("  /hr r(emove) <id|名称>  卸载指定插件（从 ServerApi 移除）");
        player.SendInfoMessage("  /hr s(can)        扫描目录，检测新增/变更的 dll");
        player.SendInfoMessage("  /hr u(pdate) <id|名称>  加载或更新指定插件");
        player.SendInfoMessage("  /hr h(elp)        显示此帮助");
        player.SendErrorMessage("注意：受保护的系统插件（TShockAPI、自身等）不可操作");
        player.SendErrorMessage("注意：热重载不会释放旧程序集内存，累计多次后建议重启服务器");
        player.SendInfoMessage("  /hr scan 给出变更序号后可用 /hr update <序号> 加载");
    }
}
