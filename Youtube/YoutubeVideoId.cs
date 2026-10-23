using System;
using System.IO;
using System.Text.RegularExpressions;

namespace UltimateKtv
{
    /// <summary>
    /// YouTube 影片 ID 與下載檔名的規則。下載的檔案一律命名為「{歌名}_{videoId}.mp4」，
    /// 所以影片 ID 也能從檔名取回。不依賴 WPF，測試專案直接連結。
    /// </summary>
    public static class YoutubeVideoId
    {
        private static readonly Regex VideoIdRegex = new(@"^[A-Za-z0-9_-]{11}$", RegexOptions.Compiled);
        private static readonly Regex FileNameVideoIdRegex = new(@"_([A-Za-z0-9_-]{11})$", RegexOptions.Compiled);

        public static bool IsValid(string? id) => !string.IsNullOrEmpty(id) && VideoIdRegex.IsMatch(id);

        /// <summary>從「{歌名}_{videoId}.mp4」取回 11 碼影片 ID；沒有這個後綴時回傳空字串。</summary>
        public static string FromFileName(string? fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return "";
            var m = FileNameVideoIdRegex.Match(Path.GetFileNameWithoutExtension(fileName));
            return m.Success ? m.Groups[1].Value : "";
        }

        /// <summary>檔名中的歌名部分 (「{歌名}_{videoId}.mp4」→「{歌名}」)。</summary>
        public static string TitleFromFileName(string? fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return "";
            string nameOnly = Path.GetFileNameWithoutExtension(fileName);
            var m = FileNameVideoIdRegex.Match(nameOnly);
            return m.Success && m.Index > 0 ? nameOnly.Substring(0, m.Index) : nameOnly;
        }

        /// <summary>
        /// 一首 YouTube 歌真正的影片 ID，絕不是點唱紀錄的編號 (例如「Y0017」)。
        /// 依序看 youtubeId、songId (網頁點歌時是影片 ID)、檔名；都沒有時回傳空字串。
        /// </summary>
        public static string Resolve(string? youtubeId, string? songId, string? filePath = null)
        {
            if (IsValid(youtubeId)) return youtubeId!;
            if (IsValid(songId)) return songId!;
            return FromFileName(filePath);
        }

        /// <summary>下載快取中的檔名「{歌名}_{videoId}.mp4」(歌名中不能用在檔名的字元換成底線)。</summary>
        public static string CacheFileName(string title, string videoId) =>
            $"{string.Join("_", (title ?? "").Split(Path.GetInvalidFileNameChars()))}_{videoId}.mp4";
    }
}
