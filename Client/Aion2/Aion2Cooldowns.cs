using System.Buffers.Binary;
using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2;

/// <summary>Why a ready skill is left off the assist's list (see <see cref="Aion2Cooldowns.NotWorth"/>).</summary>
public enum NotWorthListing { NoCooldown, Chain }

/// <summary>One entry of the cooldown list: the skill (or a shared cooldown group) and what is left of it, in the game's unit (see <see cref="Aion2Cooldowns"/>).</summary>
public readonly record struct CooldownEntry(int Id, int Remaining);

/// <summary>
/// What the assist shows of a skill's cooldown at a moment (<see cref="Aion2Cooldowns.View"/>): running or ready, the seconds left (a countdown that only
/// ever goes down) and the part of the cooldown still to go (1 at its start, 0 at its end).
/// </summary>
public readonly record struct CooldownView(bool Running, double Seconds, double Fraction)
{
    public static readonly CooldownView Ready = new(false, 0, 0);

    /// <summary>The whole seconds shown: rounded up, and at least 1 until the game says the skill is ready.</summary>
    public int WholeSeconds => Running ? Math.Max(1, (int)Math.Ceiling(Seconds - 1e-9)) : 0;
}

/// <summary>
/// The local player's cooldowns: the frame <c>0x4738</c>, <c>n (u8) | n x (id u32 | remaining varint)</c>, 0 = ready. Since late October 2026 the game sends
/// one skill per frame (n = 1) while it runs, about every 0.6 s, and the whole list (all skills of the character) only at login; before, the whole list came
/// every 0.6 s. A frame with one or a few skills changes only those: the list is kept per skill (it was replaced by the short frame when the last frame was
/// older than 10 s, so after a pause the assist showed one skill, then the other - 4 November 2026).
/// <para>The unit is not settled: the number falls about 1.5 to 4 times as fast as the clock, not the same for every skill and not the same for every use of
/// it. The time left comes from the speed of the skill's own numbers since its use, mixed with the speed of its
/// last uses (kept in the settings) while there are few numbers yet; what is shown (<see cref="View"/>) is smoothed so that it only ever counts down.</para>
/// </summary>
public static class Aion2Cooldowns
{
    private static readonly object Gate = new();
    private static DateTime _at;

    /// <summary>A frame with at least this many entries is the whole list (at login: every skill of the character).</summary>
    private const int WholeList = 5;

    private sealed class Slot
    {
        public int Remaining;
        public DateTime At;
        public int Order;
    }

    /// <summary>Per skill (or group) the last number the game sent and when; the order is that of the last whole list, then first seen.</summary>
    private static readonly Dictionary<int, Slot> State = new();

    private static int _nextOrder = 1000;

    /// <summary>The class of the character the list belongs to: the first two digits of its skills (11 Gladiator, 12 Templar ...); 0 while unknown.</summary>
    private static int _class;

    /// <summary>The numbers seen per skill since it last was ready (or last started again), oldest first.</summary>
    private static readonly Dictionary<int, List<(DateTime At, int Value)>> Runs = new();

    /// <summary>The speed assumed until a skill's own was measured (units per second): the counter falls about 2.4 times as fast as the clock.</summary>
    public const double FallbackRate = 2400;

    private const int KeptSamples = 64;

    /// <summary>A number that rises by more than this over the one before is a new use of the skill (the counter starts again).</summary>
    private const int RestartRise = 200;

    /// <summary>What one step may fall beyond the run's middle speed (capture and batching jitter, about 0.6 s of counter); more than this is a jump (a stone that took time off).</summary>
    private const double JumpResidual = 1500;

    /// <summary>Seconds of a skill's own numbers after which their speed alone counts (before: mixed with the speed of its last uses).</summary>
    private const double OwnRateAfter = 3.0;

    /// <summary>The speeds of a skill's last finished uses (units per second), newest last; the typical speed is their middle one.</summary>
    private static readonly Dictionary<int, List<double>> UseRates = new();

