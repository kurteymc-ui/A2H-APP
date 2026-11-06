using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Combat;

/// <summary>Where a part of Combat Power comes from, in the order the calculator shows them.</summary>
public enum CpSource
{
    Gear,
    Stigmas,
    Skills,
    Daevanion,
    Genus,
    Pets,
    Titles,
    Wings,
    Arcana,
    Attributes,
    Base,
}

/// <summary>Everything of a character the calculator counts: what the game sent at login (and since).</summary>
/// <param name="ClassName">The meter's class name (Templar ...): the class base of the model.</param>
/// <param name="Attributes">The attribute points and deity values the game counts (stat frame ids 1..17); null when they didn't come.</param>
/// <param name="TitlesOwned">The ids of every title the character owns; null when the login list didn't come (then the worn titles' "owned" stats count, as on the website).</param>
/// <param name="WingsOwned">The ids of every wing the character owns; null when unknown.</param>
/// <param name="Held">How many of an item the character holds (bag, storage, the account's storage); null when unknown (then nothing counts as held).</param>
/// <param name="Stats">The game's own stat list of the character (the stat frame), for the offence and guard its percentages count with
/// (<see cref="CpCalc.Strength"/>); null: what the calculator's own lines add up to.</param>
public sealed record CpWholeInput(
    string? ClassName,
    IReadOnlyList<Aion2EquippedItem> Gear,
    CpCalc.StonesOf? StonesOf = null,
    IReadOnlyDictionary<int, int>? Attributes = null,
    IReadOnlyList<Aion2SkillEntry>? Skills = null,
    IReadOnlyList<Aion2DaevanionBoard>? Daevanion = null,
    IReadOnlyList<Aion2TitleSlot>? TitlesWorn = null,
    IReadOnlyList<int>? TitlesOwned = null,
    int? WingId = null,
    IReadOnlyList<int>? WingsOwned = null,
    IReadOnlyList<Aion2SpeciesKnowledge>? Genus = null,
    IReadOnlyList<Aion2PetState>? Pets = null,
    IReadOnlyDictionary<string, double>? Factors = null,
    Func<int, long>? Held = null,
    IReadOnlyDictionary<int, int>? Stats = null);

/// <summary>A stat a source gives: its token, its value (the game's units) and its CP, its share of the character's Combat Power (null: CP doesn't count it).</summary>
public sealed record CpSourceLine(string Stat, double Value, double? Cp);

/// <summary>A skill as the calculator counts it: its level, the trained level, active or passive, whether its levels count (an active skill only on the bar),
/// their CP, what one more level gives, its specialisation slots (open, filled) and the level that opens the next one.</summary>
public sealed record CpSkillRow(int SkillId, int Level, int BaseLevel, bool Passive, bool Counted, double Cp, double PerLevel, int SpecOpen, int SpecFilled, int? NextSpecAt);

/// <summary>A Stigma: its level, whether it is equipped (only those count), its CP and what its next level gives (0 at the top).</summary>
public sealed record CpStigmaRow(int SkillId, int Level, bool Equipped, double Cp, double NextLevel, int SpecFilled);

/// <summary>A Daevanion board: the nodes opened and all its nodes (the start aside), what the open stat nodes give, their CP, the CP of the stat nodes still
/// closed, and the skill levels its nodes add (they count with the skills).</summary>
public sealed record CpBoardRow(int BoardId, int Open, int Total, IReadOnlyList<CpSourceLine> Lines, double Cp, double CpLeft, int ClosedStatNodes, int SkillLevels);

/// <summary>
/// A line of a Genus Insight board: its slot (1..9), grade (1 Common .. 5 Heroic), stat, value and CP; what a new Analysis of the slot gives on average at the
/// board's Insight level (the CP it adds on its own), how much this line gives the same way, the chance a new one beats it, and the best a new one can give.
/// </summary>
public sealed record CpGenusLine(int Slot, int Grade, string Stat, double Value, double? Cp, double Now, double Average, double Better, double Best);

/// <summary>A Genus Insight board: species id, the tables' key (Intellect ...), Insight level, the presets the game keeps (the first counts), its lines and CP.</summary>
public sealed record CpGenusRow(int SpeciesId, string Key, int Level, int Presets, IReadOnlyList<CpGenusLine> Lines, double Cp);

/// <summary>A genus of the pet collection: owned pets at level 1, 2 and 3, the genus's pets in the game, what all of them give and its CP, and what one more
/// step gives: a new pet (index 0), a pet from level 1 to 2 (1) and from 2 to 3 (2).</summary>
public sealed record CpPetRow(string Key, int[] ByLevel, int InGame, IReadOnlyList<CpSourceLine> Lines, double Cp, double[] StepGain);

/// <summary>A worn title: slot (1 Attack, 2 Defense, 3 Etc), what it gives while worn and its CP.</summary>
public sealed record CpTitleRow(int TitleId, int Slot, IReadOnlyList<CpSourceLine> Lines, double Cp);

/// <summary>The equipped wing: what it gives and its CP.</summary>
public sealed record CpWingRow(int WingId, IReadOnlyList<CpSourceLine> Lines, double Cp);

/// <summary>An Arcana card: its slot and item, its level, the deity value it carries and that value's CP, and the skill levels it adds (they count with the skills).</summary>
public sealed record CpArcanaRow(int Slot, int ItemId, int Level, string? GodStat, int GodValue, double Cp, int SkillLevels);

/// <summary>An attribute or deity value: its total and where its points come from (gear, pets, Arcana, the rest), and its CP.</summary>
public sealed record CpAttributeRow(int Id, string Stat, double Total, double FromGear, double FromPets, double FromArcana, double Other, double Cp);

public enum CpStepKind
{
    Enhance,
    Breakthrough,
    Reroll,
    Stone,
    BetterItem,
    Stigma,
    Skill,
    Genus,
    PetLevel,
    PetNew,
    Title,
    Wing,
    Daevanion,

    /// <summary>A crafted piece made into the next one by a transfer craft, its levels carried over (<see cref="CpTransfer"/>).</summary>
    Transfer,
}

/// <summary>How the steps are grouped: what costs nothing (titles, wings, gear the character already has), gear, Stigmas and skills, Genus Insight and the
/// pet collection, Daevanion.</summary>
public enum CpStepGroup
{
    Free,
    Gear,
    Skills,
    Collection,
    Daevanion,
}

/// <summary>An item a step takes and how many (an average for a level that can fail).</summary>
public sealed record CpMaterial(int ItemId, double Count);

