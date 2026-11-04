using AionDPS.Aion2;

namespace AionDPS.Combat;

/// <summary>
/// The guess of the player's position between the game's own reports (Aion2EntityDirectory.BestPosition): pulled into reach of every monster that
/// appears, pushed out of reach of every one left behind, not thrown across the map by one announced from afar, and moved to a group of them only
/// when nothing near the old guess lives on (the player was moved without a zone frame). Also the direction of the last dash.
/// </summary>
public static class SelfCheckPositionTrack
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Position between the game's reports (followed by the monsters coming and going):");
        var directory = new Aion2EntityDirectory();
        DateTime t0 = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc), now = t0;
        directory.Clock = () => now;
        bool ok = true;

        void Check(string what, bool good, string detail)
        {
            ok &= good;
            Console.WriteLine($"  -> {what}: {detail}: {(good ? "ok" : "FAIL")}");
        }

        string At() => directory.BestPosition is { } p ? $"({p.X / 100:0.0} m, {p.Y / 100:0.0} m)" : "none";
        bool Near(double x, double y, double metres) => directory.BestPosition is { } p && Math.Sqrt((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y)) <= metres * 100;

        // a report at the origin; five seconds later a monster appears 100 m east: he rode there, the guess is pulled to 22 m from it
        directory.NoteLocalPosition(0, 0);
        Check("an exact report", Near(0, 0, 0.01), At());
        now = t0.AddSeconds(5);
        directory.NoteMobPosition(1, 1170, 10000, 0);
        Check("a monster 100 m away appears", Near(7800, 0, 0.5), At());

        // one announced 3 km away: farther than he can have gone, the guess stays
        now = t0.AddSeconds(5.5);
        directory.NoteMobPosition(2, 1170, 300000, 0);
        Check("one announced 3 km away", Near(7800, 0, 0.5), At());

        // the first monster is taken away as out of range: he is at least 25 m from it
        now = t0.AddSeconds(6);
        directory.NoteRemoved(1, Aion2EntityDirectory.RemovedOutOfRange, now);
        Check("the monster left behind", Near(7500, 0, 0.5), At());

        // three monsters appear together far away, nothing near the guess has lived on for 5 s: he was moved there
        directory.NoteRemoved(2, Aion2EntityDirectory.RemovedOutOfRange, now);
        for (int i = 0; i < 3; i++)
        {
            now = t0.AddSeconds(20 + i * 0.5);
            directory.NoteMobPosition(10 + i, 1170, 500000 + i * 1000, 500000);
        }

        Check("moved without a zone frame", Near(500000 + 1000, 500000, 25), At());

        // standing among monsters that move: a group announced far away does not take the guess along
        now = t0.AddSeconds(30);
        directory.NoteLocalPosition(0, 0);
        for (int i = 0; i < 3; i++)
        {
            now = t0.AddSeconds(30.5 + i * 0.5);
            directory.NoteAround(20 + i, 500 * i, 300);
            directory.NoteMobPosition(30 + i, 1170, 400000 + i * 1000, 0);
        }

        Check("a far group while monsters move around him", Near(0, 0, 20), At());

        // right after a report (a teleport, a town) monsters announced 40 m around him: the guess stays within reach of the report
        now = t0.AddSeconds(40);
        directory.NoteLocalPosition(100000, 100000);
        now = t0.AddSeconds(40.2);
        for (int i = 0; i < 6; i++)
        {
            double a = i * Math.PI / 3;
            directory.NoteMobPosition(40 + i, 1170, (float)(100000 + 4000 * Math.Cos(a)), (float)(100000 + 4000 * Math.Sin(a)));
        }

        now = t0.AddSeconds(41);
        Check("monsters all around right after a report", Near(100000, 100000, 10), At()); // without the leash 18 m and more

        // flying: the game says so ten times a second; once a monster ahead told which way, the guess runs on at 12 m/s - and stops when the flight ends
        var fly = new Aion2EntityDirectory();
        DateTime f0 = t0.AddMinutes(10), fnow = f0;
        fly.Clock = () => fnow;
        bool Close((float X, float Y, DateTime At)? p, double x, double y, double metres) => p is { } q && Math.Sqrt((q.X - x) * (q.X - x) + (q.Y - y) * (q.Y - y)) <= metres * 100;
        fly.NoteLocalPosition(0, 0);
        for (double t = 0.1; t <= 2.5 + 1e-9; t += 0.1)
        {
            fnow = f0.AddSeconds(t);
            fly.NoteLocalFlying();
            if (Math.Abs(t - 1.5) < 1e-6)
            {
                fly.NoteMobPosition(900, 1170, 5000, 0); // 50 m east: the guess to 28 m east, the way flown: east
            }
        }

        Check("flying on after a monster told the way", Close(fly.BestPosition, 2800 + 1200, 0, 1.5), fly.BestPosition is { } fp ? $"({fp.X / 100:0.0} m, {fp.Y / 100:0.0} m)" : "none");
        fnow = f0.AddSeconds(8);
        Check("and stopping when the flight ends", Close(fly.BestPosition, 2800 + 1200 * 1.6, 0, 1.5), fly.BestPosition is { } fq ? $"({fq.X / 100:0.0} m, {fq.Y / 100:0.0} m)" : "none");

        // a fast flight (an Abyss flight, a flight buff): the way the corrections went tells the speed too (40 m in 1.9 s: 21 m/s), and the guess runs on with
        // it for as long as the flight lasts (up to 12 s without a correction: high in the air nothing may come into range)
        var fast = new Aion2EntityDirectory();
        DateTime s0 = t0.AddMinutes(15), snow = s0;
        fast.Clock = () => snow;
        fast.NoteLocalPosition(0, 0);
        for (double t = 0.1; t <= 12 + 1e-9; t += 0.1)
        {
            snow = s0.AddSeconds(t);
            fast.NoteLocalFlying();
            if (Math.Abs(t - 2) < 1e-6)
            {
                fast.NoteMobPosition(901, 1170, 6200, 0); // 62 m east: the guess to 40 m east
            }

            if (Math.Abs(t - 3) < 1e-6)
            {
                double speed = 4000 / 1.9;
                Check("a fast flight runs on at its own speed", Close(fast.BestPosition, 4000 + speed, 0, 1.0), fast.BestPosition is { } sp ? $"({sp.X / 100:0.0} m, {sp.Y / 100:0.0} m)" : "none");
                Check("its direction is known", fast.FlightDirection is { } way && Math.Abs(way.X - 1) < 1e-6 && Math.Abs(way.Y) < 1e-6, fast.FlightDirection?.ToString() ?? "none");
                Check("and the speed the map's arrow follows", Math.Abs(fast.RunOnVelocity.X - speed) < 1 && Math.Abs(fast.RunOnVelocity.Y) < 1e-6, $"{fast.RunOnVelocity.X / 100:0.0} m/s");
            }
        }

        Check("and on for 10 s without a correction", Close(fast.BestPosition, 4000 + 4000 / 1.9 * 10, 0, 2.0), fast.BestPosition is { } sq ? $"({sq.X / 100:0.0} m, {sq.Y / 100:0.0} m)" : "none");

        // an object of a kind that comes into range at 60 m (an ore) 150 m away: the guess is pulled to 62 m from it; one going out of range pushes it to 57 m
        var obj = new Aion2EntityDirectory();
        DateTime o0 = t0.AddMinutes(20), onow = o0;
        obj.Clock = () => onow;
        obj.NoteLocalPosition(0, 0);
        onow = o0.AddSeconds(8);
        obj.NoteObjectPlace(700, 1550004, 15000, 0);
        Check("an object of a known reach appears", Close(obj.BestPosition, 15000 - 6200, 0, 0.5), obj.BestPosition is { } op ? $"({op.X / 100:0.0} m, {op.Y / 100:0.0} m)" : "none");
        obj.NoteObjectPlace(701, 1550004, 4000, 0);
        onow = o0.AddSeconds(9);
        obj.NoteObjectGone(701, 1);
        Check("and one goes out of range", Close(obj.BestPosition, 4000 + 5700, 0, 0.5), obj.BestPosition is { } oq ? $"({oq.X / 100:0.0} m, {oq.Y / 100:0.0} m)" : "none");
        obj.NoteObjectPlace(702, 999, 90000, 0);
        Check("a kind of unknown reach changes nothing", Close(obj.BestPosition, 4000 + 5700, 0, 0.5), obj.BestPosition is { } or ? $"({or.X / 100:0.0} m, {or.Y / 100:0.0} m)" : "none");

        // the position frame's angle is the direction of the dash
        directory.NoteLocalFacing(-151.5);
        Check("the direction of the dash", directory.LocalFacing?.Degrees == -151.5, directory.LocalFacing?.Degrees.ToString("0.0") ?? "none");

        ok &= RunMarker();
        return ok;
    }

    /// <summary>The map's arrow (Ui.MarkerMotion), drawn at 60 frames a second: no lag behind a guess that moves smoothly, an exact report at once, a step
    /// of the guess glided over, a teleport taken as it is.</summary>
    private static bool RunMarker()
    {
        Console.WriteLine("[selftest] The map's arrow, frame by frame:");
        bool ok = true;
        void Check(string what, bool good, string detail)
        {
            ok &= good;
            Console.WriteLine($"  -> {what}: {detail}: {(good ? "ok" : "FAIL")}");
        }

        static string M(double units) => $"{units / 100:0.00} m";
        DateTime t0 = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        const double frame = 1.0 / 60;
        var motion = new AionDPS.Ui.MarkerMotion();
        DateTime fix = t0;
        motion.Step(0, 0, fix, t0);

        // a flight running on at 15 m/s (the guess's own speed, Aion2EntityDirectory.RunOnVelocity): the arrow is where the guess is, every frame (it used
        // to trail it by metres)
        double worst = 0;
        int n = 1;
        for (; n <= 120; n++)
        {
            double x = 1500 * n * frame;
            var (sx, sy) = motion.Step(x, 0, fix, t0.AddSeconds(n * frame), 1500, 0);
            worst = Math.Max(worst, Math.Abs(sx - x) + Math.Abs(sy));
        }

        Check("a flight running on: no lag", worst < 1, $"at most {M(worst)} behind");

        // an exact report 10 m to the side: within a tenth of a second the arrow is there, and it did not leap in one frame
        double baseX = 1500 * (n - 1) * frame;
        fix = t0.AddSeconds(n * frame);
        var first = motion.Step(baseX, 1000, fix, fix);
        double after = 0;
        for (int k = 1; k <= 6; k++)
        {
            var (_, ay) = motion.Step(baseX, 1000, fix, fix.AddSeconds(k * frame));
            after = 1000 - ay;
        }

        Check("an exact report 10 m away is reached at once", after < 100 && first.Y < 1000, $"{M(1000 - first.Y)} left after the first frame, {M(after)} after 0.1 s");

        // a report of a step under a metre is taken as it is
        DateTime fix2 = fix.AddSeconds(1);
        var small = motion.Step(baseX + 60, 1000, fix2, fix2);
        Check("a step of 60 cm is taken as it is", Math.Abs(small.X - (baseX + 60)) < 1, $"{M(baseX + 60 - small.X)} left");

        // a correction from the monsters (no new report) 6 m ahead: glided over - a third of it left after a quarter of a second, nothing after 1.5 s
        DateTime c0 = fix2.AddSeconds(1);
        motion.Step(baseX + 60, 1000, fix2, c0);
        double gx = baseX + 660;
        var c1 = motion.Step(gx, 1000, fix2, c0.AddSeconds(frame));
        double quarter = 0, later = 0;
        for (int k = 2; k <= 90; k++)
        {
            var (cx, _) = motion.Step(gx, 1000, fix2, c0.AddSeconds(k * frame));
            if (k == 15)
            {
                quarter = gx - cx;
            }

            later = gx - cx;
        }

        Check("a correction of 6 m is glided over", gx - c1.X > 500 && quarter is > 100 and < 300 && later < 5,
            $"{M(gx - c1.X)} left after one frame, {M(quarter)} after 0.25 s, {M(later)} after 1.5 s");

        // a teleport (a new place 500 m away) is taken at once
        var tp = motion.Step(gx + 50000, 1000, fix2, c0.AddSeconds(2));
        Check("a teleport is taken at once", Math.Abs(tp.X - (gx + 50000)) < 1, $"{M(gx + 50000 - tp.X)} left");
        return ok;
    }
}
