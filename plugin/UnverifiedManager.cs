using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;
using Rests;
using Newtonsoft.Json;

namespace TShockData
{
    public static class UnverifiedManager
    {
        private static string GetPlayerStateText(int state)
        {
            return state switch
            {
                0 => "Connecting",
                1 => "AssigningSlot",
                2 => "AwaitingPlayerInfo",
                3 => "AwaitingLogin",
                10 => "Playing",
                _ => $"Unknown({state})"
            };
        }

        /// <summary>
        /// GET /data/users/unverified/list
        /// 获取所有未登录玩家列表
        /// 判断逻辑：查数据库角色名是否已注册，而非依赖 Account 对象
        /// </summary>
        public static object GetUnverifiedList(RestRequestArgs args)
        {
            try
            {
                var players = new List<Dictionary<string, object>>();
                foreach (var tsPlayer in TShock.Players)
                {
                    if (tsPlayer == null || string.IsNullOrEmpty(tsPlayer.Name))
                        continue;

                    // 查数据库判断该角色名是否已有账号
                    var dbAccount = TShock.UserAccounts.GetUserAccountByName(tsPlayer.Name);
                    bool hasAccount = dbAccount != null;

                    // 已有账号且已完成登录 → 跳过
                    if (hasAccount && tsPlayer.IsLoggedIn)
                        continue;

                    players.Add(new Dictionary<string, object>
                    {
                        { "nickname", tsPlayer.Name },
                        { "ip", tsPlayer.IP },
                        { "uuid", tsPlayer.UUID },
                        { "hasAccount", hasAccount },
                        { "accountName", dbAccount?.Name ?? "" },
                        { "isLoggedIn", tsPlayer.IsLoggedIn },
                        { "state", tsPlayer.State },
                        { "stateText", GetPlayerStateText(tsPlayer.State) },
                        { "group", tsPlayer.Group?.Name ?? "default" }
                    });
                }

                return new RestObject()
                {
                    { "players", players },
                    { "count", players.Count }
                };
            }
            catch (Exception ex)
            {
                return new RestObject("500")
                {
                    { "error", ex.Message }
                };
            }
        }

        /// <summary>
        /// GET /data/users/unverified/detail?nickname=
        /// 获取单个未验证玩家详细信息
        /// </summary>
        public static object GetDetail(RestRequestArgs args)
        {
            string nickname = args.Parameters["nickname"];
            if (string.IsNullOrEmpty(nickname))
                return new RestObject("400") { { "error", "nickname is required" } };

            try
            {
                var tsPlayer = FindPlayer(nickname);
                if (tsPlayer == null)
                    return new RestObject("404") { { "error", "玩家不在线或不存在" } };

                var dbAccount = TShock.UserAccounts.GetUserAccountByName(tsPlayer.Name);
                bool hasAccount = dbAccount != null;

                return new RestObject()
                {
                    { "nickname", tsPlayer.Name },
                    { "ip", tsPlayer.IP },
                    { "uuid", tsPlayer.UUID },
                    { "hasAccount", hasAccount },
                    { "accountName", dbAccount?.Name ?? "" },
                    { "isLoggedIn", tsPlayer.IsLoggedIn },
                    { "state", tsPlayer.State },
                    { "stateText", GetPlayerStateText(tsPlayer.State) },
                    { "group", tsPlayer.Group?.Name ?? "default" }
                };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }

        /// <summary>
        /// POST /data/users/unverified/register
        /// 为未注册玩家创建账号并登录
        /// </summary>
        public static object RegisterAndLogin(RestRequestArgs args)
        {
            string nickname = args.Parameters["nickname"];
            string password = args.Parameters["password"];
            string group = args.Parameters["group"];

            if (string.IsNullOrEmpty(nickname) || string.IsNullOrEmpty(password))
                return new RestObject("400") { { "error", "nickname and password are required" } };

            if (string.IsNullOrEmpty(group))
                group = TShock.Config.Settings.DefaultRegistrationGroupName;

            try
            {
                var tsPlayer = FindPlayer(nickname);
                if (tsPlayer == null)
                    return new RestObject("404") { { "error", "玩家不在线或不存在" } };

                if (tsPlayer.Account != null)
                    return new RestObject("400") { { "error", "该玩家已有账号，请使用强制登录" } };

                var account = new UserAccount
                {
                    Name = tsPlayer.Name,
                    Group = group,
                    UUID = tsPlayer.UUID
                };
                account.CreateBCryptHash(password);
                TShock.UserAccounts.AddUserAccount(account);
                TShock.Log.ConsoleInfo($"[TSWeb] 管理员后台注册玩家: {tsPlayer.Name}");

                // 强制登录
                DoForceLogin(tsPlayer, account);

                return new RestObject()
                {
                    { "message", $"账号已创建并登录" },
                    { "username", tsPlayer.Name },
                    { "group", group }
                };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }

