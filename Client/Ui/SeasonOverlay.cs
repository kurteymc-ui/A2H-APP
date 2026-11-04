using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDPS.Aion2;

namespace AionDPS.Ui;

/// <summary>
/// The look shared by the season card of the checklist and the season overlay: each category's colour, and the card a mission shows on hover (its category,
/// name, what it asks now in the game's words, the count with a bar and every stage, the one worked on marked).
/// </summary>
public static class SeasonLook
{
    public static readonly Color Gold = Color.FromRgb(0xE8, 0xC9, 0x8A), Done = Color.FromRgb(0x6F, 0xCF, 0x8E), Muted = Color.FromRgb(0x95, 0xA6, 0xB2);

    /// <summary>The colour of a category (the game's tabs): Abyss violet, Arena red, Transcendence blue, Nightmare magenta, Challenge gold, Event green.</summary>
    public static Color Of(string category) => category switch
    {
        "Abyss" => Color.FromRgb(0x9B, 0x86, 0xF0),
        "Arena" => Color.FromRgb(0xE8, 0x6E, 0x5E),
        "PartyChallenge" => Color.FromRgb(0x5E, 0xA8, 0xE8),
        "Nightmare" => Color.FromRgb(0xD0, 0x6C, 0xC8),
        "Thema" => Color.FromRgb(0xE0, 0xB4, 0x52),
        "Event" => Color.FromRgb(0x5C, 0xC9, 0x9A),
        _ => Color.FromRgb(0x95, 0xA6, 0xB2),
    };

    public static string CategoryName(string category, string language) =>
        SeasonMissions.Categories.TryGetValue(category, out IReadOnlyDictionary<string, string>? names) ? SeasonMissions.Text(names, language) : category;

    /// <summary>"1,240 / 3,000", "0.6 / 1 h", "2 / 5" for a condition step (steps done), or the done word.</summary>
    public static string CountText(SeasonProgress p)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        SeasonMission m = p.Mission;
        if (p.Finished)
        {
            return LocalizationManager.Instance["Checklist.SeasonDone"];
        }

