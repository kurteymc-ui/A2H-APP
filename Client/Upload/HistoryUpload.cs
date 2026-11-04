using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;
using AionDPS.Combat;
using AionDPS.Data;
using AionDPS.History;
using AionDPS.Update;

namespace AionDPS.Upload;

/// <summary>
/// Uploads a boss fight from the History (a fight that was not uploaded while it happened): the same request the live meter sends, built from the
/// fight's saved file instead of the capture. Everything the website shows comes along (players, skills, heals, damage taken, effects, timeline,
/// the fight report); what only the live capture knew is left out (gear and legion of the players, the boss's hit points and level). The site keeps
/// a fight once per uploader, so sending one again only gives back its page.
/// </summary>
public static class HistoryUpload
{
    /// <summary>The backend's ceiling for the players of one upload (as the live upload).</summary>
    private const int MaxParticipants = 300;

    /// <summary>Above this many players the effects are not cut per player (a crowded world boss), as the live upload.</summary>
    private const int MaxBuffRows = 60;

    /// <summary>Whether an entry can be uploaded from the History: a cleared boss fight (a wipe is never posted) the game named by its NPC.</summary>
    public static bool CanUpload(FightLogEntry entry) => entry.Kind == FightLogKind.Boss && entry.Killed == true && entry.BossNpcId is not null;

    /// <summary>The server of the entry's character: saved with the entry, else the server the app last saw that character on; null when unknown.</summary>
    public static int? ServerOf(FightLogEntry entry)
    {
        if (entry.ServerId is > 0)
        {
            return entry.ServerId;
        }

        if (string.IsNullOrEmpty(entry.You))
        {
            return null;
        }

        return Aion2Tickets.Snapshot().FirstOrDefault(c => c.ServerId > 0 && string.Equals(c.Name, entry.You, StringComparison.Ordinal))?.ServerId;
    }

    /// <summary>
    /// Uploads boss fights of the History, oldest first. Returns the page of the last one that went up (null when none did) and a line for the user.
    /// </summary>
    public static async Task<(string? Url, string Message)> SendAsync(IReadOnlyList<FightLogEntry> entries, string? meterBar)
    {
        var list = entries.Where(CanUpload).OrderBy(e => e.Start).ToList();
        if (list.Count == 0)
        {
            return (null, "Nothing here can be uploaded: only cleared boss fights go to the website.");
        }

        string? url = null, error = null;
        int sent = 0;
        foreach (FightLogEntry entry in list)
        {
            if (ServerOf(entry) is not int serverId)
            {
                error = $"The meter does not know {(string.IsNullOrEmpty(entry.You) ? "your character" : entry.You)}'s server yet: log in with it while the meter runs, then try again.";
                continue;
            }

            EncounterUploadRequest? payload = await Task.Run(() => FightLog.Open(entry) is { } input ? Build(entry, input, serverId, meterBar) : null);
            if (payload is null)
            {
                error ??= "This fight's file is gone, or it has no player the website takes (your own character must be in it).";
                continue;
            }

            UploadResult result = await UploadClient.SendAsync(payload);
            if (result.Success)
            {
                sent++;
                url = result.Url ?? url;
            }
            else
            {
                error = result.Error;
            }
        }

        string message = sent == 0 ? $"Upload failed: {error ?? "nothing was sent."}"
            : sent == list.Count ? (sent == 1 ? "Uploaded." : $"Uploaded {sent} boss fights.")
            : $"Uploaded {sent} of {list.Count} boss fights. {error}";
        return (url, message);
    }

