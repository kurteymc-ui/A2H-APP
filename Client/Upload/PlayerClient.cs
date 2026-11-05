using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace AionDPS.Upload;

/// <summary>One line of a piece (a base or added attribute, a stone, a Theostone, a skill level): its name, its value as the page writes it, its grade.</summary>
public sealed record PlayerLine(string Name, string Value, string? Grade, string? Icon, string? Desc);

/// <summary>A worn piece: slot, name, grade, enhancement (+breakthrough), Item Level, icon, whether it is Arcana, and its lines when the page gave them.</summary>
public sealed record PlayerPiece(string Slot, string Name, string Grade, int Enchant, int Exceed, int? ItemLevel, string? Icon, bool Arcana,
    IReadOnlyList<PlayerLine> Main, IReadOnlyList<PlayerLine> Sub, IReadOnlyList<PlayerLine> Skills, IReadOnlyList<PlayerLine> Stones, IReadOnlyList<PlayerLine> Godstones)
{
    public bool HasDetails => Main.Count + Sub.Count + Skills.Count + Stones.Count + Godstones.Count > 0;
}

public sealed record PlayerSkill(string Name, int? Level, string Category, bool Equipped, string? Icon);

public sealed record PlayerBoard(string Name, int Open, int Total);

public sealed record PlayerParse(string Boss, double? Dps, int Pct, int Rank, int Of);

/// <summary>A Genus Insight board as the player's own app sent it: the species (2 Cogni .. 6 Special), its level, and its rolled lines (page, slot, stat id, value, grade 0..5 as the meter's Genus page colours them).</summary>
public sealed record PlayerGenus(int SpeciesId, int Level, IReadOnlyList<(int Page, int Slot, int Stat, long Value, int Grade)> Lines);

/// <summary>What only the app knows of a character (its player uses Atreia App): Genus Insight and the pets, and when they were sent.</summary>
public sealed record PlayerAppData(IReadOnlyList<PlayerGenus> Genus, IReadOnlyList<(int PetId, int Level)> Pets, DateTime? Updated);

/// <summary>Everything the player window shows of a character.</summary>
public sealed record PlayerPage(string Name, string ClassName, int? Level, string Server, int ServerId, string Region, string Faction, string Legion, string Title,
    int? CombatPower, int? ItemLevel, IReadOnlyList<PlayerPiece> Gear, IReadOnlyList<(string Name, string Value)> Stats, IReadOnlyList<PlayerSkill> Skills,
    int? TitlesOwned, int? TitlesTotal, (string Name, int? Level)? Pet, (string Name, string Grade)? Wing, IReadOnlyList<PlayerBoard> Daevanion,
    IReadOnlyList<PlayerParse> Parses, PlayerAppData? App, string? Page, DateTime? ReadAt);

/// <summary>The answer: the page, or why there is none.</summary>
public sealed record PlayerAnswer(PlayerPage? Page, string? Error);

