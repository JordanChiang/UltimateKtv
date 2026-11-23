using System.Data;
using Xunit;

namespace UltimateKtv.Tests
{
    /// <summary>
    /// 在測試輸出資料夾建立 CrazySong.db (SQLite)，Db 偵測到它就改用 SQLite。
    /// Db 是靜態的，所以所有用到歌庫的測試都放在這個 collection、依序執行。
    /// </summary>
    [CollectionDefinition("SongLibrary", DisableParallelization = true)]
    public class SongLibraryCollection { }

    [Collection("SongLibrary")]
    public class SongLibraryTests
    {
        static SongLibraryTests()
        {
            // 必須在第一次碰 Db 之前建立：Db 的靜態建構子依檔案是否存在決定用 SQLite 還是 Access
            string path = Path.Combine(AppContext.BaseDirectory, "CrazySong.db");
            if (!File.Exists(path)) System.Data.SQLite.SQLiteConnection.CreateFile(path);
        }

        public SongLibraryTests()
        {
            AppLogger.IsEnabled = false;
            string path = Path.Combine(AppContext.BaseDirectory, Db.SQLiteFileName);
            Assert.Equal(path, Db.FilePath);
            Assert.Equal(DbKind.SQLite, Db.Kind);

            foreach (var table in new[] { "ktv_Song", "ktv_Favorite", "ktv_User", "YoutubeSongList", SongLibrary.YoutubeFavoriteTable })
            {
                Db.ExecuteNonQuery($"DROP TABLE IF EXISTS {table}");
            }
            Db.ExecuteNonQuery("CREATE TABLE ktv_Song (Song_Id TEXT PRIMARY KEY, Song_PlayCount INTEGER, Song_Volume INTEGER, Song_Track INTEGER)");
            Db.ExecuteNonQuery("CREATE TABLE ktv_Favorite (User_Id TEXT, Song_Id TEXT)");
            Db.ExecuteNonQuery("CREATE TABLE ktv_User (User_Id TEXT PRIMARY KEY, User_Name TEXT)");
            Db.ExecuteNonQuery("CREATE TABLE YoutubeSongList (Id TEXT PRIMARY KEY, Url TEXT, PlayCount INTEGER)");
            Db.ExecuteNonQuery("INSERT INTO ktv_Song VALUES ('S1', 3, 60, 1)");
            SongLibrary.EnsureYoutubeFavoriteTable();
        }

        private static object? Scalar(string sql, params object?[] args) => Db.GetDataTable(sql, args).Rows[0][0];

        private static List<string> FavoriteUsers() =>
            Db.GetDataTable("SELECT DISTINCT User_Id FROM ktv_Favorite ORDER BY User_Id").Rows.Cast<DataRow>()
              .Select(r => (string)r[0]).ToList();

        [Fact]
        public void Song_writes_update_the_row()
        {
            SongLibrary.SetPlayCount("S1", 4);
            SongLibrary.SetVolume("S1", 75);
            SongLibrary.SetMusicTrack("S1", 2);

            var row = Db.GetDataTable("SELECT * FROM ktv_Song WHERE Song_Id = 'S1'").Rows[0];
            Assert.Equal(4L, row["Song_PlayCount"]);
            Assert.Equal(75L, row["Song_Volume"]);
            Assert.Equal(2L, row["Song_Track"]);
        }

        [Fact]
        public void Youtube_play_count_starts_from_null_and_matches_id_or_url()
        {
            Db.ExecuteNonQuery("INSERT INTO YoutubeSongList VALUES ('Y0001', 'abc123', NULL)");

            Assert.Equal(1, SongLibrary.IncrementYoutubePlayCount("Y0001"));
            Assert.Equal(1, SongLibrary.IncrementYoutubePlayCount("abc123"));
            Assert.Equal(0, SongLibrary.IncrementYoutubePlayCount("missing"));

            Assert.Equal(2L, Scalar("SELECT PlayCount FROM YoutubeSongList WHERE Id = 'Y0001'"));
        }

        /// <summary>完整的 YoutubeSongList (與主程式建立、SongMgr 匯出的欄位相同；CreatDate 是 DATETIME，讀取時會解析成 DateTime)。</summary>
        private static void CreateFullYoutubeTable()
        {
            Db.ExecuteNonQuery("DROP TABLE IF EXISTS YoutubeSongList");
            Db.ExecuteNonQuery(@"CREATE TABLE YoutubeSongList (Id TEXT PRIMARY KEY, Name TEXT, CreatDate DATETIME, Url TEXT, Singer TEXT,
                Lang TEXT, PlayCount INTEGER, Length INTEGER, FileName TEXT, FileSize INTEGER, Track INTEGER, Volume INTEGER, WordCount INTEGER)");
        }