        return p.Next?.Target is { } target
            ? (p.Stale ? "~" : "") + SeasonMissions.CountText(m, p.Count, culture) + " / " + SeasonMissions.CountText(m, target, culture) + (m.Hours ? " h" : "")
            : string.Format(culture, "{0} / {1}", p.Done, m.Steps.Count);
    }

    private static SolidColorBrush Paint(Color c) => new(c);

    private static SolidColorBrush Paint(byte a, Color c) => new(Color.FromArgb(a, c.R, c.G, c.B));

    /// <summary>A thin rounded bar: the track and the part done.</summary>
    public static FrameworkElement Bar(double fraction, Color color, double height)
    {
        var track = new Grid { Height = height, VerticalAlignment = VerticalAlignment.Center };
        track.Children.Add(new Border { CornerRadius = new CornerRadius(height / 2), Background = Paint(0x2A, Colors.White) });
        var fill = new Border { CornerRadius = new CornerRadius(height / 2), Background = Paint(color), HorizontalAlignment = HorizontalAlignment.Left };
        track.Children.Add(fill);
        track.SizeChanged += (_, e) => fill.Width = Math.Max(fraction > 0 ? height : 0, e.NewSize.Width * Math.Clamp(fraction, 0, 1));
        return track;
    }

    /// <summary>The hover card of a mission (dark, the same in the checklist and over the game).</summary>
    public static FrameworkElement Card(SeasonProgress p, string language)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        SeasonMission m = p.Mission;
        Color color = Of(m.Category);
        var stack = new StackPanel { MaxWidth = 380 };

        var kind = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };
        kind.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = Paint(color), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        kind.Children.Add(new TextBlock
        {
            Text = CategoryName(m.Category, language) + "  ·  " + loc[m.Weekly ? "Checklist.SeasonWeekly" : "Checklist.SeasonLong"], FontSize = 11, Foreground = Paint(color),
            FontWeight = FontWeights.SemiBold,
        });
        stack.Children.Add(kind);
        stack.Children.Add(new TextBlock { Text = SeasonMissions.Text(m.Name, language), FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new TextBlock
        {
            Text = SeasonMissions.Description(p, language), FontSize = 12.5, Foreground = Paint(Color.FromRgb(0xD6, 0xDE, 0xE4)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 8),
        });

        var progress = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        progress.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        progress.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        progress.Children.Add(Bar(SeasonMissions.Fraction(p), p.Finished ? Done : Gold, 6));
        var count = new TextBlock { Text = CountText(p), FontSize = 12.5, FontWeight = FontWeights.Bold, Foreground = Paint(p.Finished ? Done : Gold), Margin = new Thickness(10, 0, 0, 0) };
        Grid.SetColumn(count, 1);
        progress.Children.Add(count);
        stack.Children.Add(progress);

        stack.Children.Add(new TextBlock
        {
            Text = string.Format(CultureInfo.CurrentCulture, loc["Checklist.SeasonStage"], p.Done, m.Steps.Count).ToUpper(CultureInfo.CurrentCulture), FontSize = 10, FontWeight = FontWeights.SemiBold,
            Foreground = Paint(Muted), Margin = new Thickness(0, 0, 0, 3),
        });
        for (int i = 0; i < m.Steps.Count; i++)
        {
            bool done = i < p.Done, now = i == p.Done && !p.Finished;
            var line = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
            var mark = new TextBlock { Text = done ? "✓" : now ? "▸" : "○", Width = 16, FontSize = 11.5, Foreground = Paint(done ? Done : now ? Gold : Muted) };
            DockPanel.SetDock(mark, Dock.Left);
            line.Children.Add(mark);
            line.Children.Add(new TextBlock
            {
                Text = SeasonMissions.Text(m.Steps[i].Text, language), FontSize = 11.5, TextWrapping = TextWrapping.Wrap,
                Foreground = done ? Paint(Muted) : now ? Paint(Gold) : Paint(Color.FromRgb(0xC2, 0xCC, 0xD3)), FontWeight = now ? FontWeights.SemiBold : FontWeights.Normal,
            });
            stack.Children.Add(line);
        }

        if (p.Stale)
        {
            stack.Children.Add(new TextBlock { Text = loc["Checklist.SeasonStale"], FontSize = 11, FontStyle = FontStyles.Italic, Foreground = Paint(Muted), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        }

        return new Border
        {
            Background = new LinearGradientBrush(Color.FromArgb(0xF4, 0x18, 0x22, 0x2B), Color.FromArgb(0xF4, 0x10, 0x16, 0x1D), 90),
            BorderBrush = Paint(0x99, color), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 9, 12, 10), Child = stack,
        };
    }

    /// <summary>The hover card as a tooltip of an element (no tooltip frame of its own around it).</summary>
    public static void AttachCard(FrameworkElement target, Func<FrameworkElement> card)
    {
        var tip = new ToolTip { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), HasDropShadow = false, Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse };
        target.ToolTip = tip;
        target.ToolTipOpening += (_, _) => tip.Content = card(); // built when it opens: a page of tiles does not build all its cards
        ToolTipService.SetInitialShowDelay(target, 250);
        ToolTipService.SetShowDuration(target, 60000);
    }
}

/// <summary>
/// The season overlay: the missions picked on the checklist (★; none picked: the unfinished weekly ones) of the character logged in, live: the category's
/// colour, the name, the count against the step worked on and a bar. Resting the mouse on a line shows its hover card beside the overlay, also while the overlay
/// is locked and lets the clicks through (<see cref="SeasonOverlayController"/> follows the cursor). Placed, scaled and locked like the other overlays.
/// </summary>
public sealed class SeasonOverlayWindow : MovableOverlay
{
    private readonly StackPanel _rows = new() { MinWidth = 250 };
    private readonly List<(FrameworkElement Row, SeasonProgress Progress)> _lines = new();
    private string _shown = "";

