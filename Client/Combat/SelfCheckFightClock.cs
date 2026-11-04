using System.Windows.Media;
using AionDPS.Aion2;
using AionDPS.Ui;

namespace AionDPS.Combat;

/// <summary>The fight clock of the boss bar: it starts with the game's combat signal (also when someone only stood too close), not with the first hit.</summary>
public static class SelfCheckFightClock
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Fight clock of the boss bar:");
        var dir = new Aion2EntityDirectory();
        var t0 = new DateTime(2026, 10, 14, 20, 0, 0);
        dir.NoteEngaged(777, t0);
        dir.NoteEngaged(777, t0.AddSeconds(30)); // the second signal does not move the start
        bool start = dir.EngagedSince(777) == t0 && dir.EngagedSince(778) is null;
        dir.NoteDisengaged(777);
        bool reset = dir.EngagedSince(777) is null;
        dir.NoteEngaged(779, t0);
        dir.NoteEngaged(779, t0.AddSeconds(5), exact: true); // the signal with the start time wins over the plain flag
        reset &= dir.EngagedSince(779) == t0.AddSeconds(5);

        bool format = HpBarView.FormatElapsed(TimeSpan.FromSeconds(151)) == "02:31"
            && HpBarView.FormatElapsed(TimeSpan.FromSeconds(5)) == "00:05"
            && HpBarView.FormatElapsed(TimeSpan.FromSeconds(3725)) == "1:02:05";
        bool colours = ReferenceEquals(HpBarView.ElapsedBrush(TimeSpan.FromSeconds(239)), OverlayPictures.Muted)
            && ReferenceEquals(HpBarView.ElapsedBrush(TimeSpan.FromSeconds(240)), OverlayPictures.Amber)
            && ReferenceEquals(HpBarView.ElapsedBrush(TimeSpan.FromSeconds(279)), OverlayPictures.Amber)
            && ReferenceEquals(HpBarView.ElapsedBrush(TimeSpan.FromSeconds(280)), OverlayPictures.Red);
        bool perBoss = ReferenceEquals(HpBarView.ElapsedBrush(TimeSpan.FromSeconds(149), 210), OverlayPictures.Muted)
            && ReferenceEquals(HpBarView.ElapsedBrush(TimeSpan.FromSeconds(150), 210), OverlayPictures.Amber)
            && ReferenceEquals(HpBarView.ElapsedBrush(TimeSpan.FromSeconds(190), 210), OverlayPictures.Red)
            && ReferenceEquals(HpBarView.ElapsedBrush(TimeSpan.FromSeconds(430), 480), OverlayPictures.Amber)
            && AionDPS.Aion2.Protocol.Aion2BossRage.SecondsFor(2300582) == 210 && AionDPS.Aion2.Protocol.Aion2BossRage.SecondsFor(1) == 300;
        bool failed = FailedAttempts();
        bool death = EndsAtDeath();
        bool adds = OnlyAdds();
        bool ok = start && reset && format && colours && perBoss && failed && death && adds;
        Console.WriteLine($"  -> start kept at the first signal={start}, cleared when the boss leaves combat={reset}, m:ss and h:mm:ss={format}, yellow from 4:00 and red from 4:40={colours}, per-boss rage time={perBoss}, a failed attempt starts the next pull from zero={failed}: {(ok ? "OK" : "FAILED")}");
        return ok;
    }

    /// <summary>A boss that left combat alive and stayed out (a wipe) ended a failed attempt, so the next hit on it starts the meter from zero,
    /// even when no reading at full health came; the game's own short flicker (37 ms on a world boss, 2 November 2026) and a boss dying as it
    /// leaves combat are no failure. (3 November 2026, the owner: a failed fight must not count into the next one.)</summary>
    private static bool FailedAttempts()
    {
        var dir = new Aion2EntityDirectory();
        var t0 = new DateTime(2026, 11, 3, 21, 0, 0);
        // a quick wipe: the boss at 95 %, out of combat for 40 s, then pulled again (no reading at full health in between)
        dir.HitPoints.Note(900, t0, 1000);
        dir.HitPoints.Note(900, t0.AddSeconds(10), 950);
        dir.NoteEngaged(900, t0.AddSeconds(1));
        dir.NoteDisengaged(900, t0.AddSeconds(20));
        bool open = dir.FailedEndOf(900, t0.AddSeconds(22)) is null && dir.FailedEndOf(900, t0.AddSeconds(26)) == t0.AddSeconds(20);
        dir.NoteEngaged(900, t0.AddSeconds(60));
        bool closed = dir.FailedEndsOf(900, t0.AddSeconds(70)) is { Count: 1 } ends && ends[0] == t0.AddSeconds(20)
            && dir.HitPoints.ResetsOf(900).Count == 0; // the health readings alone could not tell
        // the game's flicker, and a boss dying as it leaves combat
        dir.HitPoints.Note(901, t0, 1000);
        dir.HitPoints.Note(901, t0.AddSeconds(5), 400);
        dir.NoteDisengaged(901, t0.AddSeconds(6));
        dir.NoteEngaged(901, t0.AddSeconds(6.037));
        dir.HitPoints.Note(902, t0, 1000);
        dir.HitPoints.Note(902, t0.AddSeconds(5), 100);
        dir.NoteDisengaged(902, t0.AddSeconds(6));
        dir.HitPoints.Note(902, t0.AddSeconds(6.1), 0);
        bool none = dir.FailedEndsOf(901, t0.AddSeconds(30)).Count == 0 && dir.FailedEndOf(902, t0.AddSeconds(30)) is null;
        // the next pull: the first hit after the failed attempt starts from zero; another hit in the same attempt does not
        var recorded = new List<DamageEvent> { new(t0.AddSeconds(10), 1, 900, 50, IsHeal: false) };
        DateTime? lastFailed(int id) => dir.FailedEndOf(id, t0.AddSeconds(61));
        bool pull = BossFight.StartsNewPull(new List<DamageEvent> { new(t0.AddSeconds(61), 1, 900, 70, IsHeal: false) }, recorded, id => id == 900, lastFailed, _ => false);
        var sameAttempt = new List<DamageEvent> { new(t0.AddSeconds(15), 1, 900, 50, IsHeal: false) };
        bool keep = !BossFight.StartsNewPull(sameAttempt, recorded, id => id == 900, _ => null, _ => false);
        return open && closed && none && pull && keep;
    }

    /// <summary>A boss fight runs to the last hit on the boss, the blows the game reports just after the kill included, as the other meters count it
    /// (5 November 2026: three fights of a party of five matched to the hit; until then the fight ended at the boss's death, 0.1-0.9 % short).</summary>
    private static bool EndsAtDeath()
    {
        var t0 = new DateTime(2026, 11, 4, 12, 21, 0);
        var hits = new List<DamageEvent>
        {
            new(t0, 1, 900, 1000, IsHeal: false), new(t0.AddSeconds(99.19), 2, 900, 500, IsHeal: false),
            new(t0.AddSeconds(99.25), 1, 900, 10_000, IsHeal: false), new(t0.AddSeconds(99.56), 3, 900, 6_000, IsHeal: false),
        };
        var shown = BossFight.ShownHits(hits, 900, null, null, id => id == 900);
        bool meter = shown.Count == 4 && Math.Abs(BossFight.SecondsOnTarget(shown, 900) - 99.56) < 1e-6;
        Console.WriteLine($"  -> the fight runs to the last hit on the boss, those just after the kill included: {meter}");
        return meter;
    }

    /// <summary>
    /// A boss fight counts the hits on the boss (and on another boss fought with it), not its adds nor the monsters around it - the boss's DPS as the
    /// other meters show it (5 November 2026; until then the adds counted too). Made-up fight: boss 900 pulled at 0 s; monster 901 was there before; 902
    /// appears with the pull (an add); 905 is a second boss of the encounter.
    /// </summary>
    private static bool OnlyAdds()
    {
        var t0 = new DateTime(2026, 11, 4, 17, 0, 0);
        var hits = new List<DamageEvent>
        {
            new(t0, 1, 900, 5000, IsHeal: false), new(t0.AddSeconds(5), 1, 901, 700, IsHeal: false), new(t0.AddSeconds(6), 1, 902, 800, IsHeal: false),
            new(t0.AddSeconds(25), 1, 905, 900, IsHeal: false), new(t0.AddSeconds(30), 1, 900, 6000, IsHeal: false), new(t0.AddSeconds(31), 1, 905, 400, IsHeal: false),
        };
        var shown = BossFight.ShownHits(hits, 900, null, null, id => id is 900 or 905);
        bool ok = shown.Select(h => h.TargetObjectId).Distinct().OrderBy(i => i).SequenceEqual(new[] { 900, 905 }) && shown.Sum(h => h.Amount) == 11_900;
        Console.WriteLine($"  -> a boss fight counts the boss (and another boss within its fight), not its adds nor the monsters around it: {string.Join(", ", shown.Select(h => h.TargetObjectId).Distinct())}: {ok}");
        return ok;
    }
}
