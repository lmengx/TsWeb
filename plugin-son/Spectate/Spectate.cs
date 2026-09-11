using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;

namespace Spectate
{
	/// <summary>
	/// Spectate —— 直播插件：/slive 进入直播模式，发放占卜球(5644)，
	/// 直播者点击后客户端原生进入观战（相机每帧跟随目标实体，平滑不卡顿）；
	/// 服务端接管后自动切换活跃玩家（排除挂机）。
	///
	/// 精简版（v1.2）：只保留 占卜球原生观战 + 挂机自动切换 + 开关提示。
	/// 已删除原版的隐身/上行拦截/伪装推送等复杂机制：
	///   - 上行拦截（OTAPI GetData + MonoMod detour 双通道）：全局每个上行包都被 detour
	///     包装，满员服务器累积开销 → 卡顿；
	///   - BlockedOpPackets 无条件丢弃直播者上行 ProjectileNew(27)/ProjectileDestroy(29)
	///     等射弹包 → 射弹丢失；
	///   - SendData detour 隐身防恢复：委托签名(object text)与真实签名(NetworkText text)
	///     不匹配，detour 实际挂载失败，代码从未生效。
	/// 直播者观战期间角色不再隐身（可见、留在原地），退出直播恢复。
	/// </summary>
	[ApiVersion(2, 1)]
	public class SpectatePlugin : TerrariaPlugin
	{
		public override string Author => "lmx12330";
		public override string Description => "直播：占卜球原生观战 + 自动切换活跃玩家";
		public override string Name => "Spectate";
		public override Version Version => new(1, 2, 0, 0);

		/// <summary>权限节点（Initialize 时自动授予 admin 组）</summary>
		public const string Permission = "spectate.use";

		/// <summary>默认挂机切换阈值（秒）</summary>
		private const double DefaultIdleSeconds = 10.0;

		/// <summary>占卜球物品 ID（客户端点击后进入观战）</summary>
		private const short ScryingOrbItem = 5644;

		// ═══ 直播状态（全局单实例）═══
		private static int _viewer = -1;          // 直播者 whoAmI
		private static int _viewTarget = -1;      // 当前直播目标 whoAmI
		private static bool _spectateActivated;   // 客户端是否已真正进入观战（spectating>=0）
		private static bool _awaitingEntry;       // 已发放占卜球，等待直播者点击进入
		private static double _liveIdleSeconds = DefaultIdleSeconds;
		private static int _targetLockUntil;      // 目标锁定窗口（接管/切换后 1 秒内不覆盖 _viewTarget）
		private static int _replacedHotbarSlot = -1;   // 快捷栏被顶替的槽位（占卜球临时占位）
		private static Item? _replacedHotbarItem;      // 被顶替的原物品

		// ═══ 活跃度统计（直播"挂机"判定）═══
		private static readonly Dictionary<int, DateTime> _lastActive = new();
		private static readonly Dictionary<int, Vector2> _lastPos = new();
		private static readonly object _sync = new();

		// ═══ 生命周期 ═══
		private static SpectatePlugin? _instance;
		private static int _tick;

		public SpectatePlugin(Main game) : base(game) { }

		public override void Initialize()
		{
			if (_instance != null)
				return;
			_instance = this;

			// 帧驱动：观战激活接管 / 直播自动切换
			ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);
			ServerApi.Hooks.ServerLeave.Register(this, OnServerLeave);

			// TShock PlayerUpdate 事件：活跃度统计（直播"挂机"判定）
			GetDataHandlers.PlayerUpdate += OnPlayerUpdateEvent;

			// 权限 + 命令
			try { TShock.Groups.GetGroupByName("admin")?.AddPermission(Permission); }
			catch { }

