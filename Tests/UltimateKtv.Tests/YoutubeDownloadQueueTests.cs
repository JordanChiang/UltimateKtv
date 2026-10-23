using System.Collections.Concurrent;
using Xunit;

namespace UltimateKtv.Tests
{
    public class YoutubeDownloadQueueTests : IDisposable
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
        private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), "ukt-yt-" + Guid.NewGuid().ToString("N"));
        private readonly FakeDownloader _downloader = new();
        private readonly YoutubeDownloadQueue _queue;
        private readonly BlockingCollection<string> _events = new();

        public YoutubeDownloadQueueTests()
        {
            _queue = new YoutubeDownloadQueue(_downloader, () => _cacheDir);
            _queue.Completed += (_, e) => _events.Add($"completed {e.Job.VideoId}{(e.WasCached ? " cached" : "")}");
            _queue.Failed += (_, e) => _events.Add($"failed {e.Job.VideoId}");
            _queue.Cancelled += (_, job) => _events.Add($"cancelled {job.VideoId}");
        }

        public void Dispose()
        {
            try { Directory.Delete(_cacheDir, recursive: true); } catch { }
        }

        private string NextEvent()
        {
            Assert.True(_events.TryTake(out var e, Timeout), "timed out waiting for a queue event");
            return e!;
        }

        private void WaitUntilWaiting(int count)
        {
            var deadline = DateTime.UtcNow + Timeout;
            while (_queue.WaitingCount != count && DateTime.UtcNow < deadline) Thread.Sleep(10);
            Assert.Equal(count, _queue.WaitingCount);
        }

        private void WaitUntilIdle()
        {
            var deadline = DateTime.UtcNow + Timeout;
            while (_queue.IsDownloading && DateTime.UtcNow < deadline) Thread.Sleep(10);
            Assert.False(_queue.IsDownloading);
        }

        [Fact]
        public void Downloads_one_at_a_time_in_order_into_the_cache_folder()
        {
            _downloader.Block("aaaaaaaaaaa");
            Assert.True(_queue.Enqueue("aaaaaaaaaaa", "第一首", null, null));
            _downloader.WaitStarted("aaaaaaaaaaa");
            Assert.True(_queue.Enqueue("bbbbbbbbbbb", "第二首", "https://www.youtube.com/watch?v=bbbbbbbbbbb", null));
            WaitUntilWaiting(1);

            _downloader.Release("aaaaaaaaaaa");
            Assert.Equal("completed aaaaaaaaaaa", NextEvent());
            Assert.Equal("completed bbbbbbbbbbb", NextEvent());
            WaitUntilIdle();

            Assert.Equal(new[] { "aaaaaaaaaaa", "bbbbbbbbbbb" }, _downloader.Downloaded);
            Assert.True(File.Exists(Path.Combine(_cacheDir, "第一首_aaaaaaaaaaa.mp4")));
            Assert.True(File.Exists(Path.Combine(_cacheDir, "第二首_bbbbbbbbbbb.mp4")));
        }

        [Fact]
        public void The_same_video_is_not_queued_twice_while_waiting_or_downloading()
        {
            _downloader.Block("aaaaaaaaaaa");
            Assert.True(_queue.Enqueue("aaaaaaaaaaa", "A", null, null));
            Assert.False(_queue.Enqueue("aaaaaaaaaaa", "A 另一個標題", null, null));
            _downloader.WaitStarted("aaaaaaaaaaa");
            Assert.False(_queue.Enqueue("aaaaaaaaaaa", "A", null, null));
            Assert.True(_queue.Contains("aaaaaaaaaaa"));

            _downloader.Release("aaaaaaaaaaa");
            Assert.Equal("completed aaaaaaaaaaa", NextEvent());
            WaitUntilIdle();
            Assert.False(_queue.Contains("aaaaaaaaaaa"));
        }

        [Fact]
        public void A_video_already_in_the_cache_is_not_downloaded_again()
        {
            Directory.CreateDirectory(_cacheDir);
            File.WriteAllText(Path.Combine(_cacheDir, "A_aaaaaaaaaaa.mp4"), "video");

            Assert.True(_queue.Enqueue("aaaaaaaaaaa", "A", null, null));

            Assert.Equal("completed aaaaaaaaaaa cached", NextEvent());
            Assert.Empty(_downloader.Downloaded);
        }

        [Fact]
        public void A_local_file_from_the_history_is_used_as_the_target()
        {
            string historyFile = Path.Combine(_cacheDir, "舊的", "A_aaaaaaaaaaa.mp4");
            YoutubeDownloadJob? completed = null;
            _queue.Completed += (_, e) => completed = e.Job;

            Assert.True(_queue.Enqueue("aaaaaaaaaaa", "A", historyFile, null));

            Assert.Equal("completed aaaaaaaaaaa", NextEvent());
            Assert.Equal(historyFile, completed!.FilePath);
            Assert.True(File.Exists(historyFile));
        }

        [Fact]
        public void A_failed_download_does_not_stop_the_queue()
        {
            _downloader.Fail("aaaaaaaaaaa");
            _downloader.Block("aaaaaaaaaaa");
            _queue.Enqueue("aaaaaaaaaaa", "A", null, null);
            _downloader.WaitStarted("aaaaaaaaaaa");
            _queue.Enqueue("bbbbbbbbbbb", "B", null, null);
            WaitUntilWaiting(1);
            _downloader.Release("aaaaaaaaaaa");

            Assert.Equal("failed aaaaaaaaaaa", NextEvent());
            Assert.Equal("completed bbbbbbbbbbb", NextEvent());
            WaitUntilIdle();
        }

        [Fact]
        public void Cancel_stops_the_current_download_and_clears_the_queue()
        {
            _downloader.Block("aaaaaaaaaaa");
            _queue.Enqueue("aaaaaaaaaaa", "A", null, null);
            _downloader.WaitStarted("aaaaaaaaaaa");
            _queue.Enqueue("bbbbbbbbbbb", "B", null, null);
            WaitUntilWaiting(1);

            _queue.Cancel();

            Assert.Equal("cancelled aaaaaaaaaaa", NextEvent());
            WaitUntilIdle();
            Assert.Equal(0, _queue.WaitingCount);
            Assert.False(_events.TryTake(out _, TimeSpan.FromMilliseconds(200)));
            Assert.DoesNotContain("bbbbbbbbbbb", _downloader.Started);
        }

        [Fact]
        public async Task Progress_only_moves_forward_until_a_restart()
        {
            // 與主程式相同：佇列建立在單一執行緒的 SynchronizationContext，進度依序回到該執行緒
            using var ui = new SingleThreadContext();
            var seen = new List<double>();
            var done = new TaskCompletionSource();
            _downloader.ProgressSteps = new[] { 0.1, 0.5, 0.3, 0.6, 0.0, 0.2 };
            ui.Run(() =>
            {
                var queue = new YoutubeDownloadQueue(_downloader, () => _cacheDir);
                queue.Progress += (_, e) => seen.Add(e.Percentage);
                queue.Completed += (_, _) => ui.Post(() => done.TrySetResult());
                queue.Enqueue("aaaaaaaaaaa", "A", null, null);
            });

            await done.Task.WaitAsync(Timeout);
            ui.Run(() => { });
            Assert.Equal(new double[] { 10, 50, 60, 0, 20 }, seen);
        }

        /// <summary>單一執行緒的 SynchronizationContext (模擬 UI 執行緒)。</summary>
        private sealed class SingleThreadContext : SynchronizationContext, IDisposable
        {
            private readonly BlockingCollection<Action> _work = new();
            private readonly Thread _thread;

            public SingleThreadContext()
            {
                _thread = new Thread(() =>
                {
                    SetSynchronizationContext(this);
                    foreach (var action in _work.GetConsumingEnumerable()) action();
                }) { IsBackground = true };
                _thread.Start();
            }

            public override void Post(SendOrPostCallback d, object? state) => _work.Add(() => d(state));
            public void Post(Action action) => _work.Add(action);

            /// <summary>在該執行緒執行並等它做完 (也等之前排入的工作)。</summary>
            public void Run(Action action)
            {
                using var finished = new ManualResetEventSlim();
                _work.Add(() => { action(); finished.Set(); });
                Assert.True(finished.Wait(Timeout));
            }

            public void Dispose() => _work.CompleteAdding();
        }

        [Fact]
        public void Video_id_comes_from_the_youtube_id_then_song_id_then_file_name()
        {
            Assert.Equal("aaaaaaaaaaa", YoutubeVideoId.Resolve("aaaaaaaaaaa", "bbbbbbbbbbb", "x_ccccccccccc.mp4"));
            Assert.Equal("bbbbbbbbbbb", YoutubeVideoId.Resolve("", "bbbbbbbbbbb", "x_ccccccccccc.mp4"));
            Assert.Equal("ccccccccccc", YoutubeVideoId.Resolve(null, "Y0017", @"D:\yt\歌_ccccccccccc.mp4"));
            Assert.Equal("", YoutubeVideoId.Resolve(null, "Y0017", @"D:\yt\沒有編號.mp4"));
        }

        [Fact]
        public void Cache_file_name_keeps_the_title_and_replaces_invalid_characters()
        {
            Assert.Equal("A_B_aaaaaaaaaaa.mp4", YoutubeVideoId.CacheFileName("A/B", "aaaaaaaaaaa"));
            Assert.Equal("A_B", YoutubeVideoId.TitleFromFileName("A_B_aaaaaaaaaaa.mp4"));
            Assert.Equal("aaaaaaaaaaa", YoutubeVideoId.FromFileName("A_B_aaaaaaaaaaa.mp4"));
        }

        private sealed class FakeDownloader : IYoutubeDownloader
        {
            private readonly ConcurrentDictionary<string, TaskCompletionSource> _gates = new();
            private readonly ConcurrentDictionary<string, TaskCompletionSource> _started = new();
            private readonly HashSet<string> _failing = new();
            public readonly ConcurrentQueue<string> Started = new();
            public readonly List<string> Downloaded = new();
            public double[] ProgressSteps { get; set; } = Array.Empty<double>();

            public void Block(string videoId) => _gates[videoId] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            public void Release(string videoId) => _gates[videoId].TrySetResult();
            public void Fail(string videoId) { lock (_failing) _failing.Add(videoId); }

            public void WaitStarted(string videoId) =>
                Assert.True(StartedSignal(videoId).Task.Wait(Timeout), $"{videoId} did not start");

            private TaskCompletionSource StartedSignal(string videoId) =>
                _started.GetOrAdd(videoId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

            public async Task DownloadAsync(string videoId, string filePath, IProgress<double> progress, Action<string> status, CancellationToken cancellationToken)
            {
                Started.Enqueue(videoId);
                StartedSignal(videoId).TrySetResult();
                foreach (var p in ProgressSteps) progress.Report(p);
                if (_gates.TryGetValue(videoId, out var gate)) await gate.Task.WaitAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                lock (_failing)
                {
                    if (_failing.Contains(videoId)) throw new IOException("download failed");
                }
                File.WriteAllText(filePath, "video");
                lock (Downloaded) Downloaded.Add(videoId);
            }
        }
    }
}
