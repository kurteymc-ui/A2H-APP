using AionDPS.Aion2.Protocol;

namespace AionDPS.Combat;

/// <summary>One of the party's hits that counts (on the boss or what the fight counts): seconds from the start, who, the skill and the damage.</summary>
public readonly record struct ReportHit(double At, int Player, int SkillId, string Skill, long Amount);

public enum MomentKind
{
    Burst,
    Drop,
    Break,
    Death,
}

/// <summary>
/// A moment of the fight worth a look: a burst (the party's DPS well above its usual), a drop (well below), the boss's stagger bar breaking, a death.
/// <see cref="Start"/>..<see cref="End"/> is the stretch it is about (seconds from the start; a burst or drop: the five seconds its DPS was taken over),
/// <see cref="Dps"/> the party's DPS then, <see cref="Why"/> what the fight shows about it.
/// </summary>
public sealed record FightMoment(MomentKind Kind, double At, double Start, double End, double Dps, string Title, IReadOnlyList<string> Why, int Player = 0);

/// <summary>What one player did in a stretch of the fight (the timeline's details): damage, DPS against their usual, the skills used and what they hit for, stagger.</summary>
public sealed record WindowPlayer(int Player, long Damage, double Dps, double Usual, int Stagger, IReadOnlyList<WindowSkill> Skills, int Deaths);

/// <summary>One skill of a player in a stretch: how often it was used, the damage it did there and the stagger it caused.</summary>
public sealed record WindowSkill(string Name, int SkillId, int Uses, long Damage, int Stagger);

/// <summary>A stretch of the fight looked at closely: the party's damage and DPS there, each player, the boss's casts and the breaks in it.</summary>
public sealed record FightWindow(double Start, double End, long Damage, double Dps, IReadOnlyList<WindowPlayer> Players, IReadOnlyList<string> BossCasts, int Breaks);

/// <summary>
/// Finds the moments of a fight (<see cref="Find"/>) and works out any stretch of it (<see cref="Window"/>). Pure: from a <see cref="FightReport"/> only.
/// A burst is a peak of the party's five-second DPS at least a quarter above the fight's usual (its median, the first seconds of the pull left out);
/// a drop a low at least 30% under it; at most four of each, eight seconds apart at least, the biggest first.
/// </summary>
public static class FightMoments
{
    public const double BurstAbove = 1.25, DropBelow = 0.70, ApartSeconds = 8;
    public const int MaxEach = 4;

    public static IReadOnlyList<FightMoment> Find(FightReport r)
    {
        var moments = new List<FightMoment>();
        string NameOf(int id) => r.PlayerOf(id)?.Player.Name ?? "someone";
        double[] line = r.PartyLine;
        double bin = r.BinSeconds;
        int skip = line.Length > (int)(2 * FightReport.RollingSeconds / bin) ? (int)(FightReport.RollingSeconds / bin) : 0;
        var settled = line.Skip(skip).ToArray();
        if (r.Seconds >= 15 && settled.Length >= 5)
        {
            double usual = Median(settled);
            int reach = Math.Max(1, (int)Math.Ceiling(4 / bin));
            if (usual > 0)
            {
                foreach (int i in Peaks(line, skip, reach, high: true).Where(i => line[i] >= usual * BurstAbove).OrderByDescending(i => line[i]).ToList().Apart(bin, ApartSeconds).Take(MaxEach))
                {
                    (double from, double to) = Stretch(i, bin, r.Seconds);
                    moments.Add(new FightMoment(MomentKind.Burst, to, from, to, line[i], $"Burst · {Ratio(line[i], usual)} the usual", BurstWhy(r, from, to)));
                }

                // a drop: not in the last bins (the boss dying ends the fight there, the line keeps the last hits)
                foreach (int i in Peaks(line, skip, reach, high: false).Where(i => line[i] <= usual * DropBelow && i < line.Length - 1).OrderBy(i => line[i]).ToList().Apart(bin, ApartSeconds).Take(MaxEach))
                {
                    (double from, double to) = Stretch(i, bin, r.Seconds);
                    moments.Add(new FightMoment(MomentKind.Drop, to, from, to, line[i], $"Drop · {Math.Round(100 * (1 - line[i] / usual))}% under the usual", DropWhy(r, from, to)));
                }
            }
        }

        foreach (StaggerBreak b in r.Stagger.Breaks)
        {
            string by = b.Player != 0 ? $"Last of the bar taken by {NameOf(b.Player)}" + (b.SkillId != 0 ? $" ({Aion2SkillNames.DisplayOf(b.SkillId)})" : "") : "Last hit unknown";
            moments.Add(new FightMoment(MomentKind.Break, b.At, b.At, Math.Min(r.Seconds, b.At + 5), DpsAt(r, b.At + 5), $"{r.BossName ?? "Boss"} staggered", new[] { by }, b.Player));
        }

        foreach (ReportPlayerLine p in r.Players)
        {
            foreach (ReportDeath d in p.Deaths)
            {
                var why = new List<string>();
                if (d.KilledBy is { } killer)
                {
                    why.Add($"Killed by {killer}" + (d.LastHit is long hit ? $" ({hit:N0})" : ""));
                }

                moments.Add(new FightMoment(MomentKind.Death, d.At, Math.Max(0, d.At - 5), d.At, DpsAt(r, d.At), $"{p.Player.Name} died", why, p.Player.Id));
            }
        }

        return moments.OrderBy(m => m.At).ToList();
    }

