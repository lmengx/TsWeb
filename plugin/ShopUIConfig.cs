using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Rests;
using Terraria;
using Terraria.ID;
using TShockAPI;

namespace TShockData
{
	/// <summary>
	/// ShopUI 可配置化 —— 配置数据类（属性名统一小写英文，JSON 文件与 REST 字段一致；
	/// TShock Rest 的 JavaScriptSerializer 不认 [JsonProperty]，故属性名即序列化名）
	/// </summary>

	/// <summary>商品解锁条件：type = always / hardmode / boss / kill / never</summary>
	public class ShopItemCondition
	{
		/// <summary>条件类型：always 始终 / hardmode 仅肉山后 / boss 击杀 flag / kill 图鉴击杀数>0 / never 永不上架</summary>
		public string type { get; set; } = "always";

		/// <summary>type=boss 时的 NPC downed flag 名（如 downedSlimeKing、downedMoonlord）</summary>
		public string flag { get; set; } = "";

		/// <summary>type=kill 时的 NPC type（图鉴击杀数>0 判定）</summary>
		public int npcId { get; set; } = 0;

		/// <summary>type=kill 时可选：多个 NPC type 任一击杀数>0 即满足（用于世吞头/身/尾 13/14/15）</summary>
		public List<int> npcIds { get; set; } = new();
	}

	/// <summary>单个商品</summary>
	public class ShopItem
	{
		public int itemId { get; set; } = 1;

		/// <summary>价格（铜币：1银=100，1金=10000，1铂金=1000000）</summary>
		public long price { get; set; } = 100L;

		public int stack { get; set; } = 1;

		public ShopItemCondition condition { get; set; } = new();
	}

	/// <summary>单个商店（商品列表，最多 GoodsSlots=36 条，超出截断）</summary>
	public class ShopDefinition
	{
		public string name { get; set; } = "";
		public List<ShopItem> items { get; set; } = new();
	}

	/// <summary>控件（商店切换按钮）：在 40 格商店面板中占据指定格子，点击跳转到 targetShopIndex 对应商店。
	/// 控件优先级高于商品——占用的格子在所有商店面板中锁定，剩余格子才是商品区</summary>
	public class StatueControl
	{
		/// <summary>在 40 格（10×4）面板中的格子位置（0-39）</summary>
		public int slot { get; set; } = 36;

		/// <summary>控件物品 ID（ChestStatue=463 / PickaxeStatue=469 / HammerStatue=455 / PotionStatue=456）</summary>
		public int statueItemId { get; set; } = ItemID.ChestStatue;

		/// <summary>跳转目标：shops[] 下标（跳转逻辑可配置）</summary>
		public int targetShopIndex { get; set; } = 0;

		public string name { get; set; } = "";
	}

	/// <summary>ShopUI 总配置</summary>
	public class ShopUIConfig
	{
		/// <summary>总开关：false 时所有钩子不生效并移除全部虚拟旅商</summary>
		public bool enabled { get; set; } = true;

		/// <summary>召唤物物品 ID（默认锡斧 3500）</summary>
		public int summonItemId { get; set; } = ItemID.TinAxe;

		/// <summary>雕像控件（目录）→ 跳转映射，最多 4 个（对应槽 36-39），不足发空槽</summary>
		public List<StatueControl> statueControls { get; set; } = new();

		/// <summary>商店定义（每个商店独立商品列表与解锁条件）</summary>
		public List<ShopDefinition> shops { get; set; } = new();
	}

	/// <summary>
	/// ShopUI 配置管理器（参照 PromotionManager 模式）：
	/// 配置文件 {TShock.SavePath}/TSWeb/ShopUI/shopui_config.json，首次运行自动生成默认配置（= 原硬编码内容）。
	/// </summary>
	public static class ShopUIConfigManager
	{
		private static ShopUIConfig _config = new();
		private static readonly object _lock = new();
		private static bool _loaded;

		private static string ConfigPath => Path.Combine(TShock.SavePath, "TSWeb", "ShopUI", "shopui_config.json");

		public static void EnsureLoaded()
		{
			if (!_loaded)
			{
				LoadConfig();
				_loaded = true;
			}
		}

