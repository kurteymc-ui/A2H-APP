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

    /// <summary>A boss fight ends when the boss died (its first 0 reading): hits reported after it struck a dead boss and are left out, on the meter, in the
    /// upload and in the History (4 Nov 2026: three hits up to 0.37 s after Transcendent Bakarma died stretched the fight and lowered every DPS by 0.4 %).</summary>
    private static bool EndsAtDeath()
    {
        var hp = new Aion2HitPoints();
        var t0 = new DateTime(2026, 11, 4, 12, 21, 0);
        hp.Note(900, t0, 1000);
        hp.Note(900, t0.AddSeconds(50), 300);
        hp.Note(900, t0.AddSeconds(99.19), 0);
        hp.Note(900, t0.AddSeconds(99.5), 0); // the game repeats the 0 reading
        bool died = hp.DiedAt(900) == t0.AddSeconds(99.19) && hp.DiedAt(901) is null;
        var alive = new Aion2HitPoints();
        alive.Note(902, t0, 1000);
        alive.Note(902, t0.AddSeconds(5), 0);
        alive.Note(902, t0.AddSeconds(30), 1000); // back for another attempt: not dead now
        died &= alive.DiedAt(902) is null;

        var hits = new List<DamageEvent>
        {
            new(t0, 1, 900, 1000, IsHeal: false), new(t0.AddSeconds(99.19), 2, 900, 500, IsHeal: false),
            new(t0.AddSeconds(99.25), 1, 900, 10_000, IsHeal: false), new(t0.AddSeconds(99.56), 3, 900, 6_000, IsHeal: false),
        };
        var shown = BossFight.ShownHits(hits, 900, null, null, id => id == 900, (_, _) => false, id => hp.DiedAt(id));
        bool meter = shown.Count == 2 && Math.Abs(BossFight.SecondsOnTarget(shown, 900) - 99.19) < 1e-6
            && BossFight.ShownHits(hits, 900, null, null, id => id == 900, (_, _) => false).Count == 4; // without the death: as before

        var target = new AionDPS.History.TargetHp(1000, new List<HpSample> { new(t0, 1000), new(t0.AddSeconds(50), 300), new(t0.AddSeconds(99.19), 0), new(t0.AddSeconds(99.5), 0) },
            Array.Empty<DateTime>());
        (DateTime from, DateTime to, List<DamageEvent> kept) = AionDPS.History.SessionAnalyzer.AsFought(t0, t0.AddSeconds(99.56), hits, target);
        bool history = from == t0 && to == t0.AddSeconds(99.19) && kept.Count == 2 && kept.Sum(h => h.Amount) == 1500;
        // a boss still alive at the end (a wipe): nothing cut
        var wipe = new AionDPS.History.TargetHp(1000, new List<HpSample> { new(t0, 1000), new(t0.AddSeconds(50), 300) }, Array.Empty<DateTime>());
        history &= AionDPS.History.SessionAnalyzer.AsFought(t0, t0.AddSeconds(99.56), hits, wipe).Hits.Count == 4;

        Console.WriteLine($"  -> the fight ends when the boss died: its death read from the 0 readings={died}, the meter leaves out the hits after it={meter}, so does the History={history}");
        return died && meter && history;
    }

    /// <summary>
    /// A boss fight counts the boss and its adds, not the monsters around it (4 November 2026, the owner: the meter counted random monsters near the boss).
    /// Made-up fight: boss 900 pulled at 0 s; monster 901 of kind 50 was there before; 902 of kind 60 appears with the pull (an add); 903 of kind 50
    /// appears during the fight (a respawn of the place's kind); 904 of kind 70 was there a minute before.
    /// </summary>
    private static bool OnlyAdds()
    {
        var dir = new Aion2EntityDirectory();
        var t0 = new DateTime(2026, 11, 4, 17, 0, 0);
        void Appear(int id, int npc, double at)
        {
            dir.NoteSpawned(id);
            dir.RegisterNpc(id, npc);
            dir.NoteAppeared(id, npc, t0.AddSeconds(at));
        }

        Appear(900, 9_999_001, -120);
        Appear(901, 50, -90);
        Appear(904, 70, -60);
        Appear(902, 60, 1);
        Appear(903, 50, 20);
        var hits = new List<DamageEvent>
        {
            new(t0, 1, 900, 5000, IsHeal: false), new(t0.AddSeconds(5), 1, 901, 700, IsHeal: false), new(t0.AddSeconds(6), 1, 902, 800, IsHeal: false),
            new(t0.AddSeconds(25), 1, 903, 900, IsHeal: false), new(t0.AddSeconds(26), 1, 904, 400, IsHeal: false), new(t0.AddSeconds(30), 1, 900, 6000, IsHeal: false),
        };
        var shown = BossFight.ShownHits(hits, 900, null, null, id => id == 900, dir.IsAddOf);
        bool ok = shown.Select(h => h.TargetObjectId).Distinct().OrderBy(i => i).SequenceEqual(new[] { 900, 902 }) && shown.Sum(h => h.Amount) == 11_800;
        Console.WriteLine($"  -> a boss fight counts the boss and its add, not the monsters around it nor a respawn of theirs: {string.Join(", ", shown.Select(h => h.TargetObjectId).Distinct())}: {ok}");
        return ok;
    }
}
