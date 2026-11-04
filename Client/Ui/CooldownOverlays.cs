using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ShapePath = System.Windows.Shapes.Path;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>How a skill's cooldown is drawn (<see cref="CooldownBar"/>, <see cref="CooldownTile"/>): shown again with the cooldown of the moment.</summary>
public interface ICooldownLook
{
    void Show(CooldownView view);
}

/// <summary>The colours, the time text and the pictures the cooldowns share: light on dark, green when ready, amber in the last ten seconds.</summary>
public static class CooldownArt
{
    public const string Bars = "Bars", Tiles = "Tiles";

    /// <summary>A bar's height and a tile's side at size 1.</summary>
    public const double BarHeight = 22, TileSize = 40;

    public static readonly Brush Text = Frozen(Colors.White);
    public static readonly Brush Soon = Frozen(Color.FromRgb(0xFF, 0xC8, 0x57));
    public static readonly Brush ReadyText = Frozen(Color.FromRgb(0xD8, 0xFF, 0xD0));
    public static readonly Brush ReadyBack = Frozen(Color.FromArgb(0xC0, 0x2C, 0x6E, 0x36));
    public static readonly Brush CoolBack = Frozen(Color.FromArgb(0xB0, 0x0C, 0x0E, 0x12));
    public static readonly Brush CoolFill = Frozen(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
    public static readonly Brush EffectBack = Frozen(Color.FromArgb(0xB0, 0x0C, 0x0E, 0x12));
    public static readonly Brush EffectFill = Frozen(Color.FromArgb(0x55, 0x3E, 0x7C, 0xC9));
    public static readonly Brush ReadyEdge = Frozen(Color.FromRgb(0xF5, 0xC5, 0x42));
    public static readonly Brush CoolEdge = Frozen(Color.FromRgb(0x4C, 0xC9, 0x6A));
    public static readonly Brush Shade = Frozen(Color.FromArgb(0x5A, 0x00, 0x00, 0x00));
    public static readonly Brush Sweep = Frozen(Color.FromArgb(0x8C, 0x00, 0x00, 0x00));
    public static readonly Brush PictureEdge = Frozen(Color.FromArgb(0xB0, 0x00, 0x00, 0x00));

    /// <summary>Keeps light text readable on any picture or background.</summary>
    public static readonly Effect Outline = FrozenShadow(Colors.Black, 3, 1);

    /// <summary>The glow around a ready tile.</summary>
    public static readonly Effect ReadyGlow = FrozenShadow(Color.FromRgb(0xF5, 0xC5, 0x42), 9, 0.9);

    /// <summary>The time left as the cooldowns show it: tenths under ten seconds (1.6), whole seconds above (18), minutes from a minute on (1:35).</summary>
    public static string Time(double seconds)
    {
        if (seconds < 10)
        {
            return (Math.Ceiling(seconds * 10 - 1e-9) / 10).ToString("0.0", CultureInfo.CurrentCulture);
        }

        int whole = (int)Math.Ceiling(seconds - 1e-9);
        return whole >= 60 ? $"{whole / 60}:{whole % 60:00}" : whole.ToString(CultureInfo.CurrentCulture);
    }

    private static readonly Dictionary<string, BitmapImage?> Pictures = new();

    /// <summary>A skill's or an effect's picture (the game's), else a drawn one by its name.</summary>
    public static ImageSource IconOf(int id)
    {
        string? path = Aion2SkillIcons.PathFor(id) ?? Aion2Artwork.EffectIconPath(id);
        if (path is not null)
        {
            if (!Pictures.TryGetValue(path, out BitmapImage? image))
            {
                image = null;
                if (System.IO.File.Exists(path))
                {
                    image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.UriSource = new Uri(path, UriKind.Absolute);
                    image.DecodePixelWidth = 80;
                    image.EndInit();
                    image.Freeze();
                }

                Pictures[path] = image;
            }

            if (image is not null)
            {
                return image;
            }
        }

        return StatusIcons.For(Aion2SkillNames.DisplayOf(id), Aion2Artwork.IsDebuffEffect(id));
    }

    /// <summary>A picture with rounded corners and a thin dark edge.</summary>
    public static FrameworkElement Picture(ImageSource icon, double size)
    {
        double radius = Math.Max(2, size / 10);
        var grid = new Grid { Width = size, Height = size, Clip = new RectangleGeometry(new Rect(0, 0, size, size), radius, radius) };
        grid.Children.Add(new Image { Source = icon, Width = size, Height = size, Stretch = Stretch.UniformToFill });
        grid.Children.Add(new Border { BorderBrush = PictureEdge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(radius) });
        return grid;
    }

    /// <summary>The part of a square still to go: from the angle reached so far clockwise back to the top (a circle larger than the square, cut by it).</summary>
    public static Geometry SweepOf(double size, double fraction)
    {
        if (fraction >= 0.999)
        {
            return new RectangleGeometry(new Rect(0, 0, size, size));
        }

        double c = size / 2, r = size;
        double angle = (1 - fraction) * 2 * Math.PI;
        Point At(double a) => new(c + r * Math.Sin(a), c - r * Math.Cos(a));
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(c, c), true, true);
            ctx.LineTo(At(angle), false, false);
            ctx.ArcTo(At(0), new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, false, false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Effect FrozenShadow(Color color, double blur, double opacity)
    {
        var shadow = new DropShadowEffect { Color = color, ShadowDepth = 0, BlurRadius = blur, Opacity = opacity };
        shadow.Freeze();
        return shadow;
    }
}

/// <summary>
/// A skill's cooldown as a bar: its picture, its name and the time left, the part still to go filled in; green with "Ready" once it is back (amber the last
/// ten seconds, tenths under ten).
/// </summary>
public sealed class CooldownBar : Border, ICooldownLook
{
    private readonly Border _fill;
    private readonly TextBlock _time;
    private readonly double _width;
    private bool? _running;
    private string _shown = "";

    public CooldownBar(int skillId, double width)
    {
        _width = width;
        Width = width;
        Height = CooldownArt.BarHeight;
        CornerRadius = new CornerRadius(3);
        Background = CooldownArt.CoolBack;
        ClipToBounds = true;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _fill = new Border { HorizontalAlignment = HorizontalAlignment.Left, Background = CooldownArt.CoolFill, Width = 0, CornerRadius = new CornerRadius(3) };
        Grid.SetColumnSpan(_fill, 3);
        grid.Children.Add(_fill);
        FrameworkElement picture = CooldownArt.Picture(CooldownArt.IconOf(skillId), CooldownArt.BarHeight - 2);
        picture.Margin = new Thickness(1);
        grid.Children.Add(picture);
        var name = new TextBlock
        {
            Text = Aion2SkillNames.DisplayOf(skillId), Foreground = CooldownArt.Text, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Effect = CooldownArt.Outline,
        };
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);
        _time = new TextBlock { FontSize = 11.5, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center, Effect = CooldownArt.Outline };
        Grid.SetColumn(_time, 2);
        grid.Children.Add(_time);
        Child = grid;
        ToolTip = null;
    }

    public void Show(CooldownView view)
    {
        if (_running != view.Running)
        {
            _running = view.Running;
            Background = view.Running ? CooldownArt.CoolBack : CooldownArt.ReadyBack;
            if (!view.Running)
            {
                _fill.Width = 0;
                _shown = LocalizationManager.Instance["Assist.Ready"];
                _time.Text = _shown;
                _time.Foreground = CooldownArt.ReadyText;
            }
        }

        if (!view.Running)
        {
            return;
        }

        _fill.Width = Math.Round(_width * view.Fraction);
        string text = CooldownArt.Time(view.Seconds);
        if (text != _shown)
        {
            _shown = text;
            _time.Text = text;
            _time.Foreground = view.Seconds < 10 ? CooldownArt.Soon : CooldownArt.Text;
        }
    }
}

/// <summary>
/// A skill's cooldown as a tile: its picture with a gold edge and glow while ready; cooling down darker, the part still to go swept off clockwise, a green
/// edge and the time left in the middle (amber the last ten seconds).
/// </summary>
public sealed class CooldownTile : Grid, ICooldownLook
{
    private readonly double _size;
    private readonly Rectangle _shade;
    private readonly ShapePath _sweep;
    private readonly TextBlock _time;
    private readonly Border _edge;
    private bool? _running;
    private double _fraction = -1;
    private string _shown = "";

