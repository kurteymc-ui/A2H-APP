using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using AionDPS.Schedule;

namespace AionDPS.Ui;

/// <summary>
/// The Timers page's day ring, the same as the website's: the next 24 hours as a clock face that turns with the day, the needle at the top is now. Each event
/// has its own coloured ring with an arc per occurrence (faded once it is over, thicker with a glow while it runs; the rift's arc is strong for the minutes its
/// portals are open), the hours are marked around the edge in the player's own time, and the slice just before the needle is the hour that has passed.
/// Drawn as vectors in OnRender; the page asks for a new drawing (InvalidateVisual) twice a minute and when an event starts or ends, never per second.
/// </summary>
internal sealed class TimersDial : FrameworkElement
{
    // drawn on a 440 × 440 square (the website's), scaled to the element's size
    private const double Box = 440, Mid = 220;
    private static readonly double[] Lanes = { 188, 174, 158, 142, 126, 110 };

    /// <summary>The ring of each event (the website's; the Middle Reshanta bosses share the battlefield's ring, which ends at 21:00 before they come).</summary>
    private static readonly Dictionary<string, double> Ring = new(StringComparer.Ordinal)
    {
        ["shugo"] = 188, ["invasion"] = 174, ["rift"] = 158, ["kaira"] = 142, ["arena"] = 126, ["siege"] = 110, ["exec"] = 110, ["nahma"] = 110, ["mboss"] = 126,
    };

