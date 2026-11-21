using Microsoft.Xna.Framework;
using OpenWards.Client.Maps;
using Xunit;

namespace OpenWards.Client.Tests;

public sealed class BriefLineTests
{
    private static readonly Point Screen = new(1280, 800);
    private static readonly Rectangle Panel = new(302, 752, 676, 48);

    [Fact]
    public void The_line_sits_centred_just_above_the_bottom_panel()
    {
        var box = MapViewScreen.BriefLineBox(Screen, Panel, [], new Point(200, 20));

        Assert.Equal(new Rectangle(540, 730, 200, 20), box);
    }

    [Fact]
    public void The_line_rises_above_the_fax_icons_it_would_cover()
    {
        var faxes = new Rectangle(303, 728, 150, 28);

        var box = MapViewScreen.BriefLineBox(Screen, Panel, [faxes], new Point(500, 20));

        Assert.Equal(728 - 2 - 20, box.Y);
    }

    [Fact]
    public void Obstacles_beside_the_line_leave_it_where_it_is()
    {
        var faxes = new Rectangle(303, 728, 30, 28);

        var box = MapViewScreen.BriefLineBox(Screen, Panel, [faxes, Rectangle.Empty], new Point(200, 20));

        Assert.Equal(730, box.Y);
    }

    [Fact]
    public void The_line_rises_past_each_obstacle_in_turn()
    {
        var faxes = new Rectangle(303, 728, 150, 28);
        var balloon = new Rectangle(400, 700, 400, 40);

        var box = MapViewScreen.BriefLineBox(Screen, Panel, [balloon, faxes], new Point(500, 20));

        Assert.Equal(700 - 2 - 20, box.Y);
    }

    [Fact]
    public void Without_the_panel_the_line_goes_to_the_bottom_of_the_screen()
    {
        var box = MapViewScreen.BriefLineBox(Screen, null, [], new Point(200, 20));

        Assert.Equal(new Rectangle(540, 800 - 3 - 2 - 20, 200, 20), box);
    }

    [Fact]
    public void A_line_wider_than_the_panel_stays_on_the_screen()
    {
        var box = MapViewScreen.BriefLineBox(new Point(700, 600), new Rectangle(0, 552, 640, 48), [], new Point(690, 20));

        Assert.Equal(0, box.X);
        Assert.True(box.Right <= 700);
    }
}
