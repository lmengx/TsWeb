# 模块: Handlers

---

# Handlers/DisplayDollItemSyncHandler.cs

**命名空间**: `TShockAPI.Handlers`

## 类型定义
- **DisplayDollItemSyncHandler** : `I`

## 方法
### `void OnReceive(object sender, DisplayDollItemSyncEventArgs args)`

---

# Handlers/EmojiHandler.cs

**命名空间**: `TShockAPI.Handlers`

## 类型定义
- **EmojiHandler** : `I`

## 方法
### `void OnReceive(object sender, EmojiEventArgs args)`

---

# Handlers/IPacketHandler.cs

**命名空间**: `TShockAPI.Handlers`

## 类型定义
- **IPacketHandler<TEventArgs>**

---

# Handlers/LandGolfBallInCupHandler.cs

**命名空间**: `TShockAPI.Handlers`

## 类型定义
- **LandGolfBallInCupHandler** : `I`

## 方法
### `void OnReceive(object sender, LandGolfBallInCupEventArgs args)`

---

# Handlers/RequestTileEntityInteractionHandler.cs

**命名空间**: `TShockAPI.Handlers`

## 类型定义
- **RequestTileEntityInteractionHandler** : `I`

## 方法
### `void OnReceive(object sender, RequestTileEntityInteractionEventArgs args)`

---

# Handlers/SendTileRectHandler.cs

**命名空间**: `TShockAPI.Handlers`

## 类型定义
- **SendTileRectHandler** : `I`
- **TileRect**
- **TileRectMatch**
- **MatchType**
- **MatchResult**
- **allows**
- **WorldGenMock**
- **MockTile**

## 枚举值
| 名称 | 值 | 说明 |
|---|---|---|
| `Placement` |  |  |
| `StateChange` |  |  |
| `Removal` |  |  |

## 方法
### `public TileRect(NetTile[,] tiles, int x, int y, int width, int height)`

### `static TileRect Read(MemoryStream stream, int tileX, int tileY, int width, int height)`
The resulting tile rect.

### `private TileRectMatch(MatchType type, int width, int height, ushort tileType, short maxFrameX, short maxFrameY, short frameXStep, short frameYStep)`

### `static TileRectMatch Placement(int width, int height, ushort tileType, short maxFrameX, short maxFrameY, short frameXStep, short frameYStep)`
The resulting operation match.

### `static TileRectMatch StateChange(int width, int height, ushort tileType, short maxFrameX, short maxFrameY, short frameXStep, short frameYStep)`
The resulting operation match.

### `static TileRectMatch StateChangeX(int width, int height, ushort tileType, short maxFrame, short frameStep)`
The resulting operation match.

### `static TileRectMatch StateChangeY(int width, int height, ushort tileType, short maxFrame, short frameStep)`
The resulting operation match.

### `static TileRectMatch Removal(int width, int height, ushort tileType)`
The resulting operation match.

### `MatchResult Matches(TSPlayer player, TileRect rect)`
, if the rect matches this operation and the changes have been applied, otherwise .

### `MatchResult MatchPlacement(TSPlayer player, TileRect rect)`

### `MatchResult MatchStateChange(TSPlayer player, TileRect rect)`

### `MatchResult MatchRemoval(TSPlayer player, TileRect rect)`

### `void OnReceive(object sender, GetDataHandlers.SendTileRectEventArgs args)`

### `static bool IsRectPositionValid(TSPlayer player, TileRect rect)`
, if the rect at a valid position, otherwise .

### `static bool IsRectDistanceValid(TSPlayer player, TileRect rect)`
, if the rect at a valid distance, otherwise .

### `static bool MatchesConversionSpread(TSPlayer player, TileRect rect)`
, if the rect matches a conversion spread operation, otherwise .

### `static bool MatchesFlowerBoots(TSPlayer player, TileRect rect)`
, if the rect matches a Flower Boots placement, otherwise .

### `static bool MatchesGrassMow(TSPlayer player, TileRect rect)`
, if the rect matches a grass mowing operation, otherwise .

### `static bool MatchesChristmasTree(TSPlayer player, TileRect rect)`
, if the rect matches a christmas tree operation, otherwise .

### `static void FrameAndSyncRect(TileRect rect)`

### `public MockTile(ushort type, ushort wall, HashSet<ushort> setTypes, HashSet<ushort> setWalls)`

### `static void SimulateConversionChange(int x, int y, out HashSet<ushort> validTiles, out HashSet<ushort> validWalls)`

### `static void Convert(MockTile tile, int k, int l, int conversionType)`

---

# Handlers/SyncTilePickingHandler.cs

**命名空间**: `TShockAPI.Handlers`

## 类型定义
- **SyncTilePickingHandler** : `I`

## 方法
### `void OnReceive(object sender, SyncTilePickingEventArgs args)`
