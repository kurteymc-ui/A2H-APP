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
// What it does. Each party buff on a player (and each debuff on the boss) becomes a relative gain from its uptime and its effect
// at the caster's skill level (read off the buff frame, see Aion2FrameDecoder.DecodeBuff). Gains compose as Π(1+gain)−1;
// nDPS = DPS / (1 + total gain) is what the player did without other people's buffs; each caster is credited the recipient's
// nDPS × that buff's gain, and rDPS = nDPS + everything a player lent out. The extra hits some party effects deal (a Cleric's
// Earth's Blessing, a Gladiator's blood blade) arrive as real damage on the recipient's meter: they are moved to the caster
// whole. Only the eight party-synergy effects are priced (their values by level are in Effects below); the rest give nothing,
// so rDPS never invents a contribution. The values were measured on the Korean servers by waffle_meter's authors.

namespace AionDPS.Combat;

public enum BuffEffectKind
{
    None = 0,
    DamageAmp,       // damage amplification %p (the additive amplification bucket)
    BossResistDown,  // the boss's damage resistance taken down %p (the same bucket, as a subtraction); boss debuffs only
    AttackRatio,     // attack %p (the additive attack multiplier)
    CritDamageAmp,   // critical damage amplification %p
    CritRate,        // critical rate %p
    SmiteRate,       // smite (double damage) rate %p
    PerfectRate,     // perfect rate %p
    FrontAmp,        // frontal damage amplification %p
    CombatSpeed,     // attack speed %p
}

public readonly record struct BuffGainEffect(BuffEffectKind Kind, double Value);

/// <summary>Where in the damage formula a player already sits; a buff is worth less on a bucket that is already full. The default is a
/// typical end-game character (waffle_meter's measured baseline).</summary>
public readonly record struct BuffGainContext(
    double AmpBucketPercent, double AttackIncreasePercent, double CritAmpPercent, double FrontAmpPercent,
    double CritRate, double SmiteRate, double PerfectRate, double FrontRate, double PerfectBonusRatio)
{
    public static readonly BuffGainContext Default = new(174.9, 123.0, 79.2, 13.7, 0.865, 0.468, 0.510, 0.20, 0.079);

    public double CritMultiplier => 1.0 + Clamp01(CritRate) * (0.5 + Math.Max(CritAmpPercent, 0.0) / 100.0);

    public double SmiteMultiplier => 1.0 + Clamp01(SmiteRate);

    public double PerfectMultiplier => 1.0 + Clamp01(PerfectRate) * Math.Max(PerfectBonusRatio, 0.0);

    public double FrontMultiplier => 1.0 + Clamp01(FrontRate) * Math.Max(FrontAmpPercent, 0.0) / 100.0;

    private static double Clamp01(double v) => Math.Clamp(v, 0.0, 1.0);
}

/// <summary>One buff (or boss debuff) as the model sees it: its display base, who cast it, the share of the fight it was on (0..100),
/// the caster's skill level (0 = not read) and its merged spans in milliseconds of the fight.</summary>
public readonly record struct RaidBuff(int DisplayBase, int Caster, double UptimePercent, int Level, bool BossScope, IReadOnlyList<(long Start, long End)> Spans);

/// <param name="Hits">The player's damage hits (milliseconds from the window's start, amount): given, rDPS is worked out hit by hit - each hit's buffed
/// share goes to whoever's effects were on at that moment - instead of from the effects' uptimes over the whole window.</param>
public sealed record RaidParticipant(int Id, double Dps, IReadOnlyList<RaidBuff> Buffs, IReadOnlyDictionary<int, long> GrantedDamageBySource, int JobPrefix = 0,
    IReadOnlyList<(long AtMs, long Amount)>? Hits = null);

/// <param name="Ndps">What the player did without the buffs other people put on them.</param>
/// <param name="Rdps">nDPS plus everything this player's buffs added to the others.</param>
/// <param name="Given">rDPS − nDPS: what this player lent out.</param>
public readonly record struct RaidDpsResult(double Ndps, double Rdps, double Given);

