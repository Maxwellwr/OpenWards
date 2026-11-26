// Land auctions in a game of several hospitals, after the original's network game (NETPLAY.TXT, "Buying Plots": a player
// wishing to buy a plot starts the bidding, every player may bid, and when the hammer falls the plot goes to the
// highest bidder) and the maintainer's specification of the game's logic ("Участки и аукционы"). CorsixTH has no network
// game (its town_map.lua leaves auctions as a TODO), so none of this comes from it.

using System.Globalization;
using OpenWards.Sim.Serialization;

namespace OpenWards.Sim.Economy;

/// <summary>
/// The auction of a parcel. While it is under way the game's time stands still for everyone: each step of the simulation
/// is a frame of the bidding screen (<see cref="Simulation.Frame"/>) and the hammer counts them down. The first bid is the
/// parcel's price, each bid raises the next by a step fixed when the auction opens, and the hammer falls
/// <see cref="HammerFrames"/> frames after the last bid.
/// </summary>
public sealed class LandAuction
{
    /// <summary>Frames from the opening, or from the last bid, until the hammer falls.</summary>
    public const int HammerFrames = 100;

    /// <summary>The smallest step a bid goes up by.</summary>
    public const long MinimumStep = 10;

    /// <summary>The hammer rests until this many frames are left, then rises every ten frames.</summary>
    public const int HammerRestsAbove = 70;

    /// <summary>The frames left when the hammer strikes, with its sound.</summary>
    public const int HammerStrikes = 3;

    private readonly long[] _bids;

    private LandAuction(int parcel, int opener, long startPrice, long step, long price, int leader, int hammer, int bidCount, long[] bids)
    {
        Parcel = parcel;
        Opener = opener;
        StartPrice = startPrice;
        Step = step;
        Price = price;
        Leader = leader;
        Hammer = hammer;
        BidCount = bidCount;
        _bids = bids;
    }

    /// <summary>The parcel up for auction.</summary>
    public int Parcel { get; }

    /// <summary>The player (0-based) who opened the auction. Opening it is not a bid.</summary>
    public int Opener { get; }

    /// <summary>The parcel's price when the auction opened: the first bid.</summary>
    public long StartPrice { get; }

    /// <summary>What each bid adds to the next: a tenth of the price, at least <see cref="MinimumStep"/>.</summary>
    public long Step { get; }

    /// <summary>What the next bid is.</summary>
    public long Price { get; private set; }

    /// <summary>The player (0-based) whose bid stands, or -1 before anyone has bid.</summary>
    public int Leader { get; private set; }

    /// <summary>The leader's bid, what they pay when the hammer falls; 0 before anyone has bid.</summary>
    public long Bid => Leader < 0 ? 0 : _bids[Leader];

    /// <summary>Frames left until the hammer falls.</summary>
    public int Hammer { get; private set; }

    /// <summary>How many bids have been made.</summary>
    public int BidCount { get; private set; }

    /// <summary>
    /// How high the hammer is: 0 at rest while more than <see cref="HammerRestsAbove"/> frames are left, then one higher
    /// every ten frames.
    /// </summary>
    public int HammerHeight => Hammer > HammerRestsAbove ? 0 : ((HammerRestsAbove + 10 - Hammer) / 10);

    /// <summary>Whether the hammer has struck: <see cref="HammerStrikes"/> frames or fewer are left.</summary>
    public bool HammerHasStruck => Hammer <= HammerStrikes;

    /// <summary>The last bid a player made, or 0 if they have not bid.</summary>
    public long BidOf(int player) => player >= 0 && player < _bids.Length ? _bids[player] : 0;

    /// <summary>A tenth of the price, rounded down, but at least <see cref="MinimumStep"/>.</summary>
    public static long StepFor(long price) => Math.Max(MinimumStep, price / 10);

    /// <summary>
    /// A player who may buy a parcel (<see cref="Simulation.IsParcelPurchasable"/>) wishing to buy it with other hospitals in
    /// the game opens its auction, if none is under way and their balance is more than the price. Alone in the game they
    /// buy it at once (<see cref="Simulation.PurchaseParcel"/>).
    /// </summary>
    internal static void Start(Simulation simulation, int parcel, int player)
    {
        var price = simulation.ParcelPrice(parcel);
        if (simulation.Auction is not null || !simulation.CanAffordParcel(parcel, player))
        {
            return;
        }

        simulation.Auction = new LandAuction(parcel, player, price, StepFor(price), price, -1, HammerFrames, 0, new long[simulation.Hospitals.Count]);
    }

