using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace AionDPS.Ui;

/// <summary>What a global hotkey does.</summary>
public enum HotkeyAction
{
    ToggleHideUi,
    TogglePause,
    CopyDamageRanking,
    ClearDamage,
    UploadBoss,
    NextMode,
    ToggleTimetable,
    TogglePetMap,
    ToggleHotkeyOverlay,
    ToggleAllOverlays,
    ToggleWorldBossOverlay,
    ToggleNetworkOverlay,
    ToggleCombatOverlay,
    TogglePetArrows,
    PetMapZoomIn,
    PetMapZoomOut,
    ToggleGatherArrows,
    ToggleAssistOverlay,
    ToggleLootOverlay,
    ToggleCraftOverlay,
    MoveOverlays,
}

/// <summary>A key plus modifiers, written "Ctrl+Alt+H" in the settings file.</summary>
public readonly record struct HotkeyBinding(bool Ctrl, bool Alt, bool Shift, bool Win, Key Key)
{
    public bool IsEmpty => Key == Key.None;

    public override string ToString()
    {
        if (IsEmpty)
        {
            return "";
        }

        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        // Page Up / Page Down have a second name in the Key enum (Prior / Next), and Key.ToString() may give either: the one people know
        parts.Add(Key switch { Key.PageUp => "PageUp", Key.PageDown => "PageDown", _ => Key.ToString() });
        return string.Join('+', parts);
    }

    /// <summary>
    /// The keys that may be a hotkey on their own, without Ctrl, Alt, Shift or Win: the function keys, the number pad and the keys above the arrows
    /// (Insert, Home, End, Page Up and Down, Pause, Scroll Lock) - none of them is typed in a chat or a text box. A letter, a digit or the space bar alone
    /// would be taken from every program, the game's chat too, so those still need a modifier.
    /// </summary>
    /// <remarks>F12 alone stays out: Windows keeps it for debuggers and refuses it as a hotkey.</remarks>
    public static bool StandsAlone(Key key) => key is >= Key.F1 and <= Key.F11 or >= Key.F13 and <= Key.F24 or >= Key.NumPad0 and <= Key.NumPad9
        or Key.Multiply or Key.Add or Key.Subtract or Key.Divide or Key.Decimal
        or Key.Insert or Key.Home or Key.End or Key.PageUp or Key.PageDown or Key.Pause or Key.Scroll;

    /// <summary>Parses "Ctrl+Alt+H" or "F8"; empty or unreadable text, or a typing key without a modifier, gives an empty binding
    /// (a global hotkey of a plain letter would swallow that letter in every program).</summary>
    public static HotkeyBinding Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return default;
        }

        bool ctrl = false, alt = false, shift = false, win = false;
        Key key = Key.None;
        foreach (string part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": ctrl = true; break;
                case "alt": alt = true; break;
                case "shift": shift = true; break;
                case "win": win = true; break;
                default:
                    if (!Enum.TryParse(part, true, out key))
                    {
                        return default;
                    }

                    break;
            }
        }

        return key == Key.None || !(ctrl || alt || shift || win || StandsAlone(key)) ? default : new HotkeyBinding(ctrl, alt, shift, win, key);
    }
}

/// <summary>Why a hotkey does nothing: another program has the combination, or another of the meter's hotkeys has it too (the first one keeps it).</summary>
public enum HotkeyProblem
{
    Taken,
    Twice,
}

/// <summary>
/// Every hotkey of the meter: what it does, its id (the hotkey overlay's rows and <see cref="MeterSettings.HotkeyOverlayHidden"/>), its name and where the
/// settings keep it. One list for the registration, the overlay, the settings page and the in-game overlay menu, so none of them can leave one out.
/// </summary>
public static class HotkeyTable
{
    public sealed record Entry(HotkeyAction Action, string Id, string NameKey, Func<MeterSettings, string> Get, Action<MeterSettings, string> Set);

