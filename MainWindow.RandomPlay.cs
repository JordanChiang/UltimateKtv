using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows.Media;
using System.Xml.Linq;
using UltimateKtv.Enums;

namespace UltimateKtv
{
    public partial class MainWindow
    {
        private readonly Random _random = new Random();
        private bool _isRandomSongPlaying = false; // Tracks if the current song is from random play


        /// <summary>
        /// Tries to play a random song based on settings.
        /// Returns true if a song was added to the waiting list.
        /// </summary>
        private bool TryPlayRandomSong()
        {
            var settings = SettingsManager.Instance.CurrentSettings;
            
            if (!settings.RandomPlayEnabled)
            {
                return false;
            }

//            AppLogger.Log($"Random play: Selecting from category {settings.RandomPlayCategory}");

            SongDisplayItem? randomSong = null;

            switch (settings.RandomPlayCategory)
            {
                case 0: // 國語排行
                    randomSong = GetRandomSongFromRanking("國語");
                    break;
                case 1: // 台語排行
                    randomSong = GetRandomSongFromRanking("台語");
                    break;
                case 2: // 新進歌曲
                    randomSong = GetRandomSongFromNewSongs();
                    break;
                case 3: // 全部排行
                    randomSong = GetRandomSongFromRanking(null);
                    break;
                case 10: // 我的最愛
                    randomSong = GetRandomSongFromFavorites(settings.RandomPlayFavoriteUser);
                    break;
                default:
                    return false;
            }

            if (randomSong == null)
            {
                string categoryName = settings.RandomPlayCategory switch
                {
                    0 => "國語排行",
                    1 => "台語排行",
                    2 => "新進歌曲",
                    3 => "全部排行",
                    10 => "我的最愛",
                    _ => "未知類別"
                };
                AppLogger.Log($"Random play: No songs found in {categoryName}");
                ShowRandomPlayMarquee($"隨機播放: {categoryName} 無可用歌曲");
                return false;
            }

            // Set audio track based on RandomPlayAudioChannel setting
            // 0 = 人聲 (vocal/default), 1 = 伴唱 (music track)
            int audioTrack = randomSong.AudioTrack;
            if (settings.RandomPlayAudioChannel == 1)
            {
                // Set to music track - the actual music track value will be applied when song plays
                // We store a marker value that will be used in mediaUriElement_MediaOpened
                audioTrack = -1; // Marker for "use music track"
            }

            // Create waiting list item
            var waitingItem = new WaitingListItem
            {
                SongId = randomSong.SongId,
                WaitingListSongName = randomSong.SongName,
                WaitingListSingerName = randomSong.SingerName,
                FilePath = randomSong.FilePath,
                Volume = randomSong.Volume,
                AudioTrack = audioTrack,
                OrderedBy = "隨機播放"
            };

            // Add to waiting list
            _waitingList?.Add(waitingItem);
            
            AppLogger.Log($"Random play: Added '{randomSong.SongName}' by '{randomSong.SingerName}'");
            DebugLog($"Random play: Added '{randomSong.SongName}' by '{randomSong.SingerName}'");

            // Apply the audio channel after the song starts playing
            // Store the setting for use in MediaOpened event
            _pendingRandomPlayAudioChannel = settings.RandomPlayAudioChannel;

            // Update display and continue playback
            UpdateWaitingListDisplay();
            return true;
        }

        // Stores pending audio channel setting for random play
        private int _pendingRandomPlayAudioChannel = -1;

