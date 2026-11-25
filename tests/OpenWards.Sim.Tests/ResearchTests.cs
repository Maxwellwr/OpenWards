using OpenWards.Sim.Commands;
using OpenWards.Sim.Definitions;
using OpenWards.Sim.Diseases;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Entities;
using OpenWards.Sim.Maps;
using OpenWards.Sim.Research;
using static OpenWards.Sim.Definitions.FootprintFlags;

namespace OpenWards.Sim.Tests;

/// <summary>Research as the community's logic specification describes the original game.</summary>
public class ResearchTests
{
    /// <summary>The ultrascanner's object type and fallback expertise.</summary>
    private const int Ultrascanner = 22;

    private const int UltrascanExpertise = 40;

    private const int Inflator = 9;

    private const int HairRestorer = 25;

    private const int Electrolyser = 46;

    private static readonly ObjectDefinition Machine = new(
        "ultrascanner", "Ultrascanner", Ultrascanner, new Dictionary<Direction, ObjectOrientation> { [Direction.North] = new([new(0, 0, CompleteCell), new(0, 1, UsePosition)]) });

    private static readonly RoomDefinition Ultrascan = new(
        "ultrascan", "Ultrascan", 4, 19, new WallSet(84, 85, 92, 93), new Dictionary<string, int> { ["ultrascanner"] = 1 }, LevelConfigId: 14);

    [Fact]
    public void EverySliderStartsAtAFifthAndEachAreaWorksOnItsCheapestItem()
    {
        var research = NewSimulation().Hospitals[0].Research;

        Assert.All(Enum.GetValues<ResearchCategory>(), category => Assert.Equal(20, research.Area(category).Share));
        Assert.Equal(100, research.Total);
        Assert.True(research.Area(ResearchCategory.Cure).Current.IsNone);
        Assert.Equal(ResearchTarget.Object("ultrascanner"), research.Area(ResearchCategory.Diagnosis).Current);
        Assert.True(research.Area(ResearchCategory.Drugs).Current.IsNone);
        Assert.Equal(ResearchTarget.Object("inflator"), research.Area(ResearchCategory.Improvements).Current);
        Assert.True(research.Area(ResearchCategory.Specialisation).Current.IsNone);
        Assert.False(research.Objects["ultrascanner"].Discovered);
        Assert.True(research.Objects["inflator"].Discovered);
    }

    [Fact]
    public void EachDayTheAreasTakeTheirShareOfWhatIsLeftInThePoolInTurn()
    {
        var simulation = NewSimulation();
        var hospital = simulation.Hospitals[0];
        var research = hospital.Research;
        research.AddPoints(1_000);

        research.OnEndDay(simulation, hospital);

        // Cure has nothing to discover: its points stay in the pool and its slider goes off. Diagnosis takes 20 % of
        // 1,000, drugs have nothing, improvements take 20 % of the 800 left, specialisation has no disease chosen.
        Assert.Equal(200, research.Objects["ultrascanner"].Points);
        Assert.Equal(160, research.Objects["inflator"].Points);
        Assert.Equal(640, research.Pool);
        Assert.Equal(0, research.Area(ResearchCategory.Cure).Share);
        Assert.Equal(0, research.Area(ResearchCategory.Drugs).Share);
        Assert.Equal(0, research.Area(ResearchCategory.Specialisation).Share);
        Assert.Equal(40, research.Total);
    }

    [Fact]
    public void FiveFifthsTakeTwentySixteenAndAHalfOfTheRestInTurn()
    {
        var simulation = NewSimulation(cureObjects: true, drugDisease: true);
        var hospital = simulation.Hospitals[0];
        var research = hospital.Research;
        Assert.True(research.Concentrate(simulation, hospital, DiseaseCatalog.Find("diag_ultrascan")!));
        research.AddPoints(100_000);

        research.OnEndDay(simulation, hospital);

        // 20 % of 100,000, then 20 % of 80,000, 64,000, 51,200 and 40,960; the rest stays.
        Assert.Equal(100_000 - 20_000 - 16_000 - 12_800 - 10_240 - 8_192, research.Pool);
    }

