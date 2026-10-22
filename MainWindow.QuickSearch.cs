using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UltimateKtv.Enums;
using YoutubeExplode;
using YoutubeExplode.Common;

namespace UltimateKtv
{
    public partial class MainWindow
    {
        // Backing collection for the 40 quick word buttons; bind once and update in place
        private readonly ObservableCollection<string> _quickWords = new();
        private readonly YoutubeClient _youtube = new();

        // State storage for each quick search method to preserve user input
        private readonly Dictionary<QuickMethod, List<string>> _quickMethodSelectedWords = new();
        private readonly Dictionary<QuickMethod, int> _quickMethodCurrentPage = new();
        private readonly Dictionary<QuickMethod, List<SongDisplayItem>> _quickMethodResultsCache = new();
        private bool _isQuickSearchInitialized = false;

        // 最後一次 YT 搜尋的關鍵字、結果與頁數。YT 搜尋與一般搜尋共用上面的快取，
        // 切到別頁時那些快取會被清掉，所以另存一份，切回 Youtube 頁時還原 (不用重新搜尋)
        private string _youtubeSearchKeyword = string.Empty;
        private List<SongDisplayItem> _youtubeSearchResults = new();
        private int _youtubeSearchPage = 1;

        // Exclusive selector for the 4 search type buttons
        private enum QuickMethod { Bopomofo, EngNum, PenStyle, SongId, Keyboard, Extra5, YoutubeHistory }
        private QuickMethod _currentQuickMethod = QuickMethod.Bopomofo;

        // Toggle state for Song vs Singer vs Youtube search
        private enum SearchMode { Song, Singer, Youtube }
        private SearchMode _searchModeValue = SearchMode.Song;

