using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

namespace UltimateKtv
{
    public class FavoriteUserInfo
    {
        public string User_Id { get; set; } = "";
        public string User_Name { get; set; } = "";
    }

    /// <summary>
    /// A static class to hold all song and singer data loaded from the database.
    /// This acts as an in-memory cache for fast access throughout the application.
    /// </summary>
    public static class SongDatas
    {
        // Static properties to hold the loaded data.
        // Using nullable types (e.g., List<...>? ) is good practice.
        public static ILookup<string, Dictionary<string, object?>>? SongsByCleanedTitle { get; private set; }
        public static Dictionary<string, List<Dictionary<string, object?>>>? GenerationSongs { get; private set; }
        public static Dictionary<string, string>? GenerationNames { get; private set; }
        public static List<Dictionary<string, object?>>? SongData { get; private set; }
        public static DataTable? SongDataSchema { get; private set; }
        public static DataTable? LangData { get; private set; }
        public static List<string>? SongLangList { get; private set; }
        public static List<Dictionary<string, object?>>? SingerData { get; private set; }
        public static DataTable? SingerDataSchema { get; private set; }
        public static DataTable? SingerTypeData { get; private set; }
        public static List<string>? SingerTypeList { get; private set; }
        public static DataTable? FavoriteUserData { get; private set; }
        public static List<Dictionary<string, object?>>? FavoriteSongData { get; private set; }
        public static DataTable? FavoriteSongDataSchema { get; private set; }
        public static DataTable? PlayListData { get; private set; }

        /// <summary>
        /// A comparer that utilizes the native Windows NLS string sort for accurate stroke counting.
        /// This flawlessly handles non-standard, simplified, and Kanji characters missing in Big5 definitions.
        /// </summary>
        private class NativeStrokeComparer : IComparer<string>
        {
            public static readonly NativeStrokeComparer Instance = new NativeStrokeComparer();
            private const uint SORT_STROKE = 0x00040000;

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
            private static extern int CompareStringEx(
                string lpLocaleName,
                uint dwCmpFlags,
                string lpString1,
                int cchCount1,
                string lpString2,
                int cchCount2,
                IntPtr lpVersionInformation,
                IntPtr lpReserved,
                IntPtr lParam);

            public int Compare(string? x, string? y)
            {
                if (x == y) return 0;
                if (x == null) return -1;
                if (y == null) return 1;

                int result = CompareStringEx("zh-TW_stroke", 0, x, x.Length, y, y.Length, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                return result == 0 ? string.Compare(x, y, StringComparison.Ordinal) : result - 2;
            }
        }

        /// <summary>
        /// Removes text within parentheses or brackets from a song title to normalize it for matching.
        /// Example: "My Song (Remix)" becomes "My Song".
        /// </summary>
        /// <param name="title">The original song title.</param>
        /// <returns>The cleaned song title.</returns>
        public static string CleanSongTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return string.Empty;
            // This regex removes content within () or [], along with surrounding whitespace.
            return Regex.Replace(title, @"\s*[\(\[].*?[\)\]]\s*", " ").Trim();
        }

		/// <summary>
		/// Checks if a singer field contains the specified singer name.
		/// Handles multiple singers separated by common delimiters like &, /, ,, etc.
		/// This version handles cases where both songSinger and targetSinger can be multi-valued.
		/// It returns true only if ALL singers in targetSinger are found in songSinger.
		/// </summary>
		/// <param name="songSinger">The singer field from the song data</param>
		/// <param name="targetSinger">The singer name(s) to search for (e.g., "Artist B & Artist A")</param>
		/// <returns>True if all singers in targetSinger are present in songSinger.</returns>
		public static bool ContainsSinger(string songSinger, string targetSinger)
		{
			if (string.IsNullOrEmpty(songSinger) || string.IsNullOrEmpty(targetSinger))
				return false;

			// Common delimiters used to separate multiple singers
			char[] delimiters = { '&', '/', ',', '、', '；', ';', '|' };

			// Create a HashSet of singers from the song data for efficient, case-insensitive lookups.
			var songSingers = new HashSet<string>(
				songSinger.Split(delimiters, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()),
				StringComparer.OrdinalIgnoreCase
			);

			// If the song has no singers listed, it cannot be a match.
			if (songSingers.Count == 0) return false;

			// Get the list of target singers from the search query.
			var targetSingers = targetSinger.Split(delimiters, StringSplitOptions.RemoveEmptyEntries)
											.Select(s => s.Trim())
											.ToList();

			// If the search query is empty after trimming, it's not a valid search.
			if (targetSingers.Count == 0) return false;

			// Use LINQ's All() to ensure every target singer exists in the song's list of singers.
			return targetSingers.All(t => songSingers.Contains(t));
		}

