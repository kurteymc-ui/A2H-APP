using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>One pet of the merchant's pop-up: the pet, its lowest price on the pages read, its group, its genus (Cognia, Fera, Natura, Varia, Specia) and the
/// level the character the shop is open on has it at (0 = not owned; null = not known).</summary>
public sealed record BlackCloudToastPet(int PetId, long Price, MerchantPetKind? Kind, string? Species = null, int? Level = null);

/// <summary>
/// The Black Cloud Merchants' pop-up over the game: the pets the character still needs that the merchant offers now (ticked, of the genera the player wants
/// alerts for, not at the top level), each with its picture, name, genus, group, how far the character has it and price - every page of the shop read since
/// its offers renewed together, so turning to another tab of the shop adds to it instead of replacing it. It shows while the shop is open (the game's own
/// window is over the game then, which hides the other overlays: this one stays), above the shop's cards, click-through, never taking the focus, and fades
/// 20 s after the last page came. Players asked for it this way (4 November 2026: pictures and names), then for the genus and the card marked on the page
/// (7 November 2026: "so you don't have to search your screen"; <see cref="ShopMarks"/>).
/// </summary>
public static class BlackCloudToast
{
    private static Window? _window;
    private static Border? _body;
    private static System.Windows.Threading.DispatcherTimer? _timer;
    private static NativeOverlay? _native;

    /// <summary>How long it stays after the last page of the shop came.</summary>
    public static readonly TimeSpan Stay = TimeSpan.FromSeconds(20);

    private static readonly Color Gold = Color.FromRgb(0xE2, 0xBE, 0x78);

    /// <summary>Each genus's colour (the pop-up's chip, the mark on the shop's card): Cognia blue, Fera red, Natura green, Varia violet, Specia gold.</summary>
    public static Color SpeciesColor(string? species) => species switch
    {
        "Cognia" => Color.FromRgb(0x6F, 0xA8, 0xFF),
        "Fera" => Color.FromRgb(0xFF, 0x7E, 0x5F),
        "Natura" => Color.FromRgb(0x6C, 0xD0, 0x8A),
        "Varia" => Color.FromRgb(0xB9, 0x8C, 0xFF),
        "Specia" => Color.FromRgb(0xE8, 0xC1, 0x70),
        _ => Color.FromRgb(0xA8, 0xB0, 0xBA),
    };

    /// <summary>"not owned", "level 1 of 3", or nothing when the character's collection is not known.</summary>
    public static string LevelText(int? level) => level switch
    {
        null => "",
        0 => LocalizationManager.Instance["BlackCloud.NotOwned"],
        _ => string.Format(CultureInfo.CurrentCulture, LocalizationManager.Instance["BlackCloud.LevelOf"], level, Aion2Pets.TopLevel),
    };

