using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using MonoMod.RuntimeDetour;
using Newtonsoft.Json;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;

namespace TShockData
{
	/// <summary>目标服务器配置（每服一份，按对端 Name 匹配共享密钥）</summary>
	public class TransferServerInfo
	{
		[JsonProperty("名称")] public string Name { get; set; } = "";
		[JsonProperty("地址")] public string IP { get; set; } = "127.0.0.1";
		[JsonProperty("端口")] public int Port { get; set; } = 7777;
		[JsonProperty("协议版本")] public int VersionNum { get; set; } = 319;
		[JsonProperty("共享密钥")] public string Secret { get; set; } = "";
		[JsonProperty("进服密码(可选)")] public string? Password { get; set; }
	}

	/// <summary>跨服传送配置：{TShock.SavePath}/TSWeb/CrossTransfer.json</summary>
	public class CrossTransferConfig
	{
		[JsonProperty("本服ID")] public string SelfServerId { get; set; } = "server-a";
		/// <summary>本服密钥：签名时用自己的密钥；其他服在各自配置的对端条目里填这把密钥用于验签</summary>
		[JsonProperty("本服密钥")] public string SelfSecret { get; set; } = "";
		[JsonProperty("目标服务器列表")] public System.Collections.Generic.List<TransferServerInfo> Servers { get; set; } = new();
	}

	/// <summary>
	/// 跨服传送（纯插件，阶段 0：控制通道地基）。
	/// - 自定义包通道：Unused15(15)，ServerApi.Hooks.NetGetData 最高优先级（int.MinValue）
	///   在 TShock.OnGetData 之前完全接管（原 MonoMod detour private 方法在本环境不触发）
	/// - Auth 密钥鉴权：HMAC-SHA256（算法复用 WebhookAuth）+ 时间窗 + nonce 去重
	/// - preTransfer：目标服记录受信连接（whoAmI → 玩家/UUID/真实IP），供后续握手放行与 SyncIP 使用
	/// - 命令：/跨服 <服务器名> [进服密码]、/返回（回程占位）
	/// </summary>
	public static class CrossTransfer
	{
		public const string Permission = "tools.crosstransfer";

		public static CrossTransferConfig Config { get; private set; } = new();
		public static string ConfigPath => Path.Combine(TShock.SavePath, "TSWeb", "CrossTransfer.json");

		private static bool _initialized;
		private static TerrariaPlugin? _plugin;
		private static Hook? _getDataDetour;

		// 玩家进入本服握手期发送的 PlayerInfo 原始帧缓存（whoAmI → 完整帧），跨服重放给目标服
		private static readonly Dictionary<int, byte[]> PlayerInfoFrames = new();

		public static void Initialize(TerrariaPlugin plugin)
		{
			if (_initialized) return;
			_initialized = true;
			_plugin = plugin;

			LoadConfig();

			// ═══ GetData 钩子：捕获玩家首次进入的 PlayerInfo 原始帧（跨服重放用）═══
			ServerApi.Hooks.NetGetData.Register(plugin, OnNetGetData);

			// ═══ 最高优先级 GetData 事件：在 TShock.OnGetData 之前完全接管包 68/15 ═══
			// 原 MonoMod detour TShock.OnGetData（private 方法）在本环境注册成功但 detour 不触发，
			// 68 被 TShock 早期 State 拦截（AllowedEarlyPackets 不含 68）→ ClientUUID 永不写入 →
			// OnJoin 的 KickEmptyUUID 踢出。改用 ServerApi.Hooks.NetGetData 的 int.MinValue 事件：
			// 每个包都先于 TShock.OnGetData 执行，处理 68（写原生 ClientUUID）/15（Auth/SyncIP）
			// 后 e.Handled=true，TShock 与原版底层自动跳过，效果等同 detour 且不依赖 MonoMod。
			ServerApi.Hooks.NetGetData.Register(plugin, OnCrossTransferGetData, int.MinValue);

			// ═══ 最外层 detour：MessageBuffer.GetData（Terraria 解析客户端包唯一总入口）═══
			// 比 OTAPI 钩子更外层：桥接玩家在此层直接转发并 return（不调用 orig），
			// 彻底切断 OTAPI 事件 → TSAPI 事件 → TShock/所有插件（BugFixes 等）处理链，
			// 任何检测都碰不到跨服玩家的包。非桥接玩家走 orig，握手期 68/15 仍由 OTAPI 钩子处理。
			// 1.4.4.9 与 1.4.5 的 GetData 签名不同（2 参 / 3 参带 out），运行时自适应。
			try
			{
				var mi2 = typeof(MessageBuffer).GetMethod("GetData",
					BindingFlags.Public | BindingFlags.Instance, null,
					new[] { typeof(int), typeof(int) }, null);
				if (mi2 != null)
				{
					_getDataDetour = new Hook(mi2, new DetourGetData2(OnGetDataDetour2));
					TShock.Log.ConsoleInfo("[CrossTransfer] GetData detour 已挂载（2 参签名）");
				}
				else
				{
					var mi3 = typeof(MessageBuffer).GetMethod("GetData",
						BindingFlags.Public | BindingFlags.Instance, null,
						new[] { typeof(int), typeof(int), typeof(int).MakeByRefType() }, null);
					if (mi3 != null)
					{
						_getDataDetour = new Hook(mi3, new DetourGetData3(OnGetDataDetour3));
						TShock.Log.ConsoleInfo("[CrossTransfer] GetData detour 已挂载（3 参签名）");
					}
					else
						TShock.Log.ConsoleError("[CrossTransfer] 未找到 MessageBuffer.GetData，桥接包转发不可用");
				}
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[CrossTransfer] GetData detour 注册失败: {ex}");
			}

			// ═══ 最底层钩子：OTAPI.MessageBuffer.GetData（即 MonoMod detour Terraria 原生
			//  MessageBuffer.GetData —— 客户端包解析唯一总入口，含握手期所有包，PacketCatch/Omni 同款）。
			//  无论 TShock 事件系统是否触发、TShock 是否已处理，这里都独立拦截 68/15。
			//  取消时同时置 Result=Cancel 与 PacketId=255（Omni 技巧：TSAPI 不尊重 Result）。
			OTAPI.Hooks.MessageBuffer.GetData += OnMessageBufferGetData;

			// ═══ 出站拦截：玩家桥接期间丢弃 A 服发给他的所有包（防止 A 服旧世界数据污染 B 服世界）═══
			// 桥接的下行数据不走 NetMessage.SendBytes（直接 socket.AsyncSend），天然绕过此钩子。
			OTAPI.Hooks.NetMessage.SendBytes += OnSendBytes;

			// ═══ 玩家断线 → 清理桥接会话 ═══
			ServerApi.Hooks.ServerLeave.Register(plugin, OnServerLeave);

			// ═══ 最高优先级 ServerJoin：在 TShock.OnJoin 的 KickEmptyUUID 检查前写入 UUID（兜底）═══
			ServerApi.Hooks.ServerJoin.Register(plugin, OnCrossTransferJoin, int.MinValue);

			Commands.ChatCommands.Add(new Command(Permission, TransferCommand, "跨服", "crosstransfer"));
			Commands.ChatCommands.Add(new Command(Permission, ReturnCommand, "返回", "ctback"));
			Commands.ChatCommands.Add(new Command(Permission, PasswordCommand, "跨服密码", "ctpass"));

			TShock.Log.ConsoleInfo($"[CrossTransfer] 跨服传送初始化完成（{Config.Servers.Count} 个目标服）");
		}

