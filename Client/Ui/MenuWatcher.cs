using System.Runtime.InteropServices;

namespace AionDPS.Ui;

/// <summary>
/// Hides the meter's overlays while a game window is open (Arcana, enchanting, the inventory ...), by two signs: the mouse cursor (see <see cref="MenuDetector"/>,
/// only in the control mode that hides it while playing) and the game window's picture (see <see cref="HudWatcher"/>, PrintWindow: the game's HUD is
/// covered, in every control mode).
/// <see cref="Hidden"/> is what <see cref="GameWindow"/> reads, so every overlay that is shown only while the game is in front steps aside by itself. Runs on the
/// UI thread, so the flag needs no lock.
/// </summary>
public sealed class MenuWatcher : IDisposable
{
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly MenuDetector _detector = new();
    private readonly HudWatcher _hud = new();
    private DateTime _lastHudLook = DateTime.MinValue;
    private readonly ControlModeLearner _learner = new();
    private MeterSettings _settings = MeterSettings.Load();
    private DateTime _settingsAt = DateTime.UtcNow;
    private DateTime _lastOwnHit = DateTime.MinValue;
    private int _look;
    private bool _held;
    private DateTime? _lastClick;

    public MenuWatcher()
    {
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    /// <summary>True while a menu is open and the overlays should be out of the way.</summary>
    public static bool Hidden { get; private set; }

    /// <summary>Whether the game's HUD has been learnt from the screen (then game windows are told in every control mode).</summary>
    public static bool ScreenReady { get; private set; }

    /// <summary>How many looks in fights the screen has learnt from so far (it needs <see cref="HudWatcher.NeedLooks"/>).</summary>
    public static int ScreenLearnt { get; private set; }

    /// <summary>The control mode seen so far (also kept in the settings).</summary>
    public static ControlMode Mode { get; private set; } = ControlMode.Unknown;

    /// <summary>The local player just dealt damage (the control mode is learnt while fighting).</summary>
    public void NoteOwnHit() => _lastOwnHit = DateTime.UtcNow;

    public void Dispose()
    {
        _timer.Stop();
        _hud.Dispose();
        Hidden = false;
    }

    /// <summary>
    /// How often the game window's picture is taken: each look draws the whole window (PrintWindow), which the game feels at high resolutions (3 November
    /// 2026 review: 3.3 a second all the time). About three a second only while a game window can open any moment (the cursor shows, no fight) or one is
    /// open (so the overlays come back quickly); in a fight and in action-camera play once a second, which is what the learning takes - a window opened
    /// then steps the overlays aside within a second.
    /// </summary>
    private TimeSpan HudEvery(bool cursor) =>
        _hud.MenuOpen || (cursor && DateTime.UtcNow - _lastOwnHit >= TimeSpan.FromSeconds(1.5)) ? TimeSpan.FromMilliseconds(400) : TimeSpan.FromSeconds(1);

    public static bool AllOverlaysLocked(MeterSettings s) => UnlockedOverlays(s).Count == 0;

    /// <summary>Localization keys of the names of the overlays that are unlocked (being positioned), which keeps the menus from hiding anything.</summary>
    public static List<string> UnlockedOverlays(MeterSettings s)
    {
        var open = new List<string>();
        void Check(bool locked, string key)
        {
            if (!locked)
            {
                open.Add(key);
            }
        }

        Check(s.PetFarmLocked, "Settings.PetMap.CardMap");
        Check(s.PetListLocked ?? true, "Settings.PetMap.CardList");
        Check(s.PetInfoLocked, "Settings.PetInfo.Title");
        Check(s.TimetableLocked, "Settings.Hotkey.Timetable");
        Check(s.BossOverlayLocked, "Settings.Menu.Name.Boss");
        Check(s.NetworkOverlayLocked, "Settings.Menu.Name.Network");
        Check(s.CombatOverlayLocked, "Combat.Element.TargetOfTarget");
        Check(s.CombatCastOverlayLocked ?? true, "Settings.Combat.CardBossCast");
        Check(s.HotkeyOverlayLocked ?? true, "Settings.HotkeyOverlay.Title");
        Check(s.AssistOverlayLocked, "Settings.Menu.Name.Assist");
        Check(s.LootOverlayLocked, "Settings.Menu.Name.Loot");
        Check(s.GatherOverlayLocked, "Settings.Nav.Gather");
        Check(s.SeasonOverlayLocked, "Checklist.Season");
        Check(s.CraftOverlayLocked, "Settings.Menu.Name.Craft");
        Check(s.OverlayMenuLocked, "Shell.OvMenu");
        return open;
    }

    private void Tick()
    {
        if ((DateTime.UtcNow - _settingsAt).TotalSeconds >= 1)
        {
            _settings = MeterSettings.Load();
            _settingsAt = DateTime.UtcNow;
            if (Enum.TryParse(_settings.MenuControlMode, true, out ControlMode saved) && _learner.Mode == ControlMode.Unknown && saved != ControlMode.Unknown)
            {
                Mode = saved; // what an earlier session learnt counts until this one has seen enough
            }
        }

        bool inGame = GameWindow.ForegroundIsGame();
        bool cursor = inGame && GameWindow.IsCursorVisible();
        ControlMode learned = _learner.Note(DateTime.UtcNow, inGame, DateTime.UtcNow - _lastOwnHit < TimeSpan.FromSeconds(2), cursor);
        if (learned != ControlMode.Unknown && learned != Mode)
        {
            Mode = learned;
            _settings.MenuControlMode = learned.ToString();
            var onDisk = MeterSettings.Load();
            onDisk.MenuControlMode = learned.ToString();
            onDisk.Save();
        }

        if (!_settings.HideDuringMenus)
        {
            _detector.Reset();
            _hud.Reset();
            Hidden = false;
            return;
        }

        // an unlocked overlay (being placed) pauses only the cursor's sign: the cursor shows while one drags it. The screen still tells a game window,
        // also while overlays are unlocked (their own windows are left out of what it looks at)
        bool placing = !AllOverlaysLocked(_settings);
        ScreenReady = _hud.Ready;
        ScreenLearnt = _hud.LearntLooks;
        bool hudMenu = false;
        if (!inGame)
        {
            _hud.Reset();
        }
        else if (DateTime.UtcNow - _lastHudLook >= HudEvery(cursor) && GameWindow.ForegroundClientArea(ignoreMenu: true) is { } area)
        {
            _lastHudLook = DateTime.UtcNow;
            // the picture in the window's real pixels (Windows' scaling of another monitor made it 4480 x 2520 for a 2560 x 1440 window)
            IntPtr game = GameWindow.ForegroundGame();
            System.Drawing.Rectangle real = GameWindow.ClientSizeInPixels(game) is { } size ? new System.Drawing.Rectangle(area.Location, size) : area;
            hudMenu = _hud.Look(game, real, DateTime.UtcNow - _lastOwnHit < TimeSpan.FromSeconds(1.5), DateTime.UtcNow);
        }
        else
        {
            hudMenu = _hud.MenuOpen;
        }

        if (Mode == ControlMode.Classic)
        {
            _detector.Reset();
            Hidden = hudMenu; // the cursor shows all the time in this mode: only the screen tells
            return;
        }

        // the keys are asked only while the cursor shows, and every other look: during play this costs nothing
        if (!cursor)
        {
            _held = false;
            _look = 0;
        }
        else if (_look++ % 2 == 0)
        {
            _held = GameWindow.AnyKeyDown();
        }

        if (inGame && GameWindow.MouseButtonDown())
        {
            _lastClick = DateTime.UtcNow;
        }

        bool menu = _detector.Update(DateTime.UtcNow, inGame, cursor, _held, placing, _lastClick);
        Hidden = menu || hudMenu;
    }
}
