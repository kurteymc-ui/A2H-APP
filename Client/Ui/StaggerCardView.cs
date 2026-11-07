using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDPS.Aion2.Protocol;
using AionDPS.Combat;

namespace AionDPS.Ui;

/// <summary>A player of the group as the stagger card names them: their id in the fight, name and class.</summary>
public sealed record StaggerPerson(int Id, string Name, string ClassName);

/// <summary>
/// A stagger check at a glance (7 November 2026, the owner: "groups where we don't manage to stagger but people say they used theirs ... if you keep wiping
/// to the same boss I can't sit down after the fight and go look at logs ... give exact stagger value at a time period, and showcase when mechanics start in
/// the stagger bar, make it clear, and what skills they used; allow hovering over skills"). The boss's cast that started it and how it went (the time left,
/// broken in so many seconds, or failed with so much still on the bar); the bar over the check's time - from the cast's start on the left to its end on the
/// right, how full it was, the break; then every player of the group with the exact amount they took off and their share, a player who took nothing off
/// with 0, and the skills that did it as icons: hovering one tells its hits, what each took off, the stagger buff's half as much again and when they came.
/// The meter's Stagger view shows it live in short (MainWindow.Stagger.cs, <see cref="MeterView"/>), the fight report's Stagger tab for the check or the
/// stretch picked on its chart (<see cref="StaggerChart"/>, <see cref="Rows"/>).
/// </summary>
internal static class StaggerCardView
{
    public static readonly Color Blue = Color.FromRgb(0x6F, 0x9F, 0xD8);
    private static readonly Color GreenColor = Color.FromRgb(0x5B, 0xD0, 0x7A), RedColor = Color.FromRgb(0xFF, 0x5A, 0x5A), AmberColor = Color.FromRgb(0xFF, 0xBA, 0x42);

