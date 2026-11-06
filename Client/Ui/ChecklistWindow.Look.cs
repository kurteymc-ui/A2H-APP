using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AionDPS.Aion2;
using AionDPS.Data;

namespace AionDPS.Ui;

/// <summary>
/// The Characters page in the look of Journey and Legion (6 November 2026, the owner: "design the other pages better now that we have an idea of what we
/// want"): a title over the page, the characters as cards in their class colours, the roster's totals as a board of figures and every character as a card
/// of its counters (no table to scroll sideways), each character's page under a banner in its class colour, the sections under a gold stroke.
/// </summary>
public sealed partial class ChecklistWindow
{
    /// <summary>The page's title and what it is, over the characters and the page.</summary>
    private static UIElement PageHead()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var head = new StackPanel { Margin = new Thickness(4, 2, 4, 14) };
        TextBlock title = Label(loc["Checklist.PageTitle"], "Brush.Text", 26);
        title.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Title");
        head.Children.Add(title);
        TextBlock lede = Label(loc["Checklist.PageLede"], "Brush.TextMuted", 13);
        lede.TextWrapping = TextWrapping.Wrap;
        lede.Margin = new Thickness(0, 4, 0, 0);
        head.Children.Add(lede);
        return head;
    }

    /// <summary>A section's title: a short gold stroke, then the text.</summary>
    private static FrameworkElement SectionTitle(string text, double size = 15, string brush = "Brush.Text")
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var stroke = new Border { Width = 3, Height = size, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
        stroke.SetResourceReference(Border.BackgroundProperty, "Brush.Accent");
        line.Children.Add(stroke);
        line.Children.Add(Label(text, brush, size, bold: true));
        return line;
    }

    /// <summary>A small round "i" whose tool tip holds what used to be a paragraph on the page.</summary>
    private static UIElement InfoMark(string tip)
    {
        var mark = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent, Cursor = Cursors.Help, ToolTip = new TextBlock { Text = tip, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 } };
        mark.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        TextBlock i = Label("i", "Brush.TextMuted", 10.5, bold: true);
        i.HorizontalAlignment = HorizontalAlignment.Center;
        mark.Child = i;
        ToolTipService.SetInitialShowDelay(mark, 100);
        ToolTipService.SetShowDuration(mark, 60_000);
        return mark;
    }

    private static Color Resource(string key, Color fallback) => Application.Current?.TryFindResource(key) is SolidColorBrush b ? b.Color : fallback;

    private static SolidColorBrush Tint(Color c, byte alpha) => new(Color.FromArgb(alpha, c.R, c.G, c.B));

    /// <summary>One figure of a board: a coloured dot and a caption in capitals, the figure large, a note small under it.</summary>
    private sealed record Score(string Caption, UIElement Value, UIElement? Note, string Dot, string? Tip = null);

    /// <summary>A board of figures in three columns (the roster's resets, energy, claims, whom to play next, the daily tasks), lines between the cells.</summary>
    private static UIElement Scoreboard(IReadOnlyList<Score> cells)
    {
        var grid = new Grid();
        const int columns = 3;
        int rows = (cells.Count + columns - 1) / columns;
        for (int c = 0; c < columns; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (int r = 0; r < rows; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (int i = 0; i < cells.Count; i++)
        {
            Score cell = cells[i];
            int column = i % columns, row = i / columns;
            var stack = new StackPanel();
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, cell.Dot);
            top.Children.Add(dot);
            TextBlock caption = Label(cell.Caption.ToUpper(CultureInfo.CurrentCulture), "Brush.TextMuted", 10.5, bold: true);
            caption.TextTrimming = TextTrimming.CharacterEllipsis;
            top.Children.Add(caption);
            stack.Children.Add(top);
            if (cell.Value is FrameworkElement value)
            {
                value.Margin = new Thickness(0, 6, 0, 0);
            }

            stack.Children.Add(cell.Value);
            if (cell.Note is not null)
            {
                stack.Children.Add(cell.Note);
            }

            var border = new Border
            {
                Child = stack, Padding = new Thickness(18, 14, 18, 14), Background = Brushes.Transparent, ToolTip = cell.Tip,
                BorderThickness = new Thickness(0, 0, column < columns - 1 ? 1 : 0, row < rows - 1 ? 1 : 0),
            };
            border.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            Grid.SetColumn(border, column);
            Grid.SetRow(border, row);
            grid.Children.Add(border);
        }

        var panel = new Border { Child = grid, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 12) };
        panel.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        panel.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return panel;
    }

    /// <summary>A figure of the board: large, in a brush of the theme.</summary>
    private static TextBlock Big(string text, string brush = "Brush.Text")
    {
        TextBlock big = Label(text, brush, 24, bold: true);
        big.TextTrimming = TextTrimming.CharacterEllipsis;
        return big;
    }

    /// <summary>The small line under a figure.</summary>
    private static TextBlock Small(string text, string brush = "Brush.TextSubtle")
    {
        TextBlock small = Label(text, brush, 11.5);
        small.TextTrimming = TextTrimming.CharacterEllipsis;
        small.Margin = new Thickness(0, 1, 0, 0);
        return small;
    }

    /// <summary>Cards side by side, as many columns as the width takes (one under 620, two under 1100, else three).</summary>
    private static UIElement Tiles(IReadOnlyList<UIElement> tiles)
    {
        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(-6, 0, -6, 6) };
        foreach (UIElement tile in tiles)
        {
            grid.Children.Add(tile);
        }

        grid.SizeChanged += (_, e) =>
        {
            int columns = e.NewSize.Width < 620 ? 1 : e.NewSize.Width < 1100 ? 2 : 3;
            if (grid.Columns != columns)
            {
                grid.Columns = columns;
            }
        };
        return grid;
    }

    /// <summary>
    /// One character of the roster as a card: its emblem and name in the class colour (a dot when online), level, server and when the meter saw it; its Odyle
    /// Energy with a bar, when it is full and what it holds besides; then its daily and weekly counters as small figures (a triangle where one is lost at the next
    /// refill). A click opens the character.
    /// </summary>
    private UIElement RosterTile(TicketCharacter c, Aion2Tickets.OdyleState? o, long inItems, TicketDef?[] defs, bool online, DateTime now, DateTime utcNow, string language)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        string? className = c.ClassCode > 0 ? Aion2.Protocol.Aion2SkillNames.ClassFromCode(c.ClassCode) : null;
        Color accent = className is not null ? ClassBrushes.ClassColor(className) : Resource("Brush.Accent", Color.FromRgb(0xE0, 0xB6, 0x4A));
        bool premium = Aion2Tickets.HasPremium(c, utcNow) != false;
        DateTime seenAt = c.SavedAt.ToLocalTime();
        var stack = new StackPanel();

        // who
        var who = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        if (className is not null && new ClassIconConverter().Convert(className, typeof(ImageSource), null, CultureInfo.CurrentCulture) is ImageSource icon)
        {
            var image = new Image { Source = icon, Width = 30, Height = 30, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(image, Dock.Left);
            who.Children.Add(image);
        }

        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var nameLine = new StackPanel { Orientation = Orientation.Horizontal };
        TextBlock name = Label(c.Name, "Brush.Text", 15, bold: true);
        if (className is not null)
        {
            name.Foreground = ClassBrushes.Text(className);
        }

        nameLine.Children.Add(name);
        if (online)
        {
            var on = new Border { Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(6, 0, 7, 1), CornerRadius = new CornerRadius(6), VerticalAlignment = VerticalAlignment.Center };
            on.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
            on.Child = Label("● " + loc["Checklist.OnlineNow"], "Brush.Heal", 10.5, bold: true);
            nameLine.Children.Add(on);
        }

        names.Children.Add(nameLine);
        var parts = new List<string>();
        if (c.Level > 0)
        {
            parts.Add(string.Format(CultureInfo.CurrentCulture, loc["Checklist.Level"], c.Level));
        }

        if (c.ServerId > 0)
        {
            parts.Add(Aion2.Protocol.Aion2Servers.NameOf(c.ServerId).Replace("Europe - ", "", StringComparison.Ordinal));
        }

        if (!online)
        {
            parts.Add(string.Format(CultureInfo.CurrentCulture, loc["Checklist.RosterSeen"], AgoText(now - seenAt)));
        }

        TextBlock sub = Label(string.Join("  ·  ", parts), "Brush.TextSubtle", 11);
        sub.TextTrimming = TextTrimming.CharacterEllipsis;
        names.Children.Add(sub);
        who.Children.Add(names);
        stack.Children.Add(who);

        // the Odyle Energy
        var energy = new DockPanel();
        if (o is not null)
        {
            var figure = new StackPanel { Orientation = Orientation.Horizontal };
            figure.Children.Add(Label((o.Estimated ? "~" : "") + o.Count.ToString("N0", CultureInfo.CurrentCulture), o.Full ? "Brush.Warning" : "Brush.Accent", 14, bold: true));
            figure.Children.Add(Label(" / " + o.Cap.ToString("N0", CultureInfo.CurrentCulture), "Brush.TextSubtle", 11.5));
            DockPanel.SetDock(figure, Dock.Right);
            energy.Children.Add(figure);
        }

        energy.Children.Add(Label(loc["Checklist.RosterOdyleHead"], "Brush.TextMuted", 11.5));
        stack.Children.Add(energy);
        if (o is not null)
        {
            var track = new Grid { Height = 6, Margin = new Thickness(0, 5, 0, 5) };
            var under = new Border { CornerRadius = new CornerRadius(3) };
            under.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
            track.Children.Add(under);
            var fill = new Border { CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
            fill.SetResourceReference(Border.BackgroundProperty, o.Full ? "Brush.Warning" : "Brush.Accent");
            double share = o.Cap <= 0 ? 0 : Math.Clamp(o.Count / (double)o.Cap, 0, 1);
            track.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * share;
            track.Children.Add(fill);
            track.ToolTip = string.Format(CultureInfo.CurrentCulture, loc["Checklist.RosterOdyleLine"], o.Count, o.Cap, o.Bonus, o.PerTick);
            stack.Children.Add(track);

            var after = new DockPanel();
            var extra = new List<string>();
            if (o.Bonus > 0)
            {
                extra.Add("+" + o.Bonus.ToString("N0", CultureInfo.CurrentCulture) + " " + loc["Checklist.TileCharged"]);
            }

            if (inItems > 0)
            {
                extra.Add(string.Format(CultureInfo.CurrentCulture, loc["Checklist.RosterOdyleItems"], inItems.ToString("N0", CultureInfo.CurrentCulture)));
            }

            if (extra.Count > 0)
            {
                TextBlock more = Label(string.Join("  ·  ", extra), "Brush.TextSubtle", 11);
                DockPanel.SetDock(more, Dock.Right);
                after.Children.Add(more);
            }

            bool soon = !o.Full && o.FullAt is { } f && f - utcNow < TimeSpan.FromHours(6);
            string fullText = o.Full ? "▲ " + loc["Checklist.RosterFull"] : o.FullAt is { } at ? string.Format(CultureInfo.CurrentCulture, loc["Checklist.TileFullIn"], (o.NextTick is null ? "~" : "") + InText(at - utcNow)) : "–";
            TextBlock full = Label(fullText, o.Full || soon ? "Brush.Warning" : "Brush.TextMuted", 11.5, bold: o.Full);
            full.ToolTip = o.Full ? loc["Checklist.RosterFullWaste"]
                : o.FullAt is { } when ? string.Format(CultureInfo.CurrentCulture, loc["Checklist.RosterFullAt"], when.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
                  + (o.NextTick is { } tick ? "  ·  " + string.Format(CultureInfo.CurrentCulture, loc["Checklist.RosterNextTick"], o.PerTick, tick.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)) : "")
                : null;
            after.Children.Add(full);
            stack.Children.Add(after);
        }
        else
        {
            TextBlock unknown = Label(loc["Checklist.RosterUnknown"], "Brush.TextSubtle", 11.5);
            unknown.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(unknown);
        }

        // the counters: the daily tasks, then the roster's tickets, three a line
        var counters = new UniformGrid { Columns = 3, Margin = new Thickness(0, 12, 0, 0) };
        UIElement Counter(string caption, string value, string brush, string? tip, bool warn)
        {
            var box = new Border { Padding = new Thickness(9, 6, 9, 7), Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(8), ToolTip = tip };
            box.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
            var lines = new StackPanel();
            TextBlock top = Label(caption, "Brush.TextSubtle", 10.5);
            top.TextTrimming = TextTrimming.CharacterEllipsis;
            lines.Children.Add(top);
            var figure = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            if (warn)
            {
                var triangle = new System.Windows.Shapes.Polygon { Points = new PointCollection { new Point(5, 0), new Point(10, 9), new Point(0, 9) }, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
                triangle.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Brush.Warning");
                figure.Children.Add(triangle);
            }

            figure.Children.Add(Label(value, brush, 14, bold: brush != "Brush.TextSubtle"));
            lines.Children.Add(figure);
            box.Child = lines;
            return box;
        }

        bool stale = c.Tickets.TryGetValue(Aion2Tickets.MissionsId, out TicketValue missions) && Aion2Tickets.MissionsStale(missions, now);
        if (c.Tickets.ContainsKey(Aion2Tickets.MissionsId))
        {
            int done = stale ? 0 : missions.Count;
            counters.Children.Add(Counter(loc["Checklist.RosterTasks"], done >= Aion2Tickets.MissionsPerDay ? "✓" : $"{(stale ? "~" : "")}{done}/{Aion2Tickets.MissionsPerDay}",
                done < Aion2Tickets.MissionsPerDay ? "Brush.Accent" : "Brush.Heal", loc["Checklist.RosterTasksTip"], false));
        }
        else
        {
            counters.Children.Add(Counter(loc["Checklist.RosterTasks"], "–", "Brush.TextSubtle", loc["Checklist.RosterUnknown"], false));
        }

        foreach (TicketDef? def in defs)
        {
            if (def is null)
            {
                continue;
            }

            string caption = def.ShortIn(language).Replace("\n", " ", StringComparison.Ordinal);
            bool locked = Aion2Tickets.IsLocked(c, def.Id);
            if (locked || !c.Tickets.TryGetValue(def.Id, out TicketValue value))
            {
                counters.Children.Add(Counter(caption, "–", "Brush.TextSubtle", locked ? string.Format(CultureInfo.CurrentCulture, loc["Checklist.RosterLocked"], Aion2Tickets.UnlockLevel(def.Id)) : loc["Checklist.RosterUnknown"], false));
                continue;
            }

            int count = Aion2Tickets.Expected(def, value.Count, seenAt, now, premium);
            int cap = premium && def.SubscribeMax > def.Max ? def.SubscribeMax : def.Max;
            int lost = Aion2Tickets.LostAtNextRefill(def, count, premium);
            string text = (count != value.Count ? "~" : "") + count.ToString(CultureInfo.CurrentCulture) + (value.Bonus > 0 ? $"+{value.Bonus}" : "");
            string tip = $"{def.NameIn(language)}: {count} / {Math.Max(cap, count)}" + (lost > 0 ? "  ·  " + string.Format(CultureInfo.CurrentCulture, loc[lost == 1 ? "Checklist.WarnLostOne" : "Checklist.WarnLostMany"], lost) : "");
            counters.Children.Add(Counter(caption, text, lost > 0 ? "Brush.Warning" : count > 0 ? "Brush.Accent" : "Brush.TextSubtle", tip, lost > 0));
        }

        stack.Children.Add(counters);

        // the card: a wash of the class colour from the top, its emblem faint in the corner; lit under the mouse, a click opens the character
        var host = new Grid();
        var wash = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        wash.GradientStops.Add(new GradientStop(Color.FromArgb(0x2E, accent.R, accent.G, accent.B), 0));
        wash.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, accent.R, accent.G, accent.B), 0.55));
        host.Children.Add(new Border { Background = wash, CornerRadius = new CornerRadius(11), IsHitTestVisible = false });
        var inner = new Border { Child = stack, Padding = new Thickness(16, 14, 10, 8) };
        host.Children.Add(inner);
        var card = new Border { Child = host, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Margin = new Thickness(6, 0, 6, 12), Cursor = Cursors.Hand,
            BorderBrush = Tint(accent, 0x55) };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        card.MouseEnter += (_, _) => card.BorderBrush = Tint(accent, 0xB0);
        card.MouseLeave += (_, _) => card.BorderBrush = Tint(accent, 0x55);
        string picked = c.Name;
        card.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            ShowCharacter(picked);
        };
        return card;
    }

    /// <summary>
    /// The banner over a character's page: a wash of its class colour from the left with its emblem large and faint at the right, the name in the title face and
    /// class colour with its legion, class, level and server under it; on the right whether it is online or when the meter saw it last.
    /// </summary>
    private static UIElement Hero(TicketCharacter c)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        string language = loc.Language;
        string? className = c.ClassCode > 0 ? Aion2.Protocol.Aion2SkillNames.ClassFromCode(c.ClassCode) : null;
        Color accent = className is not null ? ClassBrushes.ClassColor(className) : Resource("Brush.Accent", Color.FromRgb(0xE0, 0xB6, 0x4A));
        bool online = Aion2Tickets.CurrentName == c.Name;
        ImageSource? icon = className is not null ? new ClassIconConverter().Convert(className, typeof(ImageSource), null, CultureInfo.CurrentCulture) as ImageSource : null;

        var host = new Grid { ClipToBounds = true };
        var wash = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        wash.GradientStops.Add(new GradientStop(Color.FromArgb(0x44, accent.R, accent.G, accent.B), 0));
        wash.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, accent.R, accent.G, accent.B), 0.7));
        host.Children.Add(new Border { Background = wash });
        if (icon is not null)
        {
            host.Children.Add(new Image { Source = icon, Width = 150, Height = 150, Opacity = 0.10, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -30, 150, -30), IsHitTestVisible = false });
        }

        var grid = new Grid { Margin = new Thickness(20, 16, 20, 16) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (icon is not null)
        {
            var ring = new Border { Width = 58, Height = 58, CornerRadius = new CornerRadius(29), BorderThickness = new Thickness(2), BorderBrush = Tint(accent, 0xC0), Background = Tint(accent, 0x1A),
                Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center, Child = new Image { Source = icon, Width = 36, Height = 36 } };
            grid.Children.Add(ring);
        }

        var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var nameLine = new StackPanel { Orientation = Orientation.Horizontal };
        TextBlock name = Label(c.Name, "Brush.Text", 28);
        name.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Title");
        if (className is not null)
        {
            name.Foreground = ClassBrushes.Text(className);
        }

        nameLine.Children.Add(name);
        if (!string.IsNullOrEmpty(c.Legion))
        {
            TextBlock legion = Label("<" + c.Legion + ">", "Brush.TextMuted", 13);
            legion.Margin = new Thickness(10, 0, 0, 4);
            legion.VerticalAlignment = VerticalAlignment.Bottom;
            nameLine.Children.Add(legion);
        }

        who.Children.Add(nameLine);
        var parts = new List<string>();
        if (className is not null)
        {
            parts.Add(ClassCatalog.DisplayName(className, language));
        }

        if (c.Level > 0)
        {
            parts.Add(string.Format(CultureInfo.CurrentCulture, loc["Checklist.Level"], c.Level));
        }

        if (c.ServerId > 0)
        {
            parts.Add(Aion2.Protocol.Aion2Servers.NameOf(c.ServerId));
        }

        if (parts.Count > 0)
        {
            TextBlock info = Label(string.Join("  ·  ", parts), "Brush.TextMuted", 13);
            info.Margin = new Thickness(0, 2, 0, 0);
            who.Children.Add(info);
        }

        Grid.SetColumn(who, 1);
        grid.Children.Add(who);

        var state = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 0, 0, 0) };
        if (online)
        {
            var pill = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 3, 11, 4), HorizontalAlignment = HorizontalAlignment.Right };
            pill.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
            pill.Child = Label("●  " + loc["Checklist.OnlineNow"], "Brush.Heal", 12, bold: true);
            state.Children.Add(pill);
        }
        else
        {
            if (c.LastOnlineMs > 0)
            {
                TextBlock last = Label(string.Format(CultureInfo.CurrentCulture, loc["Checklist.LastOnline"], DateTimeOffset.FromUnixTimeMilliseconds(c.LastOnlineMs).LocalDateTime.ToString("g", CultureInfo.CurrentCulture)), "Brush.Text", 12);
                last.HorizontalAlignment = HorizontalAlignment.Right;
                state.Children.Add(last);
            }

            TextBlock asOf = Label(loc["Checklist.RelogOld"], "Brush.TextSubtle", 11);
            asOf.TextWrapping = TextWrapping.Wrap;
            asOf.TextAlignment = TextAlignment.Right;
            asOf.MaxWidth = 260;
            asOf.HorizontalAlignment = HorizontalAlignment.Right;
            state.Children.Add(asOf);
        }

        Grid.SetColumn(state, 2);
        grid.Children.Add(state);
        if (Aion2Tickets.CurrentName is null)
        {
            // the counters come with the login: a meter started after the login has not read the character's yet
            TextBlock hint = Label(loc["Checklist.NeedLogin"], "Brush.Accent", 12);
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(0, 10, 0, 0);
            Grid.SetRow(hint, 1);
            Grid.SetColumnSpan(hint, 3);
            grid.Children.Add(hint);
        }

        host.Children.Add(grid);
        var card = new Border { Child = host, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 12), BorderBrush = Tint(accent, 0x60) };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        host.SizeChanged += (_, e) => host.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 11, 11);
        return card;
    }
}