    public static readonly Entry[] All =
    {
        new(HotkeyAction.ToggleHideUi, "HideUi", "Settings.Hotkey.HideUi", s => s.HotkeyHideUi, (s, v) => s.HotkeyHideUi = v),
        new(HotkeyAction.TogglePause, "Pause", "Settings.Hotkey.Pause", s => s.HotkeyPause, (s, v) => s.HotkeyPause = v),
        new(HotkeyAction.CopyDamageRanking, "CopyDamage", "Settings.Hotkey.CopyDamage", s => s.HotkeyCopyDamage, (s, v) => s.HotkeyCopyDamage = v),
        new(HotkeyAction.ClearDamage, "Clear", "Settings.Hotkey.Clear", s => s.HotkeyClear, (s, v) => s.HotkeyClear = v),
        new(HotkeyAction.UploadBoss, "UploadBoss", "Settings.Hotkey.UploadBoss", s => s.HotkeyUploadBoss, (s, v) => s.HotkeyUploadBoss = v),
        new(HotkeyAction.NextMode, "Mode", "Settings.Hotkey.Mode", s => s.HotkeyMode, (s, v) => s.HotkeyMode = v),
        new(HotkeyAction.ToggleAllOverlays, "AllOverlays", "Settings.Hotkey.AllOverlays", s => s.HotkeyAllOverlays, (s, v) => s.HotkeyAllOverlays = v),
        new(HotkeyAction.MoveOverlays, "MoveOverlays", "Settings.Hotkey.MoveOverlays", s => s.HotkeyMoveOverlays, (s, v) => s.HotkeyMoveOverlays = v),
        new(HotkeyAction.ToggleHotkeyOverlay, "HotkeyOverlay", "Settings.Hotkey.HotkeyOverlay", s => s.HotkeyHotkeyOverlay, (s, v) => s.HotkeyHotkeyOverlay = v),
        new(HotkeyAction.ToggleLootOverlay, "Loot", "Main.MenuTools.Loot", s => s.HotkeyLoot, (s, v) => s.HotkeyLoot = v),
        new(HotkeyAction.ToggleCraftOverlay, "Craft", "Main.MenuTools.Craft", s => s.HotkeyCraft, (s, v) => s.HotkeyCraft = v),
        new(HotkeyAction.TogglePetMap, "PetMap", "Settings.Hotkey.PetMap", s => s.HotkeyPetMap, (s, v) => s.HotkeyPetMap = v),
        new(HotkeyAction.TogglePetArrows, "PetArrows", "Settings.Hotkey.PetArrows", s => s.HotkeyPetArrows, (s, v) => s.HotkeyPetArrows = v),
        new(HotkeyAction.PetMapZoomIn, "PetMapZoomIn", "Settings.Hotkey.PetMapZoomIn", s => s.HotkeyPetMapZoomIn, (s, v) => s.HotkeyPetMapZoomIn = v),
        new(HotkeyAction.PetMapZoomOut, "PetMapZoomOut", "Settings.Hotkey.PetMapZoomOut", s => s.HotkeyPetMapZoomOut, (s, v) => s.HotkeyPetMapZoomOut = v),
        new(HotkeyAction.ToggleGatherArrows, "GatherArrows", "Settings.Hotkey.GatherArrows", s => s.HotkeyGatherArrows, (s, v) => s.HotkeyGatherArrows = v),
        new(HotkeyAction.ToggleCombatOverlay, "Combat", "Settings.Hotkey.Combat", s => s.HotkeyCombatOverlay, (s, v) => s.HotkeyCombatOverlay = v),
        new(HotkeyAction.ToggleAssistOverlay, "Assist", "Main.MenuTools.Assist", s => s.HotkeyAssist, (s, v) => s.HotkeyAssist = v),
        new(HotkeyAction.ToggleTimetable, "Timetable", "Settings.Hotkey.Timetable", s => s.HotkeyTimetable, (s, v) => s.HotkeyTimetable = v),
        new(HotkeyAction.ToggleWorldBossOverlay, "WorldBoss", "Settings.Hotkey.WorldBoss", s => s.HotkeyWorldBoss, (s, v) => s.HotkeyWorldBoss = v),
        new(HotkeyAction.ToggleNetworkOverlay, "Network", "Settings.Hotkey.Network", s => s.HotkeyNetwork, (s, v) => s.HotkeyNetwork = v),
    };