/// <summary>
/// What a step costs: Kina and items. For a level that can fail, the average over the tries it takes (<see cref="Tries"/>), the points a failure adds to the
/// next try counted in; <see cref="Average"/> then is true.
/// </summary>
public sealed record CpCost(double Kina, IReadOnlyList<CpMaterial> Materials, bool Average = false, double Tries = 1)
{
    public static readonly CpCost Free = new(0, Array.Empty<CpMaterial>());

    public bool IsFree => Kina <= 0 && Materials.Count == 0;
}

/// <summary>How sure a step's gain is: exact (the tables say it), by chance (breakthrough: it may fail), an average (a roll: it can come out better or worse) or a range
/// (a stone: its kind decides).</summary>
public enum CpCertainty
{
    Exact,
    Chance,
    Average,
    Range,
}

/// <summary>
/// One way to raise Combat Power: what it is, what it gives (<see cref="Gain"/>; for an average what it gives on average and <see cref="GainMax"/> at best, for a
/// range the least and the most), how sure that is, the chance (of success, or that a roll beats what is there), and what it is about: a piece (slot, item, the level it
/// goes to, the stats), a skill, a genus, a title or wing (<see cref="OtherId"/>), how many of the same step there are (<see cref="Count"/>) and the source's key.
/// </summary>
/// <param name="Cost">What it takes (null: the tables don't say, or it costs no Kina or items the calculator knows of).</param>
/// <param name="Held">The piece it names is one the character already has (bag or storage).</param>
/// <param name="ComboId">A transfer craft's "Splendent" piece, made instead of <see cref="OtherId"/> at <see cref="ComboChance"/> %.</param>
/// <param name="FromLevel">A Stigma's or skill's level now (the step takes it to <see cref="ToLevel"/>).</param>
/// <param name="Shards">The Stigma Shards a Stigma step takes.</param>
/// <param name="Order">The step's place in the class build's order (Stigmas and skills, see <see cref="CpPlan"/>; 1 first, 0: none).</param>
public sealed record CpStep(CpStepKind Kind, double Gain, double GainMax, CpCertainty Certainty, double? Chance = null, int Slot = 0, int ItemId = 0, int ToLevel = 0,
    string? FromStat = null, string? ToStat = null, int OtherId = 0, int Count = 1, string? Key = null, CpCost? Cost = null, bool Held = false, int ComboId = 0,
    double? ComboChance = null, int FromLevel = 0, int Shards = 0, int Order = 0)
{
    /// <summary>What the list ranks by: the gain, for an average the average.</summary>
    public double Rank => Certainty == CpCertainty.Range ? (Gain + GainMax) / 2 : Gain;

    /// <summary>The group the step is shown in.</summary>
    public CpStepGroup Group => Kind switch
    {
        CpStepKind.Title or CpStepKind.Wing => CpStepGroup.Free,
        CpStepKind.BetterItem when Held => CpStepGroup.Free,
        CpStepKind.Stigma or CpStepKind.Skill => CpStepGroup.Skills,
        CpStepKind.Genus or CpStepKind.PetLevel or CpStepKind.PetNew => CpStepGroup.Collection,
        CpStepKind.Daevanion => CpStepGroup.Daevanion,
        _ => CpStepGroup.Gear,
    };

    /// <summary>CP per million Kina (null when the step costs no Kina).</summary>
    public double? PerMillion => Cost is { Kina: > 0 } c ? Rank / c.Kina * 1_000_000 : null;
}

/// <summary>The whole character in Combat Power: the parts by source (they add up to <see cref="Total"/>), each source's detail, and the ways to raise it.</summary>
public sealed class CpWholeResult
{
    public required CpTotals Totals { get; init; }

    public required double Scale { get; init; }

    public required IReadOnlyDictionary<CpSource, double> Parts { get; init; }

    public double Total => Parts.Values.Sum();

    public required IReadOnlyList<CpPiece> Pieces { get; init; }

    public required IReadOnlyList<CpSkillRow> Skills { get; init; }

    public required IReadOnlyList<CpStigmaRow> Stigmas { get; init; }

    public required IReadOnlyList<CpBoardRow> Boards { get; init; }

    public required IReadOnlyList<CpGenusRow> Genus { get; init; }

    public required IReadOnlyList<CpPetRow> Pets { get; init; }

    /// <summary>The pets closest to their next level: pet, level, progress, progress needed.</summary>
    public required IReadOnlyList<(int PetId, int Level, int Progress, int Needed)> PetsNearLevel { get; init; }

    public required IReadOnlyList<CpTitleRow> Titles { get; init; }

    /// <summary>What every owned title gives once owned, summed (null when the list of owned titles didn't come: then the worn titles' count, in <see cref="Titles"/>).</summary>
    public required IReadOnlyList<CpSourceLine>? OwnedTitles { get; init; }

    public required double OwnedTitlesCp { get; init; }

    public required int OwnedTitleCount { get; init; }

    public required CpWingRow? Wing { get; init; }

    public required IReadOnlyList<CpArcanaRow> Arcana { get; init; }

    public required IReadOnlyList<CpAttributeRow> Attributes { get; init; }

    public required IReadOnlyList<CpStep> Steps { get; init; }
}

/// <summary>
/// The CP calculator for the whole character: gear (<see cref="CpCalc"/>), Stigmas and skill levels, Daevanion, Genus Insight, the pet collection, titles worn and
/// owned, the wing, Arcana and the attribute points nothing of that explains, plus the class's base - every stat from the item data or the game's login lists,
/// its CP from the website's model (<see cref="Aion2CpData"/>). The parts add up to the total: a point of Attack and an increase that raises it (Might, "Attack
/// increase") share what they make together half and half (<see cref="CpTotals.Split"/>). A percentage is worth as much as the character's offence (Damage
/// Boost, Double Chance ...) or guard (Damage Tolerance), a point the same on every character (<see cref="CpTotals.Offense"/>): the same Genus Insight board,
/// which every character of an account on a server shares, gives a weaker character less. <see cref="Evaluate"/>'s scale fits the model to the game's own number
/// (the page passes the game's Combat Power at the last login against the calculator's at the same gear). What a step gives is what it adds on its own.
/// </summary>
public static class CpWhole
{
    /// <summary>The level a skill's specialisation slots open at (mastery skills: 8, 12, 20).</summary>
    public static readonly int[] SpecLevels = { 8, 12, 20 };

    /// <summary>The highest skill level the calculator offers a step to (the game shows skills up to 20).</summary>
    public const int SkillTop = 20;

    /// <summary>The highest Stigma level the calculator offers a step to: 20, the top any character the website read has reached (the model's bands go on to 25).</summary>
    public const int StigmaTop = 20;

