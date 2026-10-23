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

	public static string QuerySinger(string condition, string page, string rows, string sort)
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

	public static string FavoriteUser(string page, string rows)
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

	/// <summary>手機選我的最愛使用者：回傳這位使用者是否存在 (伺服器不記住登入，每次查詢都帶使用者)。</summary>
	public static string FavoriteLogin(string user) =>
		JsonConvert.SerializeObject(new { success = IsFavoriteUser(user) });

	private static bool IsFavoriteUser(string user) =>
		!string.IsNullOrEmpty(user) && SongDatas.FavoriteUserData != null &&
		SongDatas.FavoriteUserData.AsEnumerable().Any(row => Convert.ToString(row["User_Id"]) == user);

	public static string FavoriteSong(string user, string page, string rows)
	{
		if (!IsFavoriteUser(user) || SongDatas.FavoriteSongData == null || SongDatas.FavoriteSongData.Count == 0)
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

	public static string OrderSong(string value, string userName, RemoteControl remote)
	{
		if (string.IsNullOrEmpty(value) || SongDatas.SongData == null)
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
			var result = remote.Order(songItem);
			System.Diagnostics.Debug.WriteLine($"[OrderSong] {songItem.SongName}: {result}");

			return result switch
			{
				EnqueueResult.Added or EnqueueResult.MovedToFirst =>
					JsonConvert.SerializeObject(new { success = true, message = Loc.T("Web.Msg.OrderOk"), songId = value }),
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

	public static string QueryPlayerState(RemoteControl remote)
	{
		try
		{
			var (now, playlistCount) = remote.GetPlayerState();
			var state = new JObject
			{
				{ "IsPlaying", now.IsPlaying },
				{ "SongName", now.SongName },
				{ "SingerName", now.SingerName },
				{ "SongId", now.SongId },
				{ "CurrentSong", CurrentSongText(now) },
				{ "OrderedBy", now.OrderedBy },
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

	/// <summary>「歌名 - 歌手」(沒有歌手時只有歌名)；沒有歌在播放時為空字串。</summary>
	private static string CurrentSongText(RemoteNowPlaying now) =>
		!now.IsPlaying ? "" : !string.IsNullOrEmpty(now.SingerName) ? $"{now.SongName} - {now.SingerName}" : now.SongName;

	public static string QueryYoutubeQueue(RemoteControl remote)
	{
		try
		{
			return JsonConvert.SerializeObject(remote.GetYoutubeQueueStatus());
		}
		catch
		{
			return JsonConvert.SerializeObject(new { count = 0, isDownloading = false });
		}
	}

	public static string QueryHostServerInfo()
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

	public static string QueryPlaylist(string value, RemoteControl remote)
	{
		try
		{
			var (now, waitingList) = remote.GetWaitingList();
			var playlist = new JArray();
			foreach (var item in waitingList)
			{
				if (string.IsNullOrEmpty(item.WaitingListSongName)) continue;
				playlist.Add(new JObject
				{
					{ "SongName", item.WaitingListSongName },
					{ "SingerName", item.WaitingListSingerName },
					{ "SongId", item.SongId ?? "" },
					{ "OrderedBy", item.OrderedBy ?? "" },
					{ "Id", item.Id.ToString() }
				});
			}

			if (string.Equals(value, "withCurrent", StringComparison.OrdinalIgnoreCase) ||
			    string.Equals(value, "includePlaying", StringComparison.OrdinalIgnoreCase))
			{
				var result = new JObject
				{
					{ "nowPlaying", new JObject
						{
							{ "isPlaying", now.IsPlaying },
							{ "songName", now.SongName },
							{ "singerName", now.SingerName },
							{ "songId", now.SongId },
							{ "orderedBy", now.OrderedBy },
							{ "currentSong", CurrentSongText(now) }
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

	/// <summary>與主畫面切歌鈕相同 (含解除暫停與單曲循環)。</summary>
	public static string SkipCurrentSong(RemoteControl remote)
	{
		try
		{
			remote.Skip();
			return JsonConvert.SerializeObject(new { success = true, message = Loc.T("Web.Msg.SkipSent") });
		}
		catch (Exception ex)
		{
			return ErrorResult("Skip Error", ex.Message);
		}
	}

	public static string SwitchToVocal(RemoteControl remote)
	{
		try
		{
			remote.SwitchToVocal();
			return JsonConvert.SerializeObject(new { success = true, message = Loc.T("Web.Msg.Vocal") });
		}
		catch (Exception ex)
		{
			return ErrorResult("Vocal Switch Error", ex.Message);
		}
	}

	public static string SwitchToMusic(RemoteControl remote)
	{
		try
		{
			remote.SwitchToMusic();
			return JsonConvert.SerializeObject(new { success = true, message = Loc.T("Web.Msg.Music") });
		}
		catch (Exception ex)
		{
			return ErrorResult("Music Switch Error", ex.Message);
		}
	}

	public static string MoveToFirst(string id, RemoteControl remote)
	{
		try
		{
			if (!Guid.TryParse(id, out Guid guid))
				return ErrorResult("Invalid ID", "Provided ID is not a valid GUID");
			remote.MoveToFirst(guid);
			return JsonConvert.SerializeObject(new { success = true, message = Loc.T("Web.Msg.MovedToFirst") });
		}
		catch (Exception ex)
		{
			return ErrorResult("Error", ex.Message);
		}
	}

	public static string RemoveFromList(string id, RemoteControl remote)
	{
		try
		{
			if (!Guid.TryParse(id, out Guid guid))
				return ErrorResult("Invalid ID", "Provided ID is not a valid GUID");
			remote.Remove(guid);
			return JsonConvert.SerializeObject(new { success = true, message = Loc.T("Web.Msg.Removed") });
		}
		catch (Exception ex)
		{
			return ErrorResult("Error", ex.Message);
		}
	}

	public static string MoveToNext(string id, RemoteControl remote)
	{
		try
		{
			if (!Guid.TryParse(id, out Guid guid))
				return ErrorResult("Invalid ID", "Provided ID is not a valid GUID");
			remote.MoveToNext(guid);
			return JsonConvert.SerializeObject(new { success = true, message = Loc.T("Web.Msg.MovedNext") });
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
	
	public static string VolumeUp(RemoteControl remote)
	{
		try
		{
			remote.VolumeUp();
			return JsonConvert.SerializeObject(new { success = true, message = Loc.T("Web.Msg.VolUp") });
		}
		catch (Exception ex)
		{
			return ErrorResult("VolumeUp Error", ex.Message);
		}
	}

	public static string VolumeDown(RemoteControl remote)
	{
		try
		{
			remote.VolumeDown();
			return JsonConvert.SerializeObject(new { success = true, message = Loc.T("Web.Msg.VolDown") });
		}
		catch (Exception ex)
		{
			return ErrorResult("VolumeDown Error", ex.Message);
		}
	}

	public static string ToggleVolumeLock(RemoteControl remote)
	{
		try
		{
			bool isLocking = remote.ToggleVolumeLock();
			return JsonConvert.SerializeObject(new { success = true, message = isLocking ? Loc.T("Web.Msg.VolLocked") : Loc.T("Web.Msg.VolUnlocked") });
		}
		catch (Exception ex)
		{
			return ErrorResult("ToggleVolumeLock Error", ex.Message);
		}
	}

	public static string PitchUp(RemoteControl remote)
	{
		try
		{
			remote.PitchUp();
			return JsonConvert.SerializeObject(new { success = true, message = Loc.T("Web.Msg.PitchUp") });
		}
		catch (Exception ex)
		{
			return ErrorResult("PitchUp Error", ex.Message);
		}
	}

	public static string PitchDown(RemoteControl remote)
	{
		try
		{
			remote.PitchDown();
			return JsonConvert.SerializeObject(new { success = true, message = Loc.T("Web.Msg.PitchDown") });
		}
		catch (Exception ex)
		{
			return ErrorResult("PitchDown Error", ex.Message);
		}
	}

	public static string PitchReset(RemoteControl remote)
	{
		try
		{
			remote.PitchReset();
			return JsonConvert.SerializeObject(new { success = true, message = Loc.T("Web.Msg.PitchReset") });
		}
		catch (Exception ex)
		{
			return ErrorResult("PitchReset Error", ex.Message);
		}
	}

	public static string QuitApp(RemoteControl remote)
	{
		try
		{
			remote.Quit(shutdownWindows: false);
			return JsonConvert.SerializeObject(new { success = true, message = Loc.T("Web.Msg.Quitting") });
		}
		catch (Exception ex)
		{
			return ErrorResult("QuitApp Error", ex.Message);
		}
	}

	public static string ShutdownWin(RemoteControl remote)
	{
		try
		{
			remote.Quit(shutdownWindows: true);
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

	    public static async Task<string> OrderYoutubeAsync(string videoId, string userName, RemoteControl remote)
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

	            // 下載快取裡已有這部影片時，手機顯示「已下載」
	            string cacheDir = SettingsManager.Instance.CurrentSettings.YoutubeDownloadDir;
	            if (!Path.IsPathRooted(cacheDir)) cacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, cacheDir);
	            string vId = YtDlpHelper.ResolveVideoId(songItem.YoutubeId, songItem.SongId);
	            string safeName = string.Join("_", songItem.SongName.Split(Path.GetInvalidFileNameChars()));
	            bool isDownloaded = File.Exists(Path.Combine(cacheDir, $"{safeName}_{vId}.mp4"));

	            bool downloadQueued = false;
	            bool alreadyQueued = false;
	            try
	            {
	                alreadyQueued = !remote.QueueYoutube(songItem);
	                downloadQueued = true;
	                System.Diagnostics.Debug.WriteLine($"[OrderYoutubeAsync] Triggered download via UI thread: {songItem.SongName}");
	            }
	            catch (Exception ex)
	            {
	                System.Diagnostics.Debug.WriteLine($"[OrderYoutubeAsync] Failed to trigger download: {ex.Message}");
	            }

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

	    public static string OrderYoutubeHistory(string id, string userName, RemoteControl remote)
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

	            bool alreadyQueued = !remote.QueueYoutube(songItem);

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

	public static async Task<string> CheckNewYoutubeAsync(RemoteControl remote)
	{
		try
		{
			return JsonConvert.SerializeObject(new { count = await remote.CountUnrecordedYoutubeFilesAsync() });
		}
		catch (Exception ex)
		{
			return JsonConvert.SerializeObject(new { count = 0, error = ex.Message });
		}
	}

	public static async Task<string> ImportNewYoutubeAsync(RemoteControl remote)
	{
		try
		{
			await remote.ImportUnrecordedYoutubeFilesAsync();
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
