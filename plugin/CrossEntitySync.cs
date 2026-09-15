using System;
using System.Collections.Generic;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.NetModules;
using Terraria.Net;
using TShockAPI;

namespace TShockData
{
	/// <summary>
	/// 跨服切换时的「客户端旧世界实体视图」清理（移植自 Dimensions 的 clearutils.ts + 实体台账）。
	///
	/// 背景：TSWeb 的跨服桥接**不重连客户端**——同一个 socket 换世界（A → B、B → C、返回 A）。
	/// Terraria 客户端在收到新世界数据时**不会自己清空实体数组**（反编译已确认：只有收到
	/// PlayerActive(14) 才会把 Main.player[slot].active 置 false；NPC 只有收到 SyncNPC(23)
	/// 且解码后的 life &lt;= 0 才会 active=false；物品同理）。因此旧世界的 NPC / 掉落物 /
	/// 其他玩家 / 晶塔会残留在客户端显示，其中晶塔最严重：客户端晶塔表按「位置 + 类型」累加，
	/// 残留项会导致地图上的晶塔传送图标不消失，并使同类型晶塔「挖掉后无法再放置」。
	///
	/// Dimensions 在 changeServer() 里按固定顺序广播合成包清理（clearPlayers → clearNPCs →
	/// clearItems → clearPylons → clearJourneyPowers）。本类等价实现前四项，顺序一致。
	///
	/// 线格式取自本地反编译源码（1.4.5.x，scripts/反编译/参考源码/_bossai_ref）：
	///   14 PlayerActive ：[byte slot][byte active]                            （NetMessage.cs case 14）
	///   21 SyncItem     ：[short slot][f32 x][f32 y][f32 vx][f32 vy][short stack]
	///                     [byte prefix][byte flags][short type]               （MessageBuffer.cs case 21）
	///   23 SyncNPC      ：[byte slot][byte gen][f32 x][f32 y][f32 vx][f32 vy][ushort target]
	///                     [byte b30][byte b31][short netID][byte lifeKind][life 值]
	///                     （MessageBuffer.cs case 23；life 编码 = 1 字节类型 + 1/2/4 字节值）
	///   82 LoadNetModule：[ushort moduleId][byte subType][short x][short y][byte type]
	///                     （NetTeleportPylonModule.SerializePylonWasAddedOrRemoved；
	///                       subType：0=Added 1=Removed 2=PlayerRequestsTeleport）
	///
	/// 未实现项（Dimensions 的 clearJourneyPowers）：创造模式能力（Godmode / FarPlacementRange /
	/// SpawnRateSlider）在客户端跨世界保留，需经 NetCreativePowers 模块子包复位。TSWeb 目标服均为
	/// 非创造模式，故暂不实现；若将来接入创造模式服务器，在此补第五步即可。
	/// </summary>
	public static class CrossEntitySync
	{
		public const byte MsgPlayerActive = 14;
		public const byte MsgSyncItem = 21;
		public const byte MsgSyncNpc = 23;
		public const byte MsgLoadNetModule = 82;

		/// <summary>玩家槽位数（Terraria 协议固定 255）</summary>
		private const int PlayerSlots = 255;
		/// <summary>NPC 槽位数</summary>
		private const int NpcSlots = 200;
		/// <summary>掉落物槽位数</summary>
		private const int ItemSlots = 400;

		/// <summary>玩家活动包长度：[ushort len][byte 14][byte slot][byte active]</summary>
		private const int PlayerFrameSize = 5;
		/// <summary>物品同步包长度（flags=0，无 shimmer/抓取延迟尾随字段）</summary>
		private const int ItemFrameSize = 27;
		/// <summary>NPC 同步包长度（b30=b31=0，lifeKind=0 + 1 字节 sbyte，另留 1 字节容忍 releaseOwner）</summary>
		private const int NpcFrameSize = 30;
		/// <summary>晶塔 NetModule 包长度（固定，无尾随字段）</summary>
		private const int PylonFrameSize = 11;

		/// <summary>晶塔模块 ID（主线程初始化时解析；0 = 解析失败，跳过晶塔清理）</summary>
		private static ushort _pylonModuleId;
		private static bool _pylonIdWarned;

		/// <summary>开关：跟随跨服配置，热改配置即时生效</summary>
		public static bool Enabled => CrossTransfer.Config.ClearClientEntities;

		/// <summary>主线程初始化：解析并缓存晶塔模块 ID（NetModule 的 ID 由启动期注册顺序决定）</summary>
		public static void Initialize()
		{
			ResolvePylonModuleId();
		}

		private static void ResolvePylonModuleId()
		{
			try
			{
				_pylonModuleId = NetManager.Instance.GetId<NetTeleportPylonModule>();
				TShock.Log.ConsoleInfo($"[CrossTransfer] 实体清理已就绪（晶塔 NetModule ID={_pylonModuleId}）");
			}
			catch (Exception ex)
			{
				_pylonModuleId = 0;
				TShock.Log.ConsoleWarn($"[CrossTransfer] 晶塔模块 ID 解析失败，将跳过晶塔清理: {ex.Message}");
			}
		}

