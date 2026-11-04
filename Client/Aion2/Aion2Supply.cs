using System.Buffers.Binary;
using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2;

/// <summary>One supply request of the game's table: any of <see cref="Items"/> counts towards <see cref="Target"/>; what it gives (<see cref="Rewards"/>).
/// <see cref="Kind"/>: Daily (the Emergency tab), Weekly, Craft or Boss (the two Regular tabs).</summary>
public sealed record SupplyRequest(int Id, char Faction, string Kind, bool Rare, int Target, IReadOnlyList<int> Items, IReadOnlyList<(int Item, int Count)> Rewards,
    IReadOnlyDictionary<string, string> Names);

/// <summary>A request on a character's tab and how far it is.</summary>
public sealed class SupplyEntry
{
    public int Id { get; set; }
    public int Progress { get; set; }
}

/// <summary>One tab as the login list gave it: its group (a new one each day for Emergency, each week for Weekly), the tab (1 Emergency, 2 Weekly,
/// 3 Regular: Craft, 4 Regular: Boss), when it began and when it ends (Unix ms; the Regular tabs end in 2099), and its requests.</summary>
public sealed class SupplyGroup
{
    public int Group { get; set; }
    public int Tab { get; set; }
    public long BeginMs { get; set; }
    public long EndMs { get; set; }
    public List<SupplyEntry> Requests { get; set; } = new();

    public bool Over(DateTime utcNow) => EndMs > 0 && DateTimeOffset.FromUnixTimeMilliseconds(EndMs).UtcDateTime <= utcNow;
}

/// <summary>What the meter last saw of a character's supply requests.</summary>
public sealed class SupplyCharacter
{
    public DateTime SavedAt { get; set; }
    public List<SupplyGroup> Groups { get; set; } = new();
}

/// <summary>
/// The supply requests ("Supply Request" window: Emergency, Weekly, Regular: Craft, Regular: Boss) of every character:
/// <list type="bullet">
/// <item><c>0x56e2</c> at a login: <c>n u8 | n x ( group u32 | tab u8 | begin u64 ms | end u64 ms | m u8 | m x ( group u32 | request u32 | progress u32 ) )</c>;
/// the list carries one byte more before every eighth entry (the 2nd, 10th, 18th ... of the frame; meaning unknown), stepped over here.</item>
/// <item><c>0x3ee2</c> at a delivery: <c>00 00 | group u32 | request u32 | progress u32 (absolute) | done u8 | k u8 | k x 17 bytes (the items given)</c>.</item>
/// </list>
/// The requests themselves (target, accepted items, rewards, names) are assets/aion2/supply/supply-requests.json.
/// A login's list comes before the game names the character: it waits for the name, as the achievement counters do (<see cref="Aion2Achievements"/>).
/// Kept in aion2-supply.json beside the settings.
/// </summary>
public static class Aion2Supply
{
    public const int ListOpcode = 0x56e2, ProgressOpcode = 0x3ee2;

    private static readonly object Gate = new();
    private static readonly Dictionary<string, SupplyCharacter> Characters = new(StringComparer.Ordinal);
    private static List<SupplyGroup>? _pending;
    private static string? _pendingWas;
    private static DateTime _pendingAt;
    private static bool _persist, _dirty;
    private static DateTime _savedAt = DateTime.MinValue;
    private static IReadOnlyDictionary<int, SupplyRequest>? _requests;
    private static IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>> _tabs = new Dictionary<int, IReadOnlyDictionary<string, string>>();

    public static event Action? Changed;

    public static Func<string?> CurrentName { get; set; } = () => Aion2Tickets.CurrentName;

