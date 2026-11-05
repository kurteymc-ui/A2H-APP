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
    public static readonly Brush Shade = Frozen(Color.FromArgb(0x5A, 0x00, 0x00, 0x00));
    public static readonly Brush Sweep = Frozen(Color.FromArgb(0x8C, 0x00, 0x00, 0x00));
    public static readonly Brush PictureEdge = Frozen(Color.FromArgb(0xB0, 0x00, 0x00, 0x00));

    /// <summary>Keeps light text readable on any picture or background.</summary>
    public static readonly Effect Outline = FrozenShadow(Colors.Black, 3, 1);

    /// <summary>
    /// The time left as the cooldowns show it, always rounded up (it reads 0.1 in the last tenth, never 0 while running): tenths under ten seconds (1.6),
    /// whole seconds above (18), minutes from a minute on (1:35). Just under ten seconds reads 10, not 10.0 (11, 10, 9.9, 9.8 ... - no longer text between).
    /// </summary>
    public static string Time(double seconds)
    {
        if (seconds < 10)
        {
            double tenths = Math.Ceiling(seconds * 10 - 1e-9) / 10;
            if (tenths < 10)
            {
                return tenths.ToString("0.0", CultureInfo.CurrentCulture);
            }
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
/// ten seconds, tenths under ten). The fill is cut off where the time stands, to a fraction of a unit (shown every frame while it runs, so it glides).
/// </summary>
public sealed class CooldownBar : Border, ICooldownLook
{
    private readonly Border _fill;
    private readonly RectangleGeometry _fillClip = new(new Rect(0, 0, 0, CooldownArt.BarHeight), 3, 3);
    private readonly TextBlock _time;
    private readonly double _width;
    private bool? _running;
    private string _shown = "";
    private double _fillShown = -1;

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
        _fill = new Border { HorizontalAlignment = HorizontalAlignment.Left, Background = CooldownArt.CoolFill, Width = width, Clip = _fillClip };
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
                FillTo(0);
                _shown = LocalizationManager.Instance["Assist.Ready"];
                _time.Text = _shown;
                _time.Foreground = CooldownArt.ReadyText;
            }
        }

        if (!view.Running)
        {
            return;
        }

        FillTo(_width * view.Fraction);
        string text = CooldownArt.Time(view.Seconds);
        if (text != _shown)
        {
            _shown = text;
            _time.Text = text;
            _time.Foreground = view.Seconds < 10 ? CooldownArt.Soon : CooldownArt.Text;
        }
    }

    /// <summary>The fill up to <paramref name="width"/> units (changed only when it moved a twentieth of one).</summary>
    private void FillTo(double width)
    {
        if (Math.Abs(width - _fillShown) >= 0.05)
        {
            _fillShown = width;
            _fillClip.Rect = new Rect(0, 0, Math.Max(0, width), CooldownArt.BarHeight);
        }
    }
}

