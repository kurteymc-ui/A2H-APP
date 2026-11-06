using System.IO;
using AionDPS.History;
using AionDPS.Ui;

namespace AionDPS.Combat;

/// <summary>Fight percentiles (History/FightRanks.cs, Ui/RankTier.cs, 6 November 2026): the website's colours for a rank, its number and words, when a kept
/// rank is asked again, and that ranks survive a restart.</summary>
public static class SelfCheckRanks
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Fight percentiles (the website's colours, the words, when a rank is asked again, kept on this PC):");
        bool ok = true;

        // the colours, as the fight pages draw them: 100 gold, 99 pink, 95 orange, 75 purple, 50 blue, 25 green, below grey
        var cases = new (double Pct, string Hex)[]
        {
            (100, "E5CC80"), (99.5, "E268A8"), (99, "E268A8"), (95, "FF9A2E"), (94.9, "B77CF2"), (75, "B77CF2"), (74.99, "4D9CFF"), (50, "4D9CFF"),
            (25, "4FC17A"), (24.9, "9D9D9D"), (0, "9D9D9D"), (double.NaN, "9D9D9D"),
        };
        var wrong = cases.Where(c => { var k = RankTier.Colour(c.Pct); return $"{k.R:X2}{k.G:X2}{k.B:X2}" != c.Hex; }).Select(c => c.Pct.ToString()).ToList();
        Console.WriteLine($"  -> tiers as on the website: {wrong.Count == 0}{(wrong.Count > 0 ? " (wrong at " + string.Join(", ", wrong) + ")" : "")}");
        ok &= wrong.Count == 0;

        bool numbers = RankTier.Number(99.6) == "99" && RankTier.Number(100) == "100" && RankTier.Number(0.4) == "0" && RankTier.Number(78.2) == "78";
        bool ordinals = string.Join(",", new[] { 1, 2, 3, 4, 11, 12, 13, 21, 22, 78, 101, 111, 112 }.Select(RankTier.Ordinal)) == "1st,2nd,3rd,4th,11th,12th,13th,21st,22nd,78th,101st,111th,112th";
        bool words = RankTier.Bracket(50_000, 60_000) == "50k–60k CP " && RankTier.Bracket(null, null) == ""
            && RankTier.Describe(new FightRanks.Own(78.24, 12, 214, 10, 50_000, 60_000, DateTime.UtcNow), "Templar", "Ashen Warden")
               == "Better than 78.2% of 50k–60k CP Templars on Ashen Warden · rank 12 of 214"
            && RankTier.Percentile(78.9) == "78th percentile";
        Console.WriteLine($"  -> the number rounded down, ordinals, the tooltip's words: {numbers && ordinals && words} (numbers {numbers}, ordinals {ordinals}, words {words})");
        ok &= numbers && ordinals && words;

        // asked again: none yet at once; a real rank once a day for two weeks after the fight; one with too few logs every six hours for a month
        DateTime now = DateTime.UtcNow;
        FightRanks.Own Ranked(double hoursAgo) => new(80, 5, 100, 10, null, null, now.AddHours(-hoursAgo));
        FightRanks.Own Thin(double hoursAgo) => new(0, 0, 4, 10, null, null, now.AddHours(-hoursAgo));
        bool stale = FightRanks.Stale(null, now.AddDays(-400), now)
            && !FightRanks.Stale(Ranked(2), now.AddDays(-1), now) && FightRanks.Stale(Ranked(25), now.AddDays(-3), now)
            && !FightRanks.Stale(Ranked(25), now.AddDays(-20), now)
            && !FightRanks.Stale(Thin(2), now.AddDays(-1), now) && FightRanks.Stale(Thin(7), now.AddDays(-2), now) && !FightRanks.Stale(Thin(7), now.AddDays(-40), now);
        bool meaning = Ranked(0).Ranked && !Thin(0).Ranked && !new FightRanks.Own(0, 0, 0, 10, null, null, now).Ranked;
        Console.WriteLine($"  -> asked again when old, a thin rank more often, a rank with few logs means nothing: {stale && meaning} (stale {stale}, meaning {meaning})");
        ok &= stale && meaning;

        // kept on this PC: a rank seeded is there after the file is read again
        string before = FightLog.Folder;
        bool offline = FightRanks.Offline;
        string folder = Path.Combine(Path.GetTempPath(), "a2h-selftest-ranks-" + Environment.ProcessId);
        try
        {
            FightLog.Folder = folder;
            FightRanks.Offline = true;
            FightRanks.Reload();
            const string url = "https://atreia.app/meter/logs/abcde12345";
            FightRanks.Seed(url, new FightRanks.Own(96.4, 3, 150, 10, 40_000, 50_000, now));
            FightRanks.Reload();
            bool kept = FightRanks.Get(url) is { Percent: 96.4, Place: 3, Of: 150, Low: 40_000, Ranked: true };
            var entry = new FightLogEntry("x", FightLogKind.Boss, "Ashen Warden", null, DateTime.Now, 90, true, 0, 1000, 5000, 1, 1, "x.json.gz", "Aerin", "Templar", url);
            bool byEntry = FightRanks.Of(entry) is { Place: 3 } && FightRanks.Of(entry with { Url = null }) is null;
            Console.WriteLine($"  -> kept on this PC by the fight's page: {kept && byEntry} (kept {kept}, by entry {byEntry})");
            ok &= kept && byEntry;
        }
        finally
        {
            FightLog.Folder = before;
            FightRanks.Offline = offline;
            FightRanks.Reload();
            try
            {
                Directory.Delete(folder, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // left for the system to clean
            }
        }

        return ok;
    }
}
