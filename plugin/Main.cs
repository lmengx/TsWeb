﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using System.Reflection;
using TShockAPI;
using Terraria;
using TerrariaApi.Server;
using Rests;
using System.Data;
using TShockAPI.DB;

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

            BossLimit.Initialize();

            BypassHelper.RegisterPermissionHook();

            AutoRegister.Initialize(this);

            ItemRestrict.Initialize();

            QQBind.Initialize();

			TShock.RestApi.Register(new SecureRestCommand("/data/users/invsee", GetPlayerInv.GetInv, "data.rest.invsee"));
			TShock.RestApi.Register(new SecureRestCommand("/data/users/editinv", GetPlayerInv.EditInv, "data.rest.invsee"));
			TShock.RestApi.Register(new SecureRestCommand("/data/users/query_detail", QueryUsers.QueryUsersList, "data.rest.invsee"));
			TShock.RestApi.Register(new SecureRestCommand("/data/users/stats", PlayerStats.GetPlayerStats, "data.rest.invsee"));
			TShock.RestApi.Register(new SecureRestCommand("/data/users/stats/set", PlayerStats.SetPlayerStats, "data.rest.invsee"));
			TShock.RestApi.Register(new SecureRestCommand("/data/users/duplicateips", QueryUsers.QueryDuplicateIPs, "data.rest.invsee"));
			TShock.RestApi.Register(new SecureRestCommand("/data/users/allduplicateips", QueryUsers.QueryAllDuplicateIPs, "data.rest.invsee"));
			TShock.RestApi.Register(new SecureRestCommand("/data/users/ban", QueryUsers.BanPlayerByNameorID, "data.rest.invsee"));
            TShock.RestApi.Register(new SecureRestCommand("/data/users/unban", QueryUsers.UnbanPlayer, "data.rest.invsee"));

			TShock.RestApi.Register(new SecureRestCommand("/data/groups/list", GroupOP.GetAllGroups, "data.groups"));
			TShock.RestApi.Register(new SecureRestCommand("/data/groups/get", GroupOP.GetGroup, "data.groups"));
			TShock.RestApi.Register(new SecureRestCommand("/data/groups/create", GroupOP.CreateGroup, "data.groups"));
			TShock.RestApi.Register(new SecureRestCommand("/data/groups/delete", GroupOP.DeleteGroup, "data.groups"));
			TShock.RestApi.Register(new SecureRestCommand("/data/groups/update", GroupOP.UpdateGroup, "data.groups"));
			TShock.RestApi.Register(new SecureRestCommand("/data/groups/permission/add", GroupOP.AddPermission, "data.groups"));
			TShock.RestApi.Register(new SecureRestCommand("/data/groups/permission/remove", GroupOP.RemovePermission, "data.groups"));

            TShock.RestApi.Register(new SecureRestCommand("/data/users/getpassword", QueryPwd.GetUserPassword, "data.rest.invsee"));
            TShock.RestApi.Register(new SecureRestCommand("/data/users/clearcharacter", ClearCharacter.ClearCharacterData, "data.rest.invsee"));
            TShock.RestApi.Register(new SecureRestCommand("/data/users/clearallcharacter", ClearCharacter.ClearAllCharacterData, "data.rest.invsee"));

            TShock.RestApi.Register(new SecureRestCommand("/data/anticheat/proj-config/getprojconfig", ProjConfigHandler.GetProjConfig, "tshock.admin"));
            TShock.RestApi.Register(new SecureRestCommand("/data/anticheat/proj-config/saveprojconfig", ProjConfigHandler.SaveProjConfig, "tshock.admin"));
            TShock.RestApi.Register(new SecureRestCommand("/data/anticheat/item-config/getitemconfig", ItemConfigHandler.GetItemConfigApi, "tshock.admin"));
            TShock.RestApi.Register(new SecureRestCommand("/data/anticheat/item-config/saveitemconfig", ItemConfigHandler.SaveItemConfigApi, "tshock.admin"));
            TShock.RestApi.Register(new SecureRestCommand("/data/anticheat/item-config/scanall", ItemConfigHandler.ScanAllItemsApi, "tshock.admin"));
            TShock.RestApi.Register(new SecureRestCommand("/data/anticheat/item-config/scan-by-id", ItemConfigHandler.ScanItemByIdApi, "tshock.admin"));

            TShockAPI.Commands.ChatCommands.Add(new Command("tools.runas", tools.runas, "runas"));

			TShockAPI.Commands.ChatCommands.Add(new Command("tshock.admin.ban", tools.banp, "banp"));
            TShockAPI.Commands.ChatCommands.Add(new Command("tshock.admin", tools.remove, "remove"));
            TShockAPI.Commands.ChatCommands.Add(new Command("tshock.admin", tools.find, "find"));

            TShockAPI.Commands.ChatCommands.Add(new Command("", BossProgress.GetBossInfo, "进度", "bossinfo"));

            TShock.RestApi.Register(new SecureRestCommand("/data/boss/progress", BossProgress.GetBossInfoJson, ""));
            TShock.RestApi.Register(new SecureRestCommand("/data/config/tsweb", AutoRegister.GetConfigJson, ""));
            TShock.RestApi.Register(new SecureRestCommand("/data/config/tsweb/set", AutoRegister.SetConfigJson, "data.rest.invsee"));

            TShockAPI.Commands.ChatCommands.Add(new Command("tools.planoff", PlannedOff.PlanOff, "planoff"));
            PlannedOff.Initialize(this);

            BugFixes.Initialize(this);

            TShockAPI.Commands.ChatCommands.Add(new Command("tshock.admin", AutoRegister.HandleCommand, "autoregister", "ar"));

            TShockAPI.Commands.ChatCommands.Add(new Command("tshock.admin", ExportPlayer.Export, "export", "导出"));

            TShockAPI.Commands.ChatCommands.Add(new Command("", PasswordManager.ChangePassword, "pwd", "密码") { DoLog = false });

            AntiCheat.Initialize();
            ProjDetection.Initialize();
            ItemConfigHandler.LoadItemConfig();
            ItemDetection.Initialize();

            OnlineData.Initialize(this);

            TShock.RestApi.Register(new SecureRestCommand("/data/online/hourly", OnlineData.GetHourlyOnline, "data.rest.invsee"));
            TShock.RestApi.Register(new SecureRestCommand("/data/online/ranking", OnlineData.GetRanking, "data.rest.invsee"));
            TShock.RestApi.Register(new SecureRestCommand("/data/online/player", OnlineData.GetPlayerCalendar, "data.rest.invsee"));

            TShock.RestApi.Register(new SecureRestCommand("/data/users/unverified/list", UnverifiedManager.GetUnverifiedList, "data.rest.invsee"));
            TShock.RestApi.Register(new SecureRestCommand("/data/users/unverified/detail", UnverifiedManager.GetDetail, "data.rest.invsee"));
            TShock.RestApi.Register(new SecureRestCommand("/data/users/unverified/register", UnverifiedManager.RegisterAndLogin, "data.rest.invsee"));
            TShock.RestApi.Register(new SecureRestCommand("/data/users/unverified/force-login", UnverifiedManager.ForceLogin, "data.rest.invsee"));
            TShock.RestApi.Register(new SecureRestCommand("/data/users/unverified/kick", UnverifiedManager.KickUnverified, "data.rest.invsee"));
            TShock.RestApi.Register(new SecureRestCommand("/data/users/unverified/ban", UnverifiedManager.BanUnverified, "data.rest.invsee"));

            TShock.RestApi.Register(new SecureRestCommand("/data/files/read", FileManager.ReadFile, "data.rest.invsee"));
            TShock.RestApi.Register(new SecureRestCommand("/data/files/write", FileManager.WriteFile, "data.rest.invsee"));
            TShock.RestApi.Register(new SecureRestCommand("/data/files/list", FileManager.ListDirectory, "data.rest.invsee"));
            TShock.RestApi.Register(new SecureRestCommand("/data/files/tree", FileManager.GetDirectoryTree, "data.rest.invsee"));

            TShock.RestApi.Register(new SecureRestCommand("/data/qq/bind", QQBind.BindQQ, "data.rest.invsee"));
            TShock.RestApi.Register(new SecureRestCommand("/data/qq/register", QQBind.RegisterAndBind, "data.rest.invsee"));
            TShock.RestApi.Register(new SecureRestCommand("/data/qq/reset-password", QQBind.ResetPasswordByQQ, "data.rest.invsee"));

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
				BugFixes.Dispose(this);
				AutoRegister.Dispose(this);
				ItemRestrict.Dispose();
				OnlineData.Dispose();
				RuntimeHooks.Dispose();
				BossLimit.Dispose();
				ItemDetection.StopAutoScan();
				BypassHelper.UnregisterPermissionHook();

				CleanupChatCommands();
				CleanupRestApiRoutes();
			}
			base.Dispose(Disposing);
		}

		/// <summary>
		/// 清理 TSWeb 注册的所有聊天命令
		/// </summary>
		private static void CleanupChatCommands()
		{
			var tswebCommandNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
			{
				"runas", "banp", "remove", "find",
				"进度", "bossinfo",
				"planoff",
				"autoregister", "ar",
				"export", "导出",
				"pwd", "密码",
				"scan", "扫描",
				"projlist", "违禁弹幕",
				"scanlist", "违禁物品",
				"bosslimit", "进度锁",
			};

			Commands.ChatCommands.RemoveAll(cmd =>
				cmd.Names.Any(name => tswebCommandNames.Contains(name)));

			TShock.Log.ConsoleInfo("[TSWeb] 聊天命令已清理");
		}

		/// <summary>
		/// 清理 TSWeb 注册的所有 REST API 路由
		/// </summary>
		private static void CleanupRestApiRoutes()
		{
			var tswebRoutes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
			{
				"/data/users/invsee",
				"/data/users/editinv",
				"/data/users/query_detail",
				"/data/users/stats",
				"/data/users/stats/set",
				"/data/users/duplicateips",
				"/data/users/allduplicateips",
				"/data/users/ban",
				"/data/users/unban",
				"/data/groups/list",
				"/data/groups/get",
				"/data/groups/create",
				"/data/groups/delete",
				"/data/groups/update",
				"/data/groups/permission/add",
				"/data/groups/permission/remove",
				"/data/users/getpassword",
				"/data/users/clearcharacter",
				"/data/users/clearallcharacter",
				"/data/anticheat/proj-config/getprojconfig",
				"/data/anticheat/proj-config/saveprojconfig",
				"/data/anticheat/item-config/getitemconfig",
				"/data/anticheat/item-config/saveitemconfig",
				"/data/anticheat/item-config/scanall",
				"/data/anticheat/item-config/scan-by-id",
				"/data/boss/progress",
				"/data/config/tsweb",
				"/data/config/tsweb/set",
				"/data/online/hourly",
				"/data/online/ranking",
				"/data/online/player",
				"/data/users/unverified/list",
				"/data/users/unverified/detail",
				"/data/users/unverified/register",
				"/data/users/unverified/force-login",
				"/data/users/unverified/kick",
				"/data/users/unverified/ban",
				"/data/files/read",
				"/data/files/write",
				"/data/files/list",
				"/data/files/tree",
				"/data/qq/bind",
				"/data/qq/register",
				"/data/qq/reset-password",
			};

			try
			{
				var commandsField = typeof(Rests.Rest).GetField("commands",
					BindingFlags.NonPublic | BindingFlags.Instance);
				if (commandsField?.GetValue(TShock.RestApi) is List<Rests.RestCommand> cmdList)
				{
					var removed = cmdList.RemoveAll(c => tswebRoutes.Contains(c.UriTemplate));
					TShock.Log.ConsoleInfo($"[TSWeb] REST API 路由已清理: {removed} 条");
				}
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[TSWeb] REST API 路由清理失败: {ex.Message}");
			}
		}

		
	}
}
