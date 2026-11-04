using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AionDPS.Schedule;

namespace AionDPS.Ui;

/// <summary>
/// The Timers page (Tools → Timers), built like the website's event timers: today at a glance on a 24-hour ring (a coloured ring per event, the needle at
/// now, the next big event in the middle), what is happening now, the next start of each event with its in-game icon, a countdown and a bell for its
/// reminder, the daily and weekly resets with the server's clock, the week's weekly events, and every event with its overlay switch and reminder sound.
/// Times are the player's own; the events run on the server clock of the own server's region (EventSchedule). The page has no timer of its own: its
/// ModulePage calls <see cref="Tick"/> once a second, which moves the countdowns; the lists are laid out again only when an event starts or ends
/// (TimerBoard), the ring is drawn again twice a minute. Every change of a switch is kept at once (a slider's a moment later).
/// </summary>
public sealed class TimersPage : Grid
{
    private const double DialSize = 360;

    /// <summary>The website's bell (an outline; filled when the reminder is on).</summary>
    private static readonly Geometry BellGeometry = MakeBell();

    private readonly Action? _overlayChanged;
    private readonly StackPanel _column = new() { MaxWidth = 1120 };
    private readonly TextBlock _lede = Wrapped("", "Brush.TextMuted", 13);
    private readonly Grid _hero = new();
    private readonly Border _dialCard;
    private readonly StackPanel _side = new();
    private readonly TimersDial _dial = new() { Width = DialSize, Height = DialSize };
    private readonly StackPanel _center = new() { Width = DialSize * 0.4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    private readonly TextBlock _centerName = Centered("", "Brush.Text", 14, bold: true);
    private readonly TextBlock _centerCount = Centered("", "Brush.Accent", 30, bold: true);
    private readonly TextBlock _centerWhen = Centered("", "Brush.TextSubtle", 11.5);
    private readonly WrapPanel _key = new() { Margin = new Thickness(0, 12, 0, 0), MaxWidth = DialSize };
    private readonly StackPanel _live = new();
    private readonly StackPanel _next = new();
    private readonly TextBlock _dailyCount = TimerLook.Text("", "Brush.Text", 26, bold: true);
    private readonly TextBlock _dailyAt = Wrapped("", "Brush.TextSubtle", 12);
    private readonly TextBlock _weeklyCount = TimerLook.Text("", "Brush.Text", 26, bold: true);
    private readonly TextBlock _weeklyAt = Wrapped("", "Brush.TextSubtle", 12);
    private readonly TextBlock _serverClock = TimerLook.Text("", "Brush.Text", 26, bold: true);
    private readonly TextBlock _serverSub = Wrapped("", "Brush.TextSubtle", 12);
    private readonly Border _weekHost = new();
    private readonly UniformGrid _cards = new() { Columns = 2 };
    private readonly CheckBox _notify = new() { VerticalContentAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 0, 8) };
    private readonly Grid _minutesRow = new() { Margin = new Thickness(26, 0, 0, 14), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Slider _minutes = new() { Minimum = 0, Maximum = 60, TickFrequency = 1, IsSnapToTickEnabled = true, SmallChange = 1, LargeChange = 5, Width = 220, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 10, 0) };
    private readonly TextBlock _minutesValue = TimerLook.Text("", "Brush.Text", 12.5);
    private readonly List<(TextBlock Text, DateTime At)> _countdowns = new();
    private readonly List<(ProgressBar Bar, DateTime Start, DateTime End)> _bars = new();
    private readonly List<(string Id, Border Bell, System.Windows.Shapes.Path Glyph)> _bells = new();
    private readonly List<EventControls> _controls = new();
    private readonly Dictionary<string, Action<MeterSettings>> _pending = new(StringComparer.Ordinal);
    private readonly System.Windows.Threading.DispatcherTimer _saveSoon = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private TimerBoard.Board? _board;
    private IReadOnlyList<ScheduledEvent>? _table;
    private DateTime _dialAt = DateTime.MinValue, _weekDay = DateTime.MinValue, _syncedAt = DateTime.MinValue;
    private long _stripMinute = -1;
    private bool _syncing, _narrow, _laidOut;

    /// <summary>The controls of one event's card, kept to follow the settings when they change elsewhere (the Settings window, a bell).</summary>
    private sealed record EventControls(ScheduledEvent Event, CheckBox Show, ComboBox? Sound, Slider? Volume, Button? Test, TextBlock NextLine);

