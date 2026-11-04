using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using AionDPS.Combat;
using AionDPS.Combat.Sources;

namespace AionDPS.Aion2;

/// <summary>One kind of item picked up on one map in one session: how many, in how many pickups, the first and the last, and in how many of the counted kills
/// it dropped (a kill that dropped two stacks of it counts once: the drop rate is per kill).</summary>
public sealed class LootItem
{
    public int ItemId { get; set; }
    public long Count { get; set; }
    public int Picks { get; set; }
    public DateTime First { get; set; }
    public DateTime Last { get; set; }
    public int Kills { get; set; }
}

/// <summary>One run on a map: from entering it until leaving it (a dungeon entered again is a new run). <see cref="Kills"/>: the monsters killed there that could
/// drop for the player (he hit them, or one of their drops was his); <see cref="MobKills"/> the same by monster (NPC id).</summary>
public sealed class LootMap
{
    public int Zone { get; set; }
    public DateTime Entered { get; set; }

    /// <summary>Which entry of a map this is, in this run of the meter (only the running session looks it up).</summary>
    public int Run { get; set; }
    public List<LootItem> Items { get; set; } = new();
    public int Kills { get; set; }
    public Dictionary<int, int> MobKills { get; set; } = new();
}

/// <summary>One item in a monster's all-time drop table: in how many of its counted kills it dropped, and how many in all.</summary>
public sealed class LootMobItem
{
    public int ItemId { get; set; }
    public int Kills { get; set; }
    public long Count { get; set; }
}

/// <summary>One monster (NPC id) over all sessions: its counted kills, the maps, what it dropped. NPC id 0 holds what came from elsewhere (a chest, a gathering
/// place, a drop whose monster is not known).</summary>
public sealed class LootMob
{
    public int NpcId { get; set; }
    public int Kills { get; set; }
    public List<int> Zones { get; set; } = new();
    public DateTime Last { get; set; }
    public List<LootMobItem> Items { get; set; } = new();
}

/// <summary>One kind of item a Hidden Cube gave.</summary>
public sealed class LootCubeItem
{
    public int ItemId { get; set; }
    public long Count { get; set; }
}

/// <summary>One Hidden Cube the player looted: when (the first pickup from it), on which map, and what he picked up from it.</summary>
public sealed class LootCube
{
    public DateTime At { get; set; }
    public int Zone { get; set; }
    public List<LootCubeItem> Items { get; set; } = new();
}

/// <summary>
/// The loot of one map: every visit to it adds to the same session (2 Nov 2026: one session per map, so Verteron is one entry that comes back when the
/// player does, instead of a new session every half hour and a new run every entry). <see cref="Maps"/> holds that one map. <see cref="ActiveSeconds"/>:
/// the time spent looting there (gaps under half an hour between kills and pickups of one visit), for "per hour"; <see cref="Visits"/>: how often the
/// player came back. <see cref="Id"/> tells sessions apart (two maps can start at the same moment).
/// </summary>
public sealed class LootSession
{
    public string Id { get; set; } = "";
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public double ActiveSeconds { get; set; }
    public int Visits { get; set; }
    public List<LootMap> Maps { get; set; } = new();
}

/// <summary>
/// What the player picked up, by session and map; only on this computer, nothing is sent anywhere. Three frames tell it:
/// 0x1E56 is a drop appearing on the ground (<c>opcode | 01 | monster u32 | x y z f32 | x y z f32 | u32 | loot id u32 at 35 | item id u32 at 39 | count u32 at 43</c>),
/// 0x1F56 is a drop leaving the ground (<c>opcode | 01 | monster u32 | u32 | loot id u32 at 11 | owner u32 | ... | last byte 1 = picked up by its owner, 0 = went otherwise; checked on 1,546 drops of the two own characters, 2026-10-23</c>) and 0x1B56 an item landing in
/// the inventory (<c>opcode | 01 | kind (0 new slot, 1 more of it, 2 gone) | ... | item id u32 at 13</c>). A drop that left the ground counts when the same item landed in the inventory
/// within 1.5 s of it, either way round: another player's pickup, and a drop that vanished, never put anything into the inventory. 0x2136 names the map. The layouts
/// were first seen in StacysView's fork of this meter (2026-10-17, MIT) and checked here on the recordings.
/// The drop rates: a kill counts when the player hit the monster, or when one of its drops was his (a party's kill that dropped for him); a drop's monster is the one
/// the ground frame names, so each pickup goes to the monster (NPC id) that dropped it. Per run of a map, and over all sessions per monster (<see cref="Mobs"/>).
/// A Hidden Cube's loot is a ground drop too, whose "monster" is the cube (checked on the openings of 2026-10-31 and 11-03: the drops come 0.1 s after the open
/// frame 0x3a36 and before the cube's remove frame): it goes to <see cref="Cubes"/>, one entry per cube, and stays out of the maps' sessions and the drop rates.
/// </summary>
public static class Aion2Loot
{
    public const int GroundItemOpcode = 0x1e56;
    public const int GroundGoneOpcode = 0x1f56;
    private const int MaxDrops = 500;
    private const int MaxSessions = 60;
    private const int MaxKillsKept = 3000;
    private const int MaxCubes = 2000;
    private static readonly TimeSpan OwnHitWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan SessionGap = TimeSpan.FromMinutes(30);