    private static SolidColorBrush Brush(Color c, byte alpha = 0xFF)
    {
        var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    /// <summary>Shows (or updates) the pop-up for <paramref name="character"/> (empty: not known yet), and marks the pets' cards on the shop's page;
    /// nothing when <paramref name="pets"/> is empty.</summary>
    public static void Show(string character, IReadOnlyList<BlackCloudToastPet> pets, bool mark = true)
    {
        if (pets.Count == 0 || MainWindow.Headless || !GameWindow.OverlayMayShow(ignoreMenu: true))
        {
            ShopMarks.Clear();
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
            AltTabHidden.Apply(_window);
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
        Rect area = GameArea(_window) ?? SystemParameters.WorkArea;
        // above the shop's cards, under its "product list will refresh" bar: the cards stand in an arch, lowest in the middle, so the middle is free
        _window.Left = area.Left + (area.Width - _window.ActualWidth) / 2;
        _window.Top = area.Top + area.Height * 0.125;
        _timer!.Stop();
        _timer.Start();
        if (mark)
        {
            ShopMarks.Look(pets);
        }
    }

    /// <summary>The game window's client area in the units of <paramref name="window"/> (null when the game is not in front).</summary>
    internal static Rect? GameArea(Visual window)
    {
        if (GameWindow.ForegroundClientArea(ignoreMenu: true) is { } game && PresentationSource.FromVisual(window)?.CompositionTarget is { } target)
        {
            return new Rect(target.TransformFromDevice.Transform(new Point(game.Left, game.Top)), target.TransformFromDevice.Transform(new Point(game.Right, game.Bottom)));
        }

        return null;
    }

    /// <summary>
    /// The pop-up itself (also the picture of it): a heading (the merchant, the character, how many pets), then a line per pet - its picture in a ring of its
    /// genus's colour, name, genus, group and how far the character has it, and the price; at most eight.
    /// </summary>
    public static FrameworkElement Build(string character, IReadOnlyList<BlackCloudToastPet> pets)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var text = Brush(Color.FromRgb(0xF4, 0xEE, 0xE2));
        var muted = Brush(Color.FromRgb(0x9C, 0xA3, 0xAD));
        var stack = new StackPanel { MinWidth = 340, MaxWidth = 460 };

        // the heading: a gold bag, BLACK CLOUD MERCHANT, the character on the right; under it how many pets it offers that the character needs
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 2), LastChildFill = true };
        if (character.Length > 0)
        {
            var who = new TextBlock { Text = character, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            DockPanel.SetDock(who, Dock.Right);
            head.Children.Add(who);
        }

        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(new TextBlock
        {
            Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 12, Foreground = Brush(Gold), VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0),
        });
        title.Children.Add(new TextBlock { Text = loc["BlackCloud.Title"].ToUpper(CultureInfo.CurrentCulture), FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Brush(Gold), VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(title);
        stack.Children.Add(head);
        stack.Children.Add(new TextBlock
        {
            Text = string.Format(CultureInfo.CurrentCulture, loc[pets.Count == 1 ? "BlackCloud.ToastOne" : "BlackCloud.ToastMany"], pets.Count), FontSize = 14,
            FontWeight = FontWeights.SemiBold, Foreground = text, Margin = new Thickness(0, 0, 0, 8),
        });

        const int most = 8;
        bool first = true;
        foreach (BlackCloudToastPet pet in pets.Take(most))
        {
            if (!first)
            {
                stack.Children.Add(new Border { Height = 1, Background = Brush(Colors.White, 0x12), Margin = new Thickness(56, 0, 0, 0) });
            }

            first = false;
            stack.Children.Add(Row(pet, text, muted));
        }

        if (pets.Count > most)
        {
            stack.Children.Add(new TextBlock { Text = string.Format(CultureInfo.CurrentCulture, loc["BlackCloud.ToastMore"], pets.Count - most), FontSize = 11.5, Foreground = muted, Margin = new Thickness(56, 4, 0, 0) });
        }

        return new Border
        {
            Background = new LinearGradientBrush(Color.FromArgb(0xF4, 0x1C, 0x1D, 0x24), Color.FromArgb(0xF4, 0x11, 0x12, 0x17), 90),
            BorderBrush = Brush(Gold, 0x8C), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 12, 16, 10), Child = stack,
            Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 4, Opacity = 0.6, Color = Colors.Black },
            Margin = new Thickness(14, 6, 14, 20), // room for the shadow
        };
    }

    private static FrameworkElement Row(BlackCloudToastPet pet, Brush text, Brush muted)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        Color genus = SpeciesColor(pet.Species);
        var row = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // the portrait in a ring of the genus's colour
        var picture = new Border
        {
            Width = 44, Height = 44, CornerRadius = new CornerRadius(22), Margin = new Thickness(0, 0, 12, 0), BorderThickness = new Thickness(2), BorderBrush = Brush(genus),
            Background = new RadialGradientBrush(Color.FromArgb(0x60, genus.R, genus.G, genus.B), Color.FromArgb(0x30, 0, 0, 0)), ClipToBounds = true,
        };
        if (Aion2Pets.PetIconPath(pet.PetId) is { } icon)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(icon);
                image.DecodePixelWidth = 80;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                picture.Child = new Image { Source = image, Width = 38, Height = 38, Clip = new EllipseGeometry(new Point(19, 19), 19, 19) };
            }
            catch (Exception ex) when (ex is NotSupportedException or System.IO.IOException or UriFormatException)
            {
                // no picture then
            }
        }

        row.Children.Add(picture);

        // the name; under it the genus as a chip, the group and how far the character has it
        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        name.Children.Add(new TextBlock { Text = Aion2Pets.PetName(pet.PetId, loc.Language) ?? "#" + pet.PetId, FontSize = 14.5, FontWeight = FontWeights.SemiBold, Foreground = text, TextTrimming = TextTrimming.CharacterEllipsis });
        var tags = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
        if (pet.Species is { Length: > 0 } species)
        {
            tags.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 0, 6, 1), Margin = new Thickness(0, 0, 8, 0), Background = Brush(genus, 0x2E), BorderBrush = Brush(genus, 0x90),
                BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = species, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Brush(genus) },
            });
        }

        var words = new List<string>();
        if (pet.Kind is { } kind)
        {
            words.Add(loc["BlackCloud.KindShort." + kind]);
        }

        if (LevelText(pet.Level) is { Length: > 0 } level)
        {
            words.Add(level);
        }

        if (words.Count > 0)
        {
            tags.Children.Add(new TextBlock { Text = string.Join("  ·  ", words), FontSize = 11, Foreground = muted, VerticalAlignment = VerticalAlignment.Center });
        }

        name.Children.Add(tags);
        Grid.SetColumn(name, 1);
        row.Children.Add(name);
        if (pet.Price > 0)
        {
            var price = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) };
            price.Children.Add(new TextBlock { Text = pet.Price.ToString("N0", CultureInfo.CurrentCulture), FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Brush(Gold), HorizontalAlignment = HorizontalAlignment.Right });
            price.Children.Add(new TextBlock { Text = "Kinah", FontSize = 10.5, Foreground = muted, HorizontalAlignment = HorizontalAlignment.Right });
            Grid.SetColumn(price, 2);
            row.Children.Add(price);
        }

        return row;
    }
}

