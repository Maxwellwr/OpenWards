using OpenWards.Content.Saves;
using OpenWards.Formats;
using OpenWards.Formats.Tests;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Entities;
using OpenWards.Sim.Maps;

namespace OpenWards.Content.Tests;

public sealed class OriginalSaveImporterTests(ITestOutputHelper output)
{
    [Fact]
    public void RebuildsTheRealSaves()
    {
        var install = RealGameData.Require();
        var folder = install.FindDirectory("SAVE");
        var files = folder is null
            ? []
            : Directory.EnumerateFiles(folder, "*.H*").Where(file => new FileInfo(file).Length == OriginalSaveGame.Length).Order().ToList();
        if (files.Count == 0)
        {
            Assert.Skip("No original saves in the SAVE folder.");
        }

        foreach (var file in files)
        {
            var save = OriginalSaveGame.Parse(File.ReadAllBytes(file));
            if (save.Level is < 1 or > 12)
            {
                continue;
            }

            var (simulation, report) = OriginalSaveImporter.Start(install, save, seed: 1);
            var hospital = simulation.Hospitals[0];
            var rooms = simulation.Entities.OfType<Room>().Count(r => r.IsActive);
            var expectedRooms = save.Rooms.Count(r => r.Width > 1 && r.Height > 1);
            output.WriteLine($"{Path.GetFileName(file)}: level {save.Level}, {rooms}/{expectedRooms} rooms, {report.Problems.Count} problems, balance {hospital.Balance}, {simulation.Date}");
            foreach (var problem in report.Problems)
            {
                output.WriteLine("   " + problem);
            }

            Assert.Empty(report.Problems);
            AssertSecondHalvesMatch(save, simulation);
            Assert.Equal(save.Hospitals[0].Money, hospital.Balance);
            Assert.Equal(save.Hospitals[0].Loan, hospital.Loan);
            Assert.Equal(save.Month + 1, simulation.Date.Month);
            var staffInSave = save.Entities.Count(e => e.Kind == 4 && e.TileX != 0);
            var staff = simulation.Entities.OfType<Humanoid>().Where(h => h.Profile is not null && h.Employer == 0).ToList();
            output.WriteLine($"   staff {staff.Count}/{staffInSave}");
            Assert.Equal(staffInSave, staff.Count);
            Assert.All(staff, s => Assert.NotNull(s.Profile!.ImportedName));
            var patientsInSave = save.Entities.Count(e => e.Kind == 2 && e.TileX != 0 && e.Disease > 0);
            var patients = simulation.Entities.OfType<Humanoid>().Count(h => h.Behaviour is OpenWards.Sim.People.PatientBehaviour);
            output.WriteLine($"   patients {patients}/{patientsInSave}");
            Assert.InRange(patients, patientsInSave * 9 / 10, patientsInSave);

            var saved = save.Hospitals[0];
            Assert.Equal(saved.Reputation, hospital.Reputation);
            Assert.Equal(saved.PeopleCured, hospital.Cured);

            // Every littered tile the hospital counts comes over, and its rat holes from its list of holes.
            var litter = simulation.Entities.OfType<Litter>().Count(l => l.Player == 0 && !l.IsRemoved);
            output.WriteLine($"   litter {litter}/{saved.LitteredTiles}, rat holes {hospital.Ratholes.Count}/{saved.RatholeCount}");
            Assert.Equal(saved.LitteredTiles, litter);
            Assert.Equal(Math.Min(saved.RatholeCount, OpenWards.Sim.Disasters.Rats.MaxRatholes), hospital.Ratholes.Count);

            // The computer players' reputations go to the level's opponents.
            var rivals = save.Hospitals.Where(h => h.Kind == 2).Select(h => h.Reputation).Order();
            Assert.Equal(rivals.Take(simulation.Competitors.Count), simulation.Competitors.Select(c => c.Reputation).Order().Take(rivals.Count()));

            // They go on with their scenario: open or not, their staff and the step they had got to.
            foreach (var rival in save.Hospitals.Where(h => h.Kind == 2))
            {
                if (simulation.Competitors.FirstOrDefault(c => string.Equals(c.Name, rival.Name, StringComparison.OrdinalIgnoreCase)) is { } opponent)
                {
                    output.WriteLine($"   {rival.Name}: position {rival.CompetitorScenarioPosition} → step {opponent.ScenarioStep}, pending {opponent.Pending}, open {opponent.IsOpen}, staff {opponent.Staff.Count}, rooms {string.Join(",", opponent.Rooms)}, wages {opponent.Wages}, patients {opponent.PatientsInHospital}");
                    Assert.Equal(rival.IsOpen, opponent.IsOpen);
                    Assert.Equal(rival.Nurses + rival.Doctors + rival.Handymen + rival.Receptionists, opponent.Staff.Count);
                    Assert.Equal(rival.CompetitorScenarioPosition is >= 1 and <= 9 ? rival.CompetitorScenarioPosition - 1 : opponent.ScenarioStep, opponent.ScenarioStep);
                    Assert.Equal(rival.CompetitorQueue.Count(task => task.Type is 1 or 4), opponent.Queue.Count(task => task is not null));
                    Assert.Equal(rival.CompetitorPending.Kind != 0, opponent.Pending is not null);
                }
            }

            output.WriteLine($"   statement {hospital.Transactions.Count}, months {hospital.Statistics.Count}, discovered {hospital.Casebook.Values.Count(c => c.Discovered)}");
            if (hospital.Transactions.Count > 0)
            {
                Assert.Equal(saved.Money, hospital.Transactions[0].Balance);
            }

            // The rebuilt hospital runs: a week of play goes by without an error.
            for (var tick = 0; tick < 7 * 50; tick++)
            {
                simulation.Step();
            }
        }
    }

