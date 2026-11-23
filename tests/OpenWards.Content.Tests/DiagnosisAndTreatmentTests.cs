using OpenWards.Sim;
using OpenWards.Sim.Commands;
using OpenWards.Sim.Diseases;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Entities;
using OpenWards.Sim.People;
using OpenWards.Sim.Staff;
using static OpenWards.Content.Tests.PatientFlowTests;

namespace OpenWards.Content.Tests;

/// <summary>
/// Diagnosis steps, guesses and the outcome of a treatment by the community specification of the original game, in a
/// hospital where broken wind (cured in the pharmacy) is the only disease.
/// </summary>
public class DiagnosisAndTreatmentTests
{
    [Fact]
    public void TheGpsStepAddsTheDoctorsScoreLessTheDifficultyAndRollsTheDifficultyOnce()
    {
        var (simulation, humanoid, patient) = NewPatient(maxDiagDiff: 700);
        var gp = Room(simulation, "gp");

        PatientCare.GpDealtWith(simulation, gp, humanoid, patient);

        Assert.InRange(patient.DiagnosisDifficulty, 1, 700);
        var difficulty = patient.DiagnosisDifficulty;

        // A top consultant scores at least 1000 + 200 + 250 − 500 − 100 (tired) = 850, at most 1000 + 200 + 100 + 250.
        Assert.InRange(patient.DiagnosisProgress, Math.Min(850 - difficulty, PatientCare.FullDiagnosis), PatientCare.FullDiagnosis);

        patient.IsDiagnosed = false;
        patient.DiagnosisProgress = 0;
        PatientCare.GpDealtWith(simulation, gp, humanoid, patient);
        Assert.Equal(difficulty, patient.DiagnosisDifficulty);
    }

    [Fact]
    public void AStepNeverTakesProgressBackAndAWrongRoomCostsHealth()
    {
        var (simulation, humanoid, patient) = NewPatient(maxDiagDiff: 0);
        var gp = Room(simulation, "gp");
        patient.DiagnosisDifficulty = 5000;
        patient.DiagnosisProgress = 300;

        PatientCare.GpDealtWith(simulation, gp, humanoid, patient);

        Assert.Equal(300, patient.DiagnosisProgress);
        Assert.Equal(0, patient.PendingHealthChange);
    }

    [Fact]
    public void DoctorsGainASkillPointForEachProcedure()
    {
        var (simulation, humanoid, patient) = NewPatient(maxDiagDiff: 0, strongDoctor: false);
        var gp = Room(simulation, "gp");
        var doctor = (Humanoid)simulation.FindEntity(gp.StaffId)!;
        var skill = doctor.Profile!.Skill;

        PatientCare.DealtWith(simulation, gp, humanoid, patient);

        Assert.Equal(Math.Max(skill + 1, StaffProfile.MinProcedureSkill), doctor.Profile.Skill);
    }

    [Theory]
    [InlineData(700, 29)]
    [InlineData(900, 9)]
    public void AGuessIsWrongWithChanceNinetyNineLessThePercentDiagnosed(int progress, int wrongPercent)
    {
        var (simulation, humanoid, patient) = NewPatient(maxDiagDiff: 0);
        var disease = patient.Disease!;
        var wrong = 0;
        const int guesses = 20_000;
        for (var i = 0; i < guesses; i++)
        {
            patient.DiagnosisProgress = progress;
            PatientCare.GuessDiagnosis(simulation, humanoid, patient, disease);
            wrong += patient.IsWrongGuess ? 1 : 0;
        }

        Assert.True(patient.IsDiagnosed);
        Assert.InRange(wrong * 100.0 / guesses, wrongPercent - 1.5, wrongPercent + 1.5);
    }

