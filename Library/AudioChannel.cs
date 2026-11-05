namespace UltimateKtv
{
    /// <summary>
    /// 聲道 (player 的 AudioChannel)。數值與 MediaKit、MpvPlayer、歌庫 (YouTube 點唱紀錄的 Track、單音軌歌曲的 Song_Track) 相同，不可改動。
    /// </summary>
    public enum AudioChannel
    {
        Stereo = 0,
        Left = 1,
        Right = 2,
        /// <summary>消人聲 (左右相減)。</summary>
        NoVocal = 3
    }

    public static class AudioChannels
    {
        /// <summary>資料庫等外部來源的數值轉成聲道；不是 0~3 時回傳 null。</summary>
        public static AudioChannel? FromValue(int? value) =>
            value is >= (int)AudioChannel.Stereo and <= (int)AudioChannel.NoVocal ? (AudioChannel)value.Value : null;

        /// <summary>單音軌歌曲左右聲道一邊伴唱、一邊人聲：給其中一邊 (數值)，回傳另一邊 (不是左聲道時一律回傳左聲道)。</summary>
        public static AudioChannel OppositeSide(int channel) =>
            channel == (int)AudioChannel.Left ? AudioChannel.Right : AudioChannel.Left;
    }
}
