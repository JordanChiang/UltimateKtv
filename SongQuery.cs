using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;

namespace UltimateKtv
{
    /// <summary>歌曲查詢需要的外部條件；由呼叫端提供，查詢本身不讀設定或系統時間。</summary>
    public sealed record SongQueryOptions(int SortMethod, DateTime Today, int NewSongDays)
    {
        public static SongQueryOptions FromSettings(AppSettings settings) =>
            new(settings.SongSortMethod, DateTime.Today, settings.NewSongDays);
    }

    /// <summary>清單的一頁；Number 從 1 開始，超過最後一頁時 Items 為空。</summary>
    public sealed record Page<T>(IReadOnlyList<T> Items, int Number, int Size, int TotalCount)
    {
        public int TotalPages => Size <= 0 || TotalCount == 0 ? 1 : (TotalCount + Size - 1) / Size;

        /// <summary>取第 number 頁 (小於 1 視為 1)；size 小於 1 = 不分頁，整份清單為一頁。</summary>
        public static Page<T> Of(IReadOnlyList<T> source, int number, int size)
        {
            if (number < 1) number = 1;
            if (size < 1) return new Page<T>(source, 1, size, source.Count);
            return new Page<T>(source.Skip((number - 1) * size).Take(size).ToList(), number, size, source.Count);
        }
    }

    /// <summary>快速搜尋比對的歌曲欄位。</summary>
    public enum SongTextField { Spell, PenStyle, Id, Name }

    /// <summary>
    /// 歌庫的歌曲查詢：新歌、排行、語系篩選、快速搜尋、搜尋框、歌星的歌，以及歌曲排序。
    /// 主畫面、隨機播放與手機點歌共用同一份規則。歌庫尚未載入 (null) 時回傳空清單。
    /// </summary>
    public static class SongQuery
    {
        private const string Mandarin = "國語";
        private const string Taiwanese = "台語";
        private const string OtherLang = "其它";
        private const int DuetSingerType = 3;

        /// <summary>語系頁的「其它」= 不在這些語系之內；新歌與排行的「其它」= 不是國語也不是台語。</summary>
        private static readonly HashSet<string> StandardLangs = new() { "國語", "台語", "英語", "日語", "粵語", "兒歌" };

        private static readonly char[] SingerDelimiters = { '&', '/', ',', '、', '；', ';', '|' };

        /// <summary>歌名排列順序：英文 (ASCII 開頭) 在前，其餘依筆畫。</summary>
        public static IComparer<string> NameOrder { get; } = new NameComparer();

        /// <summary>依使用者設定的歌曲排序排列。</summary>
        public static List<Song> Sort(IEnumerable<Song>? songs, SongQueryOptions options)
        {
            if (songs == null) return new List<Song>();
            var ordered = options.SortMethod switch
            {
                1 => songs.OrderByDescending(s => s.PlayCount),
                2 => songs.OrderByDescending(s => s.CreatDate ?? DateTime.MinValue),
                _ => songs.OrderBy(s => s.WordCount),
            };
            return ordered.ThenBy(s => s.Name, NameOrder).ToList();
        }

        /// <summary>新歌；lang 為空 = 全部語系，「其它」= 國語、台語以外。</summary>
        public static List<Song> NewSongs(IEnumerable<Song>? songs, string? lang, SongQueryOptions options)
        {
            if (songs == null) return new List<Song>();
            var cutoff = options.Today.AddDays(-options.NewSongDays);
            return Sort(songs.Where(s => s.CreatDate is DateTime created && created >= cutoff && MatchesLang(s, lang)), options);
        }

        /// <summary>
        /// 排行：唱過的歌依播放次數由多到少。filter 為按鈕的寫法，例如「國語-單曲」「台語-合唱」「其它」；
        /// 只寫語系或空白時單曲與合唱都含。
        /// </summary>
        public static List<Song> Ranking(IEnumerable<Song>? songs, string? filter)
        {
            if (songs == null) return new List<Song>();
            var parts = (filter ?? "").Split('-');
            string lang = parts[0];
            string type = parts.Length > 1 ? parts[1] : "";

            return songs
                .Where(s => s.PlayCount > 0 && MatchesLang(s, lang) && type switch
                {
                    "合唱" => IsDuet(s),
                    "單曲" => !IsDuet(s),
                    _ => true,
                })
                .OrderByDescending(s => s.PlayCount)
                .ToList();
        }