    [Fact]
    public void ADrugThatFailsCostsHealthAndSendsThePatientBackToTheGp()
    {
        var (simulation, humanoid, patient) = NewPatient(maxDiagDiff: 0);
        var hospital = simulation.Hospitals[0];
        var entry = hospital.CaseOf(patient.Disease!, simulation.Rules.Diseases);
        // rnd(0, 99) < 1 only when both draws are 0, once in 2500.
        entry.CureEffectiveness = 1;
        patient.IsDiagnosed = true;
        patient.Health = 1m;

        PatientCare.TreatDisease(simulation, Room(simulation, "pharmacy"), humanoid, patient, patient.Disease!);

        Assert.False(patient.IsCured);
        Assert.Equal(-(1000 - (10 * 1)), patient.PendingHealthChange);
        Assert.Equal(RoomSeek.Gp, patient.Seeking);
        Assert.True(patient.WillSeekRoom);
        Assert.Equal(0, hospital.Deaths);
    }

    [Fact]
    public void ADrugThatFailsKillsAPatientWithTooLittleHealthLeft()
    {
        var (simulation, humanoid, patient) = NewPatient(maxDiagDiff: 0);
        var hospital = simulation.Hospitals[0];
        hospital.CaseOf(patient.Disease!, simulation.Rules.Diseases).CureEffectiveness = 0;
        patient.IsDiagnosed = true;
        patient.Health = 0.5m;

        PatientCare.TreatDisease(simulation, Room(simulation, "pharmacy"), humanoid, patient, patient.Disease!);

        Assert.False(patient.IsCured);
        Assert.Equal(-1000, patient.PendingHealthChange);
        Assert.Equal(1, hospital.Deaths);
    }

    [Fact]
    public void ADrugCuresWithTheTriangularChanceOfItsEffectiveness()
    {
        var (simulation, humanoid, patient) = NewPatient(maxDiagDiff: 0);
        var hospital = simulation.Hospitals[0];
        hospital.CaseOf(patient.Disease!, simulation.Rules.Diseases).CureEffectiveness = 70;
        var pharmacy = Room(simulation, "pharmacy");
        var cured = 0;
        const int treatments = 10_000;
        for (var i = 0; i < treatments; i++)
        {
            patient.IsDiagnosed = true;
            patient.PendingHealthChange = 0;
            var before = hospital.Cured;
            PatientCare.TreatDisease(simulation, pharmacy, humanoid, patient, patient.Disease!);
            cured += hospital.Cured - before;
        }

        // The specification's table: effectiveness 70 cures 82.6 % of patients.
        Assert.InRange(cured * 100.0 / treatments, 81, 84.2);
    }

    [Fact]
    public void AWrongGuessMakesEvenTheBestDrugKill()
    {
        var (simulation, humanoid, patient) = NewPatient(maxDiagDiff: 0);
        var hospital = simulation.Hospitals[0];
        hospital.CaseOf(patient.Disease!, simulation.Rules.Diseases).CureEffectiveness = 100;
        patient.IsDiagnosed = true;
        patient.IsWrongGuess = true;

        PatientCare.TreatDisease(simulation, Room(simulation, "pharmacy"), humanoid, patient, patient.Disease!);

        Assert.False(patient.IsCured);
        Assert.Equal(-1000, patient.PendingHealthChange);
        Assert.Equal(1, hospital.Deaths);
    }

    [Fact]
    public void AFailedDrugKillsAnEmergencyPatient()
    {
        var (simulation, humanoid, patient) = NewPatient(maxDiagDiff: 0);
        var hospital = simulation.Hospitals[0];
        hospital.CaseOf(patient.Disease!, simulation.Rules.Diseases).CureEffectiveness = 1;
        patient.IsDiagnosed = true;
        patient.Health = 1m;
        typeof(PatientBehaviour).GetProperty(nameof(PatientBehaviour.EmergencyIndex))!.SetValue(patient, 1);

        PatientCare.TreatDisease(simulation, Room(simulation, "pharmacy"), humanoid, patient, patient.Disease!);

        Assert.Equal(-1000, patient.PendingHealthChange);
        Assert.Equal(1, hospital.Deaths);
    }

