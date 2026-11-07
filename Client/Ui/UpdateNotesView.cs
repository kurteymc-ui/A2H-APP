using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using AionDPS.Update;

namespace AionDPS.Ui;

/// <summary>
/// The update notes in the update prompt (<see cref="UpdateNotes"/>): a "What's new" bar that says how much changed and opens into every version's
/// changes, newest first. Each version has its number, its day and a headline; each change a coloured word for its kind (new, improved, fixed),
/// where in the app it is, a title, a few plain sentences and, for most, an example set apart under a gold stroke.
/// </summary>
internal static class UpdateNotesView
{
    private static readonly Color Gold = Color.FromRgb(0xDC, 0xB8, 0x77);

    private static SolidColorBrush Brush(Color c, byte alpha = 0xFF)
    {
        var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    private static (string Word, Color Color) KindOf(string kind) => kind.ToLowerInvariant() switch
    {
        "new" => ("NEW", Color.FromRgb(0x5B, 0xD0, 0x7A)),
        "fixed" => ("FIXED", Color.FromRgb(0xF0, 0xA0, 0x5A)),
        _ => ("IMPROVED", Color.FromRgb(0x6F, 0x9F, 0xD8)),
    };

    private static TextBlock Text(string text, double size, string brushKey = "Brush.Text", bool bold = false, double opacity = 1)
    {
        var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Opacity = opacity };
        t.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return t;
    }

    /// <summary>The bar and, under it, the notes (open to begin with when <paramref name="open"/>).</summary>
    public static FrameworkElement Build(IReadOnlyList<UpdateRelease> releases, bool open, double maxHeight = 420)
    {
        int changes = releases.Sum(r => r.Items.Count);
        var root = new StackPanel();

        // the bar: "What's new", how much, and the chevron
        var bar = new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10, 12, 10), Cursor = Cursors.Hand, BorderThickness = new Thickness(1),
            BorderBrush = Brush(Gold, 0x55), Background = Brush(Gold, 0x14),
        };
        var head = new DockPanel();
        var chevron = new TextBlock { Text = open ? "" : "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 11, Foreground = Brush(Gold),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        DockPanel.SetDock(chevron, Dock.Right);
        head.Children.Add(chevron);
        var count = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0),
            Text = changes == 1 ? "1 change" : $"{changes} changes" + (releases.Count > 1 ? $" in {releases.Count} updates" : "") };
        count.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        DockPanel.SetDock(count, Dock.Right);
        head.Children.Add(count);
        var title = new TextBlock { Text = "What's new", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brush(Gold), VerticalAlignment = VerticalAlignment.Center };
        head.Children.Add(title);
        bar.Child = head;
        root.Children.Add(bar);

        // the headlines under the bar while it is shut: the gist without opening it
        var gist = new StackPanel { Margin = new Thickness(2, 10, 2, 0) };
        foreach (string line in releases.Select(r => r.Headline ?? r.Items[0].Title).Distinct().Take(3))
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var dot = new TextBlock { Text = "◆", FontSize = 8, Foreground = Brush(Gold), Margin = new Thickness(0, 4, 9, 0), VerticalAlignment = VerticalAlignment.Top };
            row.Children.Add(dot);
            row.Children.Add(Text(line, 12.5));
            gist.Children.Add(row);
        }

        root.Children.Add(gist);

        var list = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (UpdateRelease release in releases)
        {
            list.Children.Add(Release(release));
        }

        var scroll = new ScrollViewer
        {
            Content = list, MaxHeight = maxHeight, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 6, 0), Margin = new Thickness(0, 4, 0, 0),
        };
        root.Children.Add(scroll);

        void Show(bool shown)
        {
            scroll.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            gist.Visibility = shown ? Visibility.Collapsed : Visibility.Visible;
            chevron.Text = shown ? "" : "";
        }

        Show(open);
        bar.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Show(scroll.Visibility != Visibility.Visible);
        };
        bar.MouseEnter += (_, _) => bar.Background = Brush(Gold, 0x22);
        bar.MouseLeave += (_, _) => bar.Background = Brush(Gold, 0x14);
        return root;
    }

    private static FrameworkElement Release(UpdateRelease release)
    {
        var block = new StackPanel { Margin = new Thickness(0, 12, 0, 4) };

        // the version and its day, a thin line to the right
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        var version = new TextBlock { Text = "Version " + release.Version, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brush(Gold), VerticalAlignment = VerticalAlignment.Center };
        top.Children.Add(version);
        if (DateTime.TryParseExact(release.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime day))
        {
            TextBlock date = Text(day.ToString("d MMMM", CultureInfo.GetCultureInfo("en-GB")), 11.5, "Brush.TextMuted");
            date.Margin = new Thickness(8, 0, 0, 0);
            date.VerticalAlignment = VerticalAlignment.Center;
            top.Children.Add(date);
        }

        top.Children.Add(new Border { Height = 1, Margin = new Thickness(12, 1, 0, 0), Background = Brush(Gold, 0x30), VerticalAlignment = VerticalAlignment.Center });
        block.Children.Add(top);
        if (release.Headline is { Length: > 0 } headline)
        {
            TextBlock h = Text(headline, 15.5, bold: true);
            h.Margin = new Thickness(0, 2, 0, 2);
            block.Children.Add(h);
        }

        foreach (UpdateItem item in release.Items)
        {
            block.Children.Add(Item(item));
        }

        return block;
    }

    private static FrameworkElement Item(UpdateItem item)
    {
        (string word, Color color) = KindOf(item.Kind);
        var card = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };

        // NEW · Meter
        var tags = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        tags.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 1, 5, 1), Background = Brush(color, 0x26), BorderBrush = Brush(color, 0x70), BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = word, FontSize = 9.5, FontWeight = FontWeights.Bold, Foreground = Brush(color) },
        });
        if (item.Area.Length > 0)
        {
            TextBlock area = Text(item.Area.ToUpperInvariant(), 10, "Brush.TextMuted", bold: true);
            area.Margin = new Thickness(8, 1, 0, 0);
            area.VerticalAlignment = VerticalAlignment.Center;
            tags.Children.Add(area);
        }

        card.Children.Add(tags);
        card.Children.Add(Text(item.Title, 13.5, bold: true));
        TextBlock body = Text(item.Text, 12.5, "Brush.Text", opacity: 0.82);
        body.Margin = new Thickness(0, 3, 0, 0);
        body.LineHeight = 18;
        card.Children.Add(body);
        if (item.Example is { Length: > 0 } example)
        {
            var text = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, LineHeight = 17 };
            var label = new Run("Example  ") { FontWeight = FontWeights.SemiBold, Foreground = Brush(Gold) };
            text.Inlines.Add(label);
            var run = new Run(example);
            run.SetResourceReference(TextElement.ForegroundProperty, "Brush.Text");
            text.Inlines.Add(run);
            card.Children.Add(new Border
            {
                Margin = new Thickness(0, 7, 0, 0), Padding = new Thickness(10, 6, 10, 7), BorderThickness = new Thickness(2, 0, 0, 0), BorderBrush = Brush(Gold, 0xB0),
                Background = Brush(Colors.White, 0x08), CornerRadius = new CornerRadius(0, 5, 5, 0), Child = text,
            });
        }

        return card;
    }
}
