using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.GameContent.Events;
using TShockAPI;
using Rests;
using Newtonsoft.Json.Linq;

namespace TShockData
{
	/// <summary>
	/// 简易世界修改器（REST 版）
	/// 移植自 hufang360/WorldModify（v20230305）的世界参数字段能力，仅保留 REST 供前端查看/修改，不注册任何游戏内指令。
	/// 权限沿用原插件指令权限：查看 = worldinfo，修改 = worldmodify。
	/// 动态应用机制：修改 Main.* 字段后广播 WorldInfo 包（PacketTypes.WorldInfo），客户端即时生效。
	/// </summary>
	public static class WorldModify
	{
		// ═══════════════ 字段定义 ═══════════════

		private sealed class FieldDef
		{
			public string Key;
			public string Group;   // basic/secret/time/terrain/moon/boss/npc/weather
			public string Label;   // 中文名
			public string Type;    // text/number/float/bool/select
			public Dictionary<string, string> Options; // select 选项
			public bool Readonly;  // 只读（派生状态，仅展示）
			public bool Danger;    // 危险操作（前端需二次确认）
			public Func<object> Get;
			public Action<JToken> Set;
		}

		/// <summary>分组顺序与中文名（前端渲染用）</summary>
		private static readonly Dictionary<string, string> _groupNames = new Dictionary<string, string>
		{
			{ "basic", "世界基础" },
			{ "secret", "秘密世界种子" },
			{ "time", "时间·日晷" },
			{ "terrain", "地形深度" },
			{ "moon", "月亮" },
			{ "boss", "Boss 击败状态" },
			{ "npc", "NPC 解救状态" },
			{ "weather", "天气·事件·节日" }
		};

		private static readonly Dictionary<string, FieldDef> _fields = BuildFields();

