using OpenWards.Client.Ui;
using OpenWards.Client.Ui.Windows;
using OpenWards.Sim.People;
using OpenWards.Sim.Staff;

namespace OpenWards.Client.Tests;

/// <summary>A member of staff's raise request (UIStaffRise).</summary>
public class StaffRiseWindowTests
{
    [Fact]
    public void TheRequestShowsTheComplaintAndGrantingRaisesTheWage()
    {
        var host = new WindowTestHost();
        var doctor = host.Hire(StaffClass.Doctor);
        var wage = doctor.Profile!.Wage;
        doctor.RaiseRequest = 40;
        doctor.RaiseReason = RaiseReason.Underpaid;
        doctor.QuittingIn = 700;
        var window = new StaffRiseWindow(doctor.Id, 40, text: 0);
        host.Open(window);

        Assert.Contains(host.Lines(window), line => line.Contains("and 40 more", StringComparison.Ordinal) && line.Contains("Asks for a rise of", StringComparison.Ordinal));
        Assert.True(window.MustPause);
        Assert.False(window.ClosesWithEsc);
        Assert.False(window.IsDone(host));

        host.Click(window, "Grant");
        Assert.Equal(wage + 40, doctor.Profile.Wage);
        Assert.Equal(0, doctor.RaiseRequest);
        Assert.DoesNotContain(window, host.Opened);
        Assert.Contains("bonusal2.wav", host.Sounds);
        Assert.True(window.IsDone(host));
    }

    [Fact]
    public void RefusingOftenSeesThemGo()
    {
        var host = new WindowTestHost();
        var nurse = host.Hire(StaffClass.Nurse);
        nurse.RaiseRequest = 30;
        nurse.RaiseReason = RaiseReason.Payment;
        nurse.RaiseRequestCount = 1_000_000;
        nurse.QuittingIn = 700;
        var window = new StaffRiseWindow(nurse.Id, 30);
        host.Open(window);
        Assert.Contains(host.Lines(window), line => line.Contains("one-off payment", StringComparison.Ordinal));

        host.Click(window, "Refuse");
        Assert.True(nurse.IsFired);
        Assert.True(window.IsDone(host));
    }

    [Fact]
    public void SomeoneNothingKeepsSaysSo()
    {
        var host = new WindowTestHost();
        var nurse = host.Hire(StaffClass.Nurse);
        nurse.RaiseReason = RaiseReason.DefiniteQuit;
        nurse.QuittingIn = 700;
        var window = new StaffRiseWindow(nurse.Id, 0);
        host.Open(window);

        Assert.False(window.IsDone(host));
        Assert.Contains(host.Lines(window), line => line.Contains("Will quit whatever the answer", StringComparison.Ordinal));
    }

    [Fact]
    public void LuaFormatFillsEachPlaceholderInTurn() =>
        Assert.Equal("a rise of 40 to 440, 100%", StaffRiseWindow.FormatLua("a rise of %d to %d, 100%%", 40, 440));

    [Fact]
    public void EscLeavesTheRequestOpen()
    {
        var layer = new WindowLayer(text => text.Length * 6, 10);
        var info = new InformationWindow(["Hello"]);
        layer.Open(info);
        layer.Open(new StaffRiseWindow(1, 10));

        Assert.True(layer.CloseTop());
        Assert.DoesNotContain(info, layer.Windows);
        Assert.False(layer.CloseTop());
        Assert.Single(layer.Windows);
    }
}
