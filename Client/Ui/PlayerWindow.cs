using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;
using AionDPS.Upload;

namespace AionDPS.Ui;

/// <summary>
/// The player window (6 November 2026, the owner: "a tab in game, not a redirect to the website, so people don't have to alt-tab; a clean tab with all details
/// of the person if they are using our app; if they are not, genus/pets are hidden, since we cannot get that info any other way"). Opened from Find a player:
/// the character's official page as the website reads it (gear with its lines and stones, Arcana, stats, skills, the pet and wing, Daevanion, titles), its best
/// parses, and, when its player uses Atreia App, Genus Insight and the pets. Over the game, dragged by its header, closed with Esc or ×.
/// </summary>
public sealed class PlayerWindow : Window
{
    private readonly PlayerView _view;

    public PlayerWindow(FoundPlayer who)
    {
        Title = who.Name + " · " + Brand.AppName;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        AltTabHidden.Apply(this);
        FontFamily = ThemeManager.UiFont;
        _view = new PlayerView(who, () => Close(), DragFromHeader);
        Content = _view;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        };
        Loaded += async (_, _) => await _view.LoadAsync(false);
    }

    /// <summary>Another character in the same window.</summary>
    public async Task ShowPlayerAsync(FoundPlayer who)
    {
        Title = who.Name + " · " + Brand.AppName;
        await _view.SwitchAsync(who);
    }

    private void DragFromHeader()
    {
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // the button was let go before the move began
        }
    }
}

/// <summary>The player window's content (also drawn on its own for the pictures of the meter).</summary>
public sealed class PlayerView : Border
{
    private static readonly Dictionary<string, ImageSource?> Remote = new(StringComparer.Ordinal);
    private static readonly Color[] GenusGrades = { Color.FromRgb(0x8A, 0x93, 0x9E), Color.FromRgb(0xD8, 0xDD, 0xE3), Color.FromRgb(0x5B, 0xD3, 0x6B), Color.FromRgb(0x4A, 0xA8, 0xFF),
        Color.FromRgb(0xFF, 0xC9, 0x4D), Color.FromRgb(0xFF, 0x8A, 0x3D) };
    private static readonly string[] TabNames = { "Overview", "Gear", "Stats", "Skills", "Genus & Pets" };

