using OpenWards.Sim.Commands;
using OpenWards.Sim.Diseases;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Entities;
using OpenWards.Sim.Maps;
using OpenWards.Sim.People;
using OpenWards.Sim.Rooms;

namespace OpenWards.Sim.Tests;

/// <summary>Patients' mood, needs and leave count, updated every 16 ticks (<see cref="PatientNeeds"/>).</summary>
public class NeedsTests
{
    [Fact]
    public void TheTriangularRandomFavoursTheMiddleAndMissesAnOddTop()
    {
        var random = new SimRandom(7);
        var counts = new int[6];
        for (var i = 0; i < 90_000; i++)
        {
            counts[random.Triangular(55, 60) - 55]++;
        }

        // 1 : 2 : 3 : 2 : 1, and never 60.
        Assert.Equal(0, counts[5]);
        Assert.InRange(counts[0], 9_000, 11_000);
        Assert.InRange(counts[1], 19_000, 21_000);
        Assert.InRange(counts[2], 29_000, 31_000);
        Assert.InRange(counts[3], 19_000, 21_000);
        Assert.InRange(counts[4], 9_000, 11_000);

        for (var i = 0; i < 1000; i++)
        {
            Assert.InRange(random.Triangular(200, 1000), 200, 1000);
        }
    }

    [Fact]
    public void PatientsStartFullOfDrinkAtMiddleWarmthAndTheSquitsHalfWayToTheToilet()
    {
        var simulation = NewSimulation();
        var (humanoid, patient) = AddPatient(simulation, "uncommon_cold");
        Assert.Equal(100, patient.DrinkStock);
        Assert.Equal(0, patient.Bowel);
        Assert.Equal(50, PatientNeeds.Warmth(humanoid));
        Assert.Equal(100, PatientNeeds.Mood(humanoid));

        var squits = AddPatient(simulation, "the_squits").Patient;
        Assert.Equal(PatientNeedsRules.Default.BowelFull / 2, squits.Bowel);
    }

    [Fact]
    public void EachUpdateTheWaitGrowsTheDrinksRunDownAndHealthFalls()
    {
        var simulation = NewSimulation();
        var (humanoid, patient) = AddPatient(simulation, "uncommon_cold");
        humanoid.Happiness = 0.9m;

        PatientNeeds.Modify(simulation);

        // No radiators: warmth falls a point towards 0 and is still comfortable.
        Assert.Equal(49, PatientNeeds.Warmth(humanoid));
        Assert.Equal(91, PatientNeeds.Mood(humanoid));
        Assert.Equal(1, patient.LeaveCount);
        Assert.Equal(99, patient.DrinkStock);
        Assert.Equal(899, patient.Health);
        Assert.InRange(patient.Bowel, 0, 1);
    }

    [Fact]
    public void EachGrievanceMakesTheWaitGrowFaster()
    {
        var simulation = NewSimulation();
        var (humanoid, patient) = AddPatient(simulation, "uncommon_cold");
        humanoid.Happiness = 0.3m;
        patient.Bowel = 60;
        patient.DrinkStock = 20;

        PatientNeeds.Modify(simulation);

        // One for the time, one each for being unhappy (below 50), needing the toilet (above 50) and thirsty (below 30).
        Assert.Equal(4, patient.LeaveCount);
        Assert.True(patient.IsSad);
        Assert.True(patient.IsNeedingToilet);
        Assert.True(patient.IsThirsty);
    }

    [Fact]
    public void ColdAndHeatCostMoodAndHeatMakesSickAndThirsty()
    {
        var simulation = NewSimulation();
        var (humanoid, patient) = AddPatient(simulation, "uncommon_cold");
        humanoid.Happiness = 0.7m;
        humanoid.Warmth = 0.1m;
        PatientNeeds.Modify(simulation);
        Assert.Equal(69, PatientNeeds.Mood(humanoid));

        humanoid.Warmth = 0.9m;
        PatientNeeds.Modify(simulation);

        // Warmth 89: hot, so one less mood and one more nausea, and 2 more drink gone for being above 60.
        Assert.Equal(68, PatientNeeds.Mood(humanoid));
        Assert.Equal(1, patient.Nausea);
        Assert.Equal(100 - 1 - 3, patient.DrinkStock);
    }

    [Fact]
    public void RadiatorsWarmTheCorridorByTheirDistance()
    {
        var simulation = NewSimulation();
        var (humanoid, _) = AddPatient(simulation, "uncommon_cold");
        AddObject(simulation, "radiator", new TilePosition(4, 2));
        AddObject(simulation, "radiator", new TilePosition(2, 3));
        var world = PatientNeeds.World.Of(simulation);

        // Heat 5: ⌊50 / 2⌋ + ⌊50 / 1⌋.
        Assert.Equal(75, PatientNeeds.WarmthTarget(simulation, world, humanoid, roomId: 0));

        PatientNeeds.Modify(simulation);
        Assert.Equal(51, PatientNeeds.Warmth(humanoid));
    }

