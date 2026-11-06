using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AionDPS.History;

namespace AionDPS.Ui;

/// <summary>One boss of the note after an upload: the boss, your class, your rank there.</summary>
public sealed record RankToastLine(string Boss, string? ClassName, FightRanks.Own Rank);

/// <summary>
/// The note over the game a moment after a boss fight was uploaded (6 November 2026, "fight percentiles"): your rank on each boss, its number in the website's
/// colour, "78th percentile · 50k–60k CP Templars · rank 12 of 214". At the top of the screen, click-through, never taking the focus; it fades after a few
/// seconds. Switched by <see cref="MeterSettings.RankPopup"/> (on by default). A boss without a rank that means something yet isn't shown.
/// </summary>
public static class RankToast
{
    private static Window? _window;
    private static Border? _body;
    private static System.Windows.Threading.DispatcherTimer? _timer;
    private static NativeOverlay? _native;

    /// <summary>How long it stays.</summary>
    public static readonly TimeSpan Stay = TimeSpan.FromSeconds(9);

    /// <summary>Shows the note for the ranked bosses of <paramref name="lines"/>; nothing when none is ranked, off the game, or in a picture mode.</summary>
    public static void Show(IReadOnlyList<RankToastLine> lines)
    {
        var ranked = lines.Where(l => l.Rank.Ranked).ToList();
        if (ranked.Count == 0 || MainWindow.Headless || !GameWindow.OverlayMayShow())
        {
            return;
        }

        if (_window is null)
        {
            _body = new Border();
            _window = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false,
                ShowActivated = false, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, Focusable = false, Content = _body,
            };
            _window.SourceInitialized += (_, _) =>
            {
                _native = new NativeOverlay(_window);
                _native.SetClickThrough(true);
            };
            _timer = new System.Windows.Threading.DispatcherTimer { Interval = Stay };
            _timer.Tick += (_, _) =>
            {
                _timer.Stop();
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(500));
                fade.Completed += (_, _) =>
                {
                    if (!_timer.IsEnabled)
                    {
                        _window?.Hide();
                    }
                };
                _window?.BeginAnimation(UIElement.OpacityProperty, fade);
            };
        }

        _body!.Child = Build(ranked);
        _window.BeginAnimation(UIElement.OpacityProperty, null);
        _window.Opacity = 1;
        if (!_window.IsVisible)
        {
            _window.Show();
        }

        _window.UpdateLayout();
        Rect area = SystemParameters.WorkArea;
        if (GameWindow.ForegroundClientArea() is { } game && PresentationSource.FromVisual(_window)?.CompositionTarget is { } target)
        {
            // over the game, on whichever monitor it is (the work area is the main monitor's)
            area = new Rect(target.TransformFromDevice.Transform(new Point(game.Left, game.Top)), target.TransformFromDevice.Transform(new Point(game.Right, game.Bottom)));
        }

        _window.Left = area.Left + (area.Width - _window.ActualWidth) / 2;
        _window.Top = area.Top + area.Height * 0.2;   // below the cube's note (at 12 %)
        _timer!.Stop();
        _timer.Start();
    }

    /// <summary>The note itself (also its picture): a line per boss, at most four.</summary>
    public static FrameworkElement Build(IReadOnlyList<RankToastLine> lines)
    {
        var gold = new SolidColorBrush(Color.FromRgb(0xD9, 0xB3, 0x6A));
        var text = new SolidColorBrush(Color.FromRgb(0xF4, 0xE9, 0xCF));
        var muted = new SolidColorBrush(Color.FromRgb(0xA8, 0x9F, 0x92));
        var stack = new StackPanel { MinWidth = 300 };
        stack.Children.Add(new TextBlock
        {
            Text = lines.Count == 1 ? "YOUR RANK ON THIS BOSS" : "YOUR RANKS", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = gold, Margin = new Thickness(0, 0, 0, 6),
        });
        foreach (RankToastLine line in lines.Take(4))
        {
            FightRanks.Own r = line.Rank;
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var box = new Border
            {
                Width = 48, Height = 40, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 12, 0), Background = RankTier.Back(r.Percent),
                BorderBrush = RankTier.Brush(r.Percent), BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = RankTier.Number(r.Percent), FontSize = 20, FontWeight = FontWeights.Bold, Foreground = RankTier.Brush(r.Percent),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            row.Children.Add(box);
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(new TextBlock { Text = line.Boss, FontSize = 14.5, FontWeight = FontWeights.SemiBold, Foreground = text, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 380 });
            var detail = new TextBlock { FontSize = 11.5, Foreground = muted, Margin = new Thickness(0, 1, 0, 0) };
            detail.Inlines.Add(new System.Windows.Documents.Run(RankTier.Percentile(r.Percent)) { Foreground = RankTier.Brush(r.Percent), FontWeight = FontWeights.SemiBold });
            detail.Inlines.Add(new System.Windows.Documents.Run(" of " + RankTier.Bracket(r.Low, r.High) + RankTier.Many(line.ClassName)
                + " · rank " + r.Place.ToString("N0", CultureInfo.CurrentCulture) + " of " + r.Of.ToString("N0", CultureInfo.CurrentCulture)));
            words.Children.Add(detail);
            Grid.SetColumn(words, 1);
            row.Children.Add(words);
            stack.Children.Add(row);
        }

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x16, 0x14, 0x1A)), BorderBrush = gold, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 18, 10), Child = stack,
        };
    }
}
