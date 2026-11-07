using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AionDPS.Combat;

namespace AionDPS.Ui;

/// <summary>
/// The fight report's stagger chart (7 November 2026, the owner: "give exact stagger value at a time period, and showcase when mechanics start in the
/// stagger bar, make it clear"): the boss's stagger bar over the whole fight, how full it was; every stagger check as a band from the boss's cast that
/// started it to the end of that cast - green when the bar broke in time, red when it didn't - with the cast's name over a line where it starts and a red
/// dashed line where it ends, a diamond at each break and what was left where a check failed. Hovering reads the moment: the time, the bar (exactly, in
/// points), the check it is in. A click on a check picks that check; a drag picks a stretch; a click elsewhere the whole fight. The window shows, under it,
/// who took how much off the bar in what was picked.
/// </summary>
internal sealed class StaggerChart : ReportChartBase
{
    private static readonly Color Blue = StaggerCardView.Blue;
    private static readonly Color Green = Color.FromRgb(0x5B, 0xD0, 0x7A), Red = Color.FromRgb(0xFF, 0x5A, 0x5A), Grey = Color.FromRgb(0x9D, 0xB3, 0xC2);
    private static readonly Color Mechanic = ReportPalette.Cast;

    /// <summary>Above the plot: two rows of the casts' names.</summary>
    private const double LabelRows = 34;

    private readonly FightReport _report;
    private readonly double _seconds;
    private double _hoverX = -1;
    private double? _dragFrom;
    private Rect _plot;

    /// <summary>What is picked (seconds from the fight's start) and the check it is, if one.</summary>
    public (double From, double To, StaggerCheck? Check)? Selection { get; private set; }

    /// <summary>A check (a click on its band), a stretch (a drag) or the whole fight (a click elsewhere) was picked.</summary>
    public event Action<double, double, StaggerCheck?>? Picked;

    public StaggerChart(FightReport report)
    {
        _report = report;
        _seconds = Math.Max(1, report.Seconds);
        Height = 236;
        Cursor = Cursors.Cross;
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
        MouseLeftButtonDown += (_, e) =>
        {
            if (TimeAt(e.GetPosition(this).X) is double t)
            {
                _dragFrom = t;
                CaptureMouse();
            }
        };
        MouseLeftButtonUp += (_, e) =>
        {
            ReleaseMouseCapture();
            if (_dragFrom is not double from || TimeAt(Math.Clamp(e.GetPosition(this).X, _plot.Left, _plot.Right)) is not double to)
            {
                _dragFrom = null;
                return;
            }

            _dragFrom = null;
            if (Math.Abs(to - from) * _plot.Width / _seconds >= 6 && Math.Abs(to - from) >= 0.5)
            {
                Pick(Math.Min(from, to), Math.Max(from, to), null);
            }
            else if (CheckAt(to) is { } check)
            {
                (double a, double b) = Span(check);
                Pick(a, b, check);
            }
            else
            {
                Pick(0, _seconds, null);
            }
        };
    }

    private void Pick(double from, double to, StaggerCheck? check)
    {
        Select(from, to, check);
        Picked?.Invoke(from, to, check);
    }

    /// <summary>Shades what is picked.</summary>
    public void Select(double from, double to, StaggerCheck? check)
    {
        Selection = (from, to, check);
        InvalidateVisual();
    }

    /// <summary>A check's stretch on the chart: from the cast's start to its end (or the break, or the check's end when there is no cast).</summary>
    public (double From, double To) Span(StaggerCheck c)
    {
        double from = (c.Start - _report.StaggerCheckStart).TotalSeconds;
        double to = from + Math.Max(c.Limit ?? 0, c.Seconds(c.End ?? c.Start));
        return (Math.Clamp(from, 0, _seconds), Math.Clamp(Math.Max(to, from + 0.5), 0, _seconds));
    }

    /// <summary>The check whose band covers a moment (the latest one, should two touch).</summary>
    private StaggerCheck? CheckAt(double t) => _report.StaggerChecks.LastOrDefault(c => Span(c) is var s && t >= s.From - 0.3 && t <= s.To + 0.3);

    private double? TimeAt(double x) => _plot.Width > 0 && x >= _plot.Left - 2 && x <= _plot.Right + 2
        ? Math.Clamp((x - _plot.Left) / _plot.Width, 0, 1) * _seconds
        : null;

    private static Color Tone(StaggerCheck c) => c.Outcome switch
    {
        StaggerOutcome.Broken => Green,
        StaggerOutcome.Failed => Red,
        _ => Grey,
    };