    private FoundPlayer _who;
    private PlayerPage? _page;
    private string _tab = "Overview";
    private readonly Action _close;
    private readonly Action _drag;
    private readonly StackPanel _header = new();
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 14, 8) };
    private readonly ContentControl _body = new();
    private readonly TextBlock _foot = new() { FontSize = 10.5, Margin = new Thickness(14, 6, 14, 10), TextWrapping = TextWrapping.Wrap };
    private readonly ScrollViewer _scroll;
    private int _asked;

    public PlayerView(FoundPlayer who, Action close, Action drag)
    {
        _who = who;
        _close = close;
        _drag = drag;
        Width = 560;
        CornerRadius = new CornerRadius(12);
        BorderThickness = new Thickness(1);
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x14, 0x12, 0x18)); // never see-through: every part takes the mouse over the game
        _foot.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
        _scroll = new ScrollViewer
        {
            Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = Math.Max(320, SystemParameters.WorkArea.Height * 0.62), Padding = new Thickness(14, 0, 14, 0),
        };
        var dock = new DockPanel();
        DockPanel.SetDock(_header, Dock.Top);
        DockPanel.SetDock(_tabs, Dock.Top);
        DockPanel.SetDock(_foot, Dock.Bottom);
        dock.Children.Add(_header);
        dock.Children.Add(_tabs);
        dock.Children.Add(_foot);
        dock.Children.Add(_scroll);
        Child = dock;
        DrawHeader();
        DrawTabs();
        _body.Content = Muted("Reading " + who.Name + "…", new Thickness(0, 10, 0, 16));
    }

    public async Task LoadAsync(bool fresh)
    {
        int asked = ++_asked;
        if (_page is null)
        {
            _body.Content = Muted("Reading " + _who.Name + "…", new Thickness(0, 10, 0, 16));
        }
        else
        {
            _foot.Text = "Reading the character again…";
        }

        PlayerAnswer answer = await PlayerClient.GetAsync(_who, fresh);
        if (asked != _asked)
        {
            return;
        }

        if (answer.Page is null)
        {
            if (_page is null)
            {
                ShowError(answer.Error ?? "The character can't be read right now.");
            }
            else
            {
                DrawFoot(answer.Error);
            }

            return;
        }

        Show(answer.Page);
    }

    public async Task SwitchAsync(FoundPlayer who)
    {
        _who = who;
        _page = null;
        _tab = "Overview";
        DrawHeader();
        DrawTabs();
        await LoadAsync(false);
    }

    /// <summary>A page read (or made up, for the pictures).</summary>
    public void Show(PlayerPage page)
    {
        _page = page;
        if (_tab == "Genus & Pets" && page.App is null)
        {
            _tab = "Overview";
        }

        DrawHeader();
        DrawTabs();
        DrawBody();
        DrawFoot(null);
    }

    /// <summary>The first piece with lines opened, as after a click on it (the pictures).</summary>
    internal void OpenFirstPiece()
    {
        if (_body.Content is StackPanel stack)
        {
            foreach (StackPanel holder in stack.Children.OfType<StackPanel>())
            {
                if (holder.Children.Count == 2 && holder.Children[1] is StackPanel details)
                {
                    details.Visibility = Visibility.Visible;
                    return;
                }
            }
        }
    }

    /// <summary>All of a tab at once, no scrolling (the pictures).</summary>
    internal void ShowAll() => _scroll.MaxHeight = double.PositiveInfinity;

    /// <summary>One of the tabs (the pictures).</summary>
    public void SelectTab(string tab)
    {
        _tab = tab;
        DrawTabs();
        DrawBody();
    }

    private void ShowError(string error)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 10, 0, 16) };
        stack.Children.Add(Text(error, 12.5, "Brush.OverlayText", wrap: true));
        var again = ActionButton("Try again", () => _ = LoadAsync(false));
        again.Margin = new Thickness(0, 10, 0, 0);
        again.HorizontalAlignment = HorizontalAlignment.Left;
        stack.Children.Add(again);
        _body.Content = stack;
    }

    // ------------------------------------------------------------------ header, tabs, footer

    private void DrawHeader()
    {
        _header.Children.Clear();
        var top = new Grid { Margin = new Thickness(14, 12, 10, 10), Background = Brushes.Transparent };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                _drag();
            }
        };
        string cls = _page?.ClassName is { Length: > 0 } c ? c : _who.ClassName ?? "";
        var frame = new Border
        {
            Width = 48, Height = 48, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 12, 0),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            Child = new Image { Width = 32, Height = 32, Source = cls.Length > 0 ? new ClassIconConverter().Convert(cls, typeof(ImageSource), null, CultureInfo.CurrentCulture) as ImageSource : null },
        };
        top.Children.Add(frame);
        var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = _page?.Name ?? _who.Name, FontSize = 19, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        name.Foreground = cls.Length > 0 ? ClassBrushes.Text(cls) : (Brush)FindResource("Brush.OverlayText");
        who.Children.Add(name);
        var parts = new List<string>();
        if ((_page?.Level ?? _who.Level) is int lv)
        {
            parts.Add("Lv " + lv.ToString(CultureInfo.CurrentCulture));
        }

        if (cls.Length > 0)
        {
            parts.Add(cls);
        }

        parts.Add(_page?.Server is { Length: > 0 } sv ? sv : _who.Server);
        parts.Add(FindPlayerClient.Regions.FirstOrDefault(r => r.Code == (_page?.Region ?? _who.Region)).Name ?? _who.Region.ToUpperInvariant());
        who.Children.Add(Text(string.Join("  ·  ", parts.Where(p => p.Length > 0)), 12, "Brush.TextMuted"));
        if (_page is { } p2 && (p2.Legion.Length > 0 || p2.Title.Length > 0))
        {
            who.Children.Add(Text(string.Join("  ·  ", new[] { p2.Legion.Length > 0 ? "<" + p2.Legion + ">" : "", p2.Title }.Where(x => x.Length > 0)), 11.5, "Brush.TextSubtle"));
        }

        Grid.SetColumn(who, 1);
        top.Children.Add(who);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        buttons.Children.Add(IconButton("", "Read the character again from the game's page (once in ten minutes)", () => _ = LoadAsync(true)));
        if ((_page?.Page ?? _who.Page) is { } site)
        {
            buttons.Children.Add(IconButton("", "Open on the website", () => OpenInBrowser(site)));
        }

        buttons.Children.Add(IconButton("", "Close (Esc)", _close));
        Grid.SetColumn(buttons, 2);
        top.Children.Add(buttons);
        _header.Children.Add(top);

        // the figures under the name
        var tiles = new UniformGrid { Columns = 3, Margin = new Thickness(14, 0, 14, 10) };
        int? cp = _page?.CombatPower ?? _who.CombatPower, il = _page?.ItemLevel ?? _who.ItemLevel;
        tiles.Children.Add(Tile("Combat power", cp is int x ? Num(x) : "–", Color.FromRgb(0xE8, 0xC3, 0x5A), null));
        tiles.Children.Add(Tile("Item level", il is int y ? Num(y) : "–", null, null));
        PlayerParse? best = _page?.Parses.OrderByDescending(p => p.Pct).FirstOrDefault();
        int? pct = best?.Pct ?? _who.BestPercent;
        tiles.Children.Add(Tile("Best parse", pct is int q ? q.ToString(CultureInfo.CurrentCulture) + "%" : "–", pct is int q2 ? MainWindow.ParseColour(q2) : null,
            best is not null ? $"{best.Boss}: rank {best.Rank} of {best.Of}" : "No parse: this character's fights were never uploaded with Atreia App (or are not public)"));
        _header.Children.Add(tiles);
    }

    private void DrawTabs()
    {
        _tabs.Children.Clear();
        foreach (string tab in TabNames)
        {
            if (tab == "Genus & Pets" && _page?.App is null)
            {
                continue; // only the player's own app knows them
            }

            bool on = tab == _tab;
            var pill = new Border
            {
                CornerRadius = new CornerRadius(7), Padding = new Thickness(11, 4, 11, 4), Margin = new Thickness(0, 0, 5, 0), Cursor = Cursors.Hand, BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(on ? Color.FromArgb(0x99, 0xDC, 0xB8, 0x77) : Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                Background = new SolidColorBrush(on ? Color.FromArgb(0x2A, 0xDC, 0xB8, 0x77) : Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF)),
            };
            var text = Text(tab, 12, on ? "Brush.OverlayText" : "Brush.TextMuted");
            text.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            pill.Child = text;
            string which = tab;
            pill.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                if (_page is not null)
                {
                    SelectTab(which);
                }
            };
            _tabs.Children.Add(pill);
        }
    }

    private void DrawFoot(string? error)
    {
        var parts = new List<string>();
        if (_page?.ReadAt is DateTime read)
        {
            parts.Add("Game page read " + Ago(read));
        }

        if (_page?.App is { Updated: DateTime sent })
        {
            parts.Add("Genus and pets from their Atreia App " + Ago(sent));
        }
        else if (_page is not null)
        {
            parts.Add("Genus Insight and pets: only for players of Atreia App");
        }

        if (error is not null)
        {
            parts.Add(error);
        }

        _foot.Text = string.Join("  ·  ", parts);
    }

    private void DrawBody()
    {
        if (_page is not { } p)
        {
            return;
        }

        _body.Content = _tab switch
        {
            "Gear" => GearTab(p),
            "Stats" => StatsTab(p),
            "Skills" => SkillsTab(p),
            "Genus & Pets" when p.App is { } app => GenusTab(app),
            _ => OverviewTab(p),
        };
        _scroll.ScrollToTop();
    }

    // ------------------------------------------------------------------ tabs

    private UIElement OverviewTab(PlayerPage p)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 2, 0, 6) };
        stack.Children.Add(Heading("Best parses"));
        if (p.Parses.Count == 0)
        {
            stack.Children.Add(Muted("None: this character's boss fights were never uploaded with Atreia App, or are not public."));
        }
        else
        {
            foreach (PlayerParse x in p.Parses)
            {
                var row = Row();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                Put(row, 0, Text(x.Boss, 12.5, "Brush.OverlayText"));
                Put(row, 1, Right(Text(x.Dps is double d ? PlayerRow.Compact((long)Math.Round(d)) + " DPS" : "", 12, "Brush.TextMuted")));
                var pct = Right(new TextBlock { Text = x.Pct.ToString(CultureInfo.CurrentCulture) + "%", FontSize = 13, FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(MainWindow.ParseColour(x.Pct)) });
                Put(row, 2, pct);
                Put(row, 3, Right(Text($"#{x.Rank} of {x.Of}", 11.5, "Brush.TextSubtle")));
                stack.Children.Add(row);
            }
        }

        if (p.Daevanion.Count > 0)
        {
            stack.Children.Add(Heading("Daevanion"));
            foreach (PlayerBoard b in p.Daevanion)
            {
                var row = Row();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
                Put(row, 0, Text(b.Name, 12.5, "Brush.OverlayText"));
                double share = b.Total > 0 ? Math.Clamp((double)b.Open / b.Total, 0, 1) : 0;
                var track = new Grid { Height = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 8, 0) };
                track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(share, GridUnitType.Star) });
                track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - share, GridUnitType.Star) });
                var groove = new Border { CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)) };
                Grid.SetColumnSpan(groove, 2);
                track.Children.Add(groove);
                track.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromRgb(0xC9, 0xA4, 0x6E)) });
                Put(row, 1, track);
                Put(row, 2, Right(Text($"{b.Open} / {b.Total}", 11.5, "Brush.TextMuted")));
                stack.Children.Add(row);
            }
        }

        stack.Children.Add(Heading("Companions and titles"));
        if (p.Pet is { } pet)
        {
            stack.Children.Add(Pair("Pet", pet.Name + (pet.Level is int pl ? "  ·  Lv " + pl.ToString(CultureInfo.CurrentCulture) : "")));
        }

        if (p.Wing is { } wing)
        {
            stack.Children.Add(Pair("Wings", wing.Name, GradeColor(wing.Grade)));
        }

        if (p.TitlesOwned is int owned)
        {
            stack.Children.Add(Pair("Titles", owned.ToString("N0", CultureInfo.CurrentCulture) + (p.TitlesTotal is int all ? " of " + all.ToString("N0", CultureInfo.CurrentCulture) : "")));
        }

        if (p.App is null)
        {
            stack.Children.Add(Muted("Genus Insight and the pet collection show for players of Atreia App only: the game's character pages don't have them.", new Thickness(0, 12, 0, 4)));
        }

        return stack;
    }

    private UIElement GearTab(PlayerPage p)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 2, 0, 6) };
        if (p.Gear.Count == 0)
        {
            stack.Children.Add(Muted("The game's character page shows no gear for this character right now."));
            return stack;
        }

        foreach (bool arcana in new[] { false, true })
        {
            var pieces = p.Gear.Where(g => g.Arcana == arcana).ToList();
            if (pieces.Count == 0)
            {
                continue;
            }

            stack.Children.Add(Heading(arcana ? "Arcana" : "Equipment"));
            foreach (PlayerPiece g in pieces)
            {
                stack.Children.Add(GearRow(g));
            }
        }

        stack.Children.Add(Muted("Click a piece for its attributes and stones.", new Thickness(0, 8, 0, 0)));
        return stack;
    }

    private UIElement GearRow(PlayerPiece g)
    {
        var holder = new StackPanel();
        var row = new Grid { Margin = new Thickness(0, 1, 0, 1), Cursor = g.HasDetails ? Cursors.Hand : null, Background = new SolidColorBrush(Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF)) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Color grade = GradeColor(g.Grade);
        var icon = new Border
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(6), Margin = new Thickness(2, 3, 6, 3), BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x88, grade.R, grade.G, grade.B)), Background = new SolidColorBrush(Color.FromArgb(0x22, grade.R, grade.G, grade.B)),
            Child = new Image { Source = RemoteImage(g.Icon), Width = 30, Height = 30 },
        };
        Put(row, 0, icon);
        var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var line = new TextBlock { FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis };
        if (g.Enchant > 0)
        {
            line.Inlines.Add(new System.Windows.Documents.Run("+" + g.Enchant.ToString(CultureInfo.CurrentCulture) + " ") { Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xE6, 0xC8)), FontWeight = FontWeights.SemiBold });
        }

        line.Inlines.Add(new System.Windows.Documents.Run(g.Name) { Foreground = new SolidColorBrush(grade), FontWeight = FontWeights.SemiBold });
        if (g.Exceed > 0)
        {
            line.Inlines.Add(new System.Windows.Documents.Run("  ✦" + g.Exceed.ToString(CultureInfo.CurrentCulture)) { Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x3D)) });
        }

        who.Children.Add(line);
        who.Children.Add(Text(g.Slot + (g.Stones.Count > 0 ? "  ·  " + g.Stones.Count.ToString(CultureInfo.CurrentCulture) + (g.Stones.Count == 1 ? " stone" : " stones") : ""), 11, "Brush.TextSubtle"));
        Put(row, 1, who);
        if (g.ItemLevel is int il)
        {
            Put(row, 2, new TextBlock { Text = Num(il), FontSize = 12, Margin = new Thickness(6, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("Brush.TextMuted") });
        }

        holder.Children.Add(row);
        if (g.HasDetails)
        {
            var details = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(44, 2, 8, 8) };
            Section(details, "Base attributes", g.Main);
            Section(details, "Additional attributes", g.Sub);
            Section(details, "Skill levels", g.Skills);
            Section(details, "Manastones", g.Stones);
            Section(details, "Theostones", g.Godstones);
            holder.Children.Add(details);
            row.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                details.Visibility = details.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            };
        }

        return holder;
    }

    private void Section(StackPanel into, string title, IReadOnlyList<PlayerLine> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }

        into.Children.Add(Text(title.ToUpperInvariant(), 10, "Brush.TextSubtle", margin: new Thickness(0, 6, 0, 2)));
        foreach (PlayerLine l in lines)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = Text(l.Name, 12, "Brush.OverlayText");
            if (l.Grade is { Length: > 0 } grade)
            {
                name.Foreground = new SolidColorBrush(GradeColor(grade));
            }

            Put(row, 0, name);
            Put(row, 1, Text(l.Value, 12, "Brush.OverlayText"));
            into.Children.Add(row);
            if (l.Desc is { Length: > 0 } desc)
            {
                if (l.Value.Length == 0)
                {
                    into.Children.Add(Text(desc, 11, "Brush.TextMuted", wrap: true, margin: new Thickness(0, 0, 0, 3))); // a Theostone: what it does
                }
                else
                {
                    name.ToolTip = desc;
                }
            }
        }
    }

    private UIElement StatsTab(PlayerPage p)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 2, 0, 6) };
        if (p.Stats.Count == 0)
        {
            stack.Children.Add(Muted("The game's character page shows no stats for this character right now."));
            return stack;
        }

        var grid = new UniformGrid { Columns = 2 };
        foreach (var (name, value) in p.Stats)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 14, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Put(row, 0, Text(name, 12, "Brush.TextMuted", margin: new Thickness(0, 3, 0, 3)));
            var v = Text(value, 12.5, "Brush.OverlayText", margin: new Thickness(8, 3, 0, 3));
            v.FontWeight = FontWeights.SemiBold;
            Put(row, 1, v);
            grid.Children.Add(row);
        }

        stack.Children.Add(grid);
        return stack;
    }

    private UIElement SkillsTab(PlayerPage p)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 2, 0, 6) };
        if (p.Skills.Count == 0)
        {
            stack.Children.Add(Muted("The game's character page shows no skills for this character right now."));
            return stack;
        }

        foreach (bool equipped in new[] { true, false })
        {
            var skills = p.Skills.Where(s => s.Equipped == equipped && (equipped || s.Level is > 0)).OrderByDescending(s => s.Level ?? 0).ToList();
            if (skills.Count == 0)
            {
                continue;
            }

            stack.Children.Add(Heading(equipped ? "On the skill bar" : "Learned"));
            var grid = new UniformGrid { Columns = 2 };
            foreach (PlayerSkill s in skills)
            {
                var row = new Grid { Margin = new Thickness(0, 1, 12, 1) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Put(row, 0, new Image { Source = RemoteImage(s.Icon), Width = 26, Height = 26, Margin = new Thickness(0, 0, 6, 0) });
                var name = Text(s.Name, 12, "Brush.OverlayText");
                name.ToolTip = s.Category.Length > 0 ? s.Category : null;
                Put(row, 1, name);
                if (s.Level is int lv)
                {
                    Put(row, 2, Text("Lv " + lv.ToString(CultureInfo.CurrentCulture), 11.5, "Brush.TextMuted", margin: new Thickness(6, 0, 0, 0)));
                }

                grid.Children.Add(row);
            }

            stack.Children.Add(grid);
        }

        return stack;
    }

    private UIElement GenusTab(PlayerAppData app)
    {
        string lang = LocalizationManager.Instance.Language;
        var stack = new StackPanel { Margin = new Thickness(0, 2, 0, 6) };
        stack.Children.Add(Heading("Genus Insight"));
        if (app.Genus.Count == 0)
        {
            stack.Children.Add(Muted("Not seen by their app yet."));
        }
        else
        {
            var cards = new UniformGrid { Columns = 2 };
            foreach (PlayerGenus g in app.Genus.OrderBy(x => x.SpeciesId))
            {
                var card = new StackPanel();
                var head = new Grid { Margin = new Thickness(0, 0, 0, 5) };
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var title = Text(Aion2Artwork.SpeciesName(g.SpeciesId, lang), 13.5, "Brush.OverlayText");
                title.FontWeight = FontWeights.SemiBold;
                title.Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xD3, 0xC2));
                Put(head, 0, title);
                Put(head, 1, Text("Level " + g.Level.ToString(CultureInfo.CurrentCulture), 11.5, "Brush.TextMuted"));
                card.Children.Add(head);
                int page = g.Lines.Count == 0 ? 1 : g.Lines.Min(l => l.Page); // the board in use
                foreach (var l in g.Lines.Where(l => l.Page == page).OrderBy(l => l.Slot))
                {
                    string token = Aion2CpData.Token(l.Stat) ?? "#" + l.Stat.ToString(CultureInfo.InvariantCulture);
                    var row = new Grid();
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var stat = Text(Aion2CpData.Name(token, lang), 11.5, "Brush.OverlayText");
                    if (l.Grade > 0)
                    {
                        stat.Foreground = new SolidColorBrush(GenusGrades[Math.Clamp(l.Grade, 0, GenusGrades.Length - 1)]); // as the meter's own Genus page colours them
                    }

                    Put(row, 0, stat);
                    string value = Aion2CpData.Percent(token) ? (l.Value / 100.0).ToString("0.##", CultureInfo.CurrentCulture) + " %" : l.Value.ToString("N0", CultureInfo.CurrentCulture);
                    Put(row, 1, Text("+" + value, 11.5, "Brush.OverlayText"));
                    card.Children.Add(row);
                }

                cards.Children.Add(new Border
                {
                    Child = card, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(10, 8, 10, 8), CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(1),
                });
            }

            stack.Children.Add(cards);
        }

        stack.Children.Add(Heading("Pets"));
        if (app.Pets.Count == 0)
        {
            stack.Children.Add(Muted("Not seen by their app yet."));
            return stack;
        }

        for (int i = 0; i < Aion2Pets.Species.Length; i++)
        {
            string species = Aion2Pets.Species[i];
            var mine = app.Pets.Where(x => Aion2Pets.SpeciesOf(x.PetId) == species).OrderByDescending(x => x.Level).ThenBy(x => x.PetId).ToList();
            int inGame = Aion2Pets.AllPetIds.Count(id => Aion2Pets.SpeciesOf(id) == species);
            if (mine.Count == 0 && inGame == 0)
            {
                continue;
            }

            var head = new Grid { Margin = new Thickness(0, 6, 0, 3) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = Text(Aion2Artwork.SpeciesName(i + 2, lang), 12.5, "Brush.OverlayText");
            title.FontWeight = FontWeights.SemiBold;
            Put(head, 0, title);
            string counts = $"{mine.Count} of {Math.Max(inGame, mine.Count)}  ·  Lv 3: {mine.Count(x => x.Level >= 3)}  ·  Lv 2: {mine.Count(x => x.Level == 2)}  ·  Lv 1: {mine.Count(x => x.Level == 1)}";
            Put(head, 1, Text(counts, 11, "Brush.TextMuted"));
            stack.Children.Add(head);
            var icons = new WrapPanel();
            foreach (var (petId, level) in mine)
            {
                icons.Children.Add(PetChip(petId, level, lang));
            }

            stack.Children.Add(icons);
        }

        return stack;
    }

    private static UIElement PetChip(int petId, int level, string lang)
    {
        ImageSource? source = null;
        if (Aion2Pets.PetIconPath(petId) is { } path)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.DecodePixelWidth = 48;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                source = bitmap;
            }
            catch (Exception ex) when (ex is System.IO.IOException or NotSupportedException or UriFormatException or ArgumentException)
            {
                source = null;
            }
        }

        Color ring = level >= 3 ? Color.FromRgb(0xF0, 0xC0, 0x60) : level == 2 ? Color.FromRgb(0x5B, 0xB8, 0xF0) : Color.FromRgb(0x8A, 0x93, 0x9E);
        var grid = new Grid { Width = 34, Height = 34, Margin = new Thickness(0, 0, 4, 4), ToolTip = (Aion2Pets.PetName(petId, lang) ?? "Pet " + petId) + "  ·  Lv " + level };
        grid.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(17), BorderThickness = new Thickness(1.5), BorderBrush = new SolidColorBrush(ring),
            Background = new SolidColorBrush(Color.FromArgb(0x22, ring.R, ring.G, ring.B)), Child = new Image { Source = source, Width = 28, Height = 28 },
        });
        grid.Children.Add(new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(6), Padding = new Thickness(3, 0, 3, 0),
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x14, 0x12, 0x18)), BorderBrush = new SolidColorBrush(ring), BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = level.ToString(CultureInfo.CurrentCulture), FontSize = 9, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(ring) },
        });
        return grid;
    }

    // ------------------------------------------------------------------ small pieces

    /// <summary>The website's colours of the game's grades.</summary>
    internal static Color GradeColor(string? grade)
    {
        string s = (grade ?? "").ToLowerInvariant();
        return s switch
        {
            _ when s.Contains("myth") || s.Contains("hero") || s.Contains("eternal") || s.Contains("ancient") => Color.FromRgb(0xFF, 0x6F, 0x7D),
            _ when s.Contains("special") => Color.FromRgb(0xFF, 0x94, 0x66),
            _ when s.Contains("epic") || s.Contains("purple") => Color.FromRgb(0xC4, 0x8C, 0xFF),
            _ when s.Contains("unique") || s.Contains("gold") => Color.FromRgb(0xF0, 0xC0, 0x60),
            _ when s.Contains("legend") => Color.FromRgb(0x6C, 0xB4, 0xFF),
            _ when s.Contains("rare") || s.Contains("green") => Color.FromRgb(0x5F, 0xCF, 0x86),
            _ when s.Contains("common") || s.Contains("normal") => Color.FromRgb(0xE4, 0xE0, 0xD8),
            _ => Color.FromRgb(0xE4, 0xE0, 0xD8),
        };
    }

    /// <summary>A picture of the game's site, downloaded once per run.</summary>
    private static ImageSource? RemoteImage(string? url)
    {
        if (url is null)
        {
            return null;
        }

        lock (Remote)
        {
            if (Remote.TryGetValue(url, out ImageSource? had))
            {
                return had;
            }
        }

        ImageSource? image = null;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(url, UriKind.Absolute);
            bitmap.DecodePixelWidth = 64;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            image = bitmap;
        }
        catch (Exception ex) when (ex is NotSupportedException or UriFormatException or ArgumentException or System.Net.WebException or System.IO.IOException)
        {
            image = null;
        }

        lock (Remote)
        {
            Remote[url] = image;
        }

        return image;
    }

    private Border Tile(string label, string value, Color? colour, string? tip)
    {
        var stack = new StackPanel();
        stack.Children.Add(Text(label, 10.5, "Brush.TextMuted"));
        var v = new TextBlock { Text = value, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 1, 0, 0) };
        if (colour is Color c)
        {
            v.Foreground = new SolidColorBrush(c);
        }
        else
        {
            v.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverlayText");
        }

        stack.Children.Add(v);
        return new Border
        {
            Child = stack, Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 6, 0), CornerRadius = new CornerRadius(8), ToolTip = tip,
            Background = new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
        };
    }

    private static Border IconButton(string glyph, string tip, Action click)
    {
        var text = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        var b = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand, ToolTip = tip, Child = text, Margin = new Thickness(2, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF)) };
        b.MouseEnter += (_, _) => b.Background = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
        b.MouseLeave += (_, _) => b.Background = new SolidColorBrush(Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF));
        b.MouseLeftButtonDown += (_, e) => e.Handled = true; // not a drag of the header
        b.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            click();
        };
        return b;
    }

    private static Border ActionButton(string label, Action click)
    {
        var text = new TextBlock { Text = label, FontSize = 12.5, FontWeight = FontWeights.SemiBold };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverlayText");
        var b = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(14, 6, 14, 6), Cursor = Cursors.Hand, Child = text, BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)), Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)) };
        b.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            click();
        };
        return b;
    }

    private static TextBlock Text(string text, double size, string brush, bool wrap = false, Thickness? margin = null)
    {
        var t = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center, TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, Margin = margin ?? new Thickness(0) };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    private static TextBlock Muted(string text, Thickness? margin = null) => Text(text, 11.5, "Brush.TextMuted", wrap: true, margin: margin ?? new Thickness(0, 2, 0, 4));

    private static TextBlock Heading(string text)
    {
        var t = Text(text.ToUpperInvariant(), 10.5, "Brush.Accent", margin: new Thickness(0, 12, 0, 5));
        t.FontWeight = FontWeights.SemiBold;
        return t;
    }

    private static Grid Row() => new() { Margin = new Thickness(0, 2, 0, 2) };

    private static Grid Pair(string label, string value, Color? colour = null)
    {
        var row = Row();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Put(row, 0, Text(label, 12, "Brush.TextMuted"));
        var v = Text(value, 12.5, "Brush.OverlayText");
        if (colour is Color c)
        {
            v.Foreground = new SolidColorBrush(c);
        }

        Put(row, 1, v);
        return row;
    }

    private static T Right<T>(T e) where T : FrameworkElement
    {
        e.HorizontalAlignment = HorizontalAlignment.Right;
        return e;
    }

    private static void Put(Grid grid, int column, UIElement e)
    {
        Grid.SetColumn(e, column);
        grid.Children.Add(e);
    }

    private static string Num(int v) => v.ToString("N0", CultureInfo.CurrentCulture);

    private static string Ago(DateTime utc)
    {
        TimeSpan d = DateTime.UtcNow - utc;
        return d.TotalMinutes < 1 ? "just now" : d.TotalHours < 1 ? $"{(int)d.TotalMinutes} min ago" : d.TotalDays < 1 ? $"{(int)d.TotalHours} h ago" : $"{(int)d.TotalDays} d ago";
    }

    private static void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // no browser to open: nothing else to do
        }
    }
}
