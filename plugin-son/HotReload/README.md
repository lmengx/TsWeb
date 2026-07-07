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
| `/hr list` | `/hr l` | 列出全部插件（含未加载状态标记） |
| `/hr listload` | `/hr ll` | 仅列出已加载的插件，标记 dll 已被覆盖的 |
| `/hr listunload` | `/hr lu` | 扫目录，列出所有未加载项（新增/覆盖/已卸载） |
| `/hr remove <序号\|名称>` | `/hr r` | 卸载指定插件 |
| `/hr update <序号\|名称>` | `/hr u` | 加载或更新指定插件 |
| `/hr help` | `/hr h` | 显示帮助 |

### 使用流程

```
# 1. 查看已加载插件中哪些 dll 被替换了
/hr ll

# 2. 查看未加载的插件 / 新增文件 / 被覆盖的文件
/hr lu
/hr lu

# 3. 扫描变更
/hr scan

# 4. 按序号加载新版本
/hr update 1

# 或直接按名称
/hr u MyPlugin
```

### listload 示例

```
> [ 1] TSWeb                   v1.0.0.0  by lmx12330
>       文件: TSWeb.dll                  当前哈希: a1b2c3d4e5f6...  [已被覆盖，dll 已与加载时不同]
> 其中 1 个插件的 dll 文件已被替换，建议 /hr update 重新加载
> 其余 3 个插件状态正常：
> [ 2] TShockAPI               v5.x      by Pryaxis  [保护]
```

### listunload 示例

```
> 未加载插件共 2 个：
> [ 5] MyPlugin                v1.0.0.0  by someone  [已被主动卸载]
> [ 6] NewPlugin                          by unknown  [新增文件（从未加载）]
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
