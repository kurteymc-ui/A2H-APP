using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AionDPS.Ui;

/// <summary>The pieces the Journey page is drawn with: cards, period chips, figures with their change and a small line of the days, the day bars.</summary>
internal static class JourneyViews
{
    public static readonly Color Gold = Color.FromRgb(0xDC, 0xB8, 0x77);
    public static readonly Color Up = Color.FromRgb(0x5F, 0xD0, 0x8A);
    public static readonly Color Down = Color.FromRgb(0xEE, 0x6E, 0x6E);
    public static readonly Color Boss = Color.FromRgb(0xE8, 0xC0, 0x5A);
    public static readonly Color Dungeon = Color.FromRgb(0xA4, 0x8C, 0xF4);
    public static readonly Color Other = Color.FromRgb(0x4A, 0x55, 0x66);

    public static TextBlock Text(string text, string brush, double size, bool bold = false)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    public static TextBlock Wrapped(string text, string brush, double size, bool bold = false)
    {
        TextBlock block = Text(text, brush, size, bold);
        block.TextWrapping = TextWrapping.Wrap;
        return block;
    }

    /// <summary>A card: the panel's colour, a thin edge, round corners; an optional small title in capitals with something on its right.</summary>
    public static Border Card(UIElement content, string? title = null, UIElement? titleRight = null, Thickness? margin = null)
    {
        var stack = new StackPanel();
        if (title is not null)
        {
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            if (titleRight is not null)
            {
                DockPanel.SetDock(titleRight, Dock.Right);
                head.Children.Add(titleRight);
            }

            head.Children.Add(Text(title.ToUpperInvariant(), "Brush.TextMuted", 11, bold: true));
            stack.Children.Add(head);
        }

        stack.Children.Add(content);
        var card = new Border
        {
            Child = stack, Padding = new Thickness(18, 14, 18, 16), Margin = margin ?? new Thickness(0, 0, 12, 12), CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
        };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return card;
    }

    /// <summary>A pill to pick with (the periods): raised when on.</summary>
    public static Border Chip(string text, bool on, Action pick, string? glyph = null)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        if (glyph is not null)
        {
            TextBlock icon = Text(glyph, on ? "Brush.Accent" : "Brush.TextSubtle", 11);
            icon.FontFamily = new FontFamily("Segoe MDL2 Assets");
            icon.Margin = new Thickness(0, 1, 6, 0);
            line.Children.Add(icon);
        }

