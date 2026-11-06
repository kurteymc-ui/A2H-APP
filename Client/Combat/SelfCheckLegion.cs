using System.IO;
using AionDPS.Aion2;
using AionDPS.History;
using AionDPS.Upload;

namespace AionDPS.Combat;

/// <summary>The Legion page (Ui/LegionPage.cs, 6 November 2026): the login's member list kept on this PC under the legion's name, and what the site says of the
/// legion on Discord.</summary>
public static class SelfCheckLegion
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Legion page (the login's member list kept on this PC, the bot's servers):");
        string? before = Environment.GetEnvironmentVariable("AIONDPS_DATA_DIR");
        string folder = Path.Combine(Path.GetTempPath(), "a2h-selftest-legion-" + Environment.ProcessId);
        bool ok = true;
        try
        {
            Environment.SetEnvironmentVariable("AIONDPS_DATA_DIR", folder);
            LegionStore.UseForPictures(Array.Empty<LegionSnapshot>());
            long seen = DateTimeOffset.UtcNow.AddHours(-3).ToUnixTimeMilliseconds();
            var dir = new Aion2EntityDirectory();
            dir.SetLocalCharacter(new Aion2CharacterInfo(77, "Aerin", 5, 45, Array.Empty<Aion2EquippedItem>(), DateTime.UtcNow, ServerId: 2302));
            var members = new List<LegionMember>
            {
                new(1, 2302, 5, "Aerin", 45, 0, seen, 1010, 3100, 190_000), new(2, 2302, 29, "Vesh", 45, 2, seen, 0, 3000, 180_000),
                new(3, 2302, 9, "Kaelo", 45, 1, seen, 0, 2950, 175_000),
            };
            dir.NoteLegionRoster(members);

            // the legion's name comes with the login's character list: without it nothing is kept
            bool waits = !LegionStore.NoteLive(dir) && LegionStore.Legions().Count == 0;
            dir.NoteAccountCharacters(new[] { new AccountCharacter("Aerin", 5, 1, 45, 3100, "Dawnbreakers", 0, 0, 2302, 1) });
            bool kept = LegionStore.NoteLive(dir) && LegionStore.Legions() is [{ Legion: "Dawnbreakers", ServerId: 2302, Character: "Aerin", Members.Count: 3 }];
            bool once = !LegionStore.NoteLive(dir);
            Console.WriteLine($"  -> kept under the legion the character list names, once a list: {waits && kept && once} (waits {waits}, kept {kept}, once {once})");
            ok &= waits && kept && once;

            // from the file at the next start, the rank, zone and numbers as the game sent them
            LegionStore.Forget();
            bool file = LegionStore.Legions() is [{ } back] && back.Members.Single(m => m.Name == "Kaelo") is { Rank: 1, Zone: 0, ItemLevel: 2950, CombatPower: 175_000, LastActiveMs: var t } && t == seen
                        && back.Members.Single(m => m.Name == "Aerin").Zone == 1010;
            Console.WriteLine($"  -> read back from legion.json with every member's rank, place and numbers: {file}");
            ok &= file;

            // a list that doesn't name the character played (one from another character's login) is never kept under this one's legion
            var other = new Aion2EntityDirectory();
            other.SetLocalCharacter(new Aion2CharacterInfo(78, "Orrin", 9, 45, Array.Empty<Aion2EquippedItem>(), DateTime.UtcNow, ServerId: 2302));
            other.NoteAccountCharacters(new[] { new AccountCharacter("Orrin", 9, 1, 45, 3000, "Nightwatch", 0, 0, 2302, 2) });
            other.NoteLegionRoster(members);
            bool notMine = !LegionStore.NoteLive(other) && LegionStore.Legions().Count == 1;
            Console.WriteLine($"  -> a list without the character played is not kept: {notMine}");
            ok &= notMine;

            // a later login without the character list: the legion of the list kept for this character, as most of its members are in the new one
            Thread.Sleep(20);
            var later = new Aion2EntityDirectory();
            later.SetLocalCharacter(new Aion2CharacterInfo(79, "Aerin", 5, 45, Array.Empty<Aion2EquippedItem>(), DateTime.UtcNow, ServerId: 2302));
            later.NoteLegionRoster(members.Append(new LegionMember(4, 2302, 13, "Mirae", 45, 2, seen, 0, 2800, 160_000)).ToList());
            bool same = LegionStore.NoteLive(later) && LegionStore.Legions() is [{ Legion: "Dawnbreakers", Members.Count: 4 }];
            Console.WriteLine($"  -> without the character list, the kept legion when its members are the same: {same}");
            ok &= same;
        }
        finally
        {
            Environment.SetEnvironmentVariable("AIONDPS_DATA_DIR", before);
            LegionStore.Forget();
            try
            {
                Directory.Delete(folder, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // a temp folder left behind
            }
        }

        // the site's answer: a hidden server has no name, only https invites, the add link only to atreia.app
        LegionInfo? info = LegionInfoClient.Parse("""
            {"ok":true,"legion":"Dawnbreakers","servers":[{"name":"Dawn HQ","recruiting":true,"invite":"https://discord.gg/abc","lang":"English","hours":"Evenings","linked":9},
            {"name":null,"recruiting":false,"invite":"http://example.com","linked":2}],"member":true,"add":"https://example.com/add","linked":["Aerin","Vesh"]}
            """, "Dawnbreakers");
        bool parsed = info is { Member: true, Servers: [{ Name: "Dawn HQ", Recruiting: true, Invite: "https://discord.gg/abc", Linked: 9 }, { Name: null, Invite: null, Linked: 2 }], Linked: ["Aerin", "Vesh"] }
                      && info.AddUrl == LegionInfoClient.AddUrl;
        bool refused = LegionInfoClient.Parse("""{"ok":false}""", "X") is null;
        Console.WriteLine($"  -> the site's servers, invites and links read, nothing taken from elsewhere: {parsed && refused}");
        ok &= parsed && refused;
        return ok;
    }
}
