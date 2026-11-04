using System.Runtime.InteropServices;
using AionDPS.Aion2;

namespace AionDPS.Ui;

/// <summary>
/// Group alerts: while the game is in the background (tabbed out), the group finder's news calls the player back - someone joined the group, he was
/// taken into a group, the group is going in (see Aion2GroupFinder). The game's taskbar button flashes until it is brought to the front. Someone joining
/// only flashes it, without a sound (the owner, 4 November 2026: no sound when people join the run; a Windows notification plays Windows' own sound, so
/// there is none for a join either); being taken into a group or the group going in also gets a Windows notification and, when switched on, a sound.
/// Nothing happens while the game is in front: the game shows these itself (any sound then is the game's own).
/// </summary>
public partial class MainWindow
{
    private bool _groupAlertsHooked;

    private void HookGroupAlerts()
    {
        if (_groupAlertsHooked || Headless)
        {
            return;
        }

        _groupAlertsHooked = true;
        Aion2GroupFinder.Noticed += notice => Dispatcher.BeginInvoke(new Action(() => OnGroupNotice(notice)));
    }

    private void OnGroupNotice(GroupNotice notice)
    {
        if (!_gateSettings.GroupAlert || GameWindow.ForegroundIsGame())
        {
            return;
        }

        string? own = (_source?.Entities as Aion2EntityDirectory)?.LocalCharacter?.Name;
        string text = notice.Kind switch
        {
            GroupNoticeKind.Entering => "Your group is going in.",
            _ when own is not null && string.Equals(notice.Name, own, StringComparison.OrdinalIgnoreCase) => "You were taken into a group.",
            _ => $"{notice.Name} joined your group.",
        };

        FlashGame();
        // someone joining: the flash alone, silently
        if (notice.Kind == GroupNoticeKind.Joined && !(own is not null && string.Equals(notice.Name, own, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _trayIcon?.Notify("Aion 2", text);
        if (_gateSettings.GroupAlertSound)
        {
            System.Media.SystemSounds.Asterisk.Play();
        }
    }

    /// <summary>Flashes the game's taskbar button until the game comes to the front (a plain window call, as any app's notification does).</summary>
    private static void FlashGame()
    {
        IntPtr game = GameWindow.KnownGameWindow;
        if (game == IntPtr.Zero)
        {
            return;
        }

        var info = new FlashInfo
        {
            Size = (uint)Marshal.SizeOf<FlashInfo>(),
            Window = game,
            Flags = FlashAll | FlashUntilForeground,
            Count = uint.MaxValue,
            Timeout = 0,
        };
        FlashWindowEx(ref info);
    }

    private const uint FlashAll = 0x3;
    private const uint FlashUntilForeground = 0xC;

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashInfo info);
}
