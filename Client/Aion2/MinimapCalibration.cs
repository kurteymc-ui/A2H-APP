using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2;

/// <summary>
/// What the minimap readings are off by on one map, learnt from the game's own exact reports (a skill, a hit, a dash: 0x3c37) and put right on every
/// reading (Ui/MinimapReader.cs). Two parts:
/// <list type="bullet">
/// <item>The map's own: a world map's tiles can be placed a little off or drawn at a slightly other scale than the game's coordinates (on the owner's
/// Abyss the readings were 3 m off in one place and 10 m in another). Per axis, the game's place against the reading as a straight line (a scale
/// within 3 % and an offset) once the reports cover 400 m of that axis, else only the offset; robust (reports far off the line left out, three
/// rounds). Kept per map in minimap-calibration.json beside the settings (the last 400 reports), with the map's own numbers: a map whose tiles or
/// scale change starts again.</item>
/// <item>Here and now: what is left near the last reports (in the last 10 minutes, within about 150 m), so a place the map's line doesn't fit is
/// right after one report there. A nearby fresh report counts fully, a farther or older one less.</item>
/// </list>
/// The map's picture is drawn where these say the land is (<see cref="PictureAt"/>, 5 November 2026): the tiles' scale and place in maps.json are a little
/// off on some maps (the owner's Altgard: 10 to 17 m in the south, growing with the distance, about 2 % down), and the cubes, the collectibles and the
/// arrow - all in the game's own coordinates - stood that far from their spot on the picture.
/// Only reports made at a walk (8 m/s at most: at speed the moment of the report and of the picture differ a little) and less than 25 m off are used,
/// or up to 100 m off when several reports in a row agree on it (<see cref="Add"/>'s steady: the owner's Altgard on 3 November 2026 read 30 m west of
/// every report for an hour, each left out as a teleport). World units throughout (100 a metre).
/// </summary>
public sealed class MinimapCalibration
{
    public const double MaxSpeed = 800, MaxOff = 2500, MaxSteady = 10000, Reach = 15000, LocalAge = 600, SlopeSpan = 40000, MaxSlope = 0.03;
    private const int Keep = 400, LocalKeep = 60;

    private readonly Dictionary<string, MapCal> _maps = new(StringComparer.Ordinal);
    private readonly object _gate = new(); // the reader learns off the UI thread, the map draws on it

    /// <summary>The program's one calibration (the file beside the settings): the minimap reader learns it, the map draws its picture with it.</summary>
    public static MinimapCalibration Shared => _shared.Value;

    private static readonly Lazy<MinimapCalibration> _shared = new(() => new MinimapCalibration(DefaultPath));
    private readonly string? _path;
    private int _unsaved;

    private sealed class MapCal
    {
        public string Stamp = "";
        public List<double[]> Pairs = new();   // raw x, raw y, game x, game y
        public List<(double X, double Y, double Gx, double Gy, DateTime At)> Local = new();   // the last reports, for here and now
        public (double A, double B) Fx = (1, 0), Fy = (1, 0);
        public int Fitted;
    }

    /// <param name="path">The file the map's part is kept in (null: nothing kept, as in the self-test).</param>
    public MinimapCalibration(string? path = null)
    {
        _path = path;
        Load();
    }

    /// <summary>The file beside the settings.</summary>
    public static string DefaultPath => Path.Combine(Path.GetDirectoryName(Aion2CharacterStore.DefaultPath)!, "minimap-calibration.json");

    /// <summary>A map's own numbers (its tiles' scale and offset): learnt pairs belong to them.</summary>
    public static string StampOf(Protocol.Aion2MapInfo map) => FormattableString.Invariant($"2|{map.Scale:R}|{map.OffsetX:R}|{map.OffsetY:R}");

    private MapCal Of(string map, string stamp)
    {
        if (!_maps.TryGetValue(map, out MapCal? cal) || cal.Stamp != stamp)
        {
            _maps[map] = cal = new MapCal { Stamp = stamp };
        }

        return cal;
    }

    /// <summary>How many reports the map's part rests on.</summary>
    public int Count(string map)
    {
        lock (_gate)
        {
            return _maps.TryGetValue(map, out MapCal? cal) ? cal.Pairs.Count : 0;
        }
    }

