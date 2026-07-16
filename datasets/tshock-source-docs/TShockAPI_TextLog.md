# TextLog.cs

**命名空间**: `TShockAPI`

## 类型定义
- **TextLog** : `I`

## 方法
### `public TextLog(string filename, bool clear)`
Whether or not to clear the log file on initialization.

### `bool MayWriteType(TraceLevel type)`

### `void Data(string message)`
The message to be written.

### `void Data(string format, params object[] args)`
The format arguments.

### `void Error(string message)`
The message to be written.

### `void Error(string format, params object[] args)`
The format arguments.

### `void ConsoleError(string message)`
The message to be written.

### `void ConsoleWarn(string message)`
The message to be written.

### `void ConsoleWarn(string format, params object[] args)`
The format arguments.

### `void ConsoleError(string format, params object[] args)`
The format arguments.

### `void Warn(string message)`
The message to be written.

### `void Warn(string format, params object[] args)`
The format arguments.

### `void Info(string message)`
The message to be written.

### `void Info(string format, params object[] args)`
The format arguments.

### `void ConsoleInfo(string message)`
The message to be written.

### `void ConsoleInfo(string format, params object[] args)`
The format arguments.

### `void ConsoleDebug(string message)`
The message to be written.

### `void ConsoleDebug(string format, params object[] args)`
The format arguments.

### `void Debug(string message)`
The message to be written.

### `void Debug(string format, params object[] args)`
The format arguments.

### `void Write(string message, TraceLevel level)`

### `void Dispose()`
