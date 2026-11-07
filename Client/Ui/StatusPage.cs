using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDPS.Aion2;
using AionDPS.Aion2.Capture;
using AionDPS.Combat.Sources;
using AionDPS.History;
using AionDPS.Update;
using AionDPS.Upload;
using static AionDPS.Ui.JourneyViews;

namespace AionDPS.Ui;

/// <summary>
/// Status (the column's bottom, 6 November 2026, the owner: "reliability before new features ... a small health panel and a one-click diagnostics report"):
/// what the app is doing right now, each part with a dot - green when it works, amber when it waits for something, red when something is wrong - and one
/// line of what it sees: the game connection, the character read, the Atreia account and the character's verification, the legion list and its send, the
/// minimap, the game window's pictures and what the app itself costs. "Copy the report" puts all of it on the clipboard as text to paste into a message;
/// nothing secret is in it (no sign-in token, no stream key, no file of the player's).
/// </summary>
internal sealed class StatusPage : Grid
{
    private readonly Func<Aion2EntityDirectory?> _directory;
    private readonly Action _openNetwork;
    private readonly StackPanel _rows = new();
    private readonly TextBlock _copied;
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private List<Line> _last = new();

    private static LocalizationManager Loc => LocalizationManager.Instance;

    private enum Mark { Good, Wait, Bad, None }

    private sealed record Line(string Title, Mark Mark, string Text, string? Detail = null);

