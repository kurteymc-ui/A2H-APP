using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2.Protocol;

/// <summary>A title's stats: the slot it is worn in (Attack, Defense, Etc), its grade, what it gives while worn and what it gives once owned.</summary>
public sealed record CpTitleData(int Id, string Category, string Grade, IReadOnlyDictionary<string, int> Equip, IReadOnlyDictionary<string, int> Owned);

/// <summary>A wing's stats while it is equipped (the game's units).</summary>
public sealed record CpWingData(int Id, string Grade, IReadOnlyDictionary<string, int> Stats);

/// <summary>A property an Analysis of a Genus Insight slot can roll: the stat, its range (the game's units) and its weight among the slot's properties of that grade.</summary>
public sealed record CpGenusOption(string Stat, int Min, int Max, int Weight);

/// <summary>
/// A Genus Insight board as the data has it: the meter's species id (2 Cogni .. 6 Special), the odds (in 1/10000) of an Analysis rolling
/// each grade (Common, Rare, Epic, Unique, Heroic = the packet's kind 1..5) at each Insight level 1..10, and every slot's properties by grade.
/// </summary>
public sealed record CpGenusData(string Key, int SpeciesId, IReadOnlyList<int[]> Odds, IReadOnlyList<IReadOnlyList<IReadOnlyList<CpGenusOption>>> Slots)
{
    /// <summary>The grade odds at an Insight level (1..10), as shares of 1.</summary>
    public double[] OddsAt(int level)
    {
        int[] raw = Odds.Count == 0 ? new int[5] : Odds[Math.Clamp(level, 1, Odds.Count) - 1];
        double sum = raw.Sum();
        return raw.Select(o => sum > 0 ? o / sum : 0).ToArray();
    }

    /// <summary>The properties of a slot (1..9) of a grade (1..5); empty when the slot has none of that grade.</summary>
    public IReadOnlyList<CpGenusOption> Options(int slot, int grade) =>
        slot >= 1 && slot <= Slots.Count && grade >= 1 && grade <= Slots[slot - 1].Count ? Slots[slot - 1][grade - 1] : Array.Empty<CpGenusOption>();
}

/// <summary>
/// The CP calculator's other sources (assets/aion2/cp/cp_sources.json): titles (worn and owned), wings, what every pet of a genus gives at its level 1-3, Genus Insight's property pools and grade odds,
/// the stigma level bands of the website's model, and the stat ids and names the Genus Insight frame and the deity values use.
/// </summary>
public static class Aion2CpSources
{
    private static readonly object Gate = new();
    private static Data? _data;

    /// <summary>The pet window's genera in its order, by the name the game uses (Intellect = Cogni ...), with the meter's species id.</summary>
    public static readonly IReadOnlyList<(string Key, int SpeciesId)> Genera = new[] { ("Intellect", 2), ("Feral", 3), ("Nature", 4), ("Trans", 5), ("Special", 6) };

    private sealed class Data
    {
        public Dictionary<int, string> Ids = new();
        public Dictionary<string, int> IdOf = new(StringComparer.Ordinal);
        public Dictionary<string, Dictionary<string, string>> Names = new(StringComparer.Ordinal);
        public HashSet<string> Percent = new(StringComparer.Ordinal);
        public Dictionary<int, CpTitleData> Titles = new();
        public Dictionary<int, CpWingData> Wings = new();
        public Dictionary<string, List<Dictionary<string, int>>> Pets = new(StringComparer.Ordinal);
        public Dictionary<string, CpGenusData> Genus = new(StringComparer.Ordinal);
        public List<(int From, int To, double Per)> StigmaBands = new();
    }