public static class RaidDps
{
    // the eight party-synergy effects, by display base (the buff's skill id / 100 000 × 10 000)
    public const int SwordCounter = 11_780_000;        // Gladiator
    public const int SwordBloodBlade = 11_340_000;     // Gladiator: its hits land on the others' meters
    public const int GuardianFervor = 12_780_000;      // Templar
    public const int ClericProtectLight = 17_410_000;  // Cleric
    public const int ClericEarthBlessing = 17_400_058; // Cleric: the party side of the earth skill
    public const int ChanterMantra = 18_190_000;       // Chanter
    public const int ChanterGale = 18_250_000;         // Chanter
    public const int ChanterEarthPromise = 18_780_000; // Chanter: a debuff on the boss
    public const int ClericChainTorment = 17_070_000;  // Cleric: a debuff on the boss, Earth's Promise's twin (the two don't stack)

    private const double MaxSingleEffectGain = 1.0;
    private const double MaxTotalExternalGain = 4.0;

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

    /// <summary>Effects the game does not stack: only one of each pair counts (a fixed winner, else the higher level, else the tie winner).</summary>
    private static readonly (int A, int B, int FixedWinner, int TieWinner)[] ExclusivePairs =
    {
        (SwordCounter, GuardianFervor, 0, 0),
        (ClericProtectLight, ChanterMantra, 0, ChanterMantra),
        (ClericEarthBlessing, ChanterGale, ChanterGale, 0),
        (ClericChainTorment, ChanterEarthPromise, 0, ChanterEarthPromise), // the game's group "target damage resistance down, Cleric/Chanter" (rdps_watchlist.json)
    };

    /// <summary>A buff's display base from its skill id (the buff frame's effect / 10): class skills 110 000 000..199 999 999 fold to
    /// their skill family, and so do the 8-digit codes 11 000 000..19 999 999 the buff frames carry for a class effect on the party (the
    /// Templar's Fury on the others is 12780001, the Gladiator's counter 11780007: a fight of 1 Nov 2026 had 62 of them, and rDPS priced
    /// none because only the 9-digit form was folded). The Cleric's party-side earth effect keeps its own code.</summary>
    public static int DisplayBase(int skillId) =>
        DisplayBaseOverrides.TryGetValue(skillId, out int mapped) ? mapped
        : skillId is >= 110_000_000 and <= 199_999_999 ? skillId / 100_000 * 10_000
        : skillId is >= 11_000_000 and <= 19_999_999 && skillId != ClericEarthBlessing ? skillId / 10_000 * 10_000 : skillId;

    /// <summary>Is this one of the effects rDPS prices (worth collecting spans for).</summary>
    public static bool IsSynergy(int displayBase) => displayBase is SwordCounter or SwordBloodBlade or GuardianFervor or ClericProtectLight
        or ClericEarthBlessing or ChanterMantra or ChanterGale or ChanterEarthPromise or ClericChainTorment;

    /// <summary>The effect that granted a damage skill's hits to a party member (the extra hits land as real damage), or 0.</summary>
    public static int GrantedDamageSource(int skillCode)
    {
        if (skillCode == ClericEarthBlessing)
        {
            return ClericEarthBlessing;
        }

        return skillCode / 10_000 * 10_000 == SwordBloodBlade ? SwordBloodBlade : 0;
    }

    private static int GrantingJobPrefix(int displayBase) => displayBase switch
    {
        SwordBloodBlade => 11,
        ClericEarthBlessing => 17,
        _ => 0,
    };