        [Fact]
        public void Table_list_contains_the_tables()
        {
            var tables = Db.GetTableList();
            Assert.Contains("ktv_Song", tables);
            Assert.Contains("YoutubeSongList", tables);
        }

        [Fact]
        public void Recorded_youtube_date_is_read_back_as_datetime()
        {
            CreateFullYoutubeTable();
            Db.ExecuteNonQuery("INSERT INTO YoutubeSongList (Id, CreatDate) VALUES ('Y0001', '2026-08-16 00:45:08')");
            SongLibrary.RecordYoutubeSong("aaaaaaaaaaa", "新歌", @"D:\yt\新歌_aaaaaaaaaaa.mp4", "", 30, AudioChannel.NoVocal);

            // SELECT * 讀整張表 (YouTube 紀錄清單的讀法)：任何一筆日期解析失敗都會讓整份清單讀不出來
            var dates = Db.GetDataTable("SELECT * FROM YoutubeSongList ORDER BY CreatDate DESC").Rows.Cast<DataRow>()
                .Select(r => (string)r["Id"]).ToList();
            Assert.Equal(new[] { "Y0002", "Y0001" }, dates);
            Assert.Equal(1L, Scalar("SELECT CreatDate LIKE '____-__-__ __:__:__' FROM YoutubeSongList WHERE Id = 'Y0002'"));
        }

        [Fact]
        public void Repair_youtube_history_converts_slash_dates()
        {
            CreateFullYoutubeTable();
            Db.ExecuteNonQuery("INSERT INTO YoutubeSongList (Id, CreatDate) VALUES ('Y0001', '2026/11/01 11:01:25')");
            Db.ExecuteNonQuery("INSERT INTO YoutubeSongList (Id, CreatDate) VALUES ('Y0002', '2026-08-16 00:45:08')");

            Assert.Equal(1, SongLibrary.RepairYoutubeHistory());
            Assert.Equal(0, SongLibrary.RepairYoutubeHistory());

            Assert.Equal(new DateTime(2026, 11, 1, 11, 1, 25), Scalar("SELECT CreatDate FROM YoutubeSongList WHERE Id = 'Y0001'"));
        }

        [Fact]
        public void Record_youtube_song_numbers_new_rows_and_reuses_the_same_video()
        {
            CreateFullYoutubeTable();

            Assert.Equal("Y0001", SongLibrary.RecordYoutubeSong("aaaaaaaaaaa", "第一首", @"D:\yt\第一首_aaaaaaaaaaa.mp4", "", 30, AudioChannel.NoVocal));
            Assert.Equal("Y0002", SongLibrary.RecordYoutubeSong("bbbbbbbbbbb", "第二首", @"D:\yt\第二首_bbbbbbbbbbb.mp4", "", 30, AudioChannel.NoVocal));
            Assert.Equal("Y0001", SongLibrary.RecordYoutubeSong("aaaaaaaaaaa", "第一首", @"D:\other\x.mp4", "", 30, AudioChannel.NoVocal));
            Assert.Equal(2L, Scalar("SELECT COUNT(*) FROM YoutubeSongList"));
        }

        [Fact]
        public void Record_youtube_song_fills_the_missing_video_id_of_an_imported_file()
        {
            CreateFullYoutubeTable();
            Assert.Equal("Y0001", SongLibrary.RecordYoutubeSong("", "匯入", @"D:\yt\匯入.mp4", "", 30, AudioChannel.NoVocal));

            Assert.Equal("Y0001", SongLibrary.RecordYoutubeSong("ccccccccccc", "匯入", @"D:\yt\匯入.mp4", "", 30, AudioChannel.NoVocal));
            Assert.Equal("ccccccccccc", Scalar("SELECT Url FROM YoutubeSongList WHERE Id = 'Y0001'"));
        }

        [Fact]
        public void Youtube_length_is_written_only_once()
        {
            CreateFullYoutubeTable();
            SongLibrary.RecordYoutubeSong("aaaaaaaaaaa", "A", @"D:\yt\A_aaaaaaaaaaa.mp4", "", 30, AudioChannel.NoVocal);

            Assert.True(SongLibrary.UpdateYoutubeSongLength("aaaaaaaaaaa", 200));
            Assert.False(SongLibrary.UpdateYoutubeSongLength("Y0001", 300));
            Assert.Equal(200L, Scalar("SELECT Length FROM YoutubeSongList WHERE Id = 'Y0001'"));
        }

