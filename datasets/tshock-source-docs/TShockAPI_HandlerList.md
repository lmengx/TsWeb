# HandlerList.cs

**命名空间**: `TShockAPI`

## 类型定义
- **HandlerList** : `H`
- **HandlerList<T>**
- **HandlerItem**
- **HandlerPriority**

## 枚举值
| 名称 | 值 | 说明 |
|---|---|---|
| `Highest` | 1 |  |
| `High` | 2 |  |
| `Normal` | 3 |  |
| `Low` | 4 |  |
| `Lowest` | 5 |  |

## 方法
### `public HandlerList()`

### `void Register(EventHandler<T> handler, HandlerPriority priority = HandlerPriority.Normal, bool gethandled = false)`
Should the handler receive a call even if it has been handled

### `void Register(HandlerItem obj)`

### `void UnRegister(EventHandler<T> handler)`

### `void Invoke(object sender, T e)`

### `static HandlerItem Create(EventHandler<T> handler, HandlerPriority priority = HandlerPriority.Normal, bool gethandled = false)`
