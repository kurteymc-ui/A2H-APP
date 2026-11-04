// rDPS / nDPS: who made the group's damage possible.
//
// Ported from waffle_meter (https://github.com/Waffle-ens/waffle_meter, dotnet/src/WaffleMeter.Data: DpsMetrics.cs,
// PartySynergyCatalog.cs, BuffGain.cs, BuffUptime.cs; DataManager's buff base codes and exclusive pairs), under its MIT License:
//
//   MIT License
//
//   Copyright (c) 2026 TK-open-public
//
//   Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation
//   files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy,
//   modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software
//   is furnished to do so, subject to the following conditions:
//
//   The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
//
//   THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
//   OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
//   LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
//   CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
// What it does (5 November 2026, matched fight by fight to the rDPS of the meters players compare against, the hits and the effects as this meter
// read them: every player's rDPS within about 1 % on three boss fights of a party of five). Hit by hit, every effect on the player and on the boss
// at that moment is worth a multiplier 1 + v (assets/aion2/skills/rdps_values.json, by the effect's exact code): the hit without them would have done
// hit / Π(1 + v), and the difference is shared among the effects in proportion to log(1 + v) - FFLogs' split. The player's own effects take their share
// too, which stays with him; the others' shares go to their casters. Damage-over-time ticks are left out of the split. nDPS = (damage - what the others'
// effects added) / time; rDPS = nDPS + what one's own effects added to the others. Extra hits a party effect deals stay on the meter they land on.

namespace AionDPS.Combat;

/// <summary>One effect (a buff on a player or a debuff on the boss) as the model sees it: its family (display base, for names and the support tab), who cast it,
/// the share of the fight it was on (0..100), the caster's skill level (0 = not read), its merged spans in milliseconds of the fight and its exact code
/// (what it is worth, <see cref="RaidDpsValues"/>).</summary>
public readonly record struct RaidBuff(int DisplayBase, int Caster, double UptimePercent, int Level, bool BossScope, IReadOnlyList<(long Start, long End)> Spans, int EffectId = 0);

/// <param name="Hits">The player's direct hits of the fight (milliseconds from the window's start, amount), damage-over-time ticks left out: rDPS is
/// worked out hit by hit. Without them the player is credited nothing and keeps his DPS.</param>
public sealed record RaidParticipant(int Id, double Dps, IReadOnlyList<RaidBuff> Buffs, IReadOnlyList<(long AtMs, long Amount)>? Hits = null);

/// <param name="Ndps">What the player did without the effects other people put on him (or on the boss).</param>
/// <param name="Rdps">nDPS plus everything this player's effects added to the others.</param>
/// <param name="Given">rDPS − nDPS: what this player lent out.</param>
public readonly record struct RaidDpsResult(double Ndps, double Rdps, double Given);

/// <summary>What each effect is worth (assets/aion2/skills/rdps_values.json): v of the multiplier 1 + v, by the effect's exact code; 0 for one not listed.</summary>
public static class RaidDpsValues
{
    private static Dictionary<int, double>? _values;

    /// <summary>The value of an effect; a 9-digit code (an older frame's, the skill's id with a level digit: 181 900 010) as its 8-digit effect (18 190 001).</summary>
    public static double Of(int effectId) =>
        effectId == 0 ? 0
        : Values().TryGetValue(effectId, out double v) ? v
        : effectId >= 100_000_000 && Values().TryGetValue(effectId / 10, out double old) ? old : 0;

