using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows;

namespace UltimateKtv
{
    /// <summary>
    /// 介面多國語言。語言檔放在 Languages\{culture}.json (平面 key → 字串)。
    /// 載入後每個 key 以 "Str.{key}" 寫入 Application 資源，XAML 用 {DynamicResource Str.xxx} 參照，切換語言不需重開。
    /// zh-TW 為基準語言，其他語言缺少的 key 一律退回 zh-TW；zh-TW.json 也內嵌在程式裡，外部檔案遺失時使用內嵌版本。
    /// 遠端點歌網頁 ("Web." 開頭的 key) 依各裝置的語言 (Accept-Language) 回應，見 WebCulture。
    /// </summary>
    public static class Loc
    {
        public const string DefaultCulture = "zh-TW";
        public const string ResourcePrefix = "Str.";

        /// <summary>語言檔裡放該語言自己名稱的 key (顯示在設定的語言選單)，不會退回 zh-TW。</summary>
        public const string LanguageNameKey = "Language.Name";

        private static IReadOnlyList<KeyValuePair<string, string>>? _supportedCultures;

        /// <summary>
        /// 可用的介面語言 (culture → 該語言自己的名稱)，來自 Languages 目錄下的 *.json，
        /// 使用者自行新增的語言檔也會列出。zh-TW 固定排第一。
        /// </summary>
        public static IReadOnlyList<KeyValuePair<string, string>> SupportedCultures => _supportedCultures ??= ScanCultures();

        /// <summary>重新掃描 Languages 目錄 (開啟設定視窗時呼叫，新增的語言檔不用重開程式即可選擇)。</summary>
        public static IReadOnlyList<KeyValuePair<string, string>> RefreshCultures()
        {
            _cultureCache.Clear(); // 語言檔可能被修改，網頁用的快取一併清除
            return _supportedCultures = ScanCultures();
        }