        /// <summary>
        /// 目前的搜尋模式。搜尋列 (含 YouTube 下載狀態) 由歌曲/歌星/YouTube 共用，
        /// 切換模式時一併決定下載狀態是否顯示：只在 YouTube 模式顯示，下載仍在背景繼續。
        /// </summary>
        private SearchMode _searchMode
        {
            get => _searchModeValue;
            set
            {
                _searchModeValue = value;
                if (YoutubeStatusPanel != null)
                    YoutubeStatusPanel.Visibility = value == SearchMode.Youtube ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        private bool _isSingerSearchMode => _searchMode == SearchMode.Singer;
        
        // Centralized page size for Quick Search (Keyboard uses 11 rows to utilize available vertical space, -1 for card style height)
        // 歌星搜尋以每排 4 位的按鈕格顯示 (QuickSingerGrid)，一頁 = 排數 x 4
        private const int QuickSingerColumns = 4;
        // Youtube 搜尋結果用 4x4 縮圖格 (16)；Youtube 點播記錄用 QuickSongListGrid，一頁 12 首
        // 鍵盤與 Youtube 點播記錄沒有 40 字按鈕格，列表加高 (810) 並多顯示幾列
        private bool IsTallQuickList => _currentQuickMethod == QuickMethod.Keyboard || _currentQuickMethod == QuickMethod.YoutubeHistory;
        // 12 列歌曲列表 (列高 60)：Youtube 點播記錄、鍵盤搜尋歌曲
        private bool IsTwelveRowQuickSongList => _currentQuickMethod == QuickMethod.YoutubeHistory
            || (_currentQuickMethod == QuickMethod.Keyboard && _searchMode == SearchMode.Song);

        private int QuickSearchPageSize => (_searchMode == SearchMode.Youtube && _currentQuickMethod != QuickMethod.YoutubeHistory)
            ? 16
            : IsTwelveRowQuickSongList ? 12
            : (IsTallQuickList ? 11 : 8) * (_isSingerSearchMode ? QuickSingerColumns : 1);

        /// <summary>
        /// QuickSongListGrid 高度與位置：一般 8 列 (594)、鍵盤 11 列 (810)、
        /// Youtube 點播記錄與鍵盤搜尋歌曲 12 列：列高改 60，每列實際約佔 65 (列邊距 3 + 外框 2)，表頭約 58，
        /// 共約 838，高度 850；上方多留 6 往下移一點，總高 856 (內容區放得下 Margin 8 + 850 的縮圖格)。
        /// </summary>
        private void ApplyQuickSongListLayout()
        {
            if (QuickSongListGrid == null) return;
            bool isTwelveRows = IsTwelveRowQuickSongList;
            QuickSongListGrid.RowHeight = isTwelveRows ? 60 : 62;
            QuickSongListGrid.Height = isTwelveRows ? 850 : IsTallQuickList ? 810 : 594;
            QuickSongListGrid.Margin = isTwelveRows ? new Thickness(0, 6, 0, 0) : new Thickness(0);
        }

        // For async quick search to prevent UI lag
        private CancellationTokenSource? _quickSearchCts;

        private List<string> BopomofoList = null!;
        private List<string> PenStyleList = null!;
        private List<string> EngAndNumList = null!;
        private List<string> SongIdStyleList = null!;

        /// <summary>
        /// Initialize the data sets for the 40 quick word buttons.
        /// Ensures each list has exactly 40 items (pads with blanks if needed).
        /// </summary>
        private void InitializeQuickWordSets()
        {
            // Bopomofo core (36) + ㄦ + 3 tone marks to reach 40
            BopomofoList = new List<string>
            {
//                "ㄅ","ㄆ","ㄇ","ㄈ","ㄉ","ㄊ","ㄋ","ㄌ",
//                "ㄍ","ㄎ","ㄏ","ㄐ","ㄑ","ㄒ","ㄓ","ㄔ",
//                "ㄕ","ㄖ","ㄗ","ㄘ","ㄙ","ㄧ","ㄨ","ㄩ",
//                "ㄚ","ㄛ","ㄜ","ㄝ","ㄞ","ㄟ","ㄠ","ㄡ",
//                "ㄢ","ㄣ","ㄤ","ㄥ","ㄦ","ˊ","ˇ","ˋ"

                "ㄅ", "ㄊ", "ㄏ", "ㄔ", "ㄙ", "ㄛ", "ㄠ", "ㄥ",
                "ㄆ", "ㄋ", "ㄐ", "ㄕ", "ㄧ", "ㄜ", "ㄡ", "ㄦ",
                "ㄇ", "ㄌ", "ㄑ", "ㄖ", "ㄨ", "ㄝ", "ㄢ", "ˊ",
                "ㄈ", "ㄍ", "ㄒ", "ㄗ", "ㄩ", "ㄞ", "ㄣ", "ˇ",
                "ㄉ", "ㄎ", "ㄓ", "ㄘ", "ㄚ", "ㄟ", "ㄤ", "ˋ" 
            };

            // English letters + digits (36) + 4 symbols to reach 40
            EngAndNumList = new List<string>
            {
                "A","F","K","P","U","1","2","3",
                "B","G","L","Q","V","4","5","6",
                "C","H","M","R","W","7","8","9",
                "D","I","N","S","X","","0","",
                "E","J","O","T","Y","Z", " "
            };

            // Pen style (stroke count) placeholders: 
            PenStyleList = new List<string>
            {
                "丿","一","丨","乛","丶"
            };

            // number style (stroke count) placeholders: 
            SongIdStyleList = new List<string>
            {
                "1","2","3"," "," "," "," "," ",
                "4","5","6"," "," "," "," "," ",
                "7","8","9"," "," "," "," "," ",
                " ","0"," "," "," "," "," "," ",
            };

            // Ensure lists are exactly 40 items
            BopomofoList = PadTo40(BopomofoList);
            EngAndNumList = PadTo40(EngAndNumList);
            PenStyleList = PadTo40(PenStyleList);
            SongIdStyleList = PadTo40(SongIdStyleList);
        }

        private static List<string> PadTo40(List<string> src)
        {
            var list = new List<string>(src);
            while (list.Count < 40) list.Add(string.Empty);
            if (list.Count > 40) list = list.Take(40).ToList();
            return list;
        }

        /// <summary>
        /// Binds the provided list to the 40 quick word buttons.
        /// </summary>
        private void SetQuickWordSet(List<string> items)
        {
            try
            {
                var count = items?.Count ?? 0;
                using (Dispatcher.DisableProcessing())
                {

                    // Ensure we have exactly 40 entries to display
                    var list = PadTo40(items ?? new List<string>());

                    // Initialize collection once to exactly 40 placeholders
                    if (_quickWords.Count == 0)
                    {
                        for (int i = 0; i < 40; i++) _quickWords.Add(string.Empty);
                    }

                    // Update in place to avoid regenerating containers
                    int n = Math.Min(40, list.Count);
                    for (int i = 0; i < n; i++)
                    {
                        if (!string.Equals(_quickWords[i], list[i]))
                            _quickWords[i] = list[i];
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SetQuickWordSet error: {ex.Message}");
            }
        }

        // Selector button click handlers (exclusive)
        private void BopomofoListBtn_Click(object sender, RoutedEventArgs e) => SetQuickMethod(QuickMethod.Bopomofo, sender as Button);

        private void YoutubeSearchBtn_Click(object sender, RoutedEventArgs e)
        {
            // 下載中也可以繼續搜尋/點歌 (多首排隊下載)；取消下載用獨立的 YoutubeCancelDownloadBtn
            if (_searchMode == SearchMode.Youtube && _currentQuickMethod == QuickMethod.YoutubeHistory)
            {
                SetQuickMethod(QuickMethod.Keyboard, KeyboardListBtn);
            }

            if (_searchMode == SearchMode.Youtube || _currentQuickMethod == QuickMethod.Keyboard)
            {
                UpdateSearchWords(true);
            }
        }
        private void EngAndNumListBtn_Click(object sender, RoutedEventArgs e) => SetQuickMethod(QuickMethod.EngNum, sender as Button);
        private void PenStyleListBtn_Click(object sender, RoutedEventArgs e) => SetQuickMethod(QuickMethod.PenStyle, sender as Button);
        private void SongIdListBtn_Click(object sender, RoutedEventArgs e) => SetQuickMethod(QuickMethod.SongId, sender as Button);
        private void KeyboardListBtn_Click(object sender, RoutedEventArgs e) => SetQuickMethod(QuickMethod.Keyboard, sender as Button);
        private void ExtraListBtn5_Click(object sender, RoutedEventArgs e) => SetQuickMethod(QuickMethod.Extra5, sender as Button);
        public bool HasCheckedYoutubeHistoryFiles { get { return _hasCheckedYoutubeHistoryFiles; } set { _hasCheckedYoutubeHistoryFiles = value; } }
        private bool _hasCheckedYoutubeHistoryFiles = false;

        public List<string> GetUnrecordedYoutubeFiles()
        {
            var newFiles = new List<string>();
            try
            {
                var dlFolder = SettingsManager.Instance.CurrentSettings.YoutubeDownloadDir;
                if (!System.IO.Path.IsPathRooted(dlFolder))
                {
                    dlFolder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dlFolder);
                }

                if (!string.IsNullOrEmpty(dlFolder) && System.IO.Directory.Exists(dlFolder))
                {
                    var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
                    { 
                        ".mpeg", ".mpg", ".mp4", ".mp3", ".avi", ".mkv" 
                    };
                    var files = System.IO.Directory.GetFiles(dlFolder)
                        .Where(f => allowedExtensions.Contains(System.IO.Path.GetExtension(f)))
                        .ToArray();
                        
                    if (files.Length > 0)
                    {
                        var history = SongDatas.GetYoutubeHistory();
                        var existingFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var item in history)
                        {
                            if (item.TryGetValue("FileName", out var fnObj) && fnObj is string fn)
                            {
                                existingFileNames.Add(fn);
                            }
                        }

                        foreach (var file in files)
                        {
                            var fileName = System.IO.Path.GetFileName(file);
                            if (!existingFileNames.Contains(fileName))
                            {
                                newFiles.Add(file);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Error in GetUnrecordedYoutubeFiles", ex);
            }
            return newFiles;
        }

        public async Task ImportUnrecordedYoutubeFilesAsync(List<string> newFiles)
        {
            if (newFiles == null || newFiles.Count == 0) return;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await Task.Run(() =>
            {
                foreach (var file in newFiles)
                {
                    var item = new SongDisplayItem
                    {
                        SongName = YtDlpHelper.GetTitleFromFileName(file),
                        FilePath = file,
                        SongId = YtDlpHelper.ExtractVideoIdFromFileName(file), // blank if the name has no "_{videoId}" suffix
                        Language = "",
                        Volume = 30
                    };
                    SongDatas.RecordYoutubeSong(item);
                    System.Threading.Thread.Sleep(5); // Pause to prevent OleDb AccessViolation
                }
            });
            stopwatch.Stop();
            
            // Invalidate cache so it refreshes
            _quickMethodResultsCache[QuickMethod.YoutubeHistory] = new List<SongDisplayItem>();
            if (_currentQuickMethod == QuickMethod.YoutubeHistory)
            {
                _ = RebuildQuickResultsCache();
            }
            
            string successMsg = $"Successfully imported {newFiles.Count} YouTube files to database. Time taken: {stopwatch.ElapsedMilliseconds} ms.";
            AppLogger.Log(successMsg);
            Debug.WriteLine(successMsg);
        }

        public async Task CheckAndImportLocalYoutubeFiles(bool silent = false)
        {
            try
            {
                var newFiles = GetUnrecordedYoutubeFiles();

                if (newFiles.Count > 0)
                {
                    string logMsg = $"Found {newFiles.Count} unrecorded YouTube files in cache";
                    AppLogger.Log(logMsg);
                    System.Diagnostics.Debug.WriteLine(logMsg);

                    MessageBoxResult result = MessageBoxResult.Yes;
                    if (!silent)
                    {
                        var msg = Loc.T("Youtube.ImportPrompt", newFiles.Count);
                        result = MessageBox.Show(msg, Loc.T("Youtube.ImportTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
                    }

                    if (result == MessageBoxResult.Yes)
                    {
                        await ImportUnrecordedYoutubeFilesAsync(newFiles);
                    }
                    else
                    {
                        string declinedMsg = "User declined to import unrecorded YouTube files.";
                        AppLogger.Log(declinedMsg);
                        System.Diagnostics.Debug.WriteLine(declinedMsg);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Error in CheckAndImportLocalYoutubeFiles", ex);
                Debug.WriteLine($"Error in CheckAndImportLocalYoutubeFiles: {ex.Message}");
            }
        }

        private void YoutubeHistoryBtn_Click(object sender, RoutedEventArgs e) 
        {
            if (_currentQuickMethod == QuickMethod.YoutubeHistory)
            {
                SetQuickMethod(QuickMethod.Keyboard, KeyboardListBtn);
                RestoreYoutubeSearchResults();
            }
            else
            {
                SetQuickMethod(QuickMethod.YoutubeHistory, sender as Button);
            }
        }

        /// <summary>
        /// 把上次的 YT 搜尋結果放回目前查詢方式的快取 (含關鍵字與頁數)；沒有結果時回傳 false。
        /// </summary>
        private bool RestoreYoutubeSearchResults()
        {
            if (_currentQuickMethod == QuickMethod.YoutubeHistory || _youtubeSearchResults.Count == 0) return false;

            var words = _quickMethodSelectedWords[_currentQuickMethod];
            words.Clear();
            words.Add(_youtubeSearchKeyword);
            _quickMethodResultsCache[_currentQuickMethod] = _youtubeSearchResults;
            _quickMethodCurrentPage[_currentQuickMethod] = _youtubeSearchPage;
            return true;
        }

        private void ClearYoutubeBtn_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(Loc.T("Youtube.ClearPrompt"), Loc.T("Youtube.ClearTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                // Delete from DB
                try
                {
                    SongLibrary.ClearYoutubeHistory();
                    AppLogger.Log("Cleared YoutubeSongList database.");
                    
                    // Clear YoutubeHistory memory cache if any
                    _quickMethodResultsCache[QuickMethod.YoutubeHistory] = new List<SongDisplayItem>();
                    if (_currentQuickMethod == QuickMethod.YoutubeHistory)
                    {
                        RefreshQuickResultsPage();
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("Failed to clear Youtube database", ex);
                }

                // Delete from FileSystem
                try
                {
                    var dlFolder = SettingsManager.Instance.CurrentSettings.YoutubeDownloadDir;
                    // Provide a fully qualified path if it's a relative path.
                    if (!System.IO.Path.IsPathRooted(dlFolder))
                    {
                        dlFolder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dlFolder);
                    }
                    if (!string.IsNullOrEmpty(dlFolder) && System.IO.Directory.Exists(dlFolder))
                    {
                        var files = System.IO.Directory.GetFiles(dlFolder);
                        int count = 0;
                        foreach (var file in files)
                        {
                            try 
                            { 
                                System.IO.File.Delete(file); 
                                count++;
                            } 
                            catch { }
                        }
                        AppLogger.Log($"Cleared {count} Youtube files from {dlFolder}.");
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("Failed to clear Youtube files", ex);
                }
            }
        }

        private QuickMethod _previousQuickMethod = QuickMethod.Bopomofo;

        // Toggle button handler
        // yt-dlp 與未記錄檔案的檢查只在左側「Youtube」按鈕 (FuncBtnByYoutube_Click) 執行
        private void SearchMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton radio)
            {
                if (radio == YoutubeRadio)
                {
                    // Clear search keywords when entering YouTube mode IMMEDIATELY,
                    // then bring back the last YouTube search results (if any) instead of searching again
                    if (SearchWords != null) SearchWords.Text = string.Empty;
                    _quickMethodSelectedWords[_currentQuickMethod].Clear();
                    bool restoredYoutubeSearch = RestoreYoutubeSearchResults();

                    if (_currentQuickMethod != QuickMethod.YoutubeHistory)
                    {
                        _previousQuickMethod = _currentQuickMethod;
                    }

                    if (BopomofoListBtn != null) BopomofoListBtn.Visibility = Visibility.Collapsed;
                    if (PenStyleListBtn != null) PenStyleListBtn.Visibility = Visibility.Collapsed;
                    if (EngAndNumListBtn != null) EngAndNumListBtn.Visibility = Visibility.Collapsed;
                    if (SongIdListBtn != null) SongIdListBtn.Visibility = Visibility.Collapsed;
                    if (KeyboardListBtn != null) KeyboardListBtn.Visibility = Visibility.Collapsed;
                    if (YoutubeHistoryBtn != null) YoutubeHistoryBtn.Visibility = Visibility.Visible;
                    if (ClearYoutubeBtn != null) ClearYoutubeBtn.Visibility = Visibility.Visible;
                    if (SearchModeSegmentBorder != null) SearchModeSegmentBorder.Visibility = Visibility.Collapsed;

                    _searchMode = SearchMode.Youtube;
                    SearchWords?.Focus();
                    if (SearchInputGrid != null) SearchInputGrid.Visibility = Visibility.Collapsed;
                    if (SingerGrid != null) SingerGrid.Visibility = Visibility.Collapsed;
                    if (VisualSingerGrid != null) VisualSingerGrid.Visibility = Visibility.Collapsed;

                    bool useYoutubeGrid = _currentQuickMethod != QuickMethod.YoutubeHistory;

                    // Relocated: YoutubeThumbnailGrid is now inside QuickResultsContainer
                    if (YoutubeThumbnailGrid != null) YoutubeThumbnailGrid.Visibility = useYoutubeGrid ? Visibility.Visible : Visibility.Collapsed;
                    if (QuickResultsContainer != null) QuickResultsContainer.Visibility = Visibility.Visible;
                    if (QuickSongListGrid != null) QuickSongListGrid.Visibility = !useYoutubeGrid ? Visibility.Visible : Visibility.Collapsed;
                    if (QuickSingerGrid != null) QuickSingerGrid.Visibility = Visibility.Collapsed;
                    if (SingerSongContentGrid != null) SingerSongContentGrid.Visibility = Visibility.Collapsed;

                    if (restoredYoutubeSearch)
                    {
                        UpdateSearchWords(false); // shows the restored keyword and page
                        return;
                    }
                }
                else
                {
                    var oldMode = _searchMode;
                    if (radio == SingerRadio) _searchMode = SearchMode.Singer;
                    else _searchMode = SearchMode.Song;

                    // Clear caches to prevent cross-contamination between modes
                    if (oldMode != _searchMode)
                    {
                        foreach (var key in _quickMethodResultsCache.Keys.ToList())
                        {
                            _quickMethodResultsCache[key] = new List<SongDisplayItem>();
                        }
                    }

                    if (BopomofoListBtn != null) BopomofoListBtn.Visibility = Visibility.Visible;
                    if (PenStyleListBtn != null) PenStyleListBtn.Visibility = Visibility.Visible;
                    if (EngAndNumListBtn != null) EngAndNumListBtn.Visibility = Visibility.Visible;
                    if (SongIdListBtn != null) SongIdListBtn.Visibility = Visibility.Visible;
                    if (KeyboardListBtn != null) KeyboardListBtn.Visibility = Visibility.Visible;
                    if (YoutubeHistoryBtn != null) YoutubeHistoryBtn.Visibility = Visibility.Collapsed;
                    if (ClearYoutubeBtn != null) ClearYoutubeBtn.Visibility = Visibility.Collapsed;
                    if (SearchModeSegmentBorder != null) SearchModeSegmentBorder.Visibility = Visibility.Visible;

                    // Clear search keywords when switching from YouTube mode to others as requested
                    if (oldMode == SearchMode.Youtube)
                    {
                        if (SearchWords != null) SearchWords.Text = string.Empty;
                        foreach (var key in _quickMethodSelectedWords.Keys.ToList())
                        {
                            _quickMethodSelectedWords[key].Clear();
                        }
                        
                        // Restore previous quick method state
                        Button? targetBtn = null;
                        switch (_previousQuickMethod)
                        {
                            case QuickMethod.Bopomofo: targetBtn = BopomofoListBtn; break;
                            case QuickMethod.PenStyle: targetBtn = PenStyleListBtn; break;
                            case QuickMethod.EngNum: targetBtn = EngAndNumListBtn; break;
                            case QuickMethod.SongId: targetBtn = SongIdListBtn; break;
                            case QuickMethod.Keyboard: targetBtn = KeyboardListBtn; break;
                        }
                        SetQuickMethod(_previousQuickMethod, targetBtn);
                    }

                    if (SearchInputGrid != null) SearchInputGrid.Visibility = (_currentQuickMethod == QuickMethod.Keyboard) ? Visibility.Collapsed : Visibility.Visible;
                    if (QuickSongListGrid != null) ApplyQuickSongListLayout();
                    // Other visibilities will be handled by UpdateSearchWords -> RefreshQuickResultsPage
                }

                UpdateSearchWords(true);
            }
        }

        private void SetQuickMethod(QuickMethod method, Button? clicked)
        {
            if (!_isQuickSearchInitialized)
            {
                InitializeQuickWordSets();
                _isQuickSearchInitialized = true;
            }

            _currentQuickMethod = method;

            using (Dispatcher.DisableProcessing())
            {
                // Temporarily collapse word buttons to avoid intermediate layout churn
                var oldQuickWordsVisibility = SearchInputGrid?.Visibility ?? Visibility.Visible;
                if (SearchInputGrid != null) SearchInputGrid.Visibility = Visibility.Collapsed;

                // Update 40-button set by method

                switch (method)
                {
                    case QuickMethod.Bopomofo:
                        SetQuickWordSet(BopomofoList);
                        break;
                    case QuickMethod.EngNum:
                        SetQuickWordSet(EngAndNumList);
                        break;
                    case QuickMethod.PenStyle:
                        SetQuickWordSet(PenStyleList);
                        break;
                    case QuickMethod.SongId:
                        SetQuickWordSet(SongIdStyleList);
                        break;
                    case QuickMethod.Keyboard:
                    case QuickMethod.Extra5:
                    case QuickMethod.YoutubeHistory:
                        SetQuickWordSet(new List<string>()); // no items yet
                        break;
                }


                // Visual selection: highlight clicked by changing colors, de-emphasize others
                var activeBg = (System.Windows.Media.Brush)(TryFindResource("BrushCyberActiveGradient") ?? FindResource("SingerButtonBackground"));
                var activeFg = (System.Windows.Media.Brush)FindResource("BrushOnAccentActive");
                var inactiveBg = (System.Windows.Media.Brush)(TryFindResource("BrushButtonFlat") ?? System.Windows.Media.Brushes.Transparent);
                var inactiveFg = (System.Windows.Media.Brush)FindResource("SingerButtonBackground");
                var activeBorder = (System.Windows.Media.Brush)(TryFindResource("BrushAccent") ?? TryFindResource("PrimaryHueMidBrush") ?? activeBg);
                var inactiveBorder = (System.Windows.Media.Brush)FindResource("SingerButtonBackground");

                var buttons = new[] { BopomofoListBtn, EngAndNumListBtn, PenStyleListBtn, SongIdListBtn, KeyboardListBtn, YoutubeHistoryBtn };
                foreach (var btn in buttons)
                {
                    if (btn == null) continue;
                    bool isActive = btn == clicked;
                    btn.FontWeight = isActive ? FontWeights.Bold : FontWeights.Normal;
                    btn.Opacity = isActive ? 1.0 : 0.85;
                    btn.Background = isActive ? activeBg : inactiveBg;
                    btn.Foreground = isActive ? activeFg : inactiveFg;
                    btn.BorderBrush = isActive ? activeBorder : inactiveBorder;
                }

                // Restore visibility after updates
                if (SearchInputGrid != null) SearchInputGrid.Visibility = (method == QuickMethod.Keyboard || _searchMode == SearchMode.Youtube) ? Visibility.Collapsed : Visibility.Visible;
                if (QuickSongListGrid != null) ApplyQuickSongListLayout();
            }

            // Update the display with the preserved state for the selected method.
            // IMPORTANT: Do not trigger a new search when only switching tabs to avoid lag.
            // Reuse existing cache and just refresh the UI.
            // Defer the refresh so layout/render can coalesce first
            Dispatcher.BeginInvoke(new Action(() =>
            {
                bool triggerSearch = method == QuickMethod.YoutubeHistory;
                if (!triggerSearch)
                {
                    // If the cache is empty but there are selected words, we MUST trigger a search!
                    if (_quickMethodResultsCache.TryGetValue(method, out var cache) && cache.Count == 0 &&
                        _quickMethodSelectedWords.TryGetValue(method, out var words) && words.Count > 0)
                    {
                        triggerSearch = true;
                    }
                }
                UpdateSearchWords(triggerSearch);
                if (method == QuickMethod.Keyboard)
                {
                    SearchWords?.Focus();
                }

            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        // Quick word button click: append up to 10 tokens
        private void QuickWordButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                var word = Convert.ToString(btn.Tag) ?? string.Empty;
                if (string.IsNullOrEmpty(word)) return;
                AddQuickSearchWord(word);
            }
        }

        // Update the static text to reflect current selected words
        // resetPage: false keeps the current page (e.g. background refresh of YouTube history after a download)
        private async void UpdateSearchWords(bool triggerSearch = true, bool resetPage = true)
        {
            try
            {
                if (SearchWords != null)
                {
                    // If in Youtube mode or Keyboard mode, we might want to preserve what the user typed manually
                    bool isTextInputMode = (_searchMode == SearchMode.Youtube && _currentQuickMethod != QuickMethod.YoutubeHistory) || _currentQuickMethod == QuickMethod.Keyboard;
                    
                    // If triggerSearch is false (just refreshing UI), we update the text
                    // If triggerSearch is true (new search), we might want to sync text -> buffer
                    if (!triggerSearch || !isTextInputMode)
                    {
                        SearchWords.Text = string.Join("", _quickMethodSelectedWords[_currentQuickMethod]);
                    }
                    else
                    {
                        // Sync UI text back to buffer for Youtube/Keyboard mode
                        _quickMethodSelectedWords[_currentQuickMethod].Clear();
                        if (!string.IsNullOrEmpty(SearchWords.Text))
                        {
                            _quickMethodSelectedWords[_currentQuickMethod].Add(SearchWords.Text);
                        }
                    }
                }
                // Update quick list visibility and content
                if (triggerSearch)
                {
                    // Refresh UI immediately to show/hide grids (like YoutubeThumbnailGrid) 
                    // before potential long-running cache rebuild
                    RefreshQuickResultsPage();
                    
                    if (resetPage) _quickMethodCurrentPage[_currentQuickMethod] = 1; // reset to first page on query change
                    await RebuildQuickResultsCache();
                }
                RefreshQuickResultsPage();
            }
            catch (Exception)
            {
                // ignore UI update errors
            }
        }

        private void SearchWords_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_searchMode == SearchMode.Youtube) return; // YouTube uses search button
            if (_currentQuickMethod == QuickMethod.Keyboard && SearchWords.IsFocused)
            {
                UpdateSearchWords(true);
            }
        }

        // Delete last selected token (or character in Youtube mode)
        private void DeleteEnteredWordBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_searchMode == SearchMode.Youtube && _currentQuickMethod == QuickMethod.YoutubeHistory)
            {
                SetQuickMethod(QuickMethod.Keyboard, KeyboardListBtn);
            }

            if (_searchMode == SearchMode.Youtube || _currentQuickMethod == QuickMethod.Keyboard)
            {
                if (SearchWords != null && SearchWords.Text.Length > 0)
                {
                    SearchWords.Text = SearchWords.Text.Substring(0, SearchWords.Text.Length - 1);
                    // Move caret to end
                    SearchWords.SelectionStart = SearchWords.Text.Length;
                    SearchWords.Focus();
                }
                return;
            }

            var currentWords = _quickMethodSelectedWords[_currentQuickMethod];
            if (currentWords.Count > 0)
            {
                currentWords.RemoveAt(currentWords.Count - 1);
                UpdateSearchWords();
            }
        }

        // Clear all selected tokens (or text in Youtube mode)
        private void ClearEnteredWordBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_searchMode == SearchMode.Youtube && _currentQuickMethod == QuickMethod.YoutubeHistory)
            {
                SetQuickMethod(QuickMethod.Keyboard, KeyboardListBtn);
            }

            if (_searchMode == SearchMode.Youtube || _currentQuickMethod == QuickMethod.Keyboard)
            {
                if (SearchWords != null)
                {
                    SearchWords.Text = string.Empty;
                    SearchWords.Focus();
                }
                return;
            }

            var currentWords = _quickMethodSelectedWords[_currentQuickMethod];
            if (currentWords.Count > 0)
            {
                currentWords.Clear();
                UpdateSearchWords();
            }
        }


        // Rebuild the quick results cache when query (selected words/method) changes
        private async Task RebuildQuickResultsCache()
        {
            // Cancel any previous search that is still running.
            _quickSearchCts?.Cancel();
            _quickSearchCts = new CancellationTokenSource();
            var cancellationToken = _quickSearchCts.Token;

            try
            {
                await Task.Delay(50, cancellationToken); // A small delay to debounce rapid input.
                var currentWords = _quickMethodSelectedWords[_currentQuickMethod];
                var key = string.Join("", currentWords);

                // Panel visibility when no key
                if (string.IsNullOrEmpty(key) && _currentQuickMethod != QuickMethod.YoutubeHistory)
                {
                    _quickMethodResultsCache[_currentQuickMethod] = new List<SongDisplayItem>();
                    _quickMethodCurrentPage[_currentQuickMethod] = 1;
                    
                    if (_searchMode == SearchMode.Youtube)
                    {
                        // An empty YouTube search clears the remembered results too
                        _youtubeSearchKeyword = string.Empty;
                        _youtubeSearchResults = new List<SongDisplayItem>();
                        _youtubeSearchPage = 1;
                        RefreshQuickResultsPage();
                    }
                    else
                    {
                        if (QuickSongListGrid != null) QuickSongListGrid.Visibility = Visibility.Collapsed;
                        if (QuickSingerGrid != null) QuickSingerGrid.Visibility = Visibility.Collapsed;
                        if (QuickSongCountText != null) QuickSongCountText.Visibility = Visibility.Collapsed;
                    }
                    return;
                }

                // For SongId search, require at least 2 digits before showing any results to avoid overly broad searches.
                if (_currentQuickMethod == QuickMethod.SongId && key.Length < 2)
                {
                    _quickMethodResultsCache[_currentQuickMethod] = new List<SongDisplayItem>();
                    _quickMethodCurrentPage[_currentQuickMethod] = 1;
                    // Explicitly call Refresh to ensure the UI (count, buttons) is cleared.
                    RefreshQuickResultsPage();
                    return;
                }

                if (SongDatas.SongData == null)
                {
                    _quickMethodResultsCache[_currentQuickMethod] = new List<SongDisplayItem>();
                    _quickMethodCurrentPage[_currentQuickMethod] = 1;
                    if (QuickSongListGrid != null)
                    {
                        QuickSongListGrid.ItemsSource = null;
                        QuickSongListGrid.Visibility = Visibility.Collapsed;
                    }
                    if (QuickSongCountText != null) QuickSongCountText.Visibility = Visibility.Collapsed;
                    return;
                }

                // Determine matching strategy based on method
                bool useContains = _currentQuickMethod == QuickMethod.EngNum || _currentQuickMethod == QuickMethod.Keyboard;
                IEnumerable<T> Match<T>(IEnumerable<T> source, Func<T, string> getField) => source.Where(s =>
                {
                    try
                    {
                        var f = getField(s);
                        if (string.IsNullOrEmpty(f)) return false;
                        return useContains
                            ? f.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0
                            : f.StartsWith(key, StringComparison.OrdinalIgnoreCase);
                    }
                    catch { return false; }
                });

                Func<List<SongDisplayItem>> search;

                if (_searchMode == SearchMode.Youtube)
                {
                    if (_currentQuickMethod == QuickMethod.YoutubeHistory)
                    {
                        AppLogger.Log("QuickSearch: Triggered YoutubeHistory search mode.");
                        var history = SongDatas.GetYoutubeHistory();
                        AppLogger.Log($"QuickSearch: Received {history.Count} history items from SongDatas.");
                        var historyResults = history.Select(h => {
                            string url = h.TryGetValue("Url", out var u) ? u?.ToString() ?? "" : "";
                            string id = h.TryGetValue("Id", out var iObj) ? iObj?.ToString() ?? "" : url;
                            int lengthSeconds = h.TryGetValue("Length", out var lenObj) && int.TryParse(lenObj?.ToString(), out int l) ? l : 0;
                            string downloadDir = SettingsManager.Instance.CurrentSettings.YoutubeDownloadDir ?? "";
                            string rawPath = h.TryGetValue("FileName", out var fn) && fn != null
                                           ? System.IO.Path.Combine(downloadDir, fn.ToString() ?? "") : "";
                            if (!string.IsNullOrEmpty(rawPath) && !System.IO.Path.IsPathRooted(rawPath))
                            {
                                rawPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, rawPath);
                            }

                            return new SongDisplayItem
                            {
                                SongId = id,
                                SongName = h.TryGetValue("Name", out var n) ? n?.ToString() ?? "" : "",
                                SingerName = lengthSeconds > 0 ? TimeSpan.FromSeconds(lengthSeconds).ToString(@"hh\:mm\:ss") : Loc.T("Youtube.UnknownLength"),
                                Language = h.TryGetValue("Lang", out var lang) ? lang?.ToString() ?? "" : "",
                                FilePath = rawPath,
                                IsYoutube = true,
                                YoutubeId = url,
                                Volume = h.TryGetValue("Volume", out var v) && int.TryParse(v?.ToString(), out int iv) ? iv : 30,
                                ThumbnailUrl = $"https://i.ytimg.com/vi/{url}/hqdefault.jpg",
                                Duration = SongDisplayItem.FormatDuration(lengthSeconds)
                            };
                        }).ToList();

                        if (!string.IsNullOrEmpty(key))
                        {
                            historyResults = historyResults.Where(r => r.SongName.Contains(key, StringComparison.OrdinalIgnoreCase)).ToList();
                        }
                        
                        _quickMethodResultsCache[_currentQuickMethod] = historyResults;
                        RefreshQuickResultsPage();
                        return;
                    }

                    // === YOUTUBE SEARCH MODE ===
                    if (YoutubeStatusText != null)
                    {
                        YoutubeStatusText.Text = Loc.T("Youtube.Searching");
                        YoutubeStatusText.Visibility = Visibility.Visible;
                    }

                    // Use YoutubeExplode to search for videos
                    var youtubeResults = await _youtube.Search.GetVideosAsync(key).CollectAsync(SettingsManager.Instance.CurrentSettings.YoutubeSearchCount);
                    var results = youtubeResults.Select(v => {
                        string durationStr = "";
                        if (v.Duration.HasValue)
                        {
                            var d = v.Duration.Value;
                            durationStr = $"{(int)d.TotalMinutes}:{d.Seconds:D2}";
                        }

                        return new SongDisplayItem
                        {
                            SongId = v.Id.Value,
                            SongName = v.Title,
                            SingerName = durationStr, // Show duration in Singer column as requested
                            Language = "",            // Roll back Language column usage
                            FilePath = v.Url,
                            IsYoutube = true,
                            Volume = 30,
                            ThumbnailUrl = v.Thumbnails.OrderByDescending(t => t.Resolution.Area).FirstOrDefault()?.Url,
                            Duration = v.Duration.HasValue ? SongDisplayItem.FormatDuration(v.Duration.Value.TotalSeconds) : ""
                        };
                    }).ToList();

                    _quickMethodResultsCache[_currentQuickMethod] = results;
                    _youtubeSearchKeyword = key;
                    _youtubeSearchResults = results;
                    _youtubeSearchPage = 1;

                    if (YoutubeStatusText != null) YoutubeStatusText.Visibility = Visibility.Collapsed;

                    RefreshQuickResultsPage();
                    return;
                }
                else if (_searchMode == SearchMode.Singer)
                {
                    // === SINGER SEARCH MODE ===
                    if (SongDatas.SingerData == null)
                    {
                        _quickMethodResultsCache[_currentQuickMethod] = new List<SongDisplayItem>();
                        RefreshQuickResultsPage();
                        return;
                    }

                    var singers = SongDatas.SingerData;
                    // Default fallback
                    Func<Dictionary<string, object?>, string> getField = s => s.TryGetValue("Singer_Name", out var v) ? v?.ToString() ?? string.Empty : string.Empty;

                    switch (_currentQuickMethod)
                    {
                        case QuickMethod.Bopomofo:
                            // Attempt to use Singer_Spell (Bopomofo) if available, otherwise name?
                            // CAUTION: If Singer_Spell doesn't exist, this will return empty and match nothing.
                            // We assume standard field naming convention.
                            getField = s => s.TryGetValue("Singer_Spell", out var v) ? v?.ToString() ?? string.Empty : string.Empty;
                            break;
                        case QuickMethod.PenStyle:
                            getField = s => s.TryGetValue("Singer_PenStyle", out var v) ? v?.ToString() ?? string.Empty : string.Empty;
                            break;
                        case QuickMethod.EngNum:
                            // Check name for english/num match
                            getField = s => s.TryGetValue("Singer_Name", out var v) ? v?.ToString() ?? string.Empty : string.Empty;
                            break;
                        case QuickMethod.SongId:
                             // Maybe search by Singer_Id?
                            getField = s => s.TryGetValue("Singer_Id", out var v) ? v?.ToString() ?? string.Empty : string.Empty;
                            break;
                        case QuickMethod.Keyboard:
                             getField = s => s.TryGetValue("Singer_Name", out var v) ? v?.ToString() ?? string.Empty : string.Empty;
                             break;
                    }

                    // Map Singer Data to SongDisplayItem for display
                    // SongName column -> Singer Name
                    // SingerName column -> "歌手" (Static text or count if available?)
                    // We'll put "歌手" for now to indicate it represents a singer object
                    search = () => Match(singers, getField).Select(s => new SongDisplayItem
                    {
                        SongName = s.TryGetValue("Singer_Name", out var n) ? n?.ToString() ?? string.Empty : string.Empty,
                        SingerName = Loc.T("Col.Singer"), // Indicator
                        // 歌星模式沒有語言，借用此欄存放 Singer_Type (歌星按鈕格依類型上色)
                        Language = s.TryGetValue("Singer_Type", out var t) ? t?.ToString() ?? "" : "",
                        FilePath = "", // Not playable
                        SongId = s.TryGetValue("Singer_Id", out var id) ? id?.ToString() ?? "" : "",
                        // Other fields default
                    }).ToList();
                }
                else
                {
                    // === SONG SEARCH MODE (Existing Logic) ===
                    var songs = SongDatas.SongData ?? new List<Song>();
                    Func<Song, string> getField = _currentQuickMethod switch
                    {
                        QuickMethod.Bopomofo => song => song.Spell,
                        QuickMethod.PenStyle => song => song.PenStyle,
                        QuickMethod.SongId => song => song.Id,
                        // EngNum: fallback to name contains for now
                        _ => song => song.Name,
                    };
                    search = () => Match(songs, getField).Select(SongDisplayItem.FromSong).ToList();
                }

                // Perform the expensive filtering and mapping on a background thread.
                var matched = await Task.Run(() =>
                {
                    if (cancellationToken.IsCancellationRequested) return new List<SongDisplayItem>();

                    var results = search();

                    // Apply sorting based on settings (using centralized method)
                    return SongDatas.ApplySongSorting(results);
                }, cancellationToken);

                // Cache and compute paging (8 per grid page, 16 per youtube page)
                _quickMethodResultsCache[_currentQuickMethod] = matched;
                int total = _quickMethodResultsCache[_currentQuickMethod].Count;
                var _quickTotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)QuickSearchPageSize));
                if (_quickMethodCurrentPage[_currentQuickMethod] > _quickTotalPages) _quickMethodCurrentPage[_currentQuickMethod] = _quickTotalPages;
                if (_quickMethodCurrentPage[_currentQuickMethod] < 1) _quickMethodCurrentPage[_currentQuickMethod] = 1;
            }
            catch (Exception)
            {
                // ignore update errors
            }
        }

        /// <summary>
        /// 歌星搜尋結果按鈕格：點擊歌手進入其歌曲列表
        /// </summary>
        private void QuickSingerButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is string singerName && !string.IsNullOrEmpty(singerName))
            {
                OpenSingerFromQuickSearch(singerName);
            }
        }

