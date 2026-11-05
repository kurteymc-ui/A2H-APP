using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AionDPS.Upload;

namespace AionDPS.Ui;

/// <summary>
/// Home's help (5 November 2026, the owner: "a small tooltip of what they can use, like a slider, and prompt to edit cooldowns, buffs and other stuff they
/// might need"): a line of tips at the bottom of the greeting, one after the other every 12 seconds (with the player's own hotkeys, and a way to the page
/// it is about), and a "Get set up" card with what is worth setting up first and is not yet (cooldowns, buffs, world boss alerts, the map, season
/// missions, signing in), each with a way there and a cross for "not for me"; the card goes once everything is done or put away.
/// </summary>
public partial class MainWindow
{
    private int _tipAt = -1;
    private DateTime _tipMoved = DateTime.Now;
    private bool _tipHeld;
    private Action<int>? _tipShow;
    private static readonly TimeSpan TipEvery = TimeSpan.FromSeconds(12);

    private sealed record HomeTip(string Text, string? Page);

    /// <summary>The tips, the hotkeys as the player set them (a tip of a hotkey switched off is left out), the pages there are in this mode.</summary>
    private List<HomeTip> HomeTips()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        MeterSettings s = MeterSettings.Load();
        Dictionary<string, ShellItem> pages = ShellPages();
        var tips = new List<HomeTip>();
        void Key(string? hotkey, string text, string? page)
        {
            HotkeyBinding binding = HotkeyBinding.Parse(hotkey);
            if (!binding.IsEmpty)
            {
                tips.Add(new HomeTip(string.Format(CultureInfo.CurrentCulture, loc[text], binding.ToString()), page));
            }
        }

        void Page(string text, string page)
        {
            if (pages.ContainsKey(page))
            {
                tips.Add(new HomeTip(loc[text], page));
            }
        }

