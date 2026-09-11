using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Rests;
using Terraria;
using Terraria.ID;
using Terraria.IO;
using TShockAPI;
using TShockAPI.DB;

namespace TShockData
{
	/// <summary>
	/// 玩家角色（.plr）导入导出。
	///
	/// 导出链：tsCharacter(DB) → PlayerData → Player → .plr 文件
	///   - to=server   → 保存到服务端 PlayerExports/&lt;世界名&gt;/&lt;玩家名&gt;.plr
	///   - to=download → 返回 base64（前端可下载或转存后端目录）
	/// 导入链：.plr 文件（浏览器上传 / 后端目录 / 服务端目录）
	///   → Player.LoadPlayer() → Player → PlayerData → tsCharacter(DB)（覆盖写入）
	/// </summary>
	public static class PlayerTransfer
	{
		/// <summary>服务端导出根目录（与 /export 聊天命令共用）</summary>
		private static readonly string ExportPath = Path.Combine(TShock.SavePath, "PlayerExports");

		private class MyPlayer : TSPlayer
		{
			public MyPlayer() : base(string.Empty)
			{
				this.Account = new UserAccount();
			}

			public Player Player
			{
				get => this.TPlayer;
				set => typeof(TSPlayer).GetField("FakePlayer",
								System.Reflection.BindingFlags.NonPublic |
								System.Reflection.BindingFlags.Instance)!.
								SetValue(this, value);
			}
		}

		// ═══════════════════════════════════════════════════════════
		// 导出 REST
		// ═══════════════════════════════════════════════════════════

		/// <summary>
		/// GET /data/players/export?username=X&amp;to=download|server
		/// 导出单个玩家角色。to=download 返回 base64；to=server 保存到服务端 PlayerExports/。
		/// </summary>
		public static object ExportRest(RestRequestArgs args)
		{
			string username = GetParam(args, "username");
			string to = GetParam(args, "to") ?? "download";

			if (string.IsNullOrWhiteSpace(username))
				return new RestObject("400") { { "error", "参数 username 为必填" } };

			try
			{
				Player? player = BuildPlayerForUser(username, out UserAccount? account);
				if (player == null || account == null)
					return new RestObject("404") { { "error", $"未找到玩家 {username} 或该玩家没有角色数据" } };

				if (to.Equals("server", StringComparison.OrdinalIgnoreCase))
				{
					string dir = Path.Combine(ExportPath, FormatFileName(Main.worldName));
					Directory.CreateDirectory(dir);
					string filePath = Path.Combine(dir, $"{FormatFileName(account.Name)}.plr");
					if (!Export(player, filePath))
						return new RestObject("500") { { "error", "导出到服务端失败" } };
					return new RestObject()
					{
						{ "response", "已导出到服务端" },
						{ "success", true },
						{ "username", account.Name },
						{ "path", filePath },
						{ "filename", Path.GetFileName(filePath) }
					};
				}

				// to=download：导出到临时文件 → 读回 base64 → 删除
				string tmp = Path.Combine(Path.GetTempPath(), $"tsweb_export_{Guid.NewGuid():N}.plr");
				try
				{
					if (!Export(player, tmp))
						return new RestObject("500") { { "error", "导出失败（数据残缺）" } };
					byte[] bytes = File.ReadAllBytes(tmp);
					return new RestObject()
					{
						{ "response", "导出成功" },
						{ "success", true },
						{ "username", account.Name },
						{ "filename", $"{FormatFileName(account.Name)}.plr" },
						{ "base64", Convert.ToBase64String(bytes) },
						{ "size", bytes.Length }
					};
				}
				finally
				{
					try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
				}
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError("[PlayerTransfer] 导出失败: " + ex);
				return new RestObject("500") { { "error", ex.Message } };
			}
		}

