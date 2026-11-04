using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>
/// The Interactive map page is the one place to choose what the map tracks. Under the pet route (MainWindow.PetRoute.cs) it picks the gathering
/// materials and holds the Hidden Cube switches: the very settings the Gathering and Cube finder pages change (PetMapGatherItems, ShowMapOnCube), which
/// stay as they are. A click changes only its own entry in the settings file, and the page's timer brings the cards back in line with the file, so a
/// pick made on one of the other pages shows here too and none of the three pages undoes what another one picked.
/// </summary>
public partial class MainWindow
{
    /// <summary>The widths of the two cards side by side under the route card; the route card is as wide as both together, so their edges line up.</summary>
    private const double MapGatherCardWidth = 470, MapCubesCardWidth = 290;

    /// <summary>The Hidden Cube among the collectibles (its English name is its key): its own card switches it, the gathering list leaves it out.</summary>
    private const string HiddenCubeKey = "Hidden Cube";

    /// <summary>One kind of material in the gathering card: its line, how many of it are on, the link that switches them all, and its check boxes.</summary>
    private sealed record MapGatherKind(FrameworkElement Line, TextBlock Count, TextBlock All, List<(CheckBox Box, string Key, string Words)> Items);

    /// <summary>
    /// The gathering card: a search box and what the player's faction can gather, by kind (the list of the Gathering page, without the Hidden Cube,
    /// which has its own card), each kind with how many of its materials are on and a link that switches them all. Also returns what brings the
    /// check boxes back in line with the settings (the page's timer calls it).
    /// </summary>
    private (Border Card, Action<MeterSettings> Sync) MapGatherCard()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        string? faction = Aion2Gather.OwnFaction();
        HashSet<string> start = MeterSettings.Load().PetMapGatherItems.ToHashSet();
        var stack = new StackPanel();
        stack.Children.Add(ShellText(loc["Settings.Nav.Gather"], "Brush.Text", 14, bold: true));
        TextBlock lede = Wrapped(loc["Settings.Gather.Hint"], "Brush.TextMuted", 12);
        lede.Margin = new Thickness(0, 2, 0, 8);
        stack.Children.Add(lede);
        if (faction is null)
        {
            // until the faction is known both sides' plants are listed, as on the Gathering page
            TextBlock note = Wrapped(loc["Settings.Gather.FactionUnknown"], "Brush.TextSubtle", 11.5);
            note.Margin = new Thickness(0, 0, 0, 8);
            stack.Children.Add(note);
        }

        // the search box, with its hint while it is empty (as the crafting calculator's)
        var search = new TextBox { Padding = new Thickness(6, 3, 6, 3), VerticalContentAlignment = VerticalAlignment.Center };
        search.SetResourceReference(Control.BackgroundProperty, "Brush.Control");
        search.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        search.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");
        search.SetResourceReference(TextBox.CaretBrushProperty, "Brush.Text");
        TextBlock hint = ShellText(loc["Settings.PetMap.Search"], "Brush.TextMuted", 12.5);
        hint.IsHitTestVisible = false;
        hint.Margin = new Thickness(9, 0, 0, 0);
        var searchHost = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        searchHost.Children.Add(search);
        searchHost.Children.Add(hint);
        stack.Children.Add(searchHost);

        void Save(List<string> keys, bool on)
        {
            // only these entries change: what the Gathering page or the Cube finder put in the list meanwhile stays
            MeterSettings s = MeterSettings.Load();
            s.PetMapGatherItems.RemoveAll(k => keys.Contains(k));
            if (on)
            {
                s.PetMapGatherItems.AddRange(keys);
            }

            s.Save();
            _petMapController?.SettingsChanged(); // the map draws the new picks at once, not at its next read of the settings
        }

