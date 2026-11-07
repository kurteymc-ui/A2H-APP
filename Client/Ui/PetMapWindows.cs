using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>
/// A small transparent overlay that can be placed and scaled on its own: locked it is click-through, unlocked it takes clicks, can be dragged by
/// its body and scaled with the handles at its corners and edges. Place and size are kept per overlay (the pet map and the pet list are two of them).
/// </summary>
public abstract class MovableOverlay : Window, IOverlayPlace
{
    private const double MinScale = 0.5, MaxScale = 3.0;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly Border _frame;
    private readonly Border _bodyHost = new();
    private readonly Border _shiftHost = new();
    private readonly TranslateTransform _shift = new();
    private readonly Func<(double? Left, double? Top, double Scale)> _load;
    private readonly Action<double, double, double> _save;
    private readonly Action<double?, double?> _saveSize;
    private readonly Func<(double? Width, double? Height)> _loadSizeFn;
    private bool _locked = true;
    private readonly OverlayEdgeResize _edges;

    protected MovableOverlay(string title, UIElement body, Func<(double? Left, double? Top, double Scale)> load, Action<double, double, double> save, double defaultLeft, double defaultTop,
        Func<(double? Width, double? Height)> loadSize, Action<double?, double?> saveSize, OverlayEdgeMode edgeMode = OverlayEdgeMode.Crop)
    {
        Title = title;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AltTabHidden.Apply(this);
        ShowActivated = false; // the game keeps the keyboard
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        _load = load;
        _save = save;
        _saveSize = saveSize;
        _loadSizeFn = loadSize;

        var saved = load();
        SetScale(saved.Scale);
        _shiftHost.RenderTransform = _shift;
        _shiftHost.Child = body;
        _bodyHost.Child = _shiftHost;
        _bodyHost.ClipToBounds = true;
        var stack = new DockPanel { LastChildFill = true };
        stack.Children.Add(_bodyHost);
        _frame = new Border { Child = stack, LayoutTransform = _scale, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(2), Background = Brushes.Transparent };
        _frame.MouseLeftButtonDown += (_, e) =>
        {
            if (!_locked && e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
                SavePlace();
            }
        };
        var size = loadSize();
        if (size.Width is > 0) { _frame.Width = size.Width.Value; }
        if (size.Height is > 0) { _frame.Height = size.Height.Value; }
        if (edgeMode == OverlayEdgeMode.CropAnchored && MeterSettings.Load().OverlayShifts.TryGetValue(title, out double[]? shift) && shift.Length == 2)
        {
            (_shift.X, _shift.Y) = Plausible(shift[0], shift[1]);
        }

        // an edge pulled moves the content by the distance of this pull on top of where it was (3 November 2026: each pull set it anew, so a second pull at
        // another edge put it back to nothing on the screen while the first pull's value stayed in the settings; after the next start the map showed a third
        // of itself, cut off on the right with the padlock)
        _edges = new OverlayEdgeResize(this, _frame, () => _scale.ScaleX, () => _shiftAtStart = (_shift.X, _shift.Y), SavePlace, SetScale, edgeMode,
            (x, y) => { _shift.X = _shiftAtStart.X + x; _shift.Y = _shiftAtStart.Y + y; });
        Content = _edges.Wrap(_frame);
        Left = saved.Left ?? defaultLeft;
        Top = saved.Top ?? defaultTop;
        KeepOnScreen();
        ApplyLocked(true);
        _defaultLeft = defaultLeft;
        _defaultTop = defaultTop;
        Action<string> onReset = title => { if (title == Title) { Dispatcher.Invoke(ReloadPlace); } };
        OverlayPlaces.WasReset += onReset;
        Closed += (_, _) => OverlayPlaces.WasReset -= onReset;
        OverlayDocking.Attach(this, SavePlace);
    }

    private readonly double _defaultLeft, _defaultTop;
    private (double X, double Y) _shiftAtStart;

    /// <summary>
    /// A saved shift that cannot be meant is dropped: an empty margin of more than 40 % of the frame on the left or at the top (the content then cut off on
    /// the other side: the owner's map, 3 November 2026, 313 of 444 pixels empty on the left), or the content pushed out of the frame but for 60 pixels.
    /// </summary>
    private (double X, double Y) Plausible(double x, double y)
    {
        double w = double.IsFinite(_frame.Width) ? _frame.Width : 440, h = double.IsFinite(_frame.Height) ? _frame.Height : 440;
        static bool Fits(double v, double size) => double.IsFinite(v) && (v >= 0 ? v <= 0.4 * size : -v <= 380);
        return Fits(x, w) && Fits(y, h) ? (x, y) : (0, 0);
    }

    /// <summary>
    /// A place no monitor shows is brought onto the nearest one (2 Nov 2026: the loot overlay was saved at y 1140 on a 2560 × 1440 monitor; on a 2560 × 1600
    /// screen at 175 % that is below the bottom (914 high), so it showed nowhere, not even on the layout). The saved place is kept until the overlay is
    /// moved by hand: with the other monitor back, it is there again.
    /// </summary>
    private void KeepOnScreen()
    {
        if (_isView || !double.IsFinite(Left) || !double.IsFinite(Top))
        {
            return;
        }

        double w = ActualWidth > 0 ? ActualWidth : 260, h = ActualHeight > 0 ? ActualHeight : 120;
        var place = new Rect(Left, Top, w, h);
        if (WindowPlacement.IsOnScreen(place))
        {
            return;
        }

        Rect area = WindowPlacement.WorkAreaOf(place);
        Left = Math.Max(area.Left, Math.Min(Left, area.Right - Math.Min(w, area.Width)));
        Top = Math.Max(area.Top, Math.Min(Top, area.Bottom - Math.Min(h, area.Height)));
    }
    private DateTime _opacityAt = DateTime.MinValue;

    /// <summary>The place, size and scale are read from the settings again (after "reset size and position").</summary>
    private void ReloadPlace()
    {
        var saved = _load();
        SetScale(saved.Scale);
        var size = _loadSizeFn();
        _frame.Width = size.Width is > 0 ? size.Width.Value : double.NaN;
        _frame.MaxHeight = double.PositiveInfinity;
        _frame.Height = size.Height is > 0 ? size.Height.Value : double.NaN;
        (_shift.X, _shift.Y) = MeterSettings.Load().OverlayShifts.TryGetValue(Title, out double[]? shifted) && shifted.Length == 2 ? Plausible(shifted[0], shifted[1]) : (0, 0);
        Left = saved.Left ?? _defaultLeft;
        Top = saved.Top ?? _defaultTop;
        KeepOnScreen();
        ApplyLocked(_locked);
    }

    public bool IsLocked => _locked;

    // ---- the layout editor (OverlayLayout.cs) ----
    private bool _isView;

    public bool PlaceIsView => _isView;

    public Rect PlaceBounds
    {
        get
        {
            Size size = OverlayMeasure.SizeOf(this);
            return new Rect(double.IsFinite(Left) ? Left : 0, double.IsFinite(Top) ? Top : 0, size.Width, size.Height);
        }
    }

    public double PlaceScale => _scale.ScaleX;

    public void PlaceAt(double left, double top, bool save)
    {
        Left = left;
        Top = top;
        if (save)
        {
            SavePlace();
        }
    }

    public void PlaceScaleTo(double scale, bool save)
    {
        SetScale(scale);
        if (save)
        {
            SavePlace();
        }
    }

    /// <summary>The height the frame may take in its own units (the scaling excluded): the height it was pulled to; without one, the screen's.</summary>
    protected double HeightLimit
    {
        get
        {
            double h = !double.IsNaN(_frame.Height) ? _frame.Height : !double.IsInfinity(_frame.MaxHeight) ? _frame.MaxHeight : SystemParameters.WorkArea.Height;
            return h / Math.Max(_scale.ScaleY, 0.1);
        }
    }

    /// <summary>The size the body really has (in its own units, the frame's scaling excluded).</summary>
    /// <remarks>A view's body is laid out in the tool page, not in this never shown window (whose own size stays 0): its own size counts there,
    /// the whole view until it has been laid out.</remarks>
    protected double BodyWidth => _detached is FrameworkElement shown ? (shown.ActualWidth > 0 ? shown.ActualWidth : double.PositiveInfinity) : _bodyHost.ActualWidth;

    protected double BodyHeight => _detached is FrameworkElement shown ? (shown.ActualHeight > 0 ? shown.ActualHeight : double.PositiveInfinity) : _bodyHost.ActualHeight;

    private UIElement? _detached;

    /// <summary>Takes the content out of a window that is never shown, to put it into a tool's own window (a live view): the window keeps rendering into it.</summary>
    public UIElement? DetachBody()
    {
        _isView = true;
        UIElement? body = _shiftHost.Child;
        _shiftHost.Child = null;
        _detached = body;
        return body;
    }

    /// <summary>The content of the overlay (set by the derived window once it has built it).</summary>
    protected UIElement? Body
    {
        set => _shiftHost.Child = value;
    }

    /// <summary>No room around the content (the frame's outline stays, seen only unlocked): overlays that sit flush against each other, a skill's cooldown.</summary>
    protected void Flush()
    {
        _frame.Padding = new Thickness(0);
        _frame.CornerRadius = new CornerRadius(2);
    }

    /// <summary>Sets the scale (the one size of a group of overlays, kept elsewhere) without saving it.</summary>
    public void UseScale(double scale)
    {
        if (Math.Abs(_scale.ScaleX - scale) > 0.001)
        {
            SetScale(scale);
        }
    }

    /// <summary>Applies the lock only when it changed (the controller asks every tick).</summary>
    public void ApplyLockedIfChanged(bool locked)
    {
        if (locked != _locked)
        {
            ApplyLocked(locked);
        }
    }

    private void SetScale(double scale)
    {
        scale = Math.Clamp(double.IsFinite(scale) ? scale : 1.0, MinScale, MaxScale);
        _scale.ScaleX = scale;
        _scale.ScaleY = scale;
    }

    private void SavePlace()
    {
        _save(Left, Top, _scale.ScaleX);
        double? height = !double.IsNaN(_frame.Height) ? _frame.Height : !double.IsInfinity(_frame.MaxHeight) ? _frame.MaxHeight : null;
        _saveSize(double.IsNaN(_frame.Width) ? null : _frame.Width, height);
        // saved also when it is back at nothing (a value kept from before came back at the next start)
        var s = MeterSettings.Load();
        bool had = s.OverlayShifts.TryGetValue(Title, out double[]? before);
        if (_shift.X != 0 || _shift.Y != 0)
        {
            s.OverlayShifts[Title] = new[] { _shift.X, _shift.Y };
            s.Save();
        }
        else if (had)
        {
            s.OverlayShifts.Remove(Title);
            s.Save();
        }
    }

    /// <summary>The outline while unlocked (null: the orange one of the overlays); a cooldown or the effects draw a quiet one of their own.</summary>
    protected Brush? UnlockedBorder { get; set; }

    /// <summary>The shade behind the content while unlocked (null: a light dark one).</summary>
    protected Brush? UnlockedBackground { get; set; }

    /// <summary>A click never makes it the active window, unlocked too: the game keeps the keyboard while the overlay is placed (a skill's cooldown, moved by
    /// its own drag rather than by Windows' move loop).</summary>
    protected bool NeverActivate { get; set; }

    /// <summary>The handles for the size drawn as quiet light marks instead of the orange ones.</summary>
    protected void QuietHandles() => _edges.Quiet();

    /// <summary>Locked: click-through, no outline, no grip. Unlocked: clickable and outlined, so it can be found, moved and scaled.</summary>
    public void ApplyLocked(bool locked)
    {
        _locked = locked;
        // Unlocked, the frame has the height it was pulled to (the sample rows fill it); locked, that height is only the most it may take,
        // so a short list ends at its last row without empty space below.
        if (locked && !double.IsNaN(_frame.Height))
        {
            _frame.MaxHeight = _frame.Height;
            _frame.Height = double.NaN;
        }
        else if (!locked && !double.IsInfinity(_frame.MaxHeight))
        {
            _frame.Height = _frame.MaxHeight;
            _frame.MaxHeight = double.PositiveInfinity;
        }

        _edges.Show(!locked);
        _frame.Cursor = locked ? Cursors.Arrow : Cursors.SizeAll;
        _frame.BorderBrush = locked ? Brushes.Transparent : UnlockedBorder ?? new SolidColorBrush(Color.FromRgb(0xFF, 0xBA, 0x42));
        _frame.Background = locked ? Brushes.Transparent : UnlockedBackground ?? new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0));
        ApplyClickThrough();
    }

    private void ApplyClickThrough()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
        if (handle != IntPtr.Zero)
        {
            using var native = new NativeOverlay(this);
            native.SetClickThrough(_locked && !_hoverInput);
            native.SetNoActivate((_locked && _hoverInput) || NeverActivate); // a click on a locked overlay (its buttons) never takes the keyboard from the game
        }
    }

    private bool _hoverInput;

    /// <summary>
    /// A locked overlay lets every click through to the game. While <paramref name="over"/> (the mouse rests on it and the game shows the cursor) it takes
    /// the mouse for that moment - its wheel and its buttons - without taking the focus from the game, and lets everything through again once the cursor leaves.
    /// </summary>
    protected void TakeMouseWhileOver(bool over)
    {
        if (over == _hoverInput || _isView)
        {
            return;
        }

        _hoverInput = over;
        if (_locked)
        {
            ApplyClickThrough();
        }
    }

    /// <summary>Shows or hides the window; the click-through style is set again after the first show (WPF may recreate the handle).</summary>
    public void ShowOverlay(bool visible)
    {
        if (!visible)
        {
            _hoverInput = false; // shown again, it lets the clicks through until the cursor rests on it
            if (IsVisible)
            {
                Hide();
            }

            return;
        }

        if (!IsVisible || (DateTime.UtcNow - _opacityAt).TotalSeconds >= 2)
        {
            _opacityAt = DateTime.UtcNow;
            ApplyStoredOpacity(); // also while shown: a changed opacity in the settings takes effect within a moment
            KeepOnScreen();
        }

        if (!IsVisible)
        {
            Show();
            ApplyClickThrough();
        }
    }

    private byte? _baseAlpha;

    /// <summary>A least opacity (0 to 1) the overlay keeps while set, whatever its own setting says (the pet list over the shop, where it sits on the game's own window text).</summary>
    public double? OpacityFloor { get; set; }

    /// <summary>The overlay's own opacity (0 to 1, set per overlay in its module; the key is its title) replaces the alpha of the body's dark background; <see cref="OpacityFloor"/> raises it.</summary>
    public void ApplyStoredOpacity()
    {
        if (_shiftHost.Child is not Border { Background: SolidColorBrush bg } body)
        {
            return;
        }

        _baseAlpha ??= bg.Color.A;
        double alpha = MeterSettings.Load().OverlayOpacities.TryGetValue(Title, out double v) ? Math.Clamp(v, 0, 1) : _baseAlpha.Value / 255.0;
        if (OpacityFloor is { } floor)
        {
            alpha = Math.Max(alpha, floor);
        }

        body.Background = new SolidColorBrush(Color.FromArgb((byte)Math.Round(alpha * 255), bg.Color.R, bg.Color.G, bg.Color.B));
    }

    public void RaiseToFront()
    {
        if (IsVisible)
        {
            NativeOverlay.KeepOnTop(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        }
    }
}

