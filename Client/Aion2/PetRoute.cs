using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AionDPS.Aion2;

/// <summary>A dense spawn spot of a pet: where (metres, the game's X and Y over 100), how many of its monsters spawn there, and the area's name.</summary>
public sealed record PetRouteSpot(double X, double Z, int Count, string Zone);

/// <summary>A teleport hop before a stop: the Kibelisk to teleport to and how far the stop is from it (metres).</summary>
public sealed record PetRouteTeleport(string Name, double X, double Z, int Distance);

/// <summary>How to get to a stop on a windstream island ("wind"), or back from one ("leave").</summary>
public sealed record PetRouteVia(string Kind, string Island, string? Teleport, string How, string Leave);

/// <summary>One stop of the route: where to stand (metres), how far its spots reach, the area, the island it is on (empty: the mainland), the pets
/// farmed there (meter pet ids, by English name) and the spot of each.</summary>
public sealed record PetRouteStop(int Step, double X, double Z, double Radius, string Zone, string Island, IReadOnlyList<int> Pets,
    IReadOnlyDictionary<int, PetRouteSpot> At, PetRouteTeleport? Teleport = null, PetRouteVia? Via = null);

/// <summary>A route: the map, where it starts (the camp, or where the player is), and the stops in the order to farm them.</summary>
public sealed record PetRoutePlan(string MapKey, double OriginX, double OriginZ, string OriginLabel, bool FromPlayer, IReadOnlyList<PetRouteStop> Stops);

/// <summary>
/// The pet farming route of atreia.app's Pet Soul Map Atlas (guides/pets-route-map.html, buildRoute), the same steps in C#: each pet is on
/// the route once, at one of its dense spawn spots (at least 60 % of the spawns of its densest one); stops go where the most pets have a dense spot
/// within 260 m; the mainland's stops are ordered for the shortest way with teleports counted like 200 m of walking (every first stop tried,
/// then improved by turning parts round), the windstream islands come after the mainland. The data (assets/aion2/pets/route.json) is made from the
/// site's own by Tools/pet-route/make-route.mjs, so the meter plans the route the site shows, from the camp or from where the player stands.
/// </summary>
public static class PetRoute
{
    /// <summary>The genera the route can take (the special pets come from shops and events, never from monsters).</summary>
    public static readonly string[] Genera = { "Cogni", "Natura", "Fera", "Varian" };

    private sealed record Teleport(string Type, string Name, double X, double Z);

    private sealed record Island(string Teleport, string? Kibelisk, string How, string Leave);

    private sealed record MapData(string Site, Dictionary<string, (string Name, double X, double Z)> Starts, List<Teleport> Tps,
        Dictionary<string, Island> Islands, List<(int Id, string Name, List<PetRouteSpot> Spots)> Clusters);

    /// <summary>The route data: the planner's numbers (as the page has them: the stop's reach, what a dense spot is, a teleport's cost in metres, how
    /// many fewest-stop sets are compared and how long the search for them may run, what an island stop costs) and the maps.</summary>
    private sealed record Data(double StopRadius, double GoodSpot, double TeleportCost, int Covers, int Nodes, double IslandStop, Dictionary<int, string> Genus, Dictionary<string, MapData> Maps);

