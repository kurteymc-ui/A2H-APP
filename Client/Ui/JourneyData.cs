using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;
using AionDPS.Combat;
using AionDPS.History;
using AionDPS.Upload;

namespace AionDPS.Ui;

/// <summary>The periods of the Journey page.</summary>
internal enum JourneySpan { Session, Day, Week7, ThisWeek, LastWeek, Days30, ThisMonth, All, Custom }

/// <summary>A character the page can show: its name, server and class.</summary>
internal sealed record JourneyWho(string Name, int ServerId, string? Class)
{
    public string Key => JourneyLog.Key(Name, ServerId);
}

/// <summary>A drop: what, how many, what it was worth (0: no price), when.</summary>
internal sealed record JourneyDrop(int ItemId, string Name, int Grade, long Count, long Worth, DateTime At);

/// <summary>A boss in the period: kills, the best and the average own DPS, the average of the period before (0: not fought then), own deaths.</summary>
internal sealed record JourneyBoss(string Name, int Kills, double BestDps, double AverageDps, double AverageBefore, int Deaths);

/// <summary>A dungeon run of the period slower than the best run there: its time and the best.</summary>
internal sealed record JourneyRun(string Name, double Seconds, double BestSeconds, DateTime At);

/// <summary>One day (or week, for long periods) of the period: minutes played, at bosses, in dungeons; loot worth; boss kills; the best drop.</summary>
internal sealed record JourneyDayNumbers(DateTime Day, double Played, double BossMinutes, double DungeonMinutes, long Worth, int Kills, double BossDps, int Deaths, int? ItemLevel, JourneyDrop? Drop);

/// <summary>The figures of one period.</summary>
internal sealed class JourneyNumbers
{
    public double MinutesPlayed { get; set; }
    public double MinutesIdle { get; set; }
    public double MinutesFighting { get; set; }

    /// <summary>Whether the play time was recorded for the period (the log began on the first start of this version).</summary>
    public bool TimeKnown { get; set; }
    public long LootWorth { get; set; }
    public bool WorthKnown { get; set; }
    public JourneyDrop? BestDrop { get; set; }
    public int BossKills { get; set; }
    public double BossDps { get; set; }
    public double BestBossDps { get; set; }
    public string? BestBossName { get; set; }
    public int Deaths { get; set; }
    public int Dungeons { get; set; }
    public int? ItemLevelStart { get; set; }
    public int? ItemLevelEnd { get; set; }
    public int? CpEnd { get; set; }
    public List<JourneyBoss> Bosses { get; set; } = new();
    public List<JourneyDayNumbers> Days { get; set; } = new();
}

/// <summary>Everything the page shows for a character and a period, with the period before for the changes.</summary>
internal sealed record JourneyReport(JourneyWho Who, DateTime From, DateTime To, JourneyNumbers Now, JourneyNumbers Before, List<JourneyRun> SlowerRuns);

/// <summary>"You and your class": the character's cleared fights on the website with their ranks (percent of players beaten) in its class and in its power bracket.</summary>
internal sealed record JourneyRank(string Boss, DateTime At, double Dps, double? ClassPct, int? ClassN, double? BracketPct, int? BracketN, long? BracketLo, long? BracketHi);

/// <summary>
/// The Journey page's numbers (5 November 2026, the owner: "any period; what improved; where to improve; to share; you and your class"), from what the app keeps
/// on this PC: the fights (History), the loot sessions (worth at the market's prices of the character's server), the Combat Power checks at the logins (Item
/// Level), the play time of <see cref="JourneyLog"/>; and from the website the ranks of the character's uploaded fights.
/// </summary>
internal static class JourneyData
{
    private static readonly Dictionary<string, int> DeathsKept = new(StringComparer.Ordinal);

