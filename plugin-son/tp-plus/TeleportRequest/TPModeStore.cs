using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace TeleportRequest
{
	/// <summary>
	/// 玩家传送模式：允许 / 需同意 / 拒绝
	/// </summary>
	public enum TPMode
	{
		Agree,
		Request,
		Block
	}

	/// <summary>
	/// 持久化数据模型
	/// </summary>
	internal class StoreData
	{
		public TPMode _default = TPMode.Agree;
		public Dictionary<int, TPMode> players = new Dictionary<int, TPMode>();
	}

	/// <summary>
	/// 玩家传送模式的持久化存储（线程安全）
	/// 未设置的玩家使用全局默认值，已设置的玩家全部写入文件
	/// </summary>
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

		/// <summary>获取全局默认模式</summary>
		public static TPMode DefaultMode
		{
			get { lock (Lock) { return _data._default; } }
			set { lock (Lock) { _data._default = value; Save(); } }
		}

		/// <summary>获取玩家模式，未设置则返回全局默认值</summary>
		public static TPMode GetMode(int playerId)
		{
			lock (Lock)
			{
				return _data.players.TryGetValue(playerId, out var mode) ? mode : _data._default;
			}
		}

		/// <summary>设置玩家模式，始终写入文件</summary>
		public static void SetMode(int playerId, TPMode mode)
		{
			lock (Lock)
			{
				_data.players[playerId] = mode;
				Save();
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
				catch
				{
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