    /// <summary>The upload of one saved boss fight; null when it has nothing the website takes (no boss, or the own character is not in it).</summary>
    internal static EncounterUploadRequest? Build(FightLogEntry entry, ReportInput input, int serverId, string? meterBar)
    {
        if (!input.Killed || input.BossId is not int bossId || (input.BossNpcId ?? entry.BossNpcId) is not int bossNpc)
        {
            return null;
        }

        DateTime start = input.Start, end = input.End;
        FightExtras extras = input.Extras;
        Func<int, string?> nameOf = input.NameOf ?? (_ => null);
        var playerIds = input.Players.Select(p => p.Id).ToHashSet();
        bool cutEffects = input.Players.Count <= MaxBuffRows;

        var hitsBySource = new Dictionary<int, List<DamageEvent>>();
        var healsBySource = new Dictionary<int, List<DamageEvent>>();
        var takenBy = new Dictionary<int, long>();
        var bossHits = new List<DamageEvent>();
        foreach (DamageEvent e in input.Events)
        {
            if (e.Timestamp < start || e.Timestamp > end)
            {
                continue;
            }

            if (e.IsHeal)
            {
                Add(healsBySource, e.SourceObjectId, e);
            }
            else if (e.TargetObjectId == bossId)
            {
                Add(hitsBySource, e.SourceObjectId, e);
                bossHits.Add(e);
            }
            else if (e.SourceObjectId == bossId)
            {
                takenBy[e.TargetObjectId] = takenBy.GetValueOrDefault(e.TargetObjectId) + e.Amount;
            }
        }

        if (bossHits.Count == 0)
        {
            return null;
        }

        double fightSeconds = (bossHits.Max(e => e.Timestamp) - bossHits.Min(e => e.Timestamp)).TotalSeconds;
        double durationSeconds = Math.Max((end - start).TotalSeconds, 0.001);
        Dictionary<int, AnalysisUpload> analysis = cutEffects
            ? FightAnalysis.Build(input.Events, bossId, extras, playerIds.ToList(), id => id != bossId && !playerIds.Contains(id), start, end)
            : new Dictionary<int, AnalysisUpload>();

        var participants = new List<ParticipantUpload>();
        var empty = new List<DamageEvent>();
        foreach (ReportPlayer p in input.Players)
        {
            if (p.Name.StartsWith("Player #", StringComparison.Ordinal) || p.Name.StartsWith("0x", StringComparison.Ordinal) || !ClassCatalog.IsKnownClass(p.ClassName))
            {
                continue;
            }

            List<DamageEvent> hits = hitsBySource.GetValueOrDefault(p.Id) ?? empty;
            List<DamageEvent> heals = healsBySource.GetValueOrDefault(p.Id) ?? empty;
            if (hits.Count == 0 && heals.Count == 0 && !p.IsSelf && !takenBy.ContainsKey(p.Id))
            {
                continue;
            }

            long damage = hits.Sum(e => e.Amount);
            double idps = fightSeconds > 0 && hits.Count > 0 ? damage / fightSeconds : 0;
            bool estimated = hits.Count > 0 && hits.Count(e => e.Estimated) * 2 > hits.Count;
            var skills = SkillBreakdown.For(hits).Select(s => new SkillUsageUpload(s.Skill, s.Hits, estimated ? 0 : s.CritHits, s.Total, s.Min, s.Max)).ToList();
            var healSkills = SkillBreakdown.For(heals, heals: true).Select(s => new SkillUsageUpload(s.Skill, s.Hits, s.CritHits, s.Total, s.Min, s.Max)).ToList();
            long healing = heals.Sum(e => e.Amount);
            participants.Add(new ParticipantUpload(
                p.Name, p.ClassName, "", p.IsSelf,
                damage, idps, idps, healing, healing / durationSeconds, skills, healSkills, takenBy.GetValueOrDefault(p.Id),
                cutEffects ? BuffUpload.For(extras, p.Id, start, end, nameOf).ToList() : new List<BuffUsageUpload>(),
                ServerId: p.IsSelf ? serverId : null,
                Estimated: estimated,
                Timeline: cutEffects ? TimelineBuilder.For(extras, p.Id, start, end, nameOf, bossId, analysis.GetValueOrDefault(p.Id)) : null));
        }

        if (participants.Count > MaxParticipants)
        {
            participants = participants.Where(x => x.IsSelf).Concat(participants.Where(x => !x.IsSelf).OrderByDescending(x => x.TotalDamage)).Take(MaxParticipants).ToList();
        }

        // effects given by nobody of the fight came from a monster; the shields that took the boss's hits are the players' absorbed damage
        var names = participants.Select(x => x.Name).ToHashSet();
        var byId = input.Players.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().Name);
        var absorbed = new Dictionary<string, long>();
        foreach (DamageEvent shield in input.Shields)
        {
            if (shield.SourceObjectId == bossId && shield.Timestamp >= start && shield.Timestamp <= end && ShieldSkills.IsAbsorb(shield)
                && byId.TryGetValue(shield.TargetObjectId, out string? who))
            {
                absorbed[who] = absorbed.GetValueOrDefault(who) + shield.Amount;
            }
        }

        participants = participants.Select(x => x with
        {
            Buffs = BuffUpload.KeepGroupSources(x.Buffs ?? Array.Empty<BuffUsageUpload>(), names),
            DamageAbsorbed = absorbed.GetValueOrDefault(x.Name),
        }).ToList();

        // exactly one uploader: the own character (twice when the game gave it a new id during the run)
        if (participants.Count(x => x.IsSelf) > 1)
        {
            ParticipantUpload keep = participants.Where(x => x.IsSelf).MaxBy(x => x.TotalDamage)!;
            participants = participants.Select(x => x.IsSelf && !ReferenceEquals(x, keep) ? x with { IsSelf = false, ServerId = null } : x).ToList();
        }

        if (participants.Count == 0 || !participants.Any(x => x.IsSelf))
        {
            return null;
        }

        ReportUpload? report = null;
        if (cutEffects)
        {
            try
            {
                report = ReportUpload.From(FightReport.Build(input));
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or KeyNotFoundException)
            {
                report = null;
            }
        }

        string bossName = input.Title.StartsWith("0x", StringComparison.Ordinal) ? Aion2Npcs.NameOf(bossNpc, "en") ?? input.Title : input.Title;
        string serverName = Aion2Servers.NameOf(serverId);
        int? zone = entry.ZoneId > 0 ? entry.ZoneId : null;
        return new EncounterUploadRequest(
            AppVersion.Text, bossName, start.ToUniversalTime(), end.ToUniversalTime(), participants,
            "aion2:" + System.Text.RegularExpressions.Regex.Replace(serverName.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-'), serverName,
            Game: "aion2",
            BossNpcId: bossNpc,
            ServerId: serverId,
            BossDebuffs: cutEffects ? BuffUpload.OnBoss(extras, bossId, start, end, nameOf) : null,
            Report: report,
            ZoneId: zone,
            ZoneName: zone is int z ? Aion2Zones.NameOf(z, "en") : null,
            MeterBar: meterBar);
    }

    private static void Add(Dictionary<int, List<DamageEvent>> map, int key, DamageEvent e)
    {
        if (!map.TryGetValue(key, out List<DamageEvent>? list))
        {
            map[key] = list = new List<DamageEvent>();
        }

        list.Add(e);
    }
}
