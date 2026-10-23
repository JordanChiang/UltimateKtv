using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using UltimateKtv.Enums;

namespace UltimateKtv
{
    /// <summary>
    /// 跑馬燈訊息：呼叫端只說「哪一種訊息、內容、顯示在哪」，樣式 (顏色、字型、大小、速度、次數、停留)
    /// 一律依設定在這裡解析成 <see cref="MarqueeItem"/> 交給 <see cref="MarqueeManager"/>。
    /// </summary>
    public static class MarqueeAPI
    {
        private const string DefaultFontFamily = "微軟正黑體";
        private static readonly AppSettings DefaultSettings = new();

        // ---------------- 捲動訊息 ----------------

        /// <summary>一般捲動文字 (MarqueeText 樣式)。</summary>
        public static void ShowText(string text, MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            var settings = SettingsManager.Instance.CurrentSettings;
            Show(new MarqueeItem
            {
                Text = text,
                Color = ParseBrush(settings.MarqueeTextFillColor, Brushes.White),
                FontFamily = ParseFontFamily(settings.MarqueeTextFontFamily),
                FontSize = settings.MarqueeTextFontSize,
                RepeatCount = settings.MarqueeTextPlayCount,
                Position = MarqueePosition.Top,
                Speed = settings.MarqueeTextTimeDuration,
                FontWeight = FontWeights.Normal,
                HoldTimeMs = 500
            }, device);
        }

        /// <summary>
        /// 播放中跑馬燈 (MarqueeTextString1~3 範本)。
        /// {0}=CurrentSong, {1}=CurrentSinger, {2}=NextSong, {3}=NextSinger, {4}=LAN IP:Port, {5}=WAN IP:Port, {6}=OrderedBy, {7}=NextOrderedBy
        /// </summary>
        public static void ShowSongPlaybackMarquee(string currentSong, string currentSinger, string? nextSong, string? nextSinger, bool isRandomSong = false, string? orderedBy = null, string? nextOrderedBy = null, MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            var settings = SettingsManager.Instance.CurrentSettings;

            string formattedText;
            bool hasNextSong = !string.IsNullOrWhiteSpace(nextSong);

            string lanIpPort = $"{HttpServer.GetLocalIPAddress()}:{HttpServer.CurrentPort}";
            string wanIpPort = $"{HttpServer.GetPublicIPAddress()}:{settings.PublicServerPort}";
            string displayOrderedBy = DisplayOrderedBy(orderedBy);
            string displayNextOrderedBy = DisplayOrderedBy(nextOrderedBy);

            string template;
            if (isRandomSong && !hasNextSong)
            {
                // MarqueeTextString3: 播放隨機點播歌曲, 待播清單沒有歌
                template = ResolveTemplate(settings.MarqueeTextString3, DefaultSettings.MarqueeTextString3, "Marquee.DefaultRandom", "Marquee.FallbackRandom");
            }
            else if (hasNextSong)
            {
                // MarqueeTextString1: 播放點播歌曲, 待播清單有歌 (或隨機播放但待播清單有歌)
                template = ResolveTemplate(settings.MarqueeTextString1, DefaultSettings.MarqueeTextString1, "Marquee.DefaultWithNext", "Marquee.FallbackWithNext");
            }
            else
            {
                // MarqueeTextString2: 播放點播歌曲, 待播清單沒有歌
                template = ResolveTemplate(settings.MarqueeTextString2, DefaultSettings.MarqueeTextString2, "Marquee.DefaultPlaying", "Marquee.FallbackPlaying");
            }

            try
            {
                formattedText = string.Format(template, currentSong ?? "", currentSinger ?? "", nextSong ?? "", nextSinger ?? "", lanIpPort, wanIpPort, displayOrderedBy, displayNextOrderedBy);
            }
            catch
            {
                formattedText = hasNextSong
                    ? string.Format(Loc.T("Marquee.FallbackWithNext"), currentSong, currentSinger, nextSong, nextSinger)
                    : string.Format(Loc.T(isRandomSong ? "Marquee.FallbackRandom" : "Marquee.FallbackPlaying"), currentSong, currentSinger);
            }

            ShowCustomStyledMarquee(formattedText, settings, device, isSongPlayback: true, priority: MarqueePriority.Normal);
        }

