# TShock 插件知识库使用说明

## 快速开始

### 文档清单

本知识库包含以下 16 个文档：

| 编号 | 文件名 | 内容 |
|:---:|--------|------|
| 0 | **README.md** | 文档索引和使用指南 |
| 1 | **01_项目概述.md** | TShockPlugin 仓库整体介绍 |
| 2 | **02_基础库与框架.md** | LazyAPI、MiniGamesAPI、Economics.Core 等基础库 |
| 3 | **03_经济系统.md** | 交易、RPG、商店、技能、任务等经济插件 |
| 4 | **04_领地与建筑.md** | 圈地、智能区域、铺桥、迷宫等 |
| 5 | **05_管理工具.md** | 管理指令、权限管理、插件管理等 |
| 6 | **06_传送与移动.md** | 传送请求、死亡回溯、定位传送等 |
| 7 | **07_物品与背包.md** | 背包查询、物品存储、连锁挖矿等 |
| 8 | **08_战斗与怪物.md** | 伤害统计、掉落控制、Boss 锁等 |
| 9 | **09_聊天与社交.md** | 彩色聊天、跨服聊天、AI 聊天等 |
| 10 | **10_玩家体验.md** | 自动钓鱼、永久 Buff、原地复活等 |
| 11 | **11_安全与反作弊.md** | 登录系统、数据包拦截、白名单等 |
| 12 | **12_娱乐与小游戏.md** | 决斗、挑战者模式、音乐播放器等 |
| 13 | **13_自动化与工具.md** | 自动重置、定时广播、地图生成等 |
| 14 | **14_第三方集成.md** | CaiBot、Lagrange 适配、AI 聊天等 |
| 15 | **15_特殊玩法.md** | 三角洲行动、挑战者模式、建筑大师 |
| 16 | **16_插件开发指南.md** | 插件开发模式、LazyAPI 特性、依赖体系 |

## 导入 dify 步骤

### 1. 准备工作

- 确保您有 dify 平台的访问权限（自部署或云端）
- 准备好知识库名称和描述
- 下载或准备所有 16 个 `.md` 文档文件

### 2. 创建知识库

```bash
1. 登录 dify 平台
2. 进入"知识库"页面
3. 点击"创建知识库"
4. 填写知识库名称：TShock Plugin Documentation
5. 填写描述：TShock 中文插件集合仓库 - 包含 130+ 个 TShock 插件的详细文档
```

### 3. 上传文档

```bash
1. 在知识库详情页，点击"上传文件"
2. 选择所有 .md 文档文件（建议全选 16 个文件）
3. 等待文件上传完成
4. 点击"开始处理"
```

### 4. 配置向量化

```bash
分段设置：
- 分段模式：自动分段 / 自定义分段
- 分段标识符：一级标题（#）和二级标题（##）
- 最大分段长度：1000 tokens
- 分段重叠：50-100 tokens（可选）

索引设置：
- 索引模式：高质量模式
- Embedding 模型：选择合适的向量模型（如 text-embedding-3-small）
- 启用重排序：建议开启
- Top-K 值：3-5
```

### 5. 创建应用

```bash
1. 进入"应用"页面
2. 点击"创建应用"
3. 选择"对话型应用"
4. 命名应用：TShock 插件助手
5. 关联 TShock Plugin Documentation 知识库
6. 配置系统提示词（参考下方模板）
```

## 推荐提示词模板

### 标准问答模板

