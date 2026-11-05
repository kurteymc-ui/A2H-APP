using AionDPS.Ui;

namespace AionDPS.Combat;

/// <summary>The meter's bars in parts (Ui/MeterBarParts.cs, 5 November 2026): own damage, then the buffs given to the others, then the healing on them.</summary>
public static class SelfCheckBarParts
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Meter bars in parts (own damage, buffs for others, healing on others):");

        // the damage view: a Gladiator with 1,000,000 damage lending 100/s for 100 s, a Cleric with 400,000 damage lending 2,000/s and healing the
        // others for 300,000 (self-heals are not counted by the caller), all against the longest whole bar (the Cleric's 900,000 < the Gladiator's 1,010,000)
        var glad = new PlayerRow(1) { Name = "Aerin", ClassName = "Gladiator", Damage = 1_000_000, Dps = 10_000, Given = 100 };
        var cleric = new PlayerRow(2) { Name = "Vesh", ClassName = "Cleric", Damage = 400_000, Dps = 4_000, Given = 2_000, HealOthers = 300_000, HealOthersRate = 3_000 };
        var rows = new List<PlayerRow> { glad, cleric };
        MeterBarParts.Apply(rows, rdpsView: false, on: true, seconds: 100);
        bool Near(double a, double b) => Math.Abs(a - b) < 0.01;
        bool damage = Near(glad.FillPercent, 100.0 * 1_000_000 / 1_010_000) && Near(glad.GivenFillPercent, 100) && Near(glad.HealFillPercent, 100)
                      && Near(cleric.FillPercent, 100.0 * 400_000 / 1_010_000) && Near(cleric.GivenFillPercent, 100.0 * 600_000 / 1_010_000)
                      && Near(cleric.HealFillPercent, 100.0 * 900_000 / 1_010_000);
        Console.WriteLine($"  -> damage view: own, then the buffs' damage over the span, then the healing, against the longest bar: {damage} ({glad.FillPercent:0.0}/{cleric.FillPercent:0.0}/{cleric.GivenFillPercent:0.0}/{cleric.HealFillPercent:0.0})");
        bool plus = cleric.BarGivenText == "+2.0K" && cleric.BarHealText == "+3.0K" && glad.BarGivenText == "+100" && glad.BarHealText == "";
        Console.WriteLine($"  -> the + figures are per second, none for no healing: {plus} ({cleric.BarGivenText} {cleric.BarHealText} {glad.BarGivenText} '{glad.BarHealText}')");

        // the rDPS view: the own part is nDPS, the grey part ends at rDPS, the green after it (bar = rDPS + healing)
        glad.Ndps = 9_000;
        glad.Rdps = 9_100;
        cleric.Ndps = 3_900;
        cleric.Rdps = 5_900;
        MeterBarParts.Apply(rows, rdpsView: true, on: true, seconds: 100);
        // the longest whole bar is the Gladiator's 9,100 (the Cleric's 5,900 + 3,000 = 8,900)
        bool rdps = Near(glad.HealFillPercent, 100) && Near(glad.FillPercent, 100.0 * 9_000 / 9_100) && Near(cleric.FillPercent, 100.0 * 3_900 / 9_100)
               && Near(cleric.GivenFillPercent, 100.0 * 5_900 / 9_100) && Near(cleric.HealFillPercent, 100.0 * 8_900 / 9_100);
        Console.WriteLine($"  -> rDPS view: nDPS, to rDPS in grey, then the healing in green: {rdps} ({cleric.FillPercent:0.0}/{cleric.GivenFillPercent:0.0}/{cleric.HealFillPercent:0.0})");

        // switched off: the bars are the damage alone, as before
        MeterBarParts.Apply(rows, rdpsView: false, on: false, seconds: 100);
        bool off = Near(glad.FillPercent, 100) && Near(cleric.FillPercent, 40) && Near(cleric.HealFillPercent, 40) && cleric.BarHealText == "" && cleric.BarGivenText == "";
        Console.WriteLine($"  -> off: the damage alone, no parts, no figures: {off}");

        // nobody lent anything (rDPS unknown): the rate shown is the own part, the healing still after it
        var solo = new PlayerRow(3) { Name = "Mirae", ClassName = "Sorcerer", Damage = 500_000, Dps = 5_000, RdpsView = true };
        var healer = new PlayerRow(4) { Name = "Lunari", ClassName = "Chanter", Damage = 200_000, Dps = 2_000, RdpsView = true, HealOthers = 100_000, HealOthersRate = 1_000 };
        MeterBarParts.Apply(new[] { solo, healer }, rdpsView: true, on: true, seconds: 100);
        bool unknown = Near(solo.FillPercent, 100) && Near(healer.FillPercent, 40) && Near(healer.GivenFillPercent, 40) && Near(healer.HealFillPercent, 60);
        Console.WriteLine($"  -> no rDPS known: DPS, then the healing: {unknown} ({healer.FillPercent:0.0}/{healer.HealFillPercent:0.0})");

        bool ok = damage && plus && rdps && off && unknown;
        Console.WriteLine(ok ? "[selftest] PASS: the meter's bar parts" : "[selftest] FAIL: the meter's bar parts");
        return ok;
    }
}