/// <summary>
/// The pets of the pop-up marked on the shop's page itself: a frame in the genus's colour around each one's card, with its genus and how far the character
/// has it on a tag above (players, 7 November 2026: "put another overlay over the Urugugu Brawler so you don't have to search your screen"). The cards are
/// found in the game window's own picture (<see cref="ShopPetFinder"/>), only for the pets of the page the shop showed last, once the page has settled; then
/// once a second each mark is checked against a new picture and taken away when its card is gone (another tab, the shop shut). Click-through, never
/// taking the focus, only while the game is in front.
/// </summary>
public static class ShopMarks
{
    private static Window? _window;
    private static Canvas? _canvas;
    private static System.Windows.Threading.DispatcherTimer? _check;
    private static IReadOnlyList<BlackCloudToastPet> _pets = Array.Empty<BlackCloudToastPet>();
    private static List<ShopPetFinder.Found> _marks = new();
    private static int _generation;
    private static bool _busy;
    private static DateTime _until;

    /// <summary>A mark stays while its card still matches at least this well.</summary>
    private const double Keep = 0.7;

    /// <summary>Looks for the cards of <paramref name="pets"/> on the shop's page (a moment after the page came, and again a little later while it settles).</summary>
    public static void Look(IReadOnlyList<BlackCloudToastPet> pets)
    {
        if (MainWindow.Headless || !MeterSettings.Load().BlackCloudMark)
        {
            Clear();
            return;
        }

        // only the pets of the page the shop shows now (the last one it sent)
        var onPage = Aion2.Aion2BlackCloud.LastPage?.Pets ?? Array.Empty<int>();
        _pets = pets.Where(p => onPage.Contains(p.PetId)).ToList();
        int generation = ++_generation;
        _until = DateTime.UtcNow.AddMinutes(3);
        if (_pets.Count == 0)
        {
            Clear();
            return;
        }

        EnsureTimer();
        foreach (int ms in new[] { 450, 1300, 2600 })
        {
            var once = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            once.Tick += (_, _) =>
            {
                once.Stop();
                if (generation == _generation && (_marks.Count < _pets.Count || ms == 1300))
                {
                    Search(generation);
                }
            };
            once.Start();
        }
    }

