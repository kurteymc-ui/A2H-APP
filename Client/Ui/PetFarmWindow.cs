using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>
/// The pet farming overlay: a small panel that shows the level of the pet the targeted monster drops. Locked (the default) it is click-through
/// and completely invisible (hidden) when nothing is targeted or the monster has no pet. Unlocked (Settings: "Overlay locked" off) it stays
/// visible with an outline, takes clicks and can be dragged and scaled with the grip in its corner; place and size are remembered.
/// The target comes from the game's network messages (the monster marked or hit), the level from the pet list of the login frame; nothing is read from the screen.
/// </summary>
public sealed class PetFarmWindow : Window, IOverlayPlace
{
    private const double MinScale = 0.6, MaxScale = 3.0;

    private readonly StackPanel _lines = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly Border _panel;
    private IReadOnlyList<(string Pet, string Level)> _current = Array.Empty<(string, string)>();
    private bool _locked = true;
    private double _centerX;
    private bool _resizing;
    private readonly OverlayEdgeResize _edges;

    public PetFarmWindow()
    {
        Title = "Aion DPS Pet Farming";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AltTabHidden.Apply(this);
        ShowActivated = false; // the game keeps the keyboard
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;

        var settings = MeterSettings.Load();
        ApplyOpacity(settings.OverlayOpacity);
        SetScale(settings.PetFarmScale);

        var stack = new DockPanel { LastChildFill = true };
        stack.Children.Add(new Border { Child = _lines, ClipToBounds = true });
        _panel = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 5, 8, 5), Child = stack, LayoutTransform = _scale, BorderThickness = new Thickness(1) };
        _panel.SetResourceReference(Border.BackgroundProperty, "Brush.OverlayBg");
        _panel.MouseLeftButtonDown += (_, e) =>
        {
            if (!_locked && e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
                var s = MeterSettings.Load();
                _centerX = Left + ActualWidth / 2;
                s.PetFarmLeft = _centerX; // the saved value is the window's middle: the text grows to both sides
                s.PetFarmTop = Top;
                s.Save();
            }
        };
        if (settings.PetFarmWidth is > 0) { _panel.Width = settings.PetFarmWidth.Value; }
        if (settings.PetFarmHeight is > 0) { _panel.Height = settings.PetFarmHeight.Value; }
        _edges = new OverlayEdgeResize(this, _panel, () => _scale.ScaleX, () => _resizing = true, () =>
        {
            _resizing = false;
            _centerX = Left + ActualWidth / 2;
            var s = MeterSettings.Load();
            s.PetFarmScale = _scale.ScaleX;
            s.PetFarmWidth = double.IsNaN(_panel.Width) ? null : _panel.Width;
            s.PetFarmHeight = double.IsNaN(_panel.Height) ? null : _panel.Height;
            s.PetFarmLeft = _centerX;
            s.PetFarmTop = Top;
            s.Save();
        }, SetScale, OverlayEdgeMode.Uniform);
        Content = _edges.Wrap(_panel);
        _centerX = settings.PetFarmLeft ?? SystemParameters.WorkArea.Width / 2;
        Left = _centerX - 60;
        SizeChanged += (_, e) => { if (!_resizing) { Left = _centerX - e.NewSize.Width / 2; } }; // keep the middle in place when the text changes
        Top = settings.PetFarmTop ?? 140;
        ApplyLocked(settings.PetInfoLocked);
        Action<string> onReset = name => { if (name == Title) { Dispatcher.Invoke(ReloadPlace); } };
        OverlayPlaces.WasReset += onReset;
        Closed += (_, _) => OverlayPlaces.WasReset -= onReset;
        LocationChanged += (_, _) => _centerX = Left + ActualWidth / 2; // placed from outside (docked): the middle is where it is now
        OverlayDocking.Attach(this, () =>
        {
            var s = MeterSettings.Load();
            s.PetFarmLeft = _centerX;
            s.PetFarmTop = Top;
            s.Save();
        });
    }

    // ---- the layout editor (OverlayLayout.cs) ----
    public bool PlaceIsView => false;

    public Rect PlaceBounds
    {
        get
        {
            Size size = OverlayMeasure.SizeOf(this);
            return new Rect(double.IsFinite(Left) ? Left : 0, double.IsFinite(Top) ? Top : 0, size.Width, size.Height);
        }
    }

    public double PlaceScale => _scale.ScaleX;

    public void PlaceAt(double left, double top, bool save)
    {
        Left = left;
        Top = top;
        _centerX = Left + ActualWidth / 2;
        if (save)
        {
            SavePetFarmPlace();
        }
    }

    public void PlaceScaleTo(double scale, bool save)
    {
        SetScale(scale);
        if (save)
        {
            SavePetFarmPlace();
        }
    }

    private void SavePetFarmPlace()
    {
        var s = MeterSettings.Load();
        s.PetFarmScale = _scale.ScaleX;
        s.PetFarmLeft = _centerX;
        s.PetFarmTop = Top;
        s.Save();
    }

    /// <summary>Place, size and scale from the settings again (after "reset size and position").</summary>
    private void ReloadPlace()
    {
        var s = MeterSettings.Load();
        SetScale(s.PetFarmScale);
        _panel.Width = s.PetFarmWidth is > 0 ? s.PetFarmWidth.Value : double.NaN;
        _panel.Height = s.PetFarmHeight is > 0 ? s.PetFarmHeight.Value : double.NaN;
        _centerX = s.PetFarmLeft ?? SystemParameters.WorkArea.Width / 2;
        Left = _centerX - ActualWidth / 2;
        Top = s.PetFarmTop ?? 140;
    }

    private void SetScale(double scale)
    {
        scale = Math.Clamp(double.IsFinite(scale) ? scale : 1.0, MinScale, MaxScale);
        _scale.ScaleX = scale;
        _scale.ScaleY = scale;
    }

    public void ApplyOpacity(double opacity)
    {
        opacity = Math.Clamp(double.IsFinite(opacity) ? opacity : 0.6, 0.0, 1.0);
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(opacity * 255), 0, 0, 0));
        brush.Freeze();
        Resources["Brush.OverlayBg"] = brush;
    }

    /// <summary>Locked: click-through, no outline, no grip, hidden without a pet. Unlocked: clickable, outlined, with a hint when empty so it can be placed (over the game; never on the desktop).</summary>
    public void ApplyLocked(bool locked)
    {
        _locked = locked;
        _edges.Show(!locked);
        _panel.Cursor = locked ? Cursors.Arrow : Cursors.SizeAll;
        _panel.SetResourceReference(Border.BorderBrushProperty, locked ? "Brush.OverlayBg" : "Brush.Accent");
        var handle = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
        if (handle != IntPtr.Zero)
        {
            using var native = new NativeOverlay(this);
            native.SetClickThrough(locked);
        }

        Render();
    }

    /// <summary>Hides the content (the window stays) while the game is not in front: the overlay is never on the desktop.</summary>
    public void ApplyGate(bool show)
    {
        if (Content is UIElement content)
        {
            content.Visibility = show ? Visibility.Visible : Visibility.Hidden;
        }
    }

    /// <summary>Shows the given lines (pet name, level text); with none the window is hidden - unless it is unlocked, then it shows a hint so it can be moved.</summary>
    public void Show(IReadOnlyList<(string Pet, string Level)> lines)
    {
        string key = string.Join("\n", lines.Select(l => l.Pet + "|" + l.Level));
        if (key == string.Join("\n", _current.Select(l => l.Pet + "|" + l.Level)) && IsVisible == (lines.Count > 0 || !_locked))
        {
            return;
        }

        _current = lines;
        Render();
    }

    /// <summary>One line of the overlay: the pet's name and, in the accent colour, what is known about its level.</summary>
    internal static StackPanel BuildRow(string pet, string level)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1), HorizontalAlignment = HorizontalAlignment.Center };
        var name = new TextBlock { Text = pet, FontWeight = FontWeights.Bold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverlayText");
        row.Children.Add(name);
        if (level.Length > 0)
        {
            var detail = new TextBlock { Text = "· " + level, FontWeight = FontWeights.Bold, FontSize = 13, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            detail.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
            row.Children.Add(detail);
        }

        return row;
    }

    private void Render()
    {
        bool placeholder = _current.Count == 0 && !_locked;
        if (_current.Count == 0 && !placeholder)
        {
            Hide();
            return;
        }

        _lines.Children.Clear();
        var rows = placeholder ? new[] { (LocalizationManager.Instance["PetFarm.Placeholder"], "") } : _current.ToArray();
        foreach ((string pet, string level) in rows)
        {
            var row = BuildRow(pet, level);
            _lines.Children.Add(row);
        }

        if (!IsVisible)
        {
            Show();
            ApplyLockedStyle();
        }
    }

    // the click-through style is lost when WPF recreates the native window on a first Show
    private void ApplyLockedStyle()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            using var native = new NativeOverlay(this);
            native.SetClickThrough(_locked);
        }
    }

    public void RaiseToFront()
    {
        if (IsVisible)
        {
            NativeOverlay.KeepOnTop(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        }
    }
}

