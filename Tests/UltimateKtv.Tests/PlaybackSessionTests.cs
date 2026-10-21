using UltimateKtv.Playback;
using Xunit;

namespace UltimateKtv.Tests
{
    public class PlaybackSessionTests
    {
        private readonly FakePlaybackTarget _target = new();
        private readonly HashSet<string> _missingFiles = new(StringComparer.OrdinalIgnoreCase);
        private PlaybackSession? _sessionInstance;
        private readonly List<(QueuedSong Song, SongEndReason Reason)> _ended = new();
        private readonly List<QueuedSong> _started = new();
        private readonly List<QueuedSong> _opened = new();
        private int _idleCount;
        private int _queueChangedCount;

        public PlaybackSessionTests()
        {
            AppLogger.IsEnabled = false;
        }

        /// <summary>
        /// 第一次使用時才建立：session 綁定建立它的執行緒，而 xUnit 不保證建構子與測試方法同一條執行緒。
        /// </summary>
        private PlaybackSession _session
        {
            get
            {
                if (_sessionInstance != null) return _sessionInstance;
                var s = new PlaybackSession(_target, path => Task.FromResult(!_missingFiles.Contains(path)));
                s.SongStarting += (_, song) => _started.Add(song);
                s.SongOpened += (_, song) => _opened.Add(song);
                s.SongEnded += (_, e) => _ended.Add((e.Song, e.Reason));
                s.BecameIdle += (_, _) => _idleCount++;
                s.QueueChanged += (_, _) => _queueChangedCount++;
                return _sessionInstance = s;
            }
        }

        private static QueuedSong Song(string name, string singer = "歌手", bool random = false, bool youtube = false) => new()
        {
            SongId = name,
            SongName = name,
            SingerName = singer,
            FilePath = $@"D:\Songs\{name}.mpg",
            IsRandom = random,
            IsYoutube = youtube
        };

        private EnqueueResult Order(QueuedSong song, OrderSource source = OrderSource.User, bool insertFirst = false)
        {
            var task = _session.EnqueueAsync(song, source, insertFirst);
            Assert.True(task.IsCompleted);
            return task.Result;
        }

        /// <summary>點一首歌並讓它進入播放中。</summary>
        private QueuedSong PlayNow(QueuedSong song, OrderSource source = OrderSource.User)
        {
            Order(song, source);
            _target.RaiseOpened();
            Assert.Equal(PlaybackState.Playing, _session.State);
            return song;
        }

        // ---------- 點歌 ----------

        [Fact]
        public void Order_when_idle_starts_the_song()
        {
            var a = Song("A");

            Assert.Equal(EnqueueResult.Added, Order(a));

            Assert.Equal(PlaybackState.Opening, _session.State);
            Assert.Equal(a, _session.Current);
            Assert.Equal(a, _target.LastOpened);
            Assert.Empty(_session.Snapshot);
            Assert.Equal(new[] { a }, _started);
        }

        [Fact]
        public void Opened_moves_to_playing_and_raises_SongOpened()
        {
            var a = Song("A");
            Order(a);

            _target.RaiseOpened();

            Assert.Equal(PlaybackState.Playing, _session.State);
            Assert.Equal(new[] { a }, _opened);
        }

        [Fact]
        public void Order_while_playing_appends_without_opening()
        {
            PlayNow(Song("A"));
            var b = Song("B");
            var c = Song("C");

            Order(b);
            Order(c);

            Assert.Single(_target.Opened_);
            Assert.Equal(new[] { b, c }, _session.Snapshot);
        }

        [Fact]
        public void Order_while_opening_is_added_immediately_and_not_opened()
        {
            Order(Song("A"));
            var b = Song("B");

            Assert.Equal(EnqueueResult.Added, Order(b));

            Assert.Single(_target.Opened_);
            Assert.Equal(new[] { b }, _session.Snapshot);
        }

        [Fact]
        public void Insert_first_puts_song_at_head()
        {
            PlayNow(Song("A"));
            Order(Song("B"));
            var c = Song("C");

            Order(c, insertFirst: true);

            Assert.Equal(c, _session.Snapshot[0]);
        }

        [Fact]
        public void Duplicate_by_file_path_is_rejected()
        {
            PlayNow(Song("A"));
            Order(Song("B"));

            var sameFile = Song("B") with { SongName = "別名", FilePath = @"d:\songs\B.MPG" };

            Assert.Equal(EnqueueResult.Duplicate, Order(sameFile));
            Assert.Single(_session.Snapshot);
        }

