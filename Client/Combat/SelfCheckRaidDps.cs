namespace AionDPS.Combat;

/// <summary>rDPS (RaidDps): the maths against a worked example, the buff spans from buff events, and the pairs that don't stack.</summary>
public static class SelfCheckRaidDps
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] rDPS:");
        const int dps = 1, chanter = 2, cleric = 3;
        var mantra25 = new RaidBuff(RaidDps.ChanterMantra, chanter, 100, 25, false, new List<(long, long)> { (0, 60_000) });

        // Mantra at level 25, on the whole fight: +22.5 % amplification over a 174.9 % bucket, +5 % crit damage, +5 % smite, +10 % perfect,
        // each against the default character: 1.0818 × 1.0204 × 1.0341 × 1.0076 − 1 = 15.02 %.
        double gain = RaidDps.Gain(mantra25, BuffGainContext.Default);
        bool gainOk = Math.Abs(gain - 0.1502) < 0.0015;
        Console.WriteLine($"  -> Mantra L25 for the whole fight is worth {gain:P2} to its recipient (expected ~15.02 %): {gainOk}");

        var result = RaidDps.Compute(new[]
        {
            new RaidParticipant(dps, 10_000, new[] { mantra25 }, new Dictionary<int, long>(), 13),
            new RaidParticipant(chanter, 3_000, Array.Empty<RaidBuff>(), new Dictionary<int, long>(), 18),
        }, Array.Empty<RaidBuff>(), 60);
        double ndps = 10_000 / (1 + gain), rdpsChanter = 3_000 + ndps * gain;
        bool creditOk = Math.Abs(result[dps].Rdps - ndps) < 1 && Math.Abs(result[chanter].Rdps - rdpsChanter) < 1 && Math.Abs(result[chanter].Given - ndps * gain) < 1;
        Console.WriteLine($"  -> the DPS keeps {result[dps].Rdps:0} (nDPS), the Chanter is credited {result[chanter].Given:0}: {creditOk}");

        // Protective Light and Mantra don't stack: at equal levels Mantra wins, the Cleric's light counts only where Mantra was off.
        var light = new RaidBuff(RaidDps.ClericProtectLight, cleric, 100, 25, false, new List<(long, long)> { (0, 60_000) });
        var both = RaidDps.Compute(new[]
        {
            new RaidParticipant(dps, 10_000, new[] { mantra25, light }, new Dictionary<int, long>(), 13),
            new RaidParticipant(chanter, 3_000, Array.Empty<RaidBuff>(), new Dictionary<int, long>(), 18),
            new RaidParticipant(cleric, 2_000, Array.Empty<RaidBuff>(), new Dictionary<int, long>(), 17),
        }, Array.Empty<RaidBuff>(), 60);
        bool pairOk = both[cleric].Given < 1 && Math.Abs(both[chanter].Given - ndps * gain) < 1;
        Console.WriteLine($"  -> overlapping Protective Light and Mantra: only the Mantra counts: {pairOk}");

        // The spans: an application at 10 s for 20 s on the DPS, renewed at 25 s for 20 s more, cast at level 22: on from 10 s to 45 s
        // of a 60 s window (58.3 %), at level 22; an effect that isn't a party buff is left out.
        DateTime t0 = new(2026, 10, 31, 12, 0, 0);
        int skill = RaidDps.ChanterMantra / 10_000 * 100_000 + 51; // a rank code of the Mantra family
        var events = new List<BuffEvent>
        {
            new(t0.AddSeconds(10), BuffKind.On, dps, 7, skill, chanter, 20_000, 22),
            new(t0.AddSeconds(25), BuffKind.Renewed, dps, 7, skill, chanter, 20_000, 22),
            new(t0.AddSeconds(12), BuffKind.On, dps, 8, 2_000_123, dps, 30_000, 0),
        };
        var (onPlayers, _) = RaidDps.BuffsIn(events, new[] { dps }, null, t0, t0.AddSeconds(60));
        RaidBuff? read = onPlayers.TryGetValue(dps, out var list) && list.Count == 1 ? list[0] : null;
        bool spansOk = read is { } b && b.DisplayBase == RaidDps.ChanterMantra && b.Caster == chanter && b.Level == 22 && Math.Abs(b.UptimePercent - 58.33) < 0.1;
        Console.WriteLine($"  -> buff events become one span of the Mantra from the Chanter at level 22, {read?.UptimePercent:0.0} % of the fight: {spansOk}");

        // the 8-digit codes the buff frames carry for a class effect on the party (a real fight: Fury on the others 12780001, the counter 11780007)
        bool eightOk = RaidDps.DisplayBase(12_780_001) == RaidDps.GuardianFervor && RaidDps.DisplayBase(11_780_007) == RaidDps.SwordCounter
            && RaidDps.DisplayBase(18_190_003) == RaidDps.ChanterMantra && RaidDps.DisplayBase(RaidDps.ClericEarthBlessing) == RaidDps.ClericEarthBlessing
            && RaidDps.IsSynergy(RaidDps.DisplayBase(12_780_001))
            // the Cleric's Earth's Blessing on the party as the buff frames carry it (Transcendent Bakarma, 4 Nov 2026: 17400038, 30 times; Earth Punishment's mark on the boss, 17400040, stays apart)
            && RaidDps.DisplayBase(17_400_038) == RaidDps.ClericEarthBlessing && RaidDps.DisplayBase(17_400_027) == RaidDps.ClericEarthBlessing
            && RaidDps.DisplayBase(17_400_040) != RaidDps.ClericEarthBlessing;
        var furyEvents = new List<BuffEvent> { new(t0.AddSeconds(5), BuffKind.On, dps, 9, 12_780_001, chanter, 30_000, 15) };
        var (furyOn, _) = RaidDps.BuffsIn(furyEvents, new[] { dps, chanter }, null, t0, t0.AddSeconds(60));
        bool furyRead = furyOn.TryGetValue(dps, out var fl) && fl.Count == 1 && fl[0].DisplayBase == RaidDps.GuardianFervor;
        var blessingEvents = new List<BuffEvent> { new(t0.AddSeconds(5), BuffKind.On, dps, 10, 17_400_038, chanter, 20_000, 20) };
        var (blessOn, _) = RaidDps.BuffsIn(blessingEvents, new[] { dps, chanter }, null, t0, t0.AddSeconds(60));
        bool blessRead = blessOn.TryGetValue(dps, out var bl) && bl.Count == 1 && bl[0].DisplayBase == RaidDps.ClericEarthBlessing && bl[0].Level == 20
            && RaidDps.Gain(bl[0], BuffGainContext.Default) > 0;
        furyRead &= blessRead;
        Console.WriteLine($"  -> 8-digit party effect codes fold to their family (Fury 12780001, counter 11780007, Earth's Blessing 17400038): {eightOk}, Fury and Earth's Blessing on another player are read and priced: {furyRead}");

        // hit by hit: Mantra L25 on the first half of a 60 s fight only; ten 6,000 hits inside it and ten outside. Only the first ten are buffed: each
        // gives 6,000 × (1 - 1/1.1502) to the Chanter; the uptime estimate would have charged all twenty hits half as much.
        var halfMantra = mantra25 with { UptimePercent = 50, Spans = new List<(long, long)> { (0, 30_000) } };
        var hits = Enumerable.Range(0, 10).Select(i => ((long)(1_000 + i * 2_000), 6_000L)).Concat(Enumerable.Range(0, 10).Select(i => ((long)(31_000 + i * 2_000), 6_000L))).ToList();
        var perHit = RaidDps.Compute(new[]
        {
            new RaidParticipant(dps, 2_000, new[] { halfMantra }, new Dictionary<int, long>(), 13, hits),
            new RaidParticipant(chanter, 1_000, Array.Empty<RaidBuff>(), new Dictionary<int, long>(), 18, new List<(long, long)>()),
        }, Array.Empty<RaidBuff>(), 60);
        double expectGiven = 10 * 6_000 * (1 - 1 / (1 + gain)) / 60;
        bool hitOk = Math.Abs(perHit[chanter].Given - expectGiven) < 1 && Math.Abs(perHit[dps].Ndps - (2_000 - expectGiven)) < 1;
        Console.WriteLine($"  -> hit by hit, Mantra on half the fight credits the Chanter {perHit[chanter].Given:0.0} DPS (expected {expectGiven:0.0}), the DPS keeps {perHit[dps].Ndps:0.0}: {hitOk}");

        // hit by hit, the Chanter's own hits under his own Mantra (L25) and the Cleric's Light (L25) at once: the two do not stack and the Mantra wins at equal
        // levels, so the Cleric gets nothing for them (the own Mantra used to be left out before the pair was settled, and the Cleric's Light "won");
        // the DPS's hits under both give the Mantra's share to the Chanter and nothing to the Cleric either
        var chanterHits = Enumerable.Range(0, 10).Select(i => ((long)(1_000 + i * 2_000), 6_000L)).ToList();
        var ownMantra = RaidDps.Compute(new[]
        {
            new RaidParticipant(chanter, 1_000, new[] { mantra25, light }, new Dictionary<int, long>(), 18, chanterHits),
            new RaidParticipant(cleric, 500, Array.Empty<RaidBuff>(), new Dictionary<int, long>(), 17, new List<(long, long)>()),
            new RaidParticipant(dps, 2_000, new[] { mantra25, light }, new Dictionary<int, long>(), 13, chanterHits),
        }, Array.Empty<RaidBuff>(), 60);
        bool ownWins = ownMantra[cleric].Given < 0.01 && Math.Abs(ownMantra[chanter].Ndps - 1_000) < 0.01 && Math.Abs(ownMantra[chanter].Given - expectGiven) < 1;
        Console.WriteLine($"  -> hit by hit, the Chanter's own Mantra wins over the Cleric's Light on his own hits: the Cleric gets {ownMantra[cleric].Given:0.0}, the Chanter keeps his hits and is credited {ownMantra[chanter].Given:0.0} for the DPS's: {ownWins}");

        return gainOk && creditOk && pairOk && spansOk && eightOk && furyRead && hitOk && ownWins;
    }
}