		public static void Dispose()
		{
			if (!_initialized) return;
			_initialized = false;

			ServerApi.Hooks.NetGetData.Deregister(_plugin!, OnNetGetData);
			ServerApi.Hooks.NetGetData.Deregister(_plugin!, OnCrossTransferGetData);
			try { _getDataDetour?.Dispose(); } catch { }
			_getDataDetour = null;
			OTAPI.Hooks.MessageBuffer.GetData -= OnMessageBufferGetData;
			OTAPI.Hooks.NetMessage.SendBytes -= OnSendBytes;
			ServerApi.Hooks.ServerLeave.Deregister(_plugin!, OnServerLeave);
			ServerApi.Hooks.ServerJoin.Deregister(_plugin!, OnCrossTransferJoin);

			PlayerInfoFrames.Clear();

			Commands.ChatCommands.RemoveAll(c => c.Names.Any(n =>
				n.Equals("跨服", StringComparison.OrdinalIgnoreCase) ||
				n.Equals("crosstransfer", StringComparison.OrdinalIgnoreCase) ||
				n.Equals("返回", StringComparison.OrdinalIgnoreCase) ||
				n.Equals("ctback", StringComparison.OrdinalIgnoreCase) ||
				n.Equals("跨服密码", StringComparison.OrdinalIgnoreCase) ||
				n.Equals("ctpass", StringComparison.OrdinalIgnoreCase)));
		}

		// ════════════════════════════════════════════
		//  配置
		// ════════════════════════════════════════════

		public static void LoadConfig()
		{
			try
			{
				var dir = Path.GetDirectoryName(ConfigPath)!;
				Directory.CreateDirectory(dir);
				if (File.Exists(ConfigPath))
				{
					Config = JsonConvert.DeserializeObject<CrossTransferConfig>(File.ReadAllText(ConfigPath))
						?? new CrossTransferConfig();
				}
				else
				{
					Config = new CrossTransferConfig
					{
						SelfServerId = "server-a",
						SelfSecret = "change-me",
						Servers = new System.Collections.Generic.List<TransferServerInfo>
						{
							new TransferServerInfo
							{
								Name = "server-b",
								IP = "127.0.0.1",
								Port = 7778,
								VersionNum = 319,
								Secret = "change-me"
							}
						}
					};
					SaveConfig();
					TShock.Log.ConsoleInfo($"[CrossTransfer] 已生成默认配置 {ConfigPath}，请编辑后 /reload");
				}
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[CrossTransfer] 配置加载失败: {ex}");
			}
		}

		public static void SaveConfig()
		{
			try
			{
				var dir = Path.GetDirectoryName(ConfigPath)!;
				Directory.CreateDirectory(dir);
				File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(Config, Formatting.Indented));
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[CrossTransfer] 配置保存失败: {ex}");
			}
		}

		// ════════════════════════════════════════════
		//  最高优先级 GetData 事件：TShock.OnGetData 之前完全接管包 68/15
		// ════════════════════════════════════════════