    private readonly record struct Line(CpSource Source, string Key, string Stat, double Value);

    private static string SlotKey(int slot) => "slot:" + slot;

    /// <summary>The page of a board the game uses: the first (a second page is another preset of the board, not in use).</summary>
    private static int ActivePage(Aion2SpeciesKnowledge k) => k.Effects.Count == 0 ? 1 : k.Effects.Min(e => e.Page);

    /// <summary>The whole character in Combat Power. <paramref name="scale"/>: every part but the class base is multiplied by it (1: the model as it is).</summary>
    public static CpWholeResult Evaluate(CpWholeInput input, CpModel? model = null, double scale = 1)
    {
        model ??= Aion2CpData.Model;
        var zero = new CpTotals(0, 0, 0, 0, 0);
        var lines = new List<Line>();

        // gear: the tables' lines (a stone the character page doesn't name counts at the middle of its colour's range)
        foreach (CpPiece piece in CpCalc.Pieces(input.Gear, zero, model, input.StonesOf))
        {
            foreach (CpLine l in piece.Lines.Where(l => !l.IsSkill)) // a skill line raises a skill's level: counted with the skills
            {
                lines.Add(new Line(CpSource.Gear, SlotKey(piece.Slot), l.Stat, (l.Value + l.ValueMax) / 2));
            }
        }

        // Daevanion: the open stat nodes (skill nodes raise skill levels, which count with the skills)
        foreach (Aion2DaevanionBoard board in input.Daevanion ?? Array.Empty<Aion2DaevanionBoard>())
        {
            foreach (int nodeId in board.NodeIds)
            {
                if (Aion2DaevanionCatalog.Find(nodeId) is { Type: "Stat" } node)
                {
                    lines.Add(new Line(CpSource.Daevanion, "board:" + board.BoardId, node.Key, node.Value));
                }
            }
        }

        // titles: the worn ones while worn; once owned, every owned one (the worn ones' when the list of owned titles didn't come, as the website counts them)
        var worn = (input.TitlesWorn ?? Array.Empty<Aion2TitleSlot>()).Where(t => Aion2CpSources.Title(t.TitleId) is not null).GroupBy(t => t.Slot).Select(g => g.First()).ToList();
        foreach (Aion2TitleSlot t in worn)
        {
            foreach (var (stat, value) in Aion2CpSources.Title(t.TitleId)!.Equip)
            {
                lines.Add(new Line(CpSource.Titles, "title:" + t.TitleId, stat, value));
            }
        }

        IReadOnlyList<int> owned = input.TitlesOwned ?? worn.Select(t => t.TitleId).ToList();
        foreach (int id in owned.Distinct())
        {
            foreach (var (stat, value) in Aion2CpSources.Title(id)?.Owned ?? new Dictionary<string, int>())
            {
                lines.Add(new Line(CpSource.Titles, "owned", stat, value));
            }
        }

        // the wing
        if (input.WingId is int wingId && Aion2CpSources.Wing(wingId) is { } wing)
        {
            foreach (var (stat, value) in wing.Stats)
            {
                lines.Add(new Line(CpSource.Wings, "wing:" + wingId, stat, value));
            }
        }

        // Genus Insight: the board's page in use
        foreach (Aion2SpeciesKnowledge k in input.Genus ?? Array.Empty<Aion2SpeciesKnowledge>())
        {
            int page = ActivePage(k);
            foreach (Aion2SpeciesEffect e in k.Effects.Where(e => e.Page == page))
            {
                if (Aion2CpData.Token(e.StatId) is { } token)
                {
                    lines.Add(new Line(CpSource.Genus, $"genus:{k.SpeciesId}:{e.Slot}", token, e.Value));
                }
            }
        }

        // the pet collection: every pet gives what its genus gives at its level
        foreach (Aion2PetState pet in input.Pets ?? Array.Empty<Aion2PetState>())
        {
            if (Aion2CpSources.GenusKeyOfPetSpecies(Aion2Pets.SpeciesOf(pet.PetId)) is { } genus)
            {
                foreach (var (stat, value) in Aion2CpSources.PetStats(genus, pet.Level))
                {
                    lines.Add(new Line(CpSource.Pets, "pets:" + genus, stat, value));
                }
            }
        }

        // Arcana: the deity value each card carries
        foreach (Aion2EquippedItem item in input.Gear)
        {
            if (item.Arcana is { GodValue: > 0 } a && Aion2CpData.Token(a.GodStatId) is { } token)
            {
                lines.Add(new Line(CpSource.Arcana, SlotKey(item.SlotIndex), token, a.GodValue));
            }
        }

        // the attribute points the game counts that none of the above explains (Daevanion's deity values, collections ...)
        if (input.Attributes is { } attributes)
        {
            foreach (var (id, total) in attributes.Where(kv => kv.Key is >= Aion2Attributes.FirstId and <= Aion2Attributes.LastId).OrderBy(kv => kv.Key))
            {
                if (Aion2CpData.Token(id) is not { } token)
                {
                    continue;
                }

                double known = lines.Where(l => l.Stat == token).Sum(l => l.Value);
                if (total - known > 0.5)
                {
                    lines.Add(new Line(CpSource.Attributes, "attr:" + id, token, total - known));
                }
            }
        }

        // the totals: points per model stat, the increases that raise them
        double Points(string key) => lines.Sum(l => Aion2CpData.MapOf(l.Stat) is { } m && m.Key == key ? l.Value / m.Unit : 0);
        double Increase(string key) => lines.Sum(l => Aion2CpData.ShareOf(l.Stat) is { } s && (s.Total == key || (s.Total == "critacc" && key is "crit" or "acc")) ? s.Per * l.Value : 0);
        var (offense, guard) = input.Stats is { Count: > 0 } statList ? CpCalc.Strength(statList, model)
            : (Aion2CpData.OffenseOf(Points, Increase("atk"), Increase("crit"), Increase("acc"), model), Aion2CpData.GuardOf(Points("hp"), Points("def"), Increase("hp"), Increase("def"), model));
        var totals = new CpTotals(Points("atk"), Points("crit"), Points("acc"), Points("hp"), Points("def"), Increase("atk"), Increase("crit"), Increase("acc"), Increase("hp"),
            scale, input.Factors, Increase("def"), Split: true, Offense: offense, Guard: guard);
        CpTotals marginal = totals.Marginal;
        double Cp(string stat, double value) => Aion2CpData.Cp(stat, value, totals, model) ?? 0;
        double? CpOrNull(string stat, double value) => Aion2CpData.Cp(stat, value, totals, model);
        double Adds(string stat, double value) => Aion2CpData.Cp(stat, value, marginal, model) ?? 0;
        double AddsAll(IReadOnlyDictionary<string, int> stats) => stats.Sum(kv => Adds(kv.Key, kv.Value));
        IReadOnlyList<CpSourceLine> Summed(IEnumerable<Line> of) => of.GroupBy(l => l.Stat).Select(g => new CpSourceLine(g.Key, g.Sum(l => l.Value), CpOrNull(g.Key, g.Sum(l => l.Value))))
            .OrderByDescending(l => l.Cp ?? -1).ThenBy(l => l.Stat, StringComparer.Ordinal).ToList();

        var parts = Enum.GetValues<CpSource>().ToDictionary(s => s, _ => 0.0);
        foreach (Line l in lines.Where(l => l.Source != CpSource.Gear))
        {
            parts[l.Source] += Cp(l.Stat, l.Value);
        }

        IReadOnlyList<CpPiece> pieces = CpCalc.Pieces(input.Gear, totals, model, input.StonesOf);
        parts[CpSource.Gear] = pieces.Sum(p => p.Mid);
        parts[CpSource.Base] = model.BaseOf(input.ClassName);

        // skills and Stigmas: the levels of the equipped active skills and of all passives, each equipped Stigma by its level bands
        double perLevel = model.Weight("skill") * scale * totals.Factor("skill");
        double stigmaWeight = model.Weight("stigma") * scale * totals.Factor("stigma");
        var skills = new List<CpSkillRow>();
        var stigmas = new List<CpStigmaRow>();
        foreach (Aion2SkillEntry s in (input.Skills ?? Array.Empty<Aion2SkillEntry>()).GroupBy(s => s.SkillId).Select(g => g.First()))
        {
            string? kind = Aion2Artwork.SkillKind(s.SkillId);
            if (kind is null || s.Level <= 0)
            {
                continue;
            }

            int filled = s.Stones?.Count(x => x > 0) ?? 0;
            if (s.Stigma)
            {
                double now = s.Equipped ? Aion2CpSources.StigmaPoints(s.Level) * stigmaWeight : 0;
                double next = s.Level < StigmaTop ? (Aion2CpSources.StigmaPoints(s.Level + 1) - Aion2CpSources.StigmaPoints(s.Level)) * stigmaWeight : 0;
                stigmas.Add(new CpStigmaRow(s.SkillId, s.Level, s.Equipped, now, next, filled));
                continue;
            }

            bool passive = kind == "p";
            bool counted = passive || s.Equipped;
            int open = passive ? 0 : SpecLevels.Count(l => s.Level >= l);
            int? nextAt = passive ? null : SpecLevels.Where(l => s.Level < l).Select(l => (int?)l).FirstOrDefault();
            skills.Add(new CpSkillRow(s.SkillId, s.Level, s.BaseLevel, passive, counted, counted ? s.Level * perLevel : 0, perLevel, open, filled, nextAt));
        }

        parts[CpSource.Skills] = skills.Sum(s => s.Cp);
        parts[CpSource.Stigmas] = stigmas.Sum(s => s.Cp);

        // a piece's skill lines: what their levels are worth with the skill (counted there, so not in the piece's own sum)
        var countedSkills = skills.Where(s => s.Counted).Select(s => s.SkillId).ToHashSet();
        pieces = pieces.Select(p => !p.Lines.Any(l => l.IsSkill) ? p : p with
        {
            Lines = p.Lines.Select(l => l.IsSkill ? l with { SkillCp = countedSkills.Contains(l.SkillId) || countedSkills.Contains(Aion2Cooldowns.BaseOf(l.SkillId)) ? l.Value * perLevel : 0 } : l).ToList(),
        }).ToList();

        // Daevanion boards
        var boards = new List<CpBoardRow>();
        foreach (Aion2DaevanionBoard board in input.Daevanion ?? Array.Empty<Aion2DaevanionBoard>())
        {
            var open = board.NodeIds.ToHashSet();
            var all = Aion2DaevanionCatalog.NodesOfBoard(board.BoardId).Where(n => n.Type != "Start").ToList();
            var closed = all.Where(n => !open.Contains(n.Id) && n.Type == "Stat").ToList();
            int skillLevels = all.Where(n => open.Contains(n.Id) && n.Type == "SkillLevel").Sum(n => n.Value);
            var of = lines.Where(l => l.Source == CpSource.Daevanion && l.Key == "board:" + board.BoardId).ToList();
            boards.Add(new CpBoardRow(board.BoardId, all.Count(n => open.Contains(n.Id)), all.Count, Summed(of), of.Sum(l => Cp(l.Stat, l.Value)),
                closed.Sum(n => Adds(n.Key, n.Value)), closed.Count, skillLevels));
        }

        // Genus Insight
        var genusRows = new List<CpGenusRow>();
        foreach (Aion2SpeciesKnowledge k in (input.Genus ?? Array.Empty<Aion2SpeciesKnowledge>()).OrderBy(k => k.SpeciesId))
        {
            CpGenusData? data = Aion2CpSources.GenusOfSpecies(k.SpeciesId);
            int page = ActivePage(k);
            var rowLines = new List<CpGenusLine>();
            foreach (Aion2SpeciesEffect e in k.Effects.Where(e => e.Page == page).OrderBy(e => e.Slot))
            {
                string stat = Aion2CpData.Token(e.StatId) ?? "#" + e.StatId;
                double now = Adds(stat, e.Value);
                var (average, better, best) = data is null ? (0.0, 0.0, 0.0) : Analysis(data, e.Slot + 1, k.Level, now, Adds);
                rowLines.Add(new CpGenusLine(e.Slot + 1, e.Kind, stat, e.Value, CpOrNull(stat, e.Value), now, average, better, best));
            }

            genusRows.Add(new CpGenusRow(k.SpeciesId, data?.Key ?? "", k.Level, k.Effects.Select(e => e.Page).Distinct().Count(), rowLines, rowLines.Sum(l => l.Cp ?? 0)));
        }

        // the pet collection by genus
        var petRows = new List<CpPetRow>();
        var petsNow = input.Pets ?? Array.Empty<Aion2PetState>();
        foreach (var (key, speciesId) in Aion2CpSources.Genera)
        {
            string species = Aion2Pets.Species[speciesId - 2];
            var mine = petsNow.Where(p => Aion2Pets.SpeciesOf(p.PetId) == species).ToList();
            int inGame = Aion2Pets.AllPetIds.Count(id => Aion2Pets.SpeciesOf(id) == species);
            int[] byLevel = { mine.Count(p => p.Level == 1), mine.Count(p => p.Level == 2), mine.Count(p => p.Level >= 3) };
            var of = lines.Where(l => l.Source == CpSource.Pets && l.Key == "pets:" + key).ToList();
            IReadOnlyDictionary<string, int> At(int level) => Aion2CpSources.PetStats(key, level);
            double Step(int from, int to) => AddsAll(Difference(At(to), At(from)));
            petRows.Add(new CpPetRow(key, byLevel, Math.Max(inGame, mine.Count), Summed(of), of.Sum(l => Cp(l.Stat, l.Value)), new[] { Step(0, 1), Step(1, 2), Step(2, 3) }));
        }

        var nearLevel = petsNow.Where(p => p.Level is >= 1 and < Aion2Pets.TopLevel && Aion2Pets.ProgressNeeded(p.Level) > 0)
            .Select(p => (p.PetId, p.Level, p.Progress, Needed: Aion2Pets.ProgressNeeded(p.Level)))
            .OrderByDescending(p => (double)p.Progress / p.Needed).ThenBy(p => p.Needed - p.Progress).Take(6).ToList();

        // titles, the wing, Arcana, attributes
        var titles = worn.Select(t => new CpTitleRow(t.TitleId, t.Slot, Summed(lines.Where(l => l.Source == CpSource.Titles && l.Key == "title:" + t.TitleId)),
            lines.Where(l => l.Source == CpSource.Titles && l.Key == "title:" + t.TitleId).Sum(l => Cp(l.Stat, l.Value)))).ToList();
        var ownedLines = lines.Where(l => l.Source == CpSource.Titles && l.Key == "owned").ToList();
        CpWingRow? wingRow = input.WingId is int w2 && Aion2CpSources.Wing(w2) is not null
            ? new CpWingRow(w2, Summed(lines.Where(l => l.Source == CpSource.Wings)), parts[CpSource.Wings])
            : null;
        var arcana = input.Gear.Where(g => g.Arcana is not null).OrderBy(g => g.SlotIndex).Select(g =>
        {
            Aion2ArcanaDetails a = g.Arcana!;
            string? god = Aion2CpData.Token(a.GodStatId);
            return new CpArcanaRow(g.SlotIndex, g.ItemId, g.Enchant, god, a.GodValue, god is null ? 0 : Cp(god, a.GodValue), a.SkillBonuses.Sum(b => b.Level));
        }).ToList();
        var attributeRows = new List<CpAttributeRow>();
        for (int id = Aion2Attributes.FirstId; id <= Aion2Attributes.LastId; id++)
        {
            if (Aion2CpData.Token(id) is not { } token)
            {
                continue;
            }

            var of = lines.Where(l => l.Stat == token).ToList();
            double From(CpSource s) => of.Where(l => l.Source == s).Sum(l => l.Value);
            double total = of.Sum(l => l.Value);
            if (total > 0)
            {
                attributeRows.Add(new CpAttributeRow(id, token, total, From(CpSource.Gear), From(CpSource.Pets), From(CpSource.Arcana),
                    total - From(CpSource.Gear) - From(CpSource.Pets) - From(CpSource.Arcana), Cp(token, total)));
            }
        }

        var result = new CpWholeResult
        {
            Totals = totals,
            Scale = scale,
            Parts = parts,
            Pieces = pieces,
            Skills = skills.OrderByDescending(s => s.Counted).ThenBy(s => s.Passive).ThenByDescending(s => s.Level).ThenBy(s => s.SkillId).ToList(),
            Stigmas = stigmas.OrderByDescending(s => s.Equipped).ThenByDescending(s => s.Level).ThenBy(s => s.SkillId).ToList(),
            Boards = boards.OrderBy(b => b.BoardId).ToList(),
            Genus = genusRows,
            Pets = petRows,
            PetsNearLevel = nearLevel,
            Titles = titles,
            OwnedTitles = input.TitlesOwned is null ? null : Summed(ownedLines),
            OwnedTitlesCp = ownedLines.Sum(l => Cp(l.Stat, l.Value)),
            OwnedTitleCount = owned.Distinct().Count(),
            Wing = wingRow,
            Arcana = arcana,
            Attributes = attributeRows,
            Steps = Array.Empty<CpStep>(),
        };
        return Steps(result, input, model, Adds, AddsAll);
    }

