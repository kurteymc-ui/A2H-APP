using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AionDPS.Ui;

/// <summary>
/// The page of a tool in the tools window: the tool's name, its overlay switch (the master of its overlays), its hotkey (a click opens the hotkey page of the settings)
/// and a cog for its settings, above a live view of what the tool shows. The overlay can be on at the same time; the page and the overlay do not depend on each other.
/// </summary>
public sealed class ModulePage : DockPanel
{
    private readonly Func<MeterSettings, bool> _getOn;
    private readonly Func<MeterSettings, string>? _getKey;
    private readonly CheckBox _master = new();
    private readonly Button _key = new();
    private readonly Button _pop = new();
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    private Action? _popOut;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _syncing;

    public ModulePage(string title, UIElement body, Func<MeterSettings, bool> getOn, Action<MeterSettings, bool> setOn, Func<MeterSettings, string>? getKey,
        Action openHotkeys, Action? openSettings, Action<MeterSettings> applied, Action? tick = null, UIElement? left = null, string? masterLabel = null)
    {
        _getOn = getOn;
        _getKey = getKey;
        var loc = LocalizationManager.Instance;
        Margin = new Thickness(22, 14, 22, 16);

        // the head in the look of Journey and Legion (6 November 2026): the name large in the title face, the tool's controls together on a bar at the right
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var masterText = new TextBlock { Text = masterLabel ?? loc["Settings.Overlay.Master"], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        masterText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
        _master.VerticalAlignment = VerticalAlignment.Center;
        if (Application.Current?.TryFindResource("SettingsSwitch") is Style style)
        {
            _master.Style = style;
        }

        _master.Click += (_, _) =>
        {
            if (_syncing)
            {
                return;
            }

            MeterSettings s = MeterSettings.Load();
            setOn(s, _master.IsChecked == true);
            s.Save();
            applied(s);
        };
        _key.Margin = new Thickness(0, 0, 8, 0);
        _key.Padding = new Thickness(10, 2, 10, 2);
        _key.MinHeight = 26;
        _key.ToolTip = loc["Settings.Hotkey.ChipTip"];
        _key.Click += (_, _) => openHotkeys();
        var cogLabel = new StackPanel { Orientation = Orientation.Horizontal };
        var cogIcon = new TextBlock { Text = "\uE713", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        var cogText = new TextBlock { Text = loc["Settings.Configuration"], Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        cogIcon.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        cogText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        cogLabel.Children.Add(cogIcon);
        cogLabel.Children.Add(cogText);
        var cog = new Button
        {
            Content = cogLabel, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(8, 2, 10, 2), MinHeight = 26, ToolTip = loc["Settings.WindowTitle"],
        };
        cog.SetResourceReference(Control.BorderBrushProperty, "Brush.Accent");
        cog.Click += (_, _) => openSettings?.Invoke();
        right.Children.Add(_key);
        if (openSettings is not null)
        {
            right.Children.Add(cog);
        }

        right.Children.Add(masterText);
        right.Children.Add(_master);
        // right of the overlay switch: the tool in a window of its own, to put it anywhere (only where the owner of the page has set PopOut)
        var popIcon = new TextBlock { Text = "\uE8A7", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        popIcon.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        _pop.Content = popIcon;
        _pop.Margin = new Thickness(10, 0, 0, 0);
        _pop.Padding = new Thickness(8, 2, 8, 2);
        _pop.MinHeight = 26;
        _pop.ToolTip = loc["Tools.PopOut.Tip"];
        _pop.Visibility = Visibility.Collapsed;
        _pop.SetResourceReference(Control.BorderBrushProperty, "Brush.Accent");
        _pop.Click += (_, _) => PopOut?.Invoke();
        right.Children.Add(_pop);
        var bar = new Border { Child = right, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(6, 5, 8, 5), VerticalAlignment = VerticalAlignment.Center };
        bar.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        bar.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        DockPanel.SetDock(bar, Dock.Right);
        var name = new TextBlock { Text = title, FontSize = 26, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        ThemeManager.UseTitleFont(name);
        name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        header.Children.Add(bar);
        header.Children.Add(left ?? name);

        SetDock(header, Dock.Top);
        Children.Add(header);
        _scroll.Content = body;
        Children.Add(_scroll);

        Sync();
        _timer.Tick += (_, _) =>
        {
            Sync();
            tick?.Invoke();
        };
        _timer.Start();
    }

    /// <summary>Shows the tool in a window of its own; the button for it (right of the overlay switch) is there only while this is set.</summary>
    public Action? PopOut
    {
        get => _popOut;
        set
        {
            _popOut = value;
            _pop.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>The page takes the window's width instead of scrolling sideways: for a page that lays itself out to the width it gets (the Timers page puts
    /// its ring beside its lists when there is room and under them when there is not).</summary>
    public bool FitWidth
    {
        get => _scroll.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled;
        set => _scroll.HorizontalScrollBarVisibility = value ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
    }

    public void Stop() => _timer.Stop();

    /// <summary>The switch and the hotkey follow the settings (a hotkey may have flipped the switch).</summary>
    private void Sync()
    {
        MeterSettings s = MeterSettings.Load();
        _syncing = true;
        _master.IsChecked = _getOn(s);
        _syncing = false;
        string key = _getKey is null ? "" : HotkeyBinding.Parse(_getKey(s)).ToString();
        _key.Visibility = _getKey is null ? Visibility.Collapsed : Visibility.Visible;
        _key.Content = key.Length > 0 ? key : LocalizationManager.Instance["Settings.Hotkey.NoKey"];
    }

    /// <summary>A caption above one view of a tool that has several (the map and its list): a short gold stroke, then the text.</summary>
    public static FrameworkElement Caption(string text)
    {
        FrameworkElement title = Stroked(text);
        title.Margin = new Thickness(0, 14, 0, 8);
        return title;
    }

    /// <summary>A section's title as the new pages have it: a short gold stroke, then the text in semibold.</summary>
    public static FrameworkElement Stroked(string text)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var stroke = new Border { Width = 3, Height = 14, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
        stroke.SetResourceReference(Border.BackgroundProperty, "Brush.Accent");
        var block = new TextBlock { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        line.Children.Add(stroke);
        line.Children.Add(block);
        return line;
    }

    /// <summary>A grey note under a tool's header.</summary>
    public static TextBlock Hint(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) };
        block.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        return block;
    }

    /// <summary>A caption with a switch at its end ("show as overlay"): the choice is kept the moment it is flipped, no Save needed.</summary>
    public static (DockPanel Row, CheckBox Switch) SwitchCaption(string text, string switchText)
    {
        var row = new DockPanel { Margin = new Thickness(0, 14, 0, 8) };
        var box = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
        if (Application.Current?.TryFindResource("SettingsSwitch") is Style style)
        {
            box.Style = style;
        }

        var label = new TextBlock { Text = switchText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), FontSize = 12 };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(label);
        right.Children.Add(box);
        DockPanel.SetDock(right, Dock.Right);
        row.Children.Add(right);
        row.Children.Add(Stroked(text));
        return (row, box);
    }
}