		private static void OnCrossTransferGetData(GetDataEventArgs e)
		{
			if (e.Handled) return;

			// 桥接中的玩家：所有上行包已由最底层钩子（OnMessageBufferGetData）转发目标服并取消，
			// 这里兜底跳过，防止 A 服 TShock 继续处理其包（幽灵操作/误判）。
			if (Bridges.ContainsKey(e.Msg.whoAmI))
			{
				e.Handled = true;
				return;
			}

			// B 服：preTransfer 连接的 PlayerInfo → 先保障账号（UUID 绑定），让 TShock 自动登录
			if (e.MsgID == PacketTypes.PlayerInfo && TransferProtocol.PreTransfers.ContainsKey(e.Msg.whoAmI))
				TransferProtocol.EnsureAccount(e.Msg.whoAmI);

			// B 服：ClientUUID(68) 会被 TShock 早期 State 拦截
			// （68 不在 AllowedEarlyPackets → e.Handled=true → Terraria 底层跳过），
			// 导致 RemoteClient.ClientUUID 从未写入 → OnJoin 的 KickEmptyUUID 检查踢出。
			// 这里【不依赖 preTransfer/Auth 时序】总是解析 uuid 并写入 Terraria 原生字段。
			if (e.MsgID == PacketTypes.ClientUUID)
			{
				try
				{
					var who = e.Msg.whoAmI;
					if (who >= 0 && who < Netplay.Clients.Length && e.Length > 1)
					{
						using var ms = new MemoryStream(e.Msg.readBuffer, e.Index, e.Length - 1);
						using var br = new BinaryReader(ms);
						var uuid = br.ReadString();
						if (!string.IsNullOrEmpty(uuid))
						{
							TransferProtocol.SetRemoteClientUUID(who, uuid);
							if (TransferProtocol.PreTransfers.TryGetValue(who, out var pre)) pre.UUID = uuid;
							TShock.Log.ConsoleInfo($"[CrossTransfer] ClientUUID 已写入 slot#{who}: {uuid}");
							e.Handled = true;
							return;
						}
					}
				}
				catch (Exception ex)
				{
					TShock.Log.ConsoleWarn($"[CrossTransfer] ClientUUID 处理失败: {ex.Message}");
				}
			}

			// 完全接管包 15（自定义控制通道）
			if (e.MsgID == (PacketTypes)TransferProtocol.CustomPacketId)
			{
				try
				{
					if (TransferProtocol.HandleInbound(e.Msg.whoAmI, e.Msg.readBuffer, e.Index, e.Length))
					{
						e.Handled = true;
						return; // 已消费，TShock 不再处理
					}
				}
				catch (Exception ex)
				{
					TShock.Log.ConsoleError($"[CrossTransfer] 包15处理异常: {ex}");
				}
			}
		}

		// ════════════════════════════════════════════
		//  最底层钩子：OTAPI.MessageBuffer.GetData（MonoMod detour MessageBuffer.GetData）
		//  独立于 TShock 事件系统 —— 无论 TShock 是否处理过，都先于原版 case 拦截 68/15。
		// ════════════════════════════════════════════

		private static void OnMessageBufferGetData(object? sender, OTAPI.Hooks.MessageBuffer.GetDataEventArgs args)
		{
			try
			{
				var buf = args.Instance?.readBuffer;
				if (buf == null) return;
				int off = args.ReadOffset;
				int len = args.Length;
				if (off <= 0 || len <= 0 || off > buf.Length || len > buf.Length - off) return;

				// 真实包 ID：payload 前一字节（args.PacketId 可能被其他插件改写为 255）
				byte id = buf[off - 1];

				// ═══ 桥接：玩家已传送 → 所有上行包（含 PlayerUpdate/聊天/交互）原样转发目标服 ═══
				if (Bridges.TryGetValue(args.Instance.whoAmI, out var bridge))
				{
					ForwardToTarget(bridge, buf, off, len);
					CancelPacket(args);
					return;
				}

				// B 服：ClientUUID(68) → 写入 Terraria 原生 ClientUUID
				if (id == (byte)PacketTypes.ClientUUID)
				{
					var who = args.Instance.whoAmI;
					if (who >= 0 && who < Netplay.Clients.Length && len > 1)
					{
						using var ms = new MemoryStream(buf, off, len - 1);
						using var br = new BinaryReader(ms);
						var uuid = br.ReadString();
						if (!string.IsNullOrEmpty(uuid))
						{
							TransferProtocol.SetRemoteClientUUID(who, uuid);
							if (TransferProtocol.PreTransfers.TryGetValue(who, out var pre)) pre.UUID = uuid;
							TShock.Log.ConsoleInfo($"[CrossTransfer] ClientUUID 已写入 slot#{who}: {uuid}");
							CancelPacket(args);
							return;
						}
					}
				}

				// B 服：自定义控制通道（Auth/SyncIP）
				if (id == TransferProtocol.CustomPacketId)
				{
					if (TransferProtocol.HandleInbound(args.Instance.whoAmI, buf, off, len))
					{
						CancelPacket(args);
						return;
					}
				}
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[CrossTransfer] 底层钩子处理异常: {ex}");
			}
		}

		/// <summary>取消包：TSAPI 不尊重 Result，必须同时把 PacketId 改成 255（Omni 同款技巧）</summary>
		private static void CancelPacket(OTAPI.Hooks.MessageBuffer.GetDataEventArgs args)
		{
			args.Result = OTAPI.HookResult.Cancel;
			args.PacketId = byte.MaxValue;
		}

