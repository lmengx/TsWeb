# NetItem.cs

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
- **NetItem**

## 方法
### `public NetItem(int netId, int stack = 1, byte prefixId = 0, bool favorited = false)`
The favorited state.

### `public NetItem(Item item)`
Item in the game.

### `Item ToItem()`
A copy of the item.

### `override string ToString()`

### `static NetItem Parse(string str)`