/// <summary>The mouse cursor's place on the screen (device pixels), read from Windows: the overlays let every click through and never get mouse events.</summary>
internal static class NativeCursor
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    public static bool TryGet(out int x, out int y)
    {
        bool ok = GetCursorPos(out NativePoint p);
        x = p.X;
        y = p.Y;
        return ok;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct CursorInfo
    {
        public int Size;
        public int Flags;
        public IntPtr Cursor;
        public NativePoint Position;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetCursorInfo(ref CursorInfo info);

    /// <summary>True while a mouse cursor is on the screen: the game hides it while the mouse turns the camera (hidden, or set to none).</summary>
    public static bool Showing()
    {
        var info = new CursorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<CursorInfo>() };
        return GetCursorInfo(ref info) && (info.Flags & 1) != 0 && info.Cursor != IntPtr.Zero;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    /// <summary>True while the left mouse button is held (with the buttons swapped, the physical right one). Asked of Windows: a drag of the map that
    /// left its window ends where the button is let go, which the window itself never hears of.</summary>
    public static bool LeftButtonHeld() => (GetAsyncKeyState(SystemParameters.SwapButtons ? 0x02 : 0x01) & 0x8000) != 0;
}

/// <summary>
/// The arithmetic of the map's free move (pure, so the self-test can check it). A view shows the world around its middle (<c>midX</c>, <c>midY</c>, world units)
/// at <c>ppu</c> view pixels per world unit; (<c>cx</c>, <c>cy</c>) is the middle of the view in its own pixels.
/// </summary>
public static class PetMapPan
{
    /// <summary>The world place under the point (<paramref name="atX"/>, <paramref name="atY"/>) of the view.</summary>
    public static (double X, double Y) WorldAt(double atX, double atY, double midX, double midY, double cx, double cy, double ppu) =>
        (midX + (atX - cx) / ppu, midY + (atY - cy) / ppu);

    /// <summary>The view's middle that puts the world place (<paramref name="worldX"/>, <paramref name="worldY"/>) under the point (<paramref name="atX"/>,
    /// <paramref name="atY"/>) of the view: a drag keeps the place it took hold of under the cursor.</summary>
    public static (double X, double Y) MiddleHolding(double worldX, double worldY, double atX, double atY, double cx, double cy, double ppu) =>
        (worldX - (atX - cx) / ppu, worldY - (atY - cy) / ppu);

    /// <summary>The view's middle after the scale went from <paramref name="oldPpu"/> to <paramref name="newPpu"/>, zoomed around the point (<paramref name="atX"/>,
    /// <paramref name="atY"/>) of the view: the place under it stays where it was (the wheel zooms around the cursor; the view's own middle for the buttons).</summary>
    public static (double X, double Y) ZoomAround(double midX, double midY, double atX, double atY, double cx, double cy, double oldPpu, double newPpu)
    {
        var (worldX, worldY) = WorldAt(atX, atY, midX, midY, cx, cy, oldPpu);
        return MiddleHolding(worldX, worldY, atX, atY, cx, cy, newPpu);
    }

    /// <summary>
    /// The view's middle kept on the land (from <paramref name="x0"/>, <paramref name="y0"/>, <paramref name="w"/> wide and <paramref name="h"/> high, world units),
    /// the view reaching <paramref name="halfW"/> and <paramref name="halfH"/> to each side of its middle: the view stays on the land, and a land smaller than the view
    /// sits in its middle. So the map's picture can never be dragged out of the view.
    /// </summary>
    public static (double X, double Y) Clamp(double midX, double midY, double x0, double y0, double w, double h, double halfW, double halfH) =>
        (Axis(midX, x0, w, halfW), Axis(midY, y0, h, halfH));

    private static double Axis(double mid, double start, double length, double half) =>
        2 * half >= length ? start + length / 2 : Math.Clamp(mid, start + half, start + length - half);
}

/// <summary>The colours of the chosen pets (the same on the map and in the list).</summary>
public static class PetMapPalette
{
    private static readonly string[] Hex = { "#4D9BFF", "#FF8A3D", "#5BD07A", "#E05BD0", "#FFE04D", "#4DDCE0", "#FF5A6E", "#B58BFF" };

    public static Color Of(int index) => (Color)ColorConverter.ConvertFromString(Hex[index % Hex.Length]);

    private static readonly Dictionary<string, Color> GatherColors = new();
    private static readonly Dictionary<Color, SolidColorBrush> FrozenBrushes = new();

    /// <summary>The colour of a kind of collectible (shown as a diamond). Looked up for every point on every map tick, so each kind is parsed once.</summary>
    public static Color OfGather(string kind)
    {
        lock (GatherColors)
        {
            if (!GatherColors.TryGetValue(kind, out Color color))
            {
                color = (Color)ColorConverter.ConvertFromString(kind switch
                {
                    "Od" => "#7FE3FF",
                    "Herb" => "#3DDC84",
                    "Food" => "#FFC24D",
                    "Ore" => "#C9CED6",
                    "Wood" => "#B5835A",
                    "Gemstone" => "#E08CFF",
                    "Cotton" => "#F3EBD3",
                    "Fragment" => "#FF7B7B",
                    "Cube" => "#FFD84D",
                    _ => "#FFFFFF",
                });
                GatherColors[kind] = color;
            }

            return color;
        }
    }

    /// <summary>A frozen brush of a colour, shared: the map assigns one to every dot every 50 ms, and a fresh unfrozen brush each time is garbage and a re-render trigger.</summary>
    public static SolidColorBrush BrushOf(Color color)
    {
        lock (FrozenBrushes)
        {
            if (!FrozenBrushes.TryGetValue(color, out SolidColorBrush? brush))
            {
                brush = new SolidColorBrush(color);
                brush.Freeze();
                FrozenBrushes[color] = brush;
            }

            return brush;
        }
    }
}

/// <summary>
/// The pictures of the map's marks, each made once and then shared (frozen): a pet's portrait in a ring of its colour for its monsters, the item a
/// collectible gives (the ore, the gem, the herb) on a small dark tile edged in its kind's colour, and a gold cube for the Hidden Cubes. The maps redraw
/// twenty times a second: they only move these pictures around, they never read a file or build a shape per frame.
/// </summary>
public static class PetMapMarks
{
    private static readonly Dictionary<string, BitmapSource?> Bitmaps = new();
    private static readonly Dictionary<(int Pet, Color Ring, double Size, bool Live), ImageSource> Pets = new();
    private static readonly Dictionary<(string Kind, string Key, double Size), ImageSource> Gathers = new();
    private static readonly Dictionary<(double Size, bool Live), ImageSource> Cubes = new();
    private static readonly Brush TileBack = PetMapPalette.BrushOf(Color.FromArgb(0xD8, 0x12, 0x16, 0x1C));
    private static readonly Brush SlotBack = PetMapPalette.BrushOf(Color.FromArgb(0xE0, 0x2A, 0x30, 0x38)); // a little lighter: the dark icons (wood, ore) stay readable
    private static readonly Brush EdgeDark = PetMapPalette.BrushOf(Color.FromArgb(0xB0, 0x0A, 0x0C, 0x10));
    private static readonly Brush CubeTop = PetMapPalette.BrushOf(Color.FromRgb(0xFF, 0xE5, 0x8A));
    private static readonly Brush CubeLeft = PetMapPalette.BrushOf(Color.FromRgb(0xF0, 0xB9, 0x2E));
    private static readonly Brush CubeRight = PetMapPalette.BrushOf(Color.FromRgb(0xB4, 0x7C, 0x14));
    private static readonly Brush CubeEdge = PetMapPalette.BrushOf(Color.FromRgb(0x3B, 0x2A, 0x05));
    private static readonly Brush CubeGlow = FrozenGlow();
    private static readonly Brush CubeHollow = PetMapPalette.BrushOf(Color.FromArgb(0x8C, 0x14, 0x10, 0x08));
    private static readonly Brush CubeOutline = PetMapPalette.BrushOf(Color.FromArgb(0xD0, 0xF0, 0xC0, 0x50));

    /// <summary>
    /// A pet's mark: its portrait cut round, in a ring of the pet's colour (the list beside the map has the same colour); a monster that is there right now
    /// (<paramref name="live"/>) gets a white edge on top. Too small to read a portrait, or a pet without one: the dot of its colour, as the map had before.
    /// </summary>
    public static ImageSource Pet(int petId, Color ring, double size, bool live)
    {
        lock (Pets)
        {
            var key = (petId, ring, size, live);
            if (Pets.TryGetValue(key, out ImageSource? made))
            {
                return made;
            }

            if (Pets.Count > 2000)
            {
                Pets.Clear(); // the colours move between pets as the chosen set changes: a limit instead of a table that only grows
            }

            made = Finish(DrawPet(size >= 12 ? Bitmap(Aion2Pets.PetIconPath(petId)) : null, ring, size, live));
            Pets[key] = made;
            return made;
        }
    }

    /// <summary>
    /// A collectible's mark: the icon of the item it gives on a small dark tile (a square like an inventory slot, so it is never taken for a monster)
    /// edged in its kind's colour. No icon known: a diamond of the kind's colour on the tile; very small (the whole map): the plain diamond.
    /// The Hidden Cubes' places are cubes (<see cref="Cube"/>).
    /// </summary>
    public static ImageSource Gather(string kind, string key, double size)
    {
        if (kind == "Cube")
        {
            return Cube(size, false);
        }

        lock (Gathers)
        {
            var cacheKey = (kind, key, size);
            if (Gathers.TryGetValue(cacheKey, out ImageSource? made))
            {
                return made;
            }

            made = Finish(DrawGather(PetMapPalette.BrushOf(PetMapPalette.OfGather(kind)), size >= 11 ? Bitmap(Aion2Gather.IconPath(key)) : null, size));
            Gathers[cacheKey] = made;
            return made;
        }
    }

    /// <summary>A Hidden Cube: an isometric cube in gold (three faces, light on top) that glows, one the game announces right now (<paramref name="live"/>); a place a
    /// cube can appear at is only its gold outline on a dark see-through cube, so it is never taken for a cube (5 November 2026: the places were solid gold
    /// cubes, bigger than the real one).</summary>
    public static ImageSource Cube(double size, bool live)
    {
        lock (Cubes)
        {
            if (Cubes.TryGetValue((size, live), out ImageSource? made))
            {
                return made;
            }

            made = Finish(DrawCube(size, live));
            Cubes[(size, live)] = made;
            return made;
        }
    }

    private static DrawingGroup DrawPet(BitmapSource? portrait, Color ring, double size, bool live)
    {
        var group = new DrawingGroup();
        double c = size / 2;
        var middle = new Point(c, c);
        Brush color = PetMapPalette.BrushOf(ring);
        using (DrawingContext dc = group.Open())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, size, size)); // the picture is the whole square, so it is placed by its middle
            if (portrait is null)
            {
                double edge = size >= 12 ? (live ? 2.2 : 1.4) : 0.7;
                dc.DrawEllipse(color, new Pen(Brushes.White, edge), middle, c - edge / 2, c - edge / 2);
            }
            else
            {
                // a white edge (live) or a dark one, so the mark stands out on any ground; inside it the ring of the pet's colour over the portrait
                double outer = live ? 1.4 : 0.8, band = live ? 2.2 : 1.7, r = c - outer;
                dc.DrawEllipse(live ? Brushes.White : EdgeDark, null, middle, c, c);
                dc.PushClip(new EllipseGeometry(middle, r, r));
                dc.DrawRectangle(TileBack, null, new Rect(c - r, c - r, 2 * r, 2 * r));
                dc.DrawImage(portrait, new Rect(c - r, c - r, 2 * r, 2 * r));
                dc.Pop();
                dc.DrawEllipse(null, new Pen(color, band), middle, r - band / 2, r - band / 2);
            }
        }

        return group;
    }

    private static DrawingGroup DrawGather(Brush kindColor, BitmapSource? icon, double size)
    {
        var group = new DrawingGroup();
        double c = size / 2;
        using (DrawingContext dc = group.Open())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, size, size));
            if (size < 11)
            {
                dc.DrawGeometry(kindColor, new Pen(Brushes.Black, 0.9), Figure(new Point(c, 0.5), new Point(size - 0.5, c), new Point(c, size - 0.5), new Point(0.5, c)));
                return group;
            }

            dc.DrawRoundedRectangle(SlotBack, new Pen(kindColor, 1.2), new Rect(0.6, 0.6, size - 1.2, size - 1.2), size * 0.2, size * 0.2);
            if (icon is not null)
            {
                dc.DrawImage(icon, new Rect(1, 1, size - 2, size - 2)); // the icons have a margin of their own
            }
            else
            {
                double a = size * 0.24;
                dc.DrawGeometry(kindColor, new Pen(Brushes.Black, 0.8), Figure(new Point(c, a), new Point(size - a, c), new Point(c, size - a), new Point(a, c)));
            }
        }

        return group;
    }

    private static DrawingGroup DrawCube(double size, bool live)
    {
        var group = new DrawingGroup();
        double c = size / 2;
        // the outline of an isometric cube is a hexagon; h is half its height, the top face reaches down to the middle
        double h = live ? size * 0.36 : size * 0.32, w = h * 0.866;
        var top = new Point(c, c - h);
        var upperRight = new Point(c + w, c - h / 2);
        var lowerRight = new Point(c + w, c + h / 2);
        var bottom = new Point(c, c + h);
        var lowerLeft = new Point(c - w, c + h / 2);
        var upperLeft = new Point(c - w, c - h / 2);
        var middle = new Point(c, c);
        var edge = new Pen(CubeEdge, size >= 14 ? 1.0 : 0.7) { LineJoin = PenLineJoin.Round };
        using (DrawingContext dc = group.Open())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, size, size));
            if (live)
            {
                dc.DrawEllipse(CubeGlow, null, middle, c, c);
            }

            if (!live)
            {
                var outline = new Pen(CubeOutline, size >= 14 ? 1.1 : 0.8) { LineJoin = PenLineJoin.Round };
                dc.DrawGeometry(CubeHollow, outline, Figure(top, upperRight, lowerRight, bottom, lowerLeft, upperLeft));
                dc.DrawLine(outline, upperLeft, middle); // the inner edges: the top face's two lower ones and the front corner
                dc.DrawLine(outline, upperRight, middle);
                dc.DrawLine(outline, middle, bottom);
                return group;
            }

            dc.DrawGeometry(CubeTop, edge, Figure(top, upperRight, middle, upperLeft));
            dc.DrawGeometry(CubeLeft, edge, Figure(upperLeft, middle, bottom, lowerLeft));
            dc.DrawGeometry(CubeRight, edge, Figure(middle, upperRight, lowerRight, bottom));
        }

        return group;
    }

    /// <summary>A closed, filled shape through the points.</summary>
    private static Geometry Figure(params Point[] points)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext g = geometry.Open())
        {
            g.BeginFigure(points[0], true, true);
            g.PolyLineTo(points.Skip(1).ToList(), true, false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Brush FrozenGlow()
    {
        var glow = new RadialGradientBrush(Color.FromArgb(0xC8, 0xFF, 0xD8, 0x4D), Color.FromArgb(0x00, 0xFF, 0xD8, 0x4D));
        glow.Freeze();
        return glow;
    }

    /// <summary>The drawing as a frozen picture (the icons in it scaled smoothly): it can then be shown by any number of marks at once.</summary>
    private static ImageSource Finish(DrawingGroup drawing)
    {
        RenderOptions.SetBitmapScalingMode(drawing, BitmapScalingMode.HighQuality);
        if (drawing.CanFreeze)
        {
            drawing.Freeze();
        }

        var image = new DrawingImage(drawing);
        if (image.CanFreeze)
        {
            image.Freeze();
        }

        return image;
    }

    /// <summary>An icon file, read once and kept (small: the marks are 14 to 24 pixels; 48 keeps them sharp on a screen at twice the size). Null when unreadable.</summary>
    private static BitmapSource? Bitmap(string? path)
    {
        if (path is null)
        {
            return null;
        }

        lock (Bitmaps)
        {
            if (Bitmaps.TryGetValue(path, out BitmapSource? known))
            {
                return known;
            }

            BitmapSource? bitmap = null;
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 48;
                image.UriSource = new Uri(path);
                image.EndInit();
                image.Freeze();
                bitmap = image;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or FormatException or ArgumentException or UnauthorizedAccessException)
            {
                bitmap = null; // the plain mark instead
            }

            Bitmaps[path] = bitmap;
            return bitmap;
        }
    }
}