        /// <summary>
        /// 從歌星搜尋結果選取歌手：載入該歌手歌曲並切換到歌曲列表
        /// </summary>
        private void OpenSingerFromQuickSearch(string singerName)
        {
            Debug.WriteLine($"[SingerSearch] Selected singer: {singerName}");
            _selectedSinger = singerName;
            _isLanguageMode = false;
            LoadSongsForSinger(singerName);

            // Switch view
            ShowQuickInputPanels(false);
            if (LanguageSongListGrid != null) LanguageSongListGrid.Visibility = Visibility.Collapsed;
            if (LanguageSecondFilterGrid != null) LanguageSecondFilterGrid.Visibility = Visibility.Collapsed;
            if (LanguageWordCountFilterGrid != null) LanguageWordCountFilterGrid.Visibility = Visibility.Collapsed;
            if (SingerGrid != null) SingerGrid.Visibility = Visibility.Collapsed;
            if (VisualSingerGrid != null) VisualSingerGrid.Visibility = Visibility.Collapsed;
            if (SingerSongContentGrid != null) SingerSongContentGrid.Visibility = Visibility.Visible;
            if (SongListGrid != null) SongListGrid.Visibility = Visibility.Visible;
            SetupFilterButtons(MainFilterMode.Singer);

            ClearSongGridsSelection();
        }