    private static IReadOnlyDictionary<string, int> Difference(IReadOnlyDictionary<string, int> to, IReadOnlyDictionary<string, int> from) =>
        to.Keys.Union(from.Keys).ToDictionary(k => k, k => to.GetValueOrDefault(k) - from.GetValueOrDefault(k));

    /// <summary>
    /// What a new Analysis of a Genus Insight slot gives at an Insight level, the CP it adds on its own: on average (the grade by the level's odds, the property by its
    /// weight among the slot's properties of that grade, the value anywhere in its range), the chance it beats <paramref name="now"/>, and the best it can give.
    /// </summary>
    public static (double Average, double Better, double Best) Analysis(CpGenusData data, int slot, int level, double now, Func<string, double, double> adds)
    {
        double[] odds = data.OddsAt(level);
        double weightOfGrades = Enumerable.Range(1, 5).Where(g => data.Options(slot, g).Count > 0).Sum(g => odds[g - 1]);
        if (weightOfGrades <= 0)
        {
            return (0, 0, 0);
        }

        double average = 0, better = 0, best = 0;
        for (int grade = 1; grade <= 5; grade++)
        {
            IReadOnlyList<CpGenusOption> options = data.Options(slot, grade);
            double weights = options.Sum(o => o.Weight);
            if (options.Count == 0 || weights <= 0 || odds[grade - 1] <= 0)
            {
                continue;
            }

            double p = odds[grade - 1] / weightOfGrades;
            foreach (CpGenusOption o in options)
            {
                double q = p * o.Weight / weights;
                double perUnit = adds(o.Stat, 1);
                average += q * perUnit * (o.Min + o.Max) / 2.0;
                best = Math.Max(best, perUnit * o.Max);
                if (perUnit > 0)
                {
                    // the value is even anywhere in min..max: the share of it above the value that would give as much as the line now
                    double need = now / perUnit;
                    better += q * (o.Max <= o.Min ? (o.Max > need ? 1 : 0) : Math.Clamp((o.Max - need) / (o.Max - o.Min), 0, 1));
                }
            }
        }

        return (average, better, best);
    }

