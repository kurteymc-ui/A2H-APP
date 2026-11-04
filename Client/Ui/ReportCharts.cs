using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using AionDPS.Combat;

namespace AionDPS.Ui;

/// <summary>The colours of the report's charts: one per line of a stacked chart, the last for "Other".</summary>
internal static class ReportPalette
{
    public static readonly Color[] Series =
    {
        Color.FromRgb(0xD9, 0x46, 0x7E), Color.FromRgb(0x22, 0xB5, 0xC8), Color.FromRgb(0xD9, 0xB5, 0x26),
        Color.FromRgb(0x4F, 0xC1, 0x7A), Color.FromRgb(0x9B, 0x7F, 0xE6), Color.FromRgb(0xE0, 0x8A, 0x3C),
    };

    public static readonly Color Other = Color.FromRgb(0x7D, 0x74, 0x80);
    public static readonly Color Good = Color.FromRgb(0x5C, 0xC2, 0x7E);
    public static readonly Color Bad = Color.FromRgb(0xE0, 0x6A, 0x5E);
    public static readonly Color Cast = Color.FromRgb(0xF0, 0xA0, 0x1E);
    public static readonly Color Party = Color.FromRgb(0xF2, 0xEE, 0xE8);

    public static Color For(int index, string name) => name == "Other" ? Other : Series[index % Series.Length];

    public static SolidColorBrush Brush(Color c, byte alpha = 0xFF)
    {
        var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    /// <summary>12,345,678 as 12.3M, 48,210 as 48.2K: the axes' numbers.</summary>
    public static string Short(double v) => v >= 1_000_000 ? (v / 1_000_000).ToString("0.0", CultureInfo.InvariantCulture) + "M"
        : v >= 1000 ? (v / 1000).ToString("0.0", CultureInfo.InvariantCulture) + "K"
        : v.ToString("0", CultureInfo.InvariantCulture);

    public static string Clock(double seconds)
    {
        int s = Math.Max(0, (int)Math.Round(seconds));
        return $"{s / 60}:{s % 60:00}";
    }
}

/// <summary>Draws text and axes for the report charts in the window's font.</summary>
internal abstract class ReportChartBase : FrameworkElement
{
    protected const double Left = 56, Right = 14, Top = 12;

    protected FormattedText Text(string text, double size, Brush brush, bool bold = false)
    {
        var family = TextElement.GetFontFamily(this);
        return new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal), size, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    protected Brush Res(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    /// <summary>A round top for the axis: 1, 2, 2.5, 5 or 10 times a power of ten above the value.</summary>
    protected static double NiceMax(double max)
    {
        if (max <= 0)
        {
            return 1;
        }

        double power = Math.Pow(10, Math.Floor(Math.Log10(max)));
        foreach (double step in new[] { 1, 1.25, 2, 2.5, 4, 5, 10 })
        {
            if (step * power >= max)
            {
                return step * power;
            }
        }

        return 10 * power;
    }

    /// <summary>Four grid lines with their numbers and the time axis under the plot.</summary>
    protected void Axes(DrawingContext dc, Rect plot, double max, double seconds)
    {
        Brush muted = Res("Brush.TextMuted", Brushes.Gray);
        var grid = new Pen(new SolidColorBrush(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF)), 1);
        for (int i = 0; i <= 4; i++)
        {
            double y = plot.Bottom - plot.Height * i / 4;
            dc.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
            if (i > 0)
            {
                FormattedText label = Text(ReportPalette.Short(max * i / 4), 10.5, muted);
                dc.DrawText(label, new Point(plot.Left - 8 - label.Width, y - label.Height / 2));
            }
        }

        TimeAxis(dc, plot, seconds, plot.Bottom + 6);
    }

    protected void TimeAxis(DrawingContext dc, Rect plot, double seconds, double y)
    {
        Brush muted = Res("Brush.TextMuted", Brushes.Gray);
        double step = seconds <= 60 ? 10 : seconds <= 180 ? 30 : seconds <= 600 ? 60 : seconds <= 1800 ? 300 : 600;
        for (double t = 0; t <= seconds + 0.01; t += step)
        {
            double x = plot.Left + plot.Width * t / seconds;
            FormattedText label = Text(ReportPalette.Clock(t), 10.5, muted);
            dc.DrawText(label, new Point(Math.Min(x - label.Width / 2, plot.Right - label.Width), y));
        }
    }
}

/// <summary>
/// A stacked area chart of a player's skills (or the sources of their damage taken) over the fight: per second, a band per line, the biggest at the
/// bottom. Hover shows the time and each line's value.
/// </summary>
internal sealed class StackedAreaChart : ReportChartBase
{
    private readonly IReadOnlyList<(string Name, double[] Values, Color Color)> _series;
    private readonly double _bin, _seconds;
    private double _hoverX = -1;

