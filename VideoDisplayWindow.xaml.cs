using System;
using System.Linq;
using System.Windows.Controls;
using System.Windows;
using System.Runtime.InteropServices;

namespace UltimateKtv
{
    public partial class VideoDisplayWindow : Window
    {
        public int TargetMonitorIndex { get; set; } = 1; // Default to second monitor
        public MainWindow? OwnerWindow { get; set; }
        
        // Win32 API for multi-monitor support
        [DllImport("user32.dll")]
        static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, EnumMonitorsDelegate lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll")]
        static extern bool GetMonitorInfo(IntPtr hmon, ref MONITORINFO lpmi);

        // DPI awareness APIs for proper scaling support
        [DllImport("shcore.dll")]
        static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

        [DllImport("user32.dll")]
        static extern uint GetDpiForSystem();

        // Store monitor handles for DPI lookup
        private static System.Collections.Generic.Dictionary<int, IntPtr> _monitorHandles = new System.Collections.Generic.Dictionary<int, IntPtr>();

        delegate bool EnumMonitorsDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public uint cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        private static System.Collections.Generic.List<MONITORINFO> _monitors = new System.Collections.Generic.List<MONITORINFO>();
        private static bool _monitorsInitialized = false; // Cache flag to avoid redundant Win32 API calls
        
        /// <summary>
        /// Refreshes the cached monitor list. Call this when display configuration may have changed.
        /// </summary>
        public static void RefreshMonitors()
        {
            _monitorsInitialized = false;
            _monitorHandles.Clear();
            GetMonitors(); // Re-enumerate monitors
        }
        
        public VideoDisplayWindow()
        {
            InitializeComponent();
            this.Loaded += VideoDisplayWindow_Loaded;
            this.Closing += VideoDisplayWindow_Closing;
        }

        private void VideoDisplayWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // MoveToTargetMonitor is now called before Show() to avoid appearing on primary display first
            System.Diagnostics.Debug.WriteLine("VideoDisplayWindow loaded - position should already be set");
        }

