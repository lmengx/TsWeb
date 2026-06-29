using System;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;
using TShockAPI.Hooks;
using Newtonsoft.Json;
using Rests;

namespace TShockData
{
    public enum RegisterMode
    {
        Default,
        Auto,
        Disable,
        Block
    }

    public class RegisterConfig
    {
        [JsonProperty("AutoRegisterMode")]
        public string AutoRegisterMode { get; set; } = "default";

        [JsonProperty("Boss限制模式")]
        public string BossLimitMode { get; set; } = "disabled";

        [JsonProperty("BOSS限制")]
        public bool BossLimitEnabled { get; set; } = false;

        [JsonProperty("新BOSS召唤最低人数")]
        public int BossLimitMinPlayers { get; set; } = 7;

        public RegisterMode GetMode()
        {
            return AutoRegisterMode.ToLower() switch
            {
                "auto" => RegisterMode.Auto,
                "disable" => RegisterMode.Disable,
                "block" => RegisterMode.Block,
                _ => RegisterMode.Default
            };
        }
    }

    public static class AutoRegister
    {
        private static bool _isInitialized = false;
        public static RegisterConfig Config { get; private set; } = new RegisterConfig();
        private static string ConfigPath => Path.Combine(TShock.SavePath, "TSWeb", "config.json");
        private static CommandDelegate _originalRegisterDelegate;

        // 记录由插件发起密码挑战的玩家
        private static readonly HashSet<string> _passwordPending = new();

        public static void Initialize(TerrariaPlugin plugin)
        {
            if (_isInitialized)
                return;

            LoadConfig();
            ServerApi.Hooks.NetGetData.Register(plugin, OnGetData, int.MaxValue);
            
            ReplaceRegisterCommand();

            _isInitialized = true;
            TShock.Log.ConsoleInfo($"[TSWeb] 自动注册功能已启用 - 模式: {Config.AutoRegisterMode}");
        }

        public static void Dispose(TerrariaPlugin plugin)
        {
            if (!_isInitialized)
                return;

            ServerApi.Hooks.NetGetData.Deregister(plugin, OnGetData);
            _isInitialized = false;
        }

        public static void LoadConfig()
        {
            try
            {
                var directory = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    Config = JsonConvert.DeserializeObject<RegisterConfig>(json) ?? new RegisterConfig();
                }
                else
                {
                    Config = new RegisterConfig();
                    SaveConfig();
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 加载配置文件失败: {ex.Message}");
                Config = new RegisterConfig();
            }
        }

        public static void SaveConfig()
        {
            try
            {
                var directory = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonConvert.SerializeObject(Config, Formatting.Indented);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 保存配置文件失败: {ex.Message}");
            }
        }

        private static void ReplaceRegisterCommand()
        {
            var originalCommand = TShockAPI.Commands.ChatCommands.Find(c => 
                c.Names.Contains("register") || c.Names.Contains("reg")
            );

            if (originalCommand != null)
            {
                _originalRegisterDelegate = originalCommand.CommandDelegate;
                TShockAPI.Commands.ChatCommands.Remove(originalCommand);
                TShock.Log.ConsoleInfo("[TSWeb] 已替换注册命令");
            }
            else
            {
                TShock.Log.ConsoleError("[TSWeb] 未找到原始注册命令");
            }

            TShockAPI.Commands.ChatCommands.Add(new Command("", HandleRegisterCommand, "register", "reg"));
        }

        public static void HandleRegisterCommand(CommandArgs args)
        {
            var mode = Config.GetMode();

            switch (mode)
            {
                case RegisterMode.Default:
                    if (_originalRegisterDelegate != null)
                    {
                        _originalRegisterDelegate(args);
                    }
                    else
                    {
                        args.Player.SendErrorMessage("注册命令不可用，请联系管理员");
                    }
                    break;

                case RegisterMode.Auto:
                    args.Player.SendErrorMessage("服务器已启用自动注册模式，无需手动注册");
                    args.Player.SendInfoMessage("加入服务器时系统会自动为您创建账户");
                    break;

                case RegisterMode.Disable:
                    args.Player.SendErrorMessage("服务器已禁用注册功能");
                    args.Player.SendInfoMessage("请联系管理员获取账户");
                    break;

                case RegisterMode.Block:
                    args.Player.SendErrorMessage("服务器已关闭注册，未注册玩家无法进入");
                    break;
            }
        }

