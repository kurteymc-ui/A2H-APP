namespace AionDPS.Aion2;

/// <summary>
/// Which way the camera looks, from the game's minimap (5 November 2026, for the cube beam: "a beacon of light above the cube for easier spotting"). The
/// minimap keeps north up and lights a cone from the player's spot towards where the camera looks, edged by two thin white lines (the owner's minimap
/// pictures: a cone some 110 degrees wide, its edges sharp from the player's circle out to the frame). The two edges are found along rays around the
/// player's spot: an edge is a ray most of whose pixels are white (bright and grey), with darker rays a few degrees to each side; the cone is the
/// shorter way from one edge to the other and must be lit (brighter than the same arc on the far side). Nothing is read from the game but the picture
/// of its window the minimap reader takes anyway.
/// </summary>
public static class MinimapCone
{
    /// <summary>The camera's direction on the minimap (radians: 0 east, counter-clockwise, north at half pi), the cone's opening (radians) and how
    /// clearly its edges stood out (0 to 2).</summary>
    public sealed record Reading(double Yaw, double Width, double Score);

    private const int Bins = 720; // half a degree each

    /// <summary>
    /// The cone around the player's spot (<paramref name="cx"/>, <paramref name="cy"/>, pixels of the picture <paramref name="argb"/>, a row after row of
    /// 0xAARRGGBB) on a minimap reaching <paramref name="halfW"/> across and <paramref name="halfH"/> down from there. Null when no cone is seen (a menu over
    /// it, another picture, no minimap). The spot a pixel or two off turns the reading by about a degree.
    /// </summary>
    public static Reading? Read(int[] argb, int width, int height, double cx, double cy, double halfW, double halfH)
    {
        double r0 = halfH * 0.40, r1 = Math.Min(halfW, halfH) * 0.95;
        return r1 - r0 < 12 || argb.Length < width * height ? null : ReadAt(argb, width, height, cx, cy, r0, r1);
    }

    private static Reading? ReadAt(int[] argb, int w, int h, double cx, double cy, double r0, double r1)
    {
        var white = new double[Bins];
        var lum = new double[Bins];
        for (int b = 0; b < Bins; b++)
        {
            double a = b * 2 * Math.PI / Bins, cos = Math.Cos(a), sin = Math.Sin(a);
            int n = 0, whites = 0;
            double sum = 0;
            for (double r = r0; r <= r1; r += 2)
            {
                int x = (int)Math.Round(cx + (r * cos)), y = (int)Math.Round(cy - (r * sin));
                if (x < 0 || y < 0 || x >= w || y >= h)
                {
                    continue;
                }

                int p = argb[(y * w) + x];
                int red = (p >> 16) & 255, green = (p >> 8) & 255, blue = p & 255;
                int max = Math.Max(red, Math.Max(green, blue)), min = Math.Min(red, Math.Min(green, blue));
                double l = (red + green + blue) / 3.0;
                n++;
                sum += l;
                if (l >= 200 && max - min <= 60)
                {
                    whites++;
                }
            }

            white[b] = n >= 6 ? (double)whites / n : 0;
            lum[b] = n > 0 ? sum / n : 0;
        }

        // an edge is one to three pixels wide: a bin and its neighbours
        var s = new double[Bins];
        for (int b = 0; b < Bins; b++)
        {
            s[b] = Math.Max(white[b], Math.Max(white[(b + 1) % Bins], white[(b + Bins - 1) % Bins]));
        }

        int b1 = ArgMax(s, _ => true);
        if (s[b1] < 0.45 || !Sharp(s, b1))
        {
            return null;
        }

        // the other edge: 55 to 150 degrees away
        int b2 = ArgMax(s, b => Gap(b, b1) is >= 110 and <= 300 && Sharp(s, b));
        if (b2 < 0 || s[b2] < 0.30)
        {
            return null;
        }

        double a1 = Refine(white, b1), a2 = Refine(white, b2);
        double span = Wrap(a2 - a1);
        double yaw = Normal(a1 + (span / 2)), opening = Math.Abs(span);
        double half = (opening / 2) - (4 * Math.PI / 180);
        if (half <= 0 || MeanAround(lum, yaw, half) < MeanAround(lum, yaw + Math.PI, half) + 2)
        {
            return null; // not lit inside: two lines of something else
        }

        return new Reading(yaw, opening, s[b1] + s[b2]);
    }