			Commands.ChatCommands.Add(new Command(Permission, LiveCommand, "slive", "直播"));
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing && _instance != null)
			{
				_instance = null;
				StopViewing();

				GetDataHandlers.PlayerUpdate -= OnPlayerUpdateEvent;
				ServerApi.Hooks.GameUpdate.Deregister(this, OnGameUpdate);
				ServerApi.Hooks.ServerLeave.Deregister(this, OnServerLeave);

				Commands.ChatCommands.RemoveAll(c => c.Names.Any(n =>
					n.Equals("slive", StringComparison.OrdinalIgnoreCase) ||
					n.Equals("直播", StringComparison.OrdinalIgnoreCase)));

				lock (_sync)
				{
					_lastActive.Clear();
					_lastPos.Clear();
				}
			}
			base.Dispose(disposing);
		}

		// ════════════════════════════════════════════════
		//  原生观战通道：SpectatePlayer(150) 包
		// ════════════════════════════════════════════════

		/// <summary>发 SpectatePlayer(150)：直播者 index + 目标 index（short）。
		/// 客户端仅在直播者自己 spectating>=0 时接受（已进入观战），目标=-1 退出观战。</summary>
		private static void SendSpectate(int viewer, int target)
		{
			try
			{
				NetMessage.SendData(150, viewer, -1, null, viewer, target);
			}
			catch { }
		}

		// ════════════════════════════════════════════════
		//  状态切换
		// ════════════════════════════════════════════════

		/// <summary>
		/// 准备直播：发放真实占卜球，等待直播者点击进入观战（客户端原生进入，可靠）。
		/// 进入后 OnGameUpdate 检测 spectating 激活 → 接管（GodMode + 150 切目标）。
		/// </summary>
		private static void PrepareViewing(int adminWho, int targetWho)
		{
			StopViewing();

			var adminTs = GetPlayer(adminWho);
			if (adminTs == null)
				return;

			_viewer = adminWho;
			_viewTarget = targetWho;
			_spectateActivated = false;
			_awaitingEntry = true;

			GiveScryingOrb(adminTs);
		}

		/// <summary>给直播者发放真实占卜球。
		/// 必须放快捷栏（槽 50-58）——客户端 ItemCheck 只处理 selectedItem（快捷栏），
		/// 背包里的物品无法被"使用"触发观战。若快捷栏无空位则顶替第 9 格（58），退出时恢复。</summary>
		private static void GiveScryingOrb(TSPlayer player)
		{
			var tplr = player.TPlayer;
			if (tplr == null)
				return;

			// 优先快捷栏空槽（50-58）
			for (int i = 50; i < 59; i++)
			{
				var slot = tplr.inventory[i];
				if (slot == null || slot.type == 0 || slot.stack <= 0)
				{
					var orb = new Item();
					orb.SetDefaults(ScryingOrbItem);
					orb.stack = 1;
					tplr.inventory[i] = orb;
					player.SendData(PacketTypes.PlayerSlot, "", i);
					return;
				}
			}

			// 快捷栏满：顶替第 9 格（58），记录原物品退出时恢复
			var replaced = tplr.inventory[58];
			_replacedHotbarSlot = 58;
			_replacedHotbarItem = replaced != null && replaced.type > 0 ? replaced.Clone() : null;
			var orb2 = new Item();
			orb2.SetDefaults(ScryingOrbItem);
			orb2.stack = 1;
			tplr.inventory[58] = orb2;
			player.SendData(PacketTypes.PlayerSlot, "", 58);
		}

		/// <summary>切换直播目标（150 切换）</summary>
		private static void SwitchTarget(int newTarget)
		{
			if (_viewer < 0)
				return;
			_viewTarget = newTarget;
			_targetLockUntil = _tick + 60;   // 1 秒锁定期：等客户端稳定到新目标
			SendSpectate(_viewer, newTarget);
		}

		/// <summary>退出直播：150(-1) 退出原生观战 + 恢复 GodMode/快捷栏</summary>
		private static void StopViewing()
		{
			if (_viewer < 0)
				return;
			int admin = _viewer;
			_viewer = -1;
			_viewTarget = -1;
			_spectateActivated = false;
			_awaitingEntry = false;

			var adminTs = GetPlayer(admin);

			// 1) 退出原生观战（客户端已进入观战时接受 150(-1)）
			if (adminTs != null && adminTs.ConnectionAlive
				&& Main.player[admin] != null && Main.player[admin].spectating >= 0)
			{
				try { SendSpectate(admin, -1); }
				catch { }
			}

			// 2) 恢复 GodMode
			if (adminTs != null)
				adminTs.GodMode = false;

			// 3) 恢复被占卜球顶替的快捷栏物品
			if (adminTs != null && adminTs.TPlayer != null && _replacedHotbarSlot >= 0)
			{
				try
				{
					adminTs.TPlayer.inventory[_replacedHotbarSlot] = _replacedHotbarItem ?? new Item();
					adminTs.SendData(PacketTypes.PlayerSlot, "", _replacedHotbarSlot);
				}
				catch { }
			}
			_replacedHotbarSlot = -1;
			_replacedHotbarItem = null;
		}

		// ════════════════════════════════════════════════
		//  帧驱动：观战激活接管 / 150 保持 / 直播自动切换（排除挂机）
		// ════════════════════════════════════════════════

		private static void OnGameUpdate(EventArgs args)
		{
			_tick++;
			if (_viewer < 0)
				return;

			var viewer = GetPlayer(_viewer);

			// 1) 直播者断开连接 → 结束
			if (viewer == null || !viewer.ConnectionAlive)
			{
				StopViewing();
				return;
			}

			// 2) 客户端观战状态检测
			bool spectatingActive = false;
			try
			{
				spectatingActive = Main.player[_viewer] != null && Main.player[_viewer].spectating >= 0;
			}
			catch { }

			if (spectatingActive && !_spectateActivated)
			{
				// 刚进入观战：接管（GodMode + 150 切目标）
				_spectateActivated = true;
				_awaitingEntry = false;
				viewer.GodMode = true;
				int initTarget = ResolveTarget(_viewTarget);
				if (initTarget >= 0)
				{
					_viewTarget = initTarget;
					_targetLockUntil = _tick + 60;   // 1 秒锁定期：等客户端稳定到服务端目标
					SendSpectate(_viewer, initTarget);
				}
				return;
			}

			if (!spectatingActive && _spectateActivated)
			{
				// 客户端退出了观战视角（按 Esc/任意键/滚轮等）。
				// 保持会话（隐身不恢复），回到待进入，可再点占卜球恢复。
				_spectateActivated = false;
				viewer.GodMode = false;
				_awaitingEntry = true;
				return;
			}

			// 3) 等待进入：保持会话（等直播者点击占卜球）
			if (!spectatingActive && _awaitingEntry)
				return;

			if (!spectatingActive)
				return;

			// 已激活：保活（spectating 时客户端停止上报，防超时踢出）
			if (viewer.ConnectionAlive)
				KeepAlive(_viewer);

			// 直播者左右键手动切换目标 → 同步服务端记录（切换/接管后 1 秒锁定期内不覆盖，
			// 避免把服务端刚切的目标误判成"手动切换"）
			if (_tick > _targetLockUntil
				&& Main.player[_viewer] != null && Main.player[_viewer].spectating >= 0
				&& Main.player[_viewer].spectating != _viewTarget)
			{
				_viewTarget = Main.player[_viewer].spectating;
			}

			// 4) 目标死亡/下线 → 自动切换下一位。绝不自动结束。
			var target = GetPlayer(_viewTarget);
			bool targetGone = target == null || !target.Active || target.TPlayer == null
				|| !target.TPlayer.active || target.TPlayer.dead;
			if (targetGone)
			{
				int next = FindNextLiveTarget(_viewTarget);
				if (next < 0)
				{
					// 无存活玩家：保持当前状态等待（玩家复活/上线后自动切换）
					return;
				}
				SwitchTarget(next);
			}

			// 5) 直播：每秒检查挂机 → 自动切换（排除挂机）
			if (_tick % 60 != 0)
				return;

			bool targetIdle = false;
			if (_viewTarget >= 0)
			{
				lock (_sync)
				{
					targetIdle = !_lastActive.TryGetValue(_viewTarget, out var t)
						|| (DateTime.UtcNow - t).TotalSeconds >= _liveIdleSeconds;
				}
			}
			if (targetIdle)
			{
				int next = FindNextLiveTarget(_viewTarget);
				if (next < 0)
					return;
				SwitchTarget(next);
			}
		}

		/// <summary>解析实际直播目标：指定目标无效时退回第一个存活/活跃玩家（直播优先活跃）</summary>
		private static int ResolveTarget(int specified)
		{
			if (IsAliveTarget(specified) && specified != _viewer)
				return specified;
			return FindNextLiveTarget(-1);
		}

		private static bool IsAliveTarget(int who)
		{
			var ts = GetPlayer(who);
			if (ts == null || !ts.Active || ts.TPlayer == null)
				return false;
			var p = Main.player[who];
			return p != null && p.active && !p.dead;
		}

		/// <summary>找下一位存活玩家（whoAmI 递增循环，跳过自己）</summary>
		private static int FindNextTarget(int current)
		{
			int start = current >= 0 ? current : 0;
			for (int i = 1; i < Main.player.Length; i++)
			{
				int idx = (start + i) % Main.player.Length;
				if (IsAliveTarget(idx) && idx != _viewer)
					return idx;
			}
			return -1;
		}

		/// <summary>直播找下一位活跃玩家（存活 + 未挂机）</summary>
		private static int FindNextLiveTarget(int current)
		{
			int start = current >= 0 ? current : 0;
			for (int i = 1; i < Main.player.Length; i++)
			{
				int idx = (start + i) % Main.player.Length;
				if (!IsAliveTarget(idx) || idx == _viewer)
					continue;
				lock (_sync)
				{
					if (_lastActive.TryGetValue(idx, out var t)
						&& (DateTime.UtcNow - t).TotalSeconds < _liveIdleSeconds)
						return idx;
				}
			}
			return -1;
		}

		private static void OnServerLeave(LeaveEventArgs args)
		{
			int who = args.Who;
			if (who == _viewer)
			{
				// 直播者本人下线：直接退出
				StopViewing();
			}
			// 目标下线：不立即退出（直播是持续状态），交给 OnGameUpdate 自动切换下一位存活玩家
			lock (_sync)
			{
				_lastActive.Remove(who);
				_lastPos.Remove(who);
			}
		}

		// ════════════════════════════════════════════════
		//  活跃度统计（直播"挂机"判定）
		// ════════════════════════════════════════════════

		private static void OnPlayerUpdateEvent(object? sender, GetDataHandlers.PlayerUpdateEventArgs e)
		{
			if (e.Player == null)
				return;
			int who = e.Player.Index;
			if (who == _viewer)
				return; // 直播者自己不计活跃

			bool operating = e.Control.MoveUp || e.Control.MoveDown || e.Control.MoveLeft || e.Control.MoveRight
				|| e.Control.Jump || e.Control.IsUsingItem;

			lock (_sync)
			{
				bool moved = _lastPos.TryGetValue(who, out var lp)
					&& Vector2.DistanceSquared(lp, e.Position) > 1f;
				_lastPos[who] = e.Position;

				if (moved || operating)
					_lastActive[who] = DateTime.UtcNow;
				else if (!_lastActive.ContainsKey(who))
					_lastActive[who] = DateTime.UtcNow;
			}
		}

		// ════════════════════════════════════════════════
		//  命令
		// ════════════════════════════════════════════════

		private static void LiveCommand(CommandArgs args)
		{
			TSPlayer admin = args.Player;
			if (admin == null) return;

			var paras = args.Parameters;
			if (paras.Count >= 1 && (paras[0].Equals("off", StringComparison.OrdinalIgnoreCase) || paras[0].Equals("stop", StringComparison.OrdinalIgnoreCase)))
			{
				if (_viewer == admin.Index && (_spectateActivated || _awaitingEntry))
				{
					StopViewing();
					admin.SendInfoMessage("[slive]结束");
				}
				return;
			}

			if (paras.Count >= 1 && double.TryParse(paras[0], out double sec))
			{
				_liveIdleSeconds = Math.Clamp(sec, 1.0, 300.0);
			}

			if (_viewer == admin.Index && (_spectateActivated || _awaitingEntry))
				return;

			// 优先切第一个活跃玩家；全员挂机时降级切第一个存活玩家（直播是持续状态）
			int first = FindNextLiveTarget(-1);
			if (first < 0)
				first = FindNextTarget(-1);
			if (first < 0)
				return;

			PrepareViewing(admin.Index, first);
			admin.SendInfoMessage("[slive]启动");
		}

		// ════════════════════════════════════════════════
		//  辅助
		// ════════════════════════════════════════════════

		private static TSPlayer? GetPlayer(int who)
			=> who >= 0 && who < TShock.Players.Length ? TShock.Players[who] : null;

		private static void KeepAlive(int who)
		{
			if (who >= 0 && who < Netplay.Clients.Length)
				Netplay.Clients[who].TimeOutTimer = 0;
		}
	}
}