		/// <summary>
		/// GET /data/players/export-all
		/// 批量导出所有有角色数据的玩家到服务端 PlayerExports/&lt;世界名&gt;/，返回文件列表与统计。
		/// </summary>
		public static object ExportAllRest(RestRequestArgs args)
		{
			try
			{
				var all = new List<UserAccount>();
				using (var queryResult = TShock.DB.QueryReader("SELECT DISTINCT Account FROM tsCharacter"))
				{
					while (queryResult.Read())
					{
						int accountId = queryResult.Get<int>("Account");
						var user = TShock.UserAccounts.GetUserAccountByID(accountId);
						if (user != null)
							all.Add(user);
					}
				}

				if (all.Count == 0)
					return new RestObject() { { "response", "没有可导出的角色数据" }, { "success", true }, { "files", new List<object>() }, { "exported", 0 }, { "failed", 0 } };

				string dir = Path.Combine(ExportPath, FormatFileName(Main.worldName));
				Directory.CreateDirectory(dir);

				var files = new List<object>();
				int exported = 0, failed = 0;
				foreach (var one in all)
				{
					try
					{
						var online = TShock.Players.FirstOrDefault(p => p?.Account?.ID == one.ID);
						Player? player = online != null ? online.TPlayer : BuildPlayerForUser(one.Name, out _);
						if (player == null) { failed++; continue; }

						string filePath = Path.Combine(dir, $"{FormatFileName(one.Name)}.plr");
						if (Export(player, filePath))
						{
							exported++;
							files.Add(new Dictionary<string, object>
							{
								{ "name", one.Name },
								{ "filename", Path.GetFileName(filePath) },
								{ "size", new FileInfo(filePath).Length }
							});
						}
						else failed++;
					}
					catch { failed++; }
				}

				return new RestObject()
				{
					{ "response", $"批量导出完成：成功 {exported}，失败 {failed}" },
					{ "success", true },
					{ "files", files },
					{ "exported", exported },
					{ "failed", failed },
					{ "dir", dir }
				};
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError("[PlayerTransfer] 批量导出失败: " + ex);
				return new RestObject("500") { { "error", ex.Message } };
			}
		}

		/// <summary>
		/// GET /data/players/export-list
		/// 列出服务端 PlayerExports/&lt;世界名&gt;/ 下的 .plr 文件。
		/// </summary>
		public static object ListExportsRest(RestRequestArgs args)
		{
			try
			{
				string dir = Path.Combine(ExportPath, FormatFileName(Main.worldName));
				if (!Directory.Exists(dir))
					return new RestObject() { { "success", true }, { "files", new List<object>() } };

				var files = Directory.GetFiles(dir, "*.plr")
					.Select(f => new FileInfo(f))
					.OrderByDescending(f => f.LastWriteTime)
					.Select(f => (object)new Dictionary<string, object>
					{
						{ "filename", f.Name },
						{ "size", f.Length },
						{ "lastModified", f.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") }
					})
					.ToList();

				return new RestObject() { { "success", true }, { "files", files }, { "dir", dir } };
			}
			catch (Exception ex)
			{
				return new RestObject("500") { { "error", ex.Message } };
			}
		}

		// ═══════════════════════════════════════════════════════════
		// 导入 REST
		// ═══════════════════════════════════════════════════════════

		/// <summary>
		/// POST /data/players/import（body/query: username + plrBase64）
		/// 从 base64 的 .plr 内容导入到指定账号（覆盖该账号现有角色数据）。
		/// </summary>
		public static object ImportRest(RestRequestArgs args)
		{
			string username = GetParam(args, "username");
			string plrBase64 = GetParam(args, "plrBase64");

			if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(plrBase64))
				return new RestObject("400") { { "error", "参数 username 和 plrBase64 为必填" } };

			byte[] bytes;
			try
			{
				bytes = Convert.FromBase64String(plrBase64);
			}
			catch
			{
				return new RestObject("400") { { "error", "plrBase64 不是有效的 Base64" } };
			}

			if (bytes.Length == 0 || bytes.Length > 50 * 1024 * 1024)
				return new RestObject("400") { { "error", "plr 文件内容为空或超过 50MB" } };

			string tmp = Path.Combine(Path.GetTempPath(), $"tsweb_import_{Guid.NewGuid():N}.plr");
			try
			{
				File.WriteAllBytes(tmp, bytes);
				return ImportFromFile(username, tmp);
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError("[PlayerTransfer] 导入失败: " + ex);
				return new RestObject("500") { { "error", ex.Message } };
			}
			finally
			{
				try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
			}
		}

		/// <summary>
		/// GET /data/players/import-from-server?username=X&amp;path=Y
		/// 从服务端已存在的 .plr 文件导入（path 相对 PlayerExports/&lt;世界名&gt;/，防穿越）。
		/// </summary>
		public static object ImportFromServerRest(RestRequestArgs args)
		{
			string username = GetParam(args, "username");
			string fileName = GetParam(args, "path");

			if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(fileName))
				return new RestObject("400") { { "error", "参数 username 和 path 为必填" } };

			// 安全：仅允许 PlayerExports/<世界名>/ 下的文件名
			string safeName = Path.GetFileName(fileName.Replace('\\', '/'));
			if (string.IsNullOrEmpty(safeName) || !safeName.EndsWith(".plr", StringComparison.OrdinalIgnoreCase))
				return new RestObject("400") { { "error", "path 必须为 .plr 文件名" } };

