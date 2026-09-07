using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UltimateKtv.Enums;

namespace UltimateKtv
{
    public enum MarqueePriority
    {
        Low = 0,        // Startup / Idle marquee
        Normal = 1,     // Song playback marquee (MarqueeTextString1~3)
        High = 2        // Notification / Alert / Static announcement with timeout (e.g. 點播: ...)
    }

    public class MarqueeItem
    {
        public MarqueePriority Priority { get; set; } = MarqueePriority.Normal;
        public bool IsStatic { get; set; }
        public string Text { get; set; } = string.Empty;
        public Brush Color { get; set; } = Brushes.White;
        public FontFamily FontFamily { get; set; } = new FontFamily("Microsoft JhengHei");
        public FontWeight FontWeight { get; set; } = FontWeights.Normal;
        public double FontSize { get; set; } = 24;
        public Brush? StrokeColor { get; set; }
        public double StrokeThickness { get; set; } = 0;
        public MarqueePosition Position { get; set; } = MarqueePosition.Top;
        public int TimeoutSeconds { get; set; } = 0;
        public int RepeatCount { get; set; } = 1;
        public double Speed { get; set; } = 100;
        public int HoldTimeMs { get; set; } = 500;
        public int DisplayDevice { get; set; }
        public bool IsSongPlayback { get; set; }
    }

    internal class DeviceMarqueeState
    {
        public int DeviceId { get; }
        public MarqueeControl? ActiveMainControl { get; set; }
        public MarqueeItem? ActiveMainItem { get; set; }
        public MarqueeItem? PendingSongPlaybackItem { get; set; }
        public Dictionary<MarqueePosition, MarqueeControl> ActiveCornerControls { get; } = new();

        public DeviceMarqueeState(int deviceId)
        {
            DeviceId = deviceId;
        }
    }

    /// <summary>
    /// Manages marquee displays across main window and video display windows with priority and queuing support.
    /// </summary>
    public class MarqueeManager
    {
        private static MarqueeManager? _instance;
        private readonly Dictionary<int, DeviceMarqueeState> _deviceStates;
        private MainWindow? _mainWindow;
        private VideoDisplayWindow? _videoDisplayWindow;

        public static MarqueeManager Instance => _instance ??= new MarqueeManager();

        private MarqueeManager()
        {
            _deviceStates = new Dictionary<int, DeviceMarqueeState>();
        }

        private DeviceMarqueeState GetState(int deviceId)
        {
            if (!_deviceStates.TryGetValue(deviceId, out var state))
            {
                state = new DeviceMarqueeState(deviceId);
                _deviceStates[deviceId] = state;
            }
            return state;
        }

        private static bool IsCornerPosition(MarqueePosition position)
        {
            return position is MarqueePosition.TopLeft
                or MarqueePosition.TopRight
                or MarqueePosition.BottomLeft
                or MarqueePosition.BottomRight;
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
            foreach (var kvp in _deviceStates)
            {
                var state = kvp.Value;
                var deviceId = kvp.Key;
                
                double width = 0;
                if (deviceId == 0)
                {
                    width = _mainWindow?.MarqueeContainer?.ActualWidth ?? _mainWindow?.MediaPlayerContainer?.ActualWidth ?? 0;
                }
                else
                {
                    width = _videoDisplayWindow?.MarqueeContainer?.ActualWidth ?? _videoDisplayWindow?.VideoContainer?.ActualWidth ?? 0;
                }

                if (width > 0 && state.ActiveMainControl != null)
                {
                    state.ActiveMainControl.Width = width;
                }
            }
        }

        /// <summary>
        /// Shows a marquee with the specified parameters
        /// </summary>
        public void ShowMarquee(string text, Brush color, FontFamily fontFamily, double fontSize,
            int repeatCount, MarqueePosition position, double speed, int displayDevice)
        {
            ShowMarquee(text, color, fontFamily, fontSize, repeatCount, position, speed, displayDevice,
                FontWeights.Normal, null, 0, 500, MarqueePriority.Normal, false);
        }

