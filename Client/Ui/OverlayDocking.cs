using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace AionDPS.Ui;

/// <summary>Where an overlay hangs on another one: the side of the parent it touches and how far along that side it starts.</summary>
public sealed class OverlayDock
{
    public string Parent { get; set; } = "";

    /// <summary>Left, Right, Top or Bottom: the side of the parent the overlay touches.</summary>
    public string Side { get; set; } = "Bottom";

    /// <summary>Distance of the overlay's start from the parent's start along the shared side (in screen units).</summary>
    public double Offset { get; set; }
}

/// <summary>
/// Placing overlays on a grid and docking them to each other (two switches in the settings). While an overlay is dragged its place snaps to the grid and,
/// close to another overlay, flush against its edge; letting go there docks it. A docked overlay follows its parent: when the parent is moved or changes its
/// size (also by itself, when its content grows) the docked ones keep their place at its side. Dragging a docked overlay away undocks it. The skills'
/// cooldowns (a group) always snap and dock; one of them dragged moves alone, with Shift held together with those hanging on it.
/// </summary>
public static class OverlayDocking
{
    private const double Reach = 14; // how close an edge has to come to be pulled in, in screen units
    private const int WM_MOVING = 0x0216, WM_ENTERSIZEMOVE = 0x0231, WM_EXITSIZEMOVE = 0x0232;
    private static readonly Dictionary<string, Entry> Windows = new();
    private static readonly HashSet<string> Dirty = new();
    private static readonly DispatcherTimer SaveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private static bool _applying, _dragging, _docksDirty;
    private static MeterSettings? _cache;
    private static DateTime _cacheAt = DateTime.MinValue;

    /// <summary>The settings, read from the file at most once a second (dragging asks on every mouse move).</summary>
    private static MeterSettings Current()
    {
        if (_cache is null || (DateTime.UtcNow - _cacheAt).TotalSeconds >= 1)
        {
            _cache = MeterSettings.Load();
            _cacheAt = DateTime.UtcNow;
        }

        return _cache;
    }

    private sealed record Entry(Window Window, Action Save);

    /// <summary>Overlays of a group (the skills' cooldowns) snap to and dock on each other whatever the two switches say: they are made to be laid out together.</summary>
    private static readonly Dictionary<string, string> Groups = new();

    /// <summary>Puts an overlay into a group (by its title): it snaps to and docks on the others of the group always.</summary>
    public static void JoinGroup(Window window, string group) => Groups[window.Title] = group;

    private static bool SameGroup(string a, string b) => Groups.TryGetValue(a, out string? ga) && Groups.TryGetValue(b, out string? gb) && ga == gb;

    /// <summary>Whether the two overlays may dock: the switch is on, or they are of one group.</summary>
    private static bool MayDock(MeterSettings s, string a, string b) => s.OverlayDocking || SameGroup(a, b);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    static OverlayDocking()
    {
        SaveTimer.Tick += (_, _) =>
        {
            SaveTimer.Stop();
            foreach (string title in Dirty.ToArray())
            {
                if (Windows.TryGetValue(title, out Entry? e))
                {
                    e.Save();
                }
            }

            Dirty.Clear();
            if (_docksDirty)
            {
                _docksDirty = false;
                var fresh = MeterSettings.Load(); // not the cached copy: the overlays have written their places in the meantime
                fresh.OverlayDocks = Current().OverlayDocks;
                fresh.Save();
            }
        };
    }

