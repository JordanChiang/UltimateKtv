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
        private const ulong ObsPause = 1, ObsTimePos = 2, ObsDuration = 3;
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
            SetOption("input-cursor", "no");
            SetOption("cursor-autohide", "no");
            SetOption("osc", "no");
            SetOption("osd-level", "0");
            SetOption("ytdl", "no");                 // YouTube 串流網址已由 YtDlpHelper 解析
            SetOption("load-scripts", "no");
            SetOption("config", "no");               // 不讀使用者的 mpv.conf，行為可預期
            SetOption("volume-max", "1000");
            SetOption("audio-client-name", "UltimateKtv");
            SetOption("background-color", "#000000");
            if (!Environment.Is64BitProcess)
            {
                // 32 位元 libmpv 的 gpu-next (libplacebo + d3d11) 一開始播放就存取違規當掉；vo=gpu 正常
                SetOption("vo", "gpu");
            }

            int err = MpvNative.Initialize!(_ctx);
            if (err < 0) throw new InvalidOperationException($"mpv_initialize failed: {MpvNative.Error(err)}");

            Observe(ObsPause, "pause", MpvNative.FORMAT_FLAG);
            Observe(ObsTimePos, "time-pos", MpvNative.FORMAT_DOUBLE);
            Observe(ObsDuration, "duration", MpvNative.FORMAT_DOUBLE);

            Stretch = Stretch.Uniform;
            ApplySettings(settings);
            _audioAmplify = settings.AudioAmplify;
            ApplyAudioFilters();
            ApplyVolume();

            // DirectShow 版會持續送 MediaPositionChanged；這裡用 UI 計時器模擬，只在播放中觸發
            _positionTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _positionTimer.Tick += (_, _) =>
            {
                if (_fileLoaded && !_paused) MediaPositionChanged?.Invoke(this, EventArgs.Empty);
            };

            _eventThread = new Thread(EventLoop) { IsBackground = true, Name = "mpv-events" };
            _eventThread.Start();

            AppLogger.Log($"[mpv] Player created ({MpvNative.LoadedPath}), mpv version: {GetString("mpv-version")}");
        }

        public FrameworkElement View => _host;
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
                Command("seek", (value / (double)TimeSpan.TicksPerSecond).ToString("0.###", CultureInfo.InvariantCulture), "absolute");
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
        }

        public void Pause() => SetProperty("pause", "yes");

        public void Stop()
        {
            _fileLoaded = false;
            _positionTimer.Stop();
            Command("stop");
        }

        public void Close()
        {
            if (_closing) return;
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

        private void LoadCurrentSource()
        {
            if (_source == null) return;
            _fileLoaded = false;
            Interlocked.Exchange(ref _positionTicks, 0);
            Interlocked.Exchange(ref _durationTicks, 0);
            string target = _source.IsFile ? _source.LocalPath : _source.AbsoluteUri;
            // 先暫停載入：等 MediaOpened 處理完音軌/聲道/音量後才開始播放，避免開頭漏出錯誤聲道
            // (不用 loadfile 的 per-file 選項參數，因為 mpv 0.38 前後參數位置不同)
            SetProperty("pause", "yes");
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
                    var prop = Marshal.PtrToStructure<MpvNative.mpv_event_property>(ev.data);
                    if (prop.data == IntPtr.Zero) break; // 屬性無值 (例如沒有檔案)
                    if (ev.reply_userdata == ObsPause && prop.format == MpvNative.FORMAT_FLAG)
                        _paused = Marshal.ReadInt32(prop.data) != 0;
                    else if (ev.reply_userdata == ObsTimePos && prop.format == MpvNative.FORMAT_DOUBLE)
                        Interlocked.Exchange(ref _positionTicks, SecondsToTicks(ReadDouble(prop.data)));
                    else if (ev.reply_userdata == ObsDuration && prop.format == MpvNative.FORMAT_DOUBLE)
                        Interlocked.Exchange(ref _durationTicks, SecondsToTicks(ReadDouble(prop.data)));
                    break;

                case MpvNative.EVENT_FILE_LOADED:
                    RefreshTracks();
                    _dispatcher.InvokeAsync(() =>
                    {
                        if (_closing) return;
                        _fileLoaded = true;
                        MediaOpened?.Invoke(this, EventArgs.Empty);
                        SetProperty("pause", "no"); // 設定都套用後才開始播放
                        _positionTimer.Start();
                        AppLogger.Log($"[mpv] opened: vo={GetString("current-vo")}, ao={GetString("current-ao")}, " +
                                      $"aid={GetString("aid")}, volume={GetString("volume")}, mute={GetString("mute")}, af={GetString("af")}");
                    });
                    break;

                case MpvNative.EVENT_END_FILE:
                    var end = Marshal.PtrToStructure<MpvNative.mpv_event_end_file>(ev.data);
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
            IntPtr n = MpvNative.Utf8(name), v = MpvNative.Utf8(value);
            try
            {
                int err = MpvNative.SetOptionString!(_ctx, n, v);
                if (err < 0) AppLogger.Log($"[mpv] set option {name}={value} failed: {MpvNative.Error(err)}");
            }
            finally { Marshal.FreeCoTaskMem(n); Marshal.FreeCoTaskMem(v); }
        }

        private int SetProperty(string name, string value)
        {
            if (_ctx == IntPtr.Zero || _closing) return -1;
            IntPtr n = MpvNative.Utf8(name), v = MpvNative.Utf8(value);
            try
            {
                int err = MpvNative.SetPropertyString!(_ctx, n, v);
                if (err < 0) AppLogger.Log($"[mpv] set {name}={value} failed: {MpvNative.Error(err)}");
                return err;
            }
            finally { Marshal.FreeCoTaskMem(n); Marshal.FreeCoTaskMem(v); }
        }

        private string? GetString(string name)
        {
            if (_ctx == IntPtr.Zero || _closing) return null;
            IntPtr n = MpvNative.Utf8(name);
            try
            {
                IntPtr p = MpvNative.GetPropertyString!(_ctx, n);
                if (p == IntPtr.Zero) return null;
                string? s = MpvNative.PtrToUtf8(p);
                MpvNative.Free!(p);
                return s;
            }
            finally { Marshal.FreeCoTaskMem(n); }
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
            var ptrs = new IntPtr[args.Length + 1];
            IntPtr arr = IntPtr.Zero;
            try
            {
                for (int i = 0; i < args.Length; i++) ptrs[i] = MpvNative.Utf8(args[i]);
                arr = Marshal.AllocCoTaskMem(IntPtr.Size * ptrs.Length);
                Marshal.Copy(ptrs, 0, arr, ptrs.Length);
                int err = MpvNative.Command!(_ctx, arr);
                if (err < 0) AppLogger.Log($"[mpv] command {string.Join(" ", args)} failed: {MpvNative.Error(err)}");
                return err;
            }
            finally
            {
                foreach (var p in ptrs) if (p != IntPtr.Zero) Marshal.FreeCoTaskMem(p);
                if (arr != IntPtr.Zero) Marshal.FreeCoTaskMem(arr);
            }
        }
    }
}