    private const int KeptUseRates = 8;

    /// <summary>Counts every change of the typical speeds (what is kept in the settings follows it).</summary>
    public static int RatesVersion { get; private set; }

    /// <summary>The skills seen with a cooldown of more than 2 s (any character; the assist shows those of the character in the game), in the order of the
    /// game's whole list.</summary>
    private static readonly List<int> Known = new();

    /// <summary>Counts every change of <see cref="KnownSkills"/> (what is kept in the settings follows it).</summary>
    public static int KnownVersion { get; private set; }

    /// <summary>Raised (on the capturing thread) when a list arrived.</summary>
    public static event Action? Changed;

    /// <summary>The list as it stands (every skill and group the game named, in the order of its whole list) and when the last frame came.</summary>
    public static (IReadOnlyList<CooldownEntry> Entries, DateTime At) Last
    {
        get
        {
            lock (Gate)
            {
                return (State.OrderBy(kv => kv.Value.Order).Select(kv => new CooldownEntry(kv.Key, kv.Value.Remaining)).ToList(), _at);
            }
        }
    }

    /// <summary>The frame (opcode included) must read to its last byte.</summary>
    public static bool TryParse(ReadOnlySpan<byte> frame, out List<CooldownEntry> entries)
    {
        entries = new List<CooldownEntry>();
        if (frame.Length < 3)
        {
            return false;
        }

        int count = frame[2];
        int p = 3;
        for (int i = 0; i < count; i++)
        {
            if (p + 4 > frame.Length)
            {
                return false;
            }

            int id = (int)BinaryPrimitives.ReadUInt32LittleEndian(frame[p..]);
            p += 4;
            long value = 0;
            int shift = 0;
            bool done = false;
            while (p < frame.Length && shift < 35)
            {
                byte b = frame[p++];
                value |= (long)(b & 0x7f) << shift;
                if ((b & 0x80) == 0)
                {
                    done = true;
                    break;
                }

                shift += 7;
            }

            if (!done || value > int.MaxValue)
            {
                return false;
            }

            entries.Add(new CooldownEntry(id, (int)value));
        }

        return p == frame.Length;
    }

    /// <summary>The class digits of a skill (11 Gladiator, 12 Templar ...); 0 for a group, a shared skill or a dodge.</summary>
    public static int ClassOf(int id) => id >= 11_000_000 && id / 10_000 % 100 != 0 ? id / 1_000_000 : 0;

    public static void Receive(List<CooldownEntry> entries, DateTime at)
    {
        lock (Gate)
        {
            // another character's skills: the list of the one before is gone
            int cls = entries.Select(e => ClassOf(e.Id)).FirstOrDefault(c => c != 0);
            if (cls != 0 && cls != _class)
            {
                if (_class != 0)
                {
                    State.Clear();
                    Runs.Clear();
                    Display.Clear();
                }

                _class = cls;
            }

            // the whole list: what it does not name is gone; a short frame changes only its skills
            bool whole = entries.Count >= WholeList;
            if (whole)
            {
                foreach (int gone in State.Keys.Where(id => entries.All(e => e.Id != id)).ToList())
                {
                    State.Remove(gone);
                    Runs.Remove(gone);
                    Display.Remove(gone);
                }
            }

            for (int i = 0; i < entries.Count; i++)
            {
                CooldownEntry e = entries[i];
                if (!State.TryGetValue(e.Id, out Slot? slot))
                {
                    State[e.Id] = slot = new Slot { Order = _nextOrder++ };
                }

                if (whole)
                {
                    slot.Order = i;
                }

                slot.Remaining = e.Remaining;
                slot.At = at;
            }

            if (whole)
            {
                OrderKnown();
            }

            _at = at;
            foreach (CooldownEntry e in entries)
            {
                Note(e, at);
            }

            Advance(at);
        }

        Changed?.Invoke();
    }