			string dir = Path.Combine(ExportPath, FormatFileName(Main.worldName));
			string fullPath = Path.Combine(dir, safeName);
			if (!File.Exists(fullPath))
				return new RestObject("404") { { "error", $"文件不存在: {safeName}" } };

			try
			{
				return ImportFromFile(username, fullPath);
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError("[PlayerTransfer] 从服务端导入失败: " + ex);
				return new RestObject("500") { { "error", ex.Message } };
			}
		}

		/// <summary>
		/// GET /data/players/has-character?username=X
		/// 判断某账号是否有角色数据（用于前端批量导出筛选）。
		/// </summary>
		public static object HasCharacterRest(RestRequestArgs args)
		{
			string username = GetParam(args, "username");
			if (string.IsNullOrWhiteSpace(username))
				return new RestObject("400") { { "error", "参数 username 为必填" } };

			try
			{
				var account = TShock.UserAccounts.GetUserAccountByName(username);
				if (account == null)
					return new RestObject() { { "success", true }, { "hasCharacter", false }, { "exists", false } };

				bool has = false;
				using (var qr = TShock.DB.QueryReader("SELECT Account FROM tsCharacter WHERE Account = @0", account.ID))
					has = qr.Read();

				return new RestObject() { { "success", true }, { "hasCharacter", has }, { "exists", true } };
			}
			catch (Exception ex)
			{
				return new RestObject("500") { { "error", ex.Message } };
			}
		}

		// ═══════════════════════════════════════════════════════════
		// 导入核心
		// ═══════════════════════════════════════════════════════════

		/// <summary>从 .plr 文件导入到指定账号（覆盖 tsCharacter 中该账号的角色数据）</summary>
		private static object ImportFromFile(string username, string plrPath)
		{
			var account = TShock.UserAccounts.GetUserAccountByName(username);
			if (account == null)
				return new RestObject("404") { { "error", $"未找到账号 {username}" } };

			// 1. 加载 .plr 文件 → Player 对象
			PlayerFileData fileData = Player.LoadPlayer(plrPath, false);
			Player? player = fileData?.Player;
			if (player == null || player.loadStatus != StatusID.Ok)
			{
				string status = player?.loadStatus.ToString() ?? "UnknownError";
				return new RestObject("400") { { "error", $"plr 文件解析失败（{status}），文件可能已损坏或版本过新" } };
			}

			// 2. Player → PlayerData（覆盖写库）
			PlayerData data = BuildPlayerDataFromPlayer(player);

			// 3. 写入 tsCharacter（覆盖）
			bool ok = WriteCharacterData(account, data);
			if (!ok)
				return new RestObject("500") { { "error", "写入角色数据库失败" } };

			// 4. 若玩家在线，同步到客户端（使改动立即生效）
			SyncOnlinePlayer(account);

			TShock.Log.ConsoleInfo($"[TSWeb] 已导入角色数据: {username} ← {Path.GetFileName(plrPath)}");
			return new RestObject()
			{
				{ "response", $"已从 {Path.GetFileName(plrPath)} 导入角色数据到 {username}" },
				{ "success", true },
				{ "username", username }
			};
		}

		/// <summary>在线玩家同步（数据已被覆盖写库，重新拉取并推送客户端）</summary>
		private static void SyncOnlinePlayer(UserAccount account)
		{
			try
			{
				var online = TShock.Players.FirstOrDefault(p => p?.Account?.ID == account.ID && p.Active);
				if (online == null) return;

				// 重新从库中读取（含刚写入的数据）并恢复
				online.PlayerData = TShock.CharacterDB.GetPlayerData(online, account.ID);
				online.PlayerData.RestoreCharacter(online);
				online.SendInfoMessage("[TSWeb] 管理员已导入新的角色数据，你的角色已更新。");
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError("[PlayerTransfer] 在线玩家同步失败: " + ex);
			}
		}

		// ═══════════════════════════════════════════════════════════
		// Player ↔ PlayerData 双向转换
		// ═══════════════════════════════════════════════════════════

