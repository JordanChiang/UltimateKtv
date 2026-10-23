using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UltimateKtv.Playback;

namespace UltimateKtv
{
    /// <summary>正在播放的歌 (手機查詢用)；沒有歌在播放時 IsPlaying 為 false、其餘欄位為空字串。</summary>
    public sealed record RemoteNowPlaying(bool IsPlaying, string SongName, string SingerName, string SongId, string OrderedBy);

    /// <summary>
    /// 手機遠端點歌對主程式的所有操作。HTTP 請求在 Kestrel 執行緒上，這裡統一切到 UI 執行緒：
    /// 切歌、人聲/伴唱、音量、升降調、移動待播清單排進 UI 執行緒後立即返回 (與主畫面按鈕相同的動作)；
    /// 點歌、查詢狀態與音量鎖定等到 UI 執行緒做完才回傳結果。
    /// </summary>
    public sealed class RemoteControl
    {
        private readonly MainWindow _main;

        public RemoteControl(MainWindow main) => _main = main;

        // ---- 點歌與待播清單 ----

        public EnqueueResult Order(SongDisplayItem song) => InvokeAsync(m => m.OrderSongAsync(song, OrderSource.Remote));

        public void MoveToFirst(Guid id) => Post(m => m.MoveSongToFirst(id));
        public void MoveToNext(Guid id) => Post(m => m.MoveSongToNext(id));
        public void Remove(Guid id) => Post(m => m.RemoveSongFromList(id));

        /// <summary>正在播放的歌與待播清單首數 (一次切換 UI 執行緒)。</summary>
        public (RemoteNowPlaying NowPlaying, int WaitingCount) GetPlayerState() =>
            Invoke(m => (NowPlaying(m), m.GetWaitingListCount()));

        public (RemoteNowPlaying NowPlaying, List<WaitingListItem> WaitingList) GetWaitingList() =>
            Invoke(m => (NowPlaying(m), m.GetWaitingListItems()));

        // ---- 播放控制 ----

        public void Skip() => Post(m => m.SkipCurrentSong());
        public void SwitchToVocal() => Post(m => m.SwitchToVocal());
        public void SwitchToMusic() => Post(m => m.SwitchToMusic());
        public void VolumeUp() => Post(m => m.VolumeUp());
        public void VolumeDown() => Post(m => m.VolumeDown());

        /// <summary>切換音量鎖定，回傳切換後是否鎖定。</summary>
        public bool ToggleVolumeLock() => Invoke(m => m.ToggleVolumeLock());

        public void PitchUp() => Post(m => m.AdjustPitch(1));
        public void PitchDown() => Post(m => m.AdjustPitch(-1));
        public void PitchReset() => Post(m => m.SetOriginalPitch());

        /// <summary>結束程式 (shutdownWindows 時一併關機)；排進 UI 執行緒後立即返回，讓 HTTP 先回應。</summary>
        public void Quit(bool shutdownWindows) => Post(m => { _ = App.QuitAsync(shutdownWindows); });

        // ---- YouTube ----

        /// <summary>加入 YouTube 下載佇列；已在佇列或待播清單中回傳 false。</summary>
        public bool QueueYoutube(SongDisplayItem song) => Invoke(m => m.DownloadYoutubeVideo(song));

        public object GetYoutubeQueueStatus() => Invoke(m => m.GetYoutubeDownloadQueueStatus());

        public async Task<int> CountUnrecordedYoutubeFilesAsync() =>
            await _main.Dispatcher.InvokeAsync(() => _main.GetUnrecordedYoutubeFiles()?.Count ?? 0);

        public async Task ImportUnrecordedYoutubeFilesAsync()
        {
            await _main.Dispatcher.InvokeAsync(async () =>
            {
                var files = _main.GetUnrecordedYoutubeFiles();
                if (files != null && files.Count > 0) await _main.ImportUnrecordedYoutubeFilesAsync(files);
            }).Task.Unwrap();
        }

        private static RemoteNowPlaying NowPlaying(MainWindow main)
        {
            var info = main.GetCurrentPlayingSongInfo();
            return new RemoteNowPlaying(info.IsPlaying, info.SongName, info.SingerName, info.SongId, info.OrderedBy);
        }

        /// <summary>排進 UI 執行緒，不等結果。</summary>
        private void Post(Action<MainWindow> action) => _main.Dispatcher.BeginInvoke((Action)(() => action(_main)));

        /// <summary>在 UI 執行緒執行並等待結果。</summary>
        private T Invoke<T>(Func<MainWindow, T> func) => _main.Dispatcher.Invoke(() => func(_main));

        /// <summary>在 UI 執行緒執行非同步動作並等待結果 (不卡住 UI，只卡住呼叫的 Kestrel 執行緒)。</summary>
        private T InvokeAsync<T>(Func<MainWindow, Task<T>> func) =>
            _main.Dispatcher.InvokeAsync(() => func(_main)).Task.Unwrap().GetAwaiter().GetResult();
    }
}
