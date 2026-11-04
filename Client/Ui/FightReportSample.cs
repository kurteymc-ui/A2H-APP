using AionDPS.Combat;
using AionDPS.Combat.Sources;

namespace AionDPS.Ui;

/// <summary>A made-up boss fight of 55 seconds for the report's pictures and checks: five players, a healer, party buffs, debuffs on the boss, shields,
/// avoided attacks, casts with wind-ups, a stagger and one death. Seeded, so it is the same every time.</summary>
internal static class FightReportSample
{
    public const int Boss = 9001;

    public static ReportInput Input()
    {
        var rng = new Random(7);
        DateTime t0 = new(2026, 10, 30, 18, 7, 0);
        var players = new List<ReportPlayer>
        {
            new(1, "Jinwoo", "Gladiator", 191_210), new(2, "Goku", "Chanter", 154_876), new(3, "Ichigo", "Templar", 158_758, IsSelf: true),
            new(4, "Naruto", "Assassin", 189_896), new(5, "Tsubasa", "Sorcerer", 182_839),
        };
        var names = players.ToDictionary(p => p.Id, p => p.Name);
        names[Boss] = "Talisra of the Void";
        var skills = new Dictionary<int, (string Name, int Id, double Weight)[]>
        {
            [1] = new[] { ("Rending Blow", 11010000, 4.0), ("Basic Attack", 11000001, 2.0), ("Experienced Counterstrike", 11780000, 3.0), ("Sharp Strike", 11020000, 2.0) },
            [2] = new[] { ("Basic Attack", 18000102, 2.0), ("Earth's Promise", 18780000, 1.0), ("Power of the Storm", 18250000, 1.0) },
            [3] = new[] { ("Vicious Strike", 12010000, 3.0), ("Basic Attack", 12000001, 2.0), ("Fury", 12780000, 1.0), ("Shield Bash", 12020000, 2.0) },
            [4] = new[] { ("Quick Slice", 13010000, 4.0), ("Basic Attack", 13000001, 2.0), ("Ambush", 13020000, 3.0), ("Venom Strike", 13030000, 2.0), ("Shadow Fall", 13040000, 1.5) },
            [5] = new[] { ("Flame Scattershot", 15010000, 4.0), ("Basic Attack", 15000001, 1.0), ("Frost Burst", 15020000, 2.5), ("Delayed Damage", 15030000, 1.5) },
        };
        var power = new Dictionary<int, double> { [1] = 1.15, [2] = 0.55, [3] = 0.6, [4] = 1.2, [5] = 1.05 };
        var events = new List<DamageEvent>();
        var uses = new List<SkillUse>();
        for (double t = 0.2; t < 55; t += 0.35)
        {
            foreach (ReportPlayer p in players)
            {
                if (rng.NextDouble() > 0.55)
                {
                    continue;
                }

                var options = skills[p.Id];
                double pick = rng.NextDouble() * options.Sum(o => o.Weight);
                var skill = options[0];
                foreach (var o in options)
                {
                    pick -= o.Weight;
                    if (pick <= 0)
                    {
                        skill = o;
                        break;
                    }
                }

                // the burst in the stagger window (14 to 20 s) and a quiet stretch while the boss casts (22 to 41 s)
                double phase = t is > 14 and < 20 ? 1.8 : t is > 22 and < 41 ? 0.7 : 1.0;
                long amount = (long)(rng.Next(6000, 14000) * power[p.Id] * phase * (t < 4 ? t / 4 : 1));
                bool crit = rng.NextDouble() < 0.3;
                var kinds = (rng.NextDouble() < 0.4 ? HitKinds.Back : HitKinds.Front) | (rng.NextDouble() < 0.12 ? HitKinds.Perfect : 0) | (rng.NextDouble() < 0.1 ? HitKinds.Double : 0);
                DateTime at = t0.AddSeconds(t + rng.NextDouble() * 0.2);
                events.Add(new DamageEvent(at, p.Id, Boss, crit ? amount * 2 : amount, false, skill.Name, crit, SkillId: skill.Id, Kinds: kinds));
                if (rng.NextDouble() < 0.35)
                {
                    uses.Add(new SkillUse(at.AddMilliseconds(-150), p.Id, skill.Id));
                }
            }
        }

        // the healer's heals and the boss's hits on the group
        for (double t = 1; t < 55; t += 1.7)
        {
            int target = 1 + rng.Next(5);
            events.Add(new DamageEvent(t0.AddSeconds(t), 2, target, rng.Next(800, 2600), true, t % 3 < 1.5 ? "Recovery Spell" : "Sprint Mantra", SkillId: 18010000));
            events.Add(new DamageEvent(t0.AddSeconds(t + 0.3), 3, 3, rng.Next(300, 900), true, "Superior Life Potion"));
        }

        foreach (double t in new[] { 1.0, 2.5, 4.0, 5.2, 18.0, 19.0, 20.1 })
        {
            int target = t < 10 ? 3 : 1 + rng.Next(5);
            events.Add(new DamageEvent(t0.AddSeconds(t), Boss, target, rng.Next(1500, 4200), false, t < 10 ? "Attack" : "Cast Fall", SkillId: 0));
        }

        var shields = new List<DamageEvent>
        {
            new(t0.AddSeconds(2.6), Boss, 3, 3110, true, "Protection Circle"), new(t0.AddSeconds(18.1), Boss, 1, 2844, true, "Protection Circle"),
            new(t0.AddSeconds(19.2), Boss, 4, 1200, true, "Protection Circle"),
        };
        var avoids = new List<AvoidEvent>
        {
            new(t0.AddSeconds(3.0), Boss, 3, AvoidKind.Block, "Attack"), new(t0.AddSeconds(3.8), Boss, 3, AvoidKind.Parry, "Attack"), new(t0.AddSeconds(4.5), Boss, 3, AvoidKind.Dodge, "Attack"),
        };

        // party buffs (the Chanter's mantras, the Templar's Fury), debuffs on the boss and the boss's own effect
        var buffs = new List<BuffEvent>();
        int instance = 1;
        foreach (ReportPlayer p in players)
        {
            buffs.Add(new BuffEvent(t0.AddSeconds(-5), BuffKind.On, p.Id, instance++, 181_900_010, 2, -1, 15));
            buffs.Add(new BuffEvent(t0.AddSeconds(12), BuffKind.On, p.Id, instance++, 182_500_010, 2, 20_000, 20));
            buffs.Add(new BuffEvent(t0.AddSeconds(30), BuffKind.On, p.Id, instance++, 127_800_010, 3, 15_000, 20));
            buffs.Add(new BuffEvent(t0.AddSeconds(-2), BuffKind.On, p.Id, instance++, 174_100_010, 2, 50_000, 10));
        }

        buffs.Add(new BuffEvent(t0.AddSeconds(2), BuffKind.On, Boss, instance++, 187_800_010, 2, 25_000, 18));
        buffs.Add(new BuffEvent(t0.AddSeconds(5), BuffKind.On, Boss, instance++, 174_000_010, 2, 30_000, 20));
        buffs.Add(new BuffEvent(t0.AddSeconds(-10), BuffKind.On, Boss, instance++, 100_000_010, Boss, -1, 1));

        var windups = new List<WindupRecord>();
        foreach ((double at, double length) in new[] { (5.0, 1.0), (9.5, 1.0), (18.5, 1.3), (22.0, 19.0), (52.0, 1.3) })
        {
            windups.Add(new WindupRecord(t0.AddSeconds(at), Boss, 90_000_100, 3, (int)(length * 1000), 0, 0, 0, false));
            uses.Add(new SkillUse(t0.AddSeconds(at), Boss, 90_000_100));
        }

        var targets = new List<TargetChange> { new(t0, Boss, 3), new(t0.AddSeconds(24), Boss, 1), new(t0.AddSeconds(31), Boss, 3) };
        var staggers = new List<StaggerEvent> { new(t0.AddSeconds(14), Boss, 100, 100, true), new(t0.AddSeconds(20), Boss, 0, 100, false) };
        var deaths = new List<DeathRecord> { new(t0.AddSeconds(46), 5, t0.AddSeconds(50)) };
        var extras = new FightExtras(uses, buffs, staggers, names, Windups: windups, Deaths: deaths, Targets: targets);
        return new ReportInput(names[Boss], t0, t0.AddSeconds(55), players, events, shields, avoids, extras, Boss, null, Killed: true,
            NameOf: id => names.GetValueOrDefault(id), Targets: new HashSet<int> { Boss });
    }
}