    /// <summary>An overlay joins in. <paramref name="save"/> writes its place to the settings (called after it was carried along by its parent).</summary>
    public static void Attach(Window window, Action save)
    {
        string title = window.Title;
        Windows[title] = new Entry(window, save);
        window.Closed += (_, _) => Windows.Remove(title);
        window.LocationChanged += (_, _) => Follow(title, false);
        window.SizeChanged += (_, _) => Follow(title, true);

        void Hook()
        {
            HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)?.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (msg == WM_ENTERSIZEMOVE)
                {
                    _dragging = true;
                    if (Groups.ContainsKey(window.Title) && (GetAsyncKeyState(VK_SHIFT) & 0x8000) == 0)
                    {
                        LetGo(window.Title);
                    }
                }
                else if (msg == WM_MOVING)
                {
                    Snap(window, lParam);
                }
                else if (msg == WM_EXITSIZEMOVE)
                {
                    _dragging = false;
                    Dropped(window);
                }

                return IntPtr.Zero;
            });
        }

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
        {
            Hook();
        }
        else
        {
            window.SourceInitialized += (_, _) => Hook();
        }
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    private const int VK_SHIFT = 0x10;

    /// <summary>
    /// A box of a group (a skill's cooldown) is dragged on its own: the boxes hanging on it stay where they are, so every box goes wherever it is put. With
    /// Shift held they come along (the whole column, dragged by its first box).
    /// </summary>
    private static void LetGo(string title)
    {
        var s = MeterSettings.Load();
        var children = s.OverlayDocks.Where(kv => kv.Value.Parent == title).Select(kv => kv.Key).ToList();
        if (children.Count == 0)
        {
            return;
        }

        foreach (string child in children)
        {
            s.OverlayDocks.Remove(child);
        }

        s.Save();
        _cache = s;
        _cacheAt = DateTime.UtcNow;
    }

    private static Rect2 Bounds(Window w) => new(w.Left, w.Top, w.ActualWidth, w.ActualHeight);

    private readonly record struct Rect2(double X, double Y, double W, double H)
    {
        public double Right => X + W;
        public double Bottom => Y + H;
    }

    // ---- while dragging: grid and edges ------------------------------------------------------------------------------------------------

    private static void Snap(Window window, IntPtr lParam)
    {
        var s = Current();
        bool grouped = Groups.ContainsKey(window.Title);
        if (!s.OverlayGridSnap && !s.OverlayDocking && !grouped)
        {
            return;
        }

        var source = PresentationSource.FromVisual(window);
        if (source?.CompositionTarget is not { } target)
        {
            return;
        }

        var toDip = target.TransformFromDevice;
        var toDevice = target.TransformToDevice;
        var rect = Marshal.PtrToStructure<Rect>(lParam);
        var tl = toDip.Transform(new Point(rect.Left, rect.Top));
        var br = toDip.Transform(new Point(rect.Right, rect.Bottom));
        double x = tl.X, y = tl.Y, w = br.X - tl.X, h = br.Y - tl.Y;
        bool snappedX = false, snappedY = false;

        if (s.OverlayDocking || grouped)
        {
            var carried = Descendants(window.Title);
            foreach (var (title, entry) in Windows)
            {
                if (entry.Window == window || !entry.Window.IsVisible || carried.Contains(title) || !MayDock(s, window.Title, title))
                {
                    continue;
                }

                var o = Bounds(entry.Window);
                bool overlapsY = y < o.Bottom && y + h > o.Y, overlapsX = x < o.Right && x + w > o.X;
                if (!snappedX && overlapsY)
                {
                    if (Math.Abs(x - o.Right) <= Reach) { x = o.Right; snappedX = true; }
                    else if (Math.Abs(x + w - o.X) <= Reach) { x = o.X - w; snappedX = true; }
                }

                if (!snappedY && overlapsX)
                {
                    if (Math.Abs(y - o.Bottom) <= Reach) { y = o.Bottom; snappedY = true; }
                    else if (Math.Abs(y + h - o.Y) <= Reach) { y = o.Y - h; snappedY = true; }
                }

                // touching on one side: the other axis may line up with the neighbour's start or end
                if (snappedY && !snappedX)
                {
                    if (Math.Abs(x - o.X) <= Reach) { x = o.X; snappedX = true; }
                    else if (Math.Abs(x + w - o.Right) <= Reach) { x = o.Right - w; snappedX = true; }
                }
                else if (snappedX && !snappedY)
                {
                    if (Math.Abs(y - o.Y) <= Reach) { y = o.Y; snappedY = true; }
                    else if (Math.Abs(y + h - o.Bottom) <= Reach) { y = o.Bottom - h; snappedY = true; }
                }
            }
        }

        if (s.OverlayGridSnap && !grouped)
        {
            double cell = Math.Clamp(s.OverlayGridSize, 5, 200);
            if (!snappedX) { x = Math.Round(x / cell) * cell; }
            if (!snappedY) { y = Math.Round(y / cell) * cell; }
        }

        var ntl = toDevice.Transform(new Point(x, y));
        var nbr = toDevice.Transform(new Point(x + w, y + h));
        rect.Left = (int)Math.Round(ntl.X);
        rect.Top = (int)Math.Round(ntl.Y);
        rect.Right = (int)Math.Round(nbr.X);
        rect.Bottom = (int)Math.Round(nbr.Y);
        Marshal.StructureToPtr(rect, lParam, false);
    }

    // ---- letting go: dock or undock ----------------------------------------------------------------------------------------------------

    private static void Dropped(Window window)
    {
        var s = MeterSettings.Load();
        string title = window.Title;
        s.OverlayDocks.Remove(title);
        if (s.OverlayDocking || Groups.ContainsKey(title))
        {
            var carried = Descendants(title);
            var me = Bounds(window);
            const double tol = 3;
            foreach (var (otherTitle, entry) in Windows)
            {
                if (entry.Window == window || !entry.Window.IsVisible || carried.Contains(otherTitle) || !MayDock(s, title, otherTitle))
                {
                    continue;
                }

                var o = Bounds(entry.Window);
                bool overlapsY = me.Y < o.Bottom - tol && me.Bottom > o.Y + tol, overlapsX = me.X < o.Right - tol && me.Right > o.X + tol;
                string? side = null;
                double offset = 0;
                if (overlapsY && Math.Abs(me.X - o.Right) <= tol) { side = "Right"; offset = me.Y - o.Y; }
                else if (overlapsY && Math.Abs(me.Right - o.X) <= tol) { side = "Left"; offset = me.Y - o.Y; }
                else if (overlapsX && Math.Abs(me.Y - o.Bottom) <= tol) { side = "Bottom"; offset = me.X - o.X; }
                else if (overlapsX && Math.Abs(me.Bottom - o.Y) <= tol) { side = "Top"; offset = me.X - o.X; }
                if (side is not null)
                {
                    s.OverlayDocks[title] = new OverlayDock { Parent = otherTitle, Side = side, Offset = offset };
                    break;
                }
            }
        }

        s.Save();
        _cache = s;
        _cacheAt = DateTime.UtcNow;
    }

    // ---- following the parent ----------------------------------------------------------------------------------------------------------

    /// <summary>The overlays that hang (directly or further down) on the one with this title: they move with it.</summary>
    private static HashSet<string> Descendants(string title)
    {
        var docks = Current().OverlayDocks;
        var found = new HashSet<string>();
        var queue = new Queue<string>();
        queue.Enqueue(title);
        while (queue.Count > 0)
        {
            string parent = queue.Dequeue();
            foreach (var (child, dock) in docks)
            {
                if (dock.Parent == parent && found.Add(child))
                {
                    queue.Enqueue(child);
                }
            }
        }

        return found;
    }

    /// <summary>An overlay moved or changed its size: those docked to it are put back at its side (and those docked to them in turn).</summary>
    private static void Follow(string title, bool sizeChanged)
    {
        if (_applying || !Windows.ContainsKey(title))
        {
            return;
        }

        var s = Current();
        if ((!s.OverlayDocking && !Groups.ContainsKey(title)) || s.OverlayDocks.Count == 0)
        {
            return;
        }

        _applying = true;
        try
        {
            if (!_dragging && s.OverlayDocks.TryGetValue(title, out OverlayDock? own) && Windows.TryGetValue(own.Parent, out Entry? parentEntry))
            {
                var mine = Windows[title].Window;
                if (sizeChanged && own.Side is "Left" or "Top")
                {
                    // it grew while it hangs at the far side of its parent (left or above): it keeps its edge at the parent
                    Place(mine, parentEntry.Window, own);
                    Dirty.Add(title);
                }
                else
                {
                    // placed by hand (a handle was pulled): it stays docked where it is, or it came loose
                    var m = Bounds(mine);
                    var p = Bounds(parentEntry.Window);
                    double gap = own.Side switch { "Right" => m.X - p.Right, "Left" => p.X - m.Right, "Top" => p.Y - m.Bottom, _ => m.Y - p.Bottom };
                    if (Math.Abs(gap) <= 3)
                    {
                        double offset = own.Side is "Left" or "Right" ? m.Y - p.Y : m.X - p.X;
                        if (Math.Abs(offset - own.Offset) > 0.4)
                        {
                            own.Offset = offset;
                            _docksDirty = true;
                        }
                    }
                    else
                    {
                        s.OverlayDocks.Remove(title);
                        _docksDirty = true;
                    }
                }
            }

            var done = new HashSet<string> { title };
            var queue = new Queue<string>();
            queue.Enqueue(title);
            while (queue.Count > 0)
            {
                string parent = queue.Dequeue();
                if (!Windows.TryGetValue(parent, out Entry? p))
                {
                    continue;
                }

                foreach (var (child, dock) in s.OverlayDocks)
                {
                    if (dock.Parent != parent || !MayDock(s, parent, child) || !done.Add(child) || !Windows.TryGetValue(child, out Entry? c))
                    {
                        continue;
                    }

                    Place(c.Window, p.Window, dock);
                    Dirty.Add(child);
                    queue.Enqueue(child);
                }
            }
        }
        finally
        {
            _applying = false;
        }

        if (Dirty.Count > 0 || _docksDirty)
        {
            SaveTimer.Stop();
            SaveTimer.Start();
        }
    }

    private static void Place(Window child, Window parent, OverlayDock dock)
    {
        var p = Bounds(parent);
        double w = child.ActualWidth, h = child.ActualHeight;
        (double x, double y) = dock.Side switch
        {
            "Right" => (p.Right, p.Y + dock.Offset),
            "Left" => (p.X - w, p.Y + dock.Offset),
            "Top" => (p.X + dock.Offset, p.Y - h),
            _ => (p.X + dock.Offset, p.Bottom),
        };
        if (Math.Abs(child.Left - x) > 0.4) { child.Left = x; }
        if (Math.Abs(child.Top - y) > 0.4) { child.Top = y; }
    }
}
