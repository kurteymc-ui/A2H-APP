using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AionDPS.Aion2.Protocol;

/// <summary>A line a piece of gear can roll when its soul is bound: the stat, its range (the game's units) and its roll weight.</summary>
public sealed record CpPoolLine(string Stat, int Min, int Max, int Weight);

/// <summary>What the data says about one piece of gear: its enhancement group (which also keys its breakthrough levels), its kind
/// (Greatsword, Helmet ...), its level, its base stats and the lines its soul binding can roll.</summary>
public sealed record CpItemData(int Id, string Group, string Category, int Level, IReadOnlyDictionary<string, int> Base, IReadOnlyList<CpPoolLine> Pool);

/// <summary>
/// The Combat Power model: the CP one point (or one percent) of each stat is worth, measured on real characters by the website and refit there every week.
/// <see cref="Confidence"/> is the website's word for each weight (confirmed, fitted, rough, disputed); <see cref="FromWebsite"/> tells the built-in model from
/// the one fetched at runtime. <see cref="IlMult"/>: how much more every stat is worth per 1,000 Item Level above <see cref="IlRef"/> (the model's
/// progression factor: Combat Power grows with progression in ways the stats don't show). <see cref="Base"/>: the Combat Power of each class's base
/// stats at level 45 (the website's fit; "Spiritmaster" is the meter's Elementalist).
/// </summary>
public sealed record CpModel(IReadOnlyDictionary<string, double> Weights, IReadOnlyDictionary<string, string> Confidence, bool FromWebsite, double IlMult = 0, double IlRef = 2500,
    IReadOnlyDictionary<string, double>? Base = null)
{
    public double Weight(string key) => Weights.GetValueOrDefault(key);

    /// <summary>The class's base (the meter's class names; an unknown class gets the average of the others, as on the website).</summary>
    public double BaseOf(string? className)
    {
        if (Base is not { Count: > 0 } bases)
        {
            return 0;
        }

        string key = className == "Elementalist" ? "Spiritmaster" : className ?? "";
        return bases.TryGetValue(key, out double b) ? b : bases.Values.Average();
    }

    /// <summary>The progression factor at an Item Level (1 without one or without the factor).</summary>
    public double Progression(int? itemLevel) => IlMult > 0 && itemLevel is > 0 ? Math.Max(0.5, 1 + IlMult * (itemLevel.Value - IlRef) / 1000) : 1;
}

/// <summary>
/// The character's numbers the CP of a line depends on, counted the way the website's model counts them: the totals some lines take a share of (Might:
/// 0.1 % of Attack a point; "Attack increase" 1 % of it per 100 ...), the increases that raise every point of Attack, Critical Hit, Accuracy and HP
/// (Attack = points x (1 + Attack increase)), the progression factor of the character's Item Level, and the factors measured on the character itself
/// (<see cref="AionDPS.Combat.CpChecks"/>: the game's own Combat Power before and after a change against what the model said, by the model's stat).
/// <see cref="Split"/>: the totals of the whole character (<see cref="AionDPS.Combat.CpWhole"/>), where every line's CP is its share of the character's
/// Combat Power: a point of Attack and the increase that raises it share what they make together half and half, so all lines add up to the total
/// (without it a line's CP is what it adds on its own, and an Attack line and a Might line both count the Attack the Might raises).
/// </summary>
public sealed record CpTotals(double Attack, double Critical, double Accuracy, double Hp, double Defense, double AttackIncrease = 0, double CritIncrease = 0,
    double AccuracyIncrease = 0, double HpIncrease = 0, double Scale = 1, IReadOnlyDictionary<string, double>? Factors = null, double DefenseIncrease = 0, bool Split = false)
{
    /// <summary>The same totals counting what a change adds on its own (what one more point gives).</summary>
    public CpTotals Marginal => this with { Split = false };

    /// <summary>The factor measured for a model stat ("atk"), 1 when none was.</summary>
    public double Factor(string key) => Factors is not null && Factors.TryGetValue(key, out double f) ? f : 1;
}

