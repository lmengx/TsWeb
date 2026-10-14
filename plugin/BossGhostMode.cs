using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;

namespace TShockData;

/// <summary>
/// 进度锁 · BOSS 死亡幽灵模式
///
/// 目的：取代旧的"重进击杀"（BossLimitQuit 的织网+石化惩罚）——旧机制惩罚的是"在 BOSS 存活时退出"，
/// 玩家死亡后退出重进即可刷新复活时间，逃避效率比受罚更高。
///
/// 本模块把约束前移到"死亡"这一刻：
///   1. BOSS 存活时，打过该 BOSS 的玩家死亡 → 变幽灵（TPlayer.ghost = true）并开始复活倒计时；
///   2. 倒计时用"绝对到期时刻"（UTC ticks）计时，退出重进立即恢复幽灵态且**剩余时间一分不减**；
///   3. 倒计时结束 → 满血复活回出生点，恢复正常状态。
///
/// 与 TShock 原版的关系：TShock 自己的 RespawnTimer 会在 KillMe 时被赋值并逐秒递减、
/// 到 0 时调用 player.Spawn(ReviveFromDeath)。本模块**接管**这一路径：
///   - 死亡时把 TShock 的 RespawnTimer 压成一个极大值，阻止它自己提前把玩家复活；
///   - 由本模块的到期时刻单向驱动复活，并把 RespawnTimer 归零。
/// 这样既不用 detour，也不与 TShock 抢时钟。
/// </summary>
public static class BossGhostMode
{
    /// <summary>单个玩家的幽灵倒计时记录（以账号名为键，跨重进保留）</summary>
    private sealed class GhostRecord
    {
        /// <summary>绝对到期时刻（UTC）</summary>
        public DateTime ExpiresAtUtc;

        /// <summary>本次倒计时总时长（秒），仅用于展示与日志</summary>
        public int TotalSeconds;

        /// <summary>触发时锁定的 BOSS 名（展示用）</summary>
        public string BossName = "";

        /// <summary>已施加幽灵状态的连接；重进后必须对新连接重新同步</summary>
        public TSPlayer? AppliedPlayer;
    }

    /// <summary>复活同步绑定具体连接，等待出生响应及其后的正常状态包。</summary>
    private sealed class RevivalRecord
    {
        public TSPlayer Player = null!;
        public bool SpawnAcknowledged;
    }

    /// <summary>把一个玩家的 TShock RespawnTimer 压到极大值，阻止 TShock 自行复活</summary>
    private const int SuppressRespawnTimerSeconds = 86400;

    private static bool _initialized;
    private static TerrariaPlugin? _plugin;

    /// <summary>玩家名（大小写不敏感）→ 幽灵倒计时</summary>
    private static readonly Dictionary<string, GhostRecord> Ghosts =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<int, RevivalRecord> PendingRevivals = new();

    private static readonly object SyncRoot = new();

    /// <summary>周期性对账用的帧计数器</summary>
    private static int _tickCounter;

    /// <summary>上次广播剩余时间的秒数档位，用于"每秒只提示一次"</summary>
    private static readonly Dictionary<string, int> LastNotifiedSecond =
        new(StringComparer.OrdinalIgnoreCase);

    // ═══════════════════════════════════════════════
    // 生命周期
    // ═══════════════════════════════════════════════

