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

        // Exclusive selector for the 4 search type buttons
        private enum QuickMethod { Bopomofo, EngNum, PenStyle, SongId, Keyboard, Extra5, YoutubeHistory }
        private QuickMethod _currentQuickMethod = QuickMethod.Bopomofo;

        // Toggle state for Song vs Singer vs Youtube search
        private enum SearchMode { Song, Singer, Youtube }
        private SearchMode _searchMode = SearchMode.Song;
        private bool _isSingerSearchMode => _searchMode == SearchMode.Singer;
        
        // Centralized page size for Quick Search
        private int QuickSearchPageSize => (_searchMode == SearchMode.Youtube) ? (_currentQuickMethod == QuickMethod.YoutubeHistory ? 15 : 16) : 10;

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
            if (IsDownloadingYoutube)
            {
                _youtubeDownloadCts?.Cancel();
                return;
            }

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
                    var nameOnly = System.IO.Path.GetFileNameWithoutExtension(file);
                    var item = new SongDisplayItem
                    {
                        SongName = nameOnly,
                        FilePath = file,
                        SongId = "", // Url field stays blank
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
                        var msg = $"找到 {newFiles.Count} 首未記錄的本機 YouTube 歌曲檔案，是否要把這些歌新增到 Youtube 記錄裏？";
                        result = MessageBox.Show(msg, "新增記錄", MessageBoxButton.YesNo, MessageBoxImage.Question);
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
            }
            else
            {
                SetQuickMethod(QuickMethod.YoutubeHistory, sender as Button);
            }
        }

        private void ClearYoutubeBtn_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("確定要清除所有 Youtube 點播記錄與已下載的檔案嗎？", "確認清除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                // Delete from DB
                try
                {
                    string dbPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CrazySong.mdb");
                    string sql = "DELETE FROM YoutubeSongList";
                    DbHelper.Access.ExecuteNonQuery(dbPath, sql, null);
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
        private async void SearchMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton radio)
            {
                if (radio == YoutubeRadio)
                {
                    _ = YtDlpHelper.EnsureYtDlpAsync();

                    if (!_hasCheckedYoutubeHistoryFiles)
                    {
                        _hasCheckedYoutubeHistoryFiles = true;
                        await CheckAndImportLocalYoutubeFiles();
                    }

                    // Clear search keywords when entering YouTube mode IMMEDIATELY
                    if (SearchWords != null) SearchWords.Text = string.Empty;
                    _quickMethodSelectedWords[_currentQuickMethod].Clear();

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
                    if (SingerSongContentGrid != null) SingerSongContentGrid.Visibility = Visibility.Collapsed;
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
                var activeBg = (System.Windows.Media.Brush)FindResource("SingerButtonBackground");
                var activeFg = System.Windows.Media.Brushes.White;
                var inactiveBg = System.Windows.Media.Brushes.Transparent;
                var inactiveFg = (System.Windows.Media.Brush)FindResource("SingerButtonBackground");

                var buttons = new[] { BopomofoListBtn, EngAndNumListBtn, PenStyleListBtn, SongIdListBtn, KeyboardListBtn, YoutubeHistoryBtn };
                foreach (var btn in buttons)
                {
                    if (btn == null) continue;
                    bool isActive = btn == clicked;
                    btn.FontWeight = isActive ? FontWeights.Bold : FontWeights.Normal;
                    btn.Opacity = isActive ? 1.0 : 0.8;
                    btn.Background = isActive ? activeBg : inactiveBg;
                    btn.Foreground = isActive ? activeFg : inactiveFg;
                }

                // Restore visibility after updates
                if (SearchInputGrid != null) SearchInputGrid.Visibility = (method == QuickMethod.Keyboard || _searchMode == SearchMode.Youtube) ? Visibility.Collapsed : Visibility.Visible;
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
        private async void UpdateSearchWords(bool triggerSearch = true)
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
                    
                    _quickMethodCurrentPage[_currentQuickMethod] = 1; // reset to first page on query change
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
                        RefreshQuickResultsPage();
                    }
                    else
                    {
                        if (QuickSongListGrid != null) QuickSongListGrid.Visibility = Visibility.Collapsed;
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

                IEnumerable<Dictionary<string, object?>> query;
                Func<Dictionary<string, object?>, string> getField;

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
                                SingerName = lengthSeconds > 0 ? TimeSpan.FromSeconds(lengthSeconds).ToString(@"hh\:mm\:ss") : "未知長度",
                                Language = h.TryGetValue("Lang", out var lang) ? lang?.ToString() ?? "" : "",
                                FilePath = rawPath,
                                IsYoutube = true,
                                YoutubeId = url,
                                Volume = h.TryGetValue("Volume", out var v) && int.TryParse(v?.ToString(), out int iv) ? iv : 30,
                                ThumbnailUrl = $"https://i.ytimg.com/vi/{url}/hqdefault.jpg"
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
                        YoutubeStatusText.Text = "YouTube 搜尋中...";
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
                            ThumbnailUrl = v.Thumbnails.OrderByDescending(t => t.Resolution.Area).FirstOrDefault()?.Url
                        };
                    }).ToList();

                    _quickMethodResultsCache[_currentQuickMethod] = results;
                    
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

                    query = SongDatas.SingerData;
                    // Default fallback
                    getField = s => s.TryGetValue("Singer_Name", out var v) ? v?.ToString() ?? string.Empty : string.Empty;

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
                }
                else
                {
                    // === SONG SEARCH MODE (Existing Logic) ===
                    query = SongDatas.SongData;
                    getField = song => song.TryGetValue("Song_SongName", out var v) ? v?.ToString() ?? string.Empty : string.Empty;
                    switch (_currentQuickMethod)
                    {
                        case QuickMethod.Bopomofo:
                            getField = song => song.TryGetValue("Song_Spell", out var v) ? v?.ToString() ?? string.Empty : string.Empty;
                            break;
                        case QuickMethod.PenStyle:
                            getField = song => song.TryGetValue("Song_PenStyle", out var v) ? v?.ToString() ?? string.Empty : string.Empty;
                            break;
                        case QuickMethod.EngNum:
                            // fallback to name contains for now
                            getField = song => song.TryGetValue("Song_SongName", out var v) ? v?.ToString() ?? string.Empty : string.Empty;
                            break;
                        case QuickMethod.SongId:
                            getField = song => song.TryGetValue("Song_Id", out var v) ? v?.ToString() ?? string.Empty : string.Empty;
                            break;
                        case QuickMethod.Keyboard:
                            getField = song => song.TryGetValue("Song_SongName", out var v) ? v?.ToString() ?? string.Empty : string.Empty;
                            break;
                    }
                }

                // Determine matching strategy based on method
                bool useContains = _currentQuickMethod == QuickMethod.EngNum || _currentQuickMethod == QuickMethod.Keyboard;

                // Perform the expensive filtering and mapping on a background thread.
                var matched = await Task.Run(() =>
                {
                    if (cancellationToken.IsCancellationRequested) return new List<SongDisplayItem>();

                    var results = query
                        .Where(s =>
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
                        })

                        .Select(s => 
                        {
                            if (_searchMode == SearchMode.Singer)
                            {
                                // Map Singer Data to SongDisplayItem for display
                                // SongName column -> Singer Name
                                // SingerName column -> "歌手" (Static text or count if available?)
                                // We'll put "歌手" for now to indicate it represents a singer object
                                return new SongDisplayItem
                                {
                                    SongName = s.TryGetValue("Singer_Name", out var n) ? n?.ToString() ?? string.Empty : string.Empty,
                                    SingerName = "歌手", // Indicator
                                    Language = "", // Singer doesn't have language usually
                                    FilePath = "", // Not playable
                                    SongId = s.TryGetValue("Singer_Id", out var id) ? id?.ToString() ?? "" : "",
                                    // Other fields default
                                };
                            }
                            else
                            {
                                // Normal Song Mapping
                                return new SongDisplayItem
                                {
                                    SongId = s.TryGetValue("Song_Id", out var id) ? id?.ToString() ?? string.Empty : string.Empty,
                                    SongName = s.TryGetValue("Song_SongName", out var n) ? n?.ToString() ?? string.Empty : string.Empty,
                                    SingerName = s.TryGetValue("Song_Singer", out var sn) ? sn?.ToString() ?? string.Empty : string.Empty,
                                    Language = s.TryGetValue("Song_Lang", out var lg) ? lg?.ToString() ?? string.Empty : string.Empty,
                                    FilePath = s.TryGetValue("FilePath", out var fp) ? fp?.ToString() ?? "" : "",
                                    Song_WordCount = s.TryGetValue("Song_WordCount", out var wcObj) && int.TryParse(wcObj?.ToString(), out int wc) ? wc : 0,
                                    Song_PlayCount = s.TryGetValue("Song_PlayCount", out var pcObj) && int.TryParse(pcObj?.ToString(), out int pc) ? pc : 0,
                                    Song_CreatDate = s.TryGetValue("Song_CreatDate", out var dateObj) && dateObj is DateTime dt ? dt : (DateTime?)null,
                                    Volume = s.TryGetValue("Song_Volume", out var vol)
                                              && int.TryParse(vol?.ToString(), out var iv)
                                              ? iv
                                              : (s.TryGetValue("Song_Volume", out var vol2) && double.TryParse(vol2?.ToString(), out var dv)
                                                    ? (int)Math.Round(dv)
                                                    : 0),
                                    AudioTrack = s.TryGetValue("Song_Track", out var tr) && int.TryParse(tr?.ToString(), out var it) ? it : 0,
                                };
                            }
                        })
                        .ToList();
                    
                    // Apply sorting based on settings (using centralized method)
                    return SongDatas.ApplySongSorting(results, (s, key) => s.GetType().GetProperty(key)?.GetValue(s, null));
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

                if (YoutubeThumbnailGrid != null)
                {
                    YoutubeThumbnailGrid.ItemsSource = useYoutubeGrid ? pageItems : null;
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
                    // Restore primary content visibility when not in YouTube mode
                    if (SingerSongContentGrid != null) SingerSongContentGrid.Visibility = Visibility.Visible;
                }

                if (QuickSongListGrid != null)
                {
                    QuickSongListGrid.ItemsSource = !useYoutubeGrid ? pageItems : null;
                    if (!useYoutubeGrid)
                    {
                        QuickSongListGrid.Visibility = pageItems.Any() ? Visibility.Visible : Visibility.Collapsed;
                    }
                    else
                    {
                        QuickSongListGrid.Visibility = Visibility.Collapsed;
                    }
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
                        QuickSongCountText.Text = $"第 {currentPage} / {_quickTotalPages} 頁 · 共 {total} 首";
                        QuickSongCountText.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        QuickSongCountText.Text = string.Empty;
                        QuickSongCountText.Visibility = Visibility.Collapsed;
                    }
                }
                if (QuickPageUpBtn != null) QuickPageUpBtn.IsEnabled = currentPage > 1;
                if (QuickPageDownBtn != null) QuickPageDownBtn.IsEnabled = currentPage < _quickTotalPages;
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
    }
}