    [Fact]
    public void ARadiatorOnThePatientsTileDoesNotWarmThem()
    {
        var simulation = NewSimulation();
        var (humanoid, _) = AddPatient(simulation, "uncommon_cold");
        AddObject(simulation, "radiator", new TilePosition(2, 2));
        AddObject(simulation, "radiator", new TilePosition(2, 4));
        var world = PatientNeeds.World.Of(simulation);

        // Heat 5: only the one two tiles away, ⌊50 / 2⌋.
        Assert.Equal(25, PatientNeeds.WarmthTarget(simulation, world, humanoid, roomId: 0));
    }

    [Fact]
    public void ARoomIsWarmedByItsOwnRadiatorsOverItsAreaWhateverTheHeatingLevel()
    {
        var simulation = NewSimulation();
        var (humanoid, _) = AddPatient(simulation, "uncommon_cold");
        var room = AddRoom(simulation, new TileRect(1, 1, 6, 5));
        AddObject(simulation, "radiator", new TilePosition(1, 1));
        AddObject(simulation, "radiator", new TilePosition(6, 7));
        var world = PatientNeeds.World.Of(simulation);

        // One radiator in a 6 × 5 room: ⌊1250 / 30⌋; the corridor's radiator does not reach in.
        Assert.Equal(41, PatientNeeds.WarmthTarget(simulation, world, humanoid, room.Id));

        // The heating level only matters when off.
        Assert.True(simulation.Hospitals[0].SetRadiatorHeat(10));
        Assert.Equal(41, PatientNeeds.WarmthTarget(simulation, world, humanoid, room.Id));

        // Two radiators in a small room: more than the most warmth.
        AddObject(simulation, "radiator", new TilePosition(3, 1));
        world = PatientNeeds.World.Of(simulation);
        Assert.Equal(83, PatientNeeds.WarmthTarget(simulation, world, humanoid, room.Id));
    }

    [Fact]
    public void ARoomsRadiatorsDoNotWarmTheCorridor()
    {
        var simulation = NewSimulation();
        var (humanoid, _) = AddPatient(simulation, "uncommon_cold");
        AddRoom(simulation, new TileRect(4, 0, 4, 8));
        AddObject(simulation, "radiator", new TilePosition(4, 2));
        var world = PatientNeeds.World.Of(simulation);

        // Two tiles away, but inside the room: nothing.
        Assert.Equal(0, PatientNeeds.WarmthTarget(simulation, world, humanoid, roomId: 0));
    }

    [Fact]
    public void PlantsAndLitterNearbyChangeMoodAndNausea()
    {
        var simulation = NewSimulation();
        var (humanoid, patient) = AddPatient(simulation, "uncommon_cold");
        humanoid.Happiness = 0.5m;
        AddObject(simulation, "plant", new TilePosition(5, 5));
        Litter.Drop(simulation, 0, LitterType.Puke, new TilePosition(3, 2), false);
        Litter.Drop(simulation, 0, LitterType.Paper, new TilePosition(2, 2), false);
        Litter.Drop(simulation, 0, LitterType.Paper, new TilePosition(5, 2), false);

        PatientNeeds.Modify(simulation);

        // Plant +1, puke next to them −2, paper underfoot −1, comfortable +1; paper three tiles away does not count.
        Assert.Equal(49, PatientNeeds.Mood(humanoid));
        Assert.Equal(3, patient.Nausea);
        Assert.Equal(3, patient.LitterCount);
    }

    [Fact]
    public void AWaitingPatientLeavesOnceTheWaitIsOverTheLimit()
    {
        var simulation = NewSimulation();
        var (humanoid, patient) = AddPatient(simulation, "uncommon_cold");
        var hospital = simulation.Hospitals[0];
        hospital.CaseOf(patient.Disease!, simulation.Rules.Diseases).Discovered = true;
        patient.DiagnosisProgress = 300;
        simulation.Schedule(0, new GuessPatientCureCommand(humanoid.Id));
        simulation.Step();
        Assert.Equal(PatientPhase.Waiting, patient.Phase);

        patient.LeaveCount = PatientNeedsRules.Default.LeaveMax - 1;
        PatientNeeds.Modify(simulation);
        Assert.Equal(PatientPhase.Waiting, patient.Phase);

        PatientNeeds.Modify(simulation);
        Assert.Equal(PatientPhase.GoingHome, patient.Phase);
        Assert.Equal(1, hospital.NotCured);
    }

    [Fact]
    public void AnEmergencyPatientKeepsTheirHealthAndAPatientAtNoHealthIsSetToDie()
    {
        var simulation = NewSimulation();
        var (emergency, emergencyPatient) = AddPatient(simulation, "uncommon_cold");
        emergencyPatient.StartEmergency(simulation, emergency, 1);
        var (_, dying) = AddPatient(simulation, "uncommon_cold");
        dying.Health = 1;

        PatientNeeds.Modify(simulation);

        Assert.Equal(900, emergencyPatient.Health);
        Assert.Equal(0, dying.Health);
        Assert.True(dying.IsSetToDie);
    }

