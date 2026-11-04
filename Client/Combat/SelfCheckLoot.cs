using System.Buffers.Binary;
using AionDPS.Aion2;
using AionDPS.Combat.Sources;

namespace AionDPS.Combat;

/// <summary>The loot log: a drop on the ground, the item landing in the inventory and the drop leaving the ground make a pickup, in either order; a drop that vanished and
/// another player's pickup make none. The frames are built to the layouts seen on the recordings of 2026-10-09/10 (see <see cref="Aion2Loot"/>).</summary>
public static class SelfCheckLoot
{
    internal static byte[] Ground(uint lootId, int item, uint count, int monster = 0)
    {
        var f = new byte[67];
        f[0] = 0x1e;
        f[1] = 0x56;
        f[2] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(f.AsSpan(3), monster);
        BinaryPrimitives.WriteUInt32LittleEndian(f.AsSpan(35), lootId);
        BinaryPrimitives.WriteUInt32LittleEndian(f.AsSpan(39), (uint)item);
        BinaryPrimitives.WriteUInt32LittleEndian(f.AsSpan(43), count);
        return f;
    }

    internal static byte[] Gone(uint lootId, byte lastByte)
    {
        var f = new byte[24];
        f[0] = 0x1f;
        f[1] = 0x56;
        f[2] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(f.AsSpan(11), lootId);
        f[^1] = lastByte;
        return f;
    }

    internal static byte[] Inventory(byte kind, int item)
    {
        var f = new byte[43];
        f[0] = 0x1b;
        f[1] = 0x56;
        f[2] = 1;
        f[3] = kind;
        BinaryPrimitives.WriteUInt32LittleEndian(f.AsSpan(13), (uint)item);
        return f;
    }

    /// <summary>Two Hidden Cubes opened in Altgard as on the recording of 2026-11-03 22:24 (seven drops of the cube entity, picked up one by one), a kill beside
    /// them: the cube log holds two cubes with their items, the map's session and the drop tables only the kill's soul.</summary>
    private static bool Cubes(DateTime c0)
    {
        Aion2Loot.Clear();
        Aion2Loot.ClearMobs();
        Aion2Loot.ClearCubes();
        Aion2Loot.NoteZone(1110);
        const int cube1 = 47434, cube2 = 41695, mob = 710, codex = 535400001, crystal = 531900009, silentium = 630840002, soul = 534660160;
        var npcs = new Dictionary<int, int> { [mob] = 2400936 };
        Func<int, int?> npcOf = e => npcs.TryGetValue(e, out int n) ? n : null;
        Func<int, bool> isCube = e => e is cube1 or cube2;
        void Pick(uint id, int item, uint count, int monster, double s)
        {
            Aion2Loot.NoteGroundItem(Ground(id, item, count, monster), npcOf, isCube);
            Aion2Loot.NoteInventory(Inventory(0, item), c0.AddSeconds(s));
            Aion2Loot.NoteGroundGone(Gone(id, 1), c0.AddSeconds(s).AddMilliseconds(100));
        }

        Pick(31246336, codex, 5, cube1, 0);
        Pick(31246338, crystal, 1000, cube1, 0.1);
        Pick(31246340, silentium, 10, cube1, 0.2);
        Pick(31246341, silentium, 1, cube1, 0.3);
        Aion2Loot.NoteCombat(new[] { new DamageEvent(c0.AddSeconds(20), 1, mob, 5000, false) }, new[] { new KillEvent(c0.AddSeconds(21), 1, mob, false) }, e => e == 1, npcOf);
        Pick(31246400, soul, 1, mob, 22);
        Pick(31253373, codex, 5, cube2, 100);
        Pick(31253375, crystal, 1000, cube2, 100.1);

        var log = Aion2Loot.Cubes;
        bool two = log.Count == 2 && log.All(c => c.Zone == 1110) && log[0].At > log[1].At
            && log[1].Items.Count == 3 && log[1].Items.First(i => i.ItemId == silentium).Count == 11 && log[1].Items.First(i => i.ItemId == crystal).Count == 1000
            && log[0].Items.Count == 2 && log[0].Items.Sum(i => i.Count) == 1005;
        var session = Aion2Loot.Sessions.FirstOrDefault(s => s.Maps[0].Zone == 1110);
        bool apart = session is { Maps: [{ Kills: 1, Items: [{ ItemId: soul, Count: 1 }] }] } && Aion2Loot.Mobs.All(m => m.Items.All(i => i.ItemId == soul))
            && Aion2Loot.Mobs.All(m => m.NpcId != 0);
        var lines = Ui.LootStats.Lines(log.SelectMany(c => c.Items.Select(i => (i.ItemId, i.Count, 1, c.At))));
        bool rates = lines.First(l => l.Key == codex).WithKills == 2 && lines.First(l => l.Key == silentium).WithKills == 1 && Ui.LootText.OneInCubes(1, 2).Length > 0;
        Console.WriteLine($"  -> two Hidden Cubes, their drops picked up one by one: two cubes in the cube log with their items ({two}); the map and the drop tables hold only the kill's soul ({apart}); codex in 2 of 2 cubes, silentium in 1 ({rates})");

        // the Loot page's Reset: the areas, the drop rates and the cube log all go at once
        Aion2Loot.ClearAll();
        bool reset = Aion2Loot.Sessions.Count == 0 && Aion2Loot.Mobs.Count == 0 && Aion2Loot.Cubes.Count == 0 && Aion2Loot.Current is null;
        Console.WriteLine($"  -> Reset: no area, no drop rate, no cube left: {reset}");
        return two && apart && rates && reset;
    }

