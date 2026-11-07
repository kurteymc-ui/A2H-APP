using AionDPS.Aion2.Protocol;
using AionDPS.Combat.Sources;

namespace AionDPS.Combat;

/// <summary>A player of a fight report: the id their events carry, name, class and the combat power the game showed of them (null when unknown).</summary>
public sealed record ReportPlayer(int Id, string Name, string ClassName, int? CombatPower = null, bool IsSelf = false);

/// <summary>
/// What a fight report is made of: the fight's window, its players, every damage and heal event in it (shields apart), the avoided attacks and the
/// capture's extras (skills used, effects, stagger, casts, deaths, who the boss aimed at). <see cref="Targets"/>: the damage that counts (a boss and its
/// adds); null counts every hit on anything that is no player of the fight.
/// </summary>
public sealed record ReportInput(
    string Title,
    DateTime Start,
    DateTime End,
    IReadOnlyList<ReportPlayer> Players,
    IReadOnlyList<DamageEvent> Events,
    IReadOnlyList<DamageEvent> Shields,
    IReadOnlyList<AvoidEvent> Avoids,
    FightExtras Extras,
    int? BossId = null,
    int? BossNpcId = null,
    bool Killed = false,
    Func<int, string?>? NameOf = null,
    IReadOnlySet<int>? Targets = null);

/// <summary>One skill's line of a report table. The rates are percents (0 to 100); a hit-quality rate is null when the game said nothing of the hits.</summary>
public sealed record ReportSkill(
    string Name, int SkillId, long Total, double Share, double Dps, int Hits, double PerMinute, int Uses, double? AvgInterval,
    double? Crit, double? Back, double? Front, double? Perfect, double? Double, double? Multi, double? Parry, double Average, long Highest, bool Absorb = false,
    long Overheal = 0);

/// <summary>A line of a stacked chart: its name and the rate (per second) in each bin of the fight.</summary>
public sealed record ReportSeries(string Name, double[] Values);

/// <summary>Who or what dealt a player's damage taken, with its skills.</summary>
public sealed record ReportSource(string Name, int SourceId, long Total, double Share, IReadOnlyList<ReportSkill> Skills);

/// <summary>An effect on a player or on the boss: its share of the fight, how long, the highest level, how often it was put on (by whom), harmful or not, and the givers.</summary>
public sealed record ReportEffect(string Name, int SkillId, double UptimePercent, double Seconds, int Level, int Applications, IReadOnlyDictionary<int, int> ByCaster,
    bool Harmful, IReadOnlyList<int> Givers)
{
    /// <summary>How often <paramref name="playerId"/> put it on.</summary>
    public int By(int playerId) => ByCaster.GetValueOrDefault(playerId);
}

/// <summary>One effect a player gave: on the party (other players) or on the target, its uptime there and the damage it added (null when the meter has no value for it).</summary>
public sealed record ReportSupportItem(string Name, int SkillId, bool OnTarget, double UptimePercent, double? Damage);

/// <summary>What one player gave the others: their effects and the DPS they added to the group (rDPS lent out).</summary>
public sealed record ReportSupport(int PlayerId, double GivenDps, IReadOnlyList<ReportSupportItem> Items);

/// <summary>A death: when (seconds into the fight), and the hit that came last before it.</summary>
public sealed record ReportDeath(double At, string? KilledBy, long? LastHit);

/// <summary>Everything about one player of the report.</summary>
public sealed class ReportPlayerLine
{
    public required ReportPlayer Player { get; init; }
    public long Damage { get; init; }
    public double Dps { get; init; }
    public double Share { get; init; }
    public double? Rdps { get; set; }
    public double Given { get; set; }
    public long Healing { get; init; }

    /// <summary>The part of <see cref="Healing"/> that went past the targets' maximum hit points (see <see cref="FightReport.OverhealOf"/>).</summary>
    public long Overheal { get; init; }

    /// <summary>The part of <see cref="Healing"/> whose overheal is known (the target's hit points were read); 0 for a fight recorded before the meter read them.</summary>
    public long HealChecked { get; init; }

    public double Hps { get; init; }
    public double HealShare { get; init; }
    public long Absorbed { get; init; }
    public long Taken { get; init; }
    public double TakenDps { get; init; }
    public double TakenShare { get; init; }
    public int HitsTaken { get; init; }
    public int Dodged { get; init; }
    public int Parried { get; init; }
    public int Blocked { get; init; }
    public int Resisted { get; init; }
    public IReadOnlyList<ReportDeath> Deaths { get; init; } = Array.Empty<ReportDeath>();
    public IReadOnlyList<ReportSkill> Skills { get; init; } = Array.Empty<ReportSkill>();
    public IReadOnlyList<ReportSkill> Heals { get; init; } = Array.Empty<ReportSkill>();
    public IReadOnlyList<ReportSource> TakenBy { get; init; } = Array.Empty<ReportSource>();
    public IReadOnlyList<ReportSkill> ShieldsOn { get; init; } = Array.Empty<ReportSkill>();
    public IReadOnlyList<ReportSkill> AvoidedBy { get; init; } = Array.Empty<ReportSkill>();
    public IReadOnlyList<ReportSeries> DamageSeries { get; init; } = Array.Empty<ReportSeries>();
    public IReadOnlyList<ReportSeries> HealSeries { get; init; } = Array.Empty<ReportSeries>();
    public IReadOnlyList<ReportSeries> TakenSeries { get; init; } = Array.Empty<ReportSeries>();
    public double[] DpsLine { get; init; } = Array.Empty<double>();
    public IReadOnlyList<ReportEffect> Buffs { get; init; } = Array.Empty<ReportEffect>();
    public IReadOnlyList<ReportEffect> Passives { get; init; } = Array.Empty<ReportEffect>();
    public RotationReport? Rotation { get; init; }
    public double[] RotationDps { get; init; } = Array.Empty<double>();

