# 模块: DB

---

# DB/BanManager.cs

**命名空间**: `TShockAPI.DB`

## 类型定义
- **BanManager**
- **BanSortMethod**
- **AddBanResult**
- **BanEventArgs** : `E`
- **BanPreAddEventArgs** : `E`
- **Identifier**
- **that**
- **Ban**

## 枚举值
| 名称 | 值 | 说明 |
|---|---|---|
| `ExpirationSoonestToLatest` |  |  |
| `ExpirationLatestToSoonest` |  |  |
| `AddedNewestToOldest` |  |  |
| `AddedOldestToNewest` |  |  |
| `TicketNumber` |  |  |

## 方法
### `public BanManager(IDbConnection db)`
A valid connection to the TShock database

### `void UpdateBans()`

### `void TryConvertBans()`

### `bool CheckBan(TSPlayer player)`

### `bool IsValidBan(Ban ban, TSPlayer player)`

### `void BanValidateCheck(object sender, BanEventArgs args)`

### `void BanAddedCheck(object sender, BanPreAddEventArgs args)`

### `AddBanResult InsertBan(string identifier, string reason, string banningUser, DateTime fromDate, DateTime toDate)`

### `AddBanResult InsertBan(BanPreAddEventArgs args)`

### `bool RemoveBan(int ticketNumber, bool fullDelete = false)`

### `Ban GetBanById(int id)`

### `IEnumerable<Ban> RetrieveBansByIdentifier(string identifier, bool currentOnly = true)`

### `IEnumerable<Ban> GetBansByIdentifiers(bool currentOnly = true, params string[] identifiers)`

### `IEnumerable<Ban> RetrieveAllBansSorted(BanSortMethod sortMethod)`

### `bool ClearBans()`
true, if bans were cleared, false otherwise.

### `private Identifier(string prefix, string description)`

### `override string ToString()`

### `static Identifier Register(string prefix, string description)`

### `string GetPrettyExpirationString()`

### `string GetPrettyTimeSinceBanString()`

### `public Ban(int ticketNumber, string identifier, string reason, string banningUser, DateTime start, DateTime end)`
DateTime at which the ban will end

---

# DB/CharacterManager.cs

**命名空间**: `TShockAPI.DB`

## 类型定义
- **CharacterManager**

## 方法
### `public CharacterManager(IDbConnection db)`

### `PlayerData GetPlayerData(TSPlayer player, int acctid)`

### `bool SeedInitialData(UserAccount account)`

### `bool IsSeededAppearanceMissing(PlayerData playerData)`
true when appearance fields are still missing.

### `bool SyncSeededAppearance(UserAccount account, TSPlayer player)`
true if update succeeded.

### `bool InsertPlayerData(TSPlayer player, bool fromCommand = false)`
true if inserted successfully

### `bool RemovePlayer(int userid)`
true if removed successfully

### `bool InsertSpecificPlayerData(TSPlayer player, PlayerData data)`
If the command succeeds.

---

# DB/DbBuilder.cs

**命名空间**: `TShockAPI.DB`

## 类型定义
- **DbBuilder**

## 方法
### `public DbBuilder(TShock caller, TShockConfig config, string savePath)`
The savePath registered by TShock. See .

### `IDbConnection BuildDbConnection()`

### `SqliteConnection BuildSqliteConnection()`

### `MySqlConnection BuildMySqlConnection()`

### `NpgsqlConnection BuildPostgresConnection()`

### `FileInfo GetDbFile(string path)`

---

# DB/GroupManager.cs

**命名空间**: `TShockAPI.DB`

## 类型定义
- **GroupManager** : `I`
- **with**
- **GroupManagerException** : `E`
- **GroupExistsException** : `G`
- **GroupNotExistException** : `G`

## 方法
### `public GroupManager(IDbConnection db)`
The connection.

### `void AssertCoreGroupsPresent()`

### `bool AssertGroupValid(TSPlayer player, Group group, bool kick)`

### `void AddDefaultGroup(string name, string parent, string permissions)`

### `bool GroupExists(string group)`
true if it does; otherwise, false.

### `IEnumerator<Group> GetEnumerator()`
The enumerator.

### `void AddGroup(string name, string parentname, string permissions, string chatcolor)`
chatcolor

### `void UpdateGroup(string name, string parentname, string permissions, string chatcolor, string suffix, string prefix)`

### `string RenameGroup(string name, string newName)`
The result from the operation to be sent back to the user.

### `string DeleteGroup(string name, bool exceptions = false)`
The result from the operation to be sent back to the user.

### `string AddPermissions(string name, List<string> permissions)`
The result from the operation to be sent back to the user.

### `string DeletePermissions(string name, List<string> permissions)`
The result from the operation to be sent back to the user.

### `void LoadPermisions()`

### `IEnumerator IEnumerable.GetEnumerator()`

---

# DB/ItemManager.cs