    public CooldownTile(int skillId, double size = CooldownArt.TileSize)
    {
        _size = size;
        Width = Height = size;
        double radius = Math.Max(3, size / 10);
        var inner = new Grid { Clip = new RectangleGeometry(new Rect(0, 0, size, size), radius, radius) };
        inner.Children.Add(new Image { Source = CooldownArt.IconOf(skillId), Width = size, Height = size, Stretch = Stretch.UniformToFill });
        _shade = new Rectangle { Fill = CooldownArt.Shade, Visibility = Visibility.Collapsed };
        _sweep = new ShapePath { Fill = CooldownArt.Sweep, Visibility = Visibility.Collapsed };
        inner.Children.Add(_shade);
        inner.Children.Add(_sweep);
        Children.Add(inner);
        _edge = new Border { BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(radius), BorderBrush = CooldownArt.ReadyEdge };
        Children.Add(_edge);
        _time = new TextBlock
        {
            Foreground = CooldownArt.Text, FontWeight = FontWeights.Bold, FontSize = Math.Round(size * 0.36), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Effect = CooldownArt.Outline, Visibility = Visibility.Collapsed,
        };
        Children.Add(_time);
        Margin = new Thickness(1);
    }

    public void Show(CooldownView view)
    {
        if (_running != view.Running)
        {
            _running = view.Running;
            _edge.BorderBrush = view.Running ? CooldownArt.CoolEdge : CooldownArt.ReadyEdge;
            _edge.Effect = view.Running ? null : CooldownArt.ReadyGlow;
            _shade.Visibility = _time.Visibility = view.Running ? Visibility.Visible : Visibility.Collapsed;
            if (!view.Running)
            {
                _sweep.Visibility = Visibility.Collapsed;
                _fraction = -1;
                _shown = "";
            }
        }

        if (!view.Running)
        {
            return;
        }

        if (Math.Abs(view.Fraction - _fraction) > 0.002)
        {
            _fraction = view.Fraction;
            _sweep.Data = CooldownArt.SweepOf(_size, _fraction);
            _sweep.Visibility = _fraction > 0.001 ? Visibility.Visible : Visibility.Collapsed;
        }

        string text = CooldownArt.Time(view.Seconds);
        if (text != _shown)
        {
            _shown = text;
            _time.Text = text;
            _time.FontSize = Math.Round(_size * (text.Length >= 4 ? 0.28 : 0.36));
            _time.Foreground = view.Seconds < 10 ? CooldownArt.Soon : CooldownArt.Text;
        }
    }
}

/// <summary>
/// One skill's cooldown on the screen (a bar or a tile): placed on its own, anywhere; dragged close to another skill's it snaps flush against it and hangs on
/// it, so a column of bars or a row of tiles moves as one by its first (<see cref="OverlayDocking"/>, the group "cooldowns"). Locked it lets every click
/// through; the size is the same for all of them (the assist window).
/// </summary>
public sealed class CooldownSkillWindow : MovableOverlay
{
    public const string Group = "cooldowns";