    /// <summary>The effects of a party-synergy buff at its caster's level; level 0 (not read) counts as level 1, the floor of the real
    /// scale, so rDPS under-credits rather than inventing.</summary>
    public static IReadOnlyList<BuffGainEffect>? Effects(int displayBase, int level)
    {
        if (level <= 0)
        {
            level = 1;
        }

        static BuffGainEffect Amp(double v) => new(BuffEffectKind.DamageAmp, v);
        return displayBase switch
        {
            SwordCounter => new[] { Amp(5.4 + 0.4 * (level - 1)) },
            GuardianFervor => new[] { Amp(5.5 + 0.5 * (level - 1)) },
            ChanterMantra => Ladder(new List<BuffGainEffect> { Amp(10.5 + 0.5 * (level - 1)) },
                (15, new(BuffEffectKind.CritDamageAmp, 5.0)), (20, new(BuffEffectKind.SmiteRate, 5.0)), (25, new(BuffEffectKind.PerfectRate, 10.0))),
            ChanterGale => Ladder(new List<BuffGainEffect>(), (20, new(BuffEffectKind.AttackRatio, 10.0)), (25, Amp(5.0))),
            ChanterEarthPromise => new[] { new BuffGainEffect(BuffEffectKind.BossResistDown, -(5.4 + 0.4 * (level - 1))) },
            // No tooltip value in the data for the Cleric's chain: priced as the Chanter's twin it does not stack with (the same game group; the pairs the
            // meter knows both ways - Mantra and Protective Light, the counter and Fury - run on the same or nearly the same scale). Measured on the owner's
            // fights of 21 Oct - 1 Nov 2026, damage with it on was 2-5 % higher, Earth's Promise 2-7 %: the same range, not precise enough for a value of its own.
            ClericChainTorment => new[] { new BuffGainEffect(BuffEffectKind.BossResistDown, -(5.4 + 0.4 * (level - 1))) },
            ClericEarthBlessing => Ladder(new List<BuffGainEffect>(), (10, new(BuffEffectKind.SmiteRate, 5.0)), (15, new(BuffEffectKind.AttackRatio, 5.0))),
            ClericProtectLight => Ladder(new List<BuffGainEffect> { Amp(10.5 + 0.5 * (level - 1)) },
                (20, new(BuffEffectKind.PerfectRate, 10.0)), (25, new(BuffEffectKind.SmiteRate, 5.0))),
            SwordBloodBlade => Array.Empty<BuffGainEffect>(),
            _ => null,
        };

        IReadOnlyList<BuffGainEffect> Ladder(List<BuffGainEffect> start, params (int From, BuffGainEffect Effect)[] steps)
        {
            foreach ((int from, BuffGainEffect effect) in steps)
            {
                if (level >= from)
                {
                    start.Add(effect);
                }
            }

            return start;
        }
    }

