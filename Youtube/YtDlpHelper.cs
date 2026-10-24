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
        private const string LatestReleaseUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest";
        private const string LatestDownloadUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";

        // yt-dlp errors that will never succeed by updating yt-dlp and retrying
        private static readonly string[] PermanentErrorMarkers =
        {
            "Incomplete YouTube ID", "looks truncated", "is not a valid URL",
            "Video unavailable", "Private video", "has been removed", "account associated with this video has been terminated"
        };

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
        private static DateTime _lastConfirmedUpToDate = DateTime.MinValue;
        private static Task<bool>? _ongoingUpdateTask;
        private static readonly object _updateLockObj = new();

        /// <summary>
        /// Updates yt-dlp only when a newer version exists. Returns true only if a new binary was installed.
        /// </summary>
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

                // Even a forced update is skipped if we recently confirmed the latest version is installed
                if (DateTime.Now - _lastConfirmedUpToDate < TimeSpan.FromMinutes(30) && File.Exists(GetYtDlpPath()))
                {
                    AppLogger.LogInfo("[YouTube] yt-dlp was confirmed up to date recently, skipping update check.");
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
                    if (stdOut.Contains("is up to date", StringComparison.OrdinalIgnoreCase))
                    {
                        // Same version: nothing downloaded, nothing to sync
                        _lastConfirmedUpToDate = DateTime.Now;
                        AppLogger.LogInfo($"[YouTube] yt-dlp is already the latest version, no download needed: {stdOut.Trim()}");
                        return false;
                    }

                    AppLogger.LogInfo($"[YouTube] yt-dlp updated to a new version: {stdOut.Trim()}");
                    _lastConfirmedUpToDate = DateTime.Now;
                    SyncToProjectRootIfApplicable(ytDlpPath);
                    return true;
                }
                else
                {
                    AppLogger.LogError($"[YouTube] yt-dlp -U failed with exit code {process.ExitCode}: {stdErr.Trim()}");

                    using var client = new HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(60);

                    // Fallback: compare local version with the latest GitHub release before downloading
                    string localVersion = await GetLocalVersionAsync(ytDlpPath);
                    string latestVersion = await GetLatestReleaseVersionAsync(client);
                    if (!string.IsNullOrEmpty(localVersion) && localVersion == latestVersion)
                    {
                        _lastConfirmedUpToDate = DateTime.Now;
                        AppLogger.LogInfo($"[YouTube] Local yt-dlp ({localVersion}) matches latest release, skipping download.");
                        return false;
                    }

                    AppLogger.LogInfo($"[YouTube] Downloading latest yt-dlp from GitHub releases (local={localVersion}, latest={latestVersion})...");
                    byte[] data = await client.GetByteArrayAsync(LatestDownloadUrl);
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
                        _lastConfirmedUpToDate = DateTime.Now;
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

        private static readonly Regex DurationRegex = new(@"Duration:\s*(\d+):(\d{2}):(\d{2}(?:\.\d+)?)", RegexOptions.Compiled);

        /// <summary>
        /// Reads a media file's duration (seconds) via "ffmpeg -i"; returns 0 if unknown.
        /// </summary>
        public static async Task<int> GetMediaDurationSecondsAsync(string filePath)
        {
            try
            {
                string ffmpegPath = Path.Combine(GetFFmpegDir(), "ffmpeg.exe");
                if (!File.Exists(ffmpegPath) || !File.Exists(filePath)) return 0;

                var psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = $"-hide_banner -i \"{filePath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = new Process { StartInfo = psi };
                process.Start();
                var stdOutTask = process.StandardOutput.ReadToEndAsync();
                string stdErr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                await stdOutTask;

                // ffmpeg exits with 1 (no output file) but still prints the input info
                var m = DurationRegex.Match(stdErr);
                if (!m.Success) return 0;
                double seconds = int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60
                               + double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
                return (int)seconds; // truncate, same as the player's MediaDuration-based length
            }
            catch (Exception ex)
            {
                AppLogger.LogWarn($"[YouTube] Failed to read media duration for '{filePath}': {ex.Message}");
                return 0;
            }
        }

        private static async Task<string> GetLocalVersionAsync(string ytDlpPath)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ytDlpPath,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = new Process { StartInfo = psi };
                process.Start();
                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                return output.Trim();
            }
            catch
            {
                return "";
            }
        }

        private static async Task<string> GetLatestReleaseVersionAsync(HttpClient client)
        {
            try
            {
                // releases/latest redirects to .../releases/tag/{version}
                using var response = await client.GetAsync(LatestReleaseUrl, HttpCompletionOption.ResponseHeadersRead);
                string? finalUrl = response.RequestMessage?.RequestUri?.AbsoluteUri;
                if (string.IsNullOrEmpty(finalUrl)) return "";
                int idx = finalUrl.LastIndexOf("/tag/", StringComparison.OrdinalIgnoreCase);
                return idx >= 0 ? finalUrl[(idx + 5)..].Trim('/') : "";
            }
            catch
            {
                return "";
            }
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

                using var client = new HttpClient();
                byte[] data = await client.GetByteArrayAsync(LatestDownloadUrl);
                await File.WriteAllBytesAsync(path, data);
                _lastConfirmedUpToDate = DateTime.Now;
            }
            finally
            {
                DownloadLock.Release();
            }
        }

        public static async Task<string?> GetPreviewStreamUrlAsync(string videoId, CancellationToken cancellationToken = default)
        {
            if (!YoutubeVideoId.IsValid(videoId))
            {
                AppLogger.LogWarn($"[YouTube Preview] '{videoId}' is not a valid YouTube video ID, skipping preview.");
                return null;
            }

            string ytDlpPath = GetYtDlpPath();

            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = ytDlpPath,
                        Arguments = $"--extractor-args \"youtube:player_client=android,web\" -g \"https://www.youtube.com/watch?v={videoId}\"",
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
            if (!YoutubeVideoId.IsValid(videoId))
            {
                AppLogger.LogError($"[YouTube Download] '{videoId}' is not a valid YouTube video ID, download aborted.");
                throw new Exception(Loc.T("Ytdlp.InvalidId", videoId));
            }

            string ffmpegDir = GetFFmpegDir();
            string outDir = Path.GetDirectoryName(outputFilePath)!;
            if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

            string formatArg = $"-f \"{YoutubeFormat.YtDlpFormat(isHighQuality ? 1080 : 720)}\" --merge-output-format mp4";

            await EnsureYtDlpAsync();
            string ytDlpPath = GetYtDlpPath();

            for (int attempt = 1; attempt <= 2; attempt++)
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ytDlpPath,
                    Arguments = $"{formatArg} --extractor-args \"youtube:player_client={YoutubeFormat.DownloadClients}\" --newline --no-playlist --ffmpeg-location \"{ffmpegDir}\" -o \"{outputFilePath}\" \"https://www.youtube.com/watch?v={videoId}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi };
                var stderrBuilder = new StringBuilder();
                int totalStreams = 2; // Default to 2 streams (video + audio) for bestvideo+bestaudio
                int currentStream = 0;
                double maxStreamPct = 0;
                double currentOverallPct = 0;
                bool isMerging = false;
                CancellationTokenSource? mergerCts = null;

                void UpdateProgress(double pct, string? status = null)
                {
                    if (pct > currentOverallPct)
                    {
                        currentOverallPct = Math.Min(99.0, pct);
                        progress?.Report(currentOverallPct / 100.0);
                    }
                    if (!string.IsNullOrEmpty(status))
                    {
                        statusCallback?.Invoke(status);
                    }
                }

                process.OutputDataReceived += (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(e.Data)) return;

                    // 1. Detect number of streams from format info if available
                    // e.g. "[info] ...: Downloading 1 format(s): 398+251"
                    var formatMatch = Regex.Match(e.Data, @"Downloading\s+\d+\s+format\(s\):\s*([^\s]+)");
                    if (formatMatch.Success)
                    {
                        totalStreams = formatMatch.Groups[1].Value.Contains('+') ? 2 : 1;
                    }

                    // 2. Detect stream start: "[download] Destination: ..."
                    if (e.Data.Contains("[download] Destination:"))
                    {
                        currentStream++;
                        maxStreamPct = 0;
                        if (totalStreams == 2)
                        {
                            if (currentStream <= 1)
                            {
                                UpdateProgress(currentOverallPct, Loc.T("Ytdlp.DownloadingVideo"));
                            }
                            else
                            {
                                UpdateProgress(Math.Max(currentOverallPct, 75.0), Loc.T("Ytdlp.DownloadingAudio"));
                            }
                        }
                        else
                        {
                            UpdateProgress(currentOverallPct, Loc.T("Ytdlp.Downloading"));
                        }
                    }

                    // 3. Handle already downloaded file / stream
                    if (e.Data.Contains("has already been downloaded"))
                    {
                        if (totalStreams == 2 && currentStream <= 1)
                        {
                            UpdateProgress(75.0, Loc.T("Ytdlp.VideoDone"));
                        }
                        else
                        {
                            UpdateProgress(90.0, Loc.T("Ytdlp.AudioDone"));
                        }
                    }

                    // 4. Parse stream percentage: "[download]  17.2% of ..."
                    var match = ProgressRegex.Match(e.Data);
                    if (match.Success && double.TryParse(match.Groups[1].Value, out double streamPct))
                    {
                        if (streamPct > maxStreamPct)
                        {
                            maxStreamPct = streamPct;
                            double overallPct;
                            string? statusMsg = null;

                            if (totalStreams == 2)
                            {
                                if (currentStream <= 1)
                                {
                                    // Video stream: maps to 0% -> 75%
                                    overallPct = (streamPct / 100.0) * 75.0;
                                    statusMsg = Loc.T("Ytdlp.DownloadingVideo");
                                }
                                else
                                {
                                    // Audio stream: maps to 75% -> 90%
                                    overallPct = 75.0 + ((streamPct / 100.0) * 15.0);
                                    statusMsg = Loc.T("Ytdlp.DownloadingAudio");
                                }
                            }
                            else
                            {
                                // Single combined stream: maps to 0% -> 92%
                                overallPct = (streamPct / 100.0) * 92.0;
                                statusMsg = Loc.T("Ytdlp.Downloading");
                            }

                            UpdateProgress(overallPct, statusMsg);
                        }
                    }

                    // 5. Detect post-processing / merger: "[Merger] Merging formats into ..."
                    if (e.Data.Contains("[Merger]") || e.Data.Contains("[Fixup") || e.Data.Contains("[VideoConvertor]"))
                    {
                        if (!isMerging)
                        {
                            isMerging = true;
                            UpdateProgress(Math.Max(currentOverallPct, 92.0), Loc.T("Ytdlp.Merging"));

                            // Gradually increment progress up to 98% while FFmpeg is merging
                            mergerCts = new CancellationTokenSource();
                            var mToken = mergerCts.Token;
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    while (!mToken.IsCancellationRequested && currentOverallPct < 98.0)
                                    {
                                        await Task.Delay(500, mToken);
                                        if (mToken.IsCancellationRequested) break;
                                        UpdateProgress(currentOverallPct + 1.0, Loc.T("Ytdlp.Merging"));
                                    }
                                }
                                catch { }
                            }, mToken);
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
                    try { mergerCts?.Cancel(); } catch { }
                }))
                {
                    await process.WaitForExitAsync(cancellationToken);
                }

                mergerCts?.Cancel();
                mergerCts?.Dispose();
                mergerCts = null;

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
                    statusCallback?.Invoke(Loc.T("Ytdlp.Done"));
                    return; // Successfully downloaded!
                }

                string stderrText;
                lock (stderrBuilder)
                {
                    stderrText = stderrBuilder.ToString().Trim();
                }

                bool isPermanentError = Array.Exists(PermanentErrorMarkers, m => stderrText.Contains(m, StringComparison.OrdinalIgnoreCase));

                // If attempt 1 failed, trigger auto-update and retry the download (pointless for permanent errors)
                if (attempt == 1 && !isPermanentError)
                {
                    AppLogger.LogWarn($"[YouTube Download] yt-dlp failed (ExitCode: {process.ExitCode}) for '{videoId}'. Error details: {stderrText}");
                    AppLogger.LogInfo($"[YouTube Download] Updating yt-dlp to latest version before retrying song '{videoId}'...");
                    statusCallback?.Invoke(Loc.T("Ytdlp.Updating"));

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
                    statusCallback?.Invoke(Loc.T("Ytdlp.UpdatedRetrying"));
                    progress?.Report(0);

                    continue; // Proceed to attempt 2
                }

                // Attempt 2 also failed, or the error is permanent
                AppLogger.LogError($"[YouTube Download] yt-dlp failed for video '{videoId}' (attempt {attempt}, permanent={isPermanentError}, ExitCode: {process.ExitCode}). Error: {stderrText}");
                throw new Exception(Loc.T("Ytdlp.Failed", process.ExitCode, stderrText));
            }
        }
    }
}
