using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AionDPS.Aion2;
using AionDPS.Upload;

namespace AionDPS.Ui;

/// <summary>
/// The loot tool's page, four tabs (underlined, as on the History page):
/// <list type="bullet">
/// <item>Overview: the loot over all time, from the monsters' drop tables that outlive the sessions: kills, items, gold gear and all gear with how many kills one
/// took, the drops by grade, the monsters gold gear came from, what was picked up most.</item>
/// <item>Areas: every map looted on, on the left under its kind (open world, Abyss, dungeons), the one played last first in each (going back to a map carries
/// on its entry); the chosen one on the right: its numbers as tiles, what it is worth at the own server's market prices (<see cref="MarketPrices"/>), what
/// dropped there over every visit, and the monsters killed there with their best drop.</item>
/// <item>Drop rates: every monster killed, searchable, with each drop as "1 in N kills" and a bar.</item>
/// <item>Hidden Cubes: the cubes looted, what they gave as "1 in N cubes", where they were found and the last ones; cube loot is in no other tab.</item>
/// </list>
/// Gear of one grade is one line everywhere ("Gold gear ×2", its pieces in the tooltip): which piece dropped is luck, that a gold piece dropped is what counts.
/// The log lives only on this computer.
/// </summary>
public sealed class LootPage : Grid
{
    private enum Tab
    {
        Overview,
        Sessions,
        Rates,
        Cubes,
    }

    private static readonly Color GoldColor = Color.FromRgb(0xDC, 0xB8, 0x77);

    private readonly StackPanel _tabStrip = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _overview = new();
    private readonly Grid _sessionsView = new();
    private readonly StackPanel _sessions = new();
    private readonly StackPanel _detail = new();
    private readonly StackPanel _ratesView = new();
    private readonly StackPanel _rates = new();
    private readonly TextBlock _ratesSummary = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
    private readonly TextBox _search = new();
    private readonly StackPanel _cubes = new();
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private Tab _tab = Tab.Overview;
    private string _tabsShown = "", _overviewShown = "", _shown = "", _ratesShown = "", _cubesShown = "";
    private List<LootSession> _list = new();
    private string? _selectedId;
    private bool _consent;
    private long _consentAt = long.MinValue;

    /// <summary>The privacy policy accepted (read from the settings at most every ten seconds: the page refreshes several times a second).</summary>
    private bool Consent()
    {
        long tick = Environment.TickCount64;
        if (tick - _consentAt > 10_000)
        {
            _consentAt = tick;
            _consent = PrivacyConsent.CanUpload;
        }

        return _consent;
    }

