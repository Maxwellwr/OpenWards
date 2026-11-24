using OpenWards.Sim.Diseases;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Entities;
using OpenWards.Sim.Maps;

namespace OpenWards.Sim.Tests;

/// <summary>The hospital's reputation, the reputation for each disease, and how they move prices.</summary>
public class ReputationTests
{
    private static readonly DiseaseDefinition Cold = DiseaseCatalog.Find("uncommon_cold")!;
    private static readonly DiseaseDefinition Gp = DiseaseCatalog.Find(DiseaseCatalog.GpDiagnosis)!;

    [Fact]
    public void TheReputationIsTheAverageOutcomeOfThePatients()
    {
        var hospital = new Hospital(0, 0);
        var cold = hospital.CaseOf(Cold, DiseaseRules.Default);
        Assert.Equal((Hospital.AverageReputation, 10_000, 20), (hospital.Reputation, hospital.ReputationScore, hospital.ReputationVisits));
        Assert.Equal((Hospital.AverageReputation, 20), (cold.Reputation, cold.ReputationVisits));
        Assert.Null(hospital.CaseOf(Gp, DiseaseRules.Default).Reputation);

        // The community's example: 100 patients fully diagnosed and cured, ⌊(20 + 200) × 500 / 120⌋.
        for (var i = 0; i < 100; i++)
        {
            hospital.CountPatient(cold, visit: true, points: 2);
        }

        Assert.Equal((916, 916), (hospital.Reputation, cold.Reputation));

        // Nothing wears off: a patient who leaves undiagnosed only dilutes it, ⌊110,000 / 121⌋.
        hospital.CountPatient(cold, visit: true, points: 0);
        Assert.Equal((909, 909), (hospital.Reputation, cold.Reputation));

        // An event without a disease moves only the hospital's.
        hospital.ChangeReputationPercent(-10);
        Assert.Equal((859, 909), (hospital.Reputation, cold.Reputation)); // 12.1 points cut to 12: ⌊104,000 / 121⌋
    }

    [Fact]
    public void EachOutcomePullsTheReputationTowardsItsOwnLevel()
    {
        // Pulled to 1000, 500, 0 and below 0 by +2, +1, 0 and −1 points a visit.
        Assert.Equal(999, After(2, 10_000)); // ⌊10,010,000 / 10,020⌋
        Assert.Equal(500, After(1, 10_000));
        Assert.Equal(0, After(0, 100_000));
        Assert.Equal(0, After(-1, 30));

        static int After(int points, int patients)
        {
            var hospital = new Hospital(0, 0);
            for (var i = 0; i < patients; i++)
            {
                hospital.CountPatient(null, visit: true, points);
            }

            return hospital.Reputation;
        }
    }

    [Fact]
    public void OnlyTheReputationIsHeldWhatIsBeyondItStays()
    {
        var hospital = new Hospital(0, 0);
        for (var i = 0; i < 20; i++)
        {
            hospital.CountPatient(null, visit: false, points: 2);
        }

        // 60 points for 20 visits would be 1500.
        Assert.Equal((Hospital.MaxReputation, 30_000), (hospital.Reputation, hospital.ReputationScore));
        hospital.CountPatient(null, visit: false, points: -10);
        Assert.Equal(Hospital.MaxReputation, hospital.Reputation);
    }

    [Fact]
    public void EventsAreAShareOfTheVisits()
    {
        // With 20 visits 2 % is 0.4 of a point, which is cut to nothing.
        var hospital = new Hospital(0, 0);
        Assert.Equal(0, hospital.ChangeReputationPercent(2));
        Assert.Equal(-50, hospital.ChangeReputationPercent(-10)); // 2 points of 20 visits

        // With 200 visits ±2 % is ±4 points, ±10 in reputation, whatever the reputation is.
        var busy = new Hospital(0, 0);
        for (var i = 0; i < 180; i++)
        {
            busy.CountPatient(null, visit: true, points: 1);
        }

        Assert.Equal(10, busy.ChangeReputationPercent(2));
        Assert.Equal(-10, busy.ChangeReputationPercent(-2));
        Assert.Equal(-5, busy.ChangeReputationPercent(-1));

        // The year end's awards are in reputation and move it by as much.
        Assert.Equal(25, busy.ShiftReputation(25));
    }

