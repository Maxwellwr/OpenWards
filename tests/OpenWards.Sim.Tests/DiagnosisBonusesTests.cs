using OpenWards.Sim.Diseases;

namespace OpenWards.Sim.Tests;

/// <summary>The table of disease × diagnosis room bonuses the maintainer filled in.</summary>
public class DiagnosisBonusesTests
{
    [Fact]
    public void EachDiseaseGetsItsDiagnosisRoomsFromTheTable()
    {
        Assert.Equal(["general_diag", "cardiogram", "scanner", "x_ray", "ward"], DiseaseCatalog.Find("bloaty_head")!.DiagnosisRooms);
        Assert.Equal(["scanner", "ward"], DiseaseCatalog.Find("transparency")!.DiagnosisRooms);

        // Spare ribs lost general diagnosis; alien DNA comes only in emergencies, already diagnosed.
        Assert.DoesNotContain("general_diag", DiseaseCatalog.Find("spare_ribs")!.DiagnosisRooms);
        Assert.Empty(DiseaseCatalog.Find("alien_dna")!.DiagnosisRooms);
    }

    [Fact]
    public void BonusesComeFromTheTableAndTheGpAddsNothing()
    {
        Assert.Equal(400, DiagnosisBonuses.Bonus("bloaty_head", "scanner"));
        Assert.Equal(200, DiagnosisBonuses.Bonus("bloaty_head", "ward"));
        Assert.Equal(-400, DiagnosisBonuses.Bonus("serious_radiation", "x_ray"));
        Assert.Equal(0, DiagnosisBonuses.Bonus("bloaty_head", "gp"));
        Assert.Equal(0, DiagnosisBonuses.Bonus("bloaty_head", "psych"));
    }

    [Fact]
    public void EveryRealDiseaseButAlienDnaHasADiagnosisRoomAndOnlyKnownRoomsAppear()
    {
        string[] rooms = ["general_diag", "cardiogram", "scanner", "ultrascan", "blood_machine", "x_ray", "psych", "ward"];
        foreach (var disease in DiseaseCatalog.All.Where(d => !d.IsPseudo && d.Id != "alien_dna"))
        {
            Assert.NotEmpty(disease.DiagnosisRooms);
            Assert.All(disease.DiagnosisRooms, room => Assert.Contains(room, rooms));
            Assert.All(disease.DiagnosisRooms, room => Assert.Contains(DiagnosisBonuses.Bonus(disease.Id, room), new[] { -400, -200, 0, 200, 400 }));
        }
    }

    [Fact]
    public void TheSpecialDiseasesAreTheMaintainersTenAndNeverContagious()
    {
        string[] special = ["bloaty_head", "hairyitis", "king_complex", "invisibility", "slack_tongue", "alien_dna", "fractured_bones", "baldness", "pregnant", "transparency"];
        Assert.Equal(special.Order(), DiseaseCatalog.All.Where(d => d.IsSpecial).Select(d => d.Id).Order());
        Assert.All(DiseaseCatalog.All.Where(d => d.IsSpecial), d => Assert.False(d.IsContagious));
        Assert.Contains(DiseaseCatalog.All, d => d.IsContagious);
    }
}
