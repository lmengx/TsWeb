		using System;
		using System.Collections.Generic;
		using System.IO;
		using System.Linq;
		using System.Net.Sockets;
		using System.Reflection;
		using System.Text;
		using System.Threading;
		using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using MonoMod.RuntimeDetour;
using Newtonsoft.Json;
using Terraria;
using Terraria.Localization;
using Terraria.Net;
using Terraria.Net.Sockets;
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
	/// 丢弃发送、转发接收的包装 socket。挂在桥接玩家的 Netplay.Clients[who].Socket 上：
	/// - A 服所有下行（NetManager.Broadcast / NetMessage.SendData / SendBytes）走 AsyncSend → 被丢弃，
	///   不再直达玩家（解决跨服聊天重放、A 服广播污染、静止玩家）。
	/// - 上行接收 AsyncReceive 照常转发真 socket（玩家数据仍到 A 服，由桥接钩子转发目标服）。
	/// 桥接下行（目标服 → 玩家）改用 BridgeSession.PlayerSocket（真 socket）直接发，不受影响。
	/// </summary>
	public sealed class DiscardSocket : ISocket
	{
		private readonly ISocket _inner;
		public DiscardSocket(ISocket inner) => _inner = inner;

		void ISocket.Close() => _inner.Close();
		bool ISocket.IsConnected() => _inner.IsConnected();
		void ISocket.Connect(RemoteAddress address) => _inner.Connect(address);
		void ISocket.AsyncSend(byte[] data, int offset, int size, SocketSendCallback callback, object state)
			=> callback?.Invoke(state); // 丢弃：不真正发送，立即回调假装完成
		void ISocket.AsyncReceive(byte[] data, int offset, int size, SocketReceiveCallback callback, object state)
			=> _inner.AsyncReceive(data, offset, size, callback, state);
		bool ISocket.IsDataAvailable() => _inner.IsDataAvailable();
		RemoteAddress ISocket.GetRemoteAddress() => _inner.GetRemoteAddress();
		bool ISocket.StartListening(SocketConnectionAccepted callback)
			=> throw new NotSupportedException("DiscardSocket 不是监听 socket");
		void ISocket.StopListening() { }
	}

	/// <summary>
	/// 桥接保留 socket：挂在【已完整退出】的桥接玩家 slot 上。
	/// - IsConnected() 返回 false → A 服认为该连接已断开，触发 Terraria 完整退出流程
	///   （Netplay.UpdateConnectedClients 检测 IsConnected==false && IsActive==true →
	///   PendingTermination → RemoteClient.Reset() 清状态 + SyncDisconnectedPlayer 广播下线），
	///   使 A 服 slot 彻底静默、不再参与任何玩家同步（修复“幽灵连接导致 A 服玩家位置错乱”）。
	/// - Close() 不关闭真 socket：真 socket 保留给桥接自读自写（玩家客户端仍连在上面）。
	/// - AsyncSend 丢弃：拦截 A 服所有下行（含最后一步的 kick 踢出包），玩家客户端不收到。
	/// - AsyncReceive 立即回调 0：若 A 服在退出完成前仍读一次，按断开语义（length=0）加速退出。
	/// </summary>
	public sealed class RetainedSocket : ISocket
	{
		private readonly ISocket _real;
		public ISocket Real => _real;
		public RetainedSocket(ISocket real) => _real = real;

		void ISocket.Close() { /* 保留真 socket 给桥接，不真正关闭 */ }
		bool ISocket.IsConnected() => false; // 关键：让 A 服认为断开 → 触发完整退出 + 不再向 slot 发送
		void ISocket.Connect(RemoteAddress address) => _real.Connect(address);
		void ISocket.AsyncSend(byte[] data, int offset, int size, SocketSendCallback callback, object state)
			=> callback?.Invoke(state); // 丢弃 A 服下行（含 kick 包），立即回调假装完成
		void ISocket.AsyncReceive(byte[] data, int offset, int size, SocketReceiveCallback callback, object state)
			=> callback?.Invoke(state, 0); // A 服不应再读；若读则按断开返回 0（加速退出）
		bool ISocket.IsDataAvailable() => false;
		RemoteAddress ISocket.GetRemoteAddress() => _real.GetRemoteAddress();
		bool ISocket.StartListening(SocketConnectionAccepted callback)
			=> throw new NotSupportedException("RetainedSocket 不是监听 socket");
		void ISocket.StopListening() { }
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

		// 保活定时器：桥接玩家的 TimeOutTimer 虽已随上行包重置，但玩家长时间静止（无上行包）
		// 时仍可能超时被踢，这里定时兜底归零，确保桥接连接稳定存活（约 20s 一次）。
		private static System.Threading.Timer? _keepAliveTimer;

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

			// ═══ 玩家断线 → 清理桥接会话 ═══
			ServerApi.Hooks.ServerLeave.Register(plugin, OnServerLeave);

			// ═══ 最高优先级 ServerJoin：在 TShock.OnJoin 的 KickEmptyUUID 检查前写入 UUID（兜底）═══
			ServerApi.Hooks.ServerJoin.Register(plugin, OnCrossTransferJoin, int.MinValue);

			// ═══ 名字冲突处理：preTransfer/回环模拟连接放行（TShock 的 NetHooks_NameCollision 用
			//    Players.First(...)，当原玩家 slot 已被桥接隐藏（TShock.Players[i]=null）时无匹配会
			//    抛 Sequence contains no matching element 并把新连接踢出）═══
			ServerApi.Hooks.NetNameCollision.Register(plugin, OnNameCollision, int.MinValue);

			Commands.ChatCommands.Add(new Command(Permission, TransferCommand, "跨服", "crosstransfer"));
			Commands.ChatCommands.Add(new Command(Permission, ReturnCommand, "返回", "ctback"));
			Commands.ChatCommands.Add(new Command(PasswordCommand, "跨服密码", "ctpass"));

			// ═══ 保活定时器：桥接玩家 TimeOutTimer 兜底归零（防止静止无上行包时超时断线）═══
			_keepAliveTimer = new System.Threading.Timer(_ =>
			{
				try
				{
					foreach (var who in Bridges.Keys)
					{
						if (who < 0 || who >= Netplay.Clients.Length) continue;
						var rc = Netplay.Clients[who];
						if (rc.IsActive) rc.TimeOutTimer = 0;
					}
				}
				catch { }
			}, null, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20));

			TShock.Log.ConsoleInfo($"[CrossTransfer] 跨服传送初始化完成（{Config.Servers.Count} 个目标服）");
		}

		public static void Dispose()
		{
			if (!_initialized) return;
			_initialized = false;

			// ═══ 0) 清理全部运行态（热重载后从零开始，杜绝残留导致的"寄生/包错乱/登录不生效"）═══
			// 1) 中断所有进行中的跨服桥接：断 B 服连接 + 取消读循环 + 断开玩家残留连接
			foreach (var who in Bridges.Keys.ToArray())
			{
				try { CleanupBridge(who); }
				catch (Exception ex) { TShock.Log.ConsoleWarn($"[CrossTransfer] 卸载清理桥接 slot#{who} 失败: {ex.Message}"); }
			}
			Bridges.Clear();

			// 1.5) 停止保活定时器
			try { _keepAliveTimer?.Dispose(); } catch { }
			_keepAliveTimer = null;

			// 2) 清理 B 服 preTransfer 标记 + nonce 缓存
			TransferProtocol.Reset();

			// 3) 清理挂起的进服密码会话（解除等待，旧握手自然失败退出）
			CrossLoginClient.Reset();

			// 4) 清理 PlayerInfo 帧缓存
			PlayerInfoFrames.Clear();

			// ═══ 5) 卸载全部钩子 ═══
			ServerApi.Hooks.NetGetData.Deregister(_plugin!, OnNetGetData);
			ServerApi.Hooks.NetGetData.Deregister(_plugin!, OnCrossTransferGetData);
			try { _getDataDetour?.Dispose(); } catch { }
			_getDataDetour = null;
			OTAPI.Hooks.MessageBuffer.GetData -= OnMessageBufferGetData;
			ServerApi.Hooks.ServerLeave.Deregister(_plugin!, OnServerLeave);
			ServerApi.Hooks.ServerJoin.Deregister(_plugin!, OnCrossTransferJoin);
			ServerApi.Hooks.NetNameCollision.Deregister(_plugin!, OnNameCollision);

			// ═══ 6) 移除命令 ═══
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
			if (IsBridgeActive(e.Msg.whoAmI))
			{
				e.Handled = true;
				return;
			}

			// B 服：preTransfer 连接的 PlayerInfo → 不做任何账号干预，由 TShock 原生
			// HandleConnecting 决定（有账号+UUID 匹配自动登录；无账号游客放行或按目标服
			// 策略踢出——踢出时 A 服侧握手失败，玩家留在原服）。

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
								TShock.Log.ConsoleDebug($"[CrossTransfer] ClientUUID 已写入 slot#{who}");
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
			if (Bridges.TryGetValue(args.Instance.whoAmI, out var bridge) && IsBridgeActive(args.Instance.whoAmI))
			{
				ForwardToTarget(bridge, buf, off, len);
				// 兜底：若最外层 detour 未生效（此路径由 orig 内 OTAPI 钩子触发），
				// MessageBuffer.GetData 开头的 TimeOutTimer 重置已执行；这里再归零一次无害。
				if (args.Instance.whoAmI >= 0 && args.Instance.whoAmI < Netplay.Clients.Length)
					Netplay.Clients[args.Instance.whoAmI].TimeOutTimer = 0;
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
				if (!IsBridgeActive(self.whoAmI)) return false; // 该 slot 已不是桥接保留连接（如返回回环复用）→ 放行原版处理
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

				// 关键修复：桥接玩家的包被本 detour 完全接管（不调 orig），
				// MessageBuffer.GetData 开头的 `Netplay.Clients[whoAmI].TimeOutTimer = 0`
				// 重置代码永不执行 → 服务器主循环每 tick 累加 TimeOutTimer，
				// 7200 tick（≈2 分钟@60fps，用户感知"约 5 分钟内"）后判定超时
				// （PendingTermination）→ RemoteClient.Reset() → Socket.Close() → 桥接必断。
				// 这里手动重置，保持 A 服认为该连接持续活跃。
				if (self.whoAmI >= 0 && self.whoAmI < Netplay.Clients.Length)
					Netplay.Clients[self.whoAmI].TimeOutTimer = 0;
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
			public ISocket? PlayerSocket;       // 玩家在 A 服的真实 socket（桥接占用，桥接下行直发 + 上行自读用）
			public CancellationTokenSource Cts = new();
			public Task? ReadLoop;
			public CancellationTokenSource UpstreamCts = new(); // 上行自读循环独立的取消源（切换目标时不被取消）
			public Task? UpstreamLoop;
			public int TargetGeneration;    // 目标连接代数：切换目标服时递增。旧读循环检测到代数变化 → 丢弃并退出，
			                                // 杜绝切换期间旧服残留数据串到客户端（“同时收到两个服的数据包”）。
			public bool Switching;              // 切换中：旧读循环 finally 不得清理桥接
			public readonly object WriteLock = new();
		}

		private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, BridgeSession> Bridges = new();

		/// <summary>玩家是否处于跨服桥接中（供其他模块豁免处理）。
		/// 使用 IsBridgeActive 语义（slot 仍挂着 RetainedSocket 才算桥接）：
		/// 避免 Bridges 残留条目（断线/切换失败未及时清理）误伤复用同一 slot 的新玩家。</summary>
		public static bool IsBridging(int who) => IsBridgeActive(who);

		/// <summary>
		/// 该 slot 是否仍处于桥接保留中：仅当 Netplay.Clients[who].Socket 仍挂着本桥接的
		/// RetainedSocket 时才视为桥接激活（拦截其包转发目标服）。
		/// 返回时回环连接可能复用原 slot（Socket 被新连接替换，不再是 RetainedSocket）→
		/// 不再视为桥接 → A 服正常处理回环握手包（否则会卡在握手）。
		/// </summary>
		private static bool IsBridgeActive(int who)
		{
			return who >= 0 && who < Netplay.Clients.Length
				&& Bridges.ContainsKey(who)
				&& Netplay.Clients[who]?.Socket is RetainedSocket;
		}

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
				Stream = result.Connection.GetStream(),
				PlayerSocket = player.Index >= 0 && player.Index < Netplay.Clients.Length
					? Netplay.Clients[player.Index]?.Socket : null
			};

			// 提示必须先于桥接激活发送（激活后 A 服→玩家的消息会被出站拦截钩子丢弃）
			player.SendSuccessMessage($"[跨服] 已传送到 {server.Name}，正在加载目标世界…");

			Bridges[player.Index] = session;

			// ═══ 让 A 服对桥接玩家执行【完整退出】，仅保留真 socket 给桥接 ═══
			// 关键：不是“半活跃幽灵连接”，而是让 Terraria/TShock 真的认为玩家退出了：
			//  0) 把 Netplay.Clients[who].Socket 换成 RetainedSocket（IsConnected()=false）→
			//     服务器主循环下一 tick 检测到 IsConnected==false && IsActive==true →
			//     PendingTermination → RemoteClient.Reset()（清 RemoteClient/Main.player/MessageBuffer
			//     状态、IsActive=false）+ SyncDisconnectedPlayer（广播下线）→ 完整退出。
			//  1) 手动执行 TShock.OnLeave 等效逻辑（广播已离开 / 保存 SSC / 触发 OnPlayerLogout /
			//     置 TShock.Players[who]=null）→ 之后底层触发的 ServerLeave→TShock.OnLeave 因
			//     tsplr==null 自动跳过，不会重复。
			//  2) 拦截 kick 包：退出流程给 who 的任何下行（含最后踢出包）都被 RetainedSocket.AsyncSend
			//     丢弃，玩家客户端不会收到“被踢出”也不真正断开。
			//  3) 退出完成后 A 服 slot 彻底静默（不再参与任何玩家同步）→ 桥接不再干扰 A 服玩家位置。
			//  4) 玩家上行改由自读循环直读真 socket（A 服已不读 slot），下行由 BridgeReadLoop 直发真 socket。
			try
			{
				var tsplr = TShock.Players[player.Index];

				// 0) 替换 Socket = RetainedSocket（IsConnected=false，Close 保留真 socket）
				if (session.PlayerSocket != null && player.Index >= 0 && player.Index < Netplay.Clients.Length)
				{
					Netplay.Clients[player.Index].Socket = new RetainedSocket(session.PlayerSocket);
				}

				// 1) 广播传送消息（替代 TShock 的“已离开”广播：桥接玩家传送走后 A 服应显示传送去向）
				if (tsplr != null && tsplr.ReceivedInfo && !tsplr.SilentKickInProgress
					&& tsplr.State >= (int)ConnectionState.RequestingWorldData && tsplr.FinishedHandshake)
				{
					TShock.Utils.Broadcast($"{tsplr.Name} 传送到 {server.Name}", Color.Yellow);
					TShock.Log.Info($"{tsplr.Name} transferred to {server.Name}.");
				}

				// 2) 广播 PlayerActive(false)：A 服其他玩家看到该玩家下线、实体移除
				//    注意：PlayerActive = 14！之前误用 69（ChestName）导致实体未移除（虚假玩家残留）
				NetMessage.TrySendData((int)PacketTypes.PlayerActive, -1, player.Index, null, player.Index, 0);

				// 3) 保存角色/清理状态（复刻 TShock.OnLeave 剩余逻辑）
				if (tsplr != null)
				{
					if (tsplr.IsLoggedIn && !tsplr.IsDisabledPendingTrashRemoval && Main.ServerSideCharacter
						&& (!tsplr.Dead || tsplr.TPlayer.difficulty != 2))
					{
						tsplr.PlayerData.CopyCharacter(tsplr);
						TShock.CharacterDB.InsertPlayerData(tsplr);
					}
					if (TShock.Config.Settings.RememberLeavePos && !tsplr.LoginHarassed)
						TShock.RememberedPos?.InsertLeavePos(tsplr.Name, tsplr.IP,
							(int)(tsplr.X / 16), (int)(tsplr.Y / 16));
					if (tsplr.tempGroupTimer != null)
						tsplr.tempGroupTimer.Stop();
					tsplr.FinishedHandshake = false;
					if (tsplr.IsLoggedIn)
						TShockAPI.Hooks.PlayerHooks.OnPlayerLogout(tsplr);
				}

				// 4) A 服世界实体移除 + TShock 在线列表移除
				Main.player[player.Index].active = false;
				Main.player[player.Index].name = ""; // 清名字：Terraria 底层名字冲突检测用 Main.player 的 name，
				                                    // 不清会触发 NameCollision（原玩家 slot 已退但名字残留）
				TShock.Players[player.Index] = null;
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleWarn($"[CrossTransfer] 模拟玩家离开失败: {ex.Message}");
			}

			// ═══ 触发并等待 A 服底层完整退出（Reset + SyncDisconnectedPlayer）═══
			// 显式置 PendingTermination，让 UpdateConnectedClients 下一 tick 立即执行
			// RemoteClient.Reset()（清状态 + RetainedSocket.Close() 不关真 socket）+ SyncDisconnectedPlayer。
			// ⚠️ 超时（IsActive 仍未归零）时【继续桥接】：IsActive 归零依赖主循环执行 Reset，后台线程
			// 轮询经常等不到（历史行为即如此）；若因超时 CleanupBridge 会断开玩家 → 跨服连接必断。
			try
			{
				if (player.Index >= 0 && player.Index < Netplay.Clients.Length)
				{
					Netplay.Clients[player.Index].PendingTermination = true;
					Netplay.Clients[player.Index].PendingTerminationApproved = true;
					// 等待退出完成（Reset 后 IsActive=false，最多 5 秒）
					for (int i = 0; i < 250 && Netplay.Clients[player.Index].IsActive; i++)
						Thread.Sleep(20);
				}
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleWarn($"[CrossTransfer] 等待 A 服完整退出失败: {ex.Message}");
			}

			// 1) 重放握手期缓存的 B 服世界数据快照（WorldInfo/tile/NPC/玩家等）
			//    关键：必须转发 LoadPlayer(3)[B slot]——客户端据此把 Main.myPlayer 更新为目标服 slot，
			//    B 服以该 slot 发的包才会被客户端渲染为"自己"（不转发 → 寄生/自身无实体/收别人包）。
			//    MultiSEngine 参考：FlushBufferedPacketsToClientAsync 原样转发全部握手缓冲包（含 LoadPlayer）。
			//    仅过滤 RequestPassword(37)：客户端已连接，无需密码握手。
			foreach (var frame in result.BufferedPackets)
			{
				if (frame.Length >= 3 && frame[2] == 37) continue;
				SendToPlayerSocket(player.Index, frame);
			}

			// 2) 启动 B 服 → 玩家 下行读循环
			session.ReadLoop = Task.Run(() => BridgeReadLoop(session));

			// 3) 启动 玩家 → B 服 上行自读循环（A 服已完整退出、不再读该 socket，由我们直读真 socket）
			session.UpstreamLoop = Task.Run(() => BridgeUpstreamLoop(session));

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
			int gen = bridge.TargetGeneration; // 记录本循环对应的目标连接代数
			var lenBuf = new byte[2];
			try
			{
				while (!bridge.Cts.IsCancellationRequested)
				{
					// 目标已切换（代数变化）→ 旧读循环直接退出，不再转发旧服数据
					if (gen != bridge.TargetGeneration) break;
					// 玩家已断线（上行自读循环已结束）→ 退出并触发清理，避免目标服幽灵连接
					if (bridge.UpstreamLoop != null && bridge.UpstreamLoop.IsCompleted) break;
					if (!ReadExactly(s, lenBuf, 0, 2, bridge.Cts.Token)) break;
					int total = lenBuf[0] | (lenBuf[1] << 8);
					if (total < 3 || total > 0xFFFF) break;
					var frame = new byte[total];
					frame[0] = lenBuf[0];
					frame[1] = lenBuf[1];
					if (!ReadExactly(s, frame, 2, total - 2, bridge.Cts.Token)) break;

					// 读到完整帧后再查一次：切换瞬间旧服残留数据必须丢弃（防止“同时收到两个服的数据包”）
					if (gen != bridge.TargetGeneration) break;

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

		/// <summary>
		/// 上行：玩家 → 目标服。A 服已完整退出（不再读该 socket），由本循环直读真 socket 原始字节流，
		/// 按 Terraria 帧（[ushort 整帧总长][msgId][payload]）切分后逐帧转发当前目标服连接。
		/// 使用独立的 UpstreamCts（切换目标服时不被取消，仅更换写入目标 Stream）。
		/// </summary>
		private static void BridgeUpstreamLoop(BridgeSession bridge)
		{
			var sock = bridge.PlayerSocket;
			if (sock == null) return;
			var buf = new byte[8192];
			var acc = new byte[65536];
			int accLen = 0;
			try
			{
				while (!bridge.UpstreamCts.IsCancellationRequested)
				{
					if (!sock.IsConnected()) break;
					// 无数据时轮询（不发起 AsyncReceive → 无挂起读 → CleanupBridge Close socket 不会卡死）
					if (!sock.IsDataAvailable()) { Thread.Sleep(2); continue; }

					int got = 0;
					using var done = new ManualResetEventSlim(false);
					sock.AsyncReceive(buf, 0, buf.Length, (st, len) => { got = len; done.Set(); }, null);
					// 有数据才发起 → 回调必然快速触发（socket 关闭时不在此时发起，故不会卡死）
					// ⚠️ 不能加 Wait 超时：IsDataAvailable=true 后回调若因时序延迟触发，超时 break 会误判断开
					// → 上游循环退出 → ReadLoop 检测到 IsCompleted → CleanupBridge 断开玩家（跨服连接必断）
					done.Wait();
					if (got <= 0) break; // 断开

					// 累积（溢出保护：异常数据直接丢弃重新同步）
					if (accLen + got > acc.Length) { accLen = 0; Buffer.BlockCopy(buf, 0, acc, 0, Math.Min(got, acc.Length)); accLen = Math.Min(got, acc.Length); continue; }
					Buffer.BlockCopy(buf, 0, acc, accLen, got);
					accLen += got;

					// 按帧切分转发
					int pos = 0;
					while (accLen - pos >= 2)
					{
						int total = acc[pos] | (acc[pos + 1] << 8);
						if (total < 3 || total > 0xFFFF)
						{
							accLen = 0; pos = 0; break; // 非法帧：丢弃整段重新同步
						}
						if (accLen - pos < total) break; // 帧未完整，等待更多数据

						var frame = new byte[total];
						Buffer.BlockCopy(acc, pos, frame, 0, total);
						pos += total;
						ForwardUpstreamFrame(bridge, frame);
					}
					if (pos > 0)
					{
						Buffer.BlockCopy(acc, pos, acc, 0, accLen - pos);
						accLen -= pos;
					}
				}
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleWarn($"[CrossTransfer] 桥接上行自读循环结束: {ex.Message}");
			}
			finally
			{
				// 主动切换目标服时（Switching=true）旧读循环退出不清理，由切换逻辑接管
				if (!bridge.Switching)
					CleanupBridge(bridge.PlayerIndex);
			}
		}

		/// <summary>把完整帧（[ushort 整帧总长][msgId][payload]）转发到当前目标服连接。</summary>
		private static void ForwardUpstreamFrame(BridgeSession bridge, byte[] frame)
		{
			var s = bridge.Stream;
			if (s == null) return;
			lock (bridge.WriteLock)
			{
				try { s.Write(frame, 0, frame.Length); }
				catch (Exception ex) { TShock.Log.ConsoleWarn($"[CrossTransfer] 上行帧转发失败: {ex.Message}"); }
			}
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
				// 切换进行中：拒绝重复传送请求（防并发触发多次 SwitchTarget）
				if (bridge.Switching)
				{
					SendTransferResult(bridge, false, "正在切换服务器，请稍候再试");
					return;
				}

				// 已在目标服？（必须先于 SelfServerId 分支：玩家已在源服时再请求返回源服
				// 不应再走一次回环握手）
				if (bridge.CurrentServerName != null
					&& bridge.CurrentServerName.Equals(target, StringComparison.OrdinalIgnoreCase))
				{
					SendTransferResult(bridge, false, $"你已在该服务器（{target}）");
					return;
				}

				// 目标 = 源服自身 → 返回
				if (target.Equals(Config.SelfServerId, StringComparison.OrdinalIgnoreCase))
				{
					SendTransferResult(bridge, true, "正在返回源服…");
					ReturnToSelf(bridge);
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
					bridge.TargetGeneration++; // 代数递增：旧读循环下次检测到代数变化 → 丢弃残留数据并退出
					bridge.Cts.Cancel();
					// 优雅关闭旧连接：先 Shutdown(Both) 发 FIN 让对端正常走下线流程（清 slot/PreTransfers），
					// 再 Dispose；避免直接 RST 导致对端 slot 下线处理不完整（幽灵连接/持续广播）。
					try
					{
						var old = bridge.Target;
						if (old != null)
						{
							try { old.Client?.Shutdown(SocketShutdown.Both); } catch { }
							old.Dispose();
						}
					}
					catch { }
					bridge.Stream = null;
					// 等旧读循环退出（旧连接 Dispose 后同步 Read 必然抛异常立即退出；
					// 即使未及时退出，代数检测也保证其不再转发旧服数据）
					try { bridge.ReadLoop?.Wait(3000); } catch { }
					bridge.ReadLoop = null;

					// 2) 新握手（复用与首次传送完全相同的 CrossLoginClient 流程）
					var result = await CrossLoginClient.FullHandshakeAsync(
						server, bridge.PlayerName, bridge.UUID, bridge.RealIP,
						bridge.PlayerInfoFrame, server.Password, msg => { });
					if (!result.Ok || result.Connection == null)
					{
						TShock.Log.ConsoleError($"[CrossTransfer] 切换到 {server.Name} 失败: {result.Reason}");
						bridge.Switching = false; // 恢复可清理状态再清理，避免 OnServerLeave 防护漏掉后续断线
						CleanupBridge(who);  // 断开玩家，避免幽灵
						return;
					}

					// 3) 更新会话（TargetGeneration 已为新代数，新读循环记录之）
					bridge.Target = result.Connection;
					bridge.Stream = result.Connection.GetStream();
					bridge.Cts = new CancellationTokenSource();
					bridge.CurrentServerName = server.Name;

					// 4) 玩家若在切换期间真断线（上行自读循环已退出）→ 立即清理新连接，
					//    避免目标服以为玩家在线而产生幽灵玩家/持续广播。
					//    注意：必须在 Switching 仍为 true 时检查——UpstreamLoop 的 finally
					//    因 Switching=true 不会自行清理，由这里统一处理。
					if (bridge.UpstreamLoop == null || bridge.UpstreamLoop.IsCompleted)
					{
						TShock.Log.ConsoleInfo($"[CrossTransfer] 玩家 {bridge.PlayerName} 切换期间已断线，取消切换后的桥接");
						bridge.Switching = false;
						CleanupBridge(who);
						return;
					}

					bridge.Switching = false;

					// 5) 重放新世界数据（转发 LoadPlayer 同步 myPlayer 到新目标服 slot，其余照旧）
					foreach (var frame in result.BufferedPackets)
					{
						if (frame.Length >= 3 && frame[2] == 37) continue;
						SendToPlayerSocket(who, frame);
					}

					// 6) 新读循环
					bridge.ReadLoop = Task.Run(() => BridgeReadLoop(bridge));

					// 7) 返回原服：A 服原生流程对“回环模拟连接”下发装备可能不及时/不完整
					//    （客户端 Main.player[myPlayer] 残留 B 服装备）→ 主动补发 A 服角色装备，
					//    从 A 服 Main.player[RemoteSlot] 读取（SSC/PlayerInfo 已加载），覆盖 B 服残留。
					if (server.Name.Equals(Config.SelfServerId, StringComparison.OrdinalIgnoreCase)
						&& result.RemoteSlot >= 0 && result.RemoteSlot < Main.player.Length)
					{
						int rslot = result.RemoteSlot;
						_ = Task.Delay(800).ContinueWith(_ =>
						{
							try
							{
								// 等待 A 服完成角色填充（Main.player[rslot].name 非空），最多等 4 秒
								for (int i = 0; i < 10; i++)
								{
									if (Main.player[rslot] != null && !string.IsNullOrEmpty(Main.player[rslot].name)) break;
									Thread.Sleep(400);
								}
								SyncAllEquipment(rslot);
							}
							catch (Exception ex) { TShock.Log.ConsoleWarn($"[CrossTransfer] 返回后补发装备失败: {ex.Message}"); }
						});
					}

					TShock.Log.ConsoleInfo($"[CrossTransfer] 桥接已切换到 {server.Name}（玩家 {bridge.PlayerName}）");
				}
				catch (Exception ex)
				{
					TShock.Log.ConsoleError($"[CrossTransfer] 切换桥接异常: {ex}");
					bridge.Switching = false;
					try { CleanupBridge(who); } catch { }
				}
			});
		}

		/// <summary>返回源服：恢复玩家可见性与 TShock 状态，重发源服世界数据（无缝切回）</summary>
		/// <summary>
		/// 返回源服：把本服自身当作目标服，回环握手（模拟客户端连接 127.0.0.1:本服端口），
		/// 复用完整 CrossLoginClient 握手 + SwitchTarget 桥接切换 → 客户端走 A 服原生完整加入流程，
		/// 形象/物品/tile 全部由 A 服原生流程处理，避免手动模拟的所有问题。
		/// </summary>
		private static void ReturnToSelf(BridgeSession bridge)
		{
			try
			{
				var self = new TransferServerInfo
				{
					Name = Config.SelfServerId,
					IP = "127.0.0.1",
					Port = Netplay.ListenPort,
					Secret = Config.SelfSecret,
					Password = Config.Servers.FirstOrDefault(
						s => s.Name.Equals(Config.SelfServerId, StringComparison.OrdinalIgnoreCase))?.Password
				};
				TShock.Log.ConsoleInfo($"[CrossTransfer] 玩家 {bridge.PlayerName} 正在返回源服（回环握手 {self.IP}:{self.Port}）");
				SwitchTarget(bridge, self);
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[CrossTransfer] 返回源服失败: {ex}");
				try { CleanupBridge(bridge.PlayerIndex); } catch { }
			}
		}

		/// <summary>全量同步玩家物品/装备到客户端（从 Main.player[who] 读取，逐槽发 SyncEquipment(5) 只发给 who）。</summary>
		private static void SyncAllEquipment(int who)
		{
			try
			{
				if (who < 0 || who >= Main.player.Length) return;
				var tpl = Main.player[who];
				if (tpl == null) return;

				// 先同步当前套装索引（SyncLoadout 必须先于物品）
				NetMessage.SendData((int)PacketTypes.SyncLoadout, who, -1, null, who, tpl.CurrentLoadoutIndex);

				// 网络槽号（Terraria 协议固定值，对应 PlayerItemSlotID）：
				// Inventory0=0 Armor0=59 Dye0=79 Misc0=89 MiscDye0=94 Bank1_0=99 Bank2_0=139
				// TrashItem=179 Bank3_0=180 Bank4_0=220
				const int Inventory0 = 0, Armor0 = 59, Dye0 = 79, Misc0 = 89, MiscDye0 = 94,
					Bank1_0 = 99, Bank2_0 = 139, TrashItem = 179, Bank3_0 = 180, Bank4_0 = 220;

				for (int k = 0; k < NetItem.InventorySlots; k++)
					NetMessage.SendData(5, who, -1, null, who, Inventory0 + k);
				for (int k = 0; k < NetItem.ArmorSlots; k++)
					NetMessage.SendData(5, who, -1, null, who, Armor0 + k);
				for (int k = 0; k < NetItem.DyeSlots; k++)
					NetMessage.SendData(5, who, -1, null, who, Dye0 + k);
				for (int k = 0; k < NetItem.MiscEquipSlots; k++)
					NetMessage.SendData(5, who, -1, null, who, Misc0 + k);
				for (int k = 0; k < NetItem.MiscDyeSlots; k++)
					NetMessage.SendData(5, who, -1, null, who, MiscDye0 + k);
				for (int k = 0; k < NetItem.PiggySlots; k++)
					NetMessage.SendData(5, who, -1, null, who, Bank1_0 + k);
				for (int k = 0; k < NetItem.SafeSlots; k++)
					NetMessage.SendData(5, who, -1, null, who, Bank2_0 + k);
				NetMessage.SendData(5, who, -1, null, who, TrashItem);
				for (int k = 0; k < NetItem.ForgeSlots; k++)
					NetMessage.SendData(5, who, -1, null, who, Bank3_0 + k);
				for (int k = 0; k < NetItem.VoidSlots; k++)
					NetMessage.SendData(5, who, -1, null, who, Bank4_0 + k);
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleWarn($"[CrossTransfer] 物品同步失败: {ex.Message}");
			}
		}

		/// <summary>直发玩家 socket（不走 NetMessage.SendBytes，天然绕过出站拦截钩子）</summary>
		private static void SendToPlayerSocket(int who, byte[] data)
		{
			try
			{
				// 桥接中：必须用 session 保存的真 socket 发送（Netplay.Clients[who].Socket
				// 已被替换为 RetainedSocket，其 AsyncSend 会丢弃）。
				// 仅当 Bridges 中存在该 slot 时才发送；Bridges 已移除（桥接结束）后一律丢弃——
				// 绝不能向 Netplay.Clients[who].Socket 兜底发送：该 slot 可能已被新连接/
				// 其他玩家复用，会把目标服数据误发给无关玩家（"其它玩家收到 B 服数据"）。
				if (!Bridges.TryGetValue(who, out var b) || b.PlayerSocket == null)
					return;
				if (!b.PlayerSocket.IsConnected())
					return;
				b.PlayerSocket.AsyncSend(data, 0, data.Length, _ => { });
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleWarn($"[CrossTransfer] 向玩家 socket#{who} 发送失败: {ex.Message}");
			}
		}

		/// <summary>玩家断线 → 清理桥接（A 服）与跨服标记（B 服）</summary>
		private static void OnServerLeave(LeaveEventArgs args)
		{
			TransferProtocol.PreTransfers.TryRemove(args.Who, out _); // B 服：清理跨服玩家标记
			PlayerInfoFrames.Remove(args.Who); // 清理该 slot 缓存的 PlayerInfo 帧（防复用后误用旧角色数据）

			// A 服：若该 slot 处于跨服桥接保留中，不得在此清理桥接，否则会断掉桥接本身：
			//  1) Socket 仍为 RetainedSocket —— 我们主动触发的完整退出（StartBridge）；
			//  2) bridge.Switching == true —— SwitchTarget 切换中。返回 A 服时回环连接
			//     可能复用了玩家原 slot（Socket 不再是 RetainedSocket），切换断开回环连接
			//     会触发本 ServerLeave；若在此清理会误关玩家真 socket 并移除桥接 →
			//     玩家断联、B 服数据无处送达。切换期间桥接生命周期由 SwitchTarget 统一接管，
			//     切换完成后它会检查玩家是否存活并决定是否清理。
			// 桥接生命周期由上行自读循环管理：真 socket 真正断开时，自读循环 finally 自己 CleanupBridge。
			if (args.Who >= 0 && args.Who < Netplay.Clients.Length
				&& Bridges.TryGetValue(args.Who, out var b)
				&& (Netplay.Clients[args.Who]?.Socket is RetainedSocket || b.Switching))
			{
				return;
			}
			CleanupBridge(args.Who); // 非桥接玩家的正常下线清理
		}

		/// <summary>清理桥接会话：取消读循环、关闭 B 服连接、断开玩家真 socket（让 A 服 slot 彻底静默可复用）</summary>
		private static void CleanupBridge(int who)
		{
			if (!Bridges.TryRemove(who, out var bridge)) return;
			bridge.Cts.Cancel();
			bridge.UpstreamCts.Cancel();
			// 优雅关闭到目标服的连接：先 Shutdown(Both) 发 FIN，让对端正常走下线流程
			//（清理 slot / PreTransfers），避免 RST 导致对端留下幽灵连接。
			try
			{
				var t = bridge.Target;
				if (t != null)
				{
					try { t.Client?.Shutdown(SocketShutdown.Both); } catch { }
					t.Dispose();
				}
			}
			catch { }
			bridge.Stream = null;

			// 断开玩家真 socket：让上行自读循环挂起的 AsyncReceive 立即返回（length=0 → 退出）。
			// 注意：Netplay.Clients[who].Socket 此时是 RetainedSocket（Close 不关真 socket），
			// 必须显式 Close 保存的真 socket 才能真正断开玩家连接。
			try { bridge.PlayerSocket?.Close(); } catch { }
			TShock.Log.ConsoleInfo($"[CrossTransfer] 玩家 slot#{who} 桥接已清理");

			// 兜底：若真 socket 未被上面关闭，断开 A 服挂着的连接对象
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
				TShock.Log.ConsoleDebug($"[CrossTransfer] OnJoin 前写入 UUID slot#{args.Who}");
			}
		}

		/// <summary>
		/// 名字冲突处理：preTransfer/回环模拟连接直接放行。
		/// 原玩家 slot 在桥接时已被隐藏（TShock.Players[i]=null、Main.player[i].name=""），
		/// Terraria 底层仍可能基于 Main.player 残留触发 NameCollision；此时 TShock 的
		/// NetHooks_NameCollision 用 Players.First(...) 无匹配会抛异常并把新连接踢出，
		/// 这里在 int.MinValue 优先级直接 Handled，跳过 TShock 的名字冲突踢出逻辑。
		/// </summary>
		private static void OnNameCollision(NameCollisionEventArgs args)
		{
			if (TransferProtocol.PreTransfers.ContainsKey(args.Who))
				args.Handled = true;
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

		/// <summary>/返回：回程请求。桥接中（在目标服）→ 向源服发 TransferRequest(来源服ID)，
		/// 由源服判定并执行回环返回；普通状态提示无需返回。</summary>
		private static void ReturnCommand(CommandArgs args)
		{
			var player = args.Player;
			if (player == null || !player.RealPlayer) return;

			if (TransferProtocol.PreTransfers.TryGetValue(player.Index, out var pre)
				&& !string.IsNullOrEmpty(pre.SourceServerId))
			{
				var req = TransferProtocol.EncodeCustomData(TransferProtocol.TransferRequestPacket,
					bw => bw.Write(pre.SourceServerId));
				TransferProtocol.SendRaw(player.Index, req);
				player.SendInfoMessage($"[跨服] 已请求返回原服 {pre.SourceServerId}，等待判定…");
				return;
			}

			player.SendErrorMessage("[跨服] 你当前不在跨服传送状态，无需返回");
		}
	}
}
