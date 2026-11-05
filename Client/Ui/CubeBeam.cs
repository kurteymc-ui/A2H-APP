using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDPS.Aion2;

namespace AionDPS.Ui;

/// <summary>
/// The cube beam (5 November 2026, the owner: "a 3d shape of a cube or a beacon of light above the cubes for easier spotting ... of the ones that's claimable
/// near me"): while the game announces a Hidden Cube in range, a column of gold light stands over the game where that cube is on screen, with its distance;
/// while the cube is behind the camera an arrow at the screen's left or right edge says which way to turn. Where it is on screen comes from three things the
/// meter has anyway: the cube's place (the game's announcement), the player's (the map's arrow, <see cref="CubeRange"/>) and which way the camera looks (the
/// view cone on the game's minimap, <see cref="MinimapReader.Camera"/>, read about five times a second while a cube is near). Nothing is drawn into the game
/// and nothing is read from it: a narrow see-through window over it, click-through, never taking the focus, moved sideways. The place is close, not exact:
/// the camera's height and distance behind the player are not known, and the beam stands in front of hills and walls.
/// </summary>
internal sealed class CubeBeam
{
    /// <summary>How far behind the player the camera is taken to be (world units, 100 a metre): it turns a near cube's angle a little towards the middle.</summary>
    private const double CameraBehind = 500;

    /// <summary>How old the camera's direction may be for the beam to stand (the reader looks five times a second; a menu over the minimap stops it).</summary>
    private static readonly TimeSpan CameraFresh = TimeSpan.FromSeconds(1.5);

    private const double BeamWidth = 150;

    private readonly Func<Aion2EntityDirectory?> _directory;
    private readonly System.Windows.Threading.DispatcherTimer _watch = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly System.Windows.Threading.DispatcherTimer _frame = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private Window? _window;
    private NativeOverlay? _native;
    private Grid? _beam;
    private Grid? _arrow;
    private TextBlock? _beamText;
    private TextBlock? _arrowText;
    private System.Windows.Shapes.Path? _chevron;
    private MeterSettings _settings = MeterSettings.Load();
    private DateTime _settingsAt = DateTime.MinValue;
    private double _x = double.NaN;
    private int _side;

    public CubeBeam(Func<Aion2EntityDirectory?> directory)
    {
        _directory = directory;
        _watch.Tick += (_, _) => Watch();
        _frame.Tick += (_, _) => Frame();
        if (!MainWindow.Headless)
        {
            _watch.Start();
        }
    }

    /// <summary>Four times a second: a cube in range and the switch on starts the frames, else the beam goes.</summary>
    private void Watch()
    {
        if (DateTime.UtcNow - _settingsAt > TimeSpan.FromSeconds(1))
        {
            _settings = MeterSettings.Load();
            _settingsAt = DateTime.UtcNow;
        }

        bool wanted = _settings.CubeBeam && _settings.MinimapReading && !PrivacyConsent.Restricted && _directory() is { } directory && directory.LiveCubes().Count > 0;
        if (wanted && !_frame.IsEnabled)
        {
            _frame.Start();
        }
        else if (!wanted && _frame.IsEnabled)
        {
            _frame.Stop();
            Hide();
        }
    }

    private void Frame()
    {
        if (_directory() is not { } directory || directory.LiveCubes() is not { Count: > 0 } cubes || CubeRange.Nearest(directory, cubes) is not { } near
            || MinimapReader.Camera is not { } camera || DateTime.UtcNow - camera.At > CameraFresh || GameWindow.ForegroundClientArea() is not { } game)
        {
            Hide();
            return;
        }

        double fov = Math.Clamp(MinimapReader.CameraFov ?? camera.Width, 60 * Math.PI / 180, 140 * Math.PI / 180);
        double off = MinimapCone.Off(near.Dx, near.Dy, camera.Yaw, near.Metres * 100 > CameraBehind * 2 ? CameraBehind : 0);
        string metres = CubeRange.Text(near);
        EnsureWindow();
        Window window = _window!;
        if (PresentationSource.FromVisual(window)?.CompositionTarget is not { } target)
        {
            window.Show(); // its first frame: placed from the next one on
            return;
        }

        Point topLeft = target.TransformFromDevice.Transform(new Point(game.Left, game.Top));
        Point bottomRight = target.TransformFromDevice.Transform(new Point(game.Right, game.Bottom));
        double width = bottomRight.X - topLeft.X, height = bottomRight.Y - topLeft.Y;
        if (window.Height != height)
        {
            window.Height = height;
        }

        if (MinimapCone.ScreenX(off, fov, width) is double x)
        {
            // the beam over the cube's place, sliding there (a read comes five times a second; the frames in between move it a part of the way)
            _x = double.IsNaN(_x) || _side != 0 ? x : _x + ((x - _x) * 0.35);
            _side = 0;
            _beam!.Visibility = Visibility.Visible;
            _arrow!.Visibility = Visibility.Collapsed;
            _beamText!.Text = metres;
            // nearer is wider: from 40 px at 100 m to 110 px at a few metres
            double core = Math.Round(Math.Clamp(110 - (near.Metres * 0.7), 40, 110));
            ((FrameworkElement)_beam.Children[0]).Width = core;
            window.Width = BeamWidth;
            window.Left = topLeft.X + Math.Clamp(_x, 0, width) - (BeamWidth / 2);
        }
        else
        {
            // behind the camera: an arrow at the edge it is nearer to
            int side = off > 0 ? -1 : 1;
            _side = side;
            _x = double.NaN;
            _beam!.Visibility = Visibility.Collapsed;
            _arrow!.Visibility = Visibility.Visible;
            _arrowText!.Text = metres;
            _chevron!.RenderTransform = side < 0 ? new ScaleTransform(-1, 1, 14, 22) : Transform.Identity;
            _arrow.HorizontalAlignment = side < 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            window.Width = BeamWidth;
            window.Left = side < 0 ? topLeft.X + 6 : bottomRight.X - BeamWidth - 6;
        }

        window.Top = topLeft.Y;
        if (!window.IsVisible)
        {
            window.Show();
        }
    }

