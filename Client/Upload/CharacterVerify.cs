using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Upload;

/// <summary>One character as the meter's check sends it: the name and the server (its number) as the game names them, the class and level the game
/// showed, and from the login's character list when it was created and its legion (null when the list doesn't say).</summary>
public sealed record VerifyCharacterUpload(string Name, int ServerId, string? Cls, int? Level, long? CreatedMs, string? Legion);

/// <summary>What POST /meter/api/verify takes: the region of the character's server in the site's words ("eu"; null where the server's number doesn't
/// surely tell it), the character logged in with (the game's own record of it), and the account's character list from the login.</summary>
public sealed record VerifyUpload(int V, string? Region, VerifyCharacterUpload Character, IReadOnlyList<VerifyCharacterUpload> Account);

/// <summary>How the last check went, for the account window. Kind: "verified", "mismatch" (the list doesn't fit the site's records), "taken" (someone
/// else proved the character is theirs), "full" (the most characters the meter can verify for one Discord account) or "later" (tried again later).</summary>
public sealed record VerifyResult(string Name, string Server, string Kind);

/// <summary>
/// Verifies the character the player plays as theirs on atreia.app, with no title to show: while the meter is signed in (<see cref="Account"/>),
/// once the game has sent its own record of the character logged in with (not one restored from an earlier session) and the login's character list of
/// that account was seen (a meter started in the middle of a session sees it at the next login), the meter sends both (POST /meter/api/verify). The site
/// checks them against its records and NCSOFT's character search; the character then counts as proven theirs on the site and in the Discord servers
/// with Atreia (their legion's roles), though never over someone else's proof. Once per character per run of the meter. When the site or NCSOFT
/// can't answer it is tried again later, quietly (after 5, 15, 45 minutes and so on, at most 3 hours apart); a refusal (the list doesn't fit, someone
/// else's character) isn't sent again in this run. Only with the privacy policy accepted, and not while the meter is too old to upload.
/// </summary>
public static class CharacterVerify
{
    /// <summary>The most characters of an account's list that are sent (the site takes 40).</summary>
    public const int MaxListed = 40;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) }; // the site asks NCSOFT about the characters it doesn't know
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private static readonly TimeSpan FirstRetry = TimeSpan.FromMinutes(5), LongestRetry = TimeSpan.FromHours(3);

    private static readonly object Gate = new();
    private static readonly HashSet<string> Done = new(StringComparer.Ordinal);
    private static readonly HashSet<string> VerifiedKeys = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, (DateTime At, int Tries)> Later = new(StringComparer.Ordinal);
    private static Func<Aion2EntityDirectory?>? _directory;
    private static System.Threading.Timer? _timer;
    private static string? _signedInWith;
    private static int _busy;

    /// <summary>The last check's outcome in this run (null before the first one, and after signing in to another account or out).</summary>
    public static VerifyResult? Last { get; private set; }

    /// <summary>Raised after a check had an outcome, and when the sign-in changed (not on the UI thread).</summary>
    public static event Action? Changed;

    /// <summary>Raised when a character was verified just now (not when it was verified already): its name and server, for the status line (not on the UI thread).</summary>
    public static event Action<string, string>? Verified;

    /// <summary>The meter's start: <paramref name="directory"/> gives the live capture's directory (null while there is none); every 20 seconds it is looked at.</summary>
    public static void Start(Func<Aion2EntityDirectory?> directory)
    {
        _directory = directory;
        if (_timer is not null)
        {
            return;
        }

        _signedInWith = Account.Token;
        Account.Changed += () =>
        {
            // another account (or signed out): every character again; the name refreshed for the same sign-in changes nothing
            string? now = Account.Token;
            if (now == _signedInWith)
            {
                return;
            }

            _signedInWith = now;
            lock (Gate)
            {
                Done.Clear();
                Later.Clear();
                VerifiedKeys.Clear();
            }

            Last = null;
            Changed?.Invoke();
        };
        _timer = new System.Threading.Timer(_ => _ = TickAsync(), null, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20));
    }

    private static async Task TickAsync()
    {
        if (!Account.SignedIn || _directory?.Invoke() is not { } directory || Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return;
        }

        try
        {
            if (Build(directory.LocalCharacter, directory.AccountCharacters()) is not { } upload)
            {
                return;
            }

            string key = upload.Character.Name + "|" + upload.Character.ServerId;
            lock (Gate)
            {
                if (Done.Contains(key) || (Later.TryGetValue(key, out var later) && DateTime.UtcNow < later.At))
                {
                    return;
                }
            }

            if (!Ui.PrivacyConsent.CanUpload || UploadClient.IsBlocked)
            {
                return;
            }

            await SendAsync(key, upload);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException or InvalidOperationException)
        {
            // nothing to do now: the next tick looks again
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    /// <summary>
    /// What is sent for this login, or null while there is nothing to send: the game's own record of the character played (not one restored from an
    /// earlier session) and the login's character list with that character in it (same name and server; or the only one of that name, since the list
    /// keeps the server a character was made on: see <see cref="Aion2Moved"/>). The other characters of the list go with it when their server was read
    /// (the site checks each one on its server; those listed on the old server of the character played, on its new one), <see cref="MaxListed"/> at most.
    /// </summary>
    public static VerifyUpload? Build(Aion2CharacterInfo? played, IReadOnlyList<AccountCharacter> account)
    {
        if (played is not { Restored: false } || played.Name.Length == 0 || played.ServerId <= 0)
        {
            return null;
        }

        AccountCharacter? listed = Aion2Moved.Find(account, c => c.Name, c => c.ServerId, played.Name, played.ServerId);
        if (listed is null)
        {
            return null;
        }

        var moved = Aion2Moved.Of(listed.ServerId, played.ServerId);
        var list = new List<VerifyCharacterUpload> { Entry(listed with { ServerId = played.ServerId }) };
        foreach (AccountCharacter c in account)
        {
            if (list.Count >= MaxListed)
            {
                break;
            }

            int now = Aion2Moved.Now(c.ServerId, moved);
            if (now > 0 && !ReferenceEquals(c, listed) && !list.Any(x => x.Name == c.Name && x.ServerId == now))
            {
                list.Add(Entry(c with { ServerId = now }));
            }
        }

        // The region only where the server's number surely tells it: so far only Europe's numbers are confirmed with real characters (Aion2Servers), and a
        // wrong region would never find the character. Without one the site looks in every region and takes the character only when one region alone lists it.
        string? region = Aion2Servers.RegionCodeOf(played.ServerId) == "eu" ? "eu" : null;
        var character = new VerifyCharacterUpload(played.Name, played.ServerId, Aion2SkillNames.ClassFromCode(played.ClassCode), played.Level > 0 ? played.Level : null, null, null);
        return new VerifyUpload(1, region, character, list);
    }

    private static VerifyCharacterUpload Entry(AccountCharacter c) =>
        new(c.Name, c.ServerId, Aion2SkillNames.ClassFromCode(c.ClassCode), c.Level > 0 ? c.Level : null, c.CreatedMs > 0 ? c.CreatedMs : null,
            string.IsNullOrWhiteSpace(c.Legion) ? null : c.Legion);

    private static async Task SendAsync(string key, VerifyUpload upload)
    {
        string? token = Account.Token;
        if (token is null)
        {
            return;
        }

        string name = upload.Character.Name, server = ServerName(upload.Character.ServerId);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{UploadClient.ApiBaseUrl}/api/verify")
        {
            Content = JsonContent.Create(upload, options: JsonOptions),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        string body;
        int status;
        try
        {
            using HttpResponseMessage response = await Http.SendAsync(request);
            status = (int)response.StatusCode;
            body = await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            PutOff(key, name, server);
            return;
        }

        if (status == 401)
        {
            Account.Rejected(token); // signed out on the site: the next sign-in starts again (Account.Changed)
            return;
        }

        (bool ok, bool already, string? code, string? shownName, string? shownServer) = Read(body);
        if (status == 200 && ok)
        {
            name = shownName ?? name;
            server = shownServer ?? server;
            Finish(key, new VerifyResult(name, server, "verified"));
            if (!already)
            {
                Verified?.Invoke(name, server);
            }

            return;
        }

        // the site's refusals stand for this run; anything else (NCSOFT busy, a character not listed yet, too many checks, the site down) is asked again later
        string? refused = code switch { "MISMATCH" or "INVALID_INPUT" => "mismatch", "TAKEN" => "taken", "TOO_MANY" => "full", _ => null };
        if (refused is not null)
        {
            Finish(key, new VerifyResult(name, server, refused));
        }
        else
        {
            PutOff(key, name, server);
        }
    }

    /// <summary>Whether this character was verified as the player's in this run (the site said so), for <see cref="LegionShare"/>.</summary>
    public static bool IsVerified(string name, int serverId)
    {
        lock (Gate)
        {
            return VerifiedKeys.Contains(name + "|" + serverId);
        }
    }

    private static void Finish(string key, VerifyResult result)
    {
        lock (Gate)
        {
            Done.Add(key);
            Later.Remove(key);
            if (result.Kind == "verified")
            {
                VerifiedKeys.Add(key);
            }
        }

        Last = result;
        Changed?.Invoke();
    }

    /// <summary>Tried again later: 5 minutes after the first failure, three times as long after each next one, at most 3 hours.</summary>
    private static void PutOff(string key, string name, string server)
    {
        lock (Gate)
        {
            int tries = Later.TryGetValue(key, out var was) ? was.Tries + 1 : 1;
            double minutes = Math.Min(LongestRetry.TotalMinutes, FirstRetry.TotalMinutes * Math.Pow(3, Math.Min(tries - 1, 6)));
            Later[key] = (DateTime.UtcNow.AddMinutes(minutes), tries);
        }

        Last = new VerifyResult(name, server, "later");
        Changed?.Invoke();
    }

    /// <summary>The site's answer: {ok, verified, already, name, server} or {ok: false, code, error}; an answer that isn't the site's reads as nothing.</summary>
    private static (bool Ok, bool Already, string? Code, string? Name, string? Server) Read(string body)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (false, false, null, null, null);
            }

            string? Text(string property) => root.TryGetProperty(property, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            bool ok = root.TryGetProperty("ok", out JsonElement o) && o.ValueKind == JsonValueKind.True
                && root.TryGetProperty("verified", out JsonElement f) && f.ValueKind == JsonValueKind.True;
            bool already = root.TryGetProperty("already", out JsonElement a) && a.ValueKind == JsonValueKind.True;
            return (ok, already, Text("code"), Text("name") is { Length: > 0 } n ? n : null, Text("server") is { Length: > 0 } s ? s : null);
        }
        catch (JsonException)
        {
            return (false, false, null, null, null);
        }
    }

    /// <summary>The server's own name for "Quake (Nemon)": the table's name without its region or number (Aion2Servers.NameOf).</summary>
    private static string ServerName(int serverId)
    {
        string name = Aion2Servers.NameOf(serverId);
        int dash = name.IndexOf(" - ", StringComparison.Ordinal);
        if (dash >= 0)
        {
            return name[(dash + 3)..];
        }

        int bracket = name.IndexOf(" [", StringComparison.Ordinal);
        return bracket > 0 ? name[..bracket] : name;
    }
}
