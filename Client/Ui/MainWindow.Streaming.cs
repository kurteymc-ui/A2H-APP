using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AionDPS.Ui;

public partial class MainWindow
{
    /// <summary>Whether the widgets' server was asked to start and couldn't (no free port): the Streaming page says so instead of "starting".</summary>
    private bool _streamFailed;

    /// <summary>
    /// The Streaming page (redone 6 November 2026, the owner: "ok tf is this, fix it a bit and make sure it works"): a state panel that says plainly whether the
    /// widgets run, where and for how many browser sources, with a button to turn them on when they're off and what went wrong when they couldn't start; the
    /// two choices that matter (names, a second PC) as switches right there; the three widgets with their link in a box of the app's own look, copy and
    /// preview; how to add one to OBS or Streamlabs in three steps; the link's options as chips.
    /// </summary>
    private ToolPage BuildStreamPage(Action hotkeys, Action settingsPage, Action<MeterSettings> applied)
    {
        var loc = LocalizationManager.Instance;
        var stack = new StackPanel { Margin = new Thickness(0, 4, 0, 0), MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };
        var mono = new FontFamily("Cascadia Mono, Consolas");
        var gold = Color.FromRgb(0xE6, 0xC0, 0x68);

        TextBlock Words(string text, string brush, double size, bool bold = false)
        {
            var t = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap };
            t.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return t;
        }

