using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2;

/// <summary>One step of a season mission: its text in the client's languages and the count it needs (none for a step that is a condition, e.g. "within 1 minute").</summary>
/// <summary>A step of a season mission: its target (null for a condition), its text, and the Oath Coins (the season tokens) it gives
/// (the game's rewards; 0 for the event missions, which give event coins).</summary>
public sealed record SeasonStep(int? Target, IReadOnlyDictionary<string, string> Text, int Coins = 0);

/// <summary>
/// A season mission of the current season: the game's counter id (frames 0x8356/0x8456), faction ('L' Elyos, 'D' Asmodian), category (the game's tab:
/// Abyss, Arena, PartyChallenge, Nightmare, Thema, Event), whether it starts again every week, its name and its steps. <see cref="Hours"/>: the count is
/// minutes, shown as hours.
/// </summary>
public sealed record SeasonMission(int Id, char Faction, string Category, bool Weekly, IReadOnlyDictionary<string, string> Name, bool Hours, IReadOnlyList<SeasonStep> Steps);

/// <summary>Where a character stands in a mission: the count, the steps done, and whether the numbers are of a week already over (0 then, until the next login).</summary>
public readonly record struct SeasonProgress(SeasonMission Mission, int Count, int Done, bool Stale)
{
    public bool Finished => Done >= Mission.Steps.Count;

    /// <summary>The step worked on now (null when all are done).</summary>
    public SeasonStep? Next => Finished ? null : Mission.Steps[Done];
}