    /// <param name="overlayChanged">Called after an event's switch or sound was kept, so the overlay shows the change at once (null where there is none).</param>
    public TimersPage(Action? overlayChanged = null)
    {
        _overlayChanged = overlayChanged;
        LocalizationManager loc = LocalizationManager.Instance;
        Children.Add(_column);

        _lede.Margin = new Thickness(0, 0, 12, 14);
        _lede.MaxWidth = 820;
        _lede.HorizontalAlignment = HorizontalAlignment.Left;
        _column.Children.Add(_lede);

        // today at a glance: the ring with the next big event in its middle, the key under it; beside it what is on now and what starts next
        TextBlock centerHead = Centered(loc["Timers.NextBig"], "Brush.TextMuted", 11.5);
        _centerName.Margin = new Thickness(0, 2, 0, 0);
        _centerCount.Margin = new Thickness(0, 2, 0, 0);
        _center.Children.Add(centerHead);
        _center.Children.Add(_centerName);
        _center.Children.Add(_centerCount);
        _center.Children.Add(_centerWhen);
        var dialGrid = new Grid { Width = DialSize, Height = DialSize, HorizontalAlignment = HorizontalAlignment.Left };
        dialGrid.Children.Add(_dial);
        dialGrid.Children.Add(_center);
        var dialStack = new StackPanel();
        dialStack.Children.Add(dialGrid);
        dialStack.Children.Add(_key);
        TextBlock dialNote = Wrapped(loc["Timers.DialNote"], "Brush.TextSubtle", 11.5);
        dialNote.Margin = new Thickness(0, 4, 0, 0);
        dialNote.MaxWidth = DialSize;
        dialNote.HorizontalAlignment = HorizontalAlignment.Left;
        dialStack.Children.Add(dialNote);
        _dialCard = Card(loc["Timers.Today"], dialStack);
        _dialCard.HorizontalAlignment = HorizontalAlignment.Left;

        _side.Children.Add(Card(loc["Timers.Now"], _live));
        _side.Children.Add(Card(loc["Timers.Next"], _next, loc["Timers.BellHint"]));
        _hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _hero.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _hero.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _hero.Children.Add(_dialCard);
        _hero.Children.Add(_side);
        _column.Children.Add(_hero);

        // the resets and the server's clock
        _column.Children.Add(Strip());

        // this week
        _column.Children.Add(Card(loc["Timers.Week"], _weekHost, loc["Timers.WeekLede"]));

        // every event: the reminder switch and its minutes, then a card per event with its overlay switch and sound
        var events = new StackPanel();
        var notifyText = new StackPanel();
        notifyText.Children.Add(TimerLook.Text(loc["Settings.Timetable.Notify"], "Brush.Text", 13.5, bold: true));
        TextBlock notifyHint = Wrapped(loc["Timers.NotifyHint"], "Brush.TextMuted", 12);
        notifyHint.Margin = new Thickness(0, 2, 0, 0);
        notifyHint.MaxWidth = 700;
        notifyText.Children.Add(notifyHint);
        _notify.Content = notifyText;
        _notify.Click += (_, _) =>
        {
            MeterSettings saved = Change(s => s.TimetableNotify = _notify.IsChecked == true);
            ShowSettings(saved);
        };
        events.Children.Add(_notify);
        _minutesRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _minutesRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _minutesRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _minutesRow.Children.Add(TimerLook.Text(loc["Settings.Timetable.SoundMinutes"], "Brush.TextMuted", 12.5));
        _minutes.SetResourceReference(StyleProperty, "ThemedSlider");
        Grid.SetColumn(_minutes, 1);
        _minutesRow.Children.Add(_minutes);
        Grid.SetColumn(_minutesValue, 2);
        _minutesRow.Children.Add(_minutesValue);
        _minutes.ValueChanged += (_, _) =>
        {
            int minutes = (int)_minutes.Value;
            _minutesValue.Text = string.Format(CultureInfo.CurrentCulture, loc["Settings.Timetable.LookaheadValue"], minutes);
            if (!_syncing)
            {
                Soon("minutes", s => s.TimetableSoundMinutes = minutes);
            }
        };
        events.Children.Add(_minutesRow);
        events.Children.Add(_cards);
        _column.Children.Add(Card(loc["Timers.Events"], events, loc["Timers.EventsLede"]));

        _saveSoon.Tick += (_, _) => FlushSaves();
        SizeChanged += (_, e) => Layout(e.NewSize.Width);
        Layout(1000);
        Tick();
    }

    /// <summary>Once a second (the ModulePage's timer): the countdowns and clocks move; the lists follow when an event started or ended, the ring twice a minute.</summary>
    public void Tick()
    {
        DateTime now = Aion2.ServerClock.Now; // the PC's time, corrected by the server clock's offset (as the overlay and the reminders have it)
        IReadOnlyList<ScheduledEvent> table = EventSchedule.Events;
        bool newTable = !ReferenceEquals(table, _table);
        if (newTable)
        {
            // a table from the server (another region's clocks, a changed time): everything that shows the events is made again
            FlushSaves();
            _table = table;
            ShowLede();
            BuildKey();
            BuildEventCards();
            _weekDay = DateTime.MinValue;
        }

        TimerBoard.Board board = TimerBoard.At(now);
        bool changed = newTable || !ReferenceEquals(board, _board);
        if (changed)
        {
            _board = board;
            _countdowns.Clear();
            _bars.Clear();
            _bells.Clear();
            BuildCenter(board, now);
            BuildLive(board, now);
            BuildNext(board, now);
            ShowNextLines(now);
        }

        if (now.Date != _weekDay)
        {
            _weekDay = now.Date;
            BuildWeek(now);
        }

        if (changed || (now - _dialAt).Duration() >= TimeSpan.FromSeconds(30))
        {
            _dialAt = now;
            _dial.Now = now;
            _dial.InvalidateVisual();
        }

        foreach (var (text, at) in _countdowns)
        {
            text.Text = TimerLook.Countdown(at - now);
        }

        foreach (var (bar, start, end) in _bars)
        {
            bar.Value = end > start ? Math.Clamp((now - start).TotalSeconds / (end - start).TotalSeconds, 0, 1) : 0;
        }

        ShowStrip(now);
        // what the Settings window (or the overlay's close button) changed shows here within a few seconds
        if ((DateTime.UtcNow - _syncedAt).Duration() >= TimeSpan.FromSeconds(5))
        {
            _syncedAt = DateTime.UtcNow;
            ShowSettings(MeterSettings.Load());
        }
    }