        /// <summary>開機歡迎跑馬燈 (MarqueeTextStartup 範本)。</summary>
        public static void ShowStartupMarquee(MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            var settings = SettingsManager.Instance.CurrentSettings;
            if (string.IsNullOrWhiteSpace(settings.MarqueeTextStartup))
                return;
            string startupTemplate = ResolveTemplate(settings.MarqueeTextStartup, DefaultSettings.MarqueeTextStartup, "Marquee.DefaultStartup", "Marquee.DefaultStartup");

            string lanIpPort = $"{HttpServer.GetLocalIPAddress()}:{HttpServer.CurrentPort}";
            string wanIpPort = $"{HttpServer.GetPublicIPAddress()}:{settings.PublicServerPort}";

            string formattedText;
            try
            {
                formattedText = string.Format(startupTemplate, "", "", "", "", lanIpPort, wanIpPort);
            }
            catch
            {
                formattedText = startupTemplate;
            }

            ShowCustomStyledMarquee(formattedText, settings, device, isSongPlayback: false, priority: MarqueePriority.Low);
        }

        // ---------------- 靜態訊息 ----------------

        /// <summary>
        /// 點歌成功通知 (MarqueeSongAdded 範本與樣式)。
        /// {0}=SongName, {1}=SingerName, {4}=LAN IP:Port, {5}=WAN IP:Port, {6}=OrderedBy
        /// </summary>
        public static void ShowSongAddedNotification(string songName, string singerName, string? orderedBy = null, MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            var settings = SettingsManager.Instance.CurrentSettings;
            if (settings.MarqueeSongAddedTimeDuration <= 0)
                return;

            string lanIpPort = $"{HttpServer.GetLocalIPAddress()}:{HttpServer.CurrentPort}";
            string wanIpPort = $"{HttpServer.GetPublicIPAddress()}:{settings.PublicServerPort}";

            string template = ResolveTemplate(settings.MarqueeSongAddedString, DefaultSettings.MarqueeSongAddedString, "Marquee.DefaultSongAdded", "Marquee.FallbackSongAdded");

            string formattedText;
            string fallbackTemplate = Loc.T("Marquee.FallbackSongAdded");
            if (string.IsNullOrWhiteSpace(singerName))
            {
                template = RemoveEmptyPlaceholder(template, 1);
                fallbackTemplate = RemoveEmptyPlaceholder(fallbackTemplate, 1);
            }

            try
            {
                formattedText = string.Format(template, songName ?? "", singerName ?? "", "", "", lanIpPort, wanIpPort, DisplayOrderedBy(orderedBy));
            }
            catch
            {
                formattedText = string.Format(fallbackTemplate, songName, singerName);
            }

            Show(Static(
                formattedText,
                ParseBrush(settings.MarqueeSongAddedFillColor, Brushes.LightGreen),
                ParseFontFamily(settings.MarqueeSongAddedFontFamily),
                Math.Clamp(settings.MarqueeSongAddedFontSize > 0 ? settings.MarqueeSongAddedFontSize : 48, 12, 128),
                MarqueePosition.Top,
                Math.Clamp(settings.MarqueeSongAddedTimeDuration, 1, 60),
                MarqueePriority.High), device);
        }

        /// <summary>
        /// 公告/廣播 (MarqueeBroadcast 樣式)，timeoutSeconds 為 0 時用設定的停留秒數。
        /// 設定的顯示時間為 0 代表關閉系統廣播，有指定 timeoutSeconds 也不顯示。
        /// </summary>
        public static void ShowStaticAnnouncement(string text, int timeoutSeconds = 0, MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            var settings = SettingsManager.Instance.CurrentSettings;
            if (settings.MarqueeBroadcastTimeDuration <= 0)
                return;
            int duration = timeoutSeconds > 0 ? timeoutSeconds : settings.MarqueeBroadcastTimeDuration;

            Show(Static(text, BroadcastBrush(settings), ParseFontFamily(settings.MarqueeBroadcastFontFamily),
                BroadcastFontSize(settings), MarqueePosition.Top, duration, MarqueePriority.High), device);
        }

