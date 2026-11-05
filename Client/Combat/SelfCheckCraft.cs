using System.Buffers.Binary;
using System.Globalization;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Combat;

/// <summary>
/// The crafting tool: the stock read from the login frame (inventory, character storage, account storage), a change of one slot, a sort that resends the inventory
/// (what is more in the inventory than the single frames explain was taken from the storage), and the materials of a plan (an ingot broken down into its ore,
/// the first recipe of the list getting the stock first, a recipe of the other faction found by its twin).
/// </summary>
public static class SelfCheckCraft
{
    private const int Kinah = 930100001, Elixir = 530116001, Tinder = 530126003, Potion = 530126004, Mana = 530126005, Heal = 530146001, Crystal = 530116006, Greater = 530116010;

    /// <summary>A frame as the game sends it: opcode, then a record per item (kind byte, 9 bytes, item u32, quantity u64, container, slot, 12 bytes).</summary>
    private static byte[] Frame(byte opcodeHigh, int minimumLength, params (byte Kind, byte Container, byte Slot, int Item, long Quantity)[] records)
    {
        var body = new List<byte> { (byte)records.Length };
        foreach (var r in records)
        {
            body.Add(r.Kind);
            body.AddRange(new byte[9]);
            var item = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(item, (uint)r.Item);
            body.AddRange(item);
            var quantity = new byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(quantity, (ulong)r.Quantity);
            body.AddRange(quantity);
            body.Add(r.Container);
            body.Add(r.Slot);
            body.AddRange(new byte[12]);
        }

        while (body.Count + 2 < minimumLength)
        {
            body.Add(0);
        }

        var frame = new List<byte> { opcodeHigh == 0x11 ? (byte)0x11 : (byte)0x1b, 0x56 };
        frame.AddRange(body);
        return frame.ToArray();
    }