        private static IReadOnlyList<KeyValuePair<string, string>> ScanCultures()
        {
            var list = new List<KeyValuePair<string, string>>();
            try
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Languages");
                if (Directory.Exists(dir))
                {
                    foreach (var file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                    {
                        string culture = Path.GetFileNameWithoutExtension(file);
                        string name = LoadFile(culture).TryGetValue(LanguageNameKey, out var n) && !string.IsNullOrWhiteSpace(n) ? n : culture;
                        list.Add(new KeyValuePair<string, string>(culture, name));
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Failed to scan language files", ex);
            }

            // 基準語言一定存在且排第一
            list.RemoveAll(c => string.Equals(c.Key, DefaultCulture, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, new KeyValuePair<string, string>(DefaultCulture, "繁體中文"));
            return list;
        }

        private static Dictionary<string, string> _strings = new();
        private static ResourceDictionary? _dictionary;

        public static string CurrentCulture { get; private set; } = DefaultCulture;

        /// <summary>切換語言時觸發，給程式碼產生的文字重新整理用。</summary>
        public static event EventHandler? LanguageChanged;

        public static bool IsSupported(string? culture) =>
            culture != null && SupportedCultures.Any(c => string.Equals(c.Key, culture, StringComparison.OrdinalIgnoreCase));

        public static string Normalize(string? culture) =>
            SupportedCultures.FirstOrDefault(c => string.Equals(c.Key, culture, StringComparison.OrdinalIgnoreCase)).Key ?? DefaultCulture;

        /// <summary>載入指定語言並更新 Application 資源。</summary>
        public static void Apply(string? culture)
        {
            culture = Normalize(culture);

            var strings = LoadMerged(culture);
            _strings = strings;
            _cultureCache[culture] = strings;
            CurrentCulture = culture;

            var app = Application.Current;
            if (app != null)
            {
                var dict = new ResourceDictionary();
                foreach (var kv in strings)
                {
                    dict[ResourcePrefix + kv.Key] = kv.Value;
                }

                // 整份替換，DynamicResource 會跟著更新
                if (_dictionary != null) app.Resources.MergedDictionaries.Remove(_dictionary);
                app.Resources.MergedDictionaries.Add(dict);
                _dictionary = dict;
            }

            AppLogger.Log($"UI language applied: {culture} ({strings.Count} strings)");
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }

        // ===== 遠端點歌網頁：依裝置語言 =====

        public const string WebKeyPrefix = "Web.";

        private static readonly AsyncLocal<string?> _webCulture = new();
        private static readonly ConcurrentDictionary<string, Dictionary<string, string>> _cultureCache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 目前處理中的網頁請求所用的語言 (由 HttpServer 依 Accept-Language 設定，只在該請求的非同步流程內有效)。
        /// 只影響 "Web." 開頭的 key；跑馬燈等主程式文字仍用主程式的介面語言。
        /// </summary>
        public static string? WebCulture
        {
            get => _webCulture.Value;
            set => _webCulture.Value = value;
        }

        /// <summary>
        /// 依瀏覽器的 Accept-Language (例如 "zh-TW,zh;q=0.9,en-US;q=0.8") 選出可用的語言；都對應不到時用主程式目前的語言。
        /// </summary>
        public static string ResolveWebCulture(string? acceptLanguage)
        {
            if (string.IsNullOrWhiteSpace(acceptLanguage)) return CurrentCulture;

            var requested = acceptLanguage.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select((part, index) =>
                {
                    var pieces = part.Split(';', StringSplitOptions.TrimEntries);
                    double q = 1.0;
                    foreach (var p in pieces.Skip(1))
                    {
                        if (p.StartsWith("q=", StringComparison.OrdinalIgnoreCase) &&
                            double.TryParse(p[2..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                            q = v;
                    }
                    return (Tag: pieces[0], Q: q, Index: index);
                })
                .Where(x => x.Tag.Length > 0 && x.Tag != "*" && x.Q > 0)
                .OrderByDescending(x => x.Q).ThenBy(x => x.Index)
                .Select(x => x.Tag);

            var available = SupportedCultures.Select(c => c.Key).ToList();
            foreach (var tag in requested)
            {
                var match = MatchCulture(tag, available);
                if (match != null) return match;
            }
            return CurrentCulture;
        }

        private static string? MatchCulture(string tag, List<string> available)
        {
            string? Find(string c) => available.FirstOrDefault(a => string.Equals(a, c, StringComparison.OrdinalIgnoreCase));

            // 1) 完全相同
            var exact = Find(tag);
            if (exact != null) return exact;

            // 2) 中文：繁體 (台灣/香港/澳門/Hant) → zh-TW，簡體 (中國/新加坡/Hans/只有 zh) → zh-CN
            string lower = tag.ToLowerInvariant();
            if (lower == "zh" || lower.StartsWith("zh-"))
            {
                bool traditional = lower.Contains("hant") || lower.EndsWith("-tw") || lower.EndsWith("-hk") || lower.EndsWith("-mo");
                var zh = Find(traditional ? "zh-TW" : "zh-CN");
                if (zh != null) return zh;
            }

            // 3) 主要語言相同 (en-US → en、ja-JP → ja，或 pt → pt-BR)
            string primary = lower.Split('-')[0];
            return available.FirstOrDefault(a => string.Equals(a, primary, StringComparison.OrdinalIgnoreCase))
                ?? available.FirstOrDefault(a => a.Split('-')[0].Equals(primary, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>指定語言的完整字串表 (zh-TW 補齊缺少的 key)，網頁請求使用，會快取。</summary>
        private static Dictionary<string, string> GetCultureStrings(string culture) =>
            string.Equals(culture, CurrentCulture, StringComparison.OrdinalIgnoreCase) ? _strings : _cultureCache.GetOrAdd(culture, LoadMerged);

        /// <summary>遠端點歌網頁用的字串 ("Web." 開頭)，依 WebCulture (沒有時用主程式語言)。</summary>
        public static Dictionary<string, string> GetWebStrings() =>
            GetCultureStrings(WebCulture ?? CurrentCulture)
                .Where(kv => kv.Key.StartsWith(WebKeyPrefix, StringComparison.Ordinal))
                .ToDictionary(kv => kv.Key, kv => kv.Value);

        /// <summary>取得翻譯字串；找不到時回傳 null (不記錄 log)，給有自己預設值的地方使用。</summary>
        public static string? Find(string key) => _strings.TryGetValue(key, out var text) ? text : null;

        /// <summary>取得翻譯字串；有參數時以 string.Format 套用。找不到 key 時回傳 key 本身。</summary>
        public static string T(string key, params object?[] args)
        {
            var strings = _strings;
            var webCulture = _webCulture.Value;
            if (webCulture != null && key.StartsWith(WebKeyPrefix, StringComparison.Ordinal))
            {
                strings = GetCultureStrings(webCulture);
            }

            if (!strings.TryGetValue(key, out var text))
            {
                AppLogger.Log($"Missing localization key: {key}");
                return key;
            }
            return args.Length == 0 ? text : string.Format(text, args);
        }

        // 歌曲語系 (資料庫 Song_Lang 的值，也是篩選按鈕的 Tag) → 語言檔 key。值本身不可翻譯，只翻譯顯示文字。
        private static readonly Dictionary<string, string> SongLangKeys = new()
        {
            ["國語"] = "SongLang.Mandarin",
            ["台語"] = "SongLang.Taiwanese",
            ["英語"] = "SongLang.English",
            ["日語"] = "SongLang.Japanese",
            ["粵語"] = "SongLang.Cantonese",
            ["韓語"] = "SongLang.Korean",
            ["客語"] = "SongLang.Hakka",
            ["兒歌"] = "SongLang.Children",
            ["其他"] = "SongLang.Other",
            ["其它"] = "SongLang.Other",
        };

        private static readonly Dictionary<string, string> SongTypeKeys = new()
        {
            ["單曲"] = "SongType.Solo",
            ["合唱"] = "SongType.Duet",
        };

        /// <summary>歌曲語系的顯示文字 (例如 "國語" → "Mandarin")。繁中介面或未知的值原樣回傳。</summary>
        public static string SongLang(string? value)
        {
            if (string.IsNullOrEmpty(value) || CurrentCulture == DefaultCulture) return value ?? "";
            return SongLangKeys.TryGetValue(value, out var key) && _strings.TryGetValue(key, out var text) ? text : value;
        }

        /// <summary>歌曲列表語言欄用的短名 (欄寬有限，例如 "Mandarin" → "Mand")；語言檔沒有短名時用完整名稱。</summary>
        public static string SongLangShort(string? value)
        {
            if (string.IsNullOrEmpty(value) || CurrentCulture == DefaultCulture) return value ?? "";
            return SongLangKeys.TryGetValue(value, out var key) && _strings.TryGetValue(key.Replace("SongLang.", "SongLangShort."), out var text)
                ? text
                : SongLang(value);
        }

        /// <summary>"語系-類型" 組合篩選 (例如排行的 "國語-單曲") 的顯示文字；不含 '-' 時等同 SongLang。</summary>
        public static string SongFilter(string? value)
        {
            if (string.IsNullOrEmpty(value) || CurrentCulture == DefaultCulture) return value ?? "";
            int dash = value.IndexOf('-');
            if (dash < 0) return SongLang(value);
            string type = value[(dash + 1)..];
            string typeText = SongTypeKeys.TryGetValue(type, out var key) && _strings.TryGetValue(key, out var text) ? text : type;
            return string.Format(T("SongFilter.Format"), SongLang(value[..dash]), typeText);
        }

        /// <summary>是否為歌曲語系篩選值 (例如 "國語"、"國語-單曲")。</summary>
        public static bool IsSongFilter(string? value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            int dash = value.IndexOf('-');
            return SongLangKeys.ContainsKey(dash < 0 ? value : value[..dash]);
        }

        // 點歌人欄位的系統標記 (存在資料裡，不可翻譯)
        public const string OrderedByRandom = "隨機播放";
        public const string OrderedByWeb = "網路點歌";

        /// <summary>點歌人的顯示文字：系統標記翻譯，使用者名稱原樣回傳。</summary>
        public static string OrderedBy(string? value) => value switch
        {
            OrderedByRandom => T("OrderedBy.Random"),
            OrderedByWeb => T("OrderedBy.Web"),
            _ => value ?? ""
        };

        /// <summary>zh-TW 為底，再疊上指定語言。</summary>
        private static Dictionary<string, string> LoadMerged(string culture)
        {
            var strings = LoadFile(DefaultCulture);
            if (!string.Equals(culture, DefaultCulture, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var kv in LoadFile(culture))
                {
                    strings[kv.Key] = kv.Value;
                }
            }
            return strings;
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private static Dictionary<string, string> LoadFile(string culture)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Languages", culture + ".json");
            bool isDefault = string.Equals(culture, DefaultCulture, StringComparison.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(path))
                {
                    AppLogger.Log($"Language file not found: {path}");
                    return isDefault ? LoadEmbeddedDefault() : new Dictionary<string, string>();
                }

                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), JsonOptions)
                       ?? new Dictionary<string, string>();
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"Failed to load language file: {path}", ex);
                return isDefault ? LoadEmbeddedDefault() : new Dictionary<string, string>();
            }
        }

        /// <summary>內嵌在程式裡的 zh-TW.json (外部語言檔遺失或損壞時的最後保障)。</summary>
        private static Dictionary<string, string> LoadEmbeddedDefault()
        {
            try
            {
                using var stream = typeof(Loc).Assembly.GetManifestResourceStream("UltimateKtv.Languages.zh-TW.json");
                if (stream == null)
                {
                    AppLogger.LogError("Embedded zh-TW.json not found.", null);
                    return new Dictionary<string, string>();
                }
                AppLogger.Log("Using embedded zh-TW language strings.");
                return JsonSerializer.Deserialize<Dictionary<string, string>>(stream, JsonOptions) ?? new Dictionary<string, string>();
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Failed to load embedded zh-TW.json", ex);
                return new Dictionary<string, string>();
            }
        }
    }
}