    /// <summary>The moment drawn: the needle.</summary>
    public DateTime Now { get; set; } = DateTime.Now;

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0)
        {
            return;
        }

        DateTime now = Now;
        DateTime nowUtc = now.ToUniversalTime();
        // the angle of a moment: 0 at the top (now), a full turn for a day, clockwise
        double Angle(DateTime t) => (t.ToUniversalTime() - nowUtc).TotalMinutes / 1440.0 * 360.0;

        Color lane = ThemeColor("Brush.Control", Color.FromRgb(0x26, 0x23, 0x2C));
        Color tick = ThemeColor("Brush.Border", Color.FromRgb(0x5A, 0x52, 0x60));
        Color bigTick = ThemeColor("Brush.TextSubtle", Color.FromRgb(0x8C, 0x81, 0x91));
        Color label = ThemeColor("Brush.TextMuted", Color.FromRgb(0xA7, 0x9C, 0x90));
        Color accent = ThemeColor("Brush.Accent", Color.FromRgb(0xE8, 0xC4, 0x8F));
        Color face = ThemeColor("Brush.Window", Color.FromRgb(0x14, 0x13, 0x18));

        dc.PushTransform(new ScaleTransform(size / Box, size / Box));

        // the hour that just passed: a dark slice before the needle
        var wedge = new StreamGeometry();
        using (StreamGeometryContext ctx = wedge.Open())
        {
            ctx.BeginFigure(new Point(Mid, Mid), true, true);
            ctx.LineTo(Polar(204, -15), false, false);
            ctx.ArcTo(Polar(204, 0), new Size(204, 204), 0, false, SweepDirection.Clockwise, false, false);
        }

        wedge.Freeze();
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(0x5A, 0, 0, 0)), null, wedge);

        var lanePen = new Pen(new SolidColorBrush(lane), 9);
        foreach (double r in Lanes)
        {
            dc.DrawEllipse(null, lanePen, new Point(Mid, Mid), r, r);
        }

        // the hours, in the player's own time, a label every three hours (the day's name at midnight)
        DateTime from = now.AddHours(-1), to = now.AddHours(23);
        var tickPen = new Pen(new SolidColorBrush(tick), 1.2);
        var bigTickPen = new Pen(new SolidColorBrush(bigTick), 1.6);
        var labelBrush = new SolidColorBrush(label);
        var dayBrush = new SolidColorBrush(accent);
        bool twelveHours = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('h');
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        FontFamily family = TextElement.GetFontFamily(this);
        var typeface = new Typeface(family, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var dayface = new Typeface(family, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        DateTime firstHour = new DateTime(from.Year, from.Month, from.Day, from.Hour, 0, 0, DateTimeKind.Local).ToUniversalTime();
        for (DateTime utc = firstHour; utc <= to.ToUniversalTime(); utc = utc.AddHours(1))
        {
            DateTime local = utc.ToLocalTime();
            if (local < from)
            {
                continue;
            }

            double a = Angle(local);
            int hour = local.Hour;
            bool big = hour % 3 == 0;
            dc.DrawLine(big ? bigTickPen : tickPen, Polar(big ? 194 : 197, a), Polar(202, a));
            if (big && a > 4 && a < 341)
            {
                string text = hour == 0 ? local.ToString("ddd", CultureInfo.CurrentCulture)
                    : twelveHours ? ((hour % 12) == 0 ? 12 : hour % 12).ToString(CultureInfo.CurrentCulture) + (hour < 12 ? "a" : "p")
                    : hour.ToString("00", CultureInfo.CurrentCulture);
                var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, hour == 0 ? dayface : typeface, 15, hour == 0 ? dayBrush : labelBrush, dip);
                Point at = Polar(216, a);
                dc.DrawText(formatted, new Point(at.X - formatted.Width / 2, at.Y - formatted.Height / 2));
            }
        }

        // the events: an arc per occurrence on the event's own ring
        foreach (ScheduledEvent scheduled in EventSchedule.Events)
        {
            if (scheduled.Always)
            {
                continue;
            }

            double r = Ring.TryGetValue(scheduled.Look, out double ring) ? ring : Lanes[^1];
            Color color = TimerLook.ColorOf(scheduled.Look);
            var brush = new SolidColorBrush(color);
            foreach (EventOccurrence occurrence in EventSchedule.Between(scheduled, from, to))
            {
                double a0 = Math.Max(-15, Angle(occurrence.Start)), a1 = Math.Min(345, Angle(occurrence.End));
                if (a1 - a0 < 1.4)
                {
                    a1 = a0 + 1.4;
                }

                if (a0 >= 345)
                {
                    continue;
                }

                bool past = occurrence.End <= now, live = occurrence.Start <= now && now < occurrence.End;
                double opacity = past ? 0.28 : 1.0;
                if (live)
                {
                    dc.DrawGeometry(null, RoundPen(new SolidColorBrush(Color.FromArgb(0x38, color.R, color.G, color.B)), 22), Arc(r, a0, a1));
                }

                Pen pen = RoundPen(brush, live ? 12 : 9);
                if (scheduled.OpenMinutes > 0)
                {
                    // the whole hour faint, the minutes the portals are open strong
                    dc.PushOpacity(opacity * 0.42);
                    dc.DrawGeometry(null, pen, Arc(r, a0, a1));
                    dc.Pop();
                    double b1 = Math.Min(345, Angle(occurrence.OpenUntil));
                    if (b1 > a0)
                    {
                        dc.PushOpacity(opacity);
                        dc.DrawGeometry(null, pen, Arc(r, a0, Math.Max(b1, a0 + 1.4)));
                        dc.Pop();
                    }
                }
                else
                {
                    dc.PushOpacity(opacity);
                    dc.DrawGeometry(null, pen, Arc(r, a0, a1));
                    dc.Pop();
                }
            }
        }

        // the face in the middle (the page puts the next big event on it) and the needle
        dc.DrawEllipse(new SolidColorBrush(face), new Pen(new SolidColorBrush(tick), 1), new Point(Mid, Mid), 98, 98);
        var needle = new Pen(new SolidColorBrush(accent), 2.2);
        dc.DrawLine(needle, Polar(99, 0), Polar(204, 0));
        dc.DrawEllipse(new SolidColorBrush(accent), null, Polar(204, 0), 4.5, 4.5);
        dc.Pop();
    }

    /// <summary>The colour of a theme brush (taken as a colour, so the theme's own brush is never frozen or changed here).</summary>
    private Color ThemeColor(string key, Color fallback) => TryFindResource(key) is SolidColorBrush brush ? brush.Color : fallback;

    private static Pen RoundPen(Brush brush, double thickness) =>
        new(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

    private static Point Polar(double r, double degrees)
    {
        double a = (degrees - 90) * Math.PI / 180;
        return new Point(Mid + r * Math.Cos(a), Mid + r * Math.Sin(a));
    }

    private static StreamGeometry Arc(double r, double a0, double a1)
    {
        if (a1 - a0 > 359.5)
        {
            a1 = a0 + 359.5;
        }

        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(Polar(r, a0), false, false);
            ctx.ArcTo(Polar(r, a1), new Size(r, r), 0, a1 - a0 > 180, SweepDirection.Clockwise, true, false);
        }

        geometry.Freeze();
        return geometry;
    }
}