    public StackedAreaChart(IReadOnlyList<ReportSeries> series, double bin, double seconds)
    {
        _series = series.Select((s, i) => (s.Name, s.Values, ReportPalette.For(i, s.Name))).ToList();
        _bin = bin;
        _seconds = seconds;
        Height = 230;
        MouseMove += (_, e) =>
        {
            _hoverX = e.GetPosition(this).X;
            InvalidateVisual();
        };
        MouseLeave += (_, _) =>
        {
            _hoverX = -1;
            InvalidateVisual();
        };
    }

    public IEnumerable<(string Name, Color Color)> Legend => _series.Select(s => (s.Name, s.Color));

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        int bins = _series.Count == 0 ? 0 : _series.Max(s => s.Values.Length);
        if (bins == 0)
        {
            return;
        }

        var plot = new Rect(Left, Top, Math.Max(10, ActualWidth - Left - Right), Math.Max(10, ActualHeight - Top - 28));
        var stacked = new double[bins];
        var tops = new List<double[]>();
        foreach (var s in _series)
        {
            var top = new double[bins];
            for (int i = 0; i < bins; i++)
            {
                stacked[i] += i < s.Values.Length ? s.Values[i] : 0;
                top[i] = stacked[i];
            }

            tops.Add(top);
        }

        double max = NiceMax(stacked.Max() * 1.05);
        Axes(dc, plot, max, _seconds);
        double X(int i) => plot.Left + plot.Width * Math.Min(_seconds, (i + 0.5) * _bin) / _seconds;
        double Y(double v) => plot.Bottom - plot.Height * v / max;

        // drawn from the top band down, so each lower band covers the one above's bottom edge
        for (int k = _series.Count - 1; k >= 0; k--)
        {
            double[] top = tops[k];
            var geometry = new StreamGeometry();
            using (StreamGeometryContext g = geometry.Open())
            {
                g.BeginFigure(new Point(X(0), plot.Bottom), true, true);
                for (int i = 0; i < bins; i++)
                {
                    g.LineTo(new Point(X(i), Y(top[i])), true, true);
                }

                g.LineTo(new Point(X(bins - 1), plot.Bottom), true, true);
            }

            geometry.Freeze();
            Color c = _series[k].Color;
            dc.DrawGeometry(ReportPalette.Brush(c, 0xD8), new Pen(ReportPalette.Brush(Color.FromRgb((byte)Math.Min(255, c.R + 40), (byte)Math.Min(255, c.G + 40), (byte)Math.Min(255, c.B + 40))), 1.1), geometry);
        }

        if (_hoverX >= plot.Left && _hoverX <= plot.Right)
        {
            int i = Math.Clamp((int)((_hoverX - plot.Left) / plot.Width * _seconds / _bin), 0, bins - 1);
            var pen = new Pen(Res("Brush.TextMuted", Brushes.Gray), 1) { DashStyle = DashStyles.Dash };
            dc.DrawLine(pen, new Point(_hoverX, plot.Top), new Point(_hoverX, plot.Bottom));
            var lines = new List<FormattedText> { Text(ReportPalette.Clock(i * _bin), 11, Res("Brush.Text", Brushes.White), bold: true) };
            for (int k = 0; k < _series.Count; k++)
            {
                double v = i < _series[k].Values.Length ? _series[k].Values[i] : 0;
                if (v > 0)
                {
                    lines.Add(Text($"{_series[k].Name}  {v.ToString("N0", CultureInfo.InvariantCulture)}/s", 11, ReportPalette.Brush(_series[k].Color)));
                }
            }

            Tip(dc, lines, _hoverX, plot);
        }
    }

    internal static void Tip(DrawingContext dc, IReadOnlyList<FormattedText> lines, double x, Rect plot)
    {
        double w = lines.Max(l => l.Width) + 16, h = lines.Sum(l => l.Height + 2) + 10;
        double left = x + 12 + w > plot.Right ? x - 12 - w : x + 12;
        var box = new Rect(left, plot.Top + 4, w, h);
        dc.DrawRoundedRectangle(ReportPalette.Brush(Color.FromRgb(0x17, 0x14, 0x1B), 0xF2), new Pen(ReportPalette.Brush(Color.FromRgb(0x3A, 0x34, 0x41)), 1), box, 6, 6);
        double y = box.Top + 5;
        foreach (FormattedText line in lines)
        {
            dc.DrawText(line, new Point(box.Left + 8, y));
            y += line.Height + 2;
        }
    }
}