		private static Dictionary<string, FieldDef> BuildFields()
		{
			var f = new Dictionary<string, FieldDef>(StringComparer.Ordinal);

			// ═══ 世界基础 ═══
			f["name"] = new FieldDef { Key = "name", Group = "basic", Label = "世界名称", Type = "text",
				Get = () => Main.worldName, Set = t => Main.worldName = JString(t) };
			f["mode"] = new FieldDef { Key = "mode", Group = "basic", Label = "难度模式", Type = "select",
				Options = new Dictionary<string, string> { { "1", "经典" }, { "2", "专家" }, { "3", "大师" }, { "4", "旅行" } },
				Get = () => Main.GameMode + 1, Set = t => Main.GameMode = Clamp(JInt(t, "mode"), 1, 4) - 1 };
			f["seed"] = new FieldDef { Key = "seed", Group = "basic", Label = "世界种子", Type = "text",
				Get = () => Main.ActiveWorldFileData.SeedText, Set = t => Main.ActiveWorldFileData.SetSeed(JString(t)) };
			f["worldId"] = new FieldDef { Key = "worldId", Group = "basic", Label = "世界 ID", Type = "number",
				Get = () => Main.ActiveWorldFileData.WorldId, Set = t => Main.ActiveWorldFileData.WorldId = JInt(t, "worldId") };
			f["uuid"] = new FieldDef { Key = "uuid", Group = "basic", Label = "世界 UUID", Type = "text",
				Get = () => Main.ActiveWorldFileData.UniqueId.ToString(),
				Set = t =>
				{
					var s = JString(t);
					if (!Guid.TryParse(s, out var g) || g == Guid.Empty)
						throw new FormatException("uuid 格式不正确，需要标准 GUID（如 8 位-4 位-4 位-4 位-12 位）");
					Main.ActiveWorldFileData.UniqueId = g;
				} };

			// ═══ 秘密世界种子 ═══
			AddBool(f, "secret", "drunkWorld", "05162020（醉酒世界）", () => Main.drunkWorld, v => Main.drunkWorld = v, danger: true);
			AddBool(f, "secret", "tenthAnniversaryWorld", "05162021（十周年庆典）", () => Main.tenthAnniversaryWorld, v => Main.tenthAnniversaryWorld = v, danger: true);
			AddBool(f, "secret", "getGoodWorld", "for the worthy", () => Main.getGoodWorld, v => Main.getGoodWorld = v, danger: true);
			AddBool(f, "secret", "notTheBeesWorld", "not the bees", () => Main.notTheBeesWorld, v => Main.notTheBeesWorld = v, danger: true);
			AddBool(f, "secret", "dontStarveWorld", "永恒领域（饥荒联动）", () => Main.dontStarveWorld, v => Main.dontStarveWorld = v, danger: true);
			AddBool(f, "secret", "remixWorld", "Remix（don't dig up）", () => Main.remixWorld, v => Main.remixWorld = v, danger: true);
			AddBool(f, "secret", "noTrapsWorld", "No Traps", () => Main.noTrapsWorld, v => Main.noTrapsWorld = v, danger: true);
			AddBool(f, "secret", "zenithWorld", "天顶（getfixedboi）", () => Main.zenithWorld, v => Main.zenithWorld = v, danger: true);
			AddBool(f, "secret", "skyblockWorld", "空岛", () => Main.skyblockWorld, v => Main.skyblockWorld = v, danger: true);
			AddBool(f, "secret", "vampireSeed", "吸血鬼", () => Main.vampireSeed, v => Main.vampireSeed = v, danger: true);
			AddBool(f, "secret", "infectedSeed", "感染世界", () => Main.infectedSeed, v => Main.infectedSeed = v, danger: true);
			AddBool(f, "secret", "teamBasedSpawnsSeed", "团队生成点", () => Main.teamBasedSpawnsSeed, v => Main.teamBasedSpawnsSeed = v, danger: true);
			AddBool(f, "secret", "dualDungeonsSeed", "双地牢", () => Main.dualDungeonsSeed, v => Main.dualDungeonsSeed = v, danger: true);

			// ═══ 时间·日晷 ═══
			AddBool(f, "time", "fastForwardTimeToDawn", "附魔日晷（快进到黎明）", () => Main.fastForwardTimeToDawn, v => Main.fastForwardTimeToDawn = v);
			f["sundialCooldown"] = new FieldDef { Key = "sundialCooldown", Group = "time", Label = "日晷冷却天数", Type = "number",
				Get = () => Main.sundialCooldown, Set = t => Main.sundialCooldown = Math.Max(0, JInt(t, "sundialCooldown")) };
			AddBool(f, "time", "fastForwardTimeToDusk", "附魔月晷（快进到黄昏）", () => Main.fastForwardTimeToDusk, v => Main.fastForwardTimeToDusk = v);
			f["moondialCooldown"] = new FieldDef { Key = "moondialCooldown", Group = "time", Label = "月晷冷却天数", Type = "number",
				Get = () => Main.moondialCooldown, Set = t => Main.moondialCooldown = Math.Max(0, JInt(t, "moondialCooldown")) };

			// ═══ 地形深度 ═══
			f["worldSurface"] = new FieldDef { Key = "worldSurface", Group = "terrain", Label = "地表深度", Type = "number",
				Get = () => Main.worldSurface, Set = t => Main.worldSurface = Math.Max(0, JInt(t, "worldSurface")) };
			f["rockLayer"] = new FieldDef { Key = "rockLayer", Group = "terrain", Label = "洞穴深度", Type = "number",
				Get = () => Main.rockLayer, Set = t => Main.rockLayer = Math.Max(0, JInt(t, "rockLayer")) };

			// ═══ 月亮 ═══
			f["moonPhase"] = new FieldDef { Key = "moonPhase", Group = "moon", Label = "月相", Type = "select",
				Options = new Dictionary<string, string>
				{
					{ "0", "满月" }, { "1", "亏凸月" }, { "2", "下弦月" }, { "3", "残月" },
					{ "4", "新月" }, { "5", "娥眉月" }, { "6", "上弦月" }, { "7", "盈凸月" }
				},
				Get = () => Main.moonPhase,
				Set = t => { Main.dayTime = false; Main.moonPhase = Clamp(JInt(t, "moonPhase"), 0, 7); Main.time = 0; } };
			f["moonType"] = new FieldDef { Key = "moonType", Group = "moon", Label = "月亮样式", Type = "select",
				Options = new Dictionary<string, string>
				{
					{ "0", "正常" }, { "1", "火星样式" }, { "2", "土星样式" }, { "3", "秘银风格" },
					{ "4", "明亮的偏蓝白色" }, { "5", "绿色" }, { "6", "糖果" }, { "7", "金星样式" }, { "8", "紫色的三重月亮" }
				},
				Get = () => Main.moonType,
				Set = t => { Main.dayTime = false; Main.moonType = Clamp(JInt(t, "moonType"), 0, 8); Main.time = 0; } };

			// ═══ Boss 击败状态 ═══
			AddBool(f, "boss", "downedSlimeKing", "史莱姆王", () => NPC.downedSlimeKing, v => NPC.downedSlimeKing = v);
			AddBool(f, "boss", "downedBoss1", "克苏鲁之眼", () => NPC.downedBoss1, v => NPC.downedBoss1 = v);
			AddBool(f, "boss", "downedBoss2", "世界吞噬怪 / 克苏鲁之脑", () => NPC.downedBoss2, v => NPC.downedBoss2 = v);
			AddBool(f, "boss", "downedDeerclops", "鹿角怪", () => NPC.downedDeerclops, v => NPC.downedDeerclops = v);
			AddBool(f, "boss", "downedBoss3", "骷髅王", () => NPC.downedBoss3, v => NPC.downedBoss3 = v);
			AddBool(f, "boss", "downedQueenBee", "蜂王", () => NPC.downedQueenBee, v => NPC.downedQueenBee = v);
			f["hardMode"] = new FieldDef { Key = "hardMode", Group = "boss", Label = "血肉墙（困难模式）", Type = "bool", Danger = true,
				Get = () => Main.hardMode,
				Set = t =>
				{
					if (JBool(t))
					{
						if (TShock.Config.Settings.DisableHardmode)
							throw new InvalidOperationException("TShock 配置已禁止困难模式（DisableHardmode=true），无法开启");
						WorldGen.StartHardmode();
					}
					else
					{
						Main.hardMode = false;
					}
				} };
			AddBool(f, "boss", "downedMechBoss1", "毁灭者", () => NPC.downedMechBoss1, v => NPC.downedMechBoss1 = v);
			AddBool(f, "boss", "downedMechBoss2", "双子魔眼", () => NPC.downedMechBoss2, v => NPC.downedMechBoss2 = v);
			AddBool(f, "boss", "downedMechBoss3", "机械骷髅王", () => NPC.downedMechBoss3, v => NPC.downedMechBoss3 = v);
			AddBool(f, "boss", "downedPlantBoss", "世纪之花", () => NPC.downedPlantBoss, v => NPC.downedPlantBoss = v);
			AddBool(f, "boss", "downedGolemBoss", "石巨人", () => NPC.downedGolemBoss, v => NPC.downedGolemBoss = v);
			AddBool(f, "boss", "downedQueenSlime", "史莱姆皇后", () => NPC.downedQueenSlime, v => NPC.downedQueenSlime = v);
			AddBool(f, "boss", "downedEmpressOfLight", "光之女皇", () => NPC.downedEmpressOfLight, v => NPC.downedEmpressOfLight = v);
			AddBool(f, "boss", "downedFishron", "猪龙鱼公爵", () => NPC.downedFishron, v => NPC.downedFishron = v);
			AddBool(f, "boss", "downedAncientCultist", "拜月教邪教徒", () => NPC.downedAncientCultist, v => NPC.downedAncientCultist = v);
			AddBool(f, "boss", "downedMoonlord", "月亮领主", () => NPC.downedMoonlord, v => NPC.downedMoonlord = v);
			AddBool(f, "boss", "downedGoblins", "哥布林军队", () => NPC.downedGoblins, v => NPC.downedGoblins = v);
			AddBool(f, "boss", "downedPirates", "海盗入侵", () => NPC.downedPirates, v => NPC.downedPirates = v);
			AddBool(f, "boss", "downedMartians", "火星暴乱", () => NPC.downedMartians, v => NPC.downedMartians = v);
			AddBool(f, "boss", "downedHalloweenTree", "哀木", () => NPC.downedHalloweenTree, v => NPC.downedHalloweenTree = v);
			AddBool(f, "boss", "downedHalloweenKing", "南瓜王", () => NPC.downedHalloweenKing, v => NPC.downedHalloweenKing = v);
			AddBool(f, "boss", "downedChristmasIceQueen", "冰雪女王", () => NPC.downedChristmasIceQueen, v => NPC.downedChristmasIceQueen = v);
			AddBool(f, "boss", "downedChristmasTree", "常绿尖叫怪", () => NPC.downedChristmasTree, v => NPC.downedChristmasTree = v);
			AddBool(f, "boss", "downedChristmasSantank", "圣诞坦克", () => NPC.downedChristmasSantank, v => NPC.downedChristmasSantank = v);
			AddBool(f, "boss", "downedTowerSolar", "日耀柱", () => NPC.downedTowerSolar, v => NPC.downedTowerSolar = v);
			AddBool(f, "boss", "downedTowerVortex", "星旋柱", () => NPC.downedTowerVortex, v => NPC.downedTowerVortex = v);
			AddBool(f, "boss", "downedTowerNebula", "星云柱", () => NPC.downedTowerNebula, v => NPC.downedTowerNebula = v);
			AddBool(f, "boss", "downedTowerStardust", "星尘柱", () => NPC.downedTowerStardust, v => NPC.downedTowerStardust = v);
			AddBool(f, "boss", "dd2DownedInvasionT1", "撒旦军队 T1", () => DD2Event.DownedInvasionT1, v => DD2Event.DownedInvasionT1 = v);
			AddBool(f, "boss", "dd2DownedInvasionT2", "撒旦军队 T2", () => DD2Event.DownedInvasionT2, v => DD2Event.DownedInvasionT2 = v);
			AddBool(f, "boss", "dd2DownedInvasionT3", "撒旦军队 T3（双足翼龙）", () => DD2Event.DownedInvasionT3, v => DD2Event.DownedInvasionT3 = v);

			// ═══ NPC 解救状态 ═══
			AddBool(f, "npc", "savedAngler", "渔夫", () => NPC.savedAngler, v => NPC.savedAngler = v);
			AddBool(f, "npc", "savedGoblin", "哥布林工匠", () => NPC.savedGoblin, v => NPC.savedGoblin = v);
			AddBool(f, "npc", "savedMech", "机械师", () => NPC.savedMech, v => NPC.savedMech = v);
			AddBool(f, "npc", "savedStylist", "发型师", () => NPC.savedStylist, v => NPC.savedStylist = v);
			AddBool(f, "npc", "savedBartender", "酒馆老板", () => NPC.savedBartender, v => NPC.savedBartender = v);
			AddBool(f, "npc", "savedGolfer", "高尔夫球手", () => NPC.savedGolfer, v => NPC.savedGolfer = v);
			AddBool(f, "npc", "savedWizard", "巫师", () => NPC.savedWizard, v => NPC.savedWizard = v);
			AddBool(f, "npc", "savedTaxCollector", "税收官", () => NPC.savedTaxCollector, v => NPC.savedTaxCollector = v);
			AddBool(f, "npc", "boughtCat", "猫咪许可证", () => NPC.boughtCat, v => NPC.boughtCat = v);
			AddBool(f, "npc", "boughtDog", "狗狗许可证", () => NPC.boughtDog, v => NPC.boughtDog = v);
			AddBool(f, "npc", "boughtBunny", "兔兔许可证", () => NPC.boughtBunny, v => NPC.boughtBunny = v);
			AddBool(f, "npc", "unlockedSlimeBlueSpawn", "呆瓜史莱姆", () => NPC.unlockedSlimeBlueSpawn, v => NPC.unlockedSlimeBlueSpawn = v);
			AddBool(f, "npc", "unlockedSlimeGreenSpawn", "冷酷史莱姆", () => NPC.unlockedSlimeGreenSpawn, v => NPC.unlockedSlimeGreenSpawn = v);
			AddBool(f, "npc", "unlockedSlimeOldSpawn", "年长史莱姆", () => NPC.unlockedSlimeOldSpawn, v => NPC.unlockedSlimeOldSpawn = v);
			AddBool(f, "npc", "unlockedSlimePurpleSpawn", "笨拙史莱姆", () => NPC.unlockedSlimePurpleSpawn, v => NPC.unlockedSlimePurpleSpawn = v);
			AddBool(f, "npc", "unlockedSlimeRainbowSpawn", "唱将史莱姆", () => NPC.unlockedSlimeRainbowSpawn, v => NPC.unlockedSlimeRainbowSpawn = v);
			AddBool(f, "npc", "unlockedSlimeRedSpawn", "粗暴史莱姆", () => NPC.unlockedSlimeRedSpawn, v => NPC.unlockedSlimeRedSpawn = v);
			AddBool(f, "npc", "unlockedSlimeYellowSpawn", "神秘史莱姆", () => NPC.unlockedSlimeYellowSpawn, v => NPC.unlockedSlimeYellowSpawn = v);
			AddBool(f, "npc", "unlockedSlimeCopperSpawn", "侍卫史莱姆", () => NPC.unlockedSlimeCopperSpawn, v => NPC.unlockedSlimeCopperSpawn = v);

			// ═══ 天气·事件·节日 ═══
			f["bloodMoon"] = new FieldDef { Key = "bloodMoon", Group = "weather", Label = "血月", Type = "bool", Danger = true,
				Get = () => Main.bloodMoon, Set = t => TSPlayer.Server.SetBloodMoon(JBool(t)) };
			f["eclipse"] = new FieldDef { Key = "eclipse", Group = "weather", Label = "日食", Type = "bool", Danger = true,
				Get = () => Main.eclipse, Set = t => TSPlayer.Server.SetEclipse(JBool(t)) };
			f["rainIntensity"] = new FieldDef { Key = "rainIntensity", Group = "weather", Label = "雨量强度（0=停雨，0.3 小雨，0.6 中雨，1.0 雷暴）", Type = "float",
				Get = () => Main.raining ? Main.maxRaining : 0f,
				Set = t =>
				{
					float v = Clamp(JFloat(t, "rainIntensity"), 0f, 1f);
					if (v <= 0f) { Main.StopRain(); }
					else { Main.StartRain(); Main.raining = true; Main.maxRaining = v; }
				} };
			f["windSpeed"] = new FieldDef { Key = "windSpeed", Group = "weather", Label = "风速（-1 西风 ~ 1 东风，|值|≥0.35 大风天）", Type = "float",
				Get = () => Main.windSpeedCurrent,
				Set = t => { float v = Clamp(JFloat(t, "windSpeed"), -1f, 1f); Main.windSpeedTarget = v; Main.windSpeedCurrent = v; } };
			f["numClouds"] = new FieldDef { Key = "numClouds", Group = "weather", Label = "云量（0~200）", Type = "number",
				Get = () => Main.numClouds, Set = t => Main.numClouds = Clamp(JInt(t, "numClouds"), 0, 200) };
			f["sandstorm"] = new FieldDef { Key = "sandstorm", Group = "weather", Label = "沙尘暴", Type = "bool", Danger = true,
				Get = () => Sandstorm.Happening,
				Set = t => { if (JBool(t)) Sandstorm.StartSandstorm(); else Sandstorm.StopSandstorm(); } };
			f["slimeRain"] = new FieldDef { Key = "slimeRain", Group = "weather", Label = "史莱姆雨", Type = "bool",
				Get = () => Main.slimeRain, Set = t => { if (JBool(t)) Main.slimeRain = true; else Main.StopSlimeRain(); } };
			f["spawnMeteor"] = new FieldDef { Key = "spawnMeteor", Group = "weather", Label = "陨石（开启后触发陨石坠落）", Type = "bool", Danger = true,
				Get = () => WorldGen.spawnMeteor, Set = t => WorldGen.spawnMeteor = JBool(t) };
			AddBool(f, "weather", "xMas", "圣诞节", () => Main.xMas, v => Main.xMas = v);
			AddBool(f, "weather", "halloween", "万圣节", () => Main.halloween, v => Main.halloween = v);

			// ═══ 只读派生状态（供前端展示，不参与 apply）═══
			f["isStorming"] = new FieldDef { Key = "isStorming", Group = "weather", Label = "雷暴中（派生）", Type = "bool", Readonly = true,
				Get = () => Main.IsItStorming, Set = null };
			f["isHappyWindyDay"] = new FieldDef { Key = "isHappyWindyDay", Group = "weather", Label = "大风天（派生）", Type = "bool", Readonly = true,
				Get = () => Main.IsItAHappyWindyDay, Set = null };
			f["moonPhaseName"] = new FieldDef { Key = "moonPhaseName", Group = "moon", Label = "月相（中文）", Type = "text", Readonly = true,
				Get = () => _moonPhaseNames[Main.moonPhase], Set = null };
			f["moonTypeName"] = new FieldDef { Key = "moonTypeName", Group = "moon", Label = "月亮样式（中文）", Type = "text", Readonly = true,
				Get = () => _moonTypeNames[Main.moonType], Set = null };

			return f;
		}