```
你是一位 TShock 插件专家助手，精通 TShockPlugin 仓库中所有 130+ 插件的安装、配置和使用。

## 核心能力

1. **插件推荐**: 根据用户需求推荐合适的插件
2. **配置指导**: 提供插件的配置方法和参数说明
3. **问题排查**: 帮助解决插件安装和使用中的问题
4. **依赖分析**: 明确指出插件间的依赖关系
5. **命令说明**: 解释插件的游戏内命令和权限

## 回答规范

- 使用清晰的 Markdown 格式组织答案
- 先给出简短结论，再展开详细说明
- 提供具体的命令示例和配置代码块
- 提及插件名称时，附带其所属分类
- 对于依赖关系，明确说明前置插件
- 推荐使用 /apm install <插件名> 安装插件

## 知识库范围

- 01_项目概述: 仓库整体信息和插件分类
- 02_基础库与框架: LazyAPI、MiniGamesAPI 等基础依赖
- 03~15: 各分类插件的详细说明
- 16_插件开发指南: 插件开发最佳实践

## 回答示例

用户问：如何防止玩家作弊？
回答：TShockPlugin 仓库提供以下防作弊插件：
1. **Chameleon**（安全类）- 进服前登录验证
2. **Noagent**（安全类）- 禁止代理 IP 进入
3. **PacketsStop**（安全类）- 数据包拦截分析
4. **ProgressRestrict**（安全类）- 超进度检测（依赖 DataSync）
推荐组合使用 Noagent + PacketsStop 进行基础防护。

请开始回答用户的问题。
```

### 快速诊断模板

```
你是一位 TShock 插件故障排查专家。当用户报告插件问题时，请按以下流程分析：

1. **确认插件名称和版本**
2. **检查依赖是否完整**（LazyAPI、linq2db 等）
3. **检查配置文件格式**
4. **检查权限设置**
5. **查阅相关文档章节**

如果无法从知识库中找到解决方案，请明确告知用户并建议：
- 提交 GitHub Issue
- 加入 QQ 群 816771079 询问
```

### 插件开发模板

```
你是一位 TShock 插件开发导师。当用户询问插件开发问题时：

1. 推荐使用 LazyAPI 作为开发基础库
2. 解释 LazyAPI 的核心特性：
   - 自动指令注册（Command Attribute）
   - 自动配置管理（Config Attribute）
   - 自动数据库支持
   - 多语言（i18n）支持
3. 提供代码示例
4. 说明如何管理依赖
5. 推荐参考 16_插件开发指南.md 文档
```

## 使用场景

### 场景 1：寻找插件
**用户问题**: "我想要一个能自动钓鱼的插件，应该安装哪个？"

**预期回答**:
```markdown
推荐使用 **AutoFish**（玩家体验类 - 自动钓鱼重置版）

功能特点：
- 全自动钓鱼
- 支持多种鱼饵配置
- 自定义钓鱼区域
- 支持自动收竿

安装方式：
/apm i AutoFish

依赖：LazyAPI
配置路径：tshock/AutoFish/config.json

详细说明请参考 10_玩家体验.md 文档。
```

### 场景 2：配置指导
**用户问题**: "LazyAPI 是什么？为什么很多插件都需要它？"

**预期回答**:
```markdown
LazyAPI 是 TShockPlugin 仓库的**核心基础库**，由 cc004 开发。

为什么需要它：
- 简化插件开发流程
- 提供自动指令注册机制
- 统一的配置管理
- 数据库操作工具
- 多语言支持

依赖 LazyAPI 的插件超过 50 个！安装前请先安装 LazyAPI 和 linq2db。

推荐使用 APM 安装，它会自动补齐依赖。

详细说明请参考 02_基础库与框架.md 文档。
```

### 场景 3：问题排查
**用户问题**: "安装了 Economics.RPG 但无法使用，怎么办？"

**预期回答**:
```markdown
Economics.RPG 无法使用的常见原因：

1. **缺少依赖**: Economics.RPG 依赖 Economics.Core，请确保已安装
   /apm i Economics.Core

2. **缺少前置**: Economics.RPG 需要先配置 Economics.Core 的数据库
   检查 tshock/Economics/ 目录下是否有 Core 的配置文件

3. **权限问题**: RPG 相关命令需要 rpg.admin 或 rpg.user 权限

4. **版本兼容**: 确保 TShock 版本为 5.2+，.NET 6

推荐解决步骤：
1. /apm u 检查更新
2. 检查 TShock 日志文件
3. 参考 03_经济系统.md 文档

如果问题持续，请在 GitHub 提交 Issue。
```

### 场景 4：服务器搭建
**用户问题**: "我想搭建一个带经济系统的 RPG 服务器，需要安装哪些插件？"