        /// <summary>
        /// 角落提示 (MarqueeBroadcast 樣式)；同一角落的新提示會原地取代舊的。
        /// 設定的顯示時間為 0 代表關閉系統廣播，有指定 timeoutSeconds 也不顯示。
        /// </summary>
        public static void ShowCornerText(string text, int timeoutSeconds = 0, MarqueePosition position = MarqueePosition.BottomRight, double? fontSize = null, MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            var settings = SettingsManager.Instance.CurrentSettings;
            if (settings.MarqueeBroadcastTimeDuration <= 0)
                return;
            int duration = timeoutSeconds > 0 ? timeoutSeconds : settings.MarqueeBroadcastTimeDuration;

            Show(Static(text, BroadcastBrush(settings), ParseFontFamily(settings.MarqueeBroadcastFontFamily),
                fontSize ?? BroadcastFontSize(settings), position, duration), device);
        }

        /// <summary>右下角音量。</summary>
        public static void ShowVolumeLevel(string volumeLevel, MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            ShowCornerText($"🔊{volumeLevel}", 0, MarqueePosition.BottomRight, null, device);
        }

        /// <summary>持續顯示的系統提示 (例如隨機播放狀態)，直到被其他訊息取代或停止。</summary>
        public static void ShowNotice(string text, MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            var settings = SettingsManager.Instance.CurrentSettings;
            Show(Static(text, ThemeSettingsHandler.StaticTextForeground,
                ParseFontFamily(settings.MarqueeBroadcastFontFamily, DefaultFontFamily),
                settings.MarqueeBroadcastFontSize, MarqueePosition.Top, 0), device);
        }

        /// <summary>
        /// 網路點歌主機資訊，持續顯示到第一次點歌。displayDevice 是實際的視窗編號 (0 = 主視窗, 1 = 電視視窗)。
        /// </summary>
        public static void ShowWebHostInfo(string text, int displayDevice)
        {
            var item = Static(text, ThemeSettingsHandler.WebHostInfoForeground, ThemeSettingsHandler.FontFamily,
                ThemeSettingsHandler.Settings.WebHostInfoFontSize, MarqueePosition.Top, 0);
            item.DisplayDevice = displayDevice;
            MarqueeManager.Instance.Show(item);
        }

        // ---------------- 停止 / 查詢 ----------------

        public static void Stop(MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            MarqueeManager.Instance.StopMarquee(GetDisplayDeviceIndex(device));
        }

        public static void StopAll()
        {
            MarqueeManager.Instance.StopAllMarquees();
        }

        /// <summary>播放中跑馬燈正在顯示，或被其他訊息暫時蓋過、稍後會恢復。</summary>
        public static bool IsSongPlaybackMarqueeActiveOrPending(MarqueeDisplayDevice device = MarqueeDisplayDevice.PlayerScreen)
        {
            return MarqueeManager.Instance.IsSongPlaybackMarqueeActiveOrPending(GetDisplayDeviceIndex(device));
        }

        // ---------------- 樣式解析 ----------------

        /// <summary>PlayerScreen = 電視視窗 (1)，ConsoleScreen = 主視窗 (0)。</summary>
        private static int GetDisplayDeviceIndex(MarqueeDisplayDevice device) =>
            device == MarqueeDisplayDevice.PlayerScreen ? 1 : 0;

        private static void Show(MarqueeItem item, MarqueeDisplayDevice device)
        {
            item.DisplayDevice = GetDisplayDeviceIndex(device);
            MarqueeManager.Instance.Show(item);
        }

        /// <summary>靜態文字；沒指定優先權時，有倒數的提示優先於播放中跑馬燈。</summary>
        private static MarqueeItem Static(string text, Brush color, FontFamily fontFamily, double fontSize,
            MarqueePosition position, int timeoutSeconds, MarqueePriority? priority = null) => new()
        {
            Text = text,
            Color = color,
            FontFamily = fontFamily,
            FontSize = fontSize,
            Position = position,
            TimeoutSeconds = timeoutSeconds,
            IsStatic = true,
            Priority = priority ?? (timeoutSeconds > 0 ? MarqueePriority.High : MarqueePriority.Normal)
        };

