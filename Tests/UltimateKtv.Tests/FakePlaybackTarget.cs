using UltimateKtv.Playback;

namespace UltimateKtv.Tests
{
    /// <summary>測試用 player：記錄開了哪些歌，事件由測試手動觸發。</summary>
    internal sealed class FakePlaybackTarget : IPlaybackTarget
    {
        public List<QueuedSong> Opened_ { get; } = new();
        public int StopCount { get; private set; }
        public bool ThrowOnOpen { get; set; }

        public QueuedSong? LastOpened => Opened_.LastOrDefault();

        public event EventHandler? Opened;
        public event EventHandler? Ended;
        public event EventHandler<Exception?>? Failed;

        public void Open(QueuedSong song)
        {
            if (ThrowOnOpen) throw new InvalidOperationException("open failed");
            Opened_.Add(song);
        }

        public void Stop() => StopCount++;

        public void RaiseOpened() => Opened?.Invoke(this, EventArgs.Empty);
        public void RaiseEnded() => Ended?.Invoke(this, EventArgs.Empty);
        public void RaiseFailed() => Failed?.Invoke(this, new Exception("media failed"));
    }
}
