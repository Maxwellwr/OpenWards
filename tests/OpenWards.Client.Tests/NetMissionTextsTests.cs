using OpenWards.Client.Ui;

namespace OpenWards.Client.Tests;

public sealed class NetMissionTextsTests
{
    [Fact]
    public void FillsPlainSlotsInTurn()
    {
        Assert.Equal("Ruf 500 in 6 Monaten", NetMissionTexts.Fill("Ruf %d in %d Monaten", 500, 6));
    }

    [Fact]
    public void FillsNumberedSlotsByNumber()
    {
        Assert.Equal("In 6 Monaten Ruf 500", NetMissionTexts.Fill("In %2$d Monaten Ruf %1$d", 500, 6));
    }
}
