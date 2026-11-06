using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Combat;

/// <summary>The Black Cloud Merchants: a real page of the shop (3 November 2026, page byte 0xc9 that the old reading dropped) gives its pet and the renewal
/// time; the pets only the merchant sells; every character's needs and the notes.</summary>
public static class SelfCheckBlackCloud
{
    /// <summary>A page of the shop read at 15:54:02 UTC: ten offers (skins, theostones, the pet Drana Mutant at 90,000 Kinah), renewing at 16:00 UTC.</summary>
    private const string Page = "9156c90ab7350300e3cfef1f0100000000000000035dd47b000000000000ea3d000000000000554e370300ddcfef1f0100000000000000035d00270600000000008013030000000000c0370300a356f11f0100000000000000035d6035010000000000b09a000000000000a03803000ccbdf1f0100000000000000005da086010000000000905f01000000000061350300074bee1f0100000000000000015d00a60e000000000000b80b000000000055593403009ae7d11f0100000000000000025d00a60e000000000000410a00000000006834030088c0d11f0100000000000000005d003e49000000000000eb410000000000c635030078d0ef1f0100000000000000005dd47b000000000000726f0000000000004b350300a94aee1f0100000000000000015d00a60e000000000000b80b0000000000056c3403008cc0d11f0100000000000000015d009f240000000000004c1d0000000000008823a2a101000000000000";

