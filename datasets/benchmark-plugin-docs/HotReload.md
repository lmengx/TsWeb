# HotReload — 运行时插件热重载管理（标杆插件深度技术文档）

> **版本**: 1.0.0.0  
> **作者**: lmx12330  
> **框架**: .NET 9.0 · TShock 6.1.0+ · ApiVersion 2.1  
> **源码路径**: `plugin-son/HotReload/`  
> **输出 DLL**: `HotReload.dll`  
> **许可证**: 内部项目

---

## 目录

1. [插件概述](#1-插件概述)
2. [核心原理](#2-核心原理)
3. [完整命令参考](#3-完整命令参考)
4. [启动行为详解](#4-启动行为详解)
5. [受保护插件列表与原因](#5-受保护插件列表与原因)
6. [已知限制](#6-已知限制)
7. [下载与部署](#7-下载与部署)
8. [最佳实践](#8-最佳实践)
9. [源码架构分析](#9-源码架构分析)
10. [常见问题与排错](#10-常见问题与排错)

---

## 1. 插件概述

### 1.1 运行时热重载插件管理的价值

在 TShock 服务端环境中，插件的生命周期传统上由服务器启动时统一加载。每当插件代码变更后，开发者需要：

1. 重新编译插件
2. 停止服务器
3. 替换 `ServerPlugins/` 目录下的 DLL 文件
4. 重启服务器

这个过程在**插件开发调试**场景下极为低效——每次修改代码都意味着几十秒到数分钟的重启等待。对于生产环境，服务器重启意味着玩家掉线、进度丢失，这对于需要高可用性的服务器是不可接受的。

**HotReload 解决了这个核心痛点**：它允许在服务器**运行状态下**动态卸载旧版本插件并加载新版本，全程无需重启、无需关闭服务器、不影响在线玩家的游戏体验。

### 1.2 核心应用场景

| 场景 | 说明 |
|------|------|
| **插件开发调试** | 改代码 → 编译 → `/hr ld <名称>` 即时验证，开发效率提升 10 倍以上 |
| **紧急热修复** | 线上插件出现 Bug，快速编译修复版本 `reload-all` 一键替换 |
| **插件热替换** | 功能升级场景，在不中断服务前提下完成插件版本升级 |
| **异常插件移除** | 某插件行为异常导致服务器卡顿/崩溃，`/hr ul <序号>` 快速卸载 |

### 1.3 技术栈

```
运行时:     .NET 9.0
游戏框架:   Terraria (TerrariaServerAPI via TShock)
服务端框架: TShock 6.1.0+
API 版本:   2.1
依赖注入:   TShock 内部 PluginContainer 机制
哈希算法:   SHA256
```

---

## 2. 核心原理

### 2.1 卸载原理（反射 ServerApi 内部字段）

HotReload 的核心卸载机制依赖于**反射访问 TShock 内部私有字段**。TShock 的 `ServerApi` 类维护着两个关键内部状态：

```csharp
// ServerApi 内部字段（私有）
private static Dictionary<string, Assembly> loadedAssemblies;  // 已加载程序集
private static List<PluginContainer> plugins;                   // 插件容器列表
private static Main game;                                       // 游戏实例
```

HotReload 通过以下反射代码获取这些字段：

```csharp
private static readonly BindingFlags PrivateFlags =
    BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

private static Dictionary<string, Assembly>? GetLoadedAssembliesMap()
    => (Dictionary<string, Assembly>?)
        typeof(ServerApi).GetField("loadedAssemblies", PrivateFlags)?.GetValue(null);

private static List<PluginContainer>? GetPluginsList()
    => (List<PluginContainer>?)
        typeof(ServerApi).GetField("plugins", PrivateFlags)?.GetValue(null);
```

**卸载执行流程**（`RemovePluginInternal` 方法）：

```
┌──────────────────────────────────────────────┐
│               开始卸载                        │
├──────────────────────────────────────────────┤
│  1. 调用 plugin.Dispose()                    │
│     → 触发插件的清理逻辑                     │
│     → 反注册钩子、释放资源                   │
├──────────────────────────────────────────────┤
│  2. 从 ServerApi.Plugins 列表中移除          │
│     plugins.Remove(targetContainer)          │
│     → TShock 不再识别此插件                  │
├──────────────────────────────────────────────┤
│  3. 从 loadedAssemblies 字典中移除           │
│     loadedAssemblies.Remove(assemblyName)    │
│     → TShock 不再持有程序集引用              │
├──────────────────────────────────────────────┤
│  4. 更新 PluginRecord 状态                   │
│     record.IsLoaded = false                  │
│     _hotReloadCount++                        │
└──────────────────────────────────────────────┘
```

> **关键点**：`Dispose()` 的调用是卸载过程中最脆弱的一环。如果插件的 `Dispose()` 实现不完整（未反注册所有事件），会导致事件残留问题（详见 [6.2 事件残留问题](#62-事件残留问题)）。

### 2.2 加载原理（Assembly.Load 字节数组）

加载机制采用 `Assembly.Load(byte[])` 方式，而非 `Assembly.LoadFile` 或 `Assembly.LoadFrom`：

```csharp
// 读取 dll 字节
var asmBytes = File.ReadAllBytes(record.FullPath);

// 可选：读取 pdb 调试符号
var pdbBytes = File.Exists(pdbPath) ? File.ReadAllBytes(pdbPath) : null;

// 从字节数组加载（不锁文件）
var assembly = Assembly.Load(asmBytes, pdbBytes);
```

**为什么选择 `Assembly.Load(byte[])`？**

| 方案 | 文件锁 | 多次加载 | 适用场景 |
|------|--------|----------|----------|
| `Assembly.LoadFile` | 锁文件 | 每次新加载 | 文件路径加载 |
| `Assembly.LoadFrom` | 锁文件 | 可能复用 | 带依赖解析加载 |
| `Assembly.Load(byte[])` | **不锁文件** | 每次新加载 | ✅ HotReload 选择 |

不锁文件的特性至关重要——因为 `Assembly.Load(byte[])` 将程序集内容读入内存后立即释放文件句柄，这使得后续 `dotnet build` 编译时不会遇到文件被占用的问题。

**加载执行流程**（`LoadPluginFromDisk` 方法）：

```
┌──────────────────────────────────────────────┐
│              开始加载                        │
├──────────────────────────────────────────────┤
│  1. 读取 DLL + PDB 字节流                   │
│     → 文件不锁定，后续编译不受影响           │
├──────────────────────────────────────────────┤
│  2. Assembly.Load(asmBytes, pdbBytes)        │
│     → 新程序集加载到当前 AssemblyLoadContext │
├──────────────────────────────────────────────┤
│  3. 注册到 loadedAssemblies                 │
│     loadedAssemblies[asmName] = assembly     │
│     → 让 TShock 能感知到此程序集            │
├──────────────────────────────────────────────┤
│  4. 扫描 TerrariaPlugin 子类                │
│     foreach type in assembly.GetExportedTypes│
│       if type.IsSubclassOf(TerrariaPlugin)   │
│         && type is public && not abstract   │
├──────────────────────────────────────────────┤
│  5. API 版本校验                            │
│     ApiVersionAttribute 比对 Major/Minor    │
│     与 ServerApi.ApiVersion 一致才通过      │
├──────────────────────────────────────────────┤
│  6. Activator.CreateInstance(type, game)    │
│     → 实例化插件                             │
│     → 用 PluginContainer 包装               │
│     → plugins.Add(container)                │
│     → container.Initialize() 启动插件       │
├──────────────────────────────────────────────┤
│  7. 更新记录 + 计数器递增                   │
│     record.IsLoaded = true                  │
│     record.FileHash = 重新计算              │
│     _hotReloadCount++                       │
└──────────────────────────────────────────────┘
```

### 2.3 SHA256 哈希跟踪机制

哈希跟踪是整个变更检测系统的**数据基石**。其核心逻辑在 `ComputeFileHash` 方法中：

```csharp
internal static string ComputeFileHash(string filePath)
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
    catch { return ""; }
}
```

**哈希在三个关键点被使用：**

1. **初始化基线**（`ScanLoadedPlugins`）：服务器启动时计算每个已加载 DLL 的 SHA256，作为"原始状态"基准
2. **变更检测**（`ScanDirectory`）：扫描目录时重新计算哈希，与基线对比，不一致则标记为"已覆盖"
3. **加载更新**（`LoadPluginFromDisk`）：成功加载后更新 `FileHash` 为当前值，形成新基线

**Dashboard 中的可视化**：使用 `OverwriteStatus` 方法实时对比，并在仪表盘中使用 **红色** 标注哈希不一致的插件。

### 2.4 对比 AutoPluginManager 的异同

HotReload 采用的方案在**技术路线上与 AutoPluginManager 本质相同**——都基于反射操作 ServerApi 内部字段。但存在以下关键差异：

| 维度 | HotReload | AutoPluginManager |
|------|-----------|-------------------|
| **核心机制** | 反射 ServerApi 内部字段 | 反射 ServerApi 内部字段 |
| **哈希跟踪** | ✅ SHA256 完整跟踪 | 视具体实现 |
| **增量检测** | ✅ ScanDirectory 精确区分新增/变更/卸载 | 可能为全量扫描 |
| **批量重载** | ✅ `reload-all` 一键应用所有变更 | 视具体实现 |
| **备份机制** | ✅ 自动备份到 `.hotreload-backup/` | 视具体实现 |
| **保护列表** | ✅ 硬编码 9 个系统组件 | 视具体实现 |
| **并发安全** | ✅ 操作锁 `_opLock` + `_operationInProgress` | 视具体实现 |
| **懒初始化** | ✅ `GamePostInitialize` + 命令触发双重保障 | 视具体实现 |

**核心相同点**：两者都受限于 .NET 的 AssemblyLoadContext 限制——旧程序集一旦加载就无法从默认上下文卸载。

### 2.5 与 AssemblyLoadContext 方案的区别

理想的方案应该使用 **`AssemblyLoadContext`（ALC）**，这是 .NET Core 提供的官方程序集隔离机制：

```
AssemblyLoadContext 方案（理想）:
┌─────────────────────────────────────────────────┐
│  1. 创建自定义 ALC                              │
│     var alc = new PluginLoadContext()           │
│  2. 在 ALC 中加载插件                            │
│     alc.LoadFromAssemblyPath(dllPath)           │
│  3. 卸载时回收整个 ALC                          │
│     alc.Unload()                                │
│     → 所有程序集和类型被 GC 回收                │
│  4. ✅ 内存完全释放                             │
└─────────────────────────────────────────────────┘
```

**为什么 HotReload 没有使用 ALC？**

| 原因 | 详细说明 |
|------|----------|
| **TShock 兼容性** | TShock 的 `ServerApi.Plugins` 使用 `List<PluginContainer>` 直接持有插件实例引用，这些引用存在于默认 ALC 上下文中。如果插件在自定义 ALC 中加载，类型跨上下文交互会引发 `FileNotFoundException` 或类型不匹配异常 |
| **全局类型共享** | 插件通常引用 `TShockAPI.TerrariaPlugin`、`Terraria.Main` 等全局类型。如果插件在自定义 ALC 中加载，这些类型会被 ALC 边界隔离，导致 `is` / `as` 类型判断失败 |
| **事件委托跨上下文** | 插件注册的事件回调如果跨 ALC 边界，会导致委托无法正确解析 |
| **`Activator.CreateInstance`** | TShock 内部使用 `Activator.CreateInstance` 实例化插件，ALC 方式需要额外定制加载逻辑 |

**结论**：在当前 TShock 架构下，使用反射操作 ServerApi 内部字段是实现运行时热重载的**唯一可行方案**。ALC 方案需要 TShock 框架层面进行重大重构才能支持。

---

## 3. 完整命令参考

### 3.1 命令总览

| 命令 | 简写 | 参数 | 所需权限 | 说明 |
|------|------|------|----------|------|
| `/hr` | — | 无 | `hotreload.admin` | 全量仪表盘：显示所有插件状态及变更详情 |
| `/hr load` | `/hr ld` | `<序号\|名称>` | `hotreload.admin` | 加载新插件或重载已有插件 |
| `/hr unload` | `/hr ul` | `<序号>` | `hotreload.admin` | 卸载已加载的插件 |
| `/hr info` | `/hr i` | `<序号\|名称>` | `hotreload.admin` | 查看插件详细信息（路径、哈希、版本等） |
| `/hr reload-all` | `/hr ra` | 无 | `hotreload.admin` | 一键重载所有已变更插件 |
| `/hr help` | `/hr h` | 无 | `hotreload.admin` | 显示帮助信息 |

### 3.2 命令详解

#### `/hr` — 仪表盘

全量仪表盘包含四个区域：

```
=== HotReload 仪表盘 (累计热重载 6 次) ===
已加载 16 / 共 18 个插件

已加载：
  [1] TShockAPI  v6.1.0.0  by The TShock Team [保护]
  [2] BCrypt.Net-Core  v1.0.0.0  by Ryan D. Emerle
  [3] Essentials  v1.0.0.0  by 张三  [红色] ← 磁盘文件已变更，使用 /hr load 3 重载
  ...

已卸载：
  [17] OldPlugin  v2.0.0.0  by 李四  可重新加载

磁盘新增：
  NewPlugin.dll  哈希: a1b2c3d4e5f6...  从未加载
  使用 /hr load NewPlugin 加载

操作提示：/hr load <序号|名称>   /hr unload <序号>   /hr reload-all 一键应用
注意：已累计热重载 15 次，建议在下次维护窗口重启服务器
```

- **绿色（`SendSuccessMessage`）**：正常状态
- **红色（`SendErrorMessage`）**：磁盘文件已变更或内存警告
- **白色（`SendInfoMessage`）**：信息提示

#### `/hr load <序号|名称>` — 加载/重载

支持三种目标解析方式：

1. **按序号**：`/hr ld 3` → 重载 `_records` 列表中 ID=3 的插件
2. **按名称**：`/hr ld Essentials` → 按 DisplayName / AssemblyName / DllFileName 匹配
3. **按文件名**：`/hr ld NewPlugin.dll` → 用于磁盘新增插件

**内部决策树**：

```
传入 target
  ├─ 先在 "未加载变更列表" (GetUnloadedFromDisk) 中查找
  │   ├─ 命中且有对应已加载记录 → 先卸载旧版 → LoadPluginFromDisk
  │   └─ 命中且无已加载记录 → 直接 LoadPluginFromDisk
  └─ 未命中 → 在 _records 中查找 (ResolveRecord)
      ├─ 已加载 → 卸载 → LoadPluginFromDisk
      ├─ 未加载 → LoadPluginFromDisk
      └─ 找不到 → 报错
```

#### `/hr unload <序号>` — 卸载

严格限制：**只能按序号操作**。按名称匹配时需通过 `ResolveRecord` 解析。

前置校验链：
```
1. ResolveRecord(target) → 是否存在
2. IsProtected → 受保护则拒绝
3. IsLoaded → 未加载则提示 "无需卸载"
4. _operationInProgress → 并发锁检查
```

#### `/hr info <序号|名称>` — 插件详情

显示完整的插件元数据：

```
=== 插件详情 ===
名称:     Essentials
程序集:   Essentials
DLL文件:  Essentials.dll
完整路径: C:\Server\ServerPlugins\Essentials.dll
版本:     v1.1.0.0
作者:     张三
状态:     已加载
保护:     否
加载时哈希: a1b2c3d4e5f6g7h8i9j0...
磁盘当前哈希: a1b2c3d4e5f6g7h8i9j0  (与加载时一致)
```

磁盘哈希比对功能对于排查"明明放了新 DLL 却未生效"的问题非常有用。

#### `/hr reload-all` — 批量重载

批量重载的**目标收集逻辑**：

```csharp
var targets = new List<(string Name, string AssemblyName)>();

// 来源1：ScanDirectory 检测到的所有变更（文件被覆盖/新增）
foreach (var c in changes)
    if (!c.IsProtected) targets.Add(...);

// 来源2：已卸载但仍有磁盘文件（如之前被 ul 的插件）
foreach (var p in unloadedPlugins)
    if (!p.IsProtected && !已存在) targets.Add(...);

// 按顺序逐个执行 UpdatePlugin
for (int i = 0; i < targets.Count; i++)
    Core.UpdatePlugin(targets[i].AssemblyName);
```

重要行为：
- **跳过受保护插件**（不会对 TShockAPI 等系统组件操作）
- **去重处理**（同一个插件不会出现在两个来源中）
- 逐个执行，即使中间有失败也不会中断整个批次

---

## 4. 启动行为详解

### 4.1 生命周期时序

```
服务器启动流程
│
├── ServerApi.Initialize()
│   ├── 按顺序加载 ServerPlugins/*.dll
│   ├── HotReload.dll 被 TShock 扫描并加载
│   │   └── Main.Initialize() 被调用
│   │       ├── 注册 /hr 命令（hotreload.admin 权限）
│   │       └── 注册 GamePostInitialize 钩子
│   └── 其他插件依次初始化...
│
├── Terraria 世界加载中...
│   (世界加载可能耗时数秒到数十秒)
│
├── GamePostInitialize 事件触发
│   └── HotReload.OnWorldReady() 被调用
│       └── Core.EnsureInitialized()
│           └── ScanLoadedPlugins()
│               ├── 遍历 ServerApi.Plugins
│               ├── 记录每个插件的元数据
│               └── 计算 SHA256 哈希基线
│
├── 服务器进入正常运行状态
│   └── 此时可执行 /hr 命令
│
└── 如果 GamePostInitialize 未触发
    └── 首次 /hr 命令 → EnsureInitialized()
        └── 同样执行 ScanLoadedPlugins()
```

### 4.2 双重初始化保障

`EnsureInitialized` 采用**双重检查锁定模式（Double-Checked Locking）**：

```csharp
public static void EnsureInitialized()
{
    if (_initialized) return;          // 第一次检查（无锁）
    lock (_initLock)                   // 加锁
    {
        if (_initialized) return;      // 第二次检查（有锁）
        ScanLoadedPlugins();
        _initialized = true;
        TShock.Log.ConsoleInfo($"[HotReload] 已跟踪 {_records.Count} 个插件");
    }
}
```

这种设计保证了：
- **线程安全**：多个命令同时触发时仅初始化一次
- **性能最优**：初始化后不再进入锁竞争路径

### 4.3 扫描结果示例

```
[HotReload] 已跟踪 16 个插件
```

输出的 `16` 是指启用了多少插件记录。注意这个数字可能小于 `ServerPlugins` 目录中的 DLL 数量，因为：

- 受保护插件的 `AssemblyName` 可能与 DLL 文件名不一致
- 某些 DLL 可能包含多个 `TerrariaPlugin` 子类（但技术上 TShock 只识别第一个）
- 未包含 `TerrariaPlugin` 子类的 DLL 不会被 TShock 加载

---

## 5. 受保护插件列表与原因

### 5.1 完整保护列表

```csharp
private static readonly HashSet<string> ProtectedAssemblyNames = new(StringComparer.OrdinalIgnoreCase)
{
    "TShockAPI",          // TShock 核心框架
    "Terraria",           // 游戏主程序集
    "TerrariaServerAPI",  // 服务端 API 层
    "OTAPI",              // Terraria 跨平台运行时钩子
    "MonoMod",            // Mono 修改引擎（OTAPI 依赖）
    "MonoMod.RuntimeDetour", // 运行时钩子库
    "Newtonsoft.Json",    // JSON 序列化基础设施
    "MySql.Data",         // 数据库驱动
    "HotReload",          // 自身（卸了自己就再也无法加载）
};
```

### 5.2 保护原因分析

| 程序集 | 保护原因 | 卸载后果 |
|--------|----------|----------|
| **TShockAPI** | 整个服务器的管理框架，所有插件依赖 | 全部插件崩溃，服务器管理功能完全丧失 |
| **Terraria** | 游戏逻辑核心 | 游戏世界无法继续运行 |
| **TerrariaServerAPI** | 服务端 API 基础设施 | 所有与游戏交互的钩子失效 |
| **OTAPI** | 底层运行时钩子，负责 IL 修改 | 整个服务器运行时崩溃 |
| **MonoMod / MonoMod.RuntimeDetour** | OTAPI 的依赖链 | 间接导致 OTAPI 崩溃 |
| **Newtonsoft.Json** | 配置序列化/反序列化基础设施 | TShock 配置系统崩溃 |
| **MySql.Data** | 数据库连接驱动 | 数据库相关功能（用户系统等）全部失效 |
| **HotReload** | 自身保护—卸载后再无加载入口 | 永久失去热重载能力 |

### 5.3 保护机制的实现

保护检查在三个入口处执行：

1. **卸载时**（`RemovePlugin`）：`if (record.IsProtected) return (false, "禁止操作...")`
2. **加载时**（`UpdatePlugin`）：`if (record.IsProtected) return (false, "禁止操作...")`
3. **批量重载时**（`HandleReloadAll`）：`if (c.IsProtected) continue;`

`IsProtected` 的赋值发生在 `ScanLoadedPlugins` 初始化阶段：

```csharp
IsProtected = ProtectedAssemblyNames.Contains(asmName)
```

判定依据是**程序集名称（Assembly Name）**，而非 DLL 文件名。这使得即使 DLL 被重命名，保护机制仍然有效。

---

## 6. 已知限制

### 6.1 内存泄漏（旧程序集不释放）

**这是当前方案最根本的技术限制**。

#### 问题根源

.NET 的默认 `AssemblyLoadContext`（即 `AssemblyLoadContext.Default`）是所有未指定上下文的程序集默认加载的位置。一旦程序集被加载到默认上下文中，就**没有任何 API 可以将其卸载**——这是 .NET 运行时层面的硬性约束。

```
每次热重载的内存影响：

加载前：001.dll (10MB)  ← 被 Assembly.Load 加载
  ↓ 卸载
内存中：001.dll v1 (10MB)  ← ❌ 无法回收
  ↓ 加载 v2
内存中：001.dll v1 (10MB) + 001.dll v2 (10MB)  ← 🚩 翻倍
  ↓ 下次重载
内存中：001.dll v1 (10MB) + 001.dll v2 (10MB) + 001.dll v3 (10MB)
```

#### 15 次警告阈值

```csharp
private const int MaxHotReloadWarning = 15;
```

警告触发逻辑：

```csharp
private static void CheckMemoryWarning()
{
    if (_hotReloadCount >= MaxHotReloadWarning && _hotReloadCount % 5 == 0)
    {
        // 15、20、25... 次时触发警告
    }
}
```

**为什么选择 15？** 经验值。对于普通规模的 TShock 服务器（10-20 个插件，每个 1-5MB），15 次热重载大约增加 20-100MB 内存，在服务器内存充裕（通常 512MB-2GB）的情况下不会立即造成问题，但确认了这是一个应引起注意的信号。

#### 缓解建议

- 开发期间无需关注此限制（频繁重载是开发常态）
- 生产环境：热重载用于**紧急修复**，正常流程仍建议重启部署
- 定期重启释放内存（配合维护窗口）

### 6.2 事件残留问题

#### 问题描述

当插件被卸载时，HotReload 调用 `plugin.Dispose()`。但如果插件的 `Dispose()` 实现**不完整**——即没有反注册其注册的所有事件钩子——会导致：

1. **事件回调重复执行**：旧实例虽然逻辑上已"卸载"，但其事件委托仍被钩住，新加载的插件实例会再注册一次，导致同一事件被触发两次
2. **匿名委托残留**：使用 `+=` 注册的匿名委托（lambda）无法在 Dispose 中按引用反注册
3. **静态事件泄漏**：如果插件注册了静态事件（如 `ServerApi.Hooks.*`），即使插件实例被 GC，静态委托仍持有对实例的引用

#### 典型表现

```
# 玩家聊天时，某插件输出两条同样的消息
[插件] 欢迎玩家张三加入游戏！  ← 旧实例残留
[插件] 欢迎玩家张三加入游戏！  ← 新实例
```

#### 排查方法

1. 热重载后检查是否有重复消息或行为
2. 使用 `TShock.Log.ConsoleInfo` 添加日志追踪事件执行次数
3. 审查目标插件的 `Dispose()` 方法实现

#### 解决方式（需插件开发者配合）

```csharp
// ❌ 不完整的 Dispose
protected override void Dispose(bool disposing)
{
    if (disposing) { }
    base.Dispose(disposing);
}

// ✅ 完整的 Dispose
protected override void Dispose(bool disposing)
{
    if (disposing)
    {
        ServerApi.Hooks.NetGetData.Deregister(this, OnGetData);
        ServerApi.Hooks.ServerChat.Deregister(this, OnChat);
        // ... 反注册所有钩子
    }
    base.Dispose(disposing);
}
```

### 6.3 TSWeb 兼容性问题

#### 问题描述

TSWeb 插件（TSWeb 管理面板后端）的热重载会导致其注册的 REST API 路由丢失。

#### 根本原因

TSWeb 在 `Initialize()` 中通过 `TShockAPI.RestApi.Register` 或类似机制注册 HTTP 路由端点。当 TSWeb 被卸载时：

1. `Dispose()` 应反注册所有路由
2. 重新加载后 `Initialize()` 重新注册路由

但实际 TSWeb 的 `Dispose()` **可能未完全清理 REST API 路由表**，或者 REST API 路由注册在内部使用了无法二次注册的状态。

#### 影响范围

| 受影响功能 | 说明 |
|------------|------|
| Web 管理面板 | 页面可打开但 API 请求返回 404 |
| REST API 调用 | 所有自定义端点失效 |
| 外部工具接入 | 依赖 TSWeb API 的外部工具无法工作 |

#### 解决方案

此问题**不在 HotReload 的能力范围内**，需要 TSWeb 自身修复：
- 实现完整的路由反注册机制
- 支持路由表状态重置
- 或采用幂等的路由注册方式

当前建议：**不要对 TSWeb 执行热重载**，将 TSWeb 加入保护列表或将其排除在重载目标之外。

---

## 7. 下载与部署

### 7.1 编译命令

```bash
# Release 编译
dotnet build -c Release

# Debug 编译（带调试符号，适合开发调试）
dotnet build -c Debug
```

**项目配置**（`HotReload.csproj`）：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="TShock" Version="6.1.0" />
  </ItemGroup>
</Project>
```

编译产物位于 `bin/Release/net9.0/`：

```
bin/Release/net9.0/
├── HotReload.dll          ← 主程序集
├── HotReload.pdb          ← 调试符号（可选）
└── (其他依赖项)
```

### 7.2 部署到 ServerPlugins 目录

```bash
# 将编译产物复制到服务器插件目录
cp bin/Release/net9.0/HotReload.dll /path/to/server/ServerPlugins/
```

### 7.3 使用流程

```
首次部署：
1. dotnet build -c Release
2. 将 HotReload.dll 放入 ServerPlugins/
3. 重启服务器
4. 控制台输出: [HotReload] 已跟踪 X 个插件

日常使用：
1. 修改插件代码 → dotnet build -c Release
2. 将新 DLL 复制到 ServerPlugins/（覆盖旧文件）
3. 游戏中执行 /hr           （查看变更列表）
4. 执行 /hr ld <名称>       （逐个重载）
   或 /hr ra                （一键全部重载）

验证：
5. 执行 /hr info <序号>     （检查哈希是否一致）
```

---

## 8. 最佳实践

### 8.1 开发调试工作流

**推荐工作流（最高效）：**

```
┌─────────────────────────────────────────────────────────────────┐
│  1. 在 IDE 中编写/修改插件代码                                  │
│  2. dotnet build -c Debug（快速编译，增量编译通常 < 3 秒）      │
│  3. 将 DLL 复制到服务器 ServerPlugins/（覆盖旧文件）            │
│  4. 游戏中 /hr ld <名称>                                       │
│  5. 观察插件行为                                               │
│  6. 回到第 1 步                                                │
└─────────────────────────────────────────────────────────────────┘
```

**小技巧**：可以为插件项目配置构建后事件，自动将 DLL 复制到测试服务器目录：

```xml
<!-- 在插件项目的 .csproj 文件中添加 -->
<Target Name="CopyToServer" AfterTargets="Build">
  <Copy SourceFiles="$(OutputPath)$(AssemblyName).dll"
        DestinationFolder="C:\TestServer\ServerPlugins\" />
</Target>
```

这样每次编译后 DLL 自动到位，只需在游戏中执行 `/hr ld <名称>` 即可。

### 8.2 与备份机制配合使用

HotReload 内置自动备份机制：

```csharp
private static void BackupDll(string filePath)
{
    var backupDir = Path.Combine(ServerPluginsPath, ".hotreload-backup");
    Directory.CreateDirectory(backupDir);
    var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
    var backupPath = Path.Combine(backupDir,
        $"{Path.GetFileNameWithoutExtension(fileName)}.{timestamp}.dll");
    File.Copy(filePath, backupPath, overwrite: true);
}
```

备份文件位于 `ServerPlugins/.hotreload-backup/`，命名格式为 `<插件名>.<时间戳>.dll`。

**备份恢复示例**：

```bash
# 查看备份
ls ServerPlugins/.hotreload-backup/
  Essentials.20260715-143022.dll
  Essentials.20260715-150105.dll

# 恢复到特定版本
cp ServerPlugins/.hotreload-backup/Essentials.20260715-143022.dll \
   ServerPlugins/Essentials.dll
# 然后游戏中 /hr ld Essentials
```

### 8.3 何时应该重启服务器

| 场景 | 是否需重启 | 说明 |
|------|-----------|------|
| 开发调试 | ❌ 不需要 | 热重载即可，效率最高 |
| 紧急修复线上 Bug | ❌ 不需要 | 热重载快速修复，零停机 |
| 热重载次数达到 15+ | ⚠️ 建议重启 | 释放内存，避免 OOM |
| 累积热重载超过 30 次 | ✅ 强力建议 | 内存占用过高风险 |
| 插件出现事件重复 | ⚠️ 可能需要 | 若热重载无法解决残留问题 |
| TSWeb 被热重载导致路由丢失 | ✅ 必须重启 | 热重载无法恢复路由 |
| 服务器性能无故下降 | ⚠️ 建议重启 | 排查是否因热重载内存泄漏导致 |

---

## 9. 源码架构分析

### 9.1 分层架构总览

HotReload 的源码结构清晰，采用**双层架构**：

```
HotReload/
├── HotReload.csproj      ← 项目配置
├── HotReload.slnx        ← 解决方案
├── Main.cs               ← UI / 命令层（393 行）
├── Core.cs               ← 核心引擎层（630 行）
└── README.md             ← 用户文档
```

### 9.2 Main.cs — UI / 命令层

**职责**：命令解析、玩家交互、信息展示。

**类结构**：

```
HotReload (继承 TerrariaPlugin)
├── [属性] Name, Description, Author, Version
├── Initialize()
│   ├── 注册 /hr 命令
│   └── 注册 GamePostInitialize 钩子
├── Dispose(bool)
│   ├── 反注册钩子
│   └── 清理命令
│
├── OnWorldReady(EventArgs)          ← 静态回调
│   └── Core.EnsureInitialized()
│
├── HandleCommand(CommandArgs)       ← 命令分发
│   ├── 无参数 → ShowDashboard()
│   ├── status/st → ShowDashboard()
│   ├── load/ld → HandleLoad()
│   ├── unload/ul → HandleUnload()
│   ├── info/i → HandleInfo()
│   ├── reload-all/ra → HandleReloadAll()
│   └── help/h → ShowHelp()
│
├── ShowDashboard()                  ← 仪表盘渲染
│   ├── Core.ListPlugins() + Core.ScanDirectory()
│   ├── 已加载区（含 OverwriteStatus 标记红色）
│   ├── 已卸载区
│   ├── 磁盘新增区（绿色）
│   └── 内存警告（≥15 次）
│
├── HandleLoad()                     ← 加载命令
│   ├── 数字 → ResolveRecord
│   └── 名称 → Core.UpdatePlugin
│
├── HandleUnload()                   ← 卸载命令
│   ├── 前置校验（存在 / 已加载 / 非保护）
│   └── Core.RemovePlugin
│
├── HandleInfo()                     ← 详情命令
│   ├── Core.ResolveRecord
│   └── OverwriteStatus 哈希对比
│
├── HandleReloadAll()                ← 批量重载
│   ├── 收集变更 + 已卸载（排除保护）
│   └── 逐个执行 Core.UpdatePlugin
│
├── ShowHelp()                       ← 帮助信息
│
└── OverwriteStatus()                ← 哈希对比工具
    ├── Core.ComputeFileHash()
    └── 返回 overwritten + currentHash
```

**关键设计决策**：

| 决策 | 理由 |
|------|------|
| 所有 `Handle*` 方法均为 `static` | 无需实例状态，简化依赖 |
| 操作前调用 `Core.EnsureInitialized()` | 确保记录已就绪 |
| `ShowDashboard` 合并 scan 结果 | 一次调用即获取完整视图，减少 I/O |
| 颜色区分（红/绿/白） | 视觉化表达变更状态，提升 UX |

### 9.3 Core.cs — 核心引擎层

**职责**：插件的加载/卸载/扫描/跟踪等所有底层操作。

**类结构**：

```
PluginRecord (数据类)
├── Id, DisplayName, AssemblyName
├── DllFileName, FullPath
├── Version, Author
├── FileHash (SHA256)
├── IsLoaded, IsProtected
└── ToString() → 格式化展示

Core (静态类 - 核心引擎)
├── [字段] ProtectedAssemblyNames         ← 受保护程序集列表
├── [字段] _records                       ← 所有插件记录
├── [字段] _initialized, _initLock        ← 懒初始化
├── [字段] _hotReloadCount                ← 累计热重载计数
├── [字段] _operationInProgress, _opLock  ← 并发操作锁
│
├── EnsureInitialized()                   ← 双重检查锁定初始化
│   └── ScanLoadedPlugins()
│
├── ScanLoadedPlugins()                   ← 扫描已加载插件建立基线
│   ├── GetLoadedAssembliesMap()
│   ├── 遍历 ServerApi.Plugins
│   └── 创建 PluginRecord（含哈希计算）
│
├── ScanDirectory()                       ← 扫描目录检测变更
│   ├── 遍历 ServerPlugins/*.dll
│   ├── 已知+哈希一致 → 跳过
│   ├── 已知+哈希不同 → 标记变更
│   └── 未知 → 标记新增
│
├── GetUnloadedFromDisk()                 ← 获取所有未加载项
│   ├── 已加载且哈希一致 → 跳过
│   ├── 已加载且哈希不同 → 覆盖变更
│   ├── 已记录但未加载 → 主动卸载
│   └── 全新文件 → 新增
│
├── RemovePlugin(target)                  ← 公开卸载接口
│   ├── ResolveRecord + 前置校验
│   ├── 操作锁获取
│   └── RemovePluginInternal()
│
├── RemovePluginInternal(record)          ← 内部卸载逻辑
│   ├── GetLoadedAssembliesMap/GetPluginsList
│   ├── plugin.Dispose()
│   ├── Plugins.Remove / loadedAssemblies.Remove
│   └── record.IsLoaded = false
│
├── UpdatePlugin(target)                  ← 公开加载接口
│   ├── 优先从变更列表匹配
│   ├── 已加载 → RemovePluginInternal
│   ├── BackupDll()
│   └── LoadPluginFromDisk()
│
├── LoadPluginFromDisk(record)            ← 内部加载逻辑
│   ├── File.ReadAllBytes → Assembly.Load
│   ├── 注册到 loadedAssemblies
│   ├── 扫描 TerrariaPlugin 子类
│   ├── API 版本校验
│   └── Activator.CreateInstance → Initialize()
│
├── ResolveRecord(target)                 ← 记录解析（序号/名称）
│
├── ComputeFileHash(path)                 ← SHA256 哈希计算
│
├── BackupDll(path)                       ← 自动备份
│   └── 复制到 .hotreload-backup/
│
├── CheckMemoryWarning()                  ← 内存警报检查
│   └── ≥15 次 && 5 的倍数 → 输出警告
│
├── GetHotReloadCount()                   ← 获取累计次数
│
└── [反射方法]                            ← 获取 ServerApi 内部状态
    ├── GetLoadedAssembliesMap()          ← loadedAssemblies 字段
    ├── GetPluginsList()                  ← plugins 字段
    └── GetGameInstance()                 ← game 字段
```

**并发安全设计**：

```csharp
// 操作锁 — 确保同一时间只有一个卸载或加载操作在执行
private static volatile bool _operationInProgress;
private static readonly object _opLock = new();

// 使用模式
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
```

### 9.4 反射字段对照表

| ServerApi 内部字段 | 类型 | 访问修饰符 | 获取方式 |
|-------------------|------|-----------|----------|
| `loadedAssemblies` | `Dictionary<string, Assembly>` | `private static` | `GetField("loadedAssemblies", NonPublic\|Static)` |
| `plugins` | `List<PluginContainer>` | `private static` | `GetField("plugins", NonPublic\|Static)` |
| `game` | `Main` | `private static` | `GetField("game", NonPublic\|Static)` |

### 9.5 关键数据流

```
初始化流程：
  GamePostInitialize
       │
       ▼
  EnsureInitialized()
       │
       ▼
  ScanLoadedPlugins()
       │
       ├── GetLoadedAssembliesMap() → fileName→Assembly 映射
       ├── 遍历 ServerApi.Plugins → 每个 PluginContainer
       │       │
       │       ▼
       │   plugin.GetType().Assembly.GetName().Name
       │       │
       │       ▼
       │   ComputeFileHash(fullPath) → SHA256
       │       │
       │       ▼
       │   创建 PluginRecord 加入 _records
       │
       ▼
  _initialized = true

变更检测流程：
  /hr 命令
       │
       ▼
  ScanDirectory()
       │
       ▼
  遍历 ServerPlugins/*.dll
       │
       ├── AssemblyName 在 _records 中？
       │   ├── 是 → 哈希是否一致？
       │   │   ├── 一致 → 跳过（正常运行）
       │   │   └── 不同 → 标记为"覆盖变更"
       │   └── 否 → 标记为"磁盘新增"
       │
       ▼
  返回 changes 列表 → 仪表盘展示

加载流程：
  /hr ld <名称>
       │
       ▼
  UpdatePlugin(target)
       │
       ├── GetUnloadedFromDisk() → 变更列表
       ├── 匹配目标
       ├── 若已加载 → RemovePluginInternal(卸载)
       │       │
       │       ▼
       │   BackupDll(备份旧文件)
       │       │
       │       ▼
       └── LoadPluginFromDisk(加载)
               │
               ▼
           Assembly.Load(byte[]) → 新程序集
           loadedAssemblies[asm] = assembly
           扫描 TerrariaPlugin 子类
           API 版本校验
           Activator.CreateInstance
           container.Initialize()
```

---

## 10. 常见问题与排错

### 10.1 加载失败："在 xxx.dll 中未找到有效的 TerrariaPlugin 子类"

**原因**：DLL 中不包含公开的、非抽象的、继承 `TerrariaPlugin` 且标注了 `[ApiVersion]` 特性的类。

**排查步骤**：
1. 使用工具查看 DLL 内容：
   ```bash
   # 在 PowerShell 中查看导出类型
   [System.Reflection.Assembly]::LoadFile("path\to\xxx.dll").GetExportedTypes()
   ```
2. 确认插件类标注了 `[ApiVersion(2, 1)]`
3. 确认插件类是 `public` 且非 `abstract`
4. 确认继承链：`class MyPlugin : TerrariaPlugin`

### 10.2 加载失败："API 版本不匹配"

**原因**：插件标注的 `ApiVersion` 与服务器当前的 `ServerApi.ApiVersion` 不一致。

**排查**：
```bash
# 查看插件标注的 API 版本
[System.Reflection.Assembly]::LoadFile("xxx.dll").GetCustomAttributes(
    [TerrariaApi.Server.ApiVersionAttribute], $false)

# 查看服务器的 API 版本（在 TShock 控制台）
/serverinfo
```

**解决**：修改插件项目的 `[ApiVersion]` 属性为服务器对应版本（通常是 `2, 1`）。

### 10.3 卸载失败："无法访问 ServerApi 内部状态"

**原因**：反射获取 `ServerApi` 内部字段失败。

**可能原因**：
1. TShock 版本升级导致内部字段名变更（`loadedAssemblies` / `plugins` / `game`）
2. TShock 编译时使用了混淆或字段名重写

**检查**：
```bash
# 使用反射调试工具查看实际字段名
[System.Reflection.BindingFlags]::NonPublic -bor
[System.Reflection.BindingFlags]::Static
```

**修复**：更新源码中的字段名字符串以匹配新版本 TShock。

### 10.4 热重载后插件行为异常（事件重复触发）

**原因**：插件的 `Dispose()` 未完整清理事件注册。

**排查方法**：
1. 查看插件源码中的 `Dispose()` 实现
2. 检查是否有未反注册的 `ServerApi.Hooks.*`、`Commands.ChatCommands` 等
3. 检查是否有静态事件注册

**临时解决**：完全重启服务器。

**根本解决**：修复插件的 `Dispose()`，确保反注册所有事件。

### 10.5 "已有操作正在进行中"

**原因**：多个 `/hr` 命令同时在短时间内触发。

**说明**：这是 HotReload 的并发保护机制在工作。等待当前操作完成后重试即可。

### 10.6 仪表盘显示 "磁盘文件已变更" 但实际未改

**原因**：SHA256 哈希比对发现差异，可能原因：
1. 文件确实被覆盖（即使你认为没改）
2. 文件权限变更导致读取内容不同
3. NTFS 文件流变化

**验证**：
```bash
# 手动计算哈希对比
certutil -hashfile ServerPlugins/xxx.dll SHA256
```

### 10.7 编译错误：找不到 TShock 包

**原因**：NuGet 源未配置 TShock 包或版本不匹配。

**解决**：
```bash
# 确认 NuGet 源配置
dotnet nuget list source

# 检查 TShock 版本
dotnet list package --outdated
```

### 10.8 如何判断是否需要重启服务器

```bash
# 查看 HotReload 累计次数
/hr
# 仪表盘顶部显示：累计热重载 N 次

# 查看服务器内存占用（在 TShock 控制台或 SSH）
# Linux/macOS:
ps aux | grep Terraria
# Windows:
tasklist /FI "IMAGENAME eq TerrariaServer.exe"
```

**决策参考**：
- 累计 < 15 次：无需担心，继续使用
- 15-30 次：建议计划下次维护时重启
- > 30 次：尽快安排重启

---

## 附录：源码速查

### A. 核心常量

```csharp
// Core.cs
private const int MaxHotReloadWarning = 15;
private static readonly HashSet<string> ProtectedAssemblyNames = new(StringComparer.OrdinalIgnoreCase)
{
    "TShockAPI", "Terraria", "TerrariaServerAPI", "OTAPI",
    "MonoMod", "MonoMod.RuntimeDetour", "Newtonsoft.Json",
    "MySql.Data", "HotReload",
};
```

### B. 反射绑定标志

```csharp
private static readonly BindingFlags PrivateFlags =
    BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
```

### C. 关键文件路径

```csharp
private static string ServerPluginsPath =>
    Path.Combine(AppContext.BaseDirectory, ServerApi.PluginsPath ?? "ServerPlugins");

// 备份目录
var backupDir = Path.Combine(ServerPluginsPath, ".hotreload-backup");
```

---

> **文档版本**: 1.0  
> **最后更新**: 2026-07-15  
> **维护者**: TsWeb 项目组  
> **下一阶段计划**: 考虑添加 AssemblyLoadContext 实验性支持、可配置保护列表
