using System.Buffers.Binary;
using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2;

/// <summary>Why a ready skill is left off the assist's list (see <see cref="Aion2Cooldowns.NotWorth"/>).</summary>
public enum NotWorthListing { NoCooldown, Chain }

/// <summary>One entry of the game's cooldown frame: the skill (or a shared cooldown group) and the milliseconds left of its cooldown (0 = ready).</summary>
public readonly record struct CooldownEntry(int Id, int Remaining);

/// <summary>
/// What the assist shows of a skill's cooldown at a moment (<see cref="Aion2Cooldowns.View"/>): running or ready, the seconds left and the part of the
/// cooldown still to go (1 at its start, 0 at its end).
/// </summary>
public readonly record struct CooldownView(bool Running, double Seconds, double Fraction)
{
    public static readonly CooldownView Ready = new(false, 0, 0);

    /// <summary>The whole seconds shown: rounded up, and at least 1 until the skill is ready.</summary>
    public int WholeSeconds => Running ? Math.Max(1, (int)Math.Ceiling(Seconds - 1e-9)) : 0;
}

/// <summary>
/// The local player's cooldowns.
/// <para>What the game sends (frame <c>0x4738</c>, <c>n (u8) | n x (id u32 | remaining varint)</c>): the milliseconds left of a skill's cooldown, which
/// run down with the clock. The whole list comes at login and after a teleport; afterwards a skill is sent only when something changes its cooldown - a hit
/// that takes a second or two off (a Templar's Pummel off Punishment, a basic attack off Warding Strike), a reset (0), the game's own correction. Most skills
/// are never sent when they are used (4 November 2026, the owner's Templar: Taunt, Shield Rush, Shield Smite, Debilitating Smash, Battlefield Banner, Noble
/// Armor and Defiance came only with the whole list): before, they showed only after a login, and as ready while they were cooling down.</para>
/// <para>So a use starts the cooldown (<see cref="NoteOwnCast"/>, the own cast frame) with the skill's length, and every number of the game sets the
/// time left exactly. The length is the game's own, by the skill's specialization: the cast frame's id is the variant (Warding Strike 12350150 with the
/// specializations 1 and 5, 25 s; without them 30 s), and the client's skill table has each variant's cooldown (assets/aion2/skills/assist_skill_lists.json,
/// "cooldowns", every class). The character's cooldown reduction shortens it a little (the owner's Templar: 5 % on most skills, none on Defiance), so the
/// length is also learned on the character: the time between two uses with no number of the game in between, when the skill is used as soon as it is back -
/// the tenth shortest of its last 16, never more than the table's (a few uses come earlier: an effect the game does not send took time off).</para>
/// </summary>
public static class Aion2Cooldowns
{
    private static readonly object Gate = new();
    private static DateTime _at;

    /// <summary>A frame with at least this many entries is the whole list (at login: the skills used lately).</summary>
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

    /// <summary>Raised when the class of the skills changes (the class before, the new one): another character logged in, its first skill, or the player's
    /// own pick in the Skill Assist. Raised outside the lock, on the thread that noticed it.</summary>
    public static event Action<int, int>? ClassChanged;

    /// <summary>The two digits of a class's skills by its name (Gladiator 11, Templar 12, Assassin 13, Ranger 14, Sorcerer 15, Spiritmaster 16, Cleric 17,
    /// Chanter 18; checked against the skill names: Throw Shadowblade 13, Rapid Fire 14, Bittercold Wind 15, Command: Proxy 16, Healing Light 17).</summary>
    public static int DigitsOf(string? className) => className switch
    {
        "Gladiator" => 11, "Templar" => 12, "Assassin" => 13, "Ranger" => 14, "Sorcerer" => 15, "Spiritmaster" => 16, "Cleric" => 17, "Chanter" => 18, _ => 0,
    };

    /// <summary>The class's name by its two digits (null for none).</summary>
    public static string? NameOfDigits(int cls) => cls switch
    {
        11 => "Gladiator", 12 => "Templar", 13 => "Assassin", 14 => "Ranger", 15 => "Sorcerer", 16 => "Spiritmaster", 17 => "Cleric", 18 => "Chanter", _ => null,
    };

