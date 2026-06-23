# Scan 命令修复计划

## 问题分析

通过分析 `AntiCheat.cs` 文件，发现以下问题：

### 1. 遗留的 `GetPlayerInventory` 方法（第888-923行）

```csharp
public static List<InventoryData> GetPlayerInventory(TSPlayer player)
{
    // 使用旧的索引映射方式
    // NetItem.ArmorIndex.Item1 + i
    // NetItem.DyeIndex.Item1 + i
}
```

**问题**：
- 该方法使用了旧的索引映射方式，与已修复的 `GetPlayerInv.GetOnlinePlayerInventory` 不一致
- 该方法目前没有被任何地方调用（遗留代码）
- 如果未来被调用，会导致索引混乱

### 2. 实际使用的背包获取方法

`ItemDetection` 类中的扫描方法调用的是：
- `GetPlayerInv.GetOnlinePlayerInventory(player)` - 第218行、第273行
- `GetPlayerInv.GetOfflinePlayerInventory(accountId)` - 第325行

这些方法已在之前的修复中更新，使用统一的索引映射。

### 3. 索引映射对比

| 区域 | 旧方法索引 | 新方法索引 |
|------|-----------|-----------|
| 盔甲 | `NetItem.ArmorIndex.Item1 + i` (59-78) | 59-68(盔甲) + 69-78(时装) |
| 染料 | `NetItem.DyeIndex.Item1 + i` (79-88) | 79-88 |
| 副装备 | 未包含 | 89-93 |
| 副染料 | 未包含 | 94-98 |
| 存储罐 | 未包含 | 99-138 |
| 保险柜 | 未包含 | 139-178 |
| 垃圾桶 | 未包含 | 179 |
| 护卫熔炉 | 未包含 | 180-219 |
| 虚空宝库 | 未包含 | 220-259 |
| 装备组 | 未包含 | 289-378 |

## 修复方案

### 方案一：删除遗留方法（推荐）

直接删除 `AntiCheat.GetPlayerInventory` 方法，因为：
1. 它没有被任何地方调用
2. `GetPlayerInv` 类已经提供了完整的背包获取功能
3. 避免代码冗余和混淆

### 方案二：修复遗留方法

如果需要保留该方法，则需要将其索引映射与 `GetPlayerInv.GetOnlinePlayerInventory` 保持一致。

## 实施步骤

1. **删除 `AntiCheat.GetPlayerInventory` 方法**（第888-923行）
2. **验证所有调用点**：确认 `ItemDetection` 类中的扫描方法正常工作
3. **测试**：运行 scan 命令验证功能正常

## 预期结果

- 代码更加简洁，消除冗余
- 背包索引映射统一，避免混淆
- scan 命令功能正常，在线和离线玩家扫描结果一致