    /// <summary>nDPS, rDPS and what each player lent out, for the players of one fight window of <paramref name="seconds"/>.</summary>
    /// <param name="byBuff">When given, filled with what each player lent out per effect: (caster, effect's display base) → DPS given.</param>
    public static Dictionary<int, RaidDpsResult> Compute(IReadOnlyList<RaidParticipant> participants, IReadOnlyList<RaidBuff> bossDebuffs, double seconds,
        Dictionary<(int Caster, int DisplayBase), double>? byBuff = null)
    {
        var results = new Dictionary<int, RaidDpsResult>();
        if (participants.Count == 0)
        {
            return results;
        }

        var ids = participants.Select(p => p.Id).ToHashSet();
        var incoming = participants.ToDictionary(p => p.Id, _ => new List<(int Actor, double Gain, int Base)>());
        BuffGainContext context = BuffGainContext.Default;
        foreach (RaidParticipant p in participants)
        {
            foreach (RaidBuff buff in Surviving(p.Buffs))
            {
                if (buff.Caster == p.Id)
                {
                    continue; // one's own buffs are one's own play
                }

                double gain = Gain(buff, context);
                if (gain > 0)
                {
                    incoming[p.Id].Add((ids.Contains(buff.Caster) ? buff.Caster : -1, gain, buff.DisplayBase));
                }
            }
        }

        foreach (RaidBuff debuff in bossDebuffs)
        {
            if (!ids.Contains(debuff.Caster))
            {
                continue;
            }

            foreach (RaidParticipant p in participants)
            {
                if (p.Id == debuff.Caster)
                {
                    continue;
                }

                double gain = Gain(debuff, context);
                if (gain > 0)
                {
                    incoming[p.Id].Add((debuff.Caster, gain, debuff.DisplayBase));
                }
            }
        }

        // the extra hits a party effect dealt on someone's meter move to whoever granted them
        var moves = new List<(int Recipient, int Granter, long Damage, int Base)>();
        foreach (RaidParticipant p in participants)
        {
            foreach ((int source, long damage) in p.GrantedDamageBySource)
            {
                if (damage > 0)
                {
                    foreach ((int granter, long share) in SplitGrant(p, source, damage, ids, participants))
                    {
                        moves.Add((p.Id, granter, share, source));
                    }
                }
            }
        }

        var movedOut = participants.ToDictionary(p => p.Id, _ => 0L);
        foreach ((int recipient, int _, long damage, int _) in moves)
        {
            movedOut[recipient] += damage;
        }

        var ndps = new Dictionary<int, double>();
        // hit by hit (as FFLogs): for every hit, the effects of the others on at that moment (of a non-stacking pair only one) make a multiplier M;
        // the hit's buffed share (1 - 1/M) goes to their casters in proportion to log(1 + gain), so overlapping buffs share the overlap instead of
        // losing it. A player without hits falls back to the effects' uptimes over the window (the old estimate).
        var hitGiven = participants.ToDictionary(p => p.Id, _ => 0.0);
        var perHit = new HashSet<int>();
        foreach (RaidParticipant p in participants)
        {
            double own = Math.Max(0.0, p.Dps - PerSecond(movedOut[p.Id], seconds));
            if (p.Hits is { Count: > 0 } hits && seconds > 0)
            {
                perHit.Add(p.Id);
                // Every effect on the player (his own and those of players outside the window too) takes part in the pairs that do not stack, and only
                // then are his own and the outsiders' left out of the credit: a Chanter's own Undefeated Mantra that wins over a Cleric's Light of
                // Protection leaves the Cleric nothing for those hits (3 November 2026: the Cleric got the Chanter's rDPS in a fight the other meter split
                // the other way round; the own effects were taken out before the pairs were settled, so the Cleric's always "won").
                var onPlayer = p.Buffs.Concat(bossDebuffs).ToList();
                double hitTotal = 0, lostTotal = 0;
                foreach ((long at, long amount) in hits)
                {
                    hitTotal += amount;
                    var gains = new List<(RaidBuff Buff, double Gain)>();
                    double m = 1.0;
                    foreach (RaidBuff b in ActiveAt(onPlayer, at))
                    {
                        if (b.Caster == p.Id || !ids.Contains(b.Caster))
                        {
                            continue; // won its pair, but it is the player's own play (or no one's in the window): no one is credited
                        }

                        double g = Gain(b with { UptimePercent = 100 }, context);
                        if (g > 0)
                        {
                            gains.Add((b, g));
                            m *= 1.0 + g;
                        }
                    }

                    if (gains.Count == 0)
                    {
                        continue;
                    }

                    m = Math.Min(m, 1.0 + MaxTotalExternalGain);
                    double lost = amount * (1.0 - 1.0 / m), logM = Math.Log(m);
                    lostTotal += lost;
                    foreach ((RaidBuff b, double g) in gains)
                    {
                        double share = logM > 0 ? lost * Math.Log(1.0 + g) / logM / seconds : 0;
                        hitGiven[b.Caster] += share;
                        if (byBuff is not null)
                        {
                            byBuff[(b.Caster, b.DisplayBase)] = byBuff.GetValueOrDefault((b.Caster, b.DisplayBase)) + share;
                        }
                    }
                }

                ndps[p.Id] = own * (hitTotal > 0 ? 1.0 - lostTotal / hitTotal : 1.0);
                continue;
            }

            double total = Math.Min(incoming[p.Id].Aggregate(1.0, (m, g) => m * (1.0 + g.Gain)) - 1.0, MaxTotalExternalGain);
            ndps[p.Id] = own / (1.0 + total);
        }

        var given = participants.ToDictionary(p => p.Id, _ => 0.0);
        foreach ((int id, double dps) in hitGiven)
        {
            given[id] += dps;
        }

        foreach (RaidParticipant p in participants)
        {
            if (perHit.Contains(p.Id))
            {
                continue; // credited hit by hit above
            }

            foreach ((int actor, double gain, int display) in incoming[p.Id])
            {
                if (actor >= 0 && given.ContainsKey(actor))
                {
                    given[actor] += ndps[p.Id] * gain;
                    if (byBuff is not null)
                    {
                        byBuff[(actor, display)] = byBuff.GetValueOrDefault((actor, display)) + ndps[p.Id] * gain;
                    }
                }
            }
        }

        foreach ((int _, int granter, long damage, int display) in moves)
        {
            given[granter] += PerSecond(damage, seconds);
            if (byBuff is not null && given.ContainsKey(granter))
            {
                byBuff[(granter, display)] = byBuff.GetValueOrDefault((granter, display)) + PerSecond(damage, seconds);
            }
        }

        foreach (RaidParticipant p in participants)
        {
            results[p.Id] = new RaidDpsResult(ndps[p.Id], ndps[p.Id] + given[p.Id], given[p.Id]);
        }

        return results;
    }

