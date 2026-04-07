using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UltimateKtv;
using YoutubeExplode;
using YoutubeExplode.Common;

namespace UltimateKtv;

public class HttpServerHelper
{
	private static string FavoriteLoginID = string.Empty;
	private static object lockthis = new object();
	private static string EmptyResult = JsonConvert.SerializeObject(new JArray());

	public static Dictionary<string, string> MimeTypeMap = new Dictionary<string, string>
	{
		{ ".323", "text/h323" },
		{ ".htm", "text/html" },
		{ ".html", "text/html" },
		{ ".jpg", "image/jpeg" },
		{ ".jpeg", "image/jpeg" },
		{ ".png", "image/png" },
		{ ".gif", "image/gif" },
		{ ".css", "text/css" },
		{ ".js", "application/x-javascript" },
		{ ".json", "application/json" },
		{ ".xml", "text/xml" },
		{ ".txt", "text/plain" },
		{ ".mp3", "audio/mpeg" },
		{ ".mp4", "video/mp4" },
		{ ".avi", "video/x-msvideo" },
		{ ".zip", "application/x-zip-compressed" }
	};

	public static int GetAvailablePort(int rangeStart, int rangeEnd, IPAddress ip, bool includeIdlePorts)
	{
		IPGlobalProperties iPGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
		Func<IPAddress, bool> isIpAnyOrLoopBack = (IPAddress i) =>
			IPAddress.Any.Equals(i) || IPAddress.IPv6Any.Equals(i) ||
			IPAddress.Loopback.Equals(i) || IPAddress.IPv6Loopback.Equals(i);

		List<ushort> list = new List<ushort>();
		list.AddRange(from n in iPGlobalProperties.GetActiveTcpConnections()
					  where n.LocalEndPoint.Port >= rangeStart && n.LocalEndPoint.Port <= rangeEnd &&
						  (isIpAnyOrLoopBack(ip) || n.LocalEndPoint.Address.Equals(ip) || isIpAnyOrLoopBack(n.LocalEndPoint.Address)) &&
						  (!includeIdlePorts || n.State != TcpState.TimeWait)
					  select (ushort)n.LocalEndPoint.Port);
		list.AddRange(from n in iPGlobalProperties.GetActiveTcpListeners()
					  where n.Port >= rangeStart && n.Port <= rangeEnd &&
						  (isIpAnyOrLoopBack(ip) || n.Address.Equals(ip) || isIpAnyOrLoopBack(n.Address))
					  select (ushort)n.Port);
		list.AddRange(from n in iPGlobalProperties.GetActiveUdpListeners()
					  where n.Port >= rangeStart && n.Port <= rangeEnd &&
						  (isIpAnyOrLoopBack(ip) || n.Address.Equals(ip) || isIpAnyOrLoopBack(n.Address))
					  select (ushort)n.Port);
		list.Sort();

		for (int num = rangeStart; num <= rangeEnd; num++)
		{
			if (!list.Contains((ushort)num))
			{
				return num;
			}
		}
		return 0;
	}

	private static string ErrorResult(string key, string value)
	{
		if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
		{
			JArray val = new JArray();
			JObject val2 = new JObject();
			val2.Add("Error", JToken.FromObject(key));
			val2.Add("Message", JToken.FromObject(value));
			val.Add(val2);
			return JsonConvert.SerializeObject(val);
		}
		return JsonConvert.SerializeObject(new JArray());
	}

	private static string DataTableToJSON(DataTable dt)
	{
		if (dt == null || dt.Rows.Count == 0)
			return EmptyResult;

		var list = dt.ToDictionary();
		return JsonConvert.SerializeObject(list);
	}

	private static bool IsNumeric(string value)
	{
		return int.TryParse(value, out _);
	}

	/// <summary>
	/// Creates a DataTable from a list of song dictionaries, including only the specified columns.
	/// </summary>
	private static DataTable CreateSongDataTable(List<Dictionary<string, object?>> songs, string[] columnsToInclude)
	{
		DataTable dataTable = new DataTable();

		if (SongDatas.SongDataSchema != null)
		{
			foreach (var colName in columnsToInclude)
			{
				if (SongDatas.SongDataSchema.Columns.Contains(colName))
				{
					dataTable.Columns.Add(colName, SongDatas.SongDataSchema.Columns[colName]!.DataType);
				}
			}
		}

		foreach (var song in songs)
		{
			var newRow = dataTable.NewRow();
			foreach (DataColumn col in dataTable.Columns)
			{
				if (song.TryGetValue(col.ColumnName, out var value)) { newRow[col.ColumnName] = value ?? DBNull.Value; }
			}
			dataTable.Rows.Add(newRow);
		}

		return dataTable;
	}

