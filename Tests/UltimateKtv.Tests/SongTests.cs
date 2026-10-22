using System.Data;
using Xunit;

namespace UltimateKtv.Tests
{
    public class SongTests
    {
        [Fact]
        public void Access_column_types_are_read_into_typed_fields()
        {
            var created = new DateTime(2025, 3, 4, 5, 6, 7);
            var song = Song.FromColumns(new Dictionary<string, object?>
            {
                ["Song_Id"] = "S0001",
                ["Song_SongName"] = "月亮代表我的心",
                ["Song_Singer"] = "鄧麗君",
                ["Song_Lang"] = "國語",
                ["Song_SingerType"] = (short)1,
                ["Song_WordCount"] = 7,
                ["Song_SongStroke"] = (byte)4,
                ["Song_Spell"] = "YLDBWDX",
                ["Song_PenStyle"] = "ㄩㄌㄉㄅㄨㄉㄒ",
                ["Song_CreatDate"] = created,
                ["Song_Path"] = @"D:\KTV\國語",
                ["Song_FileName"] = "S0001.mpg",
                ["Song_PlayCount"] = 12,
                ["Song_Volume"] = 85,
                ["Song_Track"] = (short)2,
                ["FilePath"] = @"D:\KTV\國語\S0001.mpg",
            });

            Assert.Equal("S0001", song.Id);
            Assert.Equal("月亮代表我的心", song.Name);
            Assert.Equal("鄧麗君", song.Singer);
            Assert.Equal("國語", song.Lang);
            Assert.Equal(1, song.SingerType);
            Assert.Equal(7, song.WordCount);
            Assert.Equal(4, song.SongStroke);
            Assert.Equal("YLDBWDX", song.Spell);
            Assert.Equal("ㄩㄌㄉㄅㄨㄉㄒ", song.PenStyle);
            Assert.Equal(created, song.CreatDate);
            Assert.Equal(@"D:\KTV\國語", song.Path);
            Assert.Equal("S0001.mpg", song.FileName);
            Assert.Equal(12, song.PlayCount);
            Assert.Equal(85, song.Volume);
            Assert.Equal(2, song.Track);
            Assert.Equal(@"D:\KTV\國語\S0001.mpg", song.FilePath);
        }

        [Fact]
        public void SQLite_column_types_are_read_into_typed_fields()
        {
            var song = Song.FromColumns(new Dictionary<string, object?>
            {
                ["Song_Id"] = "S0002",
                ["Song_WordCount"] = 3L,
                ["Song_PlayCount"] = 4L,
                ["Song_Volume"] = 72.6,
                ["Song_Track"] = "1",
                ["Song_CreatDate"] = "2024-12-31 23:59:00",
            });

            Assert.Equal(3, song.WordCount);
            Assert.Equal(4, song.PlayCount);
            Assert.Equal(73, song.Volume);
            Assert.Equal(1, song.Track);
            Assert.Equal(new DateTime(2024, 12, 31, 23, 59, 0), song.CreatDate);
        }

        [Fact]
        public void Missing_or_null_columns_use_defaults()
        {
            var song = Song.FromColumns(new Dictionary<string, object?>
            {
                ["Song_Id"] = "S0003",
                ["Song_SongName"] = null,
                ["Song_Volume"] = null,
                ["Song_CreatDate"] = null,
                ["Song_WordCount"] = "not a number",
            });

            Assert.Equal("S0003", song.Id);
            Assert.Equal("", song.Name);
            Assert.Equal("", song.Singer);
            Assert.Equal(-1, song.SingerType);
            Assert.Equal(0, song.WordCount);
            Assert.Equal(0, song.PlayCount);
            Assert.Null(song.Volume);
            Assert.Null(song.Track);
            Assert.Null(song.CreatDate);
            Assert.Equal("", song.FilePath);
        }

        [Fact]
        public void FromDataRow_treats_DBNull_as_null()
        {
            var table = new DataTable();
            table.Columns.Add("Song_Id", typeof(string));
            table.Columns.Add("Song_SongName", typeof(string));
            table.Columns.Add("Song_Volume", typeof(int));
            table.Columns.Add("Song_PlayCount", typeof(int));
            table.Rows.Add("S0004", "小幸運", DBNull.Value, 9);

            var song = Song.FromDataRow(table.Rows[0]);

            Assert.Equal("S0004", song.Id);
            Assert.Equal("小幸運", song.Name);
            Assert.Null(song.Volume);
            Assert.Equal(9, song.PlayCount);
            Assert.Null(song.GetColumnValue("Song_Volume"));
            Assert.Equal("小幸運", song.GetColumnValue("Song_SongName"));
        }

        [Fact]
        public void GetColumnValue_returns_current_values_for_runtime_fields()
        {
            var song = Song.FromColumns(new Dictionary<string, object?>
            {
                ["Song_Id"] = "S0005",
                ["Song_PlayCount"] = 1,
                ["Song_Volume"] = 80,
                ["Song_Track"] = 1,
                ["Song_SongType"] = "情歌",
            });

            song.PlayCount = 2;
            song.Volume = 60;
            song.Track = 2;
            song.FilePath = @"E:\Songs\S0005.mkv";

            Assert.Equal(2, song.GetColumnValue("Song_PlayCount"));
            Assert.Equal(60, song.GetColumnValue("Song_Volume"));
            Assert.Equal(2, song.GetColumnValue("Song_Track"));
            Assert.Equal(@"E:\Songs\S0005.mkv", song.GetColumnValue("FilePath"));
            Assert.Equal("情歌", song.GetColumnValue("Song_SongType"));
            Assert.Null(song.GetColumnValue("NoSuchColumn"));
        }
    }
}
