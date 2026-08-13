# StatusPanel — 服务器信息面板

在客户端固定屏幕位置显示一个**持久文本框**（服务器名 / 在线人数），纯服务端实现，所有原版客户端可见。

## 显示效果

```
⭐建筑服                               （[i:3525] 图标 + [c/4DABF7] 蓝色，写死）
在线人数：12人                          （实时刷新）
```

## 位置原理（抓包实证，非猜测）

客户端把 9 号 StatusText 绘制在**固定锚点**（`x 中心 ≈ 628 + (屏宽-800)`，`y=84`）：

```csharp
Vector2 position = new Vector2(
    628f - 文本总宽/2 + (screenWidth - 800),
    84f);
```

**关键技巧**：给每行行尾补大量空格把文本块撑宽 → 文本块中心被强制对齐到固定锚点 → **可视文字被推到屏幕中上部**，视觉上就是"玩家正上方"，不再缩在右上角与地图重叠。这是从真实服务器抓包（`scripts/抓包节选.txt`）反向得到的实现：对方就是 9 号 StatusText 包 + 行尾空格撑宽 + 图标/颜色排版。

## 实现机制

- 每帧（`GameUpdate`）向所有在线玩家发送 **9 号 StatusText 包**：
  `TSPlayer.SendData(PacketTypes.Status, 富文本, 0, 0x1f)`
- 客户端收到写入 `Netplay.Connection.StatusText`，持续绘制；服务器持续发送即保持显示

## 可调项

| 项 | 位置 | 说明 |
|----|------|------|
| `ServerLine` | 常量 | 服务器名行，`[i:图标][c/颜色:名字]`（写死） |
| `SpacerWidth` | 常量 | **行尾空格数**：越大文本块越宽 → 可视文字越靠近屏幕中部；屏幕越宽需越大，过大偏出左屏 |

## 构建与部署

```bash
cd plugin-son/statuspanel
dotnet build -c Release
```

产物：`bin/Release/net9.0/statuspanel.dll`，放入 TShock 的 `ServerPlugins/` 目录，重启或热加载生效。