    private static SolidColorBrush Brush(Color c, byte alpha = 0xFF)
    {
        var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    /// <summary>The check's title: its mechanic's name, else "Stagger bar" for a bar that is always up.</summary>
    public static string Title(StaggerCheck check) => check.Mechanic != 0 ? Aion2SkillNames.DisplayOf(check.Mechanic) : "Stagger bar";

    /// <summary>How the check went, in a few words, and its colour.</summary>
    public static (string Text, Color Color) Status(StaggerCheck check, DateTime now)
    {
        string S(double s) => s.ToString(s < 10 ? "0.0" : "0", CultureInfo.CurrentCulture) + " s";
        return check.Outcome switch
        {
            StaggerOutcome.Broken => ("✓ Broken in " + S(check.Seconds(now)) + (check.Limit is double lim ? " of " + S(lim) : ""), GreenColor),
            StaggerOutcome.Failed => ("✗ Not broken: " + check.Left.ToString("N0", CultureInfo.CurrentCulture) + " of " + check.Max.ToString("N0", CultureInfo.CurrentCulture) + " left", RedColor),
            StaggerOutcome.Ended => ("Ended: " + check.Left.ToString("N0", CultureInfo.CurrentCulture) + " of " + check.Max.ToString("N0", CultureInfo.CurrentCulture) + " left", Color.FromRgb(0x9D, 0xB3, 0xC2)),
            _ => check.TimeLeft(now) is double left
                ? (S(left) + " left · " + check.Left.ToString("N0", CultureInfo.CurrentCulture) + " to go", left <= 3 ? RedColor : AmberColor)
                : (check.Left.ToString("N0", CultureInfo.CurrentCulture) + " of " + check.Max.ToString("N0", CultureInfo.CurrentCulture) + " to go", AmberColor),
        };
    }

    /// <param name="check">The check.</param>
    /// <param name="now">The time now (a running check's time left).</param>
    /// <param name="people">The group: everyone listed, a player who took nothing off with 0; a player not in it who did take some off is added.</param>
    /// <param name="nameOf">A name and class for a player not in <paramref name="people"/>.</param>
    /// <param name="next">A line under the card: the next check the boss timers expect (null: none).</param>
    /// <param name="overlay">On the meter: its scaled text sizes and colours; in the report: fixed sizes.</param>
    /// <param name="close">The card's ×: hides it until the next check (null: no ×).</param>
    /// <param name="compact">The meter's Stagger view: no chart, a thin bar of what is left, tighter lines.</param>
    public static FrameworkElement Card(StaggerCheck check, DateTime now, IReadOnlyList<StaggerPerson> people, Func<int, StaggerPerson>? nameOf, string? next, bool overlay,
        Action? close = null, bool compact = false)
    {
        Func<string, double, Brush?, bool, TextBlock> text = Texts(overlay);
        TextBlock Text(string t, double size, Brush? brush = null, bool bold = false) => text(t, size, brush, bold);

        var root = new StackPanel();

        // the head: what started it, and how it went
        var head = new DockPanel();
        (string status, Color statusColor) = Status(check, now);
        if (close is not null)
        {
            var x = new TextBlock { Text = "✕", FontSize = 10, Margin = new Thickness(8, 0, 0, 0), Cursor = System.Windows.Input.Cursors.Hand, Foreground = OverlayPictures.Muted,
                VerticalAlignment = VerticalAlignment.Center, ToolTip = "Hide until the next stagger check" };
            x.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                close();
            };
            DockPanel.SetDock(x, Dock.Right);
            head.Children.Add(x);
        }

        TextBlock statusText = Text(status, 11.5, Brush(statusColor), bold: true);
        statusText.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(statusText, Dock.Right);
        head.Children.Add(statusText);
        var title = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        if (overlay)
        {
            OverlayPictures.Scaled(title, 11.5);
        }
        else
        {
            title.FontSize = 12.5;
        }

        title.Inlines.Add(new System.Windows.Documents.Run("◆ STAGGER  ") { Foreground = Brush(Blue), FontWeight = FontWeights.SemiBold, FontSize = title.FontSize * 0.85 });
        var titleRun = new System.Windows.Documents.Run(Title(check)) { FontWeight = FontWeights.SemiBold };
        titleRun.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, overlay ? "Brush.OverlayText" : "Brush.Text");
        title.Inlines.Add(titleRun);
        title.ToolTip = check.Mechanic != 0
            ? $"The boss's cast \"{Title(check)}\" started the check: its stagger bar ({check.Max:N0}) has to break before the cast ends ({check.Limit:0.#} s)."
            : $"The boss's stagger bar ({check.Max:N0}): break it to stagger the boss.";
        head.Children.Add(title);
        root.Children.Add(head);

