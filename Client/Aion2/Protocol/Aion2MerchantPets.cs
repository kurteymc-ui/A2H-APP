using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2.Protocol;

/// <summary>Where the soul of a pet the Black Cloud Merchants sell comes from, when it can't be farmed out in the world.</summary>
public enum MerchantPetKind
{
    /// <summary>Only from a field boss.</summary>
    FieldBoss,

    /// <summary>Only from monsters inside a party dungeon.</summary>
    Dungeon,

    /// <summary>No monster drops its soul: only chests and shops.</summary>
    Chests,

    /// <summary>A special pet (genus Specia): the shop, events and passes.</summary>
    Special,
}

/// <summary>A pet of the merchant's list: its group and, for a field boss or dungeon one, where its soul drops (in every language the game has).</summary>
public sealed record MerchantPet(int PetId, MerchantPetKind Kind, IReadOnlyDictionary<string, string>? Where)
{
    /// <summary>"Gluttonous Egetis · Eltnen", "Fire Temple"; null for the chests and special ones.</summary>
    public string? WhereIn(string language) => Where is null ? null : Where.GetValueOrDefault(language) ?? Where.GetValueOrDefault("en");
}

/// <summary>
/// The pets the Black Cloud Merchants sell that can't be farmed out in the world, in the owner's four groups (4 November 2026, assets/aion2/pets/merchant.json): only from a field boss (5: Elite Guardite and Elite Klaw added the same night), only in party dungeons (5), no monster drops them (35), the special pets (6).
/// These are the pets the merchant's notes are about (ticked in the Pets page, a pop-up when one is on offer).
/// </summary>
public static class Aion2MerchantPets
{
    private static IReadOnlyList<MerchantPet>? _all;
    private static Dictionary<int, MerchantPet>? _byId;

    /// <summary>Every pet of the list, group by group (field boss, dungeon, chests, special), in the file's order.</summary>
    public static IReadOnlyList<MerchantPet> All => _all ??= Load();

    public static MerchantPet? Of(int petId) => (_byId ??= All.GroupBy(p => p.PetId).ToDictionary(g => g.Key, g => g.First())).GetValueOrDefault(petId);

    public static bool Is(int petId) => Of(petId) is not null;

    private static IReadOnlyList<MerchantPet> Load()
    {
        var list = new List<MerchantPet>();
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "pets", "merchant.json");
            if (!File.Exists(path))
            {
                return list;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (JsonElement group in doc.RootElement.GetProperty("groups").EnumerateArray())
            {
                MerchantPetKind? kind = group.GetProperty("kind").GetString() switch
                {
                    "fieldboss" => MerchantPetKind.FieldBoss,
                    "dungeon" => MerchantPetKind.Dungeon,
                    "chests" => MerchantPetKind.Chests,
                    "special" => MerchantPetKind.Special,
                    _ => null,
                };
                if (kind is null)
                {
                    continue;
                }

                foreach (JsonElement pet in group.GetProperty("pets").EnumerateArray())
                {
                    Dictionary<string, string>? where = pet.TryGetProperty("where", out JsonElement w)
                        ? w.EnumerateObject().ToDictionary(n => n.Name, n => n.Value.GetString() ?? "")
                        : null;
                    list.Add(new MerchantPet(pet.GetProperty("id").GetInt32(), kind.Value, where));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // no list: no merchant notes
        }

        return list;
    }
}