    private static Dictionary<int, double> Values()
    {
        if (_values is { } known)
        {
            return known;
        }

        var values = new Dictionary<int, double>();
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "skills", "rdps_values.json");
            if (File.Exists(path))
            {
                using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
                foreach (System.Text.Json.JsonProperty p in doc.RootElement.GetProperty("values").EnumerateObject())
                {
                    if (int.TryParse(p.Name, out int id) && p.Value.TryGetDouble(out double v) && v > 0 && v < 10)
                    {
                        values[id] = v;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // no values: rDPS equals DPS
        }

        _values = values;
        return values;
    }
}

public static class RaidDps
{
    // the party effects shown as such (a player's party-buff line, buff alerts, support badges), by display base (the buff's skill id / 100 000 × 10 000);
    // rDPS prices far more (every effect in RaidDpsValues, by its exact code)
    public const int SwordCounter = 11_780_000;        // Gladiator
    public const int SwordBloodBlade = 11_340_000;     // Gladiator: its hits land on the others' meters
    public const int GuardianFervor = 12_780_000;      // Templar
    public const int ClericProtectLight = 17_410_000;  // Cleric
    public const int ClericEarthBlessing = 17_400_058; // Cleric: the party side of the earth skill
    public const int ChanterMantra = 18_190_000;       // Chanter
    public const int ChanterGale = 18_250_000;         // Chanter
    public const int ChanterEarthPromise = 18_780_000; // Chanter: a debuff on the boss
    public const int ClericChainTorment = 17_070_000;  // Cleric: a debuff on the boss, Earth's Promise's twin (the two don't stack)

    // the party code of the Cleric's earth skill (self and party ranks) shows as its own effect, in the 9-digit form and in the 8-digit form the buff frames
    // carry for it on the party (17400038 = 174000381 / 10: every Cleric fight of 3-4 Nov 2026 had it, 25-34 times, and none was priced - the Cleric's
    // Earth's Blessing, worth 10,000 DPS in a Transcendent Bakarma kill, went to nobody)
    private static readonly Dictionary<int, int> DisplayBaseOverrides = new()
    {
        [174000271] = ClericEarthBlessing, [174000281] = ClericEarthBlessing,
        [174000371] = ClericEarthBlessing, [174000381] = ClericEarthBlessing,
        [174000571] = ClericEarthBlessing, [174000591] = ClericEarthBlessing,
        [17400027] = ClericEarthBlessing, [17400028] = ClericEarthBlessing,
        [17400037] = ClericEarthBlessing, [17400038] = ClericEarthBlessing,
        [17400057] = ClericEarthBlessing, [17400059] = ClericEarthBlessing,
    };

    /// <summary>A buff's display base from its skill id (the buff frame's effect / 10): class skills 110 000 000..199 999 999 fold to
    /// their skill family, and so do the 8-digit codes 11 000 000..19 999 999 the buff frames carry for a class effect on the party (the
    /// Templar's Fury on the others is 12780001, the Gladiator's counter 11780007: a fight of 1 Nov 2026 had 62 of them, and rDPS priced
    /// none because only the 9-digit form was folded). The Cleric's party-side earth effect keeps its own code.</summary>
    public static int DisplayBase(int skillId) =>
        DisplayBaseOverrides.TryGetValue(skillId, out int mapped) ? mapped
        : skillId is >= 110_000_000 and <= 199_999_999 ? skillId / 100_000 * 10_000
        : skillId is >= 11_000_000 and <= 19_999_999 && skillId != ClericEarthBlessing ? skillId / 10_000 * 10_000 : skillId;

    /// <summary>Is this one of the party effects shown as such (the buff line, alerts, support badges).</summary>
    public static bool IsSynergy(int displayBase) => displayBase is SwordCounter or SwordBloodBlade or GuardianFervor or ClericProtectLight
        or ClericEarthBlessing or ChanterMantra or ChanterGale or ChanterEarthPromise or ClericChainTorment;

    /// <summary>What an effect is worth: v of its multiplier 1 + v (<see cref="RaidDpsValues"/>).</summary>
    public static double ValueOf(RaidBuff buff) => RaidDpsValues.Of(buff.EffectId);

