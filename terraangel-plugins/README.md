# terraangel-plugins — TerraAngel 验证插件工程

用于开发 `.TAPlugin.dll` 插件，进行 **Bug 验证**（复现/确认服务器漏洞）与 **反制研究**（分析 TerraAngel 源码后反哺 TsWeb 服务端反作弊）。

## 目录结构

```
terraangel-plugins/
├── Directory.Build.props   # ★ 统一引用配置（方案A）：所有插件共享
├── client-build/           # 放客户端构建产物 dll（需自行放入，见下）
└── TaDebug.TAPlugin/       # 示例验证插件
```

## 一次性准备：client-build 目录

插件引用的是 **魔改后的客户端二进制**（不可用 NuGet 替代，官方未发布包）：

1. 构建 TerraAngel 客户端（`./fast.ps1 -Decompile -Patch -Compile`），或从已有客户端安装目录获取
2. 将产物目录 `TerraAngel/Terraria/bin/Release/net10.0/` 下的 dll 复制到 `client-build/`：
   - `TerraAngelPluginAPI.dll`（必需）
   - `Terraria.dll`（必需）
   - `ReLogic.dll`（建议）
3. 以后每次升级 TerraAngel 版本，同步替换这些 dll

> 路径只需在 `Directory.Build.props` 的 `<TerraAngelClientDir>` 配置一次，所有插件生效。

## 新建一个插件

1. 复制 `TaDebug.TAPlugin/` 目录并重命名（如 `MyPlugin.TAPlugin/`）
2. 修改 csproj：`<AssemblyName>` 必须以 `.TAPlugin` 结尾（加载器硬性要求）
3. 创建类继承 `TerraAngel.Plugin.Plugin`，实现 `Name`、构造函数（`string path`）、`Load/Update/Unload`
4. 编译输出 `bin/Release/net10.0/xxx.TAPlugin.dll`

## 安装与使用（客户端侧）

1. 将插件 dll 放入客户端插件目录：`{Terraria 存档}/TerraAngel/Plugins/`
2. 启动客户端，在插件 UI 中**勾选启用**该插件（插件默认禁用）
3. 控制台验证：
   ```
   #hi 你好 world     → 测试插件控制台命令链路
   #reload            → 热重载所有插件（会先 Unload 再重新 Load）
   #help              → 查看已注册命令
   ```

## 技术要点备忘（源码实证）

| 项 | 值 |
|---|---|
| 目标框架 | `net10.0`（官方 PLUGINS.md 写的 net7.0 已过时） |
| 文件名 | 必须以 `.TAPlugin.dll` 结尾 |
| 加载方式 | `AssemblyLoadContext` 可收集 ALC，`#reload` 可真正卸载程序集 |
| 默认状态 | 禁用，需 UI 勾选启用 |
| 控制台命令 | `ClientLoader.Console.AddCommand(name, Action<CmdStr>, desc)`，以 `#` 触发 |
| 依赖 dll | 放插件目录即可被 `AssemblyResolve` 解析；引用本体用 `Private=false` 不拷贝 |
