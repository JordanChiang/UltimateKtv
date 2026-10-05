using System;
using System.Windows;
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
            if (device == MarqueeDisplayDevice.PlayerScreen)
            {
                // PlayerScreen uses the secondary display window (device 1)
                return 1;
            }
            else // ConsoleScreen
            {
                // ConsoleScreen uses the main window (device 0)
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
            var settings = SettingsManager.Instance.CurrentSettings;
            int displayDevice = GetDisplayDeviceIndex(device);
            MarqueeManager.Instance.ShowMarquee(
                text, 
                ParseBrush(settings.MarqueeTextFillColor, Brushes.White),
                ParseFontFamily(settings.MarqueeTextFontFamily), 
                settings.MarqueeTextFontSize, 
                settings.MarqueeTextPlayCount, 
                MarqueePosition.Top, 
                settings.MarqueeTextTimeDuration, 
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
            var settings = SettingsManager.Instance.CurrentSettings;
            int displayDevice = GetDisplayDeviceIndex(device);
            MarqueeManager.Instance.ShowMarquee(
                text, 
                TextSettingsHandler.AnnouncementForeground,
                ParseFontFamily(settings.MarqueeTextFontFamily), 
                settings.MarqueeTextFontSize, 
                settings.MarqueeTextPlayCount, 
                MarqueePosition.Top, 
                settings.MarqueeTextTimeDuration, 
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
            ShowSongInfo(songName, artistName, Speed, SettingsManager.Instance.CurrentSettings.MarqueeTextFontSize, device);
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
            var settings = SettingsManager.Instance.CurrentSettings;
            int displayDevice = GetDisplayDeviceIndex(device);
            var text = $"播放歌曲：{songName}     歌手：{artistName}";
            MarqueeManager.Instance.ShowMarquee(
                text, 
                ParseBrush(settings.MarqueeTextFillColor, Brushes.White),
                ParseFontFamily(settings.MarqueeTextFontFamily), 
                FontSize, 
                settings.MarqueeTextPlayCount, 
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
            ShowSongPlaybackMarquee(songName, artistName, null, null, false, null, null, device);
        }

        /// <summary>
        /// Shows a customizable song playback marquee based on AppSettings
        /// {0}=CurrentSong, {1}=CurrentSinger, {2}=NextSong, {3}=NextSinger, {4}=LAN IP:Port, {5}=WAN IP:Port, {6}=OrderedBy, {7}=NextOrderedBy
        /// </summary>
        public static void ShowSongPlaybackMarquee(string currentSong, string currentSinger, string? nextSong, string? nextSinger, bool isRandomSong = false, string? orderedBy = null, string? nextOrderedBy = null, MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            var settings = SettingsManager.Instance.CurrentSettings;
            int displayDevice = GetDisplayDeviceIndex(device);

            string formattedText;
            bool hasNextSong = !string.IsNullOrWhiteSpace(nextSong);

            string lanIpPort = $"{HttpServer.GetLocalIPAddress()}:{settings.HttpServerPort}";
            string wanIpPort = $"{HttpServer.GetPublicIPAddress()}:{settings.PublicServerPort}";
            string displayOrderedBy = (!string.IsNullOrWhiteSpace(orderedBy) && orderedBy != "本機" && orderedBy != "隨機播放" && orderedBy != "網路點歌") ? orderedBy : "";
            string displayNextOrderedBy = (!string.IsNullOrWhiteSpace(nextOrderedBy) && nextOrderedBy != "本機" && nextOrderedBy != "隨機播放" && nextOrderedBy != "網路點歌") ? nextOrderedBy : "";

            string template;
            if (isRandomSong && !hasNextSong)
            {
                // MarqueeTextString3: 播放隨機點播歌曲, 待播清單沒有歌
                template = string.IsNullOrEmpty(settings.MarqueeTextString3)
                    ? "隨機播放歌曲：「{1} - {0}」"
                    : settings.MarqueeTextString3;
            }
            else if (hasNextSong)
            {
                // MarqueeTextString1: 播放點播歌曲, 待播清單有歌 (或隨機播放但待播清單有歌)
                template = string.IsNullOrEmpty(settings.MarqueeTextString1)
                    ? "目前正在播放歌曲：「{1} - {0}」，下一首播放：「{3} - {2}」，請準備！！"
                    : settings.MarqueeTextString1;
            }
            else
            {
                // MarqueeTextString2: 播放點播歌曲, 待播清單沒有歌
                template = string.IsNullOrEmpty(settings.MarqueeTextString2)
                    ? "目前正在播放歌曲：「{1} - {0}」"
                    : settings.MarqueeTextString2;
            }

            try
            {
                formattedText = string.Format(template, currentSong ?? "", currentSinger ?? "", nextSong ?? "", nextSinger ?? "", lanIpPort, wanIpPort, displayOrderedBy, displayNextOrderedBy);
            }
            catch
            {
                formattedText = hasNextSong
                    ? $"目前正在播放歌曲：「{currentSinger} - {currentSong}」，下一首播放：「{nextSinger} - {nextSong}」，請準備！！"
                    : (isRandomSong ? $"隨機播放歌曲：「{currentSinger} - {currentSong}」" : $"目前正在播放歌曲：「{currentSinger} - {currentSong}」");
            }

            ShowCustomStyledMarquee(formattedText, settings, displayDevice, isSongPlayback: true, priority: MarqueePriority.Normal);
        }

        /// <summary>
        /// Shows startup welcome marquee on player screen based on MarqueeTextStartup
        /// </summary>
        public static void ShowStartupMarquee(MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            var settings = SettingsManager.Instance.CurrentSettings;
            if (string.IsNullOrWhiteSpace(settings.MarqueeTextStartup))
                return;

            int displayDevice = GetDisplayDeviceIndex(device);
            string lanIpPort = $"{HttpServer.GetLocalIPAddress()}:{settings.HttpServerPort}";
            string wanIpPort = $"{HttpServer.GetPublicIPAddress()}:{settings.PublicServerPort}";

            string formattedText;
            try
            {
                formattedText = string.Format(settings.MarqueeTextStartup, "", "", "", "", lanIpPort, wanIpPort);
            }
            catch
            {
                formattedText = settings.MarqueeTextStartup;
            }

            ShowCustomStyledMarquee(formattedText, settings, displayDevice, isSongPlayback: false, priority: MarqueePriority.Low);
        }

        private static void ShowCustomStyledMarquee(string formattedText, AppSettings settings, int displayDevice, bool isSongPlayback = false, MarqueePriority priority = MarqueePriority.Normal)
        {
            if (settings.MarqueeTextPlayCount <= 0)
            {
                // PlayCount is 0, meaning disabled
                return;
            }

            // Parse FontFamily
            FontFamily fontFamily;
            try
            {
                fontFamily = !string.IsNullOrWhiteSpace(settings.MarqueeTextFontFamily)
                    ? new FontFamily(settings.MarqueeTextFontFamily)
                    : new FontFamily("微軟正黑體");
            }
            catch
            {
                fontFamily = new FontFamily("微軟正黑體");
            }

            // Parse FontWeight
            FontWeight fontWeight = FontWeights.Bold;
            if (!string.IsNullOrWhiteSpace(settings.MarqueeTextFontWeight))
            {
                try
                {
                    var converter = new System.Windows.FontWeightConverter();
                    var converted = converter.ConvertFromString(settings.MarqueeTextFontWeight);
                    if (converted is FontWeight fw)
                    {
                        fontWeight = fw;
                    }
                }
                catch
                {
                    fontWeight = FontWeights.Bold;
                }
            }

            // Parse Fill Color
            Brush fillBrush = Brushes.White;
            if (!string.IsNullOrWhiteSpace(settings.MarqueeTextFillColor))
            {
                try
                {
                    var brush = (Brush?)new BrushConverter().ConvertFromString(settings.MarqueeTextFillColor);
                    if (brush != null)
                    {
                        if (brush.CanFreeze) brush.Freeze();
                        fillBrush = brush;
                    }
                }
                catch
                {
                    fillBrush = Brushes.White;
                }
            }

            // Parse Stroke Color
            Brush strokeBrush = Brushes.Black;
            if (!string.IsNullOrWhiteSpace(settings.MarqueeTextStrokeColor))
            {
                try
                {
                    var brush = (Brush?)new BrushConverter().ConvertFromString(settings.MarqueeTextStrokeColor);
                    if (brush != null)
                    {
                        if (brush.CanFreeze) brush.Freeze();
                        strokeBrush = brush;
                    }
                }
                catch
                {
                    strokeBrush = Brushes.Black;
                }
            }

            double fontSize = Math.Clamp(settings.MarqueeTextFontSize > 0 ? settings.MarqueeTextFontSize : 72, 12, 128);
            double strokeThickness = Math.Clamp(settings.MarqueeTextFontStrokeThickness >= 0 ? settings.MarqueeTextFontStrokeThickness : 8, 0, 32);
            double speed = Math.Clamp(settings.MarqueeTextTimeDuration > 0 ? settings.MarqueeTextTimeDuration : 200, 100, 1000);
            int playCount = Math.Clamp(settings.MarqueeTextPlayCount > 0 ? settings.MarqueeTextPlayCount : 2, 1, 10);
            int holdTimeMs = Math.Max(0, settings.MarqueeTextTimeHold);

            MarqueeManager.Instance.ShowMarquee(
                formattedText,
                fillBrush,
                fontFamily,
                fontSize,
                playCount,
                MarqueePosition.Top,
                speed,
                displayDevice,
                fontWeight,
                strokeBrush,
                strokeThickness,
                holdTimeMs,
                priority,
                isSongPlayback
            );
        }


        private static FontFamily ParseFontFamily(string? fontName)
        {
            if (string.IsNullOrWhiteSpace(fontName))
                return new FontFamily(SettingsManager.Instance.CurrentSettings.MarqueeTextFontFamily);
            try
            {
                return new FontFamily(fontName);
            }
            catch
            {
                return new FontFamily(SettingsManager.Instance.CurrentSettings.MarqueeTextFontFamily);
            }
        }

        private static Brush ParseBrush(string? hexColor, Brush defaultBrush)
        {
            if (string.IsNullOrWhiteSpace(hexColor))
                return defaultBrush;
            try
            {
                var brush = (Brush?)new BrushConverter().ConvertFromString(hexColor);
                if (brush != null)
                {
                    if (brush.CanFreeze) brush.Freeze();
                    return brush;
                }
            }
            catch { }
            return defaultBrush;
        }

        /// <summary>
        /// Shows song added notification on player screen using MarqueeSongAdded settings (Template, Font, Size, Color, Time)
        /// {0}=SongName, {1}=SingerName, {4}=LAN IP:Port, {5}=WAN IP:Port, {6}=OrderedBy
        /// </summary>
        public static void ShowSongAddedNotification(string songName, string singerName, string? orderedBy = null, MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            var settings = SettingsManager.Instance.CurrentSettings;
            if (settings.MarqueeSongAddedTimeDuration <= 0)
                return;

            int displayDevice = GetDisplayDeviceIndex(device);
            string lanIpPort = $"{HttpServer.GetLocalIPAddress()}:{settings.HttpServerPort}";
            string wanIpPort = $"{HttpServer.GetPublicIPAddress()}:{settings.PublicServerPort}";

            string template = string.IsNullOrEmpty(settings.MarqueeSongAddedString)
                ? "點播歌曲：「{1} - {0}」"
                : settings.MarqueeSongAddedString;

            // 若為網路點歌但無使用者名稱("網路點歌")，或為本機/隨機，則等同本機點歌(displayOrderedBy為空字串)
            string displayOrderedBy = (!string.IsNullOrWhiteSpace(orderedBy) && orderedBy != "本機" && orderedBy != "隨機播放" && orderedBy != "網路點歌") ? orderedBy : "";

            string formattedText;
            try
            {
                formattedText = string.Format(template, songName ?? "", singerName ?? "", "", "", lanIpPort, wanIpPort, displayOrderedBy);
            }
            catch
            {
                formattedText = $"點播歌曲：「{singerName} - {songName}」";
            }

            FontFamily fontFamily = ParseFontFamily(settings.MarqueeSongAddedFontFamily);
            Brush fillBrush = ParseBrush(settings.MarqueeSongAddedFillColor, Brushes.LightGreen);
            double fontSize = Math.Clamp(settings.MarqueeSongAddedFontSize > 0 ? settings.MarqueeSongAddedFontSize : 48, 12, 128);
            int timeout = Math.Clamp(settings.MarqueeSongAddedTimeDuration, 1, 60);

            MarqueeManager.Instance.ShowStaticText(
                formattedText,
                fillBrush,
                fontFamily,
                fontSize,
                MarqueePosition.Top,
                timeout,
                displayDevice,
                MarqueePriority.High
            );
        }

        /// <summary>
        /// Shows song added notification using a pre-formatted text string
        /// </summary>
        public static void ShowSongAddedNotification(string text, MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            var settings = SettingsManager.Instance.CurrentSettings;
            if (settings.MarqueeSongAddedTimeDuration <= 0)
                return;

            int displayDevice = GetDisplayDeviceIndex(device);
            FontFamily fontFamily = ParseFontFamily(settings.MarqueeSongAddedFontFamily);
            Brush fillBrush = ParseBrush(settings.MarqueeSongAddedFillColor, Brushes.LightGreen);
            double fontSize = Math.Clamp(settings.MarqueeSongAddedFontSize > 0 ? settings.MarqueeSongAddedFontSize : 48, 12, 128);
            int timeout = Math.Clamp(settings.MarqueeSongAddedTimeDuration, 1, 60);

            MarqueeManager.Instance.ShowStaticText(
                text,
                fillBrush,
                fontFamily,
                fontSize,
                MarqueePosition.Top,
                timeout,
                displayDevice,
                MarqueePriority.High
            );
        }

        /// <summary>
        /// Shows a welcome message marquee
        /// </summary>
        /// <param name="message">Welcome message</param>
        /// <param name="device">Target display: PlayerScreen or ConsoleScreen</param>
        public static void ShowWelcome(string message, MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            var settings = SettingsManager.Instance.CurrentSettings;
            int displayDevice = GetDisplayDeviceIndex(device);
            MarqueeManager.Instance.ShowMarquee(
                message,
                TextSettingsHandler.StaticTextForeground,
                ParseFontFamily(settings.MarqueeTextFontFamily),
                settings.MarqueeTextFontSize,
                settings.MarqueeTextPlayCount,
                MarqueePosition.Top,
                settings.MarqueeTextTimeDuration,
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
            var settings = SettingsManager.Instance.CurrentSettings;
            int displayDevice = GetDisplayDeviceIndex(device);
            MarqueeManager.Instance.ShowMarquee(
                $"⚠ {alertText} ⚠", 
                Brushes.Red,
                new FontFamily("Microsoft JhengHei UI"), 
                settings.MarqueeTextFontSize, 
                settings.MarqueeTextPlayCount, 
                MarqueePosition.Top, 
                settings.MarqueeTextTimeDuration, 
                displayDevice,
                FontWeights.Bold,
                Brushes.Black,
                4,
                500,
                MarqueePriority.High
            );
        }

        /// <summary>
        /// Shows a custom marquee with full parameter control
        /// </summary>
        public static void ShowCustom(string text, Brush color, FontFamily fontFamily, double fontSize,
            int repeatCount, MarqueePosition position, double speed, MarqueeDisplayDevice device)
        {
            int displayDevice = GetDisplayDeviceIndex(device);
            MarqueeManager.Instance.ShowMarquee(text, color, fontFamily, fontSize, repeatCount, position, speed, displayDevice);
        }

        /// <summary>
        /// Stops marquee on specified device
        /// </summary>
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
        public static bool IsActive(MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            int displayDevice = GetDisplayDeviceIndex(device);
            return MarqueeManager.Instance.IsMarqueeActive(displayDevice);
        }

        /// <summary>
        /// Checks if a song playback marquee is currently active on screen or pending resumption on specified device.
        /// </summary>
        public static bool IsSongPlaybackMarqueeActiveOrPending(MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            int displayDevice = GetDisplayDeviceIndex(device);
            return MarqueeManager.Instance.IsSongPlaybackMarqueeActiveOrPending(displayDevice);
        }

        /// <summary>
        /// Shows static text with countdown timer (no scrolling animation)
        /// </summary>
        public static void ShowStaticText(string text, int timeoutSeconds = 0, MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            var settings = SettingsManager.Instance.CurrentSettings;
            int displayDevice = GetDisplayDeviceIndex(device);
            MarqueeManager.Instance.ShowStaticText(
                text,
                TextSettingsHandler.StaticTextForeground,
                ParseFontFamily(settings.MarqueeBroadcastFontFamily),
                settings.MarqueeBroadcastFontSize,
                MarqueePosition.Top,
                timeoutSeconds,
                displayDevice
            );
        }

        /// <summary>
        /// Shows static announcement / broadcast with countdown timer using MarqueeBroadcast settings
        /// </summary>
        public static void ShowStaticAnnouncement(string text, int timeoutSeconds = 0, MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            var settings = SettingsManager.Instance.CurrentSettings;
            int duration = timeoutSeconds > 0 ? timeoutSeconds : settings.MarqueeBroadcastTimeDuration;
            if (duration <= 0)
                return;

            int displayDevice = GetDisplayDeviceIndex(device);
            FontFamily fontFamily = ParseFontFamily(settings.MarqueeBroadcastFontFamily);
            Brush fillBrush = ParseBrush(settings.MarqueeBroadcastFillColor, Brushes.Gold);
            double fontSize = Math.Clamp(settings.MarqueeBroadcastFontSize > 0 ? settings.MarqueeBroadcastFontSize : 38, 12, 128);

            MarqueeManager.Instance.ShowStaticText(
                text,
                fillBrush,
                fontFamily,
                fontSize,
                MarqueePosition.Top,
                duration,
                displayDevice,
                MarqueePriority.High
            );
        }

        /// <summary>
        /// Shows custom static text with full parameter control and countdown timer
        /// </summary>
        public static void ShowCustomStaticText(string text, Brush color, FontFamily fontFamily, double fontSize,
            MarqueePosition position, int timeoutSeconds, MarqueeDisplayDevice device)
        {
            int displayDevice = GetDisplayDeviceIndex(device);
            MarqueeManager.Instance.ShowStaticText(text, color, fontFamily, fontSize, position, timeoutSeconds, displayDevice);
        }

        /// <summary>
        /// Shows a corner marquee with text using MarqueeBroadcast settings
        /// </summary>
        public static void ShowCornerText(string text, int timeoutSeconds = 0, MarqueePosition position = MarqueePosition.BottomRight, double? fontSize = null, MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            var settings = SettingsManager.Instance.CurrentSettings;
            int duration = timeoutSeconds > 0 ? timeoutSeconds : settings.MarqueeBroadcastTimeDuration;
            if (duration <= 0)
                return;

            int displayDevice = GetDisplayDeviceIndex(device);
            FontFamily fontFamily = ParseFontFamily(settings.MarqueeBroadcastFontFamily);
            Brush fillBrush = ParseBrush(settings.MarqueeBroadcastFillColor, Brushes.Gold);
            double size = fontSize ?? Math.Clamp(settings.MarqueeBroadcastFontSize > 0 ? settings.MarqueeBroadcastFontSize : 38, 12, 128);

            MarqueeManager.Instance.ShowStaticText(
                text,
                fillBrush,
                fontFamily,
                size,
                position,
                duration,
                displayDevice
            );
        }

        /// <summary>
        /// Shows a corner notification in the top-right corner
        /// </summary>
        public static void ShowCornerNotification(string text, int timeoutSeconds = 0, MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            ShowCornerText(text, timeoutSeconds, MarqueePosition.TopRight, null, device);
        }

        /// <summary>
        /// Shows volume level in bottom-right corner
        /// </summary>
        public static void ShowVolumeLevel(string volumeLevel, MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            ShowCornerText($"🔊{volumeLevel}", 0, MarqueePosition.BottomRight, null, device);
        }

    }
}