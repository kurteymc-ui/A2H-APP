namespace AionDPS.Combat;

/// <summary>rDPS (RaidDps): the split of a hit among the effects on it (the player's own take their share), the spans read from buff events, and the
/// values of the effects as the meter has them.</summary>
public static class SelfCheckRaidDps
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] rDPS:");
        const int dps = 1, chanter = 2, templar = 3;
        bool valuesOk = RaidDpsValues.Of(18_190_041) > 0.1 && RaidDpsValues.Of(12_780_001) > 0.05 && RaidDpsValues.Of(17_070_240) > 0.02
            && RaidDpsValues.Of(18_250_011) == 0 && RaidDpsValues.Of(0) == 0;
        Console.WriteLine($"  -> the effects' values are read (the Chanter's Mantra {RaidDpsValues.Of(18_190_041):P1}, the Templar's Fury {RaidDpsValues.Of(12_780_001):P1}, " +
            $"attack speed nothing): {valuesOk}");

        // One hit of 10,000 under the Chanter's Mantra (v1), the Templar's Fury (v2) and the player's own effect (v3), all on the whole 60 s: the hit
        // would have done 10,000 / ((1+v1)(1+v2)(1+v3)) without them; the difference goes to the three in proportion to log(1 + v), the own share
        // stays with the player.
        var window = new List<(long, long)> { (0, 60_000) };
        RaidBuff Effect(int code, int caster) => new(RaidDps.DisplayBase(code), caster, 100, 20, false, window, code);
        double v1 = RaidDpsValues.Of(18_190_041), v2 = RaidDpsValues.Of(12_780_001), v3 = RaidDpsValues.Of(13_270_037);
        var hits = new List<(long, long)> { (30_000, 10_000) };
        var result = RaidDps.Compute(new[]
        {
            new RaidParticipant(dps, 10_000 / 60.0, new[] { Effect(18_190_041, chanter), Effect(12_780_001, templar), Effect(13_270_037, dps) }, hits),
            new RaidParticipant(chanter, 0, Array.Empty<RaidBuff>(), new List<(long, long)>()),
            new RaidParticipant(templar, 0, Array.Empty<RaidBuff>(), new List<(long, long)>()),
        }, Array.Empty<RaidBuff>(), 60);
        double l1 = Math.Log(1 + v1), l2 = Math.Log(1 + v2), l3 = Math.Log(1 + v3), added = 10_000 * (1 - 1 / ((1 + v1) * (1 + v2) * (1 + v3)));
        double toChanter = added * l1 / (l1 + l2 + l3) / 60, toTemplar = added * l2 / (l1 + l2 + l3) / 60;
        bool splitOk = v3 > 0 && Math.Abs(result[chanter].Given - toChanter) < 0.01 && Math.Abs(result[templar].Given - toTemplar) < 0.01
            && Math.Abs(result[dps].Ndps - (10_000 / 60.0 - toChanter - toTemplar)) < 0.01 && Math.Abs(result[chanter].Rdps - toChanter) < 0.01;
        Console.WriteLine($"  -> a hit under two party effects and an own one: the Chanter gets {result[chanter].Given:0.00} DPS (expected {toChanter:0.00}), the Templar " +
            $"{result[templar].Given:0.00} ({toTemplar:0.00}), the player keeps his own share: {splitOk}");

        // An effect on the boss counts on every player's hits; a hit at the very moment an effect lands was struck before it.
        double v4 = RaidDpsValues.Of(17_070_240);
        var bossDebuff = new RaidBuff(RaidDps.DisplayBase(17_070_240), templar, 50, 15, true, new List<(long, long)> { (10_000, 40_000) }, 17_070_240);
        var onBoss = RaidDps.Compute(new[]
        {
            new RaidParticipant(dps, 30_000 / 60.0, Array.Empty<RaidBuff>(), new List<(long, long)> { (10_000, 10_000), (20_000, 10_000), (50_000, 10_000) }),
            new RaidParticipant(templar, 0, Array.Empty<RaidBuff>(), new List<(long, long)>()),
        }, new[] { bossDebuff }, 60);
        double expect = 10_000 * (1 - 1 / (1 + v4)) / 60;
        bool bossOk = Math.Abs(onBoss[templar].Given - expect) < 0.01;
        Console.WriteLine($"  -> an effect on the boss is credited on the one hit inside it ({onBoss[templar].Given:0.00} DPS, expected {expect:0.00}): {bossOk}");

        // The spans: an application at 10 s for 20 s on the player, renewed at 25 s for 20 s more, at level 20: on from 10 s to 45 s of a 60 s window;
        // the slot then taken over by another effect at 40 s (the game hands it the instance) ends it there; an effect worth nothing is left out.
        DateTime t0 = new(2026, 11, 5, 12, 0, 0);
        var events = new List<BuffEvent>
        {
            new(t0.AddSeconds(10), BuffKind.On, dps, 7, 18_190_041, chanter, 20_000, 20),
            new(t0.AddSeconds(25), BuffKind.Renewed, dps, 7, 18_190_041, chanter, 20_000, 20),
            new(t0.AddSeconds(40), BuffKind.Renewed, dps, 7, 12_780_001, templar, 20_000, 20),
            new(t0.AddSeconds(12), BuffKind.On, dps, 8, 18_250_011, chanter, 30_000, 20),
        };
        var (onPlayers, _) = RaidDps.BuffsIn(events, new[] { dps, chanter, templar }, null, t0, t0.AddSeconds(60));
        var read = onPlayers.GetValueOrDefault(dps) ?? new List<RaidBuff>();
        RaidBuff? mantra = read.Where(b => b.EffectId == 18_190_041).Cast<RaidBuff?>().FirstOrDefault();
        RaidBuff? fury = read.Where(b => b.EffectId == 12_780_001).Cast<RaidBuff?>().FirstOrDefault();
        bool spansOk = read.Count == 2 && mantra is { } m && m.Caster == chanter && m.Level == 20 && m.Spans.Count == 1 && m.Spans[0] == (10_000, 40_000)
            && fury is { } f && f.Caster == templar && f.Spans[0] == (40_000, 60_000) && m.DisplayBase == RaidDps.ChanterMantra;
        Console.WriteLine($"  -> buff events become spans per effect and caster, a slot taken over ends the first effect: {spansOk}");

        // the 8-digit codes the buff frames carry for a class effect on the party fold to their family (names, the support tab)
        bool eightOk = RaidDps.DisplayBase(12_780_001) == RaidDps.GuardianFervor && RaidDps.DisplayBase(11_780_007) == RaidDps.SwordCounter
            && RaidDps.DisplayBase(18_190_003) == RaidDps.ChanterMantra && RaidDps.DisplayBase(17_400_038) == RaidDps.ClericEarthBlessing
            && RaidDps.IsSynergy(RaidDps.DisplayBase(12_780_001));
        Console.WriteLine($"  -> 8-digit party effect codes fold to their family: {eightOk}");

        return valuesOk && splitOk && bossOk && spansOk && eightOk;
    }
}
