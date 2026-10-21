using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UltimateKtv.Playback
{
    public sealed class SongEndedEventArgs : EventArgs
    {
        public SongEndedEventArgs(QueuedSong song, SongEndReason reason)
        {
            Song = song;
            Reason = reason;
        }

        public QueuedSong Song { get; }
        public SongEndReason Reason { get; }
    }

    /// <summary>
    /// 播放場次：擁有待播清單與正在播放的歌，決定何時開播下一首。
    /// <para>
    /// 只能在建立它的執行緒 (UI 執行緒) 上呼叫，事件也都在該執行緒上觸發。
    /// 待播清單只能透過這裡的方法修改；Snapshot 是唯讀快照。
    /// </para>
    /// </summary>
    public sealed class PlaybackSession
    {
        private readonly IPlaybackTarget _target;
        private readonly Func<string, Task<bool>> _fileExists;
        private readonly Thread _ownerThread;
        private readonly List<QueuedSong> _queue = new();

        // 開檔中收到的切歌/切掉隨機歌，等 Opened 後再執行 (避免舊的 Opened 事件套到下一首)
        private SongEndReason? _pendingEnd;
        // 閒置時重唱用：最後一首播放的歌
        private QueuedSong? _last;

        public PlaybackSession(IPlaybackTarget target, Func<string, Task<bool>> fileExists)
        {
            _target = target;
            _fileExists = fileExists;
            _ownerThread = Thread.CurrentThread;

            _target.Opened += Target_Opened;
            _target.Ended += Target_Ended;
            _target.Failed += Target_Failed;
        }

        public PlaybackState State { get; private set; } = PlaybackState.Idle;

        /// <summary>正在播放 (或開檔中) 的歌；閒置時為 null。</summary>
        public QueuedSong? Current { get; private set; }

        /// <summary>單曲循環：播完後重唱，而不是播下一首。</summary>
        public bool LoopCurrent { get; set; }

        public IReadOnlyList<QueuedSong> Snapshot => _queue.ToArray();

        /// <summary>待播清單內容改變 (點歌、移動、刪除、取出下一首)。</summary>
        public event EventHandler? QueueChanged;

        /// <summary>即將交給 player 開檔 (含重唱)。</summary>
        public event EventHandler<QueuedSong>? SongStarting;

        /// <summary>player 已開檔成功，歌曲開始播放。</summary>
        public event EventHandler<QueuedSong>? SongOpened;

        public event EventHandler<SongEndedEventArgs>? SongEnded;

        /// <summary>待播清單播完，沒有正在播放的歌。</summary>
        public event EventHandler? BecameIdle;

        public async Task<EnqueueResult> EnqueueAsync(QueuedSong song, OrderSource source, bool insertFirst = false)
        {
            VerifyAccess();

            if (!song.IsYoutube)
            {
                if (string.IsNullOrEmpty(song.FilePath))
                {
                    AppLogger.Log($"Invalid file path for song '{song.SongName}': (empty)");
                    return EnqueueResult.InvalidPath;
                }

                // 檔案檢查交給背景 (NAS/HDD 延遲時不卡 UI/跑馬燈)，await 後回到 UI 執行緒
                if (!await FileExistsAsync(song.FilePath))
                {
                    AppLogger.Log($"Invalid file path for song '{song.SongName}': {song.FilePath}");
                    return EnqueueResult.FileMissing;
                }
            }

            var duplicate = FindDuplicate(song);
            if (duplicate != null)
            {
                if (insertFirst)
                {
                    // 插播已在歌單中的歌：直接移到第一首
                    MoveToFirst(duplicate.Id, source);
                    return EnqueueResult.MovedToFirst;
                }

                AppLogger.Log($"{source} action: '{song.SongName}' by '{song.SingerName}' is already in waiting list");
                return EnqueueResult.Duplicate;
            }

            if (insertFirst)
            {
                _queue.Insert(0, song);
            }
            else
            {
                _queue.Add(song);
            }

            // YouTube 的 SingerName 是頻道名/歌曲時間，不是歌手
            string songDesc = song.IsYoutube
                ? $"YouTube song '{song.SongName}' ({song.SongId})"
                : $"song '{song.SongName}' by '{song.SingerName}'";
            AppLogger.Log($"{source} action: {(insertFirst ? "Inserted" : "Added")} {songDesc} to waiting list" +
                (!string.IsNullOrEmpty(song.OrderedBy) ? $" (Ordered by: {song.OrderedBy})" : ""));
            QueueChanged?.Invoke(this, EventArgs.Empty);

            if (source != OrderSource.Random && Current is { IsRandom: true })
            {
                // 隨機歌播放中有人點歌：切掉隨機歌，讓點的歌馬上播
                AppLogger.Log("Random play cutoff: User ordered a song, stopping random playback.");
                EndCurrentOrDefer(SongEndReason.CutOff);
            }
            else if (State == PlaybackState.Idle)
            {
                _ = StartNextAsync();
            }

            return EnqueueResult.Added;
        }

        public bool MoveToFirst(Guid id, OrderSource source)
        {
            VerifyAccess();
            int idx = _queue.FindIndex(s => s.Id == id);
            if (idx < 0) return false;
            if (idx == 0) return true;

            var song = _queue[idx];
            AppLogger.Log($"{source} action: Move song '{song.SongName}' to first in waiting list");
            _queue.RemoveAt(idx);
            _queue.Insert(0, song);
            QueueChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        /// <summary>與下一首交換位置。</summary>
        public bool MoveDown(Guid id, OrderSource source)
        {
            VerifyAccess();
            int idx = _queue.FindIndex(s => s.Id == id);
            if (idx < 0 || idx >= _queue.Count - 1) return false;

            var song = _queue[idx];
            AppLogger.Log($"{source} action: Move song '{song.SongName}' down in waiting list");
            _queue[idx] = _queue[idx + 1];
            _queue[idx + 1] = song;
            QueueChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public bool Remove(Guid id, OrderSource source)
        {
            VerifyAccess();
            int idx = _queue.FindIndex(s => s.Id == id);
            if (idx < 0) return false;

            AppLogger.Log($"{source} action: Remove song '{_queue[idx].SongName}' from waiting list");
            _queue.RemoveAt(idx);
            QueueChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        /// <summary>切歌：結束正在播放的歌並播下一首。開檔中則等開檔完成再切。</summary>
        public void Skip()
        {
            VerifyAccess();
            if (Current == null) return;
            EndCurrentOrDefer(SongEndReason.Skipped);
        }

        /// <summary>重唱：正在播放的歌從頭再播；閒置時重唱最後一首。</summary>
        public void Replay()
        {
            VerifyAccess();
            if (State == PlaybackState.Opening) return;

            var song = Current ?? _last;
            if (song == null) return;

            if (Current != null)
            {
                SongEnded?.Invoke(this, new SongEndedEventArgs(Current, SongEndReason.Replayed));
            }
            OpenAgain(song);
        }

        private void Target_Opened(object? sender, EventArgs e)
        {
            VerifyAccess();
            if (State != PlaybackState.Opening || Current == null) return; // 已被切掉的舊歌

            State = PlaybackState.Playing;

            if (_pendingEnd is SongEndReason reason)
            {
                EndCurrentAndStartNext(reason);
                return;
            }

            SongOpened?.Invoke(this, Current);
        }

        private void Target_Ended(object? sender, EventArgs e)
        {
            VerifyAccess();
            // 只認播放中的歌播完；切歌後 player 停止時可能補發的 Ended 不算
            if (State != PlaybackState.Playing || Current == null) return;

            if (LoopCurrent)
            {
                SongEnded?.Invoke(this, new SongEndedEventArgs(Current, SongEndReason.Replayed));
                OpenAgain(Current);
                return;
            }

            EndCurrentAndStartNext(SongEndReason.Finished);
        }

        private void Target_Failed(object? sender, Exception? e)
        {
            VerifyAccess();
            if (Current == null) return;

            AppLogger.LogError($"Media failed to load or play: '{Current.SongName}'", e);
            EndCurrentAndStartNext(SongEndReason.Failed);
        }

        private void EndCurrentOrDefer(SongEndReason reason)
        {
            if (State == PlaybackState.Opening)
            {
                _pendingEnd = reason;
                return;
            }
            EndCurrentAndStartNext(reason);
        }

        private void EndCurrentAndStartNext(SongEndReason reason)
        {
            var song = Current!;
            Current = null;
            State = PlaybackState.Idle;
            _pendingEnd = null;
            SongEnded?.Invoke(this, new SongEndedEventArgs(song, reason));
            _ = StartNextAsync();
        }

        private void OpenAgain(QueuedSong song)
        {
            // 重唱的隨機歌視為使用者要唱的歌，之後點歌不會切掉它
            var again = song with { IsRandom = false };
            Current = again;
            _last = again;
            _pendingEnd = null;
            State = PlaybackState.Opening;
            AppLogger.Log($"User action: Replaying '{again.FilePath}'");
            SongStarting?.Invoke(this, again);
            OpenCurrent();
        }

        private async Task StartNextAsync()
        {
            if (State != PlaybackState.Idle) return;

            try
            {
                while (true)
                {
                    var next = _queue.FirstOrDefault();
                    if (next == null)
                    {
                        Current = null;
                        _target.Stop();
                        BecameIdle?.Invoke(this, EventArgs.Empty);
                        return;
                    }

                    // 從這裡開始到 Opened/Failed 都是開檔中，其他點歌只會排進清單
                    State = PlaybackState.Opening;
                    _queue.RemoveAt(0);
                    QueueChanged?.Invoke(this, EventArgs.Empty);

                    if (!next.IsYoutube && !await FileExistsAsync(next.FilePath))
                    {
                        AppLogger.Log($"Video file not found: {next.FilePath}. Removing and trying next.");
                        State = PlaybackState.Idle;
                        continue;
                    }

                    Current = next;
                    _last = next;
                    _pendingEnd = null;
                    SongStarting?.Invoke(this, next);
                    AppLogger.Log(next.IsYoutube
                        ? $"Playing from waiting list: YouTube song {next.SongName}"
                        : $"Playing from waiting list: {next.SongName} by {next.SingerName}");

                    OpenCurrent();
                    return;
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Error playing from waiting list", ex);
                Current = null;
                State = PlaybackState.Idle;
            }
        }

        /// <summary>Open 丟出例外時當作開檔失敗：結束這首並嘗試下一首。</summary>
        private void OpenCurrent()
        {
            try
            {
                _target.Open(Current!);
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"Error opening '{Current!.FilePath}'", ex);
                EndCurrentAndStartNext(SongEndReason.Failed);
            }
        }

        private QueuedSong? FindDuplicate(QueuedSong song) =>
            _queue.FirstOrDefault(item =>
                (!string.IsNullOrEmpty(item.FilePath) && item.FilePath.Equals(song.FilePath, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(item.SongName) &&
                 item.SongName.Equals(song.SongName, StringComparison.OrdinalIgnoreCase) &&
                 item.SingerName.Equals(song.SingerName, StringComparison.OrdinalIgnoreCase)));

        private async Task<bool> FileExistsAsync(string path)
        {
            try
            {
                return await _fileExists(path);
            }
            catch
            {
                return false;
            }
        }

        private void VerifyAccess()
        {
            if (Thread.CurrentThread != _ownerThread)
            {
                throw new InvalidOperationException("PlaybackSession must be used on the thread that created it (the UI thread).");
            }
        }
    }
}
