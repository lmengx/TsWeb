using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Rests;
using TShockAPI;

namespace TShockData
{
	/// <summary>
	/// 反作弊检测日志（AntiCheatLog）：
	///
	/// 每次反作弊检测命中（物品违禁 / 弹幕违禁 / 粒子恶意 / 粒子超频丢弃）都会落一条日志，
	/// 存储在本机 SQLite（AntiCheatLog.sqlite），供网页端「反作弊日志」页分页查询与统计。
	///
	/// 设计约束：
	///  - 写入绝不抛出异常（try/catch + 串行锁），检测主流程不受日志影响；
	///  - 使用 Pooling=false 连接，避免连接池句柄占用导致文件锁（对齐 AutoBackup 的经验）；
	///  - Time 存 "yyyy-MM-dd HH:mm:ss" 文本，字典序即时间序，查询区间直接字符串比较。
	/// </summary>
	public static class AntiCheatLog
	{
		private static readonly object WriteLock = new object();

		private static string DbPath => Path.Combine(TShock.SavePath, "AntiCheatLog.sqlite");
		private static string ConnectionString => $"Data Source={DbPath};Pooling=false";

		// 单次批量写入上限（前端一页最多显示 100 条，查询时防止一次性拉全库）
		public const int MaxQueryPageSize = 200;

		public static void Initialize()
		{
			try
			{
				EnsureTable();
				TShock.Log.ConsoleInfo($"[TSWeb] 反作弊日志模块已初始化 ({DbPath})");
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[TSWeb] 反作弊日志初始化失败: {ex.Message}");
			}
		}

		private static SqliteConnection GetConnection()
		{
			var conn = new SqliteConnection(ConnectionString);
			conn.Open();
			return conn;
		}

		public static void EnsureTable()
		{
			var dir = Path.GetDirectoryName(DbPath);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
				Directory.CreateDirectory(dir);

			using var conn = GetConnection();
			using var cmd = conn.CreateCommand();
			cmd.CommandText = @"
				CREATE TABLE IF NOT EXISTS CheatLog (
					Id         INTEGER PRIMARY KEY AUTOINCREMENT,
					Time       TEXT    NOT NULL,
					PlayerName TEXT    NOT NULL DEFAULT '',
					PlayerId   INTEGER NOT NULL DEFAULT 0,
					Category   TEXT    NOT NULL DEFAULT '',
					ItemId     INTEGER NOT NULL DEFAULT 0,
					ItemName   TEXT    NOT NULL DEFAULT '',
					ProjId     INTEGER NOT NULL DEFAULT 0,
					Method     TEXT    NOT NULL DEFAULT '',
					Detail     TEXT    NOT NULL DEFAULT '',
					WorldId    TEXT    NOT NULL DEFAULT ''
				);
				CREATE INDEX IF NOT EXISTS idx_cheatlog_time ON CheatLog (Time DESC);
				CREATE INDEX IF NOT EXISTS idx_cheatlog_player ON CheatLog (PlayerName);
				CREATE INDEX IF NOT EXISTS idx_cheatlog_category ON CheatLog (Category);
			";
			cmd.ExecuteNonQuery();

			// 旧库补列（SQLite 不支持 IF NOT EXISTS 的 ADD COLUMN）
			TryAddColumn(conn, "PlayerId", "INTEGER NOT NULL DEFAULT 0");
			TryAddColumn(conn, "ProjId", "INTEGER NOT NULL DEFAULT 0");
		}

		private static void TryAddColumn(SqliteConnection conn, string column, string def)
		{
			try
			{
				using var alter = conn.CreateCommand();
				alter.CommandText = $"ALTER TABLE CheatLog ADD COLUMN {column} {def}";
				alter.ExecuteNonQuery();
			}
			catch
			{
				// 列已存在，忽略
			}
		}

		// ═══════════════════════════════════════════════
		//  写入
		// ═══════════════════════════════════════════════