    public static void Initialize(TerrariaPlugin plugin)
    {
        if (_initialized) return;
        _plugin = plugin;

        // 未开 SSC 时服务端主导的角色状态（ghost / active）不会被客户端接受，
        // 幽灵模式必然失效 —— 与其静默无效，不如直接关闭并明确告警。
        if (!TShock.ServerSideCharacterConfig.Settings.Enabled)
        {
            TShock.Log.ConsoleWarn("[BossGhostMode] 未开启 SSC（服务器端角色），BOSS 死亡幽灵模式已自动关闭");
            return;
        }

        // 仅幽灵期间的保护先于 Bouncer；首次正常死亡仍在其校验之后登记。
        GetDataHandlers.KillMe.Register(OnGhostKillMe, HandlerPriority.Highest);
        GetDataHandlers.KillMe.Register(OnKillMe);
        GetDataHandlers.PlayerDamage.Register(OnPlayerDamage, HandlerPriority.Highest);
        GetDataHandlers.PlayerHP.Register(OnPlayerHP, HandlerPriority.Highest);
        GetDataHandlers.PlayerUpdate.Register(OnPlayerUpdate, HandlerPriority.Highest);
        GetDataHandlers.PlayerSpawn.Register(OnPlayerSpawn);
        ServerApi.Hooks.GameUpdate.Register(plugin, OnGameUpdate);

        _initialized = true;
        TShock.Log.ConsoleInfo("[BossGhostMode] BOSS 死亡幽灵模式已初始化");
    }

    public static void Dispose()
    {
        if (!_initialized) return;

        // 卸载也必须走正式复活，不能留下被压制一天的复活计时器。
        ClearAll();

        GetDataHandlers.KillMe.UnRegister(OnKillMe);
        GetDataHandlers.KillMe.UnRegister(OnGhostKillMe);
        GetDataHandlers.PlayerDamage.UnRegister(OnPlayerDamage);
        GetDataHandlers.PlayerHP.UnRegister(OnPlayerHP);
        GetDataHandlers.PlayerUpdate.UnRegister(OnPlayerUpdate);
        GetDataHandlers.PlayerSpawn.UnRegister(OnPlayerSpawn);
        if (_plugin != null)
        {
            ServerApi.Hooks.GameUpdate.Deregister(_plugin, OnGameUpdate);
        }

        lock (SyncRoot)
        {
            Ghosts.Clear();
            PendingRevivals.Clear();
            LastNotifiedSecond.Clear();
        }

        _plugin = null;
        _initialized = false;
        TShock.Log.ConsoleInfo("[BossGhostMode] BOSS 死亡幽灵模式已卸载");
    }

    // ═══════════════════════════════════════════════
    // 事件处理
    // ═══════════════════════════════════════════════

    private static void OnGhostKillMe(object? sender, GetDataHandlers.KillMeEventArgs e)
    {
        if (e.Handled || !TryGetProtectedGhost(e.Player, out var record)) return;

        // 直接 return 会把死亡包交给 TShock 和原版，仍然计次、广播死亡甚至执行中核惩罚。
        e.Handled = true;
        ApplyGhost(e.Player, record);
    }

    private static void OnKillMe(object? sender, GetDataHandlers.KillMeEventArgs e)
    {
        if (e.Handled) return;
        // 防御性调用；正常事件链中 OnGhostKillMe 已先执行。
        OnGhostKillMe(sender, e);
        if (e.Handled) return;

        var player = e.Player;
        if (player == null || !player.RealPlayer || string.IsNullOrEmpty(player.Name)) return;
        // 已通过正常死亡校验的新死亡结束上一次复活过渡，不能干预下一轮生命周期。
        lock (SyncRoot)
        {
            if (PendingRevivals.TryGetValue(player.Index, out var revival) &&
                ReferenceEquals(revival.Player, player))
                PendingRevivals.Remove(player.Index);
        }
        if (!BossConfigManager.Config.QuitLimitEnabled || !IsReady(player)) return;

        // 判定条件一：此刻是否有 BOSS 存活
        var bossName = BossLimitQuit.GetLiveBossNameNear(player);
        if (bossName == null)
        {
            // 非 BOSS 场合：不干预，走 TShock 原版复活
            return;
        }

        // 判定条件二：该玩家是否真的打过 BOSS
        if (!BossLimitQuit.HasDamagedAnyBoss(player.Name))
        {
            return;
        }

        StartGhost(player, bossName);
    }

