# Commands.cs

**命名空间**: `TShockAPI`

## 类型定义
- **CommandArgs** : `E`
- **Command**
- **Commands**

## 方法
### `public CommandArgs(string message, TSPlayer ply, List<string> args)`

### `public CommandArgs(string message, bool silent, TSPlayer ply, List<string> args)`

### `public Command(CommandDelegate cmd, params string[] names)`

### `bool Run(CommandArgs args)`

### `bool Run(string msg, bool silent, TSPlayer ply, List<string> parms)`

### `bool Run(string msg, TSPlayer ply, List<string> parms)`

### `bool HasAlias(string name)`

### `bool CanRun(TSPlayer ply)`

### `static void InitCommands()`

### `static bool HandleCommand(TSPlayer player, string text)`

### `tried to execute(args omitted)`

### `static List<String> ParseParameters(string str)`

### `static bool IsWhiteSpace(char c)`

### `Account commands

		private static void AttemptLogin(CommandArgs args)`

### `static void Logout(CommandArgs args)`

### `static void PasswordUser(CommandArgs args)`

### `static void RegisterUser(CommandArgs args)`

### `static void ManageUsers(CommandArgs args)`

### `Stupid commands

		private static void ServerInfo(CommandArgs args)`

### `static void WorldInfo(CommandArgs args)`

### `Player Management Commands

		private static void GrabUserUserInfo(CommandArgs args)`

### `static void ViewAccountInfo(CommandArgs args)`

### `static void Kick(CommandArgs args)`

### `static void Ban(CommandArgs args)`

### `Provides extended help on specific ban commands

			void Help()`

### `void MoreHelp(string cmd)`

### `void DisplayBanDetails(Ban ban)`

### `AddBanResult DoBan(string ident, string reason, DateTime expiration)`

### `void AddBan()`

### `void DelBan()`

### `void ListBans()`

### `string PickColorForBan(Ban ban)`

### `void BanDetails()`

### `static void Whitelist(CommandArgs args)`

### `static void DisplayLogs(CommandArgs args)`

### `static void SaveSSC(CommandArgs args)`

### `static void OverrideSSC(CommandArgs args)`

### `static void UploadJoinData(CommandArgs args)`

### `static void ForceHalloween(CommandArgs args)`

### `static void ForceXmas(CommandArgs args)`

### `static void TempGroup(CommandArgs args)`

### `static void SubstituteUser(CommandArgs args)`

### `Executes a command as a superuser if you have sudo rights.
		private static void SubstituteUserDo(CommandArgs args)`

### `static void Broadcast(CommandArgs args)`

### `static void Off(CommandArgs args)`

### `static void OffNoSave(CommandArgs args)`

### `static void CheckUpdates(CommandArgs args)`

### `static void ManageRest(CommandArgs args)`

### `static void ManageWorldEvent(CommandArgs args)`

### `void FailedPermissionCheck()`

### `static void DropMeteor(CommandArgs args)`

### `static void Fullmoon(CommandArgs args)`

### `static void Bloodmoon(CommandArgs args)`

### `static void Eclipse(CommandArgs args)`

### `static void Invade(CommandArgs args)`

### `static void Sandstorm(CommandArgs args)`

### `static void Rain(CommandArgs args)`

### `static void LanternsNight(CommandArgs args)`

### `static void MeteorShower(CommandArgs args)`

### `static void ClearAnglerQuests(CommandArgs args)`

### `static void ChangeWorldMode(CommandArgs args)`

### `static void Hardmode(CommandArgs args)`

### `static void SwitchEvil(CommandArgs args)`

### `static void SpawnBoss(CommandArgs args)`

### `static void SpawnMob(CommandArgs args)`

### `Teleport Commands

		private static void Home(CommandArgs args)`

### `static void Spawn(CommandArgs args)`

### `static void TP(CommandArgs args)`

### `static void TPHere(CommandArgs args)`

### `static void TPNpc(CommandArgs args)`

### `static void GetPos(CommandArgs args)`

### `static void TPPos(CommandArgs args)`

### `static void TPAllow(CommandArgs args)`

### `static void Warp(CommandArgs args)`

### `Item Management

		private static void ItemBan(CommandArgs args)`

### `Projectile Management

		private static void ProjectileBan(CommandArgs args)`

### `Tile Management
		private static void TileBan(CommandArgs args)`

### `Server Config Commands

		private static void SetSpawn(CommandArgs args)`

### `static void SetDungeon(CommandArgs args)`

### `static void Reload(CommandArgs args)`

### `static void ServerPassword(CommandArgs args)`

### `static void Save(CommandArgs args)`

### `static void Settle(CommandArgs args)`

### `static void MaxSpawns(CommandArgs args)`

### `static void SpawnRate(CommandArgs args)`

### `Commands

		private static void Time(CommandArgs args)`

### `static void Slap(CommandArgs args)`

### `static void Wind(CommandArgs args)`

### `Region Commands

		private static void Region(CommandArgs args)`

### `World Protection Commands

		private static void ToggleAntiBuild(CommandArgs args)`

### `static void ProtectSpawn(CommandArgs args)`

### `General Commands

		private static void Help(CommandArgs args)`

### `static void GetVersion(CommandArgs args)`

### `static void ListConnectedPlayers(CommandArgs args)`

### `static void SetupToken(CommandArgs args)`

### `static void ThirdPerson(CommandArgs args)`

### `static void PartyChat(CommandArgs args)`

### `static void Mute(CommandArgs args)`

### `static void Motd(CommandArgs args)`

### `static void Rules(CommandArgs args)`

### `static void Whisper(CommandArgs args)`

### `static void Wallow(CommandArgs args)`

### `static void Reply(CommandArgs args)`

### `static void Annoy(CommandArgs args)`

### `static void Rocket(CommandArgs args)`

### `static void FireWork(CommandArgs args)`

### `static void Aliases(CommandArgs args)`

### `static void CreateDumps(CommandArgs args)`

### `static void SyncLocalArea(CommandArgs args)`

### `static void ShowDeath(CommandArgs args)`

### `static void ShowPVPDeath(CommandArgs args)`

### `static void ShowAllDeath(CommandArgs args)`

### `static void ShowAllPVPDeath(CommandArgs args)`

### `static void BossDamage(CommandArgs args)`

### `Game Commands

		private static void Clear(CommandArgs args)`

### `static void Kill(CommandArgs args)`

### `static void Respawn(CommandArgs args)`

### `static void Butcher(CommandArgs args)`

### `static void Item(CommandArgs args)`

### `static void RenameNPC(CommandArgs args)`

### `static void Give(CommandArgs args)`

### `static void Heal(CommandArgs args)`

### `static void Buff(CommandArgs args)`

### `static void GBuff(CommandArgs args)`

### `static void Grow(CommandArgs args)`

### `bool rejectCannotGrowEvil()`

### `bool prepareAreaForGrow(ushort groundType = TileID.Grass, bool evil = false)`

### `bool growTree(ushort groundType, string fancyName, bool evil = false)`

### `bool growTreeByType(ushort groundType, string fancyName, ushort typeToPrepare = 2, bool evil = false)`

### `bool growPalmTree(ushort sandType, ushort supportingType, string properName, bool evil = false)`

### `static void ToggleGodMode(CommandArgs args)`
