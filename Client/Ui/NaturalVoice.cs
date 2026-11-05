using System.IO;
using System.Text.Json;

namespace AionDPS.Ui;

/// <summary>
/// The recorded voice (5 November 2026): every phrase the meter says in English - each class skill's "... ready", every named boss skill, the words said
/// around it ("on you", "on everyone", "lethal", "Attention"), the attack shapes and the test samples - spoken once by an open speech model (Kokoro-82M,
/// Apache-2.0; Tools/voice and the Voice workflow) and shipped as small clips in assets/voice/&lt;voice&gt;/ with an index.json of phrase to file. It
/// sounds like a person, not like the Windows voices; a phrase it has no clip for is said by an English Windows voice instead.
/// </summary>
public static class NaturalVoice
{
    /// <summary>What a recorded voice's id starts with, in <see cref="MeterSettings.SpeechVoiceId"/> next to the Windows voices' ids.</summary>
    public const string Prefix = "natural:";

    /// <summary>The voice recorded so far (the folder assets/voice/en-heart).</summary>
    public const string Heart = Prefix + "en-heart";

    /// <summary>The language its words are in (a tag as the Windows voices carry it).</summary>
    public const string Language = "en-US";

    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Indexes = new(StringComparer.Ordinal);
    private static readonly object Gate = new();

    /// <summary>A recorded voice, not a Windows one.</summary>
    public static bool Is(string? voiceId) => voiceId is not null && voiceId.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>The recorded voices that are there (their clips came with the program), for the voice lists.</summary>
    public static IReadOnlyList<SpeechVoice> Voices() =>
        Index(Heart).Count > 0 ? new[] { new SpeechVoice(Heart, Language, "Heart (" + LocalizationManager.Instance["Settings.Speech.Natural"] + ")", true) } : Array.Empty<SpeechVoice>();

    /// <summary>How a phrase is looked up, as Tools/voice/render.py writes it: lower case, commas as spaces, single spaces, no end punctuation.</summary>
    public static string Key(string text)
    {
        string[] words = text.ToLowerInvariant().Replace(',', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words).Trim(' ', '.', '!', '?');
    }

    /// <summary>Whether the voice has every one of these phrases (an empty phrase is a short pause and always there).</summary>
    public static bool Has(string voiceId, IEnumerable<string> phrases) => Clips(voiceId, phrases) is not null;

    /// <summary>The files to play for these phrases one after the other (an empty phrase is a short pause), or null when one is missing.</summary>
    public static IReadOnlyList<string>? Clips(string voiceId, IEnumerable<string> phrases)
    {
        IReadOnlyDictionary<string, string> index = Index(voiceId);
        if (index.Count == 0)
        {
            return null;
        }

        string folder = Folder(voiceId);
        var files = new List<string>();
        foreach (string phrase in phrases)
        {
            string key = Key(phrase);
            if (key.Length == 0)
            {
                string pause = Path.Combine(folder, "pause.mp3");
                if (files.Count > 0 && File.Exists(pause))
                {
                    files.Add(pause);
                }

                continue;
            }

            if (!index.TryGetValue(key, out string? file))
            {
                return null;
            }

            files.Add(Path.Combine(folder, file));
        }

        // a pause at the end is not heard
        while (files.Count > 0 && files[^1].EndsWith("pause.mp3", StringComparison.Ordinal))
        {
            files.RemoveAt(files.Count - 1);
        }

        return files.Count > 0 ? files : null;
    }

    private static string Folder(string voiceId) => Path.Combine(AppContext.BaseDirectory, "assets", "voice", voiceId[Prefix.Length..]);

    private static IReadOnlyDictionary<string, string> Index(string voiceId)
    {
        lock (Gate)
        {
            if (Indexes.TryGetValue(voiceId, out var known))
            {
                return known;
            }

            var index = new Dictionary<string, string>(StringComparer.Ordinal);
            string name = Is(voiceId) ? voiceId[Prefix.Length..] : "";
            // only a plain folder name: the id comes from the settings file
            if (name.Length > 0 && name.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-'))
            {
                try
                {
                    string path = Path.Combine(Folder(voiceId), "index.json");
                    if (File.Exists(path))
                    {
                        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
                        if (doc.RootElement.TryGetProperty("clips", out JsonElement clips) && clips.ValueKind == JsonValueKind.Object)
                        {
                            foreach (JsonProperty clip in clips.EnumerateObject())
                            {
                                if (clip.Value.GetString() is { } file && file.Length > 0 && !file.Contains('/') && !file.Contains('\\'))
                                {
                                    index[clip.Name] = file;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    index.Clear(); // a broken index: the Windows voice says it all
                }
            }

            Indexes[voiceId] = index;
            return index;
        }
    }
}
