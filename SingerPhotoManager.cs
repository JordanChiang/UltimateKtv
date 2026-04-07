using System;
using System.IO;
using System.Linq;

namespace UltimateKtv
{
    public static class SingerPhotoManager
    {
        private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp" };
        private const string DefaultPhotoPath = "/Images/FavoriteUser/favoriteuser_default.png";
        private const string MissingSingersLogFile = "MissingSingerPhotos.txt";

        // Cache for photo lookups: Normalized Folder Path -> (SingerName -> Full Path)
        private static System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>> _foldersCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _cacheLock = new object();

        private static string NormalizeName(string? name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            return name.Replace('（', '(').Replace('）', ')')
                       .Replace('［', '[').Replace('］', ']')
                       .Replace(" ", "").Replace("　", "") // Remove all half-width and full-width spaces
                       .Trim();
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            try
            {
                // Ensure absolute path and remove trailing slashes
                string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return fullPath;
            }
            catch
            {
                return path;
            }
        }
        
        /// <summary>
        /// Gets or sets the current subfolder for photos (e.g., "Avatar" or "FavoriteUser").
        /// Defaults to "Avatar".
        /// </summary>
        public static string CurrentSubFolder { get; set; } = "SingerAvatar";

        /// <summary>
        /// Checks if the singer photo directory exists and contains at least one image.
        /// Also ensures the cache is initialized if photos are available.
        /// </summary>
        public static bool HasAnyPhotos()
        {
            var settings = SettingsManager.Instance.CurrentSettings;
            string basePath = settings.SingerPhotoPath;
            if (string.IsNullOrEmpty(basePath)) return false;

            try
            {
                basePath = Path.Combine(basePath, CurrentSubFolder);

                if (!Path.IsPathRooted(basePath))
                {
                    basePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, basePath);
                }

                string normalizedPath = NormalizePath(basePath);
                
                // Auto-initialize cache if needed
                lock (_cacheLock)
                {
                    if (!_foldersCache.ContainsKey(normalizedPath))
                    {
                        InitializeCache(basePath);
                    }
                    
                    if (_foldersCache.TryGetValue(normalizedPath, out var cache))
                    {
                        return cache.Count > 0;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Scans the photo directory once and builds a fast lookup dictionary.
        /// </summary>
        public static void InitializeCache(string? path = null)
        {
            lock (_cacheLock)
            {
                try
                {
                    var settings = SettingsManager.Instance.CurrentSettings;
                    string? basePath = path ?? settings.SingerPhotoPath;

                    if (string.IsNullOrEmpty(basePath)) return;
                    
                    if (path == null) // If no explicit path provided, use the root + current subfolder
                    {
                        basePath = Path.Combine(basePath, CurrentSubFolder);
                    }

                    if (!Path.IsPathRooted(basePath))
                    {
                        basePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, basePath);
                    }

                    string normalizedPath = NormalizePath(basePath);

                    if (!Directory.Exists(basePath))
                    {
                        AppLogger.Log($"InitializeCache: Directory not found: {basePath}");
                        _foldersCache[normalizedPath] = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        return;
                    }

                    var newCache = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    
                    // Enumerate files once
                    var files = Directory.EnumerateFiles(basePath, "*.*", SearchOption.TopDirectoryOnly);
                    foreach (var file in files)
                    {
                        string ext = Path.GetExtension(file).ToLower();
                        if (ImageExtensions.Contains(ext))
                        {
                            string? name = Path.GetFileNameWithoutExtension(file);
                            string normalizedName = NormalizeName(name);
                            // Store the first one we find for this name
                            if (!string.IsNullOrEmpty(normalizedName) && !newCache.ContainsKey(normalizedName))
                            {
                                newCache[normalizedName] = file; // Store raw local path
                            }
                        }
                    }

                    _foldersCache[normalizedPath] = newCache;
                    
                    AppLogger.Log($"Singer photo cache initialized with {newCache.Count} images from {basePath}");
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("Failed to initialize singer photo cache", ex);
                }
            }
        }

        /// <summary>
        /// Scans all singers and records those with missing photos to the log file.
        /// This is intended to be called at application startup.
        /// </summary>
        public static void RecordMissingSinger()
        {
            try
            {
                var settings = SettingsManager.Instance.CurrentSettings;
                if (!settings.VisualSingerStyle) return;

                string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, MissingSingersLogFile);

                // If MissingSingersLogFile exist, skip actions
                if (File.Exists(logFilePath))
                {
                    return;
                }

                if (SongDatas.SingerData == null || !SongDatas.SingerData.Any())
                {
                    AppLogger.Log("RecordMissingSinger: No singer data available to scan.");
                    return;
                }

                AppLogger.Log("Starting to scan for missing singer photos...");
                var sw = System.Diagnostics.Stopwatch.StartNew();

                // Get all distinct singer names
                var allSingerNames = SongDatas.SingerData
                    .Select(s => s.TryGetValue("Singer_Name", out var nameObj) ? nameObj?.ToString() ?? "" : "")
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Distinct()
                    .ToList();

                var missingSingers = new System.Collections.Generic.List<string>();

                foreach (var name in allSingerNames)
                {
                    if (GetPhotoLocalPath(name) == null)
                    {
                        missingSingers.Add(name);
                    }
                }

                if (missingSingers.Any())
                {
                    // Ensure the Logs directory exists
                    string? logDir = Path.GetDirectoryName(logFilePath);
                    if (logDir != null && !Directory.Exists(logDir))
                    {
                        Directory.CreateDirectory(logDir);
                    }

                    // Write all missing singers to the file at once
                    File.WriteAllLines(logFilePath, missingSingers.OrderBy(n => n));
                    AppLogger.Log($"Finished scanning for missing singer photos. Found {missingSingers.Count} missing photos. Recorded to {MissingSingersLogFile}.");
                }
                else
                {
                    // Even if none are missing, we might want to create an empty file or just log it.
                    // Requirement says "create MissingSingersLogFile and scan ... then record if photo file not exist".
                    // I'll create an empty file so it doesn't scan again next time.
                    File.WriteAllText(logFilePath, "");
                    AppLogger.Log("Finished scanning for missing singer photos. None were found.");
                }

                sw.Stop();
                AppLogger.Log($"RecordMissingSinger elapsed time: {sw.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Failed during RecordMissingSinger bulk scan", ex);
            }
        }

        /// <summary>
        /// Records a singer name to the missing singers log file if not already recorded.
        /// </summary>
        private static void RecordMissingSinger(string singerName)
        {
            try
            {
                string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, MissingSingersLogFile);
                
                // Ensure the Logs directory exists
                string? logDir = Path.GetDirectoryName(logFilePath);
                if (logDir != null && !Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                // Check if the singer name is already in the file to avoid duplicates
                bool alreadyRecorded = false;
                if (File.Exists(logFilePath))
                {
                    var existingLines = File.ReadAllLines(logFilePath);
                    alreadyRecorded = existingLines.Any(line => 
                        line.Trim().Equals(singerName, StringComparison.OrdinalIgnoreCase));
                }

                // Append the singer name if not already recorded
                if (!alreadyRecorded)
                {
                    File.AppendAllText(logFilePath, singerName + Environment.NewLine);
                    AppLogger.Log($"Recorded missing singer photo: {singerName}");
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"Failed to record missing singer: {singerName}", ex);
            }
        }

        /// <summary>
        /// Gets the photo URI for a singer, supporting chorus singers by splitting names.
        /// </summary>
        public static string GetPhoto(string? singerName)
        {
            string? localPath = GetPhotoLocalPath(singerName);
            if (localPath == null) return DefaultPhotoPath;

            try
            {
                return new Uri(localPath).AbsoluteUri;
            }
            catch
            {
                return DefaultPhotoPath;
            }
        }

        /// <summary>
        /// Gets the local file path for a singer photo. Returns null if not found.
        /// </summary>
        public static string? GetPhotoLocalPath(string? singerName)
        {
            if (string.IsNullOrEmpty(singerName)) return null;

            var settings = SettingsManager.Instance.CurrentSettings;
            string? basePath = settings.SingerPhotoPath;
            
            if (string.IsNullOrEmpty(basePath)) return null;

            try
            {
                basePath = Path.Combine(basePath, CurrentSubFolder);

                if (!Path.IsPathRooted(basePath))
                {
                    basePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, basePath);
                }

                if (!Directory.Exists(basePath))
                {
                    // AppLogger.Log($"GetPhotoLocalPath: Directory not found: {basePath}");
                    return null;
                }

                string normalizedPath = NormalizePath(basePath);

                // Ensure cache is ready
                lock (_cacheLock)
                {
                    if (!_foldersCache.ContainsKey(normalizedPath))
                    {
                        InitializeCache(basePath);
                    }
                }

                // Split names for chorus singers (e.g., "Artist A / Artist B")
                char[] separators = { '/', '&', ',', '、', '+' };
                string[] individualNames = singerName.Split(separators, StringSplitOptions.RemoveEmptyEntries)
                                                     .Select(n => n.Trim())
                                                     .ToArray();

                // Try each name including the full combined name
                var namesToTry = new System.Collections.Generic.List<string> { singerName };
                namesToTry.AddRange(individualNames);

                if (_foldersCache.TryGetValue(normalizedPath, out var currentCache))
                {
                    foreach (var nameToTry in namesToTry.Distinct())
                    {
                        string normalizedName = NormalizeName(nameToTry);
                        if (currentCache.TryGetValue(normalizedName, out string? localPath))
                        {
                            return localPath;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"Error finding local photo path for singer {singerName}", ex);
            }

            // Record the singer name if photo was not found
            //RecordMissingSinger(singerName);
            
            return null;
        }
    }
}
