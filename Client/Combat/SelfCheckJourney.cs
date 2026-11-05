using AionDPS.History;
using AionDPS.Ui;

namespace AionDPS.Combat;

/// <summary>The Journey page's play time (History/JourneyLog.cs) and its periods (5 November 2026).</summary>
public static class SelfCheckJourney
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Journey (play time by day and session, the periods):");
        JourneyLog.UseForPictures(new Dictionary<string, JourneyCharacter>(StringComparer.Ordinal));
        var t0 = new DateTime(2026, 11, 5, 20, 0, 0);
        string me = JourneyLog.Key("Aerin", 2302);
        for (int m = 0; m < 30; m++)
        {
            JourneyLog.Minute(me, fighting: m is >= 10 and < 20, moved: m < 10, t0.AddMinutes(m));
        }

        for (int m = 0; m < 5; m++)
        {
            JourneyLog.Minute(me, false, true, t0.AddMinutes(60 + m)); // back after half an hour: a new session
        }

        JourneyCharacter c = JourneyLog.Of(me);
        JourneyDay day = c.Days["2026-11-05"];
        bool minutes = day.InGame == 35 && day.Fighting == 10 && day.Idle == 10;
        Console.WriteLine($"  -> a day: 35 minutes in game, 10 fighting, 10 idle (neither fighting nor moving): {minutes} ({day.InGame}/{day.Fighting}/{day.Idle})");
        bool sessions = c.Sessions.Count == 2 && (c.Sessions[0].End - c.Sessions[0].Start).TotalMinutes is >= 29 and <= 31;
        Console.WriteLine($"  -> two sessions (a gap of 30 minutes splits them): {sessions} ({c.Sessions.Count})");

        var now = new DateTime(2026, 11, 5, 21, 30, 0);
        var who = new JourneyWho("Aerin", 2302, "Templar");
        (DateTime from7, DateTime to7) = JourneyData.Range(JourneySpan.Week7, now, who, null, null);
        (DateTime fromS, _) = JourneyData.Range(JourneySpan.Session, now, who, null, null);
        (DateTime fromM, _) = JourneyData.Range(JourneySpan.ThisMonth, now, who, null, null);
        (DateTime fromC, DateTime toC) = JourneyData.Range(JourneySpan.Custom, now, who, new DateTime(2026, 11, 1), new DateTime(2026, 11, 3));
        bool periods = from7 == new DateTime(2026, 10, 30) && to7 == now && fromS == t0.AddMinutes(59).ToUniversalTime().ToLocalTime() && fromM == new DateTime(2026, 11, 1)
                       && fromC == new DateTime(2026, 11, 1) && toC == new DateTime(2026, 11, 4);
        Console.WriteLine($"  -> 7 days from the 30th, the session from its start, the month from the 1st, picked dates to the end of the last day: {periods} ({from7:d} {fromS:t} {fromM:d} {fromC:d}-{toC:d})");
        JourneyLog.UseForPictures(new Dictionary<string, JourneyCharacter>(StringComparer.Ordinal));
        return minutes && sessions && periods;
    }
}