    public SeasonOverlayWindow() : base(OverlayPlaces.Season, new Border { Child = null }, Load, Save, SystemParameters.WorkArea.Right - 340, 220, LoadSize, SaveSize, OverlayEdgeMode.Width)
    {
        Body = new Border
        {
            Background = new LinearGradientBrush(Color.FromArgb(0xC8, 0x15, 0x22, 0x2D), Color.FromArgb(0xC8, 0x0B, 0x13, 0x1A), 90),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, 0x4A, 0x66, 0x78)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 9), Child = _rows,
        };
    }

    public new bool HasContent => _lines.Count > 0;

    /// <summary>The lines shown, for the hover: each line's element and its mission.</summary>
    public IReadOnlyList<(FrameworkElement Row, SeasonProgress Progress)> Lines => _lines;

    private static (double?, double?) LoadSize()
    {
        var s = MeterSettings.Load();
        return (s.SeasonOverlayWidth, s.SeasonOverlayHeight);
    }

    private static void SaveSize(double? width, double? height)
    {
        var s = MeterSettings.Load();
        s.SeasonOverlayWidth = width;
        s.SeasonOverlayHeight = height;
        s.Save();
    }

    private static (double?, double?, double) Load()
    {
        var s = MeterSettings.Load();
        return (s.SeasonOverlayLeft, s.SeasonOverlayTop, s.SeasonOverlayScale);
    }

    private static void Save(double left, double top, double scale)
    {
        var s = MeterSettings.Load();
        s.SeasonOverlayLeft = left;
        s.SeasonOverlayTop = top;
        s.SeasonOverlayScale = scale;
        s.Save();
    }

    /// <summary>The lines (<paramref name="sample"/>: made-up ones, to place the overlay while nothing is to show), with the time left to the weekly reset.</summary>
    public void Render(IReadOnlyList<SeasonProgress> list, TimeSpan untilReset, bool sample)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        string language = loc.Language;
        string key = sample + "|" + language + "|" + (int)untilReset.TotalHours + "|" + string.Join(";", list.Select(p => $"{p.Mission.Id}:{p.Count}:{p.Done}:{p.Stale}"));
        if (key == _shown)
        {
            return;
        }

        _shown = key;
        _rows.Children.Clear();
        _lines.Clear();
        if (list.Count == 0)
        {
            return;
        }

        var head = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 5) };
        var resets = new TextBlock
        {
            Text = "↻ " + ChecklistWindow.LeftText(untilReset), FontSize = 11, Foreground = new SolidColorBrush(SeasonLook.Muted), VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(10, 0, 0, 1), ToolTip = string.Format(CultureInfo.CurrentCulture, loc["Checklist.SeasonResets"], ChecklistWindow.LeftText(untilReset)),
        };
        DockPanel.SetDock(resets, Dock.Right);
        head.Children.Add(resets);
        head.Children.Add(new TextBlock
        {
            Text = loc["Checklist.Season"], FontWeight = FontWeights.Bold, FontSize = 13.5, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Bottom, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        _rows.Children.Add(head);

        foreach (SeasonProgress p in list.Take(14))
        {
            FrameworkElement row = Line(p, language);
            _rows.Children.Add(row);
            _lines.Add((row, p));
        }

        if (list.Count > 14)
        {
            _rows.Children.Add(new TextBlock { Text = string.Format(loc["Loot.More"], list.Count - 14), FontSize = 11, Foreground = new SolidColorBrush(SeasonLook.Muted), Margin = new Thickness(0, 4, 0, 0) });
        }
    }

    // One mission: a stripe in the category's colour, the name and the count, a bar under them.
    private static FrameworkElement Line(SeasonProgress p, string language)
    {
        Color color = SeasonLook.Of(p.Mission.Category);
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3), Background = Brushes.Transparent };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new Border { CornerRadius = new CornerRadius(1.5), Background = new SolidColorBrush(color), Margin = new Thickness(0, 1, 0, 1) });

        var text = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        var top = new DockPanel { LastChildFill = true };
        var count = new TextBlock
        {
            Text = p.Finished ? "✓" : SeasonLook.CountText(p), FontSize = 12, FontWeight = FontWeights.Bold, Margin = new Thickness(8, 0, 0, 0),
            Foreground = new SolidColorBrush(p.Finished ? SeasonLook.Done : p.Count > 0 || p.Done > 0 ? SeasonLook.Gold : SeasonLook.Muted),
        };
        DockPanel.SetDock(count, Dock.Right);
        top.Children.Add(count);
        top.Children.Add(new TextBlock
        {
            Text = SeasonMissions.Text(p.Mission.Name, language), FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = p.Finished ? new SolidColorBrush(SeasonLook.Muted) : Brushes.White,
        });
        text.Children.Add(top);
        FrameworkElement bar = SeasonLook.Bar(p.Finished ? 1 : p.Next?.Target is null ? (double)p.Done / p.Mission.Steps.Count : SeasonMissions.Fraction(p), p.Finished ? SeasonLook.Done : SeasonLook.Gold, 4);
        bar.Margin = new Thickness(0, 3, 0, 0);
        text.Children.Add(bar);
        return row;
    }

    /// <summary>Made-up lines in the overlay's real size, to place it while there is nothing to show yet.</summary>
    public static IReadOnlyList<SeasonProgress> Sample()
    {
        var weekly = SeasonMissions.All.Where(m => m.Faction == 'L' && m.Weekly && m.Steps.Count >= 2 && m.Steps[0].Target is > 0).Take(4).ToList();
        return weekly.Select((m, i) => new SeasonProgress(m, (int)(m.Steps[0].Target!.Value * (0.2 + 0.25 * i)), i == 3 ? m.Steps.Count : 0, false)).ToList();
    }
}