		/// <summary>
		/// Applies the user-defined sorting to a list of songs. This is a centralized method
		/// to ensure consistent sorting across the main UI and web services.
		/// </summary>
		/// <typeparam name="T">The type of the song object (e.g., SongDisplayItem or Dictionary<string, object?>).</typeparam>
		/// <param name="songs">The list of songs to sort.</param>
		/// <param name="getter">A function to retrieve a property value from a song object by name.</param>
		/// <returns>A sorted list of songs.</returns>
		public static List<T> ApplySongSorting<T>(IEnumerable<T> songs, Func<T, string, object?> getter)
		{
			var settings = SettingsManager.Instance.CurrentSettings;
            var strokeComparer = NativeStrokeComparer.Instance;

			Func<T, int> getPlayCount = s => int.TryParse((getter(s, "Song_PlayCount") ?? getter(s, "PlayCount"))?.ToString(), out int val) ? val : 0;
			Func<T, DateTime> getCreateDate = s => (getter(s, "Song_CreatDate") ?? getter(s, "Song_CreatDate")) is DateTime dt ? dt : DateTime.MinValue;
			Func<T, int> getWordCount = s => int.TryParse(getter(s, "Song_WordCount")?.ToString(), out int val) ? val : 0;
			Func<T, string> getSongName = s => (getter(s, "Song_SongName") ?? getter(s, "SongName"))?.ToString() ?? "";

			switch (settings.SongSortMethod)
			{
				case 1: // 點播次數 (Play Count)
					return songs.OrderByDescending(getPlayCount)
                                .ThenBy(s =>
					            {
					                var name = getSongName(s);
					                // Sort English names (ASCII) first (return 0), then others (return 1)
					                return string.IsNullOrEmpty(name) || name[0] > 127 ? 1 : 0;
					            })
					            .ThenBy(getSongName, strokeComparer).ToList();

				case 2: // 加歌日期 (Date Added)
					return songs.OrderByDescending(getCreateDate)
					            .ThenBy(s =>
					            {
					                var name = getSongName(s);
					                // Sort English names (ASCII) first (return 0), then others (return 1)
					                return string.IsNullOrEmpty(name) || name[0] > 127 ? 1 : 0;
					            })
					            .ThenBy(getSongName, strokeComparer).ToList();

				case 3: // 字數 (Word Count)
				default:			

					return songs.OrderBy(getWordCount)
					            .ThenBy(s =>
					            {
					                var name = getSongName(s);
					                // Sort English names (ASCII) first (return 0), then others (return 1)
					                return string.IsNullOrEmpty(name) || name[0] > 127 ? 1 : 0;
					            })
					            .ThenBy(getSongName, strokeComparer).ToList();
			}
		}

