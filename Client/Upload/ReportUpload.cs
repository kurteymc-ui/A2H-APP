using AionDPS.Combat;

namespace AionDPS.Upload;

// The fight report as it goes to the website with a boss fight (atreia.app/meter/logs/...), so the page shows the same tabs as the client's
// report window. Compact: times in tenths of a second, rates in tenths of a percent, players by their place in Players, the charts cut to at most
// MaxPoints values; shares, DPS and averages the page works out from the totals and the fight's length.

public sealed record ReportSkillUpload(string N, int Id, long T, int H, int U, double? Iv, int? C, int? B, int? F, int? P, int? D, int? M, int? Pa, long Mx, bool? Ab = null, long? O = null);

public sealed record ReportSourceUpload(string N, long T, IReadOnlyList<ReportSkillUpload> S);

public sealed record ReportSeriesUpload(string N, IReadOnlyList<long> V);

/// <summary>An effect: uptime in tenths of a percent, highest level, applications, who put it on how often (pairs of player index and count), harmful, givers.</summary>
public sealed record ReportEffectUpload(string N, int Id, int Up, int Lv, int Ap, IReadOnlyList<int> By, bool Bad, IReadOnlyList<int> From);

public sealed record ReportSupportItemUpload(string N, int Id, bool T, int Up, long? Dmg);

public sealed record ReportSupportUpload(int P, double Given, IReadOnlyList<ReportSupportItemUpload> Items);

/// <summary>One of the boss's wind-ups: start and end (tenths of a second), the skill's name and id, the boss's health then (thousandths; null: not known).</summary>
public sealed record ReportCastUpload(int S, int E, string N, int? I = null, int? H = null);

public sealed record ReportDeathUpload(int At, string? By, long? Hit);

public sealed record ReportRotationUpload(string N, int Id, int U, double Pm, double? Gap, int First, double Share);

public sealed record ReportPlayerUpload(
    string Name, string Cls, int? Cp, bool Self, long Dmg, double? Rdps, double Given, long Heal, long Absorbed, long Taken, int Hits,
    int Dodged, int Parried, int Blocked, int Resisted,
    IReadOnlyList<ReportSkillUpload> Skills, IReadOnlyList<ReportSkillUpload> Heals, IReadOnlyList<ReportSourceUpload> TakenBy,
    IReadOnlyList<ReportSkillUpload> Shields, IReadOnlyList<ReportSkillUpload> Avoided,
    IReadOnlyList<ReportSeriesUpload> DmgSeries, IReadOnlyList<ReportSeriesUpload> HealSeries, IReadOnlyList<ReportSeriesUpload> TakenSeries, IReadOnlyList<long> Line,
    IReadOnlyList<ReportEffectUpload> Buffs, IReadOnlyList<string> Passives, IReadOnlyList<ReportDeathUpload> Deaths,
    IReadOnlyList<ReportRotationUpload> Rotation, IReadOnlyList<int> Casts, IReadOnlyList<long>? UseHits = null, IReadOnlyList<ReportSpanUpload>? BuffSpans = null,
    long? Over = null, long? HealChecked = null);

/// <summary>When an effect was on the player: its spans as pairs of tenths of a second (start, end), for the rotation's details on the website.</summary>
public sealed record ReportSpanUpload(string N, int Id, IReadOnlyList<int> Sp);

