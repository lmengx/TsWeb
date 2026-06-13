using TShockAPI;
using Terraria;
using TerrariaApi.Server;
using Rests;
using System.Globalization;
using Microsoft.Xna.Framework;

namespace TrCDK
{
	//[ApiVersion(2, 1)]
	public class TrCDK : TerrariaPlugin
	{
		public override string Author => "Jonesn";
		public override string Description => "CDK系统";
		public override string Name => "TrCDK";
		public override Version Version => new Version(1, 0, 0, 0);
		public TrCDK(Main game) : base(game) { }
		public override void Initialize()
		{
			Data.Init();
			TShock.RestApi.Register(new SecureRestCommand("/cdk/loadall", LoadAll, "cdk.rest.loadall"));//加载全部CDK
			TShock.RestApi.Register(new SecureRestCommand("/cdk/addcdk", AddCDK, "cdk.rest.addcdk"));//添加CDK
			TShock.RestApi.Register(new SecureRestCommand("/cdk/delcdk", DelCDK, "cdk.rest.delcdk"));//删除CDK
			TShock.RestApi.Register(new SecureRestCommand("/cdk/updatecdk", UpdateCDK, "cdk.rest.update"));//更改CDK
			TShock.RestApi.Register(new SecureRestCommand("/cdk/give", UseCmd, "cdk.rest.give"));//更改CDK
			Commands.ChatCommands.Add(new Command("cdk.use", UseCDK, "cdk"));
		}
		protected override void Dispose(bool Disposing)
		{
			if (Disposing)
			{
			}
			base.Dispose(Disposing);
		}
		private object AddCDK(RestRequestArgs args)//添加CDK
		{
			string CDKname = args.Request.Parameters["CDKname"];
			int Usetime = int.Parse(args.Parameters["Usetime"]);
			string Utiltime = args.Parameters["Utiltime"];
			string Grouplimit = args.Parameters["Grouplimit"];
			string Playerlimit = args.Parameters["Playerlimit"];
			string Cmds = args.Parameters["Cmds"];
			DateTimeFormatInfo timeForInfo = new DateTimeFormatInfo();
			timeForInfo.ShortDatePattern = "yyyy-MM-ddThh:ii";
			var time = Convert.ToDateTime(Utiltime, timeForInfo);
			if (Data.Insert(CDKname, Usetime, Convert.ToDateTime(time.ToString()).Ticks, Grouplimit, Playerlimit, Cmds))
			{
				return new RestObject();
			}
			else
			{
				return new RestObject()
				{
					Status = "401"
				};
			}

		}
		private object DelCDK(RestRequestArgs args)//删除CDK
		{
			string CDKname = args.Parameters["CDKname"];
			Data.DelCDK(CDKname);
			return new RestObject();
		}
		private object UpdateCDK(RestRequestArgs args)//更新CDK
		{
			string CDKname = args.Parameters["CDKname"];
			int Usetime = int.Parse(args.Parameters["Usetime"]);
			string Utiltime = args.Parameters["Utiltime"];
			string Grouplimit = args.Parameters["Grouplimit"];
			string Playerlimit = args.Parameters["Playerlimit"];
			string Used = args.Parameters["Used"];
			string Cmds = args.Parameters["Cmds"];
			DateTimeFormatInfo timeForInfo = new DateTimeFormatInfo();
			timeForInfo.ShortDatePattern = "yyyy-MM-ddThh:ii";
			var time = Convert.ToDateTime(Utiltime, timeForInfo);
			Data.Update(CDKname, Usetime, Convert.ToDateTime(time.ToString()).Ticks, Grouplimit, Playerlimit, Used, Cmds);
			return new RestObject();

		}
		private object LoadAll(RestRequestArgs args)//加载全部cdk
		{
			var allcdk = Data.GetAllData();
			return new RestObject()
			{
				{
					"data",
					allcdk
				}
			};
		}
		private object UseCmd(RestRequestArgs args)
		{
			var player1 = TShockAPI.TSPlayer.FindByNameOrID(args.Parameters["name"]);
			string thecmds = args.Parameters["cmds"];
			if (player1.Count() > 0 && thecmds != "")
			{
				string[] cmds = thecmds.Split(',');
				foreach (string cmd in cmds)
				{
					player1[0].PermissionlessInvoke(cmd);
				}
				return new RestObject();
			}
			else 
			{
				return new RestObject("400");
			}
		}
		private void UseCDK(CommandArgs args)
		{
			if (args.Parameters.Count == 0)
			{
				args.Player.SendInfoMessage("[c/FF0000:【CDK】]\n[c/ffd700:指令:/cdk CDK兑换码  -  兑换一个CDK礼包]");
			}
			else
			{
				CDK cdk1 = Data.GetData(args.Parameters[0]);
				if (cdk1.Playerlimit != "")
				{
					if (!cdk1.Playerlimit.Contains(args.Player.Name))
					{
						args.Player.SendInfoMessage("[c/FF0000:【CDK】]\n[c/ffd700:你不在该CDK的领取名单中]");
						return;
					}
				}
				if (cdk1.Grouplimit != "")
				{
					if (!cdk1.Grouplimit.Contains(args.Player.Group.Name))
					{
						args.Player.SendInfoMessage("[c/FF0000:【CDK】]\n[c/ffd700:你所在的组不能领取该CDK]");
						return;
					}
				}
				if (cdk1.Usetime < 1)
				{
					args.Player.SendInfoMessage("[c/FF0000:【CDK】]\n[c/ffd700:你手慢了, 该CDK已经被领完]");
					return;
				}
				long time2 = Convert.ToDateTime(DateTime.Now.ToString()).Ticks;
				long min = (cdk1.Utiltime - time2) / 10000000;
				if (min <= 0)
				{
					args.Player.SendInfoMessage("[c/FF0000:【CDK】]\n[c/ffd700:你来晚了, 该CDK已经过期了]");
					return;
				}
				if (cdk1.Used.Contains(args.Player.Name))
				{
					args.Player.SendInfoMessage("[c/FF0000:【CDK】]\n[c/ffd700:已经领取过了，不能太贪心哦]");
					return;
				}
				string strcmd = cdk1.Cmds.Replace("[plr]", args.Player.Name);
				string[] cmds = strcmd.Split(',');
				foreach (string cmd in cmds)
				{
					args.Player.PermissionlessInvoke(cmd);
				}
				Data.Update(cdk1.Cdkname, cdk1.Usetime - 1, cdk1.Utiltime, cdk1.Grouplimit, cdk1.Playerlimit, (cdk1.Used=="")? args.Player.Name:cdk1.Used + "," + args.Player.Name, cdk1.Cmds);
			}
		}
	}
}