using TShockAPI;
using Terraria;
using TerrariaApi.Server;
using Rests;
using System.Globalization;
using TShockAPI.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static Org.BouncyCastle.Math.EC.ECCurve;

namespace TShockData
{
	[ApiVersion(2, 1)]
	public class TShockData : TerrariaPlugin
	{
		public override string Author => "Jonesn";
		public override string Description => "TShock数据管理";
		public override string Name => "TShockData";
		public override Version Version => new Version(1, 0, 0, 0);
		public TShockData(Main game) : base(game) { }
		public override void Initialize()
		{
			TShock.RestApi.Register(new SecureRestCommand("/data/users/loadall", LoadAllUsers, "data.rest.loadall"));//加载全部用户
		}
		protected override void Dispose(bool Disposing)
		{
			if (Disposing)
			{
			}
			base.Dispose(Disposing);
		}
		private object LoadAllUsers(RestRequestArgs args)//加载全部用户
		{
			List<UserAccount> users = TShock.UserAccounts.GetUserAccounts();
			return new RestObject()
			{
				{
					"users",
					users
				}
			};
		}
	}
}