    /// <summary>The characters with fights or play time on this PC, the most recent first; the one the game is read for first of all.</summary>
    public static List<JourneyWho> Characters(Aion2EntityDirectory? dir)
    {
        var seen = new Dictionary<string, (JourneyWho Who, DateTime Last)>(StringComparer.Ordinal);
        foreach (FightLogEntry e in FightLog.Entries())
        {
            if (e.You is { Length: > 0 } name && e.ServerId is int server and > 0)
            {
                var who = new JourneyWho(name, server, e.YourClass);
                if (!seen.TryGetValue(who.Key, out var had) || had.Last < e.Start)
                {
                    seen[who.Key] = (had.Who?.Class is { } c && who.Class is null ? who with { Class = c } : who, e.Start);
                }
            }
        }

        foreach (string key in JourneyLog.Characters())
        {
            int at = key.LastIndexOf('@');
            if (at > 0 && int.TryParse(key[(at + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int server) && !seen.ContainsKey(key))
            {
                JourneySession? last = JourneyLog.Of(key).Sessions.LastOrDefault();
                seen[key] = (new JourneyWho(key[..at], server, null), last?.End.ToLocalTime() ?? DateTime.MinValue);
            }
        }

        var list = seen.Values.OrderByDescending(v => v.Last).Select(v => v.Who).ToList();
        if (dir?.LocalCharacter is { Name.Length: > 0 } me && me.ServerId > 0)
        {
            string key = JourneyLog.Key(me.Name, me.ServerId);
            JourneyWho current = list.FirstOrDefault(w => w.Key == key) ?? new JourneyWho(me.Name, me.ServerId, Aion2SkillNames.ClassFromCode(me.ClassCode));
            list.RemoveAll(w => w.Key == key);
            list.Insert(0, current with { Class = current.Class ?? Aion2SkillNames.ClassFromCode(me.ClassCode) });
        }

        return list;
    }

    /// <summary>The period's start and end (local time).</summary>
    public static (DateTime From, DateTime To) Range(JourneySpan span, DateTime now, JourneyWho? who, DateTime? customFrom, DateTime? customTo)
    {
        DateTime today = now.Date;
        switch (span)
        {
            case JourneySpan.Session:
                JourneySession? last = who is null ? null : JourneyLog.Of(who.Key).Sessions.LastOrDefault();
                return last is null ? (today, now) : (last.Start.ToLocalTime(), last.End.ToLocalTime() > now ? now : last.End.ToLocalTime().AddMinutes(1));
            case JourneySpan.Day:
                return (now.AddHours(-24), now);
            case JourneySpan.Week7:
                return (today.AddDays(-6), now);
            case JourneySpan.ThisWeek:
                return (WeekStart(now), now);
            case JourneySpan.LastWeek:
                DateTime start = WeekStart(now);
                return (start.AddDays(-7), start);
            case JourneySpan.Days30:
                return (today.AddDays(-29), now);
            case JourneySpan.ThisMonth:
                return (new DateTime(now.Year, now.Month, 1), now);
            case JourneySpan.All:
                DateTime first = FightLog.Entries().Select(e => e.Start).DefaultIfEmpty(today).Min();
                return (first.Date, now);
            default:
                DateTime from = (customFrom ?? today.AddDays(-6)).Date, to = (customTo ?? today).Date.AddDays(1);
                return from < to ? (from, to > now ? now : to) : (to.AddDays(-1), from.AddDays(1));
        }
    }

    /// <summary>The game's week: from the last weekly reset (the tickets' weekly reset), else from Monday.</summary>
    private static DateTime WeekStart(DateTime now)
    {
        if (Aion2Tickets.Find(101) is { } weekly && Aion2Tickets.NextReset(weekly, now) is { } next)
        {
            return next.AddDays(-7);
        }

        int back = ((int)now.DayOfWeek + 6) % 7;
        return now.Date.AddDays(-back);
    }

    /// <summary>The report (reads fight files: off the window's thread).</summary>
    public static JourneyReport Build(JourneyWho who, DateTime from, DateTime to)
    {
        TimeSpan length = to - from;
        IReadOnlyList<FightLogEntry> all = FightLog.Entries().Where(e => Mine(e, who)).ToList();
        JourneyCharacter log = JourneyLog.Of(who.Key);
        // the character's server's prices (ExchangePrices: fetched in the background, the page draws again when they come)
        MarketBook? book = ExchangePrices.For(who.ServerId);
        List<CpCheckPoint> points = CpChecks.State(who.Name, who.ServerId)?.Points ?? new List<CpCheckPoint>();
        JourneyNumbers before = Numbers(who, all, log, book, points, from - length, from, null);
        JourneyNumbers now = Numbers(who, all, log, book, points, from, to, before);

        // dungeons: a run of the period slower than the best run in that dungeon (10 % and 30 s at least)
        var slower = new List<JourneyRun>();
        foreach (var zone in all.Where(e => e.Kind == FightLogKind.Run && e.Seconds > 60).GroupBy(e => e.Title))
        {
            FightLogEntry best = zone.MinBy(e => e.Seconds)!;
            FightLogEntry? latest = zone.Where(e => e.Start >= from && e.Start < to).MaxBy(e => e.Start);
            if (latest is not null && latest.Id != best.Id && latest.Seconds >= best.Seconds * 1.1 && latest.Seconds - best.Seconds >= 30)
            {
                slower.Add(new JourneyRun(zone.Key, latest.Seconds, best.Seconds, latest.Start));
            }
        }

        return new JourneyReport(who, from, to, now, before, slower.OrderByDescending(r => r.Seconds / r.BestSeconds).Take(3).ToList());
    }

    private static bool Mine(FightLogEntry e, JourneyWho who) =>
        string.Equals(e.You, who.Name, StringComparison.OrdinalIgnoreCase) && (e.ServerId is null || e.ServerId == who.ServerId);

    private static JourneyNumbers Numbers(JourneyWho who, IReadOnlyList<FightLogEntry> all, JourneyCharacter log, MarketBook? book, List<CpCheckPoint> points,
        DateTime from, DateTime to, JourneyNumbers? before)
    {
        var n = new JourneyNumbers();
        List<FightLogEntry> fights = all.Where(e => e.Start >= from && e.Start < to).ToList();
        List<FightLogEntry> kills = fights.Where(e => e.Kind == FightLogKind.Boss && e.Killed == true && e.YourDps > 0).ToList();
        n.BossKills = kills.Count;
        n.BossDps = kills.Count > 0 ? kills.Average(e => e.YourDps) : 0;
        if (kills.MaxBy(e => e.YourDps) is { } top)
        {
            n.BestBossDps = top.YourDps;
            n.BestBossName = top.Title;
        }

        n.Dungeons = fights.Count(e => e.Kind == FightLogKind.Run);
        var deathsByBoss = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (FightLogEntry e in fights.Where(e => e.Kind == FightLogKind.Boss))
        {
            int d = DeathsIn(e);
            n.Deaths += d;
            if (d > 0)
            {
                deathsByBoss[e.Title] = deathsByBoss.GetValueOrDefault(e.Title) + d;
            }
        }

        n.Bosses = kills.GroupBy(e => e.Title).Select(g => new JourneyBoss(g.Key, g.Count(), g.Max(e => e.YourDps), g.Average(e => e.YourDps),
                before?.Bosses.FirstOrDefault(b => b.Name == g.Key)?.AverageDps ?? 0, deathsByBoss.GetValueOrDefault(g.Key)))
            .Concat(deathsByBoss.Keys.Where(k => kills.All(e => e.Title != k)).Select(k => new JourneyBoss(k, 0, 0, 0, 0, deathsByBoss[k])))
            .OrderByDescending(b => b.Kills).ThenByDescending(b => b.Deaths).ToList();

        // play time from the log (since it began), at least the time in fights
        double fightMinutes = fights.Where(e => e.Kind is FightLogKind.Run || (e.Kind == FightLogKind.Boss && string.IsNullOrEmpty(e.Detail))).Sum(e => e.Seconds) / 60;
        var logDays = log.Days.Where(kv => DateTime.TryParseExact(kv.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d) && d >= from.Date && d < to)
            .Select(kv => kv.Value).ToList();
        n.TimeKnown = logDays.Count > 0;
        n.MinutesPlayed = Math.Max(logDays.Sum(d => d.InGame), fightMinutes);
        n.MinutesIdle = logDays.Sum(d => d.Idle);
        n.MinutesFighting = logDays.Sum(d => d.Fighting);

        // loot: what was picked up in the period, at the market's prices
        var drops = new List<JourneyDrop>();
        string language = LocalizationManager.Instance.Language;
        foreach (LootSession session in Aion2Loot.Sessions.Where(s => s.End >= from && s.Start < to))
        {
            foreach (LootItem item in session.Maps.SelectMany(m => m.Items).Where(i => i.First >= from && i.First < to))
            {
                long each = book is not null && book.Items.TryGetValue(item.ItemId, out MarketPrice? p) && p.Value is long v ? v : 0;
                drops.Add(new JourneyDrop(item.ItemId, Aion2ItemNames.NameOf(item.ItemId, language) ?? item.ItemId.ToString(CultureInfo.InvariantCulture),
                    Aion2ItemNames.GradeOf(item.ItemId), item.Count, each * item.Count, item.First));
            }
        }

        n.WorthKnown = book is not null;
        n.LootWorth = drops.Sum(d => d.Worth);
        n.BestDrop = drops.OrderByDescending(d => d.Worth).ThenByDescending(d => d.Grade).FirstOrDefault();

        // Item Level and Combat Power: the checks at the logins (and before the period, the last one then)
        List<CpCheckPoint> inside = points.Where(p => p.At.ToLocalTime() >= from && p.At.ToLocalTime() < to && p.ItemLevel > 0).OrderBy(p => p.At).ToList();
        CpCheckPoint? earlier = points.Where(p => p.At.ToLocalTime() < from && p.ItemLevel > 0).MaxBy(p => p.At);
        n.ItemLevelStart = (earlier ?? inside.FirstOrDefault())?.ItemLevel;
        n.ItemLevelEnd = inside.LastOrDefault()?.ItemLevel ?? earlier?.ItemLevel;
        n.CpEnd = inside.LastOrDefault()?.Cp ?? earlier?.Cp;

        // the days (weeks for periods over 31 days)
        int span = Math.Max(1, (int)Math.Ceiling((to.Date.AddDays(to.TimeOfDay > TimeSpan.Zero ? 1 : 0) - from.Date).TotalDays));
        int step = span > 31 ? 7 : 1;
        for (DateTime day = from.Date; day < to; day = day.AddDays(step))
        {
            DateTime end = day.AddDays(step);
            List<FightLogEntry> f = fights.Where(e => e.Start >= day && e.Start < end).ToList();
            double played = log.Days.Where(kv => DateTime.TryParseExact(kv.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d) && d >= day && d < end).Sum(kv => kv.Value.InGame);
            double boss = f.Where(e => e.Kind == FightLogKind.Boss && string.IsNullOrEmpty(e.Detail)).Sum(e => e.Seconds) / 60;
            double dungeon = f.Where(e => e.Kind == FightLogKind.Run).Sum(e => e.Seconds) / 60;
            List<FightLogEntry> k = f.Where(e => e.Kind == FightLogKind.Boss && e.Killed == true && e.YourDps > 0).ToList();
            List<JourneyDrop> dayDrops = drops.Where(d => d.At >= day && d.At < end).ToList();
            int? level = points.Where(p => p.At.ToLocalTime() < end && p.ItemLevel > 0).MaxBy(p => p.At)?.ItemLevel;
            n.Days.Add(new JourneyDayNumbers(day, Math.Max(played, boss + dungeon), boss, dungeon, dayDrops.Sum(d => d.Worth), k.Count,
                k.Count > 0 ? k.Average(e => e.YourDps) : 0, f.Where(e => e.Kind == FightLogKind.Boss).Sum(DeathsIn), level,
                dayDrops.Where(d => d.Grade >= 4 || d.Worth > 0).OrderByDescending(d => d.Worth).ThenByDescending(d => d.Grade).FirstOrDefault()));
        }

        return n;
    }

    /// <summary>The own deaths in a saved boss fight (its file read once).</summary>
    private static int DeathsIn(FightLogEntry e)
    {
        lock (DeathsKept)
        {
            if (DeathsKept.TryGetValue(e.Id, out int kept))
            {
                return kept;
            }
        }

        int deaths = 0;
        try
        {
            if (FightLog.Open(e) is { } fight && fight.Players.FirstOrDefault(p => p.IsSelf) is { } self)
            {
                deaths = fight.Extras.Deaths?.Count(d => d.Entity == self.Id) ?? 0;
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or JsonException or System.IO.InvalidDataException or InvalidOperationException)
        {
            deaths = 0;
        }

        lock (DeathsKept)
        {
            DeathsKept[e.Id] = deaths;
        }

        return deaths;
    }

    // ---- "You and your class": the website's ranks ----

    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>The character's cleared fights on the website with their ranks (public ones), newest first; null when they cannot be read now.</summary>
    public static async Task<(string? Class, List<JourneyRank> Ranks)?> RanksAsync(JourneyWho who)
    {
        try
        {
            string region = FindPlayerClient.RegionOf(who.ServerId), server = Aion2Servers.NameOf(who.ServerId);
            string url = $"{Brand.Api}/api/char?region={Uri.EscapeDataString(region)}&server={Uri.EscapeDataString(server)}&name={Uri.EscapeDataString(who.Name)}";
            using HttpResponseMessage response = await Http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            JsonElement root = doc.RootElement;
            string? cls = root.TryGetProperty("character", out JsonElement c) && c.TryGetProperty("cls", out JsonElement k) && k.ValueKind == JsonValueKind.String ? k.GetString() : null;
            var ranks = new List<JourneyRank>();
            if (root.TryGetProperty("logs", out JsonElement logs) && logs.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement l in logs.EnumerateArray())
                {
                    if (Str(l, "boss") is not { } boss || When(l) is not DateTime at)
                    {
                        continue;
                    }

                    JsonElement rank = l.TryGetProperty("rank", out JsonElement r) && r.ValueKind == JsonValueKind.Object ? r : default;
                    JsonElement inClass = rank.ValueKind == JsonValueKind.Object && rank.TryGetProperty("all", out JsonElement a) && a.ValueKind == JsonValueKind.Object ? a : default;
                    JsonElement inBracket = rank.ValueKind == JsonValueKind.Object && rank.TryGetProperty("br", out JsonElement b) && b.ValueKind == JsonValueKind.Object ? b : default;
                    ranks.Add(new JourneyRank(boss, at, Num(l, "dps") ?? 0, Num(inClass, "pct"), (int?)Num(inClass, "n"), Num(inBracket, "pct"), (int?)Num(inBracket, "n"),
                        (long?)Num(inBracket, "lo"), (long?)Num(inBracket, "hi")));
                }
            }

            return (cls, ranks);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;

    private static double? Num(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) ? d : null;

    /// <summary>A fight's start: milliseconds or seconds since 1970, or a date.</summary>
    private static DateTime? When(JsonElement l)
    {
        if (!l.TryGetProperty("started", out JsonElement s))
        {
            return null;
        }

        if (s.ValueKind == JsonValueKind.Number && s.TryGetInt64(out long t) && t > 0)
        {
            return (t > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(t) : DateTimeOffset.FromUnixTimeSeconds(t)).LocalDateTime;
        }

        return s.ValueKind == JsonValueKind.String && DateTime.TryParse(s.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime d)
            ? d.ToLocalTime() : null;
    }

    // ---- the pictures ----

    /// <summary>A made-up report for the pictures of the app (a Templar's week).</summary>
    public static JourneyReport Sample(DateTime now)
    {
        var who = new JourneyWho("Aerin", 2302, "Templar");
        DateTime from = now.Date.AddDays(-6);
        int[] played = { 0, 95, 260, 205, 150, 230, 180 }, boss = { 0, 0, 0, 9, 12, 8, 14 }, dungeon = { 0, 0, 40, 0, 0, 25, 30 };
        long[] worth = { 0, 0, 820_000, 0, 60_500, 2_300_000, 1_570_000 };
        int[] kills = { 0, 0, 1, 2, 2, 2, 2 };
        var days = new List<JourneyDayNumbers>();
        for (int i = 0; i < 7; i++)
        {
            JourneyDrop? drop = worth[i] > 1_000_000 ? new JourneyDrop(0, i == 5 ? "Radiant Ruby Stone" : "Warden's Gauntlets", 5, 1, worth[i] / 2, from.AddDays(i).AddHours(20)) : null;
            days.Add(new JourneyDayNumbers(from.AddDays(i), played[i], boss[i], dungeon[i], worth[i], kills[i], kills[i] > 0 ? 80_000 + (i * 1_800) : 0, i == 4 ? 2 : i == 6 ? 1 : 0,
                2_624 + (i * 3), drop));
        }

        var now7 = new JourneyNumbers
        {
            MinutesPlayed = played.Sum(), MinutesIdle = 190, MinutesFighting = 420, TimeKnown = true, LootWorth = worth.Sum(), WorthKnown = true,
            BestDrop = new JourneyDrop(0, "Radiant Ruby Stone", 5, 1, 1_150_000, from.AddDays(5).AddHours(20)), BossKills = 9, BossDps = 84_294, BestBossDps = 94_052,
            BestBossName = "Thamon", Deaths = 3, Dungeons = 2, ItemLevelStart = 2_624, ItemLevelEnd = 2_643, CpEnd = 182_730, Days = days,
            Bosses = new()
            {
                new("Thamon", 4, 94_052, 86_100, 79_400, 1), new("Kromede the Corrupt", 3, 88_300, 83_900, 87_200, 2), new("Debilkarim", 2, 81_200, 80_100, 0, 0),
            },
        };
        var before = new JourneyNumbers
        {
            MinutesPlayed = 1_180, MinutesIdle = 160, TimeKnown = true, LootWorth = 7_800_000, WorthKnown = true, BossKills = 8, BossDps = 72_600, BestBossDps = 88_000,
            Deaths = 0, Dungeons = 3, ItemLevelStart = 2_600, ItemLevelEnd = 2_624,
        };
        return new JourneyReport(who, from, now, now7, before, new() { new JourneyRun("Fire Temple", 14 * 60 + 20, 11 * 60 + 5, now.AddDays(-1)) });
    }

    /// <summary>Made-up ranks for the pictures.</summary>
    public static List<JourneyRank> SampleRanks(DateTime now) => new()
    {
        new("Thamon", now.AddDays(-1), 94_052, 92, 31, 96, 12, 180_000, 190_000),
        new("Kromede the Corrupt", now.AddDays(-2), 88_300, 77, 40, 81, 14, 180_000, 190_000),
        new("Debilkarim", now.AddDays(-3), 81_200, 51, 45, 63, 11, 180_000, 190_000),
        new("Thamon", now.AddDays(-9), 79_400, 71, 30, 74, 12, 170_000, 180_000),
    };
}
