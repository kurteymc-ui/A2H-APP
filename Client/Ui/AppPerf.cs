using System.Diagnostics;

namespace AionDPS.Ui;

/// <summary>
/// What the app itself costs, for the Status page (6 November 2026, "find and fix the lag"): how long the meter's work takes at each tick, whether the app's
/// window thread stalls (a timer that should come every 100 ms coming late means the app hung that long), its CPU share and memory. Kept for the last minute.
/// </summary>
internal static class AppPerf
{
    private static readonly object Gate = new();
    private static readonly Queue<(DateTime At, double Ms)> Ticks = new();
    private static readonly Queue<(DateTime At, double Ms)> Stalls = new();
    private static System.Windows.Threading.DispatcherTimer? _watch;
    private static long _lastBeat;
    private static (TimeSpan Cpu, DateTime At)? _cpuWas;
    private static double _cpu;
    public static DateTime Started { get; } = DateTime.Now;

    /// <summary>A stall of the window thread longer than this is counted (the timer itself may be 15 ms late any time).</summary>
    private const double StallMs = 150;

    /// <summary>The meter's work at one tick (MainWindow's poll), in milliseconds.</summary>
    public static void NoteTick(double ms) => Note(Ticks, ms);

    private static void Note(Queue<(DateTime At, double Ms)> queue, double ms)
    {
        lock (Gate)
        {
            DateTime now = DateTime.UtcNow;
            queue.Enqueue((now, ms));
            while (queue.Count > 0 && now - queue.Peek().At > TimeSpan.FromSeconds(60))
            {
                queue.Dequeue();
            }
        }
    }

    /// <summary>Starts the stall watch on the window thread (once).</summary>
    public static void StartWatch()
    {
        if (_watch is not null)
        {
            return;
        }

        _lastBeat = Stopwatch.GetTimestamp();
        _watch = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(100) };
        _watch.Tick += (_, _) =>
        {
            long now = Stopwatch.GetTimestamp();
            double late = Stopwatch.GetElapsedTime(_lastBeat, now).TotalMilliseconds - 100;
            _lastBeat = now;
            if (late > StallMs)
            {
                Note(Stalls, late);
            }
        };
        _watch.Start();
    }

    /// <summary>The meter's work per tick in the last minute: how many ticks, the average and the longest (ms).</summary>
    public static (int Count, double AverageMs, double MaxMs) TickStats()
    {
        lock (Gate)
        {
            return (Ticks.Count, Ticks.Count == 0 ? 0 : Ticks.Average(t => t.Ms), Ticks.Count == 0 ? 0 : Ticks.Max(t => t.Ms));
        }
    }

    /// <summary>The window thread's stalls in the last minute: how many and the longest (ms).</summary>
    public static (int Count, double LongestMs) StallStats()
    {
        lock (Gate)
        {
            return (Stalls.Count, Stalls.Count == 0 ? 0 : Stalls.Max(t => t.Ms));
        }
    }

    /// <summary>The app's share of the whole PC's CPU since the last ask (at least a second apart; percent of all cores), and its memory in MB.</summary>
    public static (double CpuPercent, long MemoryMb) ProcessUse()
    {
        using var me = System.Diagnostics.Process.GetCurrentProcess();
        DateTime now = DateTime.UtcNow;
        TimeSpan cpu = me.TotalProcessorTime;
        if (_cpuWas is { } was && (now - was.At).TotalSeconds >= 1)
        {
            _cpu = (cpu - was.Cpu).TotalMilliseconds / (now - was.At).TotalMilliseconds / Environment.ProcessorCount * 100;
            _cpuWas = (cpu, now);
        }
        else if (_cpuWas is null)
        {
            _cpuWas = (cpu, now);
        }

        return (_cpu, me.WorkingSet64 / (1024 * 1024));
    }
}
