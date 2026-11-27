using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace UltimateKtv
{
    /// <summary>
    /// YouTube 點唱紀錄 (YoutubeSongList) 的紀錄 Id 編號規則。欄位是 TEXT(5) (本程式與 SongMgr 建表都是)，所以一律 5 個字元：
    /// 第 1–9999 號是 Y0001–Y9999；之後是 YA000–YZZZZ (第 2 碼字母，後 3 碼 0-9A-Z 的 36 進位)，字串排序仍跟編號順序一致 ('9' &lt; 'A')。
    /// Access 的文字比對不分大小寫，所以解析也不分。不依賴 WPF，測試專案直接連結。
    /// </summary>
    public static class YoutubeRecordId
    {
        private const int DecimalCount = 9999;
        private const string Base36Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        private const int Base36Places = 3;
        private const int LetterBlockSize = 36 * 36 * 36;

        /// <summary>紀錄 Id 的總數 (Y0001–Y9999 加 YA000–YZZZZ)。</summary>
        public const int Count = DecimalCount + 26 * LetterBlockSize;

        /// <summary>第 number 號 (1 起) 的紀錄 Id；超出範圍回傳 null。</summary>
        public static string? FromNumber(int number)
        {
            if (number < 1 || number > Count) return null;
            if (number <= DecimalCount) return $"Y{number:D4}";

            // 在字母區段 (YA000 起) 中的序號
            int offset = number - DecimalCount - 1;
            var chars = new char[5];
            chars[0] = 'Y';
            chars[1] = (char)('A' + offset / LetterBlockSize);
            int rest = offset % LetterBlockSize;
            for (int i = 4; i >= 2; i--)
            {
                chars[i] = Base36Digits[rest % 36];
                rest /= 36;
            }
            return new string(chars);
        }

        /// <summary>紀錄 Id 是第幾號 (1 起)；不是這個編號格式時回傳 0。</summary>
        public static int NumberOf(string? id)
        {
            if (id == null || id.Length != 5) return 0;
            id = id.ToUpperInvariant();
            if (id[0] != 'Y') return 0;

            if (char.IsAsciiDigit(id[1]))
            {
                return int.TryParse(id.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n >= 1 ? n : 0;
            }
            if (id[1] < 'A' || id[1] > 'Z') return 0;

            int rest = 0;
            for (int i = 2; i < 2 + Base36Places; i++)
            {
                int digit = Base36Digits.IndexOf(id[i]);
                if (digit < 0) return 0;
                rest = rest * 36 + digit;
            }
            int offset = (id[1] - 'A') * LetterBlockSize + rest;
            return DecimalCount + 1 + offset;
        }

        /// <summary>是否為紀錄 Id (而不是影片 ID 等其他值)。</summary>
        public static bool IsValid(string? id) => NumberOf(id) > 0;

        /// <summary>新紀錄的 Id：現有 Id 之外最小的編號 (補上刪除留下的空號)；全部用完時回傳 null。</summary>
        public static string? Next(IEnumerable<string> existingIds)
        {
            var used = new HashSet<int>(existingIds.Select(NumberOf).Where(n => n > 0));
            for (int n = 1; n <= Count; n++)
            {
                if (!used.Contains(n)) return FromNumber(n);
            }
            return null;
        }
    }
}