/// <summary>
/// Drives the pet farming overlay: about once a second, while the game window is the one in front, it takes the monster the player marked or hit from
/// the game's network messages, looks it up in the pet table and tells the window what to show. Monsters that lead to no pet go to <see cref="PetFarmLog"/>.
/// No picture of the screen is taken (2 Nov 2026, the owner: no BitBlt; the optional reading of the target plate is gone).
/// </summary>
public sealed class PetFarmController : IDisposable
{
    private readonly PetFarmWindow _window;
    private readonly Func<Aion2EntityDirectory?> _directory;
    private readonly CancellationTokenSource _stop = new();

    public PetFarmController(PetFarmWindow window, Func<Aion2EntityDirectory?> directory)
    {
        _window = window;
        _directory = directory;
        _ = Task.Run(LoopAsync);
    }

    public void Dispose() => _stop.Cancel();

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(900, _stop.Token);
                var lines = ReadOnce();
                await _window.Dispatcher.InvokeAsync(() => _window.Show(lines));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException or System.IO.IOException)
            {
                PetFarmLog.Write($"ERROR {ex.GetType().Name}: {ex.Message}");
                await Task.Delay(5000, _stop.Token).ContinueWith(_ => { });
            }
        }
    }

    private IReadOnlyList<(string Pet, string Level)> ReadOnce()
    {
        var none = Array.Empty<(string, string)>();
        if (GameWindow.ForegroundClientArea(ignoreMenu: true) is null)
        {
            return none; // the game is not the window in front
        }

        var directory = _directory();
        IReadOnlyCollection<int>? pets = null;

        // 1. The game's own message when the player marks a monster (tab or click): the entity id leads to the NPC and the pet.
        // Without a mark (aiming with the camera) the monster the player hit last stands in, for 15 seconds after the hit.
        var marked = directory?.LocalTarget is { } m && DateTime.UtcNow - m.At < TimeSpan.FromMinutes(2) ? m : (ValueTuple<int, int?, DateTime>?)null;
        var hit = directory?.LocalHitTarget is { } h && DateTime.UtcNow - h.At < TimeSpan.FromSeconds(15) ? h : (ValueTuple<int, int?, DateTime>?)null;
        var current = marked is { } a && hit is { } b ? (b.Item3 > a.Item3 ? b : a) : marked ?? hit; // the later of the two
        if (current is { Item2: int npcId })
        {
            var ofNpc = Aion2Pets.PetsOfNpc(npcId);
            if (ofNpc.Count == 0)
            {
                PetFarmLog.NoteUnknownMonster(npcId);
                return none;
            }

            pets = ofNpc.ToArray();
        }

        if (pets is null)
        {
            return none;
        }

        var states = directory?.LocalPetStates ?? Array.Empty<Aion2PetState>();
        var loc = LocalizationManager.Instance;
        string lang = loc.Language;
        var result = new List<(string, string)>();
        foreach (int petId in pets.OrderBy(p => p))
        {
            string name = Aion2Pets.PetName(petId, lang) ?? $"#{petId}";
            string level;
            if (states.Count == 0)
            {
                level = loc["PetFarm.Unknown"];
            }
            else if (states.FirstOrDefault(s => s.PetId == petId) is { } state)
            {
                level = state.Level >= Aion2Pets.TopLevel
                    ? string.Format(loc["PetFarm.Max"], state.Level)
                    : string.Format(loc["PetFarm.Level"], state.Level) + $" ({state.Progress}/{Aion2Pets.ProgressNeeded(state.Level)})";
            }
            else if (directory is { LocalPetStatesLive: false })
            {
                level = loc["PetFarm.Unknown"]; // the list is the saved one of an earlier session, the pet may be newer than it
            }
            else
            {
                level = loc["PetFarm.NotOwned"];
            }

            result.Add((name, level));
        }

        return result;
    }
}