    public static bool Run()
    {
        Console.WriteLine("[selftest] Crafting tool (stock, sort, recipes, ingots):");
        DateTime t = new(2026, 10, 21, 17, 40, 0, DateTimeKind.Local);
        Aion2Stock.Reset();

        // login: 5 elixirs in the inventory, 10 in the character storage, 3 potions there too, 7 in the account storage
        Aion2Stock.NoteSnapshot(Frame(0x11, 6000, (1, 1, 0, Elixir, 5), (1, 15, 0, Elixir, 10), (1, 15, 1, Potion, 3), (1, 2, 0, Potion, 7), (1, 1, 1, Kinah, 1000)), t);
        bool login = Aion2Stock.Count(Elixir, StockSources.All) == 15 && Aion2Stock.Count(Potion, StockSources.AccountStorage) == 7 && Aion2Stock.Count(Potion, StockSources.Inventory) == 0
            && Aion2Stock.Count(Kinah, StockSources.Inventory) == 1000;
        Console.WriteLine($"  -> the login frame fills the three stocks apart: {login}");

        // one slot changes (8 elixirs now), an item of kind 2 goes
        Aion2Stock.NoteChange(Frame(0x1b, 0, (1, 1, 0, Elixir, 8)), t.AddSeconds(5));
        bool one = Aion2Stock.Count(Elixir, StockSources.Inventory) == 8 && Aion2Stock.Count(Elixir, StockSources.CharacterStorage) == 10;
        Aion2Stock.NoteChange(Frame(0x1b, 0, (2, 1, 1, Kinah, 0)), t.AddSeconds(6));
        bool gone = Aion2Stock.Count(Kinah, StockSources.Inventory) == 0;
        Console.WriteLine($"  -> a changed slot sets its quantity, a slot of kind 2 is gone: {one && gone}");

        // eight new items at once (kind 0) are loot, not a sort: nothing is taken from the storage and the other slots stay
        Aion2Stock.NoteChange(Frame(0x1b, 0, (0, 1, 20, Tinder, 1), (0, 1, 21, Mana, 1), (0, 1, 22, Heal, 1), (0, 1, 23, Crystal, 1), (0, 1, 24, Greater, 1), (0, 1, 25, Kinah, 5), (0, 1, 26, Potion, 1), (0, 1, 27, Elixir, 1)), t.AddSeconds(8));
        bool loot = Aion2Stock.Count(Elixir, StockSources.Inventory) == 9 && Aion2Stock.Count(Elixir, StockSources.CharacterStorage) == 10 && Aion2Stock.Count(Potion, StockSources.AccountStorage) == 7;
        Console.WriteLine($"  -> eight pickups at once are no sort (the storages stay as they were): {loot}");

        // a sort resends the inventory: 14 elixirs in all now, so 5 of the 9 explained by the single frames came from the character storage; 1 potion in the sort = 0 more than the loot
        Aion2Stock.NoteChange(Frame(0x1b, 0, (1, 1, 0, Elixir, 14), (1, 1, 1, Tinder, 1), (1, 1, 2, Mana, 1), (1, 1, 3, Heal, 1), (1, 1, 4, Crystal, 1), (1, 1, 5, Greater, 1), (1, 1, 6, Kinah, 5), (1, 1, 7, Potion, 1)), t.AddSeconds(30));
        bool sort = Aion2Stock.Count(Elixir, StockSources.Inventory) == 14 && Aion2Stock.Count(Elixir, StockSources.CharacterStorage) == 5 && Aion2Stock.Count(Potion, StockSources.AccountStorage) == 7
            && Aion2Stock.Count(Tinder, StockSources.Inventory) == 1;
        Console.WriteLine($"  -> a sort lists the inventory again: what is more than the single frames explain was taken from the storage (5 of 10 elixirs): {sort}");

        // what came in (the gathering tally reads it): a stack moved to another slot is nothing, a slot the meter never saw is what it held already,
        // a known stack that grows by 3 brought 3, a new stack brought its quantity
        const int Ore = 610620013, FineOre = 610620014;
        bool moved = Aion2Stock.NoteChange(Frame(0x1b, 0, (2, 1, 0, Elixir, 0), (0, 1, 40, Elixir, 14)), t.AddSeconds(40)).Count == 0
            && Aion2Stock.Count(Elixir, StockSources.Inventory) == 14;
        bool unseen = Aion2Stock.NoteChange(Frame(0x1b, 0, (1, 1, 50, Ore, 162)), t.AddSeconds(41)).Count == 0;
        var grew = Aion2Stock.NoteChange(Frame(0x1b, 0, (1, 1, 50, Ore, 165), (0, 1, 51, FineOre, 1)), t.AddSeconds(42));
        bool gained = grew.Count == 2 && grew.Contains((Ore, 3L)) && grew.Contains((FineOre, 1L));
        Console.WriteLine($"  -> a moved stack brings nothing: {moved}; an unseen stack of 162 brings nothing: {unseen}; 162 -> 165 and a new stack bring 3 and 1: {gained}");

        // the recipes: three smithing hammers need 6 ingots each; an ingot is made of ore, so the list asks for the ore
        CraftRecipe? hammer = Aion2CraftData.Find(119016001);
        int ingot = hammer?.Components[0].Item ?? 0;
        CraftRecipe? smelt = hammer is null ? null : Aion2CraftData.ProducerOf(ingot, true);
        bool data = hammer is not null && smelt is not null && hammer.Elyos && Aion2CraftData.All.Count > 1000;
        Console.WriteLine($"  -> the recipe table is read (a hammer, the recipe of its ingot): {data}");
        if (!data)
        {
            Aion2Stock.Reset();
            return false;
        }

        var none = new Dictionary<int, long>();
        List<CraftResult> raw = CraftCalc.Compute(new[] { new CraftEntry { Recipe = hammer!.Id, Count = 3 } }, none, resolve: true, elyos: true);
        long ingots = hammer.Components[0].Count * 3;
        long batches = (ingots + smelt!.Count - 1) / smelt.Count;
        bool oreAsked = raw.Count == 1 && raw[0].Intermediates.Any(i => i.Item == ingot && i.Crafts == batches)
            && smelt.Components.All(c => raw[0].Materials.Any(m => m.Item == c.Item && m.Needed == c.Count * batches)) && raw[0].Materials.All(m => m.Item != ingot);
        Console.WriteLine($"  -> 3 hammers: {ingots} ingots = {batches} crafts of the ingot recipe, the list shows the ore and no ingot: {oreAsked}");

        List<CraftResult> unresolved = CraftCalc.Compute(new[] { new CraftEntry { Recipe = hammer.Id, Count = 3 } }, none, resolve: false, elyos: true);
        bool plain = unresolved[0].Materials.Any(m => m.Item == ingot && m.Needed == ingots) && unresolved[0].Intermediates.Count == 0;
        Console.WriteLine($"  -> without breaking down, the ingot is a line of its own: {plain}");

        // ingots in the stock are used first: 6 on hand = 6 fewer to make
        var withIngots = new Dictionary<int, long> { [ingot] = 6 };
        List<CraftResult> some = CraftCalc.Compute(new[] { new CraftEntry { Recipe = hammer.Id, Count = 3 } }, withIngots, resolve: true, elyos: true);
        long restBatches = (ingots - 6 + smelt.Count - 1) / smelt.Count;
        bool used = some[0].Intermediates.Any(i => i.Item == ingot && i.Crafts == restBatches) && some[0].Materials.Any(m => m.Item == ingot && m.Needed == 6 && m.Have == 6);
        Console.WriteLine($"  -> 6 ingots on hand are used first (counted as there, the rest is made): {used}");

        // two recipes want the same material: the first one in the list gets the stock
        int shared = smelt.Components[0].Item;
        var scarce = new Dictionary<int, long> { [shared] = smelt.Components[0].Count * 2 };
        var twice = new[] { new CraftEntry { Recipe = smelt.Id, Count = 1 }, new CraftEntry { Recipe = smelt.Id, Count = 3 } };
        List<CraftResult> order = CraftCalc.Compute(twice, scarce, resolve: true, elyos: true);
        long firstHave = order[0].Materials.First(m => m.Item == shared).Have, secondHave = order[1].Materials.First(m => m.Item == shared).Have;
        bool first = firstHave == smelt.Components[0].Count && secondHave == smelt.Components[0].Count && order[0].Percent >= order[1].Percent;
        Console.WriteLine($"  -> the first recipe of two that want the same material gets the stock first ({firstHave} then {secondHave}): {first}");

        // the plan made as an Elyos is found again as an Asmodian: the twin recipe makes the same piece
        CraftRecipe? twin = Aion2CraftData.ForFaction(hammer.Id, false);
        bool faction = twin is { Elyos: false } && twin.Category == hammer.Category && twin.Level == hammer.Level && Aion2CraftData.ForFaction(twin.Id, true)?.Id == hammer.Id;
        Console.WriteLine($"  -> a recipe of one faction finds its twin of the other and back: {faction}");

        // the learned recipes: the list frame 0x7e8d (n, then id + 0 each); the twin of the other faction counts as learned too
        Aion2Recipes.Reset();
        bool noList = Aion2Recipes.IsKnown(hammer);
        var list = new List<byte> { 0x8d, 0x7e, 2 };
        foreach (int id in new[] { hammer.Id, 100000001 })
        {
            var entry = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(entry, (uint)id);
            list.AddRange(entry);
        }

        bool read = Aion2Recipes.Note(list.ToArray()) && Aion2Recipes.IsKnown(hammer) && Aion2Recipes.IsKnown(twin!) && !Aion2Recipes.IsKnown(smelt) && !Aion2Recipes.Note(list.Take(10).ToArray());
        Aion2Recipes.Reset();
        // a crafting level-up (profession 1 = weapon smithing, level 50 absolute) teaches the recipes learnt by level up to it, in the category of the profession only
        Aion2Recipes.Note(list.ToArray());
        int before = Aion2CraftData.All.Count(r => Aion2Recipes.IsKnown(r));
        byte[] levelUp = { 0x8d, 0x7d, 1, 50, 0, 0, 0, 0, 0 };
        int taught = Aion2Recipes.NoteLevelUp(levelUp);
        CraftRecipe? forged = Aion2CraftData.All.FirstOrDefault(r => r.Category == "Blacksmithing" && r.Learn == "Auto" && r.Level is > 1 and <= 50 && r.Id != hammer.Id);
        CraftRecipe? tailored = Aion2CraftData.All.FirstOrDefault(r => r.Category == "Tailoring" && r.Learn == "Auto" && r.Level is > 1 and <= 50);
        CraftRecipe? later = Aion2CraftData.All.FirstOrDefault(r => r.Category == "Blacksmithing" && r.Learn == "Auto" && r.Level > 50);
        bool levelled = taught > 0 && forged is not null && Aion2Recipes.IsKnown(forged) && tailored is not null && !Aion2Recipes.IsKnown(tailored) && later is not null && !Aion2Recipes.IsKnown(later) && Aion2Recipes.NoteLevelUp(levelUp) == 0;
        Aion2Recipes.Reset();
        bool noBase = Aion2Recipes.NoteLevelUp(levelUp) == 0 && !Aion2Recipes.HasData;
        Console.WriteLine($"  -> a crafting level-up teaches the level's recipes of its profession (nothing before a login list): {levelled && noBase} ({before} known before)");
        bool known = noList && read && levelled && noBase;
        Console.WriteLine($"  -> without a list every recipe counts as learned, the list frame sets the learned ones (twin included): {known}");

        // chain skills: 1838 opens the follow-up, 1938 closes a window, an unclosed one ends by itself
        Aion2Chains.Reset();
        byte[] open = new byte[11];
        open[0] = 0x38; open[1] = 0x18; open[6] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(open.AsSpan(2), 11010340);
        BinaryPrimitives.WriteUInt32LittleEndian(open.AsSpan(7), 11420340);
        byte[] close = new byte[7];
        close[0] = 0x38; close[1] = 0x19; close[2] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(close.AsSpan(3), 11420340);
        bool opened = Aion2Chains.NoteOpen(open, t) && Aion2Chains.Available(t.AddSeconds(1)) is [{ Skill: 11420340, SecondsLeft: > 1.9 and < 2.1 }];
        bool closed = Aion2Chains.NoteClose(close) && Aion2Chains.Available(t.AddSeconds(1)).Count == 0;
        Aion2Chains.NoteOpen(open, t);
        bool expired = Aion2Chains.Available(t.AddSeconds(4)).Count == 0;
        Aion2Chains.Reset();
        bool chain = opened && closed && expired;
        Console.WriteLine($"  -> a chain skill is open for 3 s after frame 1838, closed by 1938 or by the time: {chain}");

        // the legion list: a member with a comment (length in bytes, one more byte) and one without, the name's length counting characters
        static byte[] Member(int id, int server, int classCode, string name, int level, int rank, string? comment)
        {
            var m = new List<byte>();
            m.AddRange(BitConverter.GetBytes(id));
            m.AddRange(new byte[] { 0, 0 });
            m.AddRange(BitConverter.GetBytes((ushort)server));
            m.AddRange(BitConverter.GetBytes(classCode));
            m.Add((byte)name.Length);
            m.AddRange(System.Text.Encoding.UTF8.GetBytes(name));
            m.AddRange(new byte[] { (byte)level, 0, 0, 0, (byte)rank });
            if (comment is not null)
            {
                byte[] c = System.Text.Encoding.UTF8.GetBytes(comment);
                m.Add((byte)c.Length);
                m.AddRange(c);
                m.Add(0x1b);
            }

            m.AddRange(BitConverter.GetBytes(1_791_000_000_000L));
            m.AddRange(BitConverter.GetBytes(2047));
            m.AddRange(BitConverter.GetBytes(2100));
            m.AddRange(BitConverter.GetBytes(95000));
            m.AddRange(new byte[4]);
            m.Add(2);
            m.AddRange(BitConverter.GetBytes(470418));
            m.AddRange(new byte[4]);
            return m.ToArray();
        }

        var legionFrame = new List<byte> { 0x05, 0x8a, 0, 0, 0x14, 2 };
        legionFrame.AddRange(new byte[8]);
        legionFrame.AddRange(Member(207710, 1304, 30, "Sidy", 45, 2, "Hier könnte Werbung stehen"));
        legionFrame.AddRange(Member(208558, 1304, 18, "Pencilgon", 22, 0, null));
        legionFrame.AddRange(Member(208939, 2304, 34, "Ärgernis", 45, 1, null));
        bool legion = Aion2LegionList.TryParse(legionFrame.ToArray(), out List<LegionMember> listed) && listed.Count == 3
            && listed[0] is { Name: "Sidy", ServerId: 1304, ClassCode: 30, Level: 45, Rank: 2, ItemLevel: 2100, CombatPower: 95000, CharacterId: 207710 }
            && listed[1] is { Name: "Pencilgon", Level: 22, Rank: 0 } && listed[2] is { Name: "Ärgernis", ServerId: 2304 };
        Console.WriteLine($"  -> the legion list is read with and without a comment, names counted in characters: {legion}");

        // the weekly points of the achievements frame: only that entry is read
        var ach = new List<byte> { 0x83, 0x56, 2 };
        foreach ((uint id, uint count) in new[] { (100030000u, 1u), (210000100u, 3440u) })
        {
            ach.AddRange(BitConverter.GetBytes(id));
            ach.AddRange(BitConverter.GetBytes(count));
            ach.AddRange(BitConverter.GetBytes(1_791_499_543_290UL));
            ach.Add(0);
        }

        bool points = Aion2Tickets.TryParseAchievements(ach.ToArray(), out TicketReading week) && week.Id == Aion2Tickets.WeeklyPointsId && week.Value.Count == 3440
            && week.Value.TimeMs == 1_791_499_543_290 && !Aion2Tickets.TryParseAchievements(ach.Take(10).ToArray(), out _);
        Console.WriteLine($"  -> the week's season points are read from the achievements frame: {points}");
        // and they are over at the weekly reset (Wednesday 07:00 UTC, after the maintenance), not a week after they last changed
        DateTime Utc(int day, int hour) => new DateTime(2026, 11, day, hour, 0, 0, DateTimeKind.Utc).ToLocalTime();
        var tuesday = new TicketValue(3440, 0, new DateTimeOffset(new DateTime(2026, 11, 3, 20, 0, 0, DateTimeKind.Utc)).ToUnixTimeMilliseconds());
        bool weekOver = !Aion2Tickets.WeeklyPointsStale(tuesday, Utc(4, 6)) && Aion2Tickets.WeeklyPointsStale(tuesday, Utc(4, 8))
            && Aion2Tickets.NextWeeklyResetAfter(Utc(3, 20)) == Utc(4, 7) && Aion2Tickets.WeeklyPointsStale(default, Utc(4, 6));
        Console.WriteLine($"  -> the week's season points are over at the weekly reset after the maintenance (Wednesday 07:00 UTC): {weekOver}");
        points &= weekOver && Achievements() && Season() && Supply() && Inventories() && SeasonCoins();

        // the own rank of the ranking window (a real frame: Abyss, overall, rank 1,764, 92,435 points, fell 16 places)
        byte[] rankFrame = Convert.FromHexString("578D00000100E4060000353B030000001805044161687A08416B617473756B6901002D00000000000000021369010000000000347B226B696C6C5F636F756E74223A31312C2264656174685F636F756E74223A34322C226173736973745F636F756E74223A36317DF0FFFFFF00000000120000000F0000003422000016C5282CA1010000");
        bool ranked = Aion2Rankings.TryParseOwnRank(rankFrame, t, out OwnRank own) && own is { Type: 1, Scope: 0, Entry: { Rank: 1764, ServerId: 1304, Name: "Aahz", Legion: "Akatsuki", ClassId: 2, Points: 92435, Change: -16, Grade: 18 } }
            && own.Entry.StatsJson.Contains("kill_count") && !Aion2Rankings.TryParseOwnRank(rankFrame.Take(60).ToArray(), t, out _);
        Console.WriteLine($"  -> the own rank of the ranking window is read: {ranked}");

        // the debug points of the recorder: an unknown opcode is a point the first time and once more after two quiet minutes, never a third time, a known opcode never
        var watchSet = new Aion2.Capture.UnknownFrameWatch(new[] { 1, 2 });
        byte[] probe = { 5, 0, 1, 2 };
        DateTime w0 = new(2026, 10, 23, 12, 0, 0);
        bool pointFirst = watchSet.Note(5, probe, w0) is { Opcode: 5, Length: 4, Seen: 1 };
        bool pointQuiet = watchSet.Note(5, probe, w0.AddSeconds(30)) is null && watchSet.Note(1, probe, w0) is null;
        bool pointAgain = watchSet.Note(5, probe, w0.AddSeconds(200)) is { Seen: 3 };
        bool pointCapped = watchSet.Note(5, probe, w0.AddSeconds(500)) is null && watchSet.UnknownOpcodes == 1;
        Console.WriteLine($"  -> an unknown frame is a debug point once, again after two quiet minutes, never more than twice, a known one never: {pointFirst && pointQuiet && pointAgain && pointCapped}");

        // the privacy answer: only an accepted current version allows uploads, never asked or an older version is asked, declined is the restricted mode and is not asked again
        var fresh = new Ui.MeterSettings();
        var accepted = new Ui.MeterSettings { PrivacyDecision = Ui.PrivacyConsent.Accepted, PrivacyVersion = Ui.PrivacyConsent.Version };
        var older = new Ui.MeterSettings { PrivacyDecision = Ui.PrivacyConsent.Accepted, PrivacyVersion = Ui.PrivacyConsent.Version - 1 };
        var declined = new Ui.MeterSettings { PrivacyDecision = Ui.PrivacyConsent.Declined };
        bool consent = !Ui.PrivacyConsent.IsAccepted(fresh) && Ui.PrivacyConsent.NeedsAsking(fresh) && Ui.PrivacyConsent.IsAccepted(accepted) && !Ui.PrivacyConsent.NeedsAsking(accepted)
            && !Ui.PrivacyConsent.IsAccepted(older) && Ui.PrivacyConsent.NeedsAsking(older) && !Ui.PrivacyConsent.IsAccepted(declined) && !Ui.PrivacyConsent.NeedsAsking(declined)
            && Ui.PrivacyConsent.RestrictedTools.SetEquals(new[] { "checklist", "attributes", "cpcalc", "loot", "craft", "timetable", "theme", "network" });
        Console.WriteLine($"  -> the privacy answer: uploads only with the current version accepted, older or never asked is asked, declined is not asked again, eight tools stay: {consent}");

        Aion2Stock.Reset();
        return consent && pointFirst && pointQuiet && pointAgain && pointCapped && ranked && points && legion && chain && login && one && gone && loot && sort && moved && unseen && gained && oreAsked && plain && used && first && faction && known;
    }

