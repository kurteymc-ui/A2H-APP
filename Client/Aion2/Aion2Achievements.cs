using System.Buffers.Binary;
using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2;

/// <summary>One achievement counter as the game sends it: the count, how many of its stages are done (the game's own byte) and when it last changed (ms since 1970).</summary>
public readonly record struct AchievementValue(int Count, int Stage, long TimeMs);

/// <summary>
/// Every achievement counter the game sends, per character: the season missions are among them.
/// <list type="bullet">
/// <item><c>0x8356</c> (raw bytes 83 56) at a login: <c>n (varint) | n x ( id u32 | count u32 | time u64 ms | stage u8 )</c>, all of the character's counters.</item>
/// <item><c>0x8456</c> (raw bytes 84 56) while playing: <c>n u8 | n x ( the same 17 bytes )</c>, the counters that changed.</item>
/// </list>
/// The ids of a counter differ by faction: Asmodian ones are the Elyos ones with the faction digit raised (3xxxxxxxx -> 4xxxxxxxx, 23x -> 24x, 81x -> 82x,
/// 91x -> 92x; seen in the owner's Asmodian recordings against the Elyos notes of October). The game sends no names or stage targets: those come later
/// (the season page read from a screenshot). Until then the counters are kept here, with what the packets show about each one: the counts at which its
/// stage went up, and the highest count seen (a finished mission stops at its last target).
/// Kept in aion2-achievements.json beside the settings. A login's list comes before the game names the character: it waits until a name is known.
/// </summary>
public static class Aion2Achievements
{
    // The opcode as the frame decoder reads it: the first two bytes, big-endian (opcodes.json "opcodeBigEndian"). Read little-endian before
    // 3 November 2026: the login list (83 56 = 0x5683) was refused and the live frame (dispatched as 0x8456) never reached the parser.
    public const int ListOpcode = 0x8356, LiveOpcode = 0x8456;
    private const int MaxEntries = 6000;

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Dictionary<int, AchievementValue>> Characters = new(StringComparer.Ordinal);
    private static readonly Dictionary<int, SortedDictionary<int, int>> StageAt = new();   // id -> stage -> the lowest count it was seen reached at
    private static Dictionary<int, AchievementValue>? _pending;
    private static bool _persist, _dirty;
    private static DateTime _savedAt = DateTime.MinValue;

    /// <summary>Who is logged in now (the tickets know it); a test puts its own here.</summary>
    public static Func<string?> CurrentName { get; set; } = () => Aion2Tickets.CurrentName;

    /// <summary>Raised when something changed (on the capturing thread).</summary>
    public static event Action? Changed;

    public static string StorePath => Path.Combine(Path.GetDirectoryName(Aion2CharacterStore.DefaultPath)!, "aion2-achievements.json");

