using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace UltimateKtv.Services
{
    public static class UpdateManager
    {
        private const string DefaultUpdateUrl = "https://api.github.com/repos/JordanChiang/UltimateKtv/releases/latest";
        private static readonly HttpClient _httpClient;

        static UpdateManager()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "UltimateKtv-UpdateClient");
            // Set timeout relatively short for version check so it doesn't hang startup
            _httpClient.Timeout = TimeSpan.FromSeconds(15);
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
                string? downloadUrl = null;

                // 4. Parse version info (Try GitHub JSON format first, then plain text)
                try
                {
                    var latestRelease = JsonConvert.DeserializeObject<GitHubReleaseInfo>(content);
                    if (latestRelease != null && !string.IsNullOrEmpty(latestRelease.TagName))
                    {
                        if (Version.TryParse(latestRelease.TagName.TrimStart('v', 'V'), out latestVersion))
                        {
                            releaseNotes = latestRelease.Body;
                            var zipAsset = latestRelease.Assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
                            downloadUrl = zipAsset?.BrowserDownloadUrl;
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
                if (latestVersion > currentVersion)
                {
                    // If we have a download URL, proceed to prompt
                    if (!string.IsNullOrEmpty(downloadUrl))
                    {
                        bool userWantsUpdate = false;
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            var prompt = new UpdatePromptWindow(currentVersion.ToString(), latestVersion.ToString(), releaseNotes);
                            prompt.Owner = Application.Current.MainWindow;
                            prompt.ShowDialog();
                            userWantsUpdate = prompt.DoUpdate;
                        });

                        if (userWantsUpdate)
                        {
                            await PerformUpdateAsync(downloadUrl);
                        }
                    }
                    else if (apiUrl == DefaultUpdateUrl)
                    {
                        // If it's the default GitHub repo but we couldn't find a zip, something is wrong
                        AppLogger.Log("找到了新版本，但找不到對應的 ZIP 安裝包。");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Update Check Failed: {ex.Message}");
            }
        }

        private static async Task PerformUpdateAsync(string downloadUrl)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "UltimateKtv_Updater");
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
            Directory.CreateDirectory(tempDir);

            string zipFilePath = Path.Combine(tempDir, "update.zip");
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
                // Download file with progress
                using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength;

                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(zipFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        var totalRead = 0L;
                        var buffer = new byte[8192];
                        var isMoreToRead = true;

                        do
                        {
                            var read = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                            if (read == 0)
                            {
                                isMoreToRead = false;
                            }
                            else
                            {
                                await fileStream.WriteAsync(buffer, 0, read);
                                totalRead += read;

                                if (totalBytes.HasValue && totalBytes.Value > 0)
                                {
                                    double percentage = (double)totalRead / totalBytes.Value * 100;
                                    progressWindow?.ReportProgress(percentage, $"正在下載: {totalRead / 1024 / 1024} MB / {totalBytes.Value / 1024 / 1024} MB");
                                }
                            }
                        } while (isMoreToRead);
                    }
                }

                progressWindow?.ReportProgress(100, "下載完成，正在解壓縮檔案...");

                // Extract Zip
                await Task.Run(() =>
                {
                    ZipFile.ExtractToDirectory(zipFilePath, extractPath, true);
                });

                progressWindow?.ReportProgress(100, "準備套用更新...");

                // Hide progress window right before triggering restart
                await Application.Current.Dispatcher.InvokeAsync(() => progressWindow?.Close());

                // Create update command line for the standalone WPF Updater
                string currentAppPath = AppDomain.CurrentDomain.BaseDirectory;
                string currentExePath = Process.GetCurrentProcess().MainModule?.FileName ?? "UltimateKtv.exe";
                int currentPid = Process.GetCurrentProcess().Id;
                
                // Locate the standalone updater exe (should be in the same folder)
                string updaterPath = Path.Combine(currentAppPath, "UltimateKtv.Updater.exe");
                
                if (!File.Exists(updaterPath))
                {
                    throw new FileNotFoundException($"找不到更新程式：{updaterPath}");
                }

                // Arguments: --pid [pid] --source [source] --dest [dest] --exe [exe]
                string arguments = $"--pid {currentPid} --source \"{extractPath}\" --dest \"{currentAppPath.TrimEnd('\\')}\" --exe \"{currentExePath}\"";

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
                    MessageBox.Show($"無法啟動更新管理員：{ex.Message}", "更新錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                    return; 
                }

                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    progressWindow?.Close();
                    MessageBox.Show($"更新處理失敗：{ex.Message}", "更新錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }
    }
}
