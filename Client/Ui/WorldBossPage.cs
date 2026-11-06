using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AionDPS.Aion2;
using static AionDPS.Ui.JourneyViews;

namespace AionDPS.Ui;

/// <summary>
/// World bosses (redone 6 November 2026, the owner: "fix the world boss tab ... ugly, and unfixed ... the UI design is terrible"). Every field boss with when it
/// is back, as the game's own list of a region says it (<see cref="Aion2FieldBosses"/>: the server sends the list of the region the player is in - all of its
/// bosses, alive or when they come back - and it is kept on this PC until a newer one comes). At the top what is up now and what comes next over all regions;
/// below one region at a time as a table, with when its list was read; a boss can be watched (on the overlay, and its sound when it is back) with a click.
/// </summary>
internal sealed class WorldBossPage : Grid
{
    private readonly StackPanel _root = new() { Margin = new Thickness(0, 0, 0, 16), MaxWidth = 1180, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly List<Action<DateTime>> _clocks = new();
    private string? _region;
    private DateTime _drawnAt = DateTime.MinValue;
    private int _changes;
    private int _drawnChanges = -1;

    private static readonly Color Green = Color.FromRgb(0x5F, 0xD0, 0x8A);
    private static readonly Color Amber = Color.FromRgb(0xF0, 0xB4, 0x4A);
    private static readonly Color Blue = Color.FromRgb(0x6E, 0xA8, 0xF0);
    private static readonly Color Purple = Color.FromRgb(0xB0, 0x8C, 0xF4);
    private static readonly Color Grey = Color.FromRgb(0x8A, 0x94, 0xA0);
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

    public WorldBossPage()
    {
        Children.Add(_root);
        Action changed = () => Dispatcher.BeginInvoke(new Action(() => _changes++));
        Aion2FieldBosses.Changed += changed;
        Unloaded += (_, _) => Aion2FieldBosses.Changed -= changed;
        Draw();
    }

    /// <summary>Once a second (the page's timer): the clocks count down; the whole page is drawn again when a list came or every 20 seconds (a boss's state
    /// can change by the clock alone: its time has come).</summary>
    public void Tick()
    {
        DateTime now = DateTime.Now;
        if (_changes != _drawnChanges || now - _drawnAt > TimeSpan.FromSeconds(20))
        {
            Draw();
            return;
        }

        foreach (Action<DateTime> clock in _clocks)
        {
            clock(now);
        }
    }

    private static string Language => LocalizationManager.Instance.Language;

    private sealed record Row(FieldBossMap Map, FieldBoss Boss, BossStatus Status, string Name);

    private void Draw()
    {
        _drawnAt = DateTime.Now;
        _drawnChanges = _changes;
        _clocks.Clear();
        _root.Children.Clear();
        DateTime now = DateTime.Now;
        IReadOnlyList<FieldBossMap> maps = Ordered();
        if (maps.Count == 0)
        {
            _root.Children.Add(Wrapped("The list of the field bosses is missing from the app's files. Reinstall the app.", "Brush.TextMuted", 13));
            return;
        }

        var rows = maps.SelectMany(m => m.Bosses.Select(b => new Row(m, b, Aion2FieldBosses.StatusOf(b, now), Aion2FieldBosses.DisplayName(m, b, Language)))).ToList();
        MeterSettings settings = MeterSettings.Load();
        _region ??= maps.FirstOrDefault(m => Aion2FieldBosses.ReadAt(m) is not null)?.Key ?? maps[0].Key;

        TextBlock lede = Wrapped("Every field boss and when it's back, straight from the game. Go into a region once - Altgard, Morheim, Reshanta, wherever - and the game sends "
            + "the times of all its bosses by itself; they're kept here until it sends newer ones. No need to stand at a boss or open anything.", "Brush.TextMuted", 12.5);
        lede.Margin = new Thickness(0, 0, 0, 14);
        lede.MaxWidth = 980;
        lede.HorizontalAlignment = HorizontalAlignment.Left;
        _root.Children.Add(lede);

        // ---- the numbers
        var up = rows.Where(r => r.Status.Phase == BossPhase.Now && Aion2FieldBosses.ReadAt(r.Map) is not null).ToList();
        var coming = rows.Where(r => r.Status.Phase is BossPhase.Respawn or BossPhase.Fixed && r.Status.At > now).OrderBy(r => r.Status.At).ToList();
        int watched = rows.Count(r => settings.BossAlerts.TryGetValue(r.Boss.Npc.ToString(), out BossAlertSetting? a) && a.Enabled);
        int read = maps.Count(m => Aion2FieldBosses.ReadAt(m) is not null);
        var figures = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, -10, 2) };
        figures.Children.Add(Figure("Up now", up.Count.ToString(En), null, up.Count == 0 ? "none known" : string.Join(", ", up.Take(2).Select(r => r.Name)) + (up.Count > 2 ? " …" : ""), null, Green));
        Border next = Figure("Next back", coming.Count == 0 ? "–" : "", null, coming.Count == 0 ? "no time known yet" : coming[0].Name, null, Amber);
        if (coming.Count > 0 && FindBig(next) is { } big)
        {
            DateTime at = coming[0].Status.At!.Value;
            _clocks.Add(t => big.Text = Left(at - t));
            big.Text = Left(at - now);
        }

