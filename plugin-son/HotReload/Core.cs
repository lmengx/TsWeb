using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;

namespace HotReload;

public class PluginRecord
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = "";
    public string AssemblyName { get; set; } = "";
    public string DllFileName { get; set; } = "";
    public string FullPath { get; set; } = "";
    public Version Version { get; set; } = new();
    public string Author { get; set; } = "";
    public string FileHash { get; set; } = "";
    public bool IsLoaded { get; set; }
    public bool IsProtected { get; set; }

    public override string ToString()
    {
        var protectMark = IsProtected ? " [P]" : "";
        var loadedMark = IsLoaded ? "" : " [未加载]";
        return $"[{Id,2}] {DisplayName,-20} v{Version} by {Author}{loadedMark}{protectMark}";
    }
}

public static class Core
{
    // ── 白名单 ──────────────────────────────────────────────
    private static readonly HashSet<string> ProtectedAssemblyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "TShockAPI",
        "Terraria",
        "TerrariaServerAPI",
        "OTAPI",
        "MonoMod",
        "MonoMod.RuntimeDetour",
        "Newtonsoft.Json",
        "MySql.Data",
        "HotReload",  // 自身禁止操作
    };

    // ── 内部状态 ────────────────────────────────────────────
    private static readonly List<PluginRecord> _records = new();
    private static bool _initialized;
    private static readonly object _initLock = new();
    private static int _hotReloadCount;

    // ── 操作锁（防止并发卸载/加载导致状态损坏）───────────────
    private static volatile bool _operationInProgress;
    private static readonly object _opLock = new();

    // ── 常量 ────────────────────────────────────────────────
    private const int MaxHotReloadWarning = 15;
    private static string ServerPluginsPath =>
        Path.Combine(AppContext.BaseDirectory, ServerApi.PluginsPath ?? "ServerPlugins");

    // ══════════════════════════════════════════════════════════
    //  初始化
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// 懒初始化：在第一次执行 /hr 命令时调用。
    /// 扫描当前已加载的所有插件，记录文件名和哈希作为基线。
    /// </summary>
    public static void EnsureInitialized()
    {
        if (_initialized) return;
        lock (_initLock)
        {
            if (_initialized) return;

            TShock.Log.ConsoleInfo("[HotReload] 首次初始化，扫描当前已加载插件...");
            ScanLoadedPlugins();
            _initialized = true;
            TShock.Log.ConsoleInfo($"[HotReload] 初始化完成，已跟踪 {_records.Count} 个插件");
        }
    }

    // ══════════════════════════════════════════════════════════
    //  扫描（加载态）
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// 扫描当前 ServerApi 中已加载的所有插件，建立初始记录。
    /// </summary>
    private static void ScanLoadedPlugins()
    {
        var asmMap = GetLoadedAssembliesMap();          // fileName → Assembly
        var asmToFile = asmMap.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var id = 1;

        foreach (var container in ServerApi.Plugins)
        {
            var plugin = container.Plugin;
            var assembly = plugin.GetType().Assembly;
            var asmName = assembly.GetName().Name;
            if (string.IsNullOrEmpty(asmName) || !seen.Add(asmName))
                continue;

            asmToFile.TryGetValue(assembly, out var fileName);
            var dllFile = fileName != null ? fileName + ".dll" : "";
            var fullPath = !string.IsNullOrEmpty(dllFile)
                ? Path.Combine(ServerPluginsPath, dllFile)
                : "";

            var rec = new PluginRecord
            {
                Id = id++,
                DisplayName = plugin.Name,
                AssemblyName = asmName,
                DllFileName = dllFile,
                FullPath = fullPath,
                Version = plugin.Version,
                Author = plugin.Author,
                FileHash = ComputeFileHash(fullPath),
                IsLoaded = true,
                IsProtected = ProtectedAssemblyNames.Contains(asmName),
            };
            _records.Add(rec);
        }
    }

    // ══════════════════════════════════════════════════════════
    //  扫描（目录态）
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// 扫描 ServerPlugins 目录，找出：
    ///   1. 全新的 dll（从未跟踪过）
    ///   2. 哈希变动的 dll（文件被替换）
    /// 返回变更列表。
    /// </summary>
    public static List<PluginRecord> ScanDirectory()
    {
        EnsureInitialized();

        var changes = new List<PluginRecord>();
        var knownAsmNames = _records
            .Where(r => !string.IsNullOrEmpty(r.DllFileName))
            .Select(r => Path.GetFileNameWithoutExtension(r.DllFileName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(ServerPluginsPath))
            return changes;

        foreach (var dll in Directory.GetFiles(ServerPluginsPath, "*.dll"))
        {
            var asmName = Path.GetFileNameWithoutExtension(dll);

            // 跳过已知且哈希未变的
            if (knownAsmNames.Contains(asmName))
            {
                var existing = _records.FirstOrDefault(r =>
                    string.Equals(r.AssemblyName, asmName, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    var newHash = ComputeFileHash(dll);
                    if (string.Equals(existing.FileHash, newHash, StringComparison.OrdinalIgnoreCase))
                        continue; // 没变

                    // 哈希变了 → 标记变更
                    changes.Add(new PluginRecord
                    {
                        DisplayName = existing.DisplayName,
                        AssemblyName = asmName,
                        DllFileName = Path.GetFileName(dll),
                        FullPath = dll,
                        FileHash = newHash,
                        IsLoaded = existing.IsLoaded,
                        IsProtected = existing.IsProtected,
                    });
                }
                continue;
            }

            // 全新的 dll
            changes.Add(new PluginRecord
            {
                AssemblyName = asmName,
                DllFileName = Path.GetFileName(dll),
                FullPath = dll,
                FileHash = ComputeFileHash(dll),
                IsLoaded = false,
                IsProtected = ProtectedAssemblyNames.Contains(asmName),
            });
        }

        return changes;
    }

    // ══════════════════════════════════════════════════════════
    //  列出
    // ══════════════════════════════════════════════════════════

    public static IReadOnlyList<PluginRecord> ListPlugins()
    {
        EnsureInitialized();
        return _records.AsReadOnly();
    }

    // ══════════════════════════════════════════════════════════
    //  移除（卸载）
    // ══════════════════════════════════════════════════════════

    public static (bool success, string message) RemovePlugin(string target)
    {
        EnsureInitialized();

        var record = ResolveRecord(target);
        if (record == null)
            return (false, $"未找到匹配的插件: {target}");

        if (record.IsProtected)
            return (false, $"禁止操作: {record.DisplayName} 是受保护的系统插件，无法卸载");

        if (!record.IsLoaded)
            return (false, $"{record.DisplayName} 当前未加载，无需卸载");

        lock (_opLock)
        {
            if (_operationInProgress)
                return (false, "已有操作正在进行中，请等待完成");
            _operationInProgress = true;
        }

        try
        {
            return RemovePluginInternal(record);
        }
        finally
        {
            lock (_opLock) _operationInProgress = false;
        }
    }

    private static (bool, string) RemovePluginInternal(PluginRecord record)
    {
        var loadedAssemblies = GetLoadedAssembliesMap();
        var plugins = GetPluginsList();
        if (plugins == null || loadedAssemblies == null)
            return (false, "无法访问 ServerApi 内部状态，卸载失败");

        // 在 plugins 列表中找到匹配的 PluginContainer
        PluginContainer? targetContainer = null;
        foreach (var c in plugins)
        {
            if (string.Equals(
                    c.Plugin.GetType().Assembly.GetName().Name,
                    record.AssemblyName,
                    StringComparison.OrdinalIgnoreCase))
            {
                targetContainer = c;
                break;
            }
        }

        if (targetContainer == null)
            return (false, $"在 ServerApi 中未找到 {record.DisplayName} 的运行实例");

        try
        {
            // 1. Dispose 插件
            targetContainer.Plugin.Dispose();
            TShock.Log.ConsoleInfo($"[HotReload] {record.DisplayName} v{record.Version} Dispose 完成");

            // 2. 从 plugins 列表移除
            plugins.Remove(targetContainer);

            // 3. 从 loadedAssemblies 移除
            loadedAssemblies.Remove(record.AssemblyName);

            // 4. 更新记录
            record.IsLoaded = false;

            _hotReloadCount++;
            CheckMemoryWarning();

            return (true, $"{record.DisplayName} v{record.Version} 已卸载");
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[HotReload] 卸载 {record.DisplayName} 失败: {ex}");
            return (false, $"卸载失败: {ex.Message}");
        }
    }

    // ══════════════════════════════════════════════════════════
    //  加载/更新
    // ══════════════════════════════════════════════════════════

    public static (bool success, string message) UpdatePlugin(string target)
    {
        EnsureInitialized();

        var changes = ScanDirectory();
        PluginRecord? targetChange = null;

        // 尝试按序号或名称匹配变更列表
        if (int.TryParse(target, out var index))
        {
            if (index >= 1 && index <= changes.Count)
                targetChange = changes[index - 1];
        }
        else
        {
            targetChange = changes.FirstOrDefault(c =>
                string.Equals(c.AssemblyName, target, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.DllFileName, target, StringComparison.OrdinalIgnoreCase));
        }

        // 如果变更列表里没有，尝试从已加载列表中找（重新加载）
        if (targetChange == null)
        {
            var record = ResolveRecord(target);
            if (record == null)
                return (false, $"未找到匹配的插件: {target}");

            if (record.IsProtected)
                return (false, $"禁止操作: {record.DisplayName} 是受保护的系统插件，无法热重载");

            // 已加载 → 先卸载再加载
            if (record.IsLoaded)
            {
                var (ok, msg) = RemovePluginInternal(record);
                if (!ok) return (false, msg);
            }

            // 从磁盘加载
            return LoadPluginFromDisk(record);
        }

        // 来自变更列表
        if (targetChange.IsProtected)
            return (false, $"禁止操作: {targetChange.AssemblyName} 是受保护的系统插件，无法热重载");

        // 如果已加载，先卸载
        var existingRec = _records.FirstOrDefault(r =>
            string.Equals(r.AssemblyName, targetChange.AssemblyName, StringComparison.OrdinalIgnoreCase) && r.IsLoaded);
        if (existingRec != null)
        {
            var (ok, msg) = RemovePluginInternal(existingRec);
            if (!ok) return (false, msg);
        }

        // 备份旧 dll
        BackupDll(targetChange.FullPath);

        // 从磁盘加载
        return LoadPluginFromDisk(targetChange);
    }

    private static (bool, string) LoadPluginFromDisk(PluginRecord record)
    {
        if (string.IsNullOrEmpty(record.FullPath) || !File.Exists(record.FullPath))
            return (false, $"文件不存在: {record.FullPath}");

        var loadedAssemblies = GetLoadedAssembliesMap();
        var plugins = GetPluginsList();
        var game = GetGameInstance();
        if (plugins == null || loadedAssemblies == null || game == null)
            return (false, "无法访问 ServerApi 内部状态，加载失败");

        try
        {
            // 读取 dll 和 pdb
            var asmBytes = File.ReadAllBytes(record.FullPath);
            var pdbPath = Path.ChangeExtension(record.FullPath, ".pdb");
            byte[]? pdbBytes = File.Exists(pdbPath) ? File.ReadAllBytes(pdbPath) : null;

            // Assembly.Load 从字节数组加载（不锁文件）
            var assembly = Assembly.Load(asmBytes, pdbBytes);

            // 注册到 loadedAssemblies
            loadedAssemblies[record.AssemblyName] = assembly;

            // 扫描插件类型
            var loaded = false;
            foreach (var type in assembly.GetExportedTypes())
            {
                if (!type.IsSubclassOf(typeof(TerrariaPlugin)) || !type.IsPublic || type.IsAbstract)
                    continue;
                if (type.GetCustomAttribute<ApiVersionAttribute>() == null)
                    continue;

                // API 版本校验
                if (!ServerApi.IgnoreVersion)
                {
                    var apiVer = type.GetCustomAttribute<ApiVersionAttribute>()!.ApiVersion;
                    if (apiVer.Major != ServerApi.ApiVersion.Major || apiVer.Minor != ServerApi.ApiVersion.Minor)
                    {
                        TShock.Log.ConsoleError($"[HotReload] {type.FullName} API 版本不匹配，跳过");
                        continue;
                    }
                }

                var pluginInstance = (TerrariaPlugin?)Activator.CreateInstance(type, game);
                if (pluginInstance == null) continue;

                var container = new PluginContainer(pluginInstance);
                plugins.Add(container);
                container.Initialize();

                // 更新记录
                record.DisplayName = pluginInstance.Name;
                record.Version = pluginInstance.Version;
                record.Author = pluginInstance.Author;
                record.IsLoaded = true;
                record.FileHash = ComputeFileHash(record.FullPath);

                // 如果记录不存在，加入列表
                if (!_records.Any(r => string.Equals(r.AssemblyName, record.AssemblyName, StringComparison.OrdinalIgnoreCase)))
                {
                    record.Id = _records.Count > 0 ? _records.Max(r => r.Id) + 1 : 1;
                    _records.Add(record);
                }

                loaded = true;
                _hotReloadCount++;
                CheckMemoryWarning();

                TShock.Log.ConsoleInfo($"[HotReload] 已加载 {pluginInstance.Name} v{pluginInstance.Version}");

                return (true, $"{pluginInstance.Name} v{pluginInstance.Version} 已加载{(pdbBytes != null ? "（含调试符号）" : "")}");
            }

            if (!loaded)
                return (false, $"在 {record.DllFileName} 中未找到有效的 TerrariaPlugin 子类");

            return (false, "未知错误");
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[HotReload] 加载 {record.DllFileName} 失败: {ex}");
            return (false, $"加载失败: {ex.Message}");
        }
    }

    // ══════════════════════════════════════════════════════════
    //  工具方法
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// 按序号或准确名称解析记录。
    /// </summary>
    private static PluginRecord? ResolveRecord(string target)
    {
        // 序号
        if (int.TryParse(target, out var index))
        {
            return _records.FirstOrDefault(r => r.Id == index);
        }

        // 准确名称（优先 DisplayName，其次 AssemblyName，其次 DllFileName）
        return _records.FirstOrDefault(r =>
            string.Equals(r.DisplayName, target, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r.AssemblyName, target, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r.DllFileName, target, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 计算文件的 SHA256 哈希（十六进制小写）。
    /// </summary>
    private static string ComputeFileHash(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return "";

        try
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha256.ComputeHash(stream);
            return Convert.ToHexString(hash).ToLower();
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// 备份旧 dll 到 .backup 目录。
    /// </summary>
    private static void BackupDll(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return;

        try
        {
            var backupDir = Path.Combine(ServerPluginsPath, ".hotreload-backup");
            Directory.CreateDirectory(backupDir);
            var fileName = Path.GetFileName(filePath);
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var backupPath = Path.Combine(backupDir, $"{Path.GetFileNameWithoutExtension(fileName)}.{timestamp}.dll");
            File.Copy(filePath, backupPath, overwrite: true);
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleWarn($"[HotReload] 备份 {filePath} 失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 检查热重载次数，超出警告阈值时输出日志。
    /// </summary>
    private static void CheckMemoryWarning()
    {
        if (_hotReloadCount >= MaxHotReloadWarning && _hotReloadCount % 5 == 0)
        {
            var msg = $"[HotReload] 警告：已累计热重载 {_hotReloadCount} 次，旧程序集内存无法释放，建议在下次维护窗口重启服务器";
            TShock.Log.ConsoleWarn(msg);
        }
    }

    /// <summary>
    /// 获取当前的累计热重载次数。
    /// </summary>
    public static int GetHotReloadCount() => _hotReloadCount;

    // ══════════════════════════════════════════════════════════
    //  反射获取 ServerApi 内部字段
    // ══════════════════════════════════════════════════════════

    private static readonly BindingFlags PrivateFlags =
        BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    private static Dictionary<string, Assembly>? GetLoadedAssembliesMap()
    {
        return (Dictionary<string, Assembly>?)
            typeof(ServerApi).GetField("loadedAssemblies", PrivateFlags)?.GetValue(null);
    }

    private static List<PluginContainer>? GetPluginsList()
    {
        return (List<PluginContainer>?)
            typeof(ServerApi).GetField("plugins", PrivateFlags)?.GetValue(null);
    }

    private static Main? GetGameInstance()
    {
        return (Main?)
            typeof(ServerApi).GetField("game", PrivateFlags)?.GetValue(null);
    }
}
