using Xunit;

namespace UltimateKtv.Tests
{
    public class AppSettingsTests
    {
        [Fact]
        public void Defaults_are_already_within_range()
        {
            Assert.Empty(new AppSettings().Normalize());
        }

        [Fact]
        public void Out_of_range_values_move_to_the_nearest_limit()
        {
            var settings = new AppSettings
            {
                AudioAmplify = 600,
                YoutubeSearchCount = 5,
                PlayHistoryCount = 0,
                PlayCountUpdatePercentage = 95,
                SoundCurveExponent = 9.5,
                MarqueeTextFontSize = 4,
                MarqueeTextFontStrokeThickness = 40,
                MarqueeTextPlayCount = -1,
                MarqueeTextTimeDuration = 5000,
                MarqueeSongAddedFontSize = 200,
                MarqueeSongAddedTimeDuration = 99,
                MarqueeBroadcastFontSize = 1,
                MarqueeBroadcastTimeDuration = -3,
            };

            var corrected = settings.Normalize();

            Assert.Equal(500, settings.AudioAmplify);
            Assert.Equal(10, settings.YoutubeSearchCount);
            Assert.Equal(1, settings.PlayHistoryCount);
            Assert.Equal(90, settings.PlayCountUpdatePercentage);
            Assert.Equal(5.0, settings.SoundCurveExponent);
            Assert.Equal(12, settings.MarqueeTextFontSize);
            Assert.Equal(32, settings.MarqueeTextFontStrokeThickness);
            Assert.Equal(0, settings.MarqueeTextPlayCount);
            Assert.Equal(1000, settings.MarqueeTextTimeDuration);
            Assert.Equal(128, settings.MarqueeSongAddedFontSize);
            Assert.Equal(60, settings.MarqueeSongAddedTimeDuration);
            Assert.Equal(12, settings.MarqueeBroadcastFontSize);
            Assert.Equal(0, settings.MarqueeBroadcastTimeDuration);
            Assert.Equal(13, corrected.Count);
            Assert.Contains("AudioAmplify: 600 → 500", corrected);
        }

        [Fact]
        public void Values_on_the_limits_are_kept()
        {
            var settings = new AppSettings { AudioAmplify = 100, MarqueeTextPlayCount = 0, MarqueeSongAddedTimeDuration = 60 };
            Assert.Empty(settings.Normalize());
            Assert.Equal(100, settings.AudioAmplify);
        }

        [Fact]
        public void Clone_is_independent_of_the_original()
        {
            var settings = new AppSettings { SongLibraryDrivePath = @"D:\KTV", RandomPlayEnabled = false };
            var copy = settings.Clone();
            settings.SongLibraryDrivePath = @"E:\KTV";
            settings.RandomPlayEnabled = true;
            Assert.Equal(@"D:\KTV", copy.SongLibraryDrivePath);
            Assert.False(copy.RandomPlayEnabled);
        }
    }
}