    /// <summary>
    /// The class of the character in the game (7 November 2026: the Skill Assist listed the Assassin skills of the character before on a Ranger until the game
    /// sent a cooldown): from its record at the login, a skill it used, or the player's pick. The numbers of the class before are dropped.
    /// </summary>
    public static void SetClass(int cls)
    {
        int before;
        lock (Gate)
        {
            before = _class;
            if (cls == 0 || cls == before)
            {
                return;
            }

            SwitchClass(cls);
        }

        ClassChanged?.Invoke(before, cls);
    }

    // Under the lock: another character's skills - the numbers of the one before are gone.
    private static void SwitchClass(int cls)
    {
        if (_class != 0)
        {
            State.Clear();
            Runs.Clear();
            LastUse.Clear();
            LastCastFrame.Clear();
            LastTick.Clear();
            Framed.Clear();
            Variants.Clear();
            _barSeen = null;
        }

        _class = cls;
    }

    /// <summary>A cooldown running: when it ends, how long it is in all (for the part still to go), and whether the game's number set it.</summary>
    private sealed class Run
    {
        public DateTime Ends;
        public double Length;
        public bool FromGame;
    }

    private static readonly Dictionary<int, Run> Runs = new();

    /// <summary>Per skill its last use (the first cast frame of it) and the last cast frame of it at all.</summary>
    private static readonly Dictionary<int, DateTime> LastUse = new(), LastCastFrame = new(), LastTick = new();

    // The bar as the list last saw it, and when a skill left it (the latest use known then): a skill swapped off the bar is not listed again for a use
    // from before (5 November 2026, the owner: "when I swap a skill, it changes but then won't swap back to my skill").
    private static List<int>? _barSeen;
    private static readonly Dictionary<int, DateTime> LeftBar = new();

    /// <summary>Per skill (base id) the variant of its last use: the cast frame's id with its last digit cleared (the specializations chosen).</summary>
    private static readonly Dictionary<int, int> Variants = new();

    /// <summary>The skills the game sent a number for since their last use: the time to the next use then says nothing of the length.</summary>
    private static readonly HashSet<int> Framed = new();

    /// <summary>Per skill variant the times between two uses with no number of the game in between (seconds, the last 16, oldest first): what its length is
    /// learned from.</summary>
    private static readonly Dictionary<int, List<double>> Gaps = new();

    /// <summary>Per skill the length the game's first number after a use gave (time since the use + time left), this session.</summary>
    private static readonly Dictionary<int, double> FirstTotals = new();

    private const int KeptGaps = 16;

    /// <summary>Gaps used for a length: at least this many.</summary>
    private const int GapsForLength = 4;

    /// <summary>Two cast frames of a skill this close are one use (its effects come as cast frames of their own just after).</summary>
    private static readonly TimeSpan SameUse = TimeSpan.FromSeconds(1);

    /// <summary>A number of the game that ends at most this much later than the running cooldown belongs to it (a correction, not a new use).</summary>
    private static readonly TimeSpan SameRun = TimeSpan.FromSeconds(1.5);

    /// <summary>The game's first number this soon after a use counts from the use (its effects take a moment; the list after a teleport comes later).</summary>
    private static readonly TimeSpan FromUseWithin = TimeSpan.FromSeconds(60);

    /// <summary>Counts every change of the learned gaps (what is kept in the settings follows it).</summary>
    public static int GapsVersion { get; private set; }

    /// <summary>The skills known to have a cooldown (the game sent more than 2 s, or a use started one), in the order they came.</summary>
    private static readonly List<int> Known = new();

    /// <summary>Counts every change of <see cref="KnownSkills"/> (what is kept in the settings follows it).</summary>
    public static int KnownVersion { get; private set; }

    /// <summary>Raised (on the capturing thread) when a list arrived.</summary>
    public static event Action? Changed;

    /// <summary>The game's numbers as they stand (every skill and group it named, in the order of its whole list) and when the last frame came.</summary>
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