    private static void Note(CooldownEntry e, DateTime at)
    {
        if (e.Remaining <= 0)
        {
            Finish(e.Id);
            Runs.Remove(e.Id);
            return;
        }

        // more than 2 s left: a cooldown to speak of (and not one of the skills learned to have none)
        if (e.Remaining / RateOf(e.Id, Runs.GetValueOrDefault(e.Id)).Rate > NoCooldownSeconds)
        {
            int id = BaseOf(e.Id);
            LongSeen[id] = at;
            Timed.Add(id);
            if (Learned.Remove(id))
            {
                LearnedVersion++;
            }

            if (e.Id >= FirstSkillId && !Known.Contains(e.Id) && NotWorth(e.Id) is null) // a basic attack or chain skill on a lock: never listed
            {
                Known.Add(e.Id);
                OrderKnown();
                KnownVersion++;
            }
        }

        if (!Runs.TryGetValue(e.Id, out List<(DateTime At, int Value)>? run))
        {
            Runs[e.Id] = run = new List<(DateTime, int)>();
        }

        if (run.Count > 0)
        {
            (DateTime lastAt, int lastValue) = run[^1];
            if (at <= lastAt)
            {
                return;
            }

            if (e.Remaining > lastValue + RestartRise)
            {
                Finish(e.Id); // used again
                run.Clear();
            }
        }

        run.Add((at, e.Remaining));
        if (run.Count > KeptSamples)
        {
            run.RemoveAt(0);
        }
    }

    /// <summary>A use ended (ready, or used again): the speed of its numbers joins the skill's last ones, when they span 2 s at least.</summary>
    private static void Finish(int id)
    {
        if (!Runs.TryGetValue(id, out var run) || run.Count < 3 || (run[^1].At - run[0].At).TotalSeconds < 2 || Fit(run) is not { } rate)
        {
            return;
        }

        if (!UseRates.TryGetValue(id, out List<double>? rates))
        {
            UseRates[id] = rates = new List<double>();
        }

        rates.Add(rate);
        if (rates.Count > KeptUseRates)
        {
            rates.RemoveAt(0);
        }

        RatesVersion++;
    }

    /// <summary>The known skills in the order of the game's whole list (the skills it does not name keep their place after them).</summary>
    private static void OrderKnown()
    {
        var order = Known.Select((id, i) => (id, i)).OrderBy(x => State.TryGetValue(x.id, out Slot? s) && s.Order < 1000 ? s.Order : 1000 + x.i).Select(x => x.id).ToList();
        if (!order.SequenceEqual(Known))
        {
            Known.Clear();
            Known.AddRange(order);
            KnownVersion++;
        }
    }

    /// <summary>
    /// The speed of a skill's counter in units per second, measured from its own numbers since it started: a straight line through them, and where a
    /// stone took time off in between (a jump), only the numbers after it. Null while there are not two numbers at least half a second apart.
    /// </summary>
    public static double? MeasuredRate(int id)
    {
        lock (Gate)
        {
            return Runs.TryGetValue(id, out var run) ? Fit(run) : null;
        }
    }

    /// <summary>The typical speed of a skill: the middle one of its last uses; null before the first.</summary>
    public static double? TypicalRate(int id)
    {
        lock (Gate)
        {
            return Typical(id);
        }
    }

    private static double? Typical(int id)
    {
        if (!UseRates.TryGetValue(id, out var rates) || rates.Count == 0)
        {
            return null;
        }

        var sorted = rates.OrderBy(r => r).ToList();
        return sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
    }

    /// <summary>The speed used for a running cooldown: its own (from its numbers), mixed with the typical one while its numbers span less than 3 s; the
    /// typical one before there are two numbers; <see cref="FallbackRate"/> for a skill never seen.</summary>
    private static (double Rate, bool Measured) RateOf(int id, List<(DateTime At, int Value)>? run)
    {
        double? typical = Typical(id);
        double span = 0;
        double? own = run is null ? null : Fit(run, out span);
        if (own is null)
        {
            return (typical ?? FallbackRate, false);
        }

        if (typical is null)
        {
            return (own.Value, true);
        }

        double weight = Math.Clamp(span / OwnRateAfter, 0, 1);
        return (weight * own.Value + (1 - weight) * typical.Value, true);
    }

