using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>How the loot tool names and orders what was picked up: the same lines in the overlay and in the tool's page.</summary>
public static class LootText
{
    public static string ItemName(int itemId)
    {
        string language = LocalizationManager.Instance.Language;
        return Aion2ItemNames.NameOf(itemId, language) ?? Aion2ItemCatalog.Find(itemId)?.Name ?? "#" + itemId;
    }

    public static Color GradeColor(int itemId)
    {
        int grade = Aion2ItemNames.GradeOf(itemId);
        if (grade == 0 && Aion2ItemCatalog.Find(itemId) is { } info)
        {
            grade = info.Grade;
        }

        return CharacterWindow.GradeColors.GetValueOrDefault(grade, CharacterWindow.GradeColors[1]);
    }

    public static bool IsCommon(int itemId) => Aion2ItemNames.GradeOf(itemId) is 0 or 1;

    public static string MapName(int zone)
    {
        var loc = LocalizationManager.Instance;
        return zone > 0 ? Aion2Zones.NameOf(zone, loc.Language) ?? loc["Loot.Map"] + " " + zone : loc["Loot.UnknownMap"];
    }

    /// <summary>Items of the same kind summed over the maps, the one picked up last first.</summary>
    public static List<LootItem> Merge(IEnumerable<LootMap> maps) =>
        maps.SelectMany(m => m.Items).GroupBy(i => i.ItemId)
            .Select(g => new LootItem { ItemId = g.Key, Count = g.Sum(i => i.Count), Picks = g.Sum(i => i.Picks), First = g.Min(i => i.First), Last = g.Max(i => i.Last), Kills = g.Sum(i => i.Kills) })
            .OrderByDescending(i => i.Last).ToList();

    /// <summary>Fewer kills than this and a rate is shown as rough (~).</summary>
    public const int RoughBelow = 30;

    /// <summary>The share of the kills an item dropped in, as a percentage ("2.1") and as "one in so many kills" ("48"); null without a kill to go by.</summary>
    public static (string Percent, string OneIn)? RateParts(int withItem, int kills)
    {
        if (kills <= 0 || withItem <= 0)
        {
            return null;
        }

        double percent = 100.0 * withItem / kills;
        double every = (double)kills / withItem;
        string p = percent >= 10 ? percent.ToString("0") : percent >= 1 ? percent.ToString("0.#") : percent.ToString("0.##");
        string n = every < 10 ? every.ToString("0.#") : every.ToString("N0");
        return (p, n);
    }

    /// <summary>"2.1% · 1 in 48 kills", or "100% · every kill" (with ~ in front while the kills are few).</summary>
    public static string Rate(int withItem, int kills) =>
        RateParts(withItem, kills) is { } r
            ? (kills < RoughBelow ? "~" : "") + string.Format(LocalizationManager.Instance[r.OneIn == "1" ? "Loot.Rate.Every" : "Loot.Rate"], r.Percent, r.OneIn)
            : "";

    /// <summary>"1 kill", "48 kills".</summary>
    public static string Kills(int kills) =>
        kills == 1 ? LocalizationManager.Instance["Loot.Kills.One"] : string.Format(LocalizationManager.Instance["Loot.Kills"], kills.ToString("N0"));

    /// <summary>The tooltip of a rate: in how many of the kills, how many in all, and whether it is still rough.</summary>
    public static string RateTip(int withItem, int kills, long count) =>
        string.Format(LocalizationManager.Instance["Loot.RateTip"], withItem.ToString("N0"), kills.ToString("N0"), count.ToString("N0"))
        + (kills < RoughBelow ? " " + LocalizationManager.Instance["Loot.Rough"] : "");

    /// <summary>Fewer cubes than this and a cube rate is shown as rough (~).</summary>
    public const int RoughCubesBelow = 10;

