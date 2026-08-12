using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;

namespace TShockData
{
	/// <summary>跨服预传送标记（目标服侧：来自他服的受信连接，握手期放行 + SyncIP 用）</summary>
	public class PreTransferInfo
	{
		public string SourceServerId { get; set; } = "";   // 来源服 serverId
		public string PlayerName { get; set; } = "";       // 玩家名
		public string UUID { get; set; } = "";             // 玩家 UUID
		public string RealIP { get; set; } = "";           // 真实 IP（SyncIP 用）
		public long AuthedAt { get; set; }                 // 鉴权时间戳
	}

	/// <summary>
	/// Unused15(15) 自定义包协议 + Auth 密钥鉴权（信封格式与 MultiSEngine 一致）：
	///   [ushort 总长度][byte 15][string 包名][payload...]
	/// TShock 不处理包 15、客户端从不发 15 → 安全通道；由 CrossTransfer 的
	/// MonoMod detour 在 TShock.OnGetData 之前完全接管。
	/// </summary>
	public static class TransferProtocol
	{
		public const byte CustomPacketId = 15;   // MessageID.Unused15

		// 包名（全局唯一）
		public const string AuthPacket = "TSWeb.Auth";       // A→B：密钥鉴权握手
		public const string AuthAckPacket = "TSWeb.AuthAck"; // B→A：鉴权结果
		public const string SyncIPPacket = "TSWeb.SyncIP";   // A→B：指定真实 IP
		public const string ReturnPacket = "TSWeb.Return";   // B→A：请求返回原服
		public const string TransferRequestPacket = "TSWeb.TransferRequest"; // B→A：桥接玩家在 B 服请求再传送
		public const string TransferResultPacket = "TSWeb.TransferResult";   // A→B：源服判定结果

		/// <summary>本服 TShock 眼中"来自他服的受信连接"（whoAmI → 预传送信息）</summary>
		public static readonly ConcurrentDictionary<int, PreTransferInfo> PreTransfers = new();

		private static readonly HashSet<string> _nonceCache = new();
		private static readonly object _nonceLock = new();

		/// <summary>编码自定义包：[ushort len][byte 15][string name][payload...]</summary>
		public static byte[] EncodeCustomData(string name, Action<BinaryWriter> writePayload)
		{
			using var ms = new MemoryStream();
			using (var bw = new BinaryWriter(ms, Encoding.UTF8, true))
			{
				bw.Write(CustomPacketId);
				bw.Write(name);
				writePayload(bw);
			}
			return WrapWithLength(ms.ToArray());
		}

		/// <summary>编码普通协议包：[ushort len][body...]（body 首字节 = 包类型）</summary>
		public static byte[] EncodePacket(Action<BinaryWriter> writeBody)
		{
			using var ms = new MemoryStream();
			using (var bw = new BinaryWriter(ms, Encoding.UTF8, true))
				writeBody(bw);
			return WrapWithLength(ms.ToArray());
		}

		private static byte[] WrapWithLength(byte[] body)
		{
			// Terraria 协议：长度字段 = 整帧总长（含 2 字节长度前缀本身）
			var total = body.Length + 2;
			if (total > ushort.MaxValue) throw new InvalidOperationException("包过大");
			var packet = new byte[total];
			packet[0] = (byte)(total & 0xFF);
			packet[1] = (byte)((total >> 8) & 0xFF);
			Buffer.BlockCopy(body, 0, packet, 2, body.Length);
			return packet;
		}

		// ════════════════════════════════════════════
		//  Auth 密钥鉴权（HMAC-SHA256，算法与 WebhookAuth 一致）
		// ════════════════════════════════════════════

		/// <summary>签名原文：{source}.{ts}.{nonce}.{player}.{uuid}.{ip}</summary>
		public static string BuildAuthSignInput(string source, long ts, string nonce,
			string player, string uuid, string ip)
			=> $"{source}.{ts}.{nonce}.{player}.{uuid}.{ip}";

		/// <summary>验签：时间窗 ±300s + nonce 去重 + constant-time 比对</summary>
		public static bool VerifyAuth(TransferServerInfo server, long ts, string nonce,
			string signInput, string sig)
		{
			if (string.IsNullOrEmpty(server?.Secret)) return false;
			if (Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - ts) > 300_000) return false;

			lock (_nonceLock)
			{
				var key = $"{server.Name}:{nonce}";
				if (!_nonceCache.Add(key)) return false;
				if (_nonceCache.Count > 10000) _nonceCache.Clear();
			}

			var expected = WebhookAuth.HmacSha256Hex(server.Secret, signInput);
			return string.Equals(expected, sig, StringComparison.OrdinalIgnoreCase);
		}