/// <summary>
/// The CP calculator's data (assets/aion2/cp/cp_data.json):
/// every piece of gear by item id, its enhancement and breakthrough levels (totals), the manastone values per colour (a range: the meter reads a
/// stone's stat and colour, not which kind of stone it was), the meter's stat ids and the stats' names in the game's languages, and the CP each stat is
/// worth (the website's model; <see cref="RefreshModelAsync"/> takes the live one).
/// </summary>
public static class Aion2CpData
{
    private static readonly object Gate = new();
    private static Data? _data;
    private static CpModel? _live;
    private static int _refreshing;

    private sealed class Data
    {
        public Dictionary<int, string> Ids = new();
        public Dictionary<string, Dictionary<string, string>> Names = new(StringComparer.Ordinal);
        public HashSet<string> Percent = new(StringComparer.Ordinal);
        public Dictionary<int, CpItemData> Items = new();
        public Dictionary<string, List<Dictionary<string, int>>> Enchant = new(StringComparer.Ordinal);
        public Dictionary<string, List<Dictionary<string, int>>> Exceed = new(StringComparer.Ordinal);
        public Dictionary<string, double?[]> ExceedChance = new(StringComparer.Ordinal);
        public Dictionary<string, Dictionary<int, (int Min, int Max)>> Stones = new(StringComparer.Ordinal);
        public Dictionary<string, (string Key, double Unit)> Map = new(StringComparer.Ordinal);
        public Dictionary<string, (string Total, double Per)> Share = new(StringComparer.Ordinal);
        public CpModel Model = new(new Dictionary<string, double>(), new Dictionary<string, string>(), false);
    }

    private static readonly IReadOnlyDictionary<string, int> None = new Dictionary<string, int>();

