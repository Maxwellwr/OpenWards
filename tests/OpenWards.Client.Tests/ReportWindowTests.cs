using OpenWards.Client.Ui.Windows;
using OpenWards.Sim.Commands;
using OpenWards.Sim.Economy;

namespace OpenWards.Client.Tests;

/// <summary>The cheats, progress report, annual report and level end windows.</summary>
public class ReportWindowTests
{
    [Fact]
    public void ACheatFromTheWindowMarksTheHospital()
    {
        var host = new WindowTestHost();
        var window = new CheatsWindow();
        var balance = host.Simulation.Hospitals[0].Balance;

        host.Click(window, "Money +$10,000");
        Assert.Equal(balance + 10_000, host.Simulation.Hospitals[0].Balance);
        Assert.True(host.Simulation.Hospitals[0].Cheated);

        host.Click(window, "No rest: off");
        Assert.True(host.Simulation.Hospitals[0].ActiveCheats.HasFlag(ToggleCheats.NoRest));
        Assert.Contains(window.Rows(host).SelectMany(row => row.Cells), cell => cell.Text == "No rest: on" && cell.Highlight);
    }

    [Fact]
    public void TheMachineCheatsAreInTheWindow()
    {
        var host = new WindowTestHost();
        var window = new CheatsWindow();

        host.Click(window, "Invulnerable machines: off");
        Assert.True(host.Simulation.Hospitals[0].ActiveCheats.HasFlag(ToggleCheats.InvulnerableMachines));
        host.Click(window, "Repair all machines");
        Assert.Contains(host.Submitted, c => c is CheatCommand { Cheat: CheatKind.RepairAllMachines });
    }

    [Fact]
    public void TheMachinesListSaysWhenThereAreNone()
    {
        var host = new WindowTestHost();

        Assert.Contains("The hospital has no machines.", host.Lines(new MachineMenuWindow()));
    }

    [Theory]
    [InlineData(480, 1, 4)]
    [InlineData(720, 1, 6)]
    [InlineData(1080, 1, 9)]
    [InlineData(540, 2, 9)]
    [InlineData(270, 4, 6)]
    public void TheMachinesListHasRowsForAQuarterOfTheScreen(int height, int scale, int rows) =>
        Assert.Equal(rows, MachineMenuWindow.RowsFor(height, scale));

    [Fact]
    public void TheMachinesListScrollbarFollowsCorsixTH()
    {
        var bar = new Microsoft.Xna.Framework.Rectangle(446, 45, 24, 32 * 4);

        // All machines fit: the slider fills the bar.
        Assert.Equal((1, new Microsoft.Xna.Framework.Rectangle(447, 46, 22, 126)), MachineMenuWindow.ScrollbarRange(bar, 3, 4, 1));

        // Ten machines, four shown: the slider is 51 high, and the value stops at the last page.
        var (value, slider) = MachineMenuWindow.ScrollbarRange(bar, 10, 4, 9);
        Assert.Equal(7, value);
        Assert.Equal(51, slider.Height);
        Assert.Equal(46 + 75 - 11, slider.Y);
        Assert.Equal(1, MachineMenuWindow.ScrollbarValueAt(bar, 10, 4, 0));
        Assert.Equal(7, MachineMenuWindow.ScrollbarValueAt(bar, 10, 4, 500));
    }

    [Fact]
    public void TheProgressReportSaysWhenALevelHasNoGoals()
    {
        var host = new WindowTestHost();

        Assert.Equal(["This level has no goals."], host.Lines(new ProgressReportWindow()));
    }

    [Fact]
    public void WinningOffersTheNextLevel()
    {
        var host = new WindowTestHost();
        var window = new LevelEndWindow();
        host.Submit(new CheatCommand(CheatKind.WinLevel));
        Assert.Equal(EndOutcome.Won, host.Simulation.Hospitals[0].Outcome);

        host.Click(window, "Next level");

        Assert.True(host.Started);
    }

    [Fact]
    public void TheAnnualReportListsTheYearsFigures()
    {
        var host = new WindowTestHost();

        var lines = host.Lines(new AnnualReportWindow());

        Assert.Contains("No trophies or awards this year.", lines);
        Assert.Contains(lines, line => line.StartsWith("Hospital value|", StringComparison.Ordinal));
    }
}
