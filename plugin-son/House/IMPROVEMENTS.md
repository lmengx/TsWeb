# House 插件优化方案

## 一、命令简化：`/h` 快捷指令

### 1.1 注册

```csharp
Commands.ChatCommands.Add(new Command("house.use", HCommands, "house") { ... });
Commands.ChatCommands.Add(new Command("house.use", HCommands, "h")     { ... });
```

### 1.2 命令总览

```
/h                      → 帮助（房屋列表 + 引导）
/h help                 → 同上
/h c                    → 圈地二级说明
/h c 1 / 2 / clear      → 选点 / 清除
/h c 屋名               → 创建房屋
/h settings [屋名]      → 查看设置（缺省=当前房屋）
/h [屋名] 参数 0/1      → 修改权限（缺省=当前房屋）
/h tp [屋名]            → 传送
/h showme               → 切换：自己房屋进入时自动边框
/h showothers           → 切换：他人房屋进入时自动边框
/h list [页码]          → 列表
/h allow 玩家 屋名      → 添加共有者
/h disallow 玩家 屋名   → 移除共有者
/h adduser 玩家 屋名    → 添加使用者
/h deluser 玩家 屋名    → 移除使用者
/h delete 屋名          → 删除
/h redefine 屋名        → 重定义
/h name                 → 查看所属
/h editmsg [屋名] 0/1 [0/1] → 通知设置
/h settp [屋名] [x] [y]     → 设置传送点
/h setexpel [屋名] [x] [y]  → 设置驱离点
```

---

## 二、`/h` 帮助输出

### 2.1 格式

**第一行——房屋列表（彩色）：**

遍历用户拥有的所有房屋（Author/Owner/User），每个房屋用不同颜色：

```
你的房屋: [我的小屋] [城堡] [仓库]
          亮黄      亮青    粉紫
```

- 无房屋时显示 `你还没有房屋，使用 /h c 创建一个吧`
- 颜色轮换：`Color.LightYellow`, `Color.Cyan`, `Color.MediumPurple`, `Color.Lime`, `Color.Orange`, `Color.Pink`

**第二行——引导：**

```
/h c 圈地  |  /h settings 查看设置  |  /h tp <屋名> 传送
```

### 2.2 `/h c` 二级说明（无参数时）

```
/h c 1      — 设置左上角点（敲击方块）
/h c 2      — 设置右下角点（敲击方块）
/h c 屋名   — 完成圈地，创建房屋
/h c clear  — 清除已选的点
```

解析：第二个参数是 `"1"`/`"2"` → `HandleSet`，`"clear"` → 清除，其他 → 当作屋名调 `HandleAdd`。

---

## 三、`/h settings` 查看设置

### 3.1 行为

- 缺省屋名 → 当前所在房屋
- 不在任何房屋内 → `请站在房屋内或指定屋名: /h settings 屋名`
- 站在房屋内但不是自己的 → 仅显示，提示 `你无权修改此房屋设置`
- 是自己的 → 显示彩色面板

### 3.2 输出格式（分颜色）

```
━━━ 我的小屋 ━━━                     ← 金色
房主: PlayerA                        ← 白色
共有者: PlayerB, PlayerC             ← 白色
使用者: PlayerD                      ← 白色
区域: 30×20  传送点: (115,200)       ← 白色

通知设置                               ← 灰色
  破坏通知: ●开  ○关                 ← 绿/红
  进入通知: ○开  ●关

权限设置  (/h 参数 0/1 修改)          ← 灰色
  允许进入  ●     放置  ○     破坏  ○
  传送      ○     液体  ○     箱子  ○
  植物      ○     复活  ○     挖坟  ●
  开关      ●     门    ●     易碎  ●
  违规驱离  ○
                                     ●=允许(绿) ○=禁止(红)
```

- `●` 亮绿 `Color.Lime`，`○` 红 `Color.Red`
- 底部灰色引导 `/h 参数 0/1 修改`

---

## 四、`/h [屋名] 参数 0/1` 修改权限

`/h settings` 只查看不修改。修改仍然走 `/h [屋名] 参数 0/1`。

参数表：`允许进入`、`允许传送`、`放置`、`破坏`、`液体`、`箱子`、`植物`、`设置复活点`、`挖坟`、`开关`、`门`、`易碎品`、`违规驱离`。

---

## 五、边框显示重构

### 5.1 旧问题

`ToggleHouseDisplay` 是 toggle。`OnUpdate` 进入时调它 → 第二次进入同一房屋把边框关了。

### 5.2 新设计

去掉 `/h show`、`/h showall`、`/h auto`，改为：

| 命令 | 作用 |
|------|------|
| `/h show` | 查看边框显示状态和用法（纯提示，不修改） |
| `/h showme` | 切换：自己房屋进入时自动边框 |
| `/h showothers` | 切换：他人房屋进入时自动边框 |

**默认值：** `showme=关`，`showothers=开`。

#### `/h show` 输出格式

