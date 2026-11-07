using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>One pet the Black Cloud shop offers that the character still needs: the pet, its lowest price on the pages read, its group, its genus (Cognia,
/// Fera, Natura, Varia, Specia) and the level the character the shop is open on has it at (0 = not owned; null = not known).</summary>
public sealed record BlackCloudToastPet(int PetId, long Price, MerchantPetKind? Kind, string? Species = null, int? Level = null);

/// <summary>
/// The pets the Black Cloud shop offers that the character still needs, marked on the shop's page itself: a frame in the genus's colour around each one's
/// card, with its genus and how far the character has it on a tag above (players, 7 November 2026: "put another overlay over the Urugugu Brawler so you
/// don't have to search your screen"; the owner the same day: no pop-up at the top any more, only the frames, and none for a pet once bought). The cards
/// are found in the game window's own picture (<see cref="ShopPetFinder"/>), only for the pets of the page the shop showed last, once the page has settled;
/// then once a second each mark is checked against a new picture and taken away when its card is gone (another tab, the shop shut) or the pet was bought.
/// Click-through, never taking the focus, only while the game is in front.
/// </summary>
public static class ShopMarks
{
    /// <summary>Each genus's colour (the mark on the shop's card, the Pets page): Cognia blue, Fera red, Natura green, Varia violet, Specia gold.</summary>
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

    /// <summary>The game window's client area in the units of <paramref name="window"/> (null when the game is not in front).</summary>
    internal static Rect? GameArea(Visual window)
    {
        if (GameWindow.ForegroundClientArea(ignoreMenu: true) is { } game && PresentationSource.FromVisual(window)?.CompositionTarget is { } target)
        {
            return new Rect(target.TransformFromDevice.Transform(new Point(game.Left, game.Top)), target.TransformFromDevice.Transform(new Point(game.Right, game.Bottom)));
        }

        return null;
    }

    /// <summary>A pet was bought (its item came into the bag): its mark goes at once, and it is not looked for again.</summary>
    public static void Drop(int petId)
    {
        _pets = _pets.Where(p => p.PetId != petId).ToList();
        _marks = _marks.Where(m => m.PetId != petId).ToList();
        _shown = _shown.Where(m => m.PetId != petId).ToList();
        Draw();
    }

    private static Window? _window;
    private static Canvas? _canvas;
    private static System.Windows.Threading.DispatcherTimer? _check;
    private static System.Windows.Threading.DispatcherTimer? _input;
    private static IReadOnlyList<BlackCloudToastPet> _pets = Array.Empty<BlackCloudToastPet>();

    /// <summary>Where the cards of the page are (found when the page came); <see cref="_shown"/>: the ones drawn now, whose card is still there.</summary>
    private static List<ShopPetFinder.Found> _marks = new(), _shown = new();

    private static int _generation;
    private static bool _busy;
    private static DateTime _until;

    /// <summary>A click or a key since the last look: the marks are away until the screen has settled and the cards are checked again.</summary>
    private static bool _stale;
    private static DateTime _lastInput;
    private static bool _mouseWas, _keyWas;

    /// <summary>A mark stays while its card still matches at least this well.</summary>
    private const double Keep = 0.7;

    /// <summary>How long after the last click or key the cards are checked again (the game's window opened or shut by then).</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(400);

    /// <summary>Looks for the cards of <paramref name="pets"/> on the shop's page (a moment after the page came, and again a little later while it settles).</summary>
    public static void Look(IReadOnlyList<BlackCloudToastPet> pets)
    {
        if (MainWindow.Headless || !MeterSettings.Load().BlackCloudPopup || !GameWindow.OverlayMayShow(ignoreMenu: true))
        {
            Clear();
            return;
        }

        // only the pets of the page the shop shows now (the last one it sent)
        var onPage = Aion2.Aion2BlackCloud.LastPage?.Pets ?? Array.Empty<int>();
        _pets = pets.Where(p => onPage.Contains(p.PetId)).ToList();
        int generation = ++_generation;
        _until = DateTime.UtcNow.AddMinutes(3);
        _marks = new();
        _shown = new();
        _stale = false;
        Draw();
        if (_pets.Count == 0)
        {
            Clear();
            return;
        }

        EnsureTimers();
        foreach (int ms in new[] { 450, 1300, 2600 })
        {
            var once = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            once.Tick += (_, _) =>
            {
                once.Stop();
                if (generation == _generation && !_stale && (_marks.Count < _pets.Count || ms == 1300))
                {
                    Search(generation, anySize: true);
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
        _shown = new();
        _check?.Stop();
        _input?.Stop();
        _canvas?.Children.Clear();
        _window?.Hide();
    }

    private static void EnsureTimers()
    {
        if (_check is null)
        {
            _check = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _check.Tick += (_, _) => Verify();
            _input = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            _input.Tick += (_, _) => WatchInput();
        }

        _check.Start();
        _input!.Start();
    }

    /// <summary>
    /// 25 times a second: a click or a key (pressed now, not one held) takes the marks away at once - a click on a card opens the game's purchase question
    /// over the page, Esc shuts the shop, a tab turns the page (7 November 2026, the owner: "when clicking the pet it bugs out to the side, it should
    /// instantly just go away"). Once nothing was pressed for <see cref="Settle"/>, the cards are checked where they were and the marks come back only on
    /// those still there and not dimmed by a window of the game.
    /// </summary>
    private static void WatchInput()
    {
        bool mouse = GameWindow.MouseButtonDown(), key = GameWindow.AnyKeyDown();
        bool pressed = (mouse && !_mouseWas) || (key && !_keyWas) || mouse;
        _mouseWas = mouse;
        _keyWas = key;
        if (pressed)
        {
            _lastInput = DateTime.UtcNow;
            _stale = true;
            if (_shown.Count > 0)
            {
                _shown = new();
                Draw();
            }

            return;
        }

        if (_stale && !_busy && DateTime.UtcNow - _lastInput >= Settle)
        {
            _stale = false;
            if (_marks.Count > 0)
            {
                Recheck();
            }
            else
            {
                Search(_generation, anySize: false);
            }
        }
    }

    /// <summary>A new picture of the game, its coarse and fine copies made on a worker (the picture is the game window's client area in real pixels).</summary>
    private static bool Picture(out ColorPlanes? coarse, out ColorPlanes? fine, bool coarseToo = true)
    {
        coarse = fine = null;
        IntPtr game = GameWindow.ForegroundGame();
        if (game == IntPtr.Zero || GameWindow.ClientSizeInPixels(game) is not { } size)
        {
            return false;
        }

        ColorPlanes? c = null, f = null;
        bool ok = GameCapture.Use(game, size, TimeSpan.FromMilliseconds(150), bitmap =>
        {
            int cw = (int)Math.Round(bitmap.Width * (double)ShopPetFinder.CoarseHeight / bitmap.Height);
            int fw = (int)Math.Round(bitmap.Width * (double)ShopPetFinder.FineHeight / bitmap.Height);
            if (coarseToo)
            {
                c = ColorPlanes.Of(bitmap, cw, ShopPetFinder.CoarseHeight);
            }

            f = ColorPlanes.Of(bitmap, fw, ShopPetFinder.FineHeight);
        });
        coarse = c;
        fine = f;
        return ok && f is not null && (!coarseToo || c is not null);
    }

    /// <summary>The cards looked for on the whole page (a page that came; <paramref name="anySize"/> only then: later looks keep to the cards' size, so the
    /// purchase question's bigger picture of the pet is never taken for its card).</summary>
    private static void Search(int generation, bool anySize)
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

                return ShopPetFinder.Find(coarse!, fine!, pets.Select(p => p.PetId), ShopPetFinder.Icon, anySize);
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
                if (generation != _generation || _stale || !task.IsCompletedSuccessfully || task.Result is not { } found)
                {
                    return; // another page meanwhile, or a click since the picture: the next look decides
                }

                var still = _pets.Select(p => p.PetId).ToHashSet();
                _marks = found.Where(f => still.Contains(f.PetId)).ToList();
                _shown = _marks;
                Draw();
            }));
        }, TaskScheduler.Default);
    }

    /// <summary>Once a second (and after a click has settled): each card is checked where it was found; a mark shows only while its card matches and is not
    /// dimmed (the game's purchase question over the page); gone for good after a few minutes or with the game behind another window.</summary>
    private static void Verify()
    {
        if (DateTime.UtcNow > _until || !GameWindow.OverlayMayShow(ignoreMenu: true))
        {
            Clear();
            return;
        }

        if (_marks.Count > 0 && !_busy && !_stale)
        {
            Recheck();
        }
    }

    private static void Recheck()
    {
        _busy = true;
        var marks = _marks;
        int generation = _generation;
        Task.Run(() =>
        {
            try
            {
                if (!Picture(out _, out ColorPlanes? fine, coarseToo: false))
                {
                    return null;
                }

                return marks.Where(m => ShopPetFinder.Icon(m.PetId) is { } icon && StillThere(fine!, icon, m)).ToList();
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
                if (generation != _generation || _stale || !task.IsCompletedSuccessfully || task.Result is not { } kept)
                {
                    return;
                }

                kept = kept.Where(m => _pets.Any(p => p.PetId == m.PetId)).ToList();
                if (kept.Count != _shown.Count || kept.Any(k => !_shown.Contains(k)))
                {
                    _shown = kept;
                    Draw();
                }
            }));
        }, TaskScheduler.Default);
    }

    /// <summary>The card still matches where it was found and is about as bright as then (a window of the game over the page dims it to well under that).</summary>
    private static bool StillThere(ColorPlanes fine, System.Drawing.Bitmap icon, ShopPetFinder.Found at)
    {
        using (icon)
        {
            var (score, light) = ShopPetFinder.ScoreAt(fine, icon, at);
            return score >= Keep && (at.Light <= 0 || light >= 0.75 * at.Light);
        }
    }

    /// <summary>The frames over the game: one window as large as the game's client area, a frame and a tag for each card found.</summary>
    private static void Draw()
    {
        if (_shown.Count == 0)
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

        if (GameArea(_window) is not { } area)
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
        foreach (ShopPetFinder.Found found in _shown)
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
        Color color = SpeciesColor(pet?.Species);
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
            words.Add(Aion2Pets.SpeciesShown(species, LocalizationManager.Instance.Language));
        }

        if (LevelText(pet?.Level) is { Length: > 0 } level)
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
