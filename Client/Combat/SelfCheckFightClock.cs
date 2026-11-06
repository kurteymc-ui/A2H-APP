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
        bool wipes = WipeInTheSameRoom();
        bool ok = start && reset && format && colours && perBoss && failed && death && adds && wipes;
        Console.WriteLine($"  -> start kept at the first signal={start}, cleared when the boss leaves combat={reset}, m:ss and h:mm:ss={format}, yellow from 4:00 and red from 4:40={colours}, per-boss rage time={perBoss}, a failed attempt starts the next pull from zero={failed}, also with the automatic reset off (Krao Cave)={wipes}: {(ok ? "OK" : "FAILED")}");
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

    /// <summary>
    /// The Krao Cave run of 4 November 2026: the group wiped on Ultimate Berk three times, revived inside the cave (no map change) and the boss kept
    /// its entity id; the meter kept all four attempts as one fight, its automatic reset being off. Now the next attempt after a wipe starts from zero
    /// whatever that setting is (5 November 2026, the owner: "it should be like that in every raid"): by the boss back at full health, and by the boss
    /// out of combat alive when no reading at full health came. The first hit on the boss with the reset off leaves the trash before on screen; another
    /// boss still being fought (hurt, alive, hit) holds the fight open. The numbers are the recording's (Berk 24,316,500 in a party of four).
    /// </summary>
    private static bool WipeInTheSameRoom()
    {
        var dir = new Aion2EntityDirectory();
        var t0 = new DateTime(2026, 11, 4, 21, 10, 0);
        const int berk = 21505, harcon = 53144, gadioton = 44089, trash = 30001;
        dir.RegisterNpc(berk, 2320171);
        dir.RegisterNpc(harcon, 2320104);
        dir.RegisterNpc(gadioton, 2320108);
        bool known = dir.BossNpcIdOf(berk) == 2320171 && dir.BossNpcIdOf(trash) is null;
        // the bosses before: killed
        dir.HitPoints.Note(harcon, t0.AddSeconds(-90), 18_574_526);
        dir.HitPoints.Note(harcon, t0.AddSeconds(20), 0);
        var recorded = new List<DamageEvent> { new(t0.AddSeconds(10), 1, trash, 900, IsHeal: false), new(t0.AddSeconds(19), 1, harcon, 5_000, IsHeal: false) };
        List<DamageEvent> Hit(double s) => new() { new DamageEvent(t0.AddSeconds(s), 2, berk, 7_900, IsHeal: false) };

        // the first pull: from zero with the automatic reset on, the trash and Harcon stay with it off
        dir.NoteEngaged(berk, t0.AddSeconds(62), exact: true);
        dir.HitPoints.Note(berk, t0.AddSeconds(63), 24_312_032);
        bool firstOn = BossFight.StartsNextAttempt(Hit(63), recorded, dir, afterWipeOnly: false);
        bool firstOff = !BossFight.StartsNextAttempt(Hit(63), recorded, dir, afterWipeOnly: true);
        recorded.AddRange(Hit(63));
        dir.HitPoints.Note(berk, t0.AddSeconds(101), 20_901_738);
        recorded.AddRange(Hit(101));
        bool sameAttempt = !BossFight.StartsNextAttempt(Hit(101.5), recorded, dir, afterWipeOnly: true);

        // the wipe: out of combat at 106 s, back at full health at 107.6 s, pulled again at 120.8 s
        dir.NoteDisengaged(berk, t0.AddSeconds(106.2));
        dir.HitPoints.Note(berk, t0.AddSeconds(107.6), 24_316_500);
        dir.NoteEngaged(berk, t0.AddSeconds(120.9), exact: true);
        bool retry = BossFight.StartsNextAttempt(Hit(120.8), recorded, dir, afterWipeOnly: true)
            && BossFight.StartsNextAttempt(Hit(120.8), recorded, dir, afterWipeOnly: false);

        // the same wipe seen by a player who was dead and far: no reading at full health, only the boss out of combat for 14 s, and no signal that it is
        // in combat again: the pull is its run of hits (three, a moment apart)
        var far = new Aion2EntityDirectory();
        far.RegisterNpc(berk, 2320171);
        far.HitPoints.Note(berk, t0.AddSeconds(63), 24_312_032);
        far.HitPoints.Note(berk, t0.AddSeconds(101), 20_901_738);
        far.NoteDisengaged(berk, t0.AddSeconds(106.2));
        var farPull = new List<DamageEvent>(recorded);
        farPull.AddRange(Hit(120.8));
        farPull.AddRange(Hit(121.4));
        bool retryFar = !BossFight.StartsNextAttempt(Hit(120.8), recorded, far, afterWipeOnly: true)
            && BossFight.NextAttemptStart(Hit(122.1), farPull, far, afterWipeOnly: true) == t0.AddSeconds(120.8);

        // a last hit landing on the boss as it resets (45k, 6 s after it left combat; 6 November 2026) starts nothing: the meter keeps the failed
        // attempt on screen, and the pull after it starts the next one where the game says the boss is in combat again, its first hits kept
        var late = new Aion2EntityDirectory();
        late.RegisterNpc(berk, 2320171);
        late.HitPoints.Note(berk, t0.AddSeconds(63), 24_312_032);
        late.HitPoints.Note(berk, t0.AddSeconds(101), 20_901_738);
        late.NoteDisengaged(berk, t0.AddSeconds(106.2));
        List<DamageEvent> lastBlow = new() { new DamageEvent(t0.AddSeconds(112.4), 4, berk, 45_000, IsHeal: false) };
        bool stragglerKept = !BossFight.StartsNextAttempt(lastBlow, recorded, late, afterWipeOnly: true);
        var withStraggler = new List<DamageEvent>(recorded);
        withStraggler.AddRange(lastBlow);
        withStraggler.AddRange(Hit(130.8)); // the pull's first hit, before the game's signal
        late.NoteEngaged(berk, t0.AddSeconds(130.9), exact: true);
        DateTime? next = BossFight.NextAttemptStart(Hit(131.5), withStraggler, late, afterWipeOnly: true);
        var lateHits = withStraggler.Concat(Hit(131.5)).Where(e => e.TargetObjectId == berk).OrderBy(e => e.Timestamp).ToList();
        IReadOnlyList<DateTime> starts = BossFight.AttemptStarts(late, berk, lateHits, t0.AddSeconds(131.5));
        bool stragglerApart = next is DateTime n && n > t0.AddSeconds(112.4) && n <= t0.AddSeconds(130.8) && starts.Count == 1
            && BossFight.PullAfter(lateHits, t0.AddSeconds(106.2)) is null && BossFight.PullAfter(lateHits.Concat(Hit(132)).ToList(), t0.AddSeconds(106.2)) == t0.AddSeconds(130.8);
        Console.WriteLine($"  -> a last hit on a boss resetting after a wipe stays with the failed attempt; the next starts at the pull: {stragglerKept && stragglerApart} " +
            $"(kept {stragglerKept}, next attempt at {(next is DateTime at ? (at - t0).TotalSeconds.ToString("0.0") : "none")} s)");

        // two bosses at once: Gadioton hurt, alive and hit a moment ago holds the fight open
        dir.HitPoints.Note(gadioton, t0.AddSeconds(60), 18_563_287);
        dir.HitPoints.Note(gadioton, t0.AddSeconds(104), 9_000_000);
        var withOther = new List<DamageEvent>(recorded) { new(t0.AddSeconds(104), 3, gadioton, 6_000, IsHeal: false) };
        bool heldOpen = !BossFight.StartsNextAttempt(Hit(120.8), withOther, dir, afterWipeOnly: true);

        bool ok = known && firstOn && firstOff && sameAttempt && retry && retryFar && heldOpen && stragglerKept && stragglerApart;
        if (!ok)
        {
            Console.WriteLine($"  -> Krao Cave wipes: boss known={known}, first pull with the reset on={firstOn}, kept with it off={firstOff}, same attempt kept={sameAttempt}, "
                + $"after the wipe={retry}, seen from afar={retryFar}, another boss held it open={heldOpen}, last hit kept={stragglerKept}, pull apart={stragglerApart}");
        }

        return ok;
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