		/// <summary>
		/// 切换世界前清理客户端实体视图：玩家 → NPC → 物品 → 晶塔（顺序与 Dimensions 一致）。
		/// 调用时机：目标服世界数据帧重放**之前**（客户端此刻仍显示旧世界）。
		/// </summary>
		/// <param name="who">桥接玩家在 A 服的 slot（发送用）</param>
		/// <param name="selfSlot">客户端自认的自身 slot（必须跳过，否则会把自己的角色清掉）</param>
		/// <param name="pylons">旧世界晶塔表（首次切换取本服 Main.PylonSystem，之后取台账）</param>
		/// <param name="ledger">本会话的晶塔台账（清理后清空）</param>
		public static void ClearBeforeSwitch(
			int who, int selfSlot,
			IEnumerable<(short X, short Y, byte Type)>? pylons,
			PylonLedger ledger)
		{
			if (!Enabled) return;
			try
			{
				var playerFrame = BuildPlayerClear(selfSlot, out int playerCount);
				if (playerFrame.Length > 0) CrossTransfer.SendToPlayerSocket(who, playerFrame);

				CrossTransfer.SendToPlayerSocket(who, BuildNpcClear());
				CrossTransfer.SendToPlayerSocket(who, BuildItemClear());

				int pylonCount = 0;
				var pylonFrame = BuildPylonClear(pylons, out pylonCount);
				if (pylonFrame != null) CrossTransfer.SendToPlayerSocket(who, pylonFrame);
				ledger.Clear();

				TShock.Log.ConsoleInfo(
					$"[CrossTransfer] 已清理客户端 #{who} 的旧世界实体视图" +
					$"（玩家 {playerCount} 槽 / NPC {NpcSlots} 槽 / 物品 {ItemSlots} 槽 / 晶塔 {pylonCount} 座，自身 slot={selfSlot}）");
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleWarn($"[CrossTransfer] 清理客户端实体失败: {ex.Message}");
			}
		}

		/// <summary>本服自身的晶塔表快照（首次切换时客户端显示的正是本服世界）</summary>
		public static List<(short X, short Y, byte Type)> LocalPylonSnapshot()
		{
			var list = new List<(short, short, byte)>();
			try
			{
				foreach (TeleportPylonInfo pylon in Main.PylonSystem.Pylons)
					list.Add((pylon.PositionInTiles.X, pylon.PositionInTiles.Y, (byte)pylon.TypeOfPylon));
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleWarn($"[CrossTransfer] 读取本服晶塔表失败: {ex.Message}");
			}
			return list;
		}

		// ────────────────────────── 合成包构造 ──────────────────────────

		private static byte[] BuildPlayerClear(int selfSlot, out int count)
		{
			var buf = new byte[PlayerSlots * PlayerFrameSize];
			int o = 0;
			count = 0;
			for (int slot = 0; slot < PlayerSlots; slot++)
			{
				if (slot == selfSlot) continue;   // 跳过自身：否则客户端会把自己的角色当作已下线
				buf[o++] = PlayerFrameSize;
				buf[o++] = 0;
				buf[o++] = MsgPlayerActive;
				buf[o++] = (byte)slot;
				buf[o++] = 0;                     // active = false
				count++;
			}
			if (o == buf.Length) return buf;
			var trimmed = new byte[o];
			Buffer.BlockCopy(buf, 0, trimmed, 0, o);
			return trimmed;
		}

		private static byte[] BuildNpcClear()
		{
			var buf = new byte[NpcSlots * NpcFrameSize];
			int o = 0;
			for (int slot = 0; slot < NpcSlots; slot++)
			{
				// [ushort len] [23] [slot] [gen=0] [pos*2 f32] [vel*2 f32] [target ushort]
				// [b30=0] [b31=0] [netID short=0] [lifeKind byte=0] [life sbyte=0] [容忍字节 0]
				buf[o++] = NpcFrameSize;
				buf[o++] = 0;
				buf[o++] = MsgSyncNpc;
				buf[o++] = (byte)slot;
				buf[o++] = 0;                     // generation：与客户端现有值不同时客户端会重建实例，
				                                  // netID=0 + life=0 两种分支最终都会 active=false
				buf[o++] = 0; buf[o++] = 0; buf[o++] = 0; buf[o++] = 0;   // position = 0,0
				buf[o++] = 0; buf[o++] = 0; buf[o++] = 0; buf[o++] = 0;   // velocity = 0,0
				buf[o++] = 0; buf[o++] = 0;                               // target = 0
				buf[o++] = 0;                                             // b30=0：无 AI 字段，b30[7]=0 走 life 分支
				buf[o++] = 0;                                             // b31=0：无玩家数/难度字段
				buf[o++] = 0; buf[o++] = 0;                               // netID = 0（空槽）
				buf[o++] = 0;                                             // life 编码类型 = 0 → sbyte
				buf[o++] = 0;                                             // life = 0 → 客户端 active=false
				buf[o++] = 0;                                             // 容忍字节（可捕获 NPC 才读 releaseOwner）
			}
			return buf;
		}

