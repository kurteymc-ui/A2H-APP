using AionDPS.Aion2;

namespace AionDPS.Ui;

/// <summary>
/// How far the nearest Hidden Cube is, the way the cube finder shows it (the meter's line, the cube note, the parking-sensor sound). The cube's place is the
/// game's own (the frame that announces it). The player's is the map's arrow (<see cref="PlayerArrow"/>, one for the whole program), so the meter and the
/// map always agree (5 November 2026, the owner: "don't sync the cube meters away from the map"): the arrow goes to the game's exact report the moment one
/// comes (a skill, a hit, a dash, an effect the player puts on), and between reports it follows the minimap's readings. The game never reports the
/// player's place while he only walks or rides (the owner's recordings of 2 to 4 November 2026: no report at all while he walked up to 9 of 18 cubes), so
/// the metres are marked as the game's ("42 m") for <see cref="ExactFor"/> after a report and as the map's ("~42 m") otherwise.
/// Metres are shown only while the place is known: a report or a minimap reading at most <see cref="LocatedFor"/> old (3 November 2026: between them the
/// guess from the monsters around was taken as the player's place; walking to a cube with no fight it was 60 m off). Before, the line kept an arrow of
/// its own and let its metres change no faster than 30 m/s: the meter and the map showed two different distances.
/// </summary>
internal static class CubeRange
{
    /// <summary>How long after the game's own report of the player's place the metres count as the game's (no "~").</summary>
    public static readonly TimeSpan ExactFor = TimeSpan.FromSeconds(1.5);

    /// <summary>How old the last exact place (the game's report or a reading of the minimap) may be for the metres to be shown.</summary>
    public static readonly TimeSpan LocatedFor = TimeSpan.FromSeconds(3);

    /// <summary>How old the position guess itself may be.</summary>
    private static readonly TimeSpan Fresh = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The nearest cube from the map's arrow: how far it lies (world units, east and north of the player), the metres, and whether they are the game's own
    /// (a report of the player's place in the last <see cref="ExactFor"/>); null while the place is not known or no cube is in range.
    /// </summary>
    public static (double Dx, double Dy, double Metres, bool Exact)? Nearest(Aion2EntityDirectory directory, IReadOnlyList<(float X, float Y)> cubes)
    {
        DateTime now = directory.Clock(); // the directory's own clock (a replay's, the self-test's)
        if (cubes.Count == 0 || directory.BestPosition is not { } me || now - me.At > Fresh
            || directory.LocalPosition is not { } located || now - located.At > LocatedFor)
        {
            return null;
        }

        (float x, float y) = PlayerArrow.Step(directory, me);
        var nearest = cubes.MinBy(c => ((c.X - x) * (c.X - x)) + ((c.Y - y) * (c.Y - y)));
        double dx = nearest.X - x, dy = nearest.Y - y;
        bool exact = directory.ReportedPosition is { } report && now - report.At <= ExactFor;
        return (dx, dy, Math.Sqrt((dx * dx) + (dy * dy)) / 100.0, exact);
    }

    /// <summary>The metres only (see <see cref="Nearest"/>): what the parking-sensor sound goes by.</summary>
    public static double? Metres(Aion2EntityDirectory directory, IReadOnlyList<(float X, float Y)> cubes) => Nearest(directory, cubes)?.Metres;

    /// <summary>"42 m" right after the game's own report of the player's place, "~42 m" from the map's arrow in between.</summary>
    public static string Text((double Dx, double Dy, double Metres, bool Exact) near)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        int metres = Math.Max(1, (int)Math.Round(near.Metres));
        return string.Format(System.Globalization.CultureInfo.CurrentCulture, loc[near.Exact ? "Cubes.Metres" : "Cubes.MetresAbout"], metres);
    }
}
