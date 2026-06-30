# 灵活注册 (Flexible Registration )

## 功能说明

提供三种注册模式，支持动态切换，无需重启服务器。

## 注册模式

| 模式 | 说明 | `/register` 命令行为 |
|------|------|---------------------|
| `default` | 默认模式，保留 TShock 原有注册逻辑 | 正常执行注册 |
| `auto` | 自动注册模式，新玩家加入时自动创建账户并登录 | 提示"无需手动注册" |
| `disable` | 禁用注册模式，禁止任何注册 | 提示"注册已禁用" |

## 配置文件

配置文件路径：`/TShock/TSWeb/config.json`

```json
{
  "mode": "default"
}
```

### 配置选项

| 选项 | 类型 | 说明 |
|------|------|------|
| `mode` | string | 注册模式：`default` / `auto` / `disable` |

## 命令

| 命令 | 说明 | 权限 |
|------|------|------|
| `/autoregister` | 查看当前状态和帮助信息 | `tshock.admin` |
| `/autoregister mode <模式>` | 设置注册模式 | `tshock.admin` |
| `/autoregister reload` | 重新加载配置文件 | `tshock.admin` |
| `/autoregister status` | 查看当前状态 | `tshock.admin` |
| `/ar` | `/autoregister` 的简写 | `tshock.admin` |

## 使用示例

### 查看状态和帮助

```
/autoregister
```

### 设置为自动注册模式

```
/autoregister mode auto
```

### 设置为禁用注册模式

```
/autoregister mode disable
```

### 恢复默认模式

```
/autoregister mode default
```

### 重新加载配置文件

```
/autoregister reload
```

## 工作流程

### 默认模式 (default)

```
玩家加入服务器
       ↓
使用 /register 命令手动注册
       ↓
使用 /login 命令登录
```

### 自动注册模式 (auto)

```
玩家加入服务器
       ↓
检查是否存在同名账户
       ↓
┌─────────────┬─────────────────┐
│ 账户存在    │ 账户不存在       │
├─────────────┼─────────────────┤
│ TShock自动  │ 自动创建账户     │
│ UUID登录    │ → 自动登录       │
└─────────────┴─────────────────┘
玩家直接进入游戏
```

### 禁用注册模式 (disable)

```
玩家加入服务器
       ↓
无账户玩家无法注册
       ↓
只能使用已有账户登录
```

## 安全特性

1. **UUID 绑定**：自动注册时会绑定玩家 UUID，防止账户冒用
2. **权限控制**：所有管理命令需要 `tshock.admin` 权限
3. **日志记录**：所有操作都会记录到控制台日志

## 注意事项

1. 模式切换即时生效，无需重启服务器
2. 配置文件会自动创建，首次启动时默认为 `default` 模式
3. 自动注册的玩家密码为随机生成的 GUID，无需玩家记忆
4. 需要确保服务器已引用 `Newtonsoft.Json` 库