# CompatClient1457 —— TerraAngel 客户端侧跨版本兼容插件

让 **1.4.5.6（Terraria319）的 TerraAngel 客户端** 直接进入 **普通的 1.4.5.7（Terraria325）服务器**
（服务器**无需**安装任何兼容插件，保持纯净 1.4.5.7）。

与 `plugin-son/Compat1456`（服务器端兼容插件）方向相反、且没有其固有边界：

| 维度 | 服务器端 Compat1456 | 本插件（客户端侧） |
|------|------|------|
| 出站翻译 buffer 增长 | ❌ readBuffer 无法增长 → 入站 27/29 只能丢弃/语义级重放 | ✅ 发送数组可自由构造，27 +1B / 29 +9B 无障碍 |
| 广播包翻译 | ❌ remoteClient=-1 无法逐客户端翻译（硬伤） | ✅ 天然不存在：客户端只处理自己收到的字节流 |
| 服务器纯净度 | 服务器需挂插件 | 服务器零插件 |
| 兼容范围 | 所有旧客户端 | 仅 TerraAngel 客户端 |

## v2.0 稳定架构（解决 #reload 卡死）

**为什么之前的版本会卡死**：Harmony/MonoMod 在 .NET 5+ 的补丁都是 **native detour**
（写原生机器码跳板，`HarmonyLib.Memory.WriteJump → DetourHelper.Native` 反编译实证）。
TerraAngel 的 `#reload` 会**同步卸载插件 ALC 并重建**，卸载时写回原机器码——
若此刻网络线程正执行被补丁的方法（GetData / SendPacketToServer 高频热路径）→ **进程级卡死**。
（MonoMod 两版、Harmony 一版全部实测复现此现象）

**v2.0 的做法**：
1. 全部翻译逻辑 + Harmony prefix 委托放进 **CompatCore.dll**，由插件 `Assembly.Load(bytes)`
   加载到 **默认 ALC**——不随插件 ALC 卸载；
2. 补丁 **一次安装、永不卸载**：native 跳板从不被写回，网络线程执行永不冲突；
3. `#reload` 只切换翻译器实例（纯托管状态切换，`Translator` 替换），**彻底安全**；
4. 网络线程回调不访问任何 UI，异常只计数，主线程 `Update` 汇总输出。

## 原理（翻译表）

Harmony prefix（委托在 CompatCore/默认 ALC）：

1. **出站** `NetMessage.SendPacketToServer(byte[])`（private static，客户端模式唯一发包出口）
   - `ref` 替换 data 数组（可自由增长）
   - `1` Hello：版本串 `Terraria319` → `Terraria325`
   - `27` SyncProjectile：`identity+owner` → `ProjectileKey`（`Pack(owner,ident,0)`）
   - `29` KillProjectile：`identity+owner` → `key + deathPos(NaN,NaN)`（NaN 触发服务器静默销毁分支）
   - `28` DamageNPC：`short npc` → `byte npc + byte gen`（gen 从入站 23 收集的映射回填，服务器硬校验）
   - `82` NetModule：moduleId 旧→新（ID≥5 → +1）
2. **入站** `MessageBuffer.GetData(int,int,out int)`（public instance，客户端同样走此收包入口）
   - 修改 `ref length` + 覆盖 readBuffer 前缀（只缩短/等长，不动长度头）
   - `17` Tile：新 body 9B → 旧 8B
   - `21` SyncItem：截断 24B + flags 置 0
   - `22` ItemOwner：头 3 + 尾 8 拼成 11B
   - `23` SyncNPC：gen 清零 + 保存 gen→映射
   - `27` SyncProjectile：key → `identity+owner`（缩短 1B）
   - `28` DamageNPC：gen 清零（等长）
   - `29` KillProjectile：key+deathPos → `identity+owner`（缩短 9B）
   - `82` NetModule：moduleId 新→旧（ID≥6 → -1）；ID==5(CreativeUnlocks) → 改类型 93 整体跳过
   - `162` DamageNPCAck：1.4.5.6 客户端 `MessageID.Count==162`，GetData 开头自动忽略，无需处理

## 构建

```
# 单命令：插件工程会自动先构建 CompatCore（输出到 client-build/）再编译自身
dotnet build terraangel-plugins/CompatClient1457.TAPlugin -c Release
```

- 依赖 NuGet 包 **Lib.Harmony 2.3.5**（首次构建需联网还原；若离线环境无法还原，
  回退方案：把 csproj 的 PackageReference 改回本地 `..\client-build\0Harmony.dll` Reference 2.2.0）
- 插件 csproj 内嵌 `BuildCompatCoreDependency` 目标（BeforeTargets ResolveAssemblyReferences），
  自动 `Restore;Build` CompatCore，并把 0Harmony.dll 一并复制到插件输出目录
- 输出目录 bin/Release/net10.0/ 应包含：CompatClient1457.TAPlugin.dll、CompatCore.dll、0Harmony.dll

## 部署

把构建输出的 **3 个 dll** 复制到客户端插件目录：

```
{Terraria存档}/TerraAngel/Plugins/
  ├─ CompatClient1457.TAPlugin.dll   # 插件壳（装配 + 会话管理）
  ├─ CompatCore.dll                  # 翻译核心（默认 ALC 常驻，不随插件卸载）
  └─ 0Harmony.dll                    # Harmony 运行时（v2.2.0）
```

客户端插件 UI 中勾选启用 **CompatClient1457**（插件默认禁用）。

## 稳定性说明

- ✅ **`#reload` 安全**：补丁永不卸载，重载只切换翻译器（v2.0 已解决）
- ✅ **启动/进服/退出** 正常
- ✅ 网络线程零 UI 访问；补丁安装失败会在客户端控制台输出错误（不卡死）
- 故障恢复：客户端异常打不开时，删除 Plugins/ 目录下上述 3 个 dll 即可恢复

## 已知边界（v1）

- 入站 `23` 未做 SyncAnchor 位置还原（1.4.5.6 客户端无 `NPCID.Sets.SyncAnchor` 表，1.4.5.7 新增）
  → 锚点非 (0,0) 的怪（较少）位置偏移；多数怪 Anchor=0 等价
- 出站 `28` 的 gen 查不到映射时填 0 → 服务器 NPC generation 非 0 时该次伤害可能被拒（一般怪刚被 23 同步过，映射存在）
- 出站 `27` 的 Generation 恒填 0（服务器 `NewProjectileSetup` 用传入 key 的 gen，实测链路可用）
