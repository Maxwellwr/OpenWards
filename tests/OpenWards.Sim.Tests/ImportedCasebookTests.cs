using OpenWards.Sim.Diseases;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Import;
using OpenWards.Sim.Maps;
using OpenWards.Sim.Staff;

namespace OpenWards.Sim.Tests;

/// <summary>What an original save's casebook brings into the game.</summary>
public class ImportedCasebookTests
{
    [Fact]
    public void OnlyDrugsTakeTheOriginalsEffectiveness()
    {
        var simulation = NewSimulation();

        // The original keeps 0 for diseases not cured by drugs, such as fake blood at the psychiatrist's.
        GameImport.Apply(simulation, new ImportedGame
        {
            Month = 3,
            Day = 1,
            Casebook =
            [
                new ImportedCase("fake_blood", true, 0, 3, 0, 0, 0),
                new ImportedCase("king_complex", true, 0, 1, 0, 0, 0),
                new ImportedCase("broken_wind", true, 40, 2, 0, 0, 0),
            ],
        });

        var casebook = simulation.Hospitals[0].Casebook;
        Assert.Equal(DiseaseCase.FullEffectiveness, casebook["fake_blood"].CureEffectiveness);
        Assert.Equal(DiseaseCase.FullEffectiveness, casebook["king_complex"].CureEffectiveness);
        Assert.Equal(40, casebook["broken_wind"].CureEffectiveness);
        Assert.Equal(3, casebook["fake_blood"].Recoveries);
    }

    [Fact]
    public void LoadingAGameImportedBeforeTheFixMendsTheEffectiveness()
    {
        var simulation = NewSimulation();
        var hospital = simulation.Hospitals[0];
        hospital.CaseOf(DiseaseCatalog.Find("fake_blood")!, simulation.Rules.Diseases).CureEffectiveness = 0;
        hospital.CaseOf(DiseaseCatalog.Find("broken_wind")!, simulation.Rules.Diseases).CureEffectiveness = 40;

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream).Hospitals[0].Casebook;

        Assert.Equal(DiseaseCase.FullEffectiveness, loaded["fake_blood"].CureEffectiveness);
        Assert.Equal(40, loaded["broken_wind"].CureEffectiveness);
    }

    [Fact]
    public void TakesTheSavesReputationRecordInsuranceDebtsAndPriceLevel()
    {
        var simulation = NewSimulation();

        GameImport.Apply(simulation, new ImportedGame
        {
            Month = 3,
            Day = 17,
            Visitors = 295,
            Reputation = 464,
            ReputationRecord = (239, 257),
            Casebook = [new ImportedCase("broken_wind", true, 40, 2, 0, 0, 0, ReputationPoints: 44, ReputationVisits: 36)],
            InsuranceOwed = [(1, 0, 1332), (3, 9, 589)],
            PriceLevel = 102,
        });

        var hospital = simulation.Hospitals[0];
        Assert.Equal((239L * Hospital.ReputationPerPoint, 257), (hospital.ReputationScore, hospital.ReputationVisits));
        Assert.Equal(464, hospital.Reputation);
        var wind = hospital.Casebook["broken_wind"];
        Assert.Equal((44L * Hospital.ReputationPerPoint, 36), (wind.ReputationScore, wind.ReputationVisits));
        Assert.Equal((1332, 0, 589), (hospital.InsuranceOwed(1), hospital.InsuranceOwed(2), hospital.InsuranceOwed(3)));
        Assert.Equal(102, hospital.PriceLevel);
    }

    [Fact]
    public void PatientsKeepTheirMoneyInsurerAndNeeds()
    {
        var simulation = NewSimulation();

        GameImport.Apply(simulation, new ImportedGame
        {
            Month = 3,
            Day = 1,
            Patients =
            [
                new ImportedPatient("broken_wind", new TilePosition(2, 2), 0.472m, 0.9m, 0.6m, 0.5m, 1522, Wallet: 38710, InsuranceCompany: 2, DiagnosisDifficulty: 83, Bowel: 42, Nausea: 38, ContagiousStrength: 4),
            ],
        });

        var patient = simulation.Entities.OfType<Entities.Humanoid>().Select(h => h.Behaviour).OfType<People.PatientBehaviour>().Single();
        Assert.Equal(472, patient.Health);
        Assert.Equal((38710, 2, 83), (patient.Wallet, patient.InsuranceCompany, patient.DiagnosisDifficulty));
        Assert.Equal((42, 38, 4), (patient.Bowel, patient.Nausea, patient.ContagiousStrength));
        Assert.Equal(1000, patient.DiagnosisProgress);
    }

    [Fact]
    public void StaffKeepTheirDiscontentRaiseRequestsServiceAndShares()
    {
        var simulation = NewSimulation();
        var handyman = StaffProfile.CreateRandom(StaffClass.Handyman, 3, simulation.Rules.Staff, simulation.Random);

        GameImport.Apply(simulation, new ImportedGame
        {
            Month = 3,
            Day = 1,
            Staff = [new ImportedStaff(handyman, new TilePosition(2, 2), 0.5m, 0.2m, Discontent: 53, RaiseRequests: 4, HiredHoursAgo: 1000, HandymanShares: (20, 30, 50))],
        });

        var staff = simulation.Entities.OfType<Entities.Humanoid>().Single(h => h.Profile is not null);
        Assert.Equal((53, 4), (staff.Discontent, staff.RaiseRequestCount));
        Assert.Equal(simulation.Hours - 1000, staff.HiredAt);
        var behaviour = Assert.IsType<People.HandymanBehaviour>(staff.Behaviour);
        Assert.Equal((200, 300, 500), (behaviour.CleaningPriority, behaviour.WateringPriority, behaviour.RepairingPriority));
    }

    [Fact]
    public void TakesTheYearsVipsAndRatsAndTheDaysSinceTheLastVip()
    {
        var simulation = NewSimulation();

        GameImport.Apply(simulation, new ImportedGame
        {
            Month = 3,
            Day = 1,
            VipsThisYear = (1, -2),
            DaysSinceVip = 82,
            RatsThisYear = (7, 12),
        });

        var hospital = simulation.Hospitals[0];
        Assert.Equal((1, -2, 1), (hospital.Vips.ThisYear, hospital.Vips.ScoreSumThisYear, hospital.Vips.PleasedThisYear));
        Assert.Equal(82, hospital.Vips.DaysSinceLast);
        Assert.Equal((7, 12), (hospital.RatsKilledThisYear, hospital.RatShotsThisYear));
    }

    private static Simulation NewSimulation()
    {
        var map = new LevelMap(6, 6, 1);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                map[x, y].Floor = 1;
                map[x, y].Set(TileFlags.Passable, true);
            }
        }

        map[3, 3].Set(TileFlags.Hospital, true);
        map.UpdatePathfinding();
        return new Simulation(map, seed: 2);
    }
}
