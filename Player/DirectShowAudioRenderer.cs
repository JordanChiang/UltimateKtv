namespace UltimateKtv
{
    /// <summary>
    /// DirectShow 播放引擎的音訊渲染器選擇規則 (純函式，不依賴 WPF / MediaKit，方便單元測試)。
    /// </summary>
    public static class DirectShowAudioRenderer
    {
        /// <summary>
        /// MediaUriPlayer 的 AudioRenderer 預設值，代表系統預設 DirectSound 裝置。
        /// </summary>
        public const string DefaultDeviceName = "Default DirectSound Device";

        /// <summary>
        /// 依設定的音訊輸出裝置，決定要指派給 MediaUriElement.AudioRenderer 的裝置名稱。
        /// 一律回傳有效的裝置名稱字串，絕不回傳 null 或空字串：
        /// MediaKit 的 InsertAudioRenderer 遇 null/空字串會回傳 null，
        /// 開檔連接音訊渲染器時會爆 E_POINTER (無效的指標)。
        /// </summary>
        public static string Resolve(string? audioRendererDevice)
            => string.IsNullOrEmpty(audioRendererDevice) || audioRendererDevice == DefaultDeviceName
                ? DefaultDeviceName
                : audioRendererDevice;
    }
}
