# GetDataHandlers.cs

**命名空间**: `TShockAPI`

## 类型定义
- **GetDataHandlerArgs** : `E`
- **GetDataHandledEventArgs** : `H`
- **GetDataHandlers**
- **PlayerInfoEventArgs** : `G`
- **PlayerSlotEventArgs** : `G`
- **GetSectionEventArgs** : `G`
- **PlayerUpdateEventArgs** : `G`
- **PlayerHPEventArgs** : `G`
- **TileEditEventArgs** : `G`
- **DoorUseEventArgs** : `G`
- **SendTileRectEventArgs** : `G`
- **ItemDropEventArgs** : `G`
- **NewProjectileEventArgs** : `G`
- **NPCStrikeEventArgs** : `G`
- **ProjectileKillEventArgs** : `G`
- **TogglePvpEventArgs** : `G`
- **SpawnEventArgs** : `G`
- **ChestItemEventArgs** : `G`
- **ChestOpenEventArgs** : `G`
- **PlaceChestEventArgs** : `G`
- **PlayerZoneEventArgs** : `G`
- **NpcTalkEventArgs** : `G`
- **PlayerAnimationEventArgs** : `G`
- **PlayerManaEventArgs** : `G`
- **PlayerTeamEventArgs** : `G`
- **SignReadEventArgs** : `G`
- **SignEventArgs** : `G`
- **LiquidSetEventArgs** : `G`
- **LiquidType** : `b`
- **PlayerBuffUpdateEventArgs** : `G`
- **NPCSpecialEventArgs** : `G`
- **NPCAddBuffEventArgs** : `G`
- **PlayerBuffEventArgs** : `G`
- **HouseholdStatus** : `b`
- **NPCHomeChangeEventArgs** : `G`
- **PaintTileEventArgs** : `G`
- **PaintWallEventArgs** : `G`
- **TeleportEventArgs** : `G`
- **HealOtherPlayerEventArgs** : `G`
- **ReleaseNpcEventArgs** : `G`
- **PlaceObjectEventArgs** : `G`
- **PlaceTileEntityEventArgs** : `G`
- **PlaceItemFrameEventArgs** : `G`
- **TeleportThroughPortalEventArgs** : `G`
- **GemLockToggleEventArgs** : `G`
- **MassWireOperationEventArgs** : `G`
- **PlayerDamageEventArgs** : `G`
- **KillMeEventArgs** : `G`
- **EmojiEventArgs** : `G`
- **DisplayDollInventoryID**
- **DisplayDollItemSyncEventArgs** : `G`
- **DisplayDollPoseSyncEventArgs** : `G`
- **RequestTileEntityInteractionEventArgs** : `G`
- **SyncTilePickingEventArgs** : `G`
- **LandGolfBallInCupEventArgs** : `G`
- **FishOutNPCEventArgs** : `G`
- **FoodPlatterTryPlacingEventArgs** : `G`
- **DisplayJarTryPlacingEventArgs** : `G`
- **ReadNetModuleEventArgs** : `G`
- **DoorAction**
- **EditAction**
- **EditType**
- **ProjectileStruct**
- **NetModuleType**
- **CreativePowerTypes**

## 枚举值
| 名称 | 值 | 说明 |
|---|---|---|
| `Water` | 0 |  |
| `Lava` | 1 |  |
| `Honey` | 2 |  |
| `Shimmer` | 3 |  |
| `Removal` | 255 |  |

## 方法
### `public GetDataHandlerArgs(TSPlayer player, MemoryStream data)`

### `static void InitGetDataHandler()`

### `Client only sends when recieving FinishedConnectingToServer(packet 129)`

### `static bool HandlerGetData(PacketTypes type, TSPlayer player, MemoryStream data)`

