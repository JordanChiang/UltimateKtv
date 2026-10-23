using System;
using System.Threading;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace UltimateKtv
{
    /// <summary>
    /// 正式的下載器：先用 yt-dlp，失敗 (取消除外) 再改用 YoutubeExplode 下載單一檔案的影音串流。
    /// </summary>
    public sealed class YtDlpDownloader : IYoutubeDownloader
    {
        private readonly YoutubeClient _youtube;

        public YtDlpDownloader(YoutubeClient youtube) => _youtube = youtube;

        public async Task DownloadAsync(string videoId, string filePath, IProgress<double> progress, Action<string> status, CancellationToken cancellationToken)
        {
            try
            {
                bool highQuality = SettingsManager.Instance.CurrentSettings.HighQualityYoutube;
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
                var streamManifest = await _youtube.Videos.Streams.GetManifestAsync(videoId, cancellationToken);
                var muxedStream = streamManifest.GetMuxedStreams().GetWithHighestVideoQuality()
                    ?? throw new InvalidOperationException($"No downloadable stream for {videoId}");
                await _youtube.Videos.Streams.DownloadAsync(muxedStream, filePath, progress, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AppLogger.Log($"YouTube Download: YoutubeExplode fallback also failed: {ex.Message}");
                throw;
            }
        }
    }
}