    /// <summary>
    /// A bid: any player with at least the next bid in the bank makes it, even the leader, and the hammer waits
    /// <see cref="HammerFrames"/> again. Only the opener had to be able to buy the parcel. The money is checked when the
    /// bid runs, not when it was asked for, so nobody can bid more than they have.
    /// </summary>
    internal static void PlaceBid(Simulation simulation, int parcel, int player)
    {
        if (simulation.Auction is not { } auction || auction.Parcel != parcel || player < 0 || player >= simulation.Hospitals.Count
            || simulation.Hospitals[player].Balance < auction.Price)
        {
            return;
        }

        auction._bids[player] = auction.Price;
        auction.Leader = player;
        auction.Price += auction.Step;
        auction.Hammer = HammerFrames;
        auction.BidCount++;
    }

    /// <summary>
    /// A frame of the bidding screen: the hammer counts down, and when it falls the auction ends. Without a bid the parcel
    /// stays for sale; otherwise the leader buys it for their bid and the others pay nothing.
    /// </summary>
    internal static void Frame(Simulation simulation)
    {
        if (simulation.Auction is not { } auction || --auction.Hammer > 0)
        {
            return;
        }

        simulation.Auction = null;
        if (auction.Leader >= 0 && simulation.Map.GetParcelOwner(auction.Parcel) == 0)
        {
            simulation.SetParcelOwner(auction.Parcel, auction.Leader + 1);
            simulation.Hospitals[auction.Leader].Spend(simulation.Hours, checked((int)auction.Bid), TransactionKind.BuyLand, auction.Parcel.ToString(CultureInfo.InvariantCulture));
            simulation.Hospitals[auction.Leader].NetMission.Happened(NetCriterion.BuyPlot);
        }

        simulation.LastAuction = new AuctionResult(auction.Parcel, auction.Leader, auction.Bid);
    }

    internal void Write(SimWriter writer)
    {
        writer.WriteInt32(Parcel);
        writer.WriteInt32(Opener);
        writer.WriteInt64(StartPrice);
        writer.WriteInt64(Step);
        writer.WriteInt64(Price);
        writer.WriteInt32(Leader);
        writer.WriteInt32(Hammer);
        writer.WriteInt32(BidCount);
        writer.WriteInt32(_bids.Length);
        foreach (var bid in _bids)
        {
            writer.WriteInt64(bid);
        }
    }

    internal static LandAuction Read(SimReader reader, int players)
    {
        if (reader.Version is not 0 and < 115)
        {
            // Before version 115 the opener's wish was the first bid and the hammer waited two days of the game: the bid
            // standing stays, the steps and the hammer are the new ones.
            var parcel = reader.ReadInt32();
            var bid = reader.ReadInt64();
            var leader = reader.ReadInt32();
            reader.ReadInt64();
            var start = reader.ReadInt64();
            var bids = new long[players];
            if (leader >= 0 && leader < players)
            {
                bids[leader] = bid;
            }

            var step = StepFor(start);
            return new LandAuction(parcel, leader, start, step, bid + step, leader, HammerFrames, 1, bids);
        }

        var readParcel = reader.ReadInt32();
        var opener = reader.ReadInt32();
        var startPrice = reader.ReadInt64();
        var readStep = reader.ReadInt64();
        var price = reader.ReadInt64();
        var readLeader = reader.ReadInt32();
        var hammer = reader.ReadInt32();
        var bidCount = reader.ReadInt32();
        var all = new long[reader.ReadCount(players)];
        for (var i = 0; i < all.Length; i++)
        {
            all[i] = reader.ReadInt64();
        }

        return new LandAuction(readParcel, opener, startPrice, readStep, price, readLeader, hammer, bidCount, all);
    }
}

/// <summary>How the last auction ended: the parcel, the highest bidder (-1 if nobody bid) and their bid.</summary>
public sealed record AuctionResult(int Parcel, int Winner, long Bid)
{
    internal void Write(SimWriter writer)
    {
        writer.WriteInt32(Parcel);
        writer.WriteInt32(Winner);
        writer.WriteInt64(Bid);
    }

    internal static AuctionResult Read(SimReader reader) => new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt64());
}