    [Fact]
    public void WithoutDiagnosisRoomsAPatientDiagnosedFarEnoughHasTheCureGuessed()
    {
        var (simulation, _, patient) = NewPatient(maxDiagDiff: 0);
        patient.DiagnosisDifficulty = 900;
        Run(simulation, new SetPolicyCommand(PolicyKind.SendHome, 0));
        Run(simulation, new SetPolicyCommand(PolicyKind.GuessCure, 100));

        for (var tick = 0; tick < 20 * Sim.Calendar.GameDate.HoursPerDay && !patient.IsDiagnosed; tick++)
        {
            simulation.Step();
        }

        // Short of a full diagnosis, no diagnosis room built, the pharmacy there: the policy guesses the cure.
        Assert.True(patient.IsDiagnosed);
        Assert.InRange(patient.DiagnosisProgress, 100, PatientCare.FullDiagnosis - 1);
        Assert.Equal(RoomSeek.Treatment, patient.Seeking);
    }

    [Fact]
    public void WithoutDiagnosisRoomsAPatientHardlyDiagnosedIsSentHome()
    {
        var (simulation, _, patient) = NewPatient(maxDiagDiff: 0);
        patient.DiagnosisDifficulty = 900;
        Run(simulation, new SetPolicyCommand(PolicyKind.GuessCure, 1000));
        Run(simulation, new SetPolicyCommand(PolicyKind.SendHome, 1000));

        for (var tick = 0; tick < 20 * Sim.Calendar.GameDate.HoursPerDay && patient.Phase != PatientPhase.GoingHome; tick++)
        {
            simulation.Step();
        }

        Assert.Equal(PatientPhase.GoingHome, patient.Phase);
        Assert.False(patient.IsDiagnosed);
    }

    private static (Simulation Simulation, Humanoid Humanoid, PatientBehaviour Patient) NewPatient(int maxDiagDiff, bool strongDoctor = true)
    {
        var simulation = NewStaffedHospital(Rules(maxDiagDiff), weakDoctor: !strongDoctor);
        BuildPharmacy(simulation);
        var gp = Room(simulation, "gp");
        for (var tick = 0; tick < 2000 && !gp.StaffPresent; tick++)
        {
            simulation.Step();
        }

        Assert.True(gp.StaffPresent);
        Run(simulation, new CheatCommand(CheatKind.CreatePatient));
        var humanoid = simulation.Entities.OfType<Humanoid>().Single(h => h.Behaviour is PatientBehaviour);
        return (simulation, humanoid, (PatientBehaviour)humanoid.Behaviour!);
    }

    private static Room Room(Simulation simulation, string type) => simulation.Entities.OfType<Room>().Single(r => r.RoomType == type);

    private static LevelRules Rules(int maxDiagDiff)
    {
        var nonVisuals = new int[DiseaseRules.NonVisualsCount];
        nonVisuals[DiseaseCatalog.Find("broken_wind")!.NonVisualsId!.Value] = 1;
        var diseases = new DiseaseRules(
            Enumerable.Repeat(100, DiseaseRules.ExpertiseCount + 1).ToArray(),
            Enumerable.Repeat(true, DiseaseRules.ExpertiseCount + 1).ToArray(),
            Enumerable.Repeat(maxDiagDiff, DiseaseRules.ExpertiseCount + 1).ToArray(),
            new int[DiseaseRules.VisualsCount],
            nonVisuals,
            new int[DiseaseRules.VisualsCount],
            new int[DiseaseRules.NonVisualsCount],
            0,
            0);
        return new LevelRules(
            1_000_000,
            [KeyValuePair.Create("gp", 2500), KeyValuePair.Create("pharmacy", 2500)],
            [KeyValuePair.Create(ReceptionDesks.ObjectId, 600)],
            diseases: diseases);
    }
}
