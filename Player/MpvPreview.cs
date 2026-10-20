using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace UltimateKtv
{
    /// <summary>
    /// 主視窗預覽：第二個 libmpv 實例 (無聲) 以軟體渲染畫進記憶體，再送進 D3DImage 顯示
    /// (D3D9 不可用時退回 InteropBitmap)。
    /// 結果是一般的 WPF Image，主視窗的跑馬燈、對話框照常疊在上面 (不能用原生視窗，否則會蓋住它們)。
    /// 先用 vf 縮到預覽大小再渲染，1080p 實測約 +1% CPU (Ryzen 5 5600X)。
    /// </summary>
    internal sealed class MpvPreview : IDisposable
    {
        private const int W = 480, H = 270;   // 16:9；Image 以 Uniform 縮放到預覽區
        private const int Stride = W * 4;     // 1920，為 64 的倍數 (SW render 對齊需求)

        private readonly Dispatcher _dispatcher;
        private readonly D3DImageSurface? _surface;
        private readonly InteropBitmap? _bitmap;
        private readonly IntPtr _section;
        private readonly Image _image;
        private readonly IntPtr _ctx;
        private readonly IntPtr _render;
        private readonly IntPtr _buffer;
        private readonly IntPtr _renderParams;
        private readonly IntPtr[] _paramAllocs;
        private readonly MpvNative.RenderUpdateCallback _updateCallback; // 保持參考，避免被 GC
        private readonly AutoResetEvent _renderSignal = new(false);
        private readonly object _bufferLock = new();
        private readonly Thread _renderThread, _eventThread;
        private volatile bool _closing;
        private int _uiPending;
        private volatile bool _loaded;

        public FrameworkElement View => _image;

        private MpvPreview(Dispatcher dispatcher, IntPtr ctx, IntPtr render)
        {
            _dispatcher = dispatcher;
            _ctx = ctx;
            _render = render;
            // mpv 的軟體渲染目標：共享記憶體 (MapViewOfFile 為 64KB 對齊，符合 SW render 的 64 位元組對齊需求)。
            _section = CreateFileMapping(new IntPtr(-1), IntPtr.Zero, PAGE_READWRITE, 0, (uint)(Stride * H), null);
            if (_section == IntPtr.Zero) throw new InvalidOperationException("CreateFileMapping failed: " + Marshal.GetLastWin32Error());
            _buffer = MapViewOfFile(_section, FILE_MAP_ALL_ACCESS, 0, 0, (UIntPtr)(uint)(Stride * H));
            if (_buffer == IntPtr.Zero) throw new InvalidOperationException("MapViewOfFile failed: " + Marshal.GetLastWin32Error());
            // 顯示：優先用 D3DImage (與 DirectShow 預覽同一條路，每格更新幾乎不佔 UI 執行緒)。
            // 不用 WriteableBitmap.WritePixels / InteropBitmap.Invalidate 每格更新：實測 UI 執行緒會卡在等 WPF 渲染執行緒，
            // 主控台輸入延遲累積到數秒、Background 優先權的 DispatcherTimer 也排不到。
            _surface = D3DImageSurface.TryCreate(W, H);
            ImageSource source;
            if (_surface != null)
            {
                source = _surface.Image;
            }
            else
            {
                _bitmap = (InteropBitmap)System.Windows.Interop.Imaging.CreateBitmapSourceFromMemorySection(_section, W, H, PixelFormats.Bgr32, Stride, 0);
                source = _bitmap;
            }
            _image = new Image { Source = source, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.Linear);


            // render 參數：SW_SIZE / SW_FORMAT / SW_STRIDE / SW_POINTER (每次 render 共用)
            IntPtr size = Marshal.AllocHGlobal(8);
            Marshal.WriteInt32(size, 0, W);
            Marshal.WriteInt32(size, 4, H);
            IntPtr format = MpvNative.Utf8("bgr0");
            IntPtr stride = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(stride, (IntPtr)Stride);
            // 預設 render() 會一直卡到該格的顯示時間才返回 (最長約一格)；預覽不需要精準時間點，畫好就交出，
            // 否則渲染執行緒長時間持有緩衝區鎖，UI 執行緒每格都在等 → 主控台輸入延遲累積到數秒 (實測)
            IntPtr noBlock = Marshal.AllocHGlobal(4);
            Marshal.WriteInt32(noBlock, 0);
            _paramAllocs = new[] { size, format, stride, noBlock };
            _renderParams = BuildParams(
                (MpvNative.RENDER_PARAM_BLOCK_FOR_TARGET_TIME, noBlock),
                (MpvNative.RENDER_PARAM_SW_SIZE, size),
                (MpvNative.RENDER_PARAM_SW_FORMAT, format),
                (MpvNative.RENDER_PARAM_SW_STRIDE, stride),
                (MpvNative.RENDER_PARAM_SW_POINTER, _buffer));

            _updateCallback = _ => _renderSignal.Set(); // mpv 執行緒呼叫：不可在這裡呼叫 mpv API
            MpvNative.RenderContextSetUpdateCallback!(_render, _updateCallback, IntPtr.Zero);

            _renderThread = new Thread(RenderLoop) { IsBackground = true, Name = "mpv-preview-render" };
            _eventThread = new Thread(EventLoop) { IsBackground = true, Name = "mpv-preview-events" };
            _renderThread.Start();
            _eventThread.Start();
        }

        /// <summary>建立預覽實例；libmpv 沒有 render API 或建立失敗時回傳 null (預覽改顯示黑畫面)。</summary>
        public static MpvPreview? TryCreate(Dispatcher dispatcher, string hwdec)
        {
            if (!MpvNative.IsRenderApiAvailable) return null;
            IntPtr ctx = MpvNative.Create!();
            if (ctx == IntPtr.Zero) return null;
            try
            {
                foreach (var (k, v) in new[]
                {
                    ("config", "no"), ("vo", "libmpv"), ("ao", "null"), ("aid", "no"), ("sid", "no"),
                    ("idle", "yes"), ("keep-open", "no"), ("osd-level", "0"), ("ytdl", "no"), ("load-scripts", "no"),
                    ("input-default-bindings", "no"), ("hwdec", hwdec),
                    ("vf", "lavfi=[scale=" + W + ":-2:flags=fast_bilinear]"), // 先縮小再渲染，CPU 少一半以上
                    ("sws-fast", "yes"), ("zimg-fast", "yes"),
                    ("demuxer-max-bytes", "32MiB"), ("demuxer-max-back-bytes", "8MiB"),
                })
                    MpvNative.SetOptionUtf8(ctx, k, v);

                int err = MpvNative.Initialize!(ctx);
                if (err < 0) throw new InvalidOperationException("mpv_initialize: " + MpvNative.Error(err));

                IntPtr apiType = MpvNative.Utf8("sw");
                IntPtr p = BuildParams((MpvNative.RENDER_PARAM_API_TYPE, apiType));
                err = MpvNative.RenderContextCreate!(out IntPtr render, ctx, p);
                Marshal.FreeHGlobal(p);
                Marshal.FreeCoTaskMem(apiType);
                if (err < 0) throw new InvalidOperationException("render_context_create(sw): " + MpvNative.Error(err));

                AppLogger.Log("[mpv-preview] Preview instance created.");
                return new MpvPreview(dispatcher, ctx, render);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("[mpv-preview] Failed to create preview instance", ex);
                MpvNative.TerminateDestroy!(ctx);
                return null;
            }
        }

        private static IntPtr BuildParams(params (int type, IntPtr data)[] ps)
        {
            int size = Marshal.SizeOf<MpvNative.mpv_render_param>();
            IntPtr mem = Marshal.AllocHGlobal(size * (ps.Length + 1));
            for (int i = 0; i <= ps.Length; i++)
            {
                var (t, d) = i < ps.Length ? ps[i] : (MpvNative.RENDER_PARAM_INVALID, IntPtr.Zero);
                Marshal.StructureToPtr(new MpvNative.mpv_render_param { type = t, data = d }, mem + i * size, false);
            }
            return mem;
        }

        // ---------- 播放控制 (由 MpvPlayer 同步呼叫，UI 執行緒) ----------

        public void Load(string target, bool paused)
        {
            if (_closing) return;
            _loaded = false;
            Set("pause", paused ? "yes" : "no");
            MpvNative.CommandUtf8(_ctx, "loadfile", target, "replace");
        }

        public void SetPaused(bool paused) => Set("pause", paused ? "yes" : "no");

        public void Seek(double seconds)
        {
            if (_closing || !_loaded) return;
            MpvNative.CommandUtf8(_ctx, "seek", seconds.ToString("0.###", CultureInfo.InvariantCulture), "absolute");
        }

        public void Stop()
        {
            if (_closing) return;
            _loaded = false;
            MpvNative.CommandUtf8(_ctx, "stop");
            Clear();
        }

        public void SetKeepAspect(bool keepAspect) => Set("keepaspect", keepAspect ? "yes" : "no");

        /// <summary>預覽目前播放位置 (秒)；尚未載入時為 null。</summary>
        public double? Position
        {
            get
            {
                if (_closing || !_loaded) return null;
                return double.TryParse(MpvNative.GetPropertyUtf8(_ctx, "time-pos"), NumberStyles.Float, CultureInfo.InvariantCulture, out double t) ? t : null;
            }
        }

        private void Set(string name, string value)
        {
            if (!_closing) MpvNative.SetPropertyUtf8(_ctx, name, value);
        }

        private void Clear()
        {
            lock (_bufferLock)
            {
                for (int i = 0; i < Stride * H; i += 8) Marshal.WriteInt64(_buffer, i, 0);
            }
            PostInvalidate();
        }

        // ---------- 渲染 ----------

        private void RenderLoop()
        {
            while (!_closing)
            {
                _renderSignal.WaitOne(500);
                if (_closing) break;
                ulong flags = MpvNative.RenderContextUpdate!(_render);
                if ((flags & MpvNative.RENDER_UPDATE_FRAME) == 0) continue;
                lock (_bufferLock)
                {
                    MpvNative.RenderContextRender!(_render, _renderParams);
                }
                PostInvalidate();
                if (MpvNative.Trace)
                {
                    _traceRenders++;
                    if (_traceSw.ElapsedMilliseconds >= 5000)
                    {
                        AppLogger.Log($"[mpv-trace] preview renders/s={_traceRenders / (_traceSw.ElapsedMilliseconds / 1000.0):F1}");
                        _traceRenders = 0; _traceSw.Restart();
                    }
                }
            }
        }

        private int _traceRenders;
        private readonly System.Diagnostics.Stopwatch _traceSw = System.Diagnostics.Stopwatch.StartNew();

        // 退回 InteropBitmap 時限制在 15fps：每次 Invalidate 都要等 WPF 渲染執行緒，30fps 會讓 UI 執行緒排滿
        private static readonly long FallbackMinIntervalTicks = System.Diagnostics.Stopwatch.Frequency / 15;
        private long _lastPostTicks;

        private void PostInvalidate()
        {
            // 比照 MediaKit 的 D3DImage 更新：Normal 優先權 BeginInvoke，上一格還沒處理就略過
            if (Interlocked.CompareExchange(ref _uiPending, 1, 0) != 0) return;
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (_surface == null && now - _lastPostTicks < FallbackMinIntervalTicks) { Interlocked.Exchange(ref _uiPending, 0); return; }
            _lastPostTicks = now;
            _dispatcher.BeginInvoke(() =>
            {
                Interlocked.Exchange(ref _uiPending, 0);
                if (_closing || _image.Visibility != Visibility.Visible) return;
                // UI 執行緒絕不等待：渲染執行緒正在畫下一格時就跳過這一格
                if (!Monitor.TryEnter(_bufferLock)) return;
                try
                {
                    if (_surface != null) _surface.Present(_buffer, Stride);
                    else _bitmap!.Invalidate();
                }
                finally
                {
                    Monitor.Exit(_bufferLock);
                }
            });
        }

        private const uint PAGE_READWRITE = 0x04, FILE_MAP_ALL_ACCESS = 0xF001F;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFileMapping(IntPtr hFile, IntPtr attributes, uint protect, uint sizeHigh, uint sizeLow, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr MapViewOfFile(IntPtr mapping, uint access, uint offsetHigh, uint offsetLow, UIntPtr bytes);

        [DllImport("kernel32.dll")]
        private static extern bool UnmapViewOfFile(IntPtr address);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        private void EventLoop()
        {
            while (!_closing)
            {
                IntPtr evPtr = MpvNative.WaitEvent!(_ctx, -1);
                if (_closing) break;
                int id = Marshal.ReadInt32(evPtr);
                if (id == MpvNative.EVENT_SHUTDOWN) break;
                if (id == MpvNative.EVENT_FILE_LOADED) _loaded = true;
                else if (id == MpvNative.EVENT_END_FILE) _loaded = false;
            }
        }

        public void Dispose()
        {
            if (_closing) return;
            _closing = true;
            _renderSignal.Set();
            _renderThread.Join(1000);
            MpvNative.RenderContextFree!(_render); // 必須在 terminate 之前釋放
            MpvNative.Wakeup!(_ctx);
            _eventThread.Join(1000);
            MpvNative.TerminateDestroy!(_ctx);
            Marshal.FreeHGlobal(_renderParams);
            Marshal.FreeHGlobal(_paramAllocs[0]);
            Marshal.FreeCoTaskMem(_paramAllocs[1]);
            Marshal.FreeHGlobal(_paramAllocs[2]);
            Marshal.FreeHGlobal(_paramAllocs[3]);
            _surface?.Dispose();
            UnmapViewOfFile(_buffer);
            CloseHandle(_section);
        }
    }
}
