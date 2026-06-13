using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using static Terraria.ID.ContentSamples.CreativeHelper;
using MonoMod.RuntimeDetour;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace DataManger
{
	public class BossStatus
	{
		public string name { get; set; }
		public double value { get; set; }
		public BossStatus(string Name, double damage)
		{
			name = Name;
			value = damage;
		}
	}
	public class bossInfo
	{
		public string BossName { get; set; }
		public string MostOne { get; set; }
		public double MostDam { get; set; }
		public string LastOne { get; set; }
		public string Last { get; set; }
		public List<BossStatus> damage { get; set; }
	}
	public class bossList
	{
		public string BossName { get; set; }
		public int BossID { get; set; }
	}
	internal class Data
	{
		public static SqliteConnection DB;
		const string path = "tshock/DataManger.sqlite";
		public static void Init()
		{
			DB = new SqliteConnection($"Data Source={path};");
			DB.Open();
			Command("create table if not exists BossStatus (ID INTEGER PRIMARY KEY AUTOINCREMENT, bossname VARCHAR (255), damage TEXT, MostOne VARCHAR (255), MostDam INT (12), LastOne VARCHAR (255), Last VARCHAR (255))");
		}
		public static SqliteDataReader Command(string cmd)
		{
			return new SqliteCommand(cmd, DB).ExecuteReader();
		}
		public static bool Insert(string BossName, List<BossStatus> damage, string MostOne, double MostDam, string LastOne, string Last)
		{
			string json = JsonConvert.SerializeObject(damage);
			Command($"insert into BossStatus(bossname,damage,MostOne,MostDam,LastOne,Last)values('{BossName}','{json}','{MostOne}','{MostDam}','{LastOne}','{Last}')");
			return true;
		}
		public static bossInfo GetData(int ID)
		{
			bossInfo Info = new bossInfo();
			using (var reader = Command($"select * from BossStatus where ID='{ID}'"))
			{
				while (reader.Read())
				{
					var Damage = JsonConvert.DeserializeObject<List<BossStatus>>(reader.GetString(2));
					Info = new bossInfo() { BossName = reader.GetString(1), MostOne = reader.GetString(3), MostDam = reader.GetDouble(4), LastOne = reader.GetString(5), Last = reader.GetString(6),damage = Damage };
				}
			}
			return Info;
		}
		public static bossList[] GetAllData()
		{
			List<bossList> Res = new List<bossList>();
			using (var reader = Command("select * from BossStatus"))
			{
				while (reader.Read())
				{
					Res.Add(new bossList() { BossName = reader.GetString(1) ,BossID=reader.GetInt32(0)});
				}
			}
			return Res.ToArray();
		}
	}
}