        /// <summary>
        /// 語系頁的組合篩選。langs 空 = 全部語系；duetOnly 決定只看合唱或只看非合唱；
        /// singerType 為歌手類別 ("99" = 男、女、團體、合唱以外)；wordCount 為「3」或「5-7」。
        /// </summary>
        public static List<Song> ByLanguage(IEnumerable<Song>? songs, IReadOnlyCollection<string> langs, bool duetOnly,
            string? singerType, string? wordCount, SongQueryOptions options)
        {
            if (songs == null) return new List<Song>();
            bool includeOther = langs.Contains(OtherLang);
            var (minWords, maxWords) = ParseWordCount(wordCount);

            return Sort(songs.Where(s =>
            {
                if (langs.Count > 0 && !langs.Contains(s.Lang) && (!includeOther || StandardLangs.Contains(s.Lang)))
                    return false;

                if (IsDuet(s) != duetOnly) return false;

                if (!string.IsNullOrEmpty(singerType))
                {
                    if (s.SingerType == -1)
                    {
                        if (singerType != "99") return false;
                    }
                    else if (singerType == "99")
                    {
                        if (s.SingerType is 0 or 1 or 2 or 3) return false;
                    }
                    else if (int.TryParse(singerType, out int target) && s.SingerType != target)
                    {
                        return false;
                    }
                }

                return s.WordCount >= minWords && s.WordCount <= maxWords;
            }), options);
        }

        /// <summary>快速搜尋：欄位以 key 開頭 (contains 時為包含)，不分大小寫。</summary>
        public static List<Song> QuickMatch(IEnumerable<Song>? songs, SongTextField field, string key, bool contains, SongQueryOptions options)
        {
            if (songs == null || string.IsNullOrEmpty(key)) return new List<Song>();
            Func<Song, string> getField = field switch
            {
                SongTextField.Spell => s => s.Spell,
                SongTextField.PenStyle => s => s.PenStyle,
                SongTextField.Id => s => s.Id,
                _ => s => s.Name,
            };

            return Sort(songs.Where(s =>
            {
                var text = getField(s);
                if (string.IsNullOrEmpty(text)) return false;
                return contains
                    ? text.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0
                    : text.StartsWith(key, StringComparison.OrdinalIgnoreCase);
            }), options);
        }