        figures.Children.Add(next);
        figures.Children.Add(Figure("Watching", watched.ToString(En), null, watched == 0 ? "click Watch on a boss" : "on your overlay, with a sound", null, Gold));
        DateTime? newest = maps.Select(Aion2FieldBosses.ReadAt).Where(t => t is not null).DefaultIfEmpty(null).Max();
        figures.Children.Add(Figure("Regions read", $"{read} of {maps.Count}", null, newest is DateTime n ? "last " + Ago(now - n) : "go into a region once", null, Blue));
        _root.Children.Add(figures);

        // ---- up now and coming next, over all regions
        var pair = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
        var upList = new StackPanel();
        if (up.Count == 0)
        {
            upList.Children.Add(Wrapped(read == 0 ? "Nothing read yet: go into a region where field bosses live." : "None of the bosses read is up right now.", "Brush.TextSubtle", 12));
        }

        foreach (Row r in up.OrderBy(r => r.Status.At ?? DateTime.MaxValue).Take(7))
        {
            upList.Children.Add(Line(r, settings, now, showRegion: true));
        }

        if (up.Count > 7)
        {
            upList.Children.Add(Text($"and {up.Count - 7} more in the regions below", "Brush.TextSubtle", 11.5));
        }

        Border upCard = Card(upList, "Up now", null, new Thickness(0, 0, 12, 12));
        pair.Children.Add(upCard);
        var nextList = new StackPanel();
        if (coming.Count == 0)
        {
            nextList.Children.Add(Wrapped("No respawn time known yet. The times come with a region's list.", "Brush.TextSubtle", 12));
        }

        foreach (Row r in coming.Take(7))
        {
            nextList.Children.Add(Line(r, settings, now, showRegion: true));
        }

        Border nextCard = Card(nextList, "Coming back next", null, new Thickness(0, 0, 0, 12));
        Grid.SetColumn(nextCard, 1);
        pair.Children.Add(nextCard);
        _root.Children.Add(pair);

        // ---- one region at a time
        var tabs = new WrapPanel { Margin = new Thickness(0, 8, 0, 10) };
        foreach (FieldBossMap map in maps)
        {
            DateTime? at = Aion2FieldBosses.ReadAt(map);
            int upHere = rows.Count(r => r.Map == map && r.Status.Phase == BossPhase.Now && at is not null);
            tabs.Children.Add(RegionChip(map, at, upHere, now));
        }

        _root.Children.Add(tabs);
        FieldBossMap shown = maps.FirstOrDefault(m => m.Key == _region) ?? maps[0];
        _root.Children.Add(RegionCard(shown, rows.Where(r => r.Map == shown).ToList(), settings, now));

