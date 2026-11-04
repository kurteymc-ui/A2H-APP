using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AionDPS.Ui;

/// <summary>
/// The meter over the game: the main window's compact panel (CompactOverlayPanel, moved in here at the start, so the main window's code goes on
/// filling it as before) in a borderless, see-through, always-on-top window of its own without a task bar button. The main window is the client
/// beside it (history, settings, the fight report) and can be put away to the tray while this one stays. MainWindow decides when it is on screen
/// (UpdateMeterOverlay): in instances and while the player fights; not between fights in the open world, never while another program is in front.
/// It fades in and out instead of popping.
/// </summary>
public sealed class MeterOverlayWindow : Window, IOverlayPlace
{
    private static readonly Duration FadeIn = new(TimeSpan.FromMilliseconds(160));
    private static readonly Duration FadeOut = new(TimeSpan.FromMilliseconds(260));

    private readonly FrameworkElement _panel;
    private NativeOverlay? _native;
    private bool _clickThrough;
    private bool _wanted;

    public MeterOverlayWindow(FrameworkElement panel, ResourceDictionary resources)
    {
        Title = Brand.AppName + " overlay";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false; // the game keeps the keyboard
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        // the main window's resources (its styles, converters and the overlay's background brush, which Settings replace): the panel keeps finding them
        Resources = resources;
        AltTabHidden.Apply(this);
        _panel = panel;
        panel.Visibility = Visibility.Visible;
        panel.Opacity = 0;
        Content = panel;
        PlaceFromSettings();
        // growing upwards (Meter look): the bottom edge stays where it is when lines come and go
        SizeChanged += (_, e) =>
        {
            if (!e.HeightChanged)
            {
                return;
            }

            if (_growUp && _bottom is double bottom && double.IsFinite(bottom))
            {
                Top = bottom - e.NewSize.Height;
            }
            else if (double.IsFinite(Top))
            {
                _bottom = Top + e.NewSize.Height;
            }
        };
        SourceInitialized += (_, _) =>
        {
            _native = new NativeOverlay(this);
            _native.SetClickThrough(_clickThrough);
        };
    }

    private bool _growUp;
    private double? _bottom;

    /// <summary>The meter grows upwards: its bottom edge stays put (Meter look).</summary>
    public bool GrowUp
    {
        get => _growUp;
        set
        {
            if (value && !_growUp && double.IsFinite(Top))
            {
                _bottom = Top + ActualHeight;
            }

            _growUp = value;
        }
    }

    /// <summary>Whether the overlay is meant to be on screen right now (it may still be fading out).</summary>
    public bool Wanted => _wanted;

    /// <summary>Fades the overlay in or out; hidden, the window is gone from the screen (and from the mouse).</summary>
    public void ShowOverlay(bool show)
    {
        if (show == _wanted)
        {
            return;
        }

        _wanted = show;
        if (show)
        {
            if (!IsVisible)
            {
                Show();
                _native?.SetClickThrough(_clickThrough); // the style is set again after a show
            }

            _panel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, FadeIn));
            return;
        }

        var fade = new DoubleAnimation(0, FadeOut);
        fade.Completed += (_, _) =>
        {
            if (!_wanted)
            {
                Hide();
            }
        };
        _panel.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Settings' "click-through panel": every click goes through the overlay to the game (then it cannot be dragged or opened).</summary>
    public void SetClickThrough(bool enabled)
    {
        _clickThrough = enabled;
        _native?.SetClickThrough(enabled);
    }

    /// <summary>Once a second: back to the front of the topmost band (a borderless full-screen game takes it when it gets the focus back).</summary>
    public void KeepOnTop()
    {
        if (IsVisible)
        {
            NativeOverlay.KeepOnTop(new WindowInteropHelper(this).Handle);
        }
    }

    /// <summary>The header was grabbed: the window follows the mouse until the button is let go, then its place is kept.</summary>
    public void DragFromHeader()
    {
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // the button was let go before the move began
        }

        SavePlace();
    }

    private void SavePlace()
    {
        var settings = MeterSettings.Load();
        settings.MeterOverlayLeft = Left;
        settings.MeterOverlayTop = Top;
        _bottom = Top + ActualHeight;
        settings.MeterOverlayBottom = _bottom;
        settings.Save();
    }

    // ---- the layout editor (OverlayLayout.cs): the scale is the compact panel's, which the main window keeps ----
    /// <summary>The panel's scale now, and setting it (and keeping it when the flag says so).</summary>
    public Func<double>? ScaleOf { get; set; }

    public Action<double, bool>? Rescale { get; set; }

    public bool PlaceIsView => false;

    public Rect PlaceBounds
    {
        get
        {
            Size size = OverlayMeasure.SizeOf(this);
            return new Rect(double.IsFinite(Left) ? Left : 0, double.IsFinite(Top) ? Top : 0, size.Width, size.Height);
        }
    }

    public double PlaceScale => ScaleOf?.Invoke() ?? 1.0;

    public void PlaceAt(double left, double top, bool save)
    {
        Left = left;
        Top = top;
        _bottom = top + ActualHeight;
        if (save)
        {
            SavePlace();
        }
    }

    public void PlaceScaleTo(double scale, bool save) => Rescale?.Invoke(scale, save);

    /// <summary>Where it was put, if that is still on a monitor; else at the right of the main monitor, below its top.</summary>
    internal void PlaceFromSettings()
    {
        var settings = MeterSettings.Load();
        Rect area = WindowPlacement.Primary();
        double left = area.Right - 430, top = area.Top + 140;
        if (settings.MeterOverlayLeft is double savedLeft && settings.MeterOverlayTop is double savedTop && double.IsFinite(savedLeft) && double.IsFinite(savedTop)
            && WindowPlacement.WorkAreas().Any(a => a.IntersectsWith(new Rect(savedLeft, savedTop, 120, 40))))
        {
            left = savedLeft;
            top = savedTop;
            // growing upwards, the bottom edge it was left with counts (the height is a different one at the start)
            if (settings.MeterGrowUp && settings.MeterOverlayBottom is double savedBottom && double.IsFinite(savedBottom))
            {
                _growUp = true;
                _bottom = savedBottom;
                top = savedBottom - ActualHeight;
            }
        }

        Left = left;
        Top = top;
    }
}
