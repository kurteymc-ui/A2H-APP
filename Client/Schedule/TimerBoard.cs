namespace AionDPS.Schedule;

/// <summary>One event at a moment: the occurrence running now (null when none) and its next start after now (null when there is none within a week).</summary>
public sealed record EventNow(ScheduledEvent Event, EventOccurrence? Live, EventOccurrence? Next);

/// <summary>
/// What every event of the timetable is doing at a moment, for the Timers page and the timers overlay (both tick once a second). It is worked out once and
/// kept until the next moment something changes (an event starts or ends, the rift's portals close) or the table is replaced, so a countdown that moves every
/// second costs no new reckoning of the time zones. Used from the UI thread only.
/// </summary>
public static class TimerBoard
{
    /// <summary>The events (in the table's order), the moment they were worked out for and the moment until which they hold.</summary>
    public sealed record Board(IReadOnlyList<EventNow> Events, DateTime Since, DateTime ValidUntil, IReadOnlyList<ScheduledEvent> Table);

    private static Board? _last;

    /// <summary>The board for <paramref name="now"/> (local time): the kept one while it still holds, else a new one. A new board is a new object, so a page
    /// can tell by reference whether it has to lay its lists out again.</summary>
    public static Board At(DateTime now)
    {
        IReadOnlyList<ScheduledEvent> table = EventSchedule.Events;
        if (_last is { } last && ReferenceEquals(last.Table, table) && now >= last.Since && now < last.ValidUntil)
        {
            return last;
        }

        // at most five minutes: a clock that was put right, or a day that turned, is caught up with soon
        DateTime until = now.AddMinutes(5);
        var events = new List<EventNow>(table.Count);
        foreach (ScheduledEvent scheduled in table)
        {
            if (scheduled.Always)
            {
                events.Add(new EventNow(scheduled, null, null));
                continue;
            }

            EventOccurrence? live = null;
            foreach (EventOccurrence occurrence in EventSchedule.Upcoming(scheduled, now))
            {
                if (occurrence.IsActiveAt(now) && (live is null || occurrence.End < live.End))
                {
                    live = occurrence;
                }
            }

            EventOccurrence? next = EventSchedule.NextAfter(scheduled, now);
            if (live is not null)
            {
                until = Min(until, live.End);
                if (live.OpenUntil > now)
                {
                    until = Min(until, live.OpenUntil);
                }
            }

            if (next is not null)
            {
                until = Min(until, next.Start);
            }

            events.Add(new EventNow(scheduled, live, next));
        }

        _last = new Board(events, now, until, table);
        return _last;
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}
