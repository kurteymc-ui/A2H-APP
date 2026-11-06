using System.Text.Json;
using AionDPS.Upload;

namespace AionDPS.Combat;

/// <summary>
/// The boss timers' pattern (Combat/BossPattern.cs, Combat/PatternFights.cs, Upload/BossTimers.cs, 6 November 2026): each attack learnt by what brings it in
/// every fight - a time from the start, the attack before it, the boss's health - and named only where the fights agree. The fights are made up: a boss
/// with an opener at 5 s, a follow-up 4 s after it, a phase at half its health with its own follow-up, an attack every 17 s, and one that comes at random.
/// </summary>
public static class SelfCheckBossPattern
{
    private const int Opener = 900001, Follow = 900002, Phase = 900003, PhaseFollow = 900004, Every = 900005, Random = 900006;

    public static bool Run()
    {
        Console.WriteLine("[selftest] Boss timers by the boss's pattern (the attack before, its health, the time):");
        bool ok = true;

        // six fights, killed in 80 to 130 s: the phase comes at half the boss's health, so at 40 to 65 s
        var fights = new List<PatternFight>();
        var rng = new System.Random(7);
        for (int i = 0; i < 6; i++)
        {
            double seconds = 80 + i * 10, jitter = (i % 3 - 1) * 0.15;
            double Health(double at) => 1 - at / seconds;
            var casts = new List<PatternCast>
            {
                new(5 + jitter, Opener, 1, Health(5 + jitter)),
                new(9 + jitter, Follow, 1.5, Health(9 + jitter)),
            };
            for (double at = 17 + jitter; at < seconds; at += 17)
            {
                casts.Add(new PatternCast(at, Every, 1, Health(at)));
            }

            double half = seconds * 0.5 + jitter;
            casts.Add(new PatternCast(half, Phase, 2, Health(half)));
            casts.Add(new PatternCast(half + 3.7, PhaseFollow, 1, Health(half + 3.7)));
            casts.Add(new PatternCast(rng.Next(10, 70), Random, 1, null));
            fights.Add(new PatternFight(seconds, true, casts.OrderBy(c => c.At).ToList()));
        }

        BossPattern pattern = BossPattern.Learn(fights);
        bool learnt = pattern.StepOf(Opener, 1) is { Trigger: PatternTrigger.Time } o && Math.Abs(o.Value - 5) < 0.3
            && pattern.StepOf(Follow, 1) is { Trigger: PatternTrigger.After, AfterSkill: Opener } f && Math.Abs(f.Value - 4) < 0.3
            && pattern.StepOf(Phase, 1) is { Trigger: PatternTrigger.Health } p && Math.Abs(p.Value - 0.5) < 0.02
            && pattern.StepOf(PhaseFollow, 1) is { Trigger: PatternTrigger.After, AfterSkill: Phase } pf && Math.Abs(pf.Value - 3.7) < 0.2
            && pattern.StepOf(Every, 2) is { Trigger: PatternTrigger.Again } e && Math.Abs(e.Value - 17) < 0.3
            && pattern.StepOf(Random, 1) is null;
        Console.WriteLine($"  -> learnt: the opener by time, its follow-up after it, the phase at half health, its follow-up after it, the repeat by its gap, " +
            $"nothing for the random one: {learnt} ({string.Join("; ", pattern.Steps.OrderBy(s => s.SkillId).ThenBy(s => s.Nth).Select(s => $"{s.SkillId % 100}#{s.Nth} {s.Trigger} {s.Value:0.##}"))})");
        ok &= learnt;

        // a new fight, 30 s in at 70 % health losing 1 % a second: the phase in 20 s, its follow-up 3.7 s later; the follow-up of the opener while the opener
        // is still being cast; the next repeat 17 s after the last one
        var seen = new List<PatternCast> { new(5, Opener, 1, null), new(9, Follow, 1.5, null), new(17, Every, 1, null) };
        var at30 = pattern.Next(seen, 30, 0.70, 0.01);
        bool phase = at30.Any(n => n.SkillId == Phase && Math.Abs(n.In - 20) < 0.5) && at30.Any(n => n.SkillId == PhaseFollow && Math.Abs(n.In - 23.7) < 0.5)
            && at30.Any(n => n.SkillId == Every && Math.Abs(n.In - 4) < 0.5) && at30.All(n => n.SkillId != Random);
        var at55 = pattern.Next(seen, 5.5, 0.95, 0.01);
        bool chained = at55.Any(n => n.SkillId == Opener && n.Casting) && at55.Any(n => n.SkillId == Follow && Math.Abs(n.In - 3.5) < 0.4);
        var early = pattern.Next(Array.Empty<PatternCast>(), 1, 1, 0);
        bool start = early.Any(n => n.SkillId == Opener && Math.Abs(n.In - 4) < 0.4) && early.Any(n => n.SkillId == Follow && Math.Abs(n.In - 8) < 0.5);
        Console.WriteLine($"  -> foretold: the phase from the health and its pace, its follow-up after it, the repeat={phase}; a follow-up while its opener is cast={chained}; " +
            $"the opener and its follow-up from the start={start} ({string.Join(", ", at30.Select(n => $"{n.SkillId % 100} in {n.In:0.#}"))})");
        ok &= phase && chained && start;

        // the site's fights read (the versions, health in thousandths, -1 for none), and a fight the site and this PC both have counted once
        string body = "{\"ok\":true,\"npc\":2320471,\"fights\":0,\"casts\":[],\"ids\":[[900001,\"Opener\"],[900002,\"Follow\"]],\"seq\":["
            + "{\"n\":2320471,\"s\":80,\"k\":true,\"c\":[[50,0,10,938],[90,1,15,-1]]},{\"n\":2300471,\"s\":90,\"k\":false,\"c\":[[51,0,10,940],[91,1,15,900],[95,7,1,1]]},{\"n\":\"x\"}]}";
        var names = new Dictionary<int, string>();
        var site = BossTimers.ParseFights(body, names);
        bool read = site.Count == 2 && site[0].Npc == 2320471 && site[0].Fight.Casts.Count == 2 && site[0].Fight.Casts[0].Health is double h0 && Math.Abs(h0 - 0.938) < 1e-9
            && site[0].Fight.Casts[1].Health is null && site[1].Fight.Casts.Count == 2 && !site[1].Fight.Killed && names[900002] == "Follow";
        var local = new List<BossTimers.VersionFight> { new(2320471, new PatternFight(80, true, new[] { new PatternCast(5.02, Opener, 1, null), new PatternCast(9.0, Follow, 1.5, null) })) };
        var merged = BossTimers.Merge(site, local);
        bool once = merged.Count == 2 && merged.Count(m => m.Npc == 2320471) == 1;
        Console.WriteLine($"  -> the site's fights read leniently={read}; the player's own upload and saved fight counted once={once}");
        ok &= read && once;

        // a saved fight: the boss's wind-ups in seconds from its start, its health from the damage done to it (it died: all of it was its health)
        DateTime t0 = new(2026, 11, 6, 21, 0, 0);
        const int boss = 4242;
        var events = new List<DamageEvent>();
        for (int i = 0; i < 100; i++)
        {
            events.Add(new DamageEvent(t0.AddSeconds(i), 1, boss, 1000, false, "Strike", SkillId: 501));
        }

        var windups = new List<WindupRecord>
        {
            new(t0.AddSeconds(49.5), boss, Phase, 1, 2000, 0, 0, 0, false),
            new(t0.AddSeconds(60), boss, Every, 1, 0, 0, 0, 0, false),
            new(t0.AddSeconds(70), 99, Opener, 1, 1000, 0, 0, 0, false),
        };
        var input = new ReportInput("Boss", t0, t0.AddSeconds(100), Array.Empty<ReportPlayer>(), events, Array.Empty<DamageEvent>(), Array.Empty<AionDPS.Combat.Sources.AvoidEvent>(),
            FightExtras.Empty with { Windups = windups }, BossId: boss, BossNpcId: 2320471, Killed: true);
        PatternFight? saved = PatternFights.From(input);
        bool file = saved is { Casts.Count: 1 } && saved.Casts[0].SkillId == Phase && Math.Abs(saved.Casts[0].At - 49.5) < 1e-6 && saved.Casts[0].Health is double hh
            && Math.Abs(hh - 0.5) < 0.011 && Math.Abs(saved.Casts[0].Seconds - 2) < 1e-9;
        Console.WriteLine($"  -> a saved fight: the boss's own wind-ups that took time, its health then from the damage done={file} " +
            $"({saved?.Casts.Count} cast, health {saved?.Casts.FirstOrDefault().Health:0.###})");
        ok &= file;

        // the upload carries the boss's health with each wind-up, for everyone's timers
        string json = JsonSerializer.Serialize(new ReportCastUpload(495, 515, "Phase", Phase, 500), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        bool upload = json.Contains("\"h\":500", StringComparison.Ordinal) && json.Contains("\"i\":900003", StringComparison.Ordinal);
        Console.WriteLine($"  -> the upload's wind-ups carry the boss's health (thousandths): {upload}");
        ok &= upload;
        return ok;
    }
}
