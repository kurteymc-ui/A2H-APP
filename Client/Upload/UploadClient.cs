using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AionDPS.Upload;

/// <summary>
/// Sends an encounter to Atreia (atreia.app/meter), signed in as the player (<see cref="Account"/>): without a sign-in
/// nothing is sent. One static HttpClient for the
/// process's lifetime, per the usual .NET guidance (a fresh client per call exhausts sockets under
/// load) - not a concern here at this call volume, but free to get right.
/// </summary>
public static class UploadClient
{
    internal const string ApiBaseUrl = Brand.Api;

    /// <summary>Atreia keeps boss fights; character profiles without a fight are not kept (yet), so the meter does not send them.</summary>
    public static bool TakesProfiles => false;

    private const string NotSignedIn = "Sign in first: Atreia account (bottom of the left column) → Sign in with Discord.";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    // WhenWritingNull is load-bearing, not cosmetic: the backend's zod schema marks optional fields
    // like serverName as .optional() (accepts a MISSING key) rather than .nullable() (accepts an
    // explicit null) - without this, a C# `null` (e.g. ServerName when nobody set a display name)
    // would serialize as a literal "null" in the JSON body and the whole upload would fail schema
    // validation, not just have that one field come through empty.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Set once the community server has said this client is too old to upload (HTTP 426, or the start-up check): from then on no
    /// upload is even tried, until the meter has been updated and restarted. Holds the version the server asks for.</summary>
    public static string? RequiredVersion { get; private set; }

    /// <summary>True while uploads are switched off because the client is outdated.</summary>
    public static bool IsBlocked => RequiredVersion is not null;

    /// <summary>Raised once when the client turns out to be outdated (with the version it should be). Not raised on the UI thread.</summary>
    public static event Action<string>? ClientOutdated;

    /// <summary>Raised when a block turns out to be gone (the server allows this version after all), so the UI can take its warning back.</summary>
    public static event Action? ClientAllowed;

    private static DateTime _lastRecheck = DateTime.MinValue;

    private static void Unblock()
    {
        if (RequiredVersion is not null)
        {
            RequiredVersion = null;
            ClientAllowed?.Invoke();
        }
    }

    /// <summary>While blocked, asks the server again now and then (at most every 30 minutes), so a block that no longer applies is lifted
    /// without a restart.</summary>
    private static async Task RecheckIfDueAsync()
    {
        if (DateTime.UtcNow - _lastRecheck < TimeSpan.FromMinutes(30) || string.IsNullOrEmpty(_checkedVersion))
        {
            return;
        }

        _lastRecheck = DateTime.UtcNow;
        await CheckPolicyAsync(_checkedVersion);
    }

    private static string? _checkedVersion;

    private static void Block(string requiredVersion)
    {
        bool first = RequiredVersion is null;
        RequiredVersion = string.IsNullOrWhiteSpace(requiredVersion) ? "?" : requiredVersion;
        if (first)
        {
            ClientOutdated?.Invoke(RequiredVersion);
        }
    }

    private static UploadResult BlockedResult() =>
        UploadResult.Failed("Old client version, please update (" + RequiredVersion + "). No uploads are possible until you do.");

