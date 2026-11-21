using OpenWards.Content.Interface;

namespace OpenWards.Content.Tests;

public class VisibleDiamondTests
{
    [Fact]
    public void DiamondKeepsTheScreenOverTheMap()
    {
        // A 128-tile map through an 800×600 screen at zoom 1: 32·128 − 600 − 400 wide, half that high.
        var diamond = VisibleDiamond.For(128, 800, 600);
        Assert.Equal(new VisibleDiamond(-400, 1748, 3096, 1548), diamond);
        Assert.False(diamond.IsEmpty);

        // Each corner of the screen at each of the diamond's vertices lies on the map's edge.
        foreach (var (x, y) in new[] { (diamond.X + diamond.W, diamond.Y), (diamond.X - diamond.W, diamond.Y), (diamond.X, diamond.Y + diamond.H), (diamond.X, diamond.Y - diamond.H) })
        {
            foreach (var (cx, cy) in new[] { (x, y), (x + 800, y), (x, y + 600), (x + 800, y + 600) })
            {
                Assert.True(Math.Abs(cx) + (2 * Math.Abs(cy - (16 * 128))) <= (32 * 128) + 1e-9);
            }
        }
    }

    [Fact]
    public void PointsInsideStayAndPointsOutsideMoveToTheEdge()
    {
        var diamond = VisibleDiamond.For(128, 800, 600);
        Assert.Equal((-400.5, 1748.25), diamond.Limit(-400.5, 1748.25));

        // Above the top vertex: to the vertex, as the screen was at the top of the map.
        Assert.Equal((-400, 200), diamond.Limit(-400, -500));

        // Past the top-right edge, between its vertices: straight onto the edge.
        var (x, y) = diamond.Limit(1000, 0);
        Assert.Equal(diamond.W, Math.Abs(x - diamond.X) + (2 * Math.Abs(y - diamond.Y)), 2);
        Assert.True(x < 1000 && y > 0);

        // Far to the right: the right vertex.
        Assert.Equal((2696, 1748), diamond.Limit(10_000, 1748));
    }

    [Fact]
    public void MapEditorHasAMargin()
    {
        var diamond = VisibleDiamond.For(128, 800, 600, mapEditor: true);
        Assert.Equal(3096 + 600, diamond.W);
        Assert.Equal(1548 + 300, diamond.H);
    }

    [Fact]
    public void MinimumZoomFillsTheWindow()
    {
        var zoom = VisibleDiamond.MinimumZoom(128, 3840, 2160);
        Assert.Equal((3840 + 4320) / 8192.0 + 0.001, zoom, 10);
        Assert.False(VisibleDiamond.For(128, 3840 / zoom, 2160 / zoom).IsEmpty);
        Assert.True(VisibleDiamond.For(128, 3840 / 0.9, 2160 / 0.9).IsEmpty);
    }
}
