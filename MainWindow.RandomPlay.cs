using System;
using UltimateKtv.Playback;
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
        private const int RecentRandomWindow = 20;
        private readonly Queue<string> _recentRandomSongIds = new(); // Most recent random picks, oldest first

        /// <summary>
        /// Picks a random item, skipping songs picked recently. The window is capped at half the pool so small pools still rotate.
        /// </summary>
        private T PickRandomAvoidingRecent<T>(IList<T> items, Func<T, string> getSongId)
        {
            int window = Math.Min(RecentRandomWindow, items.Count / 2);
            var recent = _recentRandomSongIds.Skip(Math.Max(0, _recentRandomSongIds.Count - window)).ToHashSet();
            var candidates = items.Where(i => !recent.Contains(getSongId(i))).ToList();
            if (candidates.Count == 0) candidates = items.ToList();
            return candidates[_random.Next(candidates.Count)];
        }


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
                    0 => Loc.T("Random.MandarinRank"),
                    1 => Loc.T("Random.TaiwaneseRank"),
                    2 => Loc.T("Random.NewSongs"),
                    3 => Loc.T("Random.AllRank"),
                    10 => Loc.T("Random.MyFavorites"),
                    _ => Loc.T("Random.UnknownCategory")
                };
                AppLogger.Log($"Random play: No songs found in {categoryName}");
                ShowRandomPlayMarquee(Loc.T("Random.NoSongs", categoryName));
                return false;
            }

            // 隨機歌開播時套用的聲道 (0 = 人聲, 1 = 伴唱)，在 SongOpened 時套用
            var queued = new QueuedSong
            {
                SongId = randomSong.SongId,
                SongName = randomSong.SongName,
                SingerName = randomSong.SingerName,
                FilePath = randomSong.FilePath,
                Volume = randomSong.Volume,
                AudioTrack = randomSong.AudioTrack,
                OrderedBy = Loc.OrderedByRandom,
                IsRandom = true,
                RandomAudioChannel = settings.RandomPlayAudioChannel
            };

            _recentRandomSongIds.Enqueue(randomSong.SongId);
            while (_recentRandomSongIds.Count > RecentRandomWindow) _recentRandomSongIds.Dequeue();
            DebugLog($"Random play: Adding '{randomSong.SongName}' by '{randomSong.SingerName}'");

            _ = _playback.EnqueueAsync(queued, OrderSource.Random);
            return true;
        }

        /// <summary>
        /// Applies the random-play audio channel setting once a random song has opened.
        /// </summary>
        private void ApplyRandomPlayAudioChannel(QueuedSong song)
        {
            if (!song.IsRandom || song.RandomAudioChannel < 0) return;

            try
            {
                if (song.RandomAudioChannel == 0)
                {
                    // 人聲 - call VocalBtn_Click behavior
                    VocalBtn_Click(null!, null!);
                    AppLogger.Log("Random play: Set audio to vocal track");
                }
                else if (song.RandomAudioChannel == 1)
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
                var song = PickRandomAvoidingRecent(rankedSongs, s => s.TryGetValue("Song_Id", out var sid) ? sid?.ToString() ?? "" : "");

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
                var song = PickRandomAvoidingRecent(newSongs, s => s.TryGetValue("Song_Id", out var sid) ? sid?.ToString() ?? "" : "");

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
                ShowRandomPlayMarquee(Loc.T("Random.NoFavUser"));
                return null;
            }

            if (SongDatas.FavoriteUserData == null || SongDatas.FavoriteSongData == null || SongDatas.SongData == null)
            {
                ShowRandomPlayMarquee(Loc.T("Random.DataNotLoaded"));
                return null;
            }

            try
            {
                // Find the User_Id for the given userName
                var userRow = SongDatas.FavoriteUserData.AsEnumerable()
                    .FirstOrDefault(row => row["User_Name"]?.ToString() == userName);

                if (userRow == null)
                {
                    ShowRandomPlayMarquee(Loc.T("Random.UserNotFound", userName));
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
                    ShowRandomPlayMarquee(Loc.T("Random.UserNoFavs", userName));
                    return null;
                }

                // Random select one favorite
                var favorite = PickRandomAvoidingRecent(favoriteSongs, f => f.TryGetValue("Song_Id", out var sid) ? sid?.ToString() ?? "" : "");

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
                ShowRandomPlayMarquee(Loc.T("Random.ReadFavsFailed"));
                return null;
            }
        }

        /// <summary>
        /// Shows a random play message on the player device using consistent styling
        /// </summary>
        private void ShowRandomPlayMarquee(string message)
        {
            var settings = SettingsManager.Instance.CurrentSettings;
            // Always display on PlayerScreen so users can see random play status/errors on the TV/Player screen
            MarqueeAPI.ShowCustomStaticText(
                message,
                ThemeSettingsHandler.StaticTextForeground,
                new System.Windows.Media.FontFamily(!string.IsNullOrWhiteSpace(settings.MarqueeBroadcastFontFamily) ? settings.MarqueeBroadcastFontFamily : "微軟正黑體"),
                settings.MarqueeBroadcastFontSize,
                MarqueePosition.Top,
                0,
                MarqueeDisplayDevice.PlayerScreen
            );

            // In multi-monitor mode, also display on ConsoleScreen
            if (!IsSingleMonitorMode)
            {
                MarqueeAPI.ShowCustomStaticText(
                    message,
                    ThemeSettingsHandler.StaticTextForeground,
                    new System.Windows.Media.FontFamily(!string.IsNullOrWhiteSpace(settings.MarqueeBroadcastFontFamily) ? settings.MarqueeBroadcastFontFamily : "微軟正黑體"),
                    settings.MarqueeBroadcastFontSize,
                    MarqueePosition.Top,
                    0,
                    MarqueeDisplayDevice.ConsoleScreen
                );
            }
        }


        // Timer for delayed random play check
        private System.Windows.Threading.DispatcherTimer? _randomPlayDelayTimer;

        /// <summary>
        /// Starts a delayed check for random play when playlist becomes empty.
        /// Cancels any existing delay timer before starting a new one.
        /// </summary>
        public void StartDelayedRandomPlayWhenEmpty()
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
                if (_playback.Snapshot.Count == 0)
                {
                    TryPlayRandomSong();
                }
            };
            _randomPlayDelayTimer.Start();
        }
    }
}