        /// <summary>
        /// Shows a marquee with custom stroke, weight, and hold time parameters
        /// </summary>
        public void ShowMarquee(string text, Brush color, FontFamily fontFamily, double fontSize,
            int repeatCount, MarqueePosition position, double speed, int displayDevice,
            FontWeight fontWeight, Brush? strokeColor, double strokeThickness, int holdTimeMs,
            MarqueePriority priority = MarqueePriority.Normal, bool isSongPlayback = false)
        {
            var item = new MarqueeItem
            {
                Text = text,
                Color = color,
                FontFamily = fontFamily,
                FontSize = fontSize,
                RepeatCount = repeatCount,
                Position = position,
                Speed = speed,
                DisplayDevice = displayDevice,
                FontWeight = fontWeight,
                StrokeColor = strokeColor,
                StrokeThickness = strokeThickness,
                HoldTimeMs = holdTimeMs,
                IsStatic = false,
                Priority = priority,
                IsSongPlayback = isSongPlayback
            };

            EnqueueOrDisplayItem(item);
        }

        /// <summary>
        /// Shows static text with countdown timer (no scrolling animation)
        /// </summary>
        public void ShowStaticText(string text, Brush color, FontFamily fontFamily, double fontSize,
            MarqueePosition position, int timeoutSeconds, int displayDevice,
            MarqueePriority? priority = null)
        {
            MarqueePriority effectivePriority = priority ?? (timeoutSeconds > 0 ? MarqueePriority.High : MarqueePriority.Normal);

            var item = new MarqueeItem
            {
                Text = text,
                Color = color,
                FontFamily = fontFamily,
                FontSize = fontSize,
                Position = position,
                TimeoutSeconds = timeoutSeconds,
                DisplayDevice = displayDevice,
                IsStatic = true,
                Priority = effectivePriority,
                IsSongPlayback = false
            };

            EnqueueOrDisplayItem(item);
        }

