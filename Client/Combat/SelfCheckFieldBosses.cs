using AionDPS.Aion2;

namespace AionDPS.Combat;

/// <summary>
/// World bosses (Aion2/Aion2FieldBosses.cs, 6 November 2026): the game's list of a region read for every region (the Abyss's numbers are below 1000), a list
/// with a second part after the bosses, the game's own time beating the table's schedule, the schedule on the server's clock. The frames are made up in the
/// list's layout.
/// </summary>
public static class SelfCheckFieldBosses
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] World bosses (the game's list of a region, every region, its time first):");
        bool ok = true;

        // every region has its number (the zone's id): Verteron 1010, Eltnen 1011, Altgard 1110, Morheim 1111, Lower Reshanta 20, Middle Reshanta 22
        var ids = Aion2FieldBosses.Maps.ToDictionary(m => m.Key, m => m.Id);
        bool numbered = ids.GetValueOrDefault("World_L_A") == 1010 && ids.GetValueOrDefault("World_L_B") == 1011 && ids.GetValueOrDefault("World_D_A") == 1110
            && ids.GetValueOrDefault("World_D_B") == 1111 && ids.GetValueOrDefault("Abyss_Reshanta_A") == 20 && ids.GetValueOrDefault("Abyss_Reshanta_C") == 22;
        // the game lists Lower Reshanta's Watcher Kaira second, before the three Nahmas (their three equal times in the game's list)
        FieldBossMap? lower = Aion2FieldBosses.Maps.FirstOrDefault(m => m.Key == "Abyss_Reshanta_A");
        bool order = lower is not null && lower.Bosses.Select(b => b.Npc).SequenceEqual(new[] { 2600068, 2600089, 2600084, 2600093, 2600094, 2600096, 2600097, 2600098 });
        Console.WriteLine($"  -> every region has its number, Lower Reshanta's bosses in the game's order: {numbered && order} (numbers {numbered}, order {order})");
        ok &= numbered && order;

        // a list of Lower Reshanta (map 20): one dead with its time back, one with an extra byte before its time, one alive with its place
        long back = DateTimeOffset.UtcNow.AddHours(3).ToUnixTimeMilliseconds();
        var abyss = new List<byte> { 0x01, 0x91, 0x00, 0x00 };
        abyss.AddRange(BitConverter.GetBytes(20u));
        abyss.Add(3);
        abyss.Add(0x00);
        abyss.AddRange(Place(20, 1));
        abyss.AddRange(BitConverter.GetBytes(back));
        abyss.Add(0x00);
        abyss.AddRange(Place(20, 3));
        abyss.Add(0x80);   // the one more byte some places carry
        abyss.AddRange(BitConverter.GetBytes(back));
        abyss.Add(0x01);
        abyss.AddRange(Place(20, 8));
        abyss.AddRange(BitConverter.GetBytes(-17728f));
        abyss.AddRange(BitConverter.GetBytes(-30749f));
        abyss.AddRange(BitConverter.GetBytes(0f));
        abyss.AddRange(BitConverter.GetBytes(DateTimeOffset.UtcNow.AddMinutes(-20).ToUnixTimeMilliseconds()));
        abyss.AddRange(new byte[3]);
        bool lowerRead = Aion2FieldBosses.TryParse(abyss.ToArray(), out int map20, out var slots20) && map20 == 20 && slots20.Count == 3
            && slots20[0] is { Number: 1, Alive: false } && slots20[0].TimeMs == back && slots20[1] is { Number: 3 } && slots20[1].TimeMs == back
            && slots20[2] is { Number: 8, Alive: true } && Math.Abs(slots20[2].X + 17728) < 1;

        // a list of Altgard with a second part after the bosses: the last boss still counts
        var withMore = new List<byte> { 0x01, 0x91, 0x00, 0x00 };
        withMore.AddRange(BitConverter.GetBytes(1110u));
        withMore.Add(2);
        withMore.Add(0x00);
        withMore.AddRange(Place(1110, 1));
        withMore.AddRange(BitConverter.GetBytes(back));
        withMore.Add(0x00);
        withMore.AddRange(Place(1110, 2));
        withMore.AddRange(BitConverter.GetBytes(back + 60_000));
        withMore.AddRange(new byte[] { 0x00, 0x03, 0xf3, 0x92, 0x1d, 0xe6, 0xc8, 0x10, 0x00, 0x4a, 0x4b, 0x4c, 0x00, 0x11, 0x22 });
        bool moreRead = Aion2FieldBosses.TryParse(withMore.ToArray(), out int map1110, out var slots1110) && map1110 == 1110 && slots1110.Count == 2 && slots1110[1].TimeMs == back + 60_000;
        Console.WriteLine($"  -> a list of the Abyss (number 20) and one with a second part after the bosses are read: {lowerRead && moreRead} (Abyss {lowerRead}, second part {moreRead})");
        ok &= lowerRead && moreRead;

        // the game's own time beats the table: Lower Reshanta's first Nahma dead with its time back in the list shows that time, not the schedule's
        if (lower is not null)
        {
            DateTime now = DateTime.Now;
            FieldBoss nahma = lower.Bosses[2];
            long nahmaBack = DateTimeOffset.UtcNow.AddMinutes(95).ToUnixTimeMilliseconds();
            Aion2FieldBosses.Update(20, new[] { new Aion2FieldBosses.Slot(3, false, 0, 0, nahmaBack) });
            BossStatus status = Aion2FieldBosses.StatusOf(nahma, now);
            bool listFirst = status.Phase == BossPhase.Fixed && status.At is DateTime at && Math.Abs((at - DateTimeOffset.FromUnixTimeMilliseconds(nahmaBack).LocalDateTime).TotalSeconds) < 1
                && Aion2FieldBosses.ReadAt(lower) is not null;

            // the schedule is on the server's clock: 21:05 in Berlin is 21:05 there, whatever the PC's own zone
            var rule = new FixedBossRule("weekly", "00:00", 0, new[] { 0, 1, 2, 3, 4, 5, 6 }, "21:05", 20);
            TimeZoneInfo zone = Aion2FieldBosses.RuleZone;
            bool clock = Aion2FieldBosses.FixedWindow(rule, now) is { } window
                && (ReferenceEquals(zone, TimeZoneInfo.Local) ? window.Start : TimeZoneInfo.ConvertTimeFromUtc(window.Start.ToUniversalTime(), zone)).TimeOfDay == new TimeSpan(21, 5, 0);
            Console.WriteLine($"  -> the game's time beats the schedule, the schedule runs on the server's clock ({zone.Id}): {listFirst && clock} (list first {listFirst}, clock {clock})");
            ok &= listFirst && clock;
        }

        return ok;
    }

    /// <summary>A place of the list: map x 100 + n as a varint.</summary>
    private static IEnumerable<byte> Place(int map, int n)
    {
        long value = ((long)map * 100) + n;
        var bytes = new List<byte>();
        do
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            bytes.Add(value > 0 ? (byte)(b | 0x80) : b);
        }
        while (value > 0);
        return bytes;
    }
}
