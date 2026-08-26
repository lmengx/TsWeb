# antiangel —— 检测 TerraAngel 修改客户端并踢出

> 适用：Terraria **1.4.5.7** + TShock 6.x 服务器（TShockAPI ≥6.1 / OTAPI3）
> 类型：独立 TShock 插件（可独立加载，不依赖 TSWeb）
> 来源：提取自 `参考源码/TShockPlugin-master/src/ServerTools` 的 `ModifyClientDetect`（经实测确认为反 TerraAngel 专用），独立成插件并适配本服环境

---

## 一、检测原理（指纹已用 TA 客户端源码逐字段实证）

### TerraAngel 的特征行为

TerraAngel 客户端开启「隐藏存在感广播」（`ClientConfig.BroadcastPresence`）后，会周期性发送 `PlayerControls(13)` 包，并在包的 `netCameraTarget` 字段写入**魔数坐标**：

```csharp
// TerraAngel.Net.PacketBuilderExtensions.WritePlayerControlsPacketWithHiddenPresenceMessage()
new Vector2(-114514, -1919810)   // 十六进制 = 0xFFFE40AE / 0xFFE2B4BE
```

正常客户端**永远不可能**发送这个值 → 命中即铁证。

### TA 发送的 PlayerControls 包布局（源码实证）

```
[start+1] playerIndex      [start+2] controlFlags    [start+3] movementFlags
[start+4] miscFlags        [start+5] extraFlags      [start+6] selectedItem
[start+7..14] position(XY)
[可选] movementFlags[2] → velocity(8B)      [可选] movementFlags[7] → mount.Type(2B)
[可选] miscFlags[6]     → 归返药水两位置(16B) [可选] extraFlags[5] → netCameraTarget(8B)
```

### 检测算法

挂钩 `Terraria.MessageBuffer.GetData`（MonoMod detour，运行时自适应 2 参/3 参签名），在每个上行包进入游戏逻辑前：

1. **包 ID == 13（PlayerControls）**：按 TA 布局解析 `movementFlags/miscFlags/extraFlags` 三个标志字节；
   - 若 `extraFlags[5]`（netCameraTarget 有值）置位：
   - 计算可选字段偏移 `optional = velocity(8) + mount(2) + 归返(16)`，跳到 netCameraTarget 位置读 `Vector2`
   - 匹配魔数 `(-114514, -1919810)` → **判定 TerraAngel**
2. **包 ID == 201**（原版 `MessageID.Count=162`，正常客户端永不发送的保留号）→ 判定

命中后：控制台警告 + 全服红字广播 + 踢出（均可配置）。

### 指纹隐藏（防特征扫描）

目标值**不以明文存在于 dll**：88 字节数组经 XOR 派生 32 字节 Salt，目标值以 HMAC-SHA256 哈希形式存放。静态扫描 dll 无法直接提取特征值，防止"扫到魔数 → 一键改掉绕过"。

---

## 二、部署

1. 编译：`dotnet build plugin-son/antiangel/antiangel.csproj -c Release`
2. 将 `bin/Release/net9.0/antiangel.dll` 放入服务器 `ServerPlugins/` 目录
3. **重启服务器进程**（插件加载需重启）
4. 首次启动自动生成配置 `tshock/antiangel/config.json`

## 三、配置

| 键 | 默认 | 说明 |
|---|---|---|
| `enabled` | `true` | 检测总开关 |
| `kick` | `true` | 命中后踢出玩家（false = 仅警告/广播，不踢） |
| `kickText` | `检测到使用 TerraAngel 修改客户端，已被踢出` | 踢出原因 |
| `broadcast` | `true` | 命中时全服红字广播警告 |

## 四、命令

| 命令 | 权限 | 说明 |
|---|---|---|
| `/antiangel` | `antiangel.admin` | 查看当前状态与用法 |
| `/antiangel on` | `antiangel.admin` | 开启检测 |
| `/antiangel off` | `antiangel.admin` | 关闭检测 |
| `/antiangel reload` | `antiangel.admin` | 重新加载配置文件 |

## 五、启动日志验证

```
[antiangel] GetData detour 已挂载（3 参签名）   ← 1.4.5.7 正常走 3 参
[antiangel] 已卸载                               ← 热卸载时
```

TA 客户端触发隐藏存在感广播时：

```
[antiangel] 玩家 XXX 使用 TerraAngel 修改客户端进入服务器！
```

## 六、误报与兼容性说明

- **误报率≈0**：魔数 `-114514/-1919810` 为负百万级坐标，正常玩家的任何包字段都不可能取该值；且检测只在 `extraFlags[5]`（netCameraTarget）置位时读取
- **版本适配**：原 ServerTools 为 1.4.4.x 时代 IL 注入实现，IL 模式在 1.4.5.7 可能失配；本插件改用 MonoMod RuntimeDetour 挂钩，并对 GetData 做 2 参/3 参签名运行时自适应，1.4.5.7 稳定生效
- **长度防护**：读取前校验包边界，解析异常仅跳过不误伤、绝不干扰网络处理
- **绕过成本**：TA 用户只需关闭「隐藏存在感广播」即可规避指纹①（但指纹② 包 201 仍可命中）；魔数可被 TA 更新版本改动 → 届时需同步更新本插件指纹（见 `Detector.cs` 中 `_data` 数组）

## 七、与 TSWeb 的关系

独立插件，与 TSWeb 主插件互不依赖，可单独使用。若 TSWeb 后续需要集成此检测，可将 `Detector.cs` 并入 `plugin/`。