    /// <summary>Every mark away (the shop shut, nothing to mark).</summary>
    public static void Clear()
    {
        _generation++;
        _marks = new();
        _check?.Stop();
        _canvas?.Children.Clear();
        _window?.Hide();
    }

    private static void EnsureTimer()
    {
        if (_check is null)
        {
            _check = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _check.Tick += (_, _) => Verify();
        }

        _check.Start();
    }

    /// <summary>A new picture of the game, its coarse and fine copies made on a worker (the picture is the game window's client area in real pixels).</summary>
    private static bool Picture(out ColorPlanes? coarse, out ColorPlanes? fine)
    {
        coarse = fine = null;
        IntPtr game = GameWindow.ForegroundGame();
        if (game == IntPtr.Zero || GameWindow.ClientSizeInPixels(game) is not { } size)
        {
            return false;
        }

        ColorPlanes? c = null, f = null;
        bool ok = GameCapture.Use(game, size, TimeSpan.FromMilliseconds(250), bitmap =>
        {
            int cw = (int)Math.Round(bitmap.Width * (double)ShopPetFinder.CoarseHeight / bitmap.Height);
            int fw = (int)Math.Round(bitmap.Width * (double)ShopPetFinder.FineHeight / bitmap.Height);
            c = ColorPlanes.Of(bitmap, cw, ShopPetFinder.CoarseHeight);
            f = ColorPlanes.Of(bitmap, fw, ShopPetFinder.FineHeight);
        });
        coarse = c;
        fine = f;
        return ok && c is not null && f is not null;
    }

