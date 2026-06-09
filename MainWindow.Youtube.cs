using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using YoutubeExplode;
using YoutubeExplode.Converter;
using YoutubeExplode.Videos.Streams;

namespace UltimateKtv
{
    public partial class MainWindow
    {
        private CancellationTokenSource? _youtubePreviewCts;
        private static readonly ConcurrentDictionary<string, string> _youtubeStreamUrlCache = new();
        private ConcurrentQueue<SongDisplayItem> _youtubeDownloadQueue = new();

        public object GetYoutubeDownloadQueueStatus()
        {
            return new { count = _youtubeDownloadQueue.Count, isDownloading = IsDownloadingYoutube };
        }

        private void UpdateYoutubeDownloadQueueUI()
        {
            int remaining = _youtubeDownloadQueue.Count;
            HttpServer.BroadcastEvent("YoutubeQueueUpdate", new { count = remaining, isDownloading = IsDownloadingYoutube });

            if (YoutubeStatusText != null)
            {
                if (remaining > 0)
                {
                    YoutubeStatusText.Text = $" 下載中.. (剩餘: {remaining})";
                    YoutubeStatusText.Visibility = Visibility.Visible;
                }
                else if (IsDownloadingYoutube)
                {
                    YoutubeStatusText.Text = " 下載中..";
                    YoutubeStatusText.Visibility = Visibility.Visible;
                }
            }
        }

        private void DownloadYoutubeVideo(SongDisplayItem song)
        {
            // Always prevent adding duplicate Youtube songs to queue or waiting list
            if (_waitingList.Any(w => w.SongId == song.SongId) || _youtubeDownloadQueue.Any(q => q.SongId == song.SongId))
            {
                return; // Skip if already in waiting list or queue
            }

            // Quick check if file already exists in cache directory before queueing
            string cacheDir = SettingsManager.Instance.CurrentSettings.YoutubeDownloadDir;
            if (!Path.IsPathRooted(cacheDir)) cacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, cacheDir);
            string videoId = string.IsNullOrEmpty(song.YoutubeId) ? song.SongId : song.YoutubeId;
            string filePath = song.FilePath;
            if (string.IsNullOrEmpty(filePath) || filePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || filePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                string safeName = string.Join("_", song.SongName.Split(Path.GetInvalidFileNameChars()));
                filePath = Path.Combine(cacheDir, $"{safeName}_{videoId}.mp4");
            }
            else if (!Path.IsPathRooted(filePath))
            {
                filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filePath);
            }

            if (File.Exists(filePath))
            {
                DebugLog($"YouTube: File already exists in cache: {filePath}");
                song.FilePath = filePath;
                SongDatas.RecordYoutubeSong(song);
                AddSongToWaitingList(song);
                return;
            }

            _youtubeDownloadQueue.Enqueue(song);
            UpdateYoutubeDownloadQueueUI();

