using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using UltimateKtv.Enums;


namespace UltimateKtv
{
    public partial class MainWindow
    {
        // Windows API for cursor clipping
        [DllImport("user32.dll")]
        private static extern bool ClipCursor(ref RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClipCursor(IntPtr lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        /// <summary>
        /// Initializes display configuration for single or multi-monitor setup.
        /// Returns whether single monitor mode is active.
        /// </summary>
        private bool InitializeDisplayConfiguration()
        {
            var monitors = VideoDisplayWindow.GetAvailableMonitorsInfo();
            var settings = SettingsManager.Instance.CurrentSettings;
            bool isSingleMonitorMode = settings.ConsoleScreen == settings.PlayerScreen;

            try
            {
                DebugLog("=== InitializeDisplayConfiguration Started ===");

                var availableMonitors = VideoDisplayWindow.GetAvailableMonitors();
                DebugLog($"Available monitors: {availableMonitors.Length}");
                DebugLog($"ConsoleScreen: {settings.ConsoleScreen}, PlayerScreen: {settings.PlayerScreen}");

                if (availableMonitors.Length > 1 && !isSingleMonitorMode)
                {
                    DebugLog("Multiple monitors detected and different screens configured. Setting up dual-display mode.");

                    // 1. Create the secondary window
                    _videoDisplayWindow = new VideoDisplayWindow { TargetMonitorIndex = settings.PlayerScreen - 1 };
                    _videoDisplayWindow.OwnerWindow = this;
                    _videoDisplayWindow.MoveToTargetMonitor();

                    // Update marquee manager with video display window reference
                    MarqueeManager.Instance.Initialize(this, _videoDisplayWindow);
                    _videoDisplayWindow.Show();

                    // 2. Host the single mediaUriElement in the secondary window
                    _videoDisplayWindow.SetMediaPlayer(mediaUriElement);
                    DebugLog("Hosted media element in VideoDisplayWindow.");

                    // 3. Set up the preview in the main window using a VisualBrush
                    var previewRectangle = new Rectangle();
                    var visualBrush = new VisualBrush(mediaUriElement)
                    {
                        Stretch = Stretch.Uniform
                    };
                    previewRectangle.Fill = visualBrush;

                    // 4. Add the preview rectangle to the main window's container
                    MediaPlayerContainer.Children.Clear();
                    MediaPlayerContainer.Children.Add(previewRectangle);
                    DebugLog("Set up VisualBrush preview in MainWindow.");

                    // 5. Set initial visibility of the preview based on the control flag
                    previewRectangle.Visibility = _showPreviewInMainWindow ? Visibility.Visible : Visibility.Collapsed;
                }
                else
                {
                    DebugLog("Single monitor mode or single monitor detected. Setting up single-display mode with marquee on player screen.");

                    // In single-monitor mode, create a VideoDisplayWindow to show the player on the configured screen
                    if (isSingleMonitorMode)
                    {
                        DebugLog("Single-monitor mode: Creating VideoDisplayWindow for player display.");
                        // Use the PlayerScreen setting (convert 1-based to 0-based index)
                        int targetMonitorIndex = settings.PlayerScreen - 1;
                        _videoDisplayWindow = new VideoDisplayWindow { TargetMonitorIndex = targetMonitorIndex };
                        _videoDisplayWindow.OwnerWindow = this;
                        _videoDisplayWindow.MoveToTargetMonitor();
                        
                        // Update marquee manager with video display window reference
                        MarqueeManager.Instance.Initialize(this, _videoDisplayWindow);
                        _videoDisplayWindow.Show();
                        
                        // Host the media element in the video display window
                        _videoDisplayWindow.SetMediaPlayer(mediaUriElement);
                        DebugLog("Hosted media element in VideoDisplayWindow for single-monitor mode.");

                        // Important: Subscribe to mouse events on the video display window to handle cursor hide/show
                        _videoDisplayWindow.MouseMove += MainWindow_MouseMove;
                        _videoDisplayWindow.PreviewMouseMove += MainWindow_MouseMove;


                        // Set up the preview in the main window using a VisualBrush
                        var previewRectangle = new Rectangle();
                        var visualBrush = new VisualBrush(mediaUriElement)
                        {
                            Stretch = Stretch.Uniform
                        };
                        previewRectangle.Fill = visualBrush;

                        // Add the preview rectangle to the main window's container
                        MediaPlayerContainer.Children.Clear();
                        MediaPlayerContainer.Children.Add(previewRectangle);
                        DebugLog("Set up VisualBrush preview in MainWindow for single-monitor mode.");

                        // Set initial visibility of the preview based on the control flag
                        previewRectangle.Visibility = _showPreviewInMainWindow ? Visibility.Visible : Visibility.Collapsed;
                    }
                    else
                    {
                        // Multiple monitors but same screen setting - host in main window
                        var containerGrid = new Grid();
                        containerGrid.Children.Add(mediaUriElement);
                        containerGrid.Background = new SolidColorBrush(Colors.Black);

                        MediaPlayerContainer.Children.Clear();
                        MediaPlayerContainer.Children.Add(containerGrid);
                        DebugLog("Hosted media element directly in MainWindow.");
                    }
                }

                DebugLog("=== InitializeDisplayConfiguration Completed ===");
            }
            catch (Exception ex)
            {
                // Fallback on error: try to host in main window
                AppLogger.LogError("Error during secondary display initialization. Falling back to single display mode.", ex);
                MediaPlayerContainer.Children.Clear();
                MediaPlayerContainer.Children.Add(mediaUriElement);
            }

            return isSingleMonitorMode;
        }

        /// <summary>
        /// Moves this window to the specified monitor.
        /// </summary>
        /// <param name="monitorIndex">The index of the target monitor.</param>
        private void MoveWindowToMonitor(int monitorIndex)
        {
            try
            {
                AppLogger.Log($"Attempting to move MainWindow to monitor {monitorIndex}.");
                var monitors = VideoDisplayWindow.GetAvailableMonitorsInfo();
                AppLogger.Log($"Detected {monitors.Count} monitors.");

                // Convert 1-based index from settings to 0-based for array access
                int zeroBasedIndex = monitorIndex - 1;
                if (zeroBasedIndex >= 0 && zeroBasedIndex < monitors.Count)
                {
                    var targetMonitor = monitors[zeroBasedIndex];
                    var monitorRect = targetMonitor.rcMonitor;
                    var isPrimary = (targetMonitor.dwFlags & 1) != 0;

                    DebugLog($"Target monitor {monitorIndex}: Bounds=({monitorRect.Left},{monitorRect.Top},{monitorRect.Right},{monitorRect.Bottom}), Primary={isPrimary}");
                    DebugLog($"Current window position: ({this.Left},{this.Top}) {this.Width}x{this.Height}");

                    // Get DPI scale factor for coordinate conversion
                    double dpiScale = VideoDisplayWindow.GetDpiScaleForMonitor(zeroBasedIndex);
                    DebugLog($"DPI scale factor: {dpiScale}");

                    // Set window to normal state before moving to ensure correct placement
                    this.WindowState = System.Windows.WindowState.Normal;
                    this.WindowStartupLocation = WindowStartupLocation.Manual;

                    // Convert to WPF DIUs
                    this.Left = monitorRect.Left / dpiScale;
                    this.Top = monitorRect.Top / dpiScale;

                    DebugLog($"Window moved to: ({this.Left},{this.Top})");

                    // After moving, maximize it on the new monitor
                    this.WindowState = System.Windows.WindowState.Maximized;

                    DebugLog($"Window maximized on monitor {zeroBasedIndex}");
                }
                else
                {
                    AppLogger.Log($"Invalid monitor index {monitorIndex}. Available: 1 to {monitors.Count}.");

                    // Fallback: use primary monitor (index 1) if available
                    if (monitors.Count > 0)
                    {
                        AppLogger.Log("Falling back to primary monitor (index 1).");
                        var primaryMonitor = monitors[0];
                        var monitorRect = primaryMonitor.rcMonitor;

                        // Get DPI scale factor for coordinate conversion
                        double dpiScale = VideoDisplayWindow.GetDpiScaleForMonitor(0);

                        this.WindowState = System.Windows.WindowState.Normal;
                        this.WindowStartupLocation = WindowStartupLocation.Manual;
                        this.Left = monitorRect.Left / dpiScale;
                        this.Top = monitorRect.Top / dpiScale;
                        this.WindowState = System.Windows.WindowState.Maximized;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Failed to move MainWindow to target monitor.", ex);
            }
        }

        /// <summary>
        /// Applies settings from SettingsManager to the application's state.
        /// </summary>
        private void ApplySettings()
        {
            var settings = SettingsManager.Instance.CurrentSettings;
            AppLogger.Log($"Applying settings: ShowPreview={settings.ShowPreviewInMainWindow}, NewSongDays={settings.NewSongDays}, FullScreen={settings.IsMediaFullScreen}");

            // Apply settings to MainWindow properties
            _showPreviewInMainWindow = settings.ShowPreviewInMainWindow;
            NewSongDays = settings.NewSongDays;
            PlayCountUpdatePercentage = settings.PlayCountUpdatePercentage;

            // Re-initialize photo cache to reflect any path changes
            SingerPhotoManager.InitializeCache();
            
            bool hasPhotos = SingerPhotoManager.HasAnyPhotos();
            
            // Effective visual style requires both the setting to be enabled AND photos to exist
            IsVisualSingerStyleEffective = settings.VisualSingerStyle && hasPhotos;

            // PageSize depends on the effective style
            PageSize = IsVisualSingerStyleEffective ? 18 : 60;

            AppLogger.Log($"ApplySettings: VisualStyleSetting={settings.VisualSingerStyle}, HasPhotos={hasPhotos}, EffectiveVisual={IsVisualSingerStyleEffective}, EffectivePageSize={PageSize}");
            
            if (settings.VisualSingerStyle && !hasPhotos)
            {
                AppLogger.Log("VisualSingerStyle is enabled but no photos were found. Falling back to text mode.");
            }

            // Apply media stretch based on fullscreen setting
            SetMediaStretch(settings.IsMediaFullScreen ? Stretch.Fill : Stretch.Uniform);

            // Apply singer name font size
            UpdateFontResource("VisualSingerNameFontSize", settings.VisualSingerNameFontSize);

            // Apply audio amplify setting
            SetAudioAmplify(settings.AudioAmplify);

            // Apply sound curve setting
            if (Enum.IsDefined(typeof(MainWindow.VolumeMapper.Mode), settings.SoundCurve))
            {
                MainWindow.VolumeMapper.MappingMode = (MainWindow.VolumeMapper.Mode)settings.SoundCurve;
                
                // Apply exponent setting with validation (0.1 - 5.0)
                double exponent = Math.Max(0.1, Math.Min(5.0, settings.SoundCurveExponent));
                MainWindow.VolumeMapper.Exponent = exponent;
                
                AppLogger.Log($"Applied SoundCurve setting: {MainWindow.VolumeMapper.MappingMode}, Exponent: {MainWindow.VolumeMapper.Exponent}");
            }
            else
            {
                // Fallback to default if invalid value in json
                MainWindow.VolumeMapper.MappingMode = MainWindow.VolumeMapper.Mode.Exponential;
                AppLogger.Log($"Invalid SoundCurve value ({settings.SoundCurve}), defaulting to Exponential.");
            }

            // Note: Window positioning is handled in MainWindow_Loaded event for proper timing
            // Note: HTTP server is started in MainWindow constructor after initialization
            // Note: Cursor limiting is applied in MainWindow_Loaded event after window positioning
        }

        /// <summary>
        /// Applies cursor limiting to the console screen.
        /// Restricts the cursor to the monitor specified by ConsoleScreen setting.
        /// </summary>
        public void ApplyCursorLimit()
        {
            try
            {
                var settings = SettingsManager.Instance.CurrentSettings;
                var monitors = VideoDisplayWindow.GetAvailableMonitorsInfo();
                
                // Convert 1-based ConsoleScreen to 0-based index
                int consoleScreenIndex = settings.ConsoleScreen - 1;
                
                if (consoleScreenIndex >= 0 && consoleScreenIndex < monitors.Count)
                {
                    var targetMonitor = monitors[consoleScreenIndex];
                    var monitorRect = targetMonitor.rcMonitor;
                    
                    RECT rect = new RECT
                    {
                        Left = monitorRect.Left,
                        Top = monitorRect.Top,
                        Right = monitorRect.Right,
                        Bottom = monitorRect.Bottom
                    };
                    ClipCursor(ref rect);
                    AppLogger.Log($"Cursor limited to console screen {settings.ConsoleScreen}: ({monitorRect.Left},{monitorRect.Top},{monitorRect.Right},{monitorRect.Bottom})");
                }
                else
                {
                    AppLogger.Log($"Invalid ConsoleScreen index {settings.ConsoleScreen}. Cursor limit not applied.");
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Failed to apply cursor limit", ex);
            }
        }

        /// <summary>
        /// Removes cursor limiting, allowing the cursor to move freely across all screens.
        /// </summary>
        public void RemoveCursorLimit()
        {
            try
            {
                // Pass IntPtr.Zero to remove cursor clipping
                ClipCursor(IntPtr.Zero);
                AppLogger.Log("Cursor limit removed");
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Failed to remove cursor limit", ex);
            }
        }

        /// <summary>
        /// Displays the web host IP and port information on the player screen for 10 seconds
        /// </summary>
        public void DisplayWebHostInfoMarquee()
        {
            try
            {
                var settings = SettingsManager.Instance.CurrentSettings;
                if (_httpServer == null)
                {
                    AppLogger.Log("HTTP server not available for marquee display");
                    return;
                }

                // Get the server URL
                string serverUrl = _httpServer.ServerUrl;
                string marqueeText = $"遠端點歌網址: {serverUrl}";

                AppLogger.Log($"Displaying web host info marquee: {marqueeText}");

                // In single-monitor mode (ConsoleScreen == PlayerScreen), always display on the VideoDisplayWindow (device 1)
                // because the main window (device 0) is hidden
                // In multi-monitor mode, display on the configured PlayerScreen
                bool isSingleMonitorMode = settings.ConsoleScreen == settings.PlayerScreen;
                int displayDevice = isSingleMonitorMode ? 1 : (settings.PlayerScreen - 1);
                
                AppLogger.Log($"Marquee display device: {displayDevice} (SingleMonitorMode: {isSingleMonitorMode})");
                
                // Show the marquee indefinitely until a song is ordered (timeoutSeconds: 0)
                MarqueeManager.Instance.ShowStaticText(
                    marqueeText,
                    TextSettingsHandler.WebHostInfoForeground,
                    TextSettingsHandler.FontFamily,
                    fontSize: TextSettingsHandler.Settings.WebHostInfoFontSize,
                    position: UltimateKtv.Enums.MarqueePosition.Top,
                    timeoutSeconds: 0,
                    displayDevice: displayDevice
                );

                // Set flag so marquee can be dismissed when a song is ordered
                _isWebHostMarqueeDisplayed = true;

                AppLogger.Log("Web host info marquee displayed persistently on PlayerScreen (will dismiss on first song order)");
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Failed to display web host info marquee", ex);
            }
        }

        /// <summary>
        /// Initializes the idle timer for cursor auto-hide in single monitor mode.
        /// </summary>
        private void InitializeCursorIdleTimer()
        {
            _cursorIdleTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _cursorIdleTimer.Tick += CursorIdleTimer_Tick;
        }

        /// <summary>
        /// Handles mouse movement to show the cursor and reset the idle timer.
        /// </summary>
        private void MainWindow_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            ResetCursorIdleTimer();
        }

        /// <summary>
        /// Resets the cursor idle timer and ensures the cursor is visible.
        /// </summary>
        private void ResetCursorIdleTimer()
        {
            if (IsSingleMonitorMode)
            {
                // Ensure cursor is visible
                System.Windows.Input.Mouse.OverrideCursor = null;

                // Reset and restart the timer
                if (_cursorIdleTimer != null)
                {
                    _cursorIdleTimer.Stop();
                    _cursorIdleTimer.Start();
                }
            }
        }

        /// <summary>
        /// Event handler for the cursor idle timer tick. Hides the cursor.
        /// </summary>
        private void CursorIdleTimer_Tick(object? sender, EventArgs e)
        {
            if (IsSingleMonitorMode && this.Visibility != Visibility.Visible)
            {
                // Hide cursor by setting OverrideCursor to None
                System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.None;
                DebugLog("Cursor hidden due to inactivity.");
                
                // Stop the timer so it doesn't keep ticking while hidden
                _cursorIdleTimer?.Stop();
            }
        }

    }
}