/// <summary>
/// The current season's missions (assets/aion2/season/season-missions.json) and a character's progress in them from its achievement counters
/// (<see cref="Aion2Achievements"/>).
/// <list type="bullet">
/// <item>Steps done: the game's stage byte lags the count by a step or two at times, so the larger of it and the steps whose target the count has reached
/// (in order; a step without a target only by the stage byte).</item>
/// <item>Weekly missions start again at the weekly reset (Wednesday after the maintenance): a counter the game last changed before it is of last week, 0 now.</item>
/// </list>
/// </summary>
public static class SeasonMissions
{
    private static readonly object Gate = new();
    private static IReadOnlyList<SeasonMission>? _all;
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _categories = new Dictionary<string, IReadOnlyDictionary<string, string>>();

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "season", "season-missions.json");

    /// <summary>Every mission of both factions, in the game's order (by id); empty when the file is missing.</summary>
    public static IReadOnlyList<SeasonMission> All
    {
        get
        {
            lock (Gate)
            {
                if (_all is null)
                {
                    try
                    {
                        _all = File.Exists(DefaultPath) ? Parse(File.ReadAllText(DefaultPath), out _categories) : Array.Empty<SeasonMission>();
                    }
                    catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException or FormatException)
                    {
                        _all = Array.Empty<SeasonMission>();
                    }
                }

                return _all;
            }
        }
    }

    /// <summary>The game's names of the categories ("PartyChallenge" is "Transcendence", "Thema" is "Challenge", "Weekly" the weekly tab).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Categories
    {
        get
        {
            _ = All;
            return _categories;
        }
    }

    /// <summary>Reads the data file's text.</summary>
    public static IReadOnlyList<SeasonMission> Parse(string json, out IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> categories)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        var cats = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        if (root.TryGetProperty("categories", out JsonElement c) && c.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty p in c.EnumerateObject())
            {
                cats[p.Name] = Texts(p.Value);
            }
        }

        categories = cats;
        var list = new List<SeasonMission>();
        foreach (JsonProperty p in root.GetProperty("missions").EnumerateObject())
        {
            JsonElement m = p.Value;
            var steps = new List<SeasonStep>();
            foreach (JsonElement s in m.GetProperty("s").EnumerateArray())
            {
                int? target = s.TryGetProperty("t", out JsonElement t) && t.TryGetInt32(out int n) ? n : null;
                int coins = s.TryGetProperty("o", out JsonElement o) && o.TryGetInt32(out int given) ? given : 0;
                steps.Add(new SeasonStep(target, Texts(s.GetProperty("d")), coins));
            }

            if (steps.Count == 0 || !int.TryParse(p.Name, out int id))
            {
                continue;
            }

            list.Add(new SeasonMission(id, m.GetProperty("f").GetString() is "D" ? 'D' : 'L', m.GetProperty("c").GetString() ?? "", m.GetProperty("w").GetBoolean(),
                Texts(m.GetProperty("n")), m.TryGetProperty("u", out JsonElement u) && u.GetString() == "h", steps));
        }

        return list.OrderBy(x => x.Id).ToList();
    }

    private static IReadOnlyDictionary<string, string> Texts(JsonElement e)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        if (e.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty p in e.EnumerateObject())
            {
                if (p.Value.GetString() is { Length: > 0 } s)
                {
                    d[p.Name] = s;
                }
            }
        }

        return d;
    }

    /// <summary>The season tokens' item ("Oath Coin", the game's CoinSeason).</summary>
    public const int OathCoin = 930100019;

    /// <summary>The Oath Coins of the weekly missions: what the steps done this week give, and what all the weekly missions give when every step is done
    /// (49,140 for either faction in Season 1).</summary>
    public static (int Earned, int Possible) WeeklyCoins(IEnumerable<SeasonProgress> progress)
    {
        int earned = 0, possible = 0;
        foreach (SeasonProgress p in progress.Where(p => p.Mission.Weekly))
        {
            possible += p.Mission.Steps.Sum(s => s.Coins);
            earned += p.Mission.Steps.Take(p.Done).Sum(s => s.Coins);
        }

        return (earned, possible);
    }

    /// <summary>A text in the language (the meter's codes; Chinese has none in the EU client), else English.</summary>
    public static string Text(IReadOnlyDictionary<string, string> texts, string language) =>
        texts.TryGetValue(language, out string? s) ? s : texts.TryGetValue("en", out string? en) ? en : texts.Values.FirstOrDefault() ?? "";

    /// <summary>The character's faction: by its server (2xxx Asmodian, 1xxx Elyos), else by which faction's missions its counters name.</summary>
    public static char FactionOf(int serverId, IReadOnlyDictionary<int, AchievementValue> counters, IReadOnlyList<SeasonMission>? missions = null)
    {
        if (serverId / 1000 is 1 or 2)
        {
            return serverId / 1000 == 2 ? 'D' : 'L';
        }

        missions ??= All;
        int d = missions.Count(m => m.Faction == 'D' && counters.ContainsKey(m.Id)), l = missions.Count(m => m.Faction == 'L' && counters.ContainsKey(m.Id));
        return d > l ? 'D' : 'L';
    }

    /// <summary>The steps done at a count and the game's stage byte.</summary>
    public static int DoneSteps(SeasonMission mission, int count, int stage)
    {
        int byCount = 0;
        foreach (SeasonStep step in mission.Steps)
        {
            if (step.Target is not { } target || count < target)
            {
                break;
            }

            byCount++;
        }

        return Math.Clamp(Math.Max(stage, byCount), 0, mission.Steps.Count);
    }

    /// <summary>
    /// The progress of a character of the faction in every mission of it (missions without a counter at 0). <paramref name="lastWeeklyReset"/>: the latest weekly
    /// reset (local time, <see cref="ChecklistManual.LastWeeklyReset"/>); weekly counters last changed before it count as 0 and are marked stale.
    /// </summary>
    public static IReadOnlyList<SeasonProgress> Progress(char faction, IReadOnlyDictionary<int, AchievementValue> counters, DateTime lastWeeklyReset, IReadOnlyList<SeasonMission>? missions = null)
    {
        missions ??= All;
        var list = new List<SeasonProgress>();
        foreach (SeasonMission m in missions)
        {
            if (m.Faction != faction)
            {
                continue;
            }

            if (!counters.TryGetValue(m.Id, out AchievementValue v))
            {
                list.Add(new SeasonProgress(m, 0, 0, false));
                continue;
            }

            bool stale = m.Weekly && (v.Count > 0 || v.Stage > 0)
                && (v.TimeMs <= 0 || DateTimeOffset.FromUnixTimeMilliseconds(v.TimeMs).LocalDateTime < lastWeeklyReset);
            list.Add(stale ? new SeasonProgress(m, 0, 0, true) : new SeasonProgress(m, v.Count, DoneSteps(m, v.Count, v.Stage), false));
        }

        return list;
    }

    /// <summary>What the mission asks now, in the game's own words: the text of the step worked on ("Obtain 3,000 Phantasmal Fragments"), the last step's
    /// once all are done.</summary>
    public static string Description(SeasonProgress p, string language) =>
        Text((p.Next ?? p.Mission.Steps[^1]).Text, language);

    /// <summary>How far along the step worked on is (0 to 1; 1 when finished, 0 for a condition step).</summary>
    public static double Fraction(SeasonProgress p) =>
        p.Finished ? 1 : p.Next?.Target is { } target && target > 0 ? Math.Clamp((double)p.Count / target, 0, 1) : 0;

    /// <summary>
    /// The missions the season overlay shows: the ones picked (<paramref name="chosen"/>, in the order picked; the other faction's ids are skipped), or, while
    /// none are picked, every unfinished weekly mission, the ones furthest along first.
    /// </summary>
    public static IReadOnlyList<SeasonProgress> ForOverlay(IReadOnlyList<SeasonProgress> all, IReadOnlyList<int>? chosen)
    {
        if (chosen is { Count: > 0 })
        {
            var byId = all.ToDictionary(x => x.Mission.Id);
            return chosen.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
        }

        return all.Where(x => x.Mission.Weekly && !x.Finished).OrderByDescending(Fraction).ThenBy(x => x.Mission.Id).ToList();
    }

    /// <summary>The character logged in now (or last), with its faction and progress; null before the meter has seen one.</summary>
    public static (string Name, char Faction, IReadOnlyList<SeasonProgress> Progress)? Current(DateTime now)
    {
        if (Aion2Tickets.CurrentName is not { Length: > 0 } name)
        {
            return null;
        }

        IReadOnlyDictionary<int, AchievementValue> counters = Aion2Achievements.Of(name);
        int server = Aion2Tickets.Snapshot().FirstOrDefault(c => c.Name == name)?.ServerId ?? 0;
        char faction = FactionOf(server, counters);
        return (name, faction, Progress(faction, counters, ChecklistManual.LastWeeklyReset(now)));
    }

    /// <summary>A count as the game shows it: hours with one decimal for the time missions (the game counts minutes), else the number.</summary>
    public static string CountText(SeasonMission mission, int count, IFormatProvider culture) =>
        mission.Hours ? (count / 60.0).ToString(count % 60 == 0 ? "N0" : "N1", culture) : count.ToString("N0", culture);
}