/// <summary>
/// The fight on one time axis: the party's DPS and each player's (over the last five seconds), the damage taken (dashed), the boss's stagger phases
/// shaded with their name, and lanes under it: whom the boss aimed at (in that player's class colour), its casts, its cast bars and the party's deaths.
/// </summary>
internal sealed class FightTimelineChart : ReportChartBase
{
    private const double LaneHeight = 18, LaneGap = 4, LaneLabel = 84;

    private readonly FightReport _report;
    private readonly Func<int, string> _classOf;
    private readonly HashSet<int> _hidden = new();
    private double _hoverX = -1;

    public FightTimelineChart(FightReport report, Func<int, string> classOf)
    {
        _report = report;
        _classOf = classOf;
        Height = 300 + 4 * (LaneHeight + LaneGap);
        MouseMove += (_, e) =>
        {
            _hoverX = e.GetPosition(this).X;
            InvalidateVisual();
        };
        MouseLeave += (_, _) =>
        {
            _hoverX = -1;
            InvalidateVisual();
        };
    }

    /// <summary>Shows or hides one player's line (the legend's clicks).</summary>
    public void Toggle(int playerId)
    {
        if (!_hidden.Remove(playerId))
        {
            _hidden.Add(playerId);
        }

        InvalidateVisual();
    }

    public bool IsShown(int playerId) => !_hidden.Contains(playerId);

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        double seconds = _report.Seconds;
        double lanes = 4 * (LaneHeight + LaneGap);
        var plot = new Rect(LaneLabel, Top, Math.Max(10, ActualWidth - LaneLabel - Right), Math.Max(10, ActualHeight - Top - lanes - 34));
        double bin = _report.BinSeconds;
        var visible = _report.Players.Where(p => !_hidden.Contains(p.Player.Id)).ToList();
        double top = Math.Max(_report.PartyLine.DefaultIfEmpty(0).Max(), visible.Select(p => p.DpsLine.DefaultIfEmpty(0).Max()).DefaultIfEmpty(0).Max());
        double max = NiceMax(top * 1.05);
        double X(double t) => plot.Left + plot.Width * Math.Clamp(t, 0, seconds) / seconds;
        double Y(double v) => plot.Bottom - plot.Height * Math.Min(v, max) / max;
        Brush muted = Res("Brush.TextMuted", Brushes.Gray);

        // the grid without the time axis: the lanes come between the plot and the axis
        var grid = new Pen(ReportPalette.Brush(Colors.White, 0x16), 1);
        for (int i = 0; i <= 4; i++)
        {
            double y = plot.Bottom - plot.Height * i / 4;
            dc.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
            if (i > 0)
            {
                FormattedText label = Text(ReportPalette.Short(max * i / 4), 10.5, muted);
                dc.DrawText(label, new Point(plot.Left - 8 - label.Width, y - label.Height / 2));
            }
        }

        foreach (StaggerPhase s in _report.Staggers)
        {
            var band = new Rect(new Point(X(s.Start), plot.Top), new Point(X(s.End), plot.Bottom));
            dc.DrawRectangle(ReportPalette.Brush(Color.FromRgb(0x6F, 0x9F, 0xD8), 0x22), new Pen(ReportPalette.Brush(Color.FromRgb(0x6F, 0x9F, 0xD8), 0x55), 1), band);
            FormattedText label = Text($"{_report.BossName ?? "Boss"} staggered", 10.5, ReportPalette.Brush(Color.FromRgb(0x8F, 0xB8, 0xE8)));
            dc.DrawText(label, new Point(band.Left + 4, plot.Top + 2));
        }

        Line(dc, _report.TakenLine, bin, X, Y, new Pen(ReportPalette.Brush(Color.FromRgb(0x9A, 0x92, 0x8A), 0xC0), 1.2) { DashStyle = DashStyles.Dash });
        foreach (ReportPlayerLine p in visible)
        {
            Line(dc, p.DpsLine, bin, X, Y, new Pen(ReportPalette.Brush(ClassBrushes.ClassColor(p.Player.ClassName), 0xE6), 1.4));
        }

        Line(dc, _report.PartyLine, bin, X, Y, new Pen(ReportPalette.Brush(ReportPalette.Party), 2));

