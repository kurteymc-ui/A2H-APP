using System.Windows;
using System.Windows.Media;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>
/// Runs the pet map and its list: every frame of the screen the player's arrow and the view follow the position guess (<see cref="MarkerMotion"/>),
/// twenty times a second what the map shows is worked out (the pets, the route, the list), once a second the map is recognised (from the pet monsters
/// around). Both windows are shown while the game is in front (only then, locked or not: never on the desktop); with nothing to show they stay hidden.
/// </summary>
public sealed class PetMapController : IDisposable
{
    private readonly PetMapWindow _map = new();
    private readonly PetListWindow _list = new();
    private readonly Func<Aion2EntityDirectory?> _directory;
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private Aion2MapInfo? _current;
    private double _x, _y;
    private (float X, float Y)? _lastTarget;
    private List<(int PetId, float X, float Y)> _points = new();
    private List<(int PetId, float X, float Y)> _pointsShown = new();
    private string _pointsKey = "";
    private List<(string Kind, string Key, float X, float Y)> _gather = new();
    private DateTime _lastDetect = DateTime.MinValue;
    private int _ticks;
    private MeterSettings _settings = MeterSettings.Load();
    private DateTime _settingsAt = DateTime.UtcNow;
    private string? _faction = Aion2Gather.OwnFaction();
    private HashSet<int> _savedMax = SavedMaxPets();

    public PetMapController(Func<Aion2EntityDirectory?> directory)
    {
        _directory = directory;
        Hook(_map);
        _timer.Tick += (_, _) =>
        {
            _frame = null; // the tick sets it again when the map is drawn with a known position
            Tick();
            HookFrames();
        };
        _timer.Start();
    }

    // ---- what a dot is, when the mouse rests on it ----

    private IReadOnlyList<Aion2PetState> _states = Array.Empty<Aion2PetState>();

    private void Hook(PetMapWindow map)
    {
        map.Describe = DescribePet;
        map.DescribeGather = DescribeGather;
        map.ZoomRequested = Zoom;
        map.LockRequested = SetMapLocked;
    }

    /// <summary>The padlock on the map overlay was clicked: locked, it stays in place and the mouse drags the map inside it; unlocked, it can be moved and sized,
    /// and the map follows the player again. Kept in the settings (the overlay's own lock, the same as in the settings window).</summary>
    private void SetMapLocked(bool locked)
    {
        _settings.PetFarmLocked = locked; // at once, not only when the settings are read again
        _map.ApplyLockedIfChanged(locked);
        if (!locked)
        {
            _map.Follow = true;
        }

        var onDisk = MeterSettings.Load();
        if (onDisk.PetFarmLocked != locked)
        {
            onDisk.PetFarmLocked = locked;
            onDisk.Save();
        }
    }

    // ---- the zoom: from the close-up around the player out to the whole map (the wheel over the map, its + and − buttons, the hotkeys) ----

    private double _zoomTarget = StoredZoom();
    private double _zoomShown = StoredZoom();
    private DateTime? _zoomChangedAt;

    private static double StoredZoom() => MeterSettings.Load().PetMapZoom is var zoom && double.IsFinite(zoom) ? Math.Clamp(zoom, 0, 1) : 0;

    /// <summary>Zooms the map (and its views on the app's page, which show the same) by <paramref name="change"/>: 0 is the close-up, 1 the whole map, negative zooms in.</summary>
    public void Zoom(double change)
    {
        if (!double.IsFinite(change))
        {
            return;
        }

        _zoomTarget = Math.Clamp(_zoomTarget + change, 0, 1);
        _zoomChangedAt = DateTime.UtcNow;
    }

    private DateTime _zoomedAt = DateTime.UtcNow;

    /// <summary>The zoom glides to where it was put (most of the way within a quarter of a second, 35 % of what is left per 50 ms whatever the frame rate)
    /// and is kept in the settings a moment after the last change.</summary>
    private void StepZoom()
    {
        DateTime now = DateTime.UtcNow;
        double dt = Math.Clamp((now - _zoomedAt).TotalSeconds, 0, 0.5);
        _zoomedAt = now;
        double gap = _zoomTarget - _zoomShown;
        _zoomShown = Math.Abs(gap) < 0.002 ? _zoomTarget : _zoomShown + gap * (1 - Math.Pow(0.65, dt * 20));
        if (_zoomChangedAt is { } at && DateTime.UtcNow - at > TimeSpan.FromSeconds(1))
        {
            SaveZoom();
        }
    }

    /// <summary>The zoom into the settings file (once the wheel has come to rest, not with every notch), so that the map opens as it was left.</summary>
    private void SaveZoom()
    {
        _zoomChangedAt = null;
        var onDisk = MeterSettings.Load();
        if (Math.Abs(onDisk.PetMapZoom - _zoomTarget) > 1e-6)
        {
            onDisk.PetMapZoom = _zoomTarget;
            onDisk.Save();
        }
    }

    /// <summary>Zoomed out this far, the route shows up to twelve stops (close up the next six) and, with the route on, only the spawn points of its pets.</summary>
    private const double RouteAllZoom = 0.5;

    /// <summary>The pet, the monsters that give it, how far the collection is; for a monster announced now, that one is there.</summary>
    private string? DescribePet(int petId, bool live)
    {
        var loc = LocalizationManager.Instance;
        string language = loc.Language;
        if (petId == CubePet)
        {
            return Aion2Gather.Items().FirstOrDefault(i => i.Key == "Hidden Cube")?.NameIn(language) ?? "Hidden Cube";
        }

        string name = Aion2Pets.PetName(petId, language) ?? $"#{petId}";
        var lines = new List<string> { name };
        var mobs = Aion2Pets.NpcsOfPet(petId).Select(n => Aion2Npcs.NameOf(n, language)).Where(n => !string.IsNullOrEmpty(n)).Distinct().Take(4).ToList();
        if (mobs.Count > 0 && !(mobs.Count == 1 && mobs[0] == name))
        {
            lines.Add(string.Format(loc["PetMap.HoverMobs"], string.Join(", ", mobs)));
        }

        if (_states.FirstOrDefault(st => st.PetId == petId) is { } state)
        {
            lines.Add(state.Level >= Aion2Pets.TopLevel
                ? string.Format(loc["PetFarm.Level"], state.Level)
                : string.Format(loc["PetFarm.Level"], state.Level) + $" · {state.Progress}/{Aion2Pets.ProgressNeeded(state.Level)}");
        }

        if (live)
        {
            lines.Add(loc["PetMap.HoverLive"]);
        }

        return string.Join("\n", lines);
    }

    /// <summary>The collectible's name (by its key) in the player's language.</summary>
    private static string? DescribeGather(string key) =>
        Aion2Gather.Items().FirstOrDefault(i => i.Key == key)?.NameIn(LocalizationManager.Instance.Language);

    private PetRoutePlan? _stopTextsOf;
    private string _stopTextsLanguage = "";
    private IReadOnlyList<string> _stopTexts = Array.Empty<string>();

    /// <summary>For each stop of the route (up to twelve): its number, the area and its pets, shown when the mouse rests on its number.</summary>
    private IReadOnlyList<string> StopTexts(PetRoutePlan? plan)
    {
        var loc = LocalizationManager.Instance;
        if (plan is null)
        {
            return Array.Empty<string>();
        }

        if (!ReferenceEquals(plan, _stopTextsOf) || _stopTextsLanguage != loc.Language)
        {
            _stopTextsOf = plan;
            _stopTextsLanguage = loc.Language;
            // "[13] <Kibelisk>" first: where to teleport to for the stop
            _stopTexts = plan.Stops.Take(PetMapWindow.RouteBadges).Select(st =>
                (PetRoute.KibeliskOf(plan.MapKey, st) is { } kib ? $"[{st.Step}] {kib.Name}\n" : "")
                + string.Format(System.Globalization.CultureInfo.CurrentCulture, loc["PetRoute.Stop"], st.Step, plan.Stops.Count, st.Zone) + "\n"
                + string.Join(", ", st.Pets.Select(id => Aion2Pets.PetName(id, loc.Language) ?? PetRoute.NameOf(id) ?? $"#{id}"))).ToList();
        }

        return _stopTexts;
    }

    /// <summary>A switch was changed: the settings are read again on the next tick (not only once a second).</summary>
    public void SettingsChanged() => _settingsAt = DateTime.MinValue;

    public void ApplyLocked(bool locked)
    {
        _map.ApplyLocked(locked);
        _list.ApplyLocked(_settings.PetListLocked ?? locked);
    }

