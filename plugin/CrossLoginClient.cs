using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TShockAPI;

namespace TShockData
{
	/// <summary>
	/// 模拟客户端（MultiSEngine PreConnectAdapter 移植到插件）：
	/// 以玩家身份向目标服发起前置握手，作为"代为登录"的控制通道。
	/// 阶段 0 流程：ClientHello → ClientUUID（先发 UUID）→ 读 LoadPlayer →
	///            发 Auth（Unused15）→ 等 AuthAck / Kick / RequestPassword。
	/// </summary>
	public static class CrossLoginClient
	{
		private const bool DebugLog = false; // 握手诊断日志（定位目标服响应）

		/// <summary>完整握手结果（阶段 8 桥接使用）</summary>
		public class HandshakeResult
		{
			public bool Ok { get; set; }
			public string Reason { get; set; } = "";
			public int RemoteSlot { get; set; } = -1;
			public TcpClient? Connection { get; set; }          // 保持的到目标服连接
			public List<byte[]> BufferedPackets { get; } = new(); // 握手期从目标服收到的完整帧（含长度前缀）
		}

		/// <summary>挂起的进服密码请求（目标服探测到密码 → 要求玩家在 A 服输入）</summary>
		private sealed class PendingHandshake
		{
			public string PlayerName = "";
			public readonly TaskCompletionSource<string?> PasswordTcs =
				new(TaskCreationOptions.RunContinuationsAsynchronously);
		}

		private static readonly Dictionary<string, PendingHandshake> PendingPasswordSessions = new();

		/// <summary>玩家通过 /跨服密码 <密码> 提交进服密码（解除挂起的握手）</summary>
		public static void SubmitPassword(string playerName, string password)
		{
			lock (PendingPasswordSessions)
			{
				if (PendingPasswordSessions.TryGetValue(playerName, out var s))
					s.PasswordTcs.TrySetResult(password);
			}
		}

		/// <summary>热重载/卸载时清理挂起的进服密码会话（解除等待，握手自然失败退出）</summary>
		public static void Reset()
		{
			lock (PendingPasswordSessions)
			{
				foreach (var kv in PendingPasswordSessions)
					kv.Value.PasswordTcs.TrySetResult(null);
				PendingPasswordSessions.Clear();
			}
			TShock.Log.ConsoleInfo("[CrossTransfer] CrossLoginClient 挂起会话已清理");
		}

