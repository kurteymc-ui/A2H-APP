using System.Text.Json;
using AionDPS.Upload;

namespace AionDPS.Combat;

/// <summary>Boss timers (Upload/BossTimers.cs): the site's answer read, and the casts the overlay names when.</summary>
public static class SelfCheckBossTimers
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Boss timers (learnt from uploads):");
        const string body = "{\"ok\":true,\"npc\":4004,\"fights\":12,\"casts\":[{\"id\":987650009,\"name\":\"Slam\",\"fights\":12,\"first\":10,\"every\":20,\"dur\":1},"
            + "{\"id\":987650077,\"name\":\"Quake\",\"fights\":11,\"first\":31,\"every\":60,\"dur\":3},{\"id\":987650005,\"name\":\"Roar\",\"fights\":4,\"first\":100,\"every\":null,\"dur\":2}]}";
        var (casts, fights) = BossTimers.Parse(body);
        bool parsed = fights == 12 && casts.Count == 3 && casts[1] is { Id: 987650077, Name: "Quake", First: 31, Every: 60, Dur: 3 } && casts[2].Every is null;
        Console.WriteLine($"  -> the site's answer: {casts.Count} casts of {fights} fights: {parsed}");

        var t0 = new DateTime(2026, 10, 31, 20, 0, 0);
        var seen = new Dictionary<int, DateTime> { [987650009] = t0.AddSeconds(10) };
        var at20 = BossTimers.Upcoming(casts, seen, 20, t0);
        bool next = at20.Count == 2 && at20[0] is { Name: "Slam", In: 10, Casting: false } && at20[1] is { Name: "Quake", In: 11 };
        seen[987650077] = t0.AddSeconds(31);
        var at32 = BossTimers.Upcoming(casts, seen, 32, t0);
        bool casting = at32.Any(c => c is { Name: "Quake", Casting: true }) && at32.Any(c => c is { Name: "Slam", In: 0 });
        var at50 = BossTimers.Upcoming(casts, new Dictionary<int, DateTime>(), 50, t0);
        bool passed = at50.Count == 1 && at50[0] is { Name: "Roar", In: 50 }; // Slam and Quake were due long ago unseen (another phase): left out
        var at45 = BossTimers.Upcoming(casts, new Dictionary<int, DateTime>(), 45, t0);
        bool ahead = at45.Count == 1 && at45[0] is { Name: "Roar", In: 55 };
        Console.WriteLine($"  -> next: a seen cast after its interval, an unseen one at its first time={next}; one being cast={casting}; passed unseen left out={passed}; a minute ahead={ahead}");

        // an odd answer is read a cast at a time, never thrown: a whole number written 12.0, a missing name, a text where a number goes
        var (odd, oddFights) = BossTimers.Parse("{\"fights\":3.0,\"casts\":[{\"id\":987650001.0,\"first\":4},{\"id\":\"x\",\"first\":5},{\"id\":987650002,\"first\":null}]}");
        bool lenient = oddFights == 3 && odd.Count == 1 && odd[0] is { Id: 987650001, Name: "", First: 4, Every: null };
        // the wind-ups the timers count from: every one recorded, also the short ones no display caught
        var combat = new AionDPS.Aion2.Aion2CombatState();
        combat.NoteWindup(new AionDPS.Aion2.CastNotice(5, 77, 0, t0.AddSeconds(10), 300, 0, 0, 0, 0, false));
        combat.NoteWindup(new AionDPS.Aion2.CastNotice(5, 77, 0, t0.AddSeconds(40), 300, 0, 0, 0, 0, false));
        combat.NoteWindup(new AionDPS.Aion2.CastNotice(6, 77, 0, t0.AddSeconds(50), 300, 0, 0, 0, 0, false));
        var lastOf = combat.LastWindups(5, t0);
        bool recorded = lastOf.Count == 1 && lastOf[77] == t0.AddSeconds(40) && combat.LastWindups(5, t0.AddSeconds(45)).Count == 0;
        Console.WriteLine($"  -> an odd answer read leniently={lenient}; the boss's own wind-ups since its start={recorded}");

        string json = JsonSerializer.Serialize(new ReportCastUpload(220, 410, "Quake", 987650077), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        bool upload = json.Contains("\"i\":987650077", StringComparison.Ordinal);
        Console.WriteLine($"  -> the report's cast bars carry the skill id for the site: {upload}");
        return parsed && next && casting && passed && ahead && upload && lenient && recorded;
    }
}
