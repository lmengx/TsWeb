# TSWeb 项目上手指南

## 第一步：查看入口文件

拿到项目后，**必须先查看 `plugin/Main.cs`**，这是整个 TSWeb 插件的核心入口。

`Main.cs` 负责：
- 初始化所有子模块
- 注册 REST API 路由（Web 管理后端）
- 注册游戏内聊天命令
- 注册配置重载事件
- 统一管理插件生命周期（初始化、重载、释放）

理解 `Main.cs` 的 Initialize() 方法，就能掌握整个插件的功能全貌和模块间的关系。

---

## 参考资料（本地已有，禁止联网搜索）

以下资料均位于项目本地，**绝对禁止去网上搜索源码**：

### TShock 框架源码
- **路径**：`参考源码/TShock-general-devel`
- 包含 TShock 核心库（TShockAPI）的完整源代码
- 涉及配置、命令、权限、REST API 等机制时优先查阅此目录

### 插件参考库
- **路径**：`参考源码/TShockPlugin-master`
- 包含大量 TShock 插件的参考实现
- 可作为功能实现和代码风格的参考

### 开源插件库
- **路径**：`参考源码/开源插件`
- 收集的其他开源 TShock 插件
- 可用于对比参考和学习

---

## 目录约定：scripts/ 与 tmp/（临时区）

### scripts/ —— 常用脚本仓库（本地使用，已在 .gitignore 忽略）

按分类存放**整理好的常用工具**，新增可复用工具放入对应分类：

| 分类 | 内容 |
|------|------|
| `scripts/反编译/工具` | Terraria/TShock 反编译工具（`_ilspy_*.py`、`th_decomp`、`th_dump`、`dcmp_drop`、`dcmp_mb`、`dcmpv7` 及 find/scan 辅助脚本） |
| `scripts/反编译/参考源码` | 反编译参考源码与输出（`_tml_full`、`_tml_ref`、`_bossai_ref`、`_pg_ref`、`_spectate_ref`、`_t8ref`、`_terraria_ref`、`_fargo_*`、`TShock` 源码、decompiled `.cs`） |
| `scripts/抓包/工具` | 抓包工具（`PacketCatch`、`TerraAngel1457Patch`） |
| `scripts/抓包/数据` | 抓包原始数据与日志（`.pcapd`、`进服原始日志.txt`、`抓包节选.txt`、`跨服收包.txt`） |
| `scripts/分析` | 分析/辅助脚本（Python/JS/MJS）与 `api_diff` API 差异对比 |
| `scripts/文档` | 设计文档、规范、审查报告（`开发设计*.txt`、`HOUSE_SPEC.md`、`权限设计_TSWeb_RBAC.md`、`自动任务系统设计.md` 等） |
| `scripts/数据` | 数据文件（strings 表、客户端清单、`弹幕违禁.json`、世界存档 `.wld`/`.tsb`、`tshock.sqlite`、APK、zip、dll） |

### tmp/ —— 临时与归档区（已 gitignore，禁止进 git）

**后续所有临时性文件一律放入 `tmp/`**，不得堆进 `scripts/` 或其他目录。包括：

1. 一次性/临时脚本与中间产物（用后即弃的分析脚本、反编译临时输出、转储文件）
2. 原始抓包数据、抓包日志（`.pcapd`、进服原始日志等未整理的数据）
3. 大文件与二进制（APK、dll、世界存档 `.wld`/`.tsb`、sqlite、zip 包）
4. 测试数据、复现材料、旧版本/重复产物、备份文件
5. 尚未分类整理的散落文件（先放 tmp，整理后再移入 scripts/ 对应分类）
6. 任何不应提交到 git 的本地杂物

**规则：可复用 → 整理进 `scripts/` 对应分类；一次性/临时/大文件 → 放 `tmp/`。**
从 scripts/ 清理出的杂项统一归档到 tmp/（tmp 保留 scripts/ 全量快照，可随时找回）。
当前 scripts/ 结构说明见 `scripts/README.md`。

---

## 核心原则

| 原则 | 说明 |
|------|------|
| **本地优先** | 所有源码资料本地已全，优先从本地查找 |
| **禁止联网搜源码** | 不得搜索 TShock API、插件源码等任何本已存在的代码 |
| **从 Main.cs 开始** | 理解插件入口是阅读任何模块的前提 |
| **查源码先于提问** | 遇到问题先查阅本地源码资料，而非直接询问 |
| **禁止私自写脚本** | 未经授权不得主动创建 CI/CD、构建、部署、拷贝等辅助脚本，项目已有 `start.bat` 等工具。永远禁止taskkill掉用户能操作的已有进程，开用户无法操作的进程来验证更新 |
| **禁止 Emoji** | 全项目任何位置（界面文案、注释、文档、聊天、按钮、提示）禁止使用 emoji 字符；图标一律用内联 SVG 或纯文字/CSS 实现 |

