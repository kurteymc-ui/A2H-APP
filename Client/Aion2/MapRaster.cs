using System.IO;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Aion2;

/// <summary>
/// A world map's tiles (assets/aion2/maps/&lt;key&gt;/&lt;col&gt;_&lt;row&gt;.jpg) as grey pixels, for the minimap reader (<see cref="MinimapMatch"/>): any
/// rectangle of the whole map in its native pixels, put together from the tiles it crosses. Tiles are read when first needed and the last few kept
/// (a Verteron tile is 2048 x 2048, 4 MB as grey bytes); a tile the map has none of (sea, the edge) is black.
/// </summary>
public sealed class MapRaster
{
    private const int Keep = 6;
    private readonly Aion2MapInfo _map;
    private readonly string _folder;
    private readonly object _gate = new();
    private readonly Dictionary<(int Col, int Row), byte[]?> _tiles = new();
    private readonly LinkedList<(int Col, int Row)> _used = new();

    public MapRaster(Aion2MapInfo map, string? folder = null)
    {
        _map = map;
        _folder = folder ?? Aion2Maps.TileFolder(map);
    }

    public Aion2MapInfo Map => _map;

    /// <summary>The map pixel of a world place (100 world units a metre).</summary>
    public (double X, double Y) ToPixel(double worldX, double worldY) => Aion2Maps.ToNative(_map, worldX, worldY);

    /// <summary>The world place of a map pixel.</summary>
    public (double X, double Y) ToWorld(double pixelX, double pixelY) => ((pixelX - _map.OffsetX) / _map.Scale, (pixelY - _map.OffsetY) / _map.Scale);

    /// <summary>Map pixels per metre (about 2 on Verteron, 1 on Altgard and the Abyss).</summary>
    public double PixelsPerMetre => _map.Scale * 100;

    /// <summary>The rectangle of the whole map from <paramref name="x0"/>, <paramref name="y0"/>, <paramref name="w"/> by <paramref name="h"/> pixels.</summary>
    public GrayImage Window(int x0, int y0, int w, int h)
    {
        var image = new GrayImage(w, h);
        int t = _map.Tile;
        for (int col = FloorDiv(x0, t); col <= FloorDiv(x0 + w - 1, t); col++)
        {
            for (int row = FloorDiv(y0, t); row <= FloorDiv(y0 + h - 1, t); row++)
            {
                if (TileOf(col, row) is not { } tile)
                {
                    continue;
                }

                int ax0 = Math.Max(x0, col * t), ay0 = Math.Max(y0, row * t);
                int ax1 = Math.Min(x0 + w, (col + 1) * t), ay1 = Math.Min(y0 + h, (row + 1) * t);
                for (int y = ay0; y < ay1; y++)
                {
                    int src = ((y - (row * t)) * t) + (ax0 - (col * t)), dst = ((y - y0) * w) + (ax0 - x0);
                    for (int x = ax0; x < ax1; x++)
                    {
                        image.Pixels[dst++] = tile[src++];
                    }
                }
            }
        }

        return image;
    }

    private static int FloorDiv(int a, int b) => a >= 0 ? a / b : ((a + 1) / b) - 1;

    private byte[]? TileOf(int col, int row)
    {
        if (col < 0 || row < 0 || col >= _map.Grid || row >= _map.Grid)
        {
            return null;
        }

        lock (_gate)
        {
            if (_tiles.TryGetValue((col, row), out byte[]? kept))
            {
                _used.Remove((col, row));
                _used.AddFirst((col, row));
                return kept;
            }
        }

        byte[]? read = Read(Path.Combine(_folder, $"{col}_{row}.jpg"), _map.Tile);
        lock (_gate)
        {
            _tiles[(col, row)] = read;
            _used.AddFirst((col, row));
            while (_used.Count > Keep)
            {
                _tiles.Remove(_used.Last!.Value);
                _used.RemoveLast();
            }
        }

        return read;
    }

    // a tile's grey bytes (the eye's weights of red, green and blue); null when it is missing or unreadable
    private static byte[]? Read(string path, int size)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var bitmap = new System.Drawing.Bitmap(path);
            if (bitmap.Width != size || bitmap.Height != size)
            {
                return null;
            }

            var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, size, size), System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            try
            {
                var grey = new byte[size * size];
                var line = new byte[data.Stride];
                for (int y = 0; y < size; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + (y * data.Stride), line, 0, data.Stride);
                    for (int x = 0; x < size; x++)
                    {
                        int b = line[x * 3], g = line[(x * 3) + 1], r = line[(x * 3) + 2];
                        grey[(y * size) + x] = (byte)(((r * 77) + (g * 150) + (b * 29)) >> 8);
                    }
                }

                return grey;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or OutOfMemoryException or System.Runtime.InteropServices.ExternalException)
        {
            return null;
        }
    }
}
