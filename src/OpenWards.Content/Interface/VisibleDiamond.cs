// The camera limit, ported from CorsixTH CorsixTH/Lua/game_ui.lua (GameUI:makeVisibleDiamond,
// GameUI:calculateMinimumZoom, GameUI.limitPointToDiamond).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

namespace OpenWards.Content.Interface;

/// <summary>
/// The region the screen's top-left corner may be in so that a square map of <c>N</c>×<c>N</c> tiles covers all of the
/// screen (<c>GameUI:makeVisibleDiamond</c>). World coordinates are map pixels: tile (x, y) has its top corner at
/// (32·(x − y), 16·(x + y)). The diamond's vertices are at (X ± W, Y) and (X, Y ± H), with W = 2·H.
/// </summary>
public readonly record struct VisibleDiamond(double X, double Y, double W, double H)
{
    /// <summary>
    /// The diamond for a map <paramref name="mapSize"/> tiles square seen through a screen of
    /// <paramref name="width"/>×<paramref name="height"/> map pixels (the window's size divided by the zoom). The map
    /// editor adds a margin so that the map's edges can be seen.
    /// </summary>
    public static VisibleDiamond For(int mapSize, double width, double height, bool mapEditor = false)
    {
        var margin = mapEditor ? Math.Max(width / 2, height) : 0;
        return new VisibleDiamond(
            -width / 2,
            (16.0 * mapSize) - (height / 2),
            (32.0 * mapSize) - height - (width / 2) + margin,
            (16.0 * mapSize) - (height / 2) - (width / 4) + (margin / 2));
    }

    /// <summary>
    /// <c>GameUI:calculateMinimumZoom</c>: the least zoom at which a map <paramref name="mapSize"/> tiles square still
    /// covers a window of <paramref name="width"/>×<paramref name="height"/> pixels, with a little tolerance.
    /// </summary>
    public static double MinimumZoom(int mapSize, int width, int height) => ((width + (2.0 * height)) / (64.0 * mapSize)) + 0.001;

    /// <summary>Whether the diamond is empty: the screen is bigger than the map.</summary>
    public bool IsEmpty => W < 0 || H < 0;

    /// <summary>
    /// <c>GameUI.limitPointToDiamond</c>: a top-left corner outside the diamond moves to the nearest point on its edge,
    /// rounded down; one inside stays where it is.
    /// </summary>
    public (double X, double Y) Limit(double x, double y)
    {
        var rx = x - X;
        var ry = y - Y;
        if (Math.Abs(rx) + (Math.Abs(ry) * 2) <= W)
        {
            return (x, y);
        }

        // The quadrant the point lies in gives (vx, vy), a unit vector perpendicular to the diamond's edge there; the
        // edge's two vertices p1 and p2; and d, the distance from the point to the line along that edge.
        var sqrt5 = Math.Sqrt(1.0 / 5);
        double vx, vy, d;
        double p1x = 0, p1y = 0, p2x = 0, p2y = 0;
        if (rx >= 0 && ry >= 0)
        {
            (p1x, p2y) = (W, H);
            (vx, vy) = (sqrt5, 2 * sqrt5);
            d = (rx * vx) + (ry * vy) - (p1x * vx);
        }
        else if (rx >= 0)
        {
            (p2x, p1y) = (W, -H);
            (vx, vy) = (sqrt5, -2 * sqrt5);
            d = (rx * vx) + (ry * vy) - (p2x * vx);
        }
        else if (ry >= 0)
        {
            (p2x, p1y) = (-W, H);
            (vx, vy) = (-sqrt5, 2 * sqrt5);
            d = (rx * vx) + (ry * vy) - (p2x * vx);
        }
        else
        {
            (p1x, p2y) = (-W, -H);
            (vx, vy) = (-sqrt5, -2 * sqrt5);
            d = (rx * vx) + (ry * vy) - (p1x * vx);
        }

        // Along the edge, the point either lies between the two vertices and moves straight onto the edge, or past one
        // of them and moves to that vertex.
        var p1 = (vx * p1y) - (vy * p1x);
        var p2 = (vx * p2y) - (vy * p2x);
        var pd = (vx * ry) - (vy * rx);
        if (pd < p1)
        {
            (x, y) = (p1x + X, p1y + Y);
        }
        else if (pd > p2)
        {
            (x, y) = (p2x + X, p2y + Y);
        }
        else
        {
            (x, y) = (x - (d * vx), y - (d * vy));
        }

        return (Math.Floor(x), Math.Floor(y));
    }
}