		/// <summary>深拷贝返回，避免外部修改内存配置</summary>
		public static ShopUIConfig GetConfig()
		{
			EnsureLoaded();
			lock (_lock)
			{
				return JsonConvert.DeserializeObject<ShopUIConfig>(
					JsonConvert.SerializeObject(_config)) ?? new ShopUIConfig();
			}
		}

		public static void LoadConfig()
		{
			try
			{
				var dir = Path.GetDirectoryName(ConfigPath);
				if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
					Directory.CreateDirectory(dir);

				if (File.Exists(ConfigPath))
				{
					var json = File.ReadAllText(ConfigPath);
					lock (_lock)
					{
						_config = JsonConvert.DeserializeObject<ShopUIConfig>(json) ?? new ShopUIConfig();
					}
					MigrateLegacySlots();
					TShock.Log.ConsoleInfo("[TSWeb] ShopUI 配置已加载");
				}
				else
				{
					lock (_lock)
					{
						_config = BuildDefault();
					}
					SaveConfig();
					TShock.Log.ConsoleInfo("[TSWeb] 已创建默认 ShopUI 配置");
				}
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[TSWeb] 加载 ShopUI 配置失败: {ex.Message}");
				lock (_lock) { _config = new ShopUIConfig(); }
			}
		}

		public static void SaveConfig()
		{
			try
			{
				var dir = Path.GetDirectoryName(ConfigPath);
				if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
					Directory.CreateDirectory(dir);

				lock (_lock)
				{
					File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(_config, Formatting.Indented));
				}
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[TSWeb] 保存 ShopUI 配置失败: {ex.Message}");
			}
		}

		/// <summary>旧配置迁移：v1 配置的控件无 slot 字段（反序列化后全部为默认值 36），
		/// 检测到重复/越界 slot 时自动按尾部布局重新分配（36,37,38...），并落盘。</summary>
		private static void MigrateLegacySlots()
		{
			var seen = new HashSet<int>();
			bool hasConflict = false;
			foreach (var c in _config.statueControls)
			{
				if (c.slot < 0 || c.slot >= 40 || !seen.Add(c.slot))
				{
					hasConflict = true;
					break;
				}
			}
			if (!hasConflict || _config.statueControls.Count == 0) return;

			for (int i = 0; i < _config.statueControls.Count; i++)
			{
				_config.statueControls[i].slot = Math.Max(0, 40 - _config.statueControls.Count + i);
			}
			SaveConfig();
			TShock.Log.ConsoleInfo("[TSWeb] ShopUI 旧配置已迁移：控件格子自动分配为尾部布局");
		}

		/// <summary>条件求值：商品是否在当前世界进度下上架</summary>
		public static bool EvalCondition(ShopItemCondition c)
		{
			if (c == null) return true;
			switch (c.type)
			{
				case "hardmode":
					return Main.hardMode;
				case "never":
					return false;
				case "boss":
					return EvalBossFlag(c.flag);
				case "kill":
					return EvalKillCount(c);
				case "always":
				default:
					return true;
			}
		}

		private static bool EvalBossFlag(string flag)
		{
			switch (flag)
			{
				case "downedSlimeKing": return NPC.downedSlimeKing;
				case "downedBoss1": return NPC.downedBoss1;          // 克眼
				case "downedQueenBee": return NPC.downedQueenBee;
				case "downedBoss3": return NPC.downedBoss3;          // 骷髅王
				case "downedMechBoss1": return NPC.downedMechBoss1;  // 毁灭者
				case "downedMechBoss2": return NPC.downedMechBoss2;  // 双子魔眼
				case "downedMechBoss3": return NPC.downedMechBoss3;  // 机械骷髅王
				case "downedPlantBoss": return NPC.downedPlantBoss;  // 世纪之花
				case "downedGolemBoss": return NPC.downedGolemBoss;  // 石巨人
				case "downedFishron": return NPC.downedFishron;      // 猪鲨
				case "downedMoonlord": return NPC.downedMoonlord;    // 月总
				case "downedQueenSlime": return NPC.downedQueenSlime;
				case "downedEmpressOfLight": return NPC.downedEmpressOfLight;
				case "downedDeerclops": return NPC.downedDeerclops;
				case "hardMode": return Main.hardMode;               // 兼容旧写法
				default: return true;                                 // 未知 flag 视为放行（不阻塞上架）
			}
		}

