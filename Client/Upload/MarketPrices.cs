using System.IO;
using System.Net.Http;
using System.Text.Json;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Upload;

/// <summary>An item's market price on one server: the cheapest listing now, the average sale of the last 28 days (with how many sold), the last cheapest listing seen.</summary>
public sealed record MarketPrice(long? Cheapest, long? Average, int? Sold, long? LastSeen)
{
    /// <summary>What one is worth: the average sale where it sold, else the cheapest listing, else the last one seen.</summary>
    public long? Value => Average is > 0 && Sold is > 0 ? Average : Cheapest is > 0 ? Cheapest : LastSeen is > 0 ? LastSeen : Average is > 0 ? Average : null;
}

/// <summary>A server's prices: the item id's price, the server's name and when the market data read them.</summary>
public sealed record MarketBook(int Server, string Name, DateTime? Captured, IReadOnlyDictionary<int, MarketPrice> Items);

/// <summary>
/// The market's prices for the loot's worth: the site's price service, the same one its crafting pages use (GET /meter/api/prices?server=, the server list at
/// /meter/api/market-servers; for a meter signed in to AION 2 Helper). Fetched for one server at a time, kept half an hour; the game's server id is the market's
/// (else the name is looked up). Asked only with the privacy policy accepted (the caller says so).
/// </summary>
public static class MarketPrices
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly TimeSpan Keep = TimeSpan.FromMinutes(30), Retry = TimeSpan.FromMinutes(3);
    private static readonly Dictionary<int, (DateTime At, MarketBook? Book)> Books = new();
    private static readonly HashSet<int> Loading = new();

    /// <summary>Counts up whenever prices arrived (a page redraws then).</summary>
    public static int Version { get { lock (Books) { return _version; } } }

    private static int _version;

    /// <summary>The prices of the game's server <paramref name="serverId"/>; null while they are loading (the first call fetches them) or when they could not be read.</summary>
    public static MarketBook? For(int serverId, bool ask = true)
    {
        if (serverId <= 0)
        {
            return null;
        }

        lock (Books)
        {
            bool have = Books.TryGetValue(serverId, out var hit);
            if (!ask || (have && DateTime.UtcNow - hit.At < (hit.Book is null ? Retry : Keep)))
            {
                return have ? hit.Book : null;
            }

            if (Account.Token is not { } token)
            {
                return have ? hit.Book : null; // the prices are for a signed-in meter
            }

            if (Loading.Add(serverId))
            {
                _ = Task.Run(() => FetchAsync(serverId, token, have ? hit.Book : null)); // read and parsed away from the window
            }

            return have ? hit.Book : null; // the older copy while the new one comes
        }
    }

    /// <summary>Whether this meter can be given prices at all (signed in to AION 2 Helper).</summary>
    public static bool SignedIn => Account.Token is not null;

    /// <summary>Whether the prices of this server are being fetched now.</summary>
    public static bool IsLoading(int serverId)
    {
        lock (Books)
        {
            return Loading.Contains(serverId);
        }
    }

    private static async Task<HttpResponseMessage> GetAsync(string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Brand.Api + path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return await Http.SendAsync(request).ConfigureAwait(false);
    }

    private static async Task FetchAsync(int serverId, string token, MarketBook? before)
    {
        MarketBook? book = null;
        try
        {
            using HttpResponseMessage r = await GetAsync($"/api/prices?server={serverId}", token).ConfigureAwait(false);
            if (r.IsSuccessStatusCode)
            {
                book = Parse(await r.Content.ReadAsStringAsync().ConfigureAwait(false), serverId);
            }
            else if ((int)r.StatusCode == 400 && await MarketIdOf(serverId, token).ConfigureAwait(false) is int other && other != serverId)
            {
                using HttpResponseMessage again = await GetAsync($"/api/prices?server={other}", token).ConfigureAwait(false);
                book = again.IsSuccessStatusCode ? Parse(await again.Content.ReadAsStringAsync().ConfigureAwait(false), serverId) : null;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException or InvalidOperationException or FormatException or KeyNotFoundException)
        {
            book = null;
        }
        finally
        {
            lock (Books)
            {
                // a failed refresh keeps the copy it had (asked again in a few minutes)
                Books[serverId] = book is null && before is not null ? (DateTime.UtcNow - Keep + Retry, before) : (DateTime.UtcNow, book);
                Loading.Remove(serverId);
                _version++;
            }
        }
    }

    /// <summary>The market's id of a game server the market lists under another number: by its name (and region where both have one).</summary>
    private static async Task<int?> MarketIdOf(int serverId, string token)
    {
        string name = Aion2Servers.NameOf(serverId);
        using HttpResponseMessage r = await GetAsync("/api/market-servers", token).ConfigureAwait(false);
        if (!r.IsSuccessStatusCode)
        {
            return null;
        }

        using JsonDocument doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync().ConfigureAwait(false));
        var hits = doc.RootElement.GetProperty("servers").EnumerateArray()
            .Where(s => string.Equals(s.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase))
            .Select(s => s.GetProperty("id").GetInt32()).ToList();
        return hits.Count == 1 ? hits[0] : hits.Contains(10000 + serverId) ? 10000 + serverId : (int?)null;
    }

    /// <summary>The site's answer: {ok, server: {id, name}, capturedAt, items: {"item id": {p, l, a, u, lp, s}}}.</summary>
    public static MarketBook Parse(string body, int serverId)
    {
        using JsonDocument doc = JsonDocument.Parse(body);
        JsonElement root = doc.RootElement;
        static long? Num(JsonElement o, string k) => o.TryGetProperty(k, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) && double.IsFinite(d) && Math.Abs(d) < 1e15 ? (long)Math.Round(d) : null;
        var items = new Dictionary<int, MarketPrice>();
        foreach (JsonProperty p in root.GetProperty("items").EnumerateObject())
        {
            if (int.TryParse(p.Name, out int id) && p.Value.ValueKind == JsonValueKind.Object)
            {
                items[id] = new MarketPrice(Num(p.Value, "p"), Num(p.Value, "a"), Num(p.Value, "u") is long u ? (int?)(int)Math.Clamp(u, 0, int.MaxValue) : null, Num(p.Value, "lp"));
            }
        }

        string name = root.TryGetProperty("server", out JsonElement s) && s.ValueKind == JsonValueKind.Object && s.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? "" : "";
        DateTime? captured = root.TryGetProperty("capturedAt", out JsonElement c) && c.ValueKind == JsonValueKind.String && DateTime.TryParse(c.GetString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out DateTime at) ? at : null;
        return new MarketBook(serverId, name.Length > 0 ? name : Aion2Servers.NameOf(serverId), captured, items);
    }

    /// <summary>What a session's loot is worth: the sum of count × value of the items with a price, and how many kinds had none.</summary>
    public static (long Worth, int Priced, int Unpriced) Worth(IEnumerable<(int ItemId, long Count)> items, MarketBook book)
    {
        long worth = 0;
        int priced = 0, unpriced = 0;
        foreach (var (id, count) in items)
        {
            if (book.Items.TryGetValue(id, out MarketPrice? p) && p.Value is long each)
            {
                worth += each * count;
                priced++;
            }
            else
            {
                unpriced++;
            }
        }

        return (worth, priced, unpriced);
    }
}
