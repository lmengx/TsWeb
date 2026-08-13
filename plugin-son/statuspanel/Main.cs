using TShockAPI;
using Terraria;
using TerrariaApi.Server;

namespace StatusPanel
{
	/// <summary>
	/// 服务器信息面板：持续向客户端发送 9 号 StatusText 包，在客户端固定屏幕位置显示持久文本框。
	/// 
	/// 显示两行：
	///   [i:3525][c/4DABF7:建筑服]      —— 服务器名（图标 + 整体单色）
	///   在线人数：12人                  —— 实时刷新
	/// 
	/// 排版要点（抓包实证）：每行行尾补大量空格把文本块撑宽 → 文本块中心被客户端固定锚点
	/// 强制居中，可视文字随之落在屏幕中上部（"玩家正上方"视觉效果）。
	/// </summary>
	[ApiVersion(2, 1)]
	public class StatusPanelPlugin : TerrariaPlugin
	{
		public override string Name => "StatusPanel";
		public override string Author => "lmx12330";
		public override string Description => "客户端屏幕显示服务器信息面板（服务器名/在线人数）";
		public override Version Version => new Version(1, 0, 0, 0);

		// ═══════════ 可配置项 ═══════════
		/// <summary>服务器名行（图标 + 颜色 + 名字，写死）</summary>
		private const string ServerLine = "[i:757][c/f15642:开荒服]";

		/// <summary>
		/// 行尾补空格数：把文本块撑宽，使可视文字落在屏幕中上部。
		/// 客户端锚点固定（x 中心 ≈ 628 + (屏宽-800)），文本块越宽 → 起点越靠左。
		/// 60 ≈ 参照抓包服务器；屏幕越宽需要越大，太大文本块会偏出左屏。
		/// </summary>
		private const int SpacerWidth = 60;
		// ═══════════════════════════════

		public StatusPanelPlugin(Main game) : base(game) { }

		public override void Initialize()
		{
			ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);
			ServerApi.Hooks.ServerJoin.Register(this, OnServerJoin);
		}

		private void OnGameUpdate(EventArgs args)
		{
			string text = BuildPanelText(GetOnlineCount());
			foreach (var p in TShock.Players)
			{
				if (p != null && p.Active)
				{
					// 9 号 StatusText：0x1f = 隐藏百分比 + 带阴影
					p.SendData(PacketTypes.Status, text, 0, 0x1f);
				}
			}
		}

		private void OnServerJoin(JoinEventArgs args)
		{
			var p = TShock.Players[args.Who];
			if (p != null)
				p.SendData(PacketTypes.Status, BuildPanelText(GetOnlineCount()), 0, 0x1f);
		}

		private static int GetOnlineCount()
		{
			int count = 0;
			foreach (var p in TShock.Players)
			{
				if (p != null && p.Active)
					count++;
			}
			return count;
		}

		/// <summary>两行面板：服务器名 / 在线人数</summary>
		private static string BuildPanelText(int onlineCount)
		{
			string spacer = new(' ', SpacerWidth);
			return
				ServerLine + spacer + "\n" +
				"在线人数：" + onlineCount + "人" + spacer;
		}

		protected override void Dispose(bool Disposing)
		{
			if (Disposing)
			{
				ServerApi.Hooks.GameUpdate.Deregister(this, OnGameUpdate);
				ServerApi.Hooks.ServerJoin.Deregister(this, OnServerJoin);
				// 卸载时清空所有玩家屏幕文本
				foreach (var p in TShock.Players)
				{
					if (p != null && p.Active)
						p.SendData(PacketTypes.Status, "", 0, 0x1f);
				}
			}
			base.Dispose(Disposing);
		}
	}
}