        [Fact]
        public void Duplicate_by_name_and_singer_is_rejected()
        {
            PlayNow(Song("A"));
            Order(Song("B", "歌手甲"));

            var sameName = Song("b", "歌手甲") with { FilePath = @"D:\Other\b.mpg" };

            Assert.Equal(EnqueueResult.Duplicate, Order(sameName));
        }

        [Fact]
        public void Insert_first_of_a_queued_song_moves_it_to_first()
        {
            PlayNow(Song("A"));
            var b = Song("B");
            var c = Song("C");
            Order(b);
            Order(c);

            Assert.Equal(EnqueueResult.MovedToFirst, Order(Song("C"), insertFirst: true));

            Assert.Equal(new[] { c, b }, _session.Snapshot);
        }

        [Fact]
        public void Missing_file_is_rejected()
        {
            var a = Song("A");
            _missingFiles.Add(a.FilePath);

            Assert.Equal(EnqueueResult.FileMissing, Order(a));
            Assert.Equal(PlaybackState.Idle, _session.State);
            Assert.Empty(_target.Opened_);
        }

        [Fact]
        public void Empty_path_is_rejected()
        {
            Assert.Equal(EnqueueResult.InvalidPath, Order(Song("A") with { FilePath = "" }));
        }

        [Fact]
        public void Youtube_song_skips_file_check()
        {
            var yt = Song("YT", youtube: true);
            _missingFiles.Add(yt.FilePath);

            Assert.Equal(EnqueueResult.Added, Order(yt));
        }

        // ---------- 播完 / 失敗 ----------

        [Fact]
        public void Ended_plays_the_next_song()
        {
            var a = PlayNow(Song("A"));
            var b = Song("B");
            Order(b);

            _target.RaiseEnded();

            Assert.Equal((a, SongEndReason.Finished), _ended.Single());
            Assert.Equal(b, _session.Current);
            Assert.Equal(b, _target.LastOpened);
            Assert.Equal(PlaybackState.Opening, _session.State);
            Assert.Empty(_session.Snapshot);
        }

        [Fact]
        public void Ended_with_empty_list_becomes_idle()
        {
            PlayNow(Song("A"));

            _target.RaiseEnded();

            Assert.Equal(PlaybackState.Idle, _session.State);
            Assert.Null(_session.Current);
            Assert.Equal(1, _idleCount);
            Assert.Equal(1, _target.StopCount);
        }

        [Fact]
        public void Failed_ends_the_song_and_plays_the_next()
        {
            var a = Song("A");
            Order(a);
            var b = Song("B");
            Order(b);

            _target.RaiseFailed();

            Assert.Equal((a, SongEndReason.Failed), _ended.Single());
            Assert.Equal(b, _target.LastOpened);
        }

        [Fact]
        public void Open_throwing_is_treated_as_failure()
        {
            var a = Song("A");
            _target.ThrowOnOpen = true;

            Order(a);

            Assert.Equal((a, SongEndReason.Failed), _ended.Single());
            Assert.Equal(PlaybackState.Idle, _session.State);
            Assert.Equal(1, _idleCount);
        }

        [Fact]
        public void File_missing_at_play_time_is_dropped_and_next_is_tried()
        {
            PlayNow(Song("A"));
            var b = Song("B");
            var c = Song("C");
            Order(b);
            Order(c);
            _missingFiles.Add(b.FilePath);

            _target.RaiseEnded();

            Assert.Equal(c, _target.LastOpened);
            Assert.DoesNotContain(b, _target.Opened_);
            Assert.Empty(_session.Snapshot);
        }

        // ---------- 切歌 ----------

        [Fact]
        public void Skip_ends_current_and_plays_next()
        {
            var a = PlayNow(Song("A"));
            var b = Song("B");
            Order(b);

            _session.Skip();

            Assert.Equal((a, SongEndReason.Skipped), _ended.Single());
            Assert.Equal(b, _target.LastOpened);
        }

        [Fact]
        public void Skip_when_idle_does_nothing()
        {
            _session.Skip();

            Assert.Empty(_ended);
            Assert.Equal(0, _idleCount);
        }

        [Fact]
        public void Skip_while_opening_is_applied_once_opened()
        {
            var a = Song("A");
            Order(a);
            var b = Song("B");
            Order(b);

            _session.Skip();
            Assert.Empty(_ended);
            Assert.Single(_target.Opened_);

            _target.RaiseOpened();

            Assert.Empty(_opened);
            Assert.Equal((a, SongEndReason.Skipped), _ended.Single());
            Assert.Equal(b, _target.LastOpened);
        }

        // ---------- 隨機歌 ----------

