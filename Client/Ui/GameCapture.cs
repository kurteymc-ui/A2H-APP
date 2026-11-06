using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace AionDPS.Ui;

/// <summary>
/// The game window's picture, taken once for everyone who needs it (6 November 2026, the lag: the HUD watcher and the minimap reader each drew the whole
/// window with PrintWindow, up to eight times a second together at 2560x1440, and the game feels every one). A picture younger than what the asker can live
/// with is handed out again instead of a new one; one at a time, so two never overlap. What it costs is counted for the Status page.
/// </summary>
internal static class GameCapture
{
    private const uint PwClientOnly = 1, PwRenderFullContent = 2;
    private static readonly object Gate = new();
    private static Bitmap? _whole;
    private static IntPtr _game;
    private static DateTime _at = DateTime.MinValue;
    private static bool _drawn;
    private static readonly Queue<(DateTime At, double Ms)> Taken = new();
    private static long _reused;
    [ThreadStatic]
    private static byte[]? _rowBuffer;

    private static byte[] _row
    {
        get => _rowBuffer ??= new byte[16384];
        set => _rowBuffer = value;
    }

    /// <summary>
    /// Runs <paramref name="use"/> on the game window's client area (<paramref name="size"/>, in real pixels): the last picture when it is at most
    /// <paramref name="maxAge"/> old and of the same window and size, else a new one. The picture is only valid inside <paramref name="use"/> (copy what is
    /// kept). False when the window can't be drawn (minimized, an exclusive full screen).
    /// </summary>
    public static bool Use(IntPtr game, Size size, TimeSpan maxAge, Action<Bitmap> use)
    {
        if (game == IntPtr.Zero || size.Width < 64 || size.Height < 64)
        {
            return false;
        }

        lock (Gate)
        {
            DateTime now = DateTime.UtcNow;
            bool same = _whole is not null && _game == game && _whole.Width == size.Width && _whole.Height == size.Height;
            if (same && _drawn && now - _at <= maxAge)
            {
                _reused++;
                use(_whole!);
                return true;
            }

            if (!same)
            {
                _whole?.Dispose();
                _whole = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppRgb);
                _game = game;
            }

            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            _drawn = false;
            using (var g = Graphics.FromImage(_whole!))
            {
                IntPtr dc = g.GetHdc();
                try
                {
                    _drawn = GameWindow.InRealPixels(() => PrintWindow(game, dc, PwClientOnly | PwRenderFullContent));
                }
                finally
                {
                    g.ReleaseHdc(dc);
                }
            }

            _at = DateTime.UtcNow;
            double ms = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            Taken.Enqueue((_at, ms));
            while (Taken.Count > 0 && _at - Taken.Peek().At > TimeSpan.FromSeconds(60))
            {
                Taken.Dequeue();
            }

            if (!_drawn)
            {
                return false;
            }

            use(_whole!);
            return true;
        }
    }

    /// <summary>Copies a picture into <paramref name="target"/> (made or remade at its size): for a reader that works on it longer than a moment.</summary>
    public static void CopyInto(Bitmap source, ref Bitmap? target)
    {
        if (target is null || target.Width != source.Width || target.Height != source.Height)
        {
            target?.Dispose();
            target = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppRgb);
        }

        var rect = new Rectangle(0, 0, source.Width, source.Height);
        BitmapData from = source.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            BitmapData to = target.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            try
            {
                // a row at a time through one small buffer (no large array for each copy)
                int stride = Math.Min(from.Stride, to.Stride);
                if (_row.Length < stride)
                {
                    _row = new byte[stride];
                }

                for (int y = 0; y < source.Height; y++)
                {
                    Marshal.Copy(from.Scan0 + (y * from.Stride), _row, 0, stride);
                    Marshal.Copy(_row, 0, to.Scan0 + (y * to.Stride), stride);
                }
            }
            finally
            {
                target.UnlockBits(to);
            }
        }
        finally
        {
            source.UnlockBits(from);
        }
    }

    /// <summary>The pictures taken in the last <paramref name="span"/> (at most a minute): how many a second, how long one took on average and at most,
    /// and how many asks were answered with one already taken.</summary>
    public static (double PerSecond, double AverageMs, double MaxMs, long Reused) Stats(TimeSpan span)
    {
        lock (Gate)
        {
            DateTime from = DateTime.UtcNow - span;
            var recent = Taken.Where(t => t.At >= from).ToList();
            return (recent.Count / Math.Max(1, span.TotalSeconds), recent.Count == 0 ? 0 : recent.Average(t => t.Ms), recent.Count == 0 ? 0 : recent.Max(t => t.Ms), _reused);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
}
