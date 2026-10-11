using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace UltimateKtv
{
    /// <summary>
    /// Periodically writes process resource usage (memory, GC, CPU, handles, GDI/USER objects) to AppLogger.
    /// Snapshots can also be logged on demand at key events (e.g. song start).
    /// </summary>
    public static class ResourceMonitor
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
        private static readonly object _lock = new object();
        private static Timer? _timer;
        private static TimeSpan _lastCpuTime;
        private static DateTime _lastSampleTime;

        [DllImport("user32.dll")]
        private static extern uint GetGuiResources(IntPtr hProcess, uint uiFlags);
        private const uint GR_GDIOBJECTS = 0;
        private const uint GR_USEROBJECTS = 1;

        public static void Start()
        {
            lock (_lock)
            {
                if (_timer != null) return;
                using (var process = Process.GetCurrentProcess())
                {
                    _lastCpuTime = process.TotalProcessorTime;
                    _lastSampleTime = DateTime.UtcNow;
                }
                _timer = new Timer(_ => LogSnapshot("Periodic"), null, Interval, Interval);
            }
            LogSnapshot("Startup");
        }

        public static void Stop()
        {
            lock (_lock)
            {
                _timer?.Dispose();
                _timer = null;
            }
            LogSnapshot("Shutdown");
        }

        /// <summary>
        /// Logs one resource usage line. Safe to call from any thread.
        /// </summary>
        public static void LogSnapshot(string context)
        {
            if (!AppLogger.IsEnabled) return;
            try
            {
                using var process = Process.GetCurrentProcess();

                double cpuPercent = 0;
                lock (_lock)
                {
                    var now = DateTime.UtcNow;
                    var cpuTime = process.TotalProcessorTime;
                    double elapsedMs = (now - _lastSampleTime).TotalMilliseconds;
                    if (_lastSampleTime != default && elapsedMs > 0)
                    {
                        cpuPercent = (cpuTime - _lastCpuTime).TotalMilliseconds / (elapsedMs * Environment.ProcessorCount) * 100.0;
                    }
                    _lastCpuTime = cpuTime;
                    _lastSampleTime = now;
                }

                var gcInfo = GC.GetGCMemoryInfo();
                uint gdiObjects = GetGuiResources(process.Handle, GR_GDIOBJECTS);
                uint userObjects = GetGuiResources(process.Handle, GR_USEROBJECTS);

                AppLogger.Log(
                    $"[Resource] {context}: " +
                    $"WorkingSet={ToMB(process.WorkingSet64)}MB (Peak={ToMB(process.PeakWorkingSet64)}MB), " +
                    $"Private={ToMB(process.PrivateMemorySize64)}MB, " +
                    $"ManagedHeap={ToMB(GC.GetTotalMemory(false))}MB, " +
                    $"GCCommitted={ToMB(gcInfo.TotalCommittedBytes)}MB, " +
                    $"GC0/1/2={GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}, " +
                    $"GCPause={GC.GetTotalPauseDuration().TotalMilliseconds:F0}ms, " +
                    $"CPU={cpuPercent:F1}%, " +
                    $"Threads={process.Threads.Count}, Handles={process.HandleCount}, " +
                    $"GDI={gdiObjects}, USER={userObjects}, " +
                    $"Uptime={(DateTime.Now - process.StartTime).TotalHours:F2}h");
            }
            catch (Exception ex)
            {
                AppLogger.LogWarn($"[Resource] Failed to collect resource usage: {ex.Message}");
            }
        }

        private static long ToMB(long bytes) => bytes / (1024 * 1024);
    }
}
