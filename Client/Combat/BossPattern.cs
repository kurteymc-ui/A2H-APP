namespace AionDPS.Combat;

/// <summary>One attack of a boss in a fight: when its wind-up began (seconds after the boss's start, the first hit on it), the skill, how long the wind-up
/// took, and the boss's health then (a share, 0 to 1; null when not known).</summary>
public readonly record struct PatternCast(double At, int SkillId, double Seconds, double? Health);

/// <summary>A fight a boss's pattern is learnt from: how long it lasted, whether the boss died, its attacks in the order they began.</summary>
public sealed record PatternFight(double Seconds, bool Killed, IReadOnlyList<PatternCast> Casts);

/// <summary>What brings an attack: a time after the boss's start, a time after the same attack's last one, a time after another attack began, or a share of
/// the boss's health.</summary>
public enum PatternTrigger
{
    Time,
    Again,
    After,
    Health,
}

/// <summary>
/// When the <see cref="Nth"/> time of an attack comes in a fight: by <see cref="Trigger"/> at <see cref="Value"/> (seconds, or a health share), after the
/// <see cref="AfterNth"/>-th time of <see cref="AfterSkill"/> for <see cref="PatternTrigger.After"/>. <see cref="Spread"/>: how far apart the fights
/// were (seconds; the median distance from the middle), <see cref="Fights"/>: how many fights had it, <see cref="Seconds"/>: how long its wind-up takes.
/// </summary>
public sealed record PatternStep(int SkillId, int Nth, PatternTrigger Trigger, double Value, int AfterSkill, int AfterNth, double Spread, int Fights, double Seconds);

/// <summary>An attack the boss timers name: the skill, the seconds until it comes (0 while it is being cast) and what the time rests on.</summary>
public readonly record struct PatternNext(int SkillId, double In, bool Casting, PatternTrigger Trigger);

/// <summary>
/// A boss's attacks as its fights show them (6 November 2026, the owner: "make sure it's accurate ... make sure other raids have it as well"). A boss runs
/// its attacks in set orders: one attack a set time after another (Transcendent Bakarma's Wave of Death 3.7 s after Subspace Dominion, every fight), and
/// its phases at a share of its health (Ultimate Berk's at half, Bakarma's second Winding Waterspout at about 40 %) - not at a time from the start, which
/// moves with the group's damage. So each attack is learnt by the time it comes in the fight (its first, second ... time), from what brought it most
/// alike in every fight: a time after the boss's start, after the same attack's last one, after another attack, or a share of the boss's health
/// (turned into seconds with the pace the boss's health falls at now). An attack is named only when three fights of four that had it agreed
/// (<see cref="Agree"/>) to within <see cref="MaxSpread"/> seconds (<see cref="MaxAfterSpread"/> after another attack), in at least
/// <see cref="MinFights"/> fights: a wrong time is worse than none. Checked on the owner's own fights, each left out in turn and foretold from the others:
/// of the times named 5 to 15 s ahead, 58 to 85 % came within 2 s (Transcendent Bakarma 84 %, Ultimate Berk 78 %), where the old medians from the
/// boss's start had 11 to 46 %. It names fewer attacks while a boss has few fights, and more as the fights come in. Pure.
/// </summary>
public sealed class BossPattern
{
    /// <summary>The fights a step by time, by the same attack again or by health needs.</summary>
    public const int MinFights = 4;

    /// <summary>The fights a step after another attack needs (that order is the same in every fight, its time hardly moves).</summary>
    public const int MinAfter = 3;

    /// <summary>How near the middle a fight must be to agree: seconds (after another attack: <see cref="MaxAfterSpread"/>).</summary>
    public const double MaxSpread = 2.0, MaxAfterSpread = 1.0;

    /// <summary>The share of the fights that had the attack this often that must agree on what brings it.</summary>
    public const double Agree = 0.75;

    /// <summary>A follow-up is looked for after this many attacks before it (another attack may come between them).</summary>
    public const int AnchorsBack = 3;

    /// <summary>How far ahead an attack is named, and how long one stays named after its time passed (it may come a little late).</summary>
    public const double Ahead = 60, Late = 3;

    /// <summary>A health step is still waited for this far below its share (the readings come in steps).</summary>
    public const double HealthSlack = 0.03;

    private readonly Dictionary<(int Skill, int Nth), PatternStep> _steps;
    private readonly HashSet<int> _skills;

    private BossPattern(Dictionary<(int, int), PatternStep> steps, int fights, double pace)
    {
        _steps = steps;
        _skills = steps.Keys.Select(k => k.Item1).ToHashSet();
        Fights = fights;
        Pace = pace;
    }

