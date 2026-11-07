using AionDPS.Schedule;

namespace AionDPS.Aion2;

/// <summary>
/// The Abyss Corridors: after each Artifact Siege a corridor opens for the side that holds an artifact, until the next siege; a visit is two minutes. The game
/// keeps them as tickets (<c>Contents_Ticket_ArtifactDungeon_001</c> to <c>_006</c>, <c>_101</c> to <c>_103</c>, <c>_201</c> to <c>_203</c>: ids 10000001 to 10000012),
/// each a time left in milliseconds. They are not refilled at the siege's end but when the character first enters the Abyss after it: a burst of
/// changes with reason 2, 130,000 ms (2 min 10 s) for each corridor open to the side and 0 for the others; entering one then counts its time down to 0.
/// Seen on the owner's recordings of 2 November (Lower Reshanta at 22:39, after the siege that ended 22:30: 1 = 0, 2 and 3 = 130,000; corridors 504001 and
/// 504002 then took 2 and 3 to 0) and 5 November (Upper Reshanta at 23:01: 1 and 3 = 130,000, 2 = 0; both entered). The login lists in between hold 0 for all.
/// </summary>
public static class Aion2Corridors
{
    public const int FirstId = 10000001, LastId = 10000012;

    /// <summary>A ticket of our own: when the corridors were refilled last (time, ms since 1970) and how many were open then (count). The id is no game id.</summary>
    public const int RefillId = 99000007;

    /// <summary>The zone the column takes its name from (the game's "Abyss Corridor").</summary>
    public const int ZoneId = 503001;

    /// <summary>One refill comes as a burst of frames; a refill this long after the last one is the next.</summary>
    private static readonly TimeSpan Burst = TimeSpan.FromMinutes(10);

    public static bool IsCorridor(int ticketId) => ticketId is >= FirstId and <= LastId;

    /// <summary>Notes a refill (reason 2) of corridor <paramref name="ticketId"/> in <paramref name="tickets"/> (the value is in already): the first of a burst
    /// clears the other corridors (left from the last siege), and the refill's own ticket gets the time and how many are open.</summary>
    public static void NoteRefill(Dictionary<int, TicketValue> tickets, int ticketId, DateTime at)
    {
        long ms = new DateTimeOffset(at.ToUniversalTime()).ToUnixTimeMilliseconds();
        bool fresh = !tickets.TryGetValue(RefillId, out TicketValue stamp) || ms < stamp.TimeMs || ms - stamp.TimeMs > (long)Burst.TotalMilliseconds;
        if (fresh)
        {
            for (int id = FirstId; id <= LastId; id++)
            {
                if (id != ticketId && tickets.ContainsKey(id))
                {
                    tickets[id] = default;
                }
            }
        }

        tickets[RefillId] = new TicketValue(OpenIn(tickets), 0, fresh ? ms : stamp.TimeMs);
    }

    /// <summary>How many corridors have time left.</summary>
    public static int OpenIn(IReadOnlyDictionary<int, TicketValue> tickets)
    {
        int open = 0;
        for (int id = FirstId; id <= LastId; id++)
        {
            if (tickets.TryGetValue(id, out TicketValue v) && v.TimeMs > 0)
            {
                open++;
            }
        }

        return open;
    }

    /// <summary>Since when the corridors are open (the end of the last siege) and until when (the start of the next), local times; <paramref name="SiegeOn"/>
    /// while a siege is on (none open then). Null when the timetable has no siege.</summary>
    public sealed record Window(DateTime? Since, DateTime? Until, bool SiegeOn);

    public static Window? WindowAt(DateTime now)
    {
        ScheduledEvent? siege = EventSchedule.Events.FirstOrDefault(e => e.Id == "abyss_raid");
        if (siege is null)
        {
            return null;
        }

        List<EventOccurrence> around = EventSchedule.Between(siege, now.AddDays(-8), now.AddDays(8));
        if (around.Count == 0)
        {
            return null;
        }

        bool on = around.Any(o => o.Start <= now && now < o.End);
        DateTime? since = around.Where(o => o.End <= now).Select(o => (DateTime?)o.End).Max();
        DateTime? until = around.Where(o => o.Start > now).Select(o => (DateTime?)o.Start).Min();
        return new Window(since, until, on);
    }

    /// <summary>What the column shows for a character: <paramref name="Left"/> corridors left of <paramref name="Open"/>; <paramref name="From"/> the character the
    /// numbers were worked out from when this one has not been in the Abyss since the siege (it gets as many as that one was given), null when they are its own.</summary>
    public sealed record State(int Left, int Open, string? From);

    /// <summary>The corridors of <paramref name="character"/> in the window: its own when it was in the Abyss since the siege; else as many as the newest refill
    /// of a character of the same server (and account, where known) - one side per server and account; null when no one was in the Abyss since.</summary>
    public static State? Of(TicketCharacter character, IEnumerable<TicketCharacter> others, Window window)
    {
        if (window.SiegeOn || window.Since is not { } since)
        {
            return null;
        }

        long from = new DateTimeOffset(since.ToUniversalTime()).ToUnixTimeMilliseconds();
        if (character.Tickets.TryGetValue(RefillId, out TicketValue own) && own.TimeMs >= from)
        {
            return new State(OpenIn(character.Tickets), own.Count, null);
        }

        TicketCharacter? best = null;
        TicketValue newest = default;
        foreach (TicketCharacter other in others)
        {
            if (other.Name == character.Name || other.ServerId != character.ServerId || other.ServerId <= 0
                || (other.Account is not null && character.Account is not null && other.Account != character.Account))
            {
                continue;
            }

            if (other.Tickets.TryGetValue(RefillId, out TicketValue stamp) && stamp.TimeMs >= from && stamp.TimeMs > newest.TimeMs)
            {
                best = other;
                newest = stamp;
            }
        }

        return best is null ? null : new State(newest.Count, newest.Count, best.Name);
    }
}