/// <summary>
/// The player window's data (6 November 2026, the owner: "a tab in game, not a redirect to the website; all details of the person if they use our app, if not,
/// genus/pets hidden"): <c>GET /meter/api/player?region=&amp;server=&amp;name=</c>, the character's official page as the website reads it, its best parses, and
/// what its own app sent of it. Never redirected, never throwing.
/// </summary>
public static class PlayerClient
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(45) };

    public static async Task<PlayerAnswer> GetAsync(FoundPlayer who, bool fresh = false)
    {
        if (who.Region is not ("eu" or "nae" or "naw" or "la" or "as") || who.ServerId <= 0)
        {
            return new PlayerAnswer(null, "This character's region isn't known.");
        }

        string url = $"{Brand.Api}/api/player?region={who.Region}&server={who.ServerId.ToString(CultureInfo.InvariantCulture)}&name={Uri.EscapeDataString(who.Name)}"
            + (fresh ? "&fresh=1" : "");
        try
        {
            using HttpResponseMessage response = await Http.GetAsync(url).ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            JsonElement root = doc.RootElement;
            if (!response.IsSuccessStatusCode || root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("character", out _))
            {
                string? error = root.ValueKind == JsonValueKind.Object && Str(root, "error") is { Length: > 0 } e ? e : null;
                return new PlayerAnswer(null, error ?? $"The character can't be read right now ({(int)response.StatusCode}).");
            }

            return new PlayerAnswer(Read(root, who), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return new PlayerAnswer(null, "The website can't be reached right now.");
        }
    }

    /// <summary>The page of an answer (also read by the pictures of the meter).</summary>
    public static PlayerPage Read(JsonElement root, FoundPlayer who)
    {
        JsonElement c = root.GetProperty("character");
        var gear = Items(root, "gear").Select(g => new PlayerPiece(Str(g, "slot") ?? "", Str(g, "name") ?? "", Str(g, "grade") ?? "", Int(g, "enchant") ?? 0, Int(g, "exceed") ?? 0,
            Int(g, "itemLevel"), Icon(g), Bool(g, "arcana"), Lines(g, "main"), Lines(g, "sub"), Lines(g, "skills"), Lines(g, "stones"), Lines(g, "godstones")))
            .Where(g => g.Name.Length > 0).ToList();
        var stats = Items(root, "stats").Select(s => (Str(s, "name") ?? "", Str(s, "value") ?? "")).Where(s => s.Item1.Length > 0).ToList();
        var skills = Items(root, "skills").Select(s => new PlayerSkill(Str(s, "name") ?? "", Int(s, "level"), Str(s, "category") ?? "", Bool(s, "equipped"), Icon(s)))
            .Where(s => s.Name.Length > 0).ToList();
        JsonElement titles = root.TryGetProperty("titles", out JsonElement t) && t.ValueKind == JsonValueKind.Object ? t : default;
        (string, int?)? pet = root.TryGetProperty("pet", out JsonElement p) && p.ValueKind == JsonValueKind.Object && Str(p, "name") is { Length: > 0 } pn ? (pn, Int(p, "level")) : null;
        (string, string)? wing = root.TryGetProperty("wing", out JsonElement w) && w.ValueKind == JsonValueKind.Object && Str(w, "name") is { Length: > 0 } wn ? (wn, Str(w, "grade") ?? "") : null;
        var boards = Items(root, "daevanion").Select(b => new PlayerBoard(Str(b, "name") ?? "", Int(b, "open") ?? 0, Int(b, "total") ?? 0)).Where(b => b.Name.Length > 0).ToList();
        var parses = Items(root, "parses").Select(x => new PlayerParse(Str(x, "boss") ?? "", Dbl(x, "dps"), Int(x, "pct") ?? 0, Int(x, "rank") ?? 0, Int(x, "of") ?? 0))
            .Where(x => x.Boss.Length > 0).ToList();
        PlayerAppData? app = null;
        if (root.TryGetProperty("app", out JsonElement a) && a.ValueKind == JsonValueKind.Object)
        {
            var genus = Items(a, "genus").Select(gx => new PlayerGenus(Int(gx, "id") ?? 0, Int(gx, "level") ?? 0,
                Items(gx, "fx").Where(f => f.ValueKind == JsonValueKind.Array && f.GetArrayLength() is 4 or 5 && f.EnumerateArray().All(v => v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out _)))
                    .Select(f => (f[0].GetInt32(), f[1].GetInt32(), f[2].GetInt32(), f[3].GetInt64(), f.GetArrayLength() == 5 ? f[4].GetInt32() : 0)).ToList())).Where(gx => gx.SpeciesId > 0).ToList();
            var pets = Items(a, "pets").Where(x => x.ValueKind == JsonValueKind.Array && x.GetArrayLength() == 2).Select(x => (x[0].GetInt32(), x[1].GetInt32())).ToList();
            DateTime? updated = Dbl(a, "updated") is double ms && ms > 0 ? DateTimeOffset.FromUnixTimeMilliseconds((long)ms).UtcDateTime : null;
            app = new PlayerAppData(genus, pets, updated);
        }

        string? page = Str(root, "page");
        return new PlayerPage(Str(c, "name") ?? who.Name, Str(c, "className") ?? who.ClassName ?? "", Int(c, "level") ?? who.Level, Str(c, "server") ?? who.Server,
            Int(c, "serverId") ?? who.ServerId, Str(c, "region") ?? who.Region, Str(c, "faction") ?? "", Str(c, "legion") ?? "", Str(c, "title") ?? "",
            Int(c, "combatPower"), Int(c, "itemLevel"), gear, stats, skills, titles.ValueKind == JsonValueKind.Object ? Int(titles, "owned") : null,
            titles.ValueKind == JsonValueKind.Object ? Int(titles, "total") : null, pet, wing, boards, parses, app,
            page is not null && Brand.IsSiteUrl(page) ? page : null,
            Str(root, "retrievedAt") is { } at && DateTime.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime read) ? read : null);
    }

    private static IReadOnlyList<PlayerLine> Lines(JsonElement e, string key) => Items(e, key)
        .Select(l => new PlayerLine(Str(l, "name") ?? "", Str(l, "value") ?? "", Str(l, "grade"), Icon(l), Str(l, "desc"))).Where(l => l.Name.Length > 0).ToList();

    private static IEnumerable<JsonElement> Items(JsonElement e, string key) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().ToList() : Enumerable.Empty<JsonElement>();

    /// <summary>A picture of the game's own site only (the website lets nothing else through either).</summary>
    private static string? Icon(JsonElement e) => Str(e, "icon") is { Length: > 0 } u && Uri.TryCreate(u, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host.EndsWith(".plaync.com", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("plaync.com", StringComparison.OrdinalIgnoreCase)) ? u : null;

    private static string? Str(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out JsonElement v)
        ? v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.Number => v.GetRawText(), _ => null }
        : null;

    private static int? Int(JsonElement e, string key) => Dbl(e, key) is double d && d is >= int.MinValue and <= int.MaxValue ? (int)Math.Round(d) : null;

    private static double? Dbl(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.Number
        && v.TryGetDouble(out double d) ? d : null;

    private static bool Bool(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.True;
}