    private static double PerSecond(long damage, double seconds) => seconds > 0 ? damage / seconds : 0.0;

    private static List<(int Granter, long Damage)> SplitGrant(RaidParticipant p, int source, long damage, HashSet<int> ids, IReadOnlyList<RaidParticipant> others)
    {
        var casters = new List<(int Id, double Weight)>();
        double total = 0;
        foreach (RaidBuff buff in p.Buffs)
        {
            if (buff.DisplayBase == source)
            {
                double weight = Math.Max(buff.UptimePercent, 0.01);
                casters.Add((buff.Caster, weight));
                total += weight;
            }
        }

        var result = new List<(int, long)>();
        if (casters.Count == 0)
        {
            int job = GrantingJobPrefix(source);
            if (job == 0 || p.JobPrefix == job)
            {
                return result;
            }

            foreach (RaidParticipant other in others)
            {
                if (other.Id != p.Id && other.JobPrefix == job)
                {
                    casters.Add((other.Id, 1.0));
                    total += 1.0;
                }
            }
        }

        if (casters.Count == 0 || total <= 0)
        {
            return result;
        }

        long assigned = 0;
        for (int i = 0; i < casters.Count; i++)
        {
            (int id, double weight) = casters[i];
            long share = i == casters.Count - 1 ? damage - assigned : (long)Math.Round(damage * (weight / total), MidpointRounding.AwayFromZero);
            assigned += share;
            if (id != p.Id && ids.Contains(id) && share > 0)
            {
                result.Add((id, share));
            }
        }

        return result;
    }

    /// <summary>The effects on at <paramref name="atMs"/>, one of each non-stacking pair (the same rule as <see cref="Surviving"/>: a fixed winner, the
    /// higher level, the tie winner, else the one on longer).</summary>
    private static List<RaidBuff> ActiveAt(List<RaidBuff> buffs, long atMs)
    {
        var on = new List<RaidBuff>();
        foreach (RaidBuff b in buffs)
        {
            foreach ((long start, long end) in b.Spans)
            {
                if (start <= atMs && atMs <= end)
                {
                    on.Add(b);
                    break;
                }
            }
        }

        if (on.Count < 2)
        {
            return on;
        }

        foreach ((int a, int bb, int fixedWinner, int tieWinner) in ExclusivePairs)
        {
            int ai = on.FindIndex(x => x.DisplayBase == a), bi = on.FindIndex(x => x.DisplayBase == bb);
            if (ai < 0 || bi < 0)
            {
                continue;
            }

            RaidBuff xa = on[ai], xb = on[bi];
            int loser = fixedWinner != 0 ? (fixedWinner == a ? bb : a)
                : xa.Level > 0 && xb.Level > 0 && xa.Level != xb.Level ? (xa.Level > xb.Level ? bb : a)
                : tieWinner != 0 ? (tieWinner == a ? bb : a)
                : xa.UptimePercent >= xb.UptimePercent ? bb : a;
            on.RemoveAt(loser == a ? ai : bi);
        }

        return on;
    }