public sealed record ReportUpload(
    int V, double Seconds, bool Killed, long Damage, int Deaths, double PeakDps, double LowestDps, double AverageDps, long Taken,
    IReadOnlyList<long> Party, IReadOnlyList<long> TakenLine, IReadOnlyList<ReportPlayerUpload> Players, IReadOnlyList<ReportEffectUpload> BossEffects,
    IReadOnlyList<ReportSupportUpload> Support, IReadOnlyList<int> Staggers, IReadOnlyList<int> BossTargets, IReadOnlyList<int> BossCasts,
    IReadOnlyList<ReportCastUpload> CastBars, IReadOnlyList<int> PartyDeaths)
{
    /// <summary>The most values a chart line carries.</summary>
    public const int MaxPoints = 120;

    private const int MaxSkills = 40, MaxEffects = 60, MaxSources = 8, MaxCasts = 800;

    public static ReportUpload From(FightReport r)
    {
        var index = new Dictionary<int, int>();
        for (int i = 0; i < r.Players.Count; i++)
        {
            index[r.Players[i].Player.Id] = i;
        }

        int Of(int id) => index.TryGetValue(id, out int i) ? i : -1;
        static int Tenths(double seconds) => (int)Math.Clamp(Math.Round(seconds * 10), 0, int.MaxValue);
        static int? Rate(double? percent) => percent is double p ? (int)Math.Round(Math.Clamp(p, 0, 100) * 10) : null;

        ReportSkillUpload Skill(ReportSkill s) => new(Cut(s.Name, 60), s.SkillId, s.Total, s.Hits, s.Uses, s.AvgInterval is double g ? Math.Round(g, 1) : null,
            Rate(s.Crit), Rate(s.Back), Rate(s.Front), Rate(s.Perfect), Rate(s.Double), Rate(s.Multi), Rate(s.Parry), s.Highest, s.Absorb ? true : null, s.Overheal > 0 ? s.Overheal : null);

        List<ReportSkillUpload> Skills(IEnumerable<ReportSkill> list) => list.Take(MaxSkills).Select(Skill).ToList();

        List<ReportSeriesUpload> Series(IEnumerable<ReportSeries> list) => list.Select(s => new ReportSeriesUpload(Cut(s.Name, 60), Down(s.Values))).ToList();

        ReportEffectUpload Effect(ReportEffect e) => new(Cut(e.Name, 60), e.SkillId, (int)Math.Round(e.UptimePercent * 10), e.Level, e.Applications,
            e.ByCaster.Where(kv => Of(kv.Key) >= 0).SelectMany(kv => new[] { Of(kv.Key), kv.Value }).ToList(), e.Harmful, e.Givers.Select(Of).Where(i => i >= 0).ToList());

        var players = r.Players.Select(p => new ReportPlayerUpload(
            p.Player.Name, p.Player.ClassName, p.Player.CombatPower, p.Player.IsSelf, p.Damage, p.Rdps is double rd ? Math.Round(rd) : null, Math.Round(p.Given),
            p.Healing, p.Absorbed, p.Taken, p.HitsTaken, p.Dodged, p.Parried, p.Blocked, p.Resisted,
            Skills(p.Skills), Skills(p.Heals), p.TakenBy.Take(MaxSources).Select(s => new ReportSourceUpload(Cut(s.Name, 60), s.Total, Skills(s.Skills.Take(15)))).ToList(),
            Skills(p.ShieldsOn), Skills(p.AvoidedBy),
            Series(p.DamageSeries), Series(p.HealSeries), Series(p.TakenSeries), Down(p.DpsLine),
            p.Buffs.Take(MaxEffects).Select(Effect).ToList(), p.Passives.Take(MaxEffects).Select(e => Cut(e.Name, 60)).ToList(),
            p.Deaths.Select(d => new ReportDeathUpload(Tenths(d.At), d.KilledBy is { } by ? Cut(by, 60) : null, d.LastHit)).ToList(),
            (p.Rotation?.Skills ?? Array.Empty<RotationSkill>()).Take(MaxSkills).Select(s => new ReportRotationUpload(Cut(s.Name, 60), s.SkillId, s.Uses,
                Math.Round(s.PerMinute, 1), s.AvgGapSeconds > 0 ? Math.Round(s.AvgGapSeconds, 1) : null, Tenths(s.FirstAt), Math.Round(s.SharePercent, 1))).ToList(),
            RotationCasts(p.Rotation), RotationHits(p.Rotation, p.UseHits), BuffSpans(p.Rotation),
            p.HealChecked > 0 ? p.Overheal : null, p.HealChecked > 0 ? p.HealChecked : null)).ToList();

        return new ReportUpload(1, Math.Round(r.Seconds, 1), r.Killed, r.Damage, r.Deaths, Math.Round(r.PeakDps), Math.Round(r.LowestDps), Math.Round(r.AverageDps), r.Taken,
            Down(r.PartyLine), Down(r.TakenLine), players, r.BossEffects.Take(MaxEffects).Select(Effect).ToList(),
            r.Support.Where(s => Of(s.PlayerId) >= 0).Select(s => new ReportSupportUpload(Of(s.PlayerId), Math.Round(s.GivenDps),
                s.Items.Take(MaxEffects).Select(i => new ReportSupportItemUpload(Cut(i.Name, 60), i.SkillId, i.OnTarget, (int)Math.Round(i.UptimePercent * 10),
                    i.Damage is double d ? (long)Math.Round(d) : null)).ToList())).ToList(),
            r.Staggers.SelectMany(s => new[] { Tenths(s.Start), Tenths(s.End) }).ToList(),
            r.BossTargets.Where(t => Of(t.Target) >= 0).SelectMany(t => new[] { Tenths(t.Start), Tenths(t.End), Of(t.Target) }).ToList(),
            r.BossCasts.Select(c => Tenths(c.At)).Take(MaxCasts).ToList(),
            r.CastBars.Take(MaxCasts).Select((c, k) => new ReportCastUpload(Tenths(c.Start), Tenths(c.End), Cut(c.Name, 60), k < r.CastBarIds.Count && r.CastBarIds[k] > 0 ? r.CastBarIds[k] : null,
                k < r.CastBarHealth.Count && r.CastBarHealth[k] is double h ? (int)Math.Round(Math.Clamp(h, 0, 1) * 1000) : null)).ToList(),
            r.PartyDeaths.Where(d => Of(d.Player) >= 0).SelectMany(d => new[] { Tenths(d.At), Of(d.Player) }).ToList());
    }

    /// <summary>The rotation's casts as pairs: the moment (tenths of a second) and the skill's place in the rotation table.</summary>
    private static List<int> RotationCasts(RotationReport? rotation)
    {
        if (rotation is null)
        {
            return new List<int>();
        }

        var place = rotation.Skills.Take(MaxSkills).Select((s, i) => (s.Name, i)).GroupBy(x => x.Name).ToDictionary(g => g.Key, g => g.First().i);
        return rotation.Uses.Where(u => place.ContainsKey(u.Name)).Take(MaxCasts)
            .SelectMany(u => new[] { (int)Math.Round(u.At * 10), place[u.Name] }).ToList();
    }

    /// <summary>What each of <see cref="RotationCasts"/>'s casts hit (same casts, same order), nine numbers a cast: damage, hits, critical, back, perfect,
    /// double, multi-strike, parried, front (see <see cref="UseHits"/>; 1.19.19 and 1.19.20 sent the first eight, the website tells them apart by the count).</summary>
    private static List<long> RotationHits(RotationReport? rotation, IReadOnlyList<UseHits> hits)
    {
        if (rotation is null || hits.Count != rotation.Uses.Count)
        {
            return new List<long>();
        }

        var place = rotation.Skills.Take(MaxSkills).Select(s => s.Name).ToHashSet();
        return rotation.Uses.Select((u, i) => (u, h: hits[i])).Where(x => place.Contains(x.u.Name)).Take(MaxCasts)
            .SelectMany(x => new long[] { x.h.Damage, x.h.Hits, x.h.Crit, x.h.Back, x.h.Perfect, x.h.Double, x.h.Multi, x.h.Parry, x.h.Front }).ToList();
    }

    /// <summary>The player's effects with when they were on (the rotation's buff rows that know their spans), at most 200 spans each.</summary>
    private static List<ReportSpanUpload> BuffSpans(RotationReport? rotation) =>
        (rotation?.Buffs ?? Array.Empty<BuffRow>()).Where(b => b.Spans is { Count: > 0 }).Take(MaxEffects)
            .Select(b => new ReportSpanUpload(Cut(b.Name, 60), b.SkillId, b.Spans!.Take(200).SelectMany(sp => new[] { (int)Math.Round(sp.Start * 10), (int)Math.Round(sp.End * 10) }).ToList()))
            .ToList();

    /// <summary>A chart line with at most <see cref="MaxPoints"/> values: neighbouring values averaged, rounded.</summary>
    internal static List<long> Down(double[] values)
    {
        if (values.Length <= MaxPoints)
        {
            return values.Select(v => (long)Math.Round(v)).ToList();
        }

        var result = new List<long>(MaxPoints);
        double step = (double)values.Length / MaxPoints;
        for (int i = 0; i < MaxPoints; i++)
        {
            int from = (int)(i * step), to = Math.Max(from + 1, (int)((i + 1) * step));
            double sum = 0;
            for (int j = from; j < to && j < values.Length; j++)
            {
                sum += values[j];
            }

            result.Add((long)Math.Round(sum / (to - from)));
        }

        return result;
    }

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..max];
}