    /// <summary>The map and the list are two overlays, each with its own switch (the module's switch is the master): shown only when both are on.</summary>
    private readonly List<PetMapWindow> _mapViews = new();
    private readonly List<PetListWindow> _listViews = new();

    /// <summary>Never shown copies of the map and of the list whose content a tool window shows live (see <see cref="MovableOverlay.DetachBody"/>).</summary>
    public (PetMapWindow Map, PetListWindow List) CreateViews()
    {
        var map = new PetMapWindow();
        Hook(map);
        var list = new PetListWindow();
        _mapViews.Add(map);
        _listViews.Add(list);
        return (map, list);
    }

    public void RemoveViews(PetMapWindow map, PetListWindow list)
    {
        _mapViews.Remove(map);
        _listViews.Remove(list);
        map.Close();
        list.Close();
    }

    private string _listShown = "";

    private void RenderList(IReadOnlyList<(Color, string, string, string, bool)> rows)
    {
        _list.Render(rows);
        foreach (var view in _listViews)
        {
            view.Render(rows);
        }
    }

    private bool _hideOverlays;
    private bool _lastMenu;
    private DateTime? _shopSeenAt;
    private (float X, float Y)? _shopFrom;
    private bool _shopOpen;
    private bool _inShop;
    private bool _listUseless;
    private const double ShopListOpacity = 0.95;

    /// <summary>
    /// The Black Cloud shop's clock for the pet list: "new list in 36 min · this one started 24 min ago" (its product list renews every hour, on the hour:
    /// <see cref="Aion2BlackCloud.Clock"/>); null before the shop was ever opened with the meter running (6 November 2026, the owner: "remind me every
    /// time my shop refreshes ... 36 minutes left, started 24m ago").
    /// </summary>
    internal static string? ShopClock(DateTime utcNow)
    {
        if (Aion2BlackCloud.Clock(utcNow) is not { } clock)
        {
            return null;
        }

        LocalizationManager loc = LocalizationManager.Instance;
        // whole minutes, rounded as the game's own countdown reads (00:36:03: 36 min left, the list on offer for 24); under half a minute "<1 min"
        string Minutes(TimeSpan span)
        {
            int m = (int)Math.Round(Math.Max(0, span.TotalMinutes), MidpointRounding.AwayFromZero);
            return string.Format(System.Globalization.CultureInfo.CurrentCulture, loc["BlackCloud.Minutes"], m > 0 ? m.ToString(System.Globalization.CultureInfo.CurrentCulture) : "<1");
        }

        return string.Format(System.Globalization.CultureInfo.CurrentCulture, loc["BlackCloud.Clock"], Minutes(clock.Next - utcNow), Minutes(utcNow - clock.Since));
    }

    /// <summary>The Black Cloud shop counts as open from the moment it sends a tab until the player walks away from where they stood (a closed shop sends nothing, so there is no other end to read).</summary>
    private void UpdateShopOpen(Aion2EntityDirectory? directory)
    {
        DateTime? at = directory?.ShopFrameAt;
        if (directory is null || at is null)
        {
            _shopOpen = false;
            _shopSeenAt = null;
            return;
        }

        var position = directory.BestPosition;
        if (at != _shopSeenAt)
        {
            _shopSeenAt = at;
            _shopOpen = true;
            _shopFrom = position is { } p ? (p.X, p.Y) : null;
            return;
        }

        if (position is { } now)
        {
            if (_shopFrom is not { } from)
            {
                _shopFrom = (now.X, now.Y);
            }
            else if (_shopOpen && Math.Sqrt(Math.Pow(now.X - from.X, 2) + Math.Pow(now.Y - from.Y, 2)) > 300)
            {
                _shopOpen = false; // walked off (3 m): the shop is closed
            }
        }
    }

    private bool InMenu => MenuWatcher.Hidden || _inShop;

    public bool HasViews => _mapViews.Count > 0;

    private void ShowMap(bool visible) => _map.ShowOverlay(visible && !_hideOverlays && !InMenu && (_settings.PetMapShowMap || _cubeMapOverride));

    private void ShowList(bool visible) => _list.ShowOverlay(visible && !_hideOverlays && _settings.ShowPetMap && _settings.PetMapShowList);

    /// <summary>Sample rows for the list while it is positioned: a few pets and collectibles of the game, in the settings' size.</summary>
    private static IReadOnlyList<(Color, string, string, string, bool)> SampleRows()
    {
        string language = LocalizationManager.Instance.Language;
        var rows = new List<(Color, string, string, string, bool)>();
        int i = 0;
        foreach (int pet in Aion2Pets.AllPetIds.OrderBy(p => p).Take(3))
        {
            rows.Add((PetMapPalette.Of(i++), Aion2Pets.PetName(pet, language) ?? $"#{pet}", string.Format(LocalizationManager.Instance["PetFarm.Level"], 1) + " · 5/" + Aion2Pets.ProgressNeeded(1), $"{40 + i * 35} m", false));
        }

        foreach (var item in Aion2Gather.Items().Take(3))
        {
            rows.Add((PetMapPalette.OfGather(item.Kind), item.NameIn(language), "", $"~{60 + i++ * 20} m", true));
        }

        return rows;
    }

    public void RaiseToFront()
    {
        _map.RaiseToFront();
        _list.RaiseToFront();
    }

    public void Dispose()
    {
        _timer.Stop();
        _frame = null;
        HookFrames();
        if (_zoomChangedAt is not null)
        {
            SaveZoom();
        }

        _map.Close();
        _list.Close();
    }

    /// <summary>The pseudo pet id under which the live Hidden Cubes travel with the live monsters (the map draws a cube for it).</summary>
    private const int CubePet = PetMapWindow.CubeMark;

    /// <summary>Where the player's arrow is drawn now: the program's one arrow (<see cref="PlayerArrow"/>: an exact report at once, a step of the guess
    /// glided over), the place the cube line measures from too. Stepped every frame and every tick; the view's middle is the same place.</summary>
    private static (float X, float Y) Follow(Aion2EntityDirectory directory, (float X, float Y, DateTime At) p) => PlayerArrow.Step(directory, p);

    /// <summary>What the last tick drew the map with: every frame in between draws it again with the arrow moved on (null while the map is not drawn
    /// with a known position).</summary>
    private FrameRender? _frame;

    private sealed record FrameRender(Aion2MapInfo Map, double Radius, double Tiles, IReadOnlyList<(int PetId, float X, float Y)> Points,
        IReadOnlyList<(int PetId, float X, float Y)> Live, IReadOnlyDictionary<int, Color> Colors, IReadOnlyList<(float X, float Y)>? Guards,
        bool PetArrows, bool GatherArrows, bool Overlay);

    private bool _framesHooked;
    private DateTime _frameAt;
    private FrameRender? _frameDrawn;
    private double _headingDrawn, _zoomDrawn;

    /// <summary>The frames are followed only while there is a map to move (the screen's frame event keeps WPF drawing for as long as it is hooked).</summary>
    private void HookFrames()
    {
        bool want = _frame is not null && (_map.IsVisible || _mapViews.Count > 0);
        if (want == _framesHooked)
        {
            return;
        }

        _framesHooked = want;
        if (want)
        {
            CompositionTarget.Rendering += OnFrame;
        }
        else
        {
            CompositionTarget.Rendering -= OnFrame;
        }
    }

    /// <summary>
    /// Every frame of the screen, not only at the twenty ticks a second: the arrow and the view move on with the position guess, so a flight or a ride
    /// runs as smoothly as the monitor shows it and an exact report shows at once. Which pets, the route and the list stay with the tick.
    /// </summary>
    private void OnFrame(object? sender, EventArgs e)
    {
        if (_frame is not { } f || _directory() is not { } directory || directory.BestPosition is not { } position)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        if ((now - _frameAt).TotalMilliseconds < 15)
        {
            // at most about 60 a second (3 November 2026 review: up to 165 on a fast monitor, each frame drawing the whole map while the game wants the
            // same graphics card); the arrow glides just as smoothly
            return;
        }

        _frameAt = now;
        (float x, float y) = Follow(directory, position);
        FollowHeading(directory, x, y);
        TurnArrow();
        StepZoom();
        // nothing moved since the last frame drawn (standing, the map at rest): drawn again only by the next tick
        bool same = ReferenceEquals(f, _frameDrawn) && !_map.InUse && _mapViews.All(v => !v.InUse) && Math.Abs(x - _x) < 2 && Math.Abs(y - _y) < 2 && Math.Abs(_shownHeading - _headingDrawn) < 0.05
            && _zoomShown == _zoomDrawn;
        _x = x;
        _y = y;
        if (same)
        {
            return;
        }

        _frameDrawn = f;
        _headingDrawn = _shownHeading;
        _zoomDrawn = _zoomShown;
        if (f.Overlay && _map.IsVisible)
        {
            _map.MeHeading = _shownHeading;
            _map.Render(f.Map, _x, _y, f.Radius, f.Tiles, f.Points, f.Live, f.Colors, _gather, true, f.PetArrows, f.GatherArrows, f.Guards, _zoomShown);
        }

        foreach (var view in _mapViews)
        {
            view.MeHeading = _shownHeading;
            view.Render(f.Map, _x, _y, f.Radius, 1.0, f.Points, f.Live, f.Colors, _gather, true, f.PetArrows, f.GatherArrows, f.Guards, _zoomShown);
        }
    }

