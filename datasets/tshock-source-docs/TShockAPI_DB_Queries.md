# 模块: DB / Queries

---

# DB/Queries/GenericQueryBuilder.cs

**命名空间**: `TShockAPI.DB.Queries`

## 类型定义
- **GenericQueryBuilder** : `I`

## 方法
### `string AlterTable(SqlTable from, SqlTable to)`

### `static void ValidateSqlColumnType(List<SqlColumn> columns)`

### `string DeleteRow(string table, List<SqlValue> wheres)`

### `string UpdateValue(string table, List<SqlValue> values, List<SqlValue> wheres)`

### `string ReadColumn(string table, List<SqlValue> wheres)`

### `string InsertValues(string table, List<SqlValue> values)`

### ` columns.ForEach(x =>
		{
			if (x.DefaultCurrentTimestamp && x.Type != MySqlDbType.DateTime)`

---

# DB/Queries/IQueryBuilder.cs

**命名空间**: `TShockAPI.DB.Queries`

## 类型定义
- **IQueryBuilder**
- **to**

## 枚举值
| 名称 | 值 | 说明 |
|---|---|---|
| `string` |  |  |
| `string` |  |  |
| `string` |  |  |
| `string` |  |  |
| `string` |  |  |
| `string` |  |  |

---

# DB/Queries/MysqlQueryBuilder.cs

**命名空间**: `TShockAPI.DB.Queries`

## 类型定义
- **MysqlQueryBuilder** : `G`

## 方法
### `override string CreateTable(SqlTable table)`
The sql query for the table creation.

### `override string DbTypeToString(MySqlDbType type, int? length)`

---

# DB/Queries/PostgresQueryBuilder.cs

**命名空间**: `TShockAPI.DB.Queries`

## 类型定义
- **PostgresQueryBuilder** : `G`

## 方法
### `override string CreateTable(SqlTable table)`

---

# DB/Queries/SqliteQueryBuilder.cs

**命名空间**: `TShockAPI.DB.Queries`

## 类型定义
- **SqliteQueryBuilder** : `G`
- **to**

## 枚举值
| 名称 | 值 | 说明 |
|---|---|---|
| `public` |  |  |
| `if` |  |  |
| `return` |  |  |

## 方法
### `override string CreateTable(SqlTable table)`
The sql query for the table creation.

### `override string RenameTable(string from, string to)`
The sql query for renaming the table.

### `override string DbTypeToString(MySqlDbType type, int? length)`
The string representation
