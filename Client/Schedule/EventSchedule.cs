using System.IO;
using System.Text.Json;

namespace AionDPS.Schedule;

/// <summary>One recurring time window of an event: the weekdays (0 = Sunday ... 6 = Saturday, like
/// <see cref="DayOfWeek"/>) it starts on and its start and end time of day. An end at or before the
/// start runs past midnight.</summary>
public sealed record EventWindow(IReadOnlyList<int> Days, TimeSpan Start, TimeSpan End);

/// <summary>A single dated occurrence (the next Abyss raid, read off the game's countdown) in local
/// time. Without a known length (<see cref="Minutes"/> 0) it is only ever "starts in ...".</summary>
public sealed record EventDate(DateTime At, int Minutes);

/// <summary>A recurring game event (a battlefield's matchmaking times), named per language. An
/// <see cref="Always"/> event can be queued for at any time (the 1 vs 1 arena) and has no windows.
/// <paramref name="Icon"/>, <paramref name="ShortNames"/>, <paramref name="Sure"/> and <paramref name="OpenMinutes"/> are the table's "icon", "short", "sure" and
/// "open": the Timers page's picture and colour, the name in the ring's key, how sure the time is, and how long the entrance stays open (the rift's portals).</summary>
public sealed record ScheduledEvent(string Id, string Kind, string? Players, IReadOnlyDictionary<string, string> Names, IReadOnlyList<EventWindow> Windows, bool Always = false, IReadOnlyList<EventDate>? Dates = null, bool Utc = false, bool Possible = false, string? Zone = null,
    string? Icon = null, IReadOnlyDictionary<string, string>? ShortNames = null, string? Sure = null, int OpenMinutes = 0)
{
    /// <summary>The times of <see cref="Windows"/> and <see cref="Dates"/> are UTC, the clock the game's events run on (the same in every region); they are turned into the PC's local time when the occurrences are worked out. Without it they are read as local time.</summary>
    public bool IsUtc => Utc;

    /// <summary>The time zone the times of this event are given in when it has its own (the spacetime rift runs on the European local clock, so it keeps 14:00, 17:00 ... through the change to winter time), else null: <see cref="IsUtc"/> or local time applies.</summary>
    public TimeZoneInfo? SourceZone { get; } = Zone is null ? null : EventSchedule.FindZone(Zone);

    /// <summary>The slots are only possible times: the game starts the event at random within them (the Dimensional Invasion does not take place at every slot), so the timetable says "possible" instead of promising it.</summary>
    public bool IsPossible => Possible;

    /// <summary>The name in a language (an ISO 639-1 code), else English, else the id.</summary>
    public string NameIn(string language) =>
        Names.TryGetValue(language, out string? name) ? name : Names.TryGetValue("en", out string? english) ? english : Id;

    /// <summary>A short name for tight places (the key under the Timers page's ring): the table's short name in the language, else the full name in it,
    /// else the English short name (a language the table has no names in would otherwise get the long English one).</summary>
    public string ShortIn(string language) =>
        ShortNames is not null && ShortNames.TryGetValue(language, out string? brief) ? brief
        : Names.ContainsKey(language) ? NameIn(language)
        : ShortNames is not null && ShortNames.TryGetValue("en", out string? english) ? english
        : NameIn(language);

    /// <summary>The event's look on the Timers page and its overlay: the name of its picture (assets/aion2/schedule/icons/&lt;look&gt;.png) and of its colour,
    /// the same as on the website. From the table's "icon"; a table without it (an older server's answer, kept in the cache) falls back to the known ids.</summary>
    public string Look => Icon is { Length: > 0 } icon ? icon : Id switch
    {
        "shugo_festival" => "shugo",
        "beritra_air_raid" => "invasion",
        "spacetime_rift" => "rift",
        "watcher_kaira" => "kaira",
        "arena_of_strategy" or "arena_of_silence" or "arena_of_cooperation" => "arena",
        "abyss_raid" => "siege",
        "executor_bosses" => "exec",
        "guardian_lord_nahma" => "nahma",
        "middle_reshanta_bosses" => "mboss",
        _ => Id,
    };

    /// <summary>"reported" in the table: fewer reports, or the sources disagree, so the player should check the time in game. "checked" (or nothing): in-game
    /// reports and several timers agree.</summary>
    public bool IsReported => string.Equals(Sure, "reported", StringComparison.OrdinalIgnoreCase);

    /// <summary>Starts every hour or so (the Shugo Festival, the Invasion): left out where only the big events count (the middle of the ring).</summary>
    public bool IsFrequent => Windows.Count(w => w.Days.Count >= 7) >= 12;

    /// <summary>Not on every day (the siege, the Abyss bosses): these make the Timers page's week.</summary>
    public bool IsWeekly => !Always && Windows.Count > 0 && Windows.Any(w => w.Days.Count < 7);
}

