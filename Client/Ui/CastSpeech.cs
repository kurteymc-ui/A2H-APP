using System.Globalization;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;
using AionDPS.Aion2;

namespace AionDPS.Ui;

/// <summary>One voice the system offers, as the settings list it.</summary>
public sealed record SpeechVoice(string Id, string Language, string Name, bool Female);

/// <summary>What is said: the text for a Windows voice and the same as the phrases the recorded voice has clips for (an empty phrase is a short pause).</summary>
public sealed record Spoken(string Text, IReadOnlyList<string> Phrases)
{
    /// <summary>Several announcements one after the other, with a short pause between them.</summary>
    public static Spoken Join(IReadOnlyList<Spoken> all) =>
        new(string.Join(". ", all.Select(a => a.Text)), all.SelectMany((a, i) => i == 0 ? a.Phrases : a.Phrases.Prepend("")).ToList());
}

/// <summary>
/// Voice output for the boss casts: says each cast once, with the voice the player chose - the recorded natural one (<see cref="NaturalVoice"/>, English)
/// or a Windows voice. It does not depend on the combat overlay (it works with that off) and speaks only while Aion is in the foreground.
/// </summary>
public sealed class CastSpeech
{
    private readonly HashSet<string> _said = new();
    private readonly object _gate = new();
    private int _latest;
    private MediaPlayer? _player;
    private IDisposable? _playing;