    /// <summary>
    /// Every achievement counter (Aion2/Aion2Achievements.cs): the live frame (raw 84 56, the October notes' example), the week's points of an Asmodian
    /// character (220000100; only the Elyos id was read before), and a login's whole list going to the character the game names next, not to the one
    /// before; the same list in the middle of a session goes to the one playing after 10 seconds; the lowest count a stage was seen at is kept.
    /// </summary>
    private static bool Achievements()
    {
        byte[] live = Convert.FromHexString("845601a8cf91140a01000075765868a101000005");
        bool liveRead = Aion2Achievements.TryParse(live, out var one) && one.Count == 1 && one[0].Id == 345100200 && one[0].Value == new AchievementValue(266, 5, 1_792_751_990_389)
            && !Aion2Achievements.TryParse(live.Take(19).ToArray(), out _);

        static byte[] List(params (int Id, int Count, int Stage)[] entries)
        {
            var f = new List<byte> { 0x83, 0x56, (byte)entries.Length };
            foreach (var (id, count, stage) in entries)
            {
                f.AddRange(BitConverter.GetBytes((uint)id));
                f.AddRange(BitConverter.GetBytes((uint)count));
                f.AddRange(BitConverter.GetBytes(1_791_499_543_290UL));
                f.Add((byte)stage);
            }

            return f.ToArray();
        }

        bool asmodian = Aion2Tickets.TryParseAchievements(List((401100200, 3, 1), (220000100, 2500, 0)), out TicketReading week) && week.Id == Aion2Tickets.WeeklyPointsId && week.Value.Count == 2500
            && Aion2Tickets.TryParseAchievements(List((210000100, 800, 0)), out TicketReading elyos) && elyos.Value.Count == 800;

        string? name = "Old";
        var clock = new DateTime(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc);
        Func<string?> wasName = Aion2Achievements.CurrentName;
        Func<DateTime> wasClock = Aion2Achievements.Clock;
        bool assigned;
        try
        {
            Aion2Achievements.Reset();
            Aion2Achievements.CurrentName = () => name;
            Aion2Achievements.Clock = () => clock;
            Aion2Achievements.TryParse(List((240111300, 101, 2), (240111000, 1198, 1)), out var login);
            Aion2Achievements.Note(login, whole: true);
            clock = clock.AddSeconds(2);
            Aion2Achievements.AssignPending();
            bool waits = Aion2Achievements.Of("Old").Count == 0;
            name = "New";
            Aion2Achievements.AssignPending();
            bool toNew = Aion2Achievements.Of("New").TryGetValue(240111300, out AchievementValue kills) && kills.Count == 101 && Aion2Achievements.Of("Old").Count == 0;
            Aion2Achievements.TryParse(List((240111300, 125, 2)), out var change);
            Aion2Achievements.Note(change, whole: false);
            bool changed = Aion2Achievements.Of("New")[240111300].Count == 125 && Aion2Achievements.StagesSeen(240111300)[2] == 101;
            Aion2Achievements.TryParse(List((240111000, 1200, 1)), out var again);
            Aion2Achievements.Note(again, whole: true);
            clock = clock.AddSeconds(11);
            Aion2Achievements.AssignPending();
            bool midSession = Aion2Achievements.Of("New")[240111000].Count == 1200;
            assigned = waits && toNew && changed && midSession;
        }
        finally
        {
            Aion2Achievements.CurrentName = wasName;
            Aion2Achievements.Clock = wasClock;
            Aion2Achievements.Reset();
        }

        Console.WriteLine($"  -> every achievement counter: the live frame read: {liveRead}; an Asmodian's week points (and an Elyos'): {asmodian}; a login's list goes to the character named next, the same list mid-session to the one playing, stages kept: {assigned}");
        return liveRead && asmodian && assigned;
    }

