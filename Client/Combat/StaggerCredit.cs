using AionDPS.Aion2.Protocol;

namespace AionDPS.Combat;

/// <summary>One drop of the boss's stagger bar, credited to the hit that took it off (<see cref="Player"/> 0: no hit of the party came just before it).
/// <see cref="Exact"/>: the drop is the amount that skill takes off; otherwise the nearest likely hit.</summary>
public sealed record StaggerHit(double At, int Player, int SkillId, int Amount, bool Exact);

/// <summary>The boss's stagger bar broke: when, and the hit that took the last of it off (<see cref="Player"/> 0 when unknown).</summary>
public sealed record StaggerBreak(double At, int Player, int SkillId);

/// <summary>One skill's stagger in a player's line: the amount it took off the bar and how many of its hits did.</summary>
public sealed record StaggerSkill(string Name, int SkillId, int Amount, int Hits);

/// <summary>A player's stagger: the amount they took off the boss's bar, their share of it, the breaks they finished and the skills that did it.</summary>
public sealed record StaggerLine(int Player, int Amount, double Share, int Breaks, IReadOnlyList<StaggerSkill> Skills);

/// <summary>The boss's stagger over a fight: the bar (how full, 0 to 1), every drop credited to a hit, the breaks, and each player's part.</summary>
public sealed class StaggerReport
{
    public static StaggerReport Empty { get; } = new();

    /// <summary>The bar over the fight: seconds from the start and how full it was (1 = untouched; 0 at a break).</summary>
    public IReadOnlyList<(double At, double Fill)> Bar { get; init; } = Array.Empty<(double, double)>();

    public IReadOnlyList<StaggerHit> Hits { get; init; } = Array.Empty<StaggerHit>();

    public IReadOnlyList<StaggerBreak> Breaks { get; init; } = Array.Empty<StaggerBreak>();

    /// <summary>The players who took something off the bar, the most first.</summary>
    public IReadOnlyList<StaggerLine> Players { get; init; } = Array.Empty<StaggerLine>();

    /// <summary>All that came off the bar.</summary>
    public int Total { get; init; }

    /// <summary>The part of <see cref="Total"/> whose hit matched the skill's amount exactly (0 to 100).</summary>
    public double ExactPercent { get; init; }

    public bool HasData => Bar.Count > 0;

    public StaggerLine? Of(int player) => Players.FirstOrDefault(p => p.Player == player);

    /// <summary>What <paramref name="player"/> took off the bar between two moments (seconds from the start).</summary>
    public int AmountIn(int player, double from, double to) => Hits.Where(h => h.Player == player && h.At >= from && h.At <= to).Sum(h => h.Amount);
}

/// <summary>
/// Who staggered the boss. The game sends the boss's stagger bar right after the hits that lowered it (opcode 0x05e0), one reading per hit that took
/// something off and in the order of those hits; and a skill always takes the same amount off it, or half as much again (2 for one skill, 10 for
/// another). So each drop goes to a hit just before it whose skill takes that amount off, the hits since the reading before first, in their order.
/// The amounts come from the fight itself (readings after hits of one skill only, then the amounts most of a skill's drops agree on) on top of the
/// ones seen in a recording (<see cref="Known"/>). Checked on a recording of three bosses (2026-10-31, 1,754 drops): in 67 of 67 moments with
/// several readings that could be told apart they came in the hits' order; 83 to 94% of the stagger matches a hit's skill exactly; a fight learned
/// on its own moves 6 to 8% of the stagger between players against all three learned together. Pure: a test can run it.
/// </summary>
public static class StaggerCredit
{
    /// <summary>A drop's hit landed at most this long before the reading.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMilliseconds(600);

    /// <summary>What each skill took off the bar in the recording (2026-10-31, five classes): the start of every fight's learning.</summary>
    internal static readonly IReadOnlyDictionary<int, int> Known = new Dictionary<int, int>
    {
        [11010350] = 2, [11170350] = 5, [11190340] = 10, [11390010] = 20, [11420350] = 2, [11440350] = 5, [13010340] = 2, [13050240] = 7,
        [13060120] = 7, [13070230] = 10, [13100240] = 2, [13110240] = 2, [13120240] = 2, [13130230] = 7, [13210140] = 7, [13220121] = 10,
        [13350350] = 5, [13350351] = 5, [13360120] = 5, [14010250] = 10, [14010251] = 12, [14010252] = 14, [14010253] = 16, [14050130] = 5,
        [14060030] = 20, [14080140] = 15, [14090130] = 7, [14110450] = 10, [14130230] = 10, [14340340] = 1, [15040040] = 5, [15050450] = 7,
        [15090240] = 2, [15100240] = 2, [15220450] = 7, [15280142] = 2, [15280143] = 10, [17040350] = 2, [17040357] = 2, [17050350] = 2,
        [17060230] = 20, [17070240] = 10, [17080140] = 10, [17350010] = 7, [18040350] = 2, [18050350] = 2, [18060340] = 10, [18100350] = 5,
        [18290130] = 15,
    };

