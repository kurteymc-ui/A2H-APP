using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Combat;

/// <summary>Where a line of a piece comes from.</summary>
public enum CpLineKind
{
    Base,
    Enhance,
    Breakthrough,
    Soul,
    Stone,
}

/// <summary>One line of a piece: its stat, its value (a manastone the character page doesn't know yet: the range over the kinds of stone) and the CP it is
/// worth (null: Combat Power doesn't count the stat). A soul-binding line carries the range it could roll on this piece.
/// <para>A soul-binding line can also be a skill's level (+1 Guarding Seal): <see cref="SkillId"/>, the level in <see cref="Value"/>. Combat Power counts that
/// level with the skill (the Skills part), so the line's own CP is null; <see cref="SkillCp"/> is what the level is worth there (0: the skill is not counted,
/// not on the bar).</para></summary>
public sealed record CpLine(CpLineKind Kind, string Stat, double Value, double ValueMax, double? CpMin, double? CpMax, int? PoolMin = null, int? PoolMax = null, int Tier = 0,
    int SkillId = 0, double? SkillCp = null)
{
    public double Mid => ((CpMin ?? 0) + (CpMax ?? 0)) / 2;

    /// <summary>A skill-level line of the soul binding.</summary>
    public bool IsSkill => SkillId != 0;

    /// <summary>A soul-binding line with a stat (not a skill level).</summary>
    public bool IsSoulStat => Kind == CpLineKind.Soul && SkillId == 0;
}

/// <summary>A worn piece and what it is worth: its enhancement and breakthrough level (the game sends both in one number: 16-20 is +15 with breakthrough
/// 1-5), its lines and their CP. <see cref="Data"/> is null for a piece the item data doesn't know; <see cref="EmptyStones"/>: manastone slots shown empty.</summary>
public sealed record CpPiece(int Slot, int ItemId, CpItemData? Data, int Enchant, int Exceed, IReadOnlyList<CpLine> Lines, int EmptyStones = 0)
{
    public double CpMin => Lines.Sum(l => l.CpMin ?? 0);

    public double CpMax => Lines.Sum(l => l.CpMax ?? 0);

    public double Mid => (CpMin + CpMax) / 2;
}

public enum CpUpgradeKind
{
    Enhance,
    Breakthrough,
    Reroll,
    Stone,
    BetterItem,
}

/// <summary>
/// One way to raise Combat Power and how much it gives: +1 enhancement, the next breakthrough level (with the game's chance), rerolling the weakest
/// soul-binding line into the best one the piece can roll ("up to"), a stone in an empty slot (the range over the stones), or a piece of the same kind with
/// more CP at the same enhancement and breakthrough level.
/// </summary>
public sealed record CpUpgrade(CpUpgradeKind Kind, int Slot, int ItemId, double GainMin, double GainMax, int ToLevel = 0, double? Chance = null, string? FromStat = null,
    string? ToStat = null, int OtherItemId = 0)
{
    public double Mid => (GainMin + GainMax) / 2;
}

public enum CpChangeKind
{
    Equipped,
    Enhanced,
    SoulBound,
    Stones,
    Breakthrough,
}

/// <summary>A change of the worn gear between two lists the game sent, and what it did to Combat Power.</summary>
public sealed record CpChange(DateTime At, CpChangeKind Kind, int Slot, int ItemId, double Delta, int From = 0, int To = 0, int OldItemId = 0);

/// <summary>One of the best lines a piece can roll: its top value and the CP that is worth; <see cref="Have"/> when the piece already has the stat.</summary>
public sealed record CpIdealLine(string Stat, int Max, double Cp, bool Have);

/// <summary>How good a piece's soul binding is: the CP of its lines against the CP of the best lines it could roll, as many as it has (0-100), a letter
/// (S+, S, A, B, C, D) and those best lines.</summary>
public sealed record CpGrade(double Score, string Letter, IReadOnlyList<CpIdealLine> Ideal)
{
    public int Have => Ideal.Count(l => l.Have);
}

/// <summary>The best piece of a kind in the item data, at full enhancement and breakthrough, and how the worn one compares at the same levels.</summary>
public sealed record CpBest(int ItemId, double Cp, double Mine, bool IsMine);