        // the bar over the check's time: the cast's start on the left, its end on the right (the meter's Stagger view: a thin bar of what is left)
        if (compact)
        {
            var slim = new Grid { Height = 4, Margin = new Thickness(0, 4, 0, 4) };
            double fill = check.Max > 0 ? Math.Clamp((double)check.Left / check.Max, 0, 1) : 0;
            slim.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(fill, 0.0001), GridUnitType.Star) });
            slim.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - fill, 0.0001), GridUnitType.Star) });
            slim.Children.Add(new Border { Background = Brush(Blue, 0xD0), CornerRadius = new CornerRadius(2) });
            var track = new Border { Background = Brush(Colors.White, 0x14), CornerRadius = new CornerRadius(2) };
            Grid.SetColumn(track, 1);
            slim.Children.Add(track);
            root.Children.Add(slim);
        }
        else
        {
            root.Children.Add(new StaggerGauge(check, now, overlay) { Height = overlay ? 38 : 44, Margin = new Thickness(0, 5, 0, 4) });
        }

        // the players: the most first, the group's others with 0
        root.Children.Add(Rows(check.Credit, check.Used, people, nameOf, overlay, compact, "seconds into the check", "in this check"));

        if (next is not null)
        {
            TextBlock n = Text(next, 10.5, Brush(AmberColor), bold: true);
            n.Margin = new Thickness(0, 3, 0, 0);
            root.Children.Add(n);
        }

        return root;
    }

    /// <summary>
    /// The meter's Stagger view (its "Stagger" switch, after Group; 7 November 2026, the owner: "instead of doing what you did, a button called stagger
    /// after group, this is too bulky"): in place of the lines, the boss's latest check in short - its mechanic and how it went, a thin bar of what is
    /// left, every player with the exact amount they took off and the skills that did it (hover them) - and, under it, the boss's earlier checks as chips:
    /// a click shows that one. With no check yet a line says what will show.
    /// </summary>
    /// <param name="pick">A chip clicked: show that check (null: the chips can't be clicked).</param>
    public static FrameworkElement MeterView(IReadOnlyList<StaggerCheck> checks, StaggerCheck? shown, DateTime now, IReadOnlyList<StaggerPerson> people,
        Func<int, StaggerPerson>? nameOf, string? next, Action<StaggerCheck>? pick)
    {
        Func<string, double, Brush?, bool, TextBlock> text = Texts(true);
        if (shown is null)
        {
            var empty = new StackPanel();
            TextBlock line = text("No stagger check yet. When a boss's stagger bar comes up, this shows who takes how much off it and with which skills.", 11, OverlayPictures.Muted, false);
            line.TextWrapping = TextWrapping.Wrap;
            empty.Children.Add(line);
            if (next is not null)
            {
                TextBlock n = text(next, 10.5, Brush(AmberColor), true);
                n.Margin = new Thickness(0, 3, 0, 0);
                empty.Children.Add(n);
            }

            return empty;
        }

        var root = new StackPanel();
        root.Children.Add(Card(shown, now, people, nameOf, next, overlay: true, compact: true));
        if (checks.Count > 1)
        {
            WrapPanel chips = Chips(checks, shown, now, overlay: true, pick, null, false, c => c.Start.ToString("HH:mm:ss", CultureInfo.CurrentCulture));
            chips.Margin = new Thickness(0, 4, 0, 0);
            root.Children.Add(chips);
        }

        return root;
    }

    /// <summary>
    /// The boss's checks as chips, oldest first: ✓ broken (its time), ✗ not (what was left), the shown one marked; a click picks one. <paramref name="whole"/>:
    /// a first chip for the whole fight (the report), marked when <paramref name="wholeOn"/>. <paramref name="when"/>: a check's time in its tooltip.
    /// </summary>
    public static WrapPanel Chips(IReadOnlyList<StaggerCheck> checks, StaggerCheck? shown, DateTime? now, bool overlay, Action<StaggerCheck>? pick, Action? whole, bool wholeOn,
        Func<StaggerCheck, string> when)
    {
        Func<string, double, Brush?, bool, TextBlock> text = Texts(overlay);
        double size = overlay ? 10 : 12;
        var chips = new WrapPanel();
        Border Chip(string label, Color color, bool on, string tip, Action? click)
        {
            var chip = new Border
            {
                Padding = overlay ? new Thickness(5, 0, 5, 1) : new Thickness(9, 3, 9, 4), Margin = new Thickness(0, 0, overlay ? 4 : 6, overlay ? 2 : 6),
                CornerRadius = new CornerRadius(overlay ? 3 : 5), BorderThickness = new Thickness(1),
                BorderBrush = Brush(color, (byte)(on ? 0xC0 : 0x50)), Background = Brush(color, (byte)(on ? 0x30 : 0x10)),
                Child = text(label, size, Brush(color), on), ToolTip = tip, Cursor = click is not null ? System.Windows.Input.Cursors.Hand : null,
            };
            if (click is not null)
            {
                chip.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    click();
                };
            }

            return chip;
        }

        TextBlock title = text("Checks", size, OverlayPictures.Muted, true);
        title.Margin = new Thickness(0, 0, 6, overlay ? 2 : 6);
        chips.Children.Add(title);
        if (whole is not null)
        {
            chips.Children.Add(Chip("Whole fight", Color.FromRgb(0xDC, 0xB8, 0x77), wholeOn, "Everything taken off the bar over the fight", whole));
        }

        int n = 0;
        foreach (StaggerCheck c in checks)
        {
            n++;
            (string status, Color color) = Status(c, now ?? c.End ?? c.Start);
            string mark = c.Outcome switch
            {
                StaggerOutcome.Broken => "✓ " + c.Seconds(now ?? c.End ?? c.Start).ToString("0.0", CultureInfo.CurrentCulture) + " s",
                StaggerOutcome.Failed => "✗ " + c.Left.ToString("N0", CultureInfo.CurrentCulture) + " left",
                StaggerOutcome.Ended => "– " + c.Left.ToString("N0", CultureInfo.CurrentCulture) + " left",
                _ => "● now",
            };
            StaggerCheck picked = c;
            chips.Children.Add(Chip(n.ToString(CultureInfo.CurrentCulture) + "  " + mark, color, shown is not null && c.Start == shown.Start,
                $"Check {n}: {Title(c)} at {when(c)} - {status}", pick is null ? null : () => pick(picked)));
        }

        return chips;
    }

    /// <summary>A text maker: the meter's scaled sizes and colours, or the report's.</summary>
    private static Func<string, double, Brush?, bool, TextBlock> Texts(bool overlay) => (text, size, brush, bold) =>
    {
        var t = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
        if (overlay)
        {
            OverlayPictures.Scaled(t, size);
        }

        if (brush is not null)
        {
            t.Foreground = brush;
        }
        else
        {
            t.SetResourceReference(TextBlock.ForegroundProperty, overlay ? "Brush.OverlayText" : "Brush.Text");
        }

        return t;
    };

    /// <summary>
    /// The players' lines of a stretch of the fight (a check, or a period picked in the report): the most first, every player of <paramref name="people"/> who
    /// took nothing off with 0, each with the skills that did it (hover them) and, faded, those they hit with that took nothing off. <paramref name="when"/>:
    /// what the hits' times count from ("seconds into the check"); <paramref name="where"/>: the stretch in a few words ("in this check").
    /// </summary>
    public static StackPanel Rows(StaggerReport credit, IReadOnlyList<StaggerUse> used, IReadOnlyList<StaggerPerson> people, Func<int, StaggerPerson>? nameOf, bool overlay, bool compact,
        string when, string where)
    {
        Func<string, double, Brush?, bool, TextBlock> text = Texts(overlay);
        var root = new StackPanel();
        var lines = credit.Players.ToDictionary(p => p.Player);
        var shown = new List<StaggerPerson>();
        foreach (StaggerLine line in credit.Players)
        {
            shown.Add(people.FirstOrDefault(p => p.Id == line.Player) ?? nameOf?.Invoke(line.Player) ?? new StaggerPerson(line.Player, "#" + line.Player, ""));
        }

        foreach (StaggerPerson p in people.Where(p => !lines.ContainsKey(p.Id)).OrderBy(p => p.Name, StringComparer.CurrentCulture))
        {
            shown.Add(p);
        }

        int total = credit.Total;
        int best = credit.Players.Select(p => p.Amount).DefaultIfEmpty(0).Max();
        foreach (StaggerPerson p in shown)
        {
            root.Children.Add(PlayerLine(p, lines.GetValueOrDefault(p.Id), credit, used, total, best, overlay, compact, text, when, where));
        }

        int unknown = credit.Hits.Where(h => h.Player == 0).Sum(h => h.Amount);
        if (unknown > 0)
        {
            TextBlock u = text($"+{unknown:N0} with no hit of the group just before it (a hit the meter didn't see)", 10, OverlayPictures.Muted, false);
            u.Margin = new Thickness(0, 2, 0, 0);
            u.TextWrapping = TextWrapping.Wrap;
            root.Children.Add(u);
        }

        return root;
    }

    private static FrameworkElement PlayerLine(StaggerPerson p, StaggerLine? line, StaggerReport credit, IReadOnlyList<StaggerUse> used, int total, int best, bool overlay, bool compact,
        Func<string, double, Brush?, bool, TextBlock> text, string when, string where)
    {
        int amount = line?.Amount ?? 0;
        Color cls = ClassBrushes.ClassColor(p.ClassName);
        var grid = new Grid { Margin = new Thickness(0, compact ? 0 : 1, 0, compact ? 0 : 1), MinHeight = compact ? 18 : overlay ? 20 : 24, Background = Brushes.Transparent };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(overlay ? 46 : 60) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(overlay ? 40 : 50) });

        // a bar behind, as long as the player's part against the most anyone took off
        var bar = new Grid();
        double part = best > 0 ? (double)amount / best : 0;
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(part, 0.0001), GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - part, 0.0001), GridUnitType.Star) });
        bar.Children.Add(new Border { Background = Brush(cls, 0x30), CornerRadius = new CornerRadius(3) });
        Grid.SetColumnSpan(bar, 4);
        grid.Children.Add(bar);

        TextBlock name = text(p.Name, 11.5, Brush(amount > 0 ? cls : Color.FromRgb(0x9D, 0xB3, 0xC2)), true);
        name.Margin = new Thickness(5, 0, 6, 0);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.ToolTip = PlayerTip(p, line, used, total, where);
        ToolTipService.SetInitialShowDelay(name, 150);
        grid.Children.Add(name);

        // the skills that took it off, as icons: hovering one tells what it did; then, faded, those they hit with that took nothing off
        var icons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var staggered = line?.Skills ?? Array.Empty<StaggerSkill>();
        double size = compact ? 14 : overlay ? 16 : 20;
        foreach (StaggerSkill s in staggered.Take(overlay ? 5 : 8))
        {
            icons.Children.Add(SkillIcon(s, p, credit, size, when));
        }

        var did = staggered.Select(s => s.SkillId).ToHashSet();
        foreach (StaggerUse u in used.Where(u => u.Player == p.Id && !did.Contains(u.SkillId) && u.SkillId != 0).OrderByDescending(u => u.Hits).Take(amount > 0 ? (overlay ? 2 : 4) : (overlay ? 4 : 6)))
        {
            icons.Children.Add(UsedIcon(u, p, size, where));
        }

        Grid.SetColumn(icons, 1);
        grid.Children.Add(icons);

        TextBlock value = text(amount.ToString("N0", CultureInfo.CurrentCulture), 12, amount > 0 ? null : OverlayPictures.Red, true);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        value.ToolTip = amount > 0 ? $"{p.Name} took {amount:N0} off the bar {where}" : $"{p.Name} took nothing off the bar {where}";
        Grid.SetColumn(value, 2);
        grid.Children.Add(value);

        TextBlock share = text(total > 0 && amount > 0 ? (100.0 * amount / total).ToString("0", CultureInfo.CurrentCulture) + "%" : "–", 11, OverlayPictures.Muted, false);
        share.HorizontalAlignment = HorizontalAlignment.Right;
        share.Margin = new Thickness(0, 0, 4, 0);
        Grid.SetColumn(share, 3);
        grid.Children.Add(share);
        return grid;
    }

    private static FrameworkElement SkillIcon(StaggerSkill s, StaggerPerson p, StaggerReport credit, double size, string when)
    {
        ImageSource? picture = OverlayPictures.Load((s.SkillId != 0 ? Aion2SkillIcons.PathFor(s.SkillId) : null) ?? Aion2SkillIcons.PathForName(s.Name, p.ClassName));
        UIElement inner = picture is not null
            ? new Image { Source = picture, Stretch = Stretch.UniformToFill }
            : new TextBlock { Text = s.Name.Length > 0 ? s.Name[..1] : "?", FontSize = size * 0.55, FontWeight = FontWeights.SemiBold, Foreground = Brush(Blue),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var icon = new Border
        {
            Width = size, Height = size, Margin = new Thickness(0, 0, 3, 0), CornerRadius = new CornerRadius(3), ClipToBounds = true, BorderThickness = new Thickness(1),
            BorderBrush = Brush(Blue, 0xA0), Background = Brush(Blue, 0x22), Child = inner, ToolTip = SkillTip(s, p, credit, when),
        };
        ToolTipService.SetInitialShowDelay(icon, 100);
        ToolTipService.SetShowDuration(icon, 30_000);
        return icon;
    }

    /// <summary>A skill the player hit the boss with in the check that took nothing off the bar: faded, its hits on hover.</summary>
    private static FrameworkElement UsedIcon(StaggerUse u, StaggerPerson p, double size, string where)
    {
        string name = Aion2SkillNames.DisplayOf(u.SkillId);
        ImageSource? picture = OverlayPictures.Load(Aion2SkillIcons.PathFor(u.SkillId) ?? Aion2SkillIcons.PathForName(Aion2SkillNames.NameOf(u.SkillId), p.ClassName));
        UIElement inner = picture is not null
            ? new Image { Source = picture, Stretch = Stretch.UniformToFill }
            : new TextBlock { Text = name.Length > 0 ? name[..1] : "?", FontSize = size * 0.55, Foreground = OverlayPictures.Muted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var tip = new StackPanel { MaxWidth = 320 };
        tip.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, FontSize = 13 });
        tip.Children.Add(new TextBlock { Text = $"{p.Name} hit the boss with it {u.Hits} time{(u.Hits == 1 ? "" : "s")} {where}: none of those hits took anything off the stagger bar.", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        var icon = new Border
        {
            Width = size, Height = size, Margin = new Thickness(0, 0, 3, 0), CornerRadius = new CornerRadius(3), ClipToBounds = true, BorderThickness = new Thickness(1),
            BorderBrush = Brush(Color.FromRgb(0x9D, 0xB3, 0xC2), 0x50), Child = inner, Opacity = 0.45, ToolTip = tip,
        };
        ToolTipService.SetInitialShowDelay(icon, 100);
        return icon;
    }

    /// <summary>A skill's part: its name, its hits that took something off and what each did (half as much again with a stagger buff), and when they came.</summary>
    public static FrameworkElement SkillTip(StaggerSkill s, StaggerPerson p, StaggerReport credit, string when)
    {
        var hits = credit.Hits.Where(h => h.Player == p.Id && h.SkillId == s.SkillId).OrderBy(h => h.At).ToList();
        var panel = new StackPanel { MaxWidth = 340 };
        panel.Children.Add(new TextBlock { Text = s.Name.Length > 0 ? Aion2SkillNames.DisplayOf(s.SkillId) is { Length: > 0 } shown && shown != s.SkillId.ToString(CultureInfo.InvariantCulture) ? shown : s.Name : "Attack", FontWeight = FontWeights.SemiBold, FontSize = 13 });
        panel.Children.Add(new TextBlock { Text = $"{p.Name}: {s.Amount:N0} off the bar in {s.Hits} hit{(s.Hits == 1 ? "" : "s")}", FontSize = 12, Margin = new Thickness(0, 2, 0, 4) });
        int boosted = hits.Count(h => h.Boosted);
        var amounts = hits.GroupBy(h => h.Amount).OrderByDescending(g => g.Count()).Select(g => $"{g.Count()} × {g.Key}").ToList();
        if (amounts.Count > 0)
        {
            panel.Children.Add(new TextBlock { Text = "Each hit: " + string.Join(", ", amounts) + (boosted > 0 ? $"  ({boosted} with a stagger buff: half as much again)" : ""), FontSize = 11.5, TextWrapping = TextWrapping.Wrap });
        }

        if (hits.Count > 0)
        {
            string times = string.Join(", ", hits.Take(16).Select(h => h.At.ToString("0.0", CultureInfo.CurrentCulture) + " s"));
            panel.Children.Add(new TextBlock { Text = $"When ({when}): " + times + (hits.Count > 16 ? " …" : ""), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        }

        if (hits.Any(h => !h.Exact))
        {
            panel.Children.Add(new TextBlock { Text = "Some of it went to the nearest likely hit (the amount matched no skill exactly).", FontSize = 11, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        }

        return panel;
    }

    /// <summary>A player's part: the amount, the share, the skills with what each took off.</summary>
    public static FrameworkElement PlayerTip(StaggerPerson p, StaggerLine? line, IReadOnlyList<StaggerUse> used, int total, string where)
    {
        var panel = new StackPanel { MaxWidth = 340 };
        panel.Children.Add(new TextBlock { Text = p.Name, FontWeight = FontWeights.SemiBold, FontSize = 13 });
        var others = used.Where(u => u.Player == p.Id && u.SkillId != 0 && line?.Skills.All(s => s.SkillId != u.SkillId) != false).OrderByDescending(u => u.Hits).ToList();
        string Others() => string.Join(", ", others.Take(8).Select(u => $"{Aion2SkillNames.DisplayOf(u.SkillId)} ×{u.Hits}"));
        if (line is null || line.Amount <= 0)
        {
            panel.Children.Add(new TextBlock { Text = others.Count > 0
                ? $"Took nothing off the boss's stagger bar {where}. Hit the boss with: " + Others() + " (none of it lowered the bar)."
                : $"Took nothing off the boss's stagger bar {where}, and the meter saw no hit of theirs on the boss.", FontSize = 12, TextWrapping = TextWrapping.Wrap });
            return panel;
        }

        panel.Children.Add(new TextBlock { Text = $"{line.Amount:N0} off the bar ({(total > 0 ? 100.0 * line.Amount / total : 0):0} % of what came off){(line.Breaks > 0 ? " · finished the break" : "")}", FontSize = 12, Margin = new Thickness(0, 2, 0, 4) });
        foreach (StaggerSkill s in line.Skills)
        {
            panel.Children.Add(new TextBlock { Text = $"{s.Amount,6:N0}   {s.Name} ({s.Hits} hit{(s.Hits == 1 ? "" : "s")})", FontSize = 11.5, FontFamily = new FontFamily("Consolas, Segoe UI") });
        }

        if (others.Count > 0)
        {
            panel.Children.Add(new TextBlock { Text = "Also hit with, taking nothing off: " + Others(), FontSize = 11.5, Opacity = 0.8, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        }

        return panel;
    }
}

/// <summary>
/// The stagger bar over a check's time: the left edge is the start (the boss's cast), the right edge the cast's end - the time to break it - with a red line;
/// the blue area how full the bar was, a diamond at the break; a running check's "now". A bar without a cast goes from the check's start to its end (or now).
/// </summary>
internal sealed class StaggerGauge : FrameworkElement
{
    private readonly StaggerCheck _check;
    private readonly DateTime _now;
    private readonly bool _overlay;

    public StaggerGauge(StaggerCheck check, DateTime now, bool overlay)
    {
        _check = check;
        _now = now;
        _overlay = overlay;
        ToolTip = check.Mechanic != 0
            ? "The boss's stagger bar from the start of its cast (left) to the end of the cast (red line): it has to be empty before the line."
            : "The boss's stagger bar over the check: how full it was.";
    }

    private static SolidColorBrush B(Color c, byte a)
    {
        var b = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 4 || h <= 4)
        {
            return;
        }

        Color blue = StaggerCardView.Blue;
        double labelH = 10, top = 0, bottom = h - labelH, height = bottom - top;
        double span = Math.Max(_check.Limit ?? 0, _check.Seconds(_now));
        span = Math.Max(span, 1);
        double X(double t) => Math.Clamp(t / span, 0, 1) * w;
        dc.DrawRoundedRectangle(B(Colors.White, 0x10), null, new Rect(0, top, w, height), 3, 3);

        // how full the bar was over the check: steps from reading to reading
        var bar = _check.Credit.Bar;
        if (bar.Count > 0)
        {
            var area = new StreamGeometry();
            using (StreamGeometryContext g = area.Open())
            {
                g.BeginFigure(new Point(0, bottom), true, true);
                double last = 1;
                g.LineTo(new Point(0, bottom - height * last), true, false);
                foreach ((double at, double fill) in bar)
                {
                    g.LineTo(new Point(X(at), bottom - height * last), true, false);
                    g.LineTo(new Point(X(at), bottom - height * fill), true, false);
                    last = fill;
                }

                double end = _check.End is DateTime e ? (e - _check.Start).TotalSeconds : _check.Seconds(_now);
                g.LineTo(new Point(X(end), bottom - height * last), true, false);
                g.LineTo(new Point(X(end), bottom), true, false);
            }

            area.Freeze();
            dc.DrawGeometry(B(blue, 0x70), null, area);

            // its top edge, brighter: how the bar fell
            var edge = new StreamGeometry();
            using (StreamGeometryContext g = edge.Open())
            {
                double last = 1;
                g.BeginFigure(new Point(0, bottom - height * last), false, false);
                foreach ((double at, double fill) in bar)
                {
                    g.LineTo(new Point(X(at), bottom - height * last), true, false);
                    g.LineTo(new Point(X(at), bottom - height * fill), true, false);
                    last = fill;
                }

                double end = _check.End is DateTime e ? (e - _check.Start).TotalSeconds : _check.Seconds(_now);
                g.LineTo(new Point(X(end), bottom - height * last), true, false);
            }

            edge.Freeze();
            dc.DrawGeometry(null, new Pen(B(Color.FromRgb(0xA8, 0xC8, 0xF0), 0xF0), 1.4), edge);
        }

        var typeface = new Typeface("Segoe UI");
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        void Label(string text, double x, Brush brush, bool right = false)
        {
            var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 9.5, brush, dpi);
            dc.DrawText(ft, new Point(right ? Math.Max(0, x - ft.Width) : Math.Min(x, w - ft.Width), bottom + 0.5));
        }

        Brush muted = OverlayPictures.Muted;
        Label(_check.Mechanic != 0 ? "▶ cast starts" : "0 s", 0, muted);

        // the end of the cast: the time to break it
        if (_check.Limit is double limit)
        {
            double x = X(limit);
            dc.DrawLine(new Pen(B(Color.FromRgb(0xFF, 0x5A, 0x5A), 0xE0), 2), new Point(x, top - 1), new Point(x, bottom + 1));
            Label(limit.ToString("0.#", CultureInfo.CurrentCulture) + " s: cast ends", x, B(Color.FromRgb(0xFF, 0x8A, 0x8A), 0xFF), right: true);
        }

        // the break, or now while it runs
        StaggerBreak? broke = _check.Credit.Breaks.FirstOrDefault();
        if (_check.Outcome == StaggerOutcome.Broken && broke is not null)
        {
            double x = X(broke.At);
            var diamond = new StreamGeometry();
            using (StreamGeometryContext g = diamond.Open())
            {
                double cy = top + height / 2, r = Math.Min(6, height / 2 - 1);
                g.BeginFigure(new Point(x, cy - r), true, true);
                g.LineTo(new Point(x + r, cy), true, false);
                g.LineTo(new Point(x, cy + r), true, false);
                g.LineTo(new Point(x - r, cy), true, false);
            }

            diamond.Freeze();
            dc.DrawGeometry(B(Color.FromRgb(0x5B, 0xD0, 0x7A), 0xFF), new Pen(B(Colors.Black, 0xA0), 1), diamond);
            if (_check.Limit is null)
            {
                Label(broke.At.ToString("0.#", CultureInfo.CurrentCulture) + " s: broken", x, B(Color.FromRgb(0x5B, 0xD0, 0x7A), 0xFF), right: true);
            }
        }
        else if (_check.Outcome == StaggerOutcome.Running)
        {
            double x = X(_check.Seconds(_now));
            dc.DrawLine(new Pen(B(Colors.White, 0xC0), 1.5), new Point(x, top), new Point(x, bottom));
        }

        // what is left, on the bar
        var left = new FormattedText($"{_check.Left:N0} / {_check.Max:N0} left", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(typeface.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            _overlay ? 10.5 : 11.5, B(Colors.White, 0xF0), dpi);
        dc.DrawRectangle(B(Colors.Black, 0x70), null, new Rect(4, top + (height - left.Height) / 2 - 1, left.Width + 6, left.Height + 2));
        dc.DrawText(left, new Point(7, top + (height - left.Height) / 2));
    }
}
