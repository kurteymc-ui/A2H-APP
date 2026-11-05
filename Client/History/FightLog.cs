using System.IO;
using System.IO.Compression;
using System.Text.Json;
using AionDPS.Combat;
using AionDPS.Combat.Sources;

namespace AionDPS.History;

/// <summary>What a history entry is: a dungeon run (one instance stay), one boss fight (an attempt, killed or not), a training dummy parse or a PvP stay (an arena).</summary>
public enum FightLogKind
{
    Run,
    Boss,
    Dummy,
    Pvp,
}

/// <summary>
/// One entry of the history, for the list: what, when, how long, killed or not, how many boss fights, your DPS and the group's, and the file with the
/// fight itself (every hit, heal, shield, avoided attack and the capture's extras), from which the fight report is made again.
/// </summary>
public sealed record FightLogEntry(
    string Id, FightLogKind Kind, string Title, string? Detail, DateTime Start, double Seconds, bool? Killed, int BossFights,
    double YourDps, double PartyDps, int? BossNpcId, int ZoneId, string File, string? You = null, string? YourClass = null, string? Url = null, int? ServerId = null);

/// <summary>
/// The history on this PC (%APPDATA%\A2HMeter\History): every dungeon run, boss fight, dummy parse and arena stay is saved as a file of its own
/// the moment it ends, and listed in an index. Nothing is ever one endless session: an hour in the open world with three world bosses is three
/// boss fights. Opening an entry reads its file back into a <see cref="ReportInput"/> for the report window. Pure file work: no window, no capture.
/// </summary>
public static class FightLog
{
    /// <summary>The newest entries kept; older ones and their files go.</summary>
    public const int MaxEntries = 3000;

