using AionDPS.Aion2;

namespace AionDPS.Combat;

/// <summary>The gathering tool's tally (<see cref="Aion2GatherLog"/>): the grades of a resource from the client's item names, and what counts as gathered.</summary>
public static class SelfCheckGatherTally
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Gathering tally (resources and grades):");
        bool grades = Aion2GatherLog.FamilyOf(610620013) == ("Orichalcum", 0) && Aion2GatherLog.FamilyOf(610620014) == ("Orichalcum", 1)
            && Aion2GatherLog.FamilyOf(610620015) == ("Orichalcum", 2) && Aion2GatherLog.FamilyOf(610620016) == ("Orichalcum", 3)
            && Aion2GatherLog.FamilyOf(610670045) == ("Aria", 1) && Aion2GatherLog.FamilyOf(610650018) == ("Asvata", 3) && Aion2GatherLog.FamilyOf(510116006) is null;
        bool[] aria = Aion2GatherLog.TiersOf("Aria");
        bool herbs = aria[0] && aria[1] && !aria[2] && !aria[3] && Aion2GatherLog.IsFresh("Aria") && !Aion2GatherLog.IsFresh("Orichalcum");
        Console.WriteLine($"  -> Orichalcum Ore, Fine, Pure, Radiant are one resource at grades 0-3, Fresh Aria is grade 1, a potion is none: {grades}; herbs have only normal and Fresh: {herbs}");

        Aion2GatherLog.Clear(false);
        DateTime t = new(2026, 10, 31, 21, 0, 0);
        var near = new[] { "Orichalcum" };
        Aion2GatherLog.NoteGains(new[] { (610620013, 3L), (610620016, 1L) }, t, near);           // one gathering: 3 normal and a Radiant
        Aion2GatherLog.NoteGains(new[] { (610620014, 1L) }, t.AddSeconds(5), near);               // a second one: a Fine
        Aion2GatherLog.NoteGains(new[] { (610620013, 50L) }, t.AddMinutes(5), Array.Empty<string>()); // bought or from the mail: no gathering place near
        Aion2GatherLog.NoteGains(new[] { (610660025, 2L) }, t.AddMinutes(6), near);               // a gem while only ore is near: not this place's
        Aion2GatherLog.NoteGains(new[] { (510116006, 1L) }, t.AddMinutes(7), near);               // a potion: no resource
        GatherTally? ore = Aion2GatherLog.Tallies(session: false).FirstOrDefault(x => x.Key == "Orichalcum");
        bool counted = ore is { Gathers: 2 } && ore.ByTier.SequenceEqual(new long[] { 3, 1, 0, 1 }) && Aion2GatherLog.Tallies(false).Count == 1;
        Aion2GatherLog.Clear(session: true);
        bool sessionApart = Aion2GatherLog.Tallies(session: true).Count == 0 && Aion2GatherLog.Tallies(session: false).Count == 1;
        Console.WriteLine($"  -> before any start is seen: two gatherings of ore by grade (3 normal, 1 Fine, 1 Radiant), nothing bought, from the mail or of another place: {counted}; the session clears apart from all time: {sessionApart}");

        // the own gathering starts seen: each is a try, what of that resource comes right after is its result, a try that brings nothing failed
        Aion2GatherLog.Clear(false);
        DateTime s = new(2026, 1, 5, 10, 0, 0);
        Aion2GatherLog.NoteAttempt("Orichalcum", 77, s);
        Aion2GatherLog.NoteGains(new[] { (610620013, 2L), (610620014, 1L) }, s.AddSeconds(3), Array.Empty<string>()); // 2 normal and a Fine
        Aion2GatherLog.NoteGains(new[] { (610660025, 4L) }, s.AddSeconds(4), Array.Empty<string>());                  // a gem while gathering ore: not its result
        Aion2GatherLog.NoteAttempt("Orichalcum", 78, s.AddSeconds(10));                                                 // nothing comes of it
        Aion2GatherLog.NoteAttempt("Orichalcum", 79, s.AddSeconds(20));                                                 // so 78 failed
        Aion2GatherLog.NoteAttempt("Orichalcum", 79, s.AddSeconds(20.5));                                               // the same start told twice
        Aion2GatherLog.NoteGains(new[] { (610620013, 1L) }, s.AddSeconds(23), Array.Empty<string>());
        Aion2GatherLog.NoteGains(new[] { (610620013, 1L) }, s.AddSeconds(24), Array.Empty<string>());                  // a second frame of the same gathering
        Aion2GatherLog.NoteGains(new[] { (610620013, 40L) }, s.AddMinutes(5), new[] { "Orichalcum" });                // bought next to a place of ore: no try under way
        Aion2GatherLog.NoteAttempt("Orichalcum", 80, s.AddMinutes(6));                                                  // broken off: it fails once its time is up
        GatherTally? tried = Aion2GatherLog.Tallies(session: true).FirstOrDefault(x => x.Key == "Orichalcum");
        bool attempts = tried is { Tries: 4, Fails: 2, Gathers: 2 } && tried.ByTier.SequenceEqual(new long[] { 4, 1, 0, 0 }) && Aion2GatherLog.Tallies(true).Count == 1;
        Console.WriteLine($"  -> four starts: two brought ore (4 normal, 1 Fine; a gem in between and ore bought later are not counted), two brought nothing and failed: {attempts}"
            + (tried is null ? "" : $" (tries {tried.Tries}, failed {tried.Fails}, gathers {tried.Gathers}, grades {string.Join(",", tried.ByTier)})"));

        // the roll the game sends as a gathering starts (0x318d): shown until the result comes, then checked against it
        Aion2GatherLog.Clear(false);
        DateTime r = new(2026, 1, 6, 10, 0, 0);
        Aion2GatherLog.NoteRoll(90, 1, 7, 1, r.AddMilliseconds(-20));                                                  // told a moment before the start
        Aion2GatherLog.NoteAttempt("Ruby", 90, r);
        GatherForecast? fine = Aion2GatherLog.Forecast(r.AddSeconds(2));
        Aion2GatherLog.NoteGains(new[] { (610660030, 1L) }, r.AddSeconds(8), Array.Empty<string>());                   // a Fine Ruby came, as rolled
        bool goneAfter = Aion2GatherLog.Forecast(r.AddSeconds(9)) is null;
        Aion2GatherLog.NoteAttempt("Ruby", 91, r.AddSeconds(20));
        Aion2GatherLog.NoteRoll(91, 2, 0, 0, r.AddSeconds(20.05));                                                     // outcome 0: it gives nothing
        GatherForecast? fail = Aion2GatherLog.Forecast(r.AddSeconds(21));
        Aion2GatherLog.NoteAttempt("Ruby", 92, r.AddSeconds(40));                                                      // nothing came of 91
        bool rolls = fine is { Key: "Ruby", Tier: 1, Fails: false } && goneAfter && fail is { Fails: true } && Aion2GatherLog.RollRecord() == (2, 2);
        Console.WriteLine($"  -> the roll read as a gathering starts shows its grade (Fine Ruby) or that it fails, until the result; both rolls were right: {rolls}");
        Aion2GatherLog.Clear(false);
        return grades && herbs && counted && sessionApart && attempts && rolls;
    }
}