        Key(s.HotkeyMoveOverlays, "Home.Tip.Move", "overlays");
        Page("Home.Tip.History", "history");
        Key(s.HotkeyCopyDamage, "Home.Tip.Copy", null);
        Page("Home.Tip.Cubes", "cubes");
        tips.Add(new HomeTip(loc["Home.Tip.Find"], null));
        Page("Home.Tip.Timers", "timetable");
        Key(s.HotkeyHideUi, "Home.Tip.Hide", null);
        Page("Home.Tip.Odyle", "characters");
        Page("Home.Tip.Dummy", "dummy");
        Page("Home.Tip.CpCalc", "cpcalc");
        Key(s.HotkeyPetMap, "Home.Tip.PetMap", "petmap");
        Page("Home.Tip.Season", "characters");
        Page("Home.Tip.Loot", "loot");
        Key(s.HotkeyHotkeyOverlay, "Home.Tip.Hotkeys", null);
        Page("Home.Tip.Stream", "stream");
        Page("Home.Tip.Theme", "theme");
        return tips;
    }

    /// <summary>The tips' line at the bottom of the greeting; null when the player put the tips away.</summary>
    private UIElement? HomeTipRow()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        List<HomeTip> tips = HomeTips();
        if (MeterSettings.Load().HomeTipsHidden || tips.Count == 0)
        {
            _tipShow = null;
            return null;
        }

        if (_tipAt < 0)
        {
            _tipAt = Headless ? 0 : Random.Shared.Next(tips.Count); // a different one first at each start
        }

        var dock = new DockPanel { Margin = new Thickness(0, 12, 0, 0), Background = Brushes.Transparent };
        TextBlock bulb = ShellText("", "Brush.Accent", 14);
        bulb.FontFamily = new FontFamily("Segoe MDL2 Assets");
        bulb.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(bulb, Dock.Left);
        dock.Children.Add(bulb);
        TextBlock know = ShellText(loc["Home.TipTitle"], "Brush.Accent", 12.5, bold: true);
        know.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(know, Dock.Left);
        dock.Children.Add(know);

        // on the right: the cross, the arrows and the link to the page
        TextBlock hide = TipButton("", loc["Home.TipsHide"]);
        hide.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            MeterSettings settings = MeterSettings.Load();
            settings.HomeTipsHidden = true;
            settings.Save();
            ShowShellPage("home");
        };
        TextBlock next = TipButton("", null), previous = TipButton("", null);
        hide.Margin = new Thickness(10, 0, 0, 0);
        foreach (TextBlock button in new[] { hide, next, previous })
        {
            DockPanel.SetDock(button, Dock.Right);
            dock.Children.Add(button);
        }

        var link = new Border { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(link, Dock.Right);
        dock.Children.Add(link);
        TextBlock text = ShellText("", "Brush.Text", 12.5);
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        dock.Children.Add(text);

        void Show(int index, bool animate)
        {
            _tipAt = ((index % tips.Count) + tips.Count) % tips.Count;
            _tipMoved = DateTime.Now;
            HomeTip tip = tips[_tipAt];
            text.Text = tip.Text;
            text.ToolTip = tip.Text;
            link.Child = null;
            if (tip.Page is { } page)
            {
                TextBlock go = ShellLink(loc["Home.ShowMe"] + " ›", () => ShowShellPage(page));
                go.FontSize = 12;
                link.Child = go;
            }

            if (animate)
            {
                text.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
            }
        }

        previous.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Show(_tipAt - 1, true);
        };
        next.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Show(_tipAt + 1, true);
        };
        dock.MouseEnter += (_, _) => _tipHeld = true;
        dock.MouseLeave += (_, _) =>
        {
            _tipHeld = false;
            _tipMoved = DateTime.Now;
        };
        _tipHeld = false;
        _tipShow = i => Show(i, true);
        Show(_tipAt, false);
        return dock;
    }

    private static TextBlock TipButton(string glyph, string? tip)
    {
        TextBlock button = ShellText(glyph, "Brush.TextSubtle", 10);
        button.FontFamily = new FontFamily("Segoe MDL2 Assets");
        button.Padding = new Thickness(6, 4, 6, 4);
        button.Background = Brushes.Transparent;
        button.Cursor = Cursors.Hand;
        button.ToolTip = tip;
        button.MouseEnter += (_, _) => button.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        button.MouseLeave += (_, _) => button.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
        return button;
    }

    private sealed record SetupStep(string Id, string Glyph, string Title, string What, bool Done, Action Go);

    /// <summary>What is worth setting up first, those of the pages there are in this mode.</summary>
    private List<SetupStep> HomeSetupSteps()
    {
        // 5 November 2026, the owner: cooldowns, then buffs, then the meter, the map, Characters, the CP calculator, then items. A step is done once its page
        // was opened from here, or its overlay is on already.
        LocalizationManager loc = LocalizationManager.Instance;
        MeterSettings s = MeterSettings.Load();
        Dictionary<string, ShellItem> pages = ShellPages();
        bool Opened(string id) => s.HomeSetupOpened.Contains(id);
        var steps = new List<SetupStep>();
        void Add(string id, string glyph, string key, bool on, string page)
        {
            if (pages.ContainsKey(page.StartsWith("assist.", StringComparison.Ordinal) ? "assist" : page))
            {
                steps.Add(new(id, glyph, loc["Home.Setup." + key], loc["Home.Setup." + key + "What"], on || Opened(id), () => ShowShellPage(page)));
            }
        }

        Add("cooldowns", "\uE916", "Cooldowns", s.ShowAssistOverlay, "assist." + AssistWindow.CooldownsTab);
        Add("buffs", "\uE945", "Buffs", s.EffectsOverlayOn, "assist." + AssistWindow.BuffsTab);
        Add("meter", "\uE9D2", "Meter", false, "meterlook");
        Add("map", "\uE707", "Map", s.ShowPetMap, "petmap");
        Add("characters", "\uE716", "Characters", false, "characters");
        Add("cpcalc", "\uE8EF", "CpCalc", false, "cpcalc");
        Add("items", "\uE7B8", "Items", s.ShowLootOverlay, "loot");
        return steps;
    }

    /// <summary>The "Get set up" card: the steps not done yet and not put away; null when there are none (or the card was put away).</summary>
    private UIElement? HomeSetupCard()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        MeterSettings settings = MeterSettings.Load();
        if (settings.HomeSetupHidden)
        {
            return null;
        }

        List<SetupStep> steps = HomeSetupSteps();
        List<SetupStep> open = steps.Where(x => !x.Done && !settings.HomeSetupDismissed.Contains(x.Id)).ToList();
        if (open.Count == 0)
        {
            return null;
        }

        var stack = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        TextBlock hide = ShellLink(loc["Home.SetupHide"], () =>
        {
            MeterSettings s = MeterSettings.Load();
            s.HomeSetupHidden = true;
            s.Save();
            ShowShellPage("home");
        });
        hide.FontSize = 12;
        hide.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
        DockPanel.SetDock(hide, Dock.Right);
        head.Children.Add(hide);
        head.Children.Add(ShellText(loc["Home.SetupTitle"].ToUpperInvariant(), "Brush.TextMuted", 11, bold: true)); // no "1 of 6 done": only what is left shows
        stack.Children.Add(head);

        var tiles = new UniformGrid { Columns = open.Count <= 4 ? open.Count : open.Count is 5 or 6 ? 3 : 4 };
        foreach (SetupStep step in open)
        {
            tiles.Children.Add(SetupTile(step));
        }

        stack.Children.Add(tiles);
        var card = new Border
        {
            Child = stack, Padding = new Thickness(16, 12, 10, 8), Margin = new Thickness(0, 0, 12, 12), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1),
        };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return card;
    }

    private UIElement SetupTile(SetupStep step)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = new Border
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(17), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 12, 0),
            Background = new SolidColorBrush(Color.FromArgb(0x2A, 0xDC, 0xB8, 0x77)),
        };
        TextBlock glyph = ShellText(step.Glyph, "Brush.Accent", 15);
        glyph.FontFamily = new FontFamily("Segoe MDL2 Assets");
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        icon.Child = glyph;
        grid.Children.Add(icon);
        var words = new StackPanel();
        words.Children.Add(ShellText(step.Title, "Brush.Text", 13.5, bold: true));
        TextBlock what = Wrapped(step.What, "Brush.TextMuted", 12);
        what.Margin = new Thickness(0, 3, 0, 6);
        words.Children.Add(what);
        TextBlock go = ShellText(loc["Home.SetupGo"] + "  ›", "Brush.Accent", 12.5, bold: true);
        go.HorizontalAlignment = HorizontalAlignment.Left;
        words.Children.Add(go);
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);

        // "not for me": the cross in the corner, with the mouse on the tile
        TextBlock away = TipButton("", loc["Home.SetupDismiss"]);
        away.HorizontalAlignment = HorizontalAlignment.Right;
        away.VerticalAlignment = VerticalAlignment.Top;
        away.Margin = new Thickness(0, -6, -6, 0);
        away.Visibility = Visibility.Hidden;
        Grid.SetColumnSpan(away, 2);
        grid.Children.Add(away);
        away.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            MeterSettings s = MeterSettings.Load();
            if (!s.HomeSetupDismissed.Contains(step.Id))
            {
                s.HomeSetupDismissed.Add(step.Id);
                s.Save();
            }

            ShowShellPage("home");
        };
        var tile = new Border
        {
            Child = grid, Padding = new Thickness(14, 12, 12, 12), Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand,
        };
        tile.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
        tile.MouseEnter += (_, _) =>
        {
            tile.SetResourceReference(Border.BackgroundProperty, "Brush.ControlHover");
            away.Visibility = Visibility.Visible;
        };
        tile.MouseLeave += (_, _) =>
        {
            tile.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
            away.Visibility = Visibility.Hidden;
        };
        tile.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            MeterSettings opened = MeterSettings.Load();
            if (!opened.HomeSetupOpened.Contains(step.Id))
            {
                opened.HomeSetupOpened.Add(step.Id); // seen: off the card from now on
                opened.Save();
            }

            step.Go();
        };
        return tile;
    }
}
