# Chireiden.TShock.Omni — 恋恋工具箱 完整技术文档

> **"缺什么权限？为什么用不了？—— 用 `/whynot` 看一眼。"**

---

## 1. 插件概述

| 属性 | 值 |
|------|-----|
| **插件名称** | Chireiden.TShock.Omni（通称"恋恋工具箱"、"yaaiomni"、"Omni"） |
| **作者** | SGKoishi（Chireiden 组织） |
| **仓库地址** | [https://github.com/sgkoishi/yaaiomni](https://github.com/sgkoishi/yaaiomni) |
| **开源协议** | MIT License |
| **目标框架** | .NET 6.0 |
| **TShock 版本** | 5.2.2（兼容 TShock API v2.1） |
| **NuGet 包名** | `Chireiden.TShock.Omni`（核心包） + `Chireiden.TShock.Omni.Misc`（可选扩展包） |
| **核心 DLL** | `Chireiden.TShock.Omni.dll` |
| **扩展 DLL** | `Chireiden.TShock.Omni.Misc.dll` |
| **加载顺序** | `Order = -1_000_000`（在 TShock 及其他所有插件之前加载） |
| **支持架构** | x64 / x86（ARM64 部分功能可能不可用） |

### 设计理念

Omni 是一个"多功能杂项插件"，定位是 **修补 TShock 的已知问题、增强服务器管理能力、提供调试工具**。它不是一个单一功能插件，而是一整套工具集，包含 20+ 个独立功能模块。其核心理念：

- **最小侵入**：默认配置只修复问题，不改变行为
- **渐进暴露**：通过 `Optional<T>` 隐藏高级选项，需要时通过 `/genconfig` 暴露
- **源码级兼容**：大量使用 Detour/ILHook 而非直接修改 TShock 源码，与更新兼容

---

## 2. 功能总览

### 2.1 权限诊断 — `/whynot`（最核心功能）

#### 原理

`/whynot` 是 Omni 的**招牌功能**，用于解决 TShock 服务器中最常见的问题："我缺什么权限？"

它通过 **Detour `TSPlayer.HasPermission` 方法**，劫持所有权限查询请求，记录每一次调用历史。当管理员输入 `/whynot` 时，插件展示该玩家最近的所有权限查询记录，包括：

- 权限名称
- 查询时间
- 查询结果（允许/拒绝）
- 可选的调用堆栈（StackTrace，需要额外权限）

这使得管理员能够**精确定位**玩家操作被拒绝的原因——不再需要猜测。

#### 源码实现

```csharp
// PermissionCheck.cs
private bool Detour_HasPermission(Func<TSPlayer, string, bool> orig, TSPlayer player, string permission)
{
    var result = orig(player, permission);
    var strgy = this.config.Permission.Value.Log.Value;
    if (strgy.Enabled)
    {
        var history = this[player]!.PermissionHistory;
        // ... 记录到 RingBuffer ...
        var entry = new AttachedData.PermissionCheckHistory(permission, now, result, stackTrace);
        history.Add(entry);
    }
    return result;
}
```

权限历史存储在 `Ring<PermissionCheckHistory>` 中（循环缓冲区），默认记录最近 50 条，避免内存溢出。

#### 用法

```
/whynot            — 显示所有权限查询记录（绿色=通过，红色=拒绝）
/whynot -f         — 只显示被拒绝的记录（Find Failed）
/whynot -t         — 只显示通过的记录（Find True）
/whynot -v         — 显示详细调用堆栈（需要 chireiden.omni.whynot.detailed 权限）
```

#### 权限

| 权限节点 | 说明 |
|----------|------|
| `chireiden.omni.whynot` | 使用 `/whynot` 命令 |
| `chireiden.omni.whynot.detailed` | 查看详细调用堆栈（`-v` 参数） |

---

### 2.2 服务器管理命令

#### `/setlang` — 设置服务器语言

支持单独设置 TShock 语言和游戏语言，采用 `-t` 和 `-g` 参数区分。

```
/setlang                     — 查看当前语言设置
/setlang zh-CN               — 同时设置游戏语言和 TShock 语言
/setlang zh-CN -t            — 只设置 TShock 语言
/setlang zh-CN -g            — 只设置游戏语言
/setlang -g                  — 重置游戏语言为系统默认
```

底层实现使用了 `CultureInfo` 距离算法（`Utils.Distance`），能够智能匹配最近的语言文化。

权限：`chireiden.omni.setlang`

#### `/maxplayers` — 动态设置最大玩家数

```
/maxplayers        — 查看当前最大玩家数
/maxplayers 100    — 设置为 100
```

权限：`chireiden.omni.admin.maxplayers`

#### `/runas` — 以其他玩家身份执行命令

```
/runas PlayerName /command arg1 arg2     — 以指定玩家身份执行
/runas *all* /broadcast hello            — 使用通配符对所有玩家执行
/runas -f PlayerName /command            — 跳过权限检查执行（force）
```

权限：`chireiden.omni.admin.sudo`

注意：`-f` 参数会使用 `RunWithoutPermissionChecks` 机制，通过堆栈回溯跳过权限检查，确保命令以目标玩家的身份执行但不受其权限限制。

---

### 2.3 定时任务系统

Omni 提供了基于游戏更新滴答（Update Tick）的定时任务系统，包含四个命令：

#### 命令一览

| 命令 | 功能 | 权限 |
|------|------|------|
| `/settimeout <cmd> <tick>` | 指定延迟后执行一次命令 | `chireiden.omni.timeout` |
| `/setinterval <cmd> <tick>` | 每隔 N 个 tick 重复执行 | `chireiden.omni.interval` |
| `/clearinterval <id>` | 按 ID 删除定时任务 | `chireiden.omni.cleartimeout` |
| `/showdelay` | 列出当前所有定时任务 | `chireiden.omni.showtimeout` |

#### 技术细节

TShock 默认以 60 ticks/秒 的速度运行，因此：

- `/settimeout /broadcast Hello 60` — 每秒广播一次 Hello
- `/setinterval /save 3600` — 每分钟自动保存（60 tick × 60 = 3600）
- `/setinterval /cmd 18000` — 每 5 分钟执行一次

定时任务的实现依赖 `TAHook_Update` 钩子，每次游戏更新时检查所有玩家的 `DelayCommands` 列表：

```csharp
private void TAHook_Update(EventArgs _)
{
    this.UpdateCounter++;
    foreach (var player in Utils.ActivePlayers)
        this.ProcessDelayCommand(this[player]!);
    this.ProcessDelayCommand(this[TSPlayer.Server]!);
}
```

---

### 2.4 角色管理

#### `/resetcharacter` — 重置角色数据

重置 SSC（服务器端角色）数据到初始状态，适合用于：

- 玩家角色数据损坏时重置
- 新玩家引导结束后重置为正式配置

```
/resetcharacter              — 重置自己（需要确认）
/resetcharacter -f           — 强制重置自己
/resetcharacter ts玩家名 -f  — 重置其他玩家（需要管理权限）
/resetcharacter * -f         — 重置在线所有玩家（需要最高权限）
/resetcharacter -s           — 重置时保留外观设置（皮肤、发型等）
```

权限层级：
| 权限 | 说明 |
|------|------|
| `chireiden.omni.resetcharacter` | 重置自己 |
| `chireiden.omni.admin.resetcharacter` | 重置其他玩家 |
| `chireiden.omni.admin.resetcharacter.all` | 重置所有玩家 |

#### `/exportcharacter` — 导出角色为 .plr 文件

将 SSC 中的角色数据导出为泰拉瑞亚原版的 `.plr` 文件，存储在 `tshock/exported/` 目录下。可用于：

- 备份角色数据
- 迁移角色到其他服务器
- 用泰拉瑞亚客户端离线查看角色

权限：`chireiden.omni.admin.exportcharacter`

---

### 2.5 聊天防刷

Omni 内置聊天速率限制机制，支持多层限制器（Limiter），默认配置：

| 层级 | 限制 | 效果 |
|------|------|------|
| 第一层 | 1.6 速率 / 5 秒窗口 | 3 条消息 / 5 秒 |
| 第二层 | 4.0 速率 / 20 秒窗口 | 5 条消息 / 20 秒 |

底层实现使用 **Token Bucket（令牌桶）** 算法：

```csharp
public bool Allowed
{
    get
    {
        var time = new TimeSpan(DateTime.Now.Ticks).TotalSeconds;
        var tat = Math.Max(this.Counter, time) + this.Config.RateLimit;
        if (tat > time + this.Config.Maximum) return false;
        this.Counter = tat;
        return true;
    }
}
```

触发限制后，相关消息包（`NetTextModule`）会被静默丢弃（`CancelPacket`），不广播给其他玩家。

---

### 2.6 通配符系统

Omni 扩展了 TShock 的玩家匹配系统，支持自定义通配符格式。

#### 三级通配符

| 通配符类型 | 默认值 | 说明 | 使用示例 |
|-----------|--------|------|---------|
| 自身通配符 | `*self*` | 匹配命令执行者自己 | `/heal *self*` |
| 全体通配符 | `*all*` | 匹配所有在线玩家 | `/g zenith *all*` |
| 服务端通配符 | `*server*`, `*console*` | 匹配服务端控制台 | `/whisper *server*` |

#### 实现原理

通配符通过两个钩子实现：

1. **`Detour_Wildcard_GetPlayers`** — 劫持 `TSPlayer.FindByNameOrID`，使 `*server*` 等模式匹配到 `TSPlayer.Server`
2. **`TSHook_Wildcard_PlayerCommand`** — 在命令执行前扫描参数，遇到 `*all*` 时为每个在线玩家分别执行命令

```csharp
if (this.config.PlayerWildcardFormat.Value.Contains(arg))
{
    args.Handled = true;
    foreach (var player in Utils.ActivePlayers)
    {
        var newargs = args.Parameters.ToList();
        newargs[i] = player.Name;
        TShockAPI.Commands.HandleCommand(args.Player, Utils.ToCommand(...));
    }
    return;
}
```

---

### 2.7 替代命令语法

启用 `Enhancements.AlternativeCommandSyntax` 后支持两种多命令分隔符：

#### 顺序执行（`&&`）

```
/command1 arg1 && command2 arg2 && command3 arg3
```

前面命令失败**不会**阻止后续命令执行。但调用堆栈会显示从 `HandleCommandUncatched` 进入。

#### 容错执行（`;`）

```
/command1 arg1 ; command2 arg2 ; command3 arg3
```

使用 `;` 分隔时，每个命令独立执行，某个命令抛出异常**不会**影响其他命令。

#### 引号支持

```
/command "text with spaces" arg2
/command1 "arg1" ; command2 "arg2 with \"quote\""
```

支持反斜杠转义（`\"`）、引号内空格保留。

---

### 2.8 原版模式（Vanilla Mode）

启用 `.Mode.Vanilla.Enabled = true` 后，Omni 会创建一个名为 `*vanilla*` 的用户组，并设置其为默认注册组。该组包含原版游戏体验所需的基本权限：

```
tshock.canregister, tshock.canlogin, tshock.canlogout, tshock.canchangepassword,
tshock.hurttownnpc, tshock.spawnpets, tshock.summonboss, tshock.startinvasion,
tshock.startdd2, tshock.home, tshock.spawn, tshock.rod, tshock.wormhole,
tshock.pylon, tshock.tppotion, tshock.magicconch, tshock.demonconch,
tshock.editspawn, tshock.usesundial, tshock.movenpc, tshock.canbuild,
tshock.canpaint, tshock.toggleparty, tshock.whisper, tshock.canpartychat,
tshock.cantalkinthird, tshock.canchat, tshock.synclocalarea, tshock.sendemoji,
chireiden.omni.ping
```

额外选项：
- `AllowJourneyPowers` — 允许旅途模式能力
- `IgnoreAntiCheat` — 忽略反作弊检测
- `AntiCheat.Enabled` — 在 Vanilla 模式下启用额外反作弊

---

### 2.9 启动命令

配置 `.StartupCommands` 列表中的命令会在插件初始化时自动执行。适合：

- 服务器启动时自动广播消息
- 执行初始配置命令
- 设置自动保存定时任务

示例配置：

```json
{
  "StartupCommands": [
    "/broadcast 服务器已启动，欢迎!",
    "/setinterval /save 3600"
  ]
}
```

---

### 2.10 命令重命名

`.CommandRenames` 允许重命名任何命令（包括其他插件注册的命令）。键为方法的完整签名，值为新的命令名列表。

```json
{
  "CommandRenames": {
    "Chireiden.TShock.Omni.Plugin.Command_PermissionCheck": ["whynot123", "whynot456"],
    "TShockAPI.Commands.Kick": ["踢出", "kick"]
  }
}
```

在 `ApplyConfig` 中，所有命令（包括 ChatCommands、TShockCommands 和隐藏命令）都会被检查并重命名：

```csharp
foreach (var command in Commands.ChatCommands)
    Utils.TryRenameCommand(command, this.config.CommandRenames);
```

---

## 3. 配置体系详解

### 3.1 `Optional<T>` 模式设计

Omni 的配置系统采用了独创的 `Optional<T>` 泛型包装器，这是其配置体系的核心设计模式。

#### 设计动机

TShock 的配置采用 JSON 序列化，所有配置项都会出现在配置文件中。对于 Omni 这种功能极多的插件，普通用户面对数百个配置项会不知所措。`Optional<T>` 实现了**渐进式配置暴露**：

- **公开项**：`HideWhenDefault = false`，始终显示在配置文件中
- **隐藏项**：`HideWhenDefault = true`，仅当值被修改后才出现在配置文件中

#### 核心代码

```csharp
public class Optional<T>(T value, bool hide = false) : Optional
{
    public bool IsDefault { private set; get; } = true;
    public bool HideWhenDefault { private set; get; } = hide;
    internal T _defaultValue = value;
    internal T? _value;

    public T Value
    {
        get => this.IsDefault ? this._defaultValue : this._value!;
        set
        {
            // 如果是集合类型，使用 SequenceEqual 比较
            // 否则使用 EqualityComparer 比较
            if (与默认值不同)
            {
                this.IsDefault = false;
                this._value = value;
            }
        }
    }
}
```

#### 序列化行为

在 `CustomContractResolver` 中：

```csharp
if (this.SkipOptionalDefault && property.PropertyType 是 Optional<T>)
{
    property.ShouldSerialize = value =>
        property.ValueProvider.GetValue(value) is Optional o && !o.IsHiddenValue();
}
```

- **正常保存**：隐藏项且为默认值时**跳过**序列化
- **`/genconfig` 保存**：传入 `skip = false`，**所有项**（包括隐藏默认值）都写入文件

#### 使用示例

```csharp
// 公开项：始终显示
public Optional<bool> ShowConfig = Optional.Default(false);

// 隐藏项：仅在修改后显示（true = 隐藏）
public Optional<SoundnessSettings> Soundness = Optional.Default(new SoundnessSettings(), true);
```

---

### 3.2 `/genconfig` 命令

```
/genconfig
```

生成**完整版**配置文件，包含所有隐藏的配置项和详细注释。

- **正常启动/重载**：只保存非默认值项，配置文件精简
- **`/genconfig`**：保存所有项，包括隐藏的默认值，方便高级用户调优

权限：`chireiden.omni.admin.genconfig`

> ⚠️ **警告**：生成的完整配置文件包含大量高级选项。如非必要，**不要修改隐藏项**。

---

### 3.3 各配置节说明

#### Enhancements — 增强功能

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `TrimMemory` | bool | `true` | 移除未使用的客户端对象（人偶架/帽子架）节省内存 |
| `AlternativeCommandSyntax` | bool | `true` | 启用 `&&` 和 `;` 多命令语法 |
| `CLIoverConfig` | bool | `true` | 用 CLI 参数覆盖配置文件（端口、最大玩家数） |
| `DefaultLanguageDetect` | bool | `true` | 修复 TShock 默认语言检测缺陷（[#2957](https://github.com/Pryaxis/TShock/issues/2957)） |
| `SuppressUpdate` | enum | `Silent` | 更新检查策略：Silent（静默抑制）/ Disabled（完全禁用）/ AsIs（不干涉） |
| `Socket` | enum | `AnotherAsyncSocketAsFallback` | Socket 实现选择：Vanilla/TShock/AsIs/HackyBlocked/HackyAsync/AnotherAsyncSocket |
| `NameCollision` | enum | `Unhandled` | 同名冲突处理：First/Second/Both/None/Known/Unhandled |
| `TileProvider` | enum | `AsIs` | 图格提供器实验性选项 |
| `ExtraLargeWorld` | bool | `true` | 允许超大世界（仅服务端） |
| `ShowCommandAlias` | int | `0` | 在 `/help` 中显示命令别名数量 |
| `BanPattern` | bool | `true` | 支持正则和子网掩码封禁模式 |
| `ResolveAssembly` | bool | `true` | 尝试从已加载的程序集解析引用 |
| `IPv6DualStack` | bool | `true` | IPv6 双栈支持 |

#### Soundness — 稳健性修复

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `ProjectileKillMapEditRestriction` | bool | `true` | 限制弹幕（液体炸弹、火箭等）修改地图需要建筑权限 |
| `QuickStackRestriction` | bool | `true` | 快速堆叠需要建筑权限 |
| `SignEditRestriction` | bool | `true` | 编辑牌匾需要建筑权限 |
| `ObjectInteractionRestriction` | bool | `true` | 物体交互（武器架、食物盘、帽子架）需要建筑权限 |
| `UseDefaultEncoding` | int | `-1`（Win7 以下） | 修正遗留 Windows 的编码问题 |
| `UseEnglishCommand` | bool | `true` | 修复客户端/服务端语言不一致导致的命令失效（[#2914](https://github.com/Pryaxis/TShock/issues/2914)） |
| `AllowVanillaLocalizedCommand` | bool | `true` | 接受原版本地化命令别名 |

#### Mitigation — 缓解措施（可能有副作用）

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `DisableAllMitigation` | bool | `false` | 一键关闭所有缓解措施 |
| `InventorySlotPE` | bool | `true` | 修复 PE 客户端频繁发送 PlayerSlot 包导致的高内存占用和延迟 |
| `PotionSicknessPE` | bool | `true` | 修复 PE 客户端无药水 CD 喝药的漏洞 |
| `SwapWhileUsePE` | bool | `true` | 修复 PE 客户端使用物品时切换物品的漏洞 |
| `ChatSpamRestrict` | list | `["1.6/5", "4/20"]` | 聊天速率限制（格式：`速率/窗口`） |
| `ConnectionLimit` | list | `["3/5", "15/60"]` | 连接速率限制 |
| `ConnectionStateTimeout` | dict | `{0: 1, 1: 4}` | 连接状态超时断开 |
| `DisabledDamageHandler` | enum | `Hurt` | 被禁言玩家是否可以受伤：AsIs/Hurt/Ghost |
| `ExpertExtraCoin` | enum | `ServerSide` | 专家模式硬币处理：DisableValue/ServerSide/AsIs |
| `KeepRestAlive` | bool | `true` | 修复遗留 HttpServer.dll 的长请求问题（[#2923](https://github.com/Pryaxis/TShock/issues/2923)） |
| `AcceptPartialConfigUpgrade` | enum | `Replace` | 处理部分更新配置：Ignore/Replace |
| `OverflowWorldGenItemID` | bool | `true` | 阻止利用世界生成溢出刷物品 |
| `NonRecursiveWorldGenTileCount` | bool | `true` | 修复 Mac Rosetta 下世界生成栈溢出 |
| `AllowCrossJourney` | bool | `false` | 允许旅途/非旅途玩家同服 |
| `LoadoutSwitchWithoutSSC` | bool | `true` | 非 SSC 模式下的配装切换修复 |
| `RestrictiveSocketSend` | bool | `true` | 限制每个 Send 调用只包含一个消息包 |
| `EchoUnchangedItem` | bool | `true` | 返回未修改的物品信息给所有者 |
| `AllowNonVanillaNameChange` | bool | `false` | 允许非原版的名字变更 |
| `AllowNonVanillaJoinState` | bool | `false` | 允许非原版的连接状态包 |

#### Modes — 运行模式

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `Building.Enabled` | bool | `false` | 建筑模式（入口预留） |
| `PvP.Enabled` | bool | `false` | PvP 模式（入口预留） |
| `Vanilla.Enabled` | bool | `false` | 原版模式（详见 2.8 节） |
| `Vanilla.Permissions` | list | 30+ 权限 | 原版模式下赋予的权限列表 |
| `Vanilla.AllowJourneyPowers` | bool | `false` | 允许旅途模式能力 |
| `Vanilla.IgnoreAntiCheat` | bool | `false` | 忽略反作弊 |
| `Vanilla.AntiCheat.Enabled` | bool | `false` | 启用额外反作弊 |

#### Permission — 权限设置

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `Log.Enabled` | bool | `true` | 是否启用权限查询日志（`/whynot` 的底层） |
| `Log.LogCount` | int | `50` | 每个玩家保留的权限查询历史条数 |
| `Log.LogDuplicate` | bool | `false` | 是否记录重复权限查询 |
| `Log.LogDistinctTime` | double | `1.0` | 去重时间窗口（秒） |
| `Log.LogStackTrace` | bool | `false` | 是否记录调用堆栈 |
| `Preset.Enabled` | bool | `true` | 是否应用权限预设 |
| `Preset.AlwaysApply` | bool | `false` | 是否每次重载都应用（而不是仅首次） |
| `Preset.DebugForAdminOnly` | bool | `false` | `/whynot` 是否仅管理员可用 |

---

## 4. Misc 扩展插件功能

`Chireiden.TShock.Omni.Misc.dll` 是一个可选扩展插件，提供一些随机但有用的功能。**它依赖核心插件**（需要 `Omni.Plugin` 实例）。

### 4.1 权限限制（Permission Restrict）

| 权限节点 | 限制功能 |
|----------|----------|
| `chireiden.omni.togglepvp` | 允许切换 PvP 状态 |
| `chireiden.omni.togglepvp.true` | 允许开启 PvP |
| `chireiden.omni.togglepvp.false` | 允许关闭 PvP |
| `chireiden.omni.toggleteam` | 允许切换队伍 |
| `chireiden.omni.toggleteam.0` ~ `.5` | 允许切换到指定队伍 |
| `chireiden.omni.syncloadout` | 允许切换配装 |
| `chireiden.omni.summonboss.{id}` | 允许召唤指定 Boss（NPC ID） |

**按 Boss 精确控制**：
```
chireiden.omni.summonboss.4     — 允许召唤克苏鲁之眼
chireiden.omni.summonboss.262   — 允许召唤猪鲨公爵
```

### 4.2 岩浆防刷（LavaHandler）

自动清理因方块破坏产生的异常岩浆。配置项：

```json
{
  "LavaHandler": {
    "Enabled": true,
    "AllowHellstone": false,
    "AllowCrispyHoneyBlock": false,
    "AllowHellbat": false,
    "AllowLavaSlime": false,
    "AllowLavabat": false
  }
}
```

当破坏狱石（Hellstone）或脆蜂蜜块（CrispyHoneyBlock）后，如果所在格出现岩浆，自动清除；击杀地狱生物后同样处理。

### 4.3 Misc 命令

| 命令 | 说明 | 权限 |
|------|------|------|
| `/echo <text>` | 发送纯文本消息（无颜色标记） | `chireiden.omni.echo` |
| `/_pvp [player] true/false` | 查看/设置 PvP 状态 | `chireiden.omni.setpvp` |
| `/_team [player] <team>` | 查看/设置队伍 | `chireiden.omni.setteam` |
| `/_chat <text>` | 模拟聊天消息（触发聊天钩子） | `chireiden.omni.chat` |
| `/_gc [-f]` | 触发垃圾回收 | `chireiden.omni.admin.gc` |
| `/_sv` | SQLite VACUUM | `chireiden.omni.admin.sv` |
| `/_ups [bench]` | 检查/基准测试服务器 UPS | `chireiden.omni.admin.upscheck` |
| `/rbc <msg>` | 原始广播（0,0,0 颜色） | `chireiden.omni.admin.rawbroadcast` |
| `/listclients` | 列出所有原始连接状态 | `chireiden.omni.admin.listclients` |
| `/dumpbuffer <index>` | 导出某连接的原始数据缓冲区 | `chireiden.omni.admin.dumpbuffer` |
| `/whereis <cmd>` | 查找命令注册来源（插件/程序集） | `chireiden.omni.admin.whereis` |
| `/kc <index>` | 强制终止 socket 连接 | `chireiden.omni.admin.terminatesocket` |
| `/_csf` | 查看调用堆栈 | `chireiden.omni.admin.callstackframe` |

### 4.4 版本同步（SyncVersion）

启用后禁用原版版本检查，允许不同版本的客户端连接服务端。

---

## 5. 高级特性

### 5.1 Detour/Hook 机制

Omni 使用 **MonoMod.RuntimeDetour** 进行方法拦截，支持两种钩子类型：

#### Detour（完整方法替换）

使用 `Hook` 类，在方法执行前后或完全替换逻辑。核心钩子列表：

```csharp
// 权限诊断
Detour_HasPermission → TSPlayer.HasPermission

// 更新检查抑制
Detour_UpdateCheckAsync → UpdateManager.UpdateCheckAsync

// 幽灵模式
Detour_PlayerActive → TSPlayer.Active

// 通配符系统
Detour_Wildcard_GetPlayers → TSPlayer.FindByNameOrID

// 控制台标题抑制
Detour_Mitigation_SetTitle → TShockAPI.Utils.SetConsoleTitle

// 替代命令语法
Detour_Command_Alternative → Commands.HandleCommand

// 封禁模式匹配
Detour_CheckBan_IP → BanManager.CheckBan

// 帮助命令别名显示
Detour_HelpAliases → Commands.Help

// 配置升级修复
Detour_Mitigation_ConfigUpdate → FileTools.AttemptConfigUpgrade

// 配装切换修复
Detour_Mitigation_HandleSyncLoadout → GetDataHandlers.HandleSyncLoadout

// IPv6 双栈
Detour_Socket_StartDualMode → TcpListener.Start

// IPv6 IP 解析
Detour_RealIP_IPv6Support → TShockAPI.Utils.GetRealIP

// 世界路径获取（防崩溃）
Detour_Main_getWorldPathName → Main.worldPathName
```

#### ILHook（IL 代码注入）

使用 `ILHook` 类在方法 IL 层面插入指令：

```csharp
// 禁用无敌：在 TSPlayer.IsBeingDisabled 调用后注入
ILHook_Mitigation_DisabledInvincible → Bouncer.OnPlayerDamage

// REST 保持活跃：在 ConnectionHeader.Type setter 前注入
ILHook_Mitigation_KeepRestAlive → Rest.OnRequest
```

### 5.2 包优先处理

Omni 支持两种包处理顺序：

```csharp
// 在构造函数中决定
if (this.config.PrioritizedPacketHandle)
{
    // 使用 RegisterFirst 将自己的钩子注册到最前面
    Utils.RegisterFirst<EventHandler<...>>(typeof(MessageBuffer), "GetData", null,
        this.OTHook_Modded_GetData,
        this.OTHook_Mitigation_GetData);
}
else
{
    // 正常注册，和其他插件排队
    OTAPI.Hooks.MessageBuffer.GetData += this.OTHook_Modded_GetData;
    OTAPI.Hooks.MessageBuffer.GetData += this.OTHook_Mitigation_GetData;
}
```

优先处理模式通过反射将 Omni 的处理器插入到事件委托链的最前端，确保**在任何其他插件之前**处理数据包。

### 5.3 插件加载顺序控制

```csharp
public Plugin(Main game) : base(game)
{
    this.Order = -1_000_000;
    // ...
}
```

`Order = -1_000_000` 确保 Omni **在 TShock 和所有其他插件之前加载**。这是因为：

1. Omni 需要优先注册钩子
2. 需要在其他插件之前完成配置加载
3. 需要在控制台交互阶段之前拦截 CLI 配置

---

## 6. 下载与部署

### 6.1 NuGet 包方式（推荐开发者）

```
dotnet add package Chireiden.TShock.Omni
dotnet add package Chireiden.TShock.Omni.Misc
```

NuGet 包会自动处理依赖关系，适合通过插件管理器部署的环境。

### 6.2 手动下载 Release

从 [GitHub Releases](https://github.com/sgkoishi/yaaiomni/releases) 下载：

- **Windows 用户**：下载 `.zip` 文件
- **Linux 用户**：下载 `.tar.gz` 文件（保留文件权限和符号链接）
- 将 DLL 放入 TShock 的 `ServerPlugins/` 目录

### 6.3 Docker/Linux 部署注意事项

1. **编码问题**：Linux 默认编码可能与 Windows 不同，配置项 `UseDefaultEncoding` 默认在 Linux 为 0（不修改），如有中文乱码可尝试设置为 `-1`
2. **控制台标题抑制**：某些终端（非 xterm 兼容）会出现控制台转义序列乱码，`SuppressTitle = Smart` 会自动检测
3. **Socket 类型**：Linux 下默认使用 `AnotherAsyncSocketAsFallback`，如果 TShock 默认使用 `LinuxTcpSocket` 则回退到自定义实现
4. **Git 版本号**：CI 构建需要 git 环境来生成版本号，如果手动编译可能需要处理 `CommitHashAttribute`

---

## 7. 最佳实践

### 7.1 权限诊断工作流

```
场景：玩家说"我不能开门/我不能使用某个指令"
```

1. 管理员使用 `/whynot 玩家名`（如果开启详细模式）
2. 查看红色条目（被拒绝的权限）
3. 如有必要使用 `/whynot 玩家名 -v` 查看调用堆栈，精确定位哪个插件/功能在检查权限
4. 使用 `/group addperm <group> <permission>` 授予缺失权限
5. 通知玩家重试

### 7.2 配置优化建议

#### 新人友好服务器

```json
{
  "Mode": {
    "Vanilla": {
      "Enabled": true,
      "AllowJourneyPowers": false,
      "IgnoreAntiCheat": false
    }
  },
  "Permission": {
    "Preset": {
      "DebugForAdminOnly": true
    }
  },
  "HideCommands": ["whynot", "echo", "ping"]
}
```

#### 高性能配置

```json
{
  "Mitigation": {
    "InventorySlotPE": true,
    "PotionSicknessPE": true,
    "SwapWhileUsePE": true,
    "RestrictiveSocketSend": true,
    "EchoUnchangedItem": true
  },
  "Enhancements": {
    "TrimMemory": true,
    "Socket": "AnotherAsyncSocketAsFallback",
    "SuppressUpdate": "Silent"
  }
}
```

#### 调试排错配置

```json
{
  "LogFirstChance": true,
  "DebugPacket": {
    "In": true,
    "Out": true,
    "ShowCatchedException": "All"
  },
  "Permission": {
    "Log": {
      "LogDuplicate": true,
      "LogStackTrace": true
    }
  }
}
```

### 7.3 与 TSWeb 插件配合使用

Omni 与 TSWeb 的互补关系：

| 场景 | Omni 功能 | TSWeb 功能 |
|------|-----------|------------|
| 权限排查 | `/whynot` 实时查看 | Web 后台登录历史 |
| 服务器管理 | `/setlang`, `/maxplayers`, `/runas` | Web 管理面板操作 |
| 定时任务 | `/setinterval`, `/settimeout` | Web 计划任务 |
| 角色管理 | `/resetcharacter`, `/exportcharacter` | Web 角色编辑 |
| 配置管理 | `/genconfig` | Web 配置编辑器 |

建议部署清单：
1. Omni 作为**底层工具集**，处理权限诊断、网络修复、漏洞缓解
2. TSWeb 作为**上层管理界面**，提供 Web UI 方便非管理员操作
3. Omni 的 `/runas` 可在 TSWeb 的 Web 命令执行中作为后台执行器

---

## 8. 源码架构分析

### 8.1 插件分层

```
SourceGen/                     ← 源代码生成器（编译时）
  Commands.cs                  ─ 从 [Command] 属性自动生成 InitCommands() 和权限常量
    
Core/                          ← 核心 DLL（运行时）
  Plugin.cs                    ─ 插件入口，Detour 注册，生命周期管理
  Config.cs                    ─ 配置模型 + Optional<T> + Limiter
  Json.cs                      ─ 自定义 JSON 序列化器
  Ext.cs                       ─ 扩展方法（TerrariaApi.Server 扩展，Config 突变）
  Utils.cs                     ─ 工具函数（700 行，核心基础设施）
  AttachedData.cs              ─ 玩家附加数据（权限历史、定时任务、ping）
  
  ├── 权限 & 命令
  ├── PermissionCheck.cs       ─ /whynot 权限诊断
  ├── MiscCommands.cs          ─ 通用命令（runas, echo, genconfig, exportcharacter...）
  ├── LangCommand.cs           ─ /setlang 语言管理
  ├── Timeout.cs               ─ 定时任务系统
  ├── Wildcard.cs              ─ 通配符系统
  ├── AltCmd.cs                ─ 替代命令语法
  ├── HideCommand.cs           ─ 命令隐藏
  ├── Sudo.cs                  ─ 跳过权限执行
  ├── Vanilla.cs               ─ 原版模式
  ├── Ghost.cs                 ─ 幽灵模式
  └── Ping.cs                  ─ 玩家 Ping 检测
  
  ├── 修复 & 缓解
  ├── Mitigations.cs           ─ 核心缓解措施（687 行，PE 修复、聊天防刷等）
  ├── PacketSpam.cs            ─ 连接限制和超时管理
  ├── Modded.cs                ─ 非原版客户端检测
  ├── Soundness/Misc.cs        ─ 稳健性修复（权限检查增强）
  ├── Soundness/ProjectileKill.cs ─ 弹幕地图编辑限制
  
  ├── 性能 & 调试
  ├── Enhancements.cs          ─ 增强功能（内存裁剪、Socket 切换、IPv6）
  ├── DebugPacket.cs           ─ 包调试日志
  ├── Statistics.cs            ─ 调试统计信息
  ├── FirstChance.cs           ─ 首次异常日志
  
  ├── 网络 & Socket
  ├── SocketProvider.cs        ─ 自定义 Socket 实现（312 行）
  ├── CliConfig.cs             ─ CLI 配置覆盖
  ├── MoreBan.cs               ─ 封禁模式扩展（正则、子网掩码）
  
  └── 其他
      ├── WorldGen.cs          ─ 世界生成调试工具
      ├── Backport.cs          ─ TShock 缺陷回滚补丁
      ├── LanguagePolyfill.cs  ─ 语言兼容层
      ├── TileProvider/        ─ 实验性 Tile 提供器
      └── LegacyConsts.cs      ─ 遗留常量

Misc/                          ← 可选扩展 DLL
  Plugin.cs                    ─ 扩展插件入口
  Config.cs                    ─ 扩展配置（Lava、Permission Restrict）
  Commands.cs                  ─ 扩展命令（372 行，12 个附加命令）
  Hooks.cs                     ─ 扩展 Detour/ILHook 基础设施
  LavaHandler.cs               ─ 岩浆防刷
  PermissionRestrict.cs        ─ 权限限制（Boss、队伍、PvP、配装）
```

### 8.2 配置系统设计

```
用户输入 (config.json)
        │
        ▼
  JsonUtils.DeserializeConfig<T>()
        │
        ▼
  Optional<T> 反序列化
    ├── 如果值存在 → 设置 Value (IsDefault = false)
    └── 如果值不存在 → 保持默认值 (IsDefault = true)
        │
        ▼
  CustomContractResolver.ShouldSerialize
    ├── 正常保存 → 跳过 IsDefault && HideWhenDefault 的项
    └── /genconfig → 序列化所有项（包含隐藏默认值）
```

### 8.3 权限预设系统

插件首次加载时执行 `DefaultPermissionSetup()`：

```
初始化
  │
  ▼
权限预设是否启用？
  ├── 否 → 跳过
  └── 是（首次）→ 执行 PermissionSetup()
        │
        ▼
  创建权限别名体系：
  ┌──────────────────────┐
  │ tshock.kick          │
  │   ├→ whynot          │（DebugForAdminOnly 时）
  │   ├→ ghost           │
  │   ├→ setlang         │
  │   ├→ debugstat       │
  │   ├→ timeout         │
  │   ├→ resetcharacter  │
  │   └→ runbackground   │
  ├──────────────────────┤
  │ tshock.maintenance   │
  │   ├→ maxplayers      │
  │   ├→ exportcharacter │
  │   └→ genconfig       │
  ├──────────────────────┤
  │ tshock.su            │
  │   ├→ runas (sudo)    │
  │   └→ reset all       │
  └──────────────────────┘
```

### 8.4 Detour 模式详解

Omni 使用 `MonoMod.RuntimeDetour` 的 `Hook` 类实现 Detour，模式如下：

```csharp
// 1. 定义委托类型匹配原方法签名
private bool Detour_HasPermission(Func<TSPlayer, string, bool> orig,
    TSPlayer player, string permission)
{
    // 2. 前置逻辑
    // 3. 调用原始方法（可选）
    var result = orig(player, permission);
    // 4. 后置逻辑
    return result;
}

// 5. 反射获取原方法，创建 Hook
this.Detour(
    nameof(this.Detour_HasPermission),
    typeof(TSPlayer).GetMethod(nameof(TSPlayer.HasPermission), ...),
    this.Detour_HasPermission);

// 6. 析构时撤销
foreach (var detour in this._detours.Values)
{
    detour.Undo();
    detour.Dispose();
}
```

---

## 9. 常见问题与排错

### Q1: 为什么我修改了配置文件但没生效？

**原因**：Omni 使用 `Optional<T>` 的隐藏机制。如果你修改的是隐藏项（`HideWhenDefault = true`），但值没有变化，配置文件中不会体现。

**解决**：使用 `/genconfig` 生成完整配置文件，然后修改。

### Q2: `/whynot` 显示为空

**原因**：
1. `Permission.Log.Enabled` 被设置为 `false`
2. 玩家权限检查次数较少，环形缓冲区为空
3. 插件刚重载，历史已被清除

**排查**：
```
/_debugstat        — 查看插件内部统计
/setlang           — 确认插件正常运行
```

### Q3: 玩家报告"无法交流/聊天被吞"

**原因**：聊天防刷被触发。

**排查**：
1. 查看 `ChatSpamRestrict` 配置
2. 使用 `/_debugstat` 查看 `MitigationRejectedChat` 计数

**调整**：
```json
{
  "Mitigation": {
    "ChatSpamRestrict": [
      "1.6/5",
      "4/20"
    ]
  }
}
```
每个条目的格式为 `"速率/窗口"`，增大窗口或减小速率都会放宽限制。

### Q4: 插件加载失败

**控制台输出**：
```
Duplicate Chireiden.TShock.Omni loaded:
  --> Loaded:  SomePlugin.dll (v1.0.0.0)
  --> Current: Chireiden.TShock.Omni.dll (v1.0.0.0)
```

**原因**：多个同名插件被加载。Omni 的 `AssemblyMutex` 机制会检测并抛出异常。

**解决**：检查 `ServerPlugins/` 目录，移除重复的 DLL。

### Q5: PE 玩家导致服务器卡顿/内存飙升

**原因**：PE 客户端在某些情况下会高频发送 `PlayerSlot` 包。

**解决**：确保以下配置已启用：
```json
{
  "Mitigation": {
    "InventorySlotPE": true,
    "PotionSicknessPE": true,
    "SwapWhileUsePE": true
  }
}
```

### Q6: 在 Linux 上控制台输出乱码

**原因**：终端的编码与服务器编码不一致。

**解决**：
```json
{
  "Soundness": {
    "UseDefaultEncoding": -1
  },
  "Mitigation": {
    "SuppressTitle": "Smart"
  }
}
```

### Q7: 玩家可以刷物品/刷 Boss

**原因**：某些漏洞（如门风格溢出、图格掉落溢出）未被完全防护。

**排查**：
```
/inspecttileframe           — 启动图格帧检查（高级调试）
/_debugstat                 — 查看 Mitigation 触发计数
```

**加固**：
```json
{
  "Mitigation": {
    "OverflowWorldGenItemID": true,
    "ClearOverflowWorldGenStackTrace": false,
    "DumpMapOnStackOverflowWorldGen": true
  },
  "Misc": {
    "Permission": {
      "Restrict": {
        "Enabled": true,
        "SummonBoss": true
      }
    }
  }
}
```

### Q8: 如何彻底禁用某个功能？

Omni 的每个功能都有对应的配置开关。对于缓解措施，还可以使用：

```json
{
  "Mitigation": {
    "DisableAllMitigation": true
  }
}
```

这会关闭所有缓解措施（包括 PE 修复、聊天防刷、连接限制等），回到纯 TShock 行为。

---

## 附录：完整权限列表

| 权限 | 关联命令 | 预设组 |
|------|----------|--------|
| `chireiden.omni.whynot` | `/whynot` | guest / admin 调试 |
| `chireiden.omni.whynot.detailed` | `/whynot -v` | admin |
| `chireiden.omni.setlang` | `/setlang` | kick 组 |
| `chireiden.omni.admin.maxplayers` | `/maxplayers` | maintenance 组 |
| `chireiden.omni.admin.sudo` | `/runas` | superadmin 组 |
| `chireiden.omni.timeout` | `/settimeout` | kick 组 |
| `chireiden.omni.interval` | `/setinterval` | kick 组 |
| `chireiden.omni.cleartimeout` | `/clearinterval` | kick 组 |
| `chireiden.omni.showtimeout` | `/showdelay` | kick 组 |
| `chireiden.omni.resetcharacter` | `/resetcharacter` | kick 组 |
| `chireiden.omni.admin.resetcharacter` | `/resetcharacter <player>` | maintenance 组 |
| `chireiden.omni.admin.resetcharacter.all` | `/resetcharacter *` | superadmin 组 |
| `chireiden.omni.admin.exportcharacter` | `/exportcharacter` | maintenance 组 |
| `chireiden.omni.echo` | `/echo` | guest |
| `chireiden.omni.ping` | `/_ping` | guest |
| `chireiden.omni.ghost` | `/ghost` | kick 组 |
| `chireiden.omni.admin.genconfig` | `/genconfig` | maintenance 组 |
| `chireiden.omni.admin.debugstat` | `/_debugstat` | kick 组 |
| `chireiden.omni.admin.trytileframe` | `/trytileframe` | 无预设 |
| `chireiden.omni.admin.inspecttileframe` | `/inspecttileframe` | 无预设 |
| `chireiden.omni.admin.runbackground` | `/_qbg` | kick 组 |
| `chireiden.omni.admin.locked` | `/_locked` | 无预设 |
| `chireiden.omni.admin.setupperm` | `/_setperm` | 无预设 |
| `chireiden.omni.setpvp` | `/_pvp` | guest（Misc） |
| `chireiden.omni.setteam` | `/_team` | guest（Misc） |
| `chireiden.omni.chat` | `/_chat` | canchat 组（Misc） |
| `chireiden.omni.summonboss.{id}` | Boss 召唤 | 无预设（Misc） |
| `chireiden.omni.togglepvp[.{0/1}]` | PvP 切换 | 无预设（Misc） |
| `chireiden.omni.toggleteam[.{0-5}]` | 队伍切换 | 无预设（Misc） |
| `chireiden.omni.syncloadout` | 配装切换 | 无预设（Misc） |
| `chireiden.omni.ific` | 强制存入箱子 | 无预设 |
| `chireiden.omni.signedit` | 编辑牌匾 | 无预设 |
| `chireiden.omni.objectinteract` | 交互物体 | 无预设 |

---

> **参考资源**
>
> - 源码：`参考源码/开源插件/yaaiomni-master/`
> - TShock 源码：`参考源码/TShock-general-devel/`
> - 插件参考：`参考源码/TShockPlugin-master/`
> - 官方仓库：[https://github.com/sgkoishi/yaaiomni](https://github.com/sgkoishi/yaaiomni)
> - NuGet：[https://www.nuget.org/packages/Chireiden.TShock.Omni/](https://www.nuget.org/packages/Chireiden.TShock.Omni/)
