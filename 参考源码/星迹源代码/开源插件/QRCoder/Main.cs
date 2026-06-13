using TShockAPI;
using Terraria;
using TerrariaApi.Server;
using Net.Codecrete.QrCodeGenerator;
using Rests;

namespace QRCoder
{
	[ApiVersion(2, 1)]
	public class QRCoder : TerrariaPlugin
	{
		public override string Author => "Jonesn";
		public override string Description => "生成二维码";
		public override string Name => "QRCoder";
		public override Version Version => new Version(1, 0, 0, 0);
		public QRCoder(Main game) : base(game) { }
		public override void Initialize()
		{
			TShockAPI.Commands.ChatCommands.Add(new Command("qr.add", QREncoder, "qr"));
			TShock.RestApi.Register(new SecureRestCommand("/tool/qrcoder", QRtest, "tool.rest.qrcoder"));
		}
		protected override void Dispose(bool Disposing)
		{
			if (Disposing)
			{
			}
			base.Dispose(Disposing);
		}
		private object QRtest(RestRequestArgs args)
		{
			return new RestObject();
		}
			private void QREncoder(CommandArgs args)
		{
			TSPlayer player = args.Player;
			var segments = QrSegment.MakeSegments(args.Parameters[0]);
			var qr = QrCode.EncodeSegments(segments, QrCode.Ecc.Low, 5, 5, 2, false);
			for (int y = 0; y < qr.Size; y++)
			{
				for (int x = 0; x < qr.Size; x++)
				{
					int px = player.TileX + x-20;
					int py = player.TileY + y-40;
					Main.tile[px, py].wall = 155;
					if (qr.GetModule(x, y))
					{
						Main.tile[px, py].active(true);
						Main.tile[px, py].type=369;
						Main.tile[px, py].color((byte)25);
					}
					TSPlayer.All.SendTileSquareCentered(px, py, 1);
				}
			}
		}
	}
}