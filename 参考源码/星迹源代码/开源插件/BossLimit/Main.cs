using TShockAPI;
using Terraria;
using TerrariaApi.Server;
using Rests;
using TShockAPI.Configuration;

namespace BossLimit
{
	//[ApiVersion(2, 1)]
	public class BossLimit : TerrariaPlugin
	{
		public override string Author => "Jonesn";
		public override string Description => "BOSS限制";
		public override string Name => "BossLimit";
		public override Version Version => new Version(1, 0, 0, 0);
		public BossLimit(Main game) : base(game) { }
		public override void Initialize()
		{
			Data.Init();
			TShock.RestApi.Register(new SecureRestCommand("/boss/add", BossAdd, "boss.rest.add"));//增
			TShock.RestApi.Register(new SecureRestCommand("/boss/del", BossDel, "boss.rest.del"));//删
			TShock.RestApi.Register(new SecureRestCommand("/boss/status", BossStatus, "boss.rest.status"));//状况
			ServerApi.Hooks.NpcSpawn.Register(this, Onspawnboss, 10);
			TShock.RestApi.Register(new SecureRestCommand("/boss/defeatboss", CheckBoss, "rest.boss"));//已击败
		}
		protected override void Dispose(bool Disposing)
		{
			if (Disposing)
			{
			}
			base.Dispose(Disposing);
		}
		private object BossStatus(RestRequestArgs args)
		{
			return new RestObject()
				{
					{
						"locked",
						Data.GetAllData()
					}
				};
		}
			private object BossDel(RestRequestArgs args)
		{
			string[] delboss = args.Parameters["boss"].Split(',');
			bool success = false;
			foreach (string bossid in delboss)
			{
				Data.DelBOSS(bossid);
				success = true;
			}
			if (success) { return new RestObject(); } else { return new RestObject("400"); }
		}
			private object BossAdd(RestRequestArgs args)
		{
			string[] addboss = args.Parameters["boss"].Split(',');
			bool success=false;
			foreach (string bossid in addboss) 
			{
				Data.Insert(bossid);
				success=true;
			}
			if (success) { return new RestObject(); } else { return new RestObject("400"); }
		}
		private void Onspawnboss(NpcSpawnEventArgs args)//控制进度的东西
		{
			var npc = Main.npc[args.NpcId];
			if (npc == null)
				return;
			if (npc.type == 4 || npc.type == 13 || npc.type == 266 || npc.type == 35 || npc.type == 345 || npc.type == 346 || npc.type == 325 || npc.type == 636 || npc.type == 370 || npc.type == 246 || npc.type == 327 || npc.type == 344 || npc.type == 392 || npc.type == 134 || npc.type == 125 || npc.type == 126 || npc.type == 127 || npc.type == 398 || npc.type == 491 || npc.type == 262 || npc.type == 222 || npc.type == 657 || npc.type == 507 || npc.type == 113 || npc.type == 517 || npc.type == 493 || npc.type == 422 || npc.type == 439 || npc.type == 668)
			{
				string[] boss = Data.GetAllData();
				int[] LockedNPC = Array.ConvertAll<string, int>(boss, s => int.Parse(s));
				if (npc.active && LockedNPC.Contains(npc.netID))
				{
					npc.netID = 0;
					npc.active = false;
					TSPlayer.All.SendData(PacketTypes.NpcUpdate, "", args.NpcId);
					TSPlayer.All.SendInfoMessage("该BOSS已被锁定，请联系服主解锁");
				}
			}
		}
		private object CheckBoss(RestRequestArgs args)//获取进度详情
		{
			bool[] boss = new bool[] {
				NPC.downedBoss1,//眼镜
                NPC.downedBoss2,//脑子虫子
                NPC.downedBoss3,//骨头
                NPC.downedChristmasIceQueen,
				NPC.downedChristmasSantank,
				NPC.downedChristmasTree,
				NPC.downedClown,//小丑
                NPC.downedEmpressOfLight,//光女
                NPC.downedFishron,//猪
                NPC.downedFrost,//霜月
                NPC.downedGoblins,//小妖精
                NPC.downedGolemBoss,//石头人
                NPC.downedHalloweenKing,//南瓜王
                NPC.downedHalloweenTree,//怪树
                NPC.downedMartians,//飞碟
                NPC.downedMechBoss1,//铁虫子
                NPC.downedMechBoss2,//双铁
                NPC.downedMechBoss3,//铁骨头
                NPC.downedMoonlord,//月总
                NPC.downedPirates,//海盗船
                NPC.downedPlantBoss,//花
                NPC.downedQueenBee,//蜂后
                NPC.downedQueenSlime,//史莱姆皇后
                NPC.downedSlimeKing,//史莱姆王
                NPC.downedTowerNebula,//红萝卜
                Main.hardMode,//肉山
                NPC.downedTowerSolar,//橙萝卜
                NPC.downedTowerStardust,//蓝萝卜
                NPC.downedTowerVortex,//旋涡塔
                NPC.downedAncientCultist,//教徒
				NPC.downedDeerclops//巨鹿
			};
			return new RestObject()
				{
					{
						"downedboss",
						boss
					}
				};
		}
	}
}