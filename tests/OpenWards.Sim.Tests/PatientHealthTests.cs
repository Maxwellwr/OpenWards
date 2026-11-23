using OpenWards.Sim.Diseases;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Entities;
using OpenWards.Sim.Maps;
using OpenWards.Sim.People;
using OpenWards.Sim.Rooms;

namespace OpenWards.Sim.Tests;

/// <summary>Patients' health running out (<see cref="PatientNeeds"/>) and dying (CorsixTH's Patient:tick and die).</summary>
public class PatientHealthTests
{
    private static readonly DiseaseDefinition Cold = DiseaseCatalog.Find("uncommon_cold")!;

    [Fact]
    public void AtNoHealthAPatientDiesOnceOutOfTheRoom()
    {
        var simulation = NewSimulation();
        var (humanoid, patient) = AddPatient(simulation, new TilePosition(2, 2), 2);

        PatientNeeds.Modify(simulation);
        Assert.Equal(1, patient.Health);
        Assert.False(patient.IsSetToDie);

        PatientNeeds.Modify(simulation);
        Assert.Equal(0, patient.Health);
        Assert.True(patient.IsSetToDie);

        // In a room, the patient lives on for now.
        simulation.Map[2, 2].RoomId = 99;
        patient.Update(simulation, humanoid);
        Assert.False(humanoid.IsDespawning);

        simulation.Map[2, 2].RoomId = 0;
        patient.Update(simulation, humanoid);
        Assert.Equal(PatientPhase.Dying, patient.Phase);
        for (var tick = 0; tick < 1000 && !humanoid.IsDespawning; tick++)
        {
            humanoid.Update(simulation);
        }

        Assert.True(humanoid.IsDespawning);
        var hospital = simulation.Hospitals[0];
        Assert.Equal(1, hospital.Deaths);
        Assert.Equal(1, hospital.DeathsThisYear);
        Assert.Equal(1, hospital.CaseOf(Cold, simulation.Rules.Diseases).Fatalities);
    }

    [Fact]
    public void ACureRestoresHealthAndCancelsDying()
    {
        var simulation = NewSimulation();
        var (_, patient) = AddPatient(simulation, new TilePosition(2, 2), 1);
        PatientNeeds.Modify(simulation);
        Assert.True(patient.IsSetToDie);

        patient.Cure();

        Assert.Equal(PatientNeeds.MaxHealth, patient.Health);
        Assert.False(patient.IsSetToDie);
    }

    [Fact]
    public void SavesKeepHealth()
    {
        var simulation = NewSimulation();
        AddPatient(simulation, new TilePosition(2, 2), 1);
        PatientNeeds.Modify(simulation);
        AddPatient(simulation, new TilePosition(3, 3), 830);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);

        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        var copies = restored.Entities.OfType<Humanoid>().Select(entry => (PatientBehaviour)entry.Behaviour!).ToList();
        Assert.True(copies[0].IsSetToDie);
        Assert.Equal(830, copies[1].Health);
    }

    private static (Humanoid Humanoid, PatientBehaviour Patient) AddPatient(Simulation simulation, TilePosition tile, int health)
    {
        var humanoid = simulation.AddEntity(id => Humanoid.CreatePatient(id, tile, simulation.Random, PatientLook.Standard, 0));
        var patient = new PatientBehaviour { Health = health };
        patient.SetDisease(Cold, 0);
        humanoid.Behaviour = patient;
        return (humanoid, patient);
    }

    private static Simulation NewSimulation()
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

        return new Simulation(map, seed: 1, rules: new LevelRules(10_000, [], []));
    }
}