    private static Data? _data;
    private static readonly object Gate = new();
    private static readonly Regex SkyIsland = new("Sky Island", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The route data from a file (the shipped one when null); for the checks.</summary>
    private static Data Load(string? path = null)
    {
        lock (Gate)
        {
            if (_data is not null && path is null)
            {
                return _data;
            }

            string file = path ?? Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "pets", "route.json");
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
            JsonElement root = doc.RootElement;
            var genus = new Dictionary<int, string>();
            foreach (JsonProperty p in root.GetProperty("genus").EnumerateObject())
            {
                if (int.TryParse(p.Name, out int id))
                {
                    genus[id] = p.Value.GetString() ?? "";
                }
            }

            var maps = new Dictionary<string, MapData>();
            foreach (JsonProperty mp in root.GetProperty("maps").EnumerateObject())
            {
                JsonElement m = mp.Value;
                var starts = new Dictionary<string, (string, double, double)>();
                foreach (JsonProperty s in m.GetProperty("starts").EnumerateObject())
                {
                    starts[s.Name] = (s.Value[0].GetString() ?? "", s.Value[1].GetDouble(), s.Value[2].GetDouble());
                }

                var tps = m.GetProperty("tps").EnumerateArray().Select(t => new Teleport(t[0].GetString() ?? "", t[1].GetString() ?? "", t[2].GetDouble(), t[3].GetDouble())).ToList();
                var islands = new Dictionary<string, Island>();
                foreach (JsonProperty i in m.GetProperty("islands").EnumerateObject())
                {
                    JsonElement v = i.Value;
                    islands[i.Name] = new Island(v.GetProperty("tp").GetString() ?? "", v.GetProperty("kibelisk").ValueKind == JsonValueKind.String ? v.GetProperty("kibelisk").GetString() : null,
                        v.GetProperty("how").GetString() ?? "", v.GetProperty("leave").GetString() ?? "");
                }

                var clusters = m.GetProperty("clusters").EnumerateArray().Select(c => (c[0].GetInt32(), c[1].GetString() ?? "",
                    c[2].EnumerateArray().Select(q => new PetRouteSpot(q[0].GetDouble(), q[1].GetDouble(), q[2].GetInt32(), q[3].GetString() ?? "")).ToList())).ToList();
                maps[mp.Name] = new MapData(m.GetProperty("site").GetString() ?? mp.Name, starts, tps, islands, clusters);
            }

            int Int(string name, int fallback) => root.TryGetProperty(name, out JsonElement v) ? v.GetInt32() : fallback;
            double Num(string name, double fallback) => root.TryGetProperty(name, out JsonElement v) ? v.GetDouble() : fallback;
            var data = new Data(root.GetProperty("stopRadius").GetDouble(), root.GetProperty("goodSpot").GetDouble(), root.GetProperty("teleportCost").GetDouble(),
                Int("covers", 24), Int("nodes", 100000), Num("islandStop", 600), genus, maps);
            if (path is null)
            {
                _data = data;
            }

            return data;
        }
    }

    /// <summary>The genus of a pet as the site names it (Cogni, Natura, Fera, Varian, Special), null for a pet it does not list.</summary>
    public static string? GenusOf(int petId) => Load().Genus.TryGetValue(petId, out string? g) ? g : null;

    /// <summary>Whether a map (Aion2MapInfo key: verteron, altgard, abyss) has a route.</summary>
    public static bool Covers(string mapKey) => Load().Maps.ContainsKey(mapKey);

    /// <summary>The area a place is in (metres), as the route names them: the area of the nearest dense spawn spot within 400 m, else empty
    /// (the islands are told apart by it).</summary>
    public static string ZoneNear(string mapKey, double x, double z)
    {
        if (!Load().Maps.TryGetValue(mapKey, out MapData? m))
        {
            return "";
        }

        string zone = "";
        double best = 400;
        foreach (var (_, _, spots) in m.Clusters)
        {
            foreach (PetRouteSpot q in spots)
            {
                double d = Dist((x, z), (q.X, q.Z));
                if (d < best)
                {
                    best = d;
                    zone = q.Zone;
                }
            }
        }

        return zone;
    }

    /// <summary>
    /// The Kibelisk to reach a stop by: the one the route teleports to (and the walk from it, metres), else the one nearest to the stop on its map, or,
    /// for a stop on an island, the island's own (the walk unknown: -1). Null on an island without one (it is left by teleport scroll).
    /// </summary>
    public static (string Name, int Walk)? KibeliskOf(string mapKey, PetRouteStop stop)
    {
        if (stop.Teleport is { } hop)
        {
            return (hop.Name, hop.Distance);
        }

        if (!Load().Maps.TryGetValue(mapKey, out MapData? m))
        {
            return null;
        }

        if (stop.Island.Length > 0)
        {
            return m.Islands.TryGetValue(stop.Island, out Island? isle) && !string.IsNullOrEmpty(isle.Kibelisk) ? (isle.Kibelisk!, -1) : null;
        }

        Teleport? best = m.Tps.Where(t => t.Type == "t").MinBy(t => Dist((stop.X, stop.Z), (t.X, t.Z)));
        return best is null ? null : (best.Name, (int)Math.Floor(Dist((stop.X, stop.Z), (best.X, best.Z)) + 0.5));
    }

    /// <summary>The site's English name of a pet on the route (the order of the pets of a stop follows it).</summary>
    public static string? NameOf(int petId) => Load().Maps.Values.SelectMany(m => m.Clusters).FirstOrDefault(c => c.Id == petId).Name;

    /// <summary>
    /// Plans the route of a map. <paramref name="faction"/>: "Elyos" or "Asmodian" (which camp it starts at). <paramref name="needed"/>: the pets still
    /// to farm. <paramref name="from"/>: where the player stands (metres and the area's name); null starts at the camp. <paramref name="pin"/>: the stop
    /// being farmed, which stays the first while it still has pets to farm.
    /// </summary>
    public static PetRoutePlan? Plan(string mapKey, string? faction, IReadOnlySet<string> genera, Func<int, bool> needed,
        (double X, double Z, string Zone)? from = null, PetRouteStop? pin = null) => PlanWith(Load(), mapKey, faction, genera, needed, from, pin);