        Border Panel(UIElement child, Thickness? padding = null)
        {
            var b = new Border { Child = child, Padding = padding ?? new Thickness(18, 16, 18, 16), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 12) };
            b.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
            b.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            return b;
        }

        // a switch of the app's look with its words beside it
        (CheckBox Box, FrameworkElement Line) Switch(string title, string about, bool on, Action<MeterSettings, bool> set)
        {
            var box = new CheckBox { IsChecked = on, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 12, 0) };
            if (Application.Current?.TryFindResource("SettingsSwitch") is Style style)
            {
                box.Style = style;
            }

            box.Click += (_, _) =>
            {
                MeterSettings current = MeterSettings.Load();
                set(current, box.IsChecked == true);
                current.Save();
                _streamSettingsAt = DateTime.MinValue;   // taken over at once
                StreamTick();
            };
            var words = new StackPanel();
            words.Children.Add(Words(title, "Brush.Text", 13, bold: true));
            TextBlock small = Words(about, "Brush.TextMuted", 12);
            small.Margin = new Thickness(0, 2, 0, 0);
            words.Children.Add(small);
            var line = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            DockPanel.SetDock(box, Dock.Left);
            line.Children.Add(box);
            line.Children.Add(words);
            return (box, line);
        }

        // ---- the state: running or not, where, how many browser sources; the two choices
        var dot = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        var dotIcon = new TextBlock { FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        dot.Child = dotIcon;
        TextBlock status = Words("", "Brush.Text", 16, bold: true);
        TextBlock where = Words("", "Brush.TextMuted", 12.5);
        where.Margin = new Thickness(0, 2, 0, 0);
        var turnOn = new Button
        {
            Content = new TextBlock { Text = "Turn the widgets on", FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x14, 0x08)) },
            Padding = new Thickness(16, 7, 16, 7), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0),
        };
        if (TryFindResource("GoldButton") is Style goldButton)
        {
            turnOn.Style = goldButton;
        }

        turnOn.Click += (_, _) =>
        {
            MeterSettings current = MeterSettings.Load();
            current.StreamOverlayOn = true;
            current.Save();
            _streamSettingsAt = DateTime.MinValue;
            StreamTick();
            applied(current);
        };
        var stateLine = new DockPanel();
        DockPanel.SetDock(dot, Dock.Left);
        DockPanel.SetDock(turnOn, Dock.Right);
        stateLine.Children.Add(dot);
        stateLine.Children.Add(turnOn);
        var stateWords = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stateWords.Children.Add(status);
        stateWords.Children.Add(where);
        stateLine.Children.Add(stateWords);
        var head = new StackPanel();
        head.Children.Add(stateLine);
        var rule = new Border { Height = 1, Margin = new Thickness(0, 16, 0, 2) };
        rule.SetResourceReference(Border.BackgroundProperty, "Brush.Border");
        head.Children.Add(rule);
        MeterSettings now = MeterSettings.Load();
        var names = Switch("Show the other players' names", "Off: everyone on the meter by class (\"Cleric\", \"Cleric 2\" …). The character card always shows your own name.",
            now.StreamShowNames, (s, v) => s.StreamShowNames = v);
        head.Children.Add(names.Line);
        var network = Switch("OBS on a second PC", "Lets another PC in your home network open the widgets. On that PC, put this PC's address in the link in place of 127.0.0.1.",
            now.StreamOverlayNetwork, (s, v) => s.StreamOverlayNetwork = v);
        head.Children.Add(network.Line);
        TextBlock secondPc = Words("", "Brush.TextSubtle", 12);
        secondPc.FontFamily = mono;
        secondPc.Margin = new Thickness(56, 6, 0, 0);
        head.Children.Add(secondPc);
        stack.Children.Add(Panel(head));

        // ---- the widgets, each with its link
        var links = new List<(TextBlock Link, Border Box, string Widget, Button Copy, Button Look)>();
        foreach ((string widget, string title, string what, string size, string glyph) in new[]
                 {
                     ("meter", "Meter", "The meter as you see it over the game: ten lines in their class colours, the buffs and healing on the bars, the boss line with its kill and enrage times. The lines slide into their new places.", "400 × 420", ""),
                     ("card", "Character card", "Your character: portrait (or class emblem) in your class colour, level, server, legion, Item Level and Combat Power, and this stream so far: how long, bosses killed, best DPS, Combat Power gained.", "680 × 160", ""),
                     ("boss", "Boss bar", "The boss you fight: its health as a wide bar with the percent, when it dies at your pace and its enrage. Shows itself on a boss and fades out after.", "900 × 110", ""),
                 })
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var mark = new Border
            {
                Width = 40, Height = 40, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top,
                Background = new SolidColorBrush(Color.FromArgb(0x26, gold.R, gold.G, gold.B)),
                Child = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 17, Foreground = new SolidColorBrush(gold), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            grid.Children.Add(mark);
            var body = new StackPanel();
            var top = new DockPanel();
            var sizeChip = new Border
            {
                CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 2, 8, 3), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Width × height to give the browser source", Child = Words("Size " + size, "Brush.TextMuted", 11.5),
            };
            sizeChip.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            DockPanel.SetDock(sizeChip, Dock.Right);
            top.Children.Add(sizeChip);
            top.Children.Add(Words(title, "Brush.Text", 15, bold: true));
            body.Children.Add(top);
            TextBlock about = Words(what, "Brush.TextMuted", 12.5);
            about.Margin = new Thickness(0, 3, 0, 10);
            body.Children.Add(about);

            // the link: in a box of the app's look (no white box), the buttons beside it
            var row = new DockPanel();
            var copy = new Button { Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(8, 0, 0, 0), MinHeight = 32 };
            var copyLabel = new StackPanel { Orientation = Orientation.Horizontal };
            var copyIcon = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
            var copyText = new TextBlock { Text = "Copy link", VerticalAlignment = VerticalAlignment.Center };
            copyIcon.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
            copyText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
            copyLabel.Children.Add(copyIcon);
            copyLabel.Children.Add(copyText);
            copy.Content = copyLabel;
            copy.SetResourceReference(Control.BorderBrushProperty, "Brush.Accent");
            var look = new Button { Content = "Preview", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(8, 0, 0, 0), MinHeight = 32, ToolTip = "Opens it in your browser with made-up numbers" };
            DockPanel.SetDock(look, Dock.Right);
            DockPanel.SetDock(copy, Dock.Right);
            row.Children.Add(look);
            row.Children.Add(copy);
            var link = new TextBlock { FontFamily = mono, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var box = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(11, 7, 11, 7), MinHeight = 32, Child = link };
            box.SetResourceReference(Border.BackgroundProperty, "Brush.Window");
            box.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            row.Children.Add(box);
            body.Children.Add(row);
            Grid.SetColumn(body, 1);
            grid.Children.Add(body);
            copy.Click += (_, _) =>
            {
                if (_streamServer?.Url is not string url)
                {
                    return;
                }

                try
                {
                    Clipboard.SetText(url + widget);
                    ShowUploadStatus(title + " link copied: paste it into the browser source's URL.");
                    copyText.Text = "Copied";
                    var back = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    back.Tick += (_, _) =>
                    {
                        back.Stop();
                        copyText.Text = "Copy link";
                    };
                    back.Start();
                }
                catch (COMException)
                {
                    ShowUploadStatus(url + widget);   // the clipboard was busy: the link itself instead
                }
            };
            look.Click += (_, _) =>
            {
                if (_streamServer?.Url is not string url)
                {
                    return;
                }

                try
                {
                    Process.Start(new ProcessStartInfo(url + widget + "?demo=1") { UseShellExecute = true });
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    ShowUploadStatus(url + widget);
                }
            };
            links.Add((link, box, widget, copy, look));
            stack.Children.Add(Panel(grid));
        }

        // ---- how to add one, and the link's options
        var how = new StackPanel();
        how.Children.Add(Words("Adding a widget to OBS or Streamlabs", "Brush.Text", 14, bold: true));
        int n = 0;
        foreach (string step in new[]
                 {
                     "In OBS: Sources → + → Browser (in Streamlabs: + → Browser Source) and give it a name.",
                     "Paste the widget's link as the URL and set the width and height to its size above.",
                     "That's all: the background is see-through and the widget fades in when there is something to show.",
                 })
        {
            n++;
            var line = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var num = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top,
                Background = new SolidColorBrush(Color.FromArgb(0x26, gold.R, gold.G, gold.B)),
                Child = new TextBlock { Text = n.ToString(System.Globalization.CultureInfo.InvariantCulture), FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(gold), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            DockPanel.SetDock(num, Dock.Left);
            line.Children.Add(num);
            TextBlock t = Words(step, "Brush.TextMuted", 12.5);
            t.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(t);
            how.Children.Add(line);
        }

        TextBlock optionsTitle = Words("Add to the end of a link", "Brush.Text", 13, bold: true);
        optionsTitle.Margin = new Thickness(0, 18, 0, 2);
        how.Children.Add(optionsTitle);
        how.Children.Add(Words("For example …/meter?scale=1.2&rows=6 (put & between two of them).", "Brush.TextSubtle", 12));
        var chips = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach ((string code, string meaning) in new[]
                 {
                     ("?scale=1.25", "larger"), ("?rows=5", "fewer lines"), ("?style=solid", "no see-through"), ("?style=clear", "no panel"),
                     ("?always=1", "stays between fights"), ("?demo=1", "made-up numbers"),
                 })
        {
            var chip = new Border { CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), Padding = new Thickness(9, 4, 10, 5), Margin = new Thickness(0, 6, 8, 0) };
            chip.SetResourceReference(Border.BackgroundProperty, "Brush.Window");
            chip.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            var words = new TextBlock { FontSize = 12 };
            words.Inlines.Add(new System.Windows.Documents.Run(code) { FontFamily = mono, Foreground = new SolidColorBrush(gold) });
            var rest = new System.Windows.Documents.Run("  " + meaning);
            rest.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Brush.TextMuted");
            words.Inlines.Add(rest);
            chip.Child = words;
            chips.Children.Add(chip);
        }

        how.Children.Add(chips);
        var renew = new Button { Content = "New links (the old ones stop working)", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 18, 0, 0),
            ToolTip = "For when a link got out, for example shown on stream: every widget gets a new link, the old ones stop at once." };
        renew.Click += (_, _) =>
        {
            MeterSettings current = MeterSettings.Load();
            current.StreamOverlayKey = Streaming.StreamOverlayServer.NewKey();
            current.Save();
            _streamSettingsAt = DateTime.MinValue;   // the server starts again with the new key
            StreamTick();
        };
        how.Children.Add(renew);
        stack.Children.Add(Panel(how));

        Brush Fill(Color c) => new SolidColorBrush(Color.FromArgb(0x2E, c.R, c.G, c.B));

        void Show()
        {
            MeterSettings s = MeterSettings.Load();
            bool wanted = s.StreamOverlayOn;
            bool on = _streamServer is { Running: true };
            Color colour = on ? Color.FromRgb(0x6C, 0xD4, 0x8A) : wanted && _streamFailed ? Color.FromRgb(0xEF, 0x6B, 0x6B) : Color.FromRgb(0x8A, 0x94, 0xA0);
            dot.Background = Fill(colour);
            dotIcon.Foreground = new SolidColorBrush(colour);
            dotIcon.Text = on ? "" : wanted && _streamFailed ? "" : "";
            if (on)
            {
                int count = _streamServer!.ClientCount;
                status.Text = "The widgets are running";
                where.Text = (_streamServer.Network ? "On this PC and your home network" : "On this PC") + "  ·  "
                    + (count == 0 ? "no browser source connected yet" : count == 1 ? "1 browser source connected" : count + " browser sources connected");
            }
            else if (wanted && _streamFailed)
            {
                status.Text = "The widgets couldn't start";
                where.Text = $"No free port from {Math.Clamp(s.StreamOverlayPort, 1024, 65000)} on: another program uses them. Choose another port in Configuration, or turn the widgets off and on again.";
            }
            else if (wanted)
            {
                status.Text = "Starting …";
                where.Text = "A moment.";
            }
            else
            {
                status.Text = "The widgets are off";
                where.Text = "Turn them on to make the links below work. They run while the app is open.";
            }

            turnOn.Visibility = wanted ? Visibility.Collapsed : Visibility.Visible;
            names.Box.IsChecked = s.StreamShowNames;
            network.Box.IsChecked = s.StreamOverlayNetwork;
            secondPc.Text = on && _streamServer!.NetworkUrl is string far ? "On the second PC: " + far + "meter (card, boss the same)" : "";
            secondPc.Visibility = secondPc.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach ((TextBlock link, Border box, string widget, Button copy, Button look) in links)
            {
                if (_streamServer?.Url is string url)
                {
                    link.Text = url + widget;
                    link.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
                    link.FontStyle = FontStyles.Normal;
                    link.FontFamily = mono;
                }
                else
                {
                    link.Text = wanted ? "The link shows once the widgets run" : "Turn the widgets on to get this link";
                    link.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
                    link.FontStyle = FontStyles.Italic;
                    link.ClearValue(TextBlock.FontFamilyProperty);   // the app's own face for words
                }

                copy.IsEnabled = look.IsEnabled = on;
                box.Opacity = on ? 1 : 0.75;
            }
        }

        Show();
        var page = new ModulePage(loc["Settings.Nav.Stream"], stack, s => s.StreamOverlayOn, (s, v) => s.StreamOverlayOn = v, null, hotkeys, settingsPage, applied, Show, masterLabel: "Widgets");
        return new ToolPage { Content = page, Cleanup = page.Stop };
    }
}