---

## 热重载约定（HotReload，重要）

**项目的"热重载" = HotReload 插件（`plugin-son/HotReload/`）的 `/hr` 命令机制，与 TShock 的 `/reload` 命令完全是两回事。禁止把两者混为一谈。**

- **机制**：`/hr load <名称>` 或 `/hr reload-all`（HotReload 插件），对 `ServerPlugins` 目录下的 DLL 做反射热加载：
  - `Core.cs` `LoadPluginFromDisk`：`Assembly.Load(asmBytes, pdbBytes)`（从字节数组加载，不锁文件）→ `Activator.CreateInstance` 创建插件 → `new PluginContainer(...)` → **直接调用该插件的 `Initialize()`**（世界早已加载，`Main.gameMenu == false`）。
  - 受保护名单 `ProtectedAssemblyNames`（TShockAPI/Terraria/OTAPI/MonoMod/Newtonsoft/HotReload 等）禁止操作；TSWeb 主插件（TShockData）不在名单内，**可以用 `/hr load` 热重载**。
- **另一条链路**：`plugin/tsweb-host/TsWebHost.cs`（cordis 后端插件下发接收端，SSE + DLL 反射热加载，落盘 `ServerPlugins/tsweb/*.dll`），同样直接调用插件 `Initialize()`。
- **对插件开发的关键影响**：
  - 热重载时插件的 `Initialize()` 会被直接调用，但**一次性启动事件不会再次触发**（如 `ServerApi.Hooks.GamePostInitialize` 只在冷启动、世界加载完成时触发一次）。
  - 若插件需要在热重载场景下初始化"世界已加载"的数据（如记录 `Main.worldID`），正确写法是 House 插件同款：
    ```csharp
    ServerApi.Hooks.GamePostInitialize.Register(plugin, OnWorldReady); // 冷启动
    if (!Main.gameMenu) OnWorldReady(null); // 热重载：世界已加载，立即执行
    ```
  - 实测教训（2026-09）：`BossTimeLock`（`plugin/BossTimeLock.cs`）曾只挂 `GamePostInitialize`，热重载后地图 ID 一直"未记录"，原因正是该事件在热重载下不触发。

---

## 工具使用与路径约定（Windows 中文环境，必读）

本机为 Windows + 中文目录名（如 `参考源码`），命令工具有严格使用约束：

### 1. 文件读写类工具（readFile / writeFile / editFile / listFiles / glob / codeSearch）
- **可直接使用中文路径**（UTF-8，工具内部处理编码），不受 cmd 乱码影响。
- 例：`readFile` 路径 `C:/Users/lyt/Documents/GitHub/TsWeb/参考源码/QTRHacker/src/QTRHacker.Patches/Boot.cs` 正常可用。

### 2. bash 工具（实为 Windows cmd.exe）
- **命令行中出现中文路径会乱码**（UTF-8→GBK 转换错误，报"文件名、目录名或卷标语法不正确"）。
- `ls`/`cat` 等类 Unix 命令不存在，用 cmd 原生命令（`dir`、`type`）。
- **规避方法（按优先级）**：
  1. 路径避开中文（英文路径如 `C:\Games\TerraAngelv1.4.5.6`、`backend/`、`plugin/` 直接可用）；
  2. **PowerShell -File 脚本**（最可靠，推荐）：用 writeFile 写一个**全 ASCII** 的 .ps1 脚本到英文路径
     （内容用 `Get-ChildItem -Recurse` 递归查找目标，避免中文字面量），再执行
     `powershell -NoProfile -ExecutionPolicy Bypass -File <无空格绝对路径>`（**不要加引号**）。
  3. 8.3 短路径（`dir /x` 查短名，注意：纯中文目录名通常**没有**短名，仅对英文名有效）。
- **bash 工具的坑（试错实证）**：
  - `%d`（cmd for 变量）会被吞成空 → **`for /d` 遍历中文目录无效**（会"假成功"：命令没执行却 exit 0）；
  - `$_` / `$var` 会被吞（`$_` 变 `.`）→ PowerShell `-Command` 内联复杂脚本不可用；
  - 命令行内嵌引号会被破坏（`\"` 转义问题）→ 带引号的路径/参数不可靠；
  - 简单命令（`dir`、`dotnet --version`、`tasklist`）无参数/全英文时可用。
