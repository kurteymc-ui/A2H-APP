using System.Windows.Media;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>
/// One line of the loot lists: an item, or every gear piece of one grade together ("Gold gear": which weapon or armour piece dropped is luck, that a gold piece
/// dropped is what counts). <see cref="WithKills"/>: in how many kills it dropped (summed over the pieces for a gear line); <see cref="Members"/> the items and
/// their counts, the one picked up last first.
/// </summary>
public sealed record LootLine(int Key, string Name, Color Color, int Grade, bool Gear, long Count, int WithKills, DateTime Last, IReadOnlyList<(int ItemId, long Count)> Members)
{
    /// <summary>The icon: the item's own, for a gear line the piece picked up last.</summary>
    public string? IconPath => Members.Count > 0 ? Aion2Artwork.ItemIconPath(Members[0].ItemId) : null;
}

/// <summary>A grade's drops over all time: how many, how many of them gear, in how many counted kills.</summary>
public sealed record LootGradeRow(int Grade, long Drops, long Gear, int WithKills);

/// <summary>A monster and its gold gear: its counted kills, the gold pieces, in how many kills.</summary>
public sealed record LootGoldSpot(int NpcId, IReadOnlyList<int> Zones, int Kills, long Gold, int GoldKills);

/// <summary>The loot log over all time (the monsters' drop tables, which outlive the sessions): what the Overview tab shows.</summary>
public sealed record LootAllTime(int Kills, int MobKinds, long Items, int ItemKinds, long GoldGear, int GoldKills, long GearDrops, int GearKills,
    IReadOnlyList<LootGradeRow> Grades, IReadOnlyList<LootGoldSpot> GoldSpots, IReadOnlyList<(LootLine Line, int TopNpc)> Top);

public static class LootStats
{
    /// <summary>The grade of gold gear (the character window's scale: 1 Common, 2 green, 3 blue, 4 gold, 5 orange, 6 red, 7 Special).</summary>
    public const int Gold = 4;

    /// <summary>An item's grade: the name table's, else the item catalog's; 1 when neither knows it.</summary>
    public static int GradeOf(int itemId)
    {
        int grade = Aion2ItemNames.GradeOf(itemId);
        if (grade == 0 && Aion2ItemCatalog.Find(itemId) is { } info)
        {
            grade = info.Grade;
        }

        return Math.Clamp(grade, 1, 7);
    }

    /// <summary>Weapons, armour and accessories (the item catalog has a slot for them); arcana and runes stay items of their own.</summary>
    public static bool IsGear(int itemId) => Aion2ItemCatalog.Find(itemId) is { Slot.Length: > 0 } info && info.Slot is not ("Arcana" or "Rune");

    public static Color GradeColor(int grade) => CharacterWindow.GradeColors.GetValueOrDefault(grade, CharacterWindow.GradeColors[1]);

    /// <summary>"Gold gear", "Blue gear" …</summary>
    public static string GearName(int grade) => LocalizationManager.Instance["Loot.Gear." + grade];

    /// <summary>"Gold", "Blue" …</summary>
    public static string GradeName(int grade) => LocalizationManager.Instance["Loot.Grade." + grade];

    /// <summary>Items to lines: gear of one grade together, every other item on its own; the line picked up last first.</summary>
    public static List<LootLine> Lines(IEnumerable<(int ItemId, long Count, int Kills, DateTime Last)> items)
    {
        return items.GroupBy(i => IsGear(i.ItemId) ? -GradeOf(i.ItemId) : i.ItemId).Select(g =>
        {
            bool gear = g.Key < 0;
            int grade = gear ? -g.Key : GradeOf(g.Key);
            var members = g.GroupBy(i => i.ItemId).Select(m => (ItemId: m.Key, Count: m.Sum(i => i.Count), Last: m.Max(i => i.Last)))
                .OrderByDescending(m => m.Last).Select(m => (m.ItemId, m.Count)).ToList();
            return new LootLine(g.Key, gear ? GearName(grade) : LootText.ItemName(g.Key), gear ? GradeColor(grade) : LootText.GradeColor(g.Key), grade, gear,
                g.Sum(i => i.Count), g.Sum(i => i.Kills), g.Max(i => i.Last), members);
        }).OrderByDescending(l => l.Last).ToList();
    }

