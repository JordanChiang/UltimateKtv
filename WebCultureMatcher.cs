using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace UltimateKtv
{
    /// <summary>
    /// 遠端點歌網頁依裝置語言 (瀏覽器的 Accept-Language) 選出要用的語言檔。
    /// 語言檔以檔名識別 (設定檔存的也是檔名)，比對時看的是語言檔的 "Language.Code" (BCP 47 語言代碼，例如 de、ja、pt-BR)，
    /// 所以檔名可以是 "Deutsch_German_德文" 這種自訂名稱。未填代碼的語言檔以檔名當代碼。
    /// 不依賴 WPF，可單元測試。
    /// </summary>
    public static class WebCultureMatcher
    {
        /// <summary>
        /// 解析 "Language.Code" 的值：可用逗號列出別名 (例如 "fil, tl")；空白時以檔名當代碼。
        /// </summary>
        public static IReadOnlyList<string> ParseCodes(string? codeValue, string culture)
        {
            var codes = (codeValue ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return codes.Length > 0 ? codes : new[] { culture };
        }

        /// <summary>
        /// 依 Accept-Language (例如 "zh-TW,zh;q=0.9,en-US;q=0.8") 選出語言檔；都對應不到時回傳 currentCulture (主程式目前的語言)。
        /// cultures 為可用的語言檔 (檔名 → 代碼)，依選單順序排列。
        /// 同一個代碼有多個語言檔時：主程式目前的語言優先，其次是檔名就是該代碼的 (例如 en.json)，再其次是排在前面的。
        /// </summary>
        public static string Resolve(string? acceptLanguage, IReadOnlyList<KeyValuePair<string, IReadOnlyList<string>>> cultures, string currentCulture)
        {
            var entries = cultures.SelectMany(c => c.Value.Select(code => (Culture: c.Key, Code: code))).ToList();

            foreach (var tag in ParseAcceptLanguage(acceptLanguage))
            {
                var match = MatchTag(tag, entries, currentCulture);
                if (match != null) return match;
            }
            return currentCulture;
        }

        /// <summary>Accept-Language 裡的語言標籤，依 q 值由高到低 (同分維持原順序)，略過 "*" 與 q=0。</summary>
        public static IEnumerable<string> ParseAcceptLanguage(string? acceptLanguage)
        {
            if (string.IsNullOrWhiteSpace(acceptLanguage)) return Array.Empty<string>();

            return acceptLanguage.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select((part, index) =>
                {
                    var pieces = part.Split(';', StringSplitOptions.TrimEntries);
                    double q = 1.0;
                    foreach (var p in pieces.Skip(1))
                    {
                        if (p.StartsWith("q=", StringComparison.OrdinalIgnoreCase) &&
                            double.TryParse(p[2..], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                            q = v;
                    }
                    return (Tag: pieces[0], Q: q, Index: index);
                })
                .Where(x => x.Tag.Length > 0 && x.Tag != "*" && x.Q > 0)
                .OrderByDescending(x => x.Q).ThenBy(x => x.Index)
                .Select(x => x.Tag)
                .ToList();
        }

        private static string? MatchTag(string tag, List<(string Culture, string Code)> entries, string currentCulture)
        {
            static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            static string Primary(string code) => code.Split('-')[0];

            // 1) 代碼完全相同
            var exact = Pick(entries, code => Same(code, tag), currentCulture);
            if (exact != null) return exact;

            // 2) 中文：繁體 (台灣/香港/澳門/Hant) → zh-TW，簡體 (中國/新加坡/Hans/只有 zh) → zh-CN
            string lower = tag.ToLowerInvariant();
            if (lower == "zh" || lower.StartsWith("zh-"))
            {
                bool traditional = lower.Contains("hant") || lower.EndsWith("-tw") || lower.EndsWith("-hk") || lower.EndsWith("-mo");
                var zh = Pick(entries, code => Same(code, traditional ? "zh-TW" : "zh-CN"), currentCulture);
                if (zh != null) return zh;
            }

            // 3) 主要語言相同 (en-US → en、ja-JP → ja，或 pt → pt-BR)
            string primary = Primary(tag);
            return Pick(entries, code => Same(code, primary), currentCulture)
                ?? Pick(entries, code => Same(Primary(code), primary), currentCulture);
        }

        /// <summary>符合條件的語言檔中選一個：主程式目前的語言 → 檔名等於代碼 → 排在前面的。</summary>
        private static string? Pick(List<(string Culture, string Code)> entries, Func<string, bool> codeMatches, string currentCulture)
        {
            var matches = entries.Where(e => codeMatches(e.Code)).ToList();
            if (matches.Count == 0) return null;

            return matches.FirstOrDefault(e => string.Equals(e.Culture, currentCulture, StringComparison.OrdinalIgnoreCase)).Culture
                ?? matches.FirstOrDefault(e => string.Equals(e.Culture, e.Code, StringComparison.OrdinalIgnoreCase)).Culture
                ?? matches[0].Culture;
        }
    }
}
