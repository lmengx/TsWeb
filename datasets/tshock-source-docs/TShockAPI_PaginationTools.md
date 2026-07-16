# PaginationTools.cs

**命名空间**: `TShockAPI`

## 类型定义
- **PaginationTools**
- **Settings**

## 方法
### `public Settings()`

### `static void SendPage(TSPlayer player, int pageNumber, IEnumerable dataToPaginate, int dataToPaginateCount, Settings settings = null)`

### `static void SendPage(TSPlayer player, int pageNumber, IList dataToPaginate, Settings settings = null)`

### `static List<string> BuildLinesFromTerms(IEnumerable terms, Func<object, string> termFormatter = null, string separator = ", ", int maxCharsPerLine = 80)`

### `static bool TryParsePageNumber(List<string> commandParameters, int expectedParameterIndex, TSPlayer errorMessageReceiver, out int pageNumber)`