    /// <summary>nDPS, rDPS and what each player lent out, for the players of one fight window of <paramref name="seconds"/>.</summary>
    /// <param name="bossDebuffs">The effects on the boss (from anyone): they count on every player's hits.</param>
    /// <param name="byBuff">When given, filled with what each player lent out per effect family: (caster, display base) → DPS given.</param>
    public static Dictionary<int, RaidDpsResult> Compute(IReadOnlyList<RaidParticipant> participants, IReadOnlyList<RaidBuff> bossDebuffs, double seconds,
        Dictionary<(int Caster, int DisplayBase), double>? byBuff = null)
    {
        var results = new Dictionary<int, RaidDpsResult>();
        if (participants.Count == 0)
        {
            return results;
        }

        var ids = participants.Select(p => p.Id).ToHashSet();
        var given = participants.ToDictionary(p => p.Id, _ => 0.0);
        var received = participants.ToDictionary(p => p.Id, _ => 0.0);
        var active = new List<(RaidBuff Buff, double Log)>();
        foreach (RaidParticipant p in participants)
        {
            if (p.Hits is not { Count: > 0 } hits || seconds <= 0)
            {
                continue;
            }

            // every effect worth something on the player (his own and anyone's) and on the boss
            var effects = p.Buffs.Concat(bossDebuffs).Select(b => (Buff: b, Log: Math.Log(1.0 + ValueOf(b)))).Where(e => e.Log > 0 && e.Buff.Spans.Count > 0).ToList();
            if (effects.Count == 0)
            {
                continue;
            }

            foreach ((long at, long amount) in hits)
            {
                if (amount <= 0)
                {
                    continue;
                }

                active.Clear();
                double logM = 0;
                foreach ((RaidBuff buff, double log) in effects)
                {
                    if (Covers(buff.Spans, at))
                    {
                        active.Add((buff, log));
                        logM += log;
                    }
                }

                if (logM <= 0)
                {
                    continue;
                }

                // what the effects added to this hit, shared in proportion to log(1 + v); the player's own share stays his
                double added = amount * (1.0 - Math.Exp(-logM));
                foreach ((RaidBuff buff, double log) in active)
                {
                    if (buff.Caster == p.Id || !ids.Contains(buff.Caster))
                    {
                        continue;
                    }

                    double share = added * log / logM / seconds;
                    given[buff.Caster] += share;
                    received[p.Id] += share;
                    if (byBuff is not null)
                    {
                        byBuff[(buff.Caster, buff.DisplayBase)] = byBuff.GetValueOrDefault((buff.Caster, buff.DisplayBase)) + share;
                    }
                }
            }
        }

        foreach (RaidParticipant p in participants)
        {
            double ndps = Math.Max(0.0, p.Dps - received[p.Id]);
            results[p.Id] = new RaidDpsResult(ndps, ndps + given[p.Id], given[p.Id]);
        }

        return results;
    }

    /// <summary>Whether a span of the merged, ordered list holds the moment: after its start, up to its end (a hit at the very moment an effect lands
    /// was struck before it).</summary>
    private static bool Covers(IReadOnlyList<(long Start, long End)> spans, long at)
    {
        int lo = 0, hi = spans.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (spans[mid].End < at)
            {
                lo = mid + 1;
            }
            else if (spans[mid].Start >= at)
            {
                hi = mid - 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Spans merged into ordered, non-overlapping runs.</summary>
    public static List<(long Start, long End)> Merge(IEnumerable<(long Start, long End)> spans)
    {
        var merged = new List<(long Start, long End)>();
        foreach ((long s, long e) in spans.Where(x => x.End > x.Start).OrderBy(x => x.Start))
        {
            if (merged.Count > 0 && s <= merged[^1].End)
            {
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, e));
            }
            else
            {
                merged.Add((s, e));
            }
        }

        return merged;
    }