    private static readonly object Gate = new();
    private static readonly List<LootSession> Stored = new();
    private static readonly List<LootMob> MobStore = new();
    private static readonly List<LootCube> CubeStore = new();   // newest first
    private static readonly Dictionary<int, LootCube> CubeOfEntity = new();   // the cube entity of this run of the meter -> its entry (its drops are picked up one by one)
    private static readonly Dictionary<uint, (int Item, long Count, int Monster, int Npc, bool Cube)> Drops = new();
    private static readonly Queue<uint> DropOrder = new();
    private static readonly List<(int Item, DateTime At)> Inventory = new();
    private static readonly List<(int Item, long Count, DateTime At, int Monster, int Npc, bool Cube)> Waiting = new();
    private static readonly Dictionary<int, DateTime> OwnHits = new();
    private static readonly Dictionary<int, (DateTime At, HashSet<int> Items)> CountedKills = new();   // killed monster (entity) -> when, and the items its drops gave
    private static readonly Queue<int> KillOrder = new();
    private static int _zone;
    private static DateTime _entered;
    private static int _run;
    private static (int Zone, int Run) _visit = (-1, -1);
    private static LootSession? _active;
    private static DateTime _lastPickup;
    private static bool _persist;
    private static bool _dirty;
    private static DateTime _savedAt = DateTime.MinValue;

    /// <summary>Raised (on the capturing thread) when a pickup was counted or the log was changed.</summary>
    public static event Action? Changed;

    public static string StorePath => Path.Combine(Path.GetDirectoryName(Aion2CharacterStore.DefaultPath)!, "aion2-loot.json");

    public static string MobStorePath => Path.Combine(Path.GetDirectoryName(Aion2CharacterStore.DefaultPath)!, "aion2-loot-mobs.json");

    public static string CubeStorePath => Path.Combine(Path.GetDirectoryName(Aion2CharacterStore.DefaultPath)!, "aion2-loot-cubes.json");

    /// <summary>The Hidden Cubes looted, the newest first (copies).</summary>
    public static IReadOnlyList<LootCube> Cubes
    {
        get
        {
            lock (Gate)
            {
                return CubeStore.Select(c => new LootCube { At = c.At, Zone = c.Zone, Items = c.Items.Select(i => new LootCubeItem { ItemId = i.ItemId, Count = i.Count }).ToList() }).ToList();
            }
        }
    }

    /// <summary>The monsters' all-time drop tables, the most killed first (copies).</summary>
    public static IReadOnlyList<LootMob> Mobs
    {
        get
        {
            lock (Gate)
            {
                return MobStore.OrderByDescending(m => m.Kills).ThenByDescending(m => m.Last)
                    .Select(m => new LootMob { NpcId = m.NpcId, Kills = m.Kills, Zones = m.Zones.ToList(), Last = m.Last,
                        Items = m.Items.Select(i => new LootMobItem { ItemId = i.ItemId, Kills = i.Kills, Count = i.Count }).ToList() }).ToList();
            }
        }
    }

    /// <summary>The map the player is on (0 = not known yet).</summary>
    public static int CurrentZone
    {
        get
        {
            lock (Gate)
            {
                return _zone;
            }
        }
    }