    /// <summary>The login list of the supply requests in a real recording (31 October 2026, an Asmodian, 17:27): Emergency, Weekly, Regular: Craft, Regular: Boss,
    /// with the extra byte before every eighth entry.</summary>
    private const string SupplyLogin = "56E2064EAE6100016815C290A10100006771E895A10100000A4EAE610091AF1E0000000000004EAE6100B3AF1E00000000004EAE6100B5AF1E00000000004EAE6100B9AF1E00000000004EAE6100BBAF1E00000000004EAE6100BCAF1E00000000004EAE"
            + "6100DCAF1E00000000004EAE610006B01E00000000004EAE610010B01E0000000000844EAE610016B01E000000000055AE61000268014F81A101000067855BA5A10100000A55AE61006CB31E000700000055AE610078B31E000000000055AE6100A9B31E"
            + "000000000055AE61003AB41E000000000055AE610045B41E000000000055AE6100A1B41E001400000055AE6100A2B41E00110000007855AE6100A4B41E001100000055AE6100A6B41E000000000055AE6100B9B41E008813000045B4610003684593649F"
            + "01000067096C38B40300003645B4610049B71E000500000045B461004AB71E000500000045B461004BB71E000500000045B461004CB71E000000000045B461004DB71E00000000009E45B461004EB71E000300000045B461004FB71E000300000045B461"
            + "0050B71E000300000045B4610051B71E000300000045B4610052B71E000000000045B4610053B71E000000000045B4610054B71E000200000045B4610055B71E00030000002F45B4610056B71E000300000045B4610057B71E000300000045B4610058B7"
            + "1E000300000045B4610059B71E000000000045B461005AB71E000200000045B461005BB71E000000000045B461005CB71E000000000045B461005DB71E00000000007045B461005EB71E000000000045B461005FB71E000000000045B4610060B71E0000"
            + "00000045B4610061B71E000200000045B4610062B71E000200000045B4610063B71E000200000045B4610064B71E000000000045B4610065B71E00020000000345B4610066B71E000200000045B4610067B71E000000000045B4610068B71E0000000000"
            + "45B4610069B71E000000000045B461006AB71E000000000045B461006BB71E000000000045B461006CB71E000000000045B461006DB71E00000000000045B461006EB71E000000000045B461006FB71E000000000045B4610070B71E000000000045B461"
            + "0071B71E000000000045B4610072B71E000000000045B4610073B71E000000000045B4610074B71E000000000045B4610075B71E00000000000045B4610076B71E000000000045B4610077B71E000000000045B4610078B71E000000000045B4610079B7"
            + "1E000000000045B461007AB71E000000000045B461007BB71E000000000045B461007CB71E000000000045B461007DB71E00000000000045B461007EB71E00000000003BB4610004684593649F01000067096C38B4030000363BB4610031BB1E00000000"
            + "003BB4610032BB1E00000000003BB4610033BB1E00000000003BB4610034BB1E00000000003BB4610035BB1E00000000003BB4610036BB1E00000000003BB4610037BB1E0000000000003BB4610038BB1E00000000003BB4610039BB1E00000000003BB4"
            + "61003ABB1E00000000003BB461003BBB1E00000000003BB461003CBB1E00000000003BB461003DBB1E00000000003BB461003EBB1E00000000003BB461003FBB1E0000000000003BB4610040BB1E00000000003BB4610041BB1E00000000003BB4610042"
            + "BB1E00000000003BB4610043BB1E00000000003BB4610044BB1E00000000003BB4610045BB1E00000000003BB4610046BB1E00000000003BB4610047BB1E0000000000003BB4610048BB1E00000000003BB4610049BB1E00000000003BB461004ABB1E00"
            + "000000003BB461004BBB1E00000000003BB461004CBB1E00000000003BB461004DBB1E00000000003BB461004EBB1E00000000003BB461004FBB1E0000000000003BB4610050BB1E00000000003BB4610051BB1E00000000003BB4610052BB1E00000000"
            + "003BB4610053BB1E00000000003BB4610054BB1E00000000003BB4610055BB1E00000000003BB4610056BB1E00000000003BB4610057BB1E0000000000003BB4610058BB1E00000000003BB4610059BB1E00000000003BB461005ABB1E00000000003BB4"
            + "61005BBB1E00000000003BB461005CBB1E00000000003BB461005DBB1E00000000003BB461005EBB1E00000000003BB461005FBB1E0000000000003BB4610060BB1E00010000003BB4610061BB1E00000000003BB4610062BB1E00000000003BB4610063"
            + "BB1E00000000003BB4610064BB1E00000000003BB4610065BB1E00000000003BB4610066BB1E00000000000000000005000000000000000000000000000000000000000000060000000000000000000000000000000000";