    private double _heading;
    private double _shownHeading;
    private DateTime _facingUsed = DateTime.MinValue;
    private DateTime _facingHold = DateTime.MinValue;
    private readonly Queue<(DateTime At, float X, float Y)> _trail = new();

    /// <summary>The monster or spawn point the line at the stop leads to (it keeps to it, see the route's next pet).</summary>
    private (double X, double Y)? _nextHeld;

    /// <summary>
    /// Where the arrow points. A dash or movement skill names its direction (the position frame carries it, exactly the way the player moved): taken at
    /// once and kept for a second and a half. Otherwise the arrow points the way the player went over the last three seconds, once that is 8 m or more:
    /// the guess between the game's reports moves in steps towards each monster that appears, so the last few metres of it point every which way (on the
    /// recordings of 31 October the last 6 m were 29 degrees off the real way in the median, the last three seconds 15). Standing still keeps the last
    /// direction. The arrow turns to it smoothly (<see cref="TurnArrow"/>).
    /// </summary>
    private void FollowHeading(Aion2EntityDirectory directory, float x, float y)
    {
        DateTime now = DateTime.UtcNow;
        _trail.Enqueue((now, x, y));
        while (_trail.Count > 0 && now - _trail.Peek().At > TimeSpan.FromSeconds(3))
        {
            _trail.Dequeue();
        }

        if (directory.LocalFacing is { } facing && facing.At > _facingUsed && now - facing.At < TimeSpan.FromSeconds(2))
        {
            _facingUsed = facing.At;
            _facingHold = now.AddSeconds(1.5);
            _heading = facing.Degrees + 90; // the arrow points up at 0 degrees; the map's y runs down the screen
            return;
        }

        if (now < _facingHold || _trail.Count == 0)
        {
            return;
        }

        // in flight: the way the flight goes (learnt from the corrections of the last seconds), steadier than the last few metres of the guess
        if (directory.FlightDirection is { } way)
        {
            _heading = Math.Atan2(way.Y, way.X) * 180 / Math.PI + 90;
            return;
        }

        var (since, fx, fy) = _trail.Peek();
        double dx = x - fx, dy = y - fy;
        if ((now - since).TotalSeconds >= 1.5 && dx * dx + dy * dy >= 800.0 * 800.0)
        {
            _heading = Math.Atan2(dy, dx) * 180 / Math.PI + 90;
        }
    }

    private DateTime _turnedAt = DateTime.UtcNow;

    /// <summary>The arrow on the map turns towards <see cref="_heading"/> the short way round, most of the turn within a quarter of a second (40 % of
    /// what is left per 50 ms, whatever the frame rate).</summary>
    private void TurnArrow()
    {
        DateTime now = DateTime.UtcNow;
        double dt = Math.Clamp((now - _turnedAt).TotalSeconds, 0, 0.5);
        _turnedAt = now;
        double diff = ((_heading - _shownHeading) % 360 + 540) % 360 - 180;
        _shownHeading = Math.Abs(diff) < 0.5 ? _heading : _shownHeading + diff * (1 - Math.Pow(0.6, dt * 20));
    }

    private DateTime _lastCubeBeep = DateTime.MinValue;
    private int _cubeArrivals = -1;
    private int _cubeDepartures = -1;
    private double _lastCubeInterval = 2.0;
    private DateTime _lastCubeFixAt = DateTime.MinValue;
    private bool _cubeMapOverride;
    private DateTime? _cubesGoneAt;

    /// <summary>How long the map stays after the last cube is gone (gathered, or out of range).</summary>
    private static readonly TimeSpan CubeMapStays = TimeSpan.FromSeconds(10);

    /// <summary>A Hidden Cube came into range and the map was shown for it although it was hidden (<see cref="MeterSettings.ShowMapOnCube"/>): until the switches are applied again.</summary>
    internal bool CubeMapOverride => _cubeMapOverride;

    /// <summary>The settings were applied (a hotkey, a switch, Save): a map shown for a cube or a pet near goes back to what the settings say; the next cube,
    /// or the next pet that comes that near, brings it again.</summary>
    internal void ClearCubeOverride()
    {
        _cubeMapOverride = false;
        _cubesGoneAt = null;
        _petMapOverride = false;
        _petsGoneAt = null;
    }

    // ---- the map that comes up near a pet the player still needs (MeterSettings.ShowMapOnPet) ----

    private bool _petMapOverride;
    private DateTime? _petsGoneAt;
    private DateTime _petsLookedAt = DateTime.MinValue;
    private HashSet<int> _petsNear = new();

    /// <summary>The pets whose monsters are that near now (the map shows them while it is up for them, whatever its own genus choice).</summary>
    private HashSet<int> _petsNearShown = new();

    /// <summary>The map was brought up for a pet near (<see cref="MeterSettings.ShowMapOnPet"/>): until the switches are applied again or none is near.</summary>
    internal bool PetMapOverride => _petMapOverride;

    /// <summary>
    /// Once a second: which pets the player still needs (not at the top level; the ones the map tracks - all, or those picked -; of the genera picked for
    /// alerts) have a monster within <see cref="MeterSettings.PetNearMetres"/>: a spawn point of the map, or a monster the game shows (close by, where only
    /// those count: a spawn point without one is empty). A pet that was not near before brings the map up, as a Hidden Cube does (7 November 2026, the owner:
    /// "when my character is 300 m close to an animal I don't have maxed and selected, it opens the interactive map"), with a short note of which and how far;
    /// the map goes again 10 seconds after none is that near.
    /// </summary>
    private void LookForPetsNear(MeterSettings settings, Aion2EntityDirectory? directory)
    {
        if (!settings.ShowMapOnPet || directory is null)
        {
            if (_petMapOverride || _petsNear.Count > 0)
            {
                _petMapOverride = false;
                _petsGoneAt = null;
                _petsNear = new();
                _petsNearShown = new();
            }

            return;
        }

        if (DateTime.UtcNow - _petsLookedAt < TimeSpan.FromSeconds(1))
        {
            return;
        }

        _petsLookedAt = DateTime.UtcNow;
        bool inInstance = Aion2Zones.IsInstance(directory.ZoneId);
        Aion2MapInfo? map = Aion2Maps.Choose(directory.ZoneId, inInstance, directory.RecentMobPositions(), out bool keep) ?? (keep ? _current : null);
        var position = directory.BestPosition;
        var near = new Dictionary<int, double>();
        if (map is not null && position is { } me && !PositionIsStale(directory, me))
        {
            var states = directory.LocalPetStates;
            var maxPets = states.Count > 0 ? states.Where(st => st.Level >= Aion2Pets.TopLevel).Select(st => st.PetId).ToHashSet() : _savedMax;
            var wanted = (settings.PetMapAuto ? Aion2Pets.AllPetIds : settings.PetMapPets).Where(id => !maxPets.Contains(id) && settings.PetAlertFor(id)).ToHashSet();
            double reach = Math.Clamp(settings.PetNearMetres, 50, 2000) * 100.0; // 100 world units to the metre
            void Note(int pet, float x, float y)
            {
                double d = Math.Sqrt((x - me.X) * (double)(x - me.X) + (y - me.Y) * (double)(y - me.Y));
                if (d <= reach && (!near.TryGetValue(pet, out double had) || d < had))
                {
                    near[pet] = d;
                }
            }

            foreach (var mob in directory.LiveMobs())
            {
                foreach (int pet in Aion2Pets.PetsOfNpc(mob.NpcId).Where(wanted.Contains))
                {
                    Note(pet, mob.X, mob.Y);
                }
            }

            foreach (var (pet, x, y) in Aion2Maps.PointsOf(map, wanted))
            {
                if ((x - me.X) * (double)(x - me.X) + (y - me.Y) * (double)(y - me.Y) >= MobRange * MobRange)
                {
                    Note(pet, x, y);
                }
            }
        }

        bool fresh = near.Keys.Any(pet => !_petsNear.Contains(pet));
        _petsNear = near.Keys.ToHashSet();
        if (near.Count > 0)
        {
            _petsNearShown = _petsNear;
            _petsGoneAt = null;
            if (fresh && !_petMapOverride)
            {
                _petMapOverride = true;
                var (pet, distance) = near.MinBy(kv => kv.Value);
                if (!settings.ShowPetMap && !CubeToast.Active)
                {
                    LocalizationManager loc = LocalizationManager.Instance;
                    string genus = Aion2Pets.SpeciesOf(pet) is { } sp ? Aion2Pets.SpeciesShown(sp, loc.Language) + " · " : "";
                    CubeToast.Note(string.Format(System.Globalization.CultureInfo.CurrentCulture, loc["PetNear.Note"], genus + (Aion2Pets.PetName(pet, loc.Language) ?? "#" + pet),
                        Math.Round(distance / 100)), 4);
                }
            }
        }
        else if (_petMapOverride)
        {
            _petsGoneAt ??= DateTime.UtcNow;
            if (DateTime.UtcNow - _petsGoneAt >= CubeMapStays)
            {
                _petMapOverride = false;
                _petsGoneAt = null;
                _petsNearShown = new();
            }
        }
    }

