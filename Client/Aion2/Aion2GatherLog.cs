using System.IO;
using System.Text.Json;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Aion2;

/// <summary>What one resource gave: how many of each grade (normal, Fine or Fresh, Pure, Radiant), how often the player set about gathering it, how often that
/// failed (nothing came), when last.</summary>
public sealed class GatherTally
{
    /// <summary>The resource's gathering key, the English one of the map ("Orichalcum", "Aria").</summary>
    public string Key { get; set; } = "";

    /// <summary>How many came of each grade: [0] normal, [1] Fine (Fresh for herbs), [2] Pure, [3] Radiant.</summary>
    public long[] ByTier { get; set; } = new long[4];

    /// <summary>The gatherings that gave something.</summary>
    public int Gathers { get; set; }

    /// <summary>The gatherings the player set about (the game's "starts gathering" for the own character).</summary>
    public int Tries { get; set; }

    /// <summary>The gatherings that gave nothing (failed, or broken off).</summary>
    public int Fails { get; set; }

    public DateTime Last { get; set; }

    public long Total => ByTier.Sum();
}

/// <summary>What the game rolled for a gathering the moment it started (0x318d), seconds before the result: the grade it gives (0 normal, 1 Fine or Fresh, 2 Pure,
/// 3 Radiant) or that it gives nothing.</summary>
public sealed record GatherForecast(string Key, int Tier, bool Fails, DateTime At);

/// <summary>
/// What the player gathered, by resource and grade: the gathering tool's "Picked up" card and overlay. A gathering starts when the game says the own character
/// starts on a gathering place (0x3836, <see cref="NoteAttempt"/>); what of that resource comes into the inventory in the next seconds is its result (the
/// inventory's own change, per item over the whole frame, so a stack moved or merged is nothing, see <see cref="Aion2Stock.NoteChange"/>); a gathering that brings
/// nothing failed (or was broken off). Should the start never be seen, an item still counts when it comes while a gathering place of it is near (within 15 m).
/// The grades are the game's own items of a resource: "Orichalcum Ore", "Fine Orichalcum Ore", "Pure ...", "Radiant ..." (herbs: "Aria" and "Fresh Aria").
/// All time in a file beside the settings, this run of the meter apart; nothing is sent anywhere.
/// </summary>
public static class Aion2GatherLog
{
    /// <summary>How near a gathering place has to be (world units: 15 m).</summary>
    public const float Reach = 1500;

    /// <summary>How long a place gathered (gone from the world) still counts as near.</summary>
    public static readonly TimeSpan TakenWithin = TimeSpan.FromSeconds(20);

    private static readonly object Gate = new();
    private static readonly List<GatherTally> AllTime = new();
    private static readonly List<GatherTally> Session = new();
    /// <summary>How long after its start a gathering's items may come.</summary>
    public static readonly TimeSpan ResultWithin = TimeSpan.FromSeconds(15);

    private sealed class Attempt
    {
        public required string Key { get; init; }
        public int Env { get; init; }
        public DateTime At { get; init; }
        public bool Got { get; set; }
        // what the game rolled for it (0x318d), null until read
        public GatherForecast? Roll { get; set; }
        // the result frame came (the gathering is over, whatever it gave)
        public bool Ended { get; set; }
        public int GotTier { get; set; } = -1;
    }

    // the last roll read, kept a moment for a start told after it
    private static (int Env, int Outcome, int Tier, DateTime At)? _lastRoll;
    private static int _rolls, _rollsRight;

    private static Attempt? _pending;
    // whether the own gathering starts are seen at all (then only what comes of them counts; before that, a place of the resource near the player)
    private static bool _startsSeen;
    private static Dictionary<int, (string Key, int Tier)>? _families;
    private static Dictionary<string, int>? _baseItems;
    private static bool _persist;
    private static bool _dirty;
    private static DateTime _savedAt = DateTime.MinValue;

    /// <summary>Raised (on the capturing thread) when something was counted or the tally was cleared.</summary>
    public static event Action? Changed;

    // "-2": the first version counted a stack moved or merged in the bag as gathered; its file is left behind
    public static string StorePath => Path.Combine(Path.GetDirectoryName(Aion2CharacterStore.DefaultPath)!, "aion2-gathered-2.json");

    /// <summary>The resource and grade an item stands for (null for anything that is no gathered resource): the grade is 0 normal, 1 Fine or Fresh, 2 Pure, 3 Radiant.</summary>
    public static (string Key, int Tier)? FamilyOf(int itemId)
    {
        Families();
        return _families!.TryGetValue(itemId, out var family) ? family : null;
    }

