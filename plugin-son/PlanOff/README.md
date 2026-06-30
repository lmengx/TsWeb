# 计划关闭功能 (PlannedOff)

## 功能说明

当服务器无玩家持续一定时间后自动关闭服务器。

## 命令

| 命令 | 说明 | 权限 |
|------|------|------|
| `/planoff <时间>` | 设置计划关闭时间（单位：秒） | `tools.planoff` |
| `/planoff cancel` | 取消当前计划关闭 | `tools.planoff` |
| `/planoff` | 查看当前状态和剩余时间 | `tools.planoff` |


## 用法：自动重启

服务器关闭后自动重新启动，可以使用批处理脚本实现：

### 创建启动脚本 `start_server.bat`

```batch
@echo off
:start
echo [%date% %time%] 正在启动服务器...
Tshock.Server.exe -port 7777 -world "path/to/worlds/worldname.wld"
echo [%date% %time%] 服务器已关闭，等待5秒后重启...
timeout /t 5
goto start
```

### 说明

- `Tshock.Server.exe -port 7777 -world "path/to/worlds/worldname.wld"` - 启动服务器命令
- `timeout /t 5` - 关闭后等待5秒
- `goto start` - 循环回到开头，实现自动重启

### 使用方法

1. 将 `start_server.bat` 放在 `Tshock.Server.exe` 同目录下
2. 双击 `start_server.bat` 启动
3. 服务器关闭后会自动重新启动

## 工作流程

1. 管理员执行 `/planoff 300`（例如设置300秒）
2. 如果当前无玩家，立即开始计时
3. 玩家退出时：
   - 如果剩余玩家数 ≤ 1（钩子触发时没有退出故填1），开始倒计时
4. 计时期间有玩家加入：
   - 暂停计时
   - 广播通知
5. 计时结束前再次确认玩家数量：
   - 如果有玩家，取消关闭
   - 如果无玩家，执行 `/off` 关闭服务器