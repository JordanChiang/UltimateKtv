namespace UltimateKtv.Playback
{
    /// <summary>
    /// 隨機點歌的補歌判斷：決定何時自動補一首隨機歌到待播清單。
    /// 不依賴 WPF / 播放引擎，所以可純測試 (放在 Playback/)。
    /// </summary>
    public static class RandomPlayGate
    {
        /// <summary>
        /// 待播清單為空、且目前沒有在播歌時，才自動補一首隨機歌。
        /// <para>
        /// 播歌中 (點播歌曲播放中) 待播清單雖然是空的，也不該立即插入隨機歌；
        /// 等該歌播完、播放場次閒置時再補 (由 <c>BecameIdle</c> 觸發)。
        /// </para>
        /// </summary>
        /// <param name="randomPlayEnabled">隨機點歌是否開啟。</param>
        /// <param name="hasCurrentSong">是否有正在播放 (或開檔中) 的歌。</param>
        /// <param name="waitingListCount">待播清單歌曲數。</param>
        public static bool ShouldAutoQueue(bool randomPlayEnabled, bool hasCurrentSong, int waitingListCount)
            => randomPlayEnabled && !hasCurrentSong && waitingListCount == 0;
    }
}