    /// <summary>How full the bar was at a moment (0 to 1), the last reading before it; null before the first.</summary>
    private double? FillAt(double t)
    {
        double? fill = null;
        foreach ((double at, double f) in _report.Stagger.Bar)
        {
            if (at > t)
            {
                break;
            }

            fill = f;
        }

        return fill;
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        _plot = new Rect(Left, Top + LabelRows, Math.Max(10, ActualWidth - Left - Right), Math.Max(10, ActualHeight - Top - LabelRows - 26));
        Rect plot = _plot;
        Brush muted = Res("Brush.TextMuted", Brushes.Gray);
        Brush text = Res("Brush.Text", Brushes.White);
        double X(double t) => plot.Left + plot.Width * Math.Clamp(t / _seconds, 0, 1);
        double Y(double fill) => plot.Bottom - plot.Height * Math.Clamp(fill, 0, 1);

        // the grid: empty, half and full
        var gridPen = new Pen(ReportPalette.Brush(Colors.White, 0x16), 1);
        foreach ((double f, string label) in new[] { (0.0, "0%"), (0.5, "50%"), (1.0, "full") })
        {
            dc.DrawLine(gridPen, new Point(plot.Left, Y(f)), new Point(plot.Right, Y(f)));
            FormattedText l = Text(label, 10.5, muted);
            dc.DrawText(l, new Point(plot.Left - 8 - l.Width, Y(f) - l.Height / 2));
        }

        TimeAxis(dc, plot, _seconds, plot.Bottom + 6);

        // the checks' bands behind the bar, the picked one brighter
        var checks = _report.StaggerChecks;
        foreach (StaggerCheck c in checks)
        {
            (double from, double to) = Span(c);
            bool picked = Selection?.Check is { } sc && sc.Start == c.Start;
            dc.DrawRectangle(ReportPalette.Brush(Tone(c), (byte)(picked ? 0x30 : 0x16)), null, new Rect(new Point(X(from), plot.Top), new Point(Math.Max(X(to), X(from) + 2), plot.Bottom)));
        }

        // a stretch picked by a drag (or the whole fight): shaded in gold
        if (Selection is { Check: null } free && (free.From > 0.01 || free.To < _seconds - 0.01))
        {
            var gold = Color.FromRgb(0xDC, 0xB8, 0x77);
            var r = new Rect(new Point(X(free.From), plot.Top), new Point(Math.Max(X(free.To), X(free.From) + 2), plot.Bottom));
            dc.DrawRectangle(ReportPalette.Brush(gold, 0x1C), new Pen(ReportPalette.Brush(gold, 0x90), 1), r);
        }

        // how full the bar was: steps from reading to reading
        var bar = _report.Stagger.Bar;
        if (bar.Count > 0)
        {
            var area = new StreamGeometry();
            var edge = new StreamGeometry();
            using (StreamGeometryContext g = area.Open())
            using (StreamGeometryContext e = edge.Open())
            {
                g.BeginFigure(new Point(X(bar[0].At), plot.Bottom), true, true);
                e.BeginFigure(new Point(X(bar[0].At), Y(bar[0].Fill)), false, false);
                double last = bar[0].Fill;
                foreach ((double at, double fill) in bar)
                {
                    g.LineTo(new Point(X(at), Y(last)), true, false);
                    g.LineTo(new Point(X(at), Y(fill)), true, false);
                    e.LineTo(new Point(X(at), Y(last)), true, false);
                    e.LineTo(new Point(X(at), Y(fill)), true, false);
                    last = fill;
                }

                double endAt = Math.Min(_seconds, bar[^1].At + 2);
                g.LineTo(new Point(X(endAt), Y(last)), true, false);
                g.LineTo(new Point(X(endAt), plot.Bottom), true, false);
                e.LineTo(new Point(X(endAt), Y(last)), true, false);
            }

            area.Freeze();
            edge.Freeze();
            dc.DrawGeometry(ReportPalette.Brush(Blue, 0x55), null, area);
            dc.DrawGeometry(null, new Pen(ReportPalette.Brush(Blue, 0xE0), 1.4), edge);
        }

        // each check: the cast's start (a line with its name above the plot), its end (red dashed), the break or what was left
        var namePen = new Pen(ReportPalette.Brush(Mechanic, 0xE0), 1.6);
        var endPen = new Pen(ReportPalette.Brush(Red, 0xD0), 1.4) { DashStyle = new DashStyle(new double[] { 3, 2 }, 0) };
        double[] rowEnd = { double.MinValue, double.MinValue };
        int n = 0;
        foreach (StaggerCheck c in checks)
        {
            n++;
            (double from, _) = Span(c);
            double x = X(from);
            string name = c.Mechanic != 0 ? StaggerCardView.Title(c) : "Stagger bar";
            FormattedText label = Text($"{n} ▶ {name}", 11, ReportPalette.Brush(c.Mechanic != 0 ? Mechanic : Grey), bold: true);
            int row = rowEnd[0] < x - 4 ? 0 : rowEnd[1] < x - 4 ? 1 : 0;
            double width = label.Width;
            double lx = Math.Min(x, plot.Right - width);
            double ly = Top + row * 16;
            rowEnd[row] = lx + width;
            dc.DrawText(label, new Point(lx, ly));
            dc.DrawLine(c.Mechanic != 0 ? namePen : new Pen(ReportPalette.Brush(Grey, 0xA0), 1.2), new Point(x, ly + label.Height + 1), new Point(x, plot.Bottom));
            if (c.Deadline is DateTime d)
            {
                double xe = X((d - _report.StaggerCheckStart).TotalSeconds);
                dc.DrawLine(endPen, new Point(xe, plot.Top), new Point(xe, plot.Bottom));
            }

            if (c.Outcome == StaggerOutcome.Broken && c.End is DateTime broke)
            {
                double xb = X((broke - _report.StaggerCheckStart).TotalSeconds);
                dc.DrawGeometry(ReportPalette.Brush(Green), new Pen(Res("Brush.Panel", Brushes.Black), 1.2), Diamond(xb, plot.Bottom - 1, 6));
            }
            else if (c.Outcome == StaggerOutcome.Failed && c.End is DateTime failed)
            {
                double xf = X((failed - _report.StaggerCheckStart).TotalSeconds);
                double yf = Y(c.Max > 0 ? (double)c.Left / c.Max : 0);
                FormattedText left = Text("✗ " + c.Left.ToString("N0", CultureInfo.CurrentCulture), 10.5, ReportPalette.Brush(Red), bold: true);
                dc.DrawText(left, new Point(Math.Min(xf + 3, plot.Right - left.Width), Math.Max(plot.Top, yf - left.Height - 1)));
            }
        }

        // the breaks outside a check (a bar that is always up)
        foreach (StaggerBreak b in _report.Stagger.Breaks)
        {
            if (!checks.Any(c => c.Outcome == StaggerOutcome.Broken && c.End is DateTime e && Math.Abs((e - _report.StaggerCheckStart).TotalSeconds - b.At) < 1))
            {
                dc.DrawGeometry(ReportPalette.Brush(Blue), new Pen(Res("Brush.Panel", Brushes.Black), 1), Diamond(X(b.At), plot.Bottom - 1, 5));
            }
        }

        // the moment under the mouse: the time, the bar exactly, the check
        if (_hoverX >= plot.Left && _hoverX <= plot.Right)
        {
            double t = (_hoverX - plot.Left) / plot.Width * _seconds;
            dc.DrawLine(new Pen(ReportPalette.Brush(Colors.White, 0x60), 1), new Point(_hoverX, plot.Top), new Point(_hoverX, plot.Bottom));
            StaggerCheck? at = CheckAt(t);
            var lines = new List<(string Text, Brush Brush, bool Bold)>
            {
                (ReportPalette.Clock(t) + "." + ((int)(t * 10) % 10).ToString(CultureInfo.InvariantCulture), text, true),
            };
            if (FillAt(t) is double fill)
            {
                int max = at?.Max ?? checks.LastOrDefault(c => (c.Start - _report.StaggerCheckStart).TotalSeconds <= t)?.Max ?? 0;
                lines.Add((max > 0 ? $"Bar: {Math.Round(fill * max):N0} of {max:N0} ({fill * 100:0}%)" : $"Bar: {fill * 100:0}%", ReportPalette.Brush(Blue), false));
            }

            if (at is not null)
            {
                (string status, Color color) = StaggerCardView.Status(at, at.End ?? at.Start);
                double into = t - Span(at).From;
                lines.Add(($"Check {checks.ToList().IndexOf(at) + 1}: {StaggerCardView.Title(at)}, {into:0.0} s in", ReportPalette.Brush(Mechanic), false));
                lines.Add((status, ReportPalette.Brush(color), true));
                lines.Add(("Click: who did what in it", muted, false));
            }
            else
            {
                lines.Add(("Drag to pick a stretch", muted, false));
            }

            var texts = lines.Select(l => Text(l.Text, 11, l.Brush, l.Bold)).ToList();
            double w = texts.Max(x => x.Width) + 16, h = texts.Sum(x => x.Height) + 10;
            double bx = _hoverX + 12 + w > plot.Right ? _hoverX - 12 - w : _hoverX + 12;
            var box = new Rect(bx, plot.Top + 4, w, h);
            dc.DrawRoundedRectangle(Res("Brush.Panel", ReportPalette.Brush(Color.FromRgb(0x1E, 0x1D, 0x22))), new Pen(ReportPalette.Brush(Colors.White, 0x30), 1), box, 5, 5);
            double y = box.Top + 5;
            foreach (FormattedText ft in texts)
            {
                dc.DrawText(ft, new Point(box.Left + 8, y));
                y += ft.Height;
            }
        }
    }

    private static StreamGeometry Diamond(double x, double y, double r)
    {
        var g = new StreamGeometry();
        using (StreamGeometryContext c = g.Open())
        {
            c.BeginFigure(new Point(x, y - r), true, true);
            c.LineTo(new Point(x + r, y), true, true);
            c.LineTo(new Point(x, y + r), true, true);
            c.LineTo(new Point(x - r, y), true, true);
        }

        g.Freeze();
        return g;
    }
}
