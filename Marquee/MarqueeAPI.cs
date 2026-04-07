using System;
using System.Windows.Media;
using UltimateKtv.Enums;

namespace UltimateKtv
{
    /// <summary>
    /// Simplified API for marquee operations with preset configurations
    /// </summary>
    public static class MarqueeAPI
    {
        /// <summary>
        /// Converts MarqueeDisplayDevice enum to actual display device index
        /// </summary>
        /// <param name="device">The target display device enum</param>
        /// <returns>Display device index (0 = main window, 1+ = secondary displays)</returns>
        /// <exception cref="InvalidOperationException">Thrown when in single screen mode</exception>
        private static int GetDisplayDeviceIndex(MarqueeDisplayDevice device)
        {
            var settings = SettingsManager.Instance.CurrentSettings;
            bool isSingleMonitorMode = settings.ConsoleScreen == settings.PlayerScreen;

            if (isSingleMonitorMode)
            {
                // In single monitor mode, both screens are the same
                // Always display on the VideoDisplayWindow (device 1)
                return 1;
            }

            // Multi-monitor mode
            if (device == MarqueeDisplayDevice.PlayerScreen)
            {
                // PlayerScreen uses the secondary display window
                return 1;
            }
            else // ConsoleScreen
            {
                // ConsoleScreen uses the main window
                return 0;
            }
        }

        /// <summary>
        /// Checks if single screen mode is active (PlayerScreen equals ConsoleScreen)
        /// </summary>
        /// <returns>True if in single screen mode</returns>
        public static bool IsSingleScreenMode()
        {
            var settings = SettingsManager.Instance.CurrentSettings;
            return settings.ConsoleScreen == settings.PlayerScreen;
        }

        /// <summary>
        /// Shows a simple text marquee with default settings
        /// </summary>
        /// <param name="text">Text to display</param>
        /// <param name="device">Target display: PlayerScreen or ConsoleScreen</param>
        public static void ShowText(string text, MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            int displayDevice = GetDisplayDeviceIndex(device);
            MarqueeManager.Instance.ShowMarquee(
                text, 
                TextSettingsHandler.MarqueeForeground,
                TextSettingsHandler.FontFamily, 
                TextSettingsHandler.Settings.MarqueeFontSize, 
                TextSettingsHandler.Settings.MarqueeRepeatCount, 
                MarqueePosition.Top, 
                TextSettingsHandler.Settings.MarqueeSpeed, 
                displayDevice
            );
        }

        /// <summary>
        /// Shows an announcement marquee with prominent styling
        /// </summary>
        /// <param name="text">Announcement text</param>
        /// <param name="device">Target display: PlayerScreen or ConsoleScreen</param>
        public static void ShowAnnouncement(string text, MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            int displayDevice = GetDisplayDeviceIndex(device);
            MarqueeManager.Instance.ShowMarquee(
                text, 
                TextSettingsHandler.AnnouncementForeground,
                TextSettingsHandler.FontFamily, 
                TextSettingsHandler.Settings.MarqueeFontSize, 
                TextSettingsHandler.Settings.MarqueeRepeatCount, 
                MarqueePosition.Top, 
                TextSettingsHandler.Settings.MarqueeSpeed, 
                displayDevice
            );
        }

        /// <summary>
        /// Shows song information marquee with custom speed
        /// </summary>
        /// <param name="songName">Song name</param>
        /// <param name="artistName">Artist name</param>
        /// <param name="Speed">Animation speed</param>
        /// <param name="device">Target display: PlayerScreen or ConsoleScreen</param>
        public static void ShowSongInfo(string songName, string artistName, double Speed, MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            ShowSongInfo(songName, artistName, Speed, TextSettingsHandler.Settings.MarqueeFontSize, device);
        }

        /// <summary>
        /// Shows song information marquee with custom speed and font size
        /// </summary>
        /// <param name="songName">Song name</param>
        /// <param name="artistName">Artist name</param>
        /// <param name="Speed">Animation speed</param>
        /// <param name="FontSize">Font size</param>
        /// <param name="device">Target display: PlayerScreen or ConsoleScreen</param>
        public static void ShowSongInfo(string songName, string artistName, double Speed, double FontSize, MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            int displayDevice = GetDisplayDeviceIndex(device);
            var text = $"播放歌曲：{songName}     歌手：{artistName}";
            MarqueeManager.Instance.ShowMarquee(
                text, 
                TextSettingsHandler.MarqueeForeground,
                TextSettingsHandler.FontFamily, 
                FontSize, 
                TextSettingsHandler.Settings.MarqueeRepeatCount, 
                MarqueePosition.Top, 
                Speed, 
                displayDevice
            );
        }
        
