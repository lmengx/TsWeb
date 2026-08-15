using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Terraria;
using Terraria.GameContent.Drawing;
using Terraria.GameContent.NetModules;
using Terraria.ID;
using Terraria.Net;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;

namespace ItemFlash
{
	// ════════════════════════════════════════════════════════════
	//  配置模型
	// ════════════════════════════════════════════════════════════

	/// <summary>配方中的一种材料</summary>
	public class RecipeItemConfig
	{
		/// <summary>物品 ID（如 2=土块、73=金币）</summary>
		[JsonProperty("itemId")]
		public int ItemId { get; set; }

		/// <summary>所需数量（按掉落物 stack 累计）</summary>
		[JsonProperty("count")]
		public int Count { get; set; } = 1;
	}

	/// <summary>单个献祭配方</summary>
	public class RecipeConfig
	{
		/// <summary>配方名（日志用）</summary>
		[JsonProperty("name")]
		public string Name { get; set; } = "献祭";

		/// <summary>所需材料组合</summary>
		[JsonProperty("items")]
		public List<RecipeItemConfig> Items { get; set; } = new();

		/// <summary>动画模式："simple"（默认，主角升空消失）/"zenith"（天顶剑仪式：升空→旋转→轮流飞出→雷击→生成结果）</summary>
		[JsonProperty("animation")]
		public string Animation { get; set; } = "simple";

		/// <summary>做动画的主角物品 ID（仅 simple 模式使用，必须出现在 items 中；不填则取 items[0]）</summary>
		[JsonProperty("animateItemId")]
		public int AnimateItemId { get; set; }

		/// <summary>动画结束生成的结果物品 ID（0 = 不生成）</summary>
		[JsonProperty("resultItemId")]
		public int ResultItemId { get; set; }

		/// <summary>结果物品数量</summary>
		[JsonProperty("resultCount")]
		public int ResultCount { get; set; } = 1;

		/// <summary>触发成功的玩家提示，空字符串则不提示</summary>
		[JsonProperty("message")]
		public string Message { get; set; } = "";
	}

	/// <summary>插件配置</summary>
	public class ItemFlashConfig
	{
		/// <summary>配置文件版本（默认 0 = 旧配置，加载时自动迁移；当前 3）</summary>
		[JsonProperty("version")]
		public int Version { get; set; } = 0;

		/// <summary>总开关</summary>
		[JsonProperty("enabled")]
		public bool Enabled { get; set; } = true;

		/// <summary>聚类判定距离（像素，1 格 = 16px；默认 80 ≈ 5 格）</summary>
		[JsonProperty("clusterRange")]
		public int ClusterRange { get; set; } = 80;

		/// <summary>玩家丢物登记有效期（秒），超过此时间的丢物记录不再参与判定</summary>
		[JsonProperty("recordWindowSeconds")]
		public int RecordWindowSeconds { get; set; } = 120;

		/// <summary>配方列表</summary>
		[JsonProperty("recipes")]
		public List<RecipeConfig> Recipes { get; set; } = new();
	}

	// ════════════════════════════════════════════════════════════
	//  插件入口
	// ════════════════════════════════════════════════════════════

	[ApiVersion(2, 1)]
	public class ItemFlashPlugin : TerrariaPlugin
	{
		public override string Author => "lmx12330";
		public override string Description => "掉落物组合献祭/合成：把指定物品丢在一起触发动画（支持天顶剑仪式）";
		public override string Name => "ItemFlash";
		public override Version Version => new Version(1, 2, 0, 0);

		public ItemFlashPlugin(Main game) : base(game) { }

		public override void Initialize() => ItemFlashCore.Initialize(this);

		protected override void Dispose(bool Disposing)
		{
			if (Disposing)
			{
				ItemFlashCore.Dispose();
			}
			base.Dispose(Disposing);
		}
	}

	// ════════════════════════════════════════════════════════════
	//  核心逻辑
	// ════════════════════════════════════════════════════════════

	public static class ItemFlashCore
	{
		// ---- 常量 ----
		private const int ScanInterval = 10;      // 掉落物扫描间隔（tick），10 tick ≈ 0.17 秒
		// simple 动画参数
		private const int FlashDuration = 60;     // 主角升空时长（tick）
		private const float FlashHeight = 200f;   // 主角抬升高度（像素）

		// zenith 天顶剑仪式动画参数
		private const int RiseDuration = 75;      // 上升聚拢时长（tick）
		private const float RiseHeight = 60f;     // 环形悬浮高度（中心上方像素）
		private const float SpinRadius = 144f;    // 旋转半径（像素），≈ 9 格（1.5 倍）
		private const float SpinSpeed = 0.035f;   // 旋转角速度（弧度/帧），约 3 秒一圈
		private const int ChargeDuration = 150;   // 蓄力阶段时长（tick）：核心剑移中心 + 其它环绕
		private const int FlyInterval = 60;       // 每把剑飞入中心的间隔（tick）= 1 秒
		private const int FlyDuration = 60;       // 单把剑飞入时长（tick）= 1 秒
		private const int FinalDuration = 330;    // 终局总时长（tick）：雷暴 150 + 暂停 120 + 连环雷 60 = 5.5 秒
		private const int FinalBurstEnd = 150;    // 终局前段雷暴结束（每 15t 全剑数 burst）
		private const int FinalPauseEnd = 270;    // 终局暂停结束（150~270 无雷酝酿 2 秒），之后 60t 劈 10 道彩色连环雷

		private const int SyncInterval = 3;       // 动画期间每 3 帧广播一次 21 号包
		private const int FindRadius = 192;       // 掉落物区域验证半径（像素），≈ 12 格，覆盖丢出初速造成的落点漂移
		private const int KeepTimeLock = 600;     // 动画期间防捡锁（帧），600 ≈ 10 秒

		// 彩色闪电色板（每把武器对应一种颜色）
		private static readonly Color[] Palette =
		{
			new Color(255, 80, 80),     // 红
			new Color(255, 165, 0),     // 橙
			new Color(255, 255, 80),    // 黄
			new Color(120, 255, 120),   // 绿
			new Color(80, 220, 255),    // 青
			new Color(90, 130, 255),    // 蓝
			new Color(200, 110, 255),   // 紫
			new Color(255, 120, 220),   // 粉
			new Color(255, 255, 255),   // 白
			new Color(255, 215, 0),     // 金
		};