		/// <summary>图鉴 BestiaryTracker 击杀计数 > 0（随世界存档持久化；世吞/克脑独立判定）</summary>
		private static bool EvalKillCount(ShopItemCondition c)
		{
			try
			{
				foreach (int id in c.npcIds ?? new List<int>())
				{
					if (KillCount(id) > 0) return true;
				}
				return c.npcId > 0 && KillCount(c.npcId) > 0;
			}
			catch
			{
				return false;
			}
		}

		private static int KillCount(int npcType)
		{
			return Main.BestiaryTracker.Kills.GetKillCount(
				ContentSamples.NpcBestiaryCreditIdsByNpcNetIds[npcType]);
		}

		// ═══════════════════════════════════════════════
		// REST API
		// ═══════════════════════════════════════════════

		public static object GetConfigJson(RestRequestArgs args)
		{
			EnsureLoaded();
			return new { status = 200, config = GetConfig() };
		}

		public static object SetConfigJson(RestRequestArgs args)
		{
			try
			{
				string json = args.Parameters["config"];
				if (string.IsNullOrEmpty(json))
					return new { status = 400, error = "Missing config parameter" };

				var incoming = JsonConvert.DeserializeObject<ShopUIConfig>(json);
				if (incoming == null)
					return new { status = 400, error = "Invalid config format" };

				// 基本校验：召唤物 ID 必须 >0；控件 slot 钳制 0-39 且去重（同 slot 保留第一个）；价格/数量钳制合法范围
				// （商品/控件数量不限，超出 40 格物理限制由显示层截断）
				if (incoming.summonItemId <= 0) incoming.summonItemId = ItemID.TinAxe;
				var seenSlots = new HashSet<int>();
				var validControls = new List<StatueControl>();
				foreach (var sc in incoming.statueControls)
				{
					if (sc.slot < 0 || sc.slot >= 40) sc.slot = 39; // 非法 slot 归位尾部
					if (!seenSlots.Add(sc.slot)) continue;          // 重复 slot 只保留第一个
					if (sc.statueItemId <= 0) sc.statueItemId = 1;
					validControls.Add(sc);
				}
				incoming.statueControls = validControls;
				foreach (var shop in incoming.shops)
				{
					foreach (var it in shop.items)
					{
						if (it.itemId <= 0) it.itemId = 1;
						if (it.price < 0) it.price = 0;
						if (it.stack < 1) it.stack = 1;
					}
				}

				lock (_lock) { _config = incoming; }
				SaveConfig();

				TShock.Log.ConsoleInfo("[TSWeb] ShopUI 配置已通过 REST API 更新");

				// 热重载：更新 ShopUICore 内存状态（启用状态/召唤物/默认商店），在线玩家立即按新配置刷新
				ShopUICore.ReloadConfig();

				return new { status = 200, response = "配置已保存" };
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[TSWeb] ShopUI 保存配置 API 异常: {ex.Message}");
				return new { status = 500, error = ex.Message };
			}
		}

		// ═══════════════════════════════════════════════
		// 默认配置（= 原 plugin-son/shopui 硬编码内容）
		// ═══════════════════════════════════════════════

		private static ShopUIConfig BuildDefault()
		{
			return new ShopUIConfig
			{
				enabled = true,
				summonItemId = ItemID.TinAxe, // 3500
				statueControls = new List<StatueControl>
				{
					new StatueControl { slot = 36, statueItemId = ItemID.ChestStatue,   targetShopIndex = 0, name = "宝藏袋商店" },
					new StatueControl { slot = 37, statueItemId = ItemID.PickaxeStatue, targetShopIndex = 1, name = "天然方块商店" },
					new StatueControl { slot = 38, statueItemId = ItemID.HammerStatue,  targetShopIndex = 2, name = "建筑方块商店" },
					new StatueControl { slot = 39, statueItemId = ItemID.PotionStatue,  targetShopIndex = 3, name = "药水商店" },
				},
				shops = new List<ShopDefinition>
				{
					BuildTreasureShop(),
					BuildBlockShop(),
					BuildBuildingShop(),
					BuildPotionShop(),
				},
			};
		}

