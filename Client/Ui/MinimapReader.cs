using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>
/// The exact own position from the game's minimap. The server tells the player's own place only at skills, hits and dashes; in between the meter
/// guesses it from the monsters around (3 to 20 m off). The minimap shows the same map art as the world map tiles (assets/aion2/maps), so its
/// picture is matched against the tiles around the guess (<see cref="MinimapMatch"/>, about a metre) and a good match goes in as an exact report:
/// the map's arrow, the cube distance and its sound follow at once.
/// <list type="bullet">
/// <item>The picture is the game window's own (PrintWindow, as the HUD watcher: no BitBlt of the screen, no DirectX, nothing read from the game);
/// only while the game is in front, the reading is switched on (Tools, Cube finder; on by default with a note the first time) and a Hidden Cube is in
/// range or the map overlay is on; a few times a second, off the UI thread.</item>
/// <item>Where the minimap is: first where Aion 2 draws it with its own interface size (the rectangle in the top right, <see cref="GameLayout"/>), its zoom
/// searched there; else the top right corner of the game window is searched for the map around the guess. Kept per window size (MeterSettings.MinimapLayouts),
/// never thrown away when the fixes stop coming (the match is then looked for farther from the guess, and the place and zoom searched again now and then
/// without losing the one kept); the game's own exact reports correct the readings per map (<see cref="MinimapCalibration"/>, <see cref="CheckAgainstGame"/>).</item>
/// <item>Matched by its detail (<see cref="MinimapMatch.Detail"/>), so the light circle around the player and the view cone that turns with the camera do
/// not matter; the minimap keeps north up (checked on the owner's: turned pictures fit far worse).</item>
/// <item>A fix counts once a second one agrees with it (or it follows the last one at a speed a player can move), so a single wrong match never
/// moves the arrow. No minimap (dungeons, a menu over it, a loading screen): no fix, the guess goes on as before.</item>
/// </list>
/// </summary>
internal sealed class MinimapReader : IDisposable
{
    /// <summary>What the reader does now, for the Cube finder page.</summary>
    public enum ReadState { Off, Waiting, Searching, Reading, NotSeen }

    public static ReadState State { get; private set; } = ReadState.Off;

    /// <summary>How long the last read took (picture and match, milliseconds) and the fixes in the last 10 seconds.</summary>
    public static double LastMs { get; private set; }

    public static int RecentFixes => RecentFixTimes.Count(t => DateTime.UtcNow - t < TimeSpan.FromSeconds(10));

    /// <summary>Which way the camera looked at the last read that saw the minimap's view cone (radians: 0 east, counter-clockwise; the cone's opening), and
    /// when; null before any. Read only while a Hidden Cube is in range and the cube beam is on (<see cref="CubeBeam"/>).</summary>
    public static (double Yaw, double Width, DateTime At)? Camera { get; private set; }

    /// <summary>The camera's opening as the cone showed it (the middle of the last readings; radians), null before any.</summary>
    public static double? CameraFov
    {
        get
        {
            lock (RecentWidths)
            {
                return RecentWidths.Count == 0 ? null : Median(RecentWidths);
            }
        }
    }

    private static readonly Queue<double> RecentWidths = new();

    /// <summary>Asks the next read to save the game window's picture and what the reader made of it (Debug folder), for checking.</summary>
    public static bool SaveNext { get; set; }

    /// <summary>Forgets the minimap's place: it is searched again.</summary>
    public static bool ForgetPlace { get; set; }

    /// <summary>Where the last saved picture went.</summary>
    public static string? LastSaved { get; private set; }

    private static readonly Queue<DateTime> RecentFixTimes = new();

    private readonly Func<Aion2EntityDirectory?> _directory;
    private readonly Action<string> _notice;
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Dictionary<string, MapRaster> _rasters = new();
    private volatile bool _busy;
    private MeterSettings _settings = MeterSettings.Load();
    private DateTime _settingsAt = DateTime.UtcNow;
    private Aion2MapInfo? _map;
    private DateTime _mapAt = DateTime.MinValue;
    private DateTime _searchAt = DateTime.MinValue;
    private DateTime _jobAt = DateTime.MinValue;
    private (double X, double Y)? _lastJob;
    private (double X, double Y, DateTime At)? _lastRaw;
    private DateTime _lastFixAt = DateTime.MinValue;
    private int _misses;

    /// <summary>Fixes in a row without the player's arrow in the minimap's middle: 20 of them and the place is searched again (see Search).</summary>
    private int _noArrow;
    private DateTime _recheckAt = DateTime.MinValue;
    private Bitmap? _whole;
    private readonly MinimapCalibration _cal = MinimapCalibration.Shared; // the map draws its picture with it too

    /// <summary>The minimap on screen for one window size: where the player is drawn (its middle), how far the map reaches from there across and down
    /// (client pixels), and how many metres one of its pixels shows.</summary>
    internal sealed record Layout(double Cx, double Cy, double HalfW, double HalfH, double MetresPerPixel);

