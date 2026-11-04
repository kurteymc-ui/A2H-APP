using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AionDPS.Schedule;

namespace AionDPS.Ui;

/// <summary>What an event is doing now, as the Timers page and the overlay say it: "Portals open", "close in", until when.</summary>
internal readonly record struct TimerPhase(string Label, string Verb, DateTime Until);

/// <summary>
/// The look the Timers page and the timers overlay share with the website's event timers: one colour and one in-game icon per event (ScheduledEvent.Look),
/// the countdown written the same way ("1d 4h", "4h 05m", "12:34"), and what a running event is doing (live, the rift's portals open, the battlefield's queue).
/// Brushes and pictures are made once and frozen: a page that ticks every second makes none of them again.
/// </summary>
internal static class TimerLook
{
    /// <summary>The website's event colours (its --c-&lt;id&gt; values), by look.</summary>
    private static readonly Dictionary<string, Color> Palette = new(StringComparer.Ordinal)
    {
        ["shugo"] = Color.FromRgb(0xF0, 0xC7, 0x83),
        ["invasion"] = Color.FromRgb(0x6F, 0xCF, 0xBD),
        ["rift"] = Color.FromRgb(0xA9, 0x91, 0xF2),
        ["kaira"] = Color.FromRgb(0xEF, 0x8F, 0x8F),
        ["arena"] = Color.FromRgb(0x80, 0xA9, 0xF0),
        ["siege"] = Color.FromRgb(0xF2, 0xA6, 0x5A),
        ["exec"] = Color.FromRgb(0xE2, 0x78, 0x5C),
        ["nahma"] = Color.FromRgb(0xD8, 0x67, 0xA0),
        ["mboss"] = Color.FromRgb(0xB9, 0xC4, 0x6A),
        ["reset"] = Color.FromRgb(0xCF, 0xC3, 0xB2),
    };

    /// <summary>The colour of an event the website has none for (a new one the server sent): the website's gold.</summary>
    private static readonly Color Other = Color.FromRgb(0xE8, 0xC4, 0x8F);

    private static readonly Dictionary<(string, byte), SolidColorBrush> BrushCache = new();
    private static readonly Dictionary<string, ImageSource?> IconCache = new(StringComparer.Ordinal);

    /// <summary>The dark ground behind an icon (the in-game icons are drawn for a dark ground, whatever the theme).</summary>
    private static readonly SolidColorBrush IconGround = Frozen(Color.FromRgb(0x0D, 0x0B, 0x10));

    public static Color ColorOf(string look) => Palette.TryGetValue(look, out Color color) ? color : Other;

    /// <summary>The event's colour as a frozen brush, <paramref name="alpha"/> 255 = solid.</summary>
    public static SolidColorBrush BrushOf(string look, byte alpha = 0xFF)
    {
        if (!BrushCache.TryGetValue((look, alpha), out SolidColorBrush? brush))
        {
            Color c = ColorOf(look);
            brush = Frozen(Color.FromArgb(alpha, c.R, c.G, c.B));
            BrushCache[(look, alpha)] = brush;
        }

        return brush;
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>The event's in-game icon (assets/aion2/schedule/icons/&lt;look&gt;.png, 96 px), read once; null when there is none.</summary>
    public static ImageSource? IconOf(string look)
    {
        if (IconCache.TryGetValue(look, out ImageSource? known))
        {
            return known;
        }

        ImageSource? image = null;
        // the look comes from the server's table: only a plain name is taken as a file name
        if (look.Length is > 0 and <= 40 && look.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
        {
            string path = System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "schedule", "icons", look + ".png");
            try
            {
                if (File.Exists(path))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(path, UriKind.Absolute);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    image = bitmap;
                }
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or InvalidOperationException)
            {
                image = null;
            }
        }

        IconCache[look] = image;
        return image;
    }

    /// <summary>The event's icon in a frame of its colour, as on the website (a dot of its colour when it has no icon).</summary>
    public static FrameworkElement IconFrame(ScheduledEvent scheduled, double size)
    {
        string look = scheduled.Look;
        bool small = size < 30;
        var frame = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(small ? 5 : 8), BorderThickness = new Thickness(small ? 1.5 : 2), BorderBrush = BrushOf(look),
            Background = IconGround, Padding = new Thickness(small ? 1 : 2), VerticalAlignment = VerticalAlignment.Center, SnapsToDevicePixels = true,
        };
        if (IconOf(look) is { } icon)
        {
            var image = new Image { Source = icon, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            frame.Child = image;
        }
        else
        {
            frame.Child = new Border { Width = size / 3, Height = size / 3, CornerRadius = new CornerRadius(size / 6), Background = BrushOf(look) };
        }

        return frame;
    }

