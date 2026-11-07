using System.IO;
using System.Net.Http;
using System.Text.Json;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;
using AionDPS.Combat;

namespace AionDPS.Upload;

/// <summary>One of a boss's casts as everyone's uploads show it (the site's GET /meter/api/boss-timers): the skill, how many of the fights had it, when it
/// first came (seconds after the boss went into combat), how often it came again (null: once a fight) and how long it takes.</summary>
public sealed record BossTimerCast(int Id, string Name, int Fights, double First, double? Every, double Dur);

/// <summary>A cast the callout names: the skill and the seconds until it comes (0 while it is being cast).</summary>
public sealed record BossCallout(string Name, double In, bool Casting, int SkillId = 0);

/// <summary>What the meter overlay's callout line shows: the next casts (soonest first) and the time left to the boss's rage, when the game data knows it.</summary>
public sealed record BossCalloutFrame(string Boss, IReadOnlyList<BossCallout> Next, TimeSpan? Rage, int Fights);

/// <summary>
/// Boss timers: the boss's next attacks, from its fights (6 November 2026, the owner: "collect data from others if they are running other raids too to
/// have the timers of the mechanics ... make sure it's accurate ... make sure other raids have it as well if we have any runs"). Two sources, put together:
/// every player's uploads (the site's GET /meter/api/boss-timers: each fight's attacks in order, with the boss's health at each; asked once a boss is
/// fought, kept six hours, only with the privacy policy accepted) and this PC's own History (every fight of that boss saved here, wipes too, uploaded or
/// not, so the second pull of a new boss has timers from the first). A fight both have (the player's own upload) counts once. The versions of a boss
/// (normal, Conquest ...: <see cref="Aion2BossCatalog.VersionsOf"/>) run the same attacks: a version with fewer than <see cref="BossPattern.MinFights"/>
/// fights of its own learns from all of them. What comes when is <see cref="BossPattern"/>: by the attack before it, by the boss's health or by time,
/// whichever its fights agreed on; nothing is named that they didn't agree on. A site that answers only the older medians (no fights) is counted from
/// those, as before. The rage comes from the game's own table only (<see cref="Aion2BossRage"/>), from when the game says the boss went into combat.
/// </summary>
public static class BossTimers
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly TimeSpan Keep = TimeSpan.FromHours(6), Retry = TimeSpan.FromMinutes(5);
    /// <summary>How far ahead a cast is named, and how long one stays named after its time passed (it may come a little late).</summary>
    public const double Ahead = 60, Late = 3;

    /// <summary>The most fights of this PC's History a boss learns from (the newest).</summary>
    public const int MostLocal = 60;

    /// <summary>What is known of a boss: the site's older medians (null: none), how many fights everything rests on, and its pattern (null: none learnt).</summary>
    public sealed record Known(IReadOnlyList<BossTimerCast>? Casts, int Fights, BossPattern? Pattern, IReadOnlyDictionary<int, string> Names);

    /// <summary>A fight of one version of the boss, from the site or this PC.</summary>
    public readonly record struct VersionFight(int Npc, PatternFight Fight);

    private static readonly Dictionary<int, (DateTime At, Known? Value, bool Asked)> Cache = new();
    private static readonly HashSet<int> Loading = new();
    private static readonly Dictionary<string, PatternFight?> LocalCache = new();
    private static bool _watching;

    /// <summary>What is known of this boss; null until the first answer came (the first call starts it). <paramref name="ask"/>: whether the site may be
    /// asked (the privacy policy accepted); without, only this PC's History counts.</summary>
    public static Known? For(int npcId, bool ask = true)
    {
        lock (Cache)
        {
            if (!_watching)
            {
                _watching = true;
                History.FightLog.Changed += Forget;   // a fight just saved here: learnt again at the next call
            }

            bool have = Cache.TryGetValue(npcId, out var hit);
            bool fresh = have && hit.Asked == ask && DateTime.UtcNow - hit.At < (hit.Value is null ? Retry : Keep);
            if (!fresh && Loading.Add(npcId))
            {
                _ = FetchAsync(npcId, ask, have ? hit.Value : null);
            }

            return have ? hit.Value : null; // the older answer while a new one comes
        }
    }

    /// <summary>Learns every boss again at its next call (a fight was saved: it may be one of them).</summary>
    private static void Forget()
    {
        lock (Cache)
        {
            foreach (int id in Cache.Keys.ToList())
            {
                var (_, value, asked) = Cache[id];
                Cache[id] = (DateTime.MinValue, value, asked);
            }
        }
    }

    private static async Task FetchAsync(int npcId, bool ask, Known? before)
    {
        Known? known = before;
        try
        {
            IReadOnlyList<int> versions = Aion2BossCatalog.VersionsOf(npcId);
            IReadOnlyList<BossTimerCast>? casts = null;
            int siteFights = 0;
            var fights = new List<VersionFight>();
            var names = new Dictionary<int, string>();
            bool siteFailed = false;
            if (ask)
            {
                try
                {
                    string also = versions.Count > 1 ? "&also=" + string.Join(',', versions.Skip(1).Take(15)) : "";
                    string body = await Http.GetStringAsync($"{Brand.Api}/api/boss-timers?npc={npcId}{also}").ConfigureAwait(false);
                    (casts, siteFights) = Parse(body);
                    fights.AddRange(ParseFights(body, names));
                    foreach (BossTimerCast c in casts)
                    {
                        names.TryAdd(c.Id, c.Name);
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException or InvalidOperationException or KeyNotFoundException or FormatException)
                {
                    siteFailed = true; // this PC's fights still count; the site is asked again in five minutes
                }
            }

            var local = await Task.Run(() => LocalFights(versions)).ConfigureAwait(false);
            var all = Merge(fights, local);
            var own = all.Where(f => f.Npc == npcId).ToList();
            var chosen = own.Count >= BossPattern.MinFights ? own : all;
            BossPattern pattern = BossPattern.Learn(chosen.Select(f => f.Fight).ToList());
            if (siteFailed && before is not null && pattern.Steps.Count == 0)
            {
                known = before;
            }
            else
            {
                known = new Known(casts, pattern.Steps.Count > 0 ? pattern.Fights : siteFights, pattern.Steps.Count > 0 ? pattern : null, names);
            }

            lock (Cache)
            {
                // a failed site keeps the boss asked again soon; a whole answer for six hours
                Cache[npcId] = (siteFailed ? DateTime.UtcNow - Keep + Retry : DateTime.UtcNow, known, ask);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or InvalidDataException
                                       or ArgumentException or FormatException or KeyNotFoundException or NotSupportedException)
        {
            lock (Cache)
            {
                Cache[npcId] = (DateTime.UtcNow - Keep + Retry, before, ask);
            }
        }
        finally
        {
            lock (Cache)
            {
                Loading.Remove(npcId);
            }
        }
    }

    /// <summary>This PC's saved fights of these boss versions (the newest <see cref="MostLocal"/>), each read once and kept.</summary>
    private static List<VersionFight> LocalFights(IReadOnlyList<int> versions)
    {
        var set = versions.ToHashSet();
        var found = new List<VersionFight>();
        foreach (History.FightLogEntry entry in History.FightLog.Entries().Where(e => e.Kind == History.FightLogKind.Boss && e.BossNpcId is int id && set.Contains(id))
                     .OrderByDescending(e => e.Start).Take(MostLocal))
        {
            PatternFight? fight;
            lock (LocalCache)
            {
                LocalCache.TryGetValue(entry.Id, out fight);
            }

            if (fight is null && !LocalCacheHas(entry.Id))
            {
                fight = History.FightLog.Open(entry) is { } input ? PatternFights.From(input) : null;
                lock (LocalCache)
                {
                    LocalCache[entry.Id] = fight;
                }
            }

            if (fight is not null)
            {
                found.Add(new VersionFight(entry.BossNpcId!.Value, fight));
            }
        }

        return found;
    }

    private static bool LocalCacheHas(string id)
    {
        lock (LocalCache)
        {
            return LocalCache.ContainsKey(id);
        }
    }

    /// <summary>The site's fights and this PC's, a fight both have (the player's own upload) once: the same attacks at the same times (a tenth of a second).</summary>
    public static List<VersionFight> Merge(IReadOnlyList<VersionFight> site, IReadOnlyList<VersionFight> local)
    {
        var all = local.ToList();
        foreach (VersionFight s in site)
        {
            if (!local.Any(l => l.Npc == s.Npc && Same(l.Fight, s.Fight)))
            {
                all.Add(s);
            }
        }

        return all;

        static bool Same(PatternFight a, PatternFight b)
        {
            int n = Math.Min(6, Math.Min(a.Casts.Count, b.Casts.Count));
            if (n < 2 || Math.Abs(a.Seconds - b.Seconds) > 1.5)
            {
                return false;
            }

            for (int i = 0; i < n; i++)
            {
                if (a.Casts[i].SkillId != b.Casts[i].SkillId || Math.Abs(a.Casts[i].At - b.Casts[i].At) > 0.25)
                {
                    return false;
                }
            }

            return true;
        }
    }

    private static double? Number(JsonElement o, string name) =>
        o.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) && double.IsFinite(d) ? d : null;

    /// <summary>The site's answer: {ok, npc, fights, casts: [{id, name, fights, first, every, dur}]}.</summary>
    public static (IReadOnlyList<BossTimerCast> Casts, int Fights) Parse(string body)
    {
        using JsonDocument doc = JsonDocument.Parse(body);
        JsonElement root = doc.RootElement;
        var list = new List<BossTimerCast>();
        foreach (JsonElement c in root.GetProperty("casts").EnumerateArray())
        {
            // an answer that isn't what it should be is skipped a cast at a time, never thrown
            if (c.ValueKind != JsonValueKind.Object || Number(c, "id") is not double id || id < 1 || id > int.MaxValue || Number(c, "first") is not double first)
            {
                continue;
            }

            string name = c.TryGetProperty("name", out JsonElement n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
            list.Add(new BossTimerCast((int)id, name.Length > 60 ? name[..60] : name, (int)Math.Clamp(Number(c, "fights") ?? 0, 0, int.MaxValue), first,
                Number(c, "every") is double every && every > 0 ? (double?)every : null, Math.Max(0, Number(c, "dur") ?? 0)));
        }

        return (list, (int)Math.Clamp(Number(root, "fights") ?? 0, 0, int.MaxValue));
    }

    /// <summary>
    /// The site's fights of the boss and its versions, when it sends them: {ids: [[id, name] ...], seq: [{n: version, s: seconds, k: killed,
    /// c: [[tenths, index into ids, wind-up tenths, health in thousandths or -1] ...]}]}. Anything not as it should be is skipped, never thrown.
    /// <paramref name="names"/> gets the skills' names as the uploads had them.
    /// </summary>
    public static List<VersionFight> ParseFights(string body, Dictionary<int, string>? names = null)
    {
        var fights = new List<VersionFight>();
        using JsonDocument doc = JsonDocument.Parse(body);
        JsonElement root = doc.RootElement;
        if (!root.TryGetProperty("seq", out JsonElement seq) || seq.ValueKind != JsonValueKind.Array || !root.TryGetProperty("ids", out JsonElement idsEl) || idsEl.ValueKind != JsonValueKind.Array)
        {
            return fights;
        }

        var ids = new List<int>();
        foreach (JsonElement x in idsEl.EnumerateArray())
        {
            int id = x.ValueKind == JsonValueKind.Array && x.GetArrayLength() > 0 && x[0].TryGetInt32(out int v) ? v : 0;
            ids.Add(id);
            if (id > 0 && names is not null && x.GetArrayLength() > 1 && x[1].ValueKind == JsonValueKind.String && x[1].GetString() is { Length: > 0 } name)
            {
                names.TryAdd(id, name.Length > 60 ? name[..60] : name);
            }
        }

        foreach (JsonElement f in seq.EnumerateArray())
        {
            if (f.ValueKind != JsonValueKind.Object || Number(f, "n") is not double npc || Number(f, "s") is not double seconds || seconds <= 0
                || !f.TryGetProperty("c", out JsonElement c) || c.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var casts = new List<PatternCast>();
            foreach (JsonElement x in c.EnumerateArray())
            {
                if (x.ValueKind != JsonValueKind.Array || x.GetArrayLength() < 3 || !x[0].TryGetInt32(out int t) || !x[1].TryGetInt32(out int i) || i < 0 || i >= ids.Count
                    || ids[i] <= 0 || !x[2].TryGetInt32(out int d))
                {
                    continue;
                }

                double? health = x.GetArrayLength() > 3 && x[3].TryGetInt32(out int h) && h is >= 0 and <= 1000 ? h / 1000.0 : null;
                casts.Add(new PatternCast(t / 10.0, ids[i], Math.Max(0, d) / 10.0, health));
            }

            if (casts.Count > 0)
            {
                bool killed = f.TryGetProperty("k", out JsonElement k) && k.ValueKind == JsonValueKind.True;
                fights.Add(new VersionFight((int)npc, new PatternFight(seconds, killed, casts.OrderBy(x => x.At).ToList())));
            }
        }

        return fights;
    }

    /// <summary>The callout for the boss being fought now (local time, like the casts); null when no boss is fought or nothing is known of it.
    /// <paramref name="firstHit"/>: the fight's start as the uploads count it (the first hit on that boss), null before it was hit; <paramref name="ask"/>:
    /// whether the site may be asked (the privacy policy accepted; without, this PC's own fights still count).</summary>
    public static BossCalloutFrame? Now(Aion2EntityDirectory dir, DateTime now, string language, Func<int, DateTime?> firstHit, bool ask)
    {
        if (Ui.CombatOverlayData.BossHp(dir, now) is not { } boss || dir.BossNpcIdOf(boss.Id) is not int npc)
        {
            return null;
        }

        string name = Aion2Npcs.NameOf(npc, language) ?? dir.MonsterNameFor(boss.Id) ?? "";
        TimeSpan? rage = Aion2BossRage.Known(npc) && boss.Since is DateTime since && since <= now ? since.AddSeconds(Aion2BossRage.SecondsFor(npc)) - now : null;
        if (firstHit(boss.Id) is not DateTime start || start > now || For(npc, ask) is not { } known)
        {
            return rage is null ? null : new BossCalloutFrame(name, Array.Empty<BossCallout>(), rage, 0);
        }

        if (known.Pattern is { } pattern)
        {
            var seen = dir.Combat.WindupsSince(boss.Id, start).Select(w => new PatternCast((w.At - start).TotalSeconds, w.SkillId, w.DurationMs / 1000.0, null)).ToList();
            double? health = boss.Max > 0 ? Math.Clamp((double)boss.Hp / boss.Max, 0, 1) : null;
            var next = pattern.Next(seen, (now - start).TotalSeconds, health, PaceNow(dir, boss.Id, boss.Max))
                .Select(n => new BossCallout(Label(n.SkillId, known.Names), n.In, n.Casting, n.SkillId)).ToList();
            return new BossCalloutFrame(name, next, rage, known.Fights);
        }

        return known.Casts is { Count: > 0 } casts
            ? new BossCalloutFrame(name, Upcoming(casts, dir.Combat.LastWindups(boss.Id, start), (now - start).TotalSeconds, start), rage, known.Fights)
            : rage is null ? null : new BossCalloutFrame(name, Array.Empty<BossCallout>(), rage, 0);
    }

    /// <summary>The health share the boss lost per second over the last 15 s of its readings (everybody's damage); 0 while it does not fall.</summary>
    private static double PaceNow(Aion2EntityDirectory dir, int id, long max)
    {
        if (max <= 0 || dir.HitPoints.Latest(id) is not { Hp: > 0 } last)
        {
            return 0;
        }

        var samples = dir.HitPoints.SamplesAround(id, last.At.AddSeconds(-15), last.At);
        if (samples.Count < 2)
        {
            return 0;
        }

        double seconds = (last.At - samples[0].At).TotalSeconds, lost = samples[0].Hp - last.Hp;
        return seconds >= 3 && lost > 0 ? lost / (double)max / seconds : 0;
    }

    /// <summary>The game's own name of a skill in the player's language; the uploads' name (another player's language) only for one the meter doesn't know.</summary>
    private static string Label(int id, IReadOnlyDictionary<int, string> names)
    {
        string own = Aion2SkillNames.DisplayOf(id);
        return own.Length > 0 && own != id.ToString() ? own : names.TryGetValue(id, out string? n) && n.Length > 0 ? n : own;
    }

    /// <summary>The casts due within <see cref="Ahead"/> seconds, soonest first: a seen one again after its interval, an unseen one at its first time.
    /// <paramref name="seen"/>: when each skill was last begun; <paramref name="t"/>: seconds since the boss's start.</summary>
    internal static IReadOnlyList<BossCallout> Upcoming(IReadOnlyList<BossTimerCast> casts, IReadOnlyDictionary<int, DateTime> seen, double t, DateTime origin)
    {
        var due = new List<BossCallout>();
        foreach (BossTimerCast c in casts)
        {
            // the game's own name in the player's language; the uploads' name (another player's language) only for a skill the meter doesn't know
            string own = Aion2SkillNames.DisplayOf(c.Id), label = own.Length > 0 && own != c.Id.ToString() ? own : c.Name;
            double? at;
            if (seen.TryGetValue(c.Id, out DateTime last))
            {
                double began = (last - origin).TotalSeconds;
                if (t - began < Math.Max(c.Dur, 0.5))
                {
                    due.Add(new BossCallout(label, 0, true, c.Id)); // being cast now
                    continue;
                }

                at = c.Every is double every ? began + every : null;
            }
            else
            {
                at = c.First;
            }

            if (at is double when && when - t <= Ahead && when - t >= -Late)
            {
                due.Add(new BossCallout(label, Math.Max(0, when - t), false, c.Id));
            }
        }

        return due.OrderBy(d => d.In).ToList();
    }
}
