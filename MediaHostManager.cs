using System;
using System.IO;
using System.Windows.Media;
using CrazyKTV_MediaKit.DirectShow.Controls;
using CrazyKTV_MediaKit.DirectShow.MediaPlayers;

namespace UltimateKtv
{
    /// <summary>
    /// Singleton host that owns a single MediaUriElement instance for the whole app.
    /// </summary>
    public sealed class MediaHostManager
    {
        private static readonly Lazy<MediaHostManager> _instance = new Lazy<MediaHostManager>(() => new MediaHostManager());
        public static MediaHostManager Instance => _instance.Value;

        public MediaUriElement Element { get; private set; }

        private MediaHostManager()
        {
            // Create the initial element
            Element = CreateNewElement();
        }

        /// <summary>
        /// Creates a new, clean instance of the MediaUriElement.
        /// </summary>
        private MediaUriElement CreateNewElement()
        {
            var element = new MediaUriElement();
            element.BeginInit();
            
            // Apply settings
            var settings = SettingsManager.Instance.CurrentSettings;
            
            // Video Renderer
            element.VideoRenderer = settings.VideoRendererType == 1 
                ? VideoRendererType.EnhancedVideoRenderer 
                : VideoRendererType.VideoMixingRenderer9;

            // Audio Renderer
            if (!string.IsNullOrEmpty(settings.AudioRendererDevice) && settings.AudioRendererDevice != "Default DirectSound Device")
            {
                element.AudioRenderer = settings.AudioRendererDevice;
            }

            // HW Acceleration
            element.EnableHWAccel = settings.EnableHWAccel;
            if (settings.EnableHWAccel)
            {
                // Assuming DefaultHWAccel takes the int value directly or cast to an enum
                 // 0:"AutoDetect", 1:"NVIDIA CUVID", 2:"Intel® Quick Sync", 3:"DXVA2 (copy-back)", 4:"DXVA2 (native)", 5:"D3D11"
                 element.DefaultHWAccel = (CrazyKTV_MediaKit.DirectShow.Interfaces.LavVideo.LAVHWAccel)settings.HWAccelMode;
            }

            element.DeeperColor = !(settings.VideoRendererType == 0);
            element.Stretch = Stretch.Uniform; // Main window will preview via VisualBrush; secondary will run fullscreen
            element.EnableAudioCompressor = false;
            element.EnableAudioProcessor = false;
            element.EndInit();
            
            return element;
        }

        /// <summary>
        /// Discards the current MediaUriElement and creates a new one to recover from a hung state.
        /// </summary>
        public void ResetInstance()
        {
            Element = CreateNewElement();
        }
    }
}