		/// <summary>
		/// 完整前置握手（MultiSEngine PreConnectHandler 移植）：
		///   ClientHello → ClientUUID → Auth → AuthAck →
		///   LoadPlayer → SyncIP + PlayerInfo(重放) + RequestWorldInfo →
		///   WorldData → RequestTileData + SpawnPlayer → … → FinishedConnectingToServer
		/// 期间目标服发来的所有包（WorldData/tile 数据等）缓存到 BufferedPackets。
		/// </summary>
		public static async Task<HandshakeResult> FullHandshakeAsync(
			TransferServerInfo server, string playerName, string uuid, string realIP,
			byte[]? playerInfoFrame, string? serverPassword,
			Action<string>? notify = null, int timeoutMs = 15000)
		{
			var client = new TcpClient();
			try
			{
				using var cts = new CancellationTokenSource(timeoutMs);
				await client.ConnectAsync(server.IP, server.Port, cts.Token);
			}
			catch (Exception ex)
			{
				client.Dispose();
				return new HandshakeResult { Ok = false, Reason = $"无法连接 {server.IP}:{server.Port}: {ex.Message}" };
			}

			var stream = client.GetStream();
			var result = new HandshakeResult { Connection = client };
			try
			{
				// 1) ClientHello（协议版本）
				var version = server.VersionNum > 0 ? $"Terraria{server.VersionNum}" : "Terraria319";
				await SendPacketAsync(stream, bw =>
				{
					bw.Write((byte)1);
					bw.Write(version);
				});

				// 2) ClientUUID(68)：照 MultiSEngine PreConnectAdapter——连接后立即发（不等待 LoadPlayer）
				if (!string.IsNullOrEmpty(uuid))
				{
					await SendPacketAsync(stream, bw =>
					{
						bw.Write((byte)68);
						bw.Write(uuid);
					});
				}

				// 3) Auth：密钥鉴权（B 服验签后建立 preTransfer；槽位在 ClientHello 后已分配，whoAmI 有效）
				var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
				var nonce = Guid.NewGuid().ToString("N");
				var signInput = TransferProtocol.BuildAuthSignInput(
					CrossTransfer.Config.SelfServerId, ts, nonce, playerName, uuid, realIP);
				var sig = WebhookAuth.HmacSha256Hex(CrossTransfer.Config.SelfSecret, signInput);
				await SendPacketAsync(stream, bw =>
				{
					bw.Write(TransferProtocol.CustomPacketId);
					bw.Write(TransferProtocol.AuthPacket);
					bw.Write(CrossTransfer.Config.SelfServerId);
					bw.Write(playerName);
					bw.Write(uuid);
					bw.Write(realIP);
					bw.Write(ts);
					bw.Write(nonce);
					bw.Write(sig);
				});

				// 4) 读循环
				var reader = new PacketReader(stream);
				var authed = false;
				while (true)
				{
					var (readOk, body, err) = await reader.ReadPacketAsync(timeoutMs);
					if (!readOk)
						return Fail(client, result, err ?? "握手超时");

						var type = body[0];
						// 日志只打关键包（排除 NetModule(82)/TileSection(10) 洪水），避免拖慢握手
						if (DebugLog && type != 82 && type != 10)
						{
							var desc = DescribePacket(type, body);
							TShock.Log.ConsoleInfo(
								$"[CrossTransfer][握手] {playerName} → {server.Name}: 收到 type={type} len={body.Length} {desc}");
						}

					// 缓存目标服数据帧（自定义包 15 除外，那是控制通道，不重放给玩家）
					if (type != TransferProtocol.CustomPacketId)
						result.BufferedPackets.Add(BuildFrame(body));

					using var ms = new MemoryStream(body, 1, body.Length - 1);
					using var br = new BinaryReader(ms, Encoding.UTF8);

					switch (type)
					{
					case 3: // LoadPlayer（TShock 视角 ContinueConnecting）：[byte slot][bool]
						result.RemoteSlot = br.ReadByte();

						// 1) PlayerInfo(4)：重放玩家进入本服时的原始角色数据（首字段改为目标槽位）
						if (playerInfoFrame is { Length: > 3 })
						{
							var frame = (byte[])playerInfoFrame.Clone();
							frame[3] = (byte)result.RemoteSlot;
							if (DebugLog)
								TShock.Log.ConsoleInfo($"[CrossTransfer][握手] 发送 PlayerInfo 帧 len={frame.Length}");
							await stream.WriteAsync(frame);
						}

						// 2) SyncIP：指定真实 IP（自定义包 15，B 服 detour 处理）
						await SendPacketAsync(stream, bw =>
						{
							bw.Write(TransferProtocol.CustomPacketId);
							bw.Write(TransferProtocol.SyncIPPacket);
							bw.Write(playerName);
							bw.Write(realIP);
						});

						// 3) ContinueConnecting2(6)（TShock 视角）= RequestWorldInfo：触发 TShock 登录检查
						await SendPacketAsync(stream, bw => bw.Write((byte)6));
						break;

					case 7: // WorldData：照 MultiSEngine PreConnectHandler 请求 tile 数据 + SpawnPlayer
						// 解析出生点（body: [7][maxX int][maxY int][spawnX int][spawnY int]…）
						int spawnX = 0, spawnY = 0;
						try
						{
							if (body.Length >= 17)
							{
								spawnX = BitConverter.ToInt32(body, 9);
								spawnY = BitConverter.ToInt32(body, 13);
							}
						}
						catch { }

						// RequestTileData(8)：请求 tile 数据
						await SendPacketAsync(stream, bw =>
						{
							bw.Write((byte)8);
							bw.Write(-1); // 全图
							bw.Write(-1);
							bw.Write((byte)0);
						});

						// SpawnPlayer(12)：MultiSEngine 在 WorldData 后发送，进入世界
						await SendPacketAsync(stream, bw =>
						{
							bw.Write((byte)12);
							bw.Write((byte)result.RemoteSlot);
							bw.Write((short)spawnX);
							bw.Write((short)spawnY);
							bw.Write((int)0); // timer
							bw.Write((int)0); // deathsPVE
							bw.Write((int)0); // deathsPVP
							bw.Write((byte)0); // PlayerSpawnContext
						});
						break;

						case 37: // RequestPassword：目标服原生进服密码（ServerPassword，协议 37/38）
							// 优先：配置预填的密码自动代发
							if (!string.IsNullOrEmpty(serverPassword))
							{
								await SendPacketAsync(stream, bw =>
								{
									bw.Write((byte)38);
									bw.Write(serverPassword);
								});
								break;
							}

							// 否则：探测 → 要求玩家在 A 服输入（/跨服密码 <密码>），超时 60s
							notify?.Invoke(
								$"[跨服] {server.Name} 需要进服密码，请在 60 秒内输入: /跨服密码 <密码>");
							var ph = new PendingHandshake { PlayerName = playerName };
							lock (PendingPasswordSessions) PendingPasswordSessions[playerName] = ph;
							try
							{
								var pwd = await ph.PasswordTcs.Task.WaitAsync(TimeSpan.FromSeconds(60));
								if (string.IsNullOrEmpty(pwd))
									return Fail(client, result, "未收到密码输入");
								await SendPacketAsync(stream, bw =>
								{
									bw.Write((byte)38);
									bw.Write(pwd);
								});
							}
							catch (TimeoutException)
							{
								return Fail(client, result, "输入密码超时");
							}
							finally
							{
								lock (PendingPasswordSessions) PendingPasswordSessions.Remove(playerName);
							}
							break;

						case 2: // Kick
						{
							var kickReason = SafeKickReason(body);
							return Fail(client, result,
								$"{server.Name} 拒绝连接{(kickReason.Length > 0 ? $": {kickReason}" : "")}");
						}

						case 129: // FinishedConnectingToServer：握手完成
							result.Ok = authed;
							result.Reason = authed ? "握手完成" : "未通过鉴权";
							return result;

						case TransferProtocol.CustomPacketId: // 15：自定义包
							var pktName = br.ReadString();
							if (pktName == TransferProtocol.AuthAckPacket)
							{
								var ok = br.ReadBoolean();
								var msg = br.ReadString();
								if (ok) authed = true;
								else return Fail(client, result, msg);
							}
							break;

						default:
							break;
					}
				}
			}
			catch (Exception ex)
			{
				return Fail(client, result, $"握手异常: {ex.Message}");
			}
		}

