using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace UltimateKtv
{
    /// <summary>
    /// 以 libmpv 實作的播放引擎 (原型：HwndHost 嵌入)。
    /// 語意盡量對齊 DirectShowPlayer：
    /// - 音軌編號 = mpv aid (從 1 開始)，與 MediaKit 相同
    /// - AudioChannel 0/1/2/3 = 立體聲/左/右/消人聲，以 lavfi pan 濾鏡實作
    /// - AudioPitch 以 rubberband 濾鏡實作 (需 libmpv 編入 librubberband)
    /// - Volume 0..1 視為 -100dB..0dB (同 DirectShow IBasicAudio)，再乘上 AudioAmplify%
    /// </summary>
    public sealed class MpvPlayer : UKtvPlayer
    {
        private const ulong ObsPause = 1, ObsTimePos = 2, ObsDuration = 3, ObsMousePos = 4, ObsIdle = 5;

        // 沒在播歌時 mpv 只在 OSD 變動時重畫，實測約每兩次 vsync 才畫一次 (開機跑馬燈只有 ~30fps)。
        // 閒置時持續播放一個 60fps 的黑色 lavfi 串流 (video-sync=display-desync)：電視上有跑馬燈捲動時 speed=1
        // (每次 vsync 都有新畫格 → 每次都重畫)，沒有時 speed=0.01 (幾乎不重畫；一直以 60fps 重畫會讓閒置 GPU 從 ~7% 升到 13~25%)。
        // 播放不中斷、只改速度：若改用暫停/恢復，恢復後頭 ~0.75 秒重畫節奏不穩，跑馬燈剛出現會跳格 (實測)。
        // 它的事件不轉給程式。
        private const string IdleSource = "av://lavfi:color=c=black:s=64x36:r=60";
        private long _idleEntryId = -1;
        private const int EVENT_CLIENT_MESSAGE = 16;
        private const string DoubleClickMessage = "ktv-dblclick";
        private const double MixerBaseBoost = 1.4125375; // AudioMixerDS BASE_AUDIO_BOOST_DB = 3 → 10^(3/20)

        private readonly MpvVideoHost _host;
        private readonly Dispatcher _dispatcher;
        private readonly DispatcherTimer _positionTimer;
        private readonly Thread _eventThread;
        private IntPtr _ctx;
        private volatile bool _closing;

        private Uri? _source;
        private volatile bool _fileLoaded;
        private volatile bool _paused;
        private long _positionTicks;
        private long _durationTicks;
        private bool _hasVideo;
        private List<int> _audioStreams = new();

        private double _volume = 1.0;
        private int _audioAmplify = 100;
        private double _audioPitch = 1.0;
        private int _audioChannel;
        private Stretch _stretch = Stretch.Uniform;
        private string _hwdec = "no";

        // 60fps 平滑 (display-resample)；掉格時自動降級
        private bool _smoothMotion;
        private int _smoothCheckTicks;
        private long _lastBadFrames = -1;

        // 主視窗預覽 (第二個 libmpv 實例)
        private readonly MpvPreview? _preview;
        private bool _previewEnabled = true;
        private string? _currentTarget;
        private int _previewSyncTicks;

        public MpvPlayer(AppSettings settings)
        {
            if (!MpvNative.EnsureLoaded())
                throw new InvalidOperationException("libmpv is not available.");

            _dispatcher = Dispatcher.CurrentDispatcher;
            _host = new MpvVideoHost();

            _ctx = MpvNative.Create!();
            if (_ctx == IntPtr.Zero) throw new InvalidOperationException("mpv_create failed.");

            // 這些選項必須在 mpv_initialize 之前設定
            SetOption("wid", _host.VideoHwnd.ToInt64().ToString(CultureInfo.InvariantCulture));
            SetOption("idle", "yes");
            SetOption("keep-open", "no");
            SetOption("input-default-bindings", "no");
            SetOption("input-vo-keyboard", "no");
            // 滑鼠/觸控會落在 mpv 的原生視窗上，WPF 收不到：讓 mpv 處理游標，再把移動/雙擊回報給程式
            SetOption("input-cursor", "yes");
            SetOption("cursor-autohide", "3000");    // 與主控台單螢幕模式的游標閒置時間一致
            SetOption("force-window", "yes");        // 沒在播歌時也建立影像輸出，開機跑馬燈等 OSD 才看得到
            SetOption("osc", "no");
            SetOption("osd-level", "0");
            SetOption("ytdl", "no");                 // YouTube 串流網址已由 YtDlpHelper 解析
            SetOption("sub-auto", "no");             // 同檔名 .lrc 會被 mpv 當字幕載入，與程式自己的歌詞重複
            SetOption("sid", "no");
            SetOption("load-scripts", "no");
            SetOption("config", "no");               // 不讀使用者的 mpv.conf，行為可預期
            SetOption("volume-max", "1000");
            SetOption("audio-client-name", "UltimateKtv");
            SetOption("background-color", "#000000");
            // config=no 時沒有預設快取目錄：指定著色器快取，避免每次啟動第一次顯示 OSD/影片時現場編譯造成掉格
            try
            {
                string cacheDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UltimateKtv", "mpv-shader-cache");
                System.IO.Directory.CreateDirectory(cacheDir);
                SetOption("gpu-shader-cache", "yes");
                SetOption("gpu-shader-cache-dir", cacheDir);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("[mpv] Failed to set up shader cache directory", ex);
            }
            if (!Environment.Is64BitProcess)
            {
                // 32 位元 libmpv 的 gpu-next (libplacebo + d3d11) 一開始播放就存取違規當掉；vo=gpu 正常
                SetOption("vo", "gpu");
            }
            _smoothMotion = settings.MpvSmoothMotion;
            if (_smoothMotion)
            {
                // display-resample：mpv 依螢幕更新率每次 vsync 都重畫，OSD 跑馬燈因此能以 60fps 捲動
                // (預設 video-sync=audio 只在影片換格時重畫 → 30fps 影片上跑馬燈只有 30 次/秒、每次跳兩步)。
                // 不需要插幀：實測不插幀一樣 60 次/秒、CPU 相同，且影片畫格維持原樣
                SetOption("video-sync", "display-resample");
                SetOption("interpolation", "no");
            }

            int err = MpvNative.Initialize!(_ctx);
            if (err < 0) throw new InvalidOperationException($"mpv_initialize failed: {MpvNative.Error(err)}");

            Observe(ObsPause, "pause", MpvNative.FORMAT_FLAG);
            Observe(ObsTimePos, "time-pos", MpvNative.FORMAT_DOUBLE);
            Observe(ObsDuration, "duration", MpvNative.FORMAT_DOUBLE);
            Observe(ObsMousePos, "mouse-pos", 0); // MPV_FORMAT_NONE：只要變更通知
            Observe(ObsIdle, "idle-active", MpvNative.FORMAT_FLAG);
            Command("keybind", "MBTN_LEFT_DBL", "script-message " + DoubleClickMessage);

            Osd = new MpvOsd(args => Command(args), name => GetString(name));
            Osd.ScrollingChanged += OnOsdScrollingChanged;
            WarmUpOsd();
            _preview = MpvPreview.TryCreate(_dispatcher, settings.EnableHWAccel ? "auto-copy-safe" : "no");

            Stretch = Stretch.Uniform;
            ApplySettings(settings);
            _audioAmplify = settings.AudioAmplify;
            ApplyAudioFilters();
            ApplyVolume();

            // DirectShow 版會持續送 MediaPositionChanged；這裡用 UI 計時器模擬，只在播放中觸發
            // Normal 優先權：播放進度與歌詞換行對時間敏感，不能被畫面更新延後 (Background 實測會被延到 200~450ms)
            _positionTimer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _positionTimer.Tick += (_, _) =>
            {
                if (_fileLoaded && !_paused)
                {
                    MediaPositionChanged?.Invoke(this, EventArgs.Empty);
                    CheckSmoothMotion();
                    SyncPreview();
                }
            };

            _eventThread = new Thread(EventLoop) { IsBackground = true, Name = "mpv-events" };
            _eventThread.Start();

            AppLogger.Log($"[mpv] Player created ({MpvNative.LoadedPath}), mpv version: {GetString("mpv-version")}");
        }

        public FrameworkElement View => _host;
        public MpvOsd? Osd { get; }
        public FrameworkElement? PreviewView => _preview?.View;

        public bool PreviewEnabled
        {
            get => _previewEnabled;
            set
            {
                if (_previewEnabled == value) return;
                _previewEnabled = value;
                if (_preview == null) return;
                if (!value) { _preview.Stop(); return; }
                // 重新開啟預覽：載入目前歌曲，校正會在下一秒把位置對齊
                if (_currentTarget != null && _fileLoaded) _preview.Load(_currentTarget, _paused);
            }
        }
        public string EngineName => "libmpv";
        public string VideoRendererName => GetString("current-vo") ?? "mpv";

        public Uri? Source
        {
            get => _source;
            set
            {
                _source = value;
                if (value == null)
                {
                    Command("stop");
                    _currentTarget = null;
                    _preview?.Stop();
                    return;
                }
                LoadCurrentSource();
            }
        }

        public bool IsPlaying => _fileLoaded && !_paused;
        public bool HasVideo => _fileLoaded && _hasVideo;

        public long MediaPosition
        {
            get => Interlocked.Read(ref _positionTicks);
            set
            {
                if (!_fileLoaded) return; // SafeStop 會在停止後設 0，無檔案時忽略
                Interlocked.Exchange(ref _positionTicks, value);
                double seconds = value / (double)TimeSpan.TicksPerSecond;
                Command("seek", seconds.ToString("0.###", CultureInfo.InvariantCulture), "absolute");
                if (_previewEnabled) _preview?.Seek(seconds);
            }
        }

        public long MediaDuration => Interlocked.Read(ref _durationTicks);

        public double Volume
        {
            get => _volume;
            set { _volume = Math.Clamp(value, 0.0, 1.0); ApplyVolume(); }
        }

        public int AudioAmplify
        {
            get => _audioAmplify;
            set
            {
                int amplify = Math.Max(0, value);
                if (amplify == _audioAmplify) return;
                _audioAmplify = amplify;
                ApplyAudioFilters();
            }
        }

        public double AudioPitch
        {
            get => _audioPitch;
            set
            {
                if (Math.Abs(_audioPitch - value) < 0.00001) return;
                _audioPitch = value;
                ApplyAudioFilters();
            }
        }

        public List<int> AudioStreams => _audioStreams;

        public int AudioTrack
        {
            get => int.TryParse(GetString("aid"), out int id) ? id : 0;
            set => SetProperty("aid", value.ToString(CultureInfo.InvariantCulture));
        }

        public int AudioChannel
        {
            get => _audioChannel;
            set
            {
                if (_audioChannel == value) return;
                _audioChannel = value;
                ApplyAudioFilters();
            }
        }

        public Stretch Stretch
        {
            get => _stretch;
            set
            {
                _stretch = value;
                SetProperty("keepaspect", value == Stretch.Fill ? "no" : "yes");
                SetProperty("panscan", value == Stretch.UniformToFill ? "1.0" : "0.0");
                _preview?.SetKeepAspect(value != Stretch.Fill);
            }
        }

        public void ApplySettings(AppSettings settings)
        {
            // HWAccelMode: 0=自動, 1=NVIDIA, 2=Intel, 3=DXVA2(copy-back), 4=DXVA2(native), 5=D3D11
            string hwdec = !settings.EnableHWAccel ? "no" : settings.HWAccelMode switch
            {
                1 => "nvdec",
                3 => "dxva2-copy",
                4 => "dxva2",
                5 => "d3d11va",
                _ => "auto-safe" // Intel QuickSync 在 mpv 走 d3d11va，交給 auto-safe
            };
            if (hwdec != _hwdec)
            {
                _hwdec = hwdec;
                SetProperty("hwdec", hwdec);
            }
            // 音訊輸出裝置：DirectShow 裝置名稱與 mpv (wasapi) 不相容，原型先用系統預設裝置
        }

        public void Play()
        {
            if (!_fileLoaded && _source != null)
            {
                LoadCurrentSource(); // Stop() 之後再 Play()：重新載入 (同 DirectShow 從頭播放)
                return;
            }
            SetProperty("pause", "no");
            if (_previewEnabled) _preview?.SetPaused(false);
        }

        public void Pause()
        {
            SetProperty("pause", "yes");
            if (_previewEnabled) _preview?.SetPaused(true);
        }

        public void Stop()
        {
            _fileLoaded = false;
            _positionTimer.Stop();
            Command("stop");
            _preview?.Stop();
        }

        public void Close()
        {
            if (_closing) return;
            Osd?.Dispose();
            _preview?.Dispose();
            _closing = true;
            _positionTimer.Stop();
            _fileLoaded = false;
            // mpv_terminate_destroy 不可與 mpv_wait_event 同時呼叫，交給事件執行緒收尾
            MpvNative.Wakeup!(_ctx);
            _eventThread.Join(TimeSpan.FromSeconds(3));
        }

        public event EventHandler? MediaOpened;
        public event EventHandler? MediaEnded;
        public event EventHandler? MediaClosed;
        public event EventHandler? MediaPositionChanged;
        public event EventHandler<PlayerFailedEventArgs>? MediaFailed;
        public event EventHandler? VideoMouseActivity;
        public event EventHandler? VideoDoubleClick;

        private void LoadCurrentSource()
        {
            if (_source == null) return;
            _fileLoaded = false;
            Interlocked.Exchange(ref _positionTicks, 0);
            Interlocked.Exchange(ref _durationTicks, 0);
            string target = _source.IsFile ? _source.LocalPath : _source.AbsoluteUri;
            _currentTarget = target;
            if (_previewEnabled) _preview?.Load(target, paused: true);
            // 先暫停載入：等 MediaOpened 處理完音軌/聲道/音量後才開始播放，避免開頭漏出錯誤聲道
            // (不用 loadfile 的 per-file 選項參數，因為 mpv 0.38 前後參數位置不同)
            SetProperty("pause", "yes");
            SetProperty("video-sync", _smoothMotion ? "display-resample" : "audio"); // 黑畫面期間改成 display-desync，載入歌曲時還原
            SetProperty("speed", "1"); // 黑畫面閒置時會調成極慢速
            // aid 在 mpv 是跨檔案保留的選項；每首歌重新自動選預設音軌 (同 DirectShow 重建 graph)
            SetProperty("aid", "auto");
            int err = Command("loadfile", target, "replace");
            if (err < 0)
            {
                RaiseFailed($"loadfile failed: {MpvNative.Error(err)}");
            }
        }

        // ---------- 事件處理 ----------

        private void EventLoop()
        {
            try
            {
                while (!_closing)
                {
                    IntPtr evPtr = MpvNative.WaitEvent!(_ctx, -1);
                    if (_closing) break;
                    var ev = Marshal.PtrToStructure<MpvNative.mpv_event>(evPtr);
                    if (ev.event_id == MpvNative.EVENT_SHUTDOWN) break;
                    HandleEvent(ev);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("[mpv] Event loop crashed", ex);
            }
            finally
            {
                IntPtr ctx = _ctx;
                _ctx = IntPtr.Zero;
                if (ctx != IntPtr.Zero) MpvNative.TerminateDestroy!(ctx);
            }
        }

        private void HandleEvent(MpvNative.mpv_event ev)
        {
            switch (ev.event_id)
            {
                case MpvNative.EVENT_PROPERTY_CHANGE:
                    if (ev.reply_userdata == ObsMousePos)
                    {
                        _dispatcher.InvokeAsync(() => VideoMouseActivity?.Invoke(this, EventArgs.Empty));
                        break;
                    }
                    var prop = Marshal.PtrToStructure<MpvNative.mpv_event_property>(ev.data);
                    if (prop.data == IntPtr.Zero) break; // 屬性無值 (例如沒有檔案)
                    if (ev.reply_userdata == ObsPause && prop.format == MpvNative.FORMAT_FLAG)
                        _paused = Marshal.ReadInt32(prop.data) != 0;
                    else if (ev.reply_userdata == ObsTimePos && prop.format == MpvNative.FORMAT_DOUBLE)
                        Interlocked.Exchange(ref _positionTicks, SecondsToTicks(ReadDouble(prop.data)));
                    else if (ev.reply_userdata == ObsDuration && prop.format == MpvNative.FORMAT_DOUBLE)
                        Interlocked.Exchange(ref _durationTicks, SecondsToTicks(ReadDouble(prop.data)));
                    else if (ev.reply_userdata == ObsIdle && prop.format == MpvNative.FORMAT_FLAG && Marshal.ReadInt32(prop.data) != 0)
                        ShowIdleSource(); // 開機或歌曲結束：載入黑畫面 (沒有捲動時暫停)
                    break;

                case MpvNative.EVENT_START_FILE:
                    // mpv_event_start_file { int64 playlist_entry_id; }
                    if (GetString("path") == IdleSource) _idleEntryId = Marshal.ReadInt64(ev.data);
                    break;

                case EVENT_CLIENT_MESSAGE:
                    // mpv_event_client_message { int num_args; const char **args; }
                    int argc = Marshal.ReadInt32(ev.data);
                    IntPtr argv = Marshal.ReadIntPtr(ev.data, IntPtr.Size);
                    if (argc >= 1 && MpvNative.PtrToUtf8(Marshal.ReadIntPtr(argv)) == DoubleClickMessage)
                        _dispatcher.InvokeAsync(() => VideoDoubleClick?.Invoke(this, EventArgs.Empty));
                    break;

                case MpvNative.EVENT_FILE_LOADED:
                    if (GetString("path") == IdleSource) break; // 閒置黑畫面不是歌曲
                    RefreshTracks();
                    _dispatcher.InvokeAsync(() =>
                    {
                        if (_closing) return;
                        _fileLoaded = true;
                        MediaOpened?.Invoke(this, EventArgs.Empty);
                        SetProperty("pause", "no"); // 設定都套用後才開始播放
                        if (_previewEnabled) _preview?.SetPaused(false);
                        _positionTimer.Start();
                        AppLogger.Log($"[mpv] opened: vo={GetString("current-vo")}, ao={GetString("current-ao")}, " +
                                      $"aid={GetString("aid")}, speed={GetString("speed")}, volume={GetString("volume")}, mute={GetString("mute")}, af={GetString("af")}");
                    });
                    break;

                case MpvNative.EVENT_END_FILE:
                    var end = Marshal.PtrToStructure<MpvNative.mpv_event_end_file>(ev.data);
                    if (end.playlist_entry_id == _idleEntryId) break; // 閒置黑畫面被換掉，不影響歌曲狀態
                    _dispatcher.InvokeAsync(() =>
                    {
                        if (_closing) return;
                        _fileLoaded = false;
                        _positionTimer.Stop();
                        switch (end.reason)
                        {
                            case MpvNative.END_FILE_REASON_EOF:
                                MediaEnded?.Invoke(this, EventArgs.Empty);
                                break;
                            case MpvNative.END_FILE_REASON_ERROR:
                                RaiseFailed($"mpv playback error: {MpvNative.Error(end.error)}");
                                break;
                            default: // STOP / QUIT / REDIRECT
                                MediaClosed?.Invoke(this, EventArgs.Empty);
                                break;
                        }
                    });
                    break;
            }
        }

        private void OnOsdScrollingChanged(bool scrolling)
        {
            if (_closing) return;
            // 只在播黑畫面時切換重畫方式；播歌時不動 (歌曲有自己的 video-sync 設定)
            if (GetString("path") != IdleSource) return;
            SetProperty("speed", IdleSpeed(scrolling));
        }

        // 沒有捲動時以極慢速播放 (每秒不到一格新畫面 → 幾乎不重畫)；捲動時恢復 60fps。播放不中斷，只改速度
        private static string IdleSpeed(bool scrolling) => scrolling ? "1" : "0.01";

        private void ShowIdleSource()
        {
            if (_closing) return;
            SetProperty("pause", "no");
            // 黑畫面沒有聲音，display-resample 無法對齊螢幕節奏；display-desync = 每次 vsync 都畫、不管影片時間
            SetProperty("video-sync", "display-desync");
            SetProperty("speed", IdleSpeed(Osd?.IsScrolling == true));
            int err = Command("loadfile", IdleSource, "replace");
            if (err < 0) AppLogger.Log("[mpv] idle source unavailable; OSD will redraw at a lower rate while idle.");
        }

        private void RaiseFailed(string message)
        {
            AppLogger.Log($"[mpv] {message} source={_source}");
            MediaFailed?.Invoke(this, new PlayerFailedEventArgs(message, new Exception(message)));
        }

        private void RefreshTracks()
        {
            var audio = new List<int>();
            bool hasVideo = false;
            int count = int.TryParse(GetString("track-list/count"), out int c) ? c : 0;
            for (int i = 0; i < count; i++)
            {
                string? type = GetString($"track-list/{i}/type");
                if (type == "audio")
                {
                    if (int.TryParse(GetString($"track-list/{i}/id"), out int id)) audio.Add(id);
                }
                else if (type == "video" && GetString($"track-list/{i}/albumart") != "yes")
                {
                    hasVideo = true;
                }
            }
            _audioStreams = audio;
            _hasVideo = hasVideo;
        }

        // ---------- 音訊 ----------

        // 對齊 CrazyKTV_MediaKit 的音訊處理 (MediaUriPlayer graph：解碼 → SoundTouch → AudioMixerDS → Renderer)：
        // AudioMixerDS.cpp Transform() 逐樣本處理：
        //   1. 聲道對應 (SetChannelOrder)：左=L/L、右=R/R、消人聲=兩聲道都輸出 L−R
        //   2. 增益 = AudioAmplify% × 10^(BASE_AUDIO_BOOST_DB(3)/20)
        //   3. 峰值包絡限制器：門檻 30000/32767，attack 係數 0.999 (≈21ms)、release 0.99995 (≈417ms)
        //   4. 軟削峰：超過 20000 以 tanh 壓到 32767
        // 之後 IBasicAudio 才套用 Volume (線性振幅)，所以峰值天花板會隨音量移動。
        // mpv 依同樣順序：af = rubberband → pan → volume → acompressor (同款包絡，近似 3) → aeval (與 4 相同公式)，最後是 mpv volume 屬性。
        // 以 loopback 錄同一首歌 + ffmpeg 比對：響度差 < 0.3 dB。
        private const string MixerSoftClip =
            "if(gt(val(ch)\\,0.6103516)\\,0.6103516+0.3896179*tanh((val(ch)-0.6103516)/0.3896179)\\," +
            "if(lt(val(ch)\\,-0.6103516)\\,-0.6103516-0.3896179*tanh((-0.6103516-val(ch))/0.3896179)\\,val(ch)))";

        /// <summary>
        /// 預覽是獨立的 mpv 實例，各自計時；每秒比對一次，差超過 0.3 秒就讓預覽 seek 對齊。
        /// </summary>
        // 診斷：每 100ms 往 UI 丟一個 Input 優先權工作，量等多久才被執行 (= 點擊的反應延遲)
        internal static void StartUiLatencyProbe(Dispatcher dispatcher)
        {
            var t = new Thread(() =>
            {
                double max = 0, sum = 0; int n = 0; var sw = System.Diagnostics.Stopwatch.StartNew();
                while (true)
                {
                    long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    var op = dispatcher.BeginInvoke(DispatcherPriority.Input, () => { });
                    op.Wait(TimeSpan.FromSeconds(5));
                    double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    max = Math.Max(max, ms); sum += ms; n++;
                    if (sw.ElapsedMilliseconds >= 5000)
                    {
                        AppLogger.Log($"[mpv-trace] UI input latency 5s: avg={sum / n:F0}ms max={max:F0}ms");
                        max = 0; sum = 0; n = 0; sw.Restart();
                    }
                    Thread.Sleep(100);
                }
            }) { IsBackground = true, Name = "ui-latency-probe" };
            t.Start();
        }

        /// <summary>
        /// 啟動時先放一張透明小圖讓 mpv 編譯 OSD 著色器；否則第一次出現跑馬燈時會連續掉好幾格 (實測)。
        /// </summary>
        private void WarmUpOsd()
        {
            var warm = Osd?.ShowStatic(OsdBitmap.CreateTransparent(16, 16), 0, 0);
            if (warm == null) return;
            var timer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher) { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (_, _) => { timer.Stop(); Osd?.Remove(warm); };
            timer.Start();
        }

        private void SyncPreview()
        {
            if (_preview == null || !_previewEnabled || ++_previewSyncTicks < 10) return;
            _previewSyncTicks = 0;
            double? previewPos = _preview.Position;
            double mainPos = Interlocked.Read(ref _positionTicks) / (double)TimeSpan.TicksPerSecond;
            if (MpvNative.Trace) AppLogger.Log($"[mpv-trace] vo: drop={GetString("frame-drop-count")} delayed={GetString("vo-delayed-frame-count")} mistimed={GetString("mistimed-frame-count")} display-fps={GetString("display-fps")} est-display-fps={GetString("estimated-display-fps")} vsync-jitter={GetString("vsync-jitter")} sync={GetString("video-sync")}");
            if (MpvNative.Trace) AppLogger.Log($"[mpv-trace] sync main={mainPos:F2} preview={previewPos?.ToString("F2") ?? "-"} diff={(previewPos - mainPos)?.ToString("F2") ?? "-"}");
            if (previewPos == null) return;
            if (Math.Abs(previewPos.Value - mainPos) > 0.3)
                _preview.Seek(mainPos + 0.05); // seek 本身要幾十毫秒，稍微超前
        }

        /// <summary>
        /// display-resample 會讓 mpv 每次螢幕更新都重畫；顯示卡較弱的機台跟不上時，影片優先：自動改回依影片換格。
        /// </summary>
        private void CheckSmoothMotion()
        {
            if (!_smoothMotion || ++_smoothCheckTicks < 50) return; // 每 5 秒檢查一次
            _smoothCheckTicks = 0;

            long bad = 0;
            foreach (var name in new[] { "frame-drop-count", "vo-delayed-frame-count", "mistimed-frame-count" })
                if (long.TryParse(GetString(name), out long n)) bad += n;

            if (_lastBadFrames < 0 || bad < _lastBadFrames) { _lastBadFrames = bad; return; } // 新歌計數會歸零
            long delta = bad - _lastBadFrames;
            _lastBadFrames = bad;
            if (delta <= 15) return;

            _smoothMotion = false;
            SetProperty("video-sync", "audio");
            SetProperty("interpolation", "no");
            AppLogger.Log($"[mpv] {delta} dropped/delayed frames in 5s with display-resample; smooth motion disabled until restart (vo={GetString("current-vo")}, display-fps={GetString("display-fps")}).");
        }

        private void ApplyVolume()
        {
            // mpv 軟體音量是三次方曲線：gain = (volume/100)^3
            double mpvVolume = _volume <= 0 ? 0 : Math.Min(100, 100.0 * Math.Cbrt(_volume));
            SetProperty("volume", mpvVolume.ToString("0.##", CultureInfo.InvariantCulture));
        }

        private void ApplyAudioFilters()
        {
            var filters = new List<string>();
            if (Math.Abs(_audioPitch - 1.0) > 0.0001)
                filters.Add("rubberband=pitch-scale=" + _audioPitch.ToString("0.####", CultureInfo.InvariantCulture));
            switch (_audioChannel)
            {
                case 1: filters.Add("lavfi=[pan=stereo|c0=c0|c1=c0]"); break;          // 左聲道
                case 2: filters.Add("lavfi=[pan=stereo|c0=c1|c1=c1]"); break;          // 右聲道
                case 3: filters.Add("lavfi=[pan=stereo|c0=c0-c1|c1=c0-c1]"); break;    // 消人聲 (兩聲道都是 L−R)
            }

            double gain = _audioAmplify / 100.0 * MixerBaseBoost;
            filters.Add("lavfi=[volume=" + gain.ToString("0.####", CultureInfo.InvariantCulture) + ":precision=float]");
            // acompressor 的包絡追蹤與 AudioMixerDS 相同：env += (|x|−env)×coeff，coeff = 1/(ms×取樣率/4000)
            // → attack 0.001/樣本 = 83.3ms、release 0.00005/樣本 = 1666.7ms (48kHz)；ratio 20 為上限，接近 DS 的硬限制
            filters.Add("lavfi=[acompressor=threshold=0.9155:ratio=20:attack=83.33:release=1666.67:knee=1:detection=peak:link=maximum:makeup=1]");
            filters.Add("lavfi=[aeval=exprs=" + MixerSoftClip + "|" + MixerSoftClip + ":c=same]");

            // 注意：濾鏡參數錯誤時 mpv 仍回傳成功，只會在 log 出現 "Audio filter initialized failed"
            SetProperty("af", string.Join(",", filters));
        }

        // ---------- libmpv 呼叫包裝 ----------

        private static long SecondsToTicks(double seconds) =>
            double.IsFinite(seconds) && seconds > 0 ? (long)(seconds * TimeSpan.TicksPerSecond) : 0;

        private static double ReadDouble(IntPtr p) => BitConverter.Int64BitsToDouble(Marshal.ReadInt64(p));

        private void SetOption(string name, string value)
        {
            int err = MpvNative.SetOptionUtf8(_ctx, name, value);
            if (err < 0) AppLogger.Log($"[mpv] set option {name}={value} failed: {MpvNative.Error(err)}");
        }

        private int SetProperty(string name, string value)
        {
            if (_ctx == IntPtr.Zero || _closing) return -1;
            int err = MpvNative.SetPropertyUtf8(_ctx, name, value);
            if (err < 0) AppLogger.Log($"[mpv] set {name}={value} failed: {MpvNative.Error(err)}");
            return err;
        }

        private string? GetString(string name)
        {
            if (_ctx == IntPtr.Zero || _closing) return null;
            return MpvNative.GetPropertyUtf8(_ctx, name);
        }

        private void Observe(ulong id, string name, int format)
        {
            IntPtr n = MpvNative.Utf8(name);
            try { MpvNative.ObserveProperty!(_ctx, id, n, format); }
            finally { Marshal.FreeCoTaskMem(n); }
        }

        private int Command(params string[] args)
        {
            if (_ctx == IntPtr.Zero || _closing) return -1;
            int err = MpvNative.CommandUtf8(_ctx, args);
            if (err < 0) AppLogger.Log($"[mpv] command {string.Join(" ", args)} failed: {MpvNative.Error(err)}");
            return err;
        }
    }
}