/// <summary>
/// A skill's cooldown as a square: its picture, clean while ready; cooling down darker, the part still to go swept off clockwise and the time left in the
/// middle (amber the last ten seconds). A thin dark edge only - no coloured frame.
/// </summary>
public sealed class CooldownTile : Grid, ICooldownLook
{
    private readonly double _size;
    private readonly Rectangle _shade;
    private readonly ShapePath _sweep;
    private readonly TextBlock _time;
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
        Children.Add(new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(radius), BorderBrush = CooldownArt.PictureEdge });
        _time = new TextBlock
        {
            Foreground = CooldownArt.Text, FontWeight = FontWeights.Bold, FontSize = Math.Round(size * 0.36), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Effect = CooldownArt.Outline, Visibility = Visibility.Collapsed,
        };
        Children.Add(_time);
    }

    public void Show(CooldownView view)
    {
        if (_running != view.Running)
        {
            _running = view.Running;
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

        if (Math.Abs(view.Fraction - _fraction) > 0.001)
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
/// One skill's cooldown on the screen (a bar or a square), placed on its own anywhere. Locked it lets every click through. Unlocked (placing them) it is
/// dragged with the mouse - alone, flush against the others when it comes close to one, or all of them together (<see cref="Together"/>) - and only a thin
/// light outline under the mouse shows it can be taken. Each look keeps its own places; the size is the same for all of them (the assist window).
/// </summary>
public sealed class CooldownSkillWindow : MovableOverlay
{
    /// <summary>How close (screen units) an edge comes to another skill's before it is pulled flush against it.</summary>
    private const double Reach = 10;

    private static readonly Brush HoverEdge = FrozenBrush(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF));

    /// <summary>Every skill's window that is open: what one dragged snaps to, and what moves along with it.</summary>
    private static readonly HashSet<CooldownSkillWindow> Live = new();

    private readonly Grid _host = new();
    private readonly Border _hover = new() { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private ICooldownLook? _look;
    private string _style = "";
    private double _width;

    // a drag: where the cursor took hold (screen units), where the window was, and the others that move along
    private bool _dragging;
    private Point _grab;
    private double _fromLeft, _fromTop;
    private List<(CooldownSkillWindow Window, double Left, double Top)>? _carried;

    public int SkillId { get; }

    /// <summary>Placing: one dragged moves all of them (their arrangement kept).</summary>
    public static bool Together { get; set; }

    /// <summary>Raised when a drag let go: the windows that moved (their places are saved already).</summary>
    public static event Action? Moved;

    /// <summary>The window's title: one per skill.</summary>
    public static string TitleOf(int skillId) => "Aion DPS Cooldown " + skillId.ToString(CultureInfo.InvariantCulture);

    public static bool IsCooldownTitle(string? title) => title is not null && title.StartsWith("Aion DPS Cooldown ", StringComparison.Ordinal);

    public CooldownSkillWindow(int skillId, double defaultLeft, double defaultTop)
        : base(TitleOf(skillId), new Grid(), () => Load(skillId), (left, top, _) => Save(new[] { (skillId, left, top) }), defaultLeft, defaultTop,
            () => (null, null), (_, _) => { }, OverlayEdgeMode.None)
    {
        SkillId = skillId;
        UnlockedBorder = Brushes.Transparent; // no frame around it: the bar or the square is all there is (4 November 2026: the orange frames were in the way)
        UnlockedBackground = Brushes.Transparent;
        NeverActivate = true; // taken and dragged without the game losing the keyboard (or a full-screen game going to the task bar)
        OverlayDocking.Detach(this); // placed by its own drag: never docked to the other overlays, nor they to it
        Flush();
        _hover.BorderBrush = HoverEdge;
        _host.Children.Add(_hover);
        Body = _host;
        ApplyLocked(IsLocked);
        Live.Add(this);
        Closed += (_, _) => Live.Remove(this);
    }

    /// <summary>Whether the skill has a place of its own for this look in the settings (otherwise it takes its slot in the column or the row).</summary>
    public static bool HasPlace(MeterSettings s, int skillId, string style) => PlacesOf(s, style).TryGetValue(skillId, out double[]? p) && p.Length == 2;

    /// <summary>The places of a look: bars and squares are arranged apart.</summary>
    public static Dictionary<int, double[]> PlacesOf(MeterSettings s, string style) => style == CooldownArt.Tiles ? s.AssistCooldownTilePlaces : s.AssistCooldownPlaces;

    /// <summary>
    /// Places for another size: the arrangement kept, its top left corner where it is, the distances from it <paramref name="sx"/> and <paramref name="sy"/>
    /// times as long (two squares side by side stay side by side, neither apart nor overlapping).
    /// </summary>
    public static void Rescale(Dictionary<int, double[]> places, double sx, double sy)
    {
        var valid = places.Where(kv => kv.Value is { Length: 2 } p && double.IsFinite(p[0]) && double.IsFinite(p[1])).ToList();
        if (valid.Count == 0 || !double.IsFinite(sx) || !double.IsFinite(sy) || sx <= 0 || sy <= 0)
        {
            return;
        }

        double x0 = valid.Min(kv => kv.Value[0]), y0 = valid.Min(kv => kv.Value[1]);
        foreach ((int id, double[] p) in valid)
        {
            places[id] = new[] { Math.Round(x0 + (p[0] - x0) * sx, 1), Math.Round(y0 + (p[1] - y0) * sy, 1) };
        }
    }

    /// <summary>Builds the bar or the square (again when the look or the bar's width changed).</summary>
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
        _host.Children.Add(_hover);
    }

    public void Show(CooldownView view)
    {
        if (IsLocked && _hover.Visibility == Visibility.Visible)
        {
            // locked again while the mouse was on it (Done): it lets the clicks through now and never hears the mouse leave
            _hover.Visibility = Visibility.Collapsed;
            Cursor = null;
        }

        _look?.Show(view);
    }

    /// <summary>Puts the window somewhere (not saved).</summary>
    public void MoveTo(double left, double top)
    {
        if (Math.Abs(Left - left) > 0.4)
        {
            Left = left;
        }

        if (Math.Abs(Top - top) > 0.4)
        {
            Top = top;
        }
    }

    // ---------- dragging (no window message loop of its own: the windows move as the cursor does, and snap here) ----------

    protected override void OnMouseEnter(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        _hover.Visibility = IsLocked ? Visibility.Collapsed : Visibility.Visible;
        Cursor = IsLocked ? null : System.Windows.Input.Cursors.SizeAll;
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (!_dragging)
        {
            _hover.Visibility = Visibility.Collapsed;
        }
    }

    protected override void OnPreviewMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        if (IsLocked)
        {
            base.OnPreviewMouseLeftButtonDown(e);
            return;
        }

        e.Handled = true; // not the overlays' own drag: this one snaps to the other skills and can carry them
        if (CursorOnScreen() is not Point at)
        {
            return;
        }

        _dragging = true;
        _grab = at;
        _fromLeft = Left;
        _fromTop = Top;
        // together: all the others come along, also those not shown at the moment (unticked, or another specialization's), so the arrangement stays whole
        _carried = Together ? Live.Where(w => w != this).Select(w => (w, w.Left, w.Top)).ToList() : null;
        CaptureMouse();
    }

    protected override void OnPreviewMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (!_dragging || CursorOnScreen() is not Point at)
        {
            return;
        }

        double left = _fromLeft + at.X - _grab.X, top = _fromTop + at.Y - _grab.Y;
        if (_carried is null)
        {
            (left, top) = Snap(left, top);
        }
        else
        {
            foreach ((CooldownSkillWindow w, double l, double t) in _carried)
            {
                w.MoveTo(l + left - _fromLeft, t + top - _fromTop);
            }
        }

        MoveTo(left, top);
    }

    protected override void OnPreviewMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            base.OnPreviewMouseLeftButtonUp(e);
            return;
        }

        e.Handled = true;
        EndDrag();
    }

    protected override void OnLostMouseCapture(System.Windows.Input.MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_dragging)
        {
            EndDrag();
        }
    }

    private void EndDrag()
    {
        _dragging = false;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        _hover.Visibility = IsMouseOver && !IsLocked ? Visibility.Visible : Visibility.Collapsed;
        var moved = new List<(int, double, double)> { (SkillId, Left, Top) };
        if (_carried is not null)
        {
            moved.AddRange(_carried.Select(c => (c.Window.SkillId, c.Window.Left, c.Window.Top)));
        }

        _carried = null;
        Save(moved);
        Moved?.Invoke();
    }

    /// <summary>The cursor in this window's screen units (null without a screen).</summary>
    private Point? CursorOnScreen()
    {
        if (!NativeCursor.TryGet(out int x, out int y) || PresentationSource.FromVisual(this)?.CompositionTarget is not { } target)
        {
            return null;
        }

        return target.TransformFromDevice.Transform(new Point(x, y));
    }

    /// <summary>A place pulled flush against a skill close to it (side by side or one under the other), its edges lined up with it when they nearly are.</summary>
    private (double Left, double Top) Snap(double left, double top)
    {
        double w = ActualWidth, h = ActualHeight;
        double bestX = Reach + 1, bestY = Reach + 1, x = left, y = top;
        foreach (CooldownSkillWindow o in Live)
        {
            if (o == this || !o.IsVisible)
            {
                continue;
            }

            double ol = o.Left, ot = o.Top, or = o.Left + o.ActualWidth, ob = o.Top + o.ActualHeight;
            bool besideY = top < ob + Reach && top + h > ot - Reach, besideX = left < or + Reach && left + w > ol - Reach;
            if (besideY)
            {
                // next to it: flush right or left of it, or the same left or right edge
                foreach (double candidate in new[] { or, ol - w, ol, or - w })
                {
                    double d = Math.Abs(candidate - left);
                    if (d < bestX)
                    {
                        bestX = d;
                        x = candidate;
                    }
                }
            }

            if (besideX)
            {
                // above or under it: flush under or over it, or the same top or bottom edge
                foreach (double candidate in new[] { ob, ot - h, ot, ob - h })
                {
                    double d = Math.Abs(candidate - top);
                    if (d < bestY)
                    {
                        bestY = d;
                        y = candidate;
                    }
                }
            }
        }

        return (bestX <= Reach ? x : left, bestY <= Reach ? y : top);
    }

    // ---------- the places ----------

    private static (double?, double?, double) Load(int skillId)
    {
        var s = MeterSettings.Load();
        return PlacesOf(s, s.AssistCooldownStyle).TryGetValue(skillId, out double[]? p) && p.Length == 2 ? (p[0], p[1], s.AssistCooldownScale) : (null, null, s.AssistCooldownScale);
    }

    /// <summary>Keeps where these skills are, for the look in use.</summary>
    public static void Save(IEnumerable<(int SkillId, double Left, double Top)> places)
    {
        var s = MeterSettings.Load();
        Dictionary<int, double[]> kept = PlacesOf(s, s.AssistCooldownStyle);
        bool any = false;
        foreach ((int id, double left, double top) in places)
        {
            if (double.IsFinite(left) && double.IsFinite(top))
            {
                kept[id] = new[] { Math.Round(left, 1), Math.Round(top, 1) };
                any = true;
            }
        }

        if (any)
        {
            s.Save();
        }
    }

    private static SolidColorBrush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// The small bar shown at the top of the screen while the cooldowns are being placed: their look (squares or bars), whether they move together, their
