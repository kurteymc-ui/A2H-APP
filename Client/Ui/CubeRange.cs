using AionDPS.Aion2;

namespace AionDPS.Ui;

/// <summary>
/// How far the nearest Hidden Cube is, the way the cube finder shows it (the meter's line, the cube note, the parking-sensor sound): measured from where
/// the player's arrow is drawn (<see cref="MarkerMotion"/>: an exact report reached at once, a step of the guess glided over), not from the raw guess, and the
/// metres shown change no faster than <see cref="MaxRate"/>. The raw guess moves in steps of several metres when a monster appears or a report comes; the
/// distance taken from it jumped by as much (42 m, 35 m, 39 m). Now it counts down metre by metre while the player moves, and a correction slides in.
/// An exact position (a skill, a hit, the minimap) still gets there within a few tenths of a second.
/// Metres are shown only while such an exact position is at most <see cref="ExactFor"/> old (3 November 2026: between them the guess from the monsters
/// around was taken as the player's place; walking to a cube with no fight it was 60 m off, and the distance fell from 60 to 1 when the cube was opened).
/// </summary>
internal static class CubeRange
{
    /// <summary>The fastest the shown distance changes (metres a second): above a run, a ride or a flight, below a jump of the guess.</summary>
    public const double MaxRate = 30;

    /// <summary>How old the last exact position (the game's report or a reading of the minimap) may be for the metres to be shown.</summary>
    public static readonly TimeSpan ExactFor = TimeSpan.FromSeconds(3);

    private static readonly MarkerMotion Motion = new();
    private static readonly object Gate = new();
    private static double _shown = double.NaN;
    private static DateTime _shownAt;
    private static (float X, float Y)? _cube;

    /// <summary>
    /// The nearest cube from the drawn position: how far it lies (world units, east and north of the player) and the metres to show; null while the own
    /// position is older than <paramref name="fresh"/> or no cube is in range.
    /// </summary>
    public static (double Dx, double Dy, double Metres)? Nearest(Aion2EntityDirectory directory, IReadOnlyList<(float X, float Y)> cubes, TimeSpan fresh)
    {
        DateTime now = DateTime.UtcNow;
        lock (Gate)
        {
            if (cubes.Count == 0 || directory.BestPosition is not { } me || now - me.At > fresh
                || directory.LocalPosition is not { } exact || now - exact.At > ExactFor)
            {
                Motion.Reset();
                _cube = null;
                return null;
            }

            var (vx, vy) = directory.RunOnVelocity;
            var (x, y) = Motion.Step(me.X, me.Y, directory.LocalPosition?.At, now, vx, vy);
            var nearest = cubes.MinBy(c => (c.X - x) * (c.X - x) + (c.Y - y) * (c.Y - y));
            double dx = nearest.X - x, dy = nearest.Y - y;
            double metres = Math.Sqrt(dx * dx + dy * dy) / 100.0;
            double since = (now - _shownAt).TotalSeconds;
            if (_cube == nearest && double.IsFinite(_shown) && since is >= 0 and < 1)
            {
                double step = MaxRate * since + 0.05;
                metres = Math.Clamp(metres, _shown - step, _shown + step);
            }

            _shown = metres;
            _shownAt = now;
            _cube = nearest;
            return (dx, dy, metres);
        }
    }

    /// <summary>The metres only (see <see cref="Nearest"/>).</summary>
    public static double? Metres(Aion2EntityDirectory directory, IReadOnlyList<(float X, float Y)> cubes, TimeSpan fresh) =>
        Nearest(directory, cubes, fresh)?.Metres;
}
