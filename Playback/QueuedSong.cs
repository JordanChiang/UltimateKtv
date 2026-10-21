using System;

namespace UltimateKtv.Playback
{
    /// <summary>點歌從哪裡發起 (寫進 log，並決定是否切掉隨機歌)。</summary>
    public enum OrderSource
    {
        User,
        Remote,
        YouTube,
        Random
    }

    public enum EnqueueResult
    {
        Added,
        /// <summary>插播的歌已在待播清單中，改為移到第一首。</summary>
        MovedToFirst,
        Duplicate,
        FileMissing,
        InvalidPath
    }

    public enum PlaybackState
    {
        Idle,
        /// <summary>已交給 player 開檔，尚未收到 Opened/Failed。</summary>
        Opening,
        Playing
    }

    public enum SongEndReason
    {
        Finished,
        Skipped,
        Failed,
        /// <summary>隨機歌被使用者點歌切掉。</summary>
        CutOff,
        /// <summary>重唱或單曲循環：同一首歌將重新開檔。</summary>
        Replayed
    }

    /// <summary>
    /// 待播清單中的一首歌 (不可修改)。Id 用來指定要移動/刪除哪一首。
    /// </summary>
    public sealed record QueuedSong
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public string SongId { get; init; } = string.Empty;
        public string SongName { get; init; } = string.Empty;
        public string SingerName { get; init; } = string.Empty;
        public string FilePath { get; init; } = string.Empty;
        public int Volume { get; init; } = 90;
        public int AudioTrack { get; init; }
        public string OrderedBy { get; init; } = string.Empty;
        public bool IsYoutube { get; init; }
        public bool IsRandom { get; init; }

        /// <summary>隨機歌開播時套用的聲道：-1=不指定, 0=人聲, 1=伴唱。</summary>
        public int RandomAudioChannel { get; init; } = -1;
    }
}
