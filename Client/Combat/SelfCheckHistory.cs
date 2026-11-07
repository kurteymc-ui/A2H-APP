using AionDPS.History;

namespace AionDPS.Combat;

/// <summary>Fight segmentation by silence gap (used by the session analysis).</summary>
public static class SelfCheckHistory
{
    public static bool Run()
    {
        bool ok = true;
        ok &= RunSegmenterScenario();
        ok &= SideFights();
        ok &= UpdateNotesCheck();
        return ok;
    }

    private const int You = 1;
    private const int Healer = 2;
    private const int Boss = 100;
    private const int Dummy = 101;

    private static List<DamageEvent> TwoRuns(DateTime start)
    {
        var events = new List<DamageEvent>();
        for (int i = 0; i < 30; i++)
        {
            events.Add(new DamageEvent(start.AddSeconds(i * 2), You, Boss, 1000 + i, IsHeal: false, Skill: "Cleave"));
        }
        events.Add(new DamageEvent(start.AddSeconds(20), Boss, You, 500, IsHeal: false));
        events.Add(new DamageEvent(start.AddSeconds(21), Healer, You, 400, IsHeal: true, Skill: "Healing Light"));
        // Ten minutes later: the same boss again - a separate run.
        DateTime second = start.AddMinutes(10);
        for (int i = 0; i < 20; i++)
        {
            events.Add(new DamageEvent(second.AddSeconds(i * 3), You, Boss, 2000, IsHeal: false));
        }
        // A training dummy poke that must never be recorded.
        events.Add(new DamageEvent(start.AddMinutes(20), You, Dummy, 1, IsHeal: false));
        events.Add(new DamageEvent(start.AddMinutes(20).AddSeconds(30), You, Dummy, 1, IsHeal: false));
        return events;
    }

    private static bool RunSegmenterScenario()
    {
        Console.WriteLine("[selftest] Fight segmentation (120 s silence gap):");
        var start = new DateTime(2026, 9, 22, 20, 0, 0);
        List<FightSegment> segments = FightSegmenter.Segment(TwoRuns(start), Boss);
        bool twoRuns = segments.Count == 2;
        bool firstBounds = twoRuns && segments[0].Start == start && segments[0].End == start.AddSeconds(58) && segments[0].Hits.Count == 30;
        bool secondBounds = twoRuns && segments[1].Start == start.AddMinutes(10) && segments[1].Hits.Count == 20;
        bool healsExcluded = twoRuns && segments[0].Hits.All(h => !h.IsHeal && h.TargetObjectId == Boss);
        Console.WriteLine($"  -> two runs, split at the ten-minute gap: {twoRuns}");
        Console.WriteLine($"  -> first run spans its own hits only: {firstBounds}");
        Console.WriteLine($"  -> second run picked up whole: {secondBounds}");
        Console.WriteLine($"  -> heals and the boss's own hits are not part of a run: {healsExcluded}");
        return twoRuns && firstBounds && secondBounds && healsExcluded;
    }

    /// <summary>The fights list leaves out a dungeon's side fights (Urugugu Canyon's Patrol and Shaman) once it knows the dungeon's bosses; the bosses, the run and the open world stay.</summary>
    private static bool SideFights()
    {
        Console.WriteLine("[selftest] Fights list without a dungeon's side fights:");
        DateTime t = new(2026, 11, 6, 23, 3, 0);
        const int canyon = 600011;
        FightLogEntry E(string title, int? npc, int zone, int minute) =>
            new($"f{minute}", FightLogKind.Boss, title, null, t.AddMinutes(minute), 60, true, 1, 1000, 5000, npc, zone, "");
        var all = new List<FightLogEntry>
        {
            new("run", FightLogKind.Run, "Urugugu Canyon", null, t, 600, true, 5, 1000, 5000, null, canyon, ""),
            E("Urugugu Patrol", null, canyon, 0), E("Urugugu Shaman", null, canyon, 1), E("Guardian Captain Raur", 2300206, canyon, 2), E("Judge Urahum", 2300207, canyon, 4),
            E("Divine Auldor", 2300218, canyon, 6), E("Field elite", null, 1110, 30), E("Unknown dungeon's boss", null, 699998, 40),
        };
        var shown = FightLog.WithoutSideFights(all).Select(e => e.Title).ToList();
        bool sideGone = !shown.Contains("Urugugu Patrol") && !shown.Contains("Urugugu Shaman");
        bool bossesStay = shown.Contains("Guardian Captain Raur") && shown.Contains("Judge Urahum") && shown.Contains("Divine Auldor") && shown.Contains("Urugugu Canyon");
        bool restStays = shown.Contains("Field elite") && shown.Contains("Unknown dungeon's boss");
        Console.WriteLine($"  -> Urugugu Patrol and Shaman left out: {sideGone}");
        Console.WriteLine($"  -> the three bosses and the run stay: {bossesStay}");
        Console.WriteLine($"  -> the open world and a dungeon whose bosses the table doesn't know keep every fight: {restStays}");
        return sideGone && bossesStay && restStays;
    }

    /// <summary>The update notes shipped with the app: readable, newest first, every change with a known kind, a title and a text; the ones between two
    /// versions are those after the first up to the second.</summary>
    private static bool UpdateNotesCheck()
    {
        Console.WriteLine("[selftest] Update notes:");
        var all = AionDPS.Update.UpdateNotes.Bundled();
        var versions = all.Select(r => AionDPS.Update.AppVersion.Parse(r.Version)).ToList();
        bool read = all.Count >= 10;
        bool newestFirst = versions.Zip(versions.Skip(1)).All(p => p.First > p.Second);
        bool items = all.SelectMany(r => r.Items).All(i => i.Kind is "new" or "improved" or "fixed" && i.Title.Length > 0 && i.Text.Length > 0 && i.Area.Length > 0);
        var between = AionDPS.Update.UpdateNotes.Between(all, new Version(1, 20, 53), new Version(1, 20, 56)).Select(r => r.Version).ToList();
        bool range = between.SequenceEqual(new[] { "1.20.56", "1.20.55", "1.20.54" });
        Console.WriteLine($"  -> the notes are read: {read} ({all.Count} versions)");
        Console.WriteLine($"  -> newest first, each version once: {newestFirst}");
        Console.WriteLine($"  -> every change has a kind, an area, a title and a text: {items}");
        Console.WriteLine($"  -> from 1.20.53 to 1.20.56: the three after it: {range} ({string.Join(", ", between)})");
        return read && newestFirst && items && range;
    }
}