    /// <summary>"1 in 3 cubes", "every cube" (~ in front while the cubes are few); empty without one.</summary>
    public static string OneInCubes(int withItem, int cubes)
    {
        if (RateParts(withItem, cubes) is not { } r)
        {
            return "";
        }

        var loc = LocalizationManager.Instance;
        return (cubes < RoughCubesBelow ? "~" : "") + (r.OneIn == "1" ? loc["Loot.Cubes.Every"] : string.Format(loc["Loot.Cubes.OneIn"], r.OneIn));
    }

    /// <summary>The tooltip of a cube rate: in how many of the cubes, how many in all, and whether it is still rough.</summary>
    public static string CubeRateTip(int withItem, int cubes, long count) =>
        string.Format(LocalizationManager.Instance["Loot.Cubes.RateTip"], withItem.ToString("N0"), cubes.ToString("N0"), count.ToString("N0"))
        + (cubes < RoughCubesBelow ? " " + LocalizationManager.Instance["Loot.Cubes.Rough"] : "");

    /// <summary>A monster's name in the UI language, or "Monster 2300104".</summary>
    public static string MobName(int npcId)
    {
        var loc = LocalizationManager.Instance;
        return npcId <= 0 ? loc["Loot.OtherSources"] : Aion2Npcs.NameOf(npcId, loc.Language) ?? string.Format(loc["Loot.Monster"], npcId);
    }

    /// <summary>What the overlay shows for the map the player is on: its session (every visit of the map together) with its kills and items, the best
    /// gear that dropped there (gold or better) as a banner, and the lines the player allows (gear of one grade as one line, "Gold gear ×2"), the newest
    /// first, with a last line for the rest.</summary>
    public static LootOverlayModel OverlayLines(LootSession? session, int currentZone, MeterSettings settings, bool sample)
    {
        var loc = LocalizationManager.Instance;
        if (session is null)
        {
            return sample ? Sample(settings) : Waiting(settings, currentZone, 0);
        }

        // the session is the map's (one session per map): everything looted there, over every visit
        List<LootMap> maps = session.Maps;
        int zone = maps.Count > 0 ? maps[0].Zone : currentZone;
        List<LootLine> all = LootStats.Lines(maps.SelectMany(m => m.Items));
        int kills = maps.Sum(m => m.Kills);
        List<LootLine> items = settings.LootOverlayHideCommon ? all.Where(i => i.Gear || !IsCommon(i.Key)).ToList() : all;
        string caption = MapName(zone);
        int max = Math.Clamp(settings.LootOverlayLines, 3, 30);
        var lines = items.Take(max).Select(i => Line(i, kills)).ToList();
        int more = Math.Max(0, items.Count - max);
        LootLine? best = all.Where(l => l.Gear && l.Grade >= LootStats.Gold).OrderByDescending(l => l.Grade).ThenByDescending(l => l.Last).FirstOrDefault();
        LootOverlayBanner? banner = best is null ? null : new LootOverlayBanner(best.Name, best.Count, best.Color, best.IconPath,
            string.Join(", ", best.Members.Take(2).Select(m => ItemName(m.ItemId))) + (best.Members.Count > 2 ? $" +{best.Members.Count - 2}" : ""),
            LootStats.OneIn(best.WithKills, kills));
        if (lines.Count == 0)
        {
            return sample ? Sample(settings) : Waiting(settings, zone, kills);
        }

        return new LootOverlayModel(caption, kills, all.Sum(l => l.Count), banner, lines, more);
    }

    /// <summary>Switched on, nothing picked up on this map yet: the overlay still shows (the map, its kills) with a line saying so, instead of staying
    /// invisible until the first drop (2 Nov 2026: switched on in the game, it looked broken).</summary>
    private static LootOverlayModel Waiting(MeterSettings settings, int currentZone, int kills)
    {
        var loc = LocalizationManager.Instance;
        string caption = currentZone > 0 ? MapName(currentZone) : loc["Main.MenuTools.Loot"];
        return new LootOverlayModel(caption, kills, 0, null, Array.Empty<LootOverlayLine>(), 0, loc["Loot.Overlay.Nothing"]);
    }

