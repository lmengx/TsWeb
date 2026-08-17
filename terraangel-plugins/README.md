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

# TaDebug 内置工具/窗口

| 工具 | 位置 | 可见范围 | 说明 |
|---|---|---|---|
| NPC Summoner | 主窗口「NPC Summoner」标签页 | 仅游戏内（Tool） | 搜索 + 分类生物召唤面板，多人走 FishOutNPC(130) 包（服务端无白名单检查） |
| 进服密码爆破 | 独立窗口「进服密码爆破」（默认自动打开） | **主菜单 + 游戏内均可**（ClientWindow） | 迷你客户端并发握手，逐个尝试 ServerPassword（安全测试用途：验证服务器抗暴力破解能力，反哺 TSWeb 反作弊） |

## 进服密码爆破工具（PasswordBruteWindow）

- **用途**：对自己/授权的 TShock 服务器做进服密码抗爆破能力测试（错误密码立即被踢 = 无重试机会；但无连接级限速）
- **显示**：用 `ClientWindow`（非 Tool）——Tool 的 DrawUI 只在 MainWindow 的「游戏内」分支渲染（`MainWindow.cs:31 if (!Main.gameMenu && ...)`），主菜单看不到；ClientWindow 渲染循环只看 `IsEnabled`（`ClientRenderer.cs:309`），主菜单同样可显示，且右上角可关闭/全局开关可恢复
- **输入**：目标 IP、端口、并发数（1-200）、版本串（留空自动取当前客户端）
- **密码来源三种模式**：
  - 自动生成（默认）：字符集（数字/小写/大写/符号复选）+ 长度范围 1~12，程序按长度递增索引穷举（O(1) 内存，进度显示总空间）
  - 手动字典：多行文本，每行一个，`#` 开头为注释
  - **导入字典文件**（手动模式内）：「导入字典文件」按钮 → SDL3 原生文件对话框（与 TerraAngel WorldEditPixelArt 同款）→ 选 txt 等文本文件，每行视作一个密码，追加到手动字典文本框
- **日志**：内置「复制日志」「清空日志」按钮
- **Kick 包完整解析**：NetworkText mode 0(Literal)/1(Formattable)/2(Localized) 递归解析，TShock 踢人用的 `Kicked: {0}` 是 mode 1，旧版解析不出原因——现可看到真实被拒原因（如 Outdated version / Bounced）
- **判定**：收到 WorldData(7) = 密码正确；发 SendPassword(38) 后收到 Kick(2)/断开 = 失败
- **协议基准**：Terraria 1.4.5 / TShock 6.x，帧格式 `[ushort 总长含头][type][body]`（与插件端 CrossLoginClient / TransferProtocol 同款，源码实证）
  - 握手：ClientHello(1) → ClientUUID(68) → 等 LoadPlayer(3) → PlayerInfo(4) + ContinueConnecting2(6) → 等 RequestPassword(37) → SendPassword(38) → 结果
  - 找到密码后自动停止全部并发；目标无密码时提示并停止
- **代码**：`TaDebug.TAPlugin/PasswordBruteWindow.cs`（DebugPlugin.Load 经 `ClientLoader.MainRenderer.AddWindow` 注册，Unload 经 RemoveWindow 移除并强制停线程）

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
