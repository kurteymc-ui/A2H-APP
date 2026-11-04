using AionDPS.Aion2.Protocol;

namespace AionDPS.Aion2;

/// <summary>One kind of Odyle energy item a character holds: its name, how many (in the bag and in storage), how much energy one recharges.</summary>
public sealed record OdyleItemLine(string Name, long Bag, long Storage, int Each, bool Expires)
{
    public long Count => Bag + Storage;

    public long Energy => Count * Each;
}

/// <summary>
/// The items that recharge Odyle energy and what a character holds of them. Every one the game has is "Odyle Energy ..." (40 energy) or "Small Odyle
/// Energy ..." (10): the game's own descriptions ("Recharges 40 Odyle Energy", "Recharges 10 Odyle Energy", all 28 items in its texts), and on the owner's
/// recording of 3 November 2026 using two Odyle Energy (Bound) gave +80, four gave +160. "(7 Days)" and "(30 Days)" ones run out. Bound ones count for their
/// character only; unbound ones in the server storage count for every character of the account there (as everywhere in <see cref="Aion2Inventories"/>).
/// </summary>
public static class Aion2OdyleItems
{
    /// <summary>The energy one item recharges: 40, 10 for a small one; 0 for any other item.</summary>
    public static int EnergyOf(int itemId)
    {
        string? name = Aion2ItemNames.NameOf(itemId, "en");
        return name is null ? 0 : name.StartsWith("Small Odyle Energy", StringComparison.Ordinal) ? 10 : name.StartsWith("Odyle Energy", StringComparison.Ordinal) ? 40 : 0;
    }

    /// <summary>A refill that runs out ("(7 Days)", "(30 Days)").</summary>
    public static bool Expires(int itemId) => Aion2ItemNames.NameOf(itemId, "en") is { } name && name.Contains("Days)", StringComparison.Ordinal);

    /// <summary>What the character holds, one line per kind (items of the same name and energy together), the most energy first; empty while its inventory is not known.</summary>
    public static IReadOnlyList<OdyleItemLine> Held(string character, string language)
    {
        CharacterHoldings? held = Aion2Inventories.Characters().FirstOrDefault(c => c.Name == character);
        if (held is null)
        {
            return Array.Empty<OdyleItemLine>();
        }

        var bag = held.Bag.Where(kv => kv.Value > 0 && EnergyOf(kv.Key) > 0).ToList();
        var storage = held.Storage.Where(kv => kv.Value > 0 && EnergyOf(kv.Key) > 0).ToList();
        // the server storage: only what is not bound (bound items there are another character's)
        var shared = Aion2Inventories.AccountOf(character)?.Items.Where(kv => kv.Value > 0 && EnergyOf(kv.Key) > 0 && !Aion2Inventories.IsBound(kv.Key)).ToList()
            ?? new List<KeyValuePair<int, long>>();
        return bag.Select(kv => (Id: kv.Key, Bag: kv.Value, Storage: 0L))
            .Concat(storage.Concat(shared).Select(kv => (Id: kv.Key, Bag: 0L, Storage: kv.Value)))
            .GroupBy(x => (Name: Aion2ItemNames.NameOf(x.Id, language) ?? Aion2ItemNames.NameOf(x.Id, "en") ?? "#" + x.Id, Each: EnergyOf(x.Id)))
            .Select(g => new OdyleItemLine(g.Key.Name, g.Sum(x => x.Bag), g.Sum(x => x.Storage), g.Key.Each, g.Any(x => Expires(x.Id))))
            .OrderByDescending(l => l.Each).ThenBy(l => l.Expires ? 0 : 1).ThenBy(l => l.Name, StringComparer.CurrentCulture).ToList();
    }

    /// <summary>All the energy the character's items hold.</summary>
    public static long EnergyHeld(string character) => Held(character, "en").Sum(l => l.Energy);
}
