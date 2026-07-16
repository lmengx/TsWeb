# ItemBans.cs

## 文件说明
/
TShock, a server mod for Terraria
Copyright (C) 2011-2018 Pryaxis & TShock Contributors
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
- **ItemBans**

## 方法
### `internal ItemBans(TShock plugin, IDbConnection database)`
A new item ban system.

### `void OnGameUpdate(EventArgs args)`
The standard event arguments.

### `void OnSecondlyUpdate(EventArgs args)`
The standard event arguments.

### `void OnPlayerUpdate(object sender, PlayerUpdateEventArgs args)`

### `void OnChestItemChange(object sender, ChestItemEventArgs args)`

### `void OnTileEdit(object sender, TileEditEventArgs args)`

### `void UnTaint(TSPlayer player)`

### `void Taint(TSPlayer player)`

### `void SendCorrectiveMessage(TSPlayer player, string itemName)`