    private static void OnPlayerDamage(object? sender, GetDataHandlers.PlayerDamageEventArgs e)
    {
        if (e.Handled) return;

        // 117 包的 ID 是受伤者，Player 是发包者；PvP 时两者不同。
        var target = e.ID < TShock.Players.Length ? TShock.Players[e.ID] : null;
        if (TryGetProtectedGhost(target, out var targetRecord))
        {
            e.Handled = true;
            ApplyGhost(target!, targetRecord);
        }
        else if (TryGetProtectedGhost(e.Player, out var senderRecord))
        {
            // 幽灵也不能向其他玩家发起伤害。
            e.Handled = true;
            ApplyGhost(e.Player, senderRecord);
        }
    }

    private static void OnPlayerHP(object? sender, GetDataHandlers.PlayerHPEventArgs e)
    {
        if (e.Handled || !TryGetProtectedGhost(e.Player, out var record)) return;

        // 重进时上传的正生命值会让原版 16 包把 TPlayer.dead 改回 false。
        e.Handled = true;
        ApplyGhost(e.Player, record);
    }

    private static void OnPlayerUpdate(object? sender, GetDataHandlers.PlayerUpdateEventArgs e)
    {
        if (e.Handled) return;
        if (!TryGetProtectedGhost(e.Player, out var record))
        {
            HandleRevivalUpdate(e);
            return;
        }

        // 原版 13 包会直接采用客户端的 ghost 位；拒绝重进时尚未收到修正的普通状态包。
        // 不修改事件中的 MiscData1 副本，因为 GetDataHandlers 不会把它写回原始包。
        if (!e.MiscData1.IsGhosted)
        {
            e.Handled = true;
            ApplyGhost(e.Player, record);
        }
        else if (!ReferenceEquals(record.AppliedPlayer, e.Player) ||
                 !e.Player.TPlayer.ghost || !e.Player.TPlayer.dead || !e.Player.Dead)
        {
            ApplyGhost(e.Player, record);
        }
    }

    private static void HandleRevivalUpdate(GetDataHandlers.PlayerUpdateEventArgs e)
    {
        var player = e.Player;
        if (player == null || !IsReady(player)) return;

        lock (SyncRoot)
        {
            if (!PendingRevivals.TryGetValue(player.Index, out var revival) ||
                !ReferenceEquals(revival.Player, player)) return;

            if (!e.MiscData1.IsGhosted)
            {
                // 出生响应之前也可能存在旧的普通状态包，不能据此提前撤掉保护。
                if (revival.SpawnAcknowledged) PendingRevivals.Remove(player.Index);
                return;
            }
        }

        // 整包拒绝，避免旧 ghost 位和旧位置覆盖复活结果；只向本人重申当前状态，
        // 不重复 Spawn、补血或改变无敌时间，保留复活后的正常伤害结算。
        e.Handled = true;
        player.TPlayer.ghost = false;
        player.SendData(PacketTypes.PlayerUpdate, "", player.Index);
    }

    // 到期与正式复活之间也保持保护，直到 FinishGhost 删除记录。握手期间不改角色状态。
    private static bool TryGetProtectedGhost(TSPlayer? player, out GhostRecord record)
    {
        if (player != null && player.RealPlayer && !string.IsNullOrEmpty(player.Name) &&
            IsReady(player) && TryGetRecord(player.Name, out record)) return true;

        record = null!;
        return false;
    }

    private static void OnPlayerSpawn(object? sender, GetDataHandlers.SpawnEventArgs e)
    {
        if (e.Handled) return;
        var player = e.Player;
        if (player == null || !player.RealPlayer || string.IsNullOrEmpty(player.Name)) return;

        // TShock 在回调之前设置 FinishedHandshake，但仍需处理此包把 State 从 3 推进到 10。
        // 首次出生必须放行；否则重进会永远卡在接收玩家数据。幽灵状态由游戏更新补上。
        if (!IsReady(player)) return;
        lock (SyncRoot)
        {
            if (PendingRevivals.TryGetValue(player.Index, out var revival) &&
                ReferenceEquals(revival.Player, player))
                revival.SpawnAcknowledged = true;
        }
        if (!TryGetActive(player.Name, out var record)) return;

        ApplyGhost(player, record);
        e.Handled = true;
    }

