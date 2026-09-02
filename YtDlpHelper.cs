using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
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
            string toolsDir = Path.Combine(baseDir, "Tools");
            if (File.Exists(Path.Combine(toolsDir, "ffmpeg.exe"))) return toolsDir;

            string currentDir = Directory.GetCurrentDirectory();
            string currentToolsDir = Path.Combine(currentDir, "Tools");
            if (File.Exists(Path.Combine(currentToolsDir, "ffmpeg.exe"))) return currentToolsDir;

            var dirInfo = new DirectoryInfo(baseDir);
            for (int i = 0; i < 6 && dirInfo != null; i++)
            {
                string toolsPath = Path.Combine(dirInfo.FullName, "Tools", "ffmpeg.exe");
                if (File.Exists(toolsPath)) return Path.Combine(dirInfo.FullName, "Tools");
                dirInfo = dirInfo.Parent;
            }

            return toolsDir;
        }

        private static string? _cachedYtDlpPath;

        public static string GetYtDlpPath()
        {
            if (!string.IsNullOrEmpty(_cachedYtDlpPath) && File.Exists(_cachedYtDlpPath))
            {
                return _cachedYtDlpPath;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string toolsYtDlp = Path.Combine(baseDir, "Tools", "yt-dlp.exe");
            if (File.Exists(toolsYtDlp)) return _cachedYtDlpPath = toolsYtDlp;

            string currentDir = Directory.GetCurrentDirectory();
            string currentToolsYtDlp = Path.Combine(currentDir, "Tools", "yt-dlp.exe");
            if (File.Exists(currentToolsYtDlp)) return _cachedYtDlpPath = currentToolsYtDlp;

            var dirInfo = new DirectoryInfo(baseDir);
            for (int i = 0; i < 6 && dirInfo != null; i++)
            {
                string toolsPath = Path.Combine(dirInfo.FullName, "Tools", "yt-dlp.exe");
                if (File.Exists(toolsPath)) return _cachedYtDlpPath = toolsPath;
                dirInfo = dirInfo.Parent;
            }

            string targetDir = Path.Combine(baseDir, "Tools");
            if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
            return _cachedYtDlpPath = Path.Combine(targetDir, "yt-dlp.exe");
        }

        private static DateTime _lastUpdateCheck = DateTime.MinValue;
        private static Task<bool>? _ongoingUpdateTask;
        private static readonly object _updateLockObj = new();

        public static Task<bool> UpdateYtDlpAsync(bool force = false)
        {
            lock (_updateLockObj)
            {
                if (_ongoingUpdateTask != null && !_ongoingUpdateTask.IsCompleted)
                {
                    return _ongoingUpdateTask;
                }

                if (!force && DateTime.Now - _lastUpdateCheck < TimeSpan.FromMinutes(5))
                {
                    return Task.FromResult(false);
                }

                _ongoingUpdateTask = RunUpdateInternalAsync();
                return _ongoingUpdateTask;
            }
        }

        private static async Task<bool> RunUpdateInternalAsync()
        {
            try
            {
                _lastUpdateCheck = DateTime.Now;
                string ytDlpPath = GetYtDlpPath();
                if (!File.Exists(ytDlpPath))
                {
                    await EnsureYtDlpAsync();
                    return true;
                }

                AppLogger.LogInfo("[YouTube] Checking for yt-dlp updates...");
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

                var stdOutTask = process.StandardOutput.ReadToEndAsync();
                var stdErrTask = process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();
                string stdOut = await stdOutTask;
                string stdErr = await stdErrTask;

                if (process.ExitCode == 0)
                {
                    AppLogger.LogInfo($"[YouTube] yt-dlp update process completed successfully: {stdOut.Trim()}");
                    SyncToProjectRootIfApplicable(ytDlpPath);
                    return true;
                }
                else
                {
                    AppLogger.LogError($"[YouTube] yt-dlp -U failed with exit code {process.ExitCode}: {stdErr.Trim()}");

                    // Fallback: If self-update failed, download latest release directly from GitHub
                    AppLogger.LogInfo("[YouTube] Attempting direct download of latest yt-dlp from GitHub releases...");
                    string downloadUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
                    using var client = new HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(60);
                    byte[] data = await client.GetByteArrayAsync(downloadUrl);
                    if (data.Length > 0)
                    {
                        string tempPath = ytDlpPath + ".new";
                        await File.WriteAllBytesAsync(tempPath, data);
                        if (File.Exists(ytDlpPath))
                        {
                            string oldPath = ytDlpPath + ".old";
                            try { if (File.Exists(oldPath)) File.Delete(oldPath); } catch { }
                            try { File.Move(ytDlpPath, oldPath); } catch { }
                        }
                        File.Move(tempPath, ytDlpPath, true);
                        AppLogger.LogInfo("[YouTube] Direct download of yt-dlp succeeded.");
                        SyncToProjectRootIfApplicable(ytDlpPath);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"[YouTube] yt-dlp auto-update error: {ex.Message}", ex);
            }
            return false;
        }

        private static void SyncToProjectRootIfApplicable(string updatedPath)
        {
            try
            {
                var dirInfo = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                for (int i = 0; i < 6 && dirInfo != null; i++)
                {
                    string rootTools = Path.Combine(dirInfo.FullName, "Tools", "yt-dlp.exe");
                    if (File.Exists(rootTools) && !string.Equals(Path.GetFullPath(rootTools), Path.GetFullPath(updatedPath), StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(updatedPath, rootTools, true);
                        AppLogger.LogInfo($"[YouTube] Synchronized updated yt-dlp to project root: {rootTools}");
                        break;
                    }
                    dirInfo = dirInfo.Parent;
                }
            }
            catch { }
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
            string ytDlpPath = GetYtDlpPath();

            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
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
                    var errorTask = process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync(cancellationToken);

                    if (process.ExitCode == 0)
                    {
                        string output = await outputTask;
                        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        if (lines.Length > 0) return lines[0];
                    }
                    else
                    {
                        string err = await errorTask;
                        AppLogger.LogWarn($"[YouTube Preview] yt-dlp preview returned exit code {process.ExitCode} for '{videoId}'. Error: {err.Trim()}");
                        if (attempt == 1)
                        {
                            AppLogger.LogInfo("[YouTube Preview] Updating yt-dlp before retrying preview...");
                            await UpdateYtDlpAsync(force: true);
                            continue;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    AppLogger.LogError($"[YouTube Preview] GetPreviewStreamUrlAsync error for '{videoId}' (attempt {attempt})", ex);
                    if (attempt == 1)
                    {
                        await UpdateYtDlpAsync(force: true);
                        continue;
                    }
                }
            }

            return null;
        }

        public static async Task DownloadVideoAsync(
            string videoId,
            string outputFilePath,
            bool isHighQuality,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default,
            Action<string>? statusCallback = null)
        {
            string ffmpegDir = GetFFmpegDir();
            string outDir = Path.GetDirectoryName(outputFilePath)!;
            if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

            string formatArg = isHighQuality
                ? "-f \"bestvideo[height<=1080]+bestaudio/best\" --merge-output-format mp4"
                : "-f \"bestvideo[height<=720]+bestaudio/best/bestvideo+bestaudio\" --merge-output-format mp4";

            await EnsureYtDlpAsync();
            string ytDlpPath = GetYtDlpPath();

            for (int attempt = 1; attempt <= 2; attempt++)
            {
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
                var stderrBuilder = new StringBuilder();
                double maxReportedPct = 0;

                process.OutputDataReceived += (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(e.Data)) return;
                    var match = ProgressRegex.Match(e.Data);
                    if (match.Success && double.TryParse(match.Groups[1].Value, out double pct))
                    {
                        if (pct > maxReportedPct)
                        {
                            maxReportedPct = pct;
                            progress?.Report(pct / 100.0);
                        }
                    }
                };

                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        lock (stderrBuilder)
                        {
                            stderrBuilder.AppendLine(e.Data);
                        }
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

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

                bool fileExists = File.Exists(outputFilePath);
                if (!fileExists)
                {
                    string mp4Path = Path.ChangeExtension(outputFilePath, ".mp4");
                    if (File.Exists(mp4Path))
                    {
                        outputFilePath = mp4Path;
                        fileExists = true;
                    }
                    else
                    {
                        // Give filesystem a moment
                        await Task.Delay(500, cancellationToken);
                        if (File.Exists(outputFilePath) || File.Exists(mp4Path))
                        {
                            fileExists = true;
                        }
                    }
                }

                if (process.ExitCode == 0 && fileExists)
                {
                    progress?.Report(1.0);
                    return; // Successfully downloaded!
                }

                string stderrText;
                lock (stderrBuilder)
                {
                    stderrText = stderrBuilder.ToString().Trim();
                }

                // If attempt 1 failed, trigger auto-update and retry the download
                if (attempt == 1)
                {
                    AppLogger.LogWarn($"[YouTube Download] yt-dlp failed (ExitCode: {process.ExitCode}) for '{videoId}'. Error details: {stderrText}");
                    AppLogger.LogInfo($"[YouTube Download] Updating yt-dlp to latest version before retrying song '{videoId}'...");
                    statusCallback?.Invoke("yt-dlp 更新中...");

                    // Clean up any incomplete 0-byte file before retrying
                    try
                    {
                        if (File.Exists(outputFilePath) && new FileInfo(outputFilePath).Length == 0)
                        {
                            File.Delete(outputFilePath);
                        }
                    }
                    catch { }

                    bool updateSuccess = await UpdateYtDlpAsync(force: true);
                    AppLogger.LogInfo($"[YouTube Download] yt-dlp update completed (result={updateSuccess}). Retrying download for '{videoId}'...");
                    statusCallback?.Invoke("更新完成，重新下載中...");
                    progress?.Report(0);

                    continue; // Proceed to attempt 2
                }

                // Attempt 2 also failed
                AppLogger.LogError($"[YouTube Download] yt-dlp failed for video '{videoId}' after retry (ExitCode: {process.ExitCode}). Error: {stderrText}");
                throw new Exception($"yt-dlp 下載失敗 (ExitCode: {process.ExitCode}): {stderrText}");
            }
        }
    }
}
