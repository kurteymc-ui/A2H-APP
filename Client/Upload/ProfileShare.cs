using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using AionDPS.Aion2;
using AionDPS.Ui;

namespace AionDPS.Upload;

/// <summary>One character as the website keeps it for the player window: name, server, class, level, Genus Insight (species id, level, lines: page, slot, stat,
/// value, grade) and pets (id, level).</summary>
public sealed record ProfileCharacter(string Name, int ServerId, int ClassCode, int Level, IReadOnlyList<ProfileGenus> Genus, IReadOnlyList<int[]> Pets, long At);

public sealed record ProfileGenus(int Id, int Level, IReadOnlyList<long[]> Fx);

/// <summary>
/// What only the app knows of the character played (6 November 2026, the owner: the player window shows "all details of the person if they are using our app;
/// if they are not, genus/pets are hidden, since we cannot get that info any other way"): its Genus Insight and its pets, sent to atreia.app while the app is
/// signed in and Settings' "Show my Genus and pets" (<see cref="MeterSettings.ShareProfile"/>) is on, after the privacy terms. Read from the character file
/// the meter keeps of the last login (aion2-character.json) a little after it changes; the same content is not sent twice. Switched off, the website forgets
/// what was sent (<see cref="ForgetAsync"/>).
/// </summary>
public static class ProfileShare
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly Dictionary<string, string> Sent = new(StringComparer.Ordinal);
    private static System.Threading.Timer? _timer;
    private static DateTime _fileAt = DateTime.MinValue;
    private static int _busy;

    public static void Start()
    {
        if (_timer is not null)
        {
            return;
        }

        Account.Changed += () =>
        {
            lock (Sent)
            {
                Sent.Clear(); // another account: everything again
            }

            _fileAt = DateTime.MinValue;
        };
        PrivacyConsent.Changed += () => _fileAt = DateTime.MinValue;
        _timer = new System.Threading.Timer(_ => _ = TickAsync(), null, TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(30));
    }

    /// <summary>The character file's content as the website takes it; null when it holds no Genus Insight and no pets.</summary>
    public static ProfileCharacter? Build(Aion2SavedCharacter c)
    {
        if (c.Name.Length == 0 || c.ServerId <= 0 || (c.Species.Count == 0 && c.Pets.Count == 0))
        {
            return null;
        }

        var genus = c.Species.Where(s => s.Id is > 0 and < 100).Take(8)
            .Select(s => new ProfileGenus(s.Id, s.Level, s.Effects.Take(40).Select(e => new long[] { e.Page, e.Slot, e.Stat, e.Value, Math.Clamp(e.Kind, 0, 9) }).ToList())).ToList();
        var pets = c.Pets.Where(p => p.Id > 0).Take(400).Select(p => new[] { p.Id, Math.Clamp(p.Level, 0, 9) }).ToList();
        return new ProfileCharacter(c.Name, c.ServerId, c.ClassCode, c.Level, genus, pets, new DateTimeOffset(DateTime.SpecifyKind(c.SavedAt, DateTimeKind.Utc)).ToUnixTimeMilliseconds());
    }

    private static async Task TickAsync()
    {
        if (!Account.SignedIn || Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return;
        }

        try
        {
            string path = Aion2CharacterStore.DefaultPath;
            DateTime at = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            if (at == _fileAt || DateTime.UtcNow - at < TimeSpan.FromSeconds(20))
            {
                return; // nothing new, or still being written at the login
            }

            if (!MeterSettings.Load().ShareProfile || !PrivacyConsent.CanUpload || UploadClient.IsBlocked)
            {
                _fileAt = at;
                return;
            }

            if (Aion2CharacterStore.Load(path) is not { } saved || Build(saved) is not { } character || Account.Token is not { } token)
            {
                _fileAt = at;
                return;
            }

            string key = character.Name + "|" + character.ServerId;
            string body = JsonSerializer.Serialize(character with { At = 0 }, Json);
            lock (Sent)
            {
                if (Sent.TryGetValue(key, out string? before) && before == body)
                {
                    _fileAt = at;
                    return;
                }
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{UploadClient.ApiBaseUrl}/api/profile")
            {
                Content = JsonContent.Create(new { v = 1, characters = new[] { character } }, options: Json),
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using HttpResponseMessage response = await Http.SendAsync(request);
            if (response.IsSuccessStatusCode || (int)response.StatusCode is 400 or 401 or 413)
            {
                _fileAt = at; // sent, or refused for good: tried again at the next change
                if (response.IsSuccessStatusCode)
                {
                    lock (Sent)
                    {
                        Sent[key] = body;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException or UnauthorizedAccessException)
        {
            // tried again at the next tick
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    /// <summary>"Show my Genus and pets" switched off: the website forgets what this account sent. False when it couldn't be asked (not signed in, no answer).</summary>
    public static async Task<bool> ForgetAsync()
    {
        if (Account.Token is not { } token)
        {
            return false;
        }

        lock (Sent)
        {
            Sent.Clear();
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, $"{UploadClient.ApiBaseUrl}/api/profile");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using HttpResponseMessage response = await Http.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }
}
