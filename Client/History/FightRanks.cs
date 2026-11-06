using System.IO;
using System.Text.Json;
using AionDPS.Upload;

namespace AionDPS.History;

/// <summary>
/// Your own rank in your uploaded boss fights, as the fight's page on the website shows it (6 November 2026, "fight percentiles"): better than how many of the
/// players of your class on that boss, in your Combat Power bracket when the website knows it. Kept on this PC by the fight's page (History\ranks.json) for
/// History and the note after a fight. Asked of the website in the background, one fight at a time, and asked again now and then: a rank moves while more
/// players log the boss.
/// </summary>
public static class FightRanks
{
    /// <summary>
    /// One fight's rank: better than <see cref="Percent"/> of the <see cref="Of"/> logs of the class on the boss, <see cref="Place"/> from the top;
    /// <see cref="Min"/> how many logs of a class the website wants before a rank means something; <see cref="Low"/> and <see cref="High"/> the Combat Power
    /// bracket (null: ranked over all); <see cref="At"/> when it was asked (UTC). <see cref="Of"/> 0: the website has no rank for you in that fight.
    /// </summary>
    public sealed record Own(double Percent, int Place, int Of, int Min, int? Low, int? High, DateTime At)
    {
        /// <summary>Whether the rank means something: enough logs of the class on the boss.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool Ranked => Of > 0 && Of >= Min;
    }

    private static readonly object Gate = new();
    private static Dictionary<string, Own>? _ranks;
    private static string? _loadedFrom;
    private static readonly Queue<(string Url, string You)> Todo = new();
    private static readonly HashSet<string> Waiting = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, DateTime> Failed = new(StringComparer.Ordinal);
    private static bool _working;

    /// <summary>The fights kept at most (the newest asked).</summary>
    private const int MaxKept = 3000;

    /// <summary>Raised (on a worker thread) when a rank came.</summary>
    public static event Action? Changed;

    /// <summary>Set by the picture modes and the self-test: nothing is asked of the website.</summary>
    public static bool Offline { get; set; }

    private static string FilePath => Path.Combine(FightLog.Folder, "ranks.json");

    /// <summary>The rank kept for an entry's fight; null when it has none yet (not uploaded, or not asked yet).</summary>
    public static Own? Of(FightLogEntry entry) => entry.Url is { Length: > 0 } url ? Get(url) : null;

    /// <summary>The rank kept for the fight on <paramref name="url"/>.</summary>
    public static Own? Get(string url)
    {
        lock (Gate)
        {
            return Load().GetValueOrDefault(url);
        }
    }

    /// <summary>
    /// Whether a kept rank is to be asked again: none yet, one that doesn't mean anything yet (few logs of the class) every six hours, a real one once a day
    /// for the first two weeks after the fight; after that it stays as it is.
    /// </summary>
    internal static bool Stale(Own? have, DateTime fightStartUtc, DateTime nowUtc)
    {
        if (have is null)
        {
            return true;
        }

        TimeSpan age = nowUtc - have.At;
        if (!have.Ranked)
        {
            return age >= TimeSpan.FromHours(6) && nowUtc - fightStartUtc <= TimeSpan.FromDays(30);
        }

        return age >= TimeSpan.FromDays(1) && have.At - fightStartUtc <= TimeSpan.FromDays(14);
    }

    /// <summary>Asks the website for your rank in an entry's fight in the background when it has none or it is old (a killed, uploaded boss fight only).</summary>
    public static void Want(FightLogEntry entry)
    {
        if (Offline || entry.Kind != FightLogKind.Boss || entry.Killed != true || entry.Url is not { Length: > 0 } url || entry.You is not { Length: > 0 } you)
        {
            return;
        }

        lock (Gate)
        {
            DateTime now = DateTime.UtcNow;
            if (Waiting.Contains(url) || Todo.Count >= 300 || (Failed.TryGetValue(url, out DateTime failedAt) && now - failedAt < TimeSpan.FromMinutes(15))
                || !Stale(Load().GetValueOrDefault(url), entry.Start.ToUniversalTime(), now))
            {
                return;
            }

            Waiting.Add(url);
            Todo.Enqueue((url, you));
            if (_working)
            {
                return;
            }

            _working = true;
        }

        _ = Task.Run(WorkAsync);
    }

    private static async Task WorkAsync()
    {
        while (true)
        {
            (string Url, string You) next;
            lock (Gate)
            {
                if (Todo.Count == 0)
                {
                    _working = false;
                    return;
                }

                next = Todo.Dequeue();
            }

            await FetchAsync(next.Url, next.You).ConfigureAwait(false);
            await Task.Delay(350).ConfigureAwait(false);   // one after the other, gently
        }
    }

    /// <summary>Asks the website for <paramref name="you"/>'s rank in the fight on <paramref name="url"/> now and keeps it; null when the website didn't answer.</summary>
    public static async Task<Own?> FetchAsync(string url, string you)
    {
        if (Offline)
        {
            return Get(url);
        }

        (int Min, IReadOnlyDictionary<string, UploadClient.FightRank> Ranks)? answer = await UploadClient.GetRanksAsync(url).ConfigureAwait(false);
        Own? own = null;
        lock (Gate)
        {
            Waiting.Remove(url);
            if (answer is not { } a)
            {
                Failed[url] = DateTime.UtcNow;
            }
            else
            {
                DateTime now = DateTime.UtcNow;
                own = a.Ranks.TryGetValue(you, out UploadClient.FightRank? r)
                    ? new Own(r.Percent, r.Place, r.Of, a.Min, r.Low, r.High, now)
                    : new Own(0, 0, 0, a.Min, null, null, now);
                Put(url, own);
            }
        }

        if (own is not null)
        {
            Changed?.Invoke();
        }

        return own;
    }

    /// <summary>Keeps a rank as if the website had answered it (the pictures' made-up fights, the self-test).</summary>
    internal static void Seed(string url, Own own)
    {
        lock (Gate)
        {
            Put(url, own);
        }
    }

    /// <summary>Forgets what was read before (a test or picture that changed <see cref="FightLog.Folder"/>).</summary>
    internal static void Reload()
    {
        lock (Gate)
        {
            _ranks = null;
        }
    }

    // under Gate
    private static void Put(string url, Own own)
    {
        Dictionary<string, Own> all = Load();
        all[url] = own;
        if (all.Count > MaxKept)
        {
            foreach (string old in all.OrderBy(p => p.Value.At).Take(all.Count - MaxKept).Select(p => p.Key).ToList())
            {
                all.Remove(old);
            }
        }

        try
        {
            Directory.CreateDirectory(FightLog.Folder);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(all));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // kept for this run only
        }
    }

    // under Gate
    private static Dictionary<string, Own> Load()
    {
        string path = FilePath;
        if (_ranks is not null && _loadedFrom == path)
        {
            return _ranks;
        }

        _loadedFrom = path;
        try
        {
            _ranks = File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, Own>>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            _ranks = null;
        }

        _ranks = _ranks is null ? new Dictionary<string, Own>(StringComparer.Ordinal) : new Dictionary<string, Own>(_ranks, StringComparer.Ordinal);
        return _ranks;
    }
}