		// ---- 状态 ----
		private static ItemFlashConfig _config = new();
		private static readonly List<DropRecord> _records = new();
		private static readonly List<GroupSession> _sessions = new();
		private static readonly HashSet<int> _animatingIndexes = new(); // 动画中的掉落物槽位（O(1) 查询，替代遍历会话）
		private static readonly object SyncLock = new();
		private static bool _initialized;
		private static int _tick;
		private static ItemFlashPlugin _plugin;

		/// <summary>玩家丢物登记（"记录驱动"判定：以丢出时的类型/数量/位置为准，不依赖掉落物静止状态）</summary>
		private sealed class DropRecord
		{
			public int Who;              // 玩家索引
			public int Type;             // 物品 ID
			public int Stack;            // 丢出数量
			public Vector2 Position;     // 丢出时位置（左上角，像素）
			public long Ticks;           // 丢出时的服务器 tick
		}

		/// <summary>组动画会话：管理一组掉落物的阶段性动画（simple 升空 / zenith 仪式）</summary>
		private sealed class GroupSession
		{
			public string Mode;              // "simple" | "zenith"
			public List<int> ItemIndices;    // 参与动画的所有掉落物槽
			public int CoreIndex;            // zenith：中央核心剑槽位（铜短剑，持续被劈）
			public List<int> FlyOrder;       // zenith：依次飞入中心的剑槽位（不含核心剑）
			public int Owner;                // 触发玩家索引
			public Vector2 Center;           // 仪式中心（像素）
			public int ResultItemId;         // 结束生成的结果物品 ID
			public int ResultCount;
			public int Phase;                // 0=rise 1=charge 2=flyin 3=final 4=done
			public int PhaseTick;            // 当前阶段累计帧
			public List<Vector2> RiseFrom;   // 每把剑起始位置（上升插值用，索引对应 ItemIndices）
			public List<float> SpinAngle;    // 每把剑当前旋转角（索引对应 ItemIndices）
			public bool[] FlewOut;           // 飞入完成标记（索引对应 FlyOrder）
			public int FlyCursor;            // 飞入队列游标
			public Color[] SwordColors;      // 每把剑的专属闪电颜色（索引对应 ItemIndices）
			public bool ResultSpawned;
			public int SyncAccum;
		}

		private static string ConfigPath => Path.Combine(TShock.SavePath, "ItemFlash", "config.json");

		// ---- 生命周期 ----
		public static void Initialize(ItemFlashPlugin plugin)
		{
			if (_initialized) return;
			_initialized = true;
			_plugin = plugin;

			LoadConfig();
			GetDataHandlers.ItemDrop.Register(OnItemDrop);
			ServerApi.Hooks.GameUpdate.Register(plugin, OnGameUpdate);
			GeneralHooks.ReloadEvent += OnReload;
			TShockAPI.Commands.ChatCommands.Add(new Command("tshock.admin", ZenithTestCommand, "iflash")
			{
				HelpText = "测试天顶剑仪式动画（在脚下生成材料并直接触发，绕过判定）"
			});

			TShock.Log.ConsoleInfo("[ItemFlash] 掉落物献祭/合成插件已启用（默认配方：天顶剑合成 + 土块献祭）");
		}

		public static void Dispose()
		{
			if (!_initialized) return;
			_initialized = false;

			GetDataHandlers.ItemDrop.UnRegister(OnItemDrop);
			ServerApi.Hooks.GameUpdate.Deregister(_plugin, OnGameUpdate);
			GeneralHooks.ReloadEvent -= OnReload;

			lock (SyncLock)
			{
				_records.Clear();
				_sessions.Clear();
				_animatingIndexes.Clear();
			}
			TShock.Log.ConsoleInfo("[ItemFlash] 掉落物献祭插件已卸载");
		}

		private static void OnReload(ReloadEventArgs e)
		{
			LoadConfig();
			TShock.Log.ConsoleInfo("[ItemFlash] 配置已重载");
		}

		// ---- 配置加载 ----
		private static void LoadConfig()
		{
			try
			{
				if (!File.Exists(ConfigPath))
				{
					SaveDefaultConfig();
					return;
				}

				_config = JsonConvert.DeserializeObject<ItemFlashConfig>(File.ReadAllText(ConfigPath)) ?? new ItemFlashConfig();

				// 旧配置迁移：v1（无天顶剑配方）自动补默认配方；v2（核心剑错误）修正为铜短剑
				int oldVersion = _config.Version;
				if (_config.Version < 2)
				{
					bool hasZenith = _config.Recipes.Any(r => string.Equals(r.Animation, "zenith", StringComparison.OrdinalIgnoreCase));
					if (!hasZenith)
						_config.Recipes.Insert(0, BuildZenithRecipe());
					_config.Version = 2;
				}
				if (_config.Version < 3)
				{
					// zenith 配方核心剑修正为铜短剑（旧配置曾因 animateItemId 缺失被误修为泰拉刃）
					foreach (var r in _config.Recipes)
					{
						if (!string.Equals(r.Animation, "zenith", StringComparison.OrdinalIgnoreCase))
							continue;
						bool hasCopper = r.Items != null && r.Items.Any(i => i.ItemId == ItemID.CopperShortsword);
						if (hasCopper && r.AnimateItemId != ItemID.CopperShortsword)
							r.AnimateItemId = ItemID.CopperShortsword;
					}
					_config.Version = 3;
				}
				if (oldVersion < _config.Version)
				{
					SaveConfig();
					TShock.Log.ConsoleInfo($"[ItemFlash] 旧配置已迁移（v{oldVersion} → v{_config.Version}）");
				}

				// 修正 animateItemId：必须存在于 items 中，否则退回 items[0]
				foreach (var r in _config.Recipes)
				{
					if (r.Items == null || r.Items.Count == 0) continue;
					if (string.IsNullOrWhiteSpace(r.Animation))
						r.Animation = "simple";
					if (r.AnimateItemId == 0 || !r.Items.Any(i => i.ItemId == r.AnimateItemId))
						r.AnimateItemId = r.Items[0].ItemId;
					if (r.ResultCount < 1) r.ResultCount = 1;
				}

				if (_config.ClusterRange < 32) _config.ClusterRange = 32;
				if (_config.RecordWindowSeconds < 5) _config.RecordWindowSeconds = 5;
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[ItemFlash] 配置加载失败，使用默认配置: {ex.Message}");
				_config = new ItemFlashConfig();
			}
		}

