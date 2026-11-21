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
    public void A_long_part_loses_words_so_that_the_keys_stay()
    {
        var lines = OriginalBottomPanel.HintLines("Room: drag the edges, the middle to move it   Enter: done   Esc: cancel", 22, text => text.Length);

        Assert.Equal(["Room: drag the edges,", "the…   Enter: done", "Esc: cancel"], lines);
    }

    [Fact]
    public void A_remark_in_brackets_at_the_end_of_a_part_stays()
    {
        var lines = OriginalBottomPanel.HintLines("Room: drag the edges to resize it (small)   Enter: done   Esc: cancel", 22, text => text.Length);

        Assert.Equal(["Room: drag the edges…", "(small)   Enter: done", "Esc: cancel"], lines);
    }

    [Fact]
    public void A_hint_that_cannot_be_shortened_is_cut_off_with_an_ellipsis()
    {
        var lines = OriginalBottomPanel.HintLines("aaaa   bbbb   cccc   dddd", 4, text => text.Length);

        Assert.Equal(["aaaa", "bbbb", "cccc…"], lines);
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