        // Refresh the current page view using the existing cache
        private void RefreshQuickResultsPage()
        {
            try
            {
                var currentCache = _quickMethodResultsCache.GetValueOrDefault(_currentQuickMethod, new List<SongDisplayItem>());
                var currentPage = _quickMethodCurrentPage.GetValueOrDefault(_currentQuickMethod, 1);

                int total = currentCache.Count;
                var _quickTotalPages = 1;
                _quickTotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)QuickSearchPageSize));
                if (currentPage > _quickTotalPages) currentPage = _quickTotalPages;
                if (currentPage < 1) currentPage = 1;

                var pageItems = currentCache
                    .Skip((currentPage - 1) * QuickSearchPageSize)
                    .Take(QuickSearchPageSize)
                    .ToList();

                bool useYoutubeGrid = (_searchMode == SearchMode.Youtube && _currentQuickMethod != QuickMethod.YoutubeHistory);
                if (useYoutubeGrid && ReferenceEquals(currentCache, _youtubeSearchResults))
                {
                    _youtubeSearchPage = currentPage; // remember the page for when the user comes back
                }

                if (YoutubeThumbnailGrid != null)
                {
                    if (useYoutubeGrid)
                    {
                        // 固定格子只更新內容；縮圖在背景解碼 (直接綁網址會在 UI 執行緒解碼原尺寸大圖，換頁時跑馬燈卡頓)
                        StopAllYoutubePreviews();
                        // 最後一頁不滿 16 筆時補空項目 (空格在 XAML 隱藏)，格子數不變就不會拆掉/重建
                        var gridItems = new List<SongDisplayItem>(pageItems);
                        while (gridItems.Count < QuickSearchPageSize) gridItems.Add(new SongDisplayItem());
                        ShowPageItems(YoutubeThumbnailGrid, _youtubeGridPageItems, gridItems);
                        LoadYoutubeThumbnails(_youtubeGridPageItems);
                        YoutubeThumbnailCache.Prefetch(currentCache
                            .Skip(currentPage * QuickSearchPageSize)
                            .Take(QuickSearchPageSize)
                            .Select(i => i.ThumbnailUrl));
                    }
                    YoutubeThumbnailGrid.Visibility = useYoutubeGrid ? Visibility.Visible : Visibility.Collapsed;
                }

                if (_searchMode == SearchMode.Youtube)
                {
                    if (QuickResultsContainer != null) QuickResultsContainer.Visibility = Visibility.Visible;
                    if (useYoutubeGrid)
                    {
                        if (QuickSongListGrid != null) QuickSongListGrid.Visibility = Visibility.Collapsed;
                    }
                    if (SingerSongContentGrid != null) SingerSongContentGrid.Visibility = Visibility.Collapsed;
                }
                else
                {
                    // Hide primary content when search results exist to prevent bleeding underneath
                    if (SingerSongContentGrid != null) SingerSongContentGrid.Visibility = (pageItems.Any()) ? Visibility.Collapsed : Visibility.Visible;
                }

                if (QuickSongListGrid != null)
                {
                    ApplyQuickSongListLayout();
                    if (!useYoutubeGrid)
                    {
                        // 歌星搜尋時列出的是歌手，不顯示插播/最愛；Youtube 歌曲本來就不顯示插播/最愛按鈕，整欄隱藏
                        bool isYoutubeList = _searchMode == SearchMode.Youtube;
                        QuickActionsColumn.Visibility = (_isSingerSearchMode || isYoutubeList) ? Visibility.Collapsed : Visibility.Visible;
                        // Youtube 列表不需要語言與操作欄，歌名欄固定 1100，歌曲時間欄取剩餘寬度 (約 200)
                        QuickLanguageColumn.Visibility = isYoutubeList ? Visibility.Collapsed : Visibility.Visible;
                        double songNameWidth = isYoutubeList ? 1100 : ((DataGridLength)FindResource("SongNameColumnWidth")).Value;
                        QuickSongNameColumn.Width = new DataGridLength(songNameWidth);
                        // 就地換頁：沿用既有的 DataGridRow，避免每次換頁重建所有列
                        ShowPageItems(QuickSongListGrid, _quickSongListPageItems, pageItems);
                    }
                    else
                    {
                        QuickSongListGrid.ItemsSource = null;
                    }
                    if (!useYoutubeGrid && !_isSingerSearchMode)
                    {
                        QuickSongListGrid.Visibility = pageItems.Any() ? Visibility.Visible : Visibility.Collapsed;
                    }
                    else
                    {
                        QuickSongListGrid.Visibility = Visibility.Collapsed;
                    }
                }

                // 歌星搜尋：改用每排 4 位的按鈕格 (SongName 欄位存放的是歌手名稱)
                if (QuickSingerGrid != null)
                {
                    bool showSingerGrid = _isSingerSearchMode && pageItems.Any();
                    QuickSingerGrid.ItemsSource = showSingerGrid ? pageItems.Select(s => QuickSingerItem.Create(s.SongName, s.Language)).ToList() : null;
                    QuickSingerGrid.Visibility = showSingerGrid ? Visibility.Visible : Visibility.Collapsed;
                }

                // Ensure container is visible if YT results or standard results are shown
                if (QuickResultsContainer != null)
                {
                    if (_searchMode == SearchMode.Youtube) QuickResultsContainer.Visibility = Visibility.Visible;
                    else QuickResultsContainer.Visibility = (pageItems.Any()) ? Visibility.Visible : Visibility.Collapsed;
                }
                if (QuickSongCountText != null)
                {
                    if (total > 0)
                    {
                        QuickSongCountText.Text = Loc.T(_isSingerSearchMode ? "Search.CountSingers" : "Search.CountSongs", currentPage, _quickTotalPages, total);
                        QuickSongCountText.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        QuickSongCountText.Text = string.Empty;
                        QuickSongCountText.Visibility = Visibility.Collapsed;
                    }
                }

                // Update bottom global pagination bar with search results status
                if (PageInfoTextBlock != null)
                {
                    PageInfoTextBlock.Text = total > 0 ? Loc.T(_isSingerSearchMode ? "Page.SearchSingers" : "Page.SearchSongs", currentPage, _quickTotalPages, total) : Loc.T("Page.First");
                }
                if (PageUp != null) PageUp.IsEnabled = currentPage > 1;
                if (PageDown != null) PageDown.IsEnabled = currentPage < _quickTotalPages;
            }
            catch (Exception)
            {
                // ignore update errors
            }
        }

        private void QuickPageUp_Click(object sender, RoutedEventArgs e)
        {
            var currentPage = _quickMethodCurrentPage.GetValueOrDefault(_currentQuickMethod, 1);
            if (currentPage > 1)
            {
                _quickMethodCurrentPage[_currentQuickMethod]--;
                RefreshQuickResultsPage();
            }
        }

        private void QuickPageDown_Click(object sender, RoutedEventArgs e)
        {
            var currentPage = _quickMethodCurrentPage.GetValueOrDefault(_currentQuickMethod, 1);
            var currentCache = _quickMethodResultsCache.GetValueOrDefault(_currentQuickMethod, new List<SongDisplayItem>());
            var totalPages = Math.Max(1, (int)Math.Ceiling(currentCache.Count / (double)QuickSearchPageSize));
            if (currentPage < totalPages)
            {
                _quickMethodCurrentPage[_currentQuickMethod]++;
                RefreshQuickResultsPage();
            }
        }

        /// <summary>
        /// Handles keyboard input for QuickSearch based on the active method.
        /// Returns true if the key was handled.
        /// </summary>
        private bool HandleQuickSearchKeyDown(Key key)
        {
            // Only handle for EngNum and SongId as requested
            if (_currentQuickMethod != QuickMethod.EngNum && _currentQuickMethod != QuickMethod.SongId)
                return false;

            string? inputChar = null;

            if (_currentQuickMethod == QuickMethod.EngNum)
            {
                // A-Z
                if (key >= Key.A && key <= Key.Z)
                {
                    inputChar = key.ToString();
                }
                // 0-9 (Top row)
                else if (key >= Key.D0 && key <= Key.D9)
                {
                    inputChar = (key - Key.D0).ToString();
                }
                // 0-9 (Numpad)
                else if (key >= Key.NumPad0 && key <= Key.NumPad9)
                {
                    inputChar = (key - Key.NumPad0).ToString();
                }
                // Space
                else if (key == Key.Space)
                {
                    inputChar = " ";
                }
            }
            else if (_currentQuickMethod == QuickMethod.SongId)
            {
                // 0-9 (Top row)
                if (key >= Key.D0 && key <= Key.D9)
                {
                    inputChar = (key - Key.D0).ToString();
                }
                // 0-9 (Numpad)
                else if (key >= Key.NumPad0 && key <= Key.NumPad9)
                {
                    inputChar = (key - Key.NumPad0).ToString();
                }
            }

            if (inputChar != null)
            {
                AddQuickSearchWord(inputChar);
                return true;
            }

            // Optional: Handle Backspace for better UX
            if (key == Key.Back)
            {
                DeleteEnteredWordBtn_Click(null!, null!);
                return true;
            }

            return false;
        }

        private void AddQuickSearchWord(string word)
        {
            var currentWords = _quickMethodSelectedWords[_currentQuickMethod];
            
            // Validation: Whitespace check
            if (string.IsNullOrWhiteSpace(word))
            {
                // Only allowed in EngNum mode
                if (_currentQuickMethod != QuickMethod.EngNum) return;
                
                // Never allowed as the first character
                if (currentWords.Count == 0) return;
            }

            if (currentWords.Count >= 10) return; // limit to max 10
            currentWords.Add(word);
            UpdateSearchWords();
        }

        /// <summary>
        /// 填入縮圖格的圖：快取中有的立即顯示，其餘在背景下載解碼後再填入
        /// (期間格子已換成別的影片則不填)。
        /// </summary>
        private static void LoadYoutubeThumbnails(IEnumerable<SongDisplayItem> slots)
        {
            foreach (var slot in slots)
            {
                string? url = slot.ThumbnailUrl;
                if (string.IsNullOrEmpty(url))
                {
                    slot.ThumbnailImage = null;
                    continue;
                }
                if (YoutubeThumbnailCache.TryGet(url, out var cached))
                {
                    slot.ThumbnailImage = cached;
                    continue;
                }

                slot.ThumbnailImage = null;
                _ = FillAsync(slot, url);
            }

            static async Task FillAsync(SongDisplayItem slot, string url)
            {
                var image = await YoutubeThumbnailCache.GetAsync(url); // 回到 UI 執行緒
                if (image != null && slot.ThumbnailUrl == url) slot.ThumbnailImage = image;
            }
        }
    }
}