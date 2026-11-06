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
        return ok;
    }
}