		private static readonly string[] _moonPhaseNames = { "满月", "亏凸月", "下弦月", "残月", "新月", "娥眉月", "上弦月", "盈凸月" };
		private static readonly string[] _moonTypeNames = { "正常", "火星样式", "土星样式", "秘银风格", "明亮的偏蓝白色", "绿色", "糖果", "金星样式", "紫色的三重月亮" };

		private static void AddBool(Dictionary<string, FieldDef> f, string group, string key, string label,
			Func<bool> get, Action<bool> set, bool danger = false)
		{
			f[key] = new FieldDef { Key = key, Group = group, Label = label, Type = "bool", Danger = danger,
				Get = () => get(), Set = t => set(JBool(t)) };
		}

		// ═══════════════ 类型解析辅助 ═══════════════

		private static bool JBool(JToken t)
		{
			if (t == null || t.Type != JTokenType.Boolean)
				throw new FormatException($"字段值类型错误：期望布尔值，收到 {t?.Type.ToString() ?? "null"}");
			return (bool)t;
		}

		private static int JInt(JToken t, string key)
		{
			if (t == null || t.Type != JTokenType.Integer)
				throw new FormatException($"字段 {key} 值类型错误：期望整数，收到 {t?.Type.ToString() ?? "null"}");
			return (int)t;
		}

