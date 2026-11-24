using OpenWards.Sim.Definitions;
using OpenWards.Sim.Diseases;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Entities;
using OpenWards.Sim.Maps;
using OpenWards.Sim.People;

namespace OpenWards.Sim.Tests;

/// <summary>Prices, the patient's money and insurance, as the original game has them.</summary>
public class PriceAndPaymentTests
{
    private static readonly DiseaseDefinition Cold = DiseaseCatalog.Find("uncommon_cold")!;
    private static readonly DiseaseDefinition Gp = DiseaseCatalog.Find(DiseaseCatalog.GpDiagnosis)!;

    [Fact]
    public void TheExpectedPriceRisesWithTheReputationForTheDiseaseAndThePlayersPriceIsAShareOfIt()
    {
        var hospital = new Hospital(0, 0);
        var rules = DiseaseRules.Default;
        var cold = hospital.CaseOf(Cold, rules);

        Assert.Equal(300, hospital.ExpectedPrice(Cold, rules));
        cold.Reputation = 777;
        Assert.Equal(466, hospital.ExpectedPrice(Cold, rules)); // ⌊300 × 777 / 500⌋
        cold.PricePercent = 133;
        Assert.Equal(619, hospital.TreatmentPrice(Cold, rules)); // ⌊466 × 133 / 100⌋

        // Below 500 the start price stays.
        cold.Reputation = 100;
        Assert.Equal(300, hospital.ExpectedPrice(Cold, rules));
    }

    [Theory]
    [InlineData(100, 1.0, 1.0)]
    [InlineData(105, 0.88, 0.93)]
    [InlineData(110, 0.79, 0.85)]
    [InlineData(120, 0.64, 0.70)]
    [InlineData(150, 0.31, 0.36)]
    [InlineData(200, 0.0, 0.01)]
    public void PatientsAgreeToAMarkUpAsTheOriginalsDo(int percent, double low, double high)
    {
        var random = new SimRandom(5);
        const int expected = 1000;
        var price = expected * percent / 100;
        var agreed = Enumerable.Range(0, 10_000).Count(_ => PatientCare.Agrees(random, price, expected, wallet: 50_000, insurer: 0));

        Assert.InRange(agreed / 10_000.0, low, high);
    }

    [Fact]
    public void APatientShortOfMoneyRefusesEvenTheStandardPriceUnlessInsured()
    {
        var random = new SimRandom(5);

        Assert.All(Enumerable.Range(0, 1000), _ => Assert.False(PatientCare.Agrees(random, 1000, 1000, wallet: 999, insurer: 0)));
        Assert.All(Enumerable.Range(0, 1000), _ => Assert.True(PatientCare.Agrees(random, 1000, 1000, wallet: 0, insurer: 3)));
    }

