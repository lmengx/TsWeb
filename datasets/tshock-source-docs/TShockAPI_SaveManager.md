# SaveManager.cs

**命名空间**: `TShockAPI`

## 类型定义
- **SaveManager** : `I`
- **SaveTask**

## 方法
### `private SaveManager()`

### `void OnSaveWorld(WorldSaveEventArgs args)`

### `void SaveWorld(bool wait = true, bool resetTime = false, bool direct = false)`
use the realsaveWorld method instead of saveWorld event (default: false)

### `void Dispose()`

### `void EnqueueTask(SaveTask task)`

### `void SaveWorker()`

### `public SaveTask(bool resetTime, bool direct)`

### `override string ToString()`
