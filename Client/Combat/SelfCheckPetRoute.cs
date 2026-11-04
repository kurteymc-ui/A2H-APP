using System.Text.Json;
using AionDPS.Aion2;

namespace AionDPS.Combat;

/// <summary>
/// The pet route (Aion2/PetRoute.cs) against the website's own: aion2helper.com's Pet Soul Map Atlas planned these routes with its buildRoute
/// (run as it is, on the data the meter's route.json was made from); the meter must plan the same stops, in the same order, with the same pets
/// and teleports, and start where the site starts.
/// </summary>
public static class SelfCheckPetRoute
{
    // made by running the site's buildRoute (guides/pets-route-map.html) on its data: the start, then each stop as "x,z,pets,teleport" (metres)
    private const string Expected = """
[{"name":"Altgard, Cogni, from the camp","map":"altgard","faction":"Asmodian","genera":["Cogni"],"from":null,"done":[],"origin":[457,-1178],"stops":["460,-1461,Mumu Worker,Briskwind Shelter","1812,887,Drakan Watcher,Eastern Gribade Highland Cliff","1048,-934,Decomposed Lupyllini,Purifying Forest Sunny Hill","704,1542,Manduri Fighter|Manduri Trebuchet Worker|Mumu Warrior|Varg Berserker,Kumrica's Cellar","-1637,-735,Biruta|Biruta Herbalist,Gravekeeper Camp","-1646,-153,Dratona,Temporary Retreat","1030,1496,Elite Manduri Fighter,Secret Rendezvous Point","1373,314,Addicted Shulak Cart Worker,Gribade Highland Crossroad","420,-734,Klaw Scout,Faded Stump","2220,-2376,Dracuni Herbalist,","-2046,2050,Kuru Overseer|Kuru Tinkerer|Kuru Worker|Modified Lupyllini,","-1984,2439,Kerubar|Kerubian|Kerubiel,"]},{"name":"Verteron, every genus, from the camp","map":"verteron","faction":"Elyos","genera":["Cogni","Natura","Fera","Varian"],"from":null,"done":[],"origin":[788,644],"stops":["803,644,Dark Spirit|Lesser Wind Spirit,Canyon Campsite","164,-89,Elite Klaw|Klaw|Krall Laborer|Modified Karnif|Young Ursus,Verteron Fortress Valley","-594,1370,Unstable Spider,Forest of Exiles","-242,791,Biruta|Pino|Pino Worker|Skyray,Aullaeu Village Border","-623,-173,Aberrant Bee|Ashen Spider|Crestlich|Kailin|Large Leaf Gravi|Male Crestlich|Slink|Young Kailin|Young Slink,Pamos Farm","-691,-623,Aberrant Armadon|Aberrant Tog|Blue Kalgolem|Fungen|Fungie|Giant Sparkie|Kalgolem,Eastern Cantas Valley Campsite","-619,759,Aullaeu Shaman|Dry Leaf Agrint|Fossa|Ursus,Astin Mercenaries Base","939,1595,Forest Guardian|Forest Specter|Intermediate Earth Spirit|Intermediate Water Spirit|Intermediate Wind Spirit|Magic Gravi|Papis|Superior Earth Spirit|Superior Fire Spirit|Superior Water Spirit|Superior Wind Spirit|Withered Agrint,Pilgrim's Rest","-91,-893,Black Spirit|Contaminated Slime|Forest Spirit|Marsh Targon|Mutated Aberrant Odyle Spider|Potcrab|Starturtle|Zelophi,Collapsed Verteron Barrier Tower","443,744,Karnif|Vivel,Rock Hill Outpost","187,-1014,Baby Odyle Spider|Lesser Water Spirit|Skama,Spirit's Rest","1483,323,Aberrant Skama|Decomposed Lupyllini|Elite Guardite|Guardite|Lupyllini|Mutated Forest Spirit|Scorpion|Watcher Guardite,Altamia Highland Ruins","663,-732,Aberrant Coradon|Aberrant Potcrab|Dratona,Red Maple Forest","1168,-724,Dracuni Herbalist|Drakan Probe|Krall Warshaman|Oculazen,Drana Transport Hub","1359,-1191,Drakan Guard Dog,Nahid Disposal Plant","1029,-1056,Cave Bat|Drakan Watcher|Krall Warrior,","-463,320,Agrint|Airon|Brax|Crasaur|Enhanced Krall Combatant|Enhanced Krall Sentry|Forest Monster|Gravi|Konutin Worker|Nepenthes|Sylphen,Dawn Legion Temporary Outpost","291,1490,Zaif,Southern Altamia Coastal Cliffs","1475,-323,Stone Spirit,Nahid Brigade Fortress Hill","122,403,Aberrant Zaif|Elite Manduri Fighter|Giant Bee|Mutated Cadaver Insectoid|Sparkie|Tamed Zaif,Rock Hill Outpost","933,-2073,Drana Mutant Brute|Drana Predator|Drana Spore|Drana Swarm|Manduri Fighter|Manduri Trebuchet Worker|Rafflesia|Thin Agrint,","-2243,2431,Intermediate Fire Spirit|Kerubar|Kerubian|Kerubiel|Kuru Overseer|Kuru Tinkerer|Kuru Worker|Lesser Earth Spirit|Lesser Fire Spirit|Modified Lupyllini,"]},{"name":"Altgard, every genus, from Moslan Forest","map":"altgard","faction":"Asmodian","genera":["Cogni","Natura","Fera","Varian"],"from":[200,300,"Moslan Forest"],"done":[],"origin":[200,300],"stops":["1565,-1029,Agrint|Cadaver Cleaner|Fossa|Soulless Corpse Gnat,Graverobber Campsite","413,-1498,Mumu Worker|Potcrab|Vigilant Oculazen,Briskwind Shelter","1599,1692,Black Spirit|Mudthorn|Oculazen,Collapsed Hall of Fame","457,-605,Crasaur|Distorted Mutant|Forest Spirit|Klaw Scout|Large Leaf Gravi|Lesser Wind Spirit|Magic Gravi|Slime|Stone Spirit|Superior Wind Spirit,Guide Rest Stop","907,823,Starturtle|Tamed Karnif,Abandoned Campsite","1451,269,Aberrant Zaif|Addicted Shulak Cart Worker|Airon|Scorpion,Gribade Highland Crossroad","1833,810,Drakan Guard Dog|Drakan Watcher,Eastern Gribade Highland Cliff","1184,596,Blue Kalgolem|Faded Drana|Faded Floater|Faded Mutant|Faded Mutant Brute|Faded Predator|Parasite Swarm|Swarm,Steel Hammer Workcamp","-573,-702,Armadon|Drakana Kalgolem|Drana Mutant|Drana Slime,Temporary Investigation Base","-661,320,Ashen Dionae|Ashen Spider|Basilisk|Forest Monster|Mutated Ursus,Collapsed Chasm","590,-999,Aberrant Tog|Giant Sparkie|Gravi|Kalgolem|Superior Earth Spirit|Sylphen|Unstable Spider|Withered Branch Spider,Faded Stump","1486,1011,Eternal Coradon|Papis|Young Slink,Idun's Lake","-1592,-175,Brax|Drakan Probe|Dratona|Tog,Temporary Retreat","1012,-799,Aberrant Coradon|Decomposed Lupyllini|Lesser Fire Spirit|Superior Fire Spirit,Purifying Forest Sunny Hill","855,1573,Duduka Trebuchet Worker|Duduka Worker|Elite Manduri Fighter|Manduri Fighter|Manduri Trebuchet Worker|Mumu Warrior|Tayga|Varg Berserker,Kumrica's Cellar","-1041,-419,Cadaver Insectoid|Congealed Drana|Drana Predator|Kailin|Skyray|Slink|Young Kailin,Watcher's Tent","-1514,-787,Biruta|Biruta Herbalist|Mutated Forest Spirit|Rafflesia|Thin Agrint|Ursus|Vivel|Young Ursus,Gravekeeper Camp","-1493,-1324,Aberrant Bee,","2316,-2215,Ashen Agrint|Dracuni Herbalist|Intermediate Fire Spirit|Intermediate Water Spirit|Mutated Aberrant Odyle Spider|Mutated Forest Guardian|Odyle Stone Spirit|Poison Blood|Superior Water Spirit,","-2030,2219,Drana Mutant Brute|Intermediate Earth Spirit|Intermediate Wind Spirit|Kerubar|Kerubian|Kerubiel|Kuru Overseer|Kuru Tinkerer|Kuru Worker|Modified Lupyllini|Zelophi,"]},{"name":"Lower Reshanta, Asmodian","map":"abyss","faction":"Asmodian","genera":["Cogni","Natura","Fera","Varian"],"from":null,"done":[],"origin":[562,-549],"stops":["488,184,Blue Kalgolem|Eternal Coradon|Spirit King's Bodyguard Warrior|Stone Spirit|Unstable Spider,","248,340,Drakan Guard Dog|Nepenthes|Slink|Spider|Young Slink|Zelophi,","20,5,Agrint|Black Spirit|Kalgolem,","-293,192,Klaw Scout|Modified Karnif|Silver Blade Rotan,","-368,-293,Aberrant Skama|Contaminated Slime|Klaw|Naga Elite Priest|Nagarant Guardian Warrior|Swarm|Thin Agrint|Withered Agrint,"]},{"name":"Verteron, Fera and Varian, 12 pets done","map":"verteron","faction":"Elyos","genera":["Fera","Varian"],"from":null,"done":["Forest Specter","Dark Spirit","Withered Agrint","Airon","Lupyllini","Kerubim","Mookie","Brax","Krall Laborer","Dukaki","Shulack","Lepharist"],"origin":[1624,422],"stops":["1518,372,Aberrant Skama|Elite Guardite|Guardite|Scorpion|Watcher Guardite,Hill of Purification","-594,1370,Unstable Spider,Forest of Exiles","-207,818,Pino|Pino Worker|Skyray,Aullaeu Village Border","-361,-253,Kailin|Male Crestlich|Modified Karnif|Young Kailin,Hideout in Front of the Ruins","-855,-265,Aberrant Bee|Ashen Spider|Crestlich|Kalgolem|Papis,Coastal Camp","1359,-1191,Drakan Guard Dog,Nahid Disposal Plant","1117,-688,Drakan Probe|Oculazen,Drana Transport Hub","-38,-921,Baby Odyle Spider|Contaminated Slime|Marsh Targon|Mutated Aberrant Odyle Spider|Potcrab|Skama|Starturtle,Collapsed Verteron Barrier Tower","-364,371,Crasaur|Forest Monster|Mutated Cadaver Insectoid|Sylphen,Dawn Legion Temporary Outpost","926,-1057,Cave Bat,Scarlet Forest Observation Site","-597,-607,Aberrant Armadon|Aberrant Tog|Blue Kalgolem|Giant Sparkie,Eastern Cantas Valley Campsite","443,744,Karnif|Vivel,Rock Hill Outpost","678,-661,Aberrant Coradon|Aberrant Potcrab,Red Maple Forest","-843,711,Fossa|Slink|Ursus|Young Slink,Southern Shugo Station","458,-179,Young Ursus,Kraka Uphill Path","189,435,Aberrant Zaif|Giant Bee|Sparkie|Tamed Zaif,Rock Hill Outpost","291,1490,Zaif,Southern Altamia Coastal Cliffs","940,-2053,Drana Mutant Brute|Drana Predator|Drana Swarm,"]},{"name":"Altgard, every genus, from the camp","map":"altgard","faction":"Asmodian","genera":["Cogni","Natura","Fera","Varian"],"from":null,"done":[],"origin":[1408,-946],"stops":["1565,-1029,Agrint|Cadaver Cleaner|Fossa|Soulless Corpse Gnat,Graverobber Campsite","413,-1498,Mumu Worker|Potcrab|Vigilant Oculazen,Briskwind Shelter","1599,1692,Black Spirit|Mudthorn|Oculazen,Collapsed Hall of Fame","457,-605,Crasaur|Distorted Mutant|Forest Spirit|Klaw Scout|Large Leaf Gravi|Lesser Wind Spirit|Magic Gravi|Slime|Stone Spirit|Superior Wind Spirit,Guide Rest Stop","907,823,Starturtle|Tamed Karnif,Abandoned Campsite","1451,269,Aberrant Zaif|Addicted Shulak Cart Worker|Airon|Scorpion,Gribade Highland Crossroad","1833,810,Drakan Guard Dog|Drakan Watcher,Eastern Gribade Highland Cliff","1184,596,Blue Kalgolem|Faded Drana|Faded Floater|Faded Mutant|Faded Mutant Brute|Faded Predator|Parasite Swarm|Swarm,Steel Hammer Workcamp","-573,-702,Armadon|Drakana Kalgolem|Drana Mutant|Drana Slime,Temporary Investigation Base","-661,320,Ashen Dionae|Ashen Spider|Basilisk|Forest Monster|Mutated Ursus,Collapsed Chasm","590,-999,Aberrant Tog|Giant Sparkie|Gravi|Kalgolem|Superior Earth Spirit|Sylphen|Unstable Spider|Withered Branch Spider,Faded Stump","1486,1011,Eternal Coradon|Papis|Young Slink,Idun's Lake","-1592,-175,Brax|Drakan Probe|Dratona|Tog,Temporary Retreat","1012,-799,Aberrant Coradon|Decomposed Lupyllini|Lesser Fire Spirit|Superior Fire Spirit,Purifying Forest Sunny Hill","855,1573,Duduka Trebuchet Worker|Duduka Worker|Elite Manduri Fighter|Manduri Fighter|Manduri Trebuchet Worker|Mumu Warrior|Tayga|Varg Berserker,Kumrica's Cellar","-1041,-419,Cadaver Insectoid|Congealed Drana|Drana Predator|Kailin|Skyray|Slink|Young Kailin,Watcher's Tent","-1514,-787,Biruta|Biruta Herbalist|Mutated Forest Spirit|Rafflesia|Thin Agrint|Ursus|Vivel|Young Ursus,Gravekeeper Camp","-1493,-1324,Aberrant Bee,","2316,-2215,Ashen Agrint|Dracuni Herbalist|Intermediate Fire Spirit|Intermediate Water Spirit|Mutated Aberrant Odyle Spider|Mutated Forest Guardian|Odyle Stone Spirit|Poison Blood|Superior Water Spirit,","-2030,2219,Drana Mutant Brute|Intermediate Earth Spirit|Intermediate Wind Spirit|Kerubar|Kerubian|Kerubiel|Kuru Overseer|Kuru Tinkerer|Kuru Worker|Modified Lupyllini|Zelophi,"]},{"name":"Lower Reshanta, Elyos","map":"abyss","faction":"Elyos","genera":["Cogni","Natura","Fera","Varian"],"from":null,"done":[],"origin":[-527,522],"stops":["-293,192,Klaw Scout|Modified Karnif|Silver Blade Rotan,","-368,-293,Aberrant Skama|Contaminated Slime|Klaw|Naga Elite Priest|Nagarant Guardian Warrior|Swarm|Thin Agrint|Withered Agrint,","20,5,Agrint|Black Spirit|Kalgolem,","248,340,Drakan Guard Dog|Nepenthes|Slink|Spider|Young Slink|Zelophi,","488,184,Blue Kalgolem|Eternal Coradon|Spirit King's Bodyguard Warrior|Stone Spirit|Unstable Spider,"]}]
""";

