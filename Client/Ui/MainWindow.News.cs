using System.Globalization;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AionDPS.Announcements;

namespace AionDPS.Ui;

/// <summary>
/// The news on Home (5 November 2026, the owner: "an event page, like a 1200x400 banner, with recent news that people can click to read"): a 3:1 banner
/// under the greeting going through the news (our posts about the app and the game's newest official notices, patch notes, events and maintenance; see
/// <see cref="NewsClient"/>), a new one every 8 seconds unless the mouse is on it, arrows and dots to pick one; a click opens it in the app (the "news"
/// page): its picture, title, the article, and its link (a page of the app, the website or the game's official site), with the other news under it.
/// </summary>
public partial class MainWindow
{
    private int _newsAt;
    private DateTime _newsMoved = DateTime.Now;
    private bool _newsHeld;
    private Action<int>? _newsShow;
    private NewsItem? _newsOpen;
    private bool _newsHooked;
    private bool _homeSlidesHooked;
    private readonly DispatcherTimer _homeSlides = new() { Interval = TimeSpan.FromSeconds(1) };
    private static readonly TimeSpan NewsEvery = TimeSpan.FromSeconds(8);

    /// <summary>The banner's and the tips' clock: a new news item every 8 s and a new tip every 12 s while Home shows (the mouse on one holds it).</summary>
    private void StartHomeSlides()
    {
        if (Headless)
        {
            return;
        }

        if (!_homeSlidesHooked)
        {
            _homeSlidesHooked = true;
            _homeSlides.Tick += (_, _) =>
            {
                if (_shellPage != "home")
                {
                    _homeSlides.Stop();
                    return;
                }

                if (!IsVisible || WindowState == WindowState.Minimized)
                {
                    return;
                }

                DateTime now = DateTime.Now;
                if (_newsShow is not null && !_newsHeld && now - _newsMoved >= NewsEvery)
                {
                    _newsShow(_newsAt + 1);
                }

                if (_tipShow is not null && !_tipHeld && now - _tipMoved >= TipEvery)
                {
                    _tipShow(_tipAt + 1);
                }
            };
        }

        if (!_newsHooked)
        {
            _newsHooked = true;
            NewsClient.Changed += () => _ = Dispatcher.InvokeAsync(() =>
            {
                if (_shellPage == "home")
                {
                    ShowShellPage("home");
                }
            });
        }

        _homeSlides.Start();
    }

    /// <summary>The news to show: the website's (asked again when old), else a welcome of the app's own.</summary>
    private IReadOnlyList<NewsItem> HomeNews()
    {
        if (!Headless)
        {
            NewsClient.RefreshIfStale();
        }

        IReadOnlyList<NewsItem> items = NewsClient.Items;
        return items.Count > 0 ? items : new[] { WelcomeNews() };
    }

    /// <summary>Made-up news for the pictures of the app (render-app): a post of ours with its article, and official items of each kind.</summary>
    internal static IReadOnlyList<NewsItem> SampleNews()
    {
        DateTime now = DateTime.UtcNow;
        NewsBlock B(string kind, string text) => new(kind, text, Array.Empty<string>());
        return new[]
        {
            new NewsItem("make-it-yours", "atreia", "Atreia App", "Make it yours",
                "A setup at the first start shows every change over your own game, each overlay gets its own place and transparency, and the meter tells you when the boss dies.",
                now.AddHours(-2), null, null, "setup", "Run the setup", Array.Empty<string>(),
                new[]
                {
                    B("h", "Set it up before you play"),
                    B("p", "The first time the app opens, a short setup walks you through the meter's design, its columns, the overlays, the map and the colours. Every step shows how it will look over a picture of your own game."),
                    B("h", "Every overlay on its own"),
                    B("li", "Drag each overlay to its place on the picture of your game."),
                    B("li", "Give each one its own transparency and size with Customize."),
                    new NewsBlock("row", "", new[] { "Overlay", "Transparency", "Size" }),
                    new NewsBlock("row", "", new[] { "Cooldowns", "80%", "110%" }),
                    new NewsBlock("row", "", new[] { "Timers", "60%", "100%" }),
                    B("p", "You can run the setup again any time from Home."),
                }, null, null, false),
            new NewsItem("6b0000000000000000000001", "game", "Patch notes", "Sample Update Notes", "Hard mode for two expedition dungeons. Expedition bosses hit softer.",
                now.AddDays(-1), null, null, null, null, new[] { "Hard mode for two expedition dungeons", "Expedition bosses hit softer", "A new daily dungeon" }, null, null, null, false),
            new NewsItem("6b0000000000000000000002", "game", "Event", "Sample Login Event", "Log in every day for 28 days for a reward each day.",
                now.AddDays(-2), null, null, null, null, Array.Empty<string>(), null, null, null, false),
            new NewsItem("6b0000000000000000000003", "game", "Maintenance", "Sample Maintenance", "",
                now.AddDays(-3), null, null, null, null, Array.Empty<string>(), null, now.Date.AddDays(3).AddHours(7), 240, false),
        };
    }

