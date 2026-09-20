using System;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;

namespace DiscoveryLog
{
    /// <summary>
    /// DiscoveryLog 独立插件入口。
    ///
    /// 目的：对重要探索物品的「自然获取事件」（挖掘图格 / 救助受困 NPC）挂钩并公屏记日志，
    /// 形成合规获取的证据链，供后续判定玩家背包中对应物品是否来源合理。
    ///
    /// 监控物品（写死）：
    ///   生命水晶  物品 29  图格 12  (Heart)                  —— 挖掘
    ///   龙蛋      物品 6142 图格 752 (PalworldChilletEgg)     —— 挖掘
    ///   捣蛋猫    物品 5663 受困 NPC 695 (PalworldCattivaDistressed)  —— 对话救助
    ///   火绒狐    物品 5664 受困 NPC 696 (PalworldFoxsparksDistressed) —— 对话救助
    ///
    /// 详细原理与使用见同目录 README.md。
    /// </summary>
    [ApiVersion(2, 1)]
    public class DiscoveryLogPlugin : TerrariaPlugin
    {
        public override string Author => "lmx12330";
        public override string Description => "探索物品发现日志：挖掘生命水晶/龙蛋、救助捣蛋猫/火绒狐时公屏记录发现事件";
        public override string Name => "DiscoveryLog";
        public override Version Version => new Version(1, 0, 0, 0);

        public DiscoveryLogPlugin(Main game) : base(game) { }

        public override void Initialize()
        {
            DiscoveryLogCore.Initialize();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DiscoveryLogCore.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