    /// <summary>What happened in [<paramref name="from"/>, <paramref name="to"/>] (seconds from the start).</summary>
    public static FightWindow Window(FightReport r, double from, double to)
    {
        from = Math.Clamp(from, 0, r.Seconds);
        to = Math.Clamp(Math.Max(to, from + 0.5), 0, r.Seconds);
        double seconds = Math.Max(0.5, to - from);
        var hits = r.PartyHits.Where(h => h.At >= from && h.At <= to).ToList();
        var players = new List<WindowPlayer>();
        foreach (ReportPlayerLine p in r.Players)
        {
            var mine = hits.Where(h => h.Player == p.Player.Id).ToList();
            var uses = (p.Rotation?.Uses ?? Array.Empty<RotationUse>()).Where(u => !u.Passive && u.At >= from && u.At <= to).ToList();
            var stagger = r.Stagger.Hits.Where(h => h.Player == p.Player.Id && h.At >= from && h.At <= to).ToList();
            var names = mine.Select(h => h.Skill).Concat(uses.Select(u => u.Name)).Concat(stagger.Select(s => Aion2SkillNames.NameOf(s.SkillId))).Distinct();
            var skills = names.Select(n => new WindowSkill(n,
                    mine.Where(h => h.Skill == n).Select(h => h.SkillId).Concat(uses.Where(u => u.Name == n).Select(u => u.SkillId)).FirstOrDefault(),
                    uses.Count(u => u.Name == n), mine.Where(h => h.Skill == n).Sum(h => h.Amount),
                    stagger.Where(s => Aion2SkillNames.NameOf(s.SkillId) == n).Sum(s => s.Amount)))
                .OrderByDescending(s => s.Damage).ThenByDescending(s => s.Uses).ToList();
            long damage = mine.Sum(h => h.Amount);
            int deaths = p.Deaths.Count(d => d.At >= from && d.At <= to);
            if (damage > 0 || skills.Count > 0 || deaths > 0)
            {
                players.Add(new WindowPlayer(p.Player.Id, damage, damage / seconds, p.Dps, stagger.Sum(s => s.Amount), skills, deaths));
            }
        }

        long total = hits.Sum(h => h.Amount);
        var casts = r.CastBars.Where(c => c.End >= from && c.Start <= to).Select(c => c.Name)
            .Concat(r.BossCasts.Where(c => c.At >= from && c.At <= to).Select(c => c.Name)).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
        return new FightWindow(from, to, total, total / seconds, players.OrderByDescending(p => p.Damage).ToList(), casts, r.Stagger.Breaks.Count(b => b.At >= from && b.At <= to));
    }

    // ---------- why ----------

