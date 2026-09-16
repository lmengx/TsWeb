using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.Events;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;

namespace PeaceMode;

/// <summary>
/// 和平模式插件：开启后禁止世界中全部 NPC 生成与全部事件触发。
///
/// 覆盖范围：
///   - NPC：所有敌对 NPC（含 BOSS、事件怪）生成即被拦截；变形同样拦截。
///     城镇 NPC（商人等友方）默认保留，可由配置 IncludeTownNpcs 一并禁止。
///   - 事件：入侵、血月、日食、南瓜月、霜月、史莱姆雨、旧日军团、沙尘暴，
///     由每秒轮询强制清除，防止再次触发。
///   - 天气：下雨（可选）、陨石坠落（可选）。
/// </summary>
[ApiVersion(2, 1)]
public class PeaceModePlugin : TerrariaPlugin
{
    public override string Name => "PeaceMode";
    public override string Author => "lmx12330";
    public override string Description => "和平模式：禁止世界中全部 NPC 与事件生成";
    public override Version Version => new Version(1, 0, 0, 0);

    /// <summary>当前是否处于和平模式</summary>
    public static bool Active { get; private set; }

    private static Config _config = new();
    private int _updateTick;

    public PeaceModePlugin(Main game) : base(game) { }

    public override void Initialize()
    {
        _config = Config.Read();

        Commands.ChatCommands.Add(new Command("peacemode.admin", PeaceCommand, "peace", "和平"));

        ServerApi.Hooks.NpcSpawn.Register(this, OnNpcSpawn);
        ServerApi.Hooks.NpcTransform.Register(this, OnNpcTransform);
        ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);
        GeneralHooks.ReloadEvent += OnReload;