    /// <summary>What each use of <see cref="Rotation"/> (same order as its <see cref="RotationReport.Uses"/>) hit: for the rotation's details on a click.</summary>
    public IReadOnlyList<UseHits> UseHits { get; init; } = Array.Empty<UseHits>();
}

/// <summary>
/// The hits one use of a skill did: its damage, the hits (ticks apart) and how many of them were critical, from behind, perfect, double, multi-strike or parried.
/// A use's hits are the player's hits of the same skill (its first four digits) from the use until the next use of it, at most <see cref="FightReport.UseWindowSeconds"/> later.
/// </summary>
public sealed record UseHits(long Damage, int Hits, int Crit, int Back, int Perfect, int Double, int Multi, int Parry, int Front = 0, long Biggest = 0);

/// <summary>
/// A fight worked out for the report window's tabs (damage, healing, damage taken, shields, defense, effects, support, rotation, timeline). Pure: no
/// window, no capture; built from a <see cref="ReportInput"/>, so a test can check it and the same numbers can go to the website.
/// </summary>
public sealed class FightReport
{
    /// <summary>The window the rolling DPS of the timeline and the peak/lowest are taken over.</summary>
    public const double RollingSeconds = 5;

    /// <summary>The stacked charts show the biggest few skills (sources) and the rest as one.</summary>
    public const int SeriesShown = 5;

    public required string Title { get; init; }
    public DateTime Start { get; init; }
    public double Seconds { get; init; }
    public bool Killed { get; init; }
    public int? BossId { get; init; }
    public int? BossNpcId { get; init; }
    public string? BossName { get; init; }
    public long Damage { get; init; }
    public double PartyDps { get; init; }
    public int Deaths { get; init; }
    public double PeakDps { get; init; }
    public double LowestDps { get; init; }
    public double AverageDps { get; init; }
    public long Taken { get; init; }
    public double TakenDps { get; init; }
    public double BinSeconds { get; init; } = 1;
    public double[] PartyLine { get; init; } = Array.Empty<double>();
    public double[] TakenLine { get; init; } = Array.Empty<double>();
    public IReadOnlyList<ReportPlayerLine> Players { get; init; } = Array.Empty<ReportPlayerLine>();
    public IReadOnlyList<ReportEffect> BossEffects { get; init; } = Array.Empty<ReportEffect>();
    public IReadOnlyList<ReportSupport> Support { get; init; } = Array.Empty<ReportSupport>();
    public IReadOnlyList<StaggerPhase> Staggers { get; init; } = Array.Empty<StaggerPhase>();

    /// <summary>Whom the boss aimed at: stretches of the fight (seconds) and the player (0 = nobody known).</summary>
    public IReadOnlyList<(double Start, double End, int Target)> BossTargets { get; init; } = Array.Empty<(double, double, int)>();

    /// <summary>The boss's skills, each at the moment it was used.</summary>
    public IReadOnlyList<(double At, string Name)> BossCasts { get; init; } = Array.Empty<(double, string)>();

    /// <summary>The boss's wind-ups (the cast bars): start, end and the skill.</summary>
    public IReadOnlyList<(double Start, double End, string Name)> CastBars { get; init; } = Array.Empty<(double, double, string)>();

    /// <summary>The skill id of each of <see cref="CastBars"/> (the same order): the website learns the boss's timers from them, in any language.</summary>
    public IReadOnlyList<int> CastBarIds { get; init; } = Array.Empty<int>();

    /// <summary>The boss's health share (0 to 1) when each of <see cref="CastBars"/> began, null where the fight can't tell: the boss timers learn its
    /// phases from it (Combat/BossPattern.cs).</summary>
    public IReadOnlyList<double?> CastBarHealth { get; init; } = Array.Empty<double?>();

    /// <summary>The players' deaths over the fight.</summary>
    public IReadOnlyList<(double At, int Player)> PartyDeaths { get; init; } = Array.Empty<(double, int)>();

    /// <summary>The boss's stagger bar over the fight and who took it down (see <see cref="StaggerCredit"/>).</summary>
    public StaggerReport Stagger { get; init; } = StaggerReport.Empty;

    /// <summary>The boss's stagger checks: each mechanic that showed its stagger bar, how it went and who took what off it (see <see cref="StaggerChecks"/>);
    /// their times from the fight's start in <see cref="StaggerCheckStart"/>.</summary>
    public IReadOnlyList<StaggerCheck> StaggerChecks { get; init; } = Array.Empty<StaggerCheck>();

    /// <summary>The fight's start, the checks' times are of (local time).</summary>
    public DateTime StaggerCheckStart { get; init; }

    /// <summary>The party's hits that count, in time order: the timeline's details of any stretch of the fight (<see cref="FightMoments.Window"/>).</summary>
    public IReadOnlyList<ReportHit> PartyHits { get; init; } = Array.Empty<ReportHit>();

    private IReadOnlyList<FightMoment>? _moments;

    /// <summary>The bursts, drops, stagger breaks and deaths of the fight, in time order (see <see cref="FightMoments"/>).</summary>
    public IReadOnlyList<FightMoment> Moments => _moments ??= FightMoments.Find(this);

    public ReportPlayerLine? PlayerOf(int id) => Players.FirstOrDefault(p => p.Player.Id == id);

    /// <summary>The effects on one player over [from, to], the helpful ones first by uptime, harmful ones after; no passives, nothing unnamed (the in-game meter's buff view).</summary>
    public static IReadOnlyList<ReportEffect> EffectsOn(IReadOnlyList<BuffEvent> buffs, int target, ISet<int> players, DateTime from, DateTime to)
    {
        double seconds = Math.Max(1, (to - from).TotalSeconds);
        return EffectSpans(buffs, new HashSet<int> { target }, from, to)
            .Where(kv => Named(kv.Key.Name) && !IsPassive(kv.Value.SkillId))
            .Select(kv => kv.Value.ToEffect(kv.Key.Name, seconds, players, onBoss: false))
            .Where(e => e.UptimePercent >= 1)
            .OrderBy(e => e.Harmful).ThenByDescending(e => e.UptimePercent).ToList();
    }