    private static IReadOnlyList<string> BurstWhy(FightReport r, double from, double to)
    {
        var why = new List<string>();
        if (r.Stagger.Breaks.Any(b => b.At <= to && b.At + 5 >= from) || r.Staggers.Any(s => s.Start <= to && s.End >= from))
        {
            why.Add($"{r.BossName ?? "The boss"} was staggered");
        }

        foreach (ReportPlayerLine p in Movers(r, from, to, up: true).Take(2))
        {
            double then = DpsIn(r, p.Player.Id, from, to);
            why.Add($"{p.Player.Name} {Ratio(then, p.Dps)} their usual DPS");
        }

        return why.Count > 0 ? why : new[] { "The whole party hit harder" };
    }

    private static IReadOnlyList<string> DropWhy(FightReport r, double from, double to)
    {
        var why = new List<string>();
        foreach (string cast in r.CastBars.Where(c => c.End >= from && c.Start <= to).Select(c => c.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().Take(2))
        {
            why.Add($"{r.BossName ?? "The boss"} cast {Aion2SkillNames.Display(cast)}");
        }

        foreach (ReportPlayerLine p in r.Players)
        {
            if (p.Deaths.Any(d => d.At >= from - 10 && d.At <= to))
            {
                why.Add($"{p.Player.Name} was dead");
            }
        }

        foreach (ReportPlayerLine p in Movers(r, from, to, up: false).Take(2))
        {
            double then = DpsIn(r, p.Player.Id, from, to);
            why.Add(then <= 0 ? $"{p.Player.Name} dealt no damage" : $"{p.Player.Name} at {Math.Round(100 * then / p.Dps)}% of their usual DPS");
        }

        return why.Count > 0 ? why : new[] { "The whole party's damage fell" };
    }

    /// <summary>The players whose DPS in the stretch moved most from their usual (up or down), by how much it moved.</summary>
    private static IEnumerable<ReportPlayerLine> Movers(FightReport r, double from, double to, bool up) =>
        r.Players.Where(p => p.Dps > 0).Select(p => (p, d: DpsIn(r, p.Player.Id, from, to) - p.Dps))
            .Where(x => up ? x.d > x.p.Dps * 0.25 : x.d < -x.p.Dps * 0.25)
            .OrderByDescending(x => Math.Abs(x.d)).Select(x => x.p);

    private static double DpsIn(FightReport r, int player, double from, double to) =>
        r.PartyHits.Where(h => h.Player == player && h.At >= from && h.At <= to).Sum(h => h.Amount) / Math.Max(0.5, to - from);

    private static double DpsAt(FightReport r, double at)
    {
        int i = Math.Clamp((int)(at / r.BinSeconds), 0, Math.Max(0, r.PartyLine.Length - 1));
        return r.PartyLine.Length > 0 ? r.PartyLine[i] : 0;
    }

    private static string Ratio(double value, double usual) => usual > 0 ? (value / usual).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "×" : "–";

    /// <summary>The stretch a rolling-DPS bin was taken over: the five seconds up to its end.</summary>
    private static (double From, double To) Stretch(int i, double bin, double seconds)
    {
        double to = Math.Min(seconds, (i + 1) * bin);
        return (Math.Max(0, to - FightReport.RollingSeconds), to);
    }

    /// <summary>The highest (or lowest) points of the line: no bin within <paramref name="reach"/> of it higher (lower).</summary>
    private static IEnumerable<int> Peaks(double[] line, int skip, int reach, bool high)
    {
        for (int i = skip; i < line.Length; i++)
        {
            bool best = true;
            for (int j = Math.Max(skip, i - reach); j <= Math.Min(line.Length - 1, i + reach) && best; j++)
            {
                if (j != i && (high ? line[j] > line[i] || (line[j] == line[i] && j < i) : line[j] < line[i] || (line[j] == line[i] && j < i)))
                {
                    best = false;
                }
            }

            if (best)
            {
                yield return i;
            }
        }
    }

    /// <summary>Keeps the bins (in their order: the most telling first) at least <paramref name="apart"/> seconds from one kept before.</summary>
    private static IEnumerable<int> Apart(this List<int> bins, double bin, double apart)
    {
        var kept = new List<int>();
        foreach (int i in bins)
        {
            if (kept.All(k => Math.Abs(k - i) * bin >= apart))
            {
                kept.Add(i);
                yield return i;
            }
        }
    }

    private static double Median(double[] values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        return sorted.Length == 0 ? 0 : sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }
}
