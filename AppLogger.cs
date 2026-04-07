using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace UltimateKtv
{
    /// <summary>
    /// A thread-safe, non-blocking asynchronous file logger for the application.
    /// </summary>
    public static class AppLogger
    {
        private static readonly string _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UltimateKtv_Log.txt");
        private static readonly BlockingCollection<string> _logQueue = new BlockingCollection<string>(1000);
        private static readonly Thread? _logThread;
        
        public static bool IsEnabled { get; set; } = true;

        private const string SessionMarker = "--- Log started at ";
        private const int MaxSessions = 3;

        static AppLogger()
        {
            try
            {
                // Synchronous setup: Cleanup old sessions before starting background thread
                InitializeLogFile();

                // Start background worker thread
                _logThread = new Thread(ProcessQueue)
                {
                    IsBackground = true,
                    Name = "AppLoggerWorker",
                    Priority = ThreadPriority.BelowNormal
                };
                _logThread.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FATAL: Failed to initialize logger: {ex.Message}");
            }
        }

        private static void InitializeLogFile()
        {
            // Build header for new session
            string header = $"{SessionMarker}{DateTime.Now:yyyy-MM-dd HH:mm:ss} ---{Environment.NewLine}";
            header += $"UltimateKtv Version: {System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version}{Environment.NewLine}";
            header += $"OS Version: {Environment.OSVersion}{Environment.NewLine}";
            header += $".NET Version: {Environment.Version}{Environment.NewLine}";
            header += $"Is 64-bit OS: {Environment.Is64BitOperatingSystem}{Environment.NewLine}";
            header += $"Is 64-bit Process: {Environment.Is64BitProcess}{Environment.NewLine}";
            header += $"--------------------------------------------------{Environment.NewLine}";

            string finalContent = header;

            if (File.Exists(_logFilePath))
            {
                try
                {
                    string existingContent = File.ReadAllText(_logFilePath);
                    var sessions = new System.Collections.Generic.List<string>();
                    int startIndex = 0;
                    int markerIndex;
                    
                    while ((markerIndex = existingContent.IndexOf(SessionMarker, startIndex)) >= 0)
                    {
                        int nextMarkerIndex = existingContent.IndexOf(SessionMarker, markerIndex + SessionMarker.Length);
                        if (nextMarkerIndex < 0)
                        {
                            sessions.Add(existingContent.Substring(markerIndex));
                            break;
                        }
                        else
                        {
                            sessions.Add(existingContent.Substring(markerIndex, nextMarkerIndex - markerIndex));
                            startIndex = nextMarkerIndex;
                        }
                    }

                    int sessionsToKeep = MaxSessions - 1;
                    if (sessions.Count > sessionsToKeep)
                    {
                        sessions = sessions.GetRange(sessions.Count - sessionsToKeep, sessionsToKeep);
                    }

                    if (sessions.Count > 0)
                    {
                        finalContent = string.Join("", sessions) + header;
                    }
                }
                catch { /* Ignore read errors during init */ }
            }

            File.WriteAllText(_logFilePath, finalContent);
        }

        private static void ProcessQueue()
        {
            foreach (var message in _logQueue.GetConsumingEnumerable())
            {
                try
                {
                    File.AppendAllText(_logFilePath, message, Encoding.UTF8);
                }
                catch
                {
                    // If append fails, we can't do much without getting back into a loop
                }
            }
        }

        public static void Log(string message)
        {
            if (!IsEnabled) return;
            
            string logEntry = $"[{DateTime.Now:HH:mm:ss.fff}] [INFO] {message}{Environment.NewLine}";
            Enqueue(logEntry);
        }

        public static void LogError(string message, Exception? ex = null)
        {
            if (!IsEnabled) return;
            
            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] [ERROR] {message}");
            if (ex != null)
            {
                sb.AppendLine($"Exception: {ex.GetType().Name} - {ex.Message}");
                sb.AppendLine($"Stack Trace: {ex.StackTrace}");
            }
            Enqueue(sb.ToString());
        }

        private static void Enqueue(string entry)
        {
            // Try to add, but don't block forever if the queue is full (though 1000 is plenty)
            _logQueue.TryAdd(entry);
        }
    }
}