/// <summary>
/// The pet map: the game's own world map (transparent sea), centred on the player, with a mark for every spawn point of the chosen pets' monsters
/// (the pet's portrait in a ring of its own colour), the collectibles (the icon of what they give) and the Hidden Cubes (cubes). No text on it.
/// The view glides to each new position the game reports. It zooms (the wheel over it, its + and − buttons, the hotkeys) from the close-up around the player
/// out to the whole map with the whole route, which is what the separate full map overlay used to show. Its padlock lets it go (<see cref="Follow"/>): then it is
/// dragged with the mouse to look at any part of the map.
/// </summary>
public sealed class PetMapWindow : MovableOverlay
{
    public const double ViewSize = 440;
    // not quite transparent (alpha 1): Windows passes the mouse through the fully transparent pixels of a see-through window, and the wheel over the map's
    // empty parts (only the marks shown, laid over the game's minimap) must reach it too
    private readonly Canvas _canvas = new() { Width = ViewSize, Height = ViewSize, ClipToBounds = true, Background = PetMapPalette.BrushOf(Color.FromArgb(1, 0, 0, 0)) };
    private readonly Dictionary<string, BitmapSource?> _tiles = new();
    private readonly List<string> _tileOrder = new();
    private readonly List<Image> _tileImages = new();

    /// <summary>The marks in two layers: the spawn points and the collectibles under the monsters and cubes that are there right now (the player's arrow over all).</summary>
    private readonly MarkPool _spawnMarks, _gatherMarks, _liveMarks;
    private readonly List<Polygon> _edgeArrows = new();

    /// <summary>The pet id under which a Hidden Cube the game announces travels with the live monsters: drawn as a glowing cube.</summary>
    public const int CubeMark = -1;

    // the marks' sizes (pixels) close up, zoomed out a little, and far out (where the spawn points are dots: hundreds of them, too small for a portrait)
    private static readonly double[] SpawnSizes = { 16, 12, 6 }, LiveSizes = { 22, 19, 16 }, GatherSizes = { 18, 15, 13 }, CubeSpotSizes = { 14, 11, 9 }, LiveCubeSizes = { 24, 21, 18 };

    /// <summary>The marks of one pet (or one collectible) that fall on the same few pixels when zoomed out are drawn once (the same cell is the same mark).</summary>
    private readonly HashSet<(int, int, int)> _spawnCells = new();
    private readonly HashSet<(string, int, int)> _gatherCells = new();

    /// <summary>How far one notch of the mouse wheel, a click on + or −, or a zoom hotkey zooms: a tenth of the way from the close-up to the whole map.</summary>
    public const double ZoomStep = 0.1;

    /// <summary>Zoomed out to this radius (metres) or further, the map looks as the full map did: every stop's label, the player in a ring, the map's name.</summary>
    private const double FarMetres = 1200;

    /// <summary>Asked to zoom (the wheel over the map, its + and − buttons): the change of the zoom, 0 the close-up to 1 the whole map, negative in.
    /// Null: the map does not zoom (no buttons).</summary>
    public Action<double>? ZoomRequested { get; set; }

    private Border? _zoomButtons;

    /// <summary>The padlock was clicked: true to lock the overlay in place (then the mouse drags the map inside it), false to unlock it (then the mouse moves and
    /// sizes the window, as for every overlay). The controller keeps it in the settings. Null: no padlock (the map on the app's page).</summary>
    public Action<bool>? LockRequested { get; set; }

    /// <summary>Where the left button went down on the map, and whether the view followed the player then: a click that did not move the mouse leaves it following.</summary>
    private Point? _pressAt;
    private bool _pressFollowed;

    private bool _follow = true;

    /// <summary>
    /// The view follows the player (the default, the padlock shut): its middle is his place, zoomed out drawn towards the land, as the map always did. Off (free
    /// move, the padlock open), the view stays where it was dragged to with the left mouse button and the wheel zooms around the cursor; following again, it is back
    /// on the player at once.
    /// </summary>
    public bool Follow
    {
        get => _follow;
        set
        {
            if (value == _follow)
            {
                return;
            }

            _follow = value;
            Recentre(); // free move starts from the view followed till then; following again, nothing of the free view is kept
        }
    }

    /// <summary>Free move: the view's middle shown last (world units); null until the next frame takes the one followed (the start, a double click, another map).</summary>
    private (double X, double Y)? _freeMid;

    /// <summary>Free move, while the left button is held: the world place taken hold of, kept under the cursor.</summary>
    private (double X, double Y)? _grab;

    /// <summary>The mouse is on the map or drags it: every frame is drawn then (the hover labels, the drag), not only the ones where the player moved.</summary>
    public bool InUse => _grab is not null || _zoomFocus is not null || _canvas.IsMouseOver;

    /// <summary>Free move: the point of the view the wheel zoomed around, until the zoom has come to rest (null: the view's middle, as for the buttons and the hotkeys).</summary>
    private Point? _zoomFocus;

    /// <summary>How the last frame placed the world (its middle, the view's middle, the scale): a click is turned into a world place by it.</summary>
    private double _frameMidX, _frameMidY, _frameCx, _frameCy, _framePpu;
    private string? _frameMap;

    private Border? _lockButton;
    private System.Windows.Shapes.Path? _lockBody, _lockShackle;

    /// <summary>The state the padlock shows (its picture and tip change only when the overlay's lock does).</summary>
    private bool? _lockShows;

    // the padlock's picture (14 by 16): the body, the shackle shut (following) and open, lifted on its right leg (free move)
    private static readonly Geometry PadlockBody = FrozenGeometry("M 2.5,7 H 11.5 V 13.5 H 2.5 Z");
    private static readonly Geometry PadlockShut = FrozenGeometry("M 4.5,7 V 4.8 A 2.5,2.5 0 0 1 9.5,4.8 V 7");
    private static readonly Geometry PadlockOpen = FrozenGeometry("M 9.5,7 V 3.3 A 2.5,2.5 0 0 0 4.5,3.3 V 4.8");

    private static Geometry FrozenGeometry(string data)
    {
        Geometry geometry = Geometry.Parse(data);
        if (geometry.CanFreeze)
        {
            geometry.Freeze();
        }

        return geometry;
    }

    /// <summary>Free move: back to the player (the view stays free; the padlock makes it follow again).</summary>
    private void Recentre()
    {
        EndDrag();
        _freeMid = null;
        _zoomFocus = null;
    }

    private void EndDrag()
    {
        _grab = null;
        if (_canvas.IsMouseCaptured)
        {
            _canvas.ReleaseMouseCapture();
        }
    }

    /// <summary>
    /// The pictures of one layer of marks (<see cref="PetMapMarks"/>): the images are made once and only moved, resized and given another picture on every
    /// frame. The layers keep their order through their z-index (spawn points and collectibles under the live monsters), not through when an image was added.
    /// </summary>
    private sealed class MarkPool
    {
        private readonly Canvas _canvas;
        private readonly int _z;
        private readonly List<Image> _images = new();
        private int _used;

        public MarkPool(Canvas canvas, int z)
        {
            _canvas = canvas;
            _z = z;
        }

        public void Begin() => _used = 0;

        /// <summary>The next mark: <paramref name="source"/> centred on (<paramref name="x"/>, <paramref name="y"/>), <paramref name="size"/> pixels square.</summary>
        public void Place(ImageSource source, double x, double y, double size)
        {
            if (_used >= _images.Count)
            {
                var image = new Image { Stretch = Stretch.Fill, IsHitTestVisible = false };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                Panel.SetZIndex(image, _z);
                _images.Add(image);
                _canvas.Children.Add(image);
            }

            Image mark = _images[_used++];
            mark.Source = source;
            mark.Width = size;
            mark.Height = size;
            Canvas.SetLeft(mark, x - size / 2);
            Canvas.SetTop(mark, y - size / 2);
            mark.Visibility = Visibility.Visible;
        }

        /// <summary>The marks not placed in this frame are hidden (kept for the next).</summary>
        public void End()
        {
            for (int i = _used; i < _images.Count; i++)
            {
                _images[i].Visibility = Visibility.Collapsed;
            }
        }
    }
    private readonly Polygon _me;

    /// <summary>The way the player's arrow points, in degrees clockwise from straight up (the direction he moves in).</summary>
    public double MeHeading { get; set; }
    private readonly TextBlock _meUnknown;

    /// <summary>Round instead of square (to lay over the game's minimap): the view is cut to a circle and the edge arrows sit on it.</summary>
    public bool Round { get; set; }

    /// <summary>The points of the compass around the edge (N at the top: the game's maps have north up).</summary>
    public bool ShowCompass { get; set; } = true;

    private readonly List<Border> _compass = new();
    private string _compassLanguage = "";