        line.Children.Add(Text(text, on ? "Brush.Text" : "Brush.TextMuted", 12.5, bold: on));
        var chip = new Border
        {
            Child = line, Padding = new Thickness(12, 5, 12, 6), Margin = new Thickness(0, 0, 4, 0), CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand,
            BorderThickness = new Thickness(1), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent,
        };
        if (on)
        {
            chip.SetResourceReference(Border.BackgroundProperty, "Brush.ControlRaised");
            chip.BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, Gold.R, Gold.G, Gold.B));
        }
        else
        {
            chip.MouseEnter += (_, _) => chip.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
            chip.MouseLeave += (_, _) => chip.Background = Brushes.Transparent;
        }

        chip.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            pick();
        };
        return chip;
    }

    /// <summary>A change against the period before: an arrow and the percent, green when it is better (<paramref name="higherIsBetter"/>), red when worse; "new"
    /// when there was nothing before; nothing when there is nothing to say.</summary>
    public static UIElement? Delta(double now, double before, bool higherIsBetter, bool percent = true, string? unit = null)
    {
        if (now == 0 && before == 0)
        {
            return null;
        }

        string text;
        bool good;
        if (before == 0)
        {
            text = LocalizationManager.Instance["Journey.New"];
            good = higherIsBetter;
        }
        else
        {
            double change = percent ? (now - before) / Math.Abs(before) * 100 : now - before;
            if (Math.Abs(change) < (percent ? 0.5 : 0.5))
            {
                return Text("=", "Brush.TextSubtle", 11.5, bold: true);
            }

            good = change > 0 == higherIsBetter;
            string arrow = change > 0 ? "↗" : "↘";
            text = arrow + " " + (percent ? Math.Abs(change).ToString(Math.Abs(change) < 10 ? "0.#" : "0", CultureInfo.CurrentCulture) + "%"
                : Math.Abs(change).ToString("0.#", CultureInfo.CurrentCulture) + (unit ?? ""));
        }

        var block = new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(good ? Up : Down), VerticalAlignment = VerticalAlignment.Center };
        return block;
    }

    /// <summary>A small line of the values, one point a day (left oldest), in a box <paramref name="width"/> by <paramref name="height"/>.</summary>
    public static UIElement Sparkline(IReadOnlyList<double> values, Color colour, double width = 130, double height = 26)
    {
        var canvas = new Canvas { Width = width, Height = height, ClipToBounds = false };
        if (values.Count < 2 || values.All(v => v == 0))
        {
            var flat = new Line { X1 = 0, X2 = width, Y1 = height - 1, Y2 = height - 1, StrokeThickness = 1, Stroke = new SolidColorBrush(Color.FromArgb(0x50, colour.R, colour.G, colour.B)) };
            canvas.Children.Add(flat);
            return canvas;
        }

        double max = values.Max(), min = Math.Min(0, values.Min()), span = Math.Max(1e-9, max - min);
        var points = new PointCollection();
        for (int i = 0; i < values.Count; i++)
        {
            points.Add(new Point(i * width / (values.Count - 1), height - 2 - ((values[i] - min) / span * (height - 4))));
        }

        var fill = new Polygon { Fill = new LinearGradientBrush(Color.FromArgb(0x40, colour.R, colour.G, colour.B), Color.FromArgb(0x00, colour.R, colour.G, colour.B), 90) };
        var area = new PointCollection(points) { new Point(width, height), new Point(0, height) };
        fill.Points = area;
        canvas.Children.Add(fill);
        canvas.Children.Add(new Polyline { Points = points, Stroke = new SolidColorBrush(colour), StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round });
        return canvas;
    }

    /// <summary>A figure: its title and change on top, the value big, a line under it (the change's note) and the days' line.</summary>
    public static Border Figure(string title, string value, UIElement? delta, string? note, IReadOnlyList<double>? days, Color colour)
    {
        var stack = new StackPanel();
        var head = new DockPanel();
        if (delta is not null)
        {
            DockPanel.SetDock(delta, Dock.Right);
            head.Children.Add(delta);
        }

        head.Children.Add(Text(title, "Brush.TextMuted", 12));
        stack.Children.Add(head);
        TextBlock big = Text(value, "Brush.Text", 24, bold: true);
        big.Margin = new Thickness(0, 6, 0, 2);
        stack.Children.Add(big);
        if (note is not null)
        {
            stack.Children.Add(Text(note, "Brush.TextSubtle", 11));
        }

        if (days is not null)
        {
            var line = Sparkline(days, colour);
            ((FrameworkElement)line).Margin = new Thickness(0, 8, 0, 0);
            stack.Children.Add(line);
        }

        var card = new Border { Child = stack, Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 10, 10), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return card;
    }

    /// <summary>One day's bar: its parts stacked (bosses, dungeons, the rest; minutes), what dropped best above it, a figure under it.</summary>
    public sealed record DayBar(DateTime Day, double Boss, double Dungeon, double Other, string? Under, ImageSource? Drop, string? DropTip);

    /// <summary>The days side by side as bars as high as the time played, the tallest the full height.</summary>
    public static UIElement DayBars(IReadOnlyList<DayBar> days, double height = 170)
    {
        var grid = new Grid();
        double max = Math.Max(1, days.Max(d => d.Boss + d.Dungeon + d.Other));
        for (int i = 0; i < days.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            DayBar d = days[i];
            var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var dropBox = new Grid { Height = 30, Margin = new Thickness(0, 0, 0, 4) };
            if (d.Drop is not null)
            {
                var icon = new Border
                {
                    Width = 28, Height = 28, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1.5), HorizontalAlignment = HorizontalAlignment.Center,
                    BorderBrush = new SolidColorBrush(Gold), Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1C, 0x24)), ToolTip = d.DropTip,
                    Child = new Image { Source = d.Drop, Width = 22, Height = 22 },
                };
                dropBox.Children.Add(icon);
            }

            column.Children.Add(dropBox);
            double total = d.Boss + d.Dungeon + d.Other;
            double barHeight = total <= 0 ? 0 : Math.Max(4, total / max * height);
            var bar = new Grid { Width = 34, Height = height, VerticalAlignment = VerticalAlignment.Bottom };
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
            void Part(double minutes, Color colour, CornerRadius corner)
            {
                if (minutes <= 0 || total <= 0)
                {
                    return;
                }

                stack.Children.Add(new Border { Height = Math.Max(2, barHeight * minutes / total), Background = new SolidColorBrush(colour), CornerRadius = corner });
            }

            // from the top: the rest, dungeons, bosses (the bosses at the foot, as a line of gold)
            Part(d.Other, Other, new CornerRadius(5, 5, 0, 0));
            Part(d.Dungeon, Dungeon, new CornerRadius(0));
            Part(d.Boss, Boss, new CornerRadius(0, 0, 3, 3));
            if (stack.Children.Count > 0 && stack.Children[0] is Border first && d.Other <= 0)
            {
                first.CornerRadius = new CornerRadius(5, 5, first.CornerRadius.BottomRight, first.CornerRadius.BottomLeft);
            }

            bar.Children.Add(stack);
            bar.ToolTip = string.Format(CultureInfo.CurrentCulture, LocalizationManager.Instance["Journey.DayTip"], d.Day.ToString("dddd d MMM", CultureInfo.CurrentCulture), Hours(total));
            column.Children.Add(bar);
            TextBlock day = Text(d.Day.ToString("ddd", CultureInfo.CurrentCulture).ToUpperInvariant(), "Brush.TextMuted", 11, bold: true);
            day.HorizontalAlignment = HorizontalAlignment.Center;
            day.Margin = new Thickness(0, 8, 0, 0);
            column.Children.Add(day);
            TextBlock under = Text(d.Under ?? "", "Brush.Accent", 11, bold: true);
            under.HorizontalAlignment = HorizontalAlignment.Center;
            column.Children.Add(under);
            Grid.SetColumn(column, i);
            grid.Children.Add(column);
        }

        return grid;
    }

    /// <summary>Minutes as "21 h 04" or "45 m".</summary>
    public static string Hours(double minutes)
    {
        int m = (int)Math.Round(minutes);
        return m >= 60 ? $"{m / 60} h {m % 60:00}" : $"{m} m";
    }

    /// <summary>A legend dot and its word.</summary>
    public static UIElement Legend(Color colour, string text)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0) };
        line.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(colour), Margin = new Thickness(0, 1, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        line.Children.Add(Text(text, "Brush.TextMuted", 11.5));
        return line;
    }
}
