using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace AionDPS.Ui;

/// <summary>
/// "Make it yours" (5 November 2026, the owner: "before they're able to access the app, give them options to change it so it fits them better ... with
/// previews of what they change and how it looks after: a small window that shows how it looks in the game, so they know before they go in game"): after the
/// terms and before the app, four steps - the meter's design, its columns, the overlays over the game, the app's colours - each beside a picture of a game
/// screen with what is switched on where it sits, and on the meter's steps the meter close up. Nothing is saved until the last step (Skip keeps everything
/// as it is); everything can be changed later on the pages it comes from. Shown once: a fresh install after the first-start wizard, an older one once after
/// the update that brought it.
/// </summary>
public partial class MainWindow
{
    /// <summary>The first start's look setup, once (see <see cref="LookSetup"/>).</summary>
    private void ShowLookSetupOnce()
    {
        if (Headless || MeterSettings.Load().LookSetupDone)
        {
            return;
        }

        var setup = new LookSetup(this, PrivacyConsent.Restricted);
        var window = new Window
        {
            Title = LocalizationManager.Instance["Setup.Title"], Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize, Content = setup.Root,
            Width = Math.Min(LookSetup.Width, SystemParameters.WorkArea.Width - 40), Height = Math.Min(LookSetup.Height + 40, SystemParameters.WorkArea.Height - 40),
        };
        window.SetResourceReference(BackgroundProperty, "Brush.Window");
        setup.Close = window.Close;
        window.Closed += (_, _) => setup.MarkDone();
        ThemedChrome.Apply(window);
        window.ShowDialog();
    }

    /// <summary>One step of the setup as a picture (the pictures of the build: render-tool setup-design, -columns, -overlays, -colours).</summary>
    internal FrameworkElement LookSetupPicture(int step, string? palette = null)
    {
        var setup = new LookSetup(this, restricted: false);
        setup.Root.Width = LookSetup.Width;
        setup.Root.Height = LookSetup.Height;
        if (palette is not null)
        {
            setup.Pick(palette);
        }

        setup.Show(step);
        return setup.Root;
    }

    /// <summary>The setup's steps, its preview pictures and what it saves.</summary>
    private sealed class LookSetup
    {
        public const double Width = 1240, Height = 820;

        private readonly MainWindow _main;
        private readonly bool _restricted;
        private readonly MeterSettings _draft = MeterSettings.Load();
        private readonly List<Action<MeterSettings>> _changes = new();
        private readonly TextBlock _stepText;
        private readonly TextBlock _title;
        private readonly TextBlock _lede;
        private readonly ContentControl _options = new() { Focusable = false };
        private readonly ContentControl _preview = new() { Focusable = false };
        private readonly Button _back;
        private readonly Button _next;
        private readonly List<PlayerRow> _samples = SampleMeterRows();
        private int _step;
        private string _palette;
        private bool _done;
        private ResourceDictionary? _themed;

        public Grid Root { get; } = new();

        public Action? Close { get; set; }

        private static LocalizationManager Loc => LocalizationManager.Instance;

        public LookSetup(MainWindow main, bool restricted)
        {
            _main = main;
            _restricted = restricted;
            // the colour set the app wears now: the built-in one, one of the sets below (saved by an earlier setup), or one of the player's own (none marked)
            _palette = _draft.Theme == ThemeManager.DefaultTheme ? "atreia"
                : Palettes.FirstOrDefault(p => p.File is { } f && _draft.Theme == ThemeFile.SelectionFor(f.Name!))?.Id ?? "";
            Root.SetResourceReference(Panel.BackgroundProperty, "Brush.Window");
            Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var head = new StackPanel { Margin = new Thickness(28, 20, 28, 12) };
            _stepText = ShellText("", "Brush.Accent", 11.5, bold: true);
            head.Children.Add(_stepText);
            _title = ShellText("", "Brush.Text", 22, bold: true);
            _title.Margin = new Thickness(0, 4, 0, 4);
            _title.FontFamily = ThemeManager.TitleFont;
            head.Children.Add(_title);
            _lede = Wrapped("", "Brush.TextMuted", 13);
            _lede.MaxWidth = 900;
            _lede.HorizontalAlignment = HorizontalAlignment.Left;
            head.Children.Add(_lede);
            Root.Children.Add(head);

            var body = new Grid { Margin = new Thickness(28, 0, 28, 0) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(420) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _options, Padding = new Thickness(0, 0, 14, 0) };
            body.Children.Add(scroll);
            var previewBox = new Viewbox { Child = _preview, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(20, 0, 0, 0) };
            Grid.SetColumn(previewBox, 1);
            body.Children.Add(previewBox);
            Grid.SetRow(body, 1);
            Root.Children.Add(body);

            var foot = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(28, 12, 28, 14) };
            foot.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            var bar = new DockPanel();
            var skip = new Button { Content = Loc["Setup.Skip"], Padding = new Thickness(14, 5, 14, 5), ToolTip = Loc["Setup.SkipTip"] };
            skip.Click += (_, _) =>
            {
                MarkDone();
                Close?.Invoke();
            };
            DockPanel.SetDock(skip, Dock.Left);
            bar.Children.Add(skip);
            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            _back = new Button { Content = Loc["Setup.Back"], Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(0, 0, 10, 0), MinWidth = 100 };
            _back.Click += (_, _) => Show(_step - 1);
            _next = new Button { Content = Loc["Setup.Next"], Padding = new Thickness(18, 5, 18, 5), MinWidth = 130, IsDefault = true };
            _next.Click += (_, _) =>
            {
                if (_step < 3)
                {
                    Show(_step + 1);
                }
                else
                {
                    Finish();
                    Close?.Invoke();
                }
            };
            right.Children.Add(_back);
            right.Children.Add(_next);
            bar.Children.Add(right);
            foot.Child = bar;
            Grid.SetRow(foot, 2);
            Root.Children.Add(foot);
            Show(0);
        }