		/// <summary>Player → PlayerData（与 PlayerData.CopyCharacter 的字段映射一致）</summary>
		private static PlayerData BuildPlayerDataFromPlayer(Player p)
		{
			var data = new PlayerData(false)
			{
				health = p.statLife > 0 ? p.statLife : 1,
				maxHealth = p.statLifeMax,
				mana = p.statMana,
				maxMana = p.statManaMax,
				spawnX = p.SpawnX,
				spawnY = p.SpawnY,
				extraSlot = p.extraAccessory ? 1 : 0,
				skinVariant = p.skinVariant,
				hair = p.hair,
				hairDye = p.hairDye,
				hairColor = p.hairColor,
				pantsColor = p.pantsColor,
				shirtColor = p.shirtColor,
				underShirtColor = p.underShirtColor,
				shoeColor = p.shoeColor,
				hideVisuals = p.hideVisibleAccessory,
				skinColor = p.skinColor,
				eyeColor = p.eyeColor,
				questsCompleted = p.anglerQuestsFinished,
				usingBiomeTorches = p.UsingBiomeTorches ? 1 : 0,
				happyFunTorchTime = p.happyFunTorchTime ? 1 : 0,
				unlockedBiomeTorches = p.unlockedBiomeTorches ? 1 : 0,
				currentLoadoutIndex = p.CurrentLoadoutIndex,
				ateArtisanBread = p.ateArtisanBread ? 1 : 0,
				usedAegisCrystal = p.usedAegisCrystal ? 1 : 0,
				usedAegisFruit = p.usedAegisFruit ? 1 : 0,
				usedArcaneCrystal = p.usedArcaneCrystal ? 1 : 0,
				usedGalaxyPearl = p.usedGalaxyPearl ? 1 : 0,
				usedGummyWorm = p.usedGummyWorm ? 1 : 0,
				usedAmbrosia = p.usedAmbrosia ? 1 : 0,
				unlockedSuperCart = p.unlockedSuperCart ? 1 : 0,
				enabledSuperCart = p.enabledSuperCart ? 1 : 0,
				deathsPVE = p.numberOfDeathsPVE,
				deathsPVP = p.numberOfDeathsPVP,
				voiceVariant = p.voiceVariant,
				voicePitchOffset = p.voicePitchOffset,
				team = p.team
			};

			Item[] inventory = p.inventory;
			Item[] armor = p.armor;
			Item[] dye = p.dye;
			Item[] miscEquips = p.miscEquips;
			Item[] miscDyes = p.miscDyes;
			Item[] piggy = p.bank.item;
			Item[] safe = p.bank2.item;
			Item[] forge = p.bank3.item;
			Item[] voidVault = p.bank4.item;
			Item trash = p.trashItem;
			Item[] loadout1Armor = p.Loadouts[0].Armor;
			Item[] loadout1Dye = p.Loadouts[0].Dye;
			Item[] loadout2Armor = p.Loadouts[1].Armor;
			Item[] loadout2Dye = p.Loadouts[1].Dye;
			Item[] loadout3Armor = p.Loadouts[2].Armor;
			Item[] loadout3Dye = p.Loadouts[2].Dye;

			for (int i = 0; i < NetItem.MaxInventory; i++)
			{
				if (i < NetItem.InventoryIndex.Item2)
					data.inventory[i] = (NetItem)inventory[i];
				else if (i < NetItem.ArmorIndex.Item2)
					data.inventory[i] = (NetItem)armor[i - NetItem.ArmorIndex.Item1];
				else if (i < NetItem.DyeIndex.Item2)
					data.inventory[i] = (NetItem)dye[i - NetItem.DyeIndex.Item1];
				else if (i < NetItem.MiscEquipIndex.Item2)
					data.inventory[i] = (NetItem)miscEquips[i - NetItem.MiscEquipIndex.Item1];
				else if (i < NetItem.MiscDyeIndex.Item2)
					data.inventory[i] = (NetItem)miscDyes[i - NetItem.MiscDyeIndex.Item1];
				else if (i < NetItem.PiggyIndex.Item2)
					data.inventory[i] = (NetItem)piggy[i - NetItem.PiggyIndex.Item1];
				else if (i < NetItem.SafeIndex.Item2)
					data.inventory[i] = (NetItem)safe[i - NetItem.SafeIndex.Item1];
				else if (i < NetItem.TrashIndex.Item2)
					data.inventory[i] = (NetItem)trash;
				else if (i < NetItem.ForgeIndex.Item2)
					data.inventory[i] = (NetItem)forge[i - NetItem.ForgeIndex.Item1];
				else if (i < NetItem.VoidIndex.Item2)
					data.inventory[i] = (NetItem)voidVault[i - NetItem.VoidIndex.Item1];
				else if (i < NetItem.Loadout1Armor.Item2)
					data.inventory[i] = (NetItem)loadout1Armor[i - NetItem.Loadout1Armor.Item1];
				else if (i < NetItem.Loadout1Dye.Item2)
					data.inventory[i] = (NetItem)loadout1Dye[i - NetItem.Loadout1Dye.Item1];
				else if (i < NetItem.Loadout2Armor.Item2)
					data.inventory[i] = (NetItem)loadout2Armor[i - NetItem.Loadout2Armor.Item1];
				else if (i < NetItem.Loadout2Dye.Item2)
					data.inventory[i] = (NetItem)loadout2Dye[i - NetItem.Loadout2Dye.Item1];
				else if (i < NetItem.Loadout3Armor.Item2)
					data.inventory[i] = (NetItem)loadout3Armor[i - NetItem.Loadout3Armor.Item1];
				else if (i < NetItem.Loadout3Dye.Item2)
					data.inventory[i] = (NetItem)loadout3Dye[i - NetItem.Loadout3Dye.Item1];
			}

			return data;
		}