    /// <summary>A small round dot of the event's colour (the ring's key, the week's resets row).</summary>
    public static FrameworkElement Dot(string look, double size = 10) =>
        new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = BrushOf(look), VerticalAlignment = VerticalAlignment.Center };

    /// <summary>The countdown as the website writes it: days and hours, hours and minutes, or minutes and seconds.</summary>
    public static string Countdown(TimeSpan left)
    {
        if (left < TimeSpan.Zero)
        {
            left = TimeSpan.Zero;
        }

        long s = (long)Math.Floor(left.TotalSeconds);
        long days = s / 86400, hours = s % 86400 / 3600, minutes = s % 3600 / 60, seconds = s % 60;
        return days > 0 ? $"{days}d {hours}h" : hours > 0 ? $"{hours}h {minutes:00}m" : $"{minutes}:{seconds:00}";
    }

    /// <summary>A time of day in the player's own clock style ("21:00", "9:00 PM").</summary>
    public static string Clock(DateTime t) => t.ToString("t", CultureInfo.CurrentCulture);

    /// <summary>When an occurrence starts, said from today: "21:00", "Tomorrow 21:00", "Mon 21:00".</summary>
    public static string When(DateTime t, DateTime now)
    {
        int days = (t.Date - now.Date).Days;
        return days == 0 ? Clock(t)
            : days == 1 ? LocalizationManager.Instance["Timers.Tomorrow"] + " " + Clock(t)
            : t.ToString("ddd", CultureInfo.CurrentCulture) + " " + Clock(t);
    }

    /// <summary>The name with what the event is ("Battlefield · 10v10"), and "(possible)" for slots the game may skip.</summary>
    public static string Label(ScheduledEvent scheduled, string language)
    {
        string name = scheduled.Players is { Length: > 0 } players ? scheduled.NameIn(language) + " · " + players : scheduled.NameIn(language);
        return scheduled.IsPossible ? name + " " + LocalizationManager.Instance["Home.Possible"] : name;
    }

    /// <summary>What a running occurrence is doing at <paramref name="now"/>: the rift's portals open, then the rift up with them closed; the battlefield's
    /// queue open; else live. With the moment that phase ends.</summary>
    public static TimerPhase PhaseOf(EventOccurrence occurrence, DateTime now)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        if (occurrence.Event.OpenMinutes > 0)
        {
            return now < occurrence.OpenUntil
                ? new TimerPhase(loc["Timers.PortalsOpen"], loc["Timers.PortalsCloseIn"], occurrence.OpenUntil)
                : new TimerPhase(loc["Timers.RiftUp"], loc["Timers.EndsIn"], occurrence.End);
        }

        return occurrence.Event.Kind == "battlefield"
            ? new TimerPhase(loc["Timers.QueueOpen"], loc["Timers.QueueClosesIn"], occurrence.End)
            : new TimerPhase(loc["Timers.LiveNow"], loc["Timers.EndsIn"], occurrence.End);
    }

    /// <summary>The clock the events run on: the own region's server clock (each scheduled event of the table has it), else the PC's own.</summary>
    public static TimeZoneInfo ServerZone(IReadOnlyList<ScheduledEvent> events)
    {
        foreach (ScheduledEvent scheduled in events)
        {
            if (!scheduled.Always && (scheduled.SourceZone ?? (scheduled.IsUtc ? TimeZoneInfo.Utc : null)) is { } zone)
            {
                return zone;
            }
        }

        return TimeZoneInfo.Local;
    }

    /// <summary>A local moment on the server's wall clock.</summary>
    public static DateTime OnServer(DateTime local, TimeZoneInfo zone) =>
        ReferenceEquals(zone, TimeZoneInfo.Local) ? local : TimeZoneInfo.ConvertTimeFromUtc(local.ToUniversalTime(), zone);

    /// <summary>The name of the own server's region ("NA East"); the European table before the meter has seen the own character.</summary>
    public static string RegionName(string? region) => LocalizationManager.Instance[region switch
    {
        "nae" => "Timers.Region.nae",
        "naw" => "Timers.Region.naw",
        "latam" => "Timers.Region.latam",
        "asia" => "Timers.Region.asia",
        _ => "Timers.Region.eu",
    }];

    /// <summary>The next daily reset: 07:00 UTC on every server (16:00 on the game's Korean clock, see ChecklistManual), as local time.</summary>
    public static DateTime NextDailyReset(DateTime now)
    {
        DateTime utc = now.ToUniversalTime();
        DateTime next = DateTime.SpecifyKind(utc.Date.AddHours(7), DateTimeKind.Utc);
        if (next <= utc)
        {
            next = next.AddDays(1);
        }

        return next.ToLocalTime();
    }

    /// <summary>The next weekly reset: Wednesday 07:00 UTC, as local time.</summary>
    public static DateTime NextWeeklyReset(DateTime now)
    {
        DateTime next = NextDailyReset(now).ToUniversalTime();
        while (next.DayOfWeek != DayOfWeek.Wednesday)
        {
            next = next.AddDays(1);
        }

        return next.ToLocalTime();
    }

    /// <summary>The text of a theme brush's text block: a small helper the page and the overlay share.</summary>
    public static TextBlock Text(string text, string brush, double size, bool bold = false)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }
}
