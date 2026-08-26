using System;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;

namespace ConnectionGuard
{
    /// <summary>
    /// ConnectionGuard 独立插件入口。
    ///
    /// 目的：在 1.4.5.7 服务器上回归 1.4.5.6 的「挂起连接可恢复 + 监听自愈」健壮性，
    /// 并额外提供连接洪泛限流，根治 tcping/连接风暴导致的服务器假死。
    ///
    /// 详细根因与方案见同目录 README.md。
    /// </summary>
    [ApiVersion(2, 1)]
    public class ConnectionGuardPlugin : TerrariaPlugin
    {
        public override string Author => "lmx12330";
        public override string Description => "回归 1.4.5.6 监听健壮性：修复 tcping 高并发假死（源头改写 LinuxTcpSocket + 连接限流 + 挂起清理）";
        public override string Name => "ConnectionGuard";
        public override Version Version => new Version(1, 0, 0, 0);

        public ConnectionGuardPlugin(Main game) : base(game) { }

        public override void Initialize()
        {
            GuardCore.Initialize();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                GuardCore.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