    /// <summary>The chart's bin for a fight of this length: one second up to two minutes, then wider, at most about 300 bins.</summary>
    public static double BinFor(double seconds) => seconds <= 120 ? 1 : seconds <= 600 ? 2 : Math.Ceiling(seconds / 300);

    public static FightReport Build(ReportInput input)
    {
        DateTime start = input.Start, end = input.End > input.Start ? input.End : input.Start.AddSeconds(1);
        double seconds = Math.Max(1, (end - start).TotalSeconds);
        double bin = BinFor(seconds);
        int bins = Math.Max(1, (int)Math.Ceiling(seconds / bin));
        Func<int, string?> nameOf = input.NameOf ?? (_ => null);
        var playerIds = input.Players.Select(p => p.Id).ToHashSet();
        bool InWindow(DateTime at) => at >= start && at <= end;
        bool Counts(in DamageEvent e) => !e.IsHeal && playerIds.Contains(e.SourceObjectId) && !playerIds.Contains(e.TargetObjectId)
            && (input.Targets is null || input.Targets.Contains(e.TargetObjectId));
        int BinOf(DateTime at) => Math.Clamp((int)((at - start).TotalSeconds / bin), 0, bins - 1);

        var damageBy = new Dictionary<int, List<DamageEvent>>();
        var healBy = new Dictionary<int, List<DamageEvent>>();
        var takenBy = new Dictionary<int, List<DamageEvent>>();
        var party = new double[bins];
        var takenLine = new double[bins];
        foreach (DamageEvent e in input.Events)
        {
            if (!InWindow(e.Timestamp))
            {
                continue;
            }

            if (Counts(e))
            {
                Add(damageBy, e.SourceObjectId, e);
                party[BinOf(e.Timestamp)] += e.Amount;
            }
            else if (e.IsHeal && playerIds.Contains(e.SourceObjectId))
            {
                Add(healBy, e.SourceObjectId, e);
            }
            else if (!e.IsHeal && playerIds.Contains(e.TargetObjectId) && !playerIds.Contains(e.SourceObjectId))
            {
                Add(takenBy, e.TargetObjectId, e);
                takenLine[BinOf(e.Timestamp)] += e.Amount;
            }
        }

        var shieldsOn = new Dictionary<int, List<DamageEvent>>();
        foreach (DamageEvent e in input.Shields)
        {
            if (InWindow(e.Timestamp) && playerIds.Contains(e.TargetObjectId) && e.Amount > 1)
            {
                Add(shieldsOn, e.TargetObjectId, e);
            }
        }

        var avoidsOn = input.Avoids.Where(a => InWindow(a.Timestamp) && playerIds.Contains(a.TargetObjectId)).GroupBy(a => a.TargetObjectId).ToDictionary(g => g.Key, g => g.ToList());

        long damage = damageBy.Values.Sum(l => l.Sum(e => e.Amount));
        long healing = healBy.Values.Sum(l => l.Sum(e => e.Amount));
        Func<DamageEvent, long> overOf = OverhealOf(healBy.Values.SelectMany(l => l));
        long taken = takenBy.Values.Sum(l => l.Sum(e => e.Amount));
        List<SkillUse> uses = input.Extras.Uses.Where(u => InWindow(u.At)).ToList();

        // the effects of the fight: every player's, and the boss's
        var effectTargets = new HashSet<int>(playerIds);
        if (input.BossId is int boss)
        {
            effectTargets.Add(boss);
        }

        Dictionary<(int Target, string Name), EffectAcc> effects = EffectSpans(input.Extras.Buffs, effectTargets, start, end);

        var deaths = (input.Extras.Deaths ?? Array.Empty<DeathRecord>()).Where(d => InWindow(d.At) && playerIds.Contains(d.Entity)).OrderBy(d => d.At).ToList();

        var lines = new List<ReportPlayerLine>();
        foreach (ReportPlayer p in input.Players)
        {
            List<DamageEvent> mine = damageBy.GetValueOrDefault(p.Id) ?? new List<DamageEvent>();
            List<DamageEvent> heals = healBy.GetValueOrDefault(p.Id) ?? new List<DamageEvent>();
            List<DamageEvent> hitsTaken = takenBy.GetValueOrDefault(p.Id) ?? new List<DamageEvent>();
            List<DamageEvent> shields = shieldsOn.GetValueOrDefault(p.Id) ?? new List<DamageEvent>();
            List<AvoidEvent> avoided = avoidsOn.GetValueOrDefault(p.Id) ?? new List<AvoidEvent>();
            var myUses = uses.Where(u => u.Actor == p.Id).ToList();
            long mineTotal = mine.Sum(e => e.Amount), healTotal = heals.Sum(e => e.Amount), takenTotal = hitsTaken.Sum(e => e.Amount);

            var playerDeaths = deaths.Where(d => d.Entity == p.Id).Select(d =>
            {
                DamageEvent? last = hitsTaken.Where(h => h.Timestamp <= d.At && d.At - h.Timestamp <= AionDPS.Combat.Deaths.BlowWindow).OrderBy(h => h.Timestamp).Select(h => (DamageEvent?)h).LastOrDefault();
                return new ReportDeath((d.At - start).TotalSeconds, last is { } l ? nameOf(l.SourceObjectId) ?? l.Skill : null, last?.Amount);
            }).ToList();

            // the effects on the player: helpful ones and harmful ones, the passives apart
            var onMe = effects.Where(kv => kv.Key.Target == p.Id && Named(kv.Key.Name)).Select(kv => kv.Value.ToEffect(kv.Key.Name, seconds, playerIds, onBoss: false)).ToList();

            RotationReport? rotation = myUses.Count > 0 || input.Extras.Buffs.Count > 0 ? RotationAnalysis.Build(input.Extras, p.Id, start, end, mine, nameOf) : null;
            lines.Add(new ReportPlayerLine
            {
                Player = p,
                Damage = mineTotal,
                Dps = mineTotal / seconds,
                Share = damage > 0 ? 100.0 * mineTotal / damage : 0,
                Healing = healTotal,
                Overheal = heals.Sum(overOf),
                HealChecked = heals.Where(e => e.HpBefore >= 0 && e.HpMax > 0).Sum(e => e.Amount),
                Hps = healTotal / seconds,
                HealShare = healing > 0 ? 100.0 * healTotal / healing : 0,
                Absorbed = shields.Sum(e => e.Amount),
                Taken = takenTotal,
                TakenDps = takenTotal / seconds,
                TakenShare = taken > 0 ? 100.0 * takenTotal / taken : 0,
                HitsTaken = hitsTaken.Count(h => !h.IsTick),
                Dodged = avoided.Count(a => a.Kind == AvoidKind.Dodge),
                Parried = avoided.Count(a => a.Kind == AvoidKind.Parry),
                Blocked = avoided.Count(a => a.Kind == AvoidKind.Block),
                Resisted = avoided.Count(a => a.Kind == AvoidKind.Resist),
                Deaths = playerDeaths,
                Skills = Skills(mine, mineTotal, seconds, myUses),
                Heals = Skills(heals, healTotal + shields.Where(s => s.SourceObjectId == p.Id).Sum(s => s.Amount), seconds, myUses, heals: true, overOf: overOf)
                    .Concat(Skills(shields.Where(s => s.SourceObjectId == p.Id).ToList(), healTotal + shields.Where(s => s.SourceObjectId == p.Id).Sum(s => s.Amount), seconds, myUses, heals: true)
                        .Select(s => s with { Absorb = true }))
                    .OrderByDescending(s => s.Total).ToList(),
                TakenBy = hitsTaken.GroupBy(h => h.SourceObjectId)
                    .Select(g => new ReportSource(nameOf(g.Key) ?? $"Target #{g.Key & 0xFFFF}", g.Key, g.Sum(h => h.Amount), takenTotal > 0 ? 100.0 * g.Sum(h => h.Amount) / takenTotal : 0,
                        Skills(g.ToList(), g.Sum(h => h.Amount), seconds, Array.Empty<SkillUse>())))
                    .OrderByDescending(s => s.Total).ToList(),
                ShieldsOn = Skills(shields, shields.Sum(e => e.Amount), seconds, Array.Empty<SkillUse>(), heals: true),
                AvoidedBy = avoided.GroupBy(a => a.Skill ?? "Attack").Select(g => new ReportSkill(g.Key, 0, 0, avoided.Count > 0 ? 100.0 * g.Count() / avoided.Count : 0, 0, g.Count(),
                    g.Count() * 60 / seconds, 0, null, null, null, null, null, null, null, null, 0, 0)).OrderByDescending(s => s.Hits).ToList(),
                DamageSeries = Stack(mine.GroupBy(e => e.Skill ?? "(auto attack)"), start, bin, bins),
                HealSeries = Stack(heals.Concat(shields.Where(s => s.SourceObjectId == p.Id)).GroupBy(e => e.Skill ?? "(heal)"), start, bin, bins),
                TakenSeries = Stack(hitsTaken.GroupBy(e => (nameOf(e.SourceObjectId) ?? "Target") + (e.Skill is { } sk ? " · " + sk : "")), start, bin, bins),
                DpsLine = Rolling(PerBin(mine, start, bin, bins), bin),
                Buffs = onMe.Where(e => !IsPassive(e.SkillId)).OrderByDescending(e => e.UptimePercent).ThenByDescending(e => e.Applications).ToList(),
                Passives = onMe.Where(e => IsPassive(e.SkillId)).OrderBy(e => e.Name, StringComparer.Ordinal).ToList(),
                Rotation = rotation,
                UseHits = HitsOfUses(rotation, mine, start),
                RotationDps = PerSecondRolling(mine, start, (int)Math.Ceiling(seconds)),
            });
        }

        // the party's rolling DPS: peak, lowest and average (the first seconds of the pull left out of the lowest)
        double[] partyLine = Rolling(party, bin);
        int skip = partyLine.Length > (int)(2 * RollingSeconds / bin) ? (int)(RollingSeconds / bin) : 0;
        var settled = partyLine.Skip(skip).ToList();

        // rDPS and what everyone lent out, per effect
        var byBuff = new Dictionary<(int Caster, int DisplayBase), double>();
        try
        {
            var (onPlayers, onBoss) = RaidDps.BuffsIn(input.Extras.Buffs, playerIds, input.BossId, start, end);

            // every player's direct hits, for rDPS hit by hit (milliseconds from the fight's start, as the effects' spans); damage-over-time ticks take no
            // part in the split
            List<(long, long)> HitsOf(int id) => (damageBy.GetValueOrDefault(id) ?? new List<DamageEvent>()).Where(e => !e.IsHeal && !e.IsTick && e.Amount > 0)
                .Select(e => ((long)(e.Timestamp - start).TotalMilliseconds, e.Amount)).ToList();
            var participants = lines.Select(l => new RaidParticipant(l.Player.Id, l.Dps, onPlayers.TryGetValue(l.Player.Id, out var b) ? b : new List<RaidBuff>(),
                HitsOf(l.Player.Id))).ToList();
            Dictionary<int, RaidDpsResult> results = RaidDps.Compute(participants, onBoss, seconds, byBuff);
            foreach (ReportPlayerLine line in lines)
            {
                if (results.TryGetValue(line.Player.Id, out RaidDpsResult r))
                {
                    line.Rdps = r.Rdps;
                    line.Given = r.Given;
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // no rDPS for this fight
        }

        // the support tab: every effect a player put on the others or on the boss
        var support = new List<ReportSupport>();
        foreach (ReportPlayerLine line in lines)
        {
            int giver = line.Player.Id;
            var items = new List<ReportSupportItem>();
            int others = Math.Max(1, playerIds.Count - 1);
            foreach (var group in effects.Where(kv => kv.Value.SpansByCaster.ContainsKey(giver) && Named(kv.Key.Name)).GroupBy(kv => kv.Key.Name))
            {
                var onParty = group.Where(kv => kv.Key.Target != giver && playerIds.Contains(kv.Key.Target)).ToList();
                var onTarget = group.Where(kv => input.BossId is int b && kv.Key.Target == b).ToList();
                foreach ((bool target, var list) in new[] { (false, onParty), (true, onTarget) })
                {
                    if (list.Count == 0)
                    {
                        continue;
                    }

                    int skillId = list[0].Value.SkillId;
                    // only what helps: a buff on the party, a debuff (or anything the giver put) on the target
                    if (!target && !IsBeneficial(skillId))
                    {
                        continue;
                    }

                    if (IsPassive(skillId) || skillId is >= 2_000_000 and < 2_100_000)
                    {
                        continue;
                    }

                    double uptime = target
                        ? list.Max(kv => Covered(kv.Value.SpansByCaster[giver], seconds))
                        : list.Sum(kv => Covered(kv.Value.SpansByCaster[giver], seconds)) / others;
                    if (uptime < 0.5)
                    {
                        continue;
                    }

                    int display = RaidDps.DisplayBase(skillId);
                    double? added = byBuff.TryGetValue((giver, display), out double dps) && dps > 0 ? dps * seconds : null;
                    items.Add(new ReportSupportItem(group.Key, skillId, target, Math.Min(100, uptime), added));
                }
            }

            if (items.Count > 0)
            {
                support.Add(new ReportSupport(giver, line.Given, items.OrderByDescending(i => i.Damage ?? -1).ThenByDescending(i => i.UptimePercent).ToList()));
            }
        }

        // the boss: its effects, stagger, casts, wind-ups and whom it aimed at
        IReadOnlyList<ReportEffect> bossEffects = Array.Empty<ReportEffect>();
        IReadOnlyList<StaggerPhase> staggers = Array.Empty<StaggerPhase>();
        StaggerReport stagger = StaggerReport.Empty;
        IReadOnlyList<StaggerCheck> staggerChecks = Array.Empty<StaggerCheck>();
        var bossCasts = new List<(double, string)>();
        var castBars = new List<(double, double, string)>();
        var castBarIds = new List<int>();
        var castBarHealth = new List<double?>();
        var bossTargets = new List<(double, double, int)>();
        if (input.BossId is int bossId)
        {
            bossEffects = effects.Where(kv => kv.Key.Target == bossId && Named(kv.Key.Name)).Select(kv => kv.Value.ToEffect(kv.Key.Name, seconds, playerIds, onBoss: true))
                .Where(e => !IsPassive(e.SkillId)).OrderByDescending(e => e.UptimePercent).ToList();
            // the boss's own stagger (the extras have every monster's)
            var bossBar = input.Extras.Staggers.Where(st => st.Entity == bossId && InWindow(st.At)).ToList();
            staggers = RotationAnalysis.Build(input.Extras with { Staggers = bossBar }, bossId, start, end, Array.Empty<DamageEvent>(), nameOf).Staggers;
            var bossHits = input.Events.Where(e => InWindow(e.Timestamp) && e.TargetObjectId == bossId && Counts(e)).ToList();
            stagger = StaggerCredit.Build(bossBar, bossHits, start);
            staggerChecks = Combat.StaggerChecks.Find(bossId, bossBar, input.Extras.Windups ?? Array.Empty<WindupRecord>(), bossHits, end.AddSeconds(30));
            foreach (SkillUse u in uses.Where(u => u.Actor == bossId))
            {
                bossCasts.Add(((u.At - start).TotalSeconds, Aion2SkillNames.NameOf(u.SkillId)));
            }

            Func<DateTime, double?> healthOf = PatternFights.HealthOf(input, bossId);
            foreach (WindupRecord w in (input.Extras.Windups ?? Array.Empty<WindupRecord>()).Where(w => w.Actor == bossId && w.DurationMs > 0 && InWindow(w.At)))
            {
                double s = (w.At - start).TotalSeconds;
                castBars.Add((s, Math.Min(seconds, s + w.DurationMs / 1000.0), Aion2SkillNames.NameOf(w.SkillId)));
                castBarIds.Add(w.SkillId);
                castBarHealth.Add(healthOf(w.At));
            }

            var changes = (input.Extras.Targets ?? Array.Empty<TargetChange>()).Where(t => t.Actor == bossId && t.At <= end).OrderBy(t => t.At).ToList();
            int current = 0;
            double since = 0;
            foreach (TargetChange t in changes)
            {
                double at = Math.Max(0, (t.At - start).TotalSeconds);
                if (at > since && current != 0)
                {
                    bossTargets.Add((since, at, current));
                }

                current = playerIds.Contains(t.Target) ? t.Target : 0;
                since = at;
            }

            if (current != 0 && since < seconds)
            {
                bossTargets.Add((since, seconds, current));
            }
        }

        return new FightReport
        {
            Title = input.Title,
            Start = start,
            Seconds = seconds,
            Killed = input.Killed,
            BossId = input.BossId,
            BossNpcId = input.BossNpcId,
            BossName = input.BossId is int named ? nameOf(named) ?? input.Title : null,
            Damage = damage,
            PartyDps = damage / seconds,
            Deaths = deaths.Count,
            PeakDps = settled.Count > 0 ? settled.Max() : 0,
            LowestDps = settled.Count > 0 ? settled.Min() : 0,
            AverageDps = settled.Count > 0 ? settled.Average() : 0,
            Taken = taken,
            TakenDps = taken / seconds,
            BinSeconds = bin,
            PartyLine = partyLine,
            TakenLine = Rolling(takenLine, bin),
            Players = lines.OrderByDescending(l => l.Damage).ThenByDescending(l => l.Healing).ToList(),
            BossEffects = bossEffects,
            Support = support.OrderByDescending(s => s.GivenDps).ToList(),
            Staggers = staggers,
            BossTargets = bossTargets,
            BossCasts = bossCasts,
            CastBars = castBars,
            CastBarIds = castBarIds,
            CastBarHealth = castBarHealth,
            PartyDeaths = deaths.Select(d => ((d.At - start).TotalSeconds, d.Entity)).ToList(),
            Stagger = stagger,
            StaggerChecks = staggerChecks,
            StaggerCheckStart = start,
            PartyHits = damageBy.Values.SelectMany(l => l)
                .Select(e => new ReportHit((e.Timestamp - start).TotalSeconds, e.SourceObjectId, e.SkillId, e.SkillId != 0 ? Aion2SkillNames.NameOf(e.SkillId) : e.Skill ?? "Attack", e.Amount))
                .OrderBy(h => h.At).ToList(),
        };
    }

    /// <summary>An effect the game has no name for is shown nowhere (its number says nothing to a player).</summary>
    // a name, and none of what the game puts on everybody ("Test_PLATE Equippable", the Abyss's zone states, an artifact's faction bonus: see Aion2Artwork)
    private static bool Named(string name) => name.Any(char.IsLetter) && !Aion2.Protocol.Aion2Artwork.IsWorldEffectName(name);

    private static void Add(Dictionary<int, List<DamageEvent>> map, int key, DamageEvent e)
    {
        if (!map.TryGetValue(key, out List<DamageEvent>? list))
        {
            map[key] = list = new List<DamageEvent>();
        }

        list.Add(e);
    }

    /// <summary>A table of skills: totals, rates, hits and how they landed, with the casts (uses) and the time between them where the capture saw them.</summary>
    /// <summary>
    /// How much of each heal went past its target's maximum hit points: the amount less what the target was missing when it landed. The maximum is the highest
    /// the meter knew for the target over these heals (the server's own maximum, else the highest reading), the hit points before the heal the last reading
    /// plus the heals since (see DamageEvent.HpBefore). A heal without them (an older fight, a target never read) counts as no overheal.
    /// </summary>
    public static Func<DamageEvent, long> OverhealOf(IEnumerable<DamageEvent> heals)
    {
        var max = new Dictionary<int, long>();
        foreach (DamageEvent e in heals)
        {
            if (e.IsHeal && e.HpMax > 0 && e.HpMax > max.GetValueOrDefault(e.TargetObjectId))
            {
                max[e.TargetObjectId] = e.HpMax;
            }
        }

        return e => e.IsHeal && e.HpBefore >= 0 && max.TryGetValue(e.TargetObjectId, out long m) ? e.Amount - Math.Clamp(m - e.HpBefore, 0, e.Amount) : 0;
    }

    internal static List<ReportSkill> Skills(IReadOnlyList<DamageEvent> events, long total, double seconds, IReadOnlyList<SkillUse> uses, bool heals = false,
        Func<DamageEvent, long>? overOf = null)
    {
        Dictionary<string, long>? over = overOf is null ? null
            : events.Where(e => e.IsHeal).GroupBy(e => e.Skill ?? "(auto attack)").ToDictionary(g => g.Key, g => g.Sum(overOf));
        var usesByBase = uses.GroupBy(u => Base(u.SkillId)).ToDictionary(g => g.Key, g => g.Select(u => u.At).OrderBy(a => a).ToList());
        var usesByName = uses.GroupBy(u => Aion2SkillNames.NameOf(u.SkillId)).ToDictionary(g => g.Key, g => g.Select(u => u.At).OrderBy(a => a).ToList());
        var list = new List<ReportSkill>();
        foreach (SkillUsage s in SkillBreakdown.For(events, heals))
        {
            List<DateTime>? casts = s.SkillId != 0 && usesByBase.TryGetValue(Base(s.SkillId), out var byId) ? byId : usesByName.GetValueOrDefault(s.Skill);
            double? interval = casts is { Count: > 1 } ? (casts[^1] - casts[0]).TotalSeconds / (casts.Count - 1) : null;
            HitKindCounts? k = s.Kinds is { Known: > 0 } known ? known : null;
            list.Add(new ReportSkill(s.Skill, s.SkillId, s.Total, total > 0 ? 100.0 * s.Total / total : 0, s.Total / seconds, s.Hits, s.Hits * 60 / seconds,
                casts?.Count ?? 0, interval, s.Hits > 0 && !heals || s.CritHits > 0 ? 100.0 * s.CritHits / Math.Max(1, s.Hits) : null,
                k?.Rate(k.Back), k?.Rate(k.Front), k?.Rate(k.Perfect), k?.Rate(k.Double), k?.Rate(k.Multi), k?.Rate(k.Parry),
                s.Hits > 0 ? (double)s.Total / s.Hits : 0, s.Max, Overheal: over?.GetValueOrDefault(s.Skill) ?? 0));
        }

        return list;
    }

    private static int Base(int skillId) => skillId < 10000 ? skillId : skillId / 10000 * 10000;

    /// <summary>The biggest few groups as chart lines (per second in each bin, smoothed a little), the rest as one more.</summary>
    internal static List<ReportSeries> Stack(IEnumerable<IGrouping<string, DamageEvent>> groups, DateTime start, double bin, int bins)
    {
        var ordered = groups.Select(g => (g.Key, Events: g.ToList(), Total: g.Sum(e => e.Amount))).Where(g => g.Total > 0).OrderByDescending(g => g.Total).ToList();
        var series = new List<ReportSeries>();
        foreach (var g in ordered.Take(SeriesShown))
        {
            series.Add(new ReportSeries(g.Key, Smooth(PerBin(g.Events, start, bin, bins), bin)));
        }

        if (ordered.Count > SeriesShown)
        {
            series.Add(new ReportSeries("Other", Smooth(PerBin(ordered.Skip(SeriesShown).SelectMany(g => g.Events).ToList(), start, bin, bins), bin)));
        }

        return series;
    }

    /// <summary>The amount in each bin.</summary>
    /// <summary>A use's hits end with the next use of the same skill, or this long after it.</summary>
    public const double UseWindowSeconds = 8;

    /// <summary>Every rotation use's hits (see <see cref="UseHits"/>), in the order of the uses.</summary>
    internal static IReadOnlyList<UseHits> HitsOfUses(RotationReport? rotation, IReadOnlyList<DamageEvent> mine, DateTime start)
    {
        if (rotation is null || rotation.Uses.Count == 0)
        {
            return Array.Empty<UseHits>();
        }

        static int Family(int skillId) => skillId / 10000;
        var hits = mine.Where(e => !e.IsHeal && e.SkillId > 0).GroupBy(e => Family(e.SkillId))
            .ToDictionary(g => g.Key, g => g.Select(e => ((e.Timestamp - start).TotalSeconds, e)).OrderBy(x => x.Item1).ToList());
        var result = new List<UseHits>(rotation.Uses.Count);
        for (int i = 0; i < rotation.Uses.Count; i++)
        {
            RotationUse u = rotation.Uses[i];
            int family = Family(u.SkillId);
            double until = u.At + UseWindowSeconds;
            for (int j = i + 1; j < rotation.Uses.Count; j++)
            {
                if (Family(rotation.Uses[j].SkillId) == family)
                {
                    until = Math.Min(until, rotation.Uses[j].At);
                    break;
                }
            }

            long damage = 0;
            int count = 0, crit = 0, back = 0, perfect = 0, dbl = 0, multi = 0, parry = 0, front = 0;
            long biggest = 0;
            if (u.SkillId > 0 && hits.TryGetValue(family, out var list))
            {
                foreach ((double at, DamageEvent e) in list)
                {
                    if (at < u.At - 0.05 || at >= until)
                    {
                        continue;
                    }

                    damage += e.Amount;
                    biggest = Math.Max(biggest, e.Amount);
                    if (e.IsTick)
                    {
                        continue;
                    }

                    count++;
                    HitKinds k = e.Kinds ?? HitKinds.None;
                    if (e.IsCritical) crit++;
                    if (k.HasFlag(HitKinds.Back)) back++;
                    if (k.HasFlag(HitKinds.Perfect)) perfect++;
                    if (k.HasFlag(HitKinds.Double)) dbl++;
                    if (e.HitCount > 1) multi++;
                    if (k.HasFlag(HitKinds.Parry)) parry++;
                    if (k.HasFlag(HitKinds.Front)) front++;
                }
            }

            result.Add(new UseHits(damage, count, crit, back, perfect, dbl, multi, parry, front, biggest));
        }

        return result;
    }

    internal static double[] PerBin(IReadOnlyList<DamageEvent> events, DateTime start, double bin, int bins)
    {
        var values = new double[bins];
        foreach (DamageEvent e in events)
        {
            values[Math.Clamp((int)((e.Timestamp - start).TotalSeconds / bin), 0, bins - 1)] += e.Amount;
        }

        return values;
    }

    /// <summary>Per second over the last <see cref="RollingSeconds"/> (or one bin, when the bins are wider): the DPS line of the timeline.</summary>
    internal static double[] Rolling(double[] perBin, double bin)
    {
        int width = Math.Max(1, (int)Math.Round(RollingSeconds / bin));
        var result = new double[perBin.Length];
        double sum = 0;
        for (int i = 0; i < perBin.Length; i++)
        {
            sum += perBin[i];
            if (i >= width)
            {
                sum -= perBin[i - width];
            }

            result[i] = sum / (Math.Min(i + 1, width) * bin);
        }

        return result;
    }

    /// <summary>Per second, averaged over three bins: a stacked chart that does not flicker.</summary>
    private static double[] Smooth(double[] perBin, double bin)
    {
        var result = new double[perBin.Length];
        for (int i = 0; i < perBin.Length; i++)
        {
            double sum = 0;
            int n = 0;
            for (int j = Math.Max(0, i - 1); j <= Math.Min(perBin.Length - 1, i + 1); j++)
            {
                sum += perBin[j];
                n++;
            }

            result[i] = sum / n / bin;
        }

        return result;
    }

    /// <summary>The rotation view's DPS behind the icons: per second, over the last five.</summary>
    private static double[] PerSecondRolling(IReadOnlyList<DamageEvent> events, DateTime start, int seconds) =>
        Rolling(PerBin(events, start, 1, Math.Max(1, seconds)), 1);

    /// <summary>The share of the fight (percent) the spans cover.</summary>
    private static double Covered(List<(double Start, double End)> spans, double seconds) => 100.0 * Merge(spans).Sum(s => s.End - s.Start) / seconds;

    internal static List<(double Start, double End)> Merge(IEnumerable<(double Start, double End)> spans)
    {
        var merged = new List<(double Start, double End)>();
        foreach (var (s, e) in spans.OrderBy(x => x.Start))
        {
            if (merged.Count > 0 && s <= merged[^1].End)
            {
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, e));
            }
            else
            {
                merged.Add((s, e));
            }
        }

        return merged;
    }

