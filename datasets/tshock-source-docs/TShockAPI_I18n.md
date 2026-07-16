# I18n.cs

## 文件说明
/
TShock, a server mod for Terraria
Copyright (C) 2022 Janet Blackquill
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
- **I18n**

## 方法
### `string GetString(FormattableStringAdapter text)`
Translated text.

### `static string GetString(FormattableString text)`
Translated text.

### `static string GetString(FormattableStringAdapter text, params object[] args)`
Translated text.

### `static string GetPluralString(FormattableStringAdapter text, FormattableStringAdapter pluralText, long n)`
Translated text.

### `static string GetPluralString(FormattableString text, FormattableString pluralText, long n)`
Translated text.

### `static string GetPluralString(FormattableStringAdapter text, FormattableStringAdapter pluralText, long n, params object[] args)`
Translated text.

### `static string GetParticularString(string context, FormattableStringAdapter text)`
Translated text.

### `static string GetParticularString(string context, FormattableString text)`
Translated text.

### `static string GetParticularString(string context, FormattableStringAdapter text, params object[] args)`
Translated text.

### `static string GetParticularPluralString(string context, FormattableStringAdapter text, FormattableStringAdapter pluralText, long n)`
Translated text.

### `static string GetParticularPluralString(string context, FormattableString text, FormattableString pluralText, long n)`
Translated text.

### `static string GetParticularPluralString(string context, FormattableStringAdapter text, FormattableStringAdapter pluralText, long n, params object[] args)`
Translated text.