		/// <summary>把 PlayerData 写入 tsCharacter 表（覆盖式，字段集与 InsertSpecificPlayerData 完全一致）</summary>
		private static bool WriteCharacterData(UserAccount account, PlayerData data)
		{
			try
			{
				// 与 TShock 一致：Inventory 以 "~" 分隔 NetItem 的 ToString（netId/stack/prefix/favorite）
				var invStr = string.Join("~", data.inventory);

				var db = TShock.DB;
				bool exists;
				using (var qr = db.QueryReader("SELECT Account FROM tsCharacter WHERE Account = @0", account.ID))
					exists = qr.Read();

				int rows;
				if (exists)
				{
					// 参数占位符必须与 TShock 原版 UPDATE 完全一致（@5 是 Account，spawnX=@6, spawnY=@7）
					rows = db.Query(
						"UPDATE tsCharacter SET Health = @0, MaxHealth = @1, Mana = @2, MaxMana = @3, Inventory = @4, " +
						"spawnX = @6, spawnY = @7, hair = @8, hairDye = @9, hairColor = @10, pantsColor = @11, " +
						"shirtColor = @12, underShirtColor = @13, shoeColor = @14, hideVisuals = @15, skinColor = @16, " +
						"eyeColor = @17, questsCompleted = @18, skinVariant = @19, extraSlot = @20, usingBiomeTorches = @21, " +
						"happyFunTorchTime = @22, unlockedBiomeTorches = @23, currentLoadoutIndex = @24, ateArtisanBread = @25, " +
						"usedAegisCrystal = @26, usedAegisFruit = @27, usedArcaneCrystal = @28, usedGalaxyPearl = @29, " +
						"usedGummyWorm = @30, usedAmbrosia = @31, unlockedSuperCart = @32, enabledSuperCart = @33, " +
						"deathsPVE = @34, deathsPVP = @35, voiceVariant = @36, voicePitchOffset = @37, team = @38 " +
						"WHERE Account = @5;",
						data.health, data.maxHealth, data.mana, data.maxMana, invStr,
						account.ID, data.spawnX, data.spawnY,
						data.hair ?? 0, data.hairDye,
						TShock.Utils.EncodeColor(data.hairColor),
						TShock.Utils.EncodeColor(data.pantsColor),
						TShock.Utils.EncodeColor(data.shirtColor),
						TShock.Utils.EncodeColor(data.underShirtColor),
						TShock.Utils.EncodeColor(data.shoeColor),
						TShock.Utils.EncodeBoolArray(data.hideVisuals),
						TShock.Utils.EncodeColor(data.skinColor),
						TShock.Utils.EncodeColor(data.eyeColor),
						data.questsCompleted, data.skinVariant ?? 0, data.extraSlot ?? 0,
						data.usingBiomeTorches, data.happyFunTorchTime, data.unlockedBiomeTorches,
						data.currentLoadoutIndex, data.ateArtisanBread, data.usedAegisCrystal,
						data.usedAegisFruit, data.usedArcaneCrystal, data.usedGalaxyPearl,
						data.usedGummyWorm, data.usedAmbrosia, data.unlockedSuperCart,
						data.enabledSuperCart, data.deathsPVE, data.deathsPVP,
						data.voiceVariant ?? 0, data.voicePitchOffset ?? 0f, data.team);
				}
				else
				{
					rows = db.Query(
						"INSERT INTO tsCharacter (Account, Health, MaxHealth, Mana, MaxMana, Inventory, extraSlot, " +
						"spawnX, spawnY, skinVariant, hair, hairDye, hairColor, pantsColor, shirtColor, underShirtColor, " +
						"shoeColor, hideVisuals, skinColor, eyeColor, questsCompleted, usingBiomeTorches, happyFunTorchTime, " +
						"unlockedBiomeTorches, currentLoadoutIndex, ateArtisanBread, usedAegisCrystal, usedAegisFruit, " +
						"usedArcaneCrystal, usedGalaxyPearl, usedGummyWorm, usedAmbrosia, unlockedSuperCart, " +
						"enabledSuperCart, deathsPVE, deathsPVP, voiceVariant, voicePitchOffset, team) " +
						"VALUES (@0, @1, @2, @3, @4, @5, @6, @7, @8, @9, @10, @11, @12, @13, @14, @15, @16, @17, @18, " +
						"@19, @20, @21, @22, @23, @24, @25, @26, @27, @28, @29, @30, @31, @32, @33, @34, @35, @36, @37, @38);",
						account.ID, data.health, data.maxHealth, data.mana, data.maxMana, invStr,
						data.extraSlot ?? 0, data.spawnX, data.spawnY, data.skinVariant ?? 0, data.hair ?? 0, data.hairDye,
						TShock.Utils.EncodeColor(data.hairColor),
						TShock.Utils.EncodeColor(data.pantsColor),
						TShock.Utils.EncodeColor(data.shirtColor),
						TShock.Utils.EncodeColor(data.underShirtColor),
						TShock.Utils.EncodeColor(data.shoeColor),
						TShock.Utils.EncodeBoolArray(data.hideVisuals),
						TShock.Utils.EncodeColor(data.skinColor),
						TShock.Utils.EncodeColor(data.eyeColor),
						data.questsCompleted, data.usingBiomeTorches, data.happyFunTorchTime,
						data.unlockedBiomeTorches, data.currentLoadoutIndex, data.ateArtisanBread,
						data.usedAegisCrystal, data.usedAegisFruit, data.usedArcaneCrystal,
						data.usedGalaxyPearl, data.usedGummyWorm, data.usedAmbrosia,
						data.unlockedSuperCart, data.enabledSuperCart, data.deathsPVE, data.deathsPVP,
						data.voiceVariant ?? 0, data.voicePitchOffset ?? 0f, data.team);
				}
				return rows > 0;
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError("[PlayerTransfer] 写入角色数据库失败: " + ex);
				return false;
			}
		}