        private static void OnGetData(GetDataEventArgs args)
        {
            if (args.Handled)
                return;

            var mode = Config.GetMode();
            if (mode != RegisterMode.Auto && mode != RegisterMode.Block)
                return;

            // 只关注连接阶段的包
            if (args.MsgID != PacketTypes.ContinueConnecting2 && args.MsgID != PacketTypes.PasswordSend)
                return;

            var player = TShock.Players[args.Msg.whoAmI];
            if (player == null || string.IsNullOrEmpty(player.Name))
                return;

            if (player.IsLoggedIn)
                return;

            if (mode == RegisterMode.Auto)
            {
                HandleAutoMode(player);
            }
            else if (mode == RegisterMode.Block)
            {
                if (args.MsgID == PacketTypes.ContinueConnecting2)
                {
                    HandleBlockConnecting(player, args);
                }
                else if (args.MsgID == PacketTypes.PasswordSend)
                {
                    HandleBlockPassword(player, args);
                }
            }
        }

        private static void HandleAutoMode(TSPlayer player)
        {
            var account = TShock.UserAccounts.GetUserAccountByName(player.Name);
            if (account != null)
                return;

            var newAccount = CreateAccount(player);
            if (newAccount != null)
            {
                TryAutoLogin(player, newAccount);
            }
        }

        private static void HandleBlockConnecting(TSPlayer player, GetDataEventArgs args)
        {
            var account = TShock.UserAccounts.GetUserAccountByName(player.Name);

            // 已注册 + UUID匹配 → 放行（让TShock正常处理）
            if (account != null && account.UUID == player.UUID)
            {
                TShock.Log.ConsoleInfo($"[TSWeb] UUID验证通过: {player.Name}");
                return;
            }

            // 已注册但 UUID 不匹配 → 我们自己弹密码验证
            if (account != null)
            {
                args.Handled = true;
                player.RequiresPassword = true;
                _passwordPending.Add(player.Name);
                NetMessage.SendData((int)PacketTypes.PasswordRequired, player.Index);
                TShock.Log.ConsoleInfo($"[TSWeb] 请求密码验证(UUID不匹配): {player.Name}");
                return;
            }

            // 未注册玩家 → 直接断联
            args.Handled = true;
            Task.Run(async () =>
            {
                await Task.Delay(200);
                if (player.ConnectionAlive)
                {
                    player.Disconnect("此服务器未注册玩家禁止进入，请联系管理员获取账户");
                }
            });
            TShock.Log.ConsoleInfo($"[TSWeb] 阻止未注册玩家进入: {player.Name}");
        }

        private static void HandleBlockPassword(TSPlayer player, GetDataEventArgs args)
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
                TShock.Log.ConsoleInfo($"[TSWeb] 密码验证失败: {player.Name}");
                player.Kick("密码错误，请输入角色密码，角色密码可通过已登录设备更改:/pwd 新密码", true, true);
                return;
            }

            // ★ 密码验证通过 → 完整登录流程
            _passwordPending.Remove(player.Name);
            TShock.Log.ConsoleInfo($"[TSWeb] 密码验证通过: {player.Name}");

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

            // SSC数据恢复
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

            // 更新UUID为当前设备
            TShock.UserAccounts.SetUserAccountUUID(account, player.UUID);

