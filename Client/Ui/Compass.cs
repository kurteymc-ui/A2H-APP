namespace AionDPS.Ui;

/// <summary>
/// The points of the compass on the game's maps: north is up (the world's y runs down the map, <c>pixel = offset + scale * world</c>, no flip or rotation),
/// east to the right. The eight names in the meter's language (Map.Compass: "N,NE,E,SE,S,SW,W,NW").
/// </summary>
public static class Compass
{
    /// <summary>The eight names, clockwise from north.</summary>
    public static string[] Names()
    {
        string[] names = LocalizationManager.Instance["Map.Compass"].Split(',');
        return names.Length == 8 ? names : new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
    }

    /// <summary>The bearing from one place to another in world units: 0 north, 90 east (degrees, clockwise).</summary>
    public static double Bearing(double dx, double dy) => (Math.Atan2(dx, -dy) * 180 / Math.PI + 360) % 360;

    /// <summary>Which of the eight points a bearing is nearest to (0 north ... 7 north-west).</summary>
    public static int Point(double bearing) => (int)Math.Round(((bearing % 360) + 360) % 360 / 45.0) % 8;

    /// <summary>The name of the point of the compass from one place towards another (world units).</summary>
    public static string Towards(double dx, double dy) => Names()[Point(Bearing(dx, dy))];
}
