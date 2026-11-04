using AionDPS.Aion2;

namespace AionDPS.Combat;

/// <summary>
/// The assist's cooldowns (Aion2Cooldowns): the skills left off (basic attacks and fillers with no cooldown, chain skills - from the two lists, or learned
/// from six quick uses), the countdown (a use starts it with the skill's length, the game's milliseconds set it exactly), the lengths learned from the uses,
/// the skill bar's order, and the voice when a skill is back. Made-up numbers shaped like the game's (4 November 2026).
/// </summary>
public static class SelfCheckAssistSkills
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] The assist's cooldowns (skills left off, countdown, lengths, skill bar, voice):");
        bool ok = true;

        void Check(string what, bool good, string detail)
        {
            ok &= good;
            Console.WriteLine($"  -> {what}: {detail}: {(good ? "ok" : "FAIL")}");
        }

        string Of(int id) => Aion2Cooldowns.NotWorth(id)?.ToString() ?? "listed";
        Aion2Cooldowns.Reset();

        // from the lists: a Gladiator's Rending Blow and Keen Strike have no cooldown, Rupture Strike is a chain skill (also by a stone's variant id); Ruinous Blow is listed
        Check("Rending Blow, Keen Strike", Of(11010000) == "NoCooldown" && Of(11020000) == "NoCooldown", $"{Of(11010000)}, {Of(11020000)}");
        Check("Rupture Strike, a Templar's Decisive Strike (stone variant)", Of(11030000) == "Chain" && Of(12020017) == "Chain", $"{Of(11030000)}, {Of(12020017)}");
        Check("Ruinous Blow, the dodge", Of(11100000) == "listed" && Of(11000100) == "listed", $"{Of(11100000)}, {Of(11000100)}");
        Check("a Templar's Pummel (a filler)", Of(12040000) == "NoCooldown", Of(12040000));

        // every class's basic attack, also of the classes whose list has no cooldowns: Vicious Strike, Quick Slice, Flame Arrow, Cold Shock, Earth's Retribution, Onslaught
        int[] basics = { 12010000, 13010000, 15210000, 16010000, 17010000, 18010000 };
        Check("the basic attack of every class", basics.All(id => Of(id) == "NoCooldown"), string.Join(", ", basics.Select(Of)));

        // learned: a Templar's Judgment, seven uses 1.8 s apart (each a cast frame and its effects' frames: the same use), no number of the game: no cooldown
        var t0 = new DateTime(2026, 11, 4, 16, 0, 0);
        for (int use = 0; use < 7; use++)
        {
            DateTime at = t0.AddSeconds(use * 1.8);
            Aion2Cooldowns.NoteOwnCast(12240010, at);
            Aion2Cooldowns.NoteOwnCast(12240017, at.AddSeconds(0.2));
            Aion2Cooldowns.NoteOwnCast(12240010, at.AddSeconds(0.4));
            if (use == 5)
            {
                Check("after six uses (five gaps)", Of(12240000) == "listed", Of(12240000));
            }
        }

        Check("Judgment after seven uses", Of(12240000) == "NoCooldown" && Aion2Cooldowns.LearnedNoCooldown().SequenceEqual(new[] { 12240000 }), Of(12240000));

        // Judgment comes with 12 s from the game after all (a stone): listed again
        int before = Aion2Cooldowns.LearnedVersion;
        Aion2Cooldowns.Receive(new List<CooldownEntry> { new(12240000, 12000) }, t0.AddMinutes(1));
        Check("Judgment with a cooldown after all", Of(12240000) == "listed" && Aion2Cooldowns.LearnedVersion != before && Aion2Cooldowns.IsKnown(12240000), Of(12240000));

        // a skill with a cooldown used as soon as it is back is never one without (Warding Strike, every 23.5 s)
        for (int use = 0; use < 8; use++)
        {
            Aion2Cooldowns.NoteOwnCast(12350150, t0.AddMinutes(2).AddSeconds(use * 23.5));
        }

        Check("Warding Strike used eight times", Of(12350000) == "listed", Of(12350000));

        // kept in the settings: seeded at the next start
        Aion2Cooldowns.Reset();
        Aion2Cooldowns.SeedLearned(new[] { 12240000 });
        Check("kept from the last session", Of(12240000) == "NoCooldown", Of(12240000));
        Aion2Cooldowns.Reset();

        Countdown(Check);
        Lengths(Check);
        BarAndVoice(Check);

        // the effects list: the Abyss's zone states, the artifacts' faction bonus and the game's test states are left off (seen on 2026-10-31 in Reshanta);
        // a class skill's standing effect, a scroll and a boss's timed zone stay
        static bool World(int id, bool endless) => Ui.AssistPanel.IsWorldEffect(id, Aion2.Protocol.Aion2SkillNames.NameOf(id), endless);
        Check("zone state Middle Abyss Reshanta off", World(964, true) && Aion2.Protocol.Aion2SkillNames.NameOf(964) == "Middle Abyss Reshanta", Aion2.Protocol.Aion2SkillNames.NameOf(964));
        Check("artifact bonus Lower Reshanta Artifact I off", World(1200026, true) && Aion2.Protocol.Aion2SkillNames.NameOf(1200026) == "Lower Reshanta Artifact I", Aion2.Protocol.Aion2SkillNames.NameOf(1200026));
        Check("test state For testing ... off", World(961, true), Aion2.Protocol.Aion2SkillNames.NameOf(961));
        Check("a nameless endless state without a picture off", Ui.AssistPanel.IsWorldEffect(4321, "4321", true), "4321");
        Check("a class skill's standing effect stays", !Ui.AssistPanel.IsWorldEffect(12250000, "Divine Protection Stance", true), "class skill");
        Check("the test state as the report shows it (Test PLATE Equippable) off, Poison stays",
            Aion2.Protocol.Aion2Artwork.IsWorldEffectName("Test PLATE Equippable") && Aion2.Protocol.Aion2Artwork.IsWorldEffectName("Test_PLATE Equippable")
            && !Aion2.Protocol.Aion2Artwork.IsWorldEffectName("Poison") && !Aion2.Protocol.Aion2Artwork.IsWorldEffectName("Testudo Guard"), "names");
        Check("a scroll and a boss's timed zone stay", !Ui.AssistPanel.IsWorldEffect(4321, "Courage Scroll", false) && !Ui.AssistPanel.IsWorldEffect(4322, "Flame Zone", false), "timed");
        return ok;
    }

    /// <summary>The countdown: a use starts it with the skill's length, the game's milliseconds set it exactly, and it runs with the clock.</summary>
    private static void Countdown(Action<string, bool, string> check)
    {
        Aion2Cooldowns.Reset();
        var t0 = new DateTime(2026, 11, 4, 18, 0, 0);
        const int taunt = 12120000, punishment = 12090000, warding = 12350000;

        // the whole list at login (a Templar's skills, all ready), then one skill per frame: the others stay in the list
        int[] login = { 1101, 12130000, 12430000, 12100000, 12230000, 12450000, 12300000, punishment, warding, 12270000 };
        Aion2Cooldowns.Receive(login.Select(id => new CooldownEntry(id, 0)).ToList(), t0);
        Aion2Cooldowns.Receive(new List<CooldownEntry> { new(warding, 19600) }, t0.AddSeconds(15));
        var list = Aion2Cooldowns.Last.Entries;
        check("one skill per frame keeps the list", list.Count == login.Length && list.First(e => e.Id == warding).Remaining == 19600 && list.Select(e => e.Id).SequenceEqual(login),
            $"{list.Count} entries");

        // the game's number is the milliseconds left, running down with the clock: 19.6 s, 5 s later 14.6 s, ready after 19.6 s
        CooldownView at0 = Aion2Cooldowns.View(warding, t0.AddSeconds(15)), at5 = Aion2Cooldowns.View(warding, t0.AddSeconds(20));
        check("the game's milliseconds run down with the clock", at0.Running && Math.Abs(at0.Seconds - 19.6) < 1e-6 && Math.Abs(at5.Seconds - 14.6) < 1e-6
            && Math.Abs(at0.Fraction - 1) < 1e-6 && !Aion2Cooldowns.View(warding, t0.AddSeconds(34.7)).Running, $"{at0.Seconds:F1} s, then {at5.Seconds:F1} s");

        // Taunt: the game never sends it at its use; the use starts it with the measured 23.8 s; the frames of its effects and a second frame of the use change nothing
        DateTime use = t0.AddSeconds(40);
        Aion2Cooldowns.NoteOwnCast(12120150, use);
        Aion2Cooldowns.NoteOwnCast(12120157, use.AddSeconds(0.3));
        Aion2Cooldowns.NoteOwnCast(12120150, use.AddSeconds(0.6));
        CooldownView taunt1 = Aion2Cooldowns.View(taunt, use.AddSeconds(1));
        check("a use starts the cooldown with its length (Taunt 23.8 s)", taunt1.Running && Math.Abs(taunt1.Seconds - 22.8) < 1e-6 && Aion2Cooldowns.IsKnown(taunt)
            && Aion2Cooldowns.View(taunt, use.AddSeconds(23.7)).Running && !Aion2Cooldowns.View(taunt, use.AddSeconds(23.9)).Running, $"{taunt1.Seconds:F1} s after 1 s");

        // Punishment: used, then Pummel takes a second off (the game's number), then a reset (0)
        Aion2Cooldowns.NoteOwnCast(12090150, use);
        Aion2Cooldowns.Receive(new List<CooldownEntry> { new(punishment, 22_400) }, use.AddSeconds(5));
        CooldownView cut = Aion2Cooldowns.View(punishment, use.AddSeconds(5.1));
        check("a second taken off shows at once, the length kept", cut.Running && Math.Abs(cut.Seconds - 22.3) < 1e-6 && Math.Abs(cut.Fraction - 22.3 / 28.4) < 1e-6,
            $"{cut.Seconds:F1} s of 28.4");
        Aion2Cooldowns.Receive(new List<CooldownEntry> { new(punishment, 0) }, use.AddSeconds(6));
        check("a reset (0) is ready at once", !Aion2Cooldowns.View(punishment, use.AddSeconds(6.1)).Running, "ready");

        // a use the meter did not see (the game's number with no run, or ending well after it): a new countdown from the number
        Aion2Cooldowns.Receive(new List<CooldownEntry> { new(punishment, 28_000) }, use.AddSeconds(30));
        CooldownView unseen = Aion2Cooldowns.View(punishment, use.AddSeconds(30.1));
        check("a use not seen starts with the game's number", unseen.Running && Math.Abs(unseen.Seconds - 27.9) < 1e-6 && unseen.Fraction > 0.99, $"{unseen.Seconds:F1} s");

        // a basic attack is never listed, not even on a lock of 4 s, nor used or on the skill bar (the owner: not needed, for any class)
        Aion2Cooldowns.Receive(new List<CooldownEntry> { new(12010000, 4000) }, use.AddSeconds(31));
        Aion2Cooldowns.NoteOwnCast(12010150, use.AddSeconds(32));
        check("Vicious Strike is never listed, the skills with a cooldown are",
            !Ui.AssistPanel.Listed(12010000) && !Ui.AssistPanel.Listed(12010000, new[] { 12010000, taunt }) && !Aion2Cooldowns.IsKnown(12010000)
            && Ui.AssistPanel.Listed(taunt) && Ui.AssistPanel.Listed(warding), "basic attack left out");

        // a moment's lock (1,500 = under 2 s) shows as ready
        Aion2Cooldowns.Receive(new List<CooldownEntry> { new(12300000, 1500) }, use.AddSeconds(33));
        check("a moment's lock is no cooldown", !Aion2Cooldowns.View(12300000, use.AddSeconds(33.1)).Running, "ready");

        // another character (a Gladiator's skill): the Templar's cooldowns are gone
        Aion2Cooldowns.Receive(new List<CooldownEntry> { new(11100000, 40000) }, use.AddSeconds(40));
        check("another character's list replaces the last one", Aion2Cooldowns.Last.Entries.Count == 1 && Aion2Cooldowns.Skills().SequenceEqual(new[] { 11100000 })
            && !Aion2Cooldowns.View(taunt, use.AddSeconds(40)).Running, string.Join(",", Aion2Cooldowns.Skills()));
        Aion2Cooldowns.Reset();
    }

    /// <summary>The lengths: the measured ones of the skill lists, then the tenth shortest of the gaps between uses with no number of the game in between.</summary>
    private static void Lengths(Action<string, bool, string> check)
    {
        Aion2Cooldowns.Reset();
        var t0 = new DateTime(2026, 11, 4, 19, 0, 0);
        const int warding = 12350000, shield = 12110000;
        check("the measured length until uses teach one (Warding Strike 23.5 s)", Aion2Cooldowns.Length(warding) == 23.5, $"{Aion2Cooldowns.Length(warding)} s");

        // four uses 22.4, 22.1, 22.6, 22.2 s apart on this character (a stone): the shortest of the four
        double at = 0;
        Aion2Cooldowns.NoteOwnCast(12350150, t0);
        foreach (double gap in new[] { 22.4, 22.1, 22.6 })
        {
            at += gap;
            Aion2Cooldowns.NoteOwnCast(12350150, t0.AddSeconds(at));
        }

        check("three gaps: still the measured length", Aion2Cooldowns.Length(warding) == 23.5, $"{Aion2Cooldowns.Length(warding)} s");
        at += 22.2;
        Aion2Cooldowns.NoteOwnCast(12350150, t0.AddSeconds(at));
        check("four gaps: the shortest of them", Aion2Cooldowns.Length(warding) == 22.1, $"{Aion2Cooldowns.Length(warding)} s");

        // a gap with a number of the game in it (basic attacks took 2 s off) says nothing of the length
        int gapsBefore = Aion2Cooldowns.LearnedGaps()[warding].Count;
        Aion2Cooldowns.Receive(new List<CooldownEntry> { new(warding, 12_000) }, t0.AddSeconds(at + 8));
        at += 20.5;
        Aion2Cooldowns.NoteOwnCast(12350150, t0.AddSeconds(at));
        check("a gap with the game's number in it is not learned", Aion2Cooldowns.LearnedGaps()[warding].Count == gapsBefore, $"{gapsBefore} gaps");

        // twelve gaps: the tenth shortest (the second of twelve) - a use that came early once (an effect took time off) does not shorten it
        Aion2Cooldowns.Reset();
        double[] gaps = { 22.3, 22.5, 18.0, 22.4, 22.2, 22.6, 22.3, 22.4, 22.5, 22.2, 22.7, 22.3 };
        at = 0;
        Aion2Cooldowns.NoteOwnCast(12350150, t0);
        foreach (double gap in gaps)
        {
            at += gap;
            Aion2Cooldowns.NoteOwnCast(12350150, t0.AddSeconds(at));
        }

        check("twelve gaps: the tenth shortest, an early use left out", Aion2Cooldowns.Length(warding) == 22.2, $"{Aion2Cooldowns.Length(warding)} s");

        // a skill with no measured length: the game's first number after the use (0.4 s after it, 18.6 s left: 19 s in all)
        Aion2Cooldowns.NoteOwnCast(12110150, t0.AddMinutes(10));
        check("no length before the game's number", Aion2Cooldowns.Length(shield) is null && !Aion2Cooldowns.View(shield, t0.AddMinutes(10).AddSeconds(1)).Running, "none");
        Aion2Cooldowns.Receive(new List<CooldownEntry> { new(shield, 18_600) }, t0.AddMinutes(10).AddSeconds(0.4));
        check("the first number after a use gives the length", Math.Abs(Aion2Cooldowns.Length(shield)!.Value - 19.0) < 1e-6, $"{Aion2Cooldowns.Length(shield):F1} s");
        Aion2Cooldowns.NoteOwnCast(12110150, t0.AddMinutes(11));
        CooldownView next = Aion2Cooldowns.View(shield, t0.AddMinutes(11).AddSeconds(1));
        check("the next use starts with it", next.Running && Math.Abs(next.Seconds - 18.0) < 1e-6, $"{next.Seconds:F1} s");

        // kept in the settings: the next session starts with the gaps learned
        Dictionary<int, List<double>> kept = Aion2Cooldowns.LearnedGaps();
        Aion2Cooldowns.Reset();
        Aion2Cooldowns.SeedGaps(kept);
        check("the gaps kept from the last session", Aion2Cooldowns.Length(warding) == 22.2, $"{Aion2Cooldowns.Length(warding)} s");
        Aion2Cooldowns.Reset();
    }

    /// <summary>The skill bar (its slots' order) decides the list; the voice says a ticked skill once when it is back.</summary>
    private static void BarAndVoice(Action<string, bool, string> check)
    {
        Aion2Cooldowns.Reset();
        var t0 = new DateTime(2026, 11, 4, 20, 0, 0);
        int[] bar = { 12350000, 12120000, 12010000, 12100000 };
        var dir = new Aion2EntityDirectory { LocalPlayerId = 8956 };
        dir.SetLocalBar(bar.ToHashSet(), bar);

        // Defiance used, not on the bar (moved off it): listed after the bar's skills; the basic attack on the bar is not
        Aion2Cooldowns.NoteOwnCast(12260150, t0);
        var listed = Aion2Cooldowns.Skills(dir.LocalBarOrder).Where(id => Ui.AssistPanel.Listed(id, dir.LocalBarOrder)).ToList();
        check("the bar's order, then the skills used off it, no basic attack", listed.SequenceEqual(new[] { 12350000, 12120000, 12100000, 12260000 }), string.Join(",", listed));

        // the voice: Taunt ticked, Shield Smite not; both used; said once, when Taunt is back
        var voice = new Ui.CooldownVoice();
        var voiced = new HashSet<int> { 12120000 };
        int[] skills = { 12120000, 12100000 };
        Aion2Cooldowns.NoteOwnCast(12120150, t0);
        Aion2Cooldowns.NoteOwnCast(12100150, t0);
        bool quiet = voice.Due(skills, voiced, t0.AddSeconds(1), true).Count == 0 && voice.Due(skills, voiced, t0.AddSeconds(10), true).Count == 0;
        List<int> back = voice.Due(skills, voiced, t0.AddSeconds(24), true);
        bool once = voice.Due(skills, voiced, t0.AddSeconds(25), true).Count == 0;
        check("a ticked skill is said once when it is back, the others not", quiet && back.SequenceEqual(new[] { 12120000 }) && once, string.Join(",", back));

        // not while the game is not in front
        Aion2Cooldowns.NoteOwnCast(12120150, t0.AddSeconds(30));
        voice.Due(skills, voiced, t0.AddSeconds(31), true);
        check("nothing said while the game is not in front", voice.Due(skills, voiced, t0.AddSeconds(55), false).Count == 0, "quiet");
        Aion2Cooldowns.Reset();
    }
}