		// ════════════════════════════════════════════
		//  最外层 detour：MessageBuffer.GetData（1.4.4.9 两参 / 1.4.5 三参自适应）
		//  桥接玩家 → 转发 + return（不调 orig）→ 整条处理链（OTAPI/TSAPI/插件）被切断
		// ════════════════════════════════════════════

		private delegate void OrigGetData2(MessageBuffer self, int start, int length);
		private delegate void DetourGetData2(OrigGetData2 orig, MessageBuffer self, int start, int length);
		private delegate void OrigGetData3(MessageBuffer self, int start, int length, out int messageType);
		private delegate void DetourGetData3(OrigGetData3 orig, MessageBuffer self, int start, int length, out int messageType);

		private static void OnGetDataDetour2(OrigGetData2 orig, MessageBuffer self, int start, int length)
		{
			if (ForwardBridgeIfNeeded(self, start, length)) return;
			orig(self, start, length);
		}

		private static void OnGetDataDetour3(OrigGetData3 orig, MessageBuffer self, int start, int length, out int messageType)
		{
			if (ForwardBridgeIfNeeded(self, start, length))
			{
				messageType = 0;
				return;
			}
			orig(self, start, length, out messageType);
		}

		/// <summary>
		/// 桥接玩家：把完整帧（补 2 字节长度头）转发目标服，返回 true = 已消费（调用方不得调 orig）。
		/// start 指向 msgType，length 含 msgType（GetData 语义），payload = length - 1。
		/// </summary>
		private static bool ForwardBridgeIfNeeded(MessageBuffer self, int start, int length)
		{
			try
			{
				if (!Bridges.TryGetValue(self.whoAmI, out var bridge)) return false;
				var buf = self.readBuffer;
				if (buf == null || start < 0 || start >= buf.Length) return false;
				if (length < 1 || start + length > buf.Length) return false;

				int total = length + 2;
				if (total < 3 || total > 0xFFFF) return false;
				var frame = new byte[total];
				frame[0] = (byte)(total & 0xFF);
				frame[1] = (byte)((total >> 8) & 0xFF);
				frame[2] = buf[start];
				if (length > 1)
					Buffer.BlockCopy(buf, start + 1, frame, 3, length - 1);

				var s = bridge.Stream;
				if (s == null) return false;
				lock (bridge.WriteLock)
				{
					try { s.Write(frame, 0, total); }
					catch (Exception ex) { TShock.Log.ConsoleWarn($"[CrossTransfer] 上行转发失败: {ex.Message}"); }
				}
				return true;
			}
			catch { return false; }
		}

		// ════════════════════════════════════════════
		//  阶段 8：桥接（玩家连接无缝切换到目标服）
		//  ┌─ 玩家客户端（A 服 socket）
		//  │   上行：MessageBuffer.GetData 钩子 → ForwardToTarget → B 服模拟连接
		//  │   下行：B 服读循环 → 玩家 socket.AsyncSend（直发，绕开 SendBytes 钩子）
		//  │   A 服旧世界下行：SendBytes 钩子拦截丢弃（防止污染 B 服世界视图）
		//  └─ B 服模拟连接（CrossLoginClient 握手后的 TcpClient）
		// ════════════════════════════════════════════

		/// <summary>桥接会话（玩家索引 → 会话）</summary>
		private sealed class BridgeSession
		{
			public int PlayerIndex;
			public string PlayerName = "";
			public string UUID = "";
			public string RealIP = "";
			public byte[]? PlayerInfoFrame;
			public string? CurrentServerName;   // 当前桥接目标服名（用于判断"你已在该服务器"）
			public TcpClient? Target;
			public NetworkStream? Stream;
			public CancellationTokenSource Cts = new();
			public Task? ReadLoop;
			public bool Switching;              // 切换中：旧读循环 finally 不得清理桥接
			public readonly object WriteLock = new();
		}

		private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, BridgeSession> Bridges = new();

		/// <summary>玩家是否处于跨服桥接中（供其他模块豁免处理）</summary>
		public static bool IsBridging(int who) => Bridges.ContainsKey(who);