    private static LootOverlayLine Line(LootLine i, int kills) => new(i.Key, i.Name, i.Count, i.Color, i.Grade, i.Gear, i.IconPath,
        RateParts(i.WithKills, kills) is { } r ? (kills < RoughBelow ? "~" : "") + string.Format(LocalizationManager.Instance["Loot.OneIn"], r.OneIn) : null,
        kills > 0 && i.WithKills > 0 ? Math.Min(1.0, (double)i.WithKills / kills) : 0);

    /// <summary>Lines as they look in a game session, to place the overlay in its real size while nothing was picked up yet.</summary>
    private static LootOverlayModel Sample(MeterSettings settings)
    {
        var loc = LocalizationManager.Instance;
        Color gold = CharacterWindow.GradeColors[4];
        var lines = new List<LootOverlayLine>
        {
            new(-4, LootStats.GearName(LootStats.Gold), 1, gold, 4, true, null, string.Format(loc["Loot.OneIn"], 48), 1 / 48.0),
            new(1, loc["Loot.Sample.2"], 1, CharacterWindow.GradeColors[3], 3, false, null, string.Format(loc["Loot.OneIn"], 12), 1 / 12.0),
            new(2, loc["Loot.Sample.3"], 12, CharacterWindow.GradeColors[2], 2, false, null, string.Format(loc["Loot.OneIn"], 4), 0.25),
            new(3, loc["Loot.Sample.4"], 25, CharacterWindow.GradeColors[1], 1, false, null, null, 0),
        };
        return new LootOverlayModel(loc["Loot.Sample.Map"], 48, 39,
            new LootOverlayBanner(LootStats.GearName(LootStats.Gold), 1, gold, null, loc["Loot.Sample.1"], string.Format(loc["Loot.OneIn"], 48)), lines, 0);
    }
}

/// <summary>What the loot overlay shows: the map with its kills and items picked up, the best gear as a banner, the lines, how many more.</summary>
public sealed record LootOverlayModel(string Caption, int Kills, long Items, LootOverlayBanner? Banner, IReadOnlyList<LootOverlayLine> Lines, int More, string? Note = null)
{
    public static readonly LootOverlayModel Empty = new("", 0, 0, null, Array.Empty<LootOverlayLine>(), 0);
}

/// <summary>The best gear that dropped (gold or better): its line's name, count, colour and icon, the pieces, how many kills one took.</summary>
public sealed record LootOverlayBanner(string Name, long Count, Color Color, string? IconPath, string Pieces, string Rate);

/// <summary>One line of the loot overlay: the item (or gear of a grade), its count, colour, grade and icon, its drop rate here ("1 in 24 kills") and as a
/// share of the kills (0..1, for its bar).</summary>
public sealed record LootOverlayLine(int Key, string Name, long Count, Color Color, int Grade, bool Gear, string? IconPath, string? Rate, double Share);

/// <summary>
/// The loot overlay: the items picked up on the map the player is on (or in the whole session), the one picked up last on top. Placed, scaled and locked like the other
/// overlays (<see cref="MovableOverlay"/>); <see cref="LootOverlayController"/> shows it only while the game is in front (locked or not: never on the desktop).
/// </summary>
public sealed class LootOverlayWindow : MovableOverlay
{
    private readonly StackPanel _rows = new() { MinWidth = 260, MinHeight = 30 };

    public LootOverlayWindow() : base(OverlayPlaces.Loot, new Border { Child = null }, Load, Save, SystemParameters.WorkArea.Left + 40, 360, LoadSize, SaveSize, OverlayEdgeMode.Width)
    {
        Body = new Border
        {
            Background = new LinearGradientBrush(Color.FromArgb(0xEC, 0x1B, 0x1A, 0x21), Color.FromArgb(0xE0, 0x0F, 0x0E, 0x13), 90),
            BorderBrush = new LinearGradientBrush(Color.FromArgb(0x90, 0xDC, 0xB8, 0x77), Color.FromArgb(0x26, 0xDC, 0xB8, 0x77), 90),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Child = _rows, ClipToBounds = true,
        };
    }

