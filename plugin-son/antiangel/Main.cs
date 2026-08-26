using System;
using System.Linq;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;

namespace AntiAngel
{
    /// <summary>
    /// antiangel —— 检测 TerraAngel 修改客户端并踢出。
    ///
    /// 来源：提取自参考源码 TShockPlugin-master/src/ServerTools 的 ModifyClientDetect 功能，
    /// 独立成插件并适配本服 1.4.5.7 + TShock 6.x 环境（原实现为 1.4.4.x 时代 IL 注入，
    /// 本插件改用 MonoMod RuntimeDetour 挂钩 MessageBuffer.GetData，运行时自适应签名）。
    ///
    /// 检测指纹（本地 TerraAngel 1.4.5.6 客户端源码实证）：
    ///   TerraAngel.Net.PacketBuilderExtensions.WritePlayerControlsPacketWithHiddenPresenceMessage()
    ///   在 PlayerControls(13) 包的 netCameraTarget 字段写入魔数 (-114514, -1919810)
    ///   （0xFFFE40AE / 0xFFE2B4BE），作为「隐藏存在感广播」特征。
    ///   本插件按 TA 发送布局解析该包并匹配指纹，命中即判定为 TerraAngel 客户端。
    /// </summary>
    [ApiVersion(2, 1)]
    public class AntiAngelPlugin : TerrariaPlugin
    {
        public override string Author => "lmx12330";
        public override string Description => "检测 TerraAngel 修改客户端并踢出（PlayerControls 隐藏存在感指纹）";
        public override string Name => "antiangel";
        public override Version Version => new Version(1, 0, 0, 0);

        public AntiAngelPlugin(Main game) : base(game) { }

        public override void Initialize()
        {
            Detector.Initialize(this);

            Commands.ChatCommands.Add(new Command("antiangel.admin", AntiAngelCommand, "antiangel")
            {
                HelpText = "查看/开关 TerraAngel 客户端检测（on/off/status/reload）"
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Commands.ChatCommands.RemoveAll(cmd =>
                    cmd.Names.Any(n => n.Equals("antiangel", StringComparison.OrdinalIgnoreCase)));
                Detector.Dispose();
            }
            base.Dispose(disposing);
        }

        private void AntiAngelCommand(CommandArgs args)
        {
            var sub = args.Parameters.Count > 0 ? args.Parameters[0].ToLowerInvariant() : "status";

            switch (sub)
            {
                case "on":
                    Detector.Config.Enabled = true;
                    Detector.SaveConfig();
                    args.Player.SendSuccessMessage("[antiangel] TerraAngel 检测已开启");
                    break;

                case "off":
                    Detector.Config.Enabled = false;
                    Detector.SaveConfig();
                    args.Player.SendSuccessMessage("[antiangel] TerraAngel 检测已关闭");
                    break;

                case "reload":
                    Detector.LoadConfig();
                    args.Player.SendSuccessMessage("[antiangel] 配置已重载");
                    break;

                default:
                    var s = Detector.Config;
                    args.Player.SendInfoMessage(
                        $"[antiangel] 状态: {(s.Enabled ? "开启" : "关闭")} | 命中后踢出: {(s.Kick ? "是" : "否")} | 全服广播: {(s.Broadcast ? "是" : "否")}");
                    args.Player.SendInfoMessage("[antiangel] 用法: /antiangel on|off|status|reload");
                    break;
            }
        }
    }
}
