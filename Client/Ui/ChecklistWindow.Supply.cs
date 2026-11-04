using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AionDPS.Aion2;

namespace AionDPS.Ui;

/// <summary>
/// The checklist's supply requests (the game's "Supply Request" window, <see cref="Aion2Supply"/>) and the inventory of all characters
/// (<see cref="Aion2Inventories"/>): which requests are delivered, which can be now, and which could be with the materials of the other characters on the
/// same server, moved through the Server Storage. Bound items count only on the character that holds them.
/// </summary>
public sealed partial class ChecklistWindow
{
    /// <summary>The supply card's tab: 1 Emergency, 2 Weekly, 3 Regular: Craft, 4 Regular: Boss.</summary>
    private int _supplyTab = 1;

    /// <summary>Whether the supply card shows the delivered requests (folded away by default).</summary>
    private bool _supplyShowDone;

    /// <summary>The inventory card's search text (kept while the page is drawn again).</summary>
    private string _inventorySearch = "";

    /// <summary>What a character can put towards a request: what it holds itself (bag and character storage, bound or not), the server storage, and the
    /// other characters on that server (unbound only), each with its count.</summary>
    private readonly record struct SupplyHave(long Own, long Server, IReadOnlyList<HoldingPlace> Others)
    {
        public long Total => Own + Server + Others.Sum(o => o.Count);
    }

    private static SupplyHave HaveFor(string name, SupplyRequest request)
    {
        long own = 0, server = 0;
        bool live = name == Aion2Tickets.CurrentName && Aion2Stock.HasData;
        CharacterHoldings? saved = live ? null : Aion2Inventories.Characters().FirstOrDefault(c => c.Name == name);
        AccountHoldings? storage = live ? null : Aion2Inventories.AccountOf(name);
        var others = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (int item in request.Items.Distinct())
        {
            if (live)
            {
                own += Aion2Stock.Count(item, StockSources.Inventory | StockSources.CharacterStorage);
                server += Aion2Stock.Count(item, StockSources.AccountStorage);
            }
            else
            {
                own += (saved?.Bag.GetValueOrDefault(item) ?? 0) + (saved?.Storage.GetValueOrDefault(item) ?? 0);
                server += storage?.Items.GetValueOrDefault(item) ?? 0;
            }

            foreach (HoldingPlace place in Aion2Inventories.OnOthers(name, item))
            {
                others[place.Character!] = others.GetValueOrDefault(place.Character!) + place.Count;
            }
        }

        return new SupplyHave(own, server, others.OrderByDescending(o => o.Value).Select(o => new HoldingPlace(o.Key, o.Value)).ToList());
    }

