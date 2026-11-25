using OpenWards.Sim.Commands;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Maps;

namespace OpenWards.Sim.Tests;

/// <summary>Buying parcels of land.</summary>
public class LandPurchaseTests
{
    [Fact]
    public void AParcelWithoutANeighbourListIsForSaleWhereverItLies()
    {
        var simulation = NewSimulation(10_000);

        Assert.False(simulation.IsParcelPurchasable(0, 0)); // the outside
        Assert.False(simulation.IsParcelPurchasable(1, 0)); // owned already
        Assert.True(simulation.IsParcelPurchasable(2, 0));
        Assert.True(simulation.IsParcelPurchasable(3, 0));
        Assert.True(simulation.IsParcelPurchasable(4, 0)); // walled off, touching nothing: the map does not count
        Assert.False(simulation.IsParcelPurchasable(5, 0)); // no such parcel
    }

    [Fact]
    public void AListedParcelIsForSaleOnlyToWhoeverOwnsOneOfItsNeighbours()
    {
        // Parcel 3 touches parcel 1 on the map, but its list names only parcel 2; parcel 4 touches nothing but lists 1.
        var simulation = NewSimulation(10_000, players: 2, neighbours: [new ParcelNeighbours(3, [2]), new ParcelNeighbours(4, [1, 2])]);

        Assert.False(simulation.IsParcelPurchasable(3, 0));
        Assert.True(simulation.IsParcelPurchasable(4, 0));
        Assert.False(simulation.IsParcelPurchasable(4, 1));
        Assert.True(simulation.IsParcelPurchasable(2, 1));

        simulation.SetParcelOwner(2, 2);
        Assert.False(simulation.IsParcelPurchasable(3, 0));
        Assert.True(simulation.IsParcelPurchasable(3, 1));
        Assert.True(simulation.IsParcelPurchasable(4, 1));
    }

    [Fact]
    public void BuyingAParcelCostsItsTilesAndAddsToTheValue()
    {
        var simulation = NewSimulation(10_000);
        var hospital = simulation.Hospitals[0];
        var value = hospital.Value;
        Assert.Equal(4 * 30, simulation.ParcelPrice(2));

        simulation.Schedule(0, new PurchaseParcelCommand(2));
        simulation.Step();

        Assert.Equal(1, simulation.Map.GetParcelOwner(2));
        Assert.Equal(10_000 - 120, hospital.Balance);
        Assert.Equal(value + 120, hospital.Value);
        Assert.Equal(new Transaction(0, TransactionKind.BuyLand, "2", -120, 9880), hospital.Transactions[0]);
        Assert.False(simulation.IsParcelPurchasable(2, 0));
    }

    [Fact]
    public void AParcelTooDearOrNotForSaleIsNotBought()
    {
        var simulation = NewSimulation(100);

        simulation.Schedule(0, new PurchaseParcelCommand(2));
        simulation.Schedule(0, new PurchaseParcelCommand(0));
        simulation.Step();

        Assert.Equal(0, simulation.Map.GetParcelOwner(2));
        Assert.Equal(0, simulation.Map.GetParcelOwner(0));
        Assert.Empty(simulation.Hospitals[0].Transactions);
    }