		private static float JFloat(JToken t, string key)
		{
			if (t == null || (t.Type != JTokenType.Float && t.Type != JTokenType.Integer))
				throw new FormatException($"字段 {key} 值类型错误：期望数字，收到 {t?.Type.ToString() ?? "null"}");
			return (float)t;
		}

		private static string JString(JToken t)
		{
			if (t == null || t.Type != JTokenType.String)
				throw new FormatException($"字段值类型错误：期望字符串，收到 {t?.Type.ToString() ?? "null"}");
			return (string)t;
		}

		private static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
		private static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);

		// ═══════════════ REST API ═══════════════

		/// <summary>
		/// GET /data/worldmodify/status（权限 worldinfo）
		/// 返回全部字段当前值 + 字段元数据（分组/中文名/类型/选项），前端数据驱动渲染。
		/// </summary>
		public static object GetStatusRest(RestRequestArgs args)
		{
			try
			{
				var values = new Dictionary<string, object>();
				var meta = new Dictionary<string, object>();
				foreach (var kv in _fields)
				{
					try { values[kv.Key] = kv.Value.Get(); }
					catch (Exception ex) { values[kv.Key] = null; TShock.Log.ConsoleError($"[TSWeb] WorldModify 读取字段 {kv.Key} 失败: {ex.Message}"); }

					var m = new Dictionary<string, object>
					{
						{ "group", kv.Value.Group },
						{ "label", kv.Value.Label },
						{ "type", kv.Value.Type },
						{ "readonly", kv.Value.Readonly },
						{ "danger", kv.Value.Danger }
					};
					if (kv.Value.Options != null)
						m["options"] = kv.Value.Options;
					meta[kv.Key] = m;
				}

				return new RestObject
				{
					{ "fields", values },
					{ "meta", meta },
					{ "groups", _groupNames.Select(g => new Dictionary<string, string> { { "id", g.Key }, { "label", g.Value } }).ToList() }
				};
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[TSWeb] WorldModify 读取状态失败: {ex.Message}");
				return new RestObject("500") { { "error", ex.Message } };
			}
		}

