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
        DockPanel size = SliderRow(loc["Assist.Size"], 0.6, 2.0, settings.AssistCooldownScale, v => $"{v * 100:0} %", v => Change(s => s.AssistCooldownScale = Math.Round(v, 2)));
        _sizeSlider = size.Children.OfType<Slider>().FirstOrDefault();
        screen.Children.Add(size);
        _barWidthRow = SliderRow(loc["Assist.BarWidth"], 110, 320, settings.AssistBarWidth, v => $"{v:0}", v => Change(s => s.AssistBarWidth = Math.Round(v)));
        _barWidthRow.Visibility = settings.AssistCooldownStyle == CooldownArt.Tiles ? Visibility.Collapsed : Visibility.Visible; // only the bars have a width
        screen.Children.Add(_barWidthRow);
        screen.Children.Add(Divider());
        _togetherBox = Switch(loc["Assist.TogetherShort"], settings.AssistCooldownsTogether, on => Change(s => s.AssistCooldownsTogether = on));
        _togetherBox.ToolTip = loc["Assist.MoveTogetherTip"];
        screen.Children.Add(_togetherBox);
        CheckBox ready = Switch(loc["Assist.ReadyShort"], settings.AssistShowReady, on => Change(s => s.AssistShowReady = on));
        ready.ToolTip = loc["Assist.ShowReady"];
        screen.Children.Add(ready);
        page.Children.Add(Card(screen));

        // the voice that says a skill when it is back: which voice (also the boss warnings'), how loud, a test
        var voice = new StackPanel();
        voice.Children.Add(CardHead(loc["Assist.Sec.Voice"], loc["Assist.Sec.VoiceSub"], null));
        FrameworkElement picker = Field(loc["Assist.VoicePick"], VoicePicker(settings));
        picker.Margin = new Thickness(0, 12, 0, 0);
        voice.Children.Add(picker);
        DockPanel volume = SliderRow(loc["Assist.Volume"], 0, 100, settings.AssistVoiceVolume, v => $"{v:0} %", v => Change(s => s.AssistVoiceVolume = (int)Math.Round(v)));
        var test = new Button { Content = Glyphed("\uE768", loc["Assist.VoiceTest"]), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        test.Click += (_, _) =>
        {
            MeterSettings now = MeterSettings.Load();
            _voiceTest.Say(new[] { CooldownVoice.SampleName(now) }, now);
        };
        DockPanel.SetDock(test, Dock.Right);
        volume.Children.Insert(0, test);
        voice.Children.Add(volume);
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
        var combo = new ComboBox { MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = loc["Assist.VoicePickTip"] };
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

    /// <summary>A segment of the look's control: flat, raised with an accent edge when chosen.</summary>
    private static ControlTemplate SegmentTemplate() => (ControlTemplate)XamlReader.Parse(
        "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='RadioButton'>" +
        "<Border x:Name='Bd' Background='Transparent' BorderBrush='Transparent' BorderThickness='1' CornerRadius='6' Padding='14,4' Margin='0'>" +
        "<ContentPresenter x:Name='Text' HorizontalAlignment='Center' VerticalAlignment='Center' TextElement.Foreground='{DynamicResource Brush.TextMuted}'/></Border>" +
        "<ControlTemplate.Triggers>" +
        "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Bd' Property='Background' Value='{DynamicResource Brush.ControlHover}'/></Trigger>" +
        "<Trigger Property='IsChecked' Value='True'><Setter TargetName='Bd' Property='Background' Value='{DynamicResource Brush.ControlRaised}'/>" +
        "<Setter TargetName='Bd' Property='BorderBrush' Value='{DynamicResource Brush.Accent}'/>" +
        "<Setter TargetName='Text' Property='TextElement.Foreground' Value='{DynamicResource Brush.Text}'/></Trigger>" +
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

    /// <summary>A labelled slider; the value is written a moment after the last move.</summary>
    private static DockPanel SliderRow(string text, double min, double max, double value, Func<double, string> shown, Action<double> changed)
    {
        var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), Width = LabelWidth, TextTrimming = TextTrimming.CharacterEllipsis };
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

    /// <summary>Changes one setting in the file; the overlays' controller reads it at its next tick.</summary>
    private static void Change(Action<MeterSettings> apply)
    {
        MeterSettings settings = MeterSettings.Load();
        apply(settings);
        settings.Save();
        AssistController.AskReload();
    }
}
