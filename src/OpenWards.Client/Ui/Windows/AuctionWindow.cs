// The bidding screen of a land auction in a game of several hospitals (the original's network game, NETPLAY.TXT:
// "all players automatically go to the bidding screen ... make a bid by left clicking on the Tick button"), after the
// maintainer's specification of the game's logic ("Торги"). CorsixTH has no such screen.

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenWards.Client.Rendering;
using OpenWards.Sim.Commands;

namespace OpenWards.Client.Ui.Windows;

/// <summary>
/// The land auction under way: the plot, the bid standing and whose it is, the hammer, and the button that makes the next
/// bid. It opens for every player when an auction starts and closes only when the hammer falls: there is no leaving it.
/// </summary>
internal sealed class AuctionWindow : UiWindow
{
    /// <summary>The sound of the hammer striking: the game's own AUCTION.WAV.</summary>
    private const string HammerSound = "auction.wav";

    private int _hammerHeight;
    private bool _struck;

    public override string Title => UiText.Get("openwards.auction.caption", "Land auction");

    public override Vector2? DefaultPosition => new(0.5f, 0.3f);

    public override bool ClosesWithEsc => false;

    public override Point? CanvasSize => new(96, 48);

    public override bool IsDone(IWindowHost host) => host.Simulation.Auction is null;

    public override bool IsSameAs(UiWindow other) => other is AuctionWindow;

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var simulation = host.Simulation;
        if (simulation.Auction is not { } auction)
        {
            yield break;
        }

        // The hammer rises as the frames run out and strikes, with its sound, just before it falls.
        _hammerHeight = auction.HammerHeight;
        if (auction.HammerHasStruck && !_struck)
        {
            host.PlaySound(HammerSound);
        }

        _struck = auction.HammerHasStruck;

        yield return WindowCell.Line(auction.Leader < 0
            ? UiText.Format("openwards.auction.no_bids", "Plot %1%: no bids yet", auction.Parcel)
            : UiText.Format("openwards.auction.lot", "Plot %1%: bid $%2% by %3%", auction.Parcel, auction.Bid, ProgressReportScreen.HospitalName(host, auction.Leader)), highlight: true);

        // The money is checked again when the bid runs; a bid the player cannot pay only sounds a refusal.
        var player = host.Player;
        var canBid = player >= 0 && player < simulation.Hospitals.Count && simulation.Hospitals[player].Balance >= auction.Price;
        yield return WindowRow.Of(WindowCell.Button(UiText.Format("openwards.auction.bid", "Bid $%1%", auction.Price), () =>
        {
            if (canBid)
            {
                host.Submit(new BidForParcelCommand(auction.Parcel));
            }
            else
            {
                host.PlaySound("wrong2.wav");
            }
        }, highlight: canBid));
    }

    /// <summary>The auctioneer's block and hammer: the head lifts as the frames run out and comes down on the block.</summary>
    public override void DrawCanvas(SpriteBatch batch, Rectangle area, Texture2D pixel, TextRenderer text)
    {
        var wood = new Color(120, 72, 32);
        var dark = new Color(72, 40, 16);
        var block = new Rectangle(area.Center.X - 20, area.Bottom - 8, 40, 8);
        batch.Draw(pixel, block, dark);

        var lift = _struck ? 0 : _hammerHeight * 4;
        var head = new Rectangle(area.Center.X - 12, block.Top - 10 - lift, 24, 10);
        batch.Draw(pixel, head, wood);
        batch.Draw(pixel, new Rectangle(head.Right, head.Center.Y - 2, Math.Max(0, area.Right - head.Right - 8), 4), wood);
    }
}