        /// <summary>
        /// Shows a song information marquee with default settings
        /// </summary>
        /// <param name="songName">Song name</param>
        /// <param name="artistName">Artist name</param>
        /// <param name="device">Target display: PlayerScreen or ConsoleScreen</param>
        public static void ShowSongInfo(string songName, string artistName, MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            ShowSongInfo(songName, artistName, TextSettingsHandler.Settings.MarqueeSpeed, device);
        }


        /// <summary>
        /// Shows a welcome message marquee
        /// </summary>
        /// <param name="message">Welcome message</param>
        /// <param name="device">Target display: PlayerScreen or ConsoleScreen</param>
        public static void ShowWelcome(string message, MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            int displayDevice = GetDisplayDeviceIndex(device);
            MarqueeManager.Instance.ShowMarquee(
                message,
                TextSettingsHandler.StaticTextForeground,
                TextSettingsHandler.FontFamily,
                TextSettingsHandler.Settings.MarqueeFontSize,
                TextSettingsHandler.Settings.MarqueeRepeatCount,
                MarqueePosition.Top,
                TextSettingsHandler.Settings.MarqueeSpeed,
                displayDevice
            );
        }

        /// <summary>
        /// Shows an alert marquee with attention-grabbing styling
        /// </summary>
        /// <param name="alertText">Alert message</param>
        /// <param name="device">Target display: PlayerScreen or ConsoleScreen</param>
        public static void ShowAlert(string alertText, MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            int displayDevice = GetDisplayDeviceIndex(device);
            MarqueeManager.Instance.ShowMarquee(
                $"⚠ {alertText} ⚠", 
                Brushes.Red,
                new FontFamily("Microsoft JhengHei UI"), 
                TextSettingsHandler.Settings.MarqueeFontSize, 
                TextSettingsHandler.Settings.MarqueeRepeatCount, 
                MarqueePosition.Top, 
                TextSettingsHandler.Settings.MarqueeSpeed, 
                displayDevice
            );
        }

        /// <summary>
        /// Shows a custom marquee with full parameter control
        /// </summary>
        /// <param name="text">Text to display</param>
        /// <param name="color">Text color</param>
        /// <param name="fontFamily">Font family</param>
        /// <param name="fontSize">Font size</param>
        /// <param name="repeatCount">Number of repeats</param>
        /// <param name="position">Screen position</param>
        /// <param name="speed">Animation speed (pixels per second)</param>
        /// <param name="device">Target display: PlayerScreen or ConsoleScreen</param>
        public static void ShowCustom(string text, Brush color, FontFamily fontFamily, double fontSize,
            int repeatCount, MarqueePosition position, double speed, MarqueeDisplayDevice device)
        {
            int displayDevice = GetDisplayDeviceIndex(device);
            MarqueeManager.Instance.ShowMarquee(text, color, fontFamily, fontSize, repeatCount, position, speed, displayDevice);
        }

        /// <summary>
        /// Stops marquee on specified device
        /// </summary>
        /// <param name="device">Target display: PlayerScreen or ConsoleScreen</param>
        public static void Stop(MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            int displayDevice = GetDisplayDeviceIndex(device);
            MarqueeManager.Instance.StopMarquee(displayDevice);
        }

        /// <summary>
        /// Stops all active marquees
        /// </summary>
        public static void StopAll()
        {
            MarqueeManager.Instance.StopAllMarquees();
        }

        /// <summary>
        /// Checks if marquee is active on specified device
        /// </summary>
        /// <param name="device">Target display: PlayerScreen or ConsoleScreen</param>
        /// <returns>True if active, false otherwise</returns>
        public static bool IsActive(MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            int displayDevice = GetDisplayDeviceIndex(device);
            return MarqueeManager.Instance.IsMarqueeActive(displayDevice);
        }