		// ═══════════════════════════════════════════════════════════
		// 构建 Player（导出用）
		// ═══════════════════════════════════════════════════════════

		/// <summary>按用户名构建 Player 对象（在线玩家用其当前 TPlayer，否则从数据库重建）</summary>
		private static Player? BuildPlayerForUser(string username, out UserAccount? account)
		{
			account = null;
			var online = TShock.Players.FirstOrDefault(p => p?.Name?.Equals(username, StringComparison.OrdinalIgnoreCase) ?? false);
			if (online != null)
			{
				account = online.Account;
				return online.TPlayer;
			}

			var users = TShock.UserAccounts.GetUserAccountsByName(username, true);
			if (users.Count == 0) return null;
			var acc = users.Count > 1 ? users.Find(x => x.Name == username) : users[0];
			if (acc == null) return null;
			account = acc;

			// 无角色数据 → 返回 null（前端提示）
			bool has = false;
			using (var qr = TShock.DB.QueryReader("SELECT Account FROM tsCharacter WHERE Account = @0", acc.ID))
				has = qr.Read();
			if (!has) return null;

			return NewPlayer(acc);
		}

		private static Player NewPlayer(UserAccount acc)
		{
			var p = new MyPlayer();
			p.Account.ID = acc.ID;
			p.Player = new Player { name = acc.Name };

			var data = TShock.CharacterDB.GetPlayerData(p, acc.ID);
			return CreatePlayerFromData(acc.Name, data);
		}

