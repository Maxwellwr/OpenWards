// The parcels of the original levels that a player may buy only next to land of their own, from the maintainer's
// specification of the game's logic ("Кто может купить", plot_neighbours.csv): for each listed parcel, the parcels of which
// the player must own one. Parcels not listed may be bought at any time; whether parcels touch on the map does not count.

using OpenWards.Sim.Economy;

namespace OpenWards.Content.Levels;

/// <summary>The neighbour lists of the original levels' parcels (levels 6–12, 20, 22, 23, 30, 33, 34 and 40).</summary>
public static class PlotNeighbours
{
    /// <summary>
    /// The table as the specification gives it: level, parcel, the parcels of which one must be the player's. On level 40
    /// the rows of parcels 7, 10, 11, 25 and 26 were made for another version of the map (there are no parcels 25 and 26),
    /// and on level 7 the row of parcel 7 is for a parcel the map does not have; they are kept as the original has them.
    /// </summary>
    private static readonly (int Level, int Parcel, int[] AnyOf)[] Table =
    [
        (6, 7, [1, 6]),
        (7, 7, [2]),
        (8, 4, [5]),
        (9, 8, [4, 5, 6]),
        (9, 5, [2, 6, 8]),
        (9, 6, [7, 8]),
        (9, 7, [2, 6]),
        (10, 3, [10, 4]),
        (10, 7, [2]),
        (10, 9, [2]),
        (10, 4, [3, 12]),
        (10, 5, [3]),
        (10, 10, [2]),
        (10, 6, [3]),
        (10, 8, [3, 11]),
        (10, 11, [6, 8]),
        (10, 12, [1, 4]),
        (10, 13, [7]),
        (10, 14, [6]),
        (11, 4, [1, 2]),
        (11, 6, [1]),
        (11, 5, [1, 3]),
        (11, 7, [4, 6]),
        (11, 9, [8]),
        (12, 6, [4, 8]),
        (12, 9, [3, 7]),
        (12, 7, [5, 9]),
        (12, 10, [4, 11]),
        (12, 11, [4, 10]),
        (12, 13, [5, 14]),
        (12, 14, [7, 13]),
        (20, 13, [15, 7, 5]),
        (20, 15, [13, 14, 17]),
        (20, 14, [6, 15]),
        (20, 17, [16, 15]),
        (22, 3, [1, 8]),
        (22, 4, [2, 10]),
        (22, 9, [8]),
        (22, 11, [10]),
        (23, 10, [11]),
        (23, 11, [12]),
        (23, 12, [9]),
        (30, 8, [5, 7, 9]),
        (30, 12, [10, 11, 14]),
        (30, 13, [14]),
        (30, 4, [5]),
        (33, 5, [6, 8]),
        (34, 7, [4, 6, 8]),
        (34, 12, [10, 11, 14]),
        (40, 7, [5, 6, 8, 9]),
        (40, 11, [8, 14, 12]),
        (40, 10, [9, 13, 18]),
        (40, 26, [22, 23, 27, 28]),
        (40, 25, [17, 21, 24, 29]),
    ];

    /// <summary>The neighbour lists of original level <paramref name="levelNumber"/>, none for another map.</summary>
    public static IReadOnlyList<ParcelNeighbours> ForLevel(int? levelNumber)
    {
        return levelNumber is { } level
            ? [.. Table.Where(row => row.Level == level).Select(row => new ParcelNeighbours(row.Parcel, row.AnyOf))]
            : [];
    }
}