    private static readonly object Gate = new();
    private static List<FightLogEntry>? _index;
    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    /// <summary>Where the history lives; a test points it elsewhere.</summary>
    public static string Folder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Brand.DataFolder, "History");

    private static string IndexPath => Path.Combine(Folder, "index.json");

    /// <summary>Raised (on the saving thread) when an entry was added or removed.</summary>
    public static event Action? Changed;

    private sealed record PlayerDto(int Id, string Name, string Class, int? Cp, bool Self);

    private sealed record EventDto(long T, int S, int G, long A, bool H, string? Sk, bool C,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] int I = 0,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? F = null,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] int N = 0,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] int X = 0,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] long? B = null,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] long? U = null);

    private sealed record AvoidDto(long T, int S, int G, int K, string? Sk);

    private sealed record Snapshot(int V, string Title, long Start, long End, int? BossId, int? BossNpcId, bool Killed, List<PlayerDto> Players,
        List<EventDto> Events, List<EventDto> Shields, List<AvoidDto> Avoids, string? Extras, List<int>? Targets);

    private static EventDto ToDto(DamageEvent e) => new(e.Timestamp.Ticks, e.SourceObjectId, e.TargetObjectId, e.Amount, e.IsHeal, e.Skill, e.IsCritical, e.SkillId,
        e.Kinds is { } k ? (int)k : null, e.HitCount > 1 ? e.HitCount : 0, (e.IsTick ? 1 : 0) | (e.ViaSummon ? 2 : 0) | (e.Estimated ? 4 : 0),
        e.IsHeal && e.HpBefore >= 0 ? e.HpBefore : null, e.IsHeal && e.HpMax > 0 ? e.HpMax : null);

    private static DamageEvent FromDto(EventDto d) => new(new DateTime(d.T, DateTimeKind.Local), d.S, d.G, d.A, d.H, d.Sk, d.C, IsTick: (d.X & 1) != 0, SkillId: d.I,
        Kinds: d.F is int f ? (HitKinds)f : null, HitCount: d.N > 1 ? d.N : 1, Estimated: (d.X & 4) != 0, ViaSummon: (d.X & 2) != 0, HpBefore: d.B ?? -1, HpMax: d.U ?? 0);

    /// <summary>The entries, newest first.</summary>
    public static IReadOnlyList<FightLogEntry> Entries()
    {
        lock (Gate)
        {
            return LoadIndex().OrderByDescending(e => e.Start).ToList();
        }
    }

    /// <summary>
    /// Saves a fight as an entry of its own: its file first, then the index. <paramref name="detail"/> is the second word of the title ("Normal",
    /// "2:00 parse"); <paramref name="bossFights"/> the boss fights inside a run. Returns the entry, or null when there is nothing to keep (no damage).
    /// </summary>
    public static FightLogEntry? Save(FightLogKind kind, ReportInput input, string? detail = null, int zoneId = 0, int bossFights = 0, int? serverId = null)
    {
        var playerIds = input.Players.Select(p => p.Id).ToHashSet();
        double seconds = Math.Max(1, (input.End - input.Start).TotalSeconds);
        long party = 0, yours = 0;
        ReportPlayer? you = input.Players.FirstOrDefault(p => p.IsSelf);
        foreach (DamageEvent e in input.Events)
        {
            if (e.IsHeal || e.Timestamp < input.Start || e.Timestamp > input.End || !playerIds.Contains(e.SourceObjectId) || playerIds.Contains(e.TargetObjectId)
                || (input.Targets is not null && !input.Targets.Contains(e.TargetObjectId)))
            {
                continue;
            }

            party += e.Amount;
            if (you is not null && e.SourceObjectId == you.Id)
            {
                yours += e.Amount;
            }
        }

        if (party <= 0)
        {
            return null;
        }

        string id = $"{input.Start:yyyyMMdd-HHmmss}-{kind.ToString().ToLowerInvariant()}-{Guid.NewGuid().ToString("N")[..6]}";
        string relative = Path.Combine(input.Start.ToString("yyyy-MM"), id + ".json.gz");
        var snapshot = new Snapshot(1, input.Title, input.Start.Ticks, input.End.Ticks, input.BossId, input.BossNpcId, input.Killed,
            input.Players.Select(p => new PlayerDto(p.Id, p.Name, p.ClassName, p.CombatPower, p.IsSelf)).ToList(),
            input.Events.Select(ToDto).ToList(), input.Shields.Select(ToDto).ToList(),
            input.Avoids.Select(a => new AvoidDto(a.Timestamp.Ticks, a.SourceObjectId, a.TargetObjectId, (int)a.Kind, a.Skill)).ToList(),
            Convert.ToBase64String(ExtrasBlob.Pack(input.Extras with { Tracks = null, Hp = null })), input.Targets?.ToList());
        var entry = new FightLogEntry(id, kind, input.Title, detail, input.Start, seconds, kind == FightLogKind.Boss ? input.Killed : null, bossFights,
            you is not null ? yours / seconds : 0, party / seconds, input.BossNpcId, zoneId, relative, you?.Name, you?.ClassName, ServerId: serverId > 0 ? serverId : null);

        lock (Gate)
        {
            // uploaded before the stay ended (the usual case): its page waits here for the entry
            if (kind == FightLogKind.Boss && input.BossNpcId is int npc && PendingUrls.FindIndex(p => p.Npc == npc && Near(p.Start, input.Start)) is int at and >= 0)
            {
                entry = entry with { Url = PendingUrls[at].Url };
                PendingUrls.RemoveAt(at);
            }

            string path = Path.Combine(Folder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (FileStream file = File.Create(path))
            using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
            {
                JsonSerializer.Serialize(gzip, snapshot, Json);
            }

            List<FightLogEntry> index = LoadIndex();
            index.Add(entry);
            foreach (FightLogEntry old in index.OrderByDescending(e => e.Start).Skip(MaxEntries).ToList())
            {
                index.Remove(old);
                TryDelete(Path.Combine(Folder, old.File));
            }

            SaveIndex(index);
        }

        Changed?.Invoke();
        return entry;
    }

    /// <summary>The fight of an entry, as the report window takes it; null when its file is gone or unreadable.</summary>
    public static ReportInput? Open(FightLogEntry entry)
    {
        try
        {
            using FileStream file = File.OpenRead(Path.Combine(Folder, entry.File));
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            Snapshot? s = JsonSerializer.Deserialize<Snapshot>(gzip, Json);
            if (s is null)
            {
                return null;
            }

            var names = s.Players.ToDictionary(p => p.Id, p => p.Name);
            FightExtras extras = s.Extras is { Length: > 0 } blob ? ExtrasBlob.Unpack(Convert.FromBase64String(blob)) : FightExtras.Empty;
            foreach (var (eid, name) in extras.Names)
            {
                names.TryAdd(eid, name);
            }

            return new ReportInput(s.Title, new DateTime(s.Start, DateTimeKind.Local), new DateTime(s.End, DateTimeKind.Local),
                s.Players.Select(p => new ReportPlayer(p.Id, p.Name, p.Class, p.Cp, p.Self)).ToList(),
                s.Events.Select(FromDto).ToList(), s.Shields.Select(FromDto).ToList(),
                s.Avoids.Select(a => new AvoidEvent(new DateTime(a.T, DateTimeKind.Local), a.S, a.G, (AvoidKind)a.K, a.Sk)).ToList(),
                extras, s.BossId, s.BossNpcId, s.Killed, id => names.GetValueOrDefault(id), s.Targets?.ToHashSet());
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or FormatException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static readonly List<(int Npc, DateTime Start, string Url)> PendingUrls = new();

    private static bool Near(DateTime a, DateTime b) => Math.Abs((a - b).TotalSeconds) <= 30;

    /// <summary>
    /// The website's page of an uploaded boss fight (atreia.app/meter/...), kept with its History entry so the report can open it or copy the link.
    /// The entry is found by the boss and its start; one not saved yet (the stay is still going on) gets it when it is.
    /// </summary>
    public static void RememberUrl(int? bossNpcId, DateTime started, string url)
    {
        if (bossNpcId is not int npc || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        DateTime start = started.Kind == DateTimeKind.Utc ? started.ToLocalTime() : started;
        bool changed = false;
        lock (Gate)
        {
            List<FightLogEntry> index = LoadIndex();
            for (int i = 0; i < index.Count; i++)
            {
                if (index[i].Kind == FightLogKind.Boss && index[i].BossNpcId == npc && Near(index[i].Start, start))
                {
                    index[i] = index[i] with { Url = url };
                    changed = true;
                }
            }

            if (changed)
            {
                SaveIndex(index);
            }
            else
            {
                PendingUrls.Add((npc, start, url));
                if (PendingUrls.Count > 50)
                {
                    PendingUrls.RemoveAt(0);
                }
            }
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Removes an entry and its file.</summary>
    public static void Delete(FightLogEntry entry)
    {
        lock (Gate)
        {
            List<FightLogEntry> index = LoadIndex();
            index.RemoveAll(e => e.Id == entry.Id);
            SaveIndex(index);
            TryDelete(Path.Combine(Folder, entry.File));
        }

        Changed?.Invoke();
    }

    /// <summary>Forgets the index read before (a test that changed <see cref="Folder"/>).</summary>
    public static void Reload()
    {
        lock (Gate)
        {
            _index = null;
        }
    }

    // under Gate
    private static List<FightLogEntry> LoadIndex()
    {
        if (_index is not null)
        {
            return _index;
        }

        try
        {
            _index = File.Exists(IndexPath) ? JsonSerializer.Deserialize<List<FightLogEntry>>(File.ReadAllText(IndexPath), Json) ?? new() : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _index = new();
        }

        return _index;
    }

    // under Gate: written to a side file first, so a crash mid-write never leaves half an index
    private static void SaveIndex(List<FightLogEntry> index)
    {
        Directory.CreateDirectory(Folder);
        string temp = IndexPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(index, Json));
        File.Move(temp, IndexPath, overwrite: true);
        _index = index;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // gone already or in use: the next prune tries again
        }
    }
}