    [Fact]
    public void SavesKeepTheNeighbourLists()
    {
        var simulation = NewSimulation(10_000, neighbours: [new ParcelNeighbours(3, [2])]);
        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);

        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        Assert.False(restored.IsParcelPurchasable(3, 0));
        Assert.True(restored.IsParcelPurchasable(4, 0));
        Assert.Equal([2], restored.Rules.NeighboursOf(3));
    }

    [Fact]
    public void WithSeveralHospitalsAParcelGoesToAuctionAndTheHighestBidderGetsItWhenTheHammerFalls()
    {
        var simulation = NewSimulation(10_000, players: 2);
        Assert.Equal(12, LandAuction.StepFor(120));
        Assert.Equal(10, LandAuction.StepFor(60));

        // Player 1 wishes to buy parcel 2: the auction opens at its price without a bid of theirs, and time stands still.
        simulation.Schedule(0, new PurchaseParcelCommand(2));
        simulation.Step();
        var auction = Assert.IsType<LandAuction>(simulation.Auction);
        Assert.Equal((2, 0, 120L, 12L, -1, 0L, 0), (auction.Parcel, auction.Opener, auction.Price, auction.Step, auction.Leader, auction.Bid, auction.BidCount));
        Assert.Equal(LandAuction.HammerFrames - 1, auction.Hammer);
        Assert.Equal((0L, 1L, 1L), (simulation.Tick, simulation.Frame, simulation.AuctionFrames));

        // In one frame player 2 bids the price and player 1 a step more; then player 1 outbids themself.
        simulation.Schedule(1, new BidForParcelCommand(2), tick: 5);
        simulation.Schedule(0, new BidForParcelCommand(2), tick: 5);
        simulation.Schedule(0, new BidForParcelCommand(2), tick: 6);

        // On the bidding screen nothing else happens: a wish to buy another parcel is dropped.
        simulation.Schedule(1, new PurchaseParcelCommand(3), tick: 6);
        RunToFrame(simulation, 7);
        Assert.Equal((0, 144L, 156L, 3), (auction.Leader, auction.Bid, auction.Price, auction.BidCount));
        Assert.Equal((120L, 144L), (auction.BidOf(1), auction.BidOf(0)));
        Assert.Equal(LandAuction.HammerFrames - 1, auction.Hammer);

        // The hammer rests, then rises every ten frames and strikes three frames before it falls.
        Assert.Equal(0, auction.HammerHeight);
        RunToFrame(simulation, 7 + 29);
        Assert.Equal((70, 1), (auction.Hammer, auction.HammerHeight));
        RunToFrame(simulation, 7 + 96);
        Assert.Equal((3, true), (auction.Hammer, auction.HammerHasStruck));
        RunToFrame(simulation, 7 + 98);
        Assert.Same(auction, simulation.Auction);

        // The hammer falls: the leader pays their bid, the price and a step for each bid after the first.
        simulation.Step();
        Assert.Null(simulation.Auction);
        Assert.Equal(0L, simulation.Tick);
        Assert.Equal(1, simulation.Map.GetParcelOwner(2));
        Assert.Equal(0, simulation.Map.GetParcelOwner(3));
        Assert.Equal(10_000 - 120 - (2 * 12), simulation.Hospitals[0].Balance);
        Assert.Equal(10_000, simulation.Hospitals[1].Balance);
        Assert.Equal(new AuctionResult(2, 0, 144), simulation.LastAuction);

        // Then the game's time goes on.
        simulation.Step();
        Assert.Equal(1L, simulation.Tick);
    }

    [Fact]
    public void WithoutABidTheParcelStaysForSale()
    {
        var simulation = NewSimulation(10_000, players: 2);
        simulation.Schedule(1, new PurchaseParcelCommand(2));
        simulation.Step();
        for (var i = 1; i < LandAuction.HammerFrames; i++)
        {
            Assert.NotNull(simulation.Auction);
            simulation.Step();
        }

        Assert.Null(simulation.Auction);
        Assert.Equal((0L, (long)LandAuction.HammerFrames), (simulation.Tick, simulation.AuctionFrames));
        Assert.Equal(0, simulation.Map.GetParcelOwner(2));
        Assert.Equal(new AuctionResult(2, -1, 0), simulation.LastAuction);
        Assert.True(simulation.IsParcelPurchasable(2, 0));
        Assert.All(simulation.Hospitals, hospital => Assert.Equal(10_000, hospital.Balance));
    }

    [Fact]
    public void OpeningAnAuctionTakesMoreMoneyThanThePrice()
    {
        var even = NewSimulation(120, players: 2);
        even.Schedule(0, new PurchaseParcelCommand(2));
        even.Step();
        Assert.Null(even.Auction);
        Assert.Equal(1L, even.Tick);

        var more = NewSimulation(121, players: 2);
        more.Schedule(0, new PurchaseParcelCommand(2));
        more.Step();
        Assert.NotNull(more.Auction);
    }

    [Fact]
    public void ABidTooDearForTheBidderIsNotMadeAndAnAuctionIsSaved()
    {
        var simulation = NewSimulation(130, players: 2);
        simulation.Schedule(0, new PurchaseParcelCommand(2));
        simulation.Schedule(1, new BidForParcelCommand(2), tick: 1);
        simulation.Schedule(0, new BidForParcelCommand(2), tick: 1);
        RunToFrame(simulation, 2);

        // Player 1 had 130 for the opening but not the 132 of the next bid by the time it ran.
        Assert.Equal((1, 120L, 132L, 1), (simulation.Auction!.Leader, simulation.Auction.Bid, simulation.Auction.Price, simulation.Auction.BidCount));

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);
        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        var auction = restored.Auction!;
        Assert.Equal((2, 0, 120L, 12L, 132L, 1, 120L), (auction.Parcel, auction.Opener, auction.StartPrice, auction.Step, auction.Price, auction.Leader, auction.Bid));
        Assert.Equal((simulation.Auction.Hammer, 1, 2L), (auction.Hammer, auction.BidCount, restored.AuctionFrames));
    }

    private static void RunToFrame(Simulation simulation, long frame)
    {
        while (simulation.Frame < frame)
        {
            simulation.Step();
        }
    }

    /// <summary>
    /// Parcel 1 (the player's) in the middle, parcel 2 at the top touching the outside, parcel 3 enclosed by parcel 1,
    /// and parcel 4 cut off by walls (impassable tiles).
    /// </summary>
    private static Simulation NewSimulation(int startCash, int players = 1, ParcelNeighbours[]? neighbours = null)
    {
        var map = TestMaps.FromRows([.. Rows.Select(row => new string([.. row.Select(c => c == '#' ? '#' : '.')]))]);
        map.SetPlayerCount(players);
        map.ParcelCount = 5;
        map.SetParcelOwner(1, 1);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                ref var tile = ref map[x, y];
                tile.Parcel = Rows[y][x] switch
                {
                    '1' => 1,
                    '2' => 2,
                    '3' => 3,
                    '4' => 4,
                    _ => 0,
                };
            }
        }

        // Parcel 4's tiles are walled off.
        map[3, 4].Set(TileFlags.Passable, false);
        map[4, 4].Set(TileFlags.Passable, false);
        return new Simulation(map, seed: 1, rules: new LevelRules(startCash, [], [], landCostPerTile: 30, parcelNeighbours: neighbours));
    }

    private static readonly string[] Rows =
    [
        "......",
        ".2222.",
        ".1311.",
        ".1111.",
        ".1#44.",
        "...##.",
    ];
}
