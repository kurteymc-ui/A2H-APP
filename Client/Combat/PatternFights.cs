namespace AionDPS.Combat;

/// <summary>
/// A boss fight as its pattern needs it (Combat/BossPattern.cs): the boss's attacks with the wind-up's time, in seconds after the fight's start (the first hit
/// on the boss, as the fight's upload counts it), and the boss's health share at each - from the game's own readings of it when the fight has them, else
/// for a boss that died from the damage done to it so far (all of it was its health). Pure.
/// </summary>
public static class PatternFights
{
    /// <summary>The fight of a report input, or null without a boss or a wind-up of it that took time.</summary>
    public static PatternFight? From(ReportInput input)
    {
        if (input.BossId is not int boss || input.End <= input.Start)
        {
            return null;
        }

        Func<DateTime, double?> health = HealthOf(input, boss);
        var casts = (input.Extras.Windups ?? Array.Empty<WindupRecord>())
            .Where(w => w.Actor == boss && w.DurationMs > 0 && w.At >= input.Start.AddSeconds(-1) && w.At <= input.End.AddSeconds(1))
            .OrderBy(w => w.At)
            .Select(w => new PatternCast(Math.Max(0, (w.At - input.Start).TotalSeconds), w.SkillId, w.DurationMs / 1000.0, health(w.At)))
            .ToList();
        return casts.Count == 0 ? null : new PatternFight((input.End - input.Start).TotalSeconds, input.Killed, casts);
    }

    /// <summary>The boss's health share (0 to 1) at a moment of the fight; null when the fight can't tell (no readings, and the boss didn't die).</summary>
    public static Func<DateTime, double?> HealthOf(ReportInput input, int boss)
    {
        if (input.Extras.Hp?.FirstOrDefault(h => h.Entity == boss && h.T.Length > 0) is { } series)
        {
            return at =>
            {
                double ms = (at - series.Start).TotalMilliseconds;
                int i = Array.BinarySearch(series.T, (int)Math.Clamp(ms, int.MinValue, int.MaxValue));
                i = i >= 0 ? i : ~i - 1;
                return i >= 0 ? series.Permille[i] / 1000.0 : series.Permille[0] / 1000.0;
            };
        }

        if (!input.Killed)
        {
            return _ => null;
        }

        var hits = input.Events.Where(e => e.TargetObjectId == boss && !e.IsHeal && e.Amount > 0 && e.Timestamp >= input.Start && e.Timestamp <= input.End.AddSeconds(1))
            .OrderBy(e => e.Timestamp).ToList();
        double total = hits.Sum(e => (double)e.Amount);
        if (total <= 0)
        {
            return _ => null;
        }

        var times = hits.Select(e => e.Timestamp).ToArray();
        var done = new double[hits.Count];
        double sum = 0;
        for (int i = 0; i < hits.Count; i++)
        {
            sum += hits[i].Amount;
            done[i] = sum;
        }

        return at =>
        {
            int i = Array.BinarySearch(times, at);
            i = i >= 0 ? i : ~i - 1;
            return 1 - (i >= 0 ? done[i] : 0) / total;
        };
    }
}