    /// <summary>The map shown for a cube goes away again 10 seconds after the last cube is gone (picked up, or left behind).</summary>
    private void ExpireCubeOverride()
    {
        if (!_cubeMapOverride)
        {
            return;
        }

        if (_directory()?.LiveCubes().Count > 0)
        {
            _cubesGoneAt = null;
        }
        else if (_cubesGoneAt is null)
        {
            _cubesGoneAt = DateTime.UtcNow;
        }
        else if (DateTime.UtcNow - _cubesGoneAt >= CubeMapStays)
        {
            ClearCubeOverride();
        }
    }

    /// <summary>
    /// A new Hidden Cube in range (the game announces one within some 100 m, measured on our recordings of 5 to 29 October 2026 at 94 and 103 m and by dr89mh/aion2-cube-radar at 97 to 105 m, so its coming alone means "a cube nearby"): the arrival chime at once, before the radar beeps
    /// that wait for the player's own place, and the map comes back when it is hidden. Both are Stacy's ideas (her fork, 17 October 2026). The first reading only takes the count
    /// (no chime for cubes seen at the start).
    /// </summary>
    private void NoteCubeArrivals(MeterSettings settings)
    {
        int arrivals = _directory()?.CubeArrivals ?? -1;
        if (arrivals < 0)
        {
            return;
        }

        if (_cubeArrivals >= 0 && arrivals > _cubeArrivals)
        {
            OnCubeArrived(settings);
        }

        _cubeArrivals = arrivals;

        // a cube that leaves the range while the player walks away from it: the falling "out of sight" sound (the arrival chime rises), so he hears at once that he goes the wrong way
        int departures = _directory()?.CubeDepartures ?? -1;
        if (_cubeDepartures >= 0 && departures > _cubeDepartures && settings.CubeArrivalSound && settings.CubeAlertsNow())
        {
            NotifySounds.PlayCubeLeft(settings.CubeSoundVolume);
            _lastCubeBeep = DateTime.UtcNow;
        }

        _cubeDepartures = departures;
    }

    /// <summary>A cube has just come into range: the chime (with the cube sound's switch and volume), and the map where the switches say so and the map shows cubes.</summary>
    internal void OnCubeArrived(MeterSettings settings)
    {
        if (!settings.CubeAlertsNow())
        {
            return; // no key on the character: nothing to open the cube with
        }

        if (settings.CubeArrivalSound)
        {
            NotifySounds.PlayCubeArrival(settings.CubeSoundVolume);
            _lastCubeBeep = DateTime.UtcNow; // the next radar beep waits its turn after the chime
        }

        if (settings.CubePopup)
        {
            // the note at the top of the screen: it stays while the cube is in range, with how far it is and which way (kept up to date every tick)
            CubeToast.Show(CubeNote(_directory()));
        }

        if (settings.ShowMapOnCube)
        {
            _cubeMapOverride = true;
            _cubesGoneAt = null;
        }
    }

    /// <summary>"Hidden Cube nearby · 42 m · NE": the nearest cube in range, how far and which way from where the game last reported the player (with the
    /// report's age once it is a few seconds old; without the distance before any report).</summary>
    private static string CubeNote(Aion2EntityDirectory? directory)
    {
        string text = LocalizationManager.Instance["Cubes.Toast"];
        var cubes = directory?.LiveCubes() ?? Array.Empty<(float X, float Y)>();
        if (cubes.Count > 0 && CubeRange.Nearest(directory!, cubes) is { } near)
        {
            text += " · " + CubeRange.Text(near);
            if (near.Metres >= 3)
            {
                text += " · " + Compass.Towards(near.Dx, near.Dy);
            }
        }

        return text;
    }

    /// <summary>The cube note follows the player every tick while a cube is in range, and goes once none is (opened, or left behind).</summary>
    private void FollowCubeNote(MeterSettings settings)
    {
        if (!CubeToast.Active)
        {
            return;
        }

        var directory = _directory();
        if (!settings.CubePopup || !settings.CubeAlertsNow() || directory is null || directory.LiveCubes().Count == 0)
        {
            CubeToast.Release();
            return;
        }

        CubeToast.Update(CubeNote(directory));
    }

    /// <summary>
    /// The Hidden Cube sound works like a parking sensor: while a cube is in range the sound repeats, the faster the closer the player stands to the nearest one
    /// (about every 2.5 s from 100 m away, where the game announces a cube, down to 0.15 s within 3 m; 100 world units are a metre). The scale used to end at 60 m,
    /// so from the announcement at 100 m to 60 m the rhythm did not change at all and the rise only began near the cube. The game reports the own position only now and
    /// then (skills, hits, dashes): the rhythm follows each report and goes on for up to 8 s after it, and without a report for longer it stays silent.
    /// </summary>
    private void AnnounceCubes(MeterSettings settings)
    {
        if (!settings.CubeBeeps || !settings.CubeAlertsNow())
        {
            return;
        }

        var directory = _directory();
        var cubes = directory?.LiveCubes() ?? Array.Empty<(float X, float Y)>();
        if (cubes.Count == 0)
        {
            return;
        }

        if (CubeRange.Metres(directory!, cubes) is double meters)
        {
            _lastCubeInterval = Math.Clamp(0.15 + (meters - 3) / 97.0 * 2.35, 0.15, 2.5);
            _lastCubeFixAt = DateTime.UtcNow;
        }
        else if (DateTime.UtcNow - _lastCubeFixAt > TimeSpan.FromSeconds(8))
        {
            return; // no position for too long: the distance is unknown, silence
        }

        if ((DateTime.UtcNow - _lastCubeBeep).TotalSeconds >= _lastCubeInterval)
        {
            _lastCubeBeep = DateTime.UtcNow;
            NotifySounds.Play(settings.CubeSound, settings.CubeSoundVolume);
        }
    }