    private static double? Fit(IReadOnlyList<(DateTime At, int Value)> run) => Fit(run, out _);

    /// <param name="span">The seconds the numbers used span.</param>
    private static double? Fit(IReadOnlyList<(DateTime At, int Value)> run, out double span)
    {
        span = 0;
        if (run.Count < 2)
        {
            return null;
        }

        // a jump: a step that falls by more than JumpResidual beyond what the run's middle speed explains (a stone took time off). The speed is that of
        // the numbers after the last jump; right after it (one number), that of the numbers before it.
        var steps = new List<double>();
        for (int i = 1; i < run.Count; i++)
        {
            double dt = (run[i].At - run[i - 1].At).TotalSeconds;
            if (dt > 0)
            {
                steps.Add((run[i - 1].Value - run[i].Value) / dt);
            }
        }

        int start = 0, before = -1;
        if (steps.Count >= 2)
        {
            steps.Sort();
            double middle = Math.Max(0, steps.Count % 2 == 1 ? steps[steps.Count / 2] : (steps[steps.Count / 2 - 1] + steps[steps.Count / 2]) / 2);
            for (int i = 1; i < run.Count; i++)
            {
                double dt = Math.Max(0, (run[i].At - run[i - 1].At).TotalSeconds);
                if (run[i - 1].Value - run[i].Value > middle * dt + JumpResidual)
                {
                    before = start;
                    start = i;
                }
            }
        }

        if (Line(run, start, run.Count - 1, out span) is { } rate)
        {
            return rate;
        }

        return before >= 0 ? Line(run, before, start - 1, out span) : null;
    }

    /// <summary>The speed of the numbers <paramref name="from"/> to <paramref name="to"/>: a straight line through them; null for fewer than two, less than
    /// half a second apart, or not falling.</summary>
    private static double? Line(IReadOnlyList<(DateTime At, int Value)> run, int from, int to, out double span)
    {
        span = to > from ? (run[to].At - run[from].At).TotalSeconds : 0;
        if (to - from < 1 || span < 0.5)
        {
            return null;
        }

        int n = to - from + 1;
        double meanT = 0, meanV = 0;
        for (int i = from; i <= to; i++)
        {
            meanT += (run[i].At - run[from].At).TotalSeconds;
            meanV += run[i].Value;
        }

        meanT /= n;
        meanV /= n;
        double sxx = 0, sxy = 0;
        for (int i = from; i <= to; i++)
        {
            double dt = (run[i].At - run[from].At).TotalSeconds - meanT;
            sxx += dt * dt;
            sxy += dt * (run[i].Value - meanV);
        }

        double slope = sxx > 0 ? sxy / sxx : 0;
        return slope < 0 ? -slope : null;
    }

    /// <summary>
    /// What is left of a running cooldown in seconds at <paramref name="now"/>, the speed used and whether the skill's own numbers went into it (otherwise
    /// the speed of its last uses or the assumed <see cref="FallbackRate"/>). <paramref name="remaining"/> and <paramref name="at"/> are the entry of the
    /// list and when it came; the skill's own last number is used when there is one. The raw estimate: <see cref="View"/> is what to show.
    /// </summary>
    public static (double Seconds, double Rate, bool Measured) Left(int id, int remaining, DateTime at, DateTime now)
    {
        lock (Gate)
        {
            return Estimate(id, remaining, at, now);
        }
    }

    private static (double Seconds, double Rate, bool Measured) Estimate(int id, int remaining, DateTime at, DateTime now)
    {
        Runs.TryGetValue(id, out var run);
        (double rate, bool measured) = RateOf(id, run);
        if (run is { Count: > 0 })
        {
            (at, remaining) = run[^1];
        }

        return (remaining / rate - Math.Max(0, (now - at).TotalSeconds), rate, measured);
    }

