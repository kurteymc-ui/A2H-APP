using System.IO;
namespace AionDPS.Combat;

/// <summary>The fight report on a made-up fight (Ui.FightReportSample): totals add up, the shares make 100 %, a buff that was on before the pull
/// counts from its start, shields, avoided attacks, the death, whom the boss aimed at and what the Chanter lent out are where they belong.</summary>
public static class SelfCheckFightReport
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Fight report:");
        ReportInput input = Ui.FightReportSample.Input();
        FightReport r = FightReport.Build(input);
        long onBoss = input.Events.Where(e => !e.IsHeal && e.TargetObjectId == Ui.FightReportSample.Boss && e.SourceObjectId is >= 1 and <= 5).Sum(e => e.Amount);
        bool totals = r.Damage == onBoss && r.Players.Sum(p => p.Damage) == onBoss && Math.Abs(r.Players.Sum(p => p.Share) - 100) < 0.01
            && Math.Abs(r.PartyDps - onBoss / 55.0) < 0.5 && r.Players.All(p => p.Skills.Sum(s => s.Total) == p.Damage)
            && r.PeakDps >= r.AverageDps && r.AverageDps >= r.LowestDps && r.Killed && Math.Abs(r.Seconds - 55) < 0.001;
        Console.WriteLine($"  -> damage, DPS, shares and the skills add up; peak >= average >= lowest: {totals}");

        ReportPlayerLine you = r.PlayerOf(3)!;
        ReportEffect? mantra = you.Buffs.FirstOrDefault(b => b.Name == "Undefeated Mantra");
        bool effects = mantra is { UptimePercent: > 99.9, Applications: 0 } && mantra.Givers.Contains(2)
            && r.BossEffects.Any(b => b.Name == "Earth's Promise" && b.Harmful && b.By(2) == 1 && Math.Abs(b.UptimePercent - 100.0 * 25 / 55) < 0.1);
        Console.WriteLine($"  -> a buff put on before the pull is on from the start; a debuff on the boss counts its giver and its 25 s: {effects}");

        bool defense = you.Absorbed == 3110 && you.Blocked == 1 && you.Parried == 1 && you.Dodged == 1 && you.ShieldsOn.Count == 1
            && r.PlayerOf(5)!.Deaths.Count == 1 && r.Deaths == 1 && r.PartyDeaths.Count == 1 && you.TakenBy.Count == 1 && you.Taken > 0;
        Console.WriteLine($"  -> shields, avoided attacks, damage taken by source and the death are on the right players: {defense}");

        bool boss = r.BossTargets.Count == 3 && r.BossTargets[0].Start == 0 && r.BossTargets[0].End == 24 && r.BossTargets[0].Target == 3 && r.BossTargets[1].Target == 1 && r.CastBars.Count == 5 && r.CastBarIds.Count == 5 && r.CastBarIds.All(i => i > 0)
            && Math.Abs(r.CastBars[3].End - 41) < 0.01 && r.BossCasts.Count == 5;
        Console.WriteLine($"  -> whom the boss aimed at, its casts and cast bars: {boss}");

        ReportSupport? chanter = r.Support.FirstOrDefault(s => s.PlayerId == 2);
        bool support = chanter is { GivenDps: > 0 } && chanter.Items.Any(i => i.Name == "Undefeated Mantra" && i.Damage > 0 && i.UptimePercent > 99) && r.PlayerOf(1)!.Rdps is double rd && rd < r.PlayerOf(1)!.Dps;
        Console.WriteLine($"  -> the Chanter's mantra is worth DPS to the others, a buffed player's rDPS is below their DPS: {support}");

        bool series = you.DamageSeries.Count is > 0 and <= FightReport.SeriesShown + 1 && r.PartyLine.Length == 55 && you.DamageSeries.All(s => s.Values.Length == 55);
        Console.WriteLine($"  -> one chart point a second, at most {FightReport.SeriesShown} skills and the rest: {series}");
        bool history = HistoryRoundTrip(input, r);
        return totals && effects && defense && boss && support && series && history;
    }

    /// <summary>A fight saved in the history and opened again gives the same report; the index lists it newest first; delete takes the file too.</summary>
    private static bool HistoryRoundTrip(ReportInput input, FightReport r)
    {
        string folder = Path.Combine(Path.GetTempPath(), "a2h-history-" + Guid.NewGuid().ToString("N")[..8]);
        string before = History.FightLog.Folder;
        History.FightLog.Folder = folder;
        History.FightLog.Reload();
        try
        {
            History.FightLogEntry? boss = History.FightLog.Save(History.FightLogKind.Boss, input);
            History.FightLogEntry? run = History.FightLog.Save(History.FightLogKind.Run, input with { Start = input.Start.AddMinutes(-1), Targets = null }, "Normal", 600001, 3);
            bool saved = boss is { Killed: true } && run is { BossFights: 3, Killed: null } && History.FightLog.Entries().Count == 2 && History.FightLog.Entries()[0].Id == boss.Id
                && Math.Abs(boss.PartyDps - r.PartyDps) < 0.5 && boss.You == "Ichigo" && Math.Abs(boss.YourDps - r.PlayerOf(3)!.Dps) < 0.5;
            ReportInput? back = History.FightLog.Open(boss!);
            FightReport again = FightReport.Build(back!);
            bool same = again.Damage == r.Damage && again.Players.Count == r.Players.Count && again.PlayerOf(3)!.Skills.Count == r.PlayerOf(3)!.Skills.Count
                && again.PlayerOf(3)!.Skills.All(s => s.SkillId != 0) && again.BossTargets.Count == r.BossTargets.Count && again.CastBars.Count == r.CastBars.Count
                && again.PlayerOf(3)!.Buffs.Any(b => b.Name == "Undefeated Mantra" && b.Level == 15) && again.PlayerOf(3)!.Absorbed == r.PlayerOf(3)!.Absorbed;
            // uploaded later from the History: the same players and damage as the report, the own character as the uploader with its server; never a wipe
            History.FightLogEntry named = boss! with { BossNpcId = 2400001 };
            Upload.EncounterUploadRequest? sent = Upload.HistoryUpload.Build(named, back!, 2001, "glow");
            bool upload = sent is { ServerId: 2001, MeterBar: "glow", Report: not null } && sent.Participants.Count(p => p.IsSelf) == 1
                && sent.Participants.Single(p => p.IsSelf) is { Name: "Ichigo", ServerId: 2001 } self && self.TotalDamage == r.PlayerOf(3)!.Damage
                && sent.Participants.Count == 5 && sent.BossNpcId == 2400001
                && Upload.HistoryUpload.Build(named, back! with { Killed = false }, 2001, null) is null
                && !Upload.HistoryUpload.CanUpload(named with { Killed = false }) && Upload.HistoryUpload.CanUpload(named) && !Upload.HistoryUpload.CanUpload(boss!);
            Console.WriteLine($"  -> a saved boss fight uploads later with the report's players and damage, the own character's server, and a wipe never goes: {upload}");
            History.FightLog.Delete(boss!);
            History.FightLog.Reload();
            bool deleted = History.FightLog.Entries().Count == 1 && History.FightLog.Open(boss!) is null;
            Console.WriteLine($"  -> the history: saved on its own, listed newest first, opened again to the same report (skill ids, levels, casts), deleted with its file: {saved && same && deleted}");
            return saved && same && upload && deleted;
        }
        finally
        {
            History.FightLog.Folder = before;
            History.FightLog.Reload();
            try
            {
                Directory.Delete(folder, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
