using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;

namespace Patch1458
{
	/// <summary>
	/// 1458临时补丁 —— TShock Bouncer NPC buff 白名单的 1.4.5.8 内容兼容补丁。
	///
	/// ════════════════════════════════════════════════════════════════
	/// 背景（根因分析结论，全部经 1.4.5.8 服务器反编译实证）：
	///   • TShock Bouncer.OnNPCAddBuff（Bouncer.cs:2167）对客户端 53 号包
	///     （AddNPCBuff：npcId/buffType/time 三字段）做静态白名单校验：
	///     buffType ∉ NPCAddBuffTimeMax → Kick("Added buff to ... NPC abnormally.")。
	///   • 该白名单（Bouncer.cs:3228，private static Dictionary<int, short>，33 项）
	///     按 1.4.4 / 早期 1.4.5 原版数值抄死，1.4.5.8 新增的 5 个 NPC debuff
	///     全部缺席 → 使用 1458 新内容（催化手环等）的合法玩家被误踢。
	///   • 典型案例：1.4.5.8 新饰品"催化手环"（Catalyst Band）置位
	///     Player.catalystBand 后，任意玩家弹幕命中敌怪即
	///     Projectile.StatusNPC → NPC.AddBuff(398, 300)（非静默 → 客户端必发 53 包），
	///     服务器白名单无 398 → 误踢（"给 NPC：敌怪 异常添加了buff"）。
	/// ════════════════════════════════════════════════════════════════
	/// 补丁方式：
	///   反射取出 Bouncer.NPCAddBuffTimeMax（private static），注入下表 5 项
	///   （时长上限 = 1.4.5.8 原版代码全部调用点的最大值，与 TShock 收录惯例一致）。
	///   不改任何包处理流程，Bouncer 其余防护（超时踢/townNPC/禁用检查）原样保留。
	///   注入发生在服务器启动阶段（任何玩家加入前），线程安全（此时无并发判定）。
	///
	/// 局限（已知不覆盖场景）：
	///   • townNPC（城镇 NPC）debuff 白名单是 Bouncer 内硬编码的 16 个 BuffID
	///     常量 if 链（非集合），无法以字典注入方式扩展——催化手环等对
	///     城镇 NPC 施加新 debuff 仍会被踢。玩家主动攻击城镇 NPC 属边缘场景，
	///     如确需放行须改 TShock 源码或 Harmony patch（本插件刻意不做）。
	/// ════════════════════════════════════════════════════════════════
	/// 管理：/patch1458 查看注入状态（权限 patch1458.use，自动授予 admin 组）。
	/// Dispose 时仅移除本插件注入的条目（官方已有条目一律不动），可安全热重载。
	/// </summary>
	[ApiVersion(2, 1)]
	public class Patch1458Plugin : TerrariaPlugin
	{
		public override string Author => "lmx12330";
		public override string Name => "1458临时补丁";
		public override string Description =>
			"1.4.5.8 内容兼容补丁：向 TShock Bouncer NPC buff 白名单注入 1.4.5.8 新增 debuff（催化手环/强酸/叶绿孢子/蓝红闪电），修复新内容合法玩家被误踢";
		public override Version Version => new Version(1, 0, 0, 0);

		/// <summary>查询注入状态命令权限</summary>
		public const string Permission = "patch1458.use";

		/// <summary>
		/// 1.4.5.8 新增 NPC debuff → 原版最大时长（tick）。
		/// 数值出处：1.4.5.8 TerrariaServer.exe 反编译 Projectile.StatusNPC 全部调用点：
		///   • 395 PotentAcids（强酸）：诅咒涂层 60（t8_Projectile.cs:11203）、
		///     弹幕 282/283 120（:11684-11688）→ 上限 120
		///   • 397 ChlorophyteSpore（叶绿孢子）：弹幕 1127 固定 300（:11729-11733）
		///   • 398 AcceleratePoisons（催化毒液）：催化手环 catalystBand 固定 300（:11191-11196，
		///     唯一调用点）
		///   • 399 BlueLightning（蓝闪电）：弹幕 1117 LightningStrikeShot
		///     60*rand(4,8) → 240..420（:11230-11234）→ 上限 420
		///   • 400 RedLightning（红闪电）：弹幕 1122 ArcSurge
		///     60*rand(4,8) → 240..420（:11235-11239）→ 上限 420
		/// </summary>
		private static readonly Dictionary<int, short> AddedBuffs = new()
		{
			{ 395, 120 },
			{ 397, 300 },
			{ 398, 300 },
			{ 399, 420 },
			{ 400, 420 },
		};

		/// <summary>buff id → 名称（仅用于日志/命令展示）</summary>
		private static readonly Dictionary<int, string> BuffNames = new()
		{
			{ 395, "PotentAcids(强酸)" },
			{ 397, "ChlorophyteSpore(叶绿孢子)" },
			{ 398, "AcceleratePoisons(催化毒液/催化手环)" },
			{ 399, "BlueLightning(蓝闪电)" },
			{ 400, "RedLightning(红闪电)" },
		};

		// ═══ 反射缓存（运行时绑定服务器实际加载的 TShockAPI.dll）═══
		// TShock 源码：internal class Bouncer（不能直接 typeof），经同程序集反射定位；
		// private static Dictionary<int, short> NPCAddBuffTimeMax（Bouncer.cs:3228）
		private static readonly FieldInfo? _f_NPCAddBuffTimeMax;