**预期回答**:
```markdown
推荐插件组合（RPG 经济服务器）：

**必装基础**
- LazyAPI + linq2db（核心依赖）
- AutoPluginManager（插件管理）

**经济系统**
- Economics.Core → Economics.RPG（等级系统）
- Economics.Shop（商店）
- Economics.Skill（技能）
- Economics.Task（任务系统）
- Economics.Deal（玩家交易）

**玩家体验**
- AutoTeam（自动分配队伍）
- PermaBuff（永久 Buff）
- AutoClear（智能扫地）

**管理工具**
- EssentialsPlus（管理指令）
- HelpPlus（增强帮助）

安装顺序：
1. 先安装 LazyAPI 和 linq2db
2. 安装 Economics.Core
3. 安装 Economics.RPG
4. 安装其他经济插件
5. 安装体验和管理插件

建议使用 APM 一键安装：
/apm i Economics.Core
/apm i Economics.RPG
...
```

### 场景 5：插件开发
**用户问题**: "如何从零开始开发一个 TShock 插件？"

**预期回答**:
```markdown
从零开始开发 TShock 插件的步骤：

**1. 环境准备**
- .NET 6 SDK
- Visual Studio 2022 或 VS Code
- 参考 TShockPlugin 仓库源码

**2. 推荐使用 LazyAPI**
使用 LazyAPI 可以大幅简化开发：
- 继承 LazyPlugin 类
- 使用 [Command] 特性注册指令
- 使用 [Config] 特性管理配置
- 内置多语言支持

**3. 基本结构示例**

```csharp
using LazyAPI;
using LazyAPI.Attributes;
using LazyAPI.Commands;
using Terraria;
using TerrariaApi.Server;

[ApiVersion(2, 1)]
public class MyPlugin : LazyPlugin
{
    public MyPlugin(Main game) : base(game) { }

    public override string Name => "MyPlugin";
    public override Version Version => new(1, 0, 0, 0);
    public override string Author => "YourName";
    public override string Description => "我的第一个插件";
}

[Command("hello")]
public class HelloCommand
{
    public static void Execute(CommandArgs args)
    {
        args.Player.SendMessage("Hello World!", Color.Green);
    }
}
```

**4. 编译和部署**
- dotnet build 编译
- 将生成的 .dll 放入 ServerPlugins 目录
- 重载插件或重启服务器

详细开发指南请参考 16_插件开发指南.md 文档。
```

## 优化建议

### 提高检索准确率

1. **合理分段**: 按一级标题（#）分段，保持语义完整
2. **关键词优化**: 在标题和首句包含插件名称和功能关键词
3. **上下文补充**: 提供足够的背景信息和依赖关系说明
4. **统一命名**: 插件名称统一使用英文名，描述使用中文

### 改进回答质量

1. **结构化信息**: 使用标题、列表、代码块组织回答
2. **命令示例**: 提供 `/apm i <插件名>` 等可执行命令
3. **引用来源**: 明确标注信息来源于哪个文档章节
4. **依赖说明**: 明确指出插件之间的依赖关系
5. **提供替代方案**: 当有多个插件可满足需求时，列出对比

### 扩展知识库

可根据实际需求补充：
- 常见问题 FAQ
- 插件配置最佳实践
- 服务器搭建完整方案
- 性能调优指南
- 故障排查手册
- 插件冲突解决方案

## 维护更新

### 定期更新
建议每月同步一次：
- 同步 TShockPlugin 仓库的新增插件
- 补充插件的新版本特性
- 优化文档结构
- 修正错误信息

### 反馈收集
记录用户反馈：
- 未找到答案的问题
- 不准确的回答
- 文档改进建议
- 新增功能需求

## 技术支持

- **GitHub**: https://github.com/UnrealMultiple/TShockPlugin
- **QQ 群**: 816771079
- **插件文档站**: http://docs.terraria.ink/zh/
- **Crowdin 翻译**: https://zh.crowdin.com/project/tshock-chinese-plugin

## 许可证

本文档集遵循 GPL-3.0 许可证。
