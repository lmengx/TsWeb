using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using MonoMod.RuntimeDetour;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;

namespace TShockData
{
	/// <summary>
	/// ShopUI — 虚拟旅商商店
	///
	/// 交互（全部基于 1.4.5 协议源码实证）：
	///   1. 手持锡斧（ItemID.TinAxe=3500）按下使用键（挥动）→ 在脚底召唤虚拟旅商（NPC 368）
	///   2. 仅自己可见：detour NetMessage.SendPacket，把虚拟旅商的 23 号包（SyncNPC）
	///      / 40 号包（SyncTalkNPC）只发给所属玩家，其他玩家收不到 → 天然不可见
	///   3. 旅商存在时再次挥动锡斧 → 拉回身边（改 position + 定向广播 23）
	///   4. 关闭对话（客户端上报 talkNPC=-1）或下线 → 自动移除（life=0 + active=false + 定向 23）
	///
	/// 已确认的限制：40 号包只同步 talkNPC 状态，不填充客户端对话文本（npcChatText 由
	/// 客户端点击 NPC 时 GetChat() 生成）→ 服务端无法强制弹出对话面板，玩家需点击一次旅商。
	/// ⚠️ 因此绝不主动发 40 号包/SetTalkNPC（会把 talkNPC 卡住产生虚假对话窗口，
	///    破坏玩家后续点击正常 NPC 的交互）。对话状态完全由客户端点击旅商自然建立。
	/// </summary>
	[ApiVersion(2, 1)]
	public class ShopUIPlugin : TerrariaPlugin
	{
		public override string Author => "lmx12330";
		public override string Description => "虚拟旅商商店（挥动锡斧召唤，仅自己可见）";
		public override string Name => "ShopUI";
		public override Version Version => new Version(1, 0, 2, 0);

		public ShopUIPlugin(Main game) : base(game) { }

		public override void Initialize()
		{
			ShopUICore.Initialize(this);
			TShockAPI.Commands.ChatCommands.Add(new Command("", ShopUICore.HandleCommand, "shopui", "旅商"));
		}

		protected override void Dispose(bool Disposing)
		{
			if (Disposing)
			{
				ShopUICore.Dispose();
				TShockAPI.Commands.ChatCommands.RemoveAll(c =>
					c.Names.Any(n => n.Equals("shopui", StringComparison.OrdinalIgnoreCase) || n == "旅商"));
			}
			base.Dispose(Disposing);
		}
	}

	public static class ShopUICore
	{
		private const int TravelingMerchantType = NPCID.TravellingMerchant; // 368
		private const int TinAxeItem = ItemID.TinAxe;                       // 3500

		/// <summary>whoAmI → 虚拟旅商 NPC 索引（每玩家同时最多一个）</summary>
		private static readonly Dictionary<int, int> _active = new Dictionary<int, int>();
		/// <summary>whoAmI → 上一帧是否按着使用键（挥动上升沿检测）</summary>
		private static readonly Dictionary<int, bool> _prevUsing = new Dictionary<int, bool>();
		/// <summary>whoAmI → 是否正在与虚拟旅商对话（精确区分：关闭的是旅商对话还是其他 NPC 对话）</summary>
		private static readonly Dictionary<int, bool> _talkingWithMerchant = new Dictionary<int, bool>();

		private static bool _initialized;
		private static TerrariaPlugin? _plugin;
		private static Hook? _sendPacketHook;

		// ═══════════ MonoMod detour ═══════════

		private delegate void OrigSendPacket(byte[] data, int remoteClient);
		private delegate void SendPacketDetourHandler(OrigSendPacket orig, byte[] data, int remoteClient);

		public static void Initialize(TerrariaPlugin plugin)
		{
			if (_initialized) return;
			_initialized = true;
			_plugin = plugin;

			// 1. 挥动检测（PlayerControls 包 13 号，TShock 已解析）
			GetDataHandlers.PlayerUpdate += OnPlayerUpdate;
			// 2. 定向过滤（仅自己可见）：detour 出站包发送入口
			TryInstallSendPacketHook();
			// 3. 关闭对话检测（客户端→服务端 40 号包，最底层钩子）
			OTAPI.Hooks.MessageBuffer.GetData += OnGetData;
			// 4. 玩家下线兜底清理
			ServerApi.Hooks.ServerLeave.Register(plugin, OnServerLeave);

			TShock.Log.ConsoleInfo("[ShopUI] 已初始化（挥动锡斧召唤仅自己可见的虚拟旅商）");
		}

		public static void Dispose()
		{
			if (!_initialized) return;
			_initialized = false;

			GetDataHandlers.PlayerUpdate -= OnPlayerUpdate;
			OTAPI.Hooks.MessageBuffer.GetData -= OnGetData;
			ServerApi.Hooks.ServerLeave.Deregister(_plugin, OnServerLeave);
			_sendPacketHook?.Dispose();
			_sendPacketHook = null;

			foreach (int who in _active.Keys.ToList())
			{
				RemoveMerchant(who, closeChat: false, silent: true);
			}
			_active.Clear();
			_prevUsing.Clear();
			_talkingWithMerchant.Clear();
			_plugin = null;
			TShock.Log.ConsoleInfo("[ShopUI] 已释放");
		}

