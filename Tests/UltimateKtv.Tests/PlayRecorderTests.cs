using System.Data;
using UltimateKtv.Playback;
using Xunit;

namespace UltimateKtv.Tests
{
    [Collection("SongLibrary")]
    public class PlayRecorderTests
    {
        private const string Session = "10/23 20:00";
        private int _historyChanged;
        private int _threshold = 35;
        private readonly List<(string Id, int Seconds)> _lengths = new();

        static PlayRecorderTests()
        {
            // 必須在第一次碰 Db 之前建立：Db 的靜態建構子依檔案是否存在決定用 SQLite 還是 Access
            string path = Path.Combine(AppContext.BaseDirectory, "CrazySong.db");
            if (!File.Exists(path)) System.Data.SQLite.SQLiteConnection.CreateFile(path);
        }

        public PlayRecorderTests()
        {
            AppLogger.IsEnabled = false;
            Assert.Equal(DbKind.SQLite, Db.Kind);
            foreach (var table in new[] { "ktv_Song", "ktv_Favorite", "ktv_User", "YoutubeSongList", SongLibrary.YoutubeFavoriteTable })
            {
                Db.ExecuteNonQuery($"DROP TABLE IF EXISTS {table}");
            }
            SongLibrary.EnsureYoutubeFavoriteTable();
            Db.ExecuteNonQuery("CREATE TABLE ktv_Song (Song_Id TEXT PRIMARY KEY, Song_PlayCount INTEGER, Song_Volume INTEGER, Song_Track INTEGER)");
            Db.ExecuteNonQuery("CREATE TABLE ktv_Favorite (User_Id TEXT, Song_Id TEXT)");
            Db.ExecuteNonQuery("CREATE TABLE ktv_User (User_Id TEXT PRIMARY KEY, User_Name TEXT)");
            Db.ExecuteNonQuery("CREATE TABLE YoutubeSongList (Id TEXT PRIMARY KEY, Url TEXT, PlayCount INTEGER, Length INTEGER)");
            Db.ExecuteNonQuery("INSERT INTO ktv_Song VALUES ('S1', 3, 60, 1)");
            Db.ExecuteNonQuery("INSERT INTO ktv_Song VALUES ('S2', 0, 60, 1)");
            Db.ExecuteNonQuery("INSERT INTO YoutubeSongList VALUES ('Y0001', 'aaaaaaaaaaa', NULL, 0)");
            // 前一次開機留下的今日點唱
            Db.ExecuteNonQuery($"INSERT INTO ktv_Favorite VALUES ('{SongLibrary.TodayListUserId}', 'OLD')");
            SongLibrary.AddYoutubeFavorite(SongLibrary.TodayListUserId, "oldoldoldol", "舊影片", 0);
        }

        private PlayRecorder Recorder()
        {
            var recorder = new PlayRecorder(Session, () => _threshold, () => 5, () => _historyChanged++, work => work());
            recorder.YoutubeLengthStored += (id, seconds) => _lengths.Add((id, seconds));
            return recorder;
        }

        private static Song Local(string id, int playCount = 0) => new() { Id = id, Name = id, PlayCount = playCount };
        private static Song Youtube(string id, string filePath = "") => new() { Id = id, Name = "影片", FilePath = filePath };

        private static List<string> YoutubeList(string userId) =>
            SongLibrary.GetYoutubeFavorites().Where(f => f.UserId == userId).Select(f => f.VideoId).OrderBy(v => v).ToList();

        private static List<string> TodayList() =>
            Db.GetDataTable("SELECT Song_Id FROM ktv_Favorite WHERE User_Id = ? ORDER BY Song_Id", SongLibrary.TodayListUserId)
              .Rows.Cast<DataRow>().Select(r => (string)r[0]).ToList();

        private static object? Scalar(string sql) => Db.GetDataTable(sql).Rows[0][0];

        [Fact]
        public void First_song_clears_todays_list_then_every_song_is_recorded()
        {
            var recorder = Recorder();

            recorder.SongOpened(Local("S1"), isYoutube: false, isRandom: false, TimeSpan.FromMinutes(4));
            recorder.SongOpened(Local("S2"), isYoutube: false, isRandom: false, TimeSpan.FromMinutes(4));

            Assert.Equal(new[] { "S1", "S2" }, TodayList());
            Assert.Equal(2L, Scalar($"SELECT COUNT(*) FROM ktv_Favorite WHERE User_Id = '{Session}'"));
            Assert.Equal(2, _historyChanged);
        }

        [Fact]
        public void Play_count_goes_up_once_after_the_threshold()
        {
            var recorder = Recorder();
            var song = Local("S1", playCount: 3);
            recorder.SongOpened(song, isYoutube: false, isRandom: false, TimeSpan.FromMinutes(4));

            recorder.ProgressChanged(35);
            Assert.Equal(3, song.PlayCount);
            recorder.ProgressChanged(36);
            recorder.ProgressChanged(80);

            Assert.Equal(4, song.PlayCount);
            Assert.Equal(4L, Scalar("SELECT Song_PlayCount FROM ktv_Song WHERE Song_Id = 'S1'"));
        }

