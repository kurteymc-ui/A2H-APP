using System.IO;
using System.Text.Json;

namespace AionDPS.History;

/// <summary>One character's day: minutes the game was read with it, of them minutes fighting and minutes standing idle (no fight, not moving).</summary>
public sealed class JourneyDay
{
    public int InGame { get; set; }
    public int Fighting { get; set; }
    public int Idle { get; set; }
}

/// <summary>A stretch of play with one character: from the first minute the game was read with it to the last, gaps under 15 minutes joined.</summary>
public sealed class JourneySession
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
}

public sealed class JourneyCharacter
{
    /// <summary>By local day, "yyyy-MM-dd".</summary>
    public Dictionary<string, JourneyDay> Days { get; set; } = new(StringComparer.Ordinal);

    public List<JourneySession> Sessions { get; set; } = new();
}

/// <summary>
/// What the Journey page needs and nothing else kept (5 November 2026): how long each character was played, fighting and idle, minute by minute folded into
/// days, and its sessions. Written once a minute by the main window while the game is read (journey.json in the data folder: the last 400 days, the last 300
/// sessions per character). Everything else the page shows comes from what the app kept already (the fights, the loot, the Combat Power checks).
/// </summary>
public static class JourneyLog
{
    private static readonly object Gate = new();
    private static Dictionary<string, JourneyCharacter>? _all;
    private static DateTime _savedAt = DateTime.MinValue;
    private static readonly TimeSpan SessionGap = TimeSpan.FromMinutes(15);

    private static string DataFolder => Environment.GetEnvironmentVariable("AIONDPS_DATA_DIR") is { Length: > 0 } testDir
        ? testDir : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Brand.DataFolder);

    public static string FilePath => Path.Combine(DataFolder, "journey.json");

    /// <summary>The key of a character: "name@server id".</summary>
    public static string Key(string name, int serverId) => name + "@" + serverId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static Dictionary<string, JourneyCharacter> All()
    {
        if (_all is not null)
        {
            return _all;
        }

        try
        {
            _all = File.Exists(FilePath) ? JsonSerializer.Deserialize<Dictionary<string, JourneyCharacter>>(File.ReadAllText(FilePath)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _all = null;
        }

        return _all ??= new Dictionary<string, JourneyCharacter>(StringComparer.Ordinal);
    }

    /// <summary>One minute of play with <paramref name="character"/> (its <see cref="Key"/>): fighting, else idle when it did not move. <paramref name="now"/> local.</summary>
    public static void Minute(string character, bool fighting, bool moved, DateTime now)
    {
        lock (Gate)
        {
            Dictionary<string, JourneyCharacter> all = All();
            if (!all.TryGetValue(character, out JourneyCharacter? c))
            {
                all[character] = c = new JourneyCharacter();
            }

            string day = now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            if (!c.Days.TryGetValue(day, out JourneyDay? d))
            {
                c.Days[day] = d = new JourneyDay();
                foreach (string old in c.Days.Keys.OrderBy(k => k, StringComparer.Ordinal).SkipLast(400).ToList())
                {
                    c.Days.Remove(old);
                }
            }

            d.InGame++;
            if (fighting)
            {
                d.Fighting++;
            }
            else if (!moved)
            {
                d.Idle++;
            }

            DateTime utc = now.ToUniversalTime();
            if (c.Sessions.Count > 0 && utc - c.Sessions[^1].End <= SessionGap)
            {
                c.Sessions[^1].End = utc;
            }
            else
            {
                c.Sessions.Add(new JourneySession { Start = utc.AddMinutes(-1), End = utc });
                if (c.Sessions.Count > 300)
                {
                    c.Sessions.RemoveRange(0, c.Sessions.Count - 300);
                }
            }

            if (DateTime.UtcNow - _savedAt >= TimeSpan.FromMinutes(2))
            {
                SaveLocked();
            }
        }
    }

    /// <summary>Writes what is held (also when the app closes).</summary>
    public static void Save()
    {
        lock (Gate)
        {
            if (_all is not null)
            {
                SaveLocked();
            }
        }
    }

    private static void SaveLocked()
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_all));
            File.Move(temp, FilePath, overwrite: true);
            _savedAt = DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the next minute tries again
        }
    }

    /// <summary>A copy of a character's days and sessions (empty when it was never played since the log began).</summary>
    public static JourneyCharacter Of(string character)
    {
        lock (Gate)
        {
            if (!All().TryGetValue(character, out JourneyCharacter? c))
            {
                return new JourneyCharacter();
            }

            return new JourneyCharacter
            {
                Days = c.Days.ToDictionary(kv => kv.Key, kv => new JourneyDay { InGame = kv.Value.InGame, Fighting = kv.Value.Fighting, Idle = kv.Value.Idle }, StringComparer.Ordinal),
                Sessions = c.Sessions.Select(s => new JourneySession { Start = s.Start, End = s.End }).ToList(),
            };
        }
    }

    /// <summary>The characters in the log.</summary>
    public static IReadOnlyList<string> Characters()
    {
        lock (Gate)
        {
            return All().Keys.ToList();
        }
    }

    /// <summary>For the pictures and the self-check: a made-up log in place of the file's.</summary>
    internal static void UseForPictures(Dictionary<string, JourneyCharacter> all)
    {
        lock (Gate)
        {
            _all = all;
            _savedAt = DateTime.MaxValue; // never written
        }
    }
}