**命名空间**: `TShockAPI.DB`

## 类型定义
- **ItemManager**
- **ItemBan** : `I`
- **instead**

## 方法
### `public ItemManager(IDbConnection db)`

### `void UpdateItemBans()`

### `void AddNewBan(string itemname = "")`

### `void RemoveBan(string itemname)`

### `bool ItemIsBanned(string name, TSPlayer ply)`

### `bool AllowGroup(string item, string name)`

### `bool RemoveGroup(string item, string group)`

### `ItemBan GetItemBanByName(string name)`

### `public ItemBan()`

### `bool HasPermissionToUseItem(TSPlayer ply)`

### `void SetAllowedGroups(string groups)`

### `bool RemoveGroup(string groupName)`

### `override string ToString()`

---

# DB/ProjectileManager.cs

**命名空间**: `TShockAPI.DB`

## 类型定义
- **ProjectileManagager**
- **ProjectileBan** : `I`
- **instead**

## 方法
### `public ProjectileManagager(IDbConnection db)`

### `void UpdateBans()`

### `void AddNewBan(short id = 0)`

### `void RemoveBan(short id)`

### `bool ProjectileIsBanned(short id, TSPlayer ply)`

### `bool AllowGroup(short id, string name)`

### `bool RemoveGroup(short id, string group)`

### `ProjectileBan GetBanById(short id)`

### `public ProjectileBan()`

### `bool HasPermissionToCreateProjectile(TSPlayer ply)`

### `void SetAllowedGroups(string groups)`

---

# DB/RegionManager.cs

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

**命名空间**: `TShockAPI.DB`

## 类型定义
- **RegionManager**
- **Region**

## 方法
### `internal RegionManager(IDbConnection db)`

### `void Reload()`

### `bool AddRegion(int tx, int ty, int width, int height, string regionname, string owner, string worldid, int z = 0)`
Whether the region was created and added successfully.

### `bool DeleteRegion(int id)`
Whether the region was successfully deleted.

### `bool DeleteRegion(string name)`
Whether the region was successfully deleted.

### `bool SetRegionState(int id, bool state)`
Whether the region's state was successfully changed.

### `bool SetRegionState(string name, bool state)`
Whether the region's state was successfully changed.

### `bool CanBuild(int x, int y, TSPlayer ply)`
Whether the player can build at the given (x, y) coordinate

### `bool ResizeRegion(string regionName, int addAmount, int direction)`

### `bool RenameRegion(string oldName, string newName)`
true if renamed successfully, false otherwise

### `bool RemoveUser(string regionName, string userName)`
true if removed successfully

### `bool AddNewUser(string regionName, string userName)`
true if added successfully

### `bool PositionRegion(string regionName, int x, int y, int width, int height)`
Whether the operation succeeded.

### `List<Region> ListAllRegions(string worldid)`
List of regions with only their names

### `bool ChangeOwner(string regionName, string newOwner)`
Whether the change was successful

### `bool AllowGroup(string regionName, string groupName)`
Whether the change was successful

### `bool RemoveGroup(string regionName, string group)`
Whether the change was successful

### `Region GetTopRegion(IEnumerable<Region> regions)`

### `bool SetZ(string name, int z)`
Whether the change was successful

### `public Region()`

### `bool InArea(Rectangle point)`
Whether the point exists in the region's area

### `bool HasPermissionToBuildInRegion(TSPlayer ply)`
Whether the player has permission

### `void SetAllowedIDs(string ids)`
String of IDs to set

### `void SetAllowedGroups(string groups)`
String of group names to set

### `bool RemoveID(int id)`
true if the user was found and removed from the region's allowed users

### `bool RemoveGroup(string groupName)`

---

# DB/RememberedPosManager.cs

**命名空间**: `TShockAPI.DB`

## 类型定义
- **RememberedPosManager**

## 方法
### `public RememberedPosManager(IDbConnection db)`

### `Vector2 CheckLeavePos(string name)`

### `Vector2 GetLeavePos(string name, string IP)`

### `void InsertLeavePos(string name, string IP, int X, int Y)`

---

# DB/ResearchDatastore.cs

**命名空间**: `TShockAPI.DB`

## 类型定义
- **is**
- **for**
- **ResearchDatastore**

## 方法
### `public ResearchDatastore(IDbConnection db)`
A valid connection to the TShock database

### `Dictionary<int, int> GetSacrificedItems()`

### `Dictionary<int, int> ReadFromDatabase()`
A dictionary of ItemID keys and Amount Sacrificed values.

### `int SacrificeItem(int itemId, int amount, TSPlayer player)`
The cumulative total sacrifices for this item.

---

# DB/SqlColumn.cs

**命名空间**: `TShockAPI.DB`

## 类型定义
- **SqlColumn**
- **SqlColumnException** : `E`

## 方法
### `public SqlColumn(string name, MySqlDbType type, int? length)`

---