    [Fact]
    public void EnoughPointsDiscoverTheUltrascannerAndItsRoomCanBeBuilt()
    {
        var simulation = NewSimulation();
        var hospital = simulation.Hospitals[0];
        Assert.False(hospital.IsRoomDiscovered("ultrascan"));
        hospital.Research.AdjustShare(ResearchCategory.Improvements, -100);

        // Diagnosis takes 2,000 of 10,000 points; the discovery takes 1,000 and the item starts again from 0.
        hospital.Research.AddPoints(10_000);
        hospital.Research.OnEndDay(simulation, hospital);

        Assert.True(hospital.Research.Objects["ultrascanner"].Discovered);
        Assert.Equal(0, hospital.Research.Objects["ultrascanner"].Points);
        Assert.True(hospital.IsRoomDiscovered("ultrascan"));
        Assert.True(hospital.Research.Area(ResearchCategory.Diagnosis).Current.IsNone);
    }

    [Fact]
    public void AnAreaWorksOnTheCheapestItemItCan()
    {
        var simulation = NewSimulation(cureObjects: true);
        var research = simulation.Hospitals[0].Research;

        // The hair restorer takes 500 points and the electrolyser 300.
        Assert.Equal(ResearchTarget.Object("electrolyser"), research.Area(ResearchCategory.Cure).Current);
    }

    [Theory]
    [InlineData(0.0, 350)]
    [InlineData(0.65, 200)]
    [InlineData(0.75, 125)]
    public void AnActionGivesFiftyOrAHundredPointsAndTheSkillLessWhenTired(double fatigue, int expected)
    {
        var simulation = NewSimulation();

        // Medium difficulty: tired above 600, very tired above 700.
        Assert.Equal(expected, ResearchDepartment.ActionPoints(simulation, ResearchDepartment.DeskPoints, 300, (decimal)fatigue));
        Assert.Equal(expected + 50, ResearchDepartment.ActionPoints(simulation, ResearchDepartment.MachinePoints, 300, (decimal)fatigue));
    }

    [Fact]
    public void TheAllResearchCheatDiscoversEveryObjectToResearch()
    {
        var simulation = NewSimulation();
        var hospital = simulation.Hospitals[0];

        Run(simulation, new CheatCommand(CheatKind.AllResearch));

        Assert.True(hospital.Research.Objects["ultrascanner"].Discovered);
        Assert.True(hospital.IsRoomDiscovered("ultrascan"));
        Assert.True(hospital.Cheated);
    }

    [Fact]
    public void EquipmentComesByItselfInTheMonthTheLevelNames()
    {
        var simulation = NewSimulation(whenAvail: 3);
        var hospital = simulation.Hospitals[0];
        hospital.Research.CheckAutomaticDiscovery(simulation, hospital, 2);
        Assert.False(hospital.IsRoomDiscovered("ultrascan"));

        hospital.Research.CheckAutomaticDiscovery(simulation, hospital, 3);
        Assert.True(hospital.IsRoomDiscovered("ultrascan"));
    }

    [Fact]
    public void AMachineImprovesInARoundOfNothingStrengthAndCost()
    {
        var simulation = NewSimulation();
        var hospital = simulation.Hospitals[0];
        var research = hospital.Research;
        foreach (var category in Enum.GetValues<ResearchCategory>())
        {
            research.AdjustShare(category, -100);
        }

        research.AdjustShare(ResearchCategory.Improvements, 100);
        var inflator = research.Objects["inflator"];
        var target = ResearchTarget.Object("inflator");

        // Of the inflator's 2,000 points the first improvement takes 10 %, and each next one 10 % more.
        int[] costs = [200, 400, 600, 800];
        Assert.Equal(costs[0], research.Required(simulation, hospital, target));
        research.AddPoints(costs[0]);
        research.OnEndDay(simulation, hospital);
        Assert.Equal((1, 10, 5_000), (inflator.Improvements, inflator.StartStrength, inflator.Cost));

        Assert.Equal(costs[1], research.Required(simulation, hospital, target));
        research.AddPoints(costs[1]);
        research.OnEndDay(simulation, hospital);
        Assert.Equal((2, 12, 5_000), (inflator.Improvements, inflator.StartStrength, inflator.Cost));

        // 7/8 of $5,000 is $4,375, rounded down to tens.
        Assert.Equal(costs[2], research.Required(simulation, hospital, target));
        research.AddPoints(costs[2]);
        research.OnEndDay(simulation, hospital);
        Assert.Equal((3, 12, 4_370), (inflator.Improvements, inflator.StartStrength, inflator.Cost));
        Assert.Equal(4_370, research.ObjectCost(simulation.Rules, "inflator"));

        Assert.Equal(costs[3], research.Required(simulation, hospital, target));
    }