		private static byte[] BuildItemClear()
		{
			var buf = new byte[ItemSlots * ItemFrameSize];
			int o = 0;
			for (int slot = 0; slot < ItemSlots; slot++)
			{
				// [ushort len] [21] [slot short] [pos*2 f32] [vel*2 f32] [stack short=0]
				// [prefix byte=0] [flags byte=0] [type short=0]
				buf[o++] = ItemFrameSize;
				buf[o++] = 0;
				buf[o++] = MsgSyncItem;
				buf[o++] = (byte)(slot & 0xFF);
				buf[o++] = (byte)((slot >> 8) & 0xFF);
				for (int i = 0; i < 16; i++) buf[o++] = 0;   // position + velocity = 0
				buf[o++] = 0; buf[o++] = 0;                 // stack = 0
				buf[o++] = 0;                               // prefix = 0
				buf[o++] = 0;                               // flags = 0（无 shimmer / 抓取延迟尾随字段）
				buf[o++] = 0; buf[o++] = 0;                 // type = 0 → 客户端把该槽视为空
			}
			return buf;
		}

		private static byte[]? BuildPylonClear(IEnumerable<(short X, short Y, byte Type)>? pylons, out int count)
		{
			count = 0;
			if (pylons == null) return null;
			if (_pylonModuleId == 0)
			{
				if (!_pylonIdWarned)
				{
					_pylonIdWarned = true;
					TShock.Log.ConsoleWarn("[CrossTransfer] 晶塔模块 ID 未知，本次不清理晶塔视图");
				}
				return null;
			}

			var list = new List<(short X, short Y, byte Type)>(pylons);
			if (list.Count == 0) return null;

			var buf = new byte[list.Count * PylonFrameSize];
			int o = 0;
			foreach (var p in list)
			{
				// [ushort len] [82] [moduleId ushort] [subType=1 Removed] [x short] [y short] [type byte]
				buf[o++] = PylonFrameSize;
				buf[o++] = 0;
				buf[o++] = MsgLoadNetModule;
				buf[o++] = (byte)(_pylonModuleId & 0xFF);
				buf[o++] = (byte)((_pylonModuleId >> 8) & 0xFF);
				buf[o++] = 1;                                  // PylonWasRemoved
				buf[o++] = (byte)(p.X & 0xFF);
				buf[o++] = (byte)((p.X >> 8) & 0xFF);
				buf[o++] = (byte)(p.Y & 0xFF);
				buf[o++] = (byte)((p.Y >> 8) & 0xFF);
				buf[o++] = p.Type;
				count++;
			}
			return buf;
		}

		/// <summary>
		/// 晶塔台账：桥接下行转发时解析 NetModule(82) 的晶塔子包，记录客户端当前「已看到」的晶塔。
		/// 目的与 Dimensions 的 entityTracking.pylons 相同——切换目标服时必须按台账发 Removed，
		/// 否则上一目标服的晶塔会在客户端一直残留（客户端晶塔表是累加的，且无法被新世界覆盖）。
		/// </summary>
		public sealed class PylonLedger
		{
			private readonly object _lock = new();
			private readonly HashSet<(short X, short Y, byte Type)> _set = new();

			/// <summary>解析一帧下行数据（帧含 2 字节长度前缀，frame[2] 为包类型）</summary>
			public void Track(byte[]? frame)
			{
				try
				{
					if (frame == null || frame.Length < PylonFrameSize) return;
					if (frame[2] != MsgLoadNetModule) return;
					if (_pylonModuleId == 0) return;

					ushort moduleId = (ushort)(frame[3] | (frame[4] << 8));
					if (moduleId != _pylonModuleId) return;

					byte sub = frame[5];
					if (sub > 1) return;   // 2 = 客户端上行请求传送，不出现在下行

					short x = (short)(frame[6] | (frame[7] << 8));
					short y = (short)(frame[8] | (frame[9] << 8));
					byte type = frame[10];

					lock (_lock)
					{
						if (sub == 0) _set.Add((x, y, type));
						else _set.Remove((x, y, type));
					}
				}
				catch
				{
					// 解析失败不影响转发
				}
			}

			public List<(short X, short Y, byte Type)> Snapshot()
			{
				lock (_lock) return new List<(short, short, byte)>(_set);
			}

			public void Clear()
			{
				lock (_lock) _set.Clear();
			}
		}
	}
}
