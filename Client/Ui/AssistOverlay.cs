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
    private double _opacityNow = 1, _readyOpacityNow = 1;

    /// <summary>How see-through a cooldown is drawn: the Skill Assist's opacity, times the ready skills' own while it is ready.</summary>
    private double FadeOf(bool running) => _opacityNow * (running ? 1 : _readyOpacityNow);
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

    /// <summary>The look of the cooldowns now (no places): what a class keeps of its own.</summary>
    private static CooldownPreset LookOf(MeterSettings s) => new()
    {
        SavedAt = DateTime.Now,
        Style = s.AssistCooldownStyle == CooldownArt.Tiles ? CooldownArt.Tiles : CooldownArt.Bars,
        Scale = s.AssistCooldownScale,
        BarWidth = s.AssistBarWidth,
        Together = s.AssistCooldownsTogether,
        ShowReady = s.AssistShowReady,
        Opacity = s.AssistCooldownOpacity,
        ReadyOpacity = s.AssistReadyOpacity,
        BackOpacity = s.AssistBackOpacity,
    };

    private static void PutLook(MeterSettings s, CooldownPreset p)
    {
        s.AssistCooldownStyle = p.Style == CooldownArt.Tiles ? CooldownArt.Tiles : CooldownArt.Bars;
        s.AssistCooldownScale = Math.Clamp(p.Scale, 0.5, 3);
        s.AssistBarWidth = Math.Clamp(p.BarWidth, 90, 400);
        s.AssistCooldownsTogether = p.Together;
        s.AssistShowReady = p.ShowReady;
        s.AssistCooldownOpacity = Math.Clamp(p.Opacity, 0.1, 1);
        s.AssistReadyOpacity = Math.Clamp(p.ReadyOpacity, 0.1, 1);
        s.AssistBackOpacity = Math.Clamp(p.BackOpacity, 0, 1);
    }

    /// <summary>
    /// Another class is played (its record at the login, its first skill, or picked in the Skill Assist): the look of the class before is kept for it, and the
    /// new class's own look is put on (a class seen for the first time keeps the look of the moment). The places are kept by skill, so each class's skills
    /// stand where they were put for it. Called on the window's thread.
    /// </summary>
    public static void OnClassChanged(int before, int after)
    {
        var s = MeterSettings.Load();
        if (before != 0)
        {
            s.AssistClassLooks[before] = LookOf(s);
        }

        if (s.AssistClassLooks.TryGetValue(after, out CooldownPreset? look) && look is not null)
        {
            PutLook(s, look);
        }

        s.AssistPresetActive = 0; // the presets are the class's own
        s.Save();
        PlacesVersion++;
        AskReload();
        EditBarChanged?.Invoke();
    }

    /// <summary>The preset slots of a class (its two digits; 0, no class known: the shared slots of before). The shared ones of before go to the class whose
    /// skills they hold, once.</summary>
    internal static Dictionary<int, CooldownPreset> PresetsOf(MeterSettings s, int cls)
    {
        foreach (var (slot, old) in s.AssistPresets.ToList())
        {
            int owner = old.Places.Keys.Concat(old.TilePlaces.Keys).Concat(old.Extra).Select(Aion2Cooldowns.ClassOf).Where(c => c != 0)
                .GroupBy(c => c).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            if (owner == 0)
            {
                continue;
            }

            if (!s.AssistClassPresets.TryGetValue(owner, out var slots))
            {
                s.AssistClassPresets[owner] = slots = new();
            }

            slots.TryAdd(slot, old);
            s.AssistPresets.Remove(slot);
        }

        if (cls == 0)
        {
            return s.AssistPresets;
        }

        if (!s.AssistClassPresets.TryGetValue(cls, out var mine))
        {
            s.AssistClassPresets[cls] = mine = new();
        }

        return mine;
    }

    /// <summary>The cooldowns as they sit on the screen now, kept in a preset slot (1 to 3) of the class played: its look and the places of its skills.</summary>
    public static void SavePreset(int slot)
    {
        var s = MeterSettings.Load();
        int cls = Aion2Cooldowns.CurrentClass();
        bool Mine(int id) => cls == 0 || Aion2Cooldowns.ClassOf(id) == cls;
        CooldownPreset preset = LookOf(s);
        preset.Places = s.AssistCooldownPlaces.Where(kv => Mine(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
        preset.TilePlaces = s.AssistCooldownTilePlaces.Where(kv => Mine(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
        preset.Hidden = s.AssistOverlayHidden.Where(Mine).ToList();
        preset.Extra = s.AssistExtraSkills.Where(Mine).ToList();
        PresetsOf(s, cls)[slot] = preset;
        s.AssistPresetActive = slot;
        s.Save();
    }

    /// <summary>A preset of the class played back on the screen: every cooldown of the class goes to its place in it, with its look and size (the windows are
    /// made again there); the other classes' places are left as they are. False when the slot is empty.</summary>
    public static bool LoadPreset(int slot)
    {
        var s = MeterSettings.Load();
        int cls = Aion2Cooldowns.CurrentClass();
        if (!PresetsOf(s, cls).TryGetValue(slot, out CooldownPreset? p) || p is null)
        {
            return false;
        }

        bool Mine(int id) => cls == 0 || Aion2Cooldowns.ClassOf(id) == cls;
        PutLook(s, p);
        Replace(s.AssistCooldownPlaces, p.Places, Mine);
        Replace(s.AssistCooldownTilePlaces, p.TilePlaces, Mine);
        s.AssistOverlayHidden = s.AssistOverlayHidden.Where(id => !Mine(id)).Concat(p.Hidden).Distinct().ToList();
        s.AssistExtraSkills = s.AssistExtraSkills.Where(id => !Mine(id)).Concat(p.Extra).Distinct().ToList();
        s.AssistPresetActive = slot;
        DropDocks(s);
        s.Save();
        PlacesVersion++; // the windows are made again at the preset's places, with its size (not rescaled from the old one)
        AskReload();
        EditBarChanged?.Invoke();
        return true;
    }

    private static void Replace(Dictionary<int, double[]> places, Dictionary<int, double[]> from, Func<int, bool> mine)
    {
        foreach (int id in places.Keys.Where(mine).ToList())
        {
            places.Remove(id);
        }

        foreach (var (id, at) in from)
        {
            if (at is { Length: 2 })
            {
                places[id] = at.ToArray();
            }
        }
    }

    /// <summary>The classes that have a layout of their own to copy (places of their skills on the screen), by their two digits.</summary>
    internal static IReadOnlyList<int> ClassesWithLayout(MeterSettings s) =>
        s.AssistCooldownPlaces.Keys.Concat(s.AssistCooldownTilePlaces.Keys).Select(Aion2Cooldowns.ClassOf).Where(c => c != 0).Distinct().OrderBy(c => c).ToList();

    /// <summary>
    /// The layout of class <paramref name="from"/> put on the class played (7 November 2026, the owner: "implement a preset from another class to that class,
    /// similar positions of squares and bars, if they're lazy"): its look, and its places one by one onto this class's skills - the first skill of its bar
    /// takes the place of the first skill of the other class's bar, and so on (without a known bar, in the order the places stand on the screen, top to
    /// bottom, then left to right). False when there is nothing to copy.
    /// </summary>
    public static bool CopyLayout(int from)
    {
        var s = MeterSettings.Load();
        int to = Aion2Cooldowns.CurrentClass();
        if (to == 0 || from == to)
        {
            return false;
        }

        List<int> source = BarOrder(s, from), target = BarOrder(s, to);
        target.AddRange(s.AssistExtraSkills.Where(id => Aion2Cooldowns.ClassOf(id) == to && !target.Contains(id)));
        target.AddRange(Aion2Cooldowns.ClassSkills(to).Where(id => !target.Contains(id)));
        bool any = false;
        foreach (Dictionary<int, double[]> places in new[] { s.AssistCooldownPlaces, s.AssistCooldownTilePlaces })
        {
            // the other class's placed skills: its bar's order first, then the rest as they stand on the screen
            var placed = source.Where(places.ContainsKey).ToList();
            placed.AddRange(places.Where(kv => Aion2Cooldowns.ClassOf(kv.Key) == from && !placed.Contains(kv.Key))
                .OrderBy(kv => kv.Value[1]).ThenBy(kv => kv.Value[0]).Select(kv => kv.Key));
            for (int i = 0; i < Math.Min(placed.Count, target.Count); i++)
            {
                places[target[i]] = places[placed[i]].ToArray();
                any = true;
            }
        }

        if (s.AssistClassLooks.TryGetValue(from, out CooldownPreset? look) && look is not null)
        {
            PutLook(s, look);
            any = true;
        }

        if (!any)
        {
            return false;
        }

        s.AssistPresetActive = 0;
        DropDocks(s);
        s.Save();
        PlacesVersion++;
        AskReload();
        EditBarChanged?.Invoke();
        return true;
    }

    /// <summary>A remembered skill bar of a character of the class (base ids in slot order; empty when none is known).</summary>
    private static List<int> BarOrder(MeterSettings s, int cls) =>
        s.AssistBars.Values.Where(bar => bar.Count > 0 && bar.Any(id => Aion2Cooldowns.ClassOf(id) == cls))
            .OrderByDescending(bar => bar.Count).Select(bar => bar.Where(id => Aion2Cooldowns.ClassOf(id) == cls).Distinct().ToList()).FirstOrDefault() ?? new List<int>();

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

    private List<int>? _barBefore;
    private int _barPresetBefore;

    /// <summary>
    /// A skill swapped onto the bar in the game takes the place on the screen of the one it replaced, in both looks (5 November 2026, the owner: "when swapping
    /// a skill it should go to the replaced skill's spot, instead of randomly at the top"). A swap is a slot of the bar holding another skill, the old one gone
    /// from the bar and the new one not on it before; at most two slots at once (a preset switched, or another character, is no swap).
    /// </summary>
    private void FollowSwaps(IReadOnlyList<int>? bar, int preset)
    {
        if (bar is not { Count: > 0 })
        {
            return;
        }

        List<int>? before = _barBefore;
        bool samePreset = preset == _barPresetBefore;
        _barBefore = bar.ToList();
        _barPresetBefore = preset;
        if (before is null || !samePreset || before.SequenceEqual(bar))
        {
            return;
        }

        List<(int Was, int Now)> swaps = Swaps(before, bar);
        if (swaps.Count is 0 or > 2)
        {
            return;
        }

        MeterSettings s = MeterSettings.Load();
        bool changed = false;
        foreach ((int was, int now) in swaps)
        {
            foreach (Dictionary<int, double[]> places in new[] { s.AssistCooldownPlaces, s.AssistCooldownTilePlaces })
            {
                if (places.TryGetValue(was, out double[]? p) && p is { Length: 2 })
                {
                    places[now] = p.ToArray();
                    changed = true;
                }
            }

            // its window, if it was up already (used before the swap came), moves there now
            if (_skills.TryGetValue(now, out CooldownSkillWindow? window)
                && CooldownSkillWindow.PlacesOf(s, s.AssistCooldownStyle == CooldownArt.Tiles ? CooldownArt.Tiles : CooldownArt.Bars).TryGetValue(now, out double[]? at))
            {
                window.MoveTo(at[0], at[1]);
            }
        }

        if (changed)
        {
            s.Save();
            _settings = s;
        }
    }

    /// <summary>The slots of the bar that hold another skill now: the old one gone from the bar, the new one not on it before.</summary>
    internal static List<(int Was, int Now)> Swaps(IReadOnlyList<int> before, IReadOnlyList<int> now)
    {
        var swaps = new List<(int, int)>();
        for (int i = 0; i < Math.Min(before.Count, now.Count); i++)
        {
            if (before[i] != now[i] && !now.Contains(before[i]) && !before.Contains(now[i]))
            {
                swaps.Add((before[i], now[i]));
            }
        }

        return swaps;
    }

    /// <summary>The skills' windows: the skills on the bar (else those known to have a cooldown) that are ticked, each where it was put.</summary>
    private void RunSkills(bool inGame)
    {
        Aion2EntityDirectory? dir = _directory();
        IReadOnlyList<int>? bar = SkillBars.For(dir); // the bar the game sent, else the character's remembered one
        FollowSwaps(bar, dir?.ActivePreset ?? 0);
        var listed = Aion2Cooldowns.Skills(bar).Where(id => AssistPanel.Listed(id, bar)).ToList();
        // the class's other skills the player ticked to show too (Skill Assist, "Other skills"), after the bar's
        int cls = Aion2Cooldowns.CurrentClass(bar);
        listed.AddRange(_settings.AssistExtraSkills.Where(id => Aion2Cooldowns.ClassOf(id) == cls && !listed.Contains(id) && AssistPanel.Offered(id)).Distinct().ToList());
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
        (_opacityNow, _readyOpacityNow) = (Math.Clamp(_settings.AssistCooldownOpacity, 0.1, 1), Math.Clamp(_settings.AssistReadyOpacity, 0.1, 1));
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
            window.Backdrop(Math.Clamp(_settings.AssistBackOpacity, 0, 1));
            CooldownView view = Aion2Cooldowns.View(id, now);
            window.Show(view);
            window.Fade(FadeOf(view.Running));
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
                window.Fade(FadeOf(view.Running));
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
