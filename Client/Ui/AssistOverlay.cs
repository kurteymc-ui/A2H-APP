using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDPS.Aion2;

namespace AionDPS.Ui;

/// <summary>
/// The effects overlay: the effects on the own character (<see cref="AssistPanel"/>) as bars or pictures, no box around them, placed, scaled and locked like
/// the other overlays (<see cref="MovableOverlay"/>); <see cref="AssistController"/> shows it only while the game is in front. (The skills' cooldowns are a
/// window each since 4 November 2026: <see cref="CooldownSkillWindow"/>.)
/// </summary>
public sealed class AssistOverlayWindow : MovableOverlay
{
    public AssistPanel Panel { get; } = new(true) { MinWidth = 44, MinHeight = 40 };

    public AssistOverlayWindow(bool effects = false) : base(effects ? OverlayPlaces.Effects : OverlayPlaces.Assist, new Border { Child = null }, effects ? LoadEffects : Load, effects ? SaveEffects : Save,
        SystemParameters.WorkArea.Left + (effects ? 300 : 40), 520, effects ? LoadEffectsSize : LoadSize, effects ? SaveEffectsSize : SaveSize, OverlayEdgeMode.Width)
    {
        Panel.Part = effects ? AssistPart.Effects : AssistPart.Cooldowns;
        Flush();
        // placed together with the skills' cooldowns, so as quiet as they are: a faint light outline and light marks, no orange (4 November 2026)
        UnlockedBorder = new SolidColorBrush(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF));
        UnlockedBackground = new SolidColorBrush(Color.FromArgb(0x26, 0x00, 0x00, 0x00));
        QuietHandles();
        ApplyLocked(IsLocked);
        Body = new Grid { Children = { Panel } }; // no box around it (4 November 2026: the backdrop was in the way): the bars and pictures carry their own
    }

    private static (double?, double?) LoadSize()
    {
        var s = MeterSettings.Load();
        return (s.AssistOverlayWidth, s.AssistOverlayHeight);
    }

    private static void SaveSize(double? width, double? height)
    {
        var s = MeterSettings.Load();
        s.AssistOverlayWidth = width;
        s.AssistOverlayHeight = height;
        s.Save();
    }

    private static (double?, double?, double) Load()
    {
        var s = MeterSettings.Load();
        return (s.AssistOverlayLeft, s.AssistOverlayTop, s.AssistOverlayScale);
    }

    private static void Save(double left, double top, double scale)
    {
        var s = MeterSettings.Load();
        s.AssistOverlayLeft = left;
        s.AssistOverlayTop = top;
        s.AssistOverlayScale = scale;
        s.Save();
    }

    private static (double?, double?) LoadEffectsSize()
    {
        var s = MeterSettings.Load();
        return (s.EffectsOverlayWidth, s.EffectsOverlayHeight);
    }

    private static void SaveEffectsSize(double? width, double? height)
    {
        var s = MeterSettings.Load();
        s.EffectsOverlayWidth = width;
        s.EffectsOverlayHeight = height;
        s.Save();
    }

    private static (double?, double?, double) LoadEffects()
    {
        var s = MeterSettings.Load();
        return (s.EffectsOverlayLeft, s.EffectsOverlayTop, s.EffectsOverlayScale);
    }

    private static void SaveEffects(double left, double top, double scale)
    {
        var s = MeterSettings.Load();
        s.EffectsOverlayLeft = left;
        s.EffectsOverlayTop = top;
        s.EffectsOverlayScale = scale;
        s.Save();
    }
}

