using System;
using UltimateKtv.Playback;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace UltimateKtv
{
    public partial class MainWindow
    {
        private CancellationTokenSource? _youtubePreviewCts;
        private static readonly ConcurrentDictionary<string, string> _youtubeStreamUrlCache = new();
        private YoutubeDownloadQueue? _youtubeQueue;
        private YoutubeDownloadJob? _youtubeShownJob;

        /// <summary>YouTube 下載佇列；第一次使用時建立 (一定在 UI 執行緒，事件也回到 UI 執行緒)。</summary>
        private YoutubeDownloadQueue YoutubeQueue => _youtubeQueue ??= CreateYoutubeQueue();

        private YoutubeDownloadQueue CreateYoutubeQueue()
        {
            var queue = new YoutubeDownloadQueue(new YtDlpDownloader(_youtube), YoutubeCacheDir);
            queue.Changed += (_, _) => YoutubeQueue_Changed();
            queue.Progress += YoutubeQueue_Progress;
            queue.Completed += YoutubeQueue_Completed;
            queue.Failed += YoutubeQueue_Failed;
            queue.Cancelled += YoutubeQueue_Cancelled;
            return queue;
        }

        /// <summary>YouTube 下載快取資料夾 (設定的 YoutubeDownloadDir，相對路徑以程式目錄為準)。</summary>
        internal static string YoutubeCacheDir()
        {
            string cacheDir = SettingsManager.Instance.CurrentSettings.YoutubeDownloadDir;
            return Path.IsPathRooted(cacheDir) ? cacheDir : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, cacheDir);
        }

        public object GetYoutubeDownloadQueueStatus()
        {
            return new { count = YoutubeQueue.WaitingCount, isDownloading = YoutubeQueue.IsDownloading };
        }

        private void UpdateYoutubeDownloadQueueUI()
        {
            int remaining = YoutubeQueue.WaitingCount;
            HttpServer.BroadcastEvent("YoutubeQueueUpdate", new { count = remaining, isDownloading = IsDownloadingYoutube });

            if (YoutubeStatusText != null)
            {
                if (remaining > 0)
                {
                    YoutubeStatusText.Text = " " + Loc.T("Youtube.DownloadingRemaining", remaining);
                    YoutubeStatusText.Visibility = Visibility.Visible;
                }
                else if (IsDownloadingYoutube)
                {
                    YoutubeStatusText.Text = " " + Loc.T("Youtube.Downloading");
                    YoutubeStatusText.Visibility = Visibility.Visible;
                }
            }
        }

        /// <summary>點 YouTube 影片：已在待播清單或下載佇列中 (以影片 ID 判斷) 時回傳 false。</summary>
        internal bool DownloadYoutubeVideo(SongDisplayItem song)
        {
            string videoId = YoutubeVideoId.Resolve(song.YoutubeId, song.SongId, song.FilePath);
            if (IsYoutubeInWaitingList(videoId, song) || !YoutubeQueue.Enqueue(videoId, song.SongName, song.FilePath, song))
            {
                AppLogger.Log($"YouTube: '{song.SongName}' ({videoId}) is already in the waiting list or download queue, skipped.");
                return false;
            }
            return true;
        }

        /// <summary>待播清單中是否已有這部影片；沒有影片 ID 時 (例如匯入的檔案) 退回比對 SongId。</summary>
        private bool IsYoutubeInWaitingList(string videoId, SongDisplayItem song) =>
            _playback.Snapshot.Any(w => w.IsYoutube && (videoId.Length > 0
                ? YoutubeVideoId.Resolve(null, w.SongId, w.FilePath) == videoId
                : w.SongId == song.SongId));

        private void YoutubeQueue_Changed()
        {
            IsDownloadingYoutube = YoutubeQueue.IsDownloading;
            if (!IsDownloadingYoutube)
            {
                _youtubeShownJob = null;
                CleanupDownloadUI();
                return;
            }

            // 開始下載新的一部：進度歸零並顯示進度
            var current = YoutubeQueue.Current;
            if (current != null && !ReferenceEquals(current, _youtubeShownJob))
            {
                _youtubeShownJob = current;
                YoutubeDownloadPercentage = 0;
                if (YoutubeDownloadText != null) YoutubeDownloadText.Visibility = Visibility.Visible;
                DebugLog($"YouTube Download: Starting for {current.Title} ({current.VideoId})");
            }
            UpdateYoutubeDownloadQueueUI();
        }

        private void YoutubeQueue_Progress(object? sender, YoutubeDownloadProgressEventArgs e)
        {
            YoutubeDownloadPercentage = e.Percentage;

            string status = e.Status.Length > 0 ? e.Status : Loc.T("Youtube.DownloadingEllipsis");
            if (e.Status.Length > 0 && YoutubeStatusText != null)
            {
                int remaining = YoutubeQueue.WaitingCount;
                YoutubeStatusText.Text = remaining > 0 ? " " + Loc.T("Youtube.StatusRemaining", e.Status, remaining) : $" {e.Status}";
                YoutubeStatusText.Visibility = Visibility.Visible;
            }
            HttpServer.BroadcastEvent("YoutubeProgress", new { videoId = e.Job.VideoId, percentage = e.Percentage, status });
        }

        private async void YoutubeQueue_Completed(object? sender, YoutubeDownloadCompletedEventArgs e)
        {
            if (e.Job.Tag is not SongDisplayItem song) return;
            try
            {
                song.FilePath = e.Job.FilePath;
                string recordId = SongLibrary.RecordYoutubeSong(e.Job.VideoId, song.SongName, e.Job.FilePath, song.Language, song.Volume);

                if (!e.WasCached)
                {
                    DebugLog($"YouTube Download: Completed! Path: {e.Job.FilePath}");
                    HttpServer.BroadcastEvent("YoutubeComplete", new { videoId = e.Job.VideoId });

                    // 下載完就寫入長度，點唱紀錄列表不必等到播放才有長度
                    int lengthSeconds = await YtDlpHelper.GetMediaDurationSecondsAsync(e.Job.FilePath);
                    if (lengthSeconds > 0 && !string.IsNullOrEmpty(recordId))
                    {
                        SongLibrary.UpdateYoutubeSongLength(recordId, lengthSeconds);
                        RefreshYoutubeSongLengthInLists(song.SongId, lengthSeconds);
                    }
                }
                else
                {
                    DebugLog($"YouTube: File already exists in cache: {e.Job.FilePath}");
                }

                _ = OrderSongAsync(song, OrderSource.YouTube);

                if (!e.WasCached && _currentQuickMethod == QuickMethod.YoutubeHistory)
                {
                    UpdateSearchWords(true, resetPage: false);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"YouTube: failed to order downloaded song '{song.SongName}'", ex);
            }
        }

        private void YoutubeQueue_Failed(object? sender, YoutubeDownloadFailedEventArgs e)
        {
            DebugLog($"YouTube Download: Error: {e.Error.Message}");
            HttpServer.BroadcastEvent("YoutubeError", new { videoId = e.Job.VideoId, message = e.Error.Message });
            if (e.Job.Tag is not SongDisplayItem song) return;

            // 對話框排到之後顯示，不擋住佇列下載下一部
            Dispatcher.BeginInvoke(() =>
            {
                if (!string.IsNullOrEmpty(song.YoutubeId) && song.SongId.StartsWith("Y"))
                {
                    var result = MessageBox.Show(Loc.T("Youtube.DownloadFailedRemove", e.Error.Message), Loc.T("Youtube.DownloadErrorTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (result == MessageBoxResult.Yes)
                    {
                        SongLibrary.DeleteYoutubeHistory(song.SongId);
                        if (_currentQuickMethod == QuickMethod.YoutubeHistory)
                        {
                            UpdateSearchWords(true, resetPage: false);
                        }
                    }
                }
                else
                {
                    MessageBox.Show(Loc.T("Youtube.DownloadFailed", e.Error.Message), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
        }

        private void YoutubeQueue_Cancelled(object? sender, YoutubeDownloadJob job)
        {
            DebugLog("YouTube Download: Cancelled by user. Clearing queue.");
            HttpServer.BroadcastEvent("YoutubeError", new { videoId = job.VideoId, message = Loc.T("Youtube.UserCancelled") });
        }

        /// <summary>
        /// Updates the "歌曲時間" cell (SingerName) of a YouTube history song in the cached results and the visible page.
        /// songId may be the history Id (e.g. "Y0017") or the video ID.
        /// </summary>
        private void RefreshYoutubeSongLengthInLists(string songId, int lengthSeconds)
        {
            if (string.IsNullOrEmpty(songId) || lengthSeconds <= 0) return;
            string lengthText = TimeSpan.FromSeconds(lengthSeconds).ToString(@"hh\:mm\:ss");
            string durationText = SongDisplayItem.FormatDuration(lengthSeconds);

            bool Matches(SongDisplayItem s) => s.IsYoutube && (s.SongId == songId || s.YoutubeId == songId);

            if (_quickMethodResultsCache.TryGetValue(QuickMethod.YoutubeHistory, out var cached) && cached != null)
            {
                foreach (var item in cached.Where(Matches)) { item.SingerName = lengthText; item.Duration = durationText; }
            }
            foreach (var item in _quickSongListPageItems.Where(Matches)) { item.SingerName = lengthText; item.Duration = durationText; }
        }

        /// <summary>取消下載：停止目前這首並清空下載佇列。</summary>
        private void YoutubeCancelDownloadBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!IsDownloadingYoutube) return;
            AppLogger.Log($"User action: Cancel YouTube download (queued: {YoutubeQueue.WaitingCount})");
            YoutubeQueue.Cancel();
        }

        private void CleanupDownloadUI()
        {
            IsDownloadingYoutube = false;
            YoutubeDownloadPercentage = 0;
            if (YoutubeDownloadText != null) YoutubeDownloadText.Visibility = Visibility.Collapsed;
            if (YoutubeStatusText != null) YoutubeStatusText.Visibility = Visibility.Collapsed;
            UpdateYoutubeDownloadQueueUI();
        }


        private void YoutubeThumbnail_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not SongDisplayItem song) return;

            // Single click orders it (Clone: 縮圖格是固定格子，換頁時內容會被改掉)
            DownloadYoutubeVideo(song.Clone());
        }

        private void YoutubeThumbnail_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not SongDisplayItem song) return;

            try
            {
                // Trigger preview via helper (Clone: 固定格子換頁時內容會被改掉)
                StartYoutubePreview(btn, song.Clone(), immediate: false);
            }
            catch (Exception ex)
            {
                DebugLog($"YouTube Preview Error for {song.SongId}: {ex.Message}");
            }
        }

        private async void StartYoutubePreview(Button btn, SongDisplayItem song, bool immediate)
        {
            _youtubePreviewCts?.Cancel();
            _youtubePreviewCts = new CancellationTokenSource();
            var token = _youtubePreviewCts.Token;

            try
            {
                if (!immediate)
                {
                    // Wait 0.3 seconds for hover
                    await Task.Delay(300);
                    if (token.IsCancellationRequested) return;
                }

                string streamUrl = string.Empty;

                if (_youtubeStreamUrlCache.TryGetValue(song.SongId, out var cachedUrl))
                {
                    streamUrl = cachedUrl;
                 }
                else
                {
                    string previewVideoId = YoutubeVideoId.Resolve(song.YoutubeId, song.SongId, song.FilePath);

                    // Primary engine: yt-dlp
                    if (!token.IsCancellationRequested)
                    {
                        streamUrl = await YtDlpHelper.GetPreviewStreamUrlAsync(previewVideoId, token) ?? string.Empty;
                    }

                    // Fallback engine: YoutubeExplode
                    if (string.IsNullOrEmpty(streamUrl) && !token.IsCancellationRequested && YoutubeVideoId.IsValid(previewVideoId))
                    {
                        try
                        {
                            var streamManifest = await _youtube.Videos.Streams.GetManifestAsync(previewVideoId);
                            var streamInfo = streamManifest.GetMuxedStreams()
                                .OrderBy(s => s.VideoResolution.Height)
                                .FirstOrDefault();

                            if (streamInfo != null)
                            {
                                streamUrl = streamInfo.Url;
                            }
                        }
                        catch { }
                    }

                    if (!string.IsNullOrEmpty(streamUrl) && !token.IsCancellationRequested)
                    {
                        if (_youtubeStreamUrlCache.Count > 100) _youtubeStreamUrlCache.Clear();
                        _youtubeStreamUrlCache[song.SongId] = streamUrl;
                    }
                }

                if (!string.IsNullOrEmpty(streamUrl))
                {
                    // Find the MediaElement and Image inside the button's template (refresh if needed or pass in)
                    var previewPlayer = btn.Template.FindName("PreviewPlayer", btn) as MediaElement;
                    var thumbnailImage = btn.Template.FindName("ThumbnailImage", btn) as Image;
                    var titleBorder = btn.Template.FindName("TitleBorder", btn) as Border;

                    if (previewPlayer != null)
                    {                        
                        // Hook up an event handler just once to catch MediaFailed errors from MediaElement
                        previewPlayer.MediaFailed -= PreviewPlayer_MediaFailed;
                        previewPlayer.MediaFailed += PreviewPlayer_MediaFailed;
                        previewPlayer.MediaOpened -= PreviewPlayer_MediaOpened;
                        previewPlayer.MediaOpened += PreviewPlayer_MediaOpened;

                        // Note: WPF MediaElement inherently has some buffering delay when playing HTTP streams
                        previewPlayer.Source = new Uri(streamUrl);
                        if (thumbnailImage != null) thumbnailImage.Visibility = Visibility.Collapsed;
                        if (titleBorder != null) titleBorder.Visibility = Visibility.Collapsed;
                        previewPlayer.Visibility = Visibility.Visible;
                        previewPlayer.Play();
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                DebugLog($"YouTube Preview Error for {song.SongId}: {ex.Message}");
            }
        }

        private void PreviewPlayer_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
        {
            DebugLog($"PreviewPlayer_MediaFailed: {e.ErrorException?.Message}");
        }

        private void PreviewPlayer_MediaOpened(object sender, RoutedEventArgs e)
        {
            DebugLog($"PreviewPlayer_MediaOpened: Preview successfully opened and playing.");
        }

        private void YoutubeThumbnail_MouseLeave(object sender, MouseEventArgs e)
        {
            _youtubePreviewCts?.Cancel();
            if (sender is Button btn) StopYoutubePreview(btn);
        }

        /// <summary>
        /// 停止所有縮圖格的預覽影片。縮圖格是固定格子，換頁時要先停掉，否則新的一頁會留著上一頁的預覽。
        /// </summary>
        private void StopAllYoutubePreviews()
        {
            _youtubePreviewCts?.Cancel();
            foreach (var btn in FindVisualChildren<Button>(YoutubeThumbnailGrid))
            {
                if (btn.Template?.FindName("PreviewPlayer", btn) is MediaElement { Visibility: Visibility.Visible })
                {
                    StopYoutubePreview(btn);
                }
            }
        }

        private void StopYoutubePreview(Button btn)
        {
            var previewPlayer = btn.Template.FindName("PreviewPlayer", btn) as MediaElement;
            var thumbnailImage = btn.Template.FindName("ThumbnailImage", btn) as Image;
            var titleBorder = btn.Template.FindName("TitleBorder", btn) as Border;
            
            if (previewPlayer != null)
            {
                try
                {
                    // Call Close() first to properly instruct the underlying DirectShow/WMF graph 
                    // to release the HTTP stream instead of just nullifying it which causes SocketExceptions
                    previewPlayer.Stop();
                    previewPlayer.Close();
                    previewPlayer.Source = null;
                }
                catch (Exception)
                {
                    // Ignore background teardown errors
                }
                finally
                {
                    previewPlayer.Visibility = Visibility.Collapsed;
                    // Unhook events to prevent leaks
                    previewPlayer.MediaFailed -= PreviewPlayer_MediaFailed;
                    previewPlayer.MediaOpened -= PreviewPlayer_MediaOpened;
                }
            }
            
            if (thumbnailImage != null) thumbnailImage.Visibility = Visibility.Visible;
            if (titleBorder != null) titleBorder.Visibility = Visibility.Visible;
        }
    }
}
