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

        // from the lists: a Gladiator's Rending Blow and Keen Strike have no cooldown, Rupture Strike is a chain skill (also by a specialization's variant id);
        // Ruinous Blow is listed
        Check("Rending Blow, Keen Strike", Of(11010000) == "NoCooldown" && Of(11020000) == "NoCooldown", $"{Of(11010000)}, {Of(11020000)}");
        Check("Rupture Strike, a Templar's Decisive Strike (stone variant)", Of(11030000) == "Chain" && Of(12020017) == "Chain", $"{Of(11030000)}, {Of(12020017)}");
        Check("Ruinous Blow (45 s in the client's table)", Of(11100000) == "listed", Of(11100000));
        Check("a Templar's Pummel (a filler)", Of(12040000) == "NoCooldown", Of(12040000));

        // every class's basic attack, also of the classes whose list has no cooldowns: Vicious Strike, Quick Slice, Flame Arrow, Cold Shock, Earth's Retribution, Onslaught
        int[] basics = { 12010000, 13010000, 15210000, 16010000, 17010000, 18010000 };
        Check("the basic attack of every class", basics.All(id => Of(id) == "NoCooldown"), string.Join(", ", basics.Select(Of)));

        // learned: a skill the client's table does not have (made up, 12,990,000), seven uses 1.8 s apart (each a cast frame and its effects' frames: the
        // same use), no number of the game: no cooldown
        const int unknown = 12990000;
        var t0 = new DateTime(2026, 11, 4, 16, 0, 0);
        for (int use = 0; use < 7; use++)
        {
            DateTime at = t0.AddSeconds(use * 1.8);
            Aion2Cooldowns.NoteOwnCast(unknown + 10, at);
            Aion2Cooldowns.NoteOwnCast(unknown + 17, at.AddSeconds(0.2));
            Aion2Cooldowns.NoteOwnCast(unknown + 10, at.AddSeconds(0.4));
            if (use == 5)
            {
                Check("after six uses (five gaps)", Of(unknown) == "listed", Of(unknown));
            }
        }

        Check("a skill used every 1.8 s after seven uses", Of(unknown) == "NoCooldown" && Aion2Cooldowns.LearnedNoCooldown().SequenceEqual(new[] { unknown }), Of(unknown));

        // it comes with 12 s from the game after all: listed again
        int before = Aion2Cooldowns.LearnedVersion;
        Aion2Cooldowns.Receive(new List<CooldownEntry> { new(unknown, 12000) }, t0.AddMinutes(1));
        Check("with a cooldown from the game after all", Of(unknown) == "listed" && Aion2Cooldowns.LearnedVersion != before && Aion2Cooldowns.IsKnown(unknown), Of(unknown));

        // the client's table by the specialization in use: Judgment has 5 s, with the specializations 3 and 5 (12,240,350) none - left off from its first use
        string judgmentBefore = Of(12240000);
        Aion2Cooldowns.NoteOwnCast(12240350, t0.AddMinutes(2));
        Check("Judgment with the specializations 3 and 5 has none (the client's table)", judgmentBefore == "listed" && Of(12240000) == "NoCooldown",
            $"{judgmentBefore}, then {Of(12240000)}");

        // a skill with a cooldown used as soon as it is back is never one without (Warding Strike, every 23.5 s)
        for (int use = 0; use < 8; use++)
        {
            Aion2Cooldowns.NoteOwnCast(12350150, t0.AddMinutes(3).AddSeconds(use * 23.5));
        }

        Check("Warding Strike used eight times", Of(12350000) == "listed", Of(12350000));

        // kept in the settings: seeded at the next start
        Aion2Cooldowns.Reset();
        Aion2Cooldowns.SeedLearned(new[] { unknown });
        Check("kept from the last session", Of(unknown) == "NoCooldown", Of(unknown));
        Aion2Cooldowns.Reset();

        Countdown(Check);
        Lengths(Check);
        BarAndVoice(Check);
        Places(Check);
        Swapped(Check);
        TimeText(Check);

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
        // the class's other skills offered to tick onto the screen (5 November 2026): a Templar's Doom Shield and Shield of Protection are both there,
        // a basic attack (Vicious Strike) and a filler (Pummel) are not; no class known, none
        var templar = Aion2Cooldowns.ClassSkills(12);
        Check("a Templar's other skills", templar.Contains(12070000) && templar.Contains(12110000) && !templar.Contains(12010000) && !templar.Contains(12040000)
            && templar.All(id => id / 1_000_000 == 12 && id % 10000 == 0) && Aion2Cooldowns.ClassSkills(0).Count == 0, $"{templar.Count} skills");

        // the class follows the character (7 November 2026: an Assassin's skills were offered on a Ranger): the record of a Ranger at the login sets it; a
        // party member's buff landing on the character (a Chanter's 18160032 in its own cast frames, the owner's recording of 5 November) changes nothing and
        // keeps the running cooldowns (the same day: "insta resets the cd on every buff"); two different skills of another class used switch it; each change
        // is raised once, with the class before
        Aion2Cooldowns.Reset();
        var changes = new List<(int, int)>();
        void Seen(int before, int after) => changes.Add((before, after));
        Aion2Cooldowns.ClassChanged += Seen;
        Aion2Cooldowns.SetClass(13);
        Aion2Cooldowns.SetClass(Aion2Cooldowns.DigitsOf(Aion2.Protocol.Aion2SkillNames.ClassFromCode(14)));
        int afterLogin = Aion2Cooldowns.CurrentClass();
        DateTime rangerAt = DateTime.Now;
        int rangerSkill = Aion2Cooldowns.ClassSkills(14).FirstOrDefault();
        Aion2Cooldowns.NoteOwnCast(rangerSkill + 10, rangerAt);
        bool running = Aion2Cooldowns.View(rangerSkill, rangerAt.AddSeconds(1)).Running;
        Aion2Cooldowns.NoteOwnCast(18160032, rangerAt.AddSeconds(2));  // the Chanter's buff, three times
        Aion2Cooldowns.NoteOwnCast(18160032, rangerAt.AddSeconds(4));
        Aion2Cooldowns.NoteOwnCast(18160002, rangerAt.AddSeconds(6));
        bool buffKeeps = Aion2Cooldowns.CurrentClass() == 14 && running && Aion2Cooldowns.View(rangerSkill, rangerAt.AddSeconds(7)).Running;
        Aion2Cooldowns.NoteOwnCast(18010000, rangerAt.AddSeconds(10)); // one Chanter skill used: not yet
        int afterOne = Aion2Cooldowns.CurrentClass();
        Aion2Cooldowns.NoteOwnCast(18020000, rangerAt.AddSeconds(12)); // a second one: the character is a Chanter
        Aion2Cooldowns.ClassChanged -= Seen;
        Check("the class of the character: an Assassin, then a Ranger logs in (code 14); a Chanter's buff on it keeps its class and cooldowns; two Chanter skills used switch it",
            afterLogin == 14 && rangerSkill != 0 && buffKeeps && afterOne == 14 && Aion2Cooldowns.CurrentClass() == 18
            && changes.SequenceEqual(new[] { (0, 13), (13, 14), (14, 18) })
            && Aion2Cooldowns.DigitsOf("Spiritmaster") == 16 && Aion2Cooldowns.NameOfDigits(15) == "Sorcerer",
            $"{string.Join(" ", changes.Select(c => $"{c.Item1}>{c.Item2}"))}, running {running}, kept {buffKeeps}");

        // a use's ticks are no uses (7 November 2026, a tester's Spiritmaster: Flame Blessing showed 1.1 s while the blessing was on): the use 16370030, then
        // its flame 16370001 every 1.1 s for 20 s, as on the owner's recordings - the 60 s cooldown runs from the use; a channel's hits 0.1 s apart (Bittercold
        // Wind 15280142, no frame ending in 0) start its 15 s once and the next use after it starts it again; gaps learnt from ticks before are not a cooldown
        Aion2Cooldowns.Reset();
        Aion2Cooldowns.SetClass(16);
        DateTime blessed = DateTime.Now;
        Aion2Cooldowns.NoteOwnCast(16370030, blessed);
        for (int k = 1; k <= 18; k++)
        {
            Aion2Cooldowns.NoteOwnCast(16370001, blessed.AddSeconds(1.1 * k));
        }

        CooldownView flame = Aion2Cooldowns.View(16370000, blessed.AddSeconds(21));
        bool blessing = flame.Running && Math.Abs(flame.Seconds - 39) < 0.01 && Aion2Cooldowns.Length(16370000) == 60;
        Aion2Cooldowns.Reset();
        Aion2Cooldowns.SetClass(15);
        for (int k = 0; k < 20; k++)
        {
            Aion2Cooldowns.NoteOwnCast(15280142, blessed.AddSeconds(0.1 * k));
        }

        CooldownView wind = Aion2Cooldowns.View(15280000, blessed.AddSeconds(5));
        for (int k = 0; k < 20; k++)
        {
            Aion2Cooldowns.NoteOwnCast(15280142, blessed.AddSeconds(16 + 0.1 * k));
        }

        CooldownView again = Aion2Cooldowns.View(15280000, blessed.AddSeconds(20));
        bool channel = wind.Running && Math.Abs(wind.Seconds - 10) < 0.01 && again.Running && Math.Abs(again.Seconds - 11) < 0.01;
        Aion2Cooldowns.Reset();
        Aion2Cooldowns.SeedGaps(new Dictionary<int, List<double>> { [16370000] = new() { 2.1, 2.2, 2.3, 2.4, 2.6, 2.8 } });
        bool seeded = Aion2Cooldowns.Length(16370000) == 60;
        Check("a use's ticks are no uses: Flame Blessing's flame every 1.1 s, a channel's hits every 0.1 s; gaps of ticks learnt before are no cooldown",
            blessing && channel && seeded, $"blessing {flame.Seconds:0.0} s left ({blessing}), wind {wind.Seconds:0.0} then {again.Seconds:0.0} ({channel}), seeded {Aion2Cooldowns.Length(16370000)}");
        Aion2Cooldowns.Reset();
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

        // Taunt with the specialization 4 (12,120,040): the game never sends it at its use; the use starts it with the client's 25 s for that variant (30 s
        // without it); the frames of its effects and a second frame of the use change nothing
        DateTime use = t0.AddSeconds(40);
        Aion2Cooldowns.NoteOwnCast(12120040, use);
        Aion2Cooldowns.NoteOwnCast(12120047, use.AddSeconds(0.3));
        Aion2Cooldowns.NoteOwnCast(12120040, use.AddSeconds(0.6));
        CooldownView taunt1 = Aion2Cooldowns.View(taunt, use.AddSeconds(1));
        check("a use starts the cooldown with the client's length of its variant (Taunt 25 s)", taunt1.Running && Math.Abs(taunt1.Seconds - 24) < 1e-6
            && Aion2Cooldowns.IsKnown(taunt) && Aion2Cooldowns.View(taunt, use.AddSeconds(24.9)).Running && !Aion2Cooldowns.View(taunt, use.AddSeconds(25.1)).Running,
            $"{taunt1.Seconds:F1} s after 1 s");

        // Punishment (30 s): used, then Pummel takes a second off (the game's number), then a reset (0)
        Aion2Cooldowns.NoteOwnCast(12090350, use);
        Aion2Cooldowns.Receive(new List<CooldownEntry> { new(punishment, 22_400) }, use.AddSeconds(5));
        CooldownView cut = Aion2Cooldowns.View(punishment, use.AddSeconds(5.1));
        check("a second taken off shows at once, the length kept", cut.Running && Math.Abs(cut.Seconds - 22.3) < 1e-6 && Math.Abs(cut.Fraction - 22.3 / 30) < 1e-6,
            $"{cut.Seconds:F1} s of 30");
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

    /// <summary>The lengths: the client's table by the variant used, then the tenth shortest of the gaps between uses with no number of the game in between
    /// (never more than the table's).</summary>
    private static void Lengths(Action<string, bool, string> check)
    {
        Aion2Cooldowns.Reset();
        var t0 = new DateTime(2026, 11, 4, 19, 0, 0);
        const int warding = 12350000, variant = 12350150, taunt = 12120000, unknown = 12990000;
        double? plain = Aion2Cooldowns.Length(warding);
        Aion2Cooldowns.NoteOwnCast(variant, t0);
        check("the client's length: Warding Strike 30 s, with the specializations 1 and 5 25 s", plain == 30 && Aion2Cooldowns.Length(warding) == 25,
            $"{plain} s, then {Aion2Cooldowns.Length(warding)} s");

        // four uses 22.4, 22.1, 22.6, 22.2 s apart on this character (its cooldown reduction): the shortest of the four
        double at = 0;
        foreach (double gap in new[] { 22.4, 22.1, 22.6 })
        {
            at += gap;
            Aion2Cooldowns.NoteOwnCast(variant, t0.AddSeconds(at));
        }

        check("three gaps: still the table's", Aion2Cooldowns.Length(warding) == 25, $"{Aion2Cooldowns.Length(warding)} s");
        at += 22.2;
        Aion2Cooldowns.NoteOwnCast(variant, t0.AddSeconds(at));
        check("four gaps: the shortest of them", Aion2Cooldowns.Length(warding) == 22.1, $"{Aion2Cooldowns.Length(warding)} s");

        // a gap with a number of the game in it (basic attacks took 2 s off) says nothing of the length
        int gapsBefore = Aion2Cooldowns.LearnedGaps()[variant].Count;
        Aion2Cooldowns.Receive(new List<CooldownEntry> { new(warding, 12_000) }, t0.AddSeconds(at + 8));
        at += 20.5;
        Aion2Cooldowns.NoteOwnCast(variant, t0.AddSeconds(at));
        check("a gap with the game's number in it is not learned", Aion2Cooldowns.LearnedGaps()[variant].Count == gapsBefore, $"{gapsBefore} gaps");

        // twelve gaps: the tenth shortest (the second of twelve) - a use that came early once (an effect took time off) does not shorten it
        Aion2Cooldowns.Reset();
        double[] gaps = { 22.3, 22.5, 18.0, 22.4, 22.2, 22.6, 22.3, 22.4, 22.5, 22.2, 22.7, 22.3 };
        at = 0;
        Aion2Cooldowns.NoteOwnCast(variant, t0);
        foreach (double gap in gaps)
        {
            at += gap;
            Aion2Cooldowns.NoteOwnCast(variant, t0.AddSeconds(at));
        }

        check("twelve gaps: the tenth shortest, an early use left out", Aion2Cooldowns.Length(warding) == 22.2, $"{Aion2Cooldowns.Length(warding)} s");

        // used later than it was back every time: never longer than the table's; another specialization starts from its own
        DateTime t1 = t0.AddMinutes(10);
        at = 0;
        Aion2Cooldowns.NoteOwnCast(12120040, t1);
        foreach (double gap in new[] { 27.0, 26.5, 27.5, 26.8 })
        {
            at += gap;
            Aion2Cooldowns.NoteOwnCast(12120040, t1.AddSeconds(at));
        }

        double? late = Aion2Cooldowns.Length(taunt);
        Aion2Cooldowns.NoteOwnCast(12120010, t1.AddSeconds(at + 40));
        check("never longer than the table's; another variant has its own", late == 25 && Aion2Cooldowns.Length(taunt) == 30 && !Aion2Cooldowns.LearnedGaps().ContainsKey(12120010),
            $"{late} s, then {Aion2Cooldowns.Length(taunt)} s");

        // a skill the table does not have: the game's first number after the use (0.4 s after it, 18.6 s left: 19 s in all)
        DateTime t2 = t0.AddMinutes(20);
        Aion2Cooldowns.NoteOwnCast(unknown + 150, t2);
        check("no length before the game's number", Aion2Cooldowns.Length(unknown) is null && !Aion2Cooldowns.View(unknown, t2.AddSeconds(1)).Running, "none");
        Aion2Cooldowns.Receive(new List<CooldownEntry> { new(unknown, 18_600) }, t2.AddSeconds(0.4));
        check("the first number after a use gives the length", Math.Abs(Aion2Cooldowns.Length(unknown)!.Value - 19.0) < 1e-6, $"{Aion2Cooldowns.Length(unknown):F1} s");
        Aion2Cooldowns.NoteOwnCast(unknown + 150, t2.AddMinutes(1));
        CooldownView next = Aion2Cooldowns.View(unknown, t2.AddMinutes(1).AddSeconds(1));
        check("the next use starts with it", next.Running && Math.Abs(next.Seconds - 18.0) < 1e-6, $"{next.Seconds:F1} s");

        // kept in the settings: the next session uses the gaps learned from the first use of the variant on
        Dictionary<int, List<double>> kept = Aion2Cooldowns.LearnedGaps();
        Aion2Cooldowns.Reset();
        Aion2Cooldowns.SeedGaps(kept);
        Aion2Cooldowns.NoteOwnCast(variant, t0.AddHours(1));
        check("the gaps kept from the last session", Aion2Cooldowns.Length(warding) == 22.2, $"{Aion2Cooldowns.Length(warding)} s");
        Aion2Cooldowns.Reset();
    }

    /// <summary>The time as the cooldowns show it: rounded up, tenths under ten seconds but never "10.0" (11, 10, 9.9 ...), the last tenth 0.1, minutes from a minute on.</summary>
    private static void TimeText(Action<string, bool, string> check)
    {
        static string T(double seconds) => Ui.CooldownArt.Time(seconds);
        static string N(double value) => value.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture);
        string[] got = { T(10.4), T(10.0), T(9.95), T(9.9), T(9.81), T(0.04), T(75) };
        string[] want = { "11", "10", "10", N(9.9), N(9.9), N(0.1), "1:15" };
        check("the time: 11, 10, 9.9 - never 10.0; the last tenth 0.1; 1:15", got.SequenceEqual(want), string.Join(" ", got));
    }

    /// <summary>The cooldowns' places at another size: the arrangement kept around its top left corner (squares side by side neither apart nor overlapping).</summary>
    private static void Places(Action<string, bool, string> check)
    {
        // two squares side by side at size 1 (42 units each, 2 apart) and one under the first; at size 1.5 a square is 63 units
        var places = new Dictionary<int, double[]> { [1] = new[] { 60.0, 300.0 }, [2] = new[] { 104.0, 300.0 }, [3] = new[] { 60.0, 344.0 } };
        Ui.CooldownSkillWindow.Rescale(places, 1.5, 1.5);
        bool corner = places[1][0] == 60 && places[1][1] == 300;
        bool beside = places[2][0] == 126 && places[2][1] == 300 && places[2][0] >= 60 + 42 * 1.5;
        bool under = places[3][0] == 60 && places[3][1] == 366;
        check("the places at size 1.5: the corner stays, side by side and one under the other stay so", corner && beside && under,
            $"{places[2][0]},{places[2][1]} {places[3][0]},{places[3][1]}");

        var none = new Dictionary<int, double[]>();
        Ui.CooldownSkillWindow.Rescale(none, 2, 2);
        var odd = new Dictionary<int, double[]> { [1] = new[] { 10.0, 20.0 } };
        Ui.CooldownSkillWindow.Rescale(odd, double.NaN, 0);
        check("no places, or no size: nothing changes", none.Count == 0 && odd[1][0] == 10 && odd[1][1] == 20, $"{odd[1][0]},{odd[1][1]}");
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

        // the voice: Taunt (25 s) ticked, Shield Smite (10 s) not; both used; said once, when Taunt is back
        var voice = new Ui.CooldownVoice();
        var voiced = new HashSet<int> { 12120000 };
        int[] skills = { 12120000, 12100000 };
        Aion2Cooldowns.NoteOwnCast(12120040, t0);
        Aion2Cooldowns.NoteOwnCast(12100150, t0);
        bool quiet = voice.Due(skills, voiced, t0.AddSeconds(1), true).Count == 0 && voice.Due(skills, voiced, t0.AddSeconds(12), true).Count == 0;
        List<int> back = voice.Due(skills, voiced, t0.AddSeconds(26), true);
        bool once = voice.Due(skills, voiced, t0.AddSeconds(27), true).Count == 0;
        check("a ticked skill is said once when it is back, the others not", quiet && back.SequenceEqual(new[] { 12120000 }) && once, string.Join(",", back));

        // not while the game is not in front
        Aion2Cooldowns.NoteOwnCast(12120040, t0.AddSeconds(30));
        voice.Due(skills, voiced, t0.AddSeconds(31), true);
        check("nothing said while the game is not in front", voice.Due(skills, voiced, t0.AddSeconds(55), false).Count == 0, "quiet");
        Aion2Cooldowns.Reset();
    }
    /// <summary>
    /// A skill swapped on the bar in the game (5 November 2026, the owner's recording: Shield of Protection for Empyrean Lord's Punishment in slot 7 and back):
    /// the game sends the first macro page again at once (0x93e2), the list follows it without a map change, the skill swapped off is not listed again for a
    /// use from before, and the new one takes its place on the screen.
    /// </summary>
    private static void Swapped(Action<string, bool, string> check)
    {
        static string Record(int slot, int id) => "0301" + slot.ToString("x2") + Hex(id) + "04" + Hex(id);
        static string Hex(int v) => Convert.ToHexString(BitConverter.GetBytes(v)).ToLowerInvariant();
        static byte[] Wire(string hex)
        {
            byte[] body = Convert.FromHexString(hex);
            var wire = new List<byte>();
            int length = body.Length + 4;
            while (length >= 0x80)
            {
                wire.Add((byte)((length & 0x7f) | 0x80));
                length >>= 7;
            }

            wire.Add((byte)length);
            wire.AddRange(body);
            return wire.ToArray();
        }

        // Vicious Strike, Judgment, Shield of Protection, Taunt; then Empyrean Lord's Punishment in Shield of Protection's slot
        string Page(int third) => "93e2010c" + Record(0, 12010000) + Record(1, 12240000) + Record(2, third) + Record(20, 12120000) + "00";
        using var source = new Aion2PacketCombatSource(Aion2.Protocol.Aion2Protocol.Load());
        var at = new DateTime(2026, 11, 5, 20, 41, 0, DateTimeKind.Utc);
        uint seq = 100;
        void Send(string hex)
        {
            byte[] wire = Wire(hex);
            source.Ingest(new Aion2.Capture.TcpSegment(at, "10.0.0.1:7777", "192.168.0.2:50000", seq, wire, FromServer: true));
            seq += (uint)wire.Length;
        }

        Send(Page(12110000));
        var dir = (Aion2EntityDirectory)source.Entities;
        bool first = dir.LocalBarOrder.Take(4).SequenceEqual(new[] { 12010000, 12240000, 12110000, 12120000 }); // by slot: 0, 1, 2, 20
        Send(Page(12310000));
        List<int> after = dir.LocalBarOrder.ToList();
        bool swapped = after.Contains(12310000) && !after.Contains(12110000) && after.IndexOf(12310000) == 2;
        check("the bar sent again after a swap (0x93e2) is the bar at once: the new skill in the old one's slot", first && swapped, string.Join(",", after.Take(5)));

        var pairs = Ui.AssistController.Swaps(new[] { 1, 2, 3 }, new[] { 1, 4, 3 });
        bool moved = Ui.AssistController.Swaps(new[] { 1, 2, 3 }, new[] { 2, 1, 3 }).Count == 0;
        check("a slot holding another skill is a swap; two skills changing places are not", pairs.Count == 1 && pairs[0] == (2, 4) && moved, string.Join(",", pairs));

        // Taunt used, then swapped off the bar for Noble Armor: no longer listed; used again (put back somewhere else), listed again
        Aion2Cooldowns.Reset();
        var t0 = new DateTime(2026, 11, 5, 20, 0, 0);
        int[] bar = { 12010000, 12120000, 12100000 }, swappedBar = { 12010000, 12230000, 12100000 };
        Aion2Cooldowns.NoteOwnCast(12120040, t0);
        bool before = Aion2Cooldowns.Skills(bar).Contains(12120000);
        List<int> off = Aion2Cooldowns.Skills(swappedBar).ToList();
        Aion2Cooldowns.NoteOwnCast(12120040, t0.AddMinutes(2));
        bool again = Aion2Cooldowns.Skills(swappedBar).Contains(12120000);
        check("a skill swapped off the bar is not listed for an earlier use, and is again when used after", before && !off.Contains(12120000) && off.Contains(12230000) && again,
            string.Join(",", off));
        Aion2Cooldowns.Reset();
    }
}
