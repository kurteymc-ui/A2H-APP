using AionDPS.Aion2.Protocol;
using AionDPS.Ui;

namespace AionDPS.Combat;

/// <summary>
/// The recorded natural voice (Ui/NaturalVoice, 5 November 2026): its clips came with the build, a class skill's "... ready" and a boss cast's parts
/// ("Ice Rain" + "on you" + "lethal") are found, several announcements get a pause between them, and a phrase it lacks makes it hand over to a Windows voice.
/// </summary>
public static class SelfCheckNaturalVoice
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] The natural voice (recorded clips):");
        bool ok = true;

        void Check(string what, bool good, string detail)
        {
            ok &= good;
            Console.WriteLine($"  -> {what}: {detail}: {(good ? "ok" : "FAIL")}");
        }

        Check("the phrase key", NaturalVoice.Key("Ice Rain,  lethal.") == "ice rain lethal" && NaturalVoice.Key(" Taunt ready! ") == "taunt ready",
            $"\"{NaturalVoice.Key("Ice Rain,  lethal.")}\", \"{NaturalVoice.Key(" Taunt ready! ")}\"");
        Check("the voice came with the build", NaturalVoice.Voices().Any(v => v.Id == NaturalVoice.Heart), string.Join(", ", NaturalVoice.Voices().Select(v => v.Name)));

        var settings = new MeterSettings { SpeechVoiceId = NaturalVoice.Heart, SpeechOn = true, SpeechOnYou = true, SpeechOnAll = true, SpeechLethalYou = true, SpeechLethalAll = true };
        string words = CastSpeech.WordsLanguage(settings);
        Check("its words are English", words == "en" && CastSpeech.Natural(settings), words);

        // a class skill coming back, by its English name (a Gladiator's Ruinous Blow, a Templar's Decisive Strike), and the test button's "Skill ready"
        foreach (int id in new[] { 11_100_000, 12_020_000 })
        {
            string name = Aion2SkillNames.EnglishOf(id);
            string line = string.Format(System.Globalization.CultureInfo.InvariantCulture, LocalizationManager.Instance.In("Assist.VoiceReady", "en"), name);
            Check($"\"{line}\"", NaturalVoice.Clips(NaturalVoice.Heart, new[] { line }) is { Count: 1 }, name);
        }

        string sample = string.Format(System.Globalization.CultureInfo.InvariantCulture, LocalizationManager.Instance.In("Assist.VoiceReady", "en"), CooldownVoice.SampleName(settings));
        Check($"\"{sample}\"", NaturalVoice.Clips(NaturalVoice.Heart, new[] { sample }) is { Count: 1 }, "the test button");

        // a boss cast at you that would kill: the sample skill, "on you", "lethal" - three clips; two casts get a pause between them
        Spoken? one = CastSpeech.Said(settings, LocalizationManager.Instance.In("Speech.SampleIce", "en"), true, false, true, words);
        Spoken? two = CastSpeech.Said(settings, LocalizationManager.Instance.In("Speech.SampleDeath", "en"), false, true, false, words);
        IReadOnlyList<string>? clips = one is null ? null : NaturalVoice.Clips(NaturalVoice.Heart, one.Phrases);
        Check("a lethal cast at you", one is not null && clips is { Count: 3 }, one is null ? "nothing said" : string.Join(" + ", one.Phrases));
        Spoken? both = one is not null && two is not null ? Spoken.Join(new[] { one, two }) : null;
        IReadOnlyList<string>? joined = both is null ? null : NaturalVoice.Clips(NaturalVoice.Heart, both.Phrases);
        Check("two casts, a pause between", joined is { Count: 6 } && joined[3].EndsWith("pause.mp3", StringComparison.Ordinal), both?.Text ?? "nothing said");

        Check("a phrase it lacks: the Windows voice says it", NaturalVoice.Clips(NaturalVoice.Heart, new[] { "Zzyzx Qwerty ready" }) is null, "no clip");
        return ok;
    }
}