    public static List<LootLine> Lines(IEnumerable<LootItem> items) => Lines(items.Select(i => (i.ItemId, i.Count, i.Kills, i.Last)));

    /// <summary>A monster's drops as lines, the most frequent first.</summary>
    public static List<LootLine> Lines(LootMob mob) =>
        Lines(mob.Items.Select(i => (i.ItemId, i.Count, i.Kills, mob.Last))).OrderByDescending(l => l.WithKills).ThenByDescending(l => l.Count).ToList();

    /// <summary>The whole log over all time, from the monsters' drop tables. Rates count only the kills of known monsters; chests and the like add to the
    /// counts.</summary>
    public static LootAllTime AllTime(IReadOnlyList<LootMob> mobs)
    {
        var killed = mobs.Where(m => m.NpcId > 0 && m.Kills > 0).ToList();
        int kills = killed.Sum(m => m.Kills);
        var all = mobs.SelectMany(m => m.Items.Select(i => (Mob: m, Item: i))).ToList();
        bool Counted((LootMob Mob, LootMobItem Item) x) => x.Mob.NpcId > 0;

        var grades = all.GroupBy(x => GradeOf(x.Item.ItemId)).Select(g => new LootGradeRow(g.Key, g.Sum(x => x.Item.Count),
                g.Where(x => IsGear(x.Item.ItemId)).Sum(x => x.Item.Count), g.Where(Counted).Sum(x => x.Item.Kills)))
            .OrderByDescending(r => r.Grade).ToList();

        bool IsGoldGear(int itemId) => IsGear(itemId) && GradeOf(itemId) == Gold;
        var spots = killed.Select(m => new LootGoldSpot(m.NpcId, m.Zones, m.Kills, m.Items.Where(i => IsGoldGear(i.ItemId)).Sum(i => i.Count),
                m.Items.Where(i => IsGoldGear(i.ItemId)).Sum(i => i.Kills)))
            .Where(s => s.Gold > 0).OrderBy(s => (double)s.Kills / Math.Max(1, s.GoldKills)).ThenByDescending(s => s.Kills).ToList();

        var top = Lines(all.Select(x => (x.Item.ItemId, x.Item.Count, Counted(x) ? x.Item.Kills : 0, x.Mob.Last))).OrderByDescending(l => l.Count).Take(8)
            .Select(l => (l, all.Where(x => l.Members.Any(m => m.ItemId == x.Item.ItemId)).GroupBy(x => x.Mob.NpcId)
                .OrderByDescending(g => g.Sum(x => x.Item.Count)).First().Key)).ToList();

        return new LootAllTime(kills, killed.Count, all.Sum(x => x.Item.Count), all.Select(x => x.Item.ItemId).Distinct().Count(),
            all.Where(x => IsGoldGear(x.Item.ItemId)).Sum(x => x.Item.Count), all.Where(x => Counted(x) && IsGoldGear(x.Item.ItemId)).Sum(x => x.Item.Kills),
            all.Where(x => IsGear(x.Item.ItemId)).Sum(x => x.Item.Count), all.Where(x => Counted(x) && IsGear(x.Item.ItemId)).Sum(x => x.Item.Kills),
            grades, spots, top);
    }

    /// <summary>"1 in 48 kills", "every kill" (~ in front while the kills are few); empty without a drop or a kill.</summary>
    public static string OneIn(int withItem, int kills)
    {
        if (LootText.RateParts(withItem, kills) is not { } r)
        {
            return "";
        }

        var loc = LocalizationManager.Instance;
        return (kills < LootText.RoughBelow ? "~" : "") + (r.OneIn == "1" ? loc["Loot.OneIn.Every"] : string.Format(loc["Loot.OneIn"], r.OneIn));
    }

    /// <summary>A gear line's pieces, for its tooltip.</summary>
    public static string MembersTip(LootLine line) =>
        LocalizationManager.Instance["Loot.GearTip"] + "\n" + string.Join("\n", line.Members.Select(m => LootText.ItemName(m.ItemId) + (m.Count > 1 ? $"  ×{m.Count:N0}" : "")));
}
