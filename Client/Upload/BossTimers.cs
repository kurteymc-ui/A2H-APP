using System.IO;
using System.Net.Http;
using System.Text.Json;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Upload;

/// <summary>One of a boss's casts as everyone's uploads show it (the site's GET /meter/api/boss-timers): the skill, how many of the fights had it, when it
/// first came (seconds after the boss went into combat), how often it came again (null: once a fight) and how long it takes.</summary>
public sealed record BossTimerCast(int Id, string Name, int Fights, double First, double? Every, double Dur);

/// <summary>A cast the callout names: the skill and the seconds until it comes (0 while it is being cast).</summary>
public sealed record BossCallout(string Name, double In, bool Casting);

/// <summary>What the meter overlay's callout line shows: the next casts (soonest first) and the time left to the boss's rage, when the game data knows it.</summary>
public sealed record BossCalloutFrame(string Boss, IReadOnlyList<BossCallout> Next, TimeSpan? Rage, int Fights);

/// <summary>
/// Boss timers learnt from uploads: every fight the meters send keeps the boss's casts by skill id (src/meter.js, mt_casts), and the site works out per boss
/// when each cast first comes, how often again, how long it takes. While a boss is fought the meter fetches that once (kept six hours; only with the privacy
/// policy accepted, the caller says so), and counts down to the next casts from the boss's wind-ups it recorded: from the last time a cast began, else
/// from the fight's start, which is the first hit on the boss as the uploads count it. A cast whose time passed unseen (a phase that did not come yet) is
/// left out. The rage comes from the game's own table only (<see cref="Aion2BossRage"/>), from when the game says the boss went into combat.
/// </summary>
public static class BossTimers
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly TimeSpan Keep = TimeSpan.FromHours(6), Retry = TimeSpan.FromMinutes(5);
    /// <summary>How far ahead a cast is named, and how long one stays named after its time passed (it may come a little late).</summary>
    public const double Ahead = 60, Late = 3;

    private static readonly Dictionary<int, (DateTime At, IReadOnlyList<BossTimerCast>? Casts, int Fights)> Known = new();
    private static readonly HashSet<int> Loading = new();

    /// <summary>What the site knows of this boss; null until it answered (the first call asks it).</summary>
    public static (IReadOnlyList<BossTimerCast> Casts, int Fights)? For(int npcId)
    {
        lock (Known)
        {
            bool have = Known.TryGetValue(npcId, out var hit);
            if (have && DateTime.UtcNow - hit.At < (hit.Casts is null ? Retry : Keep))
            {
                return hit.Casts is null ? null : (hit.Casts, hit.Fights);
            }

            if (Loading.Add(npcId))
            {
                _ = FetchAsync(npcId, have ? hit : default);
            }

            return have && hit.Casts is not null ? (hit.Casts, hit.Fights) : null; // the older answer while a new one comes
        }
    }

    private static async Task FetchAsync(int npcId, (DateTime At, IReadOnlyList<BossTimerCast>? Casts, int Fights) before)
    {
        IReadOnlyList<BossTimerCast>? casts = null;
        int fights = 0;
        try
        {
            string body = await Http.GetStringAsync($"{Brand.Api}/api/boss-timers?npc={npcId}").ConfigureAwait(false);
            (casts, fights) = Parse(body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            casts = null;
        }
        finally
        {
            lock (Known)
            {
                // a failed refresh keeps the answer it had (asked again in five minutes)
                Known[npcId] = casts is null && before.Casts is not null ? (DateTime.UtcNow - Keep + Retry, before.Casts, before.Fights) : (DateTime.UtcNow, casts, fights);
                Loading.Remove(npcId);
            }
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

    /// <summary>The callout for the boss being fought now (local time, like the casts); null when no boss is fought or nothing is known of it.
    /// <paramref name="firstHit"/>: the fight's start as the uploads count it (the first hit on that boss), null before it was hit; <paramref name="ask"/>:
    /// whether the site may be asked (the privacy policy accepted).</summary>
    public static BossCalloutFrame? Now(Aion2EntityDirectory dir, DateTime now, string language, Func<int, DateTime?> firstHit, bool ask)
    {
        if (Ui.CombatOverlayData.BossHp(dir, now) is not { } boss || dir.BossNpcIdOf(boss.Id) is not int npc)
        {
            return null;
        }

        string name = Aion2Npcs.NameOf(npc, language) ?? dir.MonsterNameFor(boss.Id) ?? "";
        TimeSpan? rage = Aion2BossRage.Known(npc) && boss.Since is DateTime since && since <= now ? since.AddSeconds(Aion2BossRage.SecondsFor(npc)) - now : null;
        if (!ask || firstHit(boss.Id) is not DateTime start || start > now || For(npc) is not { } known)
        {
            return rage is null ? null : new BossCalloutFrame(name, Array.Empty<BossCallout>(), rage, 0);
        }

        return new BossCalloutFrame(name, Upcoming(known.Casts, dir.Combat.LastWindups(boss.Id, start), (now - start).TotalSeconds, start), rage, known.Fights);
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
                    due.Add(new BossCallout(label, 0, true)); // being cast now
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
                due.Add(new BossCallout(label, Math.Max(0, when - t), false));
            }
        }

        return due.OrderBy(d => d.In).ToList();
    }
}
