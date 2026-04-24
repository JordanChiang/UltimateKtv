using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace UltimateKtv.Updater
{
    class Program
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        static async Task Main(string[] args)
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "UltimateKtv-Updater-Independent");
            
            Console.Title = "UltimateKtv 更新管理員";
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("====================================================");
            Console.WriteLine("=             UltimateKtv 更新管理系統        V1.0 =");
            Console.WriteLine("====================================================");
            Console.ResetColor();
            Console.WriteLine();

            if (!TryParseArgs(args, out int targetPid, out string sourceDir, out string destDir, out string targetExe))
            {
                // Independent Mode: Check for updates manually
                try
                {
                    await RunIndependentUpdateCheck();
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\n[錯誤] 無法啟動獨立更新檢查：{ex.Message}");
                    Console.ResetColor();
                    if (ex.InnerException != null) Console.WriteLine($"詳情: {ex.InnerException.Message}");
                    Console.WriteLine("\n請按任意鍵退出...");
                    Console.ReadKey();
                }
                return;
            }

            try
            {
                await RunUpdateProcess(targetPid, sourceDir, destDir, targetExe);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[錯誤] 更新過程中發生問題：{ex.Message}");
                Console.ResetColor();
                Console.WriteLine("\n請嘗試手動解壓更新檔案，或重新啟動程式。");
                Console.WriteLine("請按任意鍵退出...");
                Console.ReadKey();
            }
        }

        static async Task RunIndependentUpdateCheck()
        {
            string currentDir = AppDomain.CurrentDomain.BaseDirectory;
            string repoPath = "JordanChiang/UltimateKtv"; // Use default repo when launched standalone

            // 1. Detect Main EXE
            string targetExe = Path.Combine(currentDir, "UltimateKtv.exe");
            if (!File.Exists(targetExe)) targetExe = Path.Combine(currentDir, "UltimateKtv_x86.exe");
            
            bool isFreshInstall = !File.Exists(targetExe);
            if (isFreshInstall)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[資訊] 找不到主程式，將為您執行完整下載與安裝。");
                Console.ResetColor();
                targetExe = Path.Combine(currentDir, "UltimateKtv.exe"); // Default for new install
            }

            Console.WriteLine($"[1/5] 正在從預設來源檢查更新 ({repoPath})...");
            
            // 2. Construct API URL
            string apiUrl = $"https://api.github.com/repos/{repoPath}/releases/latest";
            
            var response = await _httpClient.GetAsync(apiUrl);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"      [失敗] 無法連線至更新伺服器 ({response.StatusCode})。");
                Console.WriteLine("\n請按任意鍵退出...");
                Console.ReadKey();
                return;
            }

            var content = await response.Content.ReadAsStringAsync();
            var release = JsonSerializer.Deserialize<ReleaseInfo>(content);

            if (release == null || string.IsNullOrEmpty(release.TagName))
            {
                Console.WriteLine("      [失敗] 無法讀取更新資訊。");
                Console.WriteLine("\n請按任意鍵退出...");
                Console.ReadKey();
                return;
            }

            // 3. Compare Version
            if (!isFreshInstall)
            {
                Version latestVersion = new Version(release.TagName.TrimStart('v', 'V'));
                Version currentVersion = new Version(FileVersionInfo.GetVersionInfo(targetExe).FileVersion ?? "0.0.0.0");

                Console.WriteLine($"      目前版本: {currentVersion}");
                Console.WriteLine($"      最新版本: {latestVersion}");

                if (latestVersion <= currentVersion)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("\n[資訊] 您目前已是最新版本，無需更新。");
                    Console.ResetColor();
                    Console.WriteLine("\n請按任意鍵退出...");
                    Console.ReadKey();
                    return;
                }

                Console.WriteLine($"\n[發現新版本] {release.TagName}");
            }
            else
            {
                Console.WriteLine($"      最新可用版本: {release.TagName}");
            }

            Console.WriteLine(release.Body);
            Console.WriteLine(isFreshInstall ? "\n是否立即下載並安裝？ (Y/N)" : "\n是否立即下載並更新？ (Y/N)");
            var key = Console.ReadKey();
            if (key.Key != ConsoleKey.Y) return;
            Console.WriteLine();

            // 4. Download
            var zipAsset = release.Assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (zipAsset == null)
            {
                Console.WriteLine("      [失敗] 找不到更新壓縮檔。");
                return;
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "UltimateKtv_ManualUpdate");
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            Directory.CreateDirectory(tempDir);

            string zipPath = Path.Combine(tempDir, "update.zip");
            string extractPath = Path.Combine(tempDir, "extracted");

            Console.WriteLine($"[2/5] 正在下載更新檔...");
            using (var downloadResponse = await _httpClient.GetAsync(zipAsset.BrowserDownloadUrl))
            {
                using (var fs = new FileStream(zipPath, FileMode.Create))
                {
                    await downloadResponse.Content.CopyToAsync(fs);
                }
            }

            // 5. Extract
            Console.WriteLine("[3/5] 正在解壓縮...");
            ZipFile.ExtractToDirectory(zipPath, extractPath, true);

            // 6. Check if app is running
            int targetPid = 0;
            string exeName = Path.GetFileNameWithoutExtension(targetExe);
            var processes = Process.GetProcessesByName(exeName);
            if (processes.Length > 0)
            {
                Console.WriteLine("\n[注意] 主程式正在執行中，必須關閉才能更新。");
                Console.WriteLine("是否嘗試自動關閉主程式？ (Y/N)");
                if (Console.ReadKey().Key != ConsoleKey.Y) return;
                
                targetPid = processes[0].Id;
                foreach (var p in processes)
                {
                    try { p.CloseMainWindow(); p.WaitForExit(3000); if (!p.HasExited) p.Kill(); } catch { }
                }
            }

            // 7. Run standard update process
            await RunUpdateProcess(targetPid, extractPath, currentDir, targetExe);
        }

        public class ReleaseInfo
        {
            [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
            [JsonPropertyName("body")] public string Body { get; set; } = "";
            [JsonPropertyName("assets")] public List<AssetInfo> Assets { get; set; } = new List<AssetInfo>();
        }

        public class AssetInfo
        {
            [JsonPropertyName("name")] public string Name { get; set; } = "";
            [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = "";
        }

        static bool TryParseArgs(string[] args, out int targetPid, out string sourceDir, out string destDir, out string targetExe)
        {
            targetPid = 0;
            sourceDir = "";
            destDir = "";
            targetExe = "";

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--pid" && i + 1 < args.Length) targetPid = int.Parse(args[++i]);
                else if (args[i] == "--source" && i + 1 < args.Length) sourceDir = args[++i];
                else if (args[i] == "--dest" && i + 1 < args.Length) destDir = args[++i];
                else if (args[i] == "--exe" && i + 1 < args.Length) targetExe = args[++i];
            }

            return targetPid != 0 && !string.IsNullOrEmpty(sourceDir) && !string.IsNullOrEmpty(destDir) && !string.IsNullOrEmpty(targetExe);
        }

        static async Task RunUpdateProcess(int targetPid, string sourceDir, string destDir, string targetExe)
        {
            // 1. Wait for process to exit
            if (targetPid > 0)
            {
                Console.WriteLine("[4/5] 正在等待主程式關閉...");
                try
                {
                    var process = Process.GetProcessById(targetPid);
                    while (!process.HasExited)
                    {
                        await Task.Delay(500);
                    }
                }
                catch (ArgumentException) { /* Process already exited */ }
                Console.WriteLine("      [OK] 主程式已關閉。");
            }

            // 2. Small delay for file release
            await Task.Delay(1000);

            // 3. Copy files
            Console.WriteLine("[5/5] 正在複製更新檔案...");
            var files = Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories);
            int totalFiles = files.Length;
            int copiedFiles = 0;

            foreach (string file in files)
            {
                string fileName = Path.GetFileName(file);
                
                // Skip the updater's own files to prevent "file in use" errors
                if (fileName.Equals("UltimateKtv.Updater.exe", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("UltimateKtv.Updater.dll", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("UltimateKtv.Updater.pdb", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("UltimateKtv.Updater.deps.json", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("UltimateKtv.Updater.runtimeconfig.json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string relativePath = Path.GetRelativePath(sourceDir, file);
                string destFile = Path.Combine(destDir, relativePath);
                string destFolder = Path.GetDirectoryName(destFile)!;

                if (!Directory.Exists(destFolder)) Directory.CreateDirectory(destFolder);

                int retry = 0;
                while (retry < 5)
                {
                    try
                    {
                        File.Copy(file, destFile, true);
                        break;
                    }
                    catch (IOException)
                    {
                        retry++;
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"      [重試 {retry}/5] 檔案佔用中: {Path.GetFileName(destFile)}");
                        Console.ResetColor();
                        await Task.Delay(1000);
                        if (retry == 5) throw new IOException($"無法覆蓋檔案: {destFile}. 請確保所有相關程式已關閉。");
                    }
                }

                copiedFiles++;
                int percentage = (int)((double)copiedFiles / totalFiles * 100);
                if (percentage % 10 == 0 || copiedFiles == totalFiles)
                {
                    Console.Write($"\r      正在處理：{percentage}% ({copiedFiles}/{totalFiles})");
                }
            }
            Console.WriteLine("\n      [OK] 檔案複製完成。");

            // 4. Restart app
            Console.WriteLine("\n正在重新啟動應用程式...");
            await Task.Delay(1000);

            ProcessStartInfo startInfo = new ProcessStartInfo(targetExe)
            {
                WorkingDirectory = destDir,
                UseShellExecute = true
            };
            Process.Start(startInfo);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n[成功] 更新已完成，祝您歌唱愉快！");
            Console.ResetColor();
            await Task.Delay(1500);
        }
    }
}
