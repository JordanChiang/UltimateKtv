#nullable enable
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace UltimateKtv.Services
{
    /// <summary>
    /// Release 附檔 update.json 的內容：把發佈檔案分成數個元件 (主程式、Codec、Codec_x86、Tools)，
    /// 每個元件一個獨立 zip，並列出元件內每個檔案的 SHA256。
    /// 自動更新時只下載檔案雜湊與本機不同的元件；沒有 update.json 的舊 release 退回完整包。
    /// 清單與各元件 zip 由 scripts\pack-release.ps1 產生。
    /// </summary>
    public class UpdateManifest
    {
        [JsonProperty("version")]
        public string Version { get; set; } = string.Empty;

        [JsonProperty("tag")]
        public string Tag { get; set; } = string.Empty;

        [JsonProperty("components")]
        public List<UpdateComponent> Components { get; set; } = new List<UpdateComponent>();
    }

    /// <summary>更新清單裡的一個元件。</summary>
    public class UpdateComponent
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("desc")]
        public string Desc { get; set; } = string.Empty;

        /// <summary>此元件 zip 的下載網址。</summary>
        [JsonProperty("url")]
        public string Url { get; set; } = string.Empty;

        /// <summary>此元件 zip 的位元組大小 (用於進度顯示)。</summary>
        [JsonProperty("size")]
        public long Size { get; set; }

        /// <summary>相對於程式資料夾的路徑 (以 / 分隔) 對應小寫 SHA256。</summary>
        [JsonProperty("files")]
        public Dictionary<string, string> Files { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string DisplayName => string.IsNullOrEmpty(Desc) ? Name : Desc;
    }

    /// <summary>更新清單的純函式邏輯 (雜湊比對、決定要下載哪些元件)，不依賴 WPF / 網路，方便測試。</summary>
    public static class UpdateManifestHelper
    {
        /// <summary>
        /// 程式資料夾已有這些檔案時永遠保留不覆蓋，也不列入「元件有無變動」的判斷 (缺檔時仍會安裝一次)：
        /// 使用者的歌庫與 SongMgr 的歌庫/自訂語系。工具 exe 的保留另走 <see cref="ProtectedTools"/> 的「沒動過才覆蓋」。
        /// 套用更新的 Updater (Updater\Program.cs) 也有一份對應的跳過清單，兩邊要一起維護。
        /// </summary>
        public static readonly string[] KeepExisting =
        {
            "CrazySong.mdb",
            "CrazySong.db",
            @"SongMgr\CrazySongMgr.mdb",
            @"SongMgr\CrazySongMgr.db",
            @"SongMgr\Custom.lang",
        };

        /// <summary>
        /// 使用者可能自行更新的工具 exe，採「沒動過才覆蓋」：本機檔與「上次我們安裝的版本 (基準)」相同 (使用者沒動過)
        /// 才更新成新版；被使用者改過 (例如 yt-dlp -U、手動換 ffmpeg) 就保留。基準記在執行目錄的
        /// <see cref="BaselineFileName"/>。判斷與套用分別在 NeedsUpdate / DecideProtected，只走主程式自動更新路徑。
        /// ffplay/ffprobe、Codec/Codec_x86 不在此列，維持一般雜湊同步。
        /// </summary>
        public static readonly string[] ProtectedTools =
        {
            @"Tools\yt-dlp.exe",
            @"Tools\yt-dlp_x86.exe",
            @"Tools\ffmpeg.exe",
        };

        /// <summary>記錄受保護工具上次安裝版本雜湊的檔案 (執行目錄；相對路徑→sha256)。pack-release 會排除、不進發佈包。</summary>
        public const string BaselineFileName = "update-baseline.json";

        public static string LocalPath(string appDir, string relPath) =>
            Path.Combine(appDir, relPath.Replace('/', Path.DirectorySeparatorChar));

        public static bool IsKept(string relPath)
        {
            string rel = relPath.Replace('/', '\\');
            return KeepExisting.Any(k => string.Equals(k, rel, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsProtected(string relPath)
        {
            string rel = relPath.Replace('/', '\\');
            return ProtectedTools.Any(k => string.Equals(k, rel, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>基準字典的鍵：相對路徑一律以 / 分隔、小寫。</summary>
        public static string BaselineKey(string relPath) => relPath.Replace('\\', '/').ToLowerInvariant();

        /// <summary>讀取基準檔 (相對路徑→上次安裝的 sha256)；不存在或壞掉回傳空字典。</summary>
        public static Dictionary<string, string> LoadBaseline(string appDir)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = Path.Combine(appDir, BaselineFileName);
                if (!File.Exists(path)) return dict;
                var parsed = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
                if (parsed != null)
                    foreach (var kv in parsed)
                        if (!string.IsNullOrEmpty(kv.Value))
                            dict[BaselineKey(kv.Key)] = kv.Value.ToLowerInvariant();
            }
            catch { }
            return dict;
        }

        /// <summary>寫回基準檔；失敗 (例如唯讀目錄) 時忽略，不影響更新。</summary>
        public static void SaveBaseline(string appDir, Dictionary<string, string> baseline)
        {
            try
            {
                string path = Path.Combine(appDir, BaselineFileName);
                File.WriteAllText(path, JsonConvert.SerializeObject(baseline, Formatting.Indented));
            }
            catch { }
        }

        /// <summary>套用更新時，一個受保護工具 exe 的處置。</summary>
        public enum ProtectedAction
        {
            /// <summary>本機沒有 → 安裝。</summary>
            Install,
            /// <summary>本機 == 基準 (沒動過) 且與新版不同 → 覆蓋成新版。</summary>
            Overwrite,
            /// <summary>本機 == 新版 → 不用動 (只需把基準記成新版)。</summary>
            AlreadyCurrent,
            /// <summary>本機 ≠ 基準 (使用者改過) 或無基準 → 保留本機。</summary>
            Keep,
        }

        /// <summary>依「沒動過才覆蓋」決定受保護工具的處置 (純函式，方便測試)。</summary>
        public static ProtectedAction DecideProtected(bool localExists, string localHash, string newHash, string? baseHash)
        {
            if (!localExists) return ProtectedAction.Install;
            if (string.Equals(localHash, newHash, StringComparison.OrdinalIgnoreCase)) return ProtectedAction.AlreadyCurrent;
            if (baseHash != null && string.Equals(localHash, baseHash, StringComparison.OrdinalIgnoreCase)) return ProtectedAction.Overwrite;
            return ProtectedAction.Keep;
        }

        public static string FileHash(string file)
        {
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
            }
        }

        /// <summary>
        /// 元件裡有任何檔案不存在或雜湊值不同就需要更新。使用者已有的保留檔案 (KeepExisting) 不算；
        /// 受保護工具 (ProtectedTools) 只有在「本機==基準 (沒動過) 且與新版不同」時才算需要更新，
        /// 被使用者改過時不因它下載 (否則 yt-dlp -U 後會每次重抓整包 Tools)。
        /// </summary>
        public static bool NeedsUpdate(UpdateComponent comp, string appDir, IReadOnlyDictionary<string, string>? baseline = null)
        {
            foreach (var f in comp.Files)
            {
                string local = LocalPath(appDir, f.Key);
                bool exists = File.Exists(local);
                if (IsKept(f.Key) && exists) continue;
                if (!exists) return true;
                try
                {
                    string localHash = FileHash(local);
                    if (string.Equals(localHash, f.Value, StringComparison.OrdinalIgnoreCase)) continue; // 已是最新

                    if (IsProtected(f.Key))
                    {
                        // 沒動過 (本機==基準) 才需要更新；被改過或沒基準則保留、不因它下載。
                        if (baseline != null
                            && baseline.TryGetValue(BaselineKey(f.Key), out var baseHash)
                            && string.Equals(localHash, baseHash, StringComparison.OrdinalIgnoreCase))
                            return true;
                        continue;
                    }

                    return true; // 一般檔案雜湊不同 → 需更新
                }
                catch
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>解析 update.json；格式錯誤時回傳 null。</summary>
        public static UpdateManifest? Parse(string json)
        {
            try
            {
                var manifest = JsonConvert.DeserializeObject<UpdateManifest>(json);
                if (manifest == null || manifest.Components.Count == 0) return null;
                foreach (var c in manifest.Components)
                {
                    if (string.IsNullOrEmpty(c.Name) || string.IsNullOrEmpty(c.Url) || c.Files.Count == 0) return null;
                }
                return manifest;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>依清單挑出本機需要更新的元件 (保持清單順序)。baseline 給受保護工具的「沒動過才覆蓋」判斷。</summary>
        public static List<UpdateComponent> ChangedComponents(UpdateManifest manifest, string appDir, IReadOnlyDictionary<string, string>? baseline = null) =>
            manifest.Components.Where(c => NeedsUpdate(c, appDir, baseline)).ToList();
    }
}