		/// <summary>启动桥接：握手成功后调用。把玩家的网络流切换到目标服模拟连接。</summary>
		private static void StartBridge(TSPlayer player, CrossLoginClient.HandshakeResult result, TransferServerInfo server, byte[]? playerInfoFrame)
		{
			if (Bridges.ContainsKey(player.Index))
			{
				player.SendErrorMessage("[跨服] 你已在跨服桥接中，请勿重复传送");
				return;
			}
			if (result.Connection == null || !result.Connection.Connected)
			{
				player.SendErrorMessage($"[跨服] 目标服连接已失效，传送失败");
				return;
			}

			var session = new BridgeSession
			{
				PlayerIndex = player.Index,
				PlayerName = player.Name,
				UUID = player.UUID,
				RealIP = player.IP,
				PlayerInfoFrame = playerInfoFrame,
				CurrentServerName = server.Name,
				Target = result.Connection,
				Stream = result.Connection.GetStream()
			};

			// 提示必须先于桥接激活发送（激活后 A 服→玩家的消息会被出站拦截钩子丢弃）
			player.SendSuccessMessage($"[跨服] 已传送到 {server.Name}，正在加载目标世界…");

			Bridges[player.Index] = session;

			// ═══ 玩家从 A 服"消失"（socket 保留用于桥接，实体/在线列表移除）═══
			// 1) 广播 PlayerActive(69, active=false)：A 服其他玩家看到该玩家下线
			// 2) A 服世界实体移除
			// 3) TShock 在线列表移除（后续其上行包由桥接钩子转发 + 事件兜底跳过）
			try
			{
				NetMessage.TrySendData(69, -1, player.Index, null, player.Index, 0);
				Main.player[player.Index].active = false;
				TShock.Players[player.Index] = null;
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleWarn($"[CrossTransfer] 移除 A 服玩家实体失败: {ex.Message}");
			}

			// 1) 重放握手期缓存的 B 服世界数据快照（WorldInfo/tile/NPC/玩家等）
			//    过滤握手控制包：客户端游戏中收到 LoadPlayer(3)/RequestPassword(37)/
			//    FinishedConnecting(129) 会重置 myPlayer / 重新进入握手流程，造成状态错乱。
			foreach (var frame in result.BufferedPackets)
			{
				if (frame.Length >= 3)
				{
					byte t = frame[2];
					if (t == 3 || t == 37 || t == 129) continue;
				}
				SendToPlayerSocket(player.Index, frame);
			}

			// 2) 启动 B 服 → 玩家 下行读循环
			session.ReadLoop = Task.Run(() => BridgeReadLoop(session));

			TShock.Log.ConsoleInfo($"[CrossTransfer] 玩家 {player.Name} 桥接已启动 → {server.Name}（slot#{result.RemoteSlot}，重放 {result.BufferedPackets.Count} 包）");
		}

		/// <summary>上行：玩家 → 目标服。把原始帧（补 2 字节长度头）转发到 B 服模拟连接。</summary>
		private static void ForwardToTarget(BridgeSession bridge, byte[] buf, int off, int len)
		{
			// len = msgId(1) + payload；完整帧 = [2字节总长][msgId][payload]
			int total = len + 2;
			var frame = new byte[total];
			frame[0] = (byte)(total & 0xFF);
			frame[1] = (byte)((total >> 8) & 0xFF);
			frame[2] = buf[off - 1];
			Buffer.BlockCopy(buf, off, frame, 3, len - 1);
			var s = bridge.Stream;
			if (s == null) return;
			lock (bridge.WriteLock)
			{
				try { s.Write(frame, 0, total); }
				catch (Exception ex) { TShock.Log.ConsoleWarn($"[CrossTransfer] 上行转发失败: {ex.Message}"); }
			}
		}

		/// <summary>下行：目标服 → 玩家。读 B 服连接完整帧，直发玩家 socket。</summary>
		private static void BridgeReadLoop(BridgeSession bridge)
		{
			var s = bridge.Stream;
			if (s == null) return;
			var lenBuf = new byte[2];
			try
			{
				while (!bridge.Cts.IsCancellationRequested)
				{
					if (!ReadExactly(s, lenBuf, 0, 2, bridge.Cts.Token)) break;
					int total = lenBuf[0] | (lenBuf[1] << 8);
					if (total < 3 || total > 0xFFFF) break;
					var frame = new byte[total];
					frame[0] = lenBuf[0];
					frame[1] = lenBuf[1];
					if (!ReadExactly(s, frame, 2, total - 2, bridge.Cts.Token)) break;

					// B 服自定义控制包（15）：截获处理（返回等），不转发给玩家客户端
					if (frame.Length >= 3 && frame[2] == TransferProtocol.CustomPacketId)
					{
						HandleTargetControlPacket(bridge, frame);
						continue;
					}

					SendToPlayerSocket(bridge.PlayerIndex, frame);
				}
			}
			catch (OperationCanceledException) { }
			catch (Exception ex)
			{
				TShock.Log.ConsoleWarn($"[CrossTransfer] 桥接读循环结束: {ex.Message}");
			}
			finally
			{
				// 主动切换目标服时（Switching=true）旧读循环退出不清理，由切换逻辑接管
				if (!bridge.Switching)
					CleanupBridge(bridge.PlayerIndex);
			}
		}

		/// <summary>从流中读取恰好 count 字节（支持取消）。false = 连接断开/取消。</summary>
		private static bool ReadExactly(NetworkStream s, byte[] buf, int offset, int count, CancellationToken ct)
		{
			int read = 0;
			while (read < count)
			{
				int n = s.Read(buf, offset + read, count - read);
				if (n <= 0) return false;
				read += n;
			}
			return true;
		}

		/// <summary>截获 B 服自定义控制包（15 号通道）：处理二次传送请求/返回指令</summary>
		private static void HandleTargetControlPacket(BridgeSession bridge, byte[] frame)
		{
			try
			{
				// frame = [len低][len高][15][string 包名][payload...]
				using var ms = new MemoryStream(frame, 3, frame.Length - 3);
				using var br = new BinaryReader(ms, Encoding.UTF8);
				var name = br.ReadString();
				TShock.Log.ConsoleInfo($"[CrossTransfer] 目标服控制包: {name}");

				if (name == TransferProtocol.TransferRequestPacket)
				{
					// B 服桥接玩家请求二次传送：目标服名 → 判定 → 回 TransferResult
					var target = br.ReadString();
					ProcessTransferRequest(bridge, target);
				}
				else if (name == TransferProtocol.ReturnPacket)
				{
					// 目标服主动请求玩家返回（旧协议保留）
					ReturnToSelf(bridge);
				}
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleWarn($"[CrossTransfer] 控制包解析失败: {ex.Message}");
			}
		}