		/// <summary>
		/// GET /data/worldmodify/apply?fields={"name":"x","moonPhase":2}（权限 worldmodify）
		/// 逐项校验并应用，全部成功后广播一次 WorldInfo 包（客户端即时生效）。
		/// </summary>
		public static object ApplyRest(RestRequestArgs args)
		{
			try
			{
				string fieldsJson = null;
				try { fieldsJson = args.Parameters["fields"]; } catch { }
				if (string.IsNullOrEmpty(fieldsJson))
					return new RestObject("400") { { "error", "缺少 fields 参数（JSON 对象：{ \"字段名\": 值 }）" } };

				JObject parsed;
				try { parsed = JObject.Parse(fieldsJson); }
				catch (Exception ex) { return new RestObject("400") { { "error", $"fields 参数不是合法 JSON: {ex.Message}" } }; }

				var results = new Dictionary<string, string>();
				int applied = 0;
				var appliedKeys = new List<string>();

				foreach (var prop in parsed.Properties())
				{
					if (!_fields.TryGetValue(prop.Name, out var def))
					{
						results[prop.Name] = "error: 未知字段";
						continue;
					}
					if (def.Set == null)
					{
						results[prop.Name] = "error: 只读字段不可修改";
						continue;
					}
					try
					{
						def.Set(prop.Value);
						results[prop.Name] = "ok";
						applied++;
						appliedKeys.Add(prop.Name);
					}
					catch (Exception ex)
					{
						results[prop.Name] = $"error: {ex.Message}";
					}
				}

				if (applied > 0)
				{
					// 一次广播 WorldInfo 包，让所有玩家客户端即时生效
					TSPlayer.All.SendData(PacketTypes.WorldInfo);
					TShock.Log.ConsoleInfo($"[TSWeb] WorldModify 已应用 {applied} 个字段: {string.Join(", ", appliedKeys)}");
				}

				return new RestObject
				{
					{ "results", results },
					{ "applied", applied }
				};
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[TSWeb] WorldModify 应用失败: {ex.Message}");
				return new RestObject("500") { { "error", ex.Message } };
			}
		}
	}
}