/// <summary>The character the calculator counts for: its Item Level (the model's progression factor) and the factors measured on it by model stat
/// (<see cref="CpChecks"/>).</summary>
public sealed record CpCharacter(int? ItemLevel = null, IReadOnlyDictionary<string, double>? Factors = null);

/// <summary>What one more of a stat is worth for the character: a point, or 1 % of a percentage stat (<see cref="Amount"/> in the game's units).</summary>
public sealed record CpPerUnit(string Stat, double Amount, double Cp, string Key);

/// <summary>
/// The CP calculator: what each worn piece is worth line by line (base stats, enhancement, breakthrough, soul binding, manastones; values from the game's
/// own tables, CP from the website's model, see <see cref="Aion2CpData"/>), how good its soul binding is, whether it is the best of its kind, the ways to
/// raise Combat Power ranked by what they give, and what a change of the gear did. Manastone values the game doesn't send come from the character page
/// (the website's lookup) when it has them, else they are a range.
/// </summary>
public static class CpCalc
{
    /// <summary>Base stats the page leaves out (caps and the parry figures, not stats).</summary>
    private static readonly HashSet<string> Hidden = new(StringComparer.Ordinal) { "MaxWeaponBlock", "MaxShieldBlock" };

    /// <summary>The exact manastones of a piece from its character page (stat, value); null when the page doesn't know the piece.</summary>
    public delegate IReadOnlyList<(string Stat, int Value)>? StonesOf(Aion2EquippedItem item);

    /// <summary>The enhancement and breakthrough level in the one number the game sends: above the group's top enhancement level the rest is breakthrough.</summary>
    public static (int Enchant, int Exceed) Levels(Aion2EquippedItem item, CpItemData? data)
    {
        int max = data is null ? 0 : Aion2CpData.MaxEnchant(data.Group);
        return data is not null && max > 0 && item.Enchant > max ? (max, Math.Min(item.Enchant - max, Aion2CpData.MaxExceed(data.Group))) : (item.Enchant, 0);
    }

    public static CpPiece Piece(Aion2EquippedItem item, CpTotals totals, CpModel? model = null, StonesOf? stonesOf = null)
    {
        model ??= Aion2CpData.Model;
        CpItemData? data = Aion2CpData.Item(item.ItemId);
        var (enchant, exceed) = Levels(item, data);
        var lines = new List<CpLine>();
        if (data is not null)
        {
            foreach (var (stat, value) in data.Base)
            {
                if (!Hidden.Contains(stat))
                {
                    lines.Add(Exact(CpLineKind.Base, stat, value, totals, model));
                }
            }

            foreach (var (stat, value) in Aion2CpData.Enchant(data.Group, enchant))
            {
                lines.Add(Exact(CpLineKind.Enhance, stat, value, totals, model));
            }

            foreach (var (stat, value) in Aion2CpData.Exceed(data.Group, exceed))
            {
                lines.Add(Exact(CpLineKind.Breakthrough, stat, value, totals, model));
            }
        }

        foreach (Aion2RolledStat rolled in item.Stats ?? Array.Empty<Aion2RolledStat>())
        {
            string stat = Aion2CpData.Token(rolled.StatId) ?? "#" + rolled.StatId;
            var pool = data?.Pool.Where(p => p.Stat == stat).ToList();
            lines.Add(Exact(CpLineKind.Soul, stat, rolled.Value, totals, model) with
            {
                PoolMin = pool is { Count: > 0 } ? pool.Min(p => p.Min) : null, PoolMax = pool is { Count: > 0 } ? pool.Max(p => p.Max) : null,
            });
        }

        // the soul binding's skill lines (+1 Guarding Seal): lines of the piece like its stats - a Nuakum or Kromede piece has four lines in all, a Splendent
        // Dragon Lord piece five (4 November 2026: they were only counted with the skills)
        if (data is { Pool.Count: > 0 } || item.Stats is { Count: > 0 })
        {
            foreach (Aion2SkillBonus bonus in item.SkillBonuses ?? Array.Empty<Aion2SkillBonus>())
            {
                if (bonus.SkillId > 0 && bonus.Level > 0)
                {
                    lines.Add(new CpLine(CpLineKind.Soul, "Skill:" + bonus.SkillId, bonus.Level, bonus.Level, null, null, SkillId: bonus.SkillId));
                }
            }
        }

        // a stone the character page names (same stat, a value its colour can have) is exact; any other is the range of its colour
        var known = (stonesOf?.Invoke(item) ?? Array.Empty<(string Stat, int Value)>()).ToList();
        foreach (Aion2Stone stone in item.Stones ?? Array.Empty<Aion2Stone>())
        {
            if (stone.StatId == 0)
            {
                continue;
            }

            string stat = Aion2CpData.Token(stone.StatId) ?? "#" + stone.StatId;
            (int Min, int Max)? range = Aion2CpData.Stone(stat, stone.Tier);
            int match = known.FindIndex(k => k.Stat == stat && (range is null || (k.Value >= range.Value.Min && k.Value <= range.Value.Max)));
            if (match >= 0)
            {
                int value = known[match].Value;
                known.RemoveAt(match);
                lines.Add(Exact(CpLineKind.Stone, stat, value, totals, model) with { Tier = stone.Tier });
            }
            else if (range is { } r)
            {
                lines.Add(new CpLine(CpLineKind.Stone, stat, r.Min, r.Max, Aion2CpData.Cp(stat, r.Min, totals, model), Aion2CpData.Cp(stat, r.Max, totals, model), Tier: stone.Tier));
            }
            else
            {
                lines.Add(new CpLine(CpLineKind.Stone, stat, 0, 0, null, null, Tier: stone.Tier));
            }
        }

        return new CpPiece(item.SlotIndex, item.ItemId, data, enchant, exceed, lines, (item.Stones ?? Array.Empty<Aion2Stone>()).Count(s => s.StatId == 0));
    }

