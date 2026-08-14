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

		/// <summary>做动画的主角物品 ID（必须出现在 items 中；不填则取 items[0]）</summary>
		[JsonProperty("animateItemId")]
		public int AnimateItemId { get; set; }

		/// <summary>触发成功的玩家提示，空字符串则不提示</summary>
		[JsonProperty("message")]
		public string Message { get; set; } = "";
	}

	/// <summary>插件配置</summary>
	public class ItemFlashConfig
	{
		/// <summary>总开关</summary>
		[JsonProperty("enabled")]
		public bool Enabled { get; set; } = true;

		/// <summary>聚类判定距离（像素，1 格 = 16px；默认 80 ≈ 5 格）</summary>
		[JsonProperty("clusterRange")]
		public int ClusterRange { get; set; } = 80;

		/// <summary>玩家丢物登记有效期（秒），超过此时间的丢物记录不再参与判定</summary>
		[JsonProperty("recordWindowSeconds")]
		public int RecordWindowSeconds { get; set; } = 60;

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
		public override string Description => "掉落物组合献祭：把指定物品丢在一起，触发动画后消失";
		public override string Name => "ItemFlash";
		public override Version Version => new Version(1, 0, 0, 0);

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
		private const int FlashDuration = 60;     // 主角动画时长（tick），60 ≈ 1 秒
		private const int FlashHeight = 200;      // 主角抬升高度（像素），约 12.5 格
		private const int SyncInterval = 3;       // 动画期间每 3 帧广播一次 21 号包
		private const int ParticleInterval = 10;  // 动画期间每 10 帧广播一次粒子特效
		private const float IdleSpeed = 1f;       // 判定"落地静止"的速度阈值（像素/tick）
		private const float MatchRange = 96f;     // 丢物登记与掉落物的匹配距离（像素），≈ 6 格
		private const int KeepTimeLock = 600;     // 动画期间防捡锁（帧），600 ≈ 10 秒

		// ---- 状态 ----
		private static ItemFlashConfig _config = new();
		private static readonly List<DropRecord> _records = new();
		private static readonly List<FlashSession> _sessions = new();
		private static readonly object SyncLock = new();
		private static bool _initialized;
		private static int _tick;

		/// <summary>玩家丢物登记（用于把掉落物与"玩家丢的"来源关联，排除 NPC 掉落）</summary>
		private sealed class DropRecord
		{
			public int Who;              // 玩家索引
			public int Type;             // 物品 ID
			public Vector2 Position;     // 丢出时位置（左上角，像素）
			public long Ticks;           // 丢出时的服务器 tick
		}

		/// <summary>主角动画会话</summary>
		private sealed class FlashSession
		{
			public int ItemIndex;        // 主角掉落物槽位
			public int Owner;            // 触发玩家索引
			public int TickLeft;         // 剩余帧
			public Vector2 StartPos;     // 动画起点（左上角，像素）
			public int SyncAccum;
			public int ParticleAccum;
		}

		private static string ConfigPath => Path.Combine(TShock.SavePath, "ItemFlash", "config.json");

		// ---- 生命周期 ----
		public static void Initialize(ItemFlashPlugin plugin)
		{
			if (_initialized) return;
			_initialized = true;

			LoadConfig();
			GetDataHandlers.ItemDrop.Register(OnItemDrop);
			ServerApi.Hooks.GameUpdate.Register(plugin, OnGameUpdate);
			GeneralHooks.ReloadEvent += OnReload;

			TShock.Log.ConsoleInfo("[ItemFlash] 掉落物献祭插件已启用（默认配方：1 土块 + 2 金币）");
		}

		public static void Dispose()
		{
			if (!_initialized) return;
			_initialized = false;

			GetDataHandlers.ItemDrop.UnRegister(OnItemDrop);
			//ServerApi.Hooks.GameUpdate.UnRegister(OnGameUpdate);
			GeneralHooks.ReloadEvent -= OnReload;

			lock (SyncLock)
			{
				_records.Clear();
				_sessions.Clear();
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

				// 修正 animateItemId：必须存在于 items 中，否则退回 items[0]
				foreach (var r in _config.Recipes)
				{
					if (r.Items == null || r.Items.Count == 0) continue;
					if (r.AnimateItemId == 0 || !r.Items.Any(i => i.ItemId == r.AnimateItemId))
						r.AnimateItemId = r.Items[0].ItemId;
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
				Enabled = true,
				ClusterRange = 80,
				RecordWindowSeconds = 60,
				Recipes = new List<RecipeConfig>
				{
					new RecipeConfig
					{
						Name = "土块献祭",
						Items = new List<RecipeItemConfig>
						{
							new RecipeItemConfig { ItemId = ItemID.DirtBlock, Count = 1 },
							new RecipeItemConfig { ItemId = ItemID.GoldCoin, Count = 2 },
						},
						AnimateItemId = ItemID.DirtBlock,
						Message = "献祭成功！土块带着金币升天啦",
					},
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
					Position = e.Position,
					Ticks = _tick,
				});
			}
		}

		// ---- GameUpdate：推进动画 + 周期扫描判定 ----
		private static void OnGameUpdate(EventArgs args)
		{
			_tick++;
			AdvanceSessions();

			if (_tick % ScanInterval != 0 || !_config.Enabled)
				return;

			PruneRecords();
			TryMatchRecipes();
		}

		private static void PruneRecords()
		{
			long window = _config.RecordWindowSeconds * 60L;
			lock (SyncLock)
				_records.RemoveAll(r => _tick - r.Ticks > window);
		}

		// ---- 配方匹配 ----
		private static void TryMatchRecipes()
		{
			if (_config.Recipes.Count == 0)
				return;

			// 1. 收集"落地静止"的掉落物，并关联到丢它的玩家（NPC 掉落物无登记，跳过）
			var drops = new List<(int Index, int Type, int Stack, Vector2 Center, int Who)>();
			for (int i = 0; i < Main.item.Length; i++)
			{
				if (_sessions.Any(s => s.ItemIndex == i))
					continue; // 正在播放动画的主角，不参与新判定

				var it = Main.item[i];
				if (!it.active || it.IsAir || it.type <= 0 || it.stack <= 0)
					continue;
				if (it.velocity.Length() > IdleSpeed)
					continue; // 还在飞，等落地

				int who = FindRecordOwner(it.type, it.Center);
				if (who < 0)
					continue;

				drops.Add((i, it.type, it.stack, it.Center, who));
			}
			if (drops.Count == 0)
				return;

			// 2. 按玩家分组，组内做空间聚类（贪心连通）
			foreach (var group in drops.GroupBy(d => d.Who))
			{
				var list = group.ToList();
				var used = new bool[list.Count];

				for (int k = 0; k < list.Count; k++)
				{
					if (used[k]) continue;

					var cluster = new List<(int Index, int Type, int Stack, Vector2 Center)>
					{
						(list[k].Index, list[k].Type, list[k].Stack, list[k].Center),
					};
					used[k] = true;

					bool grew;
					do
					{
						grew = false;
						for (int j = 0; j < list.Count; j++)
						{
							if (used[j]) continue;
							if (cluster.Any(c => Vector2.Distance(c.Center, list[j].Center) <= _config.ClusterRange))
							{
								cluster.Add((list[j].Index, list[j].Type, list[j].Stack, list[j].Center));
								used[j] = true;
								grew = true;
							}
						}
					} while (grew);

					foreach (var recipe in _config.Recipes)
					{
						if (MatchRecipe(cluster, recipe))
						{
							Trigger(recipe, cluster, group.Key);
							break; // 一个聚类最多触发一个配方
						}
					}
				}
			}
		}

		/// <summary>在登记表中查找最近的同类型有效记录，返回玩家索引（-1 = 未找到）</summary>
		private static int FindRecordOwner(int type, Vector2 center)
		{
			long oldest = _tick - _config.RecordWindowSeconds * 60L;
			lock (SyncLock)
			{
				int best = -1;
				float bestDist = MatchRange;
				for (int i = 0; i < _records.Count; i++)
				{
					var r = _records[i];
					if (r.Ticks < oldest || r.Type != type)
						continue;
					float d = Vector2.Distance(r.Position, center);
					if (d < bestDist)
					{
						bestDist = d;
						best = r.Who;
					}
				}
				return best;
			}
		}

		/// <summary>聚类与配方匹配：聚类内只能出现配方涉及的类型，且每种数量必须足够</summary>
		private static bool MatchRecipe(List<(int Index, int Type, int Stack, Vector2 Center)> cluster, RecipeConfig recipe)
		{
			if (recipe.Items == null || recipe.Items.Count == 0)
				return false;

			// 按物品 ID 合并数量，避免配置里重复 itemId 导致 ToDictionary 崩溃
			var needed = recipe.Items
				.GroupBy(i => i.ItemId)
				.ToDictionary(g => g.Key, g => g.Sum(i => i.Count));

			// 聚类中出现配方以外的类型 → 不匹配
			foreach (var d in cluster)
				if (!needed.ContainsKey(d.Type))
					return false;

			// 每种材料数量必须足够
			foreach (var kv in needed)
			{
				int have = cluster.Where(d => d.Type == kv.Key).Sum(d => d.Stack);
				if (have < kv.Value)
					return false;
			}
			return true;
		}

		// ---- 触发：材料消失 + 主角动画 ----
		private static void Trigger(RecipeConfig recipe, List<(int Index, int Type, int Stack, Vector2 Center)> cluster, int who)
		{
			// 主角 = animateItemId 对应的掉落物（同类型多个时选 stack 最大的）
			var main = cluster
				.Where(d => d.Type == recipe.AnimateItemId)
				.OrderByDescending(d => d.Stack)
				.FirstOrDefault();
			if (main.Type != recipe.AnimateItemId)
				return; // 防御：聚类中没有主角类型（正常不会发生）

			// 非主角材料静默消失
			foreach (var d in cluster)
			{
				if (d.Index == main.Index)
					continue;
				if (d.Index >= 0 && d.Index < Main.item.Length && Main.item[d.Index].active)
				{
					Main.item[d.Index].TurnToAir();
					NetMessage.SendData(21, -1, -1, null, d.Index, 0, 0);
				}
			}

			// 主角：防捡锁 + 动画会话
			if (main.Index >= 0 && main.Index < Main.item.Length && Main.item[main.Index].active)
			{
				var it = Main.item[main.Index];
				it.keepTime = KeepTimeLock;
				it.velocity = Vector2.Zero;
				_sessions.Add(new FlashSession
				{
					ItemIndex = main.Index,
					Owner = who,
					TickLeft = FlashDuration,
					StartPos = it.position,
				});
				NetMessage.SendData(21, -1, -1, null, main.Index, 0, 0);
			}

			// 玩家提示
			if (!string.IsNullOrWhiteSpace(recipe.Message) && who >= 0 && who < TShock.Players.Length)
				TShock.Players[who]?.SendSuccessMessage(recipe.Message);

			TShock.Log.ConsoleInfo($"[ItemFlash] 触发配方「{recipe.Name}」 玩家: {PlayerName(who)}");
		}

		// ---- 主角动画推进（每帧） ----
		private static void AdvanceSessions()
		{
			if (_sessions.Count == 0)
				return;

			for (int k = _sessions.Count - 1; k >= 0; k--)
			{
				var s = _sessions[k];

				if (s.ItemIndex < 0 || s.ItemIndex >= Main.item.Length)
				{
					_sessions.RemoveAt(k);
					continue;
				}

				var it = Main.item[s.ItemIndex];
				if (!it.active || it.IsAir)
				{
					_sessions.RemoveAt(k); // 被异常移除，放弃动画
					continue;
				}

				// 线性抬升路径（完全由服务端控制位置，不依赖物理）
				float t = 1f - (float)s.TickLeft / FlashDuration; // 0 → 1
				it.position = s.StartPos + new Vector2(0f, -FlashHeight * t);
				it.velocity = Vector2.Zero;
				it.keepTime = KeepTimeLock; // 防捡：FindOwner 在 keepTime>0 时直接返回

				// 定期广播 21 号包，同步位置给所有客户端
				s.SyncAccum++;
				if (s.SyncAccum >= SyncInterval)
				{
					s.SyncAccum = 0;
					NetMessage.SendData(21, -1, -1, null, s.ItemIndex, 0, 0);
				}

				// 定期广播粒子特效（82 号 NetModule 通道，服务端广播不受 ParticleGuard 拦截）
				s.ParticleAccum++;
				if (s.ParticleAccum >= ParticleInterval)
				{
					s.ParticleAccum = 0;
					BroadcastParticles(it.Center, s.Owner);
				}

				s.TickLeft--;
				if (s.TickLeft <= 0)
				{
					// 动画结束：掉落物消失（type=0 → 客户端 SetDefaults(0) → active=false）
					it.TurnToAir();
					NetMessage.SendData(21, -1, -1, null, s.ItemIndex, 0, 0);
					BroadcastParticles(it.Center, s.Owner); // 消失瞬间补一发粒子
					_sessions.RemoveAt(k);
				}
			}
		}

		/// <summary>广播粒子特效（ItemTransfer：金色物品转移粒子）</summary>
		private static void BroadcastParticles(Vector2 pos, int owner)
		{
			try
			{
				var settings = new ParticleOrchestraSettings
				{
					PositionInWorld = pos,
					IndexOfPlayerWhoInvokedThis = (byte)Math.Clamp(owner, 0, 255),
					MovementVector = Vector2.Zero,
				};
				var packet = NetParticlesModule.Serialize(ParticleOrchestraType.ItemTransfer, settings);
				NetManager.Instance.Broadcast(packet);
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[ItemFlash] 粒子广播失败: {ex.Message}");
			}
		}

		private static string PlayerName(int who)
		{
			if (who >= 0 && who < TShock.Players.Length && TShock.Players[who] != null)
				return TShock.Players[who].Name;
			return $"#{who}";
		}
	}
}
