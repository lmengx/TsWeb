using Terraria;

namespace InvJudge;

/// <summary>
/// 槽位状态账本：按（玩家 / 箱子）维护服务端视角的槽位内容，用于计算每次背包/箱子变化的 delta。
/// 网络槽位 id 直接作 key（主背包 0-58 与网络槽一致；银行/时装槽按网络槽区间，聚合时按物品类型归并即可）。
/// </summary>
public static class InvLedger
{
    /// <summary>玩家索引 → 网络槽位 → (物品类型, 堆叠)</summary>
    private static readonly Dictionary<int, Dictionary<int, SlotState>> _players = new();

    /// <summary>箱子 id → 槽位 → (物品类型, 堆叠)</summary>
    private static readonly Dictionary<int, Dictionary<int, SlotState>> _chests = new();

    public readonly struct SlotState
    {
        public readonly int Type;
        public readonly int Stack;

        public SlotState(int type, int stack)
        {
            Type = type;
            Stack = stack;
        }

        public static readonly SlotState Empty = new(0, 0);
    }

    /// <summary>
    /// 应用一次玩家槽位变化（packet 5），返回该槽位的变化量（delta 按物品类型：负=减少/消耗，正=增加/产出）。
    /// 首次见到某槽位时视为基线（delta=0），避免把进服同步误判为操作。
    /// </summary>
    public static void ApplyPlayerSlot(int playerIndex, int networkSlot, int type, int stack,
        Dictionary<int, int> deltaAccum)
    {
        if (!_players.TryGetValue(playerIndex, out var slots))
        {
            slots = new Dictionary<int, SlotState>();
            _players[playerIndex] = slots;
        }

        if (!slots.TryGetValue(networkSlot, out var old))
        {
            // 基线：以服务端当前实际状态为准（若可读），否则视为空
            old = ReadServerPlayerSlot(playerIndex, networkSlot);
            slots[networkSlot] = old;
        }

        var newState = new SlotState(type, stack);
        // 净变化（按物品类型累计；跨类型变化按各自类型记）
        if (old.Type != newState.Type || old.Stack != newState.Stack)
        {
            if (old.Type > 0 && old.Stack > 0)
                AddDelta(deltaAccum, old.Type, -old.Stack);
            if (newState.Type > 0 && newState.Stack > 0)
                AddDelta(deltaAccum, newState.Type, newState.Stack);
        }

        slots[networkSlot] = newState;
    }

    /// <summary>
    /// 应用一次箱子槽位变化（packet 32）。
    /// </summary>
    public static void ApplyChestSlot(int chestId, int slot, int type, int stack,
        Dictionary<int, int> deltaAccum)
    {
        if (!_chests.TryGetValue(chestId, out var slots))
        {
            slots = new Dictionary<int, SlotState>();
            _chests[chestId] = slots;
        }

        if (!slots.TryGetValue(slot, out var old))
        {
            old = ReadServerChestSlot(chestId, slot);
            slots[slot] = old;
        }

        var newState = new SlotState(type, stack);
        if (old.Type != newState.Type || old.Stack != newState.Stack)
        {
            if (old.Type > 0 && old.Stack > 0)
                AddDelta(deltaAccum, old.Type, -old.Stack);
            if (newState.Type > 0 && newState.Stack > 0)
                AddDelta(deltaAccum, newState.Type, newState.Stack);
        }

        slots[slot] = newState;
    }

    /// <summary>
    /// 玩家离开/换角色时清理账本。
    /// </summary>
    public static void RemovePlayer(int playerIndex)
    {
        _players.Remove(playerIndex);
    }

    public static void ResetAll()
    {
        _players.Clear();
        _chests.Clear();
    }

    private static void AddDelta(Dictionary<int, int> delta, int type, int amount)
    {
        delta.TryGetValue(type, out int cur);
        delta[type] = cur + amount;
    }

    /// <summary>
    /// 从服务器内存读取玩家槽位当前状态（网络槽 → 内部槽，仅主背包 0-58 与网络一致；其余返回空，走基线逻辑）。
    /// </summary>
    private static SlotState ReadServerPlayerSlot(int playerIndex, int networkSlot)
    {
        try
        {
            if (networkSlot < 0 || networkSlot > 58 || playerIndex < 0 || playerIndex >= Main.maxPlayers)
                return SlotState.Empty;

            var p = Main.player[playerIndex];
            if (p == null)
                return SlotState.Empty;

            var item = p.inventory[networkSlot];
            if (item == null)
                return SlotState.Empty;
            return new SlotState(item.type, item.stack);
        }
        catch
        {
            return SlotState.Empty;
        }
    }

    private static SlotState ReadServerChestSlot(int chestId, int slot)
    {
        try
        {
            if (chestId < 0 || chestId >= Main.chest.Length || Main.chest[chestId] == null)
                return SlotState.Empty;
            var chest = Main.chest[chestId];
            if (slot < 0 || slot >= chest.item.Length || chest.item[slot] == null)
                return SlotState.Empty;
            var item = chest.item[slot];
            return new SlotState(item.type, item.stack);
        }
        catch
        {
            return SlotState.Empty;
        }
    }
}
