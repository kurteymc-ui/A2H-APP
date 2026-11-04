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
/// own (<see cref="CooldownSkillWindow"/>), placed where the player put it; the effects are one overlay. Shown only while the game is in front (never on the
/// desktop); unlocked, every ticked skill shows (also when ready) to be placed. Also keeps what the cooldowns learned in the settings and says the skills
/// ticked for it when they come back.
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

    /// <summary>The <see cref="Aion2Cooldowns.LearnedVersion"/>, <see cref="Aion2Cooldowns.KnownVersion"/> and <see cref="Aion2Cooldowns.GapsVersion"/> the
    /// settings have.</summary>
    private int _learnedSaved = -1, _knownSaved = -1, _gapsSaved = -1;

    /// <summary>Counts every "reset places" of the cooldowns: the windows are made again at their first places.</summary>
    public static int PlacesVersion { get; private set; }

    /// <summary>Every skill's cooldown back at its first place (one column of bars or one row of tiles, hanging together).</summary>
    public static void ResetCooldownPlaces()
    {
        var s = MeterSettings.Load();
        s.AssistCooldownPlaces.Clear();
        foreach (string title in s.OverlayDocks.Keys.Where(CooldownSkillWindow.IsCooldownTitle).ToList())
        {
            s.OverlayDocks.Remove(title);
        }

        s.Save();
        PlacesVersion++;
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
    }

    public void Dispose()
    {
        _timer.Stop();
        _effects?.Close();
        foreach (CooldownSkillWindow w in _skills.Values)
        {
            w.Close();
        }

        _skills.Clear();
    }

    private void Tick()
    {
        if ((DateTime.UtcNow - _settingsAt).TotalSeconds >= 1)
        {
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

        if (changed)
        {
            _settings.Save();
        }
    }

    /// <summary>The skills' windows: the skills on the bar (else those known to have a cooldown) that are ticked; a new one goes after the last one shown.</summary>
    private void RunSkills(bool inGame)
    {
        Aion2EntityDirectory? dir = _directory();
        IReadOnlyList<int>? bar = dir?.LocalBarOrder is { Count: > 0 } order ? order : null;
        var listed = Aion2Cooldowns.Skills(bar).Where(id => AssistPanel.Listed(id, bar)).ToList();
        var shown = listed.Where(id => !_hidden.Contains(id)).ToList();
        DateTime now = DateTime.Now; // the capture stamps its frames with the local clock
        _voice.Tick(shown, _settings, now, inGame && _settings.ShowAssistOverlay);
        if (PlacesVersion != _placesSeen)
        {
            _placesSeen = PlacesVersion;
            foreach (CooldownSkillWindow w in _skills.Values)
            {
                w.Close();
            }

            _skills.Clear();
        }

        if (!_settings.ShowAssistOverlay || !inGame)
        {
            // hidden while the game is not in front (locked or not: never on the desktop)
            foreach (CooldownSkillWindow w in _skills.Values)
            {
                w.ShowOverlay(false);
            }

            return;
        }

        bool locked = _settings.AssistOverlayLocked;
        string style = _settings.AssistCooldownStyle == CooldownArt.Tiles ? CooldownArt.Tiles : CooldownArt.Bars;
        double scale = Math.Clamp(_settings.AssistCooldownScale, 0.5, 3);
        double width = Math.Clamp(_settings.AssistBarWidth, 90, 400);
        CooldownSkillWindow? previous = null;
        foreach (int id in shown)
        {
            if (!_skills.TryGetValue(id, out CooldownSkillWindow? window))
            {
                window = Create(id, previous, style, scale, width);
                _skills[id] = window;
            }

            window.Use(style, width);
            window.UseScale(scale);
            window.ApplyLockedIfChanged(locked);
            CooldownView view = Aion2Cooldowns.View(id, now);
            window.Show(view);
            window.ShowOverlay(!locked || view.Running || _settings.AssistShowReady);
            previous = window;
        }

        foreach ((int id, CooldownSkillWindow w) in _skills)
        {
            if (!shown.Contains(id))
            {
                w.ShowOverlay(false);
            }
        }
    }

    /// <summary>A skill's window. Without a place of its own it goes under the bar before it (to the right of the tile before it) and hangs on it, so the
    /// first ones moved carry the others; the first of all at the left of the screen.</summary>
    private CooldownSkillWindow Create(int id, CooldownSkillWindow? previous, string style, double scale, double width)
    {
        bool tiles = style == CooldownArt.Tiles;
        double w = (tiles ? CooldownArt.TileSize + 2 : width) * scale + 2, h = (tiles ? CooldownArt.TileSize + 2 : CooldownArt.BarHeight) * scale + 2;
        double left = SystemParameters.WorkArea.Left + 60, top = SystemParameters.WorkArea.Top + SystemParameters.WorkArea.Height * 0.35;
        bool placed = CooldownSkillWindow.HasPlace(_settings, id);
        if (!placed && previous is not null && double.IsFinite(previous.Left) && double.IsFinite(previous.Top))
        {
            double pw = previous.ActualWidth > 0 ? previous.ActualWidth : w, ph = previous.ActualHeight > 0 ? previous.ActualHeight : h;
            (left, top) = tiles ? (previous.Left + pw, previous.Top) : (previous.Left, previous.Top + ph);
            var s = MeterSettings.Load();
            s.OverlayDocks[CooldownSkillWindow.TitleOf(id)] = new OverlayDock { Parent = previous.Title, Side = tiles ? "Right" : "Bottom", Offset = 0 };
            s.Save();
        }

        var window = new CooldownSkillWindow(id, left, top);
        window.Use(style, width);
        window.UseScale(scale);
        return window;
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
        if (!inGame)
        {
            // Hidden while the game is not in front (locked or not: never on the desktop): the panel is not rebuilt for nobody to see.
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
