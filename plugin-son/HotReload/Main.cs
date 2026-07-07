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
        ServerApi.Hooks.GamePostInitialize.Register(this, OnWorldReady);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ServerApi.Hooks.GamePostInitialize.Deregister(this, OnWorldReady);
            Commands.ChatCommands.RemoveAll(cmd => cmd.CommandDelegate == HandleCommand);
        }
        base.Dispose(disposing);
    }

    private static void OnWorldReady(EventArgs args)
    {
        Core.EnsureInitialized();
        
    }

    // ══════════════════════════════════════════════════════════
    //  命令入口
    // ══════════════════════════════════════════════════════════

    private static void HandleCommand(CommandArgs args)
    {
        Core.EnsureInitialized();

        if (args.Parameters.Count == 0)
        {
            ShowHelp(args.Player);
            return;
        }

        var sub = args.Parameters[0].ToLowerInvariant();

        switch (sub)
        {
            case "l": case "list": case "-l": case "--list":
                HandleList(args); break;

            case "ll": case "listload":
                HandleListLoad(args); break;

            case "lu": case "listunload":
                HandleListUnload(args); break;

            case "r": case "remove": case "-r": case "--remove":
                HandleRemove(args); break;

            case "u": case "update": case "-u": case "--update":
                HandleUpdate(args); break;

            case "h": case "help": case "-h": case "--help":
                ShowHelp(args.Player); break;

            default:
                args.Player.SendErrorMessage($"未知子命令: {args.Parameters[0]}");
                ShowHelp(args.Player);
                break;
        }
    }

    // ══════════════════════════════════════════════════════════
    //  list — 全部插件
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
            $"全部插件 已加载 {plugins.Count(p => p.IsLoaded)} / 共 {plugins.Count}  |  累计热重载 {Core.GetHotReloadCount()} 次");

        foreach (var p in plugins)
        {
            OverwriteStatus(p, out var overwrite, out _);
            var protect = p.IsProtected ? " [保护]" : "";
            var status = p.IsLoaded ? "已加载" : "未加载";
            var marker = overwrite ? " [被覆盖]" : "";
            args.Player.SendInfoMessage(
                $"  [{p.Id,2}] {status}  {p.DisplayName,-20}  v{p.Version}  by {p.Author}{protect}{marker}");
        }
    }

    // ══════════════════════════════════════════════════════════
    //  listload — 仅已加载，标记被覆盖的
    // ══════════════════════════════════════════════════════════

    private static void HandleListLoad(CommandArgs args)
    {
        var loaded = Core.ListPlugins().Where(p => p.IsLoaded).ToList();
        if (loaded.Count == 0)
        {
            args.Player.SendInfoMessage("当前没有已加载的插件");
            return;
        }

        // 预先算好所有插件的覆盖状态（只哈希一次）
        var statuses = loaded.Select(p =>
        {
            var currentHash = Core.ComputeFileHash(p.FullPath);

            bool overwritten;
            string reason;

            if (string.IsNullOrEmpty(p.FullPath))
            {
                overwritten = true;
                reason = "无磁盘文件（可能为内置插件）";
            }
            else if (string.IsNullOrEmpty(currentHash))
            {
                overwritten = true;
                reason = "文件无法读取";
            }
            else if (string.IsNullOrEmpty(p.FileHash))
            {
                overwritten = true;
                reason = "未记录初始哈希";
            }
            else if (!string.Equals(p.FileHash, currentHash, StringComparison.OrdinalIgnoreCase))
            {
                overwritten = true;
                reason = "dll 已被覆盖";
            }
            else
            {
                overwritten = false;
                reason = "";
            }

            return (Plugin: p, Overwritten: overwritten, CurrentHash: currentHash, Reason: reason);
        }).ToList();

        var overwritten = statuses.Where(s => s.Overwritten).ToList();
        var normal = statuses.Where(s => !s.Overwritten).ToList();

        if (overwritten.Count > 0)
        {
            args.Player.SendErrorMessage($"发现 {overwritten.Count} 个异常：");
            foreach (var (p, _, hash, reason) in overwritten)
            {
                var hashShort = !string.IsNullOrEmpty(hash) ? hash[..12] + "..." : "无";
                args.Player.SendErrorMessage(
                    $"  [{p.Id,2}] {p.DisplayName,-20}  v{p.Version}  by {p.Author}{(p.IsProtected ? " [保护]" : "")}");
                args.Player.SendErrorMessage(
                    $"        [{reason}]  当前哈希: {hashShort}");
            }
        }

        if (normal.Count > 0)
        {
            if (overwritten.Count > 0)
                args.Player.SendSuccessMessage($"其余 {normal.Count} 个状态正常：");
            else
                args.Player.SendSuccessMessage($"全部 {normal.Count} 个插件状态正常，无异常：");

            foreach (var (p, _, _, _) in normal)
                args.Player.SendSuccessMessage(
                    $"  [{p.Id,2}] {p.DisplayName,-20}  v{p.Version}  by {p.Author}{(p.IsProtected ? " [保护]" : "")}");
        }
    }

    // ══════════════════════════════════════════════════════════
    //  listunload — 扫目录，磁盘减去已加载 = 未加载
    // ══════════════════════════════════════════════════════════

    private static void HandleListUnload(CommandArgs args)
    {
        args.Player.SendInfoMessage("正在扫描插件目录...");

        var unloaded = Core.GetUnloadedFromDisk();
        if (unloaded.Count == 0)
        {
            args.Player.SendSuccessMessage("所有插件正常运行，磁盘文件与运行状态一致");
            return;
        }

        args.Player.SendInfoMessage($"发现 {unloaded.Count} 项未加载：");

        foreach (var p in unloaded)
        {
            var existing = Core.ListPlugins().FirstOrDefault(r =>
                string.Equals(r.AssemblyName, p.AssemblyName, StringComparison.OrdinalIgnoreCase));

            string reason;
            if (existing != null && existing.IsLoaded)
                reason = "dll 已被覆盖，运行的是旧代码";
            else if (existing != null)
                reason = "已被主动卸载";
            else
                reason = "新增文件（从未加载）";

            var hashShort = !string.IsNullOrEmpty(p.FileHash) ? p.FileHash[..12] + "..." : "";
            var displayName = !string.IsNullOrEmpty(p.DisplayName) ? p.DisplayName : p.AssemblyName;
            args.Player.SendInfoMessage(
                $"  [{p.Id,2}] {displayName,-20}  v{p.Version}  by {p.Author}");
            args.Player.SendInfoMessage(
                $"        文件: {p.DllFileName,-20}  哈希: {hashShort}  [{reason}]");
        }

        args.Player.SendInfoMessage("  使用 /hr update <序号> 加载或重新加载");
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

        if (ok) args.Player.SendSuccessMessage(msg);
        else args.Player.SendErrorMessage(msg);
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

        if (ok) args.Player.SendSuccessMessage(msg);
        else args.Player.SendErrorMessage(msg);
    }

    // ══════════════════════════════════════════════════════════
    //  help
    // ══════════════════════════════════════════════════════════

    private static void ShowHelp(TSPlayer player)
    {
        player.SendSuccessMessage($"=== HotReload — 运行时插件热重载 (累计 {Core.GetHotReloadCount()} 次) ===");
        player.SendInfoMessage("  /hr l(ist)           列出全部插件（含未加载状态）");
        player.SendInfoMessage("  /hr ll (listload)    仅列出已加载的，标记 dll 被覆盖的");
        player.SendInfoMessage("  /hr lu (listunload)  扫目录，列出所有未加载项（新增/覆盖/已卸载）");
        player.SendInfoMessage("  /hr r(emove) <id|名称>  卸载指定插件");
        player.SendInfoMessage("  /hr u(pdate) <id|名称>  加载或更新指定插件");
        player.SendInfoMessage("  /hr h(elp)           显示此帮助");
        player.SendErrorMessage("注意：受保护的系统插件（TShockAPI、自身等）不可操作");
        player.SendErrorMessage("注意：热重载不会释放旧程序集内存，累计多次后建议重启服务器");
    }

    // ══════════════════════════════════════════════════════════
    //  工具
    // ══════════════════════════════════════════════════════════

    private static bool OverwriteStatus(PluginRecord p, out bool overwritten, out string currentHash)
    {
        if (!p.IsLoaded)
        {
            overwritten = false;
            currentHash = "";
            return false;
        }

        if (string.IsNullOrEmpty(p.FullPath))
        {
            currentHash = "";
            overwritten = false;
            return false;
        }

        currentHash = Core.ComputeFileHash(p.FullPath);

        // 文件无法读取
        if (string.IsNullOrEmpty(currentHash))
        {
            overwritten = true;  // 作为异常状态显示
            return true;
        }

        // 记录哈希为空（理论上不应该，防御性处理）
        if (string.IsNullOrEmpty(p.FileHash))
        {
            overwritten = true;
            return true;
        }

        overwritten = !string.Equals(p.FileHash, currentHash, StringComparison.OrdinalIgnoreCase);
        return overwritten;
    }
}
