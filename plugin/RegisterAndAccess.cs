using System;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;
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

        // 一次性特许IP列表（仅内存，不持久化，用完即删）
        private static readonly HashSet<string> _allowedIPs = new();

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
            else if (mode == RegisterMode.Block && args.MsgID == PacketTypes.ContinueConnecting2)
            {
                // 检查是否在白名单IP中
                if (_allowedIPs.Contains(player.IP))
                {
                    _allowedIPs.Remove(player.IP);
                    TShock.Log.ConsoleInfo($"[TSWeb] 特许IP放行（已消耗）: {player.IP} / {player.Name}");
                    return;
                }
                HandleBlockMode(player, args);
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

        private static void HandleBlockMode(TSPlayer player, GetDataEventArgs args)
        {
            var account = TShock.UserAccounts.GetUserAccountByName(player.Name);

            // 未注册玩家 → 直接断联（世界数据尚未发送，玩家看不到世界）
            if (account == null)
            {
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
                return;
            }

            // 已注册但 UUID 不匹配 → 直接断联
            if (account.UUID != player.UUID)
            {
                args.Handled = true;
                Task.Run(async () =>
                {
                    await Task.Delay(200);
                    if (player.ConnectionAlive)
                    {
                        player.Disconnect("UUID验证失败，请使用原设备登录或联系管理员");
                    }
                });
                TShock.Log.ConsoleInfo($"[TSWeb] 阻止UUID不匹配玩家进入: {player.Name}");
                return;
            }

            // 已注册且 UUID 匹配 → 放行
            TShock.Log.ConsoleInfo($"[TSWeb] UUID验证通过: {player.Name}");
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
                case "allow":
                    HandleAllowCommand(args);
                    break;
                case "allowlist":
                    ShowAllowList(args);
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

        private static void HandleAllowCommand(CommandArgs args)
        {
            if (args.Parameters.Count < 2)
            {
                args.Player.SendErrorMessage("用法: /ar allow <IP>");
                return;
            }

            var ip = args.Parameters[1];
            _allowedIPs.Add(ip);
            args.Player.SendSuccessMessage($"[TSWeb] 已特许IP {ip} 一次性进入");
            TShock.Log.ConsoleInfo($"[TSWeb] 特许IP: {ip} (由 {args.Player.Name} 添加)");
        }

        private static void ShowAllowList(CommandArgs args)
        {
            if (_allowedIPs.Count == 0)
            {
                args.Player.SendInfoMessage("[TSWeb] 当前没有特许IP");
                return;
            }

            args.Player.SendInfoMessage($"[TSWeb] 当前特许IP列表 ({_allowedIPs.Count} 个):");
            foreach (var ip in _allowedIPs)
            {
                args.Player.SendInfoMessage($"  - {ip}");
            }
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
            args.Player.SendInfoMessage("/ar allow <IP> - 特许某IP一次性进入（仅本次生效）");
            args.Player.SendInfoMessage("/ar allowlist - 查看当前特许IP列表");
            args.Player.SendInfoMessage("/autoregister mode [default/auto/disable/block] - 设置注册模式");
            args.Player.SendInfoMessage("/autoregister reload - 重新加载配置文件");
            args.Player.SendInfoMessage("/autoregister status - 查看当前状态");
            args.Player.SendInfoMessage("");
            args.Player.SendInfoMessage("模式说明:");
            args.Player.SendInfoMessage("  default - 默认行为，允许手动注册");
            args.Player.SendInfoMessage("  auto - 自动注册新玩家，禁用手动注册");
            args.Player.SendInfoMessage("  disable - 完全禁用注册");
            args.Player.SendInfoMessage("  block - 阻止未注册/UUID不匹配玩家进入");
            args.Player.SendInfoMessage("");
            args.Player.SendInfoMessage("特许命令:");
            args.Player.SendInfoMessage("  /ar allow <IP> - 特许某IP一次性进入，用完即失效");
            args.Player.SendInfoMessage("  /ar allowlist - 查看剩余特许IP");
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
                var ble = args.Parameters["bossLimitEnabled"];
                if (!string.IsNullOrEmpty(ble))
                    Config.BossLimitEnabled = ble.ToLower() == "true";
                var blmp = args.Parameters["bossLimitMinPlayers"];
                if (!string.IsNullOrEmpty(blmp) && int.TryParse(blmp, out var num) && num > 0)
                    Config.BossLimitMinPlayers = num;
                SaveConfig();
                TShock.Log.ConsoleInfo($"[TSWeb] REST 更新配置: mode={Config.AutoRegisterMode}, bossLimit={Config.BossLimitEnabled}, minPlayers={Config.BossLimitMinPlayers}");
                return new { status = "200", message = "配置已保存" };
            }
            catch (Exception ex)
            {
                return new { status = "500", error = ex.Message };
            }
        }

        // ═══════════════════════════════════════════
        // 特许IP REST API
        // ═══════════════════════════════════════════

        public static object GetAllowList(RestRequestArgs args)
        {
            return new
            {
                status = "200",
                ips = _allowedIPs.ToArray()
            };
        }

        public static object AddAllowIP(RestRequestArgs args)
        {
            var ip = args.Parameters["ip"];
            if (string.IsNullOrEmpty(ip))
                return new { status = "400", error = "缺少IP参数" };

            _allowedIPs.Add(ip);
            TShock.Log.ConsoleInfo($"[TSWeb] REST 特许IP: {ip}");
            return new { status = "200", message = $"已添加 {ip}" };
        }

        public static object ClearAllowList(RestRequestArgs args)
        {
            var count = _allowedIPs.Count;
            _allowedIPs.Clear();
            TShock.Log.ConsoleInfo($"[TSWeb] REST 清空特许IP列表 (共{count}个)");
            return new { status = "200", message = $"已清空 {count} 个IP" };
        }
    }
}
