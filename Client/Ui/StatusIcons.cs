using System.Windows;
using System.Windows.Media;

namespace AionDPS.Ui;

/// <summary>
/// A small drawn picture for an effect the game's data has no icon for (Slow, Incapacitated Immunity, a monster's Attack, Shield Block Status ...): a dark
/// square with a sign for what the name says (an hourglass for slows, a shield for blocks and immunities, a star for stuns and roots, a drop for poisons, a
/// blade for attacks), edged red when the effect is harmful and green when it helps. Better than an empty box in the lists of effects.
/// </summary>
public static class StatusIcons
{
    private static readonly Dictionary<(string Sign, bool Harmful), ImageSource> Made = new();
    private static readonly object Gate = new();

    // the signs, drawn in a 24 x 24 box: (path, filled)
    private static readonly Dictionary<string, (string Path, bool Fill)> Signs = new()
    {
        ["slow"] = ("M7.5,5 L16.5,5 L12,12 L16.5,19 L7.5,19 L12,12 Z", false),
        ["immune"] = ("M12,4 L18.5,6.5 L18.5,12 C18.5,15.5 15.8,18.3 12,20 C8.2,18.3 5.5,15.5 5.5,12 L5.5,6.5 Z M9,12 L11.2,14.2 L15.2,9.8", false),
        ["shield"] = ("M12,4 L18.5,6.5 L18.5,12 C18.5,15.5 15.8,18.3 12,20 C8.2,18.3 5.5,15.5 5.5,12 L5.5,6.5 Z", false),
        ["stun"] = ("M12,4.5 L13.9,9.8 L19.5,9.9 L15,13.3 L16.6,18.7 L12,15.5 L7.4,18.7 L9,13.3 L4.5,9.9 L10.1,9.8 Z", true),
        ["poison"] = ("M12,4.5 C12,4.5 6.8,10.6 6.8,14.3 A5.2,5.2 0 0 0 17.2,14.3 C17.2,10.6 12,4.5 12,4.5 Z", true),
        ["blade"] = ("M6,18 L16.5,7.5 L18,6 M14.5,5.5 L18.5,9.5 M5,14.5 L9.5,19", false),
        ["balance"] = ("M12,4.5 L19.5,12 L12,19.5 L4.5,12 Z M12,8.5 L15.5,12 L12,15.5 L8.5,12 Z", false),
        ["up"] = ("M12,18.5 L12,6 M7.5,10.5 L12,6 L16.5,10.5", false),
        ["down"] = ("M12,5.5 L12,18 M7.5,13.5 L12,18 L16.5,13.5", false),
    };

    /// <summary>The picture for an effect named <paramref name="name"/>; <paramref name="harmful"/>: a debuff.</summary>
    public static ImageSource For(string? name, bool harmful)
    {
        string sign = SignOf(name ?? "", harmful);
        lock (Gate)
        {
            if (Made.TryGetValue((sign, harmful), out ImageSource? made))
            {
                return made;
            }

            var edge = harmful ? Color.FromRgb(0xE0, 0x64, 0x64) : Color.FromRgb(0x5F, 0xBF, 0x7A);
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x23, 0x27, 0x30)), new Pen(new SolidColorBrush(edge), 1.4),
                new RectangleGeometry(new Rect(0.7, 0.7, 22.6, 22.6), 4, 4)));
            var (path, fill) = Signs[sign];
            var ink = new SolidColorBrush(Color.FromRgb(0xEA, 0xDF, 0xC6));
            group.Children.Add(fill
                ? new GeometryDrawing(ink, null, Geometry.Parse(path))
                : new GeometryDrawing(null, new Pen(ink, 1.7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, Geometry.Parse(path)));
            var image = new DrawingImage(group);
            image.Freeze();
            Made[(sign, harmful)] = image;
            return image;
        }
    }

    private static string SignOf(string name, bool harmful)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("immun"))
        {
            return "immune";
        }

        if (n.Contains("slow") || n.Contains("delay"))
        {
            return "slow";
        }

        if (n.Contains("stun") || n.Contains("incapac") || n.Contains("knock") || n.Contains("bind") || n.Contains("root") || n.Contains("snare") || n.Contains("silence") || n.Contains("sleep") || n.Contains("fear"))
        {
            return "stun";
        }

        if (n.Contains("poison") || n.Contains("venom") || n.Contains("bleed"))
        {
            return "poison";
        }

        if (n.Contains("shield") || n.Contains("block") || n.Contains("guard") || n.Contains("armor") || n.Contains("barrier"))
        {
            return "shield";
        }

        if (n.Contains("attack") || n.Contains("strike") || n.Contains("blade") || n.Contains("slash"))
        {
            return "blade";
        }

        if (n.Contains("balance"))
        {
            return "balance";
        }

        return harmful ? "down" : "up";
    }
}
