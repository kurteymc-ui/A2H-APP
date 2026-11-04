using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Upload;

/// <summary>One member of the legion as POST /meter/api/legion takes it: the game's name, server number, class, level, Item Level and Combat Power.</summary>
public sealed record LegionMemberUpload(string Name, int ServerId, string? Cls, int? Level, int? Il, int? Cp);

/// <summary>The character played (name and server number) and its legion's member list from the login.</summary>
public sealed record LegionUpload(int V, string Legion, LegionMemberUpload Character, IReadOnlyList<LegionMemberUpload> Members);

/// <summary>How the last send went, for the account window: "sent" (Known members updated on the site, Lookup to be looked up), "later" or "refused".</summary>
public sealed record LegionShareResult(string Legion, int Members, string Kind, int Known = 0, int Lookup = 0);

/// <summary>
/// The legion's member list for the Discord servers that follow the legion (aion2helper.com, /guild legion): at a login the game sends the own legion's
/// list (frame 0x058a: every member's name, server, class, level, Item Level and Combat Power, <see cref="Aion2LegionList"/>). While the meter is signed in
/// (<see cref="Account"/>), <see cref="Ui.MeterSettings.ShareLegionList"/> is on and the character played was verified as the player's
/// (<see cref="CharacterVerify"/>), the list goes to the site (POST /meter/api/legion), which puts every member on those servers' rosters with the game's
/// numbers, so members only have to claim theirs. Only the numbers the game's legion window shows anyway: no last-online time, no zone, no rank.
/// Once per legion per run of the meter; tried again later when the site can't take it (after 5, 15, 45 minutes and so on, at most 3 hours apart).
/// The list must name the character played (it is replaced at every login, so a list left from another character is never sent under this one's legion).
/// </summary>
public static class LegionShare
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly TimeSpan FirstRetry = TimeSpan.FromMinutes(5), LongestRetry = TimeSpan.FromHours(3);
    private static readonly object Gate = new();
    private static readonly HashSet<string> Done = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, (DateTime At, int Tries)> Later = new(StringComparer.Ordinal);
    private static Func<Aion2EntityDirectory?>? _directory;
    private static System.Threading.Timer? _timer;
    private static int _busy;

    /// <summary>The last send's outcome in this run (null before the first one).</summary>
    public static LegionShareResult? Last { get; private set; }

    /// <summary>Raised after a send had an outcome (not on the UI thread).</summary>
    public static event Action? Changed;

    /// <summary>The meter's start: <paramref name="directory"/> gives the live capture's directory (null while there is none); every 30 seconds it is looked at.</summary>
    public static void Start(Func<Aion2EntityDirectory?> directory)
    {
        _directory = directory;
        _timer ??= new System.Threading.Timer(_ => _ = TickAsync(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    private static async Task TickAsync()
    {
        if (!Account.SignedIn || _directory?.Invoke() is not { } directory || Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return;
        }

        try
        {
            if (!Ui.MeterSettings.Load().ShareLegionList || !Ui.PrivacyConsent.CanUpload || UploadClient.IsBlocked
                || Build(directory.LocalCharacter, directory.AccountCharacters(), directory.LegionRoster()) is not { } upload
                || !CharacterVerify.IsVerified(upload.Character.Name, upload.Character.ServerId))
            {
                return;
            }

            string key = upload.Legion + "|" + upload.Character.ServerId;
            lock (Gate)
            {
                if (Done.Contains(key) || (Later.TryGetValue(key, out var later) && DateTime.UtcNow < later.At))
                {
                    return;
                }
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
    /// What is sent, or null while there is nothing to send: the game's own record of the character played (not one restored from an earlier session), its
    /// legion as the login's character list names it, and that legion's list from the login, which must name the character played (same name and server, or
    /// the only member of that name: the lists keep the server a character was made on, see <see cref="Aion2Moved"/>; members on the old server of the
    /// character played go as on its new one).
    /// </summary>
    public static LegionUpload? Build(Aion2CharacterInfo? played, IReadOnlyList<AccountCharacter> account, IReadOnlyList<LegionMember> legion)
    {
        if (played is not { Restored: false } || played.Name.Length == 0 || played.ServerId <= 0 || legion.Count == 0)
        {
            return null;
        }

        string? name = Aion2Moved.Find(account, c => c.Name, c => c.ServerId, played.Name, played.ServerId)?.Legion?.Trim();
        LegionMember? own = Aion2Moved.Find(legion, m => m.Name, m => m.ServerId, played.Name, played.ServerId);
        if (string.IsNullOrEmpty(name) || own is null)
        {
            return null;
        }

        var moved = Aion2Moved.Of(own.ServerId, played.ServerId);
        var members = legion
            .Where(m => m.Name.Length > 0 && m.ServerId > 0)
            .Select(m => m with { ServerId = Aion2Moved.Now(m.ServerId, moved) })
            .GroupBy(m => (m.Name, m.ServerId))
            .Select(g => g.First())
            .Take(300)
            .Select(m => new LegionMemberUpload(m.Name, m.ServerId, Aion2SkillNames.ClassFromCode(m.ClassCode), m.Level > 0 ? m.Level : null,
                m.ItemLevel is > 0 and < 10000 ? m.ItemLevel : null, m.CombatPower is > 0 and < 10_000_000 ? m.CombatPower : null))
            .ToList();
        return new LegionUpload(1, name, new LegionMemberUpload(played.Name, played.ServerId, null, null, null, null), members);
    }

    private static async Task SendAsync(string key, LegionUpload upload)
    {
        string? token = Account.Token;
        if (token is null)
        {
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{UploadClient.ApiBaseUrl}/api/legion")
        {
            Content = JsonContent.Create(upload, options: CharacterVerify.JsonOptions),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        int status;
        string body;
        try
        {
            using HttpResponseMessage response = await Http.SendAsync(request);
            status = (int)response.StatusCode;
            body = await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            PutOff(key, upload);
            return;
        }

        if (status == 401)
        {
            Account.Rejected(token);
            return;
        }

        (bool ok, int known, int lookup, string? code) = Read(body);
        if (status == 200 && ok)
        {
            Finish(key, new LegionShareResult(upload.Legion, upload.Members.Count, "sent", known, lookup));
        }
        else if (code is "INVALID_INPUT" or "LEGION_MISMATCH")
        {
            // the site's refusals stand for this run (a legion the site has under another name is read again there every day)
            Finish(key, new LegionShareResult(upload.Legion, upload.Members.Count, "refused"));
        }
        else
        {
            PutOff(key, upload);
        }
    }

    private static void Finish(string key, LegionShareResult result)
    {
        lock (Gate)
        {
            Done.Add(key);
            Later.Remove(key);
        }

        Last = result;
        Changed?.Invoke();
    }

    /// <summary>Tried again later: 5 minutes after the first failure, three times as long after each next one, at most 3 hours.</summary>
    private static void PutOff(string key, LegionUpload upload)
    {
        lock (Gate)
        {
            int tries = Later.TryGetValue(key, out var was) ? was.Tries + 1 : 1;
            double minutes = Math.Min(LongestRetry.TotalMinutes, FirstRetry.TotalMinutes * Math.Pow(3, Math.Min(tries - 1, 6)));
            Later[key] = (DateTime.UtcNow.AddMinutes(minutes), tries);
        }

        Last = new LegionShareResult(upload.Legion, upload.Members.Count, "later");
        Changed?.Invoke();
    }

    /// <summary>The site's answer: {ok, kept, known, lookup} or {ok: false, code, error}; an answer that isn't the site's reads as nothing.</summary>
    private static (bool Ok, int Known, int Lookup, string? Code) Read(string body)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (false, 0, 0, null);
            }

            int Number(string property) => root.TryGetProperty(property, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : 0;
            bool ok = root.TryGetProperty("ok", out JsonElement o) && o.ValueKind == JsonValueKind.True;
            string? code = root.TryGetProperty("code", out JsonElement c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            return (ok, Number("known"), Number("lookup"), code);
        }
        catch (JsonException)
        {
            return (false, 0, 0, null);
        }
    }
}