    /// <summary>The sessions, newest first (copies).</summary>
    public static IReadOnlyList<LootSession> Sessions
    {
        get
        {
            lock (Gate)
            {
                return Stored.Select(Copy).ToList();
            }
        }
    }

    /// <summary>The session of the map the player is on (it picks up where the last visit left off), or null before anything was looted there.</summary>
    public static LootSession? Current
    {
        get
        {
            lock (Gate)
            {
                return SessionOf(_zone) is { } session ? Copy(session) : null;
            }
        }
    }

    // Under the lock.
    private static LootSession? SessionOf(int zone) => Stored.FirstOrDefault(s => s.Maps.Count > 0 && s.Maps[0].Zone == zone);

    private static string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// The log as one session per map, the one played last first. Sessions from before 2 Nov 2026 (half an hour each, with a run per entry of a map) are merged
    /// map by map: the items, kills and monsters added up, the visits counted, the time looted estimated from each run's first to last pickup.
    /// </summary>
    internal static List<LootSession> ByMap(IEnumerable<LootSession> sessions)
    {
        var result = new List<LootSession>();
        foreach (var group in sessions.SelectMany(s => s.Maps.Select(m => (Session: s, Map: m))).GroupBy(x => x.Map.Zone))
        {
            var parts = group.ToList();
            if (parts.Count == 1 && parts[0].Session.Maps.Count == 1)
            {
                LootSession one = parts[0].Session;
                if (one.Id.Length == 0)
                {
                    one.Id = NewId();
                }

                one.Visits = Math.Max(1, one.Visits);
                result.Add(one);
                continue;
            }

            var map = new LootMap { Zone = group.Key, Entered = parts.Max(p => p.Map.Entered), Kills = parts.Sum(p => p.Map.Kills) };
            foreach (var p in parts)
            {
                foreach ((int npc, int n) in p.Map.MobKills)
                {
                    map.MobKills[npc] = map.MobKills.GetValueOrDefault(npc) + n;
                }
            }

            map.Items = parts.SelectMany(p => p.Map.Items).GroupBy(i => i.ItemId).Select(i => new LootItem
            {
                ItemId = i.Key, Count = i.Sum(x => x.Count), Picks = i.Sum(x => x.Picks), First = i.Min(x => x.First), Last = i.Max(x => x.Last), Kills = i.Sum(x => x.Kills),
            }).ToList();
            static double Looted(LootSession s, LootMap m) => s.ActiveSeconds > 0 && s.Maps.Count == 1 ? s.ActiveSeconds
                : s.Maps.Count == 1 ? Math.Max(0, (s.End - s.Start).TotalSeconds)
                : m.Items.Count > 0 ? Math.Max(0, (m.Items.Max(i => i.Last) - m.Items.Min(i => i.First)).TotalSeconds) : 0;
            result.Add(new LootSession
            {
                Id = NewId(), Start = parts.Min(p => p.Session.Start), End = parts.Max(p => p.Map.Items.Count > 0 ? p.Map.Items.Max(i => i.Last) : p.Session.End),
                ActiveSeconds = parts.Sum(p => Looted(p.Session, p.Map)), Visits = parts.Sum(p => Math.Max(1, p.Session.Visits)), Maps = new List<LootMap> { map },
            });
        }

        return result.OrderByDescending(s => s.End).ToList();
    }

    private static LootSession Copy(LootSession s) => new()
    {
        Id = s.Id, Start = s.Start, End = s.End, ActiveSeconds = s.ActiveSeconds, Visits = s.Visits,
        Maps = s.Maps.Select(m => new LootMap
        {
            Zone = m.Zone, Entered = m.Entered, Run = m.Run, Kills = m.Kills, MobKills = new Dictionary<int, int>(m.MobKills),
            Items = m.Items.Select(i => new LootItem { ItemId = i.ItemId, Count = i.Count, Picks = i.Picks, First = i.First, Last = i.Last, Kills = i.Kills }).ToList(),
        }).ToList(),
    };

