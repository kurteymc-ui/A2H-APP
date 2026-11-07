using AionDPS.Combat;

namespace AionDPS.Ui;

/// <summary>
/// Made-up stagger checks for the pictures (render-overlay stagger, stagger-failed, stagger-tip): a boss's 8 s cast like Ultimate Berk's "Extinction for
/// Eternity" with a bar of 480; a Gladiator, an Assassin (some hits with its stagger buff), a Ranger and a Sorcerer take it down, a Chanter hits it with
/// skills that take nothing off. Three checks: one a minute and a half before that failed, one 45 s before broken in time, and the one now - running
/// 5.2 s in, or failed when the cast ran out with part of the bar still up.
/// </summary>
internal static class StaggerSample
{
    private const int Boss = 777;
    private const int Max = 480;

    public static readonly IReadOnlyList<StaggerPerson> People = new[]
    {
        new StaggerPerson(1, "Kaivhan", "Gladiator"), new StaggerPerson(2, "Nyxelle", "Assassin"), new StaggerPerson(3, "Seraphyne", "Ranger"),
        new StaggerPerson(4, "Draegor", "Sorcerer"), new StaggerPerson(5, "Lumiael", "Chanter"),
    };

    private enum How
    {
        Running,
        Failed,
        Broken,
    }

    /// <summary>The check now: running 5.2 s in, or failed.</summary>
    public static (StaggerCheck Check, DateTime Now) Make(bool failed)
    {
        var (checks, now) = Series(failed);
        return (checks[^1], now);
    }

    /// <summary>The boss's three checks, oldest first, and the time now.</summary>
    public static (List<StaggerCheck> Checks, DateTime Now) Series(bool failed)
    {
        DateTime t0 = new(2026, 11, 7, 21, 30, 0);
        DateTime now = failed ? t0.AddSeconds(14) : t0.AddSeconds(5.2);
        var windups = new List<WindupRecord>();
        var bar = new List<StaggerEvent>();
        var hits = new List<DamageEvent>();
        Add(t0.AddSeconds(-90), How.Failed, 0.3, windups, bar, hits, now);
        Add(t0.AddSeconds(-45), How.Broken, 0.13, windups, bar, hits, now);
        Add(t0, failed ? How.Failed : How.Running, failed ? 0.18 : 0.35, windups, bar, hits, now);
        List<StaggerCheck> checks = StaggerChecks.Find(Boss, bar, windups, hits, now).Where(c => c.Mechanic != 0).ToList();
        return (checks, now);
    }

    /// <param name="step">The time between two hits that take something off (seconds).</param>
    private static void Add(DateTime t0, How how, double step, List<WindupRecord> windups, List<StaggerEvent> bar, List<DamageEvent> hits, DateTime now)
    {
        windups.Add(new WindupRecord(t0, Boss, 1800141, 1, 8000, 0, 0, 0, false));
        bar.Add(new StaggerEvent(t0, Boss, Max, Max, false));
        // who, skill, what it takes off; in turns
        var turns = new (int Who, int Skill, int Drop)[]
        {
            (1, 11190340, 10), (2, 13070230, 10), (3, 14010250, 10), (4, 15050450, 7), (1, 11170350, 5), (2, 13220121, 10), (3, 14080140, 15), (1, 11390010, 20),
            (2, 13070230, 15), (4, 15050450, 7), (3, 14010250, 10), (1, 11170350, 5),
        };
        int current = Max;
        double at = 0.6;
        int k = 0;
        while (at < 7.9)
        {
            (int who, int skill, int drop) = turns[k++ % turns.Length];
            if (how == How.Failed && current - drop < 53)
            {
                break;
            }

            DateTime when = t0.AddSeconds(at);
            if (how == How.Running && when > now)
            {
                break;
            }

            hits.Add(new DamageEvent(when, who, Boss, 9_000, false, "Skill", SkillId: skill));
            if (k % 3 == 0)
            {
                // the Chanter hits too, with skills that take nothing off the bar: shown faded on their line
                hits.Add(new DamageEvent(when.AddSeconds(0.07), 5, Boss, 6_000, false, "Skill", SkillId: k % 6 == 0 ? 18010000 : 18030000));
            }

            current -= drop;
            if (how == How.Broken && current <= 0)
            {
                bar.Add(new StaggerEvent(when, Boss, 0, Max, true));
                return;
            }

            bar.Add(new StaggerEvent(when, Boss, current, Max, false));
            at += step;
        }

        if (how == How.Failed)
        {
            bar.Add(new StaggerEvent(t0.AddSeconds(8), Boss, Max, Max, false)); // the cast ran out: the bar fills again
        }
    }
}