        [Fact]
        public void Youtube_track_is_recorded_and_read_by_record_id_or_video_id()
        {
            CreateFullYoutubeTable();
            SongLibrary.RecordYoutubeSong("aaaaaaaaaaa", "A", @"D:\yt\A_aaaaaaaaaaa.mp4", "", 30, AudioChannel.Stereo);

            Assert.Equal(AudioChannel.Stereo, SongLibrary.GetYoutubeTrack("Y0001"));
            Db.ExecuteNonQuery("UPDATE YoutubeSongList SET Track = 2 WHERE Id = 'Y0001'"); // SongMgr 修改
            Assert.Equal(AudioChannel.Right, SongLibrary.GetYoutubeTrack("aaaaaaaaaaa"));
            Assert.Null(SongLibrary.GetYoutubeTrack("bbbbbbbbbbb"));

            Db.ExecuteNonQuery("UPDATE YoutubeSongList SET Track = NULL WHERE Id = 'Y0001'");
            Assert.Null(SongLibrary.GetYoutubeTrack("Y0001"));
        }

        [Fact]
        public void Repair_youtube_history_fixes_legacy_rows_once()
        {
            CreateFullYoutubeTable();
            Db.ExecuteNonQuery("INSERT INTO YoutubeSongList (Id, Name, Url, Lang, FileName) VALUES ('Y0001', '歌名_aaaaaaaaaaa', '', '05:11', '歌名_aaaaaaaaaaa.mp4')");
            Db.ExecuteNonQuery("INSERT INTO YoutubeSongList (Id, Name, Url, Lang, FileName) VALUES ('Y0002', '正常', 'bbbbbbbbbbb', '國語', '正常_bbbbbbbbbbb.mp4')");

            Assert.Equal(3, SongLibrary.RepairYoutubeHistory());
            Assert.Equal(0, SongLibrary.RepairYoutubeHistory());

            var row = Db.GetDataTable("SELECT Name, Url, Lang FROM YoutubeSongList WHERE Id = 'Y0001'").Rows[0];
            Assert.Equal("歌名", row["Name"]);
            Assert.Equal("aaaaaaaaaaa", row["Url"]);
            Assert.Equal("", row["Lang"]);
            Assert.Equal("國語", Scalar("SELECT Lang FROM YoutubeSongList WHERE Id = 'Y0002'"));
        }

        [Fact]
        public void Add_favorite_is_idempotent()
        {
            Assert.True(SongLibrary.AddFavorite("U1", "S1"));
            Assert.False(SongLibrary.AddFavorite("U1", "S1"));

            Assert.True(SongLibrary.IsFavorite("U1", "S1"));
            Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM ktv_Favorite"));
        }

        [Fact]
        public void Youtube_favorite_table_is_created_once()
        {
            SongLibrary.EnsureYoutubeFavoriteTable();
            Assert.True(SongLibrary.AddYoutubeFavorite("U1", "dQw4w9WgXcQ", "歌名", 212));
            SongLibrary.EnsureYoutubeFavoriteTable();

            Assert.Single(SongLibrary.GetYoutubeFavorites());
        }

        [Fact]
        public void Add_youtube_favorite_is_idempotent_per_user()
        {
            SongLibrary.EnsureYoutubeFavoriteTable();
            Assert.True(SongLibrary.AddYoutubeFavorite("U1", "dQw4w9WgXcQ", "歌名", 212));
            Assert.False(SongLibrary.AddYoutubeFavorite("U1", "dQw4w9WgXcQ", "另一個歌名", 0));
            Assert.True(SongLibrary.AddYoutubeFavorite("U2", "dQw4w9WgXcQ", "歌名", 212));

            Assert.True(SongLibrary.IsYoutubeFavorite("U1", "dQw4w9WgXcQ"));
            Assert.False(SongLibrary.IsYoutubeFavorite("U3", "dQw4w9WgXcQ"));
            var favorites = SongLibrary.GetYoutubeFavorites();
            Assert.Equal(new[] { "U1", "U2" }, favorites.Select(f => f.UserId).OrderBy(u => u));
            var first = favorites.Single(f => f.UserId == "U1");
            Assert.Equal("歌名", first.Name);
            Assert.Equal(212, first.LengthSeconds);
            Assert.NotNull(first.CreatDate);
        }