        /// <summary>
        /// Applies the audio channel setting for random play after song starts
        /// </summary>
        private void ApplyRandomPlayAudioChannel()
        {
            if (_pendingRandomPlayAudioChannel < 0) return;

            try
            {
                if (_pendingRandomPlayAudioChannel == 0)
                {
                    // 人聲 - call VocalBtn_Click behavior
                    VocalBtn_Click(null!, null!);
                    AppLogger.Log("Random play: Set audio to vocal track");
                }
                else if (_pendingRandomPlayAudioChannel == 1)
                {
                    // 伴唱 - call MusicBtn_Click behavior
                    MusicBtn_Click(null!, null!);
                    AppLogger.Log("Random play: Set audio to music track");
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Random play: Error setting audio channel", ex);
            }
            finally
            {
                _pendingRandomPlayAudioChannel = -1;
            }
        }

        /// <summary>
        /// Gets a random song from ranking by language (top played songs)
        /// </summary>
        private SongDisplayItem? GetRandomSongFromRanking(string? language)
        {
            if (SongDatas.SongData == null)
            {
                DebugLog($"GetRandomSongFromRanking: SongData is null");
                return null;
            }

            try
            {
                // Filter songs by language and order by play count
                var rankedSongs = SongDatas.SongData
                    .Where(s =>
                    {
                        var playCount = s.TryGetValue("Song_PlayCount", out var pcObj) && int.TryParse(pcObj?.ToString(), out int pc) ? pc : 0;
                        if (playCount == 0) return false;

                        var songLang = s.TryGetValue("Song_Lang", out var langObj) ? langObj?.ToString() : "";
                        return string.IsNullOrEmpty(language) || songLang == language;
                    })
                    .OrderByDescending(s => s.TryGetValue("Song_PlayCount", out var pc) && int.TryParse(pc?.ToString(), out int count) ? count : 0)
                    .Take(100) // Top 100 songs to random select from
                    .ToList();

                if (rankedSongs.Count == 0)
                {
                    DebugLog($"GetRandomSongFromRanking: No songs found for language '{language}'");
                    return null;
                }

                // Random select one
                var randomIndex = _random.Next(rankedSongs.Count);
                var song = rankedSongs[randomIndex];

                return new SongDisplayItem
                {
                    SongId = song.TryGetValue("Song_Id", out var id) ? id?.ToString() ?? "" : "",
                    SongName = song.TryGetValue("Song_SongName", out var name) ? name?.ToString() ?? "" : "",
                    SingerName = song.TryGetValue("Song_Singer", out var singer) ? singer?.ToString() ?? "" : "",
                    Language = language ?? (song.TryGetValue("Song_Lang", out var langObj) ? langObj?.ToString() ?? "" : ""),
                    FilePath = song.TryGetValue("FilePath", out var fp) ? fp?.ToString() ?? "" : "",
                    Volume = song.TryGetValue("Song_Volume", out var vol) && int.TryParse(vol?.ToString(), out int v) ? v : 90,
                    AudioTrack = song.TryGetValue("Song_Track", out var tr) && int.TryParse(tr?.ToString(), out int t) ? t : 0
                };
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"GetRandomSongFromRanking: Error getting random song for '{language}'", ex);
                return null;
            }
        }

        /// <summary>
        /// Gets a random song from new songs (within NewSongDays setting)
        /// </summary>
        private SongDisplayItem? GetRandomSongFromNewSongs()
        {
            if (SongDatas.SongData == null) return null;

            try
            {
                var cutoffDate = DateTime.Today.AddDays(-NewSongDays);

                // Filter songs created within NewSongDays
                var newSongs = SongDatas.SongData
                    .Where(s =>
                    {
                        if (s.TryGetValue("Song_CreatDate", out var dateObj) && dateObj is DateTime createDate)
                        {
                            return createDate >= cutoffDate;
                        }
                        return false;
                    })
                    .ToList();

                if (newSongs.Count == 0) return null;

                // Random select one
                var randomIndex = _random.Next(newSongs.Count);
                var song = newSongs[randomIndex];

                return new SongDisplayItem
                {
                    SongId = song.TryGetValue("Song_Id", out var id) ? id?.ToString() ?? "" : "",
                    SongName = song.TryGetValue("Song_SongName", out var name) ? name?.ToString() ?? "" : "",
                    SingerName = song.TryGetValue("Song_Singer", out var singer) ? singer?.ToString() ?? "" : "",
                    Language = song.TryGetValue("Song_Lang", out var lang) ? lang?.ToString() ?? "" : "",
                    FilePath = song.TryGetValue("FilePath", out var fp) ? fp?.ToString() ?? "" : "",
                    Volume = song.TryGetValue("Song_Volume", out var vol) && int.TryParse(vol?.ToString(), out int v) ? v : 90,
                    AudioTrack = song.TryGetValue("Song_Track", out var tr) && int.TryParse(tr?.ToString(), out int t) ? t : 0
                };
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"GetRandomSongFromNewSongs: Error getting random new song", ex);
                return null;
            }
        }

