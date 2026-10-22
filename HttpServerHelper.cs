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
using UltimateKtv.Playback;
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
	/// Creates a DataTable from a list of songs, including only the specified ktv_Song columns.
	/// </summary>
	private static DataTable CreateSongDataTable(IEnumerable<Song> songs, string[] columnsToInclude)
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
				newRow[col.ColumnName] = song.GetColumnValue(col.ColumnName) ?? DBNull.Value;
			}
			dataTable.Rows.Add(newRow);
		}

		return dataTable;
	}

	/// <summary>手機歌曲列表輸出的欄位。</summary>
	private static readonly string[] SongListColumns = {
		"Song_Id", "Song_SongName", "Song_Singer", "Song_WordCount", "Song_Lang",
		"Song_PlayCount", "Song_CreatDate", "Song_SingerType", "Song_SongType",
		"Song_SongStroke", "Song_CashboxId"
	};

	/// <summary>
	/// 取歌曲清單的一頁輸出成 JSON。page 從 0 開始；rows 不是數字時用 defaultRows (0 = 不分頁)。
	/// </summary>
	private static string SongPageToJson(List<Song> songs, string page, string rows, int defaultRows, string[]? columns = null)
	{
		if (songs.Count == 0) return EmptyResult;
		int number = IsNumeric(page) ? Convert.ToInt32(page) + 1 : 1;
		int size = IsNumeric(rows) ? Convert.ToInt32(rows) : defaultRows;
		var paged = Page<Song>.Of(songs, number, size);
		using DataTable dataTable = CreateSongDataTable(paged.Items, columns ?? SongListColumns);
		return JsonConvert.SerializeObject(dataTable.ToDictionary());
	}

	private static SongQueryOptions CurrentQueryOptions() => SongQueryOptions.FromSettings(SettingsManager.Instance.CurrentSettings);

	/// <summary>舊版網頁以 RowFilter 字串查詢 (querysong)；已改為有型別的查詢，提示重新整理頁面。</summary>
	public static string OutdatedPage() => ErrorResult("Outdated page", "Please reload this page.");

	/// <summary>手機搜尋框：歌名或歌星包含 text，lang 空白 = 全部語系。</summary>
	public static string SearchSongs(string text, string lang, string page, string rows)
	{
		try
		{
			return SongPageToJson(SongQuery.Search(SongDatas.SongData, text, lang, CurrentQueryOptions()), page, rows, 0);
		}
		catch (Exception ex)
		{
			return ErrorResult("Query Error", ex.Message);
		}
	}

	/// <summary>手機排行：filter 為按鈕的寫法 (例如「國語-單曲」「其它」)。</summary>
	public static string QueryRanking(string filter, string page, string rows)
	{
		try
		{
			return SongPageToJson(SongQuery.Ranking(SongDatas.SongData, filter), page, rows, 0);
		}
		catch (Exception ex)
		{
			return ErrorResult("Query Error", ex.Message);
		}
	}

	/// <summary>手機點歌星：該歌星的歌 (精確比對歌星名，含合唱)。</summary>
	public static string QuerySingerSongs(string singer, string page, string rows)
	{
		try
		{
			return SongPageToJson(SongQuery.BySinger(SongDatas.SongData, singer, CurrentQueryOptions()), page, rows, 0);
		}
		catch (Exception ex)
		{
			return ErrorResult("Query Error", ex.Message);
		}
	}

	/// <summary>手機輸入歌曲編號：找不到回傳空清單。</summary>
	public static string QuerySongById(string id)
	{
		var song = SongDatas.FindSongById(id);
		return song == null ? EmptyResult : SongPageToJson(new List<Song> { song }, "", "", 0);
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
				.Where(x => favSongIds.Contains(x.Id))
				.OrderBy(x => x.WordCount)
				.ThenBy(x => x.SongStroke)
				.ThenBy(x => x.Name)
				.ThenBy(x => x.Singer)
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
					row[colName] = song.GetColumnValue(colName) ?? DBNull.Value;
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

	/// <summary>
	/// 在 UI 執行緒執行非同步動作並等待結果 (HTTP 請求在 Kestrel 執行緒上，不會卡住 UI)。
	/// </summary>
	private static T RunOnUiThread<T>(Func<UltimateKtv.MainWindow, Task<T>> action)
	{
		var dispatcher = System.Windows.Application.Current?.Dispatcher
			?? throw new InvalidOperationException("Application is shutting down");
		return dispatcher.InvokeAsync(() =>
		{
			var mainWindow = System.Windows.Application.Current.MainWindow as UltimateKtv.MainWindow
				?? throw new InvalidOperationException("MainWindow is not available");
			return action(mainWindow);
		}).Task.Unwrap().GetAwaiter().GetResult();
	}

	public static string OrderSong(string value, string userName, object playListData, object songData, object mainDataContext)
	{
		if (string.IsNullOrEmpty(value) || SongDatas.PlayListData == null || SongDatas.SongData == null)
			return EmptyResult;

		try
		{
			System.Diagnostics.Debug.WriteLine($"[OrderSong] Ordering song: {value}, User: {userName}");

			// Find the song in SongData
			var song = SongDatas.FindSongById(value);
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

			// Add to the waiting list via the UI thread
			var songItem = UltimateKtv.SongDisplayItem.FromSong(song);
			songItem.OrderedBy = !string.IsNullOrEmpty(userName) ? userName : Loc.OrderedByWeb;

			// Validate FilePath
			if (string.IsNullOrEmpty(songItem.FilePath) || !File.Exists(songItem.FilePath))
			{
				System.Diagnostics.Debug.WriteLine($"[OrderSong] Invalid FilePath: {songItem.FilePath}");
				return JsonConvert.SerializeObject(new { success = false, message = Loc.T("Web.Msg.SongFileNotFound"), songId = value });
			}

			// 在 UI 執行緒點歌，等待真實結果 (重複、找不到檔案時回報失敗)
			var result = RunOnUiThread(mainWindow => mainWindow.OrderSongAsync(songItem, OrderSource.Remote));
			System.Diagnostics.Debug.WriteLine($"[OrderSong] {songItem.SongName}: {result}");

			return result switch
			{
				EnqueueResult.Added or EnqueueResult.MovedToFirst =>
					JsonConvert.SerializeObject(new { success = true, message = Loc.T("Web.Msg.OrderOk"), songId = primaryKey }),
				EnqueueResult.Duplicate =>
					JsonConvert.SerializeObject(new { success = false, message = Loc.T("Wait.DuplicateMessage", songItem.SongName, songItem.SingerName), songId = value }),
				_ =>
					JsonConvert.SerializeObject(new { success = false, message = Loc.T("Web.Msg.SongFileNotFound"), songId = value }),
			};
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[OrderSong] Exception: {ex.Message}");
			return EmptyResult;
		}
	}

	public static string QueryPlayerState(string value, object mainDataContext)
	{
		try
		{
			bool isPlaying = false;
			string songName = "";
			string singerName = "";
			string songId = "";
			string orderedBy = "";
			int playlistCount = 0;

			System.Windows.Application.Current?.Dispatcher.Invoke(() =>
			{
				if (System.Windows.Application.Current?.MainWindow is UltimateKtv.MainWindow mainWindow)
				{
					var info = mainWindow.GetCurrentPlayingSongInfo();
					isPlaying = info.IsPlaying;
					songName = info.SongName;
					singerName = info.SingerName;
					songId = info.SongId;
					orderedBy = info.OrderedBy;
					playlistCount = mainWindow.GetWaitingListCount();
				}
			});

			var state = new JObject
			{
				{ "IsPlaying", isPlaying },
				{ "SongName", songName },
				{ "SingerName", singerName },
				{ "SongId", songId },
				{ "CurrentSong", isPlaying ? (!string.IsNullOrEmpty(singerName) ? $"{songName} - {singerName}" : songName) : "" },
				{ "OrderedBy", orderedBy },
				{ "PlaylistCount", playlistCount }
			};
			return JsonConvert.SerializeObject(state);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[QueryPlayerState] Exception: {ex.Message}");
			return EmptyResult;
		}
	}

	public static string QueryYoutubeQueue()
	{
	    try
	    {
	        object status = new { count = 0, isDownloading = false };
	        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
	        {
	            if (System.Windows.Application.Current?.MainWindow is UltimateKtv.MainWindow mainWindow)
	            {
	                status = mainWindow.GetYoutubeDownloadQueueStatus();
	            }
	        });
	        return JsonConvert.SerializeObject(status);
	    }
	    catch
	    {
	        return JsonConvert.SerializeObject(new { count = 0, isDownloading = false });
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
			bool isPlaying = false;
			string playingSongName = "";
			string playingSingerName = "";
			string playingSongId = "";
			string playingOrderedBy = "";

			// Access the waiting list and current playing song on the UI thread
			System.Windows.Application.Current?.Dispatcher.Invoke(() =>
			{
				if (System.Windows.Application.Current?.MainWindow is UltimateKtv.MainWindow mainWindow)
				{
					var playInfo = mainWindow.GetCurrentPlayingSongInfo();
					isPlaying = playInfo.IsPlaying;
					playingSongName = playInfo.SongName;
					playingSingerName = playInfo.SingerName;
					playingSongId = playInfo.SongId;
					playingOrderedBy = playInfo.OrderedBy;

					var waitingList = mainWindow.GetWaitingListItems();
					foreach (var item in waitingList)
					{
						var songName = item.WaitingListSongName;
						var singerName = item.WaitingListSingerName;
						var songId = item.SongId;
						var orderedBy = item.OrderedBy;

						if (!string.IsNullOrEmpty(songName))
						{
							var songObject = new JObject
							{
								{ "SongName", songName },
								{ "SingerName", singerName },
								{ "SongId", songId ?? "" },
								{ "OrderedBy", orderedBy ?? "" },
								{ "Id", item.Id.ToString() }
							};
							playlist.Add(songObject);
						}
					}
				}
			});

			if (string.Equals(value, "withCurrent", StringComparison.OrdinalIgnoreCase) ||
			    string.Equals(value, "includePlaying", StringComparison.OrdinalIgnoreCase))
			{
				var result = new JObject
				{
					{ "nowPlaying", new JObject
						{
							{ "isPlaying", isPlaying },
							{ "songName", playingSongName },
							{ "singerName", playingSingerName },
							{ "songId", playingSongId },
							{ "orderedBy", playingOrderedBy },
							{ "currentSong", isPlaying ? (!string.IsNullOrEmpty(playingSingerName) ? $"{playingSongName} - {playingSingerName}" : playingSongName) : "" }
						}
					},
					{ "playlist", playlist }
				};
				return JsonConvert.SerializeObject(result);
			}

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
				mainWindow?.SkipSong_Click(null!, null!); // 與主畫面切歌鈕相同 (含解除單曲循環)
			}));

			return JsonConvert.SerializeObject(new { success = true, message = Loc.T("Web.Msg.SkipSent") });
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
				mainWindow?.SwitchToVocal();
			}));

			return JsonConvert.SerializeObject(new { success = true, message = Loc.T("Web.Msg.Vocal") });
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
				mainWindow?.SwitchToMusic();
			}));

			return JsonConvert.SerializeObject(new { success = true, message = Loc.T("Web.Msg.Music") });
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
                return JsonConvert.SerializeObject(new { success = true, message = Loc.T("Web.Msg.MovedToFirst") });
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
                return JsonConvert.SerializeObject(new { success = true, message = Loc.T("Web.Msg.Removed") });
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
                return JsonConvert.SerializeObject(new { success = true, message = Loc.T("Web.Msg.MovedNext") });
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
				foreach (DataColumn col in dataTable.Columns)
				{
					row[col] = song.GetColumnValue(col.ColumnName) ?? DBNull.Value;
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
	
					var newSongs = SongQuery.NewSongs(SongDatas.SongData, lang, CurrentQueryOptions());
					return SongPageToJson(newSongs, page, rows, 50, new[] { "Song_Id", "Song_SongName", "Song_Singer", "Song_Lang", "Song_CreatDate" });
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
	            return JsonConvert.SerializeObject(new { success = true, message = Loc.T("Web.Msg.VolUp") });
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
	            return JsonConvert.SerializeObject(new { success = true, message = Loc.T("Web.Msg.VolDown") });
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
	            return JsonConvert.SerializeObject(new { success = true, message = isLocking ? Loc.T("Web.Msg.VolLocked") : Loc.T("Web.Msg.VolUnlocked") });
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
	            return JsonConvert.SerializeObject(new { success = true, message = Loc.T("Web.Msg.PitchUp") });
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
	            return JsonConvert.SerializeObject(new { success = true, message = Loc.T("Web.Msg.PitchDown") });
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
	            return JsonConvert.SerializeObject(new { success = true, message = Loc.T("Web.Msg.PitchReset") });
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
	            System.Windows.Application.Current?.Dispatcher.BeginInvoke((Action)(async () =>
	            {
	                // Stop recording and wait for the MP3 off the UI thread: NAudio's RecordingStopped
	                // dispatches back to the UI thread, so waiting in App_Exit would deadlock until timeout
	                await Task.Run(() =>
	                {
	                    try
	                    {
	                        RecordingManager.Instance.StopRecording();
	                        RecordingManager.Instance.WaitForRecordingToFinish();
	                    }
	                    catch (Exception ex)
	                    {
	                        AppLogger.LogError("Error stopping recording before quit", ex);
	                    }
	                });
	                System.Windows.Application.Current?.Shutdown();
	            }));
	            return JsonConvert.SerializeObject(new { success = true, message = Loc.T("Web.Msg.Quitting") });
	        }
	        catch (Exception ex)
	        {
	            return ErrorResult("QuitApp Error", ex.Message);
	        }
	    }

	    public static string ShutdownWin(string value, object mainDataContext)
	    {
	        try
	        {
	            System.Windows.Application.Current?.Dispatcher.BeginInvoke((Action)(async () =>
	            {
	                await Task.Run(() =>
	                {
	                    try
	                    {
	                        RecordingManager.Instance.StopRecording();
	                        RecordingManager.Instance.WaitForRecordingToFinish();
	                    }
	                    catch (Exception ex)
	                    {
	                        AppLogger.LogError("Error stopping recording before shutdown", ex);
	                    }
	                });
	                System.Windows.Application.Current?.Shutdown();
	                System.Diagnostics.Process.Start("shutdown", "/s /t 0");
	            }));
	            return JsonConvert.SerializeObject(new { success = true, message = Loc.T("Web.Msg.ShuttingDown") });
	        }
	        catch (Exception ex)
	        {
	            return ErrorResult("ShutdownWin Error", ex.Message);
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
	                Song_Lang = v.Duration.HasValue ? UltimateKtv.SongDisplayItem.FormatDuration(v.Duration.Value.TotalSeconds) : "",
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
	                Language = "", // YouTube songs have no language; duration is stored as Length after download
	                FilePath = video.Url,
	                IsYoutube = true,
	                Volume = 30,
	                OrderedBy = !string.IsNullOrEmpty(userName) ? userName : Loc.OrderedByWeb,
	                AudioTrack = 0
	            };

	            bool downloadQueued = false;
	            bool alreadyQueued = false;
	            bool isDownloaded = false;
	            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
	            {
	                try
	                {
	                    var mainWindow = System.Windows.Application.Current.MainWindow as UltimateKtv.MainWindow;
	                    if (mainWindow != null)
	                    {
                            // Check if file already exists in cache directory
                            string cacheDir = SettingsManager.Instance.CurrentSettings.YoutubeDownloadDir;
                            if (!Path.IsPathRooted(cacheDir)) cacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, cacheDir);
                            string vId = YtDlpHelper.ResolveVideoId(songItem.YoutubeId, songItem.SongId);
                            string safeName = string.Join("_", songItem.SongName.Split(Path.GetInvalidFileNameChars()));
                            string filePath = Path.Combine(cacheDir, $"{safeName}_{vId}.mp4");

                            if (File.Exists(filePath))
                            {
                                isDownloaded = true;
                            }

                            alreadyQueued = !mainWindow.DownloadYoutubeVideo(songItem);
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
                    return JsonConvert.SerializeObject(new { success = false, message = Loc.T("Web.Msg.QueueFailed") });
                }
                if (alreadyQueued)
                {
                    return JsonConvert.SerializeObject(new { success = false, message = Loc.T("Web.AlreadyInPlaylist", songItem.SongName) });
                }

	            return JsonConvert.SerializeObject(new { success = true, message = Loc.T("Web.Msg.OrderOk"), isDownloaded = isDownloaded });
	        }
	        catch (Exception ex)
	        {
	            System.Diagnostics.Debug.WriteLine($"[OrderYoutubeAsync] Exception: {ex.Message}");
	            return ErrorResult("Order Error", ex.Message);
	        }
	    }

	    public static string OrderYoutubeHistory(string id, string userName, object mainDataContext)
	    {
	        try
	        {
	            var item = SongDatas.GetYoutubeHistoryById(id);
	            if (item == null) return ErrorResult("Not Found", Loc.T("Web.Msg.HistoryNotFound"));

	            string url = item.TryGetValue("Url", out var u) ? u?.ToString() ?? "" : "";
	            string name = item.TryGetValue("Name", out var n) ? n?.ToString() ?? "" : "";
	            int lengthSeconds = item.TryGetValue("Length", out var lenObj) && int.TryParse(lenObj?.ToString(), out int l) ? l : 0;
	            string downloadDir = SettingsManager.Instance.CurrentSettings.YoutubeDownloadDir ?? "";
	            string rawPath = item.TryGetValue("FileName", out var fn) && fn != null
	                           ? System.IO.Path.Combine(downloadDir, fn.ToString() ?? "") : "";

	            if (!string.IsNullOrEmpty(rawPath) && !System.IO.Path.IsPathRooted(rawPath))
	            {
	                rawPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, rawPath);
	            }

	            var songItem = new UltimateKtv.SongDisplayItem
	            {
	                SongId = string.IsNullOrEmpty(url) ? id : url,
	                SongName = name,
	                SingerName = lengthSeconds > 0 ? TimeSpan.FromSeconds(lengthSeconds).ToString(@"hh\:mm\:ss") : Loc.T("Web.UnknownLength"),
	                Language = item.TryGetValue("Lang", out var lang) ? lang?.ToString() ?? "" : "",
	                FilePath = rawPath,
	                IsYoutube = true,
	                YoutubeId = url,
	                Volume = item.TryGetValue("Volume", out var v) && int.TryParse(v?.ToString(), out int iv) ? iv : 30,
	                OrderedBy = !string.IsNullOrEmpty(userName) ? userName : Loc.OrderedByWeb,
	                AudioTrack = 0
	            };

	            bool fileExists = System.IO.File.Exists(rawPath);
	            if (!fileExists && string.IsNullOrEmpty(url))
	            {
	                return ErrorResult("File Not Found", Loc.T("Web.Msg.NoFileNoLink"));
	            }

	            bool alreadyQueued = false;
	            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
	            {
	                if (System.Windows.Application.Current.MainWindow is UltimateKtv.MainWindow mainWindow)
	                {
	                    alreadyQueued = !mainWindow.DownloadYoutubeVideo(songItem);
	                }
	            });

	            if (alreadyQueued)
	            {
	                return JsonConvert.SerializeObject(new { success = false, message = Loc.T("Web.AlreadyInPlaylist", name) });
	            }

	            return JsonConvert.SerializeObject(new { success = true, isDownloaded = fileExists, message = Loc.T("Web.Msg.OrderOk") });
	        }
	        catch (Exception ex)
	        {
	            System.Diagnostics.Debug.WriteLine($"[OrderYoutubeHistory] Exception: {ex.Message}");
	            return ErrorResult("Order Error", ex.Message);
	        }
	    }

        public static async Task<string> CheckNewYoutubeAsync()
        {
            try
            {
                int count = 0;
                if (System.Windows.Application.Current != null)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (System.Windows.Application.Current.MainWindow is UltimateKtv.MainWindow mainWindow)
                        {
                            var files = mainWindow.GetUnrecordedYoutubeFiles();
                            count = files?.Count ?? 0;
                        }
                    });
                }
                return JsonConvert.SerializeObject(new { count = count });
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new { count = 0, error = ex.Message });
            }
        }

        public static async Task<string> ImportNewYoutubeAsync()
        {
            try
            {
                if (System.Windows.Application.Current != null)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                    {
                        if (System.Windows.Application.Current.MainWindow is UltimateKtv.MainWindow mainWindow)
                        {
                            var files = mainWindow.GetUnrecordedYoutubeFiles();
                            if (files != null && files.Count > 0)
                            {
                                await mainWindow.ImportUnrecordedYoutubeFilesAsync(files);
                            }
                        }
                    }).Task.Unwrap();
                }
                return JsonConvert.SerializeObject(new { success = true });
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new { success = false, error = ex.Message });
            }
        }

		public static string QueryYoutubeHistory(string page, string rows)
		{
			try
			{
				var history = SongDatas.GetYoutubeHistory();
				if (history == null || history.Count == 0)
					return EmptyResult;

				if (string.IsNullOrEmpty(rows) || !IsNumeric(rows))
					return JsonConvert.SerializeObject(history);

				int pageIndex = (string.IsNullOrEmpty(page) || !IsNumeric(page)) ? 1 : Convert.ToInt32(page) + 1;
				int pageSize = Convert.ToInt32(rows);
				var pagedHistory = history.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToList();

				return JsonConvert.SerializeObject(pagedHistory);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[QueryYoutubeHistory] Exception: {ex.Message}");
				return ErrorResult("Query Error", ex.Message);
			}
		}

	    #endregion
	}
