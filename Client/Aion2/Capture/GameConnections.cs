using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AionDPS.Aion2.Capture;

/// <summary>
/// The TCP connections the game process (AION2.exe) has open right now, as Windows lists them (GetExtendedTcpTable): the ports
/// its traffic uses, wherever it goes. With a ping booster or a VPN (ExitLag, WTFast, a local proxy ...) the game talks to
/// another port, often on 127.0.0.1 or a virtual adapter, so the meter learns those ports from the process instead of
/// guessing. Nothing is sent or changed; only Windows' own connection list is read, a few times a minute at most.
/// </summary>
public static class GameConnections
{
    public const string ProcessName = "AION2";

    private const int AfInet = 2;
    private const int TcpTableOwnerPidConnections = 4;
    private const int ErrorInsufficientBuffer = 122;
    private const uint StateEstablished = 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int addressFamily, int tableClass, uint reserved);

    /// <summary>Is the game running at all.</summary>
    public static bool GameRunning()
    {
        Process[] found = Process.GetProcessesByName(ProcessName);
        foreach (Process p in found)
        {
            p.Dispose();
        }

        return found.Length > 0;
    }

    /// <summary>The remote ports of the game's established TCP connections (empty when the game isn't running or Windows says nothing).</summary>
    public static IReadOnlyList<int> RemotePorts()
    {
        var pids = new HashSet<uint>();
        foreach (Process p in Process.GetProcessesByName(ProcessName))
        {
            pids.Add((uint)p.Id);
            p.Dispose();
        }

        if (pids.Count == 0)
        {
            return Array.Empty<int>();
        }

        var ports = new SortedSet<int>();
        int size = 0;
        IntPtr buffer = IntPtr.Zero;
        try
        {
            uint result = GetExtendedTcpTable(IntPtr.Zero, ref size, false, AfInet, TcpTableOwnerPidConnections, 0);
            for (int attempt = 0; attempt < 3 && result == ErrorInsufficientBuffer; attempt++)
            {
                if (buffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(buffer);
                }

                size += 4096; // the table can grow between the two calls
                buffer = Marshal.AllocHGlobal(size);
                result = GetExtendedTcpTable(buffer, ref size, false, AfInet, TcpTableOwnerPidConnections, 0);
            }

            if (result != 0 || buffer == IntPtr.Zero)
            {
                return Array.Empty<int>();
            }

            int count = Marshal.ReadInt32(buffer);
            int rowSize = Marshal.SizeOf<TcpRowOwnerPid>();
            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<TcpRowOwnerPid>(buffer + 4 + i * rowSize);
                if (row.State == StateEstablished && pids.Contains(row.OwningPid))
                {
                    int port = (int)(((row.RemotePort & 0xFF) << 8) | ((row.RemotePort >> 8) & 0xFF));
                    if (port is > 0 and not 80 and not 443)
                    {
                        ports.Add(port);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or OutOfMemoryException)
        {
            return Array.Empty<int>();
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return ports.ToList();
    }
}
