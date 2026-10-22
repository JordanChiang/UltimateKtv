using Xunit;

namespace UltimateKtv.Tests
{
    public class SongQueryTests
    {
        private static readonly DateTime Today = new(2026, 10, 22);

        private static SongQueryOptions Options(int sortMethod = 3, int newSongDays = 30) => new(sortMethod, Today, newSongDays);

        private static Song S(string id, string name = "", string singer = "", string lang = "國語", int singerType = 0,
            int words = 0, int plays = 0, DateTime? created = null, string spell = "", string penStyle = "") => new()
            {
                Id = id,
                Name = name == "" ? id : name,
                Singer = singer,
                Lang = lang,
                SingerType = singerType,
                WordCount = words,
                PlayCount = plays,
                CreatDate = created,
                Spell = spell,
                PenStyle = penStyle,
            };

        private static string[] Ids(IEnumerable<Song> songs) => songs.Select(s => s.Id).ToArray();

        // ---- 歌曲排序 ----

        [Fact]
        public void Sort_by_play_count_puts_most_played_first()
        {
            var songs = new[] { S("a", plays: 1), S("b", plays: 9), S("c", plays: 5) };
            Assert.Equal(new[] { "b", "c", "a" }, Ids(SongQuery.Sort(songs, Options(sortMethod: 1))));
        }

        [Fact]
        public void Sort_by_date_added_puts_newest_first_and_undated_last()
        {
            var songs = new[] { S("a", created: Today.AddDays(-5)), S("b"), S("c", created: Today) };
            Assert.Equal(new[] { "c", "a", "b" }, Ids(SongQuery.Sort(songs, Options(sortMethod: 2))));
        }

        [Fact]
        public void Sort_by_word_count_is_the_default()
        {
            var songs = new[] { S("a", words: 5), S("b", words: 2), S("c", words: 3) };
            Assert.Equal(new[] { "b", "c", "a" }, Ids(SongQuery.Sort(songs, Options(sortMethod: 0))));
        }

        [Fact]
        public void Ties_put_english_names_first_then_chinese_by_stroke()
        {
            var songs = new[] { S("3", name: "三"), S("en", name: "Hello"), S("1", name: "一"), S("2", name: "人") };
            Assert.Equal(new[] { "en", "1", "2", "3" }, Ids(SongQuery.Sort(songs, Options())));
        }

        [Fact]
        public void Library_not_loaded_returns_empty_lists()
        {
            Assert.Empty(SongQuery.Sort(null, Options()));
            Assert.Empty(SongQuery.NewSongs(null, "國語", Options()));
            Assert.Empty(SongQuery.Ranking(null, "國語-單曲"));
            Assert.Empty(SongQuery.ByLanguage(null, new[] { "國語" }, false, null, null, Options()));
            Assert.Empty(SongQuery.QuickMatch(null, SongTextField.Name, "a", true, Options()));
            Assert.Empty(SongQuery.BySinger(null, "甲", Options()));
            Assert.Empty(SongQuery.Search(null, "甲", null, Options()));
        }

        // ---- 新歌 ----

        [Fact]
        public void New_songs_are_those_added_within_the_new_song_days()
        {
            var songs = new[]
            {
                S("edge", created: Today.AddDays(-30)),
                S("old", created: Today.AddDays(-31)),
                S("undated"),
                S("fresh", created: Today),
            };
            Assert.Equal(new[] { "edge", "fresh" }, Ids(SongQuery.NewSongs(songs, null, Options(newSongDays: 30))).Order().ToArray());
        }

        [Fact]
        public void New_songs_other_means_neither_mandarin_nor_taiwanese()
        {
            var songs = new[]
            {
                S("m", lang: "國語", created: Today),
                S("t", lang: "台語", created: Today),
                S("e", lang: "英語", created: Today),
                S("k", lang: "韓語", created: Today),
            };
            Assert.Equal(new[] { "e", "k" }, Ids(SongQuery.NewSongs(songs, "其它", Options())).Order().ToArray());
            Assert.Equal(new[] { "t" }, Ids(SongQuery.NewSongs(songs, "台語", Options())));
            Assert.Equal(4, SongQuery.NewSongs(songs, "", Options()).Count);
        }

        // ---- 排行 ----

        private static readonly Song[] RankingSongs =
        {
            S("solo", lang: "國語", singerType: 1, plays: 5),
            S("duet", lang: "國語", singerType: 3, plays: 9),
            S("never", lang: "國語", singerType: 1, plays: 0),
            S("tw", lang: "台語", singerType: 0, plays: 7),
            S("en", lang: "英語", singerType: 3, plays: 2),
        };