		// ═══════════════════ detour：SendPacket 定向过滤 ═══════════════════

		private static void TryInstallSendPacketHook()
		{
			try
			{
				var method = typeof(NetMessage).GetMethod("SendPacket",
					BindingFlags.NonPublic | BindingFlags.Static,
					null, new[] { typeof(byte[]), typeof(int) }, null);
				if (method == null)
				{
					TShock.Log.ConsoleError("[ShopUI] 未找到 NetMessage.SendPacket，仅自己可见功能不可用");
					return;
				}
				_sendPacketHook = new Hook(method, new SendPacketDetourHandler(SendPacketDetour));
				TShock.Log.ConsoleInfo("[ShopUI] SendPacket 定向过滤已挂载");
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[ShopUI] SendPacket Hook 注册失败: {ex.Message}");
			}
		}

		private static void SendPacketDetour(OrigSendPacket orig, byte[] data, int remoteClient)
		{
			if (_active.Count > 0 && data != null && data.Length >= 6 && ShouldSuppress(data, remoteClient))
			{
				return; // 虚拟旅商的包：跳过非所属玩家
			}
			orig(data, remoteClient);
		}

		/// <summary>
		/// 出站包格式：[0..1]=长度 [2]=包类型 [3..]=body
		/// 23 号包（SyncNPC）body 前 2 字节 = NPC 索引；40 号包（SyncTalkNPC）body = [玩家ID][talkNPC]
		/// </summary>
		private static bool ShouldSuppress(byte[] data, int remoteClient)
		{
			byte msgType = data[2];

			if (msgType == (byte)MessageID.SyncNPC) // 23
			{
				int npcIdx = data[3] | (data[4] << 8);
				foreach (var kvp in _active)
				{
					if (kvp.Value == npcIdx && kvp.Key != remoteClient) return true;
				}
			}
			else if (msgType == (byte)MessageID.SyncTalkNPC) // 40
			{
				short talkNPC = (short)(data[4] | (data[5] << 8));
				foreach (var kvp in _active)
				{
					if (kvp.Value == talkNPC && kvp.Key != remoteClient) return true;
				}
			}
			return false;
		}

		// ═══════════════════ 挥动检测 ═══════════════════

		private static void OnPlayerUpdate(object sender, GetDataHandlers.PlayerUpdateEventArgs args)
		{
			var plr = args.Player;
			if (plr == null || !plr.RealPlayer) return;
			int who = plr.Index;
			if (who < 0 || who >= Main.maxPlayers) return;

			// 只在"按下使用键"瞬间响应（false→true 上升沿），按住不重复触发
			bool usingNow = args.Control.IsUsingItem;
			bool wasUsing = _prevUsing.TryGetValue(who, out var w) && w;
			_prevUsing[who] = usingNow;
			if (!usingNow || wasUsing) return;

			// 手持物品必须是锡斧
			int slot = args.SelectedItem;
			if (slot < 0 || slot >= plr.TPlayer.inventory.Length) return;
			var held = plr.TPlayer.inventory[slot];
			if (held == null || held.type != TinAxeItem) return;

			Trigger(who);
		}

		// ═══════════════════ 核心逻辑 ═══════════════════

		/// <summary>存在 → 拉回；不存在 → 召唤</summary>
		private static void Trigger(int who)
		{
			if (_active.ContainsKey(who))
			{
				PullBack(who);
			}
			else
			{
				Spawn(who);
			}
		}

		private static void Spawn(int who)
		{
			var tp = Main.player[who];
			if (tp == null || !tp.active) return;

			// 创建真实旅商 NPC（仅自己可见：23 号包被 detour 定向过滤）
			int npcIndex = NPC.NewNPC(new EntitySource_WorldGen(),
				(int)tp.Bottom.X, (int)tp.Bottom.Y, TravelingMerchantType);
			if (npcIndex < 0 || npcIndex >= Main.maxNPCs)
			{
				TShock.Players[who]?.SendErrorMessage("[ShopUI] 旅商生成失败（没有可用 NPC 槽位）");
				return;
			}

			var npc = Main.npc[npcIndex];
			npc.aiStyle = -1;           // 冻结移动（站定展示）
			npc.velocity = Vector2.Zero;
			_active[who] = npcIndex;

			// 广播 23 号包 → detour 过滤 → 仅目标玩家可见
			// ⚠️ 不再主动发 40 号包：它只 SetTalkNPC 不同步对话文本，会把客户端
			//    talkNPC 卡在虚拟旅商上（与残留 npcChatText 组合出虚假对话窗口，
			//    且 mouseInterface 被占用 → 玩家点击正常 NPC 失效）。
			//    玩家点击旅商时客户端自然建立对话状态（SetTalkNPC + GetChat + 发 40 同步）。
			NetMessage.SendData(23, -1, -1, null, npcIndex);

			TShock.Log.ConsoleInfo($"[ShopUI] 玩家 {TShock.Players[who]?.Name} 召唤了虚拟旅商（NPC #{npcIndex}）");
			TShock.Players[who]?.SendSuccessMessage("[ShopUI] 虚拟旅商已出现！点击他对话；关闭对话后自动消失；再挥动锡斧可拉回身边");
		}

