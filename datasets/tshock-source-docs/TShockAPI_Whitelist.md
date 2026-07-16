# Whitelist.cs

## 文件说明
/
TShock, a server mod for Terraria
Copyright (C) 2011-2025 Pryaxis & TShock Contributors
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
- **Whitelist**

## 方法
### `public Whitelist(string path)`

### `bool IsWhitelisted(string host)`
true/false

### `void ReloadFromFile()`

### `void ReadWhitelistFromFile()`

### `void ReadWhitelistLine(scoped ReadOnlySpan<char> content, int line)`

### `bool AddToWhitelist(scoped ReadOnlySpan<char> ip)`
true if the address or network was added successfully; otherwise, false.

### `bool RemoveFromWhitelist(scoped ReadOnlySpan<char> ip)`
>true if the address or network was removed successfully; otherwise, false.

### `bool AddLine(scoped ReadOnlySpan<char> content)`

### `bool RemoveLine(scoped ReadOnlySpan<char> content)`
