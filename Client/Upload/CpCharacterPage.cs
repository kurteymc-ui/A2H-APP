using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Upload;

/// <summary>A worn piece as the character's official page lists it: the item, its level as the game sends it (enhancement + breakthrough) and its stones.</summary>
public sealed record CpOfficialPiece(int ItemId, int Level, IReadOnlyList<(string Stat, int Value)> Stones);

/// <summary>What the character's official page said when it was read: the pieces with their stones, the Combat Power and Item Level it shows and when the
/// website read it from the game's servers.</summary>
public sealed record CpOfficialRead(IReadOnlyList<CpOfficialPiece> Pieces, int? Cp, int? ItemLevel, DateTime? At);

/// <summary>
/// The character's official page through the website's character lookup for the app (atreia.app/meter/api/character-page, the lookup's gear only):
/// the worn pieces' manastones (the game sends a stone's stat and colour but not its value, the page names the value, "Attack +8"), the Combat Power
/// and the Item Level the page shows. The site's own lookup address (/api/lookup) is for its pages and refuses a program, so the calculator showed every
/// stone as a range until 4 November 2026 (v1.20.5). Read
/// once per character at its login while the meter runs, and again when the player asks for it (a refresh of the character on the website: at most one in
/// ten minutes per character, the website's rule); never when the player declined the privacy terms. What is asked is the character's name and server,
/// which the page shows to anyone.
/// </summary>
public static class CpCharacterPage
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(45) };

    /// <summary>The page; null when it can't be read (no answer, another format, the character not found). <paramref name="fresh"/>: refresh the
    /// character on the website first.</summary>
    public static async Task<CpOfficialRead?> ReadAsync(string name, int serverId, bool fresh = false)
    {
        string? region = Aion2Servers.RegionCodeOf(serverId) switch { "latam" => "la", "asia" => "as", var r => r };
        if (region is null || name.Length == 0 || serverId <= 0)
        {
            return null;
        }

        string url = PageUrl(region, serverId, name, fresh);
        try
        {
            using HttpResponseMessage response = await Http.GetAsync(url).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            return ReadPage(doc.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The address of the character's page for the app.</summary>
    internal static string PageUrl(string region, int serverId, string name, bool fresh) =>
        $"{Brand.Api}/api/character-page?region={region}&server={serverId.ToString(CultureInfo.InvariantCulture)}&name={Uri.EscapeDataString(name)}{(fresh ? "&fresh=1" : "")}";

    /// <summary>The page of the lookup's answer: the pieces (<see cref="Read"/>), <c>character.combatPower</c>, <c>character.itemLevel</c> and the time of the read
    /// (<c>checkedAt</c>, else <c>retrievedAt</c>); null when the answer has no gear.</summary>
    internal static CpOfficialRead? ReadPage(JsonElement root)
    {
        IReadOnlyList<CpOfficialPiece> pieces = Read(root);
        if (pieces.Count == 0)
        {
            return null;
        }

        int? Int(JsonElement e, string property) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(property, out JsonElement v) && Number(v) is double d && d > 0 ? (int)d : null;
        JsonElement character = root.TryGetProperty("character", out JsonElement c) ? c : default;
        DateTime? at = null;
        foreach (string property in new[] { "checkedAt", "retrievedAt" })
        {
            if (root.TryGetProperty(property, out JsonElement t) && t.ValueKind == JsonValueKind.String
                && DateTime.TryParse(t.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime when))
            {
                at = when;
                break;
            }
        }

        return new CpOfficialRead(pieces, Int(character, "combatPower"), Int(character, "itemLevel"), at);
    }

    /// <summary>The pieces of the lookup's answer (<c>gear[] {id, enchantLevel, exceedLevel, details.magicStoneStat[] {id, name, value}}</c>; a stone's stat by
    /// the game's own id of it (the meter's token, "WeaponFixingDamage"), else by its English name as the game writes it ("Attack"); a value in the game's
    /// units ("+100" Weapon Damage Boost = 1%), a boost written as a percentage in hundredths: "1.5%" = 150).</summary>
    internal static IReadOnlyList<CpOfficialPiece> Read(JsonElement root)
    {
        var tokens = new HashSet<string>(Aion2CpData.StoneStats, StringComparer.Ordinal);
        var byName = Aion2CpData.StoneStats.GroupBy(t => Aion2CpData.Name(t, "en"), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        string? StatOf(JsonElement stone) =>
            stone.TryGetProperty("id", out JsonElement i) && i.ValueKind == JsonValueKind.String && tokens.Contains((i.GetString() ?? "").Trim()) ? (i.GetString() ?? "").Trim()
            : stone.TryGetProperty("name", out JsonElement n) && n.ValueKind == JsonValueKind.String && byName.TryGetValue((n.GetString() ?? "").Trim(), out string? token) ? token
            : null;
        var result = new List<CpOfficialPiece>();
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("gear", out JsonElement gear) || gear.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (JsonElement piece in gear.EnumerateArray())
        {
            if (piece.ValueKind != JsonValueKind.Object || !piece.TryGetProperty("id", out JsonElement idElement) || Number(idElement) is not double id
                || !piece.TryGetProperty("details", out JsonElement details) || details.ValueKind != JsonValueKind.Object
                || !details.TryGetProperty("magicStoneStat", out JsonElement stones) || stones.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var list = new List<(string, int)>();
            foreach (JsonElement stone in stones.EnumerateArray())
            {
                if (stone.ValueKind == JsonValueKind.Object && StatOf(stone) is string token && stone.TryGetProperty("value", out JsonElement v) && Number(v) is double value)
                {
                    bool percent = v.ValueKind == JsonValueKind.String && (v.GetString() ?? "").Contains('%');
                    list.Add((token, (int)Math.Round(percent ? value * 100 : value)));
                }
            }

            int Level(string property) => piece.TryGetProperty(property, out JsonElement e) && Number(e) is double d ? (int)d : 0;
            result.Add(new CpOfficialPiece((int)id, Level("enchantLevel") + Level("exceedLevel"), list));
        }

        return result;
    }

    /// <summary>
    /// The stones the page names for a worn piece: the page's piece with the same item (and, when the character wears it twice, the same level and stones
    /// that fit the worn ones: the same stats, values the colours can have). Null when no piece or more than one different piece fits: the values stay a range.
    /// </summary>
    public static IReadOnlyList<(string Stat, int Value)>? StonesFor(IReadOnlyList<CpOfficialPiece>? pieces, Aion2EquippedItem item)
    {
        if (pieces is null)
        {
            return null;
        }

        var worn = (item.Stones ?? Array.Empty<Aion2Stone>()).Where(s => s.StatId != 0).ToList();
        var fits = pieces.Where(p => p.ItemId == item.ItemId).ToList();
        if (fits.Count > 1)
        {
            fits = fits.Where(p => p.Level == item.Enchant && Fit(p.Stones, worn)).ToList();
        }

        if (fits.Count == 0 || fits.Skip(1).Any(p => !Same(p.Stones, fits[0].Stones)))
        {
            return null;
        }

        return fits[0].Stones;
    }

    /// <summary>Every worn stone has a stone of the page's piece with its stat and a value its colour can have, one each.</summary>
    private static bool Fit(IReadOnlyList<(string Stat, int Value)> official, IReadOnlyList<Aion2Stone> worn)
    {
        var left = official.ToList();
        foreach (Aion2Stone stone in worn)
        {
            string? stat = Aion2CpData.Token(stone.StatId);
            (int Min, int Max)? range = stat is null ? null : Aion2CpData.Stone(stat, stone.Tier);
            int at = left.FindIndex(o => o.Stat == stat && (range is null || (o.Value >= range.Value.Min && o.Value <= range.Value.Max)));
            if (at < 0)
            {
                return false;
            }

            left.RemoveAt(at);
        }

        return true;
    }

    private static bool Same(IReadOnlyList<(string Stat, int Value)> a, IReadOnlyList<(string Stat, int Value)> b) =>
        a.OrderBy(x => x.Stat, StringComparer.Ordinal).ThenBy(x => x.Value).SequenceEqual(b.OrderBy(x => x.Stat, StringComparer.Ordinal).ThenBy(x => x.Value));

    private static double? Number(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Number)
        {
            return e.GetDouble();
        }

        if (e.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string digits = new((e.GetString() ?? "").Where(c => char.IsDigit(c) || c == '.').ToArray());
        return double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;
    }
}