    // 记录跨退出保留；所有状态变更在握手完成后的游戏更新中处理，不用后台任务追踪槽位。
    private static bool IsReady(TSPlayer player) =>
        player.ConnectionAlive && player.Active && player.FinishedHandshake && player.State >= 10;

    private static void OnGameUpdate(EventArgs args)
    {
        if (!_initialized) return;

        _tickCounter++;
        // 每 6 帧（约 0.1 秒）对账一次：足够及时，且不增加可感开销
        if (_tickCounter < 6) return;
        _tickCounter = 0;

        var now = DateTime.UtcNow;

        // 槽位重用不能把上一条连接的复活过渡带给新玩家，也不保留已断线连接。
        lock (SyncRoot)
        {
            foreach (var index in PendingRevivals.Keys.ToArray())
            {
                var revival = PendingRevivals[index];
                if (!revival.Player.ConnectionAlive || index >= TShock.Players.Length ||
                    !ReferenceEquals(TShock.Players[index], revival.Player))
                    PendingRevivals.Remove(index);
            }
        }

        // 先清掉"已到期但玩家不在线"的残留记录：
        // 这些记录没有人来触发 FinishGhost，留着会随重启前的时间无限堆积。
        PurgeExpiredOfflineRecords(now);

        foreach (var player in TShock.Players)
        {
            if (player == null || !player.RealPlayer || string.IsNullOrEmpty(player.Name)) continue;
            if (!IsReady(player)) continue;

            // 注意：这里必须拿"记录是否存在"，不能用 TryGetActive ——
            // 后者会把已过期的记录过滤掉，导致到期分支永远走不到、玩家被永久锁在幽灵态。
            if (!TryGetRecord(player.Name, out var record))
            {
                continue;
            }

            if (now >= record.ExpiresAtUtc)
            {
                FinishGhost(player, record);
                continue;
            }

            // 兜底：玩家可能在别处被复活（管理员操作、TShock 到点自行 Spawn 等），发现即压回幽灵态。
            if (!ReferenceEquals(record.AppliedPlayer, player) || !player.TPlayer.ghost ||
                !player.TPlayer.dead || !player.Dead || player.TPlayer.statLife != 0)
            {
                ApplyGhost(player, record);
            }
            // TShock 每秒给 RespawnTimer 减 1，长时间倒计时下需偶尔重申，避免它提前归零把玩家拉起。
            // 只在剩余不足一半压制值时重申，正常路径下每轮倒计时最多触发一次。
            else if (player.RespawnTimer < SuppressRespawnTimerSeconds / 2)
            {
                player.RespawnTimer = SuppressRespawnTimerSeconds;
            }

            NotifyRemaining(player, record, now);
        }
    }

    /// <summary>清理已到期但玩家不在线的记录（无人触发 FinishGhost，留着只会堆积）</summary>
    private static void PurgeExpiredOfflineRecords(DateTime now)
    {
        List<string> stale = new();

        lock (SyncRoot)
        {
            foreach (var kv in Ghosts)
            {
                if (now < kv.Value.ExpiresAtUtc) continue;

                bool online = TShock.Players.Any(p =>
                    p != null && p.RealPlayer &&
                    string.Equals(p.Name, kv.Key, StringComparison.OrdinalIgnoreCase));

                if (!online) stale.Add(kv.Key);
            }

            foreach (var name in stale)
            {
                Ghosts.Remove(name);
                LastNotifiedSecond.Remove(name);
            }
        }
    }

    // ═══════════════════════════════════════════════
    // 幽灵状态控制
    // ═══════════════════════════════════════════════