    private static NewsItem WelcomeNews()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        return new NewsItem("welcome", "atreia", "Atreia App", loc["News.Welcome.Title"], loc["News.Welcome.Text"], DateTime.UtcNow, null, Brand.Site + "/", null, null,
            Array.Empty<string>(), new[] { new NewsBlock("p", loc["News.Welcome.Body"], Array.Empty<string>()) }, null, null, false);
    }

    /// <summary>The 3:1 banner of Home: the news one after the other, a click opens the one shown.</summary>
    private UIElement NewsBanner(IReadOnlyList<NewsItem> items)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var host = new Grid { Margin = new Thickness(0, 0, 12, 12), Cursor = Cursors.Hand, MinHeight = 200 };
        host.SizeChanged += (_, e) =>
        {
            if (e.WidthChanged)
            {
                host.Height = Math.Round(e.NewSize.Width / 3);
            }

            host.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 8, 8);
        };
        host.Children.Add(new Border { Background = NightBrush() });
        var picture = new Image { Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        host.Children.Add(picture);

        // the words sit on the left: a dark fade from there, and a little at the bottom for the dots
        var fade = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(0xF0, 0x07, 0x0A, 0x11), 0));
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(0xC0, 0x07, 0x0A, 0x11), 0.36));
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0x07, 0x0A, 0x11), 0.72));
        host.Children.Add(new Border { Background = fade });
        var bottom = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        bottom.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0.6));
        bottom.GradientStops.Add(new GradientStop(Color.FromArgb(0x80, 0, 0, 0), 1));
        host.Children.Add(new Border { Background = bottom });
        host.Children.Add(new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xDC, 0xB8, 0x77)) });

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(30, 0, 30, 24), MaxWidth = 460 };
        var meta = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var tag = new Border { CornerRadius = new CornerRadius(3), Padding = new Thickness(7, 2, 7, 2), VerticalAlignment = VerticalAlignment.Center };
        var tagText = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0x12, 0x18)) };
        tag.Child = tagText;
        meta.Children.Add(tag);
        var date = new TextBlock { FontSize = 12, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Color.FromRgb(0xB8, 0xC0, 0xCC)) };
        meta.Children.Add(date);
        words.Children.Add(meta);
        var title = new TextBlock
        {
            FontSize = 28, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 76, LineHeight = 36,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.7 },
        };
        title.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Title");
        words.Children.Add(title);
        var summary = new TextBlock
        {
            FontSize = 13.5, Margin = new Thickness(0, 6, 0, 0), Foreground = new SolidColorBrush(Color.FromRgb(0xD6, 0xDC, 0xE4)), TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.WordEllipsis, MaxHeight = 60, LineHeight = 20,
        };
        words.Children.Add(summary);
        var read = new Border
        {
            CornerRadius = new CornerRadius(15), Padding = new Thickness(14, 5, 14, 6), Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
            BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0xB8, 0x77)), Background = new SolidColorBrush(Color.FromArgb(0x30, 0xDC, 0xB8, 0x77)),
            Child = new TextBlock { Text = loc["News.Read"] + "  ›", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xD9, 0xA8)) },
        };
        words.Children.Add(read);
        host.Children.Add(words);
        host.SizeChanged += (_, e) => words.MaxWidth = Math.Max(340, (e.NewSize.Width * 0.5) - 30); // the left half: the pictures keep their right half free of words

        // arrows on the sides (with the mouse on the banner) and the dots
        var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 22, 20) };
        Border previous = NewsArrow("", HorizontalAlignment.Left), next = NewsArrow("", HorizontalAlignment.Right);
        if (items.Count > 1)
        {
            host.Children.Add(dots);
            host.Children.Add(previous);
            host.Children.Add(next);
        }

        void Show(int index, bool animate)
        {
            _newsAt = ((index % items.Count) + items.Count) % items.Count;
            _newsMoved = DateTime.Now;
            NewsItem item = items[_newsAt];
            tagText.Text = NewsTag(item.Tag);
            tag.Background = new SolidColorBrush(NewsTagColour(item.Tag));
            date.Text = NewsDate(item.Posted);
            title.Text = item.Title;
            summary.Text = NewsSummary(item);
            summary.Visibility = summary.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            picture.Source = null;
            int shown = _newsAt;
            NewsPicture(item.Image, 1200, source =>
            {
                if (_newsAt == shown)
                {
                    picture.Source = source;
                }
            });
            dots.Children.Clear();
            for (int k = 0; k < items.Count; k++)
            {
                int to = k;
                var dot = new Border
                {
                    Width = k == _newsAt ? 20 : 8, Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(3, 0, 3, 0), Cursor = Cursors.Hand,
                    Background = k == _newsAt ? new SolidColorBrush(Color.FromRgb(0xDC, 0xB8, 0x77)) : new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)),
                };
                dot.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    Show(to, true);
                };
                dots.Children.Add(dot);
            }

            if (animate)
            {
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = new QuadraticEase() };
                words.BeginAnimation(OpacityProperty, fadeIn);
                picture.BeginAnimation(OpacityProperty, fadeIn);
            }
        }

        previous.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Show(_newsAt - 1, true);
        };
        next.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Show(_newsAt + 1, true);
        };
        host.MouseEnter += (_, _) =>
        {
            _newsHeld = true;
            previous.Opacity = next.Opacity = 1;
        };
        host.MouseLeave += (_, _) =>
        {
            _newsHeld = false;
            _newsMoved = DateTime.Now;
            previous.Opacity = next.Opacity = 0;
        };
        host.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            OpenNews(items[_newsAt]);
        };
        _newsHeld = false;
        _newsShow = i => Show(i, true);
        Show(_newsAt, false);
        return host;
    }

    private static Border NewsArrow(string glyph, HorizontalAlignment side)
    {
        var arrow = new Border
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(18), HorizontalAlignment = side, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0),
            Background = new SolidColorBrush(Color.FromArgb(0x80, 0x08, 0x0B, 0x12)), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)),
            Opacity = 0, Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        return arrow;
    }

    /// <summary>Under a picture still on its way (or none): a night sky in the app's colours.</summary>
    private static Brush NightBrush()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x08, 0x0B, 0x12), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x11, 0x18, 0x27), 0.55));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x1A, 0x14, 0x24), 1));
        brush.Freeze();
        return brush;
    }

    private static string NewsTag(string tag)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        return tag switch
        {
            "Atreia App" => loc["News.Tag.App"],
            "Patch notes" => loc["News.Tag.Patch"],
            "Event" => loc["News.Tag.Event"],
            "Maintenance" => loc["News.Tag.Maintenance"],
            "Announcement" => loc["News.Tag.Notice"],
            _ => tag,
        };
    }

    private static Color NewsTagColour(string tag) => tag switch
    {
        "Patch notes" => Color.FromRgb(0x6F, 0xB4, 0xEC),
        "Event" => Color.FromRgb(0x72, 0xD6, 0x8E),
        "Maintenance" => Color.FromRgb(0xEC, 0xAE, 0x52),
        "Announcement" => Color.FromRgb(0xB4, 0xBE, 0xCB),
        _ => Color.FromRgb(0xDC, 0xB8, 0x77),
    };

    private static string NewsDate(DateTime posted)
    {
        DateTime local = posted.ToLocalTime();
        return local.ToString(local.Year == DateTime.Now.Year ? "d MMM" : "d MMM yyyy", CultureInfo.CurrentCulture);
    }

    /// <summary>The line under the title: a maintenance's start (in the player's own time) and length, else the item's summary.</summary>
    private static string NewsSummary(NewsItem item)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        if (item.Tag != "Maintenance" || item.Start is not DateTime start)
        {
            return item.Summary;
        }

        DateTime local = start.ToLocalTime();
        string when = local.ToString("ddd d MMM", CultureInfo.CurrentCulture) + ", " + local.ToString("t", CultureInfo.CurrentCulture);
        string text = string.Format(CultureInfo.CurrentCulture, loc[item.Done ? "News.MaintenanceWas" : "News.MaintenanceAt"], when);
        if (item.Minutes is int minutes)
        {
            text += " · " + string.Format(CultureInfo.CurrentCulture, loc["News.MaintenanceLength"],
                minutes >= 120 ? (minutes / 60.0).ToString("0.#", CultureInfo.CurrentCulture) + " h" : minutes + " min");
        }

        return text;
    }

    // ---- the reader ----

    private void OpenNews(NewsItem item)
    {
        _newsOpen = item;
        ShowShellPage("news");
    }

    /// <summary>The "news" page: the item opened on Home, in full.</summary>
    private UIElement BuildNewsPage()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        IReadOnlyList<NewsItem> all = Headless ? HomeNews() : NewsClient.Items;
        NewsItem item = _newsOpen ?? all.FirstOrDefault() ?? WelcomeNews();
        var page = new StackPanel { Margin = new Thickness(24, 18, 24, 28), MaxWidth = 900 };
        TextBlock back = ShellLink("‹  " + loc["Shell.Home"], () => ShowShellPage("home"));
        back.HorizontalAlignment = HorizontalAlignment.Left;
        back.Margin = new Thickness(0, 0, 0, 12);
        page.Children.Add(back);

        // the picture, 3:1
        var frame = new Grid();
        frame.SizeChanged += (_, e) =>
        {
            if (e.WidthChanged)
            {
                frame.Height = Math.Round(e.NewSize.Width / 3);
            }

            frame.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 8, 8);
        };
        frame.Children.Add(new Border { Background = NightBrush() });
        var picture = new Image { Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        frame.Children.Add(picture);
        NewsPicture(item.Image, 1200, source => picture.Source = source);
        page.Children.Add(frame);

        var meta = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 6) };
        meta.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(3), Padding = new Thickness(7, 2, 7, 2), Background = new SolidColorBrush(NewsTagColour(item.Tag)), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = NewsTag(item.Tag), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0x12, 0x18)) },
        });
        TextBlock when = ShellText(NewsDate(item.Posted) + (item.Official ? "  ·  " + loc["News.FromGame"] : ""), "Brush.TextMuted", 12);
        when.Margin = new Thickness(10, 0, 0, 0);
        meta.Children.Add(when);
        page.Children.Add(meta);
        TextBlock title = Wrapped(item.Title, "Brush.Text", 26);
        title.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Title");
        page.Children.Add(title);
        if (NewsSummary(item) is { Length: > 0 } lede)
        {
            TextBlock line = Wrapped(lede, "Brush.TextMuted", 14.5);
            line.Margin = new Thickness(0, 8, 0, 0);
            line.LineHeight = 22;
            page.Children.Add(line);
        }

        // a patch note's headlines first, while the rest comes
        if (item.Points.Count > 0)
        {
            var box = new StackPanel();
            box.Children.Add(ShellText(loc["News.InShort"].ToUpperInvariant(), "Brush.TextMuted", 11, bold: true));
            foreach (string point in item.Points)
            {
                box.Children.Add(NewsListLine(point));
            }

            var card = new Border { Child = box, Padding = new Thickness(16, 12, 16, 8), Margin = new Thickness(0, 16, 0, 0), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1) };
            card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
            card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            page.Children.Add(card);
        }

        var body = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        page.Children.Add(body);
        if (item.Blocks is { Count: > 0 } blocks)
        {
            body.Children.Add(NewsBlocks(blocks));
        }
        else if (!Headless)
        {
            body.Children.Add(ShellText(loc["News.Loading"], "Brush.TextMuted", 13));
            _ = FillNewsAsync(item, body);
        }

        // where it leads: a page of the app, the website, the official notice
        var actions = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
        if (item.Page is { } target)
        {
            var go = new Button { Content = item.PageLabel ?? loc["Shell.Open"], Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 10, 0) };
            go.Click += (_, _) => OpenNewsTarget(target);
            actions.Children.Add(go);
        }

        if (item.Url is { } url)
        {
            var open = new Button { Content = loc[item.Official ? "News.OfficialSite" : "News.Website"] + "  ↗", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 10, 0) };
            open.Click += (_, _) => OpenUrl(url);
            actions.Children.Add(open);
        }

        if (actions.Children.Count > 0)
        {
            page.Children.Add(actions);
        }

        // the other news
        List<NewsItem> others = all.Where(o => o.Id != item.Id).Take(7).ToList();
        if (others.Count > 0)
        {
            TextBlock more = ShellText(loc["News.More"].ToUpperInvariant(), "Brush.TextMuted", 11, bold: true);
            more.Margin = new Thickness(0, 28, 0, 8);
            page.Children.Add(more);
            foreach (NewsItem other in others)
            {
                page.Children.Add(NewsRow(other));
            }
        }

        return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };
    }

    private async Task FillNewsAsync(NewsItem item, StackPanel body)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        IReadOnlyList<NewsBlock>? blocks = await NewsClient.ArticleAsync(item);
        if (_shellPage != "news" || _newsOpen?.Id != item.Id)
        {
            return;
        }

        body.Children.Clear();
        body.Children.Add(blocks is { Count: > 0 } ? NewsBlocks(blocks) : Wrapped(loc[item.Url is null ? "News.Unreadable" : "News.UnreadableLink"], "Brush.TextMuted", 13));
    }

    /// <summary>The article: headings, paragraphs, list lines, tables and pictures in their order.</summary>
    private static UIElement NewsBlocks(IReadOnlyList<NewsBlock> blocks)
    {
        var stack = new StackPanel();
        for (int i = 0; i < blocks.Count; i++)
        {
            NewsBlock block = blocks[i];
            switch (block.Kind)
            {
                case "h":
                    TextBlock head = Wrapped(block.Text, "Brush.Text", 17);
                    head.FontWeight = FontWeights.SemiBold;
                    head.Margin = new Thickness(0, i == 0 ? 4 : 18, 0, 6);
                    stack.Children.Add(head);
                    break;
                case "li":
                    stack.Children.Add(NewsListLine(block.Text));
                    break;
                case "img":
                    var image = new Image { Stretch = Stretch.Uniform, MaxHeight = 560, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 12), Visibility = Visibility.Collapsed };
                    NewsPicture(block.Text, 1000, source =>
                    {
                        image.Source = source;
                        image.MaxWidth = source is BitmapSource bitmap ? bitmap.PixelWidth : double.PositiveInfinity;
                        image.Visibility = Visibility.Visible;
                    });
                    stack.Children.Add(image);
                    break;
                case "row":
                    var rows = new List<IReadOnlyList<string>>();
                    while (i < blocks.Count && blocks[i].Kind == "row")
                    {
                        rows.Add(blocks[i].Cells);
                        i++;
                    }

                    i--;
                    stack.Children.Add(NewsTable(rows));
                    break;
                default:
                    TextBlock paragraph = Wrapped(block.Text, "Brush.Text", 13.5);
                    paragraph.LineHeight = 21;
                    paragraph.Margin = new Thickness(0, 0, 0, 9);
                    stack.Children.Add(paragraph);
                    break;
            }
        }

        return stack;
    }

    private static UIElement NewsListLine(string text)
    {
        var line = new DockPanel { Margin = new Thickness(6, 3, 0, 4) };
        TextBlock dot = ShellText("•", "Brush.Accent", 13.5, bold: true);
        dot.VerticalAlignment = VerticalAlignment.Top;
        dot.Margin = new Thickness(0, 0, 9, 0);
        DockPanel.SetDock(dot, Dock.Left);
        line.Children.Add(dot);
        TextBlock words = Wrapped(text, "Brush.Text", 13.5);
        words.LineHeight = 21;
        line.Children.Add(words);
        return line;
    }

    /// <summary>A table of the article (the first row its heads), its cells side by side.</summary>
    private static UIElement NewsTable(List<IReadOnlyList<string>> rows)
    {
        int columns = Math.Max(1, rows.Max(r => r.Count));
        var grid = new Grid();
        for (int c = 0; c < columns; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(c == 0 && columns > 2 ? 1.2 : 1, GridUnitType.Star) });
        }

        for (int r = 0; r < rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var back = new Border();
            back.SetResourceReference(Border.BackgroundProperty, r == 0 ? "Brush.ControlRaised" : r % 2 == 0 ? "Brush.RowAlt" : "Brush.Panel");
            Grid.SetRow(back, r);
            Grid.SetColumnSpan(back, columns);
            grid.Children.Add(back);
            for (int c = 0; c < rows[r].Count; c++)
            {
                TextBlock cell = Wrapped(rows[r][c], r == 0 ? "Brush.Text" : "Brush.TextMuted", 12.5);
                cell.FontWeight = r == 0 ? FontWeights.SemiBold : FontWeights.Normal;
                cell.Margin = new Thickness(10, 6, 10, 6);
                cell.VerticalAlignment = VerticalAlignment.Top;
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                if (c == rows[r].Count - 1 && rows[r].Count < columns)
                {
                    Grid.SetColumnSpan(cell, columns - c);
                }

                grid.Children.Add(cell);
            }
        }

        var box = new Border { Child = grid, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 4, 0, 12), ClipToBounds = true };
        box.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return box;
    }

    /// <summary>A line of the other news: its small picture, kind, title and day; a click opens it.</summary>
    private UIElement NewsRow(NewsItem item)
    {
        var grid = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var thumb = new Grid { Width = 108, Height = 36, HorizontalAlignment = HorizontalAlignment.Left, Clip = new RectangleGeometry(new Rect(0, 0, 108, 36), 4, 4) };
        thumb.Children.Add(new Border { Background = NightBrush() });
        var picture = new Image { Stretch = Stretch.UniformToFill };
        thumb.Children.Add(picture);
        NewsPicture(item.Image, 1200, source => picture.Source = source);
        grid.Children.Add(thumb);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        TextBlock head = ShellText(item.Title, "Brush.Text", 13.5, bold: true);
        head.TextTrimming = TextTrimming.CharacterEllipsis;
        words.Children.Add(head);
        words.Children.Add(ShellText(NewsTag(item.Tag) + "  ·  " + NewsDate(item.Posted), "Brush.TextSubtle", 11.5));
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);
        var row = new Border { Child = grid, Padding = new Thickness(8, 6, 8, 6), CornerRadius = new CornerRadius(6), Margin = new Thickness(-8, 0, -8, 2), Background = Brushes.Transparent };
        row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            OpenNews(item);
        };
        return row;
    }

    /// <summary>Where a post's button goes: the first-start setup, else a page of the app.</summary>
    private void OpenNewsTarget(string target)
    {
        if (target == "setup")
        {
            ShowLookSetup();
            return;
        }

        if (target.StartsWith("assist.", StringComparison.Ordinal) || target is "account" or "settings" or "checklist" || ShellPages().ContainsKey(target)
            || ShellHubs.Any(h => h.Hub == target))
        {
            ShowShellPage(target);
        }
    }

    // ---- pictures ----

    private static readonly Dictionary<string, ImageSource> NewsPictures = new(StringComparer.Ordinal);
    private static readonly HttpClient PictureHttp = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>
    /// The picture at <paramref name="url"/> handed to <paramref name="show"/> once there: from memory, the data folder or the website (or the game's own hosts for a
    /// picture in an official notice). Null: the app's own (the Atreia cover, assets/news/atreia.jpg). Nothing when it cannot be had now.
    /// </summary>
    private static void NewsPicture(string? url, int width, Action<ImageSource> show)
    {
        string key = (url ?? "bundled") + "@" + width;
        lock (NewsPictures)
        {
            if (NewsPictures.TryGetValue(key, out ImageSource? kept))
            {
                show(kept);
                return;
            }
        }

        if (url is null || Headless)
        {
            string file = System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "news", "atreia.jpg");
            if (System.IO.File.Exists(file) && DecodeNewsPicture(System.IO.File.ReadAllBytes(file), width) is { } own)
            {
                lock (NewsPictures)
                {
                    NewsPictures[key] = own;
                }

                show(own);
            }

            return;
        }

        _ = LoadNewsPictureAsync(url, width, key, show);
    }

    private static async Task LoadNewsPictureAsync(string url, int width, string key, Action<ImageSource> show)
    {
        ImageSource? source = await Task.Run(async () =>
        {
            string file = System.IO.Path.Combine(NewsClient.PictureFolder, Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(url))) + ".img");
            try
            {
                if (System.IO.File.Exists(file) && DecodeNewsPicture(await System.IO.File.ReadAllBytesAsync(file), width) is { } kept)
                {
                    return kept;
                }

                using HttpResponseMessage response = await PictureHttp.GetAsync(url);
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 8_000_000)
                {
                    return null;
                }

                byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                if (bytes.Length > 8_000_000 || DecodeNewsPicture(bytes, width) is not { } fetched)
                {
                    return null;
                }

                System.IO.Directory.CreateDirectory(NewsClient.PictureFolder);
                await System.IO.File.WriteAllBytesAsync(file, bytes);
                return fetched;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.IO.IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                return null;
            }
        });
        if (source is null)
        {
            return;
        }

        lock (NewsPictures)
        {
            NewsPictures[key] = source;
        }

        show(source);
    }

    private static ImageSource? DecodeNewsPicture(byte[] bytes, int width)
    {
        try
        {
            // a picture wider than asked is read smaller; a smaller one (an icon in a notice) at its own size
            int natural = BitmapDecoder.Create(new System.IO.MemoryStream(bytes), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0].PixelWidth;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = new System.IO.MemoryStream(bytes);
            if (natural > width)
            {
                bitmap.DecodePixelWidth = width;
            }

            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is NotSupportedException or System.IO.IOException or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
}