    public static bool Run()
    {
        Console.WriteLine("[selftest] Black Cloud Merchants:");
        DateTime at = new(2026, 11, 3, 15, 54, 2, DateTimeKind.Utc);
        var page = Aion2.Protocol.Aion2FrameDecoder.ParseShop(Convert.FromHexString(Page), at);
        bool read = page.HasValue && page.Value.Page == 0xc9 && page.Value.Offers == 10 && page.Value.Pets.Count == 1 && page.Value.Pets[0].PetId == 1179
            && page.Value.Pets[0].Price == 90000 && page.Value.RenewMs == new DateTimeOffset(2026, 11, 3, 16, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        Console.WriteLine($"  -> a page of today's shop (page byte 0xc9): {page?.Offers} offers, the pet Drana Mutant at 90,000, renewing 16:00 UTC: {read}");

        // the merchant's pets that can't be farmed out in the world, in the owner's four groups (4 November 2026); the pets no monster of the data
        // belongs to and no place lists (the 13 of the first list) are all among the chests-only and special ones
        var all = Aion2MerchantPets.All;
        int Count(MerchantPetKind kind) => all.Count(p => p.Kind == kind);
        int[] computed = { 1025, 1032, 1057, 1102, 1111, 1112, 1122, 1177, 1182, 1204, 1240, 1278, 1299 };
        bool onlyOk = all.Count == 51 && Count(MerchantPetKind.FieldBoss) == 5 && Count(MerchantPetKind.Dungeon) == 5 && Count(MerchantPetKind.Chests) == 35
            && Count(MerchantPetKind.Special) == 6 && all.Select(p => p.PetId).Distinct().Count() == 51
            && Aion2MerchantPets.Of(1206)?.WhereIn("en") == "Divine Ansas · Verteron" && Aion2MerchantPets.Of(1156)?.WhereIn("en") == "Bodyguard Teegant · Verteron"
            && Aion2MerchantPets.Of(1103) is { Kind: MerchantPetKind.FieldBoss } pretor && pretor.WhereIn("en") == "Gluttonous Egetis · Eltnen"
            && Aion2MerchantPets.Of(1058)?.WhereIn("en") == "Fire Temple" && Aion2MerchantPets.Of(1111)?.Kind == MerchantPetKind.Special
            && computed.All(id => Aion2MerchantPets.Of(id)?.Kind is MerchantPetKind.Chests or MerchantPetKind.Special) && Aion2Pets.ShopOnlyPets.All(computed.Contains)
            && !Aion2MerchantPets.Is(1179) && !Aion2MerchantPets.Is(1046) && all.All(p => Aion2Pets.PetName(p.PetId, "en") is not null);
        Console.WriteLine($"  -> {all.Count} merchant pets: field boss {Count(MerchantPetKind.FieldBoss)} (Pretor Veil Administrator: Gluttonous Egetis), dungeon {Count(MerchantPetKind.Dungeon)}, "
            + $"chests and shops {Count(MerchantPetKind.Chests)}, special {Count(MerchantPetKind.Special)}; Drana Mutant and Armadon farmable: {onlyOk}");

        // two characters: YIELD lacks Swift Lugi, Dead has it at level 1; the shop offers it to YIELD - the note names both; its renewal is due once
        Aion2BlackCloud.Reset();
        Aion2BlackCloud.NotePets("YIELD", 2302, new[] { new Aion2PetState(1179, 3, 0), new Aion2PetState(1240, 3, 0) });
        Aion2BlackCloud.NotePets("Dead", 2302, new[] { new Aion2PetState(1111, 1, 4) });
        IReadOnlyList<BlackCloudWanted>? told = null;
        void Seen(string name, IReadOnlyList<BlackCloudWanted> offered) => told = offered;
        Aion2BlackCloud.Offered += Seen;
        long renew = new DateTimeOffset(at.AddMinutes(6)).ToUnixTimeMilliseconds();
        Aion2BlackCloud.NoteOffers("YIELD", 2302, 0xc9, new[] { new Aion2ShopPet(1179, 0xc9, 90000), new Aion2ShopPet(1111, 0xc9, 1_200_000) }, renew, at.ToLocalTime());
        Aion2BlackCloud.Offered -= Seen;
        var lugi = told?.FirstOrDefault(w => w.PetId == 1111);
        bool needs = told is { Count: 2 } && told[0].PetId == 1111 && lugi is { Merchant: true, Price: 1_200_000 }
            && lugi.Needed.Count == 2 && lugi.Needed[0] == ("YIELD", 0) && lugi.Needed[1] == ("Dead", 1)
            && told.First(w => w.PetId == 1179).Needed.Count == 1 && Aion2BlackCloud.Wanted(1240).Needed.Count == 1;
        Console.WriteLine($"  -> the shop offers Swift Lugi to YIELD: missing for YIELD, level 1 for Dead, listed first: {needs}");

        // the next tab (another page byte) adds its pets: the pop-up gets the pets of both tabs; a page read before the meter knows the character still raises it
        Aion2BlackCloud.Offered += Seen;
        Aion2BlackCloud.NoteOffers("YIELD", 2302, 0xca, new[] { new Aion2ShopPet(1103, 0xca, 50_000), new Aion2ShopPet(1206, 0xca, 70_000) }, renew, at.AddSeconds(5).ToLocalTime());
        bool tabs = told is { Count: 4 } && told.Select(w => w.PetId).OrderBy(p => p).SequenceEqual(new[] { 1103, 1111, 1179, 1206 });
        string? whose = null;
        void SeenAny(string name, IReadOnlyList<BlackCloudWanted> offered) => whose = name;
        Aion2BlackCloud.Offered += SeenAny;
        told = null;
        Aion2BlackCloud.NoteOffers("", 0, 0xca, new[] { new Aion2ShopPet(1156, 0xca, 70_000) }, renew, at.ToLocalTime());
        Aion2BlackCloud.Offered -= Seen;
        Aion2BlackCloud.Offered -= SeenAny;
        tabs = tabs && whose == "" && told is [{ PetId: 1156, Merchant: true }] && Aion2BlackCloud.Characters().All(c => c.Name.Length > 0)
            && Aion2BlackCloud.Characters().First(c => c.Name == "YIELD").Offers.Count == 4;
        // the pop-up: only the ticked pets of the merchant's list (not Drana Mutant, not the unticked Elite Guardite), field boss ones before the special one
        var toast = Ui.MainWindow.ToastPets(new[] { Aion2BlackCloud.Wanted(1179, 90_000), Aion2BlackCloud.Wanted(1111, 1_200_000), Aion2BlackCloud.Wanted(1156, 70_000),
            Aion2BlackCloud.Wanted(1103, 50_000), Aion2BlackCloud.Wanted(1206, 70_000) }, new[] { 1103, 1111, 1156 });
        tabs = tabs && toast.Count == 3 && toast[2] is { PetId: 1111, Kind: MerchantPetKind.Special, Price: 1_200_000 }
            && toast.Take(2).Select(p => p.PetId).OrderBy(p => p).SequenceEqual(new[] { 1103, 1156 }) && toast.Take(2).All(p => p.Kind == MerchantPetKind.FieldBoss);
        Console.WriteLine($"  -> a second tab adds its pets (Pretor Veil Administrator, Elite Guardite), a page before the character is known still pops up; the pop-up's pets: {tabs}");

        var watched = new[] { 1111, 1240 };
        bool notYet = Aion2BlackCloud.DueRenewals(at.AddMinutes(5), watched).Count == 0;
        var due = Aion2BlackCloud.DueRenewals(at.AddMinutes(7), watched);
        Aion2BlackCloud.MarkAnnounced(due.Select(d => d.Name), at.AddMinutes(7));
        bool renewOk = notYet && due is [("YIELD", 1)] && Aion2BlackCloud.DueRenewals(at.AddMinutes(8), watched).Count == 0
            && Aion2BlackCloud.DueRenewals(at.AddMinutes(7), new[] { 1240 }).Count == 0;
        // every hour after it, without the shop opened again: 17:00 once more; a renewal the meter missed by more than 20 minutes not at all
        var next = Aion2BlackCloud.DueRenewals(at.AddMinutes(67), watched);
        Aion2BlackCloud.MarkAnnounced(next.Select(d => d.Name), at.AddMinutes(67));
        bool hourly = next is [("YIELD", 1)] && Aion2BlackCloud.DueRenewals(at.AddMinutes(70), watched).Count == 0 && Aion2BlackCloud.DueRenewals(at.AddMinutes(156), watched).Count == 0;
        // the pet list's clock: at 15:54 the list renews at 16:00 (the one on offer since 15:00), at 17:30 at 18:00 (since 17:00)
        bool clock = Aion2BlackCloud.Clock(at) is { } c1 && c1.Next == at.AddMinutes(6) && c1.Since == at.AddMinutes(-54)
            && Aion2BlackCloud.Clock(at.AddMinutes(96)) is { } c2 && c2.Next == at.AddMinutes(126) && c2.Since == at.AddMinutes(66);
        // the line on the pet list: 36 min left and 24 min on offer at 19:23:57, as the game's 00:36:03
        string? line = Ui.PetMapController.ShopClock(new DateTime(2026, 11, 3, 19, 23, 57, DateTimeKind.Utc));
        clock = clock && line is not null && line.Contains("36 min") && line.Contains("24 min");
        renewOk = renewOk && hourly && clock;
        Console.WriteLine($"  -> the offers renew at 16:00 and every hour after: a note once each, for YIELD (1 ticked pet still missing), none before and none after; the clock: {renewOk} (hourly {hourly}, clock {clock})");
        Aion2BlackCloud.Reset();

        // the Odyle energy items: 40 for Odyle Energy, 10 for a small one (the game's texts), the 7-day ones run out; a character's bag and storage by kind
        bool each = Aion2OdyleItems.EnergyOf(503700031) == 40 && Aion2OdyleItems.EnergyOf(503700032) == 10 && Aion2OdyleItems.EnergyOf(562000037) == 40
            && Aion2OdyleItems.EnergyOf(562000038) == 10 && Aion2OdyleItems.Expires(562000038) && !Aion2OdyleItems.Expires(503700031) && Aion2OdyleItems.EnergyOf(511490001) == 0;
        Aion2Inventories.Reset();
        Aion2Inventories.Put(new CharacterHoldings
        {
            Name = "YIELD", ServerId = 2302, SavedAt = DateTime.UtcNow,
            Bag = new() { [503700031] = 3, [562000038] = 30, [511490001] = 5 }, Storage = new() { [503700031] = 1, [532000015] = 2 },
        });
        var held = Aion2OdyleItems.Held("YIELD", "en");
        bool items = held.Count == 3 && held[0] is { Name: "Odyle Energy (Bound)", Bag: 3, Storage: 1, Each: 40, Energy: 160 }
            && held.Any(l => l is { Each: 10, Expires: true, Count: 30, Energy: 300 }) && held.Any(l => l is { Each: 10, Expires: false, Storage: 2 })
            && Aion2OdyleItems.EnergyHeld("YIELD") == 480 && Aion2OdyleItems.Held("Nobody", "en").Count == 0;
        Aion2Inventories.Reset();
        Console.WriteLine($"  -> Odyle Energy 40 and Small Odyle Energy 10 each, the 7-day ones marked ({each}); a bag of 3 + 1 in storage, 30 small 7-day and 2 small: 3 kinds, 480 energy ({items})");
        return read && onlyOk && needs && tabs && renewOk && each && items;
    }
}