    public LootPage()
    {
        MinHeight = 380;
        MaxWidth = 1180;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        // the tabs, and on their right the reset of everything (the owner, 4 November 2026: a button at the top, with a warning before)
        var reset = FlatButton(LocalizationManager.Instance["Loot.ResetAll"], danger: true, ResetAll, new Thickness(12, 0, 0, 8));
        reset.ToolTip = LocalizationManager.Instance["Loot.ResetAll.Tip"];
        var tabRow = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(reset, Dock.Right);
        tabRow.Children.Add(reset);
        tabRow.Children.Add(_tabStrip);
        var tabLine = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 2, 0, 16), Child = tabRow };
        tabLine.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        Children.Add(tabLine);

        foreach (FrameworkElement view in new FrameworkElement[] { _overview, _sessionsView, _ratesView, _cubes })
        {
            SetRow(view, 1);
            Children.Add(view);
        }

        BuildSessionsView();
        BuildRatesView();
        _refresh.Tick += (_, _) => Refresh();
        Loaded += (_, _) => _refresh.Start();
        Unloaded += (_, _) => _refresh.Stop();
        ShowTab(Tab.Overview);
    }

    /// <summary>Opens the Sessions tab (the headless picture of the page).</summary>
    internal void ShowSessionsTab() => ShowTab(Tab.Sessions);

    /// <summary>Opens the drop rates tab (the headless picture of the page).</summary>
    internal void ShowRatesTab() => ShowTab(Tab.Rates);

    /// <summary>Opens the Hidden Cubes tab (the headless picture of the page).</summary>
    internal void ShowCubesTab() => ShowTab(Tab.Cubes);

    public void Stop() => _refresh.Stop();

    private void ShowTab(Tab tab)
    {
        _tab = tab;
        _overview.Visibility = tab == Tab.Overview ? Visibility.Visible : Visibility.Collapsed;
        _sessionsView.Visibility = tab == Tab.Sessions ? Visibility.Visible : Visibility.Collapsed;
        _ratesView.Visibility = tab == Tab.Rates ? Visibility.Visible : Visibility.Collapsed;
        _cubes.Visibility = tab == Tab.Cubes ? Visibility.Visible : Visibility.Collapsed;
        _tabsShown = _overviewShown = _shown = _ratesShown = _cubesShown = "";
        Refresh();
    }

    private void Refresh()
    {
        DrawTabs();
        switch (_tab)
        {
            case Tab.Overview:
                RefreshOverview();
                break;
            case Tab.Sessions:
                RefreshSessions();
                break;
            case Tab.Cubes:
                RefreshCubes();
                break;
            default:
                RefreshRates();
                break;
        }
    }

    // ---------- the tab strip ----------

    private void DrawTabs()
    {
        var loc = LocalizationManager.Instance;
        string key = $"{_tab}/{loc.Language}";
        if (key == _tabsShown)
        {
            return;
        }

        _tabsShown = key;
        _tabStrip.Children.Clear();
        foreach ((Tab tab, string name) in new[] { (Tab.Overview, loc["Loot.Tab.Overview"]), (Tab.Sessions, loc["Loot.Tab.Areas"]), (Tab.Rates, loc["Loot.Tab.Rates"]),
                     (Tab.Cubes, loc["Loot.Tab.Cubes"]) })
        {
            bool on = tab == _tab;
            var label = new TextBlock { Text = name, FontSize = 14, Margin = new Thickness(0, 0, 0, 9), FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal };
            label.SetResourceReference(TextBlock.ForegroundProperty, on ? "Brush.Text" : "Brush.TextMuted");
            var stack = new StackPanel { Margin = new Thickness(0, 0, 26, -1), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            stack.Children.Add(label);
            stack.Children.Add(new Border { Height = 2, CornerRadius = new CornerRadius(1), Background = on ? Frozen(GoldColor) : Brushes.Transparent });
            stack.MouseLeftButtonUp += (_, _) => ShowTab(tab);
            _tabStrip.Children.Add(stack);
        }
    }

    // ---------- Overview: all time ----------

    private void RefreshOverview()
    {
        var loc = LocalizationManager.Instance;
        IReadOnlyList<LootMob> mobs = Aion2Loot.Mobs;
        string key = string.Join("|", mobs.Select(m => $"{m.NpcId}:{m.Kills}:{m.Items.Sum(i => i.Count)}")) + "/" + loc.Language + "/" + ActualWidth.ToString("0");
        if (key == _overviewShown)
        {
            return;
        }

        _overviewShown = key;
        _overview.Children.Clear();
        LootAllTime all = LootStats.AllTime(mobs);
        if (all.Kills == 0 && all.Items == 0)
        {
            _overview.Children.Add(EmptyCard(loc["Loot.RatesEmpty"]));
            return;
        }

        var tiles = new UniformGrid { Columns = 4, Margin = new Thickness(-5, 0, -5, 0) };
        tiles.Children.Add(Tile(loc["Loot.Tile.Kills"], all.Kills.ToString("N0"), string.Format(loc["Loot.Tile.KillsSub"], all.MobKinds.ToString("N0")), null));
        tiles.Children.Add(Tile(loc["Loot.Tile.Items"], all.Items.ToString("N0"), string.Format(loc["Loot.Tile.ItemsSub"], all.ItemKinds.ToString("N0")), null));
        tiles.Children.Add(Tile(loc["Loot.Tile.Gold"], all.GoldGear.ToString("N0"),
            all.GoldGear == 0 ? loc["Loot.Tile.None"] : LootStats.OneIn(all.GoldKills, all.Kills), LootStats.GradeColor(LootStats.Gold)));
        tiles.Children.Add(Tile(loc["Loot.Tile.Gear"], all.GearDrops.ToString("N0"),
            all.GearDrops == 0 ? loc["Loot.Tile.None"] : LootStats.OneIn(all.GearKills, all.Kills), null));
        _overview.Children.Add(tiles);

        // the grades beside the gold spots (one under the other on a narrow page)
        bool wide = ActualWidth <= 0 || ActualWidth >= 760;
        var pair = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (wide)
        {
            pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        else
        {
            pair.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            pair.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        FrameworkElement grades = GradesCard(all);
        FrameworkElement spots = GoldSpotsCard(all, mobs);
        pair.Children.Add(grades);
        if (wide)
        {
            SetColumn(spots, 2);
        }
        else
        {
            SetRow(spots, 1);
            spots.Margin = new Thickness(0, 12, 0, 0);
        }

        pair.Children.Add(spots);
        _overview.Children.Add(pair);

        if (all.Top.Count > 0)
        {
            var top = new StackPanel();
            int i = 0;
            foreach ((LootLine line, int npc) in all.Top)
            {
                top.Children.Add(ItemRow(line, i++ > 0, sub: npc > 0 ? string.Format(loc["Loot.From"], LootText.MobName(npc)) : LootText.MobName(0),
                    rate: null, worth: null));
            }

            _overview.Children.Add(Section(loc["Loot.TopDrops"], top, new Thickness(0, 12, 0, 0)));
        }

        _overview.Children.Add(Note(loc["Loot.AllTime.Note"], new Thickness(2, 12, 0, 4)));
    }

    /// <summary>Each grade's drops: a dot and its name, a bar for its share of the drops, the count, how much of it gear and how many kills one took.</summary>
    private static FrameworkElement GradesCard(LootAllTime all)
    {
        var loc = LocalizationManager.Instance;
        var rows = new StackPanel { Margin = new Thickness(16, 6, 16, 12) };
        long most = Math.Max(1, all.Grades.Count == 0 ? 1 : all.Grades.Max(g => g.Drops));
        foreach (LootGradeRow g in all.Grades)
        {
            Color color = LootStats.GradeColor(g.Grade);
            var row = new Grid { Margin = new Thickness(0, 7, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            name.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = Frozen(color), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            name.Children.Add(new TextBlock { Text = LootStats.GradeName(g.Grade), FontSize = 13, Foreground = Frozen(color), FontWeight = FontWeights.SemiBold });
            row.Children.Add(name);

            var middle = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 12, 0) };
            middle.Children.Add(Bar(Math.Sqrt((double)g.Drops / most), color, 6));
            string detail = string.Join("  ·  ", new[] { g.Gear > 0 ? string.Format(loc["Loot.ByGrade.Gear"], g.Gear.ToString("N0")) : null, LootStats.OneIn(g.WithKills, all.Kills) }
                .Where(s => !string.IsNullOrEmpty(s)));
            if (detail.Length > 0)
            {
                middle.Children.Add(Muted(detail, 11.5, new Thickness(0, 3, 0, 0)));
            }

            SetColumn(middle, 1);
            row.Children.Add(middle);
            var count = Strong(g.Drops.ToString("N0"), 14);
            count.HorizontalAlignment = HorizontalAlignment.Right;
            count.VerticalAlignment = VerticalAlignment.Center;
            SetColumn(count, 2);
            row.Children.Add(count);
            rows.Children.Add(row);
        }

        return Section(loc["Loot.ByGrade"], rows, new Thickness(0));
    }

    /// <summary>The monsters gold gear came from, the best rate first; before the first gold piece the most killed monsters, and a line on why.</summary>
    private static FrameworkElement GoldSpotsCard(LootAllTime all, IReadOnlyList<LootMob> mobs)
    {
        var loc = LocalizationManager.Instance;
        var rows = new StackPanel();
        Color gold = LootStats.GradeColor(LootStats.Gold);
        if (all.GoldSpots.Count == 0)
        {
            rows.Children.Add(Muted(loc["Loot.GoldSpots.Empty"], 12, new Thickness(16, 10, 16, 6)));
            var killed = mobs.Where(m => m.NpcId > 0 && m.Kills > 0).OrderByDescending(m => m.Kills).Take(4).ToList();
            if (killed.Count > 0)
            {
                rows.Children.Add(Caps(loc["Loot.MostKilled"], new Thickness(16, 8, 16, 2)));
                int i = 0;
                foreach (LootMob mob in killed)
                {
                    rows.Children.Add(MobRow(mob.NpcId, mob.Zones, i++ > 0, LootText.Kills(mob.Kills), null, null));
                }
            }
        }
        else
        {
            int i = 0;
            foreach (LootGoldSpot spot in all.GoldSpots.Take(6))
            {
                rows.Children.Add(MobRow(spot.NpcId, spot.Zones, i++ > 0, LootStats.OneIn(spot.GoldKills, spot.Kills),
                    $"{LootStats.GearName(LootStats.Gold)} ×{spot.Gold:N0}  ·  {LootText.Kills(spot.Kills)}", gold));
            }
        }

        rows.Children.Add(new Border { Height = 8 });
        return Section(loc["Loot.GoldSpots"], rows, new Thickness(0));
    }

    /// <summary>A monster's line: its name and maps, a figure on the right (in gold for a gold rate) with a line under it.</summary>
    private static FrameworkElement MobRow(int npcId, IReadOnlyList<int> zones, bool separator, string figure, string? under, Color? figureColor)
    {
        var row = new DockPanel { Margin = new Thickness(16, 0, 16, 0) };
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var big = new TextBlock { Text = figure, FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
        if (figureColor is Color c)
        {
            big.Foreground = Frozen(c);
        }
        else
        {
            big.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        }

        right.Children.Add(big);
        if (under is not null)
        {
            var small = Muted(under, 11.5, new Thickness(0, 1, 0, 0));
            small.HorizontalAlignment = HorizontalAlignment.Right;
            small.TextWrapping = TextWrapping.NoWrap;
            right.Children.Add(small);
        }

        DockPanel.SetDock(right, Dock.Right);
        row.Children.Add(right);
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = Strong(LootText.MobName(npcId), 13);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        left.Children.Add(name);
        if (zones.Count > 0)
        {
            var where = Muted(string.Join(", ", zones.Take(3).Select(LootText.MapName)), 11.5, new Thickness(0, 1, 0, 0));
            where.TextWrapping = TextWrapping.NoWrap;
            where.TextTrimming = TextTrimming.CharacterEllipsis;
            left.Children.Add(where);
        }

        row.Children.Add(left);
        return Separated(row, separator, new Thickness(0, 8, 0, 8));
    }

    // ---------- Sessions ----------

    private void BuildSessionsView()
    {
        _sessionsView.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(236) });
        _sessionsView.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        _sessionsView.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var listCard = Card(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _sessions, MaxHeight = 620, Padding = new Thickness(6) });
        listCard.VerticalAlignment = VerticalAlignment.Top;
        _sessionsView.Children.Add(listCard);
        SetColumn(_detail, 2);
        _sessionsView.Children.Add(_detail);
    }

    private LootSession? Selected() => _list.FirstOrDefault(s => s.Id == _selectedId);

    private void RefreshSessions()
    {
        var loc = LocalizationManager.Instance;
        _list = Aion2Loot.Sessions.ToList();
        int server = OwnServer();
        bool ask = Consent() && MarketPrices.SignedIn;
        MarketBook? book = MarketPrices.For(server, ask);
        string key = string.Join("|", _list.Select(s => $"{s.Id}:{s.End.Ticks}:{s.Maps.Sum(m => m.Items.Sum(i => i.Picks))}:{s.Maps.Sum(m => m.Kills)}")) + "/" + _selectedId + "/" + loc.Language
            + "/" + server + ":" + MarketPrices.Version + ":" + ask;
        if (key == _shown)
        {
            return;
        }

        _shown = key;
        if (_selectedId is null || _list.All(s => s.Id != _selectedId))
        {
            _selectedId = _list.FirstOrDefault()?.Id;
        }

        DrawSessionList();
        _detail.Children.Clear();
        if (Selected() is not { } session)
        {
            _detail.Children.Add(EmptyCard(loc["Loot.Empty"]));
            return;
        }

        DrawSession(session, book, server, ask);
    }

    /// <summary>The kinds of area, in the order the list shows them.</summary>
    private enum AreaKind
    {
        Open,
        Abyss,
        Instance,
        Unknown,
    }

    /// <summary>An open map, a layer of the Abyss (Lower, Middle, Upper Reshanta), a dungeon or other instance, or a map the game has not named yet.</summary>
    private static AreaKind KindOf(int zone) =>
        zone <= 0 ? AreaKind.Unknown : zone is 20 or 22 or 23 ? AreaKind.Abyss : Aion2.Protocol.Aion2Zones.IsInstance(zone) ? AreaKind.Instance : AreaKind.Open;

    /// <summary>Every map looted on, under its kind of area (open world, Abyss, dungeons), the one played last first in each: one entry per map over every visit,
    /// not per day.</summary>
    private void DrawSessionList()
    {
        var loc = LocalizationManager.Instance;
        _sessions.Children.Clear();
        if (_list.Count == 0)
        {
            _sessions.Children.Add(Muted(loc["Loot.Tab.Areas"], 12, new Thickness(8, 6, 8, 6)));
            return;
        }

        foreach (var kind in _list.GroupBy(x => KindOf(ZoneOf(x))).OrderBy(g => g.Key))
        {
            _sessions.Children.Add(Caps(loc["Loot.Area." + kind.Key], new Thickness(8, _sessions.Children.Count == 0 ? 4 : 12, 8, 4)));
            foreach (LootSession s in kind.OrderByDescending(x => x.End))
            {
                _sessions.Children.Add(SessionEntry(s));
            }
        }
    }

    private static string DayName(DateTime at)
    {
        var loc = LocalizationManager.Instance;
        DateTime today = DateTime.Now.Date;
        return at.Date == today ? loc["Loot.Today"] : at.Date == today.AddDays(-1) ? loc["Loot.Yesterday"] : at.ToString("ddd, MMM d");
    }

    private static int ZoneOf(LootSession s) => s.Maps.Count > 0 ? s.Maps[0].Zone : 0;

    /// <summary>A map's entry: its name, the time looted there, items and kills, a gold mark when gold gear dropped.</summary>
    private FrameworkElement SessionEntry(LootSession s)
    {
        var loc = LocalizationManager.Instance;
        bool selected = s.Id == _selectedId;
        long items = s.Maps.Sum(m => m.Items.Sum(i => i.Count));
        int kills = s.Maps.Sum(m => m.Kills);
        long gold = s.Maps.Sum(m => m.Items.Where(i => LootStats.IsGear(i.ItemId) && LootStats.GradeOf(i.ItemId) == LootStats.Gold).Sum(i => i.Count));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var name = Strong(LootText.MapName(ZoneOf(s)), 13);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        grid.Children.Add(name);
        var length = Muted(Minutes(TimeSpan.FromSeconds(s.ActiveSeconds)), 11.5, new Thickness(8, 0, 0, 0));
        length.VerticalAlignment = VerticalAlignment.Center;
        length.TextWrapping = TextWrapping.NoWrap;
        length.ToolTip = loc["Loot.TimeLootedTip"];
        SetColumn(length, 1);
        grid.Children.Add(length);
        var detail = Muted(string.Format(loc["Loot.Items"], items.ToString("N0")) + (kills > 0 ? "  ·  " + LootText.Kills(kills) : "") + "  ·  " + DayName(s.End), 11.5, new Thickness(0, 2, 0, 0));
        detail.TextWrapping = TextWrapping.NoWrap;
        detail.TextTrimming = TextTrimming.CharacterEllipsis;
        SetRow(detail, 1);
        grid.Children.Add(detail);
        if (gold > 0)
        {
            var mark = Pill("◆ " + gold.ToString("N0"), LootStats.GradeColor(LootStats.Gold));
            mark.ToolTip = $"{LootStats.GearName(LootStats.Gold)} ×{gold:N0}";
            mark.Margin = new Thickness(8, 2, 0, 0);
            SetRow(mark, 1);
            SetColumn(mark, 1);
            grid.Children.Add(mark);
        }

        var inner = new Border { Child = grid, Padding = new Thickness(selected ? 9 : 12, 7, 10, 7) };
        var entry = new Border
        {
            Child = inner, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 1, 0, 1), Cursor = Cursors.Hand,
            BorderThickness = new Thickness(selected ? 3 : 0, 0, 0, 0), BorderBrush = Frozen(GoldColor),
        };
        if (selected)
        {
            entry.SetResourceReference(Border.BackgroundProperty, "Brush.ControlHoverStrong");
        }
        else
        {
            entry.Background = Brushes.Transparent;
            entry.MouseEnter += (_, _) => entry.SetResourceReference(Border.BackgroundProperty, "Brush.ControlHover");
            entry.MouseLeave += (_, _) => entry.Background = Brushes.Transparent;
        }

        string id = s.Id;
        entry.MouseLeftButtonUp += (_, _) =>
        {
            _selectedId = id;
            _shown = "";
            Refresh();
        };
        return entry;
    }

    private static string Minutes(TimeSpan span) => span.TotalMinutes < 1 ? "<1 min" : span.TotalHours >= 1 ? $"{(int)span.TotalHours} h {span.Minutes:00} min" : $"{(int)span.TotalMinutes} min";

    /// <summary>A map's session: its name, the time looted there and the visits, first and last time there; the tiles, the worth, then what dropped there with
    /// how many kills each took; the delete buttons at the bottom. Every visit to the map adds to this one session.</summary>
    private void DrawSession(LootSession session, MarketBook? book, int server, bool ask)
    {
        var loc = LocalizationManager.Instance;
        LootMap? map = session.Maps.FirstOrDefault();
        List<LootLine> lines = LootStats.Lines(session.Maps.SelectMany(m => m.Items)).OrderByDescending(l => l.Last).ToList();
        long items = lines.Sum(l => l.Count);
        int kills = session.Maps.Sum(m => m.Kills);
        LootLine? gold = lines.FirstOrDefault(l => l.Gear && l.Grade == LootStats.Gold);
        TimeSpan looted = TimeSpan.FromSeconds(session.ActiveSeconds);

        var head = new StackPanel { Margin = new Thickness(2, 0, 0, 12) };
        head.Children.Add(Strong(LootText.MapName(ZoneOf(session)), 17));
        string visits = session.Visits == 1 ? loc["Loot.Visit.One"] : string.Format(loc["Loot.Visits"], Math.Max(1, session.Visits).ToString("N0"));
        head.Children.Add(Muted($"{Minutes(looted)} {loc["Loot.Looted"]}  ·  {visits}  ·  {string.Format(loc["Loot.Since"], DayName(session.Start) + " " + session.Start.ToString("t"))}"
            + $"  ·  {string.Format(loc["Loot.LastHere"], DayName(session.End) + " " + session.End.ToString("t"))}", 12, new Thickness(0, 2, 0, 0)));
        _detail.Children.Add(head);

        (long worth, string? worthNote) = Worth(session, book, server, ask);
        var tiles = new UniformGrid { Columns = worth > 0 ? 4 : 3, Margin = new Thickness(-5, 0, -5, 0) };
        tiles.Children.Add(Tile(loc["Loot.Tile.Items"], items.ToString("N0"), string.Format(loc["Loot.Tile.ItemsSub"], LootText.Merge(session.Maps).Count.ToString("N0")), null));
        tiles.Children.Add(Tile(loc["Loot.Tile.Kills"], kills.ToString("N0"), looted.TotalMinutes >= 5 && kills > 0
            ? string.Format(loc["Loot.PerHour"], ((long)(kills / looted.TotalHours)).ToString("N0")) : null, null));
        tiles.Children.Add(Tile(loc["Loot.Tile.Gold"], (gold?.Count ?? 0).ToString("N0"),
            gold is null ? loc["Loot.Tile.None"] : LootStats.OneIn(gold.WithKills, kills), LootStats.GradeColor(LootStats.Gold)));
        if (worth > 0 && book is not null)
        {
            var tile = Tile(loc["Loot.Tile.Worth"], LootPageText.Short(worth),
                looted >= TimeSpan.FromMinutes(5) ? string.Format(loc["Loot.Tile.PerHour"], LootPageText.Short((long)(worth / looted.TotalHours))) : string.Format(loc["Loot.Tile.Kinah"], book.Name), null);
            tile.ToolTip = string.Format(loc["Loot.Worth"], worth.ToString("N0"), book.Name) + "\n" + string.Format(loc["Loot.WorthTip"], book.Name)
                + (book.Captured is DateTime at ? " " + at.ToLocalTime().ToString("g") : "");
            tiles.Children.Add(tile);
        }

        _detail.Children.Add(tiles);
        if (worthNote is not null)
        {
            _detail.Children.Add(Note(worthNote, new Thickness(2, 8, 0, 0)));
        }

        // what dropped on this map over every visit, the latest pickup first
        var rows = new StackPanel();
        if (lines.Count == 0)
        {
            rows.Children.Add(Muted(loc["Loot.NoDropYet"], 12, new Thickness(16, 10, 16, 10)));
        }

        int i = 0;
        foreach (LootLine line in lines)
        {
            rows.Children.Add(ItemRow(line, i++ > 0, sub: null, rate: (line.WithKills, kills), worth: book));
        }

        _detail.Children.Add(Section(loc["Loot.Drops"], rows, new Thickness(0, 12, 0, 0), null, kills > 0 ? LootText.Kills(kills) : null));
        if (map is { MobKills.Count: > 0 } && MobsHere(map) is { } mobsHere)
        {
            _detail.Children.Add(mobsHere);
        }

        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 4) };
        bar.Children.Add(FlatButton(loc["Loot.DeleteSession"], danger: true, () =>
        {
            if (Ask(loc["Loot.DeleteSession.Confirm"]))
            {
                Aion2Loot.Clear(session);
            }
        }));
        bar.Children.Add(FlatButton(loc["Loot.DeleteAll"], danger: true, () =>
        {
            if (Ask(loc["Loot.DeleteAll.Confirm"]))
            {
                Aion2Loot.Clear();
            }
        }, new Thickness(8, 0, 0, 0)));
        _detail.Children.Add(bar);
    }

    /// <summary>The monsters killed in the area, the most killed first: their kills there and, from their all-time drop table, the drop they give most often.</summary>
    private static FrameworkElement? MobsHere(LootMap map)
    {
        var loc = LocalizationManager.Instance;
        var tables = Aion2Loot.Mobs.GroupBy(m => m.NpcId).ToDictionary(g => g.Key, g => g.First());
        var rows = new StackPanel();
        int i = 0;
        foreach ((int npc, int n) in map.MobKills.Where(m => m.Key > 0 && m.Value > 0).OrderByDescending(m => m.Value).Take(12))
        {
            string? under = null;
            if (tables.TryGetValue(npc, out LootMob? mob) && mob.Kills > 0 && LootStats.Lines(mob).FirstOrDefault() is { } best)
            {
                under = best.Name + "  ·  " + LootStats.OneIn(best.WithKills, mob.Kills);
            }

            rows.Children.Add(MobRow(npc, Array.Empty<int>(), i++ > 0, LootText.Kills(n), under ?? loc["Loot.NoDropYet"], null));
        }

        return i == 0 ? null : Section(loc["Loot.MobsHere"], rows, new Thickness(0, 12, 0, 0), loc["Loot.MobsHere.Note"]);
    }

    /// <summary>The session's worth at the own server's prices, or 0 and the line that says why there is none (not signed in, loading, no prices).</summary>
    private static (long Worth, string? Note) Worth(LootSession session, MarketBook? book, int server, bool ask)
    {
        var loc = LocalizationManager.Instance;
        if (server <= 0)
        {
            return (0, null);
        }

        if (!ask && book is null)
        {
            return (0, loc["Loot.WorthSignIn"]);
        }

        if (book is null)
        {
            return (0, MarketPrices.IsLoading(server) ? loc["Loot.WorthLoading"] : loc["Loot.WorthNone"]);
        }

        var (worth, _, unpriced) = MarketPrices.Worth(LootText.Merge(session.Maps).Select(i => (i.ItemId, i.Count)), book);
        return (worth, unpriced > 0 ? string.Format(loc["Loot.WorthUnpriced"], unpriced) : null);
    }

    /// <summary>The server whose prices count: the character logged in now, else the one played last.</summary>
    private static int OwnServer()
    {
        var characters = Aion2Tickets.Snapshot();
        string? now = Aion2Tickets.CurrentName;
        return (characters.FirstOrDefault(c => c.Name == now && c.ServerId > 0) ?? characters.Where(c => c.ServerId > 0).OrderByDescending(c => c.SavedAt).FirstOrDefault())?.ServerId ?? 0;
    }

    // ---------- Drop rates ----------

    private void BuildRatesView()
    {
        var loc = LocalizationManager.Instance;
        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var reset = FlatButton(loc["Loot.ResetRates"], danger: true, () =>
        {
            if (Ask(loc["Loot.ResetRates.Confirm"]))
            {
                Aion2Loot.ClearMobs();
            }
        });
        DockPanel.SetDock(reset, Dock.Right);
        toolbar.Children.Add(reset);

        var searchBox = new Grid { Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
        _search.Padding = new Thickness(30, 6, 10, 6);
        _search.BorderThickness = new Thickness(1);
        _search.FontSize = 13;
        _search.VerticalContentAlignment = VerticalAlignment.Center;
        _search.SetResourceReference(Control.BackgroundProperty, "Brush.Control");
        _search.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");
        _search.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        _search.SetResourceReference(TextBoxBase.CaretBrushProperty, "Brush.Text");
        var placeholder = Muted(loc["Loot.Search"], 13, new Thickness(31, 0, 0, 0));
        placeholder.VerticalAlignment = VerticalAlignment.Center;
        placeholder.IsHitTestVisible = false;
        placeholder.TextWrapping = TextWrapping.NoWrap;
        var lens = Muted("⌕", 15, new Thickness(10, 0, 0, 1));
        lens.VerticalAlignment = VerticalAlignment.Center;
        lens.IsHitTestVisible = false;
        _search.TextChanged += (_, _) =>
        {
            placeholder.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _ratesShown = "";
            RefreshRates();
        };
        searchBox.Children.Add(_search);
        searchBox.Children.Add(placeholder);
        searchBox.Children.Add(lens);
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(searchBox);
        _ratesSummary.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        left.Children.Add(_ratesSummary);
        toolbar.Children.Add(left);
        _ratesView.Children.Add(toolbar);

        var hint = Note(loc["Loot.RatesShort"], new Thickness(2, 4, 0, 2));
        hint.ToolTip = loc["Loot.RatesHint"];
        _ratesView.Children.Add(hint);
        _ratesView.Children.Add(_rates);
    }

    /// <summary>Every monster killed, the most killed first, as a card: its name and maps, its kills, its gold gear rate when it dropped any, and each drop with
    /// "1 in N kills", a bar and the count. What came from elsewhere (chests, gathering) last, with its counts only. The search keeps the monsters whose name
    /// matches with all their drops, and of the others the drops whose name matches.</summary>
    private void RefreshRates()
    {
        var loc = LocalizationManager.Instance;
        IReadOnlyList<LootMob> mobs = Aion2Loot.Mobs;
        string query = _search.Text.Trim();
        string key = string.Join("|", mobs.Select(m => $"{m.NpcId}:{m.Kills}:{m.Items.Sum(i => i.Count)}:{m.Items.Sum(i => i.Kills)}")) + "/" + loc.Language + "/" + query;
        if (key == _ratesShown)
        {
            return;
        }

        _ratesShown = key;
        _rates.Children.Clear();
        List<LootMob> killed = mobs.Where(m => m.NpcId > 0 && m.Kills > 0).ToList();
        _ratesSummary.Text = killed.Count == 0 ? "" : string.Format(loc["Loot.RatesTotal"], killed.Count.ToString("N0"), killed.Sum(m => m.Kills).ToString("N0"));
        if (mobs.Count == 0)
        {
            _rates.Children.Add(EmptyCard(loc["Loot.RatesEmpty"]));
            return;
        }

        bool Matches(string text) => query.Length == 0 || text.Contains(query, StringComparison.CurrentCultureIgnoreCase);
        int shown = 0;
        foreach (LootMob mob in mobs.OrderBy(m => m.NpcId == 0 ? 1 : 0).ThenByDescending(m => m.Kills).ThenByDescending(m => m.Last))
        {
            string name = LootText.MobName(mob.NpcId);
            List<LootLine> lines = LootStats.Lines(mob);
            if (!Matches(name))
            {
                lines = lines.Where(l => Matches(l.Name) || l.Members.Any(m => Matches(LootText.ItemName(m.ItemId)))).ToList();
                if (lines.Count == 0)
                {
                    continue;
                }
            }

            var rows = new StackPanel();
            if (lines.Count == 0)
            {
                rows.Children.Add(Muted(loc["Loot.NoDropYet"], 12, new Thickness(16, 10, 16, 10)));
            }

            int i = 0;
            foreach (LootLine line in lines)
            {
                rows.Children.Add(ItemRow(line, i++ > 0, sub: null, rate: mob.NpcId > 0 ? (line.WithKills, mob.Kills) : null, worth: null));
            }

            string? where = mob.Zones.Count > 0 ? string.Join(", ", mob.Zones.Take(3).Select(LootText.MapName)) : null;
            // a monster that dropped gold gear says how often, in gold beside its kills
            LootLine? gold = mob.NpcId > 0 ? LootStats.Lines(mob).FirstOrDefault(l => l.Gear && l.Grade == LootStats.Gold) : null;
            Border? badge = gold is null ? null : Pill("◆ " + LootStats.OneIn(gold.WithKills, mob.Kills), LootStats.GradeColor(LootStats.Gold));
            if (badge is not null)
            {
                badge.ToolTip = $"{LootStats.GearName(LootStats.Gold)} ×{gold!.Count:N0}";
            }

            FrameworkElement card = Section(name, rows, new Thickness(0, shown == 0 ? 8 : 12, 0, 0), where, mob.NpcId > 0 ? LootText.Kills(mob.Kills) : null, badge);
            _rates.Children.Add(card);
            shown++;
        }

        if (shown == 0)
        {
            _rates.Children.Add(EmptyCard(loc["Loot.NoMatch"]));
        }
    }

    // ---------- Hidden Cubes ----------

    /// <summary>The Hidden Cubes looted: tiles (how many, the items, the last one), what they gave with "1 in N cubes", where they were found, the last ten
    /// cubes with their items, and the delete button. Cube loot is only here, never in the areas or the drop rates.</summary>
    private void RefreshCubes()
    {
        var loc = LocalizationManager.Instance;
        IReadOnlyList<LootCube> cubes = Aion2Loot.Cubes;
        string key = cubes.Count + ":" + (cubes.Count > 0 ? cubes[0].At.Ticks + ":" + cubes[0].Items.Sum(i => i.Count) : "") + "/" + loc.Language;
        if (key == _cubesShown)
        {
            return;
        }

        _cubesShown = key;
        _cubes.Children.Clear();
        if (cubes.Count == 0)
        {
            _cubes.Children.Add(EmptyCard(loc["Loot.Cubes.Empty"]));
            return;
        }

        List<LootLine> lines = LootStats.Lines(cubes.SelectMany(c => c.Items.Select(i => (i.ItemId, i.Count, Kills: 1, Last: c.At))))
            .OrderByDescending(l => l.WithKills).ThenByDescending(l => l.Count).ToList();
        int maps = cubes.Where(c => c.Zone > 0).Select(c => c.Zone).Distinct().Count();
        long items = cubes.Sum(c => c.Items.Sum(i => i.Count));
        LootCube last = cubes[0];
        var tiles = new UniformGrid { Columns = 3, Margin = new Thickness(-5, 0, -5, 0) };
        tiles.Children.Add(Tile(loc["Loot.Cubes.Tile.Opened"], cubes.Count.ToString("N0"), maps == 0 ? null : maps == 1 ? loc["Loot.Cubes.Tile.Map"] : string.Format(loc["Loot.Cubes.Tile.Maps"], maps), GoldColor));
        tiles.Children.Add(Tile(loc["Loot.Tile.Items"], items.ToString("N0"), string.Format(loc["Loot.Tile.ItemsSub"], cubes.SelectMany(c => c.Items).Select(i => i.ItemId).Distinct().Count().ToString("N0")), null));
        tiles.Children.Add(Tile(loc["Loot.Cubes.Tile.Last"], DayName(last.At) + " " + last.At.ToString("t"), LootText.MapName(last.Zone), null));
        _cubes.Children.Add(tiles);

        var rows = new StackPanel();
        int i = 0;
        foreach (LootLine line in lines)
        {
            rows.Children.Add(ItemRow(line, i++ > 0, sub: null, rate: (line.WithKills, cubes.Count), worth: null, perCube: true));
        }

        _cubes.Children.Add(Section(loc["Loot.Cubes.Gave"], rows, new Thickness(0, 12, 0, 0), null, CubeCount(cubes.Count)));

        // where they were found, beside the last cubes (one under the other on a narrow page)
        bool wide = ActualWidth <= 0 || ActualWidth >= 760;
        var pair = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (wide)
        {
            pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        }
        else
        {
            pair.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            pair.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        var where = new StackPanel();
        int w = 0;
        foreach (var zone in cubes.GroupBy(c => c.Zone).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Max(c => c.At)))
        {
            var row = new DockPanel { Margin = new Thickness(16, 0, 16, 0) };
            var count = Strong(CubeCount(zone.Count()), 13);
            count.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(count, Dock.Right);
            row.Children.Add(count);
            var name = new StackPanel();
            name.Children.Add(Strong(LootText.MapName(zone.Key), 13));
            name.Children.Add(Muted(string.Format(loc["Loot.LastHere"], DayName(zone.Max(c => c.At))), 11.5, new Thickness(0, 1, 0, 0)));
            row.Children.Add(name);
            where.Children.Add(Separated(row, w++ > 0, new Thickness(0, 8, 0, 8)));
        }

        var recent = new StackPanel();
        int r = 0;
        foreach (LootCube cube in cubes.Take(10))
        {
            var row = new StackPanel { Margin = new Thickness(16, 0, 16, 0) };
            row.Children.Add(Strong(DayName(cube.At) + " " + cube.At.ToString("t") + "  ·  " + LootText.MapName(cube.Zone), 12.5));
            var got = Muted(string.Join(", ", cube.Items.Select(x => LootText.ItemName(x.ItemId) + " ×" + x.Count.ToString("N0"))), 11.5, new Thickness(0, 2, 0, 0));
            row.Children.Add(got);
            recent.Children.Add(Separated(row, r++ > 0, new Thickness(0, 8, 0, 8)));
        }

        FrameworkElement whereCard = Section(loc["Loot.Cubes.ByMap"], where, new Thickness(0));
        FrameworkElement recentCard = Section(loc["Loot.Cubes.Recent"], recent, new Thickness(0));
        pair.Children.Add(whereCard);
        if (wide)
        {
            SetColumn(recentCard, 2);
        }
        else
        {
            SetRow(recentCard, 1);
            recentCard.Margin = new Thickness(0, 12, 0, 0);
        }

        pair.Children.Add(recentCard);
        _cubes.Children.Add(pair);

        var bar = new DockPanel { Margin = new Thickness(0, 12, 0, 4) };
        var delete = FlatButton(loc["Loot.Cubes.Delete"], danger: true, () =>
        {
            if (Ask(loc["Loot.Cubes.Delete.Confirm"]))
            {
                Aion2Loot.ClearCubes();
            }
        });
        DockPanel.SetDock(delete, Dock.Right);
        bar.Children.Add(delete);
        bar.Children.Add(Note(loc["Loot.Cubes.Note"], new Thickness(2, 0, 12, 0)));
        _cubes.Children.Add(bar);
    }

    private static string CubeCount(int n) => n == 1 ? LocalizationManager.Instance["Loot.Cubes.One"] : string.Format(LocalizationManager.Instance["Loot.Cubes.Count"], n.ToString("N0"));

    // ---------- the building blocks ----------

    /// <summary>One line of a list: the icon in the grade's frame, the name in the grade's colour (a gear line: the last piece under it, every piece in the
    /// tooltip), the drop rate with its bar, the worth at the market's prices, and the count.</summary>
    private static FrameworkElement ItemRow(LootLine line, bool separator, string? sub, (int WithKills, int Kills)? rate, MarketBook? worth, bool perCube = false)
    {
        var loc = LocalizationManager.Instance;
        var grid = new Grid { Margin = new Thickness(14, 0, 16, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
        grid.Children.Add(Icon(line, 30));

        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(11, 0, 8, 0) };
        names.Children.Add(new TextBlock { Text = line.Name, FontSize = 13, FontWeight = line.Grade >= 3 ? FontWeights.SemiBold : FontWeights.Normal, Foreground = Frozen(line.Color), TextTrimming = TextTrimming.CharacterEllipsis });
        string? under = sub ?? (line.Gear ? string.Join(", ", line.Members.Take(2).Select(m => LootText.ItemName(m.ItemId))) + (line.Members.Count > 2 ? $" +{line.Members.Count - 2}" : "") : null);
        if (under is not null)
        {
            var small = Muted(under, 11.5, new Thickness(0, 1, 0, 0));
            small.TextWrapping = TextWrapping.NoWrap;
            small.TextTrimming = TextTrimming.CharacterEllipsis;
            names.Children.Add(small);
        }

        if (line.Gear)
        {
            names.ToolTip = LootStats.MembersTip(line);
        }

        SetColumn(names, 1);
        grid.Children.Add(names);

        if (rate is { } r && LootText.RateParts(r.WithKills, r.Kills) is { } parts)
        {
            int withKills = r.WithKills, kills = r.Kills;
            var box = new StackPanel { Width = 116, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0),
                ToolTip = perCube ? LootText.CubeRateTip(withKills, kills, line.Count) : LootText.RateTip(withKills, kills, line.Count) };
            var text = new DockPanel();
            var percent = Muted(parts.Percent + "%", 11, new Thickness(0));
            percent.VerticalAlignment = VerticalAlignment.Bottom;
            DockPanel.SetDock(percent, Dock.Right);
            text.Children.Add(percent);
            var oneIn = new TextBlock { Text = perCube ? LootText.OneInCubes(withKills, kills) : LootStats.OneIn(withKills, kills), FontSize = 12, FontWeight = FontWeights.SemiBold };
            oneIn.SetResourceReference(TextBlock.ForegroundProperty, kills < (perCube ? LootText.RoughCubesBelow : LootText.RoughBelow) ? "Brush.TextMuted" : "Brush.Text");
            text.Children.Add(oneIn);
            box.Children.Add(text);
            box.Children.Add(Bar(Math.Sqrt(Math.Min(1.0, (double)withKills / kills)), line.Color, 4, new Thickness(0, 4, 0, 0)));
            SetColumn(box, 2);
            grid.Children.Add(box);
        }

        if (worth is not null)
        {
            long each = 0, sum = 0;
            foreach ((int itemId, long count) in line.Members)
            {
                if (worth.Items.TryGetValue(itemId, out MarketPrice? price) && price.Value is long value)
                {
                    each = value;
                    sum += value * count;
                }
            }

            if (sum > 0)
            {
                var value = new TextBlock { Text = LootPageText.Short(sum), FontSize = 12.5, Margin = new Thickness(8, 0, 4, 0), MinWidth = 52, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = line.Members.Count == 1 ? string.Format(loc["Loot.ItemWorth"], each.ToString("N0"), sum.ToString("N0")) : sum.ToString("N0") + " Kinah" };
                value.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
                SetColumn(value, 3);
                grid.Children.Add(value);
            }
        }

        var amount = new TextBlock { Text = "×" + line.Count.ToString("N0"), FontSize = 13, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        amount.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        SetColumn(amount, 4);
        grid.Children.Add(amount);
        return Separated(grid, separator, new Thickness(0, 7, 0, 7));
    }

    /// <summary>The item's icon in a frame of its grade's colour; a diamond in that colour where the meter has no picture of it.</summary>
    private static FrameworkElement Icon(LootLine line, double size)
    {
        var frame = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = Frozen(Color.FromArgb(0xA0, line.Color.R, line.Color.G, line.Color.B)), Background = Frozen(Color.FromArgb(0x24, line.Color.R, line.Color.G, line.Color.B)),
        };
        if (OverlayPictures.Load(line.IconPath) is ImageSource picture)
        {
            frame.Child = new Image { Source = picture, Stretch = Stretch.Uniform, Margin = new Thickness(2) };
        }
        else
        {
            frame.Child = new TextBlock { Text = "◆", FontSize = size * 0.42, Foreground = Frozen(line.Color), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }

        return frame;
    }

    /// <summary>A number tile: a small caption, the figure big (in <paramref name="accent"/> with a line of it on top when given), a line under it.</summary>
    private static Border Tile(string label, string value, string? sub, Color? accent)
    {
        var stack = new StackPanel();
        stack.Children.Add(Caps(label, new Thickness(0)));
        var big = new TextBlock { Text = value, FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0) };
        if (accent is Color c)
        {
            big.Foreground = Frozen(c);
        }
        else
        {
            big.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        }

        stack.Children.Add(big);
        if (!string.IsNullOrEmpty(sub))
        {
            var small = Muted(sub, 11.5, new Thickness(0, 1, 0, 0));
            small.TextWrapping = TextWrapping.NoWrap;
            small.TextTrimming = TextTrimming.CharacterEllipsis;
            stack.Children.Add(small);
        }

        var tile = Card(new Border { Child = stack, Padding = new Thickness(16, 12, 14, 12) });
        tile.Margin = new Thickness(5, 0, 5, 0);
        if (accent is Color line)
        {
            tile.BorderBrush = Frozen(Color.FromArgb(0x90, line.R, line.G, line.B));
        }

        return tile;
    }

    /// <summary>A card with a title strip (the title, a muted note beside it, a figure on the right in gold) over its rows.</summary>
    private static Border Section(string title, UIElement body, Thickness margin, string? note = null, string? figure = null, FrameworkElement? badge = null)
    {
        var head = new DockPanel();
        if (figure is not null)
        {
            var f = new TextBlock { Text = figure, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Frozen(GoldColor), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            DockPanel.SetDock(f, Dock.Right);
            head.Children.Add(f);
        }

        if (badge is not null)
        {
            badge.Margin = new Thickness(12, 0, 0, 0);
            DockPanel.SetDock(badge, Dock.Right);
            head.Children.Add(badge);
        }

        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var t = new System.Windows.Documents.Run(title) { FontSize = 14, FontWeight = FontWeights.SemiBold };
        t.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Brush.Text");
        text.Inlines.Add(t);
        if (note is not null)
        {
            var n = new System.Windows.Documents.Run("   " + note) { FontSize = 12 };
            n.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Brush.TextMuted");
            text.Inlines.Add(n);
        }

        head.Children.Add(text);
        var strip = new Border { Child = head, Padding = new Thickness(16, 10, 16, 10), BorderThickness = new Thickness(0, 0, 0, 1) };
        strip.SetResourceReference(Border.BackgroundProperty, "Brush.RowAlt");
        strip.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        var stack = new StackPanel();
        stack.Children.Add(strip);
        stack.Children.Add(body);
        var card = Card(stack);
        card.Margin = margin;
        return card;
    }

    private static Border Card(UIElement child)
    {
        var card = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Child = child, ClipToBounds = true };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return card;
    }

    private static Border EmptyCard(string text)
    {
        var stack = new StackPanel { Margin = new Thickness(22, 20, 22, 20) };
        var mark = new TextBlock { Text = "◆", FontSize = 20, Foreground = Frozen(GoldColor), Margin = new Thickness(0, 0, 0, 8) };
        stack.Children.Add(mark);
        stack.Children.Add(Muted(text, 13, new Thickness(0)));
        return Card(stack);
    }

    /// <summary>A row with a line above it unless it is the first.</summary>
    private static Border Separated(UIElement child, bool separator, Thickness padding)
    {
        var b = new Border { Child = child, Padding = padding, BorderThickness = new Thickness(0, separator ? 1 : 0, 0, 0) };
        b.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return b;
    }

    /// <summary>A rounded bar on a faint track; <paramref name="share"/> 0..1 of the width.</summary>
    private static Grid Bar(double share, Color color, double height, Thickness? margin = null)
    {
        var grid = new Grid { Height = height, Margin = margin ?? new Thickness(0) };
        var track = new Border { CornerRadius = new CornerRadius(height / 2) };
        track.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
        grid.Children.Add(track);
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Clamp(share, 0.02, 1.0), GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - Math.Clamp(share, 0.02, 1.0), GridUnitType.Star) });
        SetColumnSpan(track, 2);
        grid.Children.Add(new Border { CornerRadius = new CornerRadius(height / 2), Background = Frozen(color) });
        return grid;
    }

    private static Border Pill(string text, Color color) => new()
    {
        CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 1, 7, 1), Background = Frozen(Color.FromArgb(0x2A, color.R, color.G, color.B)),
        VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right,
        Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Frozen(color) },
    };

    /// <summary>A quiet button: a rounded outline that lights up under the mouse (red for one that deletes).</summary>
    private static Border FlatButton(string text, bool danger, Action click, Thickness? margin = null)
    {
        var label = new TextBlock { Text = text, FontSize = 12.5 };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        var button = new Border
        {
            Child = label, Padding = new Thickness(12, 5, 12, 5), CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
            Margin = margin ?? new Thickness(0), VerticalAlignment = VerticalAlignment.Center, Focusable = true,
        };
        button.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
        button.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        button.MouseEnter += (_, _) =>
        {
            button.SetResourceReference(Border.BorderBrushProperty, danger ? "Brush.Danger" : "Brush.Accent");
            label.SetResourceReference(TextBlock.ForegroundProperty, danger ? "Brush.Danger" : "Brush.Text");
        };
        button.MouseLeave += (_, _) =>
        {
            button.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        };
        button.MouseLeftButtonUp += (_, _) => click();
        return button;
    }

    private static TextBlock Strong(string text, double size)
    {
        var t = new TextBlock { Text = text, FontSize = size, FontWeight = FontWeights.SemiBold };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        return t;
    }

    private static TextBlock Muted(string text, double size, Thickness margin)
    {
        var t = new TextBlock { Text = text, FontSize = size, Margin = margin, TextWrapping = TextWrapping.Wrap };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        return t;
    }

    /// <summary>A small caption in capitals ("TODAY", "MONSTERS KILLED").</summary>
    private static TextBlock Caps(string text, Thickness margin)
    {
        var t = new TextBlock { Text = text.ToUpper(CultureInfo.CurrentCulture), FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = margin };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
        return t;
    }

    private static TextBlock Note(string text, Thickness margin)
    {
        var t = Muted(text, 11.5, margin);
        return t;
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>The Reset button: everything the loot log keeps on this PC (areas, drop rates, Hidden Cube rewards) after a warning that it can't be undone.</summary>
    private void ResetAll()
    {
        var loc = LocalizationManager.Instance;
        if (ThemedMessageBox.Show(Window.GetWindow(this), loc["Loot.ResetAll.Confirm"], loc["Loot.ResetAll"], MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            Aion2Loot.ClearAll();
            _tabsShown = _overviewShown = _shown = _ratesShown = _cubesShown = "";
            Refresh();
        }
    }

    private bool Ask(string text) =>
        ThemedMessageBox.Show(Window.GetWindow(this), text, LocalizationManager.Instance["Main.MenuTools.Loot"], MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}

/// <summary>Kinah figures short enough for a line.</summary>
public static class LootPageText
{
    /// <summary>1,234 / 12.3k / 1.23M.</summary>
    public static string Short(long kinah) => kinah switch
    {
        >= 1_000_000_000 => (kinah / 1e9).ToString("0.##") + "B",
        >= 1_000_000 => (kinah / 1e6).ToString("0.##") + "M",
        >= 10_000 => (kinah / 1e3).ToString("0.#") + "k",
        _ => kinah.ToString("N0"),
    };
}
