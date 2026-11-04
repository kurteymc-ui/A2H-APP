using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AionDPS.Aion2;

namespace AionDPS.Ui;

/// <summary>
/// The assist window (Tools menu, Skill Assist): two tabs, Cooldowns and Buffs, each with its switches and its list (an <see cref="AssistPanel"/>) of the
/// local player's cooldowns or the effects on him. Cooldowns (4 November 2026): every skill on the skill bar, ticked to be shown on the screen - each where
/// the player puts it, snapping to the others (<see cref="CooldownSkillWindow"/>) - and to be said out loud when it is back; the look (bars or tiles), the
/// size, the bars' width and the voice's volume. The tab chosen last opens again. Built in code, in the look of the other windows.
/// </summary>
public sealed class AssistWindow : Window
{
    private readonly Func<Aion2EntityDirectory?> _directory;
    private readonly AssistPanel _panel = new(false) { Part = AssistPart.Cooldowns };
    private readonly AssistPanel _effects = new(false) { Part = AssistPart.Effects };
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(125) };
    private readonly StackPanel _tabStrip = new() { Orientation = Orientation.Horizontal };
    private readonly DockPanel _cooldownsPage = new();
    private readonly DockPanel _buffsPage = new();
    private readonly List<CheckBox> _lockBoxes = new();
    private readonly CooldownVoice _voiceTest = new();

    /// <summary>The tabs' names in the settings (<see cref="MeterSettings.AssistTab"/>) and for the tools window's size per tab.</summary>
    public const string CooldownsTab = "cooldowns", BuffsTab = "buffs";

    /// <summary>The tab shown: <see cref="CooldownsTab"/> or <see cref="BuffsTab"/>.</summary>
    public string Tab { get; private set; } = CooldownsTab;

    /// <summary>Raised when the player chooses another tab.</summary>
    public event Action? TabChanged;

    public AssistWindow(Func<Aion2EntityDirectory?> directory)
    {
        _directory = directory;
        LocalizationManager loc = LocalizationManager.Instance;
        Title = loc["Assist.Title"];
        Width = 560;
        Height = 640;
        MinWidth = 420;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        MeterSettings settings = MeterSettings.Load();
        var root = new DockPanel { Margin = new Thickness(12, 6, 12, 12) };
        var tabLine = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 2, 0, 12), Child = _tabStrip };
        tabLine.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        DockPanel.SetDock(tabLine, Dock.Top);
        root.Children.Add(tabLine);

        // which skills and effects the overlays show: the boxes in the lists; unticked ones are kept in the settings (one list for both tabs)
        _panel.Hidden = settings.AssistOverlayHidden.ToHashSet();
        _effects.Hidden = _panel.Hidden;
        _panel.ShowInOverlayChanged = (id, shown) => Change(s =>
        {
            s.AssistOverlayHidden.Remove(id);
            if (!shown)
            {
                s.AssistOverlayHidden.Add(id);
            }
        });
        _effects.ShowInOverlayChanged = _panel.ShowInOverlayChanged;
        _effects.SortByTime = settings.AssistSortByTime;
        _panel.Voiced = settings.AssistVoiceSkills.ToHashSet();
        _panel.VoiceChanged = (id, on) => Change(s =>
        {
            s.AssistVoiceSkills.Remove(id);
            if (on)
            {
                s.AssistVoiceSkills.Add(id);
            }
        });

        // Cooldowns: on the screen or not, placing them, the look, then the skills
        var cooldownSwitches = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        cooldownSwitches.Children.Add(Switch(loc["Assist.ShowCooldownsOverlay"], settings.ShowAssistOverlay, on => Change(s => s.ShowAssistOverlay = on)));
        cooldownSwitches.Children.Add(LockSwitch(settings, loc["Assist.Place"]));
        cooldownSwitches.Children.Add(Switch(loc["Assist.ShowReady"], settings.AssistShowReady, on => Change(s => s.AssistShowReady = on)));
        cooldownSwitches.Children.Add(LookRow(settings));
        cooldownSwitches.Children.Add(SliderRow(loc["Assist.Size"], 0.6, 2.0, settings.AssistCooldownScale, v => $"{v * 100:0} %", v => Change(s => s.AssistCooldownScale = Math.Round(v, 2))));
        cooldownSwitches.Children.Add(SliderRow(loc["Assist.BarWidth"], 110, 320, settings.AssistBarWidth, v => $"{v:0}", v => Change(s => s.AssistBarWidth = Math.Round(v))));
        var volume = SliderRow(loc["Assist.VoiceVolume"], 0, 100, settings.AssistVoiceVolume, v => $"{v:0} %", v => Change(s => s.AssistVoiceVolume = (int)Math.Round(v)));
        var test = new Button { Content = loc["Assist.VoiceTest"], Padding = new Thickness(10, 1, 10, 1), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        test.Click += (_, _) => _voiceTest.Say(new[] { loc["Assist.VoiceSample"] }, MeterSettings.Load());
        DockPanel.SetDock(test, Dock.Right);
        volume.Children.Insert(0, test);
        cooldownSwitches.Children.Add(volume);
        var reset = new Button { Content = loc["Assist.ResetPlaces"], HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(12, 3, 12, 3) };
        reset.Click += (_, _) => AssistController.ResetCooldownPlaces();
        cooldownSwitches.Children.Add(reset);
        Fill(_cooldownsPage, cooldownSwitches, loc["Assist.PickHint"], _panel);

        // Buffs: its overlay, the order, then the effects on the player
        var buffSwitches = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        buffSwitches.Children.Add(Switch(loc["Assist.ShowEffectsOverlay"], settings.EffectsOverlayOn, on => Change(s => s.ShowEffectsOverlay = on)));
        buffSwitches.Children.Add(LockSwitch(settings, LocalizationManager.Instance["Settings.PetFarm.Locked"]));
        buffSwitches.Children.Add(Switch(loc["Assist.SortByTime"], settings.AssistSortByTime, on =>
        {
            _effects.SortByTime = on;
            Change(s => s.AssistSortByTime = on);
            _effects.Refresh(_directory());
        }));
        buffSwitches.Children.Add(OverlayControls.ResetButton(OverlayPlaces.Effects));
        Fill(_buffsPage, buffSwitches, loc["Assist.PickHintBuffs"], _effects);

        var pages = new Grid();
        pages.Children.Add(_cooldownsPage);
        pages.Children.Add(_buffsPage);
        root.Children.Add(pages);
        Content = root;
        ThemedChrome.Apply(this);

        // the shown list is updated eight times a second (its rows are kept; a new skill or a box clicked rebuilds it): not while a box is being clicked
        _tick.Tick += (_, _) =>
        {
            AssistPanel shown = Shown();
            if (System.Windows.Input.Mouse.LeftButton == System.Windows.Input.MouseButtonState.Pressed && shown.IsMouseOver)
            {
                return;
            }

            shown.Refresh(_directory());
        };
        _tick.Start();
        Closed += (_, _) => _tick.Stop();
        ShowTab(settings.AssistTab == BuffsTab ? BuffsTab : CooldownsTab, remember: false);
    }

    /// <summary>Opens a tab (<see cref="CooldownsTab"/> or <see cref="BuffsTab"/>).</summary>
    public void ShowTab(string tab, bool remember = true)
    {
        bool changed = tab != Tab;
        Tab = tab;
        _cooldownsPage.Visibility = tab == CooldownsTab ? Visibility.Visible : Visibility.Collapsed;
        _buffsPage.Visibility = tab == BuffsTab ? Visibility.Visible : Visibility.Collapsed;
        DrawTabs();
        Shown().Refresh(_directory());
        if (remember)
        {
            Change(s => s.AssistTab = tab);
        }

        if (changed)
        {
            TabChanged?.Invoke();
        }
    }

    private AssistPanel Shown() => Tab == BuffsTab ? _effects : _panel;

    /// <summary>The two tabs, underlined as on the Loot and History pages.</summary>
    private void DrawTabs()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        _tabStrip.Children.Clear();
        foreach ((string tab, string name) in new[] { (CooldownsTab, loc["Assist.Cooldowns"]), (BuffsTab, loc["Assist.Tab.Buffs"]) })
        {
            bool on = tab == Tab;
            var label = new TextBlock { Text = name, FontSize = 14, Margin = new Thickness(0, 0, 0, 9), FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal };
            label.SetResourceReference(TextBlock.ForegroundProperty, on ? "Brush.Text" : "Brush.TextMuted");
            var line = new Border { Height = 2, CornerRadius = new CornerRadius(1) };
            if (on)
            {
                line.SetResourceReference(Border.BackgroundProperty, "Brush.Accent");
            }

            var stack = new StackPanel { Margin = new Thickness(0, 0, 26, -1), Cursor = System.Windows.Input.Cursors.Hand, Background = System.Windows.Media.Brushes.Transparent };
            stack.Children.Add(label);
            stack.Children.Add(line);
            string chosen = tab;
            stack.MouseLeftButtonUp += (_, _) => ShowTab(chosen);
            _tabStrip.Children.Add(stack);
        }
    }

    /// <summary>A tab's page: its switches on top, the hint with "Show all", then its list.</summary>
    private void Fill(DockPanel page, StackPanel switches, string hint, AssistPanel list)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        DockPanel.SetDock(switches, Dock.Top);
        page.Children.Add(switches);
        var pickBar = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(pickBar, Dock.Top);
        var showAll = new Button { Content = loc["Assist.ShowAll"], Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        showAll.Click += (_, _) =>
        {
            _panel.Hidden.Clear();
            Change(s => s.AssistOverlayHidden.Clear());
            list.Refresh(_directory());
        };
        DockPanel.SetDock(showAll, Dock.Right);
        var pickHint = new TextBlock { Text = hint, TextWrapping = TextWrapping.Wrap, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        pickHint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        pickBar.Children.Add(showAll);
        pickBar.Children.Add(pickHint);
        page.Children.Add(pickBar);
        page.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list });
    }

    /// <summary>"Place them": one setting for the cooldowns and the effects, on both tabs (the two boxes follow each other).</summary>
    private CheckBox LockSwitch(MeterSettings settings, string text)
    {
        CheckBox box = Switch(text, !settings.AssistOverlayLocked, on =>
        {
            Change(s => s.AssistOverlayLocked = !on);
            foreach (CheckBox other in _lockBoxes)
            {
                other.IsChecked = on;
            }
        });
        _lockBoxes.Add(box);
        return box;
    }

    /// <summary>The look of the cooldowns and the effects: bars or tiles (two buttons, the chosen one marked).</summary>
    private static UIElement LookRow(MeterSettings settings)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var label = new TextBlock { Text = loc["Assist.Look"], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), MinWidth = 90 };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        row.Children.Add(label);
        var buttons = new List<RadioButton>();
        foreach ((string look, string name) in new[] { (CooldownArt.Bars, loc["Assist.LookBars"]), (CooldownArt.Tiles, loc["Assist.LookTiles"]) })
        {
            var button = new RadioButton { Content = name, GroupName = "assistLook", IsChecked = (settings.AssistCooldownStyle == CooldownArt.Tiles) == (look == CooldownArt.Tiles),
                Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            button.SetResourceReference(ForegroundProperty, "Brush.Text");
            string chosen = look;
            button.Checked += (_, _) => Change(s => s.AssistCooldownStyle = chosen);
            buttons.Add(button);
            row.Children.Add(button);
        }

        return row;
    }

    /// <summary>A labelled slider; the value is written a moment after the last move.</summary>
    private static DockPanel SliderRow(string text, double min, double max, double value, Func<double, string> shown, Action<double> changed)
    {
        var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), MinWidth = 90 };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        DockPanel.SetDock(label, Dock.Left);
        var amount = new TextBlock { Text = shown(Math.Clamp(value, min, max)), VerticalAlignment = VerticalAlignment.Center, MinWidth = 48, TextAlignment = TextAlignment.Right };
        amount.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        DockPanel.SetDock(amount, Dock.Right);
        var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), VerticalAlignment = VerticalAlignment.Center };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            changed(slider.Value);
        };
        slider.ValueChanged += (_, _) =>
        {
            amount.Text = shown(slider.Value);
            timer.Stop();
            timer.Start();
        };
        row.Children.Add(label);
        row.Children.Add(amount);
        row.Children.Add(slider);
        return row;
    }

    private static CheckBox Switch(string text, bool on, Action<bool> changed)
    {
        var box = new CheckBox { Content = text, IsChecked = on };
        if (Application.Current?.TryFindResource("SettingsSwitch") is Style style)
        {
            box.Style = style;
        }

        box.Click += (_, _) => changed(box.IsChecked == true);
        return box;
    }

    /// <summary>Changes one setting in the file; the overlay's controller reads it within a second.</summary>
    private static void Change(Action<MeterSettings> apply)
    {
        MeterSettings settings = MeterSettings.Load();
        apply(settings);
        settings.Save();
    }
}