    private readonly Grid _host = new();
    private ICooldownLook? _look;
    private string _style = "";
    private double _width;

    public int SkillId { get; }

    /// <summary>The window's title (the key of its docking): one per skill.</summary>
    public static string TitleOf(int skillId) => "Aion DPS Cooldown " + skillId.ToString(CultureInfo.InvariantCulture);

    public static bool IsCooldownTitle(string title) => title.StartsWith("Aion DPS Cooldown ", StringComparison.Ordinal);

    public CooldownSkillWindow(int skillId, double defaultLeft, double defaultTop)
        : base(TitleOf(skillId), new Grid(), () => Load(skillId), (left, top, _) => Save(skillId, left, top), defaultLeft, defaultTop,
            () => (null, null), (_, _) => { }, OverlayEdgeMode.None)
    {
        SkillId = skillId;
        Flush();
        Body = _host; // not a Border: the overlays' opacity setting does not touch the bar's own colours
        OverlayDocking.JoinGroup(this, Group);
    }

    /// <summary>Whether the skill has a place of its own in the settings (otherwise it is put after the others).</summary>
    public static bool HasPlace(MeterSettings s, int skillId) => s.AssistCooldownPlaces.TryGetValue(skillId, out double[]? p) && p.Length == 2;

    /// <summary>Builds the bar or the tile (again when the look or the bar's width changed).</summary>
    public void Use(string style, double barWidth)
    {
        if (style == _style && (style != CooldownArt.Bars || Math.Abs(barWidth - _width) < 0.5))
        {
            return;
        }

        _style = style;
        _width = barWidth;
        _host.Children.Clear();
        UIElement element = style == CooldownArt.Tiles ? new CooldownTile(SkillId) : new CooldownBar(SkillId, barWidth);
        _look = (ICooldownLook)element;
        _host.Children.Add(element);
    }

