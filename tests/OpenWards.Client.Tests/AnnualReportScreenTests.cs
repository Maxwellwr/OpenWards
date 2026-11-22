using Microsoft.Xna.Framework;
using OpenWards.Client.Ui.Windows;
using OpenWards.Sim.Economy;
using Xunit;

namespace OpenWards.Client.Tests;

public sealed class AnnualReportScreenTests
{
    [Fact]
    public void TrophiesStandApartFromAwardsAndPenalties()
    {
        Assert.True(AnnualReportScreen.IsTrophy(YearEndAwardKind.DrinksSoldTrophy));
        Assert.True(AnnualReportScreen.IsTrophy(YearEndAwardKind.HappyVips));
        Assert.False(AnnualReportScreen.IsTrophy(YearEndAwardKind.PoorReputation));
        Assert.Equal((27, 26), AnnualReportScreen.Text(YearEndAwardKind.DrinksSoldTrophy));
        Assert.Equal((56, 2), AnnualReportScreen.Text(YearEndAwardKind.PoorReputation));
    }

    [Fact]
    public void AwardsHangThreeOnEachSide()
    {
        Assert.Equal(new Point(16, 74), AnnualReportScreen.AwardPlace(0));
        Assert.Equal(new Point(16, 304), AnnualReportScreen.AwardPlace(2));
        Assert.Equal(new Point(525, 189), AnnualReportScreen.AwardPlace(4));
    }

    [Fact]
    public void TheChartsRankTheComputerOpponentsWithThePlayer()
    {
        var rules = new LevelRules(0, [], [], competitors: [new PlannedCompetitor(1, "ORAC"), new PlannedCompetitor(2, "HAL")]);
        var host = new WindowTestHost(rules);
        var orac = host.Simulation.Competitors[0];
        for (var i = 0; i < 50; i++)
        {
            orac.CountNewPatient();
        }

        var entries = AnnualReportScreen.Entries(host);
        Assert.Equal(host.PlayerName, entries[0].Name);
        Assert.Equal(["ORAC", "HAL"], entries.Skip(1).Select(e => e.Name));
        Assert.Equal(3, entries.Count);
        Assert.Equal("ORAC", AnnualReportScreen.Ranked(entries, e => e.Cured, ascending: false).First().Name);
        Assert.Equal("ORAC", AnnualReportScreen.Ranked(entries, e => e.Visitors, ascending: false).First().Name);
    }

    [Fact]
    public void ThePlayerGoesByTheNameAnImportedSaveBrings()
    {
        var host = new WindowTestHost();
        host.Simulation.Hospitals[0].Name = "MAXWELL";
        Assert.Equal("MAXWELL", AnnualReportScreen.Entries(host)[0].Name);
    }
}