        if (_config.Enabled)
        {
            SetActive(true);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Commands.ChatCommands.RemoveAll(cmd => cmd.CommandDelegate == PeaceCommand);
            ServerApi.Hooks.NpcSpawn.Deregister(this, OnNpcSpawn);
            ServerApi.Hooks.NpcTransform.Deregister(this, OnNpcTransform);
            ServerApi.Hooks.GameUpdate.Deregister(this, OnGameUpdate);
            GeneralHooks.ReloadEvent -= OnReload;
        }
        base.Dispose(disposing);
    }

    #region 命令

    private void PeaceCommand(CommandArgs args)
    {
        if (args.Parameters.Count == 0)
        {
            args.Player.SendInfoMessage($"和平模式当前: {(Active ? "[c/32FF82:已开启]" : "[c/FF514A:已关闭]")}");
            args.Player.SendInfoMessage("用法: /peace on | off | status | reload");
            return;
        }

        switch (args.Parameters[0].ToLower())
        {
            case "on":
                SetActive(true);
                args.Player.SendSuccessMessage("和平模式已开启：禁止全部 NPC 与事件生成");
                break;

            case "off":
                SetActive(false);
                args.Player.SendSuccessMessage("和平模式已关闭");
                break;

            case "status":
                args.Player.SendInfoMessage($"和平模式当前: {(Active ? "[c/32FF82:已开启]" : "[c/FF514A:已关闭]")}");
                args.Player.SendInfoMessage($"  禁止事件: {(_config.BanEvents ? "是" : "否")}  禁止下雨: {(_config.BanRain ? "是" : "否")}  禁止陨石: {(_config.BanMeteor ? "是" : "否")}");
                args.Player.SendInfoMessage($"  城镇 NPC: {(_config.IncludeTownNpcs ? "一并禁止" : "保留")}");
                break;

            case "reload":
                Reload();
                args.Player.SendSuccessMessage("和平模式配置已重新加载");
                break;

            default:
                args.Player.SendErrorMessage("未知子命令。可用: on, off, status, reload");
                break;
        }
    }

    #endregion

    #region 开关

    /// <summary>切换和平模式开关</summary>
    private static void SetActive(bool value)
    {
        Active = value;
        if (value)
        {
            ClearEvents();
            if (_config.ClearExistingNpcsOnEnable)
            {
                ClearExistingNpcs();
            }
        }
        TShock.Utils.Broadcast(
            Active
                ? "[c/32FF82:【和平模式】已开启] 世界不再生成 NPC 与事件"
                : "[c/FF514A:【和平模式】已关闭] 世界恢复原状",
            Color.White);
    }

    /// <summary>重新加载配置，并按配置同步开关状态</summary>
    private void Reload()
    {
        _config = Config.Read();
        if (_config.Enabled != Active)
        {
            SetActive(_config.Enabled);
        }
    }

    private void OnReload(ReloadEventArgs args)
    {
        Reload();
        args.Player?.SendSuccessMessage("和平模式配置已重新加载");
    }

    #endregion

    #region NPC 拦截

    private void OnNpcSpawn(NpcSpawnEventArgs args)
    {
        if (!Active || args.Handled)
        {
            return;
        }

        var npc = Main.npc[args.NpcId];
        if (npc == null)
        {
            return;
        }

        // 城镇 NPC（友方）默认保留
        if (npc.townNPC && !_config.IncludeTownNpcs)
        {
            return;
        }

        args.Handled = true;
        npc.active = false;
        TSPlayer.All.SendData(PacketTypes.NpcUpdate, "", args.NpcId);
    }

    private void OnNpcTransform(NpcTransformationEventArgs args)
    {
        if (!Active || args.Handled)
        {
            return;
        }

        var npc = Main.npc[args.NpcId];
        if (npc == null)
        {
            return;
        }

        if (npc.townNPC && !_config.IncludeTownNpcs)
        {
            return;
        }

        npc.active = false;
        TSPlayer.All.SendData(PacketTypes.NpcUpdate, "", args.NpcId);
    }

    /// <summary>清空场上所有敌对 NPC（开启和平模式时调用）</summary>
    private static void ClearExistingNpcs()
    {
        var cleared = 0;
        for (var i = 0; i < Main.npc.Length; i++)
        {
            var npc = Main.npc[i];
            if (npc == null || !npc.active)
            {
                continue;
            }

            if (npc.townNPC && !_config.IncludeTownNpcs)
            {
                continue;
            }

            npc.active = false;
            npc.life = 0;
            TSPlayer.All.SendData(PacketTypes.NpcUpdate, "", i);
            cleared++;
        }

        if (cleared > 0)
        {
            TShock.Log.ConsoleInfo($"[PeaceMode] 已清除场上 {cleared} 个 NPC");
        }
    }

    #endregion

    #region 事件/天气清空

    private void OnGameUpdate(EventArgs args)
    {
        if (!Active)
        {
            return;
        }

        // 每秒轮询一次，强制清除事件状态，防止再次触发
        if (++_updateTick % 60 != 0)
        {
            return;
        }

        ClearEvents();
    }

    /// <summary>清除当前所有事件与天气状态（入侵/血月/日食/南瓜月/霜月/史莱姆雨/旧日军团/沙尘暴/雨/陨石）</summary>
    private static void ClearEvents()
    {
        var changed = false;

        if (_config.BanEvents)
        {
            if (Main.invasionType != 0 || Main.invasionSize != 0)
            {
                Main.invasionType = 0;
                Main.invasionSize = 0;
                Main.invasionDelay = 0;
                changed = true;
            }

            if (Main.bloodMoon)
            {
                Main.bloodMoon = false;
                changed = true;
            }

            if (Main.eclipse)
            {
                Main.eclipse = false;
                changed = true;
            }

            if (Main.pumpkinMoon)
            {
                Main.pumpkinMoon = false;
                changed = true;
            }

            if (Main.snowMoon)
            {
                Main.snowMoon = false;
                changed = true;
            }

            if (Main.slimeRain)
            {
                Main.StopSlimeRain();
                changed = true;
            }

            if (DD2Event.Ongoing)
            {
                DD2Event.StopInvasion();
                changed = true;
            }

            if (Sandstorm.Happening)
            {
                Sandstorm.StopSandstorm();
                changed = true;
            }
        }

        if (_config.BanRain && Main.raining)
        {
            Main.StopRain();
            changed = true;
        }

        if (_config.BanMeteor && WorldGen.spawnMeteor)
        {
            WorldGen.spawnMeteor = false;
            changed = true;
        }

        if (changed)
        {
            TSPlayer.All.SendData(PacketTypes.WorldInfo, "");
        }
    }

    #endregion
}