```
━━━ 边框显示 ━━━
自己房屋: ●开  ○关     ← 当前值亮绿，另一个红
他人房屋: ○开  ●关

/h showme     — 切换自己房屋自动边框
/h showothers — 切换他人房屋自动边框
```

### 5.3 持久化：`houseshow.json`

**文件路径：** `tshock/HouseRegion/houseshow.json`

```json
{
  "123": { "showMe": true },
  "456": { "showOthers": false }
}
```

- `showMe` 默认 `false` → 不存
- `showOthers` 默认 `true` → 不存
- 只存改了默认值的玩家，回到默认值时删除键

```csharp
public class PlayerShowPref
{
    public bool ShowMe { get; set; } = false;
    public bool ShowOthers { get; set; } = true;
}

public static class ShowPrefManager
{
    private static Dictionary<string, PlayerShowPref> _data = new();
    private static readonly string FilePath = 
        Path.Combine(TShock.SavePath, "HouseRegion", "houseshow.json");

    public static bool GetShowMe(string id)
        => _data.TryGetValue(id, out var p) ? p.ShowMe : false;

    public static bool GetShowOthers(string id)
        => _data.TryGetValue(id, out var p) ? p.ShowOthers : true;

    public static bool ToggleShowMe(string id)
    {
        var cur = GetShowMe(id);
        Set(id, showMe: !cur);
        return !cur;
    }

    public static bool ToggleShowOthers(string id)
    {
        var cur = GetShowOthers(id);
        Set(id, showOthers: !cur);
        return !cur;
    }

    private static void Set(string id, bool? showMe = null, bool? showOthers = null)
    {
        if (!_data.ContainsKey(id)) _data[id] = new PlayerShowPref();
        if (showMe.HasValue) _data[id].ShowMe = showMe.Value;
        if (showOthers.HasValue) _data[id].ShowOthers = showOthers.Value;
        // 回到默认值 → 删键
        if (!_data[id].ShowMe && _data[id].ShowOthers)
            _data.Remove(id);
        Save();
    }

    public static void Load()  { /* 读 JSON */ }
    public static void Save()  { /* 写 JSON */ }
}
```

### 5.4 OnUpdate 进入逻辑

```csharp
// 进入房屋
if (currentHouse != null && lastHouse == null)
{
    bool isMine = Utils.IsAuthorized(ts, currentHouse);
    string myId = ts.Account.ID.ToString();

    if ((isMine && ShowPrefManager.GetShowMe(myId)) ||
        (!isMine && ShowPrefManager.GetShowOthers(myId)))
    {
        ShowHouseDisplay(ts, currentHouse);
    }
    // ...
}

// 离开房屋
if (currentHouse == null && lastHouse != null)
{
    HideHouseDisplay(ts, lastHouse);
    // ...
}
```

### 5.5 ShowHouseDisplay / HideHouseDisplay（非 toggle）

```csharp
public static void ShowHouseDisplay(TSPlayer player, House house)
{
    if (!PlayerActiveHouses.TryGetValue(player.Index, out var list))
    {
        list = new List<Rectangle>();
        PlayerActiveHouses[player.Index] = list;
        StartRefreshCycle(player.Index);
    }
    if (!list.Contains(house.HouseArea))
    {
        list.Add(house.HouseArea);
        ShowRegion(player, house.HouseArea);
    }
}

public static void HideHouseDisplay(TSPlayer player, House house)
{
    if (PlayerActiveHouses.TryGetValue(player.Index, out var list))
    {
        if (list.Remove(house.HouseArea))
        {
            ClearRegionProjectiles(player, house.HouseArea);
            if (list.Count == 0) PlayerRefreshFlags.Remove(player.Index);
        }
    }
}
```

### 5.6 刷新优化

```csharp
// GetRefreshEnumerator 内，每次刷新先清再画
foreach (var rect in list)
{
    ClearRegionProjectiles(player, rect);
    ShowRegion(player, rect);
}
```

---

## 六、实现步骤

| 步骤 | 内容 | 涉及文件 |
|:---:|------|----------|
| 1 | 注册 `/h` 别名 | Plugin.cs |
| 2 | `case "c":` 路由圈地（1/2/clear/屋名），无参→二级说明 | Plugin.cs |
| 3 | `case "settings":` 路由彩色设置查看 | Plugin.cs |
| 4 | `case "showme":` / `case "showothers":` toggle | Plugin.cs |
| 5 | 重写 `HandleHelp` 两行格式 | Plugin.cs |
| 6 | 新建 `ShowPrefManager` + `houseshow.json` | ShowPrefManager.cs |
| 7 | `ShowHouseDisplay` / `HideHouseDisplay`（非 toggle） | PacketReceive.cs |
| 8 | `OnUpdate` 进入/离开用新方法 + ShowPref 判断 | Plugin.cs |
| 9 | 刷新周期加先清后画 | PacketReceive.cs |
| 10 | 删除旧的 `ToggleAllDisplays`、`AutoShowPlayers` | PacketReceive.cs + Plugin.cs |