		// ════════════════════════════════════════════
		//  包 15 分发（由 CrossTransfer 的 detour 调用）
		// ════════════════════════════════════════════

		/// <summary>解析并分发包 15（由底层 MessageBuffer 钩子与 TShock 事件共同调用）。返回 true 表示已消费。</summary>
		public static bool HandleInbound(int whoAmI, byte[] readBuffer, int index, int length)
		{
			if (length <= 1) return true; // 空包：消费

			// 注意：index 已指向 payload 起始（msgId 之后）！
			// 底层钩子（OTAPI.MessageBuffer.GetData）的 ReadOffset 与 TShock.GetDataEventArgs.Index
			// 语义一致（均不含 msgId），length 含 msgId 1 字节 → payload 长度 = length - 1。
			// 之前误用 index + 1 跳过了包名首字节 → Auth/SyncIP 从未被识别（"未知自定义包"）。
			using var ms = new MemoryStream(readBuffer, index, length - 1);
			using var br = new BinaryReader(ms, Encoding.UTF8);
			var name = br.ReadString();
			TShock.Log.ConsoleInfo($"[CrossTransfer] 收到自定义包 slot#{whoAmI}: {name}");
			switch (name)
			{
				case AuthPacket:
					OnAuth(whoAmI, br);
					return true;
				case SyncIPPacket:
					OnSyncIP(whoAmI, br);
					return true;
				case TransferResultPacket:
					OnTransferResult(whoAmI, br);
					return true;
				default:
					TShock.Log.ConsoleWarn($"[CrossTransfer] 未知自定义包: {name}");
					return true;
			}
		}

		/// <summary>B 服收到 Auth：验签 → preTransfer 标记 → AuthAck 回发</summary>
		private static void OnAuth(int whoAmI, BinaryReader br)
		{
			var source = br.ReadString();
			var playerName = br.ReadString();
			var uuid = br.ReadString();
			var realIP = br.ReadString();
			var ts = br.ReadInt64();
			var nonce = br.ReadString();
			var sig = br.ReadString();

			// 按来源 serverId 找本服配置中对端密钥
			var server = CrossTransfer.Config.Servers.Find(
				s => s.Name.Equals(source, StringComparison.OrdinalIgnoreCase));
			if (server == null)
			{
				SendAuthAck(whoAmI, false, $"未配置来源服务器: {source}");
				return;
			}

			var signInput = BuildAuthSignInput(source, ts, nonce, playerName, uuid, realIP);
			if (!VerifyAuth(server, ts, nonce, signInput, sig))
			{
				TShock.Log.ConsoleWarn($"[CrossTransfer] 鉴权失败: {playerName}@{source} (slot#{whoAmI})");
				SendAuthAck(whoAmI, false, "鉴权失败");
				SendKick(whoAmI, "跨服传送鉴权失败");
				return;
			}

			PreTransfers[whoAmI] = new PreTransferInfo
			{
				SourceServerId = source,
				PlayerName = playerName,
				UUID = uuid,
				RealIP = realIP,
				AuthedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			};
			TShock.Log.ConsoleInfo($"[CrossTransfer] 受信连接已鉴权: {playerName} ({source}) → 本服 slot#{whoAmI}");

			// 提前写入 Terraria 原生 ClientUUID（防止 OnJoin 的 KickEmptyUUID 检查读到空白）
			SetRemoteClientUUID(whoAmI, uuid);

			// 立即保障账号（在 ClientUUID 与 ContinueConnecting2 到达之前完成 UUID 绑定）
			EnsureAccount(whoAmI);

			SendAuthAck(whoAmI, true, "");
		}

		// ════════════════════════════════════════════
		//  SyncIP：真实 IP 替换（TSPlayer.IP 只读，反射写私有 CacheIP）
		// ════════════════════════════════════════════

		private static void OnSyncIP(int whoAmI, BinaryReader br)
		{
			var playerName = br.ReadString();
			var ip = br.ReadString();

			if (PreTransfers.TryGetValue(whoAmI, out var pre))
				pre.RealIP = ip;

			if (whoAmI >= 0 && whoAmI < TShock.Players.Length && TShock.Players[whoAmI] is { } p)
			{
				try
				{
					var f = typeof(TSPlayer).GetField("CacheIP",
						BindingFlags.NonPublic | BindingFlags.Instance);
					f?.SetValue(p, ip);
					TShock.Log.ConsoleInfo($"[CrossTransfer] SyncIP: {playerName} → {ip} (slot#{whoAmI})");
				}
				catch (Exception ex)
				{
					TShock.Log.ConsoleWarn($"[CrossTransfer] SyncIP 替换失败: {ex.Message}");
				}
			}
		}

