# 模块: Configuration

---

# Configuration/ConfigFile.cs

**命名空间**: `TShockAPI.Configuration`

## 类型定义
- **ConfigFile<TSettings>** : `I`

## 方法
### `virtual TSettings Read(string path, out bool incompleteSettings)`
Settings object

### `virtual TSettings Read(Stream stream, out bool incompleteSettings)`
Settings object

### `virtual TSettings ConvertJson(string json, out bool incompleteSettings)`
Settings object

### `virtual void Write(string path)`
The file path the configuration file will be written to

### `virtual void Write(Stream stream)`
stream

---

# Configuration/IConfigFile.cs

**命名空间**: `TShockAPI.Configuration`

## 类型定义
- **wrapping**
- **IConfigFile<TSettings>**

---

# Configuration/ServerSideConfig.cs

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

**命名空间**: `TShockAPI.Configuration`

## 类型定义
- **SscSettings**
- **ServerSideConfig** : `C`

## 方法
### `override SscSettings ConvertJson(string json, out bool incompleteSettings)`

### `static void DumpDescriptions()`

---

# Configuration/TShockConfig.cs

**命名空间**: `TShockAPI.Configuration`

## 类型定义
- **TShockSettings**
- **TShockConfig** : `C`
- **PvPModes**

## 方法
### `<summary>Allows players to break temporary tiles(grass, pots, etc)`

### `override TShockSettings ConvertJson(string json, out bool incompleteSettings)`

### `static void DumpDescriptions()`