            if (!IsDownloadingYoutube)
            {
                _ = ProcessYoutubeDownloadQueueAsync();
            }
        }

        private async Task ProcessYoutubeDownloadQueueAsync()
        {
            if (IsDownloadingYoutube) return;
            IsDownloadingYoutube = true;

            while (_youtubeDownloadQueue.TryDequeue(out var song))
            {
                UpdateYoutubeDownloadQueueUI();

                try
                {
                    YoutubeDownloadPercentage = 0;
                    
                    // Show progress UI
                    if (YoutubeDownloadText != null) YoutubeDownloadText.Visibility = Visibility.Visible;

                _youtubeDownloadCts = new CancellationTokenSource();
                var token = _youtubeDownloadCts.Token;

                    string cacheDir = SettingsManager.Instance.CurrentSettings.YoutubeDownloadDir;
                    if (!Path.IsPathRooted(cacheDir))
                    {
                        cacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, cacheDir);
                    }
                    if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);

                    string videoId = string.IsNullOrEmpty(song.YoutubeId) ? song.SongId : song.YoutubeId;
                    string filePath = song.FilePath;
                    
                    if (string.IsNullOrEmpty(filePath) || filePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || filePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        string safeName = string.Join("_", song.SongName.Split(Path.GetInvalidFileNameChars()));
                        filePath = Path.Combine(cacheDir, $"{safeName}_{videoId}.mp4");
                    }
                    else if (!Path.IsPathRooted(filePath))
                    {
                        filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filePath);
                    }

                    // Check if already exists
                    if (File.Exists(filePath))
                    {
                        DebugLog($"YouTube: File already exists in cache: {filePath}");
                        song.FilePath = filePath;
                        SongDatas.RecordYoutubeSong(song);
                        AddSongToWaitingList(song);
                        continue;
                    }

                DebugLog($"YouTube Download: Starting for {song.SongName} ({videoId})");
                
                var streamManifest = await _youtube.Videos.Streams.GetManifestAsync(videoId, token);

                // Check user setting for high quality download
                bool isHighQualityEnabled = SettingsManager.Instance.CurrentSettings.HighQualityYoutube;

                IVideoStreamInfo? videoStream = null;
                IAudioStreamInfo? audioStream = null;
                IVideoStreamInfo? muxedStream = null;
                double totalMegaBytes = 0;

                if (isHighQualityEnabled)
                {
                    // Try to get a high-quality video-only stream (up to 1080p, prefer MP4)
                    videoStream = streamManifest.GetVideoOnlyStreams()
                        .Where(s => s.Container == Container.Mp4)
                        .OrderByDescending(s => s.VideoResolution.Height)
                        .FirstOrDefault(s => s.VideoResolution.Height <= 1080);

                    audioStream = (IAudioStreamInfo)streamManifest.GetAudioOnlyStreams()
                        .GetWithHighestBitrate();

                    if (videoStream != null && audioStream != null)
                    {
                        totalMegaBytes = videoStream.Size.MegaBytes + audioStream.Size.MegaBytes;
                    }
                }

                if (totalMegaBytes == 0) // Fallback or user preference
                {
                    muxedStream = streamManifest.GetMuxedStreams().GetWithHighestVideoQuality();
                    if (muxedStream == null)
                    {
                        MessageBox.Show("找不到適合的 YouTube 影片串流。", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                        CleanupDownloadUI();
                        return;
                    }
                    totalMegaBytes = muxedStream.Size.MegaBytes;
                }

                    if (totalMegaBytes > 300)
                    {
                        var result = MessageBox.Show($"此影片檔案較大 (約 {totalMegaBytes:F1} MB)，是否確定要下載？\n下載時間可能會較長。", "檔案較大", MessageBoxButton.YesNo, MessageBoxImage.Question);
                        if (result != MessageBoxResult.Yes)
                        {
                            continue;
                        }
                    }

                // Download with progress
                var progress = new Progress<double>(p => {
                    YoutubeDownloadPercentage = p * 100;
                    HttpServer.BroadcastEvent("YoutubeProgress", new { videoId = song.SongId, percentage = YoutubeDownloadPercentage });
                });

                if (videoStream != null && audioStream != null)
                {
                    // Use FFmpeg to mux separate video + audio → supports 720p / 1080p
                    string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "ffmpeg.exe");
                    var streamInfos = new IStreamInfo[] { videoStream, audioStream };
                    var conversionRequest = new ConversionRequestBuilder(filePath)
                        .SetFFmpegPath(ffmpegPath)
                        .SetContainer(Container.Mp4)
                        .Build();
                    await _youtube.Videos.DownloadAsync(streamInfos, conversionRequest, progress, token);
                }
                else if (muxedStream != null)
                {
                    DebugLog($"YouTube Download: {(isHighQualityEnabled ? "Fallback" : "User Preference")} using muxed stream.");
                    await _youtube.Videos.Streams.DownloadAsync(muxedStream, filePath, progress, token);
                }

                DebugLog($"YouTube Download: Completed! Path: {filePath}");
                HttpServer.BroadcastEvent("YoutubeComplete", new { videoId = song.SongId });
                
                // Update song path, record to database, and add to waiting list
                song.FilePath = filePath;
                SongDatas.RecordYoutubeSong(song);
                AddSongToWaitingList(song);

                    if (_currentQuickMethod == QuickMethod.YoutubeHistory)
                    {
                        UpdateSearchWords(true);
                    }
                }
                catch (OperationCanceledException)
                {
                    DebugLog("YouTube Download: Cancelled by user. Clearing queue.");
                    HttpServer.BroadcastEvent("YoutubeError", new { videoId = song.SongId, message = "使用者取消下載" });
                    _youtubeDownloadQueue.Clear(); // Clear the remaining queue on cancel
                    break;
                }
                catch (Exception ex)
                {
                    DebugLog($"YouTube Download: Error: {ex.Message}");
                    HttpServer.BroadcastEvent("YoutubeError", new { videoId = song.SongId, message = ex.Message });
                    
                    if (!string.IsNullOrEmpty(song.YoutubeId) && song.SongId.StartsWith("Y"))
                    {
                        var result = MessageBox.Show($"下載失敗: {ex.Message}\n這支影片可能已被下架或變更權限。\n是否要將此紀錄從點播歷史中刪除？", "下載錯誤", MessageBoxButton.YesNo, MessageBoxImage.Question);
                        if (result == MessageBoxResult.Yes)
                        {
                            SongDatas.DeleteYoutubeHistory(song.SongId);
                            
                            // Also trigger UI refresh for YoutubeHistory tab
                            if (_currentQuickMethod == QuickMethod.YoutubeHistory)
                            {
                                UpdateSearchWords(true);
                            }
                        }
                    }
                    else
                    {
                        MessageBox.Show($"下載失敗: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }

            CleanupDownloadUI();
        }

        private void CleanupDownloadUI()
        {
            IsDownloadingYoutube = false;
            YoutubeDownloadPercentage = 0;
            if (YoutubeDownloadText != null) YoutubeDownloadText.Visibility = Visibility.Collapsed;
            if (YoutubeStatusText != null) YoutubeStatusText.Visibility = Visibility.Collapsed;
            _youtubeDownloadCts?.Dispose();
            _youtubeDownloadCts = null;
            UpdateYoutubeDownloadQueueUI();
        }


        private void YoutubeThumbnail_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not SongDisplayItem song) return;

            // Single click orders it
            DownloadYoutubeVideo(song);
        }

        private void YoutubeThumbnail_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not SongDisplayItem song) return;

            try
            {
                // Trigger preview via helper
                StartYoutubePreview(btn, song, immediate: false);
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
                    DebugLog($"YoutubeThumbnail_MouseEnter: Using cached stream URL for {song.SongId}");
                }
                else
                {
                    DebugLog($"YoutubeThumbnail_MouseEnter: Fetching manifest for {song.SongId}");
                    // Do NOT pass the cancellation token to GetManifestAsync. 
                    // Cancelling it mid-flight throws TaskCanceledException/IOException in the core libraries 
                    // which spams the output window. Let it finish and just ignore the result.
                    var streamManifest = await _youtube.Videos.Streams.GetManifestAsync(song.SongId);
                    
                    if (token.IsCancellationRequested) return;

                    // Try to get a low-res muxed stream (best for simple preview)
                    var streamInfo = streamManifest.GetMuxedStreams()
                        .OrderBy(s => s.VideoResolution.Height)
                        .FirstOrDefault();

                    if (streamInfo != null)
                    {
                        streamUrl = streamInfo.Url;
                        // Avoid holding memory for too long, but cache enough for the session
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
                        DebugLog($"YoutubeThumbnail_MouseEnter: Stream URL ready, setting source...");
                        
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
                        DebugLog($"YoutubeThumbnail_MouseEnter: Play() called on PreviewPlayer.");
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
            
            if (sender is not Button btn) return;
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