    /// <summary>The server's answer to an upload: 426 Upgrade Required switches the uploads off. Returns the failure to report.</summary>
    private static async Task<UploadResult> FailureOf(HttpResponseMessage response, string? usedToken)
    {
        string body = await response.Content.ReadAsStringAsync();
        if ((int)response.StatusCode == 401)
        {
            Account.Rejected(usedToken);
            return UploadResult.Failed(NotSignedIn);
        }

        if ((int)response.StatusCode == 426)
        {
            string required = "?";
            try
            {
                using JsonDocument doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("requiredVersion", out JsonElement v) && v.ValueKind == JsonValueKind.String)
                {
                    required = v.GetString() ?? "?";
                }
            }
            catch (JsonException)
            {
                // an answer we cannot read still means "update"
            }

            Block(required);
            return BlockedResult();
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out JsonElement e) && e.ValueKind == JsonValueKind.String)
            {
                return UploadResult.Failed(Truncate(e.GetString()!, 200));
            }
        }
        catch (JsonException)
        {
            // not the site's own answer (a proxy, an outage): the raw text below
        }

        return UploadResult.Failed($"{(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(body, 200)}");
    }

    /// <summary>One signed-in POST of a JSON body to the site. The caller has checked <see cref="Account.SignedIn"/>.</summary>
    private static async Task<UploadResult> PostAsync(string path, HttpContent content, bool wantsId = false)
    {
        string? token = Account.Token;
        if (token is null)
        {
            return UploadResult.Failed(NotSignedIn);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}{path}") { Content = content };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return await FailureOf(response, token);
        }

        if (!wantsId)
        {
            return UploadResult.Ok;
        }

        (int? id, string? url) = await AnswerOfAsync(response);
        LastFightUrl = url ?? LastFightUrl;
        return UploadResult.OkWith(id, url);
    }

    /// <summary>A player's rank in an uploaded fight, as its page shows it: better than <paramref name="Percent"/> of the <paramref name="Of"/> logs of the same class on
    /// the boss (in the 10k Combat Power bracket <paramref name="Low"/> to <paramref name="High"/> when the site knows it, else over all).</summary>
    public sealed record FightRank(double Percent, int Place, int Of, int? Low = null, int? High = null);

    /// <summary>
    /// The ranks of an uploaded fight (its page's address), by player name, and how many logs of a class the site wants before a rank means something;
    /// null when the site can't be reached or doesn't have the fight. A player the site didn't rank is missing from the list.
    /// </summary>
    public static async Task<(int Min, IReadOnlyDictionary<string, FightRank> Ranks)?> GetRanksAsync(string pageUrl)
    {
        string slug = pageUrl.TrimEnd('/').Split('/').LastOrDefault() ?? "";
        if (!System.Text.RegularExpressions.Regex.IsMatch(slug, "^[a-z0-9]{10}$"))
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/api/ranks?slug={slug}");
            if (Account.Token is { } token)
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);   // a private fight of one's own
            }

            using HttpResponseMessage response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            int min = doc.RootElement.TryGetProperty("min", out JsonElement m) && m.TryGetInt32(out int mv) ? mv : 10;
            var ranks = new Dictionary<string, FightRank>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonElement r in doc.RootElement.GetProperty("ranks").EnumerateArray())
            {
                if (r.TryGetProperty("name", out JsonElement n) && n.GetString() is { } name && r.TryGetProperty("rank", out JsonElement rank) && rank.ValueKind == JsonValueKind.Object)
                {
                    // the 10k Combat Power bracket when the site has one, else over all (as the fight's page)
                    bool inBracket = rank.TryGetProperty("br", out JsonElement br) && br.ValueKind == JsonValueKind.Object;
                    JsonElement pick = inBracket ? br : rank.GetProperty("all");
                    int? low = inBracket && br.TryGetProperty("lo", out JsonElement lo) && lo.TryGetInt32(out int lv) ? lv : null;
                    int? high = inBracket && br.TryGetProperty("hi", out JsonElement hi) && hi.TryGetInt32(out int hv) ? hv : null;
                    ranks[name] = new FightRank(pick.GetProperty("pct").GetDouble(), pick.GetProperty("rank").GetInt32(), pick.GetProperty("n").GetInt32(), low, high);
                }
            }

            return (min, ranks);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>The page of the fight uploaded last (atreia.app/meter/logs/...), for "open it".</summary>
    public static string? LastFightUrl { get; private set; }

    /// <summary>Asks the community server right at start-up whether this version may upload, so a client that is too old learns it at once
    /// and not with its first fight. A server that cannot be reached changes nothing (the upload itself is checked as well).</summary>
    public static async Task CheckPolicyAsync(string version)
    {
        _checkedVersion = version;
        try
        {
            using HttpClient quick = new() { Timeout = TimeSpan.FromSeconds(8) };
            using JsonDocument doc = JsonDocument.Parse(await quick.GetStringAsync($"{ApiBaseUrl}/api/client-policy?version={Uri.EscapeDataString(version)}"));
            if (doc.RootElement.TryGetProperty("allowed", out JsonElement allowed) && allowed.ValueKind == JsonValueKind.False)
            {
                string required = doc.RootElement.TryGetProperty("requiredVersion", out JsonElement r) && r.ValueKind == JsonValueKind.String ? r.GetString() ?? "?" : "?";
                Block(required);
            }
            else if (doc.RootElement.TryGetProperty("allowed", out JsonElement ok) && ok.ValueKind == JsonValueKind.True)
            {
                Unblock();
            }
        }
        catch (Exception)
        {
            // offline or an older server: nothing to decide here
        }
    }

    /// <summary>Posts one encounter. Never throws - on any network/server failure a failed upload
    /// must not interrupt whatever the user is doing with a running boss fight. <see cref="UploadResult.Error"/>
    /// carries the actual reason (an HTTP status/body, or the exception message) so the UI can show
    /// something more useful than a blanket "unreachable", which was misleading for e.g. a rejected
    /// payload - per the user, not knowing whether/why an upload failed was itself the problem.</summary>
    public static async Task<UploadResult> SendAsync(EncounterUploadRequest payload)
    {
        if (!Ui.PrivacyConsent.CanUpload)
        {
            return UploadResult.Failed("The privacy policy is not accepted: nothing is uploaded.");
        }

        if (IsBlocked)
        {
            await RecheckIfDueAsync();
        }

        if (IsBlocked)
        {
            return BlockedResult();
        }

        try
        {
            UploadResult result = await PostAsync("/api/uploads", JsonContent.Create(payload, options: JsonOptions), wantsId: true);
            if (result.Success && result.Url is { } url)
            {
                // the fight's page goes with its History entry, for the report window's "Open on the website" and "Copy link"
                History.FightLog.RememberUrl(payload.BossNpcId, payload.StartedAt, url);
            }

            return result;
        }
        catch (Exception ex)
        {
            return UploadResult.Failed(ex.Message);
        }
    }

    /// <summary>The fight's id and page on the site from the answer of a boss upload (nulls when the answer names none).</summary>
    private static async Task<(int? Id, string? Url)> AnswerOfAsync(HttpResponseMessage response)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            JsonElement root = doc.RootElement;
            int? id = root.TryGetProperty("encounterId", out JsonElement i) && i.ValueKind == JsonValueKind.Number && i.TryGetInt32(out int n) ? n : null;
            string? url = root.TryGetProperty("url", out JsonElement u) && u.ValueKind == JsonValueKind.String && Brand.IsSiteUrl(u.GetString()) ? u.GetString() : null;
            return (id, url);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return (null, null);
        }
    }

    /// <summary>Posts the paths of a boss fight for the replay (see <see cref="ReplayUploadRequest"/>). Same never-throws contract as <see cref="SendAsync"/>.</summary>
    public static async Task<UploadResult> SendReplayAsync(ReplayUploadRequest payload)
    {
        if (!Ui.PrivacyConsent.CanUpload)
        {
            return UploadResult.Failed("The privacy policy is not accepted: nothing is uploaded.");
        }

        if (IsBlocked)
        {
            return BlockedResult();
        }

        try
        {
            return await PostAsync("/api/uploads/replay", JsonContent.Create(payload, options: JsonOptions));
        }
        catch (Exception ex)
        {
            return UploadResult.Failed(ex.Message);
        }
    }

    /// <summary>The paths the clients of a boss fight sent for the replay, merged by the server; empty when there are none or the server cannot be reached.</summary>
    public static async Task<IReadOnlyList<ServerReplayTrack>> GetReplayAsync(string serverFingerprint, int bossNpcId, DateTime startedAtUtc)
    {
        if (!Ui.PrivacyConsent.CanUpload)
        {
            return Array.Empty<ServerReplayTrack>();
        }

        try
        {
            string url = $"{ApiBaseUrl}/api/replay?server={Uri.EscapeDataString(serverFingerprint)}&bossNpcId={bossNpcId}&start={Uri.EscapeDataString(startedAtUtc.ToString("o"))}";
            using JsonDocument doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            var list = new List<ServerReplayTrack>();
            if (doc.RootElement.TryGetProperty("tracks", out JsonElement tracks))
            {
                foreach (JsonElement t in tracks.EnumerateArray())
                {
                    list.Add(new ServerReplayTrack(
                        t.GetProperty("kind").GetString() ?? "", t.GetProperty("name").GetString() ?? "",
                        t.TryGetProperty("className", out JsonElement c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null, t.GetProperty("startMs").GetInt64(),
                        t.GetProperty("t").EnumerateArray().Select(v => v.GetInt32()).ToArray(), t.GetProperty("x").EnumerateArray().Select(v => v.GetInt32()).ToArray(),
                        t.GetProperty("y").EnumerateArray().Select(v => v.GetInt32()).ToArray(), t.TryGetProperty("sources", out JsonElement s) ? s.GetInt32() : 1));
                }
            }

            return list;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or KeyNotFoundException or InvalidOperationException)
        {
            return Array.Empty<ServerReplayTrack>();
        }
    }

    /// <summary>Posts Aion 2 character profiles that have no boss fight to attach to. Same
    /// never-throws contract as <see cref="SendAsync"/>.</summary>
    public static async Task<UploadResult> SendProfilesAsync(ProfilesUploadRequest payload)
    {
        if (!TakesProfiles)
        {
            return UploadResult.Failed("Atreia keeps boss fights only, not character profiles on their own.");
        }

        if (!Ui.PrivacyConsent.CanUpload)
        {
            return UploadResult.Failed("The privacy policy is not accepted: nothing is uploaded.");
        }

        if (IsBlocked)
        {
            return BlockedResult();
        }

        try
        {
            return await PostAsync("/api/uploads/profiles", JsonContent.Create(payload, options: JsonOptions));
        }
        catch (Exception ex)
        {
            return UploadResult.Failed(ex.Message);
        }
    }

    /// <summary>Posts the login traffic of a legion member (see <see cref="LoginDumpUploadRequest"/>). Same never-throws contract as <see cref="SendAsync"/>.</summary>
    public static async Task<UploadResult> SendLoginDumpAsync(LoginDumpUploadRequest payload)
    {
        if (!Ui.PrivacyConsent.CanUpload)
        {
            return UploadResult.Failed("The privacy policy is not accepted: nothing is uploaded.");
        }

        if (IsBlocked)
        {
            return BlockedResult();
        }

        try
        {
            return await PostAsync("/api/uploads/login-dump", JsonContent.Create(payload, options: JsonOptions));
        }
        catch (Exception ex)
        {
            return UploadResult.Failed(ex.Message);
        }
    }

    /// <summary>Posts a network report (see Aion2/Capture/NetworkReport.cs) the player has looked at and agreed to send. Same never-throws contract as <see cref="SendAsync"/>.</summary>
    public static Task<UploadResult> SendNetworkReportAsync(string json) => PostJsonAsync("/api/diagnostics/network", json);

    /// <summary>Posts a stat recording (see Aion2/StatRecording.cs) the player has looked at and agreed to send. Same never-throws contract as <see cref="SendAsync"/>.</summary>
    public static Task<UploadResult> SendStatRecordingAsync(string json) => PostJsonAsync("/api/uploads/stat-recording", json);

    /// <summary>
    /// Posts a measured Combat Power change (see Upload/CpShare.cs) <b>without</b> the sign-in: no token goes with it, so the site can't tie it to an
    /// account. Never throws. Retry: true when the site couldn't be reached or was busy (worth one more try later), false when it answered.
    /// </summary>
    public static async Task<(UploadResult Result, bool Retry)> SendCpChangeAsync(string json)
    {
        if (!Ui.PrivacyConsent.CanUpload)
        {
            return (UploadResult.Failed("The privacy policy is not accepted: nothing is uploaded."), false);
        }

        if (IsBlocked)
        {
            await RecheckIfDueAsync();
        }

        if (IsBlocked)
        {
            return (BlockedResult(), false);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/api/uploads/cp-change") { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
            using HttpResponseMessage response = await Http.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                return (UploadResult.Ok, false);
            }

            int status = (int)response.StatusCode;
            if (status == 426)
            {
                return (await FailureOf(response, null), false);
            }

            return (UploadResult.Failed($"{status} {response.ReasonPhrase}"), status is 429 or >= 500);
        }
        catch (Exception ex)
        {
            return (UploadResult.Failed(ex.Message), true);
        }
    }

    /// <summary>The map accuracy checks of one map (Upload/MapShare.cs): anonymous, no sign-in; Retry when the site could not take them now.</summary>
    public static async Task<(UploadResult Result, bool Retry)> SendMapPointsAsync(string json)
    {
        if (!Ui.PrivacyConsent.CanUpload)
        {
            return (UploadResult.Failed("The privacy policy is not accepted: nothing is uploaded."), false);
        }

        if (IsBlocked)
        {
            await RecheckIfDueAsync();
        }

        if (IsBlocked)
        {
            return (BlockedResult(), false);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/api/uploads/map-points") { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
            using HttpResponseMessage response = await Http.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                return (UploadResult.Ok, false);
            }

            int status = (int)response.StatusCode;
            if (status == 426)
            {
                return (await FailureOf(response, null), false);
            }

            return (UploadResult.Failed($"{status} {response.ReasonPhrase}"), status is 429 or >= 500);
        }
        catch (Exception ex)
        {
            return (UploadResult.Failed(ex.Message), true);
        }
    }

    private static async Task<UploadResult> PostJsonAsync(string path, string json)
    {
        if (!Ui.PrivacyConsent.CanUpload)
        {
            return UploadResult.Failed("The privacy policy is not accepted: nothing is uploaded.");
        }

        if (IsBlocked)
        {
            await RecheckIfDueAsync();
        }

        if (IsBlocked)
        {
            return BlockedResult();
        }

        try
        {
            return await PostAsync(path, new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        }
        catch (Exception ex)
        {
            return UploadResult.Failed(ex.Message);
        }
    }

    private static string Truncate(string s, int maxLength) =>
        s.Length <= maxLength ? s : s[..maxLength] + "...";
}
