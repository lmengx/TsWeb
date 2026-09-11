using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Streams;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
    /// <summary>
    /// BugFixes 模块配置（TSWeb/BugFixes.json）
    /// 总开关默认关；子功能默认全开。加载时 ensure 补齐缺失字段并落盘。
    /// </summary>
    public class BugFixesConfig
    {
        [JsonProperty("启用")]
        public bool Enabled { get; set; } = false;

        [JsonProperty("登录修复")]
        public bool LoginFix { get; set; } = true;

        [JsonProperty("宝箱修复")]
        public bool ChestFix { get; set; } = true;

        [JsonProperty("召唤物限制")]
        public bool MinionLimit { get; set; } = true;

        [JsonProperty("粒子防线")]
        public bool Lightning { get; set; } = true;
    }

    /// <summary>
    /// 修复 TShock 恶性 Bug 的通用模块
    /// 子模块：
    ///   LoginFix — 修复 UUID 变更导致无法进服的连接层 Bug
    ///   ChestFix — 修复宝箱数据包校验缺失漏洞
    ///   DualChestFix — 禁止玩家同时打开多个箱子（防双箱刷物品）
    ///   MinionLimit — 召唤物数量上限（异常数据限制）
    ///   ParticleGuard（Lightning）— 闪电粒子洪泛防线
    ///
    /// 配置化：总开关（Enabled 默认 false）+ 子功能开关（默认全开）；
    /// REST 动态开关通过 ApplyConfig 先全卸载再按新配置挂载，天然幂等。
    /// </summary>
    public static class BugFixes
    {
        private static bool _isInitialized = false;
        private static TerrariaPlugin? _plugin;
        public static BugFixesConfig Config { get; private set; } = new BugFixesConfig();
        private static string ConfigPath => Path.Combine(TShock.SavePath, "TSWeb", "BugFixes.json");
        /// <summary>当前已挂载的子模块名集合（LoginFix/ChestFix/MinionLimit/Lightning）</summary>
        private static readonly HashSet<string> _activeModules = new(StringComparer.OrdinalIgnoreCase);

        public static void Initialize(TerrariaPlugin plugin)
        {
            if (_isInitialized)
                return;

            _plugin = plugin;
            LoadConfig();
            ApplySubmodules();

            _isInitialized = true;
            TShock.Log.ConsoleInfo($"[TSWeb] BugFixes 已加载 - 启用:{Config.Enabled}, 登录修复:{Config.LoginFix}, 宝箱修复:{Config.ChestFix}, 召唤物限制:{Config.MinionLimit}, 粒子防线:{Config.Lightning}");
        }

        public static void Dispose(TerrariaPlugin plugin)
        {
            if (!_isInitialized)
                return;

            UnloadAllSubmodules();
            _isInitialized = false;
        }

        // ═══════════════ 配置读写（ensure 补齐缺失字段） ═══════════════

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
                    Config = JsonConvert.DeserializeObject<BugFixesConfig>(json) ?? new BugFixesConfig();
                }
                else
                {
                    Config = new BugFixesConfig();
                }

                // ensure：缺失字段用默认值补齐（总开关关 / 子功能开）并落盘
                File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(Config, Formatting.Indented));
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 加载 BugFixes 配置失败: {ex.Message}");
                Config = new BugFixesConfig();
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
                File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(Config, Formatting.Indented));
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] 保存 BugFixes 配置失败: {ex.Message}");
            }
        }

        /// <summary>按当前配置挂载启用的子模块（先清空已挂集合，幂等）</summary>
        private static void ApplySubmodules()
        {
            _activeModules.Clear();

            // 粒子防线运行时开关与配置同步（lightning 关闭时 /lightning 命令拒绝、Hook 不拦截）
            ParticleGuard.Enabled = Config.Enabled && Config.Lightning;

            if (!Config.Enabled)
                return;

            if (Config.LoginFix)
            {
                LoginFix.Initialize(_plugin!);
                _activeModules.Add("LoginFix");
            }
            if (Config.ChestFix)
            {
                ChestFix.Initialize(_plugin!);
                _activeModules.Add("ChestFix");
            }
            if (Config.MinionLimit)
            {
                MinionLimit.Initialize(_plugin!);
                _activeModules.Add("MinionLimit");
            }
            if (Config.Lightning)
            {
                ParticleGuard.Initialize(_plugin!);
                _activeModules.Add("Lightning");
            }
        }

        /// <summary>卸载全部已挂载子模块（幂等）</summary>
        private static void UnloadAllSubmodules()
        {
            foreach (var m in _activeModules)
            {
                try
                {
                    switch (m)
                    {
                        case "LoginFix": LoginFix.Dispose(_plugin!); break;
                        case "ChestFix": ChestFix.Dispose(_plugin!); break;
                        case "MinionLimit": MinionLimit.Dispose(_plugin!); break;
                        case "Lightning": ParticleGuard.Dispose(); break;
                    }
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleError($"[TSWeb] 卸载子模块 {m} 失败: {ex.Message}");
                }
            }
            _activeModules.Clear();
        }

        /// <summary>REST 动态开关：先全卸载，再按新配置挂载</summary>
        public static void ApplyConfig(BugFixesConfig newConfig)
        {
            UnloadAllSubmodules();
            Config = newConfig;
            SaveConfig();
            ApplySubmodules();
            TShock.Log.ConsoleInfo($"[TSWeb] BugFixes 配置已应用 - 启用:{Config.Enabled}, 登录修复:{Config.LoginFix}, 宝箱修复:{Config.ChestFix}, 召唤物限制:{Config.MinionLimit}, 粒子防线:{Config.Lightning}");
        }

        // ═══════════════ REST API ═══════════════

        /// <summary>GET /data/bugfix — 读取当前配置</summary>
        public static object GetConfigApi(RestRequestArgs args)
        {
            return new
            {
                status = "200",
                enabled = Config.Enabled,
                loginFix = Config.LoginFix,
                chestFix = Config.ChestFix,
                minionLimit = Config.MinionLimit,
                lightning = Config.Lightning
            };
        }

        /// <summary>POST /data/bugfix/set — 动态开关（缺省参数保持原值）</summary>
        public static object SetConfigApi(RestRequestArgs args)
        {
            try
            {
                var cfg = new BugFixesConfig
                {
                    Enabled = Config.Enabled,
                    LoginFix = Config.LoginFix,
                    ChestFix = Config.ChestFix,
                    MinionLimit = Config.MinionLimit,
                    Lightning = Config.Lightning
                };

                var enabled = args.Parameters["enabled"];
                if (!string.IsNullOrEmpty(enabled))
                    cfg.Enabled = enabled.ToLower() == "true";
                var loginFix = args.Parameters["loginFix"];
                if (!string.IsNullOrEmpty(loginFix))
                    cfg.LoginFix = loginFix.ToLower() == "true";
                var chestFix = args.Parameters["chestFix"];
                if (!string.IsNullOrEmpty(chestFix))
                    cfg.ChestFix = chestFix.ToLower() == "true";
                var minionLimit = args.Parameters["minionLimit"];
                if (!string.IsNullOrEmpty(minionLimit))
                    cfg.MinionLimit = minionLimit.ToLower() == "true";
                var lightning = args.Parameters["lightning"];
                if (!string.IsNullOrEmpty(lightning))
                    cfg.Lightning = lightning.ToLower() == "true";

                ApplyConfig(cfg);
                return new { status = "200", message = "配置已保存" };
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[TSWeb] BugFixes 配置保存失败: {ex.Message}");
                return new RestObject("500") { { "error", ex.Message } };
            }
        }

        // ==========================================================================
        // 子模块1: LoginFix — 在进服前校验已有账户的登录密码
        // ==========================================================================
        public static class LoginFix
        {
            private static readonly HashSet<string> _passwordPending = new();
            private static bool _initialized;

            public static void Initialize(TerrariaPlugin plugin)
            {
                if (_initialized)
                    return;
                ServerApi.Hooks.NetGetData.Register(plugin, OnGetData, int.MaxValue);
                _initialized = true;
            }

            public static void Dispose(TerrariaPlugin plugin)
            {
                if (!_initialized)
                    return;
                ServerApi.Hooks.NetGetData.Deregister(plugin, OnGetData);
                _initialized = false;
            }

            private static void OnGetData(GetDataEventArgs args)
            {
                if (args.Handled)
                    return;

                if (args.MsgID != PacketTypes.ContinueConnecting2 && args.MsgID != PacketTypes.PasswordSend)
                    return;

                var player = TShock.Players[args.Msg.whoAmI];
                if (player == null || string.IsNullOrEmpty(player.Name))
                    return;

                if (player.IsLoggedIn)
                    return;

                var account = TShock.UserAccounts.GetUserAccountByName(player.Name);
                if (account == null || account.UUID == player.UUID)
                    return;

                if (args.MsgID == PacketTypes.ContinueConnecting2)
                {
                    args.Handled = true;
                    player.RequiresPassword = true;
                    _passwordPending.Add(player.Name);
                    NetMessage.SendData((int)PacketTypes.PasswordRequired, player.Index);
                    TShock.Log.ConsoleInfo($"[TSWeb][LoginFix] UUID不匹配，请求密码验证: {player.Name}");
                }
                else if (args.MsgID == PacketTypes.PasswordSend)
                {
                    HandlePasswordChallenge(player, args);
                }
            }

            private static void HandlePasswordChallenge(TSPlayer player, GetDataEventArgs args)
            {
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
                    TShock.Log.ConsoleWarn($"[TSWeb] 来自{player.IP}的访问尝试登录账号:{player.Name},但密码验证失败:");
                    // 踢出文本可配置（TSWeb/config.json 中的 KickPasswordMessage）
                    var kickMessage = AutoRegister.Config?.KickPasswordMessage ??
                        "密码错误\n" +
                        "请输入角色密码。已登录设备可使用 /pwd 新密码 设置密码。\n" +
                        "如果没有可以登录的设备，请联系服务器管理员。\n" +
                        "如果这是你第一次进服，说明你的角色名已被占用，请更换。";
                    player.Kick(kickMessage, true, true);
                    return;
                }

                _passwordPending.Remove(player.Name);
                TShock.Log.ConsoleInfo($"[TSWeb] 密码验证通过: {player.Name}");

                player.RequiresPassword = false;

                if (player.State == (int)ConnectionState.AssigningPlayerSlot)
                    player.State = (int)ConnectionState.AwaitingPlayerInfo;

                NetMessage.SendData((int)PacketTypes.WorldInfo, player.Index);

                player.PlayerData = TShock.CharacterDB.GetPlayerData(player, account.ID);

                var group = TShock.Groups.GetGroupByName(account.Group);
                if (!TShock.Groups.AssertGroupValid(player, group, true))
                    return;

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

                TShock.UserAccounts.SetUserAccountUUID(account, player.UUID);

                player.SendSuccessMessage($"验证通过: {account.Name}");
                PlayerHooks.OnPlayerPostLogin(player);
            }
        }

        // ==========================================================================
        // 子模块2: ChestFix — 修复宝箱数据包校验缺失漏洞
        // ==========================================================================
        public static class ChestFix
        {
            private const string Ver = "1.0.3";
            private static int _blockedCount;
            private static bool _initialized;

            public static void Initialize(TerrariaPlugin plugin)
            {
                if (_initialized)
                    return;
                GetDataHandlers.ChestItemChange += OnChestItemChange;
                ServerApi.Hooks.NetGetData.Register(plugin, OnNetGetData, -1000);
                Commands.ChatCommands.Add(new Command("chestfix.admin", ChestFixCommand, "chestfix", "cstf"));
                _initialized = true;
            }

            public static void Dispose(TerrariaPlugin plugin)
            {
                if (!_initialized)
                    return;
                GetDataHandlers.ChestItemChange -= OnChestItemChange;
                ServerApi.Hooks.NetGetData.Deregister(plugin, OnNetGetData);
                Commands.ChatCommands.RemoveAll(c => c.CommandDelegate == ChestFixCommand);
                _initialized = false;
            }

            private static void OnChestItemChange(object? sender, GetDataHandlers.ChestItemEventArgs e)
            {
                if (e.ID < 0 || e.ID >= Main.chest.Length)
                {
                    AuditAndKick(e.Player, "ChestItem", "越界宝箱ID", $"id={e.ID}", "");
                    e.Handled = true;
                    return;
                }

                if (Main.chest[e.ID] == null)
                {
                    AuditAndKick(e.Player, "ChestItem", "空宝箱引用", $"id={e.ID}", "");
                    e.Handled = true;
                    return;
                }

                if (e.Slot < 0 || e.Slot >= Main.chest[e.ID].maxItems)
                {
                    AuditAndKick(e.Player, "ChestItem", "越界槽位", $"slot={e.Slot}/max={Main.chest[e.ID].maxItems}", "");
                    e.Handled = true;
                    return;
                }

                if (e.Type < 0 || e.Type >= ItemID.Count)
                {
                    AuditAndKick(e.Player, "ChestItem", "无效物品ID", $"type={e.Type}", "");
                    e.Handled = true;
                    return;
                }

                if (e.Prefix < 0)
                {
                    AuditAndKick(e.Player, "ChestItem", "无效词缀", $"prefix={e.Prefix}", "");
                    e.Handled = true;
                    return;
                }

                if (e.Stacks < 0)
                {
                    AuditAndKick(e.Player, "ChestItem", "负堆叠", $"stacks={e.Stacks}", "");
                    e.Handled = true;
                    return;
                }
            }

			private static void OnNetGetData(GetDataEventArgs e)
			{
				// 已被 TShock/Bouncer/其他插件判定并处理 → 不重复判定（避免二次误罚）
				if (e.Handled)
					return;

				// ═══ 跨服桥接玩家豁免 ═══
				// ① A 服（源服）侧：桥接玩家开箱子的包已被 CrossTransfer 转发目标服，
				//    这里用本地箱子数组审查必然误判 → 直接跳过（其交互不属于本服攻击面）。
				if (CrossTransfer.IsBridging(e.Msg.whoAmI))
					return;
				// ② B 服（目标服）侧：跨服玩家客户端世界状态可能未完全同步，同样豁免。
				if (TransferProtocol.PreTransfers.ContainsKey(e.Msg.whoAmI))
					return;

				switch (e.MsgID)
				{
					case PacketTypes.ChestOpen:          // 33：负ID+nameLen 越界攻击面
						HandleChestOpenPacket(e);
						break;
					case PacketTypes.ChestGetContents:  // 31：越界坐标
						HandleChestGetContentsPacket(e);
						break;
					case (PacketTypes)155:              // SyncChestSize：原版客户端从不主动上行 → 确定性恶意面
						HandleSyncChestSizePacket(e);
						break;
					case (PacketTypes)153:              // NPCDebuffDamage：上行=加血无敌/秒杀 → 确定性恶意面
						HandleNpcDebuffDamagePacket(e);
						break;
				}
			}

            private static void HandleChestOpenPacket(GetDataEventArgs e)
            {
                var plr = TShock.Players[e.Msg.whoAmI];
                if (plr == null || !plr.Active)
                    return;

                using var ms = new MemoryStream(e.Msg.readBuffer, e.Index, Math.Max(e.Length - 1, 0));
                var raw = HexOf(e);
                try
                {
                    // 最短合法载荷：chestId(2) + x(2) + y(2) + nameLen(1) = 7 字节
                    if (ms.Length < 7)
                    {
                        AuditAndKick(plr, "ChestOpen", "畸形包", $"长度不足 len={ms.Length}", raw);
                        e.Handled = true;
                        return;
                    }

                    var id = ms.ReadInt16();
                    ms.ReadInt16();  // x
                    ms.ReadInt16();  // y
                    var nameLen = ms.ReadByte();

                    // 无名字：打开/关闭(-1)/切换箱子，核心逻辑安全，放行
                    if (nameLen == 0)
                        return;

                    // 带名字 = 改名操作。
                    // 服务端核心 case 33 改名时用 Main.player[whoAmI].chest（玩家当前打开的箱子）定位，
                    // 完全忽略包内 id —— 原版客户端"仅改名不换箱子"时发送 id=-1 + 名字 是正常行为。
                    // 真实攻击面：玩家未打开任何箱子却发改名包 → 核心访问 Main.chest[-1] 越界崩服。
                    var playerChest = Main.player[e.Msg.whoAmI].chest;
                    if (playerChest < 0 || playerChest >= Main.chest.Length || Main.chest[playerChest] == null)
                    {
                        AuditAndKick(plr, "ChestOpen", "未打开箱子发送改名包", $"player.chest={playerChest} id={id} nameLen={nameLen}", raw);
                        e.Handled = true;
                        return;
                    }

                    // 已打开有效箱子 → 正常改名，放行（包 id 仅用于关闭/切换箱子，由核心处理）
                }
                catch (Exception ex)
                {
                    AuditAndKick(plr, "ChestOpen", "解析异常", ex.Message, raw);
                    e.Handled = true;
                }
            }

            private static void HandleChestGetContentsPacket(GetDataEventArgs e)
            {
                var plr = TShock.Players[e.Msg.whoAmI];
                if (plr == null || !plr.Active)
                    return;

                using var ms = new MemoryStream(e.Msg.readBuffer, e.Index, Math.Max(e.Length - 1, 0));
                var raw = HexOf(e);
                try
                {
                    if (ms.Length < 4)  // x(2) + y(2)
                    {
                        AuditAndKick(plr, "ChestGetContents", "畸形包", $"长度不足 len={ms.Length}", raw);
                        e.Handled = true;
                        return;
                    }

                    var x = ms.ReadInt16();
                    var y = ms.ReadInt16();

                    var chestId = Chest.FindChest(x, y);
                    if (chestId < 0 || chestId >= Main.chest.Length)
                    {
                        AuditAndKick(plr, "ChestGetContents", "无效坐标", $"({x},{y})", raw);
                        e.Handled = true;
                        return;
                    }

                    if (Main.chest[chestId] == null)
                    {
                        AuditAndKick(plr, "ChestGetContents", "空宝箱", $"({x},{y})", raw);
                        e.Handled = true;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    AuditAndKick(plr, "ChestGetContents", "解析异常", ex.Message, raw);
                    e.Handled = true;
                }
            }

            /// <summary>
            /// SyncChestSize(155) 上行监测。
            /// 原版核心中 155 仅由服务端经 SendChestContentsTo 下行下发，客户端从不主动上行；
            /// 任何上行 155 = TerraAngel SyncChestSizeExploit 式攻击（Resize(32767) OOM / NRE）。
            /// </summary>
            private static void HandleSyncChestSizePacket(GetDataEventArgs e)
            {
                var plr = TShock.Players[e.Msg.whoAmI];
                if (plr == null || !plr.Active)
                    return;

                using var ms = new MemoryStream(e.Msg.readBuffer, e.Index, Math.Max(e.Length - 1, 0));
                var raw = HexOf(e);
                try
                {
                    if (ms.Length < 4)  // chestId(2) + newSize(2)
                    {
                        AuditAndKick(plr, "SyncChestSize", "畸形包", $"长度不足 len={ms.Length}", raw);
                        e.Handled = true;
                        return;
                    }

                    var chestId = ms.ReadInt16();
                    var newSize = ms.ReadInt16();

                    // 上行 155 确定恶意 → 一律拦截，阻止 Resize 内存放大/NRE
                    e.Handled = true;

                    string verdict;
                    if (newSize > Chest.DefaultMaxItems)  // >40：扩容攻击（32767 是 TerraAngel 默认载荷）
                        verdict = "恶意扩容";
                    else if (chestId < 0 || chestId >= Main.chest.Length || Main.chest[chestId] == null)
                        verdict = "非法索引";
                    else
                        verdict = "异常上行";

                    AuditAndKick(plr, "SyncChestSize", verdict, $"chest={chestId} newSize={newSize}", raw);
                }
                catch (Exception ex)
                {
                    AuditAndKick(plr, "SyncChestSize", "解析异常", ex.Message, raw);
                    e.Handled = true;
                }
            }

            /// <summary>
            /// NPCDebuffDamage(153) 上行监测。
            /// 原版核心中 153 仅由服务端广播减益伤害（case 153 / GetHurtByDebuff 内广播），客户端从不主动上行；
            /// TerraAngel 用它做两种攻击：
            ///   1. NPCDebuffDamageExploit — 负伤害给全图 NPC 加血至 int.MaxValue（Make All NPC Invincible）
            ///   2. Butcher — 正伤害 32767 连发秒杀 NPC
            /// 任何上行 153 = 确定性恶意。
            /// </summary>
            private static void HandleNpcDebuffDamagePacket(GetDataEventArgs e)
            {
                var plr = TShock.Players[e.Msg.whoAmI];
                if (plr == null || !plr.Active)
                    return;

                using var ms = new MemoryStream(e.Msg.readBuffer, e.Index, Math.Max(e.Length - 1, 0));
                var raw = HexOf(e);
                try
                {
                    if (ms.Length < 3)  // npcIndex(1) + damage(2)
                    {
                        AuditAndKick(plr, "NPCDebuffDamage", "畸形包", $"长度不足 len={ms.Length}", raw);
                        e.Handled = true;
                        return;
                    }

                    var npcIndex = ms.ReadByte();
                    var damage = ms.ReadInt16();

                    // 上行 153 确定恶意 → 一律拦截，阻止服务端 GetHurtByDebuff（加血无敌/秒杀）
                    e.Handled = true;

                    string verdict;
                    if (damage < 0)
                        verdict = "负伤害加血";   // Make All NPC Invincible 攻击
                    else if (npcIndex >= Main.npc.Length)
                        verdict = "越界索引";     // byte 索引超出 Main.npc 数组
                    else
                        verdict = "正伤害秒杀";   // Butcher 攻击

                    AuditAndKick(plr, "NPCDebuffDamage", verdict, $"npc={npcIndex} damage={damage}", raw);
                }
                catch (Exception ex)
                {
                    AuditAndKick(plr, "NPCDebuffDamage", "解析异常", ex.Message, raw);
                    e.Handled = true;
                }
            }

            /// <summary>审计本次违规并立即踢出玩家（一次违规即踢，不计数）</summary>
            private static void AuditAndKick(TSPlayer plr, string packet, string verdict, string detail, string raw)
            {
                Interlocked.Increment(ref _blockedCount);
                LogAudit(plr, packet, verdict, detail, raw);
                KickPlayer(plr, $"发送异常数据包: {packet} {detail}");
            }

            /// <summary>结构化审计日志：玩家/账号/IP/包/判定/详情/原始字节</summary>
            private static void LogAudit(TSPlayer plr, string packet, string verdict, string detail, string raw)
            {
                var account = plr.Account?.Name ?? "未登录";
                TShock.Log.ConsoleInfo($"[ChestFix][审计] 玩家={plr.Name} 账号={account} IP={plr.IP} 包={packet} 判定={verdict} 详情={detail} hex={raw}");
            }

            /// <summary>取包原始字节（前 64 字节）便于回溯取证</summary>
            private static string HexOf(GetDataEventArgs e)
            {
                var len = Math.Min(Math.Max(e.Length - 1, 0), 64);
                var sb = new StringBuilder();
                for (var i = 0; i < len; i++)
                    sb.Append(e.Msg.readBuffer[e.Index + i].ToString("X2"));
                return sb.ToString();
            }

            private static void ChestFixCommand(CommandArgs args)
            {
                var cmd = args.Parameters.Count > 0 ? args.Parameters[0].ToLower() : "";

                switch (cmd)
                {
                    case "reset":
                        _blockedCount = 0;
                        args.Player.SendInfoMessage("[ChestFix] 计数器已重置");
                        break;

                    case "scan":
                        ScanChests(args);
                        break;

                    default:
                        var sb = new StringBuilder();
                        sb.AppendLine($"=== [ChestFix] 宝箱安全修复 v{Ver} ===");
                        sb.AppendLine($"  已拦截恶意包: {_blockedCount}");
                        sb.AppendLine($"  用法:");
                        sb.AppendLine($"    /chestfix           - 显示状态");
                        sb.AppendLine($"    /chestfix reset     - 重置计数器");
                        sb.AppendLine($"    /chestfix scan      - 扫描世界宝箱脏数据");
                        sb.AppendLine($"    /chestfix scan fix  - 扫描并自动修复");
                        args.Player.SendInfoMessage(sb.ToString().TrimEnd());
                        break;
                }
            }

            private static void ScanChests(CommandArgs args)
            {
                var fix = args.Parameters.Count > 1 && args.Parameters[1].ToLower() == "fix";

                args.Player.SendInfoMessage($"[ChestFix] 正在扫描...{(fix ? "" : "(预览模式，加 fix 修复)")}");

                int totalChests = 0, orphanedChests = 0, dirtySlots = 0, fixedSlots = 0, fixedOrphans = 0;
                int abnormalLen = 0, fixedLen = 0;
                int duplicatePos = 0, fixedDup = 0;
                var seenCoords = new HashSet<(int, int)>();
                var report = new StringBuilder();

                for (int i = 0; i < Main.chest.Length; i++)
                {
                    var chest = Main.chest[i];
                    if (chest == null) continue;
                    totalChests++;

                    bool chestBlockExists = ChestBlockExists(chest.x, chest.y);
                    if (!chestBlockExists)
                    {
                        report.AppendLine($"  [孤悬] 宝箱[{i}] 坐标({chest.x},{chest.y}) 方块已不存在");
                        orphanedChests++;
                        if (fix)
                        {
                            Main.chest[i] = null;
                            fixedOrphans++;
                        }
                        continue;
                    }

                    var coord = (chest.x, chest.y);
                    if (!seenCoords.Add(coord))
                    {
                        report.AppendLine($"  [重复] 宝箱[{i}] 坐标({chest.x},{chest.y}) 与前面的宝箱位置相同");
                        duplicatePos++;
                        if (fix)
                        {
                            Main.chest[i] = null;
                            fixedDup++;
                        }
                        continue;
                    }

                    if (chest.item.Length > 40)
                    {
                        report.AppendLine($"  [异常] 宝箱[{i}] 坐标({chest.x},{chest.y}) item数组长度={chest.item.Length}, 标准最大=40");
                        abnormalLen++;
                        if (fix)
                        {
                            var oldItems = chest.item;
                            chest.maxItems = 40;
                            chest.item = new Item[40];
                            for (int s = 0; s < 40 && s < oldItems.Length; s++)
                                chest.item[s] = oldItems[s] ?? new Item();
                            fixedLen++;
                        }
                    }

                    for (int s = 0; s < chest.item.Length; s++)
                    {
                        var item = chest.item[s];
                        if (item == null || item.type == 0) continue;

                        var issues = new List<string>();

                        if (item.type < 0 || item.type >= ItemID.Count)
                            issues.Add($"type={item.type} 超范围[0,{ItemID.Count})");

                        var def = new Item();
                        def.netDefaults(item.type);
                        if (item.stack < 0 || item.stack > def.maxStack)
                            issues.Add($"stack={item.stack}/{def.maxStack} 超限");

                        if (item.prefix < 0)
                            issues.Add($"prefix={item.prefix} 为负数");

                        if (issues.Count == 0) continue;

                        dirtySlots++;
                        report.AppendLine($"  [脏数据] 宝箱[{i}] slot={s} {string.Join(", ", issues)}");

                        if (fix)
                        {
                            chest.item[s] = new Item();
                            fixedSlots++;
                        }
                    }
                }

                var result = new StringBuilder();
                result.AppendLine($"[ChestFix] 扫描完成: 共扫描 {totalChests} 个宝箱");

                if (totalChests == 0)
                {
                    result.Append("  世界中没有任何宝箱");
                }
                else if (orphanedChests == 0 && dirtySlots == 0 && abnormalLen == 0 && duplicatePos == 0)
                {
                    result.Append("  未发现异常数据 ✓");
                }
                else
                {
                    if (orphanedChests > 0)
                        result.AppendLine($"  发现 {orphanedChests} 个孤悬宝箱" +
                            (fix ? $" (已清理 {fixedOrphans} 个)" : ""));
                    if (duplicatePos > 0)
                        result.AppendLine($"  发现 {duplicatePos} 个重复坐标宝箱" +
                            (fix ? $" (已移除 {fixedDup} 个)" : ""));
                    if (abnormalLen > 0)
                        result.AppendLine($"  发现 {abnormalLen} 个数组长度异常宝箱" +
                            (fix ? $" (已截断 {fixedLen} 个)" : ""));
                    if (dirtySlots > 0)
                        result.AppendLine($"  发现 {dirtySlots} 个脏数据槽位" +
                            (fix ? $" (已修复 {fixedSlots} 个)" : ""));
                }

                var msg = result.ToString().TrimEnd();
                args.Player.SendInfoMessage(msg);
                TShock.Log.ConsoleInfo(msg);

                if (fix && (fixedOrphans > 0 || fixedLen > 0 || fixedSlots > 0 || fixedDup > 0))
                {
                    args.Player.SendInfoMessage("[ChestFix] 修复已写入内存, 重新打开宝箱即可看到效果");
                    TShock.Log.ConsoleInfo($"[ChestFix] 内存修复: 清理 {fixedOrphans} 孤悬, 移除 {fixedDup} 重复, 截断 {fixedLen} 长度异常, 修复 {fixedSlots} 脏槽位");
                }
            }

            private static bool ChestBlockExists(int x, int y)
            {
                if (x < 0 || x >= Main.maxTilesX || y < 0 || y >= Main.maxTilesY)
                    return false;

                var tile = Main.tile[x, y];
                if (tile == null || !tile.active())
                    return false;

                int type = tile.type;
                return type == TileID.Containers
                    || type == TileID.Containers2
                    || type == TileID.Dressers;
            }

            private static void KickPlayer(TSPlayer player, string reason)
            {
                try
                {
                    player.Kick($"发送恶意数据包: {reason}", true);
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleError($"[ChestFix] 踢出玩家失败: {ex.Message}");
                }
            }
        }

        // ==========================================================================
        // 子模块3: MinionLimit — 召唤物数量上限（异常数据限制）
        // 检测依据：实时统计该玩家名下活跃召唤弹幕占用的槽位（minionSlots 之和，
        // 遍历 Main.projectile，服务端权威、与客户端 UI 一致）。
        //
        // 【为什么不用 TPlayer.slotsMinions —— 误判根因（已查 Terraria 反编译源码确认）】
        // 服务端 slotsMinions 每帧由 Player.Update 重置为 0（Player.cs 26999-27000），
        // 再由 Projectile.Update 对所有活跃 minion 弹幕累加（Projectile.cs 15959）。
        // 但 Player.Update 在玩家 dead（23609）/ghost（23602）状态提前 return，
        // 跳过重置；而死亡玩家的召唤物弹幕不会立即消失（如飞行小鬼 375 timeLeft*=5，
        // 残留数千帧；弹幕清理只查掉线 !active 不查死亡 dead），每帧继续累加，
        // 导致 slotsMinions 在死亡/幽灵期间只增不减、虚高到 20+。
        // 玩家重生瞬间发 NewProjectile 包时读到该虚高值 → 把只召唤 2 个的玩家判成 21 踢出。
        // 实时统计 Main.projectile 不受该重置时序影响，任何时刻都等于当前真实活跃弹幕槽位。
        // 星尘龙段(626/627) 每段仅 0.5 槽、头部(625)/尾部(628) 原版不计（15933 排除），
        // 天然不会把星尘龙误判超限；按槽位求和而非弹幕条数，泰拉棱镜剑刃(946,1槽)也准确。
        // 实现参照 TShockPlugin-master/src/ServerTools 的 NewProj（e.Player 而非包内 owner）。
        //
        // 【误判修复（2026 对比其它插件方案后整改）】
        // 1. 统计循环原样计入"主人已死亡"的残留弹幕（注释声称排除但代码未实现）：
        //    死亡时召唤物残留（timeLeft 数千帧）在重生后仍 active 被计入 → 正常玩家被误踢。
        //    修复 a：统计时排除 owner 死亡/幽灵/不活跃的弹幕（与发送者存活检查互补的防御性过滤）；
        //    修复 b（根治）：GameUpdate 检测重生（dead:true→false）即清理该玩家名下残留召唤物弹幕
        //            （Kill + 全服广播 ProjectileDestroy，复用 PacketReceive.Clear*Projectiles 模式），
        //            重生后统计不再含死亡前残留，从根上消除虚高来源。
        // 2. 边界差 1：NewProjectile 包触发时本次弹幕尚未创建，原判定 slots<=Max 放行后实际超限。
        //    修复：slots + 本次槽位 > MaxMinions 才拦截。
        // 3. 硬编码 625/628（星尘龙头/尾）：改为通用 minionSlots > 0f 过滤，自动覆盖 0 槽弹幕，
        //    版本升级更健壮（对照 Fargo 的 minionSlots > 0f 写法）。
        // 4. 类型缓存重构为槽位值缓存（-1 未知 / 0 非召唤物或 0 槽 / >0 槽位），
        //    一次 SetDefaults 同时判定 minion/sentry/minionSlots。
        // ==========================================================================
        public static class MinionLimit
        {
            /// <summary>召唤槽位上限：超过即拦截创建并踢出（正常玩家极限约 11 槽，20 留足余量）</summary>
            private const int MaxMinions = 20;
            /// <summary>弹幕类型 → 召唤槽位占用（-1=未知, 0=非召唤物或0槽, &gt;0=召唤槽位）</summary>
            private static readonly float[] _slotCache = new float[ProjectileID.Count];
            /// <summary>每位玩家上一帧的死亡状态（用于检测重生并清理残留召唤物弹幕）</summary>
            private static readonly bool[] _wasDead = new bool[Main.maxPlayers];
            private static bool _initialized;

            public static void Initialize(TerrariaPlugin plugin)
            {
                if (_initialized)
                    return;
                Array.Fill(_slotCache, -1f);
                Array.Clear(_wasDead, 0, _wasDead.Length);
                GetDataHandlers.NewProjectile.Register(OnNewProjectile);
                ServerApi.Hooks.GameUpdate.Register(plugin, OnGameUpdate);
                _initialized = true;
                TShock.Log.ConsoleInfo($"[TSWeb] MinionLimit 已加载 (召唤物上限 {MaxMinions})");
            }

            public static void Dispose(TerrariaPlugin plugin)
            {
                if (!_initialized)
                    return;
                GetDataHandlers.NewProjectile.UnRegister(OnNewProjectile);
                ServerApi.Hooks.GameUpdate.Deregister(plugin, OnGameUpdate);
                _initialized = false;
            }

            /// <summary>
            /// 每帧检测玩家重生（dead:true → false）：重生即清理其名下残留召唤物弹幕。
            /// 原版死亡玩家的召唤物弹幕不会立即消失（如飞行小鬼 375 timeLeft*=5 残留数千帧），
            /// 若不清理会持续占用槽位，导致重生后首次召唤被误判超限。
            /// 清理后全服广播 ProjectileDestroy，客户端同步消失。
            /// </summary>
            private static void OnGameUpdate(EventArgs args)
            {
                try
                {
                    for (int i = 0; i < Main.maxPlayers; i++)
                    {
                        var t = Main.player[i];
                        if (t == null || !t.active)
                        {
                            _wasDead[i] = false;
                            continue;
                        }
                        if (_wasDead[i] && !t.dead)
                        {
                            // 重生：清理该玩家名下残留召唤物弹幕（全服广播）
                            for (int j = 0; j < Main.maxProjectiles; j++)
                            {
                                var p = Main.projectile[j];
                                if (!p.active || p.owner != i || !p.minion || p.sentry)
                                    continue;
                                p.Kill();
                                NetMessage.SendData((int)PacketTypes.ProjectileDestroy, -1, -1, null, j);
                            }
                            TShock.Log.ConsoleDebug($"[MinionLimit] 玩家#{i} 重生，已清理残留召唤物弹幕");
                        }
                        _wasDead[i] = t.dead;
                    }
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleError($"[MinionLimit] GameUpdate 异常: {ex.Message}");
                }
            }

            private static void OnNewProjectile(object? sender, GetDataHandlers.NewProjectileEventArgs e)
            {
                if (e.Handled)
                    return;

                // 本次弹幕的召唤槽位（0=非召唤物/0槽，不检查；哨兵/宠物/坐骑不计）
                float thisSlot = GetMinionSlots(e.Type);
                if (thisSlot <= 0f)
                    return;

                // 以发送者为准（e.Player），不能用包内 owner 字段（e.Owner 客户端可控，
                // 恶意包可伪造 owner 指向他人，导致检查并踢错玩家 —— ServerTools 参考实现同款写法）。
                var plr = e.Player;
                if (plr == null || !plr.Active || !plr.IsLoggedIn)
                    return;

                // 死亡/幽灵状态：Player.Update 提前 return 不重置 slotsMinions，
                // 且残留召唤物弹幕仍每帧累加 → slotsMinions 虚高，此时直接跳过检查。
                if (plr.TPlayer.dead || plr.TPlayer.ghost)
                    return;

                // 实时统计该玩家名下当前活跃召唤弹幕的槽位占用（minionSlots 之和）。
                // 不信任 TPlayer.slotsMinions：其准确性依赖 Player.Update 每帧重置，
                // 死亡/幽灵/重生瞬间均不可靠（详见模块注释）。
                float slots = 0f;
                for (int i = 0; i < Main.maxProjectiles; i++)
                {
                    var p = Main.projectile[i];
                    if (!p.active || p.owner != plr.Index || !p.minion || p.sentry)
                        continue;
                    // 仅统计"主人当前存活"的弹幕：死亡/幽灵/离线的残留召唤物不计入
                    // （重生瞬间的残留由 OnGameUpdate 重生检测统一清理，双保险）
                    if (p.owner < 0 || p.owner >= Main.maxPlayers
                        || !Main.player[p.owner].active || Main.player[p.owner].dead || Main.player[p.owner].ghost)
                        continue;
                    // 0 槽弹幕（星尘龙头 625/尾 628 等）不计入 —— 原版 slotsMinions 累加同样排除（Projectile.cs 15933）
                    if (p.minionSlots <= 0f)
                        continue;
                    slots += p.minionSlots;
                }

                // 边界修正：加上本次弹幕的槽位后才判超限（NewProjectile 包触发时本次弹幕尚未创建）
                if (slots + thisSlot <= MaxMinions)
                    return;

                // 已满 → 拦截本次召唤 + 审计 + 踢出（一次即踢）
                e.Handled = true;
                var account = plr.Account?.Name ?? "未登录";
                TShock.Log.ConsoleInfo($"[MinionLimit][审计] 玩家={plr.Name} 账号={account} IP={plr.IP} 召唤槽位={slots}+{thisSlot} 上限={MaxMinions} 弹幕类型={e.Type}");
                try
                {
                    plr.Kick($"召唤物数量异常 ({(slots + thisSlot):0.#}/{MaxMinions})", true);
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleError($"[MinionLimit] 踢出玩家失败: {ex.Message}");
                }
            }

            /// <summary>
            /// 获取弹幕类型占用的召唤槽位（0=非召唤物/0槽，&gt;0=召唤槽位），结果按类型缓存避免重复 SetDefaults。
            /// minion 且非 sentry 的弹幕才占用槽位；哨兵/宠物/坐骑返回 0。
            /// </summary>
            private static float GetMinionSlots(int type)
            {
                if (type < 0 || type >= _slotCache.Length)
                    return 0f;
                if (_slotCache[type] >= 0f)
                    return _slotCache[type];

                var p = new Projectile();
                p.SetDefaults(type);
                _slotCache[type] = (p.minion && !p.sentry) ? p.minionSlots : 0f;
                return _slotCache[type];
            }
        }

    }
}