        /// <summary>
        /// POST /data/users/unverified/force-login
        /// 强制登录已有账号的玩家，更新UUID
        /// </summary>
        public static object ForceLogin(RestRequestArgs args)
        {
            string nickname = args.Parameters["nickname"];
            if (string.IsNullOrEmpty(nickname))
                return new RestObject("400") { { "error", "nickname is required" } };

            try
            {
                var tsPlayer = FindPlayer(nickname);
                if (tsPlayer == null)
                    return new RestObject("404") { { "error", "玩家不在线或不存在" } };

                UserAccount account;
                if (tsPlayer.Account != null)
                {
                    account = tsPlayer.Account;
                }
                else
                {
                    // 尝试从数据库查账号（角色名可能也注册过）
                    account = TShock.UserAccounts.GetUserAccountByName(tsPlayer.Name);
                    if (account == null)
                        return new RestObject("400") { { "error", "该玩家没有账号，请先注册" } };
                }

                // 更新UUID
                TShock.UserAccounts.SetUserAccountUUID(account, tsPlayer.UUID);
                TShock.Log.ConsoleInfo($"[TSWeb] 管理员强制登录玩家: {tsPlayer.Name}");

                DoForceLogin(tsPlayer, account);

                return new RestObject()
                {
                    { "message", "强制登录成功" },
                    { "username", tsPlayer.Name }
                };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }

        /// <summary>
        /// POST /data/users/unverified/kick
        /// 踢出未验证玩家
        /// </summary>
        public static object KickUnverified(RestRequestArgs args)
        {
            string nickname = args.Parameters["nickname"];
            string reason = args.Parameters["reason"];
            if (string.IsNullOrEmpty(reason))
                reason = "管理员踢出";

            if (string.IsNullOrEmpty(nickname))
                return new RestObject("400") { { "error", "nickname is required" } };

            try
            {
                var tsPlayer = FindPlayer(nickname);
                if (tsPlayer == null)
                    return new RestObject("404") { { "error", "玩家不在线或不存在" } };

                tsPlayer.Kick(reason, false, true);
                TShock.Log.ConsoleInfo($"[TSWeb] 管理员踢出玩家: {tsPlayer.Name}, 原因: {reason}");

                return new RestObject()
                {
                    { "message", $"已踢出 {nickname}" }
                };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }

        /// <summary>
        /// POST /data/users/unverified/ban
        /// 封禁未验证玩家（只封IP+UUID，不封玩家名）
        /// </summary>
        public static object BanUnverified(RestRequestArgs args)
        {
            string nickname = args.Parameters["nickname"];
            string reason = args.Parameters["reason"];
            if (string.IsNullOrEmpty(reason))
                reason = "管理员封禁";

            if (string.IsNullOrEmpty(nickname))
                return new RestObject("400") { { "error", "nickname is required" } };

            try
            {
                var tsPlayer = FindPlayer(nickname);
                if (tsPlayer == null)
                    return new RestObject("404") { { "error", "玩家不在线或不存在" } };

                var banned = new List<string>();

                // 封禁IP（跳过127.0.0.1，防止封掉本地服务器）
                if (!string.IsNullOrEmpty(tsPlayer.IP))
                {
                    if (tsPlayer.IP == "127.0.0.1" || tsPlayer.IP == "localhost" || tsPlayer.IP == "::1")
                    {
                        TShock.Log.ConsoleWarn($"[TSWeb] 跳过封禁本地回环IP: {tsPlayer.IP}");
                    }
                    else
                    {
                        TShock.Bans.InsertBan($"ip:{tsPlayer.IP}", reason, "TSWeb", DateTime.UtcNow, DateTime.MaxValue);
                        banned.Add($"ip:{tsPlayer.IP}");
                    }
                }

                // 封禁UUID
                if (!string.IsNullOrEmpty(tsPlayer.UUID))
                {
                    TShock.Bans.InsertBan($"uuid:{tsPlayer.UUID}", reason, "TSWeb", DateTime.UtcNow, DateTime.MaxValue);
                    banned.Add($"uuid:{tsPlayer.UUID}");
                }

                // 踢出
                tsPlayer.Kick(reason, false, true);

                TShock.Log.ConsoleInfo($"[TSWeb] 管理员封禁玩家: {nickname}, 封禁项: {string.Join(", ", banned)}");

                return new RestObject()
                {
                    { "message", $"已封禁 {nickname}（IP+UUID）" },
                    { "banned", banned }
                };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }

        private static TSPlayer FindPlayer(string nickname)
        {
            return TShock.Players.FirstOrDefault(p =>
                p != null && p.Active &&
                p.Name.Equals(nickname, StringComparison.OrdinalIgnoreCase));
        }

        private static void DoForceLogin(TSPlayer player, UserAccount account)
        {
            var group = TShock.Groups.GetGroupByName(account.Group);
            if (!TShock.Groups.AssertGroupValid(player, group, true))
                return;

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

            // 推进连接状态（如果还在登录阶段）
            if (player.State < 10)
                player.State = 10;

            player.SendSuccessMessage($"管理员已为您登录: {account.Name}");
            TShockAPI.Hooks.PlayerHooks.OnPlayerPostLogin(player);
        }
    }
}
