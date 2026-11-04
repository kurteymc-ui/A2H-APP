using AionDPS.Aion2;

namespace AionDPS.Combat;

/// <summary>
/// The skills the assist leaves off its cooldown list while they are ready (Aion2Cooldowns.NotWorth): basic attacks with no cooldown and chain skills from the
/// two lists, and the skills learned to have none from use (three uses without a cooldown of more than 2 s; a longer one takes a skill off again).
/// </summary>
public static class SelfCheckAssistSkills
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Skills left off the assist's cooldown list (basic attacks, chain skills):");
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

        // every class's basic attack, also of the classes whose list has no cooldowns: Vicious Strike, Quick Slice, Flame Arrow, Cold Shock, Earth's Retribution, Onslaught
        int[] basics = { 12010000, 13010000, 15210000, 16010000, 17010000, 18010000 };
        Check("the basic attack of every class", basics.All(id => Of(id) == "NoCooldown"), string.Join(", ", basics.Select(Of)));

        // learned: a Templar's Judgment hits five times in a second (one use), three uses 4 s apart, never a cooldown in the list: no cooldown
        var t0 = new DateTime(2026, 10, 31, 16, 0, 0);
        for (int use = 0; use < 3; use++)
        {
            for (int hit = 0; hit < 5; hit++)
            {
                Aion2Cooldowns.NoteOwnUse(12240017, t0.AddSeconds(use * 4 + hit * 0.2));
            }

            Aion2Cooldowns.Receive(new List<CooldownEntry> { new(12350000, 0) }, t0.AddSeconds(use * 4 + 2));
            if (use == 1)
            {
                Check("after two uses", Of(12240000) == "listed", Of(12240000));
            }
        }

        Check("Judgment after three uses", Of(12240000) == "NoCooldown" && Aion2Cooldowns.LearnedNoCooldown().SequenceEqual(new[] { 12240000 }), Of(12240000));

        // a skill with a cooldown: the list shows it right at the use (Warding Strike, 21,500 = about 9 s); three uses, still listed
        var t1 = t0.AddMinutes(1);
        for (int use = 0; use < 3; use++)
        {
            Aion2Cooldowns.Receive(new List<CooldownEntry> { new(12350000, 21500) }, t1.AddSeconds(use * 12));
            Aion2Cooldowns.NoteOwnUse(12350010, t1.AddSeconds(use * 12 + 0.4));
            Aion2Cooldowns.Receive(new List<CooldownEntry> { new(12350000, 0) }, t1.AddSeconds(use * 12 + 10));
        }

        Check("Warding Strike with its cooldown", Of(12350000) == "listed", Of(12350000));

        // a moment's lock after a basic attack (1,500 = about 0.6 s) is no cooldown
        var t2 = t0.AddMinutes(2);
        for (int use = 0; use < 3; use++)
        {
            Aion2Cooldowns.NoteOwnUse(12010000, t2.AddSeconds(use * 3));
            Aion2Cooldowns.Receive(new List<CooldownEntry> { new(12010000, 1500) }, t2.AddSeconds(use * 3 + 0.1));
            Aion2Cooldowns.Receive(new List<CooldownEntry> { new(12010000, 0) }, t2.AddSeconds(use * 3 + 1.8));
        }

        Check("Vicious Strike with a moment's lock", Of(12010000) == "NoCooldown", Of(12010000));

        // Judgment shows a cooldown of 12 s after all (a stone): listed again
        int before = Aion2Cooldowns.LearnedVersion;
        Aion2Cooldowns.Receive(new List<CooldownEntry> { new(12240000, 28800) }, t0.AddMinutes(3));
        Check("Judgment with a cooldown after all", Of(12240000) == "listed" && Aion2Cooldowns.LearnedVersion != before, Of(12240000));

        // kept in the settings: seeded at the next start
        Aion2Cooldowns.Reset();
        Aion2Cooldowns.SeedLearned(new[] { 12240000 });
        Check("kept from the last session", Of(12240000) == "NoCooldown", Of(12240000));
        Aion2Cooldowns.Reset();

        Countdown(Check);

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

    /// <summary>
    /// The countdown the assist shows (Aion2Cooldowns.View), on made-up numbers shaped like the game's (4 November 2026): one skill per frame, the counter
    /// falling by about the same step each frame while the capture's timestamps jitter by a few tenths of a second.
    /// </summary>
    private static void Countdown(Action<string, bool, string> check)
    {
        Aion2Cooldowns.Reset();
        var t0 = new DateTime(2026, 11, 4, 18, 0, 0);
        const int a = 12350000, b = 12090000;

        // the whole list at login (a Templar's skills, all ready), then nothing for 15 s, then one skill per frame: the others stay in the list
        int[] login = { 1101, 12130000, 12430000, 12100000, 12230000, 12450000, 12300000, b, a, 12270000 };
        Aion2Cooldowns.Receive(login.Select(id => new CooldownEntry(id, 0)).ToList(), t0);
        Aion2Cooldowns.Receive(new List<CooldownEntry> { new(a, 19600) }, t0.AddSeconds(15));
        Aion2Cooldowns.Receive(new List<CooldownEntry> { new(b, 26400) }, t0.AddSeconds(15.4));
        var list = Aion2Cooldowns.Last.Entries;
        check("one skill per frame after a pause keeps the list", list.Count == login.Length && list.First(e => e.Id == a).Remaining == 19600
            && list.First(e => e.Id == b).Remaining == 26400 && list.Select(e => e.Id).SequenceEqual(login), $"{list.Count} entries");
        check("the skills in the order of the game's list", Aion2Cooldowns.Skills().Take(2).SequenceEqual(new[] { b, a }), string.Join(",", Aion2Cooldowns.Skills().Take(2)));

        // a basic attack is never listed, not even on a lock of 4 s (the owner: not needed, for any class)
        Aion2Cooldowns.Receive(new List<CooldownEntry> { new(12010000, 9600) }, t0.AddSeconds(15.6));
        check("Vicious Strike on a lock of 4 s is not listed, the skills with a cooldown are",
            !Ui.AssistPanel.Listed(12010000) && !Aion2Cooldowns.IsKnown(12010000) && Ui.AssistPanel.Listed(a) && Ui.AssistPanel.Listed(b), "basic attack left out");

        // skill a: 2,600 a frame, frames 0.49 to 1.3 s apart; shown every 0.1 s from the first number to the game's 0
        double[] gaps = { 0.94, 0.61, 0.64, 0.49, 0.67, 1.30, 0.52, 0.69 };
        DateTime Run(DateTime start, int first, out bool steady, out bool readyAtZero, out double firstShown)
        {
            var frames = new List<(DateTime At, int Value)> { (start, first) };
            DateTime at = start;
            for (int i = 0; i < gaps.Length; i++)
            {
                at = at.AddSeconds(gaps[i]);
                frames.Add((at, Math.Max(0, first - 2600 * (i + 1))));
            }

            steady = true;
            readyAtZero = false;
            firstShown = -1;
            double last = double.MaxValue;
            int lastWhole = int.MaxValue;
            int next = 0;
            for (DateTime now = start; now <= at.AddSeconds(0.3); now = now.AddSeconds(0.1))
            {
                while (next < frames.Count && frames[next].At <= now)
                {
                    Aion2Cooldowns.Receive(new List<CooldownEntry> { new(a, frames[next].Value) }, frames[next].At);
                    next++;
                }

                CooldownView view = Aion2Cooldowns.View(a, now);
                if (firstShown < 0)
                {
                    firstShown = view.Seconds;
                }

                if (next == frames.Count)
                {
                    readyAtZero = !view.Running;
                }
                else if (view.Running)
                {
                    steady &= view.Seconds <= last + 1e-9 && view.WholeSeconds <= lastWhole;
                    last = view.Seconds;
                    lastWhole = view.WholeSeconds;
                }
                else
                {
                    steady = false; // ready before the game said so
                }
            }

            return at;
        }

        DateTime end = Run(t0.AddSeconds(15), 19600, out bool steady, out bool ready, out double shownFirst);
        double real = (end - t0.AddSeconds(15)).TotalSeconds; // the game's 0 came 5.86 s after the first number
        check("the shown time never goes up, ready at the game's 0", steady && ready, $"steady {steady}, ready {ready}");
        check("a skill never seen: the first estimate from the assumed speed", Math.Abs(shownFirst - 19600 / Aion2Cooldowns.FallbackRate) < 0.01, $"{shownFirst:F1} s");
        double? typical = Aion2Cooldowns.TypicalRate(a);
        check("the finished use teaches the skill's speed", typical is > 2900 and < 3700, $"{typical:F0} a second (real {19600 / real:F0})");

        // the next use starts right: its first estimate is the speed of the last use, within a second of the real length
        DateTime second = end.AddSeconds(4);
        Run(second, 19600, out bool steady2, out bool ready2, out double shownFirst2);
        check("the next use starts with the learned speed", steady2 && ready2 && Math.Abs(shownFirst2 - real) < 1.0, $"{shownFirst2:F1} s for {real:F1} s");

        // a stone takes 8 s off at once: shown at once, no slow slide; used again: a new countdown
        DateTime third = second.AddSeconds(20);
        Aion2Cooldowns.Receive(new List<CooldownEntry> { new(a, 40000) }, third);
        Aion2Cooldowns.Receive(new List<CooldownEntry> { new(a, 37400) }, third.AddSeconds(0.8));
        double before = Aion2Cooldowns.View(a, third.AddSeconds(0.9)).Seconds;
        Aion2Cooldowns.Receive(new List<CooldownEntry> { new(a, 37400 - 2600 - 8 * 3200) }, third.AddSeconds(1.5));
        CooldownView cut = Aion2Cooldowns.View(a, third.AddSeconds(1.6));
        double rest = (37400 - 2600 - 8 * 3200) / Aion2Cooldowns.TypicalRate(a)!.Value; // what is left at the speed before the jump
        check("time taken off by a stone shows at once, the speed kept", cut.Seconds < before - 2 && Math.Abs(cut.Seconds - rest) < 0.6,
            $"{before:F1} s, then {cut.Seconds:F1} s (about {rest:F1} s left)");
        Aion2Cooldowns.Receive(new List<CooldownEntry> { new(a, 40000) }, third.AddSeconds(2.2));
        CooldownView again = Aion2Cooldowns.View(a, third.AddSeconds(2.3));
        check("used again: a new countdown", again.Running && again.Seconds > 9 && again.Fraction > 0.95, $"{again.Seconds:F1} s");

        // a moment's lock (1,500 = well under 2 s) shows as ready
        Aion2Cooldowns.Receive(new List<CooldownEntry> { new(12300000, 1500) }, third.AddSeconds(3));
        check("a moment's lock is no cooldown", !Aion2Cooldowns.View(12300000, third.AddSeconds(3.1)).Running, "ready");

        // another character (a Gladiator's skill): the Templar's list is gone
        Aion2Cooldowns.Receive(new List<CooldownEntry> { new(11100000, 40000) }, third.AddSeconds(10));
        check("another character's list replaces the last one", Aion2Cooldowns.Last.Entries.Count == 1 && Aion2Cooldowns.Skills().SequenceEqual(new[] { 11100000 }),
            string.Join(",", Aion2Cooldowns.Skills()));
        Aion2Cooldowns.Reset();
    }
}
