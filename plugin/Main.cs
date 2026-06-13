﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using TShockAPI;
using Terraria;
using TerrariaApi.Server;
using Rests;
using System.Data;
using TShockAPI.DB;
using TShockAPI;

namespace TShockData
{
	[ApiVersion(2, 1)]
	public class TShockData : TerrariaPlugin
	{
		public override string Author => "Jonesn";
		public override string Description => "TShockREST背包管理";
		public override string Name => "RESTInvsee";
		public override Version Version => new Version(1, 0, 0, 0);
		public TShockData(Main game) : base(game) { }
		public override void Initialize()
		{
			//RuntimeHooks.Initialize();

			TShock.RestApi.Register(new SecureRestCommand("/data/users/invsee", GetPlayerInv.GetInv, "data.rest.invsee"));
			TShock.RestApi.Register(new SecureRestCommand("/data/users/editinv", GetPlayerInv.EditInv, "data.rest.invsee"));
			TShock.RestApi.Register(new SecureRestCommand("/data/users/query_detail", QueryUsers.QueryUsersList, "data.rest.invsee"));
			TShock.RestApi.Register(new SecureRestCommand("/data/users/duplicateips", QueryUsers.QueryDuplicateIPs, "data.rest.invsee"));
			TShock.RestApi.Register(new SecureRestCommand("/data/users/ban", QueryUsers.BanPlayerByNameorID, "data.rest.invsee"));

			TShock.RestApi.Register(new SecureRestCommand("/data/groups/list", GroupOP.GetAllGroups, "data.groups"));
			TShock.RestApi.Register(new SecureRestCommand("/data/groups/get", GroupOP.GetGroup, "data.groups"));
			TShock.RestApi.Register(new SecureRestCommand("/data/groups/create", GroupOP.CreateGroup, "data.groups"));
			TShock.RestApi.Register(new SecureRestCommand("/data/groups/delete", GroupOP.DeleteGroup, "data.groups"));
			TShock.RestApi.Register(new SecureRestCommand("/data/groups/update", GroupOP.UpdateGroup, "data.groups"));
			TShock.RestApi.Register(new SecureRestCommand("/data/groups/permission/add", GroupOP.AddPermission, "data.groups"));
			TShock.RestApi.Register(new SecureRestCommand("/data/groups/permission/remove", GroupOP.RemovePermission, "data.groups"));

            TShockAPI.Commands.ChatCommands.Add(new Command("tools.runas", tools.runas, "runas"));


        }
		protected override void Dispose(bool Disposing)
		{
			if (Disposing)
			{
				RuntimeHooks.Dispose();
			}
			base.Dispose(Disposing);
		}

		
	}
}