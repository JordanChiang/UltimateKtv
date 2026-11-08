#nullable enable
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace UltimateKtv.Services
{
    public static class UpdateManager
    {
        private const string DefaultUpdateUrl = "https://api.github.com/repos/JordanChiang/UltimateKtv/releases/latest";
        private const string ManifestAssetName = "update.json";
        private static readonly HttpClient _httpClient;

        /// <summary>要下載並套用的一包檔案 (差異更新時為一個元件，無清單時為完整包)。</summary>
        private readonly struct DownloadItem
        {
            public DownloadItem(string url, string name, long size)
            {
                Url = url;
                Name = name;
                Size = size;
            }

            public string Url { get; }
            public string Name { get; }
            public long Size { get; }
        }

        static UpdateManager()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "UltimateKtv-UpdateClient");
            // Set timeout relatively short for version check so it doesn't hang startup
            _httpClient.Timeout = TimeSpan.FromMinutes(30);
        }

        private static string GetFullUrl(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return DefaultUpdateUrl;

            // If it's already a full URL, use it directly
            if (input.StartsWith("https", StringComparison.OrdinalIgnoreCase))
            {
                return input;
            }

            // Otherwise, treat as GitHub Owner/Repo
            return $"https://api.github.com/repos/{input}/releases/latest";
        }

        public static async Task CheckForUpdatesAsync()
        {
            try
            {
                // 1. Get current local version first
                Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

                // 2. Determine which URL to check
                string setting = SettingsManager.Instance.CurrentSettings.GitHubRepoUrl;
                string apiUrl = GetFullUrl(setting);

                // 3. Fetch latest release info
                var response = await _httpClient.GetAsync(apiUrl);

                // Fallback to default if custom URL fails and it wasn't already the default
                if (!response.IsSuccessStatusCode && apiUrl != DefaultUpdateUrl)
                {
                    AppLogger.Log($"自定義更新位址 {apiUrl} 無法存取 ({response.StatusCode})，改用預設位址。");
                    apiUrl = DefaultUpdateUrl;
                    response = await _httpClient.GetAsync(apiUrl);
                }

                if (!response.IsSuccessStatusCode)
                    return; // Fail silently on startup if both fail

                var content = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(content)) return;

                Version? latestVersion = null;
                string releaseNotes = "";
                GitHubReleaseInfo? latestRelease = null;

                // 4. Parse version info (Try GitHub JSON format first, then plain text)
                try
                {
                    latestRelease = JsonConvert.DeserializeObject<GitHubReleaseInfo>(content);
                    if (latestRelease != null && !string.IsNullOrEmpty(latestRelease.TagName))
                    {
                        if (Version.TryParse(latestRelease.TagName.TrimStart('v', 'V'), out latestVersion))
                        {
                            releaseNotes = latestRelease.Body;
                        }
                    }
                }
                catch
                {
                    // If JSON parsing fails, the content might be a simple version string (e.g., "1.2.3")
                }

                if (latestVersion == null)
                {
                    // Fallback: Try to parse the entire content as a version string
                    if (!Version.TryParse(content.Trim().TrimStart('v', 'V'), out latestVersion))
                    {
                        return; // Could not determine remote version
                    }
                }

                // 5. Compare version
                if (latestVersion <= currentVersion) return;
                if (latestRelease == null || latestRelease.Assets.Count == 0)
                {
                    if (apiUrl == DefaultUpdateUrl)
                        AppLogger.Log("找到了新版本，但 release 沒有可下載的附檔。");
                    return;
                }

                // 6. Decide what to download: differential (update.json) first, else the full package zip
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                List<DownloadItem>? items = await BuildDownloadPlanAsync(latestRelease, appDir);
                if (items == null || items.Count == 0)
                {
                    if (apiUrl == DefaultUpdateUrl)
                        AppLogger.Log("找到了新版本，但找不到可用的更新附檔 (update.json 或完整包)。");
                    return;
                }

                // 7. Ask the user, then perform the update
                bool userWantsUpdate = false;
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var prompt = new UpdatePromptWindow(currentVersion.ToString(), latestVersion!.ToString(), releaseNotes);
                    prompt.Owner = Application.Current.MainWindow;
                    prompt.ShowDialog();
                    userWantsUpdate = prompt.DoUpdate;
                });

                if (userWantsUpdate)
                {
                    await PerformUpdateAsync(items);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Update Check Failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 依 release 決定要下載哪些包：有 update.json 時只挑雜湊有變的元件；沒有時退回完整包。
        /// 全部元件都已是最新 (例如只有歌庫這類保留檔不同) 時回傳空清單。
        /// </summary>
        private static async Task<List<DownloadItem>?> BuildDownloadPlanAsync(GitHubReleaseInfo release, string appDir)
        {
            var manifestAsset = release.Assets.FirstOrDefault(
                a => a.Name.Equals(ManifestAssetName, StringComparison.OrdinalIgnoreCase));

            if (manifestAsset != null && !string.IsNullOrEmpty(manifestAsset.BrowserDownloadUrl))
            {
                try
                {
                    string json = await _httpClient.GetStringAsync(manifestAsset.BrowserDownloadUrl);
                    UpdateManifest? manifest = UpdateManifestHelper.Parse(json);
                    if (manifest != null)
                    {
                        // 比對檔案雜湊可能較耗時，移出 UI 執行緒
                        List<UpdateComponent> changed = await Task.Run(
                            () => UpdateManifestHelper.ChangedComponents(manifest, appDir));
                        return changed
                            .Select(c => new DownloadItem(c.Url, c.DisplayName, c.Size))
                            .ToList();
                    }

                    // 有 update.json 卻讀不到或格式錯誤：這次先不更新 (下次啟動再檢查)，不改抓完整包
                    AppLogger.Log("更新清單 update.json 格式錯誤，略過這次更新。");
                    return null;
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"無法取得更新清單 update.json：{ex.Message}");
                    return null;
                }
            }

            // 沒有 update.json 的舊 release：下載完整包
            GitHubReleaseAsset? full = PickFullPackage(release.Assets);
            if (full == null || string.IsNullOrEmpty(full.BrowserDownloadUrl)) return null;
            return new List<DownloadItem> { new DownloadItem(full.BrowserDownloadUrl, full.Name, 0) };
        }

        /// <summary>完整安裝包 zip：優先認 UltimateKtv_&lt;版本&gt;.zip，避免誤抓元件 zip (Codec_*.zip…)；舊 release 退回第一個 .zip。</summary>
        private static GitHubReleaseAsset? PickFullPackage(List<GitHubReleaseAsset> assets)
        {
            var full = assets.FirstOrDefault(
                a => Regex.IsMatch(a.Name ?? "", @"^UltimateKtv_[\d.]+\.zip$", RegexOptions.IgnoreCase));
            if (full != null) return full;
            return assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        }

        private static async Task PerformUpdateAsync(List<DownloadItem> items)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "UltimateKtv_Updater");
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
            Directory.CreateDirectory(tempDir);

            string extractPath = Path.Combine(tempDir, "extracted");

            // Open Progress Window
            DownloadProgressWindow? progressWindow = null;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                progressWindow = new DownloadProgressWindow();
                if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsVisible)
                {
                    progressWindow.Owner = Application.Current.MainWindow;
                }
                progressWindow.Show();
            });

            try
            {
                long totalBytes = items.Sum(i => i.Size); // 0 時 (完整包) 改看各自的 Content-Length
                long completedBytes = 0;

                for (int i = 0; i < items.Count; i++)
                {
                    DownloadItem item = items[i];
                    string zipFilePath = Path.Combine(tempDir, $"package{i}.zip");

                    // Download file with progress
                    using (var response = await _httpClient.GetAsync(item.Url, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();
                        long itemTotal = response.Content.Headers.ContentLength ?? item.Size;

                        using (var contentStream = await response.Content.ReadAsStreamAsync())
                        using (var fileStream = new FileStream(zipFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                        {
                            var buffer = new byte[81920];
                            long itemRead = 0;
                            int read;
                            while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, read);
                                itemRead += read;

                                long doneBytes = completedBytes + itemRead;
                                double percentage;
                                string text;
                                if (totalBytes > 0)
                                {
                                    percentage = (double)doneBytes / totalBytes * 100;
                                    text = Loc.T("Update.Progress", doneBytes / 1024 / 1024, totalBytes / 1024 / 1024);
                                }
                                else if (itemTotal > 0)
                                {
                                    percentage = (double)itemRead / itemTotal * 100;
                                    text = Loc.T("Update.Progress", itemRead / 1024 / 1024, itemTotal / 1024 / 1024);
                                }
                                else
                                {
                                    percentage = 0;
                                    text = Loc.T("Update.Progress", itemRead / 1024 / 1024, 0);
                                }
                                if (items.Count > 1) text = $"[{i + 1}/{items.Count}] {item.Name} - {text}";
                                progressWindow?.ReportProgress(percentage, text);
                            }

                            completedBytes += item.Size > 0 ? item.Size : itemRead;
                        }
                    }

                    // Extract this package into the shared folder (differential: only changed components land here)
                    string zip = zipFilePath;
                    await Task.Run(() => ZipHelper.SafeExtractZip(zip, extractPath, msg => AppLogger.Log(msg)));
                }

                progressWindow?.ReportProgress(100, Loc.T("Update.Applying"));

                // Hide progress window right before triggering restart
                await Application.Current.Dispatcher.InvokeAsync(() => progressWindow?.Close());

                // Create update command line for the standalone WPF Updater
                string currentAppPath = AppDomain.CurrentDomain.BaseDirectory;
                string currentExePath = Process.GetCurrentProcess().MainModule?.FileName ?? "UltimateKtv.exe";
                int currentPid = Process.GetCurrentProcess().Id;

                // Locate the standalone updater exe
                string updaterPath = Path.Combine(currentAppPath, "UltimateKtv.Updater.exe");

                // If the extracted files contain UltimateKtv.Updater.*, attempt to overwrite existing files before launching
                bool preCopySuccess = false;
                string? extractedUpdater = Directory.GetFiles(extractPath, "UltimateKtv.Updater.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (!string.IsNullOrEmpty(extractedUpdater))
                {
                    string extractedUpdaterDir = Path.GetDirectoryName(extractedUpdater)!;
                    try
                    {
                        foreach (var file in Directory.GetFiles(extractedUpdaterDir, "UltimateKtv.Updater.*"))
                        {
                            string fileName = Path.GetFileName(file);
                            string destFile = Path.Combine(currentAppPath, fileName);
                            File.Copy(file, destFile, true);
                        }
                        preCopySuccess = true;
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Log($"無法預先覆蓋更新程式檔：{ex.Message}");
                    }
                }

                // If pre-copy failed or local updater doesn't exist, fallback to launching updater directly from extractPath
                if ((!preCopySuccess || !File.Exists(updaterPath)) && !string.IsNullOrEmpty(extractedUpdater) && File.Exists(extractedUpdater))
                {
                    updaterPath = extractedUpdater;
                }

                if (!File.Exists(updaterPath))
                {
                    throw new FileNotFoundException(Loc.T("Update.UpdaterMissing", updaterPath));
                }

                // Arguments: --pid [pid] --source [source] --dest [dest] --exe [exe]
                string arguments = $"--pid {currentPid} --source \"{extractPath.TrimEnd('\\')}\" --dest \"{currentAppPath.TrimEnd('\\')}\" --exe \"{currentExePath.TrimEnd('\\')}\"";

                try
                {
                    AppLogger.Log($"啟動更新管理員：{updaterPath} {arguments}");

                    ProcessStartInfo updaterStartInfo = new ProcessStartInfo(updaterPath, arguments)
                    {
                        UseShellExecute = true,
                        WorkingDirectory = currentAppPath,
                        Verb = "runas"
                    };

                    Process? p = Process.Start(updaterStartInfo);
                    if (p != null)
                    {
                        AppLogger.Log($"更新管理員已啟動，PID: {p.Id}");
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("無法啟動更新管理員", ex);
                    MessageBox.Show(Loc.T("Update.LaunchFailed", ex.Message), Loc.T("Update.ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    progressWindow?.Close();
                    MessageBox.Show(Loc.T("Update.Failed", ex.Message), Loc.T("Update.ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }
    }
}