    // ---------- what is shown: a countdown that only goes down ----------
    // The estimate changes with every number the game sends (the speed varies within a use, the capture's timestamps jitter by a few tenths): shown as it
    // is, the time jumped up and down. What is shown follows it at between 0.4 and 2 times the speed of the clock, and never goes up; an estimate more than
    // 2 s below (a stone took time off) is taken at once. Measured against the recordings of 31 October to 4 November 2026 (the uses of 9 skills that
    // ended cleanly, every 0.1 s): half the time within 0.6 s of the real end, nine times in ten within 1.6 s, the whole seconds never going up and never
    // ready early (before: the raw estimate as it came, 0.7 s and 2.2 s off, going up again 66 times).

    private sealed class Shown
    {
        public DateTime RunStart;
        public DateTime Time;
        public double Seconds;
        public double Length;
    }

    private static readonly Dictionary<int, Shown> Display = new();

    /// <summary>The slowest and fastest the shown countdown runs, against the clock.</summary>
    private const double SlowestPace = 0.4, FastestPace = 2.0;

    /// <summary>An estimate this many seconds below the shown time is taken at once.</summary>
    private const double TakeAtOnce = 2.0;

    /// <summary>The estimate this far past the end without the game's 0: taken as ready (a frame was lost).</summary>
    private const double OverdueReady = 2.0;

    /// <summary>What to show of a skill's cooldown at <paramref name="now"/>: ready, or running with the seconds left and the part still to go. The
    /// seconds only ever go down within a use; a use of at most 2 s (a moment's lock) shows as ready.</summary>
    public static CooldownView View(int id, DateTime now)
    {
        lock (Gate)
        {
            if (!State.TryGetValue(id, out Slot? slot) || slot.Remaining <= 0 || !Runs.TryGetValue(id, out var run) || run.Count == 0)
            {
                Display.Remove(id);
                return CooldownView.Ready;
            }

            double raw = Estimate(id, slot.Remaining, slot.At, now).Seconds;
            DateTime start = run[0].At;
            if (!Display.TryGetValue(id, out Shown? shown) || shown.RunStart != start)
            {
                // a new use: its length counted from its first number
                double first = Math.Max(0, raw);
                Display[id] = shown = new Shown { RunStart = start, Time = now, Seconds = first, Length = first + Math.Max(0, (now - start).TotalSeconds) };
            }
            else if (now > shown.Time)
            {
                double dt = (now - shown.Time).TotalSeconds;
                double natural = shown.Seconds - dt;
                double next = raw < natural - TakeAtOnce ? raw : Math.Clamp(raw, shown.Seconds - FastestPace * dt, shown.Seconds - SlowestPace * dt);
                shown.Seconds = Math.Clamp(next, 0, shown.Seconds);
                shown.Time = now;
            }

            if (shown.Length <= NoCooldownSeconds || raw < -OverdueReady)
            {
                return CooldownView.Ready;
            }

            return new CooldownView(true, shown.Seconds, shown.Length > 0 ? Math.Clamp(shown.Seconds / shown.Length, 0, 1) : 0);
        }
    }

    /// <summary>The skills to show in the assist, in their order: the ones seen with a cooldown (of the character in the game), then the others of its list.</summary>
    public static IReadOnlyList<int> Skills()
    {
        lock (Gate)
        {
            if (_class == 0)
            {
                return Array.Empty<int>();
            }

            var list = Known.Where(id => ClassOf(id) == _class || (ClassOf(id) == 0 && State.ContainsKey(id))).ToList();
            var others = State.Where(kv => kv.Key >= FirstSkillId && !list.Contains(kv.Key) && (ClassOf(kv.Key) == _class || ClassOf(kv.Key) == 0))
                .OrderBy(kv => kv.Value.Order).Select(kv => kv.Key).ToList();
            list.AddRange(others);
            return list;
        }
    }

