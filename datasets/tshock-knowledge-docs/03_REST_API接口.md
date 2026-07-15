# TShock REST API 接口文档

## API 概述

### 基础信息
- **协议**: HTTP/HTTPS
- **数据格式**: JSON
- **认证方式**: Token 认证
- **基础路径**: `/api/`

### 认证机制

所有 API 请求都需要携带有效的认证 Token：

```
GET /api/v2/endpoint?token=YOUR_TOKEN
```

**Token 获取**：
- 在服务器配置文件中创建 REST Token
- 使用 `rest` 命令管理 Token
- 每个 Token 关联特定的权限

**权限系统**：
API 权限通过 TShock 权限节点控制：
- `tshock.rest.bans.manage` - 封禁管理
- `tshock.rest.bans.view` - 封禁查看
- `tshock.rest.groups.manage` - 组管理
- `tshock.rest.groups.view` - 组查看
- `tshock.rest.kick` - 踢出玩家
- `tshock.rest.kill` - 杀死玩家
- `tshock.rest.mute` - 禁言玩家
- `tshock.rest.command` - 执行命令

## API 版本

TShock 提供多个 API 版本：
- **V2**: 稳定版本，基础功能
- **V3**: 增强版本，更多功能
- **V4**: 最新版本，完整功能

## 封禁管理 API

### 1. 创建封禁 (BanCreateV3)

**端点**: `/v3/bans/create`

**方法**: GET

**权限**: `tshock.rest.bans.manage`

**参数**:
- `identifier` (必填): 要封禁的标识符（IP 或用户名）
- `reason` (可选): 封禁原因
- `start` (可选): 封禁开始时间（日期时间字符串）
- `end` (可选): 封禁结束时间（日期时间字符串）
- `token` (必填): REST Token

**示例请求**:
```http
GET /v3/bans/create?identifier=player1&reason=违规行为&start=2024-01-01T00:00:00&end=2024-12-31T23:59:59&token=YOUR_TOKEN
```

**响应示例**:
```json
{
  "status": "success",
  "ban": {
    "ticketNumber": "12345",
    "identifier": "player1",
    "reason": "违规行为",
    "start": "2024-01-01T00:00:00",
    "end": "2024-12-31T23:59:59"
  }
}
```

### 2. 删除封禁 (BanDestroyV3)

**端点**: `/v3/bans/destroy`

**方法**: GET

**权限**: `tshock.rest.bans.manage`

**参数**:
- `ticketNumber` (必填): 封禁票号
- `fullDelete` (可选): 是否完全删除记录（布尔值）
- `token` (必填): REST Token

**示例请求**:
```http
GET /v3/bans/destroy?ticketNumber=12345&fullDelete=true&token=YOUR_TOKEN
```

### 3. 查看封禁详情 (BanInfoV3)

**端点**: `/v3/bans/read`

**方法**: GET

**权限**: `tshock.rest.bans.view`

**参数**:
- `ticketNumber` (必填): 封禁票号
- `token` (必填): REST Token

**示例请求**:
```http
GET /v3/bans/read?ticketNumber=12345&token=YOUR_TOKEN
```

### 4. 查看封禁列表 (BanListV3)

**端点**: `/v3/bans/list`

**方法**: GET

**权限**: `tshock.rest.bans.view`

**参数**:
- `token` (必填): REST Token

**示例请求**:
```http
GET /v3/bans/list?token=YOUR_TOKEN
```

**响应示例**:
```json
{
  "status": "success",
  "bans": [
    {
      "ticketNumber": "12345",
      "identifier": "player1",
      "reason": "违规行为",
      "start": "2024-01-01T00:00:00",
      "end": "2024-12-31T23:59:59"
    }
  ]
}
```

## 组管理 API

### 1. 创建组 (GroupCreate)

**端点**: `/v2/groups/create`

**方法**: GET

**权限**: `tshock.rest.groups.manage`

**参数**:
- `group` (必填): 组名
- `parent` (可选): 父组名
- `permissions` (可选): 权限列表（逗号分隔）
- `chatcolor` (可选): 聊天颜色（RGB 格式：r,g,b）
- `token` (必填): REST Token

**示例请求**:
```http
GET /v2/groups/create?group=vip&parent=default&permissions=tshock.tp.self,tshock.tp.others&chatcolor=255,215,0&token=YOUR_TOKEN
```

### 2. 删除组 (GroupDestroy)

**端点**: `/v2/groups/destroy`

**方法**: GET

**权限**: `tshock.rest.groups.manage`

**参数**:
- `group` (必填): 组名
- `token` (必填): REST Token

**示例请求**:
```http
GET /v2/groups/destroy?group=vip&token=YOUR_TOKEN
```