        /// <summary>
        /// Rebuilds the FilePath for all songs based on current settings.
        /// Call this when the SongLibraryDrivePath setting changes to apply immediately.
        /// Returns old FilePath -> new FilePath for paths that changed, so already-built list items can be updated.
        /// </summary>
        public static Dictionary<string, string> RebuildSongFilePaths()
        {
            var changedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (SongData == null) return changedPaths;

            var settings = SettingsManager.Instance.CurrentSettings;
            var songLibraryDrivePath = settings.ChangeSongLibraryDriveEnabled && 
                                        !string.IsNullOrEmpty(settings.SongLibraryDrivePath)
                                        ? settings.SongLibraryDrivePath
                                        : "";

            foreach (var song in SongData)
            {
                string songPath = song["Song_Path"]?.ToString() ?? string.Empty;
                string songFileName = song["Song_FileName"]?.ToString() ?? string.Empty;
                string oldFilePath = song.TryGetValue("FilePath", out var oldObj) ? oldObj?.ToString() ?? "" : "";

                if (!string.IsNullOrEmpty(songLibraryDrivePath))
                {
                    // Remove the drive letter and root from the original path
                    string relativePath = songPath;
                    if (Path.IsPathRooted(songPath))
                    {
                        string? pathRoot = Path.GetPathRoot(songPath);
                        if (!string.IsNullOrEmpty(pathRoot))
                        {
                            relativePath = songPath.Substring(pathRoot.Length);
                        }
                    }
                    song["FilePath"] = Path.Combine(songLibraryDrivePath, relativePath, songFileName);
                }
                else
                {
                    song["FilePath"] = Path.Combine(songPath, songFileName);
                }

                string newFilePath = song["FilePath"]?.ToString() ?? "";
                if (oldFilePath.Length > 0 && !string.Equals(oldFilePath, newFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    changedPaths[oldFilePath] = newFilePath;
                }
            }

            AppLogger.Log($"[SongDatas] Rebuilt file paths for {SongData.Count} songs. LibraryPath: '{songLibraryDrivePath}'");
            Debug.WriteLine($"[SongDatas] Rebuilt file paths for {SongData.Count} songs. LibraryPath: '{songLibraryDrivePath}'");
            return changedPaths;
        }

        /// <summary>
        /// Initializes and loads all data from the song database (see <see cref="Db"/>).
        /// </summary>
        public static void Init()
        {
            string databaseFile = Db.FilePath;
            if (!File.Exists(databaseFile))
            {
                // Throw a specific, user-friendly exception if the database file is missing.
                throw new FileNotFoundException(Loc.T("App.DbMissing", databaseFile), databaseFile);
            }

            try
            {
                // Check and create YoutubeSongList table before proceeding
                CheckAndCreateYoutubeTable();

                // Load all data tables into static properties
                SongData = Db.GetDictionary("SELECT * FROM ktv_Song ORDER BY Song_Id");

                // --- Schema Fix: Expand User_Id column if needed ---
                // Must be done before initializing other data that might depend on it,
                // though technically valid at any point before we write to ktv_User.
                // SQLite does not enforce TEXT lengths, so this only applies to Access.
                if (Db.IsAccess)
                {
                    FixUserTableSchema(databaseFile);
                }


                // After loading song data, construct the full FilePath for each song.
                if (SongData != null)
                {
                    // Build FilePath for each song using the centralized method
                    RebuildSongFilePaths();

                    // Create a lookup for fast searching by a "cleaned" song title (case-insensitive).
                    // This is a one-time operation that dramatically speeds up searches later.
                    SongsByCleanedTitle = SongData.ToLookup(
                        s => CleanSongTitle(s.TryGetValue("Song_SongName", out var nameObj) ? nameObj?.ToString() ?? "" : ""),
                        StringComparer.OrdinalIgnoreCase
                    );

                    // Pre-load and cache data from generation JSON files for fast access.
                    // This avoids a 3-5 second freeze when clicking the generation filter buttons.
                    //Debug.WriteLine("Starting to cache Generation data...");
                    //var sw = Stopwatch.StartNew();
                    //LoadGenerationData();
                    //Debug.WriteLine($"Generation data cached in {sw.ElapsedMilliseconds}ms.");
                }

                SongDataSchema = Db.GetDataTable("SELECT * FROM ktv_Song WHERE 1=0");

                // Add the FilePath column to the schema, so it's available in DataTables.
                if (SongDataSchema != null)
                {
                    SongDataSchema.Columns.Add("FilePath", typeof(string));
                }

                LangData = Db.GetDataTable("SELECT * FROM ktv_Langauage ORDER BY Langauage_Id");
                SingerData = Db.GetDictionary("SELECT * FROM ktv_Singer ORDER BY Singer_Id");
                SingerDataSchema = Db.GetDataTable("SELECT * FROM ktv_Singer WHERE 1=0");
                SingerTypeData = Db.GetDataTable("SELECT * FROM ktv_Swan ORDER BY Swan_Id");
                FavoriteUserData = Db.GetDataTable("SELECT * FROM ktv_User ORDER BY User_Id");
                FavoriteSongData = Db.GetDictionary("SELECT * FROM ktv_Favorite ORDER BY User_Id");
                FavoriteSongDataSchema = Db.GetDataTable("SELECT * FROM ktv_Favorite WHERE 1=0");

                // Populate lists from the newly loaded DataTables
                if (LangData != null)
                {
                    SongLangList = LangData.AsEnumerable()
                        .Select(row => row["Langauage_Name"]?.ToString() ?? string.Empty)
                        .ToList();
                }
                if (SingerTypeData != null)
                {
                    SingerTypeList = SingerTypeData.AsEnumerable()
                        .Select(row => row["Swan_Name"]?.ToString() ?? string.Empty)
                        .ToList();
                }

                // Sort SingerData immediately using global logic
                if (SingerData != null)
                {
                    try
                    {
                        var strokeComparer = StringComparer.Create(
                            System.Globalization.CultureInfo.GetCultureInfo("zh-TW"),
                            System.Globalization.CompareOptions.StringSort);

                        // Extract all active singers from SongData
                        var activeSingers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        if (SongData != null)
                        {
                            char[] delimiters = { '&', '/', ',', '、', '；', ';', '|' };
                            foreach (var song in SongData)
                            {
                                if (song.TryGetValue("Song_Singer", out var singerObj) && singerObj is string songSinger)
                                {
                                    var singers = songSinger.Split(delimiters, StringSplitOptions.RemoveEmptyEntries);
                                    foreach (var s in singers)
                                    {
                                        var trimmed = s.Trim();
                                        if (!string.IsNullOrEmpty(trimmed))
                                        {
                                            activeSingers.Add(trimmed);
                                        }
                                    }
                                }
                            }
                        }

                        SingerData = SingerData
                            .Where(s => s != null)
                            .Where(s => 
                            {
                                var name = s.TryGetValue("Singer_Name", out var nameObj) ? nameObj?.ToString()?.Trim() ?? "" : "";
                                bool hasSongs = activeSingers.Contains(name);
                                if (!hasSongs && !string.IsNullOrEmpty(name))
                                {
                                    Debug.WriteLine($"[SongDatas] Filtered out singer with 0 songs: {name}");
                                }
                                return hasSongs;
                            })
                            .OrderBy(s =>
                            {
                                var name = s.TryGetValue("Singer_Name", out var nameObj) ? nameObj?.ToString() ?? "" : "";
                                // Sort English names (ASCII) first (return 0), then others (return 1)
                                return string.IsNullOrEmpty(name) || name[0] > 127 ? 1 : 0;
                            })
                            .ThenBy(s => s.TryGetValue("Singer_Name", out var name) ? name?.ToString() ?? "" : "", strokeComparer)
                            .ToList();

                         Debug.WriteLine($"[SongDatas] SingerData loaded and sorted with {SingerData.Count} singers.");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SongDatas] Failed to sort SingerData: {ex.Message}");
                    }
                }


                // Initialize the playlist DataTable
                if (SongDataSchema != null)
                {
                    PlayListData = SongDataSchema.Clone();
                    PlayListData.Columns.Add("Song_PrimaryKey", typeof(string));
                    PlayListData.Columns.Add("Song_PlayOrder", typeof(string));
                    PlayListData.Columns.Add("Song_Status", typeof(string));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"An error occurred while loading the database: {ex.Message}");
                // Re-throw the exception so the calling code (MainWindow) can handle it.
                throw;
            }
        }

