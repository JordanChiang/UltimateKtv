using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

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

        /// <summary>語言檔裡放語言代碼的 key (例如 "de"，可用逗號列出別名)，遠端點歌網頁依此對應裝置語言；未填時以檔名當代碼。</summary>
        public const string LanguageCodeKey = "Language.Code";

        /// <summary>掃描 Languages 目錄的結果：選單用的名稱與網頁比對用的代碼放在一起，整個換掉。</summary>
        private sealed class CultureCatalog
        {
            public CultureCatalog(IReadOnlyList<KeyValuePair<string, string>> names, IReadOnlyList<KeyValuePair<string, IReadOnlyList<string>>> codes)
            {
                Names = names;
                Codes = codes;
            }
            public IReadOnlyList<KeyValuePair<string, string>> Names { get; }
            public IReadOnlyList<KeyValuePair<string, IReadOnlyList<string>>> Codes { get; }
        }

        private static volatile CultureCatalog? _catalog;

        private static CultureCatalog Catalog => _catalog ??= ScanCultures();

        /// <summary>
        /// 可用的介面語言 (culture → 該語言自己的名稱)，來自 Languages 目錄下的 *.json，
        /// 使用者自行新增的語言檔也會列出。zh-TW 固定排第一。
        /// </summary>
        public static IReadOnlyList<KeyValuePair<string, string>> SupportedCultures => Catalog.Names;

        /// <summary>重新掃描 Languages 目錄 (開啟設定視窗時呼叫，新增的語言檔不用重開程式即可選擇)。</summary>
        public static IReadOnlyList<KeyValuePair<string, string>> RefreshCultures()
        {
            _cultureCache.Clear(); // 語言檔可能被修改，網頁用的快取一併清除
            return (_catalog = ScanCultures()).Names;
        }

        private static CultureCatalog ScanCultures()
        {
            var names = new List<KeyValuePair<string, string>>();
            var codes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Languages");
                if (Directory.Exists(dir))
                {
                    foreach (var file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                    {
                        string culture = Path.GetFileNameWithoutExtension(file);
                        var strings = LoadFile(culture);
                        string name = strings.TryGetValue(LanguageNameKey, out var n) && !string.IsNullOrWhiteSpace(n) ? n : culture;
                        names.Add(new KeyValuePair<string, string>(culture, name));
                        codes[culture] = strings.TryGetValue(LanguageCodeKey, out var code) ? code : null;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Failed to scan language files", ex);
            }

            // 基準語言一定存在且排第一
            names.RemoveAll(c => string.Equals(c.Key, DefaultCulture, StringComparison.OrdinalIgnoreCase));
            names.Insert(0, new KeyValuePair<string, string>(DefaultCulture, "繁體中文"));

            var cultureCodes = names
                .Select(c => new KeyValuePair<string, IReadOnlyList<string>>(c.Key, WebCultureMatcher.ParseCodes(codes.GetValueOrDefault(c.Key), c.Key)))
                .ToList();
            return new CultureCatalog(names, cultureCodes);
        }

        /// <summary>目前語言與字串表放在同一個物件，整個換掉 (切換時背景執行緒不會讀到「新語言 + 舊字串」的混合狀態)。</summary>
        private sealed class LangState
        {
            public LangState(string culture, Dictionary<string, string> strings) { Culture = culture; Strings = strings; }
            public string Culture { get; }
            public Dictionary<string, string> Strings { get; }
        }

        private static volatile LangState _state = new(DefaultCulture, new Dictionary<string, string>());
        private static ResourceDictionary? _dictionary;
        private static readonly object _applyLock = new();

        public static string CurrentCulture => _state.Culture;

        /// <summary>切換語言時觸發，給程式碼產生的文字重新整理用。</summary>
        public static event EventHandler? LanguageChanged;

        public static bool IsSupported(string? culture) =>
            culture != null && SupportedCultures.Any(c => string.Equals(c.Key, culture, StringComparison.OrdinalIgnoreCase));

        public static string Normalize(string? culture) =>
            SupportedCultures.FirstOrDefault(c => string.Equals(c.Key, culture, StringComparison.OrdinalIgnoreCase)).Key ?? DefaultCulture;

        /// <summary>
        /// 程式啟動時載入介面語言：先套用 zh-TW 當底，再嘗試指定語言。
        /// 指定的語言檔有問題時維持 zh-TW，problems 列出有問題的項目 (呼叫端負責提示使用者)。
        /// </summary>
        public static bool Initialize(string? culture, out List<string> problems)
        {
            ApplyStrings(DefaultCulture, LoadBase());
            return TryApply(culture, out problems);
        }

        /// <summary>
        /// 載入指定語言並更新 Application 資源。語言檔會先完整檢查，任何一項有問題就不切換 (繼續使用目前的語言)，
        /// 回傳 false 並由 problems 列出有問題的項目。
        /// </summary>
        public static bool TryApply(string? culture, out List<string> problems)
        {
            problems = new List<string>();
            string requested = string.IsNullOrWhiteSpace(culture) ? DefaultCulture : culture.Trim();
            culture = Normalize(requested);
            if (!string.Equals(requested, culture, StringComparison.OrdinalIgnoreCase))
            {
                problems?.Add(T("Lang.Err.NotFound", requested + ".json"));
                AppLogger.Log($"UI language '{requested}' not available, keeping {CurrentCulture}");
                return false;
            }

            var strings = LoadBase();
            if (!string.Equals(culture, DefaultCulture, StringComparison.OrdinalIgnoreCase))
            {
                var overlay = LoadFileChecked(culture, problems, strings);
                if (problems.Count > 0)
                {
                    AppLogger.Log($"UI language '{culture}' rejected, keeping {CurrentCulture}. Problems:{Environment.NewLine}  " +
                                  string.Join(Environment.NewLine + "  ", problems));
                    return false;
                }
                foreach (var kv in overlay) strings[kv.Key] = kv.Value;
            }

            try
            {
                ApplyStrings(culture, strings);
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"Failed to apply UI language '{culture}'", ex);
                problems?.Add(ex.Message);
                return false;
            }
        }

        /// <summary>把已檢查過的字串表套用到程式 (一律在 UI 執行緒執行)。</summary>
        private static void ApplyStrings(string culture, Dictionary<string, string> strings)
        {
            var app = Application.Current;
            if (app != null && !app.Dispatcher.CheckAccess())
            {
                app.Dispatcher.Invoke(() => ApplyStrings(culture, strings));
                return;
            }

            lock (_applyLock)
            {
                if (app != null)
                {
                    // 先把新字典完整建好，失敗的話完全不動目前的資源
                    var dict = new ResourceDictionary();
                    foreach (var kv in strings)
                    {
                        dict[ResourcePrefix + kv.Key] = kv.Value;
                    }

                    // 先加新的再移除舊的：後加入的字典優先，中間不會有 DynamicResource 找不到字串 (變成空白) 的空檔
                    var old = _dictionary;
                    app.Resources.MergedDictionaries.Add(dict);
                    _dictionary = dict;
                    if (old != null) app.Resources.MergedDictionaries.Remove(old);
                }

                _cultureCache[culture] = strings;
                _state = new LangState(culture, strings);
                RefreshHeaders();
            }

            AppLogger.Log($"UI language applied: {culture} ({strings.Count} strings)");

            // 個別處理程序出錯不影響其他處理程序，也不讓語言切換失敗
            if (LanguageChanged != null)
            {
                foreach (EventHandler handler in LanguageChanged.GetInvocationList())
                {
                    try { handler(null, EventArgs.Empty); }
                    catch (Exception ex) { AppLogger.LogError("LanguageChanged handler failed", ex); }
                }
            }
        }

        /// <summary>把語言檔的問題組成提示訊息 (項目太多時只列前面幾項)。</summary>
        public static string FormatProblems(string culture, IReadOnlyList<string> problems, int max = 12)
        {
            var lines = problems.Take(max).Select(p => "• " + p).ToList();
            if (problems.Count > max) lines.Add(T("Lang.Err.More", problems.Count));
            string current = SupportedCultures.FirstOrDefault(c => string.Equals(c.Key, CurrentCulture, StringComparison.OrdinalIgnoreCase)).Value ?? CurrentCulture;
            return T("Lang.Invalid", culture + ".json", current) + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, lines);
        }

        // ===== DataGrid 欄位標題 =====
        // DataGridColumn 不在視覺樹裡，Header="{DynamicResource ...}" 只會解析一次，切換語言不會更新。
        // 改用 local:Loc.HeaderKey="Col.SongName"，切換語言時由這裡重新設定 Header。

        public static readonly DependencyProperty HeaderKeyProperty = DependencyProperty.RegisterAttached(
            "HeaderKey", typeof(string), typeof(Loc), new PropertyMetadata(null, OnHeaderKeyChanged));

        public static string? GetHeaderKey(DependencyObject obj) => (string?)obj.GetValue(HeaderKeyProperty);
        public static void SetHeaderKey(DependencyObject obj, string? value) => obj.SetValue(HeaderKeyProperty, value);

        private static readonly List<WeakReference<DataGridColumn>> _headerColumns = new();

        private static void OnHeaderKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not DataGridColumn column) return;
            if (e.NewValue is string key && key.Length > 0)
            {
                column.Header = T(key);
                if (e.OldValue == null)
                {
                    _headerColumns.RemoveAll(w => !w.TryGetTarget(out _));
                    _headerColumns.Add(new WeakReference<DataGridColumn>(column));
                }
            }
        }

        private static void RefreshHeaders()
        {
            _headerColumns.RemoveAll(w => !w.TryGetTarget(out _));
            foreach (var weak in _headerColumns)
            {
                if (weak.TryGetTarget(out var column) && GetHeaderKey(column) is string key)
                {
                    column.Header = T(key);
                }
            }
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
        /// 依瀏覽器的 Accept-Language (例如 "zh-TW,zh;q=0.9,en-US;q=0.8") 選出可用的語言 (比對語言檔的 Language.Code)；
        /// 都對應不到時用主程式目前的語言。
        /// </summary>
        public static string ResolveWebCulture(string? acceptLanguage) =>
            WebCultureMatcher.Resolve(acceptLanguage, Catalog.Codes, CurrentCulture);

        /// <summary>指定語言的完整字串表 (zh-TW 補齊缺少的 key)，網頁請求使用，會快取。</summary>
        private static Dictionary<string, string> GetCultureStrings(string culture)
        {
            var state = _state;
            return string.Equals(culture, state.Culture, StringComparison.OrdinalIgnoreCase) ? state.Strings : _cultureCache.GetOrAdd(culture, LoadMerged);
        }

        /// <summary>遠端點歌網頁用的字串 ("Web." 開頭)，依 WebCulture (沒有時用主程式語言)。</summary>
        public static Dictionary<string, string> GetWebStrings() =>
            GetCultureStrings(WebCulture ?? CurrentCulture)
                .Where(kv => kv.Key.StartsWith(WebKeyPrefix, StringComparison.Ordinal))
                .ToDictionary(kv => kv.Key, kv => kv.Value);

        /// <summary>取得翻譯字串；找不到時回傳 null (不記錄 log)，給有自己預設值的地方使用。</summary>
        public static string? Find(string key) => _state.Strings.TryGetValue(key, out var text) ? text : null;

        /// <summary>取得翻譯字串；有參數時以 string.Format 套用。找不到 key 時回傳 key 本身。</summary>
        public static string T(string key, params object?[] args)
        {
            var strings = _state.Strings;
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
            var state = _state;
            if (string.IsNullOrEmpty(value) || state.Culture == DefaultCulture) return value ?? "";
            return SongLangKeys.TryGetValue(value, out var key) && state.Strings.TryGetValue(key, out var text) ? text : value;
        }

        /// <summary>歌曲列表語言欄用的短名 (欄寬有限，例如 "Mandarin" → "Mand")；語言檔沒有短名時用完整名稱。</summary>
        public static string SongLangShort(string? value)
        {
            var state = _state;
            if (string.IsNullOrEmpty(value) || state.Culture == DefaultCulture) return value ?? "";
            return SongLangKeys.TryGetValue(value, out var key) && state.Strings.TryGetValue(key.Replace("SongLang.", "SongLangShort."), out var text)
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
            string typeText = SongTypeKeys.TryGetValue(type, out var key) && _state.Strings.TryGetValue(key, out var text) ? text : type;
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

        /// <summary>zh-TW 為底，再疊上指定語言 (網頁用)。語言檔有問題時整份不用，只回傳 zh-TW。</summary>
        private static Dictionary<string, string> LoadMerged(string culture)
        {
            var strings = LoadBase();
            if (!string.Equals(culture, DefaultCulture, StringComparison.OrdinalIgnoreCase))
            {
                var problems = new List<string>();
                var overlay = LoadFileChecked(culture, problems, strings);
                if (problems.Count > 0)
                {
                    AppLogger.Log($"Language file '{culture}' has {problems.Count} problem(s), using {DefaultCulture} for web pages.");
                    return strings;
                }
                foreach (var kv in overlay) strings[kv.Key] = kv.Value;
            }
            return strings;
        }

        private static readonly JsonDocumentOptions JsonDocOptions = new()
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private static string LanguageFilePath(string culture) =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Languages", culture + ".json");

        /// <summary>基準語言 zh-TW：外部檔案遺失或有任何問題時改用內嵌版本。每次回傳新的字典 (呼叫端可直接修改)。</summary>
        private static Dictionary<string, string> LoadBase()
        {
            if (File.Exists(LanguageFilePath(DefaultCulture)))
            {
                var problems = new List<string>();
                var strings = LoadFileChecked(DefaultCulture, problems, null);
                if (problems.Count == 0 && strings.Count > 0) return strings;
                AppLogger.Log($"{DefaultCulture}.json has problems, using embedded copy:{Environment.NewLine}  " +
                              string.Join(Environment.NewLine + "  ", problems));
            }
            else
            {
                AppLogger.Log($"Language file not found: {LanguageFilePath(DefaultCulture)}");
            }
            return LoadEmbeddedDefault();
        }

        /// <summary>只讀取語言名稱等用途：不檢查，讀得到多少算多少。</summary>
        private static Dictionary<string, string> LoadFile(string culture) =>
            LoadFileChecked(culture, null, null);

        private static readonly Regex PlaceholderRegex = new(@"(?<!\{)\{(\d+)", RegexOptions.Compiled);

        /// <summary>{0} {1} … 佔位符的數量 (最大索引 + 1)，沒有佔位符時為 0。</summary>
        private static int PlaceholderCount(string text)
        {
            int count = 0;
            foreach (Match m in PlaceholderRegex.Matches(text))
            {
                if (int.TryParse(m.Groups[1].Value, out int index)) count = Math.Max(count, index + 1);
            }
            return count;
        }

        /// <summary>
        /// 讀取並逐項檢查語言檔，問題加到 problems (有問題的項目不會放進回傳的字典)。檢查項目：
        /// 檔案存在/可讀、JSON 格式、最外層是物件、key 不可空白或重複、值必須是非空白字串、
        /// 佔位符必須能以 string.Format 套用 (括號成對，且不超過 zh-TW 同一個 key 的參數數量)。
        /// baseStrings 為 zh-TW 字串表 (檢查其他語言時用來比對佔位符)，檢查 zh-TW 本身時傳 null。
        /// </summary>
        private static Dictionary<string, string> LoadFileChecked(string culture, List<string>? problems, Dictionary<string, string>? baseStrings)
        {
            var result = new Dictionary<string, string>();
            string path = LanguageFilePath(culture);
            string fileName = culture + ".json";

            string text;
            try
            {
                if (!File.Exists(path))
                {
                    problems?.Add(T("Lang.Err.NotFound", fileName));
                    return result;
                }
                text = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"Failed to read language file: {path}", ex);
                problems?.Add(T("Lang.Err.Read", fileName, ex.Message));
                return result;
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(text, JsonDocOptions);
            }
            catch (JsonException ex)
            {
                problems?.Add(T("Lang.Err.Json", (ex.LineNumber ?? 0) + 1, (ex.BytePositionInLine ?? 0) + 1));
                return result;
            }

            int unknownKeys = 0;
            using (doc)
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    problems?.Add(T("Lang.Err.NotObject"));
                    return result;
                }

                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    string key = prop.Name;
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        problems?.Add(T("Lang.Err.EmptyKey"));
                        continue;
                    }
                    if (!seen.Add(key))
                    {
                        problems?.Add(T("Lang.Err.Duplicate", key));
                        continue;
                    }
                    if (prop.Value.ValueKind != JsonValueKind.String)
                    {
                        problems?.Add(T("Lang.Err.NotString", key));
                        continue;
                    }

                    string value = prop.Value.GetString() ?? "";
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        problems?.Add(T("Lang.Err.Empty", key));
                        continue;
                    }

                    // 佔位符：程式以 zh-TW 的參數數量呼叫 string.Format，括號不成對或用到不存在的參數會直接例外
                    string? baseValue = null;
                    baseStrings?.TryGetValue(key, out baseValue);
                    int argCount = PlaceholderCount(baseValue ?? value);
                    if (argCount > 0 || PlaceholderCount(value) > 0)
                    {
                        try
                        {
                            string.Format(value, Enumerable.Repeat((object)"x", argCount).ToArray());
                        }
                        catch (FormatException)
                        {
                            problems?.Add(argCount > 0
                                ? T("Lang.Err.Format", key, string.Join(" ", Enumerable.Range(0, argCount).Select(i => "{" + i + "}")))
                                : T("Lang.Err.FormatNone", key));
                            continue;
                        }
                    }

                    if (baseStrings != null && !baseStrings.ContainsKey(key)) unknownKeys++;
                    result[key] = value;
                }
            }

            // zh-TW 沒有的 key 不算錯 (例如設定說明，繁中直接用程式內的預設文字)，只記錄數量
            if (unknownKeys > 0) AppLogger.Log($"Language file {fileName}: {unknownKeys} key(s) not in {DefaultCulture}.json");
            return result;
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
