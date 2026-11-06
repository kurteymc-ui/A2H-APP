using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace AionDPS.Ui;

/// <summary>
/// Tells the game's own windows (Arcana, enchanting, the inventory, the big map ...) from play by the screen, in any control mode: the game's HUD (skill bar,
/// minimap frame, the character's bars ...) stands still at the top and the bottom of the screen while one plays, and a game window covers it. The watcher
/// learns where the HUD is while the player fights (then the game shows its normal screen for sure): nine thousand fixed points in the top and the bottom
/// fifth of the game window, each the brightness step between two pixels a step apart; the points whose step is strong and steady look after look are the
/// HUD's edges. Points that are strong in fights but go away between them (a boss's bar, the target's frame) are dropped again. Later, when most of the
/// HUD's points have lost their edge for most of a second, a game window covers the HUD. The picture is the game window's own, asked of Windows with
/// PrintWindow (2 Nov 2026, the owner: no BitBlt of the screen, no DirectX): what lies over the game (the meter's overlays, other windows) is not in it, so
/// nothing has to be left out for them. It looks a few times a second, off the UI thread, at the picture held in memory only; nothing is kept but the
/// learnt numbers (a small file beside the settings, per window size, so the next start knows them at once).
/// </summary>
internal sealed class HudWatcher : IDisposable
{
    public const int Cols = 96, RowsPerBand = 12, PerCell = 4, Step = 2;
    public const int Points = 2 * Cols * RowsPerBand * PerCell;
    public const int NeedLooks = 40, NeedPoints = 60;
    private const double BandShare = 0.2;
    private const int FileVersion = 2;

    private readonly float[] _sum = new float[Points], _sumSq = new float[Points], _count = new float[Points];
    private readonly float[] _outSeen = new float[Points], _outKept = new float[Points];
    private readonly bool[] _dropped = new bool[Points];
    private bool[]? _mask;
    private float[]? _maskMean;
    private float _looks;
    private System.Drawing.Size _size;
    private Bitmap? _top, _bottom;
    private byte[] _pixels = Array.Empty<byte>();
    private int _low, _high;
    private DateTime _lastLearn = DateTime.MinValue, _lastSave = DateTime.MinValue;
    private volatile bool _menuOpen, _busy, _resetAsked;

    /// <summary>A game window covers the HUD.</summary>
    public bool MenuOpen => _menuOpen;

    /// <summary>Whether the HUD has been learnt for the game window's size (until then nothing is hidden by the screen).</summary>
    public bool Ready => _mask is not null;

    // ---- a small diagnostic log beside the settings (hud-log.txt): what the watcher decided, to find out why it does not hide on someone's screen ----

    private sealed class MinuteTally
    {
        public DateTime From;
        public int Judged, Fighting, Unlearnt, Covered, Black;
        public double Low = 1, High;
    }

    private MinuteTally _minute = new();

    private void Summary(DateTime now)
    {
        if (_minute.From == default)
        {
            _minute.From = now;
            return;
        }

        if (now - _minute.From < TimeSpan.FromMinutes(1))
        {
            return;
        }

        MinuteTally m = _minute;
        Note($"minute: judged {m.Judged} (score {(m.Judged > 0 ? m.Low.ToString("0.00") + "-" + m.High.ToString("0.00") : "-")}), fighting {m.Fighting}, "
            + $"not learnt yet {m.Unlearnt}, under own overlays {m.Covered}, black picture {m.Black}; learnt from {(int)_looks} looks, HUD {HudPoints} points, open {_menuOpen}");
        _minute = new MinuteTally { From = now };
    }