		/// <summary>
		/// 源服（桥接中心）判定 B 服玩家的二次传送请求：
		///   目标=源服自身 → 返回流程；目标=合法可达 C 服 → 切换桥接；否则拒绝。
		/// </summary>
		private static void ProcessTransferRequest(BridgeSession bridge, string target)
		{
			try
			{
				// 目标 = 源服自身 → 返回
				if (target.Equals(Config.SelfServerId, StringComparison.OrdinalIgnoreCase))
				{
					SendTransferResult(bridge, true, "正在返回源服…");
					ReturnToSelf(bridge);
					return;
				}

				// 已在目标服？
				if (bridge.CurrentServerName != null
					&& bridge.CurrentServerName.Equals(target, StringComparison.OrdinalIgnoreCase))
				{
					SendTransferResult(bridge, false, $"你已在该服务器（{target}）");
					return;
				}

				// 配置检查
				var server = Config.Servers.FirstOrDefault(
					s => s.Name.Equals(target, StringComparison.OrdinalIgnoreCase));
				if (server == null)
				{
					SendTransferResult(bridge, false, $"未配置目标服务器: {target}");
					return;
				}

				// 可达探测
				if (!ProbeReachable(server))
				{
					SendTransferResult(bridge, false, $"无法连接目标服务器 {target}");
					return;
				}

				// 合法 → 允许 + 异步切换桥接到目标服
				SendTransferResult(bridge, true, $"正在传送至 {target}…");
				TShock.Log.ConsoleInfo($"[CrossTransfer] 桥接玩家 {bridge.PlayerName} 请求二次传送 → {target}，判定合法");
				SwitchTarget(bridge, server);
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[CrossTransfer] 传送判定异常: {ex}");
				try { SendTransferResult(bridge, false, $"传送判定异常: {ex.Message}"); } catch { }
			}
		}

		/// <summary>把判定结果回传给目标服（B 服收到后提示玩家）</summary>
		private static void SendTransferResult(BridgeSession bridge, bool ok, string msg)
		{
			try
			{
				var bytes = TransferProtocol.EncodeCustomData(TransferProtocol.TransferResultPacket,
					bw =>
					{
						bw.Write(ok);
						bw.Write(msg ?? "");
					});
				var s = bridge.Stream;
				if (s == null) return;
				lock (bridge.WriteLock)
				{
					try { s.Write(bytes, 0, bytes.Length); } catch { }
				}
			}
			catch { }
		}

		/// <summary>可达探测：尝试 TCP 连接目标服端口（1500ms 超时）</summary>
		private static bool ProbeReachable(TransferServerInfo server)
		{
			try
			{
				using var c = new TcpClient();
				var t = c.ConnectAsync(server.IP, server.Port);
				return t.Wait(1500) && c.Connected;
			}
			catch { return false; }
		}

		/// <summary>切换桥接到新目标服：停旧连接 → 新握手 → 重放世界 → 新读循环</summary>
		private static void SwitchTarget(BridgeSession bridge, TransferServerInfo server)
		{
			int who = bridge.PlayerIndex;
			bridge.Switching = true;

			Task.Run(async () =>
			{
				try
				{
					// 1) 停旧读循环 + 断开旧连接（旧读循环 finally 因 Switching=true 不清理）
					bridge.Switching = true;
					bridge.Cts.Cancel();
					try { bridge.Target?.Dispose(); } catch { }
					bridge.Stream = null;
					// 等旧读循环退出（旧连接 Dispose 后同步 Read 必然抛异常立即退出），
					// 避免其 finally 在 Switching 复位后才执行造成误清理
					try { bridge.ReadLoop?.Wait(1500); } catch { }

					// 2) 新握手（复用与首次传送完全相同的 CrossLoginClient 流程）
					var result = await CrossLoginClient.FullHandshakeAsync(
						server, bridge.PlayerName, bridge.UUID, bridge.RealIP,
						bridge.PlayerInfoFrame, server.Password, msg => { });
					if (!result.Ok || result.Connection == null)
					{
						TShock.Log.ConsoleError($"[CrossTransfer] 切换到 {server.Name} 失败: {result.Reason}");
						CleanupBridge(who);  // 断开玩家，避免幽灵
						return;
					}

					// 3) 更新会话
					bridge.Target = result.Connection;
					bridge.Stream = result.Connection.GetStream();
					bridge.Cts = new CancellationTokenSource();
					bridge.CurrentServerName = server.Name;
					bridge.Switching = false;

					// 4) 重放新世界数据（过滤握手控制包）
					foreach (var frame in result.BufferedPackets)
					{
						if (frame.Length >= 3)
						{
							byte t = frame[2];
							if (t == 3 || t == 37 || t == 129) continue;
						}
						SendToPlayerSocket(who, frame);
					}

					// 5) 新读循环
					bridge.ReadLoop = Task.Run(() => BridgeReadLoop(bridge));

					TShock.Log.ConsoleInfo($"[CrossTransfer] 桥接已切换到 {server.Name}（玩家 {bridge.PlayerName}）");
				}
				catch (Exception ex)
				{
					TShock.Log.ConsoleError($"[CrossTransfer] 切换桥接异常: {ex}");
					try { CleanupBridge(who); } catch { }
				}
			});
		}

