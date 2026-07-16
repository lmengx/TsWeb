# 模块: Extensions

---

# Extensions/DbExt.cs

**命名空间**: `TShockAPI.DB`

## 类型定义
- **DbExt**
- **SqlType**
- **QueryResult** : `I`

## 枚举值
| 名称 | 值 | 说明 |
|---|---|---|
| `Unknown` |  |  |
| `Sqlite` |  |  |
| `Mysql` |  |  |
| `Postgres` |  |  |

## 方法
### `static int Query(this IDbConnection olddb, string query, params object[] args)`

### `static QueryResult QueryReader(this IDbConnection olddb, string query, params object[] args)`

### `static QueryResult QueryReaderDict(this IDbConnection olddb, string query, Dictionary<string, object> values)`

### `static IDbDataParameter AddParameter(this IDbCommand command, string name, object data)`

### `static IDbConnection CloneEx(this IDbConnection conn)`

### `public QueryResult(IDbConnection conn, IDataReader reader, IDbCommand command)`

### `void Dispose()`

### `virtual void Dispose(bool disposing)`

### `bool Read()`

---

# Extensions/ExceptionExt.cs

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

**命名空间**: `TShockAPI.Extensions`

## 类型定义
- **ExceptionExt**

## 方法
### `static string BuildExceptionString(this Exception ex)`

---

# Extensions/RandomExt.cs

**命名空间**: `TShockAPI.Extensions`

## 类型定义
- **RandomExt**

## 方法
### `static string NextString(this Random rand, int length)`

---

# Extensions/StringExt.cs

**命名空间**: `TShockAPI`

## 类型定义
- **StringExt**

## 方法
### `static string SFormat(this string str, params object[] args)`

### `static string Color(this object obj, string color)`
