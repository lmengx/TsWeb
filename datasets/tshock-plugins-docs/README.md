# TShock 插件知识库文档索引

## 文档概述

本知识库包含 **TShockPlugin** 项目的完整文档，该项目是 [UnrealMultiple](https://github.com/UnrealMultiple/TShockPlugin) 维护的 **TShock 中文插件集合仓库**，收录了 130+ 个 Terraria 服务器插件。本文档适合导入到 dify 向量知识库中进行检索和问答。

## 文档列表

### 1. 项目概述文档
**文件名**: `01_项目概述.md`

**内容概览**:
- 仓库定位：TShock 中文插件集合仓库
- 核心特点：130+ 插件、持续更新、开源免费（GPL-3.0）
- 插件依赖体系：LazyAPI 为基础库，linq2db 为 ORM
- 分类总览（15 大分类）
- 插件列表概览表格
- 使用注意事项

**适合检索的问题**:
- TShockPlugin 是什么？
- 有哪些可用的 TShock 插件？
- LazyAPI 是什么？
- 如何安装 TShock 插件？
- 插件有哪些分类？

### 2. 基础库与框架
**文件名**: `02_基础库与框架.md`

**内容概览**:
- LazyAPI：插件基础库，提供自动命令注册、配置管理、多语言、数据库工具
- MiniGamesAPI：豆沙小游戏 API
- Economics.Core：经济插件前置
- DeltaForce.Protocol：三角洲行动通信协议库
- Shared：共享工具库
- SourceGen：源码生成器

**适合检索的问题**:
- LazyAPI 有哪些功能？
- 如何使用 LazyAPI 开发插件？
- LazyAPI 的指令注册机制是怎样的？
- Economics.Core 是什么？
- MiniGamesAPI 能做什么？

### 3. 经济系统插件
**文件名**: `03_经济系统.md`

**内容概览**:
- Economics.Deal：交易插件
- Economics.NPC：自定义怪物奖励
- Economics.RPG：RPG 等级系统
- Economics.Shop：商店插件
- Economics.Skill：技能插件
- Economics.Task：任务插件
- Economics.Regain：物品回收
- Economics.WeaponPlus：武器强化
- Economics.Projectile：自定义弹幕

**适合检索的问题**:
- 如何部署经济系统？
- RPG 插件怎么用？
- 如何设置怪物掉落金币？
- 交易插件支持什么功能？
- 技能插件有哪些技能？

### 4. 领地与建筑插件
**文件名**: `04_领地与建筑.md`

**内容概览**:
- HouseRegion：圈地插件
- SmartRegions：智能区域
- RegionView：显示区域边界
- BridgeBuilder：快速铺桥
- BuildMaster：建筑大师小游戏
- CreateSpawn：出生点建筑生成
- SpawnInfra：基础建设生成
- MazeGenerator：迷宫生成器

**适合检索的问题**:
- 如何圈地保护建筑？
- 如何快速铺桥？
- 智能区域如何工作？
- 如何生成出生点建筑？
- 迷宫生成器怎么用？

### 5. 管理工具插件
**文件名**: `05_管理工具.md`

**内容概览**:
- EssentialsPlus：更多管理指令
- ServerTools：服务器管理工具集
- AutoPluginManager：自动插件管理器
- Ezperm：批量改权限
- PersonalPermission：玩家单独权限
- ListPlugins：查看已安装插件
- HelpPlus：增强 Help 命令
- ConsoleSql：控制台执行 SQL
- ShortCommand：简短指令别名
- SpclPerm：服主特权
- StatusTextManager：状态文本管理

**适合检索的问题**:
- 有哪些管理插件？
- 如何批量修改玩家权限？
- 如何查看已安装的插件？
- 如何给单个玩家设置特殊权限？
- AutoPluginManager 怎么用？

### 6. 传送与移动插件
**文件名**: `06_传送与移动.md`

**内容概览**:
- TeleportRequest：传送请求
- Back：死亡回溯
- MapTp：双击地图传送
- DwTP：定位传送
- BedSet：设置重生点
- TownNPCHomes：NPC 快速回家
- CreateSpawn：传送点管理

**适合检索的问题**:
- 如何请求传送到队友身边？
- 如何回到死亡地点？
- 双击地图传送怎么用？
- 如何设置个人重生点？
- 如何让 NPC 快速回家？

### 7. 物品与背包插件
**文件名**: `07_物品与背包.md`

**内容概览**:
- RestInventory：REST 查询背包
- UnseenInventory：服务器端生成隐藏物品
- ItemBox：离线背包系统
- AutoStoreItems：自动存储
- ItemDecoration：手持物品浮动显示
- ItemPreserver：物品不消耗
- RolesModifying：修改玩家背包
- CGive：离线给予物品
- ChestRestore：无限宝箱
- PerPlayerLoot：玩家独立战利品
- VeinMiner：连锁挖矿

**适合检索的问题**:
- 如何远程查询玩家背包？
- 如何让物品不消耗？
- 连锁挖矿怎么配置？
- 如何设置独立战利品？
- 离线给予物品怎么用？

### 8. 战斗与怪物插件
**文件名**: `08_战斗与怪物.md`

**内容概览**:
- DamageStatistic：Boss 战伤害统计
- DamageRuleLoot：伤害规则掉落
- CriticalHit：击打提示
- DeathDrop：自定义怪物掉落
- DisableMonsLoot：禁用怪物掉落
- BanNpc：阻止怪物生成
- MonsterRegen：怪物进度回血
- ConvertWorld：击败怪物转换世界
- BossLock：Boss 进度锁
- ModifyWeapons：武器修改
- WeaponPlus：武器强化

**适合检索的问题**:
- 如何查看玩家的 Boss 输出？
- 如何自定义怪物掉落？
- 如何禁用指定怪物的生成？
- 如何锁定 Boss 进度？
- 伤害规则掉落如何配置？

### 9. 聊天与社交插件
**文件名**: `09_聊天与社交.md`

**内容概览**:
- RainbowChat：彩色聊天
- DonotFuck：脏话过滤
- CaiCustomEmojiCommand：自定义表情命令
- AIChatPlugin：AI 聊天机器人
- ChattyBridge：跨服聊天桥接
- NoteWall：留言墙
- SignInSign：告示牌登录
- ShowArmors：装备展示
- ItemDecoration：手持物品显示

**适合检索的问题**:
- 如何实现彩色聊天？
- 如何过滤敏感词？
- 跨服聊天如何配置？
- AI 聊天插件支持哪些 API？
- 留言墙怎么用？

### 10. 玩家体验插件
**文件名**: `10_玩家体验.md`

**内容概览**:
- AutoTeam：自动分配队伍
- AutoAirItem：自动垃圾桶
- AutoClear：智能扫地
- AutoFish：自动钓鱼
- PermaBuff：永久 Buff
- LifemaxExtra：更多生命值上限
- Respawn：原地复活
- GhostView：鬼魂观战
- Invincibility：限时无敌
- PlayerSpeed：玩家速度控制
- GoodNight：宵禁系统
- RealTime：时间同步
- TimeRate：时间加速
- RebirthCoin：复活币
- ProgressBag：进度礼包
- OnlineGiftPackage：在线礼包

**适合检索的问题**:
- 如何自动分配队伍？
- 如何设置永久 Buff？
- 如何实现自动钓鱼？
- 原地复活怎么配置？
- 如何增加生命值上限？

### 11. 安全与反作弊插件
**文件名**: `11_安全与反作弊.md`

**内容概览**:
- Chameleon：进服前登录系统
- Noagent：禁止代理 IP
- SessionSentinel：处理不活跃玩家
- SurfaceBlock：禁止地表弹幕
- PacketsStop：数据包拦截
- ProgressRestrict：超进度检测
- BetterWhitelist：白名单系统
- BanNpc：怪物限制
- DTEntryBlock：阻止进入地牢/神庙

**适合检索的问题**:
- 如何防止代理 IP 进入？
- 如何设置登录系统？
- 如何拦截数据包？
- 超进度检测怎么用？
- 如何阻止玩家进入特定区域？

### 12. 娱乐与小游戏插件
**文件名**: `12_娱乐与小游戏.md`

**内容概览**:
- SurvivalCrisis：类 Among Us 小游戏
- PvPer：决斗系统
- Challenger：挑战者模式（高难度）
- BadApplePlayer：BadApple 播放器
- PlayerRandomSwapper：玩家位置随机交换
- ReverseWorld：世界反转
- Sandstorm：沙尘暴控制
- GolfRewards：高尔夫奖励
- MusicPlayer：音乐播放器
- QRCoder：二维码生成
- RecipesBrowser：合成表查询
- CaiRewardChest：奖励箱
- RandomBroadcast：随机广播
- VotePlus：投票系统

**适合检索的问题**:
- 有哪些小游戏插件？
- 如何开启决斗系统？
- 挑战者模式怎么玩？
- 如何播放音乐？
- 如何查看合成表？

### 13. 自动化与工具插件
**文件名**: `13_自动化与工具.md`

**内容概览**:
- AutoReset：完全自动重置
- ProgressControls：计划书自动化控制
- TimerKeeper：计时器状态保存
- SwitchCommands：区域执行指令
- AutoBroadcast：自动广播
- RandomBroadcast：随机广播
- DataSync：进度同步
- ReFishTask：刷新渔夫任务
- JourneyUnlock：旅途模式解锁
- WikiLangPackLoader：加载 Wiki 语言包
- TransferPatch：翻译补丁
- ProxyProtocolSocket：代理协议支持
- DumpTerrariaID：输出 ID 工具
- GenerateMap：地图生成
- DumpPluginsList：导出插件列表
- Platform：玩家设备判断

**适合检索的问题**:
- 如何设置自动重置？
- 如何实现定时广播？
- 如何同步多服务器进度？
- 如何生成服务器地图？
- 如何判断玩家设备类型？

### 14. 第三方集成插件
**文件名**: `14_第三方集成.md`

**内容概览**:
- CaiBotLite：CaiBot 官方机器人适配
- Lagrange.XocMat.Adapter：Lagrange 适配插件
- AIChatPlugin：AI 聊天集成
- CaiPacketDebug：数据包调试工具

**适合检索的问题**:
- 如何接入 CaiBot 机器人？
- 如何配置 QQ 机器人适配？
- AI 聊天插件支持哪些模型？
- 如何调试数据包？

### 15. 特殊玩法插件
**文件名**: `15_特殊玩法.md`

**内容概览**:
- DeltaForce.Core：三角洲行动·特勤处
- DeltaForce.Game：三角洲行动·游戏端
- DeltaForce.Protocol：三角洲行动通信协议
- Challenger：挑战者模式
- BuildMaster：建筑大师模式

**适合检索的问题**:
- 三角洲行动是什么玩法？
- 挑战者模式有哪些特性？
- 建筑大师模式怎么玩？

### 16. 插件开发指南
**文件名**: `16_插件开发指南.md`

**内容概览**:
- 插件开发模式
- LazyAPI 特性详解（命令注册、配置管理、数据库、多语言）
- 依赖体系说明
- 开发最佳实践

**适合检索的问题**:
- 如何开发一个 TShock 插件？
- 如何使用 LazyAPI 开发插件？
- 插件的依赖怎么管理？
- 如何实现多语言支持？

## 使用指南

### 导入到 dify

1. 登录 dify 平台
2. 创建新的知识库，名称如 "TShock Plugin Documentation"
3. 上传所有 `*.md` 文档文件
4. 配置向量化和检索参数
5. 创建应用并关联知识库

### 最佳实践

#### 文档分割策略
- 建议按一级标题（#）分段
- 每个文档独立处理
- 保留文档的标题层级结构
- 最大分段长度：1000-1500 tokens

#### 检索优化
- 使用语义搜索模式
- 配置 top-k 值在 3-5 之间
- 启用重排序功能提高精度
- 针对插件名称和功能关键词做精确匹配

#### 提示词优化

```
你是一位 TShock 插件专家助手，基于提供的知识库文档回答用户关于 TShock 插件的问题。

请遵循以下原则：
1. 优先使用知识库中的信息回答问题
2. 提供具体的插件名称、命令和配置示例
3. 引用相关的文档章节以供查阅
4. 说明插件之间的依赖关系
5. 如果知识库中没有相关信息，明确告知用户
6. 推荐使用 AutoPluginManager (APM) 安装插件
```

## 文档更新

### 更新策略
1. 同步 TShockPlugin 仓库的最新变更
2. 跟进新增插件的文档
3. 补充常见使用问题
4. 优化检索效果

### 贡献指南
欢迎通过以下方式改进文档：
1. 提交 Issue 报告问题
2. 补充实际使用案例
3. 分享配置经验

## 相关资源

- **GitHub**: https://github.com/UnrealMultiple/TShockPlugin
- **插件文档**: http://docs.terraria.ink/zh/
- **APM 镜像**: http://api.terraria.ink:11434/plugin/get_all_plugins
- **Crowdin 翻译**: https://zh.crowdin.com/project/tshock-chinese-plugin

## 版本信息

- **文档版本**: 1.0
- **TShock 版本**: 5.2+
- **开发框架**: .NET 6
- **更新日期**: 2025-07-15

## 许可证

本文档集遵循 GPL-3.0 许可证，与 TShockPlugin 项目保持一致。