		/// <summary>记录一条反作弊检测日志（调用点见 ItemDetection / ItemRestrict / ProjDetection / ParticleGuard）</summary>
		/// <param name="playerName">玩家名</param>
		/// <param name="playerId">账号 ID（无则 0）</param>
		/// <param name="category">分类：item / proj / particle</param>
		/// <param name="method">处理方式：log / kick / ban / drop / query</param>
		/// <param name="detail">人类可读详情</param>
		/// <param name="itemId">物品 ID（非物品类传 0）</param>
		/// <param name="itemName">物品名</param>
		/// <param name="projId">弹幕 ID（非弹幕类传 0）</param>
		public static void Record(string playerName, int playerId, string category, string method, string detail,
			int itemId = 0, string itemName = "", int projId = 0)
		{
			try
			{
				lock (WriteLock)
				{
					using var conn = GetConnection();
					using var cmd = conn.CreateCommand();
					cmd.CommandText = @"
						INSERT INTO CheatLog (Time, PlayerName, PlayerId, Category, ItemId, ItemName, ProjId, Method, Detail, WorldId)
						VALUES (@t, @p, @pid, @c, @iid, @iname, @proj, @m, @d, @w)";
					cmd.Parameters.AddWithValue("@t", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
					cmd.Parameters.AddWithValue("@p", playerName ?? "");
					cmd.Parameters.AddWithValue("@pid", playerId);
					cmd.Parameters.AddWithValue("@c", category ?? "");
					cmd.Parameters.AddWithValue("@iid", itemId);
					cmd.Parameters.AddWithValue("@iname", itemName ?? "");
					cmd.Parameters.AddWithValue("@proj", projId);
					cmd.Parameters.AddWithValue("@m", method ?? "");
					cmd.Parameters.AddWithValue("@d", detail ?? "");
					cmd.Parameters.AddWithValue("@w", GetWorldId());
					cmd.ExecuteNonQuery();
				}
			}
			catch (Exception ex)
			{
				// 日志失败绝不影响检测主流程
				TShock.Log.ConsoleError($"[TSWeb] 反作弊日志写入失败: {ex.Message}");
			}
		}

		private static string GetWorldId()
		{
			try
			{
				return Terraria.Main.worldID.ToString();
			}
			catch
			{
				return "";
			}
		}

		// ═══════════════════════════════════════════════
		//  查询（REST）
		// ═══════════════════════════════════════════════

		/// <summary>GET /data/anticheat/logs — 分页查询反作弊日志</summary>
		/// <remarks>参数：page / pageSize / player（玩家名模糊）/ category / method / timeFrom / timeTo / q（关键字模糊）</remarks>
		public static object GetLogsApi(RestRequestArgs args)
		{
			try
			{
				int page = ParseInt(GetParam(args, "page"), 1);
				int pageSize = ParseInt(GetParam(args, "pageSize"), 50);
				pageSize = Math.Clamp(pageSize, 1, MaxQueryPageSize);
				if (page < 1) page = 1;

				var where = new List<string>();
				var pars = new Dictionary<string, object>();

				AddFilter(where, pars, "Category", GetParam(args, "category"));
				AddFilter(where, pars, "Method", GetParam(args, "method"));

				var player = GetParam(args, "player");
				if (!string.IsNullOrWhiteSpace(player))
				{
					where.Add("PlayerName LIKE @player");
					pars["@player"] = $"%{player}%";
				}

				var q = GetParam(args, "q");
				if (!string.IsNullOrWhiteSpace(q))
				{
					where.Add("(PlayerName LIKE @q OR ItemName LIKE @q OR Detail LIKE @q)");
					pars["@q"] = $"%{q}%";
				}

				var timeFrom = GetParam(args, "timeFrom");
				if (!string.IsNullOrWhiteSpace(timeFrom))
				{
					where.Add("Time >= @tf");
					pars["@tf"] = timeFrom;
				}

				var timeTo = GetParam(args, "timeTo");
				if (!string.IsNullOrWhiteSpace(timeTo))
				{
					where.Add("Time <= @tt");
					pars["@tt"] = timeTo;
				}

				string whereSql = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

				using var conn = GetConnection();

				// 总数
				long total = 0;
				using (var countCmd = conn.CreateCommand())
				{
					countCmd.CommandText = $"SELECT COUNT(*) FROM CheatLog {whereSql}";
					foreach (var kv in pars)
						countCmd.Parameters.AddWithValue(kv.Key, kv.Value);
					total = Convert.ToInt64(countCmd.ExecuteScalar());
				}

				// 分页数据（Time DESC 排序，最新在前）
				var rows = new List<object>();
				using (var qCmd = conn.CreateCommand())
				{
					qCmd.CommandText = $@"
						SELECT Id, Time, PlayerName, PlayerId, Category, ItemId, ItemName, ProjId, Method, Detail, WorldId
						FROM CheatLog
						{whereSql}
						ORDER BY Id DESC
						LIMIT @limit OFFSET @offset";
					foreach (var kv in pars)
						qCmd.Parameters.AddWithValue(kv.Key, kv.Value);
					qCmd.Parameters.AddWithValue("@limit", pageSize);
					qCmd.Parameters.AddWithValue("@offset", (page - 1) * pageSize);

					using var reader = qCmd.ExecuteReader();
					while (reader.Read())
					{
						rows.Add(new
						{
							id = reader.GetInt64(0),
							time = reader.GetString(1),
							playerName = reader.GetString(2),
							playerId = reader.GetInt32(3),
							category = reader.GetString(4),
							itemId = reader.GetInt32(5),
							itemName = reader.GetString(6),
							projId = reader.GetInt32(7),
							method = reader.GetString(8),
							detail = reader.GetString(9),
							worldId = reader.GetString(10)
						});
					}
				}

				int totalPages = (int)Math.Ceiling(total / (double)pageSize);
				return new
				{
					status = 200,
					rows,
					total,
					page,
					pageSize,
					totalPages
				};
			}
			catch (Exception ex)
			{
				return new { status = 500, error = ex.Message };
			}
		}

		/// <summary>GET /data/anticheat/logs/stats — 反作弊日志统计（总量 / 今日 / 按分类 / 按处理方式 / 最近命中）</summary>
		public static object GetStatsApi(RestRequestArgs args)
		{
			try
			{
				using var conn = GetConnection();

				long total = 0, today = 0;
				using (var c = conn.CreateCommand())
				{
					c.CommandText = "SELECT COUNT(*) FROM CheatLog";
					total = Convert.ToInt64(c.ExecuteScalar());
				}
				using (var c = conn.CreateCommand())
				{
					var todayStart = DateTime.Today.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
					c.CommandText = "SELECT COUNT(*) FROM CheatLog WHERE Time >= @ts";
					c.Parameters.AddWithValue("@ts", todayStart);
					today = Convert.ToInt64(c.ExecuteScalar());
				}

				var byCategory = new Dictionary<string, long>();
				using (var c = conn.CreateCommand())
				{
					c.CommandText = "SELECT Category, COUNT(*) AS cnt FROM CheatLog GROUP BY Category";
					using var r = c.ExecuteReader();
					while (r.Read())
						byCategory[r.GetString(0)] = r.GetInt64(1);
				}

				var byMethod = new Dictionary<string, long>();
				using (var c = conn.CreateCommand())
				{
					c.CommandText = "SELECT Method, COUNT(*) AS cnt FROM CheatLog GROUP BY Method";
					using var r = c.ExecuteReader();
					while (r.Read())
						byMethod[r.GetString(0)] = r.GetInt64(1);
				}

				// 最近 10 条（供统计卡片下方滚动展示）
				var recent = new List<object>();
				using (var c = conn.CreateCommand())
				{
					c.CommandText = @"
						SELECT Id, Time, PlayerName, Category, ItemName, ProjId, Method, Detail
						FROM CheatLog
						ORDER BY Id DESC
						LIMIT 10";
					using var r = c.ExecuteReader();
					while (r.Read())
					{
						recent.Add(new
						{
							id = r.GetInt64(0),
							time = r.GetString(1),
							playerName = r.GetString(2),
							category = r.GetString(3),
							itemName = r.GetString(4),
							projId = r.GetInt32(5),
							method = r.GetString(6),
							detail = r.GetString(7)
						});
					}
				}

				return new { status = 200, total, today, byCategory, byMethod, recent };
			}
			catch (Exception ex)
			{
				return new { status = 500, error = ex.Message };
			}
		}

		// ═══════════════════════════════════════════════
		//  辅助
		// ═══════════════════════════════════════════════

		private static void AddFilter(List<string> where, Dictionary<string, object> pars, string column, string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return;
			where.Add($"{column} = @{column}");
			pars[$"@{column}"] = value;
		}

		private static string GetParam(RestRequestArgs args, string key)
		{
			try
			{
				object? v = args.Parameters[key];
				return v?.ToString() ?? "";
			}
			catch
			{
				return "";
			}
		}

		private static int ParseInt(string s, int def)
		{
			return int.TryParse(s, out var v) ? v : def;
		}
	}
}