		private static ShopItemCondition Boss(string flag) => new ShopItemCondition { type = "boss", flag = flag };
		private static ShopItemCondition Kill(int npcId, params int[] more)
		{
			var ids = new List<int>(more) { npcId };
			return new ShopItemCondition { type = "kill", npcIds = ids };
		}
		private static ShopItemCondition Hard() => new ShopItemCondition { type = "hardmode" };
		private static ShopItemCondition Always() => new ShopItemCondition { type = "always" };

		/// <summary>宝藏袋商店：按 Boss 击败进度解锁，固定价格（铜币）。克脑/世吞用图鉴击杀数独立判定</summary>
		private static ShopDefinition BuildTreasureShop()
		{
			var items = new List<ShopItem>
			{
				new ShopItem { itemId = 3318, price = 500000L,  condition = Boss("downedSlimeKing") },      // 史莱姆王 50金
				new ShopItem { itemId = 3319, price = 550000L,  condition = Boss("downedBoss1") },          // 克眼 55金
				new ShopItem { itemId = 3320, price = 600000L,  condition = Kill(NPCID.EaterofWorldsHead, NPCID.EaterofWorldsBody, NPCID.EaterofWorldsTail) }, // 世吞 60金
				new ShopItem { itemId = 3321, price = 600000L,  condition = Kill(NPCID.BrainofCthulhu) },   // 克脑 60金
				new ShopItem { itemId = 3322, price = 700000L,  condition = Boss("downedQueenBee") },       // 蜂后 70金
				new ShopItem { itemId = 3323, price = 750000L,  condition = Boss("downedBoss3") },          // 骷髅王 75金
				new ShopItem { itemId = 3324, price = 1000000L, condition = Hard() },                       // 肉山 1铂金
				new ShopItem { itemId = 3325, price = 1500000L, condition = Boss("downedMechBoss1") },      // 毁灭者
				new ShopItem { itemId = 3326, price = 1500000L, condition = Boss("downedMechBoss2") },      // 双子魔眼
				new ShopItem { itemId = 3327, price = 1500000L, condition = Boss("downedMechBoss3") },      // 机械骷髅王
				new ShopItem { itemId = 3328, price = 2000000L, condition = Boss("downedPlantBoss") },      // 世纪之花 2铂金
				new ShopItem { itemId = 3329, price = 2500000L, condition = Boss("downedGolemBoss") },      // 石巨人
				new ShopItem { itemId = 3330, price = 3000000L, condition = Boss("downedFishron") },        // 猪鲨
				new ShopItem { itemId = 3332, price = 10000000L, condition = Boss("downedMoonlord") },      // 月总 10铂金
				new ShopItem { itemId = 4957, price = 1200000L, condition = Boss("downedQueenSlime") },     // 史莱姆皇后
				new ShopItem { itemId = 4782, price = 4500000L, condition = Boss("downedEmpressOfLight") }, // 光之女皇
				new ShopItem { itemId = 5111, price = 800000L,  condition = Boss("downedDeerclops") },      // 鹿角怪
			};
			return new ShopDefinition { name = "宝藏袋商店", items = items };
		}

		/// <summary>天然方块商店：每个 1 银（100 铜）。珍珠木仅肉山后</summary>
		private static ShopDefinition BuildBlockShop()
		{
			var items = new List<ShopItem>
			{
				// 木材（珍珠木=肉山后）
				I(ItemID.Wood), I(ItemID.RichMahogany), I(ItemID.PalmWood), I(ItemID.BorealWood),
				I(ItemID.Ebonwood), I(ItemID.Shadewood), I(ItemID.AshWood), I(ItemID.BambooBlock),
				I(ItemID.DynastyWood), IH(ItemID.Pearlwood),
				// 土石沙
				I(ItemID.DirtBlock), I(ItemID.ClayBlock), I(ItemID.StoneBlock), I(ItemID.SandBlock),
				I(ItemID.EbonstoneBlock), I(ItemID.CrimstoneBlock), I(ItemID.EbonsandBlock),
				I(ItemID.CrimsandBlock), I(ItemID.Sandstone), I(ItemID.HardenedSand),
				// 泥雪等
				I(ItemID.MudBlock), I(ItemID.AshBlock), I(ItemID.SiltBlock), I(ItemID.SlushBlock),
				I(ItemID.SnowBlock), I(ItemID.IceBlock), I(ItemID.MarbleBlock), I(ItemID.GraniteBlock),
				I(ItemID.Cloud), I(ItemID.RainCloud),
			};
			return new ShopDefinition { name = "天然方块商店", items = items };
		}