        /// <summary>MarqueeText 樣式 (含外框、粗細、停留) 的捲動跑馬燈；播放次數為 0 表示關閉。</summary>
        private static void ShowCustomStyledMarquee(string formattedText, AppSettings settings, MarqueeDisplayDevice device, bool isSongPlayback, MarqueePriority priority)
        {
            if (settings.MarqueeTextPlayCount <= 0)
            {
                // PlayCount is 0, meaning disabled
                return;
            }

            Show(new MarqueeItem
            {
                Text = formattedText,
                Color = ParseBrush(settings.MarqueeTextFillColor, Brushes.White),
                FontFamily = ParseFontFamily(settings.MarqueeTextFontFamily, DefaultFontFamily),
                FontWeight = ParseFontWeight(settings.MarqueeTextFontWeight, FontWeights.Bold),
                FontSize = Math.Clamp(settings.MarqueeTextFontSize > 0 ? settings.MarqueeTextFontSize : 72, 12, 128),
                StrokeColor = ParseBrush(settings.MarqueeTextStrokeColor, Brushes.Black),
                StrokeThickness = Math.Clamp(settings.MarqueeTextFontStrokeThickness >= 0 ? settings.MarqueeTextFontStrokeThickness : 8, 0, 32),
                Speed = Math.Clamp(settings.MarqueeTextTimeDuration > 0 ? settings.MarqueeTextTimeDuration : 200, 100, 1000),
                RepeatCount = Math.Clamp(settings.MarqueeTextPlayCount > 0 ? settings.MarqueeTextPlayCount : 2, 1, 10),
                HoldTimeMs = Math.Max(0, settings.MarqueeTextTimeHold),
                Position = MarqueePosition.Top,
                Priority = priority,
                IsSongPlayback = isSongPlayback
            }, device);
        }

        private static Brush BroadcastBrush(AppSettings settings) => ParseBrush(settings.MarqueeBroadcastFillColor, Brushes.Gold);

        private static double BroadcastFontSize(AppSettings settings) =>
            Math.Clamp(settings.MarqueeBroadcastFontSize > 0 ? settings.MarqueeBroadcastFontSize : 38, 12, 128);

        /// <summary>網路點歌沒有名稱 ("網路點歌")、本機、隨機播放都不顯示點歌者。</summary>
        private static string DisplayOrderedBy(string? orderedBy) =>
            !string.IsNullOrWhiteSpace(orderedBy) && orderedBy != "本機" && orderedBy != Loc.OrderedByRandom && orderedBy != Loc.OrderedByWeb
                ? orderedBy
                : "";

        /// <summary>
        /// 跑馬燈範本：使用者沒有自訂 (空白或仍是預設值) 時跟著介面語言，自訂過的原樣使用。
        /// </summary>
        private static string ResolveTemplate(string? configured, string defaultValue, string defaultKey, string emptyKey)
        {
            if (string.IsNullOrEmpty(configured)) return Loc.T(emptyKey);
            return configured == defaultValue ? Loc.T(defaultKey) : configured;
        }

        /// <summary>
        /// 欄位值為空 (如 YT 歌曲沒有歌手) 時，把範本中的 {index} 連同相鄰的分隔符號 (- / | 等) 一起拿掉，
        /// 避免出現「點播歌曲： - 歌名」。
        /// </summary>
        private static string RemoveEmptyPlaceholder(string template, int index)
        {
            string ph = Regex.Escape("{" + index + "}");
            const string sep = @"\s*[-–—/|·]\s*";
            template = Regex.Replace(template, ph + sep, "");
            template = Regex.Replace(template, sep + ph, "");
            return Regex.Replace(template, ph, "");
        }

        /// <param name="fallback">字型名稱空白或無效時使用；null 表示用 MarqueeText 的字型。</param>
        private static FontFamily ParseFontFamily(string? fontName, string? fallback = null)
        {
            fallback ??= SettingsManager.Instance.CurrentSettings.MarqueeTextFontFamily;
            if (string.IsNullOrWhiteSpace(fontName))
                return new FontFamily(fallback);
            try
            {
                return new FontFamily(fontName);
            }
            catch
            {
                return new FontFamily(fallback);
            }
        }

        private static FontWeight ParseFontWeight(string? weight, FontWeight defaultWeight)
        {
            if (string.IsNullOrWhiteSpace(weight)) return defaultWeight;
            try
            {
                if (new FontWeightConverter().ConvertFromString(weight) is FontWeight fw) return fw;
            }
            catch { }
            return defaultWeight;
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
    }
}