    /// <summary>The ways to raise Combat Power, the biggest first (an average by its average, a range by its middle); nothing under 1 CP.</summary>
    private static CpWholeResult Steps(CpWholeResult r, CpWholeInput input, CpModel model, Func<string, double, double> adds, Func<IReadOnlyDictionary<string, int>, double> addsAll)
    {
        var steps = new List<CpStep>();
        double Sum(IReadOnlyDictionary<string, int> stats) => addsAll(stats);

        // gear: enhancement, breakthrough, the weakest soul-binding line, empty stone slots, a crafted piece's transfer craft, a better piece of the same kind
        long Held(int itemId) => input.Held?.Invoke(itemId) ?? 0;
        char race = RaceOf(input.Gear);
        foreach (CpPiece piece in r.Pieces.GroupBy(p => p.Slot).Select(g => g.First()))
        {
            if (piece.Data is not { } data)
            {
                continue;
            }

            int maxEnchant = Aion2CpData.MaxEnchant(data.Group);
            if (piece.Enchant < maxEnchant)
            {
                double gain = Sum(Difference(Aion2CpData.Enchant(data.Group, piece.Enchant + 1), Aion2CpData.Enchant(data.Group, piece.Enchant)));
                CpTry? next = Aion2CpCosts.Enchant(piece.ItemId, piece.Enchant);
                bool sure = next is null || next.Chance >= 100;
                steps.Add(new CpStep(CpStepKind.Enhance, gain, gain, sure ? CpCertainty.Exact : CpCertainty.Chance, sure ? null : next!.Chance / 100, piece.Slot, piece.ItemId,
                    piece.Enchant + 1, Cost: CostOf(next)));
            }

            int maxExceed = Aion2CpData.MaxExceed(data.Group);
            if (piece.Enchant >= maxEnchant && maxEnchant > 0 && piece.Exceed < maxExceed)
            {
                double gain = Sum(Difference(Aion2CpData.Exceed(data.Group, piece.Exceed + 1), Aion2CpData.Exceed(data.Group, piece.Exceed)));
                CpTry? next = Aion2CpCosts.Exceed(piece.ItemId, piece.Exceed);
                double? chance = next is not null ? next.Chance / 100 : Aion2CpData.ExceedChance(data.Group, piece.Exceed + 1) is double c ? c / 100 : null;
                steps.Add(new CpStep(CpStepKind.Breakthrough, gain, gain, CpCertainty.Chance, chance, piece.Slot, piece.ItemId, piece.Exceed + 1, Cost: CostOf(next)));
            }

            // the soul-binding line that gives least, rolled again: a line of the piece's pool by its weight (not one of the other lines' stats), its value anywhere in range
            var souls = piece.Lines.Where(l => l.IsSoulStat).ToList();
            if (souls.Count > 0 && data.Pool.Count > 0)
            {
                CpLine weakest = souls.OrderBy(l => adds(l.Stat, l.Value)).First();
                double now = adds(weakest.Stat, weakest.Value);
                var others = souls.Where(l => !ReferenceEquals(l, weakest)).Select(l => l.Stat).ToHashSet(StringComparer.Ordinal);
                var pool = data.Pool.Where(p => !others.Contains(p.Stat)).ToList();
                double weights = pool.Sum(p => p.Weight);
                if (weights > 0)
                {
                    double average = 0, better = 0, best = 0;
                    string bestStat = weakest.Stat;
                    foreach (CpPoolLine p in pool)
                    {
                        double q = p.Weight / weights, perUnit = adds(p.Stat, 1);
                        average += q * perUnit * (p.Min + p.Max) / 2.0;
                        if (perUnit * p.Max > best)
                        {
                            best = perUnit * p.Max;
                            bestStat = p.Stat;
                        }

                        if (perUnit > 0)
                        {
                            double need = now / perUnit;
                            better += q * (p.Max <= p.Min ? (p.Max > need ? 1 : 0) : Math.Clamp((p.Max - need) / (p.Max - p.Min), 0, 1));
                        }
                    }

                    if (average - now >= 1)
                    {
                        steps.Add(new CpStep(CpStepKind.Reroll, average - now, best - now, CpCertainty.Average, better, piece.Slot, piece.ItemId, FromStat: weakest.Stat, ToStat: bestStat));
                    }
                }
            }

            // empty stone slots: the purple stone (the best colour) that gives most, its range over the kinds of purple stone
            if (piece.EmptyStones > 0)
            {
                var stone = Aion2CpData.StoneStats.Select(s => (Stat: s, Range: BestStone(s))).Where(s => s.Range is not null)
                    .Select(s => (s.Stat, Lo: adds(s.Stat, s.Range!.Value.Min), Hi: adds(s.Stat, s.Range!.Value.Max))).OrderByDescending(s => s.Hi).FirstOrDefault();
                if (stone.Stat is not null && stone.Hi >= 1)
                {
                    steps.Add(new CpStep(CpStepKind.Stone, stone.Lo * piece.EmptyStones, stone.Hi * piece.EmptyStones, CpCertainty.Range, Slot: piece.Slot, ItemId: piece.ItemId,
                        ToLevel: piece.EmptyStones, ToStat: stone.Stat));
                }
            }

            // what the piece gives now, and what another piece would give at the same levels (its own top levels at most)
            double mine = Sum(Total(data, piece.Enchant, piece.Exceed));
            double At(CpItemData other) => Sum(Total(other, Math.Min(piece.Enchant, Aion2CpData.MaxEnchant(other.Group)), Math.Min(piece.Exceed, Aion2CpData.MaxExceed(other.Group))));
            CpPieceCost? mineCost = Aion2CpCosts.Piece(piece.ItemId);
            bool maxed = piece.Enchant >= maxEnchant && piece.Exceed >= maxExceed;

            // a crafted piece is carried over into the next crafted one (its enhancement, amplification and soul binding go with it), not swapped
            IReadOnlyList<CpTransfer> transfers = mineCost is { Crafted: true } ? Aion2CpCosts.Transfers(piece.ItemId) : Array.Empty<CpTransfer>();
            CpStep? transfer = null;
            foreach (CpTransfer t in transfers)
            {
                if (Aion2CpData.Item(t.OutputId) is not { } output)
                {
                    continue;
                }

                double plain = At(output) - mine;
                double combo = Aion2CpData.Item(t.ComboId) is { } splendent ? At(splendent) - mine : plain;
                double c = Math.Clamp(t.ComboChance / 100, 0, 1);
                double expected = (1 - c) * plain + c * combo;
                if (expected >= 1 && (transfer is null || expected > transfer.Gain))
                {
                    var materials = t.Materials.Where(m => m.ItemId != 0 && m.Count > 0).Select(m => new CpMaterial(m.ItemId, m.Count)).ToList();
                    transfer = new CpStep(CpStepKind.Transfer, expected, Math.Max(plain, combo), c > 0 ? CpCertainty.Average : CpCertainty.Exact, c > 0 ? c : null, piece.Slot,
                        piece.ItemId, piece.Enchant, OtherId: t.OutputId, Cost: new CpCost(t.Kina, materials), Held: Held(t.OutputId) > 0, ComboId: t.ComboId,
                        ComboChance: c > 0 ? c : null);
                }
            }

            if (transfer is not null)
            {
                steps.Add(transfer);
                continue;
            }

            // a better piece of the same kind the character can wear: one it holds, any that isn't crafted, a crafted one only once this piece is at its top
            // (or for a crafted piece with nothing left to craft it into)
            var candidates = Aion2CpData.Items
                .Where(x => x.Category == data.Category && x.Id != data.Id && x.Level >= data.Level && RaceFits(x.Id, race))
                .Select(x => (Item: x, Held: Held(x.Id) > 0, Crafted: Aion2CpCosts.Piece(x.Id) is { Crafted: true }))
                .Where(x => x.Held || ((mineCost is not { Crafted: true } || maxed) && (!x.Crafted || maxed)))
                .Select(x => (x.Item, x.Held, Gain: At(x.Item) - mine))
                .Where(x => x.Gain >= 1)
                .ToList();
            var fromBag = candidates.Where(x => x.Held).OrderByDescending(x => x.Gain).FirstOrDefault();
            if (fromBag.Item is not null)
            {
                steps.Add(new CpStep(CpStepKind.BetterItem, fromBag.Gain, fromBag.Gain, CpCertainty.Exact, Slot: piece.Slot, ItemId: piece.ItemId, ToLevel: piece.Enchant,
                    OtherId: fromBag.Item.Id, Cost: CpCost.Free, Held: true));
            }

            var top = candidates.OrderByDescending(x => x.Gain).ThenBy(x => x.Item.Level).FirstOrDefault();
            if (top.Item is not null && !top.Held && top.Gain > fromBag.Gain + 0.5)
            {
                steps.Add(new CpStep(CpStepKind.BetterItem, top.Gain, top.Gain, CpCertainty.Exact, Slot: piece.Slot, ItemId: piece.ItemId, ToLevel: piece.Enchant,
                    OtherId: top.Item.Id));
            }
        }

        // Stigmas and skills: each one's next step along the class build's path (CpPlan: the build's order, the lower ranks first), in that order
        CpClassPlan? plan = CpPlan.For(input.ClassName);
        double stigmaWeight = model.Weight("stigma") * r.Scale * r.Totals.Factor("stigma");
        var worn = r.Stigmas.Where(s => s.Equipped && s.Level >= 1).ToList();
        int order = 0;
        var stigmaSeen = new HashSet<int>();
        foreach (CpLevelStep p in CpPlan.StigmaPath(worn.Select(s => CpPlan.Key(Aion2SkillNames.NameOf(s.SkillId))).ToList(), worn.Select(s => s.Level).ToList(), plan, StigmaTop))
        {
            if (!stigmaSeen.Add(p.Index))
            {
                continue;
            }

            CpStigmaRow s = worn[p.Index];
            double gain = (Aion2CpSources.StigmaPoints(p.To) - Aion2CpSources.StigmaPoints(p.From)) * stigmaWeight;
            // "build": a Stigma of the class build, not past the level the build takes it to
            bool inBuild = plan is not null && plan.StigmaGoals.TryGetValue(CpPlan.Key(Aion2SkillNames.NameOf(s.SkillId)), out int goal) && p.To <= goal;
            steps.Add(new CpStep(CpStepKind.Stigma, gain, gain, CpCertainty.Exact, OtherId: s.SkillId, ToLevel: p.To, FromLevel: p.From, Shards: CpPlan.ShardsTo(p.From, p.To),
                Order: ++order, Key: inBuild ? "build" : null));
        }

        // skill levels: every counted skill gives the same per level; the build says which first
        var counted = r.Skills.Where(s => s.Counted && s.PerLevel > 0).ToList();
        var skillSeen = new HashSet<int>();
        foreach (CpLevelStep p in CpPlan.SkillPath(counted.Select(s => CpPlan.Key(Aion2SkillNames.NameOf(s.SkillId))).ToList(), counted.Select(s => s.Level).ToList(), plan, SkillTop))
        {
            if (!skillSeen.Add(p.Index))
            {
                continue;
            }

            CpSkillRow s = counted[p.Index];
            double gain = (p.To - p.From) * s.PerLevel;
            bool inBuild = plan is not null && plan.SkillTargets.TryGetValue(CpPlan.Key(Aion2SkillNames.NameOf(s.SkillId)), out int target) && p.To <= target;
            steps.Add(new CpStep(CpStepKind.Skill, gain, gain, CpCertainty.Exact, OtherId: s.SkillId, ToLevel: p.To, FromLevel: p.From, Order: ++order, Key: inBuild ? "build" : null));
        }

        // Genus Insight: the line of each board a new Analysis beats on average by the most
        foreach (CpGenusRow g in r.Genus)
        {
            CpGenusLine? weak = g.Lines.Where(l => l.Average - l.Now >= 1).OrderByDescending(l => l.Average - l.Now).FirstOrDefault();
            if (weak is not null)
            {
                steps.Add(new CpStep(CpStepKind.Genus, weak.Average - weak.Now, weak.Best - weak.Now, CpCertainty.Average, weak.Better, weak.Slot, FromStat: weak.Stat,
                    OtherId: g.SpeciesId, Key: g.Key));
            }
        }

        // pets: the next level of a pet of each genus (2 -> 3 first), a pet not collected yet
        foreach (CpPetRow p in r.Pets)
        {
            if (p.ByLevel[1] > 0 && p.StepGain[2] >= 1)
            {
                steps.Add(new CpStep(CpStepKind.PetLevel, p.StepGain[2], p.StepGain[2], CpCertainty.Exact, ToLevel: 3, Count: p.ByLevel[1], Key: p.Key));
            }

            if (p.ByLevel[0] > 0 && p.StepGain[1] >= 1)
            {
                steps.Add(new CpStep(CpStepKind.PetLevel, p.StepGain[1], p.StepGain[1], CpCertainty.Exact, ToLevel: 2, Count: p.ByLevel[0], Key: p.Key));
            }

            int missing = p.InGame - p.ByLevel.Sum();
            if (missing > 0 && p.StepGain[0] >= 1)
            {
                steps.Add(new CpStep(CpStepKind.PetNew, p.StepGain[0], p.StepGain[0] + p.StepGain[1] + p.StepGain[2], CpCertainty.Exact, Count: missing, Key: p.Key));
            }
        }

        // titles: the owned title that gives most in each slot, against the worn one
        if (input.TitlesOwned is { Count: > 0 } ownedTitles)
        {
            string[] categories = { "Attack", "Defense", "Etc" };
            for (int slot = 1; slot <= 3; slot++)
            {
                string category = categories[slot - 1];
                int wornId = (input.TitlesWorn ?? Array.Empty<Aion2TitleSlot>()).FirstOrDefault(t => t.Slot == slot)?.TitleId ?? 0;
                double now = Aion2CpSources.Title(wornId) is { } wornTitle ? Sum(wornTitle.Equip) : 0;
                var best = ownedTitles.Distinct().Select(Aion2CpSources.Title).Where(t => t is not null && t.Category == category)
                    .Select(t => (Title: t!, Gives: Sum(t!.Equip))).OrderByDescending(t => t.Gives).FirstOrDefault();
                if (best.Title is not null && best.Title.Id != wornId && best.Gives - now >= 1)
                {
                    steps.Add(new CpStep(CpStepKind.Title, best.Gives - now, best.Gives - now, CpCertainty.Exact, Slot: slot, OtherId: best.Title.Id, ItemId: wornId, Cost: CpCost.Free));
                }
            }
        }

        // the owned wing that gives most, against the one equipped
        if (input.WingsOwned is { Count: > 0 } ownedWings)
        {
            double now = input.WingId is int current && Aion2CpSources.Wing(current) is { } equipped ? Sum(equipped.Stats) : 0;
            var best = ownedWings.Distinct().Select(Aion2CpSources.Wing).Where(w => w is not null).Select(w => (Wing: w!, Gives: Sum(w!.Stats))).OrderByDescending(w => w.Gives).FirstOrDefault();
            if (best.Wing is not null && best.Wing.Id != input.WingId && best.Gives - now >= 1)
            {
                steps.Add(new CpStep(CpStepKind.Wing, best.Gives - now, best.Gives - now, CpCertainty.Exact, OtherId: best.Wing.Id, ItemId: input.WingId ?? 0, Cost: CpCost.Free));
            }
        }

        // Daevanion: the stat nodes still closed, per node on average (opening one takes the points and the path to it)
        foreach (CpBoardRow b in r.Boards.Where(b => b.ClosedStatNodes > 0 && b.CpLeft >= 1))
        {
            double each = b.CpLeft / b.ClosedStatNodes;
            if (each >= 1)
            {
                steps.Add(new CpStep(CpStepKind.Daevanion, each, b.CpLeft, CpCertainty.Average, OtherId: b.BoardId, Count: b.ClosedStatNodes));
            }
        }

        return new CpWholeResult
        {
            Totals = r.Totals, Scale = r.Scale, Parts = r.Parts, Pieces = r.Pieces, Skills = r.Skills, Stigmas = r.Stigmas, Boards = r.Boards, Genus = r.Genus,
            Pets = r.Pets, PetsNearLevel = r.PetsNearLevel, Titles = r.Titles, OwnedTitles = r.OwnedTitles, OwnedTitlesCp = r.OwnedTitlesCp,
            OwnedTitleCount = r.OwnedTitleCount, Wing = r.Wing, Arcana = r.Arcana, Attributes = r.Attributes,
            Steps = steps.Where(s => Math.Max(s.Gain, s.GainMax) >= 1).OrderByDescending(s => s.Rank).ThenBy(s => s.Kind).ThenBy(s => s.Slot).ToList(),
        };
    }

