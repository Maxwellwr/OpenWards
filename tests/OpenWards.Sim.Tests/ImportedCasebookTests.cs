using OpenWards.Sim.Diseases;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Import;
using OpenWards.Sim.Maps;

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
