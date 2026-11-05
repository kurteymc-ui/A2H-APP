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
/// own) against the period just before: the figures with their change and a line of the days, the days as bars, what got better, what to work on, a card to
/// copy into Discord, and its uploaded boss fights ranked against its class and its power on atreia.app.
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
                if (MarketPrices.Version != _pricesSeen)
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
        _pricesSeen = MarketPrices.Version;
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

    private void Draw()
    {
        _root.Children.Clear();
        _root.Children.Add(Head());
        _root.Children.Add(Periods());
        if (_who is null)
        {
            _root.Children.Add(Card(Wrapped(Loc["Journey.NoCharacter"], "Brush.TextMuted", 13)));
            return;
        }

        if (_report is not { } r)
        {
            _root.Children.Add(Card(Text(Loc["Journey.Loading"], "Brush.TextMuted", 13)));
            return;
        }

        _root.Children.Add(Hero(r));
        _root.Children.Add(Figures(r));
        _root.Children.Add(Row(Days(r), Improved(r), 3, 2));
        _root.Children.Add(Row(ToImprove(r), Share(r), 3, 2));
        if (!PrivacyConsent.Restricted || MainWindow.Headless)
        {
            _root.Children.Add(Class(r));
        }
    }

    private UIElement Head()
    {
        var dock = new DockPanel { Margin = new Thickness(0, 0, 12, 12) };
        if (_characters.Count > 0)
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
        title.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Title");
        words.Children.Add(title);
        TextBlock lede = Text(Loc["Journey.Lede"], "Brush.TextMuted", 13);
        lede.Margin = new Thickness(0, 4, 0, 0);
        words.Children.Add(lede);
        dock.Children.Add(words);
        return dock;
    }

    private UIElement Periods()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 12, 14) };
        var chips = new WrapPanel();
        var box = new Border { Child = chips, Padding = new Thickness(4), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left };
        box.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        box.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        foreach ((JourneySpan span, string key) in new[]
                 {
                     (JourneySpan.Session, "Session"), (JourneySpan.Day, "Day"), (JourneySpan.Week7, "Week7"), (JourneySpan.ThisWeek, "ThisWeek"), (JourneySpan.LastWeek, "LastWeek"),
                     (JourneySpan.Days30, "Days30"), (JourneySpan.ThisMonth, "ThisMonth"), (JourneySpan.All, "All"), (JourneySpan.Custom, "Custom"),
                 })
        {
            chips.Children.Add(Chip(Loc["Journey.Span." + key], _span == span, () =>
            {
                _span = span;
                Load();
            }, span == JourneySpan.Custom ? "" : null));
        }

        stack.Children.Add(box);
        if (_span == JourneySpan.Custom)
        {
            var dates = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 10, 0, 0) };
            var from = new DatePicker { SelectedDate = _customFrom ?? DateTime.Today.AddDays(-6), Width = 140, DisplayDateEnd = DateTime.Today };
            var to = new DatePicker { SelectedDate = _customTo ?? DateTime.Today, Width = 140, DisplayDateEnd = DateTime.Today };
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
            dates.Children.Add(Text(Loc["Journey.From"], "Brush.TextMuted", 12.5));
            from.Margin = new Thickness(8, 0, 16, 0);
            dates.Children.Add(from);
            dates.Children.Add(Text(Loc["Journey.To"], "Brush.TextMuted", 12.5));
            to.Margin = new Thickness(8, 0, 0, 0);
            dates.Children.Add(to);
            stack.Children.Add(dates);
        }

        return stack;
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

    private UIElement Hero(JourneyReport r)
    {
        JourneyNumbers n = r.Now;
        var host = new Grid { Margin = new Thickness(0, 0, 12, 12), MinHeight = 210 };
        host.SizeChanged += (_, e) => host.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 12, 12);
        var back = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        back.GradientStops.Add(new GradientStop(Color.FromRgb(0x14, 0x17, 0x20), 0));
        back.GradientStops.Add(new GradientStop(Color.FromRgb(0x1B, 0x1A, 0x26), 1));
        host.Children.Add(new Border { Background = back });

        // the game behind, on the right, fading into the card
        string art = Path.Combine(AppContext.BaseDirectory, "assets", "setup", "game.jpg");
        if (File.Exists(art))
        {
            var picture = new Image { Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Right, Opacity = 0.55 };
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(art, UriKind.Absolute);
                bitmap.DecodePixelWidth = 1000;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                picture.Source = bitmap;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException)
            {
                picture.Source = null;
            }

            var mask = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            mask.GradientStops.Add(new GradientStop(Colors.Transparent, 0.35));
            mask.GradientStops.Add(new GradientStop(Colors.Black, 0.85));
            picture.OpacityMask = mask;
            host.Children.Add(picture);
        }

        host.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, Gold.R, Gold.G, Gold.B)),
        });
        var words = new StackPanel { Margin = new Thickness(26, 22, 26, 22), VerticalAlignment = VerticalAlignment.Center };
        var meta = new StackPanel { Orientation = Orientation.Horizontal };
        if (r.Who.Class is { Length: > 0 } cls)
        {
            meta.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 2, 8, 3), Margin = new Thickness(0, 0, 10, 0), Background = new SolidColorBrush(Color.FromArgb(0x30, Gold.R, Gold.G, Gold.B)),
                BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, Gold.R, Gold.G, Gold.B)),
                Child = new TextBlock { Text = cls.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Gold) },
            });
        }

        meta.Children.Add(new TextBlock { Text = r.Who.Name + "  ·  " + Dates(r.From, r.To), FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xCF, 0xD8)), VerticalAlignment = VerticalAlignment.Center });
        words.Children.Add(meta);
        words.Children.Add(new TextBlock
        {
            Text = Headline(r), FontSize = 28, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, MaxWidth = 720,
            Margin = new Thickness(0, 10, 0, 16), HorizontalAlignment = HorizontalAlignment.Left,
        });
        var tiles = new WrapPanel();
        if (n.BestDrop is { } drop)
        {
            tiles.Children.Add(HeroTile(DropIcon(drop, 34), drop.Name, drop.Worth > 0 ? Loc["Journey.BestDrop"] + " · " + Kinah(drop.Worth) : Loc["Journey.BestDrop"]));
        }

        if (n.BestBossDps > 0)
        {
            tiles.Children.Add(HeroTile(GlyphIcon(""), PlayerRow.Compact((long)n.BestBossDps) + "/s", string.Format(CultureInfo.CurrentCulture, Loc["Journey.BestDpsOn"], n.BestBossName)));
        }

        tiles.Children.Add(HeroTile(GlyphIcon(""), n.BossKills.ToString(CultureInfo.CurrentCulture),
            n.Dungeons > 0 ? string.Format(CultureInfo.CurrentCulture, Loc["Journey.KillsAndRuns"], n.Dungeons) : Loc["Journey.BossesDown"]));
        words.Children.Add(tiles);
        host.Children.Add(words);
        return host;
    }

    private static Border HeroTile(UIElement icon, string big, string small)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(icon);
        var words = new StackPanel { Margin = new Thickness(10, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = big, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis });
        words.Children.Add(new TextBlock { Text = small, FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(0xB8, 0xC0, 0xCC)) });
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);
        return new Border
        {
            Child = grid, Padding = new Thickness(10, 8, 12, 8), Margin = new Thickness(0, 0, 10, 0), CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0x10, 0x12, 0x19)), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
        };
    }

    private static UIElement GlyphIcon(string glyph) => new Border
    {
        Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Background = new SolidColorBrush(Color.FromArgb(0x30, Gold.R, Gold.G, Gold.B)),
        Child = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 15, Foreground = new SolidColorBrush(Gold), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
    };

    private static UIElement DropIcon(JourneyDrop drop, double size)
    {
        Color colour = LootStats.GradeColor(Math.Max(1, drop.Grade));
        var frame = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), BorderThickness = new Thickness(1.5), BorderBrush = new SolidColorBrush(colour),
            Background = new SolidColorBrush(Color.FromArgb(0x40, colour.R, colour.G, colour.B)), ToolTip = drop.Name,
        };
        if (drop.ItemId > 0 && OverlayPictures.Load(Aion2Artwork.ItemIconPath(drop.ItemId)) is ImageSource picture)
        {
            frame.Child = new Image { Source = picture, Margin = new Thickness(4) };
        }
        else
        {
            frame.Child = new TextBlock { Text = "◆", FontSize = size * 0.4, Foreground = new SolidColorBrush(colour), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }

        return frame;
    }

    private static string Kinah(long kinah) => PlayerRow.Compact(kinah);

    private UIElement Figures(JourneyReport r)
    {
        JourneyNumbers n = r.Now, b = r.Before;
        var grid = new UniformGrid { Columns = 6, Margin = new Thickness(0, 0, 2, 2) };
        List<JourneyDayNumbers> days = n.Days;
        grid.Children.Add(Figure(Loc["Journey.Played"], Hours(n.MinutesPlayed), Delta(n.MinutesPlayed, b.MinutesPlayed, true),
            n.TimeKnown ? Loc["Journey.AgainstBefore"] : Loc["Journey.TimeFromFights"], days.Select(d => d.Played).ToList(), Color.FromRgb(0x6F, 0xB4, 0xEC)));
        grid.Children.Add(Figure(Loc["Journey.Loot"], n.WorthKnown ? Kinah(n.LootWorth) : "–", n.WorthKnown ? Delta(n.LootWorth, b.LootWorth, true) : null,
            n.WorthKnown ? Loc["Journey.AtMarket"] : Loc["Journey.NoPrices"], days.Select(d => (double)d.Worth).ToList(), Gold));
        grid.Children.Add(Figure(Loc["Journey.Bosses"], n.BossKills.ToString(CultureInfo.CurrentCulture), Delta(n.BossKills, b.BossKills, true),
            Loc["Journey.AgainstBefore"], days.Select(d => (double)d.Kills).ToList(), Boss));
        grid.Children.Add(Figure(Loc["Journey.BossDps"], n.BossDps > 0 ? PlayerRow.Compact((long)n.BossDps) + "/s" : "–", Delta(n.BossDps, b.BossDps, true),
            Loc["Journey.Average"], days.Select(d => d.BossDps).ToList(), Up));
        grid.Children.Add(Figure(Loc["Journey.Deaths"], n.Deaths.ToString(CultureInfo.CurrentCulture), Delta(n.Deaths, b.Deaths, false),
            Loc["Journey.InBossFights"], days.Select(d => (double)d.Deaths).ToList(), Down));
        grid.Children.Add(Figure(Loc["Journey.ItemLevel"], n.ItemLevelEnd?.ToString("N0", CultureInfo.CurrentCulture) ?? "–",
            n.ItemLevelEnd is int end && (b.ItemLevelEnd ?? n.ItemLevelStart) is int start ? Delta(end, start, true, percent: false) : null,
            n.CpEnd is int cp ? string.Format(CultureInfo.CurrentCulture, Loc["Journey.Cp"], cp) : Loc["Journey.AtLogins"],
            days.Select(d => (double)(d.ItemLevel ?? 0)).ToList(), Dungeon));
        return grid;
    }

    private static UIElement Row(UIElement left, UIElement right, int leftStars, int rightStars)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(leftStars, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(rightStars, GridUnitType.Star) });
        if (left is FrameworkElement l)
        {
            l.VerticalAlignment = VerticalAlignment.Stretch;
        }

        if (right is FrameworkElement r)
        {
            r.VerticalAlignment = VerticalAlignment.Stretch;
        }

        Grid.SetColumn(right, 1);
        row.Children.Add(left);
        row.Children.Add(right);
        return row;
    }

    private UIElement Days(JourneyReport r)
    {
        var legend = new StackPanel { Orientation = Orientation.Horizontal };
        legend.Children.Add(Legend(Boss, Loc["Journey.Legend.Bosses"]));
        legend.Children.Add(Legend(Dungeon, Loc["Journey.Legend.Dungeons"]));
        legend.Children.Add(Legend(Other, Loc["Journey.Legend.Rest"]));
        var bars = r.Now.Days.Select(d => new DayBar(d.Day, d.BossMinutes, d.DungeonMinutes, Math.Max(0, d.Played - d.BossMinutes - d.DungeonMinutes),
            d.Worth > 0 ? Kinah(d.Worth) : null, d.Drop is { ItemId: > 0 } drop ? OverlayPictures.Load(Aion2Artwork.ItemIconPath(drop.ItemId)) : null, d.Drop?.Name)).ToList();
        UIElement content = bars.Count == 0 || bars.All(b => b.Boss + b.Dungeon + b.Other <= 0)
            ? Wrapped(Loc["Journey.NoDays"], "Brush.TextMuted", 12.5)
            : DayBars(bars.TakeLast(31).ToList());
        return Card(content, Loc[r.Now.Days.Count > 0 && r.Now.Days.Count >= 2 && (r.Now.Days[1].Day - r.Now.Days[0].Day).TotalDays >= 7 ? "Journey.WeekByWeek" : "Journey.DayByDay"], legend);
    }

    private UIElement Improved(JourneyReport r)
    {
        JourneyNumbers n = r.Now, b = r.Before;
        var list = new StackPanel();
        void Add(string title, string detail) => list.Children.Add(ListPoint(title, detail, Up, ""));
        if (n.BestDrop is { } drop)
        {
            Add(string.Format(CultureInfo.CurrentCulture, Loc["Journey.Up.Drop"], drop.Name), drop.Worth > 0 ? string.Format(CultureInfo.CurrentCulture, Loc["Journey.Up.DropWorth"], Kinah(drop.Worth), drop.At.ToString("ddd d MMM", CultureInfo.CurrentCulture))
                : drop.At.ToString("ddd d MMM", CultureInfo.CurrentCulture));
        }

        if (n.BestBossDps > 0)
        {
            Add(string.Format(CultureInfo.CurrentCulture, Loc["Journey.Up.BestDps"], PlayerRow.Compact((long)n.BestBossDps), n.BestBossName),
                b.BestBossDps > 0 && n.BestBossDps > b.BestBossDps ? string.Format(CultureInfo.CurrentCulture, Loc["Journey.Up.BetterThan"], PlayerRow.Compact((long)b.BestBossDps)) : Loc["Journey.Up.Highest"]);
        }

        if (n.WorthKnown && n.LootWorth > b.LootWorth && n.LootWorth > 0)
        {
            Add(string.Format(CultureInfo.CurrentCulture, Loc["Journey.Up.Loot"], Kinah(n.LootWorth)),
                b.LootWorth > 0 ? string.Format(CultureInfo.CurrentCulture, Loc["Journey.Up.LootBefore"], Kinah(b.LootWorth)) : Loc["Journey.Up.NothingBefore"]);
        }

        if (n.ItemLevelEnd is int end && n.ItemLevelStart is int start && end > start)
        {
            Add(string.Format(CultureInfo.CurrentCulture, Loc["Journey.Up.ItemLevel"], start, end), string.Format(CultureInfo.CurrentCulture, Loc["Journey.Up.ItemLevelGain"], end - start));
        }

        if (n.Bosses.Where(x => x.AverageBefore > 0 && x.AverageDps >= x.AverageBefore * 1.03).MaxBy(x => x.AverageDps / x.AverageBefore) is { } boss)
        {
            Add(string.Format(CultureInfo.CurrentCulture, Loc["Journey.Up.Boss"], boss.Name, Math.Round((boss.AverageDps / boss.AverageBefore - 1) * 100)),
                string.Format(CultureInfo.CurrentCulture, Loc["Journey.Up.BossDetail"], PlayerRow.Compact((long)boss.AverageDps), PlayerRow.Compact((long)boss.AverageBefore)));
        }

        if (list.Children.Count == 0)
        {
            list.Children.Add(Wrapped(Loc["Journey.Up.None"], "Brush.TextMuted", 12.5));
        }

        return Card(list, Loc["Journey.Improved"]);
    }

    private UIElement ToImprove(JourneyReport r)
    {
        JourneyNumbers n = r.Now, b = r.Before;
        var list = new StackPanel();
        void Add(string title, string detail, Color colour) => list.Children.Add(ListPoint(title, detail, colour, ""));
        if (n.Deaths > 0)
        {
            JourneyBoss? worst = n.Bosses.Where(x => x.Deaths > 0).MaxBy(x => x.Deaths);
            Add(string.Format(CultureInfo.CurrentCulture, Loc[n.Deaths == 1 ? "Journey.Down.Death" : "Journey.Down.Deaths"], n.Deaths),
                (worst is not null ? string.Format(CultureInfo.CurrentCulture, Loc["Journey.Down.DeathsMost"], worst.Name, worst.Deaths) + " · " : "")
                + (b.Deaths >= 0 ? string.Format(CultureInfo.CurrentCulture, Loc["Journey.Down.DeathsBefore"], b.Deaths) : ""), Down);
        }

        foreach (JourneyBoss boss in n.Bosses.Where(x => x.AverageBefore > 0 && x.AverageDps < x.AverageBefore * 0.95).OrderBy(x => x.AverageDps / x.AverageBefore).Take(2))
        {
            Add(string.Format(CultureInfo.CurrentCulture, Loc["Journey.Down.Dps"], boss.Name, Math.Round((1 - (boss.AverageDps / boss.AverageBefore)) * 100)),
                string.Format(CultureInfo.CurrentCulture, Loc["Journey.Down.DpsDetail"], PlayerRow.Compact((long)boss.AverageDps), PlayerRow.Compact((long)boss.AverageBefore)), JourneyViews.Boss);
        }

        if (n.TimeKnown && n.MinutesPlayed >= 60 && n.MinutesIdle / n.MinutesPlayed >= 0.25)
        {
            Add(string.Format(CultureInfo.CurrentCulture, Loc["Journey.Down.Idle"], Hours(n.MinutesIdle)),
                string.Format(CultureInfo.CurrentCulture, Loc["Journey.Down.IdleDetail"], Math.Round(n.MinutesIdle / n.MinutesPlayed * 100)), Other);
        }

        foreach (JourneyRun run in r.SlowerRuns)
        {
            Add(string.Format(CultureInfo.CurrentCulture, Loc["Journey.Down.Run"], run.Name, Clock(run.Seconds)),
                string.Format(CultureInfo.CurrentCulture, Loc["Journey.Down.RunDetail"], Clock(run.BestSeconds), Clock(run.Seconds - run.BestSeconds)), Dungeon);
        }

        if (list.Children.Count == 0)
        {
            list.Children.Add(Wrapped(Loc["Journey.Down.None"], "Brush.TextMuted", 12.5));
        }

        return Card(list, Loc["Journey.ToImprove"]);
    }

    private static string Clock(double seconds)
    {
        TimeSpan t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : t.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>A line of a list: a round mark in its colour, a title, a detail under it.</summary>
    private static UIElement ListPoint(string title, string detail, Color colour, string glyph)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(14), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 12, 0),
            Background = new SolidColorBrush(Color.FromArgb(0x28, colour.R, colour.G, colour.B)),
            Child = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12, Foreground = new SolidColorBrush(colour), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        });
        var words = new StackPanel();
        words.Children.Add(Wrapped(title, "Brush.Text", 13.5, bold: true));
        if (detail.Length > 0)
        {
            TextBlock d = Wrapped(detail, "Brush.TextMuted", 12);
            d.Margin = new Thickness(0, 2, 0, 0);
            words.Children.Add(d);
        }

        Grid.SetColumn(words, 1);
        grid.Children.Add(words);
        return grid;
    }

    // ---- to share ----

    private UIElement Share(JourneyReport r)
    {
        var stack = new StackPanel();
        var preview = new Viewbox { Stretch = Stretch.Uniform, MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Left, Child = ShareCard(r) };
        stack.Children.Add(preview);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var copy = new Button { Content = Loc["Journey.Share.Copy"], Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        var save = new Button { Content = Loc["Journey.Share.Save"], Padding = new Thickness(14, 6, 14, 6) };
        TextBlock done = Text("", "Brush.Heal", 12, bold: true);
        done.Margin = new Thickness(10, 0, 0, 0);
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
        buttons.Children.Add(copy);
        buttons.Children.Add(save);
        buttons.Children.Add(done);
        stack.Children.Add(buttons);
        return Card(stack, Loc["Journey.ToShare"]);
    }

    /// <summary>The card for Discord (600 x 315): the portrait, the name, class and server, the period, three figures, the site.</summary>
    private FrameworkElement ShareCard(JourneyReport r)
    {
        JourneyNumbers n = r.Now;
        var card = new Grid { Width = 600, Height = 315 };
        var back = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        back.GradientStops.Add(new GradientStop(Color.FromRgb(0x0C, 0x10, 0x1A), 0));
        back.GradientStops.Add(new GradientStop(Color.FromRgb(0x1C, 0x16, 0x26), 1));
        card.Children.Add(new Border { Background = back, CornerRadius = new CornerRadius(14) });
        card.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1.5), BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, Gold.R, Gold.G, Gold.B)),
        });
        var grid = new Grid { Margin = new Thickness(28, 24, 28, 22) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // the portrait (or the class's first letter)
        var face = new Border
        {
            Width = 130, Height = 170, CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Top, ClipToBounds = true,
            Background = new SolidColorBrush(Color.FromArgb(0x30, Gold.R, Gold.G, Gold.B)), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, Gold.R, Gold.G, Gold.B)),
        };
        face.Child = _portrait is { } portrait
            ? new Image { Source = portrait, Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Top }
            : new TextBlock { Text = (r.Who.Class ?? r.Who.Name)[..1].ToUpperInvariant(), FontSize = 64, Foreground = new SolidColorBrush(Gold), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(face);

        var words = new StackPanel();
        var name = new TextBlock { Text = r.Who.Name, FontSize = 30, Foreground = Brushes.White };
        name.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Title");
        words.Children.Add(name);
        words.Children.Add(new TextBlock
        {
            Text = string.Join("  ·  ", new[] { r.Who.Class, Aion2Servers.NameOf(r.Who.ServerId) }.Where(s => s is { Length: > 0 })), FontSize = 14,
            Foreground = new SolidColorBrush(Gold), Margin = new Thickness(0, 2, 0, 0),
        });
        words.Children.Add(new TextBlock { Text = Loc["Journey.Title"] + "  ·  " + Dates(r.From, r.To), FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromRgb(0xB8, 0xC0, 0xCC)), Margin = new Thickness(0, 4, 0, 16) });
        var figures = new UniformGrid { Columns = 3 };
        figures.Children.Add(CardFigure(n.BestBossDps > 0 ? PlayerRow.Compact((long)n.BestBossDps) : "–", Loc["Journey.Card.BestDps"]));
        figures.Children.Add(CardFigure(n.BossKills.ToString(CultureInfo.CurrentCulture), Loc["Journey.Card.Bosses"]));
        figures.Children.Add(CardFigure(n.ItemLevelEnd?.ToString("N0", CultureInfo.CurrentCulture) ?? (n.WorthKnown ? Kinah(n.LootWorth) : "–"),
            Loc[n.ItemLevelEnd is null && n.WorthKnown ? "Journey.Card.Loot" : "Journey.Card.ItemLevel"]));
        words.Children.Add(figures);
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);
        var foot = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var site = new TextBlock { Text = "ATREIA.APP", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Gold) };
        DockPanel.SetDock(site, Dock.Right);
        foot.Children.Add(site);
        foot.Children.Add(new TextBlock { Text = Headline(r), FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0xD6, 0xDC, 0xE4)), TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetRow(foot, 1);
        Grid.SetColumnSpan(foot, 2);
        grid.Children.Add(foot);
        card.Children.Add(grid);
        return card;
    }

    private static UIElement CardFigure(string value, string label)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = value, FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        stack.Children.Add(new TextBlock { Text = label, FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(0xB8, 0xC0, 0xCC)) });
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
        if (_ranksLoading && _ranks is null)
        {
            stack.Children.Add(Text(Loc["Journey.Loading"], "Brush.TextMuted", 12.5));
            return Card(stack, Loc["Journey.Class.Title"]);
        }

        List<JourneyRank> all = _ranks?.Ranks ?? new List<JourneyRank>();
        List<JourneyRank> now = all.Where(x => x.At >= r.From && x.At < r.To).ToList();
        TimeSpan length = r.To - r.From;
        List<JourneyRank> before = all.Where(x => x.At >= r.From - length && x.At < r.From).ToList();
        if (_ranks is null)
        {
            stack.Children.Add(Wrapped(Loc["Journey.Class.Offline"], "Brush.TextMuted", 12.5));
            return Card(stack, Loc["Journey.Class.Title"]);
        }

        if (now.Count == 0)
        {
            stack.Children.Add(Wrapped(string.Format(CultureInfo.CurrentCulture, Loc["Journey.Class.None"], r.Who.Name), "Brush.TextMuted", 12.5));
            return Card(stack, Loc["Journey.Class.Title"]);
        }

        // the period in one line: the average rank in the class and in the power bracket, against the period before
        double? classNow = Average(now.Select(x => x.ClassPct)), classBefore = Average(before.Select(x => x.ClassPct)), bracketNow = Average(now.Select(x => x.BracketPct));
        JourneyRank? bracket = now.FirstOrDefault(x => x.BracketLo is not null);
        var summary = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        if (classNow is double cn)
        {
            summary.Children.Add(Badge(Math.Round(cn).ToString(CultureInfo.CurrentCulture), string.Format(CultureInfo.CurrentCulture, Loc["Journey.Class.AverageIn"], cls), Pct(cn),
                classBefore is double cb ? Delta(cn, cb, true, percent: false, unit: "") : null));
        }

        if (bracketNow is double bn && bracket is { BracketLo: long lo, BracketHi: long hi })
        {
            summary.Children.Add(Badge(Math.Round(bn).ToString(CultureInfo.CurrentCulture),
                string.Format(CultureInfo.CurrentCulture, Loc["Journey.Class.AverageBracket"], PlayerRow.Compact(lo), PlayerRow.Compact(hi)), Pct(bn), null));
        }

        stack.Children.Add(summary);

        // each boss: the best fight of the period, its rank in the class and in the bracket
        var table = new Grid();
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        int row = 0;
        void Cell(UIElement e, int column)
        {
            Grid.SetRow(e, row);
            Grid.SetColumn(e, column);
            if (e is FrameworkElement f)
            {
                f.Margin = new Thickness(0, 6, 14, 6);
            }

            table.Children.Add(e);
        }

        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Cell(Text(Loc["Journey.Class.Boss"], "Brush.TextSubtle", 11.5, bold: true), 0);
        Cell(Text(Loc["Journey.Class.Dps"], "Brush.TextSubtle", 11.5, bold: true), 1);
        Cell(Text(string.Format(CultureInfo.CurrentCulture, Loc["Journey.Class.InClass"], cls.ToUpperInvariant()), "Brush.TextSubtle", 11.5, bold: true), 2);
        Cell(Text(Loc["Journey.Class.InBracket"], "Brush.TextSubtle", 11.5, bold: true), 3);
        foreach (JourneyRank best in now.GroupBy(x => x.Boss).Select(g => g.MaxBy(x => x.ClassPct ?? x.Dps / 1e9)!).OrderByDescending(x => x.ClassPct ?? 0).Take(8))
        {
            row++;
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Cell(Text(best.Boss, "Brush.Text", 13, bold: true), 0);
            Cell(Text(PlayerRow.Compact((long)best.Dps) + "/s", "Brush.Text", 13), 1);
            Cell(Bar(best.ClassPct, best.ClassN), 2);
            Cell(Bar(best.BracketPct, best.BracketN), 3);
        }

        stack.Children.Add(table);
        TextBlock note = Wrapped(Loc["Journey.Class.Note"], "Brush.TextSubtle", 11.5);
        note.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(note);
        return Card(stack, string.Format(CultureInfo.CurrentCulture, Loc["Journey.Class.TitleWith"], cls));
    }

    private static double? Average(IEnumerable<double?> values)
    {
        var list = values.Where(v => v is not null).Select(v => v!.Value).ToList();
        return list.Count > 0 ? list.Average() : null;
    }

    /// <summary>The colour of a rank (percent of the players beaten): the logs' colours, gold at the top.</summary>
    private static Color Pct(double pct) => pct >= 95 ? Color.FromRgb(0xF2, 0xA6, 0x3A) : pct >= 75 ? Color.FromRgb(0xB0, 0x7C, 0xF0) : pct >= 50 ? Color.FromRgb(0x5A, 0x9C, 0xF0)
        : pct >= 25 ? Color.FromRgb(0x5F, 0xD0, 0x8A) : Color.FromRgb(0x8A, 0x93, 0xA0);

    private static UIElement Bar(double? pct, int? n)
    {
        if (pct is not double p)
        {
            return Text("–", "Brush.TextSubtle", 12.5);
        }

        Color colour = Pct(p);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var track = new Grid { Height = 8, VerticalAlignment = VerticalAlignment.Center };
        var back = new Border { CornerRadius = new CornerRadius(4) };
        back.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
        track.Children.Add(back);
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Clamp(p, 1, 100), GridUnitType.Star) });
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Clamp(100 - p, 0, 99), GridUnitType.Star) });
        Grid.SetColumnSpan(back, 2);
        track.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(colour) });
        grid.Children.Add(track);
        var label = new TextBlock
        {
            Text = Math.Round(p).ToString(CultureInfo.CurrentCulture) + (n is int count ? $"  /{count}" : ""), FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(colour), Margin = new Thickness(10, 0, 0, 0), MinWidth = 54,
        };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        return grid;
    }

    private static Border Badge(string big, string label, Color colour, UIElement? delta)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(new TextBlock { Text = big, FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(colour), VerticalAlignment = VerticalAlignment.Center });
        var words = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(Text(label, "Brush.TextMuted", 12));
        if (delta is not null)
        {
            words.Children.Add(delta);
        }

        line.Children.Add(words);
        var box = new Border { Child = line, Padding = new Thickness(14, 8, 16, 8), Margin = new Thickness(0, 0, 10, 0), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) };
        box.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
        box.BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, colour.R, colour.G, colour.B));
        return box;
    }
}
