using System;
using System.Collections.Generic;
using System.IO;

namespace UltimateKtv
{
    /// <summary>一首歌的歌詞從哪裡來；一次只顯示一種。</summary>
    public enum LyricsKind
    {
        None,
        /// <summary>同檔名的字幕檔 (.ass / .ssa / .srt)，由播放引擎渲染。</summary>
        SubtitleFile,
        /// <summary>影片內嵌的預設字幕軌，由播放引擎渲染。</summary>
        EmbeddedTrack,
        /// <summary>同檔名的歌詞檔 (.lrc)，由程式畫在電視疊加層。</summary>
        LrcFile,
    }

    /// <summary>
    /// 歌詞來源的規則：字幕檔 (.ass &gt; .ssa &gt; .srt) &gt; 內嵌字幕軌 &gt; .lrc。
    /// 只認與影片完全同名的檔案 (不認 歌名.zh-TW.srt 這類帶語言的檔名)；本機歌與 YouTube 快取影片相同。
    /// 有沒有內嵌字幕軌要等引擎開檔後才知道，所以分兩步：開檔前 <see cref="FindSubtitleFile"/> 決定交給引擎的字幕檔，
    /// 開檔後 <see cref="Choose"/> 決定要不要用 .lrc。
    /// </summary>
    public static class LyricsSource
    {
        /// <summary>字幕檔副檔名，依優先順序。</summary>
        public static readonly IReadOnlyList<string> SubtitleExtensions = new[] { ".ass", ".ssa", ".srt" };

        public const string LrcExtension = ".lrc";

        /// <summary>與影片同名的字幕檔 (依優先順序第一個存在的)；沒有時 null。副檔名大小寫由檔案系統決定 (Windows 不分)。</summary>
        public static string? FindSubtitleFile(string? videoPath, Func<string, bool> fileExists)
        {
            if (string.IsNullOrEmpty(videoPath)) return null;
            foreach (string ext in SubtitleExtensions)
            {
                string path = Path.ChangeExtension(videoPath, ext);
                if (fileExists(path)) return path;
            }
            return null;
        }

        /// <summary>與影片同名的 .lrc；沒有時 null。</summary>
        public static string? FindLrcFile(string? videoPath, Func<string, bool> fileExists)
        {
            if (string.IsNullOrEmpty(videoPath)) return null;
            string path = Path.ChangeExtension(videoPath, LrcExtension);
            return fileExists(path) ? path : null;
        }

        /// <summary>開檔後決定這首歌顯示哪一種歌詞。</summary>
        public static LyricsKind Choose(bool hasSubtitleFile, bool hasEmbeddedTrack, bool hasLrcFile)
        {
            if (hasSubtitleFile) return LyricsKind.SubtitleFile;
            if (hasEmbeddedTrack) return LyricsKind.EmbeddedTrack;
            if (hasLrcFile) return LyricsKind.LrcFile;
            return LyricsKind.None;
        }
    }
}
