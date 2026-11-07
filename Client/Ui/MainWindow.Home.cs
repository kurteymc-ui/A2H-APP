using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;
using AionDPS.Combat;
using AionDPS.History;
using AionDPS.Schedule;
using AionDPS.Upload;

namespace AionDPS.Ui;

/// <summary>
/// Home: what needs doing first (the game not read yet, an update waiting), a greeting with the character, the resets and a tip (MainWindow.HomeHelp.cs),
/// the news (MainWindow.News.cs), what is worth setting up first, then the four things a player looks at every day: the characters' Odyle Energy, the
/// last fight, the next events and the pet collection. Everything else has its page.
/// </summary>
public partial class MainWindow
{
    private UIElement BuildHomePage()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        DateTime now = DateTime.Now, utcNow = DateTime.UtcNow;
        var page = new StackPanel { Margin = new Thickness(24, 20, 24, 24), MaxWidth = 1120, HorizontalAlignment = HorizontalAlignment.Stretch };

        // what needs doing first
        foreach (UIElement notice in HomeNotices())
        {
            page.Children.Add(notice);
        }

        page.Children.Add(HomeGreeting(now));

        // the characters' Odyle Energy: who is full, who is next
        string? online = Aion2Tickets.CurrentName;
        var odyle = Aion2Tickets.OnHomeServer().Where(c => c.Tickets.Count > 0)
            .Select(c => (Character: c, State: Aion2Tickets.OdyleNow(c, utcNow, c.Name == online))).Where(x => x.State is not null)
            .OrderBy(x => x.State!.Full ? DateTime.MinValue : x.State.FullAt ?? DateTime.MaxValue).Take(8).ToList();
        var energy = new StackPanel();
        if (odyle.Count == 0)
        {
            energy.Children.Add(Wrapped(loc["Shell.NoCharacters"], "Brush.TextMuted", 12.5));
        }

        foreach (var (character, state) in odyle)
        {
            energy.Children.Add(OdyleRow(character, state!, utcNow));
        }

        // first what is due (5 November 2026, the owner: the Odyle Energy and the next events at the top, under the greeting, then the news), the two as high
        page.Children.Add(HomeRow(HomeCard(loc["Shell.Odyle"], energy, "characters", 0), HomeCard(loc["Home.NextEvents"], NextEvents(now), "timetable", 0), stretch: true));

        // the news (the game's official patch notes and notices, with our write-ups) and what is worth setting up first
        if (Headless || !PrivacyConsent.Restricted)
        {
            page.Children.Add(NewsBanner(HomeNews()));
        }

        if (HomeSetupCard() is { } setup)
        {
            page.Children.Add(setup);
        }

        StartHomeSlides();

        // the last fight, big, and the ones before it; the pet collection
        IReadOnlyList<FightLogEntry> entries = FightLog.Entries();
        var fights = new StackPanel();
        if (entries.Count == 0)
        {
            fights.Children.Add(Wrapped(loc["Shell.NoFights"], "Brush.TextMuted", 12.5));
        }
        else
        {
            fights.Children.Add(LastFight(entries[0], now));
            foreach (FightLogEntry entry in entries.Skip(1).Take(3))
            {
                fights.Children.Add(FightRow(entry, now));
            }
        }

        page.Children.Add(HomeRow(HomeCard(loc["Home.LastFight"], fights, "history", 0), HomeCard(loc["Home.Pets"], PetCollection(), "petmap.pets", 0), stretch: false));

