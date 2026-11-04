using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AionDPS.Schedule;

namespace AionDPS.Ui;

/// <summary>
/// The timers overlay, in the look of the Timers page and the website's timers: a compact list of what runs NOW (tinted in the event's colour, with a stripe
/// of it and the time left) and what starts within the lookahead (the next one when nothing does), each with its in-game icon framed in its colour, its name
/// and a countdown (see <see cref="EventSchedule"/>, <see cref="TimerBoard"/>). A transparent, always-on-top panel independent of the meter window: drag its
/// header to place it, the corner grip scales it, both are remembered. One timer a second moves the countdowns; the rows are laid out again only when an
/// event starts, ends or comes into the lookahead.
/// </summary>
public sealed class TimetableWindow : Window, IOverlayPlace
{
    private TimeSpan _lookahead = TimeSpan.FromMinutes(60);
    private const double PanelWidth = 260;
    private const double MinScale = 0.7, MaxScale = 2.0;

    /// <summary>The ground of a row that is not running (a light veil over the overlay's dark background).</summary>
    private static readonly Brush RowBack = Frozen(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));

    private readonly StackPanel _body = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly Border _panel;
    private readonly DockPanel _header;
    private readonly TextBlock _title;
    private readonly Border _close;
    private readonly OverlayEdgeResize _edges;
    private bool _locked = true;
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<(TextBlock Text, DateTime At)> _countdowns = new();
    private TimerBoard.Board? _board;
    private DateTime _rebuildAt = DateTime.MinValue, _shownAt = DateTime.MinValue;
    private Dictionary<string, TimetableEventSetting> _shown = new();
    private string _language = "";
    private bool _dirty = true;

    public TimetableWindow()
    {
        Title = "Aion DPS Timetable";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AltTabHidden.Apply(this);
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;

        var settings = MeterSettings.Load();
        ApplyOpacity(settings.TimetableOpacity ?? settings.OverlayOpacity);
        ApplyLookahead(settings.TimetableLookaheadMinutes);
        SetScale(settings.TimetableScale);

        var header = new DockPanel { Margin = new Thickness(2, 0, 0, 4), Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (!_locked && e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
                SavePosition();
            }
        };
        var close = new Border { Padding = new Thickness(3, 2, 3, 2), CornerRadius = new CornerRadius(3), Cursor = Cursors.Hand, Background = Brushes.Transparent,
            ToolTip = LocalizationManager.Instance["Main.Overlay.Close"] };
        var closeGlyph = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
        closeGlyph.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
        close.Child = closeGlyph;
        close.MouseEnter += (_, _) => close.Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        close.MouseLeave += (_, _) => close.Background = Brushes.Transparent;
        close.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            var s = MeterSettings.Load();
            s.ShowTimetable = false;
            s.Save();
            Hide();
        };
        DockPanel.SetDock(close, Dock.Right);
        var title = new TextBlock { FontWeight = FontWeights.Bold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Tag = "title" };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverlayText");
        header.Children.Add(close);
        header.Children.Add(title);
        _header = header;
        _title = title;
        _close = close;

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(_body);
        _panel = new Border
        {
            Width = PanelWidth, CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 5, 6, 5), Child = stack,
            LayoutTransform = _scale, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        };
        _panel.SetResourceReference(Border.BackgroundProperty, "Brush.OverlayBg");
        if (settings.TimetableWidth is > 0) { _panel.Width = settings.TimetableWidth.Value; }
        _edges = new OverlayEdgeResize(this, _panel, () => _scale.ScaleX, () => { }, SavePosition, SetScale, OverlayEdgeMode.Width);
        Content = _edges.Wrap(_panel);
        ApplyLocked(settings.TimetableLocked);

        Left = settings.TimetableLeft ?? SystemParameters.WorkArea.Right - PanelWidth - 40;
        Top = settings.TimetableTop ?? 120;
        _timer.Tick += (_, _) => Refresh();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                Refresh();
                _timer.Start();
            }
            else
            {
                _timer.Stop();
            }
        };
        Closed += (_, _) => _timer.Stop();
        Action<string> onReset = name => { if (name == Title) { Dispatcher.Invoke(ReloadPlace); } };
        OverlayPlaces.WasReset += onReset;
        Closed += (_, _) => OverlayPlaces.WasReset -= onReset;
        OverlayDocking.Attach(this, SavePosition);
    }

    /// <summary>Place, width and scale from the settings again (after "reset size and position").</summary>
    private void ReloadPlace()
    {
        var s = MeterSettings.Load();
        SetScale(s.TimetableScale);
        _panel.Width = s.TimetableWidth is > 0 ? s.TimetableWidth.Value : PanelWidth;
        Left = s.TimetableLeft ?? SystemParameters.WorkArea.Right - PanelWidth - 40;
        Top = s.TimetableTop ?? 120;
    }

    /// <summary>Locked: click-through, no close button, no handles. Unlocked: clickable and outlined, so it can be found, moved and scaled.</summary>
    public void ApplyLocked(bool locked)
    {
        _locked = locked;
        _close.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
        _edges.Show(!locked);
        _header.Cursor = locked ? Cursors.Arrow : Cursors.SizeAll;
        _panel.BorderThickness = new Thickness(1);
        _panel.BorderBrush = locked ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(0xFF, 0xBA, 0x42));
        ApplyClickThrough();
    }

    /// <summary>Takes the content out of a window that is never shown, to put it into a tool's own window (a live view); <see cref="Refresh"/> keeps filling it.</summary>
    public UIElement DetachView()
    {
        _isView = true;
        ApplyLocked(true);
        var content = (UIElement)Content;
        Content = null;
        return content;
    }

    // ---- the layout editor (OverlayLayout.cs) ----
    private bool _isView;

    public bool PlaceIsView => _isView;

    public Rect PlaceBounds
    {
        get
        {
            Size size = OverlayMeasure.SizeOf(this);
            return new Rect(double.IsFinite(Left) ? Left : 0, double.IsFinite(Top) ? Top : 0, size.Width, size.Height);
        }
    }

    public double PlaceScale => _scale.ScaleX;

    public void PlaceAt(double left, double top, bool save)
    {
        Left = left;
        Top = top;
        if (save)
        {
            SavePosition();
        }
    }

    public void PlaceScaleTo(double scale, bool save)
    {
        SetScale(scale);
        if (save)
        {
            SavePosition();
        }
    }

    /// <summary>Applies the lock only when it changed (the main window passes the settings on every refresh).</summary>
    public void ApplyLockedIfChanged(bool locked)
    {
        if (locked != _locked)
        {
            ApplyLocked(locked);
        }
    }

    private void ApplyClickThrough()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
        if (handle != IntPtr.Zero)
        {
            using var native = new NativeOverlay(this);
            native.SetClickThrough(_locked);
        }
    }

    /// <summary>How many minutes ahead an event is listed as "starts within ..." (Settings, 0 to 60; 0 lists nothing as "about to start").</summary>
    public void ApplyLookahead(int minutes)
    {
        TimeSpan lookahead = TimeSpan.FromMinutes(Math.Clamp(minutes, 0, 60));
        if (lookahead != _lookahead)
        {
            _lookahead = lookahead;
            _dirty = true;
        }
    }

    /// <summary>An event was switched on or off for the overlay (the Timers page): read the settings again and lay the rows out now.</summary>
    public void Reload()
    {
        _shownAt = DateTime.MinValue;
        _dirty = true;
        Refresh();
    }

    /// <summary>The overlay's dark background at the Settings' opacity (the same as the DPS overlay's).</summary>
    public void ApplyOpacity(double opacity)
    {
        opacity = Math.Clamp(double.IsFinite(opacity) ? opacity : 0.6, 0.0, 1.0);
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(opacity * 255), 0, 0, 0));
        brush.Freeze();
        Resources["Brush.OverlayBg"] = brush;
    }

    private void SetScale(double scale)
    {
        scale = Math.Clamp(double.IsFinite(scale) ? scale : 1.0, MinScale, MaxScale);
        _scale.ScaleX = scale;
        _scale.ScaleY = scale;
    }

    private void SavePosition()
    {
        var s = MeterSettings.Load();
        s.TimetableLeft = Left;
        s.TimetableTop = Top;
        s.TimetableScale = _scale.ScaleX;
        s.TimetableWidth = _panel.Width;
        s.Save();
    }

    /// <summary>Once a second: the countdowns move. The rows are laid out again when an event starts or ends, one comes into the lookahead, the
    /// language changes or an event was switched on or off (the settings are read again every few seconds, not every second).</summary>
    public void Refresh()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        DateTime now = Aion2.ServerClock.Now; // the PC's time, corrected by the server clock's offset
        if ((DateTime.UtcNow - _shownAt).Duration() >= TimeSpan.FromSeconds(5))
        {
            _shownAt = DateTime.UtcNow;
            Dictionary<string, TimetableEventSetting> shown = MeterSettings.Load().TimetableEvents;
            if (shown.Count != _shown.Count || shown.Any(kv => !_shown.TryGetValue(kv.Key, out TimetableEventSetting? was) || was.Show != kv.Value.Show))
            {
                _shown = shown;
                _dirty = true;
            }
        }

        if (loc.Language != _language)
        {
            _language = loc.Language;
            _title.Text = loc["Timers.Title"];
            _dirty = true;
        }

        TimerBoard.Board board = TimerBoard.At(now);
        if (_dirty || !ReferenceEquals(board, _board) || now >= _rebuildAt)
        {
            _dirty = false;
            _board = board;
            Rebuild(board, now);
        }

        foreach (var (text, at) in _countdowns)
        {
            text.Text = TimerLook.Countdown(at - now);
        }
    }

    /// <summary>The rows: what runs now (the soonest to end first), then what starts within the lookahead (the next one when nothing does), then the events
    /// open all day in one line. The events switched off for the overlay stay out.</summary>
    private void Rebuild(TimerBoard.Board board, DateTime now)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        _body.Children.Clear();
        _countdowns.Clear();
        var shown = board.Events.Where(x => !_shown.TryGetValue(x.Event.Id, out TimetableEventSetting? setting) || setting.Show).ToList();
        foreach (var (scheduled, phase) in shown.Where(x => x.Live is not null).Select(x => (x.Event, Phase: TimerLook.PhaseOf(x.Live!, now))).OrderBy(x => x.Phase.Until))
        {
            AddRow(scheduled, phase.Until, phase.Label + " · " + string.Format(CultureInfo.CurrentCulture, loc["Timers.Until"], TimerLook.Clock(phase.Until)), now, live: true);
        }

        var waiting = shown.Where(x => x.Live is null && x.Next is not null).OrderBy(x => x.Next!.Start).ToList();
        DateTime rebuildAt = DateTime.MaxValue;
        int soon = 0;
        foreach (EventNow x in waiting)
        {
            DateTime start = x.Next!.Start;
            if (start - now <= _lookahead)
            {
                AddRow(x.Event, start, TimerLook.When(start, now), now, live: false);
                soon++;
            }
            else if (start - _lookahead < rebuildAt)
            {
                rebuildAt = start - _lookahead; // it comes into the lookahead then
            }
        }

        if (soon == 0 && waiting.Count > 0)
        {
            // nothing within the lookahead: the next one all the same, a little quieter
            DateTime start = waiting[0].Next!.Start;
            AddRow(waiting[0].Event, start, TimerLook.When(start, now), now, live: false, quiet: true);
        }

        List<string> always = shown.Where(x => x.Event.Always).Select(x => TimerLook.Label(x.Event, _language)).ToList();
        if (always.Count > 0)
        {
            AddNote(string.Join(", ", always) + " · " + loc["Timetable.Always"]);
        }

        if (_body.Children.Count == 0)
        {
            AddNote(loc["Home.NoEvents"]);
        }

        _rebuildAt = rebuildAt;
    }

    /// <summary>Hides the content (the window stays, so the hotkey and the settings still see it as on) while the game is not in front or a game menu is open.</summary>
    public void ApplyGate(bool show)
    {
        if (Content is UIElement content)
        {
            content.Visibility = show ? Visibility.Visible : Visibility.Hidden;
        }
    }

    /// <summary>Puts the overlay back at the front of the topmost band. The main window calls this right after
    /// raising itself, once a second, so the two never take turns being in front (that made the timetable
    /// flicker through the meter). Not done from <see cref="Refresh"/>: its timer is offset from the main one.</summary>
    public void RaiseToFront()
    {
        if (IsVisible)
        {
            NativeOverlay.KeepOnTop(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        }
    }

    /// <summary>One event: its icon framed in its colour, its name and what it does (or when it starts), the countdown. A running one is tinted in its colour
    /// with a stripe of it, its countdown in it too.</summary>
    private void AddRow(ScheduledEvent scheduled, DateTime until, string sub, DateTime now, bool live, bool quiet = false)
    {
        string look = scheduled.Look;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(TimerLook.IconFrame(scheduled, 24));
        var names = new StackPanel { Margin = new Thickness(7, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = TimerLook.Label(scheduled, _language), FontWeight = FontWeights.Bold, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverlayText");
        names.Children.Add(name);
        var what = new TextBlock { Text = sub, FontSize = 9, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) };
        what.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
        names.Children.Add(what);
        Grid.SetColumn(names, 1);
        grid.Children.Add(names);
        var count = new TextBlock { Text = TimerLook.Countdown(until - now), FontSize = 12.5, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        if (live)
        {
            count.Foreground = TimerLook.BrushOf(look);
        }
        else
        {
            count.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        }

        Grid.SetColumn(count, 2);
        grid.Children.Add(count);
        _countdowns.Add((count, until));
        var row = new DockPanel();
        if (live)
        {
            var stripe = new Border { Width = 3, CornerRadius = new CornerRadius(1.5), Background = TimerLook.BrushOf(look), Margin = new Thickness(0, 1, 6, 1) };
            DockPanel.SetDock(stripe, Dock.Left);
            row.Children.Add(stripe);
        }

        row.Children.Add(grid);
        _body.Children.Add(new Border
        {
            Child = row, CornerRadius = new CornerRadius(4), Padding = new Thickness(live ? 3 : 5, 3, 7, 3), Margin = new Thickness(0, 1, 0, 1),
            Background = live ? TimerLook.BrushOf(look, 0x40) : RowBack, Opacity = quiet ? 0.8 : 1.0,
        });
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void AddNote(string text)
    {
        var note = new TextBlock { Text = text, FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 2, 2, 2) };
        note.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
        _body.Children.Add(note);
    }
}
