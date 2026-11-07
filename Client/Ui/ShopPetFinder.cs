using System.Drawing;
using System.IO;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace AionDPS.Ui;

/// <summary>A picture as colour planes of floats (0 to 255), for <see cref="ShopPetFinder"/>.</summary>
internal sealed class ColorPlanes
{
    public ColorPlanes(int width, int height)
    {
        Width = width;
        Height = height;
        R = new float[width * height];
        G = new float[width * height];
        B = new float[width * height];
    }

    /// <summary>The opacity (0 to 255), for an icon; null for a picture of the game.</summary>
    public float[]? A { get; private set; }

    public int Width { get; }
    public int Height { get; }
    public float[] R { get; }
    public float[] G { get; }
    public float[] B { get; }

    /// <summary>The picture drawn at <paramref name="width"/> x <paramref name="height"/> (smoothed when it shrinks, so a small copy keeps the colours of what it stands for).</summary>
    public static ColorPlanes Of(Bitmap source, int width, int height, bool alpha = false)
    {
        PixelFormat format = alpha ? PixelFormat.Format32bppArgb : PixelFormat.Format32bppRgb;
        using var scaled = new Bitmap(width, height, format);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingMode = CompositingMode.SourceCopy;
            using var attributes = new ImageAttributes();
            attributes.SetWrapMode(WrapMode.TileFlipXY); // no dark seam at the edges
            g.DrawImage(source, new Rectangle(0, 0, width, height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        }

        var planes = new ColorPlanes(width, height);
        if (alpha)
        {
            planes.A = new float[width * height];
        }

        BitmapData data = scaled.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, format);
        try
        {
            var row = new byte[data.Stride];
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(data.Scan0 + (y * data.Stride), row, 0, data.Stride);
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    planes.B[i] = row[x * 4];
                    planes.G[i] = row[x * 4 + 1];
                    planes.R[i] = row[x * 4 + 2];
                    if (planes.A is not null)
                    {
                        planes.A[i] = row[x * 4 + 3];
                    }
                }
            }
        }
        finally
        {
            scaled.UnlockBits(data);
        }

        return planes;
    }
}

/// <summary>
/// Finds where a pet's card is in the Black Cloud shop (7 November 2026, players: "put another overlay over the Urugugu Brawler, so you don't have to search
/// your screen"). The shop's card shows the same portrait as the pet window's icon (assets/aion2/pets/icons), so the icon is looked for in the game window's
/// own picture (PrintWindow, as the minimap reader takes it): first coarsely at every size a card can have, then finely around the best few places. The
/// match is normalised colour correlation (1 = the same picture); on a picture of the shop's Pets page with ten cards each pet scored 0.74 to 0.86 at its own
/// card. Which pets are on the page shown comes from the shop's own frame, so only those are looked for, and nothing is drawn below <see cref="Enough"/>.
/// </summary>
internal static class ShopPetFinder
{
    /// <summary>The height of the coarse copy of the picture and of the fine one (pixels).</summary>
    public const int CoarseHeight = 140, FineHeight = 540;

    /// <summary>The least score a card is marked at.</summary>
    /// <remarks>On the test picture the ten pets of the page scored 0.86 to 0.97 at their own cards; of the 197 pets not on it, 99 % stayed below 0.78 at their best place (one look-alike of the same monster 0.85). Only the pets the page holds are looked for.</remarks>
    public const double Enough = 0.8;

    /// <summary>The portrait's size as a share of the game window's height: from small to large UI scales.</summary>
    public const double SmallestShare = 0.07, LargestShare = 0.17;

    /// <summary>A pet's portrait found: its square in the picture as shares of its width and height (0 to 1), and how well it matched.</summary>
    public readonly record struct Found(int PetId, double X, double Y, double Size, double SizeY, double Score)
    {
        /// <summary>The whole card around the portrait (title above, price below), as shares of the picture: measured on the shop's Pets page.</summary>
        public (double X, double Y, double W, double H) Card => (X - 0.29 * Size, Y - 0.72 * SizeY, 1.52 * Size, 2.14 * SizeY);
    }

    /// <summary>The share of the height the last found portrait had: the next search tries sizes near it first.</summary>
    private static double? _lastShare;