    public static bool Run()
    {
        Console.WriteLine("[selftest] Loot log (ground drop, inventory, pickup):");
        Aion2Loot.Clear();
        Aion2Loot.NoteZone(1010);
        DateTime t = new(2026, 10, 17, 20, 0, 0, DateTimeKind.Local);
        const int soul = 534660001, stone = 513560001;

        // inventory first, then the drop leaves the ground
        Aion2Loot.NoteGroundItem(Ground(1, soul, 1));
        Aion2Loot.NoteInventory(Inventory(0, soul), t);
        Aion2Loot.NoteGroundGone(Gone(1, 1), t.AddMilliseconds(100));
        // the drop leaves the ground first, then the inventory frame
        Aion2Loot.NoteGroundItem(Ground(2, soul, 2));
        Aion2Loot.NoteGroundGone(Gone(2, 1), t.AddSeconds(2));
        Aion2Loot.NoteInventory(Inventory(1, soul), t.AddSeconds(2).AddMilliseconds(80));
        // a drop that vanished, and another player's pickup (no inventory frame): not counted
        Aion2Loot.NoteGroundItem(Ground(3, stone, 1));
        Aion2Loot.NoteGroundGone(Gone(3, 0), t.AddSeconds(4));
        Aion2Loot.NoteGroundItem(Ground(4, stone, 1));
        Aion2Loot.NoteGroundGone(Gone(4, 1), t.AddSeconds(6));
        // the same item in the inventory long after the other's pickup: still not the pickup
        Aion2Loot.NoteInventory(Inventory(0, stone), t.AddSeconds(30));
        // a second map
        Aion2Loot.NoteZone(20);
        Aion2Loot.NoteGroundItem(Ground(5, stone, 3));
        Aion2Loot.NoteInventory(Inventory(0, stone), t.AddSeconds(40));
        Aion2Loot.NoteGroundGone(Gone(5, 1), t.AddSeconds(40).AddMilliseconds(50));

        // one session per map, the map played last first
        var sessions = Aion2Loot.Sessions;
        LootMap? verteron = sessions.FirstOrDefault(s => s.Maps[0].Zone == 1010)?.Maps[0];
        LootMap? abyss = sessions.FirstOrDefault(s => s.Maps[0].Zone == 20)?.Maps[0];
        bool one = sessions.Count == 2 && sessions.All(s => s.Maps.Count == 1 && s.Visits == 1 && s.Id.Length > 0) && sessions[0].Maps[0].Zone == 20 && sessions[0].Id != sessions[1].Id;
        bool souls = verteron?.Items.Count == 1 && verteron.Items[0] is { ItemId: soul, Count: 3, Picks: 2 };
        bool stones = abyss?.Items.Count == 1 && abyss.Items[0] is { ItemId: stone, Count: 3, Picks: 1 };
        Console.WriteLine($"  -> a session for each of the two maps, the last one first: {one}; 2 pickups = 3 souls on the first map, either order of the two frames: {souls}; the stones of the second map only (a vanished drop and another's pickup left out): {stones}");

        // two hours later on the same map: the same session goes on, the break is not time looted
        Aion2Loot.NoteGroundItem(Ground(6, soul, 1));
        Aion2Loot.NoteInventory(Inventory(0, soul), t.AddHours(2));
        Aion2Loot.NoteGroundGone(Gone(6, 1), t.AddHours(2).AddMilliseconds(30));
        var later = Aion2Loot.Sessions;
        bool second = later.Count == 2 && later[0].Maps[0].Zone == 20 && later[0].Maps[0].Items.Count == 2 && later[0].ActiveSeconds < 60
            && later[0].End >= t.AddHours(2) && later[0].Start < t.AddMinutes(1);
        Console.WriteLine($"  -> a pickup two hours later on the same map adds to its session, the break not counted as time looted ({later.FirstOrDefault()?.ActiveSeconds:0} s): {second}");

        // back on the first map: its session comes back (a second visit), no new one
        Aion2Loot.NoteZone(1010);
        Aion2Loot.NoteGroundItem(Ground(7, soul, 1));
        Aion2Loot.NoteInventory(Inventory(0, soul), t.AddHours(2).AddMinutes(5));
        Aion2Loot.NoteGroundGone(Gone(7, 1), t.AddHours(2).AddMinutes(5).AddMilliseconds(30));
        var back = Aion2Loot.Sessions;
        bool again = back.Count == 2 && back[0].Maps[0].Zone == 1010 && back[0].Visits == 2 && back[0].Maps[0].Items is [{ ItemId: soul, Count: 4, Picks: 3 }];
        Console.WriteLine($"  -> back on the first map, its session carries on (4 souls, visit 2) and comes to the top: {again}");

        // the log from before one session per map (a session per half hour, a run per entry of a map): merged map by map
        DateTime o = t.AddDays(-3);
        LootMap Old(int zone, int item, long count, int kills, double at) => new()
        {
            Zone = zone, Kills = kills,
            Items = { new LootItem { ItemId = item, Count = count, Picks = 1, Kills = 1, First = o.AddMinutes(at), Last = o.AddMinutes(at + 10) } },
        };
        var merged = Aion2Loot.ByMap(new[]
        {
            new LootSession { Start = o.AddHours(3), End = o.AddHours(3).AddMinutes(20), Maps = { Old(1010, soul, 2, 6, 180) } },
            new LootSession { Start = o, End = o.AddMinutes(40), Maps = { Old(1010, soul, 1, 4, 0), Old(20, stone, 5, 2, 25), Old(1010, stone, 1, 3, 30) } },
        });
        LootSession? oldVerteron = merged.FirstOrDefault(s => s.Maps[0].Zone == 1010);
        bool legacy = merged.Count == 2 && merged.All(s => s.Maps.Count == 1 && s.Id.Length > 0) && merged[0] == oldVerteron
            && oldVerteron!.Visits == 3 && oldVerteron.Maps[0].Kills == 13 && oldVerteron.Maps[0].Items.Sum(i => i.Count) == 4
            && oldVerteron.Maps[0].Items.FirstOrDefault(i => i.ItemId == soul)?.Count == 3 && oldVerteron.ActiveSeconds is > 0 and < 3600;
        Console.WriteLine($"  -> an old log of 2 sessions (3 runs on the first map) becomes one session per map with the counts added up: {legacy}");
        Aion2Loot.Clear();

        // every map the game names: an Asmodian in Altgard invading Verteron and back, the Abyss's layers - each its own session that comes back
        DateTime f0 = t.AddHours(3);
        uint ground = 20;
        void PickOn(int zone, int minute)
        {
            Aion2Loot.NoteZone(zone);
            Aion2Loot.NoteGroundItem(Ground(ground, stone, 1));
            Aion2Loot.NoteInventory(Inventory(0, stone), f0.AddMinutes(minute));
            Aion2Loot.NoteGroundGone(Gone(ground++, 1), f0.AddMinutes(minute).AddMilliseconds(40));
        }

        PickOn(1110, 0);  // Altgard
        PickOn(1010, 5);  // through the rift into Verteron
        PickOn(1110, 9);  // home again
        PickOn(20, 20);   // the Abyss: Lower Reshanta
        PickOn(22, 30);   // Middle Reshanta
        PickOn(20, 40);   // Lower again
        var regions = Aion2Loot.Sessions;
        int[] zones = { 1110, 1010, 20, 22 };
        bool everyMap = regions.Count == 4 && zones.All(z => regions.Count(s => s.Maps[0].Zone == z) == 1)
            && regions.First(s => s.Maps[0].Zone == 1110).Visits == 2 && regions.First(s => s.Maps[0].Zone == 20).Visits == 2 && regions[0].Maps[0].Zone == 20
            && new[] { 1010, 1011, 1110, 1111, 20, 22, 23 }.All(z => Aion2.Protocol.Aion2Zones.NameOf(z, "en") is not null);
        Console.WriteLine($"  -> Altgard, Verteron by invasion, the Abyss's layers: one session each, coming back carries on ({string.Join(", ", regions.Select(s => Ui.LootText.MapName(s.Maps[0].Zone) + " x" + s.Visits))}): {everyMap}");
        Aion2Loot.Clear();

        // drop rates: the kills the player took part in, each pickup to the monster that dropped it
        Aion2Loot.ClearMobs();
        var npcs = new Dictionary<int, int> { [700] = 2090666, [701] = 2090666, [702] = 2090666, [703] = 2090666, [704] = 2300104, [705] = 2090666 };
        Func<int, int?> npcOf = e => npcs.TryGetValue(e, out int n) ? n : null;
        DateTime k0 = t.AddHours(5);
        void Hit(int e, double s) => Aion2Loot.NoteCombat(new[] { new DamageEvent(k0.AddSeconds(s), 1, e, 100, false) }, Array.Empty<KillEvent>(), id => id == 1, npcOf);
        void Die(int e, double s) => Aion2Loot.NoteCombat(Array.Empty<DamageEvent>(), new[] { new KillEvent(k0.AddSeconds(s), null, e, false) }, id => id == 1, npcOf);
        void Pick(uint id, int item, uint count, int monster, double s)
        {
            Aion2Loot.NoteGroundItem(Ground(id, item, count, monster), npcOf);
            Aion2Loot.NoteInventory(Inventory(0, item), k0.AddSeconds(s));
            Aion2Loot.NoteGroundGone(Gone(id, 1), k0.AddSeconds(s).AddMilliseconds(100));
        }

        Aion2Loot.NoteZone(1010);
        Hit(700, 0);
        Die(700, 1);
        Hit(701, 10);
        Die(701, 11);
        Hit(702, 20);
        Die(702, 21);
        Die(703, 30);                    // killed by others, not hit: not yet
        Die(704, 32);                    // another player's boss, nothing for us: never
        Pick(900, soul, 1, 700, 40);     // two stacks of souls from one kill: one kill with souls
        Pick(901, soul, 1, 700, 45);
        Pick(902, soul, 1, 703, 50);     // a soul from the monster the others killed: its kill counts now
        Die(703, 51);                    // its kill frame again: still once
        LootMap? run = Aion2Loot.Sessions.FirstOrDefault()?.Maps.FirstOrDefault(m => m.Zone == 1010);
        LootItem? souls2 = run?.Items.FirstOrDefault(i => i.ItemId == soul);
        LootMob? kobold = Aion2Loot.Mobs.FirstOrDefault(m => m.NpcId == 2090666);
        bool kills = run is { Kills: 4 } && run.MobKills.GetValueOrDefault(2090666) == 4 && Aion2Loot.Mobs.All(m => m.NpcId != 2300104);
        bool perKill = souls2 is { Kills: 2, Count: 3 } && kobold is { Kills: 4 } && kobold.Items.FirstOrDefault(i => i.ItemId == soul) is { Kills: 2, Count: 3 };
        bool rate = Ui.LootText.RateParts(2, 4) is ("50", "2") && Ui.LootText.RateParts(1, 48) is (_, "48") && Ui.LootText.RateParts(0, 10) is null;
        Console.WriteLine($"  -> kills counted: hit and killed, or one of its drops was ours; another's kill without a drop never ({kills}); souls in 2 of 4 kills, 3 in all ({perKill}); 50% = 1 in 2 ({rate})");

        // leaving the map and coming back: the kills go on in the map's one session
        Aion2Loot.NoteZone(20);
        Aion2Loot.NoteZone(1010);
        Hit(705, 60);
        Die(705, 61);
        var maps = Aion2Loot.Sessions.Where(s => s.Maps[0].Zone == 1010).ToList();
        bool newRun = maps is [{ Visits: 2, Maps: [{ Kills: 5 }] }] && Aion2Loot.Mobs.First(m => m.NpcId == 2090666).Kills == 5;
        Console.WriteLine($"  -> the map entered again goes on in its session: 5 kills over 2 visits, the monster's all-time kills too (5): {newRun}");

        // the meter started on a map (no zone frame yet): a monster known from Verteron says it is Verteron, no "unknown map" session
        Aion2Loot.NoteZone(0);
        npcs[706] = 2090666;
        Hit(706, 70);
        Die(706, 71);
        var guessed = Aion2Loot.Sessions;
        bool guess = Aion2Loot.CurrentZone == 1010 && guessed.All(s => s.Maps[0].Zone != 0) && guessed.Count(s => s.Maps[0].Zone == 1010) == 1
            && guessed.First(s => s.Maps[0].Zone == 1010).Maps[0].Kills == 6;
        Console.WriteLine($"  -> started on a map the game has not named yet, a monster seen only in Verteron puts the loot in Verteron's session: {guess}");
        Aion2Loot.ClearMobs();

        string name = Aion2.Protocol.Aion2ItemNames.NameOf(soul, "en") ?? "";
        string german = Aion2.Protocol.Aion2ItemNames.NameOf(soul, "de") ?? "";
        bool named = name.StartsWith("Soul:") && german.Length > 0 && Aion2.Protocol.Aion2ItemNames.GradeOf(110120003) == 4;
        Console.WriteLine($"  -> an item is named from the client's table in English and German ({name} / {german}), a gear piece has its grade: {named}");

        // every drop has its picture: a soul (its creature), a soulstone, a gold greatsword; gear of one grade is one line
        bool icons = Aion2.Protocol.Aion2Artwork.ItemIconPath(soul) is not null && Aion2.Protocol.Aion2Artwork.ItemIconPath(stone) is not null
            && Aion2.Protocol.Aion2Artwork.ItemIconPath(110120003) is not null;
        var lines = Ui.LootStats.Lines(new[] { (110120003, 1L, 1, t), (210130001, 1L, 1, t.AddSeconds(1)), (soul, 3L, 2, t) });
        bool gear = lines.Count == 2 && lines.Any(l => l.Gear && l.Grade == Ui.LootStats.Gold && l.Count == 2 && l.WithKills == 2 && l.Members.Count == 2);
        Console.WriteLine($"  -> the soul, the soulstone and the greatsword have their icons ({icons}); two gold pieces are one \"Gold gear ×2\" line ({gear})");
        // switched on before the first drop: the overlay still shows the map with a line, it doesn't stay invisible
        var waiting = Ui.LootText.OverlayLines(null, 1010, new Ui.MeterSettings(), sample: false);
        bool shows = waiting.Lines.Count == 0 && waiting.Note is { Length: > 0 } && waiting.Caption.Length > 0;
        Console.WriteLine($"  -> switched on with nothing picked up yet, the overlay shows \"{waiting.Caption}\" and a line ({shows})");
        Aion2Loot.Clear();

        // a Hidden Cube's loot (the drop's "monster" is the cube): to the cube log, one entry per cube, and in no area and no drop table
        bool cubes = Cubes(t.AddHours(8));
        Aion2Loot.Clear();
        return one && souls && stones && second && again && legacy && everyMap && guess && named && kills && perKill && rate && newRun && icons && gear && shows && cubes;
    }
}
