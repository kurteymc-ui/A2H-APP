using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>The pictures of the gathered resources (the client's item icons), loaded once each.</summary>
public static class GatherIcons
{
    private static readonly Dictionary<int, ImageSource?> Cache = new();

    /// <summary>The icon of an item (a resource at one grade), null when the meter has none.</summary>
    public static ImageSource? Of(int itemId)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(itemId, out ImageSource? known))
            {
                return known;
            }

            ImageSource? image = null;
            if (Aion2Artwork.ItemIconPath(itemId) is { } path && File.Exists(path))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(path);
                    bitmap.DecodePixelWidth = 48;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    image = bitmap;
                }
                catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException)
                {
                    image = null;
                }
            }

            Cache[itemId] = image;
            return image;
        }
    }

    /// <summary>A resource's icon at a size: its normal grade's item picture, or a small diamond in the colour of its kind when there is none.</summary>
    public static FrameworkElement Picture(string key, double size)
    {
        int item = Aion2GatherLog.BaseItemOf(key);
        if (Of(item) is { } source)
        {
            return new Image { Source = source, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        }

        string kind = Aion2Gather.Items().FirstOrDefault(i => i.Key == key)?.Kind ?? "Ore";
        return new System.Windows.Shapes.Polygon
        {
            Points = new PointCollection { new Point(size / 2, 0), new Point(size, size / 2), new Point(size / 2, size), new Point(0, size / 2) },
            Fill = new SolidColorBrush(PetMapPalette.OfGather(kind)), Stroke = Brushes.Black, StrokeThickness = 1, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>The colour of a grade (0 normal ... 3 Radiant): the game's item grade colours.</summary>
    public static Color TierColor(int tier) => CharacterWindow.GradeColors.GetValueOrDefault(tier + 1, CharacterWindow.GradeColors[1]);
}

/// <summary>
/// The gathering overlay: what was gathered in this run of the meter, resource by resource - its icon and name, the total, a count for every grade it comes in
/// (normal, Fine or Fresh, Pure, Radiant, each in the game's grade colour, a dim 0 for none yet) and how many tries failed - the most gathered on top. Placed,
/// scaled and locked like the other overlays (<see cref="MovableOverlay"/>); shown only while the game is in front.
/// </summary>
public sealed class GatherOverlayWindow : MovableOverlay
{
    private const double IconBox = 32, Cell = 48, CellGap = 4;
    private static readonly Color Muted = Color.FromRgb(0x8F, 0xA3, 0xB0), Gold = Color.FromRgb(0xE8, 0xC9, 0x8A), Failed = Color.FromRgb(0xE6, 0xA2, 0x6B);
    private readonly StackPanel _rows = new() { MinWidth = IconBox + 10 + (4 * Cell) + (3 * CellGap), MinHeight = 30 };
    private string _shown = "";

    public GatherOverlayWindow() : base(OverlayPlaces.Gather, new Border { Child = null }, Load, Save, SystemParameters.WorkArea.Left + 40, 560, LoadSize, SaveSize, OverlayEdgeMode.Width)
    {
        Body = new Border
        {
            Background = new LinearGradientBrush(Color.FromArgb(0xC8, 0x15, 0x22, 0x2D), Color.FromArgb(0xC8, 0x0B, 0x13, 0x1A), 90),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, 0x4A, 0x66, 0x78)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 9), Child = _rows,
        };
    }

    public new bool HasContent => _rows.Children.Count > 0;

    private static (double?, double?) LoadSize()
    {
        var s = MeterSettings.Load();
        return (s.GatherOverlayWidth, s.GatherOverlayHeight);
    }

    private static void SaveSize(double? width, double? height)
    {
        var s = MeterSettings.Load();
        s.GatherOverlayWidth = width;
        s.GatherOverlayHeight = height;
        s.Save();
    }

    private static (double?, double?, double) Load()
    {
        var s = MeterSettings.Load();
        return (s.GatherOverlayLeft, s.GatherOverlayTop, s.GatherOverlayScale);
    }

    private static void Save(double left, double top, double scale)
    {
        var s = MeterSettings.Load();
        s.GatherOverlayLeft = left;
        s.GatherOverlayTop = top;
        s.GatherOverlayScale = scale;
        s.Save();
    }

    private static SolidColorBrush Paint(Color color) => new(color);

    /// <summary>"Ruby: Fine" in the grade's colour with its picture, or "Ruby: will fail" in orange: the roll of the gathering under way.</summary>
    private static FrameworkElement RollLine(GatherForecast roll, LocalizationManager loc, CultureInfo culture)
    {
        string name = Aion2ItemNames.NameOf(Aion2GatherLog.BaseItemOf(roll.Key), loc.Language) ?? roll.Key;
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 6) };
        if (!roll.Fails && GatherIcons.Of(Aion2GatherLog.ItemOf(roll.Key, roll.Tier)) is { } icon)
        {
            line.Children.Add(new Image { Source = icon, Width = 26, Height = 26, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        }

        line.Children.Add(new TextBlock
        {
            Text = roll.Fails ? string.Format(culture, loc["Gather.Roll.Fails"], name)
                : string.Format(culture, loc["Gather.Roll.Gives"], name, Aion2GatherLog.GradeName(roll.Key, roll.Tier, k => loc[k])),
            FontSize = 15, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center,
            Foreground = Paint(roll.Fails ? Failed : GatherIcons.TierColor(roll.Tier)),
        });
        return line;
    }

    /// <summary>The rows of the tally (<paramref name="sample"/>: made-up ones, to place the overlay in its real size while nothing was gathered yet).</summary>
    /// <param name="forecast">What the game rolled for the gathering under way (shown big above the tally until the result comes).</param>
    public void Render(IReadOnlyList<GatherTally> tallies, bool sample, GatherForecast? forecast = null)
    {
        var loc = LocalizationManager.Instance;
        string key = sample + "|" + loc.Language + "|" + forecast?.At.Ticks + "|" + string.Join(";", tallies.Select(t => t.Key + ":" + string.Join(",", t.ByTier) + ":" + t.Gathers + ":" + t.Tries + ":" + t.Fails));
        if (key == _shown)
        {
            return;
        }

        _shown = key;
        _rows.Children.Clear();
        CultureInfo culture = CultureInfo.CurrentCulture;
        FrameworkElement? rollLine = forecast is { } roll ? RollLine(roll, loc, culture) : null;
        if (tallies.Count == 0)
        {
            if (rollLine is not null)
            {
                _rows.Children.Add(rollLine);
            }

            return;
        }

        List<GatherTally> shown = tallies.Take(10).ToList();

        // the head: "Gathered" and the total of all
        var head = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 3) };
        var total = new TextBlock
        {
            Text = string.Format(culture, loc["Gather.Overlay.Total"], tallies.Sum(t => t.Total).ToString("N0", culture)), FontWeight = FontWeights.SemiBold, FontSize = 12.5,
            Foreground = Paint(Gold), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10, 0, 0, 0),
        };
        DockPanel.SetDock(total, Dock.Right);
        head.Children.Add(total);
        head.Children.Add(new TextBlock
        {
            Text = loc["Gather.Overlay.Caption"], FontWeight = FontWeights.Bold, FontSize = 13.5, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Bottom,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        _rows.Children.Add(head);
        if (rollLine is not null)
        {
            _rows.Children.Add(rollLine);
        }

        // the grades and their colours, once ("Fine" for ore and gems, "Fresh" for herbs: the same grade)
        bool anyFresh = shown.Any(t => Aion2GatherLog.IsFresh(t.Key)), anyFine = shown.Any(t => !Aion2GatherLog.IsFresh(t.Key) && Aion2GatherLog.TiersOf(t.Key)[1]);
        var legend = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        for (int tier = 0; tier < 4; tier++)
        {
            if (tier > 1 && !shown.Any(t => Aion2GatherLog.TiersOf(t.Key)[tier]))
            {
                continue; // only herbs shown: they come in no Pure or Radiant
            }

            string name = tier switch
            {
                0 => loc["Gather.Tally.Normal"],
                1 => anyFresh && anyFine ? loc["Gather.Tally.Fine"] + " / " + loc["Gather.Tally.Fresh"] : anyFresh ? loc["Gather.Tally.Fresh"] : loc["Gather.Tally.Fine"],
                2 => loc["Gather.Tally.Pure"],
                _ => loc["Gather.Tally.Radiant"],
            };
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0) };
            item.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(1.5), Background = Paint(GatherIcons.TierColor(tier)), VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 1, 4, 0), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(45) });
            item.Children.Add(new TextBlock { Text = name, FontSize = 10.5, Foreground = Paint(GatherIcons.TierColor(tier)) });
            legend.Children.Add(item);
        }

        _rows.Children.Add(legend);

        foreach (GatherTally tally in shown)
        {
            _rows.Children.Add(Row(tally, loc, culture));
        }

        if (tallies.Count > shown.Count)
        {
            _rows.Children.Add(new TextBlock { Text = string.Format(loc["Loot.More"], tallies.Count - shown.Count), FontSize = 11, Foreground = Paint(Muted), Margin = new Thickness(0, 4, 0, 0) });
        }
    }

    // One resource: its icon in a small tile, the name and total, a cell per grade it comes in, and the tries with how many failed.
    private static FrameworkElement Row(GatherTally tally, LocalizationManager loc, CultureInfo culture)
    {
        var row = new Grid { Margin = new Thickness(0, 5, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IconBox + 10) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        FrameworkElement picture = GatherIcons.Picture(tally.Key, IconBox - 6);
        var tile = new Border
        {
            Width = IconBox, Height = IconBox, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1), Child = picture, VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 1, 0, 0),
        };
        picture.HorizontalAlignment = HorizontalAlignment.Center;
        row.Children.Add(tile);

        var text = new StackPanel();
        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        var title = new DockPanel { LastChildFill = true };
        var sum = new TextBlock { Text = "×" + tally.Total.ToString("N0", culture), FontWeight = FontWeights.Bold, FontSize = 13, Foreground = Paint(Gold), Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(sum, Dock.Right);
        title.Children.Add(sum);
        title.Children.Add(new TextBlock
        {
            Text = Aion2ItemNames.NameOf(Aion2GatherLog.BaseItemOf(tally.Key), loc.Language) ?? tally.Key, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        text.Children.Add(title);

        // a cell per grade, in its colour; a grade the resource does not come in (herbs: Pure, Radiant) keeps its place empty so the columns line up
        bool[] tiers = Aion2GatherLog.TiersOf(tally.Key);
        var cells = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
        for (int tier = 0; tier < 4; tier++)
        {
            var cell = new Border { Width = Cell, Height = 18, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, tier < 3 ? CellGap : 0, 0) };
            if (tiers[tier] || tally.ByTier[tier] > 0)
            {
                long count = tally.ByTier[tier];
                Color color = GatherIcons.TierColor(tier);
                cell.Background = new SolidColorBrush(count > 0 ? Color.FromArgb(0x33, color.R, color.G, color.B) : Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF));
                cell.BorderBrush = new SolidColorBrush(count > 0 ? Color.FromArgb(0x66, color.R, color.G, color.B) : Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF));
                cell.BorderThickness = new Thickness(1);
                cell.Child = new TextBlock
                {
                    Text = count.ToString("N0", culture), FontSize = 11, FontWeight = count > 0 ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = count > 0 ? Paint(color) : new SolidColorBrush(Color.FromArgb(0x80, Muted.R, Muted.G, Muted.B)),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                };
            }

            cells.Children.Add(cell);
        }

        text.Children.Add(cells);

        // how often it was tried, and how often that brought nothing
        if (tally.Tries > 0)
        {
            text.Children.Add(new TextBlock
            {
                Text = string.Format(culture, loc["Gather.Overlay.Tries"], tally.Tries.ToString("N0", culture), tally.Fails.ToString("N0", culture)),
                FontSize = 10.5, Foreground = Paint(tally.Fails > 0 ? Failed : Muted), Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        return row;
    }

    /// <summary>Made-up rows in the overlay's real size, to place it while nothing was gathered yet.</summary>
    public static IReadOnlyList<GatherTally> Sample() => new[]
    {
        new GatherTally { Key = "Orichalcum", ByTier = new long[] { 42, 4, 1, 0 }, Gathers = 17, Tries = 19, Fails = 2 },
        new GatherTally { Key = "Aria", ByTier = new long[] { 18, 3, 0, 0 }, Gathers = 9, Tries = 9 },
        new GatherTally { Key = "Odyle", ByTier = new long[] { 12, 0, 0, 1 }, Gathers = 7, Tries = 8, Fails = 1 },
    };
}

/// <summary>Runs the gathering overlay: twice a second it reads the settings and this session's tally, and shows the overlay only while the game is in front (or
/// while it is unlocked so it can be placed), and only once something was gathered.</summary>
public sealed class GatherOverlayController : IDisposable
{
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private GatherOverlayWindow? _overlay;
    private MeterSettings _settings = MeterSettings.Load();
    private DateTime _settingsAt = DateTime.UtcNow;

    public GatherOverlayController()
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

        if (!_settings.ShowGatherOverlay)
        {
            _overlay?.ShowOverlay(false);
            return;
        }

        bool locked = _settings.GatherOverlayLocked;
        _overlay ??= new GatherOverlayWindow();
        _overlay.ApplyLockedIfChanged(locked);
        IReadOnlyList<GatherTally> tallies = Aion2GatherLog.Tallies(session: true);
        GatherForecast? forecast = Aion2GatherLog.Forecast();
        bool sample = tallies.Count == 0 && forecast is null && !locked;
        _overlay.Render(sample ? GatherOverlayWindow.Sample() : tallies, sample, forecast);
        if (!GameWindow.OverlayMayShow() || (locked && !_overlay.HasContent))
        {
            _overlay.ShowOverlay(false);
            return;
        }

        _overlay.ShowOverlay(true);
    }
}