    [Fact]
    public void AMachineGetsAtMostTwentyImprovements()
    {
        var simulation = NewSimulation();
        var hospital = simulation.Hospitals[0];
        var research = hospital.Research;
        foreach (var category in Enum.GetValues<ResearchCategory>())
        {
            research.AdjustShare(category, -100);
        }

        research.AdjustShare(ResearchCategory.Improvements, 100);
        var inflator = research.Objects["inflator"];
        for (var i = 0; i < 30; i++)
        {
            research.AddPoints(10_000);
            research.OnEndDay(simulation, hospital);
        }

        Assert.Equal(ResearchDepartment.MaxImprovements, inflator.Improvements);
        // Six times 7/8 of the cost, rounded down to tens each time; seven times two more uses, up to 20.
        Assert.Equal(2_230, inflator.Cost);
        Assert.Equal(ResearchRules.DefaultMaxObjectStrength, inflator.StartStrength);
        Assert.Null(research.Required(simulation, hospital, ResearchTarget.Object("inflator")));
        Assert.True(research.Area(ResearchCategory.Improvements).Current.IsNone);
    }

    [Fact]
    public void AKnownDrugGetsCheaperOrMoreEffectiveAndNeverBelowTenDollars()
    {
        var simulation = NewSimulation(drugDisease: true);
        var hospital = simulation.Hospitals[0];
        var research = hospital.Research;
        var disease = DrugDisease(simulation);
        var entry = hospital.CaseOf(disease, simulation.Rules.Diseases);
        entry.CureEffectiveness = 80;
        entry.DrugCost = 40;
        entry.Discovered = true;
        research.OnDiseaseDiscovered(simulation, hospital);
        var target = ResearchTarget.Drug(disease.Id);
        Assert.Equal(target, research.Area(ResearchCategory.Drugs).Current);

        // ⌊1,000 × (10 + 10 × ⌊80 / 25⌋) / 100⌋.
        Assert.Equal(400, research.Required(simulation, hospital, target));
        foreach (var category in Enum.GetValues<ResearchCategory>())
        {
            research.AdjustShare(category, -100);
        }

        research.AdjustShare(ResearchCategory.Drugs, 100);
        var changes = 0;
        for (var i = 0; i < 200 && research.Area(ResearchCategory.Drugs).Current == target; i++)
        {
            var (effectiveness, cost) = (entry.CureEffectiveness, entry.DrugCost);
            research.AddPoints(research.Required(simulation, hospital, target)!.Value);
            research.OnEndDay(simulation, hospital);

            // Cheaper by 5 % or a dollar, or the same; more effective by 5 or the same.
            Assert.Contains(entry.DrugCost, new[] { cost, Math.Max(10, cost * 95 / 100), Math.Max(10, cost - 1) });
            Assert.Contains(entry.CureEffectiveness, new[] { effectiveness, Math.Min(100, effectiveness + 5) });
            changes += entry.DrugCost != cost || entry.CureEffectiveness != effectiveness ? 1 : 0;
        }

        Assert.True(changes > 0);
        Assert.Equal(100, entry.CureEffectiveness);
        Assert.True(entry.DrugCost >= ResearchDepartment.MinDrugCost);
        Assert.True(research.Area(ResearchCategory.Drugs).Current.IsNone);
    }