### 3. 查看组信息 (GroupInfo)

**端点**: `/v2/groups/read`

**方法**: GET

**权限**: `tshock.rest.groups.view`

**参数**:
- `group` (必填): 组名
- `token` (必填): REST Token

**示例请求**:
```http
GET /v2/groups/read?group=vip&token=YOUR_TOKEN
```

**响应示例**:
```json
{
  "status": "success",
  "group": {
    "name": "vip",
    "parent": "default",
    "permissions": ["tshock.tp.self", "tshock.tp.others"],
    "chatColor": [255, 215, 0]
  }
}
```

### 4. 查看组列表 (GroupList)

**端点**: `/v2/groups/list`

**方法**: GET

**权限**: `tshock.rest.groups.view`

**参数**:
- `token` (必填): REST Token

**示例请求**:
```http
GET /v2/groups/list?token=YOUR_TOKEN
```

## 玩家管理 API

### 1. 踢出玩家 (PlayerKickV2)

**端点**: `/v2/players/kick`

**方法**: GET

**权限**: `tshock.rest.kick`

**参数**:
- `player` (必填): 玩家名称
- `reason` (可选): 踢出原因
- `token` (必填): REST Token

**示例请求**:
```http
GET /v2/players/kick?player=player1&reason=违规行为&token=YOUR_TOKEN
```

### 2. 杀死玩家 (PlayerKill)

**端点**: `/v2/players/kill`

**方法**: GET

**权限**: `tshock.rest.kill`

**参数**:
- `player` (必填): 玩家名称
- `from` (可选): 凶手名称
- `token` (必填): REST Token

**示例请求**:
```http
GET /v2/players/kill?player=player1&from=admin&token=YOUR_TOKEN
```

### 3. 禁言玩家 (PlayerMute)

**端点**: `/v2/players/mute`

**方法**: GET

**权限**: `tshock.rest.mute`

**参数**:
- `player` (必填): 玩家名称
- `token` (必填): REST Token

**示例请求**:
```http
GET /v2/players/mute?player=player1&token=YOUR_TOKEN
```

### 4. 解除禁言 (PlayerUnMute)

**端点**: `/v2/players/unmute`

**方法**: GET

**权限**: `tshock.rest.mute`

**参数**:
- `player` (必填): 玩家名称
- `token` (必填): REST Token

**示例请求**:
```http
GET /v2/players/unmute?player=player1&token=YOUR_TOKEN
```

### 5. 玩家列表 (PlayerList)

**端点**: `/lists/players`

**方法**: GET

**权限**: 无特殊权限要求

**参数**:
- `token` (必填): REST Token

**示例请求**:
```http
GET /lists/players?token=YOUR_TOKEN
```

**响应示例**:
```json
{
  "status": "success",
  "players": ["player1", "player2", "player3"]
}
```

### 6. 详细玩家列表 (PlayerListV2)

**端点**: `/v2/players/list`

**方法**: GET

**权限**: 无特殊权限要求

**参数**:
- `token` (必填): REST Token

**示例请求**:
```http
GET /v2/players/list?token=YOUR_TOKEN
```

**响应示例**:
```json
{
  "status": "success",
  "players": [
    {
      "name": "player1",
      "id": 1,
      "group": "default",
      "ip": "127.0.0.1",
      "position": {"x": 100, "y": 200}
    }
  ]
}
```

### 7. 查看玩家信息 (PlayerReadV3)

**端点**: `/v3/players/read`

**方法**: GET

**权限**: `tshock.rest.users.info`

**参数**:
- `player` (必填): 玩家名称
- `token` (必填): REST Token

**示例请求**:
```http
GET /v3/players/read?player=player1&token=YOUR_TOKEN
```

### 8. 查看玩家信息 V4 (PlayerReadV4)

**端点**: `/v4/players/read`

**方法**: GET

**权限**: `tshock.rest.users.info`

**参数**:
- `player` (必填): 玩家名称
- `token` (必填): REST Token

**示例请求**:
```http
GET /v4/players/read?player=player1&token=YOUR_TOKEN
```

## 服务器管理 API

### 1. 广播消息 (ServerBroadcast)

**端点**: `/v2/server/broadcast`

**方法**: GET

**权限**: 无特殊权限要求

**参数**:
- `msg` (必填): 广播消息内容
- `token` (必填): REST Token

**示例请求**:
```http
GET /v2/server/broadcast?msg=服务器将在5分钟后重启&token=YOUR_TOKEN
```

### 2. 执行命令 (ServerCommandV3)

**端点**: `/v3/server/command`

**方法**: GET