    private static CpLine Exact(CpLineKind kind, string stat, double value, CpTotals totals, CpModel model)
    {
        double? cp = Aion2CpData.Cp(stat, value, totals, model);
        return new CpLine(kind, stat, value, value, cp, cp);
    }

    private static double Sum(IReadOnlyDictionary<string, int> stats, CpTotals totals, CpModel model) =>
        stats.Sum(kv => Aion2CpData.Cp(kv.Key, kv.Value, totals, model) ?? 0);

    /// <summary>A piece of the item data at an enhancement and breakthrough level: base stats, enhancement, breakthrough (soul binding and stones aside).</summary>
    public static double ItemCp(CpItemData data, int enchant, int exceed, CpTotals totals, CpModel? model = null)
    {
        model ??= Aion2CpData.Model;
        return Sum(data.Base, totals, model) + Sum(Aion2CpData.Enchant(data.Group, enchant), totals, model) + Sum(Aion2CpData.Exceed(data.Group, exceed), totals, model);
    }

    /// <summary>How good the piece's soul binding is against the best lines it could roll (null without soul binding or a pool).</summary>
    public static CpGrade? Grade(CpPiece piece, CpTotals totals, CpModel? model = null)
    {
        model ??= Aion2CpData.Model;
        var souls = piece.Lines.Where(l => l.IsSoulStat).ToList();
        if (piece.Data is not { } data || souls.Count == 0 || data.Pool.Count == 0)
        {
            return null;
        }

        var have = souls.Select(l => l.Stat).ToHashSet(StringComparer.Ordinal);
        var best = BestLines(data, souls.Count, totals, model);
        double ideal = best.Sum(x => x.Cp);
        if (ideal <= 0)
        {
            return null;
        }

        double score = Math.Clamp(souls.Sum(l => l.CpMin ?? 0) / ideal * 100, 0, 100);
        return new CpGrade(score, Letter(score), best.Select(x => new CpIdealLine(x.Stat, x.Max, x.Cp, have.Contains(x.Stat))).ToList());
    }

    /// <summary>The <paramref name="count"/> lines of a piece's pool worth the most CP at their top value, best first (for a class without its own list).</summary>
    public static IReadOnlyList<CpIdealLine> BestLinesOf(CpItemData data, int count, CpTotals totals, CpModel? model = null) =>
        BestLines(data, count, totals, model ?? Aion2CpData.Model).Select(x => new CpIdealLine(x.Stat, x.Max, x.Cp, false)).ToList();