/// <summary>Finds the game's window (process AION2) and says where its client area is on screen, but only while it is the window in front.</summary>
internal static class GameWindow
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    /// <summary>DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2.</summary>
    private static readonly IntPtr RealPixelsContext = new(-4);

    /// <summary>
    /// Runs <paramref name="work"/> with this thread seeing the screen in real pixels. The meter (WPF) takes the scaling of the display Windows started
    /// with for every monitor; with a laptop at 175 % and the game on a 2560 x 1440 monitor at 100 %, Windows gave it the game window as 4480 x 2520
    /// (3 November 2026: the owner's game window was logged at that size, and neither the minimap nor the HUD was ever found in its pictures).
    /// </summary>
    public static T InRealPixels<T>(Func<T> work)
    {
        IntPtr before = IntPtr.Zero;
        try
        {
            before = SetThreadDpiAwarenessContext(RealPixelsContext);
        }
        catch (EntryPointNotFoundException)
        {
            // Windows before 10 (1607): the scaling as it is
        }

        try
        {
            return work();
        }
        finally
        {
            if (before != IntPtr.Zero)
            {
                SetThreadDpiAwarenessContext(before);
            }
        }
    }

    /// <summary>The size of a window's client area in real pixels (see <see cref="InRealPixels"/>); null when it cannot be read.</summary>
    public static System.Drawing.Size? ClientSizeInPixels(IntPtr hwnd) => hwnd == IntPtr.Zero ? null : InRealPixels(() =>
        GetClientRect(hwnd, out Rect r) && r.Right - r.Left > 0 && r.Bottom - r.Top > 0 ? new System.Drawing.Size(r.Right - r.Left, r.Bottom - r.Top) : (System.Drawing.Size?)null);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo { public int Size, Flags; public IntPtr Cursor; public Point Position; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorInfo(ref CursorInfo info);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    private const int CursorShowing = 1;

    /// <summary>Whether Windows is showing a mouse cursor. The game hides it while playing (in the control mode that does) and shows it when a menu opens.
    /// A failing call counts as "not shown": the meter then simply stays up.</summary>
    public static bool IsCursorVisible()
    {
        var info = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        return GetCursorInfo(ref info) && (info.Flags & CursorShowing) != 0 && info.Cursor != IntPtr.Zero;
    }

    /// <summary>Whether a mouse button (left, right, middle) is held.</summary>
    public static bool MouseButtonDown() => (GetAsyncKeyState(0x01) & 0x8000) != 0 || (GetAsyncKeyState(0x02) & 0x8000) != 0 || (GetAsyncKeyState(0x04) & 0x8000) != 0;

    /// <summary>Whether any keyboard key is held (not the lock keys, not the mouse buttons). Asked only while the cursor shows, to tell a menu from the show-cursor key being held.</summary>
    public static bool AnyKeyDown()
    {
        for (int key = 1; key < 255; key++)
        {
            if (key is 0x01 or 0x02 or 0x04 or 0x05 or 0x06)
            {
                continue; // mouse buttons: clicking around in a menu must not look like the show-cursor key and bring the overlays back
            }

            if (key is 0x14 or 0x90 or 0x91)
            {
                continue; // Caps, Num and Scroll lock stay "on" without being held
            }

            if ((GetAsyncKeyState(key) & 0x8000) != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the overlays may be on the screen: only over the game - while the game is the window in front, or one of the overlays themselves (an
    /// unlocked one being moved or sized) - never over the desktop, a browser or the meter's own window, locked or not (they are placed on the Overlays
    /// page or over the game).
    /// </summary>
    /// <param name="ignoreMenu">True for the overlays that stay while a game menu is open (the pet map and list, the pet farm).</param>
    public static bool OverlayMayShow(bool ignoreMenu = false)
    {
        if (ForegroundClientArea(ignoreMenu) is not null)
        {
            return true;
        }

        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == (uint)Environment.ProcessId && System.Windows.Interop.HwndSource.FromHwnd(hwnd)?.RootVisual is IOverlayPlace and not MainWindow;
    }

    /// <summary>Whether the window in front is the game itself (not the meter's own windows).</summary>
    /// <summary>The game window when it is the one in front, else zero.</summary>
    public static IntPtr ForegroundGame()
    {
        IntPtr hwnd = GetForegroundWindow();
        return hwnd != IntPtr.Zero && IsGame(hwnd) ? hwnd : IntPtr.Zero;
    }

    public static bool ForegroundIsGame()
    {
        IntPtr hwnd = GetForegroundWindow();
        return hwnd != IntPtr.Zero && IsGame(hwnd);
    }

    private static IntPtr _known;

    /// <summary>The game's window as last seen in front (zero before that): the group alerts flash its taskbar button.</summary>
    public static IntPtr KnownGameWindow => _known;
    private static IntPtr _lastChecked;
    private static bool _lastWasGameOrSelf;

    /// <summary>Whether the window in front is the game or one of the meter's own windows (its settings, its dialogs): the DPS overlay
    /// shows only then, so it does not sit over a browser or the desktop.</summary>
    public static bool ForegroundIsGameOrSelf()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        if (hwnd == _lastChecked)
        {
            return _lastWasGameOrSelf && !(MenuWatcher.Hidden && IsGame(hwnd));
        }

        bool result = IsGame(hwnd);
        if (!result)
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            result = pid == (uint)Environment.ProcessId;
        }

        _lastChecked = hwnd;
        _lastWasGameOrSelf = result;
        return result && !(MenuWatcher.Hidden && IsGame(hwnd)); // a game menu is open: out of the way
    }

    /// <param name="ignoreMenu">True for the pet and collection views: they stay (and the shop reading works) while a game menu is open.</param>
    public static System.Drawing.Rectangle? ForegroundClientArea(bool ignoreMenu = false)
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero || !IsGame(hwnd) || (MenuWatcher.Hidden && !ignoreMenu) || !GetClientRect(hwnd, out Rect client))
        {
            return null;
        }

        var origin = new Point();
        if (!ClientToScreen(hwnd, ref origin) || client.Right <= 200 || client.Bottom <= 200)
        {
            return null;
        }

        return new System.Drawing.Rectangle(origin.X, origin.Y, client.Right, client.Bottom);
    }

    private static IntPtr _notGame;
    private static long _notGameAt;

    private static bool IsGame(IntPtr hwnd)
    {
        if (hwnd == _known)
        {
            return true;
        }

        // The overlays ask every 50 ms. While another window (the meter itself, a browser) is in front, looking up the process each time
        // is expensive and starves the UI thread, so a "not the game" answer is remembered for a few seconds.
        long now = Environment.TickCount64;
        if (hwnd == _notGame && now - _notGameAt < 5000)
        {
            return false;
        }

        try
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == (uint)Environment.ProcessId)
            {
                _notGame = hwnd;
                _notGameAt = now;
                return false;
            }

            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            if (process.ProcessName.Equals("AION2", StringComparison.OrdinalIgnoreCase))
            {
                _known = hwnd;
                return true;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }

        _notGame = hwnd;
        _notGameAt = now;
        return false;
    }
}