        /// <summary>
        /// 歌星的歌 (使用者點了某位歌星)：精確比對歌星名，合唱歌的任一位歌星相符即可；
        /// singer 本身是合唱名 (例如「甲&amp;乙」) 時，全部歌星都在才算，否則退回比對第一位。
        /// </summary>
        public static List<Song> BySinger(IEnumerable<Song>? songs, string singer, SongQueryOptions options)
        {
            if (songs == null || string.IsNullOrEmpty(singer)) return new List<Song>();
            var primary = singer.Split(SingerDelimiters, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? singer;

            return Sort(songs.Where(s => !string.IsNullOrEmpty(s.Name)
                && (ContainsSinger(s.Singer, singer) || (!string.IsNullOrEmpty(primary) && ContainsSinger(s.Singer, primary)))), options);
        }

        /// <summary>
        /// 搜尋框：歌名或歌星包含 text 就算 (不分大小寫、全形半形、平假名片假名，不限字數)；
        /// text 空白 = 該語系全部，lang 空白 = 全部語系。
        /// </summary>
        public static List<Song> Search(IEnumerable<Song>? songs, string? text, string? lang, SongQueryOptions options)
        {
            if (songs == null) return new List<Song>();
            bool Contains(string field) => SearchCompare.IndexOf(field, text!, SearchOptions) >= 0;
            return Sort(songs.Where(s => MatchesLang(s, lang) && (string.IsNullOrEmpty(text) || Contains(s.Name) || Contains(s.Singer))), options);
        }

        private static readonly CompareInfo SearchCompare = CultureInfo.GetCultureInfo("zh-TW").CompareInfo;
        private const CompareOptions SearchOptions = CompareOptions.IgnoreCase | CompareOptions.IgnoreWidth | CompareOptions.IgnoreKanaType;

        /// <summary>手機歌星清單「其它」以外的歌手類別：男、女、團體、外國男、外國女、外國團。</summary>
        private static readonly HashSet<string> NamedSingerTypes = new() { "0", "1", "2", "4", "5", "6" };

        /// <summary>
        /// 手機歌星清單的篩選：type 為歌手類別 (空白 = 全部，"99" = 上述六類以外，含未設定)；
        /// name 為歌星名包含的文字 (不分大小寫、全形半形、平假名片假名)，空白 = 不限。
        /// </summary>
        public static bool MatchesSinger(string singerName, string? singerType, string? type, string? name)
        {
            string actualType = singerType ?? "";
            if (type == "99")
            {
                if (NamedSingerTypes.Contains(actualType)) return false;
            }
            else if (!string.IsNullOrEmpty(type) && actualType != type)
            {
                return false;
            }
            return string.IsNullOrEmpty(name) || SearchCompare.IndexOf(singerName ?? "", name, SearchOptions) >= 0;
        }

        private static bool IsDuet(Song song) => song.SingerType == DuetSingerType;

        private static bool MatchesLang(Song song, string? lang)
        {
            if (string.IsNullOrEmpty(lang)) return true;
            if (lang == OtherLang) return song.Lang != Mandarin && song.Lang != Taiwanese;
            return song.Lang == lang;
        }

        /// <summary>「5」→ 5~5，「3-7」→ 3~7；空白或無法解析 = 不限字數。</summary>
        private static (int Min, int Max) ParseWordCount(string? wordCount)
        {
            if (string.IsNullOrEmpty(wordCount)) return (int.MinValue, int.MaxValue);
            var parts = wordCount.Split('-');
            if (parts.Length == 2 && int.TryParse(parts[0], out int min) && int.TryParse(parts[1], out int max)) return (min, max);
            if (parts.Length == 1 && int.TryParse(parts[0], out int exact)) return (exact, exact);
            return (int.MinValue, int.MaxValue);
        }

        /// <summary>targetSinger 的每一位歌星 (可用 &amp; / , 等分隔) 都出現在 songSinger 中才算相符，不分大小寫。</summary>
        private static bool ContainsSinger(string songSinger, string targetSinger)
        {
            if (string.IsNullOrEmpty(songSinger) || string.IsNullOrEmpty(targetSinger)) return false;

            var songSingers = new HashSet<string>(
                songSinger.Split(SingerDelimiters, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()),
                StringComparer.OrdinalIgnoreCase);
            if (songSingers.Count == 0) return false;

            var targetSingers = targetSinger.Split(SingerDelimiters, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
            return targetSingers.Count > 0 && targetSingers.All(songSingers.Contains);
        }

        private sealed class NameComparer : IComparer<string>
        {
            public int Compare(string? x, string? y)
            {
                int byScript = NonAsciiFirst(x).CompareTo(NonAsciiFirst(y));
                return byScript != 0 ? byScript : NativeStrokeComparer.Instance.Compare(x, y);
            }

            private static int NonAsciiFirst(string? name) => string.IsNullOrEmpty(name) || name[0] > 127 ? 1 : 0;
        }

        /// <summary>
        /// 以 Windows NLS 的筆畫排序比較字串，可正確處理 Big5 沒有定義的簡體字與日文漢字。
        /// </summary>
        private sealed class NativeStrokeComparer : IComparer<string>
        {
            public static readonly NativeStrokeComparer Instance = new();

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
            private static extern int CompareStringEx(
                string lpLocaleName,
                uint dwCmpFlags,
                string lpString1,
                int cchCount1,
                string lpString2,
                int cchCount2,
                IntPtr lpVersionInformation,
                IntPtr lpReserved,
                IntPtr lParam);

            public int Compare(string? x, string? y)
            {
                if (x == y) return 0;
                if (x == null) return -1;
                if (y == null) return 1;

                int result = CompareStringEx("zh-TW_stroke", 0, x, x.Length, y, y.Length, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                return result == 0 ? string.Compare(x, y, StringComparison.Ordinal) : result - 2;
            }
        }
    }
}