        // ------------------------------------------------------------------ the steps

        public void Show(int step)
        {
            _step = Math.Clamp(step, 0, 3);
            string[] keys = { "Design", "Columns", "Overlays", "Colours" };
            string key = keys[_step];
            _stepText.Text = string.Format(CultureInfo.CurrentCulture, Loc["Setup.Step"], _step + 1, 4).ToUpper(CultureInfo.CurrentCulture);
            _title.Text = Loc["Setup." + key + ".Title"];
            _lede.Text = Loc["Setup." + key + ".Lede"];
            _back.Visibility = _step == 0 ? Visibility.Hidden : Visibility.Visible;
            _next.Content = _step == 3 ? Loc["Setup.Finish"] : Loc["Setup.Next"];
            _options.Content = _step switch
            {
                0 => DesignOptions(),
                1 => ColumnOptions(),
                2 => OverlayOptions(),
                _ => ColourOptions(),
            };
            Redraw();
        }

        private void Change(Action<MeterSettings> change)
        {
            change(_draft);
            _changes.Add(change);
            Redraw();
        }

        private void Redraw()
        {
            _preview.Content = _step switch
            {
                0 or 1 => MeterPreview(),
                2 => OverlayPreview(),
                _ => ColourPreview(),
            };
        }

        /// <summary>Saves what was chosen over the settings as they are now (only the parts changed here), and puts it on everything.</summary>
        private void Finish()
        {
            MeterSettings s = MeterSettings.Load();
            foreach (Action<MeterSettings> change in _changes)
            {
                change(s);
            }

            if (Palettes.FirstOrDefault(p => p.Id == _palette) is { File: { } file } palette)
            {
                try
                {
                    Directory.CreateDirectory(ThemeFile.Folder);
                    File.WriteAllText(ThemeFile.PathFor(file.Name!), System.Text.Json.JsonSerializer.Serialize(file, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                    s.Theme = ThemeFile.SelectionFor(file.Name!);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // the colours stay as they are
                }
            }
            else if (_paletteTouched && _palette == "atreia")
            {
                s.Theme = ThemeManager.DefaultTheme;
            }

            s.LookSetupDone = true;
            s.Save();
            _done = true;
            _main.ApplySettingsEverywhere(s);
            _main.ApplyMeterLook(s);
        }

        /// <summary>Closed or skipped: not shown again; nothing else changes.</summary>
        public void MarkDone()
        {
            if (_done)
            {
                return;
            }

            _done = true;
            MeterSettings s = MeterSettings.Load();
            s.LookSetupDone = true;
            s.Save();
        }

        // ---- step 1: the design

        private UIElement DesignOptions()
        {
            var stack = new StackPanel();
            stack.Children.Add(Heading(Loc["Look.Presets"]));
            var cards = new WrapPanel();
            foreach (MeterLookPreset preset in MeterLook.Presets)
            {
                cards.Children.Add(PresetCard(preset));
            }

            stack.Children.Add(cards);
            stack.Children.Add(Heading(Loc["Setup.Size"]));
            stack.Children.Add(Choices(MeterLook.Sizes, MeterLook.From(_draft).Size, id => Loc["Look.Size." + id], id => Change(s => s.MeterRowSize = id)));
            stack.Children.Add(Heading(Loc["Look.TextSize"]));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 8) };
            var slider = new Slider { Minimum = 85, Maximum = 130, Value = Math.Round(Math.Clamp(_draft.MeterTextScale, 0.85, 1.3) * 100), Width = 220, IsSnapToTickEnabled = true,
                TickFrequency = 5, VerticalAlignment = VerticalAlignment.Center };
            TextBlock value = ShellText($"{slider.Value:0} %", "Brush.TextMuted", 12.5);
            value.Margin = new Thickness(10, 0, 0, 0);
            slider.ValueChanged += (_, _) =>
            {
                value.Text = $"{slider.Value:0} %";
                double scale = slider.Value / 100.0;
                Change(s => s.MeterTextScale = scale);
            };
            row.Children.Add(slider);
            row.Children.Add(value);
            stack.Children.Add(row);
            stack.Children.Add(Check(Loc["Setup.Total"], _draft.MeterShowTotal, on => Change(s => s.MeterShowTotal = on)));
            stack.Children.Add(Note(Loc["Setup.Later"]));
            return stack;
        }