/// <summary>An event that is on right now (<see cref="Remaining"/> until it ends) or starts soon
/// (<see cref="Until"/> it starts).</summary>
public sealed record EventOccurrence(ScheduledEvent Event, DateTime Start, DateTime End)
{
    public bool IsAlways => Event.Always;

    public bool IsActiveAt(DateTime now) => IsAlways || (Start <= now && now < End);

    /// <summary>Until when the entrance is open (the rift's portals close after ten minutes while the rift stays up for the hour); the end for an event without one.</summary>
    public DateTime OpenUntil => Event.OpenMinutes > 0 && Start.AddMinutes(Event.OpenMinutes) < End ? Start.AddMinutes(Event.OpenMinutes) : End;
}

/// <summary>A timetable as it was read: its events, and the region whose server clock its times follow ("eu", "nae" ...; null = the file's own, European clock).</summary>
public sealed record ScheduleTable(IReadOnlyList<ScheduledEvent> Events, string? Region);

/// <summary>
/// The event timetable from assets/aion2/schedule/events.json and the question the timetable overlay
/// asks of it: what is active NOW, and what starts within the next minutes. The times are those the
/// game's own pages show, on the European clock ("timeZone": "Europe/Berlin": the events keep their hour through the change to winter time), as UTC ("UTC") or, with "local", as the PC's local time.
/// </summary>
public static class EventSchedule
{
    private static readonly string Path_ = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "schedule", "events.json");

    private static readonly ScheduleTable Empty = new(Array.Empty<ScheduledEvent>(), null);

    /// <summary>The table shipped with the client (the self-check reads it with a region's clocks).</summary>
    public static string ShippedPath => Path_;

    private static volatile ScheduleTable? _table;

    /// <summary>The timetable in use: the last one the server sent (<see cref="ScheduleClient"/>), else the one shipped with the client.</summary>
    public static ScheduleTable Table => _table ??= LoadCachedOrShipped();

    public static IReadOnlyList<ScheduledEvent> Events => Table.Events;

    /// <summary>The region whose server clock the table in use follows ("eu", "nae", "naw", "latam", "asia"); null = the file's own, the European clock.</summary>
    public static string? Region => Table.Region;

    /// <summary>Takes a timetable the server sent in use, once it was read as a whole.</summary>
    public static void Replace(ScheduleTable table) => _table = table;

    private static ScheduleTable LoadCachedOrShipped()
    {
        ScheduleTable cached = LoadTable(ScheduleClient.CachePath);
        return cached.Events.Count > 0 ? cached : LoadTable(Path_);
    }

    public static IReadOnlyList<ScheduledEvent> Load(string path) => LoadTable(path).Events;

    /// <summary>Reads a timetable file (see <see cref="ParseTable"/>); an empty table when there is none or it cannot be read.</summary>
    public static ScheduleTable LoadTable(string path, string? region = null)
    {
        try
        {
            return File.Exists(path) ? ParseTable(File.ReadAllText(path), region) : Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Empty;
        }
    }

    /// <summary>The server could not be asked for the own region's table: when the table in use follows another region's clock, the table shipped with the
    /// client is taken with the own region's clocks (its "zones"), so the times still are those of the own server. Nothing changes for an unknown region.</summary>
    public static void UseShippedFor(string? region)
    {
        if (region is null || string.Equals(Region ?? "eu", region, StringComparison.Ordinal))
        {
            return;
        }

        ScheduleTable shipped = LoadTable(Path_, region);
        if (shipped.Events.Count > 0)
        {
            _table = shipped;
        }
    }

    /// <summary>Reads a timetable from its JSON text; an empty list when it is not one.</summary>
    public static IReadOnlyList<ScheduledEvent> Parse(string json) => ParseTable(json).Events;

    /// <summary>
    /// Reads a timetable from its JSON text; an empty table when it is not one. Each region runs its events on its own server clock (21:00 on NA East is
    /// 21:00 New York time): an event names the clock per region in "zones" ({"nae": "America/New_York"}). The server's answer for a region has already put
    /// that clock into each event's own "timeZone" and says which region it is ("region"); the shipped file is read with <paramref name="region"/> for the same.
    /// </summary>
    public static ScheduleTable ParseTable(string json, string? region = null)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var events = new List<ScheduledEvent>();
            JsonElement root = document.RootElement;
            if (region is null && root.TryGetProperty("region", out var regionElement) && regionElement.ValueKind == JsonValueKind.String)
            {
                region = regionElement.GetString();
            }

            bool hasZone = root.TryGetProperty("timeZone", out var zone);
            bool utc = hasZone && string.Equals(zone.GetString(), "UTC", StringComparison.OrdinalIgnoreCase);
            // A named zone for the whole table (the game's events run on the European clock); "UTC" and "local" are handled by the flag above.
            string? tableZone = hasZone && zone.GetString() is { } zoneName && !utc && !string.Equals(zoneName, "local", StringComparison.OrdinalIgnoreCase) ? zoneName : null;
            foreach (JsonElement e in root.GetProperty("events").EnumerateArray())
            {
                var names = e.GetProperty("names").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
                var windows = e.GetProperty("windows").EnumerateArray()
                    .Select(w => new EventWindow(
                        w.GetProperty("days").EnumerateArray().Select(d => d.GetInt32()).ToList(),
                        TimeSpan.Parse(w.GetProperty("start").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
                        TimeSpan.Parse(w.GetProperty("end").GetString()!, System.Globalization.CultureInfo.InvariantCulture)))
                    .ToList();
                var dates = e.TryGetProperty("dates", out var dateList)
                    ? dateList.EnumerateArray().Select(x => new EventDate(
                        DateTime.Parse(x.GetProperty("at").GetString()!, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None),
                        x.TryGetProperty("minutes", out var minutes) ? minutes.GetInt32() : 0)).ToList()
                    : new List<EventDate>();
                // the event's own clock, else the own region's from "zones", else the table's
                string? eventZone = e.TryGetProperty("timeZone", out var ownZone) ? ownZone.GetString()
                    : region is not null && e.TryGetProperty("zones", out var zones) && zones.ValueKind == JsonValueKind.Object
                      && zones.TryGetProperty(region, out var regionZone) && regionZone.ValueKind == JsonValueKind.String ? regionZone.GetString()
                    : tableZone;
                Dictionary<string, string>? shortNames = e.TryGetProperty("short", out var shortElement) && shortElement.ValueKind == JsonValueKind.Object
                    ? shortElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "")
                    : null;
                events.Add(new ScheduledEvent(
                    e.GetProperty("id").GetString()!,
                    e.TryGetProperty("kind", out var kind) ? kind.GetString() ?? "" : "",
                    e.TryGetProperty("players", out var players) ? players.GetString() : null,
                    names, windows, e.TryGetProperty("always", out var always) && always.GetBoolean(), dates, utc,
                    e.TryGetProperty("possible", out var possible) && possible.GetBoolean(), eventZone,
                    Icon: e.TryGetProperty("icon", out var icon) && icon.ValueKind == JsonValueKind.String ? icon.GetString() : null,
                    ShortNames: shortNames,
                    Sure: e.TryGetProperty("sure", out var sure) && sure.ValueKind == JsonValueKind.String ? sure.GetString() : null,
                    OpenMinutes: e.TryGetProperty("open", out var open) && open.ValueKind == JsonValueKind.Number ? open.GetInt32() : 0));
            }

            return new ScheduleTable(events, region);
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        {
            // No table: an empty timetable.
            return Empty;
        }
    }

    /// <summary>The Windows ids of the region clocks, for a machine whose .NET cannot turn an IANA name into its own id.</summary>
    private static readonly Dictionary<string, string> WindowsZoneIds = new(StringComparer.Ordinal)
    {
        ["Europe/Berlin"] = "W. Europe Standard Time",
        ["America/New_York"] = "Eastern Standard Time",
        ["America/Los_Angeles"] = "Pacific Standard Time",
        ["America/Sao_Paulo"] = "E. South America Standard Time",
        ["Asia/Tokyo"] = "Tokyo Standard Time",
    };

    /// <summary>A time zone by its IANA name (Windows only knows its own ids), or null when this machine has neither.</summary>
    public static TimeZoneInfo? FindZone(string id)
    {
        foreach (string candidate in WindowsZoneIds.TryGetValue(id, out string? windowsId) ? new[] { id, windowsId } : new[] { id })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(candidate);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
            }
        }

        return null;
    }

    /// <summary>
    /// The occurrences on at <paramref name="now"/> (<c>Active</c>, soonest end first) and those
    /// starting within <paramref name="lookahead"/> (<c>Soon</c>, soonest start first).
    /// </summary>
    public static (List<EventOccurrence> Active, List<EventOccurrence> Soon) Evaluate(
        IEnumerable<ScheduledEvent> events, DateTime now, TimeSpan lookahead)
    {
        var active = new List<EventOccurrence>();
        var soon = new List<EventOccurrence>();
        foreach (ScheduledEvent scheduled in events)
        {
            if (scheduled.Always)
            {
                active.Add(new EventOccurrence(scheduled, now.Date, now.Date.AddDays(1)));
                continue;
            }

            foreach (EventOccurrence occurrence in OccurrencesAround(scheduled, now))
            {
                if (occurrence.IsActiveAt(now))
                {
                    active.Add(occurrence);
                }
                else if (occurrence.Start > now && occurrence.Start - now <= lookahead)
                {
                    soon.Add(occurrence);
                }
            }
        }

        return (active.OrderBy(o => o.IsAlways).ThenBy(o => o.End).ToList(), soon.OrderBy(o => o.Start).ToList());
    }

    /// <summary>The occurrences of an event around <paramref name="now"/> (from yesterday to tomorrow), for the sound reminders.</summary>
    public static IEnumerable<EventOccurrence> Upcoming(ScheduledEvent scheduled, DateTime now) => OccurrencesAround(scheduled, now);

    /// <summary>The next occurrence of an event after <paramref name="now"/> (within a week), or null.</summary>
    public static EventOccurrence? NextAfter(ScheduledEvent scheduled, DateTime now) =>
        scheduled.Always ? null : OccurrencesAround(scheduled, now, daysBefore: 0, daysAfter: 7).Where(o => o.Start > now).OrderBy(o => o.Start).FirstOrDefault();

    /// <summary>Every occurrence of an event that overlaps <paramref name="from"/> to <paramref name="to"/> (local times), soonest first: the Timers page's ring
    /// (the next 24 hours) and its week. None for an event without times.</summary>
    public static List<EventOccurrence> Between(ScheduledEvent scheduled, DateTime from, DateTime to)
    {
        var list = new List<EventOccurrence>();
        if (scheduled.Always || to <= from)
        {
            return list;
        }

        int days = (int)Math.Ceiling((to - from).TotalDays) + 1;
        foreach (EventOccurrence occurrence in OccurrencesAround(scheduled, from, daysBefore: 1, daysAfter: days))
        {
            if (occurrence.End > from && occurrence.Start < to)
            {
                list.Add(occurrence);
            }
        }

        list.Sort((a, b) => a.Start.CompareTo(b.Start));
        return list;
    }

    /// <summary>Occurrences that start from the day before to the day after (a window past midnight
    /// started yesterday).</summary>
    private static IEnumerable<EventOccurrence> OccurrencesAround(ScheduledEvent scheduled, DateTime now, int daysBefore = 1, int daysAfter = 1)
    {
        // Events on their own clock (UTC or a named zone): the days and times are worked out on that clock and handed back as local time (so a winter or a PC in another zone shows the right hour).
        TimeZoneInfo? zone = scheduled.SourceZone ?? (scheduled.IsUtc ? TimeZoneInfo.Utc : null);
        DateTime reference = zone is null ? now : TimeZoneInfo.ConvertTimeFromUtc(now.ToUniversalTime(), zone);
        DateTime ToLocalTime(DateTime t)
        {
            if (zone is null)
            {
                return t;
            }

            // A time in the hour that the spring change skips does not exist: it falls to the hour after.
            if (zone.IsInvalidTime(t))
            {
                t = t.AddHours(1);
            }

            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(t, DateTimeKind.Unspecified), zone).ToLocalTime();
        }

        foreach (EventDate date in scheduled.Dates ?? Array.Empty<EventDate>())
        {
            yield return new EventOccurrence(scheduled, ToLocalTime(date.At), ToLocalTime(date.At.AddMinutes(date.Minutes)));
        }

        for (int offset = -daysBefore; offset <= daysAfter; offset++)
        {
            DateTime day = reference.Date.AddDays(offset);
            foreach (EventWindow window in scheduled.Windows)
            {
                if (!window.Days.Contains((int)day.DayOfWeek))
                {
                    continue;
                }

                DateTime start = day + window.Start;
                DateTime end = day + window.End;
                if (end <= start)
                {
                    end = end.AddDays(1);
                }

                yield return new EventOccurrence(scheduled, ToLocalTime(start), ToLocalTime(end));
            }
        }
    }
}
