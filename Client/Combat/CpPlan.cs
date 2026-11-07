using System.IO;
using System.Text.Json;

namespace AionDPS.Combat;

/// <summary>A class build of the website (Class Builds, PvE): the skills in priority order with the level each is taken to, the Stigmas' levels and the order
/// they are levelled in.</summary>
public sealed record CpClassPlan(IReadOnlyList<string> SkillOrder, IReadOnlyDictionary<string, int> SkillTargets, IReadOnlyDictionary<string, int> StigmaGoals,
    IReadOnlyList<(string Name, int Level, bool Whole)> StigmaPlan);

/// <summary>One step of a levelling path: the skill or Stigma (its index in the list given), from one level to another.</summary>
public readonly record struct CpLevelStep(int Index, int From, int To);

/// <summary>
/// The order the CP calculator puts Stigma and skill levels in: the website's class build (assets/aion2/builds/class_plans.json, made from the Class Builds
/// page; 6 November 2026, the owner: "Stigma and skills, priority should be based on the class website"). Levels go by ranks, the lower ranks first:
/// Stigmas by 5, 10, 15, 20 (a Stigma to 10 before another from 10 to 13); active skills by the levels that count, 8, 12, 16, 20 (7 November 2026: the
/// first specialisation at 8, +500 / +900 / +1,000 Combat Power on reaching 12 / 16 / 20, in-game figures). Stigmas follow the build's own plan first
/// (the order the Class Builds page and the Character Lookup's suggestions level them in), then every Stigma by ranks; skills go by ranks in the build's
/// priority order, each up to the level the build takes it to, then the rest. Pure, apart from reading the file once.
/// </summary>
public static class CpPlan
{
    /// <summary>The ranks Stigma levels go by (and skills', unless others are given).</summary>
    public static readonly int[] Ranks = { 5, 10, 15, 20 };

    private static Dictionary<string, CpClassPlan>? _plans;

    /// <summary>The build of a class (by its English name, any case), or null for a class without one.</summary>
    public static CpClassPlan? For(string? className) =>
        className is { Length: > 0 } && Plans().TryGetValue(className.Trim().ToLowerInvariant(), out CpClassPlan? plan) ? plan : null;

