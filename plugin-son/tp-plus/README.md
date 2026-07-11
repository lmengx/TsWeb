# TeleportRequest - 增强传送管理

TShock 传送管理系统，支持三模式传送：**允许 / 需同意 / 拒绝**，覆盖原生 `/tp` 指令。

---

## 指令

| 指令 | 说明 | 权限 |
|------|------|------|
| `/tp <玩家>` | 传送到目标玩家。有 `tshock.tp.override` 则无视模式直接传 | 所有人可用 |
| `/tpa` | 同意传送请求 | 所有人可用 |
| `/tpd` | 拒绝传送请求 | 所有人可用 |
| `/tpmode` | 查看/设置传送模式 | 所有人可用 |
| `/tpmode agree` | 允许任何人传送（默认） | 所有人可用 |
| `/tpmode request` | 需同意才可传送 | 所有人可用 |
| `/tpmode block` | 拒绝所有人传送 | `tshock.tp.block` |

> `/tpmode` 支持首字母简写：`a`/`ag` = agree, `r`/`re`/`req` = request, `b`/`bl`/`blo` = block

---

## 传送模式详解

### agree（允许）

目标为 `agree` 模式时，任何人使用 `/tp <你>` 会**直接传送**到你身边，无需确认。

```
/tp Alice  →  Alice 模式为 agree  →  直接传送
```

### request（需同意）

目标为 `request` 模式时，传送会转为**请求**，目标需用 `/tpaccept` 确认或用 `/tpdeny` 拒绝，超时自动取消。

```
/tp Alice  →  Alice 模式为 request  →  发送请求
                                    →  Alice: /tpaccept   ✓  传送成功
                                    →  Alice: /tpdeny     ✗  拒绝
                                    →  超时               ✗  取消
```

### block（拒绝）

目标为 `block` 模式时，任何人的传送请求都会被**直接拒绝**，提示"目标玩家拒绝了传送请求"。

```
/tp Alice  →  Alice 模式为 block  →  "目标玩家拒绝了传送请求"
```

> `block` 模式需要 `tshock.tp.block` 权限，适合管理员/受保护玩家使用。

---

## 权限

| 权限节点 | 说明 | 默认拥有 |
|----------|------|----------|
| `tshock.tp.override` | 绕过所有模式检查，直接传送 | 管理员组 |
| `tshock.tp.block` | 允许设置 `block`（拒绝）模式 | 管理员组 |

---

## 配置

配置文件位于 `TShock/tpconfig.json`：

```json
{
  "Interval": 3,
  "Timeout": 3
}
```

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Interval` | int | 3 | 提醒间隔（秒） |
| `Timeout` | int | 3 | 超时次数，总超时 = Interval × Timeout |

> 修改配置后执行 `/reload` 即可生效。

---

## 数据持久化

玩家传送模式存储在 `TShock/tpplus.json`，仅保存非默认模式：

```json
{
  "5": "Request",
  "10": "Block"
}
```

- **Key**: 玩家 ID
- **Value**: 模式名（`Request` / `Block`）
- **默认**: 未记录的玩家均为 `Agree`（允许），不写入文件以节省 IO
- **线程安全**: 读写均有锁保护

---

## 生命周期

| 操作 | 行为 |
|------|------|
| 插件加载 | 注册命令、读取配置、启动定时器 |
| 插件卸载（`/卸载` / 关服） | 停止定时器、清理命令、注销事件 |
| 热重载（`/reload`） | 重新读取配置和模式存储、重启定时器、清空待处理请求 |

---

## 构建

```bash
cd plugin-son/tp-plus/TeleportRequest
dotnet restore
dotnet build
```

输出：`bin/Debug/net9.0/TeleportRequest.dll`

### 依赖

- [TShock](https://www.nuget.org/packages/TShock/) 6.1.0+
- .NET 9.0 SDK

---

## 与旧版差异

| 项目 | 旧版 | 新版 |
|------|------|------|
| 目标框架 | .NET Framework 4.0 | .NET 9.0 |
| 引用方式 | 本地 DLL | NuGet `TShock` 6.1.0 |
| 玩家查找 | `TShock.Utils.FindPlayer()` | `TSPlayer.FindByNameOrID()` |
| API 版本 | `1.16` | `2.1` |
| 核心指令 | `/tpa` / `/tpahere` | `/tp`（统一入口） |
| 模式设置 | `/tpautodeny`（开关） | `/tpmode`（三模式） |
| 持久化 | 无（内存仅开关） | `tpplus.json`（线程安全） |
| 热重载 | 不支持 | `/reload` 全量刷新 |
