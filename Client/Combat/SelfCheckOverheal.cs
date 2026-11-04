using System.Buffers.Binary;
using AionDPS.Aion2;
using AionDPS.Aion2.Capture;
using AionDPS.Aion2.Protocol;
using AionDPS.Combat.Sources;

namespace AionDPS.Combat;

/// <summary>
/// Overheal: a heal's starting hit points come from the target's last HP frame (0x008d), two heals in one burst count one after the other, and the report
/// takes what went past the maximum as overheal.
/// </summary>
public static class SelfCheckOverheal
{
    private const int Member = 4697;

    public static bool Run()
    {
        Console.WriteLine("[selftest] Overheal:");

        // A real Recuperation hit of 308 on a party member (the frame of SelfCheckAion2's tick capture), around synthetic HP frames of that member.
        const string recuperation = "0438D9240400833A407D140105020BED006C010000008256B4020100";
        var wire = new List<byte>();
        Add(wire, Hp(Member, 1_000, 1_200));       // 1,000 of 1,200
        Add(wire, Convert.FromHexString(recuperation)); // 200 missing: 108 over
        Add(wire, Convert.FromHexString(recuperation)); // same burst, already full: all 308 over
        Add(wire, Hp(Member, 1_200, null));
        Add(wire, Hp(Member, 500, null));          // hit hard
        Add(wire, Convert.FromHexString(recuperation)); // 700 missing: nothing over

        using var source = new Aion2PacketCombatSource(Aion2Protocol.Load());
        source.Ingest(new TcpSegment(new DateTime(2026, 11, 2, 20, 0, 0, DateTimeKind.Utc), "10.0.0.1:7777", "192.168.0.2:50000", 9000, wire.ToArray(), FromServer: true));
        CombatBatch batch = source.Poll(false);
        var heals = batch.Damage.Where(e => e.IsHeal && e.TargetObjectId == Member).ToList();
        bool read = heals.Count == 3 && heals[0].HpBefore == 1_000 && heals[0].HpMax == 1_200 && heals[1].HpBefore == 1_308 && heals[2].HpBefore == 500;
        Console.WriteLine($"  -> the heals start from {string.Join(", ", heals.Select(h => h.HpBefore))} of {heals.FirstOrDefault().HpMax} (expected 1000, 1308, 500 of 1200): {read}");

        Func<DamageEvent, long> overOf = FightReport.OverhealOf(heals);
        long[] over = heals.Select(overOf).ToArray();
        bool overOk = over.SequenceEqual(new long[] { 108, 308, 0 });
        Console.WriteLine($"  -> overheal {string.Join(", ", over)} (expected 108, 308, 0): {overOk}");

        // The report: the healer's line and skill carry it; a heal without hit points (an older fight) counts as none.
        DateTime start = heals.Count > 0 ? heals[0].Timestamp.AddSeconds(-1) : DateTime.Now;
        var events = heals.Append(new DamageEvent(start.AddSeconds(0.5), 7427, Member, 100, IsHeal: true, "Recuperation", SkillId: 18120000)).ToList();
        var input = new ReportInput("Test", start, start.AddSeconds(10), new[] { new ReportPlayer(7427, "Healer", "Chanter"), new ReportPlayer(Member, "Member", "Templar") },
            events, Array.Empty<DamageEvent>(), Array.Empty<AvoidEvent>(), FightExtras.Empty);
        ReportPlayerLine? healer = FightReport.Build(input).Players.FirstOrDefault(p => p.Player.Id == 7427);
        bool reportOk = healer is { Healing: 1_024, Overheal: 416, HealChecked: 924 } && healer.Heals.FirstOrDefault()?.Overheal == 416;
        Console.WriteLine($"  -> the report: {healer?.Healing} healed, {healer?.Overheal} over, {healer?.HealChecked} checked (expected 1024, 416, 924): {reportOk}");
        return read && overOk && reportOk;
    }

    private static byte[] Hp(int entity, long current, long? max)
    {
        var frame = new List<byte> { 0x00, 0x8d };
        for (uint v = (uint)entity; ; v >>= 7)
        {
            if (v < 0x80)
            {
                frame.Add((byte)v);
                break;
            }

            frame.Add((byte)(v & 0x7f | 0x80));
        }

        frame.Add(0x02); // format: the 8-byte group only
        frame.Add((byte)(max is null ? 1 : 2));
        var value = new byte[8];
        frame.Add(0);
        BinaryPrimitives.WriteInt64LittleEndian(value, current);
        frame.AddRange(value);
        if (max is long m)
        {
            frame.Add(7);
            BinaryPrimitives.WriteInt64LittleEndian(value, m);
            frame.AddRange(value);
        }

        return frame.ToArray();
    }

    private static void Add(List<byte> wire, byte[] body)
    {
        int length = body.Length + 4;
        while (length >= 0x80)
        {
            wire.Add((byte)(length & 0x7f | 0x80));
            length >>= 7;
        }

        wire.Add((byte)length);
        wire.AddRange(body);
    }
}