    /// <summary>Which grades a resource has at all ([0] normal ... [3] Radiant): herbs have Fresh but no Pure or Radiant, cooking plants only the normal one.</summary>
    public static bool[] TiersOf(string key)
    {
        Families();
        var tiers = new bool[4];
        foreach (var family in _families!.Values)
        {
            if (family.Key == key)
            {
                tiers[family.Tier] = true;
            }
        }

        return tiers;
    }

    /// <summary>The item of a resource at a grade (its picture shows the grade), 0 when the resource has none at that grade.</summary>
    public static int ItemOf(string key, int tier)
    {
        Families();
        return _families!.Where(f => f.Value.Key == key && f.Value.Tier == tier).Select(f => f.Key).DefaultIfEmpty(0).Min();
    }

    /// <summary>Whether a resource's second grade is called Fresh (herbs), not Fine.</summary>
    public static bool IsFresh(string key)
    {
        Families();
        return _families!.Any(f => f.Value.Key == key && f.Value.Tier == 1 && Aion2ItemNames.NameOf(f.Key, "en")?.StartsWith("Fresh ", StringComparison.Ordinal) == true);
    }

    /// <summary>The normal grade's item of a resource (its name names the resource), 0 when unknown.</summary>
    public static int BaseItemOf(string key)
    {
        Families();
        return _baseItems!.GetValueOrDefault(key);
    }

    /// <summary>The tally of all time, or of this run of the meter (<paramref name="session"/>), the most gathered first (copies).</summary>
    public static IReadOnlyList<GatherTally> Tallies(bool session)
    {
        bool settled;
        lock (Gate)
        {
            settled = Settle(DateTime.Now, null);
        }

        if (settled)
        {
            Changed?.Invoke();
        }

        lock (Gate)
        {
            return (session ? Session : AllTime).OrderByDescending(t => t.Total).ThenByDescending(t => t.Tries).ThenBy(t => t.Key, StringComparer.Ordinal)
                .Select(t => new GatherTally { Key = t.Key, ByTier = t.ByTier.ToArray(), Gathers = t.Gathers, Tries = t.Tries, Fails = t.Fails, Last = t.Last }).ToList();
        }
    }

    // Under the lock. The gathering under way is over when its time is up or another one starts: nothing came, it failed. True when one was counted.
    private static bool Settle(DateTime now, Attempt? next)
    {
        if (_pending is not { } open || (next is null && now - open.At <= ResultWithin))
        {
            return false;
        }

        _pending = null;
        Score(open);
        if (open.Got)
        {
            return false;
        }

        foreach (GatherTally tally in new[] { Tally(AllTime, open.Key), Tally(Session, open.Key) })
        {
            tally.Fails++;
        }

        Note($"{open.At:HH:mm:ss} {open.Key}: nothing came - failed or broken off");
        Version++;
        _dirty = true;
        Save(force: false);
        return true;
    }

    // Under the lock.
    private static GatherTally Tally(List<GatherTally> list, string key)
    {
        GatherTally? tally = list.FirstOrDefault(t => t.Key == key);
        if (tally is null)
        {
            tally = new GatherTally { Key = key };
            list.Add(tally);
        }

        return tally;
    }

    /// <summary>The own character starts gathering a place of the resource <paramref name="key"/> (env object <paramref name="env"/>): a try. The one before is
    /// settled (a fail when nothing came of it).</summary>
    public static void NoteAttempt(string key, int env, DateTime at)
    {
        DateTime now = at.Kind == DateTimeKind.Utc ? at.ToLocalTime() : at;
        lock (Gate)
        {
            if (_pending is { } same && same.Env == env && now - same.At < TimeSpan.FromSeconds(1.5))
            {
                return; // the same start told twice
            }

            _startsSeen = true;
            var next = new Attempt { Key = key, Env = env, At = now };
            Settle(now, next);
            _pending = next;
            if (_lastRoll is { } roll && roll.Env == env && Math.Abs((now - roll.At).TotalSeconds) < 3)
            {
                Attach(next, roll.Outcome, roll.Tier, roll.At);
            }

            foreach (GatherTally tally in new[] { Tally(AllTime, key), Tally(Session, key) })
            {
                tally.Tries++;
                tally.Last = now;
            }

            Note($"{now:HH:mm:ss} {key}: gathering starts (place {env})");
            Version++;
            _dirty = true;
            Save(force: false);
        }

        Changed?.Invoke();
    }