        [Fact]
        public void Ranking_lists_played_songs_by_play_count()
        {
            Assert.Equal(new[] { "duet", "tw", "solo", "en" }, Ids(SongQuery.Ranking(RankingSongs, null)));
        }

        [Fact]
        public void Ranking_can_be_limited_to_solo_or_duet()
        {
            Assert.Equal(new[] { "solo" }, Ids(SongQuery.Ranking(RankingSongs, "國語-單曲")));
            Assert.Equal(new[] { "duet" }, Ids(SongQuery.Ranking(RankingSongs, "國語-合唱")));
        }

        [Fact]
        public void Ranking_without_solo_or_duet_includes_both()
        {
            Assert.Equal(new[] { "duet", "solo" }, Ids(SongQuery.Ranking(RankingSongs, "國語")));
        }

        [Fact]
        public void Ranking_other_includes_duets_outside_mandarin_and_taiwanese()
        {
            Assert.Equal(new[] { "en" }, Ids(SongQuery.Ranking(RankingSongs, "其它")));
        }

        // ---- 語系篩選 ----

        [Fact]
        public void Language_filter_matches_any_selected_language()
        {
            var songs = new[] { S("m", lang: "國語"), S("e", lang: "英語"), S("j", lang: "日語") };
            Assert.Equal(new[] { "e", "m" }, Ids(SongQuery.ByLanguage(songs, new[] { "國語", "英語" }, false, null, null, Options())).Order().ToArray());
            Assert.Equal(3, SongQuery.ByLanguage(songs, Array.Empty<string>(), false, null, null, Options()).Count);
        }

        [Fact]
        public void Language_filter_other_means_outside_the_six_standard_languages()
        {
            var songs = new[] { S("m", lang: "國語"), S("c", lang: "兒歌"), S("k", lang: "韓語"), S("h", lang: "客語") };
            Assert.Equal(new[] { "h", "k" }, Ids(SongQuery.ByLanguage(songs, new[] { "其它" }, false, null, null, Options())).Order().ToArray());
        }

        [Fact]
        public void Language_filter_shows_duets_only_when_asked()
        {
            var songs = new[] { S("solo", singerType: 1), S("duet", singerType: 3) };
            Assert.Equal(new[] { "solo" }, Ids(SongQuery.ByLanguage(songs, new[] { "國語" }, false, null, null, Options())));
            Assert.Equal(new[] { "duet" }, Ids(SongQuery.ByLanguage(songs, new[] { "國語" }, true, null, null, Options())));
        }

        [Fact]
        public void Language_filter_by_singer_type_and_other_singer_type()
        {
            var songs = new[] { S("male", singerType: 0), S("female", singerType: 1), S("band", singerType: 2), S("x", singerType: 7), S("unset", singerType: -1) };
            Assert.Equal(new[] { "female" }, Ids(SongQuery.ByLanguage(songs, new[] { "國語" }, false, "1", null, Options())));
            Assert.Equal(new[] { "unset", "x" }, Ids(SongQuery.ByLanguage(songs, new[] { "國語" }, false, "99", null, Options())).Order().ToArray());
        }

        [Fact]
        public void Language_filter_by_word_count_or_range()
        {
            var songs = new[] { S("w2", words: 2), S("w3", words: 3), S("w6", words: 6), S("w9", words: 9) };
            Assert.Equal(new[] { "w3" }, Ids(SongQuery.ByLanguage(songs, new[] { "國語" }, false, null, "3", Options())));
            Assert.Equal(new[] { "w6", "w9" }, Ids(SongQuery.ByLanguage(songs, new[] { "國語" }, false, null, "6-99", Options())));
            Assert.Equal(4, SongQuery.ByLanguage(songs, new[] { "國語" }, false, null, "8+", Options()).Count);
        }

        // ---- 快速搜尋 ----

        [Fact]
        public void Quick_match_is_prefix_by_default_and_ignores_case()
        {
            var songs = new[] { S("1", spell: "YLDB"), S("2", spell: "AYL"), S("3", spell: "yl") };
            Assert.Equal(new[] { "1", "3" }, Ids(SongQuery.QuickMatch(songs, SongTextField.Spell, "YL", false, Options())).Order().ToArray());
        }

