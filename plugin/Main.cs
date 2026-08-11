using System.Reflection;
using TShockAPI;
using Terraria;
using TerrariaApi.Server;
using Rests;
using System.Data;
using TShockAPI.DB;
using HouseRegion;

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
            BossLimit.InitQuit(this);
            BossConfigManager.LoadConfig();

            BypassHelper.RegisterPermissionHook();

            PvPLockManager.Initialize(this);
            TeamLockManager.Initialize(this);

            AutoRegister.Initialize(this);

            ItemRestrict.Initialize();

            QQBind.Initialize();
            PromotionManager.LoadConfig();

            // ═══ House 房屋系统（原 plugin-son/House 并入）═══
            HouseCore.Instance.Initialize(this);
            HouseApi.Register();

			TShock.RestApi.Register(new SecureRestCommand("/data/users/invsee", GetPlayerInv.GetInv, "data.rest.invsee"));
			TShock.RestApi.Register(new SecureRestCommand("/data/users/editinv", GetPlayerInv.EditInv, "data.rest.invsee"));
			TShock.RestApi.Register(new SecureRestCommand("/data/users/batch-edit", GetPlayerInv.BatchEdit, "data.rest.invsee"));
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

            TShockAPI.Commands.ChatCommands.Add(new Command("tshock.admin", tools.pvp, "pvp"));
            TShockAPI.Commands.ChatCommands.Add(new Command("tshock.admin", tools.pvplock, "pvplock"));
            TShockAPI.Commands.ChatCommands.Add(new Command("tshock.admin", tools.team, "team"));
            TShockAPI.Commands.ChatCommands.Add(new Command("tshock.admin", tools.teamlock, "teamlock"));

            TShockAPI.Commands.ChatCommands.Add(new Command("", BossProgress.GetBossInfo, "进度", "bossinfo"));

            TShock.RestApi.Register(new SecureRestCommand("/data/boss/progress", BossProgress.GetBossInfoJson, ""));
            TShock.RestApi.Register(new SecureRestCommand("/data/bosslimit/status", BossLimitQuit.GetStatusJson, ""));
            TShock.RestApi.Register(new SecureRestCommand("/data/config/tsweb", AutoRegister.GetConfigJson, ""));
            TShock.RestApi.Register(new SecureRestCommand("/data/config/tsweb/set", AutoRegister.SetConfigJson, "data.rest.invsee"));
            TShock.RestApi.Register(new SecureRestCommand("/data/config/boss", BossConfigManager.GetConfigJson, ""));
            TShock.RestApi.Register(new SecureRestCommand("/data/config/boss/set", BossConfigManager.SetConfigJson, "data.rest.invsee"));

            TShock.RestApi.Register(new SecureRestCommand("/data/config/backup", AutoBackup.GetConfigJson, ""));
            TShock.RestApi.Register(new SecureRestCommand("/data/config/backup/set", AutoBackup.SetConfigJson, "data.rest.invsee"));

            TShockAPI.Commands.ChatCommands.Add(new Command("tools.planoff", PlannedOff.PlanOff, "planoff"));
            PlannedOff.Initialize(this);

            BugFixes.Initialize(this);

            // ping 命令：测量玩家到服务器的延迟（/ping 查自己，/ping 玩家名 需 tshock.admin）
            TShockAPI.Commands.ChatCommands.Add(new Command("", Ping.PingCommand, "ping") { HelpText = "查看你到服务器的延迟；管理员可用 /ping 玩家名 查看指定玩家延迟" });
            Ping.Initialize(this);

            TShockAPI.Commands.ChatCommands.Add(new Command("tshock.admin", AutoRegister.HandleCommand, "autoregister", "ar"));

            TShockAPI.Commands.ChatCommands.Add(new Command("tshock.admin", ExportPlayer.Export, "export", "导出"));

            TShockAPI.Commands.ChatCommands.Add(new Command("", PasswordManager.ChangePassword, "pwd", "密码") { DoLog = false });

            AntiCheat.Initialize();
            ProjDetection.Initialize();
            ItemConfigHandler.LoadItemConfig();
            ItemDetection.Initialize();

			// ═══ 粒子防线：拦截客户端伪造粒子请求（82 + NetParticlesModule）═══
            ParticleGuard.Initialize(this);

            OnlineData.Initialize(this);

            TShock.RestApi.Register(new SecureRestCommand("/data/online/hourly", OnlineData.GetHourlyOnline, "data.rest.invsee"));
            TShock.RestApi.Register(new SecureRestCommand("/data/online/ranking", OnlineData.GetRanking, "data.rest.invsee"));
            TShock.RestApi.Register(new SecureRestCommand("/data/online/player", OnlineData.GetPlayerCalendar, "data.rest.invsee"));
            TShock.RestApi.Register(new SecureRestCommand("/data/online/ranking/stats", OnlineData.GetRankingStats, "data.rest.invsee"));

            // 控制台命令执行
            TShock.RestApi.Register(new SecureRestCommand("/data/online/log/command", SSELogger.ExecuteCommandApi, "data.rest.invsee"));

            // 日志轮询（替代 SSE，通过 RCON 推送）
            TShock.RestApi.Register(new SecureRestCommand("/data/online/log/poll", SSELogger.PollLogs, "data.rest.invsee"));

            SSELogger.Initialize(this);

            // ═══ 现代 REST 监听接管（替换旧 HttpServer.dll，支持 SSE 长连接/日志实时推送）═══
            if (TShock.Config.Settings.RestApiEnabled)
            {
                TShock.RestApi.Stop();   // 释放旧 HttpServer 占用的端口
                WebRestServer.Start(TShock.Config.Settings.RestApiPort);
            }

            // ═══ 自动任务系统 ═══
            TaskScheduler.Initialize();

            // ═══ 自动备份系统（地图 + sqlite 压缩包，可选推送后端）═══
            AutoBackup.Initialize();

            TShock.RestApi.Register(new SecureRestCommand("/data/tasks/list", TaskScheduler.ListTasksApi, "data.rest.invsee"));
            TShock.RestApi.Register(new SecureRestCommand("/data/tasks/get", TaskScheduler.GetTaskApi, "data.rest.invsee"));
            TShock.RestApi.Register(new SecureRestCommand("/data/tasks/save", TaskScheduler.SaveTaskApi, "data.rest.invsee"));
            TShock.RestApi.Register(new SecureRestCommand("/data/tasks/delete", TaskScheduler.DeleteTaskApi, "data.rest.invsee"));
            TShock.RestApi.Register(new SecureRestCommand("/data/tasks/run", TaskScheduler.RunTaskApi, "data.rest.invsee"));
            TShock.RestApi.Register(new SecureRestCommand("/data/tasks/log", TaskScheduler.ListLogsApi, "data.rest.invsee"));
            TShock.RestApi.Register(new SecureRestCommand("/data/tasks/log/detail", TaskScheduler.LogDetailApi, "data.rest.invsee"));

            // ═══ Webhook 日志推流注册 API ═══
            TShock.RestApi.Register(new SecureRestCommand("/data/config/log-webhook/register", SSELogger.RegisterWebhookApi, "tshock.admin"));
            TShock.RestApi.Register(new SecureRestCommand("/data/config/log-webhook/unregister", SSELogger.UnregisterWebhookApi, "tshock.admin"));
            TShock.RestApi.Register(new SecureRestCommand("/data/config/log-webhook/status", SSELogger.GetWebhookStatusApi, "tshock.admin"));

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
            TShock.RestApi.Register(new SecureRestCommand("/data/files/delete", FileManager.DeleteFile, "data.rest.invsee"));
            TShock.RestApi.Register(new SecureRestCommand("/data/files/upload", FileManager.UploadFile, "data.rest.invsee"));

            TShock.RestApi.Register(new SecureRestCommand("/data/qq/bind", QQBind.BindQQ, "data.rest.invsee"));
            TShock.RestApi.Register(new SecureRestCommand("/data/qq/register", QQBind.RegisterAndBind, "data.rest.invsee"));
            TShock.RestApi.Register(new SecureRestCommand("/data/qq/reset-password", QQBind.ResetPasswordByQQ, "data.rest.invsee"));
            TShock.RestApi.Register(new SecureRestCommand("/data/qq/query-player", QQBind.QueryPlayerByQQ, "data.rest.invsee"));

            // 权限提升配置
            TShock.RestApi.Register(new SecureRestCommand("/data/promotion/config", PromotionManager.GetConfigJson, "data.rest.invsee"));
            TShock.RestApi.Register(new SecureRestCommand("/data/promotion/config/set", PromotionManager.SetConfigJson, "data.rest.invsee"));

            TShockAPI.Commands.ChatCommands.Add(new Command("tshock.admin", AntiCheat.HandleScanCommand, "scan", "扫描"));

            TShockAPI.Commands.ChatCommands.Add(new Command("tshock.admin", ProjDetection.ShowRestrictedList, "projlist", "违禁弹幕"));
            TShockAPI.Commands.ChatCommands.Add(new Command("tshock.admin", ItemDetection.ShowRestrictedList, "scanlist", "违禁物品"));

            // /lightning：服务端广播闪电（管理员受控入口，配合 ParticleGuard）
            TShockAPI.Commands.ChatCommands.Add(new Command("tshock.admin", ParticleGuard.LightningCommand, "lightning", "闪电"));

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
            PromotionManager.LoadConfig();
            TaskScheduler.Reload();
            AutoBackup.LoadConfig();

            TShock.Log.ConsoleInfo("[TSWeb] 反作弊配置已重新加载");
        }

		protected override void Dispose(bool Disposing)
		{
			if (Disposing)
			{
				TShockAPI.Hooks.GeneralHooks.ReloadEvent -= OnReload;
				PlannedOff.Dispose();
				BugFixes.Dispose(this);
                Ping.Dispose(this);
				AutoRegister.Dispose(this);
				ItemRestrict.Dispose();
				OnlineData.Dispose();
				SSELogger.Dispose();
                TaskScheduler.Dispose();
                AutoBackup.Dispose();
				RuntimeHooks.Dispose();
				BossLimit.Dispose();
				ItemDetection.StopAutoScan();
                ParticleGuard.Dispose();
				BypassHelper.UnregisterPermissionHook();
				PvPLockManager.Dispose();
				TeamLockManager.Dispose();
				HouseCore.Instance.Dispose();

				CleanupChatCommands();
				CleanupRestApiRoutes();

                // ═══ 让出现代 REST 监听，恢复 TShock 原 REST（热卸载场景服务器仍运行）═══
                WebRestServer.Stop();
                try
                {
                    if (TShock.Config.Settings.RestApiEnabled)
                        TShock.RestApi.Start();
                }
                catch { }
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
				"runas", "banp", "remove", "find", "pvp", "pvplock", "team", "teamlock",
				"进度", "bossinfo",
				"planoff",
				"autoregister", "ar",
				"export", "导出",
				"pwd", "密码",
				"scan", "扫描",
				"projlist", "违禁弹幕",
				"scanlist", "违禁物品",
                "lightning", "闪电",
                "ping",
				"bosslimit", "进度锁",			};

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
				"/data/users/batch-edit",
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
                "/data/bosslimit/status",
				"/data/config/tsweb",
				"/data/config/tsweb/set",
				"/data/config/backup",
				"/data/config/backup/set",
				"/data/online/hourly",
				"/data/online/ranking",
                "/data/online/player",
                "/data/online/ranking/stats",
                "/data/online/log/command",
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
                "/data/files/delete",
                "/data/files/upload",
				"/data/qq/bind",
                "/data/qq/register",
                "/data/qq/reset-password",
				"/data/qq/query-player",
				"/data/promotion/config",
                "/data/promotion/config/set",
                "/data/config/log-webhook/register",
                "/data/config/log-webhook/unregister",
                "/data/config/log-webhook/status",
                "/data/online/log/command",
				"/data/tasks/list",
                "/data/tasks/get",
                "/data/tasks/save",
                "/data/tasks/delete",
                "/data/tasks/run",
                "/data/tasks/log",
                "/data/tasks/log/detail",
                "/data/house/list",
                "/data/buildings/list",
                "/data/buildings/info",
                "/data/buildings/export",
                "/data/buildings/import",
                "/data/buildings/upload",
                "/data/buildings/delete-local",
                "/data/buildings/online-players",
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
