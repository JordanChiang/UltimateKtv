using System;
using System.Threading.Tasks;

namespace UltimateKtv.Playback
{
    /// <summary>
    /// 播放紀錄：決定一首歌開播後要寫入哪些紀錄。隨機歌一律不記。
    /// <list type="bullet">
    /// <item>這次開機第一首 (本機) 歌開播時清空今日點唱，接著記錄點唱 (今日點唱 + 點唱紀錄)。</item>
    /// <item>播放進度超過設定比例時播放次數加一 (每次開播只算一次)；本機歌同時更新記憶體中的歌曲。</item>
    /// <item>YouTube 歌開播時記下影片長度 (只在還沒有長度時)。</item>
    /// </list>
    /// 所有寫入都丟到背景：開播當下 UI 執行緒要跑切歌的背景掃光等動畫，同步寫歌庫 (x86 的 Jet 寫今日點唱 + 點唱紀錄約 0.3 秒)
    /// 會讓動畫停頓。這些寫入彼此依序執行 (今日點唱要先清空才記錄)；與主程式其他地方 (音量、我的最愛) 的寫入不互斥，
    /// 各自都是獨立的 SQL 指令。
    /// </summary>
    public sealed class PlayRecorder
    {
        private readonly string _sessionUserId;
        private readonly Func<int> _playCountPercentage;
        private readonly Func<int> _maxSessions;
        private readonly Action _historyChanged;
        private readonly Action<Action> _background;

        private Song? _playing;
        private bool _isYoutube;
        private bool _counted = true;
        private bool _todayListCleared; // 只在背景工作中讀寫 (工作依序執行)
        private readonly object _queueLock = new();
        private Task _queue = Task.CompletedTask;

        /// <param name="sessionUserId">這次開機的點唱紀錄 User_Id ("MM/dd HH:mm")。</param>
        /// <param name="playCountPercentage">播放次數加一的進度門檻 (百分比)，每次讀取以套用設定變更。</param>
        /// <param name="maxSessions">點唱紀錄保留的開機次數。</param>
        /// <param name="historyChanged">今日點唱/點唱紀錄寫入後呼叫 (主程式重新載入我的最愛快取)；在背景執行緒呼叫。</param>
        /// <param name="background">背景寫入的方式；預設依序在背景執行緒執行，測試傳入直接執行。</param>
        public PlayRecorder(string sessionUserId, Func<int> playCountPercentage, Func<int> maxSessions,
            Action historyChanged, Action<Action>? background = null)
        {
            _sessionUserId = sessionUserId;
            _playCountPercentage = playCountPercentage;
            _maxSessions = maxSessions;
            _historyChanged = historyChanged;
            _background = background ?? Enqueue;
        }

        /// <summary>排在前一個寫入之後，在背景執行緒執行；例外寫入 log，不影響之後的寫入。</summary>
        private void Enqueue(Action work)
        {
            lock (_queueLock)
            {
                _queue = _queue.ContinueWith(_ =>
                {
                    try { work(); }
                    catch (Exception ex) { AppLogger.LogError("Play record write failed", ex); }
                }, TaskScheduler.Default);
            }
        }

        /// <summary>寫入 YouTube 影片長度後發出 (紀錄 Id 或影片 ID, 秒數)；在背景執行緒。</summary>
        public event Action<string, int>? YoutubeLengthStored;

        /// <summary>一首歌開播。playing 為歌庫中的歌 (YouTube 為臨時建立的歌)；duration 為影片長度。</summary>
        public void SongOpened(Song playing, bool isYoutube, bool isRandom, TimeSpan duration)
        {
            _playing = isRandom ? null : playing;
            _isYoutube = isYoutube;
            _counted = false;
            if (_playing == null || string.IsNullOrEmpty(playing.Id)) return;

            string songId = playing.Id;
            if (isYoutube)
            {
                int lengthSeconds = (int)duration.TotalSeconds;
                if (lengthSeconds <= 0) return;
                _background(() =>
                {
                    if (SongLibrary.UpdateYoutubeSongLength(songId, lengthSeconds)) YoutubeLengthStored?.Invoke(songId, lengthSeconds);
                });
                return;
            }

            int maxSessions = _maxSessions();
            _background(() =>
            {
                try
                {
                    if (!_todayListCleared)
                    {
                        SongLibrary.ClearTodayList();
                        _todayListCleared = true;
                    }
                    SongLibrary.RecordPlay(songId, _sessionUserId, maxSessions);
                    _historyChanged();
                }
                catch (Exception ex)
                {
                    AppLogger.LogError($"Failed to record song play for Song_Id '{songId}'", ex);
                }
            });
        }

        /// <summary>播放進度 (0~100)；超過門檻時播放次數加一，每次開播只算一次。</summary>
        public void ProgressChanged(double percentage)
        {
            if (_counted || _playing == null || percentage <= _playCountPercentage()) return;
            _counted = true;

            string songId = _playing.Id;
            if (string.IsNullOrEmpty(songId)) return;

            if (_isYoutube)
            {
                string name = _playing.Name.Length > 0 ? _playing.Name : "Unknown";
                _background(() =>
                {
                    try
                    {
                        // 紀錄 Id (桌機點播) 或影片 ID (手機點播) 都可以
                        int rows = SongLibrary.IncrementYoutubePlayCount(songId);
                        AppLogger.Log($"DB Write: Incremented PlayCount for Youtube '{name}' ({songId}), rows affected={rows}");
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogError($"DB Write failed: PlayCount update for '{name}'", ex);
                    }
                });
                return;
            }

            // 記憶體中的歌一起更新，排行與排序馬上反映
            int playCount = ++_playing.PlayCount;
            _background(() =>
            {
                try
                {
                    SongLibrary.SetPlayCount(songId, playCount);
                    AppLogger.Log($"DB Write: Updated Song_PlayCount to {playCount} for Song_Id '{songId}'");
                }
                catch (Exception ex)
                {
                    AppLogger.LogError($"DB Write failed: Song_PlayCount update for Song_Id '{songId}'", ex);
                }
            });
        }

        /// <summary>歌曲結束或進入閒置：之後的進度不再計入這首。</summary>
        public void SongEnded() => _playing = null;
    }
}