        [Fact]
        public void Quick_match_can_match_anywhere_in_the_field()
        {
            var songs = new[] { S("1", name: "Let It Go"), S("2", name: "Golden"), S("3", name: "Hello") };
            Assert.Equal(new[] { "1", "2" }, Ids(SongQuery.QuickMatch(songs, SongTextField.Name, "go", true, Options())).Order().ToArray());
        }

        [Fact]
        public void Quick_match_with_empty_key_finds_nothing()
        {
            Assert.Empty(SongQuery.QuickMatch(new[] { S("1") }, SongTextField.Id, "", true, Options()));
        }

        // ---- 搜尋框 ----

        [Fact]
        public void Search_matches_song_name_or_singer_anywhere_ignoring_case()
        {
            var songs = new[] { S("byName", name: "林中小屋"), S("bySinger", name: "Song", singer: "林志炫"), S("en", name: "Hello", singer: "adele"), S("none", name: "雨", singer: "王傑") };
            Assert.Equal(new[] { "byName", "bySinger" }, Ids(SongQuery.Search(songs, "林", null, Options())).Order().ToArray());
            Assert.Equal(new[] { "en" }, Ids(SongQuery.Search(songs, "ADE", "", Options())));
        }

        [Fact]
        public void Search_ignores_full_and_half_width()
        {
            var songs = new[] { S("full", name: "Boom！", singer: "袁小迪＆曾心梅"), S("half", name: "Ｈｅｌｌｏ") };
            Assert.Equal(new[] { "full" }, Ids(SongQuery.Search(songs, "!", null, Options())));
            Assert.Equal(new[] { "full" }, Ids(SongQuery.Search(songs, "&", null, Options())));
            Assert.Equal(new[] { "half" }, Ids(SongQuery.Search(songs, "hello", null, Options())));
        }

        [Fact]
        public void Search_without_text_lists_the_whole_language()
        {
            var songs = new[] { S("m", lang: "國語"), S("t", lang: "台語") };
            Assert.Equal(new[] { "t" }, Ids(SongQuery.Search(songs, "", "台語", Options())));
            Assert.Equal(2, SongQuery.Search(songs, null, null, Options()).Count);
        }

        // ---- 分頁 ----

        [Fact]
        public void Page_takes_the_requested_slice_and_counts_pages()
        {
            var items = Enumerable.Range(1, 7).ToList();
            var page = Page<int>.Of(items, 2, 3);
            Assert.Equal(new[] { 4, 5, 6 }, page.Items);
            Assert.Equal(7, page.TotalCount);
            Assert.Equal(3, page.TotalPages);
            Assert.Equal(new[] { 7 }, Page<int>.Of(items, 3, 3).Items);
        }

        [Fact]
        public void Page_beyond_the_end_is_empty_and_below_one_is_the_first()
        {
            var items = Enumerable.Range(1, 7).ToList();
            Assert.Empty(Page<int>.Of(items, 4, 3).Items);
            Assert.Equal(new[] { 1, 2, 3 }, Page<int>.Of(items, 0, 3).Items);
        }

        [Fact]
        public void Page_size_below_one_means_no_paging()
        {
            var items = Enumerable.Range(1, 7).ToList();
            var page = Page<int>.Of(items, 5, 0);
            Assert.Equal(items, page.Items);
            Assert.Equal(1, page.TotalPages);
            Assert.Equal(1, Page<int>.Of(new List<int>(), 1, 10).TotalPages);
        }

        // ---- 歌星的歌 ----

        [Fact]
        public void Singer_songs_include_duets_with_that_singer()
        {
            var songs = new[] { S("solo", singer: "王傑"), S("duet", singer: "葉歡&王傑"), S("other", singer: "王傑森") };
            Assert.Equal(new[] { "duet", "solo" }, Ids(SongQuery.BySinger(songs, "王傑", Options())).Order().ToArray());
        }

        [Fact]
        public void Duet_singer_name_falls_back_to_the_first_singer()
        {
            var songs = new[] { S("both", singer: "甲/乙"), S("first", singer: "甲"), S("second", singer: "乙") };
            Assert.Equal(new[] { "both", "first" }, Ids(SongQuery.BySinger(songs, "甲&乙", Options())).Order().ToArray());
        }

        [Fact]
        public void Singer_match_ignores_case_and_skips_unnamed_songs()
        {
            var songs = new[] { new Song { Id = "noname", Name = "", Singer = "Jay" }, S("named", singer: "JAY") };
            Assert.Equal(new[] { "named" }, Ids(SongQuery.BySinger(songs, "jay", Options())));
        }
    }
}
