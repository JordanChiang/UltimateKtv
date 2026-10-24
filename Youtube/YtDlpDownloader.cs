using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace UltimateKtv
{
    /// <summary>
    /// 正式的下載器：先用 yt-dlp，失敗 (取消除外) 再改用 YoutubeExplode 分別下載影像與音訊，以 ffmpeg 合併。
    /// </summary>
    public sealed class YtDlpDownloader : IYoutubeDownloader
    {
        private readonly YoutubeClient _youtube;

        public YtDlpDownloader(YoutubeClient youtube) => _youtube = youtube;

        public async Task DownloadAsync(string videoId, string filePath, IProgress<double> progress, Action<string> status, CancellationToken cancellationToken)
        {
            bool highQuality = SettingsManager.Instance.CurrentSettings.HighQualityYoutube;
            try
            {
                await YtDlpHelper.DownloadVideoAsync(videoId, filePath, highQuality, progress, cancellationToken, status);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"YouTube Download: yt-dlp engine failed ({ex.Message}), trying YoutubeExplode fallback...");
            }

            status(Loc.T("Youtube.FallbackDownloading"));
            progress.Report(0);
            try
            {
                await FallbackDownloadAsync(videoId, filePath, highQuality ? 1080 : 720, progress, status, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AppLogger.Log($"YouTube Download: YoutubeExplode fallback also failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// YouTube 已不提供影音合併串流 (只剩 DASH 的純影像/純音訊)，所以分開下載再用 ffmpeg 不轉碼合併成 mp4。
        /// 選擇規則與 yt-dlp 路徑相同 (<see cref="YoutubeFormat.SelectStreams"/>)。沒有分開的串流時才退回合併串流。
        /// </summary>
        private async Task FallbackDownloadAsync(string videoId, string filePath, int maxHeight, IProgress<double> progress, Action<string> status, CancellationToken cancellationToken)
        {
            var manifest = await _youtube.Videos.Streams.GetManifestAsync(videoId, cancellationToken);
            var (video, audio) = YoutubeFormat.SelectStreams(manifest, maxHeight);

            if (video == null || audio == null)
            {
                var muxed = manifest.GetMuxedStreams().TryGetWithHighestVideoQuality()
                    ?? throw new InvalidOperationException($"No downloadable stream for {videoId}");
                AppLogger.Log($"YouTube Download: YoutubeExplode has no separate video/audio streams for {videoId}, using muxed {muxed.VideoQuality}.");
                await _youtube.Videos.Streams.DownloadAsync(muxed, filePath, progress, cancellationToken);
                return;
            }

            AppLogger.Log($"YouTube Download: YoutubeExplode fallback for {videoId}: video {video.VideoQuality} {video.VideoCodec} ({video.Container}), audio {audio.AudioCodec} {audio.Bitrate} ({audio.Container}).");
            string videoPath = $"{filePath}.video.{video.Container.Name}";
            string audioPath = $"{filePath}.audio.{audio.Container.Name}";
            try
            {
                // 進度分配與 yt-dlp 路徑相同：影像 0~75%、音訊 75~90%、合併到 100%
                status(Loc.T("Ytdlp.DownloadingVideo"));
                await _youtube.Videos.Streams.DownloadAsync(video, videoPath, new Progress<double>(p => progress.Report(p * 0.75)), cancellationToken);
                status(Loc.T("Ytdlp.DownloadingAudio"));
                await _youtube.Videos.Streams.DownloadAsync(audio, audioPath, new Progress<double>(p => progress.Report(0.75 + p * 0.15)), cancellationToken);

                status(Loc.T("Ytdlp.Merging"));
                progress.Report(0.9);
                await MergeAsync(videoPath, audioPath, filePath, cancellationToken);
                progress.Report(1.0);
                status(Loc.T("Ytdlp.Done"));
            }
            catch
            {
                TryDelete(filePath); // 合併到一半的檔案不能留著，否則會被當成已下載
                throw;
            }
            finally
            {
                TryDelete(videoPath);
                TryDelete(audioPath);
            }
        }

        private static async Task MergeAsync(string videoPath, string audioPath, string outputPath, CancellationToken cancellationToken)
        {
            string ffmpegPath = Path.Combine(YtDlpHelper.GetFFmpegDir(), "ffmpeg.exe");
            if (!File.Exists(ffmpegPath)) throw new FileNotFoundException("ffmpeg.exe not found", ffmpegPath);

            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-hide_banner -nostdin -loglevel error -y -i \"{videoPath}\" -i \"{audioPath}\" -map 0:v:0 -map 1:a:0 -c copy -movflags +faststart -f mp4 \"{outputPath}\"",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = new Process { StartInfo = psi };
            var stderr = new StringBuilder();
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (stderr) stderr.AppendLine(e.Data); };
            process.Start();
            process.BeginErrorReadLine();
            using (cancellationToken.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } }))
            {
                await process.WaitForExitAsync(cancellationToken);
            }

            if (process.ExitCode != 0 || !File.Exists(outputPath))
            {
                string err;
                lock (stderr) err = stderr.ToString().Trim();
                throw new InvalidOperationException($"ffmpeg merge failed (ExitCode: {process.ExitCode}): {err}");
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
