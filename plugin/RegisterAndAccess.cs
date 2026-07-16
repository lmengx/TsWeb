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
        Block
    }

    public class RegisterConfig
    {
        [JsonProperty("SetupCompleted")]
        public bool SetupCompleted { get; set; } = false;

        [JsonProperty("AutoRegisterMode")]
        public string AutoRegisterMode { get; set; } = "default";

        [JsonProperty("Boss限制模式")]
        public string BossLimitMode { get; set; } = "disabled";

        [JsonProperty("BOSS限制")]
        public bool BossLimitEnabled { get; set; } = false;

        [JsonProperty("新BOSS召唤最低人数")]
        public int BossLimitMinPlayers { get; set; } = 7;

        [JsonProperty("QuitLimitEnabled")]
        public bool QuitLimitEnabled { get; set; } = false;

        [JsonProperty("LateCompEnabled")]
        public bool LateCompEnabled { get; set; } = false;

        // ═══ RCON 远程控制台配置 ═══

        [JsonProperty("RconEnabled")]
        public bool RconEnabled { get; set; } = false;

        [JsonProperty("RconPort")]
        public int RconPort { get; set; } = 7880;

        [JsonProperty("RconPassword")]
        public string RconPassword { get; set; } = "";

        [JsonProperty("RconExternalPort")]
        public int RconExternalPort { get; set; } = 7880;

        public RegisterMode GetMode()
        {
            return AutoRegisterMode.ToLower() switch
            {
                "auto" => RegisterMode.Auto,
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
                    // 不自动创建文件，等待前端初始化后再保存
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
            if (args.MsgID != PacketTypes.ContinueConnecting2)
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
            }
        }

        private static void HandleAutoMode(TSPlayer player)
        {
            var account = TShock.UserAccounts.GetUserAccountByName(player.Name);
            if (account != null)
                return;

            // 检查IP/UUID是否已被封禁，防止被封玩家换名绕过
            if (CheckPlayerBanned(player))
            {
                TShock.Log.ConsoleInfo($"[TSWeb] 阻止被封禁玩家自动注册: {player.Name}, IP:{player.IP}");
                return;
            }

            var newAccount = CreateAccount(player);
            if (newAccount != null)
            {
                TryAutoLogin(player, newAccount);

                // 欢迎新玩家广播
                var welcomeMsg = $"欢迎新玩家 {player.Name} 加入服务器！";
                TSPlayer.All.SendSuccessMessage(welcomeMsg);
                //TShock.Log.ConsoleInfo($"[TSWeb] 新玩家欢迎广播已发送: {player.Name}");
            }
        }

        private static void HandleBlockConnecting(TSPlayer player, GetDataEventArgs args)
        {
            var account = TShock.UserAccounts.GetUserAccountByName(player.Name);

            // 有账户 → 放行（BugFixes 会处理 UUID 不匹配的密码挑战）
            if (account != null)
                return;

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
            if (newMode != "default" && newMode != "auto" && newMode != "block")
            {
                args.Player.SendErrorMessage("无效模式! 可用模式: default / auto / block");
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
            args.Player.SendInfoMessage("  block - 阻止未注册玩家进入");
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
                bossLimitMinPlayers = Config.BossLimitMinPlayers,
                quitLimitEnabled = Config.QuitLimitEnabled,
                lateCompEnabled = Config.LateCompEnabled,
                rconEnabled = Config.RconEnabled,
                rconPort = Config.RconPort,
                rconPassword = string.IsNullOrEmpty(Config.RconPassword) ? "" : "已设置",
                rconExternalPort = Config.RconExternalPort
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
            if (m == "default" || m == "auto" || m == "block")
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
                var qle = args.Parameters["quitLimitEnabled"];
                if (!string.IsNullOrEmpty(qle))
                    Config.QuitLimitEnabled = qle.ToLower() == "true";
                var lce = args.Parameters["lateCompEnabled"];
                if (!string.IsNullOrEmpty(lce))
                    Config.LateCompEnabled = lce.ToLower() == "true";

                // ═══ RCON 配置 ═══
                var rconEn = args.Parameters["rconEnabled"];
                if (!string.IsNullOrEmpty(rconEn))
                    Config.RconEnabled = rconEn.ToLower() == "true";
                var rconPort = args.Parameters["rconPort"];
                if (!string.IsNullOrEmpty(rconPort) && int.TryParse(rconPort, out var rp) && rp > 0 && rp < 65536)
                    Config.RconPort = rp;
                var rconPwd = args.Parameters["rconPassword"];
                if (!string.IsNullOrEmpty(rconPwd))
                    Config.RconPassword = rconPwd;
                var rconExt = args.Parameters["rconExternalPort"];
                if (!string.IsNullOrEmpty(rconExt) && int.TryParse(rconExt, out var re) && re > 0 && re < 65536)
                    Config.RconExternalPort = re;

                SaveConfig();
                TShock.Log.ConsoleInfo($"[TSWeb] REST 更新配置: mode={Config.AutoRegisterMode}, bossLimitMode={Config.BossLimitMode}, minPlayers={Config.BossLimitMinPlayers}, quitLimit={Config.QuitLimitEnabled}, lateComp={Config.LateCompEnabled}, rconEnabled={Config.RconEnabled}, rconPort={Config.RconPort}");
                return new { status = "200", message = "配置已保存" };
            }
            catch (Exception ex)
            {
                return new { status = "500", error = ex.Message };
            }
        }

        /// <summary>
        /// 检查玩家的 IP 或 UUID 是否存在生效中的封禁
        /// </summary>
        private static bool CheckPlayerBanned(TSPlayer player)
        {
            try
            {
                // 检查 IP 封禁
                if (!string.IsNullOrEmpty(player.IP))
                {
                    string ipIdentifier = $"ip:{player.IP}";
                    if (TShock.Bans.Bans.Values.Any(b =>
                        b.Identifier.Equals(ipIdentifier, StringComparison.OrdinalIgnoreCase) &&
                        DateTime.UtcNow > b.BanDateTime && DateTime.UtcNow < b.ExpirationDateTime))
                        return true;
                }

                // 检查 UUID 封禁
                if (!string.IsNullOrEmpty(player.UUID))
                {
                    string uuidIdentifier = $"uuid:{player.UUID}";
                    if (TShock.Bans.Bans.Values.Any(b =>
                        b.Identifier.Equals(uuidIdentifier, StringComparison.OrdinalIgnoreCase) &&
                        DateTime.UtcNow > b.BanDateTime && DateTime.UtcNow < b.ExpirationDateTime))
                        return true;
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 封禁检查失败: {ex.Message}");
            }

            return false;
        }
    }
}
