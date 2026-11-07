using System.Globalization;
using System.Windows;
using AionDPS.Combat;

namespace AionDPS.Ui;

/// <summary>
/// The stagger check on the meter (7 November 2026, the owner: "if you keep wiping to the same boss I can't sit down after the fight and go look at logs ...
/// see it at a glance"): over the lines, as soon as a boss's stagger check starts, the card of <see cref="StaggerCardView"/> - its mechanic, the time left,
/// the bar over the cast's time, every player of the group with the exact amount they took off it (0 for one who took nothing off) and the skills that did
/// it, each with its hits on hover. It stays after the check (broken, or not with what was left) until the next one starts or five minutes go by, so a
/// wipe's check can be read before the next pull; its × hides it until the next check. Its own switch in Meter look (MeterStaggerCard), on to begin with.
/// </summary>
public partial class MainWindow
{
    /// <summary>How long an ended check stays on the meter.</summary>
    private static readonly TimeSpan StaggerKeep = TimeSpan.FromMinutes(5);

    private long _staggerAt;
    private string _staggerShown = "";
    private DateTime? _staggerHidden;

    /// <summary>The checks that ended, kept as they were: the meter's events can be reset after a wipe, the card still shows who did what.</summary>
    private readonly Dictionary<(int Boss, DateTime Start), StaggerCheck> _staggerDone = new();

    private void UpdateOverlayStagger()
    {
        if (Headless)
        {
            return; // the pictures fill it themselves
        }

        long tick = Environment.TickCount64;
        if (tick - _staggerAt < 200)
        {
            return;
        }

        _staggerAt = tick;
        DateTime now = DateTime.Now;
        StaggerCheck? check = _gateSettings.MeterStaggerCard && !_historyMode && !_pvpOnly && _source?.Entities is Aion2.Aion2EntityDirectory dir ? CurrentStaggerCheck(dir, now) : null;
        if (check is null || (check.End is DateTime end && now - end > StaggerKeep) || (_staggerHidden is DateTime hidden && hidden == check.Start))
        {
            if (OverlayStaggerCard.Visibility != Visibility.Collapsed)
            {
                OverlayStaggerCard.Visibility = Visibility.Collapsed;
                _staggerShown = "";
            }

            return;
        }

        IReadOnlyList<StaggerPerson> people = StaggerPeople();
        string? next = check.Outcome != StaggerOutcome.Running ? NextStaggerCheck() : null;
        // rebuilt only when something on it changed (a tooltip being read stays open); a running check's time moves every time
        string key = string.Join("|", check.Start.Ticks, check.Outcome, check.Left, check.Outcome == StaggerOutcome.Running ? Math.Round(check.Seconds(now), 1) : 0, next,
            string.Join(",", check.Credit.Players.Select(p => p.Player + ":" + p.Amount + ":" + p.Skills.Count)), string.Join(",", people.Select(p => p.Id)));
        if (key == _staggerShown && OverlayStaggerCard.Visibility == Visibility.Visible)
        {
            return;
        }

        _staggerShown = key;
        DateTime start = check.Start;
        OverlayStaggerCard.Child = StaggerCardView.Card(check, now, people, StaggerPersonOf, next, overlay: true, close: () =>
        {
            _staggerHidden = start;
            OverlayStaggerCard.Visibility = Visibility.Collapsed;
        });
        OverlayStaggerCard.Visibility = Visibility.Visible;
    }

    /// <summary>The check to show: of the boss whose stagger bar the game sent last (in the last ten minutes), the one running, else the last one.</summary>
    private StaggerCheck? CurrentStaggerCheck(Aion2.Aion2EntityDirectory dir, DateTime now)
    {
        if (dir.Combat.LastStagger() is not (int boss, DateTime at) || now - at > TimeSpan.FromMinutes(10) || dir.BossNpcIdOf(boss) is null)
        {
            return null;
        }

        DateTime since = now.AddMinutes(-10);
        List<StaggerEvent> readings = dir.Combat.StaggersSince(boss, since);
        if (readings.Count == 0)
        {
            return null;
        }

        var windups = dir.Combat.WindupsSince(boss, readings[0].At.AddSeconds(-5));
        List<DamageEvent> hits = HitsOn(boss, readings[0].At.AddSeconds(-2));
        List<StaggerCheck> checks = StaggerChecks.Find(boss, readings, windups, hits, now);

        // an ended check as it was when it ended (the meter's events may have been reset since)
        for (int i = 0; i < checks.Count; i++)
        {
            var id = (boss, checks[i].Start);
            if (checks[i].Outcome == StaggerOutcome.Running)
            {
                continue;
            }

            if (_staggerDone.TryGetValue(id, out StaggerCheck? kept) && kept.Credit.Total >= checks[i].Credit.Total)
            {
                checks[i] = kept;
            }
            else
            {
                _staggerDone[id] = checks[i];
            }
        }

        foreach (var old in _staggerDone.Where(kv => kv.Value.End is DateTime e && now - e > StaggerKeep + TimeSpan.FromMinutes(5)).Select(kv => kv.Key).ToList())
        {
            _staggerDone.Remove(old);
        }

        return StaggerChecks.Latest(checks);
    }

    /// <summary>The hits on <paramref name="target"/> since <paramref name="since"/>, in the order they came (the meter's events, read from the newest back).</summary>
    private List<DamageEvent> HitsOn(int target, DateTime since)
    {
        IReadOnlyList<DamageEvent> events = _aggregator.Events;
        var found = new List<DamageEvent>();
        int older = 0;
        for (int i = events.Count - 1; i >= 0; i--)
        {
            DamageEvent e = events[i];
            if (e.Timestamp < since)
            {
                if (++older > 300)
                {
                    break; // the events come in time order but for a few late ones
                }

                continue;
            }

            older = 0;
            if (e.TargetObjectId == target && !e.IsHeal)
            {
                found.Add(e);
            }
        }

        found.Reverse();
        return found;
    }

    /// <summary>The group as the meter lists it: its players (not the enemies), each with 0 when they took nothing off.</summary>
    private IReadOnlyList<StaggerPerson> StaggerPeople() =>
        _rows.Where(r => !r.IsEnemy && r.ObjectId >= 0).GroupBy(r => r.ObjectId).Select(g => g.First())
            .Select(r => new StaggerPerson(r.ObjectId, r.Name, r.ClassName ?? "")).Take(12).ToList();

    private StaggerPerson StaggerPersonOf(int id) =>
        new(id, ResolveDisplayName(id), (_source?.Entities as Aion2.Aion2EntityDirectory)?.ClassOf(id) ?? "");

    /// <summary>The next check the boss timers expect: a cast known to start one, coming up (null: none).</summary>
    private string? NextStaggerCheck() =>
        _calloutFrame?.Next.FirstOrDefault(n => n.SkillId != 0 && StaggerChecks.IsMechanic(n.SkillId)) is { } c
            ? c.Casting ? $"Next stagger check: {c.Name} now" : $"Next stagger check: {c.Name} in {Math.Ceiling(c.In).ToString(CultureInfo.CurrentCulture)} s"
            : null;

    /// <summary>For the pictures: the card with a check as given.</summary>
    internal void ShowStaggerCard(StaggerCheck check, DateTime now, IReadOnlyList<StaggerPerson> people, string? next)
    {
        OverlayStaggerCard.Child = StaggerCardView.Card(check, now, people, id => new StaggerPerson(id, "#" + id, ""), next, overlay: true, close: () => { });
        OverlayStaggerCard.Visibility = Visibility.Visible;
    }
}