    [Fact]
    public void ADrugIsThereFromTheStartAndResearchOnlyImprovesIt()
    {
        // A disease the level does not mark Known: the pharmacy has its drug all the same.
        var simulation = NewSimulation(drugDisease: true);
        var hospital = simulation.Hospitals[0];
        var research = hospital.Research;
        var disease = DrugDisease(simulation);
        Assert.False(simulation.Rules.Diseases.IsKnown(disease.ExpertiseId));

        // Research improves it at once, at the improvement's cost: ⌊1,000 × (10 + 10 × ⌊50 / 25⌋) / 100⌋.
        var target = ResearchTarget.Drug(disease.Id);
        Assert.Equal(target, research.Area(ResearchCategory.Drugs).Current);
        Assert.Equal(300, research.Required(simulation, hospital, target));
    }

    [Fact]
    public void ConcentratingOnADiseaseResearchesItsItem()
    {
        var simulation = NewSimulation();
        var hospital = simulation.Hospitals[0];
        var research = hospital.Research;
        var diagnosis = DiseaseCatalog.Find("diag_ultrascan")!;
        hospital.CaseOf(diagnosis, simulation.Rules.Diseases).Discovered = true;

        Run(simulation, new ConcentrateResearchCommand(diagnosis.Id));
        Assert.Equal(diagnosis.Id, research.Concentrated);
        Assert.Equal(ResearchTarget.Object("ultrascanner"), research.Area(ResearchCategory.Specialisation).Current);

        // Once it is discovered, the specialisation improves it; the casebook's button again stops it.
        research.AddPoints(100_000);
        research.OnEndDay(simulation, hospital);
        Assert.True(research.Objects["ultrascanner"].Discovered);
        Assert.Equal(ResearchTarget.Object("ultrascanner"), research.Area(ResearchCategory.Specialisation).Current);
        Run(simulation, new ConcentrateResearchCommand(diagnosis.Id));
        Assert.Equal(string.Empty, research.Concentrated);
        Assert.True(research.Area(ResearchCategory.Specialisation).Current.IsNone);
    }

    [Fact]
    public void AnAutopsyAddsAThirdOfTheDiseasesItemAtOnce()
    {
        var simulation = NewSimulation();
        var hospital = simulation.Hospitals[0];

        hospital.Research.AddPointsForAutopsy(simulation, hospital, DiseaseCatalog.Find("diag_ultrascan")!);

        // ⌊1,000 × 33 / 100⌋.
        Assert.Equal(330, hospital.Research.Objects["ultrascanner"].Points);
        Assert.Equal(0, hospital.Research.Pool);
    }

    [Fact]
    public void ResearchCostsNoMoney()
    {
        var simulation = NewSimulation();
        var hospital = simulation.Hospitals[0];
        hospital.Research.AddPoints(1_000);
        var balance = hospital.Balance;

        for (var day = 0; day < 40; day++)
        {
            hospital.Research.OnEndDay(simulation, hospital);
        }

        Assert.Equal(balance, hospital.Balance);
        Assert.DoesNotContain(hospital.Transactions, transaction => transaction.Kind == TransactionKind.Research);
    }

    [Fact]
    public void TheResearchWindowMovesSharesWithinOneHundredPercent()
    {
        var simulation = NewSimulation();
        var research = simulation.Hospitals[0].Research;

        Run(simulation, new AdjustResearchCommand(ResearchCategory.Improvements, -15));
        Assert.Equal(5, research.Area(ResearchCategory.Improvements).Share);
        Assert.Equal(85, research.Total);

        // Not above 100 %, and not for an area with nothing to work on.
        Run(simulation, new AdjustResearchCommand(ResearchCategory.Cure, -20));
        Run(simulation, new AdjustResearchCommand(ResearchCategory.Cure, 5));
        Assert.Equal(0, research.Area(ResearchCategory.Cure).Share);
        Run(simulation, new AdjustResearchCommand(ResearchCategory.Diagnosis, 50));
        Assert.Equal(55, research.Area(ResearchCategory.Diagnosis).Share);
        Assert.Equal(100, research.Total);
    }

