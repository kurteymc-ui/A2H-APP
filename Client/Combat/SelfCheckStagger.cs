using AionDPS.Ui;

namespace AionDPS.Combat;

/// <summary>
/// Stagger and the fight's moments (Combat/StaggerCredit.cs, Combat/FightMoments.cs, 6 November 2026): each drop of the boss's stagger bar credited to
/// the hit that caused it, the amounts learned from the fight, the breaks; the bursts, drops, breaks and deaths of the sample fight and the details of
/// a stretch of it. The data is made up (the report's sample fight, and a few hits written here).
/// </summary>
public static class SelfCheckStagger
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Stagger and the fight's moments (who staggered, bursts and drops):");
        bool ok = true;

        // three hits at one moment and two readings after them, in the hits' order (5 for the first, 2 for the third: the basic attack between takes
        // nothing off); between those moments a lone hit of each skill, as in a fight, which is how the amounts are learned
        DateTime t0 = new(2026, 11, 6, 20, 0, 0);
        const int boss = 77;
        var hits = new List<DamageEvent>();
        var bar = new List<StaggerEvent> { new(t0, boss, 168, 168, false) };
        int current = 168;
        void Reading(DateTime at, int drop)
        {
            current -= drop;
            bar.Add(new StaggerEvent(at, boss, current, 168, false));
        }

        for (int i = 0; i < 12; i++)
        {
            DateTime at = t0.AddSeconds(1 + i);
            hits.Add(new DamageEvent(at, 1, boss, 1000, false, "Strike", SkillId: 501));     // takes 5 off
            hits.Add(new DamageEvent(at, 2, boss, 900, false, "Basic Attack", SkillId: 900)); // takes nothing off
            hits.Add(new DamageEvent(at, 2, boss, 800, false, "Slam", SkillId: 602));         // takes 2 off
            Reading(at, 5);
            Reading(at, 2);
            hits.Add(new DamageEvent(at.AddSeconds(0.3), 1, boss, 1000, false, "Strike", SkillId: 501));
            Reading(at.AddSeconds(0.3), 5);
            hits.Add(new DamageEvent(at.AddSeconds(0.6), 2, boss, 800, false, "Slam", SkillId: 602));
            Reading(at.AddSeconds(0.6), 2);
            hits.Add(new DamageEvent(at.AddSeconds(0.9), 2, boss, 900, false, "Basic Attack", SkillId: 900));
        }

        bar.Add(new StaggerEvent(t0.AddSeconds(13), boss, 0, 0, true));
        StaggerReport st = StaggerCredit.Build(bar, hits, t0);
        bool credited = st.Total == 168 && st.Of(1)?.Amount == 120 && st.Of(2)?.Amount == 48 && st.ExactPercent > 99.9
            && st.Of(2)!.Skills.Count == 1 && st.Of(2)!.Skills[0].SkillId == 602 && st.Breaks.Count == 1 && st.Breaks[0].Player == 2;
        Console.WriteLine($"  -> each drop of the bar goes to the hit that takes that amount off, in the hits' order (a basic attack between takes none): {credited} " +
            $"(total {st.Total}, player 1 {st.Of(1)?.Amount}, player 2 {st.Of(2)?.Amount}, exact {st.ExactPercent:0.#}%, breaks {st.Breaks.Count} by {st.Breaks.FirstOrDefault()?.Player})");
        ok &= credited;

        // the sample fight: its bar breaks at 14 s; every player's stagger is what their skills took off
        FightReport report = FightReport.Build(FightReportSample.Input());
        bool sample = report.Stagger.HasData && report.Stagger.Breaks.Count >= 1 && Math.Abs(report.Stagger.Breaks[0].At - 14) < 0.5
            && report.Stagger.Players.Count >= 4 && report.Stagger.ExactPercent > 80;
        Console.WriteLine($"  -> the sample fight's bar breaks at 14 s, each player's part credited: {sample} " +
            $"(breaks {string.Join(", ", report.Stagger.Breaks.Select(b => b.At.ToString("0.0")))}, players {report.Stagger.Players.Count}, exact {report.Stagger.ExactPercent:0.#}%)");
        ok &= sample;

        // the moments: the burst while the boss is staggered (14 to 20 s), the drop while it casts (22 to 41 s), the break, the death at 46 s
        var moments = report.Moments;
        bool burst = moments.Any(m => m.Kind == MomentKind.Burst && m.At is > 14 and <= 22 && m.Why.Any(w => w.Contains("staggered")));
        bool drop = moments.Any(m => m.Kind == MomentKind.Drop && m.At is > 22 and < 43);
        bool others = moments.Any(m => m.Kind == MomentKind.Break) && moments.Any(m => m.Kind == MomentKind.Death && Math.Abs(m.At - 46) < 0.5);
        Console.WriteLine($"  -> a burst in the stagger, a drop while the boss casts, the break and the death: {burst && drop && others} " +
            $"({string.Join("; ", moments.Select(m => $"{m.At:0.0} {m.Title}"))})");
        ok &= burst && drop && others;

        // a stretch: its damage is the party's hits in it, every player's skills there
        FightWindow whole = FightMoments.Window(report, 0, report.Seconds);
        FightWindow some = FightMoments.Window(report, 14, 20);
        bool window = whole.Damage == report.Damage && whole.Players.Sum(p => p.Damage) == report.Damage
            && some.Damage > 0 && some.Damage < report.Damage && some.Players.All(p => p.Skills.Count > 0) && some.Breaks == 0
            && FightMoments.Window(report, 13, 15).Breaks == 1;
        Console.WriteLine($"  -> a stretch has the party's hits in it and each player's skills: {window} (whole {whole.Damage:N0} of {report.Damage:N0}, 14-20 s {some.Damage:N0})");
        ok &= window;

        // the live meter: a skull and the count after the name, and while dead how long
        var row = new PlayerRow(5) { Name = "Tsubasa", Deaths = 2, DeadFor = TimeSpan.FromSeconds(72.8) };
        string dead = row.NameDisplay;
        row.DeadFor = null;
        string back = row.NameDisplay;
        row.Deaths = 0;
        bool meter = dead == "Tsubasa  ☠2 1:12" && back == "Tsubasa  ☠2" && row.NameDisplay == "Tsubasa";
        Console.WriteLine($"  -> the meter shows deaths after the name, and how long while dead: {meter} ({dead} / {back})");
        ok &= meter;
        ok &= Checks();
        return ok;
    }

    /// <summary>
    /// Stagger checks (Combat/StaggerChecks.cs), on a made-up boss like the recordings' Ultimate Berk: an 8 s cast shows its bar full and the bar breaks in
    /// time (two players, one hit with a stagger buff: half as much again); late readings after the break; the bar back up and waiting until the next cast
    /// (the check starts with the cast), which runs out with the bar still up (it fills again: failed); a third cast seen live before anybody hit (running,
    /// nothing taken), then past its end (failed); a bar that is always up, broken without a cast.
    /// </summary>
    private static bool Checks()
    {
        DateTime c0 = new(2026, 11, 7, 21, 0, 0);
        const int boss = 4243, mechanic = 1800141;
        var hits = new List<DamageEvent>();
        var windups = new List<WindupRecord> { new(c0, boss, mechanic, 1, 8000, 0, 0, 0, false) };
        var plan = new List<(double At, int Who, int Skill, int Drop)>();
        for (int i = 0; i <= 32; i++)
        {
            if (i < 30)
            {
                plan.Add((0.4 + i * 0.2, 1, 501, 10));
            }

            plan.Add((0.5 + i * 0.2, 2, 602, i == 16 ? 7 : 5));
        }

        int max = plan.Sum(x => x.Drop), current = max;
        var bar = new List<StaggerEvent> { new(c0, boss, max, max, false) };
        foreach ((double at, int who, int skill, int drop) in plan)
        {
            hits.Add(new DamageEvent(c0.AddSeconds(at), who, boss, 1000, false, "Skill", SkillId: skill));
            current -= drop;
            bar.Add(new StaggerEvent(c0.AddSeconds(at), boss, current, max, false));
        }

        double broke = plan[^1].At + 0.05;
        bar.Add(new StaggerEvent(c0.AddSeconds(broke), boss, 0, 0, true));
        bar.Add(new StaggerEvent(c0.AddSeconds(broke + 0.02), boss, 3, max, false)); // late readings after the break, low on the bar: no check
        bar.Add(new StaggerEvent(c0.AddSeconds(broke + 0.1), boss, 0, 0, true));
        bar.Add(new StaggerEvent(c0.AddSeconds(15), boss, max, max, false));       // back up, waiting for the next cast
        windups.Add(new WindupRecord(c0.AddSeconds(22), boss, 1800190, 1, 1500, 0, 0, 0, false)); // a short cast between: no mechanic
        windups.Add(new WindupRecord(c0.AddSeconds(30), boss, mechanic, 1, 8000, 0, 0, 0, false));
        current = max;
        for (int i = 0; i < 20; i++)
        {
            hits.Add(new DamageEvent(c0.AddSeconds(30.5 + i * 0.3), 1, boss, 1000, false, "Skill", SkillId: 501));
            current -= 10;
            bar.Add(new StaggerEvent(c0.AddSeconds(30.5 + i * 0.3), boss, current, max, false));
        }

        bar.Add(new StaggerEvent(c0.AddSeconds(38), boss, max, max, false));       // the cast ended: the bar fills again
        windups.Add(new WindupRecord(c0.AddSeconds(50), boss, mechanic, 1, 8000, 0, 0, 0, false));

        List<StaggerCheck> at52 = StaggerChecks.Find(boss, bar, windups, hits, c0.AddSeconds(52));
        StaggerCheck? first = at52.ElementAtOrDefault(0), second = at52.ElementAtOrDefault(1), third = at52.ElementAtOrDefault(2);
        bool broken = at52.Count == 3 && first is { Outcome: StaggerOutcome.Broken, Mechanic: mechanic, Left: 0 } && Math.Abs(first.Seconds(c0) - broke) < 0.01
            && first.Limit == 8 && first.Credit.Of(1)?.Amount == 300 && first.Credit.Of(2)?.Amount == 167 && first.Credit.Hits.Count(h => h.Boosted) == 1
            && first.Credit.Hits.Single(h => h.Boosted) is { Player: 2, Amount: 7 } && first.Credit.Breaks.Count >= 1;
        bool failed = second is { Outcome: StaggerOutcome.Failed, Mechanic: mechanic } && second.Start == c0.AddSeconds(30) && second.Deadline == c0.AddSeconds(38)
            && second.Left == max - 200 && second.Taken == 200 && second.Credit.Of(1)?.Amount == 200 && second.Credit.Of(2) is null;
        bool running = third is { Outcome: StaggerOutcome.Running, Taken: 0 } && third.Start == c0.AddSeconds(50) && third.TimeLeft(c0.AddSeconds(52)) is double left && Math.Abs(left - 6) < 0.01
            && StaggerChecks.Latest(at52) == third;
        List<StaggerCheck> later = StaggerChecks.Find(boss, bar, windups, hits, c0.AddSeconds(58.8));
        bool ranOut = later.Count == 3 && later[2] is { Outcome: StaggerOutcome.Failed, Left: var all } && all == max && later[2].End == c0.AddSeconds(58);
        Console.WriteLine($"  -> stagger checks: broken in time by two players (one hit with a stagger buff)={broken}; the next cast's check, not broken={failed}; " +
            $"a cast seen live before anybody hit={running}; past its end={ranOut} ({string.Join("; ", at52.Select(c => $"{(c.Start - c0).TotalSeconds:0.#} s {c.Outcome} {c.Taken}/{c.Max}"))})");

        // a bar that is always up: broken without a cast, a check of its own; the bar sitting full after it is none
        var plain = new List<StaggerEvent> { new(c0, boss, 1790, 1800, false) };
        var plainHits = new List<DamageEvent>();
        for (int i = 1; i <= 179; i++)
        {
            plainHits.Add(new DamageEvent(c0.AddSeconds(i * 0.2), 3, boss, 1000, false, "Skill", SkillId: 501));
            plain.Add(new StaggerEvent(c0.AddSeconds(i * 0.2), boss, 1790 - i * 10, 1800, false));
        }

        plain.Add(new StaggerEvent(c0.AddSeconds(36), boss, 0, 0, true));
        plain.Add(new StaggerEvent(c0.AddSeconds(41), boss, 1800, 1800, false));
        List<StaggerCheck> always = StaggerChecks.Find(boss, plain, Array.Empty<WindupRecord>(), plainHits, c0.AddSeconds(45));
        bool up = always.Count == 1 && always[0] is { Outcome: StaggerOutcome.Broken, Mechanic: 0, Deadline: null } && always[0].Credit.Of(3)?.Amount == 1790;
        Console.WriteLine($"  -> a bar that is always up, broken without a cast: one check, the bar full again after it none: {up}");
        return broken && failed && running && ranOut && up;
    }
}
