using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AionDPS.Ui;

/// <summary>
/// An overlay the layout editor can place: where it is and how big (device-independent pixels of the whole desktop, like a window's Left/Top), and moving or
/// scaling it. Every overlay window has it (MovableOverlay and the overlays with a frame of their own); a copy that only lends its content to a tool page
/// (<see cref="PlaceIsView"/>) is never placed.
/// </summary>
public interface IOverlayPlace
{
    /// <summary>A window that only lends its content to a tool page: not an overlay on the screen.</summary>
    bool PlaceIsView { get; }

    /// <summary>Where it is and how big; measured when it was never shown.</summary>
    Rect PlaceBounds { get; }

    double PlaceScale { get; }

    /// <summary>Moves it (and keeps the place when <paramref name="save"/>: the editor saves once, when the mouse is let go).</summary>
    void PlaceAt(double left, double top, bool save);

    /// <summary>Scales it (each overlay keeps to its own limits) and keeps the scale when <paramref name="save"/>.</summary>
    void PlaceScaleTo(double scale, bool save);
}

/// <summary>An overlay on the layout editor: its name and its window.</summary>
public sealed record OverlayEntry(string Name, IOverlayPlace Place);

public static class OverlayMeasure
{
    /// <summary>The size a window takes on the screen: its own once it was laid out, else what its content asks for.</summary>
    public static Size SizeOf(Window window)
    {
        if (window.ActualWidth > 1 && window.ActualHeight > 1)
        {
            return new Size(window.ActualWidth, window.ActualHeight);
        }

        if (window.Content is UIElement content)
        {
            content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (content.DesiredSize.Width > 1 && content.DesiredSize.Height > 1)
            {
                return content.DesiredSize;
            }
        }

        return new Size(240, 120);
    }
}

/// <summary>
/// The layout editor: the monitors drawn small with a grid, every overlay that is switched on as a box where it is on them. Drag a box to move the
/// overlay, pull its corner to scale it; the overlay itself moves along at once and keeps its place when the mouse is let go. Snapping (on by default)
/// keeps places on a 10-pixel grid and catches the edges of a monitor. Read again every second while nothing is being dragged (overlays come and go).
/// </summary>
public sealed class OverlayLayoutEditor : Border
{
    private const double GridStep = 10, EdgeCatch = 14, MinScale = 0.5, MaxScale = 3.0;
    private readonly Func<IReadOnlyList<OverlayEntry>> _source;
    private readonly Canvas _canvas = new() { ClipToBounds = true };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private IReadOnlyList<Rect> _screens = Array.Empty<Rect>();
    private Rect _world = new(0, 0, 1920, 1080);
    private double _k = 0.4;
    private bool _dragging;
    private string? _selected;

    /// <summary>Places keep to a 10-pixel grid and catch the edges of a monitor.</summary>
    public bool Snap { get; set; } = true;

    /// <summary>An overlay was moved or scaled (after the mouse was let go).</summary>
    public event Action? Changed;

