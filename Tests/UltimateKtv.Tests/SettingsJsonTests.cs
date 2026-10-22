using Xunit;

namespace UltimateKtv.Tests
{
    public class SettingsJsonTests
    {
        private static SettingsLoadResult<AppSettings> LoadApp(string json) => SettingsJson.Load<AppSettings>(json);

        [Fact]
        public void Valid_file_loads_values()
        {
            var r = LoadApp("""{ "AudioAmplify": 300, "IsLegacyAudioChannelDefinitionEnabled": false, "YoutubeDownloadDir": "D:\\yt" }""");

            Assert.Equal(300, r.Value.AudioAmplify);
            Assert.False(r.Value.IsLegacyAudioChannelDefinitionEnabled);
            Assert.Equal(@"D:\yt", r.Value.YoutubeDownloadDir);
            Assert.False(r.HasProblems);
        }

        [Fact]
        public void Comments_and_trailing_commas_are_allowed()
        {
            var r = LoadApp("""
                {
                  // 音量放大
                  "AudioAmplify": 300,
                }
                """);

            Assert.Equal(300, r.Value.AudioAmplify);
            Assert.False(r.HasProblems);
        }

        [Fact]
        public void Missing_items_use_defaults_and_are_reported()
        {
            var r = LoadApp("""{ "AudioAmplify": 300 }""");

            Assert.Equal(new AppSettings().HttpServerPort, r.Value.HttpServerPort);
            Assert.Contains(nameof(AppSettings.HttpServerPort), r.MissingItems);
            Assert.DoesNotContain(nameof(AppSettings.AudioAmplify), r.MissingItems);
            Assert.False(r.HasProblems);
            Assert.True(r.NeedsSave);
        }

        [Fact]
        public void A_wrong_item_only_resets_that_item()
        {
            var r = LoadApp("""{ "AudioAmplify": "abc", "HttpServerPort": 9090, "PlayerEngine": 1 }""");

            Assert.Equal(new AppSettings().AudioAmplify, r.Value.AudioAmplify);
            Assert.Equal(9090, r.Value.HttpServerPort);
            Assert.Equal(1, r.Value.PlayerEngine);
            Assert.Equal(new[] { nameof(AppSettings.AudioAmplify) }, r.InvalidItems);
            Assert.True(r.HasProblems);
        }

        [Theory]
        [InlineData("""{ "AudioAmplify": "300" }""", 300)]
        [InlineData("""{ "AudioAmplify": " 300 " }""", 300)]
        [InlineData("""{ "AudioAmplify": 300.5 }""", 200)]
        [InlineData("""{ "AudioAmplify": 99999999999 }""", 200)]
        [InlineData("""{ "AudioAmplify": true }""", 200)]
        public void Integer_items(string json, int expected)
        {
            Assert.Equal(expected, LoadApp(json).Value.AudioAmplify);
        }

        [Theory]
        [InlineData("""{ "IsLegacyAudioChannelDefinitionEnabled": "false" }""", false, false)]
        [InlineData("""{ "IsLegacyAudioChannelDefinitionEnabled": "FALSE" }""", false, false)]
        [InlineData("""{ "IsLegacyAudioChannelDefinitionEnabled": 0 }""", false, false)]
        [InlineData("""{ "IsLegacyAudioChannelDefinitionEnabled": 2 }""", true, true)]
        [InlineData("""{ "IsLegacyAudioChannelDefinitionEnabled": "yes" }""", true, true)]
        public void Boolean_items(string json, bool expected, bool invalid)
        {
            var r = LoadApp(json);

            Assert.Equal(expected, r.Value.IsLegacyAudioChannelDefinitionEnabled);
            Assert.Equal(invalid, r.HasProblems);
        }

        [Fact]
        public void Null_falls_back_to_default()
        {
            var r = SettingsJson.Load<ThemeSettings>("""{ "FontFamily": null, "PrimaryColor": null }""");

            Assert.Equal("Microsoft JhengHei", r.Value.FontFamily);
            Assert.Equal(new ThemeSettings().PrimaryColor, r.Value.PrimaryColor);
            Assert.Equal(2, r.InvalidItems.Count);
        }

        [Fact]
        public void Item_names_are_case_insensitive()
        {
            var r = LoadApp("""{ "audioamplify": 300 }""");

            Assert.Equal(300, r.Value.AudioAmplify);
            Assert.Contains(nameof(AppSettings.AudioAmplify), r.MisnamedItems);
            Assert.False(r.HasProblems);
            Assert.True(r.NeedsSave);
        }

        [Fact]
        public void Exact_name_wins_over_different_case()
        {
            var r = LoadApp("""{ "audioamplify": 400, "AudioAmplify": 300 }""");

            Assert.Equal(300, r.Value.AudioAmplify);
            Assert.Empty(r.MisnamedItems);
        }

        [Theory]
        [InlineData("""{ "AudioAmplify": 300 "PlayerEngine": 1 }""")]
        [InlineData("")]
        [InlineData("[1, 2]")]
        public void Syntax_error_uses_all_defaults(string json)
        {
            var r = LoadApp(json);

            Assert.True(r.SyntaxError);
            Assert.True(r.HasProblems);
            Assert.Equal(new AppSettings().AudioAmplify, r.Value.AudioAmplify);
        }

        [Fact]
        public void Unknown_items_are_ignored()
        {
            var r = LoadApp("""{ "Foo": 1, "AudioAmplify": 300 }""");

            Assert.Equal(300, r.Value.AudioAmplify);
            Assert.False(r.HasProblems);
        }

        [Fact]
        public void Double_items_accept_numbers_and_strings()
        {
            var r = SettingsJson.Load<ThemeSettings>("""{ "SongListFontSize": "40", "WaitingListFontSize": 26.5 }""");

            Assert.Equal(40, r.Value.SongListFontSize);
            Assert.Equal(26.5, r.Value.WaitingListFontSize);
            Assert.False(r.HasProblems);
        }

        [Fact]
        public void Backup_copies_the_original_file()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ktv-settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "settings.json");
                File.WriteAllText(path, "{ broken");

                string? backup = SettingsJson.Backup(path);

                Assert.Equal(path + ".bak", backup);
                Assert.Equal("{ broken", File.ReadAllText(backup!));
                Assert.Null(SettingsJson.Backup(Path.Combine(dir, "missing.json")));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // ---------- 主題字級 ----------

        [Fact]
        public void Theme_normalize_clamps_font_sizes()
        {
            var theme = new ThemeSettings { SongListFontSize = -5, FuncBtnFontSize = 0, WaitingListFontSize = 500 };

            var corrected = theme.Normalize();

            Assert.Equal(16, theme.SongListFontSize);
            Assert.Equal(14, theme.FuncBtnFontSize);
            Assert.Equal(60, theme.WaitingListFontSize);
            Assert.Equal(new[] { nameof(ThemeSettings.FuncBtnFontSize), nameof(ThemeSettings.WaitingListFontSize), nameof(ThemeSettings.SongListFontSize) }, corrected);
        }

        [Fact]
        public void Theme_normalize_keeps_defaults()
        {
            Assert.Empty(new ThemeSettings().Normalize());
        }
    }
}
