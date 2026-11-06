using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;
using AionDPS.History;
using AionDPS.Upload;
using static AionDPS.Ui.JourneyViews;

namespace AionDPS.Ui;

/// <summary>
/// Legion (an entry of its own in the column, under Overlays; 6 November 2026, the owner: "a legion tab, which finds your legion via packets and lists all the members, we can use
/// this for easy access to add guilds to the disc bot as well ... a nice page called legion similar to character/journey design"). The legion's member list
/// the game sends at every login (frame 0x058a: each member's class, level, Item Level, Combat Power, when last active and where), kept on this PC
/// (<see cref="LegionStore"/>), with what the Atreia bot has of the legion (<see cref="LegionInfoClient"/>: the Discord servers following it, who is linked
/// there). The legion on a rail at the left (its numbers, its classes, its Discord with the bot's add link and the command to follow it), the members on the
/// right, to search and sort.
/// </summary>
internal sealed class LegionPage : Grid
{
    private enum Sort { Cp, ItemLevel, Name, Active, Class, Rank }

    private readonly Func<Aion2EntityDirectory?> _directory;
    private readonly Action? _openAccount;
    private readonly StackPanel _root = new() { Margin = new Thickness(24, 18, 24, 24), MaxWidth = 1200 };
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private List<LegionSnapshot> _legions = new();
    private LegionSnapshot? _shown;
    private LegionInfo? _info;
    private string? _infoFor;
    private bool _infoLoading;
    private Sort _sort = Sort.Cp;
    private string _search = "";
    private StackPanel? _list;

    private static LocalizationManager Loc => LocalizationManager.Instance;

    private static readonly Color Gold = Color.FromRgb(0xE6, 0xC0, 0x68);
    private static readonly Color Blurple = Color.FromRgb(0x8C, 0x9E, 0xFF);

