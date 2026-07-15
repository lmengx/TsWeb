# Tp-Plus (TeleportRequest) 标杆插件技术文档

> **版本**: 1.2.0 | **框架**: .NET 9.0, TShock 6.1.0+, ApiVersion 2.1  
> **作者**: lmx12330 (Original author: MarioE)  
> **源码路径**: `plugin-son/tp-plus/TeleportRequest/`  
> **输出 DLL**: `TeleportRequest.dll`

---

## 目录

1. [插件概述](#1-插件概述)
2. [功能详解](#2-功能详解)
3. [完整命令参考](#3-完整命令参考)
4. [配置文件详解](#4-配置文件详解)
5. [数据持久化](#5-数据持久化)
6. [下载与部署步骤](#6-下载与部署步骤)
7. [生命周期与事件](#7-生命周期与事件)
8. [最佳实践](#8-最佳实践)
9. [与 TShock 原版对比](#9-与-tshock-原版对比)
10. [常见问题与排错](#10-常见问题与排错)

---

## 1. 插件概述

### 1.1 功能简述

Tp-Plus 是对 TShock 内置 `/tp` 和 `/tpallow` 命令的**完全重写**，提供了一套**三模式传送管理系统**：

- **Agree（允许）** — 他人可直接传送至你的位置
- **Request（需同意）** — 他人传送需你手动确认或拒绝，超时自动取消
- **Block（拒绝）** — 拒绝一切传送请求

此外还包含**白名单系统**和**全局默认模式**，为服务器管理员提供细粒度的传送控制。

### 1.2 设计哲学

```
┌─────────────────────────────────────────────────────────┐
│                   传送决策流水线                          │
│                                                         │
│  发起 /tp → 自传送? → 白名单? → Override权限? → 模式判定 │
│                                                         │
│  每个环节都有明确的优先级规则，无歧义、可预测              │
└─────────────────────────────────────────────────────────┘
```

### 1.3 文件清单

| 文件 | 行数 | 职责 |
|------|------|------|
| `TeleportRequest.cs` | 538 行 | 核心插件类：生命周期、命令处理、定时器 |
| `TPModeStore.cs` | 133 行 | 数据持久化：三模式存储、白名单存储 |
| `Config.cs` | 48 行 | 配置文件读写 |
| `TeleportRequest.csproj` | — | 项目定义（.NET 9.0, TShock 6.1.0） |

---

## 2. 功能详解

### 2.1 三模式传送系统

#### 2.1.1 模式定义

| 模式 | 枚举值 | /tpmode 参数 | 行为 |
|------|--------|-------------|------|
| **Agree**（允许） | `TPMode.Agree` | `agree`, `a` | 他人发送 `/tp <你>` 时**直接传送**，无需确认 |
| **Request**（需同意） | `TPMode.Request` | `request`, `r` | 他人发送 `/tp <你>` 时**创建待处理请求**，等待 `/tpa` 同意或 `/tpd` 拒绝 |
| **Block**（拒绝） | `TPMode.Block` | `block`, `b` | 他人发送 `/tp <你>` 时**直接拒绝**并提示"目标玩家拒绝了传送请求" |

#### 2.1.2 传送请求生命周期（Request 模式）

```
发起者 (/tp 目标)                    目标玩家
     │                                  │
     │  1. 检查目标模式                 │
     │  2. 检查是否存在待处理请求       │
     │     (同一目标仅允许一个待处理)    │
     │                                  │
     │ ─── 发送请求 ──────────────────→ │
     │     "已向xxx发送传送请求"        │
     │                                  │
     │                         定时器提醒(Interval秒)
     │                         超时倒计时(Timeout次)
     │                                  │
     │  ┌──── 等待响应 ────────┐       │
     │  │                      │        │
     │  │  /tpa (同意)         │        │
     │  │ ←── 传送到你位置 ── │         │
     │  │                      │        │
     │  │  /tpd (拒绝)         │        │
     │  │ ←── "已拒绝" ─────── │        │
     │  │                      │        │
     │  │  超时 (timeout=0)    │        │
     │  │ ←── "请求已超时" ─── │        │
     │  └──────────────────────┘        │
```

#### 2.1.3 请求超时机制（源码分析）

```csharp
// TeleportRequest.cs - OnTimerElapsed
void OnTimerElapsed(object sender, ElapsedEventArgs e)
{
    for (int i = 0; i < _requests.Length; i++)
    {
        var req = _requests[i];
        if (req.timeout <= 0) continue;

        var src = TShock.Players[i];
        var dst = TShock.Players[req.dst];

        if (src == null || dst == null)
        {
            req.timeout = 0;  // 玩家离线 → 清除请求
            continue;
        }

        req.timeout--;
        if (req.timeout == 0)
        {
            // 超时：通知双方
            src.SendErrorMessage("你的传送请求已超时。");
            dst.SendInfoMessage("{0} 的传送请求已超时。", src.Name);
        }
        else
        {
            // 倒计时中：提醒目标
            dst.SendInfoMessage("{0} 请求传送到你所在位置。(/tpa 或 /tpd)", src.Name);
        }
    }
}
```

关键设计点：

- **超时计数器**：`req.timeout` 初始化为 `_config.Timeout + 1`（多出 1 作为首次提醒的缓冲）
- **固定数组**：`TPRequest[256]` 以玩家索引为下标，O(1) 随机访问
- **玩家离线自动清除**：`OnLeave` 事件和定时器中的 null 检查双重保障
- **同一目标单一请求**：`TP()` 方法中遍历已有请求，防止重复发送

#### 2.1.4 完整传送决策流程图

```
                 /tp <玩家>
                     │
                     ▼
          ┌─────────────────────┐
          │  参数数量为 0?       │──→ 提示语法错误
          └─────────┬───────────┘
                    ▼ 否
          ┌─────────────────────┐
          │  子命令?             │
          │  ac / aclist / acdel │──→ 执行白名单操作
          └─────────┬───────────┘
                    ▼ 否
          ┌─────────────────────┐
          │  目标玩家存在?       │──→ "未找到该玩家"
          └─────────┬───────────┘
                    ▼ 是
          ┌─────────────────────┐
          │  目标 != 自己?       │──→ "不能传送到自己"
          └─────────┬───────────┘
                    ▼ 是
          ┌─────────────────────┐
          │  在白名单中?         │──→ 直接传送（绕过一切模式）
          └─────────┬───────────┘
                    ▼ 否
          ┌─────────────────────┐
          │  有 override 权限?   │──→ 直接传送
          └─────────┬───────────┘
                    ▼ 否
          ┌─────────────────────┐
          │  读取目标模式        │
          └─────────┬───────────┘
                    │
        ┌───────────┼───────────┐
        ▼           ▼           ▼
     Agree      Request       Block
        │           │           │
        ▼           ▼           ▼
    直接传送   创建待处理      "目标玩家拒绝
               请求并等待     了传送请求"
               /tpa 或 /tpd
```

### 2.2 白名单系统

#### 2.2.1 优先级规则

白名单的优先级**高于一切模式判定**，包括 Block 模式。这是源码中明确的优先级顺序：

```
白名单 → tshock.tp.override 权限 → 目标玩家模式 → 拒绝/请求
 ↑              ↑                         ↑
 最高          次高                      按模式
优先级        优先级                     判定
```

> **源码证据**（`TeleportRequest.cs` TP 方法）：
> 1. 先检查 `TPModeStore.IsAllowed(target.Index, e.Player.Index)` — 白名单
> 2. 再检查 `e.Player.HasPermission("tshock.tp.override")` — Override 权限
> 3. 最后读取 `TPModeStore.GetMode(target.Index)` — 模式判定

#### 2.2.2 白名单命令

| 命令 | 功能 | 存储位置 |
|------|------|----------|
| `/tp ac <玩家名>` | 将某玩家加入白名单 | `tpplus.json` → `allowList` |
| `/tp aclist` | 查看自己的白名单 | 从 `tpplus.json` 读取 |
| `/tp acdel <玩家名>` | 将某玩家移出白名单 | `tpplus.json` → `allowList` |

#### 2.2.3 白名单典型使用场景

- **好友传送**：不想设置为 Agree（允许所有人传送），但希望特定好友能直接传送
- **管理员辅助**：将管理员加入白名单，确保管理传送不受模式切换影响
- **事件协调**：活动期间将参与者加入白名单，活动结束后移除

### 2.3 权限体系

| 权限节点 | 默认组 | 作用 |
|----------|--------|------|
| `tshock.tp.override` | 管理员 | 无视目标玩家的传送模式，直接传送 |
| `tshock.tp.block` | 所有玩家 | 可以使用 `/tpallow` 切换，可以设置 Block 模式 |
| `tshock.admin` | 管理员 | 使用 `/tpmode setdef` 设置全局默认模式 |

#### 2.3.1 权限检查路径（源码分析）

**Override 权限检查**（`TeleportRequest.cs` TP 方法）：
```csharp
if (e.Player.HasPermission("tshock.tp.override"))
{
    // 直接传送，无视目标模式
    e.Player.Teleport(target.X, target.Y);
    return;
}
```

**Block 权限检查**（`TeleportRequest.cs` TPModeCmd 方法）：
```csharp
if (parsed == TPMode.Block && !e.Player.HasPermission("tshock.tp.block"))
{
    e.Player.SendErrorMessage("你没有设置拒绝模式的权限...");
    return;
}
```

**Block 权限在 /tpallow 中的检查**：
```csharp
// 所有使用 /tpallow 的玩家都需要 tshock.tp.block 权限
if (!e.Player.HasPermission("tshock.tp.block"))
{
    e.Player.SendErrorMessage("你没有使用此命令的权限...");
    return;
}
```

> **设计洞察**：`/tpallow` 本质是 `block ↔ agree` 的切换开关，因此使用 `/tpallow` 需要 `tshock.tp.block` 权限。这与 TShock 原版行为一致。

---

## 3. 完整命令参考

### 3.1 命令总表

| 指令 | 语法 | 所需权限 | 说明 |
|------|------|---------|------|
| `/tp` | `/tp <玩家名>` | 无（默认） | 传送至目标玩家，根据对方模式/白名单/权限决定 |
| `/tp` | `/tp ac <玩家名>` | 无 | 将目标加入自己的白名单 |
| `/tp` | `/tp aclist` | 无 | 查看自己的白名单列表 |
| `/tp` | `/tp acdel <玩家名>` | 无 | 将目标移出白名单 |
| `/tpallow` | `/tpallow` | `tshock.tp.block` | 在 block 和 agree 模式间切换 |
| `/tpa` | `/tpa` | 无 | 同意当前所有待处理的传入传送请求 |
| `/tpd` | `/tpd` | 无 | 拒绝当前所有待处理的传入传送请求 |
| `/tpmode` | `/tpmode` | 无 | 查看当前模式和全局默认模式 |
| `/tpmode` | `/tpmode <agree\|request\|block>` | block 模式需 `tshock.tp.block` | 设置自己的传送模式 |
| `/tpmode` | `/tpmode setdef <a\|r\|b>` | `tshock.admin` | 设置未设置玩家的全局默认模式 |

### 3.2 简写别名

`/tpmode` 支持丰富的参数模糊匹配：

| 完整参数 | 简写别名 |
|----------|---------|
| `agree` | `a`, `ag`, `agr`, `agre` |
| `request` | `r`, `re`, `req`, `requ`, `reque`, `ruqes` |
| `block` | `b`, `bl`, `blo`, `bloc` |

### 3.3 命令注册细节

```csharp
// 所有命令均设置 AllowServer = false，禁止服务器控制台执行
Commands.ChatCommands.Add(new Command("", TP, "tp")
{
    AllowServer = false,
    HelpText = "传送到目标玩家..."
});
Commands.ChatCommands.Add(new Command("", TPAllow, "tpallow")
{
    AllowServer = false,
});
Commands.ChatCommands.Add(new Command("", TPAccept, "tpa")
{
    AllowServer = false,
});
Commands.ChatCommands.Add(new Command("", TPDeny, "tpd")
{
    AllowServer = false,
});
Commands.ChatCommands.Add(new Command("", TPModeCmd, "tpmode")
{
    AllowServer = false,
});
```

> **注意**：所有命令的权限参数为空字符串 `""`，表示该命令本身不需要基础权限（权限检查在方法内部手动完成）。`AllowServer = false` 意味着这些命令不能在服务器控制台执行，只能由游戏内玩家使用。

---

## 4. 配置文件详解

### 4.1 文件位置

```
{TShock.SavePath}/tpconfig.json
```

即与 TShock 的 `config.json` 同目录，通常为：
- **Windows**: ` terraria/tshock/tpconfig.json`
- **Linux**: `/home/terraria/.local/share/Terraria/tshock/tpconfig.json`

### 4.2 完整配置项

```json
{
  "Interval": 3,
  "Timeout": 3
}
```

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Interval` | `int` | `3` | 定时器触发间隔（秒）。控制向目标玩家发送提醒消息的频率。越小提醒越频繁，越大提醒间隔越久。 |
| `Timeout` | `int` | `3` | Request 模式的超时次数。**实际超时时间 = Interval × Timeout（秒）**。默认 `3 × 3 = 9` 秒。 |

### 4.3 配置加载与初始化（源码分析）

```csharp
private void LoadConfig()
{
    var configPath = Path.Combine(TShock.SavePath, "tpconfig.json");
    if (File.Exists(configPath))
        _config = Config.Read(configPath);   // 存在则读取
    _config.Write(configPath);                // 不存在则创建，存在则覆盖写入（确保字段完整）
}
```

> **设计细节**：`LoadConfig()` 总是调用 `Write()` — 如果文件不存在，Write 会创建新文件写入默认值；如果文件存在，会以当前内存中的配置覆盖写入。这意味着如果配置文件缺少某个字段，会被自动补全为默认值（反序列化时缺失字段为 `0`，随后被 Write 写入默认值覆盖）。

### 4.4 Config 类的读写实现

```csharp
public class Config
{
    public int Interval = 3;   // 字段默认值（非属性）
    public int Timeout = 3;

    public void Write(string path)
    {
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Write))
        {
            var str = JsonConvert.SerializeObject(this, Formatting.Indented);
            using (var sw = new StreamWriter(fs))
            {
                sw.Write(str);
            }
        }
    }

    public static Config Read(string path)
    {
        if (!File.Exists(path)) return new Config();
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            using (var sr = new StreamReader(fs))
            {
                return JsonConvert.DeserializeObject<Config>(sr.ReadToEnd());
            }
        }
    }
}
```

### 4.5 超时配置建议

| 服务器类型 | Interval 推荐 | Timeout 推荐 | 实际超时时间 | 理由 |
|-----------|--------------|-------------|-------------|------|
| 小规模（<20人） | `5` | `6` | 30 秒 | 玩家熟悉，给予充足反应时间 |
| 中等规模（20-60人） | `3` | `3` | 9 秒 | 默认值，平衡体验和效率 |
| 大规模（>60人） | `2` | `2` | 4 秒 | 请求量大，快速清理减少延迟 |
| 活动/事件服务器 | `5` | `10` | 50 秒 | 活动期间可能需要更多响应时间 |

---

## 5. 数据持久化

### 5.1 文件位置

```
{TShock.SavePath}/tpplus.json
```

### 5.2 数据结构

```json
{
  "_default": "Agree",
  "players": {
    "3": "Block",
    "7": "Request"
  },
  "allowList": {
    "3": [1, 5, 12],
    "7": [2]
  }
}
```

### 5.3 字段说明

| 字段 | 类型 | 说明 |
|------|------|------|
| `_default` | `TPMode` 枚举（字符串序列化） | 未单独设置模式的玩家的默认传送模式。可选值：`Agree`、`Request`、`Block` |
| `players` | `Dictionary<int, TPMode>` | 玩家 ID → 传送模式的映射。仅包含**已主动设置过模式**的玩家 |
| `allowList` | `Dictionary<int, List<int>>` | 目标玩家 ID → 被允许主动传送的发起者 ID 列表 |

### 5.4 枚举序列化行为

`TPMode` 枚举使用 Newtonsoft.Json 默认序列化，以字符串形式存储：

```csharp
public enum TPMode
{
    Agree,    // JSON中存储为 "Agree"
    Request,  // JSON中存储为 "Request"
    Block     // JSON中存储为 "Block"
}
```

### 5.5 线程安全设计（TPModeStore 源码分析）

```csharp
internal class StoreData
{
    public TPMode _default = TPMode.Agree;
    public Dictionary<int, TPMode> players = new Dictionary<int, TPMode>();
    public Dictionary<int, List<int>> allowList = new Dictionary<int, List<int>>();
}

public static class TPModeStore
{
    private static readonly object Lock = new object();
    private static StoreData _data = new StoreData();
    private static string _filePath;

    // 所有公开方法均使用 lock(Lock) 保护
    public static TPMode GetMode(int playerId)
    {
        lock (Lock)
        {
            return _data.players.TryGetValue(playerId, out var mode)
                ? mode : _data._default;
        }
    }

    public static void SetMode(int playerId, TPMode mode)
    {
        lock (Lock)
        {
            _data.players[playerId] = mode;
            Save();   // 每次修改都立即持久化
        }
    }
    // ...
}
```

关键设计要点：

1. **全局锁**：所有读写操作通过 `Lock` 对象互斥，保证线程安全
2. **立即持久化**：每个修改操作（SetMode, AddAllowed, RemoveAllowed）在修改后立即调用 `Save()`
3. **写时复制**：`GetAllowedList` 返回 `new List<int>(list)` 的副本，防止外部修改内部状态
4. **初始化重置**：`Initialize()` 调用 `Load()` 重置 `_data`，确保重载时以磁盘数据为准

### 5.6 GetMode 的默认值回退逻辑

```csharp
public static TPMode GetMode(int playerId)
{
    lock (Lock)
    {
        // 如果 players 字典中没有该玩家的记录，则返回 _default
        return _data.players.TryGetValue(playerId, out var mode)
            ? mode : _data._default;
    }
}
```

这是整个三模式系统的"兜底"逻辑：**未设置模式的玩家遵循全局默认值**。

---

## 6. 下载与部署步骤

### 6.1 从 NuGet 获取依赖

项目依赖 **TShock 6.1.0** NuGet 包。编译前需确保已配置 NuGet 源：

```bash
# 添加 TShock NuGet 源（如尚未添加）
dotnet nuget add source https://nuget.tshock.co/v3/index.json -n tshock
```

### 6.2 编译命令

```bash
# 进入项目目录
cd plugin-son/tp-plus/TeleportRequest/

# 还原依赖
dotnet restore

# 编译（Debug 模式）
dotnet build

# 编译（Release 模式）
dotnet build -c Release

# 编译输出位置
# bin/Debug/net9.0/TeleportRequest.dll
# bin/Release/net9.0/TeleportRequest.dll
```

> **注意**：项目文件设置了 `CopyLocalLockFileAssemblies=true`，编译输出目录会包含所有依赖项 DLL，部署时只需复制 `TeleportRequest.dll` 本身即可。

### 6.3 编译配置参考

```xml
<!-- TeleportRequest.csproj 关键配置 -->
<PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <AssemblyName>TeleportRequest</AssemblyName>
    <RootNamespace>TeleportRequest</RootNamespace>
    <Version>1.2.0</Version>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
</PropertyGroup>

<ItemGroup>
    <PackageReference Include="TShock" Version="6.1.0">
        <IncludeAssets>compile</IncludeAssets>
    </PackageReference>
</ItemGroup>
```

### 6.4 部署到 ServerPlugins

```bash
# 假设 TShock 服务器目录为 /server/tshock/
# 1. 复制 DLL 到 ServerPlugins 目录
cp bin/Debug/net9.0/TeleportRequest.dll /server/tshock/ServerPlugins/

# 2. 重启服务器或使用热重载
# 热重载（在游戏内或控制台执行）
/reload
```

### 6.5 验证部署

部署成功后，在服务器控制台或游戏内执行：

```
# 应能看到 TeleportRequest 插件已加载
/version
# 或检查插件列表
/plugins
```

首次加载后，将在 `TShock.SavePath` 目录下自动生成：
- `tpconfig.json` — 配置文件（默认值）
- `tpplus.json` — 持久化数据文件

### 6.6 热重载注意事项

`/reload` 触发 `OnReload` 事件：

```csharp
private void OnReload(TShockAPI.Hooks.ReloadEventArgs e)
{
    LoadConfig();           // 重新读取 tpconfig.json
    TPModeStore.Initialize(TShock.SavePath);  // 重新加载 tpplus.json
    StartTimer();           // 重启定时器（应用新的 Interval）

    // 清理所有待处理的请求
    for (int i = 0; i < _requests.Length; i++)
        _requests[i].timeout = 0;

    TShock.Log.ConsoleInfo("[Teleport] 配置已重新加载");
}
```

**重载后会发生的变化**：
- ✅ `tpconfig.json` 的修改生效（Interval / Timeout）
- ✅ `tpplus.json` 的修改生效（手动编辑时）
- ✅ 所有待处理的传送请求被清空
- ❌ 已连接的玩家的模式不会丢失（存储在 `tpplus.json` 中，会重新加载）

---

## 7. 生命周期与事件

### 7.1 初始化流程（Initialize）

```
Initialize()
    │
    ├── 1. 移除 TShock 自带的 /tp 和 /tpallow
    │       Commands.ChatCommands.RemoveAll(cmd =>
    │           cmd.Names.Any(n => n == "tp" || n == "tpallow"))
    │
    ├── 2. 注册本插件的 5 个命令
    │       /tp, /tpallow, /tpa, /tpd, /tpmode
    │
    ├── 3. 加载配置 tpconfig.json
    │       LoadConfig() → 存在则读取, 不存在则创建
    │
    ├── 4. 初始化持久化存储
    │       TPModeStore.Initialize(TShock.SavePath)
    │       → 加载 tpplus.json
    │
    ├── 5. 启动请求超时定时器
    │       StartTimer() → Timer(Interval * 1000ms)
    │
    └── 6. 注册事件
            ServerApi.Hooks.ServerLeave.Register(this, OnLeave)
            TShockAPI.Hooks.GeneralHooks.ReloadEvent += OnReload
```

### 7.2 Dispose 清理流程

```
Dispose(disposing)
    │
    ├── 1. 停止并释放定时器
    │       _timer?.Stop()
    │       _timer?.Dispose()
    │
    ├── 2. 注销事件钩子
    │       ServerApi.Hooks.ServerLeave.Deregister(this, OnLeave)
    │       TShockAPI.Hooks.GeneralHooks.ReloadEvent -= OnReload
    │
    └── 3. 清理所有注册的命令
            Commands.ChatCommands.RemoveAll(cmd =>
                cmd.Names.Any(n => OwnedCommands.Contains(n)))
```

### 7.3 命令清理的安全性

```csharp
// 维护一个本插件注册的命令集合
private static readonly HashSet<string> OwnedCommands =
    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "tp", "tpa", "tpd", "tpallow", "tpmode"
};

// Dispose 时仅移除本插件注册的命令
Commands.ChatCommands.RemoveAll(cmd =>
    cmd.Names.Any(n => OwnedCommands.Contains(n)));
```

> **设计亮点**：使用 `HashSet<string>` 配合 `StringComparer.OrdinalIgnoreCase` 区分大小写，并在 Dispose 中精确移除本插件注册的命令，**不误伤其他插件**的 `tp` 命令。

### 7.4 事件响应

| 事件 | 处理方法 | 行为 |
|------|---------|------|
| 玩家离开 | `OnLeave` | 清除该玩家的待处理请求（`_requests[e.Who].timeout = 0`） |
| 热重载 | `OnReload` | 重读配置、重载数据、重启定时器、清空所有请求 |
| 定时器触发 | `OnTimerElapsed` | 遍历所有待处理请求，递减超时计数器，发送提醒或超时通知 |

---

## 8. 最佳实践

### 8.1 权限分配建议

| 用户类型 | 推荐分配权限 | 理由 |
|---------|-------------|------|
| **普通玩家** | `tshock.tp.block`（默认已有） | 可使用 `/tpallow` 切换同意/拒绝，设置 request 模式 |
| **VIP/赞助者** | `tshock.tp.block` | 同上，通常无需额外权限 |
| **建筑师/创作** | `tshock.tp.block` | 设置 request 或 block 模式避免被打扰 |
| **初级管理员** | `tshock.tp.override` | 可无视模式传送，便于管理 |
| **高级管理员** | `tshock.tp.override` + `tshock.admin` | 可设置全局默认模式 |

### 8.2 与旧版 TShock /tp 的兼容说明

插件在 `Initialize()` 中**主动移除**了 TShock 自带的 `/tp` 和 `/tpallow` 命令：

```csharp
Commands.ChatCommands.RemoveAll(cmd =>
    cmd.Names.Any(n => n.Equals("tp", StringComparison.OrdinalIgnoreCase) ||
                       n.Equals("tpallow", StringComparison.OrdinalIgnoreCase)));
```

这意味着：

- **安装插件后**，`/tp` 和 `/tpallow` 将完全由本插件接管
- **卸载插件后**，需重启服务器才能恢复 TShock 原版 `/tp`（因为 Dispose 清理了本插件的命令，但不会恢复原版命令）
- **插件升级时**，热重载会自动重新注册命令，无需额外操作

### 8.3 白名单使用场景

**场景一：好友协作建筑**
```
玩家A（Block 模式，防止被随意打扰）
但是将玩家B（建筑伙伴）加入白名单
结果：其他玩家无法传送，但玩家B可以直接传送协助
```

**场景二：管理员匿名管理**
```
管理员（设置自己的模式为 Request）
将特定管理员加入白名单
结果：管理间可互传，普通玩家需请求许可
```

**场景三：活动事件**
```
活动主持人（设置为 Agree 或 Block）
将活动参与者加入白名单
结果：活动期间参与者可直达主持人位置
```

### 8.4 超时配置建议

参见 [4.5 超时配置建议](#45-超时配置建议)。

核心原则：
- **Interval** 决定提醒频率，值越大提醒间隔越久，网络负担越小
- **Timeout** 决定超时检测次数，值越大给予的响应时间越长
- **实际超时 = Interval × Timeout**，需根据服务器延迟和玩家习惯调整

### 8.5 安全建议

1. **`tshock.tp.override` 仅授予可信管理员**：拥有此权限的玩家可无视任何模式传送
2. **`tshock.tp.block` 默认授予所有玩家**：让玩家有权拒绝不必要的传送
3. **定期检查 `tpplus.json`**：确认白名单是否合理，清理不再需要的条目
4. **/reload 前通知玩家**：重载会清空所有待处理的传送请求

---

## 9. 与 TShock 原版对比

### 9.1 差异总表

| 对比维度 | TShock 原版 /tp | Tp-Plus 插件 |
|---------|----------------|-------------|
| **传送模式** | 仅 allow（允许）/ 不允许 | **三模式**：Agree / Request / Block |
| **请求机制** | ❌ 无 | ✅ Request 模式 + 超时自动取消 |
| **白名单** | ❌ 无 | ✅ `/tp ac/aclist/acdel` 完整白名单系统 |
| **全局默认模式** | ❌ 固定为"允许" | ✅ `/tpmode setdef` 可配置 |
| **权限粒度** | `tshock.tp` | ✅ `tshock.tp.override`、`tshock.tp.block` |
| **命令数量** | 2 个：`/tp`, `/tpallow` | **5 个**：`/tp`, `/tpallow`, `/tpa`, `/tpd`, `/tpmode` |
| **超时提醒** | ❌ 无 | ✅ 定时器 + 倒计时提醒 |
| **数据持久化** | ❌ 无 | ✅ `tpplus.json` 持久化模式和白名单 |
| **配置热重载** | 依赖 TShock | ✅ 完全支持 `/reload` |
| **线程安全** | 不适用 | ✅ 所有操作 lock 保护 |
| **服务器控制台执行** | ✅ 支持 | ❌ `AllowServer = false`，仅支持游戏内执行 |
| **兼容性** | 原版 | ❌ 会移除 TShock 原版 `/tp` 和 `/tpallow` |

### 9.2 功能对比详表

```
┌────────────────────────┬──────────────────────────────┐
│       场景              │   TShock 原版   │  Tp-Plus   │
├────────────────────────┼────────────────┼────────────┤
│ 玩家A传送到玩家B       │ 直接传送       │ 取决于B的模式 │
│ 玩家不想被打扰         │ /tpallow 关闭  │ 设置 Block   │
│ 希望好友能传但不       │ 无法实现       │ 白名单系统    │
│ 希望陌生人请求同意      │ 无法实现       │ Request 模式  │
│ 忘记处理请求           │ 永远等待      │ 自动超时取消  │
│ 管理员传送普通玩家     │ 直接传送      │ 直接传送(override) │
│ 批量管理传送模式       │ 无法实现      │ 全局默认模式  │
└────────────────────────┴────────────────┴────────────┘
```

### 9.3 升级影响

从 TShock 原版 `/tp` 升级到 Tp-Plus：

| 影响项 | 说明 | 应对方案 |
|-------|------|---------|
| 命令行为改变 | `/tp` 不再直接传送，取决于目标模式 | 告知玩家新机制，分配 `tshock.tp.override` |
| `/tpallow` 行为改变 | 不再只是切换开关，还涉及权限检查 | 确保玩家有 `tshock.tp.block` 权限 |
| 新命令引入 | `/tpa`, `/tpd`, `/tpmode` 需学习 | 使用插件内置的 HelpText 教育玩家 |
| 控制台无法用 | `AllowServer = false` | 部分管理操作需在游戏内执行 |

---

## 10. 常见问题与排错

### 10.1 部署问题

**Q: 插件加载失败，报版本兼容错误**

```
A: 检查 TShock 版本是否为 6.1.0+ 且框架为 .NET 9.0。
   插件使用 [ApiVersion(2, 1)]，确保 TShock 支持 API 2.1。
```

**Q: 编译时找不到 TShock NuGet 包**

```
A: 确保已添加 TShock NuGet 源：
   dotnet nuget add source https://nuget.tshock.co/v3/index.json -n tshock
   然后执行 dotnet restore
```

### 10.2 运行时问题

**Q: 执行 `/tp` 提示"语法错误"**

```
A: 检查是否提供了玩家名称。正确的语法：/tp <玩家名>
   注意玩家名支持模糊匹配，多个匹配时会提示"请指定更准确的名称"
```

**Q: 设置为 Block 模式但别人还能传送过来**

```
可能原因：
1. 发起者有 tshock.tp.override 权限 — 可绕过模式
2. 发起者在你的白名单中 — 白名单优先于 Block
3. 你未设置模式，全局默认模式为 Agree — 使用 /tpmode 检查当前模式
```

**Q: `/tpallow` 提示"没有权限"**

```
A: 需要 tshock.tp.block 权限。
   /tpallow 是 block ↔ agree 的切换开关，因此需要 block 权限。
   解决方案：给玩家分配 tshock.tp.block 权限。
```

**Q: 传送到目标后位置不对**

```
A: 目标玩家的 X/Y 坐标是脚底位置。
   检查是否有其他插件（如传送点插件）干扰了传送逻辑。
```

**Q: 超时时间感觉不对**

```
A: 实际超时 = Interval × Timeout
   例如 Interval=3, Timeout=3 → 实际约 9 秒
   注意 req.timeout 初始化为 Timeout + 1（多一次作为首次提醒缓冲）
```

### 10.3 数据问题

**Q: 手动编辑 tpplus.json 后不生效**

```
A: 编辑后需要执行 /reload 或在服务器控制台执行 /reload 重新加载。
   编辑时注意 JSON 格式正确，尤其是：
   - 枚举值需为字符串："Agree"、"Request"、"Block"
   - 玩家 ID 为整数
   - 列表格式正确
```

**Q: 玩家 ID 变更导致白名单失效**

```
A: 玩家 ID 是 TShock 分配的临时索引，服务器重启后可能变化。
   这不是本插件特有的问题，而是 TShock 的玩家索引机制决定的。
   建议玩家定期使用 /tp aclist 检查白名单并更新。
```

**Q: tpconfig.json 被重置为默认值**

```
A: LoadConfig() 方法在每次 Initialize 和 OnReload 时会调用 Write()。
   如果配置文件被手动修改导致 JSON 解析失败(反序列化返回默认对象)，
   然后 Write() 会以默认值覆盖文件。
```

### 10.4 与其他插件的冲突

**Q: 与其他 `/tp` 相关插件冲突**

```
A: 本插件在 Initialize() 中移除所有名为 "tp" 或 "tpallow" 的命令。
   如果其他插件也注册了同名命令，可能产生冲突。
   解决方案：卸载本插件或冲突插件，二选一。
```

**Q: `/reload` 后功能异常**

```
A: OnReload 会清空所有待处理请求。
   如果有玩家正在等待传送确认，会丢失该请求。
   建议在低负载时段执行 /reload。
```

---

## 附录 A：源码结构速览

```
TeleportRequest/
├── TeleportRequest.cs        # 538行 — 核心插件
│   ├── class TeleportRequest : TerrariaPlugin
│   │   ├── Fields: _config, _timer, _requests[256], _disposed
│   │   ├── OwnedCommands (static HashSet)
│   │   ├── Constructor
│   │   ├── Dispose()
│   │   ├── Initialize()
│   │   ├── LoadConfig()
│   │   ├── StartTimer()
│   │   ├── OnReload()
│   │   ├── OnLeave()
│   │   ├── OnTimerElapsed()
│   │   └── Commands: TP(), TPAllow(), TPAccept(), TPDeny(), TPModeCmd()
│   └── class TPRequest (内联结构)
│       ├── byte dst          # 目标玩家索引
│       ├── bool dir          # 方向（未使用）
│       └── int timeout       # 超时计数器
│
├── TPModeStore.cs            # 133行 — 数据持久化
│   ├── enum TPMode { Agree, Request, Block }
│   ├── class StoreData
│   │   ├── TPMode _default
│   │   ├── Dictionary<int, TPMode> players
│   │   └── Dictionary<int, List<int>> allowList
│   └── static class TPModeStore
│       ├── Lock, _data, _filePath
│       ├── Initialize(), Load(), Save()
│       ├── DefaultMode (property)
│       ├── GetMode(), SetMode()
│       ├── IsAllowed(), AddAllowed(), RemoveAllowed()
│       └── GetAllowedList()
│
├── Config.cs                 # 48行 — 配置读写
│   └── class Config
│       ├── int Interval = 3
│       ├── int Timeout = 3
│       ├── Write(string/Stream)
│       └── Read(string/Stream)
│
└── TeleportRequest.csproj    # 24行 — 项目定义
    ├── TargetFramework: net9.0
    ├── TShock 6.1.0 NuGet 依赖
    └── CopyLocalLockFileAssemblies: true
```

## 附录 B：TPRequest 结构体

```csharp
// TeleportRequest.cs 中的内部结构（自动实现）
// 用于存储每个玩家的传送请求状态
// 以玩家索引为下标，固定长度 256（TShock 最大玩家数）
private readonly TPRequest[] _requests = new TPRequest[256];

// 在构造函数中初始化
public TeleportRequest(Main game) : base(game)
{
    for (int i = 0; i < _requests.Length; i++)
        _requests[i] = new TPRequest();
}

// 内部结构
internal class TPRequest
{
    public byte dst;      // 目标玩家索引
    public bool dir;      // 方向（保留字段，当前未使用）
    public int timeout;   // 超时计数器（0 = 无请求，>0 = 待处理）
}
```

---

> **文档版本**: 1.0 | **最后更新**: 2026-07-15  
> **维护者**: TsWeb 项目组  
> **本文档对应源码版本**: TeleportRequest v1.2.0
