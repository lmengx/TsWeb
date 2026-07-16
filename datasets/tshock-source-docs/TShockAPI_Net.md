# 模块: Net

---

# Net/BaseMsg.cs

**命名空间**: `TShockAPI.Net`

## 类型定义
- **BaseMsg** : `I`

## 方法
### `void PackFull(Stream stream)`

### `virtual void Unpack(Stream stream)`

### `virtual void Pack(Stream stream)`

---

# Net/DisconnectMsg.cs

**命名空间**: `TShockAPI.Net`

## 类型定义
- **DisconnectMsg** : `B`

## 方法
### `override void Pack(Stream stream)`

---

# Net/NetTile.cs

**命名空间**: `TShockAPI.Net`

## 类型定义
- **NetTile** : `I`

## 方法
### `public NetTile()`

### `void Pack(Stream stream)`

### `void Unpack(Stream stream)`

---

# Net/ProjectileRemoveMsg.cs

**命名空间**: `TShockAPI.Net`

## 类型定义
- **ProjectileRemoveMsg** : `B`

## 方法
### `override void Pack(Stream stream)`

---

# Net/SpawnMsg.cs

**命名空间**: `TShockAPI.Net`

## 类型定义
- **SpawnMsg** : `B`

## 方法
### `override void Pack(Stream stream)`

---

# Net/WorldInfoMsg.cs

**命名空间**: `TShockAPI.Net`

## 类型定义
- **BossFlags** : `b`
- **BossFlags2** : `b`
- **BossFlags3** : `b`
- **BossFlags4** : `b`
- **WorldInfoMsg** : `B`

## 枚举值
| 名称 | 值 | 说明 |
|---|---|---|
| `None` | 0 |  |
| `OrbSmashed` | 1 |  |
| `DownedBoss1` | 2 |  |
| `DownedBoss2` | 4 |  |
| `DownedBoss3` | 8 |  |
| `HardMode` | 16 |  |
| `DownedClown` | 32 |  |
| `ServerSideCharacter` | 64 |  |
| `DownedPlantBoss` | 128 |  |

## 方法
### `override void Pack(Stream stream)`
