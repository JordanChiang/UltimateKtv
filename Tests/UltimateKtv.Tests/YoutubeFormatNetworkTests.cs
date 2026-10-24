using System.Diagnostics;
using System.Globalization;
using Xunit;
using Xunit.Abstractions;
using YoutubeExplode;

namespace UltimateKtv.Tests
{
    /// <summary>
    /// 連網測試：實際向 YouTube 取格式，確認下載仍拿得到高畫質影像與 AAC-LC 128k 音訊。
    /// 下載品質取決於 YouTube 與 yt-dlp (曾因 YouTube 改版而無聲無息地降成 360p + 48k)，只有連網才測得出來。
    /// 需要網路；要略過時：dotnet test ... --filter "Category!=Network"。
    /// 測試影片被下架時換成其他有 1080p 的影片 / 只有 360p 的舊影片。
    /// </summary>
    [Trait("Category", "Network")]
    public class YoutubeFormatNetworkTests
    {
        private const string Video1080 = "xWTiOqJqkk0"; // 五月天〈傷心的人別聽慢歌〉MV：有 1080p
        private const string Video360 = "Yl9sIjmaZP8";  // 張學友〈一千個傷心的理由〉：只有 360p，舊版程式只抓到 HE-AAC 48k
        private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(2);

        private readonly ITestOutputHelper _output;

        public YoutubeFormatNetworkTests(ITestOutputHelper output) => _output = output;

        [Theory]
        [InlineData(Video1080, 1080, 1080)]
        [InlineData(Video1080, 720, 720)]
        [InlineData(Video360, 1080, 360)]
        public void YtDlp_SelectsSeparateH264AndAacLc(string videoId, int maxHeight, int expectedHeight)
        {
            string line = RunYtDlp($"--simulate -f \"{YoutubeFormat.YtDlpFormat(maxHeight)}\" " +
                                   "--print \"%(format_id)s|%(height)s|%(vcodec)s|%(acodec)s|%(abr)s\" " + Url(videoId));
            _output.WriteLine(line);
            var f = line.Split('|');
            Assert.True(f.Length == 5, $"unexpected yt-dlp output: {line}");

            Assert.Contains("+", f[0]); // 分開的影像+音訊，不是 format 18 之類的影音合併檔
            Assert.Equal(expectedHeight.ToString(CultureInfo.InvariantCulture), f[1]);
            Assert.StartsWith("avc1", f[2]);
            Assert.StartsWith("mp4a.40.2", f[3]); // AAC-LC (HE-AAC 是 mp4a.40.5)
            Assert.True(double.Parse(f[4], CultureInfo.InvariantCulture) >= 100, $"audio bitrate too low: {f[4]}k");
        }

        [Fact]
        public void YtDlp_DownloadsSelectedAudio()
        {
            // --simulate 不會碰到下載網址；實際下載音訊 (約 3MB) 才測得出 403 / SABR 這類擋下載的問題
            string dir = Path.Combine(Path.GetTempPath(), "ukt-ytdlp-" + Guid.NewGuid().ToString("N"));
            try
            {
                string file = Path.Combine(dir, "audio.m4a");
                RunYtDlp($"-f \"ba[ext=m4a][protocol=https]\" --no-part -o \"{file}\" " + Url(Video1080));
                Assert.True(File.Exists(file) && new FileInfo(file).Length > 1_000_000, "audio was not downloaded");
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        [Theory]
        [InlineData(1080)]
        [InlineData(720)]
        public async Task YoutubeExplode_SelectsSeparateH264AndAacLc(int maxHeight)
        {
            var manifest = await new YoutubeClient().Videos.Streams.GetManifestAsync(Video1080);
            var (video, audio) = YoutubeFormat.SelectStreams(manifest, maxHeight);
            _output.WriteLine($"video {video?.VideoQuality} {video?.VideoCodec}, audio {audio?.AudioCodec} {audio?.Bitrate}");

            Assert.NotNull(video);
            Assert.NotNull(audio);
            Assert.Equal(maxHeight, video!.VideoQuality.MaxHeight);
            Assert.StartsWith("avc1", video.VideoCodec);
            Assert.StartsWith("mp4a.40.2", audio!.AudioCodec);
            Assert.True(audio.Bitrate.KiloBitsPerSecond >= 100, $"audio bitrate too low: {audio.Bitrate}");
        }

        [Fact]
        public async Task YoutubeExplode_DownloadsSelectedAudio()
        {
            var youtube = new YoutubeClient();
            var manifest = await youtube.Videos.Streams.GetManifestAsync(Video1080);
            var (_, audio) = YoutubeFormat.SelectStreams(manifest, 1080);
            Assert.NotNull(audio);

            string file = Path.Combine(Path.GetTempPath(), "ukt-yx-" + Guid.NewGuid().ToString("N") + ".m4a");
            try
            {
                using var cts = new CancellationTokenSource(ProcessTimeout);
                await youtube.Videos.Streams.DownloadAsync(audio!, file, cancellationToken: cts.Token);
                Assert.Equal(audio!.Size.Bytes, new FileInfo(file).Length);
            }
            finally
            {
                try { File.Delete(file); } catch { }
            }
        }

        private static string Url(string videoId) => $"\"https://www.youtube.com/watch?v={videoId}\"";

        /// <summary>以主程式相同的客戶端執行 repo 內的 Tools/yt-dlp.exe，回傳最後一行輸出；失敗時把 stderr 放進訊息。</summary>
        private static string RunYtDlp(string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = FindTool("yt-dlp.exe"),
                Arguments = $"--no-warnings --no-playlist --extractor-args \"youtube:player_client={YoutubeFormat.DownloadClients}\" {arguments}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(ProcessTimeout))
            {
                process.Kill(true);
                Assert.Fail("yt-dlp timed out");
            }
            Assert.True(process.ExitCode == 0, $"yt-dlp exit {process.ExitCode}: {stderr.Result}");
            return stdout.Result.Trim().Split('\n').Last().Trim();
        }

        private static string FindTool(string name)
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string path = Path.Combine(dir.FullName, "Tools", name);
                if (File.Exists(path)) return path;
            }
            throw new FileNotFoundException($"Tools\\{name} not found above {AppContext.BaseDirectory}");
        }
    }
}