    /// <summary>The wall clock the waiting is measured on (a test moves it on).</summary>
    public static Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    public static string DataPath => Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "supply", "supply-requests.json");

    public static string StorePath => Path.Combine(Path.GetDirectoryName(Aion2CharacterStore.DefaultPath)!, "aion2-supply.json");

    // ---- the table ----

    /// <summary>Every request of both factions by id (empty when the data file is missing).</summary>
    public static IReadOnlyDictionary<int, SupplyRequest> Requests
    {
        get
        {
            lock (Gate)
            {
                if (_requests is null)
                {
                    try
                    {
                        _requests = File.Exists(DataPath) ? Parse(File.ReadAllText(DataPath), out _tabs) : new Dictionary<int, SupplyRequest>();
                    }
                    catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException or FormatException or KeyNotFoundException)
                    {
                        _requests = new Dictionary<int, SupplyRequest>();
                    }
                }

                return _requests;
            }
        }
    }

    public static SupplyRequest? Find(int id) => Requests.TryGetValue(id, out SupplyRequest? r) ? r : null;

    /// <summary>The game's name of a tab ("Emergency", "Weekly", "Regular: Craft", "Regular: Boss") in the language, else English.</summary>
    public static string TabName(int tab, string language)
    {
        _ = Requests;
        return _tabs.TryGetValue(tab, out IReadOnlyDictionary<string, string>? texts) ? Text(texts, language) : "#" + tab;
    }

    /// <summary>A text in the language (the meter's codes; the client has no Chinese), else English.</summary>
    public static string Text(IReadOnlyDictionary<string, string> texts, string language) =>
        texts.TryGetValue(language, out string? s) ? s : texts.TryGetValue("en", out string? en) ? en : texts.Values.FirstOrDefault() ?? "";

    /// <summary>The tab a request is shown on, from its table group (for a delivery whose login list the meter did not see).</summary>
    public static int TabOf(string kind) => kind switch { "Daily" => 1, "Weekly" => 2, "Craft" => 3, "Boss" => 4, _ => 0 };

    /// <summary>Reads the data file's text.</summary>
    public static IReadOnlyDictionary<int, SupplyRequest> Parse(string json, out IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>> tabs)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        var t = new Dictionary<int, IReadOnlyDictionary<string, string>>();
        foreach (JsonProperty p in root.GetProperty("tabs").EnumerateObject())
        {
            if (int.TryParse(p.Name, out int tab))
            {
                t[tab] = Texts(p.Value);
            }
        }

        tabs = t;
        var names = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (JsonProperty p in root.GetProperty("names").EnumerateObject())
        {
            names[p.Name] = Texts(p.Value);
        }

        var list = new Dictionary<int, SupplyRequest>();
        foreach (JsonProperty p in root.GetProperty("requests").EnumerateObject())
        {
            if (!int.TryParse(p.Name, out int id))
            {
                continue;
            }

            JsonElement r = p.Value;
            var items = r.GetProperty("t").EnumerateArray().Select(x => x.GetInt32()).ToList();
            var rewards = r.GetProperty("r").EnumerateArray().Select(x => (x[0].GetInt32(), x[1].GetInt32())).ToList();
            IReadOnlyDictionary<string, string> name = names.TryGetValue(r.GetProperty("nm").GetString() ?? "", out var n) ? n : new Dictionary<string, string>();
            list[id] = new SupplyRequest(id, r.GetProperty("f").GetString() == "D" ? 'D' : 'L', r.GetProperty("k").GetString() ?? "", r.GetProperty("rare").GetInt32() == 1,
                r.GetProperty("n").GetInt32(), items, rewards, name);
        }

        return list;
    }

    private static IReadOnlyDictionary<string, string> Texts(JsonElement e)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty p in e.EnumerateObject())
        {
            if (p.Value.GetString() is { Length: > 0 } s)
            {
                d[p.Name] = s;
            }
        }

        return d;
    }

    // ---- the frames ----

    /// <summary>A login's list (<see cref="ListOpcode"/>): the tabs with their requests; the empty placeholder tabs (group 0) are left out.</summary>
    public static bool TryParseList(ReadOnlySpan<byte> frame, out List<SupplyGroup> groups)
    {
        groups = new List<SupplyGroup>();
        if (frame.Length < 3 || ((frame[0] << 8) | frame[1]) != ListOpcode || frame[2] is 0 or > 16)
        {
            return false;
        }

        int count = frame[2], p = 3;
        for (int g = 0; g < count; g++)
        {
            if (p + 22 > frame.Length)
            {
                return false;
            }

            int group = BinaryPrimitives.ReadInt32LittleEndian(frame[p..]);
            var tab = new SupplyGroup
            {
                Group = group, Tab = frame[p + 4], BeginMs = BinaryPrimitives.ReadInt64LittleEndian(frame[(p + 5)..]), EndMs = BinaryPrimitives.ReadInt64LittleEndian(frame[(p + 13)..]),
            };
            int n = frame[p + 21];
            p += 22;
            for (int i = 0; i < n; i++)
            {
                // the extra byte before every eighth entry: an entry starts with its tab's group id
                if (p + 13 <= frame.Length && BinaryPrimitives.ReadInt32LittleEndian(frame[p..]) != group && BinaryPrimitives.ReadInt32LittleEndian(frame[(p + 1)..]) == group)
                {
                    p++;
                }

                if (p + 12 > frame.Length || BinaryPrimitives.ReadInt32LittleEndian(frame[p..]) != group)
                {
                    return false;
                }

                int progress = BinaryPrimitives.ReadInt32LittleEndian(frame[(p + 8)..]);
                if (progress < 0)
                {
                    return false;
                }

                tab.Requests.Add(new SupplyEntry { Id = BinaryPrimitives.ReadInt32LittleEndian(frame[(p + 4)..]), Progress = progress });
                p += 12;
            }

            if (group != 0)
            {
                groups.Add(tab);
            }
        }

        return p == frame.Length && groups.Count > 0;
    }

    /// <summary>A delivery (<see cref="ProgressOpcode"/>): the request's new progress (absolute) and whether it is done now.</summary>
    public static bool TryParseProgress(ReadOnlySpan<byte> frame, out int group, out int request, out int progress, out bool done)
    {
        group = request = progress = 0;
        done = false;
        if (frame.Length < 18 || ((frame[0] << 8) | frame[1]) != ProgressOpcode || frame[2] != 0 || frame[3] != 0)
        {
            return false;
        }

        group = BinaryPrimitives.ReadInt32LittleEndian(frame[4..]);
        request = BinaryPrimitives.ReadInt32LittleEndian(frame[8..]);
        progress = BinaryPrimitives.ReadInt32LittleEndian(frame[12..]);
        done = frame[16] == 1;
        int items = frame[17];
        return frame.Length >= 18 + (17 * items) && progress >= 0 && frame[16] <= 1 && group > 0 && Find(request) is not null; // any group: the request must be one of the game's (a range fitted to the groups known now would drop a later season's)
    }

    // ---- what the frames tell ----

    /// <summary>A login's list: it waits until it surely belongs to the character named (the name changed since, or 10 s under the same name).</summary>
    public static void NoteList(List<SupplyGroup> groups)
    {
        lock (Gate)
        {
            _pending = groups;
            _pendingWas = CurrentName();
            _pendingAt = Clock();
            TryAssign(Clock());
        }

        Changed?.Invoke();
    }

    /// <summary>A delivery: the request's progress on the character playing (or on the list still waiting for its name).</summary>
    public static void NoteProgress(int group, int request, int progress)
    {
        lock (Gate)
        {
            List<SupplyGroup>? groups = _pending ?? (CurrentName() is { } name ? Mine(name).Groups : null);
            if (groups is null)
            {
                return;
            }

            SupplyGroup? tab = groups.FirstOrDefault(g => g.Group == group);
            if (tab is null)
            {
                // the meter missed the login list (started later): the tab is known from the request; its end from the game's reset times
                int number = Find(request) is { } r ? TabOf(r.Kind) : 0;
                groups.RemoveAll(g => g.Tab == number);
                tab = new SupplyGroup { Group = group, Tab = number, EndMs = number switch { 1 => NextReset(Clock(), daily: true), 2 => NextReset(Clock(), daily: false), _ => 0 } };
                groups.Add(tab);
            }

            SupplyEntry? entry = tab.Requests.FirstOrDefault(e => e.Id == request);
            if (entry is null)
            {
                tab.Requests.Add(entry = new SupplyEntry { Id = request });
            }

            entry.Progress = Math.Max(entry.Progress, progress);
            _dirty = true;
        }

        Changed?.Invoke();
    }

    /// <summary>The game's reset after the moment: 07:00 UTC daily, Wednesday 07:00 UTC weekly (as the login list's end times say), in Unix ms.</summary>
    public static long NextReset(DateTime utcNow, bool daily)
    {
        DateTime at = utcNow.Date.AddHours(7);
        if (at <= utcNow)
        {
            at = at.AddDays(1);
        }

        while (!daily && at.DayOfWeek != DayOfWeek.Wednesday)
        {
            at = at.AddDays(1);
        }

        return new DateTimeOffset(at, TimeSpan.Zero).ToUnixTimeMilliseconds();
    }

    /// <summary>A waiting list goes to the character now named, once that is sure (called when the tickets change and by <see cref="Of"/>).</summary>
    public static void AssignPending()
    {
        bool done;
        lock (Gate)
        {
            done = TryAssign(Clock());
        }

        if (done)
        {
            Changed?.Invoke();
        }
    }

    private static bool TryAssign(DateTime now)
    {
        if (_pending is null || CurrentName() is not { } name || (name == _pendingWas && now - _pendingAt < TimeSpan.FromSeconds(10)))
        {
            return false;
        }

        SupplyCharacter mine = Mine(name);
        mine.Groups = _pending;
        mine.SavedAt = now;
        _pending = null;
        _dirty = true;
        return true;
    }

    private static SupplyCharacter Mine(string name)
    {
        if (!Characters.TryGetValue(name, out SupplyCharacter? mine))
        {
            Characters[name] = mine = new SupplyCharacter { SavedAt = Clock() };
        }

        return mine;
    }

    /// <summary>A character's tabs as last seen, in the game's order (null when the meter never saw its list). A tab whose end has passed is still in it:
    /// <see cref="SupplyGroup.Over"/> tells (its requests are new ones after the next login).</summary>
    public static SupplyCharacter? Of(string name)
    {
        AssignPending();
        lock (Gate)
        {
            if (!Characters.TryGetValue(name, out SupplyCharacter? c))
            {
                return null;
            }

            return new SupplyCharacter
            {
                SavedAt = c.SavedAt,
                Groups = c.Groups.OrderBy(g => g.Tab).Select(g => new SupplyGroup
                {
                    Group = g.Group, Tab = g.Tab, BeginMs = g.BeginMs, EndMs = g.EndMs,
                    Requests = g.Requests.Select(e => new SupplyEntry { Id = e.Id, Progress = e.Progress }).ToList(),
                }).ToList(),
            };
        }
    }

    /// <summary>Puts a character's tabs in by hand (tests, the pictures).</summary>
    public static void Put(string name, SupplyCharacter state)
    {
        lock (Gate)
        {
            Characters[name] = state;
        }
    }

    /// <summary>Forget everything (a test, a replay).</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            Characters.Clear();
            _pending = null;
            _pendingWas = null;
            _dirty = false;
        }
    }

    // ---- the file ----

    /// <summary>The meter (not the replay tools) keeps the requests in a file between two starts.</summary>
    public static void EnablePersistence()
    {
        lock (Gate)
        {
            _persist = true;
            try
            {
                if (File.Exists(StorePath) && JsonSerializer.Deserialize<Dictionary<string, SupplyCharacter>>(File.ReadAllText(StorePath)) is { } loaded)
                {
                    foreach ((string name, SupplyCharacter c) in loaded)
                    {
                        if (name.Length > 0 && c.Groups is not null)
                        {
                            Characters[name] = c;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
            {
                if (ex is JsonException)
                {
                    AionDPS.AtomicFile.KeepDamaged(StorePath);
                }

                // a broken file: each character's requests come again with its next login
            }
        }

        Aion2Tickets.Changed += AssignPending;
        Changed += () => Flush();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush(force: true);
    }

    /// <summary>Writes the file (at most every 5 seconds unless forced).</summary>
    public static void Flush(bool force = false)
    {
        string json;
        lock (Gate)
        {
            if (!_persist || !_dirty || (!force && DateTime.UtcNow - _savedAt < TimeSpan.FromSeconds(5)))
            {
                return;
            }

            json = JsonSerializer.Serialize(Characters);
            _dirty = false;
            _savedAt = DateTime.UtcNow;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            AionDPS.AtomicFile.WriteAllText(StorePath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lock (Gate)
            {
                _dirty = true; // kept in memory; the next flush tries again
            }
        }
    }
}