    [Fact]
    public void ResearchIsSaved()
    {
        var simulation = NewSimulation();
        var hospital = simulation.Hospitals[0];
        hospital.Research.AddPoints(1_000);
        hospital.Research.OnEndDay(simulation, hospital);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, NewContent());
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
        var copy = loaded.Hospitals[0].Research;
        Assert.Equal(hospital.Research.Objects["ultrascanner"].Points, copy.Objects["ultrascanner"].Points);
        Assert.Equal(hospital.Research.Pool, copy.Pool);
        Assert.Equal(hospital.Research.Area(ResearchCategory.Diagnosis).Current, copy.Area(ResearchCategory.Diagnosis).Current);
        Assert.Equal(1_000, loaded.Rules.Research.ExpertiseRequired(UltrascanExpertise));
    }

    [Fact]
    public void OnlyObjectsTheLevelHasAndResearchHasFoundCanBeBought()
    {
        var simulation = NewSimulation();
        var research = simulation.Hospitals[0].Research;

        // UIEditRoom:purchaseItems: an undiscovered machine is not offered, a discovered one and plain furniture are.
        Assert.False(research.CanBuy(simulation.Rules, "ultrascanner"));
        Assert.True(research.CanBuy(simulation.Rules, "inflator"));
        Assert.True(research.CanBuy(simulation.Rules, "cabinet"));

        var level = new LevelRules(1000, [], [], objectAvailability: [KeyValuePair.Create("analyser", new ObjectAvailability(ForLevel: false, AtStart: false))]);
        Assert.False(research.CanBuy(level, "analyser"));
    }

    private static DiseaseDefinition DrugDisease(Simulation simulation) =>
        simulation.Rules.Diseases.AvailableDiseases().First(ResearchDepartment.IsDrugDisease);

    private static void Run(Simulation simulation, Command command)
    {
        simulation.Schedule(0, command);
        simulation.Step();
    }

    private static GameContent NewContent() => new([Ultrascan], [114], [Machine]);

    private static Simulation NewSimulation(int whenAvail = 0, bool cureObjects = false, bool drugDisease = false)
    {
        var map = TestMaps.FromRows("........", "........", "........", "........", "........", "........", "........", "........");
        map.ParcelCount = 2;
        map.SetParcelOwner(1, 1);
        var expertise = new int[DiseaseRules.ExpertiseCount + 1];
        expertise[UltrascanExpertise] = 1_000;
        expertise[2] = 2_000;
        List<KeyValuePair<int, ResearchObjectRules>> objects =
        [
            KeyValuePair.Create(Ultrascanner, new ResearchObjectRules(6_000, StartAvail: false, whenAvail, 12, ForLevel: true, null)),
            KeyValuePair.Create(Inflator, new ResearchObjectRules(5_000, StartAvail: true, 0, 10, ForLevel: true, null)),
        ];
        if (cureObjects)
        {
            objects.Add(KeyValuePair.Create(HairRestorer, new ResearchObjectRules(4_000, StartAvail: false, 0, 8, ForLevel: true, 500)));
            objects.Add(KeyValuePair.Create(Electrolyser, new ResearchObjectRules(4_000, StartAvail: false, 0, 10, ForLevel: true, 300)));
        }

        var rules = new LevelRules(
            100_000,
            [KeyValuePair.Create("ultrascan", 1_000)],
            [KeyValuePair.Create("ultrascanner", 6_000)],
            objectAvailability: [KeyValuePair.Create("ultrascanner", new ObjectAvailability(ForLevel: true, AtStart: false))],
            research: new ResearchRules(objects, expertise));
        if (drugDisease)
        {
            var disease = rules.Diseases.AvailableDiseases().First(ResearchDepartment.IsDrugDisease);
            expertise[disease.ExpertiseId] = 1_000;
            rules = new LevelRules(
                100_000,
                [KeyValuePair.Create("ultrascan", 1_000)],
                [KeyValuePair.Create("ultrascanner", 6_000)],
                objectAvailability: [KeyValuePair.Create("ultrascanner", new ObjectAvailability(ForLevel: true, AtStart: false))],
                research: new ResearchRules(objects, expertise));
        }

        var simulation = new Simulation(map, seed: 3, NewContent(), rules);
        if (drugDisease)
        {
            var hospital = simulation.Hospitals[0];
            var entry = hospital.CaseOf(DrugDisease(simulation), simulation.Rules.Diseases);
            entry.Discovered = true;
            entry.CureEffectiveness = 50;
            hospital.Research.OnDiseaseDiscovered(simulation, hospital);
        }

        return simulation;
    }
}