**权限**: `tshock.rest.command`

**参数**:
- `cmd` (必填): 要执行的命令
- `token` (必填): REST Token

**示例请求**:
```http
GET /v3/server/command?cmd=time set 0&token=YOUR_TOKEN
```

**响应示例**:
```json
{
  "status": "success",
  "output": "时间已设置为白天"
}
```

## 用户管理 API

### 用户注册
**端点**: `/v3/users/create`

**方法**: GET

**权限**: `tshock.rest.users.manage`

**参数**:
- `user` (必填): 用户名
- `password` (必填): 密码
- `group` (可选): 组名
- `token` (必填): REST Token

### 用户删除
**端点**: `/v3/users/destroy`

**方法**: GET

**权限**: `tshock.rest.users.manage`

**参数**:
- `user` (必填): 用户名
- `token` (必填): REST Token

### 用户信息
**端点**: `/v3/users/read`

**方法**: GET

**权限**: `tshock.rest.users.view`

**参数**:
- `user` (必填): 用户名
- `token` (必填): REST Token

### 用户列表
**端点**: `/v3/users/list`

**方法**: GET

**权限**: `tshock.rest.users.view`

**参数**:
- `token` (必填): REST Token

## 世界管理 API

### 世界信息
**端点**: `/v2/world/read`

**方法**: GET

**权限**: 无特殊权限要求

**参数**:
- `token` (必填): REST Token

**响应示例**:
```json
{
  "status": "success",
  "world": {
    "name": "MyWorld",
    "size": "large",
    "difficulty": "normal",
    "players": 5,
    "time": "12:00",
    "day": true
  }
}
```

### 世界保存
**端点**: `/v2/world/save`

**方法**: GET

**权限**: `tshock.rest.world.save`

**参数**:
- `token` (必填): REST Token

### 世界重载
**端点**: `/v2/world/reload`

**方法**: GET

**权限**: `tshock.rest.world.reload`

**参数**:
- `token` (必填): REST Token

## 错误响应

### 错误格式
```json
{
  "status": "error",
  "error": "错误描述",
  "code": "ERROR_CODE"
}
```

### 常见错误码
- `INVALID_TOKEN`: 无效的 Token
- `INSUFFICIENT_PERMISSION`: 权限不足
- `PLAYER_NOT_FOUND`: 玩家不存在
- `GROUP_NOT_FOUND`: 组不存在
- `BAN_NOT_FOUND`: 封禁记录不存在
- `INVALID_PARAMETER`: 参数错误
- `SERVER_ERROR`: 服务器内部错误

## 速率限制

API 请求受速率限制：
- 每分钟最多 60 次请求
- 超过限制返回 HTTP 429 状态码
- 响应头包含剩余请求数：`X-RateLimit-Remaining`

## 最佳实践

### 1. Token 管理
- 定期更新 Token
- 为不同应用使用不同 Token
- 不要在客户端暴露 Token

### 2. 错误处理
- 检查 HTTP 状态码
- 解析错误响应
- 实现重试机制

### 3. 性能优化
- 使用批量接口
- 缓存频繁访问的数据
- 避免频繁轮询

### 4. 安全建议
- 使用 HTTPS
- 验证所有输入
- 记录 API 调用日志
- 定期审计 Token 使用情况

## API 集成示例

### Python 示例
```python
import requests

class TShockAPI:
    def __init__(self, base_url, token):
        self.base_url = base_url
        self.token = token
    
    def get_players(self):
        url = f"{self.base_url}/lists/players"
        params = {"token": self.token}
        response = requests.get(url, params=params)
        return response.json()
    
    def kick_player(self, player_name, reason=""):
        url = f"{self.base_url}/v2/players/kick"
        params = {
            "token": self.token,
            "player": player_name,
            "reason": reason
        }
        response = requests.get(url, params=params)
        return response.json()

# 使用示例
api = TShockAPI("http://localhost:7878/api", "your_token")
players = api.get_players()
print(players)
```

### JavaScript 示例
```javascript
class TShockAPI {
  constructor(baseUrl, token) {
    this.baseUrl = baseUrl;
    this.token = token;
  }
  
  async getPlayers() {
    const url = `${this.baseUrl}/lists/players?token=${this.token}`;
    const response = await fetch(url);
    return await response.json();
  }
  
  async kickPlayer(playerName, reason = "") {
    const url = `${this.baseUrl}/v2/players/kick?token=${this.token}&player=${playerName}&reason=${reason}`;
    const response = await fetch(url);
    return await response.json();
  }
}

// 使用示例
const api = new TShockAPI("http://localhost:7878/api", "your_token");
api.getPlayers().then(players => console.log(players));
```