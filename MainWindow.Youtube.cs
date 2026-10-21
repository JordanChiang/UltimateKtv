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
        private ConcurrentQueue<SongDisplayItem> _youtubeDownloadQueue = new();
        // SongIds accepted but not yet in the waiting list / download queue (during the async file check); UI thread only
        private readonly HashSet<string> _youtubePendingSongIds = new();

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

        /// <returns>false if the song is already in the waiting list or download queue.</returns>
        internal bool DownloadYoutubeVideo(SongDisplayItem song)
        {
            // Always prevent adding duplicate Youtube songs to queue or waiting list
            if (_playback.Snapshot.Any(w => w.SongId == song.SongId) || _youtubeDownloadQueue.Any(q => q.SongId == song.SongId)
                || _youtubePendingSongIds.Contains(song.SongId))
            {
                AppLogger.Log($"YouTube: '{song.SongName}' is already in the waiting list or download queue, skipped.");
                return false;
            }

            _youtubePendingSongIds.Add(song.SongId);
            _ = QueueYoutubeVideoAsync(song);
            return true;
        }

        private async Task QueueYoutubeVideoAsync(SongDisplayItem song)
        {
            try
            {
                // Quick check if file already exists in cache directory before queueing
                string cacheDir = SettingsManager.Instance.CurrentSettings.YoutubeDownloadDir;
                if (!Path.IsPathRooted(cacheDir)) cacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, cacheDir);
                string videoId = YtDlpHelper.ResolveVideoId(song.YoutubeId, song.SongId, song.FilePath);
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

                bool exists = await Task.Run(() =>
                {
                    try { return File.Exists(filePath); } catch { return false; }
                });

                if (exists)
                {
                    DebugLog($"YouTube: File already exists in cache: {filePath}");
                    song.FilePath = filePath;
                    SongDatas.RecordYoutubeSong(song);
                    _ = OrderSongAsync(song, OrderSource.YouTube);
                    return;
                }

                _youtubeDownloadQueue.Enqueue(song);
                UpdateYoutubeDownloadQueueUI();

                if (!IsDownloadingYoutube)
                {
                    _ = ProcessYoutubeDownloadQueueAsync();
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"Failed to queue YouTube song '{song.SongName}'", ex);
            }
            finally
            {
                _youtubePendingSongIds.Remove(song.SongId);
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

                    string videoId = YtDlpHelper.ResolveVideoId(song.YoutubeId, song.SongId, song.FilePath);
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
                        _ = OrderSongAsync(song, OrderSource.YouTube);
                        continue;
                    }

                DebugLog($"YouTube Download: Starting for {song.SongName} ({videoId})");
                bool isHighQualityEnabled = SettingsManager.Instance.CurrentSettings.HighQualityYoutube;
                string currentStatus = Loc.T("Youtube.DownloadingEllipsis");
                var progress = new Progress<double>(p => {
                    double newPct = Math.Round(p * 100);
                    if (p == 0 || newPct > YoutubeDownloadPercentage)
                    {
                        YoutubeDownloadPercentage = newPct;
                        HttpServer.BroadcastEvent("YoutubeProgress", new { videoId = song.SongId, percentage = YoutubeDownloadPercentage, status = currentStatus });
                    }
                });

                bool downloadedSuccess = false;

                // Primary engine: yt-dlp
                try
                {
                    await YtDlpHelper.DownloadVideoAsync(videoId, filePath, isHighQualityEnabled, progress, token, status =>
                    {
                        currentStatus = status;
                        Dispatcher.BeginInvoke(() =>
                        {
                            if (YoutubeStatusText != null)
                            {
                                int rem = _youtubeDownloadQueue.Count;
                                YoutubeStatusText.Text = rem > 0 ? " " + Loc.T("Youtube.StatusRemaining", status, rem) : $" {status}";
                                YoutubeStatusText.Visibility = Visibility.Visible;
                            }
                        });
                        HttpServer.BroadcastEvent("YoutubeProgress", new { videoId = song.SongId, percentage = YoutubeDownloadPercentage, status = currentStatus });
                    });
                    downloadedSuccess = true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    DebugLog($"YouTube Download: yt-dlp engine failed ({ex.Message}), trying YoutubeExplode fallback...");
                }

                // Backup fallback engine: YoutubeExplode
                if (!downloadedSuccess)
                {
                    try
                    {
                        currentStatus = Loc.T("Youtube.FallbackDownloading");
                        YoutubeDownloadPercentage = 0;
                        _ = Dispatcher.BeginInvoke(() =>
                        {
                            if (YoutubeStatusText != null)
                            {
                                int rem = _youtubeDownloadQueue.Count;
                                YoutubeStatusText.Text = rem > 0 ? " " + Loc.T("Youtube.StatusRemaining", Loc.T("Youtube.FallbackDownloading"), rem) : " " + Loc.T("Youtube.FallbackDownloading");
                                YoutubeStatusText.Visibility = Visibility.Visible;
                            }
                        });
                        HttpServer.BroadcastEvent("YoutubeProgress", new { videoId = song.SongId, percentage = 0, status = currentStatus });

                        var streamManifest = await _youtube.Videos.Streams.GetManifestAsync(videoId, token);
                        var muxedStream = streamManifest.GetMuxedStreams().GetWithHighestVideoQuality();
                        if (muxedStream != null)
                        {
                            await _youtube.Videos.Streams.DownloadAsync(muxedStream, filePath, progress, token);
                            downloadedSuccess = true;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        DebugLog($"YouTube Download: YoutubeExplode fallback also failed: {ex.Message}");
                        throw;
                    }
                }

                if (downloadedSuccess)
                {
                    DebugLog($"YouTube Download: Completed! Path: {filePath}");
                    HttpServer.BroadcastEvent("YoutubeComplete", new { videoId = song.SongId });
                    
                    // Update song path, record to database, and add to waiting list
                    song.FilePath = filePath;
                    string recordId = SongDatas.RecordYoutubeSong(song);

                    // Store the duration right away so the history list shows it without waiting for playback
                    int lengthSeconds = await YtDlpHelper.GetMediaDurationSecondsAsync(filePath);
                    if (lengthSeconds > 0 && !string.IsNullOrEmpty(recordId))
                    {
                        SongDatas.UpdateYoutubeSongLength(recordId, lengthSeconds);
                        RefreshYoutubeSongLengthInLists(song.SongId, lengthSeconds);
                    }

                    _ = OrderSongAsync(song, OrderSource.YouTube);

                    if (_currentQuickMethod == QuickMethod.YoutubeHistory)
                    {
                        UpdateSearchWords(true, resetPage: false);
                    }
                }
                }
                catch (OperationCanceledException)
                {
                    DebugLog("YouTube Download: Cancelled by user. Clearing queue.");
                    HttpServer.BroadcastEvent("YoutubeError", new { videoId = song.SongId, message = Loc.T("Youtube.UserCancelled") });
                    _youtubeDownloadQueue.Clear(); // Clear the remaining queue on cancel
                    break;
                }
                catch (Exception ex)
                {
                    DebugLog($"YouTube Download: Error: {ex.Message}");
                    HttpServer.BroadcastEvent("YoutubeError", new { videoId = song.SongId, message = ex.Message });
                    
                    if (!string.IsNullOrEmpty(song.YoutubeId) && song.SongId.StartsWith("Y"))
                    {
                        var result = MessageBox.Show(Loc.T("Youtube.DownloadFailedRemove", ex.Message), Loc.T("Youtube.DownloadErrorTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
                        if (result == MessageBoxResult.Yes)
                        {
                            SongDatas.DeleteYoutubeHistory(song.SongId);
                            
                            // Also trigger UI refresh for YoutubeHistory tab
                            if (_currentQuickMethod == QuickMethod.YoutubeHistory)
                            {
                                UpdateSearchWords(true, resetPage: false);
                            }
                        }
                    }
                    else
                    {
                        MessageBox.Show(Loc.T("Youtube.DownloadFailed", ex.Message), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }

            CleanupDownloadUI();
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
                 }
                else
                {
                    string previewVideoId = YtDlpHelper.ResolveVideoId(song.YoutubeId, song.SongId, song.FilePath);

                    // Primary engine: yt-dlp
                    if (!token.IsCancellationRequested)
                    {
                        streamUrl = await YtDlpHelper.GetPreviewStreamUrlAsync(previewVideoId, token) ?? string.Empty;
                    }

                    // Fallback engine: YoutubeExplode
                    if (string.IsNullOrEmpty(streamUrl) && !token.IsCancellationRequested && YtDlpHelper.IsValidVideoId(previewVideoId))
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
