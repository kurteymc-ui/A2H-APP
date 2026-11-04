using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace AionDPS.Aion2;

/// <summary>
/// The character logged in, from the title of the game's own window ("AION2 | Name"). Only Windows' list of windows and their
/// titles is read (EnumWindows, GetWindowText), as any task bar does; nothing of the game is touched. It names the player the
/// moment the meter starts, mid-session too, so nobody has to log in again or change channel for the meter to know who they are.
/// The game's own character record (login, channel change, new area) still decides when it comes.
/// </summary>
public static class GameWindow
{
    private delegate bool EnumWindowsProc(IntPtr window, IntPtr state);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr state);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int max);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    /// <summary>The character name in the game window's title, or null (game not running, or no name in the title).</summary>
    public static string? CharacterName()
    {
        var pids = new HashSet<uint>();
        foreach (Process p in Process.GetProcessesByName(Capture.GameConnections.ProcessName))
        {
            pids.Add((uint)p.Id);
            p.Dispose();
        }

        if (pids.Count == 0)
        {
            return null;
        }

        string? found = null;
        try
        {
            EnumWindows((window, _) =>
            {
                if (!IsWindowVisible(window) || GetWindowThreadProcessId(window, out uint pid) == 0 || !pids.Contains(pid))
                {
                    return true;
                }

                int length = GetWindowTextLength(window);
                if (length is <= 0 or > 200)
                {
                    return true;
                }

                var text = new StringBuilder(length + 1);
                GetWindowText(window, text, text.Capacity);
                found = NameFromTitle(text.ToString());
                return found is null; // stop at the first title with a name
            }, IntPtr.Zero);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }

        return found;
    }

    /// <summary>"AION2 | Name" → "Name"; anything else (no bar, an empty or implausible name) → null.</summary>
    public static string? NameFromTitle(string title)
    {
        int bar = title.LastIndexOf('|');
        if (bar < 0 || !title.TrimStart().StartsWith("AION", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string name = title[(bar + 1)..].Trim();
        return name.Length is >= 2 and <= 24 && name.All(c => char.IsLetterOrDigit(c)) ? name : null;
    }
}