            player.SendSuccessMessage($"验证通过: {account.Name}");
            TShockAPI.Hooks.PlayerHooks.OnPlayerPostLogin(player);
        }

        private static UserAccount CreateAccount(TSPlayer player)
        {
            try
            {
                var account = new UserAccount
                {
                    Name = player.Name,
                    Group = TShock.Config.Settings.DefaultRegistrationGroupName,
                    UUID = player.UUID
                };
                account.CreateBCryptHash(Guid.NewGuid().ToString());
                TShock.UserAccounts.AddUserAccount(account);
                
                TShock.Log.ConsoleInfo($"[TSWeb] 自动注册玩家: {player.Name}");
                player.SendSuccessMessage($"账户已自动创建: {account.Name}");
                
                return account;
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 自动注册失败: {ex.Message}");
                return null;
            }
        }

        private static void TryAutoLogin(TSPlayer player, UserAccount account)
        {
            try
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
                    player.PlayerData.RestoreCharacter(player);
                }

                player.LoginFailsBySsi = false;

                if (player.HasPermission(Permissions.ignorestackhackdetection))
                    player.IsDisabledForStackDetection = false;

                if (player.HasPermission(Permissions.usebanneditem))
                    player.IsDisabledForBannedWearable = false;

                TShock.Log.ConsoleInfo($"[TSWeb] 自动登录玩家: {player.Name}");
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 自动登录失败: {ex.Message}");
            }
        }

        public static void HandleCommand(CommandArgs args)
        {
            if (args.Parameters.Count == 0)
            {
                ShowStatusAndHelp(args);
                return;
            }

            switch (args.Parameters[0].ToLower())
            {
                case "mode":
                    HandleModeCommand(args);
                    break;
                case "reload":
                    LoadConfig();
                    args.Player.SendSuccessMessage("[TSWeb] 配置文件已重新加载");
                    TShock.Log.ConsoleInfo("[TSWeb] 配置文件已重新加载");
                    break;
                case "status":
                case "查询":
                    CheckStatus(args);
                    break;
                default:
                    args.Player.SendErrorMessage("无效参数!");
                    ShowHelp(args);
                    break;
            }
        }

        private static void HandleModeCommand(CommandArgs args)
        {
            if (args.Parameters.Count < 2)
            {
                args.Player.SendInfoMessage($"当前模式: {Config.AutoRegisterMode}");
                args.Player.SendInfoMessage("可用模式: default / auto / disable / block");
                return;
            }

            var newMode = args.Parameters[1].ToLower();
            if (newMode != "default" && newMode != "auto" && newMode != "disable" && newMode != "block")
            {
                args.Player.SendErrorMessage("无效模式! 可用模式: default / auto / disable / block");
                return;
            }

            Config.AutoRegisterMode = newMode;
            SaveConfig();
            
            args.Player.SendSuccessMessage($"[TSWeb] 注册模式已设置为: {newMode}");
            TShock.Log.ConsoleInfo($"[TSWeb] 注册模式已设置为: {newMode} (由 {args.Player.Name} 操作)");
        }

        private static void ShowStatusAndHelp(CommandArgs args)
        {
            args.Player.SendInfoMessage($"[TSWeb] 当前注册模式: {Config.AutoRegisterMode}");
            ShowHelp(args);
        }

        private static void CheckStatus(CommandArgs args)
        {
            args.Player.SendInfoMessage($"[TSWeb] 当前注册模式: {Config.AutoRegisterMode}");
            args.Player.SendInfoMessage($"配置文件路径: {ConfigPath}");
        }

        private static void ShowHelp(CommandArgs args)
        {
            args.Player.SendInfoMessage("=== TSWeb 注册管理命令 ===");
            args.Player.SendInfoMessage("/autoregister - 查看状态和用法");
            args.Player.SendInfoMessage("/autoregister mode [default/auto/disable/block] - 设置注册模式");
            args.Player.SendInfoMessage("/autoregister reload - 重新加载配置文件");
            args.Player.SendInfoMessage("/autoregister status - 查看当前状态");
            args.Player.SendInfoMessage("");
            args.Player.SendInfoMessage("模式说明:");
            args.Player.SendInfoMessage("  default - 默认行为，允许手动注册");
            args.Player.SendInfoMessage("  auto - 自动注册新玩家，禁用手动注册");
            args.Player.SendInfoMessage("  disable - 完全禁用注册");
            args.Player.SendInfoMessage("  block - 阻止未注册/UUID不匹配玩家进入");
        }

        // ═══════════════════════════════════════════
        // REST API
        // ═══════════════════════════════════════════

        public static object GetConfigJson(RestRequestArgs args)
        {
            return new
            {
                status = "200",
                mode = Config.AutoRegisterMode,
                bossLimitMode = Config.BossLimitMode,
                bossLimitEnabled = Config.BossLimitEnabled,
                bossLimitMinPlayers = Config.BossLimitMinPlayers
            };
        }

        public static object SetConfigJson(RestRequestArgs args)
        {
            try
            {
                var mode = args.Parameters["mode"];
                if (!string.IsNullOrEmpty(mode))
                {
                    var m = mode.ToLower();
                    if (m == "default" || m == "auto" || m == "disable" || m == "block")
                        Config.AutoRegisterMode = m;
                }
                var blm = args.Parameters["bossLimitMode"];
                if (!string.IsNullOrEmpty(blm))
                {
                    var m = blm.ToLower();
                    if (m == "disabled" || m == "playerlimit" || m == "killrequired")
                    {
                        Config.BossLimitMode = m;
                        Config.BossLimitEnabled = m != "disabled";
                    }
                }
                var ble = args.Parameters["bossLimitEnabled"];
                if (!string.IsNullOrEmpty(ble))
                    Config.BossLimitEnabled = ble.ToLower() == "true";
                var blmp = args.Parameters["bossLimitMinPlayers"];
                if (!string.IsNullOrEmpty(blmp) && int.TryParse(blmp, out var num) && num > 0)
                    Config.BossLimitMinPlayers = num;
                SaveConfig();
                TShock.Log.ConsoleInfo($"[TSWeb] REST 更新配置: mode={Config.AutoRegisterMode}, bossLimitMode={Config.BossLimitMode}, minPlayers={Config.BossLimitMinPlayers}");
                return new { status = "200", message = "配置已保存" };
            }
            catch (Exception ex)
            {
                return new { status = "500", error = ex.Message };
            }
        }
    }
}