		/// <summary>给 body 加 [ushort 整帧总长] 前缀，得到完整帧（Terraria 长度含 2 字节前缀）</summary>
		private static byte[] BuildFrame(byte[] body)
		{
			var total = body.Length + 2;
			var frame = new byte[total];
			frame[0] = (byte)(total & 0xFF);
			frame[1] = (byte)((total >> 8) & 0xFF);
			Buffer.BlockCopy(body, 0, frame, 2, body.Length);
			return frame;
		}

		private static HandshakeResult Fail(TcpClient client, HandshakeResult result, string reason)
		{
			try { client.Dispose(); } catch { }
			result.Ok = false;
			result.Reason = reason;
			result.Connection = null;
			return result;
		}

		/// <summary>Kick 包安全解析：[short playerId][byte mode][string?]</summary>
		private static string SafeKickReason(byte[] body)
		{
			try
			{
				using var ms = new MemoryStream(body, 1, body.Length - 1);
				using var br = new BinaryReader(ms, Encoding.UTF8);
				br.ReadInt16(); // playerId
				var mode = br.ReadByte();
				if (mode == 0) return br.ReadString(); // Literal
			}
			catch { }
			return "";
		}

		/// <summary>收到包的简要描述（诊断用）</summary>
		private static string DescribePacket(byte type, byte[] body)
		{
			try
			{
				switch (type)
				{
					case 2:
						return $"Kick: {SafeKickReason(body)}";
					case 3:
						return $"LoadPlayer slot={body[1]}";
					case 7:
						return "WorldData";
					case 9:
						return "StatusText";
					case 12:
						return "SpawnPlayer";
					case 49:
						return "StartPlaying";
					case 129:
						return "FinishedConnectingToServer";
					case 37:
						return "RequestPassword";
					case 38:
						return "SendPassword";
					case 68:
						return $"ClientUUID: {SafeReadString(body, 1)}";
					case 82:
						if (body.Length >= 3)
							return $"NetModule 子类型={BitConverter.ToInt16(body, 1)}";
						break;
					case 15:
						return $"自定义包: {SafeReadString(body, 1)}";
				}
			}
			catch { }
			return "";
		}