	public static string QuerySong(string lang, string singer, string words, string condition, string page, string rows, string sort)
	{
		if (SongDatas.SongData == null || SongDatas.SongData.Count == 0 || SongDatas.SongDataSchema == null)
			return EmptyResult;

		try
		{
			System.Diagnostics.Debug.WriteLine($"[QuerySong] lang={lang}, singer={singer}, words={words}, condition={condition}, page={page}, rows={rows}, sort={sort}");

			string rowFilter = !string.IsNullOrEmpty(condition) ? condition : string.Empty;

			// Filter songs based on criteria
			var filteredSongs = SongDatas.SongData.Where(x =>
			{
				if (lang != string.Empty)
					return Convert.ToString(x["Song_Lang"]) == lang;

				string songId = Convert.ToString(x["Song_Id"]) ?? string.Empty;
				if (songId == string.Empty)
					return false;

				if (singer != string.Empty)
					return Convert.ToString(x["Song_Singer"]) == singer;

				if (words != string.Empty)
					return Convert.ToString(x["Song_WordCount"]) == words;

				return true;
			}).ToList();

			if (filteredSongs.Count == 0)
				return EmptyResult;

			using DataTable dataTable = CreateSongDataTable(filteredSongs, SongDatas.SongDataSchema.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToArray());

			// Apply row filter
			if (!string.IsNullOrEmpty(rowFilter))
			{
				try
				{
					System.Diagnostics.Debug.WriteLine($"[QuerySong] Applying rowFilter: {rowFilter}");
					dataTable.DefaultView.RowFilter = rowFilter;
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"[QuerySong] RowFilter error: {ex.Message}");
					return ErrorResult("RowFilter Error", ex.Message);
				}
			}

			// Apply sort
			if (!string.IsNullOrEmpty(sort))
			{
				try
				{
					System.Diagnostics.Debug.WriteLine($"[QuerySong] Applying sort: {sort}");
					dataTable.DefaultView.Sort = sort;
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"[QuerySong] Sort error: {ex.Message}");
					return ErrorResult("Sort Error", ex.Message);
				}
			}

			// Select specific columns that exist
			var desiredColumns = new[] {
				"Song_Id", "Song_SongName", "Song_Singer", "Song_WordCount", "Song_Lang",
				"Song_PlayCount", "Song_CreatDate", "Song_SingerType", "Song_SongType",
				"Song_SongStroke", "Song_CashboxId"
			};

			// Only select columns that actually exist in the DataTable
			var existingColumns = desiredColumns.Where(col => dataTable.Columns.Contains(col)).ToArray();

			System.Diagnostics.Debug.WriteLine($"[QuerySong] Available columns: {string.Join(", ", dataTable.Columns.Cast<DataColumn>().Select(c => c.ColumnName))}");
			System.Diagnostics.Debug.WriteLine($"[QuerySong] Selecting columns: {string.Join(", ", existingColumns)}");

			using DataTable resultTable = dataTable.DefaultView.ToTable(false, existingColumns);

			if (resultTable.Rows.Count == 0)
				return EmptyResult;

			// Get the data as a list of dictionaries
			var songsList = resultTable.ToDictionary();

			// If a custom sort parameter is provided, use the DataView's sorted order (already applied above).
			// Otherwise, apply the default user-defined sorting.
			List<Dictionary<string, object?>> sortedSongs;
			if (!string.IsNullOrEmpty(sort))
			{
				// Custom sort was already applied to dataTable.DefaultView, so use the order from resultTable
				sortedSongs = songsList;
			}
			else
			{
				// No custom sort, apply the default user-defined sorting
				sortedSongs = SongDatas.ApplySongSorting(songsList, (s, key) => s.GetValueOrDefault(key));
			}

			// Apply pagination
			if (string.IsNullOrEmpty(rows) || !IsNumeric(rows))
				return JsonConvert.SerializeObject(sortedSongs);

			int pageIndex = (string.IsNullOrEmpty(page) || !IsNumeric(page)) ? 1 : Convert.ToInt32(page) + 1;
			int pageSize = Convert.ToInt32(rows);
			var pagedSongs = sortedSongs.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToList();