    private void Tick()
    {
        if ((DateTime.UtcNow - _settingsAt).TotalSeconds >= 1)
        {
            _settings = MeterSettings.Load(); // the settings file is read once a second, not every tick
            _faction = Aion2Gather.OwnFaction();
            _savedMax = SavedMaxPets();
            _settingsAt = DateTime.UtcNow;
        }

        var settings = _settings;
        NoteCubeArrivals(settings);
        ExpireCubeOverride();
        AnnounceCubes(settings);
        FollowCubeNote(settings);
        bool mapLocked = settings.PetFarmLocked, listLocked = settings.PetListLocked ?? settings.PetFarmLocked;
        _map.ApplyLockedIfChanged(mapLocked);
        _list.ApplyLockedIfChanged(listLocked);
        // over the game, and not while one of the game's windows is open (enchant, inventory ...) - unless the mouse rests on the map itself
        bool inGame = GameWindow.OverlayMayShow(ignoreMenu: _map.CursorOnMap());
        LookForPetsNear(settings, _directory());
        // only over the game, locked or not (never on the desktop); with the module's switch off only the tool window's views are fed (a cube that came into
        // range, or a pet still needed that came near, shows the map all the same)
        _hideOverlays = !inGame || !(settings.ShowPetMap || _cubeMapOverride || _petMapOverride);
        if (_hideOverlays)
        {
            Hide();
            if (_mapViews.Count == 0)
            {
                return; // nothing to show anywhere; with a tool window open the views go on
            }
        }

        var directory = _directory();
        bool inInstance = directory is not null && Aion2Zones.IsInstance(directory.ZoneId);

        // Which map? (once a second; the previous one stays when no pet monster is in sight)
        if (directory is not null && (DateTime.UtcNow - _lastDetect).TotalSeconds >= 1)
        {
            _lastDetect = DateTime.UtcNow;
            // the zone frame names the map (and the airship above it) outright; the monsters only vote where it does not
            // the zone frame names the map, the monsters around overrule it when they clearly stand on another one (see Aion2Maps.Choose)
            var found = Aion2Maps.Choose(directory.ZoneId, inInstance, directory.RecentMobPositions(), out bool keepShown);
            if (found is null && !keepShown && _current is not null)
            {
                // an instance without a map of its own, or an open map the meter has none of: the map the player came from must not stay behind it
                _current = null;
                PlayerArrow.Reset();
                _pointsKey = "";
            }
            else if (found is not null && found != _current)
            {
                _current = found;
                PlayerArrow.Reset();
                _pointsKey = "";
            }
        }

        // The chosen pets (those at the top level need no farming)
        var states = directory?.LocalPetStates ?? Array.Empty<Aion2PetState>();
        _states = states;
        // before the game's pet list has arrived (the meter started after the login) the levels saved at the last login stand in
        var maxPets = states.Count > 0 ? states.Where(s => s.Level >= Aion2Pets.TopLevel).Select(s => s.PetId).ToHashSet() : _savedMax;
        // every pet still needed (the collection decides), or the pets picked by hand
        bool auto = settings.PetMapAuto;
        // some genera picked (not all four): the map and the list show the pets of those only, as the route does (Cogni and Natura picked: no Varian gnat)
        List<string>? genusList = settings.PetRouteGenera;
        HashSet<string>? genusPick = genusList is { Count: > 0 } && PetRoute.Genera.Any(genusName => !genusList.Contains(genusName)) ? genusList.ToHashSet() : null;
        var chosen = (auto ? Aion2Pets.AllPetIds : settings.PetMapPets)
            .Where(id => !maxPets.Contains(id) && (genusPick is null || PetRoute.GenusOf(id) is { } petGenus && genusPick.Contains(petGenus))).ToHashSet();
        if (_petMapOverride)
        {
            chosen.UnionWith(_petsNearShown.Where(id => !maxPets.Contains(id))); // the map came up for these: it shows them, whatever genera it is set to
        }
        // only what the player's own faction can use (an earlier pick of the other side's plants no longer counts)
        var gatherKinds = Aion2Gather.Items().Where(i => settings.PetMapGatherItems.Contains(i.Key) && i.IsFor(_faction)).Select(i => i.Key).ToHashSet();
        // the places cubes can appear at only when Hidden Cube is picked among the collectibles; the map that came for a cube shows that cube alone
        var spotKinds = gatherKinds.ToHashSet();
        if (_cubeMapOverride)
        {
            gatherKinds.Add("Hidden Cube");
        }

        bool nothing = chosen.Count == 0 && gatherKinds.Count == 0;
        UpdateShopOpen(directory);
        var offeredRows = ShopRows(directory, states);
        _inShop = _shopOpen && offeredRows.Count > 0;
        // the offers stay in memory for five minutes, but the list shows them only while the shop is open (not after the player left it)
        var shopRows = _inShop ? offeredRows : Array.Empty<(Color, string, string, string, bool)>();
        // over the shop the list sits on the game's own window text: it gets a nearly opaque background for as long as the shop is open
        double? floor = InMenu && shopRows.Count > 0 ? ShopListOpacity : null;
        if (_list.OpacityFloor != floor)
        {
            _list.OpacityFloor = floor;
            _list.ApplyStoredOpacity();
        }

        if (nothing && _mapViews.Count == 0)
        {
            // nothing picked and no tool page open: the overlays have nothing to show (the tool page's map still shows the land and the player)
            if (shopRows.Count == 0)
            {
                ShowEmpty(mapLocked, listLocked);
                Status = StatusOf(directory, inInstance, known: false, nothing: true, auto, 0);
                return;
            }

            // only the pets of the open shop to show: the list without a map
            RenderList(shopRows.Take(_list.RowCapacity).ToList());
            ShowList(true);
            ShowMap(!mapLocked);
            return;
        }

        // Where is the player? Unknown (game closed, nothing received yet, teleported): the last known place stays on the map, marked with a "?"
        var position = directory?.BestPosition;
        bool known = directory is not null && position is not null && _current is not null && !PositionIsStale(directory, position.Value);
        float tx, ty;
        if (known)
        {
            (tx, ty) = Follow(directory!, position!.Value);
            RememberPosition(tx, ty);
            FollowHeading(directory!, tx, ty);
            TurnArrow();
        }
        else
        {
            PlayerArrow.Reset(); // when the position is known again the arrow starts from it, not from where it was left
            if (_current is null && !inInstance && settings.PetMapLastMap is { } lastMap)
            {
                _current = Aion2Maps.All.FirstOrDefault(m => m.Key == lastMap);
                _pointsKey = "";
            }

            if (_lastKnown is null && settings.PetMapLastX is { } lx && settings.PetMapLastY is { } ly)
            {
                _lastKnown = ((float)lx, (float)ly);
            }

            (tx, ty) = _lastKnown ?? (0f, 0f);
            if (_lastKnown is null)
            {
                _current = null;
            }
        }

        string key = (_current?.Key ?? "-") + "|" + string.Join(",", chosen.OrderBy(i => i)) + "|" + string.Join(",", spotKinds.OrderBy(k => k));
        if (key != _pointsKey)
        {
            _pointsKey = key;
            _points = _current is null ? new() : Aion2Maps.PointsOf(_current, chosen).ToList();
            _gatherStatic = _current is null ? new() : Aion2Gather.NamedPointsOf(_current, spotKinds).ToList();
        }

        ShowLiveGather(gatherKinds, known, tx, ty);
        UpdateRoute(settings, maxPets, known, tx, ty);
        StepZoom();
        bool zoomedOut = _zoomShown >= RouteAllZoom;
        // close up the next six stops of the route, zoomed out further on (up to twelve)
        var routeStops = settings.PetRouteOn && _plan is { } routePlan
            ? routePlan.Stops.Take(zoomedOut ? PetMapWindow.RouteBadges : 6).Select(st => (st.X * 100, st.Z * 100, st.Radius, st.Step)).ToList()
            : new List<(double, double, double, int)>();
        string? firstName = settings.PetRouteOn ? _plan?.Stops.FirstOrDefault()?.Zone : null;
        var stopTexts = settings.PetRouteOn ? StopTexts(_plan) : Array.Empty<string>();
        // the Kibelisk to reach the stop by: its label reads "[13] <Kibelisk>" (teleport there, then walk to the monsters)
        string? firstKibelisk = settings.PetRouteOn && _plan is { Stops.Count: > 0 } kibPlan && PetRoute.KibeliskOf(kibPlan.MapKey, kibPlan.Stops[0]) is { } kib
            ? kib.Name
            : null;
        IReadOnlySet<int>? arrowPets = settings.PetRouteOn && _plan?.Stops.FirstOrDefault() is { } arrowStop ? arrowStop.Pets.ToHashSet() : null;
        // at the stop (within its reach): the line leads on to the nearest of its pets, a monster that is there first, else its nearest spawn point
        (double X, double Y)? nextPet = null;
        if (known && arrowPets is not null && _plan!.Stops[0] is { } atStop
            && Math.Sqrt((atStop.X * 100 - tx) * (atStop.X * 100 - tx) + (atStop.Z * 100 - ty) * (atStop.Z * 100 - ty)) <= atStop.Radius * 100)
        {
            double Sq(float x, float y) => (x - tx) * (double)(x - tx) + (y - ty) * (double)(y - ty);
            var liveHere = directory!.LiveMobs().SelectMany(m => Aion2Pets.PetsOfNpc(m.NpcId).Where(arrowPets.Contains).Select(_ => (m.X, m.Y))).ToList();
            var spots = _pointsShown.Where(pt => arrowPets.Contains(pt.PetId)).Select(pt => (pt.X, pt.Y)).ToList();
            var pool = liveHere.Count > 0 ? liveHere : spots;
            if (pool.Count > 0)
            {
                var best = pool.MinBy(pt => Sq(pt.Item1, pt.Item2));
                // The line keeps to the monster (or spot) it leads to while that is still there - a monster that walks is followed - and changes only
                // for one clearly nearer (by a third and 10 m): it flipped between monsters about as near as each other with every step.
                if (_nextHeld is { } held && pool.MinBy(pt => (pt.Item1 - held.X) * (pt.Item1 - held.X) + (pt.Item2 - held.Y) * (pt.Item2 - held.Y)) is var same
                    && Math.Abs(same.Item1 - held.X) < 800 && Math.Abs(same.Item2 - held.Y) < 800
                    && Math.Sqrt(Sq(same.Item1, same.Item2)) <= Math.Sqrt(Sq(best.Item1, best.Item2)) * 1.33 + 1000)
                {
                    best = same;
                }

                nextPet = (best.Item1, best.Item2);
            }
        }

        _nextHeld = nextPet;

        _map.Round = settings.PetMapRound;
        _map.ShowCompass = settings.PetMapCompass;
        _map.CompassFades = settings.PetMapCompassFade;
        _map.ArrowPets = arrowPets;
        _map.SetRoute(routeStops, nextPet, firstName, stopTexts, firstKibelisk);
        foreach (var view in _mapViews)
        {
            view.Round = settings.PetMapRound;
            view.ShowCompass = settings.PetMapCompass;
            view.ArrowPets = arrowPets;
            view.SetRoute(routeStops, nextPet, firstName, stopTexts, firstKibelisk);
        }

        // the view's middle is the arrow (MarkerMotion glides over the steps of the guess, so the view needs no glide of its own)
        _x = tx;
        _y = ty;

        // without a known map the list names everything chosen (no distances; not the whole collection); with one, only what stands on it
        var petsHere = _current is null ? (auto ? new List<int>() : chosen.OrderBy(i => i).ToList()) : _points.Select(p => p.PetId).Distinct().OrderBy(i => i).ToList();
        // on the route: the pets of the stop being farmed lead the list
        var stopPets = settings.PetRouteOn && _plan?.Stops.FirstOrDefault() is { } farming ? farming.Pets.Where(chosen.Contains).ToList() : new List<int>();
        var colors = petsHere.Select((id, i) => (id, i)).ToDictionary(t => t.id, t => PetMapPalette.Of(t.i));
        var live = known
            ? directory!.LiveMobs().SelectMany(m => Aion2Pets.PetsOfNpc(m.NpcId).Where(chosen.Contains).Select(pet => (pet, m.X, m.Y))).ToList()
            : new List<(int PetId, float X, float Y)>();
        // within the range the game announces monsters in, a spawn point without a monster is empty (killed, not respawned, or never there): only the announced monsters count there
        _pointsShown = known ? _points.Where(p => (p.X - tx) * (double)(p.X - tx) + (p.Y - ty) * (double)(p.Y - ty) >= MobRange * MobRange).ToList() : _points;
        // the Hidden Cubes in range (the game announces them only when the player is near): glowing gold cubes among the live monsters, when the map shows cubes
        if (gatherKinds.Contains("Hidden Cube") && known)
        {
            live.AddRange(directory!.LiveCubes().Select(c => (CubePet, c.X, c.Y)));
            colors[CubePet] = PetMapPalette.OfGather("Cube");
        }

        _map.MeHeading = _shownHeading;
        if (_current is not null)
        {
            // the militia guards, on the open maps of the other side only (they are friendly at home)
            var guards = settings.ShowGuards && known && Aion2Guards.IsHostileMap(_current.Table, _faction) ? directory!.LiveGuards() : null;
            // zoomed out with the route on: only the spawn points of the pets the route farms (every pet still needed on the whole map would be thousands)
            var mapPoints = _pointsShown;
            if (zoomedOut && settings.PetRouteOn && _plan is { Stops.Count: > 0 } farmPlan)
            {
                var routePets = farmPlan.Stops.Take(PetMapWindow.RouteBadges).SelectMany(st => st.Pets).ToHashSet();
                mapPoints = _pointsShown.Where(pt => routePets.Contains(pt.PetId)).ToList();
            }

            double radius = Math.Clamp(settings.PetMapRadius, 50, 500);
            // only the marks (the map laid over the game's minimap): no picture
            double tiles = settings.PetMapMarkersOnly ? 0.0 : Math.Clamp(settings.PetMapOpacity, 0.0, 1.0);
            if (!nothing)
            {
                _map.Render(_current, _x, _y, radius, tiles, mapPoints, live, colors, _gather, known, settings.ShowPetArrows, settings.ShowGatherArrows, guards, _zoomShown);
            }

            foreach (var view in _mapViews)
            {
                view.MeHeading = _shownHeading;
                // the page's map is a card on the app's page: the land in full, whatever the overlay's see-through setting
                view.Render(_current, _x, _y, radius, 1.0, mapPoints, live, colors, _gather, known, settings.ShowPetArrows, settings.ShowGatherArrows, guards, _zoomShown);
            }

            ShowMap(!nothing || !mapLocked);
            // the frames until the next tick draw the same with the arrow moved on (only with a known position: an unknown one does not move)
            _frame = known ? new FrameRender(_current, radius, tiles, mapPoints, live, colors, guards, settings.ShowPetArrows, settings.ShowGatherArrows, !nothing) : null;
        }
        else
        {
            ShowMap(!mapLocked);
        }

        Status = StatusOf(directory, inInstance, known, nothing, auto, petsHere.Count);

        bool menuNow = InMenu;
        if (++_ticks % 10 == 0 || _lastTarget != (tx, ty) || _lastKnownFlag != known || _lastMenu != menuNow)
        {
            _lastMenu = menuNow;
            _lastTarget = (tx, ty);
            _lastKnownFlag = known;
            // a game menu is open: the list stays only for the shop's pets (no chosen pets, no collectibles), the map steps aside
            var ordered = stopPets.Concat(petsHere.Where(id => !stopPets.Contains(id))).ToList();
            var rows = InMenu ? shopRows : shopRows.Concat(Rows(ordered, colors, tx, ty, states, live, known, gatherKinds)).ToList();
            // the route's next stop above the list, unless it is switched off (the map still shows the route)
            var (headTitle, headDetail) = InMenu || !settings.PetRouteInList ? (null, null) : RouteHead(settings, known, tx, ty);
            _list.RenderHead(headTitle, headDetail);
            foreach (var view in _listViews)
            {
                view.RenderHead(headTitle, headDetail);
            }

            _list.RenderShopClock(settings.BlackCloudClock ? ShopClock(DateTime.UtcNow) : null);
            if (nothing && rows.Count == 0)
            {
                // nothing picked: samples on the overlay while it is placed, a word on the tool page why the list is empty
                _list.Render(listLocked ? rows : SampleRows());
                var hint = new List<(Color, string, string, string, bool)> { (Color.FromRgb(0x9D, 0xB3, 0xC2), LocalizationManager.Instance["PetMap.ListNothing"], LocalizationManager.Instance["PetMap.ListNothingHow"], "", true) };
                foreach (var view in _listViews)
                {
                    view.Render(hint);
                }
            }
            else
            {
                RenderList(rows.Take(_list.RowCapacity).ToList()); // only what fits in the frame (plus a reserve)
            }

            // in an instance with no pet or collectible to place (no distance in any row) and no shop prices the list has nothing to say: it stays hidden
            _listUseless = inInstance && !InMenu && rows.All(r => string.IsNullOrEmpty(r.Item4));
        }

        ShowList(nothing && shopRows.Count == 0 ? !listLocked && !InMenu : (!InMenu || shopRows.Count > 0) && !_listUseless);
    }