    // A buff frame's id is the skill's id with a level digit (181 900 010 for Undefeated Mantra): the game knows it by the variant (18 190 001).
    private static bool IsBeneficial(int id) => Aion2Artwork.IsBeneficialEffect(id) || (id >= 100_000_000 && Aion2Artwork.IsBeneficialEffect(id / 10));

    private static bool IsDebuff(int id) => Aion2Artwork.IsDebuffEffect(id) || (id >= 100_000_000 && !Aion2Artwork.IsBeneficialEffect(id) && Aion2Artwork.IsDebuffEffect(id / 10));

    private static bool IsPassive(int id) => Aion2Artwork.IsPassiveEffect(id) || (id >= 100_000_000 && !Aion2Artwork.IsBeneficialEffect(id) && !Aion2Artwork.IsDebuffEffect(id) && Aion2Artwork.IsPassiveEffect(id / 10));

    /// <summary>An effect's name: the skill table's, else the name of the class skill it comes from (a buff's id is the skill's id with level digits).</summary>
    public static string EffectName(int skillId)
    {
        string name = Aion2SkillNames.NameOf(skillId);
        if (name.Length > 0 && name.All(char.IsDigit) && skillId is >= 110_000_000 and <= 199_999_999)
        {
            string family = Aion2SkillNames.NameOf(RaidDps.DisplayBase(skillId));
            if (!family.All(char.IsDigit))
            {
                return family;
            }
        }

        return name;
    }