        // rebuilt every 30 seconds: it stays where it was scrolled to
        double kept = _homeViewer is { } old && AppPageHost.Children.Contains(old) ? old.VerticalOffset : 0;
        var viewer = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };
        if (kept > 0)
        {
            viewer.Loaded += (_, _) => viewer.ScrollToVerticalOffset(kept);
        }

        _homeViewer = viewer;
        return viewer;
    }

    private ScrollViewer? _homeViewer;

    /// <summary>Two cards side by side, three fifths and two fifths; <paramref name="stretch"/>: both as high as the higher one.</summary>
    private static UIElement HomeRow(Border left, Border right, bool stretch)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star), MinWidth = 380 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MinWidth = 280 });
        if (stretch)
        {
            left.VerticalAlignment = right.VerticalAlignment = VerticalAlignment.Stretch;
        }

        Grid.SetColumn(right, 1);
        row.Children.Add(left);
        row.Children.Add(right);
        return row;
    }

    /// <summary>The notices at the top: only what the player has to do something about.</summary>
    private IEnumerable<UIElement> HomeNotices()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        if (Headless)
        {
            yield break;
        }

        if (_clientOutdated)
        {
            yield return Notice("Brush.Warning", "", loc["Home.Outdated"], loc["Home.Update"], () => OnUpdateNoticeClicked(this, null!));
        }
        else if (_downloadedUpdate is not null)
        {
            yield return Notice("Brush.Accent", "", string.Format(CultureInfo.CurrentCulture, loc["Home.UpdateReady"], _downloadedUpdate.TargetFullRelease.Version), loc["Home.Restart"],
                () => OnUpdateNoticeClicked(this, null!));
        }

        bool reading = (_source as Aion2PacketCombatSource)?.ServerFingerprint is not null;
        if (PrivacyConsent.Restricted)
        {
            yield break;
        }

        if (!reading && Aion2.Capture.GameConnections.GameRunning())
        {
            // started after the game: the character is sent at the login, so a trip to the character selection lets the meter read it
            yield return Notice("Brush.Warning", "", loc["Home.GameAlready"], null, null);
        }
        else if (reading && CompactOverlayOwnName() is null)
        {
            yield return Notice("Brush.Accent", "", loc["Home.NoCharacterYet"], null, null);
        }
    }

    private UIElement Notice(string brush, string glyph, string text, string? action, Action? act)
    {
        var dock = new DockPanel();
        TextBlock icon = ShellText(glyph, brush, 15);
        icon.FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        icon.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(icon, Dock.Left);
        dock.Children.Add(icon);
        if (action is not null && act is not null)
        {
            var button = new Button { Content = action, Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            button.Click += (_, _) => act();
            DockPanel.SetDock(button, Dock.Right);
            dock.Children.Add(button);
        }

        dock.Children.Add(Wrapped(text, "Brush.Text", 13));
        var box = new Border { Child = dock, Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 12, 10), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1, 1, 1, 1) };
        box.SetResourceReference(Border.BorderBrushProperty, brush);
        box.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        return box;
    }

    /// <summary>The greeting: the time of day and the character the game is read for (else the account), its class and server, whether the game
    /// is being read, and the two resets.</summary>
    private UIElement HomeGreeting(DateTime now)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var directory = _source?.Entities as Aion2EntityDirectory;
        Aion2CharacterInfo? me = directory?.LocalCharacter;
        string? name = Headless ? "Aerin" : me?.Name ?? Aion2Tickets.CurrentName ?? (Account.SignedIn ? Account.Name : null);
        string hello = loc[now.Hour is >= 5 and < 12 ? "Home.Morning" : now.Hour is >= 12 and < 18 ? "Home.Afternoon" : "Home.Evening"];
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var who = new StackPanel();
        TextBlock head = ShellText(name is null ? hello : string.Format(CultureInfo.CurrentCulture, loc["Home.Hello"], hello, name), "Brush.Text", 24);
        ThemeManager.UseTitleFont(head);
        who.Children.Add(head);

        // the character line and whether the game is being read
        bool reading = Headless || (_source as Aion2PacketCombatSource)?.ServerFingerprint is not null;
        var state = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        state.Children.Add(new Border
        {
            Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
            Background = reading ? new SolidColorBrush(Color.FromRgb(0x4C, 0xC8, 0x78)) : Brushes.Gray,
        });
        var parts = new List<string> { loc[reading ? "Home.Reading" : "Home.Waiting"] };
        if (Headless)
        {
            parts.Add("Gladiator · Lv 45");
        }
        else if (me is not null)
        {
            string? cls = Aion2SkillNames.ClassFromCode(me.ClassCode);
            parts.Add(string.Join(" · ", new[] { cls, me.Level > 0 ? "Lv " + me.Level : null, me.ServerId > 0 && Aion2Servers.IsKnown(me.ServerId) ? Aion2Servers.NameOf(me.ServerId) : null }.Where(p => p is { Length: > 0 })));
        }

        state.Children.Add(ShellText(string.Join("  ·  ", parts.Where(p => p.Length > 0)), "Brush.TextMuted", 12.5));
        who.Children.Add(state);
        // signing in, and the first-start setup again
        var links = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        if (!Account.SignedIn && !Headless && !PrivacyConsent.Restricted)
        {
            links.Children.Add(ShellLink(loc["Shell.SignIn"], () => OnAccountClicked(this, new RoutedEventArgs())));
            links.Children.Add(ShellText("  ·  ", "Brush.TextSubtle", 12.5));
        }

        TextBlock again = ShellLink(loc["Home.SetupAgain"], ShowLookSetup);
        again.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        links.Children.Add(again);
        who.Children.Add(links);

        grid.Children.Add(who);

        // the resets, small on the right
        var resets = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (Aion2Tickets.Find(4) is { } daily && Aion2Tickets.NextReset(daily, now) is { } nextDaily)
        {
            resets.Children.Add(ResetBox(loc["Checklist.DailyReset"], ShortSpan(nextDaily - now), nextDaily.ToString("t", CultureInfo.CurrentCulture)));
        }

        if (Aion2Tickets.Find(101) is { } weekly && Aion2Tickets.NextReset(weekly, now) is { } nextWeekly)
        {
            resets.Children.Add(ResetBox(loc["Checklist.WeeklyReset"], ShortSpan(nextWeekly - now),
                nextWeekly.ToString("ddd", CultureInfo.CurrentCulture) + " " + nextWeekly.ToString("t", CultureInfo.CurrentCulture)));
        }

        Grid.SetColumn(resets, 1);
        grid.Children.Add(resets);

        // a tip at the bottom, one after the other
        var inside = new StackPanel();
        inside.Children.Add(grid);
        if (HomeTipRow() is { } tips)
        {
            var line = new Border { Height = 1, Margin = new Thickness(0, 12, 4, 0) };
            line.SetResourceReference(Border.BackgroundProperty, "Brush.Border");
            inside.Children.Add(line);
            ((FrameworkElement)tips).Margin = new Thickness(0, 10, 0, 0);
            inside.Children.Add(tips);
        }

        var card = new Border { Child = inside, Padding = new Thickness(20, 16, 16, 16), Margin = new Thickness(0, 0, 12, 12), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        card.Background = new LinearGradientBrush(Color.FromArgb(0x30, 0xDC, 0xB8, 0x77), Color.FromArgb(0x00, 0xDC, 0xB8, 0x77), 0);
        var host = new Grid();
        var under = new Border { CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 12, 12) };
        under.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        host.Children.Add(under);
        host.Children.Add(card);
        return host;
    }

    private static UIElement ResetBox(string label, string left, string at)
    {
        var stack = new StackPanel { Margin = new Thickness(18, 0, 6, 0) };
        stack.Children.Add(ShellText(label, "Brush.TextMuted", 11.5));
        stack.Children.Add(ShellText(left, "Brush.Text", 18, bold: true));
        stack.Children.Add(ShellText(at, "Brush.TextSubtle", 11));
        return stack;
    }

    /// <summary>The newest fight in big: what it was, how it ended and the own rate; a click opens its report.</summary>
    private UIElement LastFight(FightLogEntry entry, DateTime now)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10), Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();
        TextBlock title = ShellText(entry.Title, "Brush.Text", 17, bold: true);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        left.Children.Add(title);
        string state = entry.Kind == FightLogKind.Boss ? loc[entry.Killed == true ? "Home.Killed" : "Home.Wipe"]
            : entry.Kind == FightLogKind.Dummy ? loc["Home.Dummy"] : entry.Kind.ToString();
        string line = string.Join(" · ", new[] { state, $"{TimeSpan.FromSeconds(entry.Seconds):m\\:ss}", Ago(now - entry.Start), entry.Detail }.Where(p => p is { Length: > 0 }));
        TextBlock sub = ShellText(line, entry.Kind == FightLogKind.Boss && entry.Killed != true ? "Brush.Warning" : "Brush.TextMuted", 12);
        sub.Margin = new Thickness(0, 3, 0, 0);
        left.Children.Add(sub);
        grid.Children.Add(left);
        if (entry.YourDps > 0)
        {
            var rate = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            TextBlock big = ShellText(PlayerRow.Compact((long)entry.YourDps), "Brush.Accent", 22, bold: true);
            big.HorizontalAlignment = HorizontalAlignment.Right;
            rate.Children.Add(big);
            TextBlock unit = ShellText(loc["Home.YourDps"], "Brush.TextSubtle", 11);
            unit.HorizontalAlignment = HorizontalAlignment.Right;
            rate.Children.Add(unit);
            Grid.SetColumn(rate, 1);
            grid.Children.Add(rate);
        }

        grid.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            OpenHistoryEntry(entry);
        };
        var box = new Border { Child = grid, Padding = new Thickness(12, 10, 12, 0), Margin = new Thickness(-4, 0, -4, 6), CornerRadius = new CornerRadius(6) };
        box.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
        return box;
    }

    /// <summary>The events on now and the next ones (the timetable's events), at most five.</summary>
    private static UIElement NextEvents(DateTime now)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var stack = new StackPanel();
        var shown = new List<(EventOccurrence Occurrence, bool On)>();
        try
        {
            foreach (ScheduledEvent scheduled in EventSchedule.Events.Where(e => !e.Always))
            {
                EventOccurrence? on = EventSchedule.Upcoming(scheduled, now).FirstOrDefault(o => o.IsActiveAt(now));
                if (on is not null)
                {
                    shown.Add((on, true));
                }
                else if (EventSchedule.NextAfter(scheduled, now) is { } next)
                {
                    shown.Add((next, false));
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
        {
            // a timetable that cannot be read: the card says there is nothing
        }

        var list = shown.OrderByDescending(s => s.On).ThenBy(s => s.On ? s.Occurrence.End : s.Occurrence.Start).Take(5).ToList();
        if (list.Count == 0)
        {
            stack.Children.Add(Wrapped(loc["Home.NoEvents"], "Brush.TextMuted", 12.5));
        }

        foreach (var (occurrence, on) in list)
        {
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            string name = occurrence.Event.NameIn(loc.Language) + (occurrence.Event.IsPossible ? " " + loc["Home.Possible"] : "");
            TextBlock what = ShellText(name, "Brush.Text", 13);
            what.TextTrimming = TextTrimming.CharacterEllipsis;
            grid.Children.Add(what);
            TextBlock when = on
                ? ShellText(string.Format(CultureInfo.CurrentCulture, loc["Home.EventOn"], ShortSpan(occurrence.End - now)), "Brush.Heal", 12, bold: true)
                : ShellText(string.Format(CultureInfo.CurrentCulture, loc["Home.EventIn"], ShortSpan(occurrence.Start - now), occurrence.Start.ToString("t", CultureInfo.CurrentCulture)),
                    occurrence.Start - now < TimeSpan.FromMinutes(30) ? "Brush.Warning" : "Brush.TextMuted", 12, bold: occurrence.Start - now < TimeSpan.FromMinutes(30));
            when.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(when, 1);
            grid.Children.Add(when);
            stack.Children.Add(grid);
        }

        return stack;
    }

    /// <summary>The pet collection as the game sent it last: how many pets, how many at the top level, and whether the map follows it.</summary>
    private UIElement PetCollection()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var stack = new StackPanel();
        IReadOnlyList<Aion2PetState> pets = (_source?.Entities as Aion2EntityDirectory)?.LocalPetStates is { Count: > 0 } live ? live
            : (Aion2CharacterStore.Load(Aion2CharacterStore.DefaultPath)?.Pets ?? new()).Select(p => new Aion2PetState(p.Id, p.Level, p.Progress)).ToList();
        int all = Aion2Pets.AllPetIds.Count;
        if (Headless && pets.Count == 0)
        {
            pets = Aion2Pets.AllPetIds.Take(143).Select((id, i) => new Aion2PetState(id, i % 4 == 0 ? Aion2Pets.TopLevel : 1 + i % 3, 0)).ToList();
        }

        if (pets.Count == 0)
        {
            stack.Children.Add(Wrapped(loc["Settings.PetMap.CollectionNone"], "Brush.TextMuted", 12.5));
        }
        else
        {
            int top = pets.Count(p => p.Level >= Aion2Pets.TopLevel);
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(ShellText(pets.Count.ToString(CultureInfo.CurrentCulture), "Brush.Text", 22, bold: true));
            TextBlock of = ShellText(" / " + all.ToString(CultureInfo.CurrentCulture) + "  " + loc["Home.PetsOwned"], "Brush.TextMuted", 12.5);
            of.VerticalAlignment = VerticalAlignment.Bottom;
            of.Margin = new Thickness(0, 0, 0, 4);
            line.Children.Add(of);
            stack.Children.Add(line);
            var bars = new Grid { Height = 6, Margin = new Thickness(0, 6, 0, 8) };
            var track = new Border { CornerRadius = new CornerRadius(3) };
            track.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
            bars.Children.Add(track);
            bars.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(top, GridUnitType.Star) });
            bars.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, pets.Count - top), GridUnitType.Star) });
            bars.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, all - pets.Count), GridUnitType.Star) });
            Grid.SetColumnSpan(track, 3);
            var topBar = new Border { CornerRadius = new CornerRadius(3, 0, 0, 3) };
            topBar.SetResourceReference(Border.BackgroundProperty, "Brush.Accent");
            bars.Children.Add(topBar);
            var ownedBar = new Border { Opacity = 0.45 };
            ownedBar.SetResourceReference(Border.BackgroundProperty, "Brush.Accent");
            Grid.SetColumn(ownedBar, 1);
            bars.Children.Add(ownedBar);
            stack.Children.Add(bars);
            stack.Children.Add(ShellText(string.Format(CultureInfo.CurrentCulture, loc["Home.PetsTop"], top), "Brush.TextMuted", 12));
        }

        MeterSettings settings = MeterSettings.Load();
        TextBlock map = Wrapped(loc[settings.PetMapAuto ? "Home.PetsMapAuto" : "Home.PetsMapPicked"], "Brush.TextSubtle", 11.5);
        map.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(map);
        return stack;
    }
}
