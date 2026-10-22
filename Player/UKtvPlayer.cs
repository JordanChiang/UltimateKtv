using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace UltimateKtv
{
    /// <summary>
    /// 播放引擎抽象介面。呼叫端只依賴這個介面，不直接接觸 DirectShow / libmpv 等實作。
    /// 時間單位一律為 100ns ticks（與 TimeSpan.Ticks 相同）。
    /// </summary>
    public interface UKtvPlayer
    {
        /// <summary>要放進版面或給 VisualBrush 使用的影像元素。</summary>
        FrameworkElement View { get; }

        /// <summary>引擎名稱，供記錄用。</summary>
        string EngineName { get; }

        /// <summary>目前視訊渲染器名稱，供記錄用。</summary>
        string VideoRendererName { get; }

        Uri? Source { get; set; }
        bool IsPlaying { get; }
        bool HasVideo { get; }

        long MediaPosition { get; set; }
        long MediaDuration { get; }

        /// <summary>音量 0..1。</summary>
        double Volume { get; set; }
        /// <summary>音量放大百分比（100 = 原音量）。</summary>
        int AudioAmplify { get; set; }
        /// <summary>升降 Key 倍率（1.0 = 原調）。</summary>
        double AudioPitch { get; set; }

        /// <summary>可用音軌編號清單。</summary>
        List<int> AudioStreams { get; }
        int AudioTrack { get; set; }
        /// <summary>0=立體聲, 1=左聲道, 2=右聲道, 3=消人聲。</summary>
        int AudioChannel { get; set; }

        Stretch Stretch { get; set; }

        /// <summary>
        /// 建立電視畫面的疊加層 (跑馬燈/歌詞)。videoRoot 是電視視窗中與影片同大小、左上角對齊的元素。
        /// libmpv 的影片是原生視窗，WPF 元素疊不上去，由 mpv 合成；DirectShow 直接沿用 WPF 疊加。
        /// </summary>
        ITvOverlay CreateTvOverlay(FrameworkElement videoRoot);

        /// <summary>主視窗預覽用元素；null 表示沿用 VisualBrush(View)。</summary>
        FrameworkElement? PreviewView { get; }

        /// <summary>主視窗預覽是否顯示 (關閉時預覽實例停止解碼)。</summary>
        bool PreviewEnabled { get; set; }

        /// <summary>滑鼠在電視畫面上移動 (WPF 收不到原生視窗的滑鼠時由引擎回報)。</summary>
        event EventHandler? VideoMouseActivity;

        /// <summary>在電視畫面上雙擊。</summary>
        event EventHandler? VideoDoubleClick;

        /// <summary>套用設定檔中的渲染器、音效裝置、硬體加速等播放設定（每首歌開播前呼叫）。</summary>
        void ApplySettings(AppSettings settings);

        void Play();
        void Pause();
        void Stop();
        void Close();

        event EventHandler? MediaOpened;
        event EventHandler? MediaEnded;
        event EventHandler? MediaClosed;
        event EventHandler? MediaPositionChanged;
        event EventHandler<PlayerFailedEventArgs>? MediaFailed;
    }

    public sealed class PlayerFailedEventArgs : EventArgs
    {
        public PlayerFailedEventArgs(string message, Exception exception)
        {
            Message = message;
            Exception = exception;
        }

        public string Message { get; }
        public Exception Exception { get; }
    }
}