		private static void SaveDefaultConfig()
		{
			_config = new ItemFlashConfig
			{
				Version = 3,
				Enabled = true,
				ClusterRange = 80,
				RecordWindowSeconds = 120,
				Recipes = new List<RecipeConfig>
				{
					BuildZenithRecipe(),
					BuildDirtRecipe(),
				},
			};

			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
				File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(_config, Formatting.Indented));
				TShock.Log.ConsoleInfo($"[ItemFlash] 已生成默认配置: {ConfigPath}");
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[ItemFlash] 写入默认配置失败: {ex.Message}");
			}
		}

		/// <summary>默认天顶剑合成配方（1.4.5 权威合成表）</summary>
		private static RecipeConfig BuildZenithRecipe() => new RecipeConfig
		{
			Name = "天顶剑合成",
			Items = new List<RecipeItemConfig>
			{
				new RecipeItemConfig { ItemId = ItemID.TerraBlade, Count = 1 },           // 泰拉刃
				new RecipeItemConfig { ItemId = ItemID.Meowmere, Count = 1 },              // 彩虹猫之刃
				new RecipeItemConfig { ItemId = ItemID.StarWrath, Count = 1 },             // 狂星之怒
				new RecipeItemConfig { ItemId = ItemID.InfluxWaver, Count = 1 },           // 波涌之刃
				new RecipeItemConfig { ItemId = ItemID.TheHorsemansBlade, Count = 1 },     // 南瓜剑
				new RecipeItemConfig { ItemId = ItemID.Seedler, Count = 1 },               // 种子弯刀
				new RecipeItemConfig { ItemId = ItemID.Starfury, Count = 1 },              // 星怒
				new RecipeItemConfig { ItemId = ItemID.BeeKeeper, Count = 1 },             // 养蜂人
				new RecipeItemConfig { ItemId = ItemID.EnchantedSword, Count = 1 },        // 附魔剑
				new RecipeItemConfig { ItemId = ItemID.CopperShortsword, Count = 1 },      // 铜短剑
			},
			Animation = "zenith",
			AnimateItemId = ItemID.CopperShortsword, // 核心剑：铜短剑在中央持续被劈
			ResultItemId = ItemID.Zenith,
			ResultCount = 1,
			Message = "天顶剑凝聚完成！",
		};

		/// <summary>默认土块献祭配方</summary>
		private static RecipeConfig BuildDirtRecipe() => new RecipeConfig
		{
			Name = "土块献祭",
			Items = new List<RecipeItemConfig>
			{
				new RecipeItemConfig { ItemId = ItemID.DirtBlock, Count = 1 },
				new RecipeItemConfig { ItemId = ItemID.GoldCoin, Count = 2 },
			},
			AnimateItemId = ItemID.DirtBlock,
			Message = "献祭成功！土块带着金币升天啦",
		};

		/// <summary>将当前配置写回磁盘</summary>
		private static void SaveConfig()
		{
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
				File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(_config, Formatting.Indented));
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[ItemFlash] 写配置失败: {ex.Message}");
			}
		}

		// ---- 事件：玩家丢物登记（不拦截，仅记录来源） ----
		private static void OnItemDrop(object? sender, GetDataHandlers.ItemDropEventArgs e)
		{
			if (!_config.Enabled || e.Player == null || e.Type <= 0 || e.Type >= ItemID.Count || e.Stacks <= 0)
				return;

			lock (SyncLock)
			{
				_records.Add(new DropRecord
				{
					Who = e.Player.Index,
					Type = e.Type,
					Stack = e.Stacks,
					Position = e.Position,
					Ticks = _tick,
				});
			}
		}

		// ---- GameUpdate：推进动画 + 周期扫描判定 ----
		private static void OnGameUpdate(EventArgs args)
		{
			try
			{
				_tick++;
				AdvanceSessions();

				if (_tick % ScanInterval != 0 || !_config.Enabled)
					return;

				PruneRecords();
				TryMatchRecipes();
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[ItemFlash] GameUpdate 异常: {ex}");
			}
		}

		private static void PruneRecords()
		{
			long window = _config.RecordWindowSeconds * 60L;
			lock (SyncLock)
				_records.RemoveAll(r => _tick - r.Ticks > window);
		}

		// ---- 配方匹配（记录驱动：以玩家丢物登记为准，不依赖掉落物静止状态） ----
		private static void TryMatchRecipes()
		{
			if (_config.Recipes.Count == 0)
				return;

			long oldest = _tick - _config.RecordWindowSeconds * 60L;
			List<DropRecord> records;
			lock (SyncLock)
			{
				if (_records.Count == 0)
					return; // 无任何丢物登记，直接跳过（优化：避免每轮全遍历 Main.item）
				records = _records.Where(r => r.Ticks >= oldest).ToList();
			}
			if (records.Count == 0)
				return;

			// 每轮扫描只收集一次可用掉落物快照，所有簇/配方复用（优化：避免 N 簇 N 次全遍历）
			var activeDrops = new List<int>();
			for (int i = 0; i < Main.item.Length; i++)
			{
				if (_animatingIndexes.Contains(i))
					continue;
				var it = Main.item[i];
				if (!it.active || it.IsAir || it.type <= 0 || it.stack <= 0)
					continue;
				activeDrops.Add(i);
			}
			if (activeDrops.Count == 0)
				return;

			// 按玩家分组，组内把丢物记录按位置聚类（丢在一起的记录归为一簇）
			foreach (var group in records.GroupBy(r => r.Who))
			{
				var list = group.ToList();
				var used = new bool[list.Count];

				for (int k = 0; k < list.Count; k++)
				{
					if (used[k]) continue;

					var cluster = new List<DropRecord> { list[k] };
					used[k] = true;

					bool grew;
					do
					{
						grew = false;
						for (int j = 0; j < list.Count; j++)
						{
							if (used[j]) continue;
							if (cluster.Any(c => Vector2.Distance(c.Position, list[j].Position) <= _config.ClusterRange))
							{
								cluster.Add(list[j]);
								used[j] = true;
								grew = true;
							}
						}
					} while (grew);

					foreach (var recipe in _config.Recipes)
					{
						if (!MatchRecipe(cluster, recipe))
							continue;

						// 簇质心作为判定锚点
						Vector2 anchor = Vector2.Zero;
						foreach (var r in cluster) anchor += r.Position;
						anchor /= cluster.Count;

						if (!VerifyDropsNear(activeDrops, anchor, recipe))
							continue; // 区域内实际掉落物不足（被捡走/落点漂移），等下一轮扫描

						Trigger(recipe, anchor, group.Key, activeDrops);
						break; // 一个簇最多触发一个配方
					}
				}
			}
		}

		/// <summary>从快照中收集锚点半径内的掉落物槽位（快照已过滤动画中/非活动的槽）</summary>
		private static List<int> FindDropsNear(List<int> activeDrops, Vector2 anchor, int radius)
		{
			var result = new List<int>();
			foreach (var i in activeDrops)
			{
				if (Vector2.Distance(Main.item[i].Center, anchor) <= radius)
					result.Add(i);
			}
			return result;
		}

		/// <summary>区域验证：锚点附近实际存在的掉落物必须满足配方所需数量（允许 NPC 掉落/残留物混入，扣减时才精确区分）</summary>
		private static bool VerifyDropsNear(List<int> activeDrops, Vector2 anchor, RecipeConfig recipe)
		{
			var needed = recipe.Items
				.GroupBy(i => i.ItemId)
				.ToDictionary(g => g.Key, g => g.Sum(i => i.Count));
			var idxs = FindDropsNear(activeDrops, anchor, FindRadius);
			foreach (var kv in needed)
			{
				int have = idxs.Where(i => Main.item[i].type == kv.Key).Sum(i => Main.item[i].stack);
				if (have < kv.Value)
					return false;
			}
			return true;
		}

		/// <summary>记录簇与配方匹配：只统计配方涉及类型的丢物数量是否足够（允许玩家同时丢了其他无关物品）</summary>
		private static bool MatchRecipe(List<DropRecord> cluster, RecipeConfig recipe)
		{
			if (recipe.Items == null || recipe.Items.Count == 0)
				return false;

			// 按物品 ID 合并数量，避免配置里重复 itemId 导致 ToDictionary 崩溃
			var needed = recipe.Items
				.GroupBy(i => i.ItemId)
				.ToDictionary(g => g.Key, g => g.Sum(i => i.Count));

			foreach (var kv in needed)
			{
				int have = cluster.Where(r => r.Type == kv.Key).Sum(r => r.Stack);
				if (have < kv.Value)
					return false;
			}
			return true;
		}

		// ---- 触发：材料收集 + 组动画 ----
		private static void Trigger(RecipeConfig recipe, Vector2 anchor, int who, List<int> activeDrops)
		{
			var needed = recipe.Items
				.GroupBy(i => i.ItemId)
				.ToDictionary(g => g.Key, g => g.Sum(i => i.Count));
			var idxs = FindDropsNear(activeDrops, anchor, FindRadius);
			bool zenith = string.Equals(recipe.Animation, "zenith", StringComparison.OrdinalIgnoreCase);

			if (zenith)
			{
				// 天顶剑仪式：每种材料取一个掉落物（stack 最大），全部参与动画
				var animIndices = new List<int>();
				var riseFrom = new List<Vector2>();
				var angles = new List<float>();
				foreach (var kv in needed)
				{
					var idx = idxs.Where(i => Main.item[i].type == kv.Key)
						.OrderByDescending(i => Main.item[i].stack)
						.FirstOrDefault(-1);
					if (idx < 0)
						return; // 防御：VerifyDropsNear 已保证，正常不会发生

					animIndices.Add(idx);
					riseFrom.Add(Main.item[idx].position);
					angles.Add(0f);
				}

				// 核心剑 = animateItemId 类型（铜短剑），在中央持续被劈
				int coreIdx = -1;
				for (int j = 0; j < animIndices.Count; j++)
				{
					if (Main.item[animIndices[j]].type == recipe.AnimateItemId)
					{
						coreIdx = animIndices[j];
						break;
					}
				}
				if (coreIdx < 0)
					coreIdx = animIndices[0]; // 防御
				// 飞入顺序由弱到强（按武器伤害升序），最后飞入的最强（彩虹猫）
				var flyOrder = animIndices.Where(i => i != coreIdx)
					.OrderBy(i => Main.item[i].damage)
					.ToList();

				// 每把剑分配专属闪电颜色（含核心剑）
				var swordColors = new Color[animIndices.Count];
				for (int j = 0; j < animIndices.Count; j++)
					swordColors[j] = Palette[j % Palette.Length];

				foreach (var i in animIndices)
				{
					Main.item[i].keepTime = KeepTimeLock;
					Main.item[i].velocity = Vector2.Zero;
				}
				_sessions.Add(new GroupSession
				{
					Mode = "zenith",
					ItemIndices = animIndices,
					CoreIndex = coreIdx,
					FlyOrder = flyOrder,
					Owner = who,
					Center = anchor,
					RiseFrom = riseFrom,
					SpinAngle = angles,
					FlewOut = new bool[flyOrder.Count],
					SwordColors = swordColors,
					ResultItemId = recipe.ResultItemId,
					ResultCount = recipe.ResultCount,
				});
				foreach (var i in animIndices)
					NetMessage.SendData(21, -1, -1, null, i, 2, 0); // number2=2：客户端持续保持 keepTime，禁止拾取拉取动画

				// 登记动画槽位（O(1) 查询，避免后续扫描重复遍历会话）
				foreach (var i in animIndices)
					_animatingIndexes.Add(i);
			}
			else
			{
				// simple 模式：主角升空动画 + 非主角材料精确扣减（多余的保留，避免误耗 NPC 掉落）
				var mainIdx = idxs.Where(i => Main.item[i].type == recipe.AnimateItemId)
					.OrderByDescending(i => Main.item[i].stack)
					.FirstOrDefault(-1);
				if (mainIdx < 0)
					return;

				foreach (var kv in needed)
				{
					if (kv.Key == recipe.AnimateItemId)
						continue;

					int remaining = kv.Value;
					foreach (var i in idxs.Where(i => Main.item[i].type == kv.Key).OrderByDescending(i => Main.item[i].stack))
					{
						if (remaining <= 0) break;
						var it = Main.item[i];
						int take = Math.Min(it.stack, remaining);
						it.stack -= take;
						remaining -= take;
						if (it.stack <= 0)
							it.TurnToAir();
						NetMessage.SendData(21, -1, -1, null, i, 0, 0); // 广播新状态（stack 或消失）
					}
				}

				var mit = Main.item[mainIdx];
				mit.keepTime = KeepTimeLock;
				mit.velocity = Vector2.Zero;
				_sessions.Add(new GroupSession
				{
					Mode = "simple",
					ItemIndices = new List<int> { mainIdx },
					Owner = who,
					Center = anchor,
					RiseFrom = new List<Vector2> { mit.position },
					FlewOut = Array.Empty<bool>(),
					ResultItemId = recipe.ResultItemId,
					ResultCount = recipe.ResultCount,
				});
				NetMessage.SendData(21, -1, -1, null, mainIdx, 2, 0); // number2=2：防客户端拾取拉取

				// 登记动画槽位
				_animatingIndexes.Add(mainIdx);
			}

			// 清空该玩家全部有效登记，防止残留记录导致同一堆物品重复触发
			lock (SyncLock)
				_records.RemoveAll(r => r.Who == who);

			// 玩家提示
			if (!string.IsNullOrWhiteSpace(recipe.Message) && who >= 0 && who < TShock.Players.Length)
				TShock.Players[who]?.SendSuccessMessage(recipe.Message);

			TShock.Log.ConsoleInfo($"[ItemFlash] 触发配方「{recipe.Name}」({recipe.Animation}) 玩家: {PlayerName(who)}");
		}

		// ---- 动画推进（每帧） ----
		private static void AdvanceSessions()
		{
			if (_sessions.Count == 0)
				return;

			for (int k = _sessions.Count - 1; k >= 0; k--)
			{
				var s = _sessions[k];

				// 有效性检查：参与动画的掉落物必须都还在（已飞入消失的剑、核心剑除外处理）
				bool zenith = string.Equals(s.Mode, "zenith", StringComparison.OrdinalIgnoreCase);
				bool valid = true;
				for (int j = 0; j < s.ItemIndices.Count; j++)
				{
					int idx = s.ItemIndices[j];
					if (zenith && idx == s.CoreIndex)
						continue; // 核心剑（final 结束时统一处理）
					if (zenith && s.FlyOrder != null)
					{
						int flyIdx = s.FlyOrder.IndexOf(idx);
						if (flyIdx >= 0 && flyIdx < s.FlewOut.Length && s.FlewOut[flyIdx])
							continue; // 已飞入消失
					}
					if (idx < 0 || idx >= Main.item.Length || !Main.item[idx].active || Main.item[idx].IsAir)
					{
						valid = false;
						break;
					}
				}
				if (!valid)
				{
					foreach (var i in s.ItemIndices)
						_animatingIndexes.Remove(i);
					_sessions.RemoveAt(k); // 被异常移除，放弃动画
					continue;
				}

				bool done = string.Equals(s.Mode, "zenith", StringComparison.OrdinalIgnoreCase)
					? AdvanceZenith(s)
					: AdvanceSimple(s);

				// 定期广播 21 号包，同步位置给所有客户端。number2=2（bitsByte bit1=flag17）→
				// 客户端收到后 keepTime=100，动画期间永不触发拾取拉取动画（防吸附）
				s.SyncAccum++;
				if (s.SyncAccum >= SyncInterval)
				{
					s.SyncAccum = 0;
					foreach (var i in s.ItemIndices)
						NetMessage.SendData(21, -1, -1, null, i, 2, 0);
				}

				if (done)
				{
					foreach (var i in s.ItemIndices)
						_animatingIndexes.Remove(i);
					_sessions.RemoveAt(k);
				}
			}
		}

		/// <summary>simple 动画：主角线性升空后消失（可选生成结果）。返回 true = 会话结束</summary>
		private static bool AdvanceSimple(GroupSession s)
		{
			int idx = s.ItemIndices[0];
			var it = Main.item[idx];

			s.PhaseTick++;
			float t = Math.Min(1f, s.PhaseTick / (float)FlashDuration);
			it.position = s.RiseFrom[0] + new Vector2(0f, -FlashHeight * t);
			it.velocity = Vector2.Zero;
			it.keepTime = KeepTimeLock; // 防捡：FindOwner 在 keepTime>0 时直接返回

			if (s.PhaseTick >= FlashDuration)
			{
				it.TurnToAir();
				NetMessage.SendData(21, -1, -1, null, idx, 0, 0);
				BroadcastParticles(it.Center, s.Owner); // 消失瞬间粒子
				SpawnResult(s);
				return true;
			}
			return false;
		}

		/// <summary>zenith 仪式阶段机：rise 聚拢 → charge 核心剑移中央+蓄力 → flyin 其它剑依次飞入（效果渐强）→ final 只剩核心剑续劈 3 秒 → 生成结果。返回 true = 会话结束</summary>
		private static bool AdvanceZenith(GroupSession s)
		{
			int n = s.ItemIndices.Count;
			int core = s.CoreIndex;
			int flyCount = s.FlyOrder.Count;
			var coreCenter = s.Center + new Vector2(0f, -RiseHeight); // 中心悬浮点
			s.PhaseTick++;

			switch (s.Phase)
			{
				case 0: // 上升聚拢：所有剑插值到中心上方环形
				{
					float t = Math.Min(1f, s.PhaseTick / (float)RiseDuration);
					for (int j = 0; j < n; j++)
					{
						float ang = MathF.PI * 2f * j / n;
						var target = s.Center + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * SpinRadius + new Vector2(0f, -RiseHeight);
						var it = Main.item[s.ItemIndices[j]];
						it.position = Vector2.Lerp(s.RiseFrom[j], target, t);
						it.velocity = Vector2.Zero;
						it.keepTime = KeepTimeLock;
						s.SpinAngle[j] = ang;
					}
					if (s.PhaseTick >= RiseDuration)
					{
						s.Phase = 1;
						s.PhaseTick = 0;
					}
					return false;
				}

				case 1: // 蓄力：核心剑（铜短剑）移入中央，其它剑继续环绕，中央开始劈雷 + 闪光粒子
				{
					// 核心剑插值到中心
					float t = Math.Min(1f, s.PhaseTick / 60f);
					var coreFrom = coreCenter + new Vector2(MathF.Cos(s.SpinAngle[s.ItemIndices.IndexOf(core)]), MathF.Sin(s.SpinAngle[s.ItemIndices.IndexOf(core)])) * SpinRadius;
					var coreItem = Main.item[core];
					coreItem.position = Vector2.Lerp(coreFrom, coreCenter, t);
					coreItem.velocity = Vector2.Zero;
					coreItem.keepTime = KeepTimeLock;

					// 其它剑继续环绕
					for (int j = 0; j < n; j++)
					{
						if (s.ItemIndices[j] == core) continue;
						s.SpinAngle[j] += SpinSpeed;
						var pos = s.Center + new Vector2(MathF.Cos(s.SpinAngle[j]), MathF.Sin(s.SpinAngle[j])) * SpinRadius + new Vector2(0f, -RiseHeight);
						var it = Main.item[s.ItemIndices[j]];
						it.position = pos;
						it.velocity = Vector2.Zero;
						it.keepTime = KeepTimeLock;
					}

					// 中心雷击（每 18 tick，初始 1 道，主色 = 核心剑颜色）
					if (s.PhaseTick % 18 == 0)
						BroadcastLightningBurst(coreCenter, s.Owner, 1, s.SwordColors[s.ItemIndices.IndexOf(core)]);
					// 中心闪光粒子（每 30 tick，强度 1）
					if (s.PhaseTick % 30 == 0)
						BroadcastChargeEffects(coreCenter, s.Owner, 1);

					if (s.PhaseTick >= ChargeDuration)
					{
						s.Phase = 2;
						s.PhaseTick = 0;
						s.FlyCursor = 0;
					}
					return false;
				}

				case 2: // 飞入：其它剑依次飞向中心，雷击与粒子逐渐加强
				{
					// 核心剑（铜短剑）固定在中心持续被劈（必须每帧指定位置，否则物理接管掉地）
					var coreItem = Main.item[core];
					coreItem.position = coreCenter;
					coreItem.velocity = Vector2.Zero;
					coreItem.keepTime = KeepTimeLock;

					// 派出新剑：落雷击中该剑（劈在剑所在环位），同时微光炼化 + 重铸金光，剑随后飞入
					while (s.FlyCursor < flyCount && s.PhaseTick >= s.FlyCursor * FlyInterval)
					{
						int slot = s.FlyOrder[s.FlyCursor];
						int idx = s.ItemIndices.IndexOf(slot);
						var swordPos = s.Center + new Vector2(MathF.Cos(s.SpinAngle[idx]), MathF.Sin(s.SpinAngle[idx])) * SpinRadius + new Vector2(0f, -RiseHeight);
						BroadcastLightning(swordPos, s.Owner, s.SwordColors[idx], new Vector2(0f, 200f)); // 落雷击中该剑（专属颜色）
						BroadcastParticleAt(ParticleOrchestraType.ShimmerTownNPC, swordPos, s.Owner);
						BroadcastParticleAt(ParticleOrchestraType.BestReforge, swordPos, s.Owner);
						s.FlyCursor++;
						// 每加一把剑，中心同时劈的雷就加一道（1 + 已飞入剑数），主色 = 刚加入的剑
						Color newMain = s.SwordColors[s.ItemIndices.IndexOf(s.FlyOrder[s.FlyCursor - 1])];
						BroadcastLightningBurst(coreCenter, s.Owner, 1 + s.FlyCursor, newMain);
					}

					// 更新飞行中的剑（从环绕位置收敛到中心）
					for (int j = 0; j < s.FlyCursor; j++)
					{
						var it = Main.item[s.FlyOrder[j]];
						int idx = s.ItemIndices.IndexOf(s.FlyOrder[j]);
						int elapsed = s.PhaseTick - j * FlyInterval;
						if (elapsed >= FlyDuration)
						{
							if (!s.FlewOut[j])
							{
								s.FlewOut[j] = true;
								it.TurnToAir();
								NetMessage.SendData(21, -1, -1, null, s.FlyOrder[j], 0, 0);
							}
							continue;
						}

						float f = elapsed / (float)FlyDuration;
						float ang = s.SpinAngle[idx];
						var ringPos = s.Center + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * SpinRadius + new Vector2(0f, -RiseHeight);
						it.position = Vector2.Lerp(ringPos, coreCenter, f); // 向中心收敛
						it.velocity = Vector2.Zero;
						it.keepTime = KeepTimeLock;
					}

					// 未派出的剑继续环绕转圈（不能停，否则物理接管会掉地/被吸附）
					for (int j = s.FlyCursor; j < flyCount; j++)
					{
						int slot = s.FlyOrder[j];
						int idx = s.ItemIndices.IndexOf(slot);
						s.SpinAngle[idx] += SpinSpeed;
						var pos = s.Center + new Vector2(MathF.Cos(s.SpinAngle[idx]), MathF.Sin(s.SpinAngle[idx])) * SpinRadius + new Vector2(0f, -RiseHeight);
						var it = Main.item[slot];
						it.position = pos;
						it.velocity = Vector2.Zero;
						it.keepTime = KeepTimeLock;
					}

					// 中心持续雷击（每 12 tick）：同时劈 1 + 已飞入剑数 道，主色 = 最后飞入的剑
					if (s.PhaseTick % 12 == 0)
					{
						Color main = s.FlyCursor > 0
							? s.SwordColors[s.ItemIndices.IndexOf(s.FlyOrder[s.FlyCursor - 1])]
							: s.SwordColors[s.ItemIndices.IndexOf(core)];
						BroadcastLightningBurst(coreCenter, s.Owner, 1 + s.FlyCursor, main);
					}
					// 闪光粒子渐强（每 15 tick，强度随已飞入数量递增）
					if (s.PhaseTick % 15 == 0)
						BroadcastChargeEffects(coreCenter, s.Owner, 2 + s.FlyCursor);

					// 全部飞入完成 → 只剩核心剑
					if (s.PhaseTick >= (flyCount - 1) * FlyInterval + FlyDuration)
					{
						s.Phase = 3;
						s.PhaseTick = 0;
					}
					return false;
				}

				case 3: // 终局：只剩核心剑，继续劈雷 3 秒，粒子爆发
				{
					var coreItem = Main.item[core];
					coreItem.position = coreCenter;
					coreItem.velocity = Vector2.Zero;
					coreItem.keepTime = KeepTimeLock;

					// 前段（0~150）：每 15t 全剑数彩色雷暴（主色 = 最后加入的剑/彩虹猫）
					if (s.PhaseTick < FinalBurstEnd && s.PhaseTick % 15 == 0)
					{
						Color mainColor = s.SwordColors[s.ItemIndices.IndexOf(s.FlyOrder[flyCount - 1])];
						BroadcastLightningBurst(coreCenter, s.Owner, 1 + flyCount, mainColor);
					}
					// 中段（150~270）：暂停 2 秒，无雷无粒子（酝酿蓄力）
					// 后段（270~330）：60 tick 依次劈 10 道彩色连环雷（每 6 帧一道）+ 粒子爆发渐强
					else if (s.PhaseTick >= FinalPauseEnd && (s.PhaseTick - FinalPauseEnd) % 6 == 0)
					{
						int seq = (s.PhaseTick - FinalPauseEnd) / 6;
						if (seq < 10)
						{
							var pos = coreCenter + new Vector2(Main.rand.Next(-160, 161), Main.rand.Next(-120, 121));
							BroadcastLightning(pos, s.Owner, Palette[seq % Palette.Length], new Vector2(0f, 200f));
							BroadcastChargeEffects(coreCenter, s.Owner, 4 + seq);
						}
					}

					if (s.PhaseTick >= FinalDuration)
					{
						// 核心剑炼化消失 → 生成天顶剑
						coreItem.TurnToAir();
						NetMessage.SendData(21, -1, -1, null, core, 0, 0);
						SpawnResult(s);
						return true;
					}
					return false;
				}

				default: // 4 done
				{
					SpawnResult(s);
					return true;
				}
			}
		}

		/// <summary>在仪式中心生成结果掉落物（幂等：每个会话只生成一次）。zenith 模式附加微光炼化 + 哥布林工匠重铸特效，结果为传奇词缀</summary>
		private static void SpawnResult(GroupSession s)
		{
			if (s.ResultSpawned || s.ResultItemId <= 0)
				return;
			s.ResultSpawned = true;

			try
			{
				bool zenith = string.Equals(s.Mode, "zenith", StringComparison.OrdinalIgnoreCase);

				if (zenith)
				{
					// 微光炼化特效（微光漩涡包裹）
					BroadcastParticleAt(ParticleOrchestraType.ShimmerTownNPC, s.Center, s.Owner);
					// 哥布林工匠完美重铸金光
					BroadcastParticleAt(ParticleOrchestraType.BestReforge, s.Center, s.Owner);
					BroadcastParticleAt(ParticleOrchestraType.BestReforge, s.Center + new Vector2(0f, -48f), s.Owner);
				}

				// 天顶剑结果带传奇（Legendary）词缀
				int prefix = zenith ? PrefixID.Legendary : 0;
				Item.NewItem(null, (int)s.Center.X, (int)s.Center.Y, 16, 16, s.ResultItemId, Math.Max(1, s.ResultCount), false, prefix, false);
				BroadcastParticles(s.Center, s.Owner);
				BroadcastParticles(s.Center + new Vector2(0f, -48f), s.Owner);
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[ItemFlash] 生成结果物品失败: {ex.Message}");
			}
		}

		/// <summary>广播粒子特效（ItemTransfer：金色物品转移粒子）</summary>
		private static void BroadcastParticles(Vector2 pos, int owner)
			=> BroadcastParticleAt(ParticleOrchestraType.ItemTransfer, pos, owner);

		/// <summary>按类型广播粒子特效（82 号 NetModule 通道，服务端广播直接写 socket、不经 NetManager.Read，不受 ParticleGuard 拦截）</summary>
		private static void BroadcastParticleAt(ParticleOrchestraType type, Vector2 pos, int owner)
		{
			try
			{
				var settings = new ParticleOrchestraSettings
				{
					PositionInWorld = pos,
					IndexOfPlayerWhoInvokedThis = (byte)Math.Clamp(owner, 0, 255),
					MovementVector = Vector2.Zero,
				};
				var packet = NetParticlesModule.Serialize(type, settings);
				NetManager.Instance.Broadcast(packet);
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[ItemFlash] 粒子广播失败: {ex.Message}");
			}
		}

		/// <summary>同时劈下 count 道落雷：50% 用主色（最后加入剑的颜色）更集中靠近中心，50% 随机彩色保持现有散布范围</summary>
		private static void BroadcastLightningBurst(Vector2 center, int owner, int count, Color mainColor)
		{
			if (count < 1) count = 1;
			if (count > 20) count = 20;

			for (int i = 0; i < count; i++)
			{
				if (Main.rand.Next(2) == 0)
				{
					// 50%：主色（最后加入剑的颜色），更集中靠近中心（垂直下劈）
					var pos = center + new Vector2(Main.rand.Next(-64, 65), Main.rand.Next(-56, 57));
					BroadcastLightning(pos, owner, mainColor, new Vector2(0f, 200f));
				}
				else
				{
					// 50%：随机彩色，保持现有散布范围（±160/±140）+ 随机倾斜
					var pos = center + new Vector2(Main.rand.Next(-160, 161), Main.rand.Next(-140, 141));
					var movement = new Vector2(Main.rand.Next(-120, 121), Main.rand.Next(140, 260));
					Color color = Palette[Main.rand.Next(Palette.Length)];
					BroadcastLightning(pos, owner, color, movement);
				}
			}
		}

		/// <summary>广播雷击（StormLightning 闪电粒子，可指定颜色与方向）。服务端广播直接写 socket、不经 NetManager.Read，不受 ParticleGuard 防线拦截</summary>
		private static void BroadcastLightning(Vector2 pos, int owner, Color color, Vector2 movement)
		{
			try
			{
				var settings = new ParticleOrchestraSettings
				{
					PositionInWorld = pos + new Vector2(0f, -80f), // 雷击起点在上方
					IndexOfPlayerWhoInvokedThis = (byte)Math.Clamp(owner, 0, 255),
					MovementVector = movement,
					UniqueInfoPiece = (int)color.PackedValue,      // 闪电颜色
				};
				var packet = NetParticlesModule.Serialize(ParticleOrchestraType.StormLightning, settings);
				NetManager.Instance.Broadcast(packet);
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[ItemFlash] 雷击广播失败: {ex.Message}");
			}
		}

		/// <summary>中心充能闪光粒子：按强度在中心周围广播 intensity 个金色粒子（效果随阶段增强）</summary>
		private static void BroadcastChargeEffects(Vector2 center, int owner, int intensity)
		{
			if (intensity < 1) intensity = 1;
			if (intensity > 10) intensity = 10;

			for (int i = 0; i < intensity; i++)
			{
				// 在中心周围随机偏移散布
				var pos = center + new Vector2(Main.rand.Next(-48, 49), Main.rand.Next(-48, 49));
				BroadcastParticles(pos, owner);
			}
		}

		private static string PlayerName(int who)
		{
			if (who >= 0 && who < TShock.Players.Length && TShock.Players[who] != null)
				return TShock.Players[who].Name;
			return $"#{who}";
		}

		/// <summary>测试指令：/iflash —— 在命令者脚下生成 10 把天顶剑材料掉落物并直接触发仪式动画（绕过判定，用于排查动画/判定问题）</summary>
		private static void ZenithTestCommand(CommandArgs args)
		{
			var plr = args.Player;
			if (plr == null || !plr.Active)
				return;

			int[] materials =
			{
				ItemID.TerraBlade,           // 泰拉刃
				ItemID.Meowmere,              // 彩虹猫之刃
				ItemID.StarWrath,             // 狂星之怒
				ItemID.InfluxWaver,           // 波涌之刃
				ItemID.TheHorsemansBlade,     // 南瓜剑
				ItemID.Seedler,               // 种子弯刀
				ItemID.Starfury,              // 星怒
				ItemID.BeeKeeper,             // 养蜂人
				ItemID.EnchantedSword,        // 附魔剑
				ItemID.CopperShortsword,      // 铜短剑
			};

			// 在玩家中心生成材料掉落物（同一坐标，rise 阶段会聚拢成环形）
			var center = plr.TPlayer.Center;
			var indices = new List<int>();
			var riseFrom = new List<Vector2>();
			var angles = new List<float>();

			foreach (int mat in materials)
			{
				int idx = Item.NewItem(null, (int)center.X, (int)center.Y, 16, 16, mat, 1, false, 0, false);
				if (idx < 0 || idx >= Main.item.Length)
					continue;

				indices.Add(idx);
				riseFrom.Add(Main.item[idx].position);
				angles.Add(0f);
				Main.item[idx].keepTime = KeepTimeLock;
				Main.item[idx].velocity = Vector2.Zero;
			}

			if (indices.Count == 0)
			{
				plr.SendErrorMessage("[ItemFlash] 测试物品生成失败");
				return;
			}

			// 核心剑 = 铜短剑（中央持续被劈），其它剑依次飞入
			int coreIdx = -1;
			for (int j = 0; j < indices.Count; j++)
			{
				if (Main.item[indices[j]].type == ItemID.CopperShortsword)
				{
					coreIdx = indices[j];
					break;
				}
			}
			if (coreIdx < 0)
				coreIdx = indices[0]; // 防御
			// 飞入顺序由弱到强（按武器伤害升序）
			var flyOrder = indices.Where(i => i != coreIdx)
				.OrderBy(i => Main.item[i].damage)
				.ToList();

			// 每把剑分配专属闪电颜色（含核心剑）
			var swordColors = new Color[indices.Count];
			for (int j = 0; j < indices.Count; j++)
				swordColors[j] = Palette[j % Palette.Length];

			_sessions.Add(new GroupSession
			{
				Mode = "zenith",
				ItemIndices = indices,
				CoreIndex = coreIdx,
				FlyOrder = flyOrder,
				Owner = plr.Index,
				Center = center,
				RiseFrom = riseFrom,
				SpinAngle = angles,
				FlewOut = new bool[flyOrder.Count],
				SwordColors = swordColors,
				ResultItemId = ItemID.Zenith,
				ResultCount = 1,
			});

			foreach (var i in indices)
				NetMessage.SendData(21, -1, -1, null, i, 2, 0); // number2=2：防客户端拾取拉取

			// 登记动画槽位
			foreach (var i in indices)
				_animatingIndexes.Add(i);

			plr.SendSuccessMessage($"[ItemFlash] 天顶剑仪式测试开始（{indices.Count} 把剑）");
			TShock.Log.ConsoleInfo($"[ItemFlash] 测试指令触发天顶剑仪式: {plr.Name}");
		}
	}
}
