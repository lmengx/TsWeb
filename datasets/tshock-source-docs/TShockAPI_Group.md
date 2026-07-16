# Group.cs

**命名空间**: `TShockAPI`

## 类型定义
- **used**
- **Group**
- **is**
- **SuperAdminGroup** : `G`
- **has**
- **with**

## 方法
### `virtual bool HasPermission(string permission)`
True if the group has that permission.

### `bool RealHasPermission(string permission, ref bool negated)`

### `virtual void NegatePermission(string permission)`
The permission to negate.

### `virtual void AddPermission(string permission)`
The permission to add.

### `virtual void SetPermission(List<string> permission)`
The new list of permissions to associate with the group.

### `virtual void RemovePermission(string permission)`

### `virtual void AssignTo(Group otherGroup)`
The other instance.

### `override string ToString()`
