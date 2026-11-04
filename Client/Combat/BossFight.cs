namespace AionDPS.Combat;

/// <summary>
/// A boss fight is the hits on the boss (and on another boss fought with it) from the first to the last blow on the boss - also the blows the game
/// reports just after the kill - the way the meters players compare against count a boss's DPS (5 November 2026: three fights of a party of five
/// matched to the hit, every player). The adds and the monsters around the boss are left out: they are not the boss's DPS (4 November 2026 they were
/// counted as part of the fight, which put every player 1-30 % above those meters). Kept apart from the window so the rules can be checked on their
/// own (see SelfCheckFightClock).
/// </summary>
public static class BossFight
{
    /// <summary>
    /// The damage shown for one selected target. For a boss: every hit on it, and on another boss, between the first and the last hit on it inside the
    /// run window. For anything else: the hits on that target inside the run window.
    /// </summary>
    /// <param name="damage">Damage events only (no heals).</param>
    /// <param name="from">Run window start, or null for the target's whole history.</param>
    /// <param name="to">Run window end (null with <paramref name="from"/>).</param>
    /// <param name="isBoss">True for a known boss.</param>
    public static List<DamageEvent> ShownHits(IEnumerable<DamageEvent> damage, int targetId, DateTime? from, DateTime? to, Func<int, bool> isBoss)
    {
        var all = damage as IReadOnlyList<DamageEvent> ?? damage.ToList();
        var onTarget = all.Where(ev => ev.TargetObjectId == targetId && InWindow(ev, from, to)).ToList();
        if (onTarget.Count == 0 || !isBoss(targetId))
        {
            return onTarget;
        }

        DateTime first = onTarget.Min(ev => ev.Timestamp);
        DateTime last = onTarget.Max(ev => ev.Timestamp);
        var bosses = new Dictionary<int, bool>(); // asked once per target, not per hit
        return all.Where(ev => ev.Timestamp >= first && ev.Timestamp <= last
                && (ev.TargetObjectId == targetId || (bosses.TryGetValue(ev.TargetObjectId, out bool boss) ? boss : bosses[ev.TargetObjectId] = isBoss(ev.TargetObjectId))))
            .ToList();
    }

    /// <summary>DPS over the fight: the source's damage in <paramref name="shown"/> over the time
    /// from the first to the last hit on the target (the boss, for a boss fight).</summary>
    public static double? Dps(IReadOnlyList<DamageEvent> shown, int targetId, int sourceId)
    {
        DateTime? first = null, last = null;
        long damage = 0;
        foreach (DamageEvent ev in shown)
        {
            if (ev.TargetObjectId == targetId)
            {
                first = first is null || ev.Timestamp < first ? ev.Timestamp : first;
                last = last is null || ev.Timestamp > last ? ev.Timestamp : last;
            }

            if (ev.SourceObjectId == sourceId)
            {
                damage += ev.Amount;
            }
        }

        double seconds = first is DateTime a && last is DateTime b ? (b - a).TotalSeconds : 0;
        return seconds > 0 ? damage / seconds : null;
    }

    /// <summary>The time from the first to the last hit on the target in <paramref name="shown"/>, in seconds (0 for none or a single instant).
    /// The same for every source of the fight, so a list of rows asks for it once and divides each source's damage by it
    /// (<see cref="Dps"/> asks the list anew for each source).</summary>
    public static double SecondsOnTarget(IReadOnlyList<DamageEvent> shown, int targetId)
    {
        DateTime? first = null, last = null;
        foreach (DamageEvent ev in shown)
        {
            if (ev.TargetObjectId == targetId)
            {
                first = first is null || ev.Timestamp < first ? ev.Timestamp : first;
                last = last is null || ev.Timestamp > last ? ev.Timestamp : last;
            }
        }

        return first is DateTime a && last is DateTime b ? (b - a).TotalSeconds : 0;
    }

    /// <summary>
    /// Whether this batch starts a new boss pull, so the meter should start from zero: it hits a
    /// boss that has no hit on record yet, or one whose health was reset to full (a wipe) after its
    /// last recorded hit. Not while another boss is hurt and alive (two bosses at once).
    /// </summary>
    /// <param name="recorded">The meter's damage on record.</param>
    /// <param name="lastResetOf">The last time a boss was reset to full health, if ever.</param>
    /// <param name="otherBossFightUnfinished">True while a boss other than this one is hurt and alive.</param>
    public static bool StartsNewPull(IReadOnlyList<DamageEvent> batch, IReadOnlyList<DamageEvent> recorded,
        Func<int, bool> isBoss, Func<int, DateTime?> lastResetOf, Func<int, bool> otherBossFightUnfinished)
    {
        if (recorded.Count == 0)
        {
            return false;
        }

        foreach (int boss in batch.Where(ev => !ev.IsHeal).Select(ev => ev.TargetObjectId).Distinct().Where(isBoss))
        {
            DateTime? lastHit = null;
            for (int i = recorded.Count - 1; i >= 0; i--)
            {
                if (!recorded[i].IsHeal && recorded[i].TargetObjectId == boss)
                {
                    lastHit = recorded[i].Timestamp;
                    break;
                }
            }

            bool fresh = lastHit is null || lastResetOf(boss) is DateTime reset && reset > lastHit;
            if (fresh && !otherBossFightUnfinished(boss))
            {
                return true;
            }
        }

        return false;
    }

    private static bool InWindow(DamageEvent ev, DateTime? from, DateTime? to) =>
        from is not DateTime start || (ev.Timestamp >= start && ev.Timestamp <= (to ?? DateTime.MaxValue));
}