    /// <summary>A piece's base stats, enhancement and breakthrough at the given levels, added up per stat.</summary>
    private static IReadOnlyDictionary<string, int> Total(CpItemData data, int enchant, int exceed)
    {
        var sum = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var stats in new[] { data.Base, Aion2CpData.Enchant(data.Group, enchant), Aion2CpData.Exceed(data.Group, exceed) })
        {
            foreach (var (stat, value) in stats)
            {
                sum[stat] = sum.GetValueOrDefault(stat) + value;
            }
        }

        return sum;
    }

    /// <summary>What a level costs on average: a try's Kina and material times the tries it takes (null when the tables don't say).</summary>
    private static CpCost? CostOf(CpTry? next)
    {
        if (next is null)
        {
            return null;
        }

        double tries = next.Tries;
        if (double.IsInfinity(tries))
        {
            return null;
        }

        var materials = next.MaterialId != 0 && next.Count > 0 ? new[] { new CpMaterial(next.MaterialId, next.Count * tries) } : Array.Empty<CpMaterial>();
        return new CpCost(next.Kina * tries, materials, tries > 1.0001, tries);
    }

    /// <summary>The character's race by its gear: the race of the pieces only one race can wear (D Asmodian, L Elyos), most of them; A when none says.</summary>
    public static char RaceOf(IReadOnlyList<Aion2EquippedItem> gear)
    {
        var races = gear.Select(g => Aion2CpCosts.Piece(g.ItemId)?.Race ?? 'A').Where(r => r is 'D' or 'L').ToList();
        return races.Count == 0 ? 'A' : races.GroupBy(r => r).OrderByDescending(g => g.Count()).First().Key;
    }

    /// <summary>Whether a piece fits the race: one both races wear does; a race's own only when the character's race is known to be that one.</summary>
    private static bool RaceFits(int itemId, char race)
    {
        char of = Aion2CpCosts.Piece(itemId)?.Race ?? 'A';
        return of == 'A' || of == race;
    }

    /// <summary>The range of a purple stone (the best colour) of the stat over the kinds of stone; null when the stat has no purple stone.</summary>
    private static (int Min, int Max)? BestStone(string stat) => Aion2CpData.Stone(stat, 4);

    /// <summary>
    /// The scale that fits the calculator to the game: the game's Combat Power over the calculator's at the same state, the class base aside (null when the game's number
    /// is unknown or the two are too far apart to trust a fit: under 0.7 or over 1.3).
    /// </summary>
    public static double? Fit(int? gameCp, CpWholeInput atGame, CpModel? model = null)
    {
        model ??= Aion2CpData.Model;
        if (gameCp is not > 0)
        {
            return null;
        }

        CpWholeResult r = Evaluate(atGame, model);
        double baseCp = r.Parts[CpSource.Base], rest = r.Total - baseCp;
        if (rest <= 0)
        {
            return null;
        }

        double fit = (gameCp.Value - baseCp) / rest;
        return fit is >= 0.7 and <= 1.3 ? fit : null;
    }
}
