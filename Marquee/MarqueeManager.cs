using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UltimateKtv.Enums;

namespace UltimateKtv
{
    /// <summary>
    /// Manages marquee displays across main window and video display windows
    /// </summary>
    public class MarqueeManager
    {
        private static MarqueeManager? _instance;
        private readonly Dictionary<int, MarqueeControl> _activeMarquees;
        private MainWindow? _mainWindow;
        private VideoDisplayWindow? _videoDisplayWindow;

        public static MarqueeManager Instance => _instance ??= new MarqueeManager();

        private MarqueeManager()
        {
            _activeMarquees = new Dictionary<int, MarqueeControl>();
        }

        /// <summary>
        /// Initialize the marquee manager with window references
        /// </summary>
        public void Initialize(MainWindow mainWindow, VideoDisplayWindow? videoDisplayWindow = null)
        {
            _mainWindow = mainWindow;
            _videoDisplayWindow = videoDisplayWindow;
            
            // Subscribe to size changed events to update marquee sizing
            if (_mainWindow != null)
            {
                _mainWindow.SizeChanged += (s, e) => RefreshMarqueeSizes();
            }
            
            if (_videoDisplayWindow != null)
            {
                _videoDisplayWindow.SizeChanged += (s, e) => RefreshMarqueeSizes();
            }
        }

        /// <summary>
        /// Refreshes marquee sizes when window dimensions change
        /// </summary>
        private void RefreshMarqueeSizes()
        {
            foreach (var kvp in _activeMarquees)
            {
                var marquee = kvp.Value;
                var deviceId = kvp.Key;
                
                if (deviceId == 0 && _mainWindow?.MarqueeContainer != null)
                {
                    var containerWidth = _mainWindow.MarqueeContainer.ActualWidth;
                    if (containerWidth > 0)
                    {
                        marquee.Width = containerWidth;
                    }
                }
                else if (deviceId > 0 && _videoDisplayWindow?.VideoContainer != null)
                {
                    var containerWidth = _videoDisplayWindow.VideoContainer.ActualWidth;
                    if (containerWidth > 0)
                    {
                        marquee.Width = containerWidth;
                    }
                }
            }
        }

