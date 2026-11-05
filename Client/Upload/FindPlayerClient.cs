using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace AionDPS.Upload;

/// <summary>One character the meter's "Find a player" found: what the website knows of it (Combat Power, Item Level, the best parse of a public fight with its
/// percentile in the class on that boss: players of the meter only) and the address of its page in Character Lookup.</summary>
public sealed record FoundPlayer(string Name, string? ClassName, int? Level, string Server, int ServerId, string Region, int? CombatPower, int? ItemLevel,
    double? BestDps, string? BestBoss, string? Page, int? BestPercent = null, int? BestRank = null, int? BestOf = null);

/// <summary>The answer of a search: the characters, or why there are none.</summary>
public sealed record FindAnswer(IReadOnlyList<FoundPlayer> Players, string? Error);

/// <summary>
/// The meter's "Find a player" (5 November 2026): <c>GET /meter/api/find?name=&amp;region=</c> on the website, which runs the site's own name search
/// (its limits apply, as for a visitor of Character Lookup) and adds what the site knows. Never redirected, never throwing.
/// </summary>
public static class FindPlayerClient
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>The regions to search, as the site names them, with their names on the meter.</summary>
    public static readonly (string Code, string Name)[] Regions =
    {
        ("eu", "Europe"), ("nae", "NA East"), ("naw", "NA West"), ("la", "Latin America"), ("as", "Asia"), ("all", "All regions"),
    };

    /// <summary>The site's region of a server ("eu", "nae" …), "all" when it is not known.</summary>
    public static string RegionOf(int serverId) => Aion2.Protocol.Aion2Servers.RegionCodeOf(serverId) switch
    {
        "latam" => "la",
        "asia" => "as",
        { } code => code,
        null => "all",
    };

    public static async Task<FindAnswer> SearchAsync(string name, string region)
    {
        string url = $"{Brand.Api}/api/find?name={Uri.EscapeDataString(name.Trim())}&region={Uri.EscapeDataString(region)}";
        try
        {
            using HttpResponseMessage response = await Http.GetAsync(url).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(body);
            JsonElement root = doc.RootElement;
            if (!response.IsSuccessStatusCode)
            {
                string? error = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out JsonElement e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
                return new FindAnswer(Array.Empty<FoundPlayer>(), error ?? $"The search didn't work ({(int)response.StatusCode}).");
            }

            return new FindAnswer(Read(root), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return new FindAnswer(Array.Empty<FoundPlayer>(), "The website can't be reached right now.");
        }
    }

    /// <summary>
    /// The Combat Power and Item Level of any character from its official page, through the website's lookup for the app (<c>/meter/api/character-page</c>, the
    /// lookup's limits): for a character the website has no figures of yet - most players who never used the meter (5 November 2026, the owner: "the lookup
    /// should work for people not using the meter also"). Nulls when the page can't be read.
    /// </summary>
    public static async Task<(int? CombatPower, int? ItemLevel)> FiguresAsync(FoundPlayer player)
    {
        if (player.Region is not ("eu" or "nae" or "naw" or "la" or "as") || player.ServerId <= 0)
        {
            return (null, null);
        }

        string url = $"{Brand.Api}/api/character-page?region={player.Region}&server={player.ServerId.ToString(CultureInfo.InvariantCulture)}&name={Uri.EscapeDataString(player.Name)}";
        try
        {
            using HttpResponseMessage response = await Http.GetAsync(url).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return (null, null);
            }

            using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            JsonElement c = doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("character", out JsonElement ch) ? ch : default;
            return c.ValueKind == JsonValueKind.Object ? (Int(c, "combatPower") is > 0 and int cp ? cp : null, Int(c, "itemLevel") is > 0 and int il ? il : null) : (null, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return (null, null);
        }
    }

    /// <summary>The characters of an answer (also read by the self-check).</summary>
    public static IReadOnlyList<FoundPlayer> Read(JsonElement root)
    {
        var list = new List<FoundPlayer>();
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("results", out JsonElement results) || results.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (JsonElement x in results.EnumerateArray())
        {
            if (Str(x, "name") is not { Length: > 0 } name || Int(x, "serverId") is not int serverId)
            {
                continue;
            }

            JsonElement best = x.TryGetProperty("best", out JsonElement b) && b.ValueKind == JsonValueKind.Object ? b : default;
            string? page = Str(x, "page");
            bool has = best.ValueKind == JsonValueKind.Object;
            list.Add(new FoundPlayer(name, Str(x, "className"), Int(x, "level"), Str(x, "server") ?? "", serverId, Str(x, "region") ?? "",
                Int(x, "cp"), Int(x, "il"), has ? Dbl(best, "dps") : null, has ? Str(best, "boss") : null,
                Brand.IsSiteUrl(page) ? page : null,
                has ? Int(best, "pct") : null, has ? Int(best, "rank") : null, has ? Int(best, "of") : null));
        }

        return list;
    }

    private static string? Str(JsonElement e, string key) => e.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement e, string key) => e.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d)
        && d is >= int.MinValue and <= int.MaxValue ? (int)Math.Round(d) : null;

    private static double? Dbl(JsonElement e, string key) => e.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) ? d : null;

    /// <summary>"Templar · Zikel · Europe".</summary>
    public static string Line(FoundPlayer p)
    {
        string region = Regions.FirstOrDefault(r => r.Code == p.Region).Name ?? p.Region.ToUpperInvariant();
        var parts = new List<string>();
        if (p.ClassName is { Length: > 0 } cls)
        {
            parts.Add(cls);
        }

        if (p.Server.Length > 0)
        {
            parts.Add(p.Server);
        }

        parts.Add(region);
        return string.Join("  ·  ", parts);
    }

    public static string Number(int value) => value.ToString("N0", CultureInfo.CurrentCulture);
}