    /// <summary>How many fights it was learnt from.</summary>
    public int Fights { get; }

    /// <summary>The health share the boss lost per second in the middle of those fights (for a health step before the pace of this fight is known).</summary>
    public double Pace { get; }

    public IReadOnlyCollection<PatternStep> Steps => _steps.Values;

    public static BossPattern Empty { get; } = new(new Dictionary<(int, int), PatternStep>(), 0, 0);

    private readonly record struct Sample(double At, double? Health, double? Own, IReadOnlyList<(int Skill, int Nth, double At)> Before, double Pace, double Seconds);

    /// <summary>Learns the steps from the fights (any order; a fight without attacks counts for nothing).</summary>
    public static BossPattern Learn(IReadOnlyList<PatternFight> fights)
    {
        var used = fights.Where(f => f.Casts.Count > 0 && f.Seconds > 0).ToList();
        var samples = new Dictionary<(int, int), List<Sample>>();
        var paces = new List<double>();
        foreach (PatternFight fight in used)
        {
            double pace = PaceOf(fight);
            if (pace > 0)
            {
                paces.Add(pace);
            }

            var count = new Dictionary<int, int>();
            var last = new Dictionary<int, double>();
            var before = new List<(int, int, double)>();
            foreach (PatternCast c in fight.Casts.OrderBy(c => c.At))
            {
                int nth = count[c.SkillId] = count.GetValueOrDefault(c.SkillId) + 1;
                if (!samples.TryGetValue((c.SkillId, nth), out List<Sample>? list))
                {
                    samples[(c.SkillId, nth)] = list = new List<Sample>();
                }

                list.Add(new Sample(c.At, c.Health, last.TryGetValue(c.SkillId, out double own) ? own : null, before.TakeLast(AnchorsBack).ToList(), pace, c.Seconds));
                last[c.SkillId] = c.At;
                before.Add((c.SkillId, nth, c.At));
            }
        }

        int half = (int)Math.Ceiling(used.Count * 0.5);
        var steps = new Dictionary<(int, int), PatternStep>();
        foreach (((int skill, int nth), List<Sample> s) in samples)
        {
            if (s.Count < Math.Max(Math.Max(2, MinAfter), half))
            {
                continue; // an attack most fights didn't have this often
            }

            double seconds = Median(s.Select(x => x.Seconds).ToList());
            var options = new List<(PatternStep Step, double Share)>();

            // the fights agree when most of those that had the attack this often (Agree) are near the middle (a few seconds; after another attack, one);
            // a share of health counts in seconds at each fight's own pace, a little worse than a time, as the pace now is an estimate
            void Consider(PatternTrigger trigger, List<double> values, List<double>? scale = null, int afterSkill = 0, int afterNth = 0)
            {
                if (values.Count == 0)
                {
                    return;
                }

                double middle = Median(values.ToList()), near = trigger == PatternTrigger.After ? MaxAfterSpread : MaxSpread;
                var off = values.Select((v, i) => Math.Abs(v - middle) * (scale?[i] ?? 1)).Where(d => d <= near).ToList();
                int need = trigger == PatternTrigger.After ? MinAfter : MinFights;
                if (off.Count >= need && off.Count >= Agree * s.Count)
                {
                    double spread = Median(off) + (trigger == PatternTrigger.Health ? 0.5 : 0);
                    options.Add((new PatternStep(skill, nth, trigger, middle, afterSkill, afterNth, spread, s.Count, seconds), Math.Round((double)off.Count / s.Count, 2)));
                }
            }

            Consider(PatternTrigger.Time, s.Select(x => x.At).ToList());
            Consider(PatternTrigger.Again, s.Where(x => x.Own is not null).Select(x => x.At - x.Own!.Value).ToList());
            foreach (var anchor in s.SelectMany(x => x.Before.Select(b => (b.Skill, b.Nth))).Distinct())
            {
                var after = s.SelectMany(x => x.Before.Where(b => b.Skill == anchor.Skill && b.Nth == anchor.Nth).Select(b => x.At - b.At)).ToList();
                Consider(PatternTrigger.After, after, null, anchor.Skill, anchor.Nth);
            }

            var health = s.Where(x => x.Health is not null && x.Pace > 0).ToList();
            Consider(PatternTrigger.Health, health.Select(x => x.Health!.Value).ToList(), health.Select(x => 1.3 / x.Pace).ToList());

            if (options.Count > 0)
            {
                steps[(skill, nth)] = options.OrderByDescending(o => o.Share).ThenBy(o => o.Step.Spread).ThenBy(o => o.Step.Trigger).First().Step;
            }
        }

        return new BossPattern(steps, used.Count, paces.Count > 0 ? Median(paces) : 0);
    }