        /// <summary>
        /// Shows a marquee with the specified parameters
        /// </summary>
        /// <param name="text">The text to display</param>
        /// <param name="color">Text color brush</param>
        /// <param name="fontFamily">Font family for the text</param>
        /// <param name="fontSize">Font size</param>
        /// <param name="repeatCount">Number of times to repeat the animation</param>
        /// <param name="position">Position of the marquee (top or bottom)</param>
        /// <param name="speed">Animation speed in pixels per second</param>
        /// <param name="displayDevice">Target display device (0 = main window, 1+ = secondary displays)</param>
        public void ShowMarquee(string text, Brush color, FontFamily fontFamily, double fontSize,
            int repeatCount, MarqueePosition position, double speed, int displayDevice)
        {
            try
            {
//                System.Diagnostics.Debug.WriteLine($"ShowMarquee called: Device={displayDevice}, Text='{text}', Position={position}");
                
                // Stop any existing marquee on the target device
                StopMarquee(displayDevice);

                // Create new marquee control
                var marquee = new MarqueeControl();
                marquee.UpdateMarquee(text, color, fontFamily, fontSize, repeatCount, position, speed, displayDevice);

                // Add marquee completed event handler to clean up
                marquee.MarqueeCompleted += (sender, e) => StopMarquee(displayDevice);

                // Add to the appropriate window
                if (displayDevice == 0)
                {
                    // Main window
//                    System.Diagnostics.Debug.WriteLine($"Adding marquee to main window. Container exists: {_mainWindow?.MarqueeContainer != null}");
                    AddMarqueeToMainWindow(marquee, position);
                }
                else
                {
                    // Secondary display
//                    System.Diagnostics.Debug.WriteLine($"Adding marquee to video window. Container exists: {_videoDisplayWindow?.VideoContainer != null}");
                    AddMarqueeToVideoWindow(marquee, position);
                }

                // Store reference and start animation
                _activeMarquees[displayDevice] = marquee;
                marquee.StartMarquee();
                
//                System.Diagnostics.Debug.WriteLine($"Marquee started successfully on device {displayDevice}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing marquee: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Stops the marquee on the specified display device
        /// </summary>
        public void StopMarquee(int displayDevice)
        {
            try
            {
                if (_activeMarquees.TryGetValue(displayDevice, out var marquee))
                {
                    marquee.StopMarquee();
                    RemoveMarqueeFromWindow(marquee, displayDevice);
                    _activeMarquees.Remove(displayDevice);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error stopping marquee: {ex.Message}");
            }
        }

        /// <summary>
        /// Stops all active marquees
        /// </summary>
        public void StopAllMarquees()
        {
            var deviceIds = new List<int>(_activeMarquees.Keys);
            foreach (var deviceId in deviceIds)
            {
                StopMarquee(deviceId);
            }
        }

        /// <summary>
        /// Checks if a marquee is currently active on the specified device
        /// </summary>
        public bool IsMarqueeActive(int displayDevice)
        {
            return _activeMarquees.ContainsKey(displayDevice);
        }

        /// <summary>
        /// Shows static text with countdown timer (no scrolling animation)
        /// </summary>
        /// <param name="text">The text to display</param>
        /// <param name="color">Text color brush</param>
        /// <param name="fontFamily">Font family for the text</param>
        /// <param name="fontSize">Font size</param>
        /// <param name="position">Position of the text (top or bottom)</param>
        /// <param name="timeoutSeconds">Timer duration in seconds (0 = no timeout, display indefinitely)</param>
        /// <param name="displayDevice">Target display device (0 = main window, 1+ = secondary displays)</param>
        public void ShowStaticText(string text, Brush color, FontFamily fontFamily, double fontSize,
            MarqueePosition position, int timeoutSeconds, int displayDevice)
        {
            try
            {
//                System.Diagnostics.Debug.WriteLine($"ShowStaticText called: Device={displayDevice}, Text='{text}', Timeout={timeoutSeconds}s");
                
                // Stop any existing marquee on the target device
                StopMarquee(displayDevice);

                // Create new marquee control configured for static display
                var marquee = new MarqueeControl();
                marquee.UpdateStaticText(text, color, fontFamily, fontSize, position, timeoutSeconds, displayDevice);

                // Add marquee completed event handler to clean up
                marquee.MarqueeCompleted += (sender, e) => StopMarquee(displayDevice);

                // Add to the appropriate window
                if (displayDevice == 0)
                {
//                    System.Diagnostics.Debug.WriteLine($"Adding static text to main window");
                    AddMarqueeToMainWindow(marquee, position);
                }
                else
                {
//                    System.Diagnostics.Debug.WriteLine($"Adding static text to video window");
                    AddMarqueeToVideoWindow(marquee, position);
                }

                // Store reference and start display
                _activeMarquees[displayDevice] = marquee;
                marquee.StartStaticDisplay();
                
//                System.Diagnostics.Debug.WriteLine($"Static text started successfully on device {displayDevice}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing static text: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private void AddMarqueeToMainWindow(MarqueeControl marquee, MarqueePosition position)
        {
            if (_mainWindow?.MediaPlayerContainer == null)
            {
                System.Diagnostics.Debug.WriteLine("ERROR: Cannot add marquee - MainWindow or MediaPlayerContainer is null!");
                return;
            }

            // Use MediaPlayerContainer directly (we know this works)
            var container = _mainWindow.MediaPlayerContainer;
            container.UpdateLayout();
            
            var containerWidth = container.ActualWidth > 0 ? container.ActualWidth : 404;
            var containerHeight = container.ActualHeight > 0 ? container.ActualHeight : 300;
            
            // For corner positions, use smaller dimensions
            bool isCornerPosition = position == MarqueePosition.TopLeft || 
                                   position == MarqueePosition.TopRight || 
                                   position == MarqueePosition.BottomLeft || 
                                   position == MarqueePosition.BottomRight;

            if (isCornerPosition)
            {
                // Corner marquees are smaller and positioned absolutely
                marquee.Width = 300; // Fixed width for corner text (increased for longer text)
                marquee.Height = 80;
                
                // Set alignment based on corner
                switch (position)
                {
                    case MarqueePosition.TopLeft:
                        marquee.HorizontalAlignment = HorizontalAlignment.Left;
                        marquee.VerticalAlignment = VerticalAlignment.Top;
                        marquee.Margin = new Thickness(10, 10, 0, 0);
                        break;
                    case MarqueePosition.TopRight:
                        marquee.HorizontalAlignment = HorizontalAlignment.Right;
                        marquee.VerticalAlignment = VerticalAlignment.Top;
                        marquee.Margin = new Thickness(0, 10, 10, 0);
                        break;
                    case MarqueePosition.BottomLeft:
                        marquee.HorizontalAlignment = HorizontalAlignment.Left;
                        marquee.VerticalAlignment = VerticalAlignment.Bottom;
                        marquee.Margin = new Thickness(10, 0, 0, 15);
                        break;
                    case MarqueePosition.BottomRight:
                        marquee.HorizontalAlignment = HorizontalAlignment.Right;
                        marquee.VerticalAlignment = VerticalAlignment.Bottom;
                        marquee.Margin = new Thickness(0, 0, 10, 15);
                        break;
                }
            }
            else
            {
                // Full-width marquees for top/bottom/center positions
                marquee.Width = containerWidth;
                marquee.Height = 60;
                marquee.HorizontalAlignment = HorizontalAlignment.Stretch;

                // Position based on MarqueePosition
                if (position == MarqueePosition.Top)
                {
                    marquee.VerticalAlignment = VerticalAlignment.Top;
                    marquee.Margin = new Thickness(0, 10, 0, 0);
                }
                else if (position == MarqueePosition.Center)
                {
                    marquee.VerticalAlignment = VerticalAlignment.Center;
                    marquee.Margin = new Thickness(0);
                }
                else
                {
                    marquee.VerticalAlignment = VerticalAlignment.Bottom;
                    marquee.Margin = new Thickness(0, 0, 0, 15);
                }
            }

            // Clear existing marquees and add new one
            RemoveExistingMarquees(container);
            container.Children.Add(marquee);

            // Ensure visibility
            marquee.EnsureVisible();
            
//            System.Diagnostics.Debug.WriteLine($"Added marquee to MediaPlayerContainer: {marquee.Width}x{marquee.Height}, Position: {position}");
        }

        private void RemoveExistingMarquees(Grid container)
        {
            // Remove any existing MarqueeControl instances
            var marqueesToRemove = new List<MarqueeControl>();
            foreach (var child in container.Children)
            {
                if (child is MarqueeControl marquee)
                {
                    marqueesToRemove.Add(marquee);
                }
            }
            
            foreach (var marquee in marqueesToRemove)
            {
                marquee.StopMarquee();
                container.Children.Remove(marquee);
            }
        }

        private void AddMarqueeToVideoWindow(MarqueeControl marquee, MarqueePosition position)
        {
            if (_videoDisplayWindow?.VideoContainer != null)
            {
                // Set marquee size to match container or use default
                var containerWidth = _videoDisplayWindow.VideoContainer.ActualWidth;
                if (containerWidth <= 0)
                {
                    containerWidth = 1920; // Default secondary display width
                }
                
                var containerHeight = _videoDisplayWindow.VideoContainer.ActualHeight;
                if (containerHeight <= 0)
                {
                    containerHeight = 1080; // Default secondary display height
                }

                // Check if this is a corner position
                bool isCornerPosition = position == MarqueePosition.TopLeft || 
                                       position == MarqueePosition.TopRight || 
                                       position == MarqueePosition.BottomLeft || 
                                       position == MarqueePosition.BottomRight;

                if (isCornerPosition)
                {
                    // Corner marquees are smaller and positioned absolutely
                    marquee.Width = 400; // Larger width for secondary display to accommodate longer text
                    marquee.Height = 100;
                    
                    // Set alignment based on corner
                    switch (position)
                    {
                        case MarqueePosition.TopLeft:
                            marquee.HorizontalAlignment = HorizontalAlignment.Left;
                            marquee.VerticalAlignment = VerticalAlignment.Top;
                            marquee.Margin = new Thickness(20, 20, 0, 0);
                            break;
                        case MarqueePosition.TopRight:
                            marquee.HorizontalAlignment = HorizontalAlignment.Right;
                            marquee.VerticalAlignment = VerticalAlignment.Top;
                            marquee.Margin = new Thickness(0, 20, 20, 0);
                            break;
                        case MarqueePosition.BottomLeft:
                            marquee.HorizontalAlignment = HorizontalAlignment.Left;
                            marquee.VerticalAlignment = VerticalAlignment.Bottom;
                            marquee.Margin = new Thickness(20, 0, 0, 30);
                            break;
                        case MarqueePosition.BottomRight:
                            marquee.HorizontalAlignment = HorizontalAlignment.Right;
                            marquee.VerticalAlignment = VerticalAlignment.Bottom;
                            marquee.Margin = new Thickness(0, 0, 20, 30);
                            break;
                    }
                }
                else
                {
                    // Full-width marquees for top/bottom/center positions
                    marquee.Width = containerWidth;
                    marquee.Height = 80;
                    marquee.HorizontalAlignment = HorizontalAlignment.Stretch;

                    // Position based on MarqueePosition with better margins to prevent cutoff
                    if (position == MarqueePosition.Top)
                    {
                        marquee.VerticalAlignment = VerticalAlignment.Top;
                        marquee.Margin = new Thickness(0, 20, 0, 0);
                    }
                    else if (position == MarqueePosition.Center)
                    {
                        marquee.VerticalAlignment = VerticalAlignment.Center;
                        marquee.Margin = new Thickness(0);
                    }
                    else
                    {
                        marquee.VerticalAlignment = VerticalAlignment.Bottom;
                        marquee.Margin = new Thickness(0, 0, 0, 30);
                    }
                }

                _videoDisplayWindow.VideoContainer.Children.Add(marquee);
//                System.Diagnostics.Debug.WriteLine($"Added marquee to video window: {marquee.Width}x{marquee.Height}, Position: {position}");
            }
        }

        private void RemoveMarqueeFromWindow(MarqueeControl marquee, int displayDevice)
        {
            try
            {
                if (displayDevice == 0 && _mainWindow?.MediaPlayerContainer != null)
                {
                    _mainWindow.MediaPlayerContainer.Children.Remove(marquee);
//                    System.Diagnostics.Debug.WriteLine($"Removed marquee from MediaPlayerContainer");
                }
                else if (displayDevice > 0 && _videoDisplayWindow?.VideoContainer != null)
                {
                    _videoDisplayWindow.VideoContainer.Children.Remove(marquee);
//                    System.Diagnostics.Debug.WriteLine($"Removed marquee from VideoContainer");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error removing marquee from window: {ex.Message}");
            }
        }
    }
}