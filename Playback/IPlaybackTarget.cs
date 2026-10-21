using System;

namespace UltimateKtv.Playback
{
    /// <summary>
    /// PlaybackSession 眼中的 player：只需要開檔、停止，以及三個結果事件。
    /// 事件必須在建立 PlaybackSession 的執行緒 (UI 執行緒) 上觸發。
    /// </summary>
    public interface IPlaybackTarget
    {
        /// <summary>停止目前的歌並開始開啟 song；結果以 Opened 或 Failed 回報。</summary>
        void Open(QueuedSong song);

        void Stop();

        event EventHandler? Opened;
        event EventHandler? Ended;
        event EventHandler<Exception?>? Failed;
    }
}