        private void VideoContainer_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (OwnerWindow != null && OwnerWindow.IsSingleMonitorMode)
            {
                System.Diagnostics.Debug.WriteLine("Single monitor mode: Returning to ConsoleWindow (VideoDisplayWindow stays open).");
                
                // Restore cursor visibility when returning to ConsoleWindow
                System.Windows.Input.Mouse.OverrideCursor = null;

                OwnerWindow.ShowFromVideoWindow();
            }
        }

        private void VideoDisplayWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Hide instead of close to allow reuse
            e.Cancel = true;
            this.Hide();
            ClearMediaPlayer();
        }

        public void UpdateLyrics(string text, Visibility visibility)
        {
            if (LrcTextBlock != null)
            {
                LrcTextBlock.Text = text;
                LrcTextBlock.Visibility = visibility;
                System.Diagnostics.Debug.WriteLine($"[LRC Window] Updating: Text='{text}', Vis={visibility}");
                if (!string.IsNullOrEmpty(text))
                {
                    AppLogger.Log($"[LRC Window] Displaying: {text}");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[LRC Window] LrcTextBlock is NULL!");
            }
        }

        /// <summary>
        /// Moves the window to the specified monitor and makes it fullscreen
        /// </summary>
        public void MoveToTargetMonitor()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== MoveToTargetMonitor Started ===");
                System.Diagnostics.Debug.WriteLine($"Target Monitor Index: {TargetMonitorIndex}");
                
                var monitors = GetMonitors();
                System.Diagnostics.Debug.WriteLine($"Available monitors: {monitors.Count}");
                
                // If no monitors detected, hide the window
                if (monitors.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("No monitors detected. Hiding video display.");
                    this.Hide();
                    return;
                }

                MONITORINFO targetMonitor;
                
                // Ensure we're targeting a valid monitor
                if (TargetMonitorIndex >= 0 && TargetMonitorIndex < monitors.Count)
                {
                    targetMonitor = monitors[TargetMonitorIndex];
                    System.Diagnostics.Debug.WriteLine($"Using specified monitor {TargetMonitorIndex}");
                }
                else
                {
                    // Auto-correct invalid monitor index
                    System.Diagnostics.Debug.WriteLine($"Invalid monitor index {TargetMonitorIndex}. Available monitors: 0-{monitors.Count - 1}");
                    
                    // Default to the opposite monitor from what would be typical
                    // If we have 2 monitors, use monitor 1 (secondary) as default
                    if (monitors.Count > 1)
                    {
                        TargetMonitorIndex = monitors.Count > 2 ? 1 : (monitors.Count - 1);
                        targetMonitor = monitors[TargetMonitorIndex];
                        System.Diagnostics.Debug.WriteLine($"Auto-corrected to monitor {TargetMonitorIndex}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("No secondary monitor available, hiding video display.");
                        this.Hide();
                        return;
                    }
                }

                // Set window position and size to fill the entire monitor
                var monitorRect = targetMonitor.rcMonitor;
                System.Diagnostics.Debug.WriteLine($"Target monitor bounds (raw): Left={monitorRect.Left}, Top={monitorRect.Top}, Right={monitorRect.Right}, Bottom={monitorRect.Bottom}");
                
                // Get DPI scale factor for this monitor and convert to WPF DIUs
                double dpiScale = GetDpiScaleForMonitor(TargetMonitorIndex);
                System.Diagnostics.Debug.WriteLine($"DPI scale factor for monitor {TargetMonitorIndex}: {dpiScale}");
                
                double newLeft = monitorRect.Left / dpiScale;
                double newTop = monitorRect.Top / dpiScale;
                double newWidth = (monitorRect.Right - monitorRect.Left) / dpiScale;
                double newHeight = (monitorRect.Bottom - monitorRect.Top) / dpiScale;
                
                System.Diagnostics.Debug.WriteLine($"Setting window position (DPI adjusted): ({newLeft}, {newTop}) size: {newWidth}x{newHeight}");
                
                // Ensure window is in normal state before positioning
                this.WindowState = WindowState.Normal;
                this.WindowStartupLocation = WindowStartupLocation.Manual;
                
                // Set position and size to fill the entire monitor (using DPI-adjusted values)
                this.Left = newLeft;
                this.Top = newTop;
                this.Width = newWidth;
                this.Height = newHeight;
                
                // Don't set visibility here - let the caller control when to show the window
                System.Diagnostics.Debug.WriteLine($"Window positioned at ({this.Left}, {this.Top}) with size {this.Width}x{this.Height}");

                System.Diagnostics.Debug.WriteLine($"Video display positioned on monitor {TargetMonitorIndex} at ({this.Left}, {this.Top}) {this.Width}x{this.Height}");
                System.Diagnostics.Debug.WriteLine($"Window state: {this.WindowState}, Startup location: {this.WindowStartupLocation}");
                System.Diagnostics.Debug.WriteLine("=== MoveToTargetMonitor Completed ===");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"=== ERROR in MoveToTargetMonitor ===");
                System.Diagnostics.Debug.WriteLine($"Exception Type: {ex.GetType().Name}");
                System.Diagnostics.Debug.WriteLine($"Message: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                System.Diagnostics.Debug.WriteLine($"=== END ERROR ===");
                
                // On error, hide the window to prevent overlay
                this.Hide();
            }
        }

        /// <summary>
        /// Hosts the provided media player element in this window.
        /// Detaches the element from its previous parent first.
        /// </summary>
        /// <param name="element">The UIElement to host (e.g., a MediaUriElement).</param>
        public void SetMediaPlayer(UIElement element)
        {
            // If the element is already here, do nothing.
            if (VideoContainer.Children.Contains(element))
            {
                return;
            }

            // Detach from any previous parent.
            var parent = (element as FrameworkElement)?.Parent as Panel;
            parent?.Children.Remove(element);

            // Remove existing media player elements but keep overlay containers
            ClearMediaPlayer();

            // Add the new media player element at index 0 (the bottom of the Z-order)
            // so that overlay elements like MarqueeContainer and LrcTextBlock stay on top.
            VideoContainer.Children.Insert(0, element);
        }

        public void ClearMediaPlayer()
        {
            // Only clear media player elements, keep overlay containers (Grids and TextBlocks)
            var elementsToRemove = VideoContainer.Children.Cast<UIElement>()
                .Where(child => {
                    if (child is Grid g && (g.Name == "MarqueeContainer" || g.Name == "VideoContainer")) return false;
                    if (child is TextBlock t && t.Name == "LrcTextBlock") return false;
                    return true;
                })
                .ToList();

            foreach (var element in elementsToRemove)
            {
                VideoContainer.Children.Remove(element);
            }
        }

        /// <summary>
        /// Unhooks the custom closing event and forces the window to close.
        /// </summary>
        public void ForceClose()
        {
            this.Closing -= VideoDisplayWindow_Closing;
            this.Close();
        }
        
        /// <summary>
        /// Sets the window to responsive fullscreen mode
        /// </summary>
        private void SetResponsiveFullscreen()
        {
            try
            {
                // Get primary screen dimensions using WPF SystemParameters
                var screenWidth = SystemParameters.PrimaryScreenWidth;
                var screenHeight = SystemParameters.PrimaryScreenHeight;
                
                this.Left = 0;
                this.Top = 0;
                this.Width = screenWidth;
                this.Height = screenHeight;
                this.WindowState = WindowState.Maximized;
                
                System.Diagnostics.Debug.WriteLine($"Set responsive fullscreen: {screenWidth}x{screenHeight}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting responsive fullscreen: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets available monitor information for display settings
        /// </summary>
        public static string[] GetAvailableMonitors()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== GetAvailableMonitors Started ===");
                var monitors = GetMonitors();
                System.Diagnostics.Debug.WriteLine($"GetMonitors returned {monitors.Count} monitors");
                
                var monitorInfo = new string[monitors.Count];
                
                for (int i = 0; i < monitors.Count; i++)
                {
                    var monitor = monitors[i];
                    var width = monitor.rcMonitor.Right - monitor.rcMonitor.Left;
                    var height = monitor.rcMonitor.Bottom - monitor.rcMonitor.Top;
                    var isPrimary = (monitor.dwFlags & 1) != 0 ? " (Primary)" : "";
                    monitorInfo[i] = $"Monitor {i + 1}: {width}x{height}{isPrimary}";
                    System.Diagnostics.Debug.WriteLine($"Monitor {i}: {monitorInfo[i]} - Bounds: ({monitor.rcMonitor.Left}, {monitor.rcMonitor.Top}, {monitor.rcMonitor.Right}, {monitor.rcMonitor.Bottom})");
                }
                
                System.Diagnostics.Debug.WriteLine("=== GetAvailableMonitors Completed ===");
                return monitorInfo;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"=== ERROR in GetAvailableMonitors ===");
                System.Diagnostics.Debug.WriteLine($"Exception: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                System.Diagnostics.Debug.WriteLine($"=== END ERROR ===");
                return new string[] { "Monitor 1: Error detecting monitors" };
            }
        }

        /// <summary>
        /// Gets a list of all monitor information structures.
        /// </summary>
        /// <returns>A list of MONITORINFO for all detected displays.</returns>
        public static System.Collections.Generic.List<MONITORINFO> GetAvailableMonitorsInfo()
        {
            return GetMonitors();
        }

        /// <summary>
        /// Gets the DPI scale factor for a specific monitor.
        /// Returns the scale factor (e.g., 1.0 for 100%, 1.5 for 150%, 2.0 for 200%).
        /// </summary>
        /// <param name="monitorIndex">The 0-based index of the monitor.</param>
        /// <returns>The DPI scale factor.</returns>
        public static double GetDpiScaleForMonitor(int monitorIndex)
        {
            try
            {
                // Try to get per-monitor DPI if we have the handle
                if (_monitorHandles.TryGetValue(monitorIndex, out IntPtr hMonitor))
                {
                    // MDT_EFFECTIVE_DPI = 0 (get the effective DPI used for scaling)
                    int result = GetDpiForMonitor(hMonitor, 0, out uint dpiX, out uint dpiY);
                    if (result == 0) // S_OK
                    {
                        double scale = dpiX / 96.0; // 96 is the standard DPI (100%)
                        System.Diagnostics.Debug.WriteLine($"GetDpiScaleForMonitor({monitorIndex}): DPI={dpiX}, Scale={scale}");
                        return scale;
                    }
                    System.Diagnostics.Debug.WriteLine($"GetDpiForMonitor failed with result: {result}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"No monitor handle found for index {monitorIndex}");
                }

                // Fallback: get system DPI
                uint systemDpi = GetDpiForSystem();
                double fallbackScale = systemDpi / 96.0;
                System.Diagnostics.Debug.WriteLine($"Using system DPI fallback: DPI={systemDpi}, Scale={fallbackScale}");
                return fallbackScale;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting DPI scale: {ex.Message}. Defaulting to 1.0");
                return 1.0; // Default to no scaling on error
            }
        }

        private static System.Collections.Generic.List<MONITORINFO> GetMonitors()
        {
            try
            {
                // Return cached list if already initialized
                if (_monitorsInitialized && _monitors.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"=== GetMonitors: Using cached list ({_monitors.Count} monitors) ===");
                    return _monitors;
                }
                
                System.Diagnostics.Debug.WriteLine("=== GetMonitors Started ===");
                _monitors.Clear();
                _monitorHandles.Clear();
                System.Diagnostics.Debug.WriteLine("Calling EnumDisplayMonitors...");
                bool result = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, MonitorEnumProc, IntPtr.Zero);
                System.Diagnostics.Debug.WriteLine($"EnumDisplayMonitors result: {result}");
                System.Diagnostics.Debug.WriteLine($"Found {_monitors.Count} monitors");
                _monitorsInitialized = true; // Mark as initialized
                System.Diagnostics.Debug.WriteLine("=== GetMonitors Completed ===");
                return _monitors;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"=== ERROR in GetMonitors ===");
                System.Diagnostics.Debug.WriteLine($"Exception: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                System.Diagnostics.Debug.WriteLine($"=== END ERROR ===");
                return new System.Collections.Generic.List<MONITORINFO>();
            }
        }

        private static bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"MonitorEnumProc called - hMonitor: {hMonitor}");
                var mi = new MONITORINFO();
                mi.cbSize = (uint)Marshal.SizeOf(mi);
                
                bool result = GetMonitorInfo(hMonitor, ref mi);
                System.Diagnostics.Debug.WriteLine($"GetMonitorInfo result: {result}");
                
                if (result)
                {
                    int monitorIndex = _monitors.Count;
                    _monitors.Add(mi);
                    _monitorHandles[monitorIndex] = hMonitor; // Store handle for DPI lookup
                    System.Diagnostics.Debug.WriteLine($"Added monitor {_monitors.Count}: Bounds=({mi.rcMonitor.Left},{mi.rcMonitor.Top},{mi.rcMonitor.Right},{mi.rcMonitor.Bottom}), Primary={(mi.dwFlags & 1) != 0}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("GetMonitorInfo failed");
                }
                
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR in MonitorEnumProc: {ex.Message}");
                return false;
            }
        }
    }
}