    /// <summary>The compass shows only for a moment (<see cref="MeterSettings.PetMapCompassFade"/>, 7 November 2026: "hide the cardinals after 3 s"): for 3
    /// seconds after the map comes up, is zoomed, changes to another map or the mouse rests on it, then it fades. Always there while the map is being placed.</summary>
    public bool CompassFades { get; set; }

    private DateTime _compassWake = DateTime.UtcNow;
    private double _compassZoom = -1;
    private string _compassMap = "";

    /// <summary>Shows the compass again for its moment (the map came up, was zoomed or moved, or the mouse rests on it).</summary>
    public void WakeCompass() => _compassWake = DateTime.UtcNow;

    /// <summary>
    /// The eight points of the compass just inside the edge: the four main ones in the middle of the sides, the others in the corners (round: on the circle).
    /// North in gold. They stay where they are when the view moves or zooms (the map never turns).
    /// </summary>
    private void DrawCompass(double cx, double cy, double vw, double vh)
    {
        // fading: full for 3 s after it was woken, gone 0.6 s later (always on the app's page and while the map is placed)
        double alpha = 1;
        if (CompassFades && !PlaceIsView && IsLocked)
        {
            double since = (DateTime.UtcNow - _compassWake).TotalSeconds;
            alpha = since <= 3 ? 1 : since >= 3.6 ? 0 : 1 - (since - 3) / 0.6;
        }

        if (!ShowCompass || alpha <= 0)
        {
            foreach (Border label in _compass)
            {
                label.Visibility = Visibility.Collapsed;
            }

            return;
        }

        string language = LocalizationManager.Instance.Language;
        if (_compass.Count == 0 || language != _compassLanguage)
        {
            foreach (Border old in _compass)
            {
                _canvas.Children.Remove(old);
            }

            _compass.Clear();
            _compassLanguage = language;
            string[] names = Compass.Names();
            for (int i = 0; i < 8; i++)
            {
                bool main = i % 2 == 0;
                var label = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0xB4, 0x0E, 0x12, 0x18)), CornerRadius = new CornerRadius(3), Padding = new Thickness(3, 0, 3, 1),
                    IsHitTestVisible = false,
                    Child = new TextBlock
                    {
                        Text = names[i], FontSize = main ? 11.5 : 10, FontWeight = i == 0 ? FontWeights.Bold : FontWeights.SemiBold,
                        Foreground = i == 0 ? RouteGold : new SolidColorBrush(main ? Color.FromRgb(0xE4, 0xEA, 0xEE) : Color.FromRgb(0xA9, 0xB6, 0xBF)),
                    },
                };
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Panel.SetZIndex(label, 40); // over the marks and the route, under the buttons
                _compass.Add(label);
                _canvas.Children.Add(label);
            }
        }

        const double inset = 3;
        for (int i = 0; i < 8; i++)
        {
            Border label = _compass[i];
            double w = label.DesiredSize.Width, h = label.DesiredSize.Height;
            double angle = i * Math.PI / 4;
            double dx = Math.Sin(angle), dy = -Math.Cos(angle); // north up, east right
            double x, y;
            if (Round)
            {
                double r = Math.Min(cx, cy) - Math.Max(w, h) / 2 - inset;
                (x, y) = (cx + dx * r, cy + dy * r);
            }
            else
            {
                double left = inset + w / 2, right = vw - inset - w / 2, top = inset + h / 2, bottom = vh - inset - h / 2;
                x = Math.Abs(dx) < 1e-9 ? cx : dx > 0 ? right : left;
                y = Math.Abs(dy) < 1e-9 ? cy : dy > 0 ? bottom : top;
            }

            Canvas.SetLeft(label, x - w / 2);
            Canvas.SetTop(label, y - h / 2);
            label.Opacity = alpha;
            label.Visibility = Visibility.Visible;
        }
    }

    /// <summary>The pets that may get an arrow on the edge (null: any); on the route, those of the stop being farmed.</summary>
    public IReadOnlySet<int>? ArrowPets { get; set; }

    /// <summary>At most this many pet arrows, the nearest pets first.</summary>
    public const int ArrowLimit = 8;

    private IReadOnlyList<(double X, double Y, double Radius, int Step)> _route = Array.Empty<(double, double, double, int)>();
    private static readonly Brush RouteGold = PetMapPalette.BrushOf(Color.FromRgb(0xE8, 0xC3, 0x5A));
    private static readonly Brush RouteGoldSoft = PetMapPalette.BrushOf(Color.FromArgb(0x14, 0xE8, 0xC3, 0x5A));
    private static readonly Brush RouteDark = PetMapPalette.BrushOf(Color.FromArgb(0xE6, 0x10, 0x14, 0x1A));

    /// <summary>The pet route to draw (world units): the stop being farmed first, then the next ones. Empty: no route. <paramref name="firstName"/>: where the
    /// stop being farmed is (its area), written beside the arrow and, on the full map, beside the stop.</summary>
    public void SetRoute(IReadOnlyList<(double X, double Y, double Radius, int Step)> stops, (double X, double Y)? next = null, string? firstName = null,
        IReadOnlyList<string>? stopTexts = null, string? firstKibelisk = null)
    {
        _route = stops;
        _next = next;
        _firstName = firstName;
        _stopTexts = stopTexts;
        _firstKibelisk = firstKibelisk;
    }

    private string? _firstName;

    /// <summary>The Kibelisk to reach the stop being farmed by, as the label says it in brackets ("Kibelisk: Basfelt Ruins · 120 m").</summary>
    private string? _firstKibelisk;
    private IReadOnlyList<string>? _stopTexts;

    /// <summary>What a pet's dot is (the pet, the monsters that give it, how far the collection is): shown when the mouse rests on a dot. Live: one of its monsters is there now.</summary>
    public Func<int, bool, string?>? Describe { get; set; }

    /// <summary>The name of a collectible (by its key, the English name), for the same.</summary>
    public Func<string, string?>? DescribeGather { get; set; }

    /// <summary>A mark that tells what it is: a stop (its text), a pet (its id), or a collectible (its key, named only when the mouse rests on it).</summary>
    private readonly record struct Hoverable(double X, double Y, double R, int PetId, bool Live, string? Text, string? GatherKey = null);

    /// <summary>For the pictures of the tests (no mouse there): "live" shows the label of the first live monster, "stop" that of the first stop.</summary>
    public string? HoverForPicture { get; set; }

    /// <summary>The marks drawn in the last frame that tell what they are when the mouse rests on them.</summary>
    private readonly List<Hoverable> _hoverables = new();
    private Border? _hoverTip;
    private TextBlock? _hoverText;

    /// <summary>
    /// The mouse over a dot, a diamond or a stop's number: a label beside it says what it is. The overlay lets every click through to the game (it never
    /// gets the mouse), so the cursor's place is read from Windows on every frame instead; on the app's own map page the same.
    /// </summary>
    private void ShowHover(double vw, double vh, Point? cursor)
    {
        Hoverable? best = null;
        double bestSq = double.MaxValue;
        if (HoverForPicture is { } pick)
        {
            // the pictures of the tests: no mouse there, the label of the first live monster (or of the first stop)
            best = _hoverables.Where(h => pick == "stop" ? h.Text is not null && h.PetId == 0 : h.Live).Cast<Hoverable?>().FirstOrDefault();
        }
        else if (cursor is { } c)
        {
            foreach (Hoverable h in _hoverables)
            {
                double sq = (h.X - c.X) * (h.X - c.X) + (h.Y - c.Y) * (h.Y - c.Y);
                if (sq <= h.R * h.R && sq < bestSq - 1)
                {
                    best = h;
                    bestSq = sq;
                }
            }
        }

        string? text = best is { } b ? b.Text ?? (b.GatherKey is { } key ? DescribeGather?.Invoke(key) ?? key : Describe?.Invoke(b.PetId, b.Live)) : null;
        if (best is not { } shown || string.IsNullOrEmpty(text))
        {
            if (_hoverTip is not null)
            {
                _hoverTip.Visibility = Visibility.Collapsed;
            }

            return;
        }

        if (_hoverTip is null)
        {
            _hoverText = new TextBlock { FontSize = 11.5, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, MaxWidth = 230 };
            _hoverTip = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x10, 0x14, 0x1A)), BorderBrush = RouteGold, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 3, 6, 3), IsHitTestVisible = false, Child = _hoverText,
            };
            Panel.SetZIndex(_hoverTip, 50);
            _canvas.Children.Add(_hoverTip);
        }

        _hoverText!.Text = text;
        _hoverTip.Visibility = Visibility.Visible;
        _hoverTip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Size size = _hoverTip.DesiredSize;
        // beside the mark, towards the middle of the view so it stays inside
        double left = shown.X + 12 + size.Width <= vw - 3 ? shown.X + 12 : shown.X - 12 - size.Width;
        double top = shown.Y + 10 + size.Height <= vh - 3 ? shown.Y + 10 : shown.Y - 10 - size.Height;
        Canvas.SetLeft(_hoverTip, Math.Clamp(left, 3, Math.Max(3, vw - size.Width - 3)));
        Canvas.SetTop(_hoverTip, Math.Clamp(top, 3, Math.Max(3, vh - size.Height - 3)));
    }

    /// <summary>The mouse rests on the map (to read a mark or zoom): it stays up then even while the game shows the cursor as for a menu.</summary>
    public bool CursorOnMap() => CursorOnCanvas() is not null;

    /// <summary>Where the mouse is on the map's canvas (null when the map is not on the screen or the mouse is not on it).</summary>
    private Point? CursorOnCanvas()
    {
        if (CursorAnywhere() is not { } p)
        {
            return null;
        }

        return p.X >= 0 && p.Y >= 0 && p.X <= _canvas.ActualWidth && p.Y <= _canvas.ActualHeight ? p : null;
    }

    /// <summary>Where the mouse is in the canvas's own units, also beyond its edge (a drag of the free map goes on there); null when the map is not on the screen.</summary>
    private Point? CursorAnywhere()
    {
        if (PresentationSource.FromVisual(_canvas) is null || !_canvas.IsVisible || !NativeCursor.TryGet(out int x, out int y))
        {
            return null;
        }

        try
        {
            return _canvas.PointFromScreen(new Point(x, y));
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Zoomed far out (<see cref="FarMetres"/>), up to the whole map: the route's stops are labelled as on a full map and the player is ringed.</summary>
    public bool Whole { get; private set; }

    /// <summary>As many stops as the route draws: close up the next six, zoomed out up to twelve.</summary>
    public const int RouteBadges = 12;

    /// <summary>At the stop: the nearest of its pets' monsters (or spawn points), which the line leads to instead of the stop.</summary>
    private (double X, double Y)? _next;

    /// <summary>Where a ray from the middle towards (dx, dy) leaves the view, a little inside it: on the square's edge, or on the circle when round.</summary>
    private (double X, double Y) EdgeOf(double cx, double cy, double dx, double dy)
    {
        if (Round)
        {
            double r = Math.Min(cx, cy) - 13, len = Math.Max(Math.Sqrt(dx * dx + dy * dy), 1e-6);
            return (cx + dx / len * r, cy + dy / len * r);
        }

        double t = Math.Min((cx - 13) / Math.Max(Math.Abs(dx), 1e-6), (cy - 13) / Math.Max(Math.Abs(dy), 1e-6));
        return (cx + dx * t, cy + dy * t);
    }

    /// <summary>
    /// The route: a solid gold line from the player to the stop being farmed (at the stop: to the nearest of its pets), a dashed one on from there through
    /// the next stops, the stop being farmed with its reach and its number in gold, the next ones numbered, and, when the stop is out of view, a gold arrow on
    /// the edge towards it with its number, its area and the distance. <paramref name="at"/> puts a world place on the canvas; <paramref name="me"/> is the player there.
    /// </summary>
    private void DrawRoute(Func<double, double, Point> at, Point me, double worldX, double worldY, double ppu, double cx, double cy, double vw, double vh)
    {
        // the parts are made once and moved on every frame (20 a second): new shapes with shadows every frame made the map stutter
        RouteParts parts = _routeShapes ??= NewRouteParts();
        foreach (UIElement part in parts.All)
        {
            part.Visibility = Visibility.Collapsed;
        }

        if (_route.Count == 0)
        {
            return;
        }

        // at the stop: a solid line to the nearest of its pets
        if (_next is { } next)
        {
            Point n = at(next.X, next.Y);
            parts.NextLine.X1 = me.X;
            parts.NextLine.Y1 = me.Y;
            parts.NextLine.X2 = n.X;
            parts.NextLine.Y2 = n.Y;
            parts.NextLine.Visibility = Visibility.Visible;
            Canvas.SetLeft(parts.NextTarget, n.X - 13);
            Canvas.SetTop(parts.NextTarget, n.Y - 13);
            parts.NextTarget.Visibility = Visibility.Visible;
        }

        bool round = Round && !Whole;
        bool Inside(double x, double y, double margin) => round
            ? (x - cx) * (x - cx) + (y - cy) * (y - cy) <= (Math.Min(cx, cy) - margin) * (Math.Min(cx, cy) - margin)
            : x >= margin && y >= margin && x <= vw - margin && y <= vh - margin;
        var shown = _route.Take(parts.Badges.Count).Select(st => (Stop: st, P: at(st.X, st.Y))).ToList();
        if (shown.Count > 1)
        {
            // the way on from the stop being farmed: dashed
            var points = new PointCollection(shown.Count);
            foreach (var (_, pt) in shown)
            {
                points.Add(pt);
            }

            parts.Path.Points = points;
            parts.Path.Visibility = Visibility.Visible;
        }

        if (_next is null)
        {
            // the way to the stop being farmed: solid, from the player
            parts.Leg.X1 = me.X;
            parts.Leg.Y1 = me.Y;
            parts.Leg.X2 = shown[0].P.X;
            parts.Leg.Y2 = shown[0].P.Y;
            parts.Leg.Visibility = Visibility.Visible;
        }

        for (int i = 0; i < shown.Count; i++)
        {
            var (stop, pt) = shown[i];
            if (!Inside(pt.X, pt.Y, 6))
            {
                continue;
            }

            double reach = Math.Max(12, stop.Radius * 100 * ppu);
            if (i == 0 && reach < Math.Min(cx, cy) * 0.8)
            {
                // the stop's reach, while it fits in the view (zoomed out: the number alone)
                parts.Ring.Width = parts.Ring.Height = 2 * reach;
                Canvas.SetLeft(parts.Ring, pt.X - reach);
                Canvas.SetTop(parts.Ring, pt.Y - reach);
                parts.Ring.Visibility = Visibility.Visible;
            }

            Border badge = parts.Badges[i];
            ((TextBlock)badge.Child).Text = stop.Step.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Canvas.SetLeft(badge, pt.X - badge.Width / 2);
            Canvas.SetTop(badge, pt.Y - badge.Height / 2);
            badge.Visibility = Visibility.Visible;
            if (_stopTexts is { } texts && i < texts.Count)
            {
                _hoverables.Insert(0, new Hoverable(pt.X, pt.Y, badge.Width / 2 + 2, 0, false, texts[i])); // the stops first: they lie over the dots
            }
        }

        var (first, fp) = shown[0];
        double metres = Math.Sqrt((first.X - worldX) * (first.X - worldX) + (first.Y - worldY) * (first.Y - worldY)) / 100.0;
        string name = _firstName is { Length: > 0 } n0 ? (n0.Length > 22 ? n0[..21] + "\u2026" : n0) : "";
        if (!Inside(fp.X, fp.Y, 6))
        {
            // the stop being farmed out of view: a gold arrow on the edge, its number, where it is and how far (pointed from the player; in free move,
            // where the player may be anywhere, from the view's middle, where the arrow's ray starts)
            Point origin = Follow ? me : new Point(cx, cy);
            double dx = fp.X - origin.X, dy = fp.Y - origin.Y, angle = Math.Atan2(dy, dx);
            var (tipX, tipY) = EdgeOf(cx, cy, dx, dy);
            parts.Arrow.RenderTransform = new RotateTransform(angle * 180 / Math.PI);
            Canvas.SetLeft(parts.Arrow, tipX);
            Canvas.SetTop(parts.Arrow, tipY);
            parts.Arrow.Visibility = Visibility.Visible;
            parts.LabelText.Text = RouteLabel(first.Step, name, metres);
            parts.Label.Visibility = Visibility.Visible;
            parts.Label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size sz = parts.Label.DesiredSize;
            Canvas.SetLeft(parts.Label, Math.Clamp(tipX - 30 * Math.Cos(angle) - sz.Width / 2, 3, Math.Max(3, vw - sz.Width - 3)));
            Canvas.SetTop(parts.Label, Math.Clamp(tipY - 30 * Math.Sin(angle) - sz.Height / 2, 3, Math.Max(3, vh - sz.Height - 3)));
        }
        else if (Whole && _next is null)
        {
            // on the full map: the stop's area and how far, beside it
            parts.LabelText.Text = RouteLabel(first.Step, name, metres);
            parts.Label.Visibility = Visibility.Visible;
            parts.Label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size sz = parts.Label.DesiredSize;
            bool left = fp.X + 16 + sz.Width > vw - 3;
            Canvas.SetLeft(parts.Label, Math.Clamp(left ? fp.X - 16 - sz.Width : fp.X + 16, 3, Math.Max(3, vw - sz.Width - 3)));
            Canvas.SetTop(parts.Label, Math.Clamp(fp.Y - sz.Height / 2, 3, Math.Max(3, vh - sz.Height - 3)));
        }
    }

    /// <summary>
    /// The label of the stop being farmed: "[13] Neglected Crematorium" - the stop's number and the Kibelisk to teleport to, so one can teleport there and walk
    /// to the monsters - and below it the area and how far it is. Without a Kibelisk (an island that has none) the area takes its place.
    /// </summary>
    private string RouteLabel(int step, string area, double metres) => _firstKibelisk is { Length: > 0 } kib
        ? string.Format(System.Globalization.CultureInfo.CurrentCulture, "[{0}] {1}\n{2}{3:0} m", step, kib, area.Length > 0 ? area + " · " : "", metres)
        : string.Format(System.Globalization.CultureInfo.CurrentCulture, "[{0}] {1}{2:0} m", step, area.Length > 0 ? area + " · " : "", metres);

    private sealed class RouteParts
    {
        public required Polyline Path { get; init; }
        public required Ellipse Ring { get; init; }
        public required List<Border> Badges { get; init; }
        public required Polygon Arrow { get; init; }
        public required Border Label { get; init; }
        public required TextBlock LabelText { get; init; }
        public required Line NextLine { get; init; }
        public required Ellipse NextTarget { get; init; }
        public required Line Leg { get; init; }

        public IEnumerable<UIElement> All => new UIElement[] { Path, Ring, Arrow, Label, NextLine, NextTarget, Leg }.Concat(Badges);
    }

    private RouteParts? _routeShapes;

    private RouteParts NewRouteParts()
    {
        var shadow = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 0, Color = Colors.Black, Opacity = 0.9 };
        shadow.Freeze();
        var labelText = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = RouteGold };
        var parts = new RouteParts
        {
            Path = new Polyline { Stroke = RouteGold, StrokeThickness = 2.2, Opacity = 0.75, StrokeDashArray = new DoubleCollection { 3, 2.5 }, IsHitTestVisible = false },
            Ring = new Ellipse { Stroke = RouteGold, StrokeThickness = 1.6, StrokeDashArray = new DoubleCollection { 4, 3 }, Fill = RouteGoldSoft, IsHitTestVisible = false },
            Badges = Enumerable.Range(0, RouteBadges).Select(i =>
            {
                double size = i == 0 ? 22 : 17;
                return new Border
                {
                    Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = i == 0 ? RouteGold : RouteDark, BorderBrush = RouteGold, BorderThickness = new Thickness(1.6),
                    IsHitTestVisible = false, Effect = shadow,
                    Child = new TextBlock { FontSize = i == 0 ? 12 : 10.5, FontWeight = FontWeights.Bold, Foreground = i == 0 ? RouteDark : RouteGold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                };
            }).ToList(),
            Arrow = new Polygon
            {
                Points = new PointCollection { new Point(13, 0), new Point(-9, -9), new Point(-4, 0), new Point(-9, 9) }, Fill = RouteGold, Stroke = RouteDark, StrokeThickness = 1.4,
                IsHitTestVisible = false, Effect = shadow,
            },
            Label = new Border { Background = RouteDark, BorderBrush = RouteGold, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 1, 5, 1), IsHitTestVisible = false, Child = labelText },
            LabelText = labelText,
            NextLine = new Line { Stroke = RouteGold, StrokeThickness = 2.6, IsHitTestVisible = false, Effect = shadow },
            NextTarget = new Ellipse { Width = 26, Height = 26, Stroke = RouteGold, StrokeThickness = 2.4, IsHitTestVisible = false },
            Leg = new Line { Stroke = RouteGold, StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false, Effect = shadow },
        };
        void Add(UIElement e, int z)
        {
            Panel.SetZIndex(e, z);
            e.Visibility = Visibility.Collapsed;
            _canvas.Children.Add(e);
        }

        Add(parts.Ring, 2);
        Add(parts.Path, 3);
        Add(parts.Leg, 4);
        Add(parts.NextLine, 4);
        foreach (Border badge in Enumerable.Reverse(parts.Badges))
        {
            Add(badge, 7);
        }

        Add(parts.Arrow, 8);
        Add(parts.Label, 8);
        Add(parts.NextTarget, 9);
        return parts;
    }

    public PetMapWindow() : base("Aion DPS Pet Map", new Border { Child = null }, Load, Save, SystemParameters.WorkArea.Right - ViewSize - 40, 120, LoadSize, SaveSize, OverlayEdgeMode.CropAnchored)
    {
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                WakeCompass(); // the map came up: the compass for a moment
            }
        };

        // the wheel over the map zooms it (on the overlay while the cursor rests on it, see TakeMouseWhileOver; on the app's page always)
        _canvas.MouseWheel += (_, e) =>
        {
            if (ZoomRequested is { } zoom && e.Delta != 0)
            {
                // in free move around the cursor: the place under it stays there (following, around the player as before)
                _zoomFocus = Follow ? null : e.GetPosition(_canvas);
                zoom(-e.Delta / 120.0 * ZoomStep);
                e.Handled = true;
            }
        };
        // The overlay locked in place (the padlock shut; the map on the app's page always): the left button drags the map inside it to look anywhere, a double
        // click comes back to the player. It gets the mouse as the wheel does (the overlay while the cursor rests on it). Unlocked, the press is left to the frame,
        // which moves the window as before.
        _canvas.MouseLeftButtonDown += (_, e) =>
        {
            if (!IsLocked || ZoomRequested is null || _framePpu <= 0)
            {
                return;
            }

            if (e.ClickCount >= 2)
            {
                Follow = true; // back to the player
                _pressAt = null;
                e.Handled = true;
                return;
            }

            Point at = e.GetPosition(_canvas);
            _pressAt = at;
            _pressFollowed = Follow;
            Follow = false; // the view stays where it is and goes with the mouse from here (the frame takes the view shown till now as its start)
            _grab = PetMapPan.WorldAt(at.X, at.Y, _frameMidX, _frameMidY, _frameCx, _frameCy, _framePpu);
            _canvas.CaptureMouse();
            e.Handled = true;
        };
        _canvas.MouseLeftButtonUp += (_, e) =>
        {
            if (_grab is { } grab)
            {
                Point at = e.GetPosition(_canvas);
                if (_pressFollowed && _pressAt is { } from && Math.Abs(at.X - from.X) + Math.Abs(at.Y - from.Y) < 5)
                {
                    Follow = true; // a click, not a drag: the view goes on following the player
                }
                else
                {
                    // where the button was let go (the next frame keeps it on the land)
                    _freeMid = PetMapPan.MiddleHolding(grab.X, grab.Y, at.X, at.Y, _frameCx, _frameCy, _framePpu);
                    EndDrag();
                }

                _pressAt = null;
                e.Handled = true;
            }
        };
        // over the map's picture (-2), under the live monsters (0) and the player's arrow (added last on every frame)
        _spawnMarks = new MarkPool(_canvas, -1);
        _gatherMarks = new MarkPool(_canvas, -1);
        _liveMarks = new MarkPool(_canvas, 0);
        _me = new Polygon
        {
            Points = new PointCollection { new Point(0, -11), new Point(8, 9), new Point(0, 5), new Point(-8, 9) },
            Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xBA, 0x42)), Stroke = Brushes.Black, StrokeThickness = 1,
        };
        Canvas.SetLeft(_me, ViewSize / 2);
        Canvas.SetTop(_me, ViewSize / 2);
        _canvas.Children.Add(_me);
        _meUnknown = new TextBlock
        {
            Text = "?", Width = 28, TextAlignment = TextAlignment.Center, FontSize = 26, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xBA, 0x42)),
            IsHitTestVisible = false, Visibility = Visibility.Collapsed,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 0, Color = Colors.Black, Opacity = 1 },
        };
        Canvas.SetLeft(_meUnknown, ViewSize / 2 - 14);
        Canvas.SetTop(_meUnknown, ViewSize / 2 - 20);
        _canvas.Children.Add(_meUnknown);
        Body = _canvas;
    }

    private static (double?, double?) LoadSize()
    {
        var s = MeterSettings.Load();
        return (s.PetMapWidth, s.PetMapHeight);
    }

    private static void SaveSize(double? width, double? height)
    {
        var s = MeterSettings.Load();
        s.PetMapWidth = width;
        s.PetMapHeight = height;
        s.Save();
    }

    private static (double?, double?, double) Load()
    {
        var s = MeterSettings.Load();
        return (s.PetMapLeft, s.PetMapTop, s.PetMapScale);
    }

    private static void Save(double left, double top, double scale)
    {
        var s = MeterSettings.Load();
        s.PetMapLeft = left;
        s.PetMapTop = top;
        s.PetMapScale = scale;
        s.Save();
    }

    /// <summary>How many tile pictures the last render put on the map (for tests).</summary>
    public int TilesShown => _tileImages.Count(i => i.Visibility == Visibility.Visible);

    public string TileDebug => string.Join(" ", _tiles.Select(kv => kv.Key + (kv.Value is null ? "=null" : "=ok")));

    /// <summary>How many tile pixels the cache keeps: a frame needs about ten million at most (close up four big tiles, zoomed out nine smaller ones).</summary>
    private const long TilePixelBudget = 24_000_000;
    private long _tilePixels;

    /// <summary>A tile decoded <paramref name="width"/> pixels wide (close up the full picture, zoomed out a smaller one), kept for the next frames.</summary>
    private BitmapSource? Tile(Aion2MapInfo map, int col, int row, int width)
    {
        string key = $"{map.Key}/{col}_{row}@{width}";
        if (_tiles.TryGetValue(key, out var cached))
        {
            return cached;
        }

        BitmapSource? bitmap = MapTiles.Load(map, col, row, width);
        _tiles[key] = bitmap;
        _tileOrder.Add(key);
        _tilePixels += PixelsOf(bitmap);
        while ((_tilePixels > TilePixelBudget || _tileOrder.Count > 160) && _tileOrder.Count > 1)
        {
            _tilePixels -= PixelsOf(_tiles[_tileOrder[0]]);
            _tiles.Remove(_tileOrder[0]);
            _tileOrder.RemoveAt(0);
        }

        return bitmap;
    }

    private static long PixelsOf(BitmapSource? bitmap) => bitmap is null ? 0 : (long)bitmap.PixelWidth * bitmap.PixelHeight;

    /// <summary>
    /// Draws the map around the player (<paramref name="worldX"/>, <paramref name="worldY"/>): close up (<paramref name="zoom"/> 0) <paramref name="radiusMetres"/>
    /// to each side, zoomed out (up to 1) further in even steps until the whole map fits; the marks of <paramref name="points"/> in their pets' colours
    /// (<paramref name="colors"/> by pet id), the collectibles, the monsters and cubes there right now, the guards and the route.
    /// </summary>
    public void Render(Aion2MapInfo map, double worldX, double worldY, double radiusMetres, double opacity,
        IReadOnlyList<(int PetId, float X, float Y)> points, IReadOnlyList<(int PetId, float X, float Y)> live, IReadOnlyDictionary<int, Color> colors,
        IReadOnlyList<(string Kind, string Key, float X, float Y)>? gather = null, bool positionKnown = true, bool petArrows = true, bool gatherArrows = true,
        IReadOnlyList<(float X, float Y)>? guards = null, double zoom = 0)
    {
        // the visible part of the canvas (the frame may be pulled smaller than the view): close up the player stays in the middle of it
        double vw = Math.Clamp(BodyWidth, 40, ViewSize), vh = Math.Clamp(BodyHeight, 40, ViewSize);
        double cx = vw / 2, cy = vh / 2;
        _hoverables.Clear();
        if (map.Key != _frameMap)
        {
            _frameMap = map.Key;
            Recentre(); // another map: a free view starts again from the player
        }

        zoom = double.IsFinite(zoom) ? Math.Clamp(zoom, 0, 1) : 0;
        if (Math.Abs(zoom - _compassZoom) > 0.001 || map.Key != _compassMap)
        {
            _compassZoom = zoom;
            _compassMap = map.Key;
            WakeCompass(); // zoomed, or another map: the compass for a moment
        }

        double close = Math.Max(radiusMetres, 10) * 100.0; // the close-up's radius in world units

        // zoomed out: the radius grows from the close-up's to what shows the whole land (its picture's extent; without tiles that of the spawn points);
        // in free move the view's middle is kept on the same land
        var land = zoom > 0 ? LandOf(map) : null;
        (double X0, double Y0, double W, double H) extent = zoom > 0 || !Follow ? ExtentOf(map, points, worldX, worldY) : default;
        double radius = close, midX = worldX, midY = worldY;
        if (zoom > 0)
        {
            // round, the land's corners must fit in the circle too
            double fit = Round ? Math.Min(vw, vh) / Math.Sqrt(extent.W * extent.W + extent.H * extent.H) : Math.Min((vw - 10) / extent.W, (vh - 10) / extent.H);
            double whole = ViewSize / 2 / fit;
            if (whole > close)
            {
                radius = close * Math.Pow(whole / close, zoom); // even steps: every notch of the wheel multiplies the radius by the same factor
            }

            // the view's middle: the player close up; zoomed out it is drawn towards where the view shows the most land (the land's middle at zoom 1),
            // so a map zoomed out near its edge is not half empty
            double halfW = cx * radius / (ViewSize / 2), halfH = cy * radius / (ViewSize / 2);
            double fx = 2 * halfW >= extent.W ? extent.X0 + extent.W / 2 : Math.Clamp(worldX, extent.X0 + halfW, extent.X0 + extent.W - halfW);
            double fy = 2 * halfH >= extent.H ? extent.Y0 + extent.H / 2 : Math.Clamp(worldY, extent.Y0 + halfH, extent.Y0 + extent.H - halfH);
            midX = worldX + (fx - worldX) * zoom;
            midY = worldY + (fy - worldY) * zoom;
        }

        double ppu = ViewSize / 2 / radius; // view pixels per world unit
        if (!Follow)
        {
            // free move: the view's middle is where the map was dragged to. The one place the pan enters: everything below (the picture, the marks, the
            // arrows, the guards, the route, the player) is placed from midX and midY.
            (midX, midY) = FreeMiddle(midX, midY, ppu, cx, cy, extent);
        }

        _frameMidX = midX;
        _frameMidY = midY;
        _frameCx = cx;
        _frameCy = cy;
        _framePpu = ppu;
        double metres = radius / 100.0;
        Point At(double x, double y) => new(cx + (x - midX) * ppu, cy + (y - midY) * ppu);
        Point me = At(worldX, worldY);
        Whole = metres >= FarMetres || zoom >= 0.99; // (a small map, the Abyss, is whole before it is that far out)

        // the picture: close up the tiles (decoded smaller as the map zooms out); far out the land in one picture instead of dozens of tiles.
        // Only the marks (opacity 0, laid over the game's minimap): no picture at all.
        // It lies where maps.json puts it. From 5 November 2026 (morning) it was moved by the minimap reader's calibration (PictureAt); learnt from a few
        // reports in one corner of the map, its line moved the whole picture elsewhere on the far side (the owner, the afternoon: "the map teleports to a
        // completely different part"), so the picture went back to the map's own numbers; the arrow and the marks are the game's coordinates either way.
        double k = ppu / map.Scale;            // view pixels per native map pixel
        double kx = k, ky = k;
        double tileOnScreen = map.Tile * k;    // how wide a tile is in the view
        bool landPicture = land is not null && tileOnScreen < 256;
        int used = 0;
        if (opacity > 0 && !landPicture)
        {
            int decode = Math.Min(map.Tile, tileOnScreen > 640 ? 1536 : 768);
            var (nx, ny) = Aion2Maps.ToNative(map, midX, midY); // the view's middle on the picture
            double halfNative = ViewSize / 2 / k; // generous: the tiles beyond the visible part are clipped
            int c0 = Math.Max(0, (int)Math.Floor((nx - halfNative) / map.Tile)), c1 = Math.Min(map.Grid - 1, (int)Math.Floor((nx + halfNative) / map.Tile));
            int r0 = Math.Max(0, (int)Math.Floor((ny - halfNative) / map.Tile)), r1 = Math.Min(map.Grid - 1, (int)Math.Floor((ny + halfNative) / map.Tile));
            for (int col = c0; col <= c1; col++)
            {
                for (int row = r0; row <= r1; row++)
                {
                    var source = Tile(map, col, row, decode);
                    if (source is null)
                    {
                        continue;
                    }

                    if (used >= _tileImages.Count)
                    {
                        var image = new Image { Stretch = Stretch.Fill, SnapsToDevicePixels = false };
                        Panel.SetZIndex(image, -2); // under the danger zones of the guards and everything else
                        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                        _tileImages.Add(image);
                        _canvas.Children.Insert(used, image);
                    }

                    var img = _tileImages[used++];
                    img.Source = source;
                    img.Opacity = opacity;
                    img.Width = map.Tile * kx;
                    img.Height = map.Tile * ky;
                    Canvas.SetLeft(img, cx + (col * map.Tile - nx) * kx);
                    Canvas.SetTop(img, cy + (row * map.Tile - ny) * ky);
                    img.Visibility = Visibility.Visible;
                }
            }
        }

        for (int i = used; i < _tileImages.Count; i++)
        {
            _tileImages[i].Visibility = Visibility.Collapsed;
        }

        if (opacity > 0 && landPicture && land is { } l)
        {
            if (_wholeImage is null)
            {
                _wholeImage = new Image { Stretch = Stretch.Fill, IsHitTestVisible = false };
                Panel.SetZIndex(_wholeImage, -2);
                RenderOptions.SetBitmapScalingMode(_wholeImage, BitmapScalingMode.HighQuality);
                _canvas.Children.Insert(0, _wholeImage);
            }

            Point corner = At(l.X0, l.Y0);
            _wholeImage.Source = l.Image;
            _wholeImage.Opacity = opacity;
            _wholeImage.Width = l.W * ppu;
            _wholeImage.Height = l.H * ppu;
            Canvas.SetLeft(_wholeImage, corner.X);
            Canvas.SetTop(_wholeImage, corner.Y);
            _wholeImage.Visibility = Visibility.Visible;
        }
        else if (_wholeImage is not null)
        {
            _wholeImage.Visibility = Visibility.Collapsed;
        }

        // the marks' sizes: close up the portraits and icons in full, further out smaller, far out the spawn points as dots
        int detail = Whole ? 2 : metres <= 400 ? 0 : 1;
        double spawnSize = SpawnSizes[detail], liveSize = LiveSizes[detail], gatherSize = GatherSizes[detail], cubeSpotSize = CubeSpotSizes[detail], liveCubeSize = LiveCubeSizes[detail];

        // the spawn points of the chosen pets: the pet's portrait in a ring of its colour (zoomed out, one mark where several fall on the same spot)
        _spawnMarks.Begin();
        _spawnCells.Clear();
        foreach ((int pet, float x, float y) in points)
        {
            Point pt = At(x, y);
            if (pt.X < 5 || pt.Y < 5 || pt.X > vw - 5 || pt.Y > vh - 5)
            {
                continue;
            }

            if (detail > 0 && !_spawnCells.Add((pet, (int)Math.Floor(pt.X / (spawnSize * 0.75)), (int)Math.Floor(pt.Y / (spawnSize * 0.75)))))
            {
                continue;
            }

            _spawnMarks.Place(PetMapMarks.Pet(pet, colors.GetValueOrDefault(pet, Colors.White), spawnSize, false), pt.X, pt.Y, spawnSize);
            _hoverables.Add(new Hoverable(pt.X, pt.Y, Math.Max(5, spawnSize / 2 + 1), pet, false, null));
        }

        _spawnMarks.End();

        // collectibles: the icon of what they give on a square tile edged in their kind's colour (the pets are round); the Hidden Cubes' places small cubes
        _gatherMarks.Begin();
        _gatherCells.Clear();
        foreach ((string kind, string key, float x, float y) in gather ?? Array.Empty<(string, string, float, float)>())
        {
            Point pt = At(x, y);
            if (pt.X < 6 || pt.Y < 6 || pt.X > vw - 6 || pt.Y > vh - 6)
            {
                continue;
            }

            double size = kind == "Cube" ? cubeSpotSize : gatherSize;
            if (detail > 0 && !_gatherCells.Add((key, (int)Math.Floor(pt.X / (size * 0.75)), (int)Math.Floor(pt.Y / (size * 0.75)))))
            {
                continue;
            }

            _gatherMarks.Place(PetMapMarks.Gather(kind, key, size), pt.X, pt.Y, size);
            _hoverables.Add(new Hoverable(pt.X, pt.Y, size / 2 + 1, 0, false, null, key));
        }

        _gatherMarks.End();

        // the monsters really around the player (announced by the game): larger, edged in white, over the general spawn points; a Hidden Cube there a glowing cube
        _liveMarks.Begin();
        foreach ((int pet, float x, float y) in live)
        {
            Point pt = At(x, y);
            if (pt.X < 8 || pt.Y < 8 || pt.X > vw - 8 || pt.Y > vh - 8)
            {
                continue;
            }

            double size = pet == CubeMark ? liveCubeSize : liveSize;
            _liveMarks.Place(pet == CubeMark ? PetMapMarks.Cube(size, true) : PetMapMarks.Pet(pet, colors.GetValueOrDefault(pet, Colors.White), size, true), pt.X, pt.Y, size);
            _hoverables.Add(new Hoverable(pt.X, pt.Y, size / 2 + 1, pet, true, null));
        }

        _liveMarks.End();

        // a chosen pet with nothing in view: an arrow on the edge, in its colour, towards its nearest spawn point
        int arrow = 0;
        double halfX = cx, halfY = cy;
        // one pass over all points (not one pass per pet): per pet whether one is in view, and the nearest one out of view
        var seen = new Dictionary<int, (bool Visible, double BestSq, double Bx, double By)>();
        void Consider(int id, float x, float y)
        {
            if (!petArrows || !colors.ContainsKey(id))
            {
                return;
            }

            if (!seen.TryGetValue(id, out var st))
            {
                st = (false, double.MaxValue, 0, 0);
            }
            else if (st.Visible)
            {
                return; // one in view is enough: no arrow for this pet
            }

            double dx = (x - midX) * ppu, dy = (y - midY) * ppu;
            if (Math.Abs(dx) < halfX - 5 && Math.Abs(dy) < halfY - 5)
            {
                st.Visible = true;
            }
            else
            {
                double sq = dx * dx + dy * dy;
                if (sq < st.BestSq)
                {
                    st.BestSq = sq;
                    st.Bx = dx;
                    st.By = dy;
                }
            }

            seen[id] = st;
        }

        foreach (var (id, x, y) in points)
        {
            Consider(id, x, y);
        }

        foreach (var (id, x, y) in live)
        {
            Consider(id, x, y);
        }

        void PlaceArrow(Color color, double bx, double by)
        {
            var (edgeX, edgeY) = EdgeOf(halfX, halfY, bx, by); // where the ray to the point meets the edge (the square, or the circle when round)
            if (arrow >= _edgeArrows.Count)
            {
                var a = new Polygon { Points = new PointCollection { new Point(11, 0), new Point(-8, -8), new Point(-4, 0), new Point(-8, 8) }, Stroke = Brushes.White, StrokeThickness = 1.6, IsHitTestVisible = false, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 0, Color = Colors.Black, Opacity = 0.9 } };
                _edgeArrows.Add(a);
                _canvas.Children.Add(a);
            }

            var ar = _edgeArrows[arrow++];
            ar.Fill = PetMapPalette.BrushOf(color);
            ar.RenderTransform = new RotateTransform(Math.Atan2(by, bx) * 180 / Math.PI);
            Canvas.SetLeft(ar, edgeX);
            Canvas.SetTop(ar, edgeY);
            ar.Visibility = Visibility.Visible;
        }

        // only the nearest few pets get an arrow (with every pet still needed on the map there would be dozens), on the route those of the stop being farmed
        var arrows = new List<(Color Color, double Sq, double Bx, double By)>();
        foreach (var (pet, color) in colors)
        {
            if (!seen.TryGetValue(pet, out var st) || (ArrowPets is { } only && !only.Contains(pet)))
            {
                continue;
            }

            if (st.Visible || st.BestSq == double.MaxValue)
            {
                continue;
            }

            arrows.Add((color, st.BestSq, st.Bx, st.By));
        }

        foreach (var (color, _, bx, by) in arrows.OrderBy(a => a.Sq).Take(ArrowLimit))
        {
            PlaceArrow(color, bx, by);
        }

        // gatherables: one arrow per kind with none in view, towards its nearest point
        if (gatherArrows && gather is not null)
        {
            var kinds = new Dictionary<string, (bool Visible, double BestSq, double Bx, double By)>();
            foreach (var (kind, _, x, y) in gather)
            {
                kinds.TryGetValue(kind, out var st);
                if (st == default)
                {
                    st = (false, double.MaxValue, 0, 0);
                }

                if (st.Visible)
                {
                    continue;
                }

                double dx = (x - midX) * ppu, dy = (y - midY) * ppu;
                if (Math.Abs(dx) < halfX - 5 && Math.Abs(dy) < halfY - 5)
                {
                    st.Visible = true;
                }
                else if (dx * dx + dy * dy < st.BestSq)
                {
                    st = (false, dx * dx + dy * dy, dx, dy);
                }

                kinds[kind] = st;
            }

            foreach (var (kind, st) in kinds)
            {
                if (!st.Visible && st.BestSq != double.MaxValue)
                {
                    PlaceArrow(PetMapPalette.OfGather(kind), st.Bx, st.By);
                }
            }
        }

        for (int i = arrow; i < _edgeArrows.Count; i++)
        {
            _edgeArrows[i].Visibility = Visibility.Collapsed;
        }

        DrawGuards(guards, worldX, worldY, midX, midY, ppu, cx, cy, vw, vh);
        DrawRoute((x, y) => At(x, y), me, worldX, worldY, ppu, cx, cy, vw, vh);

        // round: the view is a circle (the minimap's shape)
        _canvas.Clip = Round ? new EllipseGeometry(new Point(cx, cy), cx, cy) : null;
        DrawCompass(cx, cy, vw, vh);

        // zoomed far out: the map's name at the top and the player's arrow in a white ring, so he is found at a glance
        if (Whole && _wholeTitle is null)
        {
            _wholeTitle = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xC8, 0x10, 0x14, 0x1A)), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2, 6, 2), IsHitTestVisible = false,
                Child = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = RouteGold },
            };
            Panel.SetZIndex(_wholeTitle, 9);
            _canvas.Children.Add(_wholeTitle);
        }

        if (_wholeTitle is not null)
        {
            ((TextBlock)_wholeTitle.Child).Text = map.Title;
            _wholeTitle.Visibility = Whole ? Visibility.Visible : Visibility.Collapsed;
            if (Whole)
            {
                // in the corner; round, at the top in the middle (the corner is cut off)
                _wholeTitle.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                // (beside the compass's NW in the corner; round, under its N)
                double besideCompass = ShowCompass && _compass.Count == 8 ? _compass[7].DesiredSize.Width + 8 : 0;
                Canvas.SetLeft(_wholeTitle, Round ? cx - _wholeTitle.DesiredSize.Width / 2 : 6 + besideCompass);
                Canvas.SetTop(_wholeTitle, Round ? (ShowCompass ? 26 : 10) : 6);
            }
        }

        if (Whole && _halo is null)
        {
            _halo = new Ellipse { Width = 30, Height = 30, Stroke = Brushes.White, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xBA, 0x42)), IsHitTestVisible = false };
            _canvas.Children.Add(_halo);
        }

        if (_halo is not null)
        {
            Canvas.SetLeft(_halo, me.X - 15);
            Canvas.SetTop(_halo, me.Y - 15);
            _halo.Visibility = Whole && positionKnown ? Visibility.Visible : Visibility.Collapsed;
            _canvas.Children.Remove(_halo);
            _canvas.Children.Add(_halo);
        }

        Canvas.SetLeft(_me, me.X);
        Canvas.SetTop(_me, me.Y);
        _me.RenderTransform = new RotateTransform(MeHeading);
        Canvas.SetLeft(_meUnknown, me.X - 14);
        Canvas.SetTop(_meUnknown, me.Y - 20);
        // the player on top; a "?" instead of the arrow when the position is not known (the view then shows the last one)
        _me.Visibility = positionKnown ? Visibility.Visible : Visibility.Collapsed;
        _meUnknown.Visibility = positionKnown ? Visibility.Collapsed : Visibility.Visible;
        _canvas.Children.Remove(_me);
        _canvas.Children.Add(_me);
        _canvas.Children.Remove(_meUnknown);
        _canvas.Children.Add(_meUnknown);

        // the mouse: the label of the mark under it, the zoom buttons while it rests on the map, and (the overlay) its wheel and clicks for that moment;
        // a drag of the free map keeps the mouse until the button is let go, also beyond the map's edge
        Point? cursor = CursorOnCanvas();
        bool over = (ZoomRequested is not null || LockRequested is not null) && (_grab is not null || (cursor is not null && (PlaceIsView || NativeCursor.Showing())));
        if (cursor is not null && NativeCursor.Showing())
        {
            WakeCompass(); // the mouse rests on the map: the compass stays while it does
        }
        TakeMouseWhileOver(over);
        // (the pictures of the tests show them, so that they can be checked); the padlock also stays while the view is free, so that is never forgotten
        PlaceZoomButtons(cx, cy, vw, vh, HoverForPicture is not null || (ZoomRequested is not null && (over || !IsLocked)), zoom);
        PlaceLockButton(cx, cy, vw, vh, HoverForPicture is not null || (LockRequested is not null && !PlaceIsView && (over || !IsLocked)));
        ShowHover(vw, vh, _grab is null ? cursor : null); // no labels flashing up while the map is dragged
    }

    /// <summary>The land's extent (world units): its picture's; without tiles that of the spawn points; without those a square of 1000 m around the player.</summary>
    private static (double X0, double Y0, double W, double H) ExtentOf(Aion2MapInfo map, IReadOnlyList<(int PetId, float X, float Y)> points, double worldX, double worldY)
    {
        var land = LandOf(map);
        (double X0, double Y0, double W, double H) extent = land is { } l0 ? (l0.X0, l0.Y0, l0.W, l0.H)
            : points.Count > 0 ? (points.Min(p => p.X), points.Min(p => p.Y), Math.Max(1000, points.Max(p => p.X) - points.Min(p => p.X)), Math.Max(1000, points.Max(p => p.Y) - points.Min(p => p.Y)))
            : (worldX - 50000, worldY - 50000, 100000, 100000);
        return extent;
    }

    /// <summary>
    /// Free move: the view's middle of this frame (world units). The first frame keeps the middle followed till then (<paramref name="followX"/>,
    /// <paramref name="followY"/>); a drag holds the place it took hold of under the cursor; a change of the zoom keeps the place under the cursor where it is (the
    /// wheel), or the view's middle (the buttons, the hotkeys); the view is kept on the <paramref name="land"/> (<see cref="PetMapPan.Clamp"/>).
    /// </summary>
    private (double X, double Y) FreeMiddle(double followX, double followY, double ppu, double cx, double cy, (double X0, double Y0, double W, double H) land)
    {
        (double X, double Y) mid = _freeMid ?? (followX, followY);
        Point? held = null;
        if (_grab is { } grab)
        {
            held = CursorAnywhere();
            if (held is { } at)
            {
                mid = PetMapPan.MiddleHolding(grab.X, grab.Y, at.X, at.Y, cx, cy, ppu);
            }
        }
        else if (_freeMid is not null && _framePpu > 0 && Math.Abs(ppu - _framePpu) > ppu * 1e-9)
        {
            Point focus = _zoomFocus ?? new Point(cx, cy);
            mid = PetMapPan.ZoomAround(mid.X, mid.Y, focus.X, focus.Y, cx, cy, _framePpu, ppu);
        }
        else
        {
            _zoomFocus = null; // the zoom has come to rest: the next one (buttons, hotkeys) is around the middle again, unless the wheel says otherwise
        }

        var kept = PetMapPan.Clamp(mid.X, mid.Y, land.X0, land.Y0, land.W, land.H, cx / ppu, cy / ppu);
        if (!double.IsFinite(kept.X) || !double.IsFinite(kept.Y))
        {
            kept = (followX, followY);
        }

        if (_grab is not null && held is { } cursor && (kept.X != mid.X || kept.Y != mid.Y))
        {
            // dragged against the land's edge: what is under the cursor now is held from here on, so dragging back moves the map at once
            _grab = PetMapPan.WorldAt(cursor.X, cursor.Y, kept.X, kept.Y, cx, cy, ppu);
        }

        if (_grab is not null && !NativeCursor.LeftButtonHeld())
        {
            EndDrag(); // let go beyond the map's window, which never heard of it
        }

        _freeMid = kept;
        return kept;
    }

    /// <summary>The top left corner of the zoom buttons (22 wide, 47 high): at the bottom right, on the circle when round.</summary>
    private (double Left, double Top) ZoomButtonsCorner(double cx, double cy, double vw, double vh)
    {
        const double width = 22, height = 47;
        if (Round)
        {
            // inside the circle, below its right end (with the compass a little higher: its SE sits at 45 degrees)
            double reach = Math.Min(cx, cy) - 30;
            return ShowCompass ? (cx + reach * 0.906 - width / 2, cy + reach * 0.423 - height / 2) : (cx + reach * 0.766 - width / 2, cy + reach * 0.643 - height / 2);
        }

        return (vw - width - 6, vh - height - 6 - (ShowCompass ? 20 : 0)); // above the compass's SE in the corner
    }

    /// <summary>
    /// The padlock just above the zoom buttons: the overlay's own lock. Shut, the overlay stays in place and the left mouse button drags the map inside it to look
    /// around (the wheel zooms around the cursor, a double click comes back to the player); open (gold), the overlay can be moved and sized. A click switches.
    /// </summary>
    private void PlaceLockButton(double cx, double cy, double vw, double vh, bool shown)
    {
        if (!shown)
        {
            if (_lockButton is not null)
            {
                _lockButton.Visibility = Visibility.Collapsed;
            }

            return;
        }

        if (_lockButton is null)
        {
            _lockBody = new System.Windows.Shapes.Path { Data = PadlockBody, IsHitTestVisible = false };
            _lockShackle = new System.Windows.Shapes.Path { StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false };
            var glyph = new Canvas { Width = 14, Height = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            glyph.Children.Add(_lockShackle);
            glyph.Children.Add(_lockBody);
            _lockButton = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(4), Background = RouteDark, BorderBrush = RouteGold, BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand, Child = glyph,
            };
            _lockButton.MouseLeftButtonDown += (_, e) =>
            {
                LockRequested?.Invoke(!IsLocked);
                e.Handled = true; // not a drag of the map or of the unlocked overlay
            };
            Panel.SetZIndex(_lockButton, 45); // as the zoom buttons
            _canvas.Children.Add(_lockButton);
        }

        if (_lockShows != IsLocked)
        {
            bool shut = IsLocked;
            _lockShows = shut;
            _lockButton.Background = shut ? RouteDark : RouteGold;
            _lockBody!.Fill = shut ? RouteGold : RouteDark;
            _lockShackle!.Stroke = shut ? RouteGold : RouteDark;
            _lockShackle.Data = shut ? PadlockShut : PadlockOpen;
            _lockButton.ToolTip = LocalizationManager.Instance[shut ? "PetMap.FollowLocked" : "PetMap.FreeMove"];
        }

        var (left, top) = ZoomButtonsCorner(cx, cy, vw, vh);
        Canvas.SetLeft(_lockButton, left);
        Canvas.SetTop(_lockButton, top - 25);
        _lockButton.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// The + and − buttons of the zoom, shown while the mouse rests on the map (and while it is being placed): at the bottom right, on the circle when round.
    /// The one that cannot zoom further is dimmed.
    /// </summary>
    private void PlaceZoomButtons(double cx, double cy, double vw, double vh, bool shown, double zoom)
    {
        if (!shown)
        {
            if (_zoomButtons is not null)
            {
                _zoomButtons.Visibility = Visibility.Collapsed;
            }

            return;
        }

        if (_zoomButtons is null)
        {
            Border ZoomButton(string glyph, double change, string tipKey)
            {
                var button = new Border
                {
                    Width = 22, Height = 22, CornerRadius = new CornerRadius(4), Background = RouteDark, BorderBrush = RouteGold, BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand, ToolTip = LocalizationManager.Instance[tipKey], Margin = new Thickness(0, 0, 0, 3),
                    Child = new TextBlock { Text = glyph, FontSize = 15, FontWeight = FontWeights.Bold, Foreground = RouteGold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -2, 0, 0) },
                };
                button.MouseLeftButtonDown += (_, e) =>
                {
                    _zoomFocus = null; // in free move around the view's middle
                    ZoomRequested?.Invoke(change);
                    e.Handled = true; // not a drag of the unlocked overlay (nor of the free map)
                };
                return button;
            }

            var stack = new StackPanel();
            stack.Children.Add(ZoomButton("+", -ZoomStep, "PetMap.ZoomIn"));
            stack.Children.Add(ZoomButton("\u2212", ZoomStep, "PetMap.ZoomOut"));
            _zoomButtons = new Border { Child = stack };
            Panel.SetZIndex(_zoomButtons, 45); // over the marks and the route, under the label of the mark under the mouse
            _canvas.Children.Add(_zoomButtons);
        }

        var buttons = (StackPanel)_zoomButtons.Child;
        buttons.Children[0].Opacity = zoom <= 0.001 ? 0.45 : 1;
        buttons.Children[1].Opacity = zoom >= 0.999 ? 0.45 : 1;
        var (left, top) = ZoomButtonsCorner(cx, cy, vw, vh);
        Canvas.SetLeft(_zoomButtons, left);
        Canvas.SetTop(_zoomButtons, top);
        _zoomButtons.Visibility = Visibility.Visible;
    }

    private Image? _wholeImage;
    private Ellipse? _halo;
    private Border? _wholeTitle;

    /// <summary>The land picture of each map and where it lies (world units), made once per map (every tile at 256 pixels): the picture zoomed far out.</summary>
    private static readonly Dictionary<string, (BitmapSource Image, double X0, double Y0, double W, double H)?> Lands = new();

    private static (BitmapSource Image, double X0, double Y0, double W, double H)? LandOf(Aion2MapInfo map)
    {
        if (!Lands.TryGetValue(map.Key, out var land))
        {
            land = MapTiles.Land(map, 256) is { } l
                ? (l.Image, (l.Col0 * map.Tile - map.OffsetX) / map.Scale, (l.Row0 * map.Tile - map.OffsetY) / map.Scale, l.Cols * map.Tile / map.Scale, l.Rows * map.Tile / map.Scale)
                : null;
            Lands[map.Key] = land;
        }

        return land;
    }

    private sealed class GuardMark
    {
        public Ellipse Zone = null!, Badge = null!;
        public TextBlock Skull = null!, Label = null!;
        public Polygon Arrow = null!;
        public Border LabelBack = null!;

        public void Hide()
        {
            Zone.Visibility = Badge.Visibility = Skull.Visibility = Arrow.Visibility = LabelBack.Visibility = Visibility.Collapsed;
        }
    }

    private readonly List<GuardMark> _guardMarks = new();
    private static readonly Brush GuardRed = new SolidColorBrush(Color.FromRgb(0xE5, 0x1F, 0x1F));
    private static readonly Brush GuardZoneFill = new SolidColorBrush(Color.FromArgb(0x48, 0xFF, 0x26, 0x26));
    private static readonly Brush GuardZoneEdge = new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0x3B, 0x3B));
    private static readonly Brush GuardLabelBack = new SolidColorBrush(Color.FromArgb(0xB8, 0x10, 0x10, 0x14));
    private static readonly FontFamily SymbolFont = new("Segoe UI Symbol");

    private GuardMark NewGuardMark()
    {
        var effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 0, Color = Colors.Black, Opacity = 0.9 };
        var mark = new GuardMark
        {
            Zone = new Ellipse { Fill = GuardZoneFill, Stroke = GuardZoneEdge, StrokeThickness = 1.2, IsHitTestVisible = false },
            Badge = new Ellipse { Width = 19, Height = 19, Fill = GuardRed, Stroke = Brushes.White, StrokeThickness = 1.8, IsHitTestVisible = false, Effect = effect },
            Skull = new TextBlock { Text = "\u2620", FontFamily = SymbolFont, FontWeight = FontWeights.Bold, FontSize = 12, Foreground = Brushes.White, Width = 19, TextAlignment = TextAlignment.Center, IsHitTestVisible = false },
            Arrow = new Polygon { Points = new PointCollection { new Point(11, 0), new Point(-8, -8), new Point(-4, 0), new Point(-8, 8) }, Fill = GuardRed, Stroke = Brushes.White, StrokeThickness = 1.6, IsHitTestVisible = false, Effect = effect },
            Label = new TextBlock { FontFamily = SymbolFont, FontWeight = FontWeights.SemiBold, FontSize = 11, Foreground = GuardRed, IsHitTestVisible = false },
        };
        mark.LabelBack = new Border { Background = GuardLabelBack, CornerRadius = new CornerRadius(4), Padding = new Thickness(4, 1, 4, 1), Child = mark.Label, IsHitTestVisible = false };
        Panel.SetZIndex(mark.Zone, -1);
        foreach (UIElement e in new UIElement[] { mark.Badge, mark.Skull, mark.Arrow, mark.LabelBack })
        {
            Panel.SetZIndex(e, 6);
        }

        foreach (UIElement e in new UIElement[] { mark.Zone, mark.Badge, mark.Skull, mark.Arrow, mark.LabelBack })
        {
            _canvas.Children.Add(e);
        }

        return mark;
    }

    /// <summary>
    /// The militia guards the game announced around the player (Stacy's idea, her fork 17 October 2026): in view a see-through red danger zone of 20 m under a red badge with a white skull,
    /// out of view a red arrow on the edge towards it with the distance beside it.
    /// </summary>
    /// <remarks>(<paramref name="worldX"/>, <paramref name="worldY"/>) is the player, for the distance; (<paramref name="midX"/>, <paramref name="midY"/>) the view's middle
    /// (zoomed out it moves away from the player), where the marks are placed from.</remarks>
    private void DrawGuards(IReadOnlyList<(float X, float Y)>? guards, double worldX, double worldY, double midX, double midY, double ppu, double cx, double cy, double vw, double vh)
    {
        int used = 0;
        foreach ((float gx, float gy) in guards ?? Array.Empty<(float, float)>())
        {
            GuardMark mark = used < _guardMarks.Count ? _guardMarks[used] : NewGuardMark();
            if (used >= _guardMarks.Count)
            {
                _guardMarks.Add(mark);
            }

            used++;
            double dx = (gx - midX) * ppu, dy = (gy - midY) * ppu;
            double px = cx + dx, py = cy + dy;
            if (px >= 8 && py >= 8 && px <= vw - 8 && py <= vh - 8)
            {
                double zone = Math.Max(14, Aion2Guards.ZoneMetres * 100 * ppu);
                mark.Zone.Width = mark.Zone.Height = zone * 2;
                Canvas.SetLeft(mark.Zone, px - zone);
                Canvas.SetTop(mark.Zone, py - zone);
                Canvas.SetLeft(mark.Badge, px - 9.5);
                Canvas.SetTop(mark.Badge, py - 9.5);
                Canvas.SetLeft(mark.Skull, px - 9.5);
                Canvas.SetTop(mark.Skull, py - 9);
                mark.Zone.Visibility = mark.Badge.Visibility = mark.Skull.Visibility = Visibility.Visible;
                mark.Arrow.Visibility = mark.LabelBack.Visibility = Visibility.Collapsed;
                continue;
            }

            // out of view: the arrow on the edge, the distance beside it (towards the middle)
            double angle = Math.Atan2(dy, dx);
            var (tipX, tipY) = EdgeOf(cx, cy, dx, dy);
            mark.Arrow.RenderTransform = new RotateTransform(angle * 180 / Math.PI);
            Canvas.SetLeft(mark.Arrow, tipX);
            Canvas.SetTop(mark.Arrow, tipY);
            double metres = Math.Sqrt((gx - worldX) * (double)(gx - worldX) + (gy - worldY) * (double)(gy - worldY)) / 100.0;
            mark.Label.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, "\u2620 {0:0} m", metres);
            mark.LabelBack.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size size = mark.LabelBack.DesiredSize;
            double lx = Math.Clamp(tipX - 28 * Math.Cos(angle) - size.Width / 2, 3, Math.Max(3, vw - size.Width - 3));
            double ly = Math.Clamp(tipY - 28 * Math.Sin(angle) - size.Height / 2, 3, Math.Max(3, vh - size.Height - 3));
            Canvas.SetLeft(mark.LabelBack, lx);
            Canvas.SetTop(mark.LabelBack, ly);
            mark.Zone.Visibility = mark.Badge.Visibility = mark.Skull.Visibility = Visibility.Collapsed;
            mark.Arrow.Visibility = mark.LabelBack.Visibility = Visibility.Visible;
        }

        for (int i = used; i < _guardMarks.Count; i++)
        {
            _guardMarks[i].Hide();
        }
    }
}

