# TsWeb 项目文档

TSWeb 是面向 **Terraria（泰拉瑞亚）** 服务器的综合管理与工具平台，由两个大模块组成：

| 模块 | 定位 |
|------|------|
| **TSWeb 模块** | 基于 TShock 插件的网页管理工具，提供最强大的面板管理体验，内置白名单、房屋插件、QQ 机器人、跨服传送、自动备份、表情指令、Boss 管理、物品/弹幕反作弊、风控宵禁 |
| **Mod 移植模块** | 将 tModLoader 生态的 mod 以注入 DLL 形式原版移植到 Terraria 1.4.5.8 客户端，提供合成表（RecipeBrowser）、Hero 等工具 |

AI 开发规范见 [AGENTS.md](AGENTS.md)。

---

## 一、TSWeb 模块

三层架构：**TShock 插件（C#）** 负责游戏内逻辑与 REST 接口，**Node.js 后端** 负责多服聚合与 Web 服务，**Vue 3 前端** 提供管理面板。

```
plugin/    TShock 插件（TSWeb.dll，net9.0）—— 游戏内逻辑 + /data/* REST 路由
backend/   Node.js + Express —— 多服注册表、JWT 鉴权、webhook、SSE、QQ 机器人
frontend/  Vue 3 + Vite —— 管理面板（console 视图 + 设置页）
```

### 1.1 功能模块总览

| 功能 | 插件模块 | 后端/前端 | 说明 |
|------|----------|-----------|------|
| **进服策略** | `RegisterAndAccess`、`UnverifiedManager` | `unverifiedRoutes` / `UnverifiedDetail.vue` | 动态配置，可随时切换白名单/自动注册 |
| **物品/弹幕反作弊** | `AntiCheat` | `ItemDetection` / `ProjDetection`  | 违禁物品/弹幕检测、全服扫描 |
| **反恶性bug** | `ParticleGuard` / `BugFixes`  || 闪电防护、反恶性 bug（登录/宝箱/召唤物限制） |
| **房屋插件** | `HouseCore` / `HouseApi`（原 plugin-son/House 并入） | `houseApi.js` / `HouseManagementView.vue` | 圈地建房、屋主自定义传送点以及自主权限，一键导入导出 |
| **QQ 机器人** | `QQ.cs` / `AccountSync` | `botRoutes` / `qqAccountService` / `QQConfigView.vue` | QQ 绑定账号、查询玩家数据、绑定流程、多服时长聚合 |
| **跨服传送** | `CrossTransfer`（Unused15 自定义包通道 + Auth 密钥 + 前置握手） | `crossTransferRoutes` / `CrossTransferView.vue` | 多服间玩家传送，前端可配置、连通性探测 |
| **自动备份** | `AutoBackup` | `BackupSettingsView.vue` | 地图 + sqlite 压缩包备份，可选 webhook 推送后端 |
| **Boss 管理** | `BossLimit` / `BossConfigManager` / `BossProgress` | `BossLimitSettingsView.vue` / `ProgressView.vue` | Boss 进度查询、Boss 限制（召唤限制）、配置管理 |
| **风控宵禁** | `RiskControl` / `Curfew` | `RiskControlSettingsView.vue` / `CurfewSettingsView.vue` | 实时风控（进服/发言/命令拦截、一键踢出）、宵禁（条目化排期 + 豁免组） |
| **自动任务** | `TaskScheduler` | `tasksApi.js` / `TasksView.vue` | 条件触发（在线人数/Boss 击败/指定玩家）+ 顺序/并发执行 + 执行日志 |
| 表情指令 | `EmoteCommandManager` | `EmoteCommandView.vue` | 玩家发表情时执行指定指令 |
| 玩家管理 | `GetPlayerInv` / `QueryUsers` / `PlayerStats` / `QueryPwd` / `ClearCharacter` | `PlayersView.vue` / `UserDetailView.vue` / `InventoryViewer.vue` | 背包查看/编辑、玩家查询、统计、重复 IP、查密码、清角色 |
| 用户组管理 | `GroupOP` | `GroupsView.vue` / `GroupManager.vue` | 组列表/详情/创建/删除/更新/权限增删 |
| 在线统计 | `OnlineData` | `onlineRoutes` / `OnlineStatsView.vue` | 小时在线、排行榜、玩家日历、全服时长聚合 |
| 文件管理 | `FileManager` | `fileRoutes` / `FileManagerView.vue` | 服务器文件读写/列目录/目录树/上传删除 |
| 服务器信息面板 | `StatusPanel` | `StatusPanelSettingsView.vue` | 客户端固定屏幕位置持久文本框，前端可配置 |
| 世界修改器 | `WorldModify` | `worldModifyRoutes` / `worldModifyApi.js` | 修改种子特性，修改boss击败状态 |
| 个人独立权限 | `PersonalPermissionManager` | `permissionRoutes` / `PermissionView.vue` | 快速/批量签发权限，到期自动失效，多维排序查看 |
| 跨服聊天 | `CrossChat` | `crossChatService` | 本地聊天取消名字转义 + 跨服转发接收 |
| 权限提升 | `PromotionManager` | `PromotionConfigView.vue` | 玩家自助权限提升（按配置） |
| 审计日志 | — | `auditRoutes` / `auditLogger` / `AuditView.vue` | 后端操作审计记录 |
| 投票系统 | — | `voteRoutes` / `VoteView.vue` | 服务器投票功能，对接qq |
| 日志/控制台 | `SSELogger` / `WebRestServer` | `ConsoleTerminal.vue` / `logBroadcast` | 现代 REST 监听（替换旧 HttpServer）、实时日志流、控制台命令 |