    [Fact]
    public void APatientsVisitCountsOnceAndTheirOutcomesAddPoints()
    {
        var simulation = new Simulation(TestMaps.FromRows("."), seed: 3);
        var hospital = simulation.Hospitals[0];
        var humanoid = new Humanoid(1, new TilePosition(0, 0)) { HospitalPlayer = 0 };
        var patient = new People.PatientBehaviour();
        patient.SetDisease(Cold, 0);
        humanoid.Behaviour = patient;
        var (score, visits) = (hospital.ReputationScore, hospital.ReputationVisits);

        People.PatientCare.SetDiagnosisProgress(simulation, humanoid, patient, 700);
        Assert.Equal((score, visits), (hospital.ReputationScore, hospital.ReputationVisits));

        // A complete diagnosis is the first outcome: the visit and a point.
        People.PatientCare.SetDiagnosisProgress(simulation, humanoid, patient, People.PatientCare.FullDiagnosis);
        Assert.Equal((score + 500, visits + 1), (hospital.ReputationScore, hospital.ReputationVisits));
        Assert.True(patient.IsCountedInReputation);

        // The cure adds a point, not another visit; so does the disease's reputation.
        People.PatientCare.CountForReputation(simulation, humanoid, patient, People.PatientCare.CurePoints);
        Assert.Equal((score + 1000, visits + 1), (hospital.ReputationScore, hospital.ReputationVisits));
        Assert.Equal((11_000, 21), (hospital.Casebook[Cold.Id].ReputationScore, hospital.Casebook[Cold.Id].ReputationVisits));

        // A patient who dies undiagnosed: the visit and −1.
        var other = new People.PatientBehaviour();
        other.SetDisease(Cold, 0);
        var otherHumanoid = new Humanoid(2, new TilePosition(0, 0)) { HospitalPlayer = 0 };
        otherHumanoid.Behaviour = other;
        People.PatientCare.RecordDeath(simulation, otherHumanoid, other);
        Assert.Equal((score + 500, visits + 2, 1), (hospital.ReputationScore, hospital.ReputationVisits, hospital.Deaths));
    }

    [Fact]
    public void AVipsReportIsGoodMiddlingOrBad()
    {
        Assert.Equal(VipVisits.GoodReputationPercent, VipVisits.ReputationPercent(-2));
        Assert.Equal(VipVisits.MiddlingReputationPercent, VipVisits.ReputationPercent(-1));
        Assert.Equal(VipVisits.MiddlingReputationPercent, VipVisits.ReputationPercent(VipVisits.PleasedScore));
        Assert.Equal(VipVisits.BadReputationPercent, VipVisits.ReputationPercent(VipVisits.PleasedScore + 1));
    }

    [Fact]
    public void AGoodReputationRaisesPricesAndABadOneDoesNotLowerThem()
    {
        var hospital = new Hospital(0, 0);
        var rules = DiseaseRules.Default;
        var cold = hospital.CaseOf(Cold, rules);

        cold.Reputation = 600;
        Assert.Equal(360, hospital.TreatmentPrice(Cold, rules)); // 300 * 600 / 500
        cold.Reputation = 400;
        Assert.Equal(300, hospital.TreatmentPrice(Cold, rules));

        // A diagnosis goes by the hospital's reputation.
        Assert.Equal(117, new Hospital(0, 0, 586).TreatmentPrice(Gp, rules)); // ⌊100 × 586 / 500⌋
        Assert.Equal(100, new Hospital(0, 0, 100).TreatmentPrice(Gp, rules));
    }

    [Fact]
    public void TheLevelSetsTheStartingReputationAndSavesKeepIt()
    {
        var map = TestMaps.FromRows("....", "....");
        var rules = new LevelRules(1000, [], [], startReputation: 640);
        var original = new Simulation(map, seed: 2, rules: rules);
        var hospital = original.Hospitals[0];
        Assert.Equal(640, hospital.Reputation);

        var cold = hospital.CaseOf(Cold, original.Rules.Diseases);
        hospital.UpdateNotCuredCounts(ReputationChange.Kicked, cold);
        hospital.CountPatient(cold, visit: true, points: 0);

        using var stream = new MemoryStream();
        original.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);

        Assert.Equal(original.ComputeStateHash(), restored.ComputeStateHash());
        var loaded = restored.Hospitals[0];
        Assert.Equal(640, restored.Rules.StartReputation);
        Assert.Equal((609, 12_800, 21, 1), (loaded.Reputation, loaded.ReputationScore, loaded.ReputationVisits, loaded.NotCured)); // ⌊640 × 20 / 21⌋
        Assert.Equal((476, 21, 1), (loaded.Casebook[Cold.Id].Reputation, loaded.Casebook[Cold.Id].ReputationVisits, loaded.Casebook[Cold.Id].TurnedAway));
        Assert.Null(loaded.CaseOf(Gp, restored.Rules.Diseases).Reputation);
    }
}