    /// <summary>Whether the skill was seen with a cooldown of more than 2 s (this session or an earlier one).</summary>
    public static bool IsKnown(int id)
    {
        lock (Gate)
        {
            return Known.Contains(id);
        }
    }

    /// <summary>The skills seen with a cooldown, for the settings.</summary>
    public static List<int> KnownSkills()
    {
        lock (Gate)
        {
            return Known.ToList();
        }
    }

    /// <summary>What the settings kept from earlier sessions (once, at start).</summary>
    public static void SeedKnown(IEnumerable<int> ids)
    {
        lock (Gate)
        {
            foreach (int id in ids)
            {
                if (id >= FirstSkillId && !Known.Contains(id))
                {
                    Known.Add(id);
                }
            }
        }
    }

    /// <summary>The typical speed per skill, for the settings.</summary>
    public static Dictionary<int, double> TypicalRates()
    {
        lock (Gate)
        {
            return UseRates.Keys.Select(id => (id, rate: Typical(id))).Where(x => x.rate is not null).ToDictionary(x => x.id, x => Math.Round(x.rate!.Value));
        }
    }

    /// <summary>The typical speeds the settings kept from earlier sessions (once, at start).</summary>
    public static void SeedRates(IReadOnlyDictionary<int, double> rates)
    {
        lock (Gate)
        {
            foreach ((int id, double rate) in rates)
            {
                if (rate > 0 && !UseRates.ContainsKey(id))
                {
                    UseRates[id] = new List<double> { rate };
                }
            }
        }
    }

    public static void Reset()
    {
        lock (Gate)
        {
            State.Clear();
            _class = 0;
            _nextOrder = 1000;
            _at = default;
            Runs.Clear();
            Display.Clear();
            UseRates.Clear();
            Known.Clear();
            KnownVersion++;
            RatesVersion++;
            LongSeen.Clear();
            Casts.Clear();
            FreeUses.Clear();
            Timed.Clear();
            Learned.Clear();
            LearnedVersion++;
        }
    }

    // ---------- skills with no cooldown to speak of ----------
    // Basic attacks (Rending Blow, Keen Strike, a Templar's Judgment) have no cooldown: nothing to wait for, and in the assist's list they are
    // only a long row of "Ready". Two class lists say which ones they are (assets/aion2/skills/assist_skill_lists.json); the others are learned
    // from use: the own character used the skill (a hit or a cast of it) and the cooldown list never showed it with more than 2 s left around
    // that moment - three times in a row, and never with a cooldown. One cooldown longer than that takes a skill off again.

    /// <summary>A cooldown of at most this long is none to speak of.</summary>
    public const double NoCooldownSeconds = 2.0;

    /// <summary>Uses of a skill without a cooldown before it is taken for one with none.</summary>
    public const int FreeUsesToLearn = 3;

    private const int FirstSkillId = 10_000_000;

    /// <summary>Hits of the same skill closer than this are one use (a skill hitting several times, or several targets).</summary>
    private static readonly TimeSpan SameUse = TimeSpan.FromSeconds(1.5);

    /// <summary>How long after a use the cooldown list is given to show it (a cooldown frame comes with the cast; the hits come after it).</summary>
    private static readonly TimeSpan SettleAfter = TimeSpan.FromSeconds(1.5);

    /// <summary>A cooldown seen up to this long before the first hit of a use belongs to it (the cast comes before its hits).</summary>
    private static readonly TimeSpan RunBefore = TimeSpan.FromSeconds(3);

    /// <summary>Per skill (its base id) the last time the list showed it with more than 2 s left.</summary>
    private static readonly Dictionary<int, DateTime> LongSeen = new();

    /// <summary>Per skill the use not judged yet: its first and last hit.</summary>
    private static readonly Dictionary<int, (DateTime First, DateTime Last)> Casts = new();

    private static readonly Dictionary<int, int> FreeUses = new();

    /// <summary>The skills seen with a cooldown of more than 2 s (this session).</summary>
    private static readonly HashSet<int> Timed = new();

