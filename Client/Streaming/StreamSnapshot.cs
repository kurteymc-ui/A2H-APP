using System.Text.Json;

namespace AionDPS.Streaming;

/// <summary>
/// One line of the stream meter: a player with the figures the current view shows (the rate first), its class colour, and the ends of its bar's parts as the
/// meter over the game draws them (0-1 of the line: the own part, then the buffs given to the others in grey, then the healing on the others in green) with
/// their "+" figures.
/// </summary>
public sealed record StreamRowData(int Rank, string Name, string ClassName, double Bar, bool IsSelf, IReadOnlyList<string> Cells, string? Sub = null,
    string? Color = null, double Given = 0, double Heal = 0, string? PlusGiven = null, string? PlusHeal = null);

/// <summary>The meter widget: the view's name ("DPS", "rDPS", "HPS"), the fight's time, the group's total and its unit, the column titles and the lines.</summary>
public sealed record StreamMeter(string Mode, string Time, string Total, string TotalUnit, IReadOnlyList<string> Headers, IReadOnlyList<StreamRowData> Rows, int MaxRows = 10);

/// <summary>The boss being fought: its name, what is left of its health in percent and as "3.14M / 12.6M", when it dies and its enrage ("1:51"; null when not
/// known), whether the enrage comes first and whether it is close.</summary>
public sealed record StreamBoss(string Name, double Percent, string Text, string? Kill = null, bool KillLate = false, string? Rage = null, bool RageSoon = false);

/// <summary>
/// The streamer's own character for the card widget: name, class and its colour, level, server, legion, Item Level, Combat Power, the portrait's address on the
/// server (null without one), and this stream so far: how long it runs, the bosses killed, the best DPS on a boss and where, the Combat Power gained.
/// </summary>
public sealed record StreamMe(string Name, string ClassName, string Color, int Level, string Server, string? Legion, int? ItemLevel, int? Cp, string? Portrait,
    string? Live, int Bosses, string? Best, string? BestBoss, int? CpGain);

/// <summary>The state the browser sources draw (see <see cref="StreamOverlayPage"/>), as the JSON text the server sends; every widget reads its part of it.
/// Pure, so a test can check it.</summary>
public static class StreamSnapshot
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string ToJson(StreamMeter meter, StreamBoss? boss, StreamMe? me) =>
        JsonSerializer.Serialize(new
        {
            v = 2,
            meter = new
            {
                mode = meter.Mode,
                time = meter.Time,
                total = meter.Total,
                unit = meter.TotalUnit,
                headers = meter.Headers,
                maxRows = Math.Clamp(meter.MaxRows, 1, 24),
                rows = meter.Rows.Take(Math.Clamp(meter.MaxRows, 1, 24)).Select(r => new
                {
                    rank = r.Rank, name = r.Name, cls = r.ClassName, color = r.Color, self = r.IsSelf, cells = r.Cells, sub = r.Sub,
                    bar = Clamp(r.Bar), given = Math.Max(Clamp(r.Bar), Clamp(r.Given)), heal = Math.Max(Math.Max(Clamp(r.Bar), Clamp(r.Given)), Clamp(r.Heal)),
                    plusGiven = r.PlusGiven, plusHeal = r.PlusHeal,
                }),
            },
            boss,
            me,
        }, Options);

    private static double Clamp(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

    /// <summary>Without names, everyone is shown by their class ("Cleric", "Cleric 2", ...), you too: what is streamed is never more than the uploads carry by default.</summary>
    public static IReadOnlyList<StreamRowData> HideNames(IReadOnlyList<StreamRowData> rows)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<StreamRowData>(rows.Count);
        foreach (StreamRowData row in rows)
        {
            string cls = string.IsNullOrEmpty(row.ClassName) ? "?" : row.ClassName;
            int n = seen[cls] = seen.GetValueOrDefault(cls) + 1;
            result.Add(row with { Name = n == 1 ? cls : cls + " " + n });
        }

        return result;
    }
}
