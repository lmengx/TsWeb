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
	/// 玩家传送模式的持久化存储（线程安全）
	/// 默认 agree 不存入文件，节省 IO
	/// </summary>
	public static class TPModeStore
	{
		private static readonly object Lock = new object();
		private static Dictionary<int, TPMode> _modes = new Dictionary<int, TPMode>();
		private static string _filePath;

		public static void Initialize(string savePath)
		{
			_filePath = Path.Combine(savePath, "tpplus.json");
			Load();
		}

		public static TPMode GetMode(int playerId)
		{
			lock (Lock)
			{
				return _modes.TryGetValue(playerId, out var mode) ? mode : TPMode.Agree;
			}
		}

		public static void SetMode(int playerId, TPMode mode)
		{
			lock (Lock)
			{
				if (mode == TPMode.Agree)
					_modes.Remove(playerId);
				else
					_modes[playerId] = mode;

				Save();
			}
		}

		private static void Load()
		{
			lock (Lock)
			{
				_modes.Clear();

				if (!File.Exists(_filePath))
					return;

				try
				{
					var json = File.ReadAllText(_filePath);
					_modes = JsonConvert.DeserializeObject<Dictionary<int, TPMode>>(json)
					         ?? new Dictionary<int, TPMode>();
				}
				catch
				{
					_modes = new Dictionary<int, TPMode>();
				}
			}
		}

		private static void Save()
		{
			lock (Lock)
			{
				var json = JsonConvert.SerializeObject(_modes, Formatting.Indented);
				File.WriteAllText(_filePath, json);
			}
		}
	}
}