    /// <summary>A drop a skill of this amount makes: the amount, or half as much again (rounded down).</summary>
    public static bool Fits(int amount, int drop) => amount > 0 && (drop == amount || drop == amount * 3 / 2);

    /// <param name="readings">The boss's stagger readings and breaks, in the order the game sent them.</param>
    /// <param name="hits">The party's hits on the boss, in the order the game sent them.</param>
    /// <param name="start">The start of the fight (the times are seconds from it).</param>
    public static StaggerReport Build(IReadOnlyList<StaggerEvent> readings, IReadOnlyList<DamageEvent> hits, DateTime start)
    {
        if (!readings.Any(r => !r.Broke && r.Max > 0))
        {
            return StaggerReport.Empty;
        }

        double Secs(DateTime at) => Math.Max(0, (at - start).TotalSeconds);
        var bar = new List<(double, double)>(readings.Count);
        foreach (StaggerEvent r in readings)
        {
            if (r.Broke)
            {
                bar.Add((Secs(r.At), 0));
            }
            else if (r.Max > 0)
            {
                bar.Add((Secs(r.At), Math.Clamp((double)r.Current / r.Max, 0, 1)));
            }
        }

        // the hits by time, those of one moment in the order they came; the readings in groups of one moment
        List<DamageEvent> ordered = hits.Select((h, i) => (h, i)).OrderBy(x => x.h.Timestamp).ThenBy(x => x.i).Select(x => x.h).ToList();
        List<Group> groups = Groups(readings);
        Dictionary<int, int> table = Learn(groups, ordered);
        List<Credit> credits = Assign(groups, ordered, table);
        for (int round = 0; round < 3; round++)
        {
            // the amount most of a skill's drops agree on (two at least), over what is known
            foreach ((int skill, Dictionary<int, int> counts) in Votes(credits.Where(c => c.Hit is not null).Select(c => (ordered[c.Hit!.Value].SkillId, c.Drop))))
            {
                int best = Best(counts, out int agree);
                if (agree >= 2)
                {
                    table[skill] = best;
                }
            }

            credits = Assign(groups, ordered, table);
        }

        var stagger = new List<StaggerHit>();
        var breaks = new List<StaggerBreak>();
        StaggerHit? last = null;
        foreach (Credit c in credits)
        {
            if (c.Broke)
            {
                breaks.Add(new StaggerBreak(Secs(c.At), last?.Player ?? 0, last?.SkillId ?? 0));
                last = null;
                continue;
            }

            DamageEvent? hit = c.Hit is int k ? ordered[k] : null;
            last = new StaggerHit(Secs(c.At), hit?.SourceObjectId ?? 0, hit?.SkillId ?? 0, c.Drop, c.Exact);
            stagger.Add(last);
        }

        int total = stagger.Sum(h => h.Amount);
        var players = stagger.Where(h => h.Player != 0).GroupBy(h => h.Player).Select(g =>
        {
            int amount = g.Sum(h => h.Amount);
            var skills = g.GroupBy(h => SkillName(h.SkillId)).Select(s => new StaggerSkill(s.Key, s.First().SkillId, s.Sum(h => h.Amount), s.Count()))
                .OrderByDescending(s => s.Amount).ToList();
            return new StaggerLine(g.Key, amount, total > 0 ? 100.0 * amount / total : 0, breaks.Count(b => b.Player == g.Key), skills);
        }).OrderByDescending(p => p.Amount).ToList();
        return new StaggerReport
        {
            Bar = bar,
            Hits = stagger,
            Breaks = breaks,
            Players = players,
            Total = total,
            ExactPercent = total > 0 ? 100.0 * stagger.Where(h => h.Exact).Sum(h => h.Amount) / total : 0,
        };
    }

    private static string SkillName(int skillId) => skillId != 0 ? Aion2SkillNames.NameOf(skillId) : "Attack";

    /// <summary>A reading's drop and the hit it went to (<see cref="Hit"/>: its index in the hits), or a break.</summary>
    private readonly record struct Credit(DateTime At, int Drop, int? Hit, bool Exact, bool Broke);

    /// <summary>The readings of one moment (<see cref="At"/>): the drops in the order they came, and the moment of the group before (null: the first,
    /// or the first after a break). <see cref="Broke"/>: a break instead.</summary>
    private sealed record Group(DateTime At, DateTime? Before, List<int> Drops, bool Broke = false);

    private static List<Group> Groups(IReadOnlyList<StaggerEvent> readings)
    {
        var groups = new List<Group>();
        int? previous = null;
        DateTime? before = null;
        foreach (StaggerEvent r in readings)
        {
            if (r.Broke)
            {
                groups.Add(new Group(r.At, null, new List<int>(), Broke: true));
                previous = null;
                before = null;
                continue;
            }

            if (r.Max <= 0)
            {
                continue;
            }

            int drop = previous is int p && r.Current < p ? p - r.Current : 0;
            if (groups.Count > 0 && !groups[^1].Broke && groups[^1].At == r.At)
            {
                if (drop > 0)
                {
                    groups[^1].Drops.Add(drop);
                }
            }
            else
            {
                groups.Add(new Group(r.At, before, drop > 0 ? new List<int> { drop } : new List<int>()));
            }

            previous = r.Current;
            before = r.At;
        }

        return groups;
    }

