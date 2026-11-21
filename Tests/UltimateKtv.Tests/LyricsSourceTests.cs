using System;
using System.Collections.Generic;
using UltimateKtv;
using Xunit;

namespace UltimateKtv.Tests
{
    public class LyricsSourceTests
    {
        private const string Video = @"E:\KTV\Singer - Title.mpg";

        // Windows 的檔案系統不分大小寫
        private static Func<string, bool> Files(params string[] names)
        {
            var set = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            return path => set.Contains(path);
        }

        [Theory]
        [InlineData(new[] { ".ass", ".ssa", ".srt" }, ".ass")]
        [InlineData(new[] { ".ssa", ".srt" }, ".ssa")]
        [InlineData(new[] { ".srt", ".ass" }, ".ass")]
        [InlineData(new[] { ".srt" }, ".srt")]
        public void SubtitleFile_PriorityAssSsaSrt(string[] present, string expected)
        {
            var files = Files(Array.ConvertAll(present, ext => @"E:\KTV\Singer - Title" + ext));

            Assert.Equal(@"E:\KTV\Singer - Title" + expected, LyricsSource.FindSubtitleFile(Video, files));
        }

        [Fact]
        public void SubtitleFile_ExtensionCaseIgnoredByFileSystem()
        {
            var files = Files(@"E:\KTV\Singer - Title.ASS");

            Assert.NotNull(LyricsSource.FindSubtitleFile(Video, files));
        }

        [Fact]
        public void SubtitleFile_OnlyExactSameName()
        {
            var files = Files(@"E:\KTV\Singer - Title.zh-TW.srt", @"E:\KTV\Singer - Title2.ass",
                              @"E:\KTV\Singer - Title.mpg.srt", @"E:\KTV\Singer - Title.lrc");

            Assert.Null(LyricsSource.FindSubtitleFile(Video, files));
        }

        [Fact]
        public void YoutubeCacheFile_SameRule()
        {
            const string cached = @"E:\KTV\Cache\Title_abcdefghijk.mp4";
            var files = Files(@"E:\KTV\Cache\Title_abcdefghijk.srt",
                              @"E:\KTV\Cache\Title_abcdefghijk.lrc");

            Assert.Equal(@"E:\KTV\Cache\Title_abcdefghijk.srt", LyricsSource.FindSubtitleFile(cached, files));
            Assert.Equal(@"E:\KTV\Cache\Title_abcdefghijk.lrc", LyricsSource.FindLrcFile(cached, files));
        }

        [Fact]
        public void DotsInFileName_OnlyLastExtensionReplaced()
        {
            const string video = @"E:\KTV\A.B.C.mkv";
            var files = Files(@"E:\KTV\A.B.C.ass", @"E:\KTV\A.B.ass");

            Assert.Equal(@"E:\KTV\A.B.C.ass", LyricsSource.FindSubtitleFile(video, files));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void NoVideoPath_Nothing(string? video)
        {
            Assert.Null(LyricsSource.FindSubtitleFile(video, _ => true));
            Assert.Null(LyricsSource.FindLrcFile(video, _ => true));
        }

        [Theory]
        [InlineData(true, true, true, LyricsKind.SubtitleFile)]
        [InlineData(true, false, true, LyricsKind.SubtitleFile)]
        [InlineData(true, false, false, LyricsKind.SubtitleFile)]
        [InlineData(false, true, true, LyricsKind.EmbeddedTrack)]
        [InlineData(false, true, false, LyricsKind.EmbeddedTrack)]
        [InlineData(false, false, true, LyricsKind.LrcFile)]
        [InlineData(false, false, false, LyricsKind.None)]
        public void Choose_SubtitleFileThenEmbeddedThenLrc(bool subtitle, bool embedded, bool lrc, LyricsKind expected)
        {
            Assert.Equal(expected, LyricsSource.Choose(subtitle, embedded, lrc));
        }
    }
}
