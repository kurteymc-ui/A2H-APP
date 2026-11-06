using AionDPS.Ui;
using AionDPS.Upload;

namespace AionDPS.Combat;

/// <summary>The CP calculator's upgrade advisor (Ui/CpCalcPage.Value.cs, 6 November 2026): a step's whole cost at market prices and its CP per million Kina.</summary>
public static class SelfCheckValue
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Upgrade advisor (steps priced at the market, CP per million Kina):");
        var book = new MarketBook(2302, "Test", DateTime.UtcNow, new Dictionary<int, MarketPrice>
        {
            [5] = new MarketPrice(50_000, 48_000, 12, 51_000),   // sold: the average sale
            [6] = new MarketPrice(7_000, null, null, null),      // listed only: the cheapest listing
        });
        CpStep Step(double gain, double kina, params CpMaterial[] items) => new(CpStepKind.Enhance, gain, gain, CpCertainty.Exact, Cost: new CpCost(kina, items));
        Func<int, long> holds = id => id == 9 ? 12 : 0;

        // 1M Kina + 10 x 48,000 + 2.5 x 7,000 = 1,497,500 Kina for 300 CP: about 200 CP per million
        var mixed = CpCalcPage.Price(Step(300, 1_000_000, new CpMaterial(5, 10), new CpMaterial(6, 2.5)), book, holds);
        bool sums = mixed is { } m && Math.Abs(m.Total - 1_497_500) < 0.5 && Math.Abs(m.PerMillion - 300 / 1.4975) < 0.01 && m.Parts.Count == 2;
        // an item with no price: nothing when held enough (12 of 10), unpriced when not (12 of 20)
        var heldEnough = CpCalcPage.Price(Step(50, 200_000, new CpMaterial(9, 10)), book, holds);
        bool held = heldEnough is { Total: 200_000, Parts: [{ Each: 0, Held: 12 }] } && CpCalcPage.Price(Step(50, 200_000, new CpMaterial(9, 20)), book, holds) is null;
        // a free step isn't priced
        bool free = CpCalcPage.Price(new CpStep(CpStepKind.Title, 20, 20, CpCertainty.Exact, Cost: CpCost.Free), book, holds) is null
            && CpCalcPage.Price(new CpStep(CpStepKind.Title, 20, 20, CpCertainty.Exact), book, holds) is null;
        Console.WriteLine($"  -> Kina and items at market prices, held items without a price free, free steps left out: {sums && held && free} (sums {sums}, held {held}, free {free})");
        return sums && held && free;
    }
}