        /// <summary>
        /// Gets a random song from a user's favorites
        /// </summary>
        private SongDisplayItem? GetRandomSongFromFavorites(string userName)
        {
            if (string.IsNullOrEmpty(userName))
            {
                ShowRandomPlayMarquee("隨機播放: 未選擇最愛用戶");
                return null;
            }

            if (SongDatas.FavoriteUserData == null || SongDatas.FavoriteSongData == null || SongDatas.SongData == null)
            {
                ShowRandomPlayMarquee("隨機播放: 歌曲資料未載入");
                return null;
            }

            try
            {
                // Find the User_Id for the given userName
                var userRow = SongDatas.FavoriteUserData.AsEnumerable()
                    .FirstOrDefault(row => row["User_Name"]?.ToString() == userName);

                if (userRow == null)
                {
                    ShowRandomPlayMarquee($"隨機播放: 找不到用戶 {userName}");
                    return null;
                }

                var userId = userRow["User_Id"]?.ToString() ?? "";
                if (string.IsNullOrEmpty(userId))
                {
                    return null;
                }

                // Get all favorite songs for this user
                var favoriteSongs = SongDatas.FavoriteSongData
                    .Where(fav => fav.TryGetValue("User_Id", out var uid) && uid?.ToString() == userId)
                    .ToList();

                if (favoriteSongs.Count == 0)
                {
                    ShowRandomPlayMarquee($"隨機播放: {userName} 沒有最愛歌曲");
                    return null;
                }

                // Random select one favorite
                var randomIndex = _random.Next(favoriteSongs.Count);
                var favorite = favoriteSongs[randomIndex];

                var songId = favorite.TryGetValue("Song_Id", out var sidObj) ? sidObj?.ToString() ?? "" : "";
                if (string.IsNullOrEmpty(songId))
                {
                    return null;
                }

                // Find the corresponding song in SongData
                var song = SongDatas.SongData.FirstOrDefault(s =>
                    s.TryGetValue("Song_Id", out var id) && id?.ToString() == songId);

                if (song == null)
                {
                    DebugLog($"GetRandomSongFromFavorites: Song '{songId}' not found in database");
                    return null;
                }

                return new SongDisplayItem
                {
                    SongId = songId,
                    SongName = song.TryGetValue("Song_SongName", out var name) ? name?.ToString() ?? "" : "",
                    SingerName = song.TryGetValue("Song_Singer", out var singer) ? singer?.ToString() ?? "" : "",
                    Language = song.TryGetValue("Song_Lang", out var lang) ? lang?.ToString() ?? "" : "",
                    FilePath = song.TryGetValue("FilePath", out var fp) ? fp?.ToString() ?? "" : "",
                    Volume = song.TryGetValue("Song_Volume", out var vol) && int.TryParse(vol?.ToString(), out int v) ? v : 90,
                    AudioTrack = song.TryGetValue("Song_Track", out var tr) && int.TryParse(tr?.ToString(), out int t) ? t : 0
                };
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"GetRandomSongFromFavorites: Error getting random favorite for '{userName}'", ex);
                ShowRandomPlayMarquee("隨機播放: 讀取最愛歌曲失敗");
                return null;
            }
        }

        /// <summary>
        /// Shows a random play message on the player device using consistent styling
        /// </summary>
        private void ShowRandomPlayMarquee(string message)
        {
            MarqueeAPI.ShowCustomStaticText(
                message,
                TextSettingsHandler.StaticTextForeground,
                TextSettingsHandler.FontFamily,
                TextSettingsHandler.Settings.NotificationFontSize,
                MarqueePosition.Top,
                0,
                MarqueeDisplayDevice.ConsoleScreen
            );
        }


        // Timer for delayed random play check
        private System.Windows.Threading.DispatcherTimer? _randomPlayDelayTimer;

        /// <summary>
        /// Starts a 1-second delayed check for random play when playlist becomes empty.
        /// Cancels any existing delay timer before starting a new one.
        /// </summary>
        private void StartDelayedRandomPlayWhenEmpty()
        {
            var settings = SettingsManager.Instance.CurrentSettings;
            if (!settings.RandomPlayEnabled) return;

            // Cancel any existing timer
            if (_randomPlayDelayTimer != null)
            {
                _randomPlayDelayTimer.Stop();
                _randomPlayDelayTimer = null;
            }

            _randomPlayDelayTimer = new System.Windows.Threading.DispatcherTimer();
            _randomPlayDelayTimer.Interval = TimeSpan.FromMilliseconds(200);
            _randomPlayDelayTimer.Tick += (s, e) =>
            {
                _randomPlayDelayTimer?.Stop();
                _randomPlayDelayTimer = null;
                
                // Check if waiting list is still empty
                var nonEmptySongs = _waitingList?.Where(item => !string.IsNullOrEmpty(item.WaitingListSongName)).ToList();
                
                if (nonEmptySongs == null || nonEmptySongs.Count == 0)
                {
                    TryPlayRandomSong();
                }
            };
            _randomPlayDelayTimer.Start();
        }

        /// <summary>
        /// Releases media resources before starting random play.
        /// Should be called when a song ends to ensure clean state.
        /// </summary>
        private void ReleaseResourcesBeforeRandomPlay()
        {
            DebugLog(">>> ReleaseResourcesBeforeRandomPlay: ENTER");
            try
            {
                DebugLog(">>> ReleaseResourcesBeforeRandomPlay: Releasing media resources");
                
                // Stop the media player if it's still playing
                SafeStop(mediaUriElement, nameof(mediaUriElement));
                
                // Clear any pending operations
                _isPlayingFromWaitingList = false;
                _isTransitioningSong = false;
                ProcessPendingSongs(); // Retry pending adds
                
                // Force a small delay to allow resources to be released
                DebugLog(">>> ReleaseResourcesBeforeRandomPlay: Queueing PlayNextSongFromWaitingList via Dispatcher");
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    new Action(() =>
                    {
                        DebugLog(">>> ReleaseResourcesBeforeRandomPlay: Dispatcher callback - calling PlayNextSongFromWaitingList");
                        // Continue with the next song after resources are released
                        PlayNextSongFromWaitingList();
                    }));
            }
            catch (Exception ex)
            {
                AppLogger.LogError("ReleaseResourcesBeforeRandomPlay: Error releasing resources", ex);
                // Fallback to direct call
                PlayNextSongFromWaitingList();
            }
        }
    }
}
