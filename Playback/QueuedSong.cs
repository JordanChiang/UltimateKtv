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

        /// <summary>YouTube 影片長度 (M:SS / H:MM:SS，不知道時空白)，待播清單的歌手欄顯示它。YouTube 歌的 SingerName 一律空白 (跑馬燈、正在播放不帶歌手)。</summary>
        public string Duration { get; init; } = string.Empty;

        public bool IsRandom { get; init; }

        /// <summary>隨機歌開播時套用的聲道：-1=不指定, 0=人聲, 1=伴唱。</summary>
        public int RandomAudioChannel { get; init; } = -1;

        /// <summary>
        /// 是否為同一首歌：點歌時視為重複，歌曲列表上標示「已在待播」也用這條規則。
        /// 同檔案就是同一首；兩首都是 YouTube 時比影片 ID (沒有影片 ID 時比 SongId)，不比歌名 (同名的不同影片不算)；
        /// 兩首都是歌庫的歌時，同歌名加同歌手也算。
        /// </summary>
        public static bool IsSameSong(QueuedSong a, QueuedSong b)
        {
            if (!string.IsNullOrEmpty(a.FilePath) && a.FilePath.Equals(b.FilePath, StringComparison.OrdinalIgnoreCase)) return true;
            if (a.IsYoutube != b.IsYoutube) return false;

            if (a.IsYoutube)
            {
                string idA = YoutubeVideoId.Resolve(null, a.SongId, a.FilePath);
                string idB = YoutubeVideoId.Resolve(null, b.SongId, b.FilePath);
                return idA.Length > 0 && idB.Length > 0
                    ? idA == idB
                    : !string.IsNullOrEmpty(a.SongId) && a.SongId == b.SongId;
            }

            return !string.IsNullOrEmpty(a.SongName) &&
                   a.SongName.Equals(b.SongName, StringComparison.OrdinalIgnoreCase) &&
                   a.SingerName.Equals(b.SingerName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