    public OverlayLayoutEditor(Func<IReadOnlyList<OverlayEntry>> source, IReadOnlyList<Rect>? screens = null)
    {
        _source = source;
        _screens = screens ?? WindowPlacement.Screens();
        _world = Union(_screens);
        CornerRadius = new CornerRadius(8);
        BorderThickness = new Thickness(1);
        SetResourceReference(BorderBrushProperty, "Brush.Border");
        Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x0C, 0x10));
        Padding = new Thickness(10);
        Child = _canvas;
        SizeChanged += (_, _) => Rebuild();
        _timer.Tick += (_, _) =>
        {
            if (!_dragging)
            {
                Rebuild();
            }
        };
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    private static Rect Union(IReadOnlyList<Rect> screens)
    {
        if (screens.Count == 0)
        {
            return new Rect(0, 0, 1920, 1080);
        }

        Rect all = screens[0];
        foreach (Rect r in screens.Skip(1))
        {
            all.Union(r);
        }

        return all;
    }

    /// <summary>As wide as it is given, as high as the monitors are for that width (at most 640).</summary>
    protected override Size MeasureOverride(Size constraint)
    {
        double width = double.IsInfinity(constraint.Width) ? 900 : constraint.Width;
        double inner = Math.Max(100, width - 22);
        double height = Math.Min(640, inner * _world.Height / Math.Max(1, _world.Width)) + 22;
        base.MeasureOverride(new Size(width, height));
        return new Size(width, height);
    }

    /// <summary>Draws the monitors and the overlays again where they are now.</summary>
    public void Rebuild()
    {
        double width = ActualWidth - 22, height = ActualHeight - 22;
        if (width <= 10 || height <= 10)
        {
            return;
        }

        _k = Math.Min(width / _world.Width, height / _world.Height);
        _canvas.Children.Clear();
        foreach (Rect screen in _screens)
        {
            _canvas.Children.Add(Monitor(screen));
        }

        IReadOnlyList<OverlayEntry> entries;
        try
        {
            entries = _source();
        }
        catch (InvalidOperationException)
        {
            return; // a window closing while it was asked
        }

        foreach (OverlayEntry entry in entries)
        {
            _canvas.Children.Add(Box(entry));
        }
    }

    private Point ToCanvas(double x, double y) => new((x - _world.Left) * _k, (y - _world.Top) * _k);

    /// <summary>A monitor: a darker area with a fine grid (every 100 pixels, every 500 a little brighter).</summary>
    private UIElement Monitor(Rect screen)
    {
        Point at = ToCanvas(screen.Left, screen.Top);
        double step = 100 * _k;
        var grid = new GeometryGroup();
        grid.Children.Add(new LineGeometry(new Point(0, 0), new Point(step, 0)));
        grid.Children.Add(new LineGeometry(new Point(0, 0), new Point(0, step)));
        var brush = new DrawingBrush(new GeometryDrawing(null, new Pen(new SolidColorBrush(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF)), 1), grid))
        {
            TileMode = TileMode.Tile, Viewport = new Rect(0, 0, step, step), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None,
        };
        var area = new Border
        {
            Width = screen.Width * _k, Height = screen.Height * _k, Background = new SolidColorBrush(Color.FromRgb(0x16, 0x14, 0x1B)), BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xDC, 0xB8, 0x77)), CornerRadius = new CornerRadius(3),
            Child = new Border { Background = brush, IsHitTestVisible = false }, IsHitTestVisible = false,
        };
        Canvas.SetLeft(area, at.X);
        Canvas.SetTop(area, at.Y);
        return area;
    }

    private enum Mode { None, Move, Scale }

    /// <summary>One overlay: a box in the accent colour with its name; the body moves it, the corner scales it.</summary>
    private UIElement Box(OverlayEntry entry)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        IOverlayPlace place = entry.Place;
        Rect bounds = place.PlaceBounds;
        Point at = ToCanvas(bounds.Left, bounds.Top);
        bool selected = _selected == entry.Name;
        var accent = (Application.Current?.TryFindResource("Brush.Accent") as SolidColorBrush)?.Color ?? Color.FromRgb(0xDC, 0xB8, 0x77);
        var box = new Border
        {
            Width = Math.Max(28, bounds.Width * _k), Height = Math.Max(16, bounds.Height * _k), CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(selected ? 2 : 1.25), BorderBrush = new SolidColorBrush(accent),
            Background = new SolidColorBrush(Color.FromArgb(selected ? (byte)0x55 : (byte)0x33, accent.R, accent.G, accent.B)),
            Cursor = Cursors.SizeAll, ToolTip = entry.Name + "\n" + loc["Shell.LayoutBoxTip"],
        };
        var label = new TextBlock
        {
            Text = entry.Name, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(5, 2, 10, 2), TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 3, ShadowDepth = 0, Opacity = 0.9 },
        };
        var grip = new Border
        {
            Width = 10, Height = 10, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(2, 0, 3, 0),
            Background = new SolidColorBrush(accent), Cursor = Cursors.SizeNWSE, ToolTip = loc["Shell.LayoutGripTip"],
        };
        var inside = new Grid();
        inside.Children.Add(label);
        inside.Children.Add(grip);
        box.Child = inside;
        Canvas.SetLeft(box, at.X);
        Canvas.SetTop(box, at.Y);

        Mode mode = Mode.None;
        Point start = default;
        double startScale = 1, lastScale = 1, lastLeft = bounds.Left, lastTop = bounds.Top;
        void Begin(MouseButtonEventArgs e, Mode how)
        {
            mode = how;
            start = e.GetPosition(_canvas);
            startScale = lastScale = place.PlaceScale;
            lastLeft = bounds.Left;
            lastTop = bounds.Top;
            _dragging = true;
            _selected = entry.Name;
            box.CaptureMouse();
            e.Handled = true;
        }

        grip.MouseLeftButtonDown += (_, e) => Begin(e, Mode.Scale);
        box.MouseLeftButtonDown += (_, e) => Begin(e, Mode.Move);
        box.MouseMove += (_, e) =>
        {
            if (mode == Mode.None || !box.IsMouseCaptured)
            {
                return;
            }

            Vector d = e.GetPosition(_canvas) - start;
            if (mode == Mode.Move)
            {
                (lastLeft, lastTop) = Fit(bounds.Left + d.X / _k, bounds.Top + d.Y / _k, bounds.Size);
                Point p = ToCanvas(lastLeft, lastTop);
                Canvas.SetLeft(box, p.X);
                Canvas.SetTop(box, p.Y);
                place.PlaceAt(lastLeft, lastTop, false);
                return;
            }

            // the corner: the box grows with the mouse, the overlay's scale by as much
            double w0 = Math.Max(1, bounds.Width * _k), h0 = Math.Max(1, bounds.Height * _k);
            double factor = Math.Max((w0 + d.X) / w0, (h0 + d.Y) / h0);
            lastScale = Math.Clamp(startScale * factor, MinScale, MaxScale);
            factor = lastScale / Math.Max(0.01, startScale);
            box.Width = Math.Max(28, bounds.Width * factor * _k);
            box.Height = Math.Max(16, bounds.Height * factor * _k);
            place.PlaceScaleTo(lastScale, false);
        };
        void End()
        {
            if (mode == Mode.None)
            {
                return;
            }

            Mode was = mode;
            mode = Mode.None;
            _dragging = false;
            if (was == Mode.Move)
            {
                place.PlaceAt(lastLeft, lastTop, true);
            }
            else
            {
                place.PlaceScaleTo(lastScale, true);
            }

            Changed?.Invoke();
            Dispatcher.BeginInvoke(new Action(Rebuild), DispatcherPriority.Background); // the overlay's own size after its new scale
        }

        box.MouseLeftButtonUp += (_, _) =>
        {
            if (box.IsMouseCaptured)
            {
                box.ReleaseMouseCapture();
            }

            End();
        };
        box.LostMouseCapture += (_, _) => End();
        return box;
    }

    /// <summary>A place on the grid, caught by a monitor's edges, and at least partly on a monitor.</summary>
    private (double Left, double Top) Fit(double left, double top, Size size)
    {
        if (Snap)
        {
            left = Math.Round(left / GridStep) * GridStep;
            top = Math.Round(top / GridStep) * GridStep;
            foreach (Rect s in _screens)
            {
                if (Math.Abs(left - s.Left) < EdgeCatch) { left = s.Left; }
                if (Math.Abs(left + size.Width - s.Right) < EdgeCatch) { left = s.Right - size.Width; }
                if (Math.Abs(top - s.Top) < EdgeCatch) { top = s.Top; }
                if (Math.Abs(top + size.Height - s.Bottom) < EdgeCatch) { top = s.Bottom - size.Height; }
            }
        }

        left = Math.Clamp(left, _world.Left - size.Width + 40, _world.Right - 40);
        top = Math.Clamp(top, _world.Top, _world.Bottom - 30);
        return (left, top);
    }
}