		// ════════════════════════════════════════════
		//  B 服账号保障：preTransfer 连接在 TShock 处理 PlayerInfo 之前
		//  确保账号存在且 UUID 已绑定 → TShock UUID 自动登录（HandleConnecting）
		// ════════════════════════════════════════════

		public static void EnsureAccount(int whoAmI)
		{
			if (!PreTransfers.TryGetValue(whoAmI, out var pre)) return;
			try
			{
				var acc = TShock.UserAccounts.GetUserAccountByName(pre.PlayerName);
				if (acc == null)
				{
					acc = new UserAccount(pre.PlayerName, "", "",
						TShock.Config.Settings.DefaultRegistrationGroupName, "", "", "");
					acc.CreateBCryptHash(Guid.NewGuid().ToString("N")); // 随机密码：跨服仅靠 UUID 登录
					TShock.UserAccounts.AddUserAccount(acc);
				}
				TShock.UserAccounts.SetUserAccountUUID(acc, pre.UUID);
				TShock.Log.ConsoleInfo($"[CrossTransfer] 已为跨服玩家保障账号: {pre.PlayerName}");
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleWarn($"[CrossTransfer] 账号保障失败: {ex}");
			}
		}

		// ════════════════════════════════════════════
		//  TransferResult：源服对二次传送请求的判定结果
		// ════════════════════════════════════════════

		/// <summary>B 服收到源服的判定结果：[bool ok][string 消息] → 提示桥接玩家</summary>
		private static void OnTransferResult(int whoAmI, BinaryReader br)
		{
			var ok = br.ReadBoolean();
			var msg = br.ReadString();
			TShock.Log.ConsoleInfo($"[CrossTransfer] 源服判定结果 slot#{whoAmI}: ok={ok} {msg}");
			if (whoAmI >= 0 && whoAmI < TShock.Players.Length && TShock.Players[whoAmI] is { } p)
			{
				if (ok) p.SendSuccessMessage($"[跨服] {msg}");
				else p.SendErrorMessage($"[跨服] 传送失败: {msg}");
			}
		}

		// ════════════════════════════════════════════
		//  发送（Terraria 原生 Netplay socket 直写，不依赖 TShock 玩家对象）
		// ════════════════════════════════════════════

		/// <summary>
		/// 反射写入 Terraria 原生 RemoteClient.ClientUUID（= TSPlayer.UUID 的唯一来源，
		/// TSPlayer.cs:1113 → Netplay.Clients[i].ClientUUID）。
		/// 真实客户端的 68 包由 Terraria 底层写入；模拟客户端因 68 被 TShock 早期拦截
		/// （AllowedEarlyPackets 不含 68 → e.Handled → Terraria 跳过），必须手动写入。
		/// </summary>
		public static void SetRemoteClientUUID(int who, string uuid)
		{
			try
			{
				if (who < 0 || who >= Netplay.Clients.Length) return;
				var client = Netplay.Clients[who];
				if (client == null) return;
				var t = client.GetType();
				var f = t.GetField("ClientUUID",
					BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
				if (f != null) f.SetValue(client, uuid);
				else
					t.GetProperty("ClientUUID",
						BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
						?.SetValue(client, uuid);
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleWarn($"[CrossTransfer] 写入 RemoteClient.ClientUUID 失败: {ex.Message}");
			}
		}

		public static void SendAuthAck(int whoAmI, bool ok, string reason)
		{
			var bytes = EncodeCustomData(AuthAckPacket, bw =>
			{
				bw.Write(ok);
				bw.Write(reason ?? "");
			});
			SendRaw(whoAmI, bytes);
		}

		public static void SendKick(int whoAmI, string reason)
		{
			var bytes = EncodePacket(bw =>
			{
				bw.Write((byte)2);
				bw.Write(reason ?? "");
			});
			SendRaw(whoAmI, bytes);
		}

		/// <summary>向 Netplay.Clients[who].Socket 直写字节（AuthAck/Kick 均走底层 socket）</summary>
		public static void SendRaw(int whoAmI, byte[] data)
		{
			try
			{
				if (whoAmI < 0 || whoAmI >= Netplay.Clients.Length) return;
				var sock = Netplay.Clients[whoAmI]?.Socket;
				if (sock == null) return;
				sock.AsyncSend(data, 0, data.Length, _ => { });
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleWarn($"[CrossTransfer] 向 slot#{whoAmI} 发送失败: {ex.Message}");
			}
		}
	}
}