    private void Hide()
    {
        _x = double.NaN;
        if (_window is { IsVisible: true } window)
        {
            window.Hide();
        }
    }

    private void EnsureWindow()
    {
        if (_window is not null)
        {
            return;
        }

        var root = new Grid { IsHitTestVisible = false };
        _beam = Beam(out _beamText);
        _arrow = EdgeArrow(out _arrowText, out _chevron);
        root.Children.Add(_beam);
        root.Children.Add(_arrow);
        _window = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false, ShowActivated = false,
            ResizeMode = ResizeMode.NoResize, Focusable = false, IsHitTestVisible = false, Width = BeamWidth, Height = 400, Content = root,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000,
        };
        _window.SourceInitialized += (_, _) =>
        {
            _native = new NativeOverlay(_window);
            _native.SetClickThrough(true);
            AltTabHidden.Apply(_window);
        };
    }

    private static readonly Color Gold = Color.FromRgb(0xFF, 0xD3, 0x4D);

    /// <summary>The column of light with the cube and its distance near its top (also drawn by the pictures of the meter).</summary>
    internal static Grid Beam(out TextBlock distance)
    {
        var grid = new Grid();
        // the light: brightest in its middle, fading to the sides, and fading out at the top and bottom of the screen
        var light = new Border
        {
            Width = 70, HorizontalAlignment = HorizontalAlignment.Center,
            Background = new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(0x00, 0xFF, 0xC8, 0x40), 0), new(Color.FromArgb(0x55, 0xFF, 0xC8, 0x40), 0.30), new(Color.FromArgb(0xC0, 0xFF, 0xF1, 0xC0), 0.5),
                new(Color.FromArgb(0x55, 0xFF, 0xC8, 0x40), 0.70), new(Color.FromArgb(0x00, 0xFF, 0xC8, 0x40), 1),
            }, new Point(0, 0), new Point(1, 0)),
            OpacityMask = new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(0x00, 0, 0, 0), 0), new(Color.FromArgb(0x70, 0, 0, 0), 0.12), new(Color.FromArgb(0xD0, 0, 0, 0), 0.55),
                new(Color.FromArgb(0x60, 0, 0, 0), 0.80), new(Color.FromArgb(0x00, 0, 0, 0), 1),
            }, new Point(0, 0), new Point(0, 1)),
        };
        grid.Children.Add(light);
        // a thin bright core
        grid.Children.Add(new Border
        {
            Width = 3, HorizontalAlignment = HorizontalAlignment.Center, Background = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xF8, 0xE0)),
            OpacityMask = light.OpacityMask,
        });
        // the cube and the metres, a seventh of the way down
        var label = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 0, 0) };
        var cube = CubeToast.Cube(30);
        cube.HorizontalAlignment = HorizontalAlignment.Center;
        label.Children.Add(cube);
        distance = new TextBlock
        {
            FontSize = 15, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xF1, 0xC8)), HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0), Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 0, Opacity = 0.95, Color = Colors.Black },
        };
        label.Children.Add(distance);
        var holder = new Grid();
        holder.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        holder.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        holder.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6, GridUnitType.Star) });
        Grid.SetRow(label, 1);
        holder.Children.Add(label);
        grid.Children.Add(holder);
        return grid;
    }

    /// <summary>The arrow at the screen's edge while the cube is behind the camera: a gold chevron (pointing right; mirrored for the left edge) and the metres.</summary>
    internal static Grid EdgeArrow(out TextBlock distance, out System.Windows.Shapes.Path chevron)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        chevron = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M4,2 L24,22 L4,42"), Stroke = new SolidColorBrush(Gold), StrokeThickness = 6, StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Width = 28, Height = 44, HorizontalAlignment = HorizontalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 0, Opacity = 0.9, Color = Color.FromRgb(0x3B, 0x2A, 0x05) },
        };
        stack.Children.Add(chevron);
        var cube = CubeToast.Cube(22);
        cube.HorizontalAlignment = HorizontalAlignment.Center;
        cube.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(cube);
        distance = new TextBlock
        {
            FontSize = 14, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xF1, 0xC8)), HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0), Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 0, Opacity = 0.95, Color = Colors.Black },
        };
        stack.Children.Add(distance);
        var grid = new Grid { Visibility = Visibility.Collapsed, Width = 70 };
        grid.Children.Add(stack);
        return grid;
    }
}