		/// <summary>把虚拟旅商拉回玩家脚底</summary>
		private static void PullBack(int who)
		{
			if (!_active.TryGetValue(who, out int npcIndex)) return;
			if (npcIndex < 0 || npcIndex >= Main.maxNPCs) return;
			var tp = Main.player[who];
			if (tp == null || !tp.active) return;

			var npc = Main.npc[npcIndex];
			npc.Bottom = tp.Bottom;
			npc.velocity = Vector2.Zero;
			npc.netUpdate = true;
			// 定向广播（detour 过滤 → 仅目标玩家可见拉回）
			NetMessage.SendData(23, -1, -1, null, npcIndex);

			TShock.Players[who]?.SendSuccessMessage("[ShopUI] 虚拟旅商已拉回身边");
		}

		/// <summary>移除：实体置 inactive + 定向广播 23 + 可选关闭对话</summary>
		private static void RemoveMerchant(int who, bool closeChat, bool silent)
		{
			if (!_active.TryGetValue(who, out int npcIndex)) return;
			_active.Remove(who);
			_talkingWithMerchant[who] = false;

			if (npcIndex >= 0 && npcIndex < Main.maxNPCs && Main.npc[npcIndex].active)
			{
				Main.npc[npcIndex].life = 0;
				Main.npc[npcIndex].active = false;
				NetMessage.SendData(23, -1, -1, null, npcIndex);
			}

			if (closeChat && who >= 0 && who < Main.maxPlayers)
			{
				Main.player[who].SetTalkNPC(-1);
				NetMessage.SendData(40, -1, -1, null, who);
			}

			if (!silent)
			{
				TShock.Log.ConsoleInfo($"[ShopUI] 虚拟旅商（NPC #{npcIndex}）已移除");
			}
		}

		// ═══════════════════ 关闭对话检测（入站 40 号包） ═══════════════════

		private static void OnGetData(object sender, OTAPI.Hooks.MessageBuffer.GetDataEventArgs args)
		{
			if (_active.Count == 0) return;
			try
			{
				var buf = args.Instance?.readBuffer;
				if (buf == null) return;
				int off = args.ReadOffset;
				if (off < 1 || args.Length < 3) return;

				byte id = buf[off - 1]; // 包类型（off-1，PacketCatch 同款取法）
				if (id != (byte)MessageID.SyncTalkNPC) return; // 只关心 40

				int who = args.Instance.whoAmI;
				if (who < 0 || who >= Main.maxPlayers) return;
				if (!_active.ContainsKey(who)) return;

				// 入站 40 包 body：[0]=玩家ID [1..3]=talkNPC(LE)
				short talkNPC = (short)(buf[off + 1] | (buf[off + 2] << 8));
				int expected = _active[who];

				if (talkNPC == expected)
				{
					// 玩家正在与虚拟旅商对话
					_talkingWithMerchant[who] = true;
				}
				else if (talkNPC == -1)
				{
					// 关闭对话：仅当关闭的是旅商对话才移除（避免误移除：玩家先点了向导再关闭）
					bool wasTalking = _talkingWithMerchant.TryGetValue(who, out var t) && t;
					_talkingWithMerchant[who] = false;
					if (wasTalking)
					{
						TShock.Log.ConsoleInfo($"[ShopUI] 玩家 {TShock.Players[who]?.Name} 已断开旅商对话，移除虚拟旅商");
						RemoveMerchant(who, closeChat: false, silent: false);
					}
				}
				else
				{
					// 切换到其他 NPC 对话
					_talkingWithMerchant[who] = false;
				}
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[ShopUI] 40号包监听异常: {ex.Message}");
			}
		}

		private static void OnServerLeave(LeaveEventArgs e)
		{
			_prevUsing.Remove(e.Who);
			_talkingWithMerchant.Remove(e.Who);
			if (_active.ContainsKey(e.Who))
			{
				RemoveMerchant(e.Who, closeChat: false, silent: true);
			}
		}

		// ═══════════════════ 命令（调试辅助） ═══════════════════

		public static void HandleCommand(CommandArgs args)
		{
			if (args.Parameters.Count > 0 && args.Parameters[0].Equals("kill", StringComparison.OrdinalIgnoreCase))
			{
				if (_active.ContainsKey(args.Player.Index))
				{
					RemoveMerchant(args.Player.Index, closeChat: true, silent: false);
					args.Player.SendSuccessMessage("[ShopUI] 已移除你的虚拟旅商");
				}
				else
				{
					args.Player.SendInfoMessage("[ShopUI] 你当前没有虚拟旅商");
				}
				return;
			}

			if (args.Parameters.Count > 0 && args.Parameters[0].Equals("status", StringComparison.OrdinalIgnoreCase))
			{
				args.Player.SendInfoMessage($"[ShopUI] 当前激活的虚拟旅商: {_active.Count} 个");
				return;
			}

			// 无参数 = 模拟一次挥动（调试方便）
			Trigger(args.Player.Index);
		}
	}
}
