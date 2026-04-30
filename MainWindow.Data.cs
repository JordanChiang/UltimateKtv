using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
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
                var singersForPage = singerNamesForPage.Select(name => {
                    string photo = SingerPhotoManager.GetPhoto(name);
                    bool hasSpecificPhoto = (photo != null && !photo.Contains("favoriteuser_default.png"));
                    return new SingerDisplayItem
                    {
                        Name = name,
                        PhotoPath = photo,
                        HasPhoto = hasSpecificPhoto
                    };
                }).ToList();
                
                VisualSingerGrid.ItemsSource = singersForPage;
                VisualSingerGrid.Visibility = Visibility.Visible;
                SingerGrid.Visibility = Visibility.Collapsed;
            }
            else
            {
                SingerGrid.ItemsSource = singerNamesForPage;
                SingerGrid.Visibility = Visibility.Visible;
                VisualSingerGrid.Visibility = Visibility.Collapsed;
            }

            // Update the page information TextBlock
            PageInfoTextBlock.Text = $"第 {page}/{_totalPages} 頁 ({_allSingers.Count} 位歌手)";

            // Enable or disable the page buttons based on the current page
            PageUp.IsEnabled = (page > 1);
            PageDown.IsEnabled = (page < _totalPages);
        }

        private void PageUp_Click(object sender, RoutedEventArgs e)
        {
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

                AppLogger.Log($"User action: Singer button clicked - {name}");
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
            // Use LINQ to get the correct subset of songs for the page
            var songsForPage = _allSongs.Skip((page - 1) * SongPageSize).Take(SongPageSize).ToList();

            // Set the ItemsSource of the correct DataGrid
            if (_isLanguageMode)
            {
                LanguageSongListGrid.ItemsSource = songsForPage;
            }
            else
            {
                SongListGrid.ItemsSource = songsForPage;
            }

            // Update the page information TextBlock
            PageInfoTextBlock.Text = $"第 {page}/{_totalSongPages} 頁 ({_currentSongsTitle} - {_allSongs.Count} 首歌曲)";

            // Enable or disable the page buttons based on the current page
            PageUp.IsEnabled = (page > 1);
            PageDown.IsEnabled = (page < _totalSongPages);
        }

        private void DisplaySongsInGrid(List<SongDisplayItem> songs, string title)
        {
            _currentSongsTitle = title;
            _isLanguageMode = false;
            _allSongs = songs;
            _totalSongPages = (_allSongs.Count == 0) ? 1 : (_allSongs.Count + SongPageSize - 1) / SongPageSize;
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
            _totalSongPages = (_allSongs.Count == 0) ? 1 : (_allSongs.Count + SongPageSize - 1) / SongPageSize;
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
            _allSingers = SongDatas.GetSortedFavoriteUsers(includePadding: true)
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
            _currentSongsTitle = $"{userName} 的最愛";
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
            _allSongs = SongDatas.ApplySongSorting(songs, (s, key) => s.GetType().GetProperty(key)?.GetValue(s, null));

            // Calculate pagination for songs
            _totalSongPages = (_allSongs.Count == 0) ? 1 : (_allSongs.Count + SongPageSize - 1) / SongPageSize;
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
                string dbPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CrazySong.mdb");

                // Delete all records for User_Id "****"
                string deleteQuery = $"DELETE FROM ktv_Favorite WHERE User_Id = '{userId}'";
                DbHelper.Access.ExecuteNonQuery(dbPath, deleteQuery, null);
                AppLogger.Log("DB Write: Cleared today's play list (User_Id = ****)");
                DebugLog("Cleared today's play list (User_Id = ****)");

                // Reload the favorite data to keep it in sync
                SongDatas.ReloadFavoriteData(dbPath);
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
                string dbPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CrazySong.mdb");

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
                    string insertTodayQuery = $"INSERT INTO ktv_Favorite (User_Id, Song_Id) VALUES ('{todayUserId}', '{songId}')";
                    int rowsAffected = DbHelper.Access.ExecuteNonQuery(dbPath, insertTodayQuery, null);
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
                    string deleteQuery = $"DELETE FROM ktv_Favorite WHERE User_Id = '{oldestTimestamp}'";
                    int deletedRows = DbHelper.Access.ExecuteNonQuery(dbPath, deleteQuery, null);
                    AppLogger.Log($"DB Write: Deleted oldest timestamp records: User_Id={oldestTimestamp}, rows deleted={deletedRows}");
                    DebugLog($"Deleted oldest timestamp records: User_Id={oldestTimestamp}, rows deleted={deletedRows}");
                    
                    // Also delete from ktv_User if it exists
                    try
                    {
                        string deleteUserQuery = $"DELETE FROM ktv_User WHERE User_Id = '{oldestTimestamp}'";
                        DbHelper.Access.ExecuteNonQuery(dbPath, deleteUserQuery, null);
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
                    string checkUserQuery = $"SELECT COUNT(*) FROM ktv_User WHERE User_Id = '{timestampUserId}'";
                    var userExists = DbHelper.Access.GetDataTable(dbPath, checkUserQuery, null);
                    if (userExists.Rows.Count > 0 && Convert.ToInt32(userExists.Rows[0][0]) == 0)
                    {
                        // User doesn't exist, create it
                        string insertUserQuery = $"INSERT INTO ktv_User (User_Id, User_Name) VALUES ('{timestampUserId}', '{timestampUserId}')";
                        DbHelper.Access.ExecuteNonQuery(dbPath, insertUserQuery, null);
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
                    string insertTimestampQuery = $"INSERT INTO ktv_Favorite (User_Id, Song_Id) VALUES ('{timestampUserId}', '{songId}')";
                    int timestampRows = DbHelper.Access.ExecuteNonQuery(dbPath, insertTimestampQuery, null);
                    AppLogger.Log($"DB Write: Recorded play history: User_Id={timestampUserId}, Song_Id={songId}, rows affected={timestampRows}");
                    DebugLog($"Recorded play history: User_Id={timestampUserId}, Song_Id={songId}, rows affected={timestampRows}");
                }
                else
                {
                    DebugLog($"Play history already exists for User_Id={timestampUserId}, Song_Id={songId}, skipping");
                }

                // Reload the favorite data to keep it in sync
                SongDatas.ReloadFavoriteData(dbPath);
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