    /// <summary>The step for the <paramref name="nth"/> time of an attack, or null.</summary>
    public PatternStep? StepOf(int skill, int nth) => _steps.GetValueOrDefault((skill, nth));

    /// <summary>
    /// The attacks coming, soonest first: those being cast now (0 s), then each attack's next time by its step, within <see cref="Ahead"/> seconds.
    /// <paramref name="seen"/>: this fight's attacks so far (At in seconds after the boss's start, in any order); <paramref name="t"/>: seconds now;
    /// <paramref name="health"/>: the boss's health share now (null: unknown); <paramref name="pace"/>: the share it loses per second now (0: unknown,
    /// the learnt <see cref="Pace"/> is taken).
    /// </summary>
    public IReadOnlyList<PatternNext> Next(IReadOnlyList<PatternCast> seen, double t, double? health, double pace)
    {
        var count = new Dictionary<int, int>();
        var last = new Dictionary<int, PatternCast>();
        var began = new Dictionary<(int, int), double>();
        foreach (PatternCast c in seen.Where(c => c.At <= t).OrderBy(c => c.At))
        {
            int nth = count[c.SkillId] = count.GetValueOrDefault(c.SkillId) + 1;
            last[c.SkillId] = c;
            began[(c.SkillId, nth)] = c.At;
        }

        var due = new List<PatternNext>();
        foreach (PatternCast c in last.Values)
        {
            if (_skills.Contains(c.SkillId) && t - c.At < Math.Max(c.Seconds, 0.5))
            {
                due.Add(new PatternNext(c.SkillId, 0, true, PatternTrigger.Time)); // being cast now
            }
        }

        double rate = pace > 0 ? pace : Pace;
        var when = new Dictionary<(int, int), (double At, PatternTrigger Trigger)>();
        foreach (int skill in _skills)
        {
            int nth = count.GetValueOrDefault(skill) + 1;
            if (_steps.GetValueOrDefault((skill, nth)) is not { } step || due.Any(d => d.SkillId == skill))
            {
                continue;
            }

            double? at = step.Trigger switch
            {
                PatternTrigger.Time => step.Value,
                PatternTrigger.Again => last.TryGetValue(skill, out PatternCast own) ? own.At + step.Value : null,
                PatternTrigger.After => began.TryGetValue((step.AfterSkill, step.AfterNth), out double anchor) ? anchor + step.Value : null,
                _ => health is double h && rate > 0 && h > step.Value - HealthSlack ? t + Math.Max(0, h - step.Value) / rate : null,
            };
            if (at is double a)
            {
                when[(skill, nth)] = (a, step.Trigger);
            }
        }

        // an attack that comes after another one that is itself coming: from that one's time (Wave of Death after the Subspace Dominion still to come)
        for (int round = 0; round < 6; round++)
        {
            bool grew = false;
            foreach (int skill in _skills)
            {
                int nth = count.GetValueOrDefault(skill) + 1;
                if (!when.ContainsKey((skill, nth)) && _steps.GetValueOrDefault((skill, nth)) is { Trigger: PatternTrigger.After } step
                    && when.TryGetValue((step.AfterSkill, step.AfterNth), out var anchor))
                {
                    when[(skill, nth)] = (anchor.At + step.Value, PatternTrigger.After);
                    grew = true;
                }
            }

            if (!grew)
            {
                break;
            }
        }

        foreach (((int skill, _), (double at, PatternTrigger trigger)) in when)
        {
            if (at - t <= Ahead && at - t >= -Late)
            {
                due.Add(new PatternNext(skill, Math.Max(0, at - t), false, trigger));
            }
        }

        return due.OrderBy(d => d.In).ThenBy(d => d.Casting ? 0 : 1).ToList();
    }

    /// <summary>The health share a fight's boss lost per second: a boss that died went from all to none; else from its attacks' readings.</summary>
    private static double PaceOf(PatternFight fight)
    {
        if (fight.Killed)
        {
            return 1 / fight.Seconds;
        }

        var known = fight.Casts.Where(c => c.Health is not null).OrderBy(c => c.At).ToList();
        if (known.Count >= 2 && known[^1].At - known[0].At >= 10)
        {
            double lost = known[0].Health!.Value - known[^1].Health!.Value;
            return lost > 0 ? lost / (known[^1].At - known[0].At) : 0;
        }

        return 0;
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        values.Sort();
        int m = values.Count / 2;
        return values.Count % 2 == 1 ? values[m] : (values[m - 1] + values[m]) / 2;
    }
}