        [Fact]
        public void User_order_cuts_off_a_playing_random_song()
        {
            var r = PlayNow(Song("R", random: true), OrderSource.Random);
            var a = Song("A");

            Assert.Equal(EnqueueResult.Added, Order(a));

            Assert.Equal((r, SongEndReason.CutOff), _ended.Single());
            Assert.Equal(a, _target.LastOpened);
            Assert.Equal(a, _session.Current);
        }

        [Fact]
        public void User_order_while_random_is_opening_cuts_it_off_once_opened()
        {
            var r = Song("R", random: true);
            Order(r, OrderSource.Random);
            var a = Song("A");

            Order(a);
            Assert.Empty(_ended);

            _target.RaiseOpened();

            Assert.Equal((r, SongEndReason.CutOff), _ended.Single());
            Assert.Equal(a, _target.LastOpened);
        }

        [Fact]
        public void Random_order_does_not_cut_off_a_random_song()
        {
            PlayNow(Song("R1", random: true), OrderSource.Random);

            Order(Song("R2", random: true), OrderSource.Random);

            Assert.Empty(_ended);
        }

        [Fact]
        public void User_order_does_not_cut_off_a_normal_song()
        {
            PlayNow(Song("A"));

            Order(Song("B"));

            Assert.Empty(_ended);
        }

        // ---------- 重唱 / 單曲循環 ----------

        [Fact]
        public void Replay_reopens_the_current_song_as_non_random()
        {
            var r = PlayNow(Song("R", random: true), OrderSource.Random);

            _session.Replay();

            Assert.Equal((r, SongEndReason.Replayed), _ended.Single());
            Assert.Equal(PlaybackState.Opening, _session.State);
            Assert.Equal(r.Id, _target.LastOpened!.Id);
            Assert.False(_session.Current!.IsRandom);
        }

        [Fact]
        public void Replay_when_idle_reopens_the_last_song()
        {
            var a = PlayNow(Song("A"));
            _target.RaiseEnded();

            _session.Replay();

            Assert.Equal(a.Id, _session.Current!.Id);
            Assert.Equal(a.Id, _target.LastOpened!.Id);
            Assert.Equal(2, _target.Opened_.Count);
        }

        [Fact]
        public void Loop_replays_instead_of_playing_next()
        {
            var a = PlayNow(Song("A"));
            Order(Song("B"));
            _session.LoopCurrent = true;

            _target.RaiseEnded();

            Assert.Equal((a, SongEndReason.Replayed), _ended.Single());
            Assert.Equal(a.Id, _target.LastOpened!.Id);
            Assert.Single(_session.Snapshot);
        }

        // ---------- 移動 / 刪除 ----------

        [Fact]
        public void Move_to_first_remove_and_move_down()
        {
            PlayNow(Song("A"));
            var b = Song("B");
            var c = Song("C");
            var d = Song("D");
            Order(b);
            Order(c);
            Order(d);

            Assert.True(_session.MoveToFirst(d.Id, OrderSource.User));
            Assert.Equal(new[] { d, b, c }, _session.Snapshot);

            Assert.True(_session.MoveDown(d.Id, OrderSource.Remote));
            Assert.Equal(new[] { b, d, c }, _session.Snapshot);

            Assert.True(_session.Remove(b.Id, OrderSource.User));
            Assert.Equal(new[] { d, c }, _session.Snapshot);

            Assert.False(_session.MoveDown(c.Id, OrderSource.User));
            Assert.False(_session.Remove(Guid.NewGuid(), OrderSource.User));
        }

        [Fact]
        public void Queue_changes_raise_QueueChanged()
        {
            PlayNow(Song("A"));
            int before = _queueChangedCount;
            var b = Song("B");

            Order(b);
            _session.Remove(b.Id, OrderSource.User);

            Assert.Equal(before + 2, _queueChangedCount);
        }

        [Fact]
        public void Remap_rewrites_queued_paths_and_keeps_ids()
        {
            PlayNow(Song("A"));
            var b = Song("B");
            Order(b);

            int count = _session.RemapFilePaths(p => p.Replace(@"D:\Songs", @"E:\KTV"));

            Assert.Equal(1, count);
            Assert.Equal(@"E:\KTV\B.mpg", _session.Snapshot[0].FilePath);
            Assert.Equal(b.Id, _session.Snapshot[0].Id);
        }

        [Fact]
        public void Removing_does_not_affect_the_playing_song()
        {
            var a = PlayNow(Song("A"));

            Assert.False(_session.Remove(a.Id, OrderSource.User));
            Assert.Equal(a, _session.Current);
        }

        // ---------- 執行緒 ----------

        [Fact]
        public async Task Calls_from_another_thread_throw()
        {
            var session = _session;

            var ex = await Task.Run(() => Record.Exception(() => session.Skip()));

            Assert.IsType<InvalidOperationException>(ex);
        }
    }
}