# DB/SqlTable.cs

**命名空间**: `TShockAPI.DB`

## 类型定义
- **SqlTable**
- **SqlTableCreator**

## 方法
### `public SqlTable(string name, List<SqlColumn> columns)`

### `public SqlTableCreator(IDbConnection db, IQueryBuilder provider)`

### `false if it was not.
		public bool EnsureTableStructure(SqlTable table)`

### `List<string> GetColumns(SqlTable table)`

### `void DeleteRow(string table, List<SqlValue> wheres)`

---

# DB/SqlValue.cs

**命名空间**: `TShockAPI.DB`

## 类型定义
- **SqlValue**
- **SqlTableEditor**

## 方法
### `public SqlValue(string name, object value)`

### `public SqlTableEditor(IDbConnection db, IQueryBuilder provider)`

### `void UpdateValues(string table, List<SqlValue> values, List<SqlValue> wheres)`

### `void InsertValues(string table, List<SqlValue> values)`

### `List<object> ReadColumn(string table, string column, List<SqlValue> wheres)`

---

# DB/TileManager.cs

**命名空间**: `TShockAPI.DB`

## 类型定义
- **TileManager**
- **TileBan** : `I`
- **instead**

## 方法
### `public TileManager(IDbConnection db)`

### `void UpdateBans()`

### `void AddNewBan(short id = 0)`

### `void RemoveBan(short id)`

### `bool TileIsBanned(short id)`

### `bool TileIsBanned(short id, TSPlayer ply)`

### `bool AllowGroup(short id, string name)`

### `bool RemoveGroup(short id, string group)`

### `TileBan GetBanById(short id)`

### `public TileBan()`

### `bool Equals(TileBan other)`

### `bool HasPermissionToPlaceTile(TSPlayer ply)`

### `void SetAllowedGroups(string groups)`

### `bool RemoveGroup(string groupName)`

### `override string ToString()`

---

# DB/UserManager.cs

**命名空间**: `TShockAPI.DB`

## 类型定义
- **UserAccountManager**
- **UserAccount** : `I`
- **UserAccountManagerException** : `E`
- **UserAccountExistsException** : `U`
- **UserAccountNotExistException** : `U`
- **UserGroupUpdateLockedException** : `U`
- **GroupNotExistsException** : `U`

## 方法
### `public UserAccountManager(IDbConnection db)`
A UserAccountManager object.

### `void AddUserAccount(UserAccount account)`
The user account to be added

### `void RemoveUserAccount(UserAccount account)`
The user account

### `void SetUserAccountPassword(UserAccount account, string password)`
The user account password to be set

### `void SetUserAccountUUID(UserAccount account, string uuid)`
The user account uuid to be set

### `void SetUserGroup(UserAccount account, string group)`
The user account group to be set

### `void SetUserGroup(TSPlayer author, UserAccount account, string group)`
The user account group to be set

### `void UpdateLogin(UserAccount account)`
The user account object to modify.

### `int GetUserAccountID(string username)`
The user account ID

### `UserAccount GetUserAccountByName(string name)`
The user account object returned from the search.

### `UserAccount GetUserAccountByID(int id)`
The user account object returned from the search.

### `UserAccount GetUserAccount(UserAccount account)`
The user object that is returned from the search.

### `List<UserAccount> GetUserAccounts()`
The user accounts from the database.

### `List<UserAccount> GetUserAccountsByName(string username, bool notAtStart = false)`
Matching users or null if exception is thrown

### `UserAccount LoadUserAccountFromResult(UserAccount account, QueryResult result)`
The 'filled out' user object.

### `public UserAccount(string name, string pass, string uuid, string group, string registered, string last, string known)`
A completed user account object.

### `public UserAccount()`
A user account object.

### `bool VerifyPassword(string password)`
bool true, if the password matched, or false, if it didn't.

### `void UpgradePasswordWorkFactor(string password)`
The raw user account password (unhashed) to upgrade

### `void CreateBCryptHash(string password)`
The plain text password to hash

### `void CreateBCryptHash(string password, int workFactor)`
The work factor to use in generating the password hash

### `bool Equals(UserAccount other)`
An  to compare with this .

### `override bool Equals(object obj)`
An  to compare with this .

### `override int GetHashCode()`
A hash code for the current .

---

# DB/WarpsManager.cs

**命名空间**: `TShockAPI.DB`

## 类型定义
- **WarpManager**
- **Warp**

## 方法
### `internal WarpManager(IDbConnection db)`

### `void ReloadWarps()`

### `Warp Find(string warpName)`
The warp, if it exists, or else null.

### `bool Position(string warpName, int x, int y)`
Whether the operation succeeded.

### `bool Hide(string warpName, bool state)`
Whether the operation succeeded.

### `public Warp(Point position, string name, bool isPrivate = false)`

### `public Warp()`
Creates a warp with a default coordinate of zero, an empty name, public.