        // the lanes
        double laneTop = plot.Bottom + 8;
        string[] names = { "Boss target", "Boss casts", "Boss cast bars", "Party" };
        for (int k = 0; k < names.Length; k++)
        {
            double y = laneTop + k * (LaneHeight + LaneGap);
            FormattedText label = Text(names[k], 10.5, muted);
            dc.DrawText(label, new Point(plot.Left - 8 - label.Width, y + (LaneHeight - label.Height) / 2));
            dc.DrawRectangle(ReportPalette.Brush(Colors.White, 0x08), null, new Rect(plot.Left, y, plot.Width, LaneHeight));
        }

        foreach ((double start, double end, int target) in _report.BossTargets)
        {
            var color = ClassBrushes.ClassColor(_classOf(target));
            dc.DrawRectangle(ReportPalette.Brush(color, 0xD0), null, new Rect(new Point(X(start), laneTop + 3), new Point(Math.Max(X(end), X(start) + 1), laneTop + LaneHeight - 3)));
        }

        double castsY = laneTop + LaneHeight + LaneGap;
        var tick = new Pen(ReportPalette.Brush(Color.FromRgb(0xC8, 0xC0, 0xB4), 0xB0), 1.2);
        foreach ((double at, string _) in _report.BossCasts)
        {
            dc.DrawLine(tick, new Point(X(at), castsY + 3), new Point(X(at), castsY + LaneHeight - 3));
        }

        double barsY = castsY + LaneHeight + LaneGap;
        foreach ((double start, double end, string _) in _report.CastBars)
        {
            dc.DrawRoundedRectangle(ReportPalette.Brush(ReportPalette.Cast), null, new Rect(new Point(X(start), barsY + 3), new Point(Math.Max(X(end), X(start) + 3), barsY + LaneHeight - 3)), 2, 2);
        }

        double partyY = barsY + LaneHeight + LaneGap;
        var death = new Pen(ReportPalette.Brush(ReportPalette.Bad), 2);
        foreach ((double at, int _) in _report.PartyDeaths)
        {
            dc.DrawLine(death, new Point(X(at), partyY + 2), new Point(X(at), partyY + LaneHeight - 2));
        }

        TimeAxis(dc, plot, seconds, partyY + LaneHeight + 6);

        if (_hoverX >= plot.Left && _hoverX <= plot.Right)
        {
            double t = (_hoverX - plot.Left) / plot.Width * seconds;
            int i = Math.Clamp((int)(t / bin), 0, Math.Max(0, _report.PartyLine.Length - 1));
            dc.DrawLine(new Pen(muted, 1) { DashStyle = DashStyles.Dash }, new Point(_hoverX, plot.Top), new Point(_hoverX, partyY + LaneHeight));
            var lines = new List<FormattedText>
            {
                Text(ReportPalette.Clock(t), 11, Res("Brush.Text", Brushes.White), bold: true),
                Text($"Party  {Value(_report.PartyLine, i)}", 11, ReportPalette.Brush(ReportPalette.Party)),
            };
            foreach (ReportPlayerLine p in visible.OrderByDescending(p => p.DpsLine.Length > i ? p.DpsLine[i] : 0))
            {
                lines.Add(Text($"{p.Player.Name}  {Value(p.DpsLine, i)}", 11, ClassBrushes.Text(p.Player.ClassName)));
            }

            lines.Add(Text($"Damage taken  {Value(_report.TakenLine, i)}", 11, muted));
            string? casting = _report.CastBars.Where(c => t >= c.Start && t <= c.End).Select(c => c.Name).FirstOrDefault();
            if (!string.IsNullOrEmpty(casting))
            {
                lines.Add(Text("Casting " + casting, 11, ReportPalette.Brush(ReportPalette.Cast)));
            }

            StackedAreaChart.Tip(dc, lines, _hoverX, plot);
        }
    }

    private static string Value(double[] line, int i) => line.Length > i ? line[i].ToString("N0", CultureInfo.InvariantCulture) : "0";

    private static void Line(DrawingContext dc, double[] values, double bin, Func<double, double> x, Func<double, double> y, Pen pen)
    {
        if (values.Length < 2)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (StreamGeometryContext g = geometry.Open())
        {
            g.BeginFigure(new Point(x(0.5 * bin), y(values[0])), false, false);
            for (int i = 1; i < values.Length; i++)
            {
                g.LineTo(new Point(x((i + 0.5) * bin), y(values[i])), true, true);
            }
        }

        geometry.Freeze();
        pen.LineJoin = PenLineJoin.Round;
        dc.DrawGeometry(null, pen, geometry);
    }
}