    public void Show(CooldownView view) => _look?.Show(view);

    private static (double?, double?, double) Load(int skillId)
    {
        var s = MeterSettings.Load();
        return s.AssistCooldownPlaces.TryGetValue(skillId, out double[]? p) && p.Length == 2 ? (p[0], p[1], s.AssistCooldownScale) : (null, null, s.AssistCooldownScale);
    }

    private static void Save(int skillId, double left, double top)
    {
        if (!double.IsFinite(left) || !double.IsFinite(top))
        {
            return;
        }

        var s = MeterSettings.Load();
        s.AssistCooldownPlaces[skillId] = new[] { Math.Round(left, 1), Math.Round(top, 1) };
        s.Save();
    }
}

/// <summary>Says out loud when a skill comes back ("Taunt ready"), for the skills ticked for it, with the voice of the voice settings; only while the game is in front.</summary>
public sealed class CooldownVoice
{
    private readonly CastSpeech _speech = new();
    private readonly Dictionary<int, bool> _last = new();

    /// <summary>Looks at the skills' cooldowns of the moment and says the voiced ones that are back (while the game is in front).</summary>
    public void Tick(IEnumerable<int> skills, MeterSettings settings, DateTime now, bool inGame)
    {
        List<int> back = Due(skills, settings.AssistVoiceSkills.ToHashSet(), now, inGame);
        if (back.Count > 0)
        {
            Say(back.Select(Aion2SkillNames.DisplayOf).ToList(), settings);
        }
    }

    /// <summary>The voiced skills whose cooldown was running at the last look and is over now (a reset by the game too); every skill's state is noted.</summary>
    public List<int> Due(IEnumerable<int> skills, IReadOnlySet<int> voiced, DateTime now, bool inGame)
    {
        var back = new List<int>();
        var seen = new HashSet<int>();
        foreach (int id in skills)
        {
            seen.Add(id);
            CooldownView view = Aion2Cooldowns.View(id, now);
            if (_last.TryGetValue(id, out bool wasRunning) && wasRunning && !view.Running && voiced.Contains(id) && inGame)
            {
                back.Add(id);
            }

            _last[id] = view.Running;
        }

        foreach (int gone in _last.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            _last.Remove(gone);
        }

        return back;
    }

    /// <summary>Says that these skills are back (also the window's test button).</summary>
    public void Say(IReadOnlyList<string> skills, MeterSettings settings)
    {
        string words = CastSpeech.WordsLanguage(settings);
        string sentence = string.Join(". ", skills.Select(name => string.Format(CultureInfo.CurrentCulture, LocalizationManager.Instance.In("Assist.VoiceReady", words), name)));
        _speech.Speak(sentence, settings.SpeechVoiceId, Math.Clamp(settings.AssistVoiceVolume, 0, 100) / 100.0, AudioOutput.DeviceId);
    }
}