        var kinds = new List<MapGatherKind>();
        var lines = new StackPanel();
        foreach (string kind in Aion2Gather.Kinds)
        {
            if (kind == "Cube")
            {
                continue; // the Hidden Cubes have their own card beside this one
            }

            // what the Gathering page lists: what the world maps hold a place for, and only what the own faction can use
            var items = Aion2Gather.Items().Where(i => i.Kind == kind && i.Count > 0 && i.IsFor(faction)).ToList();
            if (items.Count == 0)
            {
                continue;
            }

            string kindName = loc["Settings.PetMap.Gather." + kind];
            var line = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // left: the kind with its diamond as the map draws it, how many are on, and "All on / All off"
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(new System.Windows.Shapes.Polygon
            {
                Points = new PointCollection { new Point(5, 0), new Point(10, 6), new Point(5, 12), new Point(0, 6) },
                Fill = new SolidColorBrush(PetMapPalette.OfGather(kind)), Stroke = Brushes.Black, StrokeThickness = 1,
                Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Width = 10, Height = 12,
            });
            TextBlock name = ShellText(kindName, "Brush.Text", 12.5, bold: true);
            name.TextWrapping = TextWrapping.Wrap;
            name.MaxWidth = 92;
            head.Children.Add(name);
            TextBlock count = ShellText("", "Brush.TextSubtle", 11);
            TextBlock all = ShellText("", "Brush.Accent", 11, bold: true);
            all.Margin = new Thickness(8, 0, 0, 0);
            all.Cursor = Cursors.Hand;
            var tally = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 2, 0, 0) };
            tally.Children.Add(count);
            tally.Children.Add(all);
            var left = new StackPanel();
            left.Children.Add(head);
            left.Children.Add(tally);

            // right: one check box per material, its places on the world maps as its tip
            var wrap = new WrapPanel();
            Grid.SetColumn(wrap, 1);
            var boxes = new List<(CheckBox Box, string Key, string Words)>();
            foreach (Aion2GatherItem item in items)
            {
                string key = item.Key;
                string shown = item.NameIn(loc.Language);
                int places = faction == "Elyos" ? item.Elyos : faction == "Asmodian" ? item.Asmodian : item.Count;
                var box = new CheckBox
                {
                    IsChecked = start.Contains(key), Content = ShellText(shown, "Brush.Text", 12.5), Margin = new Thickness(0, 1, 14, 5),
                    VerticalAlignment = VerticalAlignment.Center, ToolTip = string.Format(CultureInfo.CurrentCulture, loc["Settings.Gather.Places"], places),
                };
                box.Click += (_, _) =>
                {
                    Save(new List<string> { key }, box.IsChecked == true);
                    RefreshCounts();
                };
                wrap.Children.Add(box);
                // the search finds a material by its name, its English name or its kind
                boxes.Add((box, key, shown + " " + key + " " + kindName));
            }

            var entry = new MapGatherKind(line, count, all, boxes);
            all.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                // all of the kind that the search shows: on while one of them is off, else off
                var listed = entry.Items.Where(i => i.Box.Visibility == Visibility.Visible).ToList();
                bool turnOn = listed.Any(i => i.Box.IsChecked != true);
                Save(listed.Select(i => i.Key).ToList(), turnOn);
                foreach (var listedItem in listed)
                {
                    listedItem.Box.IsChecked = turnOn;
                }

                RefreshCounts();
            };
            line.Children.Add(left);
            line.Children.Add(wrap);
            lines.Children.Add(line);
            kinds.Add(entry);
        }

        stack.Children.Add(lines);
        TextBlock noMatch = Wrapped(loc["PetMap.GatherNoMatch"], "Brush.TextMuted", 12);
        noMatch.Margin = new Thickness(0, 6, 0, 6);
        noMatch.Visibility = Visibility.Collapsed;
        stack.Children.Add(noMatch);
        TextBlock more = ShellLink(string.Format(CultureInfo.CurrentCulture, loc["PetMap.MoreOn"], loc["Settings.Nav.Gather"]), () => OpenTool("petmap.gather"));
        more.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(more);

        void RefreshCounts()
        {
            foreach (MapGatherKind row in kinds)
            {
                int on = row.Items.Count(i => i.Box.IsChecked == true);
                row.Count.Text = $"{on} / {row.Items.Count}";
                bool anyOff = row.Items.Any(i => i.Box.Visibility == Visibility.Visible && i.Box.IsChecked != true);
                row.All.Text = anyOff ? loc["Settings.PetMap.AllOn"] : loc["Settings.PetMap.AllOff"];
            }
        }

        void Filter()
        {
            string text = search.Text.Trim();
            bool anyLine = false;
            foreach (MapGatherKind row in kinds)
            {
                bool lineShown = false;
                foreach (var (b, _, words) in row.Items)
                {
                    bool match = text.Length == 0 || words.Contains(text, StringComparison.CurrentCultureIgnoreCase);
                    b.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                    lineShown |= match;
                }

                row.Line.Visibility = lineShown ? Visibility.Visible : Visibility.Collapsed;
                anyLine |= lineShown;
            }

            noMatch.Visibility = anyLine ? Visibility.Collapsed : Visibility.Visible;
            RefreshCounts();
        }

        search.TextChanged += (_, _) =>
        {
            hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Filter();
        };

        void Sync(MeterSettings s)
        {
            HashSet<string> now = s.PetMapGatherItems.ToHashSet();
            foreach (MapGatherKind row in kinds)
            {
                foreach (var (b, k, _) in row.Items)
                {
                    bool on = now.Contains(k);
                    if (b.IsChecked != on)
                    {
                        b.IsChecked = on; // set by the code: no Click, so nothing is saved back
                    }
                }
            }

            RefreshCounts();
        }

        Filter();
        Border card = HomeCard(null, stack, null, MapGatherCardWidth);
        card.HorizontalAlignment = HorizontalAlignment.Left;
        return (card, Sync);
    }

    /// <summary>
    /// The Hidden Cubes card: the Cube finder's two map switches (mark the cubes on the map, bring the map up when one comes into range) and a link
    /// to its other switches (the sound, the line on the meter). Also returns what brings the switches back in line with the settings.
    /// </summary>
    private (Border Card, Action<MeterSettings> Sync) MapCubesCard()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        MeterSettings s0 = MeterSettings.Load();
        var stack = new StackPanel();
        stack.Children.Add(ShellText(loc["Settings.PetMap.Gather.Cube"], "Brush.Text", 14, bold: true));
        TextBlock lede = Wrapped(loc["Cubes.Note"], "Brush.TextMuted", 12);
        lede.Margin = new Thickness(0, 2, 0, 10);
        stack.Children.Add(lede);
        // the check box gives its text all the room it asks for (see the Cube finder): the text wraps only below this width (the card's padding and the box taken off)
        double textWidth = MapCubesCardWidth - 34 - 28;

        void Save(Action<MeterSettings> change)
        {
            MeterSettings s = MeterSettings.Load();
            change(s);
            s.Save();
            ApplySettingsEverywhere(s); // as the Cube finder does: a map brought up for a cube goes back to what the switches say
            _petMapController?.SettingsChanged(); // the map shows or drops the cubes at once, not at its next read of the settings
        }

        var switches = new List<(CheckBox Box, Func<MeterSettings, bool> Get)>();
        void Switch(string title, string tip, Func<MeterSettings, bool> get, Action<MeterSettings, bool> set)
        {
            var text = new StackPanel();
            TextBlock head = ShellText(title, "Brush.Text", 13, bold: true);
            head.TextWrapping = TextWrapping.Wrap;
            head.MaxWidth = textWidth;
            text.Children.Add(head);
            TextBlock small = Wrapped(tip, "Brush.TextMuted", 11.5);
            small.Margin = new Thickness(0, 2, 0, 0);
            small.MaxWidth = textWidth;
            text.Children.Add(small);
            var box = new CheckBox { IsChecked = get(s0), Content = text, Margin = new Thickness(0, 0, 0, 10), VerticalContentAlignment = VerticalAlignment.Top };
            box.Click += (_, _) => Save(s => set(s, box.IsChecked == true));
            stack.Children.Add(box);
            switches.Add((box, get));
        }

        Switch(loc["Cubes.Mark"], loc["Cubes.MarkTip"], s => s.PetMapGatherItems.Contains(HiddenCubeKey), (s, v) =>
        {
            s.PetMapGatherItems.RemoveAll(k => k == HiddenCubeKey);
            if (v)
            {
                s.PetMapGatherItems.Add(HiddenCubeKey);
            }
        });
        Switch(loc["Settings.Gather.ShowMapOnCube"], loc["Cubes.MapTip"], s => s.ShowMapOnCube, (s, v) => s.ShowMapOnCube = v);
        TextBlock more = ShellLink(string.Format(CultureInfo.CurrentCulture, loc["PetMap.MoreOn"], loc["Cubes.Title"]), () => OpenTool("cubes"));
        more.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(more);

        void Sync(MeterSettings s)
        {
            foreach (var (b, get) in switches)
            {
                bool on = get(s);
                if (b.IsChecked != on)
                {
                    b.IsChecked = on; // set by the code: no Click, so nothing is saved back
                }
            }
        }

        Border card = HomeCard(null, stack, null, MapCubesCardWidth);
        card.HorizontalAlignment = HorizontalAlignment.Left;
        return (card, Sync);
    }
}
