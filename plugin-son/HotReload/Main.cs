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
            ShowDashboard(args.Player);
            return;
        }

        var sub = args.Parameters[0].ToLowerInvariant();

        switch (sub)
        {
            case "status": case "st":
                ShowDashboard(args.Player); break;

            case "load": case "ld":
                HandleLoad(args); break;

            case "unload": case "ul":
                HandleUnload(args); break;

            case "info": case "i":
                HandleInfo(args); break;

            case "reload-all": case "ra":
                HandleReloadAll(args); break;

            case "help": case "h":
                ShowHelp(args.Player); break;

            default:
                args.Player.SendErrorMessage($"未知子命令: {args.Parameters[0]}，使用 /hr help 查看帮助");
                break;
        }
    }

    // ══════════════════════════════════════════════════════════
    //  仪表盘 — /hr  或  /hr status
    //  合并了 scan 的详细信息，带颜色标注
    // ══════════════════════════════════════════════════════════

    private static void ShowDashboard(TSPlayer player)
    {
        var plugins = Core.ListPlugins();
        var changes = Core.ScanDirectory();

        var loaded = plugins.Count(p => p.IsLoaded);
        var newFromDisk = changes.Where(c => !c.IsLoaded).ToList();
        var overwritten = new List<(PluginRecord P, string Hash)>();

        player.SendSuccessMessage($"=== HotReload 仪表盘 (累计热重载 {Core.GetHotReloadCount()} 次) ===");
        player.SendInfoMessage($"已加载 {loaded} / 共 {plugins.Count} 个插件");
        player.SendInfoMessage("");

        // ── 已加载区 ──
        var loadedPlugins = plugins.Where(p => p.IsLoaded).ToList();
        if (loadedPlugins.Count > 0)
        {
            player.SendSuccessMessage("已加载：");
            foreach (var p in loadedPlugins)
            {
                var protect = p.IsProtected ? " [保护]" : "";
                OverwriteStatus(p, out var isOverwritten, out var curHash);

                if (isOverwritten)
                {
                    player.SendErrorMessage($"  [{p.Id}] {p.DisplayName}  v{p.Version}  by {p.Author}{protect}");
                    player.SendErrorMessage($"        磁盘文件已变更，使用 /hr load {p.Id} 重载");
                    overwritten.Add((p, curHash));
                }
                else
                {
                    player.SendInfoMessage($"  [{p.Id}] {p.DisplayName}  v{p.Version}  by {p.Author}{protect}");
                }
            }
            player.SendInfoMessage("");
        }

        // ── 已卸载区（之前加载过，后被卸载） ──
        var unloadedPlugins = plugins.Where(p => !p.IsLoaded).ToList();
        if (unloadedPlugins.Count > 0)
        {
            player.SendInfoMessage("已卸载：");
            foreach (var p in unloadedPlugins)
            {
                player.SendInfoMessage($"  [{p.Id}] {p.DisplayName}  v{p.Version}  by {p.Author}  可重新加载");
            }
            player.SendInfoMessage("");
        }

        // ── 磁盘新增区 ──
        if (newFromDisk.Count > 0)
        {
            player.SendSuccessMessage("磁盘新增：");
            foreach (var p in newFromDisk)
            {
                var hashShort = !string.IsNullOrEmpty(p.FileHash) ? p.FileHash[..12] + "..." : "";
                player.SendSuccessMessage($"  {p.AssemblyName}.dll  哈希: {hashShort}  从未加载");
                player.SendSuccessMessage($"        使用 /hr load {p.AssemblyName} 加载");
            }
            player.SendInfoMessage("");
        }

        // ── 无变更提示 ──
        if (overwritten.Count == 0 && unloadedPlugins.Count == 0 && newFromDisk.Count == 0)
        {
            player.SendSuccessMessage("磁盘状态与运行状态一致，无变更");
            player.SendInfoMessage("");
        }

        // ── 快捷操作提示 ──
        if (overwritten.Count > 0 || unloadedPlugins.Count > 0 || newFromDisk.Count > 0)
        {
            player.SendInfoMessage("操作提示：/hr load <序号|名称>   /hr unload <序号>   /hr reload-all 一键应用");
        }

        // ── 内存警告 ──
        if (Core.GetHotReloadCount() >= 15)
        {
            player.SendErrorMessage("注意：已累计热重载 {0} 次，旧程序集内存无法释放，建议在下次维护窗口重启服务器", Core.GetHotReloadCount());
        }
    }

    // ══════════════════════════════════════════════════════════
    //  load — 加载或重载
    //  已加载/已卸载的插件可用序号或名称
    //  磁盘新增的插件只能用名称
    // ══════════════════════════════════════════════════════════

    private static void HandleLoad(CommandArgs args)
    {
        if (args.Parameters.Count < 2)
        {
            args.Player.SendErrorMessage("用法: /hr load <序号|插件名>");
            return;
        }

        var target = string.Join(" ", args.Parameters.Skip(1));
        var isNumeric = int.TryParse(target, out _);

        // 数字 → 只能操作 _records 中已有的（已加载/已卸载）
        if (isNumeric)
        {
            var record = Core.ResolveRecord(target);
            if (record == null)
            {
                args.Player.SendErrorMessage($"未找到序号为 {target} 的插件。磁盘新增的插件请使用名称：/hr load <插件名>");
                return;
            }
        }

        var (ok, msg) = Core.UpdatePlugin(target);
        if (ok) args.Player.SendSuccessMessage(msg);
        else args.Player.SendErrorMessage(msg);
    }

    // ══════════════════════════════════════════════════════════
    //  unload — 卸载
    //  已加载的可用序号或名称
    //  已卸载或磁盘新增的不可卸载
    // ══════════════════════════════════════════════════════════

    private static void HandleUnload(CommandArgs args)
    {
        if (args.Parameters.Count < 2)
        {
            args.Player.SendErrorMessage("用法: /hr unload <序号|插件名>");
            return;
        }

        var target = string.Join(" ", args.Parameters.Skip(1));

        // 先确认目标是否存在且已加载
        var record = Core.ResolveRecord(target);
        if (record == null)
        {
            args.Player.SendErrorMessage($"未找到插件: {target}");
            return;
        }
        if (!record.IsLoaded)
        {
            args.Player.SendErrorMessage($"{record.DisplayName} 当前未加载，无需卸载");
            return;
        }

        var (ok, msg) = Core.RemovePlugin(target);
        if (ok) args.Player.SendSuccessMessage(msg);
        else args.Player.SendErrorMessage(msg);
    }

    // ══════════════════════════════════════════════════════════
    //  info — 查看单插件详情
    // ══════════════════════════════════════════════════════════

    private static void HandleInfo(CommandArgs args)
    {
        if (args.Parameters.Count < 2)
        {
            args.Player.SendErrorMessage("用法: /hr info <序号|插件名>");
            return;
        }

        var target = string.Join(" ", args.Parameters.Skip(1));
        var record = Core.ResolveRecord(target);

        if (record == null)
        {
            args.Player.SendErrorMessage($"未找到匹配的插件: {target}");
            return;
        }

        args.Player.SendSuccessMessage($"=== 插件详情 ===");
        args.Player.SendInfoMessage($"名称:     {record.DisplayName}");
        args.Player.SendInfoMessage($"程序集:   {record.AssemblyName}");
        args.Player.SendInfoMessage($"DLL文件:  {record.DllFileName}");
        args.Player.SendInfoMessage($"完整路径: {record.FullPath}");
        args.Player.SendInfoMessage($"版本:     v{record.Version}");
        args.Player.SendInfoMessage($"作者:     {record.Author}");
        args.Player.SendInfoMessage($"状态:     {(record.IsLoaded ? "已加载" : "未加载")}");
        args.Player.SendInfoMessage($"保护:     {(record.IsProtected ? "是" : "否")}");

        if (record.IsLoaded)
        {
            var hashLoaded = !string.IsNullOrEmpty(record.FileHash) ? record.FileHash[..16] + "..." : "无";
            args.Player.SendInfoMessage($"加载时哈希: {hashLoaded}");

            OverwriteStatus(record, out var overwritten, out var currentHash);
            var diskHashShort = !string.IsNullOrEmpty(currentHash) ? currentHash[..16] + "..." : "无";
            if (overwritten)
            {
                args.Player.SendErrorMessage($"磁盘当前哈希: {diskHashShort}  (与加载时不一致，dll 已被覆盖)");
            }
            else
            {
                args.Player.SendSuccessMessage($"磁盘当前哈希: {diskHashShort}  (与加载时一致)");
            }
        }
    }

    // ══════════════════════════════════════════════════════════
    //  reload-all — 一键重载所有变更
    // ══════════════════════════════════════════════════════════

    private static void HandleReloadAll(CommandArgs args)
    {
        var changes = Core.ScanDirectory();
        var unloadedPlugins = Core.ListPlugins().Where(p => !p.IsLoaded).ToList();

        var targets = new List<(string Name, string AssemblyName)>();

        foreach (var c in changes)
        {
            if (c.IsProtected) continue;
            targets.Add((c.DisplayName, c.AssemblyName));
        }
        foreach (var p in unloadedPlugins)
        {
            if (p.IsProtected) continue;
            if (targets.Any(t => string.Equals(t.AssemblyName, p.AssemblyName, StringComparison.OrdinalIgnoreCase)))
                continue;
            targets.Add((p.DisplayName, p.AssemblyName));
        }

        if (targets.Count == 0)
        {
            args.Player.SendSuccessMessage("没有待处理的变更，所有插件状态正常");
            return;
        }

        args.Player.SendInfoMessage($"开始批量重载 {targets.Count} 个变更插件...");

        var success = 0;
        var fail = 0;

        for (int i = 0; i < targets.Count; i++)
        {
            var (name, asm) = targets[i];
            var (ok, msg) = Core.UpdatePlugin(asm);

            if (ok)
            {
                args.Player.SendSuccessMessage($"[{i + 1}/{targets.Count}] {name} ... 成功");
                success++;
            }
            else
            {
                args.Player.SendErrorMessage($"[{i + 1}/{targets.Count}] {name} ... 失败: {msg}");
                fail++;
            }
        }

        args.Player.SendSuccessMessage($"完成：成功 {success} 个，失败 {fail} 个");
        args.Player.SendInfoMessage($"累计热重载: {Core.GetHotReloadCount()} 次");
    }

    // ══════════════════════════════════════════════════════════
    //  help
    // ══════════════════════════════════════════════════════════

    private static void ShowHelp(TSPlayer player)
    {
        player.SendSuccessMessage($"=== HotReload — 运行时插件热重载 (累计 {Core.GetHotReloadCount()} 次) ===");
        player.SendInfoMessage("  /hr                    仪表盘：全量状态 + 变更详情");
        player.SendInfoMessage("  /hr load  <序号|名称>   加载或重载（可简写 /hr ld）");
        player.SendInfoMessage("  /hr unload <序号>       卸载已加载的插件（可简写 /hr ul）");
        player.SendInfoMessage("  /hr info  <序号|名称>   查看插件详细信息（可简写 /hr i）");
        player.SendInfoMessage("  /hr reload-all          一键重载所有变更（可简写 /hr ra）");
        player.SendInfoMessage("  /hr help                显示此帮助（可简写 /hr h）");
        player.SendErrorMessage("注意：受保护的系统插件不可操作；热重载不会释放旧程序集内存");
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

        if (string.IsNullOrEmpty(currentHash))
        {
            overwritten = true;
            return true;
        }

        if (string.IsNullOrEmpty(p.FileHash))
        {
            overwritten = true;
            return true;
        }

        overwritten = !string.Equals(p.FileHash, currentHash, StringComparison.OrdinalIgnoreCase);
        return overwritten;
    }
}