        [Theory]
        [InlineData("Y0017")]
        [InlineData("")]
        [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
        public void Add_youtube_favorite_rejects_non_video_ids(string id)
        {
            SongLibrary.EnsureYoutubeFavoriteTable();
            Assert.False(SongLibrary.AddYoutubeFavorite("U1", id, "歌名", 0));
            Assert.Empty(SongLibrary.GetYoutubeFavorites());
        }

        [Fact]
        public void Add_youtube_favorite_truncates_long_names()
        {
            SongLibrary.EnsureYoutubeFavoriteTable();
            Assert.True(SongLibrary.AddYoutubeFavorite("U1", "dQw4w9WgXcQ", new string('歌', 300), -5));

            var favorite = SongLibrary.GetYoutubeFavorites().Single();
            Assert.Equal(255, favorite.Name.Length);
            Assert.Equal(0, favorite.LengthSeconds);
        }

        [Fact]
        public void Record_play_adds_to_today_list_and_session()
        {
            SongLibrary.RecordPlay("S1", "10/21 20:00", maxSessions: 5);
            SongLibrary.RecordPlay("S1", "10/21 20:00", maxSessions: 5);

            Assert.True(SongLibrary.IsFavorite(SongLibrary.TodayListUserId, "S1"));
            Assert.True(SongLibrary.IsFavorite("10/21 20:00", "S1"));
            Assert.Equal(2L, Scalar("SELECT COUNT(*) FROM ktv_Favorite"));
            Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM ktv_User WHERE User_Id = '10/21 20:00'"));
        }

        [Fact]
        public void Record_play_keeps_at_most_max_sessions()
        {
            SongLibrary.RecordPlay("S1", "10/19 20:00", maxSessions: 2);
            SongLibrary.RecordPlay("S1", "10/20 20:00", maxSessions: 2);

            SongLibrary.RecordPlay("S1", "10/21 20:00", maxSessions: 2);
            SongLibrary.RecordPlay("S2", "10/21 20:00", maxSessions: 2);

            Assert.Equal(new[] { SongLibrary.TodayListUserId, "10/20 20:00", "10/21 20:00" }, FavoriteUsers());
            Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM ktv_User WHERE User_Id = '10/19 20:00'"));
        }

        [Fact]
        public void A_session_with_only_youtube_plays_counts_toward_the_limit()
        {
            SongLibrary.RecordYoutubePlay("aaaaaaaaaaa", null, "影片", 0, "10/19 20:00", maxSessions: 2);
            SongLibrary.RecordPlay("S1", "10/20 20:00", maxSessions: 2);

            SongLibrary.RecordYoutubePlay("bbbbbbbbbbb", null, "影片", 0, "10/21 20:00", maxSessions: 2);

            // 最舊的那次只有 YouTube 歌，也要被刪掉 (含 ktv_User)
            Assert.DoesNotContain(SongLibrary.GetYoutubeFavorites(), f => f.UserId == "10/19 20:00");
            Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM ktv_User WHERE User_Id = '10/19 20:00'"));
            Assert.Equal(new[] { SongLibrary.TodayListUserId, "10/20 20:00" }, FavoriteUsers());
            Assert.Contains(SongLibrary.GetYoutubeFavorites(), f => f.UserId == "10/21 20:00" && f.VideoId == "bbbbbbbbbbb");
        }

        [Fact]
        public void Deleting_the_oldest_session_also_removes_its_youtube_plays()
        {
            SongLibrary.RecordPlay("S1", "10/19 20:00", maxSessions: 1);
            SongLibrary.RecordYoutubePlay("aaaaaaaaaaa", null, "影片", 0, "10/19 20:00", maxSessions: 1);

            SongLibrary.RecordPlay("S1", "10/20 20:00", maxSessions: 1);

            Assert.Equal(new[] { SongLibrary.TodayListUserId, "10/20 20:00" }, FavoriteUsers());
            Assert.Equal(new[] { SongLibrary.TodayListUserId }, SongLibrary.GetYoutubeFavorites().Select(f => f.UserId));
        }

        [Fact]
        public void Clear_today_list_also_clears_youtube_plays()
        {
            SongLibrary.RecordYoutubePlay("aaaaaaaaaaa", null, "影片", 0, "10/21 20:00", maxSessions: 5);

            SongLibrary.ClearTodayList();

            Assert.Equal(new[] { "10/21 20:00" }, SongLibrary.GetYoutubeFavorites().Select(f => f.UserId));
        }

        [Fact]
        public void Clear_today_list_keeps_session_history()
        {
            SongLibrary.RecordPlay("S1", "10/21 20:00", maxSessions: 5);

            SongLibrary.ClearTodayList();

            Assert.Equal(new[] { "10/21 20:00" }, FavoriteUsers());
        }

        [Theory]
        [InlineData("10/21 20:00", true)]
        [InlineData("****", false)]
        [InlineData("1/21 20:00", false)]
        [InlineData("^CB0001", false)]
        public void Session_user_id_format(string userId, bool expected)
        {
            Assert.Equal(expected, SongLibrary.IsSessionUserId(userId));
        }
    }
}
