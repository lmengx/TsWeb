using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;

namespace TShockData
{
    /// <summary>
    /// 修复 TShock 恶性 Bug 的通用模块
    /// </summary>
    public static class BugFixes
    {
        private static bool _isInitialized = false;
        private static readonly HashSet<string> _passwordPending = new();

        public static void Initialize(TerrariaPlugin plugin)
        {
            if (_isInitialized)
                return;

            ServerApi.Hooks.NetGetData.Register(plugin, OnGetData, int.MaxValue);

            _isInitialized = true;
            TShock.Log.ConsoleInfo("[TSWeb] BugFixes 已加载");
        }

        public static void Dispose(TerrariaPlugin plugin)
        {
            if (!_isInitialized)
                return;

            ServerApi.Hooks.NetGetData.Deregister(plugin, OnGetData);
            _isInitialized = false;
        }

        private static void OnGetData(GetDataEventArgs args)
        {
            if (args.Handled)
                return;

            // 只关注连接阶段的包：玩家正在连接 或 正在提交密码
            if (args.MsgID != PacketTypes.ContinueConnecting2 && args.MsgID != PacketTypes.PasswordSend)
                return;

            var player = TShock.Players[args.Msg.whoAmI];
            if (player == null || string.IsNullOrEmpty(player.Name))
                return;

            if (player.IsLoggedIn)
                return;

            // 查询是否有同名账户
            var account = TShock.UserAccounts.GetUserAccountByName(player.Name);

            // 无账户或 UUID 匹配 → 放行，让 TShock 正常处理
            if (account == null || account.UUID == player.UUID)
                return;

            // ⚠ 账户存在但 UUID 不匹配 → 发起密码挑战
            if (args.MsgID == PacketTypes.ContinueConnecting2)
            {
                args.Handled = true;
                player.RequiresPassword = true;
                _passwordPending.Add(player.Name);
                NetMessage.SendData((int)PacketTypes.PasswordRequired, player.Index);
                TShock.Log.ConsoleInfo($"[TSWeb][BugFixes] UUID不匹配，请求密码验证: {player.Name}");
            }
            else if (args.MsgID == PacketTypes.PasswordSend)
            {
                HandlePasswordChallenge(player, args);
            }
        }

        private static void HandlePasswordChallenge(TSPlayer player, GetDataEventArgs args)
        {
            // 只处理由我们发起密码挑战的玩家
            if (!_passwordPending.Contains(player.Name))
                return;

            args.Handled = true;

            string password;
            using (var reader = new BinaryReader(new MemoryStream(args.Msg.readBuffer, args.Index, args.Length - 1)))
            {
                password = reader.ReadString();
            }

            var account = TShock.UserAccounts.GetUserAccountByName(player.Name);
            if (account == null || !account.VerifyPassword(password))
            {
                _passwordPending.Remove(player.Name);
                TShock.Log.ConsoleInfo($"[TSWeb][BugFixes] 密码验证失败: {player.Name}");
                player.Kick(
                    "密码错误\n" +
                    "请输入角色密码。已登录设备可使用 /pwd 新密码 设置密码。\n" +
                    "如果没有可以登录的设备，请联系服务器管理员。\n" +
                    "如果这是你第一次进服，说明你的角色名已被占用，请更换。",
                    true, true
                );
                return;
            }

            // 密码验证通过 → 完整登录流程
            _passwordPending.Remove(player.Name);
            TShock.Log.ConsoleInfo($"[TSWeb][BugFixes] 密码验证通过: {player.Name}");

            player.RequiresPassword = false;

            // 推进连接状态
            if (player.State == (int)ConnectionState.AssigningPlayerSlot)
                player.State = (int)ConnectionState.AwaitingPlayerInfo;

            // 发送世界信息，继续连接流程
            NetMessage.SendData((int)PacketTypes.WorldInfo, player.Index);

            // 加载玩家数据
            player.PlayerData = TShock.CharacterDB.GetPlayerData(player, account.ID);

            // 分配权限组
            var group = TShock.Groups.GetGroupByName(account.Group);
            if (!TShock.Groups.AssertGroupValid(player, group, true))
                return;

            player.Group = group;
            player.tempGroup = null;
            player.Account = account;
            player.IsLoggedIn = true;
            player.IsDisabledForSSC = false;

            // SSC 数据恢复
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

            // 更新 UUID 为当前设备
            TShock.UserAccounts.SetUserAccountUUID(account, player.UUID);

            player.SendSuccessMessage($"验证通过: {account.Name}");
            TShockAPI.Hooks.PlayerHooks.OnPlayerPostLogin(player);
        }
    }
}