    // ---- the pet route (Aion2/PetRoute.cs: the website's route) ----

    private PetRoutePlan? _plan;
    private string _planKey = "";
    private PetRouteStop? _pin;
    private readonly HashSet<int> _skipped = new();
    private bool _replanHere;
    private DateTime _routeAt = DateTime.MinValue;

    /// <summary>The route now: the stop being farmed first. Null without a route (switched off, or a map without one).</summary>
    public PetRoutePlan? Route => _plan;

    /// <summary>The stop being farmed is passed over (its pets are left out for this session); the route goes on from where the player is.</summary>
    public void NextStop()
    {
        if (_plan?.Stops.FirstOrDefault() is { } stop)
        {
            _skipped.UnionWith(stop.Pets);
        }

        ReplanHere();
    }

    /// <summary>The route is planned again from where the player is (and the passed-over pets come back).</summary>
    public void ReplanHere(bool keepSkipped = true)
    {
        if (!keepSkipped)
        {
            _skipped.Clear();
        }

        _pin = null;
        _planKey = "";
        _replanHere = true;
        _routeAt = DateTime.MinValue;
    }

    /// <summary>The faction whose camp the route starts at: the saved character's, else the map's own side.</summary>
    private string RouteFaction() => _faction ?? (_current?.Key is "verteron" or "eltnen" ? "Elyos" : "Asmodian");

