using System;
using UltimateKtv.Playback;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace UltimateKtv
{
    public partial class MainWindow
    {
        /// <summary>
        /// Loads a specific page of singers into the SingerGrid.
        /// </summary>
        /// <param name="page">The page number to display.</param>
        private void LoadSingerPage(int page)
        {
            // Use LINQ to get the correct subset of singers for the page
            var singerNamesForPage = _allSingers.Skip((page - 1) * PageSize).Take(PageSize).ToList();

            var settings = SettingsManager.Instance.CurrentSettings;
            // AppLogger.Log($"LoadSingerPage: VisualStyleSetting={settings.VisualSingerStyle}, Effective={IsVisualSingerStyleEffective}, NamesCount={singerNamesForPage.Count}");

            if (IsVisualSingerStyleEffective)
            {
                FillVisualSingerSlots(singerNamesForPage);

                if (!ReferenceEquals(VisualSingerGrid.ItemsSource, _visualSingerSlots))
                {
                    VisualSingerGrid.ItemsSource = _visualSingerSlots;
                }
                VisualSingerGrid.Visibility = Visibility.Visible;
                SingerGrid.Visibility = Visibility.Collapsed;

                // 背景預載「翻頁方向」的下一頁縮圖，翻頁時可以直接從快取顯示。
                // 只預載一個方向：每多解碼一張圖，WPF 就多累積一份記憶體壓力，
                // 累積到門檻會強制完整 GC (暫停 UI 執行緒，跑馬燈掉格)。
                int direction = page < _lastSingerPage ? -1 : 1;
                _lastSingerPage = page;
                PrefetchSingerPagePhotos(page + direction);
            }
            else
            {
                SingerGrid.ItemsSource = singerNamesForPage;
                SingerGrid.Visibility = Visibility.Visible;
                VisualSingerGrid.Visibility = Visibility.Collapsed;
            }

            // Update the page information TextBlock
            // 最愛頁：清單是使用者/歌單，且含補位空格，只計實際項目
            PageInfoTextBlock.Text = _currentFilterKey == "FavoriteUsers"
                ? Loc.T("Page.SingerItems", page, _totalPages, _allSingers.Count(n => !string.IsNullOrEmpty(n)))
                : Loc.T("Page.Singers", page, _totalPages, _allSingers.Count);

            // Enable or disable the page buttons based on the current page
            PageUp.IsEnabled = (page > 1);
            PageDown.IsEnabled = (page < _totalPages);
        }

        #region Visual singer grid (async photo loading)

        // 固定的一組格子：翻頁時只更新內容，不重建 ItemsControl 的容器與樣板
        private readonly ObservableCollection<SingerDisplayItem> _visualSingerSlots = new ObservableCollection<SingerDisplayItem>();

        // 每次載入頁面時遞增，用來丟棄已過期的背景載入結果
        private int _singerPageVersion;

        private void FillVisualSingerSlots(List<string> names)
        {
            int version = ++_singerPageVersion;
            int slotCount = Math.Max(PageSize, names.Count);

            while (_visualSingerSlots.Count < slotCount) _visualSingerSlots.Add(new SingerDisplayItem());
            while (_visualSingerSlots.Count > slotCount) _visualSingerSlots.RemoveAt(_visualSingerSlots.Count - 1);

            for (int i = 0; i < _visualSingerSlots.Count; i++)
            {
                var slot = _visualSingerSlots[i];
                string name = i < names.Count ? names[i] : string.Empty;

                if (string.IsNullOrEmpty(name))
                {
                    // 空白格子 (樣板中的 DataTrigger 會把 Name 為空的按鈕隱藏)
                    slot.Name = string.Empty;
                    slot.PhotoPath = null;
                    slot.HasPhoto = false;
                    slot.Photo = null;
                    continue;
                }

                // 只做字典查詢，不做檔案 I/O
                string? localPath = SingerPhotoManager.GetPhotoLocalPath(name);

                slot.Name = name;
                slot.PhotoPath = localPath;
                slot.HasPhoto = localPath != null;

                if (localPath == null)
                {
                    slot.Photo = SingerThumbnailCache.DefaultImage;
                }
                else if (SingerThumbnailCache.TryGet(localPath, out var cached))
                {
                    slot.Photo = cached;
                }
                else
                {
                    slot.Photo = null; // 先顯示空白底色 + 名字，解碼完成後再補上
                    _ = LoadSlotPhotoAsync(slot, localPath, version);
                }
            }
        }

        private async Task LoadSlotPhotoAsync(SingerDisplayItem slot, string localPath, int version)
        {
            try
            {
                var image = await SingerThumbnailCache.GetAsync(localPath);

                // 已經翻到別頁 / 這個格子已換成別的歌手，就不要套用
                if (version != _singerPageVersion || !string.Equals(slot.PhotoPath, localPath, StringComparison.OrdinalIgnoreCase))
                    return;

                slot.Photo = image ?? SingerThumbnailCache.DefaultImage;
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"LoadSlotPhotoAsync failed: {localPath}", ex);
            }
        }

        private int _lastSingerPage;

        private void PrefetchSingerPagePhotos(int page)
        {
            if (page < 1 || page > _totalPages) return;

            var paths = _allSingers.Skip((page - 1) * PageSize).Take(PageSize)
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => SingerPhotoManager.GetPhotoLocalPath(n))
                .Where(p => p != null)
                .Select(p => p!)
                .ToList();

            SingerThumbnailCache.Prefetch(paths);
        }

        #endregion

        #region In-place paging for DataGrids (fixed row slots)

        // 每個分頁列表持有一組固定的「列 (slot)」物件作為 ItemsSource。
        // 換頁時只把新資料複製到這些 slot 的屬性 (INotifyPropertyChanged)，
        // DataGrid 不會收到任何集合變更，因此不會重建 DataGridRow / Cell / 樣板，
        // 只會更新文字。這和歌星照片格的做法相同。
        //
        // 注意：從列表取出項目要往後傳遞時 (加入待播、加入最愛、長按) 請使用 Clone()，
        // 因為 slot 的內容在換頁時會被改掉。
        private readonly ObservableCollection<SongDisplayItem> _songListPageItems = new ObservableCollection<SongDisplayItem>();
        private readonly ObservableCollection<SongDisplayItem> _languageSongListPageItems = new ObservableCollection<SongDisplayItem>();
        private readonly ObservableCollection<SongDisplayItem> _quickSongListPageItems = new ObservableCollection<SongDisplayItem>();
        private readonly ObservableCollection<WaitingListItem> _waitingListPageItems = new ObservableCollection<WaitingListItem>();

        private static void ShowPageItems(DataGrid grid, ObservableCollection<SongDisplayItem> slots, IList<SongDisplayItem> items)
        {
            ShowPageItemsCore(grid, slots, items, (slot, src) => slot.CopyFrom(src));
        }

        private static void ShowPageItems(DataGrid grid, ObservableCollection<WaitingListItem> slots, IList<WaitingListItem> items)
        {
            ShowPageItemsCore(grid, slots, items, (slot, src) => slot.CopyFrom(src));
        }

        private static void ShowPageItemsCore<T>(DataGrid grid, ObservableCollection<T> slots, IList<T> items, Action<T, T> copy)
            where T : class, new()
        {
            // 只有最後一頁筆數不同時才會增減列
            while (slots.Count > items.Count)
            {
                slots.RemoveAt(slots.Count - 1);
            }

            for (int i = 0; i < items.Count; i++)
            {
                if (i < slots.Count)
                {
                    copy(slots[i], items[i]);
                }
                else
                {
                    var slot = new T();
                    copy(slot, items[i]);
                    slots.Add(slot);
                }
            }

            if (!ReferenceEquals(grid.ItemsSource, slots))
            {
                grid.ItemsSource = slots;
            }
        }

        /// <summary>
        /// 歌庫磁碟路徑變更後，把已建立的列表項目 (歌曲列表、分頁列、快搜結果、已點歌單) 換成新路徑，
        /// 不必重啟或重新切換頁面。oldToNew 來自 SongDatas.RebuildSongFilePaths()。
        /// </summary>
        public void RemapSongFilePaths(IReadOnlyDictionary<string, string> oldToNew)
        {
            if (oldToNew.Count == 0) return;

            string Remap(string path) => !string.IsNullOrEmpty(path) && oldToNew.TryGetValue(path, out var newPath) ? newPath : path;

            int count = 0;
            void RemapSongs(IEnumerable<SongDisplayItem> items)
            {
                foreach (var item in items)
                {
                    if (item.IsYoutube) continue;
                    string newPath = Remap(item.FilePath);
                    if (!ReferenceEquals(newPath, item.FilePath)) { item.FilePath = newPath; count++; }
                }
            }

            RemapSongs(_allSongs);
            RemapSongs(_songListPageItems);
            RemapSongs(_languageSongListPageItems);
            RemapSongs(_quickSongListPageItems);
            foreach (var cached in _quickMethodResultsCache.Values) RemapSongs(cached);
            count += _playback.RemapFilePaths(Remap);

            AppLogger.Log($"[SongDatas] Remapped {count} displayed/waiting song paths to new library path.");
        }

        #endregion

        private void PageUp_Click(object sender, RoutedEventArgs e)
        {
            if (SearchStyleBtnPanel?.Visibility == Visibility.Visible || QuickResultsContainer?.Visibility == Visibility.Visible)
            {
                QuickPageUp_Click(sender, e);
                return;
            }

            if (SongListGrid.Visibility == Visibility.Visible || LanguageSongListGrid.Visibility == Visibility.Visible)
            {
                // Handle song pagination
                if (_currentSongPage <= 1) return;
                _currentSongPage--;
                LoadSongPage(_currentSongPage);
            }
            else
            {
                // Handle singer pagination
                if (_currentPage <= 1) return;
                _currentPage--;
                LoadSingerPage(_currentPage);
            }
        }

        private void PageDown_Click(object sender, RoutedEventArgs e)
        {
            if (SearchStyleBtnPanel?.Visibility == Visibility.Visible || QuickResultsContainer?.Visibility == Visibility.Visible)
            {
                QuickPageDown_Click(sender, e);
                return;
            }

            if (SongListGrid.Visibility == Visibility.Visible || LanguageSongListGrid.Visibility == Visibility.Visible)
            {
                // Handle song pagination
                if (_currentSongPage >= _totalSongPages) return;
                _currentSongPage++;
                LoadSongPage(_currentSongPage);
            }
            else
            {
                // Handle singer pagination
                if (_currentPage >= _totalPages) return;
                _currentPage++;
                LoadSingerPage(_currentPage);
            }
        }

        /// <summary>
        /// Handles clicks on singer buttons in the SingerGrid
        /// </summary>
        private void SingerButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string name)
            {
                if (string.IsNullOrEmpty(name)) return; // Ignore clicks on padded empty buttons

//                AppLogger.Log($"User action: Singer button clicked - {name}");
                _selectedSinger = name;

                // Check if we're in "Favorite Users" mode
                if (_currentFilterKey == "FavoriteUsers")
                {
                    LoadFavoriteSongsForUser(name);
                }
                else
                {
                    LoadSongsForSinger(name);
                }

                // Toggle visibility: hide singer grid and show song list
                SingerGrid.Visibility = Visibility.Collapsed;
                SongListGrid.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// Loads a specific page of songs into the SongListGrid
        /// </summary>
        /// <param name="page">The page number to display</param>
        private void LoadSongPage(int page)
        {
            ClearSongGridsSelection();
            // Use LINQ to get the correct subset of songs for the page
            var songsForPage = _allSongs.Skip((page - 1) * CurrentSongPageSize).Take(CurrentSongPageSize).ToList();

            // Set the items of the correct DataGrid (in place, so existing rows are reused)
            if (_isLanguageMode)
            {
                ShowPageItems(LanguageSongListGrid, _languageSongListPageItems, songsForPage);
            }
            else
            {
                ShowPageItems(SongListGrid, _songListPageItems, songsForPage);
            }

            // Update the page information TextBlock
            PageInfoTextBlock.Text = Loc.T("Page.Songs", page, _totalSongPages, _currentSongsTitle, _allSongs.Count);

            // Enable or disable the page buttons based on the current page
            PageUp.IsEnabled = (page > 1);
            PageDown.IsEnabled = (page < _totalSongPages);
        }

        private void DisplaySongsInGrid(List<SongDisplayItem> songs, string title)
        {
            _currentSongsTitle = title;
            _isLanguageMode = false;
            _allSongs = songs;
            _totalSongPages = (_allSongs.Count == 0) ? 1 : (_allSongs.Count + CurrentSongPageSize - 1) / CurrentSongPageSize;
            _currentSongPage = 1;

            LoadSongPage(_currentSongPage);

            SongListGrid.Visibility = Visibility.Visible;
            LanguageSongListGrid.Visibility = Visibility.Collapsed;
            SingerGrid.Visibility = Visibility.Collapsed;
        }

        private void DisplayLanguageSongsInGrid(List<SongDisplayItem> songs, string title)
        {
            _currentSongsTitle = title;
            _isLanguageMode = true;
            _allSongs = songs;
            _totalSongPages = (_allSongs.Count == 0) ? 1 : (_allSongs.Count + CurrentSongPageSize - 1) / CurrentSongPageSize;
            _currentSongPage = 1;

            LoadSongPage(_currentSongPage);

            LanguageSongListGrid.Visibility = Visibility.Visible;
            SongListGrid.Visibility = Visibility.Collapsed;
            SingerGrid.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Loads all users from the ktv_User table and displays them in the SingerGrid
        /// </summary>
        private void LoadFavoriteUsers()
        {
            // 補空格對齊目前歌星格的欄數 (照片樣式 6 欄、文字樣式 4 欄)
            _allSingers = SongDatas.GetSortedFavoriteUsers(includePadding: true, columns: IsVisualSingerStyleEffective ? 6 : 4)
                                  .Select(u => u.User_Name)
                                  .ToList();
            
            // Store the current filter key for page state management
            if (!string.IsNullOrEmpty(_currentFilterKey))
            {
                _filterPageStates[_currentFilterKey] = _currentPage;
            }
            _currentFilterKey = "FavoriteUsers";

            // Calculate pagination
            _totalPages = (_allSingers.Count == 0) ? 1 : (_allSingers.Count + PageSize - 1) / PageSize;

            // Restore saved page or default to page 1
            if (_filterPageStates.TryGetValue(_currentFilterKey, out int savedPage) && savedPage <= _totalPages)
            {
                _currentPage = savedPage;
            }
            else
            {
                _currentPage = 1;
            }

            LoadSingerPage(_currentPage);
            ShowSingerGrid();
        }

        /// <summary>
        /// Loads favorite songs for the selected user
        /// </summary>
        /// <param name="userName">The name of the user to load favorites for</param>
        private void LoadFavoriteSongsForUser(string userName)
        {
            _currentSongsTitle = Loc.T("Title.UserFavorites", userName);
            if (SongDatas.FavoriteUserData == null || SongDatas.FavoriteSongData == null || SongDatas.SongData == null)
            {
                _allSongs = new List<SongDisplayItem>();
                SongListGrid.ItemsSource = _allSongs;
                return;
            }

            // Find the User_Id for the given userName
            var userRow = SongDatas.FavoriteUserData.AsEnumerable()
                .FirstOrDefault(row => row["User_Name"]?.ToString() == userName);

            if (userRow == null)
            {
                _allSongs = new List<SongDisplayItem>();
                SongListGrid.ItemsSource = _allSongs;
                return;
            }

            var userId = userRow["User_Id"]?.ToString() ?? "";
            if (string.IsNullOrEmpty(userId))
            {
                _allSongs = new List<SongDisplayItem>();
                SongListGrid.ItemsSource = _allSongs;
                return;
            }

            // Get all favorite songs for this user
            var favoriteSongs = SongDatas.FavoriteSongData
                .Where(fav => fav.TryGetValue("User_Id", out var uid) && uid?.ToString() == userId)
                .ToList();

            var songs = new List<SongDisplayItem>();

            foreach (var favorite in favoriteSongs)
            {
                // Get the Song_Id from the favorite record
                var songId = favorite.TryGetValue("Song_Id", out var sidObj) ? sidObj?.ToString() ?? "" : "";
                if (string.IsNullOrEmpty(songId)) continue;

                // Find the corresponding song in SongData
                var song = SongDatas.SongData.FirstOrDefault(s =>
                    s.TryGetValue("Song_Id", out var id) && id?.ToString() == songId);

                if (song == null) continue;

                var songItem = new SongDisplayItem
                {
                    SongId = songId,
                    SongName = song.TryGetValue("Song_SongName", out var nameObj) ? nameObj?.ToString() ?? "" : "",
                    SingerName = song.TryGetValue("Song_Singer", out var singerObj) ? singerObj?.ToString() ?? "" : "",
                    Language = song.TryGetValue("Song_Lang", out var langObj) ? langObj?.ToString() ?? "" : "",
                    FilePath = song.TryGetValue("FilePath", out var pathObj) ? pathObj?.ToString() ?? "" : "",
                    Song_WordCount = song.TryGetValue("Song_WordCount", out var wcObj) && int.TryParse(wcObj?.ToString(), out int wc) ? wc : 0,
                    Song_PlayCount = song.TryGetValue("Song_PlayCount", out var pcObj) && int.TryParse(pcObj?.ToString(), out int pc) ? pc : 0,
                    Song_CreatDate = song.TryGetValue("Song_CreatDate", out var dateObj) && dateObj is DateTime dt ? dt : (DateTime?)null,
                    Volume = song.TryGetValue("Song_Volume", out var volObj) && int.TryParse(volObj?.ToString(), out int vol) ? vol : 90,
                    AudioTrack = song.TryGetValue("Song_Track", out var trackObj) && int.TryParse(trackObj?.ToString(), out int track) ? track : 0
                };

                if (!string.IsNullOrEmpty(songItem.SongName))
                {
                    songs.Add(songItem);
                }
            }

            // Sort songs based on the selected sort method
            _allSongs = SongDatas.ApplySongSorting(songs, (s, key) => key switch
            {
                "Song_PlayCount" or "PlayCount" => s.Song_PlayCount,
                "Song_CreatDate" => s.Song_CreatDate,
                "Song_WordCount" => s.Song_WordCount,
                "Song_SongName" or "SongName" => s.SongName,
                _ => null
            });

            // Calculate pagination for songs
            _totalSongPages = (_allSongs.Count == 0) ? 1 : (_allSongs.Count + CurrentSongPageSize - 1) / CurrentSongPageSize;
            _currentSongPage = 1;

            // Load first page of songs
            LoadSongPage(_currentSongPage);
        }

        /// <summary>
        /// Clears all songs from User_Id "****" (today's play list)
        /// Called when the first song starts playing
        /// </summary>
        private void ClearTodayPlayList()
        {
            try
            {
                string userId = "****";

                // Delete all records for User_Id "****"
                Db.ExecuteNonQuery("DELETE FROM ktv_Favorite WHERE User_Id = ?", userId);
                AppLogger.Log("DB Write: Cleared today's play list (User_Id = ****)");
                DebugLog("Cleared today's play list (User_Id = ****)");

                // Reload the favorite data to keep it in sync
                SongDatas.ReloadFavoriteData();
            }
            catch (Exception ex)
            {
                DebugLog($"Error clearing today's play list: {ex.Message}");
                AppLogger.LogError("Failed to clear today's play list", ex);
            }
        }

        /// <summary>
        /// Records a song play to ktv_Favorite table with two User_Id entries:
        /// 1. User_Id "****" (today's play - for current session)
        /// 2. User_Id "MM/dd HH:mm" (timestamped history record)
        /// </summary>
        /// <param name="songId">The Song_Id to record</param>
        private void RecordSongPlay(string songId)
        {
            if (string.IsNullOrEmpty(songId)) return;

            try
            {
                var settings = SettingsManager.Instance.CurrentSettings;
                int maxRecords = settings.PlayHistoryCount;

                DebugLog($"RecordSongPlay called for Song_Id={songId}, maxRecords={maxRecords}");

                // 1. Add to User_Id "****" (today's play list)
                string todayUserId = "****";
                var todayRecords = SongDatas.FavoriteSongData?
                    .Where(fav => fav.TryGetValue("User_Id", out var uid) && uid?.ToString() == todayUserId)
                    .ToList() ?? new List<Dictionary<string, object?>>();

                DebugLog($"Current today's play list has {todayRecords.Count} songs");

                // Check if song already exists in today's list
                bool existsInToday = todayRecords.Any(fav =>
                    fav.TryGetValue("Song_Id", out var sid) && sid?.ToString() == songId);

                if (!existsInToday)
                {
                    int rowsAffected = Db.ExecuteNonQuery("INSERT INTO ktv_Favorite (User_Id, Song_Id) VALUES (?, ?)", todayUserId, songId);
                    AppLogger.Log($"DB Write: Added Song_Id={songId} to today's play list, rows affected={rowsAffected}");
                    DebugLog($"Added to today's play list: Song_Id={songId}, rows affected={rowsAffected}");
                }
                else
                {
                    DebugLog($"Song {songId} already in today's play list, skipping");
                }

                // 2. Add to timestamped User_Id (MM/dd HH:mm format) - using session timestamp
                string timestampUserId = _sessionTimestampUserId;
                DebugLog($"Using session timestamped entry: User_Id={timestampUserId}");
                
                // Get all timestamped records (User_Id that match MM/dd HH:mm pattern)
                var timestampedRecords = SongDatas.FavoriteSongData?
                    .Where(fav => 
                    {
                        if (!fav.TryGetValue("User_Id", out var uid)) return false;
                        return SongDatas.IsTimestampUserId(uid?.ToString() ?? "");
                    })
                    .OrderBy(fav => fav.TryGetValue("User_Id", out var uid) ? uid?.ToString() ?? "" : "")
                    .ToList() ?? new List<Dictionary<string, object?>>();

                // Count unique User_Id entries (not individual songs)
                var uniqueTimestamps = timestampedRecords
                    .Select(fav => fav.TryGetValue("User_Id", out var uid) ? uid?.ToString() ?? "" : "")
                    .Distinct()
                    .OrderBy(uid => uid)
                    .ToList();

                DebugLog($"Found {uniqueTimestamps.Count} unique timestamps, limit is {maxRecords}");

                // If we've reached the limit, delete all records for the oldest timestamp
                if (uniqueTimestamps.Count >= maxRecords)
                {
                    string oldestTimestamp = uniqueTimestamps.First();
                    int deletedRows = Db.ExecuteNonQuery("DELETE FROM ktv_Favorite WHERE User_Id = ?", oldestTimestamp);
                    AppLogger.Log($"DB Write: Deleted oldest timestamp records: User_Id={oldestTimestamp}, rows deleted={deletedRows}");
                    DebugLog($"Deleted oldest timestamp records: User_Id={oldestTimestamp}, rows deleted={deletedRows}");
                    
                    // Also delete from ktv_User if it exists
                    try
                    {
                        Db.ExecuteNonQuery("DELETE FROM ktv_User WHERE User_Id = ?", oldestTimestamp);
                        AppLogger.Log($"DB Write: Deleted User_Id from ktv_User: {oldestTimestamp}");
                        DebugLog($"Deleted User_Id from ktv_User: {oldestTimestamp}");
                    }
                    catch (Exception userEx)
                    {
                        DebugLog($"Note: Could not delete from ktv_User (may not exist): {userEx.Message}");
                        AppLogger.LogError("Could not delete from ktv_User", userEx);
                    }
                }

                // Ensure User_Id exists in ktv_User table (may be required for foreign key)
                try
                {
                    var userExists = Db.GetDataTable("SELECT COUNT(*) FROM ktv_User WHERE User_Id = ?", timestampUserId);
                    if (userExists.Rows.Count > 0 && Convert.ToInt32(userExists.Rows[0][0]) == 0)
                    {
                        // User doesn't exist, create it
                        Db.ExecuteNonQuery("INSERT INTO ktv_User (User_Id, User_Name) VALUES (?, ?)", timestampUserId, timestampUserId);
                        AppLogger.Log($"DB Write: Created User_Id in ktv_User: {timestampUserId}");
                        DebugLog($"Created User_Id in ktv_User: {timestampUserId}");
                    }
                }
                catch (Exception userEx)
                {
                    DebugLog($"Note: Could not check/create User_Id in ktv_User: {userEx.Message}");
                    AppLogger.LogError("Could not check/create User_Id in ktv_User", userEx);
                }

                // Check if this exact record already exists (same timestamp + song)
                bool existsInTimestamp = SongDatas.FavoriteSongData?
                    .Any(fav => 
                        fav.TryGetValue("User_Id", out var uid) && uid?.ToString() == timestampUserId &&
                        fav.TryGetValue("Song_Id", out var sid) && sid?.ToString() == songId) ?? false;

                if (!existsInTimestamp)
                {
                    // Insert the new timestamped record
                    int timestampRows = Db.ExecuteNonQuery("INSERT INTO ktv_Favorite (User_Id, Song_Id) VALUES (?, ?)", timestampUserId, songId);
                    AppLogger.Log($"DB Write: Recorded play history: User_Id={timestampUserId}, Song_Id={songId}, rows affected={timestampRows}");
                    DebugLog($"Recorded play history: User_Id={timestampUserId}, Song_Id={songId}, rows affected={timestampRows}");
                }
                else
                {
                    DebugLog($"Play history already exists for User_Id={timestampUserId}, Song_Id={songId}, skipping");
                }

                // Reload the favorite data to keep it in sync
                SongDatas.ReloadFavoriteData();
                DebugLog("Favorite data reloaded from database");
            }
            catch (Exception ex)
            {
                DebugLog($"Error recording song play: {ex.Message}");
                AppLogger.LogError("Failed to record song play", ex);
            }
        }
    }
}