    /// <summary>The <paramref name="count"/> lines of a piece's soul-binding pool worth the most CP at their top value (one line per stat).</summary>
    private static List<(string Stat, int Max, double Cp)> BestLines(CpItemData data, int count, CpTotals totals, CpModel model) =>
        data.Pool.GroupBy(p => p.Stat).Select(g => (Stat: g.Key, Max: g.Max(p => p.Max)))
            .Select(x => (x.Stat, x.Max, Cp: Aion2CpData.Cp(x.Stat, x.Max, totals, model) ?? 0)).Where(x => x.Cp > 0)
            .OrderByDescending(x => x.Cp).ThenBy(x => x.Stat, StringComparer.Ordinal).Take(count).ToList();

    /// <summary>A piece at its best: top enhancement and breakthrough level and the best soul binding it can roll with <paramref name="soulLines"/> lines.</summary>
    public static double FullCp(CpItemData data, int soulLines, CpTotals totals, CpModel? model = null)
    {
        model ??= Aion2CpData.Model;
        return ItemCp(data, Aion2CpData.MaxEnchant(data.Group), Aion2CpData.MaxExceed(data.Group), totals, model)
            + (soulLines > 0 ? BestLines(data, soulLines, totals, model).Sum(x => x.Cp) : 0);
    }

    /// <summary>The whole gear's soul binding: the CP of all lines against the CP of all the best lines (null when no piece has a grade).</summary>
    public static CpGrade? Overall(IReadOnlyList<CpPiece> pieces, CpTotals totals, CpModel? model = null)
    {
        double now = 0, ideal = 0;
        foreach (CpPiece piece in pieces)
        {
            if (Grade(piece, totals, model) is { } g)
            {
                now += piece.Lines.Where(l => l.IsSoulStat).Sum(l => l.CpMin ?? 0);
                ideal += g.Ideal.Sum(l => l.Cp);
            }
        }

        if (ideal <= 0)
        {
            return null;
        }

        double score = Math.Clamp(now / ideal * 100, 0, 100);
        return new CpGrade(score, Letter(score), Array.Empty<CpIdealLine>());
    }

    public static string Letter(double score) => score >= 85 ? "S+" : score >= 70 ? "S" : score >= 55 ? "A" : score >= 40 ? "B" : score >= 25 ? "C" : "D";

    /// <summary>
    /// The best piece of the worn one's kind in the item data, both at their best (<see cref="FullCp"/>, as many soul-binding lines as the worn piece
    /// has). The worn piece when nothing beats it; among pieces worth the same the one whose name is most like the worn one's (the same faction's version).
    /// <paramref name="race"/>: the character's race (D Asmodian, L Elyos, A unknown, see CpWhole.RaceOf) - the other race's pieces are left out (4 November
    /// 2026: an Asmodian's necklace showed the Elyos Splendent Wise one as its best).
    /// </summary>
    public static CpBest? BestInSlot(CpPiece piece, CpTotals totals, CpModel? model = null, char race = 'A')
    {
        model ??= Aion2CpData.Model;
        if (piece.Data is not { } data)
        {
            return null;
        }

        int lines = piece.Lines.Count(l => l.IsSoulStat);
        bool Fits(int itemId) => race == 'A' || (Aion2CpCosts.Piece(itemId)?.Race ?? 'A') is var of && (of == 'A' || of == race);
        var scored = Aion2CpData.Items.Where(x => x.Category == data.Category && (x.Id == data.Id || Fits(x.Id))).Select(x => (Item: x, Cp: FullCp(x, lines, totals, model))).ToList();
        double mine = FullCp(data, lines, totals, model), top = scored.Max(s => s.Cp);
        if (mine >= top - 0.5)
        {
            return new CpBest(data.Id, mine, mine, true);
        }

        var words = Words(data.Id);
        var best = scored.Where(s => s.Cp >= top - 0.5).OrderByDescending(s => Words(s.Item.Id).Count(words.Contains)).ThenBy(s => s.Item.Id).First();
        return new CpBest(best.Item.Id, best.Cp, mine, false);
    }

