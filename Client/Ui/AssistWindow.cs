using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using AionDPS.Aion2;

namespace AionDPS.Ui;

/// <summary>
/// The assist window (Tools menu, Skill Assist): two tabs, Cooldowns and Buffs, each with its switches and its list (an <see cref="AssistPanel"/>) of the
/// local player's cooldowns or the effects on him. Cooldowns (4 November 2026): every skill on the skill bar, ticked to be shown on the screen - each where
/// the player puts it, alone or all of them together (<see cref="CooldownSkillWindow"/>) - and to be said out loud when it is back; the look (bars or
/// squares), the size, the bars' width and the voice's volume. The tab chosen last opens again. Built in code, in the look of the other windows.
/// <para>5 November 2026, the Cooldowns tab redrawn ("kinda mid"): two cards with short labels - On screen (the switch in its corner, a Place button, the
/// look as two segments, the sliders, the two smaller switches) and Say when ready (the voice, the volume, a test) - then the skills under a header with
/// their columns; the whole tab scrolls as one page.</para>
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
    private readonly List<ToggleButton> _lockBoxes = new();
    private readonly List<RadioButton> _lookButtons = new();
    private readonly CooldownVoice _voiceTest = new();
    private CheckBox? _togetherBox;
    private Slider? _sizeSlider;
    private Button? _placeButton;
    private FrameworkElement? _barWidthRow;
    private Slider? _barWidthSlider;
    private Slider? _opacitySlider, _readyOpacitySlider, _backSlider;
    private FrameworkElement? _backRow;
    private CheckBox? _readyBox;
    private readonly UniformGrid _presets = new() { Columns = 3, Margin = new Thickness(0, 12, 0, 2) };

    /// <summary>Above the presets: whose they are (the class played, or picked by hand); under them: copying another class's layout.</summary>
    private readonly WrapPanel _classLine = new() { Margin = new Thickness(0, 10, 0, 0) }, _copyLine = new() { Margin = new Thickness(0, 10, 0, 0) };

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
        Width = 580;
        Height = 780;
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
        // the class's other skills ticked to show on the screen too
        _panel.Extra = settings.AssistExtraSkills.ToHashSet();
        _panel.ExtraChanged = (id, shown) => Change(s =>
        {
            s.AssistExtraSkills.Remove(id);
            if (shown)
            {
                s.AssistExtraSkills.Add(id);
            }
        });
        _panel.Voiced = settings.AssistVoiceSkills.ToHashSet();
        _panel.VoiceChanged = (id, on) => Change(s =>
        {
            s.AssistVoiceSkills.Remove(id);
            if (on)
            {
                s.AssistVoiceSkills.Add(id);
            }
        });

        // Cooldowns: a card for the screen, one for the voice, then the skills
        BuildCooldowns(settings);

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
        AssistController.EditBarChanged += FromEditBar; // the bar at the top of the screen changed the look, the size, together or Done
        Closed += (_, _) =>
        {
            _tick.Stop();
            AssistController.EditBarChanged -= FromEditBar;
        };
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

    /// <summary>Shows what the bar at the top of the screen (while placing) changed.</summary>
    private void FromEditBar()
    {
        MeterSettings s = MeterSettings.Load();
        foreach (ToggleButton box in _lockBoxes)
        {
            box.IsChecked = !s.AssistOverlayLocked;
        }

        ShowPlacing(!s.AssistOverlayLocked);
        if (_barWidthRow is not null)
        {
            _barWidthRow.Visibility = s.AssistCooldownStyle == CooldownArt.Tiles ? Visibility.Collapsed : Visibility.Visible;
        }

        if (_backRow is not null)
        {
            _backRow.Visibility = s.AssistCooldownStyle == CooldownArt.Tiles ? Visibility.Collapsed : Visibility.Visible;
        }

        foreach ((Slider? slider, double value) in new[] { (_opacitySlider, s.AssistCooldownOpacity), (_readyOpacitySlider, s.AssistReadyOpacity), (_backSlider, s.AssistBackOpacity) })
        {
            if (slider is not null && Math.Abs(slider.Value - value) > 0.004)
            {
                slider.Value = Math.Clamp(value, slider.Minimum, slider.Maximum); // a preset loaded
            }
        }

        if (_togetherBox is not null)
        {
            _togetherBox.IsChecked = s.AssistCooldownsTogether;
        }

        foreach (RadioButton button in _lookButtons)
        {
            bool tiles = Equals(button.Tag, CooldownArt.Tiles);
            if (tiles == (s.AssistCooldownStyle == CooldownArt.Tiles) && button.IsChecked != true)
            {
                button.IsChecked = true;
            }
        }

        if (_sizeSlider is not null && Math.Abs(_sizeSlider.Value - s.AssistCooldownScale) > 0.001)
        {
            _sizeSlider.Value = Math.Clamp(s.AssistCooldownScale, _sizeSlider.Minimum, _sizeSlider.Maximum);
        }

        if (_barWidthSlider is not null && Math.Abs(_barWidthSlider.Value - s.AssistBarWidth) > 0.5)
        {
            _barWidthSlider.Value = Math.Clamp(s.AssistBarWidth, _barWidthSlider.Minimum, _barWidthSlider.Maximum);
        }

        if (_readyBox is not null)
        {
            _readyBox.IsChecked = s.AssistShowReady;
        }

        // a preset loaded: its skills left off the screen, and the other skills it shows
        if (!_panel.Hidden.SetEquals(s.AssistOverlayHidden) || !_panel.Extra.SetEquals(s.AssistExtraSkills))
        {
            _panel.Hidden.Clear();
            _panel.Hidden.UnionWith(s.AssistOverlayHidden);
            _panel.Extra.Clear();
            _panel.Extra.UnionWith(s.AssistExtraSkills);
            Shown().Refresh(_directory());
        }

        DrawPresets(s);
    }

    /// <summary>"Place them": one setting for the cooldowns and the effects, on both tabs (the two boxes follow each other).</summary>
    private CheckBox LockSwitch(MeterSettings settings, string text)
    {
        CheckBox box = Switch(text, !settings.AssistOverlayLocked, on =>
        {
            Change(s => s.AssistOverlayLocked = !on);
            foreach (ToggleButton other in _lockBoxes)
            {
                other.IsChecked = on;
            }

            ShowPlacing(on);
        });
        _lockBoxes.Add(box);
        return box;
    }

    /// <summary>The Cooldowns tab: the card for the screen, the card for the voice, the skills; one page that scrolls.</summary>
    private void BuildCooldowns(MeterSettings settings)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var page = new StackPanel { Margin = new Thickness(0, 0, 4, 0) };

        // on the screen: shown or not (the switch in the card's corner), placing them, the look, the size, then the two smaller switches
        var screen = new StackPanel();
        CheckBox show = Switch("", settings.ShowAssistOverlay, on => Change(s => s.ShowAssistOverlay = on));
        show.HorizontalAlignment = HorizontalAlignment.Right;
        show.ToolTip = loc["Assist.ShowCooldownsOverlay"];
        screen.Children.Add(CardHead(loc["Assist.Sec.Screen"], loc["Assist.Sec.ScreenSub"], show));
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 6) };
        _placeButton = new Button { Template = OutlineTemplate(), Padding = new Thickness(14, 6, 14, 6), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = loc["Assist.Place"] };
        _placeButton.Click += (_, _) =>
        {
            bool placing = MeterSettings.Load().AssistOverlayLocked; // locked now: placing from here on
            Change(s => s.AssistOverlayLocked = !placing);
            foreach (ToggleButton other in _lockBoxes)
            {
                other.IsChecked = placing;
            }

            ShowPlacing(placing);
        };
        ShowPlacing(!settings.AssistOverlayLocked);
        actions.Children.Add(_placeButton);
        var reset = new Button { Content = Glyphed("\uE72C", loc["Assist.ResetShort"]), ToolTip = loc["Assist.ResetPlaces"], Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(8, 0, 0, 0) };
        reset.Click += (_, _) => AssistController.ResetCooldownPlaces();
        actions.Children.Add(reset);
        screen.Children.Add(actions);
        screen.Children.Add(Field(loc["Assist.Look"], LookSegments(settings)));
        screen.Children.Add(SliderRow(loc["Assist.Size"], 0.6, 2.0, settings.AssistCooldownScale, v => $"{v * 100:0} %", v => Change(s => s.AssistCooldownScale = Math.Round(v, 2)), out _sizeSlider));
        _barWidthRow = SliderRow(loc["Assist.BarWidth"], 110, 320, settings.AssistBarWidth, v => $"{v:0}", v => Change(s => s.AssistBarWidth = Math.Round(v)), out _barWidthSlider);
        _barWidthRow.Visibility = settings.AssistCooldownStyle == CooldownArt.Tiles ? Visibility.Collapsed : Visibility.Visible; // only the bars have a width
        screen.Children.Add(_barWidthRow);

        // how see-through: all of it, a ready skill, the bars' background
        screen.Children.Add(Divider());
        FrameworkElement opacity = SliderRow(loc["Assist.Opacity"], 0.15, 1, settings.AssistCooldownOpacity, v => $"{v * 100:0} %", v => Change(s => s.AssistCooldownOpacity = Math.Round(v, 2)), out _opacitySlider);
        opacity.ToolTip = loc["Assist.OpacityTip"];
        opacity.Margin = new Thickness(0, 4, 0, 0);
        screen.Children.Add(opacity);
        FrameworkElement readyOpacity = SliderRow(loc["Assist.ReadyOpacity"], 0.15, 1, settings.AssistReadyOpacity, v => $"{v * 100:0} %", v => Change(s => s.AssistReadyOpacity = Math.Round(v, 2)), out _readyOpacitySlider);
        readyOpacity.ToolTip = loc["Assist.ReadyOpacityTip"];
        screen.Children.Add(readyOpacity);
        _backRow = SliderRow(loc["Assist.BackOpacity"], 0, 1, settings.AssistBackOpacity, v => $"{v * 100:0} %", v => Change(s => s.AssistBackOpacity = Math.Round(v, 2)), out _backSlider);
        _backRow.ToolTip = loc["Assist.BackOpacityTip"];
        _backRow.Visibility = settings.AssistCooldownStyle == CooldownArt.Tiles ? Visibility.Collapsed : Visibility.Visible; // only the bars have a background
        screen.Children.Add(_backRow);
        screen.Children.Add(Divider());
        _togetherBox = Switch(loc["Assist.TogetherShort"], settings.AssistCooldownsTogether, on => Change(s => s.AssistCooldownsTogether = on));
        _togetherBox.ToolTip = loc["Assist.MoveTogetherTip"];
        screen.Children.Add(_togetherBox);
        _readyBox = Switch(loc["Assist.ReadyShort"], settings.AssistShowReady, on => Change(s => s.AssistShowReady = on));
        _readyBox.ToolTip = loc["Assist.ShowReady"];
        screen.Children.Add(_readyBox);
        page.Children.Add(Card(screen));

        // three saved layouts: how the cooldowns sit on the screen, to switch between
        var presets = new StackPanel();
        presets.Children.Add(CardHead(loc["Assist.Sec.Presets"], loc["Assist.Sec.PresetsSub"], null));
        presets.Children.Add(_classLine);
        presets.Children.Add(_presets);
        presets.Children.Add(_copyLine);
        DrawPresets(settings);
        page.Children.Add(Card(presets));

        // the voice that says a skill when it is back: which voice (also the boss warnings'), how loud, a test
        var voice = new StackPanel();
        voice.Children.Add(CardHead(loc["Assist.Sec.Voice"], loc["Assist.Sec.VoiceSub"], null));
        // the voice and its test side by side, the volume under them
        var test = new Button { Content = Glyphed("\uE768", loc["Assist.VoiceTest"]), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        test.Click += (_, _) =>
        {
            MeterSettings now = MeterSettings.Load();
            _voiceTest.Say(new[] { CooldownVoice.SampleName(now) }, now);
        };
        var pick = new DockPanel();
        DockPanel.SetDock(test, Dock.Right);
        pick.Children.Add(test);
        ComboBox voices = VoicePicker(settings);
        voices.HorizontalAlignment = HorizontalAlignment.Stretch;
        pick.Children.Add(voices);
        FrameworkElement picker = Field(loc["Assist.VoicePick"], pick);
        picker.Margin = new Thickness(0, 12, 0, 0);
        voice.Children.Add(picker);
        voice.Children.Add(SliderRow(loc["Assist.Volume"], 0, 100, settings.AssistVoiceVolume, v => $"{v:0} %", v => Change(s => s.AssistVoiceVolume = (int)Math.Round(v)), out _));
        page.Children.Add(Card(voice));

        // the skills: a heading with "Show all", a line on what the boxes do, the list with its columns
        var head = new DockPanel { Margin = new Thickness(2, 8, 0, 2) };
        var showAll = new Button { Content = loc["Assist.ShowAll"], Padding = new Thickness(10, 3, 10, 3), VerticalAlignment = VerticalAlignment.Center };
        showAll.Click += (_, _) =>
        {
            _panel.Hidden.Clear();
            Change(s => s.AssistOverlayHidden.Clear());
            _panel.Refresh(_directory());
        };
        DockPanel.SetDock(showAll, Dock.Right);
        head.Children.Add(showAll);
        var title = new TextBlock { Text = loc["Assist.Sec.Skills"], FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        head.Children.Add(title);
        page.Children.Add(head);
        var hint = new TextBlock { Text = loc["Assist.SkillsHint"], TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(2, 0, 0, 8) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        page.Children.Add(hint);
        page.Children.Add(_panel);
        _cooldownsPage.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page });
    }

    /// <summary>The Place button: outlined in the accent colour, filled while placing ("Done placing").</summary>
    private void ShowPlacing(bool placing)
    {
        if (_placeButton is null)
        {
            return;
        }

        LocalizationManager loc = LocalizationManager.Instance;
        _placeButton.Content = placing ? Glyphed("\uE73E", loc["Assist.PlaceDone"]) : Glyphed("\uE7C2", loc["Assist.PlaceButton"]);
        _placeButton.SetResourceReference(BorderBrushProperty, "Brush.Accent");
        _placeButton.SetResourceReference(BackgroundProperty, placing ? "Brush.Accent" : "Brush.Control");
        _placeButton.SetResourceReference(ForegroundProperty, placing ? "Brush.Window" : "Brush.Accent");
        _placeButton.FontWeight = FontWeights.SemiBold;
    }

    /// <summary>A card of the tab: the panel's colour, a thin edge, round corners.</summary>
    private static Border Card(UIElement child)
    {
        var card = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 14, 16, 12), Margin = new Thickness(0, 0, 0, 10), Child = child };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return card;
    }

    /// <summary>A card's title and the line under it, with a control in the corner (the card's switch).</summary>
    private static DockPanel CardHead(string title, string sub, FrameworkElement? corner)
    {
        var head = new DockPanel();
        if (corner is not null)
        {
            corner.VerticalAlignment = VerticalAlignment.Top;
            corner.Margin = new Thickness(12, 0, 0, 0);
            DockPanel.SetDock(corner, Dock.Right);
            head.Children.Add(corner);
        }

        var words = new StackPanel();
        var big = new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold };
        big.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        var small = new TextBlock { Text = sub, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        small.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        words.Children.Add(big);
        words.Children.Add(small);
        head.Children.Add(words);
        return head;
    }

    /// <summary>A row with its label in the column of the sliders' labels.</summary>
    private static DockPanel Field(string text, FrameworkElement control)
    {
        var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), Width = LabelWidth };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        DockPanel.SetDock(label, Dock.Left);
        control.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);
        row.Children.Add(control);
        return row;
    }

    private static Border Divider()
    {
        var line = new Border { Height = 1, Margin = new Thickness(0, 12, 0, 6) };
        line.SetResourceReference(Border.BackgroundProperty, "Brush.Border");
        return line;
    }

    /// <summary>A Segoe MDL2 glyph before a word, for the buttons.</summary>
    private static StackPanel Glyphed(string glyph, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 7, 0) });
        row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    /// <summary>The look of the cooldowns and the effects: bars or squares, as two segments of one control (the chosen one raised).</summary>
    private FrameworkElement LookSegments(MeterSettings settings)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var strip = new StackPanel { Orientation = Orientation.Horizontal };
        var frame = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(2), Child = strip, HorizontalAlignment = HorizontalAlignment.Left };
        frame.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
        frame.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        ControlTemplate template = SegmentTemplate();
        foreach ((string look, string name, string glyph) in new[] { (CooldownArt.Bars, loc["Assist.LookBars"], "\uE8FD"), (CooldownArt.Tiles, loc["Assist.LookTiles"], "\uF0E2") })
        {
            var button = new RadioButton
            {
                Content = Glyphed(glyph, name), GroupName = "assistLook", Template = template, Tag = look, Cursor = System.Windows.Input.Cursors.Hand,
                IsChecked = (settings.AssistCooldownStyle == CooldownArt.Tiles) == (look == CooldownArt.Tiles),
            };
            string chosen = look;
            button.Checked += (_, _) =>
            {
                Change(s => s.AssistCooldownStyle = chosen);
                if (_barWidthRow is not null)
                {
                    _barWidthRow.Visibility = chosen == CooldownArt.Tiles ? Visibility.Collapsed : Visibility.Visible;
                }

                if (_backRow is not null)
                {
                    _backRow.Visibility = chosen == CooldownArt.Tiles ? Visibility.Collapsed : Visibility.Visible;
                }
            };
            _lookButtons.Add(button);
            strip.Children.Add(button);
        }

        return frame;
    }

    /// <summary>The voices (the recorded natural one first), each with its language; choosing one sets the voice of the boss warnings too.</summary>
    private static ComboBox VoicePicker(MeterSettings settings)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var combo = new ComboBox { MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = loc["Assist.VoicePickTip"], Style = DarkCombo.Value, ItemContainerStyle = DarkComboItem.Value };
        IReadOnlyList<SpeechVoice> voices = CastSpeech.Voices();
        // the natural voice first, then the voice's own language, then the others
        string words = CastSpeech.WordsLanguage(settings);
        var ordered = voices.OrderBy(v => NaturalVoice.Is(v.Id) ? 0 : CastSpeech.TableLanguage(v.Language) == words ? 1 : 2).ToList();
        foreach (SpeechVoice v in ordered)
        {
            combo.Items.Add(new ComboBoxItem { Content = v.Name + " · " + CastSpeech.LanguageName(v.Language), Tag = v });
        }

        // the saved voice; none saved: the first of its language, as the voice speaks
        SpeechVoice? chosen = ordered.FirstOrDefault(v => v.Id == settings.SpeechVoiceId)
            ?? ordered.FirstOrDefault(v => !NaturalVoice.Is(v.Id) && CastSpeech.TableLanguage(v.Language) == words);
        combo.SelectedIndex = chosen is null ? -1 : ordered.IndexOf(chosen);
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: SpeechVoice v })
            {
                Change(s =>
                {
                    s.SpeechVoiceId = v.Id;
                    s.SpeechLanguage = v.Language;
                });
            }
        };
        return combo;
    }

    /// <summary>A segment of the look's control: muted text on the strip's colour, filled with the accent colour (dark text) when chosen - the chosen
    /// one stands out at a glance (5 November 2026: raised dark grey on dark grey was hard to tell apart).</summary>
    private static ControlTemplate SegmentTemplate() => (ControlTemplate)XamlReader.Parse(
        "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='RadioButton'>" +
        "<Border x:Name='Bd' Background='Transparent' CornerRadius='6' Padding='16,5' MinWidth='96'>" +
        "<ContentPresenter x:Name='Text' HorizontalAlignment='Center' VerticalAlignment='Center' TextElement.Foreground='{DynamicResource Brush.TextMuted}'/></Border>" +
        "<ControlTemplate.Triggers>" +
        "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Bd' Property='Background' Value='{DynamicResource Brush.ControlHover}'/>" +
        "<Setter TargetName='Text' Property='TextElement.Foreground' Value='{DynamicResource Brush.Text}'/></Trigger>" +
        "<Trigger Property='IsChecked' Value='True'><Setter TargetName='Bd' Property='Background' Value='{DynamicResource Brush.Accent}'/>" +
        "<Setter TargetName='Text' Property='TextElement.Foreground' Value='{DynamicResource Brush.Window}'/>" +
        "<Setter TargetName='Text' Property='TextElement.FontWeight' Value='SemiBold'/></Trigger>" +
        "</ControlTemplate.Triggers></ControlTemplate>");

    /// <summary>A button drawn by its own colours (the Place button): a round edge, its background, its foreground.</summary>
    private static ControlTemplate OutlineTemplate() => (ControlTemplate)XamlReader.Parse(
        "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>" +
        "<Border x:Name='Bd' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='6' Padding='{TemplateBinding Padding}'>" +
        "<ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>" +
        "<ControlTemplate.Triggers>" +
        "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Bd' Property='Opacity' Value='0.85'/></Trigger>" +
        "<Trigger Property='IsPressed' Value='True'><Setter TargetName='Bd' Property='Opacity' Value='0.7'/></Trigger>" +
        "</ControlTemplate.Triggers></ControlTemplate>");

    /// <summary>The width of the labels before the sliders and the fields.</summary>
    private const double LabelWidth = 92;

    /// <summary>A labelled slider in the window's own look (<see cref="NiceSlider"/>) with its value in a chip on the right; the value is written a moment
    /// after the last move.</summary>
    private static Grid SliderRow(string text, double min, double max, double value, Func<double, string> shown, Action<double> changed, out Slider made)
    {
        var row = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth + 12) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 12, 0) };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        var amount = new TextBlock { Text = shown(Math.Clamp(value, min, max)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 12, FontWeight = FontWeights.SemiBold };
        amount.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        var chip = new Border { Child = amount, MinWidth = 58, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(12, 0, 0, 0), CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
        chip.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
        chip.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), VerticalAlignment = VerticalAlignment.Center, Style = NiceSlider.Value };
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
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(chip, 2);
        row.Children.Add(label);
        row.Children.Add(slider);
        row.Children.Add(chip);
        made = slider;
        return row;
    }

    /// <summary>
    /// The window's slider: a rounded rail, the part up to the knob filled in the accent colour, a round accent knob with a ring in the card's colour that
    /// glows under the mouse and while dragged; a click on the rail jumps there (5 November 2026: the plain Windows slider was white and out of place).
    /// </summary>
    private static readonly Lazy<Style> NiceSlider = new(() => (Style)XamlReader.Parse(
        "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Slider'>" +
        "<Setter Property='Height' Value='26'/><Setter Property='Focusable' Value='False'/><Setter Property='IsMoveToPointEnabled' Value='True'/>" +
        "<Setter Property='Cursor' Value='Hand'/>" +
        "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Slider'>" +
        "<Grid Background='Transparent'>" +
        "<Border Height='6' CornerRadius='3' VerticalAlignment='Center' Margin='2,0' Background='{DynamicResource Brush.Control}' BorderBrush='{DynamicResource Brush.Border}' BorderThickness='1'/>" +
        "<Track x:Name='PART_Track'>" +
        "<Track.DecreaseRepeatButton><RepeatButton Command='Slider.DecreaseLarge' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'>" +
        "<Grid Background='Transparent'><Border Height='6' CornerRadius='3,0,0,3' VerticalAlignment='Center' Margin='2,0,-2,0' Background='{DynamicResource Brush.Accent}'/></Grid>" +
        "</ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>" +
        "<Track.IncreaseRepeatButton><RepeatButton Command='Slider.IncreaseLarge' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'>" +
        "<Grid Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>" +
        "<Track.Thumb><Thumb Width='18' Height='18'><Thumb.Template><ControlTemplate TargetType='Thumb'>" +
        "<Grid><Ellipse x:Name='Glow' Margin='-5' Fill='{DynamicResource Brush.Accent}' Opacity='0'/>" +
        "<Ellipse Fill='{DynamicResource Brush.Accent}' Stroke='{DynamicResource Brush.Panel}' StrokeThickness='3'/></Grid>" +
        "<ControlTemplate.Triggers>" +
        "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Glow' Property='Opacity' Value='0.22'/></Trigger>" +
        "<Trigger Property='IsDragging' Value='True'><Setter TargetName='Glow' Property='Opacity' Value='0.35'/></Trigger>" +
        "</ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>" +
        "</Track></Grid></ControlTemplate></Setter.Value></Setter></Style>"));

    /// <summary>The voice picker in the window's look (the Settings window's dropdown: the control's colour, round corners, a chevron, the list on the
    /// panel's colour): the plain Windows one was white.</summary>
    private static readonly Lazy<Style> DarkCombo = new(() => (Style)XamlReader.Parse(
        "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ComboBox'>" +
        "<Setter Property='Height' Value='30'/><Setter Property='Background' Value='{DynamicResource Brush.Control}'/>" +
        "<Setter Property='Foreground' Value='{DynamicResource Brush.Text}'/><Setter Property='BorderBrush' Value='{DynamicResource Brush.Border}'/><Setter Property='Cursor' Value='Hand'/>" +
        "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBox'><Grid>" +
        "<ToggleButton Focusable='False' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' ClickMode='Press' " +
        "IsChecked='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>" +
        "<ToggleButton.Template><ControlTemplate TargetType='ToggleButton'>" +
        "<Border x:Name='Bd' Background='{TemplateBinding Background}' CornerRadius='8' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1'>" +
        "<Path Data='M0.5,0.5 L4,4 L7.5,0.5' Stroke='{DynamicResource Brush.TextMuted}' StrokeThickness='1.6' StrokeStartLineCap='Round' StrokeEndLineCap='Round' " +
        "StrokeLineJoin='Round' Margin='0,1,12,0' HorizontalAlignment='Right' VerticalAlignment='Center'/></Border>" +
        "<ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Bd' Property='Background' Value='{DynamicResource Brush.ControlHover}'/></Trigger>" +
        "</ControlTemplate.Triggers></ControlTemplate></ToggleButton.Template></ToggleButton>" +
        "<ContentPresenter IsHitTestVisible='False' Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}' " +
        "Margin='12,3,30,3' VerticalAlignment='Center' HorizontalAlignment='Left' TextElement.Foreground='{DynamicResource Brush.Text}'/>" +
        "<Popup IsOpen='{TemplateBinding IsDropDownOpen}' AllowsTransparency='True' Focusable='False' PopupAnimation='Slide'>" +
        "<Border Background='{DynamicResource Brush.Panel}' BorderBrush='{DynamicResource Brush.Border}' BorderThickness='1' CornerRadius='10' Padding='0,4' Margin='0,4,0,0' " +
        "MaxHeight='360' MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}'>" +
        "<ScrollViewer SnapsToDevicePixels='True'><ItemsPresenter/></ScrollViewer></Border></Popup>" +
        "</Grid></ControlTemplate></Setter.Value></Setter></Style>"));

    private static readonly Lazy<Style> DarkComboItem = new(() => (Style)XamlReader.Parse(
        "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ComboBoxItem'>" +
        "<Setter Property='Background' Value='Transparent'/><Setter Property='Foreground' Value='{DynamicResource Brush.Text}'/>" +
        "<Setter Property='Padding' Value='10,6'/><Setter Property='Margin' Value='4,1'/><Setter Property='Cursor' Value='Hand'/>" +
        "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBoxItem'>" +
        "<Border x:Name='Bd' Background='{TemplateBinding Background}' CornerRadius='7' Padding='{TemplateBinding Padding}'><ContentPresenter VerticalAlignment='Center'/></Border>" +
        "<ControlTemplate.Triggers><Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Bd' Property='Background' Value='{DynamicResource Brush.ControlHover}'/></Trigger>" +
        "<Trigger Property='IsSelected' Value='True'><Setter Property='Foreground' Value='{DynamicResource Brush.Accent}'/></Trigger>" +
        "</ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>"));

    /// <summary>The three preset slots: each with its look, size and when it was saved, Load and Save (an empty one only Save); a slot that is full asks
    /// once more before it is written over. The one last saved or loaded is outlined.</summary>
    private void DrawPresets(MeterSettings settings)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        _presets.Children.Clear();
        int cls = Aion2Cooldowns.CurrentClass();
        Dictionary<int, CooldownPreset> slots = AssistController.PresetsOf(settings, cls);
        DrawClassLines(settings, cls);
        for (int slot = 1; slot <= 3; slot++)
        {
            int mine = slot;
            CooldownPreset? preset = slots.GetValueOrDefault(slot);
            bool active = preset is not null && settings.AssistPresetActive == slot;
            var body = new StackPanel();
            var name = new TextBlock { Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, loc["Assist.PresetN"], slot), FontWeight = FontWeights.SemiBold, FontSize = 13 };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            body.Children.Add(name);
            string line1 = preset is null ? loc["Assist.PresetEmpty"]
                : (preset.Style == CooldownArt.Tiles ? loc["Assist.LookTiles"] : loc["Assist.LookBars"]) + " · " + Math.Round(preset.Scale * 100) + " %";
            string line2 = preset is null ? " " : preset.SavedAt.Date == DateTime.Today ? preset.SavedAt.ToString("t", System.Globalization.CultureInfo.CurrentCulture)
                : preset.SavedAt.ToString("d MMM", System.Globalization.CultureInfo.CurrentCulture);
            foreach (string line in new[] { line1, line2 })
            {
                var small = new TextBlock { Text = line, FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
                small.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
                body.Children.Add(small);
            }

            var buttons = new UniformGrid { Columns = preset is null ? 1 : 2, Margin = new Thickness(0, 10, 0, 0) };
            if (preset is not null)
            {
                var load = new Button { Template = OutlineTemplate(), Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 4, 0), Cursor = System.Windows.Input.Cursors.Hand, FontWeight = FontWeights.SemiBold, Content = loc["Assist.PresetLoad"] };
                load.SetResourceReference(BorderBrushProperty, "Brush.Accent");
                load.SetResourceReference(BackgroundProperty, "Brush.Accent");
                load.SetResourceReference(ForegroundProperty, "Brush.Window");
                load.Click += (_, _) => AssistController.LoadPreset(mine); // the window follows through EditBarChanged
                buttons.Children.Add(load);
            }

            var save = new Button { Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(preset is null ? 0 : 4, 0, 0, 0), Content = loc["Assist.PresetSave"] };
            bool asked = false;
            var calm = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            calm.Tick += (_, _) =>
            {
                calm.Stop();
                asked = false;
                save.Content = loc["Assist.PresetSave"];
            };
            save.Click += (_, _) =>
            {
                if (preset is not null && !asked)
                {
                    // a full slot: one more click writes over it
                    asked = true;
                    save.Content = loc["Assist.PresetReplace"];
                    calm.Start();
                    return;
                }

                calm.Stop();
                AssistController.SavePreset(mine);
                DrawPresets(MeterSettings.Load());
            };
            buttons.Children.Add(save);
            body.Children.Add(buttons);
            var tile = new Border
            {
                Child = body, Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(slot == 1 ? 0 : 5, 0, slot == 3 ? 0 : 5, 0), CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(active ? 1.5 : 1),
            };
            tile.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
            tile.SetResourceReference(Border.BorderBrushProperty, active ? "Brush.Accent" : "Brush.Border");
            _presets.Children.Add(tile);
        }
    }

    /// <summary>The class line (7 November 2026: each class keeps its own presets and look, and the one played is followed by itself): the class the presets
    /// are of, with a choice to pick another while the game has not said yet; and the copy of another class's layout onto this one.</summary>
    private void DrawClassLines(MeterSettings settings, int cls)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        string ClassName(int digits) => Aion2Cooldowns.NameOfDigits(digits) is { } name ? AionDPS.Data.ClassCatalog.DisplayName(name, loc.Language) : "?";
        TextBlock Words(string text)
        {
            var t = new TextBlock { Text = text, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            t.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            return t;
        }

        _classLine.Children.Clear();
        _classLine.Children.Add(Words(loc["Assist.PresetsOf"]));
        var pick = new ComboBox { MinWidth = 150, VerticalAlignment = VerticalAlignment.Center, ToolTip = loc["Assist.PresetsOfTip"] };
        if (cls == 0)
        {
            pick.Items.Add(new ComboBoxItem { Content = loc["Assist.ClassUnknown"], Tag = 0 });
        }

        for (int digits = 11; digits <= 18; digits++)
        {
            pick.Items.Add(new ComboBoxItem { Content = ClassName(digits), Tag = digits });
        }

        pick.SelectedItem = pick.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == cls);
        pick.SelectionChanged += (_, _) =>
        {
            if (pick.SelectedItem is ComboBoxItem { Tag: int chosen } && chosen != 0 && chosen != Aion2Cooldowns.CurrentClass())
            {
                Aion2Cooldowns.SetClass(chosen); // the presets, the look and the "other skills" of that class; the game's own class wins as soon as it says
            }
        };
        _classLine.Children.Add(pick);

        _copyLine.Children.Clear();
        var others = AssistController.ClassesWithLayout(settings).Where(c => c != cls).ToList();
        if (cls == 0 || others.Count == 0)
        {
            _copyLine.Visibility = Visibility.Collapsed;
            return;
        }

        _copyLine.Visibility = Visibility.Visible;
        _copyLine.Children.Add(Words(loc["Assist.CopyFrom"]));
        var from = new ComboBox { MinWidth = 150, VerticalAlignment = VerticalAlignment.Center };
        foreach (int other in others)
        {
            from.Items.Add(new ComboBoxItem { Content = ClassName(other), Tag = other });
        }

        from.SelectedIndex = 0;
        _copyLine.Children.Add(from);
        var copy = new Button { Content = loc["Assist.CopyLayout"], Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(8, 0, 0, 0), ToolTip = loc["Assist.CopyLayoutTip"] };
        copy.Click += (_, _) =>
        {
            if (from.SelectedItem is ComboBoxItem { Tag: int source })
            {
                AssistController.CopyLayout(source); // the window follows through EditBarChanged
            }
        };
        _copyLine.Children.Add(copy);
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

    /// <summary>Changes one setting in the file; the overlays' controller reads it at its next tick.</summary>
    private static void Change(Action<MeterSettings> apply)
    {
        MeterSettings settings = MeterSettings.Load();
        apply(settings);
        settings.Save();
        AssistController.AskReload();
    }
}
