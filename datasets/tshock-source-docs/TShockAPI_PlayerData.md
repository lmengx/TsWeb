# PlayerData.cs

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
- **PlayerData**

## 方法
### `public PlayerData(bool includingStarterInventory = true)`
Is it necessary to load items from TShock's config

### `void StoreSlot(int slot, int netID, byte prefix, int stack, bool favorited)`

### `void StoreSlot(int slot, NetItem item)`

### `void CopyCharacter(TSPlayer player)`

### `void RestoreCharacter(TSPlayer player)`