    private void Note(string text)
    {
        if (_size.Width == 0)
        {
            return; // the self-test: no file
        }

        try
        {
            string path = Path.Combine(MeterSettings.DataDirectory, "hud-log.txt");
            if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024)
            {
                File.Delete(path);
            }

            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {_size.Width}x{_size.Height} {text}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // no log then
        }
    }

    /// <summary>How many looks in fights it has learnt from (for the settings' status line).</summary>
    public int LearntLooks => (int)_looks;

    /// <summary>How many points make up the learnt HUD now (the self-test).</summary>
    internal int HudPoints => _mask?.Count(m => m) ?? 0;

    /// <summary>The game is not in front: start the counting over (the learnt HUD stays).</summary>
    public void Reset()
    {
        _resetAsked = true;
        _menuOpen = false;
    }

    /// <summary>Called on the UI thread a few times a second while the game is in front (<paramref name="area"/>: its client area on the screen, in pixels).
    /// Starts one look in the background unless one is still running, and says what the last finished look found.
    /// <paramref name="fighting"/>: the own character hit something a moment ago - the game shows its normal screen, the HUD is learnt then and nothing is
    /// hidden.</summary>
    public bool Look(IntPtr game, Rectangle area, bool fighting, DateTime now)
    {
        if (_busy || game == IntPtr.Zero)
        {
            return _menuOpen;
        }

        // the game window's own picture holds none of the meter's overlays: no point is covered by them
        var overlays = new List<Rectangle>();
        _busy = true;
        _ = Task.Run(() =>
        {
            try
            {
                LookNow(game, area, fighting, now, overlays);
            }
            catch (Exception ex) when (ex is ExternalException or ArgumentException or InvalidOperationException or OutOfMemoryException)
            {
                _menuOpen = false;
            }
            finally
            {
                _busy = false;
            }
        });
        return _menuOpen;
    }

    private void LookNow(IntPtr game, Rectangle area, bool fighting, DateTime now, List<Rectangle> overlays)
    {
        if (_resetAsked)
        {
            _resetAsked = false;
            _low = _high = 0;
        }

        if (area.Width < 400 || area.Height < 300)
        {
            _menuOpen = false;
            return;
        }

        if (area.Size != _size)
        {
            Save();
            Start(area.Size);
        }

        float[]? edges = Capture(game, area);
        bool[] covered = Covered(area, _top?.Height ?? 0, overlays);
        Judge(edges, covered, fighting, now);
        if (fighting && now - _lastSave > TimeSpan.FromMinutes(2))
        {
            _lastSave = now;
            Save();
        }
    }

    /// <summary>
    /// One look's judgement (also what the self-test drives): <paramref name="edges"/> null is a black or failed picture (an exclusive full screen) and
    /// says nothing.
    /// </summary>
    public bool Judge(float[]? edges, bool[] covered, bool fighting, DateTime now)
    {
        if (edges is null)
        {
            _low = _high = 0;
            _minute.Black++;
            Summary(now);
            return _menuOpen = false;
        }

        if (fighting)
        {
            if (now - _lastLearn >= TimeSpan.FromSeconds(1))
            {
                _lastLearn = now;
                Learn(edges, covered);
            }

            _low = 0;
            _minute.Fighting++;
            Summary(now);
            return _menuOpen = false;
        }

        if (_mask is null || _maskMean is null)
        {
            _minute.Unlearnt++;
            Summary(now);
            return _menuOpen = false;
        }

        int counted = 0, kept = 0;
        for (int i = 0; i < Points; i++)
        {
            if (!_mask[i] || covered[i])
            {
                continue;
            }

            counted++;
            if (edges[i] >= 0.5f * _maskMean[i])
            {
                kept++;
            }
        }

        if (counted < NeedPoints / 2)
        {
            _low = _high = 0;
            _minute.Covered++;
            Summary(now);
            return _menuOpen = false; // most of the HUD lies under the meter's own overlays: no judgement
        }

        double score = (double)kept / counted;
        _minute.Judged++;
        _minute.Low = Math.Min(_minute.Low, score);
        _minute.High = Math.Max(_minute.High, score);
        if (score < 0.35)
        {
            _low++;
            _high = 0;
        }
        else if (score > 0.55)
        {
            _high++;
            _low = 0;
        }
        else
        {
            _low = _high = 0;
        }

        if (!_menuOpen && _low >= 3)
        {
            _menuOpen = true;
            Note($"game window: overlays hidden (score {score:0.00}, {counted} HUD points seen)");
        }
        else if (_menuOpen && _high >= 2)
        {
            _menuOpen = false;
            Note($"game window closed: overlays back (score {score:0.00})");
        }

        Summary(now);

        if (!_menuOpen && score >= 0.45)
        {
            Prune(edges, covered);
        }

        return _menuOpen;
    }

    /// <summary>Between fights, on the normal screen: a point of the HUD that has lost its edge most of the time belongs to something shown only in fights
    /// (a boss's bar, the target's frame) and is dropped, for good.</summary>
    private void Prune(float[] edges, bool[] covered)
    {
        if (_mask is null || _maskMean is null)
        {
            return;
        }

        int left = 0;
        for (int i = 0; i < Points; i++)
        {
            if (!_mask[i])
            {
                continue;
            }

            if (!covered[i])
            {
                _outSeen[i]++;
                if (edges[i] >= 0.5f * _maskMean[i])
                {
                    _outKept[i]++;
                }

                if (_outSeen[i] >= 200)
                {
                    _outSeen[i] *= 0.5f;
                    _outKept[i] *= 0.5f;
                }

                if (_outSeen[i] >= 30 && _outKept[i] < 0.5f * _outSeen[i])
                {
                    _dropped[i] = true;
                    _mask[i] = false;
                    continue;
                }
            }

            left++;
        }

        if (left < NeedPoints)
        {
            _mask = null;
            _maskMean = null;
        }
    }

    private void Start(System.Drawing.Size size)
    {
        _size = size;
        _top?.Dispose();
        _bottom?.Dispose();
        int band = Math.Max(20, (int)(size.Height * BandShare));
        _top = new Bitmap(size.Width, band, PixelFormat.Format32bppArgb);
        _bottom = new Bitmap(size.Width, band, PixelFormat.Format32bppArgb);
        Clear();
        _low = _high = 0;
        _menuOpen = false;
        Load();
    }

    private void Clear()
    {
        Array.Clear(_sum);
        Array.Clear(_sumSq);
        Array.Clear(_count);
        Array.Clear(_outSeen);
        Array.Clear(_outKept);
        Array.Clear(_dropped);
        _looks = 0;
        _mask = null;
        _maskMean = null;
    }

    /// <summary>
    /// The brightness step at every point of the top and the bottom band; null for a black or failed picture. The picture is the game window's client
    /// area, drawn by Windows into a bitmap of ours (PrintWindow, with the flag that takes a DirectX window's real content): the screen is not read.
    /// </summary>
    private float[]? Capture(IntPtr game, Rectangle area)
    {
        if (_top is null || _bottom is null)
        {
            return null;
        }

        try
        {
            // the picture the minimap reader took a moment ago does as well (GameCapture: one picture for both, 6 November 2026)
            Bitmap top = _top, bottom = _bottom;
            bool drawn = GameCapture.Use(game, area.Size, TimeSpan.FromMilliseconds(400), whole =>
            {
                // the two bands out of the whole picture
                using (var g = Graphics.FromImage(top))
                {
                    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    g.DrawImage(whole, new Rectangle(0, 0, top.Width, top.Height), new Rectangle(0, 0, top.Width, top.Height), GraphicsUnit.Pixel);
                }

                using (var g = Graphics.FromImage(bottom))
                {
                    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    g.DrawImage(whole, new Rectangle(0, 0, bottom.Width, bottom.Height), new Rectangle(0, whole.Height - bottom.Height, bottom.Width, bottom.Height), GraphicsUnit.Pixel);
                }
            });
            if (!drawn)
            {
                return null;
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException or ExternalException)
        {
            return null;
        }

        var edges = new float[Points];
        float bright = Read(_top, edges, 0) + Read(_bottom, edges, Points / 2);
        return bright < 2 ? null : edges;
    }

    /// <summary>Fills one band's points (from <paramref name="offset"/>) and returns its mean brightness.</summary>
    private float Read(Bitmap band, float[] edges, int offset)
    {
        int w = band.Width, h = band.Height;
        BitmapData data = band.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int stride = Math.Abs(data.Stride);
        try
        {
            int bytes = stride * h;
            if (_pixels.Length < bytes)
            {
                _pixels = new byte[bytes];
            }

            Marshal.Copy(data.Scan0, _pixels, 0, bytes);
        }
        finally
        {
            band.UnlockBits(data);
        }

        byte[] px = _pixels;
        float Lum(int x, int y)
        {
            int p = y * stride + x * 4;
            return 0.114f * px[p] + 0.587f * px[p + 1] + 0.299f * px[p + 2];
        }

        double total = 0;
        int i = offset;
        for (int r = 0; r < RowsPerBand; r++)
        {
            for (int c = 0; c < Cols; c++)
            {
                for (int k = 0; k < PerCell; k++)
                {
                    (int x, int y) = PointAt(r, c, k, w, h);
                    float l = Lum(x, y);
                    edges[i++] = Math.Abs(l - Lum(x + Step, y)) + Math.Abs(l - Lum(x, y + Step));
                    total += l;
                }
            }
        }

        return (float)(total / (RowsPerBand * Cols * PerCell));
    }

    /// <summary>A point's place in its band: fixed places inside the cell, spread over it, the same every look.</summary>
    private static (int X, int Y) PointAt(int r, int c, int k, int w, int h)
    {
        int x = Math.Clamp((int)((c + (0.2 + 0.6 * ((k * 37 + r * 11 + c * 7) % 10) / 10.0)) * w / Cols), 0, w - 1 - Step);
        int y = Math.Clamp((int)((r + (0.2 + 0.6 * ((k * 53 + c * 13 + r * 3) % 10) / 10.0)) * h / RowsPerBand), 0, h - 1 - Step);
        return (x, y);
    }

    private void Learn(float[] edges, bool[] covered)
    {
        for (int i = 0; i < Points; i++)
        {
            if (covered[i])
            {
                continue;
            }

            _sum[i] += edges[i];
            _sumSq[i] += edges[i] * edges[i];
            _count[i]++;
        }

        _looks++;
        if (_looks >= 400)
        {
            // old looks count half: a HUD moved or rescaled in the game's options is learnt anew within some minutes of fighting
            for (int i = 0; i < Points; i++)
            {
                _sum[i] *= 0.5f;
                _sumSq[i] *= 0.5f;
                _count[i] *= 0.5f;
            }

            _looks *= 0.5f;
        }

        if (_looks >= NeedLooks)
        {
            BuildMask();
        }
    }

    /// <summary>The HUD's points: a strong step (an edge) that stays the same look after look.</summary>
    private void BuildMask()
    {
        var mask = new bool[Points];
        var mean = new float[Points];
        int n = 0;
        for (int i = 0; i < Points; i++)
        {
            if (_dropped[i] || _count[i] < _looks * 0.6f)
            {
                continue;
            }

            float m = _sum[i] / _count[i];
            float sd = MathF.Sqrt(Math.Max(0, _sumSq[i] / _count[i] - m * m));
            if (m >= 25 && sd <= 0.35f * m)
            {
                mask[i] = true;
                mean[i] = m;
                n++;
            }
        }

        if (n >= NeedPoints)
        {
            if (_mask is null)
            {
                Note($"HUD learnt: {n} points from {(int)_looks} looks in fights");
            }

            _mask = mask;
            _maskMean = mean;
        }
        else if (_looks >= NeedLooks && (int)_looks % 20 == 0)
        {
            Note($"no steady HUD found yet: {n} points from {(int)_looks} looks in fights (needs {NeedPoints})");
        }
    }

    /// <summary>For each point: whether one of the meter's own overlays stands on it.</summary>
    private static bool[] Covered(Rectangle area, int band, List<Rectangle> overlays)
    {
        var covered = new bool[Points];
        if (overlays.Count == 0 || band == 0)
        {
            return covered;
        }

        int i = 0;
        for (int b = 0; b < 2; b++)
        {
            int y0 = b == 0 ? area.Top : area.Bottom - band;
            for (int r = 0; r < RowsPerBand; r++)
            {
                for (int c = 0; c < Cols; c++)
                {
                    for (int k = 0; k < PerCell; k++)
                    {
                        (int px, int py) = PointAt(r, c, k, area.Width, band);
                        int x = area.Left + px, y = y0 + py;
                        foreach (Rectangle rect in overlays)
                        {
                            // a little room around the overlay: its shadow and the step to the next pixel
                            if (x >= rect.Left - Step - 2 && x < rect.Right + 2 && y >= rect.Top - Step - 2 && y < rect.Bottom + 2)
                            {
                                covered[i] = true;
                                break;
                            }
                        }

                        i++;
                    }
                }
            }
        }

        return covered;
    }

    private string FilePath => Path.Combine(MeterSettings.DataDirectory, $"hud-{_size.Width}x{_size.Height}.bin");

    private void Save()
    {
        if (_size.Width == 0 || _looks == 0)
        {
            return;
        }

        try
        {
            using var w = new BinaryWriter(File.Create(FilePath));
            w.Write(FileVersion);
            w.Write(Points);
            w.Write(_looks);
            for (int i = 0; i < Points; i++)
            {
                w.Write(_sum[i]);
                w.Write(_sumSq[i]);
                w.Write(_count[i]);
                w.Write(_outSeen[i]);
                w.Write(_outKept[i]);
                w.Write(_dropped[i]);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // learnt again next time
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return;
            }

            using var r = new BinaryReader(File.OpenRead(FilePath));
            if (r.ReadInt32() != FileVersion || r.ReadInt32() != Points)
            {
                return;
            }

            _looks = r.ReadSingle();
            for (int i = 0; i < Points; i++)
            {
                _sum[i] = r.ReadSingle();
                _sumSq[i] = r.ReadSingle();
                _count[i] = r.ReadSingle();
                _outSeen[i] = r.ReadSingle();
                _outKept[i] = r.ReadSingle();
                _dropped[i] = r.ReadBoolean();
            }

            if (_looks >= NeedLooks)
            {
                BuildMask();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Clear();
        }
    }

    public void Dispose()
    {
        for (int i = 0; i < 40 && _busy; i++)
        {
            Thread.Sleep(10);
        }

        Save();
        _top?.Dispose();
        _bottom?.Dispose();
    }
}