    /// <summary>A skill or Stigma name the way the build writes it: lower case, punctuation as spaces ("Zikel's Blessing" is "zikel s blessing").</summary>
    public static string Key(string? name)
    {
        var chars = (name ?? "").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray();
        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Stigma Shards one level takes: 1 up to level 5, 2 up to 10, 4 up to 15, 8 above (the Class Builds page's table).</summary>
    public static int ShardsFor(int level) => level <= 5 ? 1 : level <= 10 ? 2 : level <= 15 ? 4 : 8;

    /// <summary>Stigma Shards from one level to another.</summary>
    public static int ShardsTo(int from, int to)
    {
        int total = 0;
        for (int level = from + 1; level <= to; level++)
        {
            total += ShardsFor(level);
        }

        return total;
    }

    /// <summary>The next rank above <paramref name="level"/> up to <paramref name="cap"/>, else the cap itself when it is above the level.</summary>
    private static int? NextRank(int level, int cap, IReadOnlyList<int> ranks)
    {
        foreach (int rank in ranks)
        {
            if (rank > level && rank <= cap)
            {
                return rank;
            }
        }

        return cap > level ? cap : null;
    }

    /// <summary>
    /// The Stigmas' path, the first step first: the build's plan for the Stigmas it has among <paramref name="names"/> (each up to the level the build takes it
    /// to), then each by ranks to that level, then on to <paramref name="top"/> by ranks (what is left gives Combat Power too). Without a build: every Stigma to
    /// 5, the two the build would rank first to 10, the first to 15 and 20, the second to 15, then the ranks.
    /// </summary>
    public static List<CpLevelStep> StigmaPath(IReadOnlyList<string> names, IReadOnlyList<int> levels, CpClassPlan? plan, int top)
    {
        int[] goal = names.Select(k => plan is not null && plan.StigmaGoals.TryGetValue(k, out int g) ? g : 5).ToArray();
        int[] rank = Enumerable.Range(0, names.Count).OrderByDescending(i => goal[i]).ThenBy(i => i).ToArray();
        var path = new List<(int I, int To, bool Whole)>();
        void Add(int i, int to, bool whole = false)
        {
            if (i >= 0 && i < names.Count)
            {
                path.Add((i, Math.Min(to, goal[i]), whole));
            }
        }

        if (plan is { StigmaPlan.Count: > 0 })
        {
            foreach ((string name, int level, bool whole) in plan.StigmaPlan)
            {
                Add(names.ToList().IndexOf(name), level, whole);
            }
        }
        else
        {
            for (int i = 0; i < names.Count; i++)
            {
                Add(i, 5);
            }

            if (rank.Length > 0)
            {
                Add(rank[0], 10);
            }

            if (rank.Length > 1)
            {
                Add(rank[1], 10);
            }

            if (rank.Length > 0)
            {
                Add(rank[0], 15);
                Add(rank[0], 20);
            }

            if (rank.Length > 1)
            {
                Add(rank[1], 15);
            }
        }

        foreach (int bp in new[] { 10, 15, 20 })
        {
            foreach (int i in rank)
            {
                Add(i, bp);
            }
        }

        // past the build: every Stigma on by ranks (each level still gives Combat Power)
        foreach (int bp in Ranks)
        {
            foreach (int i in rank)
            {
                path.Add((i, Math.Min(bp, top), false));
            }
        }

        return Walk(path, levels);
    }

    /// <summary>
    /// The skills' path, the first step first: by ranks (<paramref name="ranks"/>, else <see cref="Ranks"/>: all to the first, then the next ...), in the build's priority order, each up to the level the build takes
    /// it to; then the skills the build leaves lower or doesn't name, by ranks, to <paramref name="top"/>. <paramref name="names"/>: the skills to level (the
    /// ones that count).
    /// </summary>
    public static List<CpLevelStep> SkillPath(IReadOnlyList<string> names, IReadOnlyList<int> levels, CpClassPlan? plan, int top, IReadOnlyList<int>? ranks = null)
    {
        ranks ??= Ranks;
        var path = new List<(int I, int To, bool Whole)>();
        var index = names.Select((k, i) => (k, i)).GroupBy(x => x.k).ToDictionary(g => g.Key, g => g.First().i);
        if (plan is not null)
        {
            foreach (int bp in ranks)
            {
                foreach (string k in plan.SkillOrder)
                {
                    if (index.TryGetValue(k, out int i) && plan.SkillTargets.TryGetValue(k, out int target) && target > 1)
                    {
                        path.Add((i, Math.Min(bp, target), false));
                    }
                }
            }
        }

        // the rest: the build's order first, then the others as they come
        var order = (plan?.SkillOrder.Where(index.ContainsKey).Select(k => index[k]) ?? Enumerable.Empty<int>()).Concat(Enumerable.Range(0, names.Count)).Distinct().ToList();
        foreach (int bp in ranks)
        {
            foreach (int i in order)
            {
                path.Add((i, Math.Min(bp, top), false));
            }
        }

        return Walk(path, levels, ranks);
    }

    /// <summary>Follows a path from the levels now: each entry raises its skill to the next rank on the way to its level (all at once for a "whole" entry).</summary>
    private static List<CpLevelStep> Walk(List<(int I, int To, bool Whole)> path, IReadOnlyList<int> levels, IReadOnlyList<int>? ranks = null)
    {
        ranks ??= Ranks;
        int[] level = levels.ToArray();
        var steps = new List<CpLevelStep>();
        foreach ((int i, int to, bool whole) in path)
        {
            while (level[i] < to)
            {
                int next = whole ? to : NextRank(level[i], to, ranks) ?? to;
                steps.Add(new CpLevelStep(i, level[i], next));
                level[i] = next;
            }
        }

        return steps;
    }

    private static Dictionary<string, CpClassPlan> Plans()
    {
        if (_plans is not null)
        {
            return _plans;
        }

        var plans = new Dictionary<string, CpClassPlan>();
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "builds", "class_plans.json");
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (JsonProperty c in doc.RootElement.GetProperty("classes").EnumerateObject())
            {
                JsonElement skills = c.Value.GetProperty("skills"), stigmas = c.Value.GetProperty("stigmas");
                var order = skills.GetProperty("order").EnumerateArray().Select(x => x.GetString() ?? "").ToList();
                var targets = skills.GetProperty("targets").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32());
                var goals = stigmas.GetProperty("goals").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32());
                var plan = stigmas.GetProperty("plan").EnumerateArray()
                    .Select(x => (x[0].GetString() ?? "", x[1].GetInt32(), x.GetArrayLength() > 2 && x[2].ValueKind == JsonValueKind.True)).ToList();
                string name = c.Value.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? c.Name : c.Name;
                plans[name.ToLowerInvariant()] = new CpClassPlan(order, targets, goals, plan);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // no file (or a broken one): the calculator orders by ranks without a build
        }

        return _plans = plans;
    }
}