    /// <summary>The same with the route data of a file (the checks).</summary>
    internal static PetRoutePlan? PlanFrom(string path, string mapKey, string? faction, IReadOnlySet<string> genera, Func<int, bool> needed, (double X, double Z, string Zone)? from = null) =>
        PlanWith(Load(path), mapKey, faction, genera, needed, from, null);

    /// <summary>A stop the planner chose: where (metres), how far its spots reach, the island (empty: the mainland), its pets (by English name) and the spot of each, the area.</summary>
    private sealed record ChosenStop(double X, double Z, double R, string Island, List<int> Pets, Dictionary<int, PetRouteSpot> At, string Zone);

    /// <summary>
    /// The shortest order found from <paramref name="start"/> (null: anywhere, by teleport), as the page's shortestOrder: from every first stop the nearest
    /// next (or the order given as <paramref name="init"/>), then turning parts round (2-opt) and moving runs of up to three stops elsewhere, until nothing
    /// gets shorter by more than half a metre. A leg is walking, or a teleport (<paramref name="teleportCost"/>) and the walk from the Kibelisk nearest the
    /// stop (<paramref name="kd"/>; infinity: no teleports). Returns the order (indices into <paramref name="pts"/>) and its length in metres.
    /// </summary>
    private static (List<int> Order, double Cost) ShortestOrder(double teleportCost, IReadOnlyList<(double X, double Z)> pts, IReadOnlyList<double> kd, (double X, double Z)? start, IReadOnlyList<int>? init)
    {
        int n = pts.Count;
        if (n == 0)
        {
            return (new List<int>(), 0);
        }

        var leg = new double[n, n];
        var first = new double[n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                leg[i, j] = Math.Min(Dist(pts[i], pts[j]), teleportCost + kd[j]);
            }

            first[i] = Math.Min(start is { } s0 ? Dist(s0, pts[i]) : double.PositiveInfinity, teleportCost + kd[i]);
        }

        double Total(int[] o)
        {
            double c = first[o[0]];
            for (int k = 1; k < n; k++)
            {
                c += leg[o[k - 1], o[k]];
            }

            return c;
        }

        int[]? best = null;
        double bestC = double.PositiveInfinity;
        if (init is not null)
        {
            best = init.ToArray();
            bestC = Total(best);
        }
        else
        {
            for (int f = 0; f < n; f++)
            {
                var o = new int[n];
                var used = new bool[n];
                o[0] = f;
                used[f] = true;
                for (int k = 1; k < n; k++)
                {
                    int p = o[k - 1], bi = -1;
                    for (int i = 0; i < n; i++)
                    {
                        if (!used[i] && (bi < 0 || leg[p, i] < leg[p, bi]))
                        {
                            bi = i;
                        }
                    }

                    o[k] = bi;
                    used[bi] = true;
                }

                double c = Total(o);
                if (c < bestC)
                {
                    bestC = c;
                    best = o;
                }
            }
        }

        var buf = new int[n];
        for (bool imp = true; imp;)
        {
            imp = false;
            for (int a = 0; a < n - 1; a++)
            {
                for (int b = a + 1; b < n; b++)
                {
                    for (int k = 0; k < n; k++)
                    {
                        buf[k] = k < a || k > b ? best![k] : best![a + b - k];
                    }

                    double c = Total(buf);
                    if (c < bestC - 0.5)
                    {
                        Array.Copy(buf, best!, n);
                        bestC = c;
                        imp = true;
                    }
                }
            }

            // a run of best[a..a+len) taken out and put back at position b of what is left (turned round on the second go); the first move that
            // shortens the way is taken, and the search starts over
            bool moved = false;
            for (int len = 1; len <= 3 && !moved; len++)
            {
                for (int a = 0; a + len <= n && !moved; a++)
                {
                    for (int b = 0; b <= n - len && !moved; b++)
                    {
                        if (b == a)
                        {
                            continue;
                        }

                        for (int rev = 0; rev < 2 && !moved; rev++)
                        {
                            int k = 0;
                            for (int r = 0; r <= n - len; r++)
                            {
                                if (r == b)
                                {
                                    for (int t = 0; t < len; t++)
                                    {
                                        buf[k++] = best![rev == 1 ? a + len - 1 - t : a + t];
                                    }
                                }

                                if (r < n - len)
                                {
                                    buf[k++] = best![r < a ? r : r + len];
                                }
                            }

                            double c = Total(buf);
                            if (c < bestC - 0.5)
                            {
                                Array.Copy(buf, best!, n);
                                bestC = c;
                                imp = true;
                                moved = true;
                            }
                        }
                    }
                }
            }
        }

