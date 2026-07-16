# Bouncer.cs

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
- **Bouncer**
- **that**
- **BuffLimit**

## 方法
### `internal Bouncer()`
A new Bouncer.

### `void OnGetSection(object sender, GetDataHandlers.GetSectionEventArgs args)`

### `void OnPlayerUpdate(object sender, GetDataHandlers.PlayerUpdateEventArgs args)`
The packet arguments that the event has.

### `void OnTileEdit(object sender, GetDataHandlers.TileEditEventArgs args)`
The packet arguments that the event has.

### `void GetRollbackRectSize(int tileX, int tileY, out byte width, out byte length, out int offsetY)`
The Y offset from the initial tile Y that the rectangle should begin at

### `Checks for the presence of tile objects above the tile being checked
			void CheckForTileObjectsAbove(out byte objWidth, out byte objLength, out int yOffset)`

### `void OnItemDrop(object sender, GetDataHandlers.ItemDropEventArgs args)`
The packet arguments that the event has.

### `void OnNewProjectile(object sender, GetDataHandlers.NewProjectileEventArgs args)`
The packet arguments that the event has.

### `void OnNPCStrike(object sender, GetDataHandlers.NPCStrikeEventArgs args)`
The packet arguments that the event has.

### `void OnProjectileKill(object sender, GetDataHandlers.ProjectileKillEventArgs args)`
The packet arguments that the event has.

### `void OnChestItemChange(object sender, GetDataHandlers.ChestItemEventArgs args)`
The packet arguments that the event has.

### `void OnChestOpen(object sender, GetDataHandlers.ChestOpenEventArgs args)`
The packet arguments that the event has.

### `void OnPlaceChest(object sender, GetDataHandlers.PlaceChestEventArgs args)`
The packet arguments that the event has.

### `void OnPlayerZone(object sender, GetDataHandlers.PlayerZoneEventArgs args)`
The packet arguments that the event has.

### `void OnPlayerAnimation(object sender, GetDataHandlers.PlayerAnimationEventArgs args)`
args

### `void OnLiquidSet(object sender, GetDataHandlers.LiquidSetEventArgs args)`
The packet arguments that the event has.

### `void Reject(string reason)`

### `void OnPlayerBuff(object sender, GetDataHandlers.PlayerBuffEventArgs args)`
The packet arguments that the event has.

### `void Reject(bool shouldResync = true)`

### `void OnNPCAddBuff(object sender, GetDataHandlers.NPCAddBuffEventArgs args)`
The packet arguments that the event has.

### `void OnUpdateNPCHome(object sender, GetDataHandlers.NPCHomeChangeEventArgs args)`
The packet arguments that the event has.

### `void OnHealOtherPlayer(object sender, GetDataHandlers.HealOtherPlayerEventArgs args)`
The packet arguments that the event has.

### `void OnReleaseNPC(object sender, GetDataHandlers.ReleaseNpcEventArgs args)`
The packet arguments that the event has.

### `void rejectForCritterNotReleasedFromItem()`

### `void OnPlaceObject(object sender, GetDataHandlers.PlaceObjectEventArgs args)`
The packet arguments that the event has.

### `void OnPlaceTileEntity(object sender, GetDataHandlers.PlaceTileEntityEventArgs args)`
The packet arguments that the event has.

### `void OnPlaceItemFrame(object sender, GetDataHandlers.PlaceItemFrameEventArgs args)`
The packet arguments that the event has.

### `void OnPlayerPortalTeleport(object sender, GetDataHandlers.TeleportThroughPortalEventArgs args)`

### `void OnGemLockToggle(object sender, GetDataHandlers.GemLockToggleEventArgs args)`
The packet arguments that the event has.

### `void OnMassWireOperation(object sender, GetDataHandlers.MassWireOperationEventArgs args)`
The packet arguments that the event has.

### `void OnPlayerDamage(object sender, GetDataHandlers.PlayerDamageEventArgs args)`
The packet arguments that the event has.

### `void OnKillMe(object sender, GetDataHandlers.KillMeEventArgs args)`
The packet arguments that the event has.

### `Would be nice to handle all logic, should we move handling of custom death messages and excessive length to GetDataHandlers?
			void Reject()`

### `void OnFishOutNPC(object sender, GetDataHandlers.FishOutNPCEventArgs args)`

### `void OnFoodPlatterTryPlacing(object sender, GetDataHandlers.FoodPlatterTryPlacingEventArgs args)`

### `void OnProjectileDirtFluidKill(Terraria.Projectile sender, HookEvents.Terraria.Projectile.Kill_DirtAndFluidProjectiles_RunDelegateMethodPushUpForHalfBricksEventArgs args)`

### `void OnDisplayJarTryPlacing(object sender, GetDataHandlers.DisplayJarTryPlacingEventArgs args)`

### `void OnQuickStack(object sender, OTAPI.Hooks.Chest.QuickStackEventArgs args)`

### `static void OnChestCraftRequest(object sender, HookEvents.Terraria.GameContent.CraftingRequests.CanCraftFromChestEventArgs args)`

### `void OnSecondUpdate()`

### `static int GetMaxPlaceStyle(int tileID)`
The max , otherwise -1 if there's no association
