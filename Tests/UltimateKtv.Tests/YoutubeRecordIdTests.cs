using Xunit;

namespace UltimateKtv.Tests
{
    public class YoutubeRecordIdTests
    {
        [Theory]
        [InlineData(1, "Y0001")]
        [InlineData(9999, "Y9999")]
        [InlineData(10000, "YA000")]
        [InlineData(10035, "YA00Z")]
        [InlineData(10036, "YA010")]
        [InlineData(10000 + 46656, "YB000")]
        [InlineData(YoutubeRecordId.Count, "YZZZZ")]
        public void Number_and_id_round_trip(int number, string id)
        {
            Assert.Equal(id, YoutubeRecordId.FromNumber(number));
            Assert.Equal(number, YoutubeRecordId.NumberOf(id));
            Assert.True(YoutubeRecordId.IsValid(id));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(YoutubeRecordId.Count + 1)]
        public void Out_of_range_number_has_no_id(int number)
        {
            Assert.Null(YoutubeRecordId.FromNumber(number));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Y0000")]
        [InlineData("Y10000")]
        [InlineData("Y001")]
        [InlineData("X0001")]
        [InlineData("Y00A1")]
        [InlineData("Y-001")]
        [InlineData("YA0_0")]
        [InlineData("Yaaaaaaaaaa")] // Y 開頭的影片 ID
        public void Other_values_are_not_record_ids(string? id)
        {
            Assert.Equal(0, YoutubeRecordId.NumberOf(id));
            Assert.False(YoutubeRecordId.IsValid(id));
        }

        [Fact]
        public void Lowercase_id_parses_like_uppercase()
        {
            Assert.Equal(1, YoutubeRecordId.NumberOf("y0001"));
            Assert.Equal(10035, YoutubeRecordId.NumberOf("ya00z"));
        }

        [Fact]
        public void Ids_sort_in_number_order()
        {
            var ids = new[] { 1, 9998, 9999, 10000, 10035, 10036, 56655, 56656, YoutubeRecordId.Count }
                .Select(n => YoutubeRecordId.FromNumber(n)!).ToList();
            Assert.Equal(ids, ids.OrderBy(s => s, StringComparer.Ordinal).ToList());
        }

        [Fact]
        public void Next_fills_the_smallest_gap()
        {
            Assert.Equal("Y0001", YoutubeRecordId.Next(Array.Empty<string>()));
            Assert.Equal("Y0003", YoutubeRecordId.Next(new[] { "Y0001", "Y0002", "Y0005" }));
            Assert.Equal("YA000", YoutubeRecordId.Next(Enumerable.Range(1, 9999).Select(n => $"Y{n:D4}")));
        }

        [Fact]
        public void Next_treats_lowercase_ids_as_used_and_ignores_foreign_ids()
        {
            // Access 的主鍵不分大小寫：y0001 已占用 Y0001
            Assert.Equal("Y0002", YoutubeRecordId.Next(new[] { "y0001", "Z9999", "", "abc" }));
        }

        [Fact]
        public void Next_returns_null_when_every_id_is_used()
        {
            Assert.Null(YoutubeRecordId.Next(Enumerable.Range(1, YoutubeRecordId.Count).Select(n => YoutubeRecordId.FromNumber(n)!)));
        }
    }
}
