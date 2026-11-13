using System;
using System.Collections.Generic;
using System.IO;
using UltimateKtv.Services;
using Xunit;

namespace UltimateKtv.Tests
{
    /// <summary>更新清單純函式：分類 (保留/保護)、「沒動過才覆蓋」決策、以及 NeedsUpdate 的差異判斷。</summary>
    public class UpdateManifestTests
    {
        // ---- DecideProtected：沒動過才覆蓋 (純函式) ----

        [Fact]
        public void Decide_install_when_missing()
            => Assert.Equal(UpdateManifestHelper.ProtectedAction.Install,
                UpdateManifestHelper.DecideProtected(localExists: false, localHash: "", newHash: "newhash", baseHash: null));

        [Fact]
        public void Decide_already_current_when_local_equals_new() // 大小寫不敏感
            => Assert.Equal(UpdateManifestHelper.ProtectedAction.AlreadyCurrent,
                UpdateManifestHelper.DecideProtected(true, "AABB", "aabb", "oldbase"));

        [Fact]
        public void Decide_overwrite_when_untouched() // 本機==基準 (沒動過) 且與新版不同
            => Assert.Equal(UpdateManifestHelper.ProtectedAction.Overwrite,
                UpdateManifestHelper.DecideProtected(true, "base1", "new2", "BASE1"));

        [Fact]
        public void Decide_keep_when_user_modified() // 本機≠基準 → 使用者改過
            => Assert.Equal(UpdateManifestHelper.ProtectedAction.Keep,
                UpdateManifestHelper.DecideProtected(true, "userX", "new2", "base1"));

        [Fact]
        public void Decide_keep_when_no_baseline() // 無基準 → 保守保留
            => Assert.Equal(UpdateManifestHelper.ProtectedAction.Keep,
                UpdateManifestHelper.DecideProtected(true, "whatever", "new2", null));

        // ---- 分類：IsProtected / IsKept ----

        [Theory]
        [InlineData(@"Tools\yt-dlp.exe", true)]
        [InlineData("Tools/yt-dlp.exe", true)]   // 斜線正規化
        [InlineData(@"Tools\yt-dlp_x86.exe", true)]
        [InlineData(@"Tools\ffmpeg.exe", true)]
        [InlineData(@"Tools\YT-DLP.EXE", true)]  // 大小寫不敏感
        [InlineData(@"Tools\ffplay.exe", false)] // ffplay/ffprobe 一般同步
        [InlineData(@"Tools\ffprobe.exe", false)]
        [InlineData("CrazySong.mdb", false)]
        public void IsProtected_matches_tool_exes(string rel, bool expected)
            => Assert.Equal(expected, UpdateManifestHelper.IsProtected(rel));

        [Theory]
        [InlineData("CrazySong.mdb", true)]
        [InlineData("CrazySong.db", true)]
        [InlineData(@"SongMgr\Custom.lang", true)]
        [InlineData(@"Tools\yt-dlp.exe", false)] // 工具不在 KeepExisting (改走 ProtectedTools)
        [InlineData(@"Tools\ffmpeg.exe", false)]
        public void IsKept_only_user_data(string rel, bool expected)
            => Assert.Equal(expected, UpdateManifestHelper.IsKept(rel));

        [Fact]
        public void BaselineKey_normalizes_to_lower_slash()
            => Assert.Equal("tools/yt-dlp.exe", UpdateManifestHelper.BaselineKey(@"Tools\YT-DLP.exe"));

        // ---- NeedsUpdate：以暫存目錄的真實檔案驗證差異判斷 ----

        private const string FakeNewHash = "0000000000000000000000000000000000000000000000000000000000000000";

        private static UpdateComponent Comp(string rel, string hash)
        {
            var c = new UpdateComponent { Name = "X", Url = "http://x" };
            c.Files[rel] = hash;
            return c;
        }

        private sealed class TempDir : IDisposable
        {
            public string Dir { get; } = Path.Combine(Path.GetTempPath(), "uktv_mf_" + Guid.NewGuid().ToString("N"));
            public TempDir() => Directory.CreateDirectory(Dir);

            /// <summary>寫一個檔，回傳它的 sha256 (與 UpdateManifestHelper 同一套)。</summary>
            public string Write(string rel, string content)
            {
                string p = Path.Combine(Dir, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                File.WriteAllText(p, content);
                return UpdateManifestHelper.FileHash(p);
            }

            public void Dispose() { try { Directory.Delete(Dir, true); } catch { } }
        }

        private static Dictionary<string, string> Baseline(string rel, string hash)
            => new() { [UpdateManifestHelper.BaselineKey(rel)] = hash };

        [Fact]
        public void NeedsUpdate_false_when_protected_up_to_date()
        {
            using var t = new TempDir();
            string local = t.Write("Tools/yt-dlp.exe", "v1");
            Assert.False(UpdateManifestHelper.NeedsUpdate(Comp("Tools/yt-dlp.exe", local), t.Dir, new Dictionary<string, string>()));
        }

        [Fact]
        public void NeedsUpdate_true_when_protected_untouched_and_new_available()
        {
            using var t = new TempDir();
            string local = t.Write("Tools/yt-dlp.exe", "v1");
            Assert.True(UpdateManifestHelper.NeedsUpdate(Comp("Tools/yt-dlp.exe", FakeNewHash), t.Dir, Baseline("Tools/yt-dlp.exe", local)));
        }

        [Fact]
        public void NeedsUpdate_false_when_protected_user_modified()
        {
            using var t = new TempDir();
            t.Write("Tools/yt-dlp.exe", "user-updated"); // 本機 ≠ 基準
            Assert.False(UpdateManifestHelper.NeedsUpdate(Comp("Tools/yt-dlp.exe", FakeNewHash), t.Dir, Baseline("Tools/yt-dlp.exe", "some-old-official-hash")));
        }

        [Fact]
        public void NeedsUpdate_false_when_protected_no_baseline()
        {
            using var t = new TempDir();
            t.Write("Tools/yt-dlp.exe", "whatever");
            Assert.False(UpdateManifestHelper.NeedsUpdate(Comp("Tools/yt-dlp.exe", FakeNewHash), t.Dir, new Dictionary<string, string>()));
        }

        [Fact]
        public void NeedsUpdate_true_when_protected_missing()
        {
            using var t = new TempDir();
            Assert.True(UpdateManifestHelper.NeedsUpdate(Comp("Tools/yt-dlp.exe", FakeNewHash), t.Dir, new Dictionary<string, string>()));
        }

        [Fact]
        public void NeedsUpdate_true_when_normal_file_changed() // ffplay 非保護、非保留 → 一般同步
        {
            using var t = new TempDir();
            t.Write("Tools/ffplay.exe", "old");
            Assert.True(UpdateManifestHelper.NeedsUpdate(Comp("Tools/ffplay.exe", FakeNewHash), t.Dir, new Dictionary<string, string>()));
        }

        [Fact]
        public void NeedsUpdate_false_when_kept_user_data_differs() // mdb 已存在 → 保留、不算變動
        {
            using var t = new TempDir();
            t.Write("CrazySong.mdb", "user-db");
            Assert.False(UpdateManifestHelper.NeedsUpdate(Comp("CrazySong.mdb", FakeNewHash), t.Dir, new Dictionary<string, string>()));
        }
    }
}
