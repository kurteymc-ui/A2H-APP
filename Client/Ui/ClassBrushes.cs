using System.Windows.Media;

namespace AionDPS.Ui;

/// <summary>The translucent class colours behind a player's row; the same hues as the website (class pairs share a hue family - Cleric/Chanter yellow, Gladiator/Templar blue, Assassin/Ranger green, Sorcerer/Spiritmaster purple - in a lighter and a deeper shade)'s meter
/// (app.js CLASS_META), so a class reads the same colour in both.</summary>
internal static class ClassBrushes
{
    private static readonly Dictionary<string, Brush> Cache = new();

    private static readonly Dictionary<string, string> Colors = new(StringComparer.Ordinal)
    {
        ["Cleric"] = "#e8c560",
        ["Chanter"] = "#e39b4c",
        ["Templar"] = "#9db8e8",
        ["Gladiator"] = "#6b9fe8",
        ["Brawler"] = "#e2735c",
        ["Assassin"] = "#58c58e",
        ["Ranger"] = "#a2d373",
        ["Sorcerer"] = "#ad80f2",
        ["Spiritmaster"] = "#d48ce8",
    };

    private const string Unknown = "#9fb3c8";

    private static Color? _barOverride;
    private static Dictionary<string, Color> _classOverrides = new(StringComparer.Ordinal);

    /// <summary>The class names that have a colour, in the order the theme editor lists them.</summary>
    public static IReadOnlyList<string> Names { get; } = new[] { "Gladiator", "Templar", "Assassin", "Ranger", "Sorcerer", "Spiritmaster", "Cleric", "Chanter", "Brawler" };

    /// <summary>The user's own colours for single classes ("#RRGGBB" by class name); the one-colour-for-all setting wins over them.</summary>
    public static void SetClassOverrides(IReadOnlyDictionary<string, string>? colors)
    {
        var parsed = new Dictionary<string, Color>(StringComparer.Ordinal);
        if (colors is not null)
        {
            foreach (var (name, hex) in colors)
            {
                if (Colors.ContainsKey(name) && ThemeManager.TryParse(hex) is Color c)
                {
                    parsed[name] = c;
                }
            }
        }

        lock (Cache)
        {
            _classOverrides = parsed;
            Cache.Clear();
            SolidCache.Clear();
            EdgeCache.Clear();
            TextCache.Clear();
            GlowCache.Clear();
            LineCache.Clear();
        }
    }

    /// <summary>The colour in use for a class: the one-for-all bar colour, the class's own colour or the standard one.</summary>
    private static Color Effective(string key) =>
        _barOverride ?? (key.Length > 0 && _classOverrides.TryGetValue(key, out Color own) ? own : (Color)ColorConverter.ConvertFromString(key.Length > 0 ? Colors[key] : Unknown));

    /// <summary>One colour for every class (the user's own bar colour), or null for the class colours.</summary>
    public static void SetBarOverride(Color? color)
    {
        lock (Cache)
        {
            _barOverride = color;
            Cache.Clear();
            SolidCache.Clear();
            EdgeCache.Clear();
            TextCache.Clear();
            GlowCache.Clear();
            LineCache.Clear();
        }
    }

    private static readonly Dictionary<string, Brush> SolidCache = new();
    private static readonly Dictionary<string, Brush> EdgeCache = new();
    private static readonly Dictionary<string, Brush> TextCache = new();

    private static Brush Cached(Dictionary<string, Brush> cache, string? className, Func<Color, Color> make)
    {
        string key = className is not null && Colors.ContainsKey(className) ? className : "";
        lock (cache)
        {
            if (!cache.TryGetValue(key, out Brush? brush))
            {
                brush = new SolidColorBrush(make(Effective(key)));
                brush.Freeze();
                cache[key] = brush;
            }

            return brush;
        }
    }

    private static readonly Dictionary<string, Brush> GlowCache = new();
    private static readonly Dictionary<string, Brush> LineCache = new();

    private static Brush CachedGradient(Dictionary<string, Brush> cache, string? className, Func<Color, Brush> make)
    {
        string key = className is not null && Colors.ContainsKey(className) ? className : "";
        lock (cache)
        {
            if (!cache.TryGetValue(key, out Brush? brush))
            {
                brush = make(Effective(key));
                brush.Freeze();
                cache[key] = brush;
            }

            return brush;
        }
    }

    /// <summary>A player's bar: the class colour as a soft glow, stronger at the start (30 %) and fading along the bar (9 %).</summary>
    public static Brush Glow(string? className) => CachedGradient(GlowCache, className, c =>
        new LinearGradientBrush(Color.FromArgb(0x4D, c.R, c.G, c.B), Color.FromArgb(0x17, c.R, c.G, c.B), 0));

    /// <summary>The bright line along the bottom of a player's bar: from nothing to the full class colour at its end.</summary>
    public static Brush Line(string? className) => CachedGradient(LineCache, className, c =>
        new LinearGradientBrush(Color.FromArgb(0x00, c.R, c.G, c.B), Color.FromArgb(0xF0, c.R, c.G, c.B), 0));

    /// <summary>The thin edge of a player's bar in the in-game meter: the class colour, nearly solid.</summary>
    public static Brush Edge(string? className) => Cached(EdgeCache, className, c => Color.FromArgb(0xC8, c.R, c.G, c.B));

    /// <summary>A player's name in the in-game meter: the class colour, lifted towards white so the deep shades read on the dark panel.</summary>
    public static Brush Text(string? className) => Cached(TextCache, className, c => Color.FromRgb(Lift(c.R), Lift(c.G), Lift(c.B)));

    private static byte Lift(byte v) => (byte)(v + (255 - v) * 0.35);

    /// <summary>The class's own colour, whatever bar colour the user set (the theme editor's preview shows both).</summary>
    public static Color ClassColor(string? className) =>
        (Color)ColorConverter.ConvertFromString(className is not null && Colors.TryGetValue(className, out string? hex) ? hex : Unknown);

    /// <summary>The class colour without the translucency (a dot on the replay map).</summary>
    public static Brush Solid(string? className)
    {
        string key = className is not null && Colors.ContainsKey(className) ? className : "";
        lock (SolidCache)
        {
            if (!SolidCache.TryGetValue(key, out Brush? brush))
            {
                brush = new SolidColorBrush(Effective(key));
                brush.Freeze();
                SolidCache[key] = brush;
            }

            return brush;
        }
    }

    public static Brush For(string? className)
    {
        string key = className is not null && Colors.ContainsKey(className) ? className : "";
        lock (Cache)
        {
            if (!Cache.TryGetValue(key, out Brush? brush))
            {
                var color = Effective(key);
                brush = new SolidColorBrush(Color.FromArgb(0x52, color.R, color.G, color.B)); // 32 %: the bar fills the whole row, with an edge in the full colour
                brush.Freeze();
                Cache[key] = brush;
            }

            return brush;
        }
    }
}