    /// <summary>The hits a group's drops can come from: the last <see cref="Window"/> before it (the earliest index, and one past the latest).</summary>
    private static (int Lo, int Hi) Span(IReadOnlyList<DamageEvent> hits, DateTime at)
    {
        int lo = LowerBound(hits, at - Window);
        int hi = lo;
        while (hi < hits.Count && hits[hi].Timestamp <= at)
        {
            hi++;
        }

        return (lo, hi);
    }

    private static int LowerBound(IReadOnlyList<DamageEvent> hits, DateTime at)
    {
        int lo = 0, hi = hits.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (hits[mid].Timestamp < at)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    /// <summary>
    /// The amounts to start from: <see cref="Known"/>, and what this fight shows plainly - the drops of a moment whose hits since the reading before are
    /// all of one skill go to that skill.
    /// </summary>
    private static Dictionary<int, int> Learn(IReadOnlyList<Group> groups, IReadOnlyList<DamageEvent> hits)
    {
        var table = new Dictionary<int, int>(Known);
        var plain = new List<(int Skill, int Drop)>();
        foreach (Group g in groups.Where(g => !g.Broke && g.Drops.Count > 0))
        {
            (int lo, int hi) = Span(hits, g.At);
            var since = Enumerable.Range(lo, hi - lo).Where(i => g.Before is not DateTime b || hits[i].Timestamp > b).ToList();
            if (since.Count >= g.Drops.Count && since.Select(i => hits[i].SkillId).Distinct().Count() == 1)
            {
                plain.AddRange(g.Drops.Select(d => (hits[since[0]].SkillId, d)));
            }
        }

        foreach ((int skill, Dictionary<int, int> counts) in Votes(plain))
        {
            table[skill] = Best(counts, out _);
        }

        return table;
    }

    private static Dictionary<int, Dictionary<int, int>> Votes(IEnumerable<(int Skill, int Drop)> drops)
    {
        var votes = new Dictionary<int, Dictionary<int, int>>();
        foreach ((int skill, int drop) in drops)
        {
            if (!votes.TryGetValue(skill, out Dictionary<int, int>? counts))
            {
                votes[skill] = counts = new Dictionary<int, int>();
            }

            counts[drop] = counts.GetValueOrDefault(drop) + 1;
        }

        return votes;
    }

    /// <summary>The amount most of the drops fit (<see cref="Fits"/>), the smallest of equals; <paramref name="agree"/>: how many fit it.</summary>
    private static int Best(Dictionary<int, int> counts, out int agree)
    {
        int best = 0;
        agree = -1;
        foreach (int v in counts.Keys.OrderBy(v => v))
        {
            int n = counts.Where(c => Fits(v, c.Key)).Sum(c => c.Value);
            if (n > agree)
            {
                best = v;
                agree = n;
            }
        }

        return best;
    }

    /// <summary>
    /// Each drop to a hit of the last <see cref="Window"/> not given to another drop yet: the hits since the reading before first (in their order),
    /// then the older ones (the latest first); one whose skill takes that amount off, else one whose skill staggers at all, else one of a skill not
    /// known yet.
    /// </summary>
    private static List<Credit> Assign(IReadOnlyList<Group> groups, IReadOnlyList<DamageEvent> hits, IReadOnlyDictionary<int, int> table)
    {
        var claimed = new bool[hits.Count];
        var credits = new List<Credit>();
        foreach (Group g in groups)
        {
            if (g.Broke)
            {
                credits.Add(new Credit(g.At, 0, null, false, true));
                continue;
            }

            (int lo, int hi) = Span(hits, g.At);
            foreach (int drop in g.Drops)
            {
                var since = new List<int>();
                var older = new List<int>();
                for (int i = lo; i < hi; i++)
                {
                    if (!claimed[i])
                    {
                        (g.Before is not DateTime b || hits[i].Timestamp > b || hits[i].Timestamp == g.At ? since : older).Add(i);
                    }
                }

                older.Reverse();
                var order = since.Concat(older).ToList();
                int? pick = order.Cast<int?>().FirstOrDefault(i => Fits(table.GetValueOrDefault(hits[i!.Value].SkillId), drop));
                bool exact = pick is not null;
                pick ??= order.Cast<int?>().FirstOrDefault(i => table.GetValueOrDefault(hits[i!.Value].SkillId) > 0);
                pick ??= order.Cast<int?>().FirstOrDefault(i => !table.ContainsKey(hits[i!.Value].SkillId));
                if (pick is int k)
                {
                    claimed[k] = true;
                }

                credits.Add(new Credit(g.At, drop, pick, exact, false));
            }
        }

        return credits;
    }
}