### 1.2 技术栈

| 层 | 技术 |
|----|------|
| 插件层 | C# / .NET 9；TShockAPI 6.x、OTAPI、TerrariaServer API、HttpServer、ModFramework；Newtonsoft.Json 13、Microsoft.Data.Sqlite 9、MonoMod.RuntimeDetour |
| 后端 | Node.js + Express 5（ESM）；jsonwebtoken（JWT）、bcrypt（密码哈希）、cors、iconv-lite（GBK 解码）、node-forge；多服注册表 + x-server-id 请求级上下文（AsyncLocalStorage）；SSE 长连接 / 日志轮询 / webhook（HMAC 签名）；pnpm 包管理 |
| 前端 | Vue 3 + Vite 6；vue-router 5；marked（Markdown 渲染）；无第三方 UI 框架，自研组件（AppSelect、Loading、Modal 等） |

---

## 二、Mod 移植模块

位于 `ModTransplant/`（另有合并仓库 `mods-merged/`）。目标为 **Terraria 1.4.5.8 原版客户端**（非 tModLoader），以注入 DLL + 启动批处理方式运行，不改客户端任何文件，卸载即删除 DLL、零残留。核心项目：

| 项目 | 说明 |
|------|------|
| **RecipeBrowser** | 合成表浏览器——tModLoader Recipe Browser 的原版移植：配方浏览/搜索、物品图鉴、生物图鉴、掉落查询、收藏配方追踪、联动游戏内合成栏（Insert 打开、鼠标中键查询） |
| **Hero** | HEROsMod 仿制的客户端工具：物品浏览器、无限延申、自由/锁定镜头、清除生物、刷怪、Buff、上帝模式、全图点亮、地图右键传送、像素画等 |
| **ModLoader** | 注入式 Mod 加载器——为原版客户端加载 mod（TsWebModApi 契约） |
| WebView2PoC | WebView2 注入技术验证项目 |

### 2.1 技术栈

| 项 | 技术 |
|----|------|
| 语言/框架 | C# / .NET Framework 4.8（net48） |
| 运行时补丁 | Harmony（0Harmony） |
| 依赖内嵌 | Costura.Fody（单文件内嵌依赖） |
| 注入方式 | AppDomainManager 注入 + 启动批处理（`*_start.bat`），零残留卸载 |
| 构建 | MSBuild 2022，各子项目独立脚本（`build_hero.ps1` / `build_rb.ps1` / `build_modloader.ps1`） |

---

## 三、技术栈总览

| 领域 | 技术 |
|------|------|
| 服务器插件 | C# / .NET 9 / TShockAPI / OTAPI |
| 客户端注入 | C# / .NET Framework 4.8 / Harmony / Costura.Fody |
| 后端 | Node.js / Express 5 / JWT / bcrypt / SSE |
| 前端 | Vue 3 / Vite 6 / vue-router / marked |
| 数据库 | SQLite（Microsoft.Data.Sqlite，插件侧）+ 后端 JSON 文件存储 |
| 包管理 | pnpm（backend/frontend） |
| 构建 | MSBuild 2022（插件与客户端注入项目） |

---

## 四、目录结构

```
plugin/             TSWeb 插件源码（TShock 插件，约 60 个模块，入口 plugin/Main.cs）
backend/            Node.js 后端（routes/ 路由、services/ 服务、controllers/、middlewares/）
frontend/           Vue 3 前端（views/ 视图、components/ 组件、utils/ 工具）
plugin-son/         独立/并入前的子插件（House、shopui、ConnectionGuard、PacketCatch 等）
ModTransplant/      客户端注入工具（Hero、RecipeBrowser、ModLoader、QTRHacker、整合）
mods-merged/        客户端注入工具合并仓库（git subtree 形式，含完整历史）
参考源码/           TShock 框架源码、插件参考库、开源插件、反编译资料（本地查阅，禁止联网搜索源码）
scripts/            常用脚本与文档（scripts/文档/ 含设计文档、审查报告、规范）
tmp/                临时与归档区（gitignore，禁止进 git）
fixes/              Challenger 等修复工程
terraangel-plugins/ 客户端插件（TAPlugin）相关工程
```
