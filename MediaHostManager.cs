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
        }

        /// <summary>
        /// Creates a new, clean player instance.
        /// </summary>
        private static UKtvPlayer CreateNewPlayer()
        {
            return new DirectShowPlayer(SettingsManager.Instance.CurrentSettings);
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