    /// <summary>The voices there are, by language and name, the recorded ones first in their language; only those when the speech engine is not there.</summary>
    public static IReadOnlyList<SpeechVoice> Voices()
    {
        IEnumerable<SpeechVoice> windows;
        try
        {
            windows = SpeechSynthesizer.AllVoices.Select(v => new SpeechVoice(v.Id, v.Language, Clean(v.DisplayName), v.Gender == VoiceGender.Female)).ToList();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            windows = Array.Empty<SpeechVoice>();
        }

        return NaturalVoice.Voices().Concat(windows)
            .OrderBy(v => v.Language, StringComparer.OrdinalIgnoreCase).ThenBy(v => NaturalVoice.Is(v.Id) ? 0 : 1)
            .ThenBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>The recorded voice is chosen: the skills' names are said in English (the language it was recorded in).</summary>
    public static bool Natural(MeterSettings s) => NaturalVoice.Is(s.SpeechVoiceId);

    /// <summary>The volume (0 to 1) of an announcement: that of the kind that made it count; several kinds at once give the loudest.</summary>
    public static double Volume(MeterSettings s, bool atYou, bool all, bool lethal)
    {
        int percent = 0;
        if (atYou && s.SpeechOnYou)
        {
            percent = Math.Max(percent, s.SpeechVolumeOnYou);
        }

        if (all && s.SpeechOnAll)
        {
            percent = Math.Max(percent, s.SpeechVolumeOnAll);
        }

        if (lethal && atYou && s.SpeechLethalYou)
        {
            percent = Math.Max(percent, s.SpeechVolumeLethalYou);
        }

        if (lethal && all && s.SpeechLethalAll)
        {
            percent = Math.Max(percent, s.SpeechVolumeLethalAll);
        }

        return Math.Clamp(percent, 0, 100) / 100.0;
    }

    private static string Clean(string display) => display.StartsWith("Microsoft ", StringComparison.OrdinalIgnoreCase) ? display[10..] : display;

    /// <summary>The program language (a key of the string table) a voice's language tag belongs to: "de-DE" is "de", "zh-TW" its own; anything unknown is English.</summary>
    public static string TableLanguage(string tag)
    {
        if (tag.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase) || tag.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase) || tag.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase))
        {
            return "zh-TW";
        }

        string two = tag.Split('-')[0].ToLowerInvariant();
        return LocalizationManager.SupportedLanguages.Any(l => l.Code == two) ? two : "en";
    }

    /// <summary>The language's name as a speaker of it writes it ("Deutsch (Deutschland)").</summary>
    public static string LanguageName(string tag)
    {
        try
        {
            string name = CultureInfo.GetCultureInfo(tag).NativeName;
            return name.Length > 0 ? char.ToUpper(name[0], CultureInfo.CurrentCulture) + name[1..] : tag;
        }
        catch (CultureNotFoundException)
        {
            return tag;
        }
    }

    /// <summary>What is said for one cast (null = nothing): the skill's name (when switched on) with whom it is aimed at, and "lethal" when it is.</summary>
    public static string? Sentence(MeterSettings s, string skill, bool atYou, bool all, bool lethal, string tableLanguage) =>
        Said(s, skill, atYou, all, lethal, tableLanguage)?.Text;

    /// <summary><see cref="Sentence"/> with the phrases the recorded voice says it in.</summary>
    public static Spoken? Said(MeterSettings s, string skill, bool atYou, bool all, bool lethal, string tableLanguage)
    {
        var loc = LocalizationManager.Instance;
        bool announce = (atYou && s.SpeechOnYou) || (all && s.SpeechOnAll) || (lethal && atYou && s.SpeechLethalYou) || (lethal && all && s.SpeechLethalAll);
        if (!announce)
        {
            return null;
        }

        var phrases = new List<string>();
        string text = skill;
        if (!s.SpeechSkillName || string.IsNullOrWhiteSpace(skill))
        {
            text = loc.In("Speech.Warn", tableLanguage);
            phrases.Add(text);
        }
        else
        {
            phrases.Add(skill);
            string aim = atYou ? loc.In("Speech.OnYou", tableLanguage) : all ? loc.In("Speech.OnAll", tableLanguage) : "";
            if (aim.Length > 0)
            {
                text += " " + aim;
                phrases.Add(aim);
            }
        }

        if (lethal)
        {
            string word = loc.In("Speech.Lethal", tableLanguage);
            text += ", " + word;
            phrases.Add(word);
        }

        return new Spoken(text, phrases);
    }

    /// <summary>The language the words around the skill's name are spoken in: the chosen voice's (English for the recorded one).</summary>
    public static string WordsLanguage(MeterSettings s) =>
        Natural(s) ? "en" : TableLanguage(s.SpeechLanguage.Length > 0 ? s.SpeechLanguage : LocalizationManager.Instance.Language);

    /// <summary>Looks at the casts of the moment and speaks the new ones that are meant; called with the tick of the combat overlay.</summary>
    public void Tick(Aion2EntityDirectory? dir, MeterSettings settings)
    {
        if (!settings.SpeechOn || dir is null)
        {
            lock (_gate)
            {
                _said.Clear();
            }

            return;
        }

        if (GameWindow.ForegroundClientArea() is null)
        {
            return;
        }

        DateTime now = DateTime.Now;
        string words = WordsLanguage(settings);
        bool natural = Natural(settings);
        var fresh = new List<Spoken>();
        double volume = 0;
        var keys = new HashSet<string>();
        foreach (CastRowData row in CombatOverlayData.Casts(dir, settings, now, LocalizationManager.Instance.Language, all: true))
        {
            keys.Add(row.Key);
            bool known;
            lock (_gate)
            {
                known = !_said.Add(row.Key);
            }

            // a cast seen only after it ended (the setting was just switched on, or the game window just came forward) is stale
            string name = natural && row.English.Length > 0 ? row.English : row.Name;
            if (known || row.ElapsedMs > row.CastMs || Said(settings, name, row.AtYou, row.All, row.Lethal, words) is not { } sentence)
            {
                continue;
            }

            fresh.Add(sentence);
            volume = Math.Max(volume, Volume(settings, row.AtYou, row.All, row.Lethal));
        }

        lock (_gate)
        {
            _said.IntersectWith(keys); // what no longer runs is forgotten, so the set does not grow over a long session
        }

        if (fresh.Count > 0)
        {
            Speak(Spoken.Join(fresh), settings.SpeechVoiceId, volume, AudioOutput.DeviceId);
        }
    }

    /// <summary>Speaks a text now (a newer one cuts off the one still running) at a volume of 0 to 1 on the chosen device (empty or gone = the Windows default); also what the play buttons of the settings use.</summary>
    public void Speak(string text, string voiceId, double volume, string deviceId) => Speak(new Spoken(text, new[] { text }), voiceId, volume, deviceId);

    /// <summary>Speaks now: the recorded voice plays its clips of the phrases one after the other; a Windows voice (or the recorded one missing a phrase,
    /// with an English Windows voice) reads the text.</summary>
    public void Speak(Spoken said, string voiceId, double volume, string deviceId)
    {
        if (AudioOutput.Muted)
        {
            return; // the meter's sound alerts are off
        }

        int mine = Interlocked.Increment(ref _latest);
        IReadOnlyList<string>? clips = NaturalVoice.Is(voiceId) ? NaturalVoice.Clips(voiceId, said.Phrases) : null;
        _ = Task.Run(async () =>
        {
            try
            {
                MediaPlayer player;
                IDisposable holder;
                if (clips is not null)
                {
                    var list = new MediaPlaybackList { MaxPrefetchTime = TimeSpan.FromSeconds(3) };
                    var sources = new List<IDisposable>();
                    foreach (string file in clips)
                    {
                        var source = MediaSource.CreateFromUri(new Uri(file));
                        sources.Add(source);
                        list.Items.Add(new MediaPlaybackItem(source));
                    }

                    player = new MediaPlayer { Volume = Math.Clamp(volume, 0, 1), Source = list };
                    holder = new Playing(player, sources);
                }
                else
                {
                    using var synth = new SpeechSynthesizer();
                    if (SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Id == voiceId) is { } voice)
                    {
                        synth.Voice = voice;
                    }
                    else if (NaturalVoice.Is(voiceId) && SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase)) is { } english)
                    {
                        synth.Voice = english; // a phrase the recording lacks, in its language
                    }

                    var stream = await synth.SynthesizeTextToStreamAsync(said.Text);
                    if (mine != Volatile.Read(ref _latest))
                    {
                        stream.Dispose();
                        return; // a newer text came while this one was made
                    }

                    var source = MediaSource.CreateFromStream(stream, stream.ContentType);
                    player = new MediaPlayer { Volume = Math.Clamp(volume, 0, 1), Source = source };
                    holder = new Playing(player, new IDisposable[] { source, stream });
                }

                await AudioOutput.ApplyAsync(player, deviceId);
                if (mine != Volatile.Read(ref _latest))
                {
                    holder.Dispose();
                    return;
                }

                MediaPlayer? old;
                IDisposable? oldPlaying;
                lock (_gate)
                {
                    old = _player;
                    oldPlaying = _playing;
                    _player = player;
                    _playing = holder;
                }

                old?.Pause();
                oldPlaying?.Dispose();
                player.Play();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // no voice or no audio device: the meter goes on without the announcement
            }
        });
    }

    private sealed class Playing(MediaPlayer player, IReadOnlyList<IDisposable> parts) : IDisposable
    {
        public void Dispose()
        {
            player.Dispose();
            foreach (IDisposable part in parts)
            {
                part.Dispose();
            }
        }
    }
}