		/// <summary>建筑方块商店：每个 3 银（300 铜）</summary>
		private static ShopDefinition BuildBuildingShop()
		{
			var items = new List<ShopItem>
			{
				I(ItemID.GrayBrick, 300L), I(ItemID.RedBrick, 300L), I(ItemID.SnowBrick, 300L), I(ItemID.IceBrick, 300L),
				I(ItemID.SandstoneBrick, 300L), I(ItemID.EbonstoneBrick, 300L), I(ItemID.CrimstoneBrick, 300L), I(ItemID.Glass, 300L),
				I(ItemID.RedDynastyShingles, 300L), I(ItemID.BlueDynastyShingles, 300L), I(ItemID.Pumpkin, 300L), I(ItemID.Cactus, 300L),
				I(ItemID.ObsidianBrick, 300L), I(ItemID.IridescentBrick, 300L), I(ItemID.StoneSlab, 300L), I(ItemID.AccentSlab, 300L),
				I(ItemID.SandstoneSlab, 300L), I(ItemID.MarbleBlock, 300L), I(ItemID.GraniteBlock, 300L), I(ItemID.SunplateBlock, 300L),
			};
			return new ShopDefinition { name = "建筑方块商店", items = items };
		}

		/// <summary>药水商店：便宜在前；生命力/暴怒/怒气/狱火 仅肉山后</summary>
		private static ShopDefinition BuildPotionShop()
		{
			var items = new List<ShopItem>
			{
				// 30 银：铁皮/敏捷/再生
				I(ItemID.IronskinPotion, 3000L), I(ItemID.SwiftnessPotion, 3000L), I(ItemID.RegenerationPotion, 3000L),
				// 基础 1 金
				I(ItemID.ShinePotion, 10000L), I(ItemID.NightOwlPotion, 10000L), I(ItemID.GillsPotion, 10000L),
				I(ItemID.WaterWalkingPotion, 10000L), I(ItemID.GravitationPotion, 10000L), I(ItemID.ThornsPotion, 10000L),
				I(ItemID.ArcheryPotion, 10000L), I(ItemID.AmmoReservationPotion, 10000L), I(ItemID.WarmthPotion, 10000L),
				I(ItemID.TitanPotion, 10000L), I(ItemID.BuilderPotion, 10000L), I(ItemID.SummoningPotion, 10000L),
				I(ItemID.ManaRegenerationPotion, 10000L), I(ItemID.MagicPowerPotion, 10000L), I(ItemID.FeatherfallPotion, 10000L),
				I(ItemID.MiningPotion, 10000L), I(ItemID.HeartreachPotion, 10000L), I(ItemID.FlipperPotion, 10000L),
				// 3 金：洞穴探险/狩猎/危险感知/耐力/隐身
				I(ItemID.SpelunkerPotion, 30000L), I(ItemID.HunterPotion, 30000L), I(ItemID.TrapsightPotion, 30000L),
				I(ItemID.EndurancePotion, 30000L), I(ItemID.InvisibilityPotion, 30000L),
				// 5 金：生命力（仅肉山后）
				IH(ItemID.LifeforcePotion, 50000L),
				// 10 金：战斗/镇静 + 暴怒/怒气/狱火（后三者仅肉山后）
				I(ItemID.BattlePotion, 100000L), I(ItemID.CalmingPotion, 100000L),
				IH(ItemID.RagePotion, 100000L), IH(ItemID.WrathPotion, 100000L), IH(ItemID.InfernoPotion, 100000L),
				// 20 金：生物群落观测/黑曜石皮
				I(ItemID.BiomeSightPotion, 200000L), I(ItemID.ObsidianSkinPotion, 200000L),
			};
			return new ShopDefinition { name = "药水商店", items = items };
		}

		/// <summary>始终上架，默认 1 银（100 铜），stack=1</summary>
		private static ShopItem I(int itemId, long price = 100L)
			=> new ShopItem { itemId = itemId, price = price, stack = 1, condition = Always() };

		/// <summary>仅肉山后上架</summary>
		private static ShopItem IH(int itemId, long price = 100L)
			=> new ShopItem { itemId = itemId, price = price, stack = 1, condition = Hard() };
	}
}