    /// <summary>The game's numbers: each sets the time left of its skill (0: ready). A number that ends later than the running cooldown is a use the meter did
    /// not see (or the first number of one); its length is counted from the use when there was one.</summary>
    public static void Receive(List<CooldownEntry> entries, DateTime at)
    {
        int switchedFrom = 0, switchedTo = 0;
        try
        {
            lock (Gate)
            {
                // another character's skills: the numbers of the one before are gone
                int cls = entries.Select(e => ClassOf(e.Id)).FirstOrDefault(c => c != 0);
                if (cls != 0 && cls != _class)
                {
                    switchedFrom = _class;
                    switchedTo = cls;
                    SwitchClass(cls);
                }

                // the whole list: what it does not name is gone from the game's numbers; a short frame changes only its skills
                bool whole = entries.Count >= WholeList;
                if (whole)
                {
                    foreach (int gone in State.Keys.Where(id => entries.All(e => e.Id != id)).ToList())
                    {
                        State.Remove(gone);
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
                    Take(e, at);
                }

                _at = at;
            }
        }
        finally
        {
            if (switchedTo != 0)
            {
                ClassChanged?.Invoke(switchedFrom, switchedTo);
            }
        }

        Changed?.Invoke();
    }

    private static void Take(CooldownEntry e, DateTime at)
    {
        bool firstSinceUse = Framed.Add(e.Id);
        if (e.Remaining <= 0)
        {
            Runs.Remove(e.Id);
            return;
        }

        double left = e.Remaining / 1000.0;
        DateTime ends = at.AddSeconds(left);

        // the first number since a use shortly before: the cooldown counts from that use
        bool fromUse = firstSinceUse && LastUse.TryGetValue(e.Id, out DateTime use) && use <= at && (at - use) <= FromUseWithin;
        double sinceUse = fromUse ? (at - LastUse[e.Id]).TotalSeconds : 0;
        if (left > NoCooldownSeconds)
        {
            HasCooldown(e.Id);
            if (fromUse)
            {
                FirstTotals[e.Id] = sinceUse + left;
            }
        }

        if (Runs.TryGetValue(e.Id, out Run? run) && run.Ends > at && ends <= run.Ends + SameRun)
        {
            // the same cooldown: a second or two taken off, or the game's exact time for one a use started
            run.Ends = ends;
            run.FromGame = true;
            run.Length = Math.Max(run.Length, left);
            return;
        }

        // a cooldown the meter had not running (a use it did not see, the list at login) or ending later than it thought: counted from the use when it came
        // shortly before, else from now
        Runs[e.Id] = new Run { Ends = ends, Length = sinceUse + left, FromGame = true };
    }

    /// <summary>The skill has a cooldown: listed from now on, and no longer counted as one without.</summary>
    private static void HasCooldown(int id)
    {
        if (id < FirstSkillId)
        {
            return;
        }

        Timed.Add(id);
        if (Learned.Remove(id))
        {
            LearnedVersion++;
        }

        if (!Known.Contains(id) && NotWorth(id) is null)
        {
            Known.Add(id);
            KnownVersion++;
        }
    }

    // ---------- the own uses ----------

    /// <summary>
    /// The own character cast a skill (its cast frame): the first cast frame of a use starts the skill's cooldown with its length; the frames of the skill's
    /// own effects that follow (ids ending in 7 or 8: Warding Strike 12350150 is the use, 12350057 its effect) and those within a second are the same use.
    /// The time since the last use joins the skill's gaps when the game sent no number for it in between.
    /// </summary>
    public static void NoteOwnCast(int skillId, DateTime at)
    {
        if (skillId < FirstSkillId || skillId % 10 is 7 or 8)
        {
            return;
        }

        int id = BaseOf(skillId);
        int variant = id == skillId ? id : skillId / 10 * 10;
        int switchedFrom = 0, switchedTo = 0;
        try
        {
            lock (Gate)
            {
                if (ClassOf(id) is int own and not 0 && own != _class)
                {
                    // Another class's skill in the own cast frames is mostly a party member's buff landing on the character (a Chanter's 18160032 between a
                    // Templar's skills, a Cleric's 17390003 on a Chanter): switching on it dropped every cooldown at each buff (7 November 2026, a tester:
                    // "insta resets the cd on every buff"). Only two different skills of that class used (the id's last digit 0, as the own uses are) with
                    // none of the class before in between make it the character's class (one of the start, or picked, was another character's).
                    if (!OtherClassUsed(own, id, skillId, at))
                    {
                        return;
                    }

                    switchedFrom = _class;
                    switchedTo = own;
                    SwitchClass(own);
                }
                else if (ClassOf(id) == _class)
                {
                    _otherUses.Clear();
                }

                // A hit or tick of a use, not a use (7 November 2026, a tester's Spiritmaster: Flame Blessing showed 1.1 s the whole time the blessing was
                // on, another buff 3 s more than the game): the blessing's flame about every second (16370001 after the use 16370030), a channel's hit every
                // 0.1 s (Bittercold Wind 15280142), an aura's or a mantra's pulse - ids not ending in 0 - while the skill's cooldown runs or right after
                // another frame of it. Each restarted the cooldown and was learnt as a gap between uses.
                if (skillId % 10 != 0 && IsTick(id, at))
                {
                    LastTick[id] = at;
                    return;
                }

                LastTick[id] = at;
                if (LastCastFrame.TryGetValue(id, out DateTime frame) && at >= frame && at - frame <= SameUse)
                {
                    LastCastFrame[id] = at;
                    return;
                }

                LastCastFrame[id] = at;
                bool sameVariant = !Variants.TryGetValue(id, out int before) || before == variant;
                if (LastUse.TryGetValue(id, out DateTime last) && at > last && !Framed.Contains(id) && sameVariant)
                {
                    AddGap(variant, id, (at - last).TotalSeconds);
                }

                Variants[id] = variant;
                LastUse[id] = at;
                Framed.Remove(id);
                if (LengthOf(id) is double length && length > NoCooldownSeconds && NotWorth(id) is null)
                {
                    Runs[id] = new Run { Ends = at.AddSeconds(length), Length = length, FromGame = false };
                    HasCooldownKnown(id);
                }
                else
                {
                    Runs.Remove(id);
                }
            }
        }
        finally
        {
            if (switchedTo != 0)
            {
                ClassChanged?.Invoke(switchedFrom, switchedTo);
            }
        }
    }

    // Under the lock: whether a frame of skill id (its last digit not 0) is a hit or tick of a use: the cooldown has more than a little left (a quarter of
    // it, at most 3 s: a cooldown shortened by gear lets the next use come a bit early), or a frame of the skill came just before (a stream of ticks).
    private static bool IsTick(int id, DateTime at)
    {
        if (Runs.TryGetValue(id, out Run? run) && (run.Ends - at).TotalSeconds > Math.Min(3, 0.25 * run.Length))
        {
            return true;
        }

        double quiet = Math.Min(3, 0.5 * (LengthOf(id) ?? 6));
        return LastTick.TryGetValue(id, out DateTime tick) && at >= tick && (at - tick).TotalSeconds < quiet;
    }

    /// <summary>The uses of another class's skills since the last of the class in use (class, base ids, the first's time).</summary>
    private static readonly HashSet<int> _otherUses = new();
    private static int _otherClass;
    private static DateTime _otherSince;

    /// <summary>The span two uses of another class's skills must fall in to make it the character's class.</summary>
    private static readonly TimeSpan OtherClassWithin = TimeSpan.FromSeconds(30);

    // Under the lock: true when this use makes the other class the character's (no class yet: one use of a skill of its own; else two different ones).
    private static bool OtherClassUsed(int cls, int id, int skillId, DateTime at)
    {
        if (skillId % 10 != 0)
        {
            return false; // an effect, not a use (a buff of another player's class comes with 1, 2 or 3 there)
        }

        if (_class == 0)
        {
            return true;
        }

        if (_otherClass != cls || at < _otherSince || at - _otherSince > OtherClassWithin)
        {
            _otherUses.Clear();
            _otherClass = cls;
            _otherSince = at;
        }

        _otherUses.Add(id);
        if (_otherUses.Count < 2)
        {
            return false;
        }

        _otherUses.Clear();
        return true;
    }

    private static void HasCooldownKnown(int id)
    {
        if (!Known.Contains(id))
        {
            Known.Add(id);
            KnownVersion++;
        }
    }

    private static void AddGap(int variant, int id, double seconds)
    {
        if (seconds < 0.3 || seconds > 600)
        {
            return;
        }

        if (!Gaps.TryGetValue(variant, out List<double>? gaps))
        {
            Gaps[variant] = gaps = new List<double>();
        }

        gaps.Add(Math.Round(seconds, 2));
        if (gaps.Count > KeptGaps)
        {
            gaps.RemoveAt(0);
        }

        GapsVersion++;
        LearnNoCooldown(id, gaps);
    }

    /// <summary>
    /// The length of a skill's cooldown on this character in seconds (by the variant of its last use), null while not known: the tenth shortest of its last
    /// gaps of more than 2 s (at least four), at most the table's; else the client's table; else the game's first number after a use; else the shortest of two
    /// or three gaps.
    /// </summary>
    public static double? Length(int skillId)
    {
        lock (Gate)
        {
            return LengthOf(BaseOf(skillId));
        }
    }

    private static double? LengthOf(int id)
    {
        int variant = Variants.TryGetValue(id, out int v) ? v : id;
        double? table = TableOf(variant, id);
        List<double> gaps = Gaps.TryGetValue(variant, out List<double>? all) ? all.Where(g => g > NoCooldownSeconds).OrderBy(g => g).ToList() : new List<double>();
        if (gaps.Count >= GapsForLength && table is not (<= NoCooldownSeconds))
        {
            double learned = gaps[(int)(0.1 * gaps.Count)];

            // under half the table's is no shorter cooldown but the gaps between a use's ticks, learnt before they were told apart (7 November 2026)
            return table is double most ? learned < 0.5 * most ? most : Math.Min(learned, most) : learned;
        }

        if (table is double length)
        {
            return length;
        }

        if (FirstTotals.TryGetValue(id, out double total))
        {
            return total;
        }

        return gaps.Count >= 2 ? gaps[0] : null;
    }

    // ---------- what is shown ----------

    /// <summary>What to show of a skill's cooldown at <paramref name="now"/>: ready, or running with the seconds left and the part still to go.</summary>
    public static CooldownView View(int id, DateTime now)
    {
        lock (Gate)
        {
            if (!Runs.TryGetValue(id, out Run? run))
            {
                return CooldownView.Ready;
            }

            double left = (run.Ends - now).TotalSeconds;
            if (left <= 0 || run.Length <= NoCooldownSeconds)
            {
                return CooldownView.Ready;
            }

            return new CooldownView(true, left, run.Length > 0 ? Math.Clamp(left / run.Length, 0, 1) : 0);
        }
    }

    /// <summary>
    /// The skills to show, in their order: with the skill bar known (<paramref name="bar"/>, its base ids in the order of its slots) the skills on it, then any
    /// other of the character's skills used this session with a cooldown; without it the skills known to have a cooldown, then the others the game named.
    /// </summary>
    public static IReadOnlyList<int> Skills(IReadOnlyList<int>? bar = null)
    {
        lock (Gate)
        {
            int cls = _class != 0 ? _class : bar?.Select(ClassOf).FirstOrDefault(c => c != 0) ?? 0;
            if (cls == 0)
            {
                return Array.Empty<int>();
            }

            bool Mine(int id) => ClassOf(id) == cls;
            var list = new List<int>();
            if (bar is { Count: > 0 })
            {
                if (_barSeen is null || !_barSeen.SequenceEqual(bar))
                {
                    if (_barSeen is not null)
                    {
                        DateTime latest = LastUse.Count > 0 ? LastUse.Values.Max() : DateTime.MinValue;
                        foreach (int gone in _barSeen.Where(id => !bar.Contains(id)))
                        {
                            LeftBar[gone] = latest;
                        }
                    }

                    foreach (int id in bar)
                    {
                        LeftBar.Remove(id);
                    }

                    _barSeen = bar.ToList();
                }

                list.AddRange(bar.Where(id => Mine(id) && !list.Contains(id)));
                list.AddRange(LastUse.Keys.Where(id => Mine(id) && !list.Contains(id) && Known.Contains(id) && !(LeftBar.TryGetValue(id, out DateTime left) && LastUse[id] <= left))
                    .OrderBy(id => LastUse[id]));
                return list;
            }

            list.AddRange(Known.Where(id => Mine(id) || (ClassOf(id) == 0 && State.ContainsKey(id))));
            list.AddRange(State.Where(kv => kv.Key >= FirstSkillId && !list.Contains(kv.Key) && (Mine(kv.Key) || ClassOf(kv.Key) == 0))
                .OrderBy(kv => kv.Value.Order).Select(kv => kv.Key));
            return list;
        }
    }

    /// <summary>The class of the skills of the moment (its two digits: 11 Gladiator, 12 Templar ...), 0 while none is known.</summary>
    public static int CurrentClass(IReadOnlyList<int>? bar = null)
    {
        lock (Gate)
        {
            return _class != 0 ? _class : bar?.Select(ClassOf).FirstOrDefault(c => c != 0) ?? 0;
        }
    }

    /// <summary>
    /// Every skill of a class the client gives a cooldown (base ids, in id order): what the Skill Assist offers beyond the skill bar, to be ticked onto the
    /// screen (5 November 2026). The basic attacks, fillers and chain skills of the lists are left out.
    /// </summary>
    public static IReadOnlyList<int> ClassSkills(int cls)
    {
        if (cls == 0)
        {
            return Array.Empty<int>();
        }

        lock (Gate)
        {
            var (noCooldown, chain, lengths) = _lists ??= LoadLists();
            return lengths.Where(kv => kv.Key % 10000 == 0 && ClassOf(kv.Key) == cls && kv.Value > NoCooldownSeconds && !noCooldown.Contains(kv.Key) && !chain.Contains(kv.Key))
                .Select(kv => kv.Key).OrderBy(id => id).ToList();
        }
    }

    /// <summary>Whether the skill is known to have a cooldown (the game sent more than 2 s, or a use started one; this session or an earlier one).</summary>
    public static bool IsKnown(int id)
    {
        lock (Gate)
        {
            return Known.Contains(id);
        }
    }

    // ---------- what the settings keep ----------

    /// <summary>The skills known to have a cooldown, for the settings.</summary>
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

    /// <summary>The learned gaps per skill variant, for the settings.</summary>
    public static Dictionary<int, List<double>> LearnedGaps()
    {
        lock (Gate)
        {
            return Gaps.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
        }
    }

    /// <summary>The gaps the settings kept from earlier sessions (once, at start).</summary>
    public static void SeedGaps(IReadOnlyDictionary<int, List<double>> gaps)
    {
        lock (Gate)
        {
            foreach ((int id, List<double> list) in gaps)
            {
                if (id >= FirstSkillId && !Gaps.ContainsKey(id) && list is { Count: > 0 })
                {
                    Gaps[id] = list.Where(g => g is >= 0.3 and <= 600).TakeLast(KeptGaps).ToList();
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
            LastUse.Clear();
            LastCastFrame.Clear();
            LastTick.Clear();
            _barSeen = null;
            LeftBar.Clear();
            _otherUses.Clear();
            _otherClass = 0;
            Framed.Clear();
            Variants.Clear();
            Gaps.Clear();
            FirstTotals.Clear();
            GapsVersion++;
            Known.Clear();
            KnownVersion++;
            Timed.Clear();
            Learned.Clear();
            LearnedVersion++;
        }
    }

    // ---------- skills with no cooldown to speak of ----------
    // Basic attacks (Rending Blow, Keen Strike, a Templar's Vicious Strike) and fillers (Pummel) have no cooldown: nothing to wait for, and in the
    // assist's list only a row of "Ready". Two class lists say which ones they are (assets/aion2/skills/assist_skill_lists.json); the others are learned
    // from their uses: six gaps at least, the middle one under 2.5 s, and the game never sent the skill with more than 2 s (Judgment, whose cooldown its
    // level 16 removes). A cooldown of more than 2 s from the game, or the gaps growing again, takes a skill off.

    /// <summary>A cooldown of at most this long is none to speak of.</summary>
    public const double NoCooldownSeconds = 2.0;

    private const int GapsForNoCooldown = 6;
    private const double NoCooldownMiddle = 2.5;

    private const int FirstSkillId = 10_000_000;

    /// <summary>The skills the game sent with more than 2 s (this session).</summary>
    private static readonly HashSet<int> Timed = new();

    private static readonly HashSet<int> Learned = new();
    private static (HashSet<int> NoCooldown, HashSet<int> Chain, Dictionary<int, double> Lengths)? _lists;

    /// <summary>Counts every change of the skills learned to have no cooldown (what is kept in the settings follows it).</summary>
    public static int LearnedVersion { get; private set; }

    /// <summary>The id a skill's cooldown is listed under: the skill's base id (its stone and hit digits cleared), a basic attack or dodge as it is.</summary>
    public static int BaseOf(int skillId) => skillId < FirstSkillId || skillId / 10_000 % 100 == 0 ? skillId : skillId / 10_000 * 10_000;

    private static void LearnNoCooldown(int id, List<double> gaps)
    {
        if (gaps.Count < GapsForNoCooldown || Timed.Contains(id))
        {
            return;
        }

        var sorted = gaps.OrderBy(g => g).ToList();
        double middle = sorted[sorted.Count / 2];
        bool none = middle < NoCooldownMiddle && !(TableOf(Variants.TryGetValue(id, out int v) ? v : id, id) is > NoCooldownSeconds);
        if (none && Learned.Add(id))
        {
            LearnedVersion++;
            Runs.Remove(id);
        }
        else if (!none && Learned.Remove(id))
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

    /// <summary>Why a skill is not worth a row: none (null), it has no cooldown (a basic attack, a filler), or it is a chain skill (it follows another).</summary>
    public static NotWorthListing? NotWorth(int skillId)
    {
        int id = BaseOf(skillId);
        var (noCooldown, chain, _) = _lists ??= LoadLists();
        lock (Gate)
        {
            if (chain.Contains(id))
            {
                return NotWorthListing.Chain;
            }

            // the lists, what was learned, or the client's table for the variant in use (Judgment with the specializations 3 and 5 has none) - unless the
            // game sent a cooldown of more than 2 s for it
            bool none = noCooldown.Contains(id) || Learned.Contains(id)
                || (!Timed.Contains(id) && TableOf(Variants.TryGetValue(id, out int v) ? v : id, id) is <= NoCooldownSeconds);
            return none ? NotWorthListing.NoCooldown : null;
        }
    }

    /// <summary>The client's cooldowns (the skill lists' "cooldowns", seconds): a variant's own when it differs from its skill's, else the skill's.</summary>
    private static Dictionary<int, double> Table() => (_lists ??= LoadLists()).Lengths;

    /// <summary>The client's cooldown of a variant (falling back on its skill's), null for a skill the table does not have.</summary>
    private static double? TableOf(int variant, int id) =>
        Table().TryGetValue(variant, out double own) ? own : Table().TryGetValue(id, out double skill) ? skill : null;

    private static (HashSet<int>, HashSet<int>, Dictionary<int, double>) LoadLists()
    {
        var noCooldown = new HashSet<int>();
        var chain = new HashSet<int>();
        var lengths = new Dictionary<int, double>();
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "skills", "assist_skill_lists.json");
            if (File.Exists(path))
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
                noCooldown.UnionWith(doc.RootElement.GetProperty("noCooldown").EnumerateArray().Select(e => e.GetInt32()));
                chain.UnionWith(doc.RootElement.GetProperty("chain").EnumerateArray().Select(e => e.GetInt32()));
                if (doc.RootElement.TryGetProperty("cooldowns", out JsonElement cooldowns))
                {
                    foreach (JsonProperty p in cooldowns.EnumerateObject())
                    {
                        if (int.TryParse(p.Name, out int id) && p.Value.TryGetDouble(out double seconds) && seconds >= 0) // 0: a variant with none
                        {
                            lengths[id] = seconds;
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            // no lists: only what is learned counts
        }

        return (noCooldown, chain, lengths);
    }
}
