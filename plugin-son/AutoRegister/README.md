# AutoRegister — 灵活进服控制-（自动注册/白名单）

## 功能说明

提供四种注册模式，支持动态切换、UUID 验证、密码挑战、REST API 管理，无需重启服务器。

---

## 注册模式

| 模式 | 说明 | 备注 |
|------|------|---------------------|
| `default` | 默认模式，保留 TShock 原有注册逻辑 | 什么都不修改 |
| `auto` | 自动注册模式，新玩家加入时自动创建账户并登录 | 密码随机 |
| `disable` | 禁用注册模式，禁止任何注册 | 建议直接使用block模式 |
| **`block`** | **拦截模式 — 阻止未注册/UUID不匹配玩家进入** | 防止未注册即可捣乱的外挂 |

### Block 拦截模式详解

```
玩家进入服务器
  ├─ 已注册 + UUID 匹配 → ✅ 放行（TShock正常处理）
  ├─ 已注册但 UUID 不匹配 → 🔐 弹密码框验证（防盗号）
  └─ 未注册 → ❌ 直接踢出
```

**Block 模式适用场景：**
- 只允许已注册玩家进入的私服
- 防止账号盗用（UUID 不匹配时强制密码验证）
- 完全替换白名单的功能

---

## 配置文件

配置文件路径：`/TShock/AutoRegister/config.json`

```json
{
  "mode": "default"
}
```

### 配置选项

| 选项 | 类型 | 说明 |
|------|------|------|
| `mode` | string | 注册模式：`default` / `auto` / `disable` / `block` |

---

## 命令

| 命令 | 说明 | 权限 |
|------|------|------|
| `/autoregister` | 查看当前状态和帮助信息 | `tshock.admin` |
| `/autoregister mode <模式>` | 设置注册模式 | `tshock.admin` |
| `/autoregister reload` | 重新加载配置文件 | `tshock.admin` |
| `/autoregister status` | 查看当前状态 | `tshock.admin` |
| `/ar` | `/autoregister` 的简写 | `tshock.admin` |

### 使用示例

```
# 查看状态和帮助
/autoregister

# 设置为自动注册模式
/autoregister mode auto

# 设置为拦截模式（阻止未注册玩家进入）
/autoregister mode block

# 设置为禁用注册模式
/autoregister mode disable

# 恢复默认模式
/autoregister mode default

# 重新加载配置文件
/autoregister reload
```

---

## REST API（Web 管理面板）

插件注册了 REST API，可供 Web 管理面板在线管理配置。

| 接口 | 方法 | 权限 | 说明 |
|------|------|------|------|
| `/data/config/tsweb` | GET | 公开 | 获取当前配置 |
| `/data/config/tsweb/set` | POST | `data.rest.invsee` | 修改配置 |

### GET 响应示例

```json
{
  "status": "200",
  "mode": "auto"
}
```

### POST 参数

| 参数 | 类型 | 说明 |
|------|------|------|
| `mode` | string | `default` / `auto` / `disable` / `block` |

---

## 安全特性

1. **UUID 绑定**：自动注册时会绑定玩家 UUID，防止账户冒用
2. **Block 留有登录接口**：UUID 不匹配时给出密码框验证，防止换设备玩家登不上
3. **权限控制**：所有管理命令需要 `tshock.admin` 权限
4. **日志记录**：所有操作都会记录到控制台日志

## 注意事项

1. 模式切换即时生效，无需重启服务器
2. 配置文件会自动创建，首次启动时默认为 `default` 模式
3. 自动注册的玩家密码为随机生成的 GUID，无需玩家记忆
4. Block 模式下，UUID 不匹配的已注册玩家会收到密码框，验证通过后自动更新 UUID