/// size, and Done (locks them again).
/// </summary>
public sealed class CooldownEditBar : Window
{
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    private readonly Action<string> _look;
    private readonly Action _together, _smaller, _larger, _done;

    public CooldownEditBar(Action<string> look, Action together, Action smaller, Action larger, Action done)
    {
        _look = look;
        _together = together;
        _smaller = smaller;
        _larger = larger;
        _done = done;
        Title = "Aion DPS Placing Bar"; // not a cooldown's title (IsCooldownTitle)
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        AltTabHidden.Apply(this);
        Content = BarFrame(_row);
        SourceInitialized += (_, _) =>
        {
            using var native = new NativeOverlay(this);
            native.SetNoActivate(true); // clicked without the game losing the keyboard
        };
    }

    /// <summary>Shows what is chosen now.</summary>
    public void ShowState(string style, bool together)
    {
        _row.Children.Clear();
        Fill(_row, style, together, _look, _together, _smaller, _larger, _done);
    }

    /// <summary>The bar's look, also for the picture the build makes.</summary>
    public static Border BarFrame(UIElement row) => new()
    {
        Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x12, 0x16, 0x1C)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(6), Child = row,
    };

    /// <summary>The bar's buttons: a hint, the look (two), together, the size (smaller, larger), done.</summary>
    public static void Fill(System.Windows.Controls.Panel row, string style, bool together, Action<string> look, Action toggleTogether, Action smaller, Action larger, Action done)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        row.Children.Add(Words(loc["Assist.EditHint"]));
        row.Children.Add(Gap());
        row.Children.Add(Chip(loc["Assist.LookTiles"], style == CooldownArt.Tiles, () => look(CooldownArt.Tiles)));
        row.Children.Add(Chip(loc["Assist.LookBars"], style != CooldownArt.Tiles, () => look(CooldownArt.Bars)));
        row.Children.Add(Gap());
        row.Children.Add(Chip((together ? "\uE72E  " : "\uE785  ") + loc["Assist.MoveTogether"], together, toggleTogether, glyph: true, tip: loc["Assist.MoveTogetherTip"]));
        row.Children.Add(Gap());
        row.Children.Add(Words(loc["Assist.Size"]));
        row.Children.Add(Chip("\u2212", false, smaller, tip: loc["Assist.Smaller"]));
        row.Children.Add(Chip("+", false, larger, tip: loc["Assist.Larger"]));
        row.Children.Add(Gap());
        row.Children.Add(Chip(loc["Assist.Done"], true, done, accent: true));
    }

    private static UIElement Gap() => new Border { Width = 1, Margin = new Thickness(6, 4, 6, 4), Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)) };

    private static UIElement Words(string text) => new TextBlock
    {
        Text = text, FontSize = 12.5, Margin = new Thickness(6, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Color.FromRgb(0xA7, 0xB0, 0xBA)),
    };

    private static Border Chip(string text, bool on, Action click, bool glyph = false, string? tip = null, bool accent = false)
    {
        var label = new TextBlock { Text = text, FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        if (glyph)
        {
            // the lock's glyph from the system's icon font, the words in the normal one
            label.Text = "";
            label.Inlines.Add(new System.Windows.Documents.Run(text[..1]) { FontFamily = new FontFamily("Segoe MDL2 Assets") });
            label.Inlines.Add(new System.Windows.Documents.Run(text[1..]));
        }

        label.Foreground = on ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xA7, 0xB0, 0xBA));
        var chip = new Border
        {
            Child = label, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(2, 0, 2, 0), CornerRadius = new CornerRadius(5), ToolTip = tip,
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = accent ? new SolidColorBrush(Color.FromArgb(0xCC, 0x2F, 0x7D, 0x4A)) : on ? new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent,
        };
        chip.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            click();
        };
        return chip;
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
            Say(back.Select(id => NameOf(id, settings)).ToList(), settings);
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

    /// <summary>A skill's name as the voice says it: in English for the recorded voice, else in the program's language.</summary>
    public static string NameOf(int skillId, MeterSettings settings) =>
        CastSpeech.Natural(settings) ? Aion2SkillNames.EnglishOf(skillId) : Aion2SkillNames.DisplayOf(skillId);

    /// <summary>The name the test button says ("Skill"), in the voice's language for the recorded voice.</summary>
    public static string SampleName(MeterSettings settings) =>
        CastSpeech.Natural(settings) ? LocalizationManager.Instance.In("Assist.VoiceSample", "en") : LocalizationManager.Instance["Assist.VoiceSample"];

    /// <summary>Says that these skills are back (also the window's test button): "Taunt ready", each a phrase of the recorded voice.</summary>
    public void Say(IReadOnlyList<string> skills, MeterSettings settings)
    {
        string words = CastSpeech.WordsLanguage(settings);
        var each = skills.Select(name => string.Format(CultureInfo.CurrentCulture, LocalizationManager.Instance.In("Assist.VoiceReady", words), name))
            .Select(line => new Spoken(line, new[] { line })).ToList();
        _speech.Speak(Spoken.Join(each), settings.SpeechVoiceId, Math.Clamp(settings.AssistVoiceVolume, 0, 100) / 100.0, AudioOutput.DeviceId);
    }
}