    /// <summary>A list (<see cref="ListOpcode"/>) or live (<see cref="LiveOpcode"/>) frame read: its entries, or false when it isn't one.</summary>
    public static bool TryParse(ReadOnlySpan<byte> frame, out List<(int Id, AchievementValue Value)> entries)
    {
        entries = new List<(int, AchievementValue)>();
        if (frame.Length < 3)
        {
            return false;
        }

        int opcode = (frame[0] << 8) | frame[1], p = 2;
        long n;
        if (opcode == LiveOpcode)
        {
            n = frame[2];
            p = 3;
            if (frame.Length != 3 + (17 * n))
            {
                return false;
            }
        }
        else if (opcode == ListOpcode)
        {
            int shift = 0;
            n = 0;
            while (true)
            {
                if (p >= frame.Length || shift > 28)
                {
                    return false;
                }

                byte b = frame[p++];
                n |= (long)(b & 0x7f) << shift;
                if ((b & 0x80) == 0)
                {
                    break;
                }

                shift += 7;
            }

            if (n > MaxEntries || p + (17 * n) > frame.Length)
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        for (long i = 0; i < n; i++, p += 17)
        {
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(frame[p..]);
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(frame[(p + 4)..]);
            ulong time = BinaryPrimitives.ReadUInt64LittleEndian(frame[(p + 8)..]);
            if (id is < 1_000_000 or > 999_999_999 || count > int.MaxValue || (time != 0 && time is < 1_500_000_000_000 or > 4_000_000_000_000))
            {
                return false;
            }

            entries.Add(((int)id, new AchievementValue((int)count, frame[p + 16], (long)time)));
        }

        return true;
    }

    /// <summary>
    /// What a frame told: a login's whole list (<paramref name="whole"/>) or changes. A whole list waits until it surely belongs to the character named:
    /// the name changed since it came (a login), or 10 seconds went by under the same name (the list again in the middle of a session); changes that come
    /// meanwhile go with it.
    /// </summary>
    public static void Note(IReadOnlyList<(int Id, AchievementValue Value)> entries, bool whole)
    {
        lock (Gate)
        {
            foreach (var (id, v) in entries)
            {
                Learn(id, v);
            }

            string? name = CurrentName();
            if (whole)
            {
                _pending = new Dictionary<int, AchievementValue>();
                _pendingWas = name;
                _pendingAt = Clock();
            }

            if (_pending is not null || name is null)
            {
                _pending ??= new Dictionary<int, AchievementValue>();
                foreach (var (id, v) in entries)
                {
                    _pending[id] = v;
                }

                _dirty = true;
                TryAssign(Clock());
            }
            else
            {
                Dictionary<int, AchievementValue> mine = Mine(name);
                foreach (var (id, v) in entries)
                {
                    mine[id] = v;
                }

                _dirty = true;
            }
        }

        Changed?.Invoke();
    }

    /// <summary>A waiting list goes to the character now named, once that is sure (called when the tickets change, e.g. learn the name).</summary>
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

    private static string? _pendingWas;
    private static DateTime _pendingAt;

    /// <summary>The wall clock the waiting is measured on (a test moves it on).</summary>
    public static Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    private static bool TryAssign(DateTime now)
    {
        if (_pending is null || CurrentName() is not { } name || (name == _pendingWas && now - _pendingAt < TimeSpan.FromSeconds(10)))
        {
            return false;
        }

        Dictionary<int, AchievementValue> mine = Mine(name);
        foreach (var (id, v) in _pending)
        {
            mine[id] = v;
        }

        _pending = null;
        _dirty = true;
        return true;
    }

    private static Dictionary<int, AchievementValue> Mine(string name)
    {
        if (!Characters.TryGetValue(name, out Dictionary<int, AchievementValue>? mine))
        {
            Characters[name] = mine = new Dictionary<int, AchievementValue>();
        }

        return mine;
    }

    // what the packets show about a counter's stages: the lowest count each stage was seen reached at (any character)
    private static void Learn(int id, AchievementValue v)
    {
        if (v.Stage <= 0)
        {
            return;
        }

        if (!StageAt.TryGetValue(id, out SortedDictionary<int, int>? stages))
        {
            StageAt[id] = stages = new SortedDictionary<int, int>();
        }

        if (!stages.TryGetValue(v.Stage, out int at) || v.Count < at)
        {
            stages[v.Stage] = v.Count;
        }
    }

    /// <summary>A character's counters (empty when none were seen).</summary>
    public static IReadOnlyDictionary<int, AchievementValue> Of(string name)
    {
        lock (Gate)
        {
            return Characters.TryGetValue(name, out Dictionary<int, AchievementValue>? mine) ? new Dictionary<int, AchievementValue>(mine) : new Dictionary<int, AchievementValue>();
        }
    }

    /// <summary>The lowest count each stage of a counter was seen reached at: an upper bound of the stage's target.</summary>
    public static IReadOnlyDictionary<int, int> StagesSeen(int id)
    {
        lock (Gate)
        {
            return StageAt.TryGetValue(id, out SortedDictionary<int, int>? s) ? new Dictionary<int, int>(s) : new Dictionary<int, int>();
        }
    }

    /// <summary>Forget everything (a test, a replay).</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            Characters.Clear();
            StageAt.Clear();
            _pending = null;
            _pendingWas = null;
            _dirty = false;
        }
    }

    /// <summary>The meter (not the replay tools) keeps the counters in a file between two starts.</summary>
    public static void EnablePersistence()
    {
        lock (Gate)
        {
            _persist = true;
            Load();
        }

        Aion2Tickets.Changed += AssignPending;
        Changed += () => Flush();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush(force: true);
    }

    private sealed class Saved
    {
        public int V { get; set; } = 1;
        public Dictionary<string, Dictionary<int, long[]>> Characters { get; set; } = new();
        public Dictionary<int, Dictionary<int, int>> Stages { get; set; } = new();
    }

    /// <summary>Writes the file (at most every 30 seconds unless forced).</summary>
    public static void Flush(bool force = false)
    {
        string json;
        lock (Gate)
        {
            if (!_persist || !_dirty || (!force && DateTime.UtcNow - _savedAt < TimeSpan.FromSeconds(30)))
            {
                return;
            }

            var saved = new Saved
            {
                Characters = Characters.ToDictionary(c => c.Key, c => c.Value.ToDictionary(e => e.Key, e => new[] { (long)e.Value.Count, e.Value.Stage, e.Value.TimeMs })),
                Stages = StageAt.ToDictionary(s => s.Key, s => s.Value.ToDictionary(x => x.Key, x => x.Value)),
            };
            json = JsonSerializer.Serialize(saved);
            _dirty = false;
            _savedAt = DateTime.UtcNow;
        }

        try
        {
            AionDPS.AtomicFile.WriteAllText(StorePath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lock (Gate)
            {
                _dirty = true; // written again with the next flush
            }
        }
    }

    private static void Load()
    {
        try
        {
            if (!File.Exists(StorePath) || JsonSerializer.Deserialize<Saved>(File.ReadAllText(StorePath)) is not { } saved)
            {
                return;
            }

            foreach (var (name, entries) in saved.Characters)
            {
                Characters[name] = entries.Where(e => e.Value.Length == 3).ToDictionary(e => e.Key, e => new AchievementValue((int)e.Value[0], (int)e.Value[1], e.Value[2]));
            }

            foreach (var (id, stages) in saved.Stages)
            {
                StageAt[id] = new SortedDictionary<int, int>(stages);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            if (ex is JsonException)
            {
                AionDPS.AtomicFile.KeepDamaged(StorePath);
            }

            // unreadable file: start empty
        }
    }
}