        TextBlock foot = Wrapped("Sound and volume for each boss: Configuration (top right). The overlay shows the bosses you watch; the switch beside Configuration turns it on. "
            + "Abyss bosses with a fixed time (Nahma, the Executors, Watcher Kaira) show their schedule until the game's own time is read.", "Brush.TextSubtle", 11.5);
        foot.Margin = new Thickness(2, 4, 0, 0);
        foot.MaxWidth = 980;
        foot.HorizontalAlignment = HorizontalAlignment.Left;
        _root.Children.Add(foot);
    }

    private static TextBlock? FindBig(Border figure) =>
        figure.Child is StackPanel stack ? stack.Children.OfType<TextBlock>().FirstOrDefault(t => t.FontSize >= 20) : null;

    /// <summary>The regions in the order a player wants them: the own side's two (the side whose list was read last), the Abyss, the other side.</summary>
    private static IReadOnlyList<FieldBossMap> Ordered()
    {
        var maps = Aion2FieldBosses.Maps;
        DateTime Newest(string prefix) => maps.Where(m => m.Key.StartsWith(prefix, StringComparison.Ordinal)).Select(Aion2FieldBosses.ReadAt)
            .Where(t => t is not null).Select(t => t!.Value).DefaultIfEmpty(DateTime.MinValue).Max();
        bool asmodian = Newest("World_D") >= Newest("World_L");
        string[] order = asmodian ? new[] { "World_D", "Abyss", "World_L" } : new[] { "World_L", "Abyss", "World_D" };
        return maps.OrderBy(m => Array.FindIndex(order, p => m.Key.StartsWith(p, StringComparison.Ordinal)) is int i and >= 0 ? i : 9).ThenBy(m => m.Key, StringComparer.Ordinal).ToList();
    }

    private Border RegionChip(FieldBossMap map, DateTime? at, int upHere, DateTime now)
    {
        bool on = map.Key == _region;
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        Color dot = at is null ? Grey : now - at < TimeSpan.FromHours(1) ? Green : Amber;
        line.Children.Add(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(dot), Margin = new Thickness(0, 1, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        line.Children.Add(Text(map.NameIn(Language), on ? "Brush.Text" : "Brush.TextMuted", 12.5, bold: on));
        if (upHere > 0)
        {
            var count = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 0, 6, 1), Margin = new Thickness(8, 0, 0, 0), Background = new SolidColorBrush(Color.FromArgb(0x30, Green.R, Green.G, Green.B)),
                Child = new TextBlock { Text = upHere + " up", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Green) } };
            line.Children.Add(count);
        }

        var chip = new Border
        {
            Child = line, Padding = new Thickness(12, 6, 12, 7), Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(9), Cursor = Cursors.Hand, BorderThickness = new Thickness(1),
            ToolTip = at is DateTime t ? "Read " + Ago(now - t) : "Not read yet: go into " + map.NameIn(Language) + " once",
        };
        if (on)
        {
            chip.SetResourceReference(Border.BackgroundProperty, "Brush.ControlRaised");
            chip.BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, Gold.R, Gold.G, Gold.B));
        }
        else
        {
            chip.Background = Brushes.Transparent;
            chip.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            chip.MouseEnter += (_, _) => chip.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
            chip.MouseLeave += (_, _) => chip.Background = Brushes.Transparent;
        }

        chip.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _region = map.Key;
            Draw();
        };
        return chip;
    }

    private UIElement RegionCard(FieldBossMap map, IReadOnlyList<Row> rows, MeterSettings settings, DateTime now)
    {
        var stack = new StackPanel();
        DateTime? at = Aion2FieldBosses.ReadAt(map);
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        TextBlock when = Text(at is DateTime t ? "Read " + Ago(now - t) + " (" + t.ToString("HH:mm", En) + ")" : "Not read yet", at is null ? "Brush.TextSubtle" : "Brush.TextMuted", 12);
        DockPanel.SetDock(when, Dock.Right);
        head.Children.Add(when);
        TextBlock title = Text(map.NameIn(Language), "Brush.Text", 17, bold: true);
        title.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Title");
        head.Children.Add(title);
        stack.Children.Add(head);
        if (at is null)
        {
            var hint = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 9, 12, 10), Margin = new Thickness(0, 0, 0, 10), Background = new SolidColorBrush(Color.FromArgb(0x1C, Amber.R, Amber.G, Amber.B)),
                Child = Wrapped($"Go into {map.NameIn(Language)} once with the app running: the game sends the times of all {rows.Count} bosses here by itself, and they're kept.", "Brush.Text", 12.5) };
            stack.Children.Add(hint);
        }

        var columns = new Grid { Margin = new Thickness(8, 0, 8, 4) };
        Columns(columns);
        AddCell(columns, Text("BOSS", "Brush.TextSubtle", 10.5, bold: true), 1);
        AddCell(columns, Text("STATE", "Brush.TextSubtle", 10.5, bold: true), 2);
        TextBlock whenHead = Text("WHEN", "Brush.TextSubtle", 10.5, bold: true);
        whenHead.HorizontalAlignment = HorizontalAlignment.Right;
        AddCell(columns, whenHead, 3);
        stack.Children.Add(columns);
        foreach (Row r in rows.OrderBy(r => Order(r.Status, now)).ThenBy(r => r.Status.At ?? DateTime.MaxValue).ThenBy(r => r.Name, StringComparer.CurrentCulture))
        {
            stack.Children.Add(TableRow(r, settings, now, at is not null));
        }

        return Card(stack, null, null, new Thickness(0, 0, 0, 12));
    }

    private static int Order(BossStatus s, DateTime now) => s.Phase switch
    {
        BossPhase.Now => 0,
        BossPhase.Respawn or BossPhase.Fixed when s.At > now => 1,
        _ => 2,
    };

    private static void Columns(Grid grid)
    {
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(124) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
    }

    private static void AddCell(Grid grid, UIElement element, int column)
    {
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    /// <summary>A boss in the region's table: watch, the name (with its fixed time when it has one), its state as a pill, when it is back (counting down).</summary>
    private UIElement TableRow(Row r, MeterSettings settings, DateTime now, bool read)
    {
        var grid = new Grid();
        Columns(grid);
        AddCell(grid, Watch(r, settings), 0);
        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        TextBlock title = Text(r.Name, "Brush.Text", 13.5, bold: true);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        name.Children.Add(title);
        if (r.Boss.Rule is { } rule)
        {
            name.Children.Add(Text(RuleText(rule), "Brush.TextSubtle", 11));
        }

        AddCell(grid, name, 1);
        AddCell(grid, Pill(r.Status, now, read), 2);
        AddCell(grid, When(r.Status, now, read), 3);
        var line = new Border { Child = grid, Padding = new Thickness(8, 7, 8, 7), CornerRadius = new CornerRadius(8), Background = Brushes.Transparent };
        line.MouseEnter += (_, _) => line.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
        line.MouseLeave += (_, _) => line.Background = Brushes.Transparent;
        return line;
    }

    /// <summary>A boss in the top cards: the pill, the name and region, when (counting down), watch.</summary>
    private UIElement Line(Row r, MeterSettings settings, DateTime now, bool showRegion)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        TextBlock title = Text(r.Name, "Brush.Text", 13, bold: true);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        words.Children.Add(title);
        if (showRegion)
        {
            words.Children.Add(Text(r.Map.NameIn(Language), "Brush.TextSubtle", 11));
        }

        AddCell(grid, words, 0);
        UIElement when = When(r.Status, now, read: true);
        ((FrameworkElement)when).Margin = new Thickness(10, 0, 10, 0);
        AddCell(grid, when, 1);
        AddCell(grid, Watch(r, settings), 2);
        var line = new Border { Child = grid, Padding = new Thickness(8, 5, 6, 5), Margin = new Thickness(-8, 0, -6, 0), CornerRadius = new CornerRadius(8), Background = Brushes.Transparent };
        line.MouseEnter += (_, _) => line.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
        line.MouseLeave += (_, _) => line.Background = Brushes.Transparent;
        return line;
    }

    private static Border Pill(BossStatus s, DateTime now, bool read)
    {
        (string text, Color colour) = s.Phase switch
        {
            BossPhase.Now => ("Up now", Green),
            BossPhase.Respawn when s.At is DateTime at && at - now < TimeSpan.FromMinutes(15) => ("Back soon", Amber),
            BossPhase.Respawn => ("Dead", Blue),
            BossPhase.Fixed when s.At is DateTime at && at - now < TimeSpan.FromMinutes(15) => ("Starts soon", Amber),
            BossPhase.Fixed => ("Scheduled", Purple),
            _ => (read ? "Unknown" : "Not read", Grey),
        };
        return new Border
        {
            CornerRadius = new CornerRadius(7), Padding = new Thickness(9, 2, 9, 3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0x2A, colour.R, colour.G, colour.B)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, colour.R, colour.G, colour.B)), BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(colour) },
        };
    }

    /// <summary>When: the clock time it is back with the time left counting down; for one that is up, since when (or when it was seen).</summary>
    private UIElement When(BossStatus s, DateTime now, bool read)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        TextBlock top = Text("", "Brush.Text", 13, bold: true);
        top.HorizontalAlignment = HorizontalAlignment.Right;
        TextBlock under = Text("", "Brush.TextSubtle", 11);
        under.HorizontalAlignment = HorizontalAlignment.Right;
        stack.Children.Add(top);
        stack.Children.Add(under);
        switch (s.Phase)
        {
            case BossPhase.Respawn or BossPhase.Fixed when s.At is DateTime at && at > now:
                top.Text = WorldBossText.Clock(at, now);
                under.Text = "in " + Left(at - now);
                _clocks.Add(t => under.Text = at > t ? "in " + Left(at - t) : "now");
                break;
            case BossPhase.Now:
                top.Text = s.At is DateTime since && since <= now ? "since " + WorldBossText.Clock(since, now) : "up";
                top.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Heal");
                under.Text = s.SeenAt is DateTime seen ? "seen " + Ago(now - seen) : "";
                break;
            default:
                top.Text = "–";
                top.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
                under.Text = read ? "no time in the list" : "";
                break;
        }

        return stack;
    }

    /// <summary>Watch: the boss on the overlay, and its sound when it is back. One click, saved at once.</summary>
    private Border Watch(Row r, MeterSettings settings)
    {
        string key = r.Boss.Npc.ToString(En);
        bool on = settings.BossAlerts.TryGetValue(key, out BossAlertSetting? alert) && alert.Enabled;
        var text = new TextBlock { Text = on ? "Watching" : "Watch", FontSize = 11.5, FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal };
        var pill = new Border
        {
            CornerRadius = new CornerRadius(7), Padding = new Thickness(9, 2, 9, 3), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center, Child = text,
            ToolTip = on ? "On your overlay, with its sound when it's back. Click to stop." : "Put it on your overlay, with a sound when it's back",
        };
        if (on)
        {
            text.Foreground = new SolidColorBrush(Gold);
            pill.Background = new SolidColorBrush(Color.FromArgb(0x26, Gold.R, Gold.G, Gold.B));
            pill.BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, Gold.R, Gold.G, Gold.B));
        }
        else
        {
            text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            pill.Background = Brushes.Transparent;
            pill.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        }

        pill.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            MeterSettings current = MeterSettings.Load();
            BossAlertSetting setting = current.BossAlerts.GetValueOrDefault(key) ?? new BossAlertSetting();
            setting.Enabled = !(current.BossAlerts.TryGetValue(key, out BossAlertSetting? was) && was.Enabled);
            current.BossAlerts[key] = setting;
            current.Save();
            Draw();
        };
        return pill;
    }

    /// <summary>"Fri, Sun 22:05" or "every 3 h from 02:00" in the PC's own time.</summary>
    private static string RuleText(FixedBossRule rule)
    {
        DateTime now = DateTime.Now;
        if (Aion2FieldBosses.FixedWindow(rule, now) is not { } next)
        {
            return "";
        }

        if (rule.Kind == "cycle")
        {
            return $"every {rule.EveryMinutes / 60.0:0.#} h, next {next.Start:HH:mm}";
        }

        string[] names = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
        // the days as the PC's clock sees them: the next window's local weekday shift applied to all
        TimeZoneInfo zone = Aion2FieldBosses.RuleZone;
        DateTime nextOnServer = ReferenceEquals(zone, TimeZoneInfo.Local) ? next.Start : TimeZoneInfo.ConvertTimeFromUtc(next.Start.ToUniversalTime(), zone);
        int shift = ((int)next.Start.DayOfWeek - (int)nextOnServer.DayOfWeek + 7) % 7;
        string days = string.Join(", ", rule.Days.Select(d => (d + shift) % 7).OrderBy(d => d == 0 ? 7 : d).Select(d => names[d]));
        return $"{days} at {next.Start:HH:mm}";
    }

    /// <summary>"1 h 12 min", "12:04", "45 s".</summary>
    private static string Left(TimeSpan t)
    {
        if (t < TimeSpan.Zero)
        {
            t = TimeSpan.Zero;
        }

        if (t.TotalDays >= 1)
        {
            return $"{(int)t.TotalDays} d {t.Hours} h";
        }

        return t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes:00} min" : $"{t.Minutes}:{t.Seconds:00}";
    }

    private static string Ago(TimeSpan t) =>
        t.TotalMinutes < 1 ? "just now" : t.TotalHours < 1 ? $"{(int)t.TotalMinutes} min ago" : t.TotalDays < 1 ? $"{(int)t.TotalHours} h ago" : $"{(int)t.TotalDays} d ago";
}
