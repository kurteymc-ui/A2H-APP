using AionDPS.Combat;

namespace AionDPS.Ui;

/// <summary>
/// A made-up stagger check for the pictures (render-overlay stagger, stagger-failed, stagger-tip): a boss's 8 s cast like Ultimate Berk's "Extinction for
/// Eternity" with a bar of 480; a Gladiator, an Assassin (one hit with its stagger buff), a Ranger and a Sorcerer take it down, a Chanter hits it with skills that take nothing off.
/// Running 5.2 s in, or failed when the cast ran out with part of the bar still up.
/// </summary>
internal static class StaggerSample
{
    private const int Boss = 777;

    public static readonly IReadOnlyList<StaggerPerson> People = new[]
    {
        new StaggerPerson(1, "Jinwoo", "Gladiator"), new StaggerPerson(2, "Goku", "Assassin"), new StaggerPerson(3, "Ichigo", "Ranger"),
        new StaggerPerson(4, "Naruto", "Sorcerer"), new StaggerPerson(5, "Tsubasa", "Chanter"),
    };

    public static (StaggerCheck Check, DateTime Now) Make(bool failed)
    {
        DateTime t0 = new(2026, 11, 7, 21, 30, 0);
        const int max = 480;
        var windups = new List<WindupRecord> { new(t0, Boss, 1800141, 1, 8000, 0, 0, 0, false) };
        var bar = new List<StaggerEvent> { new(t0, Boss, max, max, false) };
        var hits = new List<DamageEvent>();
        // who, skill, what it takes off; in turns every 0.35 s
        var turns = new (int Who, int Skill, int Drop)[]
        {
            (1, 11190340, 10), (2, 13070230, 10), (3, 14010250, 10), (4, 15050450, 7), (1, 11170350, 5), (2, 13220121, 10), (3, 14080140, 15), (1, 11390010, 20),
            (2, 13070230, 15), (4, 15050450, 7), (3, 14010250, 10), (1, 11170350, 5),
        };
        int current = max;
        double at = 0.6;
        int k = 0;
        while (at < 7.9)
        {
            (int who, int skill, int drop) = turns[k++ % turns.Length];
            if (current - drop < 53 && failed)
            {
                break;
            }

            hits.Add(new DamageEvent(t0.AddSeconds(at), who, Boss, 9_000, false, "Skill", SkillId: skill));
            if (k % 3 == 0)
            {
                // the Chanter hits too, with skills that take nothing off the bar: shown faded on their line
                hits.Add(new DamageEvent(t0.AddSeconds(at + 0.17), 5, Boss, 6_000, false, "Skill", SkillId: k % 6 == 0 ? 18010000 : 18030000));
            }

            current -= drop;
            bar.Add(new StaggerEvent(t0.AddSeconds(at), Boss, current, max, false));
            at += 0.35;
        }

        DateTime now = failed ? t0.AddSeconds(14) : t0.AddSeconds(5.2);
        if (failed)
        {
            bar.Add(new StaggerEvent(t0.AddSeconds(8), Boss, max, max, false)); // the cast ran out: the bar fills again
        }
        else
        {
            bar.RemoveAll(b => b.At > now);
            hits.RemoveAll(h => h.Timestamp > now);
        }

        StaggerCheck check = StaggerChecks.Find(Boss, bar, windups, hits, now).Last(c => c.Mechanic != 0);
        return (check, now);
    }
}
