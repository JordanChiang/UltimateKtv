using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace UltimateKtv
{
    public static class YtDlpHelper
    {
        private static readonly Regex ProgressRegex = new(@"\[download\]\s+(\d+(?:\.\d+)?)%", RegexOptions.Compiled);
        private static readonly SemaphoreSlim DownloadLock = new(1, 1);

        public static string GetFFmpegDir()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string subDir = Path.Combine(baseDir, "ffmpeg");
            if (File.Exists(Path.Combine(subDir, "ffmpeg.exe"))) return subDir;
            if (File.Exists(Path.Combine(baseDir, "ffmpeg.exe"))) return baseDir;

            string currentDir = Directory.GetCurrentDirectory();
            string currentSubDir = Path.Combine(currentDir, "ffmpeg");
            if (File.Exists(Path.Combine(currentSubDir, "ffmpeg.exe"))) return currentSubDir;
            if (File.Exists(Path.Combine(currentDir, "ffmpeg.exe"))) return currentDir;

            var dirInfo = new DirectoryInfo(baseDir);
            for (int i = 0; i < 6 && dirInfo != null; i++)
            {
                string checkPath = Path.Combine(dirInfo.FullName, "ffmpeg", "ffmpeg.exe");
                if (File.Exists(checkPath)) return Path.Combine(dirInfo.FullName, "ffmpeg");
                dirInfo = dirInfo.Parent;
            }

            return subDir;
        }

        public static string GetYtDlpPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            
            // Check ffmpeg/yt-dlp.exe in base directory
            string ffmpegYtDlp = Path.Combine(baseDir, "ffmpeg", "yt-dlp.exe");
            if (File.Exists(ffmpegYtDlp)) return ffmpegYtDlp;

            // Check root yt-dlp.exe in base directory
            string rootYtDlp = Path.Combine(baseDir, "yt-dlp.exe");
            if (File.Exists(rootYtDlp)) return rootYtDlp;

            // Check current working directory
            string currentDir = Directory.GetCurrentDirectory();
            string currentFfmpegYtDlp = Path.Combine(currentDir, "ffmpeg", "yt-dlp.exe");
            if (File.Exists(currentFfmpegYtDlp)) return currentFfmpegYtDlp;
            string currentRootYtDlp = Path.Combine(currentDir, "yt-dlp.exe");
            if (File.Exists(currentRootYtDlp)) return currentRootYtDlp;

            var dirInfo = new DirectoryInfo(baseDir);
            for (int i = 0; i < 6 && dirInfo != null; i++)
            {
                string checkPath = Path.Combine(dirInfo.FullName, "ffmpeg", "yt-dlp.exe");
                if (File.Exists(checkPath)) return checkPath;
                dirInfo = dirInfo.Parent;
            }

            // Fallback path where it will be downloaded if missing
            string targetDir = Path.Combine(baseDir, "ffmpeg");
            if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
            return Path.Combine(targetDir, "yt-dlp.exe");
        }

        private static DateTime _lastUpdateCheck = DateTime.MinValue;
        private static readonly SemaphoreSlim UpdateLock = new(1, 1);

        public static async Task UpdateYtDlpAsync()
        {
            // Cooldown: Don't run update check more than once per 5 minutes
            if (DateTime.Now - _lastUpdateCheck < TimeSpan.FromMinutes(5)) return;

            if (!await UpdateLock.WaitAsync(0)) return; // Skip if another update is already in progress

            try
            {
                _lastUpdateCheck = DateTime.Now;
                string ytDlpPath = GetYtDlpPath();
                if (File.Exists(ytDlpPath))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = ytDlpPath,
                        Arguments = "-U",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var process = new Process { StartInfo = psi };
                    process.Start();
                    await process.WaitForExitAsync();
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"[YouTube] yt-dlp auto-update error: {ex.Message}");
            }
            finally
            {
                UpdateLock.Release();
            }
        }

        public static async Task EnsureYtDlpAsync()
        {
            string path = GetYtDlpPath();
            if (File.Exists(path) && new FileInfo(path).Length > 0) return;

            await DownloadLock.WaitAsync();
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length > 0) return;

                string dir = Path.GetDirectoryName(path)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string downloadUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
                using var client = new HttpClient();
                byte[] data = await client.GetByteArrayAsync(downloadUrl);
                await File.WriteAllBytesAsync(path, data);
            }
            finally
            {
                DownloadLock.Release();
            }
        }

        public static async Task<string?> GetPreviewStreamUrlAsync(string videoId, CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureYtDlpAsync();
                string ytDlpPath = GetYtDlpPath();

                var psi = new ProcessStartInfo
                {
                    FileName = ytDlpPath,
                    Arguments = $"-g \"https://www.youtube.com/watch?v={videoId}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi };
                process.Start();

                var outputTask = process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync(cancellationToken);

                if (process.ExitCode == 0)
                {
                    string output = await outputTask;
                    var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length > 0) return lines[0];
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                AppLogger.LogError($"[YouTube Preview] GetPreviewStreamUrlAsync error for '{videoId}'", ex);
            }

            return null;
        }

        public static async Task DownloadVideoAsync(
            string videoId,
            string outputFilePath,
            bool isHighQuality,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            await EnsureYtDlpAsync();
            string ytDlpPath = GetYtDlpPath();
            string ffmpegDir = GetFFmpegDir();

            string outDir = Path.GetDirectoryName(outputFilePath)!;
            if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

            string formatArg = isHighQuality
                ? "-f \"bestvideo[height<=1080]+bestaudio/best\" --merge-output-format mp4"
                : "-f \"best\"";

            var psi = new ProcessStartInfo
            {
                FileName = ytDlpPath,
                Arguments = $"{formatArg} --newline --no-playlist --ffmpeg-location \"{ffmpegDir}\" -o \"{outputFilePath}\" \"https://www.youtube.com/watch?v={videoId}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };

            process.OutputDataReceived += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data)) return;
                var match = ProgressRegex.Match(e.Data);
                if (match.Success && double.TryParse(match.Groups[1].Value, out double pct))
                {
                    progress?.Report(pct / 100.0);
                }
            };

            process.Start();
            process.BeginOutputReadLine();

            using (cancellationToken.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(true); } catch { }
            }))
            {
                await process.WaitForExitAsync(cancellationToken);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (process.ExitCode != 0)
            {
                AppLogger.LogError($"[YouTube Download] yt-dlp exit code {process.ExitCode} for video '{videoId}'. Triggering auto-update...");
                _ = UpdateYtDlpAsync();
                throw new Exception($"yt-dlp 下載失敗 (ExitCode: {process.ExitCode})");
            }

            if (!File.Exists(outputFilePath))
            {
                string mp4Path = Path.ChangeExtension(outputFilePath, ".mp4");
                if (File.Exists(mp4Path))
                {
                    outputFilePath = mp4Path;
                }
                else
                {
                    await Task.Delay(500, cancellationToken);
                    if (!File.Exists(outputFilePath) && !File.Exists(mp4Path))
                    {
                        AppLogger.LogError($"[YouTube Download] Output file not found for video '{videoId}': {outputFilePath}");
                        throw new Exception($"yt-dlp 下載完成但未找到輸出檔案: {outputFilePath}");
                    }
                }
            }
        }
    }
}
