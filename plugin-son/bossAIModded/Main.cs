using bossAIModded.BossMods;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;

namespace bossAIModded;

[ApiVersion(2, 1)]
public class BossAIModded : TerrariaPlugin
{
    public override string Name => "bossAIModded";
    public override string Author => "TSWeb";
    public override string Description => "Fargo Souls(Eternity) Boss 魔改的可迁移子集 → 原版客户端可见形态。首个实现：史莱姆王(KingSlime)。";
    public override Version Version => new(1, 0, 0);

    /// <summary>全局开关（/bossai 切换）。</summary>
    public static bool ModEnabled = true;

    /// <summary>路由池：npc.whoAmI -> 魔改实例。</summary>
    internal static readonly Dictionary<int, BossAIModBase> ActiveMods = new();

    private Command? _cmd;

    public BossAIModded(Main game) : base(game) { }

    public override void Initialize()
    {
        ServerApi.Hooks.NpcAIUpdate.Register(this, OnNpcAiUpdate);
        ServerApi.Hooks.NpcStrike.Register(this, OnNpcStrike);
        ServerApi.Hooks.NpcKilled.Register(this, OnNpcKilled);

        // 玩家受伤(134 PlayerHurtV2)上报 → 路由到各 Boss 实例（己方弹幕命中才施加 debuff；本体接触由实例每 tick 碰撞箱相交判定）
        GetDataHandlers.PlayerDamage.Register(OnPlayerDamageV2);

        _cmd = new Command("bossaimod.admin", Toggle, "bossai")
        {
            HelpText = "切换 bossAIModded 全局开关（当前：史莱姆王 Eternity-lite）"
        };
        Commands.ChatCommands.Add(_cmd);

        TShock.Log.ConsoleInfo("[bossAIModded] loaded，/bossai 切换，默认开启。");
    }

    protected override void Dispose(bool Disposing)
    {
        if (Disposing)
        {
            ServerApi.Hooks.NpcAIUpdate.Deregister(this, OnNpcAiUpdate);
            ServerApi.Hooks.NpcStrike.Deregister(this, OnNpcStrike);
            ServerApi.Hooks.NpcKilled.Deregister(this, OnNpcKilled);
            GetDataHandlers.PlayerDamage.UnRegister(OnPlayerDamageV2);
            if (_cmd != null)
            {
                Commands.ChatCommands.Remove(_cmd);
                _cmd = null;
            }
            ActiveMods.Clear();
        }
        base.Dispose(Disposing);
    }

    private void Toggle(CommandArgs args)
    {
        ModEnabled = !ModEnabled;
        args.Player.SendInfoMessage($"[bossAIModded] 已{(ModEnabled ? "启用" : "禁用")}。{(ModEnabled ? "下次生成的史莱姆王将带强化 AI。" : "")}");
    }

    private void OnNpcAiUpdate(NpcAiUpdateEventArgs args)
    {
        var npc = args.Npc;
        if (npc == null || !npc.active)
        {
            if (npc != null) ActiveMods.Remove(npc.whoAmI);
            return;
        }
        if (!ModEnabled)
        {
            ActiveMods.Remove(npc.whoAmI);
            return;
        }
        var mod = GetOrCreate(npc);
        if (mod == null)
        {
            ActiveMods.Remove(npc.whoAmI);
            return;
        }
        try
        {
            mod.Tick(npc);
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[bossAIModded] Tick 异常 (npc {npc.type}@{npc.whoAmI}): {ex}");
            ActiveMods.Remove(npc.whoAmI);
        }
    }

    private void OnNpcStrike(NpcStrikeEventArgs args)
    {
        if (!ModEnabled) return;
        var npc = args.Npc;
        if (npc == null || !npc.active) return;
        if (!ActiveMods.TryGetValue(npc.whoAmI, out var mod)) return;
        try
        {
            mod.OnStrike(npc, args);
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[bossAIModded] OnStrike 异常: {ex}");
        }
    }

    private void OnNpcKilled(NpcKilledEventArgs args)
    {
        var npc = args.npc;
        if (npc == null) return;
        if (ActiveMods.Remove(npc.whoAmI, out var mod))
        {
            try
            {
                mod.OnKilled(npc);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[bossAIModded] OnKilled 异常: {ex}");
            }
        }
    }

    private void OnPlayerDamageV2(object sender, GetDataHandlers.PlayerDamageEventArgs e)
    {
        if (!ModEnabled) return;
        var who = e.ID;
        if (who < 0 || who >= Main.maxPlayers) return;
        try
        {
            // 路由到所有在场魔改 Boss 实例：134 上报对应己方弹幕命中（本体接触由实例每 tick 碰撞箱相交判定）
            foreach (var mod in ActiveMods.Values)
            {
                switch (mod)
                {
                    case KingSlimeEternity ks:
                        ks.OnPlayerDamage(who, e.PlayerDeathReason);
                        break;
                    case EyeOfCthulhu eoc:
                        eoc.OnPlayerDamage(who, e.PlayerDeathReason);
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[bossAIModded] OnPlayerDamage 异常: {ex}");
        }
    }

    private static BossAIModBase? GetOrCreate(NPC npc)
    {
        if (ActiveMods.TryGetValue(npc.whoAmI, out var existing))
        {
            return existing;
        }
        BossAIModBase? mod = npc.type switch
        {
            Terraria.ID.NPCID.KingSlime => new KingSlimeEternity(),
            Terraria.ID.NPCID.EyeofCthulhu => new EyeOfCthulhu(),
            _ => null,
        };
        if (mod != null)
        {
            ActiveMods[npc.whoAmI] = mod;
        }
        return mod;
    }
}