    public LegionPage(Func<Aion2EntityDirectory?> directory, Action? openAccount = null)
    {
        _directory = directory;
        _openAccount = openAccount;
        Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _root });
        if (MainWindow.Headless)
        {
            LegionStore.UseForPictures(new[] { SampleLegion() });
        }

        Load();
        if (!MainWindow.Headless)
        {
            // a login while the page is open brings the new list
            _timer.Tick += (_, _) =>
            {
                if (LegionStore.NoteLive(_directory()))
                {
                    Load();
                }
            };
            _timer.Start();
            LegionShare.Changed += OnShared;
            Account.Changed += OnShared;
        }
    }

    public void Stop()
    {
        _timer.Stop();
        LegionShare.Changed -= OnShared;
        Account.Changed -= OnShared;
    }

    /// <summary>A send of the list had an outcome, or the sign-in changed: the Discord box anew (not on the UI thread).</summary>
    private void OnShared() => Dispatcher.BeginInvoke(() =>
    {
        _infoFor = null;
        LoadInfo();
        Draw();
    });

    private void Load()
    {
        if (!MainWindow.Headless)
        {
            LegionStore.NoteLive(_directory());
        }

        _legions = LegionStore.Legions().ToList();
        _shown = _shown is { } was ? _legions.FirstOrDefault(x => x.Legion == was.Legion && x.ServerId == was.ServerId) ?? _legions.FirstOrDefault() : _legions.FirstOrDefault();
        LoadInfo();
        Draw();
    }

    private void LoadInfo()
    {
        if (_shown is not { } legion)
        {
            return;
        }

        string key = legion.Legion + "@" + legion.ServerId;
        if (_infoFor == key)
        {
            return;
        }

        _infoFor = key;
        _info = null;
        if (MainWindow.Headless)
        {
            _info = new LegionInfo(legion.Legion, new[] { new LegionDiscord("Dawn HQ", true, "https://discord.gg/example", "English", "Evenings CET", 9) }, true,
                legion.Members.Take(9).Select(m => m.Name).ToList(), LegionInfoClient.AddUrl);
            return;
        }

        _infoLoading = true;
        _ = Task.Run(async () =>
        {
            LegionInfo? info = await LegionInfoClient.GetAsync(legion.Legion, legion.ServerId);
            _ = Dispatcher.BeginInvoke(() =>
            {
                if (_infoFor == key)
                {
                    _info = info;
                    _infoLoading = false;
                    Draw();
                }
            });
        });
    }

    // ---- the page ----

    private void Draw()
    {
        _root.Children.Clear();
        _root.Children.Add(Head());
        if (_shown is not { } legion)
        {
            _root.Children.Add(Panel(Wrapped(Loc["Legion.None"], "Brush.TextMuted", 13)));
            return;
        }

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(286) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var rail = new StackPanel();
        rail.Children.Add(LegionCard(legion));
        rail.Children.Add(Classes(legion));
        rail.Children.Add(Discord(legion));
        body.Children.Add(rail);
        var main = new StackPanel { Margin = new Thickness(18, 0, 0, 0) };
        main.Children.Add(Scoreboard(legion));
        main.Children.Add(Members(legion));
        Grid.SetColumn(main, 1);
        body.Children.Add(main);
        _root.Children.Add(body);
    }

    private UIElement Head()
    {
        var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        if (_legions.Count > 1)
        {
            var pick = new ComboBox { MinWidth = 220, VerticalAlignment = VerticalAlignment.Center };
            foreach (LegionSnapshot l in _legions)
            {
                pick.Items.Add(l.Legion + " (" + Aion2Servers.NameOf(l.ServerId) + ")");
            }

            pick.SelectedIndex = Math.Max(0, _shown is null ? 0 : _legions.FindIndex(l => l.Legion == _shown.Legion && l.ServerId == _shown.ServerId));
            pick.SelectionChanged += (_, _) =>
            {
                if (pick.SelectedIndex >= 0 && pick.SelectedIndex < _legions.Count)
                {
                    _shown = _legions[pick.SelectedIndex];
                    LoadInfo();
                    Draw();
                }
            };
            DockPanel.SetDock(pick, Dock.Right);
            dock.Children.Add(pick);
        }

        var words = new StackPanel();
        TextBlock title = Text(Loc["Legion.Title"], "Brush.Text", 26);
        title.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Title");
        words.Children.Add(title);
        TextBlock lede = Text(Loc["Legion.Lede"], "Brush.TextMuted", 13);
        lede.Margin = new Thickness(0, 4, 0, 0);
        words.Children.Add(lede);
        dock.Children.Add(words);
        return dock;
    }

    private static Border Panel(UIElement child, Thickness? padding = null)
    {
        var card = new Border { Child = child, Padding = padding ?? new Thickness(18, 16, 18, 16), Margin = new Thickness(0, 0, 0, 14), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return card;
    }

    private static UIElement Heading(string title, UIElement? right = null)
    {
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        if (right is not null)
        {
            DockPanel.SetDock(right, Dock.Right);
            head.Children.Add(right);
        }

        var name = new StackPanel { Orientation = Orientation.Horizontal };
        name.Children.Add(new Border { Width = 3, Height = 15, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Gold), Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center });
        name.Children.Add(Text(title, "Brush.Text", 15, bold: true));
        head.Children.Add(name);
        return head;
    }

    private static SolidColorBrush Tint(Color c, byte a) => new(Color.FromArgb(a, c.R, c.G, c.B));

    private static string? ClassOf(LegionMember m) => Aion2SkillNames.ClassFromCode(m.ClassCode);

    private static string Thousands(long v) => PlayerRow.Compact(v);

    // ---- the rail ----

    /// <summary>The legion: its name large on a gold wash with a shield faint behind, its server, how many, their averages, when the list came.</summary>
    private static UIElement LegionCard(LegionSnapshot legion)
    {
        var host = new Grid { ClipToBounds = true };
        var wash = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        wash.GradientStops.Add(new GradientStop(Color.FromArgb(0x50, Gold.R, Gold.G, Gold.B), 0));
        wash.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, Gold.R, Gold.G, Gold.B), 0.65));
        host.Children.Add(new Border { Background = wash });
        host.Children.Add(new TextBlock
        {
            Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 150, Foreground = Tint(Gold, 0x1A),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -20, -30, 0),
        });
        var stack = new StackPanel { Margin = new Thickness(20, 20, 20, 18) };
        stack.Children.Add(new TextBlock { Text = Loc["Legion.Kicker"].ToUpper(CultureInfo.CurrentCulture), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Gold) });
        TextBlock name = Text(legion.Legion, "Brush.Text", 28);
        name.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Title");
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.Margin = new Thickness(0, 4, 0, 0);
        stack.Children.Add(name);
        TextBlock where = Text(Aion2Servers.NameOf(legion.ServerId) + "  ·  " + string.Format(CultureInfo.CurrentCulture, Loc["Legion.MembersCount"], legion.Members.Count), "Brush.TextMuted", 13);
        where.Margin = new Thickness(0, 2, 0, 14);
        stack.Children.Add(where);
        var stats = new Grid();
        stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        UIElement Stat(string label, string value, int column)
        {
            var box = new StackPanel();
            box.Children.Add(Text(label, "Brush.TextSubtle", 11));
            box.Children.Add(Text(value, "Brush.Text", 18, bold: true));
            Grid.SetColumn(box, column);
            return box;
        }

        var withIl = legion.Members.Where(m => m.ItemLevel > 0).ToList();
        var withCp = legion.Members.Where(m => m.CombatPower > 0).ToList();
        stats.Children.Add(Stat(Loc["Legion.AvgItemLevel"], withIl.Count > 0 ? Math.Round(withIl.Average(m => m.ItemLevel)).ToString("N0", CultureInfo.CurrentCulture) : "–", 0));
        stats.Children.Add(Stat(Loc["Legion.AvgCp"], withCp.Count > 0 ? Thousands((long)withCp.Average(m => (double)m.CombatPower)) : "–", 1));
        stack.Children.Add(stats);
        TextBlock when = Text(string.Format(CultureInfo.CurrentCulture, Loc["Legion.ListFrom"], legion.At.ToLocalTime().ToString("ddd d MMM, HH:mm", CultureInfo.CurrentCulture), legion.Character), "Brush.TextSubtle", 11.5);
        when.Margin = new Thickness(0, 14, 0, 0);
        when.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(when);
        host.Children.Add(stack);
        var card = new Border { Child = host, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 14), BorderBrush = Tint(Gold, 0x55) };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        host.SizeChanged += (_, e) => host.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 11, 11);
        return card;
    }

    /// <summary>The classes: one bar in the class colours, then each class with its icon and how many.</summary>
    private static UIElement Classes(LegionSnapshot legion)
    {
        var stack = new StackPanel();
        stack.Children.Add(Heading(Loc["Legion.Classes"]));
        var counts = legion.Members.GroupBy(m => ClassOf(m) ?? "?").Select(g => (Class: g.Key, Count: g.Count())).OrderByDescending(x => x.Count).ThenBy(x => x.Class).ToList();
        var bar = new Grid { Height = 12, Margin = new Thickness(0, 0, 0, 12) };
        for (int i = 0; i < counts.Count; i++)
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(counts[i].Count, GridUnitType.Star) });
            var piece = new Border
            {
                Background = new SolidColorBrush(ClassBrushes.ClassColor(counts[i].Class)), Margin = new Thickness(i == 0 ? 0 : 1.5, 0, 0, 0),
                CornerRadius = new CornerRadius(i == 0 ? 6 : 0, i == counts.Count - 1 ? 6 : 0, i == counts.Count - 1 ? 6 : 0, i == 0 ? 6 : 0), ToolTip = counts[i].Class + ": " + counts[i].Count,
            };
            Grid.SetColumn(piece, i);
            bar.Children.Add(piece);
        }

        stack.Children.Add(bar);
        var grid = new UniformGrid { Columns = 2 };
        foreach (var (cls, count) in counts)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 7) };
            if (MainWindow.ClassIcon(cls) is { } icon)
            {
                line.Children.Add(new Image { Source = icon, Width = 18, Height = 18, Margin = new Thickness(0, 0, 7, 0) });
            }

            line.Children.Add(new TextBlock { Text = cls, FontSize = 12.5, Foreground = new SolidColorBrush(ClassBrushes.ClassColor(cls)), VerticalAlignment = VerticalAlignment.Center });
            line.Children.Add(Text("  " + count, "Brush.Text", 12.5, bold: true));
            grid.Children.Add(line);
        }

        stack.Children.Add(grid);
        return Panel(stack);
    }

    /// <summary>The legion on Discord: the servers following it with the bot, how many are linked there; else how to get the bot and follow the legion.</summary>
    private UIElement Discord(LegionSnapshot legion)
    {
        var stack = new StackPanel();
        stack.Children.Add(Heading(Loc["Legion.Discord"]));
        LegionInfo? info = _info;
        if (info is null)
        {
            stack.Children.Add(Wrapped(Loc[_infoLoading ? "Legion.Discord.Asking" : PrivacyConsent.Restricted ? "Legion.Discord.Off" : "Legion.Discord.Offline"], "Brush.TextMuted", 12.5));
        }
        else if (info.Servers.Count == 0)
        {
            stack.Children.Add(Wrapped(Loc["Legion.Discord.NoServer"], "Brush.TextMuted", 12.5));
        }
        else
        {
            foreach (LegionDiscord server in info.Servers)
            {
                var line = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
                if (server.Invite is { } invite)
                {
                    var join = new Button { Content = Loc["Legion.Discord.Join"], Padding = new Thickness(10, 3, 10, 3), VerticalAlignment = VerticalAlignment.Center };
                    join.Click += (_, _) => Open(invite);
                    DockPanel.SetDock(join, Dock.Right);
                    line.Children.Add(join);
                }

                var words = new StackPanel();
                var top = new StackPanel { Orientation = Orientation.Horizontal };
                top.Children.Add(new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 13, Foreground = new SolidColorBrush(Blurple), Margin = new Thickness(0, 1, 7, 0), VerticalAlignment = VerticalAlignment.Center });
                top.Children.Add(Text(server.Name ?? Loc["Legion.Discord.Private"], "Brush.Text", 13, bold: true));
                if (server.Recruiting)
                {
                    top.Children.Add(new Border
                    {
                        Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(6, 1, 6, 2), CornerRadius = new CornerRadius(6), Background = Tint(Up, 0x26), VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock { Text = Loc["Legion.Discord.Recruiting"], FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Up) },
                    });
                }

                words.Children.Add(top);
                string more = string.Join("  ·  ", new[]
                {
                    string.Format(CultureInfo.CurrentCulture, Loc["Legion.Discord.Linked"], server.Linked, legion.Members.Count), server.Language, server.Hours,
                }.Where(x => x is { Length: > 0 }));
                TextBlock detail = Text(more, "Brush.TextMuted", 11.5);
                detail.Margin = new Thickness(20, 2, 0, 0);
                detail.TextWrapping = TextWrapping.Wrap;
                words.Children.Add(detail);
                line.Children.Add(words);
                stack.Children.Add(line);
            }
        }

        // what to do: add the bot to a server, then follow the legion there; the member list to paste
        string command = "/guild legion legion:" + legion.Legion;
        TextBlock how = Wrapped(Loc[info is { Servers.Count: > 0 } ? "Legion.Discord.Another" : "Legion.Discord.How"], "Brush.TextSubtle", 11.5);
        how.Margin = new Thickness(0, 4, 0, 8);
        stack.Children.Add(how);
        var add = new Button { Content = Loc["Legion.Discord.Add"], Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 0, 6) };
        add.Click += (_, _) => Open(info?.AddUrl ?? LegionInfoClient.AddUrl);
        stack.Children.Add(add);
        var copy = new Button { Content = string.Format(CultureInfo.CurrentCulture, Loc["Legion.Discord.CopyCommand"], command), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 0, 6), ToolTip = command };
        TextBlock done = Text("", "Brush.Heal", 11.5, bold: true);
        copy.Click += (_, _) => done.Text = Copy(command) ? Loc["Legion.Copied"] : "";
        stack.Children.Add(copy);
        var list = new Button { Content = Loc["Legion.CopyList"], Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 0, 4) };
        list.Click += (_, _) => done.Text = Copy(MemberText(legion)) ? Loc["Legion.Copied"] : "";
        stack.Children.Add(list);
        stack.Children.Add(done);

        // the list to the bot: with it, the servers following the legion have every member on their roster (Upload/LegionShare.cs)
        var rule = new Border { Height = 1, Margin = new Thickness(0, 12, 0, 12) };
        rule.SetResourceReference(Border.BackgroundProperty, "Brush.Border");
        stack.Children.Add(rule);
        stack.Children.Add(Text(Loc["Legion.Send"], "Brush.Text", 13, bold: true));
        if (PrivacyConsent.Restricted)
        {
            TextBlock off = Wrapped(Loc["Legion.Discord.Off"], "Brush.TextMuted", 12);
            off.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(off);
        }
        else if (!Account.SignedIn)
        {
            TextBlock why = Wrapped(Loc["Legion.Send.SignIn"], "Brush.TextMuted", 12);
            why.Margin = new Thickness(0, 4, 0, 8);
            stack.Children.Add(why);
            if (_openAccount is { } openAccount)
            {
                var signIn = new Button { Content = Loc["Legion.Send.SignInButton"], Padding = new Thickness(12, 6, 12, 6), HorizontalAlignment = HorizontalAlignment.Left };
                signIn.Click += (_, _) => openAccount();
                stack.Children.Add(signIn);
            }
        }
        else
        {
            var share = new CheckBox { IsChecked = MeterSettings.Load().ShareLegionList, Margin = new Thickness(0, 6, 0, 6), Content = Wrapped(Loc["Legion.Share"], "Brush.Text", 12) };
            share.Click += (_, _) =>
            {
                MeterSettings settings = MeterSettings.Load();
                settings.ShareLegionList = share.IsChecked == true;
                settings.Save();
            };
            stack.Children.Add(share);
            stack.Children.Add(Wrapped(AccountWindow.LegionText(LegionShare.Last), "Brush.TextSubtle", 11.5));
        }

        return Panel(stack);
    }

    private static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // no browser: nothing to do
        }
    }

    private static bool Copy(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return false;
        }
    }

    /// <summary>The members as lines to paste (Discord, a sheet): name, class, level, Item Level, Combat Power; the strongest first.</summary>
    private static string MemberText(LegionSnapshot legion)
    {
        var lines = new List<string> { legion.Legion + " · " + Aion2Servers.NameOf(legion.ServerId) + " · " + legion.Members.Count };
        foreach (LegionMember m in legion.Members.OrderByDescending(m => m.CombatPower))
        {
            lines.Add(string.Join("\t", m.Name, ClassOf(m) ?? "", m.Level.ToString(CultureInfo.InvariantCulture),
                m.ItemLevel > 0 ? m.ItemLevel.ToString(CultureInfo.InvariantCulture) : "", m.CombatPower > 0 ? m.CombatPower.ToString(CultureInfo.InvariantCulture) : ""));
        }

        return string.Join("\n", lines);
    }

    // ---- the scoreboard ----

    private static DateTime? LastActive(LegionMember m) => m.LastActiveMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(m.LastActiveMs).UtcDateTime : null;

    /// <summary>In the game when the list came: the list gives an online member the subzone he is in, an offline one 0 (docs/protocol, 0x058a).</summary>
    private static bool Online(LegionMember m) => m.Zone != 0;

    /// <summary>Played within the span before the list came: online then, or last seen within it.</summary>
    private static bool ActiveWithin(LegionMember m, DateTime listAt, TimeSpan span) => Online(m) || (LastActive(m) is DateTime at && listAt - at < span);

    private UIElement Scoreboard(LegionSnapshot legion)
    {
        int count = legion.Members.Count;
        string Share(int n) => string.Format(CultureInfo.CurrentCulture, Loc["Legion.Score.Share"], count == 0 ? 0 : Math.Round(100.0 * n / count));
        int online = legion.Members.Count(Online);
        int today = legion.Members.Count(m => ActiveWithin(m, legion.At, TimeSpan.FromHours(24)));
        int week = legion.Members.Count(m => ActiveWithin(m, legion.At, TimeSpan.FromDays(7)));
        LegionMember? top = legion.Members.Where(m => m.CombatPower > 0).MaxBy(m => m.CombatPower);
        int linked = _info?.Linked?.Count ?? _info?.Servers.Select(s => s.Linked).DefaultIfEmpty(0).Max() ?? 0;
        var cells = new (string Label, string Value, string Note, Color Colour)[]
        {
            (Loc["Legion.Score.Members"], count.ToString(CultureInfo.CurrentCulture), Loc["Legion.Score.MembersNote"], Gold),
            (Loc["Legion.Score.Online"], online.ToString(CultureInfo.CurrentCulture), Loc["Legion.Score.OnlineNote"], Up),
            (Loc["Legion.Score.Today"], today.ToString(CultureInfo.CurrentCulture), Share(today), Color.FromRgb(0x6F, 0xB4, 0xEC)),
            (Loc["Legion.Score.Week"], week.ToString(CultureInfo.CurrentCulture), Share(week), Dungeon),
            (Loc["Legion.Score.Top"], top is null ? "–" : Thousands(top.CombatPower), top?.Name ?? "", Boss),
            (Loc["Legion.Score.Linked"], _info is null ? "–" : linked.ToString(CultureInfo.CurrentCulture), _info is null ? Loc["Legion.Score.LinkedUnknown"] : Loc["Legion.Score.LinkedNote"], Blurple),
        };
        var grid = new Grid();
        for (int c = 0; c < 3; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < cells.Length; i++)
        {
            var (label, value, note, colour) = cells[i];
            int column = i % 3, row = i / 3;
            var stack = new StackPanel();
            var top2 = new StackPanel { Orientation = Orientation.Horizontal };
            top2.Children.Add(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(colour), Margin = new Thickness(0, 1, 7, 0), VerticalAlignment = VerticalAlignment.Center });
            top2.Children.Add(Text(label.ToUpper(CultureInfo.CurrentCulture), "Brush.TextMuted", 10.5, bold: true));
            stack.Children.Add(top2);
            TextBlock big = Text(value, "Brush.Text", 24, bold: true);
            big.Margin = new Thickness(0, 6, 0, 0);
            stack.Children.Add(big);
            TextBlock small = Text(note, "Brush.TextSubtle", 11);
            small.TextTrimming = TextTrimming.CharacterEllipsis;
            stack.Children.Add(small);
            var cell = new Border { Child = stack, Padding = new Thickness(18, 14, 18, 14), BorderThickness = new Thickness(0, 0, column < 2 ? 1 : 0, row == 0 ? 1 : 0) };
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

    // ---- the members ----

    private UIElement Members(LegionSnapshot legion)
    {
        var stack = new StackPanel();
        // the search box in the theme's colours, its hint inside while empty
        var search = new TextBox { Width = 190, Text = _search, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 4, 8, 4), BorderThickness = new Thickness(1) };
        search.SetResourceReference(Control.BackgroundProperty, "Brush.Control");
        search.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        search.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");
        search.SetResourceReference(TextBox.CaretBrushProperty, "Brush.Text");
        TextBlock hint = Text(Loc["Legion.Search"], "Brush.TextSubtle", 12);
        hint.IsHitTestVisible = false;
        hint.Margin = new Thickness(11, 0, 0, 0);
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.Visibility = _search.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        search.TextChanged += (_, _) =>
        {
            _search = search.Text.Trim();
            hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            FillList(legion);
        };
        var box = new Grid { VerticalAlignment = VerticalAlignment.Center };
        box.Children.Add(search);
        box.Children.Add(hint);
        stack.Children.Add(Heading(string.Format(CultureInfo.CurrentCulture, Loc["Legion.MembersTitle"], legion.Members.Count), box));

        // the order: chips under the title
        var tools = new WrapPanel { Margin = new Thickness(0, -2, 0, 12) };
        TextBlock by = Text(Loc["Legion.SortBy"], "Brush.TextSubtle", 11.5);
        by.VerticalAlignment = VerticalAlignment.Center;
        by.Margin = new Thickness(0, 0, 8, 0);
        tools.Children.Add(by);
        foreach ((Sort sort, string key) in new[] { (Sort.Cp, "Cp"), (Sort.ItemLevel, "ItemLevel"), (Sort.Active, "Active"), (Sort.Rank, "Rank"), (Sort.Class, "Class"), (Sort.Name, "Name") })
        {
            bool on = _sort == sort;
            var chip = new Border
            {
                Padding = new Thickness(10, 4, 10, 5), Margin = new Thickness(0, 0, 4, 0), CornerRadius = new CornerRadius(7), Cursor = System.Windows.Input.Cursors.Hand,
                Background = on ? Tint(Gold, 0x24) : Brushes.Transparent, BorderThickness = new Thickness(1), BorderBrush = on ? Tint(Gold, 0x90) : Brushes.Transparent,
                Child = Text(Loc["Legion.Sort." + key], on ? "Brush.Text" : "Brush.TextMuted", 12, bold: on),
            };
            chip.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                _sort = sort;
                Draw();
            };
            tools.Children.Add(chip);
        }

        stack.Children.Add(tools);

        // the titles over the columns
        var heads = Line();
        void Title(int column, string key, HorizontalAlignment align = HorizontalAlignment.Left)
        {
            TextBlock t = Text(Loc[key].ToUpper(CultureInfo.CurrentCulture), "Brush.TextSubtle", 10.5, bold: true);
            t.HorizontalAlignment = align;
            Grid.SetColumn(t, column);
            heads.Children.Add(t);
        }

        Title(2, "Legion.Col.Name");
        Title(3, "Legion.Col.Rank");
        Title(4, "Legion.Col.ItemLevel", HorizontalAlignment.Right);
        Title(5, "Legion.Col.Cp");
        ((FrameworkElement)heads.Children[heads.Children.Count - 1]).Margin = new Thickness(16, 0, 0, 0);
        Title(6, "Legion.Col.Active", HorizontalAlignment.Right);
        heads.Margin = new Thickness(8, 0, 8, 6);
        stack.Children.Add(heads);
        _list = new StackPanel();
        stack.Children.Add(_list);
        FillList(legion);
        return Panel(stack);
    }

    private static Grid Line()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        return grid;
    }

    private void FillList(LegionSnapshot legion)
    {
        if (_list is null)
        {
            return;
        }

        _list.Children.Clear();
        int topCp = Math.Max(1, legion.Members.Select(m => m.CombatPower).DefaultIfEmpty(0).Max());
        int topLevel = legion.Members.Select(m => m.Level).DefaultIfEmpty(0).Max();
        var linked = new HashSet<string>(_info?.Linked ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        IEnumerable<LegionMember> members = legion.Members.Where(m => _search.Length == 0 || m.Name.Contains(_search, StringComparison.OrdinalIgnoreCase)
            || (ClassOf(m)?.Contains(_search, StringComparison.OrdinalIgnoreCase) ?? false));
        members = _sort switch
        {
            Sort.ItemLevel => members.OrderByDescending(m => m.ItemLevel).ThenByDescending(m => m.CombatPower),
            Sort.Name => members.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase),
            Sort.Active => members.OrderByDescending(Online).ThenByDescending(m => m.LastActiveMs),
            Sort.Class => members.OrderBy(m => ClassOf(m) ?? "~").ThenByDescending(m => m.CombatPower),
            Sort.Rank => members.OrderBy(m => m.Rank).ThenByDescending(m => m.CombatPower),
            _ => members.OrderByDescending(m => m.CombatPower).ThenByDescending(m => m.ItemLevel),
        };
        int n = 0;
        foreach (LegionMember m in members)
        {
            string? cls = ClassOf(m);
            Color colour = cls is null ? Color.FromRgb(0x9F, 0xB3, 0xC8) : ClassBrushes.ClassColor(cls);
            Grid line = Line();
            void Put(UIElement e, int column)
            {
                Grid.SetColumn(e, column);
                line.Children.Add(e);
            }

            n++;
            Put(Text(n.ToString(CultureInfo.CurrentCulture), "Brush.TextSubtle", 11.5, bold: true), 0);
            if (cls is not null && MainWindow.ClassIcon(cls) is { } icon)
            {
                Put(new Image { Source = icon, Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, ToolTip = cls }, 1);
            }

            var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            who.Children.Add(new TextBlock { Text = m.Name, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = ClassBrushes.Text(cls), TextTrimming = TextTrimming.CharacterEllipsis });
            TextBlock what = Text((cls ?? "") + (m.Level > 0 && m.Level < topLevel ? "  ·  " + string.Format(CultureInfo.CurrentCulture, Loc["Legion.LevelShort"], m.Level) : ""), "Brush.TextSubtle", 11);
            what.ToolTip = m.Level > 0 ? string.Format(CultureInfo.CurrentCulture, Loc["Legion.LevelShort"], m.Level) : null;
            what.TextTrimming = TextTrimming.CharacterEllipsis;
            who.Children.Add(what);
            Put(who, 2);
            if (RankBadge(m.Rank) is { } badge)
            {
                Put(badge, 3);
            }

            TextBlock il = Text(m.ItemLevel > 0 ? m.ItemLevel.ToString("N0", CultureInfo.CurrentCulture) : "–", "Brush.Text", 13, bold: true);
            il.HorizontalAlignment = HorizontalAlignment.Right;
            Put(il, 4);

            // Combat Power with a bar against the strongest
            var cp = new Grid { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            cp.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            cp.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cp.Children.Add(Text(m.CombatPower > 0 ? Thousands(m.CombatPower) : "–", "Brush.Text", 13, bold: true));
            var track = new Grid { Height = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            var under = new Border { CornerRadius = new CornerRadius(3) };
            under.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
            track.Children.Add(under);
            var fill = new Border { CornerRadius = new CornerRadius(3), Background = Tint(colour, 0xC0), HorizontalAlignment = HorizontalAlignment.Left };
            double share = Math.Clamp(m.CombatPower / (double)topCp, 0, 1);
            track.SizeChanged += (_, e) => fill.Width = Math.Max(m.CombatPower > 0 ? 4 : 0, e.NewSize.Width * share);
            track.Children.Add(fill);
            Grid.SetColumn(track, 1);
            cp.Children.Add(track);
            Put(cp, 5);

            // online when the list came (a dot), else when last seen before it
            UIElement active;
            if (Online(m))
            {
                var on = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, ToolTip = Loc["Legion.OnlineTip"] };
                on.Children.Add(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(Up), Margin = new Thickness(0, 1, 6, 0), VerticalAlignment = VerticalAlignment.Center });
                TextBlock word = Text(Loc["Legion.Online"], "Brush.Text", 12, bold: true);
                word.Foreground = new SolidColorBrush(Up);
                on.Children.Add(word);
                active = on;
            }
            else
            {
                TextBlock ago = Text(Ago(LastActive(m), MainWindow.Headless ? legion.At : DateTime.UtcNow), "Brush.TextMuted", 12);
                ago.HorizontalAlignment = HorizontalAlignment.Right;
                ago.VerticalAlignment = VerticalAlignment.Center;
                ago.ToolTip = LastActive(m)?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
                active = ago;
            }

            Put(active, 6);
            if (linked.Contains(m.Name))
            {
                Put(new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 13, Foreground = new SolidColorBrush(Blurple), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, ToolTip = Loc["Legion.LinkedTip"] }, 7);
            }

            var row = new Border { Child = line, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 0, 2), CornerRadius = new CornerRadius(8), Background = n % 2 == 0 ? Tint(Colors.White, 0x05) : Brushes.Transparent };
            Brush rest = row.Background;
            row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
            row.MouseLeave += (_, _) => row.Background = rest;
            _list.Children.Add(row);
        }

        if (n == 0)
        {
            _list.Children.Add(Text(Loc["Legion.NoMatch"], "Brush.TextMuted", 12.5));
        }
    }

    /// <summary>The rank: 0 the legion's leader (a gold crown), 1 an officer (docs/protocol, 0x058a: 0, 1 and 2 seen; 2 the newest members, shown plain).</summary>
    private static UIElement? RankBadge(int rank)
    {
        if (rank is not (0 or 1))
        {
            return null;
        }

        Color c = rank == 0 ? Gold : Color.FromRgb(0x9F, 0xB8, 0xD8);
        var chip = new Border { Padding = new Thickness(7, 1, 8, 2), CornerRadius = new CornerRadius(6), Background = Tint(c, 0x22), BorderBrush = Tint(c, 0x70), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        if (rank == 0)
        {
            line.Children.Add(new TextBlock { Text = "\u265B", FontSize = 11, Foreground = new SolidColorBrush(c), Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
        }

        line.Children.Add(new TextBlock { Text = Loc[rank == 0 ? "Legion.Leader" : "Legion.Officer"], FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(c), VerticalAlignment = VerticalAlignment.Center });
        chip.Child = line;
        return chip;
    }

    private static string Ago(DateTime? at, DateTime now)
    {
        if (at is not DateTime when)
        {
            return "–";
        }

        TimeSpan t = now - when;
        string F(string key, double v) => string.Format(CultureInfo.CurrentCulture, Loc[key], Math.Max(1, (int)v));
        return t < TimeSpan.FromHours(1) ? F("Legion.MinutesAgo", t.TotalMinutes)
            : t < TimeSpan.FromDays(1) ? F("Legion.HoursAgo", t.TotalHours) : F("Legion.DaysAgo", t.TotalDays);
    }

    // ---- the pictures ----

    private static LegionSnapshot SampleLegion()
    {
        string[] names = { "Aerin", "Kaelo", "Mirae", "Thorne", "Lunari", "Vesh", "Selwyn", "Orrin", "Kairos", "Novalis", "Tamsin", "Brannoc", "Iselle", "Corvin", "Ysolde", "Halric",
            "Maelis", "Dorran", "Sefa", "Quill", "Rhoswen", "Taveth", "Elowen", "Gareth" };
        int[] classes = { 1, 4, 5, 2, 8, 7, 5, 2, 3, 7, 6, 1, 4, 8, 6, 3, 5, 1, 7, 4, 2, 8, 6, 3 };
        DateTime at = new(2026, 11, 6, 19, 30, 0, DateTimeKind.Utc);
        long now = new DateTimeOffset(at).ToUnixTimeMilliseconds();
        var members = names.Select((name, i) => new LegionMember(1000 + i, 2302, classes[i] * 4 + 1, name, 45, i == 0 ? 0 : i < 4 ? 1 : 2,
            now - (long)(i < 8 ? 40 * 60_000 + i * 7 * 60_000 : i < 15 ? (i - 6) * 3_600_000 : (i - 13) * 86_400_000), i is 0 or 2 or 3 or 5 or 9 or 12 ? 1010 + i : 0,
            3120 - i * 31 - (i % 3) * 12, 196_000 - i * 3_900 - (i % 4) * 1_700)).ToList();
        return new LegionSnapshot { Legion = "Dawnbreakers", ServerId = 2302, Character = "Aerin", At = at, Members = members };
    }
}