        return (best!.ToList(), bestC);
    }

    /// <summary>
    /// The stops, as the page's chooseStops: the fewest that farm every pet (searched exactly over the stops no other stop outdoes, up to
    /// <see cref="Data.Covers"/> such sets, at most <see cref="Data.Nodes"/> steps), and of those the one with the shortest way on the mainland, after
    /// moving each stop to another spot that farms the same pets where that shortens the way. Each pet is then farmed at the chosen stop where its spot
    /// is densest (then the nearest, then the first).
    /// </summary>
    private static List<ChosenStop> ChooseStops(Data data, Func<string, string> islandOf, List<int> left, Dictionary<int, List<PetRouteSpot>> good,
        List<PetRouteSpot> cands, Func<(double X, double Z), double> kdOf, (double X, double Z)? start0, Dictionary<int, string> nameOf)
    {
        if (left.Count == 0)
        {
            return new List<ChosenStop>();
        }

        int pc = left.Count, words = (pc + 31) >> 5, nc = cands.Count;
        double tp = data.TeleportCost;
        // what each candidate spot would farm: per pet (in the order of `left`) the densest of its dense spots within reach, on the same island
        var cov = new List<List<(int Pet, PetRouteSpot Q)>>(nc);
        var covAt = new List<Dictionary<int, PetRouteSpot>>(nc);
        foreach (PetRouteSpot c in cands)
        {
            var list = new List<(int, PetRouteSpot)>();
            var at = new Dictionary<int, PetRouteSpot>();
            foreach (int n in left)
            {
                (double D, PetRouteSpot Q)? near = null;
                foreach (PetRouteSpot q in good[n])
                {
                    double d = Dist((c.X, c.Z), (q.X, q.Z));
                    if (d <= data.StopRadius && islandOf(q.Zone) == islandOf(c.Zone) && (near is not { } nr || q.Count > nr.Q.Count || (q.Count == nr.Q.Count && d < nr.D)))
                    {
                        near = (d, q);
                    }
                }

                if (near is { } found)
                {
                    list.Add((n, found.Q));
                    at[n] = found.Q;
                }
            }

            cov.Add(list);
            covAt.Add(at);
        }

        var cpos = new (double X, double Z)[nc];
        var ckd = new double[nc];
        var cisl = new string[nc];
        var mask = new uint[nc][];
        for (int i = 0; i < nc; i++)
        {
            cisl[i] = islandOf(cands[i].Zone);
            mask[i] = new uint[words];
            if (cov[i].Count == 0)
            {
                ckd[i] = double.PositiveInfinity;
                continue;
            }

            double x = 0, z = 0;
            foreach (var (_, q) in cov[i])
            {
                x += q.X;
                z += q.Z;
            }

            cpos[i] = (x / cov[i].Count, z / cov[i].Count);
            ckd[i] = kdOf(cpos[i]);
            for (int k = 0; k < pc; k++)
            {
                if (covAt[i].ContainsKey(left[k]))
                {
                    mask[i][k >> 5] |= 1u << (k & 31);
                }
            }
        }

        // the sets of pets a stop can farm: each once, and only those no other set holds and more
        var seen = new HashSet<string>();
        var uniq = new List<int>();
        for (int i = 0; i < nc; i++)
        {
            if (cov[i].Count > 0 && seen.Add(string.Join(",", mask[i])))
            {
                uniq.Add(i);
            }
        }

        bool Within(uint[] a, uint[] b)
        {
            for (int w = 0; w < words; w++)
            {
                if ((a[w] & ~b[w]) != 0)
                {
                    return false;
                }
            }

            return true;
        }

        var sets = uniq.Where(i => !uniq.Any(j => j != i && Within(mask[i], mask[j]) && !Within(mask[j], mask[i]))).ToList();
        var byPet = Enumerable.Range(0, pc).Select(p => sets.Where(i => ((mask[i][p >> 5] >> (p & 31)) & 1) != 0).ToList()).ToList();
        int maxSize = 0;
        foreach (int i in sets)
        {
            int c = 0;
            for (int w = 0; w < words; w++)
            {
                c += System.Numerics.BitOperations.PopCount(mask[i][w]);
            }

            maxSize = Math.Max(maxSize, c);
        }

        int nodes = 0, bestK = int.MaxValue;
        var sols = new List<List<int>>();
        void Search(uint[] unc, List<int> chosen)
        {
            if (++nodes > data.Nodes)
            {
                return;
            }

            int open = 0;
            for (int w = 0; w < words; w++)
            {
                open += System.Numerics.BitOperations.PopCount(unc[w]);
            }

            if (open == 0)
            {
                if (chosen.Count < bestK)
                {
                    bestK = chosen.Count;
                    sols.Clear();
                }

                if (chosen.Count == bestK && sols.Count < data.Covers)
                {
                    sols.Add(chosen.ToList());
                }

                return;
            }

            if (chosen.Count + (open + maxSize - 1) / maxSize > bestK)
            {
                return;
            }

            // the open pet the fewest sets farm (the first such)
            int bp = -1, bn = int.MaxValue;
            for (int p = 0; p < pc; p++)
            {
                if (((unc[p >> 5] >> (p & 31)) & 1) != 0 && byPet[p].Count < bn)
                {
                    bn = byPet[p].Count;
                    bp = p;
                }
            }

            var opts = byPet[bp].Select(i =>
            {
                int c = 0;
                for (int w = 0; w < words; w++)
                {
                    c += System.Numerics.BitOperations.PopCount(mask[i][w] & unc[w]);
                }

                return (I: i, C: c);
            }).OrderByDescending(o => o.C).ThenBy(o => o.I).ToList();
            foreach (var (i, _) in opts)
            {
                var u = new uint[words];
                for (int w = 0; w < words; w++)
                {
                    u[w] = unc[w] & ~mask[i][w];
                }

                chosen.Add(i);
                Search(u, chosen);
                chosen.RemoveAt(chosen.Count - 1);
            }
        }

        var all = new uint[words];
        for (int p = 0; p < pc; p++)
        {
            all[p >> 5] |= 1u << (p & 31);
        }

        Search(all, new List<int>());

        double LegC((double X, double Z)? a, int c) => Math.Min(a is { } p ? Dist(p, cpos[c]) : double.PositiveInfinity, tp + ckd[c]);
        (double Cost, List<ChosenStop> Stops) Refine(List<int> s0)
        {
            var chosen = s0.ToList();
            Dictionary<int, int> Count()
            {
                var k = new Dictionary<int, int>();
                foreach (int s in chosen)
                {
                    foreach (var (n, _) in cov[s])
                    {
                        k[n] = k.GetValueOrDefault(n) + 1;
                    }
                }

                return k;
            }

            var main = chosen.Where(s => cisl[s].Length == 0).ToList();
            var (o, _) = ShortestOrder(tp, main.Select(s => cpos[s]).ToList(), main.Select(s => ckd[s]).ToList(), start0, null);
            var order = o.Select(i => main[i]).ToList();
            for (int pass = 0; pass < 20; pass++)
            {
                bool moved = false;
                var k = Count();
                for (int at = 0; at < order.Count; at++)
                {
                    int s = order[at];
                    var only = cov[s].Where(v => k[v.Pet] == 1).Select(v => v.Pet).ToList();
                    (double X, double Z)? a = at > 0 ? cpos[order[at - 1]] : start0;
                    int nx = at + 1 < order.Count ? order[at + 1] : -1;
                    double baseCost = LegC(a, s) + (nx >= 0 ? LegC(cpos[s], nx) : 0);
                    double bd = -1;
                    int bc = -1;
                    for (int c = 0; c < nc; c++)
                    {
                        if (cisl[c].Length != 0 || cov[c].Count == 0 || chosen.Contains(c) || !only.All(covAt[c].ContainsKey))
                        {
                            continue;
                        }

                        double d = LegC(a, c) + (nx >= 0 ? LegC(cpos[c], nx) : 0) - baseCost;
                        if (d < bd)
                        {
                            bd = d;
                            bc = c;
                        }
                    }

                    if (bc >= 0)
                    {
                        chosen[chosen.IndexOf(s)] = bc;
                        order[at] = bc;
                        moved = true;
                        k = Count();
                    }
                }

                if (!moved)
                {
                    break;
                }

                (o, _) = ShortestOrder(tp, order.Select(s => cpos[s]).ToList(), order.Select(s => ckd[s]).ToList(), start0, Enumerable.Range(0, order.Count).ToList());
                order = o.Select(i => order[i]).ToList();
            }

            // each pet at the chosen stop where its spot is densest (then the nearest, then the first)
            var asg = new Dictionary<int, int>();
            foreach (int n in left)
            {
                int b = -1, bq = 0, bk = 0;
                double bdist = 0;
                for (int kk = 0; kk < chosen.Count; kk++)
                {
                    int s = chosen[kk];
                    if (!covAt[s].TryGetValue(n, out PetRouteSpot? q))
                    {
                        continue;
                    }

                    double d = Dist(cpos[s], (q.X, q.Z));
                    if (b < 0 || q.Count > bq || (q.Count == bq && (d < bdist || (d == bdist && kk < bk))))
                    {
                        b = s;
                        bq = q.Count;
                        bdist = d;
                        bk = kk;
                    }
                }

                asg[n] = b;
            }

            var result = new List<ChosenStop>();
            foreach (int s in chosen)
            {
                var mine = left.Where(n => asg[n] == s).ToList();
                if (mine.Count == 0)
                {
                    continue;
                }

                double x = 0, z = 0;
                foreach (int n in mine)
                {
                    x += covAt[s][n].X;
                    z += covAt[s][n].Z;
                }

                double cx = x / mine.Count, cz = z / mine.Count;
                double r = Math.Max(mine.Max(n => Dist((cx, cz), (covAt[s][n].X, covAt[s][n].Z))), 0) + 50;
                var pets = mine.OrderBy(id => nameOf[id], StringComparer.Ordinal).ToList();
                // the area the stop is in: the one most of its spots are in (the first such in the order of the pets)
                var counts = new List<(string Zone, int Count)>();
                foreach (int id in pets)
                {
                    string zone = covAt[s][id].Zone;
                    int ix = counts.FindIndex(zc => zc.Zone == zone);
                    if (ix < 0)
                    {
                        counts.Add((zone, 1));
                    }
                    else
                    {
                        counts[ix] = (zone, counts[ix].Count + 1);
                    }
                }

                int top = counts.Max(zc => zc.Count);
                string area = counts.First(zc => zc.Count == top).Zone;
                result.Add(new ChosenStop(cx, cz, r, cisl[s], pets, mine.ToDictionary(id => id, id => covAt[s][id]), area.Length > 0 ? area : "Open area"));
            }

            var ml = result.Where(g => g.Island.Length == 0).ToList();
            var (_, cost) = ShortestOrder(tp, ml.Select(g => (g.X, g.Z)).ToList(), ml.Select(g => kdOf((g.X, g.Z))).ToList(), start0, null);
            return (cost + data.IslandStop * (result.Count - ml.Count), result);
        }

        (double Cost, List<ChosenStop> Stops)? pick = null;
        foreach (List<int> sol in sols)
        {
            var r = Refine(sol);
            if (pick is not { } pk || r.Cost < pk.Cost - 1e-9)
            {
                pick = r;
            }
        }

        return pick?.Stops ?? new List<ChosenStop>();
    }

    private static double Dist((double X, double Z) a, (double X, double Z) b) => Hypot(a.X - b.X, a.Z - b.Z);

    /// <summary>
    /// The distance as JavaScript's Math.hypot works it out (V8: scaled by the larger value, the squares added with Kahan's compensation): the plain
    /// square root differs from it in the last digit now and then, and where two orders of the stops cost the same to the last digit (teleports cost
    /// the same 200 m), that decided between them. With this the meter plans exactly the site's route.
    /// </summary>
    private static double Hypot(double x, double y)
    {
        x = Math.Abs(x);
        y = Math.Abs(y);
        double max = Math.Max(x, y);
        if (double.IsInfinity(max))
        {
            return double.PositiveInfinity;
        }

        if (double.IsNaN(x) || double.IsNaN(y))
        {
            return double.NaN;
        }

        if (max == 0)
        {
            return 0;
        }

        double sum = 0, compensation = 0;
        double a = x / max, b = y / max;
        double summand = a * a - compensation;
        double preliminary = sum + summand;
        compensation = (preliminary - sum) - summand;
        sum = preliminary;
        summand = b * b - compensation;
        preliminary = sum + summand;
        sum = preliminary;
        return Math.Sqrt(sum) * max;
    }

    private static double Dist((double X, double Z)? a, (double X, double Z) b) => a is { } p ? Dist(p, b) : double.NaN;

    private static PetRoutePlan? PlanWith(Data data, string mapKey, string? faction, IReadOnlySet<string> genera, Func<int, bool> needed,
        (double X, double Z, string Zone)? from, PetRouteStop? pin)
    {
        if (!data.Maps.TryGetValue(mapKey, out MapData? m))
        {
            return null;
        }

        string side = string.Equals(faction, "Elyos", StringComparison.OrdinalIgnoreCase) ? "elyos" : "asmodian";
        double stopR = data.StopRadius, goodShare = data.GoodSpot, tpCost = data.TeleportCost;
        string IslandOf(string zone) => zone.Length > 0 && m.Islands.ContainsKey(zone) ? zone : "";
        var names = m.Clusters.Where(c => data.Genus.TryGetValue(c.Id, out string? g) && genera.Contains(g) && needed(c.Id)).ToList();
        var nameOf = names.ToDictionary(c => c.Id, c => c.Name);
        var pinned = pin is null ? new List<int>() : pin.Pets.Where(id => nameOf.ContainsKey(id)).ToList();
        var camp = m.Starts.TryGetValue(side, out var own) ? own : m.Starts.Values.First();

        // density first: a pet may only be farmed at one of its dense spots (at least 60 % of the spawns of its densest one); a stop is one of those
        // spots with every other pet that has a dense spot within StopRadius of it
        var best = new Dictionary<int, int>();
        var good = new Dictionary<int, List<PetRouteSpot>>();
        foreach (var (id, _, spots) in names)
        {
            int b = spots.Max(c => c.Count);
            best[id] = b;
            good[id] = spots.Where(c => c.Count >= Math.Max(Math.Min(3, b), b * goodShare)).ToList();
        }

        var cands = new List<PetRouteSpot>();
        foreach (var (id, _, _) in names)
        {
            cands.AddRange(good[id]);
        }

        var left = names.Select(c => c.Id).Where(id => !pinned.Contains(id)).ToList();

        // the Kibelisks one can teleport to (home maps only; not the island or Sky Island ones)
        var islandKibs = m.Islands.Values.Select(i => i.Kibelisk).Where(k => k is not null).ToHashSet();
        var kibs = m.Site == "reshanta" ? new List<Teleport>() : m.Tps.Where(t => t.Type == "t" && !islandKibs.Contains(t.Name) && !SkyIsland.IsMatch(t.Name)).ToList();
        (Teleport? T, double D) NearTp((double X, double Z) p)
        {
            Teleport? b = null;
            double bd = double.PositiveInfinity;
            foreach (Teleport t in kibs)
            {
                double d = Dist(p, (t.X, t.Z));
                if (d < bd)
                {
                    bd = d;
                    b = t;
                }
            }

            return (b, bd);
        }

        bool freeStart = pinned.Count == 0 && from is null && kibs.Count > 0;
        // where the route starts (null: anywhere, by teleport)
        (double X, double Z)? start0 = null;
        if (pinned.Count > 0)
        {
            start0 = (pin!.X, pin.Z);
        }
        else if (from is { } f0)
        {
            start0 = (f0.X, f0.Z);
        }
        else if (!freeStart)
        {
            start0 = (camp.X, camp.Z);
        }

        var stops = ChooseStops(data, IslandOf, left, good, cands, p => NearTp(p).D, start0, nameOf);

        PetRouteStop? pinStop = pinned.Count > 0
            ? new PetRouteStop(1, pin!.X, pin.Z, pin.Radius, pin.Zone, IslandOf(pin.Zone), pinned.OrderBy(id => nameOf[id], StringComparer.Ordinal).ToList(), pinned.ToDictionary(id => id, id => pin.At[id]))
            : null;
        (double X, double Z)? cur = pinStop is not null ? (pinStop.X, pinStop.Z) : from is { } f ? (f.X, f.Z) : (camp.X, camp.Z);
        string startIsl = pinStop is not null ? pinStop.Island : from is { } fz ? IslandOf(fz.Zone) : "";
        (double X, double Z)? TpAt(string name) => m.Tps.FirstOrDefault(t => t.Type == "t" && t.Name == name) is { } t ? (t.X, t.Z) : null;
        int Rank(string a) => a == startIsl ? 0 : a.Length == 0 ? 1 : m.Islands[a].Kibelisk is not null ? 2 : 3;
        var areas = stops.Select(s => s.Island).Distinct().OrderBy(Rank).ToList();
        if (freeStart)
        {
            cur = null;
        }

        (double X, double Z) Sp(int i) => (stops[i].X, stops[i].Z);
        var order = new List<int>();
        foreach (string area in areas)
        {
            var rest = Enumerable.Range(0, stops.Count).Where(i => stops[i].Island == area).ToList();
            if (area.Length == 0)
            {
                // the mainland: the shortest order found (walk, or teleport to the Kibelisk nearest the stop and walk from it)
                var pts = rest.Select(Sp).ToList();
                var (o, _) = ShortestOrder(data.TeleportCost, pts, pts.Select(p => NearTp(p).D).ToList(), cur, null);
                var bestOrder = o.Select(k => rest[k]).ToList();
                order.AddRange(bestOrder);
                if (bestOrder.Count > 0)
                {
                    cur = Sp(bestOrder[^1]);
                }

                continue;
            }

            (double X, double Z)? entry = area.Length > 0 && area != startIsl ? TpAt(m.Islands[area].Teleport) ?? cur : cur;
            var sub = new List<int>();
            (double X, double Z)? c0 = entry;
            while (rest.Count > 0)
            {
                int bi = rest[0];
                foreach (int i in rest)
                {
                    if (Dist(c0, Sp(i)) < Dist(c0, Sp(bi)))
                    {
                        bi = i;
                    }
                }

                sub.Add(bi);
                rest.Remove(bi);
                c0 = Sp(bi);
            }

            (double X, double Z)? P(int k) => k < 0 ? entry : Sp(sub[k]);
            double D2((double X, double Z)? a, (double X, double Z)? b) => a is { } p && b is { } q ? Dist(p, q) : double.NaN;
            bool better = true;
            for (int pass = 0; better && pass < 80; pass++)
            {
                better = false;
                for (int a = -1; a < sub.Count - 2; a++)
                {
                    for (int b = a + 2; b < sub.Count; b++)
                    {
                        var pa = P(a);
                        var pb = P(a + 1);
                        var pc = P(b);
                        (double X, double Z)? pd = b + 1 < sub.Count ? P(b + 1) : null;
                        double now = D2(pa, pb) + (pd is not null ? D2(pc, pd) : 0), turned = D2(pa, pc) + (pd is not null ? D2(pb, pd) : 0);
                        if (turned < now - 0.5)
                        {
                            sub.Reverse(a + 1, b - a);
                            better = true;
                        }
                    }
                }
            }

            order.AddRange(sub);
            if (sub.Count > 0)
            {
                cur = Sp(sub[^1]);
            }
        }

        var groups = new List<PetRouteStop>();
        if (pinStop is not null)
        {
            groups.Add(pinStop);
        }

        foreach (int i in order)
        {
            var s = stops[i];
            groups.Add(new PetRouteStop(groups.Count + 1, s.X, s.Z, s.R, s.Zone, s.Island, s.Pets, s.At));
        }

        double ox, oz;
        string label;
        bool fromPlayer = pinStop is not null || from is not null;
        if (pinStop is not null)
        {
            (ox, oz, label) = (pinStop.X, pinStop.Z, "YOU · " + pinStop.Zone);
        }
        else if (from is { } start)
        {
            (ox, oz, label) = (start.X, start.Z, "YOU · " + start.Zone);
        }
        else
        {
            (ox, oz, label) = (camp.X, camp.Z, "START · " + camp.Name);
        }

        // the windstream islands: how to get there, and back
        string was = startIsl;
        for (int k = 0; k < groups.Count; k++)
        {
            PetRouteStop g = groups[k];
            if (g.Island != was)
            {
                if (g.Island.Length > 0)
                {
                    Island isl = m.Islands[g.Island];
                    groups[k] = g with { Via = new PetRouteVia("wind", g.Island, isl.Teleport, isl.How, isl.Leave) };
                }
                else
                {
                    Island? from2 = m.Islands.TryGetValue(was, out Island? i) ? i : null;
                    string how = from2 is not null && from2.Kibelisk is null
                        ? $"You are on {was}, which has no Kibelisk: use a teleport scroll, then continue here."
                        : "Teleport back to the mainland with a Kibelisk, then continue here.";
                    groups[k] = g with { Via = new PetRouteVia("leave", was, null, how, "") };
                }
            }

            was = g.Island;
        }

        // teleport hops on the mainland: when the nearest Kibelisk and a short walk beat walking from the last stop
        if (kibs.Count > 0)
        {
            (double X, double Z)? prev = freeStart ? null : (ox, oz);
            for (int k = 0; k < groups.Count; k++)
            {
                PetRouteStop g = groups[k];
                if (g.Island.Length == 0 && (g.Via is null || g.Via.Kind == "leave"))
                {
                    var (t, d) = NearTp((g.X, g.Z));
                    double walk = prev is { } p ? Dist(p, (g.X, g.Z)) : double.PositiveInfinity;
                    if (t is not null && tpCost + d < walk)
                    {
                        groups[k] = g with { Teleport = new PetRouteTeleport(t.Name, t.X, t.Z, (int)Math.Floor(d + 0.5)) };
                    }
                }

                prev = (g.X, g.Z);
            }

            if (freeStart && groups.Count > 0 && groups[0].Teleport is { } firstHop)
            {
                (ox, oz, label) = (firstHop.X, firstHop.Z, "START · Teleport: " + firstHop.Name);
            }
        }

        return new PetRoutePlan(mapKey, ox, oz, label, fromPlayer, groups);
    }
}