/// <summary>The list that belongs to the pet map: the chosen pets with their colour, name, level (or "new") and the distance to the nearest spawn point.</summary>
public sealed class PetListWindow : MovableOverlay
{
    private readonly StackPanel _rows = new() { MinWidth = 250, MinHeight = 60 };
    private string _shown = "";

    /// <summary>How many rows the player can see at most (a row is about 19 units high), plus a reserve: more are not worth building.</summary>
    public int RowCapacity => (int)Math.Ceiling(HeightLimit / 19.0) + 10;

    private readonly TextBlock _headTitle = new() { FontWeight = FontWeights.Bold, FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xC3, 0x5A)), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _headDetail = new() { FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(0x9D, 0xB3, 0xC2)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) };
    private readonly Border _head = new() { Visibility = Visibility.Collapsed, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 5), Margin = new Thickness(0, 0, 0, 4), MaxWidth = 380 };

    /// <summary>The Black Cloud shop's clock under the rows: when its product list renews, how long the one on offer has been there.</summary>
    private readonly TextBlock _shop = new()
    {
        FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xB2, 0xE8)), TextWrapping = TextWrapping.Wrap, MaxWidth = 380, Margin = new Thickness(0, 5, 0, 0),
        Visibility = Visibility.Collapsed,
    };

    public PetListWindow() : base("Aion DPS Pet List", new Border { Child = null }, Load, Save, SystemParameters.WorkArea.Right - 300, 580, LoadSize, SaveSize)
    {
        _head.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xE8, 0xC3, 0x5A));
        var head = new StackPanel();
        head.Children.Add(_headTitle);
        head.Children.Add(_headDetail);
        _head.Child = head;
        var stack = new StackPanel();
        stack.Children.Add(_head);
        stack.Children.Add(_rows);
        stack.Children.Add(_shop);
        var panel = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0x0E, 0x18, 0x20)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, 0x29, 0x45, 0x57)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6), Child = stack,
        };
        Body = panel;
    }

    /// <summary>The Black Cloud shop's clock under the rows (null: none).</summary>
    public void RenderShopClock(string? text)
    {
        _shop.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        if (_shop.Text != (text ?? ""))
        {
            _shop.Text = text ?? "";
        }
    }

    /// <summary>The route's head over the rows: the stop being farmed and how far (gold), what comes after; null: no head.</summary>
    public void RenderHead(string? title, string? detail)
    {
        _head.Visibility = title is null ? Visibility.Collapsed : Visibility.Visible;
        _headTitle.Text = title ?? "";
        _headDetail.Text = detail ?? "";
        _headDetail.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static (double?, double?) LoadSize()
    {
        var s = MeterSettings.Load();
        return (s.PetListWidth, s.PetListHeight);
    }

    private static void SaveSize(double? width, double? height)
    {
        var s = MeterSettings.Load();
        s.PetListWidth = width;
        s.PetListHeight = height;
        s.Save();
    }

    private static (double?, double?, double) Load()
    {
        var s = MeterSettings.Load();
        return (s.PetListLeft, s.PetListTop, s.PetListScale);
    }

    private static void Save(double left, double top, double scale)
    {
        var s = MeterSettings.Load();
        s.PetListLeft = left;
        s.PetListTop = top;
        s.PetListScale = scale;
        s.Save();
    }

    /// <summary>Rows: colour, name, level text, distance text (empty when unknown).</summary>
    public void Render(IReadOnlyList<(Color Color, string Name, string Level, string Distance, bool Diamond)> rows)
    {
        string key = string.Join("\n", rows.Select(r => $"{r.Color}|{r.Name}|{r.Level}|{r.Distance}|{r.Diamond}"));
        if (key == _shown)
        {
            return;
        }

        _shown = key;
        _rows.Children.Clear();
        foreach (var (color, name, level, distance, diamond) in rows)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Shape dot = diamond
                ? new Polygon { Points = new PointCollection { new Point(6, 0), new Point(12, 6), new Point(6, 12), new Point(0, 6) }, Fill = new SolidColorBrush(color), Stroke = Brushes.Black, StrokeThickness = 1.2, Width = 12, Height = 12, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center }
                : new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(color), Stroke = Brushes.White, StrokeThickness = 1.5, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            var n = new TextBlock { Text = name, FontWeight = FontWeights.Bold, FontSize = 12.5, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            var l = new TextBlock { Text = level, FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x7A)), VerticalAlignment = VerticalAlignment.Center };
            var d = new TextBlock { Text = distance, FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(0x9D, 0xB3, 0xC2)), VerticalAlignment = VerticalAlignment.Center, MinWidth = 52, TextAlignment = TextAlignment.Right, Margin = new Thickness(12, 0, 0, 0) };
            Grid.SetColumn(n, 1);
            Grid.SetColumn(l, 2);
            Grid.SetColumn(d, 3);
            grid.Children.Add(dot);
            grid.Children.Add(n);
            grid.Children.Add(l);
            grid.Children.Add(d);
            _rows.Children.Add(grid);
        }
    }
}