### `static bool OnPlayerInfo(TSPlayer player, MemoryStream data, byte _plrid, byte _voiceVariant, float _voicePitchOffset,
			byte _hair, int _style, byte _difficulty, string _name)`

### `static bool OnPlayerSlot(TSPlayer player, MemoryStream data, byte _plr, short _slot, short _stack, byte _prefix, short _type, bool _favorited, bool _blockedSlot)`

### `static bool OnGetSection(TSPlayer player, MemoryStream data, int x, int y, byte team)`

### `static bool OnPlayerHP(TSPlayer player, MemoryStream data, byte _plr, short _cur, short _max)`

### `static bool OnTileEdit(TSPlayer ply, MemoryStream data, int x, int y, EditAction action, EditType editDetail, short editData, byte style)`

### `static bool OnDoorUse(TSPlayer ply, MemoryStream data, short x, short y, byte direction, DoorAction action)`

### `static bool OnSendTileRect(TSPlayer player, MemoryStream data, short tilex, short tiley, byte width, byte length, TileChangeType changeType = TileChangeType.None)`

### `static bool OnItemDrop(TSPlayer player, MemoryStream data, short id, Vector2 pos, Vector2 vel, short stacks, byte prefix, bool noDelay, short type)`

### `static bool OnNewProjectile(MemoryStream data, short ident, Vector2 pos, Vector2 vel, float knockback, short dmg, byte owner, short type, int index, TSPlayer player, float[] ai)`

### `static bool OnNPCStrike(TSPlayer player, MemoryStream data, short id, byte dir, short dmg, float knockback, byte crit)`

### `static bool OnProjectileKill(TSPlayer player, MemoryStream data, int identity, byte owner, int index)`
bool

### `static bool OnPvpToggled(TSPlayer player, MemoryStream data, byte _id, bool _pvp)`

### `static bool OnPlayerSpawn(TSPlayer player, MemoryStream data, byte pid, int spawnX, int spawnY, int respawnTimer, int numberOfDeathsPVE, int numberOfDeathsPVP,
			byte team, PlayerSpawnContext spawnContext)`

### `static bool OnChestItemChange(TSPlayer player, MemoryStream data, short id, byte slot, short stacks, byte prefix, short type)`

### `static bool OnChestOpen(MemoryStream data, int x, int y, TSPlayer player)`

### `static bool OnPlaceChest(TSPlayer player, MemoryStream data, int flag, int tilex, int tiley, short style)`

### `static bool OnPlayerZone(TSPlayer player, MemoryStream data, byte plr, BitsByte zone1, BitsByte zone2,
			BitsByte zone3, BitsByte zone4, BitsByte zone5, byte townNPCs)`

### `static bool OnNpcTalk(TSPlayer player, MemoryStream data, byte _plr, short _npctarget)`

### `static bool OnPlayerAnimation(TSPlayer player, MemoryStream data)`

### `static bool OnPlayerMana(TSPlayer player, MemoryStream data, byte _plr, short _cur, short _max)`

### `static bool OnPlayerTeam(TSPlayer player, MemoryStream data, byte _id, byte _team)`

### `static bool OnSignRead(TSPlayer player, MemoryStream data, int x, int y)`

### `static bool OnSignEvent(TSPlayer player, MemoryStream data, short id, int x, int y)`

### `static bool OnLiquidSet(TSPlayer player, MemoryStream data, int tilex, int tiley, byte amount, byte type)`

### `static bool OnPlayerBuffUpdate(TSPlayer player, MemoryStream data, byte id)`

### `static bool OnNPCSpecial(TSPlayer player, MemoryStream data, byte id, byte type)`

### `static bool OnNPCAddBuff(TSPlayer player, MemoryStream data, short id, int type, short time)`

### `static bool OnPlayerBuff(TSPlayer player, MemoryStream data, byte id, int type, int time)`

### `static bool OnUpdateNPCHome(TSPlayer player, MemoryStream data, short id, short x, short y, byte houseHoldStatus)`

### `static bool OnPaintTile(TSPlayer player, MemoryStream data, Int32 x, Int32 y, byte t, byte ct)`

### `static bool OnPaintWall(TSPlayer player, MemoryStream data, Int32 x, Int32 y, byte t, byte cw)`

### `static bool OnTeleport(TSPlayer player, MemoryStream data, Int16 id, byte f, float x, float y, byte style, int extraInfo)`

### `static bool OnHealOtherPlayer(TSPlayer player, MemoryStream data, byte targetPlayerIndex, short amount)`

### `static bool OnReleaseNpc(TSPlayer player, MemoryStream data, int _x, int _y, short _type, byte _style)`

### `static bool OnPlaceObject(TSPlayer player, MemoryStream data, short x, short y, short type, short style, byte alternate, sbyte random, bool direction)`

### `static bool OnPlaceTileEntity(TSPlayer player, MemoryStream data, short x, short y, byte type)`

### `static bool OnPlaceItemFrame(TSPlayer player, MemoryStream data, short x, short y, short itemID, byte prefix, short stack, TEItemFrame itemFrame)`

### `static bool OnPlayerTeleportThroughPortal(TSPlayer sender, byte targetPlayerIndex, MemoryStream data, Vector2 position, Vector2 velocity, int colorIndex)`

### `static bool OnGemLockToggle(TSPlayer player, MemoryStream data, short x, short y, bool on)`

### `static bool OnMassWireOperation(TSPlayer player, MemoryStream data, short startX, short startY, short endX, short endY, byte toolMode)`

### `static bool OnPlayerDamage(TSPlayer player, MemoryStream data, byte id, byte dir, short dmg, bool pvp, bool crit, sbyte cooldownCounter, PlayerDeathReason playerDeathReason)`

### `static bool OnKillMe(TSPlayer player, MemoryStream data, byte plr, byte direction, short damage, bool pvp, PlayerDeathReason playerDeathReason)`

### `static bool OnEmoji(TSPlayer player, MemoryStream data, byte playerIndex, byte emojiID)`

### `static bool OnDisplayDollItemSync(TSPlayer player, MemoryStream data, byte playerIndex, int tileEntityID, TEDisplayDoll displayDollEntity, int slot, DisplayDollInventoryID inventoryID, Item oldItem, Item newItem)`

### `static bool OnDisplayDollPoseSync(TSPlayer player, MemoryStream data, byte playerIndex, int tileEntityID, TEDisplayDoll displayDollEntity, byte pose)`

### `static bool OnRequestTileEntityInteraction(TSPlayer player, MemoryStream data, TileEntity tileEntity, byte playerIndex)`

### `static bool OnSyncTilePicking(TSPlayer player, MemoryStream data, byte playerIndex, short tileX, short tileY, byte tileDamage)`

### `static bool OnLandGolfBallInCup(TSPlayer player, MemoryStream data, byte playerIndex, ushort tileX, ushort tileY, ushort hits, ushort projectileType)`

### `static bool OnFishOutNPC(TSPlayer player, MemoryStream data, ushort tileX, ushort tileY, short npcID)`

### `static bool OnFoodPlatterTryPlacing(TSPlayer player, MemoryStream data, short tileX, short tileY, short itemID, byte prefix, short stack)`

### `static bool OnDisplayJarTryPlacing(TSPlayer player, MemoryStream data, ushort tileX, ushort tileY, short itemID, byte prefix, short stack)`

### `static bool OnReadNetModule(TSPlayer player, MemoryStream data, NetModuleType moduleType)`

### `static bool HandlePlayerInfo(GetDataHandlerArgs args)`

### `static bool HandlePlayerSlot(GetDataHandlerArgs args)`

### `static int NetworkSlotToInternalSlot(int networkSlot)`

### `static bool HandleConnecting(GetDataHandlerArgs args)`

### `static bool HandleGetSection(GetDataHandlerArgs args)`

### `static bool HandleSpawn(GetDataHandlerArgs args)`

### `static bool HandlePlayerUpdate(GetDataHandlerArgs args)`

### `static bool HandlePlayerHp(GetDataHandlerArgs args)`

### `static bool HandleTile(GetDataHandlerArgs args)`

### `static bool HandleDoorUse(GetDataHandlerArgs args)`

### `static bool HandleSendTileRect(GetDataHandlerArgs args)`

### `static bool HandleItemDrop(GetDataHandlerArgs args)`

### `static bool HandleItemOwner(GetDataHandlerArgs args)`

### `static bool HandleNpcItemStrike(GetDataHandlerArgs args)`

### `static bool HandleProjectileNew(GetDataHandlerArgs args)`

### `static bool HandleNpcStrike(GetDataHandlerArgs args)`

### `static bool HandleProjectileKill(GetDataHandlerArgs args)`

### `static bool HandleTogglePvp(GetDataHandlerArgs args)`

### `static bool HandleChestOpen(GetDataHandlerArgs args)`

### `static bool HandleChestItem(GetDataHandlerArgs args)`

### `static bool HandleChestActive(GetDataHandlerArgs args)`

### `static bool HandlePlaceChest(GetDataHandlerArgs args)`

### `static bool HandlePlayerZone(GetDataHandlerArgs args)`

### `static bool HandlePassword(GetDataHandlerArgs args)`

### `static bool HandleNpcTalk(GetDataHandlerArgs args)`

### `static bool HandlePlayerAnimation(GetDataHandlerArgs args)`

### `static bool HandlePlayerMana(GetDataHandlerArgs args)`

### `static bool HandlePlayerTeam(GetDataHandlerArgs args)`

### `static bool HandleSignRead(GetDataHandlerArgs args)`

### `static bool HandleSign(GetDataHandlerArgs args)`

### `static bool HandleLiquidSet(GetDataHandlerArgs args)`

### `static bool HandlePlayerBuffList(GetDataHandlerArgs args)`

### `static bool HandleSpecial(GetDataHandlerArgs args)`

### `HandleSpecial rejected enchanted sundial permission(ForceTime)`

### `static bool HandleNPCAddBuff(GetDataHandlerArgs args)`

### `static bool HandlePlayerAddBuff(GetDataHandlerArgs args)`

### `static bool HandleUpdateNPCHome(GetDataHandlerArgs args)`

### `static bool HandleSpawnBoss(GetDataHandlerArgs args)`

### `static bool HandlePaintTile(GetDataHandlerArgs args)`

### `static bool HandlePaintWall(GetDataHandlerArgs args)`

### `static bool HandleTeleport(GetDataHandlerArgs args)`

### `static bool HandleHealOther(GetDataHandlerArgs args)`

### `static bool HandleCatchNpc(GetDataHandlerArgs args)`

### `static bool HandleReleaseNpc(GetDataHandlerArgs args)`

### `static bool HandleTeleportationPotion(GetDataHandlerArgs args)`

### `void Fail(string tpItem)`

### `static bool HandleCompleteAnglerQuest(GetDataHandlerArgs args)`

### `static bool HandleNumberOfAnglerQuestsCompleted(GetDataHandlerArgs args)`

### `static bool HandlePlaceObject(GetDataHandlerArgs args)`

### `static bool HandleLoadNetModule(GetDataHandlerArgs args)`

### `static bool HandlePlaceTileEntity(GetDataHandlerArgs args)`

### `static bool HandlePlaceItemFrame(GetDataHandlerArgs args)`

### `static bool HandleSyncExtraValue(GetDataHandlerArgs args)`

### `static bool HandleKillPortal(GetDataHandlerArgs args)`

### `static bool HandlePlayerPortalTeleport(GetDataHandlerArgs args)`

### `static bool HandleNpcTeleportPortal(GetDataHandlerArgs args)`

### `static bool HandleGemLockToggle(GetDataHandlerArgs args)`

### `static bool HandleMassWireOperation(GetDataHandlerArgs args)`

### `static bool HandleToggleParty(GetDataHandlerArgs args)`

### `static bool HandleOldOnesArmy(GetDataHandlerArgs args)`

### `static bool HandlePlayerDamageV2(GetDataHandlerArgs args)`

### `static bool HandlePlayerKillMeV2(GetDataHandlerArgs args)`

### `static bool HandleEmoji(GetDataHandlerArgs args)`

### `static bool HandleTileEntityDisplayDollItemSync(GetDataHandlerArgs args)`

### `bool HandleItemSync(DisplayDollInventoryID inventoryID, Item[] items)`

### `static bool HandleRequestTileEntityInteraction(GetDataHandlerArgs args)`

### `static bool HandleSyncTilePicking(GetDataHandlerArgs args)`

### `static bool HandleSyncRevengeMarker(GetDataHandlerArgs args)`

### `static bool HandleLandGolfBallInCup(GetDataHandlerArgs args)`

### `static bool HandleFishOutNPC(GetDataHandlerArgs args)`

### `static bool HandleFoodPlatterTryPlacing(GetDataHandlerArgs args)`

### `static bool HandleSyncCavernMonsterType(GetDataHandlerArgs args)`

### `static bool HandleSyncLoadout(GetDataHandlerArgs args)`

### `swap around the PlayerData items ourself.

			Tuple<int, int> GetArmorSlotsForLoadoutIndex(int index)`

### `Tuple<int, int> GetDyeSlotsForLoadoutIndex(int index)`

### `static bool HandleDisplayJar(GetDataHandlerArgs args)`