        /// <summary>
        /// Loads and processes all gen*.json files at startup, caching the results for fast retrieval.
        /// </summary>
        private static void LoadGenerationData()
        {
            if (SongData == null || SongsByCleanedTitle == null)
            {
                Debug.WriteLine("[Generation Cache] Cannot load generation data because SongData is not initialized.");
                return;
            }

            GenerationSongs = new Dictionary<string, List<Dictionary<string, object?>>>();
            GenerationNames = new Dictionary<string, string>();
            var notFoundSongs = new List<string>();

            for (int genNum = 1; genNum <= 7; genNum++)
            {
                var genKey = genNum.ToString();
                var genFilePath = $"gen{genNum}.json";
                var periodName = Loc.T("Title.GenerationN", genNum);
                var genSongList = new List<Dictionary<string, object?>>();

                // Initialize with default values
                GenerationNames[genKey] = periodName;
                GenerationSongs[genKey] = genSongList;

                if (!File.Exists(genFilePath))
                {
                    Debug.WriteLine($"[Generation Cache] File not found: {genFilePath}");
                    continue;
                }

                try
                {
                    var jsonContent = File.ReadAllText(genFilePath);
                    using var jsonDoc = JsonDocument.Parse(jsonContent);
                    var root = jsonDoc.RootElement;

                    JsonElement songsElement = default;

                    // Extract period name and songs element from various possible JSON structures
                    if (root.TryGetProperty("name", out var nameElement))
                    {
                        periodName = nameElement.GetString() ?? periodName;
                        root.TryGetProperty("songs", out songsElement);
                    }
                    else if (root.TryGetProperty("decade", out var decadeElement))
                    {
                        if (decadeElement.TryGetProperty("name", out var decadeName))
                            periodName = decadeName.GetString() ?? periodName;
                        decadeElement.TryGetProperty("songs", out songsElement);
                    }
                    else if (root.TryGetProperty("period", out var periodElement))
                    {
                        if (periodElement.TryGetProperty("name", out var periodNameElement))
                            periodName = periodNameElement.GetString() ?? periodName;
                        periodElement.TryGetProperty("songs", out songsElement);
                    }

                    GenerationNames[genKey] = periodName;

                    if (songsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var song in songsElement.EnumerateArray())
                        {
                            var title = song.TryGetProperty("title", out var titleElement) ? titleElement.GetString() ?? "" : "";
                            var artist = song.TryGetProperty("artist", out var artistElement) ? artistElement.GetString() ?? "" : "";

                            if (string.IsNullOrEmpty(title)) continue;

                            var cleanedTitle = CleanSongTitle(title);
                            var potentialMatches = SongsByCleanedTitle[cleanedTitle];

                            // Split the artist string from the JSON file into individual names.
                            // This handles cases like "Artist A & Artist B".
                            char[] delimiters = { '&', '+' }; // '&', '/', ',', '、', '；', ';', '|', '+' };
                            var jsonArtists = new HashSet<string>(
                                artist.Split(delimiters, StringSplitOptions.RemoveEmptyEntries).Select(a => a.Trim()),
                                StringComparer.OrdinalIgnoreCase
                            );

                            var foundSong = potentialMatches.FirstOrDefault(s =>
                            {
                                if (s == null) return false;
                                var singerName = s.TryGetValue("Song_Singer", out var singerObj) ? singerObj?.ToString() ?? "" : "";

                                // Split the singer name from the database and check for any overlap with the artists from the JSON.
                                var dbSingers = singerName.Split(delimiters, StringSplitOptions.RemoveEmptyEntries).Select(a => a.Trim());
                                return dbSingers.Any(dbSinger => jsonArtists.Contains(dbSinger));
                            });


                            if (foundSong != null)
                            {
                                genSongList.Add(foundSong);
                            }
                            else
                            {
                                notFoundSongs.Add($"File: {genFilePath}, Title: {title}, Artist: {artist}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Generation Cache] Error processing {genFilePath}: {ex.Message}");
                }
            }

            if (notFoundSongs.Any())
            {
                File.WriteAllLines("genFilePathNA.txt", notFoundSongs);
            }
        }

        /// <summary>
        /// Checks if the YoutubeSongList table exists in the database and creates it if not.
        /// </summary>
        private static void CheckAndCreateYoutubeTable()
        {
            try
            {
                var tables = Db.GetTableList();
                if (!tables.Contains("YoutubeSongList"))
                {
                    // Access needs MEMO for long text; SQLite would give MEMO numeric affinity, so use TEXT there
                    string urlType = Db.IsAccess ? "MEMO" : "TEXT";
                    string createTableSql = $@"
                        CREATE TABLE YoutubeSongList (
                            Id TEXT(5) PRIMARY KEY,
                            Name TEXT(255),
                            CreatDate DATETIME,
                            Url {urlType},
                            Singer TEXT(50),
                            Lang TEXT(16),
                            PlayCount INTEGER,
                            Length INTEGER,
                            FileName TEXT(255),
                            FileSize INTEGER,
                            Track INTEGER DEFAULT 3,
                            Volume INTEGER,
                            WordCount INTEGER
                        )";
                    Db.ExecuteNonQuery(createTableSql);
                    AppLogger.Log("DB Write: Created YoutubeSongList table");
                    Debug.WriteLine("Created YoutubeSongList table");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to create YoutubeSongList table: {ex.Message}");
            }

            // SQLite does not enforce TEXT lengths, so only Access needs the column widened
            if (Db.IsAccess)
            {
                EnsureYoutubeNameColumnLength(Db.FilePath);
            }
        }

        // Actual YoutubeSongList.Name length; older databases may still have a shorter column if ALTER failed
        private static int _youtubeNameMaxLength = 255;

        /// <summary>
        /// Widens YoutubeSongList.Name to TEXT(255) if it is shorter, then records the real length so writes never exceed it.
        /// </summary>
        private static void EnsureYoutubeNameColumnLength(string databasePath)
        {
            try
            {
                using var conn = DbHelper.Access.OpenConn(databasePath);
                int ReadNameLength()
                {
                    var columns = conn.GetSchema("Columns", new string?[] { null, null, "YoutubeSongList", "Name" });
                    if (columns.Rows.Count == 0 || columns.Rows[0]["CHARACTER_MAXIMUM_LENGTH"] is not object lenObj || lenObj == DBNull.Value) return 0;
                    return Convert.ToInt32(lenObj);
                }

                int len = ReadNameLength();
                if (len > 0 && len < 255)
                {
                    try
                    {
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = "ALTER TABLE YoutubeSongList ALTER COLUMN Name TEXT(255)";
                        cmd.ExecuteNonQuery();
                        AppLogger.Log($"DB Write: Widened YoutubeSongList.Name from {len} to 255.");
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Log($"Failed to widen YoutubeSongList.Name ({len}): {ex.Message}");
                    }
                    len = ReadNameLength();
                }
                if (len > 0) _youtubeNameMaxLength = Math.Min(len, 255);
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Failed to check YoutubeSongList.Name length: {ex.Message}");
            }
        }
        private static readonly System.Text.RegularExpressions.Regex DurationLikeLangRegex = new(@"^\d{1,2}(:\d{2}){1,2}$");

        /// <summary>
        /// Records a downloaded YouTube song to the MDB database.
        /// </summary>
        public static string RecordYoutubeSong(SongDisplayItem song)
        {
            try
            {
                // Url must be the real video ID, never the internal history Id (e.g. "Y0017")
                string videoId = YtDlpHelper.ResolveVideoId(song.YoutubeId, song.SongId, song.FilePath);

                // Check if already exists by Url
                if (!string.IsNullOrEmpty(videoId))
                {
                    var existing = Db.GetDataTable("SELECT Id FROM YoutubeSongList WHERE Url = ?", videoId);
                    if (existing.Rows.Count > 0)
                    {
                        return existing.Rows[0][0].ToString() ?? "";
                    }
                }

                // Check if already exists by FileName (handles imported files that have empty Urls)
                string fileName = Path.GetFileName(song.FilePath) ?? "";
                if (!string.IsNullOrEmpty(fileName))
                {
                    var existing = Db.GetDataTable("SELECT Id FROM YoutubeSongList WHERE FileName = ?", fileName);
                    if (existing.Rows.Count > 0)
                    {
                        string existingId = existing.Rows[0][0].ToString() ?? "";
                        // If we have a SongId (Video ID) now, update the empty Url in the database
                        if (!string.IsNullOrEmpty(videoId))
                        {
                            Db.ExecuteNonQuery("UPDATE YoutubeSongList SET Url = ? WHERE Id = ? AND (Url IS NULL OR Url = '')", videoId, existingId);
                        }
                        return existingId;
                    }
                }

                // Generate new Id (Y0001 format)
                var maxIdTable = Db.GetDataTable("SELECT MAX(Id) FROM YoutubeSongList");
                string newId = "Y0001";
                if (maxIdTable.Rows.Count > 0 && maxIdTable.Rows[0][0] != DBNull.Value)
                {
                    string maxId = maxIdTable.Rows[0][0].ToString() ?? "";
                    if (maxId.StartsWith("Y") && int.TryParse(maxId.Substring(1), out int num))
                    {
                        newId = $"Y{(num + 1):D4}";
                    }
                }

                string name = song.SongName;
                if (!string.IsNullOrEmpty(name) && name.Length > _youtubeNameMaxLength) name = name.Substring(0, _youtubeNameMaxLength);

                string insertSql = @"INSERT INTO YoutubeSongList 
                    (Id, Name, CreatDate, Url, Singer, Lang, PlayCount, Length, FileName, FileSize, Track, Volume, WordCount) 
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";
                
                Db.ExecuteNonQuery(insertSql,
                    newId,
                    name ?? "",
                    DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"),
                    videoId,
                    "",                     // Singer
                    song.Language ?? "",
                    0,                      // PlayCount: counted when actually played
                    0,                      // Length
                    Path.GetFileName(song.FilePath) ?? "",
                    File.Exists(song.FilePath) ? (int)Math.Min(new FileInfo(song.FilePath).Length, int.MaxValue) : 0,
                    3,                      // Track
                    song.Volume,
                    0);                     // WordCount
                Debug.WriteLine($"Recorded YouTube song: {name} (Id: {newId})");
                AppLogger.Log($"DB Write: Recorded YouTube song: {name} (Id: {newId})");

                return newId;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error recording YouTube song: {ex.Message}");
                AppLogger.Log($"Error recording YouTube song: {ex.Message}");
                return "";
            }
        }

        /// <summary>
        /// Updates the Length of a recorded YouTube song.
        /// </summary>
        public static bool UpdateYoutubeSongLength(string songId, int lengthSeconds)
        {
            if (string.IsNullOrEmpty(songId) || lengthSeconds <= 0) return false;
            try
            {
                string sql = "UPDATE YoutubeSongList SET Length = ? WHERE (Id = ? OR Url = ?) AND (Length IS NULL OR Length = 0)";
                int rowsAffected = Db.ExecuteNonQuery(sql, lengthSeconds, songId, songId);
                if (rowsAffected > 0)
                {
                    AppLogger.Log($"DB Write: Updated Youtube song {songId} length to {lengthSeconds} seconds.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"Failed to update Youtube song length for {songId}", ex);
            }
            return false;
        }

        /// <summary>
        /// Gets the history of downloaded YouTube songs from the MDB database.
        /// </summary>
        public static List<Dictionary<string, object?>> GetYoutubeHistory()
        {
            var list = new List<Dictionary<string, object?>>();
            try
            {
                AppLogger.Log("GetYoutubeHistory: Starting DB fetch...");
                int sortMethod = SettingsManager.Instance.CurrentSettings.SongSortMethod;
                string orderBy = "CreatDate DESC";
                if (sortMethod == 1)
                {
                    orderBy = "PlayCount DESC, CreatDate DESC";
                }
                string query = $"SELECT * FROM YoutubeSongList ORDER BY {orderBy}";
                var dt = Db.GetDataTable(query);

                foreach (System.Data.DataRow row in dt.Rows)
                {
                    var dict = YoutubeRowToDictionary(row);
                    list.Add(dict);
                }
                AppLogger.Log($"GetYoutubeHistory: Fetched {list.Count} records successfully.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to fetch Youtube history: {ex.Message}");
                AppLogger.Log($"GetYoutubeHistory Error: {ex.Message}");
            }
            return list;
        }

        /// <summary>
        /// Gets a single YouTube history record by its Id (e.g. "Y0017"); returns null if not found.
        /// </summary>
        public static Dictionary<string, object?>? GetYoutubeHistoryById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            try
            {
                var dt = Db.GetDataTable("SELECT * FROM YoutubeSongList WHERE Id = ?", id);
                return dt.Rows.Count > 0 ? YoutubeRowToDictionary(dt.Rows[0]) : null;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"GetYoutubeHistoryById Error ({id}): {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Converts a YoutubeSongList row to a dictionary, repairing legacy data (empty Url, duration in Lang) in place.
        /// </summary>
        private static Dictionary<string, object?> YoutubeRowToDictionary(System.Data.DataRow row)
        {
            var dict = new Dictionary<string, object?>();
            foreach (System.Data.DataColumn col in row.Table.Columns)
            {
                dict[col.ColumnName] = row[col] == DBNull.Value ? null : row[col];
            }

            // Imported files have an empty Url; recover the video ID from "{title}_{videoId}.mp4" and persist it
            if (string.IsNullOrEmpty(dict.GetValueOrDefault("Url")?.ToString()))
            {
                string recoveredId = YtDlpHelper.ExtractVideoIdFromFileName(dict.GetValueOrDefault("FileName")?.ToString());
                string rowId = dict.GetValueOrDefault("Id")?.ToString() ?? "";
                if (!string.IsNullOrEmpty(recoveredId) && !string.IsNullOrEmpty(rowId))
                {
                    dict["Url"] = recoveredId;
                    try
                    {
                        Db.ExecuteNonQuery("UPDATE YoutubeSongList SET Url = ? WHERE Id = ? AND (Url IS NULL OR Url = '')", recoveredId, rowId);
                        AppLogger.Log($"DB Write: Recovered YouTube video ID '{recoveredId}' for {rowId} from file name.");
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Log($"GetYoutubeHistory: Failed to backfill Url for {rowId}: {ex.Message}");
                    }
                }
            }

            // Names used to be cut at 40 chars, and imported names kept the "_{videoId}" suffix; restore from the file name
            string name = dict.GetValueOrDefault("Name")?.ToString() ?? "";
            string fileTitle = YtDlpHelper.GetTitleFromFileName(dict.GetValueOrDefault("FileName")?.ToString());
            string fixedName = name;
            if (fileTitle.Length > 0 && name != fileTitle)
            {
                string safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
                string fileNameOnly = Path.GetFileNameWithoutExtension(dict.GetValueOrDefault("FileName")?.ToString() ?? "");
                if (name == fileNameOnly)
                    fixedName = fileTitle; // imported: Name is the whole file name incl. "_{videoId}"
                else if (name.Length == 40 && (fileTitle.StartsWith(safeName, StringComparison.Ordinal) || fileNameOnly.StartsWith(safeName, StringComparison.Ordinal)))
                    fixedName = fileTitle; // truncated at 40 chars
                // Never exceed the real column length (an old 40-char column would make the UPDATE fail on every read)
                if (fixedName.Length > _youtubeNameMaxLength) fixedName = fixedName.Substring(0, _youtubeNameMaxLength);
            }
            if (fixedName != name)
            {
                dict["Name"] = fixedName;
                string rowId = dict.GetValueOrDefault("Id")?.ToString() ?? "";
                try
                {
                    Db.ExecuteNonQuery("UPDATE YoutubeSongList SET Name = ? WHERE Id = ?", fixedName, rowId);
                    AppLogger.Log($"DB Write: Restored YouTube name for {rowId}: '{fixedName}'.");
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"GetYoutubeHistory: Failed to restore Name for {rowId}: {ex.Message}");
                }
            }

            // Web orders used to store the duration ("05:11") in Lang; clear it
            string lang = dict.GetValueOrDefault("Lang")?.ToString() ?? "";
            if (DurationLikeLangRegex.IsMatch(lang))
            {
                dict["Lang"] = "";
                string rowId = dict.GetValueOrDefault("Id")?.ToString() ?? "";
                try
                {
                    Db.ExecuteNonQuery("UPDATE YoutubeSongList SET Lang = '' WHERE Id = ?", rowId);
                    AppLogger.Log($"DB Write: Cleared duration-like Lang '{lang}' for {rowId}.");
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"GetYoutubeHistory: Failed to clear Lang for {rowId}: {ex.Message}");
                }
            }
            return dict;
        }

        public static void DeleteYoutubeHistory(string id)
        {
            try
            {
                Db.ExecuteNonQuery("DELETE FROM YoutubeSongList WHERE Id = ?", id);
                AppLogger.Log($"Deleted Youtube History record: {id}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error deleting Youtube history: {ex.Message}");
                AppLogger.Log($"Error deleting Youtube history: {ex.Message}");
            }
        }

        /// <summary>
        /// Reloads the favorite song data from the database.
        /// Used after modifying the ktv_Favorite table to keep the in-memory cache in sync.
        /// </summary>
        public static void ReloadFavoriteData()
        {
            try
            {
                FavoriteSongData = Db.GetDictionary("SELECT * FROM ktv_Favorite ORDER BY User_Id");
                Debug.WriteLine("Favorite data reloaded successfully.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reloading favorite data: {ex.Message}");
            }
        }
        /// <summary>
        /// Checks if the ktv_User table columns need to be expanded and executes the ALTER TABLE command if so.
        /// This is a "best effort" fix to support longer User IDs (timestamps).
        /// </summary>
        /// <param name="databasePath">Path to the Access database file.</param>
        /// <summary>
        /// Checks if the ktv_User table columns need to be expanded and executes the ALTER TABLE command if so.
        /// This is a "best effort" fix to support longer User IDs (timestamps).
        /// </summary>
        /// <param name="databasePath">Path to the Access database file.</param>
        private static void FixUserTableSchema(string databasePath)
        {
            try
            {
                // We must use GetSchema to reliably get the CHARACTER_MAXIMUM_LENGTH.
                // DataTable from Select * often returns valid MaxLength for OLEDB unless it's Memo,
                // but GetSchema is the source of truth for the engine.
                using (var conn = DbHelper.Access.OpenConn(databasePath))
                {
                    // "Columns" schema: [TABLE_CATALOG, TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME]
                    var restrictions = new string?[] { null, null, "ktv_User", null };
                    var columns = conn.GetSchema("Columns", restrictions);

                    foreach (DataRow row in columns.Rows)
                    {
                        var colName = row["COLUMN_NAME"]?.ToString();
                        long maxLen = 0;

                        if (row.Table.Columns.Contains("CHARACTER_MAXIMUM_LENGTH"))
                        {
                            var lenObj = row["CHARACTER_MAXIMUM_LENGTH"];

                            if (lenObj != null && lenObj != DBNull.Value)
                            {
                                maxLen = Convert.ToInt64(lenObj);
                            }
                        }

                        // Check for User_Id or User_Name
                        // to compatible with CrazyKtv DB, User_Id to 12
                        if (string.Equals(colName, "User_Id", StringComparison.OrdinalIgnoreCase))
                        {
                            if (maxLen > 0 && maxLen != 12)
                            {
                                Debug.WriteLine($"[Schema Fix] ktv_User->{colName} length is {maxLen}, expanding to 12...");
                                try
                                {
                                    // We need a separate command/connection or close and reopen? 
                                    // We can execute on the same connection if we're careful, but ExecuteNonQuery opens its own.
                                    // To avoid "file in use", we should close this connection first OR use a command on THIS connection.
                                    // Since DbHelper.Access.ExecuteNonQuery creates a NEW connection, we must close this one first?
                                    // Actually, let's just use a command on the CURRENT connection to be safe/efficient.
                                    using (var cmd = conn.CreateCommand())
                                    {
                                        cmd.CommandText = $"ALTER TABLE ktv_User ALTER COLUMN {colName} TEXT(12)";
                                        cmd.ExecuteNonQuery();
                                    }
                                    Debug.WriteLine($"[Schema Fix] ktv_User->{colName} expanded successfully.");
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"[Schema Fix] Failed to expand ktv_User->{colName}: {ex.Message}");
                                }
                            }
                        }
                        // to compatible with CrazyKtv DB, User_Name to 255
                        if (string.Equals(colName, "User_Name", StringComparison.OrdinalIgnoreCase))
                        {
                            if (maxLen > 0 && maxLen != 255)
                            {
                                Debug.WriteLine($"[Schema Fix] {colName} length is {maxLen}, expanding to 255...");
                                try
                                {
                                    // We need a separate command/connection or close and reopen? 
                                    // We can execute on the same connection if we're careful, but ExecuteNonQuery opens its own.
                                    // To avoid "file in use", we should close this connection first OR use a command on THIS connection.
                                    // Since DbHelper.Access.ExecuteNonQuery creates a NEW connection, we must close this one first?
                                    // Actually, let's just use a command on the CURRENT connection to be safe/efficient.
                                    using (var cmd = conn.CreateCommand())
                                    {
                                        cmd.CommandText = $"ALTER TABLE ktv_User ALTER COLUMN {colName} TEXT(255)";
                                        cmd.ExecuteNonQuery();
                                    }
                                    Debug.WriteLine($"[Schema Fix] ktv_User->{colName} expanded successfully.");
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"[Schema Fix] Failed to expand ktv_User->{colName}: {ex.Message}");
                                }
                            }
                        }
                    }

                    restrictions = new string?[] { null, null, "ktv_Favorite", null };
                    columns = conn.GetSchema("Columns", restrictions);
                    foreach (DataRow row in columns.Rows)
                    {
                        var colName = row["COLUMN_NAME"]?.ToString();
                        long maxLen = 0;

                        if (row.Table.Columns.Contains("CHARACTER_MAXIMUM_LENGTH"))
                        {
                            var lenObj = row["CHARACTER_MAXIMUM_LENGTH"];

                            if (lenObj != null && lenObj != DBNull.Value)
                            {
                                maxLen = Convert.ToInt64(lenObj);
                            }
                        }

                        // Check for User_Id
                        // to compatible with CrazyKtv DB, User_Id to 12
                        if (string.Equals(colName, "User_Id", StringComparison.OrdinalIgnoreCase))
                        {
                            if (maxLen > 0 && maxLen != 12)
                            {
                                Debug.WriteLine($"[Schema Fix] ktv_Favorite->{colName} length is {maxLen}, expanding to 12...");
                                try
                                {
                                    // We need a separate command/connection or close and reopen? 
                                    // We can execute on the same connection if we're careful, but ExecuteNonQuery opens its own.
                                    // To avoid "file in use", we should close this connection first OR use a command on THIS connection.
                                    // Since DbHelper.Access.ExecuteNonQuery creates a NEW connection, we must close this one first?
                                    // Actually, let's just use a command on the CURRENT connection to be safe/efficient.
                                    using (var cmd = conn.CreateCommand())
                                    {
                                        cmd.CommandText = $"ALTER TABLE ktv_Favorite ALTER COLUMN {colName} TEXT(12)";
                                        cmd.ExecuteNonQuery();
                                    }
                                    Debug.WriteLine($"[Schema Fix] ktv_Favorite->{colName} expanded successfully.");
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"[Schema Fix] Failed to expand ktv_Favorite->{colName}: {ex.Message}");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Schema Fix] Error checking schema: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if a User_Id string represents a timestamp in the format "MM/dd HH:mm".
        /// </summary>
        public static bool IsTimestampUserId(string userId)
        {
            if (string.IsNullOrEmpty(userId)) return false;
            // Match pattern: MM/dd HH:mm (11 characters)
            return userId.Length == 11 &&
                   char.IsDigit(userId[0]) && char.IsDigit(userId[1]) &&
                   userId[2] == '/' &&
                   char.IsDigit(userId[3]) && char.IsDigit(userId[4]) &&
                   userId[5] == ' ' &&
                   char.IsDigit(userId[6]) && char.IsDigit(userId[7]) &&
                   userId[8] == ':' &&
                   char.IsDigit(userId[9]) && char.IsDigit(userId[10]);
        }

        /// <summary>
        /// Determines the sort weight for a favorite user based on their User_Id.
        /// 1) Custom favorite lists (Weight 1)
        /// 2) Cashbox lists: ^CBx, ^CGx (Weight 2)
        /// 3) History Markers: ####, **** (Weight 3)
        /// 4) Timestamps (Weight 4)
        /// </summary>
        public static int GetFavoriteUserSortWeight(string userId)
        {
            if (string.IsNullOrEmpty(userId)) return 1;
            if (userId == "####" || userId == "****") return 3;
            if (IsTimestampUserId(userId)) return 4;
            if (userId.StartsWith("^CB") || userId.StartsWith("^CG")) return 2;
            return 1;
        }

        /// <summary>
        /// Returns a sorted and optionally padded list of favorite users.
        /// </summary>
        /// <param name="columns">歌星格每排幾格 (照片樣式 6、文字樣式 4)，補空格讓每一類從新的一排開始</param>
        public static List<FavoriteUserInfo> GetSortedFavoriteUsers(bool includePadding = true, int columns = 6)
        {
            if (FavoriteUserData == null) return new List<FavoriteUserInfo>();

            // Extract distinct User_Names and their first observed User_Id
            var users = FavoriteUserData.AsEnumerable()
                .Select(row => new FavoriteUserInfo
                {
                    User_Id = row["User_Id"]?.ToString() ?? "",
                    User_Name = row["User_Name"]?.ToString() ?? ""
                })
                .Where(u => !string.IsNullOrEmpty(u.User_Name))
                .GroupBy(u => u.User_Name)
                .Select(g => g.First())
                .ToList();

            var result = new List<FavoriteUserInfo>();
            var buckets = users.GroupBy(u => GetFavoriteUserSortWeight(u.User_Id))
                              .OrderBy(g => g.Key);

            foreach (var bucket in buckets)
            {
                int weight = bucket.Key;
                List<FavoriteUserInfo> sortedBucket;

                if (weight == 4) // Timestamps
                {
                    sortedBucket = bucket.OrderByDescending(u => u.User_Id).ToList();
                }
                else if (weight == 2 || weight == 3) // Cashbox or Markers
                {
                    sortedBucket = bucket.OrderBy(u => u.User_Id).ToList();
                }
                else // Custom listing
                {
                    sortedBucket = bucket.OrderBy(u => u.User_Name).ToList();
                }

                result.AddRange(sortedBucket);

                if (includePadding)
                {
                    // Padding to fill rows of `columns`
                    int paddingNeeded = (columns - (sortedBucket.Count % columns)) % columns;
                    for (int i = 0; i < paddingNeeded; i++)
                    {
                        result.Add(new FavoriteUserInfo { User_Id = "", User_Name = "" });
                    }
                }
            }

            return result;
        }
    }
}