        [Fact]
        public void The_threshold_is_read_each_time()
        {
            var recorder = Recorder();
            var song = Local("S1", playCount: 3);
            recorder.SongOpened(song, isYoutube: false, isRandom: false, TimeSpan.FromMinutes(4));

            _threshold = 60;
            recorder.ProgressChanged(50);
            Assert.Equal(3, song.PlayCount);
            recorder.ProgressChanged(61);
            Assert.Equal(4, song.PlayCount);
        }

        [Fact]
        public void Replaying_counts_again()
        {
            var recorder = Recorder();
            var song = Local("S1", playCount: 3);
            recorder.SongOpened(song, false, false, TimeSpan.FromMinutes(4));
            recorder.ProgressChanged(90);
            recorder.SongEnded();
            recorder.SongOpened(song, false, false, TimeSpan.FromMinutes(4));
            recorder.ProgressChanged(90);

            Assert.Equal(5, song.PlayCount);
        }

        [Fact]
        public void Random_songs_are_never_recorded()
        {
            var recorder = Recorder();
            var song = Local("S1", playCount: 3);

            recorder.SongOpened(song, isYoutube: false, isRandom: true, TimeSpan.FromMinutes(4));
            recorder.ProgressChanged(99);

            Assert.Equal(3, song.PlayCount);
            Assert.Equal(new[] { "OLD" }, TodayList());
            Assert.Equal(0, _historyChanged);
        }

        [Fact]
        public void A_random_song_first_does_not_use_up_the_todays_list_clearing()
        {
            var recorder = Recorder();
            recorder.SongOpened(Local("S2"), false, isRandom: true, TimeSpan.FromMinutes(4));
            recorder.SongOpened(Local("S1"), false, isRandom: false, TimeSpan.FromMinutes(4));

            Assert.Equal(new[] { "S1" }, TodayList());
        }

        [Fact]
        public void Progress_after_the_song_ended_is_ignored()
        {
            var recorder = Recorder();
            var song = Local("S1", playCount: 3);
            recorder.SongOpened(song, false, false, TimeSpan.FromMinutes(4));

            recorder.SongEnded();
            recorder.ProgressChanged(90);

            Assert.Equal(3, song.PlayCount);
        }

        [Fact]
        public void Youtube_songs_store_the_length_and_count_plays_in_the_history()
        {
            var recorder = Recorder();

            recorder.SongOpened(Youtube("Y0001"), isYoutube: true, isRandom: false, TimeSpan.FromSeconds(185));
            recorder.ProgressChanged(50);

            Assert.Equal(new[] { ("Y0001", 185) }, _lengths);
            Assert.Equal(185L, Scalar("SELECT Length FROM YoutubeSongList WHERE Id = 'Y0001'"));
            Assert.Equal(1L, Scalar("SELECT PlayCount FROM YoutubeSongList WHERE Id = 'Y0001'"));
        }

        [Fact]
        public void Youtube_songs_go_to_todays_list_and_session_by_video_id()
        {
            var recorder = Recorder();

            // 點唱紀錄 Id 由紀錄的 Url 取得影片 ID；網頁點播的 SongId 就是影片 ID
            recorder.SongOpened(Youtube("Y0001"), isYoutube: true, isRandom: false, TimeSpan.FromSeconds(185));
            recorder.SongOpened(Youtube("bbbbbbbbbbb"), isYoutube: true, isRandom: false, TimeSpan.Zero);

            Assert.Empty(TodayList()); // 第一首 (YouTube) 開播也會清空今日點唱
            Assert.Equal(new[] { "aaaaaaaaaaa", "bbbbbbbbbbb" }, YoutubeList(SongLibrary.TodayListUserId));
            Assert.Equal(new[] { "aaaaaaaaaaa", "bbbbbbbbbbb" }, YoutubeList(Session));
            Assert.Equal(185, SongLibrary.GetYoutubeFavorites().First(f => f.VideoId == "aaaaaaaaaaa").LengthSeconds);
            Assert.Equal(1L, Scalar($"SELECT COUNT(*) FROM ktv_User WHERE User_Id = '{Session}'"));
            Assert.Equal(2, _historyChanged);
        }

        [Fact]
        public void Youtube_video_id_can_come_from_the_file_name()
        {
            var recorder = Recorder();

            recorder.SongOpened(Youtube("Y0099", @"C:\YoutubeDownloads\歌名_ccccccccccc.mp4"), isYoutube: true, isRandom: false, TimeSpan.Zero);

            Assert.Equal(new[] { "ccccccccccc" }, YoutubeList(Session));
        }

        [Fact]
        public void Youtube_songs_without_a_video_id_and_random_youtube_songs_are_not_recorded()
        {
            var recorder = Recorder();

            recorder.SongOpened(Youtube("Y0099", @"C:\Imported\歌名.mp4"), isYoutube: true, isRandom: false, TimeSpan.Zero);
            recorder.SongOpened(Youtube("ddddddddddd"), isYoutube: true, isRandom: true, TimeSpan.Zero);

            Assert.Empty(YoutubeList(Session));
            Assert.Empty(YoutubeList(SongLibrary.TodayListUserId)); // 清空了，但沒有記錄
        }
    }
}