    /// <summary>Starts keeping the log in <see cref="StorePath"/> (the live meter only: a replay or a test keeps nothing). Loads what is there.</summary>
    public static void EnablePersistence()
    {
        lock (Gate)
        {
            _persist = true;
            try
            {
                if (File.Exists(StorePath))
                {
                    var loaded = JsonSerializer.Deserialize<List<LootSession>>(File.ReadAllText(StorePath));
                    Stored.Clear();
                    Stored.AddRange(ByMap(loaded ?? new()).Take(MaxSessions));
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                if (ex is JsonException)
                {
                    AionDPS.AtomicFile.KeepDamaged(StorePath);
                }

                // A broken file: start with an empty log.
            }

            try
            {
                if (File.Exists(MobStorePath))
                {
                    var mobs = JsonSerializer.Deserialize<List<LootMob>>(File.ReadAllText(MobStorePath));
                    MobStore.Clear();
                    MobStore.AddRange(mobs ?? new());
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                if (ex is JsonException)
                {
                    AionDPS.AtomicFile.KeepDamaged(MobStorePath);
                }

                // A broken file: the drop rates start anew.
            }

            try
            {
                if (File.Exists(CubeStorePath))
                {
                    var cubes = JsonSerializer.Deserialize<List<LootCube>>(File.ReadAllText(CubeStorePath));
                    CubeStore.Clear();
                    CubeStore.AddRange((cubes ?? new()).OrderByDescending(c => c.At).Take(MaxCubes));
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                if (ex is JsonException)
                {
                    AionDPS.AtomicFile.KeepDamaged(CubeStorePath);
                }

                // A broken file: the cube log starts anew.
            }
        }
    }

    /// <summary>Deletes the whole log (<paramref name="session"/> null) or one session. The drop rates stay (<see cref="ClearMobs"/>).</summary>
    public static void Clear(LootSession? session = null)
    {
        lock (Gate)
        {
            if (session is null)
            {
                Stored.Clear();
                _active = null;
                CountedKills.Clear();
                KillOrder.Clear();
                OwnHits.Clear();
            }
            else
            {
                LootSession? found = Stored.FirstOrDefault(s => session.Id.Length > 0 ? s.Id == session.Id : s.Start == session.Start);
                if (found is not null)
                {
                    Stored.Remove(found);
                    if (ReferenceEquals(found, _active))
                    {
                        _active = null;
                    }
                }
            }

            _dirty = true;
            Save(force: true);
        }

        Changed?.Invoke();
    }

    /// <summary>Deletes everything the loot log keeps on this PC: the areas (sessions), the all-time drop rates and the Hidden Cube log (the Loot page's Reset).</summary>
    public static void ClearAll()
    {
        lock (Gate)
        {
            Stored.Clear();
            _active = null;
            CountedKills.Clear();
            KillOrder.Clear();
            OwnHits.Clear();
            MobStore.Clear();
            CubeStore.Clear();
            CubeOfEntity.Clear();
            _dirty = true;
            Save(force: true);
        }

        Changed?.Invoke();
    }

    /// <summary>Deletes the Hidden Cube log only.</summary>
    public static void ClearCubes()
    {
        lock (Gate)
        {
            CubeStore.Clear();
            CubeOfEntity.Clear();
            _dirty = true;
            Save(force: true);
        }

        Changed?.Invoke();
    }

    /// <summary>Deletes the all-time drop rates only (the sessions stay).</summary>
    public static void ClearMobs()
    {
        lock (Gate)
        {
            MobStore.Clear();
            _dirty = true;
            Save(force: true);
        }

        Changed?.Invoke();
    }

    /// <summary>The map the player is on now. Coming back to a map continues its session (a new visit of it).</summary>
    public static void NoteZone(int zone)
    {
        lock (Gate)
        {
            if (zone != _zone)
            {
                _zone = zone;
                _entered = DateTime.Now;
                _run++;
            }
        }
    }

    /// <summary>
    /// What the fight tells the drop rates (the live meter's every poll, counted or not): whom the player hit, and who died. A monster's death counts as a kill when the
    /// player hit it in the last ten minutes; one he did not hit counts as soon as one of its drops turns out to be his (see <see cref="Add"/>).
    /// </summary>
    public static void NoteCombat(IReadOnlyList<DamageEvent> damage, IReadOnlyList<KillEvent> kills, Func<int, bool> isLocalPlayer, Func<int, int?> npcOf)
    {
        if (damage.Count == 0 && kills.Count == 0)
        {
            return;
        }

        // what the other side knows is asked before taking the lock (its own lock is never held inside this one)
        var hits = new List<(int Target, DateTime At)>();
        foreach (DamageEvent hit in damage)
        {
            if (!hit.IsHeal && hit.TargetObjectId != hit.SourceObjectId && isLocalPlayer(hit.SourceObjectId))
            {
                hits.Add((hit.TargetObjectId, Local(hit.Timestamp)));
            }
        }

        var deaths = new List<(int Victim, int Npc, DateTime At)>();
        foreach (KillEvent kill in kills)
        {
            if (npcOf(kill.VictimObjectId) is int npc && npc > 0)
            {
                deaths.Add((kill.VictimObjectId, npc, Local(kill.Timestamp)));
            }
        }

        bool counted = false;
        lock (Gate)
        {
            foreach ((int target, DateTime at) in hits)
            {
                OwnHits[target] = at;
            }

            if (OwnHits.Count > 2000)
            {
                DateTime cut = DateTime.Now - OwnHitWindow;
                foreach (int old in OwnHits.Where(kv => kv.Value < cut).Select(kv => kv.Key).ToList())
                {
                    OwnHits.Remove(old);
                }
            }

            foreach ((int victim, int npc, DateTime at) in deaths)
            {
                if (Counted(victim, at) || !OwnHits.TryGetValue(victim, out DateTime hitAt) || at - hitAt > OwnHitWindow)
                {
                    continue; // a monster the player never hit (its drops may still make it count), or counted already
                }

                CountKill(victim, npc, at);
                counted = true;
            }
        }

        if (counted)
        {
            Changed?.Invoke();
        }
    }

    private static DateTime Local(DateTime at) => at.Kind == DateTimeKind.Utc ? at.ToLocalTime() : at;

    // Under the lock: the session of the map the player is on, created at its first kill or pickup and continued at every visit after that. The time between two
    // kills or pickups of one visit counts as time looted while it is under half an hour (a break or a trip to town does not).
    private static LootMap Run(DateTime now)
    {
        LootSession? session = SessionOf(_zone);
        if (session is null)
        {
            session = new LootSession { Id = NewId(), Start = now, End = now, Visits = 1 };
            session.Maps.Add(new LootMap { Zone = _zone, Entered = _entered == default ? now : _entered, Run = _run });
            Stored.Insert(0, session);
            while (Stored.Count > MaxSessions)
            {
                Stored.RemoveAt(Stored.Count - 1);
            }

            _visit = (_zone, _run);
        }
        else
        {
            if (!ReferenceEquals(Stored[0], session))
            {
                Stored.Remove(session);
                Stored.Insert(0, session); // the map played last first
            }

            if (_visit != (_zone, _run))
            {
                _visit = (_zone, _run);
                session.Visits++;
                session.Maps[0].Entered = _entered == default ? now : _entered;
                session.Maps[0].Run = _run;
            }
            else if (ReferenceEquals(session, _active) && now > session.End && now - session.End < SessionGap)
            {
                session.ActiveSeconds += (now - session.End).TotalSeconds;
            }
        }

        _active = session;
        _lastPickup = now;
        if (now > session.End)
        {
            session.End = now;
        }

        return session.Maps[0];
    }

    // Under the lock.
    private static void CountKill(int entity, int npc, DateTime at)
    {
        GuessZone(npc, at);
        LootMap map = Run(at);
        map.Kills++;
        map.MobKills[npc] = map.MobKills.GetValueOrDefault(npc) + 1;
        LootMob mob = Mob(npc);
        mob.Kills++;
        mob.Last = at;
        if (_zone > 0 && !mob.Zones.Contains(_zone))
        {
            mob.Zones.Add(_zone);
        }

        CountedKills[entity] = (at, new HashSet<int>());
        KillOrder.Enqueue(entity);
        while (KillOrder.Count > MaxKillsKept)
        {
            CountedKills.Remove(KillOrder.Dequeue());
        }

        _dirty = true;
        Save(force: false);
    }

    // Under the lock. The meter started on a map (the game names the map only when the player enters one): a monster seen on one map only says which map this
    // is, so its loot goes to that map's session and not to an "unknown map" (2 Nov 2026). Any map: the player's own, the other side's (an invasion), the Abyss.
    private static void GuessZone(int npc, DateTime at)
    {
        if (_zone != 0 || npc <= 0 || MobStore.FirstOrDefault(m => m.NpcId == npc) is not { } mob || mob.Zones.Where(z => z > 0).Distinct().ToList() is not [var zone])
        {
            return;
        }

        _zone = zone;
        _entered = at;
        _run++;
    }

    // Under the lock: the monster's death was counted lately (an entity id the game gives again to a later monster counts anew).
    private static bool Counted(int entity, DateTime at) => CountedKills.TryGetValue(entity, out var kill) && (at - kill.At).Duration() < OwnHitWindow;

    // Under the lock.
    private static LootMob Mob(int npc)
    {
        LootMob? mob = MobStore.FirstOrDefault(m => m.NpcId == npc);
        if (mob is null)
        {
            mob = new LootMob { NpcId = npc };
            MobStore.Add(mob);
        }

        return mob;
    }

    /// <summary>A drop appeared on the ground (0x1E56). <paramref name="npcOf"/> names the monster that dropped it (its NPC id), while it is still known;
    /// <paramref name="isCube"/> tells a Hidden Cube's loot (the drop's "monster" is the cube).</summary>
    public static void NoteGroundItem(ReadOnlySpan<byte> frame, Func<int, int?>? npcOf = null, Func<int, bool>? isCube = null)
    {
        if (frame.Length < 47)
        {
            return;
        }

        int monster = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[3..]));
        bool cube = monster != 0 && isCube?.Invoke(monster) == true;
        int npc = !cube && monster != 0 && npcOf?.Invoke(monster) is int known and > 0 ? known : 0;
        uint lootId = BinaryPrimitives.ReadUInt32LittleEndian(frame[35..]);
        int item = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[39..]));
        long count = BinaryPrimitives.ReadUInt32LittleEndian(frame[43..]);
        if (item <= 0 || count is <= 0 or > 1_000_000_000)
        {
            return;
        }

