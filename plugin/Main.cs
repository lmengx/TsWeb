﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using TShockAPI;
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
		public override string Author => "lmx12330";
		public override string Description => "TShockRESTweb管理";
		public override string Name => "TSWeb";
		public override Version Version => new Version(1, 0, 0, 0);
		public TShockData(Main game) : base(game) { }
		public override void Initialize()
		{
			RuntimeHooks.Initialize();

            BypassHelper.RegisterPermissionHook();

            AutoRegister.Initialize(this);

			TShock.RestApi.Register(new SecureRestCommand("/data/users/invsee", GetPlayerInv.GetInv, "data.rest.invsee"));
			TShock.RestApi.Register(new SecureRestCommand("/data/users/editinv", GetPlayerInv.EditInv, "data.rest.invsee"));
			TShock.RestApi.Register(new SecureRestCommand("/data/users/query_detail", QueryUsers.QueryUsersList, "data.rest.invsee"));
			TShock.RestApi.Register(new SecureRestCommand("/data/users/stats", PlayerStats.GetPlayerStats, "data.rest.invsee"));
			TShock.RestApi.Register(new SecureRestCommand("/data/users/stats/set", PlayerStats.SetPlayerStats, "data.rest.invsee"));
			TShock.RestApi.Register(new SecureRestCommand("/data/users/duplicateips", QueryUsers.QueryDuplicateIPs, "data.rest.invsee"));
			TShock.RestApi.Register(new SecureRestCommand("/data/users/allduplicateips", QueryUsers.QueryAllDuplicateIPs, "data.rest.invsee"));
			TShock.RestApi.Register(new SecureRestCommand("/data/users/ban", QueryUsers.BanPlayerByNameorID, "data.rest.invsee"));

			TShock.RestApi.Register(new SecureRestCommand("/data/groups/list", GroupOP.GetAllGroups, "data.groups"));
			TShock.RestApi.Register(new SecureRestCommand("/data/groups/get", GroupOP.GetGroup, "data.groups"));
			TShock.RestApi.Register(new SecureRestCommand("/data/groups/create", GroupOP.CreateGroup, "data.groups"));
			TShock.RestApi.Register(new SecureRestCommand("/data/groups/delete", GroupOP.DeleteGroup, "data.groups"));
			TShock.RestApi.Register(new SecureRestCommand("/data/groups/update", GroupOP.UpdateGroup, "data.groups"));
			TShock.RestApi.Register(new SecureRestCommand("/data/groups/permission/add", GroupOP.AddPermission, "data.groups"));
			TShock.RestApi.Register(new SecureRestCommand("/data/groups/permission/remove", GroupOP.RemovePermission, "data.groups"));

            TShock.RestApi.Register(new SecureRestCommand("/data/users/getpassword", QueryPwd.GetUserPassword, "data.rest.invsee"));

			TShock.RestApi.Register(new SecureRestCommand("/data/anticheat/proj-config/getprojconfig", ProjConfigHandler.GetProjConfig, "tshock.admin"));
            TShock.RestApi.Register(new SecureRestCommand("/data/anticheat/proj-config/saveprojconfig", ProjConfigHandler.SaveProjConfig, "tshock.admin"));
            TShock.RestApi.Register(new SecureRestCommand("/data/anticheat/item-config/getitemconfig", ItemConfigHandler.GetItemConfigApi, "tshock.admin"));
            TShock.RestApi.Register(new SecureRestCommand("/data/anticheat/item-config/saveitemconfig", ItemConfigHandler.SaveItemConfigApi, "tshock.admin"));
            TShock.RestApi.Register(new SecureRestCommand("/data/anticheat/item-config/scanall", ItemConfigHandler.ScanAllItemsApi, "tshock.admin"));

            TShockAPI.Commands.ChatCommands.Add(new Command("tools.runas", tools.runas, "runas"));

			TShockAPI.Commands.ChatCommands.Add(new Command("tshock.admin.ban", tools.banp, "banp"));
            TShockAPI.Commands.ChatCommands.Add(new Command("tshock.admin", tools.remove, "remove"));
            TShockAPI.Commands.ChatCommands.Add(new Command("tshock.admin", tools.find, "find"));

            TShockAPI.Commands.ChatCommands.Add(new Command("", BossProgress.GetBossInfo, "进度", "bossinfo"));

            TShock.RestApi.Register(new SecureRestCommand("/data/boss/progress", BossProgress.GetBossInfoJson, ""));

            TShockAPI.Commands.ChatCommands.Add(new Command("tools.planoff", PlannedOff.PlanOff, "planoff"));
            PlannedOff.Initialize(this);

            TShockAPI.Commands.ChatCommands.Add(new Command("tshock.admin", AutoRegister.HandleCommand, "autoregister", "ar"));

            TShockAPI.Commands.ChatCommands.Add(new Command("tshock.admin", ExportPlayer.Export, "export", "导出"));

            TShockAPI.Commands.ChatCommands.Add(new Command("", PasswordManager.ChangePassword, "pwd", "密码"));

            AntiCheat.Initialize();
            ProjDetection.Initialize();
            ItemConfigHandler.LoadItemConfig();
            ItemDetection.Initialize();

            OnlineData.Initialize(this);

            TShock.RestApi.Register(new SecureRestCommand("/data/online/hourly", OnlineData.GetHourlyOnline, "data.rest.invsee"));
            TShock.RestApi.Register(new SecureRestCommand("/data/online/ranking", OnlineData.GetRanking, "data.rest.invsee"));
            TShock.RestApi.Register(new SecureRestCommand("/data/online/player", OnlineData.GetPlayerCalendar, "data.rest.invsee"));

            TShockAPI.Commands.ChatCommands.Add(new Command("tshock.admin", AntiCheat.HandleScanCommand, "scan", "扫描"));

            TShockAPI.Commands.ChatCommands.Add(new Command("tshock.admin", ProjDetection.ShowRestrictedList, "projlist", "违禁弹幕"));
            TShockAPI.Commands.ChatCommands.Add(new Command("tshock.admin", ItemDetection.ShowRestrictedList, "scanlist", "违禁物品"));

            TShockAPI.Hooks.GeneralHooks.ReloadEvent += OnReload;
        }

        private void OnReload(TShockAPI.Hooks.ReloadEventArgs e)
        {
            AntiCheat.LoadConfig();
            AntiCheat.LoadProjConfig();
            ItemConfigHandler.LoadItemConfig();
            ProjDetection.RefreshRestrictedProjectiles();
            ItemDetection.RefreshRestrictedItems();
            ItemDetection.StartAutoScan();
            TShock.Log.ConsoleInfo("[TSWeb] 反作弊配置已重新加载");
        }

        protected override void Dispose(bool Disposing)
        {
            if (Disposing)
            {
                TShockAPI.Hooks.GeneralHooks.ReloadEvent -= OnReload;
                PlannedOff.Dispose();
                AutoRegister.Dispose(this);
                OnlineData.Dispose();
                RuntimeHooks.Dispose();
                ItemDetection.StopAutoScan();
                BypassHelper.UnregisterPermissionHook();
            }
            base.Dispose(Disposing);
        }

		
	}
}
