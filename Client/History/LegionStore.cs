using System.IO;
using System.Text.Json;
using AionDPS.Aion2;

namespace AionDPS.History;

/// <summary>The last member list of one legion as the game sent it at a login: the legion's name, its server, the character it came with, when.</summary>
public sealed class LegionSnapshot
{
    public string Legion { get; set; } = "";
    public int ServerId { get; set; }
    public string Character { get; set; } = "";
    public DateTime At { get; set; }
    public List<LegionMember> Members { get; set; } = new();
}

/// <summary>
/// The legion lists kept on this PC (legion.json in the data folder) for the Legion page (6 November 2026, the owner: "a legion tab, which finds your
/// legion via packets and lists all the members"): the game sends the own legion's list only at a login (frame 0x058a), so the page shows the last one
/// when the game is not running. One list a legion and server, the newest.
/// </summary>
public static class LegionStore
{
    private static readonly object Gate = new();
    private static Dictionary<string, LegionSnapshot>? _all;

    private static string DataFolder => Environment.GetEnvironmentVariable("AIONDPS_DATA_DIR") is { Length: > 0 } testDir
        ? testDir : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Brand.DataFolder);

    private static string FilePath => Path.Combine(DataFolder, "legion.json");

    private static Dictionary<string, LegionSnapshot> All()
    {
        if (_all is not null)
        {
            return _all;
        }

        try
        {
            _all = File.Exists(FilePath) ? JsonSerializer.Deserialize<Dictionary<string, LegionSnapshot>>(File.ReadAllText(FilePath)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            _all = null;
        }

        return _all ??= new Dictionary<string, LegionSnapshot>(StringComparer.Ordinal);
    }

    /// <summary>A list from a login: kept when it is newer than the one kept for that legion and server.</summary>
    public static bool Note(string legion, int serverId, string character, IReadOnlyList<LegionMember> members, DateTime atUtc)
    {
        if (legion.Length == 0 || members.Count == 0)
        {
            return false;
        }

        lock (Gate)
        {
            string key = legion + "@" + serverId;
            Dictionary<string, LegionSnapshot> all = All();
            if (all.TryGetValue(key, out LegionSnapshot? had) && had.At >= atUtc)
            {
                return false;
            }

            all[key] = new LegionSnapshot { Legion = legion, ServerId = serverId, Character = character, At = atUtc, Members = members.ToList() };
            try
            {
                Directory.CreateDirectory(DataFolder);
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(all));
                File.Move(temp, FilePath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // kept in memory; the next list writes it
            }

            return true;
        }
    }

    /// <summary>
    /// The live capture's legion list into the store, as the legion share reads it (<see cref="Upload.LegionShare.Build"/>): the game's own record of the
    /// character played, a list that names it (same name and server, or the only member of that name: lists keep the server a character was made on, so
    /// every member is put on the server played on, the legion's), under the legion the login's character list names for it, else the legion the game showed over
    /// its head, else the legion of the list kept before for this character when most of its members are the same.
    /// </summary>
    public static bool NoteLive(Aion2EntityDirectory? dir)
    {
        if (dir?.LocalCharacter is not { Restored: false, Name.Length: > 0, ServerId: > 0 } me)
        {
            return false;
        }

        IReadOnlyList<LegionMember> roster = dir.LegionRoster();
        if (roster.Count == 0 || Aion2Moved.Find(roster, m => m.Name, m => m.ServerId, me.Name, me.ServerId) is null)
        {
            return false;
        }

        // every member on the legion's server, the one played on (the list keeps the server each was made on)
        List<LegionMember> members = roster.Where(m => m.Name.Length > 0 && m.ServerId > 0)
            .Select(m => m with { ServerId = me.ServerId })
            .GroupBy(m => (m.Name, m.ServerId)).Select(g => g.First()).ToList();
        string? legion = Aion2Moved.Find(dir.AccountCharacters(), c => c.Name, c => c.ServerId, me.Name, me.ServerId)?.Legion?.Trim();
        if (string.IsNullOrEmpty(legion))
        {
            legion = dir.GuildOf(me.CombatId)?.Trim();
        }

        if (string.IsNullOrEmpty(legion))
        {
            legion = Same(me.Name, me.ServerId, members);
        }

        return legion is { Length: > 0 } && Note(legion, me.ServerId, me.Name, members, dir.LegionRosterAt);
    }

    /// <summary>The legion of the list kept for this character, when at least two thirds of that list's members are in the new one.</summary>
    private static string? Same(string character, int serverId, IReadOnlyList<LegionMember> members)
    {
        var names = new HashSet<string>(members.Select(m => m.Name), StringComparer.OrdinalIgnoreCase);
        lock (Gate)
        {
            return All().Values.Where(x => x.ServerId == serverId && string.Equals(x.Character, character, StringComparison.OrdinalIgnoreCase) && x.Members.Count > 0
                    && x.Members.Count(m => names.Contains(m.Name)) * 3 >= x.Members.Count * 2)
                .OrderByDescending(x => x.At).FirstOrDefault()?.Legion;
        }
    }

    /// <summary>Every legion kept, the newest first.</summary>
    public static IReadOnlyList<LegionSnapshot> Legions()
    {
        lock (Gate)
        {
            return All().Values.OrderByDescending(x => x.At).ToList();
        }
    }

    /// <summary>For the self-check: the file read again at the next look.</summary>
    internal static void Forget()
    {
        lock (Gate)
        {
            _all = null;
        }
    }

    /// <summary>For the pictures and the self-check: made-up lists in place of the file's.</summary>
    internal static void UseForPictures(IEnumerable<LegionSnapshot> legions)
    {
        lock (Gate)
        {
            _all = legions.ToDictionary(x => x.Legion + "@" + x.ServerId, StringComparer.Ordinal);
        }
    }
}
