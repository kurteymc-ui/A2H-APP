using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2.Protocol;

/// <summary>One world map of the game as the pet map draws it: its tiles (assets/aion2/maps/&lt;key&gt;), the size of a tile and the grid, and
/// how a world coordinate becomes a pixel of the whole map (<c>pixel = offset + scale * world</c>, no flip, no rotation).</summary>
public sealed record Aion2MapInfo(string Key, string Title, string Table, int Tile, int Grid, double Scale, double OffsetX, double OffsetY,
    IReadOnlyList<string>? Tables = null, IReadOnlyList<int>? Zones = null)
{
    /// <summary>An instance (dungeon) map: it belongs to the zone ids of its entrance and not to the open world.</summary>
    public bool Instance => Zones is { Count: > 0 };
}

/// <summary>
/// The world maps (Verteron and Eltnen, Altgard and Morheim, the three levels of the Abyss), the spawn points of the pet monsters on them (pets/spawns.json)
/// and what the pet map needs from both: which map the player is on, and where the monsters of the chosen pets stand.
/// </summary>
public static class Aion2Maps
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "assets", "aion2");
    private static List<Aion2MapInfo>? _maps;
    private static List<Aion2MapInfo>? _instances;
    private static Dictionary<string, Dictionary<int, float[]>>? _spawns;

    public static IReadOnlyList<Aion2MapInfo> All => _maps ??= LoadMaps("maps.json");

    /// <summary>The maps of the instances (maps/instances.json); apart from <see cref="All"/>, which stays the open world.</summary>
    public static IReadOnlyList<Aion2MapInfo> Instances => _instances ??= LoadMaps("instances.json");

    /// <summary>The spawn points of the pet monsters of every map file: {map table: {npc id: x, y, x, y ...}} (instances and event phases included).</summary>
    internal static IReadOnlyDictionary<string, Dictionary<int, float[]>> SpawnTables => Spawns();

    /// <summary>The map a zone id belongs to: the instance maps, the open maps and the legion airships that hover above them (the airship's places are the map's own
    /// coordinates: the spawn at the middle of the Verteron airship fell on the airship's arrow of the game's Verteron map). Null for every other zone.</summary>
    public static Aion2MapInfo? OfZone(int zoneId)
    {
        if (Instances.FirstOrDefault(m => m.Zones!.Contains(zoneId)) is { } instance)
        {
            return instance;
        }

        // the open maps by their map id (the zone frame names them by it): Verteron and Eltnen (World_L_A, _B), Altgard and Morheim (World_D_A, _B), each
        // with the legion airship above it, and the three levels of the Abyss (Abyss_Reshanta_A, _C, _D: "Chaotic Lower / Middle / Upper Reshanta")
        string? key = zoneId switch
        {
            1010 or 902 => "verteron",
            1110 or 901 => "altgard",
            1011 or 904 => "eltnen",
            1111 or 905 => "morheim",
            20 => "abyss",
            22 => "reshanta-middle",
            23 => "reshanta-upper",
            _ => null,
        };
        return key is null ? null : All.FirstOrDefault(m => m.Key == key);
    }

    /// <summary>Open maps of the game the meter has no map of (map ids): Ishalgen and Poeta. There the map shows nothing rather than the map the player
    /// came from. (Middle and Upper Reshanta, Eltnen and Morheim have their maps since 3 November 2026.)</summary>
    private static readonly HashSet<int> NoMapZones = new() { 1100, 110001, 100001, 100002 };

    /// <summary>How many of the monsters around must stand at spawn points of their kind on another map before that map wins over the zone frame's.</summary>
    public const int StrongVotes = 3;

    /// <summary>
    /// The map to show: <paramref name="keep"/> true when nothing tells (the map shown stays). An instance with a map of its own shows it. Otherwise the pet
    /// monsters around vote (each one at a spawn point of its kind on a map); when at least <see cref="StrongVotes"/> of them say another open map than the
    /// zone frame and more than twice as many as say the zone's map, the monsters win - the zone frame does not always come (the Abyss, an invasion of the
    /// other side through a rift) and the player is where its monsters are. A zone frame of an open map the meter has no map of shows none.
    /// </summary>
    public static Aion2MapInfo? Choose(int zoneId, bool instance, IReadOnlyList<(int NpcId, float X, float Y)> mobs, out bool keep)
    {
        keep = false;
        Aion2MapInfo? zoneMap = OfZone(zoneId);
        if (zoneMap is { Instance: true })
        {
            return zoneMap;
        }

        if (NoMapZones.Contains(zoneId))
        {
            return null;
        }

        Dictionary<string, int> votes = Votes(mobs);
        (string? bestKey, int best) = votes.OrderByDescending(kv => kv.Value).Select(kv => ((string?)kv.Key, kv.Value)).FirstOrDefault();
        int zoneVotes = zoneMap is null ? 0 : votes.GetValueOrDefault(zoneMap.Key);
        if (bestKey is not null && bestKey != zoneMap?.Key && best >= StrongVotes && best > 2 * zoneVotes)
        {
            return All.First(m => m.Key == bestKey);
        }

        if (zoneMap is not null || instance)
        {
            return zoneMap; // an instance without a map of its own: none (the open map must not stay behind it)
        }

        keep = bestKey is null;
        return bestKey is null ? null : All.First(m => m.Key == bestKey);
    }

    public static string TileFolder(Aion2MapInfo map) => Path.Combine(Folder, "maps", map.Key);

    public static (double X, double Y) ToNative(Aion2MapInfo map, double worldX, double worldY) =>
        (map.OffsetX + map.Scale * worldX, map.OffsetY + map.Scale * worldY);

    /// <summary>
    /// The map the player is on, from the pet monsters announced around him: each monster that stands (within 40 m) at a spawn point of its
    /// species on a map votes for that map. Null when none of the monsters seen is a pet monster (then the caller keeps what it knew).
    /// </summary>
    public static Aion2MapInfo? Detect(IReadOnlyList<(int NpcId, float X, float Y)> mobs)
    {
        string? best = Votes(mobs).OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault();
        return best is null ? null : All.First(m => m.Key == best);
    }

    /// <summary>Per open map, how many of the monsters stand (within 40 m) at a spawn point of their kind on it.</summary>
    public static Dictionary<string, int> Votes(IReadOnlyList<(int NpcId, float X, float Y)> mobs)
    {
        var spawns = Spawns();
        var votes = new Dictionary<string, int>();
        foreach ((int npc, float x, float y) in mobs)
        {
            foreach (Aion2MapInfo map in All)
            {
                if (spawns.TryGetValue(map.Table, out var table) && table.TryGetValue(npc, out float[]? points) && Near(points, x, y, 4000f))
                {
                    votes[map.Key] = votes.GetValueOrDefault(map.Key) + 1;
                }
            }
        }

        return votes;
    }

    private static bool Near(float[] xy, float x, float y, float limit)
    {
        float limit2 = limit * limit;
        for (int i = 0; i + 1 < xy.Length; i += 2)
        {
            float dx = xy[i] - x, dy = xy[i + 1] - y;
            if (dx * dx + dy * dy <= limit2)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Every spawn point on a map of a monster that drops one of the pets (the pet it belongs to with it).</summary>
    public static IReadOnlyList<(int PetId, float X, float Y)> PointsOf(Aion2MapInfo map, ISet<int> pets)
    {
        var result = new List<(int, float, float)>();
        if (pets.Count == 0)
        {
            return result;
        }

        // an instance has one spawn table per difficulty: the same places, so each place is drawn once
        var seen = new HashSet<(int, float, float)>();
        foreach (string tableName in map.Tables is { Count: > 0 } ? map.Tables : new[] { map.Table })
        {
            if (!Spawns().TryGetValue(tableName, out var table))
            {
                continue;
            }

            foreach ((int npc, float[] points) in table)
            {
                foreach (int pet in Aion2Pets.PetsOfNpc(npc))
                {
                    if (pets.Contains(pet))
                    {
                        for (int i = 0; i + 1 < points.Length; i += 2)
                        {
                            if (seen.Add((pet, points[i], points[i + 1])))
                            {
                                result.Add((pet, points[i], points[i + 1]));
                            }
                        }
                    }
                }
            }
        }

        // the open maps: the spawn points of the website's Pet Soul Map Atlas that the meter's own spawn table lacks (about 15 % more: the Ellun River
        // Kailin, the Search Party Tayga ...), so every spot the pet route leads to has its dots
        if (Extra().TryGetValue(map.Key, out var extra))
        {
            foreach ((int pet, float[] points) in extra)
            {
                if (!pets.Contains(pet))
                {
                    continue;
                }

                for (int i = 0; i + 1 < points.Length; i += 2)
                {
                    if (seen.Add((pet, points[i], points[i + 1])))
                    {
                        result.Add((pet, points[i], points[i + 1]));
                    }
                }
            }
        }

        return result;
    }

    private static Dictionary<string, Dictionary<int, float[]>>? _extra;

    /// <summary>pets/spawns-extra.json: {map key: {pet id: x, y, x, y ...}} in world units.</summary>
    private static Dictionary<string, Dictionary<int, float[]>> Extra()
    {
        if (_extra is not null)
        {
            return _extra;
        }

        var result = new Dictionary<string, Dictionary<int, float[]>>();
        try
        {
            string path = Path.Combine(Folder, "pets", "spawns-extra.json");
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var map in doc.RootElement.GetProperty("maps").EnumerateObject())
                {
                    var table = new Dictionary<int, float[]>();
                    foreach (var pet in map.Value.EnumerateObject())
                    {
                        table[int.Parse(pet.Name)] = pet.Value.EnumerateArray().Select(v => (float)v.GetDouble()).ToArray();
                    }

                    result[map.Name] = table;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            // without it the map shows the client's table alone
        }

        return _extra = result;
    }

    private static List<Aion2MapInfo> LoadMaps(string file)
    {
        var list = new List<Aion2MapInfo>();
        try
        {
            string path = Path.Combine(Folder, "maps", file);
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var m in doc.RootElement.GetProperty("maps").EnumerateObject())
                {
                    var v = m.Value;
                    list.Add(new Aion2MapInfo(m.Name, v.GetProperty("title").GetString() ?? m.Name, v.GetProperty("table").GetString() ?? "",
                        v.GetProperty("tile").GetInt32(), v.GetProperty("grid").GetInt32(), v.GetProperty("scale").GetDouble(),
                        v.GetProperty("offsetX").GetDouble(), v.GetProperty("offsetY").GetDouble(),
                        v.TryGetProperty("tables", out var tables) ? tables.EnumerateArray().Select(t => t.GetString() ?? "").ToList() : null,
                        v.TryGetProperty("zones", out var zones) ? zones.EnumerateArray().Select(z => z.GetInt32()).ToList() : null));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // no maps: the pet map stays off
        }

        return list;
    }

    private static Dictionary<string, Dictionary<int, float[]>> Spawns()
    {
        if (_spawns is not null)
        {
            return _spawns;
        }

        var result = new Dictionary<string, Dictionary<int, float[]>>();
        try
        {
            string path = Path.Combine(Folder, "pets", "spawns.json");
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var map in doc.RootElement.GetProperty("maps").EnumerateObject())
                {
                    var table = new Dictionary<int, float[]>();
                    foreach (var npc in map.Value.EnumerateObject())
                    {
                        var xy = new List<float>();
                        foreach (var point in npc.Value.EnumerateArray())
                        {
                            xy.Add((float)point[0].GetDouble());
                            xy.Add((float)point[1].GetDouble());
                        }

                        table[int.Parse(npc.Name)] = xy.ToArray();
                    }

                    result[map.Name] = table;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            // no spawn points: the pet map stays empty
        }

        return _spawns = result;
    }
}