    /// <summary>
    /// Once a second: the route of the map the player is on, for the pets still needed (not at the top level, of the genera picked), planned as the
    /// website plans it. The first stop stays the stop being farmed while it has pets left (as on the site, a stop being farmed stays step 1); when its
    /// pets are done (the game's collection says so, by itself) the route is planned again from where the player is, or from that stop.
    /// </summary>
    private void UpdateRoute(MeterSettings settings, HashSet<int> maxPets, bool known, float tx, float ty)
    {
        if (!settings.PetRouteOn || _current is null || !PetRoute.Covers(_current.Key))
        {
            _plan = null;
            _pin = null;
            _planKey = "";
            return;
        }

        if ((DateTime.UtcNow - _routeAt).TotalSeconds < 1)
        {
            return;
        }

        _routeAt = DateTime.UtcNow;
        var genera = (settings.PetRouteGenera is { Count: > 0 } picked ? picked : PetRoute.Genera.ToList()).ToHashSet();
        bool Needed(int id) => !maxPets.Contains(id) && !_skipped.Contains(id);
        var needed = Aion2Pets.AllPetIds.Where(id => PetRoute.GenusOf(id) is { } g && genera.Contains(g) && Needed(id)).OrderBy(id => id);
        string faction = RouteFaction();
        string key = $"{_current.Key}|{faction}|{string.Join(",", genera.OrderBy(g => g))}|{string.Join(",", needed)}|{settings.PetRouteFromHere}";
        if (key == _planKey && !_replanHere)
        {
            return;
        }

        (double X, double Z, string Zone)? here = known ? (tx / 100.0, ty / 100.0, PetRoute.ZoneNear(_current.Key, tx / 100.0, ty / 100.0)) : null;
        PetRouteStop? pin = _pin is { } p && p.Pets.Any(Needed) ? p : null;
        (double X, double Z, string Zone)? from = null;
        if (pin is null && (settings.PetRouteFromHere || _replanHere || _pin is not null))
        {
            // a new route near the player (else at the camp, as on the site), or the stop being farmed is done: on from where the player is now
            if (here is not null)
            {
                from = here;
            }
            else if (_pin is { } done)
            {
                from = (done.X, done.Z, done.Zone);
            }
        }

        _plan = PetRoute.Plan(_current.Key, faction, genera, Needed, from, pin);
        _pin = _plan?.Stops.FirstOrDefault();
        _planKey = key;
        _replanHere = false;
    }

    /// <summary>The list's head on the route: the stop being farmed, its area and how far; its pets, the teleport or windstream to take, and the stop after it.</summary>
    private (string? Title, string? Detail) RouteHead(MeterSettings settings, bool known, float tx, float ty)
    {
        if (!settings.PetRouteOn || _current is null || !PetRoute.Covers(_current.Key))
        {
            return (null, null);
        }

        var loc = LocalizationManager.Instance;
        if (_plan is not { Stops.Count: > 0 } plan)
        {
            return (loc["PetRoute.Done"], loc["PetRoute.DoneHow"]);
        }

        PetRouteStop stop = plan.Stops[0];
        double metres = known ? Math.Sqrt((stop.X * 100 - tx) * (stop.X * 100 - tx) + (stop.Z * 100 - ty) * (stop.Z * 100 - ty)) / 100.0 : double.NaN;
        string title = string.Format(System.Globalization.CultureInfo.CurrentCulture, loc["PetRoute.Stop"], stop.Step, plan.Stops.Count, stop.Zone)
            + (double.IsNaN(metres) ? "" : $" · {metres:0} m");
        var lines = new List<string> { string.Join(", ", stop.Pets.Select(id => Aion2Pets.PetName(id, loc.Language) ?? PetRoute.NameOf(id) ?? $"#{id}")) };
        if (stop.Via is { Kind: "wind" } via)
        {
            lines.Add(string.Format(loc["PetRoute.Wind"], via.Teleport ?? "", via.Island));
        }
        else if (stop.Via is { Kind: "leave" })
        {
            lines.Add(loc["PetRoute.Leave"]);
        }
        else if (stop.Teleport is { } hop && (double.IsNaN(metres) || metres > 200 + hop.Distance))
        {
            lines.Add(string.Format(loc["PetRoute.Teleport"], hop.Name, hop.Distance));
        }

        if (plan.Stops.Count > 1)
        {
            lines.Add(string.Format(loc["PetRoute.Then"], plan.Stops[1].Step, plan.Stops[1].Zone));
        }

        return (title, string.Join("\n", lines));
    }

    /// <summary>What the tool page says above its map: why it is empty, or what it shows (updated with every tick; empty while the map shows what was picked).</summary>
    public string Status { get; private set; } = "";

    private string StatusOf(Aion2EntityDirectory? directory, bool inInstance, bool known, bool nothing, bool auto, int petsHere)
    {
        var loc = LocalizationManager.Instance;
        string where = directory is null || (directory.LocalPlayerId < 0 && directory.BestPosition is null)
            ? loc["PetMap.StatusNoGame"]
            : _current is null
                ? loc[inInstance ? "PetMap.StatusInstance" : "PetMap.StatusNoMap"]
                : !known ? loc["PetMap.StatusLastPlace"] : "";
        string what = nothing ? loc["PetMap.StatusNothing"]
            : auto && _current is not null ? string.Format(loc["PetMap.StatusAuto"], petsHere)
            : "";
        return string.Join("  ", new[] { where, what }.Where(t => t.Length > 0));
    }

    /// <summary>
    /// The pets the Black Cloud shop offers on the tabs the player has opened, with what he has of them: the ones he lacks first, then by level.
    /// The right column names the tab and the price. Without the game's pet list (the meter started after the login) the pets saved at the last login stand in.
    /// </summary>
    private static IReadOnlyList<(Color, string, string, string, bool)> ShopRows(Aion2EntityDirectory? directory, IReadOnlyList<Aion2PetState> states) =>
        directory?.ShopPets is { Count: > 0 } offers ? ShopRowsOf(offers, states) : Array.Empty<(Color, string, string, string, bool)>();

    public static IReadOnlyList<(Color, string, string, string, bool)> ShopRowsOf(IReadOnlyList<Aion2ShopPet> offers, IReadOnlyList<Aion2PetState> states)
    {
        var loc = LocalizationManager.Instance;
        var owned = states.Count > 0
            ? states.ToDictionary(p => p.PetId)
            : (Aion2CharacterStore.Load(Aion2CharacterStore.DefaultPath)?.Pets ?? new()).ToDictionary(p => p.Id, p => new Aion2PetState(p.Id, p.Level, p.Progress));
        var rows = new List<(int Rank, int Level, (Color, string, string, string, bool) Row)>();
        foreach (var offer in offers)
        {
            owned.TryGetValue(offer.PetId, out var state);
            string status;
            Color color;
            if (state is null)
            {
                status = loc["PetShop.Missing"];
                color = Color.FromRgb(0xFF, 0x5A, 0x4F);
            }
            else if (state.Level >= Aion2Pets.TopLevel)
            {
                status = string.Format(loc["PetFarm.Max"], state.Level);
                color = Color.FromRgb(0x4C, 0xC8, 0x78);
            }
            else
            {
                status = string.Format(loc["PetFarm.Level"], state.Level) + $" · {state.Progress}/{Aion2Pets.ProgressNeeded(state.Level)}";
                color = Color.FromRgb(0x9D, 0xB3, 0xC2);
            }

            // the page byte of the shop changed with a patch (3 November 2026), so no page name; the pets of the merchant's list are marked with their group
            string only = Aion2MerchantPets.Of(offer.PetId) is { } merchant ? loc["BlackCloud.KindShort." + merchant.Kind] + " · " : "";
            rows.Add((state is null ? 0 : 1, state?.Level ?? 0, (color, Aion2Pets.PetName(offer.PetId, loc.Language) ?? $"#{offer.PetId}", status, $"{only}{offer.Price:N0}", true)));
        }

        return rows.OrderBy(r => r.Rank).ThenBy(r => r.Level).Select(r => r.Row).ToList();
    }

    private static HashSet<int> SavedMaxPets() =>
        (Aion2CharacterStore.Load(Aion2CharacterStore.DefaultPath)?.Pets ?? new()).Where(p => p.Level >= Aion2Pets.TopLevel).Select(p => p.Id).ToHashSet();

