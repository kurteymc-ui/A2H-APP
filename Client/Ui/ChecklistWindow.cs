using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AionDPS.Aion2;
using AionDPS.Data;

namespace AionDPS.Ui;

/// <summary>
/// The checklist (Tools menu, no overlay): the Odyle energy and the daily and weekly counters of a character - entry counts, reward counts, boss
/// kills, access times - as the game reports them (see <see cref="Aion2Tickets"/>), with the next refill of each. The numbers are those of the last
/// login or change the meter saw; "~" marks a number that was worked out from the refills since then. Built in code, in the look of the other windows.
/// </summary>
public sealed partial class ChecklistWindow : Window
{
    private readonly StackPanel _characters = new();
    private readonly TextBlock _seen = new();
    private readonly StackPanel _body = new();
    /// <summary>Not in the daily list (the user's choice): the reward counts of the conquest dungeons (alone and in pairs) and the Shugofesta keys.</summary>
    private static readonly HashSet<int> NotDaily = new()
    {
        60000003, 60000004, 60000005, 60000101, 60001201, 60002201, 60003201, 60007201, 60009201, 1, 7,
    };

    /// <summary>The access times shown: the three levels of the Abyss and the Spacetime Rift.</summary>
    private static readonly int[] TimeTickets = { 3, 9, 13, 6 };

    /// <summary>The daily part after the mandatory tasks (refilled at every daily reset): Shugo Festival, Dimensional Invasion, Nightmare, the two daily dungeons.</summary>
    private static readonly int[] DailyTickets = { 1, 8, 4, 102, 103 };

    /// <summary>The weekly part after the daily dungeon's entries (refilled on Wednesday): Ascension Trial, Subjugation.</summary>
    private static readonly int[] WeeklyTickets = { 80000001, 70000001 };

    /// <summary>
    /// The dungeons' reward counters the game reports, in blocks under the game's own category (the "[Conquest]" in front of its names): Conquest, Transcendence,
    /// Ordeal, the cubes not yet taken, the Sanctuary raids. Every character's login carries all of them (the owner's four characters, 3 November 2026).
    /// </summary>
    private static readonly int[][] DungeonBlocks =
    {
        new[] { 60000006, 60000007 },
        new[] { 60000002, 60005301, 60006301, 60000008 },
        new[] { 60015101 },
        new[] { 60000010, 60000009 },
        new[] { 90000001, 90000002, 90000003, 90000004, 90000005, 90000006 },
    };

