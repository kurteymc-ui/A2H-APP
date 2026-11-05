using System.Diagnostics;
using System.IO;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Combat;

/// <summary>
/// The minimap reader (<see cref="MinimapMatch"/>, <see cref="Ui.MinimapReader"/>):
/// <list type="bullet">
/// <item>the owner's own minimap (assets/test/minimap-altgard-dranactus.jpg, Altgard by Dranactus, 2 Nov 2026, a picture of the game's top right corner):
/// its zoom found by itself and the place it shows;</item>
/// <item>minimaps made from the tiles as the game draws them (a rectangle, darker, with the light circle around the player and the view cone over it,
/// marks, noise; the network's guess 15 m off): within a metre on Verteron, Altgard and the Abyss;</item>
/// <item>a menu over the minimap, another map's minimap and a turned one give no fix (the meter keeps the network's guess);</item>
/// <item>a whole 2560 x 1440 game window: the minimap found where the game puts it, and read within 1.5 m; with another interface size it is found by
/// searching the corners.</item>
/// </list>
/// </summary>
public static class SelfCheckMinimap
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Minimap reader (matching the minimap against the map tiles):");
        var rng = new Random(20261102);
        bool real = RealMinimap();

        int fixes = 0;
        double worst = 0, slowest = 0;
        var perMap = new HashSet<string>();
        var cases = new (string Map, double X, double Y, double Zoom)[]
        {
            ("verteron", 5494, 7093, 1.0), ("verteron", 8348, 11819, 0.6), ("verteron", 10281, 11345, 1.6),
            ("altgard", 4586, 2117, 1.0), ("altgard", 3823, 4623, 1.6),
            ("abyss", 2209, 1578, 1.6), ("abyss", 1830, 1541, 1.6),
        };
        foreach (var c in cases)
        {
            if (Raster(c.Map) is not { } raster)
            {
                return false;
            }

            GrayImage mini = Synth(raster, c.X, c.Y, c.Zoom, 274, 180, rng, 0);
            var watch = Stopwatch.StartNew();
            MinimapFix? fix = Match(raster, mini, c.Zoom, c.X, c.Y);
            slowest = Math.Max(slowest, watch.Elapsed.TotalMilliseconds);
            if (fix is null)
            {
                Console.WriteLine($"     {c.Map} ({c.X}, {c.Y}) zoom {c.Zoom}: no fix");
                continue;
            }

            double metres = Math.Sqrt(((fix.X - c.X) * (fix.X - c.X)) + ((fix.Y - c.Y) * (fix.Y - c.Y))) / raster.PixelsPerMetre;
            Console.WriteLine($"     {c.Map} ({c.X}, {c.Y}) zoom {c.Zoom}: {metres:0.00} m off, fit {fix.Score:0.00}, {fix.Agree}/{fix.Pieces} pieces, margin {fix.Margin:0.00}");
            fixes++;
            worst = Math.Max(worst, metres);
            perMap.Add(c.Map);
        }

        bool accurate = fixes >= cases.Length - 2 && worst <= 1.0 && perMap.Count == 3;
        Console.WriteLine($"  -> minimaps as the game draws them (light circle, view cone): fixes on all three maps ({fixes} of {cases.Length}), the worst {worst:0.00} m off (needs 1 m or less); the slowest {slowest:0} ms: {accurate}");

        bool refused = false;
        if (Raster("verteron") is { } vert && Raster("altgard") is { } alt)
        {
            GrayImage menu = Synth(vert, 5494, 7093, 1.0, 274, 180, rng, 0);
            for (int y = 0; y < menu.Height; y++)
            {
                for (int x = 0; x < menu.Width; x++)
                {
                    menu[x, y] = (float)(40 + (Gauss(rng) * 3) + (y is >= 80 and < 95 && x is >= 40 and < 220 ? 150 : 0));
                }
            }

            bool noMenu = Match(vert, menu, 1.0, 5494, 7093) is null;
            bool noOther = Match(vert, Synth(alt, 4586, 2117, 1.0, 274, 180, rng, 0), 1.0, 5494, 7093) is null;
            bool noTurned = Match(vert, Synth(vert, 8348, 11819, 1.0, 274, 180, rng, 20), 1.0, 8348, 11819) is null;
            refused = noMenu && noOther && noTurned;
            Console.WriteLine($"  -> no fix from a menu over the minimap: {noMenu}, from another map's minimap: {noOther}, from a minimap turned 20 degrees: {noTurned}");
        }

        bool zoomFound = false;
        if (Raster("verteron") is { } v2)
        {
            GrayImage mini = Synth(v2, 10281, 11345, 1.27, 274, 180, rng, 0);
            var zooms = Enumerable.Range(0, 30).Select(i => 0.5 * Math.Pow(1.04, i));
            var calibrated = MinimapMatch.Calibrate(mini, zooms, (tw, th) => Around(v2, 10281, 11345, tw, th), MinimapMatch.DetailMetres * v2.PixelsPerMetre);
            zoomFound = calibrated is { } z && Math.Abs((z.Zoom / 1.27) - 1) < 0.02;
            Console.WriteLine($"  -> the minimap's zoom found by itself (1.27, between the zooms tried): {zoomFound}" + (calibrated is { } cz ? $" (found {cz.Zoom:0.000})" : " (none)"));
        }

        bool screen = ScreenCheck(rng, false) & ScreenCheck(rng, true) & ScaledCheck(rng);
        bool smooth = SmoothCheck(rng);
        bool calibrated2 = CalibrationCheck(rng);

        // the map accuracy upload (Upload/MapShare.cs): the map, its numbers, the day and each check's places to a metre - nothing that names anybody
        string json = AionDPS.Upload.MapShare.Build("altgard", "2|0.01|4000|4000", new[] { new[] { 57500.0, -140500, 58300, -139900, 4.2, 0.08, 0.6 } },
            "0123456789abcdef0123456789abcdef", new DateTime(2026, 11, 5, 9, 30, 0, DateTimeKind.Utc));
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;
        bool shared = root.GetProperty("kind").GetString() == "map-points" && root.GetProperty("day").GetString() == "2026-11-05" && root.GetProperty("map").GetString() == "altgard"
            && root.GetProperty("points")[0].GetArrayLength() == 7 && root.EnumerateObject().Select(prop => prop.Name).Order().SequenceEqual(new[] { "anonymousId", "clientVersion", "day", "formatVersion", "kind", "map", "points", "stamp" });
        Console.WriteLine($"  -> the map accuracy upload holds the map, its numbers, the day and the places, nothing that names anybody: {shared}");
        return real && accurate && refused && zoomFound && screen && smooth && calibrated2 && shared;
    }

    /// <summary>
    /// The readings put right from the game's reports (Aion2/MinimapCalibration.cs): a map whose tiles are 1 % too small across and 8 m off down (as the
    /// owner's Abyss readings looked: 3 m off in one place, 10 m in another) is learnt from reports at a walk over 600 m and put right within half a metre
    /// at a place no report was made; reports at a run teach nothing; one fresh report puts a place right at once nearby but not 500 m away, and not after
    /// 15 minutes; the map's part is kept in its file, and a map whose own numbers changed starts again.
    /// </summary>
    private static bool CalibrationCheck(Random rng)
    {
        const string map = "abyss", stamp = "s1";
        var t0 = new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc);
        (double X, double Y) Game(double x, double y) => ((1.01 * x) + 300, y + 800);
        var cal = new MinimapCalibration();
        bool fast = !cal.Add(map, stamp, 0, 0, 300, 800, 2000, t0) && cal.Count(map) == 0;
        for (int i = 0; i < 40; i++)
        {
            double x = -30000 + (i * 1500), y = 5000 + (rng.NextDouble() * 5000);
            (double gx, double gy) = Game(x, y);
            cal.Add(map, stamp, x, y, gx + (Gauss(rng) * 40), gy + (Gauss(rng) * 40), 300, t0.AddMinutes(-30));   // half an hour ago: the map's part only
        }

        (double cx, double cy) = cal.Correct(map, stamp, 12345, 7000, t0);
        (double tx, double ty) = Game(12345, 7000);
        double globalOff = Math.Sqrt(((cx - tx) * (cx - tx)) + ((cy - ty) * (cy - ty))) / 100;
        var line = cal.Line(map);
        bool global = globalOff < 0.5 && Math.Abs(line.X.A - 1.01) < 0.003 && line.Y.A == 1;

        // here and now: a place the map's line leaves 5 m off across
        (double lx, double ly) = Game(50000, 20000);
        cal.Add(map, stamp, 50000, 20000, lx + 500, ly, 0, t0);
        double near = cal.Correct(map, stamp, 52000, 20000, t0.AddSeconds(5)).X - (Game(52000, 20000).X + 500);
        double far = cal.Correct(map, stamp, -2000, 20000, t0.AddSeconds(5)).X - Game(-2000, 20000).X;   // 520 m away
        double old = cal.Correct(map, stamp, 52000, 20000, t0.AddMinutes(15)).X - Game(52000, 20000).X;
        bool local = Math.Abs(near) < 100 && Math.Abs(far) < 100 && Math.Abs(old) < 100;   // within a metre (the 5 m there or not)

        // the map's picture drawn with it (PictureAt): a picture point P lies at A * P + B in the game's coordinates - the line far from the fresh report,
        // with the 5 m added where the report was just made
        var farPic = cal.PictureAt(map, stamp, Game(-20000, 7000).X, Game(-20000, 7000).Y, t0.AddSeconds(5));
        var nearPic = cal.PictureAt(map, stamp, Game(50000, 20000).X, Game(50000, 20000).Y, t0.AddSeconds(5));
        double picFar = (farPic.X.A * -20000) + farPic.X.B - Game(-20000, 7000).X, picNear = (nearPic.X.A * 50000) + nearPic.X.B - (Game(50000, 20000).X + 500);
        bool picture = Math.Abs(picFar) < 100 && Math.Abs(picNear) < 100 && Math.Abs(farPic.Y.B - 800) < 100
            && new MinimapCalibration().PictureAt(map, stamp, 0, 0, t0) == ((1, 0), (1, 0));
        Console.WriteLine($"  -> the map's picture where the reports say the land is: {picFar / 100:0.0} m off far from the last report, {picNear / 100:0.0} m off at it: {picture}");

        string file = Path.Combine(Path.GetTempPath(), $"a2h-minimap-cal-{Environment.ProcessId}.json");
        bool kept = false;
        try
        {
            var a = new MinimapCalibration(file);
            for (int i = 0; i < 12; i++)
            {
                a.Add(map, stamp, i * 4000, 0, (i * 4000) + 700, 0, 0, t0);
            }

            a.Save();
            var b = new MinimapCalibration(file);
            kept = b.Count(map) == 12 && Math.Abs(b.Correct(map, stamp, 1000, 0, t0.AddHours(1)).X - 1700) < 1
                && b.Correct(map, "s2", 1000, 0, t0.AddHours(1)).X == 1000 && b.Add(map, "s2", 0, 0, 0, 0, 0, t0) && b.Count(map) == 1;
        }
        finally
        {
            File.Delete(file);
        }

        // a map whose tiles are 32 m off across and 10 m up here (the owner's Altgard, 3 November 2026): one report that far is left out as a teleport, the
        // same offset agreed by several reports is learnt and puts the readings around right at once
        var steady = new MinimapCalibration();
        bool alone = !steady.Add("altgard", stamp, 45485, -123727, 48600, -124877, 300, t0);
        int agreed = 0;
        foreach ((double rx, double ry, double gx, double gy) in new[] { (45485.0, -123727.0, 48600.0, -124877.0), (45129, -125034, 48302, -125944), (45964, -124807, 48925, -125827), (56618, -121854, 59723, -123011) })
        {
            agreed += steady.Add("altgard", stamp, rx, ry, gx, gy, 500, t0, steady: true) ? 1 : 0;
        }

        (double sx, double sy) = steady.Correct("altgard", stamp, 50000, -124000, t0.AddSeconds(10));
        bool steadyOk = alone && agreed == 4 && Math.Abs(sx - 50000 - 3060) < 300 && Math.Abs(sy + 124000 + 1040) < 300
            && !steady.Add("altgard", stamp, 0, 0, 20000, 0, 300, t0, steady: true);   // 200 m: no map is that far off
        Console.WriteLine($"  -> readings put right from the game's reports: the map's scale and offset learnt, {globalOff:0.00} m off where no report was (needs 0.5): {global}; " +
            $"reports at a run left out: {fast}; a fresh report nearby only ({near / 100:0.00} m near, {far / 100:0.00} m far, {old / 100:0.00} m after 15 min): {local}; kept in its file, a changed map starts again: {kept}; " +
            $"a steady 32 m offset agreed by 4 reports learnt ({(sx - 50000) / 100:+0.0;-0.0} {(sy + 124000) / 100:+0.0;-0.0} m put right) where one alone is left out: {steadyOk}");
        return global && fast && local && kept && steadyOk && picture;
    }

    /// <summary>
    /// Between two readings the arrow moves on: readings of a player walking 5 m/s east, five a second, each up to half a metre off, are smoothed into a
    /// track whose speed is the walk's within 1 m/s and whose place is closer to the true one than the readings are; the directory's guess runs on at that
    /// speed between readings (0.2 s after one: a metre on) and stops running on once the readings stop (after half a second).
    /// </summary>
    private static bool SmoothCheck(Random rng)
    {
        (double X, double Y, double Vx, double Vy, DateTime At)? track = null;
        DateTime t0 = new(2026, 11, 2, 22, 0, 0, DateTimeKind.Utc);
        double readingError = 0, trackError = 0, vx = 0;
        for (int i = 0; i < 25; i++)
        {
            DateTime at = t0.AddSeconds(i * 0.2);
            double truth = 500 * i * 0.2, rx = truth + (Gauss(rng) * 25), ry = Gauss(rng) * 25;
            var (sx, sy, svx, _) = Ui.MinimapReader.SmoothStep(ref track, rx, ry, at);
            if (i >= 10)
            {
                readingError += Math.Sqrt(((rx - truth) * (rx - truth)) + (ry * ry));
                trackError += Math.Sqrt(((sx - truth) * (sx - truth)) + (sy * sy));
                vx = svx;
            }
        }

        DateTime now = t0;
        var directory = new Aion2EntityDirectory { Clock = () => now };
        directory.NoteLocalPosition(1000, 2000, 500, 0);
        now = now.AddSeconds(0.2);
        bool runsOn = directory.BestPosition is { } p && Math.Abs(p.X - 1100) < 1 && Math.Abs(p.Y - 2000) < 1 && directory.RunOnVelocity == (500, 0);
        now = now.AddSeconds(0.5);
        bool stops = directory.RunOnVelocity == (0, 0);
        bool ok = Math.Abs(vx - 500) < 100 && trackError < readingError && runsOn && stops;
        Console.WriteLine($"  -> readings smoothed into a track: speed {vx / 100:0.0} m/s (5), {trackError / 15 / 100:0.00} m off where the readings are {readingError / 15 / 100:0.00} m off; "
            + $"the guess runs on between readings: {runsOn}, and stops once they stop: {stops}: {ok}");
        return ok;
    }

    /// <summary>
    /// The owner's own minimap (the game's top right corner on his 2560 x 1440 window, at Dranactus in Altgard): taken where the game puts the minimap,
    /// its zoom must be found by itself (0.97 map pixels a screen pixel, about a metre) and the fix must land where the match of 2 Nov 2026 put it
    /// (world 62021, -181822; the network's last exact report was 11 m away).
    /// </summary>
    private static bool RealMinimap()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "assets", "test", "minimap-altgard-dranactus.jpg");
        if (Raster("altgard") is not { } raster || !File.Exists(path))
        {
            Console.WriteLine("  -> the owner's minimap: the picture or the map is missing: False");
            return false;
        }

        using var bitmap = new System.Drawing.Bitmap(path);
        // the picture starts at the minimap frame's corner (2212, 88 of the window); the game's layout puts the player at 165.4, 145.1 of it
        var game = Ui.MinimapReader.GameLayout(2560, 1440);
        GrayImage? mini = Ui.MinimapReader.Crop(bitmap, game.Cx - 2212, game.Cy - 88, game.HalfW, game.HalfH);
        if (mini is null)
        {
            Console.WriteLine("  -> the owner's minimap: not cut out: False");
            return false;
        }

        (double gx, double gy) = raster.ToPixel(62869, -182578);
        var zooms = Enumerable.Range(0, 58).Select(i => 0.3 * Math.Pow(1.04, i) * raster.PixelsPerMetre);
        var watch = Stopwatch.StartNew();
        var found = MinimapMatch.Calibrate(mini, zooms, (tw, th) => Around(raster, gx, gy, tw, th), MinimapMatch.DetailMetres * raster.PixelsPerMetre);
        double ms = watch.Elapsed.TotalMilliseconds;
        if (found is not { } f)
        {
            Console.WriteLine("  -> the owner's minimap (Altgard): no fix: False");
            return false;
        }

        (double wx, double wy) = raster.ToWorld(f.Fix.X, f.Fix.Y);
        double metres = Math.Sqrt(((wx - 62021) * (wx - 62021)) + ((wy + 181822) * (wy + 181822))) / 100;
        bool ok = Math.Abs(f.Zoom - 0.97) < 0.02 && metres <= 3;
        Console.WriteLine($"  -> the owner's minimap (Altgard, Dranactus): zoom {f.Zoom:0.000} (0.97), {metres:0.0} m from the place, fit {f.Fix.Score:0.00}, {f.Fix.Agree}/{f.Fix.Pieces} pieces, "
            + $"margin {f.Fix.Margin:0.00}; the zoom searched in {ms:0} ms: {ok}");
        return ok;
    }

    private static MapRaster? Raster(string key) => Aion2Maps.All.FirstOrDefault(m => m.Key == key) is { } map ? new MapRaster(map) : null;

    // the network's guess 15 m off the place; the map searched 40 m around it
    private static (GrayImage Map, int X0, int Y0) Around(MapRaster raster, double x, double y, int tw, int th)
    {
        double ppm = raster.PixelsPerMetre;
        double ex = x + (15 * ppm * 0.8), ey = y - (15 * ppm * 0.6);
        int r = (int)(40 * ppm);
        int x0 = (int)(ex - (tw / 2.0) - r), y0 = (int)(ey - (th / 2.0) - r);
        return (raster.Window(x0, y0, tw + (2 * r), th + (2 * r)), x0, y0);
    }

    private static MinimapFix? Match(MapRaster raster, GrayImage mini, double zoom, double x, double y) =>
        MinimapMatch.Locate(mini, zoom, (tw, th) => Around(raster, x, y, tw, th), MinimapMatch.DetailMetres * raster.PixelsPerMetre);

    /// <summary>A minimap as the game draws it: <paramref name="w"/> x <paramref name="h"/> pixels around <paramref name="x"/>, <paramref name="y"/> (map
    /// pixels), <paramref name="zoom"/> map pixels a minimap pixel, darker, the light circle around the player and the view cone over it, six white marks
    /// and the player's arrow (as the reader masks them), noise, turned by <paramref name="turn"/> degrees.</summary>
    internal static GrayImage Synth(MapRaster raster, double x, double y, double zoom, int w, int h, Random rng, double turn)
    {
        int span = (int)(Math.Max(w, h) * zoom * 1.6) + 8, sx0 = (int)x - (span / 2), sy0 = (int)y - (span / 2);
        GrayImage map = raster.Window(sx0, sy0, span, span);
        var mask = new bool[w * h];
        var mini = new GrayImage(w, h) { Mask = mask };
        double cx = (w - 1) / 2.0, cy = (h - 1) / 2.0, a = turn * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
        for (int j = 0; j < h; j++)
        {
            for (int i = 0; i < w; i++)
            {
                double dx = (i - cx) * zoom, dy = (j - cy) * zoom;
                double rx = (dx * cos) - (dy * sin), ry = (dx * sin) + (dy * cos);
                double v = (map.Sample(x - sx0 + rx, y - sy0 + ry) * 0.7) + 20 + (Gauss(rng) * 4);
                double r = Math.Sqrt(((i - cx) * (i - cx)) + ((j - cy) * (j - cy)));
                double angle = Math.Atan2(j - cy, i - cx) * 180 / Math.PI;
                bool light = r < 0.18 * h || (r < 0.9 * w && Math.Abs((((angle - 40 + 180) % 360) + 360) % 360 - 180) < 30);
                if (light)
                {
                    v = (v * 0.55) + 120; // the light circle and the view cone
                }

                mini[i, j] = (float)Math.Clamp(v, 0, 255);
                mask[(j * w) + i] = r > 0.09 * h;
            }
        }

        for (int k = 0; k < 6; k++)
        {
            int mx = rng.Next(10, w - 10), my = rng.Next(10, h - 10);
            for (int yy = my - 4; yy < my + 4; yy++)
            {
                for (int xx = mx - 4; xx < mx + 4; xx++)
                {
                    mini[xx, yy] = 255;
                    mask[(yy * w) + xx] = false;
                }
            }
        }

        return mini;
    }

    /// <summary>
    /// A whole 2560 x 1440 game window: a noisy scene, bars of a HUD, and the minimap panel (a dark frame with a title bar, the map from Verteron around a
    /// place at 0.5 m a pixel with the light circle and the view cone, the player's arrow) while the network's guess is 17 m off. Where the game puts it
    /// (<paramref name="elsewhere"/> false) it must be found there and read within 1.5 m; 20 % smaller and elsewhere in the top right (another interface size)
    /// the corner is searched and its zoom must be found within 3 %.
    /// </summary>
    private static bool ScreenCheck(Random rng, bool elsewhere)
    {
        if (Raster("verteron") is not { } raster)
        {
            return false;
        }

        double ppm = raster.PixelsPerMetre;
        const int w = 2560, h = 1440;
        const double px = 8348, py = 11819, mpp = 0.5;
        var game = Ui.MinimapReader.GameLayout(w, h);
        double cx = game.Cx, cy = game.Cy, halfW = 0.1094 * h, halfH = 0.073 * h;
        if (elsewhere)
        {
            cx = w - 700;
            cy = 380;
            halfW *= 0.8;
            halfH *= 0.8;
        }

        using System.Drawing.Bitmap bitmap = Window(raster, rng, w, h, cx, cy, halfW, halfH, px, py, mpp);
        (double gwx, double gwy) = raster.ToWorld(px + (12 * ppm * 0.7), py + (12 * ppm * 0.7));
        var job = new Ui.MinimapReader.Job(IntPtr.Zero, new System.Drawing.Size(w, h), "test", raster, gwx, gwy, null, false, 0);
        var watch = Stopwatch.StartNew();
        Ui.MinimapReader.Layout? layout = Ui.MinimapReader.Search(bitmap, job);
        double searchMs = watch.Elapsed.TotalMilliseconds;
        bool zoom = layout is not null && Math.Abs((layout.MetresPerPixel / mpp) - 1) < (elsewhere ? 0.03 : 0.02);
        string where = elsewhere ? "with another interface size (the corner searched)" : "where the game puts it";
        if (elsewhere)
        {
            bool near = layout is not null && Math.Abs(layout.Cx - cx) < halfW && Math.Abs(layout.Cy - cy) < halfH;
            Console.WriteLine($"  -> a whole game window, the minimap {where}: found" + (layout is null ? " nothing" : $" at {layout.Cx:0}, {layout.Cy:0} ({cx:0}, {cy:0}), {layout.MetresPerPixel:0.000} m a pixel (0.5)")
                + $" in {searchMs:0} ms: {zoom && near}");
            return zoom && near;
        }

        bool placed = layout is not null && Math.Abs(layout.Cx - cx) <= 1 && Math.Abs(layout.Cy - cy) <= 1;
        double metres = double.NaN, readMs = double.NaN;
        if (layout is not null)
        {
            Ui.MinimapReader.Read(bitmap, job with { Layout = layout }, layout); // the tiles read once
            watch.Restart();
            var (fix, _) = Ui.MinimapReader.Read(bitmap, job with { Layout = layout }, layout);
            readMs = watch.Elapsed.TotalMilliseconds;
            if (fix is { } f)
            {
                (double wx, double wy) = raster.ToWorld(px, py);
                metres = Math.Sqrt(((f.X - wx) * (f.X - wx)) + ((f.Y - wy) * (f.Y - wy))) / 100;
            }
        }

        bool read = metres <= 1.5;
        Console.WriteLine($"  -> a whole game window, the minimap {where}: found" + (layout is null ? " nothing" : $" at {layout.Cx:0}, {layout.Cy:0}, {layout.MetresPerPixel:0.000} m a pixel (0.5)")
            + $" in {searchMs:0} ms: {placed && zoom}; the player read {metres:0.00} m off in {readMs:0} ms: {read}");
        return placed && zoom && read;
    }

    /// <summary>A whole game window of <paramref name="w"/> x <paramref name="h"/>: a noisy scene, bars of a HUD, and the minimap panel around
    /// (<paramref name="cx"/>, <paramref name="cy"/>) showing Verteron around (<paramref name="px"/>, <paramref name="py"/>) at <paramref name="mpp"/> m a pixel.</summary>
    private static System.Drawing.Bitmap Window(MapRaster raster, Random rng, int w, int h, double cx, double cy, double halfW, double halfH, double px, double py, double mpp)
    {
        double ppm = raster.PixelsPerMetre;
        var grey = new float[w * h];
        for (int i = 0; i < grey.Length; i++)
        {
            grey[i] = (float)(70 + (Gauss(rng) * 25));
        }

        for (int k = 0; k < 40; k++)
        {
            int x0 = rng.Next(0, w - 200), y0 = rng.Next(0, h - 60), bw = rng.Next(20, 200), bh = rng.Next(10, 60), v = rng.Next(0, 255);
            for (int y = y0; y < y0 + bh; y++)
            {
                for (int x = x0; x < x0 + bw; x++)
                {
                    grey[(y * w) + x] = v;
                }
            }
        }

        // the frame and the title bar, then the map with its overlays
        int fx0 = (int)(cx - halfW) - 10, fx1 = (int)(cx + halfW) + 10, fy0 = (int)(cy - halfH) - 42, fy1 = (int)(cy + halfH) + 10;
        for (int y = fy0; y <= fy1; y++)
        {
            for (int x = fx0; x <= fx1; x++)
            {
                grey[(y * w) + x] = y < (int)(cy - halfH) - 4 ? 30 : 45;
            }
        }

        int mw = (int)(2 * halfW), mh = (int)(2 * halfH);
        GrayImage map = Synth(raster, px, py, mpp * ppm, mw, mh, rng, 0);
        int mx0 = (int)Math.Round(cx - ((mw - 1) / 2.0)), my0 = (int)Math.Round(cy - ((mh - 1) / 2.0));
        for (int y = 0; y < mh; y++)
        {
            for (int x = 0; x < mw; x++)
            {
                grey[((my0 + y) * w) + mx0 + x] = map[x, y];
            }
        }

        var bitmap = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        var line = new byte[w * 4];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                byte b = (byte)Math.Clamp((int)grey[(y * w) + x], 0, 255);
                line[x * 4] = line[(x * 4) + 1] = line[(x * 4) + 2] = b;
                line[(x * 4) + 3] = 255;
            }

            System.Runtime.InteropServices.Marshal.Copy(line, 0, data.Scan0 + (y * data.Stride), line.Length);
        }

        bitmap.UnlockBits(data);
        return bitmap;
    }

    /// <summary>
    /// A window of a size never read before (3840 x 2160, the interface 1.5 times the 2560 x 1440 one): the minimap's place and zoom known only for
    /// 2560 x 1440, and not where <see cref="Ui.MinimapReader.GameLayout"/> puts it. The place scaled from the known one must be tried first and found,
    /// the zoom within 3 % (3 November 2026: the owner's game at 4480 x 2520 never had its minimap found, and the cube's distance was guessed).
    /// </summary>
    private static bool ScaledCheck(Random rng)
    {
        if (Raster("verteron") is not { } raster)
        {
            return false;
        }

        const int w0 = 2560, h0 = 1440, w = 3840, h = 2160;
        const double px = 8348, py = 11819, mpp0 = 0.5, k = 1.5;
        double cx0 = w0 - 700, cy0 = 380, halfW0 = 0.1094 * h0 * 0.8, halfH0 = 0.073 * h0 * 0.8;
        var known = new Dictionary<string, double[]> { [$"{w0}x{h0}"] = new[] { cx0, cy0, halfW0, halfH0, mpp0 } };
        List<Ui.MinimapReader.Layout> hints = Ui.MinimapReader.ScaledFrom(known, w, h);
        double cx = w - ((w0 - cx0) * k), cy = cy0 * k;
        using System.Drawing.Bitmap bitmap = Window(raster, rng, w, h, cx, cy, halfW0 * k, halfH0 * k, px, py, mpp0 / k);
        double ppm = raster.PixelsPerMetre;
        (double gwx, double gwy) = raster.ToWorld(px + (12 * ppm * 0.7), py + (12 * ppm * 0.7));
        var job = new Ui.MinimapReader.Job(IntPtr.Zero, new System.Drawing.Size(w, h), "test", raster, gwx, gwy, null, false, 0, Hints: hints);
        var watch = Stopwatch.StartNew();
        Ui.MinimapReader.Layout? layout = Ui.MinimapReader.Search(bitmap, job);
        double ms = watch.Elapsed.TotalMilliseconds;
        bool ok = hints.Count > 0 && layout is not null && Math.Abs(layout.Cx - cx) <= 2 && Math.Abs(layout.Cy - cy) <= 2 && Math.Abs((layout.MetresPerPixel * k / mpp0) - 1) < 0.03;
        Console.WriteLine($"  -> a {w} x {h} window, the minimap known only at {w0} x {h0}: {hints.Count} places scaled from it; found"
            + (layout is null ? " nothing" : $" at {layout.Cx:0}, {layout.Cy:0} ({cx:0}, {cy:0}), {layout.MetresPerPixel:0.000} m a pixel ({mpp0 / k:0.000})") + $" in {ms:0} ms: {ok}");
        return ok;
    }

    private static double Gauss(Random rng) => Math.Sqrt(-2 * Math.Log(1 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble());
}