    /// <summary>A frame that follows the own character's gathering (0x3a36: the place, the actor, then what the game says of the result), kept in the
    /// diagnostic log as it came, to learn its layout (success, failure, broken off).</summary>
    public static void NoteResultFrame(int env, ReadOnlySpan<byte> frame)
    {
        lock (Gate)
        {
            if (_pending is { } open && open.Env == env)
            {
                open.Ended = true;
                Version++;
                Note($"{DateTime.Now:HH:mm:ss} {open.Key}: result frame {Convert.ToHexString(frame)}");
            }
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// What the game rolled for the own gathering of the place <paramref name="env"/> as it started (0x318d): <paramref name="outcome"/> 0 = it gives nothing,
    /// <paramref name="tier"/> the grade it gives (0 normal ... 3 Radiant). Shown at once on the gathering card and overlay, seconds before the result.
    /// </summary>
    public static void NoteRoll(int env, int attempt, int outcome, int tier, DateTime at)
    {
        DateTime now = at.Kind == DateTimeKind.Utc ? at.ToLocalTime() : at;
        lock (Gate)
        {
            _lastRoll = (env, outcome, tier, now);
            if (_pending is { } open && open.Env == env && open.Roll is null && Math.Abs((now - open.At).TotalSeconds) < 3)
            {
                Attach(open, outcome, tier, now);
            }
        }

        Changed?.Invoke();
    }

    // Under the lock.
    private static void Attach(Attempt attempt, int outcome, int tier, DateTime at)
    {
        attempt.Roll = new GatherForecast(attempt.Key, Math.Clamp(tier, 0, 3), outcome == 0, at);
        _lastRoll = null;
        Version++;
        Note($"{at:HH:mm:ss} {attempt.Key}: rolled {(attempt.Roll.Fails ? "nothing (fails)" : "grade " + attempt.Roll.Tier)} (outcome {outcome})");
    }

    // Under the lock: an attempt with a roll is over; was the roll right (the grade it said came, or nothing came when it said so)?
    private static void Score(Attempt attempt)
    {
        if (attempt.Roll is not { } roll)
        {
            return;
        }

        _rolls++;
        bool right = roll.Fails ? !attempt.Got : attempt.Got && attempt.GotTier == roll.Tier;
        if (right)
        {
            _rollsRight++;
        }

        Note($"{attempt.At:HH:mm:ss} {attempt.Key}: the roll was {(right ? "right" : "wrong")} (got {(attempt.Got ? "grade " + attempt.GotTier : "nothing")})");
    }

    /// <summary>The roll of the gathering under way, while its result has not come (null when none is under way or the game sent no roll).</summary>
    public static GatherForecast? Forecast(DateTime? now = null)
    {
        lock (Gate)
        {
            return _pending is { Roll: { } roll, Got: false, Ended: false } open && (now ?? DateTime.Now) - open.At <= ResultWithin ? roll : null;
        }
    }

    /// <summary>The rolls read this run of the meter and how many of them the result bore out.</summary>
    public static (int Rolls, int Right) RollRecord()
    {
        lock (Gate)
        {
            return (_rolls, _rollsRight);
        }
    }

    /// <summary>A grade's name for a resource: "Normal", "Fine" (herbs: "Fresh"), "Pure", "Radiant".</summary>
    public static string GradeName(string key, int tier, Func<string, string> text) => tier switch
    {
        0 => text("Gather.Tally.Normal"),
        1 => text(IsFresh(key) ? "Gather.Tally.Fresh" : "Gather.Tally.Fine"),
        2 => text("Gather.Tally.Pure"),
        _ => text("Gather.Tally.Radiant"),
    };

    // a small log beside the settings (gather-log.txt): each gathering's start, what came, the frames that followed and a failure, to check the counting
    private static void Note(string text)
    {
        if (!_persist)
        {
            return; // the self-test and the pictures: no file
        }

        try
        {
            string path = Path.Combine(Path.GetDirectoryName(StorePath)!, "gather-log.txt");
            if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024)
            {
                File.Delete(path);
            }

            File.AppendAllText(path, text + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // no log then
        }
    }

    /// <summary>A version that changes with every count, for a page that redraws only on news.</summary>
    public static int Version { get; private set; }

    /// <summary>Whether any of the gains is a gathered resource at all (the caller asks the places around the player only then).</summary>
    public static bool AnyResource(IReadOnlyList<(int Item, long Amount)> gains) => gains.Any(g => FamilyOf(g.Item) is not null);

    /// <summary>
    /// What came into the inventory with one frame; <paramref name="keysNear"/> are the gathering keys of the places near the player now (or gathered there a
    /// moment ago). Each resource near is one gathering of it, with all its grades that came.
    /// </summary>
    public static void NoteGains(IReadOnlyList<(int Item, long Amount)> gains, DateTime at, IReadOnlyCollection<string> keysNear)
    {
        if (gains.Count == 0)
        {
            return;
        }

        DateTime now = at.Kind == DateTimeKind.Utc ? at.ToLocalTime() : at;
        bool counted = false;
        lock (Gate)
        {
            var gathered = new HashSet<string>();
            foreach ((int item, long amount) in gains)
            {
                if (amount <= 0 || FamilyOf(item) is not { } family)
                {
                    continue;
                }

                // the result of the gathering under way, of that resource; only while no start was ever seen (a game version that tells it otherwise),
                // a place of it near the player
                bool ofAttempt = _pending is { } open && open.Key == family.Key && now - open.At >= TimeSpan.FromSeconds(-1) && now - open.At <= ResultWithin;
                if (!ofAttempt && (_startsSeen || !keysNear.Contains(family.Key)))
                {
                    continue;
                }

                foreach (List<GatherTally> list in new[] { AllTime, Session })
                {
                    GatherTally tally = Tally(list, family.Key);
                    tally.ByTier[family.Tier] += amount;
                    tally.Last = now;
                    if (!gathered.Contains(family.Key) && !(ofAttempt && _pending!.Got))
                    {
                        tally.Gathers++;
                        if (!ofAttempt)
                        {
                            tally.Tries++; // no start seen: this one counts as tried too
                        }
                    }
                }

                if (ofAttempt)
                {
                    _pending!.Got = true;
                    _pending.GotTier = Math.Max(_pending.GotTier, family.Tier);
                }

                Note($"{now:HH:mm:ss} {family.Key}: +{amount} of grade {family.Tier} (item {item}){(ofAttempt ? "" : ", no start seen")}");
                gathered.Add(family.Key);
                counted = true;
            }

            if (counted)
            {
                Version++;
                _dirty = true;
                Save(force: false);
            }
        }

        if (counted)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Clears the tally of all time (<paramref name="session"/> false: both) or of this run only.</summary>
    public static void Clear(bool session)
    {
        lock (Gate)
        {
            Session.Clear();
            _pending = null;
            _lastRoll = null;
            _rolls = _rollsRight = 0;
            _startsSeen = false;
            if (!session)
            {
                AllTime.Clear();
            }

            Version++;
            _dirty = true;
            Save(force: true);
        }

        Changed?.Invoke();
    }

    /// <summary>Starts keeping the tally of all time in <see cref="StorePath"/> (the live meter only) and loads what is there.</summary>
    public static void EnablePersistence()
    {
        lock (Gate)
        {
            _persist = true;
            try
            {
                if (File.Exists(StorePath) && JsonSerializer.Deserialize<List<GatherTally>>(File.ReadAllText(StorePath)) is { } loaded)
                {
                    AllTime.Clear();
                    AllTime.AddRange(loaded.Where(t => t.ByTier is { Length: 4 } && t.Key.Length > 0));
                    Version++;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                if (ex is JsonException)
                {
                    AionDPS.AtomicFile.KeepDamaged(StorePath);
                }

                // a broken file: the tally starts anew
            }
        }
    }

    /// <summary>Writes what is not saved yet (the meter is closing).</summary>
    public static void Flush()
    {
        lock (Gate)
        {
            Save(force: true);
        }
    }

    // Under the lock; at most every 5 s unless forced.
    private static void Save(bool force)
    {
        if (!_persist || !_dirty || !force && DateTime.UtcNow - _savedAt < TimeSpan.FromSeconds(5))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            AionDPS.AtomicFile.WriteAllText(StorePath, JsonSerializer.Serialize(AllTime));
            _dirty = false;
            _savedAt = DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // kept in memory; the next save tries again
        }
    }

    /// <summary>
    /// The resources and their grades, from the gathering places' items (maps/gather_items.json: key -> the normal grade's item) and the client's item names: an
    /// item named like the normal one with "Fine " or "Fresh " (1), "Pure " (2) or "Radiant " (3) in front is the same resource at that grade.
    /// </summary>
    private static void Families()
    {
        if (_families is not null)
        {
            return;
        }

        var families = new Dictionary<int, (string Key, int Tier)>();
        var bases = new Dictionary<string, int>(StringComparer.Ordinal);
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "maps", "gather_items.json");
            var keys = File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path)) ?? new() : new();
            var byName = new Dictionary<string, (string Key, int Base)>(StringComparer.Ordinal);
            foreach ((string key, int item) in keys)
            {
                if (Aion2ItemNames.NameOf(item, "en") is { Length: > 0 } name)
                {
                    families[item] = (key, 0);
                    bases[key] = item;
                    byName[name] = (key, item);
                }
            }

            foreach (int item in Aion2ItemNames.AllIds())
            {
                if (families.ContainsKey(item) || Aion2ItemNames.NameOf(item, "en") is not { Length: > 0 } name)
                {
                    continue;
                }

                foreach ((string prefix, int tier) in new[] { ("Fine ", 1), ("Fresh ", 1), ("Pure ", 2), ("Radiant ", 3) })
                {
                    if (name.StartsWith(prefix, StringComparison.Ordinal) && byName.TryGetValue(name[prefix.Length..], out var family))
                    {
                        families[item] = (family.Key, tier);
                        break;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // no table: nothing counts as gathered
        }

        _baseItems = bases;
        _families = families;
    }
}
