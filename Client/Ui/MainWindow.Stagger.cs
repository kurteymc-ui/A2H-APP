using System.Globalization;
using System.Windows;
using AionDPS.Combat;

namespace AionDPS.Ui;

/// <summary>
/// The meter's Stagger view (7 November 2026, the owner: "if you keep wiping to the same boss I can't sit down after the fight and go look at logs ... see
/// it at a glance"; then: "instead of doing what you did, a button called stagger after group, this is too bulky"). The scope switch has "Stagger" after
/// "Group": it shows, in place of the lines, the boss's latest stagger check in short (<see cref="StaggerCardView.MeterView"/>) - its mechanic and the
/// time left or how it went, a thin bar of what is left, every player of the group with the exact amount they took off it (0 for one who took nothing
/// off) and the skills that did it, each with its hits on hover - and the boss's earlier checks as chips to click. A check stays after it ends (broken,
/// or not with what was left) until the next one starts or five minutes go by, so a wipe's check can be read before the next pull. Any other scope, or
/// a measure picked in the menu, brings the lines back.
/// </summary>
public partial class MainWindow
{
    /// <summary>How long an ended check stays on the meter.</summary>
    private static readonly TimeSpan StaggerKeep = TimeSpan.FromMinutes(5);

    /// <summary>The Stagger view is on (its switch after Group).</summary>
    private bool _overlayStagger;

    private long _staggerAt;
    private string _staggerShown = "";

    /// <summary>An earlier check clicked on the chips: shown until a new check starts.</summary>
    private DateTime? _staggerPicked;

    /// <summary>The checks that ended, kept as they were: the meter's events can be reset after a wipe, the view still shows who did what.</summary>
    private readonly Dictionary<(int Boss, DateTime Start), StaggerCheck> _staggerDone = new();

    /// <summary>The Stagger switch: on, the view in place of the lines; on again (or another scope), the lines.</summary>
    private void SetStaggerView(bool on)
    {
        _overlayStagger = on;
        _staggerPicked = null;
        _staggerShown = "";
        _staggerAt = 0;
        if (on)
        {
            _overlayBuffs = false;
            _dummyCardHidden = _dummyHeader;
        }

        ApplyStaggerView();
        RefreshRows();
        UpdateOverlayStagger();
        UpdateOverlayFold();
    }

    /// <summary>The lines or the Stagger view, and the switch marking it.</summary>
    private void ApplyStaggerView()
    {
        CompactOverlayRowsArea.Visibility = _overlayStagger ? Visibility.Collapsed : Visibility.Visible;
        if (!_overlayStagger)
        {
            OverlayStaggerCard.Visibility = Visibility.Collapsed;
        }

        UpdateScopeControls();
    }

    private void UpdateOverlayStagger()
    {
        if (Headless || !_overlayStagger)
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
        List<StaggerCheck> checks = !_historyMode && !_pvpOnly && _source?.Entities is Aion2.Aion2EntityDirectory dir ? CurrentStaggerChecks(dir, now) : new List<StaggerCheck>();
        checks.RemoveAll(c => c.End is DateTime end && now - end > StaggerKeep && c.Outcome != StaggerOutcome.Running);
        StaggerCheck? latest = StaggerChecks.Latest(checks);
        if (latest?.Outcome == StaggerOutcome.Running)
        {
            _staggerPicked = null; // a new check: shown at once
        }

        StaggerCheck? shown = _staggerPicked is DateTime picked ? checks.FirstOrDefault(c => c.Start == picked) ?? latest : latest;
        IReadOnlyList<StaggerPerson> people = StaggerPeople();
        string? next = shown is null || shown.Outcome != StaggerOutcome.Running ? NextStaggerCheck() : null;

        // rebuilt only when something on it changed (a tooltip being read stays open); a running check's time moves every time
        string key = string.Join("|", shown?.Start.Ticks, shown?.Outcome, shown?.Left, shown?.Outcome == StaggerOutcome.Running ? Math.Round(shown.Seconds(now), 1) : 0, next,
            string.Join(",", checks.Select(c => c.Start.Ticks + ":" + c.Outcome)),
            shown is null ? "" : string.Join(",", shown.Credit.Players.Select(p => p.Player + ":" + p.Amount + ":" + p.Skills.Count)), string.Join(",", people.Select(p => p.Id)));
        if (key != _staggerShown || OverlayStaggerCard.Visibility != Visibility.Visible)
        {
            _staggerShown = key;
            ShowStaggerView(checks, shown, now, people, next);
        }
    }

    /// <summary>The checks of the boss whose stagger bar the game sent last (in the last ten minutes), oldest first; empty without one.</summary>
    private List<StaggerCheck> CurrentStaggerChecks(Aion2.Aion2EntityDirectory dir, DateTime now)
    {
        if (dir.Combat.LastStagger() is not (int boss, DateTime at) || now - at > TimeSpan.FromMinutes(10) || dir.BossNpcIdOf(boss) is null)
        {
            return new List<StaggerCheck>();
        }

        DateTime since = now.AddMinutes(-10);
        List<StaggerEvent> readings = dir.Combat.StaggersSince(boss, since);
        if (readings.Count == 0)
        {
            return new List<StaggerCheck>();
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

        return checks;
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

    /// <summary>The Stagger view with the checks as given (the pictures call it too): the view in place of the lines, the amount taken off in big.</summary>
    internal void ShowStaggerView(IReadOnlyList<StaggerCheck> checks, StaggerCheck? shown, DateTime now, IReadOnlyList<StaggerPerson> people, string? next)
    {
        if (!_overlayStagger)
        {
            _overlayStagger = true;
            ApplyStaggerView();
        }

        Func<int, StaggerPerson> nameOf = Headless ? id => new StaggerPerson(id, "#" + id, "") : StaggerPersonOf;
        OverlayStaggerCard.Child = StaggerCardView.MeterView(checks, shown, now, people, nameOf, next, Headless ? null : c =>
        {
            _staggerPicked = c.Start;
            _staggerShown = "";
            _staggerAt = 0;
            UpdateOverlayStagger();
        });
        OverlayStaggerCard.Visibility = Visibility.Visible;
        OverlayTotalRateText.Text = shown is null ? "–" : shown.Taken.ToString("N0", CultureInfo.CurrentCulture);
        OverlayTotalUnitText.Text = shown is null ? "Stagger" : "of " + shown.Max.ToString("N0", CultureInfo.CurrentCulture) + " off";
    }
}