    private (float X, float Y)? _lastKnown;
    private bool _lastKnownFlag;
    private List<(Aion2GatherItem Item, float X, float Y)> _gatherNamed = new();
    private List<(Aion2GatherItem Item, float X, float Y)> _gatherStatic = new();


    /// <summary>How far around the player the game announces every monster (about 65 m, a margin kept for the position guess): a spawn point inside it without an announced monster is empty.</summary>
    private const double MobRange = 5500;

    /// <summary>
    /// What the map shows of the collectibles: the fixed places of the map files, except those seen taken (gathered or opened, by the player or anyone else in
    /// range: a Hidden Cube's place for half an hour, a plant's or an ore's for ten minutes), plus what the game announces - which also covers the places the
    /// map files do not hold (Od, the Hidden Cubes). A place no longer disappears just because the player comes near it and the game has announced nothing yet
    /// (the owner: "Cubes when taken should disappear, instead of disappearing when I go to them").
    /// </summary>
    private void ShowLiveGather(ISet<string> keys, bool known, float px, float py)
    {
        var live = known && _current is not null ? (_directory()?.LiveEnv().Where(e => keys.Contains(e.Key)).ToList()) : null;
        var taken = _directory()?.TakenEnv(TimeSpan.FromMinutes(10)) ?? Array.Empty<(string Key, float X, float Y)>();
        var items = Aion2Gather.Items().Where(i => keys.Contains(i.Key)).ToDictionary(i => i.Key);
        var shown = new List<(Aion2GatherItem Item, float X, float Y)>();
        // Hidden Cubes: only a cube the game announces (the live ones below), never the places a cube can appear at (5 November 2026, the owner: "just never
        // show empty spots, only the actual spot the cube is at that has spawned"; his recordings since 3 November: all 14 cubes the game announced were real,
        // the "random" cubes on the map were those places)
        foreach (var point in _gatherStatic)
        {
            if (point.Item.Kind == "Cube")
            {
                continue;
            }

            if (taken.Any(t => t.Key == point.Item.Key && Math.Abs(t.X - point.X) < 600 && Math.Abs(t.Y - point.Y) < 600))
            {
                continue;
            }

            shown.Add(point);
        }

        foreach (var e in live ?? new())
        {
            if (items.TryGetValue(e.Key, out var item) && !shown.Any(s => s.Item.Key == e.Key && Math.Abs(s.X - e.X) < 300 && Math.Abs(s.Y - e.Y) < 300))
            {
                shown.Add((item, e.X, e.Y));
            }
        }

        _gatherNamed = shown;
        _gather = shown.Select(g => (g.Item.Kind, g.Item.Key, g.X, g.Y)).ToList();
    }
    private DateTime _savedAt = DateTime.MinValue;

    /// <summary>Keeps the last known place (in memory at once, in the settings file every few seconds) for the times the position is not known.</summary>
    private void RememberPosition(float x, float y)
    {
        _lastKnown = (x, y);
        if (_current is null || _current.Instance || (DateTime.UtcNow - _savedAt).TotalSeconds < 10)
        {
            return;
        }

        _savedAt = DateTime.UtcNow;
        var onDisk = MeterSettings.Load();
        if (onDisk.PetMapLastMap == _current.Key && onDisk.PetMapLastX is { } ox && onDisk.PetMapLastY is { } oy && Math.Abs(ox - x) + Math.Abs(oy - y) < 500)
        {
            return;
        }

        onDisk.PetMapLastMap = _current.Key;
        onDisk.PetMapLastX = x;
        onDisk.PetMapLastY = y;
        onDisk.Save();
    }

    /// <summary>The reported position is old when the monsters announced around the player stand far from it (he was teleported, e.g. into a city).</summary>
    private static bool PositionIsStale(Aion2EntityDirectory directory, (float X, float Y, DateTime At) position)
    {
        var mobs = directory.RecentMobPositions();
        if (mobs.Count < 5 || DateTime.UtcNow - position.At < TimeSpan.FromSeconds(30))
        {
            return false;
        }

        var distances = mobs.Select(m => Math.Sqrt((m.X - position.X) * (double)(m.X - position.X) + (m.Y - position.Y) * (double)(m.Y - position.Y))).OrderBy(d => d).ToList();
        return distances[distances.Count / 2] > 60000; // more than 600 m
    }

    private IReadOnlyList<(Color, string, string, string, bool)> Rows(List<int> pets, Dictionary<int, Color> colors, float x, float y, IReadOnlyList<Aion2PetState> states,
        List<(int PetId, float X, float Y)> live, bool known, ISet<string> gatherKeys)
    {
        var loc = LocalizationManager.Instance;
        string language = loc.Language;
        double Dist(float px, float py) => Math.Sqrt((px - x) * (double)(px - x) + (py - y) * (double)(py - y));
        var gatherRows = new List<(double Nearest, (Color, string, string, string, bool) Row)>();
        var petRows = new List<(double Nearest, (Color, string, string, string, bool) Row)>();

        // collectibles: the chosen ones that stand on this map (all of them while the map is unknown)
        var items = Aion2Gather.Items().Where(i => gatherKeys.Contains(i.Key)).ToList();
        foreach (var item in items)
        {
            var mine = _gatherNamed.Where(g => g.Item.Key == item.Key).ToList();
            if (mine.Count == 0 && _current is not null)
            {
                continue;
            }

            double nearest = known && mine.Count > 0 ? mine.Min(g => Dist(g.X, g.Y)) : double.NaN;
            gatherRows.Add((nearest, (PetMapPalette.OfGather(item.Kind), item.NameIn(language), "", known && !double.IsNaN(nearest) ? $"~{nearest / 100:0} m" : "", true)));
        }

        // nearest point per pet in one pass over all points (not one pass per pet: the lists hold thousands of spawn points)
        var nearestPoint = new Dictionary<int, double>();
        var nearestLiveOf = new Dictionary<int, double>();
        if (known)
        {
            foreach (var (id, px, py) in _pointsShown)
            {
                double d = Dist(px, py);
                if (!nearestPoint.TryGetValue(id, out double best) || d < best)
                {
                    nearestPoint[id] = d;
                }
            }
        }

        foreach (var (id, px, py) in live)
        {
            double d = Dist(px, py);
            if (!nearestLiveOf.TryGetValue(id, out double best) || d < best)
            {
                nearestLiveOf[id] = d;
            }
        }

        foreach (int pet in pets)
        {
            double nearest = known ? nearestPoint.GetValueOrDefault(pet, double.NaN) : double.NaN;
            // a monster the game has announced around the player beats the general spawn points: its distance counts, the others show "~"
            double nearestLive = nearestLiveOf.GetValueOrDefault(pet, double.NaN);
            bool isLive = !double.IsNaN(nearestLive);
            if (isLive)
            {
                nearest = nearestLive;
            }
            string level;
            if (states.Count == 0)
            {
                level = "";
            }
            else if (states.FirstOrDefault(s => s.PetId == pet) is { } state)
            {
                level = string.Format(loc["PetFarm.Level"], state.Level) + $" · {state.Progress}/{Aion2Pets.ProgressNeeded(state.Level)}";
            }
            else
            {
                level = loc["PetFarm.New"];
            }

            petRows.Add((nearest, (colors.GetValueOrDefault(pet, Colors.White), Aion2Pets.PetName(pet, language) ?? $"#{pet}", level, double.IsNaN(nearest) ? "" : $"{(isLive ? "" : "~")}{nearest / 100:0} m", false)));
        }

        if (!known)
        {
            // position unknown: the collectibles on top, the pets below, no distances
            return gatherRows.Concat(petRows).Select(r => r.Row).ToList();
        }

        // nearest first; entries without a place on this map (unknown distance) last
        return gatherRows.Concat(petRows).OrderBy(r => double.IsNaN(r.Nearest) ? double.MaxValue : r.Nearest).Select(r => r.Row).ToList();
    }

    private void ShowEmpty(bool mapLocked, bool listLocked)
    {
        if (mapLocked && listLocked)
        {
            if (_listViews.Count > 0)
            {
                foreach (var view in _listViews)
                {
                    view.Render(Array.Empty<(Color, string, string, string, bool)>()); // nothing chosen, not positioned: nothing to list
                }
            }

            Hide();
            return;
        }

        // positioned: the frames (the list with sample rows), to grab and place; a locked one stays hidden
        RenderList(SampleRows());
        ShowList(!listLocked && !InMenu);
        ShowMap(!mapLocked);
    }

    private void Hide()
    {
        ShowMap(false);
        ShowList(false);
    }
}