    public MinimapReader(Func<Aion2EntityDirectory?> directory, Action<string> notice)
    {
        _directory = directory;
        _notice = notice;
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public void Dispose()
    {
        _timer.Stop();
        _cal.Save();
    }

    private void Tick()
    {
        DateTime now = DateTime.UtcNow;
        if ((now - _settingsAt).TotalSeconds >= 1)
        {
            _settings = MeterSettings.Load();
            _settingsAt = now;
        }

        MeterSettings settings = _settings;
        if (!settings.MinimapReading || PrivacyConsent.Restricted)
        {
            State = ReadState.Off;
            return;
        }

        if (_directory() is not { } directory)
        {
            State = ReadState.Waiting;
            return;
        }

        bool wanted = directory.LiveCubes().Count > 0 || settings.ShowPetMap || SaveNext;
        IntPtr game = GameWindow.ForegroundGame();
        if (!wanted || game == IntPtr.Zero || GameWindow.ForegroundClientArea(ignoreMenu: true) is not { } area)
        {
            State = ReadState.Waiting;
            return;
        }

        if ((now - _mapAt).TotalSeconds >= 1)
        {
            _mapAt = now;
            var found = Aion2Maps.Choose(directory.ZoneId, Aion2Zones.IsInstance(directory.ZoneId), directory.RecentMobPositions(), out bool keep);
            _map = found ?? (keep ? _map : null);
        }

        // the open maps only for now (an instance's map is drawn differently on its minimap)
        if (_map is not { Instance: false } map)
        {
            State = ReadState.NotSeen;
            return;
        }

        // Where the match looks: around the guess. A Hidden Cube in range is a better anchor than an old or wrong guess: the game announces a cube only
        // within some 105 m, so the player is within that of it (3 November 2026: after a teleport and no fight, the guess went stale, the reader stopped
        // for want of a fresh one, and the meter's distance to the cube was the guess's until the cube was opened - 60 m shown, 1 m true).
        (float X, float Y, DateTime At)? best = directory.BestPosition;
        bool fresh = best is { } known && now - known.At <= TimeSpan.FromSeconds(10);
        IReadOnlyList<(float X, float Y)> cubes = directory.LiveCubes();
        (float X, float Y)? anchor = null;
        if (cubes.Count > 0 && (!fresh || _misses >= 8))
        {
            (float X, float Y) origin = best is { } b ? (b.X, b.Y) : cubes[0];
            anchor = cubes.MinBy(c => ((c.X - origin.X) * (double)(c.X - origin.X)) + ((c.Y - origin.Y) * (double)(c.Y - origin.Y)));
        }

        if (anchor is null && !fresh)
        {
            State = ReadState.NotSeen;
            return;
        }

        (float X, float Y) guess = anchor ?? (best!.Value.X, best.Value.Y);

        // the window's real pixels (the picture is taken in them too): with Windows' scaling of another monitor the area above is not the game's size
        Size client = GameWindow.ClientSizeInPixels(game) ?? area.Size;
        string key = $"{client.Width}x{client.Height}";
        CheckAgainstGame(directory, map, key, settings);
        if (_busy)
        {
            return;
        }

        if (ForgetPlace)
        {
            ForgetPlace = false;
            Forget(key);
        }

        Layout? layout = settings.MinimapLayouts.TryGetValue(key, out double[]? saved) && saved.Length >= 5 ? new Layout(saved[0], saved[1], saved[2], saved[3], saved[4]) : null;
        if (layout is null && now - _searchAt < TimeSpan.FromSeconds(3) && !SaveNext)
        {
            return; // the search is heavy: once every 3 s until the minimap is found
        }

        List<Layout> hints = new();
        if (layout is null)
        {
            _searchAt = now;
            hints = ScaledFrom(settings.MinimapLayouts, client.Width, client.Height);
        }

        // at most five reads a second, two while the player stands (3 November 2026 review: up to ten, each drawing the whole game window); the
        // search for the minimap's place keeps its own pace above
        bool standing = _lastJob is { } before && now - _lastFixAt < TimeSpan.FromSeconds(2) && Distance(before.X, before.Y, guess.X, guess.Y) < 50;
        // the cube beam follows the camera, which turns while the player stands: five reads a second while it is up (5 November 2026)
        bool cone = settings.CubeBeam && cubes.Count > 0;
        standing &= !cone;
        if (layout is not null && now - _jobAt < TimeSpan.FromMilliseconds(standing ? 500 : 200) && !SaveNext)
        {
            return;
        }

        _jobAt = now;
        _lastJob = (guess.X, guess.Y);
        // The guess is a place of the game (its reports, the readings put right, a cube); the match looks in the map's picture, where the readings are. Where
        // the picture lies off the game the guess has to be taken back there first (5 November 2026, minimap-log: on Altgard's west the picture lies 80 to 90 m
        // off, so the match looked 85 m from the player with a reach of 40 m and found nothing; every fix came only after eight misses had widened the reach -
        // "no fix for 8 reads" every 2 to 3 s, and the arrow moved in steps).
        string stamp = MinimapCalibration.StampOf(map);
        (double readX, double readY) = ToReading(_anchor, map.Key, guess.X, guess.Y, now, (x, y) => _cal.Correct(map.Key, stamp, x, y, now));
        if (!_rasters.TryGetValue(map.Key, out MapRaster? raster))
        {
            // only the map played now is kept: each holds a decoded picture of its whole map (the memory grew with every map visited)
            _rasters.Clear();
            raster = new MapRaster(map);
            _rasters[map.Key] = raster;
        }

        bool save = SaveNext;
        SaveNext = false;
        _busy = true;
        // lost for a while: the match looked for farther from the guess, and now and then the place and zoom searched again (the one kept stays unless
        // the search finds one)
        double reach = anchor is not null ? 110 : _misses < 8 ? 40 : _misses < 25 ? 120 : 250;
        // searched again after many reads without a fix, or after many fixes without the player's arrow in the minimap's middle (the middle is in the wrong place)
        bool recheck = layout is not null && (_misses >= 50 || _noArrow >= 20) && now - _recheckAt > TimeSpan.FromSeconds(15);
        bool arrowCheck = recheck && _misses < 50; // searched for the arrow: a place found counts only with the arrow in its middle
        if (recheck)
        {
            _recheckAt = now;
            _noArrow = 0;
        }

        // lost for a while (25 reads): one picture of the minimap alone into the Debug folder, to see later why it did not match (at most every 10 minutes)
        bool saveLost = _misses == 25 && layout is not null && now - _lostSavedAt > TimeSpan.FromMinutes(10);
        if (saveLost)
        {
            _lostSavedAt = now;
        }

        // a window size where the search keeps failing: one picture of its top right corner into the Debug folder (once a start for each size)
        bool saveCorner = layout is null && _searchFails.GetValueOrDefault(key) == 10;
        var job = new Job(game, client, key, raster, readX, readY, layout, save, _misses, reach, recheck, saveLost, hints, saveCorner, arrowCheck, cone);
        _ = Task.Run(() =>
        {
            var outcome = new Outcome(null, null, false, 0, null);
            try
            {
                outcome = Work(job);
            }
            catch (Exception ex) when (ex is ExternalException or ArgumentException or InvalidOperationException or OutOfMemoryException or IOException
                or IndexOutOfRangeException or NullReferenceException)
            {
                // no read this time (a window that went away, a picture that failed): the next tick tries again
            }
            finally
            {
                _timer.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _busy = false;
                    Apply(job, outcome);
                }));
            }
        });
    }

    internal sealed record Job(IntPtr Game, Size Client, string Key, MapRaster Raster, double GuessX, double GuessY, Layout? Layout, bool Save, int Misses,
        double Reach = 40, bool Recheck = false, bool SaveLost = false, IReadOnlyList<Layout>? Hints = null, bool SaveCorner = false, bool ArrowCheck = false,
        bool Cone = false);

    /// <summary>Searches for the minimap that found nothing, per window size ("4480x2520"): logged now and then, a picture at the tenth.</summary>
    private readonly Dictionary<string, int> _searchFails = new();

    /// <summary>
    /// Where the minimap should be in a window of a size never read before, from the places found in windows of other sizes: the game's interface grows
    /// with the window (2560 x 1440 taken as the base: by the smaller of the width and height ratios, or by the height, or by the width alone - which one
    /// the game uses is not known, so each is tried), and the minimap stays in the top right corner. 3 November 2026: the owner's game ran at 2560 x 1600
    /// and at 4480 x 2520; the search found the minimap in the first and never in the second, so the meter guessed the cube's distance there.
    /// </summary>
    internal static List<Layout> ScaledFrom(IReadOnlyDictionary<string, double[]> known, int width, int height)
    {
        static double ByMin(double w, double h) => Math.Min(w / 2560.0, h / 1440.0);
        static double ByHeight(double w, double h) => h / 1440.0;
        static double ByWidth(double w, double h) => w / 2560.0;
        var hints = new List<Layout>();
        foreach ((string size, double[] v) in known)
        {
            string[] parts = size.Split('x');
            if (v.Length < 5 || parts.Length != 2 || !int.TryParse(parts[0], out int w0) || !int.TryParse(parts[1], out int h0) || w0 <= 0 || h0 <= 0
                || (w0 == width && h0 == height))
            {
                continue;
            }

            foreach (Func<double, double, double> ui in new Func<double, double, double>[] { ByMin, ByHeight, ByWidth })
            {
                double k = ui(width, height) / ui(w0, h0);
                if (k is < 0.3 or > 4 || hints.Any(h => Math.Abs((h.MetresPerPixel * k) - v[4]) < 0.01 * v[4]))
                {
                    continue; // the same as one already in the list
                }

                hints.Add(new Layout(Math.Round(width - ((w0 - v[0]) * k)), Math.Round(v[1] * k), Math.Round(v[2] * k), Math.Round(v[3] * k), v[4] / k));
            }
        }

        return hints;
    }

    /// <summary>What one read gave: a fix (world units), a new place of the minimap, whether it was searched for, how long it took, a saved picture.</summary>
    private sealed record Outcome((double X, double Y)? Fix, Layout? NewLayout, bool Searched, double Ms, string? Saved, DateTime At = default,
        MinimapFix? Match = null, (double X, double Y)? Arrow = null, MinimapCone.Reading? Cone = null);

    // on the UI thread: a fix that a second one confirms goes to the directory as an exact report
    private void Apply(Job job, Outcome outcome)
    {
        LastMs = outcome.Ms;
        if (outcome.Cone is { } seen)
        {
            Camera = (seen.Yaw, seen.Width, outcome.At);
            lock (RecentWidths)
            {
                RecentWidths.Enqueue(seen.Width);
                while (RecentWidths.Count > 15)
                {
                    RecentWidths.Dequeue();
                }
            }
        }

        if (outcome.Saved is not null)
        {
            LastSaved = outcome.Saved;
        }

        if (outcome.NewLayout is { } found)
        {
            MeterSettings s = MeterSettings.Load();
            s.MinimapLayouts[job.Key] = new[] { found.Cx, found.Cy, found.HalfW, found.HalfH, found.MetresPerPixel };
            s.Save();
            _settings = s;
            _misses = 0;
            Log(FormattableString.Invariant($"{DateTime.Now:HH:mm:ss} {job.Raster.Map.Key}: minimap {(job.Recheck ? "searched again" : "found")} at {found.Cx:0} {found.Cy:0}, {found.MetresPerPixel:0.000} m a pixel{(job.Layout is { } was ? FormattableString.Invariant($" (was {was.Cx:0} {was.Cy:0}, {was.MetresPerPixel:0.000})") : "")}"));
        }
        else if (job.Recheck)
        {
            Log(job.ArrowCheck
                ? FormattableString.Invariant($"{DateTime.Now:HH:mm:ss} {job.Raster.Map.Key}: 20 fixes without the player's arrow in the minimap's middle; searched again, no place with it found (the place kept)")
                : FormattableString.Invariant($"{DateTime.Now:HH:mm:ss} {job.Raster.Map.Key}: no fix for {_misses} reads; searched again, nothing better found (the place kept)"));
        }

        if (job.Layout is null && outcome.NewLayout is null && outcome.Searched)
        {
            int fails = _searchFails[job.Key] = _searchFails.GetValueOrDefault(job.Key) + 1;
            if (fails is 1 or 10 or 50 || fails % 200 == 0)
            {
                Log(FormattableString.Invariant($"{DateTime.Now:HH:mm:ss} {job.Raster.Map.Key}: minimap not found in the {job.Key} window ({fails} searches; {job.Hints?.Count ?? 0} places tried from other window sizes)"));
            }
        }
        else if (outcome.NewLayout is not null)
        {
            _searchFails.Remove(job.Key);
        }

        DateTime now = DateTime.UtcNow;
        if (outcome.Fix is not { } fix)
        {
            if (!outcome.Searched)
            {
                // no fix (a menu over it, a loading screen, a place without detail, a guess gone too far): looked for farther next time; the place is kept
                _misses++;
                if (_misses is 8 or 25)
                {
                    Log(FormattableString.Invariant($"{DateTime.Now:HH:mm:ss} {job.Raster.Map.Key}: no fix for {_misses} reads, looking {(_misses == 8 ? 120 : 250)} m around the guess"));
                }
            }

            State = job.Layout is null && outcome.NewLayout is null ? ReadState.Searching : now - _lastFixAt < TimeSpan.FromSeconds(2) ? ReadState.Reading : ReadState.NotSeen;
            return;
        }

        _misses = 0;
        _noArrow = outcome.Arrow is null ? _noArrow + 1 : 0;
        _lastMatch = outcome.Match;
        _lastArrow = outcome.Arrow;
        // a fix counts when it agrees with the one before (both within what a player can move in between: 30 m/s and 3 m)
        bool confirmed = _lastRaw is { } last && Distance(last.X, last.Y, fix.X, fix.Y) <= (3000 * Math.Min(2, (now - last.At).TotalSeconds)) + 300;
        _lastRaw = (fix.X, fix.Y, now);
        if (!confirmed || _directory() is not { } directory)
        {
            State = ReadState.Reading;
            return;
        }

        // smoothed and moved on to now (the picture was taken a moment ago), then carried on from the game's last exact report (Anchored) or, without one
        // near, put right by what the game's reports taught on this map; with the speed for the guess to run on until the next reading
        var (sx, sy, svx, svy) = Smooth(fix.X, fix.Y, outcome.At);
        double late = Math.Clamp((now - outcome.At).TotalSeconds, 0, 0.3);
        Aion2MapInfo map = job.Raster.Map;
        double ux = sx + (svx * late), uy = sy + (svy * late);
        (double cx, double cy) = Anchored(_anchor, map.Key, ux, uy, now) ?? _cal.Correct(map.Key, MinimapCalibration.StampOf(map), ux, uy, now);
        if (Impossible(directory.ReportedPosition, directory.Clock(), cx, cy))
        {
            // the game itself put the player somewhere else a moment ago, farther than anyone moves in the time: the minimap matched the wrong place (the owner,
            // 5 November 2026: "the map teleports to a completely different part"). Not taken; counted as a miss, so the search looks around the guess again.
            _misses++;
            _track = null;
            State = ReadState.Reading;
            return;
        }

        directory.NoteLocalPosition((float)cx, (float)cy, svx, svy);
        if (directory.LocalPosition is { } mine)
        {
            _fed.Enqueue(mine.At);
            while (_fed.Count > 64)
            {
                _fed.Dequeue();
            }
        }

        _lastFixAt = now;
        State = ReadState.Reading;
        RecentFixTimes.Enqueue(now);
        while (RecentFixTimes.Count > 0 && now - RecentFixTimes.Peek() > TimeSpan.FromSeconds(10))
        {
            RecentFixTimes.Dequeue();
        }

        if (!_settings.MinimapNoticeShown)
        {
            MeterSettings s = MeterSettings.Load();
            s.MinimapNoticeShown = true;
            s.Save();
            _settings = s;
            _notice(LocalizationManager.Instance["Minimap.Notice"]);
        }
    }

    // the smoothed track of the readings: place and speed (world units, per second) at the time of the last picture
    private (double X, double Y, double Vx, double Vy, DateTime At)? _track;

    /// <summary>How much of a reading's difference from where the track expected it is taken (the rest is the reading's own wobble), and how much of it
    /// goes into the speed (an alpha-beta filter): readings five times a second, each good to half a metre or so.</summary>
    internal const double TrackAlpha = 0.6, TrackBeta = 0.25;

    /// <summary>
    /// A reading put on the smoothed track: the track moved on to the picture's time at its speed, then pulled towards the reading (<see cref="TrackAlpha"/>),
    /// the speed by the rest of the difference (<see cref="TrackBeta"/>), at most 30 m/s. A reading more than 8 m off the track, or after a second
    /// without one, starts it anew (a teleport, a mount, the start).
    /// </summary>
    internal (double X, double Y, double Vx, double Vy) Smooth(double x, double y, DateTime at) => SmoothStep(ref _track, x, y, at);

    /// <summary>One step of <see cref="Smooth"/> on a given track (the self-test drives it).</summary>
    internal static (double X, double Y, double Vx, double Vy) SmoothStep(ref (double X, double Y, double Vx, double Vy, DateTime At)? track, double x, double y, DateTime at)
    {
        if (track is { } t && (at - t.At).TotalSeconds is > 0.01 and < 1.0)
        {
            double dt = (at - t.At).TotalSeconds;
            double px = t.X + (t.Vx * dt), py = t.Y + (t.Vy * dt), rx = x - px, ry = y - py;
            if ((rx * rx) + (ry * ry) <= 800.0 * 800.0)
            {
                double nx = px + (TrackAlpha * rx), ny = py + (TrackAlpha * ry);
                double vx = t.Vx + (TrackBeta * rx / dt), vy = t.Vy + (TrackBeta * ry / dt);
                double speed = Math.Sqrt((vx * vx) + (vy * vy));
                if (speed > 3000)
                {
                    vx *= 3000 / speed;
                    vy *= 3000 / speed;
                }

                track = (nx, ny, vx, vy, at);
                return (nx, ny, vx, vy);
            }
        }

        track = (x, y, 0, 0, at);
        return (x, y, 0, 0);
    }

    /// <summary>The game's reports more than 25 m from the readings put right, with the player's arrow in the minimap's middle (world units).</summary>
    private readonly List<(double Rx, double Ry, double Gx, double Gy, double Speed, DateTime At)> _steady = new();
    private string? _steadyMap;

    /// <summary>
    /// A report far from the reading while the minimap's middle is right (the arrow is there): the map's tiles are off here. Four reports within five minutes
    /// whose offsets agree within 6 m teach the calibration that offset (up to 100 m); offsets that disagree are a teleport or a wrong match and teach nothing.
    /// Returns the line for the log when they taught it.
    /// </summary>
    private string NoteSteady(string mapKey, string stamp, double rx, double ry, double gx, double gy, double speed, DateTime at)
    {
        if (_steadyMap != mapKey)
        {
            _steady.Clear();
            _steadyMap = mapKey;
        }

        if (speed > MinimapCalibration.MaxSpeed)
        {
            return "";
        }

        _steady.RemoveAll(r => (at - r.At).Duration() > TimeSpan.FromMinutes(5));
        _steady.Add((rx, ry, gx, gy, speed, at));
        if (_steady.Count < 4)
        {
            return "";
        }

        double mx = Median(_steady.Select(r => r.Gx - r.Rx)), my = Median(_steady.Select(r => r.Gy - r.Ry));
        if (_steady.Any(r => Distance(r.Gx - r.Rx, r.Gy - r.Ry, mx, my) > 600))
        {
            _steady.RemoveAt(0); // they disagree: not one steady offset (yet)
            return "";
        }

        int taught = _steady.Count(r => _cal.Add(mapKey, stamp, r.Rx, r.Ry, r.Gx, r.Gy, r.Speed, r.At, steady: true));
        _steady.Clear();
        _track = null;
        _lastRaw = null;
        return FormattableString.Invariant($"{taught} reports agree on {mx / 100:+0.0;-0.0} {my / 100:+0.0;-0.0} m: learnt as the map's offset here");
    }

    /// <summary>The game's last exact report of the player's place and where the readings' track was at that moment (world units), the map, and when.</summary>
    private (string Map, double Gx, double Gy, double Rx, double Ry, DateTime At)? _anchor;

    /// <summary>How long and how far from the reading at the report (world units) the readings are carried on from it.</summary>
    internal static readonly TimeSpan AnchorFor = TimeSpan.FromMinutes(10);

    internal const double AnchorReach = 60000;

    /// <summary>A report this far from the reading (world units) is a teleport or a lost reading: no anchor (the readings stand on their own).</summary>
    internal const double AnchorMostOff = 15000;

    /// <summary>
    /// A reading carried on from the game's last exact report: the report plus how far the readings moved since (5 November 2026, the owner: "near the cube it
    /// says it right, then the map teleports away from me" - in the west of Altgard the map's tiles lie 70 to 90 m off what the game reports, so every reading
    /// after a report put the player back 88 m away). What the minimap is good at is how far the player moved; where on the map comes from the game. Within
    /// <see cref="AnchorReach"/> of the reading at the report and <see cref="AnchorFor"/>; null otherwise (then the calibration's correction stands).
    /// </summary>
    internal static (double X, double Y)? Anchored((string Map, double Gx, double Gy, double Rx, double Ry, DateTime At)? anchor, string map, double x, double y, DateTime now)
    {
        if (anchor is not { } a || a.Map != map || (now - a.At).Duration() > AnchorFor || Distance(x, y, a.Rx, a.Ry) > AnchorReach)
        {
            return null;
        }

        return (a.Gx + (x - a.Rx), a.Gy + (y - a.Ry));
    }

    /// <summary>
    /// Where a place of the game (world units) is in the readings, the map's picture: the other way of <see cref="Anchored"/> near the last report, else of
    /// the calibration's correction (<paramref name="correct"/>, nearly a shift: a few steps undo it). The match looks around this.
    /// </summary>
    internal static (double X, double Y) ToReading((string Map, double Gx, double Gy, double Rx, double Ry, DateTime At)? anchor, string map, double gx, double gy,
        DateTime now, Func<double, double, (double X, double Y)> correct)
    {
        if (anchor is { } a && a.Map == map && (now - a.At).Duration() <= AnchorFor && Distance(gx, gy, a.Gx, a.Gy) <= AnchorReach)
        {
            return (gx - (a.Gx - a.Rx), gy - (a.Gy - a.Ry));
        }

        double rx = gx, ry = gy;
        for (int i = 0; i < 4; i++)
        {
            (double cx, double cy) = correct(rx, ry);
            rx += gx - cx;
            ry += gy - cy;
        }

        return double.IsFinite(rx) && double.IsFinite(ry) && Distance(rx, ry, gx, gy) <= AnchorReach ? (rx, ry) : (gx, gy);
    }

    /// <summary>
    /// A reading the game's own report rules out: within 20 s of the report and farther from it than 50 m plus 40 m for every second since (no mount or glide is
    /// faster). The game's place wins; the reading was a wrong match.
    /// </summary>
    internal static bool Impossible((float X, float Y, DateTime At)? report, DateTime now, double x, double y)
    {
        if (report is not { } r)
        {
            return false;
        }

        double ago = (now - r.At).TotalSeconds;
        return ago is >= 0 and < 20 && Distance(x, y, r.X, r.Y) > 5000 + (4000 * ago);
    }

    // the reports the reader itself gave (they are no check of the minimap's middle)
    private readonly Queue<DateTime> _fed = new();

    private MinimapFix? _lastMatch;
    private (double X, double Y)? _lastArrow;
    private DateTime _lastGameReport = DateTime.MinValue;
    private readonly Dictionary<string, List<double>> _errors = new();

    /// <summary>The game's reports 25 to 150 m from the readings, at a walk (world units, game minus reading), for <see cref="MoveMiddleIfOff"/>.</summary>
    private readonly List<(double Dx, double Dy)> _far = new();
    private string? _farKey;

    /// <summary>
    /// The minimap's middle in the wrong place: its place was found in a window of another size or by the search of the corner, and a piece of the minimap
    /// matches the map wherever it is cut, so the reading is where the piece's middle is and not the player (3 November 2026: 27 m off in a 2560 x 1440 window,
    /// every report then left out as a teleport). Four reports at a walk that agree within 6 m move the middle by their median, in screen pixels at the zoom.
    /// </summary>
    private void MoveMiddleIfOff(string key, double[] kept, double dx, double dy, double metres, double speed)
    {
        if (metres > 150 || speed > 300)
        {
            return; // a teleport, or too fast for the moment of the report to tell
        }

        if (_farKey != key)
        {
            _far.Clear();
            _farKey = key;
        }

        _far.Add((dx, dy));
        if (_far.Count < 4)
        {
            return;
        }

        double mx = Median(_far.Select(f => f.Dx)), my = Median(_far.Select(f => f.Dy));
        if (_far.Any(f => Distance(f.Dx, f.Dy, mx, my) > 600))
        {
            _far.RemoveAt(0); // they disagree: not one steady offset (yet)
            return;
        }

        double perPixel = kept[4] * 100; // world units a screen pixel
        double sx = mx / perPixel, sy = my / perPixel;
        MeterSettings s = MeterSettings.Load();
        if (!s.MinimapLayouts.TryGetValue(key, out double[]? now) || now.Length < 5)
        {
            return;
        }

        now[0] = Math.Round(now[0] + sx, 1);
        now[1] = Math.Round(now[1] + sy, 1);
        s.Save();
        _settings = s;
        _far.Clear();
        _track = null;
        _lastRaw = null;
        _anchor = null; // the readings move with the middle: the last report's anchor no longer fits them
        Log(FormattableString.Invariant($"{DateTime.Now:HH:mm:ss} the minimap's middle in the {key} window moved by {sx:+0.0;-0.0} {sy:+0.0;-0.0} px to {now[0]:0.0} {now[1]:0.0} (four reports {Math.Sqrt((mx * mx) + (my * my)) / 100:0} m off the readings, the same way)"));
    }

    /// <summary>How close the readings are to the game's own exact reports, on the map last checked: the median (metres) of the last ones, how many, which map.</summary>
    public static (double Metres, int Count, string Map)? Accuracy { get; private set; }

    /// <summary>
    /// Every exact report of the game itself (a skill, a hit, a dash: 0x3c37) is a check of the readings: the smoothed track of the readings, moved on to
    /// the report's moment at its speed, against where the game says the player is. What the player sees (the reading put right, <see cref="MinimapCalibration"/>)
    /// goes into the accuracy on the Cube finder page and into minimap-log.txt with the reading's fit, the places and the map's line; the raw reading and
    /// the game's place teach the calibration. Reports more than 0.4 s from a reading, or more than 25 m off (a teleport, a lost reading), are not used.
    /// </summary>
    private void CheckAgainstGame(Aion2EntityDirectory directory, Aion2MapInfo map, string key, MeterSettings settings)
    {
        if (directory.LocalPosition is not { } exact || exact.At <= _lastGameReport || _fed.Contains(exact.At))
        {
            return;
        }

        _lastGameReport = exact.At;
        if (_track is not { } t || !settings.MinimapLayouts.TryGetValue(key, out double[]? kept) || kept.Length < 5)
        {
            return;
        }

        double dt = (exact.At - t.At).TotalSeconds;
        if (dt is < -0.3 or > 0.4)
        {
            return;
        }

        double rx = t.X + (t.Vx * dt), ry = t.Y + (t.Vy * dt), speed = Math.Sqrt((t.Vx * t.Vx) + (t.Vy * t.Vy));
        // from now on the readings go on from this report (a teleport or a lost reading, far off, anchors nothing: the readings stand on their own again)
        _anchor = Distance(exact.X, exact.Y, rx, ry) <= AnchorMostOff ? (map.Key, exact.X, exact.Y, rx, ry, DateTime.UtcNow) : null;
        string stamp = MinimapCalibration.StampOf(map);
        (double cx, double cy) = _cal.Correct(map.Key, stamp, rx, ry, exact.At);
        double ex = exact.X - cx, ey = exact.Y - cy, metres = Math.Sqrt((ex * ex) + (ey * ey)) / 100;
        double raw = Distance(exact.X, exact.Y, rx, ry) / 100;
        if (metres > 25)
        {
            // Far even from the reading put right: a teleport, a lost reading, the minimap's middle in the wrong place - or the map's tiles off here by more
            // than learnt so far. With the player's arrow in the minimap's middle the middle is right: reports that agree on the offset teach it (a steady
            // offset is no teleport); without the arrow there the middle is moved (MoveMiddleIfOff).
            bool arrowInMiddle = _lastArrow is { } arrow && Math.Abs(arrow.X) <= 6 && Math.Abs(arrow.Y) <= 6;
            if (arrowInMiddle && raw <= 100)
            {
                // the middle is right and the map's tiles are off here by this much: just what the maps' refit needs (Upload/MapShare.cs)
                Upload.MapShare.Note(map.Key, stamp, rx, ry, exact.X, exact.Y, speed, dt, _lastMatch?.Score ?? 0, ShareOn(settings));
            }

            string steadyNote = arrowInMiddle && metres <= MinimapCalibration.MaxSteady / 100 ? NoteSteady(map.Key, stamp, rx, ry, exact.X, exact.Y, speed, exact.At) : "";
            Log(FormattableString.Invariant($"{DateTime.Now:HH:mm:ss.fff} {map.Key}: a report {metres:0} m from the reading at {rx:0} {ry:0} ({(exact.X - cx) / 100:+0.0;-0.0} {(exact.Y - cy) / 100:+0.0;-0.0} m; ") +
                (steadyNote.Length > 0 ? steadyNote : arrowInMiddle ? "the arrow in the middle: the same offset again teaches the map" : "left out: a teleport or a lost reading") + ")");
            if (!arrowInMiddle)
            {
                MoveMiddleIfOff(key, kept, exact.X - rx, exact.Y - ry, raw, speed);
            }

            return;
        }

        _far.Clear();

        if (!_errors.TryGetValue(map.Key, out List<double>? list))
        {
            _errors[map.Key] = list = new List<double>();
        }

        list.Add(metres);
        if (list.Count > 50)
        {
            list.RemoveAt(0);
        }

        Accuracy = (Median(list), list.Count, map.Title);
        bool learnt = _cal.Add(map.Key, stamp, rx, ry, exact.X, exact.Y, speed, exact.At, steady: raw > 25);
        Upload.MapShare.Note(map.Key, stamp, rx, ry, exact.X, exact.Y, speed, dt, _lastMatch?.Score ?? 0, ShareOn(settings));
        var line = _cal.Line(map.Key);
        (double px, double py) = Aion2Maps.ToNative(map, rx, ry);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        Log(string.Format(inv, "{0:HH:mm:ss.fff} {1}: {2:0.00} m off the game's report ({3:0.00} m before the correction), {4:0.0} m/s, {5:0.00} s from the reading; " +
            "game {6:0} {7:0}, reading {8:0} {9:0} (map px {10:0} {11:0}), zoom {12:0.000}{13}{14}; map line x {15:0.0000}/{16:+0;-0} y {17:0.0000}/{18:+0;-0} from {19} reports{20}",
            DateTime.Now, map.Key, metres, raw, speed / 100, dt, exact.X, exact.Y, rx, ry, px, py, kept[4],
            _lastMatch is { } m ? string.Format(inv, ", fit {0:0.00} pieces {1}/{2} margin {3:0.00}", m.Score, m.Agree, m.Pieces, m.Margin) : "",
            _lastArrow is { } a ? string.Format(inv, ", arrow {0:+0.0;-0.0} {1:+0.0;-0.0} px", a.X, a.Y) : "",
            line.X.A, line.X.B, line.Y.A, line.Y.B, _cal.Count(map.Key), learnt ? "" : " (not learnt: too fast)"));
    }

    /// <summary>The map accuracy data goes (Upload/MapShare.cs): switched on and the privacy terms accepted.</summary>
    private static bool ShareOn(MeterSettings settings) => settings.ShareMapData && PrivacyConsent.IsAccepted(settings);

    // minimap-log.txt beside the settings: the checks against the game's reports (kept under 256 KB)
    private static void Log(string text)
    {
        try
        {
            string path = Path.Combine(Path.GetDirectoryName(Aion2CharacterStore.DefaultPath)!, "minimap-log.txt");
            if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024)
            {
                File.Delete(path);
            }

            File.AppendAllText(path, text + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // no log then
        }
    }

    /// <summary>Where the player's white arrow is drawn, from where the reader puts the player (pixels; null when it is not seen): the middle of the bright
    /// pixels within a tenth of the minimap's height. Only written to the log for now, to see whether it would tell the player's point better.</summary>
    private static (double X, double Y)? ArrowOffset(Bitmap whole, Layout layout)
    {
        int r = Math.Max(6, (int)Math.Round(layout.HalfH * 0.11));
        int x0 = (int)Math.Round(layout.Cx) - r, y0 = (int)Math.Round(layout.Cy) - r;
        if (x0 < 0 || y0 < 0 || x0 + (2 * r) >= whole.Width || y0 + (2 * r) >= whole.Height)
        {
            return null;
        }

        GrayImage near = Grey(whole, new Rectangle(x0, y0, (2 * r) + 1, (2 * r) + 1));
        double sx = 0, sy = 0;
        int n = 0;
        for (int y = 0; y < near.Height; y++)
        {
            for (int x = 0; x < near.Width; x++)
            {
                if (near[x, y] >= 225)
                {
                    sx += x;
                    sy += y;
                    n++;
                }
            }
        }

        return n is >= 6 and <= 400 ? ((sx / n) + x0 - layout.Cx, (sy / n) + y0 - layout.Cy) : null;
    }

    private static double Median(IEnumerable<double> values)
    {
        double[] v = values.OrderBy(x => x).ToArray();
        return v.Length == 0 ? 0 : v.Length % 2 == 1 ? v[v.Length / 2] : (v[(v.Length / 2) - 1] + v[v.Length / 2]) / 2;
    }

    private void Forget(string key)
    {
        MeterSettings s = MeterSettings.Load();
        if (s.MinimapLayouts.Remove(key))
        {
            s.Save();
        }

        _settings = s;
        _lastRaw = null;
    }

    private static double Distance(double ax, double ay, double bx, double by) => Math.Sqrt(((ax - bx) * (ax - bx)) + ((ay - by) * (ay - by)));

    // ---- off the UI thread ----

    private Outcome Work(Job job)
    {
        var watch = Stopwatch.StartNew();
        DateTime at = DateTime.UtcNow;
        Bitmap? whole = Capture(job.Game, job.Client);
        if (whole is null)
        {
            return new Outcome(null, null, job.Layout is null, watch.Elapsed.TotalMilliseconds, null);
        }

        Layout? layout = job.Layout;
        Layout? found = null;
        if (layout is null || job.Recheck)
        {
            // searched (the first time, or again after many reads without a fix: then the place kept stays unless the search finds one)
            found = Search(whole, job with { Reach = Math.Min(job.Reach, 120) });
            if (job.ArrowCheck && found is not null && (ArrowOffset(whole, found) is null || (job.Layout is { } kept && Math.Abs(kept.Cx - found.Cx) < 3 && Math.Abs(kept.Cy - found.Cy) < 3)))
            {
                found = null; // no arrow there either, or the same place: the place kept
            }

            layout = found ?? layout;
        }

        (double X, double Y)? fix = null, arrow = null;
        MinimapFix? match = null;
        MinimapCone.Reading? coneSeen = null;
        if (layout is not null)
        {
            (fix, match) = Read(whole, job, layout);
            arrow = ArrowOffset(whole, layout);
            if (job.Cone)
            {
                coneSeen = ReadCone(whole, layout);
            }
        }

        string? saved = job.Save ? SavePicture(whole, job, layout, match, fix) : null;
        if (job.SaveCorner && layout is null)
        {
            SaveCornerPicture(whole, job);
        }

        if (job.SaveLost && fix is null && layout is not null)
        {
            SaveLostPicture(whole, job, layout, match);
        }

        return new Outcome(fix, found, job.Layout is null, watch.Elapsed.TotalMilliseconds, saved, at, match, arrow, coneSeen);
    }

    /// <summary>The minimap's view cone (<see cref="MinimapCone"/>): the rectangle of the minimap out of the window's picture, read around the player's spot.</summary>
    internal static MinimapCone.Reading? ReadCone(Bitmap whole, Layout layout)
    {
        int x0 = Math.Max(0, (int)(layout.Cx - layout.HalfW) - 4), y0 = Math.Max(0, (int)(layout.Cy - layout.HalfH) - 4);
        int x1 = Math.Min(whole.Width, (int)(layout.Cx + layout.HalfW) + 5), y1 = Math.Min(whole.Height, (int)(layout.Cy + layout.HalfH) + 5);
        int w = x1 - x0, h = y1 - y0;
        if (w < 40 || h < 40)
        {
            return null;
        }

        int[] pixels = new int[w * h];
        BitmapData data = whole.LockBits(new Rectangle(x0, y0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            for (int y = 0; y < h; y++)
            {
                Marshal.Copy(data.Scan0 + (y * data.Stride), pixels, y * w, w);
            }
        }
        finally
        {
            whole.UnlockBits(data);
        }

        return MinimapCone.Read(pixels, w, h, layout.Cx - x0, layout.Cy - y0, layout.HalfW, layout.HalfH);
    }

    // the game window's client area as Windows draws it for us (PrintWindow, client only, full content); null when it fails or comes back black
    private Bitmap? Capture(IntPtr game, Size client)
    {
        if (client.Width < 400 || client.Height < 300)
        {
            return null;
        }

        if (_whole is null || _whole.Width != client.Width || _whole.Height != client.Height)
        {
            _whole?.Dispose();
            _whole = new Bitmap(client.Width, client.Height, PixelFormat.Format32bppRgb);
        }

        using (var g = Graphics.FromImage(_whole))
        {
            IntPtr dc = g.GetHdc();
            try
            {
                if (!GameWindow.InRealPixels(() => PrintWindow(game, dc, PwClientOnly | PwRenderFullContent)))
                {
                    return null;
                }
            }
            finally
            {
                g.ReleaseHdc(dc);
            }
        }

        return _whole;
    }

    /// <summary>
    /// One read with the minimap's place known: its picture (the rectangle around the player, the game's marks left out) at the map's scale matched
    /// by its detail within 40 m of the guess. The fix in world units.
    /// </summary>
    internal static ((double X, double Y)?, MinimapFix?) Read(Bitmap whole, Job job, Layout layout)
    {
        if (Crop(whole, layout.Cx, layout.Cy, layout.HalfW, layout.HalfH) is not { } mini)
        {
            return (null, null);
        }

        MapRaster raster = job.Raster;
        double ppm = raster.PixelsPerMetre;
        (double gx, double gy) = raster.ToPixel(job.GuessX, job.GuessY);
        if (MinimapMatch.Locate(mini, layout.MetresPerPixel * ppm, (tw, th) => Around(raster, gx, gy, tw, th, job.Reach), MinimapMatch.DetailMetres * ppm) is not { } fix)
        {
            return (null, null);
        }

        (double wx, double wy) = raster.ToWorld(fix.X, fix.Y);
        return ((wx, wy), fix);
    }

    /// <summary>The map around a map pixel for a template of so many map pixels, <paramref name="reach"/> metres to each side.</summary>
    private static (GrayImage Map, int X0, int Y0) Around(MapRaster raster, double gx, double gy, int tw, int th, double reach)
    {
        int r = (int)(reach * raster.PixelsPerMetre);
        int x0 = (int)(gx - (tw / 2.0) - r), y0 = (int)(gy - (th / 2.0) - r);
        return (raster.Window(x0, y0, tw + (2 * r), th + (2 * r)), x0, y0);
    }

    /// <summary>
    /// Where Aion 2 draws its minimap with the game's own interface size: in the top right, its map from 0.1268 window heights left of the right edge
    /// (the player) and 0.1619 down, reaching 0.095 heights to each side and 0.062 up and down inside its frame (measured on the owner's 2560 x 1440
    /// window, 2 Nov 2026; the interface scales with the window's height).
    /// </summary>
    internal static (double Cx, double Cy, double HalfW, double HalfH) GameLayout(int width, int height) =>
        (width - (0.1268 * height), 0.1619 * height, 0.095 * height, 0.062 * height);

    /// <summary>The zooms tried when the minimap is looked for: 0.3 to 2.6 metres a screen pixel, 4 % apart (the match is sharp: 3 % off already halves it).</summary>
    private static IEnumerable<double> MetresPerPixelSteps()
    {
        for (double mpp = 0.3; mpp <= 2.6; mpp *= 1.04)
        {
            yield return mpp;
        }
    }

    /// <summary>
    /// Where the minimap is and its zoom. First where the game puts it with its own interface size (<see cref="GameLayout"/>): the zoom searched there.
    /// Else (another interface size) the top right corner of the game window is searched for the map around the guess at each zoom (by its detail; the place
    /// that stands out most from the rest of the corner wins), and the minimap is taken around that place; the game's exact reports then move its
    /// middle to where the player really is (<see cref="CheckAgainstGame"/>).
    /// </summary>
    internal static Layout? Search(Bitmap whole, Job job)
    {
        MapRaster raster = job.Raster;
        double ppm = raster.PixelsPerMetre;
        (double gx, double gy) = raster.ToPixel(job.GuessX, job.GuessY);
        int w = whole.Width, h = whole.Height;
        double detail = MinimapMatch.DetailMetres * ppm;

        // The candidates, each with its zoom found there: where the game puts the minimap with its own interface size (measured on the owner's 2560 x 1440
        // window), then the places scaled from other window sizes in the order of ScaledFrom's rules. Any piece of the minimap matches the map, so a fit does not
        // say that the piece's middle is the player (3 November 2026: a scaled place fitted and was 46 m off; the readings all that far from the truth): the
        // player's arrow does - it is drawn in the minimap's middle. The first candidate with the arrow in its middle wins, else the first that fits.
        var game = GameLayout(w, h);
        var candidates = new List<(Layout Place, IEnumerable<double> Zooms)>
        {
            (new Layout(Math.Round(game.Cx), Math.Round(game.Cy), Math.Round(game.HalfW), Math.Round(game.HalfH), 0), MetresPerPixelSteps()),
        };
        candidates.AddRange((job.Hints ?? Array.Empty<Layout>()).Select(hint => (hint, (IEnumerable<double>)new[] { 0.9, 0.94, 0.97, 1.0, 1.03, 1.06, 1.1 }.Select(f => hint.MetresPerPixel * f))));
        Layout? firstFit = null;
        foreach ((Layout place, IEnumerable<double> zooms) in candidates)
        {
            if (Crop(whole, place.Cx, place.Cy, place.HalfW, place.HalfH) is not { } piece
                || MinimapMatch.Calibrate(piece, zooms.Select(m => m * ppm), (tw, th) => Around(raster, gx, gy, tw, th, job.Reach), detail) is not { } fit)
            {
                continue;
            }

            Layout fitted = place with { MetresPerPixel = fit.Zoom / ppm };
            if (ArrowOffset(whole, fitted) is not null)
            {
                return fitted;
            }

            firstFit ??= fitted;
        }

        if (firstFit is not null)
        {
            return firstFit;
        }

        // another interface size: the top right corner (where Aion 2 keeps its minimap) searched at a quarter size or so, for the map around the guess
        int rw = (int)(w * 0.36), rh = (int)(h * 0.5), shrink = Math.Max(1, (int)Math.Ceiling(rw / 240.0));
        var corner = new Rectangle(w - rw, 0, rw, rh);
        GrayImage screen = Grey(whole, corner).Shrink(shrink);
        const int side = 120; // the map piece searched for: 120 screen pixels square at every zoom
        int small = side / shrink;
        (double X, double Y, double Mpp, double Score, double Margin)? best = null;
        foreach (double mpp in MetresPerPixelSteps().Where((_, i) => i % 2 == 0))
        {
            double sigma = MinimapMatch.DetailMetres / (mpp * shrink);
            GrayImage piece = MinimapMatch.Detail(MapAsScreen(raster, gx, gy, mpp * ppm * shrink, small), sigma);
            if (MinimapMatch.BestPlace(piece, MinimapMatch.Detail(screen, sigma), int.MaxValue) is not { Score: >= 0.2 } place)
            {
                continue;
            }

            if (best is null || place.Margin > best.Value.Margin)
            {
                best = (corner.X + (place.X * shrink) + ((shrink - 1) / 2.0), corner.Y + (place.Y * shrink) + ((shrink - 1) / 2.0), mpp, place.Score, place.Margin);
            }
        }

        if (best is not { Margin: >= 0.1 } b)
        {
            return null;
        }

        // the minimap around the found place, its zoom narrowed down there
        double halfW = Math.Min(0.07 * h, Math.Min(b.X, w - 1 - b.X) - 2), halfH = Math.Min(0.045 * h, Math.Min(b.Y, h - 1 - b.Y) - 2);
        if (halfW < 30 || halfH < 20 || Crop(whole, b.X, b.Y, halfW, halfH) is not { } around)
        {
            return null;
        }

        var steps = new[] { 0.9, 0.94, 0.97, 1.0, 1.03, 1.06, 1.1 }.Select(f => b.Mpp * f * ppm);
        return MinimapMatch.Calibrate(around, steps, (tw, th) => Around(raster, gx, gy, tw, th, job.Reach), detail) is { } z
            ? new Layout(Math.Round(b.X), Math.Round(b.Y), Math.Round(halfW), Math.Round(halfH), z.Zoom / ppm)
            : null;
    }

    /// <summary>The map around a map pixel as the minimap would draw it: <paramref name="side"/> screen pixels square, <paramref name="scale"/> map pixels a screen pixel.</summary>
    private static GrayImage MapAsScreen(MapRaster raster, double mx, double my, double scale, int side)
    {
        int need = (int)Math.Ceiling(side * scale) + 6;
        int x0 = (int)(mx - (need / 2.0)), y0 = (int)(my - (need / 2.0));
        GrayImage map = raster.Window(x0, y0, need, need);
        if (scale > 1.5)
        {
            map = MinimapMatch.Blurred(map, (int)Math.Round(scale / 2));
        }

        var piece = new GrayImage(side, side);
        double half = (side - 1) / 2.0;
        for (int v = 0; v < side; v++)
        {
            for (int u = 0; u < side; u++)
            {
                piece[u, v] = map.Sample(mx - x0 + ((u - half) * scale), my - y0 + ((v - half) * scale));
            }
        }

        return piece;
    }

    /// <summary>
    /// The minimap's picture for matching: the rectangle around the player (<paramref name="cx"/>, <paramref name="cy"/>), with what the game draws over the
    /// map left out: its white signs and text, the red monster dots, the player's own arrow in the middle, the clock in the bottom right corner and the
    /// zoom button in the top right one. Null when the rectangle leaves the window.
    /// </summary>
    internal static GrayImage? Crop(Bitmap whole, double cx, double cy, double halfW, double halfH)
    {
        int x0 = (int)Math.Round(cx - halfW), y0 = (int)Math.Round(cy - halfH), x1 = (int)Math.Round(cx + halfW), y1 = (int)Math.Round(cy + halfH);
        if (x0 < 0 || y0 < 0 || x1 >= whole.Width || y1 >= whole.Height || x1 - x0 < 40 || y1 - y0 < 30)
        {
            return null;
        }

        GrayImage mini = Grey(whole, new Rectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1));
        bool[] mask = mini.Mask!;
        int w = mini.Width, h = mini.Height;
        double mx = (w - 1) / 2.0, my = (h - 1) / 2.0, arrow = 0.09 * h;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                bool clock = x > w * 0.62 && y > h * 0.8, zoom = x > w * 0.85 && y < h * 0.25;
                bool player = ((x - mx) * (x - mx)) + ((y - my) * (y - my)) < arrow * arrow;
                if (clock || zoom || player)
                {
                    mask[(y * w) + x] = false;
                }
            }
        }

        return mini;
    }

    /// <summary>A part of the picture in grey; the game's marks are left out (white signs and text, the red dots), the map's own blue marks stay.</summary>
    private static GrayImage Grey(Bitmap whole, Rectangle rect)
    {
        var image = new GrayImage(rect.Width, rect.Height);
        var mask = new bool[rect.Width * rect.Height];
        BitmapData data = whole.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            var line = new byte[rect.Width * 4];
            for (int y = 0; y < rect.Height; y++)
            {
                Marshal.Copy(data.Scan0 + (y * data.Stride), line, 0, line.Length);
                for (int x = 0; x < rect.Width; x++)
                {
                    int b = line[x * 4], g = line[(x * 4) + 1], r = line[(x * 4) + 2];
                    int grey = ((r * 77) + (g * 150) + (b * 29)) >> 8;
                    image.Pixels[(y * rect.Width) + x] = grey;
                    mask[(y * rect.Width) + x] = grey < 200 && !(r > 150 && g < 90);
                }
            }
        }
        finally
        {
            whole.UnlockBits(data);
        }

        image.Mask = mask;
        return image;
    }

    private DateTime _lostSavedAt = DateTime.MinValue;

    /// <summary>
    /// When the reader is lost (25 reads without a fix): the minimap's rectangle alone (not the rest of the game window), with the guess and what the match
    /// gave, as minimap-lost-&lt;time&gt;.png/.txt in the Debug folder beside the settings; only the newest 10 are kept. Stays on the player's computer.
    /// </summary>
    /// <summary>The top right corner of a window where the minimap is not found (the Debug folder): to see what the search had in front of it.</summary>
    private static void SaveCornerPicture(Bitmap whole, Job job)
    {
        try
        {
            int w = (int)(whole.Width * 0.4), h = (int)(whole.Height * 0.5);
            string folder = Path.Combine(Path.GetDirectoryName(Aion2CharacterStore.DefaultPath)!, "Debug");
            Directory.CreateDirectory(folder);
            string stem = Path.Combine(folder, $"minimap-notfound-{job.Key}-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}");
            using (Bitmap crop = whole.Clone(new Rectangle(whole.Width - w, 0, w, h), whole.PixelFormat))
            {
                crop.Save(stem + ".png", ImageFormat.Png);
            }

            var inv = System.Globalization.CultureInfo.InvariantCulture;
            File.WriteAllText(stem + ".txt", string.Join(Environment.NewLine,
                $"map {job.Raster.Map.Key}, window {job.Key}, the picture is its top right corner from x {whole.Width - w}",
                string.Format(inv, "guess {0:0} {1:0} (world units, on the map's picture), looking {2:0} m around", job.GuessX, job.GuessY, job.Reach),
                "places tried from other window sizes: " + string.Join("; ", (job.Hints ?? Array.Empty<Layout>()).Select(l =>
                    string.Format(inv, "{0:0} {1:0} half {2:0} x {3:0} at {4:0.000} m a pixel", l.Cx, l.Cy, l.HalfW, l.HalfH, l.MetresPerPixel)))));
            Log(FormattableString.Invariant($"{DateTime.Now:HH:mm:ss} {job.Raster.Map.Key}: the {job.Key} window's top right corner saved as {Path.GetFileName(stem)}.png"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExternalException or ArgumentException)
        {
            // no picture this time
        }
    }

    private static void SaveLostPicture(Bitmap whole, Job job, Layout layout, MinimapFix? match)
    {
        try
        {
            int x = (int)Math.Max(0, layout.Cx - layout.HalfW - 8), y = (int)Math.Max(0, layout.Cy - layout.HalfH - 8);
            int w = (int)Math.Min(whole.Width - x, (2 * layout.HalfW) + 16), h = (int)Math.Min(whole.Height - y, (2 * layout.HalfH) + 16);
            if (w < 16 || h < 16)
            {
                return;
            }

            string folder = Path.Combine(Path.GetDirectoryName(Aion2CharacterStore.DefaultPath)!, "Debug");
            Directory.CreateDirectory(folder);
            foreach (FileInfo old in new DirectoryInfo(folder).GetFiles("minimap-lost-*").OrderByDescending(f => f.LastWriteTimeUtc).Skip(20))
            {
                old.Delete(); // the newest 10 pictures and their notes
            }

            string stem = Path.Combine(folder, $"minimap-lost-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}");
            using (Bitmap crop = whole.Clone(new Rectangle(x, y, w, h), whole.PixelFormat))
            {
                crop.Save(stem + ".png", ImageFormat.Png);
            }

            var inv = System.Globalization.CultureInfo.InvariantCulture;
            File.WriteAllText(stem + ".txt", string.Join(Environment.NewLine,
                $"map {job.Raster.Map.Key}",
                string.Format(inv, "guess {0:0} {1:0} (world units, on the map's picture), {2} reads without a fix, looking {3:0} m around", job.GuessX, job.GuessY, job.Misses, job.Reach),
                string.Format(inv, "minimap centre {0:0.0} {1:0.0} half size {2:0} x {3:0} metres/pixel {4:0.0000}", layout.Cx, layout.Cy, layout.HalfW, layout.HalfH, layout.MetresPerPixel),
                match is null ? "no match" : string.Format(inv, "best match score {0:0.000} pieces {1}/{2} margin {3:0.000}", match.Score, match.Agree, match.Pieces, match.Margin)));
            Log(FormattableString.Invariant($"{DateTime.Now:HH:mm:ss} {job.Raster.Map.Key}: lost - the minimap saved as {Path.GetFileName(stem)}.png"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExternalException or ArgumentException or OutOfMemoryException)
        {
            // no picture this time
        }
    }

    /// <summary>The picture, the minimap's place and what the reader made of it, into the Debug folder beside the settings (only when asked).</summary>
    private static string? SavePicture(Bitmap whole, Job job, Layout? layout, MinimapFix? match, (double X, double Y)? fix)
    {
        try
        {
            string folder = Path.Combine(Path.GetDirectoryName(Aion2CharacterStore.DefaultPath)!, "Debug");
            Directory.CreateDirectory(folder);
            string stem = Path.Combine(folder, $"minimap-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}");
            whole.Save(stem + ".png", ImageFormat.Png);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string text = string.Join(Environment.NewLine,
                $"map {job.Raster.Map.Key}",
                string.Format(inv, "guess {0:0} {1:0}", job.GuessX, job.GuessY),
                layout is null ? "minimap not found" : string.Format(inv, "minimap centre {0:0.0} {1:0.0} half size {2:0} x {3:0} metres/pixel {4:0.0000}", layout.Cx, layout.Cy, layout.HalfW, layout.HalfH, layout.MetresPerPixel),
                match is null ? "no match" : string.Format(inv, "match score {0:0.000} pieces {1}/{2} margin {3:0.000}", match.Score, match.Agree, match.Pieces, match.Margin),
                fix is { } f ? string.Format(inv, "fix {0:0} {1:0}", f.X, f.Y) : "no fix");
            File.WriteAllText(stem + ".txt", text);
            return stem + ".png";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExternalException or ArgumentException)
        {
            return null;
        }
    }

    private const uint PwClientOnly = 1, PwRenderFullContent = 2;

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
}