/// <summary>
/// Runs the season overlay: twice a second the settings and the progress of the character logged in; the overlay shows only over the game (or while unlocked, to
/// be placed). Ten times a second it follows the cursor: resting on a line a moment shows that mission's hover card beside the overlay - a separate window that
/// lets the clicks through like the overlay, so the hover works while the overlay is locked.
/// </summary>
public sealed class SeasonOverlayController : IDisposable
{
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private SeasonOverlayWindow? _overlay;
    private MeterSettings _settings = MeterSettings.Load();
    private DateTime _settingsAt = DateTime.MinValue, _renderAt = DateTime.MinValue;
    private SeasonProgress? _hovered;
    private DateTime _hoverSince;

    public SeasonOverlayController()
    {
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public void RaiseToFront() => _overlay?.RaiseToFront();

    public void Dispose()
    {
        _timer.Stop();
        SeasonHoverCard.Hide();
        _overlay?.Close();
    }

    private void Tick()
    {
        DateTime now = DateTime.Now;
        if ((now - _settingsAt).TotalSeconds >= 1)
        {
            _settings = MeterSettings.Load();
            _settingsAt = now;
        }

        if (!_settings.ShowSeasonOverlay)
        {
            _overlay?.ShowOverlay(false);
            SeasonHoverCard.Hide();
            return;
        }

        bool locked = _settings.SeasonOverlayLocked;
        _overlay ??= new SeasonOverlayWindow();
        _overlay.ApplyLockedIfChanged(locked);
        if ((now - _renderAt).TotalMilliseconds >= 500)
        {
            _renderAt = now;
            var current = SeasonMissions.Current(now);
            IReadOnlyList<SeasonProgress> list = current is { } c && Aion2Achievements.Of(c.Name).Count > 0 ? SeasonMissions.ForOverlay(c.Progress, _settings.SeasonOverlayMissions) : Array.Empty<SeasonProgress>();
            bool sample = list.Count == 0 && !locked;
            _overlay.Render(sample ? SeasonOverlayWindow.Sample() : list, Aion2Tickets.NextWeeklyResetAfter(now) - now, sample);
        }

        if (!GameWindow.OverlayMayShow() || (locked && !_overlay.HasContent))
        {
            _overlay.ShowOverlay(false);
            SeasonHoverCard.Hide();
            return;
        }

        _overlay.ShowOverlay(true);
        FollowCursor(now);
    }

    // the line under the cursor (screen pixels, as the system gives them): after 0.3 s its card, until the cursor leaves it
    private void FollowCursor(DateTime now)
    {
        SeasonProgress? over = null;
        FrameworkElement? overRow = null;
        if (_overlay is { IsVisible: true } && NativeCursor.TryGet(out int cx, out int cy))
        {
            foreach ((FrameworkElement row, SeasonProgress p) in _overlay.Lines)
            {
                if (!row.IsVisible || PresentationSource.FromVisual(row) is null)
                {
                    continue;
                }

                Point a = row.PointToScreen(new Point(0, 0)), b = row.PointToScreen(new Point(row.ActualWidth, row.ActualHeight));
                if (cx >= Math.Min(a.X, b.X) && cx <= Math.Max(a.X, b.X) && cy >= Math.Min(a.Y, b.Y) - 2 && cy <= Math.Max(a.Y, b.Y) + 2)
                {
                    over = p;
                    overRow = row;
                    break;
                }
            }
        }

        if (over is null || overRow is null)
        {
            _hovered = null;
            SeasonHoverCard.Hide();
            return;
        }

        if (_hovered is not { } was || was.Mission.Id != over.Value.Mission.Id)
        {
            _hovered = over;
            _hoverSince = now;
            SeasonHoverCard.Hide();
            return;
        }

        if ((now - _hoverSince).TotalMilliseconds >= 300)
        {
            SeasonProgress shown = over.Value;
            SeasonHoverCard.Show(() => SeasonLook.Card(shown, LocalizationManager.Instance.Language), _overlay!, overRow);
        }
    }
}

/// <summary>The hover card over the game: a click-through window beside the overlay, at the height of the line it belongs to, never taking the focus.</summary>
public static class SeasonHoverCard
{
    private static Window? _window;
    private static int _shownFor;