		private static Player CreatePlayerFromData(string name, PlayerData data)
		{
			if (data == null)
				return new Player { name = name };

			var player = new Player
			{
				name = name,
				statLife = data.health,
				statLifeMax = data.maxHealth,
				statMana = data.mana,
				statManaMax = data.maxMana,
				extraAccessory = data.extraSlot == 1,
				skinVariant = data.skinVariant ?? 0,
				hair = data.hair ?? 0,
				hairDye = data.hairDye,
				hairColor = data.hairColor ?? Color.White,
				pantsColor = data.pantsColor ?? Color.White,
				shirtColor = data.shirtColor ?? Color.White,
				underShirtColor = data.underShirtColor ?? Color.White,
				shoeColor = data.shoeColor ?? Color.White,
				hideVisibleAccessory = data.hideVisuals,
				skinColor = data.skinColor ?? Color.White,
				eyeColor = data.eyeColor ?? Color.White,
				anglerQuestsFinished = data.questsCompleted,
				UsingBiomeTorches = data.usingBiomeTorches == 1,
				happyFunTorchTime = data.happyFunTorchTime == 1,
				unlockedBiomeTorches = data.unlockedBiomeTorches == 1,
				ateArtisanBread = data.ateArtisanBread == 1,
				usedAegisCrystal = data.usedAegisCrystal == 1,
				usedAegisFruit = data.usedAegisFruit == 1,
				usedArcaneCrystal = data.usedArcaneCrystal == 1,
				usedGalaxyPearl = data.usedGalaxyPearl == 1,
				usedGummyWorm = data.usedGummyWorm == 1,
				usedAmbrosia = data.usedAmbrosia == 1,
				unlockedSuperCart = data.unlockedSuperCart == 1,
				enabledSuperCart = data.enabledSuperCart == 1,
				CurrentLoadoutIndex = data.currentLoadoutIndex
			};

			if (data.inventory != null)
			{
				for (int i = 0; i < Math.Min(data.inventory.Length, NetItem.MaxInventory); i++)
				{
					var invItem = data.inventory[i];
					if (i < NetItem.InventoryIndex.Item2)
					{
						player.inventory[i] = TShock.Utils.GetItemById(invItem.NetId);
						player.inventory[i].stack = invItem.Stack;
						player.inventory[i].prefix = invItem.PrefixId;
					}
					else if (i < NetItem.ArmorIndex.Item2)
					{
						int num = i - NetItem.ArmorIndex.Item1;
						if (num >= 0 && num < player.armor.Length)
						{
							player.armor[num] = TShock.Utils.GetItemById(invItem.NetId);
							player.armor[num].stack = invItem.Stack;
							player.armor[num].prefix = invItem.PrefixId;
						}
					}
					else if (i < NetItem.DyeIndex.Item2)
					{
						int num = i - NetItem.DyeIndex.Item1;
						if (num >= 0 && num < player.dye.Length)
						{
							player.dye[num] = TShock.Utils.GetItemById(invItem.NetId);
							player.dye[num].stack = invItem.Stack;
							player.dye[num].prefix = invItem.PrefixId;
						}
					}
					else if (i < NetItem.MiscEquipIndex.Item2)
					{
						int num = i - NetItem.MiscEquipIndex.Item1;
						if (num >= 0 && num < player.miscEquips.Length)
						{
							player.miscEquips[num] = TShock.Utils.GetItemById(invItem.NetId);
							player.miscEquips[num].stack = invItem.Stack;
							player.miscEquips[num].prefix = invItem.PrefixId;
						}
					}
					else if (i < NetItem.MiscDyeIndex.Item2)
					{
						int num = i - NetItem.MiscDyeIndex.Item1;
						if (num >= 0 && num < player.miscDyes.Length)
						{
							player.miscDyes[num] = TShock.Utils.GetItemById(invItem.NetId);
							player.miscDyes[num].stack = invItem.Stack;
							player.miscDyes[num].prefix = invItem.PrefixId;
						}
					}
					else if (i < NetItem.PiggyIndex.Item2)
					{
						int num = i - NetItem.PiggyIndex.Item1;
						if (num >= 0 && num < player.bank.item.Length)
						{
							player.bank.item[num] = TShock.Utils.GetItemById(invItem.NetId);
							player.bank.item[num].stack = invItem.Stack;
							player.bank.item[num].prefix = invItem.PrefixId;
						}
					}
					else if (i < NetItem.SafeIndex.Item2)
					{
						int num = i - NetItem.SafeIndex.Item1;
						if (num >= 0 && num < player.bank2.item.Length)
						{
							player.bank2.item[num] = TShock.Utils.GetItemById(invItem.NetId);
							player.bank2.item[num].stack = invItem.Stack;
							player.bank2.item[num].prefix = invItem.PrefixId;
						}
					}
					else if (i < NetItem.TrashIndex.Item2)
					{
						player.trashItem = TShock.Utils.GetItemById(invItem.NetId);
						player.trashItem.stack = invItem.Stack;
						player.trashItem.prefix = invItem.PrefixId;
					}
					else if (i < NetItem.ForgeIndex.Item2)
					{
						int num = i - NetItem.ForgeIndex.Item1;
						if (num >= 0 && num < player.bank3.item.Length)
						{
							player.bank3.item[num] = TShock.Utils.GetItemById(invItem.NetId);
							player.bank3.item[num].stack = invItem.Stack;
							player.bank3.item[num].prefix = invItem.PrefixId;
						}
					}
					else if (i < NetItem.VoidIndex.Item2)
					{
						int num = i - NetItem.VoidIndex.Item1;
						if (num >= 0 && num < player.bank4.item.Length)
						{
							player.bank4.item[num] = TShock.Utils.GetItemById(invItem.NetId);
							player.bank4.item[num].stack = invItem.Stack;
							player.bank4.item[num].prefix = invItem.PrefixId;
						}
					}
					else if (i < NetItem.Loadout1Armor.Item2)
					{
						int num = i - NetItem.Loadout1Armor.Item1;
						if (num >= 0 && num < player.Loadouts[0].Armor.Length)
						{
							player.Loadouts[0].Armor[num] = TShock.Utils.GetItemById(invItem.NetId);
							player.Loadouts[0].Armor[num].stack = invItem.Stack;
							player.Loadouts[0].Armor[num].prefix = invItem.PrefixId;
						}
					}
					else if (i < NetItem.Loadout1Dye.Item2)
					{
						int num = i - NetItem.Loadout1Dye.Item1;
						if (num >= 0 && num < player.Loadouts[0].Dye.Length)
						{
							player.Loadouts[0].Dye[num] = TShock.Utils.GetItemById(invItem.NetId);
							player.Loadouts[0].Dye[num].stack = invItem.Stack;
							player.Loadouts[0].Dye[num].prefix = invItem.PrefixId;
						}
					}
					else if (i < NetItem.Loadout2Armor.Item2)
					{
						int num = i - NetItem.Loadout2Armor.Item1;
						if (num >= 0 && num < player.Loadouts[1].Armor.Length)
						{
							player.Loadouts[1].Armor[num] = TShock.Utils.GetItemById(invItem.NetId);
							player.Loadouts[1].Armor[num].stack = invItem.Stack;
							player.Loadouts[1].Armor[num].prefix = invItem.PrefixId;
						}
					}
					else if (i < NetItem.Loadout2Dye.Item2)
					{
						int num = i - NetItem.Loadout2Dye.Item1;
						if (num >= 0 && num < player.Loadouts[1].Dye.Length)
						{
							player.Loadouts[1].Dye[num] = TShock.Utils.GetItemById(invItem.NetId);
							player.Loadouts[1].Dye[num].stack = invItem.Stack;
							player.Loadouts[1].Dye[num].prefix = invItem.PrefixId;
						}
					}
					else if (i < NetItem.Loadout3Armor.Item2)
					{
						int num = i - NetItem.Loadout3Armor.Item1;
						if (num >= 0 && num < player.Loadouts[2].Armor.Length)
						{
							player.Loadouts[2].Armor[num] = TShock.Utils.GetItemById(invItem.NetId);
							player.Loadouts[2].Armor[num].stack = invItem.Stack;
							player.Loadouts[2].Armor[num].prefix = invItem.PrefixId;
						}
					}
					else if (i < NetItem.Loadout3Dye.Item2)
					{
						int num = i - NetItem.Loadout3Dye.Item1;
						if (num >= 0 && num < player.Loadouts[2].Dye.Length)
						{
							player.Loadouts[2].Dye[num] = TShock.Utils.GetItemById(invItem.NetId);
							player.Loadouts[2].Dye[num].stack = invItem.Stack;
							player.Loadouts[2].Dye[num].prefix = invItem.PrefixId;
						}
					}
				}
			}

			return player;
		}

