#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace UltimateKtv
{
    public static class ZipHelper
    {
        /// <summary>
        /// Safely extracts a zip archive supporting UTF-8 and Big5 fallback,
        /// while sanitizing illegal path characters to prevent extraction errors on different OS locales.
        /// </summary>
        public static void SafeExtractZip(string zipPath, string extractPath, Action<string>? onWarning = null)
        {
            if (!Directory.Exists(extractPath))
            {
                Directory.CreateDirectory(extractPath);
            }

            Encoding[] candidateEncodings = new[]
            {
                Encoding.UTF8,
                GetSafeEncoding(950) // Traditional Chinese Big5
            };

            Exception? lastEx = null;
            foreach (var encoding in candidateEncodings)
            {
                try
                {
                    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Read, encoding))
                    {
                        foreach (var entry in archive.Entries)
                        {
                            string cleanEntryPath = SanitizeRelativePath(entry.FullName);
                            if (string.IsNullOrWhiteSpace(cleanEntryPath)) continue;

                            if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\"))
                            {
                                string dirPath = Path.Combine(extractPath, cleanEntryPath);
                                if (!Directory.Exists(dirPath)) Directory.CreateDirectory(dirPath);
                                continue;
                            }

                            string destinationPath = Path.GetFullPath(Path.Combine(extractPath, cleanEntryPath));
                            string fullExtractPath = Path.GetFullPath(extractPath).TrimEnd('\\', '/') + "\\";
                            if (!destinationPath.StartsWith(fullExtractPath, StringComparison.OrdinalIgnoreCase))
                            {
                                // Zip slip protection
                                continue;
                            }

                            string? destinationDir = Path.GetDirectoryName(destinationPath);
                            if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
                            {
                                Directory.CreateDirectory(destinationDir);
                            }

                            try
                            {
                                entry.ExtractToFile(destinationPath, true);
                            }
                            catch (Exception ex)
                            {
                                onWarning?.Invoke($"[略過異常檔案] {cleanEntryPath}: {ex.Message}");
                            }
                        }
                    }
                    return; // Success
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                }
            }

            if (lastEx != null)
            {
                throw new InvalidOperationException($"解壓縮檔案失敗：{lastEx.Message}", lastEx);
            }
        }

        public static string SanitizeRelativePath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return string.Empty;

            string normalized = relativePath.Replace('/', '\\').TrimStart('\\');
            var segments = normalized.Split(new[] { '\\' }, StringSplitOptions.None);
            var cleanSegments = new List<string>();

            char[] invalidChars = Path.GetInvalidFileNameChars();

            foreach (var seg in segments)
            {
                if (string.IsNullOrEmpty(seg) || seg == ".") continue;
                if (seg == "..") continue;

                var sb = new StringBuilder(seg.Length);
                foreach (char c in seg)
                {
                    if (invalidChars.Contains(c))
                    {
                        sb.Append('_');
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                cleanSegments.Add(sb.ToString().Trim());
            }

            return string.Join("\\", cleanSegments);
        }

        public static Encoding GetSafeEncoding(int codepage)
        {
            try
            {
                return Encoding.GetEncoding(codepage);
            }
            catch
            {
                return Encoding.Default;
            }
        }
    }
}
