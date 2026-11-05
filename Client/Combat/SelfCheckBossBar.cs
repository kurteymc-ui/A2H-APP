namespace AionDPS.Combat;

/// <summary>The meter's boss bar (5 November 2026): when the boss dies at the pace its health fell.</summary>
public static class SelfCheckBossBar
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] boss bar (when the boss dies at the group's pace):");
        var t0 = new DateTime(2026, 11, 5, 20, 0, 0);
        TimeSpan? a = Ui.MainWindow.KillTime(12_600_000, t0, 10_000_000, t0.AddSeconds(10)); // 260,000 a second, 10,000,000 left
        bool ok = a is TimeSpan k && Math.Abs(k.TotalSeconds - 38.46) < 0.1;
        Console.WriteLine($"  -> 2.6M lost in 10 s, 10M left: dies in 38.5 s: {ok} ({a?.TotalSeconds:0.0})");
        bool still = Ui.MainWindow.KillTime(10_000_000, t0, 10_000_000, t0.AddSeconds(10)) is null && Ui.MainWindow.KillTime(9_000_000, t0, 10_000_000, t0.AddSeconds(10)) is null;
        Console.WriteLine($"  -> no fall (or healed): no time: {still}");
        bool brief = Ui.MainWindow.KillTime(10_000_000, t0, 9_000_000, t0.AddSeconds(1)) is null;
        Console.WriteLine($"  -> under 3 s of readings: no time yet: {brief}");
        bool dead = Ui.MainWindow.KillTime(1_000, t0, 0, t0.AddSeconds(5)) == TimeSpan.Zero;
        Console.WriteLine($"  -> dead: 0: {dead}");
        return ok && still && brief && dead;
    }
}