		/// <summary>写 .plr 文件（与 /export 命令相同的序列化路径）</summary>
		private static bool Export(Player player, string filePath)
		{
			if (player == null) return false;

			try
			{
				PlayerFileData data = new PlayerFileData();
				data.Metadata = FileMetadata.FromCurrentSettings(FileType.Player);
				data.Player = player;
				data._isCloudSave = false;
				FileData fileData = data;
				fileData._path = filePath;
				data.SetPlayTime(new TimeSpan(0));
				Main.LocalFavoriteData.ClearEntry(data);

				if (string.IsNullOrEmpty(data.Path)) return false;

				string exportDir = Path.GetDirectoryName(data.Path)!;
				if (!Directory.Exists(exportDir))
					Directory.CreateDirectory(exportDir);

				Player.InternalSavePlayerFile(data);
				return true;
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError("[PlayerTransfer] 导出 plr 文件错误: " + ex);
				return false;
			}
		}

		// ═══════════════════════════════════════════════════════════
		// 工具
		// ═══════════════════════════════════════════════════════════

		private static string FormatFileName(string text)
		{
			for (int i = 0; i < text.Length; ++i)
			{
				bool flag = text[i] == '\\' || text[i] == '/' || text[i] == ':' || text[i] == '*' || text[i] == '?' || text[i] == '"' || text[i] == '<' || text[i] == '>' || text[i] == '|';
				if (flag)
					text = text.Replace(text[i], '-');
			}
			return text;
		}

		private static string GetParam(RestRequestArgs args, string key)
		{
			try
			{
				object? v = args.Parameters[key];
				return v?.ToString() ?? "";
			}
			catch
			{
				return "";
			}
		}
	}
}
