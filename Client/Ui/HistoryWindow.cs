using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AionDPS.Aion2.Protocol;
using AionDPS.Combat;
using AionDPS.History;
using AionDPS.Upload;

namespace AionDPS.Ui;

/// <summary>
/// The history: every boss fight, training dummy parse and arena stay saved on this PC as it ended (see <see cref="FightLog"/>), with the time, killed or
/// not, and your DPS; a click opens the fight report. Boss fights and Nightmare have tabs of their own and are shown by day, then by visit: the bosses of
/// one dungeon run together (with a link to the whole run's report), open-world bosses together by zone. Personal bests: your best DPS per boss and per
/// dummy parse length. Chips above the tabs show one character of the roster at a time.
/// </summary>
public sealed class HistoryWindow : Window
{
    private static readonly string[] Tabs = { "Boss fights", "Nightmare", "Training dummy", "PvP", "Personal bests" };
    private const string AllCharacters = "";
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");
    private static readonly Brush GoodBack = Frozen(Color.FromArgb(0x2E, 0x5C, 0xC2, 0x7E)), GoodText = Frozen(Color.FromRgb(0x7F, 0xD8, 0x9B));
    private static readonly Brush BadBack = Frozen(Color.FromArgb(0x2E, 0xE0, 0x6A, 0x5E)), BadText = Frozen(Color.FromRgb(0xF0, 0x8E, 0x84));
    private static readonly Brush HoverBack = Frozen(Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF));

    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal };
    private readonly WrapPanel _characters = new() { Margin = new Thickness(0, 16, 0, 0) };
    private readonly StackPanel _board = new() { Margin = new Thickness(0, 14, 0, 0) };

    /// <summary>Your best DPS on each boss (killed fights; by the boss's name) for the shown character: the rows' bars and stars measure against it.</summary>
    private Dictionary<string, double> _bests = new();
    private readonly StackPanel _list = new();
    private readonly StackPanel _page = new() { Margin = new Thickness(30, 22, 30, 30), MaxWidth = 1100 };
    private string _tab = Tabs[0];
    private readonly Grid _root = new();
    private readonly ScrollViewer _listView;
    // the report open in the page (a click on a fight shows it here, in place of the list; no window of its own)
    private FightReportWindow? _reportWindow;
    private FrameworkElement? _reportView;

    /// <summary>The character picked on the chips ("" = all), kept while the app runs so the page opens on the same one.</summary>
    private static string _character = AllCharacters;

    public HistoryWindow()
    {
        Title = "History · " + Brand.AppName;
        Width = 1040;
        Height = 820;
        MinWidth = 640;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Brush.Window");
        SetResourceReference(ForegroundProperty, "Brush.Text");

        StackPanel page = _page;
        var title = new TextBlock { Text = "History", FontSize = 26, FontWeight = FontWeights.SemiBold };
        title.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Title");
        page.Children.Add(title);
        page.Children.Add(Muted("Boss fights, Nightmare, training dummy parses and PvP are saved on this PC as they end, each on its own, with every number the meter showed. Click one for its report.", 13.5, new Thickness(0, 4, 0, 0)));
        page.Children.Add(_characters);
        page.Children.Add(_board);
        var tabLine = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 18, 0, 0), Child = _tabs };
        tabLine.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        page.Children.Add(tabLine);
        _list.Margin = new Thickness(0, 10, 0, 0);
        page.Children.Add(_list);
        var folder = new TextBlock { Text = "Open the history folder", FontSize = 12.5, Margin = new Thickness(0, 14, 0, 0), Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left };
        folder.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        folder.MouseLeftButtonUp += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(FightLog.Folder);
                Process.Start(new ProcessStartInfo(FightLog.Folder) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
            {
                // nothing to open
            }
        };
        page.Children.Add(folder);
        _listView = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };
        _root.Children.Add(_listView);
        Content = _root;
        ThemedChrome.Apply(this);

        Action onChanged = () => Dispatcher.BeginInvoke(new Action(Draw));
        FightLog.Changed += onChanged;
        // ranks come from the website one fight at a time: the page is drawn again once they stop coming for a moment
        var rankWait = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        rankWait.Tick += (_, _) =>
        {
            rankWait.Stop();
            if (_reportView is null)
            {
                Draw();
            }
        };
        Action onRank = () => Dispatcher.BeginInvoke(new Action(() =>
        {
            rankWait.Stop();
            rankWait.Start();
        }));
        FightRanks.Changed += onRank;
        Closed += (_, _) =>
        {
            FightLog.Changed -= onChanged;
            FightRanks.Changed -= onRank;
            rankWait.Stop();
            CloseReport();
        };
        Draw();
    }

    /// <summary>The page itself, inside the window's scroll viewer and chrome (the picture mode takes it out and draws it).</summary>
    internal StackPanel PageContent => _page;

    public void ShowTab(string tab)
    {
        CloseReport();
        _tab = Tabs.Contains(tab) ? tab : Tabs[0];
        Draw();
    }

    private void Draw()
    {
        IReadOnlyList<FightLogEntry> everything = FightLog.WithoutSideFights(FightLog.Entries());
        DrawCharacters(everything);
        IReadOnlyList<FightLogEntry> all = _character == AllCharacters ? everything : everything.Where(e => e.You == _character).ToList();
        _bests = all.Where(e => e.Kind == FightLogKind.Boss && e.Killed == true && e.YourDps > 0).GroupBy(TitleOf).ToDictionary(g => g.Key, g => g.Max(e => e.YourDps));
        DrawBoard(all);

        _tabs.Children.Clear();
        foreach (string name in Tabs)
        {
            bool on = name == _tab;
            var label = new TextBlock { FontSize = 14, Margin = new Thickness(0, 0, 0, 9), FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal };
            label.SetResourceReference(TextBlock.ForegroundProperty, on ? "Brush.Text" : "Brush.TextMuted");
            label.Inlines.Add(new System.Windows.Documents.Run(name));
            int count = name == "Personal bests" ? 0 : all.Count(e => InTab(e, name));
            if (count > 0)
            {
                var n = new System.Windows.Documents.Run("  " + count.ToString("N0", En)) { FontSize = 12, FontWeight = FontWeights.Normal };
                n.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Brush.TextMuted");
                label.Inlines.Add(n);
            }

            var stack = new StackPanel { Margin = new Thickness(0, 0, 26, -1), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            stack.Children.Add(label);
            stack.Children.Add(new Border { Height = 2, Background = on ? Frozen(Color.FromRgb(0xDC, 0xB8, 0x77)) : Brushes.Transparent });
            string target = name;
            stack.MouseLeftButtonUp += (_, _) => ShowTab(target);
            _tabs.Children.Add(stack);
        }

        _list.Children.Clear();
        if (_tab == "Personal bests")
        {
            DrawBests(all);
            return;
        }

        var entries = all.Where(e => InTab(e, _tab)).ToList();
        if (entries.Count == 0)
        {
            string who = _character == AllCharacters ? "" : " for " + _character;
            _list.Children.Add(Card(Muted(_tab switch
            {
                "Boss fights" => $"No boss fights{who} yet. Every boss you fight, in a dungeon or in the open world, is saved here when the fight ends.",
                "Nightmare" => $"No Nightmare fights{who} yet. Every boss you fight in Nightmare is saved here when the fight ends.",
                "Training dummy" => $"No dummy parses{who} yet. Hit a Training Scarecrow or a Punching Bag: the parse starts by itself and is saved here when its time is up.",
                _ => $"No arena fights{who} yet.",
            }, 13.5, new Thickness(18, 16, 18, 16))));
            return;
        }

        if (_tab is "Boss fights" or "Nightmare")
        {
            DrawVisits(entries.Where(e => e.Kind == FightLogKind.Boss).ToList(), all.Where(e => e.Kind == FightLogKind.Run).ToList(),
                entries.Where(e => e.Kind == FightLogKind.Run).ToList());
            return;
        }

        foreach (var day in entries.GroupBy(e => e.Start.Date))
        {
            _list.Children.Add(Muted(day.Key.ToString("MMM d, yyyy", En).ToUpperInvariant(), 11.5, new Thickness(2, 16, 0, 8), semibold: true));
            var card = new StackPanel();
            bool first = true;
            foreach (FightLogEntry entry in day)
            {
                card.Children.Add(Row(entry, first, best: false));
                first = false;
            }

            _list.Children.Add(Card(card));
        }
    }

    /// <summary>Nightmare: the Nightmare zone itself and its altars.</summary>
    internal static bool IsNightmare(FightLogEntry e) =>
        e.ZoneId == 200003 || e.ZoneId is >= 910011 and <= 910016 || (e.Detail?.StartsWith("Nightmare", StringComparison.OrdinalIgnoreCase) ?? false)
        || (e.Kind == FightLogKind.Run && e.Title.StartsWith("Nightmare", StringComparison.OrdinalIgnoreCase));

    private static bool InTab(FightLogEntry e, string tab) => tab switch
    {
        "Boss fights" => e.Kind == FightLogKind.Boss && !IsNightmare(e),
        "Nightmare" => (e.Kind is FightLogKind.Boss or FightLogKind.Run) && IsNightmare(e),
        "Training dummy" => e.Kind == FightLogKind.Dummy,
        "PvP" => e.Kind == FightLogKind.Pvp,
        _ => false,
    };

    /// <summary>The characters seen in the history as chips: "All characters" and one per character, the last played first. Hidden with fewer than two.</summary>
    private void DrawCharacters(IReadOnlyList<FightLogEntry> everything)
    {
        _characters.Children.Clear();
        var roster = everything.Where(e => e.You is { Length: > 0 }).GroupBy(e => e.You!)
            .Select(g => (Name: g.Key, Class: g.First().YourClass, Last: g.Max(e => e.Start), Count: g.Count()))
            .OrderByDescending(c => c.Last).ToList();
        if (_character != AllCharacters && !roster.Any(c => c.Name == _character))
        {
            _character = AllCharacters;
        }

        _characters.Visibility = roster.Count >= 2 ? Visibility.Visible : Visibility.Collapsed;
        if (roster.Count < 2)
        {
            return;
        }

        _characters.Children.Add(Chip("All characters", null, everything.Count, AllCharacters));
        foreach (var c in roster)
        {
            _characters.Children.Add(Chip(c.Name, c.Class, c.Count, c.Name));
        }
    }

    private Border Chip(string text, string? className, int count, string value)
    {
        bool on = value == _character;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (className is { Length: > 0 } && Picture(Path.Combine(AppContext.BaseDirectory, "assets", "classes", "icons", className + ".png")) is { } icon)
        {
            row.Children.Add(new Image { Source = icon, Width = 18, Height = 18, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
        }

        var label = new TextBlock { Text = text, FontSize = 13, FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, on ? "Brush.Text" : "Brush.TextMuted");
        row.Children.Add(label);
        TextBlock n = Muted(count.ToString("N0", En), 11.5, new Thickness(7, 0, 0, 0));
        n.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(n);
        var chip = new Border
        {
            Child = row, CornerRadius = new CornerRadius(15), Padding = new Thickness(className is { Length: > 0 } ? 6 : 12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 8),
            BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
        };
        chip.SetResourceReference(Border.BackgroundProperty, on ? "Brush.ControlRaised" : "Brush.Control");
        if (on)
        {
            chip.BorderBrush = Frozen(Color.FromRgb(0xDC, 0xB8, 0x77));
        }
        else
        {
            chip.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            chip.MouseEnter += (_, _) => chip.SetResourceReference(Border.BackgroundProperty, "Brush.ControlHover");
            chip.MouseLeave += (_, _) => chip.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
        }

        chip.MouseLeftButtonUp += (_, _) =>
        {
            _character = value;
            Draw();
        };
        return chip;
    }

    /// <summary>One visit: the bosses of one dungeon run (Run set), or open-world bosses of one zone close together in time.</summary>
    private sealed record Visit(FightLogEntry? Run, string Name, List<FightLogEntry> Bosses);

    /// <summary>Boss fights by day, then by visit (newest first), each visit a card with its bosses oldest to newest. <paramref name="stays"/>: runs to
    /// show even when no boss of theirs was saved (Nightmare, where the meter often finds no boss); such a visit shows the stay itself as its row.</summary>
    private void DrawVisits(List<FightLogEntry> bosses, List<FightLogEntry> runs, List<FightLogEntry> stays)
    {
        foreach (DateTime day in bosses.Concat(stays).Select(e => e.Start.Date).Distinct().OrderByDescending(d => d))
        {
            var dayBosses = bosses.Where(e => e.Start.Date == day).ToList();
            List<Visit> visits = Visits(dayBosses, runs);
            foreach (FightLogEntry stay in stays.Where(e => e.Start.Date == day && !visits.Any(v => v.Run?.Id == e.Id)))
            {
                visits.Add(new Visit(stay, stay.Title, new List<FightLogEntry>()));
            }

            int kills = dayBosses.Count(e => e.Killed == true), wipes = dayBosses.Count - kills;
            string tally = dayBosses.Count == 0 ? Plural(visits.Count, "visit") : Plural(kills, "kill") + (wipes > 0 ? " · " + Plural(wipes, "wipe") : "");
            _list.Children.Add(Muted(day.ToString("MMM d, yyyy", En).ToUpperInvariant() + "  ·  " + tally.ToUpperInvariant(), 11.5, new Thickness(2, 18, 0, 8), semibold: true));
            foreach (Visit visit in visits.OrderByDescending(v => v.Bosses.Count > 0 ? v.Bosses.Max(b => b.Start) : v.Run!.Start))
            {
                _list.Children.Add(VisitCard(visit));
            }
        }
    }

    private static List<Visit> Visits(List<FightLogEntry> bosses, List<FightLogEntry> runs)
    {
        var visits = new List<Visit>();
        var byRun = new Dictionary<string, Visit>();
        Visit? open = null;
        DateTime openEnd = DateTime.MinValue;
        foreach (FightLogEntry boss in bosses.OrderBy(e => e.Start))
        {
            FightLogEntry? run = boss.ZoneId == 0 ? null : runs.FirstOrDefault(r => r.ZoneId == boss.ZoneId
                && boss.Start >= r.Start.AddSeconds(-15) && boss.Start <= r.Start.AddSeconds(r.Seconds + 15));
            if (run is not null)
            {
                if (!byRun.TryGetValue(run.Id, out Visit? v))
                {
                    v = new Visit(run, run.Title, new List<FightLogEntry>());
                    byRun[run.Id] = v;
                    visits.Add(v);
                }

                v.Bosses.Add(boss);
                continue;
            }

            // open world (or a run that was not saved): one visit per zone until a 20-minute break
            if (open is null || open.Bosses[0].ZoneId != boss.ZoneId || boss.Start - openEnd > TimeSpan.FromMinutes(20))
            {
                string name = (boss.ZoneId != 0 ? Aion2Zones.NameOf(boss.ZoneId, "en") : null) ?? boss.Detail ?? "Open world";
                open = new Visit(null, name, new List<FightLogEntry>());
                visits.Add(open);
            }

            open.Bosses.Add(boss);
            openEnd = boss.Start.AddSeconds(boss.Seconds);
        }

        return visits.OrderByDescending(v => v.Bosses.Max(b => b.Start)).ToList();
    }

    private Border VisitCard(Visit visit)
    {
        var card = new StackPanel();
        var head = new Grid { Margin = new Thickness(16, 11, 18, 10) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = visit.Name, FontSize = 14.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        left.Children.Add(name);
        if (visit.Bosses.Count == 0 && visit.Run is { } only)
        {
            // a stay with no boss saved: the stay itself is the visit's row
            var single = Card(Row(only, true, best: false));
            single.Margin = new Thickness(0, 0, 0, 10);
            return single;
        }

        DateTime from = visit.Run?.Start ?? visit.Bosses.Min(b => b.Start);
        DateTime to = visit.Run is { } r ? r.Start.AddSeconds(r.Seconds) : visit.Bosses.Max(b => b.Start.AddSeconds(b.Seconds));
        int kills = visit.Bosses.Count(b => b.Killed == true), wipes = visit.Bosses.Count - kills;
        string range = from.ToString("h:mm tt", En) + " – " + to.ToString("h:mm tt", En);
        left.Children.Add(Muted(range + "  ·  " + Plural(kills, "kill") + (wipes > 0 ? ", " + Plural(wipes, "wipe") : ""), 12, new Thickness(0, 2, 0, 0)));
        head.Children.Add(left);
        if (visit.Run is { } run)
        {
            var link = new TextBlock { Text = "Whole run " + run.Start.ToString("h:mm tt", En) + " ›", FontSize = 12.5, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, ToolTip = "The report of the whole run, every pull from the first hit to the last" };
            link.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
            link.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                _ = OpenAsync(run);
            };
            Grid.SetColumn(link, 1);
            head.Children.Add(link);
        }

        var headLine = new Border { Child = head, BorderThickness = new Thickness(0, 0, 0, 1) };
        headLine.SetResourceReference(Border.BackgroundProperty, "Brush.RowAlt");
        headLine.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        card.Children.Add(headLine);
        bool first = true;
        foreach (FightLogEntry boss in visit.Bosses)
        {
            card.Children.Add(Row(boss, first, best: false, inVisit: true));
            first = false;
        }

        var outer = Card(card);
        outer.Margin = new Thickness(0, 0, 0, 10);
        return outer;
    }

    private static string Plural(int n, string word) => n == 1 ? "1 " + word : $"{n} {word}s";

    /// <summary>Your best DPS per boss (killed fights) and per dummy parse length, each a card: the boss's picture in a gold ring, its name, the DPS large, when.</summary>
    private void DrawBests(IReadOnlyList<FightLogEntry> all)
    {
        var bosses = all.Where(e => e.Kind == FightLogKind.Boss && e.Killed == true && e.YourDps > 0).GroupBy(TitleOf).Select(g => g.MaxBy(e => e.YourDps)!).OrderByDescending(e => e.YourDps).ToList();
        var dummies = all.Where(e => e.Kind == FightLogKind.Dummy && e.YourDps > 0).GroupBy(e => e.Detail ?? "").Select(g => g.MaxBy(e => e.YourDps)!).OrderBy(e => e.Seconds).ToList();
        if (bosses.Count == 0 && dummies.Count == 0)
        {
            _list.Children.Add(Card(Muted("No bests yet: kill a boss or finish a dummy parse.", 13.5, new Thickness(18, 16, 18, 16))));
            return;
        }

        foreach ((string heading, List<FightLogEntry> list) in new[] { ("BOSSES", bosses), ("TRAINING DUMMY", dummies) })
        {
            if (list.Count == 0)
            {
                continue;
            }

            _list.Children.Add(Muted(heading, 11.5, new Thickness(2, 16, 0, 8), semibold: true));
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(-5, 0, -5, 0) };
            grid.SizeChanged += (_, e) =>
            {
                int columns = e.NewSize.Width < 560 ? 1 : e.NewSize.Width < 820 ? 2 : 3;
                if (grid.Columns != columns)
                {
                    grid.Columns = columns;
                }
            };
            foreach (FightLogEntry entry in list)
            {
                grid.Children.Add(Medal(entry));
            }

            _list.Children.Add(grid);
        }
    }

    private static readonly Brush Gold = Frozen(Color.FromRgb(0xE6, 0xC0, 0x68));

    /// <summary>One personal best as a card; a click opens its report.</summary>
    private FrameworkElement Medal(FightLogEntry entry)
    {
        ImageSource? icon = entry.BossNpcId is int npc ? Picture(Aion2Artwork.BossThumbPath(npc)) : null;
        icon ??= entry.YourClass is { Length: > 0 } cls ? Picture(Path.Combine(AppContext.BaseDirectory, "assets", "classes", "icons", cls + ".png")) : null;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var ring = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(28), BorderThickness = new Thickness(2), BorderBrush = Gold, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center,
            Background = icon is not null ? new ImageBrush(icon) { Stretch = Stretch.UniformToFill } : Frozen(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)) };
        grid.Children.Add(ring);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        string name = TitleOf(entry) + (entry.Kind == FightLogKind.Dummy && entry.Detail is { Length: > 0 } d ? " · " + d : "");
        words.Children.Add(new TextBlock { Text = name, FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = name });
        var figure = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 0) };
        figure.Children.Add(new TextBlock { Text = Short(entry.YourDps), FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Gold, VerticalAlignment = VerticalAlignment.Center });
        if (RankBadge(entry, 36, 22, 12.5) is { } badge)
        {
            badge.Margin = new Thickness(10, 2, 0, 0);
            figure.Children.Add(badge);
        }

        words.Children.Add(figure);
        string line = entry.Start.ToString("MMM d", En) + " · " + ReportPalette.Clock(entry.Seconds)
            + (_character == AllCharacters && _characters.Visibility == Visibility.Visible && entry.You is { Length: > 0 } you ? " · " + you : "");
        TextBlock when = Muted(line, 11.5, new Thickness(0, 1, 0, 0));
        when.TextWrapping = TextWrapping.NoWrap;
        when.TextTrimming = TextTrimming.CharacterEllipsis;
        words.Children.Add(when);
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);
        var card = new Border { Child = grid, Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(5, 0, 5, 10), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        card.MouseEnter += (_, _) => card.BorderBrush = Frozen(Color.FromArgb(0x90, 0xE6, 0xC0, 0x68));
        card.MouseLeave += (_, _) => card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        card.MouseLeftButtonUp += (_, _) => _ = OpenAsync(entry);
        return card;
    }

    /// <summary>
    /// The last 7 days of the shown character (or all): bosses killed and wipes, the time in fights, the best DPS on a boss, the bosses beaten better than ever,
    /// the dummy parses, the boss fights on the website. Nothing when there is no fight in those days.
    /// </summary>
    private void DrawBoard(IReadOnlyList<FightLogEntry> all)
    {
        _board.Children.Clear();
        DateTime since = DateTime.Now.AddDays(-7);
        var week = all.Where(e => e.Start >= since).ToList();
        var bosses = week.Where(e => e.Kind == FightLogKind.Boss).ToList();
        if (week.Count == 0)
        {
            return;
        }

        int kills = bosses.Count(e => e.Killed == true), wipes = bosses.Count - kills;
        double seconds = week.Where(e => e.Kind is FightLogKind.Boss or FightLogKind.Dummy or FightLogKind.Pvp).Sum(e => e.Seconds);
        FightLogEntry? top = bosses.Where(e => e.YourDps > 0).MaxBy(e => e.YourDps);
        int newBests = bosses.Where(e => e.Killed == true && e.YourDps > 0).GroupBy(TitleOf).Count(g => _bests.TryGetValue(g.Key, out double b) && g.Max(e => e.YourDps) >= b);
        var dummies = week.Where(e => e.Kind == FightLogKind.Dummy).ToList();
        int online = bosses.Count(e => e.Url is not null);
        foreach (FightLogEntry e in bosses)
        {
            FightRanks.Want(e);
        }

        var ranked = bosses.Select(FightRanks.Of).Where(r => r is { Ranked: true }).Select(r => r!.Percent).ToList();
        double? average = ranked.Count > 0 ? ranked.Average() : null;
        string Duration(double s) => s >= 3600 ? $"{(int)(s / 3600)} h {(int)(s % 3600 / 60)} m" : $"{Math.Max(0, (int)(s / 60))} m";
        var cells = new (string Caption, string Value, Brush? ValueBrush, string Note, Color Dot)[]
        {
            ("Bosses killed", kills.ToString("N0", En), null, wipes == 0 ? "no wipes" : Plural(wipes, "wipe"), Color.FromRgb(0x6F, 0xD6, 0x9B)),
            ("Time in fights", Duration(seconds), null, Plural(week.Count(e => e.Kind is FightLogKind.Boss or FightLogKind.Dummy or FightLogKind.Pvp), "fight"), Color.FromRgb(0x6F, 0xB4, 0xEC)),
            ("Best DPS", top is null ? "–" : Short(top.YourDps), Gold, top is null ? "no boss fight" : TitleOf(top), Color.FromRgb(0xE6, 0xC0, 0x68)),
            ("New personal bests", newBests.ToString("N0", En), null, newBests == 1 ? "boss beaten better than ever" : "bosses beaten better than ever", Color.FromRgb(0xA4, 0x8C, 0xF4)),
            ("Dummy parses", dummies.Count.ToString("N0", En), null, dummies.Count == 0 ? "none this week" : "best " + Short(dummies.Max(e => e.YourDps)), Color.FromRgb(0xE2, 0x73, 0x5C)),
            ("Average percentile", average is double avg ? RankTier.Number(avg) : "–", average is double avgTier ? RankTier.Brush(avgTier) : null,
                ranked.Count > 0 ? $"best {RankTier.Number(ranked.Max())} · {ranked.Count} ranked of {online} on the website"
                : online > 0 ? $"{online} on the website, none ranked yet" : bosses.Count == 0 ? "no boss fights" : "upload a boss fight to see it",
                Color.FromRgb(0x8C, 0x9E, 0xFF)),
        };
        _board.Children.Add(Muted("THE LAST 7 DAYS", 11.5, new Thickness(2, 0, 0, 8), semibold: true));
        var grid = new Grid();
        for (int c = 0; c < 3; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < cells.Length; i++)
        {
            var (caption, value, valueBrush, note, dot) = cells[i];
            int column = i % 3, row = i / 3;
            var stack = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = Frozen(dot), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
            TextBlock cap = Muted(caption.ToUpperInvariant(), 10.5, new Thickness(0), semibold: true);
            cap.TextWrapping = TextWrapping.NoWrap;
            head.Children.Add(cap);
            stack.Children.Add(head);
            var big = new TextBlock { Text = value, FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            if (valueBrush is not null)
            {
                big.Foreground = valueBrush;
            }

            stack.Children.Add(big);
            TextBlock small = Muted(note, 11.5, new Thickness(0, 1, 0, 0));
            small.TextWrapping = TextWrapping.NoWrap;
            small.TextTrimming = TextTrimming.CharacterEllipsis;
            small.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
            stack.Children.Add(small);
            var cell = new Border { Child = stack, Padding = new Thickness(18, 14, 18, 14), BorderThickness = new Thickness(0, 0, column < 2 ? 1 : 0, row == 0 ? 1 : 0) };
            cell.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            Grid.SetColumn(cell, column);
            Grid.SetRow(cell, row);
            grid.Children.Add(cell);
        }

        _board.Children.Add(Card(grid));
    }

    private FrameworkElement Row(FightLogEntry entry, bool first, bool best, bool inVisit = false)
    {
        var grid = new Grid { Margin = new Thickness(14, 9, 18, 9) };
        foreach (GridLength w in new[] { new GridLength(52), new GridLength(1, GridUnitType.Star), new GridLength(64), new GridLength(90), new GridLength(110) })
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        }

        ImageSource? icon = entry.BossNpcId is int npc ? Picture(Aion2Artwork.BossThumbPath(npc)) : null;
        icon ??= entry.YourClass is { Length: > 0 } cls ? Picture(Path.Combine(AppContext.BaseDirectory, "assets", "classes", "icons", cls + ".png")) : null;
        var box = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(8), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        box.Background = icon is not null ? new ImageBrush(icon) { Stretch = Stretch.UniformToFill } : Frozen(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF));
        Grid.SetColumn(box, 0);
        grid.Children.Add(box);

        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis };
        title.Inlines.Add(new System.Windows.Documents.Run(TitleOf(entry)) { FontWeight = FontWeights.SemiBold });
        if (!inVisit && entry.Detail is { Length: > 0 } detail)
        {
            var d = new System.Windows.Documents.Run(" · " + detail);
            d.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Brush.TextMuted");
            title.Inlines.Add(d);
        }

        names.Children.Add(title);
        string when = best ? entry.Start.ToString("MMM d, h:mm tt", En) : entry.Start.ToString("h:mm tt", En);
        string what = entry.Kind switch
        {
            FightLogKind.Run => entry.BossFights switch { 0 => "no boss fights", 1 => "1 boss fight", int n => $"{n} boss fights" },
            FightLogKind.Boss => entry.Killed == true ? "Killed" : "Wipe",
            _ => entry.You is { } you ? you : "",
        };
        string line = what.Length > 0 ? $"{when} · {what}" : when;
        if (_character == AllCharacters && _characters.Visibility == Visibility.Visible && entry.You is { Length: > 0 } name && entry.Kind != FightLogKind.Pvp && entry.Kind != FightLogKind.Dummy)
        {
            line += " · " + name;
        }

        names.Children.Add(Muted(line, 12, new Thickness(0, 2, 0, 0)));
        Grid.SetColumn(names, 1);
        grid.Children.Add(names);

        bool bad = entry.Kind == FightLogKind.Boss && entry.Killed != true;
        var chip = new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 2, 9, 3), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Background = bad ? BadBack : GoodBack,
            Child = new TextBlock { Text = (entry.Kind == FightLogKind.Boss ? (bad ? "\u2715 " : "\u2713 ") : "") + ReportPalette.Clock(entry.Seconds), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = bad ? BadText : GoodText },
        };
        Grid.SetColumn(chip, 3);
        grid.Children.Add(chip);
        if (RankBadge(entry, 44, 30, 15) is { } badge)
        {
            Grid.SetColumn(badge, 2);
            grid.Children.Add(badge);
        }

        var dps = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, MinWidth = 96 };
        double? bestOnBoss = !best && entry.Kind == FightLogKind.Boss && entry.YourDps > 0 && _bests.TryGetValue(TitleOf(entry), out double b) ? b : null;
        bool isBest = bestOnBoss is double top && entry.Killed == true && entry.YourDps >= top;
        var figure = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        if (isBest)
        {
            figure.Children.Add(new TextBlock { Text = "\u2605 ", FontSize = 13, Foreground = Gold, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Your best on this boss" });
        }

        var dpsText = new TextBlock { Text = Short(entry.YourDps > 0 ? entry.YourDps : entry.PartyDps), FontSize = 15, FontWeight = FontWeights.SemiBold };
        if (isBest)
        {
            dpsText.Foreground = Gold;
        }

        figure.Children.Add(dpsText);
        dps.Children.Add(figure);
        if (entry.YourDps > 0 && entry.Kind != FightLogKind.Dummy)
        {
            dps.Children.Add(Muted("party " + Short(entry.PartyDps), 11, new Thickness(0, 1, 0, 0), right: true));
        }

        if (bestOnBoss is double most && most > 0)
        {
            // how close to your best on this boss
            double share = Math.Clamp(entry.YourDps / most, 0, 1);
            var track = new Grid { Width = 96, Height = 3, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Right,
                ToolTip = isBest ? "Your best on this boss" : $"{share:P0} of your best on this boss ({Short(most)})" };
            var under = new Border { CornerRadius = new CornerRadius(2) };
            under.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
            track.Children.Add(under);
            track.Children.Add(new Border { CornerRadius = new CornerRadius(2), Width = 96 * share, HorizontalAlignment = HorizontalAlignment.Left, Background = isBest ? Gold : Frozen(Color.FromArgb(0xA0, 0xDC, 0xB8, 0x77)) });
            dps.Children.Add(track);
        }

        Grid.SetColumn(dps, 4);
        grid.Children.Add(dps);

        var row = new Border { Child = grid, Background = Brushes.Transparent, Cursor = Cursors.Hand, BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0) };
        row.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        row.MouseEnter += (_, _) => row.Background = HoverBack;
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, _) => _ = OpenAsync(entry);
        var menu = new ContextMenu();
        var open = new MenuItem { Header = "Open the report" };
        open.Click += (_, _) => _ = OpenAsync(entry);
        var delete = new MenuItem { Header = "Delete" };
        delete.Click += (_, _) => FightLog.Delete(entry);
        menu.Items.Add(open);
        if (entry.Url is { Length: > 0 } url)
        {
            var web = new MenuItem { Header = "Open on the website" };
            web.Click += (_, _) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    // no browser to open it with
                }
            };
            var copyLink = new MenuItem { Header = "Copy link" };
            copyLink.Click += (_, _) =>
            {
                try
                {
                    Clipboard.SetText(url);
                }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                    // the clipboard is busy: nothing copied
                }
            };
            menu.Items.Add(web);
            menu.Items.Add(copyLink);
        }

        menu.Items.Add(delete);
        row.ContextMenu = menu;
        return row;
    }

    /// <summary>
    /// Your rank in an uploaded, killed boss fight as the website ranks it (the percentile of your class on the boss, in the website's colour), asked of it in
    /// the background the first time; a dash when there are too few logs of the class yet; null when there is none to show (not uploaded, a wipe, not asked yet).
    /// </summary>
    private FrameworkElement? RankBadge(FightLogEntry entry, double width, double height, double size)
    {
        if (entry.Kind != FightLogKind.Boss || entry.Killed != true || entry.Url is null)
        {
            return null;
        }

        FightRanks.Want(entry);
        if (FightRanks.Of(entry) is not { } rank)
        {
            return null;
        }

        string boss = TitleOf(entry);
        if (!rank.Ranked)
        {
            TextBlock dash = Muted("–", size, new Thickness(0), semibold: true);
            dash.HorizontalAlignment = HorizontalAlignment.Center;
            dash.VerticalAlignment = VerticalAlignment.Center;
            dash.ToolTip = RankTier.Why(rank, entry.YourClass, boss);
            dash.Background = Brushes.Transparent;
            return dash;
        }

        return new Border
        {
            Width = width, Height = height, CornerRadius = new CornerRadius(height / 4), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Background = RankTier.Back(rank.Percent), BorderBrush = RankTier.Brush(rank.Percent),
            ToolTip = RankTier.Describe(rank, entry.YourClass, boss) + "\nThe website's rank: it moves as more players log the boss.",
            Child = new TextBlock
            {
                Text = RankTier.Number(rank.Percent), FontSize = size, FontWeight = FontWeights.Bold, Foreground = RankTier.Brush(rank.Percent),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    /// <summary>The entry's title; a boss the game named only by its number ("0x00005C73") gets its name from the NPC list, else "Unknown boss".</summary>
    private static string TitleOf(FightLogEntry entry) =>
        entry.Title.StartsWith("0x", StringComparison.Ordinal) && entry.Kind == FightLogKind.Boss
            ? (entry.BossNpcId is int npc ? Aion2Npcs.NameOf(npc, "en") : null) ?? "Unknown boss"
            : entry.Title;

    /// <summary>Opens a fight's report in the page (the Home page's recent fights come here too).</summary>
    public void Open(FightLogEntry entry) => _ = OpenAsync(entry);

    private async Task OpenAsync(FightLogEntry entry)
    {
        Cursor = Cursors.Wait;
        try
        {
            FightReport? report = await Task.Run(() => FightLog.Open(entry) is { } input ? FightReport.Build(input) : null);
            if (report is null)
            {
                ThemedMessageBox.Show(Window.GetWindow(_root) ?? this, "This fight's file is gone or could not be read.", "History", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ShowReport(entry, report);
        }
        finally
        {
            Cursor = null;
        }
    }

    /// <summary>The report in place of the list, with a way back; the one shown before goes. Esc or the mouse's back button return to the list too.</summary>
    private void ShowReport(FightLogEntry entry, FightReport report)
    {
        CloseReport();
        var (upload, label, url) = UploadFor(entry);
        _reportWindow = new FightReportWindow(report, url, upload, label);
        UIElement content = ThemedChrome.TakeContent(_reportWindow) ?? new Grid();

        var back = new TextBlock { Text = "‹  Back to History", FontSize = 13.5, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
        back.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        back.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            CloseReport();
        };
        var where = Muted(_tab + "  ·  " + TitleOf(entry), 12.5, new Thickness(16, 0, 0, 0));
        where.VerticalAlignment = VerticalAlignment.Center;
        where.TextWrapping = TextWrapping.NoWrap;
        where.TextTrimming = TextTrimming.CharacterEllipsis;
        var barRow = new DockPanel();
        DockPanel.SetDock(back, Dock.Left);
        barRow.Children.Add(back);
        barRow.Children.Add(where);
        var bar = new Border { Padding = new Thickness(30, 12, 30, 12), BorderThickness = new Thickness(0, 0, 0, 1), Child = barRow };
        bar.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        bar.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");

        var view = new DockPanel { Focusable = true, FocusVisualStyle = null };
        DockPanel.SetDock(bar, Dock.Top);
        view.Children.Add(bar);
        view.Children.Add(content);
        view.PreviewMouseUp += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.XButton1)
            {
                e.Handled = true;
                CloseReport();
            }
        };
        view.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CloseReport();
            }
        };

        _reportView = view;
        _listView.Visibility = Visibility.Collapsed;
        _root.Children.Add(view);
        view.Focus();
    }

    /// <summary>Back to the list (where it was scrolled to): the open report goes.</summary>
    private void CloseReport()
    {
        if (_reportView is not null)
        {
            _root.Children.Remove(_reportView);
            _reportView = null;
        }

        _reportWindow?.Close();
        _reportWindow = null;
        _listView.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// What the report offers to share: a boss fight not online yet gets "Upload"; a run uploads its boss fights that are not online yet. Once online:
    /// the page of the fight (of a run: its last uploaded boss).
    /// </summary>
    private static (Func<Task<(string? Url, string Message)>>? Upload, string? Label, string? Url) UploadFor(FightLogEntry entry)
    {
        string bar = MeterLook.From(MeterSettings.Load()).Bar;
        if (entry.Kind == FightLogKind.Boss)
        {
            if (entry.Url is null && HistoryUpload.CanUpload(entry))
            {
                return (() => HistoryUpload.SendAsync(new[] { entry }, bar), "Upload", null);
            }

            // already online: sending it again adds what each skill use did (the Rotation tab's details) to a copy uploaded before 1.19.19
            if (entry.Url is not null && HistoryUpload.CanUpload(entry))
            {
                return (() => HistoryUpload.SendAsync(new[] { entry }, bar), "Add details on the website", entry.Url);
            }

            return (null, null, entry.Url);
        }

        if (entry.Kind != FightLogKind.Run)
        {
            return (null, null, entry.Url);
        }

        var bosses = FightLog.Entries().Where(b => b.Kind == FightLogKind.Boss && b.ZoneId == entry.ZoneId && b.You == entry.You
            && b.Start >= entry.Start.AddSeconds(-15) && b.Start <= entry.Start.AddSeconds(entry.Seconds + 15)).OrderBy(b => b.Start).ToList();
        var todo = bosses.Where(b => b.Url is null && HistoryUpload.CanUpload(b)).ToList();
        string? last = bosses.LastOrDefault(b => b.Url is not null)?.Url;
        if (todo.Count == 0)
        {
            return (null, null, last);
        }

        return (() => HistoryUpload.SendAsync(todo, bar), todo.Count == 1 ? "Upload the boss fight" : $"Upload {todo.Count} boss fights", null);
    }

    private static Border Card(UIElement child)
    {
        var card = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Child = child, ClipToBounds = true };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return card;
    }

    private static TextBlock Muted(string text, double size, Thickness margin, bool semibold = false, bool right = false)
    {
        var t = new TextBlock
        {
            Text = text, FontSize = size, Margin = margin, TextWrapping = TextWrapping.Wrap, FontWeight = semibold ? FontWeights.SemiBold : FontWeights.Normal,
            HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Stretch,
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        return t;
    }

    private static string Short(double v) => v >= 1_000_000 ? (v / 1_000_000).ToString("0.0#", En) + "M" : v >= 1000 ? (v / 1000).ToString("0.0", En) + "K" : v.ToString("0", En);

    private static ImageSource? Picture(string? path) => path is not null && File.Exists(path) ? OverlayPictures.Load(path) : null;

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