    /// <summary>
    /// The effects worth something (<see cref="RaidDpsValues"/>) on each of <paramref name="playerIds"/> and on <paramref name="bossId"/> during [from, to],
    /// from the buff events: spans per (effect, caster) clipped to the window, the share of the window they cover and the highest level seen. An effect
    /// slot the game hands to another effect (5 November 2026: the Templar's Fury took over the slot of a Gladiator's counter on the same player, a renewal
    /// of its instance carrying the other code) ends the first there.
    /// </summary>
    public static (Dictionary<int, List<RaidBuff>> OnPlayers, List<RaidBuff> OnBoss) BuffsIn(IReadOnlyList<BuffEvent> events, IReadOnlyCollection<int> playerIds,
        int? bossId, DateTime from, DateTime to)
    {
        double windowMs = (to - from).TotalMilliseconds;
        var players = playerIds.ToHashSet();
        var spans = new Dictionary<(int Target, int Effect, int Caster), (List<(long, long)> Spans, int Level)>();
        var open = new Dictionary<(int Target, int Instance), (DateTime Start, int Effect, int Caster, int Level, DateTime? Expect)>();
        void Close((int Target, int Instance) key, DateTime at)
        {
            if (!open.Remove(key, out var o))
            {
                return;
            }

            long s = (long)Math.Max(0, (o.Start - from).TotalMilliseconds), e = (long)Math.Min(windowMs, (at - from).TotalMilliseconds);
            if (e > s)
            {
                var k = (key.Target, o.Effect, o.Caster);
                if (!spans.TryGetValue(k, out var acc))
                {
                    acc = (new List<(long, long)>(), 0);
                }

                acc.Item1.Add((s, e));
                spans[k] = (acc.Item1, Math.Max(acc.Level, o.Level));
            }
        }

        foreach (BuffEvent ev in events.Where(b => (players.Contains(b.Target) || b.Target == bossId) && b.At <= to).OrderBy(b => b.At))
        {
            foreach (var key in open.Where(kv => kv.Value.Expect is DateTime expect && expect < ev.At).Select(kv => kv.Key).ToList())
            {
                Close(key, open[key].Expect!.Value);
            }

            var instanceKey = (ev.Target, ev.Instance);
            DateTime? expectAt = ev.DurationMs >= 0 ? ev.At.AddMilliseconds(ev.DurationMs) : null;
            switch (ev.Kind)
            {
                case BuffKind.On:
                case BuffKind.Renewed:
                    if (ev.Kind == BuffKind.Renewed && open.TryGetValue(instanceKey, out var current) && current.Effect == ev.SkillId && current.Caster == ev.Caster)
                    {
                        open[instanceKey] = current with { Expect = expectAt, Level = Math.Max(current.Level, ev.Level) };
                        break;
                    }

                    Close(instanceKey, ev.At); // a new effect in the slot ends what was in it
                    if (RaidDpsValues.Of(ev.SkillId) > 0)
                    {
                        open[instanceKey] = (ev.At, ev.SkillId, ev.Caster, ev.Level, expectAt);
                    }

                    break;
                case BuffKind.End:
                    Close(instanceKey, ev.At);
                    break;
            }
        }

        foreach (var key in open.Keys.ToList())
        {
            Close(key, open[key].Expect is DateTime expect && expect < to ? expect : to);
        }

        var onPlayers = new Dictionary<int, List<RaidBuff>>();
        var onBoss = new List<RaidBuff>();
        foreach (((int target, int effect, int caster), (List<(long, long)> list, int level)) in spans)
        {
            List<(long Start, long End)> merged = Merge(list);
            double uptime = windowMs > 0 ? 100.0 * merged.Sum(s => s.End - s.Start) / windowMs : 0;
            bool boss = target == bossId && !players.Contains(target);
            var buff = new RaidBuff(DisplayBase(effect), caster, uptime, level, boss, merged, effect);
            if (boss)
            {
                onBoss.Add(buff);
            }
            else
            {
                (onPlayers.TryGetValue(target, out var l) ? l : onPlayers[target] = new List<RaidBuff>()).Add(buff);
            }
        }

        return (onPlayers, onBoss);
    }
}
