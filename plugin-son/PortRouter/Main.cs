using System;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;

namespace PortRouter
{
    /// <summary>
    /// PortRouter 独立插件入口。
    ///
    /// 目的：让游戏端口同时承载游戏协议与 HTTP(REST) ——
    ///   进槽前嗅探首字节判定协议：
    ///     - HTTP（GET/POST/... 以 ASCII 字母开头）→ 转发到 REST 端口
    ///     - 游戏协议（2 字节长度 + MessageID 0x01 握手）→ 原位走原版进槽流程（原始 IP 保留）
    ///
    /// 详细说明见同目录 README.md。
    /// </summary>
    [ApiVersion(2, 1)]
    public class PortRouterPlugin : TerrariaPlugin
    {
        public override string Author => "lmx12330";
        public override string Description => "端口协议路由：游戏端口同时接受 HTTP(REST) 与游戏流量，游戏流量原 IP 直连保留";
        public override string Name => "PortRouter";
        public override Version Version => new Version(1, 0, 0, 0);

        public PortRouterPlugin(Main game) : base(game)
        {
            // 必须在 TShock 之后处理 OTAPI.Hooks.Netplay.CreateTcpListener（TShock 默认 Order=1）。
            // 只有 Order=1000（最后订阅）才能保证监听器替换生效（ProxyProtocolSocket 同款约束）。
            this.Order = 1000;
        }

        public override void Initialize()
        {
            PortRouterCore.Initialize();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                PortRouterCore.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
