namespace AionDPS.Aion2;

/// <summary>A grey picture (brightness 0 to 255 per pixel), row by row, with an optional mask of the pixels that count.</summary>
public sealed class GrayImage
{
    public GrayImage(int width, int height)
    {
        Width = width;
        Height = height;
        Pixels = new float[width * height];
    }

    public int Width { get; }

    public int Height { get; }

    public float[] Pixels { get; }

    /// <summary>The pixels that count (null: all of them).</summary>
    public bool[]? Mask { get; set; }

    public float this[int x, int y]
    {
        get => Pixels[(y * Width) + x];
        set => Pixels[(y * Width) + x] = value;
    }

    public bool Counts(int x, int y) => Mask is null || Mask[(y * Width) + x];

    /// <summary>The brightness between pixels (bilinear; outside the picture the nearest edge).</summary>
    public float Sample(double x, double y)
    {
        x = Math.Clamp(x, 0, Width - 1.001);
        y = Math.Clamp(y, 0, Height - 1.001);
        int x0 = (int)x, y0 = (int)y;
        double fx = x - x0, fy = y - y0;
        int i = (y0 * Width) + x0;
        double top = (Pixels[i] * (1 - fx)) + (Pixels[i + 1] * fx);
        double bottom = (Pixels[i + Width] * (1 - fx)) + (Pixels[i + Width + 1] * fx);
        return (float)((top * (1 - fy)) + (bottom * fy));
    }

    /// <summary>Every <paramref name="f"/> by <paramref name="f"/> block as one pixel (its mean; it counts when half of its pixels do).</summary>
    public GrayImage Shrink(int f)
    {
        if (f <= 1)
        {
            return this;
        }

        int w = Width / f, h = Height / f;
        var small = new GrayImage(Math.Max(1, w), Math.Max(1, h));
        bool[]? mask = Mask is null ? null : new bool[small.Pixels.Length];
        for (int y = 0; y < small.Height; y++)
        {
            for (int x = 0; x < small.Width; x++)
            {
                double sum = 0;
                int n = 0, total = 0;
                for (int dy = 0; dy < f; dy++)
                {
                    int sy = (y * f) + dy;
                    if (sy >= Height)
                    {
                        continue;
                    }

                    for (int dx = 0; dx < f; dx++)
                    {
                        int sx = (x * f) + dx;
                        if (sx >= Width)
                        {
                            continue;
                        }

                        total++;
                        if (Counts(sx, sy))
                        {
                            sum += this[sx, sy];
                            n++;
                        }
                    }
                }

                small[x, y] = n > 0 ? (float)(sum / n) : 0;
                if (mask is not null)
                {
                    mask[(y * small.Width) + x] = n * 2 >= total && n > 0;
                }
            }
        }

        small.Mask = mask;
        return small;
    }
}

/// <summary>Where the minimap's middle (the player) lies on the map: native map pixels, how well the picture fits (0 to 1), and how many of the minimap's
/// nine pieces put it in the same place on their own.</summary>
public sealed record MinimapFix(double X, double Y, double Score, int Agree, int Pieces, double Margin);

/// <summary>
/// Finds the game's minimap on the world map: the minimap is a piece of the same map art (UI/Map/WorldMap, assets/aion2/maps), smaller or larger, with
/// the game's marks drawn over it and lit by its own overlays (the light circle around the player, the view cone that turns with the camera). Both
/// pictures are compared by their detail (each pixel less the blurred surroundings, <see cref="Detail"/>), so an overlay that brightens a part of the
/// minimap, a darker or paler minimap and its vignette do not matter; the blue marks of the map art count. The minimap's picture is scaled to the map's
/// own pixels and moved over the map around where the network puts the player; where the two agree best (normalised cross-correlation) is where the
/// player stands: a coarse pass over the whole search area, a fine pass at full size around its best place, a sub-pixel step from the scores around it.
/// Then the minimap is cut into 3 x 3 pieces and each is matched on its own near that place (after hidden-cube-chaser: a mark or a cloud over one
/// piece cannot move the result): the fix counts only when most pieces agree and the place fits clearly better than anywhere else. Checked on the
/// owner's own minimap (Altgard, 2 Nov 2026): fit 0.57, nine pieces of nine, 0.42 better than anywhere else, where a minimap of another place stays
/// under 0.1.
/// </summary>
public static class MinimapMatch
{
    /// <summary>The fit a fix needs at least (normalised cross-correlation of the minimap's detail).</summary>
    public static double MinScore { get; set; } = 0.3;

