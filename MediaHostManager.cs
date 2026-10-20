using System;

namespace UltimateKtv
{
    /// <summary>
    /// Singleton host that owns a single player instance for the whole app.
    /// </summary>
    public sealed class MediaHostManager
    {
        private static readonly Lazy<MediaHostManager> _instance = new Lazy<MediaHostManager>(() => new MediaHostManager());
        public static MediaHostManager Instance => _instance.Value;

        public UKtvPlayer Player { get; private set; }

        private MediaHostManager()
        {
            // Create the initial player
            Player = CreateNewPlayer();
            if (MpvNative.Trace) MpvPlayer.StartUiLatencyProbe(System.Windows.Threading.Dispatcher.CurrentDispatcher);
        }

        /// <summary>
        /// Creates a new, clean player instance.
        /// </summary>
        private static UKtvPlayer CreateNewPlayer()
        {
            var settings = SettingsManager.Instance.CurrentSettings;
            if (settings.PlayerEngine == 1)
            {
                // libmpv 不可用 (找不到 DLL、位元數不符、初始化失敗) 時退回 DirectShow，確保一定能播放
                try
                {
                    if (MpvNative.EnsureLoaded())
                    {
                        return new MpvPlayer(settings);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("[Player] libmpv init failed, falling back to DirectShow", ex);
                }
                AppLogger.Log("[Player] PlayerEngine=libmpv requested but unavailable; using DirectShow.");
            }
            return new DirectShowPlayer(settings);
        }

        /// <summary>
        /// Discards the current player and creates a new one to recover from a hung state.
        /// </summary>
        public void ResetInstance()
        {
            Player = CreateNewPlayer();
        }
    }
}
