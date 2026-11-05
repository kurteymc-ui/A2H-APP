using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace AionDPS.Ui;

/// <summary>
/// A picture of the player's own game for the look setup's preview (5 November 2026, the owner: "add a picture of their game once the app opens, not a random
/// picture"): the game window's own picture, as the minimap reader takes it (PrintWindow, the window's client area: never the screen, so none of the meter's
/// overlays and nothing else over the game is in it), made small and kept in memory only while the setup is open; never saved, never sent. Without the game
/// running (or minimized, or drawn black): the picture that comes with the app (assets/setup/game.jpg), else none.
/// </summary>
internal static class GameShot
{
    /// <summary>The player's game now, about <paramref name="width"/> pixels wide; null when the game is not running, minimized, or the picture comes back black.</summary>
    public static BitmapSource? Take(int width = 1600)
    {
        IntPtr game = Aion2.GameWindow.Find();
        if (game == IntPtr.Zero || IsIconic(game) || GameWindow.ClientSizeInPixels(game) is not { Width: >= 640, Height: >= 360 } size)
        {
            return null;
        }

        try
        {
            using var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppRgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                IntPtr dc = g.GetHdc();
                try
                {
                    if (!GameWindow.InRealPixels(() => PrintWindow(game, dc, PwClientOnly | PwRenderFullContent)))
                    {
                        return null;
                    }
                }
                finally
                {
                    g.ReleaseHdc(dc);
                }
            }

            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, size.Width, size.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            BitmapSource source;
            try
            {
                source = BitmapSource.Create(size.Width, size.Height, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null, data.Scan0, data.Stride * size.Height, data.Stride);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            if (Dark(source))
            {
                return null; // drawn black (a loading screen, a window the system would not draw)
            }

            double scale = Math.Min(1, width / (double)size.Width);
            BitmapSource small = scale < 1 ? new TransformedBitmap(source, new System.Windows.Media.ScaleTransform(scale, scale)) : source;
            var frozen = new WriteableBitmap(small);
            frozen.Freeze();
            return frozen;
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException or InvalidOperationException or OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>The picture that comes with the app (a scene of the game), null when it is not there.</summary>
    public static BitmapSource? Bundled()
    {
        string path = System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "setup", "game.jpg");
        if (!System.IO.File.Exists(path))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is System.IO.IOException or NotSupportedException or UriFormatException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Nearly all black: a few hundred pixels read across the picture.</summary>
    private static bool Dark(BitmapSource source)
    {
        int w = source.PixelWidth, h = source.PixelHeight;
        var pixel = new byte[4];
        long sum = 0;
        int n = 0;
        for (int y = h / 10; y < h; y += Math.Max(1, h / 20))
        {
            for (int x = w / 10; x < w; x += Math.Max(1, w / 20))
            {
                source.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), pixel, 4, 0);
                sum += pixel[0] + pixel[1] + pixel[2];
                n++;
            }
        }

        return n == 0 || sum / (3.0 * n) < 10;
    }

    private const uint PwClientOnly = 1, PwRenderFullContent = 2;

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);
}