    /// <summary>How much better the best place must fit than the best one farther away (the coarse pass).</summary>
    public static double MinMargin { get; set; } = 0.12;

    /// <summary>The detail compared: what is finer than about this many metres (the blur subtracted).</summary>
    public const double DetailMetres = 6;

    /// <summary>
    /// The minimap scaled to the map's pixels: <paramref name="mapPerMini"/> map pixels per minimap pixel (the minimap's zoom); its middle is the player.
    /// Pixels the mask leaves out stay out. A minimap shrunk by more than a third is blurred first, so the fine detail of the screen does not alias.
    /// </summary>
    public static GrayImage Template(GrayImage mini, double mapPerMini)
    {
        GrayImage src = mini;
        if (mapPerMini < 0.7)
        {
            src = Blur(mini, Math.Max(1, (int)Math.Round(0.5 / mapPerMini)));
        }

        int tw = Math.Max(8, (int)Math.Round(mini.Width * mapPerMini)), th = Math.Max(8, (int)Math.Round(mini.Height * mapPerMini));
        var mask = new bool[tw * th];
        var t = new GrayImage(tw, th) { Mask = mask };
        double cx = (mini.Width - 1) / 2.0, cy = (mini.Height - 1) / 2.0, hx = (tw - 1) / 2.0, hy = (th - 1) / 2.0;
        for (int v = 0; v < th; v++)
        {
            for (int u = 0; u < tw; u++)
            {
                double sx = cx + ((u - hx) / mapPerMini), sy = cy + ((v - hy) / mapPerMini);
                int ix = (int)Math.Round(sx), iy = (int)Math.Round(sy);
                bool inside = ix >= 0 && iy >= 0 && ix < mini.Width && iy < mini.Height && mini.Counts(ix, iy);
                t[u, v] = inside ? src.Sample(sx, sy) : 0;
                mask[(v * tw) + u] = inside;
            }
        }

        return t;
    }

    /// <summary>
    /// The detail of a picture: each counted pixel less the mean of the counted pixels around it (three box passes, close to a Gaussian of
    /// <paramref name="sigma"/> pixels); pixels the mask leaves out stay out and do not colour their neighbours. 0 for <paramref name="sigma"/> under half a pixel: the picture as it is.
    /// </summary>
    public static GrayImage Detail(GrayImage g, double sigma)
    {
        if (sigma < 0.5)
        {
            return g;
        }

        int w = g.Width, h = g.Height, n = w * h;
        var value = new float[n];
        var weight = new float[n];
        for (int i = 0; i < n; i++)
        {
            bool counts = g.Mask is null || g.Mask[i];
            value[i] = counts ? g.Pixels[i] : 0;
            weight[i] = counts ? 1 : 0;
        }

        int r = Math.Max(1, (int)Math.Round((Math.Sqrt((4 * sigma * sigma) + 1) - 1) / 2));
        var tmp = new float[n];
        for (int pass = 0; pass < 3; pass++)
        {
            BoxPass(value, tmp, w, h, r);
            BoxPass(weight, tmp, w, h, r);
        }

        var detail = new GrayImage(w, h) { Mask = g.Mask };
        for (int i = 0; i < n; i++)
        {
            detail.Pixels[i] = (g.Mask is null || g.Mask[i]) && weight[i] > 1e-3f ? g.Pixels[i] - (value[i] / weight[i]) : 0;
        }

        return detail;
    }

    // one box pass of radius r, along the rows then down the columns (edges repeat the last pixel), in place
    private static void BoxPass(float[] a, float[] tmp, int w, int h, int r)
    {
        float inv = 1f / ((2 * r) + 1);
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            double s = 0;
            for (int k = -r; k <= r; k++)
            {
                s += a[row + Math.Clamp(k, 0, w - 1)];
            }

            for (int x = 0; x < w; x++)
            {
                tmp[row + x] = (float)s * inv;
                s += a[row + Math.Min(x + r + 1, w - 1)] - a[row + Math.Max(x - r, 0)];
            }
        }

