using TShockAPI;
using Terraria;
using TerrariaApi.Server;
using TShockAPI.DB;
using TShockAPI.Hooks;
using Microsoft.Xna.Framework;
using Rests;
using System.Data;
using System.Text;
using System.Collections.Generic;
using System.Collections;

namespace RESlogin
{
	[ApiVersion(2, 1)]
	public class RESlogin : TerrariaPlugin
	{
		public override string Author => "Jonesn";
		public override string Description => "魔法";
		public override string Name => "RESlogin";
		public override Version Version => new Version(1, 0, 0, 0);
		public RESlogin(Main game) : base(game) { }
		public override void Initialize()
		{
			TShock.RestApi.Register(new SecureRestCommand("/xjlogin/registeruser", UserReg, "tshock.register"));
			TShock.RestApi.Register(new SecureRestCommand("/xjlogin/loginuser", UserLogin, "tshock.login"));
		}
		protected override void Dispose(bool Disposing)
		{
			if (Disposing)
			{
			}
			base.Dispose(Disposing);
		}
		private object UserReg(RestRequestArgs args)
		{
			string name = args.Parameters["username"];
			string password = args.Parameters["password"];
			byte[] outputb = Convert.FromBase64String(password);
			password = Encoding.Default.GetString(outputb);
			string group = args.Parameters["group"];
			//string UUID = args.Parameters["UUID"];
			try
			{
				IDbConnection db = TShock.DB;
				IQueryBuilder provider;
				if (TShock.DB.GetSqlType() != SqlType.Sqlite)
				{
					IQueryBuilder queryBuilder = new MysqlQueryCreator();
					provider = queryBuilder;
				}
				else
				{
					IQueryBuilder queryBuilder = new SqliteQueryCreator();
					provider = queryBuilder;
				}
				int num;
				num = db.Query("INSERT INTO Users (Username, Password, UserGroup, Registered) VALUES (@0, @1, @2, @3);", new object[]
				{
					name,
					password,
					//UUID,
					group,
					DateTime.UtcNow.ToString("s")
				});
				if (num == 1)
				{
					return new RestObject()
			{
				{
					"response",
					"添加用户成功"
				}
			};
				}
				else
				{
					return new RestObject("400")
			{
				{
					"response",
					num
				}
			};
				}
			}
			catch (Exception ex)
			{
				return new RestObject()
			{
				{
					"response",
					ex
				}
			};
			}
		}
		private object UserLogin(RestRequestArgs args)//使用平台登录
		{
			TSPlayer player = TShockAPI.TSPlayer.FindByNameOrID(args.Parameters["name"])[0];
			if (player.IsLoggedIn)
			{
				player.SendErrorMessage("你已经登录了，无需重复登录");
				return new RestObject("401");
			}

			if (player.TPlayer.dead)
			{
				player.SendErrorMessage("死了不能登录，等你复活");
				return new RestObject("400");
			}
			if (player.TPlayer.itemTime > 0 || player.TPlayer.itemAnimation > 0)
			{
				player.SendErrorMessage("使用物品的时候不能登录");
				return new RestObject("400");
			}

			if (player.TPlayer.CCed && Main.ServerSideCharacter)
			{
				player.SendErrorMessage("You cannot login whilst crowd controlled.");
				return new RestObject("400");
			}

			UserAccount account = TShock.UserAccounts.GetUserAccountByName(args.Parameters["name"]);
			try
			{
				if (account == null)
				{
					player.SendErrorMessage("用户不存在");
					return new RestObject("400");
				}
				else if (true)
				{
					var group = TShock.Groups.GetGroupByName(account.Group);

					if (!TShock.Groups.AssertGroupValid(player, group, false))
					{
						player.SendErrorMessage("Login attempt failed - see the message above.");
						return new RestObject("400");
					}

					player.PlayerData = TShock.CharacterDB.GetPlayerData(player, account.ID);
					player.Group = group;
					player.tempGroup = null;
					player.Account = account;
					player.IsLoggedIn = true;
					player.IsDisabledForSSC = false;

					if (Main.ServerSideCharacter)
					{
						if (player.HasPermission(Permissions.bypassssc))
						{
							player.PlayerData.CopyCharacter(player);
							TShock.CharacterDB.InsertPlayerData(player);
						}
						player.PlayerData.RestoreCharacter(player);
					}
					player.LoginFailsBySsi = false;

					if (player.HasPermission(Permissions.ignorestackhackdetection))
						player.IsDisabledForStackDetection = false;

					if (player.HasPermission(Permissions.usebanneditem))
						player.IsDisabledForBannedWearable = false;

					player.SendSuccessMessage("账户 {0} 使用QQ登录成功", account.Name);

					TShock.Log.ConsoleInfo("{0} authenticated successfully as user: {1}.", player.Name, account.Name);
					if ((player.LoginHarassed) && (TShock.Config.Settings.RememberLeavePos))
					{
						if (TShock.RememberedPos.GetLeavePos(player.Name, player.IP) != Vector2.Zero)
						{
							Vector2 pos = TShock.RememberedPos.GetLeavePos(player.Name, player.IP);
							player.Teleport((int)pos.X * 16, (int)pos.Y * 16);
						}
						player.LoginHarassed = false;

					}
					TShock.UserAccounts.SetUserAccountUUID(account, player.UUID);
					TShockAPI.Hooks.PlayerHooks.OnPlayerPostLogin(player);
					return new RestObject();
				}
			}
			catch (Exception ex)
			{
				player.SendErrorMessage("登录失败，未知错误");
				TShock.Log.Error(ex.ToString());
				return new RestObject("400");
			}
		}
	}
}