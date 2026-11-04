using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>One pet of the merchant's pop-up: the pet, its lowest price on the pages read, its group.</summary>
public sealed record BlackCloudToastPet(int PetId, long Price, MerchantPetKind? Kind);

/// <summary>
/// The Black Cloud Merchants' pop-up over the game: the ticked pets the merchant offers the character now, each with its picture, its name, its group and its
/// price - every page of the shop read since its offers renewed together, so turning to another tab of the shop adds to it instead of replacing it. It shows
/// while the shop is open (the game's own window is over the game then, which hides the other overlays: this one stays), at the top of the screen,
/// click-through, never taking the focus, and fades 20 s after the last page came. Players asked for it this way (4 November 2026): the pictures and names,
/// not the levels and characters of the first version.
/// </summary>
public static class BlackCloudToast
{
    private static Window? _window;
    private static Border? _body;
    private static System.Windows.Threading.DispatcherTimer? _timer;
    private static NativeOverlay? _native;

    /// <summary>How long it stays after the last page of the shop came.</summary>
    public static readonly TimeSpan Stay = TimeSpan.FromSeconds(20);

    /// <summary>Shows (or updates) the pop-up for <paramref name="character"/> (empty: not known yet); nothing when <paramref name="pets"/> is empty.</summary>
    public static void Show(string character, IReadOnlyList<BlackCloudToastPet> pets)
    {
        if (pets.Count == 0 || MainWindow.Headless || !GameWindow.OverlayMayShow(ignoreMenu: true))
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

        _body!.Child = Build(character, pets);
        _window.BeginAnimation(UIElement.OpacityProperty, null);
        _window.Opacity = 1;
        if (!_window.IsVisible)
        {
            _window.Show();
        }

        _window.UpdateLayout();
        Rect area = SystemParameters.WorkArea;
        if (GameWindow.ForegroundClientArea(ignoreMenu: true) is { } game && PresentationSource.FromVisual(_window)?.CompositionTarget is { } target)
        {
            // over the game, on whichever monitor it is (the work area is the main monitor's)
            area = new Rect(target.TransformFromDevice.Transform(new Point(game.Left, game.Top)), target.TransformFromDevice.Transform(new Point(game.Right, game.Bottom)));
        }

        _window.Left = area.Left + (area.Width - _window.ActualWidth) / 2;
        _window.Top = area.Top + area.Height * 0.08;
        _timer!.Stop();
        _timer.Start();
    }

    /// <summary>The pop-up itself (also the picture of it): a title with the character, then a line per pet - picture, name, group, price; at most eight.</summary>
    public static FrameworkElement Build(string character, IReadOnlyList<BlackCloudToastPet> pets)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var gold = new SolidColorBrush(Color.FromRgb(0xD9, 0xB3, 0x6A));
        var text = new SolidColorBrush(Color.FromRgb(0xF4, 0xE9, 0xCF));
        var muted = new SolidColorBrush(Color.FromRgb(0xA8, 0x9F, 0x92));
        var stack = new StackPanel { MinWidth = 300 };
        var head = new TextBlock { FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = gold, Margin = new Thickness(0, 0, 0, 6) };
        head.Text = character.Length > 0 ? string.Format(CultureInfo.CurrentCulture, loc["BlackCloud.ToastTitle"], character) : loc["BlackCloud.ToastTitleAny"];
        stack.Children.Add(head);
        const int most = 8;
        foreach (BlackCloudToastPet pet in pets.Take(most))
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var picture = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 10, 0), Background = new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0x00, 0x00)) };
            if (Aion2Pets.PetIconPath(pet.PetId) is { } icon)
            {
                try
                {
                    picture.Child = new Image { Source = new BitmapImage(new Uri(icon)), Width = 34, Height = 34 };
                }
                catch (Exception ex) when (ex is NotSupportedException or System.IO.IOException or UriFormatException)
                {
                    // no picture then
                }
            }

            row.Children.Add(picture);
            var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            name.Children.Add(new TextBlock { Text = Aion2Pets.PetName(pet.PetId, loc.Language) ?? "#" + pet.PetId, FontSize = 14.5, FontWeight = FontWeights.SemiBold, Foreground = text });
            if (pet.Kind is { } kind)
            {
                name.Children.Add(new TextBlock { Text = loc["BlackCloud.KindShort." + kind], FontSize = 11, Foreground = muted, Margin = new Thickness(0, 1, 0, 0) });
            }

            Grid.SetColumn(name, 1);
            row.Children.Add(name);
            if (pet.Price > 0)
            {
                var price = new TextBlock
                {
                    Text = pet.Price.ToString("N0", CultureInfo.CurrentCulture) + " Kinah", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = gold,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0),
                };
                Grid.SetColumn(price, 2);
                row.Children.Add(price);
            }

            stack.Children.Add(row);
        }

        if (pets.Count > most)
        {
            stack.Children.Add(new TextBlock { Text = string.Format(CultureInfo.CurrentCulture, loc["BlackCloud.ToastMore"], pets.Count - most), FontSize = 11.5, Foreground = muted, Margin = new Thickness(46, 2, 0, 0) });
        }

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x16, 0x14, 0x1A)), BorderBrush = gold, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 16, 10), Child = stack,
        };
    }
}
