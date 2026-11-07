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
/// stats at level 45 (the website's fit; "Spiritmaster" is the meter's Elementalist). <see cref="PercentRef"/>: the offence at which the percentages of
/// the attack (Damage Boost, Double Chance ...) are worth their full weight, <see cref="GuardRef"/>: the guard at which those of the defence (Damage
/// Tolerance, Regeneration) are (see <see cref="CpTotals.Offense"/>); 0: they always are. <see cref="Own"/>: whether the meter's own weights count for the
/// stats the website's model has none for (<see cref="Aion2CpData.OwnWeights"/>). The CP measurements' upload counts plain amounts: no reference, no own weights.
/// </summary>
public sealed record CpModel(IReadOnlyDictionary<string, double> Weights, IReadOnlyDictionary<string, string> Confidence, bool FromWebsite, double IlMult = 0, double IlRef = 2500,
    IReadOnlyDictionary<string, double>? Base = null, double PercentRef = Aion2CpData.OffenseReference, double GuardRef = Aion2CpData.GuardReference, bool Own = true,
    CpLevels? Levels = null)
{
    /// <summary>
    /// The Combat Power of a skill at a level: by the model's tables (<see cref="Levels"/>, the weight of "skill" on top: 1 for the in-game figures); a model
    /// without tables (one from before 7 November 2026) counts every level the same, at its weight.
    /// </summary>
    public double SkillCp(bool passive, int level) => Levels is { } l ? (passive ? l.Passive : l.Active).CpAt(level) * Weight("skill") : Math.Max(0, level) * Weight("skill");

    /// <summary>The Combat Power of a Stigma at a level: by the model's table, or by the older bands (<see cref="Aion2CpSources.StigmaPoints"/>) without one.</summary>
    public double StigmaCp(int level) => (Levels is { } l ? l.Stigma.CpAt(level) : Aion2CpSources.StigmaPoints(level)) * Weight("stigma");

    /// <summary>How many Stigmas count: the highest ones of those slotted (4 in Global Season 1).</summary>
    public int StigmaSlots => Levels?.StigmaSlots ?? 4;

    /// <summary>The weight of a model stat: the website's, else the meter's own for the few the website's model has none for.</summary>
    public double Weight(string key) => Weights.TryGetValue(key, out double w) ? w : Own ? Aion2CpData.OwnWeights.GetValueOrDefault(key) : 0;

    /// <summary>Whether the model counts a model stat at all (the website's weight or the meter's own).</summary>
    public bool Counts(string key) => Weights.ContainsKey(key) || (Own && Aion2CpData.OwnWeights.ContainsKey(key));

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
/// One kind's levels -> Combat Power (the website's model "levels", src/features.js LEVELS): <see cref="Per"/> a level up to <see cref="To"/>, the amount of
/// <see cref="At"/> on reaching one of its levels, <see cref="Above"/> a level past <see cref="To"/>, and <see cref="Beyond"/> a level past <see cref="Cap"/>.
/// Level 1 counts nothing.
/// </summary>
public sealed record CpLevelTable(double Per, int To, IReadOnlyDictionary<int, double> At, double Above, int? Cap = null, double? Beyond = null)
{
    /// <summary>What a level adds on reaching it.</summary>
    public double Level(int level) => level < 2 ? 0 : At.TryGetValue(level, out double a) ? a : level <= To ? Per : Cap is int cap && level > cap ? Beyond ?? Above : Above;

    /// <summary>The Combat Power of the levels up to <paramref name="level"/>.</summary>
    public double CpAt(int level)
    {
        double cp = 0;
        for (int l = 2; l <= level; l++)
        {
            cp += Level(l);
        }

        return cp;
    }
}

/// <summary>
/// Skill and Stigma levels -> Combat Power, measured in game (7 November 2026; the website's 8,700 level 45 characters agree, a fitted weight of 1.0 to 1.1).
/// An equipped active skill: 100 a level, and 500 / 900 / 1,000 on reaching 12 / 16 / 20 (19 to 20 is +1,000), 400 a level past 20. A passive: 100 a level to
/// 16, 200 from 17 to 20, 400 past 20. A Stigma: 100 a level and 491 / 1,200 / 1,900 / 2,800 on reaching 5 / 10 / 15 / 20; the four highest slotted count.
/// </summary>
public sealed record CpLevels(CpLevelTable Active, CpLevelTable Passive, CpLevelTable Stigma, int StigmaSlots = 4)
{
    public static readonly CpLevels InGame = new(
        new CpLevelTable(100, 20, new Dictionary<int, double> { [12] = 500, [16] = 900, [20] = 1000 }, 400),
        new CpLevelTable(100, 16, new Dictionary<int, double>(), 200, 20, 400),
        new CpLevelTable(100, 25, new Dictionary<int, double> { [5] = 491, [10] = 1200, [15] = 1900, [20] = 2800 }, 100));
}

/// <summary>
/// The character's numbers the CP of a line depends on, counted the way the website's model counts them: the totals some lines take a share of (Might:
/// 0.1 % of Attack a point; "Attack increase" 1 % of it per 100 ...), the increases that raise every point of Attack, Critical Hit, Accuracy and HP
/// (Attack = points x (1 + Attack increase)), the progression factor of the character's Item Level, and the factors measured on the character itself
/// (<see cref="AionDPS.Combat.CpChecks"/>: the game's own Combat Power before and after a change against what the model said, by the model's stat).
/// <see cref="Split"/>: the totals of the whole character (<see cref="AionDPS.Combat.CpWhole"/>), where every line's CP is its share of the character's
/// Combat Power: a point of Attack and the increase that raises it share what they make together half and half, so all lines add up to the total
/// (without it a line's CP is what it adds on its own, and an Attack line and a Might line both count the Attack the Might raises).
/// <see cref="Offense"/>: the CP of the character's offence points (Attack with its increase, Critical Hit, Accuracy, Critical Attack, PvE and Boss
/// Attack, at the model's weights); a percentage of the attack (Damage Boost, PvE Damage Boost, Double Chance, Combat Speed ...) multiplies that offence, so
/// it is worth as much as the offence is: its weight at <see cref="CpModel.PercentRef"/>, proportionally less below (<see cref="Aion2CpData.PercentFactor"/>).
/// <see cref="Guard"/>: the same for the defence, the CP of the character's HP and Defense points, which a percentage of the defence (Damage Tolerance,
/// Regeneration) counts with (<see cref="Aion2CpData.GuardFactor"/>). A point of a stat is worth the same on every character: only percentages scale.
/// 0: not known, the weights as they are.
/// </summary>
public sealed record CpTotals(double Attack, double Critical, double Accuracy, double Hp, double Defense, double AttackIncrease = 0, double CritIncrease = 0,
    double AccuracyIncrease = 0, double HpIncrease = 0, double Scale = 1, IReadOnlyDictionary<string, double>? Factors = null, double DefenseIncrease = 0, bool Split = false,
    double Offense = 0, double Guard = 0)
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
    /// <summary>
    /// The offence (<see cref="CpTotals.Offense"/>, from the game's own stat list) at which the percentages of the attack are worth the model's full
    /// weight. Measured in the game (4 Nov 2026): on a character of 72,600 offence 1.5 % PvE Damage Boost from new titles raised Combat Power by 327, 218 a
    /// percent against the 507 the model's weight gives at its Item Level, 0.43 of it: about 170,000. The four characters of that account on its server
    /// share their Genus Insight boards line for line (the game counts them in full on each, as their stat lists show), and the level 20 one of 18,499
    /// Combat Power and 11,200 offence has no room for the 13,300 CP the full weights would give its percentages (900 at its offence).
    /// </summary>
    public const double OffenseReference = 170_000;

    /// <summary>The guard (<see cref="CpTotals.Guard"/>, from the game's own stat list) the meter's own weights of the percentages of the defence were
    /// measured at: 15,700 (21,000 HP and 12,300 Defense with the armour's) on the character of <see cref="OffenseReference"/>.</summary>
    public const double GuardReference = 15_700;

    /// <summary>The model stats of the percentages of the attack: they multiply the offence (<see cref="PercentFactor"/>).</summary>
    public static readonly IReadOnlySet<string> PercentKeys = new HashSet<string>(StringComparer.Ordinal) { "dmg", "wdb", "cdmg", "front", "cspd", "multi", "perf", "dbl", "pve", "cdr" };

    /// <summary>The model stats of the percentages of the defence (Damage Tolerance, Regeneration): they count with the guard (<see cref="GuardFactor"/>).</summary>
    public static readonly IReadOnlySet<string> GuardKeys = new HashSet<string>(StringComparer.Ordinal) { "dmgRed", "regen" };

    /// <summary>
    /// The meter's own weights for stats the website's model has none for, measured in the game: 13 Genus Insight Analyses in a row on one board
    /// (3 Nov 2026), each changing three lines and the game's Combat Power at once; the 13 changes fit to within a CP each, Critical Hit, PvE Accuracy and
    /// PvE Defense coming out at 7.9, 6.0 and 0.19 a point besides. Damage Tolerance ("dmgRed") 151 CP a percent, Regeneration ("regen") 19 a percent, MP
    /// 0.27 a point, Front Attack Defense and Critical Damage Defense 0.09, Back Attack Critical Resist 0.48 - at Item Level 2,500 (the model's base) and,
    /// the percentages, at <see cref="GuardReference"/>. The website's weight wins once it has one.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, double> OwnWeights = new Dictionary<string, double>(StringComparer.Ordinal)
    {
        ["dmgRed"] = 151, ["regen"] = 19, ["mp"] = 0.27, ["frontDef"] = 0.09, ["critDef"] = 0.09, ["backCritRes"] = 0.48,
        // Move Speed (a wing's line): about 100 CP a percent, measured in game by players (7 November 2026)
        ["move"] = 100,
    };

    /// <summary>How much of its weight a percentage of the attack is worth on this character: its offence against the model's reference (5 % at the least,
    /// 150 % at most); 1 when the offence isn't known or the model counts plain amounts.</summary>
    public static double PercentFactor(CpTotals totals, CpModel model) =>
        model.PercentRef > 0 && totals.Offense > 0 ? Math.Clamp(totals.Offense / model.PercentRef, 0.05, 1.5) : 1;

    /// <summary>How much of its weight a percentage of the defence is worth on this character: its guard against the reference (5 % to 150 %); 1 when
    /// the guard isn't known or the model counts plain amounts.</summary>
    public static double GuardFactor(CpTotals totals, CpModel model) =>
        model.GuardRef > 0 && totals.Guard > 0 ? Math.Clamp(totals.Guard / model.GuardRef, 0.05, 1.5) : 1;

    /// <summary>The offence (<see cref="CpTotals.Offense"/>) of points per model stat ("atk", "crit", "acc", "critAtk", "pveAtk") and the increases that raise
    /// Attack, Critical Hit and Accuracy.</summary>
    public static double OffenseOf(Func<string, double> points, double attackIncrease, double critIncrease, double accuracyIncrease, CpModel model) =>
        points("atk") * model.Weight("atk") * (1 + attackIncrease) + points("crit") * model.Weight("crit") * (1 + critIncrease)
        + points("acc") * model.Weight("acc") * (1 + accuracyIncrease) + points("critAtk") * model.Weight("critAtk") + points("pveAtk") * model.Weight("pveAtk");

    /// <summary>The guard (<see cref="CpTotals.Guard"/>) of HP and Defense points and the increases that raise them.</summary>
    public static double GuardOf(double hp, double defense, double hpIncrease, double defenseIncrease, CpModel model) =>
        hp * model.Weight("hp") * (1 + hpIncrease) + defense * model.Weight("def") * (1 + defenseIncrease);

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
    /// is raised by the character's increase of it; a percentage is worth as much as the character's offence (Damage Boost, Double Chance ...:
    /// <see cref="PercentFactor"/>) or guard (Damage Tolerance: <see cref="GuardFactor"/>); every stat by the progression factor and the factor measured
    /// on the character.
    /// </summary>
    public static double? Cp(string token, double value, CpTotals totals, CpModel? model = null) =>
        CpParts(token, value, totals, model) is { } parts ? parts.Sum(p => p.Cp) : null;

    /// <summary>The CP of a stat value split by the model stats it counts towards (Precision raises Critical Hit and Accuracy); null when Combat Power
    /// doesn't count the stat.</summary>
    public static IReadOnlyList<(string Key, double Cp)>? CpParts(string token, double value, CpTotals totals, CpModel? model = null)
    {
        model ??= Model;
        double percent = PercentFactor(totals, model), guard = GuardFactor(totals, model);
        double At(string key) => totals.Scale * totals.Factor(key) * (PercentKeys.Contains(key) ? percent : GuardKeys.Contains(key) ? guard : 1);
        if (D.Map.TryGetValue(token, out var map))
        {
            if (!model.Counts(map.Key))
            {
                return null;
            }

            double w = model.Weight(map.Key);

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
            _ => new[] { (share.Total, part * model.Weight(share.Total) * At(share.Total)) }, // percentage points of Combat Speed, Perfect, Double, Cooldown Reduction (with the offence)
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
            // "levels": the model with its skill and Stigma tables (the site gives the app from before them the model it was made for)
            string js = await http.GetStringAsync(Brand.Site + "/armory/assets/cp-model-data.js?levels=1").ConfigureAwait(false);
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

        // the website may set the references one day
        return new CpModel(weights, confidence, fromWebsite, Number("ilMult", 0), Number("ilRef", 2500), bases, Number("percentRef", OffenseReference), Number("guardRef", GuardReference),
            Levels: root.TryGetProperty("levels", out JsonElement lv) && lv.ValueKind == JsonValueKind.Object ? ReadLevels(lv) : null);
    }

    /// <summary>The model's skill and Stigma tables ({active, passive, stigma: {per, to, at: {level: amount}, above, cap?, beyond?}, slots}); null when one is missing or broken.</summary>
    internal static CpLevels? ReadLevels(JsonElement e)
    {
        static CpLevelTable? Table(JsonElement t)
        {
            if (t.ValueKind != JsonValueKind.Object || !t.TryGetProperty("per", out JsonElement per) || per.ValueKind != JsonValueKind.Number
                || !t.TryGetProperty("to", out JsonElement to) || to.ValueKind != JsonValueKind.Number || !t.TryGetProperty("above", out JsonElement above) || above.ValueKind != JsonValueKind.Number)
            {
                return null;
            }

            var at = new Dictionary<int, double>();
            if (t.TryGetProperty("at", out JsonElement a) && a.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty p in a.EnumerateObject())
                {
                    if (int.TryParse(p.Name, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int level) && p.Value.ValueKind == JsonValueKind.Number)
                    {
                        at[level] = p.Value.GetDouble();
                    }
                }
            }

            int? cap = t.TryGetProperty("cap", out JsonElement c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : null;
            double? beyond = t.TryGetProperty("beyond", out JsonElement b) && b.ValueKind == JsonValueKind.Number ? b.GetDouble() : null;
            return new CpLevelTable(per.GetDouble(), to.GetInt32(), at, above.GetDouble(), cap, beyond);
        }

        if (!e.TryGetProperty("active", out JsonElement act) || !e.TryGetProperty("passive", out JsonElement pas) || !e.TryGetProperty("stigma", out JsonElement stg)
            || Table(act) is not { } active || Table(pas) is not { } passive || Table(stg) is not { } stigma)
        {
            return null;
        }

        int slots = stg.TryGetProperty("slots", out JsonElement sl) && sl.ValueKind == JsonValueKind.Number ? Math.Clamp(sl.GetInt32(), 1, 12) : 4;
        return new CpLevels(active, passive, stigma, slots);
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