		/// <summary>从 body 的 offset 安全读取 string（越界返回 ""）</summary>
		private static string SafeReadString(byte[] body, int offset)
		{
			try
			{
				using var ms = new MemoryStream(body, offset, body.Length - offset);
				using var br = new BinaryReader(ms, Encoding.UTF8);
				return br.ReadString();
			}
			catch { return ""; }
		}

		private static async Task SendPacketAsync(NetworkStream s, Action<BinaryWriter> writeBody)
		{
			var bytes = TransferProtocol.EncodePacket(writeBody);
			if (DebugLog)
				TShock.Log.ConsoleInfo($"[CrossTransfer][握手] 发送 len={bytes.Length} 类型={(bytes.Length > 2 ? bytes[2].ToString() : "?")}");
			await s.WriteAsync(bytes);
		}

		/// <summary>逐包读取 [ushort len][body...]（处理 TCP 粘包）</summary>
		private sealed class PacketReader
		{
			private readonly NetworkStream _s;
			private readonly byte[] _lenBuf = new byte[2];

			public PacketReader(NetworkStream s) => _s = s;

			public async Task<(bool ok, byte[] body, string? err)> ReadPacketAsync(int timeoutMs)
			{
				using var cts = new CancellationTokenSource(timeoutMs);
				try
				{
					await ReadExactlyAsync(_lenBuf, 2, cts.Token);
					// Terraria 长度字段 = 整帧总长（含 2 字节长度前缀），body 长度 = total - 2
					var totalLen = _lenBuf[0] | (_lenBuf[1] << 8);
					if (totalLen < 3 || totalLen > 0xFFFF)
					{
						TShock.Log.ConsoleWarn(
							$"[CrossTransfer][握手] 非法包长度 {totalLen}，长度字节: {BitConverter.ToString(_lenBuf)}");
						return (false, Array.Empty<byte>(), $"非法包长度 {totalLen}");
					}
					var body = new byte[totalLen - 2];
					await ReadExactlyAsync(body, totalLen - 2, cts.Token);
					return (true, body, null);
				}
				catch (OperationCanceledException) { return (false, Array.Empty<byte>(), "握手超时"); }
				catch (Exception ex) { return (false, Array.Empty<byte>(), $"连接中断: {ex.Message}"); }
			}

			private async Task ReadExactlyAsync(byte[] buf, int count, CancellationToken ct)
			{
				int read = 0;
				while (read < count)
				{
					var n = await _s.ReadAsync(buf.AsMemory(read, count - read), ct);
					if (n <= 0) throw new EndOfStreamException();
					read += n;
				}
			}
		}
	}
}
