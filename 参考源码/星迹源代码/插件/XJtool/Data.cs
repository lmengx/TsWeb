using System.Data;
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using static Terraria.ID.ContentSamples.CreativeHelper;

namespace XJtool
{
    internal class Data
    {
        public static SqliteConnection DB;
        const string path = "tshock/XJtool.sqlite";
        public static void Init()
        {
            DB = new SqliteConnection($"Data Source={path};");
            DB.Open();
            Command("create table if not exists Plugins(PluginName text)");
        }
        public static SqliteDataReader Command(string cmd)
        {
            return new SqliteCommand(cmd, DB).ExecuteReader();
        }
        public static bool Insert(string PluginName)
        {
			using (var reader = Command($"select * from Plugins where PluginName='{PluginName}'"))
			{
				while (reader.Read())
				{
					if (reader.GetString(0) == PluginName)
						return false;
				}
			}
			Command($"insert into Plugins(PluginName)values('{PluginName}')");
			return true;
        }
		public static string[] GetAllData()
		{
			List<string> Res = new List<string>();
			using (var reader = Command("select * from Plugins"))
			{
				while (reader.Read())
				{
					Res.Add (reader.GetString(0));
				}
			}
			return Res.ToArray();
		}
		public static bool DelPlugin(string name)
		{
			Command($"DELETE FROM Plugins where PluginName='{name}'");
			return true;
		}
	}
}
