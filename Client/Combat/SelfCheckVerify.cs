using System.Text.Json;
using AionDPS.Aion2;
using AionDPS.Upload;

namespace AionDPS.Combat;

/// <summary>The meter's check of the character played (Upload/CharacterVerify.cs): what goes to the site for a login, and when nothing does.</summary>
public static class SelfCheckVerify
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Character verification (the character played, for the site):");
        var account = new List<AccountCharacter>
        {
            new AccountCharacter("Aahz", 9, 1, 45, 2900, "Dawnbreakers", 0, 1_760_000_000_000, 0, 0),
            new AccountCharacter("Twink", 30, 1, 12, 400, null, 0, 0, 1303, 1235),
            new AccountCharacter("Lost", 5, 1, 20, 0, null, 0, 0, 0, 0),
        };
        var played = new Aion2CharacterInfo(331, "Aahz", 9, 45, Array.Empty<Aion2EquippedItem>(), DateTime.Now, ServerId: 1303);
        VerifyUpload? upload = CharacterVerify.Build(played, account);
        // the character played first (its server from the game's own record when the list didn't read it), then the others whose server was read
        bool built = upload is { V: 1, Region: "eu", Character: { Name: "Aahz", ServerId: 1303, Cls: "Templar", Level: 45, CreatedMs: null } }
            && upload.Account.Count == 2 && upload.Account[0] is { Name: "Aahz", ServerId: 1303, Cls: "Templar", Legion: "Dawnbreakers", CreatedMs: 1_760_000_000_000 }
            && upload.Account[1] is { Name: "Twink", ServerId: 1303, Cls: "Cleric", Level: 12, CreatedMs: null, Legion: null };
        string json = JsonSerializer.Serialize(upload, CharacterVerify.JsonOptions);
        bool shape = json.StartsWith("{\"v\":1,\"region\":\"eu\",\"character\":{\"name\":\"Aahz\",\"serverId\":1303,\"cls\":\"Templar\",\"level\":45},\"account\":[", StringComparison.Ordinal)
            && !json.Contains("null", StringComparison.Ordinal);
        // nothing without the game's own record of this login, or without the login's list that has the character in it
        bool restored = CharacterVerify.Build(played with { Restored = true }, account) is null;
        bool unlisted = CharacterVerify.Build(played with { Name = "Other" }, account) is null && CharacterVerify.Build(played, Array.Empty<AccountCharacter>()) is null
            && CharacterVerify.Build(played with { ServerId = 0 }, account) is null && CharacterVerify.Build(null, account) is null;
        // a character made on another server than the one it plays on: the game's lists keep the old one (Aion2Moved, seen 2026-11-02 on two characters made on
        // Nemon 2309 and played on Zikel 2302). The only one of its name is the character played, sent with the server it plays on, and the list's others on the
        // old server with the new one too; two of that name on other servers: neither
        var movedList = new List<AccountCharacter> { account[0] with { ServerId = 2309 }, new("Fighter", 7, 2, 45, 2517, "Asgard", 0, 0, 2309, 205753), new("Dead", 13, 2, 45, 1485, null, 0, 0, 2302, 274283) };
        VerifyUpload? fromMoved = CharacterVerify.Build(played with { ServerId = 2302 }, movedList);
        bool otherServer = fromMoved is { Region: "eu", Character: { Name: "Aahz", ServerId: 2302 } }
            && string.Join(" ", fromMoved.Account.Select(a => $"{a.Name}@{a.ServerId}")) == "Aahz@2302 Fighter@2302 Dead@2302"
            && CharacterVerify.Build(played with { ServerId = 2302 }, new[] { account[0] with { ServerId = 2309 }, account[0] with { ServerId = 2310 } }) is null;
        // a server outside Europe: no region (its numbers aren't confirmed yet), the site looks in every region
        bool unsure = CharacterVerify.Build(played with { ServerId = 1401 }, account) is { Region: null, Character: { ServerId: 1401 } };
        Console.WriteLine($"  -> the login sent: character, its list, the region={built}; as the site reads it={shape}; nothing for a restored record={restored}, a character not in the list={unlisted}, one listed on the server it was made on={otherServer}; no region outside Europe={unsure}");
        bool ok = built && shape && restored && unlisted && otherServer && unsure && LegionList(played, account);
        Console.WriteLine(ok ? "  -> OK" : "  -> FAILED");
        return ok;
    }

    /// <summary>The legion's member list for the site (Upload/LegionShare.cs): the game's numbers of every member, under the legion of the character played, and
    /// nothing when the list doesn't name that character (a list left from another character) or the character has no legion.</summary>
    private static bool LegionList(Aion2CharacterInfo played, IReadOnlyList<AccountCharacter> account)
    {
        var legion = new List<LegionMember>
        {
            new(1001, 1303, 9, "Aahz", 45, 0, 1_790_000_000_000, 7, 2900, 150000),
            new(1002, 1304, 30, "Sidy", 45, 2, 1_790_000_000_000, 0, 2100, 95000),
            new(1003, 1303, 30, "Sidy", 45, 2, 1_790_000_000_000, 0, 0, 0),
        };
        LegionUpload? upload = LegionShare.Build(played, account, legion);
        bool built = upload is { V: 1, Legion: "Dawnbreakers", Character: { Name: "Aahz", ServerId: 1303 } } && upload.Members.Count == 3
            && upload.Members[1] is { Name: "Sidy", ServerId: 1304, Cls: "Cleric", Level: 45, Il: 2100, Cp: 95000 }
            && upload.Members[2] is { Name: "Sidy", ServerId: 1303, Il: null, Cp: null };
        string json = JsonSerializer.Serialize(upload, CharacterVerify.JsonOptions);
        bool shape = json.StartsWith("{\"v\":1,\"legion\":\"Dawnbreakers\",\"character\":{\"name\":\"Aahz\",\"serverId\":1303},\"members\":[{\"name\":\"Aahz\",\"serverId\":1303,\"cls\":\"Templar\",\"level\":45,\"il\":2900,\"cp\":150000}", StringComparison.Ordinal)
            && !json.Contains("lastActive", StringComparison.OrdinalIgnoreCase) && !json.Contains("zone", StringComparison.OrdinalIgnoreCase) && !json.Contains("rank", StringComparison.OrdinalIgnoreCase);
        bool none = LegionShare.Build(played, account, legion.Skip(1).ToList()) is null && LegionShare.Build(played with { Restored = true }, account, legion) is null
            && LegionShare.Build(played, account, Array.Empty<LegionMember>()) is null
            && LegionShare.Build(played with { Name = "Twink", ServerId = 1303 }, account, legion.Select(m => m with { Name = m.Name == "Aahz" ? "Twink" : m.Name }).ToList()) is null;
        // the list names the server the character was made on: the only member of its name is the character played, and the members on that old server go
        // as on its new one
        var movedLegion = new List<LegionMember>
        {
            new(1001, 2309, 9, "Aahz", 45, 0, 1_790_000_000_000, 7, 2900, 150000),
            new(1004, 2309, 30, "Rin", 45, 2, 1_790_000_000_000, 0, 2100, 95000),
            new(1005, 2302, 13, "Dead", 45, 2, 1_790_000_000_000, 0, 1500, 60000),
        };
        bool moved = LegionShare.Build(played with { ServerId = 2302 }, new[] { account[0] with { ServerId = 2309 } }, movedLegion) is { Character: { ServerId: 2302 } } fromMoved
            && string.Join(" ", fromMoved.Members.Select(m => $"{m.Name}@{m.ServerId}")) == "Aahz@2302 Rin@2302 Dead@2302";
        Console.WriteLine($"  -> the legion list sent: the members with the game's numbers={built}; as the site reads it, no last-online time, zone or rank={shape}; nothing without the character played in it or a legion={none}; "
            + $"a character listed on the server it was made on, with its legion={moved}");
        return built && shape && none && moved;
    }
}
