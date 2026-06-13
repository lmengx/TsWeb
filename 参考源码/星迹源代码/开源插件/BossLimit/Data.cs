using Microsoft.Data.Sqlite;

namespace BossLimit
{
    internal class Data
    {
        public static SqliteConnection DB;
        const string path = "tshock/BossLimit.sqlite";
        public static void Init()
        {
            DB = new SqliteConnection($"Data Source={path};");
            DB.Open();
            Command("create table if not exists BossLimit(BOSS text)");
        }
        public static SqliteDataReader Command(string cmd)
        {
            return new SqliteCommand(cmd, DB).ExecuteReader();
        }

        public static bool Insert(string BOSS)
        {
			string boss = ((int.Parse(BOSS) * 6) - 6).ToString();
			using (var reader = Command($"select * from BossLimit where BOSS='{boss}'"))
			{
				while (reader.Read())
				{
					if (reader.GetString(0) == boss)
						return false;
				}
			}
			Command($"insert into BossLimit(BOSS)values('{boss}')");
			return true;
        }

		public static string[] GetAllData()
		{
			List<string> Res = new List<string>();
			using (var reader = Command("select * from BossLimit"))
			{
				while (reader.Read())
				{
					Res.Add (((int.Parse(reader.GetString(0)) + 6) / 6).ToString());
				}
			}
			return Res.ToArray();
		}

		public static bool DelBOSS(string name)
		{
			string boss = ((int.Parse(name) * 6) - 6).ToString();
			Command($"DELETE FROM BossLimit where BOSS='{boss}'");
			return true;
		}
	}
}
