using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AionDPS.Ui;

/// <summary>
/// The overlay menu over the game (7 November 2026, the owner: a button at the top that opens the overlay menu): the Atreia button of
/// <see cref="OverlayMenuWindow"/> and what its menu holds - every overlay with its hotkey and its switch, then moving the overlays, hiding them all,
/// the hotkeys and the app itself.
/// </summary>
public partial class MainWindow
{
    private OverlayMenuWindow? _overlayMenu;

    /// <summary>Each gate tick: the button is up while the game is in front and its switch is on (never over the desktop, a browser or a game window).</summary>
    private void GateOverlayMenu(MeterSettings s)
    {
        if (Headless)
        {
            return;
        }

        if (!s.ShowOverlayMenu)
        {
            _overlayMenu?.ShowOverlay(false);
            return;
        }

        _overlayMenu ??= new OverlayMenuWindow(close => BuildOverlayMenu(close));
        _overlayMenu.ApplyLockedIfChanged(s.OverlayMenuLocked);
        _overlayMenu.ShowOverlay(GameWindow.OverlayMayShow());
    }

    private static readonly Color MenuGold = Color.FromRgb(0xD9, 0xB3, 0x6A);

    private static SolidColorBrush MenuBrush(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// The menu: a heading, one row per overlay (its name, its hotkey as a key cap - red when it does nothing - and a switch; the whole row is the switch),
    /// then four buttons. <paramref name="shown"/> stands in for the settings in the pictures.
    /// </summary>
    internal FrameworkElement BuildOverlayMenu(Action close, MeterSettings? shown = null)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        MeterSettings s = shown ?? MeterSettings.Load();
        var root = new StackPanel { Width = 312 };

        // the heading: OVERLAYS, a line of what this is, and the ✕
        var head = new DockPanel { Margin = new Thickness(4, 0, 0, 8), LastChildFill = true };
        var x = MenuIconButton("", loc["OverlayMenu.Close"], close);
        DockPanel.SetDock(x, Dock.Right);
        head.Children.Add(x);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = loc["Shell.Overlays"].ToUpper(CultureInfo.CurrentCulture), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(MenuGold) });
        titles.Children.Add(new TextBlock { Text = loc["OverlayMenu.Lede"], FontSize = 11, Foreground = MenuBrush(0xFF, 0x8E, 0x98, 0xA3), Margin = new Thickness(0, 1, 0, 0), TextWrapping = TextWrapping.Wrap });
        head.Children.Add(titles);
        root.Children.Add(head);

        var problems = HotkeyProblems;
        foreach (OverlayDef def in OverlayDefs().Where(d => d.Id != "menu"))
        {
            root.Children.Add(MenuRow(def, s, problems));
        }

        root.Children.Add(new Border { Height = 1, Background = MenuBrush(0xFF, 0x2C, 0x31, 0x3A), Margin = new Thickness(0, 8, 0, 10) });

        // the four buttons, two by two
        bool movable = OverlaysMovable(s) && !_meterOverlayLocked;
        bool hidden = _overlaysSwitchedOff is not null;
        var buttons = new UniformGrid2();
        buttons.Add(MenuAction(movable ? "" : "", loc[movable ? "OverlayMenu.Lock" : "OverlayMenu.Move"], Keys(s.HotkeyMoveOverlays), () =>
        {
            close();
            ToggleOverlaysMovable();
        }));
        buttons.Add(MenuAction(hidden ? "" : "", loc[hidden ? "OverlayMenu.ShowAll" : "OverlayMenu.HideAll"], Keys(s.HotkeyAllOverlays), () =>
        {
            close();
            ToggleAllOverlays();
        }));
        buttons.Add(MenuAction("", loc["OverlayMenu.Hotkeys"], null, () =>
        {
            close();
            BringAppForward();
            OpenSettingsPage("");
        }));
        buttons.Add(MenuAction("", loc["OverlayMenu.OpenApp"], null, () =>
        {
            close();
            BringAppForward();
            ShowShellPage("overlays");
        }));
        root.Children.Add(buttons.Panel);

        return new Border
        {
            Child = root, Padding = new Thickness(12, 10, 12, 12), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1),
            Background = MenuBrush(0xF2, 0x11, 0x13, 0x18), BorderBrush = MenuBrush(0x70, MenuGold.R, MenuGold.G, MenuGold.B),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.55, Color = Colors.Black },
            Margin = new Thickness(10, 4, 10, 14), // room for the shadow
        };

        static string? Keys(string text) => HotkeyBinding.Parse(text) is { IsEmpty: false } b ? b.ToString() : null;
    }

    /// <summary>One overlay of the menu: the name (bright while on), its hotkey and a switch; a click anywhere on the row flips it.</summary>
    private FrameworkElement MenuRow(OverlayDef def, MeterSettings s, IReadOnlyDictionary<HotkeyAction, HotkeyProblem> problems)
    {
        bool on = def.Get(s);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new TextBlock { Text = def.Name, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        grid.Children.Add(name);
        if (def.Keys?.Invoke(s) is { } text && HotkeyBinding.Parse(text) is { IsEmpty: false } binding)
        {
            HotkeyAction? action = HotkeyTable.All.FirstOrDefault(e => e.Get(s) == text)?.Action;
            bool broken = action is { } a && problems.ContainsKey(a);
            var cap = new Border
            {
                CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Padding = new Thickness(5, 0, 5, 1), Margin = new Thickness(8, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center, BorderBrush = broken ? MenuBrush(0xFF, 0x8A, 0x3A, 0x34) : MenuBrush(0xFF, 0x34, 0x39, 0x43),
                Background = MenuBrush(0xFF, 0x1A, 0x1D, 0x24),
                Child = new TextBlock
                {
                    Text = binding.ToString(), FontSize = 10.5, Foreground = broken ? MenuBrush(0xFF, 0xFF, 0x7A, 0x6E) : MenuBrush(0xFF, 0xB8, 0x9A, 0x63),
                },
                ToolTip = broken ? LocalizationManager.Instance[problems[action!.Value] == HotkeyProblem.Twice ? "Hotkeys.Problem.Twice" : "Hotkeys.Problem.Taken"] : null,
            };
            Grid.SetColumn(cap, 1);
            grid.Children.Add(cap);
        }

        var knob = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), VerticalAlignment = VerticalAlignment.Center };
        var track = new Border { Width = 32, Height = 18, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Child = knob, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(track, 2);
        grid.Children.Add(track);
        var row = new Border { Child = grid, Padding = new Thickness(8, 5, 6, 5), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Cursor = Cursors.Hand };

        void Paint()
        {
            name.Foreground = on ? MenuBrush(0xFF, 0xF1, 0xF4, 0xF7) : MenuBrush(0xFF, 0x9A, 0xA4, 0xAE);
            name.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            track.Background = on ? new SolidColorBrush(MenuGold) : MenuBrush(0xFF, 0x22, 0x26, 0x2E);
            track.BorderBrush = on ? new SolidColorBrush(MenuGold) : MenuBrush(0xFF, 0x3A, 0x40, 0x4A);
            knob.Background = on ? MenuBrush(0xFF, 0x1A, 0x12, 0x05) : MenuBrush(0xFF, 0x8A, 0x93, 0x9D);
            knob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            knob.Margin = new Thickness(2, 0, 2, 0);
        }

        Paint();
        row.MouseEnter += (_, _) => row.Background = MenuBrush(0x14, 0xFF, 0xFF, 0xFF);
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            on = !on;
            Paint();
            SetOverlayFromMenu(def, on);
        };
        return row;
    }

    /// <summary>A switch of the menu was flipped: saved, and the overlay follows at once (the meter as its eye button does).</summary>
    private void SetOverlayFromMenu(OverlayDef def, bool on)
    {
        if (def.Id == "meter")
        {
            if (_meterOverlay is null)
            {
                ToggleOverlay(); // the chip look: the window itself is the overlay
                return;
            }

            _meterOverlayOn = on;
            _overlayPeekUntil = on ? DateTime.Now + _meterOverlayHideAfter : DateTime.MinValue;
            if (on)
            {
                FollowNewestRun();
            }
        }

        MeterSettings settings = MeterSettings.Load();
        def.Set(settings, on);
        settings.Save();
        _gateSettings = settings;
        _gateSettingsAt = DateTime.UtcNow;
        if (def.Id == "meter")
        {
            _settingsWindow?.SyncMeterOverlay(on);
            UpdateScopeControls();
            RefreshRows(); // the scope applies only while the overlay is on
            RefreshUpdateBadge();
            UpdateMeterOverlay();
        }

        ApplyTimetable(settings);
        ApplyPetFarm(settings);
        ApplyHotkeyOverlay(settings);
        _settingsWindow?.SyncOverlaySwitches(settings);
    }

    /// <summary>The app's window in front (from the tray, or minimised), for the hotkeys and the app's pages.</summary>
    private void BringAppForward()
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Restore();
            return;
        }

        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    /// <summary>A round ✕-like button of the menu's heading.</summary>
    private static FrameworkElement MenuIconButton(string glyph, string tip, Action click)
    {
        var b = new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = tip, VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 10, Foreground = MenuBrush(0xFF, 0x9A, 0xA4, 0xAE),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        b.MouseEnter += (_, _) => b.Background = MenuBrush(0x22, 0xFF, 0xFF, 0xFF);
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            click();
        };
        return b;
    }

    /// <summary>One of the four buttons under the list: an icon, a word and, when it has one, its hotkey under it.</summary>
    private static FrameworkElement MenuAction(string glyph, string text, string? keys, Action click)
    {
        var icon = new TextBlock
        {
            Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14, Foreground = new SolidColorBrush(MenuGold),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0),
        };
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = MenuBrush(0xFF, 0xE9, 0xEE, 0xF2), TextTrimming = TextTrimming.CharacterEllipsis });
        if (keys is not null)
        {
            words.Children.Add(new TextBlock { Text = keys, FontSize = 10, Foreground = MenuBrush(0xFF, 0x8E, 0x98, 0xA3), Margin = new Thickness(0, 1, 0, 0) });
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(icon);
        row.Children.Add(words);
        var b = new Border
        {
            Child = row, Padding = new Thickness(10, 7, 8, 7), CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, MinHeight = 44,
            Background = MenuBrush(0xFF, 0x1C, 0x1F, 0x27), BorderBrush = MenuBrush(0xFF, 0x2F, 0x34, 0x3E),
        };
        b.MouseEnter += (_, _) =>
        {
            b.Background = MenuBrush(0xFF, 0x25, 0x29, 0x33);
            b.BorderBrush = MenuBrush(0xB0, MenuGold.R, MenuGold.G, MenuGold.B);
        };
        b.MouseLeave += (_, _) =>
        {
            b.Background = MenuBrush(0xFF, 0x1C, 0x1F, 0x27);
            b.BorderBrush = MenuBrush(0xFF, 0x2F, 0x34, 0x3E);
        };
        b.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            click();
        };
        return b;
    }

    /// <summary>Two columns of equal width with a gap between them, row after row.</summary>
    private sealed class UniformGrid2
    {
        public Grid Panel { get; } = new();
        private int _count;

        public UniformGrid2()
        {
            Panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            Panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        public void Add(FrameworkElement element)
        {
            int row = _count / 2;
            if (_count % 2 == 0)
            {
                Panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                if (row > 0)
                {
                    element.Margin = new Thickness(0, 8, 0, 0);
                }
            }
            else if (row > 0)
            {
                element.Margin = new Thickness(0, 8, 0, 0);
            }

            Grid.SetRow(element, row);
            Grid.SetColumn(element, _count % 2 == 0 ? 0 : 2);
            Panel.Children.Add(element);
            _count++;
        }
    }
}