    private static HashSet<string> Words(int itemId) =>
        (Aion2ItemCatalog.Find(itemId)?.Name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every way to raise Combat Power from the worn pieces, the biggest first (by the middle of its range); nothing below 1 CP.</summary>
    public static IReadOnlyList<CpUpgrade> Upgrades(IReadOnlyList<CpPiece> pieces, CpTotals totals, CpModel? model = null)
    {
        model ??= Aion2CpData.Model;
        var list = new List<CpUpgrade>();
        foreach (CpPiece piece in pieces)
        {
            if (piece.Data is not { } data)
            {
                continue;
            }

            int maxEnchant = Aion2CpData.MaxEnchant(data.Group);
            if (piece.Enchant < maxEnchant)
            {
                double gain = Sum(Aion2CpData.Enchant(data.Group, piece.Enchant + 1), totals, model) - Sum(Aion2CpData.Enchant(data.Group, piece.Enchant), totals, model);
                list.Add(new CpUpgrade(CpUpgradeKind.Enhance, piece.Slot, piece.ItemId, gain, gain, piece.Enchant + 1));
            }

            // a breakthrough needs the top enhancement level first
            int maxExceed = Aion2CpData.MaxExceed(data.Group);
            if (piece.Enchant >= maxEnchant && piece.Exceed < maxExceed)
            {
                double gain = Sum(Aion2CpData.Exceed(data.Group, piece.Exceed + 1), totals, model) - Sum(Aion2CpData.Exceed(data.Group, piece.Exceed), totals, model);
                list.Add(new CpUpgrade(CpUpgradeKind.Breakthrough, piece.Slot, piece.ItemId, gain, gain, piece.Exceed + 1, Aion2CpData.ExceedChance(data.Group, piece.Exceed + 1)));
            }

            // the weakest soul-binding line rerolled into the best line the piece can roll: up to that line's top value
            var souls = piece.Lines.Where(l => l.IsSoulStat).ToList();
            if (souls.Count > 0 && data.Pool.Count > 0)
            {
                CpLine weakest = souls.OrderBy(l => l.CpMin ?? 0).First();
                var best = data.Pool.Select(p => (Line: p, Lo: Aion2CpData.Cp(p.Stat, p.Min, totals, model) ?? 0, Hi: Aion2CpData.Cp(p.Stat, p.Max, totals, model) ?? 0))
                    .OrderByDescending(p => p.Hi).First();
                double now = weakest.CpMin ?? 0;
                if (best.Hi - now >= 1)
                {
                    list.Add(new CpUpgrade(CpUpgradeKind.Reroll, piece.Slot, piece.ItemId, Math.Max(0, best.Lo - now), best.Hi - now, FromStat: weakest.Stat, ToStat: best.Line.Stat));
                }
            }

            // empty manastone slots: the best stone a slot can take, over the colours and kinds of stone
            if (piece.EmptyStones > 0)
            {
                var stone = Aion2CpData.StoneStats.Select(s => (Stat: s, Lo: Low(s, totals, model), Hi: High(s, totals, model))).OrderByDescending(s => s.Hi).FirstOrDefault();
                if (stone.Stat is not null && stone.Hi >= 1)
                {
                    list.Add(new CpUpgrade(CpUpgradeKind.Stone, piece.Slot, piece.ItemId, stone.Lo * piece.EmptyStones, stone.Hi * piece.EmptyStones, ToLevel: piece.EmptyStones, ToStat: stone.Stat));
                }
            }

            // a piece of the same kind worth more at the same enhancement and breakthrough level (its own soul binding and stones aside)
            double mine = ItemCp(data, piece.Enchant, piece.Exceed, totals, model);
            CpItemData? other = Aion2CpData.Items.Where(x => x.Category == data.Category && x.Id != data.Id && x.Level >= data.Level)
                .OrderByDescending(x => ItemCp(x, piece.Enchant, piece.Exceed, totals, model)).ThenBy(x => x.Level).FirstOrDefault();
            if (other is not null)
            {
                double theirs = ItemCp(other, piece.Enchant, piece.Exceed, totals, model);
                if (theirs - mine >= 1)
                {
                    list.Add(new CpUpgrade(CpUpgradeKind.BetterItem, piece.Slot, piece.ItemId, theirs - mine, theirs - mine, piece.Enchant, OtherItemId: other.Id));
                }
            }
        }

        return list.Where(u => u.GainMax >= 1).OrderByDescending(u => u.Mid).ThenBy(u => u.Slot).ToList();
    }

    /// <summary>The least a stone of this stat gives: the lowest colour it comes in (a Damage Boost stone is never white).</summary>
    private static double Low(string stat, CpTotals totals, CpModel model) =>
        Enumerable.Range(1, 4).Select(t => Aion2CpData.Stone(stat, t)).Where(r => r is not null).Select(r => Aion2CpData.Cp(stat, r!.Value.Min, totals, model) ?? 0).DefaultIfEmpty(0).Min();

    private static double High(string stat, CpTotals totals, CpModel model) =>
        Enumerable.Range(1, 4).Select(t => Aion2CpData.Stone(stat, t) is { } r ? Aion2CpData.Cp(stat, r.Max, totals, model) ?? 0 : 0).Max();

    /// <summary>All the pieces of a list the item data knows.</summary>
    public static IReadOnlyList<CpPiece> Pieces(IReadOnlyList<Aion2EquippedItem> gear, CpTotals totals, CpModel? model = null, StonesOf? stonesOf = null) =>
        gear.Where(g => Aion2CpData.Item(g.ItemId) is not null).OrderBy(g => g.SlotIndex).Select(g => Piece(g, totals, model, stonesOf)).ToList();

    /// <summary>The pieces with the totals worked out from them (two passes: the totals need the pieces' values, the share lines the totals).</summary>
    public static (IReadOnlyList<CpPiece> Pieces, CpTotals Totals) Evaluate(IReadOnlyList<Aion2EquippedItem> gear, IReadOnlyDictionary<int, int>? frame, CpModel? model = null,
        StonesOf? stonesOf = null, CpCharacter? character = null)
    {
        CpTotals totals = Totals(frame, Pieces(gear, new CpTotals(0, 0, 0, 0, 0), model, stonesOf), character, model);
        return (Pieces(gear, totals, model, stonesOf), totals);
    }

    /// <summary>The stats of <see cref="PerUnit"/>: the points of Attack, Critical Hit, Accuracy, Critical Attack, HP and Defense, 1 % of the rest.</summary>
    private static readonly string[] PerUnitStats =
    {
        "WeaponFixingDamage", "Critical", "WeaponAccuracy", "CriticalAddDamage", "DamageRatio", "AmplifyAllDamage", "AmplifyWeaponDamage", "AmplifyCriticalDamage",
        "CombatSpeed", "AdditionalHitRate", "Perfect", "HardHit", "PvEAmplifyDamage", "HPMax", "ArmorDefense", "DecreaseDamage",
    };

    /// <summary>What one more point (or 1 %) of each stat Combat Power counts is worth for the character, as the calculator counts it.</summary>
    public static IReadOnlyList<CpPerUnit> PerUnit(CpTotals totals, CpModel? model = null)
    {
        var list = new List<CpPerUnit>();
        foreach (string stat in PerUnitStats)
        {
            double amount = Aion2CpData.Percent(stat) ? 100 : 1;
            if (Aion2CpData.Cp(stat, amount, totals, model) is double cp && cp > 0 && Aion2CpData.KeyOf(stat) is { } key)
            {
                list.Add(new CpPerUnit(stat, amount, cp, key));
            }
        }

        return list;
    }

    /// <summary>The worn pieces' lines added up by the meter's stat id (a stone the character page doesn't name counts at the least its colour gives):
    /// what the gear puts into the game's stat totals, by the tables.</summary>
    public static Dictionary<int, long> StatSums(IReadOnlyList<CpPiece> pieces)
    {
        var sums = new Dictionary<int, long>();
        foreach (CpLine line in pieces.SelectMany(p => p.Lines))
        {
            int id = Aion2CpData.StatId(line.Stat);
            if (id > 0)
            {
                sums[id] = sums.GetValueOrDefault(id) + (long)Math.Round(line.Value);
            }
        }

        return sums;
    }

    /// <summary>
    /// What changed between two lists of the same character's gear, slot by slot, and what each change did to Combat Power: another piece, another
    /// enhancement level, another breakthrough level, rerolled soul binding, other stones.
    /// </summary>
    public static IReadOnlyList<CpChange> Changes(IReadOnlyList<Aion2EquippedItem> before, IReadOnlyList<Aion2EquippedItem> after, CpTotals totals, DateTime at,
        CpModel? model = null, StonesOf? stonesOf = null)
    {
        model ??= Aion2CpData.Model;
        var changes = new List<CpChange>();
        var old = before.Where(g => Aion2CpData.Item(g.ItemId) is not null).GroupBy(g => g.SlotIndex).ToDictionary(g => g.Key, g => g.First());
        foreach (Aion2EquippedItem now in after.Where(g => Aion2CpData.Item(g.ItemId) is not null))
        {
            CpPiece next = Piece(now, totals, model, stonesOf);
            if (!old.TryGetValue(now.SlotIndex, out Aion2EquippedItem? was))
            {
                changes.Add(new CpChange(at, CpChangeKind.Equipped, now.SlotIndex, now.ItemId, next.Mid));
                continue;
            }

            CpPiece prev = Piece(was, totals, model, stonesOf);
            if (was.ItemId != now.ItemId)
            {
                changes.Add(new CpChange(at, CpChangeKind.Equipped, now.SlotIndex, now.ItemId, next.Mid - prev.Mid, OldItemId: was.ItemId));
                continue;
            }

            double Part(CpPiece p, CpLineKind kind) => p.Lines.Where(l => l.Kind == kind).Sum(l => l.Mid);
            if (prev.Enchant != next.Enchant)
            {
                changes.Add(new CpChange(at, CpChangeKind.Enhanced, now.SlotIndex, now.ItemId, Part(next, CpLineKind.Enhance) - Part(prev, CpLineKind.Enhance), prev.Enchant, next.Enchant));
            }

            if (prev.Exceed != next.Exceed)
            {
                changes.Add(new CpChange(at, CpChangeKind.Breakthrough, now.SlotIndex, now.ItemId, Part(next, CpLineKind.Breakthrough) - Part(prev, CpLineKind.Breakthrough), prev.Exceed, next.Exceed));
            }

            if (Signature(was.Stats) != Signature(now.Stats) && now.Stats is not null && was.Stats is not null)
            {
                changes.Add(new CpChange(at, CpChangeKind.SoulBound, now.SlotIndex, now.ItemId, Part(next, CpLineKind.Soul) - Part(prev, CpLineKind.Soul)));
            }

            if (StoneSignature(was.Stones) != StoneSignature(now.Stones) && now.Stones is not null && was.Stones is not null)
            {
                changes.Add(new CpChange(at, CpChangeKind.Stones, now.SlotIndex, now.ItemId, Part(next, CpLineKind.Stone) - Part(prev, CpLineKind.Stone)));
            }
        }

        return changes;
    }

    private static string Signature(IReadOnlyList<Aion2RolledStat>? stats) => stats is null ? "" : string.Join(',', stats.Select(s => s.StatId + ":" + s.Value));

    private static string StoneSignature(IReadOnlyList<Aion2Stone>? stones) => stones is null ? "" : string.Join(',', stones.Select(s => s.StatId + ":" + s.Tier));

    /// <summary>
    /// The character's numbers the CP of a line depends on, counted the way the website's model counts them: the totals the share lines take from (the stat
    /// frame the game sends: Attack = max attack + attack + additional attack, Accuracy with weapon accuracy, Defense with armour defense; else what the worn
    /// pieces add up to); the increases that raise each point of Attack ("Attack increase" lines + 0.1 % per point of Might and of Destruction), Critical
    /// Hit (Precision and Death), Accuracy (Precision and Freedom) and HP (Vitality and Life) - the attribute points are the frame's ids 1 to 17; the
    /// progression factor of the character's Item Level; the factors measured on the character; the offence and guard its percentages count with
    /// (<see cref="Strength"/>; 0, the weights as they are, without the frame).
    /// </summary>
    public static CpTotals Totals(IReadOnlyDictionary<int, int>? frame, IReadOnlyList<CpPiece> pieces, CpCharacter? character = null, CpModel? model = null)
    {
        model ??= Aion2CpData.Model;
        double F(params string[] tokens) => frame is null ? 0 : tokens.Sum(t => Aion2CpData.StatId(t) is int id and > 0 ? frame.GetValueOrDefault(id) : 0);
        double G(params string[] tokens) => pieces.SelectMany(p => p.Lines).Where(l => tokens.Contains(l.Stat)).Sum(l => (l.Value + l.ValueMax) / 2);
        double Pick(double fromFrame, double fromGear) => fromFrame > 0 ? fromFrame : fromGear;
        double Points(params int[] ids) => frame is null ? 0 : ids.Sum(id => Math.Max(0, frame.GetValueOrDefault(id)));
        double soulIncrease = pieces.SelectMany(p => p.Lines).Where(l => l is { Kind: CpLineKind.Soul, Stat: "DamageRatio" }).Sum(l => l.Value) / 10000;
        double attack = Pick(F("WeaponDamage", "WeaponFixingDamage", "FixingDamage"), G("WeaponDamage", "WeaponFixingDamage", "FixingDamage"));
        double critical = Pick(F("Critical"), G("Critical")), accuracy = Pick(F("Accuracy", "WeaponAccuracy"), G("Accuracy", "WeaponAccuracy"));
        double hp = Pick(F("HPMax"), G("HPMax")), defense = Pick(F("Defense", "ArmorDefense"), G("Defense", "ArmorDefense"));
        double attackIncrease = soulIncrease + 0.001 * Points(1, 13), critIncrease = 0.001 * Points(5, 14), accuracyIncrease = 0.001 * Points(5, 8);
        double hpIncrease = 0.001 * Points(4, 10);

        // what the percentages count with: the game's own stat list (the worn pieces alone are far from the character's strength: without it, not known)
        var (offense, guard) = frame is { Count: > 0 } ? Strength(frame, model) : (0.0, 0.0);
        return new CpTotals(
            attack,
            critical,
            accuracy,
            hp,
            defense,
            AttackIncrease: attackIncrease,
            CritIncrease: critIncrease,
            AccuracyIncrease: accuracyIncrease,
            HpIncrease: hpIncrease,
            Scale: model.Progression(character?.ItemLevel),
            Factors: character?.Factors,
            Offense: offense,
            Guard: guard);
    }

    /// <summary>
    /// The character's offence and guard (<see cref="CpTotals.Offense"/>, <see cref="CpTotals.Guard"/>), which its percentages count with, from the game's
    /// own stat list: Attack (max attack + attack + additional attack) with all of its "Attack increase" and 0.1 % a point of Might and of Destruction,
    /// Critical Hit with Precision and Death, Accuracy (weapon and PvE accuracy with it) with Precision and Freedom, Critical Attack, PvE and Boss Attack;
    /// HP with Vitality and Life, Defense with the armour's. The references (<see cref="Aion2CpData.OffenseReference"/>) were measured the same way.
    /// </summary>
    public static (double Offense, double Guard) Strength(IReadOnlyDictionary<int, int> frame, CpModel model)
    {
        double F(params string[] tokens) => tokens.Sum(t => Aion2CpData.StatId(t) is int id and > 0 ? Math.Max(0, frame.GetValueOrDefault(id)) : 0);
        double Points(params int[] ids) => ids.Sum(id => Math.Max(0, frame.GetValueOrDefault(id)));
        double offense = Aion2CpData.OffenseOf(key => key switch
        {
            "atk" => F("WeaponDamage", "WeaponFixingDamage", "FixingDamage"),
            "crit" => F("Critical"),
            "acc" => F("Accuracy", "WeaponAccuracy", "PvEAccuracy"),
            "critAtk" => F("CriticalAddDamage"),
            "pveAtk" => F("PvEAddDamage", "BossNpcAddDamage"),
            _ => 0,
        }, F("DamageRatio") / 10000 + 0.001 * Points(1, 13), 0.001 * Points(5, 14), 0.001 * Points(5, 8), model);
        return (offense, Aion2CpData.GuardOf(F("HPMax"), F("Defense", "ArmorDefense"), 0.001 * Points(4, 10), 0, model));
    }
}
