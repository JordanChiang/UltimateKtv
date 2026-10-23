using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UltimateKtv
{
    /// <summary>下載一部 YouTube 影片到指定檔案 (正式為 yt-dlp 失敗再改用 YoutubeExplode；測試用假下載器)。</summary>
    public interface IYoutubeDownloader
    {
        /// <param name="progress">0~1 的下載進度；回報 0 表示重新開始 (例如改用備援引擎)。</param>
        /// <param name="status">顯示給使用者的狀態文字。</param>
        Task DownloadAsync(string videoId, string filePath, IProgress<double> progress, Action<string> status, CancellationToken cancellationToken);
    }

    /// <summary>佇列中的一部影片。Tag 由呼叫端放自己的資料 (主程式放點歌用的歌曲項目)。</summary>
    public sealed class YoutubeDownloadJob
    {
        internal YoutubeDownloadJob(string videoId, string title, string filePath, object? tag)
        {
            VideoId = videoId;
            Title = title;
            FilePath = filePath;
            Tag = tag;
        }

        public string VideoId { get; }
        public string Title { get; }
        /// <summary>下載完成 (或快取中已有) 的檔案完整路徑。</summary>
        public string FilePath { get; }
        public object? Tag { get; }
    }

    public sealed class YoutubeDownloadProgressEventArgs : EventArgs
    {
        public YoutubeDownloadProgressEventArgs(YoutubeDownloadJob job, double percentage, string status)
        {
            Job = job;
            Percentage = percentage;
            Status = status;
        }

        public YoutubeDownloadJob Job { get; }
        /// <summary>0~100 的整數百分比，只會遞增 (重新開始時回到 0)。</summary>
        public double Percentage { get; }
        public string Status { get; }
    }

    public sealed class YoutubeDownloadCompletedEventArgs : EventArgs
    {
        public YoutubeDownloadCompletedEventArgs(YoutubeDownloadJob job, bool wasCached)
        {
            Job = job;
            WasCached = wasCached;
        }

        public YoutubeDownloadJob Job { get; }
        /// <summary>下載快取中已有這個檔案，沒有實際下載。</summary>
        public bool WasCached { get; }
    }

    public sealed class YoutubeDownloadFailedEventArgs : EventArgs
    {
        public YoutubeDownloadFailedEventArgs(YoutubeDownloadJob job, Exception error)
        {
            Job = job;
            Error = error;
        }

        public YoutubeDownloadJob Job { get; }
        public Exception Error { get; }
    }

    /// <summary>
    /// YouTube 下載佇列：一次下載一部，依加入順序；同一部影片 (以影片 ID 判斷) 不會重複排隊。
    /// 快取中已有的影片不下載，直接發出 Completed (WasCached)。下載失敗只發出 Failed，接著下載下一部；
    /// Cancel 停止目前這部並清空佇列。事件透過建立時的 SynchronizationContext 發出 (主程式為 UI 執行緒)。
    /// </summary>
    public sealed class YoutubeDownloadQueue
    {
        private readonly IYoutubeDownloader _downloader;
        private readonly Func<string> _cacheDir;
        private readonly SynchronizationContext? _context;
        private readonly object _gate = new();
        private readonly Queue<YoutubeDownloadJob> _waiting = new();
        // 已接受、正在檢查快取檔的影片 ID
        private readonly HashSet<string> _checking = new(StringComparer.Ordinal);
        private bool _running;
        private YoutubeDownloadJob? _current;
        private CancellationTokenSource? _cts;

        /// <param name="cacheDir">下載快取資料夾 (完整路徑)，每次加入時讀取，所以設定變更會生效。</param>
        public YoutubeDownloadQueue(IYoutubeDownloader downloader, Func<string> cacheDir)
        {
            _downloader = downloader;
            _cacheDir = cacheDir;
            _context = SynchronizationContext.Current;
        }

        /// <summary>佇列或下載狀態改變 (加入、開始下載一部、佇列清空)。</summary>
        public event EventHandler? Changed;
        public event EventHandler<YoutubeDownloadProgressEventArgs>? Progress;
        public event EventHandler<YoutubeDownloadCompletedEventArgs>? Completed;
        public event EventHandler<YoutubeDownloadFailedEventArgs>? Failed;
        public event EventHandler<YoutubeDownloadJob>? Cancelled;

        /// <summary>等待下載的數量 (不含正在下載的那部)。</summary>
        public int WaitingCount { get { lock (_gate) return _waiting.Count; } }

        /// <summary>正在下載的那部；沒有時為 null。</summary>
        public YoutubeDownloadJob? Current { get { lock (_gate) return _current; } }

        /// <summary>佇列正在處理 (從第一部開始下載到佇列清空為止)。</summary>
        public bool IsDownloading { get { lock (_gate) return _running; } }

        /// <summary>這部影片是否正在下載、等待下載或檢查快取中。</summary>
        public bool Contains(string videoId)
        {
            if (string.IsNullOrEmpty(videoId)) return false;
            lock (_gate)
                return _checking.Contains(videoId) || _current?.VideoId == videoId || _waiting.Any(j => j.VideoId == videoId);
        }

        /// <summary>
        /// 加入佇列。existingFilePath 是本機檔案路徑時 (例如點唱紀錄中的影片) 直接用它，否則存到快取資料夾。
        /// </summary>
        /// <returns>這部影片已在佇列中時回傳 false。</returns>
        public bool Enqueue(string videoId, string title, string? existingFilePath, object? tag)
        {
            var job = new YoutubeDownloadJob(videoId, title, TargetPath(videoId, title, existingFilePath), tag);
            lock (_gate)
            {
                if (Contains(videoId)) return false;
                if (!string.IsNullOrEmpty(videoId)) _checking.Add(videoId);
            }
            _ = EnqueueAsync(job);
            return true;
        }

        /// <summary>停止目前這部並清空佇列；目前這部發出 Cancelled。</summary>
        public void Cancel()
        {
            lock (_gate) _cts?.Cancel();
        }

        private async Task EnqueueAsync(YoutubeDownloadJob job)
        {
            bool cached = await ExistsAsync(job.FilePath);
            bool start = false;
            lock (_gate)
            {
                _checking.Remove(job.VideoId);
                if (!cached)
                {
                    _waiting.Enqueue(job);
                    start = !_running;
                    _running = true;
                }
            }

            if (cached)
            {
                Raise(() => Completed?.Invoke(this, new YoutubeDownloadCompletedEventArgs(job, wasCached: true)));
                return;
            }
            Raise(() => Changed?.Invoke(this, EventArgs.Empty));
            if (start) await ProcessAsync();
        }

        private async Task ProcessAsync()
        {
            while (true)
            {
                YoutubeDownloadJob job;
                CancellationTokenSource cts;
                lock (_gate)
                {
                    if (_waiting.Count == 0)
                    {
                        _running = false;
                        break;
                    }
                    job = _waiting.Dequeue();
                    _current = job;
                    cts = _cts = new CancellationTokenSource();
                }
                Raise(() => Changed?.Invoke(this, EventArgs.Empty));

                try
                {
                    // 排隊期間可能已經由別的途徑下載好
                    if (await ExistsAsync(job.FilePath))
                    {
                        Finish();
                        Raise(() => Completed?.Invoke(this, new YoutubeDownloadCompletedEventArgs(job, wasCached: true)));
                        continue;
                    }

                    string? folder = Path.GetDirectoryName(job.FilePath);
                    if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

                    double lastPercentage = 0;
                    string lastStatus = "";
                    var progress = new Progress<double>(p =>
                    {
                        double percentage = Math.Round(p * 100);
                        // 影音分開下載時進度會倒退，只往上更新；回報 0 代表重新開始
                        if (p != 0 && percentage <= lastPercentage) return;
                        lastPercentage = percentage;
                        Progress?.Invoke(this, new YoutubeDownloadProgressEventArgs(job, percentage, lastStatus));
                    });
                    void Status(string status)
                    {
                        lastStatus = status;
                        Raise(() => Progress?.Invoke(this, new YoutubeDownloadProgressEventArgs(job, lastPercentage, status)));
                    }

                    await _downloader.DownloadAsync(job.VideoId, job.FilePath, progress, Status, cts.Token);
                    Finish();
                    Raise(() => Completed?.Invoke(this, new YoutubeDownloadCompletedEventArgs(job, wasCached: false)));
                }
                catch (OperationCanceledException)
                {
                    Finish(clearWaiting: true);
                    Raise(() => Cancelled?.Invoke(this, job));
                }
                catch (Exception ex)
                {
                    Finish();
                    Raise(() => Failed?.Invoke(this, new YoutubeDownloadFailedEventArgs(job, ex)));
                }
                finally
                {
                    cts.Dispose();
                }
            }
            Raise(() => Changed?.Invoke(this, EventArgs.Empty));
        }

        /// <summary>目前這部結束 (完成、失敗或取消)；取消時一併清空等待中的影片。</summary>
        private void Finish(bool clearWaiting = false)
        {
            lock (_gate)
            {
                _current = null;
                _cts = null;
                if (clearWaiting) _waiting.Clear();
            }
        }

        private string TargetPath(string videoId, string title, string? existingFilePath)
        {
            if (!string.IsNullOrEmpty(existingFilePath) &&
                !existingFilePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !existingFilePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return Path.IsPathRooted(existingFilePath)
                    ? existingFilePath
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, existingFilePath);
            }
            return Path.Combine(_cacheDir(), YoutubeVideoId.CacheFileName(title, videoId));
        }

        private static Task<bool> ExistsAsync(string path) =>
            Task.Run(() => { try { return File.Exists(path); } catch { return false; } });

        /// <summary>在建立時的執行緒發出事件 (已經在該執行緒時直接執行)。</summary>
        private void Raise(Action action)
        {
            if (_context == null || SynchronizationContext.Current == _context) action();
            else _context.Post(_ => action(), null);
        }
    }
}
