namespace AionDPS.Ui;

/// <summary>
/// Where the player's arrow is drawn on the map, frame by frame. It is the position guess itself (Aion2EntityDirectory.BestPosition) plus a gap that
/// closes when the guess jumps, so the arrow never lags behind a guess that runs on by itself (a flight, see Aion2EntityDirectory.RunOnVelocity) and
/// never leaps across the map when it steps:
/// - a new exact report (a skill, a hit, a dash) is reached at once: a step of up to a metre is taken as it is, a longer one in about a tenth of a second;
/// - a correction from the monsters around (a step of the guess) is glided over in about half a second (a quarter of a second to two thirds of the way);
/// - a jump of more than 80 m (a teleport, a new map) is taken at once.
/// The map used to glide twice, twenty times a second (the position, then the view): an exact report took half a second to arrive and the arrow trailed
/// a flight by several metres. Replayed on six recordings of 31 October and 1 November 2026 (4,800 exact reports), drawn every frame: the arrow is 2.7 m
/// from where the next report puts the player during flights instead of 5.5 m (median, reports under 0.6 s apart), 1.6 m instead of 1.9 m on the
/// ground, and it changes speed abruptly half as often.
/// </summary>
public sealed class MarkerMotion
{
    /// <summary>How fast the gap after an exact report closes (time constant, seconds): about 95 % within a tenth of a second.</summary>
    public static double ExactTau { get; set; } = 0.04;

    /// <summary>How fast the gap after a correction from the monsters closes (time constant, seconds).</summary>
    public static double GuessTau { get; set; } = 0.25;

    /// <summary>An exact report this close to the guess (world units, 100 per metre) is taken as it is.</summary>
    public static double SmallStep { get; set; } = 100;

    /// <summary>A jump farther than this (world units) is a teleport or a new map: taken at once.</summary>
    public static double Teleport { get; set; } = 8000;

    private (double X, double Y)? _target;
    private double _offsetX, _offsetY;
    private DateTime _at;
    private DateTime? _fixAt;
    private double _tau = GuessTau;

    /// <summary>Where the arrow is now, without stepping (null before the first step).</summary>
    public (double X, double Y)? Shown => _target is { } t ? (t.X + _offsetX, t.Y + _offsetY) : null;

    /// <summary>Forgets the arrow's place: the next step takes the guess as it is (a new map, the position lost).</summary>
    public void Reset()
    {
        _target = null;
        _offsetX = _offsetY = 0;
        _fixAt = null;
        _tau = GuessTau;
    }

    /// <summary>
    /// One frame: <paramref name="x"/>, <paramref name="y"/> is the guess now, <paramref name="fixAt"/> the time of the last exact report (a change of it
    /// means a new one came), <paramref name="now"/> the clock, <paramref name="vx"/>, <paramref name="vy"/> how fast the guess runs on by itself (world
    /// units a second; 0 when it does not): that much of its move is followed as it is, without a glide. Returns where the arrow is drawn.
    /// </summary>
    public (double X, double Y) Step(double x, double y, DateTime? fixAt, DateTime now, double vx = 0, double vy = 0)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            return Shown ?? (0, 0);
        }

        double dt = _target is null ? 0 : Math.Clamp((now - _at).TotalSeconds, 0, 0.5);
        _at = now;
        bool newFix = fixAt is not null && fixAt != _fixAt;
        _fixAt = fixAt;
        if (_target is not { } before)
        {
            _target = (x, y);
            return (x, y);
        }

        double jumpX = x - before.X, jumpY = y - before.Y;
        double jump = Math.Sqrt(jumpX * jumpX + jumpY * jumpY);
        _target = (x, y);
        if (jump > Teleport)
        {
            _offsetX = _offsetY = 0;
            _tau = GuessTau;
            return (x, y);
        }

        // the guess running on by itself (a flight): its move forward, up to its own speed, is followed as it is; only the rest is a step to glide over
        double restX = jumpX, restY = jumpY;
        double speed = Math.Sqrt(vx * vx + vy * vy);
        if (speed > 0 && double.IsFinite(speed) && dt > 0)
        {
            double ux = vx / speed, uy = vy / speed;
            double along = Math.Clamp(jumpX * ux + jumpY * uy, 0, speed * dt * 1.05);
            restX -= along * ux;
            restY -= along * uy;
        }

        if (newFix)
        {
            // an exact report: what is left of earlier glides goes with it, fast
            _tau = ExactTau;
            if (jump > SmallStep)
            {
                _offsetX -= restX;
                _offsetY -= restY;
            }
        }
        else
        {
            _offsetX -= restX;
            _offsetY -= restY;
        }

        double keep = _tau > 0 ? Math.Exp(-dt / _tau) : 0;
        _offsetX *= keep;
        _offsetY *= keep;
        if (_offsetX * _offsetX + _offsetY * _offsetY < 25)
        {
            // closed (under 5 cm): the next correction glides at the slower pace again
            _offsetX = _offsetY = 0;
            _tau = GuessTau;
        }

        return (x + _offsetX, y + _offsetY);
    }
}