    private static (double?, double?) LoadSize()
    {
        var s = MeterSettings.Load();
        return (s.LootOverlayWidth, s.LootOverlayHeight);
    }

    private static void SaveSize(double? width, double? height)
    {
        var s = MeterSettings.Load();
        s.LootOverlayWidth = width;
        s.LootOverlayHeight = height;
        s.Save();
    }

    private static (double?, double?, double) Load()
    {
        var s = MeterSettings.Load();
        return (s.LootOverlayLeft, s.LootOverlayTop, s.LootOverlayScale);
    }

    private static void Save(double left, double top, double scale)
    {
        var s = MeterSettings.Load();
        s.LootOverlayLeft = left;
        s.LootOverlayTop = top;
        s.LootOverlayScale = scale;
        s.Save();
    }

    private string _shown = "";
    private string? _newest;

    public new bool HasContent => _rows.Children.Count > 0;

    private static readonly Color GoldColor = Color.FromRgb(0xDC, 0xB8, 0x77);
    private static readonly Brush Muted = Frozen(Color.FromRgb(0x9C, 0x97, 0x8F));
    private static readonly Brush Soft = Frozen(Color.FromRgb(0xCF, 0xCA, 0xC2));
    private static readonly Brush Faint = Frozen(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Track = Frozen(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Gold = Frozen(GoldColor);

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Color Alpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    /// <summary>
    /// The header (a gold mark, LOOT and the map; its kills big in gold on the right; the items picked up under it), the best gear as a gold banner, then a
    /// row per item: its icon in a frame of its grade's colour, the name in that colour, "1 in N kills" with a bar for the share of kills it dropped in, the
    /// count on the right. Rows of blue and better gear carry a wash of their colour; a new pickup glows gold on its row for a moment.
    /// </summary>
    public void Render(LootOverlayModel model)
    {
        string key = model.Caption + "|" + model.Kills + "|" + model.Items + "|" + model.Banner + "|" + model.More + "|" + model.Note + "\n"
            + string.Join("\n", model.Lines.Select(l => $"{l.Key}:{l.Count}:{l.Rate}:{l.IconPath}"));
        if (key == _shown)
        {
            return;
        }

        bool firstDraw = _shown.Length == 0;
        _shown = key;
        _rows.Children.Clear();
        var loc = LocalizationManager.Instance;
        if (model.Lines.Count == 0)
        {
            _newest = null;
            if (model.Note is not null)
            {
                // switched on, nothing yet: the map and its kills, and a line saying the drops come here
                _rows.Children.Add(Header(model, loc));
                _rows.Children.Add(new TextBlock { Text = model.Note, FontSize = 11.5, Foreground = Muted, Margin = new Thickness(13, 6, 12, 9), TextWrapping = TextWrapping.Wrap });
            }

            return;
        }

        _rows.Children.Add(Header(model, loc));
        if (model.Banner is { } banner)
        {
            _rows.Children.Add(Banner(banner));
        }

        var list = new StackPanel { Margin = new Thickness(6, 4, 6, 6) };
        string newest = $"{model.Lines[0].Key}:{model.Lines[0].Count}";
        bool glow = !firstDraw && _newest is not null && newest != _newest;
        _newest = newest;
        for (int i = 0; i < model.Lines.Count; i++)
        {
            list.Children.Add(Row(model.Lines[i], glow && i == 0));
        }

        if (model.More > 0)
        {
            list.Children.Add(new TextBlock { Text = string.Format(loc["Loot.More"], model.More), FontSize = 11, Foreground = Muted, Margin = new Thickness(8, 4, 0, 0) });
        }

        _rows.Children.Add(list);
    }

    private static FrameworkElement Header(LootOverlayModel model, LocalizationManager loc)
    {
        var head = new DockPanel { Margin = new Thickness(12, 9, 12, 8) };
        if (model.Kills > 0)
        {
            var kills = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            kills.Children.Add(new TextBlock { Text = model.Kills.ToString("N0"), FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Gold, HorizontalAlignment = HorizontalAlignment.Right, LineHeight = 18, LineStackingStrategy = LineStackingStrategy.BlockLineHeight });
            kills.Children.Add(new TextBlock { Text = (model.Kills == 1 ? loc["Loot.Kills.One"] : string.Format(loc["Loot.Kills"], "")).Trim(), FontSize = 10, Foreground = Muted, HorizontalAlignment = HorizontalAlignment.Right });
            DockPanel.SetDock(kills, Dock.Right);
            head.Children.Add(kills);
        }

        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(new TextBlock { Text = "◆", FontSize = 10, Foreground = Gold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        title.Children.Add(new TextBlock { Text = model.Caption, FontSize = 13.5, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        left.Children.Add(title);
        if (model.Lines.Count > 0)
        {
            left.Children.Add(new TextBlock { Text = string.Format(loc["Loot.Items"], model.Items.ToString("N0")), FontSize = 10.5, Foreground = Muted, Margin = new Thickness(16, 1, 0, 0) });
        }

        head.Children.Add(left);

        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(new Border { Height = 1, Background = new LinearGradientBrush(Alpha(GoldColor, 0x50), Alpha(GoldColor, 0x00), 0) });
        return stack;
    }

    /// <summary>The best gear of the run: a band of its colour, its icon, "GOLD GEAR ×2", the pieces and how many kills one took.</summary>
    private static FrameworkElement Banner(LootOverlayBanner b)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(Icon(b.IconPath, b.Color, 30, glow: true));
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) };
        text.Children.Add(new TextBlock { Text = b.Name.ToUpper(System.Globalization.CultureInfo.CurrentCulture), FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(b.Color) });
        text.Children.Add(new TextBlock { Text = b.Pieces, FontSize = 10.5, Foreground = Soft, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(new TextBlock { Text = "×" + b.Count.ToString("N0"), FontSize = 15, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(b.Color), HorizontalAlignment = HorizontalAlignment.Right });
        if (b.Rate.Length > 0)
        {
            right.Children.Add(new TextBlock { Text = b.Rate, FontSize = 10, Foreground = Muted, HorizontalAlignment = HorizontalAlignment.Right });
        }

        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        return new Border
        {
            Child = grid, Margin = new Thickness(8, 8, 8, 2), Padding = new Thickness(8, 7, 10, 7), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            Background = new LinearGradientBrush(Alpha(b.Color, 0x40), Alpha(b.Color, 0x0C), 0),
            BorderBrush = new LinearGradientBrush(Alpha(b.Color, 0xA0), Alpha(b.Color, 0x30), 0),
        };
    }

    private static FrameworkElement Row(LootOverlayLine line, bool glow)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(Icon(line.IconPath, line.Color, 26, glow: line.Grade >= LootStats.Gold));

        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 8, 0) };
        names.Children.Add(new TextBlock
        {
            Text = line.Name, FontSize = 12.5, FontWeight = line.Grade >= 3 ? FontWeights.SemiBold : FontWeights.Normal, Foreground = new SolidColorBrush(line.Color),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (line.Rate is not null)
        {
            var rate = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
            var text = new TextBlock { Text = line.Rate, FontSize = 10, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(text, Dock.Left);
            rate.Children.Add(text);
            // the share of the kills it dropped in (square root, so a rare drop still shows)
            double share = Math.Clamp(Math.Sqrt(line.Share), 0.04, 1.0);
            var bar = new Grid { Height = 3, MaxWidth = 70, Margin = new Thickness(8, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(share, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - share + 1e-6, GridUnitType.Star) });
            var track = new Border { CornerRadius = new CornerRadius(1.5), Background = Track };
            Grid.SetColumnSpan(track, 2);
            bar.Children.Add(track);
            bar.Children.Add(new Border { CornerRadius = new CornerRadius(1.5), Background = new SolidColorBrush(Alpha(line.Color, 0xD0)) });
            rate.Children.Add(bar);
            names.Children.Add(rate);
        }

        Grid.SetColumn(names, 1);
        grid.Children.Add(names);
        var count = new TextBlock
        {
            Text = "×" + line.Count.ToString("N0"), FontSize = 13, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center,
            Foreground = line.Grade >= LootStats.Gold ? new SolidColorBrush(line.Color) : Brushes.White,
        };
        Grid.SetColumn(count, 2);
        grid.Children.Add(count);

        // blue and better: a wash of its colour from the left; the newest pickup: a gold glow that fades
        Brush background = line.Grade >= 3 ? new LinearGradientBrush(Alpha(line.Color, 0x26), Alpha(line.Color, 0x00), 0) : Brushes.Transparent;
        var row = new Border { Child = grid, Padding = new Thickness(6, 4, 8, 4), Margin = new Thickness(0, 1, 0, 1), CornerRadius = new CornerRadius(7), Background = background };
        if (glow)
        {
            var flash = new SolidColorBrush(Alpha(GoldColor, 0x60));
            var host = new Grid();
            var shine = new Border { CornerRadius = new CornerRadius(7), Background = flash, Margin = row.Margin, IsHitTestVisible = false };
            host.Children.Add(row);
            host.Children.Add(shine);
            flash.BeginAnimation(SolidColorBrush.ColorProperty, new System.Windows.Media.Animation.ColorAnimation(Alpha(GoldColor, 0x00), TimeSpan.FromSeconds(1.8))
            {
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
            });
            return host;
        }

        return row;
    }

    /// <summary>An item's icon in a rounded frame of its grade's colour (a soft glow for gold and better); a diamond of that colour without a picture.</summary>
    private static FrameworkElement Icon(string? path, Color color, double size, bool glow)
    {
        var frame = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = new SolidColorBrush(Alpha(color, 0xC0)),
            Background = new RadialGradientBrush(Alpha(color, 0x48), Alpha(color, 0x10)),
        };
        frame.Child = OverlayPictures.Load(path) is ImageSource picture
            ? new Image { Source = picture, Stretch = Stretch.Uniform, Margin = new Thickness(1.5) }
            : new TextBlock { Text = "◆", FontSize = size * 0.42, Foreground = new SolidColorBrush(color), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (glow)
        {
            frame.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = color, BlurRadius = 9, ShadowDepth = 0, Opacity = 0.55 };
        }

        return frame;
    }
}

/// <summary>Runs the loot overlay: twice a second it reads the settings and the loot of the running session, and shows the overlay only while the game is in front
/// (or while it is unlocked so it can be placed).</summary>
public sealed class LootOverlayController : IDisposable
{
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private LootOverlayWindow? _overlay;
    private MeterSettings _settings = MeterSettings.Load();
    private DateTime _settingsAt = DateTime.UtcNow;

    public LootOverlayController()
    {
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public void RaiseToFront() => _overlay?.RaiseToFront();

    public void Dispose()
    {
        _timer.Stop();
        _overlay?.Close();
    }

    private void Tick()
    {
        if ((DateTime.UtcNow - _settingsAt).TotalSeconds >= 1)
        {
            _settings = MeterSettings.Load();
            _settingsAt = DateTime.UtcNow;
        }

        if (!_settings.ShowLootOverlay)
        {
            _overlay?.ShowOverlay(false);
            return;
        }

        bool locked = _settings.LootOverlayLocked;
        _overlay ??= new LootOverlayWindow();
        _overlay.ApplyLockedIfChanged(locked);
        _overlay.Render(LootText.OverlayLines(Aion2Loot.Current, Aion2Loot.CurrentZone, _settings, sample: !locked));
        if (!GameWindow.OverlayMayShow() || (locked && !_overlay.HasContent))
        {
            _overlay.ShowOverlay(false);
            return;
        }

        _overlay.ShowOverlay(true);
    }
}