    [Fact]
    public void TheUpdateComesEverySixteenTicks()
    {
        Assert.True(PatientNeeds.IsDue(0));
        Assert.False(PatientNeeds.IsDue(15));
        Assert.True(PatientNeeds.IsDue(16));

        var simulation = NewSimulation();
        var (_, patient) = AddPatient(simulation, "uncommon_cold");
        for (var tick = 0; tick < 50; tick++)
        {
            simulation.Step();
        }

        // Ticks 0, 16, 32 and 48.
        Assert.Equal(896, patient.Health);
    }

    [Fact]
    public void EachDayAddsToTheHealthGraph()
    {
        var simulation = NewSimulation();
        var (humanoid, patient) = AddPatient(simulation, "uncommon_cold");
        for (var day = 0; day < 25; day++)
        {
            patient.Health = 900 - day;
            Feelings.OnEndDay(simulation, humanoid);
        }

        Assert.Equal(PatientBehaviour.HealthHistorySize, patient.HealthHistory.Count);
        Assert.Equal(895, patient.HealthHistory[0]);
        Assert.Equal(876, patient.HealthHistory[^1]);
    }

    [Fact]
    public void TheLevelFilesSetWhatPatientsPutUpWith()
    {
        Assert.Equal(250, PatientNeedsRules.ForDifficulty(Difficulty.Easy).LeaveMax);
        Assert.Equal(30, PatientNeedsRules.ForDifficulty(Difficulty.Easy).Unhappy);
        Assert.Equal(150, PatientNeedsRules.ForDifficulty(Difficulty.Medium).LeaveMax);
        Assert.Equal(100, PatientNeedsRules.ForDifficulty(Difficulty.Hard).LeaveMax);
        Assert.Equal(50, PatientNeedsRules.ForDifficulty(Difficulty.Hard).Unhappy);
        Assert.Equal(PatientNeedsRules.ForDifficulty(Difficulty.Hard), new LevelRules(0, [], [], difficulty: Difficulty.Hard).Needs);
    }

    [Fact]
    public void NeedsAreSaved()
    {
        var needs = PatientNeedsRules.Default with { LeaveMax = 123, DrinkHappy = 7 };
        var simulation = NewSimulation(needs);
        var (_, patient) = AddPatient(simulation, "uncommon_cold");
        patient.Nausea = 4;
        patient.LitterCount = 6;
        patient.Bowel = 33;
        patient.DrinkStock = 20;
        PatientNeeds.Modify(simulation);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);
        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(needs, restored.Rules.Needs);
        var copy = (PatientBehaviour)restored.Entities.OfType<Humanoid>().Single().Behaviour!;
        Assert.Equal(patient.Health, copy.Health);
        Assert.Equal(patient.DrinkStock, copy.DrinkStock);
        Assert.Equal(patient.Bowel, copy.Bowel);
        Assert.Equal(patient.Nausea, copy.Nausea);
        Assert.Equal(patient.LitterCount, copy.LitterCount);
        Assert.Equal(patient.LeaveCount, copy.LeaveCount);
        Assert.True(copy.IsThirsty);
    }

    private static (Humanoid Humanoid, PatientBehaviour Patient) AddPatient(Simulation simulation, string diseaseId)
    {
        var humanoid = simulation.AddEntity(id => Humanoid.CreatePatient(id, new TilePosition(2, 2), simulation.Random, PatientLook.Standard, 0));
        var patient = new PatientBehaviour { Health = 900 };
        patient.SetDisease(DiseaseCatalog.Find(diseaseId)!, 0, simulation.Rules.Needs);
        humanoid.Behaviour = patient;
        return (humanoid, patient);
    }

    private static void AddObject(Simulation simulation, string objectType, TilePosition tile)
    {
        simulation.AddEntity(id => new RoomObject(id, objectType, tile, Direction.South, 0));
    }

    private static Room AddRoom(Simulation simulation, TileRect area)
    {
        var room = simulation.AddEntity(id => new Room(id, "gp", area, [.. Enumerable.Repeat(default(OriginalTile), area.Width * area.Height)]));
        for (var x = area.X; x < area.Right; x++)
        {
            for (var y = area.Y; y < area.Bottom; y++)
            {
                simulation.Map[x, y].RoomId = room.Id;
            }
        }

        return room;
    }

    private static Simulation NewSimulation(PatientNeedsRules? needs = null)
    {
        var map = TestMaps.FromRows("........", "........", "........", "........", "........", "........", "........", "........");
        map.ParcelCount = 2;
        map.SetParcelOwner(1, 1);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                map[x, y].Parcel = 1;
                map[x, y].Set(TileFlags.Buildable | TileFlags.Hospital, true);
            }
        }

        return new Simulation(map, seed: 1, rules: new LevelRules(10_000, [], [], needs: needs));
    }
}
