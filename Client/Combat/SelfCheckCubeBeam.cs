using AionDPS.Aion2;

namespace AionDPS.Combat;

/// <summary>
/// The cube beam's two halves (5 November 2026): the camera's direction read from a minimap's view cone (a drawn one: the edges two white lines from the
/// player's circle, the cone lit), and where a cube then lies on screen.
/// </summary>
public static class SelfCheckCubeBeam
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] cube beam (the camera from the minimap's view cone, the cube's place on screen):");
        bool ok = true;

        // a cone from 170 to 281 degrees (south-west, 111 wide: as on the owner's minimap picture of 5 November)
        var a = MinimapCone.Read(Minimap(170, 281, out int w, out int h), w, h, 150, 100, 137, 89);
        ok &= Check("a cone south-west is read: the middle at 225.5 degrees, 111 wide (within 1.5)",
            a is { } ra && Near(Deg(ra.Yaw), 225.5, 1.5) && Near(Deg(ra.Width), 111, 1.5), a is { } x ? $"{Deg(x.Yaw):0.0} / {Deg(x.Width):0.0}" : "none");
        // one across east (the edges at 330 and 80 degrees): the middle at 25
        var b = MinimapCone.Read(Minimap(330, 80, out w, out h), w, h, 150, 100, 137, 89);
        ok &= Check("a cone across east is read: the middle at 25 degrees, 110 wide", b is { } rb && Near(Deg(rb.Yaw), 25, 1.5) && Near(Deg(rb.Width), 110, 1.5),
            b is { } y ? $"{Deg(y.Yaw):0.0} / {Deg(y.Width):0.0}" : "none");
        // the place known a pixel or two off: still read
        var c = MinimapCone.Read(Minimap(170, 281, out w, out h), w, h, 152, 98, 137, 89);
        ok &= Check("read with the minimap's place two pixels off: within 2.5 degrees", c is { } rc && Near(Deg(rc.Yaw), 225.5, 2.5), c is { } z ? $"{Deg(z.Yaw):0.0}" : "none");
        // no cone (a menu over the minimap, another picture): nothing
        ok &= Check("no cone, no reading", MinimapCone.Read(Minimap(-1, -1, out w, out h), w, h, 150, 100, 137, 89) is null, "");

        // the camera looking north (90 degrees), 110 wide, a screen 2560 across
        double fov = 110 * Math.PI / 180, north = Math.PI / 2;
        double? ahead = MinimapCone.ScreenX(MinimapCone.Off(0, -3000, north), fov, 2560);
        ok &= Check("a cube straight ahead is in the middle", ahead is double m && Near(m, 1280, 1), $"{ahead}");
        double? right = MinimapCone.ScreenX(MinimapCone.Off(2000, -3000, north), fov, 2560);
        ok &= Check("a cube ahead and to the east is right of the middle", right is double r && r > 1400 && r < 2560, $"{right}");
        double? left = MinimapCone.ScreenX(MinimapCone.Off(-2000, -3000, north), fov, 2560);
        ok &= Check("and to the west, as far left", left is double l && right is double r2 && Near(l, 2560 - r2, 1), $"{left}");
        double behind = MinimapCone.Off(-500, 3000, north);
        ok &= Check("a cube behind the camera is off the screen, to the left when it lies south-west", MinimapCone.ScreenX(behind, fov, 2560) is null && behind > 0,
            $"{Deg(behind):0}");
        // the camera behind the player turns a near cube's angle towards the middle
        double fromPlayer = Math.Abs(MinimapCone.Off(2000, -2000, north)), fromCamera = Math.Abs(MinimapCone.Off(2000, -2000, north, 500));
        ok &= Check("from the camera behind the player a near cube is nearer the middle", fromCamera < fromPlayer, $"{Deg(fromPlayer):0.0} > {Deg(fromCamera):0.0}");
        return ok;
    }

    /// <summary>A minimap picture 300 x 200, the player at 150, 100: a map of browns, the player's light circle, and (unless an edge is -1) the cone between the two
    /// edges (degrees, counter-clockwise from east), lit, edged by two white lines.</summary>
    private static int[] Minimap(double from, double to, out int width, out int height)
    {
        width = 300;
        height = 200;
        var pixels = new int[width * height];
        double span = ((to - from) % 360 + 360) % 360;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // the ground: browns with some detail
                double v = 110 + (25 * Math.Sin(x * 0.11) * Math.Cos(y * 0.07)) + (12 * Math.Sin((x + y) * 0.31));
                double dx = x - 150, dy = 100 - y, r = Math.Sqrt((dx * dx) + (dy * dy));
                double angle = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
                bool inside = from >= 0 && ((angle - from) % 360 + 360) % 360 <= span;
                if (inside && r > 22)
                {
                    v += 35; // the cone is lit
                }

                int red = (int)Math.Clamp(v + 25, 0, 255), green = (int)Math.Clamp(v + 5, 0, 255), blue = (int)Math.Clamp(v - 30, 0, 255);
                if (r <= 22)
                {
                    red = green = blue = r > 19 ? 240 : 225; // the player's circle and its rim
                }

                pixels[(y * width) + x] = unchecked((int)0xFF000000) | (red << 16) | (green << 8) | blue;
            }
        }

        if (from >= 0)
        {
            foreach (double edge in new[] { from, to })
            {
                double t = edge * Math.PI / 180;
                for (double r = 22; r < 120; r += 0.25)
                {
                    for (int across = -1; across <= 0; across++)
                    {
                        int x = (int)Math.Round(150 + (r * Math.Cos(t)) - (across * Math.Sin(t))), y = (int)Math.Round(100 - (r * Math.Sin(t)) - (across * Math.Cos(t)));
                        if (x >= 0 && y >= 0 && x < width && y < height)
                        {
                            pixels[(y * width) + x] = unchecked((int)0xFFF4F1EA);
                        }
                    }
                }
            }
        }

        return pixels;
    }

    private static double Deg(double radians) => ((radians * 180 / Math.PI) % 360 + 360) % 360;

    private static bool Near(double a, double b, double within) => Math.Abs(a - b) <= within;

    private static bool Check(string what, bool passed, string got)
    {
        Console.WriteLine($"  -> {what}: {passed}{(got.Length > 0 ? " (" + got + ")" : "")}");
        return passed;
    }
}