        lock (Gate)
        {
            if (Drops.TryAdd(lootId, (item, count, monster, npc, cube)))
            {
                DropOrder.Enqueue(lootId);
                while (DropOrder.Count > MaxDrops)
                {
                    Drops.Remove(DropOrder.Dequeue());
                }
            }
        }
    }

    /// <summary>A drop left the ground (0x1F56): picked up (last byte 1) or went otherwise (0). One frame can carry several drops (20 bytes each, behind a 3 byte head).</summary>
    public static void NoteGroundGone(ReadOnlySpan<byte> frame, DateTime at)
    {
        if (frame.Length < 24 || frame[^1] != 1)
        {
            return;
        }

        int sets = Math.Min(frame[2], (frame.Length - 4) / 20);
        bool counted = false;
        for (int i = 0; i < sets; i++)
        {
            uint lootId = BinaryPrimitives.ReadUInt32LittleEndian(frame[(3 + 20 * i + 8)..]);
            lock (Gate)
            {
                if (!Drops.Remove(lootId, out var drop))
                {
                    continue;
                }

                int seen = Inventory.FindLastIndex(x => x.Item == drop.Item && Math.Abs((at - x.At).TotalSeconds) <= Window.TotalSeconds);
                if (seen >= 0)
                {
                    Inventory.RemoveAt(seen);
                    Add(drop.Item, drop.Count, at, drop.Monster, drop.Npc, drop.Cube);
                    counted = true;
                }
                else
                {
                    // The inventory frame may come a moment after the drop's.
                    Waiting.RemoveAll(w => at - w.At > Window);
                    Waiting.Add((drop.Item, drop.Count, at, drop.Monster, drop.Npc, drop.Cube));
                }
            }
        }

        if (counted)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>An item landed in the inventory (0x1B56 of kind 0, a new slot, or 1, more in a slot).</summary>
    public static void NoteInventory(ReadOnlySpan<byte> frame, DateTime at)
    {
        if (frame.Length < 17 || frame[3] > 1)
        {
            return;
        }

        int item = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[13..]));
        bool counted = false;
        lock (Gate)
        {
            Waiting.RemoveAll(w => at - w.At > Window);
            int waiting = Waiting.FindIndex(w => w.Item == item);
            if (waiting >= 0)
            {
                (int waitingItem, long count, DateTime pickedAt, int monster, int npc, bool cube) = Waiting[waiting];
                Waiting.RemoveAt(waiting);
                Add(waitingItem, count, pickedAt, monster, npc, cube);
                counted = true;
            }
            else
            {
                Inventory.RemoveAll(i => at - i.At > Window);
                Inventory.Add((item, at));
            }
        }

        if (counted)
        {
            Changed?.Invoke();
        }
    }

    // Under the lock. A pickup: to the run of the map, and to the drop table of the monster that dropped it (a monster the player did not hit counts as a kill now:
    // its drop was his). The same item from the same kill twice is one kill with it. A Hidden Cube's item goes to the cube log only.
    private static void Add(int item, long count, DateTime frameTime, int monster = 0, int npc = 0, bool cube = false)
    {
        // The session's clock is the computer's: the frames carry the time they were captured at, which is the same clock.
        DateTime now = Local(frameTime);
        if (cube)
        {
            AddCube(monster, item, count, now);
            return;
        }

        GuessZone(npc, now);
        if (npc > 0 && !Counted(monster, now))
        {
            CountKill(monster, npc, now);
        }

        LootMap map = Run(now);
        bool newKill = npc > 0 && CountedKills.TryGetValue(monster, out var kill) && kill.Items.Add(item);
        LootMob mob = Mob(npc);
        mob.Last = now;
        LootMobItem? mobItem = mob.Items.FirstOrDefault(i => i.ItemId == item);
        if (mobItem is null)
        {
            mobItem = new LootMobItem { ItemId = item };
            mob.Items.Add(mobItem);
        }

        mobItem.Count += count;
        if (newKill)
        {
            mobItem.Kills++;
        }

        LootItem? entry = map.Items.FirstOrDefault(i => i.ItemId == item);
        if (entry is null)
        {
            entry = new LootItem { ItemId = item, First = now };
            map.Items.Add(entry);
        }

        entry.Count += count;
        entry.Picks++;
        entry.Last = now;
        if (newKill)
        {
            entry.Kills++;
        }

        _dirty = true;
        Save(force: false);
    }

    // Under the lock. The items of one cube are picked up one by one: the same cube entity within ten minutes is the same cube (the game gives an entity id
    // again only much later).
    private static void AddCube(int entity, int item, long count, DateTime now)
    {
        if (!CubeOfEntity.TryGetValue(entity, out LootCube? cube) || (now - cube.At).Duration() > TimeSpan.FromMinutes(10) || !CubeStore.Contains(cube))
        {
            cube = new LootCube { At = now, Zone = _zone != 0 ? _zone : ZoneOfCube(now) };
            CubeStore.Insert(0, cube);
            while (CubeStore.Count > MaxCubes)
            {
                CubeStore.RemoveAt(CubeStore.Count - 1);
            }

            if (CubeOfEntity.Count > 200)
            {
                CubeOfEntity.Clear();
            }

            CubeOfEntity[entity] = cube;
        }

        LootCubeItem? entry = cube.Items.FirstOrDefault(i => i.ItemId == item);
        if (entry is null)
        {
            entry = new LootCubeItem { ItemId = item };
            cube.Items.Add(entry);
        }

        entry.Count += count;
        _dirty = true;
        Save(force: false);
    }

    // Under the lock: a cube opened before the game named the map takes the map of the last kill or pickup when that was a few minutes ago (0 = not known).
    private static int ZoneOfCube(DateTime now) =>
        Stored.FirstOrDefault() is { Maps: [{ Zone: > 0 } map, ..] } last && (now - last.End).Duration() < TimeSpan.FromMinutes(10) ? map.Zone : 0;

    // Under the lock; at most every 5 s unless forced, so a farming spree does not write the file for every item.
    private static void Save(bool force)
    {
        if (!_persist || !_dirty || !force && DateTime.UtcNow - _savedAt < TimeSpan.FromSeconds(5))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            AionDPS.AtomicFile.WriteAllText(StorePath, JsonSerializer.Serialize(Stored));
            AionDPS.AtomicFile.WriteAllText(MobStorePath, JsonSerializer.Serialize(MobStore));
            AionDPS.AtomicFile.WriteAllText(CubeStorePath, JsonSerializer.Serialize(CubeStore));
            _dirty = false;
            _savedAt = DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Kept in memory; the next save tries again.
        }
    }

    /// <summary>Writes what a pickup within the last 5 s left unsaved (the meter is closing).</summary>
    public static void Flush()
    {
        lock (Gate)
        {
            Save(force: true);
        }
    }
}
