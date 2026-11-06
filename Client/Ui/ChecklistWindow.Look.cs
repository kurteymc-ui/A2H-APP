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

    /// <summary>
    /// A board of figures with lines between the cells: all of them in one line where the page is wide enough, else in lines of three (the roster's resets,
    /// energy, claims, whom to play next, the daily tasks): kept low, so the characters under it are in sight.
    /// </summary>
    private static UIElement Scoreboard(IReadOnlyList<Score> cells)
    {
        var grid = new Grid();
        var borders = new List<Border>();
        foreach (Score cell in cells)
        {
            var stack = new StackPanel();
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            var dot = new System.Windows.Shapes.Ellipse { Width = 6, Height = 6, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, cell.Dot);
            top.Children.Add(dot);
            TextBlock caption = Label(cell.Caption.ToUpper(CultureInfo.CurrentCulture), "Brush.TextMuted", 9.5, bold: true);
            caption.TextTrimming = TextTrimming.CharacterEllipsis;
            top.Children.Add(caption);
            stack.Children.Add(top);
            if (cell.Value is FrameworkElement value)
            {
                value.Margin = new Thickness(0, 4, 0, 0);
            }

            stack.Children.Add(cell.Value);
            if (cell.Note is not null)
            {
                stack.Children.Add(cell.Note);
            }

            var border = new Border { Child = stack, Padding = new Thickness(14, 10, 12, 10), Background = Brushes.Transparent, ToolTip = cell.Tip };
            border.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            borders.Add(border);
            grid.Children.Add(border);
        }

        int placed = 0;
        void Place(int columns)
        {
            if (placed == columns)
            {
                return;
            }

            placed = columns;
            int rows = (borders.Count + columns - 1) / columns;
            grid.ColumnDefinitions.Clear();
            grid.RowDefinitions.Clear();
            for (int c = 0; c < columns; c++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            for (int r = 0; r < rows; r++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            for (int i = 0; i < borders.Count; i++)
            {
                int column = i % columns, row = i / columns;
                Grid.SetColumn(borders[i], column);
                Grid.SetRow(borders[i], row);
                borders[i].BorderThickness = new Thickness(0, 0, column < columns - 1 && i < borders.Count - 1 ? 1 : 0, row < rows - 1 ? 1 : 0);
            }
        }

        Place(Math.Min(3, Math.Max(1, borders.Count)));
        grid.SizeChanged += (_, e) => Place(e.NewSize.Width >= 140 * borders.Count ? borders.Count : Math.Min(3, borders.Count));
        var panel = new Border { Child = grid, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 12) };
        panel.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        panel.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return panel;
    }

    /// <summary>A figure of the board: large, in a brush of the theme.</summary>
    private static TextBlock Big(string text, string brush = "Brush.Text")
    {
        TextBlock big = Label(text, brush, 19, bold: true);
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

    /// <summary>The columns of the roster's table: the character (the rest of the width), the Odyle Energy, when it is full, then one per counter.</summary>
    private static Grid RosterColumns(int counters)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 120 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
        for (int k = 0; k < counters; k++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        }

        return grid;
    }

    private static void Cell(Grid grid, UIElement element, int column)
    {
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    /// <summary>
    /// Every character as one line of a table, as the roster was, in the page's new look (6 November 2026, the owner: "the characters like before but with nice
    /// overlay boxes ... 8 character users can't see everything"): the emblem and the name in the class colour with a stripe of it at the left, the Odyle Energy
    /// with its bar, when it is full, then each counter in a small box of its own (a triangle where some is lost at the next refill). A line opens the
    /// character.
    /// </summary>
    private UIElement RosterTable(IReadOnlyList<TicketCharacter> all, IReadOnlyDictionary<string, Aion2Tickets.OdyleState?> odyle, IReadOnlyDictionary<string, long> inItems,
        TicketDef?[] defs, string? online, DateTime now, DateTime utcNow, string language)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var table = new StackPanel();
        int counters = 1 + defs.Count(d => d is not null);

        // the titles
        Grid head = RosterColumns(counters);
        head.Margin = new Thickness(12, 0, 6, 6);
        void Title(string text, string? tip, int column, bool left = false)
        {
            // as the game writes them, broken between words only ("Dimensional / Invasion")
            TextBlock t = Label(text, "Brush.TextMuted", 10.5, bold: true);
            t.TextWrapping = TextWrapping.Wrap;
            t.TextAlignment = left ? TextAlignment.Left : TextAlignment.Center;
            t.HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Bottom;
            t.Margin = new Thickness(2, 0, 2, 0);
            t.ToolTip = tip;
            Cell(head, t, column);
        }

        Title(loc["Checklist.Character"], null, 0, left: true);
        Title(loc["Checklist.RosterOdyleHead"], loc["Checklist.RosterOdyleTip"], 1);
        Title(loc["Checklist.RosterFullHead"], loc["Checklist.RosterFullTip"], 2);
        Title(loc["Checklist.RosterTasks"], loc["Checklist.RosterTasksTip"], 3);
        int column = 4;
        foreach (TicketDef? def in defs)
        {
            if (def is not null)
            {
                Title(def.ShortIn(language).Replace("\n", " ", StringComparison.Ordinal), def.NameIn(language), column++);
            }
        }

        table.Children.Add(head);

        // a number in its small box; "–" (not known, locked) without one
        UIElement Box(string text, string brush, string? tip, bool warn = false)
        {
            if (text == "–")
            {
                TextBlock dash = Label("–", "Brush.TextSubtle", 13);
                dash.HorizontalAlignment = HorizontalAlignment.Center;
                dash.ToolTip = tip;
                return dash;
            }

            var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            if (warn)
            {
                var triangle = new System.Windows.Shapes.Polygon { Points = new PointCollection { new Point(4, 0), new Point(8, 7), new Point(0, 7) }, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
                triangle.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Brush.Warning");
                line.Children.Add(triangle);
            }

            line.Children.Add(Label(text, brush, 12.5, bold: brush != "Brush.TextSubtle"));
            var box = new Border { Child = line, MinWidth = 44, Height = 26, Padding = new Thickness(6, 0, 6, 0), CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, ToolTip = tip };
            box.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
            box.SetResourceReference(Border.BorderBrushProperty, warn ? "Brush.Warning" : "Brush.Border");
            if (warn)
            {
                box.BorderBrush = Tint(Resource("Brush.Warning", Color.FromRgb(0xF0, 0xB7, 0x1C)), 0x80);
            }

            return box;
        }

        foreach (TicketCharacter c in all)
        {
            bool isOnline = c.Name == online;
            string? className = c.ClassCode > 0 ? Aion2.Protocol.Aion2SkillNames.ClassFromCode(c.ClassCode) : null;
            Color accent = className is not null ? ClassBrushes.ClassColor(className) : Resource("Brush.Accent", Color.FromRgb(0xE0, 0xB6, 0x4A));
            bool premium = Aion2Tickets.HasPremium(c, utcNow) != false;
            DateTime seenAt = c.SavedAt.ToLocalTime();
            Grid row = RosterColumns(counters);

            // who
            var who = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (className is not null && new ClassIconConverter().Convert(className, typeof(ImageSource), null, CultureInfo.CurrentCulture) is ImageSource icon)
            {
                who.Children.Add(new Image { Source = icon, Width = 24, Height = 24, Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center });
            }

            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            TextBlock name = Label(c.Name, "Brush.Text", 13.5, bold: true);
            if (className is not null)
            {
                name.Foreground = ClassBrushes.Text(className);
            }

            top.Children.Add(name);
            if (isOnline)
            {
                var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Margin = new Thickness(7, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = loc["Checklist.OnlineNow"] };
                dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Brush.Heal");
                top.Children.Add(dot);
            }

            names.Children.Add(top);
            var parts = new List<string>();
            if (c.Level > 0)
            {
                parts.Add(string.Format(CultureInfo.CurrentCulture, loc["Checklist.Level"], c.Level));
            }

            if (c.ServerId > 0)
            {
                parts.Add(Aion2.Protocol.Aion2Servers.NameOf(c.ServerId).Replace("Europe - ", "", StringComparison.Ordinal));
            }

            parts.Add(isOnline ? loc["Checklist.OnlineNow"] : string.Format(CultureInfo.CurrentCulture, loc["Checklist.RosterSeen"], AgoText(now - seenAt)));
            TextBlock sub = Label(string.Join(" \u00b7 ", parts), "Brush.TextSubtle", 10.5);
            sub.TextTrimming = TextTrimming.CharacterEllipsis;
            names.Children.Add(sub);
            who.Children.Add(names);
            Cell(row, who, 0);

            // the Odyle Energy and when it is full
            Aion2Tickets.OdyleState? o = odyle.GetValueOrDefault(c.Name);
            long items = inItems.GetValueOrDefault(c.Name);
            if (o is not null)
            {
                var energy = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 10, 0),
                    ToolTip = string.Format(CultureInfo.CurrentCulture, loc["Checklist.RosterOdyleLine"], o.Count, o.Cap, o.Bonus, o.PerTick)
                        + (items > 0 ? "\n" + string.Format(CultureInfo.CurrentCulture, loc["Checklist.RosterOdyleItems"], items.ToString("N0", CultureInfo.CurrentCulture)) : "") };
                var figure = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                figure.Children.Add(Label((o.Estimated ? "~" : "") + o.Count.ToString("N0", CultureInfo.CurrentCulture), o.Full ? "Brush.Warning" : "Brush.Accent", 13, bold: true));
                figure.Children.Add(Label(" / " + o.Cap.ToString("N0", CultureInfo.CurrentCulture), "Brush.TextSubtle", 11));
                if (o.Bonus + items > 0)
                {
                    TextBlock bonus = Label("+" + (o.Bonus + items).ToString("N0", CultureInfo.CurrentCulture), "Brush.Text", 11.5, bold: true);
                    bonus.Margin = new Thickness(6, 0, 0, 0);
                    figure.Children.Add(bonus);
                }

                energy.Children.Add(figure);
                var track = new Grid { Height = 4, Margin = new Thickness(0, 4, 0, 0) };
                var under = new Border { CornerRadius = new CornerRadius(2) };
                under.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
                track.Children.Add(under);
                var fill = new Border { CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left };
                fill.SetResourceReference(Border.BackgroundProperty, o.Full ? "Brush.Warning" : "Brush.Accent");
                double share = o.Cap <= 0 ? 0 : Math.Clamp(o.Count / (double)o.Cap, 0, 1);
                track.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * share;
                track.Children.Add(fill);
                energy.Children.Add(track);
                Cell(row, energy, 1);

                bool soon = !o.Full && o.FullAt is { } f && f - utcNow < TimeSpan.FromHours(6);
                string fullText = o.Full ? "\u25b2 " + loc["Checklist.RosterFull"] : o.FullAt is { } at ? (o.NextTick is null ? "~" : "") + SpanText(at - utcNow) : "–";
                TextBlock full = Label(fullText, o.Full || soon ? "Brush.Warning" : "Brush.Text", 12.5, bold: true);
                full.HorizontalAlignment = HorizontalAlignment.Center;
                full.ToolTip = o.Full ? loc["Checklist.RosterFullWaste"]
                    : o.FullAt is { } when ? string.Format(CultureInfo.CurrentCulture, loc["Checklist.RosterFullAt"], when.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
                      + (o.NextTick is { } tick ? "  ·  " + string.Format(CultureInfo.CurrentCulture, loc["Checklist.RosterNextTick"], o.PerTick, tick.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)) : "")
                    : null;
                Cell(row, full, 2);
            }
            else
            {
                Cell(row, Box("–", "Brush.TextSubtle", loc["Checklist.RosterUnknown"]), 1);
                Cell(row, Box("–", "Brush.TextSubtle", loc["Checklist.RosterUnknown"]), 2);
            }

            // the daily tasks, handed in of five (a reset since the reading makes it 0, shown as worked out)
            bool stale = c.Tickets.TryGetValue(Aion2Tickets.MissionsId, out TicketValue missions) && Aion2Tickets.MissionsStale(missions, now);
            if (c.Tickets.ContainsKey(Aion2Tickets.MissionsId))
            {
                int done = stale ? 0 : missions.Count;
                Cell(row, Box(done >= Aion2Tickets.MissionsPerDay ? "\u2713" : $"{(stale ? "~" : "")}{done}/{Aion2Tickets.MissionsPerDay}", done < Aion2Tickets.MissionsPerDay ? "Brush.Accent" : "Brush.Heal",
                    loc["Checklist.RosterTasksTip"]), 3);
            }
            else
            {
                Cell(row, Box("–", "Brush.TextSubtle", loc["Checklist.RosterUnknown"]), 3);
            }

            column = 4;
            foreach (TicketDef? def in defs)
            {
                if (def is null)
                {
                    continue;
                }

                bool locked = Aion2Tickets.IsLocked(c, def.Id);
                if (locked || !c.Tickets.TryGetValue(def.Id, out TicketValue value))
                {
                    Cell(row, Box("–", "Brush.TextSubtle", locked ? string.Format(CultureInfo.CurrentCulture, loc["Checklist.RosterLocked"], Aion2Tickets.UnlockLevel(def.Id)) : loc["Checklist.RosterUnknown"]), column++);
                    continue;
                }

                int count = Aion2Tickets.Expected(def, value.Count, seenAt, now, premium);
                int cap = premium && def.SubscribeMax > def.Max ? def.SubscribeMax : def.Max;
                int lost = Aion2Tickets.LostAtNextRefill(def, count, premium);
                string text = (count != value.Count ? "~" : "") + count.ToString(CultureInfo.CurrentCulture) + (value.Bonus > 0 ? $"+{value.Bonus}" : "");
                string tip = $"{def.NameIn(language)}: {count} / {Math.Max(cap, count)}" + (lost > 0 ? "  ·  " + string.Format(CultureInfo.CurrentCulture, loc[lost == 1 ? "Checklist.WarnLostOne" : "Checklist.WarnLostMany"], lost) : "");
                Cell(row, Box(text, lost > 0 ? "Brush.Warning" : count > 0 ? "Brush.Accent" : "Brush.TextMuted", tip, lost > 0), column++);
            }

            // the line: a stripe of the class colour at the left, lit under the mouse; a click opens the character
            var host = new Grid();
            host.Children.Add(new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 6, 0, 6), Background = new SolidColorBrush(accent) });
            row.Margin = new Thickness(12, 7, 6, 7);
            host.Children.Add(row);
            var line = new Border { Child = host, CornerRadius = new CornerRadius(9), Margin = new Thickness(0, 0, 0, 4), Cursor = Cursors.Hand, BorderThickness = new Thickness(1) };
            line.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
            line.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            line.MouseEnter += (_, _) => { line.BorderBrush = Tint(accent, 0x90); line.Background = Tint(accent, 0x14); };
            line.MouseLeave += (_, _) =>
            {
                line.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
                line.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
            };
            string picked = c.Name;
            line.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                ShowCharacter(picked);
            };
            table.Children.Add(line);
        }

        return table;
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