    private static void StartGhost(TSPlayer player, string bossName)
    {
        var config = BossConfigManager.Config;
        int totalSeconds = Math.Max(1, config.GhostRespawnSeconds);

        var record = new GhostRecord
        {
            ExpiresAtUtc = DateTime.UtcNow.AddSeconds(totalSeconds),
            TotalSeconds = totalSeconds,
            BossName = bossName,
        };

        lock (SyncRoot)
        {
            Ghosts[player.Name] = record;
        }

        // KillMe 回调之后 TShock 还会设置 Dead / RespawnTimer，原版还要结算死亡与掉落。
        // 这里仅登记，下一次游戏更新再施加幽灵，避免计时器被覆盖、死亡包覆盖幽灵状态。

        player.SendMessage(
            $"[c/FF5555:你已阵亡，将在 {totalSeconds} 秒后复活]",
            Microsoft.Xna.Framework.Color.OrangeRed);
        TShock.Log.ConsoleInfo(
            $"[BossGhostMode] {player.Name} 在 BOSS {bossName} 存活时死亡，进入幽灵模式 {totalSeconds}s");
    }

    /// <summary>把玩家置于幽灵态，并压制 TShock 自带的复活时钟</summary>
    private static void ApplyGhost(TSPlayer player, GhostRecord record)
    {
        if (player?.TPlayer == null) return;

        player.TPlayer.ghost = true;
        player.TPlayer.dead = true;
        player.TPlayer.statLife = 0;
        player.Dead = true;
        record.AppliedPlayer = player;

        // 延长 TShock 的复活时钟，避免它在我们的到期时刻之前把玩家拉起来。
        // TSPlayer.RespawnTimer 以秒为单位（内部写入 TPlayer.respawnTimer = 秒 * 60）。
        player.RespawnTimer = SuppressRespawnTimerSeconds;

        TSPlayer.All.SendData(PacketTypes.PlayerUpdate, "", player.Index);
        // PlayerUpdate 不含 dead；用零生命值把本人和旁观者都同步为已死亡的幽灵。
        TSPlayer.All.SendData(PacketTypes.PlayerHp, "", player.Index);
    }

    private static void FinishGhost(TSPlayer player, GhostRecord record)
    {
        lock (SyncRoot)
        {
            Ghosts.Remove(player.Name);
            LastNotifiedSecond.Remove(player.Name);
        }

        if (player?.TPlayer == null || !IsReady(player)) return;

        // 必须执行原版 Spawn：它会重置死亡时分离的头/身体/腿位置与旋转、镜头和无敌时间。
        // PlayerUpdate 只同步 ghost 等状态，不会执行这些复活清理。
        player.TPlayer.ghost = false;
        player.Dead = false;
        player.RespawnTimer = 0;
        player.TPlayer.Spawn(PlayerSpawnContext.ReviveFromDeath);

        // 满血复活
        player.TPlayer.statLife = player.TPlayer.statLifeMax2;
        player.TPlayer.statMana = player.TPlayer.statManaMax2;

        // 清除 TShock 侧可能残留的死亡结算
        BuffUtilsClear(player);

        lock (SyncRoot)
        {
            PendingRevivals[player.Index] = new RevivalRecord { Player = player };
        }

        // 本人与其他客户端都执行 Spawn；随后同步幽灵标志、满血/满蓝与减益状态。
        TSPlayer.All.SendData(PacketTypes.PlayerSpawn, "", player.Index, (int)PlayerSpawnContext.ReviveFromDeath);
        TSPlayer.All.SendData(PacketTypes.PlayerUpdate, "", player.Index);
        TSPlayer.All.SendData(PacketTypes.PlayerHp, "", player.Index);
        TSPlayer.All.SendData(PacketTypes.PlayerMana, "", player.Index);
        TSPlayer.All.SendData(PacketTypes.PlayerBuff, "", player.Index);

        player.SendMessage("[c/55FF55:复活时间结束，你已恢复]",
            Microsoft.Xna.Framework.Color.LightGreen);
        TShock.Log.ConsoleInfo($"[BossGhostMode] {player.Name} 幽灵倒计时结束，已满血复活");
    }

