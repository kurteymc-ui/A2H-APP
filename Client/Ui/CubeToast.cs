using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace AionDPS.Ui;

/// <summary>
/// The little note at the top of the screen when a Hidden Cube comes into range ("Hidden Cube nearby · 42 m · NE", with a gold cube): it stays over the game
/// for as long as the cube is in range, its distance and direction kept up to date (<see cref="Update"/>), and fades a moment after the cube is gone or
/// opened (<see cref="Release"/>); click-through, never taking the focus. Switched by <see cref="MeterSettings.CubePopup"/> (on by default).
/// </summary>
public static class CubeToast
{
    private static Window? _window;
    private static TextBlock? _text;
    private static System.Windows.Threading.DispatcherTimer? _timer;
    private static NativeOverlay? _native;
    private static FrameworkElement? _icon;

    /// <summary>Whether the note is up for a cube in range (from <see cref="Show"/> until <see cref="Release"/>).</summary>
    public static bool Active { get; private set; }

    /// <summary>Shows the note (again) with <paramref name="text"/>; it stays until <see cref="Release"/>.</summary>
    public static void Show(string text)
    {
        Active = true;
        if (MainWindow.Headless || !GameWindow.OverlayMayShow())
        {
            return; // only over the game, never on the desktop or over one of the game's windows
        }

        if (_window is null)
        {
            _text = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0xE9, 0xCF)), VerticalAlignment = VerticalAlignment.Center };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            _icon = Cube(22);
            row.Children.Add(_icon);
            _text.Margin = new Thickness(10, 0, 0, 0);
            row.Children.Add(_text);
            _window = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false,
                ShowActivated = false, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, Focusable = false,
                Content = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x16, 0x14, 0x1A)), BorderBrush = new SolidColorBrush(Color.FromRgb(0xD9, 0xB3, 0x6A)),
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 8, 16, 8), Child = row,
                },
            };
            _window.SourceInitialized += (_, _) =>
            {
                _native = new NativeOverlay(_window);
                _native.SetClickThrough(true);
            };
            _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _timer.Tick += (_, _) =>
            {
                _timer.Stop();
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(400));
                fade.Completed += (_, _) =>
                {
                    if (!_timer.IsEnabled && !Active)
                    {
                        _window?.Hide();
                    }
                };
                _window?.BeginAnimation(UIElement.OpacityProperty, fade);
            };
        }

        _timer!.Stop();
        _timer.Interval = TimeSpan.FromSeconds(1.5);
        _icon!.Visibility = Visibility.Visible;
        Put(text);
    }

    /// <summary>A short note of another kind in the same place, without the cube (the overlays unlocked or locked by their hotkey, the Black Cloud Merchant): it
    /// fades after <paramref name="seconds"/>. A cube's note in range is left alone.</summary>
    public static void Note(string text, double seconds = 3)
    {
        if (Active || MainWindow.Headless || !GameWindow.OverlayMayShow())
        {
            return;
        }

        Show(text);
        Active = false;
        _icon!.Visibility = Visibility.Collapsed;
        _timer!.Interval = TimeSpan.FromSeconds(seconds);
        _timer.Start();
    }

    /// <summary>The note's text now (the distance and direction as the player moves); shown only over the game, hidden while one of the game's windows is open.</summary>
    public static void Update(string text)
    {
        if (!Active || MainWindow.Headless)
        {
            return;
        }

        if (!GameWindow.OverlayMayShow())
        {
            if (_window is { IsVisible: true })
            {
                _window.Hide();
            }

            return;
        }

        if (_window is null)
        {
            Show(text); // the cube came while the game was not in front
            return;
        }

        Put(text);
    }

    /// <summary>The cube is gone (opened, or left behind): the note fades a moment later.</summary>
    public static void Release()
    {
        if (!Active)
        {
            return;
        }

        Active = false;
        if (_timer is not null && _window is { IsVisible: true })
        {
            _timer.Stop();
            _timer.Start();
        }
    }

    private static void Put(string text)
    {
        bool changed = _text!.Text != text;
        _text.Text = text;
        _window!.BeginAnimation(UIElement.OpacityProperty, null);
        _window.Opacity = 1;
        bool wasHidden = !_window.IsVisible;
        if (wasHidden)
        {
            _window.Show();
        }

        if (changed || wasHidden)
        {
            _window.UpdateLayout();
            Rect area = SystemParameters.WorkArea;
            _window.Left = area.Left + (area.Width - _window.ActualWidth) / 2;
            _window.Top = area.Top + area.Height * 0.12;
        }
    }

    /// <summary>A gold cube seen from above a corner: the top face light, the two sides darker.</summary>
    public static FrameworkElement Cube(double size)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        var edge = new SolidColorBrush(Color.FromRgb(0x5A, 0x43, 0x16));
        Polygon Face(string points, Color fill) => new() { Points = PointCollection.Parse(points), Fill = new SolidColorBrush(fill), Stroke = edge, StrokeThickness = 1, StrokeLineJoin = PenLineJoin.Round };
        canvas.Children.Add(Face("12,2 21,7 12,12 3,7", Color.FromRgb(0xFF, 0xE0, 0x8A)));
        canvas.Children.Add(Face("3,7 12,12 12,22 3,17", Color.FromRgb(0xD9, 0xA5, 0x3A)));
        canvas.Children.Add(Face("21,7 12,12 12,22 21,17", Color.FromRgb(0xB0, 0x7E, 0x22)));
        return new Viewbox { Width = size, Height = size, Child = canvas, VerticalAlignment = VerticalAlignment.Center };
    }
}
