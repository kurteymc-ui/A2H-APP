using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDPS.Aion2;

namespace AionDPS.Ui;

/// <summary>
/// An assist overlay: the own skills as pictures, each in its place, with their cooldowns on them, or the effects on the own character (<see cref="AssistPanel"/>) - two
/// overlays, each placed, scaled and locked like the other overlays (<see cref="MovableOverlay"/>); <see cref="AssistController"/> shows them only while
/// the game is in front.
/// </summary>
public sealed class AssistOverlayWindow : MovableOverlay
{
    public AssistPanel Panel { get; } = new(true) { MinWidth = 44, MinHeight = 40 };

    public AssistOverlayWindow(bool effects = false) : base(effects ? OverlayPlaces.Effects : OverlayPlaces.Assist, new Border { Child = null }, effects ? LoadEffects : Load, effects ? SaveEffects : Save,
        SystemParameters.WorkArea.Left + (effects ? 300 : 40), 520, effects ? LoadEffectsSize : LoadSize, effects ? SaveEffectsSize : SaveSize, OverlayEdgeMode.Width)
    {
        Panel.Part = effects ? AssistPart.Effects : AssistPart.Cooldowns;
        Body = new Border
        {
            // one quiet backdrop, no colour of its own: the pictures are what is read
            Background = new SolidColorBrush(Color.FromArgb(0x73, 0x0B, 0x0D, 0x10)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 6, 2, 2), Child = Panel,
        };
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

/// <summary>Runs the two assist overlays (cooldowns, effects): eight times a second it refreshes them (the cooldowns' sweep runs smoothly), and shows them
/// only while the game is in front (locked or not: never on the desktop).</summary>
public sealed class AssistController : IDisposable
{
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(125) };
    private readonly Func<Aion2EntityDirectory?> _directory;
    private AssistOverlayWindow? _overlay;
    private AssistOverlayWindow? _effects;
    private MeterSettings _settings = MeterSettings.Load();
    private DateTime _settingsAt = DateTime.UtcNow;
    private HashSet<int> _hidden = new();

    /// <summary>The <see cref="Aion2Cooldowns.LearnedVersion"/> the settings have (the skills learned to have no cooldown are kept there).</summary>
    private int _learnedSaved = -1;

    /// <summary>The <see cref="Aion2Cooldowns.KnownVersion"/> and <see cref="Aion2Cooldowns.RatesVersion"/> the settings have (the skills seen with a
    /// cooldown and the speed of their counters are kept there).</summary>
    private int _knownSaved = -1, _ratesSaved = -1;

    public AssistController(Func<Aion2EntityDirectory?> directory)
    {
        _directory = directory;
        Aion2Cooldowns.SeedLearned(_settings.AssistNoCooldown);
        Aion2Cooldowns.SeedKnown(_settings.AssistKnownSkills);
        Aion2Cooldowns.SeedRates(_settings.AssistCooldownRates);
        _learnedSaved = Aion2Cooldowns.LearnedVersion;
        _knownSaved = Aion2Cooldowns.KnownVersion;
        _ratesSaved = Aion2Cooldowns.RatesVersion;
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public void RaiseToFront()
    {
        _overlay?.RaiseToFront();
        _effects?.RaiseToFront();
    }

    public void Dispose()
    {
        _timer.Stop();
        _overlay?.Close();
        _effects?.Close();
    }

    private void Tick()
    {
        if ((DateTime.UtcNow - _settingsAt).TotalSeconds >= 1)
        {
            _settings = MeterSettings.Load();
            _settingsAt = DateTime.UtcNow;
            _hidden = _settings.AssistOverlayHidden.ToHashSet();
            if (Aion2Cooldowns.LearnedVersion != _learnedSaved)
            {
                // the skills learned to have no cooldown, kept for the next start
                _learnedSaved = Aion2Cooldowns.LearnedVersion;
                List<int> learned = Aion2Cooldowns.LearnedNoCooldown();
                if (!learned.SequenceEqual(_settings.AssistNoCooldown.OrderBy(i => i)))
                {
                    _settings.AssistNoCooldown = learned;
                    _settings.Save();
                }
            }

            if (Aion2Cooldowns.KnownVersion != _knownSaved || Aion2Cooldowns.RatesVersion != _ratesSaved)
            {
                // the skills seen with a cooldown (the overlay shows them from the start) and how fast their counters run (the first second is right too)
                _knownSaved = Aion2Cooldowns.KnownVersion;
                _ratesSaved = Aion2Cooldowns.RatesVersion;
                List<int> known = Aion2Cooldowns.KnownSkills();
                Dictionary<int, double> rates = Aion2Cooldowns.TypicalRates();
                foreach ((int id, double rate) in _settings.AssistCooldownRates)
                {
                    rates.TryAdd(id, rate); // a skill not used this session keeps its speed
                }

                if (!known.SequenceEqual(_settings.AssistKnownSkills) || rates.Count != _settings.AssistCooldownRates.Count
                    || rates.Any(kv => !_settings.AssistCooldownRates.TryGetValue(kv.Key, out double kept) || Math.Abs(kept - kv.Value) >= 1))
                {
                    _settings.AssistKnownSkills = known;
                    _settings.AssistCooldownRates = rates;
                    _settings.Save();
                }
            }
        }

        bool inGame = GameWindow.OverlayMayShow();
        Run(ref _overlay, _settings.ShowAssistOverlay, effects: false, inGame);
        Run(ref _effects, _settings.EffectsOverlayOn, effects: true, inGame);
    }

    private void Run(ref AssistOverlayWindow? window, bool on, bool effects, bool inGame)
    {
        if (!on)
        {
            window?.ShowOverlay(false);
            return;
        }

        bool locked = _settings.AssistOverlayLocked;
        window ??= new AssistOverlayWindow(effects);
        window.ApplyLockedIfChanged(locked);
        window.Panel.Hidden = _hidden;
        window.Panel.SortByTime = _settings.AssistSortByTime;
        window.Panel.ShowReady = _settings.AssistShowReady;
        if (!inGame)
        {
            // Hidden while the game is not in front (locked or not: never on the desktop): the panel is not rebuilt for nobody to see (four times a second).
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
