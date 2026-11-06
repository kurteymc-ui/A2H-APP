using System.Globalization;
using System.Windows.Media;
using AionDPS.History;

namespace AionDPS.Ui;

/// <summary>
/// How a fight's rank is shown everywhere in the app (History, the note after a fight, the report's Rank column): its number and the website's colours for it,
/// from grey to gold, the same as on the fight pages and the characters' Logs tab.
/// </summary>
internal static class RankTier
{
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

    private static readonly (double From, Color Colour)[] Tiers =
    {
        (100, Color.FromRgb(0xE5, 0xCC, 0x80)), (99, Color.FromRgb(0xE2, 0x68, 0xA8)), (95, Color.FromRgb(0xFF, 0x9A, 0x2E)), (75, Color.FromRgb(0xB7, 0x7C, 0xF2)),
        (50, Color.FromRgb(0x4D, 0x9C, 0xFF)), (25, Color.FromRgb(0x4F, 0xC1, 0x7A)), (double.NegativeInfinity, Color.FromRgb(0x9D, 0x9D, 0x9D)),
    };

    private static readonly Dictionary<Color, SolidColorBrush> Cache = new();

    /// <summary>The colour of a percentile (the website's tiers: 100, 99, 95, 75, 50, 25, below).</summary>
    public static Color Colour(double percent) => Tiers.FirstOrDefault(t => percent >= t.From, Tiers[^1]).Colour;

    /// <summary>The colour as a brush (frozen, shared).</summary>
    public static SolidColorBrush Brush(double percent)
    {
        Color c = Colour(percent);
        lock (Cache)
        {
            if (!Cache.TryGetValue(c, out SolidColorBrush? b))
            {
                b = new SolidColorBrush(c);
                b.Freeze();
                Cache[c] = b;
            }

            return b;
        }
    }

    /// <summary>The same colour, faint, for a box behind the number.</summary>
    public static SolidColorBrush Back(double percent)
    {
        Color c = Colour(percent);
        var b = new SolidColorBrush(Color.FromArgb(0x26, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    /// <summary>The number shown: the percentile rounded down (99.6 is 99; only the very best is 100).</summary>
    public static string Number(double percent) => Math.Floor(Math.Clamp(percent, 0, 100)).ToString("0", En);

    /// <summary>"78th", "1st", "92nd", "13th".</summary>
    public static string Ordinal(int n)
    {
        string end = (n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return n.ToString(En) + end;
    }

    /// <summary>"78th percentile".</summary>
    public static string Percentile(double percent) => Ordinal((int)Math.Floor(Math.Clamp(percent, 0, 100))) + " percentile";

    /// <summary>The Combat Power bracket, "50k–60k CP " (with the space), or nothing when ranked over all.</summary>
    public static string Bracket(int? low, int? high) =>
        low is int lo ? $"{lo / 1000}k–{(high ?? lo + 10_000) / 1000}k CP " : "";

    /// <summary>A class name as more than one ("Templars"); "players" when unknown.</summary>
    public static string Many(string? className) => className is { Length: > 0 } c ? c + "s" : "players";

    /// <summary>The whole of it, for a tooltip: "Better than 78.4% of 50k–60k CP Templars on Ashen Warden · rank 12 of 214".</summary>
    public static string Describe(FightRanks.Own rank, string? className, string boss) =>
        $"Better than {rank.Percent.ToString("0.#", En)}% of {Bracket(rank.Low, rank.High)}{Many(className)} on {boss} · rank {rank.Place.ToString("N0", En)} of {rank.Of.ToString("N0", En)}";

    /// <summary>Why a fight on the website has no rank to show (few logs of the class, or none for you).</summary>
    public static string Why(FightRanks.Own rank, string? className, string boss) => rank.Of > 0
        ? $"Not enough data yet: a rank shows once {rank.Min} or more {Many(className)} have a log on {boss} ({rank.Of} so far)."
        : "The website has no rank for you in this fight.";
}
