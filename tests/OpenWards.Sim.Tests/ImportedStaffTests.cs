using OpenWards.Sim.Economy;
using OpenWards.Sim.Serialization;
using OpenWards.Sim.Staff;

namespace OpenWards.Sim.Tests;

public sealed class ImportedStaffTests
{
    [Fact]
    public void ImportedProfileKeepsItsValuesAndNameThroughSaving()
    {
        var name = new ImportedStaffName('T', 16, 15, 23, 13);
        var profile = StaffProfile.Import(
            StaffClass.Doctor,
            skill: 565,
            attentionToDetail: 700,
            psychiatrist: false,
            surgeon: true,
            researcher: false,
            wage: 161,
            layer5: 4,
            [0, 18, 27],
            name,
            LevelRules.Free.Staff,
            new SimRandom(1));

        using var stream = new MemoryStream();
        profile.Write(new SimWriter(stream));
        stream.Position = 0;
        var read = StaffProfile.Read(new SimReader(stream));

        Assert.Equal(name, read.ImportedName);
        Assert.Equal((565, 700, 161, (byte)4), (read.Skill, read.AttentionToDetail, read.Wage, read.Layer5));
        Assert.True(read.IsSurgeon);
        Assert.Equal(18, read.SpecialismPoints(Specialism.Psychiatrist));

        // The progress shown is the points over the level's threshold: 18 of 150 and 27 of 150.
        Assert.Equal(0.12m, read.SpecialismProgress(Specialism.Psychiatrist, TrainingRules.Default));
        Assert.Equal(0.18m, read.SpecialismProgress(Specialism.Researcher, TrainingRules.Default));
    }
}
