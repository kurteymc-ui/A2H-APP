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
        Closed += (_, _) =>
        {
            FightLog.Changed -= onChanged;
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
        IReadOnlyList<FightLogEntry> everything = FightLog.Entries();
        DrawCharacters(everything);
        IReadOnlyList<FightLogEntry> all = _character == AllCharacters ? everything : everything.Where(e => e.You == _character).ToList();

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

    /// <summary>Your best DPS per boss (killed fights) and per dummy parse length.</summary>
    private void DrawBests(IReadOnlyList<FightLogEntry> all)
    {
        var bosses = all.Where(e => e.Kind == FightLogKind.Boss && e.Killed == true && e.YourDps > 0).GroupBy(TitleOf).Select(g => g.MaxBy(e => e.YourDps)!).OrderBy(TitleOf).ToList();
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
            var card = new StackPanel();
            bool first = true;
            foreach (FightLogEntry entry in list)
            {
                card.Children.Add(Row(entry, first, best: true));
                first = false;
            }

            _list.Children.Add(Card(card));
        }
    }

    private FrameworkElement Row(FightLogEntry entry, bool first, bool best, bool inVisit = false)
    {
        var grid = new Grid { Margin = new Thickness(14, 9, 18, 9) };
        foreach (GridLength w in new[] { new GridLength(46), new GridLength(1, GridUnitType.Star), new GridLength(90), new GridLength(110) })
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        }

        ImageSource? icon = entry.BossNpcId is int npc ? Picture(Aion2Artwork.BossThumbPath(npc)) : null;
        icon ??= entry.YourClass is { Length: > 0 } cls ? Picture(Path.Combine(AppContext.BaseDirectory, "assets", "classes", "icons", cls + ".png")) : null;
        var box = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(5), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
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
            CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 2, 7, 2), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Background = bad ? BadBack : GoodBack,
            Child = new TextBlock { Text = ReportPalette.Clock(entry.Seconds), FontSize = 12.5, Foreground = bad ? BadText : GoodText },
        };
        Grid.SetColumn(chip, 2);
        grid.Children.Add(chip);

        var dps = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        dps.Children.Add(new TextBlock { Text = Short(entry.YourDps > 0 ? entry.YourDps : entry.PartyDps), FontSize = 15, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right });
        if (entry.YourDps > 0 && entry.Kind != FightLogKind.Dummy)
        {
            dps.Children.Add(Muted("party " + Short(entry.PartyDps), 11, new Thickness(0, 1, 0, 0), right: true));
        }

        Grid.SetColumn(dps, 3);
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
        var card = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Child = child, ClipToBounds = true };
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