- 示例——构建 QTRHacker（中文目录 `参考源码` 下的 sln）：先 writeFile 写 `build_qtr.ps1`（全 ASCII）：
  ```powershell
  $msbuild = "C:\PROGRA~1\MICROS~4\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
  $sln = Get-ChildItem -Path "C:\Users\lyt\Documents\GitHub\TsWeb" -Recurse -Filter "QTRHacker.sln" -File -ErrorAction SilentlyContinue | Select-Object -First 1
  & $msbuild $sln.FullName -p:Configuration=Release -p:Platform=x86 -p:PlatformToolset=v143 -v:m -nologo
  exit $LASTEXITCODE
  ```
  再执行（无引号、无空格路径）：`powershell -NoProfile -ExecutionPolicy Bypass -File C:\Users\lyt\Documents\GitHub\TsWeb\build_qtr.ps1`
  （2026-08-24 实证：MSBUILD_EXIT=0，全工程编译通过，零错误。）

### 3. 构建 QTRHacker（本地现成工具，改完必须自测）
- 编译器：VS2022 Community 的 MSBuild → `C:\PROGRA~1\MICROS~4\2022\Community\MSBuild\Current\Bin\MSBuild.exe`
- 工程：`参考源码/QTRHacker/QTRHacker.sln`（SDK 风格 + C++ QHackCLR）；现成命令在 `参考源码/QTRHacker/build.bat`
- 产物：`参考源码/QTRHacker/bin/Release/`（含 `QTRHacker.Patches.dll`、`QTRHacker.Core.dll`、`QHackLib.dll`）
- **改完 `QTRHacker.Core` / `QHackLib` / `QTRHacker.Patches` 必须自己跑构建验证编译通过，禁止只交代码不验证**；
  `dotnet build` 对含 C++ 的 solution 不可靠，必须用 MSBuild 2022。

### 4. 如何正确使用工具

**工具选型优先级（从快到强，按需递进）**：
1. **文件读写类工具**（readFile / writeFile / editFile / listFiles / glob / codeSearch）：项目内文件一律优先，中文路径直接可用，零额外开销
2. **bash 工具（cmd.exe）**：仅限无参数/全英文的简单命令（dir、tasklist、dotnet --version 等）；涉及中文路径、引号、复杂逻辑一律改用 PowerShell -File 脚本（写法见上文第 2 节）
3. **Python（可访问全盘文件）**：本地已安装 Python，可用 `python -c "..."` 或临时脚本访问全盘文件——包括 C:\Games、Steam 目录、中文路径、跨目录批量遍历、大文件/二进制/编码分析等文件读写类工具覆盖不到的场景。此授权仅限「文件访问/分析」类用途，不得用于创建 CI/CD、构建、部署、拷贝等辅助脚本（核心原则不变）；临时分析脚本用后即删

**提问工具（不明确先问，不擅自假设）**：
- 需求目标、删除/修改范围、文件归属、权限边界等任何不明确之处，先调用 askUserQuestions 提问确认后再动手，禁止凭猜测直接改
- 提问时给出带选项的候选方案（2~5 个选项），减少来回沟通
- 例：上文中「删掉 6」在文档无字面编号，即应先提问确认删除目标，而非自行推断

---

## Git 提交规范（重要）

**默认约定：commit 只提交「当前任务」的改动。工作区中任何与本任务无关的历史未提交改动，一律视为其他任务的独立工作，不得一并提交、不得询问、不得打包带走。**

本仓库工作区长期存在多组并行任务改动（例：`plugin/PacketDropManager.cs` 与 `Main.cs` 中的 `PacketDropManager.Initialize/Dispose` 调用、`plugin/tsweb-host/`、`tsweb/`、`plugin-son/invjudge/`、`plugin-son/bossAIModded/`、`plugin/AntiCheat.cs`、`plugin/CrossTransfer.cs` 等）。执行 commit 时：

1. **先 `git status --short` 核对改动清单**，分清哪些文件属于当前任务、哪些是其他任务的历史改动；
2. **只 `git add` 当前任务涉及的文件**（含本次新增的未跟踪文件），其他任务文件一律不动；
3. 若当前任务文件（如 `plugin/Main.cs`）中**混有其他任务的改动**（同一文件内既有本任务代码、又有他任务代码）：
   - 用 `git add -p`（交互式 hunk 选择）**只暂存本任务的 hunk**，跳过其他任务的 hunk；
   - 提交内容必须**自洽**：暂存的代码不得引用未提交的新文件（否则提交后编译断裂）；
   - 其他任务的 hunk 保留在工作区，由后续该任务的提交负责。
4. commit message 用中文，按 `feat: / fix: / refactor:` 前缀 + 简述本任务内容。
5. **提交后验证**：`git status --short` 确认本任务文件已提交、其他任务文件仍在工作区未被误提交。

**禁止**：把其他任务的文件/目录顺手 `git add` 进当前提交；以「保证编译」为由打包提交其他任务的半成品；反复向用户询问「要不要一起提交其他改动」——按上述规则默认隔离，直接执行。
