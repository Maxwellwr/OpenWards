using OpenWards.Sim.Economy;
using OpenWards.Sim.Staff;

namespace OpenWards.Sim.Tests;

/// <summary>What lessons add up to (the logic specification's training).</summary>
public class TrainingTests
{
    [Fact]
    public void AStudentGainsTheAbilityOncePastTheThresholdAndTheWindowShowsPointsOverIt()
    {
        var rules = new TrainingRules(40, 75, 60, 45, 10, 15, 20);
        var profile = NewDoctor(300);

        // +5 a lesson: 75 is not past the threshold of 75, the 16th lesson is.
        for (var lesson = 0; lesson < 15; lesson++)
        {
            Assert.False(profile.TrainSpecialism(Specialism.Surgeon, 5, rules.SurgeonThreshold));
        }

        Assert.Equal(1m, profile.SpecialismProgress(Specialism.Surgeon, rules));
        Assert.False(profile.IsSurgeon);
        Assert.True(profile.TrainSpecialism(Specialism.Surgeon, 5, rules.SurgeonThreshold));
        Assert.True(profile.IsSurgeon);
        Assert.Equal(0, profile.SpecialismPoints(Specialism.Surgeon));
        Assert.False(profile.TrainSpecialism(Specialism.Surgeon, 5, rules.SurgeonThreshold));

        profile.TrainSpecialism(Specialism.Psychiatrist, 15, rules.PsychiatristThreshold);
        Assert.Equal(0.25m, profile.SpecialismProgress(Specialism.Psychiatrist, rules));
    }

    [Fact]
    public void TheSkillStaysWithinThirtyAndAThousand()
    {
        var profile = NewDoctor(10);
        profile.TrainSkill(1);
        Assert.Equal(StaffProfile.MinProcedureSkill, profile.Skill);
        profile.TrainSkill(2000);
        Assert.Equal(StaffProfile.MaxSkill, profile.Skill);
    }

    [Fact]
    public void ALevelWithoutThresholdsUsesOneHundredAndFifty()
    {
        Assert.Equal((150, 150, 150), (TrainingRules.Default.Threshold(Specialism.Surgeon), TrainingRules.Default.Threshold(Specialism.Psychiatrist), TrainingRules.Default.Threshold(Specialism.Researcher)));
        Assert.Equal((10, 15, 20), (Training.Value(TrainingRules.Default, Training.Projector), Training.Value(TrainingRules.Default, Training.Skeleton), Training.Value(TrainingRules.Default, Training.Bookcase)));
    }

    private static StaffProfile NewDoctor(int skill) => StaffProfile.Import(
        StaffClass.Doctor,
        skill,
        attentionToDetail: 500,
        psychiatrist: false,
        surgeon: false,
        researcher: false,
        wage: 100,
        layer5: 0,
        [0, 0, 0],
        new ImportedStaffName('A', 0, 0, 0, 0),
        LevelRules.Free.Staff,
        new SimRandom(1));
}
