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
        /// 程式資料夾已有這些檔案時保留不覆蓋 (使用者的歌庫與 SongMgr 的歌庫/自訂語系)，也不列入「元件有無變動」的判斷。
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

        public static string LocalPath(string appDir, string relPath) =>
            Path.Combine(appDir, relPath.Replace('/', Path.DirectorySeparatorChar));

        public static bool IsKept(string relPath)
        {
            string rel = relPath.Replace('/', '\\');
            return KeepExisting.Any(k => string.Equals(k, rel, StringComparison.OrdinalIgnoreCase));
        }

        public static string FileHash(string file)
        {
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
            }
        }

        /// <summary>元件裡有任何檔案不存在或雜湊值不同就需要更新 (使用者已有的保留檔案不算)。</summary>
        public static bool NeedsUpdate(UpdateComponent comp, string appDir)
        {
            foreach (var f in comp.Files)
            {
                string local = LocalPath(appDir, f.Key);
                bool exists = File.Exists(local);
                if (IsKept(f.Key) && exists) continue;
                if (!exists) return true;
                try
                {
                    if (!string.Equals(FileHash(local), f.Value, StringComparison.OrdinalIgnoreCase)) return true;
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

        /// <summary>依清單挑出本機需要更新的元件 (保持清單順序)。</summary>
        public static List<UpdateComponent> ChangedComponents(UpdateManifest manifest, string appDir) =>
            manifest.Components.Where(c => NeedsUpdate(c, appDir)).ToList();
    }
}
