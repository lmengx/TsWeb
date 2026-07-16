# 模块: Modules

---

# Modules/Module.cs

**命名空间**: `TShockAPI.Modules`

## 类型定义
- **Module** : `I`

## 方法
### `virtual void Dispose()`

---

# Modules/ModuleManager.cs

**命名空间**: `TShockAPI.Modules`

## 类型定义
- **ModuleManager** : `I`

## 方法
### `void Initialise(object[] parameters)`
Additional constructor arguments allowed for modules

### `void InitialiseModule(Type moduleType, object[] parameters)`
Additional constructor arguments allowed for modules

### `void Dispose()`

---

# Modules/ReduceConsoleSpam.cs

**命名空间**: `TShockAPI.Modules`

## 类型定义
- **ReduceConsoleSpam** : `M`

## 方法
### `void OnMainStatusTextChange(object sender, OTAPI.Hooks.Main.StatusTextChangeArgs e)`
OTAPI event

### `void WriteIfChange(string text)`

### `bool replace(string text)`
