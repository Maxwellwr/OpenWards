using Microsoft.Xna.Framework;
using OpenWards.Client.Maps;
using OpenWards.Client.Ui;
using Xunit;

namespace OpenWards.Client.Tests;

public sealed class BriefLineTests
{
    [Fact]
    public void A_hint_s_parts_share_the_info_bar_s_lines_while_they_fit()
    {
        var lines = OriginalBottomPanel.HintLines("BUILD MODE   Drag out the room   Esc: cancel", 25, text => text.Length);

        Assert.Equal(["BUILD MODE", "Drag out the room", "Esc: cancel"], lines);
    }

    [Fact]
    public void A_short_hint_takes_one_line()
    {
        Assert.Equal(["Quick saved"], OriginalBottomPanel.HintLines("Quick saved", 25, text => text.Length));
    }

    [Fact]
    public void A_hint_longer_than_three_lines_is_cut_off()
    {
        var lines = OriginalBottomPanel.HintLines("one two three four five six seven eight", 9, text => text.Length);

        Assert.Equal(["one two", "three", "four five"], lines);
    }

    [Fact]
    public void Without_the_panel_the_line_goes_to_the_bottom_middle_of_the_screen()
    {
        var box = MapViewScreen.BriefLineBox(new Point(1280, 800), new Point(200, 20));

        Assert.Equal(new Rectangle(540, 800 - 3 - 2 - 20, 200, 20), box);
    }

    [Fact]
    public void A_line_wider_than_the_screen_starts_at_its_left_edge()
    {
        Assert.Equal(0, MapViewScreen.BriefLineBox(new Point(300, 200), new Point(320, 20)).X);
    }
}
