# HotReload — 运行时插件热重载管理

## 功能

在**不重启服务器**的情况下，卸载/重新加载插件。适用于：

- 插件开发调试（改代码 → 重新编译 → `/hr update` 立即可见）
- 紧急移除有问题的插件
- 替换插件 dll 文件后即时生效

## 启动行为

服务器世界加载完成后（`GamePostInitialize`），HotReload 自动执行初次扫描：
- 遍历所有已加载的插件
- 记录每个插件的名称、版本、dll 文件名
- 计算并记录每个 dll 的 SHA256 哈希作为基线

此后 `/hr lu` 对比的是**服务器启动时的状态**，能准确检测出运行过程中哪些 dll 被替换过。

如果 `GamePostInitialize` 因某些原因未触发，第一次执行 `/hr` 命令时也会触发扫描。

## 命令

| 命令 | 简写 | 说明 |
|------|------|------|
| `/hr` | — | 仪表盘：全量插件状态 + 变更详情（覆盖/新增分别用红/绿色标注） |
| `/hr load <序号\|名称>` | `/hr ld` | 加载或重载。已跟踪的插件可用序号，磁盘新增的只能用名称 |
| `/hr unload <序号>` | `/hr ul` | 卸载已加载的插件 |
| `/hr info <序号\|名称>` | `/hr i` | 查看插件详细信息（路径、哈希对比） |
| `/hr reload-all` | `/hr ra` | 一键重载所有已变更的插件 |
| `/hr help` | `/hr h` | 显示帮助 |

### 使用流程

```
# 1. 查看仪表盘（全量列表 + 变更详情）
/hr

# 2. 按序号加载或重载
/hr load 2

# 磁盘新增的只能用名称
/hr ld NewPlugin

# 3. 卸载
/hr unload 2

# 4. 一键全部应用
/hr reload-all

# 5. 查看某个插件的详细信息
/hr info 1
```

## ⚠️ 重要限制

### 受保护插件（不可操作）

以下插件被硬编码保护，禁止卸载/更新：

- `TShockAPI` — TShock 核心
- `Terraria` / `TerrariaServerAPI` — 游戏核心
- `Newtonsoft.Json` / `MySql.Data` — 基础设施库
- `HotReload` — 自身（卸了自己就没法再加载）

### 内存泄漏

**没有使用 AssemblyLoadContext**，热重载后的旧程序集**永远留在内存中**。累计超过 15 次热重载后控制台会输出警告，建议在下次维护窗口**重启服务器**以释放内存。

### 事件残留

部分插件的 `Dispose()` 可能未正确反注册所有事件钩子，热重载后可能出现：

- 事件被触发两次（新旧各一次）
- 匿名委托残留

如果发现此类问题，请在对应插件的 `Dispose()` 中补全清理逻辑。

### TSWeb

TSWeb 的热重载会导致其注册的 REST API 路由丢失，Web 管理面板会断开。这是 TSWeb 自身需要修复的问题，与本插件无关。

## 原理

采用与 AutoPluginManager 相同的方案（非 ALC）：

1. **卸载**：反射 `ServerApi` 内部字段，从 `plugins` 列表和 `loadedAssemblies` 字典中移除
2. **加载**：`Assembly.Load(byte[])` 从字节数组加载新 dll，扫描 `TerrariaPlugin` 子类，调用 `Initialize()`
3. **哈希跟踪**：记录每个 dll 的 SHA256 哈希，用于 scan 检测变更

## 配置文件

无。保护列表和阈值硬编码在代码中。

## 编译

```bash
dotnet build -c Release
```

将生成的 `HotReload.dll` 放入 `ServerPlugins/` 目录，重启服务器即可。