        private void EnqueueOrDisplayItem(MarqueeItem item)
        {
            try
            {
                var state = GetState(item.DisplayDevice);
                if (IsCornerPosition(item.Position))
                {
                    HandleCornerItem(state, item);
                }
                else
                {
                    HandleMainItem(state, item);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error displaying marquee: {ex.Message}");
            }
        }

        private void HandleCornerItem(DeviceMarqueeState state, MarqueeItem item)
        {
            if (state.ActiveCornerControls.TryGetValue(item.Position, out var existing))
            {
                existing.StopMarquee();
                RemoveMarqueeFromWindow(existing, state.DeviceId);
                state.ActiveCornerControls.Remove(item.Position);
            }

            var marquee = new MarqueeControl();
            if (item.IsStatic)
            {
                marquee.UpdateStaticText(item.Text, item.Color, item.FontFamily, item.FontSize, item.Position, item.TimeoutSeconds, state.DeviceId, item.FontWeight, item.StrokeColor, item.StrokeThickness);
                marquee.MarqueeCompleted += (s, e) =>
                {
                    marquee.StopMarquee();
                    RemoveMarqueeFromWindow(marquee, state.DeviceId);
                    state.ActiveCornerControls.Remove(item.Position);
                };
                if (state.DeviceId == 0) AddMarqueeToMainWindow(marquee, item.Position);
                else AddMarqueeToVideoWindow(marquee, item.Position);
                state.ActiveCornerControls[item.Position] = marquee;
                marquee.StartStaticDisplay();
            }
            else
            {
                marquee.UpdateMarquee(item.Text, item.Color, item.FontFamily, item.FontSize, item.RepeatCount, item.Position, item.Speed, state.DeviceId, item.FontWeight, item.StrokeColor, item.StrokeThickness, item.HoldTimeMs);
                marquee.MarqueeCompleted += (s, e) =>
                {
                    marquee.StopMarquee();
                    RemoveMarqueeFromWindow(marquee, state.DeviceId);
                    state.ActiveCornerControls.Remove(item.Position);
                };
                if (state.DeviceId == 0) AddMarqueeToMainWindow(marquee, item.Position);
                else AddMarqueeToVideoWindow(marquee, item.Position);
                state.ActiveCornerControls[item.Position] = marquee;
                marquee.StartMarquee();
            }
        }

        private void HandleMainItem(DeviceMarqueeState state, MarqueeItem item)
        {
            if (item.IsSongPlayback)
            {
                // This is the latest song playback marquee for the current song
                state.PendingSongPlaybackItem = item;

                // If a high-priority notification (like "點播: 歌名 - 歌手") is currently displaying on screen:
                // Keep the notification showing; do NOT interrupt it.
                // When the notification completes (3s timer), OnMainMarqueeCompleted will automatically start PendingSongPlaybackItem!
                if (state.ActiveMainItem != null && state.ActiveMainItem.Priority == MarqueePriority.High)
                {
                    return;
                }

                // Otherwise, play immediately
                PlayMainItem(state, item);
                return;
            }

            if (item.Priority == MarqueePriority.High)
            {
                // If a song playback marquee is currently running, save it as PendingSongPlaybackItem
                // so it will resume/start after this notification finishes
                if (state.ActiveMainItem != null && state.ActiveMainItem.IsSongPlayback)
                {
                    state.PendingSongPlaybackItem = state.ActiveMainItem;
                }

                // Show the high-priority notification immediately
                PlayMainItem(state, item);
                return;
            }

            if (item.Priority == MarqueePriority.Low)
            {
                // Do not interrupt active higher priority marquee
                if (state.ActiveMainItem != null && state.ActiveMainItem.Priority > MarqueePriority.Low)
                {
                    return;
                }
                PlayMainItem(state, item);
                return;
            }

            // Normal priority item
            PlayMainItem(state, item);
        }

        private void PlayMainItem(DeviceMarqueeState state, MarqueeItem item)
        {
            if (state.ActiveMainControl != null)
            {
                state.ActiveMainControl.StopMarquee();
                RemoveMarqueeFromWindow(state.ActiveMainControl, state.DeviceId);
                state.ActiveMainControl = null;
            }

            state.ActiveMainItem = item;
            var marquee = new MarqueeControl();

            if (item.IsStatic)
            {
                marquee.UpdateStaticText(item.Text, item.Color, item.FontFamily, item.FontSize, item.Position, item.TimeoutSeconds, state.DeviceId, item.FontWeight, item.StrokeColor, item.StrokeThickness);
                marquee.MarqueeCompleted += (s, e) => OnMainMarqueeCompleted(state, marquee, item);
                if (state.DeviceId == 0) AddMarqueeToMainWindow(marquee, item.Position);
                else AddMarqueeToVideoWindow(marquee, item.Position);
                state.ActiveMainControl = marquee;
                marquee.StartStaticDisplay();
            }
            else
            {
                marquee.UpdateMarquee(item.Text, item.Color, item.FontFamily, item.FontSize, item.RepeatCount, item.Position, item.Speed, state.DeviceId, item.FontWeight, item.StrokeColor, item.StrokeThickness, item.HoldTimeMs);
                marquee.MarqueeCompleted += (s, e) => OnMainMarqueeCompleted(state, marquee, item);
                if (state.DeviceId == 0) AddMarqueeToMainWindow(marquee, item.Position);
                else AddMarqueeToVideoWindow(marquee, item.Position);
                state.ActiveMainControl = marquee;
                marquee.StartMarquee();
            }
        }

        private void OnMainMarqueeCompleted(DeviceMarqueeState state, MarqueeControl control, MarqueeItem completedItem)
        {
            try
            {
                control.StopMarquee();
                RemoveMarqueeFromWindow(control, state.DeviceId);

                if (state.ActiveMainControl == control)
                {
                    state.ActiveMainControl = null;
                    state.ActiveMainItem = null;
                }

                // If the item that just completed was the song playback marquee itself, clear PendingSongPlaybackItem
                if (completedItem.IsSongPlayback)
                {
                    state.PendingSongPlaybackItem = null;
                }

                // If there is a pending song playback marquee (queued during notification or preempted), start it now!
                if (state.PendingSongPlaybackItem != null)
                {
                    var pending = state.PendingSongPlaybackItem;
                    PlayMainItem(state, pending);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error on main marquee completed: {ex.Message}");
            }
        }

        /// <summary>
        /// Stops the marquee on the specified display device and clears any pending playback marquee
        /// </summary>
        public void StopMarquee(int displayDevice)
        {
            try
            {
                if (_deviceStates.TryGetValue(displayDevice, out var state))
                {
                    if (state.ActiveMainControl != null)
                    {
                        state.ActiveMainControl.StopMarquee();
                        RemoveMarqueeFromWindow(state.ActiveMainControl, displayDevice);
                        state.ActiveMainControl = null;
                        state.ActiveMainItem = null;
                    }
                    state.PendingSongPlaybackItem = null;

                    foreach (var kvp in state.ActiveCornerControls.ToList())
                    {
                        kvp.Value.StopMarquee();
                        RemoveMarqueeFromWindow(kvp.Value, displayDevice);
                    }
                    state.ActiveCornerControls.Clear();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error stopping marquee: {ex.Message}");
            }
        }

        /// <summary>
        /// Stops all active marquees across all display devices
        /// </summary>
        public void StopAllMarquees()
        {
            var deviceIds = new List<int>(_deviceStates.Keys);
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
            if (_deviceStates.TryGetValue(displayDevice, out var state))
            {
                return state.ActiveMainControl != null || state.ActiveCornerControls.Count > 0;
            }
            return false;
        }

        private void AddMarqueeToMainWindow(MarqueeControl marquee, MarqueePosition position)
        {
            var container = _mainWindow?.MarqueeContainer ?? _mainWindow?.MediaPlayerContainer;
            if (container == null)
            {
                System.Diagnostics.Debug.WriteLine("ERROR: Cannot add marquee - MainWindow container is null!");
                return;
            }

            container.UpdateLayout();
            var containerWidth = container.ActualWidth > 0 ? container.ActualWidth : 404;
            var containerHeight = container.ActualHeight > 0 ? container.ActualHeight : 300;

            if (IsCornerPosition(position))
            {
                marquee.Width = 300;
                marquee.Height = 80;

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
                marquee.Width = containerWidth;
                marquee.Height = Math.Max(60, (marquee.TextFontSize + marquee.StrokeThickness * 2) * 1.3);
                marquee.HorizontalAlignment = HorizontalAlignment.Stretch;

                if (position == MarqueePosition.Top)
                {
                    marquee.VerticalAlignment = VerticalAlignment.Top;
                    marquee.Margin = new Thickness(0, 0, 0, 0);
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

            marquee.IsHitTestVisible = false;
            container.Children.Add(marquee);
            marquee.EnsureVisible();
        }

        private void AddMarqueeToVideoWindow(MarqueeControl marquee, MarqueePosition position)
        {
            var container = _videoDisplayWindow?.MarqueeContainer ?? _videoDisplayWindow?.VideoContainer;
            if (container == null)
            {
                System.Diagnostics.Debug.WriteLine("ERROR: Cannot add marquee - VideoDisplayWindow container is null!");
                return;
            }

            var containerWidth = container.ActualWidth > 0 ? container.ActualWidth : 1920;
            var containerHeight = container.ActualHeight > 0 ? container.ActualHeight : 1080;

            if (IsCornerPosition(position))
            {
                marquee.Width = 400;
                marquee.Height = 100;

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
                marquee.Width = containerWidth;
                marquee.Height = Math.Max(120, (marquee.TextFontSize + marquee.StrokeThickness * 2) * 1.4);
                marquee.HorizontalAlignment = HorizontalAlignment.Stretch;

                if (position == MarqueePosition.Top)
                {
                    marquee.VerticalAlignment = VerticalAlignment.Top;
                    marquee.Margin = new Thickness(0, 0, 0, 0);
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

            marquee.IsHitTestVisible = false;
            container.Children.Add(marquee);
            marquee.EnsureVisible();
        }

        private void RemoveMarqueeFromWindow(MarqueeControl marquee, int displayDevice)
        {
            try
            {
                if (displayDevice == 0)
                {
                    _mainWindow?.MarqueeContainer?.Children.Remove(marquee);
                    _mainWindow?.MediaPlayerContainer?.Children.Remove(marquee);
                }
                else
                {
                    _videoDisplayWindow?.MarqueeContainer?.Children.Remove(marquee);
                    _videoDisplayWindow?.VideoContainer?.Children.Remove(marquee);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error removing marquee from window: {ex.Message}");
            }
        }
    }
}