    /// <summary>The weekly counter of the daily dungeon (the unknown fissure's entry count).</summary>
    private const int DailyDungeonTicket = 101;

    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(30) };

    /// <summary>The season missions change with every kill: the page is drawn again a second after the last change, not at each.</summary>
    private readonly DispatcherTimer _seasonDelay = new() { Interval = TimeSpan.FromSeconds(1) };
    private string? _selected;

    /// <summary>The character the season card shows, picked in its box of characters (null: the page's character); forgotten when the page changes.</summary>
    private string? _seasonCharacter;
    private string? _seasonPage;

    /// <summary>The season card's list: the weekly missions (true) or the season-long ones.</summary>
    private bool _seasonWeekly = true;

    /// <summary>The season card's category chip: one of the game's categories, or null for all of them.</summary>
    private string? _seasonCategory;

    /// <summary>Whether the season card shows its finished missions (folded away by default).</summary>
    private bool _seasonShowFinished;

    /// <summary>Whether the shown character has the Quai premium membership (true when it is not known: the caps then are the member's, as before).</summary>
    private bool _premium = true;

    public ChecklistWindow()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        Title = loc["Checklist.Title"];
        Width = 1080; // the roster of all characters is a wide table
        SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        MinWidth = 640;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(12, 10, 12, 12) };
        UIElement pageHead = PageHead();
        DockPanel.SetDock(pageHead, Dock.Top);
        root.Children.Add(pageHead);
        // the characters as a column of cards on the left (Stacy's fork has the idea: class icon, name in the class colour, class and level below)
        var sidebar = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Width = 200, Margin = new Thickness(0, 0, 12, 0), Content = _characters };
        DockPanel.SetDock(sidebar, Dock.Left);
        root.Children.Add(sidebar);
        // the "Stand: date" line is no longer shown (_seen is still kept up to date)

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _body };
        root.Children.Add(scroll);
        Content = root;
        ThemedChrome.Apply(this);

        Aion2Tickets.Changed += OnTicketsChanged;
        Aion2Achievements.Changed += OnAchievementsChanged;
        Aion2Supply.Changed += OnAchievementsChanged; // the supply requests and the inventories: drawn again a second after the last change
        Aion2Inventories.Changed += OnAchievementsChanged;
        // not while the inventory's search box is being typed in: drawing the page again would take its focus
        _tick.Tick += (_, _) =>
        {
            if (!Typing)
            {
                Render();
            }
        };
        _tick.Start();
        _seasonDelay.Tick += (_, _) =>
        {
            if (Typing)
            {
                return; // tried again with the next change or the next tick
            }

            _seasonDelay.Stop();
            if (_refreshWaiting)
            {
                _refreshWaiting = false;
                Refresh();
            }
            else
            {
                Render();
            }
        };
        Closed += (_, _) =>
        {
            _tick.Stop();
            _seasonDelay.Stop();
            Aion2Tickets.Changed -= OnTicketsChanged;
            Aion2Achievements.Changed -= OnAchievementsChanged;
            Aion2Supply.Changed -= OnAchievementsChanged;
            Aion2Inventories.Changed -= OnAchievementsChanged;
        };
        Refresh();
    }

    // a ticket change while the inventory search has the keyboard: the rebuild waits until the box is left (3 November 2026 review: every login
    // frame rebuilt the page and threw the text being typed away)
    private bool _refreshWaiting;

    /// <summary>A text box of this window has the keyboard (another window's does not hold this one's drawing back).</summary>
    private bool Typing => IsKeyboardFocusWithin && Keyboard.FocusedElement is TextBox;

    private void OnTicketsChanged() => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (Typing)
        {
            _refreshWaiting = true;
            if (!_seasonDelay.IsEnabled)
            {
                _seasonDelay.Start();
            }

            return;
        }

        Refresh();
    }));

    private void OnAchievementsChanged() => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (!_seasonDelay.IsEnabled)
        {
            _seasonDelay.Start();
        }
    }));

    /// <summary>Rebuilds the character list (the one logged in now is chosen first) and the page.</summary>
    private void Refresh()
    {
        IReadOnlyList<TicketCharacter> all = Aion2Tickets.OnHomeServer(); // the server played on now only
        string? current = Aion2Tickets.CurrentName;
        // the roster of all characters first when there are two or more; a character the user picked stays picked
        bool roster = all.Count(c => c.Tickets.Count > 0) >= 2;
        string? wanted = _selected == RosterKey && roster ? RosterKey
            : _selected is not null && all.Any(c => c.Name == _selected) ? _selected
            : roster ? RosterKey
            : current is not null && all.Any(c => c.Name == current) ? current : all.FirstOrDefault()?.Name;
        _characters.Children.Clear();
        if (roster)
        {
            _characters.Children.Add(RosterCard(wanted == RosterKey, all.Count(c => c.Tickets.Count > 0), () =>
            {
                _selected = RosterKey;
                Refresh();
            }));
        }

        foreach (TicketCharacter c in all)
        {
            string name = c.Name;
            _characters.Children.Add(CharacterCard(c, name == wanted, name == current, () =>
            {
                _selected = name;
                Refresh();
            }));
        }

        _selected = wanted;
        if (_seasonPage != wanted)
        {
            _seasonPage = wanted;
            _seasonCharacter = null;
        }

        Render();
    }

    /// <summary>
    /// One character of the column on the left: the class icon, the name in the class colour (a dot when it is the one logged in), the class, level and server below and, in
    /// small type, when it was last online. The form is Stacy's (her fork, 17 October 2026: alts with close names were hard to tell apart); smaller type than hers.
    /// </summary>
    private static UIElement CharacterCard(TicketCharacter c, bool active, bool online, Action choose)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        string language = loc.Language;
        string? className = c.ClassCode > 0 ? Aion2.Protocol.Aion2SkillNames.ClassFromCode(c.ClassCode) : null;
        Color accent = className is not null ? ClassBrushes.ClassColor(className) : Resource("Brush.Accent", Color.FromRgb(0xE0, 0xB6, 0x4A));
        var lines = new StackPanel();
        var top = new DockPanel();
        if (online)
        {
            var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = loc["Checklist.OnlineNow"] };
            dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Brush.Heal");
            DockPanel.SetDock(dot, Dock.Right);
            top.Children.Add(dot);
        }

        if (className is not null && new ClassIconConverter().Convert(className, typeof(ImageSource), null, CultureInfo.CurrentCulture) is ImageSource icon)
        {
            var image = new Image { Source = icon, Width = 22, Height = 22, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(image, Dock.Left);
            top.Children.Add(image);
        }

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        TextBlock title = Label(c.Name, c.Tickets.Count > 0 ? "Brush.Text" : "Brush.TextSubtle", 13, bold: true);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        if (className is not null)
        {
            title.Foreground = ClassBrushes.Text(className);
        }

        words.Children.Add(title);
        var parts = new List<string>();
        if (c.Level > 0)
        {
            parts.Add(string.Format(CultureInfo.CurrentCulture, loc["Checklist.Level"], c.Level));
        }

        if (c.ServerId > 0)
        {
            parts.Add(Aion2.Protocol.Aion2Servers.NameOf(c.ServerId).Replace("Europe - ", "", StringComparison.Ordinal));
        }

        if (parts.Count > 0)
        {
            TextBlock info = Label(string.Join(" \u00b7 ", parts), "Brush.TextSubtle", 10.5);
            info.TextTrimming = TextTrimming.CharacterEllipsis;
            words.Children.Add(info);
        }

        top.Children.Add(words);
        lines.Children.Add(top);

        // its Odyle Energy as a thin bar (full: in the warning colour)
        if (c.Tickets.Count > 0 && Aion2Tickets.OdyleNow(c, DateTime.UtcNow, online) is { } o && o.Cap > 0)
        {
            var track = new Grid { Height = 3, Margin = new Thickness(30, 6, 2, 0),
                ToolTip = string.Format(CultureInfo.CurrentCulture, loc["Checklist.RosterOdyleLine"], o.Count, o.Cap, o.Bonus, o.PerTick) };
            var under = new Border { CornerRadius = new CornerRadius(2) };
            under.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
            track.Children.Add(under);
            var fill = new Border { CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left };
            fill.SetResourceReference(Border.BackgroundProperty, o.Full ? "Brush.Warning" : "Brush.Accent");
            double share = Math.Clamp(o.Count / (double)o.Cap, 0, 1);
            track.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * share;
            track.Children.Add(fill);
            lines.Children.Add(track);
        }

        // the legion and when it was online last, small, for the one picked or under the mouse it is all in the tool tip
        var tip = new List<string> { c.Name };
        if (className is not null)
        {
            tip.Add(ClassCatalog.DisplayName(className, language));
        }

        if (!string.IsNullOrEmpty(c.Legion))
        {
            tip.Add("<" + c.Legion + ">");
        }

        if (!online && c.LastOnlineMs > 0)
        {
            tip.Add(string.Format(CultureInfo.CurrentCulture, loc["Checklist.LastOnline"], DateTimeOffset.FromUnixTimeMilliseconds(c.LastOnlineMs).LocalDateTime.ToString("g", CultureInfo.CurrentCulture)));
        }

        var card = new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 4), BorderThickness = new Thickness(1),
            Child = lines, Cursor = System.Windows.Input.Cursors.Hand, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, ToolTip = string.Join("\n", tip),
        };
        if (active)
        {
            card.Background = Tint(accent, 0x22);
            card.BorderBrush = Tint(accent, 0x90);
        }
        else
        {
            card.MouseEnter += (_, _) => card.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
            card.MouseLeave += (_, _) => card.Background = Brushes.Transparent;
        }

        card.MouseLeftButtonDown += (_, _) => choose();
        return card;
    }

    private void Render()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        string language = loc.Language;
        _body.Children.Clear();
        if (_selected == RosterKey)
        {
            RenderRoster(language);
            return;
        }

        TicketCharacter? character = Aion2Tickets.Snapshot().FirstOrDefault(c => c.Name == _selected);
        if (character is null)
        {
            _seen.Text = "";
            _body.Children.Add(Card(Label(loc["Checklist.Empty"], "Brush.TextMuted", 13)));
            return;
        }

        if (character.Tickets.Count == 0)
        {
            // a character the login's list showed but that never logged in with the meter: its basics, and what it takes to see its counters
            _seen.Text = "";
            var listed = new StackPanel();
            listed.Children.Add(Label(character.Name, "Brush.Text", 16, bold: true));
            TextBlock relogNew = Label(loc["Checklist.RelogNew"], "Brush.Accent", 12);
            relogNew.TextWrapping = TextWrapping.Wrap;
            relogNew.Margin = new Thickness(0, 8, 0, 0);
            listed.Children.Add(relogNew);
            _body.Children.Add(Card(listed));
            return;
        }

        _premium = Aion2Tickets.HasPremium(character, DateTime.UtcNow) != false;
        DateTime now = DateTime.Now;
        DateTime seenAt = character.SavedAt.ToLocalTime();
        _seen.Text = string.Format(CultureInfo.CurrentCulture, loc["Checklist.Seen"], seenAt.ToString("g", CultureInfo.CurrentCulture));

        // who it is and how fresh its numbers are, then the parts of the page as tabs: the counters, the season missions, the supply requests, the inventory
        // (the owner, 4 November 2026: the one long page of everything was confusing)
        _body.Children.Add(Hero(character));
        IReadOnlyList<Section> sections = SectionsOf(character);
        if (!sections.Contains(_section))
        {
            _section = Section.Counters;
        }

        if (sections.Count > 1)
        {
            _body.Children.Add(SectionStrip(sections));
        }

        switch (_section)
        {
            case Section.Season:
                AddSeason(character, language);
                return;
            case Section.Supply:
                AddSupply(character, language);
                return;
            case Section.Inventory:
                AddInventory(character.Name);
                return;
        }

        bool odyleLocked = Aion2Tickets.IsLocked(character, Aion2Tickets.OdyleId);
        if ((odyleLocked || character.Tickets.ContainsKey(Aion2Tickets.OdyleId)) && Aion2Tickets.Find(Aion2Tickets.OdyleId) is { } odyleDef)
        {
            _body.Children.Add(OdyleCard(odyleDef, odyleLocked ? default : character.Tickets[Aion2Tickets.OdyleId], language, odyleLocked, _premium, character.Name));
        }

        // what is left today and this week side by side, the dungeons, then the memberships and passes: the counters in the order a day's play goes through them
        _warned = false;
        AddPair(Take(() => AddDaily(loc["Checklist.Daily"], character, seenAt, now, language)), Take(() => AddWeekly(loc["Checklist.Weekly"], character, seenAt, now, language)));
        AddDungeons(loc["Checklist.Dungeons"], character, seenAt, now, language);
        AddPair(Take(() => AddSubscriptions(loc["Checklist.Subs"], TimeTickets.Select(id => Aion2Tickets.Find(id)).Where(d => d is not null && (character.Tickets.ContainsKey(d.Id) || Aion2Tickets.IsLocked(character, d.Id))).Select(d => d!), character, language)),
            Take(() => AddPass(loc["Checklist.DaevaPass"], character, language)));

        var note = Label(loc["Checklist.Note"], "Brush.TextSubtle", 11);
        note.TextWrapping = TextWrapping.Wrap;
        note.Margin = new Thickness(4, 4, 4, 0);
        _body.Children.Add(note);
        if (_warned)
        {
            var legend = Label(loc["Checklist.WarnLegend"], "Brush.TextSubtle", 11);
            legend.TextWrapping = TextWrapping.Wrap;
            legend.Margin = new Thickness(4, 2, 4, 0);
            _body.Children.Add(legend);
        }
    }

    // ---- one character: its header and its parts ----

    /// <summary>The parts of a character's page, one at a time.</summary>
    internal enum Section
    {
        Counters,
        Season,
        Supply,
        Inventory,
    }

    private Section _section = Section.Counters;

    /// <summary>A warning triangle was drawn on the page (its legend then goes under it).</summary>
    private bool _warned;

    /// <summary>The parts there is something for: the counters always, the others once the meter has read them.</summary>
    private static IReadOnlyList<Section> SectionsOf(TicketCharacter character)
    {
        var list = new List<Section> { Section.Counters };
        if (SeasonMissions.All.Count > 0)
        {
            list.Add(Section.Season);
        }

        if (Aion2Supply.Requests.Count > 0)
        {
            list.Add(Section.Supply);
        }

        if (Aion2Inventories.Characters().Any(c => c.Name == character.Name))
        {
            list.Add(Section.Inventory);
        }

        return list;
    }

    /// <summary>The parts of the page as a row of pills; the chosen one raised.</summary>
    private UIElement SectionStrip(IReadOnlyList<Section> sections)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var segments = new Border { CornerRadius = new CornerRadius(15), Padding = new Thickness(3), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 8) };
        segments.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
        segments.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (Section section in sections)
        {
            string caption = section switch
            {
                Section.Season => loc["Checklist.Season"],
                Section.Supply => loc["Checklist.Supply"],
                Section.Inventory => loc["Checklist.Tab.Inventory"],
                _ => loc["Checklist.Tab.Counters"],
            };
            bool active = section == _section;
            var pill = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 4, 16, 5), Child = Label(caption, active ? "Brush.Text" : "Brush.TextMuted", 12.5, bold: true) };
            if (active)
            {
                pill.SetResourceReference(Border.BackgroundProperty, "Brush.ControlRaised");
            }
            else
            {
                Section chosen = section;
                pill.Background = Brushes.Transparent;
                pill.Cursor = Cursors.Hand;
                pill.MouseEnter += (_, _) => pill.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
                pill.MouseLeave += (_, _) => pill.Background = Brushes.Transparent;
                pill.MouseLeftButtonDown += (_, e) =>
                {
                    _section = chosen;
                    e.Handled = true;
                    Render();
                };
            }

            line.Children.Add(pill);
        }

        segments.Child = line;
        return segments;
    }

    /// <summary>Runs one of the Add… parts and takes what it added to the page back out (null: it added nothing), to be placed by the caller.</summary>
    private UIElement? Take(Action add)
    {
        int before = _body.Children.Count;
        add();
        if (_body.Children.Count == before)
        {
            return null;
        }

        var taken = _body.Children.Cast<UIElement>().Skip(before).ToList();
        foreach (UIElement e in taken)
        {
            _body.Children.Remove(e);
        }

        if (taken.Count == 1)
        {
            return taken[0];
        }

        var stack = new StackPanel();
        foreach (UIElement e in taken)
        {
            stack.Children.Add(e);
        }

        return stack;
    }

    /// <summary>Two cards side by side (one alone takes the whole width).</summary>
    private void AddPair(UIElement? left, UIElement? right)
    {
        if (left is not null && right is not null)
        {
            _body.Children.Add(TwoColumns(left, right, 12));
        }
        else if ((left ?? right) is { } one)
        {
            _body.Children.Add(one);
        }
    }

    /// <summary>
    /// Two parts side by side, as high as the higher one, while the page is wide enough (760 or more); one under the other on a narrow page (the app's page
    /// next to its sections). <paramref name="right"/> null: the left one alone, at half the width.
    /// </summary>
    private static Grid TwoColumns(UIElement left, UIElement? right, double gap)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(left);
        if (right is not null)
        {
            grid.Children.Add(right);
        }

        bool? was = null;
        void Arrange(double width)
        {
            bool wide = width <= 0 || width >= 760;
            if (was == wide)
            {
                return;
            }

            was = wide;
            Grid.SetColumnSpan(left, wide ? 1 : 2);
            if (left is FrameworkElement l)
            {
                l.Margin = new Thickness(0, 0, wide ? gap / 2 : 0, gap);
            }

            if (right is not null)
            {
                Grid.SetRow(right, wide ? 0 : 1);
                Grid.SetColumn(right, wide ? 1 : 0);
                Grid.SetColumnSpan(right, wide ? 1 : 2);
                if (right is FrameworkElement r)
                {
                    r.Margin = new Thickness(wide ? gap / 2 : 0, 0, 0, gap);
                }
            }
        }

        Arrange(0);
        grid.SizeChanged += (_, e) => Arrange(e.NewSize.Width);
        return grid;
    }

    // ---- the roster: every character at once ----

    private const string RosterKey = "\u0001roster";

    /// <summary>The counters of the roster's table after the Odyle energy and the daily tasks: Shugo Festival, Dimensional Invasion, Nightmare, Ascension Trial
    /// (the overview's), then the daily dungeon's weekly entries.</summary>
    private static readonly int[] RosterTickets = { 1, 8, 4, 80000001, DailyDungeonTicket };

    /// <summary>Opens the window on the roster (the meter's menu) or on one character.</summary>
    public void ShowRoster()
    {
        _selected = RosterKey;
        Refresh();
    }

    public void ShowCharacter(string name)
    {
        _selected = name;
        Refresh();
    }

    /// <summary>One character on one part of its page (the pictures of the page: counters, season, supply, inventory).</summary>
    internal void ShowCharacter(string name, Section section)
    {
        _section = section;
        ShowCharacter(name);
    }

    private static UIElement RosterCard(bool active, int characters, Action choose)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var lines = new StackPanel();
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        TextBlock glyph = Label("", "Brush.Accent", 13);
        glyph.FontFamily = new FontFamily("Segoe MDL2 Assets");
        glyph.Margin = new Thickness(0, 0, 8, 0);
        title.Children.Add(glyph);
        title.Children.Add(Label(loc["Checklist.Roster"], "Brush.Text", 12.5, bold: true));
        lines.Children.Add(title);
        TextBlock info = Label(string.Format(CultureInfo.CurrentCulture, loc["Checklist.RosterCount"], characters), "Brush.TextSubtle", 10.5);
        info.Margin = new Thickness(0, 2, 0, 0);
        lines.Children.Add(info);
        var card = new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 10), BorderThickness = new Thickness(1),
            Child = lines, Cursor = Cursors.Hand, Background = Brushes.Transparent,
        };
        card.SetResourceReference(Border.BorderBrushProperty, active ? "Brush.Accent" : "Brush.Border");
        if (active)
        {
            card.SetResourceReference(Border.BackgroundProperty, "Brush.ControlRaised");
        }
        else
        {
            card.MouseEnter += (_, _) => card.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
            card.MouseLeave += (_, _) => card.Background = Brushes.Transparent;
        }

        card.MouseLeftButtonDown += (_, _) => choose();
        return card;
    }

    /// <summary>"in 5 h 20 m", "in 2 d 4 h", "in 12 m".</summary>
    private static string InText(TimeSpan left)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        left = left < TimeSpan.Zero ? TimeSpan.Zero : left;
        string span = left.TotalDays >= 1 ? string.Format(CultureInfo.CurrentCulture, loc["Checklist.DaysHours"], (int)left.TotalDays, left.Hours)
            : left.TotalHours >= 1 ? string.Format(CultureInfo.CurrentCulture, loc["Checklist.HoursMinutes"], (int)left.TotalHours, left.Minutes)
            : string.Format(CultureInfo.CurrentCulture, loc["Checklist.MinutesOnly"], Math.Max(1, (int)Math.Ceiling(left.TotalMinutes)));
        return string.Format(CultureInfo.CurrentCulture, loc["Checklist.In"], span);
    }

    /// <summary>What a run costs: 40 in most level-45 content, 80 for the double reward most players with the subscription take (5 November 2026, a tester:
    /// "with sub you go for 80 most of the time"); the player picks it on the page (MeterSettings.OdylePerRun).</summary>
    private static int OdyleClaim => MeterSettings.Load().OdylePerRun == 80 ? 80 : 40;

    /// <summary>"40 | 80 a run" under the claims: the switch the cube counts go by.</summary>
    private UIElement PerRunChips()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        int now = OdyleClaim;
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
        foreach (int option in new[] { 40, 80 })
        {
            bool on = option == now;
            var choice = new Border { Padding = new Thickness(8, 0, 8, 1), Margin = new Thickness(0, 0, 4, 0), CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand, BorderThickness = new Thickness(1),
                Child = Label(option.ToString(CultureInfo.CurrentCulture), on ? "Brush.Accent" : "Brush.TextMuted", 11.5, bold: on) };
            choice.SetResourceReference(Border.BorderBrushProperty, on ? "Brush.Accent" : "Brush.Border");
            choice.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                var settings = MeterSettings.Load();
                settings.OdylePerRun = option;
                settings.Save();
                Refresh();
            };
            line.Children.Add(choice);
        }

        line.Children.Add(Label(loc["Checklist.TilePerRun"], "Brush.TextSubtle", 11.5));
        return line;
    }

    /// <summary>"23 h 33 m", "4 d 23 h", "12 m": a span for the board (<see cref="InText"/> with "in").</summary>
    private static string SpanText(TimeSpan left)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        left = left < TimeSpan.Zero ? TimeSpan.Zero : left;
        return left.TotalDays >= 1 ? string.Format(CultureInfo.CurrentCulture, loc["Checklist.DaysHours"], (int)left.TotalDays, left.Hours)
            : left.TotalHours >= 1 ? string.Format(CultureInfo.CurrentCulture, loc["Checklist.HoursMinutes"], (int)left.TotalHours, left.Minutes)
            : string.Format(CultureInfo.CurrentCulture, loc["Checklist.MinutesOnly"], Math.Max(1, (int)Math.Ceiling(left.TotalMinutes)));
    }

    /// <summary>
    /// Every character on one page: the Odyle energy now (with the recharges since the meter last saw it), when it is full, the daily tasks, Shugo Festival,
    /// Dimensional Invasion, Nightmare, Ascension Trial and the daily dungeon, with the next daily and weekly reset on top. A line opens the character.
    /// </summary>
    private void RenderRoster(string language)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        DateTime now = DateTime.Now, utcNow = DateTime.UtcNow;
        string? online = Aion2Tickets.CurrentName;
        List<TicketCharacter> all = Aion2Tickets.OnHomeServer().Where(c => c.Tickets.Count > 0).ToList();
        var odyle = all.ToDictionary(c => c.Name, c => Aion2Tickets.OdyleNow(c, utcNow, c.Name == online));

        // the board: the resets, the energy of every character together, the claims it makes, whom to play next, the daily tasks done
        var known = odyle.Values.Where(o => o is not null).Select(o => o!).ToList();
        // all of it, not only the energy that recharges (5 November 2026, a tester: "it says 5k for all my chars when that's just the regenerable one"):
        // the basic energy, the charged energy and the energy in the items each character holds
        var inItems = all.ToDictionary(c => c.Name, c => odyle[c.Name] is null ? 0 : Aion2OdyleItems.EnergyHeld(c.Name));
        var cells = new List<Score>();
        if (Aion2Tickets.Find(4) is { } daily && Aion2Tickets.NextReset(daily, now) is { } nextDaily)
        {
            cells.Add(new Score(loc["Checklist.DailyReset"], Big(SpanText(nextDaily - now)), Small(nextDaily.ToString("t", CultureInfo.CurrentCulture)), "Brush.Accent"));
        }

        if (Aion2Tickets.Find(DailyDungeonTicket) is { } weekly && Aion2Tickets.NextReset(weekly, now) is { } nextWeekly)
        {
            cells.Add(new Score(loc["Checklist.WeeklyReset"], Big(SpanText(nextWeekly - now)),
                Small(nextWeekly.ToString("dddd", CultureInfo.CurrentCulture) + " " + nextWeekly.ToString("t", CultureInfo.CurrentCulture)), "Brush.Accent"));
        }

        int per = OdyleClaim;
        if (known.Count > 0)
        {
            int full = known.Count(o => o.Full);
            long basic = known.Sum(o => (long)o.Count), charged = known.Sum(o => (long)o.Bonus), items = inItems.Values.Sum();
            cells.Add(new Score(loc["Checklist.RosterOdyleHead"], Big((basic + charged + items).ToString("N0", CultureInfo.CurrentCulture), full > 0 ? "Brush.Warning" : "Brush.Accent"),
                Small(full > 0 ? string.Format(CultureInfo.CurrentCulture, loc[full == 1 ? "Checklist.RosterFullOne" : "Checklist.RosterFullMany"], full) : loc["Checklist.TileOfAll"], full > 0 ? "Brush.Warning" : "Brush.TextSubtle"),
                full > 0 ? "Brush.Warning" : "Brush.Accent",
                string.Format(CultureInfo.CurrentCulture, loc["Checklist.RosterOdyleSplit"], basic.ToString("N0", CultureInfo.CurrentCulture), charged.ToString("N0", CultureInfo.CurrentCulture), items.ToString("N0", CultureInfo.CurrentCulture))));
            long claims = all.Where(c => odyle[c.Name] is not null).Sum(c => (odyle[c.Name]!.Count + odyle[c.Name]!.Bonus + inItems[c.Name]) / per);
            cells.Add(new Score(loc["Checklist.TileClaims"], Big(claims.ToString("N0", CultureInfo.CurrentCulture), "Brush.Accent"), PerRunChips(), "Brush.Heal", loc["Checklist.PerRunTip"]));
        }

        // whom to play next so no recharge is lost (the full ones first, then the one full soonest)
        var order = all.Where(c => odyle[c.Name] is not null).Select(c => (Character: c, O: odyle[c.Name]!))
            .OrderByDescending(x => x.O.Full).ThenBy(x => x.O.FullAt ?? DateTime.MaxValue).Take(3).ToList();
        if (order.Count > 0)
        {
            var (first, o) = order[0];
            string? cls = first.ClassCode > 0 ? Aion2.Protocol.Aion2SkillNames.ClassFromCode(first.ClassCode) : null;
            TextBlock name = Big(first.Name);
            if (cls is not null)
            {
                name.Foreground = ClassBrushes.Text(cls);
            }

            string why = o.Full ? loc["Checklist.TileFullNow"] : o.FullAt is { } at ? string.Format(CultureInfo.CurrentCulture, loc["Checklist.RosterPlanIn"], InText(at - utcNow)) : "–";
            string then = order.Count > 1 ? "  ·  " + string.Format(CultureInfo.CurrentCulture, loc["Checklist.TileThen"], string.Join(", ", order.Skip(1).Select(x => x.Character.Name))) : "";
            string plan = string.Join("\n", order.Select(x => x.Character.Name + ": " + (x.O.Full ? loc["Checklist.RosterPlanFull"]
                : x.O.FullAt is { } when ? string.Format(CultureInfo.CurrentCulture, loc["Checklist.RosterPlanIn"], InText(when - utcNow)) : "–")
                + ((x.O.Count + x.O.Bonus + inItems.GetValueOrDefault(x.Character.Name)) / per is long n and > 0 ? $", {n} × {per}" : "")));
            cells.Add(new Score(loc["Checklist.TilePlayNext"], name, Small(why + then, o.Full ? "Brush.Warning" : "Brush.TextSubtle"), o.Full ? "Brush.Warning" : "Brush.Heal",
                plan + "\n\n" + loc["Checklist.RosterPlanTip"]));
        }

        // the daily tasks: how many characters handed all five in today
        int withTasks = all.Count(c => c.Tickets.ContainsKey(Aion2Tickets.MissionsId));
        if (withTasks > 0)
        {
            int doneAll = all.Count(c => c.Tickets.TryGetValue(Aion2Tickets.MissionsId, out TicketValue m) && !Aion2Tickets.MissionsStale(m, now) && m.Count >= Aion2Tickets.MissionsPerDay);
            cells.Add(new Score(loc["Checklist.RosterTasks"], Big($"{doneAll} / {all.Count}", doneAll >= all.Count ? "Brush.Heal" : "Brush.Text"), Small(loc["Checklist.TileTasksNote"]),
                "Brush.Heal", loc["Checklist.RosterTasksTip"]));
        }

        // a board of six: the characters themselves when the daily tasks are not known yet
        if (cells.Count % 3 != 0)
        {
            int loggedIn = online is not null && all.Any(c => c.Name == online) ? 1 : 0;
            cells.Add(new Score(loc["Checklist.Roster"], Big(all.Count.ToString(CultureInfo.CurrentCulture)),
                Small(loggedIn == 1 ? string.Format(CultureInfo.CurrentCulture, loc["Checklist.TileOnline"], online) : loc["Checklist.TileNoneOnline"]), "Brush.TextMuted"));
        }

        if (cells.Count > 0)
        {
            _body.Children.Add(Scoreboard(cells));
        }

        // every character as a card of its energy and counters; what the numbers are in the "i"
        TicketDef?[] defs = RosterTickets.Select(Aion2Tickets.Find).ToArray();
        var title = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 12) };
        title.Children.Add(SectionTitle(string.Format(CultureInfo.CurrentCulture, loc["Checklist.TilesTitle"], all.Count)));
        title.Children.Add(InfoMark(loc["Checklist.RosterNote"]));
        _body.Children.Add(title);
        _body.Children.Add(RosterTable(all, odyle, inItems, defs, online, now, utcNow, language));

        // the season missions of every character: its box of characters (weekly points, missions done) picks whose missions show, the one online first
        if ((all.FirstOrDefault(c => c.Name == online) ?? all.FirstOrDefault()) is { } seasonFirst)
        {
            AddSeason(seasonFirst, language);
        }

        AddRosterInventories(); // every server's characters together: what they hold unbound and the server storage

        // the same roster on the phone: the site keeps it for the player's own Discord account (the meter's Atreia sign-in)
        var sync = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 10, 4, 0) };
        var box = new CheckBox { IsChecked = MeterSettings.Load().SyncRoster, VerticalAlignment = VerticalAlignment.Center };
        box.Content = Label(AionDPS.Upload.Account.SignedIn ? loc["Checklist.RosterSync"] : loc["Checklist.RosterSyncSignIn"], "Brush.Text", 12);
        box.Click += (_, _) =>
        {
            var settings = MeterSettings.Load();
            settings.SyncRoster = box.IsChecked == true;
            settings.Save();
        };
        sync.Children.Add(box);
        TextBlock open = Label(loc["Checklist.RosterOpenSite"], "Brush.Accent", 12, bold: true);
        open.Margin = new Thickness(12, 0, 0, 0);
        open.Cursor = Cursors.Hand;
        open.MouseLeftButtonUp += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Brand.Site + "/meter/roster") { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // no browser to open it with
            }
        };
        sync.Children.Add(open);
        _body.Children.Add(sync);
    }

    private static void Place(Grid grid, UIElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private static string AgoText(TimeSpan ago)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        ago = ago < TimeSpan.Zero ? TimeSpan.Zero : ago;
        string span = ago.TotalDays >= 1 ? string.Format(CultureInfo.CurrentCulture, loc["Checklist.DaysHours"], (int)ago.TotalDays, ago.Hours)
            : ago.TotalHours >= 1 ? string.Format(CultureInfo.CurrentCulture, loc["Checklist.HoursMinutes"], (int)ago.TotalHours, ago.Minutes)
            : string.Format(CultureInfo.CurrentCulture, loc["Checklist.MinutesOnly"], Math.Max(1, (int)ago.TotalMinutes));
        return string.Format(CultureInfo.CurrentCulture, loc["Checklist.Ago"], span);
    }

    /// <summary>The Odyle energy as the game's tooltip shows it: the basic energy (cap 840 with the subscription) and the charged energy (cap 2000).</summary>
    private static UIElement OdyleCard(TicketDef def, TicketValue value, string language, bool locked, bool premium, string character)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var stack = new StackPanel();
        stack.Children.Add(SectionTitle(def.NameIn(language), 15, locked ? "Brush.TextSubtle" : "Brush.Text"));
        ((FrameworkElement)stack.Children[0]).Margin = new Thickness(0, 0, 0, 4);
        stack.Children.Add(OdyleLine(loc["Checklist.OdyleBase"], value.Count, Math.Max(premium ? def.SubscribeMax : def.Max, value.Count), locked));
        stack.Children.Add(OdyleLine(loc["Checklist.OdyleCharged"], value.Bonus, Math.Max(OdyleChargedMax, value.Bonus), locked));
        AddOdyleItems(stack, value, language, character);
        return Card(stack);
    }

    /// <summary>
    /// The Odyle energy items the character holds, one line per kind (Odyle Energy 40 each, Small Odyle Energy 10 each, the ones that run out marked by their
    /// own name): how many in the bag and in storage, and the energy they hold; then all of it on top of the bar ("540 + 300 + 460 in items = 1,300").
    /// Nothing while the character's inventory is not known (a login with the meter running reads it).
    /// </summary>
    private static void AddOdyleItems(StackPanel stack, TicketValue value, string language, string character)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        IReadOnlyList<OdyleItemLine> items = Aion2OdyleItems.Held(character, language);
        if (items.Count == 0)
        {
            return;
        }

        TextBlock title = Label(loc["Checklist.OdyleItems"], "Brush.Text", 13, bold: true);
        title.Margin = new Thickness(0, 10, 0, 2);
        stack.Children.Add(title);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        int row = 0;
        foreach (OdyleItemLine line in items)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            string where = line.Storage == 0 ? "" : line.Bag == 0 ? "  ·  " + loc["Checklist.OdyleInStorage"]
                : "  ·  " + string.Format(CultureInfo.CurrentCulture, loc["Checklist.OdyleBagStorage"], line.Bag, line.Storage);
            TextBlock name = Label(line.Name + "  ·  " + string.Format(CultureInfo.CurrentCulture, loc["Checklist.OdyleEach"], line.Each) + where,
                line.Expires ? "Brush.Warning" : "Brush.Text", 12.5);
            name.Margin = new Thickness(0, 1, 12, 1);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            if (line.Expires)
            {
                name.ToolTip = loc["Checklist.OdyleExpires"];
            }

            Grid.SetRow(name, row);
            grid.Children.Add(name);
            TextBlock count = Label("×" + line.Count.ToString("N0", CultureInfo.CurrentCulture), "Brush.Text", 12.5, bold: true);
            count.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetRow(count, row);
            Grid.SetColumn(count, 1);
            grid.Children.Add(count);
            TextBlock energy = Label("+" + line.Energy.ToString("N0", CultureInfo.CurrentCulture), "Brush.Accent", 12.5, bold: true);
            energy.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetRow(energy, row);
            Grid.SetColumn(energy, 2);
            grid.Children.Add(energy);
            row++;
        }

        stack.Children.Add(grid);
        long inItems = items.Sum(l => l.Energy);
        long all = value.Count + value.Bonus + inItems;
        int per = OdyleClaim;
        TextBlock total = Label(string.Format(CultureInfo.CurrentCulture, loc["Checklist.OdyleTotalAt"], value.Count.ToString("N0", CultureInfo.CurrentCulture),
            value.Bonus.ToString("N0", CultureInfo.CurrentCulture), inItems.ToString("N0", CultureInfo.CurrentCulture), all.ToString("N0", CultureInfo.CurrentCulture),
            (all / per).ToString("N0", CultureInfo.CurrentCulture), per), "Brush.Accent", 12.5, bold: true);
        total.Margin = new Thickness(0, 6, 0, 0);
        total.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(total);
    }

    private const int OdyleChargedMax = 2000;

    private static UIElement OdyleLine(string label, int value, int cap, bool locked = false)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        TextBlock name = Label(label, locked ? "Brush.TextSubtle" : "Brush.Text", 13);
        TextBlock numbers = Label($"{value:N0} / {cap:N0}", value > 0 && !locked ? "Brush.Accent" : "Brush.TextSubtle", 13, bold: true);
        Grid.SetColumn(numbers, 1);
        var bar = new ProgressBar { Minimum = 0, Maximum = Math.Max(1, cap), Value = Math.Min(value, cap), Height = 6, Margin = new Thickness(0, 3, 0, 0) };
        bar.SetResourceReference(Control.ForegroundProperty, "Brush.Accent");
        bar.SetResourceReference(Control.BackgroundProperty, "Brush.Control");
        bar.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");
        Grid.SetRow(bar, 1);
        Grid.SetColumnSpan(bar, 2);
        grid.Children.Add(name);
        grid.Children.Add(numbers);
        grid.Children.Add(bar);
        return grid;
    }

    /// <summary>The number of a row as three cells so the numbers stand one under the other: the count right-aligned, the slash, the maximum left-aligned.</summary>
    private static void AddNumberCells(Grid grid, int row, int firstColumn, int count, int cap, bool estimated, bool open, int bonus = 0)
    {
        string brush = open ? "Brush.Accent" : "Brush.TextSubtle";
        TextBlock now = Label((estimated ? "~" : "") + count.ToString(CultureInfo.CurrentCulture) + (bonus > 0 ? $" (+{bonus.ToString(CultureInfo.CurrentCulture)})" : ""), brush, 13, bold: true);
        now.HorizontalAlignment = HorizontalAlignment.Right;
        now.Margin = new Thickness(0, 1, 0, 1);
        TextBlock slash = Label(" / ", "Brush.TextSubtle", 13);
        slash.Margin = new Thickness(0, 1, 0, 1);
        TextBlock max = Label(cap.ToString(CultureInfo.CurrentCulture), brush, 13, bold: true);
        max.HorizontalAlignment = HorizontalAlignment.Left;
        max.Margin = new Thickness(0, 1, 0, 1);
        foreach ((TextBlock block, int offset) in new[] { (now, 0), (slash, 1), (max, 2) })
        {
            Grid.SetRow(block, row);
            Grid.SetColumn(block, firstColumn + offset);
            grid.Children.Add(block);
        }
    }

    /// <summary>A table of counters: the name (cut with "…" when the card is narrow, the whole name on the mouse), the count right-aligned, the slash, the
    /// maximum, what the number is ("left", "done"), and a tick or a warning.</summary>
    private static Grid NumberGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // name
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                     // count with its bonus (right-aligned)
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                     // slash
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 24 });      // maximum (left-aligned)
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 34 });      // "left" / "done"
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                     // a tick or a warning
        return grid;
    }

    /// <summary>The daily part: the mandatory tasks ("Missions") of the home region (Verteron for Elyos, Altgard for Asmodians) as one counter, handed in of five
    /// (five of five gets a tick).</summary>
    private void AddDaily(string title, TicketCharacter character, DateTime seenAt, DateTime now, string language)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        // (Stacy's fix from her fork, MIT) the count read last, or 0 (shown as an estimate) when a daily reset came since that reading: the game sends it at a login, a hand-in or when the journal is opened, never at the reset
        bool stale = character.Tickets.TryGetValue(Aion2Tickets.MissionsId, out TicketValue missions) && Aion2Tickets.MissionsStale(missions, DateTime.Now);
        int done = stale ? 0 : missions.Count;
        string region = loc[character.ServerId / 1000 == 2 ? "Checklist.RegionAsmodian" : "Checklist.RegionElyos"];
        var stack = new StackPanel();
        stack.Children.Add(SectionTitle(title));
        ((FrameworkElement)stack.Children[0]).Margin = new Thickness(0, 0, 0, 4);
        Grid grid = NumberGrid();
        int row = 0;
        AddTableRow(grid, row++, string.Format(CultureInfo.CurrentCulture, loc["Checklist.DailyTasks"], region), done, Aion2Tickets.MissionsPerDay, stale, countsUp: true);
        if (done >= Aion2Tickets.MissionsPerDay)
        {
            TextBlock tick = Label("\u2713", "Brush.Accent", 15, bold: true);
            tick.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(tick, 5);
            grid.Children.Add(tick);
        }

        foreach (int id in DailyTickets)
        {
            if (Aion2Tickets.Find(id) is { } def && (character.Tickets.ContainsKey(id) || Aion2Tickets.IsLocked(character, id)))
            {
                AddTicketRow(grid, row++, def, TicketLabel(def, language), character, seenAt, now);
            }
        }

        stack.Children.Add(Label(WhenText(loc["Checklist.DailyReset"], ChecklistManual.LastDailyReset(now).AddDays(1), now), "Brush.TextSubtle", 11));
        stack.Children.Add(grid);
        _body.Children.Add(Card(stack));
    }

    /// <summary>"Daily reset in 5 h 20 m  ·  7:00 AM" under a card's title (the day too when it is more than a day away).</summary>
    private static string WhenText(string label, DateTime next, DateTime now) =>
        label + " " + InText(next - now) + "  ·  " + (next - now > TimeSpan.FromDays(1) ? next.ToString("ddd", CultureInfo.CurrentCulture) + " " : "") + next.ToString("t", CultureInfo.CurrentCulture);

    /// <summary>A counter's name in a block under its category: the game's name without the "[Conquest] " in front (the block's heading says it), or its short name.</summary>
    private static string TicketLabel(TicketDef def, string language)
    {
        if (def.ShortNames is not null)
        {
            return def.ShortIn(language);
        }

        string name = def.NameIn(language);
        int close = name.StartsWith('[') ? name.IndexOf(']') : -1;
        return close > 0 ? name[(close + 1)..].Trim() : name;
    }

    /// <summary>The game's category of a counter (the text between the brackets in front of its name), or null.</summary>
    private static string? TicketCategory(TicketDef def, string language)
    {
        string name = def.NameIn(language);
        int close = name.StartsWith('[') ? name.IndexOf(']') : -1;
        return close > 1 ? name[1..close].Trim() : null;
    }

    /// <summary>One counter as a row of a table: what is left (worked out from the refills since the meter saw it, "~"), of how many; grey when none is left or
    /// the level is too low; with <paramref name="warn"/> a "full" warning when the next daily refill runs over the cap (none for the dungeons: the owner,
    /// 4 November 2026, nothing is lost there).</summary>
    private void AddTicketRow(Grid grid, int row, TicketDef def, string name, TicketCharacter character, DateTime seenAt, DateTime now, bool warn = true)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        bool locked = Aion2Tickets.IsLocked(character, def.Id);
        TicketValue own = locked || !character.Tickets.TryGetValue(def.Id, out TicketValue found) ? default : found;
        int count = locked ? 0 : Aion2Tickets.Expected(def, own.Count, seenAt, now, _premium);
        AddTableRow(grid, row, name, count, Math.Max(CapOf(def), count + own.Bonus), count != own.Count, countsUp: false, bonus: own.Bonus);
        int lost = !warn || locked ? 0 : Aion2Tickets.LostAtNextRefill(def, count, _premium);
        if (lost > 0)
        {
            UIElement badge = WarnBadge(loc["Checklist.Full"], "Brush.Warning");
            ((FrameworkElement)badge).ToolTip = string.Format(CultureInfo.CurrentCulture, loc[lost == 1 ? "Checklist.WarnLostOne" : "Checklist.WarnLostMany"], lost);
            Grid.SetRow(badge, row);
            Grid.SetColumn(badge, 5);
            grid.Children.Add(badge);
            _warned = true;
        }
    }

    /// <summary>
    /// The dungeons: the reward and boss counters of Conquest, Transcendence and Ordeal, the cubes not yet taken and the Sanctuary raids, each block under the
    /// game's own category, the short blocks two side by side and the raids (six lines) across the card. What is left this week (or today, for the ones the game
    /// refills daily); no "full" warnings here.
    /// </summary>
    private void AddDungeons(string title, TicketCharacter character, DateTime seenAt, DateTime now, string language)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var stack = new StackPanel();
        stack.Children.Add(SectionTitle(title));
        stack.Children.Add(Label(WhenText(loc["Checklist.WeeklyReset"], ChecklistManual.LastWeeklyReset(now).AddDays(7), now), "Brush.TextSubtle", 11));
        var blocks = new List<(UIElement Box, int Rows)>();
        foreach (int[] block in DungeonBlocks)
        {
            TicketDef[] defs = block.Select(Aion2Tickets.Find).Where(d => d is not null && (character.Tickets.ContainsKey(d.Id) || Aion2Tickets.IsLocked(character, d.Id))).Select(d => d!).ToArray();
            if (defs.Length == 0)
            {
                continue;
            }

            var inner = new StackPanel();
            TextBlock heading = Label(TicketCategory(defs[0], language) ?? "", "Brush.Accent", 12, bold: true);
            heading.Margin = new Thickness(0, 0, 0, 3);
            inner.Children.Add(heading);
            Grid grid = NumberGrid();
            int row = 0;
            foreach (TicketDef def in defs)
            {
                // two counters of one name (the cubes not yet taken: the character's and the server's)
                string name = TicketLabel(def, language) + (def.PerServer && defs.Count(d => TicketLabel(d, language) == TicketLabel(def, language)) > 1 ? " " + loc["Checklist.PerServer"] : "");
                AddTicketRow(grid, row++, def, name, character, seenAt, now, warn: false);
            }

            inner.Children.Add(grid);
            var box = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 7), Child = inner };
            box.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            blocks.Add((box, defs.Length));
        }

        if (blocks.Count == 0)
        {
            return;
        }

        var area = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        UIElement? waiting = null;
        foreach ((UIElement box, int rows) in blocks)
        {
            if (rows > 4)
            {
                if (waiting is not null)
                {
                    area.Children.Add(TwoColumns(waiting, null, 8));
                    waiting = null;
                }

                ((FrameworkElement)box).Margin = new Thickness(0, 0, 0, 8);
                area.Children.Add(box);
            }
            else if (waiting is null)
            {
                waiting = box;
            }
            else
            {
                area.Children.Add(TwoColumns(waiting, box, 8));
                waiting = null;
            }
        }

        if (waiting is not null)
        {
            area.Children.Add(TwoColumns(waiting, null, 8));
        }

        stack.Children.Add(area);
        _body.Children.Add(Card(stack));
    }

    /// <summary>The weekly part: the entries of the "Daily Dungeon" (the name is misleading: its counter is weekly; cap 14, extra entries come on top as the bonus
    /// behind the count), Ascension Trial, Subjugation, the Odyle Energy bought in the trade guild and the season points of the week - all from the game. The
    /// scroll purchases and the Odyle conversions are not here: the game sends nothing about them that the meter reads, and the page shows only what the game
    /// says (the owner, 4 November 2026: nothing on it is changed by a click).</summary>
    private void AddWeekly(string title, TicketCharacter character, DateTime seenAt, DateTime now, string language)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var stack = new StackPanel();
        stack.Children.Add(SectionTitle(title));
        ((FrameworkElement)stack.Children[0]).Margin = new Thickness(0, 0, 0, 4);
        stack.Children.Add(Label(WhenText(loc["Checklist.WeeklyReset"], ChecklistManual.LastWeeklyReset(now).AddDays(7), now), "Brush.TextSubtle", 11));
        Grid grid = NumberGrid();
        int row = 0;
        bool dungeonLocked = Aion2Tickets.IsLocked(character, DailyDungeonTicket);
        if (Aion2Tickets.Find(DailyDungeonTicket) is { } dungeon && (dungeonLocked || character.Tickets.ContainsKey(dungeon.Id)))
        {
            TicketValue value = dungeonLocked ? default : character.Tickets[dungeon.Id];
            int count = dungeonLocked ? 0 : Aion2Tickets.Expected(dungeon, value.Count, seenAt, now, _premium);
            AddTableRow(grid, row++, dungeon.ShortIn(language), count, Math.Max(dungeon.Max, count), count != value.Count, countsUp: false, bonus: value.Bonus);
        }

        foreach (int id in WeeklyTickets)
        {
            if (Aion2Tickets.Find(id) is { } def && (character.Tickets.ContainsKey(id) || Aion2Tickets.IsLocked(character, id)))
            {
                AddTicketRow(grid, row++, def, TicketLabel(def, language), character, seenAt, now);
            }
        }

        // the purchase counter of the trade guild comes from the game; an older reset time means the week is over
        character.Tickets.TryGetValue(Aion2Tickets.ShopOdyleId, out TicketValue shop);
        long nowMs = new DateTimeOffset(DateTime.UtcNow, TimeSpan.Zero).ToUnixTimeMilliseconds();
        int bought = shop.TimeMs > nowMs ? Math.Min(shop.Count, Aion2Tickets.ShopOdyleMax) : 0;
        AddTableRow(grid, row++, loc["Checklist.ShopOdyle"], bought, Aion2Tickets.ShopOdyleMax, false, countsUp: true, word: loc["Checklist.Bought"]);

        // the week's points of the season missions (frame 0x8356 at login): points of 8,000; ones the game last changed before the weekly reset (Wednesday, after the
        // maintenance) are of last week: 0 now, marked as worked out ("~") until the character logs in again
        if (character.Tickets.TryGetValue(Aion2Tickets.WeeklyPointsId, out TicketValue weekly))
        {
            bool over = Aion2Tickets.WeeklyPointsStale(weekly, now);
            int points = over ? 0 : Math.Min(weekly.Count, Aion2Tickets.WeeklyPointsMax);
            AddTableRow(grid, row++, loc["Checklist.WeeklyPoints"], points, Aion2Tickets.WeeklyPointsMax, over && weekly.Count > 0, countsUp: true, word: "");
        }

        stack.Children.Add(grid);
        _body.Children.Add(Card(stack));
    }

    /// <summary>One table row: the name, the number (one under the other) and what it is - "left" for a counter the game counts down, "done" for one it counts
    /// up, or <paramref name="word"/>. Nothing in it reacts to a click: every number is the game's.</summary>
    private static void AddTableRow(Grid grid, int row, string name, int count, int cap, bool estimated, bool countsUp, int bonus = 0, string? word = null)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bool open = countsUp ? count < cap : count > 0;
        TextBlock label = Label(name, open ? "Brush.Text" : "Brush.TextSubtle", 13);
        label.Margin = new Thickness(0, 1, 10, 1);
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.ToolTip = name;
        Grid.SetRow(label, row);
        grid.Children.Add(label);
        AddNumberCells(grid, row, 1, count, cap, estimated, open, bonus);
        string what = word ?? LocalizationManager.Instance[countsUp ? "Checklist.DoneWord" : "Checklist.Left"];
        if (what.Length > 0)
        {
            TextBlock caption = Label(what, "Brush.TextSubtle", 11);
            caption.Margin = new Thickness(6, 1, 0, 0);
            caption.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(caption, row);
            Grid.SetColumn(caption, 4);
            grid.Children.Add(caption);
        }
    }

    /// <summary>The cap the game shows: the subscriber's one where there is one (Shugo Festival keys 12, with the subscription 21), else the plain one.</summary>
    private int CapOf(TicketDef def) => _premium && def.SubscribeMax > def.Max ? def.SubscribeMax : def.Max;

    /// <summary>How soon a membership or ticket counts as "runs out soon" (red or amber with a warning triangle).</summary>
    private static readonly TimeSpan SoonLeft = TimeSpan.FromDays(3);

    /// <summary>The time left as days and hours ("11 d 23 h"), as hours under a day.</summary>
    internal static string LeftText(TimeSpan left)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        return left.TotalDays >= 1
            ? string.Format(CultureInfo.CurrentCulture, loc["Checklist.DaysHours"], (int)left.TotalDays, left.Hours)
            : string.Format(CultureInfo.CurrentCulture, loc["Checklist.HoursOnly"], Math.Max(1, (int)left.TotalHours));
    }

    /// <summary>A warning: a small triangle with an exclamation mark and a short text behind it.</summary>
    private static UIElement WarnBadge(string text, string brush)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var triangle = new System.Windows.Shapes.Polygon { Points = new PointCollection { new Point(8, 0), new Point(16, 14), new Point(0, 14) } };
        triangle.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brush);
        var mark = new TextBlock { Text = "!", FontSize = 9, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
        mark.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Window");
        var shape = new Grid { Width = 16, Height = 14, VerticalAlignment = VerticalAlignment.Center };
        shape.Children.Add(triangle);
        shape.Children.Add(mark);
        panel.Children.Add(shape);
        TextBlock label = Label(text, brush, 11);
        label.Margin = new Thickness(5, 0, 0, 0);
        panel.Children.Add(label);
        return panel;
    }

    /// <summary>One line of the membership card: the name left, the time right (with a triangle when it runs out soon), and under it small lines.</summary>
    private static void AddSubscriptionRow(StackPanel stack, string name, string right, string rightBrush, string? warnBrush, bool muted, params string[] notes)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock label = Label(name, muted ? "Brush.TextSubtle" : "Brush.Text", 13);
        var value = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        if (warnBrush is not null)
        {
            UIElement badge = WarnBadge("", warnBrush);
            ((StackPanel)badge).Margin = new Thickness(0, 0, 6, 0);
            value.Children.Add(badge);
        }

        value.Children.Add(Label(right, rightBrush, 13, bold: true));
        Grid.SetColumn(value, 1);
        grid.Children.Add(label);
        grid.Children.Add(value);
        stack.Children.Add(grid);
        foreach (string note in notes)
        {
            TextBlock line = Label(note, warnBrush == "Brush.Danger" ? "Brush.Danger" : "Brush.TextSubtle", 11);
            line.TextWrapping = TextWrapping.Wrap;
            line.Margin = new Thickness(0, 1, 0, 0);
            stack.Children.Add(line);
        }
    }

    /// <summary>Membership, pet auto-loot and the access times: when they run out, and what is gone then.</summary>
    private void AddSubscriptions(string title, IEnumerable<TicketDef> defs, TicketCharacter character, string language)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        TicketDef[] times = defs.ToArray();
        DateTime now = DateTime.Now;
        bool hasMember = character.Tickets.TryGetValue(Aion2Tickets.PremiumId, out TicketValue member) && member.TimeMs > 0;
        bool hasLoot = character.Tickets.TryGetValue(Aion2Tickets.AutoLootId, out TicketValue loot);
        if (times.Length == 0 && !hasMember && !hasLoot)
        {
            return;
        }

        var stack = new StackPanel();
        stack.Children.Add(SectionTitle(title));
        ((FrameworkElement)stack.Children[0]).Margin = new Thickness(0, 0, 0, 2);
        if (hasMember)
        {
            DateTime end = DateTimeOffset.FromUnixTimeMilliseconds(member.TimeMs).LocalDateTime;
            TimeSpan left = end - now;
            TicketDef? odyle = Aion2Tickets.Find(Aion2Tickets.OdyleId);
            TicketDef? keys = Aion2Tickets.Find(1);
            string gone = string.Format(CultureInfo.CurrentCulture, loc["Checklist.PremiumLoses"], odyle?.Max ?? 560, odyle?.SubscribeMax ?? 840, keys?.Max ?? 12, keys?.SubscribeMax ?? 21);
            if (left > TimeSpan.Zero)
            {
                bool soon = left < SoonLeft;
                AddSubscriptionRow(stack, loc["Checklist.Premium"], LeftText(left), soon ? "Brush.Danger" : "Brush.Accent", soon ? "Brush.Danger" : null, false,
                    string.Format(CultureInfo.CurrentCulture, loc["Checklist.Until"], end.ToString("g", CultureInfo.CurrentCulture)) + "  ·  " + gone);
            }
            else
            {
                AddSubscriptionRow(stack, loc["Checklist.Premium"], string.Format(CultureInfo.CurrentCulture, loc["Checklist.PremiumEnded"], end.ToString("d", CultureInfo.CurrentCulture)), "Brush.Danger", "Brush.Danger", false,
                    loc["Checklist.PremiumNow"]);
            }
        }

        if (hasLoot)
        {
            string lootName = loc["Checklist.AutoLoot"];
            DateTime end = loot.TimeMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(loot.TimeMs).LocalDateTime : DateTime.MinValue;
            TimeSpan left = end - now;
            if (loot.TimeMs > 0 && left > TimeSpan.Zero)
            {
                bool soon = left < SoonLeft;
                AddSubscriptionRow(stack, lootName, LeftText(left), "Brush.Accent", soon ? "Brush.Warning" : null, false,
                    string.Format(CultureInfo.CurrentCulture, loc["Checklist.Until"], end.ToString("g", CultureInfo.CurrentCulture)) + "  ·  " + loc["Checklist.AutoLootNote"]);
            }
            else
            {
                AddSubscriptionRow(stack, lootName, loc["Checklist.AutoLootNone"], "Brush.TextSubtle", null, true);
            }
        }

        foreach (TicketDef def in times)
        {
            TimeSpan left = Aion2Tickets.IsLocked(character, def.Id) || !character.Tickets.TryGetValue(def.Id, out TicketValue timeValue) ? TimeSpan.Zero : TimeSpan.FromMilliseconds(timeValue.TimeMs);
            bool open = left > TimeSpan.Zero;
            AddSubscriptionRow(stack, def.NameIn(language), $"{(int)left.TotalHours}:{left.Minutes:00}", open ? "Brush.Accent" : "Brush.TextSubtle", null, !open);
        }

        _body.Children.Add(Card(stack));
    }

    /// <summary>The Daeva passes of the character: level (where the cost of the levels is known), experience of the week against the weekly limit, end of the pass.</summary>
    private void AddPass(string title, TicketCharacter character, string language)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var found = Aion2Tickets.Passes.Where(p => character.Tickets.ContainsKey(p.TicketId)).ToArray();
        if (found.Length == 0)
        {
            return;
        }

        var stack = new StackPanel();
        stack.Children.Add(SectionTitle(title));
        ((FrameworkElement)stack.Children[0]).Margin = new Thickness(0, 0, 0, 2);
        foreach (Aion2Tickets.PassDef pass in found)
        {
            TicketValue value = character.Tickets[pass.TicketId];
            int week = Math.Min(value.Bonus, pass.WeeklyLimit);
            bool full = value.Bonus >= pass.WeeklyLimit;
            var head = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(Label(loc[pass.NameKeyFor(character.ServerId)], "Brush.Text", 13, bold: true));
            bool ended = DateTime.Now >= pass.End;
            TextBlock end = Label(string.Format(CultureInfo.CurrentCulture, loc[ended ? "Checklist.PassEnded" : "Checklist.PassEnds"], pass.End.ToString("g", CultureInfo.CurrentCulture)), "Brush.TextSubtle", 11);
            Grid.SetColumn(end, 1);
            head.Children.Add(end);
            stack.Children.Add(head);

            var line = new Grid { Margin = new Thickness(0, 1, 0, 0) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Aion2Tickets.PassLevel? level = Aion2Tickets.PassLevelOf(pass.TicketId, value.Count);
            string levelText = level is { } l
                ? string.Format(CultureInfo.CurrentCulture, loc["Checklist.PassLevel"], l.Level, l.Into.ToString("N0", CultureInfo.CurrentCulture), l.Need.ToString("N0", CultureInfo.CurrentCulture))
                : string.Format(CultureInfo.CurrentCulture, loc["Checklist.PassTotal"], value.Count.ToString("N0", CultureInfo.CurrentCulture));
            line.Children.Add(Label(levelText, "Brush.TextSubtle", 12));
            TextBlock weekText = Label(string.Format(CultureInfo.CurrentCulture, loc["Checklist.PassWeek"], week.ToString("N0", CultureInfo.CurrentCulture), pass.WeeklyLimit.ToString("N0", CultureInfo.CurrentCulture)), full ? "Brush.Heal" : "Brush.Accent", 12, bold: true);
            Grid.SetColumn(weekText, 1);
            line.Children.Add(weekText);
            stack.Children.Add(line);

            var bar = new ProgressBar { Minimum = 0, Maximum = pass.WeeklyLimit, Value = week, Height = 5, Margin = new Thickness(0, 3, 0, 0) };
            bar.SetResourceReference(Control.ForegroundProperty, full ? "Brush.Heal" : "Brush.Accent");
            bar.SetResourceReference(Control.BackgroundProperty, "Brush.Control");
            bar.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");
            stack.Children.Add(bar);
        }

        _body.Children.Add(Card(stack));
    }

    // ---- the season missions ----

    /// <summary>The weekly reward steps of the season points (the game's six levels need these points; the last at 8,000).</summary>
    private static readonly int[] SeasonRewards = { 1500, 3000, 4500, 6000, 8000 };

    /// <summary>
    /// The season missions of the character (<see cref="SeasonMissions"/>, counters from <see cref="Aion2Achievements"/>), live. On top the time to the weekly reset,
    /// Weekly / Whole season, the switch of the season overlay and, for the weekly ones, the week's points on the reward track; then chips for the game's categories
    /// (All or one) and the missions as tiles, two a row: the category's colour, the name, what the mission asks now (the step worked on, in the game's words), a bar,
    /// the stage and the count as the game's window shows it ("1,240 / 3,000"). The ones under way first, the furthest along first; the finished ones fold away
    /// under "Finished (n)". The ★ of a tile puts it on the season overlay. Resting the mouse on a tile shows its card: the description and every stage.
    /// Weekly counts of a week already over are 0 ("~") until the character logs in again.
    /// </summary>
    private void AddSeason(TicketCharacter character, string language)
    {
        IReadOnlyList<SeasonMission> missions = SeasonMissions.All;
        if (missions.Count == 0)
        {
            return;
        }

        // the character the card shows: the one picked in its box of characters, else the page's
        if (_seasonCharacter is { } pick && Aion2Tickets.Snapshot().FirstOrDefault(c => c.Name == pick) is { } picked)
        {
            character = picked;
        }
        else
        {
            _seasonCharacter = null;
        }

        LocalizationManager loc = LocalizationManager.Instance;
        IReadOnlyDictionary<int, AchievementValue> counters = Aion2Achievements.Of(character.Name);
        DateTime now = DateTime.Now;
        char faction = SeasonMissions.FactionOf(character.ServerId, counters, missions);
        UIElement? characters = SeasonCharacters(character.Name, missions, now);
        IReadOnlyList<SeasonProgress> all = SeasonMissions.Progress(faction, counters, ChecklistManual.LastWeeklyReset(now), missions);
        MeterSettings settings = MeterSettings.Load();

        var stack = new StackPanel();
        var head = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 8) };
        TextBlock resets = Label("↻  " + string.Format(CultureInfo.CurrentCulture, loc["Checklist.SeasonResets"], LeftText(Aion2Tickets.NextWeeklyResetAfter(now) - now)), "Brush.TextMuted", 11.5);
        DockPanel.SetDock(resets, Dock.Right);
        head.Children.Add(resets);
        head.Children.Add(SectionTitle(loc["Checklist.Season"] + (characters is null ? "" : "  ·  " + character.Name)));
        stack.Children.Add(head);
        if (characters is not null)
        {
            stack.Children.Add(characters);
        }

        if (counters.Count == 0)
        {
            TextBlock none = Label(loc["Checklist.SeasonNone"], "Brush.TextMuted", 12);
            none.TextWrapping = TextWrapping.Wrap;
            stack.Children.Add(none);
            _body.Children.Add(Card(stack));
            return;
        }

        SeasonProgress[] weekly = all.Where(x => x.Mission.Weekly).ToArray(), season = all.Where(x => !x.Mission.Weekly).ToArray();
        SeasonProgress[] list = _seasonWeekly ? weekly : season;

        // Weekly | Whole season, and the overlay's switch
        var bar = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 10) };
        var overlay = new CheckBox { Content = loc["Checklist.SeasonOverlay"], IsChecked = settings.ShowSeasonOverlay, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        overlay.SetResourceReference(StyleProperty, "SettingsSwitch"); // the app's on/off switch, not the stock white box
        overlay.Click += (_, _) =>
        {
            MeterSettings s = MeterSettings.Load();
            s.ShowSeasonOverlay = overlay.IsChecked == true;
            s.Save();
        };
        DockPanel.SetDock(overlay, Dock.Right);
        bar.Children.Add(overlay);
        var segments = new Border { CornerRadius = new CornerRadius(15), Padding = new Thickness(3), BorderThickness = new Thickness(1) };
        segments.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
        segments.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        var pair = new StackPanel { Orientation = Orientation.Horizontal };
        pair.Children.Add(SeasonSegment(loc["Checklist.SeasonWeekly"], weekly, _seasonWeekly, () => _seasonWeekly = true));
        pair.Children.Add(SeasonSegment(loc["Checklist.SeasonLong"], season, !_seasonWeekly, () => _seasonWeekly = false));
        segments.Child = pair;
        DockPanel.SetDock(segments, Dock.Left);
        bar.Children.Add(segments);
        stack.Children.Add(bar);
        stack.Children.Add(SeasonPickBanner(settings, all));

        if (_seasonWeekly && character.Tickets.TryGetValue(Aion2Tickets.WeeklyPointsId, out TicketValue points))
        {
            bool over = Aion2Tickets.WeeklyPointsStale(points, now);
            stack.Children.Add(SeasonPoints(over ? 0 : Math.Min(points.Count, Aion2Tickets.WeeklyPointsMax), over && points.Count > 0));
        }

        // the categories of this list: All, then each with finished / all
        string[] categories = list.Select(x => x.Mission.Category).Distinct().ToArray();
        if (_seasonCategory is not null && !categories.Contains(_seasonCategory))
        {
            _seasonCategory = null;
        }

        var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        chips.Children.Add(SeasonChip(loc["Checklist.SeasonAll"], null, list, _seasonCategory is null, () => _seasonCategory = null));
        foreach (string category in categories)
        {
            string chosen = category;
            chips.Children.Add(SeasonChip(SeasonLook.CategoryName(category, language), SeasonLook.Of(category), list.Where(x => x.Mission.Category == category).ToArray(),
                _seasonCategory == category, () => _seasonCategory = chosen));
        }

        stack.Children.Add(chips);

        HashSet<int> pinned = (settings.SeasonOverlayMissions ?? new List<int>()).ToHashSet();
        SeasonProgress[] shown = list.Where(x => _seasonCategory is null || x.Mission.Category == _seasonCategory).ToArray();
        // under way first, the furthest along first; then the ones not started, in the game's order
        SeasonProgress[] open = shown.Where(x => !x.Finished).OrderByDescending(x => x.Count > 0 || x.Done > 0).ThenByDescending(SeasonMissions.Fraction)
            .ThenByDescending(x => x.Done).ThenBy(x => x.Mission.Id).ToArray();
        SeasonProgress[] finished = shown.Where(x => x.Finished).ToArray();
        if (_seasonCategory is null)
        {
            foreach (IGrouping<string, SeasonProgress> group in open.GroupBy(x => x.Mission.Category))
            {
                stack.Children.Add(SeasonGroupTitle(group.Key, language, list.Where(x => x.Mission.Category == group.Key).ToArray()));
                stack.Children.Add(SeasonTiles(group.ToArray(), pinned, language));
            }
        }
        else if (open.Length > 0)
        {
            stack.Children.Add(SeasonTiles(open, pinned, language));
        }

        if (finished.Length > 0)
        {
            var fold = new TextBlock { Text = (_seasonShowFinished ? "▾  " : "▸  ") + string.Format(CultureInfo.CurrentCulture, loc["Checklist.SeasonFinishedN"], finished.Length), FontSize = 12.5,
                FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 6), Cursor = Cursors.Hand };
            fold.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            fold.MouseLeftButtonDown += (_, e) =>
            {
                _seasonShowFinished = !_seasonShowFinished;
                e.Handled = true;
                Render();
            };
            stack.Children.Add(fold);
            if (_seasonShowFinished)
            {
                stack.Children.Add(SeasonTiles(finished, pinned, language));
            }
        }

        _body.Children.Add(Card(stack));
    }

    /// <summary>
    /// How to choose the overlay's missions, right under the switch: click "☆ Overlay" on a mission (it turns gold), again to take it off; the order picked is the
    /// overlay's order; with none picked it shows the unfinished weekly ones. On the right how many are picked, with "Clear".
    /// </summary>
    private UIElement SeasonPickBanner(MeterSettings settings, IReadOnlyList<SeasonProgress> all)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var ids = all.Select(x => x.Mission.Id).ToHashSet();
        int picked = (settings.SeasonOverlayMissions ?? new List<int>()).Count(ids.Contains);
        var row = new DockPanel { LastChildFill = true };
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
        right.Children.Add(Label(picked > 0 ? string.Format(CultureInfo.CurrentCulture, loc["Checklist.SeasonPicked"], picked) : loc["Checklist.SeasonPickedNone"],
            picked > 0 ? "Brush.Accent" : "Brush.TextMuted", 12, bold: picked > 0));
        if (picked > 0)
        {
            TextBlock clear = Label(loc["Checklist.SeasonClear"], "Brush.TextMuted", 12);
            clear.Margin = new Thickness(12, 0, 0, 0);
            clear.Cursor = Cursors.Hand;
            clear.TextDecorations = TextDecorations.Underline;
            clear.MouseLeftButtonDown += (_, e) =>
            {
                MeterSettings s = MeterSettings.Load();
                s.SeasonOverlayMissions = null;
                s.Save();
                e.Handled = true;
                Render();
            };
            right.Children.Add(clear);
        }

        DockPanel.SetDock(right, Dock.Right);
        row.Children.Add(right);
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        var info = new System.Windows.Documents.Run("ⓘ  ") { FontWeight = FontWeights.Bold };
        info.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Brush.Accent");
        text.Inlines.Add(info);
        text.Inlines.Add(new System.Windows.Documents.Run(loc["Checklist.SeasonOverlayHint"]));
        row.Children.Add(text);
        var banner = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(3, 1, 1, 1), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 12), Child = row };
        banner.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
        banner.SetResourceReference(Border.BorderBrushProperty, "Brush.Accent");
        return banner;
    }

    /// <summary>One half of the Weekly / Whole season switch: its name and how many of its missions are finished; the chosen half raised.</summary>
    private UIElement SeasonSegment(string caption, IReadOnlyCollection<SeasonProgress> list, bool active, Action choose)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(Label(caption, active ? "Brush.Text" : "Brush.TextMuted", 12.5, bold: true));
        TextBlock count = Label($"{list.Count(p => p.Finished)}/{list.Count}", active ? "Brush.Accent" : "Brush.TextSubtle", 11.5, bold: true);
        count.Margin = new Thickness(8, 0, 0, 0);
        line.Children.Add(count);
        var pill = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 4, 14, 5), Child = line };
        if (active)
        {
            pill.SetResourceReference(Border.BackgroundProperty, "Brush.ControlRaised");
            return pill;
        }

        pill.Background = Brushes.Transparent;
        pill.Cursor = Cursors.Hand;
        pill.MouseEnter += (_, _) => pill.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        pill.MouseLeave += (_, _) => pill.Background = Brushes.Transparent;
        pill.MouseLeftButtonDown += (_, e) =>
        {
            choose();
            e.Handled = true;
            Render();
        };
        return pill;
    }

    /// <summary>A category chip: its colour, name and finished / all; the chosen one edged in its colour.</summary>
    private UIElement SeasonChip(string caption, Color? color, IReadOnlyCollection<SeasonProgress> list, bool active, Action choose)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        if (color is { } c)
        {
            line.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(c), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        }

        line.Children.Add(Label(caption, active ? "Brush.Text" : "Brush.TextMuted", 12, bold: active));
        TextBlock count = Label($"{list.Count(p => p.Finished)}/{list.Count}", "Brush.TextSubtle", 11);
        count.Margin = new Thickness(6, 0, 0, 0);
        line.Children.Add(count);
        var chip = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 3, 10, 4), Margin = new Thickness(0, 0, 6, 6), BorderThickness = new Thickness(1), Child = line,
            Cursor = active ? null : Cursors.Hand };
        if (active)
        {
            chip.BorderBrush = new SolidColorBrush(color ?? SeasonLook.Gold);
            chip.SetResourceReference(Border.BackgroundProperty, "Brush.ControlRaised");
        }
        else
        {
            chip.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            chip.Background = Brushes.Transparent;
            chip.MouseLeftButtonDown += (_, e) =>
            {
                choose();
                e.Handled = true;
                Render();
            };
        }

        return chip;
    }

    /// <summary>The week's season points on the reward track: one segment per reward (1,500 ... 8,000), filled as far as the points go.</summary>
    private static UIElement SeasonPoints(int points, bool lastWeek)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        CultureInfo culture = CultureInfo.CurrentCulture;
        var box = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        var top = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 4) };
        int next = SeasonRewards.FirstOrDefault(r => r > points);
        // the game stops this counter at the last chest (8,000): the points go on, but the game doesn't send them, so "8,000+"
        TextBlock right = Label((lastWeek ? "~" : "") + points.ToString("N0", culture) + (next > 0 ? "" : "+") + "  ·  "
            + (next > 0 ? string.Format(culture, loc["Checklist.SeasonNextReward"], next.ToString("N0", culture)) : loc["Checklist.SeasonAllRewards"]),
            next > 0 ? "Brush.Accent" : "Brush.Heal", 12, bold: true);
        right.ToolTip = loc["Checklist.SeasonPointsTip"];
        DockPanel.SetDock(right, Dock.Right);
        top.Children.Add(right);
        top.Children.Add(Label(loc["Checklist.SeasonPoints"], "Brush.Text", 12.5, bold: true));
        box.Children.Add(top);

        var track = new Grid();
        int from = 0;
        for (int i = 0; i < SeasonRewards.Length; i++)
        {
            int to = SeasonRewards[i];
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(to - from, GridUnitType.Star) });
            var cell = new StackPanel { Margin = new Thickness(i == 0 ? 0 : 2, 0, i == SeasonRewards.Length - 1 ? 0 : 2, 0) };
            double fraction = Math.Clamp((double)(points - from) / (to - from), 0, 1);
            Color color = fraction >= 1 ? SeasonLook.Done : SeasonLook.Gold;
            cell.Children.Add(SeasonLook.Bar(fraction, color, 6));
            TextBlock mark = Label(to.ToString("N0", culture), fraction >= 1 ? "Brush.Heal" : "Brush.TextSubtle", 10.5);
            mark.HorizontalAlignment = HorizontalAlignment.Right;
            mark.Margin = new Thickness(0, 2, 0, 0);
            cell.Children.Add(mark);
            Grid.SetColumn(cell, i);
            track.Children.Add(cell);
            from = to;
        }

        box.Children.Add(track);
        return box;
    }

    /// <summary>A category's heading among the tiles: its colour, name and finished / all.</summary>
    private static UIElement SeasonGroupTitle(string category, string language, IReadOnlyCollection<SeasonProgress> list)
    {
        var title = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 5) };
        title.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(2.5), Background = new SolidColorBrush(SeasonLook.Of(category)), Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(45) });
        title.Children.Add(Label(SeasonLook.CategoryName(category, language), "Brush.Text", 13, bold: true));
        TextBlock tally = Label($"{list.Count(x => x.Finished)}/{list.Count}", "Brush.TextSubtle", 11.5);
        tally.Margin = new Thickness(8, 0, 0, 0);
        title.Children.Add(tally);
        return title;
    }

    /// <summary>The tiles of some missions, two a row.</summary>
    private UIElement SeasonTiles(IReadOnlyList<SeasonProgress> list, HashSet<int> pinned, string language)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < list.Count; i++)
        {
            if (i % 2 == 0)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            FrameworkElement tile = SeasonTile(list[i], pinned.Contains(list[i].Mission.Id), language);
            tile.Margin = new Thickness(i % 2 == 0 ? 0 : 4, 0, i % 2 == 0 ? 4 : 0, 8);
            Grid.SetRow(tile, i / 2);
            Grid.SetColumn(tile, i % 2);
            grid.Children.Add(tile);
        }

        return grid;
    }

    /// <summary>One mission as a tile: the category's stripe, the name with its ★, what it asks now, a bar, the stage dots and the count.</summary>
    private FrameworkElement SeasonTile(SeasonProgress p, bool pinned, string language)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        SeasonMission m = p.Mission;
        Color color = SeasonLook.Of(m.Category);
        bool started = p.Count > 0 || p.Done > 0;
        var tile = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), ClipToBounds = true, Opacity = p.Finished ? 0.72 : 1 };
        tile.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
        if (started && !p.Finished)
        {
            tile.BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, color.R, color.G, color.B));
        }
        else
        {
            tile.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        }

        var frame = new Grid();
        frame.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        frame.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        frame.Children.Add(new Border { Background = new SolidColorBrush(started || p.Finished ? color : Color.FromArgb(0x60, color.R, color.G, color.B)) });
        var body = new StackPanel { Margin = new Thickness(10, 7, 10, 8) };
        Grid.SetColumn(body, 1);
        frame.Children.Add(body);
        tile.Child = frame;

        var top = new DockPanel { LastChildFill = true };
        // the overlay pick: "☆ Overlay", gold "★ On overlay" when picked
        var starText = new TextBlock { Text = (pinned ? "★ " : "☆ ") + loc[pinned ? "Checklist.SeasonPickOn" : "Checklist.SeasonPickOff"], FontSize = 11.5, FontWeight = FontWeights.SemiBold };
        var star = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(8, 1, 8, 2), Margin = new Thickness(8, -1, 0, 0), Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Top, Child = starText, ToolTip = loc[pinned ? "Checklist.SeasonUnpin" : "Checklist.SeasonPin"] };
        if (pinned)
        {
            starText.Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x12, 0x05));
            star.Background = new SolidColorBrush(SeasonLook.Gold);
            star.BorderBrush = new SolidColorBrush(SeasonLook.Gold);
        }
        else
        {
            starText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            star.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            star.Background = Brushes.Transparent;
            star.MouseEnter += (_, _) => star.SetResourceReference(Border.BorderBrushProperty, "Brush.Accent");
            star.MouseLeave += (_, _) => star.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        }

        star.MouseLeftButtonDown += (_, e) =>
        {
            MeterSettings s = MeterSettings.Load();
            List<int> chosen = s.SeasonOverlayMissions ?? new List<int>();
            if (!chosen.Remove(m.Id))
            {
                chosen.Add(m.Id);
            }

            s.SeasonOverlayMissions = chosen.Count > 0 ? chosen : null;
            s.Save();
            e.Handled = true;
            Render();
        };
        DockPanel.SetDock(star, Dock.Right);
        top.Children.Add(star);
        TextBlock name = Label(SeasonMissions.Text(m.Name, language), p.Finished ? "Brush.TextMuted" : "Brush.Text", 13.5, bold: true);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        top.Children.Add(name);
        body.Children.Add(top);

        TextBlock what = Label(SeasonMissions.Description(p, language), "Brush.TextMuted", 11.5);
        what.TextTrimming = TextTrimming.CharacterEllipsis;
        what.Margin = new Thickness(0, 1, 0, 7);
        body.Children.Add(what);

        FrameworkElement bar = SeasonLook.Bar(p.Finished ? 1 : p.Next?.Target is null ? (double)p.Done / m.Steps.Count : SeasonMissions.Fraction(p), p.Finished ? SeasonLook.Done : color, 5);
        body.Children.Add(bar);

        var bottom = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 6, 0, 0) };
        TextBlock count = Label(p.Finished ? "✓  " + SeasonLook.CountText(p) : SeasonLook.CountText(p), p.Finished ? "Brush.Heal" : started ? "Brush.Accent" : "Brush.TextSubtle", 12.5, bold: true);
        DockPanel.SetDock(count, Dock.Right);
        bottom.Children.Add(count);
        // the Oath Coins of the next step (the tooltip: of all the steps left)
        if (p.Next is { Coins: > 0 } next)
        {
            TextBlock coins = Label("+" + next.Coins.ToString("N0", CultureInfo.CurrentCulture) + " " + CraftText.ItemName(SeasonMissions.OathCoin), "Brush.Text", 11);
            coins.Foreground = new SolidColorBrush(SeasonLook.Gold);
            coins.Margin = new Thickness(0, 0, 10, 0);
            coins.ToolTip = string.Format(CultureInfo.CurrentCulture, loc["Checklist.SeasonStepCoinsTip"], m.Steps.Skip(p.Done).Sum(x => x.Coins).ToString("N0", CultureInfo.CurrentCulture));
            DockPanel.SetDock(coins, Dock.Right);
            bottom.Children.Add(coins);
        }

        var dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < m.Steps.Count && m.Steps.Count <= 12; i++)
        {
            var dot = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(3.5), Margin = new Thickness(0, 0, 3, 0), BorderThickness = new Thickness(1) };
            if (i < p.Done)
            {
                dot.Background = new SolidColorBrush(p.Finished ? SeasonLook.Done : color);
            }
            else
            {
                dot.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            }

            dots.Children.Add(dot);
        }

        TextBlock stage = Label(string.Format(CultureInfo.CurrentCulture, loc["Checklist.SeasonStage"], p.Done, m.Steps.Count), "Brush.TextSubtle", 11);
        stage.Margin = new Thickness(5, 0, 0, 0);
        dots.Children.Add(stage);
        bottom.Children.Add(dots);
        body.Children.Add(bottom);

        SeasonLook.AttachCard(tile, () => SeasonLook.Card(p, language));
        return tile;
    }

    /// <summary>One line of a section: name, number, next refill - the same column widths in every section, so the numbers stand under each other.</summary>
    private static Grid RowGrid()
    {
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        return grid;
    }

    private static TextBlock Label(string text, string brush, double size, bool bold = false)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    private static Border Card(UIElement child)
    {
        var border = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(0, 0, 0, 12), Child = child };
        border.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        border.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return border;
    }
}