    [Fact]
    public void ComputerPlayersGoOnWithTheBuildingAndBuyingTheyWereWaitingFor()
    {
        var install = RealGameData.Require();
        var file = install.FindDirectory("SAVE") is { } folder ? Path.Combine(folder, "MAXWELL.H99") : null;
        if (file is null || !File.Exists(file))
        {
            Assert.Skip("MAXWELL.H99 is not in the SAVE folder.");
        }

        // MAXWELL.H99 (level 1, tick 796) catches two computer players in their start: ORAC waits to build a GP's office
        // (+4604 = 2, room type 7) and HAL to buy one more bench (+4604 = 3, object type 4, one left).
        var save = OriginalSaveGame.Parse(File.ReadAllBytes(file));
        var (simulation, _) = OriginalSaveImporter.Start(install, save, seed: 1);
        var orac = Assert.Single(simulation.Competitors, c => string.Equals(c.Name, "ORAC", StringComparison.OrdinalIgnoreCase));
        var hal = Assert.Single(simulation.Competitors, c => string.Equals(c.Name, "HAL", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(new CompetitorAction(CompetitorActionKind.Build, "gp", FromStart: true), orac.Pending);
        Assert.Equal(new CompetitorAction(CompetitorActionKind.Buy, "bench", 1, FromStart: true), hal.Pending);
    }

    [Fact]
    public void AStaffMemberWaitingForAnAnswerToARaiseRequestStillWaits()
    {
        var install = RealGameData.Require();
        var file = install.FindDirectory("SAVE") is { } folder ? Path.Combine(folder, "MAXWELL.H3") : null;
        if (file is null || !File.Exists(file))
        {
            Assert.Skip("MAXWELL.H3 is not in the SAVE folder.");
        }

        // MAXWELL.H3 (tick 54419): one doctor has just asked for money (their first request) and waits until tick 55900.
        var save = OriginalSaveGame.Parse(File.ReadAllBytes(file));
        var (simulation, _) = OriginalSaveImporter.Start(install, save, seed: 1);
        var asking = Assert.Single(simulation.Entities.OfType<Humanoid>(), h => h.Profile is not null && h.HasRaiseRequest);
        Assert.Equal((1, 55900 - save.Tick), (asking.RaiseRequestCount, asking.QuittingIn));
    }

    /// <summary>
    /// The second halves of two-part objects are not imported but drawn from their first half: each one in the save
    /// (op_sink2, operating_table_b) has to fall on the tile its imported first half draws it at, so the surgeons' sink
    /// and the table's far side stand where the original had them.
    /// </summary>
    private void AssertSecondHalvesMatch(OriginalSaveGame save, OpenWards.Sim.Simulation simulation)
    {
        (byte Type, string Master)[] halves = [(34, "op_sink1"), (31, "operating_table")];
        foreach (var (type, master) in halves)
        {
            foreach (var half in save.Entities.Where(e => e.Kind == 6 && e.TileX != 0 && e.Type == type))
            {
                var tile = new TilePosition(half.TileX, half.TileY);
                var slaves = simulation.Entities.OfType<RoomObject>().Where(o => o.ObjectType == master).Select(SlaveTile).ToList();
                output.WriteLine($"   {master} second half at {tile}");
                Assert.Contains(tile, slaves);
            }
        }
    }

    /// <summary>Where a two-part object's second half is: op_sink2.lua and operating_table_b.lua's <c>slave_position</c>.</summary>
    private static TilePosition SlaveTile(RoomObject master) => (master.ObjectType, master.Orientation is Direction.North or Direction.South) switch
    {
        ("op_sink1", true) => new TilePosition(master.Tile.X, master.Tile.Y - 1),
        ("op_sink1", false) => new TilePosition(master.Tile.X - 1, master.Tile.Y),
        (_, true) => new TilePosition(master.Tile.X + 1, master.Tile.Y - 1),
        _ => new TilePosition(master.Tile.X - 1, master.Tile.Y + 1),
    };
}
