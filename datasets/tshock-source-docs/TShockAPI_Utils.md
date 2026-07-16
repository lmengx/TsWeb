# Utils.cs

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
- **Utils**

## 方法
### `private Utils()`
Utils - Creates a utilities object.

### `string GetRealIP(string mess)`
A string IPv4 address.

### `void SaveWorld()`

### `void Broadcast(string msg, byte red, byte green, byte blue)`
blue - The amount of blue (0-255) in the color for the supported destinations.

### `void Broadcast(string msg, Color color)`
color - The color object for supported destinations.

### `void Broadcast(int ply, string msg, byte red, byte green, byte blue)`
blue - The amount of blue (0-255) in the color for the supported destinations.

### `void SendLogs(string log, Color color, TSPlayer excludedPlayer = null)`
The player to not send the message to.

### `int GetActivePlayerCount()`
The number of active players on the server.

### `void GetRandomClearTileWithInRange(int startTileX, int startTileY, int tileXRange, int tileYRange,
																							out int tileX, out int tileY)`
Y location

### `bool TilePlacementValid(int tileX, int tileY)`
If the tile is valid

### `bool TileSolid(int tileX, int tileY)`
The tile's solidity.

### `List<Item> GetItemByIdOrName(string text)`
A list of matching items.

### `Item GetItemById(int id)`
Item

### `List<Item> GetItemByName(string name)`
List of Items

### `Item GetItemFromTag(string tag)`
The item represented by the tag.

### `List<NPC> GetNPCByIdOrName(string idOrName)`
List of NPCs

### `NPC GetNPCById(int id)`
NPC

### `List<NPC> GetNPCByName(string name)`
List of matching NPCs

### `string GetBuffName(int id)`
name

### `string GetBuffDescription(int id)`
description

### `List<int> GetBuffByName(string name)`
Matching list of buff ids

### `string GetPrefixById(int id)`
Prefix name

### `List<int> GetPrefixByName(string name)`
List of prefix IDs

### `List<int> GetPrefixByIdOrName(string idOrName)`
List of prefix IDs

### `void StopServer(bool save = true, string reason = "Server shutting down!")`
string reason (default: "Server shutting down!")

### `void Reload()`

### `string GetIPv4AddressFromHostname(string hostname)`
string ip

### `bool HasWorldReachedMaxChests()`
True if the entire chest array is used

### `bool TryParseTime(string str, out int seconds)`
Whether the string was parsed successfully.

### `bool TryParseTime(string str, out ulong seconds)`
Whether the string was parsed successfully.

### `int SearchProjectile(short identity, int owner)`
projectile ID

### `IEnumerable<Point> EnumerateRegionBoundaries(Rectangle regionArea)`
The enumerated boundary points.

### `int? EncodeColor(Color? color)`
int? - The encoded color

### `Color? DecodeColor(int? encodedColor)`
Color? - The decoded color

### `int? EncodeBoolArray(bool[] bools)`
The encoded int.

### `bool[] DecodeBoolArray(int? encodedbools)`
The resulting Boolean Array.

### `byte? EncodeBitsByte(BitsByte? bitsByte)`
byte? - The converted byte

### `BitsByte? DecodeBitsByte(int? encodedBitsByte)`
BitsByte? - The decoded bitsbyte object

### `HttpWebResponse GetResponseNoException(HttpWebRequest req)`
HttpWebResponse - The response object.

### `string ColorTag(string text, Color color)`
The , surrounded by the color tag with the appropriated hex code.

### `string ItemTag(Item item)`
The  NetID surrounded by the item tag with proper stack/prefix data.

### `List<Point> GetMassWireOperationRange(Point start, Point end, bool direction = false)`

### `void Dump(bool exit = true)`

### `void PrepareLangForDump()`

### `void DumpPermissionMatrix(string path)`
The save destination.

### `void StartInvasion(int type)`
The invasion type id.

### `void FixChestStacks()`
Verifies that each stack in each chest is valid and not over the max stack count.

### `void SetConsoleTitle(bool empty)`
If the server is empty; determines if we should use Utils.GetActivePlayerCount() for player count or 0.

### `static float Distance(Vector2 value1, Vector2 value2)`
The distance between the two vectors.

### `static bool IsInSpawn(int x, int y)`
If the given x,y location is in the spawn area.

### `void ComputeMaxStyles()`
Computes the max styles...