        private Border PresetCard(MeterLookPreset preset)
        {
            var stack = new StackPanel { Width = 176 };
            MeterLook look = preset.Thumbnail();
            var lines = new StackPanel { Width = 250 };
            Grid.SetIsSharedSizeScope(lines, true);
            foreach (PlayerRow row in _samples.Take(3))
            {
                lines.Children.Add(new MeterRowView { Look = look, DataContext = row, IsHitTestVisible = false });
            }

            var (back, edge) = PanelBrushes(preset.Panel, _draft.OverlayOpacity);
            var panel = new Border
            {
                Child = lines, Background = back, BorderBrush = edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(0, 7, 0, 5),
                ClipToBounds = true, LayoutTransform = new ScaleTransform(0.62, 0.62), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            };
            stack.Children.Add(new Border { Child = panel, Height = 78, CornerRadius = new CornerRadius(5), ClipToBounds = true, Padding = new Thickness(0, 6, 0, 0), Background = SceneBrush() });
            TextBlock name = ShellText(Loc[preset.NameKey], "Brush.Text", 12.5, bold: true);
            name.Margin = new Thickness(2, 6, 2, 0);
            stack.Children.Add(name);
            TextBlock note = ShellText(Loc[preset.NoteKey], "Brush.TextMuted", 11);
            note.Margin = new Thickness(2, 1, 2, 0);
            note.TextTrimming = TextTrimming.CharacterEllipsis;
            stack.Children.Add(note);
            var card = new Border { Child = stack, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(6), BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand, ToolTip = Loc[preset.NoteKey] };
            card.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
            card.SetResourceReference(Border.BorderBrushProperty, preset.Matches(_draft) ? "Brush.Accent" : "Brush.Border");
            card.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                Change(preset.ApplyTo);
                Show(_step); // the cards and the switches show the design's settings
            };
            return card;
        }

        // ---- step 2: the columns

        private UIElement ColumnOptions()
        {
            var stack = new StackPanel();
            HashSet<string> shown = MeterLook.From(_draft).Shown.ToHashSet();
            void Toggle(string id, bool on) => Change(s =>
            {
                var columns = (s.MeterColumns ?? MeterLook.DefaultColumns.ToList()).Where(c => c != id).ToList();
                if (on)
                {
                    columns.Add(id);
                }

                s.MeterColumns = columns;
            });

            UIElement Group(string title, IEnumerable<(string Id, string Label, string? Tip)> items)
            {
                var group = new StackPanel();
                group.Children.Add(Heading(title));
                var wrap = new WrapPanel();
                foreach (var (id, label, tip) in items)
                {
                    CheckBox box = Check(label, shown.Contains(id), on => Toggle(id, on));
                    box.ToolTip = tip;
                    box.MinWidth = 180;
                    wrap.Children.Add(box);
                }

                group.Children.Add(wrap);
                return group;
            }

            (string, string, string?) Column(string id)
            {
                MeterColumn column = MeterLook.Columns.First(c => c.Id == id);
                string title = column.TitleKey.Length > 0 ? Loc[column.TitleKey] : Loc["Look.Own." + id];
                return (id, title, Loc[column.TipKey]);
            }

            stack.Children.Add(Group(Loc["Setup.ColsName"], new (string, string, string?)[]
            {
                ("rank", Loc["Look.ColRank"], null), ("icon", Loc["Look.ColIcon"], null), ("cp", Loc["Look.ColCp"], null), ("classcolor", Loc["Look.ClassColor"], null),
            }));
            stack.Children.Add(Group(Loc["Setup.ColsOwn"], new[] { "dps", "share", "total" }.Select(id => (id, Loc["Look.Own." + id], (string?)Loc[MeterLook.Columns.First(c => c.Id == id).TipKey]))));
            stack.Children.Add(Group(Loc["Setup.ColsRates"], new[] { "rdps", "ndps", "given" }.Select(Column)));
            stack.Children.Add(Group(Loc["Setup.ColsHits"], new[] { "crit", "back", "front", "perfect", "double", "parry", "hits" }.Select(Column)));
            stack.Children.Add(Note(Loc["Setup.ColsNote"]));
            return stack;
        }

        // ---- step 3: the overlays

        private IEnumerable<OverlayDef> Overlays() => _main.OverlayDefs()
            .Where(d => !_restricted || d.Page is null || d.Page == "meter" || PrivacyConsent.RestrictedTools.Contains(d.Page));

        private UIElement OverlayOptions()
        {
            var stack = new StackPanel();
            foreach (OverlayDef def in Overlays())
            {
                var text = new StackPanel();
                text.Children.Add(ShellText(def.Name, "Brush.Text", 13, bold: true));
                TextBlock what = Wrapped(def.What, "Brush.TextMuted", 11.5);
                what.Margin = new Thickness(0, 1, 0, 0);
                what.MaxWidth = 360; // a check box gives its content all the room it asks for: the text wraps only below this
                text.Children.Add(what);
                OverlayDef d = def;
                CheckBox box = Check(text, def.Get(_draft), on => Change(s => d.Set(s, on)));
                box.Margin = new Thickness(0, 0, 0, 10);
                stack.Children.Add(box);
            }

            if (!_restricted)
            {
                var text = new StackPanel();
                text.Children.Add(ShellText(Loc["Cubes.Beam"], "Brush.Text", 13, bold: true));
                TextBlock what = Wrapped(Loc["Setup.BeamWhat"], "Brush.TextMuted", 11.5);
                what.Margin = new Thickness(0, 1, 0, 0);
                what.MaxWidth = 360;
                text.Children.Add(what);
                CheckBox box = Check(text, _draft.CubeBeam, on => Change(s => s.CubeBeam = on));
                box.Margin = new Thickness(0, 0, 0, 10);
                stack.Children.Add(box);
            }

            stack.Children.Add(Note(Loc["Setup.OverlaysNote"]));
            return stack;
        }

        // ---- step 4: the colours

        /// <summary>A colour set: Atreia is the built-in theme; the others are theme files like the Design page saves (its colours over the built-in one).</summary>
        private sealed record Palette(string Id, ThemeFile? File);

        private static ThemeFile Colours(string name, string background, string title, string panel, string control, string accent, string text) => new()
        {
            Name = name, Background = background, TitleBar = title, Panel = panel, Control = control, Switch = accent, Toggle = accent, Text = text,
        };

        private static readonly Palette[] Palettes =
        {
            new("atreia", null),
            new("midnight", Colours("Midnight", "#0F1220", "#0A0C18", "#161A2E", "#1C2138", "#6FB1FF", "#E8ECFF")),
            new("elyos", Colours("Elyos", "#14181F", "#0F1217", "#1B2029", "#222935", "#E0B64A", "#EEF2F8")),
            new("asmodian", Colours("Asmodian", "#1A1416", "#140F11", "#241A1E", "#2C2025", "#E8506A", "#F2EAEE")),
            new("graphite", Colours("Graphite", "#161617", "#111112", "#1E1E20", "#262628", "#C9CDD3", "#ECECEE")),
            new("forest", Colours("Forest", "#111613", "#0D110F", "#18201B", "#1F2922", "#6CC48A", "#E7F0E9")),
        };

        private bool _paletteTouched;

        /// <summary>Puts a colour set on the setup itself (the whole window shows it at once).</summary>
        public void Pick(string id)
        {
            _palette = Palettes.Any(p => p.Id == id) ? id : "atreia";
            _paletteTouched = true;
            if (_themed is not null)
            {
                Root.Resources.MergedDictionaries.Remove(_themed);
            }

            ThemeFile? file = Palettes.First(p => p.Id == _palette).File;
            _themed = ThemeManager.Build(ThemeManager.DefaultTheme, file?.ToOverrides());
            Root.Resources.MergedDictionaries.Add(_themed);
        }

        private UIElement ColourOptions()
        {
            var stack = new StackPanel();
            stack.Children.Add(Heading(Loc["Setup.Palettes"]));
            var cards = new WrapPanel();
            foreach (Palette palette in Palettes)
            {
                ResourceDictionary colours = ThemeManager.Build(ThemeManager.DefaultTheme, palette.File?.ToOverrides());
                Color Of(string key) => colours[key] is Color c ? c : Colors.Gray;
                var swatch = new Grid { Height = 54, Width = 150 };
                swatch.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
                swatch.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                swatch.Children.Add(new Border { Background = new SolidColorBrush(Of("Color.TitleBar")), CornerRadius = new CornerRadius(5, 0, 0, 5) });
                var main = new Border { Background = new SolidColorBrush(Of("Color.Window")), CornerRadius = new CornerRadius(0, 5, 5, 0), Padding = new Thickness(8, 7, 8, 7) };
                var inner = new StackPanel();
                inner.Children.Add(new Border { Height = 8, Width = 70, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Of("Color.Text")) { Opacity = 0.85 } });
                var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 7, 0, 0) };
                line.Children.Add(new Border { Height = 16, Width = 62, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Of("Color.Panel")) });
                Color switchColour = palette.File?.Switch is { } sw && ThemeManager.TryParse(sw) is Color parsed ? parsed : Of("Color.Accent");
                line.Children.Add(new Border { Height = 12, Width = 22, Margin = new Thickness(8, 0, 0, 0), CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(switchColour), VerticalAlignment = VerticalAlignment.Center });
                inner.Children.Add(line);
                main.Child = inner;
                Grid.SetColumn(main, 1);
                swatch.Children.Add(main);
                var card = new StackPanel();
                card.Children.Add(swatch);
                TextBlock name = ShellText(Loc["Setup.Palette." + palette.Id], "Brush.Text", 12.5, bold: true);
                name.Margin = new Thickness(2, 6, 0, 0);
                card.Children.Add(name);
                var tile = new Border { Child = card, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(6), BorderThickness = new Thickness(palette.Id == _palette ? 2 : 1),
                    Cursor = System.Windows.Input.Cursors.Hand };
                tile.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
                tile.SetResourceReference(Border.BorderBrushProperty, palette.Id == _palette ? "Brush.Accent" : "Brush.Border");
                string id = palette.Id;
                tile.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    Pick(id);
                    Show(_step);
                };
                cards.Children.Add(tile);
            }

            stack.Children.Add(cards);
            stack.Children.Add(Heading(Loc["Setup.Opacity"]));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 8) };
            var slider = new Slider { Minimum = 20, Maximum = 100, Value = Math.Round(Math.Clamp(_draft.OverlayOpacity, 0.2, 1.0) * 100), Width = 220, IsSnapToTickEnabled = true,
                TickFrequency = 5, VerticalAlignment = VerticalAlignment.Center };
            TextBlock value = ShellText($"{slider.Value:0} %", "Brush.TextMuted", 12.5);
            value.Margin = new Thickness(10, 0, 0, 0);
            slider.ValueChanged += (_, _) =>
            {
                value.Text = $"{slider.Value:0} %";
                double opacity = slider.Value / 100.0;
                Change(s => s.OverlayOpacity = opacity);
            };
            row.Children.Add(slider);
            row.Children.Add(value);
            stack.Children.Add(row);
            stack.Children.Add(Note(Loc["Setup.ColoursNote"]));
            return stack;
        }

        // ------------------------------------------------------------------ the previews

        private UIElement MeterPreview()
        {
            var stack = new StackPanel { Width = 760 };
            stack.Children.Add(Caption(Loc["Setup.InGame"]));
            stack.Children.Add(Screen(560, meterOnly: true));
            stack.Children.Add(Caption(Loc["Setup.CloseUp"], top: 14));
            var close = new Border { Padding = new Thickness(18), CornerRadius = new CornerRadius(8), Background = SceneBrush(), HorizontalAlignment = HorizontalAlignment.Left, Child = Meter() };
            stack.Children.Add(close);
            return stack;
        }

        private UIElement OverlayPreview()
        {
            var stack = new StackPanel { Width = 760 };
            stack.Children.Add(Caption(Loc["Setup.InGame"]));
            stack.Children.Add(Screen(760, meterOnly: false));
            TextBlock note = Wrapped(Loc["Setup.PreviewNote"], "Brush.TextSubtle", 11.5);
            note.Margin = new Thickness(0, 10, 0, 0);
            stack.Children.Add(note);
            return stack;
        }

        private UIElement ColourPreview()
        {
            var stack = new StackPanel { Width = 760 };
            stack.Children.Add(Caption(Loc["Setup.TheApp"]));
            stack.Children.Add(AppMock());
            stack.Children.Add(Caption(Loc["Setup.InGame"], top: 14));
            stack.Children.Add(Screen(520, meterOnly: false));
            return stack;
        }

        /// <summary>The meter as the draft has it: its head, the group total, the sample players, the status line.</summary>
        private FrameworkElement Meter()
        {
            MeterLook look = MeterLook.From(_draft);
            var panel = new StackPanel();
            var head = new DockPanel { Margin = new Thickness(10, 6, 8, 2), Height = 24 };
            TextBlock buttons = ShellText("      ", "Brush.TextSubtle", 11);
            buttons.FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
            buttons.Opacity = _draft.MeterHeaderButtons == "hover" ? 0.25 : 1;
            DockPanel.SetDock(buttons, Dock.Right);
            head.Children.Add(buttons);
            TextBlock clock = ShellText("1:51", "Brush.OverlayText", 12.5, bold: true);
            clock.Margin = new Thickness(0, 0, 8, 0);
            head.Children.Add(clock);
            head.Children.Add(ShellText(Loc["Look.SampleBoss"], "Brush.TextMuted", 12));
            panel.Children.Add(head);
            if (_draft.MeterShowTotal)
            {
                var total = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 1, 10, 5) };
                TextBlock big = ShellText(PlayerRow.Compact((long)_samples.Sum(r => r.Dps ?? 0)), "Brush.Accent", 24);
                big.FontWeight = FontWeights.SemiBold;
                total.Children.Add(big);
                TextBlock unit = ShellText("DPS", "Brush.TextMuted", 12.5, bold: true);
                unit.Margin = new Thickness(6, 0, 0, 4);
                unit.VerticalAlignment = VerticalAlignment.Bottom;
                total.Children.Add(unit);
                panel.Children.Add(total);
            }

            var lines = new StackPanel();
            Grid.SetIsSharedSizeScope(lines, true);
            if (look.Titles)
            {
                lines.Children.Add(new MeterRowView { Look = look, IsHeader = true, IsHitTestVisible = false });
            }

            foreach (PlayerRow row in _draft.MeterBestLast ? _samples.AsEnumerable().Reverse() : _samples)
            {
                lines.Children.Add(new MeterRowView { Look = look, DataContext = row, IsHitTestVisible = false });
            }

            lines.Margin = new Thickness(0, 0, 0, 4);
            panel.Children.Add(lines);
            var (back, edge) = PanelBrushes(MeterLook.PanelOf(_draft.OverlayLook), _draft.OverlayOpacity);
            return new Border { Child = panel, Width = look.PanelWidth, Background = back, BorderBrush = edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), ClipToBounds = true };
        }

        /// <summary>
        /// A game screen (1920 x 1080, shown <paramref name="width"/> wide): sky, hills and a path, the game's minimap with a piece of a real map, its skill bar with
        /// real skill icons, its chat; over it every overlay switched on in the draft at its usual place - the meter as it will look, the others as panels of the
        /// overlay's colour with their name - and the cube beam when it is on.
        /// </summary>
        private FrameworkElement Screen(double width, bool meterOnly)
        {
            var canvas = new Canvas { Width = 1920, Height = 1080, ClipToBounds = true };
            DrawScene(canvas);
            if (!meterOnly && _draft.CubeBeam && !_restricted)
            {
                Grid beam = CubeBeam.Beam(out TextBlock metres);
                metres.Text = "~34 m";
                beam.Height = 1080;
                beam.Width = 150;
                Place(canvas, beam, 1010, 0);
            }

            foreach (OverlayDef def in Overlays())
            {
                bool on = def.Get(_draft);
                string key = def.Page ?? "hotkeys";
                if (!on || (meterOnly && key != "meter"))
                {
                    continue;
                }

                if (key == "meter")
                {
                    FrameworkElement meter = Meter();
                    Place(canvas, meter, 1490, 300);
                    continue;
                }

                if (Spots.TryGetValue(key, out var spot))
                {
                    Place(canvas, Ghost(def.Name, spot.Kind, spot.W, spot.H), spot.X, spot.Y);
                }
            }

            var frame = new Border
            {
                Child = new Viewbox { Child = canvas, Stretch = Stretch.Uniform }, Width = width, Height = width * 9 / 16, CornerRadius = new CornerRadius(8), ClipToBounds = true,
                BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left,
            };
            frame.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            return frame;
        }

        /// <summary>Where each overlay sits on the 1920 x 1080 picture and what its panel shows (by the page its settings are on).</summary>
        private static readonly Dictionary<string, (double X, double Y, double W, double H, string Kind)> Spots = new()
        {
            ["combat"] = (760, 60, 400, 140, "boss"),
            ["assist." + AssistWindow.CooldownsTab] = (690, 836, 540, 96, "icons"),
            ["assist." + AssistWindow.BuffsTab] = (720, 774, 480, 54, "small"),
            ["timetable"] = (1230, 40, 340, 180, "lines"),
            ["boss"] = (1230, 236, 340, 120, "lines"),
            ["petmap"] = (30, 290, 320, 320, "map"),
            ["petmap.pets"] = (30, 626, 320, 64, "pill"),
            ["loot"] = (30, 706, 320, 150, "lines"),
            ["petmap.gather"] = (1230, 372, 340, 130, "lines"),
            ["checklist"] = (30, 40, 320, 234, "lines"),
            ["craft"] = (1490, 720, 400, 130, "lines"),
            ["network"] = (1640, 990, 250, 52, "pill"),
            ["hotkeys"] = (1230, 518, 340, 150, "lines"),
        };

        private static void Place(Canvas canvas, UIElement element, double x, double y)
        {
            Canvas.SetLeft(element, x);
            Canvas.SetTop(element, y);
            canvas.Children.Add(element);
        }

        /// <summary>An overlay other than the meter: a panel of the overlay's colour, its name, and a hint of what it shows.</summary>
        private FrameworkElement Ghost(string name, string kind, double w, double h)
        {
            var (back, edge) = PanelBrushes("Themed", _draft.OverlayOpacity);
            var stack = new StackPanel { Margin = new Thickness(14, 10, 14, 10) };
            TextBlock title = ShellText(name, "Brush.OverlayText", 17, bold: true);
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            stack.Children.Add(title);
            var faint = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
            switch (kind)
            {
                case "boss":
                    var hp = new Grid { Height = 22, Margin = new Thickness(0, 10, 0, 0) };
                    hp.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72, GridUnitType.Star) });
                    hp.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28, GridUnitType.Star) });
                    hp.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(0xC8, 0x3A, 0x3A)), CornerRadius = new CornerRadius(4, 0, 0, 4) });
                    var rest = new Border { Background = new SolidColorBrush(Color.FromArgb(0x50, 0, 0, 0)), CornerRadius = new CornerRadius(0, 4, 4, 0) };
                    Grid.SetColumn(rest, 1);
                    hp.Children.Add(rest);
                    stack.Children.Add(hp);
                    var cast = new Border { Height = 14, Margin = new Thickness(0, 12, 120, 0), CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromRgb(0xE0, 0xB0, 0x50)) };
                    stack.Children.Add(cast);
                    break;
                case "icons":
                case "small":
                    var icons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
                    int size = kind == "icons" ? 44 : 26;
                    for (int i = 0; i < (kind == "icons" ? 8 : 12); i++)
                    {
                        var cell = new Grid { Width = size, Height = size, Margin = new Thickness(0, 0, 6, 0) };
                        cell.Children.Add(new Image { Source = SkillIcon(i + (kind == "icons" ? 0 : 20)), Stretch = Stretch.UniformToFill });
                        if (kind == "icons" && i % 3 == 1)
                        {
                            cell.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)) });
                            cell.Children.Add(new TextBlock { Text = (4 + i).ToString(CultureInfo.CurrentCulture), Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 18,
                                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                        }

                        icons.Children.Add(cell);
                    }

                    stack.Children.Clear(); // the skills alone, no title (as in the game)
                    stack.Children.Add(icons);
                    break;
                case "map":
                    var map = new Grid { Height = h - 60, Margin = new Thickness(0, 8, 0, 0), ClipToBounds = true };
                    if (Asset("maps", "altgard", "2_2.jpg") is { } tile)
                    {
                        map.Children.Add(new Image { Source = tile, Stretch = Stretch.UniformToFill, Opacity = 0.9, Width = 700, Height = 700, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                    }

                    var dots = new Canvas();
                    var palette = new[] { Color.FromRgb(0xF2, 0xC0, 0x4A), Color.FromRgb(0x5B, 0xB8, 0xF0), Color.FromRgb(0x6C, 0xD4, 0x8A), Color.FromRgb(0xE8, 0x6A, 0xA8) };
                    for (int i = 0; i < 9; i++)
                    {
                        var dot = new Ellipse { Width = 14, Height = 14, Fill = new SolidColorBrush(palette[i % 4]), Stroke = Brushes.White, StrokeThickness = 1.5 };
                        Canvas.SetLeft(dot, 20 + (i * 53 % 250));
                        Canvas.SetTop(dot, 20 + (i * 71 % 190));
                        dots.Children.Add(dot);
                    }

                    map.Children.Add(dots);
                    stack.Children.Add(map);
                    break;
                case "pill":
                    break;
                default:
                    for (int i = 0; i < Math.Max(1, (int)((h - 50) / 24)); i++)
                    {
                        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 9, 0, 0) };
                        line.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(Color.FromArgb(0x90, 0xDC, 0xB8, 0x77)), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
                        line.Children.Add(new Border { Height = 9, Width = 90 + (i * 47 % 120), CornerRadius = new CornerRadius(4), Background = faint });
                        stack.Children.Add(line);
                    }

                    break;
            }

            return new Border { Child = stack, Width = w, Height = h, Background = back, BorderBrush = edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), ClipToBounds = true };
        }

        private void DrawScene(Canvas canvas)
        {
            canvas.Children.Add(new Rectangle
            {
                Width = 1920, Height = 1080,
                Fill = new LinearGradientBrush(new GradientStopCollection
                {
                    new(Color.FromRgb(0x4E, 0x6F, 0x98), 0), new(Color.FromRgb(0x9F, 0xB6, 0xC6), 0.40), new(Color.FromRgb(0xD5, 0xD6, 0xC4), 0.50), new(Color.FromRgb(0x8C, 0x8A, 0x5C), 0.52),
                    new(Color.FromRgb(0x5E, 0x66, 0x3E), 0.75), new(Color.FromRgb(0x36, 0x3D, 0x26), 1),
                }, new Point(0, 0), new Point(0, 1)),
            });
            // far mountains, near hills, a path to the horizon, trees
            canvas.Children.Add(new Polygon { Points = Points("0,560 160,430 300,500 470,380 650,470 820,400 980,500 1150,360 1330,470 1500,410 1700,490 1920,400 1920,570 0,570"), Fill = new SolidColorBrush(Color.FromArgb(0xB0, 0x7A, 0x8C, 0x9E)) });
            canvas.Children.Add(new Polygon { Points = Points("0,600 220,520 420,580 640,530 860,590 1100,540 1340,600 1560,530 1920,590 1920,640 0,640"), Fill = new SolidColorBrush(Color.FromArgb(0xE0, 0x6B, 0x74, 0x48)) });
            canvas.Children.Add(new Polygon { Points = Points("880,560 1040,560 1380,1080 540,1080"), Fill = new SolidColorBrush(Color.FromArgb(0x70, 0xC9, 0xB3, 0x82)) });
            foreach (var (x, y, r) in new[] { (180.0, 640.0, 70.0), (300, 700, 95), (1640, 650, 80), (1780, 720, 110), (90, 760, 120) })
            {
                canvas.Children.Add(new Ellipse { Width = r * 1.4, Height = r * 2, Fill = new SolidColorBrush(Color.FromArgb(0xD8, 0x2C, 0x3A, 0x22)) });
                Canvas.SetLeft(canvas.Children[^1], x - (r * 0.7));
                Canvas.SetTop(canvas.Children[^1], y - r);
            }

            // the game's own interface: the minimap (a piece of a real map, the player's arrow and the view cone), the skill bar, health and mana, the chat
            var minimap = new Grid { Width = 250, Height = 170, ClipToBounds = true };
            if (Asset("maps", "altgard", "3_2.jpg") is { } tile)
            {
                // a part of a map tile, close up as the minimap shows it
                minimap.Children.Add(new Image { Source = tile, Stretch = Stretch.UniformToFill, Width = 640, Height = 640, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            }
            else
            {
                minimap.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(0x8A, 0x78, 0x50)) });
            }

            minimap.Children.Add(new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M125,85 L40,0 L210,0 Z"), Fill = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xF4, 0xD6)), Stroke = new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)), StrokeThickness = 1.5,
            });
            minimap.Children.Add(new Ellipse { Width = 22, Height = 22, Fill = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xF4, 0xD6)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            Place(canvas, new Border { Child = minimap, BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, 0xE8, 0xD8, 0xB0)), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(4) }, 1640, 40);
            var bars = new StackPanel { Orientation = Orientation.Horizontal };
            bars.Children.Add(new Border { Width = 300, Height = 12, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromRgb(0xC0, 0x3A, 0x34)), Margin = new Thickness(0, 0, 20, 0) });
            bars.Children.Add(new Border { Width = 300, Height = 12, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x7C, 0xD0)) });
            Place(canvas, bars, 650, 950);
            var hotbar = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < 11; i++)
            {
                hotbar.Children.Add(new Border
                {
                    Width = 52, Height = 52, Margin = new Thickness(0, 0, 4, 0), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, 0x10, 0x10, 0x10)),
                    Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x14, 0x14, 0x18)), Child = new Image { Source = SkillIcon(i + 8), Stretch = Stretch.UniformToFill },
                });
            }

            Place(canvas, hotbar, 652, 972);
            var chat = new StackPanel { Width = 480 };
            foreach (int len in new[] { 300, 380, 220, 340 })
            {
                chat.Children.Add(new Border { Height = 9, Width = len, CornerRadius = new CornerRadius(4), Margin = new Thickness(14, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
                    Background = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)) });
            }

            Place(canvas, new Border { Child = chat, Height = 150, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.FromArgb(0x50, 0, 0, 0)) }, 20, 900);
        }

        /// <summary>A small app window in the chosen colours: its title bar, the column of pages and a page with a card and a switch.</summary>
        private UIElement AppMock()
        {
            var window = new Grid { Width = 560, Height = 250 };
            window.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
            window.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var title = new Border { CornerRadius = new CornerRadius(8, 8, 0, 0), Padding = new Thickness(12, 0, 12, 0) };
            title.SetResourceReference(Border.BackgroundProperty, "Brush.TitleBar");
            TextBlock brand = ShellText("ATREIA  APP", "Brush.Accent", 12, bold: true);
            brand.FontFamily = ThemeManager.TitleFont;
            brand.VerticalAlignment = VerticalAlignment.Center;
            title.Child = brand;
            window.Children.Add(title);
            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var side = new StackPanel { Margin = new Thickness(8, 10, 8, 8) };
            string[] pages = { Loc["Setup.Mock.Home"], Loc["Shell.OvMeter"], Loc["Setup.Mock.Fights"], Loc["Setup.Mock.Map"], Loc["Shell.Overlays"] };
            for (int i = 0; i < pages.Length; i++)
            {
                var item = new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(10, 5, 8, 5), Margin = new Thickness(0, 0, 0, 3) };
                if (i == 1)
                {
                    item.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
                }

                item.Child = ShellText(pages[i], i == 1 ? "Brush.Text" : "Brush.TextMuted", 12);
                side.Children.Add(item);
            }

            var sideBack = new Border { Child = side, CornerRadius = new CornerRadius(0, 0, 0, 8) };
            sideBack.SetResourceReference(Border.BackgroundProperty, "Brush.Window");
            body.Children.Add(sideBack);
            var page = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };
            TextBlock heading = ShellText(Loc["Shell.OvMeter"].ToUpper(CultureInfo.CurrentCulture), "Brush.Text", 15, bold: true);
            heading.FontFamily = ThemeManager.TitleFont;
            page.Children.Add(heading);
            var card = new Border { CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 10, 0, 0) };
            card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
            card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            var line = new DockPanel();
            var toggle = new Border { Width = 34, Height = 19, CornerRadius = new CornerRadius(10), Padding = new Thickness(2) };
            toggle.SetResourceReference(Border.BackgroundProperty, "Brush.Switch");
            toggle.Child = new Ellipse { Width = 15, Height = 15, Fill = new SolidColorBrush(Color.FromRgb(0x1A, 0x12, 0x05)), HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(toggle, Dock.Right);
            line.Children.Add(toggle);
            line.Children.Add(ShellText(Loc["Setup.Mock.Switch"], "Brush.Text", 12.5));
            card.Child = line;
            page.Children.Add(card);
            var second = new Border { CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 8, 0, 0) };
            second.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
            second.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            var button = new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Left };
            button.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
            button.Child = ShellText(Loc["Setup.Mock.Button"], "Brush.Text", 12);
            second.Child = button;
            page.Children.Add(second);
            var pageBack = new Border { Child = page, CornerRadius = new CornerRadius(0, 0, 8, 0) };
            pageBack.SetResourceReference(Border.BackgroundProperty, "Brush.Window");
            Grid.SetColumn(pageBack, 1);
            body.Children.Add(pageBack);
            Grid.SetRow(body, 1);
            window.Children.Add(body);
            var frame = new Border { Child = window, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left };
            frame.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            return frame;
        }

        // ------------------------------------------------------------------ small pieces

        /// <summary>The overlays' panel colours (as the meter look page draws them), from the setup's own theme.</summary>
        private (Brush Back, Brush Edge) PanelBrushes(string panel, double opacity)
        {
            opacity = Math.Clamp(double.IsFinite(opacity) ? opacity : 0.92, 0.2, 1.0);
            Color themed = Root.TryFindResource("Color.OverlayBg") is Color c ? c : Color.FromRgb(0x12, 0x10, 0x16);
            Color back = panel switch
            {
                "Clear" => Color.FromArgb((byte)Math.Round(opacity * 0.55 * 255), 0x08, 0x08, 0x0A),
                "Graphite" => Color.FromArgb((byte)Math.Round(opacity * 255), 0x1F, 0x20, 0x24),
                "Black" => Color.FromArgb((byte)Math.Round(opacity * 255), 0x11, 0x11, 0x13),
                _ => Color.FromArgb((byte)Math.Round(opacity * 255), themed.R, themed.G, themed.B),
            };
            Color line = panel switch
            {
                "Clear" => Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF),
                "Graphite" => Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF),
                "Black" => Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF),
                _ => Color.FromArgb(0x33, 0xDC, 0xB8, 0x77),
            };
            return (new SolidColorBrush(back), new SolidColorBrush(line));
        }

        private static Brush SceneBrush() => new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromRgb(0x6E, 0x88, 0xA6), 0), new(Color.FromRgb(0xA8, 0xB4, 0xA8), 0.45), new(Color.FromRgb(0x6A, 0x70, 0x48), 0.5), new(Color.FromRgb(0x3E, 0x46, 0x2C), 1),
        }, new Point(0, 0), new Point(0, 1));

        private static readonly Dictionary<string, ImageSource?> Assets = new();

        /// <summary>A picture the app ships (assets/aion2/...), null when it isn't there.</summary>
        private static ImageSource? Asset(params string[] parts)
        {
            string path = System.IO.Path.Combine(new[] { AppContext.BaseDirectory, "assets", "aion2" }.Concat(parts).ToArray());
            if (Assets.TryGetValue(path, out ImageSource? had))
            {
                return had;
            }

            ImageSource? image = null;
            try
            {
                if (File.Exists(path))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(path, UriKind.Absolute);
                    bitmap.DecodePixelWidth = 512;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    image = bitmap;
                }
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or ArgumentException)
            {
                image = null;
            }

            Assets[path] = image;
            return image;
        }

        private static string[]? _skillIcons;

        private static ImageSource? SkillIcon(int index)
        {
            if (_skillIcons is null)
            {
                try
                {
                    string folder = System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "skills", "icons");
                    _skillIcons = Directory.Exists(folder) ? Directory.GetFiles(folder, "ICON_TE_SKILL_0*.png").Order(StringComparer.Ordinal).Select(System.IO.Path.GetFileName).Select(n => n!).ToArray() : Array.Empty<string>();
                }
                catch (IOException)
                {
                    _skillIcons = Array.Empty<string>();
                }
            }

            return _skillIcons.Length == 0 ? null : Asset("skills", "icons", _skillIcons[index % _skillIcons.Length]);
        }

        private static PointCollection Points(string text) => PointCollection.Parse(text);

        private static TextBlock Heading(string text)
        {
            TextBlock t = ShellText(text.ToUpper(CultureInfo.CurrentCulture), "Brush.TextMuted", 11, bold: true);
            t.Margin = new Thickness(0, 12, 0, 6);
            return t;
        }

        private static TextBlock Caption(string text, double top = 0)
        {
            TextBlock t = ShellText(text.ToUpper(CultureInfo.CurrentCulture), "Brush.TextMuted", 11, bold: true);
            t.Margin = new Thickness(0, top, 0, 6);
            return t;
        }

        private static TextBlock Note(string text)
        {
            TextBlock t = Wrapped(text, "Brush.TextSubtle", 11.5);
            t.Margin = new Thickness(0, 10, 0, 4);
            return t;
        }

        private static CheckBox Check(object content, bool on, Action<bool> set)
        {
            var box = new CheckBox { IsChecked = on, Content = content is string s ? ShellText(s, "Brush.Text", 13) : content, Margin = new Thickness(0, 0, 14, 8), VerticalContentAlignment = VerticalAlignment.Top };
            box.Click += (_, _) => set(box.IsChecked == true);
            return box;
        }

        private UIElement Choices(IReadOnlyList<string> ids, string current, Func<string, string> label, Action<string> pick)
        {
            var wrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 6) };
            foreach (string id in ids)
            {
                bool on = id == current;
                var tile = new Border { Child = ShellText(label(id), "Brush.Text", 13), CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 8, 8), MinWidth = 96,
                    Padding = on ? new Thickness(9, 6, 9, 6) : new Thickness(10, 7, 10, 7), BorderThickness = new Thickness(on ? 2 : 1), Cursor = System.Windows.Input.Cursors.Hand };
                tile.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
                tile.SetResourceReference(Border.BorderBrushProperty, on ? "Brush.Accent" : "Brush.Border");
                string chosen = id;
                tile.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    pick(chosen);
                    Show(_step);
                };
                wrap.Children.Add(tile);
            }

            return wrap;
        }
    }
}
