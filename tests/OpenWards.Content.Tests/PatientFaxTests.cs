using OpenWards.Sim;
using OpenWards.Sim.Calendar;
using OpenWards.Sim.Commands;
using OpenWards.Sim.Diseases;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Entities;
using OpenWards.Sim.People;
using static OpenWards.Content.Tests.PatientFlowTests;

namespace OpenWards.Content.Tests;

/// <summary>
/// The faxes a waiting patient sends (CorsixTH's makeNoDiagnosisRoomFax, makeNoTreatmentRoomFax and UIFax:choice), in a
/// hospital where broken wind is the only disease and the patient's case is hard enough to keep the GP short of a diagnosis.
/// </summary>
public class PatientFaxTests
{
    [Fact]
    public void APatientShortOfADiagnosisAsksAndAGuessedCureSendsThemOnToTreatment()
    {
        var simulation = NewStaffedHospital(Rules());
        BuildPharmacy(simulation);
        var (humanoid, patient) = WaitForQuestion(simulation, PatientQuestion.NoDiagnosisRoom);
        Assert.False(patient.IsDiagnosed);

        Run(simulation, new AnswerPatientCommand(humanoid.Id, PatientAnswer.GuessCure));

        Assert.Equal(PatientQuestion.None, patient.Question);
        Assert.True(patient.IsDiagnosed);
        Assert.Equal(RoomSeek.Treatment, patient.Seeking);
        Assert.NotEqual(PatientPhase.Waiting, patient.Phase);
    }

    [Fact]
    public void WithoutTheTreatmentRoomTheGuessedPatientAsksAgainAndCanBeSentHome()
    {
        var simulation = NewStaffedHospital(Rules());
        var (humanoid, patient) = WaitForQuestion(simulation, PatientQuestion.NoDiagnosisRoom);
        Run(simulation, new AnswerPatientCommand(humanoid.Id, PatientAnswer.GuessCure));
        Assert.Equal(PatientQuestion.NoTreatmentRoom, patient.Question);

        // Guessing only answers a diagnosis question.
        Run(simulation, new AnswerPatientCommand(humanoid.Id, PatientAnswer.GuessCure));
        Assert.Equal(PatientQuestion.NoTreatmentRoom, patient.Question);

        var reputation = simulation.Hospitals[0].Reputation;
        Run(simulation, new AnswerPatientCommand(humanoid.Id, PatientAnswer.SendHome));

        Assert.Equal(PatientPhase.GoingHome, patient.Phase);
        Assert.Equal(PatientQuestion.None, patient.Question);
        Assert.True(simulation.Hospitals[0].Reputation <= reputation);
    }

    [Fact]
    public void APatientToldToWaitStaysOnWithoutAsking()
    {
        var simulation = NewStaffedHospital(Rules());
        var (humanoid, patient) = WaitForQuestion(simulation);

        Run(simulation, new AnswerPatientCommand(humanoid.Id, PatientAnswer.Wait));

        Assert.Equal(PatientPhase.Waiting, patient.Phase);
        Assert.Equal(PatientQuestion.None, patient.Question);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream, BaseGameContent.Create());
        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());

        // Told to wait, their wait starts again: they stay until the leave count runs over LeaveMax (150 here).
        Assert.Equal(0, patient.LeaveCount);
        StepFor(simulation, 20 * GameDate.HoursPerDay);
        Assert.Equal(PatientPhase.Waiting, patient.Phase);
        Assert.Equal(PatientQuestion.None, patient.Question);
        for (var tick = 0; tick < 100 * GameDate.HoursPerDay && patient.Phase == PatientPhase.Waiting; tick++)
        {
            simulation.Step();
        }

        Assert.Equal(PatientPhase.GoingHome, patient.Phase);
    }

    [Fact]
    public void AWaitingPatientLooksAtTheWatchYawnsAndTapsAFoot()
    {
        var simulation = NewStaffedHospital(Rules());
        var (humanoid, patient) = WaitForQuestion(simulation);
        Run(simulation, new AnswerPatientCommand(humanoid.Id, PatientAnswer.Wait));

        // Impatience: as the leave count passes a half, five eighths and three quarters of LeaveMax.
        var seen = new List<PatientSpotAction>();
        var animations = new HashSet<int>();
        for (var tick = 0; tick < 100 * GameDate.HoursPerDay && patient.Phase == PatientPhase.Waiting; tick++)
        {
            simulation.Step();
            if (patient.SpotAction is PatientSpotAction.CheckWatch or PatientSpotAction.Yawn or PatientSpotAction.TapFoot
                && (seen.Count == 0 || seen[^1] != patient.SpotAction))
            {
                seen.Add(patient.SpotAction);
            }

            if (patient.SpotAction is PatientSpotAction.CheckWatch or PatientSpotAction.Yawn or PatientSpotAction.TapFoot && humanoid.ActionAnimation is { } animation)
            {
                animations.Add(animation.Id);
            }
        }

        Assert.Equal([PatientSpotAction.CheckWatch, PatientSpotAction.Yawn, PatientSpotAction.TapFoot], seen);
        var expected = BaseGameContent.PatientAnimations[humanoid.Type];
        Assert.Equal(new[] { expected.CheckWatch!.Value.Id, expected.Yawn!.Value.Id, expected.TapFoot!.Value.Id }.ToHashSet(), animations);
    }

    /// <summary>
    /// Creates patients one at a time until one asks (the question asked, if given): how far the GP gets is random, and a
    /// patient fully diagnosed asks no diagnosis question.
    /// </summary>
    private static (Humanoid Humanoid, PatientBehaviour Patient) WaitForQuestion(Simulation simulation, PatientQuestion? wanted = null)
    {
        // Never guessing leaves the decision to the player.
        Run(simulation, new SetPolicyCommand(PolicyKind.GuessCure, HospitalPolicies.MaxFraction));
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var before = simulation.Entities.OfType<Humanoid>().Select(h => h.Id).ToHashSet();
            Run(simulation, new CheatCommand(CheatKind.CreatePatient));
            var humanoid = simulation.Entities.OfType<Humanoid>().Single(h => h.Behaviour is PatientBehaviour && !before.Contains(h.Id));
            var patient = (PatientBehaviour)humanoid.Behaviour!;

            // A hard case keeps the GP's diagnosis short of 100 %.
            patient.DiagnosisDifficulty = 600;
            for (var tick = 0; tick < 20 * GameDate.HoursPerDay && patient.Question == PatientQuestion.None && patient.Phase != PatientPhase.GoingHome; tick++)
            {
                simulation.Step();
            }

            if (patient.Question != PatientQuestion.None && (wanted is null || patient.Question == wanted))
            {
                return (humanoid, patient);
            }

            patient.SendHome(simulation, humanoid);
        }

        throw new Xunit.Sdk.XunitException("No patient asked.");
    }

    private static LevelRules Rules()
    {
        var nonVisuals = new int[DiseaseRules.NonVisualsCount];
        nonVisuals[DiseaseCatalog.Find("broken_wind")!.NonVisualsId!.Value] = 1;
        var diseases = new DiseaseRules(
            Enumerable.Repeat(100, DiseaseRules.ExpertiseCount + 1).ToArray(),
            Enumerable.Repeat(true, DiseaseRules.ExpertiseCount + 1).ToArray(),
            Enumerable.Repeat(0, DiseaseRules.ExpertiseCount + 1).ToArray(),
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