    /// <summary>The page goes away: a slider's change that waits to be kept is kept now.</summary>
    public void Stop()
    {
        _saveSoon.Stop();
        FlushSaves();
    }

    // ---- layout ----

    /// <summary>Wide: the ring beside what is on and next, the event cards in two columns. Narrow (a small window): everything in one column.</summary>
    private void Layout(double width)
    {
        bool narrow = width < 840;
        if (_laidOut && narrow == _narrow)
        {
            return;
        }

        _laidOut = true;
        _narrow = narrow;
        _hero.ColumnDefinitions[0].Width = narrow ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        _hero.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(_side, narrow ? 0 : 1);
        Grid.SetRow(_side, narrow ? 1 : 0);
        _cards.Columns = narrow ? 1 : 2;
    }

    private void ShowLede()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        string text = string.Format(CultureInfo.CurrentCulture, loc["Timers.Lede"], TimerLook.RegionName(EventSchedule.Region));
        _lede.Text = EventSchedule.Region is null ? text + " " + loc["Timers.RegionLater"] : text;
    }

    /// <summary>The key under the ring: a dot of each event's colour with its short name.</summary>
    private void BuildKey()
    {
        string language = LocalizationManager.Instance.Language;
        _key.Children.Clear();
        foreach (ScheduledEvent scheduled in EventSchedule.Events.Where(e => !e.Always))
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 6) };
            item.Children.Add(TimerLook.Dot(scheduled.Look));
            TextBlock name = TimerLook.Text(scheduled.ShortIn(language), "Brush.TextMuted", 12);
            name.Margin = new Thickness(6, 0, 0, 0);
            item.Children.Add(name);
            _key.Children.Add(item);
        }
    }

    /// <summary>The middle of the ring: the next big event (not the hourly ones), its countdown and when it starts.</summary>
    private void BuildCenter(TimerBoard.Board board, DateTime now)
    {
        EventNow? big = board.Events.Where(x => !x.Event.IsFrequent && x.Next is not null).MinBy(x => x.Next!.Start);
        _center.Visibility = big is null ? Visibility.Collapsed : Visibility.Visible;
        if (big?.Next is { } next)
        {
            _centerName.Text = big.Event.NameIn(LocalizationManager.Instance.Language);
            _centerWhen.Text = TimerLook.When(next.Start, now);
            _countdowns.Add((_centerCount, next.Start));
        }
    }

    /// <summary>What runs now, the one that ends soonest first: its icon and name, the time left of what it is doing, and how far it is.</summary>
    private void BuildLive(TimerBoard.Board board, DateTime now)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        _live.Children.Clear();
        var running = board.Events.Where(x => x.Live is not null).Select(x => (x.Event, Occurrence: x.Live!, Phase: TimerLook.PhaseOf(x.Live!, now)))
            .OrderBy(x => x.Phase.Until).ToList();
        if (running.Count == 0)
        {
            _live.Children.Add(Wrapped(loc["Timers.NothingNow"], "Brush.TextMuted", 13));
            return;
        }

        foreach (var (scheduled, occurrence, phase) in running)
        {
            string look = scheduled.Look;
            Color color = TimerLook.ColorOf(look);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var head = new DockPanel();
            FrameworkElement icon = TimerLook.IconFrame(scheduled, 34);
            DockPanel.SetDock(icon, Dock.Left);
            head.Children.Add(icon);
            TextBlock name = TimerLook.Text(TimerLook.Label(scheduled, loc.Language), "Brush.Text", 14.5, bold: true);
            name.Margin = new Thickness(10, 0, 0, 0);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            head.Children.Add(name);
            grid.Children.Add(head);
            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            TextBlock verb = TimerLook.Text(phase.Verb, "Brush.TextMuted", 12);
            verb.Margin = new Thickness(0, 0, 6, 0);
            left.Children.Add(verb);
            TextBlock count = TimerLook.Text(TimerLook.Countdown(phase.Until - now), "Brush.Text", 21, bold: true);
            left.Children.Add(count);
            _countdowns.Add((count, phase.Until));
            Grid.SetColumn(left, 1);
            grid.Children.Add(left);
            TextBlock sub = Wrapped(phase.Label + " · " + string.Format(CultureInfo.CurrentCulture, loc["Timers.Started"], TimerLook.Clock(occurrence.Start)), "Brush.TextMuted", 12.5);
            sub.Margin = new Thickness(0, 6, 0, 8);
            Grid.SetRow(sub, 1);
            Grid.SetColumnSpan(sub, 2);
            grid.Children.Add(sub);
            var bar = new ProgressBar { Minimum = 0, Maximum = 1, Height = 5, BorderThickness = new Thickness(0), Foreground = TimerLook.BrushOf(look) };
            bar.SetResourceReference(Control.BackgroundProperty, "Brush.Control");
            Grid.SetRow(bar, 2);
            Grid.SetColumnSpan(bar, 2);
            grid.Children.Add(bar);
            _bars.Add((bar, occurrence.Start, occurrence.End));

            // a stripe of the event's colour on the left, the card tinted with it (the website's "happening now" cards)
            var row = new DockPanel();
            var stripe = new Border { Width = 3, CornerRadius = new CornerRadius(2), Background = TimerLook.BrushOf(look), Margin = new Thickness(0, 2, 12, 2) };
            DockPanel.SetDock(stripe, Dock.Left);
            row.Children.Add(stripe);
            row.Children.Add(grid);
            _live.Children.Add(new Border
            {
                Child = row, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), BorderBrush = TimerLook.BrushOf(look, 0x73),
                Background = new LinearGradientBrush(Color.FromArgb(0x30, color.R, color.G, color.B), Color.FromArgb(0x00, color.R, color.G, color.B), 0),
                Padding = new Thickness(10, 10, 14, 12), Margin = new Thickness(0, 0, 0, 8),
            });
        }
    }

    /// <summary>The next start of each event, soonest first: icon, name, when (the player's time, and the server's when that differs), countdown, bell.</summary>
    private void BuildNext(TimerBoard.Board board, DateTime now)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        MeterSettings settings = MeterSettings.Load();
        TimeZoneInfo zone = TimerLook.ServerZone(EventSchedule.Events);
        _next.Children.Clear();
        var rows = board.Events.Where(x => x.Next is not null).OrderBy(x => x.Next!.Start).ToList();
        for (int i = 0; i < rows.Count; i++)
        {
            ScheduledEvent scheduled = rows[i].Event;
            EventOccurrence next = rows[i].Next!;
            var grid = new Grid { Margin = new Thickness(0, 7, 0, 7) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(TimerLook.IconFrame(scheduled, 38));
            var names = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            TextBlock name = TimerLook.Text(TimerLook.Label(scheduled, loc.Language), "Brush.Text", 14, bold: true);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            names.Children.Add(name);
            TextBlock when = TimerLook.Text(WhenText(next.Start, now, zone), "Brush.TextMuted", 12);
            when.TextTrimming = TextTrimming.CharacterEllipsis;
            names.Children.Add(when);
            Grid.SetColumn(names, 1);
            grid.Children.Add(names);
            TextBlock count = TimerLook.Text(TimerLook.Countdown(next.Start - now), "Brush.Text", 19, bold: true);
            count.MinWidth = 78;
            count.TextAlignment = TextAlignment.Right;
            Grid.SetColumn(count, 2);
            grid.Children.Add(count);
            _countdowns.Add((count, next.Start));
            Border bell = Bell(scheduled.Id, settings);
            Grid.SetColumn(bell, 3);
            grid.Children.Add(bell);
            var line = new Border { Child = grid, BorderThickness = new Thickness(0, 0, 0, i < rows.Count - 1 ? 1 : 0) };
            line.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            _next.Children.Add(line);
        }

        if (rows.Count == 0)
        {
            _next.Children.Add(Wrapped(loc["Home.NoEvents"], "Brush.TextMuted", 13));
        }
    }

    private static string WhenText(DateTime start, DateTime now, TimeZoneInfo zone)
    {
        string local = TimerLook.When(start, now);
        DateTime server = TimerLook.OnServer(start, zone);
        return server.Hour == start.Hour && server.Minute == start.Minute
            ? local
            : string.Format(CultureInfo.CurrentCulture, LocalizationManager.Instance["Timers.YourAndServer"], local, TimerLook.Clock(server));
    }

    // ---- the bell ----

    private Border Bell(string id, MeterSettings settings)
    {
        var glyph = new System.Windows.Shapes.Path
        {
            Data = BellGeometry, Stretch = Stretch.Uniform, Width = 15, Height = 15, StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        };
        var bell = new Border
        {
            Width = 32, Height = 32, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Child = glyph,
            Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        bell.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            ToggleBell(id);
        };
        _bells.Add((id, bell, glyph));
        PaintBell(bell, glyph, Reminded(settings, id));
        return bell;
    }

    private static void PaintBell(Border bell, System.Windows.Shapes.Path glyph, bool on)
    {
        bell.SetResourceReference(Border.BackgroundProperty, on ? "Brush.Accent" : "Brush.Control");
        bell.SetResourceReference(Border.BorderBrushProperty, on ? "Brush.Accent" : "Brush.Border");
        glyph.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, on ? "Brush.Window" : "Brush.TextMuted");
        if (on)
        {
            glyph.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Brush.Window");
        }
        else
        {
            glyph.Fill = null;
        }

        bell.ToolTip = LocalizationManager.Instance[on ? "Timers.BellOn" : "Timers.BellOff"];
    }

    /// <summary>The event plays its sound before it starts: reminders are on and it has a sound with some volume.</summary>
    private static bool Reminded(MeterSettings s, string id) =>
        s.TimetableNotify && s.TimetableEvents.TryGetValue(id, out TimetableEventSetting? setting) && setting.Sound.Length > 0 && setting.Volume > 0;

    /// <summary>A bell rung: the event gets a sound (the first one, or the one it had) and the reminders go on; rung again, its sound goes off. The sound is
    /// played once, so the player hears what will remind them.</summary>
    private void ToggleBell(string id)
    {
        bool on = false;
        TimetableEventSetting? setting = null;
        MeterSettings saved = Change(s =>
        {
            setting = SettingOf(s, id);
            on = Reminded(s, id);
            if (on)
            {
                setting.Sound = "";
                return;
            }

            if (setting.Sound.Length == 0)
            {
                setting.Sound = NotifySounds.All[0];
            }

            if (setting.Volume <= 0)
            {
                setting.Volume = 70;
            }

            s.TimetableNotify = true;
        });
        ShowSettings(saved);
        if (!on && setting is not null)
        {
            NotifySounds.Play(setting.Sound, setting.Volume);
        }
    }

    private static Geometry MakeBell()
    {
        Geometry bell = Geometry.Parse("M10,2.6 A4.9,4.9 0 0 0 5.1,7.5 L5.1,10.6 L3.6,13.4 L16.4,13.4 L14.9,10.6 L14.9,7.5 A4.9,4.9 0 0 0 10,2.6 Z M8,15.6 A2,2 0 0 0 12,15.6");
        if (bell.CanFreeze)
        {
            bell.Freeze();
        }

        return bell;
    }

    // ---- the resets and the server's clock ----

    private Border Strip()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var grid = new Grid();
        for (int i = 0; i < 3; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        (string Title, TextBlock Big, TextBlock Sub)[] cells =
        {
            (loc["Checklist.DailyReset"], _dailyCount, _dailyAt), (loc["Checklist.WeeklyReset"], _weeklyCount, _weeklyAt), (loc["Timers.ServerTime"], _serverClock, _serverSub),
        };
        for (int i = 0; i < cells.Length; i++)
        {
            var stack = new StackPanel();
            stack.Children.Add(TimerLook.Text(cells[i].Title, "Brush.TextMuted", 12.5, bold: true));
            cells[i].Big.Margin = new Thickness(0, 2, 0, 0);
            stack.Children.Add(cells[i].Big);
            cells[i].Sub.Margin = new Thickness(0, 2, 0, 0);
            stack.Children.Add(cells[i].Sub);
            var cell = new Border { Child = stack, Padding = new Thickness(i == 0 ? 0 : 16, 2, 12, 2), BorderThickness = new Thickness(0, 0, i < cells.Length - 1 ? 1 : 0, 0) };
            cell.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }

        return Card(null, grid);
    }

    /// <summary>The resets' countdowns every second; the times under them and the server's clock when the minute changes.</summary>
    private void ShowStrip(DateTime now)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        DateTime daily = TimerLook.NextDailyReset(now), weekly = TimerLook.NextWeeklyReset(now);
        _dailyCount.Text = TimerLook.Countdown(daily - now);
        _weeklyCount.Text = TimerLook.Countdown(weekly - now);
        long minute = now.Ticks / TimeSpan.TicksPerMinute;
        if (minute == _stripMinute)
        {
            return;
        }

        _stripMinute = minute;
        _dailyAt.Text = string.Format(CultureInfo.CurrentCulture, loc["Timers.DailyAt"], TimerLook.When(daily, now));
        _weeklyAt.Text = string.Format(CultureInfo.CurrentCulture, loc["Timers.WeeklyAt"], weekly.ToString("dddd", CultureInfo.CurrentCulture) + " " + TimerLook.Clock(weekly));
        _serverClock.Text = TimerLook.Clock(TimerLook.OnServer(now, TimerLook.ServerZone(EventSchedule.Events)));
        _serverSub.Text = string.Format(CultureInfo.CurrentCulture, loc["Timers.ServerSub"], TimerLook.RegionName(EventSchedule.Region), TimerLook.Clock(now));
    }

    // ---- this week ----

    /// <summary>The events that are not on every day, for today and the next six days in the player's time, with the weekly reset; today's column lit.</summary>
    private void BuildWeek(DateTime now)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        DateTime day0 = now.Date;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int i = 0; i < 7; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        void Cell(UIElement content, int row, int column)
        {
            var cell = new Border { Child = content, Padding = new Thickness(column == 0 ? 0 : 10, 8, 10, 8), BorderThickness = new Thickness(0, 0, 0, 1) };
            cell.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            if (column == 1)
            {
                cell.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
            }

            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);
        }

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Cell(new Border(), 0, 0);
        for (int i = 0; i < 7; i++)
        {
            DateTime day = day0.AddDays(i);
            string head = i == 0 ? loc["Sessions.Today"] : day.ToString("ddd", CultureInfo.CurrentCulture) + " " + day.Day.ToString(CultureInfo.CurrentCulture);
            Cell(TimerLook.Text(head, i == 0 ? "Brush.Accent" : "Brush.TextMuted", 12.5, bold: true), 0, i + 1);
        }

        void Row(FrameworkElement head, List<string>[] times)
        {
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Cell(head, row, 0);
            for (int i = 0; i < 7; i++)
            {
                TextBlock text = times[i].Count > 0 ? TimerLook.Text(string.Join(", ", times[i]), "Brush.Text", 13.5, bold: true) : TimerLook.Text("–", "Brush.TextDisabled", 13.5);
                text.TextWrapping = TextWrapping.Wrap;
                Cell(text, row, i + 1);
            }
        }

        static List<string>[] Days()
        {
            var days = new List<string>[7];
            for (int i = 0; i < 7; i++)
            {
                days[i] = new List<string>();
            }

            return days;
        }

        foreach (ScheduledEvent scheduled in EventSchedule.Events.Where(e => e.IsWeekly))
        {
            List<string>[] times = Days();
            foreach (EventOccurrence occurrence in EventSchedule.Between(scheduled, day0, day0.AddDays(7)))
            {
                int index = (occurrence.Start.Date - day0).Days;
                if (occurrence.Start >= day0 && index is >= 0 and < 7)
                {
                    times[index].Add(TimerLook.Clock(occurrence.Start));
                }
            }

            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0) };
            head.Children.Add(TimerLook.IconFrame(scheduled, 26));
            TextBlock name = TimerLook.Text(scheduled.ShortIn(loc.Language), "Brush.Text", 13, bold: true);
            name.Margin = new Thickness(8, 0, 0, 0);
            head.Children.Add(name);
            Row(head, times);
        }

        List<string>[] resets = Days();
        DateTime weekly = TimerLook.NextWeeklyReset(day0);
        int resetDay = (weekly.Date - day0).Days;
        if (resetDay is >= 0 and < 7)
        {
            resets[resetDay].Add(TimerLook.Clock(weekly));
        }

        var resetHead = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0) };
        var dotBox = new Border { Width = 26, Child = TimerLook.Dot("reset") };
        resetHead.Children.Add(dotBox);
        TextBlock resetName = TimerLook.Text(loc["Checklist.WeeklyReset"], "Brush.Text", 13, bold: true);
        resetName.Margin = new Thickness(8, 0, 0, 0);
        resetHead.Children.Add(resetName);
        Row(resetHead, resets);
        _weekHost.Child = grid;
    }

    // ---- every event ----

    /// <summary>A card per event (the scheduled ones first): its icon, name and how sure its time is, when it runs on the server's clock, its next times in the
    /// player's, the overlay switch, and its reminder sound with volume and a play button. The events open all day have no sound: nothing starts.</summary>
    private void BuildEventCards()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        _cards.Children.Clear();
        _controls.Clear();
        MeterSettings s = MeterSettings.Load();
        foreach (ScheduledEvent scheduled in EventSchedule.Events.OrderBy(e => e.Always))
        {
            string id = scheduled.Id;
            TimetableEventSetting state = s.TimetableEvents.TryGetValue(id, out TimetableEventSetting? found) ? found : new TimetableEventSetting();
            var stack = new StackPanel();
            var head = new DockPanel();
            var show = new CheckBox
            {
                IsChecked = state.Show, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10, 0, 0, 0), ToolTip = loc["Settings.Timetable.ShowEvent"],
                Content = TimerLook.Text(loc["Settings.Overlay.Master"], "Brush.TextMuted", 12),
            };
            if (Application.Current?.TryFindResource("SettingsSwitch") is Style style)
            {
                show.Style = style;
            }

            show.Click += (_, _) => Change(x => SettingOf(x, id).Show = show.IsChecked == true);
            DockPanel.SetDock(show, Dock.Right);
            head.Children.Add(show);
            var title = new DockPanel();
            FrameworkElement icon = TimerLook.IconFrame(scheduled, 36);
            DockPanel.SetDock(icon, Dock.Left);
            title.Children.Add(icon);
            var names = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            TextBlock name = TimerLook.Text(TimerLook.Label(scheduled, loc.Language), "Brush.Text", 14, bold: true);
            name.TextWrapping = TextWrapping.Wrap;
            names.Children.Add(name);
            if (!scheduled.Always)
            {
                names.Children.Add(SureTag(scheduled.IsReported));
            }

            title.Children.Add(names);
            head.Children.Add(title);
            stack.Children.Add(head);
            TextBlock when = Wrapped(scheduled.Always ? loc["Settings.Timetable.AlwaysTip"] : ScheduleText(scheduled), "Brush.TextMuted", 12);
            when.Margin = new Thickness(0, 8, 0, 0);
            stack.Children.Add(when);
            TextBlock nextLine = Wrapped("", "Brush.TextSubtle", 12);
            nextLine.Margin = new Thickness(0, 2, 0, 0);
            nextLine.Visibility = scheduled.Always ? Visibility.Collapsed : Visibility.Visible;
            stack.Children.Add(nextLine);

            ComboBox? sound = null;
            Slider? volume = null;
            Button? test = null;
            if (!scheduled.Always)
            {
                sound = new ComboBox { Width = 124, Height = 26, VerticalContentAlignment = VerticalAlignment.Center };
                sound.Items.Add(new ComboBoxItem { Content = loc["Settings.Timetable.NoSound"], Tag = "" });
                foreach (string soundId in NotifySounds.All)
                {
                    sound.Items.Add(new ComboBoxItem { Content = loc["Sound." + soundId], Tag = soundId });
                }

                sound.SelectedItem = sound.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == state.Sound) ?? sound.Items[0];
                volume = new Slider
                {
                    Minimum = 0, Maximum = 100, Value = Math.Clamp(state.Volume, 0, 100), TickFrequency = 5, IsSnapToTickEnabled = true, SmallChange = 5, LargeChange = 10,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0), ToolTip = loc["Settings.Timetable.Volume"],
                };
                volume.SetResourceReference(StyleProperty, "ThemedSlider");
                test = new Button { Content = "▶", Width = 28, Height = 26, Padding = new Thickness(0), ToolTip = loc["Settings.Timetable.Test"] };
                ComboBox soundBox = sound;
                Slider volumeSlider = volume;
                soundBox.SelectionChanged += (_, _) =>
                {
                    if (_syncing)
                    {
                        return;
                    }

                    string pick = (soundBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
                    ShowSettings(Change(x => SettingOf(x, id).Sound = pick));
                };
                volumeSlider.ValueChanged += (_, _) =>
                {
                    if (!_syncing)
                    {
                        int level = (int)volumeSlider.Value;
                        Soon("volume:" + id, x => SettingOf(x, id).Volume = level);
                    }
                };
                test.Click += (_, _) => NotifySounds.Play((soundBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "", (int)volumeSlider.Value);
                var row = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
                DockPanel.SetDock(sound, Dock.Left);
                row.Children.Add(sound);
                DockPanel.SetDock(test, Dock.Right);
                row.Children.Add(test);
                row.Children.Add(volume);
                stack.Children.Add(row);
            }

            var card = new Border { Child = stack, Padding = new Thickness(12, 10, 12, 12), Margin = new Thickness(0, 0, 10, 10), CornerRadius = new CornerRadius(6) };
            card.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
            _cards.Children.Add(card);
            _controls.Add(new EventControls(scheduled, show, sound, volume, test, nextLine));
        }

        ShowSettings(s);
    }

    /// <summary>"Checked" (in-game reports and several timers agree) or "Reported" (fewer reports: check in game).</summary>
    private static Border SureTag(bool reported)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        string brush = reported ? "Brush.Warning" : "Brush.Heal";
        TextBlock text = TimerLook.Text(loc[reported ? "Timers.Reported" : "Timers.Checked"], brush, 10.5, bold: true);
        var tag = new Border
        {
            Child = text, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), Padding = new Thickness(6, 0, 6, 1), Margin = new Thickness(0, 3, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, ToolTip = loc[reported ? "Timers.ReportedTip" : "Timers.CheckedTip"],
        };
        tag.SetResourceReference(Border.BorderBrushProperty, brush);
        return tag;
    }

    /// <summary>When an event runs, on the server's clock: "Every hour at :30", "Every 3 hours: 02:00, 05:00 …", "Mon, Thu, Sat at 21:00", "Daily, 11:00 to 14:00".</summary>
    private static string ScheduleText(ScheduledEvent scheduled)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        CultureInfo culture = CultureInfo.CurrentCulture;
        static string Wall(TimeSpan t) => DateTime.Today.Add(t).ToString("t", CultureInfo.CurrentCulture);
        string? text = null;
        if (scheduled.Windows.Count >= 2 && scheduled.Windows.All(w => w.Days.Count >= 7))
        {
            List<TimeSpan> starts = scheduled.Windows.Select(w => w.Start).OrderBy(t => t).ToList();
            TimeSpan step = starts[1] - starts[0];
            bool regular = step > TimeSpan.Zero && Math.Abs(step.TotalHours * starts.Count - 24) < 0.01
                && starts.Zip(starts.Skip(1)).All(p => p.Second - p.First == step);
            if (regular && step == TimeSpan.FromHours(1))
            {
                text = string.Format(culture, loc["Timers.EveryHour"], starts[0].Minutes.ToString("00", culture));
            }
            else if (regular)
            {
                text = string.Format(culture, loc["Timers.EveryHours"], (int)step.TotalHours, string.Join(", ", starts.Select(Wall)));
            }
        }

        if (text is null)
        {
            var parts = new List<string>();
            foreach (EventWindow window in scheduled.Windows)
            {
                string days = window.Days.Count >= 7 ? loc["Timers.Daily"]
                    : string.Join(", ", window.Days.OrderBy(d => (d + 6) % 7).Select(d => culture.DateTimeFormat.GetAbbreviatedDayName((DayOfWeek)d)));
                TimeSpan length = window.End > window.Start ? window.End - window.Start : window.End + TimeSpan.FromDays(1) - window.Start;
                parts.Add(length >= TimeSpan.FromMinutes(90)
                    ? string.Format(culture, loc["Timers.DaysFromTo"], days, Wall(window.Start), Wall(window.End))
                    : string.Format(culture, loc["Timers.DaysAt"], days, Wall(window.Start)));
            }

            text = string.Join("; ", parts);
        }

        return string.Format(culture, loc["Timers.OnServerClock"], text);
    }

    /// <summary>"Next for you: 21:00, Tomorrow 00:00, Tomorrow 03:00" under each event's card, when an event started or ended.</summary>
    private void ShowNextLines(DateTime now)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        foreach (EventControls c in _controls)
        {
            if (c.Event.Always)
            {
                continue;
            }

            IEnumerable<string> starts = EventSchedule.Between(c.Event, now, now.AddDays(8)).Where(o => o.Start > now).Take(3).Select(o => TimerLook.When(o.Start, now));
            c.NextLine.Text = string.Format(CultureInfo.CurrentCulture, loc["Timers.NextForYou"], string.Join(", ", starts));
        }
    }

    // ---- the settings ----

    private static TimetableEventSetting SettingOf(MeterSettings s, string id)
    {
        if (!s.TimetableEvents.TryGetValue(id, out TimetableEventSetting? setting))
        {
            setting = new TimetableEventSetting();
            s.TimetableEvents[id] = setting;
        }

        return setting;
    }

    /// <summary>Keeps a change at once (with any slider change still waiting) and tells the overlay; the settings as they are now kept.</summary>
    private MeterSettings Change(Action<MeterSettings> change)
    {
        _saveSoon.Stop();
        MeterSettings s = MeterSettings.Load();
        foreach (Action<MeterSettings> waiting in _pending.Values)
        {
            waiting(s);
        }

        _pending.Clear();
        change(s);
        s.Save();
        _syncedAt = DateTime.UtcNow;
        _overlayChanged?.Invoke();
        return s;
    }

    /// <summary>A slider's change, kept a moment after the slider stops moving.</summary>
    private void Soon(string key, Action<MeterSettings> change)
    {
        _pending[key] = change;
        _saveSoon.Stop();
        _saveSoon.Start();
    }

    private void FlushSaves()
    {
        _saveSoon.Stop();
        if (_pending.Count > 0)
        {
            Change(_ => { });
        }
    }

    /// <summary>The switches, sounds, volumes and bells as the settings have them (changed in the Settings window, by a bell, or here); the sound controls
    /// are greyed out while the reminders are off. Skipped while a change of this page still waits to be kept.</summary>
    private void ShowSettings(MeterSettings s)
    {
        if (_pending.Count > 0)
        {
            return;
        }

        _syncing = true;
        try
        {
            bool on = s.TimetableNotify;
            _notify.IsChecked = on;
            if (!_minutes.IsMouseCaptureWithin)
            {
                _minutes.Value = Math.Clamp(s.TimetableSoundMinutes, 0, 60);
            }

            _minutesValue.Text = string.Format(CultureInfo.CurrentCulture, LocalizationManager.Instance["Settings.Timetable.LookaheadValue"], (int)_minutes.Value);
            _minutesRow.IsEnabled = on;
            _minutesRow.Opacity = on ? 1.0 : 0.45;
            foreach (EventControls c in _controls)
            {
                TimetableEventSetting state = s.TimetableEvents.TryGetValue(c.Event.Id, out TimetableEventSetting? found) ? found : new TimetableEventSetting();
                c.Show.IsChecked = state.Show;
                if (c.Sound is null || c.Volume is null || c.Test is null)
                {
                    continue;
                }

                c.Sound.SelectedItem = c.Sound.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == state.Sound) ?? c.Sound.Items[0];
                if (!c.Volume.IsMouseCaptureWithin)
                {
                    c.Volume.Value = Math.Clamp(state.Volume, 0, 100);
                }

                c.Sound.IsEnabled = on;
                c.Volume.IsEnabled = on;
                c.Test.IsEnabled = on && state.Sound.Length > 0;
                c.Sound.Opacity = c.Volume.Opacity = on ? 1.0 : 0.45;
                c.Test.Opacity = c.Test.IsEnabled ? 1.0 : 0.45;
            }

            foreach (var (id, bell, glyph) in _bells)
            {
                PaintBell(bell, glyph, Reminded(s, id));
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    // ---- small parts ----

    /// <summary>A card like Home's: a small title in capitals (and a grey line under it), the content under that.</summary>
    private static Border Card(string? title, UIElement content, string? lede = null)
    {
        var stack = new StackPanel();
        if (title is not null)
        {
            TextBlock head = TimerLook.Text(title.ToUpperInvariant(), "Brush.TextMuted", 11, bold: true);
            head.Margin = new Thickness(0, 0, 0, lede is null ? 8 : 2);
            stack.Children.Add(head);
        }

        if (lede is not null)
        {
            TextBlock sub = Wrapped(lede, "Brush.TextSubtle", 12);
            sub.Margin = new Thickness(0, 0, 0, 10);
            stack.Children.Add(sub);
        }

        stack.Children.Add(content);
        var card = new Border
        {
            Child = stack, Padding = new Thickness(16, 12, 16, 14), Margin = new Thickness(0, 0, 12, 12), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top,
        };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return card;
    }

    private static TextBlock Wrapped(string text, string brush, double size)
    {
        TextBlock block = TimerLook.Text(text, brush, size);
        block.TextWrapping = TextWrapping.Wrap;
        return block;
    }

    private static TextBlock Centered(string text, string brush, double size, bool bold = false)
    {
        TextBlock block = TimerLook.Text(text, brush, size, bold);
        block.TextWrapping = TextWrapping.Wrap;
        block.TextAlignment = TextAlignment.Center;
        block.HorizontalAlignment = HorizontalAlignment.Center;
        return block;
    }
}