/// <summary>
/// Runs the skills' cooldowns and the effects overlay: eight times a second. Every skill on the skill bar (its cooldown known or not yet) gets a window of its
/// own (<see cref="CooldownSkillWindow"/>), placed where the player put it - a column of bars or a row of squares until then; the effects are one overlay.
/// Shown only while the game is in front (never on the desktop). Unlocked (placing them, as a game's interface editor): every ticked skill shows, also when
/// ready and also with the assist window in front, with a small bar at the top of the screen for the look, moving together, the size and Done. Also keeps
/// what the cooldowns learned in the settings and says the skills ticked for it when they come back.
/// </summary>
public sealed class AssistController : IDisposable
{
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(125) };
    private readonly Func<Aion2EntityDirectory?> _directory;
    private AssistOverlayWindow? _effects;
    private readonly Dictionary<int, CooldownSkillWindow> _skills = new();
    private readonly CooldownVoice _voice = new();
    private MeterSettings _settings = MeterSettings.Load();
    private DateTime _settingsAt = DateTime.UtcNow;
    private HashSet<int> _hidden = new();
    private int _placesSeen = PlacesVersion;
    private CooldownEditBar? _editBar;

    // every frame while a shown cooldown runs (the timer's eight times a second made the tenths skip and the bars step): the windows shown at the last
    // tick, and whether they may show at all, are being placed, or show when ready too
    private readonly List<(int Id, CooldownSkillWindow Window)> _framed = new();
    private bool _mayShow, _placingNow, _showReadyNow, _everyFrame;
    private TimeSpan _lastFrame;

    /// <summary>The look, size and bar width the windows were last laid out with: a change rearranges them (the arrangement kept, scaled).</summary>
    private string _laidStyle = "";
    private double _laidScale, _laidWidth;

    /// <summary>The <see cref="Aion2Cooldowns.LearnedVersion"/>, <see cref="Aion2Cooldowns.KnownVersion"/> and <see cref="Aion2Cooldowns.GapsVersion"/> the
    /// settings have.</summary>
    private int _learnedSaved = -1, _knownSaved = -1, _gapsSaved = -1;

    /// <summary>Counts every "reset places" of the cooldowns: the windows are made again at their first places.</summary>
    public static int PlacesVersion { get; private set; }

    /// <summary>Every skill's cooldown back at its first place (one column of bars, one row of squares).</summary>
    public static void ResetCooldownPlaces()
    {
        var s = MeterSettings.Load();
        s.AssistCooldownPlaces.Clear();
        s.AssistCooldownTilePlaces.Clear();
        DropDocks(s);
        s.Save();
        PlacesVersion++;
    }

    /// <summary>The cooldowns of 1.20.13 hung on each other through the overlays' docking; they are placed on their own now, so those ties go.</summary>
    private static bool DropDocks(MeterSettings s)
    {
        var titles = s.OverlayDocks.Where(kv => CooldownSkillWindow.IsCooldownTitle(kv.Key) || CooldownSkillWindow.IsCooldownTitle(kv.Value?.Parent)).Select(kv => kv.Key).ToList();
        foreach (string title in titles)
        {
            s.OverlayDocks.Remove(title);
        }

        return titles.Count > 0;
    }

    /// <summary>Raised when the bar at the top changed a setting (the look, together, the size, Done): the assist window shows it too.</summary>
    public static event Action? EditBarChanged;

    private static bool _reloadAsked;

    /// <summary>The assist window changed a setting: read again at the next tick (an eighth of a second), not up to a second later.</summary>
    public static void AskReload() => _reloadAsked = true;

    /// <summary>Changes the settings now (the bar at the top while placing): saved, and taken at once.</summary>
    private void Change(Action<MeterSettings> apply)
    {
        var s = MeterSettings.Load();
        apply(s);
        s.Save();
        _settings = s;
        _settingsAt = DateTime.UtcNow;
        _hidden = s.AssistOverlayHidden.ToHashSet();
        EditBarChanged?.Invoke();
    }

    public AssistController(Func<Aion2EntityDirectory?> directory)
    {
        _directory = directory;
        Aion2Cooldowns.SeedLearned(_settings.AssistNoCooldownSkills);
        Aion2Cooldowns.SeedKnown(_settings.AssistKnownSkills);
        Aion2Cooldowns.SeedGaps(_settings.AssistCooldownGaps);
        _learnedSaved = Aion2Cooldowns.LearnedVersion;
        _knownSaved = Aion2Cooldowns.KnownVersion;
        _gapsSaved = Aion2Cooldowns.GapsVersion;
        if (DropDocks(_settings))
        {
            _settings.Save();
        }

        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public void RaiseToFront()
    {
        _effects?.RaiseToFront();
        foreach (CooldownSkillWindow w in _skills.Values)
        {
            w.RaiseToFront();
        }

        if (_editBar is { IsVisible: true })
        {
            NativeOverlay.KeepOnTop(new System.Windows.Interop.WindowInteropHelper(_editBar).Handle); // over the cooldowns it places
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        EveryFrame(false);
        _effects?.Close();
        _editBar?.Close();
        foreach (CooldownSkillWindow w in _skills.Values)
        {
            w.Close();
        }

        _skills.Clear();
    }

    private void Tick()
    {
        if (_reloadAsked || (DateTime.UtcNow - _settingsAt).TotalSeconds >= 1)
        {
            _reloadAsked = false;
            _settings = MeterSettings.Load();
            _settingsAt = DateTime.UtcNow;
            _hidden = _settings.AssistOverlayHidden.ToHashSet();
            Keep();
        }

        bool inGame = GameWindow.OverlayMayShow();
        RunSkills(inGame);
        Run(ref _effects, _settings.EffectsOverlayOn, inGame);
    }

    /// <summary>What the cooldowns learned (the skills with none, the skills with one, the gaps between uses), kept for the next start.</summary>
    private void Keep()
    {
        bool changed = false;
        if (Aion2Cooldowns.LearnedVersion != _learnedSaved)
        {
            _learnedSaved = Aion2Cooldowns.LearnedVersion;
            List<int> learned = Aion2Cooldowns.LearnedNoCooldown();
            if (!learned.SequenceEqual(_settings.AssistNoCooldownSkills.OrderBy(i => i)))
            {
                _settings.AssistNoCooldownSkills = learned;
                changed = true;
            }
        }

        if (Aion2Cooldowns.KnownVersion != _knownSaved)
        {
            _knownSaved = Aion2Cooldowns.KnownVersion;
            List<int> known = Aion2Cooldowns.KnownSkills();
            if (!known.SequenceEqual(_settings.AssistKnownSkills))
            {
                _settings.AssistKnownSkills = known;
                changed = true;
            }
        }

        if (Aion2Cooldowns.GapsVersion != _gapsSaved)
        {
            _gapsSaved = Aion2Cooldowns.GapsVersion;
            Dictionary<int, List<double>> gaps = Aion2Cooldowns.LearnedGaps();
            foreach ((int id, List<double> kept) in _settings.AssistCooldownGaps)
            {
                gaps.TryAdd(id, kept); // a skill not used this session keeps what it had
            }

            _settings.AssistCooldownGaps = gaps;
            changed = true;
        }

        if (DropDocks(_settings))
        {
            changed = true; // a tie to a skill's cooldown came back (written by an older version): gone again
        }

        if (changed)
        {
            _settings.Save();
        }
    }

    /// <summary>The skills' windows: the skills on the bar (else those known to have a cooldown) that are ticked, each where it was put.</summary>
    private void RunSkills(bool inGame)
    {
        Aion2EntityDirectory? dir = _directory();
        IReadOnlyList<int>? bar = dir?.LocalBarOrder is { Count: > 0 } order ? order : null;
        var listed = Aion2Cooldowns.Skills(bar).Where(id => AssistPanel.Listed(id, bar)).ToList();
        var shown = listed.Where(id => !_hidden.Contains(id)).ToList();
        DateTime now = DateTime.Now; // the capture stamps its frames with the local clock
        _voice.Tick(listed, _settings, now, inGame); // ticked for the voice: said also when it is not on the screen
        if (PlacesVersion != _placesSeen)
        {
            _placesSeen = PlacesVersion;
            foreach (CooldownSkillWindow w in _skills.Values)
            {
                w.Close();
            }

            _skills.Clear();
            _laidStyle = "";
        }

        bool placing = !_settings.AssistOverlayLocked;
        if (!_settings.ShowAssistOverlay || (!inGame && !placing))
        {
            // hidden while the game is not in front (never on the desktop) - but shown while they are being placed, also from the assist window
            foreach (CooldownSkillWindow w in _skills.Values)
            {
                w.ShowOverlay(false);
            }

            _editBar?.Hide();
            _mayShow = false;
            _framed.Clear();
            EveryFrame(false);
            return;
        }

        string style = _settings.AssistCooldownStyle == CooldownArt.Tiles ? CooldownArt.Tiles : CooldownArt.Bars;
        double scale = Math.Clamp(_settings.AssistCooldownScale, 0.5, 3);
        double width = Math.Clamp(_settings.AssistBarWidth, 90, 400);
        CooldownSkillWindow.Together = _settings.AssistCooldownsTogether;
        Arrange(style, scale, width, listed);
        _framed.Clear();
        bool running = false;
        foreach (int id in shown)
        {
            if (!_skills.TryGetValue(id, out CooldownSkillWindow? window))
            {
                (double left, double top) = SlotOf(listed.IndexOf(id), style, scale, width);
                window = new CooldownSkillWindow(id, left, top);
                _skills[id] = window;
            }

            window.Use(style, width);
            window.UseScale(scale);
            window.ApplyLockedIfChanged(!placing);
            CooldownView view = Aion2Cooldowns.View(id, now);
            window.Show(view);
            window.ShowOverlay(placing || view.Running || _settings.AssistShowReady);
            _framed.Add((id, window));
            running |= view.Running;
        }

        (_mayShow, _placingNow, _showReadyNow) = (true, placing, _settings.AssistShowReady);
        EveryFrame(running);

        foreach ((int id, CooldownSkillWindow w) in _skills)
        {
            if (!shown.Contains(id))
            {
                w.ShowOverlay(false);
            }
        }

        RunEditBar(placing && shown.Count > 0, style);
    }

    /// <summary>Shows the cooldowns every frame of the screen (about 60 times a second) while one of them runs; idle, nothing is done between the ticks.</summary>
    private void EveryFrame(bool on)
    {
        if (on == _everyFrame)
        {
            return;
        }

        _everyFrame = on;
        if (on)
        {
            System.Windows.Media.CompositionTarget.Rendering += OnFrame;
        }
        else
        {
            System.Windows.Media.CompositionTarget.Rendering -= OnFrame;
        }
    }

    /// <summary>
    /// One frame: each shown cooldown at this very moment - the tenths change every tenth of a second, the bars and the sweeps glide - and one that ended
    /// goes (or one that started comes) at once, as the tick would have it.
    /// </summary>
    private void OnFrame(object? sender, EventArgs e)
    {
        if (e is System.Windows.Media.RenderingEventArgs r)
        {
            if (r.RenderingTime == _lastFrame)
            {
                return; // raised twice for one frame
            }

            _lastFrame = r.RenderingTime;
        }

        if (!_mayShow)
        {
            return;
        }

        DateTime now = DateTime.Now; // the capture stamps its frames with the local clock
        foreach ((int id, CooldownSkillWindow window) in _framed)
        {
            CooldownView view = Aion2Cooldowns.View(id, now);
            bool show = _placingNow || view.Running || _showReadyNow;
            if (show)
            {
                window.Show(view);
            }

            window.ShowOverlay(show);
        }
    }

    /// <summary>
    /// The default place of the skill at <paramref name="index"/> of the list: one column of bars, one row of squares, at the left of the screen a third of
    /// the way down.
    /// </summary>
    private static (double Left, double Top) SlotOf(int index, string style, double scale, double width)
    {
        Rect area = WindowPlacement.Primary();
        double left = area.Left + 60, top = area.Top + area.Height * 0.35;
        index = Math.Max(0, index);
        return style == CooldownArt.Tiles
            ? (left + index * ((CooldownArt.TileSize + 2) * scale + 2), top)
            : (left, top + index * ((CooldownArt.BarHeight + 2) * scale + 2));
    }

    /// <summary>
    /// After a change of the look the windows go to that look's places (or its column or row); after a change of the size or the bars' width the
    /// arrangement stays as it was, larger or smaller around its top left corner - the other look's places too (the bars' width counts for the bars only).
    /// </summary>
    private void Arrange(string style, double scale, double width, List<int> listed)
    {
        if (_laidStyle.Length == 0 || _skills.Count == 0)
        {
            (_laidStyle, _laidScale, _laidWidth) = (style, scale, width);
            return;
        }

        if (style != _laidStyle)
        {
            Dictionary<int, double[]> places = CooldownSkillWindow.PlacesOf(MeterSettings.Load(), style);
            foreach ((int id, CooldownSkillWindow w) in _skills)
            {
                (double left, double top) = places.TryGetValue(id, out double[]? p) && p.Length == 2 ? (p[0], p[1]) : SlotOf(listed.IndexOf(id), style, scale, width);
                w.MoveTo(left, top);
            }

            (_laidStyle, _laidScale, _laidWidth) = (style, scale, width);
            return;
        }

        double ratio = scale / _laidScale, widthRatio = width / _laidWidth;
        if (Math.Abs(ratio - 1) > 0.001 || Math.Abs(widthRatio - 1) > 0.002)
        {
            var s = MeterSettings.Load();
            Dictionary<int, double[]> mine = CooldownSkillWindow.PlacesOf(s, style);
            foreach ((int id, CooldownSkillWindow w) in _skills)
            {
                mine[id] = new[] { Math.Round(w.Left, 1), Math.Round(w.Top, 1) }; // where it is now (its slot until moved by hand)
            }

            CooldownSkillWindow.Rescale(mine, ratio * (style == CooldownArt.Bars ? widthRatio : 1), ratio);
            string other = style == CooldownArt.Tiles ? CooldownArt.Bars : CooldownArt.Tiles;
            CooldownSkillWindow.Rescale(CooldownSkillWindow.PlacesOf(s, other), ratio * (other == CooldownArt.Bars ? widthRatio : 1), ratio);
            s.Save();
            foreach ((int id, CooldownSkillWindow w) in _skills)
            {
                w.MoveTo(mine[id][0], mine[id][1]);
            }
        }

        (_laidStyle, _laidScale, _laidWidth) = (style, scale, width);
    }

    /// <summary>The bar at the top of the screen while the cooldowns are being placed.</summary>
    private void RunEditBar(bool show, string style)
    {
        if (!show)
        {
            _editBar?.Hide();
            return;
        }

        if (_editBar is null)
        {
            _editBar = new CooldownEditBar(
                look => Change(s => s.AssistCooldownStyle = look),
                () => Change(s => s.AssistCooldownsTogether = !s.AssistCooldownsTogether),
                () => Change(s => s.AssistCooldownScale = Math.Round(Math.Clamp(s.AssistCooldownScale - 0.1, 0.6, 2.0), 2)),
                () => Change(s => s.AssistCooldownScale = Math.Round(Math.Clamp(s.AssistCooldownScale + 0.1, 0.6, 2.0), 2)),
                () => Change(s => s.AssistOverlayLocked = true));
            _editBar.SizeChanged += (_, _) => PlaceEditBar();
        }

        string state = style + "|" + _settings.AssistCooldownsTogether;
        if (!Equals(_editBar.Tag, state))
        {
            _editBar.Tag = state;
            _editBar.ShowState(style, _settings.AssistCooldownsTogether);
        }

        if (!_editBar.IsVisible)
        {
            _editBar.Show();
            PlaceEditBar();
        }
    }

    /// <summary>The bar's place: the middle of the top of the first screen.</summary>
    private void PlaceEditBar()
    {
        if (_editBar is null)
        {
            return;
        }

        Rect area = WindowPlacement.Primary();
        double w = _editBar.ActualWidth > 0 ? _editBar.ActualWidth : 520;
        _editBar.Left = area.Left + (area.Width - w) / 2;
        _editBar.Top = area.Top + 12;
    }

    private void Run(ref AssistOverlayWindow? window, bool on, bool inGame)
    {
        if (!on)
        {
            window?.ShowOverlay(false);
            return;
        }

        bool locked = _settings.AssistOverlayLocked;
        window ??= new AssistOverlayWindow(true);
        window.ApplyLockedIfChanged(locked);
        window.Panel.Hidden = _hidden;
        window.Panel.SortByTime = _settings.AssistSortByTime;
        window.Panel.Look = _settings.AssistCooldownStyle == CooldownArt.Tiles ? CooldownArt.Tiles : CooldownArt.Bars;
        window.Panel.BarWidth = Math.Clamp(_settings.AssistBarWidth, 90, 400);
        if (!inGame && locked)
        {
            // Hidden while the game is not in front (never on the desktop): the panel is not rebuilt for nobody to see. While being placed it shows, as the
            // skills' cooldowns do.
            window.ShowOverlay(false);
            return;
        }

        window.Panel.Refresh(_directory());
        if (locked && !window.Panel.HasContent)
        {
            window.ShowOverlay(false);
            return;
        }

        window.ShowOverlay(true);
    }
}
