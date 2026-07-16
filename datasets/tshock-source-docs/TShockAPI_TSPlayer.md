# TSPlayer.cs

**命名空间**: `TShockAPI`

## 类型定义
- **DisableFlags**
- **based**
- **ConnectionState** : `i`
- **TSPlayer**
- **BuildPermissionFailPoint**
- **TSRestPlayer** : `T`

## 枚举值
| 名称 | 值 | 说明 |
|---|---|---|
| `None` |  |  |
| `WriteToConsole` |  |  |
| `WriteToLog` |  |  |
| `WriteToLogAndConsole` |  |  |

## 方法
### `static List<TSPlayer> FindByNameOrID(string search)`
A list of matching players

### `bool IsBouncerThrottled()`
If the player is currently being throttled by Bouncer, or not.

### `bool IsBeingDisabled()`
If any of the checks that warrant disabling are set on this player. If true, Disable() is repeatedly called on them.

### `bool HasHackedItemStacks(bool shouldWarnPlayer = false)`
True if any stacks don't conform.

### `bool IsInRange(int x, int y, int range = 32)`
True if the player is in range of a tile or if range checks are off. False if not.

### `bool HasBuildPermission(int x, int y, bool shouldWarnPlayer = true)`
True if the player can build at the given point from build, spawn, and region protection.

### `bool HasBuildPermissionForTileObject(int x, int y, int width, int height, bool shouldWarnPlayer = true)`
True if the player can build at the given point from build, spawn, and region protection.

### `bool HasPaintPermission(int x, int y)`
True if they can paint.

### `bool HasModifiedIceSuccessfully(int x, int y, short tileType, GetDataHandlers.EditAction editAction)`
True if a player successfully places an ice tile or removes one of their past ice tiles.

### `bool SaveServerCharacter()`
bool - True/false if it saved successfully

### `bool SendServerCharacter()`
bool - True/false if it saved successfully

### `bool ContainsData(string key)`

### `object RemoveData(string key)`
The removed object.

### `void Logout()`

### `public TSPlayer(int index)`
The player's index in the.

### `protected TSPlayer(String playerName)`
The player's name.

### `virtual void Disconnect(string reason)`
The reason why the player was disconnected.

### `void TempGroupTimerElapsed(object sender, ElapsedEventArgs args)`

### `bool Teleport(Point tilePos, bool useBottom = false, byte style = 1)`
True or false.

### `bool Teleport(Vector2 pos, bool useBottom = false, byte style = 1)`
True or false.

### `bool TeleportCentered(Vector2 pos, byte style = 1)`
True or false.

### `bool Teleport(float x, float y, byte style = 1)`
True or false.

### `bool TeleportSpawnpoint()`

### `bool TeleportToWorldSpawn(bool ignoreTeamBasedSpawns = false)`
True or false.

### `void Heal(int health = 600)`
Heal health amount.

### `void Spawn(PlayerSpawnContext context, int? respawnTimer = null)`

### `void Spawn(int tilex, int tiley, PlayerSpawnContext context, int? respawnTimer = null,
				short? numberOfDeathsPVE = null, short? numberOfDeathsPVP = null, int team = -1)`
The team after player spawn.

### `void RemoveProjectile(int index, int owner)`
The projectile's owner.

### `virtual bool SendTileSquare(int x, int y, int size = 10)`

### `virtual bool SendTileSquareCentered(int x, int y, byte size = 10)`
true if the tile square was sent successfully, else false

### `virtual bool SendTileRect(short x, short y, byte width = 10, byte length = 10, TileChangeType changeType = TileChangeType.None)`

### `The server will assume that the zone is not loaded on the player, and will resend the data, but with earth blocks.
		public void UpdateSection(Rectangle? rectangle = null, bool isLoaded = false)`

### `bool GiveItemCheck(int type, string name, int stack, int prefix = 0)`
True or false, depending if the item passed the check or not.

### `virtual void GiveItem(int type, int stack, int prefix = 0)`
The item prefix.

### `virtual void GiveItem(NetItem item)`
Item with data to be given to the player.

### `void GiveItemDirectly(int type, int stack, int prefix)`

### `void SendItemSlotPacketFor(int slot)`

### `Item GiveItem_FillAmmo(Item item)`

### `Item GiveItemDirectly_FillIntoOccupiedSlot(Item item, int slot)`

### `Item GiveItemDirectly_FillEmptyInventorySlot(Item item, int slot)`

### `void GiveItemByDrop(int type, int stack, int prefix)`

### `virtual void SendInfoMessage(string msg)`
The message.

### `void SendInfoMessage(string format, params object[] args)`
An array of objects to format.

### `virtual void SendSuccessMessage(string msg)`
The message.

### `void SendSuccessMessage(string format, params object[] args)`
An array of objects to format.

### `virtual void SendWarningMessage(string msg)`
The message.

### `void SendWarningMessage(string format, params object[] args)`
An array of objects to format.

### `virtual void SendErrorMessage(string msg)`
The message.

### `void SendErrorMessage(string format, params object[] args)`
An array of objects to format.

### `virtual void SendMessage(string msg, Color color)`
The message color.

### `virtual void SendMessage(string msg, byte red, byte green, byte blue)`
The amount of blue color to factor in. Max: 255

### `virtual void SendMessageFromPlayer(string msg, byte red, byte green, byte blue, int ply)`
The player who receives the message.

### `void SendFileTextAsMessage(string file)`
Filename relative to

### `virtual void DamagePlayer(int damage)`
The amount of damage the player will take.

### `virtual void DamagePlayer(int damage, PlayerDeathReason reason)`
The reason for causing damage to player.

### `virtual void KillPlayer()`

### `virtual void KillPlayer(PlayerDeathReason reason)`
Reason for killing a player.

### `virtual void SetTeam(int team)`
The team color index.

### `virtual void SetPvP(bool mode, bool withMsg = false)`
Whether a chat message about the change should be sent.

### `virtual void Disable(string reason = "", DisableFlags flags = DisableFlags.WriteToLog)`
Flags to dictate where this event is logged to.

### `bool Kick(string reason, bool force = false, bool silent = false, string adminUserName = null, bool saveSSI = false)`
If the player's server side character should be saved on kick.

### `bool Ban(string reason, string adminUserName = null)`
The player who initiated the ban.

### `void SendMultipleMatchError(IEnumerable<object> matches)`
An enumerable list with the matches

### `void LogStackFrame()`

### `virtual void Whoopie(object time)`
The

### `virtual void SetBuff(int type, int time = 3600, bool bypass = false)`

### `virtual void SendData(PacketTypes msgType, string text = "", int number = 0, float number2 = 0f,
			float number3 = 0f, float number4 = 0f, int number5 = 0)`

### `virtual void SendDataFromPlayer(PacketTypes msgType, int ply, string text = "", float number2 = 0f,
			float number3 = 0f, float number4 = 0f, int number5 = 0)`

### `virtual void SendRawData(byte[] data)`
The data to send.

### `void AddResponse(string name, Action<object> callback)`
The method that will be executed on confirmation ie user accepts

### `bool HasPermission(string permission)`
True if the player has that permission.

### `bool HasPermission(ItemBan bannedItem)`
True if the player has permission to use the banned item.

### `bool HasPermission(ProjectileBan bannedProj)`
True if the player has permission to use the banned projectile.

### `bool HasPermission(TileBan bannedTile)`
True if the player has permission to use the banned tile.

### `List<string> GetCommandOutput()`
