using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;
using AionDPS.Upload;
using static AionDPS.Ui.JourneyViews;

namespace AionDPS.Ui;

/// <summary>
/// Journey (Characters, beside the CP calculator; 5 November 2026, the owner: "any period; what improved; where to improve; to share; you and your class"): a
/// character's play over a period it picks (the session, a day, seven days, the game's week, the week before, thirty days, the month, everything, or dates of its
/// own) against the period just before: the character and the periods on a rail, then the period's story, a scoreboard of its figures, its days as lines, what
/// went well, what to work on next, and its uploaded boss fights ranked against its class and its power on atreia.app; a card to copy into Discord.
/// </summary>
internal sealed class JourneyPage : Grid
{
    private readonly Func<Aion2EntityDirectory?> _directory;
    private readonly StackPanel _root = new() { Margin = new Thickness(24, 18, 24, 28), MaxWidth = 1200 };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private JourneySpan _span = JourneySpan.Week7;
    private DateTime? _customFrom, _customTo;
    private readonly List<JourneyWho> _characters;
    private JourneyWho? _who;
    private JourneyReport? _report;
    private (string? Class, List<JourneyRank> Ranks)? _ranks;
    private string? _ranksFor;
    private bool _ranksLoading;
    private int _generation;
    private int _pricesSeen = -1;
    private ImageSource? _portrait;
    private string? _portraitFor;

    private static LocalizationManager Loc => LocalizationManager.Instance;

