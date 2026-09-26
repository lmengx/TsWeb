using System.IO;
using System.IO.Streams;
using OTAPI;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;

namespace InvJudge;

[ApiVersion(2, 1)]
public class InvJudge : TerrariaPlugin
{
    public override string Author => "lmx12330";
    public override string Description => "完全体玩家背包判定：合成合法性校验（扣材/产量比例 + 微光可分解配方重点监控）";
    public override string Name => "InvJudge";
    public override Version Version => new(1, 0, 0, 0);

    private InvJudgeConfig _config = new();
    private bool _disposed;

    public InvJudge(Main game) : base(game) { }

    public override void Initialize()
    {
        _config = InvJudgeConfig.Load();

        CraftDetector.Initialize(_config);

        // 原始包观察：优先级 -1000 先于 TShock 与原版处理，保证账本读到的是变化前的旧值
        ServerApi.Hooks.NetGetData.Register(this, OnNetGetData, -1000);

        // 服务端 tick：周期性刷新空闲检测窗口
        ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);

        // 玩家离开：清理账本
        ServerApi.Hooks.ServerLeave.Register(this, OnServerLeave);

        // 管理命令
        Commands.ChatCommands.Add(new Command("invjudge.admin", HandleCommand, "invjudge", "背包判定"));

        CraftDetector.Audit($"插件加载: 启用={_config.Enabled}, 窗口={_config.DetectWindowMs}ms, 微光严格={_config.ShimmerStrictMode}");
        TShock.Log.ConsoleInfo(
            $"[InvJudge] 背包判定插件已加载 (启用={_config.Enabled}, 仅记录模式, 窗口={_config.DetectWindowMs}ms)");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            ServerApi.Hooks.NetGetData.Deregister(this, OnNetGetData);
            ServerApi.Hooks.GameUpdate.Deregister(this, OnGameUpdate);
            ServerApi.Hooks.ServerLeave.Deregister(this, OnServerLeave);
            Commands.ChatCommands.RemoveAll(c => c.CommandDelegate == HandleCommand);
            CraftDetector.Audit("插件卸载");
            TShock.Log.ConsoleInfo("[InvJudge] 背包判定插件已卸载");
        }
        base.Dispose(disposing);
    }

    // ===================================================================
    // 包观察
    // ===================================================================
    private void OnNetGetData(GetDataEventArgs e)
    {
        try
        {
            if (!_config.Enabled)
                return;

            // 懒构建配方索引：首个包到达时世界已加载完成
            if (!RecipeIndex.IsBuilt)
            {
                try
                {
                    RecipeIndex.Build();
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleError($"[InvJudge] 配方索引构建失败: {ex.Message}");
                }
            }

            switch (e.MsgID)
            {
                case PacketTypes.PlayerSlot:       // 5: 背包/银行/时装槽位变化
                    HandlePlayerSlot(e);
                    break;
                case PacketTypes.ChestItem:        // 32: 箱子槽位变化
                    HandleChestItem(e);
                    break;
            }
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[InvJudge] 包处理异常 ({e.MsgID}): {ex.Message}");
        }
    }

    private void HandlePlayerSlot(GetDataEventArgs e)
    {
        var player = TShock.Players[e.Msg.whoAmI];
        if (player == null || !player.Active)
            return;

        // 1.4.5.8 packet 5 (SyncEquipment):
        // byte plr, short slot, short stack, byte prefix, short type, byte flags
        using var ms = new MemoryStream(e.Msg.readBuffer, e.Index, Math.Max(e.Length - 1, 0));
        ms.ReadInt8();               // plr（服务端强制为发送者）
        short slot = ms.ReadInt16();
        short stack = ms.ReadInt16();
        ms.ReadInt8();               // prefix（本期不参与判定）
        short type = ms.ReadInt16();

        CraftDetector.OnPlayerSlot(player, slot, type, stack);
    }

    private void HandleChestItem(GetDataEventArgs e)
    {
        var player = TShock.Players[e.Msg.whoAmI];
        if (player == null || !player.Active)
            return;

        // packet 32 (ChestItem): short id, byte slot, short stacks, byte prefix, short type
        using var ms = new MemoryStream(e.Msg.readBuffer, e.Index, Math.Max(e.Length - 1, 0));
        short chestId = ms.ReadInt16();
        byte slot = ms.ReadInt8();
        short stacks = ms.ReadInt16();
        ms.ReadInt8();               // prefix
        short type = ms.ReadInt16();

        CraftDetector.OnChestItem(player, chestId, slot, type, stacks);
    }

    private void OnGameUpdate(EventArgs e)
    {
        CraftDetector.OnServerTick();
    }

    private void OnServerLeave(LeaveEventArgs e)
    {
        CraftDetector.OnPlayerLeave(e.Who);
    }

    // ===================================================================
    // 管理命令 /invjudge
    // ===================================================================
    private void HandleCommand(CommandArgs args)
    {
        if (args.Parameters.Count == 0)
        {
            ShowStatus(args);
            return;
        }

        switch (args.Parameters[0].ToLowerInvariant())
        {
            case "reload":
                _config = InvJudgeConfig.Load();
                CraftDetector.Initialize(_config);
                args.Player.SendSuccessMessage($"[InvJudge] 配置已重载 (启用={_config.Enabled}, 窗口={_config.DetectWindowMs}ms, 微光严格={_config.ShimmerStrictMode})");
                CraftDetector.Audit($"命令 reload by {args.Player.Name}");
                break;

            case "recipes":
                ShowRecipes(args);
                break;

            case "help":
                ShowHelp(args);
                break;

            default:
                ShowStatus(args);
                break;
        }
    }

    private void ShowStatus(CommandArgs args)
    {
        args.Player.SendInfoMessage("=== InvJudge 背包判定 ===");
        args.Player.SendInfoMessage($"状态: {(_config.Enabled ? "启用" : "禁用")} (仅记录模式)");
        args.Player.SendInfoMessage($"检测窗口: {_config.DetectWindowMs}ms | 微光严格模式: {(_config.ShimmerStrictMode ? "开" : "关")}");
        args.Player.SendInfoMessage($"配方索引: {(RecipeIndex.IsBuilt ? $"已构建 ({RecipeIndex.All.Count} 条, 微光可分解物品 {RecipeIndex.DecraftIndex.Count(kv => kv.Value >= 0)} 种)" : "未构建")}");
        args.Player.SendInfoMessage($"日志目录: {TShock.SavePath}/InvJudge/");
        args.Player.SendInfoMessage("用法: /invjudge help");
    }

    private void ShowRecipes(CommandArgs args)
    {
        if (args.Parameters.Count < 2 || !int.TryParse(args.Parameters[1], out int itemId))
        {
            args.Player.SendInfoMessage("用法: /invjudge recipes <物品ID>");
            return;
        }

        if (!RecipeIndex.IsBuilt)
        {
            args.Player.SendInfoMessage("配方索引尚未构建（等待世界加载）");
            return;
        }

        if (RecipeIndex.ByResult.TryGetValue(itemId, out var infos))
        {
            args.Player.SendInfoMessage($"=== 可产出 {RecipeIndex.ItemName(itemId)} 的配方 ({infos.Count}) ===");
            foreach (var info in infos)
                args.Player.SendInfoMessage($"  {info}");
        }
        else
        {
            args.Player.SendInfoMessage($"没有可产出 {RecipeIndex.ItemName(itemId)} 的配方");
        }

        if (RecipeIndex.DecraftIndex.TryGetValue(itemId, out int decraftIdx) && decraftIdx >= 0)
        {
            args.Player.SendInfoMessage($"→ 该物品可被微光分解 (配方[{decraftIdx}])");
        }
        else
        {
            args.Player.SendInfoMessage("→ 该物品不可被微光分解");
        }
    }

    private void ShowHelp(CommandArgs args)
    {
        args.Player.SendInfoMessage("=== InvJudge 命令 ===");
        args.Player.SendInfoMessage("/invjudge          - 查看状态");
        args.Player.SendInfoMessage("/invjudge reload   - 重载配置");
        args.Player.SendInfoMessage("/invjudge recipes <物品ID> - 查看配方与微光可分解信息");
    }
}
