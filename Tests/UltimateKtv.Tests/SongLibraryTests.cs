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

            foreach (var table in new[] { "ktv_Song", "ktv_Favorite", "ktv_User", "YoutubeSongList" })
            {
                Db.ExecuteNonQuery($"DROP TABLE IF EXISTS {table}");
            }
            Db.ExecuteNonQuery("CREATE TABLE ktv_Song (Song_Id TEXT PRIMARY KEY, Song_PlayCount INTEGER, Song_Volume INTEGER, Song_Track INTEGER)");
            Db.ExecuteNonQuery("CREATE TABLE ktv_Favorite (User_Id TEXT, Song_Id TEXT)");
            Db.ExecuteNonQuery("CREATE TABLE ktv_User (User_Id TEXT PRIMARY KEY, User_Name TEXT)");
            Db.ExecuteNonQuery("CREATE TABLE YoutubeSongList (Id TEXT PRIMARY KEY, Url TEXT, PlayCount INTEGER)");
            Db.ExecuteNonQuery("INSERT INTO ktv_Song VALUES ('S1', 3, 60, 1)");
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

        /// <summary>完整的 YoutubeSongList (與主程式建立的欄位相同)。</summary>
        private static void CreateFullYoutubeTable()
        {
            Db.ExecuteNonQuery("DROP TABLE IF EXISTS YoutubeSongList");
            Db.ExecuteNonQuery(@"CREATE TABLE YoutubeSongList (Id TEXT PRIMARY KEY, Name TEXT, CreatDate TEXT, Url TEXT, Singer TEXT,
                Lang TEXT, PlayCount INTEGER, Length INTEGER, FileName TEXT, FileSize INTEGER, Track INTEGER, Volume INTEGER, WordCount INTEGER)");
        }

        [Fact]
        public void Record_youtube_song_numbers_new_rows_and_reuses_the_same_video()
        {
            CreateFullYoutubeTable();

            Assert.Equal("Y0001", SongLibrary.RecordYoutubeSong("aaaaaaaaaaa", "第一首", @"D:\yt\第一首_aaaaaaaaaaa.mp4", "", 30));
            Assert.Equal("Y0002", SongLibrary.RecordYoutubeSong("bbbbbbbbbbb", "第二首", @"D:\yt\第二首_bbbbbbbbbbb.mp4", "", 30));
            Assert.Equal("Y0001", SongLibrary.RecordYoutubeSong("aaaaaaaaaaa", "第一首", @"D:\other\x.mp4", "", 30));
            Assert.Equal(2L, Scalar("SELECT COUNT(*) FROM YoutubeSongList"));
        }

        [Fact]
        public void Record_youtube_song_fills_the_missing_video_id_of_an_imported_file()
        {
            CreateFullYoutubeTable();
            Assert.Equal("Y0001", SongLibrary.RecordYoutubeSong("", "匯入", @"D:\yt\匯入.mp4", "", 30));

            Assert.Equal("Y0001", SongLibrary.RecordYoutubeSong("ccccccccccc", "匯入", @"D:\yt\匯入.mp4", "", 30));
            Assert.Equal("ccccccccccc", Scalar("SELECT Url FROM YoutubeSongList WHERE Id = 'Y0001'"));
        }

        [Fact]
        public void Youtube_length_is_written_only_once()
        {
            CreateFullYoutubeTable();
            SongLibrary.RecordYoutubeSong("aaaaaaaaaaa", "A", @"D:\yt\A_aaaaaaaaaaa.mp4", "", 30);

            Assert.True(SongLibrary.UpdateYoutubeSongLength("aaaaaaaaaaa", 200));
            Assert.False(SongLibrary.UpdateYoutubeSongLength("Y0001", 300));
            Assert.Equal(200L, Scalar("SELECT Length FROM YoutubeSongList WHERE Id = 'Y0001'"));
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
