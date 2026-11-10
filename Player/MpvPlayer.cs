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
    /// - 音軌編號 = mpv aid (從 1 開始)，與 MediaKit 相同。多音軌歌曲比照 MediaKit 把所有音軌一起解碼、只聽選取的那條 (見 OnPreloaded)
    /// - AudioChannel 0/1/2/3 = 立體聲/左/右/消人聲，以 lavfi pan 濾鏡實作 (消人聲另加 lowpass/highpass 分頻)
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

        // 多音軌：所有音軌一起解碼 (MediaKit 作法，見 OnPreloaded)。null = 單音軌，照一般方式用 aid 選音軌
        private const ulong HookPreloaded = 100;
        private const int MergedSampleRate = 48000;
        private volatile int[]? _mergedTracks;
        // 程式要求播放歌曲中 (Source 已設定、還沒停止或播完)。只在 UI 執行緒讀寫；
        // mpv 進入閒置時據此決定要不要載入黑畫面 (見 OnIdle)
        private bool _songRequested;
        // 歌曲播完/開檔失敗時只清掉「最新一次載入」的要求：舊歌的結束事件晚到 UI 時不能清掉新歌的
        private int _loadGeneration;                 // UI 執行緒每次載入歌曲 +1
        private long _songEntryId = -1;              // 最近開始的歌曲的 mpv playlist entry (事件執行緒寫)
        private volatile int _songEntryGeneration = -1;
        private double _mergedDuration;
        private volatile int _selectedTrack;
        private string? _currentGraph;
        // 多音軌時使用者 seek 的步驟 (見 StartMergedSeek)；0 = 沒有在 seek
        private int _seekStep;
        private double _seekTarget;
        private bool _resumeAfterSeek;
        private readonly DispatcherTimer _seekTimeout;

        private double _volume = 1.0;
        private int _audioAmplify = 100;
        private double _audioPitch = 1.0;
        private int _audioChannel;
        private Stretch _stretch = Stretch.Uniform;
        private string _hwdec = "no";
        private string? _audioDevice;

        // 60fps 平滑 (display-resample)；持續掉格時當首歌降級，下一首歌重新嘗試
        private bool _smoothMotion;
        private int _smoothCheckTicks;
        private long _lastBadFrames = -1;
        private long _lastDelayedFrames = -1;
        private int _smoothBadWindows; // 連續超標的檢查次數

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
            // 精確 seek 時 demuxer 先退 1 秒再解到目標：lavf 對 MPEG-PS 的 seek 會落在目標之後 (實測晚 0.5 秒，等於跳過半秒歌)
            SetOption("hr-seek-demuxer-offset", "1");
            // 診斷用：設環境變數 KTV_MPV_LOG=<檔案路徑> 時寫出 mpv 自己的詳細 log (換音軌、濾鏡、demuxer 等問題只看得到這裡)
            string? mpvLog = Environment.GetEnvironmentVariable("KTV_MPV_LOG");
            if (!string.IsNullOrWhiteSpace(mpvLog))
            {
                SetOption("log-file", mpvLog);
                SetOption("msg-level", "all=v");
            }
            // 診斷用：KTV_MPV_AO_PCM=<wav 路徑> 時聲音不送音效卡，改寫成 wav 檔 (錄下實際輸出比對音軌/聲道)。
            // 注意 ao=pcm 的緩衝在 seek 時已寫入檔案，量 seek 前後的斷音/跳秒要看 mpv log，不要看這個錄音
            string? pcmOut = Environment.GetEnvironmentVariable("KTV_MPV_AO_PCM");
            if (!string.IsNullOrWhiteSpace(pcmOut))
            {
                SetOption("ao", "pcm");
                SetOption("ao-pcm-file", pcmOut);
                SetOption("audio-channels", "stereo");
            }
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
            // display-resample：mpv 依螢幕更新率每次 vsync 都重畫，OSD 跑馬燈因此能以 60fps 捲動
            // (預設 video-sync=audio 只在影片換格時重畫 → 30fps 影片上跑馬燈只有 30 次/秒、每次跳兩步)。
            // 不需要插幀：實測不插幀一樣 60 次/秒、CPU 相同，且影片畫格維持原樣。
            // 固定開啟 (原本的設定選項已拿掉)；GPU 跟不上時 CheckSmoothMotion 會自動改回 audio
            _smoothMotion = true;
            SetOption("video-sync", "display-resample");
            SetOption("interpolation", "no");

            int err = MpvNative.Initialize!(_ctx);
            if (err < 0) throw new InvalidOperationException($"mpv_initialize failed: {MpvNative.Error(err)}");

            Observe(ObsPause, "pause", MpvNative.FORMAT_FLAG);
            Observe(ObsTimePos, "time-pos", MpvNative.FORMAT_DOUBLE);
            Observe(ObsDuration, "duration", MpvNative.FORMAT_DOUBLE);
            Observe(ObsMousePos, "mouse-pos", 0); // MPV_FORMAT_NONE：只要變更通知
            Observe(ObsIdle, "idle-active", MpvNative.FORMAT_FLAG);
            // 多音軌歌曲要在建立解碼器之前設好 lavfi-complex (見 OnPreloaded)
            IntPtr hookName = MpvNative.Utf8("on_preloaded");
            try { MpvNative.HookAdd!(_ctx, HookPreloaded, hookName, 0); }
            finally { Marshal.FreeCoTaskMem(hookName); }
            Command("keybind", "MBTN_LEFT_DBL", "script-message " + DoubleClickMessage);

            Osd = new MpvOsd(args => Command(args), name => GetString(name), () => _host.VideoHwnd);
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

            // 多音軌 seek 的每一步都在等 mpv 的 PLAYBACK_RESTART；等不到時 (例如 mpv 沒有重建) 不要一直停在暫停
            _seekTimeout = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher) { Interval = TimeSpan.FromSeconds(2) };
            _seekTimeout.Tick += (_, _) =>
            {
                AppLogger.Log($"[mpv] multi-track seek step {_seekStep} timed out (target={_seekTarget:F2}); resuming.");
                FinishMergedSeek();
            };

            _eventThread = new Thread(EventLoop) { IsBackground = true, Name = "mpv-events" };
            _eventThread.Start();

            AppLogger.Log($"[mpv] Player created ({MpvNative.LoadedPath}), mpv version: {GetString("mpv-version")}");
        }

        public FrameworkElement View => _host;
        private MpvOsd? Osd { get; }

        public ITvOverlay CreateTvOverlay(FrameworkElement videoRoot) =>
            Osd != null ? new MpvTvOverlay(Osd, videoRoot) : WpfTvOverlay.Instance;
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
                    _songRequested = false;
                    Command("stop");
                    _currentTarget = null;
                    _preview?.Stop();
                    return;
                }
                LoadCurrentSource();
            }
        }

        // 多音軌 seek 期間內部會暫停；對外回報 seek 完成後的狀態，播放/暫停鍵與升降 Key 不會因此閃一下
        public bool IsPlaying => _fileLoaded && (_seekStep != 0 ? _resumeAfterSeek : !_paused);
        public bool HasVideo => _fileLoaded && _hasVideo;

        public long MediaPosition
        {
            get => Interlocked.Read(ref _positionTicks);
            set
            {
                if (!_fileLoaded) return; // SafeStop 會在停止後設 0，無檔案時忽略
                Interlocked.Exchange(ref _positionTicks, value);
                double seconds = value / (double)TimeSpan.TicksPerSecond;
                if (_mergedTracks != null) StartMergedSeek(seconds);
                else Command("seek", seconds.ToString("0.###", CultureInfo.InvariantCulture), "absolute");
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
            get => _mergedTracks != null ? _selectedTrack : int.TryParse(GetString("aid"), out int id) ? id : 0;
            set
            {
                var merged = _mergedTracks;
                if (merged == null)
                {
                    SetProperty("aid", value.ToString(CultureInfo.InvariantCulture));
                    return;
                }
                // 所有音軌都在解碼：只改 af 最前面選聲道的 pan，不必 seek (同 MediaKit 把其他音軌的音量設為 0)
                if (Array.IndexOf(merged, value) < 0 || value == _selectedTrack) return;
                _selectedTrack = value;
                ApplyAudioFilters();
            }
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
            ApplyAudioDevice(settings.AudioRendererDevice);
        }

        private void ApplyAudioDevice(string? deviceName)
        {
            string targetDevice = MatchMpvAudioDevice(deviceName);
            if (targetDevice != _audioDevice)
            {
                _audioDevice = targetDevice;
                SetProperty("audio-device", targetDevice);
                AppLogger.Log($"[mpv] audio-device set to: {targetDevice} (setting: '{deviceName}')");
            }
        }

        private string MatchMpvAudioDevice(string? targetDevice)
        {
            if (string.IsNullOrWhiteSpace(targetDevice) ||
                targetDevice.Equals("Default DirectSound Device", StringComparison.OrdinalIgnoreCase) ||
                targetDevice.Equals("Default WaveOut Device", StringComparison.OrdinalIgnoreCase))
            {
                return "auto";
            }

            string cleanTarget = targetDevice.Trim();
            if (cleanTarget.StartsWith("DirectSound: ", StringComparison.OrdinalIgnoreCase))
            {
                cleanTarget = cleanTarget.Substring("DirectSound: ".Length).Trim();
            }
            else if (cleanTarget.StartsWith("WaveOut: ", StringComparison.OrdinalIgnoreCase))
            {
                cleanTarget = cleanTarget.Substring("WaveOut: ".Length).Trim();
            }

            int count = int.TryParse(GetString("audio-device-list/count"), out int c) ? c : 0;
            if (count <= 0) return "auto";

            // 1. 完全比對 (比對 description 或 name)
            for (int i = 0; i < count; i++)
            {
                string? name = GetString($"audio-device-list/{i}/name");
                string? desc = GetString($"audio-device-list/{i}/description");
                if (string.IsNullOrWhiteSpace(name)) continue;

                if (string.Equals(desc, cleanTarget, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, cleanTarget, StringComparison.OrdinalIgnoreCase))
                {
                    return name;
                }
            }

            // 2. 部分包含比對 (忽略大小寫)
            for (int i = 0; i < count; i++)
            {
                string? name = GetString($"audio-device-list/{i}/name");
                string? desc = GetString($"audio-device-list/{i}/description");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(desc)) continue;

                if (desc.Contains(cleanTarget, StringComparison.OrdinalIgnoreCase) ||
                    cleanTarget.Contains(desc, StringComparison.OrdinalIgnoreCase))
                {
                    return name;
                }
            }

            AppLogger.Log($"[mpv] audio device '{targetDevice}' not found in mpv device list; using auto.");
            return "auto";
        }

        public void Play()
        {
            if (!_fileLoaded && _source != null)
            {
                LoadCurrentSource(); // Stop() 之後再 Play()：重新載入 (同 DirectShow 從頭播放)
                return;
            }
            if (_seekStep != 0)
            {
                // 多音軌 seek 中：完成後才恢復播放；display-fps-override 由 FinishMergedSeek 改回 0，
                // 這裡先改回會讓 seek 那段暫停的跑馬燈又掉到 ~30 次/秒
                _resumeAfterSeek = true;
            }
            else
            {
                SetProperty("display-fps-override", "0");
                SetProperty("pause", "no");
            }
            if (_previewEnabled) _preview?.SetPaused(false);
        }

        public void Pause()
        {
            _resumeAfterSeek = false;
            SetProperty("pause", "yes");
            // 暫停時 mpv 只在 OSD 變動時重畫，且限制「兩次重畫至少相隔一個螢幕週期」(vo.c: manual redraws at most at display fps)。
            // 它估的週期 (例如 59.82fps = 16.72ms) 只要比實際長一點，每格送來的跑馬燈更新就被判定太早、延到下一格 → 只剩 ~40fps (實測)。
            // 暫停期間把假定的螢幕更新率調高讓這個限制失效 (更新本來就由 MpvOsd 對齊 vblank 每格一次)；恢復播放/換歌/閒置時改回 0 (自動)
            SetProperty("display-fps-override", "1000");
            if (_previewEnabled) _preview?.SetPaused(true);
        }

        public void Stop()
        {
            CancelMergedSeek();
            _songRequested = false;
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
            _songRequested = true;
            Interlocked.Increment(ref _loadGeneration);
            CancelMergedSeek();
            _mergedTracks = null; // 新檔案的音軌組成由 on_preloaded 決定；之前套用 af 不可沿用上一首的選聲道
            _fileLoaded = false;
            Interlocked.Exchange(ref _positionTicks, 0);
            Interlocked.Exchange(ref _durationTicks, 0);
            string target = _source.IsFile ? _source.LocalPath : _source.AbsoluteUri;
            _currentTarget = target;
            if (_previewEnabled) _preview?.Load(target, paused: true);
            SetProperty("display-fps-override", "0"); // 暫停中換歌：取消 Pause() 的設定
            // 先暫停載入：等 MediaOpened 處理完音軌/聲道/音量後才開始播放，避免開頭漏出錯誤聲道
            // (不用 loadfile 的 per-file 選項參數，因為 mpv 0.38 前後參數位置不同)
            SetProperty("pause", "yes");
            // 每首歌重新嘗試平滑捲動，掉格計數也從頭算 (前一首被降級多半是當時的負載尖峰，例如 YouTube 下載)
            _smoothMotion = true;
            _smoothCheckTicks = 0;
            _lastBadFrames = -1;
            _lastDelayedFrames = -1;
            _smoothBadWindows = 0;
            SetProperty("video-sync", "display-resample"); // 黑畫面期間改成 display-desync，載入歌曲時還原
            SetProperty("speed", "1"); // 黑畫面閒置時會調成極慢速
            // aid 在 mpv 是跨檔案保留的選項；每首歌重新自動選預設音軌 (同 DirectShow 重建 graph)
            SetProperty("aid", "auto");
            int err = Command("loadfile", target, "replace");
            if (err < 0)
            {
                _songRequested = false;
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
                        _dispatcher.InvokeAsync(OnIdle); // 開機或歌曲結束：載入黑畫面 (沒有捲動時暫停)
                    break;

                case MpvNative.EVENT_HOOK:
                    var hook = Marshal.PtrToStructure<MpvNative.mpv_event_hook>(ev.data);
                    try
                    {
                        if (ev.reply_userdata == HookPreloaded) OnPreloaded();
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogError("[mpv] on_preloaded hook failed", ex);
                    }
                    finally
                    {
                        MpvNative.HookContinue!(_ctx, hook.id); // 一定要放行，否則 mpv 會一直等這個 hook
                    }
                    break;

                case MpvNative.EVENT_PLAYBACK_RESTART:
                    if (Volatile.Read(ref _seekStep) != 0) _dispatcher.InvokeAsync(OnMergedSeekRestart);
                    break;

                case MpvNative.EVENT_START_FILE:
                    // mpv_event_start_file { int64 playlist_entry_id; }
                    long entryId = Marshal.ReadInt64(ev.data);
                    if (GetString("path") == IdleSource)
                    {
                        _idleEntryId = entryId;
                    }
                    else
                    {
                        _songEntryGeneration = Volatile.Read(ref _loadGeneration);
                        Interlocked.Exchange(ref _songEntryId, entryId);
                    }
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
                        // 同 MediaKit (MediaElementBase 開檔時 AudioChannel = 0)：每首新歌先回到立體聲，
                        // 單音軌歌曲再由 MediaOpened 的處理者改成左/右聲道。否則多音軌歌曲會沿用上一首的左/右聲道
                        // 多音軌歌曲也要在這裡套用一次 af (選取音軌的 pan)，所以不經過 AudioChannel 的「沒變就不套用」
                        _audioChannel = 0;
                        ApplyAudioFilters();
                        MediaOpened?.Invoke(this, EventArgs.Empty);
                        SetProperty("pause", "no"); // 設定都套用後才開始播放
                        if (_previewEnabled) _preview?.SetPaused(false);
                        _positionTimer.Start();
                        AppLogger.Log($"[mpv] opened: vo={GetString("current-vo")}, ao={GetString("current-ao")}, " +
                                      $"aid={GetString("aid")}, merged={(_mergedTracks == null ? "no" : $"{_mergedTracks.Length} tracks, play {_selectedTrack}")}, speed={GetString("speed")}, volume={GetString("volume")}, mute={GetString("mute")}, af={GetString("af")}");
                    });
                    break;

                case MpvNative.EVENT_END_FILE:
                    var end = Marshal.PtrToStructure<MpvNative.mpv_event_end_file>(ev.data);
                    if (end.playlist_entry_id == _idleEntryId) break; // 閒置黑畫面被換掉，不影響歌曲狀態
                    _dispatcher.InvokeAsync(() =>
                    {
                        if (_closing) return;
                        CancelMergedSeek();
                        _fileLoaded = false;
                        _positionTimer.Stop();
                        // 播完或失敗：沒有要播的歌了 (之後 mpv 閒置時才載入黑畫面)。STOP/REDIRECT 是被停止或被新歌取代，不清
                        if ((end.reason == MpvNative.END_FILE_REASON_EOF || end.reason == MpvNative.END_FILE_REASON_ERROR) &&
                            end.playlist_entry_id == Interlocked.Read(ref _songEntryId) &&
                            _songEntryGeneration == Volatile.Read(ref _loadGeneration))
                        {
                            _songRequested = false;
                        }
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
            // 0.01 倍速時 mpv 已排好的那一格要 ~1.67 秒後才換下一格；只改速度的話要等它播完才回到每次 vsync 重畫，
            // 這段期間 (0.3~1.5 秒，看切換落在哪) OSD 重畫只有 ~30 次/秒，跑馬燈剛出現會頓。原地 seek 讓 mpv 立刻以新速度重排 (實測)
            if (scrolling) Command("seek", "0", "relative+exact");
        }

        // 沒有捲動時以極慢速播放 (每秒不到一格新畫面 → 幾乎不重畫)；捲動時恢復 60fps。播放不中斷，只改速度
        private static string IdleSpeed(bool scrolling) => scrolling ? "1" : "0.01";

        /// <summary>
        /// mpv 進入閒置 (沒有檔案)。在 UI 執行緒決定要不要載入黑畫面：
        /// 停止後立刻載入同一首/下一首 (重播) 時，mpv 仍會先回報閒置；若在 mpv 事件執行緒直接載入黑畫面，
        /// 它和 UI 載入歌曲的 loadfile 只差 1ms，黑畫面較晚送出就會取代剛載入的歌 → 重播後完全沒有動作。
        /// 載入歌曲一律在 UI 執行緒，所以在這裡檢查「是否已要求播放歌曲」就能確定先後。
        /// </summary>
        private void OnIdle()
        {
            if (_closing || _songRequested) return;
            ShowIdleSource();
        }

        private void ShowIdleSource()
        {
            if (_closing) return;
            SetProperty("display-fps-override", "0"); // 暫停中停止播放：取消 Pause() 的設定
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
        //   1. 聲道對應 (SetChannelOrder)：左=L/L、右=R/R、消人聲=兩聲道都輸出分頻 L−R (見 VocalRemovalFilter)
        //   2. 增益 = AudioAmplify% × 10^(BASE_AUDIO_BOOST_DB(3)/20)
        //   3. 峰值包絡限制器：門檻 30000/32767，attack 係數 0.999 (≈21ms)、release 0.99995 (≈417ms)
        //   4. 軟削峰：超過 20000 以 tanh 壓到 32767
        // 之後 IBasicAudio 才套用 Volume (線性振幅)，所以峰值天花板會隨音量移動。
        // mpv 依同樣順序：af = rubberband → pan → volume → acompressor (同款包絡，近似 3) → aeval (與 4 相同公式)，最後是 mpv volume 屬性。
        // 以 loopback 錄同一首歌 + ffmpeg 比對：響度差 < 0.3 dB。
        private const string MixerSoftClip =
            "if(gt(val(ch)\\,0.6103516)\\,0.6103516+0.3896179*tanh((val(ch)-0.6103516)/0.3896179)\\," +
            "if(lt(val(ch)\\,-0.6103516)\\,-0.6103516-0.3896179*tanh((-0.6103516-val(ch))/0.3896179)\\,val(ch)))";

        // 分頻消人聲 (與 AudioMixerDS 模式 2 相同)：out = D + LP(M−D) + HP(M)，D = L−R、M = (L+R)/2，兩聲道輸出相同。
        // - 200 Hz ~ 12 kHz 只有 D：傳統 L−R，消去置中的人聲
        // - 低於 200 Hz：D + (M−D) = M，就是原曲的單聲道混音，大鼓、貝斯不論聲像都保留
        //   (若用 D + LP(M)，偏右聲像的低音會被 D 抵消)
        // - 高於 12 kHz：D + M，保留鈸與 D 的立體聲空間感
        // 分界以 KTV 伴奏帶對照試聽決定：高頻分界原為 6 kHz，人聲齒音與泛音 (4~10 kHz) 會從 HP(M) 漏回來。
        // 濾波器都是 2 階 (Q 0.707，即 ffmpeg lowpass/highpass 預設值)，AudioMixerDS 用同一組 RBJ 公式。
        // 先轉 float：s16 時 pan 的相加會先削峰；3.0 聲道 (FL/FR/FC) 讓 c 選項只濾其中一個聲道，整條是線性串接。
        private const string VocalRemovalFilter =
            "lavfi=[aformat=sample_fmts=flt|fltp," +
            "pan=3.0|c0=c0-c1|c1=-0.5*c0+1.5*c1|c2=0.5*c0+0.5*c1," +   // FL = D、FR = M−D、FC = M
            "lowpass=f=200:c=FR,highpass=f=12000:c=FC," +
            "pan=stereo|c0=c0+c1+c2|c1=c0+c1+c2]";

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
        /// display-resample 會讓 mpv 每次螢幕更新都重畫；顯示卡較弱的機台跟不上時，影片優先：改回依影片換格。
        /// <para>
        /// 開歌後第一個 5 秒只取基準 (載入時的掉格不算)；連續兩次 5 秒都超標才降級，避免短暫負載尖峰
        /// (例如同時下載 YouTube) 就讓跑馬燈變成 25~30fps。降級只到這首歌結束，下一首歌重新嘗試。
        /// </para>
        /// </summary>
        private void CheckSmoothMotion()
        {
            if (!_smoothMotion || ++_smoothCheckTicks < 50) return; // 每 5 秒檢查一次
            _smoothCheckTicks = 0;

            long bad = 0;
            foreach (var name in new[] { "frame-drop-count", "vo-delayed-frame-count", "mistimed-frame-count" })
                if (long.TryParse(GetString(name), out long n)) bad += n;
            long delayed = long.TryParse(GetString("vo-delayed-frame-count"), out long dl) ? dl : 0;

            if (_lastBadFrames < 0 || bad < _lastBadFrames || delayed < _lastDelayedFrames)
            {
                _lastBadFrames = bad; _lastDelayedFrames = delayed; // 開歌後的基準 / 計數被 mpv 歸零
                return;
            }
            long delta = bad - _lastBadFrames;
            long delayedDelta = delayed - _lastDelayedFrames;
            _lastBadFrames = bad;
            _lastDelayedFrames = delayed;

            // 這 5 秒內沒有任何影格因顯示跟不上而延遲 (vo-delayed) → 不是顯示卡的問題：
            // 影格時間戳錯亂的檔案 (例如部分 MPEG-2 檔每秒約 2 對影格的時間互換) 會穩定產生
            // drop/mistimed，實測 5 秒 44 格、delayed 一直是 0；若照算會在 15 秒後降級，整首跑馬燈只剩 30 次/秒、一跳一跳
            if (delta <= 15 || delayedDelta == 0)
            {
                _smoothBadWindows = 0;
                return;
            }
            if (++_smoothBadWindows < 2)
            {
                AppLogger.Log($"[mpv] {delta} dropped/delayed frames in 5s with display-resample; watching (1st time).");
                return;
            }

            _smoothMotion = false;
            SetProperty("video-sync", "audio");
            SetProperty("interpolation", "no");
            AppLogger.Log($"[mpv] {delta} dropped/delayed frames in 5s with display-resample (2nd time in a row); smooth motion disabled for this song (vo={GetString("current-vo")}, display-fps={GetString("display-fps")}).");
        }

        private void ApplyVolume()
        {
            // mpv 軟體音量是三次方曲線：gain = (volume/100)^3
            double mpvVolume = _volume <= 0 ? 0 : Math.Min(100, 100.0 * Math.Cbrt(_volume));
            SetProperty("volume", mpvVolume.ToString("0.##", CultureInfo.InvariantCulture));
        }

        // ---------- 多音軌：所有音軌一起解碼 (MediaKit 作法) ----------
        //
        // mpv 預設只解多工選取的音軌。換音軌時要回頭補讀新音軌，部分檔案做不到 (MPEG-PS 的時間戳不符合 mpv 的條件時：
        // "can't issue refresh seek" → 整首沒聲音)，就算做得到也要 seek，畫面會頓一下、關鍵影格稀疏的檔會跳秒。
        // 改成比照 MediaKit：每條音軌都解碼 (轉成立體聲後以 amerge 併成 2N 聲道)，換音軌只改 af 最前面的 pan，
        // 不 seek、不斷音。以 2~5 音軌、5.1、mp2/ac3/pcm/aac 混合的測試檔錄音比對驗證。
        //
        // amerge 是逐樣本對齊、不看時間戳，而各音軌解碼器的第一個樣本時間不同 (pcm/ac3 混合的 DVD 檔實測差 128ms)，
        // 所以每條音軌先用 aresample 的 first_pts 依時間戳補齊/裁切到同一起點。濾鏡圖在每次 seek 都會由 mpv 重建，
        // 起點必須是那次 seek 的位置 (見 StartMergedSeek)。
        // apad + atrim=end：音軌長度不同時 amerge 會在最短的音軌結束時停止，先補靜音到片長。

        /// <summary>
        /// on_preloaded：檔案已開啟、還沒選音軌與建立解碼器。lavfi-complex 要在這裡設定 —
        /// 開檔後才設會觸發重建與原地 seek，實測每首歌開頭被跳過 0.4~0.8 秒 (關鍵影格稀疏的 mkv 跳過 5.3 秒)。
        /// lavfi-complex 是跨檔案保留的選項，單音軌歌曲與閒置黑畫面要清掉。在 mpv 事件執行緒上執行。
        /// </summary>
        private void OnPreloaded()
        {
            int[]? merged = null;
            if (GetString("path") != IdleSource)
            {
                var ids = new List<int>();
                int count = int.TryParse(GetString("track-list/count"), out int c) ? c : 0;
                for (int i = 0; i < count; i++)
                {
                    if (GetString($"track-list/{i}/type") == "audio" && int.TryParse(GetString($"track-list/{i}/id"), out int id))
                        ids.Add(id);
                }
                // amerge 最多 64 聲道 = 32 條立體聲音軌
                if (ids.Count >= 2 && ids.Count <= 32) merged = ids.ToArray();
            }

            string graph = "";
            if (merged != null)
            {
                _mergedDuration = double.TryParse(GetString("duration"), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && d > 0 ? d : 0;
                _selectedTrack = merged[0]; // 同 MediaKit：新檔案先聽第一條音軌，再由 MediaOpened 的處理者選
                graph = BuildMergedGraph(merged, _mergedDuration, 0);
            }
            _mergedTracks = merged;
            _currentGraph = graph;
            int err = SetProperty("lavfi-complex", graph);
            if (err < 0 && merged != null)
            {
                // 退回一般方式 (只解選取的音軌)
                _mergedTracks = null;
                _currentGraph = "";
                SetProperty("lavfi-complex", "");
            }
        }

        /// <summary>
        /// 多音軌的 lavfi-complex。alignAt = 各音軌對齊的起點 (秒，應為濾鏡圖建立時的播放位置)；小於 0 = 不對齊
        /// (seek 途中的暫時濾鏡圖：起點與實際位置差很多時，first_pts 會補上或解碼丟掉一大段，seek 到 280 秒實測要 2.4 秒)。
        /// </summary>
        private static string BuildMergedGraph(int[] tracks, double duration, double alignAt)
        {
            var sb = new System.Text.StringBuilder();
            // first_pts 以輸入的取樣率計 → 先統一成 48kHz 再對齊 (44.1kHz 的歌直接用會差 8.8%)
            string align = alignAt >= 0
                ? ":first_pts=" + ((long)Math.Round(alignAt * MergedSampleRate)).ToString(CultureInfo.InvariantCulture)
                : "";
            // 片長不明時不補靜音：apad 沒有 atrim 會一直輸出，歌曲不會結束
            string pad = duration > 0 ? ",apad,atrim=end=" + duration.ToString("0.###", CultureInfo.InvariantCulture) : "";
            for (int i = 0; i < tracks.Length; i++)
            {
                sb.Append($"[aid{tracks[i]}]aresample={MergedSampleRate},aresample=async=1{align}," +
                          $"aformat=sample_fmts=fltp:channel_layouts=stereo{pad}[a{i}];");
            }
            for (int i = 0; i < tracks.Length; i++) sb.Append($"[a{i}]");
            sb.Append($"amerge=inputs={tracks.Length}[ao]");
            return sb.ToString();
        }

        private void SetMergedGraph(double alignAt)
        {
            var merged = _mergedTracks;
            if (merged == null) return;
            string graph = BuildMergedGraph(merged, _mergedDuration, alignAt);
            _currentGraph = graph;
            SetProperty("lavfi-complex", graph);
        }

        /// <summary>
        /// 多音軌的使用者 seek (拖曳進度條)。濾鏡圖的對齊起點要等於 seek 的位置，而且每一步都要等前一步完成：
        /// 1. 換成不對齊的濾鏡圖 (mpv 會原地重建)   2. seek 到目標
        /// 3. 換成以目標對齊的濾鏡圖 (原地重建)      4. 再精確 seek 到目標一次
        /// 不等前一步完成就下一步時，mpv 會先用新的濾鏡圖從舊位置處理到目標 (實測 200 秒要 0.3 秒)；
        /// mkv 在重建後緊接著 seek 會落到下一個關鍵影格 (實測跳過 3 秒)。第 4 步就是修正第 3 步重建的落點。
        /// 全程暫停 (實測 150~270ms)，完成後恢復原本的播放狀態。
        /// </summary>
        private void StartMergedSeek(double seconds)
        {
            if (_seekStep == 0) _resumeAfterSeek = !_paused;
            _seekTarget = Math.Max(0, seconds);
            SetProperty("pause", "yes");
            // 同 Pause()：暫停時 mpv 限制手動重畫間隔，跑馬燈會掉到 ~30~40 次/秒；seek 這 0.2 秒也一樣要解除 (實測 OSD 間隔 33ms)
            SetProperty("display-fps-override", "1000");
            _seekTimeout.Stop();
            _seekTimeout.Start();
            string unaligned = BuildMergedGraph(_mergedTracks!, _mergedDuration, -1);
            if (_currentGraph == unaligned)
            {
                // 已經是不對齊的濾鏡圖 (seek 途中又 seek)：mpv 不會重建，直接 seek
                _seekStep = 2;
                SeekExact(_seekTarget);
                return;
            }
            _seekStep = 1;
            SetMergedGraph(-1);
        }

        private void OnMergedSeekRestart()
        {
            if (_seekStep == 0 || _closing) return;
            _seekTimeout.Stop();
            _seekTimeout.Start();
            switch (_seekStep)
            {
                case 1:
                    _seekStep = 2;
                    SeekExact(_seekTarget);
                    break;
                case 2:
                    // 中途又 seek 時可能收到上一個步驟的 restart：位置還沒到就繼續等
                    if (!NearTarget()) break;
                    _seekStep = 3;
                    SetMergedGraph(_seekTarget);
                    break;
                case 3:
                    _seekStep = 4;
                    SeekExact(_seekTarget);
                    break;
                case 4:
                    if (!NearTarget()) { SeekExact(_seekTarget); break; }
                    FinishMergedSeek();
                    break;
            }
        }

        private bool NearTarget() =>
            double.TryParse(GetString("time-pos"), NumberStyles.Float, CultureInfo.InvariantCulture, out double pos) && Math.Abs(pos - _seekTarget) < 1.0;

        private void SeekExact(double seconds) =>
            Command("seek", seconds.ToString("0.######", CultureInfo.InvariantCulture), "absolute+exact");

        private void FinishMergedSeek()
        {
            if (_seekStep == 0) return;
            _seekStep = 0;
            _seekTimeout.Stop();
            if (_resumeAfterSeek && _fileLoaded)
            {
                SetProperty("display-fps-override", "0"); // 使用者原本就暫停時保留 Pause() 的設定
                SetProperty("pause", "no");
            }
        }

        /// <summary>換歌/停止時中止進行中的多音軌 seek (不恢復播放，由換歌流程決定)。</summary>
        private void CancelMergedSeek()
        {
            _seekStep = 0;
            _seekTimeout.Stop();
        }

        private void ApplyAudioFilters()
        {
            var filters = new List<string>();
            var merged = _mergedTracks;
            if (merged != null)
            {
                // 多音軌：amerge 後音軌 i 的左右聲道是 c(2i)、c(2i+1)，先選出要聽的那一對 (其他音軌等於音量 0)
                int i = Math.Max(0, Array.IndexOf(merged, _selectedTrack));
                filters.Add($"lavfi=[pan=stereo|c0=c{2 * i}|c1=c{2 * i + 1}]");
            }
            if (Math.Abs(_audioPitch - 1.0) > 0.0001)
                filters.Add("rubberband=pitch-scale=" + _audioPitch.ToString("0.####", CultureInfo.InvariantCulture));
            switch (_audioChannel)
            {
                case 1: filters.Add("lavfi=[pan=stereo|c0=c0|c1=c0]"); break;          // 左聲道
                case 2: filters.Add("lavfi=[pan=stereo|c0=c1|c1=c1]"); break;          // 右聲道
                case 3: filters.Add(VocalRemovalFilter); break;                        // 消人聲 (分頻 L−R)
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
