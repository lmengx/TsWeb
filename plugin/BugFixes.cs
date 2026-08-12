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

namespace TShockData
{
    /// <summary>
    /// 修复 TShock 恶性 Bug 的通用模块
    /// 子模块：
    ///   LoginFix — 修复 UUID 变更导致无法进服的连接层 Bug
    ///   ChestFix — 修复宝箱数据包校验缺失漏洞
    ///   DualChestFix — 禁止玩家同时打开多个箱子（防双箱刷物品）
    /// </summary>
    public static class BugFixes
    {
        private static bool _isInitialized = false;

        public static void Initialize(TerrariaPlugin plugin)
        {
            if (_isInitialized)
                return;

            LoginFix.Initialize(plugin);
            ChestFix.Initialize(plugin);
            MinionLimit.Initialize(plugin);

            _isInitialized = true;
            TShock.Log.ConsoleInfo("[TSWeb] BugFixes 已加载");
        }

        public static void Dispose(TerrariaPlugin plugin)
        {
            if (!_isInitialized)
                return;

            LoginFix.Dispose(plugin);
            ChestFix.Dispose(plugin);
            MinionLimit.Dispose(plugin);

            _isInitialized = false;
        }

        // ==========================================================================
        // 子模块1: LoginFix — 在进服前校验已有账户的登录密码
        // ==========================================================================
        public static class LoginFix
        {
            private static readonly HashSet<string> _passwordPending = new();

            public static void Initialize(TerrariaPlugin plugin)
            {
                ServerApi.Hooks.NetGetData.Register(plugin, OnGetData, int.MaxValue);
            }

            public static void Dispose(TerrariaPlugin plugin)
            {
                ServerApi.Hooks.NetGetData.Deregister(plugin, OnGetData);
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

            public static void Initialize(TerrariaPlugin plugin)
            {
                GetDataHandlers.ChestItemChange += OnChestItemChange;
                ServerApi.Hooks.NetGetData.Register(plugin, OnNetGetData, -1000);
                Commands.ChatCommands.Add(new Command("chestfix.admin", ChestFixCommand, "chestfix", "cstf"));
            }

            public static void Dispose(TerrariaPlugin plugin)
            {
                GetDataHandlers.ChestItemChange -= OnChestItemChange;
                ServerApi.Hooks.NetGetData.Deregister(plugin, OnNetGetData);
                Commands.ChatCommands.RemoveAll(c => c.CommandDelegate == ChestFixCommand);
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

                    // 关闭箱子(-1) 与随行容器(-5..-2)：仅在不携带名称时合法
                    if ((id == -1 || (id < 0 && id >= -5)) && nameLen == 0)
                        return;

                    // 负 ID 携带名称 → 核心 case 33 访问 Main.chest[player.chest](默认-1) → 越界崩服
                    if (id < 0)
                    {
                        AuditAndKick(plr, "ChestOpen", "负ID携带名称", $"id={id} nameLen={nameLen}", raw);
                        e.Handled = true;
                        return;
                    }

                    if (id >= Main.chest.Length || Main.chest[id] == null)
                    {
                        AuditAndKick(plr, "ChestOpen", "无效宝箱ID", $"id={id}", raw);
                        e.Handled = true;
                        plr.SendData(PacketTypes.ChestOpen, "", -1);
                        return;
                    }
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
        // 检测依据：玩家名下的活跃召唤物弹幕（Projectile.minion && !sentry），
        // 哨兵/宠物/坐骑不计。超过上限即审计并踢出（一次即踢）。
        // ==========================================================================
        public static class MinionLimit
        {
            /// <summary>召唤物数量上限：超过即拦截创建并踢出</summary>
            private const int MaxMinions = 20;
            /// <summary>弹幕类型 → 是否为召唤物（0=未知, 1=召唤物, 2=非召唤物）</summary>
            private static readonly int[] _typeCache = new int[ProjectileID.Count];

            public static void Initialize(TerrariaPlugin plugin)
            {
                GetDataHandlers.NewProjectile.Register(OnNewProjectile);
                TShock.Log.ConsoleInfo($"[TSWeb] MinionLimit 已加载 (召唤物上限 {MaxMinions})");
            }

            public static void Dispose(TerrariaPlugin plugin)
            {
                GetDataHandlers.NewProjectile.UnRegister(OnNewProjectile);
            }

            private static void OnNewProjectile(object? sender, GetDataHandlers.NewProjectileEventArgs e)
            {
                if (e.Handled)
                    return;

                // 只有召唤物弹幕才计数（哨兵/宠物/坐骑不计）
                if (!IsMinionType(e.Type))
                    return;

                var plr = TShock.Players[e.Owner];
                if (plr == null || !plr.Active || !plr.IsLoggedIn)
                    return;

                var count = CountMinions(e.Owner);
                if (count < MaxMinions)
                    return;

                // 已满 → 拦截本次召唤 + 审计 + 踢出（一次即踢）
                e.Handled = true;
                var account = plr.Account?.Name ?? "未登录";
                TShock.Log.ConsoleInfo($"[MinionLimit][审计] 玩家={plr.Name} 账号={account} IP={plr.IP} 召唤物={count} 上限={MaxMinions} 弹幕类型={e.Type}");
                try
                {
                    plr.Kick($"召唤物数量异常 ({count}/{MaxMinions})", true);
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleError($"[MinionLimit] 踢出玩家失败: {ex.Message}");
                }
            }

            /// <summary>判断弹幕类型是否为召唤物（minion 且非哨兵），结果按类型缓存避免重复 SetDefaults</summary>
            private static bool IsMinionType(int type)
            {
                if (type < 0 || type >= _typeCache.Length)
                    return false;
                if (_typeCache[type] != 0)
                    return _typeCache[type] == 1;

                var p = new Projectile();
                p.SetDefaults(type);
                _typeCache[type] = (p.minion && !p.sentry) ? 1 : 2;
                return _typeCache[type] == 1;
            }

            /// <summary>统计玩家当前活跃的召唤物（minion 且非哨兵）数量</summary>
            private static int CountMinions(int whoAmI)
            {
                var count = 0;
                for (var i = 0; i < Main.projectile.Length; i++)
                {
                    var p = Main.projectile[i];
                    if (p.active && p.owner == whoAmI && p.minion && !p.sentry)
                        count++;
                }
                return count;
            }
        }

    }
}