        for (int x = 0; x < w; x++)
        {
            double s = 0;
            for (int k = -r; k <= r; k++)
            {
                s += tmp[(Math.Clamp(k, 0, h - 1) * w) + x];
            }

            for (int y = 0; y < h; y++)
            {
                a[(y * w) + x] = (float)s * inv;
                s += tmp[(Math.Min(y + r + 1, h - 1) * w) + x] - tmp[(Math.Max(y - r, 0) * w) + x];
            }
        }
    }

    /// <summary>
    /// The fix: <paramref name="template"/> is the minimap at the map's scale (<see cref="Template"/>), <paramref name="map"/> the map around the network's
    /// guess (its pixel 0,0 at <paramref name="mapX0"/>, <paramref name="mapY0"/> of the whole map), <paramref name="detail"/> the blur subtracted from both
    /// (map pixels; 0: the plain pictures). Null when the minimap does not fit well enough (another map, a menu over it, nothing but sky).
    /// </summary>
    public static MinimapFix? Find(GrayImage template, GrayImage map, int mapX0, int mapY0, double detail = 0) => Find(template, map, mapX0, mapY0, detail, true, false);

    /// <summary>
    /// The fix for a minimap at a zoom: <see cref="Template"/> and <see cref="Find"/> at the smaller of the two scales. A minimap that shows more map pixels
    /// than its own (Verteron's tiles: 2 map pixels a metre) is not blown up to the map's size: the map is shrunk instead (by the zoom, rounded), which
    /// keeps the work about the minimap's own size. <paramref name="around"/> gives the map around the guess for a template of so many map pixels.
    /// <paramref name="quick"/>: the coarse pass only (its place, fit and margin), for comparing zooms.
    /// </summary>
    public static MinimapFix? Locate(GrayImage mini, double zoom, Func<int, int, (GrayImage Map, int X0, int Y0)?> around, double detail, bool quick = false, bool strict = true)
    {
        int f = zoom > 1.25 ? (int)Math.Round(zoom) : 1;
        GrayImage t = Template(mini, zoom / f);
        if (around(t.Width * f, t.Height * f) is not { } area)
        {
            return null;
        }

        GrayImage map = f > 1 ? area.Map.Shrink(f) : area.Map;
        if (Find(t, map, 0, 0, detail / f, strict, quick) is not { } fix)
        {
            return null;
        }

        return fix with { X = area.X0 + (fix.X * f) + ((f - 1) / 2.0), Y = area.Y0 + (fix.Y * f) + ((f - 1) / 2.0) };
    }

    // <paramref name="strict"/> false: the best place whatever its fit (the zoom search compares the fits); <paramref name="quick"/>: the coarse pass only
    private static MinimapFix? Find(GrayImage template, GrayImage map, int mapX0, int mapY0, double detail, bool strict, bool quick)
    {
        int tw = template.Width, th = template.Height;
        if (map.Width < tw + 2 || map.Height < th + 2)
        {
            return null;
        }

        if (detail > 0)
        {
            template = Detail(template, detail);
            map = Detail(map, detail);
        }

        // coarse: the template about 80 pixels across (the owner's minimap, 320 map pixels: a quarter; coarser washes its detail out)
        int f = Math.Max(1, (int)Math.Round(Math.Max(tw, th) / 80.0));
        GrayImage ct = template.Shrink(f), cm = map.Shrink(f);
        Prepared? coarse = Prepare(ct, 0, 0, ct.Width, ct.Height);
        if (coarse is null)
        {
            return null;
        }

        int cw = cm.Width - ct.Width, ch = cm.Height - ct.Height;
        if (cw < 0 || ch < 0)
        {
            return null;
        }

        var scores = new double[(cw + 1) * (ch + 1)];
        int bx = 0, by = 0;
        double best = double.NegativeInfinity;
        for (int oy = 0; oy <= ch; oy++)
        {
            for (int ox = 0; ox <= cw; ox++)
            {
                double s = Ncc(coarse, cm, ox, oy);
                scores[(oy * (cw + 1)) + ox] = s;
                if (s > best)
                {
                    best = s;
                    bx = ox;
                    by = oy;
                }
            }
        }

        // the best place farther than three coarse pixels from it: a minimap that fits many places alike (sea, sky) is no fix
        double second = double.NegativeInfinity;
        for (int oy = 0; oy <= ch; oy++)
        {
            for (int ox = 0; ox <= cw; ox++)
            {
                if (Math.Abs(ox - bx) > 3 || Math.Abs(oy - by) > 3)
                {
                    second = Math.Max(second, scores[(oy * (cw + 1)) + ox]);
                }
            }
        }

        double margin = double.IsFinite(second) ? best - second : 1;
        if (quick)
        {
            return new MinimapFix(mapX0 + (bx * f) + ((ct.Width * f) - 1) / 2.0, mapY0 + (by * f) + ((ct.Height * f) - 1) / 2.0, best, 0, 0, margin);
        }

        // fine: full size around the coarse best
        Prepared? whole = Prepare(template, 0, 0, tw, th);
        if (whole is null)
        {
            return null;
        }

        int fx0 = Math.Max(0, (bx * f) - f - 1), fy0 = Math.Max(0, (by * f) - f - 1);
        int fx1 = Math.Min(map.Width - tw, (bx * f) + f + 1), fy1 = Math.Min(map.Height - th, (by * f) + f + 1);
        int px = fx0, py = fy0;
        double fine = double.NegativeInfinity;
        for (int oy = fy0; oy <= fy1; oy++)
        {
            for (int ox = fx0; ox <= fx1; ox++)
            {
                double s = Ncc(whole, map, ox, oy);
                if (s > fine)
                {
                    fine = s;
                    px = ox;
                    py = oy;
                }
            }
        }

        // a sub-pixel step from the scores either side
        double subX = Vertex(Ncc(whole, map, px - 1, py), fine, Ncc(whole, map, px + 1, py), px > 0 && px + 1 <= map.Width - tw);
        double subY = Vertex(Ncc(whole, map, px, py - 1), fine, Ncc(whole, map, px, py + 1), py > 0 && py + 1 <= map.Height - th);

        // the nine pieces, each on its own within 3 pixels of the whole minimap's place
        int agree = 0, pieces = 0;
        for (int j = 0; j < 3; j++)
        {
            for (int i = 0; i < 3; i++)
            {
                int u0 = i * tw / 3, v0 = j * th / 3, w = ((i + 1) * tw / 3) - u0, h = ((j + 1) * th / 3) - v0;
                if (Prepare(template, u0, v0, w, h) is not { } piece || piece.Count < w * h / 3)
                {
                    continue;
                }

                pieces++;
                double pb = double.NegativeInfinity;
                int qx = px, qy = py;
                for (int dy = -3; dy <= 3; dy++)
                {
                    for (int dx = -3; dx <= 3; dx++)
                    {
                        int ox = px + dx, oy = py + dy;
                        if (ox < 0 || oy < 0 || ox > map.Width - tw || oy > map.Height - th)
                        {
                            continue;
                        }

                        double s = Ncc(piece, map, ox, oy);
                        if (s > pb)
                        {
                            pb = s;
                            qx = ox;
                            qy = oy;
                        }
                    }
                }

                if (Math.Abs(qx - px) <= 1 && Math.Abs(qy - py) <= 1)
                {
                    agree++;
                }
            }
        }

        if (strict && (fine < MinScore || agree < 3 || agree * 2 <= pieces || margin < MinMargin))
        {
            return null;
        }

        return new MinimapFix(mapX0 + px + subX + ((tw - 1) / 2.0), mapY0 + py + subY + ((th - 1) / 2.0), fine, agree, pieces, margin);
    }

    /// <summary>
    /// The minimap's zoom (map pixels per minimap pixel): each of <paramref name="zooms"/> is tried with the coarse pass, the best fit (of those that stand
    /// out from the rest of the search area) is then narrowed down between its neighbours (2 %, 1 %, 0.5 % either side), and the zoom counts when the full
    /// match there is a real fix. Null when none fits. Done once, then kept. <paramref name="around"/> gives the map around the network's guess for a
    /// template of so many map pixels across and down.
    /// </summary>
    public static (double Zoom, MinimapFix Fix)? Calibrate(GrayImage mini, IEnumerable<double> zooms, Func<int, int, (GrayImage Map, int X0, int Y0)?> around, double detail = 0)
    {
        double Fit(double zoom) =>
            Locate(mini, zoom, around, detail, quick: true, strict: false) is { } fix && fix.Margin >= MinMargin / 2 ? fix.Score : -1;

        double bestZoom = 0, bestFit = -1;
        foreach (double zoom in zooms)
        {
            double fit = Fit(zoom);
            if (fit > bestFit)
            {
                bestFit = fit;
                bestZoom = zoom;
            }
        }

        if (bestFit <= 0)
        {
            return null;
        }

        foreach (double step in new[] { 0.02, 0.01, 0.005 })
        {
            double centre = bestZoom;
            foreach (double zoom in new[] { centre * (1 - step), centre * (1 + step) })
            {
                double fit = Fit(zoom);
                if (fit > bestFit)
                {
                    bestFit = fit;
                    bestZoom = zoom;
                }
            }
        }

        return Locate(mini, bestZoom, around, detail) is { } found ? (bestZoom, found) : null;
    }

    /// <summary>
    /// Where <paramref name="template"/> fits best in <paramref name="image"/> (the template's middle, in the image's pixels), how well, and how much better than
    /// anywhere farther than a template's quarter away: a coarse pass with the image at most <paramref name="coarseWidth"/> pixels wide, then full size around
    /// it. The template's mask decides which of its pixels count. Null when the template has no detail or does not fit in the image.
    /// </summary>
    public static (double X, double Y, double Score, double Margin)? BestPlace(GrayImage template, GrayImage image, int coarseWidth = 240)
    {
        int n = template.Width, m = template.Height;
        if (image.Width < n + 2 || image.Height < m + 2)
        {
            return null;
        }

        int f = Math.Max(1, Math.Max((int)Math.Round(n / 32.0), (int)Math.Ceiling(image.Width / (double)coarseWidth)));
        GrayImage ct = template.Shrink(f), ci = image.Shrink(f);
        if (Prepare(ct, 0, 0, ct.Width, ct.Height) is not { } coarse || ci.Width <= ct.Width || ci.Height <= ct.Height)
        {
            return null;
        }

        int cw = ci.Width - ct.Width, ch = ci.Height - ct.Height;
        var scores = new double[(cw + 1) * (ch + 1)];
        int bx = 0, by = 0;
        double best = double.NegativeInfinity;
        for (int oy = 0; oy <= ch; oy++)
        {
            for (int ox = 0; ox <= cw; ox++)
            {
                double s = Ncc(coarse, ci, ox, oy);
                scores[(oy * (cw + 1)) + ox] = s;
                if (s > best)
                {
                    best = s;
                    bx = ox;
                    by = oy;
                }
            }
        }

        int away = Math.Max(2, ct.Width / 4);
        double second = double.NegativeInfinity;
        for (int oy = 0; oy <= ch; oy++)
        {
            for (int ox = 0; ox <= cw; ox++)
            {
                if (Math.Abs(ox - bx) > away || Math.Abs(oy - by) > away)
                {
                    second = Math.Max(second, scores[(oy * (cw + 1)) + ox]);
                }
            }
        }

        if (Prepare(template, 0, 0, n, m) is not { } whole)
        {
            return null;
        }

        int px = bx * f, py = by * f;
        double fine = double.NegativeInfinity;
        for (int oy = Math.Max(0, (by * f) - f); oy <= Math.Min(image.Height - m, (by * f) + f); oy++)
        {
            for (int ox = Math.Max(0, (bx * f) - f); ox <= Math.Min(image.Width - n, (bx * f) + f); ox++)
            {
                double s = Ncc(whole, image, ox, oy);
                if (s > fine)
                {
                    fine = s;
                    px = ox;
                    py = oy;
                }
            }
        }

        return (px + ((n - 1) / 2.0), py + ((m - 1) / 2.0), fine, double.IsFinite(second) ? best - second : 1);
    }

    /// <summary>The normalised cross-correlation of two pictures of the same size (the first one's mask decides what counts); 0 when either has no detail.</summary>
    public static double Similarity(GrayImage a, GrayImage b) =>
        a.Width == b.Width && a.Height == b.Height && Prepare(a, 0, 0, a.Width, a.Height) is { } p ? Math.Max(0, Ncc(p, b, 0, 0)) : 0;

    /// <summary>A box blur of radius <paramref name="r"/> (the mask is kept).</summary>
    public static GrayImage Blurred(GrayImage g, int r) => r < 1 ? g : Blur(g, r);

    private sealed record Prepared(int[] U, int[] V, float[] A, int Count);

    // the template's counted pixels, zero-mean and of length 1; null when the part has no detail
    private static Prepared? Prepare(GrayImage t, int u0, int v0, int w, int h)
    {
        var us = new List<int>();
        var vs = new List<int>();
        var vals = new List<float>();
        double sum = 0;
        for (int v = v0; v < v0 + h; v++)
        {
            for (int u = u0; u < u0 + w; u++)
            {
                if (!t.Counts(u, v))
                {
                    continue;
                }

                us.Add(u);
                vs.Add(v);
                vals.Add(t[u, v]);
                sum += t[u, v];
            }
        }

        int count = vals.Count;
        if (count < 16)
        {
            return null;
        }

        double mean = sum / count, norm = 0;
        var a = new float[count];
        for (int k = 0; k < count; k++)
        {
            double d = vals[k] - mean;
            a[k] = (float)d;
            norm += d * d;
        }

        if (norm < count * 4.0)
        {
            return null; // flat: under 2 brightness steps of spread
        }

        float inv = (float)(1 / Math.Sqrt(norm));
        for (int k = 0; k < count; k++)
        {
            a[k] *= inv;
        }

        return new Prepared(us.ToArray(), vs.ToArray(), a, count);
    }

    // normalised cross-correlation of a prepared template with the map at an offset (-1 to 1)
    private static double Ncc(Prepared p, GrayImage map, int ox, int oy)
    {
        if (ox < 0 || oy < 0)
        {
            return -1;
        }

        double dot = 0, sum = 0, sq = 0;
        int w = map.Width;
        float[] px = map.Pixels;
        for (int k = 0; k < p.Count; k++)
        {
            int x = ox + p.U[k], y = oy + p.V[k];
            if (x >= w || y >= map.Height)
            {
                return -1;
            }

            float m = px[(y * w) + x];
            dot += p.A[k] * m;
            sum += m;
            sq += m * m;
        }

        double variance = sq - (sum * sum / p.Count);
        return variance <= 1e-6 ? 0 : dot / Math.Sqrt(variance);
    }

    // the top of a parabola through three scores (-0.5 to 0.5 from the middle one)
    private static double Vertex(double left, double mid, double right, bool valid)
    {
        if (!valid || !double.IsFinite(left) || !double.IsFinite(right))
        {
            return 0;
        }

        double d = left - (2 * mid) + right;
        return d >= -1e-9 ? 0 : Math.Clamp(0.5 * (left - right) / d, -0.5, 0.5);
    }

    private static GrayImage Blur(GrayImage g, int r)
    {
        var tmp = new GrayImage(g.Width, g.Height) { Mask = g.Mask };
        var output = new GrayImage(g.Width, g.Height) { Mask = g.Mask };
        for (int y = 0; y < g.Height; y++)
        {
            for (int x = 0; x < g.Width; x++)
            {
                double s = 0;
                int n = 0;
                for (int d = -r; d <= r; d++)
                {
                    int xx = Math.Clamp(x + d, 0, g.Width - 1);
                    s += g[xx, y];
                    n++;
                }

                tmp[x, y] = (float)(s / n);
            }
        }

        for (int y = 0; y < g.Height; y++)
        {
            for (int x = 0; x < g.Width; x++)
            {
                double s = 0;
                int n = 0;
                for (int d = -r; d <= r; d++)
                {
                    int yy = Math.Clamp(y + d, 0, g.Height - 1);
                    s += tmp[x, yy];
                    n++;
                }

                output[x, y] = (float)(s / n);
            }
        }

        return output;
    }
}
