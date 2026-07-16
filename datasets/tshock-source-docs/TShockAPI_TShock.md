# TShock.cs

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

**命名空间**: `TShockAPI`

## 类型定义
- **TShock** : `T`

## 方法
### `IntPtr ResolveNativeDep(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)`

### `override void Initialize()`

### `handle if Log was not initialised
				void SafeError(string message)`

### `static void OnAchievementInitializerLoad(object sender, OTAPI.Hooks.Initializers.AchievementInitializerLoadEventArgs args)`

### `void CrashReporter_HeapshotRequesting(object sender, EventArgs e)`

### `override void Dispose(bool disposing)`
disposing - If set, disposes of all hooks and other systems.

### `void OnPlayerLogin(PlayerPostLoginEventArgs args)`
args - The PlayerPostLoginEventArgs object.

### `void OnAccountDelete(Hooks.AccountDeleteEventArgs args)`
args - The AccountDeleteEventArgs object.

### `void OnAccountCreate(Hooks.AccountCreateEventArgs args)`
args - The AccountCreateEventArgs object.

### `void OnPlayerPreLogin(Hooks.PlayerPreLoginEventArgs args)`
args - The PlayerPreLoginEventArgs object.

### `void NetHooks_NameCollision(NameCollisionEventArgs args)`
args - The NameCollisionEventArgs object.

### `void OnItemForceIntoChest(ForceItemIntoChestEventArgs args)`
The  object.

### `void OnXmasCheck(ChristmasCheckEventArgs args)`
args - The ChristmasCheckEventArgs object.

### `void OnHalloweenCheck(HalloweenCheckEventArgs args)`
args - The HalloweenCheckEventArgs object.

### `void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)`
e - The UnhandledExceptionEventArgs object.

### `void ConsoleCancelHandler(object sender, ConsoleCancelEventArgs args)`
The ConsoleCancelEventArgs associated with the event.

### `void HandleCommandLine(string[] parms)`
parms - The array of arguments passed in through the command line.

### `static void HandleCommandLinePostConfigLoad(string[] parms)`
parms - The array of arguments passed in through the command line.

### `void OnPostInit(EventArgs args)`
args - The EventArgs object.

### `void OnUpdate(EventArgs args)`
args - EventArgs args

### `void OnSecondUpdate()`
OnSecondUpdate - Called effectively every second for all time based checks.

### `void OnHardUpdate(HardmodeTileUpdateEventArgs args)`
args - The HardmodeTileUpdateEventArgs object.

### `void OnWorldGrassSpread(GrassSpreadEventArgs args)`
args - The GrassSpreadEventArgs object.

### `bool OnCreep(int tileType)`
True if allowed, otherwise false

### `void OnStatueSpawn(StatueSpawnEventArgs args)`
args - The StatueSpawnEventArgs object.

### `void OnConnect(ConnectEventArgs args)`
args - The ConnectEventArgs object.

### `void OnJoin(JoinEventArgs args)`
args - The JoinEventArgs object.

### `void OnLeave(LeaveEventArgs args)`
args - The LeaveEventArgs object.

### `void OnChat(ServerChatEventArgs args)`
args - The ServerChatEventArgs object.

### `void ServerHooks_OnCommand(CommandEventArgs args)`
The CommandEventArgs object

### `string TruncateChatMessageIfNecessary(ServerChatEventArgs args)`

### `void OnGetData(GetDataEventArgs e)`
e - The GetDataEventArgs object.

### `void OnGreetPlayer(GreetPlayerEventArgs args)`
args - The GreetPlayerEventArgs object.

### `void NpcHooks_OnStrikeNpc(NpcStrikeEventArgs e)`
e - The NpcStrikeEventArgs object.

### `void OnProjectileSetDefaults(SetDefaultsEventArgs<Projectile, int> e)`
e - The SetDefaultsEventArgs object parameterized with Projectile and int.

### `void NetHooks_SendData(SendDataEventArgs e)`
e - The SendDataEventArgs object.

### `void OnStartHardMode(HandledEventArgs e)`
e - The HandledEventArgs object.

### `void OnConfigRead(ConfigFile<TShockSettings> file)`
file - The config file object.
