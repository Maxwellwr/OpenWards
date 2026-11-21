using Microsoft.Xna.Framework;
using OpenWards.Client.Maps;
using OpenWards.Client.Ui.Windows;
using Xunit;

namespace OpenWards.Client.Tests;

public sealed class RoomEditScreenTests
{
    [Fact]
    public void ThePanelGrowsByALineForEachObjectToPlace()
    {
        Assert.Equal(167, RoomEditScreen.Height(0));
        Assert.Equal(167 + 58, RoomEditScreen.Height(2));
    }

    [Fact]
    public void WithoutTheGraphicsTheRoomPanelIsATable()
    {
        var tool = new BuildRoomTool();
        Assert.False(new RoomEditWindow(tool, _ => { }).IsFullscreen(new WindowTestHost()));
        Assert.False(new RoomBuyWindow(tool).IsFullscreen(new WindowTestHost()));
    }

    [Fact]
    public void APressedLineKeepsItsPanelAndDrawsThePressedButtonWhereTheButtonIs()
    {
        // place_objects.lua resize: makeButton(15, 8, ...) and (15, 12, ...) with preservePanel.
        (int, Point)[] first = [(124, new Point(0, 150)), (125, new Point(15, 158)), (156, new Point(0, 175))];
        (int, Point)[] last = [(124, new Point(0, 150)), (156, new Point(0, 175)), (125, new Point(15, 187))];
        (int, Point)[] none = [(124, new Point(0, 150)), (156, new Point(0, 175))];
        Assert.Equal(first, RoomEditScreen.LineSprites(2, 0).ToArray());
        Assert.Equal(last, RoomEditScreen.LineSprites(2, 1).ToArray());
        Assert.Equal(none, RoomEditScreen.LineSprites(2, -1).ToArray());
    }
}
