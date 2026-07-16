# 模块: Hooks

---

# Hooks/AccountHooks.cs

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

**命名空间**: `TShockAPI.Hooks`

## 类型定义
- **AccountDeleteEventArgs**
- **AccountCreateEventArgs**
- **AccountGroupUpdateEventArgs** : `H`
- **AccountGroupUpdateByPlayerEventArgs** : `A`
- **AccountHooks**

## 方法
### `public AccountDeleteEventArgs(UserAccount account)`

### `public AccountCreateEventArgs(UserAccount account)`

### `public AccountGroupUpdateEventArgs(string accountName, Group group)`

### `static void OnAccountCreate(UserAccount u)`

### `static void OnAccountDelete(UserAccount u)`

### `static bool OnAccountGroupUpdate(UserAccount account, TSPlayer author, ref Group group)`

### `static bool OnAccountGroupUpdate(UserAccount account, ref Group group)`

---

# Hooks/GeneralHooks.cs

**命名空间**: `TShockAPI.Hooks`

## 类型定义
- **ReloadEventArgs**
- **GeneralHooks**

## 方法
### `public ReloadEventArgs(TSPlayer ply)`

### `static void OnReloadEvent(TSPlayer ply)`

---

# Hooks/PlayerHooks.cs

**命名空间**: `TShockAPI.Hooks`

## 类型定义
- **PlayerPostLoginEventArgs**
- **PlayerPreLoginEventArgs** : `H`
- **PlayerLogoutEventArgs**
- **PlayerCommandEventArgs** : `H`
- **PrePlayerCommandEventArgs** : `H`
- **PostPlayerCommandEventArgs** : `H`
- **PlayerChatEventArgs** : `H`
- **PlayerPermissionEventArgs**
- **PlayerItembanPermissionEventArgs**
- **PlayerProjbanPermissionEventArgs**
- **PlayerTilebanPermissionEventArgs**
- **PlayerHasBuildPermissionEventArgs**
- **PlayerHooks**
- **PermissionHookResult**

## 枚举值
| 名称 | 值 | 说明 |
|---|---|---|
| `Unhandled` |  |  |
| `Denied` |  |  |
| `Granted` |  |  |

## 方法
### `public PlayerPostLoginEventArgs(TSPlayer ply)`
The player who fired the event.

### `public PlayerLogoutEventArgs(TSPlayer player)`
The player who fired the event.

### `public PrePlayerCommandEventArgs(Command command, CommandArgs args)`

### `public PostPlayerCommandEventArgs(Command command, CommandArgs arguments, bool handled)`

### `public PlayerPermissionEventArgs(TSPlayer player, string permission)`
The permission being checked.

### `public PlayerItembanPermissionEventArgs(TSPlayer player, ItemBan bannedItem)`
The banned item being checked.

### `public PlayerProjbanPermissionEventArgs(TSPlayer player, ProjectileBan checkedProjectile)`
The banned projectile being checked.

### `public PlayerTilebanPermissionEventArgs(TSPlayer player, TileBan checkedTile)`
The banned tile being checked.

### `static void OnPlayerPostLogin(TSPlayer ply)`
The player firing the event.

### `static bool OnPlayerCommand(TSPlayer player, string cmdName, string cmdText, List<string> args, ref IEnumerable<Command> commands, string cmdPrefix)`
True if the event has been handled.

### `static bool OnPrePlayerCommand(Command cmd, ref CommandArgs arguments)`
True if the event has been handled.

### `static void OnPostPlayerCommand(Command cmd, CommandArgs arguments, bool handled)`
Is the command executed.

### `static bool OnPlayerPreLogin(TSPlayer ply, string name, string pass)`
True if the event has been handled.

### `static void OnPlayerLogout(TSPlayer ply)`
The player firing the event.

### `static bool OnPlayerChat(TSPlayer ply, string rawtext, ref string tshockText)`
The chat text after being formatted.

### `static PermissionHookResult OnPlayerPermission(TSPlayer player, string permission)`
Event result if the event has been handled, otherwise .

### `static PermissionHookResult OnPlayerItembanPermission(TSPlayer player, ItemBan bannedItem)`
Event result if the event has been handled, otherwise .

### `static PermissionHookResult OnPlayerProjbanPermission(TSPlayer player, ProjectileBan bannedProj)`
Event result if the event has been handled, otherwise .

### `static PermissionHookResult OnPlayerTilebanPermission(TSPlayer player, TileBan bannedTile)`
Event result if the event has been handled, otherwise .

### `static PermissionHookResult OnPlayerHasBuildPermission(TSPlayer player, int x, int y)`
Event result if the event has been handled, otherwise .

---

# Hooks/RegionHooks.cs

**命名空间**: `TShockAPI.Hooks`

## 类型定义
- **RegionHooks**
- **RegionEnteredEventArgs**
- **RegionLeftEventArgs**
- **RegionCreatedEventArgs**
- **RegionDeletedEventArgs**
- **RegionRenamedEventArgs**

## 方法
### `public RegionEnteredEventArgs(TSPlayer ply, Region region)`

### `static void OnRegionEntered(TSPlayer player, Region region)`

### `public RegionLeftEventArgs(TSPlayer ply, Region region)`

### `static void OnRegionLeft(TSPlayer player, Region region)`

### `public RegionCreatedEventArgs(Region region)`

### `static void OnRegionCreated(Region region)`

### `public RegionDeletedEventArgs(Region region)`

### `static void OnRegionDeleted(Region region)`

### `public RegionRenamedEventArgs(Region region, string oldName, string newName)`

### `static void OnRegionRenamed(Region region, string oldName, string newName)`