    public StatusPage(Func<Aion2EntityDirectory?> directory, Action openNetwork)
    {
        _directory = directory;
        _openNetwork = openNetwork;
        var root = new StackPanel { Margin = new Thickness(24, 18, 24, 24), MaxWidth = 980, HorizontalAlignment = HorizontalAlignment.Left };
        TextBlock title = Text(Loc["Status.Title"], "Brush.Text", 26);
        title.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Title");
        root.Children.Add(title);
        TextBlock lede = Wrapped(Loc["Status.Lede"], "Brush.TextMuted", 13);
        lede.Margin = new Thickness(0, 4, 0, 16);
        root.Children.Add(lede);

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        var copy = new Button { Content = Loc["Status.Copy"], Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        copy.SetResourceReference(Control.BorderBrushProperty, "Brush.Accent");
        _copied = Text("", "Brush.Heal", 12, bold: true);
        _copied.VerticalAlignment = VerticalAlignment.Center;
        _copied.Margin = new Thickness(8, 0, 0, 0);
        copy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(Report());
                _copied.Text = Loc["Status.Copied"];
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                _copied.Text = "";
            }
        };
        var folder = new Button { Content = Loc["Status.Folder"], Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        folder.Click += (_, _) => Open(DataFolder);
        var network = new Button { Content = Loc["Status.Network"], Padding = new Thickness(14, 6, 14, 6) };
        network.Click += (_, _) => _openNetwork();
        tools.Children.Add(copy);
        tools.Children.Add(folder);
        tools.Children.Add(network);
        tools.Children.Add(_copied);
        root.Children.Add(tools);

        var card = new Border { Child = _rows, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(6, 4, 6, 4) };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        root.Children.Add(card);
        Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root });

        Draw();
        if (!MainWindow.Headless)
        {
            _timer.Tick += (_, _) => Draw();
            _timer.Start();
            Unloaded += (_, _) => _timer.Stop();
            Loaded += (_, _) => _timer.Start();
        }
    }

    public void Stop() => _timer.Stop();

    private static string DataFolder => Environment.GetEnvironmentVariable("AIONDPS_DATA_DIR") is { Length: > 0 } dir
        ? dir : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Brand.DataFolder);

    private static void Open(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            // nothing to open
        }
    }

    private void Draw()
    {
        _last = Lines();
        _rows.Children.Clear();
        for (int i = 0; i < _last.Count; i++)
        {
            _rows.Children.Add(Row(_last[i], i == _last.Count - 1));
        }
    }

    private static UIElement Row(Line line, bool last)
    {
        var grid = new Grid { Margin = new Thickness(14, 10, 14, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Color colour = line.Mark switch
        {
            Mark.Good => Color.FromRgb(0x5F, 0xD0, 0x8A),
            Mark.Wait => Color.FromRgb(0xF0, 0xB7, 0x1C),
            Mark.Bad => Color.FromRgb(0xF0, 0x6A, 0x5E),
            _ => Color.FromRgb(0x80, 0x84, 0x8E),
        };
        grid.Children.Add(new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(colour), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 0, 0) });
        TextBlock title = Text(line.Title, "Brush.Text", 13, bold: true);
        title.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);
        var words = new StackPanel();
        words.Children.Add(Wrapped(line.Text, "Brush.Text", 13));
        if (line.Detail is { Length: > 0 } detail)
        {
            TextBlock small = Wrapped(detail, "Brush.TextSubtle", 11.5);
            small.Margin = new Thickness(0, 2, 0, 0);
            words.Children.Add(small);
        }

        Grid.SetColumn(words, 2);
        grid.Children.Add(words);
        var row = new Border { Child = grid, BorderThickness = new Thickness(0, 0, 0, last ? 0 : 1) };
        row.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return row;
    }

    private static string F(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, Loc[key], args);

    private static string Ago(DateTime utc)
    {
        TimeSpan t = DateTime.UtcNow - utc;
        return t < TimeSpan.FromMinutes(1) ? F("Status.AgoSeconds", Math.Max(1, (int)t.TotalSeconds))
            : t < TimeSpan.FromHours(1) ? F("Status.AgoMinutes", (int)t.TotalMinutes)
            : t < TimeSpan.FromDays(1) ? F("Status.AgoHours", (int)t.TotalHours) : F("Status.AgoDays", (int)t.TotalDays);
    }

    /// <summary>Everything the page shows, as lines (the page and the report read the same).</summary>
    private List<Line> Lines()
    {
        var lines = new List<Line>();
        Aion2EntityDirectory? dir = _directory();

        // the game connection: what the capture says, and the traffic it sees
        NetworkSnapshot net = NetworkStats.Snapshot();
        SourceStatus? status = MainWindow.LastSourceStatus;
        bool running = GameConnections.GameRunning();
        if (status is { State: SourceState.Error } bad)
        {
            lines.Add(new Line(Loc["Status.Game"], Mark.Bad, bad.Message));
        }
        else if (net.Connected && net.SinceLastData is { } quiet && quiet < TimeSpan.FromSeconds(30))
        {
            string ping = net.PingAverageMs is double p ? F("Status.Ping", Math.Round(p)) : "";
            lines.Add(new Line(Loc["Status.Game"], Mark.Good, Loc["Status.GameOn"] + (ping.Length > 0 ? "  ·  " + ping : ""),
                F("Status.GameDetail", net.Retransmissions, net.Gaps, net.Dropped)));
        }
        else if (running)
        {
            lines.Add(new Line(Loc["Status.Game"], Mark.Wait, Loc["Status.GameWaiting"], status?.Message));
        }
        else
        {
            lines.Add(new Line(Loc["Status.Game"], Mark.None, Loc["Status.GameOff"]));
        }

        // the character the game named at the login
        if (dir?.LocalCharacter is { Name.Length: > 0 } me)
        {
            string who = me.Name + (me.ServerId > 0 ? " · " + Aion2.Protocol.Aion2Servers.NameOf(me.ServerId) : "")
                + (Aion2.Protocol.Aion2SkillNames.ClassFromCode(me.ClassCode) is { } cls ? " · " + cls : "") + (me.Level > 0 ? " · " + F("Status.Level", me.Level) : "");
            lines.Add(me.Restored
                ? new Line(Loc["Status.Character"], Mark.Wait, who, Loc["Status.CharacterRestored"])
                : new Line(Loc["Status.Character"], Mark.Good, who, F("Status.CharacterAt", Ago(me.ReceivedAt.ToUniversalTime()))));
        }
        else
        {
            lines.Add(new Line(Loc["Status.Character"], running ? Mark.Wait : Mark.None, Loc["Status.CharacterNone"]));
        }

        // the Atreia account, the character's verification, uploads
        if (PrivacyConsent.Restricted)
        {
            lines.Add(new Line(Loc["Status.Account"], Mark.None, Loc["Status.UploadsOff"]));
        }
        else if (!Account.SignedIn)
        {
            lines.Add(new Line(Loc["Status.Account"], Mark.Wait, Loc["Status.SignedOut"]));
        }
        else
        {
            string verify = CharacterVerify.Last switch
            {
                { Kind: "verified" } v => F("Status.Verified", v.Name),
                { Kind: "mismatch" } v => F("Status.VerifyMismatch", v.Name),
                { Kind: "taken" } v => F("Status.VerifyTaken", v.Name),
                { Kind: "full" } => Loc["Status.VerifyFull"],
                { } v => F("Status.VerifyLater", v.Name),
                null => Loc["Status.VerifyNotYet"],
            };
            Mark mark = UploadClient.IsBlocked ? Mark.Bad : CharacterVerify.Last?.Kind is "mismatch" or "taken" or "full" ? Mark.Wait : Mark.Good;
            lines.Add(new Line(Loc["Status.Account"], mark, F("Status.SignedIn", Account.Name ?? "?") + "  ·  " + verify,
                UploadClient.IsBlocked ? Loc["Status.TooOld"] : LastUpload()));
        }

        // the legion list from the login, and its send to the website
        if (dir is { } d && d.LegionRosterAt > DateTime.MinValue)
        {
            LegionSnapshot? kept = LegionStore.Legions().FirstOrDefault();
            string text = F("Status.LegionRead", d.LegionRoster().Count, Ago(d.LegionRosterAt)) + (kept is null ? "" : "  ·  " + kept.Legion);
            lines.Add(new Line(Loc["Status.Legion"], kept is null ? Mark.Wait : Mark.Good, text,
                LegionShare.Last is { } sent ? AccountWindow.LegionText(sent) : kept is null ? Loc["Status.LegionNoName"] : null));
        }
        else if (LegionStore.Legions().FirstOrDefault() is { } kept)
        {
            lines.Add(new Line(Loc["Status.Legion"], Mark.None, F("Status.LegionKept", kept.Legion, kept.Members.Count, kept.At.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)),
                LegionShare.Last is { } sent ? AccountWindow.LegionText(sent) : null));
        }
        else
        {
            lines.Add(new Line(Loc["Status.Legion"], Mark.None, Loc["Status.LegionNone"]));
        }

        // the minimap, and the game window's pictures it and the menu watch take
        string minimap = MinimapReader.State switch
        {
            MinimapReader.ReadState.Off => Loc["Cubes.MinimapOff"],
            MinimapReader.ReadState.Searching => Loc["Cubes.MinimapSearching"],
            MinimapReader.ReadState.Reading => F("Cubes.MinimapReading", MinimapReader.RecentFixes, Math.Round(MinimapReader.LastMs)),
            MinimapReader.ReadState.NotSeen => Loc["Cubes.MinimapNotSeen"],
            _ => Loc["Cubes.MinimapWaiting"],
        };
        lines.Add(new Line(Loc["Status.Minimap"], MinimapReader.State switch
        {
            MinimapReader.ReadState.Reading => Mark.Good,
            MinimapReader.ReadState.Searching or MinimapReader.ReadState.NotSeen => Mark.Wait,
            _ => Mark.None,
        }, minimap));
        var (perSecond, average, longest, shared) = GameCapture.Stats(TimeSpan.FromSeconds(10));
        lines.Add(new Line(Loc["Status.Pictures"], perSecond > 4.5 || longest > 120 ? Mark.Wait : Mark.Good,
            perSecond <= 0 ? Loc["Status.PicturesNone"] : F("Status.PicturesRate", perSecond.ToString("0.0", CultureInfo.CurrentCulture), Math.Round(average), Math.Round(longest)),
            shared > 0 ? F("Status.PicturesShared", shared) : null));

        // the app itself
        var (cpu, memory) = AppPerf.ProcessUse();
        var (ticks, tickAvg, tickMax) = AppPerf.TickStats();
        var (stalls, stallLongest) = AppPerf.StallStats();
        Mark appMark = stalls > 5 || stallLongest > 1000 || cpu > 25 ? Mark.Wait : Mark.Good;
        lines.Add(new Line(Loc["Status.App"], appMark, F("Status.AppUse", Math.Round(cpu, 1).ToString(CultureInfo.CurrentCulture), memory),
            F("Status.AppDetail", ticks == 0 ? "–" : Math.Round(tickAvg, 1).ToString(CultureInfo.CurrentCulture), Math.Round(tickMax), stalls, Math.Round(stallLongest))));
        lines.Add(new Line(Loc["Status.Version"], Mark.None, AppVersion.Text + "  ·  " + Environment.OSVersion.VersionString,
            F("Status.Running", AppPerf.Started.ToString("g", CultureInfo.CurrentCulture))));
        return lines;
    }

    /// <summary>The last line of the automatic uploads' log (auto-upload.log), with its time.</summary>
    private static string? LastUpload()
    {
        try
        {
            string path = Path.Combine(DataFolder, "auto-upload.log");
            if (!File.Exists(path))
            {
                return null;
            }

            string? last = File.ReadLines(path).LastOrDefault(l => l.Length > 0);
            return last is null ? null : F("Status.LastUpload", last);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The page as text, to paste into a message: what each part says. No sign-in token, stream key or file of the player's is in it.</summary>
    private string Report()
    {
        var text = new StringBuilder();
        text.AppendLine("Atreia App status · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        foreach (Line line in Lines())
        {
            string mark = line.Mark switch { Mark.Good => "OK  ", Mark.Wait => "WAIT", Mark.Bad => "BAD ", _ => "--  " };
            text.Append(mark).Append(' ').Append(line.Title).Append(": ").AppendLine(line.Text);
            if (line.Detail is { Length: > 0 } detail)
            {
                text.Append("       ").AppendLine(detail);
            }
        }

        return text.ToString();
    }
}