    public static void Show(Func<FrameworkElement> card, Window overlay, FrameworkElement row)
    {
        if (MainWindow.Headless)
        {
            return;
        }

        if (_window is null)
        {
            _window = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false, ShowActivated = false,
                SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, Focusable = false,
            };
            _window.SourceInitialized += (_, _) => new NativeOverlay(_window).SetClickThrough(true);
        }

        int id = row.GetHashCode();
        if (_window.IsVisible && _shownFor == id)
        {
            return;
        }

        _shownFor = id;
        _window.Content = card();
        if (!_window.IsVisible)
        {
            _window.Show();
        }

        _window.UpdateLayout();
        // beside the overlay (right of it, or left where the screen ends), level with the line, kept on the screen
        Point at = row.TranslatePoint(new Point(0, 0), overlay);
        Rect area = SystemParameters.WorkArea;
        double width = _window.ActualWidth, height = _window.ActualHeight;
        double left = overlay.Left + overlay.ActualWidth + 8;
        if (left + width > area.Right)
        {
            left = overlay.Left - width - 8;
        }

        _window.Left = Math.Max(area.Left, left);
        _window.Top = Math.Clamp(overlay.Top + at.Y - 8, area.Top, Math.Max(area.Top, area.Bottom - height));
    }

    public static void Hide()
    {
        if (_window is { IsVisible: true })
        {
            _window.Hide();
        }

        _shownFor = 0;
    }
}
