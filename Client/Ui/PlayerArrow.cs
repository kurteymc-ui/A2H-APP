using AionDPS.Aion2;

namespace AionDPS.Ui;

/// <summary>
/// The player's arrow, one for the whole program (5 November 2026): the map draws it, and the Hidden Cube's distance (<see cref="CubeRange"/>) is measured
/// from it, so the meter's metres and the map never show two different places. Before, the cube line kept an arrow of its own and let its metres change
/// no faster than 30 m/s, so the meter and the map drifted apart after every jump of the position. The arrow is the position guess through
/// <see cref="MarkerMotion"/>: the game's exact report is reached at once, the minimap's readings followed as they come, a step of the guess glided over.
/// </summary>
internal static class PlayerArrow
{
    private static readonly MarkerMotion Motion = new();
    private static readonly object Gate = new();

    /// <summary>One step to now (the map every frame, the cube line every tick: a second step at the same moment changes nothing); where the arrow is.</summary>
    public static (float X, float Y) Step(Aion2EntityDirectory directory, (float X, float Y, DateTime At) position)
    {
        var (vx, vy) = directory.RunOnVelocity;
        lock (Gate)
        {
            var (x, y) = Motion.Step(position.X, position.Y, directory.LocalPosition?.At, DateTime.UtcNow, vx, vy);
            return ((float)x, (float)y);
        }
    }

    /// <summary>Forgets the arrow's place (a new map, the position lost): the next step starts from the guess as it is.</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            Motion.Reset();
        }
    }
}