    /// <summary>
    /// The supply requests: the table (5 Silentium for 980 Abyss Points and 250 Enhance Stones, a crafting request taking any of five weapons), the real login list,
    /// a delivery, the list going to the character named next, a delivery on a tab the meter has no list of, the resets; and the frames of the supply requests and
    /// of the achievement counters through the frame decoder as the game sends them (the counters' opcodes were read the wrong way round before 3 November 2026).
    /// </summary>
    private static bool Supply()
    {
        bool table = Aion2Supply.Requests.Count == 1122 && Aion2Supply.Find(1011116) is { Target: 5, Kind: "Daily", Faction: 'L' } silentium
            && silentium.Items.SequenceEqual(new[] { 630840002 }) && silentium.Rewards.SequenceEqual(new (int Item, int Count)[] { (930100003, 980), (930100023, 250) })
            && Aion2Supply.Text(silentium.Names, "en") == "Silentium" && Aion2Supply.Find(1013007) is { Target: 3, Kind: "Craft" } craft && craft.Items.Contains(110150014)
            && Aion2Supply.Find(2012345) is { Target: 5000, Faction: 'D', Kind: "Weekly" } && Aion2Supply.TabName(1, "en") == "Emergency" && Aion2Supply.TabName(2, "de") == "Wöchentlich";

        byte[] login = Convert.FromHexString(SupplyLogin);
        bool parsed = Aion2Supply.TryParseList(login, out List<SupplyGroup> groups) && groups.Count == 4 && groups.Select(g => g.Tab).SequenceEqual(new[] { 1, 2, 3, 4 })
            && groups.Select(g => g.Requests.Count).SequenceEqual(new[] { 10, 10, 54, 54 }) && groups[1] is { Group: 6401621, EndMs: 1793775600999 }
            && groups[1].Requests.First(e => e.Id == 2012321).Progress == 20 && groups[1].Requests.First(e => e.Id == 2012345).Progress == 5000
            && groups.SelectMany(g => g.Requests).All(e => Aion2Supply.Find(e.Id) is { } r && e.Progress <= r.Target)
            && groups[2].Requests.Count(e => e.Progress >= Aion2Supply.Find(e.Id)!.Target) == 18 && !Aion2Supply.TryParseList(login.Take(login.Length - 1).ToArray(), out _);

        static byte[] Delivery(int group, int request, int progress, bool done, int item, int given)
        {
            var f = new List<byte> { 0x3e, 0xe2, 0, 0 };
            f.AddRange(BitConverter.GetBytes(group));
            f.AddRange(BitConverter.GetBytes(request));
            f.AddRange(BitConverter.GetBytes(progress));
            f.Add(done ? (byte)1 : (byte)0);
            f.Add(1);
            f.AddRange(new byte[8]);
            f.AddRange(BitConverter.GetBytes(item));
            f.AddRange(BitConverter.GetBytes(given));
            f.Add(1);
            return f.ToArray();
        }

        int leather = Aion2Supply.Find(2012322)?.Items[0] ?? 0;
        bool delivery = Aion2Supply.TryParseProgress(Delivery(6401621, 2012322, 20, true, leather, 3), out int dg, out int dr, out int dp, out bool dd) && dg == 6401621 && dr == 2012322 && dp == 20 && dd
            && !Aion2Supply.TryParseProgress(Delivery(6401621, 999, 20, true, leather, 3), out _, out _, out _, out _);

        var utc = new DateTime(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc);
        long Ms(DateTime t) => new DateTimeOffset(t).ToUnixTimeMilliseconds();
        bool resets = Aion2Supply.NextReset(utc, daily: true) == Ms(new DateTime(2026, 11, 4, 7, 0, 0, DateTimeKind.Utc))
            && Aion2Supply.NextReset(utc, daily: false) == Ms(new DateTime(2026, 11, 4, 7, 0, 0, DateTimeKind.Utc))
            && Aion2Supply.NextReset(new DateTime(2026, 11, 4, 7, 30, 0, DateTimeKind.Utc), daily: false) == Ms(new DateTime(2026, 11, 11, 7, 0, 0, DateTimeKind.Utc))
            && Aion2Supply.NextReset(new DateTime(2026, 11, 4, 6, 0, 0, DateTimeKind.Utc), daily: true) == Ms(new DateTime(2026, 11, 4, 7, 0, 0, DateTimeKind.Utc))
            && groups.Count == 4 && groups[0].Over(new DateTime(2026, 11, 1, 7, 0, 1, DateTimeKind.Utc)) && !groups[0].Over(new DateTime(2026, 11, 1, 6, 59, 0, DateTimeKind.Utc)) && !groups[2].Over(utc);

        string? name = "Old";
        DateTime clock = new(2026, 10, 31, 15, 27, 31, DateTimeKind.Utc);
        Func<string?> wasName = Aion2Supply.CurrentName, wasCounters = Aion2Achievements.CurrentName;
        Func<DateTime> wasClock = Aion2Supply.Clock, wasCountersClock = Aion2Achievements.Clock;
        bool flow;
        try
        {
            Aion2Supply.Reset();
            Aion2Achievements.Reset();
            Aion2Supply.CurrentName = () => name;
            Aion2Supply.Clock = () => clock;
            Aion2Achievements.CurrentName = () => name;
            Aion2Achievements.Clock = () => clock;
            var decoder = new Aion2FrameDecoder(Aion2Protocol.Load(), new Aion2EntityDirectory());
            decoder.Decode(login, clock);
            clock = clock.AddSeconds(2);
            bool waits = Aion2Supply.Of("Old") is null;
            name = "New"; // the game names the character that logged in
            bool assigned = waits && Aion2Supply.Of("New") is { Groups.Count: 4 } && Aion2Supply.Of("Old") is null;
            decoder.Decode(Delivery(6401621, 2012322, 20, true, leather, 3), clock);
            bool delivered = Aion2Supply.Of("New")?.Groups[1].Requests.First(e => e.Id == 2012322).Progress == 20;
            // another character, the meter started after its login: a delivery makes its tab, the end from the daily reset
            name = "Late";
            decoder.Decode(Delivery(5401513, 1011116, 4, false, 630840002, 4), clock);
            bool late = Aion2Supply.Of("Late") is { Groups: [{ Tab: 1, Group: 5401513, Requests: [{ Id: 1011116, Progress: 4 }] } tab] } && tab.EndMs == Aion2Supply.NextReset(clock, daily: true);
            // the achievement counters as the game sends them: a change (84 56) and a login list (83 56) through the decoder
            name = "New";
            decoder.Decode(Convert.FromHexString("845601a8cf91140a01000075765868a101000005"), clock);
            bool counterLive = Aion2Achievements.Of("New").TryGetValue(345100200, out AchievementValue counter) && counter.Count == 266;
            var list = new List<byte> { 0x83, 0x56, 1 };
            list.AddRange(BitConverter.GetBytes(240111300u));
            list.AddRange(BitConverter.GetBytes(125u));
            list.AddRange(BitConverter.GetBytes(1_791_499_543_290UL));
            list.Add(2);
            decoder.Decode(list.ToArray(), clock);
            clock = clock.AddSeconds(11);
            Aion2Achievements.AssignPending();
            bool counterList = Aion2Achievements.Of("New").TryGetValue(240111300, out AchievementValue kills) && kills.Count == 125;
            flow = assigned && delivered && late && counterLive && counterList;
            Console.WriteLine($"  -> supply requests: the login list waits for the name: {assigned}; a delivery sets the progress: {delivered}; a delivery without a list: {late}; "
                + $"the achievement counters reach the meter through the decoder (live {counterLive}, login list {counterList})");
        }
        finally
        {
            Aion2Supply.CurrentName = wasName;
            Aion2Supply.Clock = wasClock;
            Aion2Achievements.CurrentName = wasCounters;
            Aion2Achievements.Clock = wasCountersClock;
            Aion2Supply.Reset();
            Aion2Achievements.Reset();
        }

        Console.WriteLine($"  -> supply requests: the table read: {table}; the real login list (4 tabs, extra bytes stepped over): {parsed}; a delivery frame: {delivery}; the resets: {resets}");
        return table && parsed && delivery && resets && flow;
    }