    /// <summary>One effect on one target, while it is worked out.</summary>
    internal sealed class EffectAcc
    {
        public int SkillId;
        public int Level;
        public int Applications;
        public readonly Dictionary<int, int> ByCaster = new();
        public readonly Dictionary<int, List<(double Start, double End)>> SpansByCaster = new();

        public ReportEffect ToEffect(string name, double seconds, ISet<int> players, bool onBoss)
        {
            var all = Merge(SpansByCaster.Values.SelectMany(s => s));
            double covered = all.Sum(s => s.End - s.Start);
            bool fromPlayers = SpansByCaster.Keys.Any(players.Contains);
            bool harmful = IsDebuff(SkillId) || (onBoss ? fromPlayers && !IsBeneficial(SkillId) : !fromPlayers && !IsBeneficial(SkillId));
            return new ReportEffect(name, SkillId, Math.Min(100, 100.0 * covered / seconds), covered, Level, Applications, ByCaster,
                harmful, SpansByCaster.Keys.Where(players.Contains).ToList());
        }
    }

    /// <summary>
    /// The spans of every effect on the targets during [from, to] (seconds into the fight), per effect name and caster: an effect is on from its "on" until its
    /// "end", or until its duration runs out when no end comes; a renewal stretches it. Effects put on before the window and still running count from its start.
    /// </summary>
    internal static Dictionary<(int Target, string Name), EffectAcc> EffectSpans(IReadOnlyList<BuffEvent> events, ISet<int> targets, DateTime from, DateTime to)
    {
        double windowSeconds = (to - from).TotalSeconds;
        var result = new Dictionary<(int, string), EffectAcc>();
        var open = new Dictionary<(int Target, int Instance), (DateTime Start, int SkillId, int Caster, int Level, DateTime? Expect)>();

        EffectAcc AccFor(int target, int skillId)
        {
            string name = EffectName(skillId);
            if (!result.TryGetValue((target, name), out EffectAcc? acc))
            {
                result[(target, name)] = acc = new EffectAcc { SkillId = skillId };
            }

            return acc;
        }

        void Close((int Target, int Instance) key, DateTime at)
        {
            if (!open.Remove(key, out var o))
            {
                return;
            }

            double s = Math.Max(0, (o.Start - from).TotalSeconds), e = Math.Min(windowSeconds, (at - from).TotalSeconds);
            EffectAcc acc = AccFor(key.Target, o.SkillId);
            acc.Level = Math.Max(acc.Level, o.Level);
            if (e > s)
            {
                if (!acc.SpansByCaster.TryGetValue(o.Caster, out var spans))
                {
                    acc.SpansByCaster[o.Caster] = spans = new List<(double, double)>();
                }

                spans.Add((s, e));
            }
        }

        foreach (BuffEvent ev in events.Where(b => targets.Contains(b.Target) && b.At <= to).OrderBy(b => b.At))
        {
            foreach (var key in open.Where(kv => kv.Value.Expect is DateTime expect && expect < ev.At).Select(kv => kv.Key).ToList())
            {
                Close(key, open[key].Expect!.Value);
            }

            var instance = (ev.Target, ev.Instance);
            DateTime? expectAt = ev.DurationMs > 0 ? ev.At.AddMilliseconds(ev.DurationMs) : null;
            switch (ev.Kind)
            {
                case BuffKind.On:
                case BuffKind.Renewed:
                    if (ev.Kind == BuffKind.Renewed && open.TryGetValue(instance, out var current))
                    {
                        open[instance] = current with { Expect = expectAt, Level = Math.Max(current.Level, ev.Level) };
                    }
                    else
                    {
                        Close(instance, ev.At);
                        open[instance] = (ev.At, ev.SkillId, ev.Caster, ev.Level, expectAt);
                    }

                    if (ev.Kind == BuffKind.On && ev.At >= from)
                    {
                        EffectAcc acc = AccFor(ev.Target, ev.SkillId);
                        acc.Applications++;
                        acc.ByCaster[ev.Caster] = acc.ByCaster.GetValueOrDefault(ev.Caster) + 1;
                    }

                    break;
                case BuffKind.End:
                    Close(instance, ev.At);
                    break;
            }
        }

        foreach (var key in open.Keys.ToList())
        {
            Close(key, open[key].Expect is DateTime expect && expect < to ? expect : to);
        }

        // effects that never were on inside the window (ended before it) are no part of the fight
        foreach (var key in result.Where(kv => kv.Value.SpansByCaster.Count == 0 && kv.Value.Applications == 0).Select(kv => kv.Key).ToList())
        {
            result.Remove(key);
        }

        return result;
    }
}
