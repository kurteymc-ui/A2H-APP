using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AionDPS.Aion2;
using AionDPS.Combat;
using AionDPS.History;
using AionDPS.Upload;

namespace AionDPS.Ui;

/// <summary>
/// The meter as an app (found by the user: the small meter window with its menus and a window per tool was hard to find one's way in): a column of sections
/// on the left (Home, Meter, History, Characters, Training dummy, Overlays, then the tools, then the account and Settings) and the chosen one on the right.
/// "Meter" is the live meter (MeterPage, as it always was); every other section is a page in AppPageHost: Home and Overlays are built here, History and the
/// tools are the pages of their old windows (the window is never shown, its content is taken in and it is closed when the page goes).
/// </summary>
public partial class MainWindow
{
    private sealed record ShellItem(string Id, string Label, string Glyph);

    private string _shellPage = "home";
    private ToolPage? _shellTool;
    private Window? _shellWindow;
    private readonly Dictionary<string, Border> _shellButtons = new();
    private readonly DispatcherTimer _shellTick = new() { Interval = TimeSpan.FromSeconds(30) };
    private StackPanel? _shellNavList;
    private bool _shellBuilt;
    private bool _shellCollapsed;

    /// <summary>
    /// The hubs of the column (3 November 2026, the owner: 25 entries were too many to find one's way in): pages that belong together are the tabs of one
    /// entry. Every page keeps its id, so a link to a page (Home's cards, the Overlays page, the pictures) opens its hub on that tab.
    /// </summary>
    private static readonly (string Hub, string[] Tabs)[] ShellHubs =
    {
        ("hub.fights", new[] { "history", "dummy", "combat", "assist" }),
        ("hub.characters", new[] { "characters", "attributes", "cpcalc", "journey" }), // the CP calculator replaced the stat calculator (4 November 2026); Journey beside it (5 November)
        ("hub.map", new[] { "petmap", "petmap.pets", "petmap.gather", "cubes" }),
        ("hub.items", new[] { "loot", "craft" }),
        ("hub.timers", new[] { "timetable", "boss" }),
        // Overlays, Design and Streaming (OBS) as three entries of their own (the owner, 4 November 2026: not under Overlays)
        ("hub.overlays", new[] { "overlays" }),
        ("hub.legion", new[] { "legion" }), // an entry of its own under Overlays (the owner, 6 November 2026)
        ("hub.design", new[] { "meterlook", "theme" }),
        ("hub.stream", new[] { "stream" }),
    };

    /// <summary>Every page there is (the main ones and the tools, the restricted mode's limits applied): id -> name and picture.</summary>
    private Dictionary<string, ShellItem> ShellPages()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var pages = new List<ShellItem>
        {
            new("home", loc["Shell.Home"], "\uE80F"), new("meter", loc["Shell.Meter"], "\uE9D2"), new("history", loc["Shell.History"], "\uE81C"),
            new("characters", loc["Shell.Characters"], "\uE716"), new("dummy", loc["Shell.Dummy"], "\uE916"), new("overlays", loc["Shell.Overlays"], "\uE8A9"),
            new("meterlook", loc["Shell.MeterLook"], "\uE790"),
        };
        var glyphs = new Dictionary<string, string>
        {
            ["assist"] = "\uE945", ["combat"] = "\uE7C1", ["petmap"] = "\uE707", ["petmap.pets"] = "\uE8D4", ["petmap.gather"] = "\uE8D4", ["cubes"] = "\uE721",
            ["loot"] = "\uE7B8", ["craft"] = "\uE90F", ["timetable"] = "\uE787", ["boss"] = "\uE7BA", ["attributes"] = "\uE9F9", ["cpcalc"] = "\uE8EF",
            ["analyzer"] = "\uE9D9", ["stream"] = "\uE714", ["network"] = "\uE701", ["theme"] = "\uE790", ["journey"] = "\uE81C", ["legion"] = "\uEA18",
        };
        pages.AddRange(ToolsWindow.Groups().SelectMany(g => g).Where(t => t.Id is not ("checklist" or "dummy" or "analyzer")) // the combat analyzer: History and the fight Report do it
            .Where(t => !PrivacyConsent.Restricted || PrivacyConsent.RestrictedTools.Contains(t.Id))
            .Select(t => new ShellItem(t.Id, t.Title, glyphs.GetValueOrDefault(t.Id, "\uE8B7"))));
        if (PrivacyConsent.Restricted)
        {
            pages.RemoveAll(i => i.Id is "dummy" && !PrivacyConsent.RestrictedTools.Contains("dummy"));
        }