    /// <summary>Looks for each pet's portrait in the picture (given as its coarse and fine copies). Pets not found well enough are left out.</summary>
    public static List<Found> Find(ColorPlanes coarse, ColorPlanes fine, IEnumerable<int> petIds, Func<int, Bitmap?> icon)
    {
        var found = new List<Found>();
        foreach (int petId in petIds)
        {
            using Bitmap? picture = icon(petId);
            if (picture is null)
            {
                continue;
            }

            if (FindOne(coarse, fine, picture, _lastShare) is { } hit && hit.Score >= Enough)
            {
                found.Add(hit with { PetId = petId });
            }
            else if (_lastShare is not null && FindOne(coarse, fine, picture, null) is { } wide && wide.Score >= Enough)
            {
                found.Add(wide with { PetId = petId }); // the UI was scaled meanwhile: every size again
            }
        }

        if (found.Count > 0)
        {
            _lastShare = found.Average(f => f.SizeY);
        }

        return found;
    }

    /// <summary>The pet's icon as it ships (64 px); null when there is none.</summary>
    public static Bitmap? Icon(int petId)
    {
        if (Aion2.Protocol.Aion2Pets.PetIconPath(petId) is not { } path || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var file = new Bitmap(path);
            return new Bitmap(file); // a copy: the file is not kept open
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>The best place of one portrait: the coarse search at every size (or near <paramref name="share"/>), the fine one around its best few places.</summary>
    internal static Found? FindOne(ColorPlanes coarse, ColorPlanes fine, Bitmap icon, double? share)
    {
        double lo = share is { } s ? Math.Max(SmallestShare, s - 0.012) : SmallestShare;
        double hi = share is { } s2 ? Math.Min(LargestShare, s2 + 0.012) : LargestShare;
        var candidates = new List<(double Score, double Share, int X, int Y)>();
        int lastSize = -1;
        for (double sh = lo; sh <= hi + 1e-9; sh += 0.006)
        {
            int t = (int)Math.Round(sh * coarse.Height);
            if (t < 8 || t == lastSize || t >= coarse.Width || t >= coarse.Height)
            {
                continue;
            }

            lastSize = t;
            var template = Template.Of(icon, t);
            float[] map = Correlate(coarse, template, 0, 0, coarse.Width - t + 1, coarse.Height - t + 1, out int mw, out int mh);
            // the best three places at this size, each a portrait's width away from the others
            for (int k = 0; k < 3; k++)
            {
                int best = -1;
                for (int i = 0; i < map.Length; i++)
                {
                    if (best < 0 || map[i] > map[best])
                    {
                        best = i;
                    }
                }

                if (best < 0 || map[best] <= -1)
                {
                    break;
                }

                int bx = best % mw, by = best / mw;
                candidates.Add((map[best], sh, bx, by));
                for (int y = Math.Max(0, by - t / 2); y <= Math.Min(mh - 1, by + t / 2); y++)
                {
                    for (int x = Math.Max(0, bx - t / 2); x <= Math.Min(mw - 1, bx + t / 2); x++)
                    {
                        map[y * mw + x] = -2;
                    }
                }
            }
        }

        Found? result = null;
        double scale = (double)fine.Height / coarse.Height;
        foreach (var (_, sh, cx, cy) in candidates.OrderByDescending(c => c.Score).Take(6))
        {
            foreach (double d in new[] { -0.004, -0.002, 0, 0.002, 0.004 })
            {
                int t = (int)Math.Round((sh + d) * fine.Height);
                if (t < 8 || t >= fine.Width || t >= fine.Height)
                {
                    continue;
                }

                const int margin = 8;
                int x0 = Math.Max(0, (int)(cx * scale) - margin), y0 = Math.Max(0, (int)(cy * scale) - margin);
                int x1 = Math.Min(fine.Width - t + 1, (int)(cx * scale) + margin + 1), y1 = Math.Min(fine.Height - t + 1, (int)(cy * scale) + margin + 1);
                if (x1 <= x0 || y1 <= y0)
                {
                    continue;
                }

                var template = Template.Of(icon, t);
                float[] map = Correlate(fine, template, x0, y0, x1, y1, out int mw, out _);
                for (int i = 0; i < map.Length; i++)
                {
                    if (result is null || map[i] > result.Value.Score)
                    {
                        int x = x0 + i % mw, y = y0 + i / mw;
                        result = new Found(0, (double)x / fine.Width, (double)y / fine.Height, (double)t / fine.Width, (double)t / fine.Height, map[i]);
                    }
                }
            }
        }

        return result;
    }

    /// <summary>How well the portrait still matches where it was found (a few pixels around it, the same size): the shop's page still shows that card.</summary>
    internal static double ScoreAt(ColorPlanes fine, Bitmap icon, Found at)
    {
        int t = (int)Math.Round(at.SizeY * fine.Height);
        int x = (int)Math.Round(at.X * fine.Width), y = (int)Math.Round(at.Y * fine.Height);
        if (t < 8 || t >= fine.Width || t >= fine.Height)
        {
            return 0;
        }

        int x0 = Math.Max(0, x - 2), y0 = Math.Max(0, y - 2), x1 = Math.Min(fine.Width - t + 1, x + 3), y1 = Math.Min(fine.Height - t + 1, y + 3);
        float[] map = Correlate(fine, Template.Of(icon, t), x0, y0, x1, y1, out _, out _);
        return map.Length == 0 ? 0 : map.Max();
    }

    /// <summary>
    /// The icon at a size, only where it is opaque (the icons are the creature cut out: 42 to 78 % of the square; the card shows it on its own background,
    /// so the see-through corners would only blur the match - masked, the pets of the test picture scored 0.86 to 0.97 instead of 0.74 to 0.86): the offsets
    /// of those pixels, their colours less their mean, and their energy.
    /// </summary>
    private sealed class Template
    {
        public int Size;
        public int[] Dy = Array.Empty<int>(), Dx = Array.Empty<int>();
        public float[] R = Array.Empty<float>(), G = Array.Empty<float>(), B = Array.Empty<float>();
        public double Energy;

        public static Template Of(Bitmap icon, int size)
        {
            ColorPlanes p = ColorPlanes.Of(icon, size, size, alpha: true);
            var keep = Enumerable.Range(0, size * size).Where(i => p.A![i] >= 200).ToArray();
            if (keep.Length < 16)
            {
                keep = Enumerable.Range(0, size * size).ToArray(); // an icon without a cut-out: all of it
            }

            float mr = keep.Average(i => p.R[i]), mg = keep.Average(i => p.G[i]), mb = keep.Average(i => p.B[i]);
            var t = new Template
            {
                Size = size, Dy = keep.Select(i => i / size).ToArray(), Dx = keep.Select(i => i % size).ToArray(),
                R = keep.Select(i => p.R[i] - mr).ToArray(), G = keep.Select(i => p.G[i] - mg).ToArray(), B = keep.Select(i => p.B[i] - mb).ToArray(),
            };
            t.Energy = Enumerable.Range(0, keep.Length).Sum(k => (double)t.R[k] * t.R[k] + (double)t.G[k] * t.G[k] + (double)t.B[k] * t.B[k]);
            return t;
        }
    }

    /// <summary>
    /// Normalised colour correlation of the template at every top-left corner (x0..x1, y0..y1) of the picture, over the template's opaque pixels only: its
    /// colours less their mean times the picture's, over the root of both energies (the picture's measured around its own mean there, per colour) -
    /// OpenCV's TM_CCOEFF_NORMED with a mask, over three planes.
    /// </summary>
    private static float[] Correlate(ColorPlanes picture, Template template, int x0, int y0, int x1, int y1, out int mw, out int mh)
    {
        int w = picture.Width;
        int n = template.Dx.Length;
        int width = x1 - x0, height = y1 - y0;
        mw = width;
        mh = height;
        var map = new float[Math.Max(0, width * height)];
        if (width <= 0 || height <= 0 || n == 0 || template.Energy <= 1e-6)
        {
            return map;
        }

        // the offsets of the template's pixels in the picture's rows
        var offset = new int[n];
        for (int k = 0; k < n; k++)
        {
            offset[k] = template.Dy[k] * w + template.Dx[k];
        }

        Parallel.For(0, height, row =>
        {
            int v = y0 + row;
            for (int u = x0; u < x1; u++)
            {
                int origin = v * w + u;
                double num = 0, sr = 0, sg = 0, sb = 0, sq = 0;
                for (int k = 0; k < n; k++)
                {
                    int pi = origin + offset[k];
                    float r = picture.R[pi], g = picture.G[pi], b = picture.B[pi];
                    num += template.R[k] * r + template.G[k] * g + template.B[k] * b;
                    sr += r;
                    sg += g;
                    sb += b;
                    sq += r * r + g * g + b * b;
                }

                double energy = sq - (sr * sr + sg * sg + sb * sb) / n;
                map[row * width + (u - x0)] = energy <= 1e-6 ? 0 : (float)(num / Math.Sqrt(energy * template.Energy));
            }
        });
        return map;
    }
}

/// <summary>
/// A made-up page of the Black Cloud shop for the self-test and the pictures (no picture of a player's game in the repository): ten cards where the shop's
/// Pets page has them (measured on a picture of it: an arch of six, four below), each a dark card with the pet's portrait, its name above and a price below,
/// on a dark curtain.
/// </summary>
internal static class ShopSample
{
    /// <summary>The page's pets in the order of <see cref="Portraits"/>: Aullaeu Shaman, Slime, Biruta, Urugugu Brawler, Elite Karnif, Rafflesia, Marsh Targon,
    /// Congealed Drana, Vivel, Drana Predator.</summary>
    public static readonly int[] Pets = { 1040, 1061, 1065, 1078, 1099, 1136, 1141, 1217, 1188, 1174 };

    /// <summary>Each portrait's top-left corner as shares of the page's width and height; the portrait is <see cref="PortraitShare"/> of the height.</summary>
    public static readonly (double X, double Y)[] Portraits =
    {
        (0.069, 0.346), (0.217, 0.405), (0.362, 0.426), (0.508, 0.426), (0.654, 0.404), (0.799, 0.346),
        (0.097, 0.668), (0.244, 0.714), (0.625, 0.714), (0.770, 0.666),
    };

    public const double PortraitShare = 0.1205;

    public static Bitmap Page(int width, int height)
    {
        var page = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(page);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        using (var curtain = new LinearGradientBrush(new Rectangle(0, 0, width, height), Color.FromArgb(0x16, 0x0C, 0x10), Color.FromArgb(0x4A, 0x10, 0x16), 90f))
        {
            g.FillRectangle(curtain, 0, 0, width, height);
        }

        // folds of the curtain
        var dice = new Random(7);
        for (int i = 0; i < 40; i++)
        {
            int x = dice.Next(width);
            using var fold = new SolidBrush(Color.FromArgb(dice.Next(10, 30), 0, 0, 0));
            g.FillRectangle(fold, x, 0, dice.Next(6, 30), height);
        }

        double s = PortraitShare * height;
        using var title = new Font("Segoe UI", (float)(s * 0.13), FontStyle.Regular, GraphicsUnit.Pixel);
        using var price = new Font("Segoe UI", (float)(s * 0.12), FontStyle.Bold, GraphicsUnit.Pixel);
        using var white = new SolidBrush(Color.FromArgb(0xEE, 0xEE, 0xEE));
        for (int i = 0; i < Pets.Length; i++)
        {
            double px = Portraits[i].X * width, py = Portraits[i].Y * height;
            var card = new RectangleF((float)(px - 0.29 * s), (float)(py - 0.72 * s), (float)(1.52 * s), (float)(2.14 * s));
            using (var face = new LinearGradientBrush(card, Color.FromArgb(0x2A, 0x2A, 0x30), Color.FromArgb(0x12, 0x12, 0x16), 90f))
            {
                g.FillRectangle(face, card);
            }

            using (var edge = new Pen(Color.FromArgb(0x9A, 0x90, 0x80), 2f))
            {
                g.DrawRectangle(edge, card.X, card.Y, card.Width, card.Height);
            }

            string name = "Pet: " + (Aion2.Protocol.Aion2Pets.PetName(Pets[i], "en") ?? "");
            g.DrawString(name.Length > 18 ? name[..16] + "..." : name, title, white, (float)(px - 0.2 * s), (float)(py - 0.4 * s));
            using (Bitmap? icon = ShopPetFinder.Icon(Pets[i]))
            {
                if (icon is not null)
                {
                    g.DrawImage(icon, new RectangleF((float)px, (float)py, (float)s, (float)s));
                }
            }

            g.DrawString((70 + 10 * (i % 3)).ToString(System.Globalization.CultureInfo.InvariantCulture) + ",000", price, white, (float)(px + 0.2 * s), (float)(py + 1.12 * s));
        }

        return page;
    }

    /// <summary>The coarse and fine copies of a picture, as the shop's marks take them.</summary>
    public static (ColorPlanes Coarse, ColorPlanes Fine) Copies(Bitmap picture) =>
        (ColorPlanes.Of(picture, (int)Math.Round(picture.Width * (double)ShopPetFinder.CoarseHeight / picture.Height), ShopPetFinder.CoarseHeight),
         ColorPlanes.Of(picture, (int)Math.Round(picture.Width * (double)ShopPetFinder.FineHeight / picture.Height), ShopPetFinder.FineHeight));
}