    [Fact]
    public void AnInsuranceCompanysBonusLetsItsPatientsPayMore()
    {
        var random = new SimRandom(5);

        // At +50 %, the sixth company's 40 % bonus: 1500 ≤ 1400 + rng % 751, about 87 % agree; the eighth's 3 %: about 37 %.
        var generous = Enumerable.Range(0, 10_000).Count(_ => PatientCare.Agrees(random, 1500, 1000, wallet: 0, insurer: 6));
        var mean = Enumerable.Range(0, 10_000).Count(_ => PatientCare.Agrees(random, 1500, 1000, wallet: 0, insurer: 8));

        Assert.InRange(generous, 8400, 9000);
        Assert.InRange(mean, 3400, 4000);
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 3)]
    [InlineData(3, 10)]
    [InlineData(4, 5)]
    [InlineData(12, 5)]
    [InlineData(null, 5)]
    public void TheShareOfInsuredPatientsDependsOnTheLevel(int? level, int oneIn)
    {
        Assert.Equal(oneIn, PatientCare.InsuredOneIn(level));

        var simulation = new Simulation(TestMaps.FromRows("...."), 3, GameContent.Empty, new LevelRules(0, [], [], levelNumber: level));
        var companies = Enumerable.Range(0, 6000).Select(_ => PatientCare.ChooseInsuranceCompany(simulation)).ToList();

        Assert.InRange(companies.Count(c => c > 0), (6000 / oneIn) - 150, (6000 / oneIn) + 150);
        Assert.All(companies, c => Assert.InRange(c, 0, Hospital.InsuranceCompanies));
    }

    [Fact]
    public void EachCompanyHasItsOwnDelayAndBonus()
    {
        Assert.Equal([3, 6, 8, 2, 9, 10, 4, 3], Enumerable.Range(1, 8).Select(Hospital.InsurerPaymentDelay));
        Assert.Equal([15, 10, 5, 8, 9, 40, 12, 3], Enumerable.Range(1, 8).Select(Hospital.InsurerPriceBonus));
    }

    [Fact]
    public void APatientPaysFromTheirMoneyAndLeavesAProcedureUnpaidWhenShort()
    {
        var simulation = new Simulation(TestMaps.FromRows("...."), 3, GameContent.Empty, new LevelRules(10_000, [], []));
        var hospital = simulation.Hospitals[0];
        var (humanoid, patient) = AddPatient(simulation);
        patient.Wallet = 150;

        // The GP's 100 is paid...
        PatientCare.ReceiveMoneyForTreatment(simulation, humanoid, patient, "gp");
        Assert.Equal(50, patient.Wallet);
        Assert.Equal(10_100, hospital.Balance);

        // ...but not again: the patient goes on, the hospital goes without.
        PatientCare.ReceiveMoneyForTreatment(simulation, humanoid, patient, "gp");
        Assert.Equal(50, patient.Wallet);
        Assert.Equal(10_100, hospital.Balance);
        Assert.Equal(100, hospital.CaseOf(Gp, simulation.Rules.Diseases).MoneyEarned);
    }

    [Fact]
    public void AnInsuredPatientsCompanyOwesThePriceAndTheWalletStays()
    {
        var simulation = new Simulation(TestMaps.FromRows("...."), 3, GameContent.Empty, new LevelRules(10_000, [], []));
        var hospital = simulation.Hospitals[0];
        var humanoid = simulation.AddEntity(id => Humanoid.CreatePatient(id, new TilePosition(1, 0), simulation.Random, PatientLook.Standard, 0));
        var patient = new PatientBehaviour();
        patient.SetDisease(Cold, 1);
        humanoid.Behaviour = patient;
        var wallet = patient.Wallet;

        PatientCare.ReceiveMoneyForTreatment(simulation, humanoid, patient, "gp");

        Assert.Equal(wallet, patient.Wallet);
        Assert.Equal(10_000, hospital.Balance);
        Assert.Equal(100, hospital.InsuranceOwed(1));
    }

    [Fact]
    public void PatientsBringBetween5000And99999()
    {
        var simulation = new Simulation(TestMaps.FromRows("...."), 3);
        var random = simulation.Random.State;
        var wallets = Enumerable.Range(0, 500).Select(_ => PatientCare.NewPatientMoney(simulation)).ToList();

        Assert.All(wallets, wallet => Assert.InRange(wallet, 5000, 99_999));
        Assert.True(wallets.Min() < 20_000 && wallets.Max() > 85_000);

        // From a stream of their own: the game's other draws stay as they were.
        Assert.Equal(random, simulation.Random.State);
    }

    [Fact]
    public void TheMonthsPriceLevelHasTheAdviserSayWhenPricesAreFarOff()
    {
        var hospital = new Hospital(0, 0);
        var rules = DiseaseRules.Default;
        hospital.DiscoverRoom("gp");

        hospital.UpdatePriceLevel(rules);
        Assert.Equal(100, hospital.PriceLevel);
        Assert.Equal(0, hospital.PriceAdvices);

        hospital.CaseOf(Gp, rules).PricePercent = 160;
        hospital.UpdatePriceLevel(rules);
        Assert.Equal(160, hospital.PriceLevel);
        Assert.Equal(1, hospital.PriceAdvices);
        Assert.Equal(PriceJudgment.Over, hospital.LastPriceJudgment);
        Assert.Equal(Gp.Id, hospital.LastPriceAdviceDisease);

        hospital.CaseOf(Gp, rules).PricePercent = 55;
        hospital.UpdatePriceLevel(rules);
        Assert.Equal(55, hospital.PriceLevel);
        Assert.Equal(PriceJudgment.Under, hospital.LastPriceJudgment);

        // A known disease whose treatment room is missing does not count; with it and its drug, it does.
        var cold = hospital.CaseOf(Cold, rules);
        cold.Discovered = true;
        cold.PricePercent = 200;
        hospital.UpdatePriceLevel(rules);
        Assert.Equal(55, hospital.PriceLevel);
        hospital.DiscoverRoom("pharmacy");
        hospital.UpdatePriceLevel(rules);
        Assert.Equal(cold.DrugAvailable ? 163 : 55, hospital.PriceLevel);

        // Nor without its drug, until research finds it.
        cold.DrugAvailable = true;
        hospital.UpdatePriceLevel(rules);
        Assert.Equal(163, hospital.PriceLevel); // (55 + 600) × 100 / (100 + 300)
    }

    [Fact]
    public void AnEmptyHospitalsPriceLevelIs100()
    {
        var hospital = new Hospital(0, 0);
        hospital.UpdatePriceLevel(DiseaseRules.Default);

        Assert.Equal(100, hospital.PriceLevel);
        Assert.Equal(0, hospital.PriceAdvices);
    }

    [Fact]
    public void ARefusalOverThePriceDoesNotChangeTheReputation()
    {
        var hospital = new Hospital(0, 0);
        var cold = hospital.CaseOf(Cold, DiseaseRules.Default);

        hospital.UpdateNotCuredCounts(ReputationChange.OverPriced, cold);

        Assert.Equal(Hospital.AverageReputation, hospital.Reputation);
        Assert.Equal(Hospital.AverageReputation, cold.Reputation);
        Assert.Equal(1, hospital.NotPaying);
    }

    [Fact]
    public void SavesKeepTheWalletWhatCompaniesOweAndThePriceLevel()
    {
        var simulation = new Simulation(TestMaps.FromRows("...."), 3, GameContent.Empty, new LevelRules(10_000, [], []));
        var hospital = simulation.Hospitals[0];
        var (_, patient) = AddPatient(simulation);
        patient.Wallet = 12_345;
        hospital.ReceiveForTreatment(0, hospital.CaseOf(Gp, simulation.Rules.Diseases), 250, insuranceCompany: 3);
        hospital.DiscoverRoom("gp");
        hospital.CaseOf(Gp, simulation.Rules.Diseases).PricePercent = 170;
        hospital.UpdatePriceLevel(simulation.Rules.Diseases);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, GameContent.Empty);

        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
        Assert.Equal(250, loaded.Hospitals[0].InsuranceOwed(3));
        Assert.Equal(170, loaded.Hospitals[0].PriceLevel);
        Assert.Contains(loaded.Entities, entity => entity is Humanoid { Behaviour: PatientBehaviour { Wallet: 12_345 } });
    }

    private static (Humanoid Humanoid, PatientBehaviour Patient) AddPatient(Simulation simulation)
    {
        var humanoid = simulation.AddEntity(id => Humanoid.CreatePatient(id, new TilePosition(1, 0), simulation.Random, PatientLook.Standard, 0));
        var patient = new PatientBehaviour();
        patient.SetDisease(Cold, 0);
        humanoid.Behaviour = patient;
        return (humanoid, patient);
    }
}