    /// <summary>
    /// One report of the game: where the reading put the player (<paramref name="rawX"/>, <paramref name="rawY"/>, before any correction) and where the game
    /// says the player is. False when it isn't used (too fast, too far off). <paramref name="steady"/>: an offset of up to <see cref="MaxSteady"/> that
    /// several reports agreed on, or one the correction already knows here.
    /// </summary>
    public bool Add(string map, string stamp, double rawX, double rawY, double gameX, double gameY, double speed, DateTime at, bool steady = false)
    {
        double limit = steady ? MaxSteady : MaxOff;
        if (speed > MaxSpeed || Math.Abs(gameX - rawX) > limit || Math.Abs(gameY - rawY) > limit)
        {
            return false;
        }

        lock (_gate)
        {
            AddLocked(map, stamp, rawX, rawY, gameX, gameY, at);
        }

        if (Interlocked.Increment(ref _unsaved) >= 10)
        {
            Save();
        }

        return true;
    }

    private void AddLocked(string map, string stamp, double rawX, double rawY, double gameX, double gameY, DateTime at)
    {
        MapCal cal = Of(map, stamp);
        cal.Pairs.Add(new[] { Math.Round(rawX), Math.Round(rawY), Math.Round(gameX), Math.Round(gameY) });
        if (cal.Pairs.Count > Keep)
        {
            cal.Pairs.RemoveAt(0);
        }

        Fit(cal);
        cal.Local.Add((rawX, rawY, gameX, gameY, at));
        if (cal.Local.Count > LocalKeep)
        {
            cal.Local.RemoveAt(0);
        }
    }

    /// <summary>A reading put right: the map's line, then what is left near the last reports.</summary>
    public (double X, double Y) Correct(string map, string stamp, double rawX, double rawY, DateTime now)
    {
        lock (_gate)
        {
            return CorrectLocked(map, stamp, rawX, rawY, now);
        }
    }

    /// <summary>
    /// Where the map's picture lies in the game's coordinates around <paramref name="gameX"/>, <paramref name="gameY"/> (the view's middle): per axis
    /// game = A * picture + B, the picture's place being its own (maps.json) - the map's line, with what is left here and now (near the last reports) added
    /// to B. (1, 0) on both while nothing is learnt for the map.
    /// </summary>
    public ((double A, double B) X, (double A, double B) Y) PictureAt(string map, string stamp, double gameX, double gameY, DateTime now)
    {
        lock (_gate)
        {
            if (!_maps.TryGetValue(map, out MapCal? cal) || cal.Stamp != stamp)
            {
                return ((1, 0), (1, 0));
            }

            // the picture's place of the middle on the map's line, then what the correction adds there beyond the line
            double rx = (gameX - cal.Fx.B) / cal.Fx.A, ry = (gameY - cal.Fy.B) / cal.Fy.A;
            (double cx, double cy) = CorrectLocked(map, stamp, rx, ry, now);
            (double gx, double gy) = Global(cal, rx, ry);
            return ((cal.Fx.A, cal.Fx.B + (cx - gx)), (cal.Fy.A, cal.Fy.B + (cy - gy)));
        }
    }

    private (double X, double Y) CorrectLocked(string map, string stamp, double rawX, double rawY, DateTime now)
    {
        if (!_maps.TryGetValue(map, out MapCal? cal) || cal.Stamp != stamp)
        {
            return (rawX, rawY);
        }

        (double x, double y) = Global(cal, rawX, rawY);
        double sw = 0, sx = 0, sy = 0;
        foreach (var p in cal.Local)
        {
            double age = (now - p.At).TotalSeconds;
            if (age > LocalAge || age < -5)
            {
                continue;
            }

            // what the map's line leaves there now
            (double lx0, double ly0) = Global(cal, p.X, p.Y);
            double d2 = ((p.X - rawX) * (p.X - rawX)) + ((p.Y - rawY) * (p.Y - rawY));
            double w = Math.Exp(-d2 / (Reach * Reach)) * Math.Exp(-Math.Max(0, age) / (LocalAge / 2));
            sw += w;
            sx += w * (p.Gx - lx0);
            sy += w * (p.Gy - ly0);
        }

        if (sw > 0.01)
        {
            double lx = sx / Math.Max(1, sw), ly = sy / Math.Max(1, sw), len = Math.Sqrt((lx * lx) + (ly * ly));
            if (len > MaxSteady)
            {
                lx *= MaxSteady / len;
                ly *= MaxSteady / len;
            }

            x += lx;
            y += ly;
        }

        return (x, y);
    }

