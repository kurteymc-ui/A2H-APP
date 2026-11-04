using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>What the crafting tool's page, its configuration and its overlay share: names, icons, who the player is, and the numbers.</summary>
public static class CraftText
{
    private static readonly Dictionary<int, ImageSource?> Icons = new();

    public static string ItemName(int itemId) => LootText.ItemName(itemId);

    public static Color GradeColor(int itemId) => LootText.GradeColor(itemId);

    public static string RecipeName(CraftRecipe recipe) => ItemName(recipe.Product);

    /// <summary>The item's icon (64 px, from the client), or null; kept after the first read.</summary>
    public static ImageSource? Icon(int itemId)
    {
        if (Icons.TryGetValue(itemId, out ImageSource? known))
        {
            return known;
        }

        ImageSource? image = null;
        if (Aion2Artwork.ItemIconPath(itemId) is string path)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path);
                bitmap.EndInit();
                bitmap.Freeze();
                image = bitmap;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException)
            {
                image = null;
            }
        }

        Icons[itemId] = image;
        return image;
    }

    /// <summary>An item's icon in a small frame in the colour of its grade; a plain tile when the client has no icon.</summary>
    public static Border IconTile(int itemId, double size)
    {
        var tile = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(GradeColor(itemId)),
            Background = new SolidColorBrush(Color.FromRgb(0x10, 0x1b, 0x25)), ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center,
        };
        if (Icon(itemId) is { } image)
        {
            tile.Child = new Image { Source = image, Stretch = Stretch.UniformToFill };
            RenderOptions.SetBitmapScalingMode(tile, BitmapScalingMode.HighQuality);
        }
        else
        {
            tile.Child = new TextBlock { Text = "◆", Foreground = tile.BorderBrush, FontSize = size * 0.42, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }

        return tile;
    }

    /// <summary>The name of the character the meter follows (what the stream revealed, else the one typed in the settings); empty when none is known.</summary>
    public static string Character(Aion2EntityDirectory? directory) => directory?.LearnedLocalName ?? MeterSettings.Load().Aion2CharacterName ?? "";

    /// <summary>The player's faction: Asmodian only when the stream says so, else Elyos (the recipes of both are the same, the other faction's items are named alike).</summary>
    public static bool IsElyos(Aion2EntityDirectory? directory)
    {
        int id = directory?.LocalPlayerId ?? -1;
        return directory is null || id < 0 || directory.FactionOf(id) != "Asmodian";
    }

    public static StockSources Sources(MeterSettings settings) =>
        (settings.CraftCountInventory ? StockSources.Inventory : 0) | (settings.CraftCountStorage ? StockSources.CharacterStorage : 0) | (settings.CraftCountAccountStorage ? StockSources.AccountStorage : 0);

    /// <summary>The plan of the character worked out against its stock: one result per marked recipe, in the order of the list.</summary>
    public static List<CraftResult> Results(Aion2EntityDirectory? directory, MeterSettings settings) =>
        CraftCalc.Compute(CraftPlan.For(Character(directory)), Aion2Stock.Totals(Sources(settings)), settings.CraftResolve, IsElyos(directory));

    /// <summary>
    /// Everything the crafting character could gather: all it holds itself (cube, character storage, bound or not), the server storage, and what its other
    /// characters on the same server and account hold unbound (as their last login left it, <see cref="Aion2Inventories"/>). Bound items of the other
    /// characters never count: they can't be moved. <paramref name="own"/>: the character's own places the crafting settings count (a place switched off there
    /// stays off here, or the line "with your other characters" claimed what the own storage gave, 3 November 2026 review); the server storage always counts.
    /// </summary>
    public static Dictionary<int, long> RosterStock(string me, StockSources own = StockSources.All)
    {
        var stock = new Dictionary<int, long>();
        void Add(IReadOnlyDictionary<int, long> items, bool unboundOnly)
        {
            foreach ((int item, long count) in items)
            {
                if (count > 0 && !(unboundOnly && Aion2Inventories.IsBound(item)))
                {
                    stock[item] = stock.GetValueOrDefault(item) + count;
                }
            }
        }

        if (Aion2Stock.HasData)
        {
            Add(Aion2Stock.Totals(own | StockSources.AccountStorage), false);
        }
        else
        {
            // the meter started after the login: what the character held when the meter last saw it
            if (Aion2Inventories.Characters().FirstOrDefault(c => c.Name == me) is { } self)
            {
                if (own.HasFlag(StockSources.Inventory))
                {
                    Add(self.Bag, false);
                }

                if (own.HasFlag(StockSources.CharacterStorage))
                {
                    Add(self.Storage, false);
                }
            }

            if (Aion2Inventories.AccountOf(me) is { } server)
            {
                Add(server.Items, false);
            }
        }

        foreach (CharacterHoldings mate in Aion2Inventories.Mates(me))
        {
            Add(mate.Bag, true);
            Add(mate.Storage, true);
        }

        return stock;
    }

    /// <summary>The plan against <see cref="RosterStock"/>: how far the character is when its other characters send their materials over.</summary>
    public static List<CraftResult> RosterResults(Aion2EntityDirectory? directory, MeterSettings settings) =>
        CraftCalc.Compute(CraftPlan.For(Character(directory)), RosterStock(Character(directory), Sources(settings)), settings.CraftResolve, IsElyos(directory));

    /// <summary>"12 on Lyra" for every other character on the same server that holds the material unbound (the most first); nothing for a bound one.</summary>
    public static List<string> OnOtherCharacters(string me, CraftLine line)
    {
        var loc = LocalizationManager.Instance;
        return Aion2Inventories.OnOthers(me, line.Item).OrderByDescending(p => p.Count)
            .Select(p => string.Format(loc["Craft.OnCharacter"], p.Count.ToString("N0"), p.Character)).ToList();
    }

    /// <summary>"3 / 50" - what the player has of what the recipes need.</summary>
    public static string HaveOfNeed(CraftLine line) => $"{line.Have:N0} / {line.Needed:N0}";

    /// <summary>Where a material lies besides the inventory: "3 in storage", "1 in account storage" (the inventory is the one the player has with them, the rest has to be fetched).</summary>
    public static List<string> Whereabouts(CraftLine line)
    {
        var loc = LocalizationManager.Instance;
        var places = new List<string>();
        if (Aion2Stock.Count(line.Item, StockSources.Inventory) < line.Needed)
        {
            long storage = Aion2Stock.Count(line.Item, StockSources.CharacterStorage);
            long account = Aion2Stock.Count(line.Item, StockSources.AccountStorage);
            if (storage > 0)
            {
                places.Add(string.Format(loc["Craft.InStorage"], storage.ToString("N0")));
            }

            if (account > 0)
            {
                places.Add(string.Format(loc["Craft.InAccountStorage"], account.ToString("N0")));
            }
        }

        return places;
    }

    private static DateTime Local(DateTime t) => t.Kind == DateTimeKind.Utc ? t.ToLocalTime() : t;

    /// <summary>"Stock: login 17:40 - inventory matched 17:51" (the storages are exact up to the last full listing); a hint while nothing was read yet.</summary>
    public static string StockNote()
    {
        var loc = LocalizationManager.Instance;
        if (Aion2Stock.SnapshotAt is not { } login)
        {
            return loc["Craft.NoStock"];
        }

        string note = string.Format(loc["Craft.StockNote"], Local(login).ToString("HH:mm"));
        return Aion2Stock.ListedAt is { } listed && listed > login ? note + " · " + string.Format(loc["Craft.StockListed"], Local(listed).ToString("HH:mm")) : note;
    }
}
