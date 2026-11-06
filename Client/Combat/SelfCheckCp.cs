using System.Text.Json;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;
using AionDPS.Upload;

namespace AionDPS.Combat;

/// <summary>The CP calculator: the data file (Ludra's Blade of Extinction, its enhancement and breakthrough levels, the
/// stone ranges), the levels in the one number the game sends, the CP of a piece, its grade and the best piece of its kind, the ways to raise it in order,
/// the stones' values from the character page, and what a change of the gear did.</summary>
public static class SelfCheckCp
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] CP calculator:");
        Aion2CpData.ForgetLiveModel();
        CpModel model = Aion2CpData.Model;

        // the tables (4 November 2026): Ludra's Blade of Extinction (unique, level 102) - Max Attack 604, +15 adds 174, breakthrough 1 = Attack +14 and
        // Damage Boost 1 %, chances 66/50/33/25/20 %; a blue Attack stone gives 4 to 10 depending on the kind of stone
        CpItemData? ludra = Aion2CpData.Item(110120003);
        bool table = ludra is { Group: "Weapon_Unique_102", Category: "Greatsword", Level: 102 } && ludra.Base.GetValueOrDefault("WeaponDamage") == 604
            && Aion2CpData.MaxEnchant(ludra.Group) == 15 && Aion2CpData.Enchant(ludra.Group, 15).GetValueOrDefault("WeaponDamage") == 174
            && Aion2CpData.Exceed(ludra.Group, 1) is { } x1 && x1.GetValueOrDefault("WeaponFixingDamage") == 14 && x1.GetValueOrDefault("AmplifyAllDamage") == 100
            && Aion2CpData.MaxExceed(ludra.Group) == 5 && Aion2CpData.ExceedChance(ludra.Group, 1) == 66 && Aion2CpData.ExceedChance(ludra.Group, 5) == 20
            && Aion2CpData.Stone("WeaponFixingDamage", 3) == (4, 10) && ludra.Pool.Any(p => p is { Stat: "CombatSpeed", Min: 1123, Max: 1298 })
            && Aion2CpData.Token(282) == "CombatSpeed" && Aion2CpData.Percent("CombatSpeed") && !Aion2CpData.Percent("Critical")
            && Aion2CpData.Name("CombatSpeed", "de") == "Kampftempo" && Aion2CpData.Name("CombatSpeed", "zh") == "Combat Speed";
        Console.WriteLine($"  -> the data: Ludra's Blade 604 Max Attack, +15 adds 174, breakthrough 1 = Attack +14 and 1 % Damage Boost at 66 %, stones by colour: {table}");

        // the levels: the game sends +15 with breakthrough 2 as 17; up to the group's top level it is the plain enhancement
        bool levels = CpCalc.Levels(new Aion2EquippedItem(1, 110120003, 17), ludra) == (15, 2) && CpCalc.Levels(new Aion2EquippedItem(1, 110120003, 15), ludra) == (15, 0)
            && CpCalc.Levels(new Aion2EquippedItem(1, 110120003, 11), ludra) == (11, 0) && CpCalc.Levels(new Aion2EquippedItem(1, 110120003, 25), ludra) == (15, 5)
            && CpCalc.Levels(new Aion2EquippedItem(1, 999, 17), null) == (17, 0);
        Console.WriteLine($"  -> levels: 17 is +15 with breakthrough 2, 15 is +15, 11 is +11, past the top breakthrough level it stops at 5: {levels}");

        // the CP of stats: Attack 15 a point, Damage Boost 490 per 1 % (100 in the game's units); Might +0.1 % of the Attack total a point; Evasion not counted
        var totals = new CpTotals(3000, 1000, 2000, 30000, 4000);
        bool weights = model.Weight("atk") > 0 && Near(Aion2CpData.Cp("WeaponFixingDamage", 10, totals, model), 10 * model.Weight("atk"))
            && Near(Aion2CpData.Cp("AmplifyAllDamage", 100, totals, model), model.Weight("dmg")) && Near(Aion2CpData.Cp("STR", 10, totals, model), 3000 * 0.01 * model.Weight("atk"))
            && Aion2CpData.Cp("Evasion", 50, totals, model) is null && Aion2CpData.Cp("WeaponMinDamage", 50, totals, model) is null;
        Console.WriteLine($"  -> CP per stat: Attack {model.Weight("atk")} a point, Damage Boost {model.Weight("dmg")} per 1 %, Might from the Attack total, Evasion not counted: {weights}");

        // the sample set at +11: +1 enhancement = (+12 - +11) Max Attack x 15, no breakthrough before +15
        IReadOnlyList<Aion2EquippedItem> gear11 = Program.CpSampleGear(11);
        var (pieces11, totals11) = CpCalc.Evaluate(gear11, frame: null, model);
        IReadOnlyList<CpUpgrade> up11 = CpCalc.Upgrades(pieces11, totals11, model);
        double enhanceGain = (Aion2CpData.Enchant("Weapon_Unique_102", 12)["WeaponDamage"] - Aion2CpData.Enchant("Weapon_Unique_102", 11)["WeaponDamage"]) * model.Weight("atk");
        bool at11 = up11.FirstOrDefault(u => u.Kind == CpUpgradeKind.Enhance && u.ItemId == 110120003) is { ToLevel: 12 } e && Near(e.GainMin, enhanceGain)
            && !up11.Any(u => u.Kind == CpUpgradeKind.Breakthrough && u.ItemId == 110120003);
        Console.WriteLine($"  -> at +11: +12 gives {enhanceGain:0}, no breakthrough offered before +15: {at11}");

        // the sample set at +15 breakthrough 2: no enhancement left, breakthrough 3 = (43 - 28) x 15 + 1 % Damage Boost at 33 %; the stones a range, the MP line rerolled
        IReadOnlyList<Aion2EquippedItem> gear = Program.CpSampleGear(17);
        var (pieces, sampleTotals) = CpCalc.Evaluate(gear, frame: null, model);
        CpPiece weapon = pieces.First(p => p.ItemId == 110120003);
        IReadOnlyList<CpUpgrade> upgrades = CpCalc.Upgrades(pieces, sampleTotals, model);
        CpUpgrade? breakthrough = upgrades.FirstOrDefault(u => u.Kind == CpUpgradeKind.Breakthrough && u.ItemId == 110120003);
        CpUpgrade? stone = upgrades.FirstOrDefault(u => u.Kind == CpUpgradeKind.Stone && u.ItemId == 110120003);
        CpUpgrade? reroll = upgrades.FirstOrDefault(u => u.Kind == CpUpgradeKind.Reroll && u.ItemId == 110120003);
        double breakGain = 15 * model.Weight("atk") + model.Weight("dmg");
        bool piece = pieces.Count == gear.Count && weapon is { Enchant: 15, Exceed: 2, EmptyStones: 1 } && weapon.Lines.Count(l => l.Kind == CpLineKind.Soul) == 4
            && weapon.Lines.Count(l => l.Kind == CpLineKind.Stone) == 2 && weapon.Lines.Count(l => l.Kind == CpLineKind.Breakthrough) == 2 && weapon.CpMax > weapon.CpMin
            && weapon.Lines.First(l => l.Stat == "CombatSpeed").PoolMax == 1298;
        bool ranked = !upgrades.Any(u => u.Kind == CpUpgradeKind.Enhance && u.ItemId == 110120003)
            && breakthrough is { ToLevel: 3, Chance: 33.0 } && Near(breakthrough.GainMin, breakGain)
            && stone is { ToLevel: 1 } && stone.GainMax > stone.GainMin && reroll is { FromStat: "MPMax" } && reroll.GainMax > 0
            && upgrades.Zip(upgrades.Skip(1)).All(p => p.First.Mid >= p.Second.Mid);
        Console.WriteLine($"  -> at +15 breakthrough 2: {pieces.Count} pieces, weapon {weapon.CpMin:0}-{weapon.CpMax:0} CP (stones a range), breakthrough 3 {breakthrough?.GainMin:0} at 33 %, "
            + $"MP line rerolled up to +{reroll?.GainMax:0}, biggest first: {piece && ranked}");

        // the grade: the four soul-binding lines against the four best this weapon can roll (Combat Speed first, the only one it has); the letter by the score
        CpGrade? grade = CpCalc.Grade(weapon, sampleTotals, model);
        CpGrade? overall = CpCalc.Overall(pieces, sampleTotals, model);
        bool graded = grade is { Ideal.Count: 4, Have: 1 } && grade.Ideal[0].Stat == "CombatSpeed" && grade.Ideal[0].Max == 1298 && grade.Score is > 0 and < 100
            && grade.Letter == CpCalc.Letter(grade.Score) && overall is { Score: > 0 and <= 100 }
            && CpCalc.Letter(85) == "S+" && CpCalc.Letter(84.9) == "S" && CpCalc.Letter(55) == "A" && CpCalc.Letter(24.9) == "D";
        Console.WriteLine($"  -> grade: weapon {grade?.Letter} ({grade?.Score:0}), {grade?.Have} of {grade?.Ideal.Count} best lines, the whole gear {overall?.Letter} ({overall?.Score:0}): {graded}");

        // best in slot: Ludra's Blade is the top greatsword; a level-94 Wise Dragon Lord Breastplate is beaten by the level-102 Splendent Wise one (the same
        // faction's version of the pieces worth the same)
        CpBest? bisWeapon = CpCalc.BestInSlot(weapon, sampleTotals, model);
        CpPiece old = CpCalc.Piece(new Aion2EquippedItem(5, 210130023, 15, null, new[] { new Aion2RolledStat(193, 380), new Aion2RolledStat(307, 120) }), sampleTotals, model);
        CpBest? bisOld = CpCalc.BestInSlot(old, sampleTotals, model);
        bool bis = bisWeapon is { IsMine: true, ItemId: 110120003 } && bisOld is { IsMine: false, ItemId: 210130040 } && bisOld.Cp > bisOld.Mine;
        Console.WriteLine($"  -> best in slot: Ludra's Blade is, the level-94 breastplate gives way to {bisOld?.ItemId} (+{bisOld?.Cp - bisOld?.Mine:0} at their best): {bis}");

        // the stones from the character page: an Attack stone named "+8" on a blue slot is exact (8 x 15), a value its colour can't have stays a range
        using var doc = JsonDocument.Parse("""
            {"gear":[{"id":"110120003","enchantLevel":15,"exceedLevel":2,"details":{"magicStoneStat":[{"name":"Attack","value":"+8","grade":"Legend"},{"name":"Critical Hit","value":"+15"}]}},
                     {"id":310130040,"enchantLevel":12,"details":{"magicStoneStat":[{"name":"Attack","value":"+40"},{"name":"Damage Boost","value":"1.5%"}]}}]}
            """);
        IReadOnlyList<CpOfficialPiece> official = CpCharacterPage.Read(doc.RootElement);
        CpCalc.StonesOf stonesOf = item => CpCharacterPage.StonesFor(official, item);
        CpPiece exactWeapon = CpCalc.Piece(gear[0], sampleTotals, model, stonesOf);
        CpPiece necklace = CpCalc.Piece(gear.First(g => g.ItemId == 310130040), sampleTotals, model, stonesOf);
        CpLine? attackStone = exactWeapon.Lines.FirstOrDefault(l => l is { Kind: CpLineKind.Stone, Stat: "WeaponFixingDamage" });
        CpLine? critStone = exactWeapon.Lines.FirstOrDefault(l => l is { Kind: CpLineKind.Stone, Stat: "Critical" });
        bool stones = official.Count == 2 && official[0] is { ItemId: 110120003, Level: 17 } && official[1].Stones.Contains(("AmplifyAllDamage", 150))
            && attackStone is { Value: 8, ValueMax: 8 } && Near(attackStone.CpMin, 8 * model.Weight("atk")) && critStone is { Value: 15, ValueMax: 15 }
            && necklace.Lines.First(l => l is { Kind: CpLineKind.Stone, Stat: "WeaponFixingDamage" }) is { Value: 4, ValueMax: 10 }
            && CpCharacterPage.StonesFor(null, gear[0]) is null && CpCharacterPage.StonesFor(official, new Aion2EquippedItem(2, 110120099, 0)) is null;
        Console.WriteLine($"  -> stones from the character page: Attack +8 is {attackStone?.CpMin:0} CP exactly, a value the colour can't have stays 4-10: {stones}");

        // the page as the site's lookup for the app answers it (/meter/api/character-page, not the site pages' /api/lookup, which refuses a program): a
        // stone's stat by the game's id of it before its name ("Critical" whatever the name says), "+100" Weapon Damage Boost is 100 in the game's units
        using var page = JsonDocument.Parse("""
            {"ok":true,"checkedAt":"2026-11-04T10:00:00Z","character":{"name":"YIELD","combatPower":175556,"itemLevel":2832},
             "gear":[{"id":110830105,"enchantLevel":15,"exceedLevel":5,"details":{"magicStoneStat":[{"id":"Critical","name":"Kritischer Treffer","value":"+10","grade":"Unique"},
                 {"id":"AmplifyWeaponDamage","name":"Weapon Damage Boost","value":"+100"},{"id":"","name":"MP","value":"+30"},{"id":"Nothing","name":"Nothing","value":"+5"}]}}]}
            """);
        CpOfficialRead? read = CpCharacterPage.ReadPage(page.RootElement);
        string address = CpCharacterPage.PageUrl("eu", 2302, "YIELD", true);
        bool pageRead = read is { Cp: 175556, ItemLevel: 2832 } && read.At == new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc) && read.Pieces is [{ ItemId: 110830105, Level: 20 } onPage]
            && onPage.Stones.SequenceEqual(new[] { ("Critical", 10), ("AmplifyWeaponDamage", 100), ("MPMax", 30) })
            && address == "https://atreia.app/meter/api/character-page?region=eu&server=2302&name=YIELD&fresh=1"
            && CpCharacterPage.PageUrl("eu", 2302, "YIELD", false).EndsWith("&name=YIELD", StringComparison.Ordinal);
        Console.WriteLine($"  -> the page from the app's own lookup ({address}): {read?.Pieces.FirstOrDefault()?.Stones.Count} stones by the game's ids: {pageRead}");

        // a change: breakthrough 2 -> 3 is the breakthrough's gain; +11 -> +12 the enhancement's; another weapon in the slot is the difference of the two pieces
        DateTime at = new(2026, 11, 4, 8, 0, 0);
        IReadOnlyList<CpChange> broke = CpCalc.Changes(gear, Program.CpSampleGear(18), sampleTotals, at, model);
        IReadOnlyList<CpChange> enhanced = CpCalc.Changes(gear11, Program.CpSampleGear(12), totals11, at, model);
        var swapped = gear.Select(g => g.SlotIndex == 1 ? g with { ItemId = 110120014 } : g).ToList();
        IReadOnlyList<CpChange> swap = CpCalc.Changes(gear, swapped, sampleTotals, at, model);
        bool diff = broke is [{ Kind: CpChangeKind.Breakthrough, From: 2, To: 3 } b] && Near(b.Delta, breakGain)
            && enhanced is [{ Kind: CpChangeKind.Enhanced, From: 11, To: 12 } c] && Near(c.Delta, enhanceGain)
            && swap is [{ Kind: CpChangeKind.Equipped, OldItemId: 110120003, ItemId: 110120014 }]
            && CpCalc.Changes(gear, gear, sampleTotals, at, model).Count == 0;
        Console.WriteLine($"  -> changes: breakthrough 2 -> 3 shows +{breakGain:0} CP, +11 -> +12 +{enhanceGain:0}, another weapon a swap, the same gear nothing: {diff}");

        bool counted = Counting(model);
        bool strengths = Strength(model);
        bool packed = PackedModel();
        bool measured = Measuring(model);
        bool whole = Whole(model);
        bool costs = Costs(model);
        bool worn = WornTitles();
        bool skillLines = SkillLines(model);
        return table && levels && weights && at11 && piece && ranked && graded && bis && stones && pageRead && diff && counted && strengths && packed && measured && whole && costs
            && worn && skillLines;
    }

    /// <summary>
    /// A soul binding's skill lines are lines of the piece (made-up rolls): an Enraged Kromede Necklace with three stats and +1 to a skill has four lines,
    /// a Nuakum Breastplate with two stats and two skills four too. The skill levels count with the skills, so the piece's CP is that of its stats alone.
    /// </summary>
    private static bool SkillLines(CpModel model)
    {
        var totals = new CpTotals(3000, 1000, 2000, 30000, 4000);
        var stats = new[] { new Aion2RolledStat(282, 380), new Aion2RolledStat(193, 250), new Aion2RolledStat(5, 15) };
        CpPiece necklace = CpCalc.Piece(new Aion2EquippedItem(10, 310130051, 15, null, stats, 0, new[] { new Aion2SkillBonus(12350000, 1) }), totals, model);
        CpPiece bare = CpCalc.Piece(new Aion2EquippedItem(10, 310130051, 15, null, stats), totals, model);
        CpPiece chest = CpCalc.Piece(new Aion2EquippedItem(5, 210130053, 12, null, new[] { new Aion2RolledStat(317, 20), new Aion2RolledStat(307, 250) }, 0,
            new[] { new Aion2SkillBonus(12780000, 1), new Aion2SkillBonus(12760000, 1) }), totals, model);
        int Souls(CpPiece p) => p.Lines.Count(l => l.Kind == CpLineKind.Soul);
        bool ok = Souls(necklace) == 4 && necklace.Lines.Count(l => l.IsSkill) == 1 && Souls(chest) == 4 && chest.Lines.Count(l => l.IsSkill) == 2
            && necklace.Lines.Where(l => l.IsSkill).All(l => l.CpMin is null && l.Value == 1) && Math.Abs(necklace.CpMin - bare.CpMin) < 0.001
            && CpCalc.Grade(necklace, totals, model)?.Score == CpCalc.Grade(bare, totals, model)?.Score;
        Console.WriteLine($"  -> soul binding with skill lines: the Kromede necklace {Souls(necklace)} lines, the Nuakum chest {Souls(chest)}; the piece's CP that of its stats: {ok}");

        // the ideal lines of a Templar: six on the necklace (Combat Speed 4.4 % first), the necklace has two of them; the chest's Fury and Impact Hit lines are
        // two of its six (by the skill's name: the gear's Fury is another id than the skill's own); bracelets five; all eight classes, every slot
        int Have(CpPiece p) => Aion2IdealLines.For("Templar", p.ItemId)!.Count(i => p.Lines.Any(l => l.Kind == CpLineKind.Soul && Aion2IdealLines.Matches(i, l.Stat, l.SkillId)));
        var neck = Aion2IdealLines.For("Templar", 310130051);
        string[] slots = { "MainHand", "SubHand", "Helmet", "Shoulder", "Torso", "Pants", "Gloves", "Boots", "Cape", "Necklace", "Earring", "Ring", "Bracelet" };
        bool ideal = neck is { Count: 6 } && neck[0] is { Stat: "CombatSpeed", Top: 440, Worth: 100 } && Have(necklace) == 2 && Have(chest) == 3
            && Aion2IdealLines.ForSlot("Templar", "Bracelet") is { Count: 5 } && Aion2IdealLines.ForSlot("Chanter", "Torso")![3].Skill == "Attack Preparation"
            && Aion2IdealLines.Classes.Count == 8 && Aion2IdealLines.Classes.All(c => slots.All(slot => Aion2IdealLines.ForSlot(c, slot) is { Count: >= 5 }));
        Console.WriteLine($"  -> ideal lines per class: {string.Join(", ", Aion2IdealLines.Classes)}; the Templar necklace has {Have(necklace)} of 6, the chest {Have(chest)} of 6: {ideal}");

        // the best necklace for an Asmodian is never an Elyos one (the Kromede necklace both races wear showed the Splendent Wise one)
        CpBest? forD = CpCalc.BestInSlot(necklace, totals, model, 'D'), forL = CpCalc.BestInSlot(necklace, totals, model, 'L');
        char Race(CpBest? b) => b is null ? '?' : Aion2CpCosts.Piece(b.ItemId)?.Race ?? 'A';
        bool race = forD is not null && Race(forD) is 'D' or 'A' && forL is not null && Race(forL) is 'L' or 'A';
        Console.WriteLine($"  -> best necklace for an Asmodian {forD?.ItemId} ({Race(forD)}), for an Elyos {forL?.ItemId} ({Race(forL)}): {race}");
        return ok && ideal && race;
    }

    /// <summary>
    /// The login's title frame (0x7956) as the game sent it on 4 November 2026 (the owned list shortened to three): the three worn titles at its end, Offensive
    /// 23010021, Defensive 22030003, Other 23051004. The last id's high byte 01 with the slot 03 after it reads as "one title, slot 3" and ends at the end too;
    /// the run that starts first is the list.
    /// </summary>
    private static bool WornTitles()
    {
        var entities = new Aion2EntityDirectory();
        var decoder = new Aion2FrameDecoder(Aion2Protocol.Load(), entities);
        byte[] frame = Convert.FromHexString("7956" + "03" + "e51a5f01" + "b3265001" + "fcba5f01" + "03" + "01e51a5f01" + "02b3265001" + "03fcba5f01");
        _ = decoder.Decode(frame, new DateTime(2026, 11, 4, 12, 22, 58, DateTimeKind.Utc)).ToArray();
        IReadOnlyList<Aion2TitleSlot> titles = entities.LocalTitles;
        bool ok = titles.Count == 3 && titles[0] == new Aion2TitleSlot(1, 23010021) && titles[1] == new Aion2TitleSlot(2, 22030003) && titles[2] == new Aion2TitleSlot(3, 23051004)
            && entities.OwnedTitleIds is { Count: 3 } owned && owned.Contains(22030003);
        Console.WriteLine($"  -> worn titles at login: {string.Join(", ", titles.Select(t => $"slot {t.Slot} {t.TitleId}"))}: {ok}");
        return ok;
    }

    /// <summary>
    /// What upgrades cost and which swaps the steps offer (the tables of 4 November 2026): +10 to +11 of a level-86 Unique piece 65 % a try, +5 points a
    /// failure, 310,000 Kina and 7,850 Enhance Stones a try, 1.487 tries on average; breakthrough 1 66 % for 600,000 Kina and 6 Amplify Stones (Unique);
    /// a Rebind of a Unique piece 300,000 Kina and 3 Soul Codex: Reset. A crafted Splendent Dark Dragon Lord Guard is carried over into the Ebony one by
    /// Transfer Crafting (25 % the Splendent Ebony one), not swapped; no crafted piece is offered for a piece below its top unless the character has it,
    /// and never one of the other race.
    /// </summary>
    private static bool Costs(CpModel model)
    {
        CpTry? plus11 = Aion2CpCosts.Enchant(210330053, 10);
        CpTry? first = Aion2CpCosts.Exceed(110230105, 0);
        var rebind = Aion2CpCosts.Rebind(110230105);
        IReadOnlyList<CpTransfer> guard = Aion2CpCosts.Transfers(115030099);
        bool tables = plus11 is { Chance: 65, Pity: 5, Kina: 310_000, Count: 7_850 } && plus11.MaterialId == Aion2CpCosts.StoneId && Math.Abs(plus11.Tries - 1.48737) < 0.0001
            && Aion2CpCosts.Enchant(210330053, 0) is { Chance: 100, Kina: 10_800, Count: 430 } && Aion2CpCosts.Enchant(210330053, 15) is null
            && first is { Chance: 66, Pity: 5, Kina: 600_000, MaterialId: 516530001, Count: 6 } && Aion2CpCosts.Exceed(110230105, 4) is { Chance: 20, Pity: 1, Kina: 3_000_000, Count: 20 }
            && rebind is (300_000, 535200001, 3) && Aion2CpCosts.KinaId == 930100031
            && guard is [{ OutputId: 115030101, ComboId: 115030103, ComboChance: 25, Kina: 0 } t] && t.Materials.Count == 8 && t.Materials.All(m => m.ItemId > 0 && m.Count > 0)
            && Aion2CpCosts.Piece(115030099) is { Race: 'D', Crafted: true } && Aion2CpCosts.Piece(310130040) is { Race: 'L', Crafted: true }
            && Aion2CpCosts.Piece(115020003) is { Race: 'A', Crafted: false } && Aion2CpCosts.Transfers(110230105).Count == 0;
        Console.WriteLine($"  -> costs: +10 -> +11 {plus11?.Chance} % +{plus11?.Pity} a failure, {plus11?.Kina:N0} Kina and {plus11?.Count:N0} stones a try, {plus11?.Tries:0.000} tries; "
            + $"breakthrough 1 {first?.Kina:N0} Kina + {first?.Count} Amplify Stones; Rebind {rebind?.Kina:N0}; the Dark guard -> {guard.FirstOrDefault()?.OutputId} "
            + $"({guard.FirstOrDefault()?.ComboChance} % {guard.FirstOrDefault()?.ComboId}): {tables}");

        // a Templar (Asmodian: the crafted Dark Dragon Lord guard and longsword) with the guard at +17, the Kromede necklace at +15 and the Nuakum helm at +10
        var gear = new List<Aion2EquippedItem>
        {
            new(1, 110230105, 18), new(2, 115030099, 17), new(10, 310130051, 15), new(3, 210330053, 10),
        };
        CpWholeResult none = CpWhole.Evaluate(new CpWholeInput("Templar", gear), model);
        CpStep? transfer = none.Steps.FirstOrDefault(x => x.Kind == CpStepKind.Transfer);
        CpStep? helm = none.Steps.FirstOrDefault(x => x is { Kind: CpStepKind.Enhance, ItemId: 210330053 });
        bool rules = CpWhole.RaceOf(gear) == 'D'
            && transfer is { Slot: 2, OtherId: 115030101, ComboId: 115030103, Certainty: CpCertainty.Average, Gain: >= 1, Group: CpStepGroup.Gear } && transfer.GainMax >= transfer.Gain
            && transfer.Cost is { Kina: 0, Materials.Count: 8 }
            && !none.Steps.Any(x => x.Kind == CpStepKind.BetterItem && (x.Slot == 2 || Aion2CpCosts.Piece(x.OtherId) is { Crafted: true } || Aion2CpCosts.Piece(x.OtherId)?.Race == 'L'))
            && helm is { ToLevel: 11, Certainty: CpCertainty.Chance, Chance: 0.65, Cost: { Average: true } c } && Math.Abs(c.Kina - 310_000 * 1.48737) < 50
            && c.Materials is [{ Count: var stones }] && Math.Abs(stones - 7_850 * 1.48737) < 1 && helm.PerMillion > 0;

        // holding the Splendent Ebony Dragon Lord Necklace: offered for free, though crafted and the necklace not at its top
        CpWholeResult held = CpWhole.Evaluate(new CpWholeInput("Templar", gear, Held: id => id == 310130120 ? 1 : 0), model);
        CpStep? necklace = held.Steps.FirstOrDefault(x => x is { Kind: CpStepKind.BetterItem, Slot: 10, Held: true });
        bool holding = necklace is { OtherId: 310130120, Group: CpStepGroup.Free, Cost.IsFree: true, Gain: >= 1 };
        Console.WriteLine($"  -> steps: the guard by Transfer Crafting +{transfer?.Gain:0} on average (up to +{transfer?.GainMax:0}), no crafted swap below the top, no Elyos piece, "
            + $"the helm +11 costs {helm?.Cost?.Kina:N0} Kina on average; a held necklace for free +{necklace?.Gain:0}: {rules && holding}");
        return tables && rules && holding;
    }

    /// <summary>
    /// The whole character (<see cref="CpWhole"/>): the parts add up to the model's Combat Power (a Genus Insight line of Attack and the Might that raises it share what
    /// they make together), skill levels count on the bar and passive, a Stigma by its level bands, every pet by its genus and level, owned titles once each,
    /// a new Analysis on average by the game's odds, the steps biggest first, and the fit to the game's own number.
    /// </summary>
    private static bool Whole(CpModel model)
    {
        // Attack +100 from a Genus Insight line and 100 Might (+10 % Attack): 15 x 100 x 1.1 = 1,650 CP, the line 1,575 (its 100 points and half the
        // increase they share), the Might 75; Templar's base 4,779 on top; the second preset of the board doesn't count
        var genus = new[] { new Aion2SpeciesKnowledge(2, 10, 0, new[] { new Aion2SpeciesEffect(1, 0, 31, 100, 5), new Aion2SpeciesEffect(2, 0, 31, 900, 5) }) };
        var simple = new CpWholeInput("Templar", Array.Empty<Aion2EquippedItem>(), Attributes: new Dictionary<int, int> { [1] = 100 }, Genus: genus);
        CpWholeResult s = CpWhole.Evaluate(simple, model);
        double atk = model.Weight("atk");
        bool split = Near(s.Parts[CpSource.Genus], atk * 100 * 1.05) && Near(s.Parts[CpSource.Attributes], atk * 100 * 0.1 / 2) && Near(s.Parts[CpSource.Base], model.BaseOf("Templar"))
            && Near(s.Total, model.BaseOf("Templar") + atk * 100 * 1.1) && model.BaseOf("Templar") > 4000 && model.BaseOf("Elementalist") == model.BaseOf("Spiritmaster")
            && Near(Aion2CpData.Cp("WeaponDamage", 1, s.Totals.Marginal, model), atk * 1.1);
        Console.WriteLine($"  -> the whole character adds up: Attack +100 (Genus Insight, the first preset) with Might 100 = {s.Total - s.Parts[CpSource.Base]:0} CP, line {s.Parts[CpSource.Genus]:0} + Might {s.Parts[CpSource.Attributes]:0}: {split}");

        // skills: 16 + 10 on the bar, a passive 12, an active 20 not on the bar, a variant id that is no skill of the table; Stigmas: 15 equipped (6,400 x the
        // weight; level 16 +1,200), 20 equipped (12,400, at the top), 9 not equipped
        var skills = new[]
        {
            new Aion2SkillEntry(11010000, 16, 10, Equipped: true), new Aion2SkillEntry(11020000, 10, 10, Equipped: true), new Aion2SkillEntry(11710000, 12, 10),
            new Aion2SkillEntry(11030000, 20, 10), new Aion2SkillEntry(11250000, 15, 15, Stigma: true, Equipped: true), new Aion2SkillEntry(11270000, 9, 9, Stigma: true),
            new Aion2SkillEntry(11310000, 20, 20, Stigma: true, Equipped: true),
            new Aion2SkillEntry(11010340, 16, 10),
        };
        CpWholeResult k = CpWhole.Evaluate(new CpWholeInput("Templar", Array.Empty<Aion2EquippedItem>(), Skills: skills), model);
        bool levels = Near(k.Parts[CpSource.Skills], 38 * model.Weight("skill")) && Near(Aion2CpSources.StigmaPoints(20), 12_400) && Near(Aion2CpSources.StigmaPoints(5), 900)
            && Near(k.Parts[CpSource.Stigmas], (6_400 + 12_400) * model.Weight("stigma")) && k.Stigmas is [{ Equipped: true, Level: 20, NextLevel: 0 }, { Equipped: true, Level: 15 } st, { Equipped: false }]
            && Near(st.NextLevel, 1_200 * model.Weight("stigma")) && k.Skills.Count == 4 && k.Skills.Single(x => x.SkillId == 11010000) is { SpecOpen: 2, NextSpecAt: 20, Counted: true }
            && k.Steps.Count(x => x.Kind == CpStepKind.Stigma) == 1 && k.Steps.Any(x => x is { Kind: CpStepKind.Stigma, FromLevel: 15, ToLevel: 20, Shards: 40 })
            && k.Steps.Count(x => x.Kind == CpStepKind.Skill) == 3 && k.Steps.Any(x => x is { Kind: CpStepKind.Skill, FromLevel: 10, ToLevel: 15 })
            && Near(k.Steps.Single(x => x.Kind == CpStepKind.Stigma).Gain, 6_000 * model.Weight("stigma"));
        Console.WriteLine($"  -> skill levels {k.Parts[CpSource.Skills]:0} CP (38 levels: on the bar and passive), Stigmas 15 and 20 {k.Parts[CpSource.Stigmas]:0} CP, each to its next rank: {levels}");

        // the sample character: pets by genus and level (a Cogni pet at 3 = 30 HP, 2 Critical Hit, 1 Might), owned titles, the wing, a board's lines with a new
        // Analysis on average, the parts all there, every step at least 1 CP and the biggest first, the fit to a game number 5 % under the calculator
        IReadOnlyList<Aion2EquippedItem> gear = Program.CpSampleGear(17).Concat(Program.CpSampleArcana()).ToList();
        Aion2SavedCharacter c = Program.CpSampleCharacter(gear);
        var input = new CpWholeInput("Gladiator", gear, null, c.Attributes,
            c.Skills.Select(x => new Aion2SkillEntry(x.Id, x.Level, x.BaseLevel, x.Stigma, x.Equipped, x.Stones)).ToList(),
            c.Daevanion.Select(b => new Aion2DaevanionBoard(b.Board, b.Nodes)).ToList(),
            c.Titles.Select(t => new Aion2TitleSlot(t.Slot, t.TitleId)).ToList(), c.OwnedTitles, c.WingId, c.OwnedWings,
            c.Species.Select(x => new Aion2SpeciesKnowledge(x.Id, x.Level, x.Progress, x.Effects.Select(e => new Aion2SpeciesEffect(e.Page, e.Slot, e.Stat, e.Value, e.Kind)).ToList())).ToList(),
            c.Pets.Select(p => new Aion2PetState(p.Id, p.Level, p.Progress)).ToList());
        CpWholeResult r = CpWhole.Evaluate(input, model);
        IReadOnlyDictionary<string, int> cogni = Aion2CpSources.PetStats("Intellect", 3);
        CpPetRow? cogniRow = r.Pets.FirstOrDefault(p => p.Key == "Intellect");
        CpGenusRow? special = r.Genus.FirstOrDefault(g => g.SpeciesId == 6);
        CpGenusLine? dbl = special?.Lines.FirstOrDefault(l => l.Stat == "HardHit");
        double baseCp = r.Parts[CpSource.Base];
        double? fit = CpWhole.Fit((int)Math.Round(baseCp + (r.Total - baseCp) * 0.95), input, model);
        bool sample = cogni.GetValueOrDefault("HPMax") == 30 && cogni.GetValueOrDefault("Critical") == 2 && cogni.GetValueOrDefault("STR") == 1
            && cogniRow is { Cp: > 0 } && cogniRow.ByLevel.Sum() > 10 && cogniRow.StepGain[2] > 0 && cogniRow.StepGain[0] > 0
            && Aion2CpSources.Genus("Special")?.OddsAt(10) is { Length: 5 } odds && Near(odds.Sum(), 1) && Near(odds[4], 0.05)
            && dbl is { Slot: 4, Grade: 5, Cp: > 0 } && dbl.Average > 0 && dbl.Better is > 0 and < 1 && dbl.Best >= dbl.Now
            && r.Parts.Where(p => p.Key != CpSource.Attributes).All(p => p.Value > 0) && r.Titles.Count == 2 && r.OwnedTitleCount > 100 && r.Wing is { Cp: > 0 }
            && r.Boards.Count == 6 && r.Boards.All(b => b.Open > 0) && r.Boards.Count(b => b.Cp > 0) >= 5 && r.Arcana.Count == 5 && r.Arcana.All(a => a.GodStat is not null && a.Cp > 0)
            && r.Steps.Count > 5 && r.Steps.All(x => Math.Max(x.Gain, x.GainMax) >= 1) && r.Steps.Zip(r.Steps.Skip(1)).All(x => x.First.Rank >= x.Second.Rank - 0.001)
            && fit is double f && Math.Abs(f - 0.95) < 0.001 && CpWhole.Fit(10, input, model) is null && CpWhole.Fit(null, input, model) is null
            && Math.Abs(CpWhole.Evaluate(input, model, 0.95).Total - baseCp - (r.Total - baseCp) * 0.95) < 0.5;
        Console.WriteLine($"  -> the sample character: {r.Total:N0} CP in {r.Parts.Count(p => p.Value > 0.5)} parts, pets {r.Parts[CpSource.Pets]:0}, Genus Insight {r.Parts[CpSource.Genus]:0}"
            + $" (Double Chance {dbl?.Value / 100:0.##} %: a new Analysis {dbl?.Average:0} on average, {dbl?.Better:P0} better), {r.Steps.Count} steps, fit x{fit:0.000}: {sample}");
        foreach (CpStep step in r.Steps.Take(8))
        {
            Console.WriteLine($"       {step.Kind} {step.Gain:0}..{step.GainMax:0} {step.Certainty} {step.Chance:0.00} slot {step.Slot} {step.Key}{step.OtherId}");
        }

        return split && levels && sample;
    }

    /// <summary>
    /// A point of Attack counts with the character's Attack increase (the soul-binding lines and 0.1 % a point of Might and of Destruction), as the website's model
    /// counts Attack; "Attack increase" itself takes its share of the Attack points; the Item Level's progression factor and a measured factor go on top; a
    /// percentage of the attack counts with the offence the stat list gives (<see cref="CpCalc.Strength"/>).
    /// </summary>
    private static bool Counting(CpModel model)
    {
        var frame = new Dictionary<int, int>
        {
            [31] = 1200, [317] = 1300, [19] = 300, [425] = 200, [128] = 1000, [104] = 1800, [193] = 30000, [52] = 3000, [1] = 80, [13] = 30, [5] = 60, [14] = 40, [8] = 35, [4] = 40, [10] = 35,
        };
        var gear = new List<Aion2EquippedItem> { new(1, 110120003, 15, null, new[] { new Aion2RolledStat(425, 200) }) };
        var plain = new CpModel(model.Weights, model.Confidence, false, 0.05, 2500);
        var (_, totals) = CpCalc.Evaluate(gear, frame, plain, character: new CpCharacter(3500));
        bool increases = Near(totals.AttackIncrease, 0.02 + 0.11) && Near(totals.CritIncrease, 0.1) && Near(totals.AccuracyIncrease, 0.095) && Near(totals.HpIncrease, 0.075)
            && Near(totals.Scale, 1.05) && Near(totals.Attack, 2800);

        // the offence and guard from the stat list: Attack 2,800 x 15 x 1.13, Critical Hit 1,000 x 8 x 1.1, Accuracy 1,800 x 6 x 1.095; HP 30,000 x 0.15 x
        // 1.075 and Defense 3,000; a percentage of the attack at 68,086 / 170,000 of its weight, without a stat list at all of it
        double percent = Aion2CpData.PercentFactor(totals, plain);
        bool strength = Near(totals.Offense, 2800 * plain.Weight("atk") * 1.13 + 1000 * plain.Weight("crit") * 1.1 + 1800 * plain.Weight("acc") * 1.095)
            && Near(totals.Guard, 30000 * plain.Weight("hp") * 1.075 + 3000 * plain.Weight("def")) && Near(percent, totals.Offense / Aion2CpData.OffenseReference)
            && percent is > 0.3 and < 0.5 && CpCalc.Totals(null, CpCalc.Pieces(gear, new CpTotals(0, 0, 0, 0, 0), plain), null, plain) is { Offense: 0, Guard: 0 };
        bool attack = Near(Aion2CpData.Cp("WeaponFixingDamage", 10, totals, plain), 10 * plain.Weight("atk") * 1.13 * 1.05)
            && Near(Aion2CpData.Cp("DamageRatio", 100, totals, plain), 2800 * 0.01 * plain.Weight("atk") * 1.05)
            && Near(Aion2CpData.Cp("AmplifyAllDamage", 100, totals, plain), plain.Weight("dmg") * 1.05 * percent);
        var measuredTotals = totals with { Factors = new Dictionary<string, double> { ["atk"] = 1.1 } };
        bool factor = Near(Aion2CpData.Cp("WeaponFixingDamage", 10, measuredTotals, plain), 10 * plain.Weight("atk") * 1.13 * 1.05 * 1.1)
            && Near(Aion2CpData.Cp("AmplifyAllDamage", 100, measuredTotals, plain), plain.Weight("dmg") * 1.05 * percent)
            && plain.Progression(null) == 1 && Near(plain.Progression(2000), 0.975);
        IReadOnlyList<CpPerUnit> perUnit = CpCalc.PerUnit(totals, plain);
        CpPerUnit? onePoint = perUnit.FirstOrDefault(u => u.Stat == "WeaponFixingDamage");
        bool table = onePoint is { Amount: 1 } && Near(onePoint.Cp, plain.Weight("atk") * 1.13 * 1.05)
            && perUnit.FirstOrDefault(u => u.Stat == "AmplifyAllDamage") is { Amount: 100, Key: "dmg" };
        Console.WriteLine($"  -> counting: Attack +13 % (an increase line and Might, Destruction), Item Level 3,500 x1.05: a point of Attack {onePoint?.Cp:0.##} CP, measured x1.1 on top; "
            + $"offence {totals.Offense:N0}, so 1 % Damage Boost {Aion2CpData.Cp("AmplifyAllDamage", 100, totals, plain):0} CP ({percent:P1} of its weight), guard {totals.Guard:N0}: "
            + $"{increases && strength && attack && factor && table}");
        return increases && strength && attack && factor && table;
    }

    /// <summary>
    /// Percentages count with the character's strength (measured in the game, 3 and 4 November 2026): the same Genus Insight board - which every character
    /// of an account on a server shares - gives a character with a quarter of the offence and guard a quarter of its PvE Damage Boost and Damage Tolerance,
    /// and the same CP for its points (Critical Hit; PvE Defense a fifth of Defense). The meter's own weights (Damage Tolerance, Regeneration, MP ...) count
    /// where the website has none, its own wins; the strength is the game's stat list where the calculator has it; the CP measurements' upload counts plain
    /// amounts and only the website's stats.
    /// </summary>
    private static bool Strength(CpModel model)
    {
        var strong = new Dictionary<int, int>
        {
            [31] = 1200, [317] = 1300, [19] = 300, [425] = 200, [128] = 1000, [104] = 1800, [193] = 30000, [52] = 3000, [1] = 80, [13] = 30, [5] = 60, [14] = 40, [8] = 35, [4] = 40, [10] = 35,
        };
        var weak = strong.ToDictionary(kv => kv.Key, kv => kv.Key is 31 or 317 or 19 or 128 or 104 or 193 or 52 ? kv.Value / 4 : kv.Value);
        int pve = Aion2CpData.StatId("PvEAmplifyDamage"), pveDefense = Aion2CpData.StatId("PvEDamageDefense"), tolerance = Aion2CpData.StatId("DecreaseDamage");
        var board = new[]
        {
            new Aion2SpeciesKnowledge(2, 10, 0, new[]
            {
                new Aion2SpeciesEffect(1, 1, pveDefense, 100, 5), new Aion2SpeciesEffect(1, 2, 128, 30, 5), new Aion2SpeciesEffect(1, 3, pve, 300, 5), new Aion2SpeciesEffect(1, 6, tolerance, 252, 5),
            }),
        };
        CpWholeResult Of(Dictionary<int, int> stats) => CpWhole.Evaluate(new CpWholeInput("Templar", Array.Empty<Aion2EquippedItem>(), Genus: board, Stats: stats), model);
        CpWholeResult a = Of(strong), b = Of(weak);
        double? Line(CpWholeResult r, string stat) => r.Genus.SelectMany(g => g.Lines).FirstOrDefault(l => l.Stat == stat)?.Cp;
        var (offense, guard) = CpCalc.Strength(strong, model);
        bool scaled = Near(a.Totals.Offense, offense) && Near(a.Totals.Guard, guard) && Near(b.Totals.Offense, offense / 4) && Near(b.Totals.Guard, guard / 4)
            && Line(a, "PvEAmplifyDamage") is double pa && Line(b, "PvEAmplifyDamage") is double pb && Near(pa, 3 * model.Weight("pve") * offense / Aion2CpData.OffenseReference) && Near(pb, pa / 4)
            && Line(a, "DecreaseDamage") is double ta && Line(b, "DecreaseDamage") is double tb && Near(ta, 2.52 * model.Weight("dmgRed") * guard / Aion2CpData.GuardReference) && Near(tb, ta / 4)
            && Line(a, "Critical") is double ca && Near(ca, 30 * model.Weight("crit")) && Line(b, "Critical") is double cb && Near(cb, ca)
            && Line(a, "PvEDamageDefense") is double da && Near(da, 100 / 5.0 * model.Weight("def")) && Line(b, "PvEDamageDefense") is double db && Near(db, da)
            && Near(a.Parts[CpSource.Genus], pa + ta + ca + da) && b.Parts[CpSource.Genus] < a.Parts[CpSource.Genus];

        // the meter's own weights at the guard they were measured at; the website's weight once it has one; the factor's bounds; plain amounts for the upload
        var atReference = new CpTotals(0, 0, 0, 0, 0, Offense: Aion2CpData.OffenseReference, Guard: Aion2CpData.GuardReference);
        var site = model with { Weights = new Dictionary<string, double>(model.Weights) { ["dmgRed"] = 200 } };
        CpModel unit = AionDPS.Upload.CpShare.Unit(model);
        bool own = Near(Aion2CpData.Cp("DecreaseDamage", 100, atReference, model), 151) && Near(Aion2CpData.Cp("Restoration", 100, atReference, model), 19)
            && Near(Aion2CpData.Cp("MPMax", 100, atReference, model), 27) && Near(Aion2CpData.Cp("AmplifyAllDamage", 100, atReference, model), model.Weight("dmg"))
            && Near(Aion2CpData.Cp("DecreaseDamage", 100, atReference, site), 200) && Aion2CpData.Cp("Evasion", 100, atReference, model) is null
            && Near(Aion2CpData.PercentFactor(atReference with { Offense = 1 }, model), 0.05) && Near(Aion2CpData.PercentFactor(atReference with { Offense = 1e7 }, model), 1.5)
            && Near(Aion2CpData.PercentFactor(atReference with { Offense = 0 }, model), 1) && Near(Aion2CpData.GuardFactor(atReference with { Guard = 0 }, model), 1)
            && Aion2CpData.Cp("DecreaseDamage", 100, a.Totals, unit) is null && Aion2CpData.Cp("MPMax", 100, a.Totals, unit) is null
            && Near(Aion2CpData.Cp("PvEAmplifyDamage", 100, a.Totals, unit), 1) && Near(Aion2CpData.Cp("WeaponFixingDamage", 10, a.Totals, unit), 10);
        Console.WriteLine($"  -> percentages count with the strength: the same board, PvE Damage Boost 3 % {Line(a, "PvEAmplifyDamage"):0} CP at offence {offense:N0}, "
            + $"{Line(b, "PvEAmplifyDamage"):0} at a quarter of it; Damage Tolerance 2.52 % {Line(a, "DecreaseDamage"):0} and {Line(b, "DecreaseDamage"):0}; "
            + $"Critical Hit 30 {Line(a, "Critical"):0} and {Line(b, "Critical"):0}, PvE Defense 100 {Line(a, "PvEDamageDefense"):0}; the meter's own weights, the website's first, "
            + $"plain amounts for the upload: {scaled && own}");
        return scaled && own;
    }

    /// <summary>The website's weekly model comes scrambled (every printable character swapped for the one at its place in a key): read back exactly.</summary>
    private static bool PackedModel()
    {
        string text = "{\"w\":{\"atk\":15.5,\"dmg\":480},\"ilMult\":0.04,\"ilRef\":2600,\"note\":\"a < b & \\\"c\\\"\"}";
        var key = Enumerable.Range(0, 95).Select(i => i == 28 ? '\uE000' : (char)(32 + i)).Reverse().ToArray();
        string Enc(string s) => new(s.Select(c => c >= 32 && c <= 126 ? key[c - 32] : c).ToArray());
        string Lit(string s) => JsonSerializer.Serialize(s).Replace("<", "\\x3c", StringComparison.Ordinal);
        string js = $"(function(D){{window[D({Lit(Enc("A2_CPMODEL"))},{Lit(new string(key))})]=JSON.parse(D({Lit(Enc(text))},{Lit(new string(key))}));}})(function(s,k){{return s}});\n";
        bool ok = Aion2CpData.ModelJson(js) == text && Aion2CpData.ModelJson("window.A2_CPMODEL = {\"w\":{}};") == "{\"w\":{}}";
        Console.WriteLine($"  -> the website's weekly model file, scrambled, reads back exactly: {ok}");
        return ok;
    }

    /// <summary>
    /// The login checks: two readings of the game's Combat Power with only the weapon enhanced in between (+14 -> +15) measure the change; the game's stats moved
    /// as the tables say; the factor that comes of it reproduces the game's number (held a little towards 1). A changed skill, or a stat the weapon doesn't touch
    /// moving too, is no measurement; a stat the tables get wrong shows as a gap.
    /// </summary>
    private static bool Measuring(CpModel model)
    {
        var frameBefore = new Dictionary<int, int> { [31] = 1200, [317] = 1300, [19] = 300, [128] = 1000, [104] = 1800, [193] = 30000, [52] = 3000, [1] = 80, [13] = 30 };
        IReadOnlyList<Aion2EquippedItem> before = Program.CpSampleGear(14), after = Program.CpSampleGear(15);
        Dictionary<int, long> sumsBefore = CpCalc.StatSums(CpCalc.Pieces(before, new CpTotals(0, 0, 0, 0, 0)));
        Dictionary<int, long> sumsAfter = CpCalc.StatSums(CpCalc.Pieces(after, new CpTotals(0, 0, 0, 0, 0)));
        Dictionary<int, int> Moved(int extra = 0, int extraId = 0)
        {
            var f = new Dictionary<int, int>(frameBefore);
            foreach (int id in sumsBefore.Keys.Union(sumsAfter.Keys))
            {
                f[id] = f.GetValueOrDefault(id) + (int)(sumsAfter.GetValueOrDefault(id) - sumsBefore.GetValueOrDefault(id));
            }

            if (extraId != 0)
            {
                f[extraId] = f.GetValueOrDefault(extraId) + extra;
            }

            return f;
        }

        DateTime t0 = new(2026, 11, 4, 8, 0, 0, DateTimeKind.Utc), t1 = t0.AddHours(3);
        CpCheckPoint p0 = CpChecks.Point(t0, 140_000, 3000, before, frameBefore, "rest");
        var (parts, width) = CpChecks.Predicted(p0.Gear, CpChecks.Point(t1, 0, 3000, after, Moved(), "rest").Gear, Moved(), 3000, null, model);
        double predicted = parts.Values.Sum();
        int actual = (int)Math.Round(predicted * 1.1);
        CpCheckPoint p1 = CpChecks.Point(t1, 140_000 + actual, 3000, after, Moved(), "rest");
        CpMeasurement? m = CpChecks.Measure(p0, p1);
        IReadOnlyDictionary<string, double> f = CpChecks.Solve(new[] { (parts, (double)actual) });
        double expected = (actual / predicted + 0.1) / 1.1;
        bool measured = m is { Actual: var got, Gaps.Count: 0 } && got == actual && width == 0 && parts.Keys.All(k => k == "atk") && predicted > 150
            && f.TryGetValue("atk", out double fa) && Math.Abs(fa - expected) < 0.001;

        bool skill = CpChecks.Measure(p0, p1 with { Rest = "rest, a skill up" }) is null;
        bool hidden = CpChecks.Measure(p0, CpChecks.Point(t1, 140_000 + actual, 3000, after, Moved(500, 193), "rest")) is null;
        CpMeasurement? gap = CpChecks.Measure(p0, CpChecks.Point(t1, 140_000 + actual, 3000, after, Moved(1, 31), "rest"));
        long tables = sumsAfter.GetValueOrDefault(31) - sumsBefore.GetValueOrDefault(31);
        bool gaps = gap is { Gaps: [{ StatId: 31 } g] } && g.Tables == tables && g.Game == tables + 1;

        bool shared = m is not null && Sharing(m with { ItemLevelBefore = 2988 }, model);
        bool stored = false;
        if (m is not null)
        {
            CpChecks.Put("Selftest", 1001, new CpCheckState { Points = new() { p0, p1 }, Measured = new() { m } });
            stored = CpChecks.Factors("Selftest", 1001, model).TryGetValue("atk", out double fs) && Math.Abs(fs - expected) < 0.001
                && CpChecks.MeasuredChanges("Selftest", 1001, model) is [{ Changes: [{ Kind: CpChangeKind.Enhanced, From: 14, To: 15 }] } shown] && shown.Actual == actual;
            CpChecks.Put("Selftest", 1001, null);

            // a legion list sent again before the game counted the change: the same number with the new pieces is no baseline; the next reading measures
            CpChecks.Add("Selftest", 1001, p0);
            CpChecks.Add("Selftest", 1001, p1 with { Cp = p0.Cp, At = t0.AddHours(1) });
            CpChecks.Add("Selftest", 1001, p1);
            stored &= CpChecks.State("Selftest", 1001) is { Points: [{ Stale: false }, { Stale: true }, { Stale: false }], Measured: [{ Actual: var later }] } && later == actual;
            CpChecks.Put("Selftest", 1001, null);
        }
        Console.WriteLine($"  -> login checks: weapon +14 -> +15 measured (game +{actual}, calculator +{predicted:0}), factor {expected:0.000}; a skill up or a moving stat is no measurement, "
            + $"a stat the tables miss shows: {measured && skill && hidden && gaps && stored}");
        return measured && skill && hidden && gaps && stored && shared;
    }

    /// <summary>
    /// A measured change as it is shared (Upload/CpShare.cs): no name, server or legion in it, the Item Level and Combat Power rounded, and the stats' changes
    /// such that the model's weights and its progression give back the calculator's own number. A change with a stat the tables missed, or without the first
    /// login's Item Level, isn't sent.
    /// </summary>
    private static bool Sharing(CpMeasurement m, CpModel model)
    {
        string anonymous = new('a', 32);
        DateTime now = new(2026, 11, 4, 9, 0, 0, DateTimeKind.Utc);
        string? json = AionDPS.Upload.CpShare.Build(m, 9, 1303, anonymous, now, model); // Templar (4 x 2 + 1), Aahz (Europe)
        bool shape = false, sums = false;
        double fromWeights = 0, predicted = 0;
        if (json is not null)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            int cp = (int)Math.Round(m.CpAfter / 1000.0, MidpointRounding.AwayFromZero) * 1000;
            shape = root.GetProperty("kind").GetString() == "cp-change" && root.GetProperty("anonymousId").GetString() == anonymous && root.GetProperty("cls").GetString() == "Templar"
                && root.GetProperty("region").GetString() == "eu" && root.GetProperty("day").GetString() == "2026-11-04" && root.GetProperty("il").GetInt32() == 3000
                && root.GetProperty("dil").GetInt32() == 12 && root.GetProperty("cp").GetInt32() == cp && root.GetProperty("dcp").GetInt32() == m.Actual
                && !json.Contains("Selftest", StringComparison.Ordinal) && !root.TryGetProperty("name", out _) && !root.TryGetProperty("server", out _) && !root.TryGetProperty("serverId", out _);
            foreach (var p in root.GetProperty("d").EnumerateObject())
            {
                fromWeights += p.Value.GetDouble() * model.Weight(p.Name) * model.Progression(m.ItemLevel);
            }

            predicted = CpChecks.Predicted(m.Before, m.After, m.Frame, m.ItemLevel, null, model).Parts.Values.Sum();
            sums = predicted > 0 && Math.Abs(fromWeights - predicted) < 0.002 * predicted + 0.05;
        }

        bool skipped = AionDPS.Upload.CpShare.Build(m with { Gaps = new() { new CpStatGap(31, 5, 4) } }, 9, 1303, anonymous, now, model) is null
            && AionDPS.Upload.CpShare.Build(m with { ItemLevelBefore = 0 }, 9, 1303, anonymous, now, model) is null
            && AionDPS.Upload.CpShare.Build(m, 9, 0, anonymous, now, model) is null;
        Console.WriteLine($"  -> shared change: no name or server, rounded Item Level and Combat Power, stat changes x weights = the calculator's {predicted:0.0} ({fromWeights:0.0}); "
            + $"a gap or a missing Item Level isn't sent: {shape && sums && skipped}");
        return shape && sums && skipped;
    }

    private static bool Near(double? a, double b) => a is double v && Math.Abs(v - b) < 0.01;
}
