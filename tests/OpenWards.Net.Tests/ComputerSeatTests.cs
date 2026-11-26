using OpenWards.Net.Lockstep;
using OpenWards.Net.Transport;
using OpenWards.Sim;
using OpenWards.Sim.Commands;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Maps;

namespace OpenWards.Net.Tests;

/// <summary>A computer opponent plays a place of a network game no person holds, until a person comes.</summary>
public sealed class ComputerSeatTests : IDisposable
{
    private readonly List<LockstepClient> _clients = [];
    private LockstepServer _server = new(NewSimulation(playerCount: 3));

    public void Dispose()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }

        _server.Dispose();
    }

    [Fact]
    public void AComputerOpponentTakesEveryPlaceNoPersonHolds()
    {
        var alice = Connect("Alice");
        Exchange();
        Run(ticks: 1);

        var simulation = alice.Simulation!;
        Assert.False(simulation.Hospitals[0].IsVacant);
        Assert.True(simulation.Hospitals[1].IsVacant);
        Assert.True(simulation.Hospitals[2].IsVacant);
        Assert.Equal([1, 2], simulation.Competitors.Select(competitor => competitor.Seat).Order());
        Assert.Equal(2, simulation.Competitors.Select(competitor => competitor.Number).Distinct().Count());
        AssertInSync();
    }

    [Fact]
    public void APersonWhoJoinsTakesTheComputersPlaceAndOneWhoLeavesGivesItBack()
    {
        var alice = Connect("Alice");
        Exchange();
        Run(ticks: 30);

        var bob = Connect("Bob");
        Exchange();
        Run(ticks: 30);

        Assert.Equal(1, bob.Player);
        Assert.False(bob.Simulation!.Hospitals[1].IsVacant);
        Assert.Equal([2], bob.Simulation.Competitors.Select(competitor => competitor.Seat));
        AssertInSync();

        bob.Dispose();
        _clients.Remove(bob);
        Exchange();
        Run(ticks: 30);

        Assert.True(alice.Simulation!.Hospitals[1].IsVacant);
        Assert.Equal([1, 2], alice.Simulation.Competitors.Select(competitor => competitor.Seat).Order());
        AssertInSync();
        Assert.Empty(_server.Desyncs);
    }

    [Fact]
    public void WithoutComputerOpponentsAPlaceNoPersonHoldsStandsEmpty()
    {
        _server.Dispose();
        _server = new LockstepServer(NewSimulation(playerCount: 3)) { ComputerOpponents = false };
        var alice = Connect("Alice");
        Exchange();
        Run(ticks: 1);

        Assert.True(alice.Simulation!.Hospitals[2].IsVacant);
        Assert.Empty(alice.Simulation.Competitors);
        AssertInSync();
    }

    [Fact]
    public void AClientMayNotSayWhoHoldsAPlace()
    {
        var alice = Connect("Alice");
        Exchange();
        Run(ticks: 1);

        alice.Submit(new SetSeatCommand(1, Occupied: true, Computer: false));
        Run(ticks: 5);

        Assert.True(_server.Simulation.Hospitals[1].IsVacant);
        Assert.Contains(_server.Simulation.Competitors, competitor => competitor.Seat == 1);
    }

    private static Simulation NewSimulation(int playerCount)
    {
        var map = new LevelMap(10, 10, playerCount);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                ref var tile = ref map[x, y];
                tile.Floor = 1;
                tile.Set(TileFlags.Passable | TileFlags.CanTravelAll, true);
            }
        }

        var rules = new LevelRules(40000, [], []).WithReserveCompetitors(
        [
            new PlannedCompetitor(1, "ORAC", new CompetitorPersonality(5, 5, 40, 8, 75)),
            new PlannedCompetitor(2, "HAL", new CompetitorPersonality(3, 5, 60, 5, 75)),
            new PlannedCompetitor(3, "COLOSSUS", new CompetitorPersonality(2, 4, 50, 8, 80)),
        ]);
        return new Simulation(map, seed: 42, rules: rules);
    }

    private LockstepClient Connect(string name)
    {
        var (serverEnd, clientEnd) = InMemoryConnection.CreatePair();
        _server.AddConnection(serverEnd);
        var client = new LockstepClient(clientEnd, name);
        _clients.Add(client);
        return client;
    }

    private void Exchange()
    {
        _server.ProcessMessages();
        foreach (var client in _clients)
        {
            client.ProcessMessages();
            client.StepAllowed();
        }

        _server.ProcessMessages();
    }

    private void Run(int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            _server.ProcessMessages();
            _server.AdvanceTick();
            Exchange();
        }
    }

    private void AssertInSync()
    {
        var expected = _server.Simulation.ComputeStateHash();
        foreach (var client in _clients.Where(c => c.IsJoined))
        {
            Assert.Equal(_server.Simulation.Tick, client.Simulation!.Tick);
            Assert.Equal(expected, client.Simulation.ComputeStateHash());
        }
    }
}
