using Xunit;

namespace UltimateKtv.Tests
{
    public class WebCultureMatcherTests
    {
        // 與 Languages 目錄相同的命名方式：zh-TW 排第一，其餘依檔名排序；自訂檔名靠 Language.Code 對應
        private static readonly IReadOnlyList<KeyValuePair<string, IReadOnlyList<string>>> Cultures = Build(
            ("zh-TW", "zh-TW"),
            ("Deutsch_German_德文", "de"),
            ("English_英文", "en"),
            ("en", "en"),
            ("Pilipinas_Philippines_菲律賓文", "fil, tl"),
            ("Português_Portuguese_葡萄牙文", "pt"),
            ("User Define_自定義", null),
            ("zh-CN", "zh-CN"),
            ("日本語_Japan_日文", "ja"),
            ("简体中文_簡體中文", "zh-CN"));

        private static IReadOnlyList<KeyValuePair<string, IReadOnlyList<string>>> Build(params (string Culture, string? Code)[] files) =>
            files.Select(f => new KeyValuePair<string, IReadOnlyList<string>>(f.Culture, WebCultureMatcher.ParseCodes(f.Code, f.Culture))).ToList();

        private static string Resolve(string? acceptLanguage, string current = "zh-TW") =>
            WebCultureMatcher.Resolve(acceptLanguage, Cultures, current);

        [Theory]
        [InlineData("de-DE,de;q=0.9", "Deutsch_German_德文")]
        [InlineData("de-AT", "Deutsch_German_德文")]
        [InlineData("DE", "Deutsch_German_德文")]
        [InlineData("ja-JP,ja;q=0.9,en-US;q=0.8", "日本語_Japan_日文")]
        [InlineData("pt-BR", "Português_Portuguese_葡萄牙文")]
        [InlineData("fil-PH", "Pilipinas_Philippines_菲律賓文")]
        [InlineData("tl", "Pilipinas_Philippines_菲律賓文")]
        public void Custom_file_names_are_matched_by_language_code(string acceptLanguage, string expected)
        {
            Assert.Equal(expected, Resolve(acceptLanguage));
        }

        [Theory]
        [InlineData("zh-TW", "zh-TW")]
        [InlineData("zh-tw", "zh-TW")]
        [InlineData("zh-Hant-TW", "zh-TW")]
        [InlineData("zh-HK", "zh-TW")]
        [InlineData("zh-MO", "zh-TW")]
        [InlineData("zh-CN", "zh-CN")]
        [InlineData("zh-Hans-CN", "zh-CN")]
        [InlineData("zh-SG", "zh-CN")]
        [InlineData("zh", "zh-CN")]
        public void Chinese_is_split_into_traditional_and_simplified(string acceptLanguage, string expected)
        {
            Assert.Equal(expected, Resolve(acceptLanguage));
        }

        [Fact]
        public void Same_code_prefers_the_main_program_language()
        {
            Assert.Equal("English_英文", Resolve("en-US", current: "English_英文"));
            Assert.Equal("简体中文_簡體中文", Resolve("zh-CN", current: "简体中文_簡體中文"));
        }

        [Fact]
        public void Same_code_otherwise_prefers_the_file_named_after_the_code()
        {
            Assert.Equal("en", Resolve("en-US"));
            Assert.Equal("zh-CN", Resolve("zh-Hans"));
        }

        [Fact]
        public void Same_code_otherwise_takes_the_first_file()
        {
            var cultures = Build(("zh-TW", "zh-TW"), ("B_English", "en"), ("A_English", "en"));
            Assert.Equal("B_English", WebCultureMatcher.Resolve("en", cultures, "zh-TW"));
        }

        [Fact]
        public void Languages_are_tried_in_q_order()
        {
            Assert.Equal("日本語_Japan_日文", Resolve("ko-KR;q=0.9,xx;q=1,ja;q=0.8,de;q=0.5"));
            Assert.Equal("Deutsch_German_德文", Resolve("ja;q=0,de"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("*")]
        [InlineData("ko-KR,ko;q=0.9")]
        public void Unmatched_falls_back_to_the_main_program_language(string? acceptLanguage)
        {
            Assert.Equal("English_英文", Resolve(acceptLanguage, current: "English_英文"));
        }

        [Fact]
        public void File_without_code_uses_its_file_name_as_code()
        {
            Assert.Equal(new[] { "User Define_自定義" }, WebCultureMatcher.ParseCodes(null, "User Define_自定義"));
            Assert.Equal(new[] { "ja" }, WebCultureMatcher.ParseCodes("  ", "ja"));
            Assert.Equal(new[] { "fil", "tl" }, WebCultureMatcher.ParseCodes(" fil , tl ", "x"));

            var cultures = Build(("zh-TW", null), ("ja", null));
            Assert.Equal("ja", WebCultureMatcher.Resolve("ja-JP", cultures, "zh-TW"));
        }
    }
}
