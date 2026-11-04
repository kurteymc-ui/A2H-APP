using System.IO;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Combat;

/// <summary>The maps of the pet map: the open maps by their zone, the instance maps by the zone id of the instance, and the world -> map mapping of an instance.</summary>
public static class SelfCheckMaps
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Maps (open world and instances):");
        bool open = Aion2Maps.OfZone(1010)?.Key == "verteron" && Aion2Maps.OfZone(1110)?.Key == "altgard" && Aion2Maps.All.Count == 7
            && Aion2Maps.OfZone(1011)?.Key == "eltnen" && Aion2Maps.OfZone(904)?.Key == "eltnen" && Aion2Maps.OfZone(1111)?.Key == "morheim" && Aion2Maps.OfZone(905)?.Key == "morheim"
            && Aion2Maps.OfZone(20)?.Key == "abyss" && Aion2Maps.OfZone(22)?.Key == "reshanta-middle" && Aion2Maps.OfZone(23)?.Key == "reshanta-upper";
        Console.WriteLine($"  -> open maps by zone (Verteron, Eltnen, Altgard, Morheim, Lower / Middle / Upper Reshanta): {open}; the instances are not part of the open maps: {Aion2Maps.All.All(m => !m.Instance)}");

        // the maps added on 3 November 2026: their tiles are there, and their pet monsters' spawn points lie inside the map
        bool added = true;
        foreach (string key in new[] { "eltnen", "morheim", "reshanta-middle", "reshanta-upper" })
        {
            Aion2MapInfo? map = Aion2Maps.All.FirstOrDefault(m => m.Key == key);
            int tiles = map is null ? 0 : Directory.GetFiles(Aion2Maps.TileFolder(map), "*.jpg").Length;
            var spots = map is null ? new List<(int PetId, float X, float Y)>() : Aion2Maps.PointsOf(map, Aion2Pets.AllPetIds.ToHashSet()).ToList();
            double whole = map is null ? 0 : map.Tile * map.Grid;
            int inside = spots.Count(p => Aion2Maps.ToNative(map!, p.X, p.Y) is var px && px.X > 0 && px.Y > 0 && px.X < whole && px.Y < whole);
            bool ok = map is not null && tiles > 0 && spots.Count > 0 && inside == spots.Count;
            Console.WriteLine($"     {key}: {tiles} tiles, {spots.Count} pet spawn points, {inside} inside the map: {ok}");
            added &= ok;
        }

        open &= added;

        // zone ids of instances: the Ferocious Horn Den (600091..93) and the Dungeon_L_0010 (210010); a zone the files do not know has no map
        var horn = Aion2Maps.OfZone(600092);
        bool zones = horn?.Key == "HornCave" && horn.Instance && Aion2Maps.OfZone(210010)?.Key == "Dungeon_L_0010" && Aion2Maps.OfZone(600999) is null && Aion2Maps.OfZone(210019) is null;
        Console.WriteLine($"  -> instance zones: Horn Den {horn?.Key}, Dungeon_L_0010 {Aion2Maps.OfZone(210010)?.Key}, unknown zone none, an instance without pet monsters (Dungeon_L_0019) has no map: {zones}");

        // the world -> map pixel: a monster of the Horn Den (about (-5225, 10748)) lies on the floor mask of the map (the build's own check found 85 % of its spawn points there)
        bool mapped = false;
        if (horn is not null)
        {
            (double px, double py) = Aion2Maps.ToNative(horn, -5225, 10748);
            double whole = horn.Tile * horn.Grid;
            mapped = px > 0 && py > 0 && px < whole && py < whole;
            Console.WriteLine($"  -> Horn Den world (-5225, 10748) -> pixel ({px:0}, {py:0}) of {whole:0}: inside {mapped}");
        }

        // the pet monsters of an instance: its spawn tables (one per difficulty) give the points, a place of two difficulties once
        bool points = horn is not null && Aion2Maps.PointsOf(horn, Aion2Pets.AllPetIds.ToHashSet()).Count > 0;
        Console.WriteLine($"  -> pet spawn points in the Horn Den: {points}");
        bool chosen = RunChoose();
        bool freeMove = RunFreeMove();
        return open && zones && mapped && points && chosen && freeMove;
    }

    /// <summary>The map shown: the zone frame's (the Abyss by its zone 20), the monsters' when they clearly stand on another map (the zone frame did not
    /// come: the Abyss, an invasion of the other side), none on an open map the meter has no map of, and what was shown when nothing tells.</summary>
    private static bool RunChoose()
    {
        static List<(int, float, float)> MobsOn(string key, int count)
        {
            string table = Aion2Maps.All.First(m => m.Key == key).Table;
            return Aion2Maps.SpawnTables.TryGetValue(table, out var spawns)
                ? spawns.Where(kv => kv.Value.Length >= 2).Take(count).Select(kv => (kv.Key, kv.Value[0], kv.Value[1])).ToList()
                : new List<(int, float, float)>();
        }

        var none = new List<(int, float, float)>();
        bool abyss = Aion2Maps.Choose(20, false, none, out bool k1)?.Key == "abyss" && !k1;
        bool invasion = Aion2Maps.Choose(1110, false, MobsOn("verteron", 5), out _)?.Key == "verteron";
        bool home = Aion2Maps.Choose(1110, false, MobsOn("altgard", 5), out _)?.Key == "altgard" && Aion2Maps.Choose(1110, false, MobsOn("verteron", 2), out _)?.Key == "altgard";
        bool intoAbyss = Aion2Maps.Choose(1110, false, MobsOn("abyss", 4), out _)?.Key == "abyss";
        bool noMap = Aion2Maps.Choose(1100, false, MobsOn("abyss", 4), out bool k2) is null && !k2;
        bool middle = Aion2Maps.Choose(22, false, none, out _)?.Key == "reshanta-middle" && Aion2Maps.Choose(22, false, MobsOn("reshanta-middle", 4), out _)?.Key == "reshanta-middle";
        bool unknown = Aion2Maps.Choose(0, false, none, out bool k3) is null && k3 && Aion2Maps.Choose(0, false, MobsOn("verteron", 1), out _)?.Key == "verteron";
        bool instance = Aion2Maps.Choose(600999, true, none, out bool k4) is null && !k4 && Aion2Maps.Choose(600092, true, MobsOn("verteron", 5), out _)?.Key == "HornCave";
        Console.WriteLine($"  -> map chosen: zone 20 is the Abyss {abyss}; Verteron's monsters around while the zone says Altgard (invasion) {invasion}; Altgard stays with its own or few others {home}; "
            + $"the Abyss's monsters win too {intoAbyss}; Ishalgen (no map) shows none {noMap}; Middle Reshanta its own {middle}; nothing known keeps the map {unknown}; an instance without a map none, with one its own {instance}");
        return abyss && invasion && home && intoAbyss && noMap && middle && unknown && instance;
    }

    /// <summary>
    /// The map's free move (Ui.PetMapPan): a drag keeps the place it took hold of under the cursor, the wheel zooms around the cursor (the place under it stays),
    /// and the view is kept on the land (a land smaller than the view sits in its middle).
    /// </summary>
    private static bool RunFreeMove()
    {
        static bool Near((double X, double Y) p, double x, double y) => Math.Abs(p.X - x) < 0.01 && Math.Abs(p.Y - y) < 0.01;

        // a view 440 pixels square (its middle at 220, 220), 0.01 pixels per world unit, showing the world around (5000, 7000)
        var held = Ui.PetMapPan.WorldAt(320, 120, 5000, 7000, 220, 220, 0.01);
        // taken hold of there and dragged 50 pixels to the left and 20 down: the middle moves the other way, the place stays under the cursor
        var dragged = Ui.PetMapPan.MiddleHolding(held.X, held.Y, 270, 140, 220, 220, 0.01);
        bool drag = Near(held, 15000, -3000) && Near(dragged, 10000, 5000) && Near(Ui.PetMapPan.WorldAt(270, 140, dragged.X, dragged.Y, 220, 220, 0.01), 15000, -3000);

        // zoomed in to twice the scale around the cursor at (320, 120): the place under it stays and the middle moves half way towards it; around the middle it stays
        var zoomed = Ui.PetMapPan.ZoomAround(5000, 7000, 320, 120, 220, 220, 0.01, 0.02);
        bool zoom = Near(zoomed, 10000, 2000) && Near(Ui.PetMapPan.WorldAt(320, 120, zoomed.X, zoomed.Y, 220, 220, 0.02), 15000, -3000)
            && Near(Ui.PetMapPan.ZoomAround(5000, 7000, 220, 220, 220, 220, 0.01, 0.02), 5000, 7000);

        // a land of 100000 square, the view reaching 22000 to each side: dragged far past its corner the view stops on its edge; inside it nothing moves;
        // a land of 30000 (smaller than the view) sits in its middle
        bool clamp = Near(Ui.PetMapPan.Clamp(-50000, 250000, 0, 0, 100000, 100000, 22000, 22000), 22000, 78000)
            && Near(Ui.PetMapPan.Clamp(40000, 60000, 0, 0, 100000, 100000, 22000, 22000), 40000, 60000)
            && Near(Ui.PetMapPan.Clamp(-50000, 3000, 0, 0, 30000, 30000, 22000, 22000), 15000, 15000);
        Console.WriteLine($"  -> free move: a drag holds the place under the cursor {drag}; the wheel zooms around the cursor {zoom}; the view stays on the land {clamp}");
        return drag && zoom && clamp;
    }
}