    private void AddSupply(TicketCharacter character, string language)
    {
        if (Aion2Supply.Requests.Count == 0)
        {
            return;
        }

        LocalizationManager loc = LocalizationManager.Instance;
        DateTime utcNow = DateTime.UtcNow;
        SupplyCharacter? state = Aion2Supply.Of(character.Name);
        var stack = new StackPanel();
        var head = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 8) };
        // when the Emergency and the Weekly lists start again: the game's end times of the lists, else its reset times
        long EndOf(int tab, bool daily) => state?.Groups.FirstOrDefault(g => g.Tab == tab && !g.Over(utcNow))?.EndMs ?? Aion2Supply.NextReset(utcNow, daily);
        string ResetText(int tab, bool daily) => string.Format(CultureInfo.CurrentCulture, loc["Checklist.SupplyResetIn"], Aion2Supply.TabName(tab, language),
            LeftText(DateTimeOffset.FromUnixTimeMilliseconds(EndOf(tab, daily)).UtcDateTime - utcNow));
        TextBlock resets = Label("↻  " + ResetText(1, true) + "  ·  " + ResetText(2, false), "Brush.TextMuted", 11.5);
        DockPanel.SetDock(resets, Dock.Right);
        head.Children.Add(resets);
        head.Children.Add(Label(loc["Checklist.Supply"], "Brush.Text", 16, bold: true));
        stack.Children.Add(head);

        if (state is null || state.Groups.Count == 0)
        {
            TextBlock none = Label(loc["Checklist.SupplyNone"], "Brush.TextMuted", 12);
            none.TextWrapping = TextWrapping.Wrap;
            stack.Children.Add(none);
            _body.Children.Add(Card(stack));
            return;
        }

        // the four tabs with delivered / all (a list that is over counts as nothing known)
        if (!state.Groups.Any(g => g.Tab == _supplyTab))
        {
            _supplyTab = state.Groups[0].Tab;
        }

        var segments = new Border { CornerRadius = new CornerRadius(15), Padding = new Thickness(3), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10) };
        segments.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
        segments.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        var tabs = new WrapPanel();
        foreach (SupplyGroup g in state.Groups)
        {
            int tab = g.Tab;
            bool over = g.Over(utcNow);
            int delivered = over ? 0 : g.Requests.Count(e => Aion2Supply.Find(e.Id) is { } r && e.Progress >= r.Target);
            tabs.Children.Add(SupplySegment(Aion2Supply.TabName(tab, language), over ? "–" : $"{delivered}/{g.Requests.Count}", tab == _supplyTab, () => _supplyTab = tab));
        }

        segments.Child = tabs;
        stack.Children.Add(segments);
        stack.Children.Add(SupplyTip());

        SupplyGroup group = state.Groups.First(g => g.Tab == _supplyTab);
        if (group.Over(utcNow))
        {
            TextBlock over = Label(string.Format(CultureInfo.CurrentCulture, loc["Checklist.SupplyOver"],
                LeftText(utcNow - DateTimeOffset.FromUnixTimeMilliseconds(group.EndMs).UtcDateTime)), "Brush.Accent", 12);
            over.TextWrapping = TextWrapping.Wrap;
            over.Margin = new Thickness(0, 4, 0, 4);
            stack.Children.Add(over);
            _body.Children.Add(Card(stack));
            return;
        }

        var rows = group.Requests.Select(e => (Entry: e, Request: Aion2Supply.Find(e.Id))).Where(x => x.Request is not null)
            .Select(x => (x.Entry, Request: x.Request!, Have: HaveFor(character.Name, x.Request!))).ToList();
        // the ones that can be delivered now first, then those the other characters can complete, then by how far along
        var open = rows.Where(x => x.Entry.Progress < x.Request.Target)
            .OrderByDescending(x => x.Have.Own >= x.Request.Target - x.Entry.Progress)
            .ThenByDescending(x => x.Have.Total >= x.Request.Target - x.Entry.Progress)
            .ThenByDescending(x => (double)x.Entry.Progress / Math.Max(1, x.Request.Target)).ThenBy(x => x.Request.Id).ToList();
        var done = rows.Where(x => x.Entry.Progress >= x.Request.Target).ToList();
        foreach (var x in open)
        {
            stack.Children.Add(SupplyRow(x.Entry, x.Request, x.Have, language));
        }

        if (open.Count == 0)
        {
            TextBlock all = Label("✓  " + loc["Checklist.SupplyAllDone"], "Brush.Heal", 12.5, bold: true);
            all.Margin = new Thickness(0, 2, 0, 6);
            stack.Children.Add(all);
        }

        if (done.Count > 0)
        {
            var fold = new TextBlock { Text = (_supplyShowDone ? "▾  " : "▸  ") + string.Format(CultureInfo.CurrentCulture, loc["Checklist.SupplyDoneN"], done.Count), FontSize = 12.5,
                FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 6), Cursor = Cursors.Hand };
            fold.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            fold.MouseLeftButtonDown += (_, e) =>
            {
                _supplyShowDone = !_supplyShowDone;
                e.Handled = true;
                Render();
            };
            stack.Children.Add(fold);
            if (_supplyShowDone)
            {
                foreach (var x in done)
                {
                    stack.Children.Add(SupplyRow(x.Entry, x.Request, x.Have, language));
                }
            }
        }

        _body.Children.Add(Card(stack));
    }

    /// <summary>One tab of the supply card: its name and delivered / all.</summary>
    private UIElement SupplySegment(string caption, string count, bool active, Action choose)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(Label(caption, active ? "Brush.Text" : "Brush.TextMuted", 12.5, bold: true));
        TextBlock n = Label(count, active ? "Brush.Accent" : "Brush.TextSubtle", 11.5, bold: true);
        n.Margin = new Thickness(8, 0, 0, 0);
        line.Children.Add(n);
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

    /// <summary>The hint above the requests: unbound materials go from one character to another through the Server Storage.</summary>
    private static UIElement SupplyTip()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var row = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 10) };
        TextBlock glyph = Label("", "Brush.Accent", 13); // the "switch" arrows
        glyph.FontFamily = new FontFamily("Segoe MDL2 Assets");
        glyph.Margin = new Thickness(0, 1, 10, 0);
        glyph.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(glyph, Dock.Left);
        row.Children.Add(glyph);
        TextBlock text = Label(loc["Checklist.SupplyTip"], "Brush.Text", 12);
        text.TextWrapping = TextWrapping.Wrap;
        row.Children.Add(text);
        var box = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 10), Child = row };
        box.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
        box.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return box;
    }

    /// <summary>
    /// One request: the item's icon, its name with what it gives, the progress, and below it what can be done: ready to deliver, enough with the other
    /// characters (who holds how many, the server storage), or how many are still missing.
    /// </summary>
    private static UIElement SupplyRow(SupplyEntry entry, SupplyRequest request, SupplyHave have, string language)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        bool delivered = entry.Progress >= request.Target;
        long left = Math.Max(0, request.Target - entry.Progress);
        int item = request.Items.Count > 0 ? request.Items[0] : 0;
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8), Opacity = delivered ? 0.7 : 1 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Border icon = CraftText.IconTile(item, 34);
        icon.Margin = new Thickness(0, 0, 10, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.ToolTip = string.Format(CultureInfo.CurrentCulture, loc["Checklist.SupplyAccepts"], string.Join(", ", request.Items.Distinct().Select(CraftText.ItemName)));
        grid.Children.Add(icon);

        var middle = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        TextBlock name = Label(Aion2Supply.Text(request.Names, language), delivered ? "Brush.TextMuted" : "Brush.Text", 13.5, bold: true);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.ToolTip = icon.ToolTip;
        title.Children.Add(name);
        // what it gives: the item's icon and the count, or the count and the game's name when there is no icon (Abyss Points, Enhance Stones)
        for (int i = 0; i < request.Rewards.Count; i++)
        {
            (int reward, int count) = request.Rewards[i];
            string amount = count.ToString("N0", CultureInfo.CurrentCulture);
            if (CraftText.Icon(reward) is null)
            {
                TextBlock plain = Label((i == 0 ? "" : "·  ") + amount + " " + CraftText.ItemName(reward), "Brush.TextSubtle", 11.5);
                plain.Margin = new Thickness(i == 0 ? 12 : 6, 0, 0, 0);
                title.Children.Add(plain);
                continue;
            }

            Border r = CraftText.IconTile(reward, 16);
            r.Margin = new Thickness(12, 0, 4, 0);
            r.ToolTip = CraftText.ItemName(reward);
            title.Children.Add(r);
            TextBlock n = Label(amount, "Brush.TextSubtle", 11.5);
            n.ToolTip = r.ToolTip;
            title.Children.Add(n);
        }

        middle.Children.Add(title);
        middle.Children.Add(SupplyStatus(request, have, left, delivered));
        Grid.SetColumn(middle, 1);
        grid.Children.Add(middle);

        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), MinWidth = 92 };
        TextBlock progress = Label((delivered ? "✓  " : "") + entry.Progress.ToString("N0", CultureInfo.CurrentCulture) + " / " + request.Target.ToString("N0", CultureInfo.CurrentCulture),
            delivered ? "Brush.Heal" : entry.Progress > 0 ? "Brush.Accent" : "Brush.TextSubtle", 12.5, bold: true);
        progress.HorizontalAlignment = HorizontalAlignment.Right;
        right.Children.Add(progress);
        FrameworkElement bar = SeasonLook.Bar(Math.Clamp((double)entry.Progress / Math.Max(1, request.Target), 0, 1), delivered ? SeasonLook.Done : SeasonLook.Gold, 5);
        bar.Margin = new Thickness(0, 5, 0, 0);
        right.Children.Add(bar);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        return grid;
    }

    /// <summary>The line under a request's name: what the character and its other characters hold of it.</summary>
    private static UIElement SupplyStatus(SupplyRequest request, SupplyHave have, long left, bool delivered)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var line = new WrapPanel { Margin = new Thickness(0, 3, 0, 0) };
        void Text(string text, string brush, bool bold = false)
        {
            TextBlock t = Label(text, brush, 11.5, bold);
            t.Margin = new Thickness(0, 0, 8, 2);
            line.Children.Add(t);
        }

        void Chip(string text, string brush)
        {
            var chip = new Border { CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Padding = new Thickness(7, 0, 7, 1), Margin = new Thickness(0, 0, 6, 2) };
            chip.SetResourceReference(Border.BorderBrushProperty, brush);
            TextBlock t = Label(text, brush, 11);
            chip.Child = t;
            line.Children.Add(chip);
        }

        if (delivered)
        {
            Text(loc["Checklist.SupplyDelivered"], "Brush.Heal");
            return line;
        }

        // a currency (Enhance Stones, Kinah): what the game counts, not an item in a bag
        if (request.Items.All(Aion2Inventories.IsCurrency))
        {
            Text(string.Format(CultureInfo.CurrentCulture, loc["Checklist.SupplyMissing"], left.ToString("N0", CultureInfo.CurrentCulture)), "Brush.TextSubtle");
            return line;
        }

        string N(long n) => n.ToString("N0", CultureInfo.CurrentCulture);
        if (have.Own >= left)
        {
            Text("✓  " + string.Format(CultureInfo.CurrentCulture, loc["Checklist.SupplyReady"], N(have.Own)), "Brush.Heal", bold: true);
            return line;
        }

        if (have.Total >= left)
        {
            Text(string.Format(CultureInfo.CurrentCulture, loc["Checklist.SupplyWithOthers"], N(left - have.Own)), "Brush.Accent", bold: true);
        }
        else
        {
            Text(string.Format(CultureInfo.CurrentCulture, loc["Checklist.SupplyMissing"], N(left - have.Total)), "Brush.TextSubtle");
        }

        if (have.Own > 0)
        {
            Chip(string.Format(CultureInfo.CurrentCulture, loc["Checklist.SupplyOnYou"], N(have.Own)), "Brush.TextMuted");
        }

        if (have.Server > 0)
        {
            Chip(string.Format(CultureInfo.CurrentCulture, loc["Checklist.SupplyInServer"], N(have.Server)), "Brush.Accent");
        }

        foreach (HoldingPlace place in have.Others.Take(3))
        {
            Chip(string.Format(CultureInfo.CurrentCulture, loc["Craft.OnCharacter"], N(place.Count), place.Character), "Brush.Heal");
        }

        if (have.Others.Count > 3)
        {
            Chip("+" + (have.Others.Count - 3), "Brush.Heal");
        }

        return line;
    }

    // ---- the inventory of all characters ----

    /// <summary>The inventory card of the character's server: what it and its other characters there hold unbound, and the server storage.</summary>
    private void AddInventory(string name)
    {
        if (Aion2Inventories.Characters().Any(c => c.Name == name))
        {
            _body.Children.Add(InventoryCard(name));
        }
    }

    /// <summary>On the roster page: one inventory card per server (the characters of one account that share a server storage).</summary>
    private void AddRosterInventories()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (CharacterHoldings c in Aion2Inventories.Characters())
        {
            if (seen.Contains(c.Name))
            {
                continue;
            }

            seen.Add(c.Name);
            foreach (CharacterHoldings mate in Aion2Inventories.Mates(c.Name))
            {
                seen.Add(mate.Name);
            }

            _body.Children.Add(InventoryCard(c.Name));
        }
    }

    private UIElement InventoryCard(string anchor)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        CharacterHoldings me = Aion2Inventories.Characters().First(c => c.Name == anchor);
        IReadOnlyList<CharacterHoldings> mates = Aion2Inventories.Mates(anchor);
        var stack = new StackPanel();
        var head = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 4) };
        string server = me.ServerId > 0 ? Aion2.Protocol.Aion2Servers.NameOf(me.ServerId).Replace("Europe - ", "", StringComparison.Ordinal) : "";
        var search = new TextBox { Text = _inventorySearch, Width = 220, Padding = new Thickness(8, 3, 8, 3), BorderThickness = new Thickness(1), VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = loc["Checklist.InventorySearch"] };
        search.SetResourceReference(Control.BackgroundProperty, "Brush.Control");
        search.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        search.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");
        search.SetResourceReference(TextBox.CaretBrushProperty, "Brush.Text");
        var hint = Label(loc["Checklist.InventorySearch"], "Brush.TextSubtle", 12);
        hint.IsHitTestVisible = false;
        hint.Margin = new Thickness(10, 0, 0, 0);
        hint.Visibility = _inventorySearch.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var searchBox = new Grid { VerticalAlignment = VerticalAlignment.Center };
        searchBox.Children.Add(search);
        searchBox.Children.Add(hint);
        DockPanel.SetDock(searchBox, Dock.Right);
        head.Children.Add(searchBox);
        head.Children.Add(Label(server.Length > 0 ? string.Format(CultureInfo.CurrentCulture, loc["Checklist.InventoryOn"], server) : loc["Checklist.Inventory"], "Brush.Text", 16, bold: true));
        stack.Children.Add(head);

        string who = string.Join(", ", new[] { me }.Concat(mates).Select(c => c.Name));
        TextBlock sub = Label(string.Format(CultureInfo.CurrentCulture, loc["Checklist.InventoryWho"], who), "Brush.TextMuted", 11.5);
        sub.TextWrapping = TextWrapping.Wrap;
        sub.Margin = new Thickness(0, 0, 0, 8);
        stack.Children.Add(sub);

        var list = new StackPanel();
        stack.Children.Add(list);
        IReadOnlyList<(int Item, long Total, IReadOnlyList<HoldingPlace> Places)> all = Aion2Inventories.Combined(anchor);
        void Fill()
        {
            list.Children.Clear();
            string q = _inventorySearch.Trim();
            var shown = all.Where(x => q.Length == 0 || CraftText.ItemName(x.Item).Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
            int limit = q.Length == 0 ? 30 : 120;
            if (all.Count == 0 || shown.Count == 0)
            {
                list.Children.Add(Label(all.Count == 0 ? loc["Checklist.InventoryNone"] : loc["Checklist.InventoryNoMatch"], "Brush.TextMuted", 12));
                return;
            }

            foreach (var x in shown.Take(limit))
            {
                list.Children.Add(InventoryRow(x.Item, x.Total, x.Places));
            }

            if (shown.Count > limit)
            {
                TextBlock more = Label(string.Format(CultureInfo.CurrentCulture, loc["Checklist.InventoryMore"], shown.Count - limit), "Brush.TextSubtle", 11.5);
                more.Margin = new Thickness(0, 4, 0, 0);
                list.Children.Add(more);
            }
        }

        // typing filters this list only (the page isn't drawn again, so the box keeps the focus)
        search.TextChanged += (_, _) =>
        {
            _inventorySearch = search.Text;
            hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Fill();
        };
        Fill();

        TextBlock note = Label(loc["Checklist.InventoryNote"], "Brush.TextSubtle", 11);
        note.TextWrapping = TextWrapping.Wrap;
        note.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(note);
        return Card(stack);
    }

    /// <summary>One item of the inventory card: icon, name, the total, and where it lies (each character, the server storage).</summary>
    private static UIElement InventoryRow(int item, long total, IReadOnlyList<HoldingPlace> places)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var row = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 4) };
        Border icon = CraftText.IconTile(item, 24);
        icon.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        TextBlock count = Label(total.ToString("N0", CultureInfo.CurrentCulture), "Brush.Text", 12.5, bold: true);
        count.MinWidth = 64;
        count.TextAlignment = TextAlignment.Right;
        DockPanel.SetDock(count, Dock.Right);
        row.Children.Add(count);
        var where = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (HoldingPlace place in places)
        {
            var chip = new Border { CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Padding = new Thickness(7, 0, 7, 1), Margin = new Thickness(6, 1, 0, 1) };
            string brush = place.Character is null ? "Brush.Accent" : "Brush.Border";
            chip.SetResourceReference(Border.BorderBrushProperty, brush);
            string text = place.Character is null
                ? string.Format(CultureInfo.CurrentCulture, loc["Checklist.SupplyInServer"], place.Count.ToString("N0", CultureInfo.CurrentCulture))
                : string.Format(CultureInfo.CurrentCulture, loc["Craft.OnCharacter"], place.Count.ToString("N0", CultureInfo.CurrentCulture), place.Character);
            chip.Child = Label(text, place.Character is null ? "Brush.Accent" : "Brush.TextMuted", 11);
            where.Children.Add(chip);
        }

        DockPanel.SetDock(where, Dock.Right);
        row.Children.Add(where);
        var name = new TextBlock { Text = CraftText.ItemName(item), Foreground = new SolidColorBrush(CraftText.GradeColor(item)), VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12.5 };
        row.Children.Add(name);
        return row;
    }
}
