using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2.Protocol;

/// <summary>
/// A try at the next enhancement or breakthrough level: the chance it works (%), the points a failed try adds to the next one (%), and what a try takes:
/// Kina and a material (Enhance Stones, Amplify Stones). A failed try keeps the level.
/// </summary>
public sealed record CpTry(double Chance, double Pity, long Kina, int MaterialId, int Count)
{
    /// <summary>How many tries the level takes on average, the points a failure adds counted in.</summary>
    public double Tries => Aion2CpCosts.ExpectedTries(Chance, Pity);
}

/// <summary>What the cost tables say about a piece: its enhancement and breakthrough cost groups, its soul-binding grade, its race (D Asmodian, L Elyos,
/// A both) and whether it is crafted.</summary>
public sealed record CpPieceCost(string? EnchantKey, string? ExceedKey, string? SoulGrade, char Race, bool Crafted);

/// <summary>
/// A transfer craft ("Transfer Crafting"): a crafted piece made into a better one, its enhancement, amplification and soul binding carried over. What it
/// makes, the "Splendent" piece it makes instead at <see cref="ComboChance"/> % and what it takes (Kina, items).
/// </summary>
public sealed record CpTransfer(int OutputId, int ComboId, double ComboChance, long Kina, IReadOnlyList<(int ItemId, int Count)> Materials);

/// <summary>
/// What upgrades cost (assets/aion2/cp/cp_costs.json): every
/// enhancement and breakthrough level's chance, the points a failure adds, its Kina and material; what a soul-binding Rebind takes per grade; the transfer
/// crafts of the crafted pieces; each piece's race and whether it is crafted.
/// </summary>
public static class Aion2CpCosts
{
    private static readonly object Gate = new();
    private static Data? _data;

    private sealed class Data
    {
        public int Kina = 930100031, Stone = 930100023;
        public Dictionary<int, CpPieceCost> Pieces = new();
        public Dictionary<string, List<CpTry>> Enchant = new(StringComparer.Ordinal);
        public Dictionary<string, List<CpTry>> Exceed = new(StringComparer.Ordinal);
        public Dictionary<string, (long Kina, int MaterialId, int Count)> Soul = new(StringComparer.Ordinal);
        public Dictionary<int, List<CpTransfer>> Transfers = new();
    }

    private static Data D
    {
        get
        {
            lock (Gate)
            {
                return _data ??= Load(Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "cp", "cp_costs.json"));
            }
        }
    }

    public static bool Available => D.Pieces.Count > 0;

    /// <summary>The item id of Kina in the bag.</summary>
    public static int KinaId => D.Kina;

    /// <summary>The item id of the Enhance Stones.</summary>
    public static int StoneId => D.Stone;

    public static CpPieceCost? Piece(int itemId) => D.Pieces.GetValueOrDefault(itemId);

    /// <summary>The try from enhancement level <paramref name="level"/> to the next; null when the tables have none (the top, or an unknown piece).</summary>
    public static CpTry? Enchant(int itemId, int level) =>
        Piece(itemId)?.EnchantKey is { } key && D.Enchant.TryGetValue(key, out var tries) && level >= 0 && level < tries.Count ? tries[level] : null;

    /// <summary>The try from breakthrough level <paramref name="level"/> to the next; null when there is none.</summary>
    public static CpTry? Exceed(int itemId, int level) =>
        Piece(itemId)?.ExceedKey is { } key && D.Exceed.TryGetValue(key, out var tries) && level >= 0 && level < tries.Count ? tries[level] : null;

    /// <summary>A soul-binding Rebind of the piece: Kina and how many Soul Codex: Reset; null when the piece can't be soul-bound.</summary>
    public static (long Kina, int MaterialId, int Count)? Rebind(int itemId) =>
        Piece(itemId)?.SoulGrade is { } grade && D.Soul.TryGetValue(grade, out var cost) ? cost : null;

    /// <summary>The transfer crafts that make the crafted piece into a better one; empty when there are none.</summary>
    public static IReadOnlyList<CpTransfer> Transfers(int itemId) => D.Transfers.TryGetValue(itemId, out var list) ? list : Array.Empty<CpTransfer>();

    /// <summary>
    /// How many tries a level takes on average when a try works at <paramref name="chance"/> % and every failure adds <paramref name="pity"/> points to the
    /// next try: the sum over k of the chance that the first k tries all failed.
    /// </summary>
    public static double ExpectedTries(double chance, double pity)
    {
        if (chance >= 100)
        {
            return 1;
        }

        if (chance <= 0 && pity <= 0)
        {
            return double.PositiveInfinity;
        }

        double expected = 0, allFailed = 1;
        for (int k = 0; k < 1000 && allFailed > 1e-12; k++)
        {
            expected += allFailed;
            allFailed *= 1 - Math.Min(1, (chance + pity * k) / 100);
        }

        return expected;
    }

    private static Data Load(string path)
    {
        var d = new Data();
        try
        {
            if (!File.Exists(path))
            {
                return d;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement r = doc.RootElement;
            static string? Str(JsonElement e) => e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            d.Kina = r.TryGetProperty("kina", out JsonElement kina) ? kina.GetInt32() : d.Kina;
            d.Stone = r.TryGetProperty("stone", out JsonElement stone) ? stone.GetInt32() : d.Stone;
            foreach (JsonProperty p in r.GetProperty("items").EnumerateObject())
            {
                JsonElement v = p.Value;
                string race = Str(v[3]) ?? "A";
                d.Pieces[int.Parse(p.Name, System.Globalization.CultureInfo.InvariantCulture)] =
                    new CpPieceCost(Str(v[0]), Str(v[1]), Str(v[2]), race.Length > 0 ? race[0] : 'A', v[4].GetInt32() == 1);
            }

            foreach (JsonProperty p in r.GetProperty("enchant").EnumerateObject())
            {
                d.Enchant[p.Name] = p.Value.EnumerateArray().Select(t => new CpTry(t[0].GetDouble(), t[1].GetDouble(), t[2].GetInt64(), d.Stone, t[3].GetInt32())).ToList();
            }

            var tables = r.GetProperty("exceedTables").EnumerateArray()
                .Select(table => table.EnumerateArray().Select(t => new CpTry(t[0].GetDouble(), t[1].GetDouble(), t[2].GetInt64(), t[3].GetInt32(), t[4].GetInt32())).ToList()).ToList();
            foreach (JsonProperty p in r.GetProperty("exceed").EnumerateObject())
            {
                int index = p.Value.GetInt32();
                if (index >= 0 && index < tables.Count)
                {
                    d.Exceed[p.Name] = tables[index];
                }
            }

            foreach (JsonProperty p in r.GetProperty("soul").EnumerateObject())
            {
                d.Soul[p.Name] = (p.Value[0].GetInt64(), p.Value[1].GetInt32(), p.Value[2].GetInt32());
            }

            foreach (JsonProperty p in r.GetProperty("transfers").EnumerateObject())
            {
                d.Transfers[int.Parse(p.Name, System.Globalization.CultureInfo.InvariantCulture)] = p.Value.EnumerateArray().Select(t => new CpTransfer(
                    t[0].GetInt32(), t[1].GetInt32(), t[2].GetDouble(), t[3].GetInt64(),
                    t[4].EnumerateArray().Select(m => (m[0].GetInt32(), m[1].GetInt32())).ToList())).ToList();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException or UnauthorizedAccessException
                                       or IndexOutOfRangeException)
        {
            return new Data();
        }

        return d;
    }
}