		/// <summary>返回源服：恢复玩家可见性与 TShock 状态，重发源服世界数据（无缝切回）</summary>
		private static void ReturnToSelf(BridgeSession bridge)
		{
			int who = bridge.PlayerIndex;
			string pname = bridge.PlayerName;

			// 1) 停桥接（先标记切换中，防旧读循环 finally 清理）
			bridge.Switching = true;
			bridge.Cts.Cancel();
			try { bridge.Target?.Dispose(); } catch { }
			bridge.Stream = null;
			Bridges.TryRemove(who, out _);

			// 2) 恢复玩家在源服的可见性与 TShock 状态（复刻 HandleConnecting 登录成功逻辑）
			try
			{
				Main.player[who].active = true;

				var restored = new TSPlayer(who);
				var acc = TShock.UserAccounts.GetUserAccountByName(pname);
				if (acc != null)
				{
					restored.Account = acc;
					restored.Group = TShock.Groups.GetGroupByName(acc.Group) ?? Group.DefaultGroup;
					restored.IsLoggedIn = true;
					restored.PlayerData = TShock.CharacterDB.GetPlayerData(restored, acc.ID);
					if (Main.ServerSideCharacter && restored.PlayerData?.exists == true)
						restored.PlayerData.RestoreCharacter(restored);
				}
				restored.State = (int)ConnectionState.Complete;
				restored.FinishedHandshake = true;
				TShock.Players[who] = restored;

				// 3) 广播上线 + 重发源服世界数据（客户端重载源服世界，出生点重生）
				NetMessage.TrySendData(69, -1, who, null, who, 1);
				NetMessage.SendData((int)PacketTypes.WorldInfo, who);

				TShock.Log.ConsoleInfo($"[CrossTransfer] 玩家 {pname} 已返回源服（slot#{who}）");
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[CrossTransfer] 返回源服恢复失败: {ex}");
			}
		}

		/// <summary>直发玩家 socket（不走 NetMessage.SendBytes，天然绕过出站拦截钩子）</summary>
		private static void SendToPlayerSocket(int who, byte[] data)
		{
			try
			{
				if (who < 0 || who >= Netplay.Clients.Length) return;
				var sock = Netplay.Clients[who]?.Socket;
				if (sock == null || !sock.IsConnected()) return;
				sock.AsyncSend(data, 0, data.Length, _ => { });
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleWarn($"[CrossTransfer] 向玩家 socket#{who} 发送失败: {ex.Message}");
			}
		}

		/// <summary>出站拦截：玩家桥接期间丢弃 A 服发给他的所有包（旧世界数据不污染 B 服视图）</summary>
		private static void OnSendBytes(object? sender, OTAPI.Hooks.NetMessage.SendBytesEventArgs args)
		{
			if (Bridges.ContainsKey(args.RemoteClient))
				args.Result = OTAPI.HookResult.Cancel;
		}

		/// <summary>玩家断线 → 清理桥接（A 服）与跨服标记（B 服）</summary>
		private static void OnServerLeave(LeaveEventArgs args)
		{
			TransferProtocol.PreTransfers.TryRemove(args.Who, out _); // B 服：清理跨服玩家标记
			CleanupBridge(args.Who);                                  // A 服：清理桥接会话
		}

		/// <summary>清理桥接会话：关闭 B 服连接、取消读循环，并断开玩家在 A 服的残留连接</summary>
		private static void CleanupBridge(int who)
		{
			if (!Bridges.TryRemove(who, out var bridge)) return;
			bridge.Cts.Cancel();
			try { bridge.Target?.Dispose(); } catch { }
			bridge.Stream = null;
			TShock.Log.ConsoleInfo($"[CrossTransfer] 玩家 slot#{who} 桥接已清理");

			// 桥接结束（B 服断开/传送结束）→ 断开玩家在 A 服的连接，避免幽灵玩家
			try
			{
				if (who >= 0 && who < Netplay.Clients.Length)
					Netplay.Clients[who]?.Socket?.Close();
			}
			catch { }
		}

		// ════════════════════════════════════════════
		//  最高优先级 ServerJoin 事件：TShock.OnJoin（KickEmptyUUID 检查点）之前
		// ════════════════════════════════════════════

		/// <summary>
		/// TShock.OnJoin 检查 Config.Settings.KickEmptyUUID && IsNullOrWhiteSpace(player.UUID)
		/// （player.UUID = Netplay.Clients[who].ClientUUID）。preTransfer 连接由模拟客户端代登录，
		/// 其 68 包被早期拦截未写入 UUID，这里在检查前强制写入，绕过踢出（兜底，
		/// 正常情况下 OnCrossTransferGetData 处理 68 时已写入）。
		/// </summary>
		private static void OnCrossTransferJoin(JoinEventArgs args)
		{
			if (TransferProtocol.PreTransfers.TryGetValue(args.Who, out var pre)
				&& !string.IsNullOrEmpty(pre.UUID))
			{
				TransferProtocol.SetRemoteClientUUID(args.Who, pre.UUID);
				TShock.Log.ConsoleInfo($"[CrossTransfer] OnJoin 前写入 UUID slot#{args.Who}: {pre.UUID}");
			}
		}

		// ════════════════════════════════════════════
		//  GetData 钩子：捕获玩家首次进入的 PlayerInfo 原始帧
		// ════════════════════════════════════════════

