using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CrazyKTV_MediaKit.DirectShow.Controls;
using CrazyKTV_MediaKit.DirectShow.Interfaces.LavVideo;
using CrazyKTV_MediaKit.DirectShow.MediaPlayers;

namespace UltimateKtv
{
    /// <summary>
    /// 以 CrazyKTV_MediaKit (DirectShow + LAV Filters) 實作的播放引擎。
    /// </summary>
    public sealed class DirectShowPlayer : UKtvPlayer
    {
        private readonly MediaUriElement _element;

        public DirectShowPlayer(AppSettings settings)
        {
            _element = new MediaUriElement();
            _element.BeginInit();
            ApplyRendererSettings(settings);
            _element.Stretch = Stretch.Uniform; // Main window will preview via VisualBrush; secondary will run fullscreen
            _element.EnableAudioCompressor = false;
            _element.EnableAudioProcessor = false;
            _element.EndInit();

            // 事件轉送時以 this 當 sender，讓呼叫端拿到的是 UKtvPlayer
            _element.MediaOpened += (_, _) => MediaOpened?.Invoke(this, EventArgs.Empty);
            _element.MediaEnded += (_, _) => MediaEnded?.Invoke(this, EventArgs.Empty);
            _element.MediaClosed += (_, _) => MediaClosed?.Invoke(this, EventArgs.Empty);
            _element.MediaPositionChanged += (_, _) => MediaPositionChanged?.Invoke(this, EventArgs.Empty);
            _element.MediaFailed += (_, e) => MediaFailed?.Invoke(this,
                new PlayerFailedEventArgs(e.Message, e.Exception ?? new Exception(e.Message)));
        }

        public FrameworkElement View => _element;
        public ITvOverlay CreateTvOverlay(FrameworkElement videoRoot) => WpfTvOverlay.Instance;
        public FrameworkElement? PreviewView => null;
        public bool PreviewEnabled { get; set; } = true;

        // DirectShow 的影片是 WPF 元素，滑鼠事件由 WPF 直接處理
        public event EventHandler? VideoMouseActivity { add { } remove { } }
        public event EventHandler? VideoDoubleClick { add { } remove { } }
        public string EngineName => "DirectShow";
        public string VideoRendererName => _element.VideoRenderer.ToString();

        public Uri? Source
        {
            get => _element.Source;
            set => _element.Source = value;
        }

        public bool IsPlaying => _element.IsPlaying;
        public bool HasVideo => _element.HasVideo;

        public long MediaPosition
        {
            get => _element.MediaPosition;
            set => _element.MediaPosition = value;
        }

        public long MediaDuration => _element.MediaDuration;

        public double Volume
        {
            get => _element.Volume;
            set => _element.Volume = value;
        }

        public int AudioAmplify
        {
            get => _element.AudioAmplify;
            set => _element.AudioAmplify = value;
        }

        public double AudioPitch
        {
            get => _element.AudioPitch;
            set => _element.AudioPitch = value;
        }

        public List<int> AudioStreams => _element.AudioStreams;

        public int AudioTrack
        {
            get => _element.AudioTrack;
            set => _element.AudioTrack = value;
        }

        public int AudioChannel
        {
            get => _element.AudioChannel;
            set => _element.AudioChannel = value;
        }

        public Stretch Stretch
        {
            get => _element.Stretch;
            set => _element.Stretch = value;
        }

        public void ApplySettings(AppSettings settings)
        {
            ApplyRendererSettings(settings);
            _element.EnableAudioCompressor = false;
            _element.EnableAudioProcessor = false;

            // Configure codec directory
            string codecFolderName = Environment.Is64BitProcess ? "Codec" : "Codec_x86";
            string codecPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, codecFolderName);
            _element.MediaUriPlayer.CodecsDirectory = codecPath;
        }

        private void ApplyRendererSettings(AppSettings settings)
        {
            // Video Renderer
            _element.VideoRenderer = settings.VideoRendererType == 1
                ? VideoRendererType.EnhancedVideoRenderer
                : VideoRendererType.VideoMixingRenderer9;

            // Audio Renderer
            if (!string.IsNullOrEmpty(settings.AudioRendererDevice) && settings.AudioRendererDevice != "Default DirectSound Device")
            {
                _element.AudioRenderer = settings.AudioRendererDevice;
            }

            // HW Acceleration
            _element.EnableHWAccel = settings.EnableHWAccel;
            if (settings.EnableHWAccel)
            {
                // 0:"AutoDetect", 1:"NVIDIA CUVID", 2:"Intel® Quick Sync", 3:"DXVA2 (copy-back)", 4:"DXVA2 (native)", 5:"D3D11"
                _element.DefaultHWAccel = (LAVHWAccel)settings.HWAccelMode;
            }

            _element.DeeperColor = !(settings.VideoRendererType == 0);
        }

        public void Play() => _element.Play();
        public void Pause() => _element.Pause();
        public void Stop() => _element.Stop();
        public void Close() => _element.Close();

        public event EventHandler? MediaOpened;
        public event EventHandler? MediaEnded;
        public event EventHandler? MediaClosed;
        public event EventHandler? MediaPositionChanged;
        public event EventHandler<PlayerFailedEventArgs>? MediaFailed;
    }
}
