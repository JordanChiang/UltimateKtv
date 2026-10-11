using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace UltimateKtv
{
    /// <summary>
    /// 歌手照片縮圖快取。
    /// - 在背景執行緒解碼，並以 DecodePixelHeight 直接解碼成顯示尺寸 (不在 UI 執行緒解碼原圖)。
    /// - 解碼後 Freeze()，可跨執行緒使用，也讓 WPF 上傳到 GPU 的貼圖變小 (減輕 render thread 負擔)。
    /// - LRU 快取：翻回已看過的頁面時可立即顯示。
    /// 目的：切換功能 / 歌手列表翻頁時不阻塞 UI 執行緒，避免跑馬燈 (DoubleAnimation) 卡頓。
    /// </summary>
    public static class SingerThumbnailCache
    {
        /// <summary>
        /// 解碼高度 (像素)。歌手格子在 1920x1080 設計尺寸下約 295 DIP 高，留一點餘裕給高 DPI。
        /// </summary>
        public const int DecodePixelHeight = 320;

        /// <summary>LRU 容量 (張)。每張約 0.3~0.4 MB，180 張約 60 MB，大約 10 頁。</summary>
        private const int Capacity = 180;

        private const string DefaultImageUri = "pack://application:,,,/Images/FavoriteUser/favoriteuser_default.png";

        private static readonly object _lock = new object();
        private static readonly Dictionary<string, LinkedListNode<KeyValuePair<string, BitmapSource>>> _map =
            new Dictionary<string, LinkedListNode<KeyValuePair<string, BitmapSource>>>(StringComparer.OrdinalIgnoreCase);
        private static readonly LinkedList<KeyValuePair<string, BitmapSource>> _lru = new LinkedList<KeyValuePair<string, BitmapSource>>();

        private static readonly ConcurrentDictionary<string, Task<BitmapSource?>> _inflight =
            new ConcurrentDictionary<string, Task<BitmapSource?>>(StringComparer.OrdinalIgnoreCase);

        // 限制同時解碼數量：一次大量建立點陣圖會讓 WPF 的記憶體壓力暴增而強制 GC，
        // 也會跟 render thread 搶 CPU。2 個就足夠在一瞬間補齊一頁 18 張縮圖。
        private static readonly SemaphoreSlim _decodeGate = new SemaphoreSlim(2);

        // 預載的世代編號：快速連續翻頁時，舊的預載工作直接放棄
        private static int _prefetchGeneration;

        private static BitmapSource? _defaultImage;

        /// <summary>
        /// 沒有照片時使用的預設剪影圖 (只載入一次並 Freeze)。
        /// </summary>
        public static BitmapSource? DefaultImage
        {
            get
            {
                if (_defaultImage != null) return _defaultImage;
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(DefaultImageUri, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    _defaultImage = bmp;
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("SingerThumbnailCache: failed to load default image", ex);
                }
                return _defaultImage;
            }
        }

        /// <summary>
        /// 嘗試從快取取得縮圖 (UI 執行緒呼叫，不做任何 I/O)。
        /// </summary>
        public static bool TryGet(string path, out BitmapSource? image)
        {
            lock (_lock)
            {
                if (_map.TryGetValue(path, out var node))
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

        /// <summary>
        /// 取得縮圖；若不在快取中則於背景解碼。相同路徑的並行請求會共用同一個 Task。
        /// </summary>
        public static Task<BitmapSource?> GetAsync(string path)
        {
            if (string.IsNullOrEmpty(path)) return Task.FromResult<BitmapSource?>(null);
            if (TryGet(path, out var cached)) return Task.FromResult(cached);

            var task = _inflight.GetOrAdd(path, p => Task.Run(() => DecodeAndStoreAsync(p)));
            if (task.IsCompleted)
            {
                _inflight.TryRemove(new KeyValuePair<string, Task<BitmapSource?>>(path, task));
            }
            else
            {
                task.ContinueWith(t => _inflight.TryRemove(new KeyValuePair<string, Task<BitmapSource?>>(path, t)),
                    TaskScheduler.Default);
            }
            return task;
        }

        /// <summary>
        /// 預先載入 (例如下一頁)，不等待結果。
        /// 一張一張依序解碼 (不跟目前頁面的載入搶)；有新的預載要求時，舊的立即停止。
        /// </summary>
        public static void Prefetch(IEnumerable<string> paths)
        {
            int generation = Interlocked.Increment(ref _prefetchGeneration);
            var list = new List<string>();
            foreach (var p in paths)
            {
                if (!string.IsNullOrEmpty(p)) list.Add(p);
            }
            if (list.Count == 0) return;

            _ = Task.Run(async () =>
            {
                foreach (var p in list)
                {
                    if (generation != Volatile.Read(ref _prefetchGeneration)) return;
                    if (TryGet(p, out _)) continue;
                    try
                    {
                        await GetAsync(p).ConfigureAwait(false);
                    }
                    catch
                    {
                        // 解碼錯誤已在 Decode 中記錄
                    }
                }
            });
        }

        /// <summary>清除快取 (例如照片資料夾變更時)。</summary>
        public static void Clear()
        {
            lock (_lock)
            {
                _map.Clear();
                _lru.Clear();
            }
        }

        private static async Task<BitmapSource?> DecodeAndStoreAsync(string path)
        {
            await _decodeGate.WaitAsync().ConfigureAwait(false);
            try
            {
                // 另一個請求可能已經完成
                if (TryGet(path, out var cached)) return cached;

                var bmp = Decode(path);
                if (bmp != null) Store(path, bmp);
                return bmp;
            }
            finally
            {
                _decodeGate.Release();
            }
        }

        private static BitmapSource? Decode(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad; // EndInit 時就讀完，之後可關閉檔案
                bmp.DecodePixelHeight = DecodePixelHeight;   // 只解碼成顯示尺寸 (保持比例)
                bmp.StreamSource = fs;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"SingerThumbnailCache: failed to decode {path}", ex);
                return null;
            }
        }

        private static void Store(string path, BitmapSource image)
        {
            lock (_lock)
            {
                if (_map.TryGetValue(path, out var existing))
                {
                    _lru.Remove(existing);
                    _map.Remove(path);
                }

                var node = new LinkedListNode<KeyValuePair<string, BitmapSource>>(new KeyValuePair<string, BitmapSource>(path, image));
                _lru.AddFirst(node);
                _map[path] = node;

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