    /// <summary>The map's line now (for the log): scale and offset across and down.</summary>
    public ((double A, double B) X, (double A, double B) Y) Line(string map)
    {
        lock (_gate)
        {
            return _maps.TryGetValue(map, out MapCal? cal) ? (cal.Fx, cal.Fy) : ((1, 0), (1, 0));
        }
    }

    private static (double X, double Y) Global(MapCal cal, double x, double y) => ((cal.Fx.A * x) + cal.Fx.B, (cal.Fy.A * y) + cal.Fy.B);

    private static void Fit(MapCal cal)
    {
        if (cal.Pairs.Count < 5)
        {
            cal.Fx = (1, 0);
            cal.Fy = (1, 0);
            return;
        }

        cal.Fx = FitAxis(cal.Pairs.Select(p => (p[0], p[2])).ToList());
        cal.Fy = FitAxis(cal.Pairs.Select(p => (p[1], p[3])).ToList());
        cal.Fitted = cal.Pairs.Count;
    }

    /// <summary>
    /// One axis: game = a * raw + b. The slope only when the reports cover <see cref="SlopeSpan"/> (else 1), within <see cref="MaxSlope"/>; reports far
    /// off the line left out. With a slope of 1 the offset is the median difference.
    /// </summary>
    internal static (double A, double B) FitAxis(List<(double Raw, double Game)> pts)
    {
        double lo = pts.Min(p => p.Raw), hi = pts.Max(p => p.Raw);
        if (hi - lo < SlopeSpan || pts.Count < 12)
        {
            return (1, MedianOf(pts.Select(p => p.Game - p.Raw)));
        }

        // fitted, the reports far off the line left out (3 times their typical distance, at least a metre), fitted again: three rounds
        (double a, double b) = Line(pts);
        for (int round = 0; round < 3; round++)
        {
            (double ra, double rb) = (a, b);
            double typical = 1.4826 * MedianOf(pts.Select(p => Math.Abs(p.Game - ((ra * p.Raw) + rb))));
            var kept = pts.Where(p => Math.Abs(p.Game - ((ra * p.Raw) + rb)) <= Math.Max(3 * typical, 100)).ToList();
            if (kept.Count < 10)
            {
                break;
            }

            (a, b) = Line(kept);
        }

        if (Math.Abs(a - 1) > MaxSlope)
        {
            return (1, MedianOf(pts.Select(p => p.Game - p.Raw)));
        }

        return (a, b);
    }

    private static (double A, double B) Line(List<(double Raw, double Game)> pts)
    {
        double mx = pts.Average(p => p.Raw), my = pts.Average(p => p.Game), sxx = 0, sxy = 0;
        foreach (var (x, y) in pts)
        {
            sxx += (x - mx) * (x - mx);
            sxy += (x - mx) * (y - my);
        }

        double a = sxx > 0 ? sxy / sxx : 1;
        return (a, my - (a * mx));
    }

    private static double MedianOf(IEnumerable<double> values)
    {
        double[] v = values.OrderBy(x => x).ToArray();
        return v.Length == 0 ? 0 : v.Length % 2 == 1 ? v[v.Length / 2] : (v[(v.Length / 2) - 1] + v[v.Length / 2]) / 2;
    }

    /// <summary>Writes the maps' pairs to the file (also every 10 new reports).</summary>
    public void Save()
    {
        _unsaved = 0;
        if (_path is null)
        {
            return;
        }

        try
        {
            string json;
            lock (_gate)
            {
                json = JsonSerializer.Serialize(_maps.ToDictionary(m => m.Key, m => new Saved(m.Value.Stamp, m.Value.Pairs.ToList())));
            }

            AionDPS.AtomicFile.WriteAllText(_path, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // kept in memory then
        }
    }

    private void Load()
    {
        if (_path is null || !File.Exists(_path))
        {
            return;
        }

        try
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, Saved>>(File.ReadAllText(_path));
            foreach (var (map, saved) in data ?? new())
            {
                if (saved?.Stamp is null || saved.Pairs is null)
                {
                    continue;
                }

                var cal = new MapCal { Stamp = saved.Stamp, Pairs = saved.Pairs.Where(p => p is { Length: 4 } && p.All(double.IsFinite)).TakeLast(Keep).ToList() };
                Fit(cal);
                _maps[map] = cal;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            if (ex is JsonException)
            {
                AionDPS.AtomicFile.KeepDamaged(_path);
            }

            // starts afresh
        }
    }

    private sealed record Saved(string Stamp, List<double[]> Pairs);
}
