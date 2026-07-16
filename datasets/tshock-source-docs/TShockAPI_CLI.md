# 模块: CLI

---

# CLI/CommandLineParser.cs

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

**命名空间**: `TShockAPI.CLI`

## 类型定义
- **CommandLineParser**

## 方法
### `CommandLineParser Reset()`

### `CommandLineParser AddFlag(string flag, bool noArgs = false)`
Whether or not the flag is followed by an argument

### `CommandLineParser AddFlag(string flag, Action<string> callback)`

### `CommandLineParser AddFlag(string flag, Action callback)`

### `CommandLineParser AddFlags(FlagSet flags)`

### `CommandLineParser AddFlags(FlagSet flags, Action<string> callback)`

### `CommandLineParser AddFlags(FlagSet flags, Action callback)`

### `CommandLineParser After(Action callback)`

### `CommandLineParser ParseFromSource(string[] source)`

---

# CLI/FlagSet.cs

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

**命名空间**: `TShockAPI.CLI`

## 类型定义
- **FlagSet** : `I`

## 方法
### `public FlagSet(params string[] flags)`
Flags represented by this FlagSet

### `bool Contains(string flag)`

### `bool Equals(FlagSet other)`
