# TSServerPlayer.cs

## 文件说明
/
TShock, a server mod for Terraria
Copyright (C) 2011-2019 Pryaxis & TShock Contributors
This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.
This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.
You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
/

**命名空间**: `TShockAPI`

## 类型定义
- **TSServerPlayer** : `T`

## 方法
### `override void SendErrorMessage(string msg)`

### `override void SendInfoMessage(string msg)`

### `override void SendSuccessMessage(string msg)`

### `override void SendWarningMessage(string msg)`

### `override void SendMessage(string msg, Color color)`

### `override void SendMessage(string msg, byte red, byte green, byte blue)`

### `void SendConsoleMessage(string msg, byte red, byte green, byte blue)`

### `void SetFullMoon()`

### `void SetBloodMoon(bool bloodMoon)`

### `void SetFrostMoon(bool snowMoon)`

### `void SetPumpkinMoon(bool pumpkinMoon)`

### `void SetEclipse(bool eclipse)`

### `void SetTime(bool dayTime, double time)`

### `void SpawnNPC(int type, string name, int amount, int startTileX, int startTileY, int tileXRange = 100,
			int tileYRange = 50)`

### `void StrikeNPC(int npcid, int damage, float knockBack, int hitDirection)`

### `void RevertTiles(Dictionary<Vector2, ITile> tiles)`

### `ConsoleColor PickNearbyConsoleColor(Color color)`

### `an integer difference between two colors in euclidean space
			int ColorDiff(Color c1, Color c2)`