    private static List<RaidBuff> Surviving(IReadOnlyList<RaidBuff> buffs)
    {
        var kept = buffs.ToList();
        foreach ((int a, int b, int fixedWinner, int tieWinner) in ExclusivePairs)
        {
            int ai = kept.FindIndex(x => x.DisplayBase == a), bi = kept.FindIndex(x => x.DisplayBase == b);
            if (ai < 0 || bi < 0)
            {
                continue;
            }

            RaidBuff ba = kept[ai], bb = kept[bi];
            int loserBase = fixedWinner != 0 ? (fixedWinner == a ? b : a)
                : ba.Level > 0 && bb.Level > 0 && ba.Level != bb.Level ? (ba.Level > bb.Level ? b : a)
                : tieWinner != 0 ? (tieWinner == a ? b : a)
                : ba.UptimePercent >= bb.UptimePercent ? b : a;
            int loserIndex = loserBase == a ? ai : bi;
            RaidBuff loser = kept[loserIndex], winner = loserBase == a ? bb : ba;
            double remaining = Remainder(loser, winner);
            if (remaining <= 0)
            {
                kept.RemoveAt(loserIndex);
            }
            else
            {
                kept[loserIndex] = loser with { UptimePercent = remaining };
            }
        }

        return kept;
    }

    private static double Remainder(RaidBuff loser, RaidBuff winner)
    {
        if (loser.Spans.Count > 0 && winner.Spans.Count > 0)
        {
            long covered = loser.Spans.Sum(s => s.End - s.Start);
            if (covered <= 0)
            {
                return 0;
            }

            long overlap = IntersectionMs(loser.Spans, winner.Spans);
            return loser.UptimePercent * Math.Clamp((covered - overlap) / (double)covered, 0.0, 1.0);
        }

        return Math.Max(0.0, loser.UptimePercent - winner.UptimePercent);
    }