			return JsonConvert.SerializeObject(pagedSongs);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[QuerySong] Exception: {ex.Message}");
			System.Diagnostics.Debug.WriteLine($"[QuerySong] StackTrace: {ex.StackTrace}");
			return ErrorResult("Query Error", ex.Message);
		}
	}

	private static string GetSingerTypeIndex(string singerTypeName)
	{
		// Map singer type names to their indices in SingerTypeList
		if (SongDatas.SingerTypeList == null)
		{
			System.Diagnostics.Debug.WriteLine($"[GetSingerTypeIndex] SingerTypeList is null");
			return "";
		}

		// Log the entire list
		System.Diagnostics.Debug.WriteLine($"[GetSingerTypeIndex] SingerTypeList contents: {string.Join(", ", SongDatas.SingerTypeList.Select((s, i) => $"[{i}]='{s}'"))}");

		int index = SongDatas.SingerTypeList.IndexOf(singerTypeName);
		if (index >= 0)
		{
			System.Diagnostics.Debug.WriteLine($"[GetSingerTypeIndex] '{singerTypeName}' -> index {index}");
			return index.ToString();
		}

		System.Diagnostics.Debug.WriteLine($"[GetSingerTypeIndex] '{singerTypeName}' not found in list");
		return "";
	}

	public static string QuerySinger(string condition, string page, string rows, string sort, object singerData, object mainDataContext)
	{
		// Use cached table if available, else fallback (though fallback logic is not strictly needed if initialization is correct)
		if (HttpServer.CachedSingerTable == null)
		{
			// Fallback: This handles the case if server started very quickly or failed to init cache
			if (SongDatas.SingerData == null) return EmptyResult;
			// ... (Existing logic could go here, but let's just return error or empty for simplicity/safety)
			System.Diagnostics.Debug.WriteLine("[QuerySinger] CachedSingerTable is null! Pre-sorting not ready.");
			return EmptyResult; 
		}

		try
		{
			System.Diagnostics.Debug.WriteLine($"[QuerySinger] condition={condition}, page={page}, rows={rows}, sort={sort}");

			// Use a DataView on the CACHED (pre-sorted) table
			DataView view = new DataView(HttpServer.CachedSingerTable);

			// Apply filter
			string rowFilter = !string.IsNullOrEmpty(condition) ? condition : string.Empty;
			if (!string.IsNullOrEmpty(rowFilter))
			{
				try
				{
					view.RowFilter = rowFilter;
				}
				catch (Exception ex)
				{
					return ErrorResult("RowFilter Error", ex.Message);
				}
			}

            // Apply sort
            // The cached table is already sorted by the global logic (English first, then Chinese by stroke).
            // If the requested sort is "Singer_Strokes", we deliberately IGNORE it to preserve the complex global sort order.
            // If we applied a simple "Singer_Strokes ASC" sort here, it would sort purely by number, breaking the "English first" rule
            // and potentially mixing mixed-language result sets incorrectly.
            if (!string.IsNullOrEmpty(sort))
            {
                 // Only apply sort if it's NOT the default "Singer_Strokes" request.
                 // This ensures we respect the complex cultural sorting from SortedSingerData.
                 if (!sort.Trim().Equals("Singer_Strokes", StringComparison.OrdinalIgnoreCase))
                 {
                     view.Sort = sort;
                 }
                 else
                 {
                     // Explicitly clear any sort to ensure we use the underlying table's natural order (which is sorted)
                     view.Sort = string.Empty;
                 }
            }
            else
            {
                 // Default: use underlying table order
                 view.Sort = string.Empty;
            }

			// Select specific columns
			using DataTable resultTable = view.ToTable(false,
				"Singer_Name", "Singer_Type", "Singer_Strokes");

			if (resultTable.Rows.Count == 0)
				return EmptyResult;

			// Apply pagination
			if (string.IsNullOrEmpty(rows) || !IsNumeric(rows))
				return DataTableToJSON(resultTable);

			int pageIndex = (string.IsNullOrEmpty(page) || !IsNumeric(page)) ? 1 : Convert.ToInt32(page) + 1;
			int pageSize = Convert.ToInt32(rows);
			return DataTableToJSON(resultTable.GetPagedTable(pageIndex, pageSize));
		}
		catch (Exception ex)
		{
			return ErrorResult("Query Error", ex.Message);
		}
	}

	public static string ViewSong(string page, string rows, object playListData)
	{
		if (SongDatas.PlayListData == null || SongDatas.PlayListData.Rows.Count == 0)
			return EmptyResult;

		try
		{
			using DataTable dataTable = SongDatas.PlayListData.DefaultView.ToTable();
			if (dataTable.Rows.Count == 0)
				return EmptyResult;

			// Keep only specific columns
			string[] columnsToKeep = { "Song_Id", "Song_SongName", "Song_Singer", "Song_WordCount", "Song_Lang", "Song_PrimaryKey" };
			var columnsToRemove = dataTable.Columns.Cast<DataColumn>()
				.Where(col => !columnsToKeep.Contains(col.ColumnName))
				.ToList();

			foreach (var col in columnsToRemove)
			{
				dataTable.Columns.Remove(col);
			}

			// Apply pagination
			if (string.IsNullOrEmpty(rows) || !IsNumeric(rows))
				return DataTableToJSON(dataTable);

			int pageIndex = (string.IsNullOrEmpty(page) || !IsNumeric(page)) ? 1 : Convert.ToInt32(page) + 1;
			int pageSize = Convert.ToInt32(rows);
			return DataTableToJSON(dataTable.GetPagedTable(pageIndex, pageSize));
		}
		catch (Exception ex)
		{
			return ErrorResult("Query Error", ex.Message);
		}
	}

	public static string FavoriteUser(string page, string rows, object favoriteUserData)
	{
		try
		{
			// Use the shared sorting logic from SongDatas
			// We don't include padding for the web service as it uses a different UI layout
			var users = SongDatas.GetSortedFavoriteUsers(includePadding: false);

			if (users == null || users.Count == 0)
				return EmptyResult;

			// Apply pagination
			if (!string.IsNullOrEmpty(rows) && IsNumeric(rows))
			{
				int pageSize = Convert.ToInt32(rows);
				// The web client uses 0-based indexing for pages
				int pageIndex = (string.IsNullOrEmpty(page) || !IsNumeric(page)) ? 0 : Convert.ToInt32(page);
				
				var pagedUsers = users.Skip(pageIndex * pageSize).Take(pageSize).ToList();
				return JsonConvert.SerializeObject(pagedUsers);
			}

			return JsonConvert.SerializeObject(users);
		}
		catch (Exception ex)
		{
			return ErrorResult("Query Error", ex.Message);
		}
	}

	public static string FavoriteLogin(string user, object favoriteUserData)
	{
		if (SongDatas.FavoriteUserData == null || SongDatas.FavoriteUserData.Rows.Count == 0)
			return EmptyResult;

		if (string.IsNullOrEmpty(user))
			return EmptyResult;

		try
		{
			var userExists = SongDatas.FavoriteUserData.AsEnumerable()
				.Any(row => Convert.ToString(row["User_Id"]) == user);

			if (userExists)
			{
				FavoriteLoginID = user;
			}

			return EmptyResult;
		}
		catch
		{
			return EmptyResult;
		}
	}

	public static string FavoriteSong(string user, string page, string rows, object favoriteSongData, object songData)
	{
		if (FavoriteLoginID != user || SongDatas.FavoriteSongData == null || SongDatas.FavoriteSongData.Count == 0)
			return EmptyResult;

		if (string.IsNullOrEmpty(user))
			return EmptyResult;

		try
		{
			// Get favorite song IDs for this user
			var favSongIds = SongDatas.FavoriteSongData
				.Where(x => Convert.ToString(x["User_Id"]) == user)
				.Select(x => Convert.ToString(x["Song_Id"]))
				.ToList();

			if (favSongIds.Count == 0)
				return EmptyResult;

			// Get songs that match the favorite IDs
			var favoriteSongs = SongDatas.SongData?
				.Where(x => favSongIds.Contains(Convert.ToString(x["Song_Id"])))
				.OrderBy(x => x["Song_WordCount"])
				.ThenBy(x => x["Song_SongStroke"])
				.ThenBy(x => x["Song_SongName"])
				.ThenBy(x => x["Song_Singer"])
				.ToList();

			if (favoriteSongs == null || favoriteSongs.Count == 0)
				return EmptyResult;

			// Convert to DataTable
			using DataTable dataTable = new DataTable();
			string[] columnsToInclude = { "Song_Id", "Song_SongName", "Song_Singer", "Song_WordCount", "Song_Lang", "Song_SongStroke" };

			foreach (var colName in columnsToInclude)
			{
				dataTable.Columns.Add(colName, typeof(string));
			}

			foreach (var song in favoriteSongs)
			{
				var row = dataTable.NewRow();
				foreach (var colName in columnsToInclude)
				{
					if (song.ContainsKey(colName))
						row[colName] = song[colName] ?? DBNull.Value;
				}
				dataTable.Rows.Add(row);
			}

			// Apply pagination
			if (string.IsNullOrEmpty(rows) || !IsNumeric(rows))
				return DataTableToJSON(dataTable);

			int pageIndex = (string.IsNullOrEmpty(page) || !IsNumeric(page)) ? 1 : Convert.ToInt32(page) + 1;
			int pageSize = Convert.ToInt32(rows);
			return DataTableToJSON(dataTable.GetPagedTable(pageIndex, pageSize));
		}
		catch (Exception ex)
		{
			return ErrorResult("Query Error", ex.Message);
		}
	}

	public static string AddFavoriteSong(string user, string value, object favoriteSongData, object mainDataContext)
	{
		if (FavoriteLoginID != user || SongDatas.FavoriteSongData == null)
			return EmptyResult;

		if (string.IsNullOrEmpty(user))
			return EmptyResult;

		try
		{
			string songId = value; // Use the provided value as song ID

			// Check if already in favorites
			var alreadyExists = SongDatas.FavoriteSongData
				.Any(x => Convert.ToString(x["User_Id"]) == user && Convert.ToString(x["Song_Id"]) == songId);

			if (alreadyExists)
				return EmptyResult;

			// Add to favorites (Note: This would need database write access)
			// For now, just add to in-memory collection
			SongDatas.FavoriteSongData.Add(new Dictionary<string, object?>
			{
				{ "User_Id", user },
				{ "Song_Id", songId }
			});

			return EmptyResult;
		}
		catch
		{
			return EmptyResult;
		}
	}

	public static string OrderSong(string value, string userName, object playListData, object songData, object mainDataContext)
	{
		if (string.IsNullOrEmpty(value) || SongDatas.PlayListData == null || SongDatas.SongData == null)
			return EmptyResult;

		try
		{
			System.Diagnostics.Debug.WriteLine($"[OrderSong] Ordering song: {value}, User: {userName}");

			// Find the song in SongData
			var song = SongDatas.SongData.FirstOrDefault(x => Convert.ToString(x["Song_Id"]) == value);
			if (song == null)
			{
				System.Diagnostics.Debug.WriteLine($"[OrderSong] Song not found: {value}");
				return EmptyResult;
			}

			// Check if song already exists in playlist using Select instead of Find
			string primaryKey = value;
			var existingRows = SongDatas.PlayListData.Select($"Song_PrimaryKey = '{value}'");
			if (existingRows.Length > 0)
			{
				// Try to add with suffix
				for (int i = 1; i <= 9; i++)
				{
					string testKey = $"{value}-{i}";
					var testRows = SongDatas.PlayListData.Select($"Song_PrimaryKey = '{testKey}'");
					if (testRows.Length == 0)
					{
						primaryKey = testKey;
						break;
					}
					if (i == 9)
					{
						System.Diagnostics.Debug.WriteLine($"[OrderSong] Max duplicates reached for: {value}");
						return EmptyResult; // Max duplicates reached
					}
				}
			}

			// Add to playlist
			var newRow = SongDatas.PlayListData.NewRow();
			foreach (DataColumn col in SongDatas.PlayListData.Columns)
			{
				if (song.ContainsKey(col.ColumnName))
					newRow[col.ColumnName] = song[col.ColumnName] ?? DBNull.Value;
			}
			// Instead of adding to PlayListData, we need to add to the waiting list via the UI thread
			// Create a SongDisplayItem from the song data
			var songItem = new UltimateKtv.SongDisplayItem
			{
				SongId = song.ContainsKey("Song_Id") ? Convert.ToString(song["Song_Id"]) ?? "" : "",
				SongName = song.ContainsKey("Song_SongName") ? Convert.ToString(song["Song_SongName"]) ?? "" : "",
				SingerName = song.ContainsKey("Song_Singer") ? Convert.ToString(song["Song_Singer"]) ?? "" : "",
				Language = song.ContainsKey("Song_Lang") ? Convert.ToString(song["Song_Lang"]) ?? "" : "",
				FilePath = song.ContainsKey("FilePath") ? Convert.ToString(song["FilePath"]) ?? "" : "",
				Volume = song.ContainsKey("Song_Volume") && int.TryParse(Convert.ToString(song["Song_Volume"]), out int vol) ? vol : 90,
				AudioTrack = song.ContainsKey("Song_Track") && int.TryParse(Convert.ToString(song["Song_Track"]), out int track) ? track : 0,
				OrderedBy = !string.IsNullOrEmpty(userName) ? userName : "網路點歌"
			};

			// Validate FilePath
			if (string.IsNullOrEmpty(songItem.FilePath) || !File.Exists(songItem.FilePath))
			{
				System.Diagnostics.Debug.WriteLine($"[OrderSong] Invalid FilePath: {songItem.FilePath}");
				return JsonConvert.SerializeObject(new { success = false, message = "找不到歌曲檔案", songId = value });
			}

			// Add to waiting list on UI thread
			System.Windows.Application.Current?.Dispatcher.BeginInvoke((Action)(() =>
			{
				try
				{
					// Find MainWindow and call AddSongToWaitingList
					var mainWindow = System.Windows.Application.Current.MainWindow as UltimateKtv.MainWindow;
					if (mainWindow != null)
					{
						// Use reflection to call the private method
						var method = mainWindow.GetType().GetMethod("AddSongToWaitingList",
							System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
						method?.Invoke(mainWindow, new object[] { songItem });
						System.Diagnostics.Debug.WriteLine($"[OrderSong] Song added to waiting list via UI thread: {songItem.SongName}");
					}
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"[OrderSong] Failed to add to waiting list: {ex.Message}");
				}
			}));

			System.Diagnostics.Debug.WriteLine($"[OrderSong] Song queued successfully: {primaryKey}");
			return JsonConvert.SerializeObject(new { success = true, message = "點歌成功", songId = primaryKey });
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[OrderSong] Exception: {ex.Message}");
			return EmptyResult;
		}
	}

	public static string QueryPlayerState(string value, object mainDataContext)
	{
		// Return basic player state info
		try
		{
			var state = new JObject
			{
				{ "IsPlaying", false },
				{ "CurrentSong", "" },
				{ "PlaylistCount", SongDatas.PlayListData?.Rows.Count ?? 0 }
			};
			return JsonConvert.SerializeObject(state);
		}
		catch
		{
			return EmptyResult;
		}
	}

	public static string QueryHostServerInfo(string value, object mainDataContext)
	{
		// Return basic server info
		try
		{
			var info = new JObject
			{
				{ "ServerName", "UltimateKTV" },
				{ "Version", "1.0" },
				{ "SongCount", SongDatas.SongData?.Count ?? 0 },
				{ "GenerationNames", SongDatas.GenerationNames != null ? JObject.FromObject(SongDatas.GenerationNames) : null }
			};
			return JsonConvert.SerializeObject(info);
		}
		catch
		{
			return EmptyResult;
		}
	}

	public static string QueryPlaylist(string value, object mainDataContext)
	{
		try
		{
			var playlist = new JArray();

			// Access the waiting list on the UI thread
			System.Windows.Application.Current?.Dispatcher.Invoke(() =>
			{
				var mainWindow = System.Windows.Application.Current.MainWindow as UltimateKtv.MainWindow;
				if (mainWindow != null)
				{
					// Use reflection to get the private _waitingList field
					var waitingListField = mainWindow.GetType().GetField("_waitingList",
						System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

					if (waitingListField != null)
					{
						var waitingList = waitingListField.GetValue(mainWindow) as System.Collections.IList;
						if (waitingList != null)
						{
							foreach (var item in waitingList)
							{
								var songName = item?.GetType().GetProperty("WaitingListSongName")?.GetValue(item)?.ToString();
								var singerName = item?.GetType().GetProperty("WaitingListSingerName")?.GetValue(item)?.ToString();
								var songId = item?.GetType().GetProperty("SongId")?.GetValue(item)?.ToString();
								var orderedBy = item?.GetType().GetProperty("OrderedBy")?.GetValue(item)?.ToString();

								if (!string.IsNullOrEmpty(songName))
								{
									var songObject = new JObject
									{
										{ "SongName", songName },
										{ "SingerName", singerName },
										{ "SongId", songId },
										{ "OrderedBy", orderedBy ?? "" },
                                        { "Id", item?.GetType().GetProperty("Id")?.GetValue(item)?.ToString() ?? "" }
									};
									playlist.Add(songObject);
								}
							}
						}
					}
				}
			});

			return JsonConvert.SerializeObject(playlist);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[QueryPlaylist] Exception: {ex.Message}");
			return ErrorResult("Query Error", ex.Message);
		}
	}

	public static string SkipCurrentSong(string value, object mainDataContext)
	{
		try
		{
			// Dispatch the skip action to the UI thread
			System.Windows.Application.Current?.Dispatcher.BeginInvoke((Action)(() =>
			{
				var mainWindow = System.Windows.Application.Current.MainWindow as UltimateKtv.MainWindow;
				mainWindow?.SkipSong_Click(null!, null!);
			}));

			return JsonConvert.SerializeObject(new { success = true, message = "切歌指令已送出" });
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[SkipCurrentSong] Exception: {ex.Message}");
			return ErrorResult("Skip Error", ex.Message);
		}
	}

	public static string SwitchToVocal(string value, object mainDataContext)
	{
		try
		{
			// Dispatch the vocal action to the UI thread
			System.Windows.Application.Current?.Dispatcher.BeginInvoke((Action)(() =>
			{
				var mainWindow = System.Windows.Application.Current.MainWindow as UltimateKtv.MainWindow;
				mainWindow?.VocalBtn_Click(null!, null!);
			}));

			return JsonConvert.SerializeObject(new { success = true, message = "已切換至人聲" });
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[SwitchToVocal] Exception: {ex.Message}");
			return ErrorResult("Vocal Switch Error", ex.Message);
		}
	}

	public static string SwitchToMusic(string value, object mainDataContext)
	{
		try
		{
			// Dispatch the music action to the UI thread
			System.Windows.Application.Current?.Dispatcher.BeginInvoke((Action)(() =>
			{
				var mainWindow = System.Windows.Application.Current.MainWindow as UltimateKtv.MainWindow;
				mainWindow?.MusicBtn_Click(null!, null!);
			}));

			return JsonConvert.SerializeObject(new { success = true, message = "已切換至伴唱" });
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[SwitchToMusic] Exception: {ex.Message}");
			return ErrorResult("Music Switch Error", ex.Message);
		}
	}

    public static string MoveToFirst(string id, object mainDataContext)
    {
        try
        {
            if (Guid.TryParse(id, out Guid guid))
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke((Action)(() =>
                {
                    var mainWindow = System.Windows.Application.Current.MainWindow as UltimateKtv.MainWindow;
                    mainWindow?.MoveSongToFirst(guid);
                }));
                return JsonConvert.SerializeObject(new { success = true, message = "已移動至第一首" });
            }
            return ErrorResult("Invalid ID", "Provided ID is not a valid GUID");
        }
        catch (Exception ex)
        {
            return ErrorResult("Error", ex.Message);
        }
    }

    public static string RemoveFromList(string id, object mainDataContext)
    {
        try
        {
            if (Guid.TryParse(id, out Guid guid))
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke((Action)(() =>
                {
                    var mainWindow = System.Windows.Application.Current.MainWindow as UltimateKtv.MainWindow;
                    mainWindow?.RemoveSongFromList(guid);
                }));
                return JsonConvert.SerializeObject(new { success = true, message = "已從清單移除" });
            }
            return ErrorResult("Invalid ID", "Provided ID is not a valid GUID");
        }
        catch (Exception ex)
        {
            return ErrorResult("Error", ex.Message);
        }
    }

    public static string MoveToNext(string id, object mainDataContext)
    {
        try
        {
            if (Guid.TryParse(id, out Guid guid))
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke((Action)(() =>
                {
                    var mainWindow = System.Windows.Application.Current.MainWindow as UltimateKtv.MainWindow;
                    mainWindow?.MoveSongToNext(guid);
                }));
                return JsonConvert.SerializeObject(new { success = true, message = "已下移一首" });
            }
            return ErrorResult("Invalid ID", "Provided ID is not a valid GUID");
        }
        catch (Exception ex)
        {
            return ErrorResult("Error", ex.Message);
        }
    }

	public static string QueryGeneration(string genKey, string page, string rows)
	{
		if (string.IsNullOrEmpty(genKey) || SongDatas.GenerationSongs == null)
			return EmptyResult;

		try
		{
			System.Diagnostics.Debug.WriteLine($"[QueryGeneration] genKey={genKey}, page={page}, rows={rows}");

			// Retrieve the pre-cached song list for the given generation key.
			if (!SongDatas.GenerationSongs.TryGetValue(genKey, out var cachedSongs) || cachedSongs == null || cachedSongs.Count == 0)
			{
				System.Diagnostics.Debug.WriteLine($"[QueryGeneration] No cached songs found for genKey: {genKey}");
				return EmptyResult;
			}

			// Convert the cached data into a DataTable for pagination and serialization.
			// This is very fast as it's all in-memory.
			using DataTable dataTable = new DataTable();
			string[] columnsToInclude = { "Song_Id", "Song_SongName", "Song_Singer", "Song_WordCount", "Song_Lang", "Song_PlayCount", "Song_CreatDate", "Song_SingerType", "Song_SongType", "Song_SongStroke" };

			// Use the main song data schema to ensure data types are correct.
			if (SongDatas.SongDataSchema != null)
			{
				foreach (var colName in columnsToInclude)
				{
					if (SongDatas.SongDataSchema.Columns.Contains(colName))
					{
						dataTable.Columns.Add(colName, SongDatas.SongDataSchema.Columns[colName]!.DataType);
					}
				}
			}

			foreach (var song in cachedSongs)
			{
				var row = dataTable.NewRow();
				foreach (var colName in columnsToInclude)
				{
					if (song.ContainsKey(colName) && dataTable.Columns.Contains(colName))
						row[colName] = song[colName] ?? DBNull.Value;
				}
				dataTable.Rows.Add(row);
			}

			// Apply pagination
			if (string.IsNullOrEmpty(rows) || !IsNumeric(rows))
				return DataTableToJSON(dataTable);

			int pageIndex = (string.IsNullOrEmpty(page) || !IsNumeric(page)) ? 1 : Convert.ToInt32(page) + 1;
			int pageSize = Convert.ToInt32(rows);
			return DataTableToJSON(dataTable.GetPagedTable(pageIndex, pageSize));
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[QueryGeneration] Exception: {ex.Message}");
			return ErrorResult("Query Error", ex.Message);
		}
	}

			public static string QueryNewSong(string lang, string page, string rows)
			{
				if (SongDatas.SongData == null || SongDatas.SongData.Count == 0)
					return EmptyResult;
	
				try
				{
					System.Diagnostics.Debug.WriteLine($"[QueryNewSong] lang={lang}, page={page}, rows={rows}");
	
					var settings = SettingsManager.Instance.CurrentSettings;
					var cutoffDate = DateTime.Today.AddDays(-settings.NewSongDays);
	
					System.Diagnostics.Debug.WriteLine($"[QueryNewSong] Total songs to filter: {SongDatas.SongData.Count}");
					System.Diagnostics.Debug.WriteLine($"[QueryNewSong] Filtering songs newer than: {cutoffDate:yyyy-MM-dd}");
	
					var newSongs = SongDatas.SongData
						.Where(s =>
						{
							if (s == null) return false;
	
							// Filter by date
							bool isNew = false;
							if (s.TryGetValue("Song_CreatDate", out var dateObj) && dateObj is DateTime createDate)
							{
								isNew = createDate >= cutoffDate;
							}
							if (!isNew) return false;
	
							return true; // Keep the song if it's new, language filter will be applied next
						})
						.ToList();
	
					System.Diagnostics.Debug.WriteLine($"[QueryNewSong] Songs after date filter: {newSongs.Count}");
	
					// Apply language filter after date filter
					if (!string.IsNullOrEmpty(lang))
					{
						newSongs = newSongs.Where(s =>
						{
							var songLang = s.TryGetValue("Song_Lang", out var langObj) ? langObj?.ToString() : "";
							if (lang == "其它") return songLang != "國語" && songLang != "台語";
							return songLang == lang;
						}).ToList();
					}
	
					System.Diagnostics.Debug.WriteLine($"[QueryNewSong] Songs after language filter ('{lang}'): {newSongs.Count}");
	
					// Sort by creation date (newest first)
					var sortedSongs = SongDatas.ApplySongSorting(newSongs, (s, key) => s.GetValueOrDefault(key));
	
					if (sortedSongs.Count == 0)
						return EmptyResult;
	
					System.Diagnostics.Debug.WriteLine($"[QueryNewSong] Schema available: {SongDatas.SongDataSchema != null}");
	
	
					string[] columnsToInclude = { "Song_Id", "Song_SongName", "Song_Singer", "Song_Lang", "Song_CreatDate" };
					using DataTable dataTable = CreateSongDataTable(sortedSongs, columnsToInclude);
	
					System.Diagnostics.Debug.WriteLine($"[QueryNewSong] DataTable rows imported: {dataTable.Rows.Count}");
	
					int pageIndex = (string.IsNullOrEmpty(page) || !IsNumeric(page)) ? 1 : Convert.ToInt32(page) + 1;
					int pageSize = (string.IsNullOrEmpty(rows) || !IsNumeric(rows)) ? 50 : Convert.ToInt32(rows);
					var pagedTable = dataTable.GetPagedTable(pageIndex, pageSize);
					System.Diagnostics.Debug.WriteLine($"[QueryNewSong] Rows after pagination: {pagedTable.Rows.Count}");
					return DataTableToJSON(pagedTable);
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"[QueryNewSong] Exception: {ex.Message}");
					return ErrorResult("Query Error", ex.Message);
				}
			}
	
	    #region Remote Control Actions
	
	    public static string VolumeUp(string value, object mainDataContext)
	    {
	        try
	        {
	            System.Windows.Application.Current?.Dispatcher.BeginInvoke((Action)(() =>
	            {
	                var mainWindow = System.Windows.Application.Current.MainWindow as UltimateKtv.MainWindow;
	                mainWindow?.VolumeUp();
	            }));
	            return JsonConvert.SerializeObject(new { success = true, message = "已提升音量" });
	        }
	        catch (Exception ex)
	        {
	            return ErrorResult("VolumeUp Error", ex.Message);
	        }
	    }
	
	    public static string VolumeDown(string value, object mainDataContext)
	    {
	        try
	        {
	            System.Windows.Application.Current?.Dispatcher.BeginInvoke((Action)(() =>
	            {
	                var mainWindow = System.Windows.Application.Current.MainWindow as UltimateKtv.MainWindow;
	                mainWindow?.VolumeDown();
	            }));
	            return JsonConvert.SerializeObject(new { success = true, message = "已降低音量" });
	        }
	        catch (Exception ex)
	        {
	            return ErrorResult("VolumeDown Error", ex.Message);
	        }
	    }
	
	    public static string ToggleVolumeLock(string value, object mainDataContext)
	    {
	        try
	        {
	            bool isLocking = false;
	            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
	            {
	                var mainWindow = System.Windows.Application.Current.MainWindow as UltimateKtv.MainWindow;
	                mainWindow?.ToggleVolumeLock();
	                isLocking = mainWindow?.VolumeLockToggle.IsChecked ?? false;
	            });
	            return JsonConvert.SerializeObject(new { success = true, message = isLocking ? "音量已鎖定" : "音量已解鎖" });
	        }
	        catch (Exception ex)
	        {
	            return ErrorResult("ToggleVolumeLock Error", ex.Message);
	        }
	    }
	
	    public static string PitchUp(string value, object mainDataContext)
	    {
	        try
	        {
	            System.Windows.Application.Current?.Dispatcher.BeginInvoke((Action)(() =>
	            {
	                var mainWindow = System.Windows.Application.Current.MainWindow as UltimateKtv.MainWindow;
	                mainWindow?.AdjustPitch(1);
	            }));
	            return JsonConvert.SerializeObject(new { success = true, message = "已升調" });
	        }
	        catch (Exception ex)
	        {
	            return ErrorResult("PitchUp Error", ex.Message);
	        }
	    }
	
	    public static string PitchDown(string value, object mainDataContext)
	    {
	        try
	        {
	            System.Windows.Application.Current?.Dispatcher.BeginInvoke((Action)(() =>
	            {
	                var mainWindow = System.Windows.Application.Current.MainWindow as UltimateKtv.MainWindow;
	                mainWindow?.AdjustPitch(-1);
	            }));
	            return JsonConvert.SerializeObject(new { success = true, message = "已降調" });
	        }
	        catch (Exception ex)
	        {
	            return ErrorResult("PitchDown Error", ex.Message);
	        }
	    }
	
	    public static string PitchReset(string value, object mainDataContext)
	    {
	        try
	        {
	            System.Windows.Application.Current?.Dispatcher.BeginInvoke((Action)(() =>
	            {
	                var mainWindow = System.Windows.Application.Current.MainWindow as UltimateKtv.MainWindow;
	                mainWindow?.SetOriginalPitch();
	            }));
	            return JsonConvert.SerializeObject(new { success = true, message = "已恢復原調" });
	        }
	        catch (Exception ex)
	        {
	            return ErrorResult("PitchReset Error", ex.Message);
	        }
	    }

	    public static string QuitApp(string value, object mainDataContext)
	    {
	        try
	        {
	            System.Windows.Application.Current?.Dispatcher.BeginInvoke((Action)(() =>
	            {
	                System.Windows.Application.Current?.Shutdown();
	            }));
	            return JsonConvert.SerializeObject(new { success = true, message = "程式即將關閉" });
	        }
	        catch (Exception ex)
	        {
	            return ErrorResult("QuitApp Error", ex.Message);
	        }
	    }

	    public static async Task<string> SearchYoutubeAsync(string keyword)
	    {
	        if (string.IsNullOrWhiteSpace(keyword)) return EmptyResult;

	        try
	        {
	            var youtube = new YoutubeClient();
	            var youtubeResults = await youtube.Search.GetVideosAsync(keyword).CollectAsync(50);

	            var results = youtubeResults.Select(v => new
	            {
	                Song_Id = v.Id.Value,
	                Song_SongName = v.Title,
	                Song_Singer = v.Author.ChannelTitle,
	                Song_Lang = v.Duration?.ToString(@"mm\:ss") ?? "",
	                FilePath = v.Url,
	                IsYoutube = true,
	                Song_Volume = 30
	            }).ToList();

	            return JsonConvert.SerializeObject(results);
	        }
	        catch (Exception ex)
	        {
	            System.Diagnostics.Debug.WriteLine($"[SearchYoutubeAsync] Exception: {ex.Message}");
	            return ErrorResult("Search Error", ex.Message);
	        }
	    }

	    public static async Task<string> OrderYoutubeAsync(string videoId, string userName, object mainDataContext)
	    {
	        if (string.IsNullOrWhiteSpace(videoId)) return EmptyResult;

	        try
	        {
	            var youtube = new YoutubeClient();
	            var video = await youtube.Videos.GetAsync(videoId);

	            var songItem = new UltimateKtv.SongDisplayItem
	            {
	                SongId = video.Id.Value,
	                SongName = video.Title,
	                SingerName = video.Author.ChannelTitle,
	                Language = video.Duration?.ToString(@"mm\:ss") ?? "",
	                FilePath = video.Url,
	                IsYoutube = true,
	                Volume = 30,
	                OrderedBy = !string.IsNullOrEmpty(userName) ? userName : "網路點歌",
	                AudioTrack = 0
	            };

	            bool downloadQueued = false;
	            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
	            {
	                try
	                {
	                    var mainWindow = System.Windows.Application.Current.MainWindow as UltimateKtv.MainWindow;
	                    if (mainWindow != null)
	                    {
                            if (mainWindow.IsDownloadingYoutube)
                            {
                                return; // Handled below by returning error
                            }

                            // Trigger the host-side download using reflection since it's a private method
	                        var method = mainWindow.GetType().GetMethod("DownloadYoutubeVideo",
	                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
	                        method?.Invoke(mainWindow, new object[] { songItem });
	                        System.Diagnostics.Debug.WriteLine($"[OrderYoutubeAsync] Triggered download via UI thread: {songItem.SongName}");
                            downloadQueued = true;
	                    }
	                }
	                catch (Exception ex)
	                {
	                    System.Diagnostics.Debug.WriteLine($"[OrderYoutubeAsync] Failed to trigger download: {ex.Message}");
	                }
	            });

                if (!downloadQueued)
                {
                    return JsonConvert.SerializeObject(new { success = false, message = "已有下載正在進行中，請稍候再試" });
                }

	            return JsonConvert.SerializeObject(new { success = true, message = "點歌成功" });
	        }
	        catch (Exception ex)
	        {
	            System.Diagnostics.Debug.WriteLine($"[OrderYoutubeAsync] Exception: {ex.Message}");
	            return ErrorResult("Order Error", ex.Message);
	        }
	    }
	
	    #endregion
	}
