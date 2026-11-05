using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AionDPS.Announcements;

/// <summary>One part of a news article in reading order: a heading ("h"), a paragraph ("p"), a list line ("li"), a picture ("img", its address in
/// <see cref="Text"/>) or a table row ("row", its <see cref="Cells"/>).</summary>
public sealed record NewsBlock(string Kind, string Text, IReadOnlyList<string> Cells);

/// <summary>One item of the Home page's news: our own post about the app (<see cref="Official"/> false) or an official notice or patch note of the game.</summary>
public sealed record NewsItem(
    string Id, string Source, string Tag, string Title, string Summary, DateTime Posted, string? Image, string? Url, string? Page, string? PageLabel,
    IReadOnlyList<string> Points, IReadOnlyList<NewsBlock>? Blocks, DateTime? Start, int? Minutes, bool Done)
{
    /// <summary>An official notice or patch note of the game (read in full from the website when opened), not a post of ours.</summary>
    public bool Official => Source == "game";
}

/// <summary>
/// The news on the Home page (5 November 2026, the owner: "an event page, like a 1200x400 banner, with recent news that people can click to read"): the
/// website's list (<see cref="ListUrl"/>, src/appnews.js there) has our posts about the app and the game's newest official notices and patch notes, each
/// with a 1200x400 picture of the website. The list is asked for when Home shows and is older than 20 minutes, and kept in the data folder, so Home has
/// its news at the next start before the website answers (and offline). An official notice is read in full only when it is opened (<see cref="ArticleAsync"/>).
/// Nothing is sent: plain GETs without any identifier of the player. Never throws; a failure keeps what there is.
/// </summary>
public static class NewsClient
{
    public const string ListUrl = Brand.Site + "/api/app/news";
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(12) }; // no redirects: the host is fixed
    private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(20);
    private static readonly object Gate = new();
    private static readonly Dictionary<string, IReadOnlyList<NewsBlock>> Articles = new(StringComparer.Ordinal);
    private static IReadOnlyList<NewsItem>? _items;
    private static DateTime _asked = DateTime.MinValue;
    private static bool _asking;

    /// <summary>The list changed (a new answer of the website); raised on the thread that asked (the window's).</summary>
    public static event Action? Changed;

    private static string DataFolder => Environment.GetEnvironmentVariable("AIONDPS_DATA_DIR") is { Length: > 0 } testDir
        ? testDir : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Brand.DataFolder);

    public static string CachePath => Path.Combine(DataFolder, "news-cache.json");

    /// <summary>Where the pictures of the news are kept (by a hash of their address).</summary>
    public static string PictureFolder => Path.Combine(DataFolder, "news");

    /// <summary>The news as last known (the website's last answer, else the one kept from before), newest first; empty when there never was one.</summary>
    public static IReadOnlyList<NewsItem> Items
    {
        get
        {
            lock (Gate)
            {
                return _items ??= LoadKept();
            }
        }
    }

    /// <summary>Replaces the list (the pictures of the app draw sample news with it).</summary>
    public static void UseForPictures(IReadOnlyList<NewsItem> items)
    {
        lock (Gate)
        {
            _items = items;
            _asked = DateTime.UtcNow;
        }
    }

    /// <summary>Asks the website again when the list is older than 20 minutes (a failure waits as long), at most one question at a time.</summary>
    public static void RefreshIfStale()
    {
        lock (Gate)
        {
            if (_asking || DateTime.UtcNow - _asked < Fresh)
            {
                return;
            }

            _asking = true;
            _asked = DateTime.UtcNow;
        }

        _ = RefreshAsync();
    }

    private static async Task RefreshAsync()
    {
        try
        {
            using HttpResponseMessage response = await Http.GetAsync(ListUrl);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            string json = await response.Content.ReadAsStringAsync();
            List<NewsItem> items = Parse(json);
            if (items.Count == 0)
            {
                return; // not a list: keep the one there is
            }

            try
            {
                Directory.CreateDirectory(DataFolder);
                await File.WriteAllTextAsync(CachePath, json);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // kept for this run only
            }

            bool changed;
            lock (Gate)
            {
                changed = _items is null || !Same(_items, items);
                _items = items;
            }

            if (changed)
            {
                Changed?.Invoke();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
        {
            // offline or the website away: the list kept stays
        }
        finally
        {
            lock (Gate)
            {
                _asking = false;
            }
        }
    }

    private static bool Same(IReadOnlyList<NewsItem> a, IReadOnlyList<NewsItem> b) =>
        a.Count == b.Count && a.Zip(b).All(p => p.First.Id == p.Second.Id && p.First.Title == p.Second.Title && p.First.Summary == p.Second.Summary && p.First.Image == p.Second.Image
                                                 && p.First.Done == p.Second.Done && p.First.Start == p.Second.Start);

    private static IReadOnlyList<NewsItem> LoadKept()
    {
        try
        {
            return File.Exists(CachePath) ? Parse(File.ReadAllText(CachePath)) : Array.Empty<NewsItem>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<NewsItem>();
        }
    }

    /// <summary>An official notice in full (its blocks), from the website; our posts come with theirs. Null when it cannot be read now.</summary>
    public static async Task<IReadOnlyList<NewsBlock>?> ArticleAsync(NewsItem item)
    {
        if (item.Blocks is { Count: > 0 } own)
        {
            return own;
        }

        if (!OfficialId.IsMatch(item.Id))
        {
            return null;
        }

        lock (Gate)
        {
            if (Articles.TryGetValue(item.Id, out IReadOnlyList<NewsBlock>? kept))
            {
                return kept;
            }
        }

        try
        {
            using HttpResponseMessage response = await Http.GetAsync(ListUrl + "/" + item.Id);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("blocks", out JsonElement blocks) || ParseBlocks(blocks) is not { Count: > 0 } read)
            {
                return null;
            }

            lock (Gate)
            {
                Articles[item.Id] = read;
            }

            return read;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static readonly Regex OfficialId = new("^[a-f0-9]{24}$", RegexOptions.CultureInvariant);
    private static readonly Regex PostId = new("^[a-z0-9-]{1,60}$", RegexOptions.CultureInvariant);
    private static readonly Regex SitePicture = new(@"^/app/news/[a-z0-9-]{1,60}\.(jpg|png)$", RegexOptions.CultureInvariant);
    private static readonly Regex PageId = new(@"^[a-z][a-z.]{0,39}$", RegexOptions.CultureInvariant);

    /// <summary>Where a picture of the news may come from: the website's own (/app/news/…) and, in an official notice, the game's own hosts.</summary>
    public static string? PictureUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 500)
        {
            return null;
        }

        if (raw.StartsWith('/'))
        {
            return SitePicture.IsMatch(raw) ? Brand.Site + raw : null;
        }

        return Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0
               && (uri.Host.EndsWith(".plaync.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".playnccdn.com", StringComparison.OrdinalIgnoreCase)
                   || uri.Host.EndsWith(".ncsoft.com", StringComparison.OrdinalIgnoreCase))
            ? uri.AbsoluteUri
            : null;
    }

    /// <summary>Where a news link may go: a page of the website, or the game's official site.</summary>
    public static string? LinkUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 500)
        {
            return null;
        }

        if (raw.StartsWith('/') && !raw.StartsWith("//", StringComparison.Ordinal))
        {
            return Brand.Site + raw;
        }

        return raw.StartsWith("https://aion2.plaync.com/", StringComparison.Ordinal) || Brand.IsSiteUrl(raw) ? raw : null;
    }

    /// <summary>The website's list read into items (newest first, at most 12); anything that does not read as one is left out.</summary>
    internal static List<NewsItem> Parse(string json)
    {
        var items = new List<NewsItem>();
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("items", out JsonElement list) || list.ValueKind != JsonValueKind.Array)
            {
                return items;
            }

            foreach (JsonElement x in list.EnumerateArray())
            {
                if (x.ValueKind != JsonValueKind.Object || Text(x, "id") is not { } id || Text(x, "title", 200) is not { } title
                    || !DateTime.TryParse(Text(x, "posted"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out DateTime posted))
                {
                    continue;
                }

                string source = Text(x, "source") == "game" ? "game" : "atreia";
                if (!(source == "game" ? OfficialId : PostId).IsMatch(id) || items.Any(i => i.Id == id))
                {
                    continue;
                }

                var points = new List<string>();
                if (x.TryGetProperty("points", out JsonElement p) && p.ValueKind == JsonValueKind.Array)
                {
                    points.AddRange(p.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => Cut(e.GetString()!, 240)).Where(s => s.Length > 0).Take(6));
                }

                DateTime? start = DateTime.TryParse(Text(x, "start"), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out DateTime s) ? s : null;
                int? minutes = x.TryGetProperty("minutes", out JsonElement m) && m.ValueKind == JsonValueKind.Number && m.TryGetInt32(out int mm) && mm is > 0 and <= 2880 ? mm : null;
                string? page = Text(x, "page") is { } pg && PageId.IsMatch(pg) ? pg : null;
                items.Add(new NewsItem(
                    id, source, Text(x, "tag", 30) ?? (source == "game" ? "Announcement" : "Atreia App"), title, Text(x, "summary", 400) ?? "", posted,
                    PictureUrl(Text(x, "image")), LinkUrl(Text(x, "url")), page, page is null ? null : Text(x, "pageLabel", 40), points,
                    source == "atreia" && x.TryGetProperty("blocks", out JsonElement b) ? ParseBlocks(b) : null,
                    start, minutes, x.TryGetProperty("done", out JsonElement d) && d.ValueKind == JsonValueKind.True));
                if (items.Count >= 12)
                {
                    break;
                }
            }
        }
        catch (JsonException)
        {
            items.Clear();
        }

        items.Sort((a, b) => b.Posted.CompareTo(a.Posted));
        return items;
    }

    internal static List<NewsBlock> ParseBlocks(JsonElement blocks)
    {
        var read = new List<NewsBlock>();
        if (blocks.ValueKind != JsonValueKind.Array)
        {
            return read;
        }

        foreach (JsonElement b in blocks.EnumerateArray())
        {
            if (read.Count >= 600 || b.ValueKind != JsonValueKind.Object)
            {
                break;
            }

            switch (Text(b, "k"))
            {
                case "row" when b.TryGetProperty("c", out JsonElement c) && c.ValueKind == JsonValueKind.Array:
                    var cells = c.EnumerateArray().Take(8).Select(e => e.ValueKind == JsonValueKind.String ? Cut(e.GetString()!, 400) : "").ToList();
                    if (cells.Any(cell => cell.Length > 0))
                    {
                        read.Add(new NewsBlock("row", "", cells));
                    }

                    break;
                case "img" when PictureUrl(Text(b, "t")) is { } picture:
                    read.Add(new NewsBlock("img", picture, Array.Empty<string>()));
                    break;
                case "h" or "p" or "li" when Text(b, "t", 3000) is { } text:
                    read.Add(new NewsBlock(Text(b, "k")!, text, Array.Empty<string>()));
                    break;
            }
        }

        return read;
    }

    private static string? Text(JsonElement x, string name, int max = 500) =>
        x.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String && Cut(v.GetString()!, max) is { Length: > 0 } s ? s : null;

    private static string Cut(string s, int max)
    {
        s = s.Trim();
        return s.Length <= max ? s : s[..(max - 1)].TrimEnd() + "…";
    }
}