    /// <summary>
    /// Every character's inventory: a login's stock goes to the character named next; what the other characters on the server hold unbound counts (for the
    /// supply requests and the crafting tool), a bound item only on its own character, a character on another server or account not at all; the server storage counts.
    /// </summary>
    private static bool Inventories()
    {
        const int Stone = 610530001, BoundStone = 630530001, Leather = 610530011, Silentium = 630840002;
        bool bound = Aion2Inventories.IsBound(BoundStone) && !Aion2Inventories.IsBound(Stone) && !Aion2Inventories.IsBound(Leather) && Aion2Inventories.IsBound(Silentium) && Aion2Inventories.IsCurrency(930100023) && !Aion2Inventories.IsCurrency(Stone);
        string? name = "Main";
        DateTime clock = new(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc);
        Func<string?> wasName = Aion2Inventories.CurrentName;
        Func<DateTime> wasClock = Aion2Inventories.Clock;
        Func<string, (int, string?)> wasWhere = Aion2Inventories.Whereabouts;
        bool captured, mates, roster;
        try
        {
            Aion2Inventories.Reset();
            Aion2Stock.Reset();
            Aion2Inventories.CurrentName = () => name;
            Aion2Inventories.Clock = () => clock;
            Aion2Inventories.Whereabouts = n => n == "Far" ? (2002, "A") : (2001, n == "Stranger" ? "B" : "A");
            // the login of "Alt": its stock comes while the game still names "Main"
            Aion2Stock.NoteSnapshot(Frame(0x11, 6000, (1, 1, 0, Stone, 4), (1, 15, 0, BoundStone, 7), (1, 2, 0, Leather, 2), (1, 1, 1, Leather, 3)), clock.ToLocalTime());
            Aion2Inventories.Capture();
            bool waits = Aion2Inventories.Characters().Count == 0;
            name = "Alt";
            clock = clock.AddSeconds(1);
            Aion2Inventories.Capture();
            captured = waits && Aion2Inventories.Characters() is [{ Name: "Alt", ServerId: 2001, Account: "A" } alt] && alt.Bag[Stone] == 4 && alt.Storage[BoundStone] == 7 && alt.Bag[Leather] == 3;

            Aion2Inventories.Put(new CharacterHoldings { Name = "Main", ServerId = 2001, Account = "A", SavedAt = clock, Bag = new() { [Stone] = 1, [BoundStone] = 5 } });
            Aion2Inventories.Put(new CharacterHoldings { Name = "Far", ServerId = 2002, Account = "A", SavedAt = clock, Bag = new() { [Stone] = 100 } });
            Aion2Inventories.Put(new CharacterHoldings { Name = "Stranger", ServerId = 2001, Account = "B", SavedAt = clock, Bag = new() { [Stone] = 50 } });
            mates = Aion2Inventories.Mates("Main").Select(c => c.Name).SequenceEqual(new[] { "Alt" }) && Aion2Inventories.OnOthers("Main", Stone) is [{ Character: "Alt", Count: 4 }]
                && Aion2Inventories.OnOthers("Main", BoundStone).Count == 0 && Aion2Inventories.AccountOf("Main")?.Items[Leather] == 2
                && Aion2Inventories.Combined("Main") is var all && all.First(x => x.Item == Stone).Total == 5 && all.First(x => x.Item == Leather).Total == 5 && all.All(x => x.Item != BoundStone);
            // the crafting tool for "Main" (the meter started after its login: its last holdings): its own bound stones count, Alt's don't
            Aion2Stock.Reset();
            Dictionary<int, long> stock = AionDPS.Ui.CraftText.RosterStock("Main");
            roster = stock.GetValueOrDefault(Stone) == 5 && stock.GetValueOrDefault(BoundStone) == 5 && stock.GetValueOrDefault(Leather) == 5;

            // a login whose name comes late: after 10 s the stock goes to "Alt" (the name still shown); the game then names "Twin", so it was Twin's,
            // and Alt gets back what it had. A name that changes later with no new login list takes nothing.
            name = "Alt";
            clock = clock.AddSeconds(5);
            Aion2Stock.NoteSnapshot(Frame(0x11, 6000, (1, 1, 0, Stone, 9)), clock.ToLocalTime());
            Aion2Inventories.Capture();
            clock = clock.AddSeconds(11);
            Aion2Inventories.Capture();
            bool guessed = Aion2Inventories.Characters().Single(c => c.Name == "Alt").Bag.GetValueOrDefault(Stone) == 9;
            name = "Twin";
            clock = clock.AddSeconds(2);
            Aion2Inventories.Capture();
            var after = Aion2Inventories.Characters();
            bool undone = guessed && after.Single(c => c.Name == "Alt").Bag.GetValueOrDefault(Stone) == 4 && after.Single(c => c.Name == "Twin").Bag.GetValueOrDefault(Stone) == 9;
            name = "Other";
            clock = clock.AddMinutes(5);
            Aion2Inventories.Capture();
            captured &= undone && Aion2Inventories.Characters().All(c => c.Name != "Other");
        }
        finally
        {
            Aion2Inventories.CurrentName = wasName;
            Aion2Inventories.Clock = wasClock;
            Aion2Inventories.Whereabouts = wasWhere;
            Aion2Inventories.Reset();
            Aion2Stock.Reset();
        }

        Console.WriteLine($"  -> inventories: bound and currency items known: {bound}; a login's stock goes to the character named next: {captured}; the characters of one server and account "
            + $"put together (bound and other servers left out, the server storage in): {mates}; the crafting tool counts them: {roster}");
        return bound && captured && mates && roster;
    }