    public static Entry Of(HotkeyAction action) => All.First(e => e.Action == action);

    public static Entry? OfId(string id) => All.FirstOrDefault(e => e.Id == id);

    /// <summary>The bindings to register, in the table's order (of two alike, the first keeps the combination).</summary>
    public static List<KeyValuePair<HotkeyAction, HotkeyBinding>> Bindings(MeterSettings s) =>
        All.Select(e => new KeyValuePair<HotkeyAction, HotkeyBinding>(e.Action, HotkeyBinding.Parse(e.Get(s)))).ToList();
}

/// <summary>
/// System-wide hotkeys (RegisterHotKey), which replace the in-game chat commands: Aion 2's chat is
/// not readable from the network stream, so ".ui" and the others could never fire. A hotkey works
/// whatever window has the focus and sends nothing into the game. Registered on the main window's
/// handle; <see cref="Apply"/> can be called again after the settings changed.
/// </summary>
internal sealed class GlobalHotkeys : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int FirstId = 0xA110;
    private const uint ModAlt = 0x0001, ModControl = 0x0002, ModShift = 0x0004, ModWin = 0x0008, ModNoRepeat = 0x4000;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly IntPtr _handle;
    private readonly HwndSource _source;
    private readonly Dictionary<int, HotkeyAction> _registered = new();

    /// <summary>Fired on the window's thread when a registered hotkey is pressed.</summary>
    public event Action<HotkeyAction>? Pressed;

    public GlobalHotkeys(Window window)
    {
        _handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_handle)
            ?? throw new InvalidOperationException("GlobalHotkeys needs the window to have a native handle (construct in OnSourceInitialized).");
        _source.AddHook(WndProc);
    }

    /// <summary>Registers the bindings in their order, dropping the previous set first. Returns the actions that got no combination: another program
    /// already owns it (<see cref="HotkeyProblem.Taken"/>), or an earlier action of the list has the same one (<see cref="HotkeyProblem.Twice"/>).</summary>
    public IReadOnlyDictionary<HotkeyAction, HotkeyProblem> Apply(IEnumerable<KeyValuePair<HotkeyAction, HotkeyBinding>> bindings)
    {
        Clear();
        var failed = new Dictionary<HotkeyAction, HotkeyProblem>();
        var used = new HashSet<HotkeyBinding>();
        int id = FirstId;
        foreach (var (action, binding) in bindings)
        {
            if (binding.IsEmpty)
            {
                continue;
            }

            if (!used.Add(binding))
            {
                failed[action] = HotkeyProblem.Twice; // Windows would refuse it as "taken": it is, by the meter itself
                continue;
            }

            uint modifiers = ModNoRepeat | (binding.Ctrl ? ModControl : 0) | (binding.Alt ? ModAlt : 0)
                | (binding.Shift ? ModShift : 0) | (binding.Win ? ModWin : 0);
            if (RegisterHotKey(_handle, id, modifiers, (uint)KeyInterop.VirtualKeyFromKey(binding.Key)))
            {
                _registered[id++] = action;
            }
            else
            {
                failed[action] = HotkeyProblem.Taken;
            }
        }

        return failed;
    }

    /// <summary>Which of <paramref name="bindings"/> share a combination with an earlier one (the check of <see cref="Apply"/> without Windows).</summary>
    public static IReadOnlyDictionary<HotkeyAction, HotkeyProblem> Twice(IEnumerable<KeyValuePair<HotkeyAction, HotkeyBinding>> bindings)
    {
        var used = new HashSet<HotkeyBinding>();
        return bindings.Where(b => !b.Value.IsEmpty && !used.Add(b.Value)).ToDictionary(b => b.Key, _ => HotkeyProblem.Twice);
    }

    private void Clear()
    {
        foreach (int id in _registered.Keys)
        {
            UnregisterHotKey(_handle, id);
        }

        _registered.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _registered.TryGetValue(wParam.ToInt32(), out HotkeyAction action))
        {
            Pressed?.Invoke(action);
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Clear();
        _source.RemoveHook(WndProc);
    }
}