        return pages.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    }

    /// <summary>The hub a page belongs to ("hub.map"; null: a page of its own, as Home and the Meter). Hub ids never equal a page id: a link to a page opens that page.</summary>
    private static string? HubOf(string id) => ShellHubs.FirstOrDefault(h => h.Tabs.Contains(id)).Hub;

    /// <summary>A hub's pages that exist here (the restricted mode leaves some out), in the hub's order.</summary>
    private List<ShellItem> HubTabs(string hub, IReadOnlyDictionary<string, ShellItem>? pages = null)
    {
        pages ??= ShellPages();
        return ShellHubs.First(h => h.Hub == hub).Tabs.Where(pages.ContainsKey).Select(t => pages[t]).ToList();
    }

    /// <summary>The sections of the column: Home, the Meter and the hubs; then feedback, the network analysis, the account and Settings at the bottom.</summary>
    private List<(string? Group, List<ShellItem> Items)> ShellSections()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        Dictionary<string, ShellItem> pages = ShellPages();
        var hubNames = new Dictionary<string, string>
        {
            ["hub.fights"] = loc["Shell.HubFights"], ["hub.characters"] = loc["Shell.Characters"], ["hub.map"] = loc["Shell.HubMap"], ["hub.items"] = loc["Shell.HubItems"],
            ["hub.timers"] = loc["Timers.Title"], ["hub.overlays"] = loc["Shell.Overlays"], ["hub.design"] = loc["Shell.HubDesign"],
            ["hub.stream"] = loc["Main.MenuTools.Stream"], ["hub.legion"] = loc["Main.MenuTools.Legion"],
        };
        var main = new List<ShellItem> { pages["home"], pages["meter"] };
        foreach (var (hub, _) in ShellHubs)
        {
            List<ShellItem> tabs = HubTabs(hub, pages);
            if (tabs.Count > 0)
            {
                main.Add(new ShellItem(hub, hubNames[hub], tabs[0].Glyph));
            }
        }

        var bottom = new List<ShellItem> { new("feedback", loc["Shell.Feedback"], "\uED15") };
        if (pages.TryGetValue("network", out ShellItem? network))
        {
            bottom.Add(network);
        }

        bottom.Add(new("account", loc["Shell.Account"], "\uE77B"));
        bottom.Add(new("settings", loc["Shell.Settings"], "\uE713"));
        // pinned to the bottom of the column (BuildShell): always in sight
        return new() { (null, main), ("", bottom) };
    }

    /// <summary>Builds the column of sections and opens Home (once, at the start; again when the language changes).</summary>
    internal void BuildShell()
    {
        _shellCollapsed = MeterSettings.Load().AppNavCollapsed;
        _shellButtons.Clear();
        // the pages of the old windows took their text colour and face from their window: here they take the app's
        AppPageHost.SetResourceReference(TextElement.ForegroundProperty, "Brush.Text");
        AppPageHost.SetResourceReference(TextElement.FontFamilyProperty, "Font.Ui");
        var list = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
        var pinned = new StackPanel { Margin = new Thickness(0, 4, 0, 2) };
        foreach (var (group, items) in ShellSections())
        {
            if (group == "")
            {
                // feedback, the account and Settings: at the foot of the column, above the fold arrow, never scrolled away
                var top = new Border { Height = 1, Margin = new Thickness(12, 0, 12, 4) };
                top.SetResourceReference(Border.BackgroundProperty, "Brush.Border");
                pinned.Children.Add(top);
                foreach (ShellItem item in items)
                {
                    pinned.Children.Add(ShellButton(item));
                }

                continue;
            }

            if (group is { Length: > 0 } && !_shellCollapsed)
            {
                TextBlock head = ShellText(group.ToUpperInvariant(), "Brush.TextSubtle", 10.5, bold: true);
                head.Margin = new Thickness(18, 14, 0, 4);
                list.Children.Add(head);
            }
            else if (group is not null)
            {
                var line = new Border { Height = 1, Margin = new Thickness(12, 10, 12, 6) };
                line.SetResourceReference(Border.BackgroundProperty, "Brush.Border");
                list.Children.Add(line);
            }

            foreach (ShellItem item in items)
            {
                list.Children.Add(ShellButton(item));
            }
        }

        // the column folds to its pictures (the choice is kept)
        var fold = new Border { Padding = new Thickness(0, 9, 0, 9), Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = LocalizationManager.Instance[_shellCollapsed ? "Shell.Unfold" : "Shell.Fold"] };
        TextBlock arrow = ShellText(_shellCollapsed ? "" : "", "Brush.TextMuted", 12);
        arrow.FontFamily = new FontFamily("Segoe MDL2 Assets");
        arrow.HorizontalAlignment = HorizontalAlignment.Center;
        fold.Child = arrow;
        fold.MouseLeftButtonUp += (_, _) =>
        {
            var settings = MeterSettings.Load();
            settings.AppNavCollapsed = !_shellCollapsed;
            settings.Save();
            BuildShell();
        };
        var dock = new DockPanel();
        DockPanel.SetDock(fold, Dock.Bottom);
        dock.Children.Add(fold);
        DockPanel.SetDock(pinned, Dock.Bottom);
        dock.Children.Add(pinned);
        dock.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list });
        AppNav.Child = dock;
        AppNav.Width = _shellCollapsed ? 54 : 196;
        _shellNavList = list;
        if (!_shellBuilt)
        {
            _shellBuilt = true;
            _shellTick.Tick += (_, _) =>
            {
                if (_shellPage is "home" or "overlays" && IsVisible && WindowState != WindowState.Minimized)
                {
                    ShowShellPage(_shellPage);
                }
            };
            _shellTick.Start();
        }

        ShowShellPage(_shellPage);
    }

    private Border ShellButton(ShellItem item)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_shellCollapsed ? 30 : 26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        TextBlock glyph = ShellText(item.Glyph, "Brush.TextMuted", 14);
        glyph.FontFamily = new FontFamily("Segoe MDL2 Assets");
        glyph.HorizontalAlignment = _shellCollapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        grid.Children.Add(glyph);
        if (!_shellCollapsed)
        {
            TextBlock label = ShellText(item.Label, "Brush.TextMuted", 13);
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);
        }
        else
        {
            Grid.SetColumnSpan(glyph, 2);
        }

        var button = new Border
        {
            Child = grid, Padding = new Thickness(_shellCollapsed ? 4 : 12, 7, 8, 7), Margin = new Thickness(6, 1, 6, 1), CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent, Cursor = Cursors.Hand, BorderThickness = new Thickness(3, 0, 0, 0), BorderBrush = Brushes.Transparent, Tag = item.Id,
            ToolTip = _shellCollapsed ? item.Label : null,
        };
        button.MouseEnter += (_, _) =>
        {
            if (ShellLit != item.Id)
            {
                button.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
            }
        };
        button.MouseLeave += (_, _) =>
        {
            if (ShellLit != item.Id)
            {
                button.Background = Brushes.Transparent;
            }
        };
        button.MouseLeftButtonUp += (_, _) => ShowShellPage(item.Id);
        _shellButtons[item.Id] = button;
        return button;
    }

    /// <summary>The entry of the column the page shown belongs to: its hub, or the page itself.</summary>
    private string ShellLit => _shellPage == "news" ? "home" : HubOf(_shellPage) ?? _shellPage; // a news item read is one of Home's

    /// <summary>The chosen section lit (a gold edge and a raised background), the others quiet.</summary>
    private void MarkShell()
    {
        foreach (var (id, button) in _shellButtons)
        {
            bool on = id == ShellLit;
            if (on)
            {
                button.SetResourceReference(Border.BackgroundProperty, "Brush.ControlRaised");
                button.SetResourceReference(Border.BorderBrushProperty, "Brush.Accent");
            }
            else
            {
                button.Background = Brushes.Transparent;
                button.BorderBrush = Brushes.Transparent;
            }

            foreach (TextBlock text in ((Grid)button.Child).Children.OfType<TextBlock>())
            {
                text.SetResourceReference(TextBlock.ForegroundProperty, on ? "Brush.Text" : "Brush.TextMuted");
            }
        }
    }

    /// <summary>Opens a section. The account and Settings open their windows; the combat analyzer is a large window of its own.</summary>
    internal void ShowShellPage(string id)
    {
        if (!_shellBuilt)
        {
            return;
        }

        id = AssistTabOf(id);
        if (ShellHubs.Any(h => h.Hub == id))
        {
            // a hub: the tab it was left on, else its first
            List<ShellItem> tabs = HubTabs(id);
            string? last = MeterSettings.Load().AppHubTabs?.GetValueOrDefault(id);
            id = tabs.FirstOrDefault(t => t.Id == last)?.Id ?? tabs.FirstOrDefault()?.Id ?? "home";
        }

        switch (id)
        {
            case "account":
                OnAccountClicked(this, new RoutedEventArgs());
                return;
            case "settings":
                OnSettingsClicked(this, new RoutedEventArgs());
                return;
            case "feedback":
                OnFeedbackClicked(this, new RoutedEventArgs());
                return;
            case "analyzer":
                OpenSessionManager();
                return;
            case "checklist":
                id = "characters";
                break;
        }

        bool same = id == _shellPage && AppPageHost.Children.Count > 0;
        if (!same || id is not ("home" or "overlays"))
        {
            LeaveShellPage();
        }

        _shellPage = id;
        MarkShell();
        bool meter = id == "meter";
        MeterPage.Visibility = meter ? Visibility.Visible : Visibility.Collapsed;
        AppPageHost.Visibility = meter ? Visibility.Collapsed : Visibility.Visible;
        if (meter)
        {
            AppPageHost.Children.Clear();
            return;
        }

        UIElement content = id switch
        {
            "home" => BuildHomePage(),
            "news" => BuildNewsPage(),
            "overlays" => BuildOverlaysPage(),
            "meterlook" => BuildMeterLookPage(),
            "history" => HostWindow(new HistoryWindow()),
            "characters" => HostTool("checklist"),
            _ => HostTool(id),
        };
        if (HubOf(id) is { } hub)
        {
            RememberHubTab(hub, id);
            List<ShellItem> tabs = HubTabs(hub);
            if (tabs.Count > 1)
            {
                content = WithHubTabs(tabs, id, content);
            }
        }

        AppPageHost.Children.Clear();
        AppPageHost.Children.Add(content);
    }

    private static void RememberHubTab(string hub, string tab)
    {
        MeterSettings settings = MeterSettings.Load();
        if (settings.AppHubTabs?.GetValueOrDefault(hub) == tab)
        {
            return;
        }

        settings.AppHubTabs ??= new Dictionary<string, string>();
        settings.AppHubTabs[hub] = tab;
        settings.Save();
    }

    /// <summary>A hub's page with the hub's tabs above it: one pill per page, the shown one raised; a click opens that page.</summary>
    private UIElement WithHubTabs(List<ShellItem> tabs, string shown, UIElement page)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (ShellItem tab in tabs)
        {
            bool on = tab.Id == shown;
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            TextBlock glyph = ShellText(tab.Glyph, on ? "Brush.Accent" : "Brush.TextSubtle", 12);
            glyph.FontFamily = new FontFamily("Segoe MDL2 Assets");
            glyph.Margin = new Thickness(0, 1, 7, 0);
            line.Children.Add(glyph);
            line.Children.Add(ShellText(tab.Label, on ? "Brush.Text" : "Brush.TextMuted", 12.5, bold: on));
            var pill = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 5, 14, 6), Child = line, Background = Brushes.Transparent };
            if (on)
            {
                pill.SetResourceReference(Border.BackgroundProperty, "Brush.ControlRaised");
            }
            else
            {
                string id = tab.Id;
                pill.Cursor = Cursors.Hand;
                pill.MouseEnter += (_, _) => pill.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
                pill.MouseLeave += (_, _) => pill.Background = Brushes.Transparent;
                pill.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    ShowShellPage(id);
                };
            }

            strip.Children.Add(pill);
        }

        var bar = new Border
        {
            CornerRadius = new CornerRadius(16), Padding = new Thickness(3), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(18, 12, 18, 2), Child = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = strip },
        };
        bar.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
        bar.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        var dock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(bar, Dock.Top);
        dock.Children.Add(bar);
        if (page is FrameworkElement element && element.Parent is Panel parent)
        {
            parent.Children.Remove(element);
        }

        dock.Children.Add(page);
        return dock;
    }

    /// <summary>What the page that goes away leaves behind: its timers stop, its old window closes.</summary>
    private void LeaveShellPage()
    {
        _shellTool?.Cleanup?.Invoke();
        _shellTool = null;
        _shellWindow?.Close();
        _shellWindow = null;
        AppPageHost.Children.Clear();
    }

    private UIElement HostWindow(Window window)
    {
        _shellWindow = window;
        return ThemedChrome.TakeContent(window) ?? new Grid();
    }

    private UIElement HostTool(string id)
    {
        _shellTool = BuildToolPage(id);
        if (_shellTool?.Content is { } content)
        {
            if (content is FrameworkElement element && element.Parent is Panel parent)
            {
                parent.Children.Remove(element);
            }

            return content;
        }

        TextBlock none = ShellText(LocalizationManager.Instance["Shell.NotYet"], "Brush.TextMuted", 13);
        none.Margin = new Thickness(24);
        none.TextWrapping = TextWrapping.Wrap;
        return none;
    }

    // ---- Home ----

    /// <summary>
    /// Home: everything at a glance. The resets and the characters' Odyle Energy (who is full, who is soon), the recent fights, the training dummy, the overlays
    /// with a switch each, and the account. Every card opens its section.
    /// </summary>
    private UIElement OdyleRow(TicketCharacter c, Aion2Tickets.OdyleState o, DateTime utcNow)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(165) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        string? cls = c.ClassCode > 0 ? Aion2.Protocol.Aion2SkillNames.ClassFromCode(c.ClassCode) : null;
        TextBlock name = ShellText(c.Name, "Brush.Text", 13, bold: true);
        if (cls is not null)
        {
            name.Foreground = ClassBrushes.Solid(cls);
        }

        name.TextTrimming = TextTrimming.CharacterEllipsis;
        grid.Children.Add(name);
        var energy = new StackPanel { Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        // the basic energy (it recharges, up to the cap) and, after the +, the charged energy (from stones; it does not recharge), as the game shows them
        TextBlock numbers = ShellText((o.Estimated ? "~" : "") + o.Count.ToString("N0", CultureInfo.CurrentCulture) + " / " + o.Cap.ToString("N0", CultureInfo.CurrentCulture), o.Full ? "Brush.Warning" : "Brush.Accent", 12, bold: true);
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(numbers);
        if (o.Bonus > 0)
        {
            TextBlock charged = ShellText("+" + o.Bonus.ToString("N0", CultureInfo.CurrentCulture), "Brush.Text", 12, bold: true);
            charged.Margin = new Thickness(6, 0, 0, 0);
            line.Children.Add(charged);
        }

        energy.Children.Add(line);
        energy.ToolTip = string.Format(CultureInfo.CurrentCulture, loc["Checklist.RosterOdyleLine"], o.Count, o.Cap, o.Bonus, o.PerTick);
        var bar = new ProgressBar { Minimum = 0, Maximum = Math.Max(1, o.Cap), Value = Math.Min(o.Count, o.Cap), Height = 3, Margin = new Thickness(0, 2, 0, 0) };
        bar.SetResourceReference(Control.ForegroundProperty, o.Full ? "Brush.Warning" : "Brush.Accent");
        bar.SetResourceReference(Control.BackgroundProperty, "Brush.Control");
        bar.BorderThickness = new Thickness(0);
        energy.Children.Add(bar);
        Grid.SetColumn(energy, 1);
        grid.Children.Add(energy);
        bool soon = !o.Full && o.FullAt is { } f && f - utcNow < TimeSpan.FromHours(6);
        TextBlock full = ShellText(o.Full ? loc["Checklist.RosterFull"] : o.FullAt is { } at ? string.Format(CultureInfo.CurrentCulture, loc["Checklist.In"], ShortSpan(at - utcNow)) : "–",
            o.Full || soon ? "Brush.Warning" : "Brush.TextMuted", 12, bold: o.Full || soon);
        full.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(full, 2);
        grid.Children.Add(full);
        return grid;
    }

    private UIElement FightRow(FightLogEntry entry, DateTime now)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();
        TextBlock title = ShellText(entry.Title + (entry.Detail is { Length: > 0 } d ? " · " + d : ""), "Brush.Text", 13);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        left.Children.Add(title);
        string state = entry.Kind == FightLogKind.Boss ? (entry.Killed == true ? "Killed" : "Wipe") : entry.Kind.ToString();
        left.Children.Add(ShellText($"{Ago(now - entry.Start)} · {TimeSpan.FromSeconds(entry.Seconds):m\\:ss} · {state}", "Brush.TextSubtle", 11));
        grid.Children.Add(left);
        if (entry.YourDps > 0)
        {
            TextBlock dps = ShellText(PlayerRow.Compact((long)entry.YourDps) + " DPS", "Brush.Accent", 13, bold: true);
            dps.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(dps, 1);
            grid.Children.Add(dps);
        }

        grid.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            OpenHistoryEntry(entry);
        };
        return grid;
    }

    /// <summary>"assist.cooldowns" / "assist.buffs" (the Overlays page's links): the Skill Assist page, on that tab (kept as the tab it opens on).</summary>
    private static string AssistTabOf(string id)
    {
        if (id is not ("assist." + AssistWindow.CooldownsTab or "assist." + AssistWindow.BuffsTab))
        {
            return id;
        }

        MeterSettings settings = MeterSettings.Load();
        settings.AssistTab = id["assist.".Length..];
        settings.Save();
        return "assist";
    }

    // ---- Overlays ----

    private sealed record OverlayDef(string Name, string What, Func<MeterSettings, bool> Get, Action<MeterSettings, bool> Set, string? Page, Action? Reset = null);

    /// <summary>Every overlay over the game, the main ones first: its switch, what it shows and where its settings are.</summary>
    private List<OverlayDef> OverlayDefs()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        return new()
        {
            new(loc["Shell.OvMeter"], loc["Shell.OvMeterWhat"], s => s.MeterOverlayOn, (s, v) => s.MeterOverlayOn = v, "meter", ResetMeterOverlayPlace),
            new(loc["Settings.Nav.Combat"], loc["Shell.OvCombatWhat"], s => s.ShowCombatOverlay, (s, v) => s.ShowCombatOverlay = v, "combat"),
            new(loc["Shell.OvCooldowns"], loc["Shell.OvAssistWhat"], s => s.ShowAssistOverlay, (s, v) => s.ShowAssistOverlay = v, "assist." + AssistWindow.CooldownsTab),
            new(loc["Shell.OvEffects"], loc["Shell.OvEffectsWhat"], s => s.EffectsOverlayOn, (s, v) => s.ShowEffectsOverlay = v, "assist." + AssistWindow.BuffsTab),
            new(loc["Timers.Title"], loc["Shell.OvTimetableWhat"], s => s.ShowTimetable, (s, v) => s.ShowTimetable = v, "timetable"),
            new(loc["Settings.Nav.Boss"], loc["Shell.OvBossWhat"], s => s.ShowBossOverlay, (s, v) => s.ShowBossOverlay = v, "boss"),
            new(loc["Settings.Nav.PetMap"], loc["Shell.OvPetMapWhat"], s => s.ShowPetMap, (s, v) => s.ShowPetMap = v, "petmap"),
            new(loc["Shell.OvPetFarm"], loc["Shell.OvPetFarmWhat"], s => s.ShowPetFarm, (s, v) => s.ShowPetFarm = v, "petmap.pets"),
            new(loc["Main.MenuTools.Loot"], loc["Shell.OvLootWhat"], s => s.ShowLootOverlay, (s, v) => s.ShowLootOverlay = v, "loot"),
            new(loc["Settings.Nav.Gather"], loc["Shell.OvGatherWhat"], s => s.ShowGatherOverlay, (s, v) => s.ShowGatherOverlay = v, "petmap.gather"),
            new(loc["Checklist.Season"], loc["Shell.OvSeasonWhat"], s => s.ShowSeasonOverlay, (s, v) => s.ShowSeasonOverlay = v, "checklist"),
            new(loc["Main.MenuTools.Craft"], loc["Shell.OvCraftWhat"], s => s.ShowCraftOverlay, (s, v) => s.ShowCraftOverlay = v, "craft"),
            new(loc["Settings.Nav.Network"], loc["Shell.OvNetworkWhat"], s => s.ShowNetworkOverlay, (s, v) => s.ShowNetworkOverlay = v, "network"),
            new(loc["Shell.OvHotkeys"], loc["Shell.OvHotkeysWhat"], s => s.ShowHotkeyOverlay, (s, v) => s.ShowHotkeyOverlay = v, null),
            // the stream overlays for OBS are not here: Streaming (OBS) is an entry of the column of its own, with its switch (the owner, 4 November 2026)
        };
    }

    /// <summary>
    /// Overlays: the layout first (OverlayLayout.cs: the monitors with every overlay that is on, dragged to move, the corner to scale), with snapping, moving them
    /// over the game too (every overlay unlocked), all of them smaller or bigger, and all back at their first places; then every overlay in one list, each with
    /// its switch, what it shows and a way to its settings.
    /// </summary>
    private UIElement BuildOverlaysPage()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var page = new StackPanel { Margin = new Thickness(24, 18, 24, 24), MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Left };
        page.Children.Add(PageTitle(loc["Shell.Overlays"], loc["Shell.OverlaysLede"]));

        var editor = Headless ? new OverlayLayoutEditor(SampleLayout, new[] { new Rect(0, 0, 1920, 1080) }) : new OverlayLayoutEditor(LayoutEntries);
        var layout = new StackPanel();
        layout.Children.Add(editor);
        var bar = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var snap = new CheckBox { IsChecked = true, Content = ShellText(loc["Shell.LayoutSnap"], "Brush.Text", 12.5), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 6) };
        snap.Click += (_, _) => editor.Snap = snap.IsChecked == true;
        bar.Children.Add(snap);
        var movable = new CheckBox { IsChecked = OverlaysMovable(MeterSettings.Load()), Content = ShellText(loc["Shell.LayoutMovable"], "Brush.Text", 12.5), VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 18, 6), ToolTip = loc["Shell.LayoutMovableTip"] };
        movable.Click += (_, _) => SetOverlaysMovable(movable.IsChecked == true);
        bar.Children.Add(movable);
        Button LayoutButton(string text, string tip, Action click)
        {
            var b = new Button { Content = text, ToolTip = tip, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 6) };
            b.Click += (_, _) => click();
            return b;
        }

        bar.Children.Add(LayoutButton(loc["Shell.LayoutSmaller"], loc["Shell.LayoutScaleTip"], () => { ScaleAllOverlays(0.9); editor.Rebuild(); }));
        bar.Children.Add(LayoutButton(loc["Shell.LayoutBigger"], loc["Shell.LayoutScaleTip"], () => { ScaleAllOverlays(1.1); editor.Rebuild(); }));
        bar.Children.Add(LayoutButton(loc["Shell.LayoutReset"], loc["Shell.LayoutResetTip"], () =>
        {
            if (ThemedMessageBox.Show(this, loc["Shell.LayoutResetAsk"], loc["Shell.Overlays"], MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                ResetAllOverlayPlaces();
                editor.Rebuild();
            }
        }));
        layout.Children.Add(bar);
        TextBlock hint = Wrapped(loc["Shell.LayoutHint"], "Brush.TextMuted", 12);
        hint.Margin = new Thickness(0, 2, 0, 0);
        layout.Children.Add(hint);
        page.Children.Add(HomeCard(loc["Shell.Layout"], layout, null, 0));
        page.Children.Add(new Border { Height = 14 });

        var list = new StackPanel();
        foreach (OverlayDef def in OverlayDefs())
        {
            list.Children.Add(OverlaySwitch(def, compact: false));
        }

        page.Children.Add(HomeCard(null, list, null, 0));
        return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };
    }

    private UIElement OverlaySwitch(OverlayDef def, bool compact)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var grid = new Grid { Margin = new Thickness(0, compact ? 1 : 6, 0, compact ? 1 : 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(ShellText(def.Name, "Brush.Text", 13, bold: !compact));
        if (!compact)
        {
            TextBlock what = Wrapped(def.What, "Brush.TextMuted", 12);
            what.Margin = new Thickness(0, 2, 12, 0);
            text.Children.Add(what);
            var links = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            if (def.Page is { } pageId)
            {
                links.Children.Add(ShellLink(loc["Shell.OvSettings"], () => ShowShellPage(pageId)));
            }

            if (def.Reset is { } reset)
            {
                TextBlock back = ShellLink(loc["Shell.OvReset"], reset);
                back.Margin = new Thickness(def.Page is null ? 0 : 16, 0, 0, 0);
                links.Children.Add(back);
            }

            if (links.Children.Count > 0)
            {
                text.Children.Add(links);
            }
        }

        grid.Children.Add(text);
        var toggle = new CheckBox { IsChecked = def.Get(MeterSettings.Load()), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0) };
        if (Application.Current?.TryFindResource("SettingsSwitch") is Style style)
        {
            toggle.Style = style;
        }

        toggle.Click += (_, _) =>
        {
            var settings = MeterSettings.Load();
            def.Set(settings, toggle.IsChecked == true);
            settings.Save();
            ApplySettingsEverywhere(settings);
        };
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(toggle);
        if (!compact)
        {
            var line = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = grid, Padding = new Thickness(0, 0, 0, 8) };
            line.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            return line;
        }

        return grid;
    }

    // ---- the layout editor's overlays ----

    /// <summary>Every overlay window that is switched on, with its name (the copies that lend their content to a tool page left out).</summary>
    private IReadOnlyList<OverlayEntry> LayoutEntries()
    {
        var s = MeterSettings.Load();
        var list = new List<OverlayEntry>();
        foreach (Window window in Application.Current.Windows)
        {
            if (window is IOverlayPlace place && !place.PlaceIsView && LayoutName(window, s) is { } name)
            {
                list.Add(new OverlayEntry(name, place));
            }
        }

        return list.OrderBy(e => e.Name, StringComparer.CurrentCulture).ToList();
    }

    /// <summary>An overlay window's name on the layout, or null when it isn't switched on (or isn't one of the overlays).</summary>
    private string? LayoutName(Window window, MeterSettings s)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        if (ReferenceEquals(window, _meterOverlay))
        {
            return s.MeterOverlayOn ? loc["Shell.OvMeter"] : null;
        }

        return window.Title switch
        {
            OverlayPlaces.BossCast => s.ShowCombatOverlay && s.CombatShowBossCast ? loc["Settings.Combat.CardBossCast"] : null,
            OverlayPlaces.BossHp => null, // on the meter now
            OverlayPlaces.TargetOfTarget => s.ShowCombatOverlay && s.CombatShowTargetOfTarget ? loc["Combat.Element.TargetOfTarget"] : null,
            OverlayPlaces.Assist => s.ShowAssistOverlay ? loc["Shell.OvCooldowns"] : null,
            OverlayPlaces.Effects => s.EffectsOverlayOn ? loc["Shell.OvEffects"] : null,
            OverlayPlaces.Timetable => s.ShowTimetable ? loc["Timers.Title"] : null,
            OverlayPlaces.WorldBoss => s.ShowBossOverlay ? loc["Settings.Nav.Boss"] : null,
            OverlayPlaces.PetMap => s.ShowPetMap ? loc["Settings.Nav.PetMap"] : null,
            OverlayPlaces.PetList => s.ShowPetMap ? loc["Shell.LayoutPetList"] : null,
            OverlayPlaces.PetInfo => s.ShowPetFarm ? loc["Shell.OvPetFarm"] : null,
            OverlayPlaces.Loot => s.ShowLootOverlay ? loc["Main.MenuTools.Loot"] : null,
            OverlayPlaces.Gather => s.ShowGatherOverlay ? loc["Settings.Nav.Gather"] : null,
            OverlayPlaces.Season => s.ShowSeasonOverlay ? loc["Checklist.Season"] : null,
            OverlayPlaces.Craft => s.ShowCraftOverlay ? loc["Main.MenuTools.Craft"] : null,
            OverlayPlaces.Network => s.ShowNetworkOverlay ? loc["Settings.Nav.Network"] : null,
            OverlayPlaces.Hotkeys => s.ShowHotkeyOverlay ? loc["Shell.OvHotkeys"] : null,
            OverlayPlaces.Dummy => s.ShowDummyOverlay ? loc["Shell.Dummy"] : null,
            _ => null,
        };
    }

    /// <summary>Every overlay's lock (locked: click-through, unlocked: it can be dragged and scaled over the game). The meter has its own lock in its header.</summary>
    private static readonly (Func<MeterSettings, bool> Get, Action<MeterSettings, bool> Set)[] OverlayLocks =
    {
        (s => s.CombatOverlayLocked, (s, v) => s.CombatOverlayLocked = v), (s => s.CombatCastOverlayLocked ?? s.CombatOverlayLocked, (s, v) => s.CombatCastOverlayLocked = v),
        (s => s.CombatHpOverlayLocked ?? s.CombatOverlayLocked, (s, v) => s.CombatHpOverlayLocked = v), (s => s.AssistOverlayLocked, (s, v) => s.AssistOverlayLocked = v),
        (s => s.TimetableLocked, (s, v) => s.TimetableLocked = v), (s => s.BossOverlayLocked, (s, v) => s.BossOverlayLocked = v),
        (s => s.PetFarmLocked, (s, v) => s.PetFarmLocked = v), (s => s.PetInfoLocked, (s, v) => s.PetInfoLocked = v), (s => s.PetListLocked ?? s.PetFarmLocked, (s, v) => s.PetListLocked = v),
        (s => s.LootOverlayLocked, (s, v) => s.LootOverlayLocked = v), (s => s.CraftOverlayLocked, (s, v) => s.CraftOverlayLocked = v),
        (s => s.GatherOverlayLocked, (s, v) => s.GatherOverlayLocked = v), (s => s.SeasonOverlayLocked, (s, v) => s.SeasonOverlayLocked = v),
        (s => s.NetworkOverlayLocked, (s, v) => s.NetworkOverlayLocked = v), (s => s.DummyOverlayLocked, (s, v) => s.DummyOverlayLocked = v),
        (s => s.HotkeyOverlayLocked ?? s.PetFarmLocked, (s, v) => s.HotkeyOverlayLocked = v),
    };

    private static bool OverlaysMovable(MeterSettings s) => OverlayLocks.All(l => !l.Get(s));

    /// <summary>Every overlay unlocked (shown over the game and the desktop, to be dragged and scaled where it is) or locked again (click-through).</summary>
    private void SetOverlaysMovable(bool movable)
    {
        var s = MeterSettings.Load();
        foreach (var (_, set) in OverlayLocks)
        {
            set(s, !movable);
        }

        s.Save();
        ApplySettingsEverywhere(s);
    }

    /// <summary>
    /// The hotkey for moving the overlays in the game (3 November 2026: "allow to move overlays in game" without opening the app): every overlay and the meter
    /// unlocked - an outline, dragged and scaled where they are, over the game - and the next press locks them all again (clicks then go through to the game).
    /// A note at the top of the screen says which it was.
    /// </summary>
    private void ToggleOverlaysMovable()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        bool movable = !(OverlaysMovable(MeterSettings.Load()) && !_meterOverlayLocked);
        SetOverlaysMovable(movable);
        var s = MeterSettings.Load();
        s.MeterOverlayLocked = _meterOverlayLocked = !movable;
        s.Save();
        ApplyOverlayLock();
        string keys = HotkeyBinding.Parse(s.HotkeyMoveOverlays).ToString();
        CubeToast.Note(string.Format(System.Globalization.CultureInfo.CurrentCulture, loc[movable ? "Overlays.MoveOn" : "Overlays.MoveOff"], keys));
    }

    /// <summary>The pets only the Black Cloud Merchant sells that the player left ticked (all of them unless some were unticked on the Pets page).</summary>
    internal static IReadOnlyCollection<int> WatchedShopPets(MeterSettings s) => Aion2.Protocol.Aion2MerchantPets.All.Select(p => p.PetId).Where(p => !s.BlackCloudUnwatched.Contains(p)).ToList();

    /// <summary>
    /// A page of the Black Cloud shop came (raised on the capturing thread, run on the UI thread): the pop-up with every ticked pet of the merchant's list the
    /// shop offers the character now - all the pages read since the offers renewed, each pet once at its lowest price - with pictures, names, groups and
    /// prices (<see cref="BlackCloudToast"/>). It stays up while the shop is open, so turning to another tab adds that tab's pets.
    /// </summary>
    private void OnBlackCloudOffered(string character, IReadOnlyList<Aion2.BlackCloudWanted> offered)
    {
        MeterSettings s = MeterSettings.Load();
        if (!s.BlackCloudPopup)
        {
            return;
        }

        BlackCloudToast.Show(character, ToastPets(offered, WatchedShopPets(s)));
    }

    /// <summary>The pets of the pop-up: the ticked ones of the merchant's list among those on offer, by group (field boss, dungeon, chests, special), then name.</summary>
    internal static IReadOnlyList<BlackCloudToastPet> ToastPets(IEnumerable<Aion2.BlackCloudWanted> offered, IReadOnlyCollection<int> watched)
    {
        var ticked = watched.ToHashSet();
        string language = LocalizationManager.Instance.Language;
        return offered.Where(w => ticked.Contains(w.PetId) && Aion2.Protocol.Aion2MerchantPets.Is(w.PetId))
            .GroupBy(w => w.PetId).Select(g => new BlackCloudToastPet(g.Key, g.Where(w => w.Price > 0).Select(w => w.Price).DefaultIfEmpty(0).Min(), Aion2.Protocol.Aion2MerchantPets.Of(g.Key)?.Kind))
            .OrderBy(p => p.Kind).ThenBy(p => Aion2.Protocol.Aion2Pets.PetName(p.PetId, language) ?? "", StringComparer.CurrentCulture).ToList();
    }

    private DateTime _blackCloudCheckAt = DateTime.MinValue;

    /// <summary>A few times a second (the gate's tick), at most every 5 s: the merchant's offers renewed for characters that still need a ticked pet - one note
    /// over the game naming them (the game sends the new offers only when the shop is opened). Each renewal once; only while the game is in front.</summary>
    private void AnnounceBlackCloudRenewal()
    {
        if (DateTime.UtcNow - _blackCloudCheckAt < TimeSpan.FromSeconds(5) || !GameWindow.OverlayMayShow())
        {
            return;
        }

        _blackCloudCheckAt = DateTime.UtcNow;
        MeterSettings s = _gateSettings;
        if (!s.BlackCloudRenewNote)
        {
            return;
        }

        var due = Aion2.Aion2BlackCloud.DueRenewals(DateTime.UtcNow, WatchedShopPets(s));
        if (due.Count == 0 || CubeToast.Active)
        {
            return;
        }

        LocalizationManager loc = LocalizationManager.Instance;
        CubeToast.Note(string.Format(System.Globalization.CultureInfo.CurrentCulture, loc["BlackCloud.Renewed"],
            string.Join(", ", due.Select(d => $"{d.Name} ({d.Wanted})"))), 10);
        Aion2.Aion2BlackCloud.MarkAnnounced(due.Select(d => d.Name));
    }

    /// <summary>Every overlay that is on a little smaller or bigger (each within its own limits).</summary>
    private void ScaleAllOverlays(double factor)
    {
        foreach (OverlayEntry e in LayoutEntries())
        {
            e.Place.PlaceScaleTo(e.Place.PlaceScale * factor, true);
        }
    }

    /// <summary>Every overlay back at the place, size and scale it started with.</summary>
    private void ResetAllOverlayPlaces()
    {
        foreach (string title in new[] { OverlayPlaces.Assist, OverlayPlaces.Effects, OverlayPlaces.Network, OverlayPlaces.Dummy, OverlayPlaces.WorldBoss, OverlayPlaces.Hotkeys, OverlayPlaces.Timetable,
                     OverlayPlaces.PetMap, OverlayPlaces.PetList, OverlayPlaces.PetInfo, OverlayPlaces.Loot, OverlayPlaces.Gather, OverlayPlaces.Season, OverlayPlaces.Craft, OverlayPlaces.BossCast, OverlayPlaces.BossHp, OverlayPlaces.TargetOfTarget })
        {
            OverlayPlaces.Reset(title);
        }

        var s = MeterSettings.Load();
        s.OverlayScale = 1.0;
        s.Save();
        SetCompactOverlayScale(1.0);
        ResetMeterOverlayPlace();
        AssistController.ResetCooldownPlaces(); // the skills' own windows: back in a column
    }

    /// <summary>The layout of the pictures the build makes (no overlay windows there): a few overlays on one 1920 × 1080 monitor.</summary>
    private IReadOnlyList<OverlayEntry> SampleLayout()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        _sampleLayout ??= new List<OverlayEntry>
        {
            new(loc["Shell.OvMeter"], new SamplePlace(new Rect(1480, 260, 380, 300))),
            new(loc["Timers.Title"], new SamplePlace(new Rect(1560, 40, 300, 170))),
            new(loc["Settings.Combat.CardBossCast"], new SamplePlace(new Rect(720, 170, 486, 152))),
            new(loc["Main.MenuTools.Assist"], new SamplePlace(new Rect(60, 640, 320, 140))),
            new(loc["Shell.OvPetFarm"], new SamplePlace(new Rect(860, 860, 200, 60))),
        };
        return _sampleLayout;
    }

    private List<OverlayEntry>? _sampleLayout;

    private sealed class SamplePlace(Rect bounds) : IOverlayPlace
    {
        private Rect _bounds = bounds;
        private double _scale = 1;

        public bool PlaceIsView => false;

        public Rect PlaceBounds => _bounds;

        public double PlaceScale => _scale;

        public void PlaceAt(double left, double top, bool save) => _bounds = new Rect(left, top, _bounds.Width, _bounds.Height);

        public void PlaceScaleTo(double scale, bool save)
        {
            _bounds = new Rect(_bounds.Left, _bounds.Top, _bounds.Width / _scale * scale, _bounds.Height / _scale * scale);
            _scale = scale;
        }
    }

    /// <summary>The meter overlay back at its first place (right of the middle of the main screen).</summary>
    private void ResetMeterOverlayPlace()
    {
        var settings = MeterSettings.Load();
        settings.MeterOverlayLeft = null;
        settings.MeterOverlayTop = null;
        settings.Save();
        _meterOverlay?.PlaceFromSettings();
        _overlayPeekUntil = DateTime.Now + _meterOverlayHideAfter; // shown for a moment, so it can be seen where it is now
        UpdateMeterOverlay();
    }

    // ---- small parts ----

    private static UIElement PageTitle(string title, string lede)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        TextBlock head = ShellText(title, "Brush.Text", 24);
        head.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Title");
        stack.Children.Add(head);
        TextBlock sub = Wrapped(lede, "Brush.TextMuted", 13);
        sub.Margin = new Thickness(0, 4, 0, 0);
        sub.MaxWidth = 760;
        sub.HorizontalAlignment = HorizontalAlignment.Left;
        stack.Children.Add(sub);
        return stack;
    }

    /// <summary>A card of Home: a small title with an arrow to its section, the content under it.</summary>
    private Border HomeCard(string? title, UIElement content, string? opens, double width)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var stack = new StackPanel();
        if (title is not null)
        {
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            if (opens is not null)
            {
                TextBlock more = ShellLink(loc["Shell.Open"], () => ShowShellPage(opens));
                DockPanel.SetDock(more, Dock.Right);
                head.Children.Add(more);
            }

            head.Children.Add(ShellText(title.ToUpperInvariant(), "Brush.TextMuted", 11, bold: true));
            stack.Children.Add(head);
        }

        stack.Children.Add(content);
        var card = new Border
        {
            Child = stack, Padding = new Thickness(16, 12, 16, 14), Margin = new Thickness(0, 0, 12, 12), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top,
        };
        if (width > 0)
        {
            card.Width = width;
        }

        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return card;
    }

    private static TextBlock ShellText(string text, string brush, double size, bool bold = false)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    private static TextBlock Wrapped(string text, string brush, double size)
    {
        TextBlock block = ShellText(text, brush, size);
        block.TextWrapping = TextWrapping.Wrap;
        return block;
    }

    private static TextBlock ShellLink(string text, Action act)
    {
        TextBlock link = ShellText(text, "Brush.Accent", 12.5, bold: true);
        link.Cursor = Cursors.Hand;
        link.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            act();
        };
        return link;
    }

    private static string ShortSpan(TimeSpan left)
    {
        left = left < TimeSpan.Zero ? TimeSpan.Zero : left;
        return left.TotalDays >= 1 ? $"{(int)left.TotalDays} d {left.Hours} h" : left.TotalHours >= 1 ? $"{(int)left.TotalHours} h {left.Minutes} m" : $"{Math.Max(1, (int)Math.Ceiling(left.TotalMinutes))} m";
    }

    private static string Ago(TimeSpan ago) => string.Format(CultureInfo.CurrentCulture, LocalizationManager.Instance["Checklist.Ago"], ShortSpan(ago));

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // no browser to open it with
        }
    }
}
