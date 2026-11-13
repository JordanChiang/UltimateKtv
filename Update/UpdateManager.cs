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
        // 加歌程式 SongMgr 的獨立 repo：UltimateKtv 沒 bundle SongMgr 時，從這裡補下載 (見 BuildSongMgrPlanAsync)。
        private const string SongMgrReleaseUrl = "https://api.github.com/repos/JordanChiang/SongMgr/releases/latest";
        private const string SongMgrComponentName = "SongMgr";
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

                // 5. 準備下載清單
                //    UltimateKtv 本身：有新版才下載 (差異更新優先，無 update.json 退回完整包)。
                //    SongMgr：UltimateKtv 沒 bundle 時，獨立從 SongMgr repo 補 (不受 UltimateKtv 是否有新版影響，
                //    所以不在這裡用 latestVersion <= currentVersion 提早 return)。
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                bool ultimateKtvNewer = latestVersion > currentVersion;
                // 受保護工具 (yt-dlp/yt-dlp_x86/ffmpeg) 的「上次安裝基準」，用於「沒動過才覆蓋」判斷。
                var baseline = UpdateManifestHelper.LoadBaseline(appDir);

                DownloadPlan plan = latestRelease != null && latestRelease.Assets.Count > 0
                    ? await BuildDownloadPlanAsync(latestRelease, appDir, baseline)
                    : new DownloadPlan();
                if (plan.Abort) return; // UltimateKtv 的 update.json 壞掉，這次整個略過

                var items = new List<DownloadItem>();
                if (ultimateKtvNewer) items.AddRange(plan.Items);

                // SongMgr 補充 (維持 bundled，沒 bundle 才從 repo 補)：只在 UltimateKtv 走差異更新
                // (有 update.json) 且清單沒含 SongMgr 元件時。
                if (plan.UsedManifest && !plan.HasSongMgrComponent)
                {
                    List<DownloadItem>? songMgr = await BuildSongMgrPlanAsync(appDir, baseline);
                    if (songMgr != null) items.AddRange(songMgr);
                }

                if (items.Count == 0)
                {
                    if (ultimateKtvNewer && apiUrl == DefaultUpdateUrl)
                        AppLogger.Log("找到了新版本，但找不到可用的更新附檔 (update.json 或完整包)。");
                    return;
                }

                // 6. Ask the user, then perform the update
                //    UltimateKtv 有新版就顯示新版本號與其更新說明；只有 SongMgr 要補時顯示目前版本 + SongMgr 說明。
                string shownVersion = ultimateKtvNewer ? latestVersion!.ToString() : currentVersion.ToString();
                string shownNotes = ultimateKtvNewer ? releaseNotes : Loc.T("Update.SongMgrNotes");

                bool userWantsUpdate = false;
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var prompt = new UpdatePromptWindow(currentVersion.ToString(), shownVersion, shownNotes);
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

        /// <summary>UltimateKtv 本身的下載計畫，附帶 update.json 的狀態 (供 SongMgr 補充判斷)。</summary>
        private sealed class DownloadPlan
        {
            /// <summary>要下載的元件 (差異更新) 或完整包。</summary>
            public List<DownloadItem> Items { get; } = new List<DownloadItem>();
            /// <summary>此 release 有 update.json (走差異更新)。</summary>
            public bool UsedManifest { get; set; }
            /// <summary>update.json 已含 SongMgr 元件 (bundled)，不需另外從 SongMgr repo 補。</summary>
            public bool HasSongMgrComponent { get; set; }
            /// <summary>update.json 存在卻讀不到 / 格式錯誤：這次整個略過更新。</summary>
            public bool Abort { get; set; }
        }

        /// <summary>
        /// 依 release 決定 UltimateKtv 要下載哪些包：有 update.json 時只挑雜湊有變的元件；沒有時退回完整包。
        /// 同時回報是否走差異更新、是否已 bundle SongMgr，供呼叫端決定要不要另外補 SongMgr。
        /// </summary>
        private static async Task<DownloadPlan> BuildDownloadPlanAsync(GitHubReleaseInfo release, string appDir, IReadOnlyDictionary<string, string> baseline)
        {
            var plan = new DownloadPlan();
            var manifestAsset = release.Assets.FirstOrDefault(
                a => a.Name.Equals(ManifestAssetName, StringComparison.OrdinalIgnoreCase));

            if (manifestAsset != null && !string.IsNullOrEmpty(manifestAsset.BrowserDownloadUrl))
            {
                plan.UsedManifest = true;
                try
                {
                    string json = await _httpClient.GetStringAsync(manifestAsset.BrowserDownloadUrl);
                    UpdateManifest? manifest = UpdateManifestHelper.Parse(json);
                    if (manifest != null)
                    {
                        plan.HasSongMgrComponent = manifest.Components.Any(
                            c => c.Name.Equals(SongMgrComponentName, StringComparison.OrdinalIgnoreCase));
                        // 比對檔案雜湊可能較耗時，移出 UI 執行緒
                        List<UpdateComponent> changed = await Task.Run(
                            () => UpdateManifestHelper.ChangedComponents(manifest, appDir, baseline));
                        foreach (var c in changed)
                            plan.Items.Add(new DownloadItem(c.Url, c.DisplayName, c.Size));
                        return plan;
                    }

                    // 有 update.json 卻讀不到或格式錯誤：這次先不更新 (下次啟動再檢查)，不改抓完整包
                    AppLogger.Log("更新清單 update.json 格式錯誤，略過這次更新。");
                    plan.Abort = true;
                    return plan;
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"無法取得更新清單 update.json：{ex.Message}");
                    plan.Abort = true;
                    return plan;
                }
            }

            // 沒有 update.json 的舊 release：下載完整包
            GitHubReleaseAsset? full = PickFullPackage(release.Assets);
            if (full != null && !string.IsNullOrEmpty(full.BrowserDownloadUrl))
                plan.Items.Add(new DownloadItem(full.BrowserDownloadUrl, full.Name, 0));
            return plan;
        }

        /// <summary>
        /// 從 SongMgr 自己的 repo (JordanChiang/SongMgr) 補加歌程式：讀它的 update.json，
        /// 依雜湊挑出本機需要更新的元件 (App 與共用的 Codec_x86 / Tools)。SongMgr 與 UltimateKtv 共用
        /// Codec_x86 / Tools，雜湊相同的會自動跳過 (沿用 UltimateKtv 的)；不同或缺檔 (例如 ffplay/ffprobe)
        /// 才下載。使用者既有的 SongMgr 歌庫等保留檔 (KeepExisting) 不列入變動判斷、套用時也不會被覆蓋。
        /// 取不到時回傳 null (不影響 UltimateKtv 本身的更新)。
        /// </summary>
        private static async Task<List<DownloadItem>?> BuildSongMgrPlanAsync(string appDir, IReadOnlyDictionary<string, string> baseline)
        {
            try
            {
                var response = await _httpClient.GetAsync(SongMgrReleaseUrl);
                if (!response.IsSuccessStatusCode) return null;

                string content = await response.Content.ReadAsStringAsync();
                var release = JsonConvert.DeserializeObject<GitHubReleaseInfo>(content);
                if (release == null || release.Assets.Count == 0) return null;

                var manifestAsset = release.Assets.FirstOrDefault(
                    a => a.Name.Equals(ManifestAssetName, StringComparison.OrdinalIgnoreCase));
                if (manifestAsset == null || string.IsNullOrEmpty(manifestAsset.BrowserDownloadUrl)) return null;

                string json = await _httpClient.GetStringAsync(manifestAsset.BrowserDownloadUrl);
                UpdateManifest? manifest = UpdateManifestHelper.Parse(json);
                if (manifest == null)
                {
                    AppLogger.Log("SongMgr 的 update.json 格式錯誤，這次略過補 SongMgr。");
                    return null;
                }

                List<UpdateComponent> changed = await Task.Run(
                    () => UpdateManifestHelper.ChangedComponents(manifest, appDir, baseline));
                return changed
                    .Select(c => new DownloadItem(c.Url, c.DisplayName, c.Size))
                    .ToList();
            }
            catch (Exception ex)
            {
                AppLogger.Log($"無法取得 SongMgr 更新：{ex.Message}");
                return null;
            }
        }

        /// <summary>完整安裝包 zip：優先認 UltimateKtv_&lt;版本&gt;.zip，避免誤抓元件 zip (Codec_*.zip…)；舊 release 退回第一個 .zip。</summary>
        private static GitHubReleaseAsset? PickFullPackage(List<GitHubReleaseAsset> assets)
        {
            var full = assets.FirstOrDefault(
                a => Regex.IsMatch(a.Name ?? "", @"^UltimateKtv_[\d.]+\.zip$", RegexOptions.IgnoreCase));
            if (full != null) return full;
            return assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 套用「沒動過才覆蓋」的受保護工具 (ProtectedTools)：比對本機檔與上次安裝基準，沒動過 (本機==基準)
        /// 才覆蓋成解壓夾裡的新版，被使用者改過就保留；無基準時保留並收當下雜湊為基準。處理完的檔案一律從解壓夾
        /// 移除，讓後續的 Updater 不再碰這些工具。失敗 (檔案鎖定等) 時保留本機、不動基準，下次再試。
        /// </summary>
        private static void ApplyProtectedTools(string extractPath, string appDir)
        {
            Dictionary<string, string> baseline = UpdateManifestHelper.LoadBaseline(appDir);
            bool changed = false;

            foreach (string rel in UpdateManifestHelper.ProtectedTools)
            {
                string src = Path.Combine(extractPath, rel);
                if (!File.Exists(src)) continue; // 這次沒下載到這個工具

                string dst = Path.Combine(appDir, rel);
                string key = UpdateManifestHelper.BaselineKey(rel);
                string newHash;
                try { newHash = UpdateManifestHelper.FileHash(src); }
                catch { continue; }

                bool exists = File.Exists(dst);
                string? localHash = null;
                if (exists)
                {
                    try { localHash = UpdateManifestHelper.FileHash(dst); }
                    catch { localHash = null; }
                }
                baseline.TryGetValue(key, out string? baseHash);

                switch (UpdateManifestHelper.DecideProtected(exists, localHash ?? "", newHash, baseHash))
                {
                    case UpdateManifestHelper.ProtectedAction.Install:
                    case UpdateManifestHelper.ProtectedAction.Overwrite:
                        try
                        {
                            string? dstDir = Path.GetDirectoryName(dst);
                            if (!string.IsNullOrEmpty(dstDir)) Directory.CreateDirectory(dstDir);
                            File.Copy(src, dst, true);
                            baseline[key] = newHash;
                            changed = true;
                            TryDelete(src);
                        }
                        catch (Exception ex)
                        {
                            // 失敗就保留本機、不動基準、不刪 src (Updater 保留清單會跳過已存在的檔)，下次再試。
                            AppLogger.Log($"更新工具 {rel} 失敗，保留現有檔：{ex.Message}");
                        }
                        break;

                    case UpdateManifestHelper.ProtectedAction.AlreadyCurrent:
                        baseline[key] = newHash;
                        changed = true;
                        TryDelete(src);
                        break;

                    case UpdateManifestHelper.ProtectedAction.Keep:
                        // 使用者改過：保留本機。無基準時收當下本機雜湊為基準 (之後沒再動就會跟著更新)。
                        if (baseHash == null && localHash != null)
                        {
                            baseline[key] = localHash;
                            changed = true;
                        }
                        TryDelete(src);
                        break;
                }
            }

            if (changed) UpdateManifestHelper.SaveBaseline(appDir, baseline);
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
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

                // 受保護工具 (yt-dlp/yt-dlp_x86/ffmpeg) 的「沒動過才覆蓋」：由主程式在這裡直接處理並從解壓夾移除，
                // 交給 Updater 的檔案就不含這些 (Updater 只負責一般檔與使用者資料的保留)。
                await Task.Run(() => ApplyProtectedTools(extractPath, AppDomain.CurrentDomain.BaseDirectory));

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
