using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TShockAPI;

namespace TeleportRequest
{
	public enum TPMode
	{
		Agree,
		Request,
		Block
	}

	/// <summary>
	/// 持久化数据结构。
	///
	/// 【重要】players 与 allowList 的键、以及 allowList 的值，一律使用
	/// 「账号 ID」（UserAccount.ID），不得使用玩家槽位号（TSPlayer.Index）。
	///
	/// 槽位号只是「本局第几个进服」，每次开服都会重新分配：用它当键会导致
	/// 重启后设置挂到别人身上——自己的模式静默失效，同时白名单被陌生人继承
	/// （等于把"允许传送到我身边"的授权交给无关的人）。
	///
	/// 未登录玩家没有账号 ID，不参与持久化，由调用方（命令层）拒绝。
	/// </summary>
	internal class StoreData
	{
		public TPMode _default = TPMode.Agree;
		public Dictionary<int, TPMode> players = new Dictionary<int, TPMode>();
		public Dictionary<int, List<int>> allowList = new Dictionary<int, List<int>>();
	}

	public static class TPModeStore
	{
		private static readonly object Lock = new object();
		private static StoreData _data = new StoreData();
		private static string _filePath;

		public static void Initialize(string savePath)
		{
			_filePath = Path.Combine(savePath, "tpplus.json");
			Load();
		}

		public static TPMode DefaultMode
		{
			get { lock (Lock) { return _data._default; } }
			set { lock (Lock) { _data._default = value; Save(); } }
		}

		/// <summary>读取某账号的传送模式；未设置过则返回全局默认值。</summary>
		/// <param name="accountId">被查看者的账号 ID（UserAccount.ID）</param>
		public static TPMode GetMode(int accountId)
		{
			lock (Lock)
			{
				return _data.players.TryGetValue(accountId, out var mode) ? mode : _data._default;
			}
		}

		/// <param name="accountId">玩家自己的账号 ID（UserAccount.ID）</param>
		public static void SetMode(int accountId, TPMode mode)
		{
			lock (Lock)
			{
				_data.players[accountId] = mode;
				Save();
			}
		}

		/// <summary>
		/// 判断 sender 是否在 target 的白名单中，即 target 是否允许 sender 无视模式传送过来。
		/// 语义是「谁可以传送到我身边」，因此字典的键是 target（白名单持有者）。
		/// </summary>
		/// <param name="targetAccountId">白名单持有者的账号 ID（UserAccount.ID）</param>
		/// <param name="senderAccountId">发起传送者的账号 ID（UserAccount.ID）</param>
		public static bool IsAllowed(int targetAccountId, int senderAccountId)
		{
			lock (Lock)
			{
				return _data.allowList.TryGetValue(targetAccountId, out var list) && list.Contains(senderAccountId);
			}
		}

		/// <param name="targetAccountId">白名单持有者的账号 ID（UserAccount.ID）</param>
		/// <param name="senderAccountId">被加入白名单者的账号 ID（UserAccount.ID）</param>
		public static void AddAllowed(int targetAccountId, int senderAccountId)
		{
			lock (Lock)
			{
				if (!_data.allowList.TryGetValue(targetAccountId, out var list))
				{
					list = new List<int>();
					_data.allowList[targetAccountId] = list;
				}
				if (!list.Contains(senderAccountId))
					list.Add(senderAccountId);
				Save();
			}
		}

		/// <param name="targetAccountId">白名单持有者的账号 ID（UserAccount.ID）</param>
		/// <param name="senderAccountId">被移出白名单者的账号 ID（UserAccount.ID）</param>
		public static void RemoveAllowed(int targetAccountId, int senderAccountId)
		{
			lock (Lock)
			{
				if (_data.allowList.TryGetValue(targetAccountId, out var list))
				{
					list.Remove(senderAccountId);
					if (list.Count == 0)
						_data.allowList.Remove(targetAccountId);
					Save();
				}
			}
		}

		/// <param name="targetAccountId">白名单持有者的账号 ID（UserAccount.ID）</param>
		public static List<int> GetAllowedList(int targetAccountId)
		{
			lock (Lock)
			{
				return _data.allowList.TryGetValue(targetAccountId, out var list)
					? new List<int>(list) : new List<int>();
			}
		}

		private static void Load()
		{
			lock (Lock)
			{
				_data = new StoreData();
				if (!File.Exists(_filePath))
					return;
				try
				{
					var json = File.ReadAllText(_filePath);
					_data = JsonConvert.DeserializeObject<StoreData>(json) ?? new StoreData();
				}
				catch (Exception ex)
				{
					// 不能静默吞掉：文件一旦损坏，全部模式与白名单会被清空，
					// 必须留下痕迹，否则管理员只会看到"设置无缘无故没了"。
					TShock.Log.ConsoleError("[Teleport] 读取 tpplus.json 失败，已重置为空: {0}", ex.Message);
					_data = new StoreData();
				}
			}
		}

		private static void Save()
		{
			lock (Lock)
			{
				var json = JsonConvert.SerializeObject(_data, Formatting.Indented);
				File.WriteAllText(_filePath, json);
			}
		}
	}
}