    public static bool Run()
    {
        Console.WriteLine("[selftest] Pet route (the website's route, planned by the meter):");
        bool ok = true;
        using JsonDocument doc = JsonDocument.Parse(Expected);
        foreach (JsonElement c in doc.RootElement.EnumerateArray())
        {
            string name = c.GetProperty("name").GetString()!;
            var genera = c.GetProperty("genera").EnumerateArray().Select(g => g.GetString()!).ToHashSet();
            var done = c.GetProperty("done").EnumerateArray().Select(g => g.GetString()!).ToHashSet();
            (double, double, string)? from = c.GetProperty("from").ValueKind == JsonValueKind.Array
                ? (c.GetProperty("from")[0].GetDouble(), c.GetProperty("from")[1].GetDouble(), c.GetProperty("from")[2].GetString()!)
                : null;
            PetRoutePlan? plan = PetRoute.Plan(c.GetProperty("map").GetString()!, c.GetProperty("faction").GetString(), genera,
                id => !done.Contains(PetRoute.NameOf(id) ?? ""), from);
            var expected = c.GetProperty("stops").EnumerateArray().Select(s => s.GetString()!).ToList();
            var got = plan?.Stops.Select(s => $"{Round(s.X)},{Round(s.Z)},{string.Join("|", s.Pets.Select(id => PetRoute.NameOf(id)))},{s.Teleport?.Name ?? ""}").ToList() ?? new List<string>();
            bool origin = plan is not null && Round(plan.OriginX) == c.GetProperty("origin")[0].GetInt32() && Round(plan.OriginZ) == c.GetProperty("origin")[1].GetInt32();
            int same = expected.Zip(got).TakeWhile(p => p.First == p.Second).Count();
            bool good = origin && got.Count == expected.Count && same == expected.Count;
            ok &= good;
            Console.WriteLine($"  -> {name}: {got.Count} stops (site {expected.Count}), the same up to stop {same}, start {(origin ? "the same" : "different")}: {(good ? "ok" : "FAIL")}");
            if (!good && same < Math.Max(expected.Count, got.Count))
            {
                Console.WriteLine($"     site:  {(same < expected.Count ? expected[same] : "-")}");
                Console.WriteLine($"     meter: {(same < got.Count ? got[same] : "-")}");
            }
        }

        // every stop of every route (all genera, nothing done, from each faction's start) has spawn points of each of its pets on the meter's map
        // within 100 m of that pet's spot: the route never leads to a place where the map shows nothing of what it is there for
        foreach (var (mapKey, faction) in new[] { ("verteron", "Elyos"), ("altgard", "Asmodian"), ("abyss", "Elyos"), ("abyss", "Asmodian") })
        {
            var map = AionDPS.Aion2.Protocol.Aion2Maps.All.FirstOrDefault(m => m.Key == mapKey);
            PetRoutePlan? plan = PetRoute.Plan(mapKey, faction, PetRoute.Genera.ToHashSet(), _ => true);
            if (map is null || plan is null)
            {
                ok = false;
                Console.WriteLine($"  -> {mapKey}: no map or no route: FAIL");
                continue;
            }

            int pets = 0, covered = 0;
            var missing = new List<string>();
            foreach (PetRouteStop stop in plan.Stops)
            {
                foreach (int pet in stop.Pets)
                {
                    pets++;
                    // the pet's spot at this stop (the middle of a group of its monsters): its spawn points lie around it
                    var points = AionDPS.Aion2.Protocol.Aion2Maps.PointsOf(map, new HashSet<int> { pet });
                    PetRouteSpot spot = stop.At[pet];
                    if (points.Any(pt => Math.Sqrt((pt.X / 100.0 - spot.X) * (pt.X / 100.0 - spot.X) + (pt.Y / 100.0 - spot.Z) * (pt.Y / 100.0 - spot.Z)) <= 100))
                    {
                        covered++;
                    }
                    else
                    {
                        missing.Add($"stop {stop.Step} {PetRoute.NameOf(pet)}");
                    }
                }
            }

            bool all = covered == pets;
            ok &= all;
            Console.WriteLine($"  -> {mapKey} ({faction}): {plan.Stops.Count} stops, {covered} of {pets} pets with spawn points on the map at their spot: {(all ? "ok" : "FAIL " + string.Join(", ", missing.Take(6)))}");
        }

        Console.WriteLine(ok ? "  -> pet route: ok" : "  -> pet route: FAIL");
        return ok;
    }

    /// <summary>Rounded as JavaScript's Math.round does (halves up).</summary>
    private static int Round(double v) => (int)Math.Floor(v + 0.5);
}