		private static void OnNetGetData(GetDataEventArgs e)
		{
			if (e.Handled || e.MsgID != PacketTypes.PlayerInfo) return;
			var who = e.Msg.whoAmI;
			if (who < 0 || who >= TShock.Players.Length) return;
			var p = TShock.Players[who];
			if (p == null || p.State >= (int)ConnectionState.Complete) return; // 仅首次握手期

			// 完整帧 = [ushort 整帧总长][msgId][payload]
			// e.Length = msgId(1) + payload；e.Index 指向 payload 起始，msgId 位于 readBuffer[e.Index-1]
			// Terraria 长度字段 = 整帧总长（含 2 字节长度前缀），故 total = e.Length + 2
			var total = (ushort)(e.Length + 2);
			var frame = new byte[total];
			frame[0] = (byte)(total & 0xFF);
			frame[1] = (byte)((total >> 8) & 0xFF);
			frame[2] = e.Index > 0 ? e.Msg.readBuffer[e.Index - 1] : (byte)e.MsgID;
			Buffer.BlockCopy(e.Msg.readBuffer, e.Index, frame, 3, e.Length - 1);
			PlayerInfoFrames[who] = frame;
		}

		// ════════════════════════════════════════════
		//  命令
		// ════════════════════════════════════════════

		/// <summary>/跨服 <服务器名> [进服密码]：向目标服发起前置鉴权握手</summary>
		private static void TransferCommand(CommandArgs args)
		{
			var player = args.Player;
			if (player == null || !player.RealPlayer) return;

			if (args.Parameters.Count == 0)
			{
				player.SendInfoMessage($"[跨服] 用法: /跨服 <服务器名> [进服密码]");
				player.SendInfoMessage($"[跨服] 可用服务器: {string.Join(", ", Config.Servers.Select(s => s.Name))}");
				return;
			}

			var serverName = args.Parameters[0];

			// ═══ B 服：跨服转发来的玩家再次传送 → 把请求转发给源服（桥接中心）判定 ═══
			// 该玩家在 B 服的连接是源服的模拟连接，本地不再发起握手，由源服判定目标合法性
			// （返回源服 / 合法 C 服 / 不可达拒绝），判定结果经 TransferResult 回传提示。
			if (TransferProtocol.PreTransfers.ContainsKey(player.Index))
			{
				var req = TransferProtocol.EncodeCustomData(TransferProtocol.TransferRequestPacket,
					bw => bw.Write(serverName));
				TransferProtocol.SendRaw(player.Index, req);
				player.SendInfoMessage($"[跨服] 已向源服请求传送至 {serverName}，等待判定…");
				TShock.Log.ConsoleInfo($"[CrossTransfer] {player.Name} 在 B 服请求传送至 {serverName}，已转发源服");
				return;
			}
			var server = Config.Servers.FirstOrDefault(
				s => s.Name.Equals(serverName, StringComparison.OrdinalIgnoreCase));
			if (server == null)
			{
				player.SendErrorMessage($"[跨服] 未找到目标服: {serverName}");
				return;
			}
			if (string.IsNullOrEmpty(player.UUID))
			{
				player.SendErrorMessage("[跨服] 无法获取你的 UUID，无法建立传送通道");
				return;
			}

			var password = args.Parameters.Count > 1
				? string.Join(" ", args.Parameters.Skip(1))
				: server.Password;

			// 玩家进入本服时的 PlayerInfo 原始帧（无则跨服会缺少角色数据，提示先重进）
			PlayerInfoFrames.TryGetValue(player.Index, out var playerInfoFrame);
			if (playerInfoFrame == null)
			{
				player.SendErrorMessage("[跨服] 未捕获到你的角色数据（PlayerInfo），请重新连接本服后再试");
				return;
			}

			player.SendSuccessMessage($"[跨服] 正在建立到 {server.Name} 的传送通道…");
			Task.Run(async () =>
			{
				var result = await CrossLoginClient.FullHandshakeAsync(
					server, player.Name, player.UUID, player.IP,
					playerInfoFrame, password,
					msg => player.SendInfoMessage(msg));
				if (result.Ok)
				{
					// 阶段 8：Socket 夺取 + 桥接（玩家连接无缝切换到目标服）
					StartBridge(player, result, server, playerInfoFrame);
				}
				else
				{
					player.SendErrorMessage($"[跨服] 传送失败: {result.Reason}");
				}
			});
		}

		/// <summary>/跨服密码 <密码>：目标服探测到进服密码时，玩家在此输入（解除挂起握手）</summary>
		private static void PasswordCommand(CommandArgs args)
		{
			var player = args.Player;
			if (player == null || !player.RealPlayer) return;
			if (args.Parameters.Count == 0)
			{
				player.SendInfoMessage("[跨服] 用法: /跨服密码 <进服密码>");
				return;
			}
			var pwd = string.Join(" ", args.Parameters);
			CrossLoginClient.SubmitPassword(player.Name, pwd);
			player.SendSuccessMessage("[跨服] 密码已提交");
		}

		/// <summary>/返回：回程流程（本地回环重连原服，阶段 2 实现）</summary>
		private static void ReturnCommand(CommandArgs args)
		{
			args.Player?.SendErrorMessage("[跨服] 返回原服功能开发中（回程流程）");
		}
	}
}
