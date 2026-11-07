using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AionDPS.Update;

/// <summary>One change of a version, as players read it: what kind (new, improved, fixed), where (Meter, Reports...), a title, the text, an example.</summary>
public sealed record UpdateItem(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("area")] string Area,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("example")] string? Example = null);

/// <summary>A released version's notes: its number, its day, a headline for the bigger ones, its changes.</summary>
public sealed record UpdateRelease(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("date")] string? Date,
    [property: JsonPropertyName("headline")] string? Headline,
    [property: JsonPropertyName("items")] IReadOnlyList<UpdateItem> Items);

/// <summary>
/// The update notes (7 November 2026, the owner: "every time there's an update, instead of just showing update now, a drop down of all the changes,
/// nicely written, with examples"). They are written by hand in assets/changelog/updates.json, one entry per released version. The update prompt
/// reads the newest copy from the repository (an installed version doesn't have the notes of the next one yet), the copy shipped with the app
/// when that can't be reached; the notes shown are those after the installed version up to the one offered. After an update the shipped copy is
/// enough: it has the notes of the version that is now running.
/// </summary>
public static class UpdateNotes
{
    /// <summary>The newest notes, as written on the main branch.</summary>
    public const string RemoteUrl = "https://raw.githubusercontent.com/kurteymc-ui/Atreia-App/main/Client/assets/changelog/updates.json";

    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(8) };

    private static readonly string BundledPath = Path.Combine(AppContext.BaseDirectory, "assets", "changelog", "updates.json");

    private sealed record NotesFile([property: JsonPropertyName("versions")] List<UpdateRelease>? Versions);

    /// <summary>Reads the notes from their JSON; an unreadable file gives none.</summary>
    public static IReadOnlyList<UpdateRelease> Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<NotesFile>(json)?.Versions?.Where(v => v.Items is { Count: > 0 } && !string.IsNullOrWhiteSpace(v.Version)).ToList()
                ?? new List<UpdateRelease>();
        }
        catch (JsonException)
        {
            return Array.Empty<UpdateRelease>();
        }
    }

    /// <summary>The notes shipped with this build.</summary>
    public static IReadOnlyList<UpdateRelease> Bundled()
    {
        try
        {
            return System.IO.File.Exists(BundledPath) ? Parse(System.IO.File.ReadAllText(BundledPath)) : Array.Empty<UpdateRelease>();
        }
        catch (IOException)
        {
            return Array.Empty<UpdateRelease>();
        }
    }

    /// <summary>The newest notes from the repository, the shipped ones when it can't be reached (or says nothing readable).</summary>
    public static async Task<IReadOnlyList<UpdateRelease>> LatestAsync()
    {
        try
        {
            string json = await Http.GetStringAsync(RemoteUrl).ConfigureAwait(false);
            IReadOnlyList<UpdateRelease> remote = Parse(json);
            if (remote.Count > 0)
            {
                return remote;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            // offline, or GitHub busy: the shipped notes
        }

        return Bundled();
    }

    /// <summary>The releases after <paramref name="from"/> up to and with <paramref name="to"/>, newest first.</summary>
    public static IReadOnlyList<UpdateRelease> Between(IReadOnlyList<UpdateRelease> all, Version from, Version to) =>
        all.Select(r => (Release: r, Version: AppVersion.Parse(r.Version)))
            .Where(x => x.Version > from && x.Version <= to)
            .OrderByDescending(x => x.Version)
            .Select(x => x.Release)
            .ToList();
}
