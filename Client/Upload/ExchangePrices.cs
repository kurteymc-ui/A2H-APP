using System.IO;
using System.Net.Http;
using System.Text.Json;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Upload;

/// <summary>
/// Market prices straight from a public market price site, for the server of the character (6 November 2026, the owner: the Journey page "just use [that
/// site] to get prices, for the server of the character"; no sign-in needed). One request a server, every item at once (GET
/// https://aion2.exchange/api/items?server=&lt;its id&gt;&amp;limit=1000, its server ids from /api/servers: the game's server key 2302 is its 12302), kept half an hour in
/// memory and on disk (exchange-prices-&lt;server&gt;.json in the data folder, so the page has prices at once after a restart). What one is worth: the average
/// sale where it sold, else the cheapest listing now, else the last listing seen (<see cref="MarketPrice.Value"/>). Only the server id is sent.
/// </summary>
public static class ExchangePrices
{
    private const string Base = "https://aion2.exchange/api";
    private static readonly HttpClient Http = MakeHttp();
    private static readonly TimeSpan Keep = TimeSpan.FromMinutes(30), Retry = TimeSpan.FromMinutes(3);
    private static readonly Dictionary<int, (DateTime At, MarketBook? Book)> Books = new();
    private static readonly HashSet<int> Loading = new();
    private static Dictionary<int, int>? _siteIds;

    private static HttpClient MakeHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        string version = Update.AppVersion.Text.TrimStart('v');
        http.DefaultRequestHeaders.UserAgent.TryParseAdd($"{Brand.AppName.Replace(" ", "", StringComparison.Ordinal)}/{(version.Length > 0 ? version : "0")}");
        return http;
    }

    /// <summary>Counts up whenever prices arrived (a page redraws then).</summary>
    public static int Version { get { lock (Books) { return _version; } } }

    private static int _version;

    private static string DataFolder => Environment.GetEnvironmentVariable("AIONDPS_DATA_DIR") is { Length: > 0 } testDir
        ? testDir : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Brand.DataFolder);

    private static string FileOf(int serverId) => Path.Combine(DataFolder, $"exchange-prices-{serverId}.json");

    /// <summary>The prices of the game's server <paramref name="serverId"/>: the kept copy (also from disk), fetched again in the background when it is older than
    /// half an hour; null while the first ones load or when none could be read.</summary>
    public static MarketBook? For(int serverId)
    {
        if (serverId <= 0)
        {
            return null;
        }

        lock (Books)
        {
            if (!Books.ContainsKey(serverId) && ReadFile(serverId) is { } kept)
            {
                Books[serverId] = kept;
            }

            bool have = Books.TryGetValue(serverId, out var hit);
            if (have && DateTime.UtcNow - hit.At < (hit.Book is null ? Retry : Keep))
            {
                return hit.Book;
            }

            if (Loading.Add(serverId))
            {
                _ = Task.Run(() => FetchAsync(serverId, have ? hit.Book : null));
            }

            return have ? hit.Book : null;
        }
    }

    /// <summary>Whether the prices of this server are being fetched now.</summary>
    public static bool IsLoading(int serverId)
    {
        lock (Books)
        {
            return Loading.Contains(serverId);
        }
    }

    /// <summary>The prices, waited for (a report built off the window's thread): the kept copy when it is fresh, else fetched now.</summary>
    public static async Task<MarketBook?> GetAsync(int serverId)
    {
        MarketBook? book = For(serverId);
        for (int i = 0; i < 100 && IsLoading(serverId); i++)
        {
            await Task.Delay(200).ConfigureAwait(false);
        }

        return For(serverId) ?? book;
    }

    private static async Task FetchAsync(int serverId, MarketBook? before)
    {
        MarketBook? book = null;
        string? body = null;
        try
        {
            int site = await SiteIdOf(serverId).ConfigureAwait(false) ?? 10000 + serverId;
            using HttpResponseMessage r = await Http.GetAsync($"{Base}/items?server={site}&limit=1000").ConfigureAwait(false);
            if (r.IsSuccessStatusCode)
            {
                body = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
                book = Parse(body, serverId);
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

        if (book is not null && body is not null)
        {
            WriteFile(serverId, body);
        }
    }

    /// <summary>The site's id of a game server: by its game server key in /api/servers (2302 is 12302 today); null when the list could not be read.</summary>
    private static async Task<int?> SiteIdOf(int serverId)
    {
        if (_siteIds is null)
        {
            try
            {
                using HttpResponseMessage r = await Http.GetAsync($"{Base}/servers").ConfigureAwait(false);
                if (r.IsSuccessStatusCode)
                {
                    using JsonDocument doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync().ConfigureAwait(false));
                    var ids = new Dictionary<int, int>();
                    foreach (JsonElement s in doc.RootElement.GetProperty("servers").EnumerateArray())
                    {
                        if (s.TryGetProperty("game_server_key", out JsonElement key) && key.TryGetInt32(out int game)
                            && s.TryGetProperty("server_id", out JsonElement id) && id.TryGetInt32(out int siteId))
                        {
                            ids[game] = siteId;
                        }
                    }

                    _siteIds = ids;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException)
            {
                return null;
            }
        }

        return _siteIds is not null && _siteIds.TryGetValue(serverId, out int found) ? found : null;
    }

    /// <summary>The site's answer: {total, items: [{item_key, price, last_price, units_sold, sold_avg_price, captured_at, ...}]}.</summary>
    public static MarketBook Parse(string body, int serverId)
    {
        using JsonDocument doc = JsonDocument.Parse(body);
        static long? Num(JsonElement o, string k) => o.TryGetProperty(k, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d)
            && double.IsFinite(d) && d > 0 && d < 1e15 ? (long)Math.Round(d) : null;
        var items = new Dictionary<int, MarketPrice>();
        DateTime? newest = null;
        foreach (JsonElement item in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            if (!item.TryGetProperty("item_key", out JsonElement key) || !key.TryGetInt32(out int id))
            {
                continue;
            }

            long? sold = Num(item, "units_sold");
            items[id] = new MarketPrice(Num(item, "price"), Num(item, "sold_avg_price"), sold is long u ? (int)Math.Min(u, int.MaxValue) : null, Num(item, "last_price"));
            if (item.TryGetProperty("captured_at", out JsonElement c) && c.ValueKind == JsonValueKind.String
                && DateTime.TryParse(c.GetString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out DateTime at)
                && (newest is null || at > newest))
            {
                newest = at;
            }
        }

        return new MarketBook(serverId, Aion2Servers.NameOf(serverId), newest, items);
    }

    private static (DateTime At, MarketBook? Book)? ReadFile(int serverId)
    {
        try
        {
            string path = FileOf(serverId);
            if (!File.Exists(path))
            {
                return null;
            }

            DateTime at = File.GetLastWriteTimeUtc(path);
            return (at, Parse(File.ReadAllText(path), serverId));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return null;
        }
    }

    private static void WriteFile(int serverId, string body)
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            string temp = FileOf(serverId) + ".tmp";
            File.WriteAllText(temp, body);
            File.Move(temp, FileOf(serverId), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the next fetch tries again
        }
    }
}
