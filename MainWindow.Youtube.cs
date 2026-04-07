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
        private async void DownloadYoutubeVideo(SongDisplayItem song)
        {
            if (IsDownloadingYoutube)
            {
                MessageBox.Show("已有下載正在進行中，請稍候或取消。", "下載中", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                IsDownloadingYoutube = true;
                YoutubeDownloadPercentage = 0;
                
                // Show progress UI
                if (YoutubeDownloadProgress != null) YoutubeDownloadProgress.Visibility = Visibility.Visible;
                if (YoutubeDownloadText != null) YoutubeDownloadText.Visibility = Visibility.Visible;
                if (YoutubeStatusText != null)
                {
                    YoutubeStatusText.Text = "YouTube 下載中...";
                    YoutubeStatusText.Visibility = Visibility.Visible;
                }

                _youtubeDownloadCts = new CancellationTokenSource();
                var token = _youtubeDownloadCts.Token;

                string cacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "YoutubeCache");
                if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);

                // Sanitize filename
                string safeName = string.Join("_", song.SongName.Split(Path.GetInvalidFileNameChars()));
                string filePath = Path.Combine(cacheDir, $"{safeName}_{song.SongId}.mp4");

                // Check if already exists
                if (File.Exists(filePath))
                {
                    DebugLog($"YouTube: File already exists in cache: {filePath}");
                    song.FilePath = filePath;
                    AddSongToWaitingList(song);
                    CleanupDownloadUI();
                    return;
                }

                DebugLog($"YouTube Download: Starting for {song.SongName} ({song.SongId})");
                
                var streamManifest = await _youtube.Videos.Streams.GetManifestAsync(song.SongId, token);

                // Check user setting for high quality download
                bool isHighQualityEnabled = SettingsManager.Instance.CurrentSettings.HighQualityYoutube;

                IVideoStreamInfo? videoStream = null;
                IAudioStreamInfo? audioStream = null;

                if (isHighQualityEnabled)
                {
                    // Try to get a high-quality video-only stream (up to 1080p, prefer MP4)
                    videoStream = streamManifest.GetVideoOnlyStreams()
                        .Where(s => s.Container == Container.Mp4)
                        .OrderByDescending(s => s.VideoResolution.Height)
                        .FirstOrDefault(s => s.VideoResolution.Height <= 1080);

                    audioStream = (IAudioStreamInfo)streamManifest.GetAudioOnlyStreams()
                        .GetWithHighestBitrate();
                }

                // Download with progress
                var progress = new Progress<double>(p => {
                    YoutubeDownloadPercentage = p * 100;
                    HttpServer.BroadcastEvent("YoutubeProgress", new { videoId = song.SongId, percentage = YoutubeDownloadPercentage });
                });

                if (isHighQualityEnabled && videoStream != null && audioStream != null)
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
                else
                {
                    // Fallback or User preference: muxed stream (up to 720p, no FFmpeg needed)
                    var muxedStream = streamManifest.GetMuxedStreams().GetWithHighestVideoQuality();
                    if (muxedStream == null)
                    {
                        MessageBox.Show("找不到適合的 YouTube 影片串流。", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                        CleanupDownloadUI();
                        return;
                    }
                    DebugLog($"YouTube Download: {(isHighQualityEnabled ? "Fallback" : "User Preference")} using muxed stream.");
                    await _youtube.Videos.Streams.DownloadAsync(muxedStream, filePath, progress, token);
                }

                DebugLog($"YouTube Download: Completed! Path: {filePath}");
                HttpServer.BroadcastEvent("YoutubeComplete", new { videoId = song.SongId });
                
                // Update song path and add to waiting list
                song.FilePath = filePath;
                AddSongToWaitingList(song);
            }
            catch (OperationCanceledException)
            {
                DebugLog("YouTube Download: Cancelled by user.");
                HttpServer.BroadcastEvent("YoutubeError", new { videoId = song.SongId, message = "使用者取消下載" });
            }
            catch (Exception ex)
            {
                DebugLog($"YouTube Download: Error: {ex.Message}");
                HttpServer.BroadcastEvent("YoutubeError", new { videoId = song.SongId, message = ex.Message });
                MessageBox.Show($"下載失敗: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                CleanupDownloadUI();
            }
        }

        private void CleanupDownloadUI()
        {
            IsDownloadingYoutube = false;
            YoutubeDownloadPercentage = 0;
            if (YoutubeDownloadProgress != null) YoutubeDownloadProgress.Visibility = Visibility.Collapsed;
            if (YoutubeDownloadText != null) YoutubeDownloadText.Visibility = Visibility.Collapsed;
            if (YoutubeStatusText != null) YoutubeStatusText.Visibility = Visibility.Collapsed;
            _youtubeDownloadCts?.Dispose();
            _youtubeDownloadCts = null;
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
