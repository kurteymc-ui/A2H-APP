using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2.Protocol;

/// <summary>
/// One of the lines worth most on a slot for a class: a stat (<see cref="Stat"/>, a token of <see cref="Aion2CpData"/>) or a skill's level
/// (<see cref="Skill"/>, its English name), its top value (percentages in hundredths, a skill: its level) and its worth against the slot's best line (%).
/// </summary>
public sealed record IdealLine(string? Stat, string? Skill, int Top, int Worth)
{
    public bool IsSkill => Skill is not null;
}

/// <summary>
/// The lines worth most on each slot for each class (assets/aion2/cp/ideal_lines.json, as the owner gave them on 4 November 2026): six per slot (bracelets
/// five), best first - each class needs others (a Templar's chest wants Fury, a Chanter's Attack Preparation). By the item catalog's slot (MainHand,
/// SubHand, Helmet ... Necklace, Earring, Ring, Bracelet).
/// </summary>
public static class Aion2IdealLines
{
    private static Dictionary<string, Dictionary<string, List<IdealLine>>>? _data;
    private static readonly object Gate = new();

    /// <summary>The ideal lines for the class (Templar, Chanter ...) on the slot of <paramref name="itemId"/>; null when the class or slot isn't known.</summary>
    public static IReadOnlyList<IdealLine>? For(string? className, int itemId) =>
        className is not null && Aion2ItemCatalog.Find(itemId) is { } info ? ForSlot(className, info.Slot) : null;

    public static IReadOnlyList<IdealLine>? ForSlot(string className, string slot)
    {
        var data = Data();
        return data.TryGetValue(className, out var slots) && slots.TryGetValue(slot, out var lines) ? lines : null;
    }

    /// <summary>The classes with ideal lines.</summary>
    public static IReadOnlyCollection<string> Classes => Data().Keys;

    /// <summary>Whether a piece's line is this ideal line: the same stat, or a skill line of the skill by its English name.</summary>
    public static bool Matches(IdealLine ideal, string stat, int skillId) =>
        ideal.IsSkill ? skillId != 0 && string.Equals(Aion2SkillNames.NameOf(skillId), ideal.Skill, StringComparison.OrdinalIgnoreCase) : skillId == 0 && stat == ideal.Stat;

    private static Dictionary<string, Dictionary<string, List<IdealLine>>> Data()
    {
        lock (Gate)
        {
            return _data ??= Load(Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "cp", "ideal_lines.json"));
        }
    }

    private static Dictionary<string, Dictionary<string, List<IdealLine>>> Load(string path)
    {
        var data = new Dictionary<string, Dictionary<string, List<IdealLine>>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(path))
            {
                return data;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (JsonProperty cls in doc.RootElement.GetProperty("classes").EnumerateObject())
            {
                var slots = new Dictionary<string, List<IdealLine>>(StringComparer.OrdinalIgnoreCase);
                foreach (JsonProperty slot in cls.Value.EnumerateObject())
                {
                    slots[slot.Name] = slot.Value.EnumerateArray().Select(l =>
                    {
                        string key = l[0].GetString() ?? "";
                        bool skill = key.StartsWith("skill:", StringComparison.Ordinal);
                        return new IdealLine(skill ? null : key, skill ? key[6..] : null, l[1].GetInt32(), l[2].GetInt32());
                    }).ToList();
                }

                data[cls.Name] = slots;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException or UnauthorizedAccessException or IndexOutOfRangeException)
        {
            data.Clear();
        }

        return data;
    }
}