    private static void Search(int generation)
    {
        if (_busy || !GameWindow.OverlayMayShow(ignoreMenu: true))
        {
            return;
        }

        _busy = true;
        var pets = _pets;
        Task.Run(() =>
        {
            try
            {
                if (!Picture(out ColorPlanes? coarse, out ColorPlanes? fine))
                {
                    return null;
                }

                return ShopPetFinder.Find(coarse!, fine!, pets.Select(p => p.PetId), ShopPetFinder.Icon);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OutOfMemoryException or System.Runtime.InteropServices.ExternalException)
            {
                return null;
            }
        }).ContinueWith(task =>
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                _busy = false;
                if (generation != _generation || !task.IsCompletedSuccessfully || task.Result is not { } found)
                {
                    return;
                }

                // a card found again keeps its place; one found now is added
                _marks = found;
                Draw();
            }));
        }, TaskScheduler.Default);
    }

    /// <summary>Once a second: each mark is checked on a new picture (the card still there, matching); gone when not, or after a few minutes.</summary>
    private static void Verify()
    {
        if (DateTime.UtcNow > _until || !GameWindow.OverlayMayShow(ignoreMenu: true))
        {
            Clear();
            return;
        }

        if (_marks.Count == 0 || _busy)
        {
            return;
        }

        _busy = true;
        var marks = _marks;
        int generation = _generation;
        Task.Run(() =>
        {
            try
            {
                if (!Picture(out _, out ColorPlanes? fine))
                {
                    return null;
                }

                return marks.Where(m => ShopPetFinder.Icon(m.PetId) is { } icon && ScoreAndDispose(fine!, icon, m) >= Keep).ToList();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OutOfMemoryException or System.Runtime.InteropServices.ExternalException)
            {
                return null;
            }
        }).ContinueWith(task =>
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                _busy = false;
                if (generation != _generation || !task.IsCompletedSuccessfully || task.Result is not { } kept)
                {
                    return;
                }

                if (kept.Count != _marks.Count)
                {
                    _marks = kept;
                    Draw();
                }
            }));
        }, TaskScheduler.Default);
    }

    private static double ScoreAndDispose(ColorPlanes fine, System.Drawing.Bitmap icon, ShopPetFinder.Found at)
    {
        using (icon)
        {
            return ShopPetFinder.ScoreAt(fine, icon, at);
        }
    }

    /// <summary>The frames over the game: one window as large as the game's client area, a frame and a tag for each card found.</summary>
    private static void Draw()
    {
        if (_marks.Count == 0)
        {
            _canvas?.Children.Clear();
            _window?.Hide();
            return;
        }

        if (_window is null)
        {
            _canvas = new Canvas();
            _window = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false,
                ShowActivated = false, ResizeMode = ResizeMode.NoResize, Focusable = false, Content = _canvas, Left = -32000, Top = -32000, Width = 10, Height = 10,
            };
            AltTabHidden.Apply(_window);
            _window.SourceInitialized += (_, _) =>
            {
                using var native = new NativeOverlay(_window);
                native.SetClickThrough(true);
            };
            _window.Show();
        }

        if (BlackCloudToast.GameArea(_window) is not { } area)
        {
            _window.Hide();
            return;
        }

        if (!_window.IsVisible)
        {
            _window.Show();
        }

        _window.Left = area.Left;
        _window.Top = area.Top;
        _window.Width = area.Width;
        _window.Height = area.Height;
        _canvas!.Children.Clear();
        foreach (ShopPetFinder.Found found in _marks)
        {
            BlackCloudToastPet? pet = _pets.FirstOrDefault(p => p.PetId == found.PetId);
            foreach (UIElement part in Mark(found, pet, area.Width, area.Height))
            {
                _canvas.Children.Add(part);
            }
        }

        NativeOverlay.KeepOnTop(new System.Windows.Interop.WindowInteropHelper(_window).Handle);
    }

    /// <summary>One card's mark: a glowing frame in the genus's colour around the card, and a tag on its top edge with the genus and how far the character has it.</summary>
    internal static IEnumerable<UIElement> Mark(ShopPetFinder.Found found, BlackCloudToastPet? pet, double width, double height)
    {
        var (x, y, w, h) = found.Card;
        Color color = BlackCloudToast.SpeciesColor(pet?.Species);
        var frame = new Border
        {
            Width = w * width, Height = h * height, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(3),
            BorderBrush = new SolidColorBrush(color), Background = new SolidColorBrush(Color.FromArgb(0x14, color.R, color.G, color.B)),
            Effect = new DropShadowEffect { Color = color, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.9 },
        };
        Canvas.SetLeft(frame, x * width);
        Canvas.SetTop(frame, y * height);
        yield return frame;

        var words = new List<string>();
        if (pet?.Species is { Length: > 0 } species)
        {
            words.Add(species);
        }

        if (BlackCloudToast.LevelText(pet?.Level) is { Length: > 0 } level)
        {
            words.Add(level);
        }

        var tag = new Border
        {
            CornerRadius = new CornerRadius(9), Padding = new Thickness(9, 2, 9, 3), Background = new SolidColorBrush(color),
            Child = new TextBlock
            {
                Text = words.Count > 0 ? string.Join(" · ", words) : LocalizationManager.Instance["BlackCloud.Wanted"], FontSize = 12, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x14, 0x12, 0x10)),
            },
            Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.6, Color = Colors.Black },
        };
        tag.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(tag, x * width + (w * width - tag.DesiredSize.Width) / 2);
        Canvas.SetTop(tag, y * height - tag.DesiredSize.Height / 2);
        yield return tag;
    }
}
