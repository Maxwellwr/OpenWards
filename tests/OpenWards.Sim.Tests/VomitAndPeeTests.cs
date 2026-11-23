using OpenWards.Sim.Definitions;
using OpenWards.Sim.Diseases;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Entities;
using OpenWards.Sim.Maps;
using OpenWards.Sim.People;

namespace OpenWards.Sim.Tests;

/// <summary>Patients being sick and doing it on the floor (<see cref="PatientNeeds"/>, CorsixTH's Patient:vomit and Patient:pee).</summary>
public class VomitAndPeeTests
{
    private static readonly Dictionary<HumanoidType, PatientAnimations> Animations = new()
    {
        [HumanoidType.StandardMalePatient] = new(new(2056, 6), new(2244, 4)),
        [HumanoidType.StandardFemalePatient] = new(new(3184, 5), new(4744, 4)),
    };

    [Fact]
    public void ANauseousPatientIsSickOnTheSpotLosesHealthAndLeavesPuke()
    {
        var simulation = NewSimulation();
        var (humanoid, patient) = AddPatient(simulation, health: 700);
        var tile = humanoid.Tile;

        UpdateUntil(simulation, () => patient.SpotAction == PatientSpotAction.Vomit, () => patient.Nausea = PatientNeedsRules.DefaultVomitLimit);
        Assert.True(patient.HasVomited);
        Assert.True(patient.IsSidetracked);
        Assert.Equal(5, patient.Nausea);
        Assert.True(patient.Health <= 700 - 10 - 1, $"Health {patient.Health}.");

        var animation = Animations[humanoid.Type].Vomit!.Value;
        simulation.Step();
        Assert.Equal(animation, humanoid.ActionAnimation);
        for (var tick = 0; tick < animation.Length; tick++)
        {
            simulation.Step();
        }

        Assert.Equal(PatientSpotAction.None, patient.SpotAction);
        Assert.Null(humanoid.ActionAnimation);
        var puke = Assert.Single(simulation.Entities.OfType<Litter>());
        Assert.Equal(LitterType.Puke, puke.Type);
        Assert.Equal(tile, puke.Tile);
    }

    [Fact]
    public void APatientBelowTheLimitsIsNotSickAndDoesNotDoItOnTheFloor()
    {
        var simulation = NewSimulation();
        var (_, patient) = AddPatient(simulation, health: 950);
        for (var update = 0; update < 100; update++)
        {
            patient.Nausea = PatientNeedsRules.DefaultVomitLimit - 1;
            patient.Bowel = PatientNeedsRules.DefaultBowelOverflows - 2;
            PatientNeeds.Modify(simulation);
            Assert.Equal(PatientSpotAction.None, patient.SpotAction);
        }

        Assert.False(patient.HasVomited);
    }

    [Fact]
    public void APatientWhoseBowelsOverflowDoesItOnTheFloor()
    {
        var simulation = NewSimulation();
        var (_, patient) = AddPatient(simulation, health: 950);

        UpdateUntil(simulation, () => patient.SpotAction == PatientSpotAction.Pee, () => patient.Bowel = PatientNeedsRules.DefaultBowelOverflows);
        Assert.False(patient.IsNeedingToilet);
        Assert.Equal(0, patient.Bowel);
        Assert.True(simulation.Hospitals[0].DidItOnFloor);

        for (var tick = 0; tick < 10; tick++)
        {
            simulation.Step();
        }

        Assert.Equal(LitterType.Pee, Assert.Single(simulation.Entities.OfType<Litter>()).Type);
    }

    [Fact]
    public void APatientOffTheirHospitalsFloorDoesNeither()
    {
        var simulation = NewSimulation();
        var (humanoid, patient) = AddPatient(simulation, health: 500);
        humanoid.PlaceAt(new TilePosition(9, 9));
        simulation.Map[9, 9].Set(TileFlags.Hospital, false);

        Assert.False(patient.IsAtFullyEmptyTile(simulation, humanoid));
        patient.Vomit(simulation, humanoid);
        patient.Pee(simulation, humanoid);
        Assert.Equal(PatientSpotAction.None, patient.SpotAction);
    }

    [Fact]
    public void AGameSavedWhileAPatientIsSickContinuesIdentically()
    {
        var simulation = NewSimulation();
        var (_, patient) = AddPatient(simulation, health: 700);
        UpdateUntil(simulation, () => patient.SpotAction == PatientSpotAction.Vomit, () => patient.Nausea = PatientNeedsRules.DefaultVomitLimit);
        simulation.Step();
        simulation.Step();

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream, NewContent());
        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        for (var tick = 0; tick < 20; tick++)
        {
            simulation.Step();
            restored.Step();
        }

        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Single(restored.Entities.OfType<Litter>());
    }

    /// <summary>Updates the needs, one time in five the patient acts, until they do.</summary>
    private static void UpdateUntil(Simulation simulation, Func<bool> done, Action prepare)
    {
        for (var update = 0; update < 200 && !done(); update++)
        {
            prepare();
            PatientNeeds.Modify(simulation);
        }

        Assert.True(done());
    }

    private static (Humanoid Humanoid, PatientBehaviour Patient) AddPatient(Simulation simulation, int health)
    {
        var humanoid = simulation.AddEntity(id => Humanoid.CreatePatient(id, new TilePosition(4, 4), simulation.Random, PatientLook.Standard, 0));
        var patient = new PatientBehaviour { Health = health };
        patient.SetDisease(DiseaseCatalog.Find("uncommon_cold")!, 0);
        humanoid.Behaviour = patient;
        return (humanoid, patient);
    }

    private static GameContent NewContent() => new([], [114], patientAnimations: Animations);

    private static Simulation NewSimulation()
    {
        var map = TestMaps.FromRows("..........", "..........", "..........", "..........", "..........", "..........", "..........", "..........", "..........", "..........");
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

        map.UpdatePathfinding();
        return new Simulation(map, seed: 1, NewContent(), new LevelRules(10_000, [], []));
    }
}
