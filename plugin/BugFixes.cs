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
using Microsoft.Xna.Framework;

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
            BossDamageReport.Initialize(plugin);

            _isInitialized = true;
            TShock.Log.ConsoleInfo("[TSWeb] BugFixes 已加载");
        }

        public static void Dispose(TerrariaPlugin plugin)
        {
            if (!_isInitialized)
                return;

            LoginFix.Dispose(plugin);
            ChestFix.Dispose(plugin);
            BossDamageReport.Dispose(plugin);


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
                    player.Kick(
                        "密码错误\n" +
                        "请输入角色密码。已登录设备可使用 /pwd 新密码 设置密码。\n" +
                        "如果没有可以登录的设备，请联系服务器管理员。\n" +
                        "如果这是你第一次进服，说明你的角色名已被占用，请更换。",
                        true, true
                    );
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
            private const string Ver = "1.0.0";
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
                    Block($"越界宝箱ID {e.ID}", e.Player);
                    e.Handled = true;
                    return;
                }

                if (Main.chest[e.ID] == null)
                {
                    Block($"空宝箱引用 ID={e.ID}", e.Player);
                    e.Handled = true;
                    return;
                }

                if (e.Slot < 0 || e.Slot >= Main.chest[e.ID].maxItems)
                {
                    Block($"越界槽位 slot={e.Slot}/max={Main.chest[e.ID].maxItems}", e.Player);
                    e.Handled = true;
                    return;
                }

                if (e.Type < 0 || e.Type >= ItemID.Count)
                {
                    Block($"无效物品ID type={e.Type}", e.Player);
                    e.Handled = true;
                    return;
                }

                if (e.Prefix < 0)
                {
                    Block($"无效词缀 prefix={e.Prefix}", e.Player);
                    e.Handled = true;
                    return;
                }

                if (e.Stacks < 0)
                {
                    Block($"负堆叠 stacks={e.Stacks}", e.Player);
                    e.Handled = true;
                    return;
                }
            }

            private static void OnNetGetData(GetDataEventArgs e)
            {
                if (e.MsgID == PacketTypes.ChestOpen)
                    HandleChestOpenPacket(e);
                else if (e.MsgID == PacketTypes.ChestGetContents)
                    HandleChestGetContentsPacket(e);
            }

            private static void HandleChestOpenPacket(GetDataEventArgs e)
            {
                var plr = TShock.Players[e.Msg.whoAmI];
                if (plr == null || !plr.Active)
                    return;

                using var ms = new MemoryStream(e.Msg.readBuffer, e.Index, Math.Max(e.Length - 1, 0));
                try
                {
                    var id = ms.ReadInt16();

                    if (id == -1)
                        return;

                    if (id < 0 && id >= -5)
                        return;

                    ms.ReadInt16();
                    ms.ReadInt16();

                    if (id < 0 || id >= Main.chest.Length || Main.chest[id] == null)
                    {
                        Block($"ChestOpen 无效宝箱ID={id}", plr);
                        e.Handled = true;
                        plr.SendData(PacketTypes.ChestOpen, "", -1);
                        return;
                    }
                }
                catch { }
            }

            private static void HandleChestGetContentsPacket(GetDataEventArgs e)
            {
                var plr = TShock.Players[e.Msg.whoAmI];
                if (plr == null || !plr.Active)
                    return;

                using var ms = new MemoryStream(e.Msg.readBuffer, e.Index, Math.Max(e.Length - 1, 0));
                try
                {
                    var x = ms.ReadInt16();
                    var y = ms.ReadInt16();

                    var chestId = Chest.FindChest(x, y);
                    if (chestId < 0 || chestId >= Main.chest.Length)
                    {
                        Block($"ChestGetContents 无效坐标 ({x},{y})", plr);
                        e.Handled = true;
                        return;
                    }

                    if (Main.chest[chestId] == null)
                    {
                        Block($"ChestGetContents 空宝箱 ({x},{y})", plr);
                        e.Handled = true;
                        return;
                    }
                }
                catch { }
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

            private static void Block(string reason, TSPlayer? player)
            {
                Interlocked.Increment(ref _blockedCount);
                if (player != null && player.Active && player.IsLoggedIn)
                {
                    BanPlayer(player, reason);
                }
                TShock.Log.ConsoleInfo($"[ChestFix] 拦截: {reason} | 玩家={player?.Name ?? "?"} [已封禁]");
            }

            private static void BanPlayer(TSPlayer player, string reason)
            {
                try
                {
                    var name = player.Name;
                    Commands.HandleCommand(TSPlayer.Server, $"/ban add \"acc:{name}\" \"发送恶意宝箱数据: {reason}\" -e");
                    Commands.HandleCommand(TSPlayer.Server, $"/kick \"{name}\" 发送恶意宝箱数据");
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleError($"[ChestFix] 封禁玩家失败: {ex.Message}");
                }
            }
        }

        // ==========================================================================
        // 子模块3: BossDamageReport — Boss击杀伤害报告
        // ==========================================================================
        public static class BossDamageReport
        {
            private static bool _initialized = false;
            private static TerrariaPlugin _plugin = null;

            // npc.whoAmI -> (playerName -> totalDamage)
            private static readonly Dictionary<int, Dictionary<string, int>> _tracking = new();

            // 最近一场 Boss 战的完整伤害排行（供 /dmg 命令使用）
            private static List<(string name, int dmg)> _lastScores = new();
            private static string _lastBossName = "";
            private static int _lastTotal = 0;

            public static void Initialize(TerrariaPlugin plugin)
            {
                if (_initialized) return;
                _initialized = true;
                _plugin = plugin;

                ServerApi.Hooks.NpcStrike.Register(plugin, OnNpcStrike);
                Commands.ChatCommands.Add(new Command(HandleDmgCommand, "dmg"));

                TShock.Log.ConsoleInfo("[BugFixes] BossDamageReport 已启用");
            }

            public static void Dispose(TerrariaPlugin plugin)
            {
                if (!_initialized) return;
                ServerApi.Hooks.NpcStrike.Deregister(_plugin, OnNpcStrike);
                _initialized = false;
                _plugin = null;
            }

            private static void OnNpcStrike(NpcStrikeEventArgs args)
            {
                var npc = args.Npc;
                if (npc == null || !npc.active || !npc.boss)
                    return;

                string playerName = args.Player?.name ?? "未知";
                int npcId = npc.whoAmI;
                int damage = args.Damage;

                if (!_tracking.ContainsKey(npcId))
                    _tracking[npcId] = new Dictionary<string, int>();

                if (!_tracking[npcId].ContainsKey(playerName))
                    _tracking[npcId][playerName] = 0;

                _tracking[npcId][playerName] += damage;

                if (npc.life <= damage)
                {
                    BroadcastTop5(npcId, npc);
                    _tracking.Remove(npcId);
                }
            }

            private static void BroadcastTop5(int npcId, NPC npc)
            {
                if (!_tracking.ContainsKey(npcId) || _tracking[npcId].Count == 0)
                    return;

                var sorted = _tracking[npcId]
                    .OrderByDescending(kv => kv.Value)
                    .ToList();

                int totalDamage = sorted.Sum(kv => kv.Value);

                // 保存到最近记录供 /dmg 命令使用
                _lastBossName = npc.FullName;
                _lastScores = sorted.Select(kv => (kv.Key, kv.Value)).ToList();
                _lastTotal = totalDamage;

                TSPlayer.All.SendMessage(
                    $"[Boss] {npc.FullName} 已被击败！总伤害: {totalDamage}",
                    Color.MediumSpringGreen);

                var top5 = sorted.Take(5).ToList();
                for (int i = 0; i < top5.Count; i++)
                {
                    int pct = totalDamage > 0 ? (int)(top5[i].Value * 100L / totalDamage) : 0;
                    TSPlayer.All.SendMessage(
                        $"  {i + 1}. {top5[i].Key} — {top5[i].Value} ({pct}%)",
                        Color.MediumSpringGreen);
                }

                TSPlayer.All.SendInfoMessage("输入 /dmg 查看全部伤害排名");
            }

            public static void HandleDmgCommand(CommandArgs args)
            {
                if (_lastScores.Count == 0)
                {
                    args.Player.SendErrorMessage("暂无最近的 Boss 战斗数据");
                    return;
                }

                args.Player.SendSuccessMessage($"=== {_lastBossName} 伤害排行 (总计: {_lastTotal}) ===");
                for (int i = 0; i < _lastScores.Count; i++)
                {
                    var s = _lastScores[i];
                    int pct = _lastTotal > 0 ? (int)(s.dmg * 100L / _lastTotal) : 0;
                    args.Player.SendMessage(
                        $"  {i + 1}. {s.name} — {s.dmg} ({pct}%)",
                        Color.MediumSpringGreen);
                }
            }
        }

    }
}