    public JourneyPage(Func<Aion2EntityDirectory?> directory)
    {
        _directory = directory;
        Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _root });
        _characters = MainWindow.Headless ? new List<JourneyWho> { new("Aerin", 2302, "Templar") } : JourneyData.Characters(directory());
        _who = _characters.FirstOrDefault();
        Load();
        if (!MainWindow.Headless)
        {
            // the market's prices come a moment later (the loot's worth): drawn again then
            _timer.Tick += (_, _) =>
            {
                if (ExchangePrices.Version != _pricesSeen)
                {
                    Load();
                }
            };
            _timer.Start();
        }
    }

    public void Stop() => _timer.Stop();

    private void Load()
    {
        int generation = ++_generation;
        _pricesSeen = ExchangePrices.Version;
        if (MainWindow.Headless)
        {
            _report = JourneyData.Sample(DateTime.Now);
            _ranks = ("Templar", JourneyData.SampleRanks(DateTime.Now));
            Draw();
            return;
        }

        if (_who is not { } who)
        {
            _report = null;
            Draw();
            return;
        }

        (DateTime from, DateTime to) = JourneyData.Range(_span, DateTime.Now, who, _customFrom, _customTo);
        if (_report is null || _report.Who.Key != who.Key)
        {
            _report = null;
            Draw();
        }

        Task.Run(() => JourneyData.Build(who, from, to)).ContinueWith(t => Dispatcher.BeginInvoke(() =>
        {
            if (generation == _generation && t.Status == TaskStatus.RanToCompletion)
            {
                _report = t.Result;
                Draw();
            }
        }), TaskScheduler.Default);
        if (_ranksFor != who.Key && !PrivacyConsent.Restricted)
        {
            _ranksFor = who.Key;
            _ranks = null;
            _ranksLoading = true;
            _ = LoadRanks(who);
        }

        LoadPortrait(who);
    }

    private async Task LoadRanks(JourneyWho who)
    {
        var got = await JourneyData.RanksAsync(who);
        if (_ranksFor != who.Key)
        {
            return;
        }

        _ranks = got;
        _ranksLoading = false;
        Draw();
    }

    private void LoadPortrait(JourneyWho who)
    {
        if (_portraitFor == who.Key || !MeterSettings.Load().AutoUploadProfile)
        {
            return;
        }

        _portraitFor = who.Key;
        _portrait = null;
        string fingerprint = "aion2:" + System.Text.RegularExpressions.Regex.Replace(Aion2Servers.NameOf(who.ServerId).ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        _ = Task.Run(async () =>
        {
            string? file = await PortraitClient.GetAsync(who.Name, fingerprint);
            if (file is null)
            {
                return;
            }

            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(file, UriKind.Absolute);
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 256;
                image.EndInit();
                image.Freeze();
                _ = Dispatcher.BeginInvoke(() =>
                {
                    if (_portraitFor == who.Key)
                    {
                        _portrait = image;
                        Draw();
                    }
                });
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or ArgumentException)
            {
                // no portrait: the class's initial stands in
            }
        });
    }

    // ---- the page ----
    //
    // 6 November 2026, the owner: the first version "looks too copy pasted from that other site ... improve it". Drawn our own way since: the character on a
    // rail at the left (its emblem or portrait in its class colour, Item Level and Combat Power, the periods as a list, the card to share), and the period as a
    // story on the right: a headline, a scoreboard of six figures, the days as lines, what went well and what to work on next (each with what to do about it),
    // and the boss ranks as marks on a track. The class colour is the page's accent.

    /// <summary>The page's accent: the character's class colour (gold when the class is not known).</summary>
    private Color Accent => _who?.Class is { Length: > 0 } cls ? ClassBrushes.ClassColor(cls) : Gold;

    private void Draw()
    {
        _root.Children.Clear();
        _root.Children.Add(Head());
        if (_who is null)
        {
            _root.Children.Add(Card(Wrapped(Loc["Journey.NoCharacter"], "Brush.TextMuted", 13)));
            return;
        }

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(286) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        UIElement rail = Rail(_report);
        body.Children.Add(rail);
        var main = new StackPanel { Margin = new Thickness(18, 0, 0, 0) };
        if (_report is not { } r)
        {
            main.Children.Add(Card(Text(Loc["Journey.Loading"], "Brush.TextMuted", 13)));
        }
        else
        {
            main.Children.Add(Story(r));
            main.Children.Add(Scoreboard(r));
            main.Children.Add(Timeline(r));
            main.Children.Add(Highlights(r));
            main.Children.Add(WorkOn(r));
            if (!PrivacyConsent.Restricted || MainWindow.Headless)
            {
                main.Children.Add(Class(r));
            }
        }

        Grid.SetColumn(main, 1);
        body.Children.Add(main);
        _root.Children.Add(body);
    }

    private UIElement Head()
    {
        var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        if (_characters.Count > 1)
        {
            var pick = new ComboBox { MinWidth = 220, VerticalAlignment = VerticalAlignment.Center };
            foreach (JourneyWho w in _characters)
            {
                pick.Items.Add(w.Name + (w.Class is { } c ? " · " + c : "") + " (" + Aion2Servers.NameOf(w.ServerId) + ")");
            }

            pick.SelectedIndex = Math.Max(0, _who is null ? 0 : _characters.FindIndex(w => w.Key == _who.Key));
            pick.SelectionChanged += (_, _) =>
            {
                if (pick.SelectedIndex >= 0 && pick.SelectedIndex < _characters.Count && _characters[pick.SelectedIndex].Key != _who?.Key)
                {
                    _who = _characters[pick.SelectedIndex];
                    _report = null;
                    Load();
                }
            };
            DockPanel.SetDock(pick, Dock.Right);
            dock.Children.Add(pick);
        }

        var words = new StackPanel();
        TextBlock title = Text(Loc["Journey.Title"], "Brush.Text", 26);
        ThemeManager.UseTitleFont(title);
        words.Children.Add(title);
        TextBlock lede = Text(Loc["Journey.Lede"], "Brush.TextMuted", 13);
        lede.Margin = new Thickness(0, 4, 0, 0);
        words.Children.Add(lede);
        dock.Children.Add(words);
        return dock;
    }

    private static string Dates(DateTime from, DateTime to)
    {
        DateTime last = to.TimeOfDay == TimeSpan.Zero && to > from ? to.AddDays(-1) : to;
        string a = from.ToString("d MMM", CultureInfo.CurrentCulture), b = last.ToString("d MMM", CultureInfo.CurrentCulture);
        return from.Date == last.Date ? a : a + " – " + b;
    }

    // the headline: the clearest good news of the period
    private static string Headline(JourneyReport r)
    {
        JourneyNumbers n = r.Now, b = r.Before;
        string F(string key, object value) => string.Format(CultureInfo.CurrentCulture, Loc[key], value);
        if (n.BossDps > 0 && b.BossDps > 0 && n.BossDps >= b.BossDps * 1.03)
        {
            return F("Journey.Head.Dps", Math.Round((n.BossDps / b.BossDps - 1) * 100));
        }

        if (n.ItemLevelEnd is int end && n.ItemLevelStart is int start && end > start)
        {
            return F("Journey.Head.ItemLevel", end - start);
        }

        if (n.WorthKnown && b.LootWorth > 0 && n.LootWorth >= b.LootWorth * 1.1)
        {
            return F("Journey.Head.Loot", Math.Round((n.LootWorth / (double)b.LootWorth - 1) * 100));
        }

        return n.BossKills > 0 ? F("Journey.Head.Kills", n.BossKills) : Loc["Journey.Head.Glance"];
    }

    private static string Kinah(long kinah) => PlayerRow.Compact(kinah);

    private static SolidColorBrush Tint(Color colour, byte alpha) => new(Color.FromArgb(alpha, colour.R, colour.G, colour.B));

    /// <summary>A section of the page: a short accent stroke, its title, something on the right of it; then its content on the panel.</summary>
    private Border Section(string title, UIElement content, UIElement? right = null)
    {
        var stack = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        if (right is not null)
        {
            DockPanel.SetDock(right, Dock.Right);
            head.Children.Add(right);
        }

        var name = new StackPanel { Orientation = Orientation.Horizontal };
        name.Children.Add(new Border { Width = 3, Height = 15, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Accent), Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center });
        TextBlock words = Text(title, "Brush.Text", 15, bold: true);
        name.Children.Add(words);
        head.Children.Add(name);
        stack.Children.Add(head);
        stack.Children.Add(content);
        var card = new Border { Child = stack, Padding = new Thickness(20, 16, 20, 18), Margin = new Thickness(0, 0, 0, 14), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return card;
    }

    // ---- the rail: the character, the periods, the card to share ----

    private UIElement Rail(JourneyReport? r)
    {
        var rail = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        rail.Children.Add(Profile(r));
        rail.Children.Add(PeriodList());
        if (r is not null)
        {
            rail.Children.Add(ShareBox(r));
        }

        return rail;
    }

    /// <summary>The character: its class colour washed over the top, its portrait (else its class emblem) in a ring of it, its name, class and server, Item Level and
    /// Combat Power at the end of the period.</summary>
    private UIElement Profile(JourneyReport? r)
    {
        JourneyWho who = _who!;
        Color accent = Accent;
        var host = new Grid { ClipToBounds = true };
        var wash = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        wash.GradientStops.Add(new GradientStop(Color.FromArgb(0x58, accent.R, accent.G, accent.B), 0));
        wash.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, accent.R, accent.G, accent.B), 0.62));
        host.Children.Add(new Border { Background = wash });
        if (who.Class is { Length: > 0 } cls && MainWindow.ClassIcon(cls) is { } emblem)
        {
            // the emblem large and faint in the corner, the class's mark on the card
            host.Children.Add(new Image { Source = emblem, Width = 150, Height = 150, Opacity = 0.13, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -18, -26, 0) });
        }

        var stack = new StackPanel { Margin = new Thickness(18, 18, 18, 16) };
        var face = new Grid { Width = 78, Height = 78, HorizontalAlignment = HorizontalAlignment.Left };
        face.Children.Add(new System.Windows.Shapes.Ellipse { Stroke = new SolidColorBrush(accent), StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromRgb(0x14, 0x16, 0x1E)) });
        if (_portrait is { } portrait)
        {
            face.Children.Add(new System.Windows.Shapes.Ellipse { Margin = new Thickness(4), Fill = new ImageBrush(portrait) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Top } });
        }
        else if (who.Class is { Length: > 0 } c && MainWindow.ClassIcon(c) is { } icon)
        {
            face.Children.Add(new Image { Source = icon, Margin = new Thickness(14) });
        }
        else
        {
            face.Children.Add(new TextBlock { Text = who.Name[..1].ToUpperInvariant(), FontSize = 32, Foreground = new SolidColorBrush(accent), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        }

        stack.Children.Add(face);
        TextBlock name = Text(who.Name, "Brush.Text", 24);
        ThemeManager.UseTitleFont(name);
        name.Margin = new Thickness(0, 12, 0, 0);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        stack.Children.Add(name);
        stack.Children.Add(new TextBlock
        {
            Text = string.Join("  ·  ", new[] { who.Class, Aion2Servers.NameOf(who.ServerId) }.Where(t => t is { Length: > 0 })), FontSize = 13,
            Foreground = new SolidColorBrush(ClassBrushes.Text(who.Class) is SolidColorBrush lifted ? lifted.Color : accent), Margin = new Thickness(0, 2, 0, 14),
        });

        var stats = new Grid();
        stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        UIElement Stat(string label, string value, UIElement? delta)
        {
            var box = new StackPanel();
            box.Children.Add(Text(label, "Brush.TextSubtle", 11));
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            line.Children.Add(Text(value, "Brush.Text", 18, bold: true));
            if (delta is FrameworkElement d)
            {
                d.Margin = new Thickness(7, 3, 0, 0);
                line.Children.Add(d);
            }

            box.Children.Add(line);
            return box;
        }

        JourneyNumbers? n = r?.Now;
        stats.Children.Add(Stat(Loc["Journey.ItemLevel"], n?.ItemLevelEnd?.ToString("N0", CultureInfo.CurrentCulture) ?? "–",
            n is { ItemLevelEnd: int end } && (r!.Before.ItemLevelEnd ?? n.ItemLevelStart) is int start ? Delta(end, start, true, percent: false) : null));
        UIElement cp = Stat(Loc["Journey.Profile.Cp"], n?.CpEnd is int power ? PlayerRow.Compact(power) : "–", null);
        Grid.SetColumn(cp, 1);
        stats.Children.Add(cp);
        stack.Children.Add(stats);
        host.Children.Add(stack);
        var card = new Border { Child = host, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 14), BorderBrush = Tint(accent, 0x55) };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        host.SizeChanged += (_, e) => host.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 11, 11);
        return card;
    }

    /// <summary>The periods, one under the other; the one shown marked with the class colour. Picked dates open two date boxes under the list.</summary>
    private UIElement PeriodList()
    {
        var list = new StackPanel();
        Color accent = Accent;
        foreach ((JourneySpan span, string key) in new[]
                 {
                     (JourneySpan.Session, "Session"), (JourneySpan.Day, "Day"), (JourneySpan.Week7, "Week7"), (JourneySpan.ThisWeek, "ThisWeek"), (JourneySpan.LastWeek, "LastWeek"),
                     (JourneySpan.Days30, "Days30"), (JourneySpan.ThisMonth, "ThisMonth"), (JourneySpan.All, "All"), (JourneySpan.Custom, "Custom"),
                 })
        {
            bool on = _span == span;
            var line = new DockPanel();
            line.Children.Add(new Border { Width = 3, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 6, 10, 6), Background = on ? new SolidColorBrush(accent) : Brushes.Transparent });
            line.Children.Add(Text(Loc["Journey.Span." + key], on ? "Brush.Text" : "Brush.TextMuted", 13, bold: on));
            var item = new Border { Child = line, Height = 32, CornerRadius = new CornerRadius(7), Cursor = System.Windows.Input.Cursors.Hand, Background = on ? Tint(accent, 0x1C) : Brushes.Transparent };
            if (!on)
            {
                item.MouseEnter += (_, _) => item.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
                item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;
            }

            item.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                _span = span;
                Load();
            };
            list.Children.Add(item);
        }

        if (_span == JourneySpan.Custom)
        {
            var from = new DatePicker { SelectedDate = _customFrom ?? DateTime.Today.AddDays(-6), DisplayDateEnd = DateTime.Today, Margin = new Thickness(13, 8, 0, 4) };
            var to = new DatePicker { SelectedDate = _customTo ?? DateTime.Today, DisplayDateEnd = DateTime.Today, Margin = new Thickness(13, 0, 0, 4) };
            from.SelectedDateChanged += (_, _) =>
            {
                _customFrom = from.SelectedDate;
                Load();
            };
            to.SelectedDateChanged += (_, _) =>
            {
                _customTo = to.SelectedDate;
                Load();
            };
            list.Children.Add(from);
            list.Children.Add(to);
        }

        var card = new Border { Child = list, Padding = new Thickness(8), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 14) };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return card;
    }

    // ---- the story of the period ----

    private UIElement Story(JourneyReport r)
    {
        JourneyNumbers n = r.Now;
        Color accent = Accent;
        var host = new Grid { ClipToBounds = true, Margin = new Thickness(0, 0, 0, 14), MinHeight = 150 };
        var back = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        back.GradientStops.Add(new GradientStop(Color.FromRgb(0x12, 0x15, 0x1D), 0));
        back.GradientStops.Add(new GradientStop(Color.FromRgb(0x18, 0x1A, 0x24), 1));
        host.Children.Add(new Border { Background = back });
        var glow = new RadialGradientBrush { Center = new Point(0.92, 0.1), GradientOrigin = new Point(0.92, 0.1), RadiusX = 0.55, RadiusY = 1.2 };
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x50, accent.R, accent.G, accent.B), 0));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, accent.R, accent.G, accent.B), 1));
        host.Children.Add(new Border { Background = glow });
        host.Children.Add(new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), BorderBrush = Tint(accent, 0x45) });
        host.SizeChanged += (_, e) => host.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 12, 12);

        var grid = new Grid { Margin = new Thickness(26, 22, 22, 22) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock
        {
            Text = Dates(r.From, r.To).ToUpper(CultureInfo.CurrentCulture), FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(accent),
        });
        var headline = new TextBlock { Text = Headline(r), FontSize = 30, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 10) };
        ThemeManager.UseTitleFont(headline);
        words.Children.Add(headline);
        var facts = new List<string>();
        if (n.MinutesPlayed > 0)
        {
            facts.Add(string.Format(CultureInfo.CurrentCulture, Loc["Journey.Story.InGame"], Hours(n.MinutesPlayed)));
        }

        facts.Add(string.Format(CultureInfo.CurrentCulture, Loc["Journey.Story.Bosses"], n.BossKills));
        if (n.Dungeons > 0)
        {
            facts.Add(string.Format(CultureInfo.CurrentCulture, Loc["Journey.Story.Dungeons"], n.Dungeons));
        }

        if (n.WorthKnown && n.LootWorth > 0)
        {
            facts.Add(string.Format(CultureInfo.CurrentCulture, Loc["Journey.Story.Loot"], Kinah(n.LootWorth)));
        }

        words.Children.Add(new TextBlock { Text = string.Join("   ·   ", facts), FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(0xC2, 0xC8, 0xD2)), TextWrapping = TextWrapping.Wrap });
        grid.Children.Add(words);

        // the best drop on the right, as a medallion in its grade's colour
        if (n.BestDrop is { } drop)
        {
            var medal = new StackPanel { Width = 150, Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            UIElement icon = DropIcon(drop, 64);
            ((FrameworkElement)icon).HorizontalAlignment = HorizontalAlignment.Center;
            medal.Children.Add(icon);
            medal.Children.Add(new TextBlock { Text = Loc["Journey.BestDropTitle"].ToUpper(CultureInfo.CurrentCulture), FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xB0)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) });
            medal.Children.Add(new TextBlock { Text = drop.Name, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(LootStats.GradeColor(Math.Max(1, drop.Grade))), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center });
            if (drop.Worth > 0)
            {
                medal.Children.Add(new TextBlock { Text = Kinah(drop.Worth) + " Kinah", FontSize = 12, Foreground = new SolidColorBrush(Gold), HorizontalAlignment = HorizontalAlignment.Center });
            }

            Grid.SetColumn(medal, 1);
            grid.Children.Add(medal);
        }

        host.Children.Add(grid);
        return host;
    }

    private static UIElement DropIcon(JourneyDrop drop, double size)
    {
        Color colour = LootStats.GradeColor(Math.Max(1, drop.Grade));
        var frame = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), BorderThickness = new Thickness(size >= 48 ? 2 : 1.5), BorderBrush = new SolidColorBrush(colour),
            Background = new SolidColorBrush(Color.FromArgb(0x40, colour.R, colour.G, colour.B)), ToolTip = drop.Name,
        };
        if (drop.ItemId > 0 && OverlayPictures.Load(Aion2Artwork.ItemIconPath(drop.ItemId)) is ImageSource picture)
        {
            frame.Child = new Image { Source = picture, Margin = new Thickness(size * 0.13) };
        }
        else
        {
            frame.Child = new TextBlock { Text = "◆", FontSize = size * 0.4, Foreground = new SolidColorBrush(colour), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }

        return frame;
    }

    // ---- the scoreboard: six figures in one panel ----

    private UIElement Scoreboard(JourneyReport r)
    {
        JourneyNumbers n = r.Now, b = r.Before;
        List<JourneyDayNumbers> days = n.Days;
        var cells = new List<(string Label, string Value, UIElement? Delta, string Note, IReadOnlyList<double> Line, Color Colour)>
        {
            (Loc["Journey.Played"], Hours(n.MinutesPlayed), Delta(n.MinutesPlayed, b.MinutesPlayed, true), n.TimeKnown ? Loc["Journey.AgainstBefore"] : Loc["Journey.TimeFromFights"],
                days.Select(d => d.Played).ToList(), Color.FromRgb(0x6F, 0xB4, 0xEC)),
            (Loc["Journey.Loot"], n.WorthKnown ? Kinah(n.LootWorth) : "–", n.WorthKnown ? Delta(n.LootWorth, b.LootWorth, true) : null, n.WorthKnown ? Loc["Journey.AtMarket"] : Loc["Journey.NoPrices"],
                days.Select(d => (double)d.Worth).ToList(), Gold),
            (Loc["Journey.Bosses"], n.BossKills.ToString(CultureInfo.CurrentCulture), Delta(n.BossKills, b.BossKills, true), Loc["Journey.AgainstBefore"],
                days.Select(d => (double)d.Kills).ToList(), Boss),
            (Loc["Journey.BossDps"], n.BossDps > 0 ? PlayerRow.Compact((long)n.BossDps) + "/s" : "–", Delta(n.BossDps, b.BossDps, true), Loc["Journey.Average"],
                days.Select(d => d.BossDps).ToList(), Up),
            (Loc["Journey.Deaths"], n.Deaths.ToString(CultureInfo.CurrentCulture), Delta(n.Deaths, b.Deaths, false), Loc["Journey.InBossFights"],
                days.Select(d => (double)d.Deaths).ToList(), Down),
            (Loc["Journey.ItemLevel"], n.ItemLevelEnd?.ToString("N0", CultureInfo.CurrentCulture) ?? "–",
                n.ItemLevelEnd is int end && (b.ItemLevelEnd ?? n.ItemLevelStart) is int start ? Delta(end, start, true, percent: false) : null,
                n.CpEnd is int cp ? string.Format(CultureInfo.CurrentCulture, Loc["Journey.Cp"], cp) : Loc["Journey.AtLogins"],
                days.Select(d => (double)(d.ItemLevel ?? 0)).ToList(), Dungeon),
        };

        var grid = new Grid();
        for (int c = 0; c < 3; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < cells.Count; i++)
        {
            var (label, value, delta, note, line, colour) = cells[i];
            int column = i % 3, row = i / 3;
            var stack = new StackPanel();
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            top.Children.Add(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(colour), Margin = new Thickness(0, 1, 7, 0), VerticalAlignment = VerticalAlignment.Center });
            top.Children.Add(Text(label.ToUpper(CultureInfo.CurrentCulture), "Brush.TextMuted", 10.5, bold: true));
            stack.Children.Add(top);
            var figure = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            figure.Children.Add(Text(value, "Brush.Text", 24, bold: true));
            if (delta is FrameworkElement d)
            {
                // the change as a small pill in its colour
                var pill = new Border { Child = d, Padding = new Thickness(6, 1, 6, 2), CornerRadius = new CornerRadius(8), Margin = new Thickness(8, 4, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                pill.Background = d is TextBlock t && t.Foreground is SolidColorBrush fg ? Tint(fg.Color, 0x24) : Brushes.Transparent;
                figure.Children.Add(pill);
            }

            stack.Children.Add(figure);
            stack.Children.Add(Text(note, "Brush.TextSubtle", 11));
            UIElement spark = Sparkline(line, colour, 190, 22);
            stack.Children.Add(new Viewbox { Child = spark, Stretch = Stretch.Fill, Height = 22, Margin = new Thickness(0, 8, 0, 0) });
            var cell = new Border
            {
                Child = stack, Padding = new Thickness(18, 14, 18, 14),
                BorderThickness = new Thickness(0, 0, column < 2 ? 1 : 0, row == 0 ? 1 : 0),
            };
            cell.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            Grid.SetColumn(cell, column);
            Grid.SetRow(cell, row);
            grid.Children.Add(cell);
        }

        var panel = new Border { Child = grid, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 14) };
        panel.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        panel.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return panel;
    }

    // ---- the days as lines ----

    private UIElement Timeline(JourneyReport r)
    {
        List<JourneyDayNumbers> days = r.Now.Days.TakeLast(14).Reverse().ToList();
        bool weeks = r.Now.Days.Count >= 2 && (r.Now.Days[1].Day - r.Now.Days[0].Day).TotalDays >= 7;
        var legend = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        legend.Children.Add(Legend(Boss, Loc["Journey.Legend.Bosses"]));
        legend.Children.Add(Legend(Dungeon, Loc["Journey.Legend.Dungeons"]));
        legend.Children.Add(Legend(Other, Loc["Journey.Legend.Rest"]));
        string title = Loc[weeks ? "Journey.YourWeeks" : "Journey.YourDays"];
        if (days.Count == 0 || days.All(d => d.Played <= 0 && d.Kills == 0 && d.Worth == 0))
        {
            return Section(title, Wrapped(Loc["Journey.NoDays"], "Brush.TextMuted", 12.5), legend);
        }

        double longest = Math.Max(1, days.Max(d => Math.Max(d.Played, d.BossMinutes + d.DungeonMinutes)));
        var table = new Grid();
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        int row = 0;
        void Put(UIElement e, int column, HorizontalAlignment align = HorizontalAlignment.Left)
        {
            if (e is FrameworkElement f)
            {
                f.HorizontalAlignment = align;
                f.VerticalAlignment = VerticalAlignment.Center;
            }

            Grid.SetRow(e, row);
            Grid.SetColumn(e, column);
            table.Children.Add(e);
        }

        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Put(Text(Loc["Journey.Col.Time"], "Brush.TextSubtle", 10.5, bold: true), 2, HorizontalAlignment.Right);
        Put(Text(Loc["Journey.Col.Bosses"], "Brush.TextSubtle", 10.5, bold: true), 3, HorizontalAlignment.Right);
        Put(Text(Loc["Journey.Col.Loot"], "Brush.TextSubtle", 10.5, bold: true), 4, HorizontalAlignment.Right);
        foreach (JourneyDayNumbers d in days)
        {
            row++;
            table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            label.Children.Add(Text(weeks ? Loc["Journey.WeekShort"] : d.Day.ToString("ddd", CultureInfo.CurrentCulture), "Brush.Text", 12.5, bold: true));
            TextBlock date = Text(" " + d.Day.ToString("d MMM", CultureInfo.CurrentCulture), "Brush.TextMuted", 12);
            label.Children.Add(date);
            Put(label, 0);

            // the time of the day as one line: bosses, dungeons, the rest, as long as the longest day is the full width
            double rest = Math.Max(0, d.Played - d.BossMinutes - d.DungeonMinutes), total = d.BossMinutes + d.DungeonMinutes + rest;
            var track = new Grid { Height = 12, Margin = new Thickness(0, 0, 14, 0) };
            var under = new Border { CornerRadius = new CornerRadius(6) };
            under.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
            track.Children.Add(under);
            var parts = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
            track.SizeChanged += (_, e) => parts.Width = Math.Max(total > 0 ? 6 : 0, e.NewSize.Width * total / longest);
            double[] lengths = { d.BossMinutes, d.DungeonMinutes, rest };
            Color[] colours = { Boss, Dungeon, Other };
            int first = Array.FindIndex(lengths, v => v > 0), last = Array.FindLastIndex(lengths, v => v > 0);
            for (int i = 0; i < 3; i++)
            {
                parts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, lengths[i]), GridUnitType.Star) });
                if (lengths[i] <= 0)
                {
                    continue;
                }

                var piece = new Border { Background = new SolidColorBrush(colours[i]), CornerRadius = new CornerRadius(i == first ? 6 : 0, i == last ? 6 : 0, i == last ? 6 : 0, i == first ? 6 : 0) };
                Grid.SetColumn(piece, i);
                parts.Children.Add(piece);
            }

            track.Children.Add(parts);
            track.ToolTip = string.Format(CultureInfo.CurrentCulture, Loc["Journey.DayTip"], d.Day.ToString("dddd d MMM", CultureInfo.CurrentCulture), Hours(total));
            Put(track, 1, HorizontalAlignment.Stretch);

            Put(Text(total > 0 ? Hours(total) : "–", total > 0 ? "Brush.Text" : "Brush.TextSubtle", 12.5), 2, HorizontalAlignment.Right);
            Put(Text(d.Kills > 0 ? d.Kills.ToString(CultureInfo.CurrentCulture) : "–", d.Kills > 0 ? "Brush.Text" : "Brush.TextSubtle", 12.5, bold: d.Kills > 0), 3, HorizontalAlignment.Right);
            TextBlock loot = Text(d.Worth > 0 ? Kinah(d.Worth) : "–", d.Worth > 0 ? "Brush.Accent" : "Brush.TextSubtle", 12.5, bold: d.Worth > 0);
            Put(loot, 4, HorizontalAlignment.Right);
            if (d.Drop is { } drop)
            {
                Put(DropIcon(drop, 24), 5, HorizontalAlignment.Right);
            }
        }

        return Section(title, table, legend);
    }

    // ---- what went well, what to work on ----

    /// <summary>A finding: a stripe and a mark in its colour on a faint wash of it, a title, what it means, and (for the things to work on) what to do.</summary>
    private static UIElement Finding(string title, string detail, Color colour, string glyph, string? tip = null)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new Border
        {
            Width = 32, Height = 32, CornerRadius = new CornerRadius(9), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 12, 0), Background = Tint(colour, 0x2C),
            Child = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14, Foreground = new SolidColorBrush(colour), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        });
        var words = new StackPanel();
        words.Children.Add(Wrapped(title, "Brush.Text", 13.5, bold: true));
        if (detail.Length > 0)
        {
            TextBlock d = Wrapped(detail, "Brush.TextMuted", 12);
            d.Margin = new Thickness(0, 2, 0, 0);
            words.Children.Add(d);
        }

        if (tip is not null)
        {
            var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 6, 0, 0) };
            hint.Inlines.Add(new System.Windows.Documents.Run("→ ") { Foreground = new SolidColorBrush(colour), FontWeight = FontWeights.SemiBold });
            var text = new System.Windows.Documents.Run(tip);
            text.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Brush.Text");
            hint.Inlines.Add(text);
            words.Children.Add(hint);
        }

        Grid.SetColumn(words, 1);
        grid.Children.Add(words);
        return new Border
        {
            Child = grid, Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, 8), CornerRadius = new CornerRadius(9), Background = Tint(colour, 0x0F),
            BorderThickness = new Thickness(3, 0, 0, 0), BorderBrush = new SolidColorBrush(colour),
        };
    }

    /// <summary>Findings two to a line (one on its own when it is the last).</summary>
    private static UIElement Pairs(List<UIElement> items)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < items.Count; i++)
        {
            if (i % 2 == 0)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            if (items[i] is FrameworkElement f)
            {
                f.Margin = new Thickness(i % 2 == 0 ? 0 : 5, 0, i % 2 == 0 ? 5 : 0, 10);
            }

            Grid.SetRow(items[i], i / 2);
            Grid.SetColumn(items[i], i % 2);
            if (i == items.Count - 1 && i % 2 == 0)
            {
                Grid.SetColumnSpan(items[i], 2);
                ((FrameworkElement)items[i]).Margin = new Thickness(0, 0, 0, 10);
            }

            grid.Children.Add(items[i]);
        }

        return grid;
    }

    private UIElement Highlights(JourneyReport r)
    {
        JourneyNumbers n = r.Now, b = r.Before;
        var list = new List<UIElement>();
        if (n.BestBossDps > 0)
        {
            list.Add(Finding(string.Format(CultureInfo.CurrentCulture, Loc["Journey.Up.BestDps"], PlayerRow.Compact((long)n.BestBossDps), n.BestBossName),
                b.BestBossDps > 0 && n.BestBossDps > b.BestBossDps ? string.Format(CultureInfo.CurrentCulture, Loc["Journey.Up.BetterThan"], PlayerRow.Compact((long)b.BestBossDps)) : Loc["Journey.Up.Highest"],
                Up, ""));
        }

        if (n.Bosses.Where(x => x.AverageBefore > 0 && x.AverageDps >= x.AverageBefore * 1.03).MaxBy(x => x.AverageDps / x.AverageBefore) is { } boss)
        {
            list.Add(Finding(string.Format(CultureInfo.CurrentCulture, Loc["Journey.Up.Boss"], boss.Name, Math.Round((boss.AverageDps / boss.AverageBefore - 1) * 100)),
                string.Format(CultureInfo.CurrentCulture, Loc["Journey.Up.BossDetail"], PlayerRow.Compact((long)boss.AverageDps), PlayerRow.Compact((long)boss.AverageBefore)), Up, ""));
        }

        if (n.ItemLevelEnd is int end && n.ItemLevelStart is int start && end > start)
        {
            list.Add(Finding(string.Format(CultureInfo.CurrentCulture, Loc["Journey.Up.ItemLevel"], start, end), string.Format(CultureInfo.CurrentCulture, Loc["Journey.Up.ItemLevelGain"], end - start), Dungeon, ""));
        }

        if (n.WorthKnown && n.LootWorth > b.LootWorth && n.LootWorth > 0)
        {
            list.Add(Finding(string.Format(CultureInfo.CurrentCulture, Loc["Journey.Up.Loot"], Kinah(n.LootWorth)),
                b.LootWorth > 0 ? string.Format(CultureInfo.CurrentCulture, Loc["Journey.Up.LootBefore"], Kinah(b.LootWorth)) : Loc["Journey.Up.NothingBefore"], Gold, ""));
        }

        if (n.BestDrop is { } drop && drop.Worth > 0)
        {
            list.Add(Finding(string.Format(CultureInfo.CurrentCulture, Loc["Journey.Up.Drop"], drop.Name),
                string.Format(CultureInfo.CurrentCulture, Loc["Journey.Up.DropWorth"], Kinah(drop.Worth), drop.At.ToString("ddd d MMM", CultureInfo.CurrentCulture)), Gold, ""));
        }

        UIElement content = list.Count == 0 ? Wrapped(Loc["Journey.Up.None"], "Brush.TextMuted", 12.5) : Pairs(list);
        return Section(Loc["Journey.Highlights"], content);
    }

    private UIElement WorkOn(JourneyReport r)
    {
        JourneyNumbers n = r.Now, b = r.Before;
        var list = new List<UIElement>();
        if (n.Deaths > 0)
        {
            JourneyBoss? worst = n.Bosses.Where(x => x.Deaths > 0).MaxBy(x => x.Deaths);
            list.Add(Finding(string.Format(CultureInfo.CurrentCulture, Loc[n.Deaths == 1 ? "Journey.Down.Death" : "Journey.Down.Deaths"], n.Deaths),
                (worst is not null ? string.Format(CultureInfo.CurrentCulture, Loc["Journey.Down.DeathsMost"], worst.Name, worst.Deaths) + " · " : "")
                + (b.Deaths > 0 ? string.Format(CultureInfo.CurrentCulture, Loc["Journey.Down.DeathsBefore"], b.Deaths) : Loc["Journey.Down.NoDeathsBefore"]),
                Down, "", Loc["Journey.Tip.Deaths"]));
        }

        foreach (JourneyBoss boss in n.Bosses.Where(x => x.AverageBefore > 0 && x.AverageDps < x.AverageBefore * 0.95).OrderBy(x => x.AverageDps / x.AverageBefore).Take(2))
        {
            list.Add(Finding(string.Format(CultureInfo.CurrentCulture, Loc["Journey.Down.Dps"], boss.Name, Math.Round((1 - (boss.AverageDps / boss.AverageBefore)) * 100)),
                string.Format(CultureInfo.CurrentCulture, Loc["Journey.Down.DpsDetail"], PlayerRow.Compact((long)boss.AverageDps), PlayerRow.Compact((long)boss.AverageBefore)),
                Boss, "", Loc["Journey.Tip.Dps"]));
        }

        if (n.TimeKnown && n.MinutesPlayed >= 60 && n.MinutesIdle / n.MinutesPlayed >= 0.25)
        {
            list.Add(Finding(string.Format(CultureInfo.CurrentCulture, Loc["Journey.Down.Idle"], Hours(n.MinutesIdle)),
                string.Format(CultureInfo.CurrentCulture, Loc["Journey.Down.IdleDetail"], Math.Round(n.MinutesIdle / n.MinutesPlayed * 100)), Color.FromRgb(0x8A, 0x9A, 0xB0), "", Loc["Journey.Tip.Idle"]));
        }

        foreach (JourneyRun run in r.SlowerRuns)
        {
            list.Add(Finding(string.Format(CultureInfo.CurrentCulture, Loc["Journey.Down.Run"], run.Name, Clock(run.Seconds)),
                string.Format(CultureInfo.CurrentCulture, Loc["Journey.Down.RunDetail"], Clock(run.BestSeconds), Clock(run.Seconds - run.BestSeconds)), Dungeon, "", Loc["Journey.Tip.Run"]));
        }

        UIElement content = list.Count == 0 ? Wrapped(Loc["Journey.Down.None"], "Brush.TextMuted", 12.5) : Pairs(list);
        return Section(Loc["Journey.WorkOn"], content);
    }

    private static string Clock(double seconds)
    {
        TimeSpan t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : t.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    // ---- to share ----

    private UIElement ShareBox(JourneyReport r)
    {
        var stack = new StackPanel();
        stack.Children.Add(Text(Loc["Journey.ToShare"], "Brush.Text", 13, bold: true));
        var preview = new Viewbox { Stretch = Stretch.Uniform, Margin = new Thickness(0, 10, 0, 10), Child = ShareCard(r) };
        stack.Children.Add(preview);
        var copy = new Button { Content = Loc["Journey.Share.Copy"], Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 0, 6) };
        var save = new Button { Content = Loc["Journey.Share.Save"], Padding = new Thickness(12, 6, 12, 6) };
        TextBlock done = Text("", "Brush.Heal", 12, bold: true);
        done.Margin = new Thickness(0, 8, 0, 0);
        copy.Click += (_, _) =>
        {
            if (RenderCard(r) is { } picture)
            {
                try
                {
                    Clipboard.SetImage(picture);
                    done.Text = Loc["Journey.Share.Copied"];
                }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                    done.Text = Loc["Journey.Share.Busy"];
                }
            }
        };
        save.Click += (_, _) =>
        {
            if (RenderCard(r) is not { } picture)
            {
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = $"{r.Who.Name}-journey-{r.To:yyyy-MM-dd}.png", Filter = "PNG|*.png", DefaultExt = ".png",
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            {
                try
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(picture));
                    using FileStream file = File.Create(dialog.FileName);
                    encoder.Save(file);
                    done.Text = Loc["Journey.Share.Saved"];
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    done.Text = ex.Message;
                }
            }
        };
        stack.Children.Add(copy);
        stack.Children.Add(save);
        stack.Children.Add(done);
        var card = new Border { Child = stack, Padding = new Thickness(14), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 14) };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return card;
    }

    /// <summary>The card for Discord (600 x 315) in the class colour: the portrait (else the class emblem), the name, class and server, the period, three figures,
    /// the headline and the site.</summary>
    private FrameworkElement ShareCard(JourneyReport r)
    {
        JourneyNumbers n = r.Now;
        Color accent = Accent;
        var card = new Grid { Width = 600, Height = 315, ClipToBounds = true };
        var back = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        back.GradientStops.Add(new GradientStop(Color.FromRgb(0x0D, 0x10, 0x18), 0));
        back.GradientStops.Add(new GradientStop(Color.FromRgb(0x17, 0x18, 0x22), 1));
        card.Children.Add(new Border { Background = back, CornerRadius = new CornerRadius(16) });
        var glow = new RadialGradientBrush { Center = new Point(0.15, 0.2), GradientOrigin = new Point(0.15, 0.2), RadiusX = 0.6, RadiusY = 0.9 };
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x5A, accent.R, accent.G, accent.B), 0));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, accent.R, accent.G, accent.B), 1));
        card.Children.Add(new Border { Background = glow, CornerRadius = new CornerRadius(16) });
        if (r.Who.Class is { Length: > 0 } cls && MainWindow.ClassIcon(cls) is { } emblem)
        {
            card.Children.Add(new Image { Source = emblem, Width = 230, Height = 230, Opacity = 0.09, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -40, -50) });
        }

        card.Children.Add(new Border { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1.5), BorderBrush = Tint(accent, 0x90) });
        var grid = new Grid { Margin = new Thickness(30, 26, 30, 22) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var face = new Grid { Width = 128, Height = 128, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        face.Children.Add(new System.Windows.Shapes.Ellipse { Stroke = new SolidColorBrush(accent), StrokeThickness = 3, Fill = new SolidColorBrush(Color.FromRgb(0x12, 0x14, 0x1C)) });
        if (_portrait is { } portrait)
        {
            face.Children.Add(new System.Windows.Shapes.Ellipse { Margin = new Thickness(6), Fill = new ImageBrush(portrait) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Top } });
        }
        else if (r.Who.Class is { Length: > 0 } c && MainWindow.ClassIcon(c) is { } icon)
        {
            face.Children.Add(new Image { Source = icon, Margin = new Thickness(22) });
        }
        else
        {
            face.Children.Add(new TextBlock { Text = r.Who.Name[..1].ToUpperInvariant(), FontSize = 56, Foreground = new SolidColorBrush(accent), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        }

        grid.Children.Add(face);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = r.Who.Name, FontSize = 36, Foreground = Brushes.White };
        ThemeManager.UseTitleFont(name);
        words.Children.Add(name);
        words.Children.Add(new TextBlock
        {
            Text = string.Join("  ·  ", new[] { r.Who.Class, Aion2Servers.NameOf(r.Who.ServerId) }.Where(t => t is { Length: > 0 })), FontSize = 15,
            Foreground = new SolidColorBrush(accent), Margin = new Thickness(0, 2, 0, 0),
        });
        words.Children.Add(new TextBlock { Text = Loc["Journey.Title"].ToUpper(CultureInfo.CurrentCulture) + "  ·  " + Dates(r.From, r.To), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xB0)), Margin = new Thickness(0, 6, 0, 16) });
        var figures = new UniformGrid { Columns = 3 };
        figures.Children.Add(CardFigure(n.BestBossDps > 0 ? PlayerRow.Compact((long)n.BestBossDps) : "–", Loc["Journey.Card.BestDps"], accent));
        figures.Children.Add(CardFigure(n.BossKills.ToString(CultureInfo.CurrentCulture), Loc["Journey.Card.Bosses"], accent));
        figures.Children.Add(CardFigure(n.ItemLevelEnd?.ToString("N0", CultureInfo.CurrentCulture) ?? (n.WorthKnown ? Kinah(n.LootWorth) : "–"),
            Loc[n.ItemLevelEnd is null && n.WorthKnown ? "Journey.Card.Loot" : "Journey.Card.ItemLevel"], accent));
        words.Children.Add(figures);
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);
        var foot = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var site = new TextBlock { Text = "ATREIA.APP", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Gold), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(site, Dock.Right);
        foot.Children.Add(site);
        foot.Children.Add(new TextBlock { Text = Headline(r), FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xEA, 0xF0)), TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetRow(foot, 1);
        Grid.SetColumnSpan(foot, 2);
        grid.Children.Add(foot);
        card.Children.Add(grid);
        return card;
    }

    private static UIElement CardFigure(string value, string label, Color accent)
    {
        var stack = new StackPanel();
        stack.Children.Add(new Border { Width = 18, Height = 3, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(accent), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6) });
        stack.Children.Add(new TextBlock { Text = value, FontSize = 30, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        stack.Children.Add(new TextBlock { Text = label, FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(0xB8, 0xC0, 0xCC)) });
        return stack;
    }

    /// <summary>The share card drawn at twice its size, for the clipboard or a file.</summary>
    private BitmapSource? RenderCard(JourneyReport r)
    {
        try
        {
            FrameworkElement card = ShareCard(r);
            card.Measure(new Size(600, 315));
            card.Arrange(new Rect(0, 0, 600, 315));
            card.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1200, 630, 192, 192, PixelFormats.Pbgra32);
            bitmap.Render(card);
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or OutOfMemoryException)
        {
            return null;
        }
    }

    // ---- you and your class ----

    private UIElement Class(JourneyReport r)
    {
        var stack = new StackPanel();
        string cls = _ranks?.Class ?? r.Who.Class ?? Loc["Journey.Class.YourClass"];
        string title = string.Format(CultureInfo.CurrentCulture, Loc["Journey.Class.TitleWith"], cls);
        if (_ranksLoading && _ranks is null)
        {
            stack.Children.Add(Text(Loc["Journey.Loading"], "Brush.TextMuted", 12.5));
            return Section(title, stack);
        }

        List<JourneyRank> all = _ranks?.Ranks ?? new List<JourneyRank>();
        List<JourneyRank> now = all.Where(x => x.At >= r.From && x.At < r.To).ToList();
        TimeSpan length = r.To - r.From;
        List<JourneyRank> before = all.Where(x => x.At >= r.From - length && x.At < r.From).ToList();
        if (_ranks is null)
        {
            stack.Children.Add(Wrapped(Loc["Journey.Class.Offline"], "Brush.TextMuted", 12.5));
            return Section(title, stack);
        }

        if (now.Count == 0)
        {
            stack.Children.Add(Wrapped(string.Format(CultureInfo.CurrentCulture, Loc["Journey.Class.None"], r.Who.Name), "Brush.TextMuted", 12.5));
            return Section(title, stack);
        }

        // the period in two numbers: the average rank in the class and in the power bracket
        double? classNow = Average(now.Select(x => x.ClassPct)), classBefore = Average(before.Select(x => x.ClassPct)), bracketNow = Average(now.Select(x => x.BracketPct));
        JourneyRank? bracket = now.FirstOrDefault(x => x.BracketLo is not null);
        var summary = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
        if (classNow is double cn)
        {
            summary.Children.Add(Badge(Math.Round(cn).ToString(CultureInfo.CurrentCulture), string.Format(CultureInfo.CurrentCulture, Loc["Journey.Class.AverageIn"], cls), Pct(cn),
                classBefore is double cb ? Delta(cn, cb, true, percent: false, unit: "") : null, filled: true));
        }

        if (bracketNow is double bn && bracket is { BracketLo: long lo, BracketHi: long hi })
        {
            summary.Children.Add(Badge(Math.Round(bn).ToString(CultureInfo.CurrentCulture),
                string.Format(CultureInfo.CurrentCulture, Loc["Journey.Class.AverageBracket"], Thousands(lo), Thousands(hi)), Pct(bn), null, filled: false));
        }

        stack.Children.Add(summary);

        // each boss: its best fight of the period as two marks on one track: ● in the class, ◇ at the character's power
        foreach (JourneyRank best in now.GroupBy(x => x.Boss).Select(g => g.MaxBy(x => x.ClassPct ?? x.Dps / 1e9)!).OrderByDescending(x => x.ClassPct ?? 0).Take(8))
        {
            var line = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            who.Children.Add(Text(best.Boss, "Brush.Text", 13, bold: true));
            who.Children.Add(Text(PlayerRow.Compact((long)best.Dps) + "/s", "Brush.TextMuted", 11.5));
            line.Children.Add(who);
            UIElement track = RankTrack(best.ClassPct, best.BracketPct);
            Grid.SetColumn(track, 1);
            line.Children.Add(track);
            var numbers = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, FontSize = 13, FontWeight = FontWeights.SemiBold };
            if (best.ClassPct is double cp)
            {
                numbers.Inlines.Add(new System.Windows.Documents.Run(Math.Round(cp).ToString(CultureInfo.CurrentCulture)) { Foreground = new SolidColorBrush(Pct(cp)) });
            }

            if (best.BracketPct is double bp)
            {
                var sep = new System.Windows.Documents.Run("  ·  ");
                sep.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Brush.TextSubtle");
                numbers.Inlines.Add(sep);
                numbers.Inlines.Add(new System.Windows.Documents.Run(Math.Round(bp).ToString(CultureInfo.CurrentCulture)) { Foreground = new SolidColorBrush(Pct(bp)) });
            }

            Grid.SetColumn(numbers, 2);
            line.Children.Add(numbers);
            stack.Children.Add(line);
        }

        TextBlock note = Wrapped(Loc["Journey.Class.Note"], "Brush.TextSubtle", 11.5);
        note.Margin = new Thickness(0, 4, 0, 0);
        stack.Children.Add(note);
        var marks = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        marks.Children.Add(Text("●  " + string.Format(CultureInfo.CurrentCulture, Loc["Journey.Class.InClassShort"], cls), "Brush.TextMuted", 11.5));
        marks.Children.Add(Text("     ◇  " + Loc["Journey.Class.InBracketShort"], "Brush.TextMuted", 11.5));
        return Section(title, stack, marks);
    }

    /// <summary>A rank track 0 to 100 with its quarter marks: a dot where the class rank is, a diamond where the power-bracket rank is.</summary>
    private static UIElement RankTrack(double? classPct, double? bracketPct)
    {
        var host = new Grid { Height = 22, Margin = new Thickness(0, 0, 16, 0) };
        var canvas = new Canvas { ClipToBounds = false };
        host.Children.Add(canvas);
        host.SizeChanged += (_, e) =>
        {
            canvas.Children.Clear();
            double width = e.NewSize.Width, mid = e.NewSize.Height / 2;
            var under = new Border { Width = width, Height = 6, CornerRadius = new CornerRadius(3) };
            under.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
            Canvas.SetTop(under, mid - 3);
            canvas.Children.Add(under);
            if (classPct is double fill)
            {
                var done = new Border { Width = Math.Max(6, width * Math.Clamp(fill, 0, 100) / 100), Height = 6, CornerRadius = new CornerRadius(3), Background = Tint(Pct(fill), 0x70) };
                Canvas.SetTop(done, mid - 3);
                canvas.Children.Add(done);
            }

            foreach (double tick in new[] { 25.0, 50, 75, 95 })
            {
                var mark = new Border { Width = 1, Height = 10, Background = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)) };
                Canvas.SetLeft(mark, width * tick / 100);
                Canvas.SetTop(mark, mid - 5);
                canvas.Children.Add(mark);
            }

            if (bracketPct is double b)
            {
                var diamond = new Border { Width = 11, Height = 11, BorderThickness = new Thickness(2), BorderBrush = new SolidColorBrush(Pct(b)), Background = new SolidColorBrush(Color.FromRgb(0x14, 0x16, 0x1E)), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(45) };
                Canvas.SetLeft(diamond, width * Math.Clamp(b, 0, 100) / 100 - 5.5);
                Canvas.SetTop(diamond, mid - 5.5);
                canvas.Children.Add(diamond);
            }

            if (classPct is double c)
            {
                var dot = new System.Windows.Shapes.Ellipse { Width = 14, Height = 14, Fill = new SolidColorBrush(Pct(c)), Stroke = new SolidColorBrush(Color.FromRgb(0x14, 0x16, 0x1E)), StrokeThickness = 2 };
                Canvas.SetLeft(dot, width * Math.Clamp(c, 0, 100) / 100 - 7);
                Canvas.SetTop(dot, mid - 7);
                canvas.Children.Add(dot);
            }
        };
        return host;
    }

    private static string Thousands(long value) => (value / 1000.0).ToString("0.#", CultureInfo.CurrentCulture) + "K";

    private static double? Average(IEnumerable<double?> values)
    {
        var list = values.Where(v => v is not null).Select(v => v!.Value).ToList();
        return list.Count > 0 ? list.Average() : null;
    }

    /// <summary>The colour of a rank (percent of the players beaten): the logs' colours, gold at the top.</summary>
    private static Color Pct(double pct) => pct >= 95 ? Color.FromRgb(0xF2, 0xA6, 0x3A) : pct >= 75 ? Color.FromRgb(0xB0, 0x7C, 0xF0) : pct >= 50 ? Color.FromRgb(0x5A, 0x9C, 0xF0)
        : pct >= 25 ? Color.FromRgb(0x5F, 0xD0, 0x8A) : Color.FromRgb(0x8A, 0x93, 0xA0);

    /// <summary>A summary number: big in its rank's colour inside a ring (filled: the class; hollow: the power bracket), what it is beside it.</summary>
    private static Border Badge(string big, string label, Color colour, UIElement? delta, bool filled)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        var ring = new Grid { Width = 54, Height = 54 };
        var circle = new System.Windows.Shapes.Ellipse { Stroke = new SolidColorBrush(colour), StrokeThickness = 2.5, Fill = filled ? Tint(colour, 0x22) : Brushes.Transparent };
        if (!filled)
        {
            circle.StrokeDashArray = new DoubleCollection { 2, 1.5 };
        }

        ring.Children.Add(circle);
        ring.Children.Add(new TextBlock { Text = big, FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(colour), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        line.Children.Add(ring);
        var words = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 200 };
        words.Children.Add(Wrapped(label, "Brush.TextMuted", 12));
        if (delta is not null)
        {
            words.Children.Add(delta);
        }

        line.Children.Add(words);
        return new Border { Child = line, Margin = new Thickness(0, 0, 28, 4) };
    }
}