		static Patch1458Plugin()
		{
			// Bouncer 是 internal class → 用 public 的 GetDataHandlers 拿到 TShockAPI 程序集再按名定位
			Type? bouncerType = typeof(TShockAPI.GetDataHandlers).Assembly.GetType("TShockAPI.Bouncer");
			_f_NPCAddBuffTimeMax = bouncerType?.GetField("NPCAddBuffTimeMax",
				BindingFlags.Static | BindingFlags.NonPublic);
		}

		private static Patch1458Plugin? _instance;
		/// <summary>本次由本插件注入（原白名单不存在）的 buff id，Dispose 时按此精确回滚</summary>
		private readonly List<int> _injectedByMe = new();

		public Patch1458Plugin(Main game) : base(game) { }

		public override void Initialize()
		{
			if (_instance != null)
				return;
			_instance = this;

			if (_f_NPCAddBuffTimeMax == null)
			{
				TShock.Log.ConsoleError("[1458临时补丁] 反射绑定失败：当前 TShockAPI.Bouncer 不存在字段 NPCAddBuffTimeMax（TShock 版本不匹配？），补丁未生效，1.4.5.8 新内容仍会误踢");
				return;
			}
			if (_f_NPCAddBuffTimeMax.GetValue(null) is not Dictionary<int, short> dict)
			{
				TShock.Log.ConsoleError("[1458临时补丁] NPCAddBuffTimeMax 实际类型不是 Dictionary<int, short>（TShock 版本不匹配？），补丁未生效");
				return;
			}

			foreach (KeyValuePair<int, short> kv in AddedBuffs)
			{
				if (dict.ContainsKey(kv.Key))
				{
					// 官方白名单已有该条目（未来 TShock 更新收录后）→ 不覆盖不动，Dispose 也不回滚
					TShock.Log.ConsoleInfo($"[1458临时补丁] buff {kv.Key}({BuffNames[kv.Key]}) 官方白名单已收录（上限 {dict[kv.Key]}），跳过注入");
					continue;
				}
				dict[kv.Key] = kv.Value;
				_injectedByMe.Add(kv.Key);
			}

			if (_injectedByMe.Count > 0)
			{
				string detail = string.Join(", ", _injectedByMe.Select(id => $"{id}={dict[id]}({BuffNames[id]})"));
				TShock.Log.ConsoleInfo($"[1458临时补丁] 已向 Bouncer.NPCAddBuffTimeMax 注入 {_injectedByMe.Count} 项：{detail}");
			}
			else
			{
				TShock.Log.ConsoleInfo("[1458临时补丁] 无需注入（官方白名单已覆盖全部 1.4.5.8 新增 debuff），本补丁保持待机");
			}

			// 权限 + 查询命令
			try
			{
				TShock.Groups.GetGroupByName("admin")?.AddPermission(Permission);
			}
			catch { }
			Commands.ChatCommands.Add(new Command(Permission, StatusCommand, "patch1458")
			{
				HelpText = "查看 1458临时补丁 对 Bouncer NPC buff 白名单的注入状态"
			});

			TShock.Log.ConsoleInfo("[1458临时补丁] 已启用（/patch1458 查看状态）");
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing && _instance != null)
			{
				_instance = null;
				Commands.ChatCommands.RemoveAll(c => c.Names.Any(n =>
					n.Equals("patch1458", StringComparison.OrdinalIgnoreCase)));

				// 仅回滚本插件注入的条目（官方已有条目不动）
				if (_injectedByMe.Count > 0 && _f_NPCAddBuffTimeMax?.GetValue(null) is Dictionary<int, short> dict)
				{
					foreach (int id in _injectedByMe)
						dict.Remove(id);
					TShock.Log.ConsoleInfo($"[1458临时补丁] 已回滚注入的 {_injectedByMe.Count} 项白名单条目");
				}
				_injectedByMe.Clear();
			}
			base.Dispose(disposing);
		}

		// ════════════════════════════════════════════════
		//  /patch1458：查看注入状态
		// ════════════════════════════════════════════════

		private void StatusCommand(CommandArgs args)
		{
			var player = args.Player;
			if (_f_NPCAddBuffTimeMax == null)
			{
				player?.SendErrorMessage("[1458临时补丁] 反射绑定失败，补丁未生效（查看服务器日志）");
				return;
			}
			if (_f_NPCAddBuffTimeMax.GetValue(null) is not Dictionary<int, short> dict)
			{
				player?.SendErrorMessage("[1458临时补丁] 白名单字段类型异常，补丁未生效");
				return;
			}

			player?.SendInfoMessage("[1458临时补丁] 1.4.5.8 新增 NPC debuff 白名单状态：");
			foreach (KeyValuePair<int, short> kv in AddedBuffs)
			{
				bool present = dict.TryGetValue(kv.Key, out short limit);
				bool injected = _injectedByMe.Contains(kv.Key);
				string state = !present ? "[c/FF6060:缺失 → 未生效]"
					: injected ? $"[c/60FF60:已注入] 上限 {limit}"
					: $"[c/FFFF60:官方已收录] 上限 {limit}（未由本插件注入）";
				player?.SendInfoMessage($"  buff {kv.Key} {BuffNames[kv.Key]} → {state}");
			}
		}
	}
}