    /// <summary>The Oath Coins of the weekly missions: 200 and 280 for the two steps of "Collecting Dream Shards", 49,140 for
    /// all weekly missions of either faction; a count of last week gives none this week.</summary>
    private static bool SeasonCoins()
    {
        IReadOnlyList<SeasonMission> all = SeasonMissions.All;
        DateTime lastReset = ChecklistManual.LastWeeklyReset(DateTime.Now);
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), lastWeekMs = new DateTimeOffset(lastReset.AddDays(-1)).ToUnixTimeMilliseconds();
        (int Earned, int Possible) Coins(char faction, int id, int count, long timeMs) => SeasonMissions.WeeklyCoins(SeasonMissions.Progress(faction,
            id == 0 ? new Dictionary<int, AchievementValue>() : new Dictionary<int, AchievementValue> { [id] = new AchievementValue(count, 0, timeMs) }, lastReset, all));
        bool steps = all.FirstOrDefault(m => m.Id == 243210800) is { Weekly: true } shards && shards.Steps.Select(x => x.Coins).SequenceEqual(new[] { 200, 280 });
        bool totals = Coins('D', 0, 0, 0) == (0, 49140) && Coins('L', 0, 0, 0) == (0, 49140);
        bool done = Coins('D', 243210800, 3000, nowMs) == (480, 49140) && Coins('D', 243210800, 1500, nowMs) == (200, 49140) && Coins('D', 243210800, 3000, lastWeekMs).Earned == 0;
        Console.WriteLine($"  -> the weekly missions' Oath Coins: per step {steps}; all of a faction 49,140: {totals}; the steps done this week counted (not last week's): {done}");
        return steps && totals && done;
    }

    /// <summary>
    /// The season missions (assets/aion2/season/season-missions.json) against the owner's real counters of 2 and 3 November 2026: the steps done when the game's
    /// stage byte lags the count, the Nightmare bosses' last step (a condition), the Abyss time in hours, a weekly count of a week already over.
    /// </summary>
    private static bool Season()
    {
        IReadOnlyList<SeasonMission> all = SeasonMissions.All;
        SeasonMission? Find(int id) => all.FirstOrDefault(m => m.Id == id);
        bool loaded = all.Count == 130 && all.Count(m => m.Faction == 'D') == 65 && all.Count(m => m.Faction == 'D' && m.Weekly) == 30 && all.All(m => m.Category != "Event")
            && Find(230111300) is { Faction: 'L', Weekly: false } && Find(240211100) is { Faction: 'D', Weekly: true };
        if (!loaded)
        {
            Console.WriteLine($"  -> season missions: the data file read: False ({all.Count} missions)");
            return false;
        }

        DateTime reset = ChecklistManual.LastWeeklyReset(new DateTime(2026, 11, 5, 12, 0, 0));
        long before = new DateTimeOffset(reset.AddHours(-1)).ToUnixTimeMilliseconds(), after = new DateTimeOffset(reset.AddHours(1)).ToUnixTimeMilliseconds();
        var counters = new Dictionary<int, AchievementValue>
        {
            [240111300] = new(52, 1, after),      // Street Fighter 50/100/200/300/500
            [244211000] = new(12, 0, after),      // Baptism of Fire 6/12/18..., the stage byte still 0
            [243111400] = new(10, 7, after),      // Zikel's Apparition: levels 1-10, then "level 10 within 1 minute"
            [240111000] = new(1102, 1, after),    // Abyss time in minutes, steps 10/30/60 hours
            [240211100] = new(451, 2, before),    // Purify Reshanta (weekly), last changed before the reset
            [244210900] = new(19, 9, after),      // Shooting for the Stars (weekly) this week
        };
        Dictionary<int, SeasonProgress> p = SeasonMissions.Progress('D', counters, reset).ToDictionary(x => x.Mission.Id);
        bool steps = p[240111300] is { Done: 1, Next.Target: 100 } && p[244211000] is { Done: 2, Next.Target: 18 }
            && p[243111400] is { Done: 10, Next.Target: null, Finished: false } && SeasonMissions.DoneSteps(Find(243111400)!, 10, 11) == 11
            && p[240111000] is { Done: 1, Next.Target: 1800 } && Find(240111000)!.Hours
            && SeasonMissions.CountText(Find(240111000)!, 1102, CultureInfo.InvariantCulture) == "18.4" && SeasonMissions.CountText(Find(240111000)!, 1800, CultureInfo.InvariantCulture) == "30";
        bool weekly = p[240211100] is { Stale: true, Count: 0, Done: 0 } && p[244210900] is { Stale: false, Count: 19, Done: 9 }
            && p.Count == 65 && p.Values.Count(x => x.Count == 0 && x.Done == 0) == 65 - 5;
        bool faction = SeasonMissions.FactionOf(2302, new Dictionary<int, AchievementValue>()) == 'D' && SeasonMissions.FactionOf(1001, counters) == 'L'
            && SeasonMissions.FactionOf(0, counters) == 'D';
        Console.WriteLine($"  -> season missions: the data file ({all.Count}): {loaded}; steps done with the lagging stage byte, a condition step, hours: {steps}; a week over counts 0: {weekly}; faction: {faction}");

        // the overlay's list: none picked = the unfinished weekly ones, furthest along first (Shooting for the Stars 19/20 before the others); picked = those, in the
        // order picked, the other faction's id left out. The description is the step worked on, the last one once all are done.
        IReadOnlyList<SeasonProgress> list = p.Values.OrderBy(x => x.Mission.Id).ToList();
        IReadOnlyList<SeasonProgress> auto = SeasonMissions.ForOverlay(list, null), picked = SeasonMissions.ForOverlay(list, new[] { 240111300, 230111300, 244210900 });
        bool overlay = auto.Count == 40 && auto.All(x => x.Mission.Weekly && !x.Finished) && auto[0].Mission.Id == 244210900
            && picked.Select(x => x.Mission.Id).SequenceEqual(new[] { 240111300, 244210900 });
        SeasonMission street = Find(240111300)!;
        bool described = SeasonMissions.Description(p[240111300], "en") == SeasonMissions.Text(street.Steps[1].Text, "en")
            && SeasonMissions.Description(new SeasonProgress(street, 600, street.Steps.Count, false), "en") == SeasonMissions.Text(street.Steps[^1].Text, "en")
            && Math.Abs(SeasonMissions.Fraction(p[240111300]) - 0.52) < 1e-9;
        Console.WriteLine($"  -> season overlay: none picked shows the 40 unfinished weekly ones, furthest along first: {auto.Count == 40 && auto[0].Mission.Id == 244210900}; picked ones in their order, the other faction's left out: {overlay}; the description is the step worked on: {described}");
        return steps && weekly && faction && overlay && described;
    }
}