    private static Data D
    {
        get
        {
            lock (Gate)
            {
                return _data ??= Load(Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "cp", "cp_sources.json"));
            }
        }
    }

    public static bool Available => D.Titles.Count > 0 || D.Genus.Count > 0;

    public static string? Token(int statId) => D.Ids.GetValueOrDefault(statId);

    public static int StatId(string token) => D.IdOf.GetValueOrDefault(token);

    public static IReadOnlyDictionary<string, string>? Names(string token) => D.Names.GetValueOrDefault(token);

    public static bool Percent(string token) => D.Percent.Contains(token);

    public static CpTitleData? Title(int id) => D.Titles.GetValueOrDefault(id);

    /// <summary>Every title the tables give stats (the picture mode's owned titles).</summary>
    public static IEnumerable<int> TitleIds => D.Titles.Keys;

    /// <summary>Every wing of the tables.</summary>
    public static IEnumerable<int> WingIds => D.Wings.Keys;

    public static CpWingData? Wing(int id) => D.Wings.GetValueOrDefault(id);

    /// <summary>What one pet of a genus (Intellect, Feral, Nature, Trans, Special) gives at its level (1..3; totals, not steps); empty when nothing.</summary>
    public static IReadOnlyDictionary<string, int> PetStats(string genus, int level) =>
        D.Pets.TryGetValue(genus, out var levels) && level >= 1 && level <= levels.Count ? levels[level - 1] : new Dictionary<string, int>();

    public static CpGenusData? Genus(string key) => D.Genus.GetValueOrDefault(key);

    public static CpGenusData? GenusOfSpecies(int speciesId) => D.Genus.Values.FirstOrDefault(g => g.SpeciesId == speciesId);

    /// <summary>The genus of the pet window's category of a pet (Aion2Pets: Cognia, Fera, Natura, Varia, Specia) by the tables' name.</summary>
    public static string? GenusKeyOfPetSpecies(string? petSpecies) => petSpecies switch
    {
        "Cognia" => "Intellect", "Fera" => "Feral", "Natura" => "Nature", "Varia" => "Trans", "Specia" => "Special", _ => null,
    };

    /// <summary>
    /// What an equipped Stigma at a level counts for in the website's model, before its weight: 225 CP a level up to 5, 400 to 10, 700 to 15, 1,200 to
    /// 20 and 1,600 to 25.
    /// </summary>
    public static double StigmaPoints(int level)
    {
        double sum = 0;
        foreach (var (from, to, per) in D.StigmaBands)
        {
            int top = Math.Min(level, to);
            if (top > from)
            {
                sum += (top - from) * per;
            }
        }

        return sum;
    }

    /// <summary>The highest level the stigma bands go to (25).</summary>
    public static int StigmaTop => D.StigmaBands.Count == 0 ? 0 : D.StigmaBands.Max(b => b.To);

    private static Data Load(string path)
    {
        var d = new Data();
        try
        {
            if (!File.Exists(path))
            {
                return d;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement r = doc.RootElement;
            static Dictionary<string, int> Stats(JsonElement e) => e.EnumerateObject().ToDictionary(n => n.Name, n => n.Value.GetInt32(), StringComparer.Ordinal);
            foreach (JsonProperty p in r.GetProperty("ids").EnumerateObject())
            {
                int id = int.Parse(p.Name, System.Globalization.CultureInfo.InvariantCulture);
                string token = p.Value.GetString() ?? "";
                d.Ids[id] = token;
                d.IdOf[token] = id;
            }

            foreach (JsonProperty p in r.GetProperty("names").EnumerateObject())
            {
                d.Names[p.Name] = p.Value.EnumerateObject().ToDictionary(n => n.Name, n => n.Value.GetString() ?? "", StringComparer.Ordinal);
            }

            foreach (JsonElement p in r.GetProperty("pct").EnumerateArray())
            {
                d.Percent.Add(p.GetString() ?? "");
            }

            foreach (JsonProperty p in r.GetProperty("titles").EnumerateObject())
            {
                int id = int.Parse(p.Name, System.Globalization.CultureInfo.InvariantCulture);
                d.Titles[id] = new CpTitleData(id, p.Value[0].GetString() ?? "", p.Value[1].GetString() ?? "", Stats(p.Value[2]), Stats(p.Value[3]));
            }

            foreach (JsonProperty p in r.GetProperty("wings").EnumerateObject())
            {
                int id = int.Parse(p.Name, System.Globalization.CultureInfo.InvariantCulture);
                d.Wings[id] = new CpWingData(id, p.Value[0].GetString() ?? "", Stats(p.Value[1]));
            }

            foreach (JsonProperty p in r.GetProperty("pets").EnumerateObject())
            {
                d.Pets[p.Name] = p.Value.EnumerateArray().Select(Stats).ToList();
            }

            foreach (JsonProperty p in r.GetProperty("genus").EnumerateObject())
            {
                JsonElement g = p.Value;
                var odds = g.GetProperty("odds").EnumerateArray().Select(l => l.EnumerateArray().Select(o => o.GetInt32()).ToArray()).ToList();
                var slots = g.GetProperty("slots").EnumerateArray().Select(slot => (IReadOnlyList<IReadOnlyList<CpGenusOption>>)new[] { "Common", "Rare", "Legend", "Unique", "Epic" }
                    .Select(grade => (IReadOnlyList<CpGenusOption>)(slot.TryGetProperty(grade, out JsonElement lines)
                        ? lines.EnumerateArray().Select(l => new CpGenusOption(l[0].GetString() ?? "", l[1].GetInt32(), l[2].GetInt32(), l[3].GetInt32())).ToList()
                        : new List<CpGenusOption>())).ToList()).ToList();
                d.Genus[p.Name] = new CpGenusData(p.Name, g.GetProperty("id").GetInt32(), odds, slots);
            }

            foreach (JsonElement b in r.GetProperty("stigmaBands").EnumerateArray())
            {
                d.StigmaBands.Add((b[0].GetInt32(), b[1].GetInt32(), b[2].GetDouble()));
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException or UnauthorizedAccessException)
        {
            return new Data();
        }

        return d;
    }
}
