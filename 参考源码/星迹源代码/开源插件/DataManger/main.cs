using Google.Protobuf;
using Microsoft.Xna.Framework;
using Rests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using static MonoMod.InlineRT.MonoModRule;

namespace DataManger
{
	[ApiVersion(2, 1)]
	public class DataManger : TerrariaPlugin
	{
		public override string Name => "DataManger";
		public override Version Version => new Version(1, 0);
		public override string Author => "Jonesn";
		public override string Description => "统计并排行数据";
		public DataManger(Main game) : base(game)
		{

		}
		public override void Initialize()
		{
			Data.Init();
			TShock.RestApi.Register(new SecureRestCommand("/getbossinfo", GetBossInfo, "boss.rest.info"));
			TShock.RestApi.Register(new SecureRestCommand("/getallbossinfo", LoadAll, "boss.rest.info"));
			ServerApi.Hooks.NpcSpawn.Register(this, OnNpcSpawn);
			ServerApi.Hooks.NpcStrike.Register(this, OnStrike);
			ServerApi.Hooks.NpcKilled.Register(this, OnNpcKill);
		}
		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				ServerApi.Hooks.NpcSpawn.Deregister(this, OnNpcSpawn);
				ServerApi.Hooks.NpcStrike.Deregister(this, OnStrike);
				ServerApi.Hooks.NpcKilled.Deregister(this, OnNpcKill);
				DamageList.Clear();
			}
			base.Dispose(disposing);
		}
		private object GetBossInfo(RestRequestArgs args)//加载全部cdk
		{
			int bossinfoid = int.Parse(args.Parameters["ID"]);
			var bossinfo = Data.GetData(bossinfoid);
			return new RestObject()
			{
				{
					"data",
					bossinfo
				}
			};
		}
		private readonly Dictionary<NPC, Dictionary<string, double>> DamageList = new Dictionary<NPC, Dictionary<string, double>>();
		private readonly Dictionary<NPC, Dictionary<string, double>> Most = new Dictionary<NPC, Dictionary<string, double>>();
		private readonly Dictionary<NPC, string[]> LastOne = new Dictionary<NPC, string[]>();

		private void OnNpcSpawn(NpcSpawnEventArgs args)
		{
			NPC npc = Main.npc[args.NpcId];
			if (npc.boss)
			{
				DamageList.Add(npc, new Dictionary<string, double>());
				Most.Add(npc, new Dictionary<string, double>());
				LastOne.Add(npc, new string[2]);
				DateTime dt = DateTime.Now;
				LastOne[npc][0] = dt.ToString("yyyy-MM-dd HH:mm:ss");
			}
		}
		private void OnStrike(NpcStrikeEventArgs args)
		{
			if (DamageList.ContainsKey(args.Npc))
			{
				if (!DamageList[args.Npc].ContainsKey(args.Player.name))
				{
					DamageList[args.Npc].Add(args.Player.name, 0);
					Most[args.Npc].Add(args.Player.name, 0);
				}
				DamageList[args.Npc][args.Player.name] += args.Damage;
				LastOne[args.Npc][1] = args.Player.name;
				if (args.Damage > Most[args.Npc][args.Player.name]) Most[args.Npc][args.Player.name] = args.Damage;
			}
		}

		private void OnNpcKill(NpcKilledEventArgs args)
		{
			if (DamageList.ContainsKey(args.npc) && DamageList[args.npc].Any())
			{
				var data = DamageList[args.npc];
				var mostdam = Most[args.npc];
				string MostOne = mostdam.Keys.MaxBy(key => mostdam[key]);
				double MostDam = mostdam[MostOne];
				string Lastone = LastOne[args.npc][1];
				string Last = LastOne[args.npc][0];
				DateTime dt = DateTime.Now;
				DateTime startTime;
				DateTime.TryParse(Last, out startTime);
				TimeSpan timeDiff = dt - startTime;
				List<BossStatus> statuses = new List<BossStatus>();
				data.ForEach(p => statuses.Add(new BossStatus(p.Key, data[p.Key])));
				var damlist= statuses.OrderByDescending(o => o.value).ToList();
				Data.Insert(args.npc.FullName, damlist, MostOne,MostDam,Lastone,timeDiff.Minutes+"分"+timeDiff.Seconds+"秒");
				DamageList.Remove(args.npc);
				LastOne.Remove(args.npc);
				Most.Remove(args.npc);
			}
		}
		private object LoadAll(RestRequestArgs args)//加载全部BOSS及其ID
		{
			var allBoss = Data.GetAllData();
			return new RestObject()
			{
				{
					"data",
					allBoss
				}
			};
		}
	}
}