    /// <summary>清掉死亡残留的减益，避免复活后仍带死亡 debuff</summary>
    private static void BuffUtilsClear(TSPlayer player)
    {
        if (player?.TPlayer?.buffType == null) return;

        for (int i = 0; i < player.TPlayer.buffType.Length; i++)
        {
            int type = player.TPlayer.buffType[i];
            if (type <= 0) continue;

            // 只清与死亡强相关的残留减益（旧"重进击杀"惩罚留下的织网/石化也在此一并清掉），
            // 不动玩家自己身上的其它 buff（药水、召唤等）。
            if (type == BuffID.Webbed || type == BuffID.Stoned)
            {
                player.TPlayer.buffType[i] = 0;
                player.TPlayer.buffTime[i] = 0;
            }
        }
    }

    private static void NotifyRemaining(TSPlayer player, GhostRecord record, DateTime now)
    {
        int remain = (int)Math.Ceiling((record.ExpiresAtUtc - now).TotalSeconds);
        if (remain <= 0) return;

        lock (SyncRoot)
        {
            if (LastNotifiedSecond.TryGetValue(player.Name, out var last) && last == remain)
            {
                return;
            }
            LastNotifiedSecond[player.Name] = remain;
        }

        // 只按整秒提示，且只在剩余 10 秒内逐秒播报，避免刷屏
        if (remain <= 10 || remain % 10 == 0)
        {
            player.SendMessage($"[c/FFAA55:剩余 {remain} 秒复活]", 
                Microsoft.Xna.Framework.Color.Orange);
        }
    }

    // ═══════════════════════════════════════════════
    // 查询
    // ═══════════════════════════════════════════════

    /// <summary>记录是否存在（不论是否已到期）——到期判定由调用方负责</summary>
    private static bool TryGetRecord(string playerName, out GhostRecord record)
    {
        lock (SyncRoot)
        {
            if (Ghosts.TryGetValue(playerName, out var r))
            {
                record = r;
                return true;
            }
        }

        record = null!;
        return false;
    }

    /// <summary>是否存在尚未到期的记录（即"当前正处于幽灵倒计时中"）</summary>
    private static bool TryGetActive(string playerName, out GhostRecord record)
    {
        if (TryGetRecord(playerName, out var r) && DateTime.UtcNow < r.ExpiresAtUtc)
        {
            record = r;
            return true;
        }

        record = null!;
        return false;
    }

    /// <summary>供 REST / 命令查询当前处于幽灵倒计时的玩家</summary>
    public static List<object> GetGhostList()
    {
        var now = DateTime.UtcNow;
        var list = new List<object>();

        lock (SyncRoot)
        {
            foreach (var kv in Ghosts)
            {
                int remain = (int)Math.Ceiling((kv.Value.ExpiresAtUtc - now).TotalSeconds);
                list.Add(new
                {
                    player = kv.Key,
                    bossName = kv.Value.BossName,
                    totalSeconds = kv.Value.TotalSeconds,
                    remainSeconds = Math.Max(0, remain)
                });
            }
        }

        return list;
    }

    /// <summary>管理员手动解除某玩家的幽灵状态</summary>
    public static bool ReleasePlayer(string playerName)
    {
        GhostRecord? record;
        lock (SyncRoot)
        {
            if (!Ghosts.TryGetValue(playerName, out record)) return false;
        }

        var player = TShock.Players.FirstOrDefault(
            p => p != null && string.Equals(p.Name, playerName, StringComparison.OrdinalIgnoreCase));

        if (player != null)
        {
            FinishGhost(player, record);
            return true;
        }

        // 玩家不在线：直接移除记录（下次重进即按未死亡处理）
        lock (SyncRoot)
        {
            Ghosts.Remove(playerName);
            LastNotifiedSecond.Remove(playerName);
        }
        return true;
    }

    /// <summary>清空全部幽灵记录（配置关闭时调用）</summary>
    public static void ClearAll()
    {
        List<string> names;
        lock (SyncRoot)
        {
            names = Ghosts.Keys.ToList();
        }

        foreach (var name in names)
        {
            ReleasePlayer(name);
        }
    }
}
