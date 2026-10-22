using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace UltimateKtv
{
    /// <summary>
    /// YouTube 搜尋結果縮圖快取 (網址 → 縮小後的點陣圖)。
    /// - 在背景下載並以 DecodePixelHeight 直接解碼成顯示尺寸；原本 Image 直接綁網址時，WPF 會在 UI 執行緒
    ///   解碼原尺寸 (最高到 1280x720) 的圖，換頁時一次 16 張，跑馬燈 (DirectShow 的 WPF 動畫) 因此卡頓。
    /// - 解碼後 Freeze()，可跨執行緒使用，也讓上傳到 GPU 的貼圖變小。
    /// - LRU 快取：翻回已看過的頁面時立即顯示。
    /// 作法與 <see cref="SingerThumbnailCache"/> 相同。
    /// </summary>
    public static class YoutubeThumbnailCache
    {
        /// <summary>解碼高度 (像素)。縮圖格在 1920x1080 設計尺寸下約 210 DIP 高，留一點餘裕。</summary>
        public const int DecodePixelHeight = 240;

        /// <summary>LRU 容量 (張)。每張約 0.3 MB，96 張約 6 頁。</summary>
        private const int Capacity = 96;

        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        private static readonly object _lock = new object();
        private static readonly Dictionary<string, LinkedListNode<KeyValuePair<string, BitmapSource>>> _map =
            new Dictionary<string, LinkedListNode<KeyValuePair<string, BitmapSource>>>(StringComparer.Ordinal);
        private static readonly LinkedList<KeyValuePair<string, BitmapSource>> _lru = new LinkedList<KeyValuePair<string, BitmapSource>>();

        private static readonly ConcurrentDictionary<string, Task<BitmapSource?>> _inflight =
            new ConcurrentDictionary<string, Task<BitmapSource?>>(StringComparer.Ordinal);

        // 限制同時下載/解碼數量，避免一次建立大量點陣圖造成記憶體壓力並跟 render thread 搶 CPU
        private static readonly SemaphoreSlim _decodeGate = new SemaphoreSlim(3);

        // 預載的世代編號：快速連續翻頁時，舊的預載工作直接放棄
        private static int _prefetchGeneration;

        /// <summary>嘗試從快取取得縮圖 (UI 執行緒呼叫，不做任何 I/O)。</summary>
        public static bool TryGet(string url, out BitmapSource? image)
        {
            lock (_lock)
            {
                if (_map.TryGetValue(url, out var node))
                {
                    _lru.Remove(node);
                    _lru.AddFirst(node);
                    image = node.Value.Value;
                    return true;
                }
            }
            image = null;
            return false;
        }

        /// <summary>取得縮圖；不在快取中則於背景下載解碼。相同網址的並行請求共用同一個 Task。</summary>
        public static Task<BitmapSource?> GetAsync(string? url)
        {
            if (string.IsNullOrEmpty(url)) return Task.FromResult<BitmapSource?>(null);
            if (TryGet(url, out var cached)) return Task.FromResult(cached);

            var task = _inflight.GetOrAdd(url, u => Task.Run(() => DownloadAndStoreAsync(u)));
            if (task.IsCompleted)
            {
                _inflight.TryRemove(new KeyValuePair<string, Task<BitmapSource?>>(url, task));
            }
            else
            {
                task.ContinueWith(t => _inflight.TryRemove(new KeyValuePair<string, Task<BitmapSource?>>(url, t)),
                    TaskScheduler.Default);
            }
            return task;
        }

        /// <summary>預先載入 (例如下一頁)，不等待結果；有新的預載要求時，舊的立即停止。</summary>
        public static void Prefetch(IEnumerable<string?> urls)
        {
            int generation = Interlocked.Increment(ref _prefetchGeneration);
            var list = new List<string>();
            foreach (var u in urls)
            {
                if (!string.IsNullOrEmpty(u)) list.Add(u);
            }
            if (list.Count == 0) return;

            _ = Task.Run(async () =>
            {
                foreach (var u in list)
                {
                    if (generation != Volatile.Read(ref _prefetchGeneration)) return;
                    if (TryGet(u, out _)) continue;
                    try
                    {
                        await GetAsync(u).ConfigureAwait(false);
                    }
                    catch
                    {
                        // 錯誤已在下載/解碼時記錄
                    }
                }
            });
        }

        private static async Task<BitmapSource?> DownloadAndStoreAsync(string url)
        {
            await _decodeGate.WaitAsync().ConfigureAwait(false);
            try
            {
                // 另一個請求可能已經完成
                if (TryGet(url, out var cached)) return cached;

                byte[] data;
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    data = await _http.GetByteArrayAsync(uri).ConfigureAwait(false);
                }
                else
                {
                    data = await File.ReadAllBytesAsync(url).ConfigureAwait(false);
                }

                var bmp = Decode(data);
                if (bmp != null) Store(url, bmp);
                return bmp;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"YoutubeThumbnailCache: failed to load {url}: {ex.Message}");
                return null;
            }
            finally
            {
                _decodeGate.Release();
            }
        }

        private static BitmapSource? Decode(byte[] data)
        {
            using var ms = new MemoryStream(data);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad; // EndInit 時就讀完
            bmp.DecodePixelHeight = DecodePixelHeight;   // 只解碼成顯示尺寸 (保持比例)
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        private static void Store(string url, BitmapSource image)
        {
            lock (_lock)
            {
                if (_map.TryGetValue(url, out var existing))
                {
                    _lru.Remove(existing);
                    _map.Remove(url);
                }

                var node = new LinkedListNode<KeyValuePair<string, BitmapSource>>(new KeyValuePair<string, BitmapSource>(url, image));
                _lru.AddFirst(node);
                _map[url] = node;

                while (_lru.Count > Capacity)
                {
                    var last = _lru.Last!;
                    _lru.RemoveLast();
                    _map.Remove(last.Value.Key);
                }
            }
        }
    }
}