    /// <summary>How long two lists of merged, ordered spans overlap.</summary>
    public static long IntersectionMs(IReadOnlyList<(long Start, long End)> first, IReadOnlyList<(long Start, long End)> second)
    {
        long total = 0;
        int i = 0, j = 0;
        while (i < first.Count && j < second.Count)
        {
            long start = Math.Max(first[i].Start, second[j].Start), end = Math.Min(first[i].End, second[j].End);
            if (end > start)
            {
                total += end - start;
            }

            if (first[i].End < second[j].End)
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return total;
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

    /// <summary>A buff's gain on its recipient: each effect relative to the bucket it goes into, times the uptime; composed.</summary>
    public static double Gain(RaidBuff buff, BuffGainContext context)
    {
        double uptime = Math.Clamp(buff.UptimePercent, 0.0, 100.0) / 100.0;
        if (uptime <= 0 || Effects(buff.DisplayBase, buff.Level) is not { } effects)
        {
            return 0;
        }

        double multiplier = 1.0;
        foreach (BuffGainEffect effect in effects)
        {
            double gain = Math.Clamp(RelativeGain(effect, buff.BossScope, context) * uptime, 0.0, MaxSingleEffectGain);
            if (gain > 0)
            {
                multiplier *= 1.0 + gain;
            }
        }

        return Math.Max(0.0, multiplier - 1.0);
    }

    public static double RelativeGain(BuffGainEffect effect, bool bossScope, BuffGainContext context)
    {
        double value = effect.Value;
        if (effect.Kind == BuffEffectKind.BossResistDown)
        {
            return bossScope && value < 0 ? -value / (100.0 + Math.Max(context.AmpBucketPercent, 0.0)) : 0.0;
        }

        if (value <= 0)
        {
            return 0;
        }

        return effect.Kind switch
        {
            BuffEffectKind.DamageAmp => value / (100.0 + Math.Max(context.AmpBucketPercent, 0.0)),
            BuffEffectKind.AttackRatio => value / (100.0 + Math.Max(context.AttackIncreasePercent, 0.0)),
            BuffEffectKind.SmiteRate => Headroom(value, context.SmiteRate) / context.SmiteMultiplier,
            BuffEffectKind.PerfectRate => Headroom(value, context.PerfectRate) * Math.Max(context.PerfectBonusRatio, 0.0) / context.PerfectMultiplier,
            BuffEffectKind.CritDamageAmp => Math.Clamp(context.CritRate, 0.0, 1.0) * (value / 100.0) / context.CritMultiplier,
            BuffEffectKind.CritRate => Headroom(value, context.CritRate) * (0.5 + Math.Max(context.CritAmpPercent, 0.0) / 100.0) / context.CritMultiplier,
            BuffEffectKind.FrontAmp => Math.Clamp(context.FrontRate, 0.0, 1.0) * (value / 100.0) / context.FrontMultiplier,
            BuffEffectKind.CombatSpeed => value / 100.0,
            _ => 0.0,
        };
    }

    private static double Headroom(double points, double rate) => Math.Max(0.0, Math.Min(points / 100.0, 1.0 - Math.Clamp(rate, 0.0, 1.0)));

    /// <summary>
    /// The party buffs on each of <paramref name="playerIds"/> and the debuffs on <paramref name="bossId"/> during [from, to], from the
    /// buff events: spans per (effect, caster) clipped to the window, the share of the window they cover and the highest level seen.
    /// </summary>
    public static (Dictionary<int, List<RaidBuff>> OnPlayers, List<RaidBuff> OnBoss) BuffsIn(IReadOnlyList<BuffEvent> events, IReadOnlyCollection<int> playerIds,
        int? bossId, DateTime from, DateTime to)
    {
        double windowMs = (to - from).TotalMilliseconds;
        var players = playerIds.ToHashSet();
        var spans = new Dictionary<(int Target, int Base, int Caster), (List<(long, long)> Spans, int Level)>();
        var open = new Dictionary<(int Target, int Instance), (DateTime Start, int Base, int Caster, int Level, DateTime? Expect)>();
        void Close((int Target, int Instance) key, DateTime at)
        {
            if (!open.Remove(key, out var o))
            {
                return;
            }

            long s = (long)Math.Max(0, (o.Start - from).TotalMilliseconds), e = (long)Math.Min(windowMs, (at - from).TotalMilliseconds);
            if (e > s)
            {
                var k = (key.Target, o.Base, o.Caster);
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
                    int display = DisplayBase(ev.SkillId);
                    if (!IsSynergy(display))
                    {
                        break;
                    }

                    if (ev.Kind == BuffKind.Renewed && open.TryGetValue(instanceKey, out var current))
                    {
                        open[instanceKey] = current with { Expect = expectAt, Level = Math.Max(current.Level, ev.Level) };
                    }
                    else
                    {
                        Close(instanceKey, ev.At);
                        open[instanceKey] = (ev.At, display, ev.Caster, ev.Level, expectAt);
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
        foreach (((int target, int display, int caster), (List<(long, long)> list, int level)) in spans)
        {
            List<(long Start, long End)> merged = Merge(list);
            double uptime = windowMs > 0 ? 100.0 * merged.Sum(s => s.End - s.Start) / windowMs : 0;
            bool boss = target == bossId && !players.Contains(target);
            var buff = new RaidBuff(display, caster, uptime, level, boss, merged);
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
