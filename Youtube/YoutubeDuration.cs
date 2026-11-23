using System;

namespace UltimateKtv
{
    /// <summary>
    /// YouTube 影片長度的顯示文字 (縮圖右上角、加入最愛視窗) 與秒數互轉。不依賴 WPF，測試專案直接連結。
    /// </summary>
    public static class YoutubeDuration
    {
        /// <summary>把秒數格式化為 H:MM:SS (不到 1 小時為 M:SS)；0 以下回傳空字串。</summary>
        public static string Format(double totalSeconds)
        {
            if (totalSeconds <= 0) return string.Empty;
            var d = TimeSpan.FromSeconds(totalSeconds);
            return d.TotalHours >= 1
                ? $"{(int)d.TotalHours}:{d.Minutes:D2}:{d.Seconds:D2}"
                : $"{d.Minutes}:{d.Seconds:D2}";
        }

        /// <summary>把 M:SS、H:MM:SS (或 hh:mm:ss) 轉回秒數；空白或無法解析時回傳 0。</summary>
        public static int ParseSeconds(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            int total = 0;
            foreach (var part in text.Trim().Split(':'))
            {
                if (!int.TryParse(part, out int n) || n < 0) return 0;
                total = total * 60 + n;
            }
            return total;
        }
    }
}