    private static readonly HashSet<int> Learned = new();
    private static (HashSet<int> NoCooldown, HashSet<int> Chain)? _lists;

    /// <summary>Counts every change of the skills learned to have no cooldown (what is kept in the settings follows it).</summary>
    public static int LearnedVersion { get; private set; }

    /// <summary>The id a skill's cooldown is listed under: the skill's base id (its stone and hit digits cleared), a basic attack or dodge as it is.</summary>
    public static int BaseOf(int skillId) => skillId < FirstSkillId || skillId / 10_000 % 100 == 0 ? skillId : skillId / 10_000 * 10_000;

    /// <summary>The own character used a skill (a hit or a cast of it, at the frame's time).</summary>
    public static void NoteOwnUse(int skillId, DateTime at)
    {
        if (skillId < FirstSkillId)
        {
            return;
        }

        int id = BaseOf(skillId);
        lock (Gate)
        {
            Advance(at);
            if (Casts.TryGetValue(id, out var cast))
            {
                if (at >= cast.First && at - cast.Last < SameUse)
                {
                    Casts[id] = (cast.First, at);
                    return;
                }

                Judge(id, cast.First);
            }

            Casts[id] = (at, at);
        }
    }

    /// <summary>The uses older than <see cref="SettleAfter"/> are judged: was the skill on a cooldown of more than 2 s around them?</summary>
    private static void Advance(DateTime now)
    {
        foreach (var (id, cast) in Casts.Where(c => now - c.Value.First >= SettleAfter).ToList())
        {
            Casts.Remove(id);
            Judge(id, cast.First);
        }
    }

    private static void Judge(int id, DateTime first)
    {
        if (LongSeen.TryGetValue(id, out DateTime seen) && seen >= first - RunBefore)
        {
            FreeUses.Remove(id); // a cooldown: noted as such when it was seen (Note)
            return;
        }

        if (Timed.Contains(id))
        {
            return; // a cooldown before, so this use came just as it ended or a frame was lost
        }

        int free = FreeUses[id] = FreeUses.GetValueOrDefault(id) + 1;
        if (free >= FreeUsesToLearn && Learned.Add(id))
        {
            LearnedVersion++;
        }
    }

    /// <summary>The skills learned to have no cooldown, for the settings.</summary>
    public static List<int> LearnedNoCooldown()
    {
        lock (Gate)
        {
            return Learned.OrderBy(i => i).ToList();
        }
    }

    /// <summary>What the settings kept from earlier sessions (once, at start).</summary>
    public static void SeedLearned(IEnumerable<int> ids)
    {
        lock (Gate)
        {
            foreach (int id in ids)
            {
                if (!Timed.Contains(id))
                {
                    Learned.Add(id);
                }
            }
        }
    }

    /// <summary>Why a skill is not worth a row while it is ready: none (null), it has no cooldown (a basic attack), or it is a chain skill (it shows among the chain skills when it opens).</summary>
    public static NotWorthListing? NotWorth(int skillId)
    {
        int id = BaseOf(skillId);
        var (noCooldown, chain) = _lists ??= LoadLists();
        lock (Gate)
        {
            if (chain.Contains(id))
            {
                return NotWorthListing.Chain;
            }

            return noCooldown.Contains(id) || Learned.Contains(id) ? NotWorthListing.NoCooldown : (NotWorthListing?)null;
        }
    }

    private static (HashSet<int>, HashSet<int>) LoadLists()
    {
        var noCooldown = new HashSet<int>();
        var chain = new HashSet<int>();
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "skills", "assist_skill_lists.json");
            if (File.Exists(path))
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
                noCooldown.UnionWith(doc.RootElement.GetProperty("noCooldown").EnumerateArray().Select(e => e.GetInt32()));
                chain.UnionWith(doc.RootElement.GetProperty("chain").EnumerateArray().Select(e => e.GetInt32()));
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            // no lists: only what is learned is left out
        }

        return (noCooldown, chain);
    }
}