    private static Data D
    {
        get
        {
            lock (Gate)
            {
                return _data ??= Load(Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "cp", "cp_data.json"));
            }
        }
    }

    /// <summary>Whether the data file was there and read.</summary>
    public static bool Available => D.Items.Count > 0;

    /// <summary>The model in use: the website's live one once fetched, else the one built in.</summary>
    public static CpModel Model => _live ?? D.Model;

    public static CpItemData? Item(int itemId) => D.Items.GetValueOrDefault(itemId);

    public static IEnumerable<CpItemData> Items => D.Items.Values;

    /// <summary>The token of a stat id the meter reads ("CombatSpeed"), null when the meter doesn't know it.</summary>
    public static string? Token(int statId) => D.Ids.GetValueOrDefault(statId) ?? Aion2CpSources.Token(statId);

    public static int StatId(string token) => D.Ids.FirstOrDefault(kv => kv.Value == token).Key is int id and > 0 ? id : Aion2CpSources.StatId(token);

    /// <summary>Every stat id the meter knows a token for.</summary>
    public static IEnumerable<int> StatIds => D.Ids.Keys;

    /// <summary>The stat's name in the game's own words; English for a language the client has no text in, the token as a last resort.</summary>
    public static string Name(string token, string language)
    {
        IReadOnlyDictionary<string, string>? names = D.Names.TryGetValue(token, out var own) ? own : Aion2CpSources.Names(token);
        if (names is null)
        {
            return token;
        }

        string lang = language.StartsWith("zh", StringComparison.Ordinal) ? "en" : language.Split('-')[0];
        return names.GetValueOrDefault(lang) ?? names.GetValueOrDefault("en") ?? token;
    }

    /// <summary>Whether the game shows the stat as a percentage (its value then in hundredths of a percent).</summary>
    public static bool Percent(string token) => D.Percent.Contains(token) || Aion2CpSources.Percent(token);

    /// <summary>The stats of a piece's enhancement group at a level (totals; 0 or less: none). Above the group's top level: the top level.</summary>
    public static IReadOnlyDictionary<string, int> Enchant(string group, int level) =>
        level <= 0 || !D.Enchant.TryGetValue(group, out var levels) || levels.Count == 0 ? None : levels[Math.Min(level, levels.Count) - 1];

    public static int MaxEnchant(string group) => D.Enchant.TryGetValue(group, out var levels) ? levels.Count : 0;

    /// <summary>The stats of a breakthrough level (totals; 0: none).</summary>
    public static IReadOnlyDictionary<string, int> Exceed(string group, int level) =>
        level <= 0 || !D.Exceed.TryGetValue(group, out var levels) || levels.Count == 0 ? None : levels[Math.Min(level, levels.Count) - 1];

    public static int MaxExceed(string group) => D.Exceed.TryGetValue(group, out var levels) ? levels.Count : 0;

    /// <summary>The chance (percent) of reaching breakthrough level <paramref name="level"/> from the one below, as the game's table has it.</summary>
    public static double? ExceedChance(string group, int level) =>
        D.ExceedChance.TryGetValue(group, out var chances) && level >= 1 && level <= chances.Length ? chances[level - 1] : null;

    /// <summary>What a manastone of this stat and colour (1 white … 4 purple) adds: the range over the kinds of stone.</summary>
    public static (int Min, int Max)? Stone(string token, int tier) =>
        D.Stones.TryGetValue(token, out var tiers) && tiers.TryGetValue(tier, out var range) ? range : null;

    /// <summary>Every stat a manastone can carry.</summary>
    public static IEnumerable<string> StoneStats => D.Stones.Keys;

    /// <summary>
    /// The CP a stat value is worth (the game's units: a percentage in hundredths), or null when Combat Power doesn't count the stat. A stat that adds a
    /// share of a total (Might, "Attack increase", HP %) is worked out from <paramref name="totals"/>; a point of Attack, Critical Hit, Accuracy or HP
    /// is raised by the character's increase of it, and every stat by the progression factor and the factor measured on the character.
    /// </summary>
    public static double? Cp(string token, double value, CpTotals totals, CpModel? model = null) =>
        CpParts(token, value, totals, model) is { } parts ? parts.Sum(p => p.Cp) : null;

    /// <summary>The CP of a stat value split by the model stats it counts towards (Precision raises Critical Hit and Accuracy); null when Combat Power
    /// doesn't count the stat.</summary>
    public static IReadOnlyList<(string Key, double Cp)>? CpParts(string token, double value, CpTotals totals, CpModel? model = null)
    {
        model ??= Model;
        double At(string key) => totals.Scale * totals.Factor(key);
        if (D.Map.TryGetValue(token, out var map))
        {
            if (!model.Weights.TryGetValue(map.Key, out double w))
            {
                return null;
            }

            double raise = map.Key switch
            {
                "atk" => totals.AttackIncrease,
                "crit" => totals.CritIncrease,
                "acc" => totals.AccuracyIncrease,
                "hp" => totals.HpIncrease,
                "def" => totals.DefenseIncrease,
                _ => 0,
            };
            return new[] { (map.Key, value / map.Unit * w * (1 + (totals.Split ? raise / 2 : raise)) * At(map.Key)) };
        }

        if (!D.Share.TryGetValue(token, out var share))
        {
            return null;
        }

        double part = share.Per * value * (totals.Split && share.Total is "atk" or "def" or "hp" or "crit" or "acc" or "critacc" ? 0.5 : 1);
        return share.Total switch
        {
            "atk" => new[] { ("atk", totals.Attack * part * model.Weight("atk") * At("atk")) },
            "def" => new[] { ("def", totals.Defense * part * model.Weight("def") * At("def")) },
            "hp" => new[] { ("hp", totals.Hp * part * model.Weight("hp") * At("hp")) },
            "crit" => new[] { ("crit", totals.Critical * part * model.Weight("crit") * At("crit")) },
            "acc" => new[] { ("acc", totals.Accuracy * part * model.Weight("acc") * At("acc")) },
            "critacc" => new[] { ("crit", totals.Critical * part * model.Weight("crit") * At("crit")), ("acc", totals.Accuracy * part * model.Weight("acc") * At("acc")) },
            _ => new[] { (share.Total, part * model.Weight(share.Total) * At(share.Total)) }, // percentage points of Combat Speed, Perfect, Double, Cooldown Reduction
        };
    }

    /// <summary>The model stat a game stat adds points to and the game's units per model unit (100 for a percentage), null when it adds none.</summary>
    public static (string Key, double Unit)? MapOf(string token) => D.Map.TryGetValue(token, out var map) ? map : null;

    /// <summary>The total a game stat adds a share of (Might: "atk", 0.001 a point) and how much per point, null when it adds none.</summary>
    public static (string Total, double Per)? ShareOf(string token) => D.Share.TryGetValue(token, out var share) ? share : null;

    /// <summary>The model stat a game stat counts towards ("atk" for Attack, Might and "Attack increase"; Precision as Critical Hit); null when none.</summary>
    public static string? KeyOf(string token) =>
        D.Map.TryGetValue(token, out var map) ? map.Key : D.Share.TryGetValue(token, out var share) ? (share.Total == "critacc" ? "crit" : share.Total) : null;

    /// <summary>The model stat a game stat counts as, for its confidence ("confirmed" ...); null when CP doesn't count it.</summary>
    public static string? Confidence(string token, CpModel? model = null)
    {
        model ??= Model;
        string? key = D.Map.TryGetValue(token, out var map) ? map.Key : D.Share.TryGetValue(token, out var share) ? (share.Total == "critacc" ? "crit" : share.Total) : null;
        return key is null ? null : model.Confidence.GetValueOrDefault(key);
    }

    /// <summary>
    /// Takes the website's live model (refit weekly on the characters it reads) instead of the built-in one: once per run, in the background, never when
    /// the player declined the privacy terms. Only a public file is asked for; nothing is sent. Keeps the built-in model when the site can't be reached.
    /// </summary>
    public static async Task RefreshModelAsync(HttpClient http)
    {
        if (Interlocked.Exchange(ref _refreshing, 1) == 1)
        {
            return;
        }

        try
        {
            string js = await http.GetStringAsync(Brand.Site + "/armory/assets/cp-model-data.js").ConfigureAwait(false);
            if (ModelJson(js) is not { } json)
            {
                return;
            }

            using var doc = JsonDocument.Parse(json);
            CpModel? model = ReadModel(doc.RootElement, fromWebsite: true);
            if (model is { Weights.Count: > 5 })
            {
                _live = model;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or ArgumentException)
        {
            Interlocked.Exchange(ref _refreshing, 0); // try again next time the page opens
        }
    }

    /// <summary>For the self-test: the built-in model again.</summary>
    internal static void ForgetLiveModel() => _live = null;

    /// <summary>
    /// The model's JSON out of the website's file: the built-in file is <c>window.A2_CPMODEL = {...};</c>; the weekly refit comes scrambled like the
    /// site's other data files, <c>window[D(name, key)] = JSON.parse(D(text, key))</c>, every printable character replaced by the one at its place in
    /// the key (character n of the key stands for character 32 + n). Null when neither is there.
    /// </summary>
    internal static string? ModelJson(string js)
    {
        Match packed = Regex.Match(js, "JSON\\.parse\\(D\\((\"(?:[^\"\\\\]|\\\\.)*\"),\\s*(\"(?:[^\"\\\\]|\\\\.)*\")\\)\\)");
        if (packed.Success)
        {
            try
            {
                string text = Literal(packed.Groups[1].Value), key = Literal(packed.Groups[2].Value);
                var plain = new StringBuilder(text.Length);
                foreach (char c in text)
                {
                    int at = key.IndexOf(c);
                    plain.Append(at >= 0 ? (char)(32 + at) : c);
                }

                return plain.ToString();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        int start = js.IndexOf('{'), end = js.LastIndexOf('}');
        return start >= 0 && end > start ? js[start..(end + 1)] : null;
    }

    /// <summary>A JavaScript string literal as JSON reads it (the site writes "&lt;" as \x3c).</summary>
    private static string Literal(string literal) => JsonSerializer.Deserialize<string>(literal.Replace("\\x3c", "\\u003c", StringComparison.Ordinal)) ?? "";

    private static CpModel? ReadModel(JsonElement root, bool fromWebsite)
    {
        if (!root.TryGetProperty("w", out JsonElement w) || w.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var weights = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (JsonProperty p in w.EnumerateObject())
        {
            if (p.Value.ValueKind == JsonValueKind.Number)
            {
                weights[p.Name] = p.Value.GetDouble();
            }
        }

        var confidence = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty("confidence", out JsonElement c) && c.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty p in c.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.String)
                {
                    confidence[p.Name] = p.Value.GetString() ?? "";
                }
            }
        }

        double Number(string name, double fallback) => root.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : fallback;
        var bases = new Dictionary<string, double>(StringComparer.Ordinal);
        if (root.TryGetProperty("base", out JsonElement b) && b.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty p in b.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.Number)
                {
                    bases[p.Name] = p.Value.GetDouble();
                }
            }
        }

        return new CpModel(weights, confidence, fromWebsite, Number("ilMult", 0), Number("ilRef", 2500), bases);
    }

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
            foreach (JsonProperty p in r.GetProperty("ids").EnumerateObject())
            {
                d.Ids[int.Parse(p.Name, System.Globalization.CultureInfo.InvariantCulture)] = p.Value.GetString() ?? "";
            }

            foreach (JsonProperty p in r.GetProperty("names").EnumerateObject())
            {
                d.Names[p.Name] = p.Value.EnumerateObject().ToDictionary(n => n.Name, n => n.Value.GetString() ?? "", StringComparer.Ordinal);
            }

            foreach (JsonElement p in r.GetProperty("pct").EnumerateArray())
            {
                d.Percent.Add(p.GetString() ?? "");
            }

            foreach (JsonProperty p in r.GetProperty("items").EnumerateObject())
            {
                JsonElement a = p.Value;
                int id = int.Parse(p.Name, System.Globalization.CultureInfo.InvariantCulture);
                var baseStats = a[3].EnumerateObject().ToDictionary(n => n.Name, n => n.Value.GetInt32(), StringComparer.Ordinal);
                var pool = a[4].EnumerateArray().Select(l => new CpPoolLine(l[0].GetString() ?? "", l[1].GetInt32(), l[2].GetInt32(), l[3].GetInt32())).ToList();
                d.Items[id] = new CpItemData(id, a[0].GetString() ?? "", a[1].GetString() ?? "", a[2].GetInt32(), baseStats, pool);
            }

            static List<Dictionary<string, int>> Levels(JsonElement e) =>
                e.EnumerateArray().Select(l => l.EnumerateObject().ToDictionary(n => n.Name, n => n.Value.GetInt32(), StringComparer.Ordinal)).ToList();
            foreach (JsonProperty p in r.GetProperty("enchant").EnumerateObject())
            {
                d.Enchant[p.Name] = Levels(p.Value);
            }

            foreach (JsonProperty p in r.GetProperty("exceed").EnumerateObject())
            {
                d.Exceed[p.Name] = Levels(p.Value);
            }

            foreach (JsonProperty p in r.GetProperty("exceedChance").EnumerateObject())
            {
                d.ExceedChance[p.Name] = p.Value.EnumerateArray().Select(c => c.ValueKind == JsonValueKind.Number ? c.GetDouble() : (double?)null).ToArray();
            }

            foreach (JsonProperty p in r.GetProperty("stones").EnumerateObject())
            {
                d.Stones[p.Name] = p.Value.EnumerateObject().ToDictionary(t => int.Parse(t.Name, System.Globalization.CultureInfo.InvariantCulture), t => (t.Value[0].GetInt32(), t.Value[1].GetInt32()));
            }

            foreach (JsonProperty p in r.GetProperty("map").EnumerateObject())
            {
                d.Map[p.Name] = (p.Value[0].GetString() ?? "", p.Value[1].GetDouble());
            }

            foreach (JsonProperty p in r.GetProperty("share").EnumerateObject())
            {
                d.Share[p.Name] = (p.Value[0].GetString() ?? "", p.Value[1].GetDouble());
            }

            d.Model = ReadModel(r.GetProperty("model"), fromWebsite: false) ?? d.Model;
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException or UnauthorizedAccessException)
        {
            return new Data(); // no data: the page says so
        }

        return d;
    }
}