        /// <summary>
        /// Shows static text with countdown timer (no scrolling animation)
        /// </summary>
        /// <param name="text">Text to display</param>
        /// <param name="timeoutSeconds">Timer duration in seconds (0 = no timeout, display indefinitely)</param>
        /// <param name="device">Target display: PlayerScreen or ConsoleScreen</param>
        public static void ShowStaticText(string text, int timeoutSeconds = 0, MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            int displayDevice = GetDisplayDeviceIndex(device);
            MarqueeManager.Instance.ShowStaticText(
                text,
                TextSettingsHandler.StaticTextForeground,
                TextSettingsHandler.FontFamily,
                TextSettingsHandler.Settings.NotificationFontSize,
                MarqueePosition.Top,
                timeoutSeconds,
                displayDevice
            );
        }

        /// <summary>
        /// Shows static announcement with countdown timer
        /// </summary>
        /// <param name="text">Announcement text</param>
        /// <param name="timeoutSeconds">Timer duration in seconds (0 = no timeout, display indefinitely)</param>
        /// <param name="device">Target display: PlayerScreen or ConsoleScreen</param>
        public static void ShowStaticAnnouncement(string text, int timeoutSeconds = 0, MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            int displayDevice = GetDisplayDeviceIndex(device);
            MarqueeManager.Instance.ShowStaticText(
                text,
                TextSettingsHandler.AnnouncementForeground,
                TextSettingsHandler.FontFamily,
                TextSettingsHandler.Settings.NotificationFontSize,
                MarqueePosition.Top,
                timeoutSeconds,
                displayDevice
            );
        }

        /// <summary>
        /// Shows custom static text with full parameter control and countdown timer
        /// </summary>
        /// <param name="text">Text to display</param>
        /// <param name="color">Text color</param>
        /// <param name="fontFamily">Font family</param>
        /// <param name="fontSize">Font size</param>
        /// <param name="position">Screen position</param>
        /// <param name="timeoutSeconds">Timer duration in seconds (0 = no timeout, display indefinitely)</param>
        /// <param name="device">Target display: PlayerScreen or ConsoleScreen</param>
        public static void ShowCustomStaticText(string text, Brush color, FontFamily fontFamily, double fontSize,
            MarqueePosition position, int timeoutSeconds, MarqueeDisplayDevice device)
        {
            int displayDevice = GetDisplayDeviceIndex(device);
            MarqueeManager.Instance.ShowStaticText(text, color, fontFamily, fontSize, position, timeoutSeconds, displayDevice);
        }

        /// <summary>
        /// Shows a corner marquee with text
        /// Ideal for local notifications like volume changes, status updates, etc.
        /// </summary>
        /// <param name="text">Text to display</param>
        /// <param name="timeoutSeconds">Duration in seconds (0 = indefinite)</param>
        /// <param name="position">Corner position (TopLeft, TopRight, BottomLeft, BottomRight)</param>
        /// <param name="fontSize">Font size (default: 32)</param>
        /// <param name="device">Target display: PlayerScreen or ConsoleScreen</param>
        public static void ShowCornerText(string text, int timeoutSeconds, MarqueePosition position, double fontSize = 32, MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            int displayDevice = GetDisplayDeviceIndex(device);
            MarqueeManager.Instance.ShowStaticText(
                text,
                TextSettingsHandler.StaticTextForeground,
                TextSettingsHandler.FontFamily,
                fontSize,
                position,
                timeoutSeconds,
                displayDevice
            );
        }

        /// <summary>
        /// Shows a corner notification in the top-right corner (common for status updates)
        /// </summary>
        /// <param name="text">Text to display (keep it short)</param>
        /// <param name="timeoutSeconds">Duration in seconds (default: 3)</param>
        /// <param name="device">Target display: PlayerScreen or ConsoleScreen</param>
        public static void ShowCornerNotification(string text, int timeoutSeconds = 3, MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            ShowCornerText(text, timeoutSeconds, MarqueePosition.TopRight, TextSettingsHandler.Settings.NotificationFontSize, device);
        }

        /// <summary>
        /// Shows volume level in bottom-right corner
        /// </summary>
        /// <param name="volumeLevel">Volume level (e.g., "75", "100")</param>
        /// <param name="device">Target display: PlayerScreen or ConsoleScreen</param>
        public static void ShowVolumeLevel(string volumeLevel, MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            ShowCornerText($"🔊{volumeLevel}", 2, MarqueePosition.BottomRight, TextSettingsHandler.Settings.NotificationFontSize, device);
        }

    }
}