    /// <summary>Where a cube (or anything) lies from the player as seen on screen: <paramref name="dx"/>, <paramref name="dy"/> world units east and south of the
    /// player (the game's y grows southwards), the camera's <paramref name="yaw"/> and opening <paramref name="fov"/> (radians, from <see cref="Read"/>), the
    /// camera <paramref name="behind"/> world units behind the player. Returns the angle off the middle of the screen (radians, positive to the left).</summary>
    public static double Off(double dx, double dy, double yaw, double behind = 0)
    {
        // on the minimap (and in the angle of the yaw) north is up: a world y southwards is a minimap y downwards
        double vx = dx + (behind * Math.Cos(yaw)), vy = -dy + (behind * Math.Sin(yaw));
        return Wrap(Math.Atan2(vy, vx) - yaw);
    }

    /// <summary>Where on a screen <paramref name="screenWidth"/> wide an angle off the middle (<see cref="Off"/>) lies with a camera opening
    /// <paramref name="fov"/> wide; null when it is outside the view (then it is to the left when the angle is positive).</summary>
    public static double? ScreenX(double off, double fov, double screenWidth)
    {
        double half = fov / 2;
        if (Math.Abs(off) >= half || half <= 0.05)
        {
            return null;
        }

        return (screenWidth / 2) - ((screenWidth / 2) * Math.Tan(off) / Math.Tan(half));
    }

    private static bool Sharp(double[] s, int b)
    {
        // darker 4 to 8 degrees to both sides than on the edge itself (a lit field or a white patch is not an edge)
        double side = 0;
        int n = 0;
        for (int d = 8; d <= 16; d++)
        {
            side += s[(b + d) % Bins] + s[(b - d + Bins) % Bins];
            n += 2;
        }

        return s[b] - (side / n) >= 0.25;
    }

    private static int ArgMax(double[] s, Func<int, bool> allowed)
    {
        int best = -1;
        for (int b = 0; b < Bins; b++)
        {
            if (allowed(b) && (best < 0 || s[b] > s[best]))
            {
                best = b;
            }
        }

        return best;
    }

    private static int Gap(int a, int b)
    {
        int d = Math.Abs(a - b) % Bins;
        return Math.Min(d, Bins - d);
    }

    /// <summary>The edge's angle to a fraction of a bin: the middle of the white around it.</summary>
    private static double Refine(double[] white, int b)
    {
        double sum = 0, weight = 0;
        for (int d = -3; d <= 3; d++)
        {
            double v = white[(b + d + Bins) % Bins];
            sum += v * d;
            weight += v;
        }

        double offset = weight > 0 ? sum / weight : 0;
        return Normal((b + offset) * 2 * Math.PI / Bins);
    }

    private static double MeanAround(double[] lum, double angle, double half)
    {
        int centre = (int)Math.Round(Normal(angle) * Bins / (2 * Math.PI)) % Bins, reach = (int)(half * Bins / (2 * Math.PI));
        double sum = 0;
        int n = 0;
        for (int d = -reach; d <= reach; d++)
        {
            sum += lum[(centre + d + Bins) % Bins];
            n++;
        }

        return n > 0 ? sum / n : 0;
    }

    /// <summary>An angle in (-pi, pi].</summary>
    public static double Wrap(double a)
    {
        a = Math.IEEERemainder(a, 2 * Math.PI);
        return a <= -Math.PI ? a + (2 * Math.PI) : a;
    }

    private static double Normal(double a)
    {
        a %= 2 * Math.PI;
        return a < 0 ? a + (2 * Math.PI) : a;
    }
}
