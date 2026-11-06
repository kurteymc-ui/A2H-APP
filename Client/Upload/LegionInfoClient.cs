using System.Net.Http;
using System.Text.Json;

namespace AionDPS.Upload;

/// <summary>A Discord server that follows the legion with the Atreia bot (/guild legion): its name (null when it hid the legion), whether it takes
/// applications, the invite, language and hours its public page shows, how many of the legion's members are linked there.</summary>
public sealed record LegionDiscord(string? Name, bool Recruiting, string? Invite, string? Language, string? Hours, int Linked);

/// <summary>What the bot has of a legion: the servers following it, whether the player is a proven member of it, the members linked on Discord (only for a
/// member; else null), and the address to add the bot to a server.</summary>
public sealed record LegionInfo(string Legion, IReadOnlyList<LegionDiscord> Servers, bool Member, IReadOnlyList<string>? Linked, string AddUrl);

/// <summary>GET /meter/api/legion?serverId=&amp;legion= (the site's src/legion-feed.js meterLegionInfo): signed in or not; signed in, the members' links come too.</summary>
public static class LegionInfoClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    public const string AddUrl = "https://atreia.app/discord/server?add=1";

    public static async Task<LegionInfo?> GetAsync(string legion, int serverId)
    {
        if (legion.Length == 0 || serverId <= 0 || Ui.PrivacyConsent.Restricted)
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{Brand.Api}/api/legion?serverId={serverId}&legion={Uri.EscapeDataString(legion)}");
            if (Account.Token is { } token)
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }

            using HttpResponseMessage r = await Http.SendAsync(request).ConfigureAwait(false);
            if (!r.IsSuccessStatusCode)
            {
                return null;
            }

            return Parse(await r.Content.ReadAsStringAsync().ConfigureAwait(false), legion);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    public static LegionInfo? Parse(string body, string legion)
    {
        using JsonDocument doc = JsonDocument.Parse(body);
        JsonElement root = doc.RootElement;
        if (!root.TryGetProperty("ok", out JsonElement ok) || ok.ValueKind != JsonValueKind.True)
        {
            return null;
        }

        static string? Text(JsonElement o, string k) => o.TryGetProperty(k, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var servers = new List<LegionDiscord>();
        if (root.TryGetProperty("servers", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement s in list.EnumerateArray())
            {
                servers.Add(new LegionDiscord(Text(s, "name"), s.TryGetProperty("recruiting", out JsonElement rec) && rec.ValueKind == JsonValueKind.True,
                    Text(s, "invite") is { } invite && invite.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? invite : null, Text(s, "lang"), Text(s, "hours"),
                    s.TryGetProperty("linked", out JsonElement n) && n.TryGetInt32(out int linked) ? linked : 0));
            }
        }

        List<string>? names = root.TryGetProperty("linked", out JsonElement l) && l.ValueKind == JsonValueKind.Array
            ? l.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList() : null;
        return new LegionInfo(Text(root, "legion") ?? legion, servers, root.TryGetProperty("member", out JsonElement m) && m.ValueKind == JsonValueKind.True, names,
            Text(root, "add") is { } add && add.StartsWith("https://atreia.app/", StringComparison.Ordinal) ? add : AddUrl);
    }
}
